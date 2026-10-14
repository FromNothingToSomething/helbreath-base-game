using Mmorpg.Network;
using Server;
using Server.World;
using Server.Utils;
using Server.World.Game;

namespace Server.Helpers;

/// <summary>
/// Authoritative combat resolution for a single map: player and monster hit validation (including lag-compensated delays),
/// damage and crowd-control application to monsters and players, and related visibility fan-out.
/// </summary>
public static class Combat {
    private readonly record struct MonsterAttackResolution(
        int HpAfter,
        AttackType PacketAttackType,
        int StunlockMs,
        int? KnockbackDurationMs,
        int? KnockbackFromX,
        int? KnockbackFromY,
        int? KnockbackDestX,
        int? KnockbackDestY);

    /// <summary>Applies lethal damage to every living monster on this map (debug/admin summon dialog) and fans out <see cref="MonsterTakeDamage"/> / <see cref="MonsterDied"/> like normal combat.</summary>
    public static void HandleKillAllMonstersRequested(GameWorldRef wr, GameWorldPlayer player) {
        ArgumentNullException.ThrowIfNull(player);

        if (player.IsDead) {
            return;
        }
        if (player.IsPickupOrBowStanceLockoutActive(DateTimeOffset.UtcNow)) {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var snapshot = new List<GameWorldMonster>(wr.MonstersByMonsterId.Count);
        foreach (var monster in wr.MonstersByMonsterId.Values) {
            snapshot.Add(monster);
        }

        foreach (var monster in snapshot) {
            if (monster.Dead) {
                continue;
            }

            var damage = monster.Hp;
            if (!monster.TryApplyAttackerHit(wr, damage, now, out var hpAfter)) {
                continue;
            }

            MonsterVisibility.BroadcastMonsterTakeDamage(
                wr,
                monster,
                damage,
                AttackType.NoInterrupt,
                hpAfter);
            if (hpAfter == 0) {
                MonsterVisibility.BroadcastMonsterDied(wr, monster);
            }
        }
    }

    /// <summary>Schedules authoritative player-versus-monster hit validation after lag compensation and fans out attack animation sync immediately.</summary>
    public static void HandlePlayerAttackedMonsterRequest(GameWorldRef wr, string worldIdForLogging, GameWorldPlayer player, PlayerAttackedMonsterRequest request) {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(request);

        if (player.IsDead || !player.AttackMode) {
            return;
        }
        if (player.IsPickupOrBowStanceLockoutActive(DateTimeOffset.UtcNow)) {
            return;
        }
        if (request.MonsterId == 0 || !player.IsMonsterInRange(request.MonsterId)) {
            return;
        }
        if (!wr.MonstersByMonsterId.TryGetValue(request.MonsterId, out var targetMonster) || targetMonster.Dead) {
            return;
        }

        if (targetMonster.HasTemporaryEffect(TemporaryEffectType.Invisibility)) {
            return;
        }

        if (player.SpawnProtection) {
            Spawn.DisableSpawnProtectionAndNotify(wr, player);
        }

        TemporaryEffects.BreakInvisibilityIfPresent(wr, player);

        var attackType = (AttackType)request.AttackType;
        var distanceNow = Location.GetDistance(player.PosX, player.PosY, targetMonster.PosX, targetMonster.PosY);
        if (distanceNow > player.AttackRange + 1) {
            Console.WriteLine(
                $"[GameWorld:{worldIdForLogging}] Warning: player {player.PlayerId}'s attack was rejected: target beyond allowed range (distance={distanceNow}, maxAllowed={player.AttackRange + 1}); skipping damage delivery.");
            return;
        }

        if (attackType == AttackType.Interrupt) {
            targetMonster.ClearPendingAttackDamageFromPlayerInterrupt();
        }

        BroadcastPlayerAttackVisual(
            wr,
            player,
            NetworkManager.CreatePlayerAttackedMonster(
                player.PlayerId,
                ResolvePlayerAttackDirection(player, targetMonster.PosX, targetMonster.PosY),
                player.AttackSpeedMs,
                request.RangedAttack,
                targetMonster.MonsterId,
                player.PosX,
                player.PosY,
                request.AttackType));

        var attackerSessionId = player.SessionId;
        var targetMonsterId = request.MonsterId;
        var capturedInterruptedCount = player.InterruptedCount;
        var delayMs = ComputePlayerAttackDelayMs(wr, player, request.RangedAttack, targetMonster.PosX, targetMonster.PosY);
        wr.Scheduler.SetTimeout(delayMs, () => {
            if (!wr.World.TryGetPlayerBySessionId(attackerSessionId, out var attacker) || attacker.Disconnected) {
                return;
            }
            if (attacker.InterruptedCount != capturedInterruptedCount) {
                return;
            }
            if (!wr.MonstersByMonsterId.TryGetValue(targetMonsterId, out var delayedTargetMonster) || delayedTargetMonster.Dead) {
                return;
            }
            if (delayedTargetMonster.HasTemporaryEffect(TemporaryEffectType.Invisibility)) {
                return;
            }
            if (!attacker.IsMonsterInRange(targetMonsterId)) {
                return;
            }
            if (Location.GetDistance(attacker.PosX, attacker.PosY, delayedTargetMonster.PosX, delayedTargetMonster.PosY) > attacker.AttackRange + 1) {
                return;
            }
            if (!TryRecordPlayerAttackDamageDelivery(worldIdForLogging, attacker)) {
                return;
            }

            ApplyPlayerAttackToMonster(wr, attacker, delayedTargetMonster, attackType);
        });
    }

    /// <summary>Schedules authoritative player-versus-player hit validation after lag compensation and fans out attack animation sync immediately.</summary>
    public static void HandlePlayerAttackedPlayerRequest(GameWorldRef wr, string worldIdForLogging, GameWorldPlayer player, PlayerAttackedPlayerRequest request) {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(request);

        if (player.IsDead || !player.AttackMode) {
            return;
        }
        if (player.IsPickupOrBowStanceLockoutActive(DateTimeOffset.UtcNow)) {
            return;
        }
        if (request.TargetPlayerId == 0 || request.TargetPlayerId == player.PlayerId || !player.IsPlayerInRange(request.TargetPlayerId)) {
            return;
        }
        if (!wr.World.TryGetConnectedPlayerById(request.TargetPlayerId, out var targetPlayer) || targetPlayer.IsDead) {
            return;
        }

        if (targetPlayer.SpawnProtection) {
            return;
        }

        if (targetPlayer.HasTemporaryEffect(TemporaryEffectType.Invisibility)) {
            return;
        }

        var attackType = (AttackType)request.AttackType;
        var distanceNow = Location.GetDistance(player.PosX, player.PosY, targetPlayer.PosX, targetPlayer.PosY);
        if (distanceNow > player.AttackRange + 1) {
            Console.WriteLine(
                $"[GameWorld:{worldIdForLogging}] Warning: player {player.PlayerId}'s PvP attack was rejected: target beyond allowed range (distance={distanceNow}, maxAllowed={player.AttackRange + 1}); skipping damage delivery.");
            return;
        }

        if (player.SpawnProtection) {
            Spawn.DisableSpawnProtectionAndNotify(wr, player);
        }

        TemporaryEffects.BreakInvisibilityIfPresent(wr, player);

        BroadcastPlayerAttackVisual(
            wr,
            player,
            NetworkManager.CreatePlayerAttackedPlayer(
                player.PlayerId,
                ResolvePlayerAttackDirection(player, targetPlayer.PosX, targetPlayer.PosY),
                player.AttackSpeedMs,
                request.RangedAttack,
                targetPlayer.PlayerId,
                player.PosX,
                player.PosY,
                request.AttackType));

        var attackerSessionId = player.SessionId;
        var targetPlayerId = request.TargetPlayerId;
        var capturedInterruptedCount = player.InterruptedCount;
        var delayMs = ComputePlayerAttackDelayMs(wr, player, request.RangedAttack, targetPlayer.PosX, targetPlayer.PosY);
        wr.Scheduler.SetTimeout(delayMs, () => {
            if (!wr.World.TryGetPlayerBySessionId(attackerSessionId, out var attacker) || attacker.Disconnected) {
                return;
            }
            if (attacker.InterruptedCount != capturedInterruptedCount) {
                return;
            }
            if (!wr.World.TryGetConnectedPlayerById(targetPlayerId, out var delayedTargetPlayer) || delayedTargetPlayer.IsDead) {
                return;
            }
            if (delayedTargetPlayer.SpawnProtection) {
                return;
            }
            if (delayedTargetPlayer.HasTemporaryEffect(TemporaryEffectType.Invisibility)) {
                return;
            }
            if (!attacker.IsPlayerInRange(targetPlayerId)) {
                return;
            }
            if (Location.GetDistance(attacker.PosX, attacker.PosY, delayedTargetPlayer.PosX, delayedTargetPlayer.PosY) > attacker.AttackRange + 1) {
                return;
            }
            if (!TryRecordPlayerAttackDamageDelivery(worldIdForLogging, attacker)) {
                return;
            }

            ApplyPlayerAttackToPlayer(wr, attacker, delayedTargetPlayer, attackType);
        });
    }

    /// <summary>After an accepted dash step, applies any immediate player combat hit that the movement packet encoded.</summary>
    public static void HandlePlayerDashAttackAfterMovement(GameWorldRef wr, GameWorldPlayer attacker, RequestMovement requestMovement) {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(requestMovement);

        if (!requestMovement.DashAttack || !requestMovement.HasAttackType || !attacker.AttackMode) {
            return;
        }

        var attackType = (AttackType)requestMovement.AttackType;
        if (requestMovement.HasMonsterId) {
            if (!wr.MonstersByMonsterId.TryGetValue(requestMovement.MonsterId, out var targetMonster) || targetMonster.Dead) {
                return;
            }
            if (targetMonster.HasTemporaryEffect(TemporaryEffectType.Invisibility)) {
                return;
            }
            if (Location.GetDistance(requestMovement.CurX, requestMovement.CurY, targetMonster.PosX, targetMonster.PosY) > attacker.AttackRange + 1) {
                return;
            }
            if (Location.GetDistance(attacker.PosX, attacker.PosY, targetMonster.PosX, targetMonster.PosY) > attacker.AttackRange + 1) {
                return;
            }
            if (attackType == AttackType.Interrupt) {
                targetMonster.ClearPendingAttackDamageFromPlayerInterrupt();
            }
            if (attacker.SpawnProtection) {
                Spawn.DisableSpawnProtectionAndNotify(wr, attacker);
            }

            TemporaryEffects.BreakInvisibilityIfPresent(wr, attacker);
            ApplyPlayerAttackToMonster(wr, attacker, targetMonster, attackType);
            attacker.ClearLastPlayerAttackDamageDeliveryTime();
            return;
        }

        if (!requestMovement.HasPlayerId || requestMovement.PlayerId == 0 || requestMovement.PlayerId == attacker.PlayerId) {
            return;
        }
        if (!wr.World.TryGetConnectedPlayerById(requestMovement.PlayerId, out var targetPlayer) || targetPlayer.IsDead) {
            return;
        }
        if (targetPlayer.SpawnProtection) {
            return;
        }
        if (targetPlayer.HasTemporaryEffect(TemporaryEffectType.Invisibility)) {
            return;
        }
        if (!attacker.IsPlayerInRange(requestMovement.PlayerId)) {
            return;
        }
        if (Location.GetDistance(requestMovement.CurX, requestMovement.CurY, targetPlayer.PosX, targetPlayer.PosY) > attacker.AttackRange + 1) {
            return;
        }
        if (Location.GetDistance(attacker.PosX, attacker.PosY, targetPlayer.PosX, targetPlayer.PosY) > attacker.AttackRange + 1) {
            return;
        }

        if (attacker.SpawnProtection) {
            Spawn.DisableSpawnProtectionAndNotify(wr, attacker);
        }

        TemporaryEffects.BreakInvisibilityIfPresent(wr, attacker);
        ApplyPlayerAttackToPlayer(wr, attacker, targetPlayer, attackType);
        attacker.ClearLastPlayerAttackDamageDeliveryTime();
    }

    public static void ApplyMonsterAttackToMonster(GameWorldRef wr, GameWorldMonster attacker, GameWorldMonster targetMonster, int damage) {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(targetMonster);

        if (!TryResolveMonsterAttack(
                wr,
                targetMonster,
                damage,
                attacker.AttackType,
                attacker.StunDurationMs,
                attacker.PosX,
                attacker.PosY,
                out var resolution)) {
            return;
        }

        if (resolution.HpAfter > 0) {
            targetMonster.SetAggroFromDamageMonsterAttacker(attacker.MonsterId, attacker.Allegiance);
        }

        MonsterVisibility.BroadcastMonsterTakeDamageByMonster(
            wr,
            targetMonster,
            damage,
            attacker.MonsterId,
            resolution.PacketAttackType,
            resolution.HpAfter,
            resolution.StunlockMs,
            resolution.KnockbackDurationMs,
            resolution.KnockbackDestX,
            resolution.KnockbackDestY,
            resolution.KnockbackFromX,
            resolution.KnockbackFromY);
        if (resolution.HpAfter == 0) {
            MonsterVisibility.BroadcastMonsterDied(wr, targetMonster);
        }
    }

    /// <summary>Applies authoritative player damage to a monster for non-melee sources (for example, server-resolved spells) while reusing normal damage and death fan-out.</summary>
    public static void ApplyPlayerDamageToMonster(GameWorldRef wr, GameWorldPlayer attacker, GameWorldMonster targetMonster, AttackType attackType) {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(targetMonster);

        ApplyPlayerAttackToMonster(wr, attacker, targetMonster, attackType);
    }

    /// <summary>Applies authoritative player damage to another player for non-melee sources (for example, server-resolved spells) while reusing normal damage, interrupt, and death fan-out.</summary>
    public static void ApplyPlayerDamageToPlayer(GameWorldRef wr, GameWorldPlayer attacker, GameWorldPlayer targetPlayer, AttackType attackType) {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(targetPlayer);

        if (targetPlayer.IsDead || targetPlayer.SpawnProtection) {
            return;
        }

        ApplyPlayerAttackToPlayer(wr, attacker, targetPlayer, attackType);
    }

    /// <summary>Applies spell damage from a monster to a player using explicit damage and spell hit mode; reuses monster melee knockback/stun rules with <paramref name="spellAttackType"/>.</summary>
    public static void ApplyMonsterSpellDamageToPlayer(
        GameWorldRef wr,
        GameWorldMonster caster,
        GameWorldPlayer target,
        int damage,
        AttackType spellAttackType) {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(target);

        if (damage <= 0 || target.IsDead || target.SpawnProtection) {
            return;
        }

        var px = target.PosX;
        var py = target.PosY;
        var attackTypeOut = spellAttackType;
        var stunPacketMs = 0;
        var knockbackDurMs = 0;
        var destKbX = -1;
        var destKbY = -1;

        var remainingStunlock = target.GetRemainingCombatStunlockMs(DateTimeOffset.UtcNow);
        if ((spellAttackType == AttackType.Stun || spellAttackType == AttackType.Knockback) && remainingStunlock > 0) {
            attackTypeOut = AttackType.Interrupt;
        } else if (spellAttackType == AttackType.Stun) {
            stunPacketMs = caster.StunDurationMs;
        } else if (spellAttackType == AttackType.Knockback) {
            stunPacketMs = caster.StunDurationMs;
            var dir = Location.GetNextGridDirection(caster.PosX, caster.PosY, px, py);
            if (dir < 0 || dir > 7) {
                attackTypeOut = AttackType.Stun;
            } else {
                Location.GetDirectionDelta(dir, out var kdx, out var kdy);
                var kx = px + kdx;
                var ky = py + kdy;
                if (wr.OccupancyTracker.IsFreeAndNotTeleportCell(kx, ky)) {
                    wr.OccupancyTracker.SetFree(px, py);
                    wr.OccupancyTracker.SetOccupied(kx, ky);
                    Movement.SetPlayerPosition(wr, target, kx, ky);
                    Movement.SyncPlayerVisibilityAfterMovement(wr, target, px, py, kx, ky, broadcastPlayerMoved: false);
                    ApplyGroundEffectStepDamageToPlayer(wr, target);
                    knockbackDurMs = wr.Settings.Timings.KnockbackTimeMs;
                    destKbX = kx;
                    destKbY = ky;
                } else {
                    attackTypeOut = AttackType.Stun;
                }
            }
        }

        MonsterVisibility.BroadcastPlayerReceiveDamage(
            wr,
            target.PlayerId,
            damage,
            caster.MonsterId,
            attackTypeOut,
            stunPacketMs,
            knockbackDurMs,
            destKbX,
            destKbY,
            knockbackDurMs > 0 ? px : null,
            knockbackDurMs > 0 ? py : null);
    }

    /// <summary>Applies spell damage from a monster to another monster with explicit damage and spell hit mode.</summary>
    public static void ApplyMonsterSpellDamageToMonster(
        GameWorldRef wr,
        GameWorldMonster caster,
        GameWorldMonster targetMonster,
        int damage,
        AttackType spellAttackType) {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetMonster);

        if (!TryResolveMonsterAttack(
                wr,
                targetMonster,
                damage,
                spellAttackType,
                caster.StunDurationMs,
                caster.PosX,
                caster.PosY,
                out var resolution)) {
            return;
        }

        if (resolution.HpAfter > 0) {
            targetMonster.SetAggroFromDamageMonsterAttacker(caster.MonsterId, caster.Allegiance);
        }

        MonsterVisibility.BroadcastMonsterTakeDamageByMonster(
            wr,
            targetMonster,
            damage,
            caster.MonsterId,
            resolution.PacketAttackType,
            resolution.HpAfter,
            resolution.StunlockMs,
            resolution.KnockbackDurationMs,
            resolution.KnockbackDestX,
            resolution.KnockbackDestY,
            resolution.KnockbackFromX,
            resolution.KnockbackFromY);
        if (resolution.HpAfter == 0) {
            MonsterVisibility.BroadcastMonsterDied(wr, targetMonster);
        }
    }

    /// <summary>Applies any step-on-only ground effects on the player's current cell.</summary>
    public static void ApplyGroundEffectStepDamageToPlayer(GameWorldRef wr, GameWorldPlayer targetPlayer) {
        ArgumentNullException.ThrowIfNull(targetPlayer);
        if (!wr.GroundStateTracker.TryGetEffectsAtCell(targetPlayer.PosX, targetPlayer.PosY, out var cellEffects) || cellEffects is null) {
            return;
        }

        foreach (var effect in cellEffects) {
            if (effect.HasPeriodicDamage) {
                continue;
            }

            ApplyGroundEffectDamageToPlayer(
                wr,
                effect.CasterPlayerId,
                effect.DamagePerTick,
                targetPlayer,
                effect.SpellAttackType,
                effect.SpellId);
        }
    }

    /// <summary>Applies any step-on-only ground effects on the monster's current cell.</summary>
    public static void ApplyGroundEffectStepDamageToMonster(GameWorldRef wr, GameWorldMonster targetMonster) {
        ArgumentNullException.ThrowIfNull(targetMonster);
        if (!wr.GroundStateTracker.TryGetEffectsAtCell(targetMonster.PosX, targetMonster.PosY, out var cellEffects) || cellEffects is null) {
            return;
        }

        foreach (var effect in cellEffects) {
            if (effect.HasPeriodicDamage) {
                continue;
            }

            ApplyGroundEffectDamageToMonster(
                wr,
                effect.CasterPlayerId,
                effect.DamagePerTick,
                targetMonster,
                effect.SpellAttackType,
                effect.SpellId);
        }
    }

    /// <summary>Applies authoritative ground-effect damage to a monster using the captured caster id and damage snapshot from effect creation.</summary>
    public static void ApplyGroundEffectDamageToMonster(GameWorldRef wr, long attackerPlayerId, int damage, GameWorldMonster targetMonster, AttackType attackType, int spellId) {
        ArgumentNullException.ThrowIfNull(targetMonster);
        if (damage <= 0 || targetMonster.Dead) {
            return;
        }

        var attackStunDurationMs = 0;
        var attackerPosX = targetMonster.PosX;
        var attackerPosY = targetMonster.PosY;
        if (wr.World.TryGetConnectedPlayerById(attackerPlayerId, out var caster)) {
            attackerPosX = caster.PosX;
            attackerPosY = caster.PosY;
            if (attackType == AttackType.Stun) {
                attackStunDurationMs = caster.AttackStunDurationMs;
            }
        }

        if (!TryResolveMonsterAttack(
                wr,
                targetMonster,
                damage,
                attackType,
                attackStunDurationMs,
                attackerPosX,
                attackerPosY,
                out var resolution)) {
            return;
        }

        if (resolution.HpAfter > 0) {
            targetMonster.SetAggroFromDamagePlayerAttacker(attackerPlayerId);
        }

        MonsterVisibility.BroadcastMonsterTakeDamage(
            wr,
            targetMonster,
            damage,
            resolution.PacketAttackType,
            resolution.HpAfter,
            resolution.StunlockMs,
            resolution.KnockbackDurationMs,
            resolution.KnockbackDestX,
            resolution.KnockbackDestY,
            resolution.KnockbackFromX,
            resolution.KnockbackFromY);
        if (resolution.HpAfter == 0) {
            MonsterVisibility.BroadcastMonsterDied(wr, targetMonster);
            if (wr.World.TryGetConnectedPlayerById(attackerPlayerId, out var killer)) {
                wr.World.OnMonsterKilledByPlayer(killer, targetMonster);
            }
        } else {
            TryApplyGroundEffectSpellTemporaryEffectsAfterDamage(wr, spellId, targetMonster);
        }
    }

    /// <summary>Applies authoritative ground-effect damage to a player using the captured caster id and damage snapshot from effect creation.</summary>
    public static void ApplyGroundEffectDamageToPlayer(GameWorldRef wr, long attackerPlayerId, int damage, GameWorldPlayer targetPlayer, AttackType attackType, int spellId) {
        ArgumentNullException.ThrowIfNull(targetPlayer);
        if (damage <= 0 || targetPlayer.IsDead || targetPlayer.SpawnProtection) {
            return;
        }

        var stunDuration = 0;
        if (attackType == AttackType.Stun && wr.World.TryGetConnectedPlayerById(attackerPlayerId, out var caster)) {
            stunDuration = caster.AttackStunDurationMs;
        }

        MonsterVisibility.BroadcastPlayerTakeDamage(
            wr,
            targetPlayer.PlayerId,
            damage,
            attackerPlayerId,
            attackType,
            stunDuration);

        if (!targetPlayer.IsDead) {
            TryApplyGroundEffectSpellTemporaryEffectsAfterDamage(wr, spellId, targetPlayer);
        }
    }

    /// <summary>Applies spell <c>temporaryEffects</c> for a ground-effect spell after damage is delivered (periodic tick or step-on).</summary>
    private static void TryApplyGroundEffectSpellTemporaryEffectsAfterDamage(GameWorldRef wr, int spellId, GameWorldActionableEntity target) {
        if (!wr.SpellsById.TryGetValue(spellId, out var spell)) {
            return;
        }

        TemporaryEffects.ApplySpellTemporaryEffectsOnHit(wr, spell, target);
    }

    private static int ResolvePlayerAttackDirection(GameWorldPlayer attacker, int targetX, int targetY) {
        var attackDirection = Location.GetNextGridDirection(attacker.PosX, attacker.PosY, targetX, targetY);
        if (attackDirection < 0 || attackDirection > 7) {
            return attacker.FacingDirection;
        }

        return attackDirection;
    }

    private static void BroadcastPlayerAttackVisual(GameWorldRef wr, GameWorldPlayer attacker, ServerMessage attackVisual) {
        foreach (var recipient in wr.PlayerSpatialGrid.GetNearbyPlayers(attacker.PosX, attacker.PosY, attacker.SessionId, excludeDisconnected: true)) {
            NetworkManager.SendToPlayer(recipient, attackVisual);
        }
    }

    private static int ComputePlayerAttackDelayMs(GameWorldRef wr, GameWorldPlayer attacker, bool rangedAttack, int targetX, int targetY) {
        var delayMs = attacker.AttackSpeedMs / 2;
        if (rangedAttack) {
            delayMs += Projectile.ComputeTravelTime(attacker.PosX, attacker.PosY, targetX, targetY, wr.Settings.Timings.ArrowSpeed);
        }

        return delayMs;
    }

    private static bool TryRecordPlayerAttackDamageDelivery(string worldIdForLogging, GameWorldPlayer attacker) {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (attacker.TryRecordPlayerAttackDamageDelivery(nowMs, out var minIntervalMs, out var elapsedSinceLastDeliveryMs)) {
            return true;
        }

        Console.WriteLine(
            $"[GameWorld:{worldIdForLogging}] Warning: player {attacker.PlayerId}'s attack speed was detected to be erratic (elapsedMs={elapsedSinceLastDeliveryMs:F1}, minIntervalMs={minIntervalMs:F1}); skipping melee damage delivery.");
        return false;
    }

    private static bool TryResolveMonsterAttack(
        GameWorldRef wr,
        GameWorldMonster targetMonster,
        int damage,
        AttackType attackType,
        int attackStunDurationMs,
        int attackerPosX,
        int attackerPosY,
        out MonsterAttackResolution resolution) {
        resolution = default;
        var now = DateTimeOffset.UtcNow;
        if (!targetMonster.TryApplyAttackerHit(wr, damage, now, out var hpAfter)) {
            return false;
        }

        var stunlockMs = 0;
        int? knockbackDurMs = null;
        int? kbFromX = null;
        int? kbFromY = null;
        int? kbDestX = null;
        int? kbDestY = null;
        var packetAttackType = attackType;

        if (hpAfter > 0) {
            if (attackType == AttackType.Stun) {
                if (targetMonster.TryApplyStunlock(now, attackStunDurationMs)) {
                    stunlockMs = attackStunDurationMs;
                }
            } else if (attackType == AttackType.Knockback) {
                var fromX = targetMonster.PosX;
                var fromY = targetMonster.PosY;
                if (targetMonster.TryApplyKnockbackFromAttacker(wr, attackerPosX, attackerPosY, out var destX, out var destY)) {
                    packetAttackType = AttackType.Knockback;
                    if (targetMonster.TryApplyStunlock(now, attackStunDurationMs)) {
                        stunlockMs = attackStunDurationMs;
                    }
                    knockbackDurMs = wr.Settings.Timings.KnockbackTimeMs;
                    kbFromX = fromX;
                    kbFromY = fromY;
                    kbDestX = destX;
                    kbDestY = destY;
                } else {
                    packetAttackType = AttackType.Stun;
                    if (targetMonster.TryApplyStunlock(now, attackStunDurationMs)) {
                        stunlockMs = attackStunDurationMs;
                    }
                }
            }
        }

        resolution = new MonsterAttackResolution(
            hpAfter,
            packetAttackType,
            stunlockMs,
            knockbackDurMs,
            kbFromX,
            kbFromY,
            kbDestX,
            kbDestY);
        return true;
    }

    private static void ApplyPlayerAttackToMonster(GameWorldRef wr, GameWorldPlayer attacker, GameWorldMonster targetMonster, AttackType attackType) {
        if (!TryResolveMonsterAttack(
                wr,
                targetMonster,
                attacker.Damage,
                attackType,
                attacker.AttackStunDurationMs,
                attacker.PosX,
                attacker.PosY,
                out var resolution)) {
            return;
        }

        if (resolution.HpAfter > 0) {
            targetMonster.SetAggroFromDamagePlayerAttacker(attacker.PlayerId);
        }

        MonsterVisibility.BroadcastMonsterTakeDamage(
            wr,
            targetMonster,
            attacker.Damage,
            resolution.PacketAttackType,
            resolution.HpAfter,
            resolution.StunlockMs,
            resolution.KnockbackDurationMs,
            resolution.KnockbackDestX,
            resolution.KnockbackDestY,
            resolution.KnockbackFromX,
            resolution.KnockbackFromY);
        if (resolution.HpAfter == 0) {
            MonsterVisibility.BroadcastMonsterDied(wr, targetMonster);
            wr.World.OnMonsterKilledByPlayer(attacker, targetMonster);
        }
    }

    private static void ApplyPlayerAttackToPlayer(GameWorldRef wr, GameWorldPlayer attacker, GameWorldPlayer targetPlayer, AttackType attackType) {
        var stunPacketMs = 0;
        var knockbackDurMs = 0;
        var destKbX = -1;
        var destKbY = -1;
        var attackTypeOut = attackType;
        var px = targetPlayer.PosX;
        var py = targetPlayer.PosY;

        if (!targetPlayer.IsDead) {
            if (attackType == AttackType.Stun) {
                stunPacketMs = attacker.AttackStunDurationMs;
            } else if (attackType == AttackType.Knockback) {
                stunPacketMs = attacker.AttackStunDurationMs;
                var dir = Location.GetNextGridDirection(attacker.PosX, attacker.PosY, px, py);
                if (dir < 0 || dir > 7) {
                    attackTypeOut = AttackType.Stun;
                } else {
                    Location.GetDirectionDelta(dir, out var kdx, out var kdy);
                    var kx = px + kdx;
                    var ky = py + kdy;
                    if (wr.OccupancyTracker.IsFreeAndNotTeleportCell(kx, ky)) {
                        wr.OccupancyTracker.SetFree(px, py);
                        wr.OccupancyTracker.SetOccupied(kx, ky);
                        Movement.SetPlayerPosition(wr, targetPlayer, kx, ky);
                        Movement.SyncPlayerVisibilityAfterMovement(wr, targetPlayer, px, py, kx, ky, broadcastPlayerMoved: false);
                        ApplyGroundEffectStepDamageToPlayer(wr, targetPlayer);
                        knockbackDurMs = wr.Settings.Timings.KnockbackTimeMs;
                        destKbX = kx;
                        destKbY = ky;
                    } else {
                        attackTypeOut = AttackType.Stun;
                    }
                }
            }
        }

        MonsterVisibility.BroadcastPlayerTakeDamage(
            wr,
            targetPlayer.PlayerId,
            attacker.Damage,
            attacker.PlayerId,
            attackTypeOut,
            stunPacketMs,
            knockbackDurMs,
            destKbX,
            destKbY,
            knockbackDurMs > 0 ? px : null,
            knockbackDurMs > 0 ? py : null);
    }
}
