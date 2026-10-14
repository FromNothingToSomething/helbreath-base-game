using System.Collections.Generic;
using Mmorpg.Network;
using Server;
using Server.World;
using Server.Utils;
using Server.World.Game;

namespace Server.Helpers;

/// <summary>
/// Authoritative spell casting for a single map: cast-start/cancel fan-out, cast resolution (rectangle, cone, linear AoE, single-cell, and ground effects),
/// projectile-delayed damage scheduling where applicable, and deferred damage delivery using combat rules.
/// </summary>
public static class Casting {
    /// <summary>Client <c>EFFECT_INVISIBILITY</c> key for <see cref="CastEffect"/>.</summary>
    private const string InvisibilityCastEffectKey = "invisibility";

    /// <summary>Client <c>EFFECT_BERSERK</c> key for <see cref="CastEffect"/>.</summary>
    private const string BerserkCastEffectKey = "berserk";

    /// <summary>Authoritative spell-start selection: remembers the requested spell id and fans out cast-start visuals to nearby players.</summary>
    public static void HandleSpellCastStartRequest(
        GameWorldRef wr,
        IReadOnlyDictionary<int, SpellConfig> spellsById,
        GameWorldPlayer player,
        SpellCastStartRequest request) {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(spellsById);

        if (player.IsDead) {
            return;
        }
        if (!spellsById.TryGetValue(request.SpellId, out var spell)) {
            return;
        }

        if (player.SpawnProtection) {
            Spawn.DisableSpawnProtectionAndNotify(wr, player);
        }

        TemporaryEffects.BreakInvisibilityIfPresent(wr, player);

        player.SetRequestedSpellId(request.SpellId);
        player.RecordSpellCastStartTimeMs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var startedMessage = NetworkManager.CreateSpellCastStarted(player.PlayerId, spell.Name, player.CastSpeedMs);
        foreach (var nearbyPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(player.PosX, player.PosY, player.SessionId)) {
            NetworkManager.SendToPlayer(nearbyPlayer, startedMessage);
        }
    }

    /// <summary>Cancels the current requested spell, if any, and fans out cast-cancel visuals to nearby players.</summary>
    public static void HandleSpellCastCancelRequest(GameWorldRef wr, GameWorldPlayer player) {
        ArgumentNullException.ThrowIfNull(player);

        if (!player.RequestedSpellId.HasValue) {
            return;
        }

        player.ClearRequestedSpell();
        var cancelledMessage = NetworkManager.CreateSpellCastCancelled(player.PlayerId);
        foreach (var nearbyPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(player.PosX, player.PosY, player.SessionId)) {
            NetworkManager.SendToPlayer(nearbyPlayer, cancelledMessage);
        }
    }

    /// <summary>Resolves the currently requested spell and broadcasts the authoritative cast event to nearby players and the caster.</summary>
    public static void HandleSpellCastRequest(
        GameWorldRef wr,
        string worldIdForLogging,
        IReadOnlyDictionary<int, SpellConfig> spellsById,
        GameWorldPlayer player,
        SpellCastRequest request) {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(spellsById);

        if (player.IsDead) {
            return;
        }

        if (!player.RequestedSpellId.HasValue) {
            Console.WriteLine(
                $"[GameWorld:{worldIdForLogging}] Spell cast request without pending spell: player {player.PlayerId} target=({request.X},{request.Y}) (violation).");
            NetworkManager.SendToPlayer(player, NetworkManager.CreateSpellCastFailed());
            return;
        }
        if (!spellsById.TryGetValue(player.RequestedSpellId.Value, out var spell)) {
            player.ClearRequestedSpell();
            return;
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (player.IsSpellCastTimingViolation(nowMs, out var minIntervalMs, out var actualElapsedSinceStartMs)) {
            var actualPart = actualElapsedSinceStartMs is double elapsed
                ? $"actualElapsedMs={elapsed:0.##}"
                : "actualElapsedMs=n/a (no cast start)";
            Console.WriteLine(
                $"[GameWorld:{worldIdForLogging}] Spell cast too quick: player {player.PlayerId} ({actualPart}, minIntervalMs={minIntervalMs:0.##}, pingVariance={player.PingVariance:0.##}, cappedPingVarianceMs={player.GetCappedPingVariance():0.##}, castSpeedMs={player.CastSpeedMs}).");
            NetworkManager.SendToPlayer(player, NetworkManager.CreateSpellCastFailed());
            player.ClearRequestedSpell();
            return;
        }

        var targetX = request.X;
        var targetY = request.Y;
        TryApplySpellAimAssist(wr, player, spell, request, ref targetX, ref targetY);

        var settings = wr.Settings;
        if (Math.Abs(targetX - player.PosX) > settings.Radius.CameraRadiusX || Math.Abs(targetY - player.PosY) > settings.Radius.CameraRadiusY) {
            Console.WriteLine(
                $"[GameWorld:{worldIdForLogging}] Spell cast target out of camera range: player {player.PlayerId} target=({targetX},{targetY}) pos=({player.PosX},{player.PosY}) cameraRadius=({settings.Radius.CameraRadiusX},{settings.Radius.CameraRadiusY}).");
            NetworkManager.SendToPlayer(player, NetworkManager.CreateSpellCastFailed());
            player.ClearRequestedSpell();
            return;
        }

        player.ClearRequestedSpell();
        TemporaryEffects.BreakInvisibilityIfPresent(wr, player);

        if (!spell.DamageType.HasValue) {
            var buffCastMessage = NetworkManager.CreateCastDirectionalAoeSpell(
                player.PlayerId,
                spell.Id,
                player.PosX,
                player.PosY,
                targetX,
                targetY);
            NetworkManager.SendToPlayer(player, buffCastMessage);
            foreach (var nearbyPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(player.PosX, player.PosY, player.SessionId)) {
                NetworkManager.SendToPlayer(nearbyPlayer, buffCastMessage);
            }

            if (spell.TemporaryEffects is not null) {
                foreach (var row in spell.TemporaryEffects) {
                    string? effectKey = row.Type switch {
                        (int)TemporaryEffectType.Invisibility => InvisibilityCastEffectKey,
                        (int)TemporaryEffectType.Berserk => BerserkCastEffectKey,
                        _ => null,
                    };
                    if (effectKey is null) {
                        continue;
                    }

                    var castEffectMessage = NetworkManager.CreateCastEffect(wr.WorldId, effectKey, targetX, targetY);
                    NetworkManager.SendToPlayer(player, castEffectMessage);
                    foreach (var nearbyPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(player.PosX, player.PosY, player.SessionId)) {
                        NetworkManager.SendToPlayer(nearbyPlayer, castEffectMessage);
                    }
                }
            }

            TemporaryEffects.ResolveBuffSpellAtCell(wr, player, spell, targetX, targetY);
            return;
        }

        switch (spell.DamageType!.Value) {
            case (int)DamageType.RectangleAoe: {
                    var aoeRadius = Math.Max(0, spell.AoeRadius ?? 0);
                    var castAoeSpellMessage = NetworkManager.CreateCastAoeSpell(
                        player.PlayerId,
                        spell.Id,
                        targetX,
                        targetY);
                    NetworkManager.SendToPlayer(player, castAoeSpellMessage);
                    foreach (var nearbyPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(player.PosX, player.PosY, player.SessionId)) {
                        NetworkManager.SendToPlayer(nearbyPlayer, castAoeSpellMessage);
                    }

                    if (spell.ProjectileSpeed is int projectileSpeedPxPerSec) {
                        var delayMs = spell.ProjectileDistance is int fixedDistancePx
                            ? Projectile.ComputeTravelTimeFromPixelDistance(fixedDistancePx, projectileSpeedPxPerSec)
                            : Projectile.ComputeTravelTime(player.PosX, player.PosY, targetX, targetY, projectileSpeedPxPerSec);
                        var casterId = player.PlayerId;
                        var targetPlayerIds = new List<long>();
                        foreach (var targetPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(targetX, targetY, excludeDisconnected: false)) {
                            if (targetPlayer.PlayerId == player.PlayerId) {
                                continue;
                            }
                            if (!IsWithinSpellDamageArea(targetPlayer.PosX, targetPlayer.PosY, targetX, targetY, aoeRadius)) {
                                continue;
                            }

                            targetPlayerIds.Add(targetPlayer.PlayerId);
                        }

                        var targetMonsterIds = new List<long>();
                        foreach (var targetMonster in wr.MonsterSpatialGrid.GetNearbyMonsters(targetX, targetY)) {
                            if (!IsWithinSpellDamageArea(targetMonster.PosX, targetMonster.PosY, targetX, targetY, aoeRadius)) {
                                continue;
                            }

                            targetMonsterIds.Add(targetMonster.MonsterId);
                        }

                        wr.Scheduler.SetTimeout(delayMs, () => DeliverDeferredSpellDamage(wr, casterId, targetPlayerIds, targetMonsterIds, ResolveSpellAttackType(spell), spell.Id));
                    } else {
                        ApplyRectangleSpellDamage(wr, player, targetX, targetY, aoeRadius, ResolveSpellAttackType(spell), spell);
                    }

                    break;
                }
            case (int)DamageType.ConeAoe: {
                    var castDirectionalAoeSpellMessage = NetworkManager.CreateCastDirectionalAoeSpell(
                        player.PlayerId,
                        spell.Id,
                        player.PosX,
                        player.PosY,
                        targetX,
                        targetY);
                    NetworkManager.SendToPlayer(player, castDirectionalAoeSpellMessage);
                    foreach (var nearbyPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(player.PosX, player.PosY, player.SessionId)) {
                        NetworkManager.SendToPlayer(nearbyPlayer, castDirectionalAoeSpellMessage);
                    }

                    if (TryCollectConeSpellDamageTargets(wr, player, targetX, targetY, spell, out var coneCasterId, out var coneTargetPlayerIds, out var coneTargetMonsterIds)) {
                        wr.Scheduler.SetTimeout(settings.Timings.BlizzardSpellDamageDelayMs, () => DeliverDeferredSpellDamage(wr, coneCasterId, coneTargetPlayerIds, coneTargetMonsterIds, ResolveSpellAttackType(spell), spell.Id));
                    }

                    break;
                }
            case (int)DamageType.LinearAoe: {
                    var linearCastMessage = NetworkManager.CreateCastDirectionalAoeSpell(
                        player.PlayerId,
                        spell.Id,
                        player.PosX,
                        player.PosY,
                        targetX,
                        targetY);
                    NetworkManager.SendToPlayer(player, linearCastMessage);
                    foreach (var nearbyPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(player.PosX, player.PosY, player.SessionId)) {
                        NetworkManager.SendToPlayer(nearbyPlayer, linearCastMessage);
                    }

                    if (TryCollectLinearAoeSpellDamageTargets(wr, player, targetX, targetY, spell, out var linearCasterId, out var linearTargetPlayerIds, out var linearTargetMonsterIds) &&
                        spell.Duration is int linearDurationMs) {
                        var delayMs = linearDurationMs / 2;
                        wr.Scheduler.SetTimeout(delayMs, () => DeliverDeferredSpellDamage(wr, linearCasterId, linearTargetPlayerIds, linearTargetMonsterIds, ResolveSpellAttackType(spell), spell.Id));
                    }

                    break;
                }
            case (int)DamageType.SingleCell: {
                    var singleCellCastMessage = NetworkManager.CreateCastDirectionalAoeSpell(
                        player.PlayerId,
                        spell.Id,
                        player.PosX,
                        player.PosY,
                        targetX,
                        targetY);
                    NetworkManager.SendToPlayer(player, singleCellCastMessage);
                    foreach (var nearbyPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(player.PosX, player.PosY, player.SessionId)) {
                        NetworkManager.SendToPlayer(nearbyPlayer, singleCellCastMessage);
                    }

                    ApplyRectangleSpellDamage(wr, player, targetX, targetY, 0, ResolveSpellAttackType(spell), spell);
                    break;
                }
            case (int)DamageType.GroundEffect:
                ApplyGroundEffectSpell(wr, player, targetX, targetY, spell);
                break;
        }
    }

    /// <summary>When the spell supports aim assist and the request includes a resolvable id, replaces the cast grid cell with that entity's position.</summary>
    private static void TryApplySpellAimAssist(
        GameWorldRef wr,
        GameWorldPlayer caster,
        SpellConfig spell,
        SpellCastRequest request,
        ref int targetX,
        ref int targetY) {
        if (spell.AimAssist != true) {
            return;
        }

        var allowSelfTarget = !spell.DamageType.HasValue;
        if (request.HasMonsterId) {
            if (wr.World.TryGetMonsterByMonsterId(request.MonsterId, out var monster) && !monster.Dead) {
                if (monster.HasTemporaryEffect(TemporaryEffectType.Invisibility) && !SpellAimAssist.AllowsInvisibleMonsterTarget(monster)) {
                    return;
                }

                targetX = monster.PosX;
                targetY = monster.PosY;
                return;
            }
        }

        if (request.HasPlayerId &&
            wr.World.TryGetConnectedPlayerById(request.PlayerId, out var targetPlayer) &&
            !targetPlayer.Disconnected &&
            !targetPlayer.IsDead &&
            (allowSelfTarget || targetPlayer.PlayerId != caster.PlayerId) &&
            !targetPlayer.SpawnProtection) {
            if (targetPlayer.HasTemporaryEffect(TemporaryEffectType.Invisibility) && !SpellAimAssist.AllowsInvisiblePlayerTarget(caster, targetPlayer)) {
                return;
            }

            targetX = targetPlayer.PosX;
            targetY = targetPlayer.PosY;
        }
    }

    /// <summary>
    /// Applies spell damage to recipients captured at cast time after the scheduled delay; skips the caster if invalid and targets that are disconnected, dead, or spawn-protected.
    /// </summary>
    private static void DeliverDeferredSpellDamage(
        GameWorldRef wr,
        long casterPlayerId,
        List<long> targetPlayerIds,
        List<long> targetMonsterIds,
        AttackType attackType,
        int spellId) {
        if (!wr.World.TryGetConnectedPlayerById(casterPlayerId, out var caster) || caster.Disconnected || caster.IsDead) {
            return;
        }

        if (!wr.SpellsById.TryGetValue(spellId, out var spell)) {
            return;
        }

        foreach (var playerId in targetPlayerIds) {
            if (!wr.World.TryGetConnectedPlayerById(playerId, out var targetPlayer) || targetPlayer.Disconnected || targetPlayer.IsDead || targetPlayer.SpawnProtection) {
                continue;
            }

            Combat.ApplyPlayerDamageToPlayer(wr, caster, targetPlayer, attackType);
            TemporaryEffects.ApplySpellTemporaryEffectsOnHit(wr, spell, targetPlayer);
        }

        foreach (var monsterId in targetMonsterIds) {
            if (!wr.World.TryGetMonsterByMonsterId(monsterId, out var targetMonster) || targetMonster.Dead) {
                continue;
            }

            Combat.ApplyPlayerDamageToMonster(wr, caster, targetMonster, attackType);
            TemporaryEffects.ApplySpellTemporaryEffectsOnHit(wr, spell, targetMonster);
        }
    }

    /// <summary>Applies spell damage to every living player and monster whose authoritative cell falls inside the target-centered Chebyshev square.</summary>
    private static void ApplyRectangleSpellDamage(
        GameWorldRef wr,
        GameWorldPlayer caster,
        int targetX,
        int targetY,
        int aoeRadius,
        AttackType attackType,
        SpellConfig spell) {
        foreach (var targetPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(targetX, targetY, excludeDisconnected: false)) {
            if (targetPlayer.PlayerId == caster.PlayerId) {
                continue;
            }
            if (!IsWithinSpellDamageArea(targetPlayer.PosX, targetPlayer.PosY, targetX, targetY, aoeRadius)) {
                continue;
            }

            Combat.ApplyPlayerDamageToPlayer(wr, caster, targetPlayer, attackType);
            TemporaryEffects.ApplySpellTemporaryEffectsOnHit(wr, spell, targetPlayer);
        }

        foreach (var targetMonster in wr.MonsterSpatialGrid.GetNearbyMonsters(targetX, targetY)) {
            if (!IsWithinSpellDamageArea(targetMonster.PosX, targetMonster.PosY, targetX, targetY, aoeRadius)) {
                continue;
            }

            Combat.ApplyPlayerDamageToMonster(wr, caster, targetMonster, attackType);
            TemporaryEffects.ApplySpellTemporaryEffectsOnHit(wr, spell, targetMonster);
        }
    }

    /// <summary>Spell hit mode from <see cref="SpellConfig.AttackType"/> (default <see cref="AttackType.Interrupt"/>). Ground-effect spells map <see cref="AttackType.Knockback"/> to <see cref="AttackType.Stun"/>.</summary>
    private static AttackType ResolveSpellAttackType(SpellConfig spell) {
        var raw = (AttackType)(spell.AttackType ?? (int)AttackType.Interrupt);
        if (spell.DamageType == (int)DamageType.GroundEffect && raw == AttackType.Knockback) {
            return AttackType.Stun;
        }

        return raw;
    }

    /// <summary>Builds cone-affected cells and records which players and monsters would take damage at this moment (used for deferred Blizzard delivery).</summary>
    private static bool TryCollectConeSpellDamageTargets(
        GameWorldRef wr,
        GameWorldPlayer caster,
        int targetX,
        int targetY,
        SpellConfig spell,
        out long casterPlayerId,
        out List<long> targetPlayerIds,
        out List<long> targetMonsterIds) {
        casterPlayerId = caster.PlayerId;
        targetPlayerIds = new List<long>();
        targetMonsterIds = new List<long>();
        if (!TryBuildConeSpellAffectedCells(wr, caster.PosX, caster.PosY, targetX, targetY, spell, out var minX, out var minY, out var maxX, out var maxY)) {
            return false;
        }

        foreach (var targetPlayer in wr.PlayerSpatialGrid.GetPlayersInRectangle(minX, minY, maxX, maxY, excludeDisconnected: false)) {
            if (targetPlayer.PlayerId == caster.PlayerId) {
                continue;
            }
            if (!wr.SpellAffectedCellsScratch.Contains((targetPlayer.PosX, targetPlayer.PosY))) {
                continue;
            }

            targetPlayerIds.Add(targetPlayer.PlayerId);
        }

        foreach (var targetMonster in wr.MonsterSpatialGrid.GetMonstersInRectangle(minX, minY, maxX, maxY)) {
            if (!wr.SpellAffectedCellsScratch.Contains((targetMonster.PosX, targetMonster.PosY))) {
                continue;
            }

            targetMonsterIds.Add(targetMonster.MonsterId);
        }

        return true;
    }

    /// <summary>True when a cell falls within the target-centered spell square defined by <paramref name="aoeRadius"/>.</summary>
    private static bool IsWithinSpellDamageArea(int cellX, int cellY, int targetX, int targetY, int aoeRadius) {
        return Location.GetDistance(cellX, cellY, targetX, targetY) <= aoeRadius;
    }

    /// <summary>Places one ground-effect instance on each covered cell and broadcasts only the successfully created effects.</summary>
    private static void ApplyGroundEffectSpell(GameWorldRef wr, GameWorldPlayer caster, int targetX, int targetY, SpellConfig spell) {
        if (spell.Group is not int group || spell.Duration is not int durationMs) {
            return;
        }

        var aoeRadius = Math.Max(0, spell.AoeRadius ?? 0);
        var tickRateMs = spell.TickRate;
        var resolvedAttackType = ResolveSpellAttackType(spell);
        var createdEffects = new List<GroundEffectState>();
        var minX = Math.Max(0, targetX - aoeRadius);
        var minY = Math.Max(0, targetY - aoeRadius);
        var maxX = Math.Min(wr.OccupancyTracker.SizeX - 1, targetX + aoeRadius);
        var maxY = Math.Min(wr.OccupancyTracker.SizeY - 1, targetY + aoeRadius);
        for (var cellY = minY; cellY <= maxY; cellY++) {
            for (var cellX = minX; cellX <= maxX; cellX++) {
                if (!IsWithinSpellDamageArea(cellX, cellY, targetX, targetY, aoeRadius)) {
                    continue;
                }

                if (!wr.GroundStateTracker.TryAddEffect(
                        spell.Id,
                        ResolveGroundEffectType(spell),
                        caster.PlayerId,
                        cellX,
                        cellY,
                        group,
                        tickRateMs,
                        durationMs,
                        caster.ComputeAttackDamage(),
                        resolvedAttackType,
                        out var createdEffect) ||
                    createdEffect is null) {
                    continue;
                }

                createdEffects.Add(createdEffect);
            }
        }

        if (createdEffects.Count > 0) {
            GroundStateVisibility.BroadcastGroundEffectsCreated(wr, createdEffects);
        }
    }

    /// <summary>Resolves the visual/gameplay ground-effect kind created by this spell.</summary>
    private static GroundEffectType ResolveGroundEffectType(SpellConfig spell) {
        return spell.Id switch {
            8 => GroundEffectType.Fire,
            4 => GroundEffectType.Poison,
            7 => GroundEffectType.SpikeField,
            9 => GroundEffectType.IceStorm,
            _ => throw new InvalidOperationException($"Unhandled ground-effect spell id {spell.Id} ({spell.Name})."),
        };
    }

    /// <summary>Builds the set of grid cells covered by the sampled expanding circles used for cone-style Blizzard visuals and returns the inclusive bounding box.</summary>
    private static bool TryBuildConeSpellAffectedCells(
        GameWorldRef wr,
        int casterX,
        int casterY,
        int targetX,
        int targetY,
        SpellConfig spell,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY) {
        wr.SpellAffectedCellsScratch.Clear();
        minX = 0;
        minY = 0;
        maxX = -1;
        maxY = -1;

        if (!spell.EmissionSteps.HasValue ||
            !spell.StartRadius.HasValue ||
            !spell.EndRadius.HasValue ||
            !spell.StartShards.HasValue ||
            !spell.EndShards.HasValue) {
            return false;
        }

        var emissionSteps = spell.EmissionSteps.Value;
        var startRadius = spell.StartRadius.Value;
        var endRadius = spell.EndRadius.Value;
        var startShards = spell.StartShards.Value;
        var endShards = spell.EndShards.Value;
        var mapMaxX = Math.Max(0, wr.OccupancyTracker.SizeX - 1);
        var mapMaxY = Math.Max(0, wr.OccupancyTracker.SizeY - 1);
        var hasAnyCell = false;

        for (var step = 0; step < emissionSteps; step++) {
            var progress = emissionSteps > 1 ? (double)step / (emissionSteps - 1) : 1d;
            var radius = (int)Math.Floor(startRadius + (endRadius - startRadius) * progress);
            var shardCount = (int)Math.Round(startShards + (endShards - startShards) * progress, MidpointRounding.AwayFromZero);
            if (shardCount <= 0) {
                continue;
            }

            var centerX = casterX + (targetX - casterX) * progress;
            var centerY = casterY + (targetY - casterY) * progress;
            var stepMinX = Math.Max(0, (int)Math.Floor(centerX - radius));
            var stepMaxX = Math.Min(mapMaxX, (int)Math.Ceiling(centerX + radius));
            var stepMinY = Math.Max(0, (int)Math.Floor(centerY - radius));
            var stepMaxY = Math.Min(mapMaxY, (int)Math.Ceiling(centerY + radius));
            var radiusSquared = radius * radius;

            for (var cellY = stepMinY; cellY <= stepMaxY; cellY++) {
                for (var cellX = stepMinX; cellX <= stepMaxX; cellX++) {
                    var dx = cellX - centerX;
                    var dy = cellY - centerY;
                    if ((dx * dx) + (dy * dy) > radiusSquared) {
                        continue;
                    }

                    wr.SpellAffectedCellsScratch.Add((cellX, cellY));
                    if (!hasAnyCell) {
                        minX = cellX;
                        minY = cellY;
                        maxX = cellX;
                        maxY = cellY;
                        hasAnyCell = true;
                    } else {
                        minX = Math.Min(minX, cellX);
                        minY = Math.Min(minY, cellY);
                        maxX = Math.Max(maxX, cellX);
                        maxY = Math.Max(maxY, cellY);
                    }
                }
            }
        }

        return hasAnyCell;
    }

    /// <summary>Builds the thickened line (orthogonal ±1 per Bresenham step), optional target-centered AoE, and records which players and monsters would take damage at cast time.</summary>
    private static bool TryCollectLinearAoeSpellDamageTargets(
        GameWorldRef wr,
        GameWorldPlayer caster,
        int targetX,
        int targetY,
        SpellConfig spell,
        out long casterPlayerId,
        out List<long> targetPlayerIds,
        out List<long> targetMonsterIds) {
        casterPlayerId = caster.PlayerId;
        targetPlayerIds = new List<long>();
        targetMonsterIds = new List<long>();
        if (!TryBuildLinearAoeAffectedCells(wr, caster.PosX, caster.PosY, targetX, targetY, spell, out var minX, out var minY, out var maxX, out var maxY)) {
            return false;
        }

        foreach (var targetPlayer in wr.PlayerSpatialGrid.GetPlayersInRectangle(minX, minY, maxX, maxY, excludeDisconnected: false)) {
            if (targetPlayer.PlayerId == caster.PlayerId) {
                continue;
            }
            if (!wr.SpellAffectedCellsScratch.Contains((targetPlayer.PosX, targetPlayer.PosY))) {
                continue;
            }

            targetPlayerIds.Add(targetPlayer.PlayerId);
        }

        foreach (var targetMonster in wr.MonsterSpatialGrid.GetMonstersInRectangle(minX, minY, maxX, maxY)) {
            if (!wr.SpellAffectedCellsScratch.Contains((targetMonster.PosX, targetMonster.PosY))) {
                continue;
            }

            targetMonsterIds.Add(targetMonster.MonsterId);
        }

        return true;
    }

    /// <summary>
    /// Fills <see cref="GameWorldRef.SpellAffectedCellsScratch"/> with: (1) a thickened Bresenham beam (center cell plus four orthogonals at each step),
    /// (2) when <paramref name="spell"/>.<see cref="SpellConfig.AoeRadius"/> is set, all cells within Chebyshev distance of the target cell (same rule as rectangle AoE).
    /// </summary>
    private static bool TryBuildLinearAoeAffectedCells(
        GameWorldRef wr,
        int casterX,
        int casterY,
        int targetX,
        int targetY,
        SpellConfig spell,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY) {
        wr.SpellAffectedCellsScratch.Clear();
        minX = 0;
        minY = 0;
        maxX = -1;
        maxY = -1;
        var mapMaxX = Math.Max(0, wr.OccupancyTracker.SizeX - 1);
        var mapMaxY = Math.Max(0, wr.OccupancyTracker.SizeY - 1);
        var hasAnyCell = false;

        var x0 = casterX;
        var y0 = casterY;
        var x1 = targetX;
        var y1 = targetY;
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;
        var x = x0;
        var y = y0;

        while (true) {
            AddLinearAoeOrthogonalPlus(wr, x, y, 0, 0, mapMaxX, mapMaxY, ref minX, ref minY, ref maxX, ref maxY, ref hasAnyCell);

            if (x == x1 && y == y1) {
                break;
            }

            var e2 = 2 * err;
            if (e2 >= -dy) {
                err -= dy;
                x += sx;
            }
            if (e2 <= dx) {
                err += dx;
                y += sy;
            }
        }

        if (spell.AoeRadius is int aoeRadius && aoeRadius >= 0) {
            for (var cy = targetY - aoeRadius; cy <= targetY + aoeRadius; cy++) {
                for (var cx = targetX - aoeRadius; cx <= targetX + aoeRadius; cx++) {
                    if (Location.GetDistance(cx, cy, targetX, targetY) > aoeRadius) {
                        continue;
                    }

                    TryAddLinearAoeCell(wr, cx, cy, 0, 0, mapMaxX, mapMaxY, ref minX, ref minY, ref maxX, ref maxY, ref hasAnyCell);
                }
            }
        }

        return hasAnyCell;
    }

    private static void AddLinearAoeOrthogonalPlus(
        GameWorldRef wr,
        int cx,
        int cy,
        int mapMinX,
        int mapMinY,
        int mapMaxX,
        int mapMaxY,
        ref int minX,
        ref int minY,
        ref int maxX,
        ref int maxY,
        ref bool hasAnyCell) {
        TryAddLinearAoeCell(wr, cx, cy, mapMinX, mapMinY, mapMaxX, mapMaxY, ref minX, ref minY, ref maxX, ref maxY, ref hasAnyCell);
        TryAddLinearAoeCell(wr, cx - 1, cy, mapMinX, mapMinY, mapMaxX, mapMaxY, ref minX, ref minY, ref maxX, ref maxY, ref hasAnyCell);
        TryAddLinearAoeCell(wr, cx + 1, cy, mapMinX, mapMinY, mapMaxX, mapMaxY, ref minX, ref minY, ref maxX, ref maxY, ref hasAnyCell);
        TryAddLinearAoeCell(wr, cx, cy - 1, mapMinX, mapMinY, mapMaxX, mapMaxY, ref minX, ref minY, ref maxX, ref maxY, ref hasAnyCell);
        TryAddLinearAoeCell(wr, cx, cy + 1, mapMinX, mapMinY, mapMaxX, mapMaxY, ref minX, ref minY, ref maxX, ref maxY, ref hasAnyCell);
    }

    private static void TryAddLinearAoeCell(
        GameWorldRef wr,
        int cellX,
        int cellY,
        int mapMinX,
        int mapMinY,
        int mapMaxX,
        int mapMaxY,
        ref int minX,
        ref int minY,
        ref int maxX,
        ref int maxY,
        ref bool hasAnyCell) {
        if (cellX < mapMinX || cellX > mapMaxX || cellY < mapMinY || cellY > mapMaxY) {
            return;
        }

        if (!wr.SpellAffectedCellsScratch.Add((cellX, cellY))) {
            return;
        }

        if (!hasAnyCell) {
            minX = maxX = cellX;
            minY = maxY = cellY;
            hasAnyCell = true;
        } else {
            minX = Math.Min(minX, cellX);
            minY = Math.Min(minY, cellY);
            maxX = Math.Max(maxX, cellX);
            maxY = Math.Max(maxY, cellY);
        }
    }

    /// <summary>Authoritative monster spell resolution: broadcasts monster cast packets to viewers and applies damage using <paramref name="damage"/> for all targets (same roll as player spell damage).</summary>
    public static void ApplyMonsterSpell(
        GameWorldRef wr,
        GameWorldMonster caster,
        SpellConfig spell,
        int targetX,
        int targetY,
        int damage) {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(spell);

        var settings = wr.Settings;
        if (Math.Abs(targetX - caster.PosX) > settings.Radius.CameraRadiusX || Math.Abs(targetY - caster.PosY) > settings.Radius.CameraRadiusY) {
            return;
        }

        var attackType = ResolveSpellAttackType(spell);
        switch (spell.DamageType!.Value) {
            case (int)DamageType.RectangleAoe: {
                    var aoeRadius = Math.Max(0, spell.AoeRadius ?? 0);
                    var castAoeSpellMessage = NetworkManager.CreateMonsterCastAoeSpell(
                        caster.MonsterId,
                        spell.Id,
                        targetX,
                        targetY);
                    foreach (var nearbyPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(caster.PosX, caster.PosY, null, excludeDisconnected: true)) {
                        NetworkManager.SendToPlayer(nearbyPlayer, castAoeSpellMessage);
                    }

                    if (spell.ProjectileSpeed is int projectileSpeedPxPerSec) {
                        var delayMs = spell.ProjectileDistance is int fixedDistancePx
                            ? Projectile.ComputeTravelTimeFromPixelDistance(fixedDistancePx, projectileSpeedPxPerSec)
                            : Projectile.ComputeTravelTime(caster.PosX, caster.PosY, targetX, targetY, projectileSpeedPxPerSec);
                        var casterMonsterId = caster.MonsterId;
                        var targetPlayerIds = new List<long>();
                        foreach (var targetPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(targetX, targetY, excludeDisconnected: false)) {
                            if (!IsWithinSpellDamageArea(targetPlayer.PosX, targetPlayer.PosY, targetX, targetY, aoeRadius)) {
                                continue;
                            }

                            targetPlayerIds.Add(targetPlayer.PlayerId);
                        }

                        var targetMonsterIds = new List<long>();
                        foreach (var targetMonster in wr.MonsterSpatialGrid.GetNearbyMonsters(targetX, targetY)) {
                            if (targetMonster.MonsterId == caster.MonsterId) {
                                continue;
                            }
                            if (!IsWithinSpellDamageArea(targetMonster.PosX, targetMonster.PosY, targetX, targetY, aoeRadius)) {
                                continue;
                            }

                            targetMonsterIds.Add(targetMonster.MonsterId);
                        }

                        wr.Scheduler.SetTimeout(delayMs, () => DeliverDeferredMonsterSpellDamage(wr, casterMonsterId, targetPlayerIds, targetMonsterIds, attackType, damage, spell.Id));
                    } else {
                        ApplyRectangleMonsterSpellDamage(wr, caster, targetX, targetY, aoeRadius, attackType, damage, spell);
                    }

                    break;
                }
            case (int)DamageType.ConeAoe: {
                    var castDirectionalAoeSpellMessage = NetworkManager.CreateMonsterCastDirectionalAoeSpell(
                        caster.MonsterId,
                        spell.Id,
                        caster.PosX,
                        caster.PosY,
                        targetX,
                        targetY);
                    foreach (var nearbyPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(caster.PosX, caster.PosY, null, excludeDisconnected: true)) {
                        NetworkManager.SendToPlayer(nearbyPlayer, castDirectionalAoeSpellMessage);
                    }

                    if (TryCollectConeSpellDamageTargetsMonster(wr, caster, targetX, targetY, spell, out var coneTargetPlayerIds, out var coneTargetMonsterIds)) {
                        wr.Scheduler.SetTimeout(settings.Timings.BlizzardSpellDamageDelayMs, () => DeliverDeferredMonsterSpellDamage(wr, caster.MonsterId, coneTargetPlayerIds, coneTargetMonsterIds, attackType, damage, spell.Id));
                    }

                    break;
                }
            case (int)DamageType.LinearAoe: {
                    var linearCastMessage = NetworkManager.CreateMonsterCastDirectionalAoeSpell(
                        caster.MonsterId,
                        spell.Id,
                        caster.PosX,
                        caster.PosY,
                        targetX,
                        targetY);
                    foreach (var nearbyPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(caster.PosX, caster.PosY, null, excludeDisconnected: true)) {
                        NetworkManager.SendToPlayer(nearbyPlayer, linearCastMessage);
                    }

                    if (TryCollectLinearAoeSpellDamageTargetsMonster(wr, caster, targetX, targetY, spell, out var linearTargetPlayerIds, out var linearTargetMonsterIds) &&
                        spell.Duration is int linearDurationMs) {
                        var delayMs = linearDurationMs / 2;
                        wr.Scheduler.SetTimeout(delayMs, () => DeliverDeferredMonsterSpellDamage(wr, caster.MonsterId, linearTargetPlayerIds, linearTargetMonsterIds, attackType, damage, spell.Id));
                    }

                    break;
                }
            case (int)DamageType.SingleCell: {
                    var singleCellCastMessage = NetworkManager.CreateMonsterCastDirectionalAoeSpell(
                        caster.MonsterId,
                        spell.Id,
                        caster.PosX,
                        caster.PosY,
                        targetX,
                        targetY);
                    foreach (var nearbyPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(caster.PosX, caster.PosY, null, excludeDisconnected: true)) {
                        NetworkManager.SendToPlayer(nearbyPlayer, singleCellCastMessage);
                    }

                    ApplyRectangleMonsterSpellDamage(wr, caster, targetX, targetY, 0, attackType, damage, spell);
                    break;
                }
            default:
                throw new InvalidOperationException($"Monster spell {spell.Id} ({spell.Name}) has unsupported damageType {spell.DamageType}.");
        }
    }

    private static void DeliverDeferredMonsterSpellDamage(
        GameWorldRef wr,
        long casterMonsterId,
        List<long> targetPlayerIds,
        List<long> targetMonsterIds,
        AttackType attackType,
        int damage,
        int spellId) {
        if (!wr.World.TryGetMonsterByMonsterId(casterMonsterId, out var caster) || caster.Dead) {
            return;
        }

        if (!wr.SpellsById.TryGetValue(spellId, out var spell)) {
            return;
        }

        foreach (var playerId in targetPlayerIds) {
            if (!wr.World.TryGetConnectedPlayerById(playerId, out var targetPlayer) || targetPlayer.Disconnected || targetPlayer.IsDead || targetPlayer.SpawnProtection) {
                continue;
            }

            Combat.ApplyMonsterSpellDamageToPlayer(wr, caster, targetPlayer, damage, attackType);
            TemporaryEffects.ApplySpellTemporaryEffectsOnHit(wr, spell, targetPlayer);
        }

        foreach (var monsterId in targetMonsterIds) {
            if (!wr.World.TryGetMonsterByMonsterId(monsterId, out var targetMonster) || targetMonster.Dead) {
                continue;
            }

            Combat.ApplyMonsterSpellDamageToMonster(wr, caster, targetMonster, damage, attackType);
            TemporaryEffects.ApplySpellTemporaryEffectsOnHit(wr, spell, targetMonster);
        }
    }

    private static void ApplyRectangleMonsterSpellDamage(
        GameWorldRef wr,
        GameWorldMonster caster,
        int targetX,
        int targetY,
        int aoeRadius,
        AttackType attackType,
        int damage,
        SpellConfig spell) {
        foreach (var targetPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(targetX, targetY, excludeDisconnected: false)) {
            if (!IsWithinSpellDamageArea(targetPlayer.PosX, targetPlayer.PosY, targetX, targetY, aoeRadius)) {
                continue;
            }

            Combat.ApplyMonsterSpellDamageToPlayer(wr, caster, targetPlayer, damage, attackType);
            TemporaryEffects.ApplySpellTemporaryEffectsOnHit(wr, spell, targetPlayer);
        }

        foreach (var targetMonster in wr.MonsterSpatialGrid.GetNearbyMonsters(targetX, targetY)) {
            if (targetMonster.MonsterId == caster.MonsterId) {
                continue;
            }
            if (!IsWithinSpellDamageArea(targetMonster.PosX, targetMonster.PosY, targetX, targetY, aoeRadius)) {
                continue;
            }

            Combat.ApplyMonsterSpellDamageToMonster(wr, caster, targetMonster, damage, attackType);
            TemporaryEffects.ApplySpellTemporaryEffectsOnHit(wr, spell, targetMonster);
        }
    }

    private static bool TryCollectConeSpellDamageTargetsMonster(
        GameWorldRef wr,
        GameWorldMonster caster,
        int targetX,
        int targetY,
        SpellConfig spell,
        out List<long> targetPlayerIds,
        out List<long> targetMonsterIds) {
        targetPlayerIds = new List<long>();
        targetMonsterIds = new List<long>();
        if (!TryBuildConeSpellAffectedCells(wr, caster.PosX, caster.PosY, targetX, targetY, spell, out var minX, out var minY, out var maxX, out var maxY)) {
            return false;
        }

        foreach (var targetPlayer in wr.PlayerSpatialGrid.GetPlayersInRectangle(minX, minY, maxX, maxY, excludeDisconnected: false)) {
            if (!wr.SpellAffectedCellsScratch.Contains((targetPlayer.PosX, targetPlayer.PosY))) {
                continue;
            }

            targetPlayerIds.Add(targetPlayer.PlayerId);
        }

        foreach (var targetMonster in wr.MonsterSpatialGrid.GetMonstersInRectangle(minX, minY, maxX, maxY)) {
            if (targetMonster.MonsterId == caster.MonsterId) {
                continue;
            }
            if (!wr.SpellAffectedCellsScratch.Contains((targetMonster.PosX, targetMonster.PosY))) {
                continue;
            }

            targetMonsterIds.Add(targetMonster.MonsterId);
        }

        return true;
    }

    private static bool TryCollectLinearAoeSpellDamageTargetsMonster(
        GameWorldRef wr,
        GameWorldMonster caster,
        int targetX,
        int targetY,
        SpellConfig spell,
        out List<long> targetPlayerIds,
        out List<long> targetMonsterIds) {
        targetPlayerIds = new List<long>();
        targetMonsterIds = new List<long>();
        if (!TryBuildLinearAoeAffectedCells(wr, caster.PosX, caster.PosY, targetX, targetY, spell, out var minX, out var minY, out var maxX, out var maxY)) {
            return false;
        }

        foreach (var targetPlayer in wr.PlayerSpatialGrid.GetPlayersInRectangle(minX, minY, maxX, maxY, excludeDisconnected: false)) {
            if (!wr.SpellAffectedCellsScratch.Contains((targetPlayer.PosX, targetPlayer.PosY))) {
                continue;
            }

            targetPlayerIds.Add(targetPlayer.PlayerId);
        }

        foreach (var targetMonster in wr.MonsterSpatialGrid.GetMonstersInRectangle(minX, minY, maxX, maxY)) {
            if (targetMonster.MonsterId == caster.MonsterId) {
                continue;
            }
            if (!wr.SpellAffectedCellsScratch.Contains((targetMonster.PosX, targetMonster.PosY))) {
                continue;
            }

            targetMonsterIds.Add(targetMonster.MonsterId);
        }

        return true;
    }
}
