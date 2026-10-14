using System.Collections.Generic;
using Mmorpg.Network;
using Server.Nocthemar.Core;
using Server;
using Server.World;
using Server.Utils;
using Server.World.Game;

namespace Server.Helpers;

/// <summary>
/// View-radius queries and bulk monster enter/leave packets for player–monster visibility (monsters are not socket clients).
/// </summary>
public static class MonsterVisibility {
    /// <summary>Clears <paramref name="destination"/> and fills it with every <see cref="GameWorldMonster"/> in <see cref="GameWorldRef.MonsterSpatialGrid"/> within view of <paramref name="centerX"/>, <paramref name="centerY"/>.</summary>
    public static void FillNearbyMonstersById(GameWorldRef wr, int centerX, int centerY, Dictionary<long, GameWorldMonster> destination) {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        foreach (var monster in wr.MonsterSpatialGrid.GetNearbyMonsters(centerX, centerY)) {
            destination[monster.MonsterId] = monster;
        }
    }

    /// <summary>Reconciles <paramref name="player"/>.<see cref="GameWorldPlayer.MonstersInRange"/> after they moved to <see cref="GameWorldPlayer.PosX"/>/<c>PosY</c>.</summary>
    public static void SyncPlayerMonsterVisibilityAfterMovement(GameWorldRef wr, GameWorldPlayer player) {
        ArgumentNullException.ThrowIfNull(player);

        wr.MonstersPreviouslyInRangeScratch.Clear();
        wr.MonstersPreviouslyInRangeScratch.UnionWith(player.MonstersInRange);

        FillNearbyMonstersById(wr, player.PosX, player.PosY, wr.NearbyMonstersByIdScratch);
        var monstersNow = wr.NearbyMonstersByIdScratch;

        var entered = wr.PlayerMonsterVisibilityEnteredScratch;
        var left = wr.PlayerMonsterVisibilityLeftMonsterIdsScratch;
        entered.Clear();
        left.Clear();
        foreach (var kv in monstersNow) {
            if (!wr.MonstersPreviouslyInRangeScratch.Contains(kv.Key)) {
                entered.Add(kv.Value);
            }
        }

        foreach (var monsterId in wr.MonstersPreviouslyInRangeScratch) {
            if (!monstersNow.ContainsKey(monsterId)) {
                left.Add(monsterId);
            }
        }

        if (entered.Count > 0 && !player.Disconnected) {
            NetworkManager.SendToPlayer(player, NetworkManager.CreateMonstersEnteredRange(entered));
        }

        foreach (var monster in entered) {
            monster.AddPlayerInRange(player.PlayerId);
        }

        if (left.Count > 0 && !player.Disconnected) {
            NetworkManager.SendToPlayer(player, NetworkManager.CreateMonstersLeftRange(left));
        }

        foreach (var monsterId in left) {
            if (wr.MonstersByMonsterId.TryGetValue(monsterId, out var monster)) {
                monster.RemovePlayerInRange(player.PlayerId);
            }
        }

        player.ReplaceMonstersInRange(monstersNow.Keys);
    }

    /// <summary>Sends bulk enter for monsters near <paramref name="player"/>, registers mutual visibility (join / reconnect), and updates <see cref="GameWorldPlayer.ReplaceMonstersInRange"/>. Logs to the console when sending, and when the map has monsters but none fall inside the player's view box (spawn vs radius diagnostic).</summary>
    public static void SendMonstersInRangeOnPlayerJoin(GameWorldRef wr, GameWorldPlayer player) {
        ArgumentNullException.ThrowIfNull(player);

        FillNearbyMonstersById(wr, player.PosX, player.PosY, wr.NearbyMonstersByIdScratch);
        var nearby = wr.NearbyMonstersByIdScratch;
        if (nearby.Count > 0 && !player.Disconnected) {
            Console.WriteLine(
                $"[MonsterVisibility] Join: sending {nearby.Count} monster(s) in view to player {player.PlayerId} at ({player.PosX},{player.PosY}); map has {wr.MonstersByMonsterId.Count} total.");
            NetworkManager.SendToPlayer(player, NetworkManager.CreateMonstersEnteredRange(nearby.Values));
        } else if (wr.MonstersByMonsterId.Count > 0) {
            Console.WriteLine(
                $"[MonsterVisibility] Join: 0 monsters in view for player {player.PlayerId} at ({player.PosX},{player.PosY}); {wr.MonstersByMonsterId.Count} on map (check spawn vs view radius).");
        }

        foreach (var monster in nearby.Values) {
            monster.AddPlayerInRange(player.PlayerId);
        }

        player.ReplaceMonstersInRange(nearby.Keys);
        MonsterChase.EvaluateChaseForPlayer(wr, player);
    }

    /// <summary>Sends <see cref="MonsterAttacked"/> to every connected player whose view includes <paramref name="monster"/>.</summary>
    public static void BroadcastMonsterAttacked(GameWorldRef wr, GameWorldMonster monster, int direction, int attackSpeedMs, bool rangedAttack, long targetPlayerId) {
        ArgumentNullException.ThrowIfNull(monster);

        var message = NetworkManager.CreateMonsterAttacked(monster.MonsterId, direction, attackSpeedMs, rangedAttack, targetPlayerId, monster.PosX, monster.PosY);
        foreach (var recipient in wr.PlayerSpatialGrid.GetNearbyPlayers(monster.PosX, monster.PosY, excludeDisconnected: true)) {
            NetworkManager.SendToPlayer(recipient, message);
        }
    }

    /// <summary>Sends <see cref="MonsterAttackedMonster"/> to every connected player whose view includes <paramref name="monster"/>.</summary>
    public static void BroadcastMonsterAttackedMonster(GameWorldRef wr, GameWorldMonster monster, int direction, int attackSpeedMs, bool rangedAttack, long targetMonsterId) {
        ArgumentNullException.ThrowIfNull(monster);

        var message = NetworkManager.CreateMonsterAttackedMonster(monster.MonsterId, direction, attackSpeedMs, rangedAttack, targetMonsterId, monster.PosX, monster.PosY);
        foreach (var recipient in wr.PlayerSpatialGrid.GetNearbyPlayers(monster.PosX, monster.PosY, excludeDisconnected: true)) {
            NetworkManager.SendToPlayer(recipient, message);
        }
    }

    /// <summary>Sends <see cref="MonsterTakeDamage"/> to every connected player whose view includes <paramref name="monster"/>.</summary>
    /// <param name="stunlockDurationMs">Monster stunlock from this hit when <paramref name="attackType"/> is <see cref="AttackType.Stun"/> or knockback fallback stun, and the server applied it; otherwise 0.</param>
    /// <param name="knockbackDurationMs">When <paramref name="attackType"/> is <see cref="AttackType.Knockback"/> and the monster moved; otherwise null.</param>
    public static void BroadcastMonsterTakeDamage(
        GameWorldRef wr,
        GameWorldMonster monster,
        int damage,
        AttackType attackType,
        int hp,
        int stunlockDurationMs = 0,
        int? knockbackDurationMs = null,
        int? destX = null,
        int? destY = null,
        int? knockbackFromX = null,
        int? knockbackFromY = null) {
        ArgumentNullException.ThrowIfNull(monster);

        var message = NetworkManager.CreateMonsterTakeDamage(
            monster.MonsterId,
            damage,
            attackType,
            hp,
            stunlockDurationMs,
            knockbackDurationMs,
            destX,
            destY,
            knockbackFromX,
            knockbackFromY);
        foreach (var recipient in wr.PlayerSpatialGrid.GetNearbyPlayers(monster.PosX, monster.PosY, excludeDisconnected: true)) {
            NetworkManager.SendToPlayer(recipient, message);
        }
    }

    /// <summary>Sends <see cref="MonsterTakeDamageByMonster"/> to every connected player whose view includes <paramref name="monster"/>.</summary>
    public static void BroadcastMonsterTakeDamageByMonster(
        GameWorldRef wr,
        GameWorldMonster monster,
        int damage,
        long attackerMonsterId,
        AttackType attackType,
        int hp,
        int stunlockDurationMs = 0,
        int? knockbackDurationMs = null,
        int? destX = null,
        int? destY = null,
        int? knockbackFromX = null,
        int? knockbackFromY = null) {
        ArgumentNullException.ThrowIfNull(monster);

        var message = NetworkManager.CreateMonsterTakeDamageByMonster(
            monster.MonsterId,
            damage,
            attackerMonsterId,
            attackType,
            hp,
            stunlockDurationMs,
            knockbackDurationMs,
            destX,
            destY,
            knockbackFromX,
            knockbackFromY);
        foreach (var recipient in wr.PlayerSpatialGrid.GetNearbyPlayers(monster.PosX, monster.PosY, excludeDisconnected: true)) {
            NetworkManager.SendToPlayer(recipient, message);
        }
    }

    /// <summary>Sends <see cref="MonsterDied"/> to every connected player whose view includes <paramref name="monster"/>.</summary>
    public static void BroadcastMonsterDied(GameWorldRef wr, GameWorldMonster monster) {
        ArgumentNullException.ThrowIfNull(monster);

        monster.ClearAllTemporaryEffects(wr);

        var message = NetworkManager.CreateMonsterDied(monster.MonsterId, monster.CorpseDecayDurationMs);
        foreach (var recipient in wr.PlayerSpatialGrid.GetNearbyPlayers(monster.PosX, monster.PosY, excludeDisconnected: true)) {
            NetworkManager.SendToPlayer(recipient, message);
        }
    }

    /// <summary>Sends <see cref="PlayerReceiveDamage"/> to every connected player whose view includes <paramref name="targetPlayerId"/>'s cell.</summary>
    /// <param name="knockbackFromX">With <paramref name="knockbackFromY"/>, server cell before knockback (set for knockback so clients align with authority vs movement prediction).</param>
    public static void BroadcastPlayerReceiveDamage(
        GameWorldRef wr,
        long targetPlayerId,
        int damage,
        long monsterId,
        AttackType attackType,
        int stunDurationMs,
        int knockbackDurationMs = 0,
        int destX = -1,
        int destY = -1,
        int? knockbackFromX = null,
        int? knockbackFromY = null,
        DamageSchool school = DamageSchool.Physical) {
        damage = MitigateForTarget(wr, targetPlayerId, damage, school);
        var combatMessage = NetworkManager.CreatePlayerReceiveDamage(
            targetPlayerId,
            damage,
            monsterId,
            attackType,
            stunDurationMs,
            knockbackDurationMs,
            destX,
            destY,
            knockbackFromX,
            knockbackFromY);
        BroadcastCombatDamageToPlayer(
            wr,
            targetPlayerId,
            damage,
            attackType,
            stunDurationMs,
            knockbackDurationMs,
            destX,
            destY,
            knockbackFromX,
            knockbackFromY,
            combatMessage);
    }

    /// <summary>Sends <see cref="PlayerTakeDamage"/> to every connected player whose view includes <paramref name="targetPlayerId"/>'s cell.</summary>
    public static void BroadcastPlayerTakeDamage(
        GameWorldRef wr,
        long targetPlayerId,
        int damage,
        long attackerPlayerId,
        AttackType attackType,
        int stunDurationMs,
        int knockbackDurationMs = 0,
        int destX = -1,
        int destY = -1,
        int? knockbackFromX = null,
        int? knockbackFromY = null,
        DamageSchool school = DamageSchool.Physical) {
        damage = MitigateForTarget(wr, targetPlayerId, damage, school);
        var combatMessage = NetworkManager.CreatePlayerTakeDamage(
            targetPlayerId,
            damage,
            attackerPlayerId,
            attackType,
            stunDurationMs,
            knockbackDurationMs,
            destX,
            destY,
            knockbackFromX,
            knockbackFromY);
        BroadcastCombatDamageToPlayer(
            wr,
            targetPlayerId,
            damage,
            attackType,
            stunDurationMs,
            knockbackDurationMs,
            destX,
            destY,
            knockbackFromX,
            knockbackFromY,
            combatMessage);
    }

    /// <summary>The damage the target actually takes after its armor and resistances; the broadcast message carries this value, so clients show what was really lost.</summary>
    private static int MitigateForTarget(GameWorldRef wr, long targetPlayerId, int damage, DamageSchool school) {
        return wr.World.TryGetConnectedPlayerById(targetPlayerId, out var target) ? target.MitigateDamage(damage, school) : damage;
    }

    private static void BroadcastCombatDamageToPlayer(
        GameWorldRef wr,
        long targetPlayerId,
        int damage,
        AttackType attackType,
        int stunDurationMs,
        int knockbackDurationMs,
        int destX,
        int destY,
        int? knockbackFromX,
        int? knockbackFromY,
        ServerMessage combatDamageMessage) {
        if (!wr.World.TryGetConnectedPlayerById(targetPlayerId, out var target)) {
            return;
        }

        if (attackType != AttackType.NoInterrupt) {
            target.RegisterNonNoInterruptDamage();
            target.ClearPickupActionLockout();
            target.ClearBowStanceActionLockout();
            target.ClearSpellCastStateOnInterruptingDamage();
        }

        if ((attackType == AttackType.Stun || attackType == AttackType.Knockback) && stunDurationMs > 0) {
            target.RegisterCombatInterruptStunlock(stunDurationMs);
        }

        target.ApplyDamage(damage);
        if (damage > 0) {
            target.NotifyCombatDamageMayCancelLogout();
        }

        foreach (var recipient in wr.PlayerSpatialGrid.GetNearbyPlayers(target.PosX, target.PosY, excludeDisconnected: true)) {
            NetworkManager.SendToPlayer(recipient, combatDamageMessage);
        }

        NetworkManager.SendToPlayer(target, NetworkManager.CreateHpUpdated(target.Hp, target.MaxHp));
        if (target.IsDead) {
            wr.World.HandlePlayerDeath(wr, target);
        }
    }

    /// <summary>Notifies connected players near the spawn cell and records mutual visibility.</summary>
    public static void BroadcastMonsterSpawnToNearbyPlayers(GameWorldRef wr, GameWorldMonster spawned) {
        ArgumentNullException.ThrowIfNull(spawned);

        var message = NetworkManager.CreateMonstersEnteredRange(spawned);
        foreach (var recipient in wr.PlayerSpatialGrid.GetNearbyPlayers(spawned.PosX, spawned.PosY, null, excludeDisconnected: true)) {
            if (recipient.IsMonsterInRange(spawned.MonsterId)) {
                continue;
            }

            NetworkManager.SendToPlayer(recipient, message);
            recipient.AddMonsterInRange(spawned.MonsterId);
            spawned.AddPlayerInRange(recipient.PlayerId);
        }

        MonsterChase.EvaluateChaseForMonster(wr, spawned);
    }

    /// <summary>
    /// After an authoritative monster cell step: sends <see cref="MonsterMoved"/> to viewers who still see it,
    /// <see cref="MonstersEnteredRange"/> / <see cref="MonstersLeftRange"/> for players whose view changed,
    /// and refreshes mutual range membership (mirrors player movement visibility).
    /// </summary>
    public static void SyncMonsterVisibilityAfterMonsterStep(
        GameWorldRef wr,
        GameWorldMonster monster,
        int prevX,
        int prevY,
        int newX,
        int newY) {
        ArgumentNullException.ThrowIfNull(monster);

        var oldV = wr.MonsterStepOldViewersScratch;
        var newV = wr.MonsterStepNewViewersScratch;
        oldV.Clear();
        newV.Clear();

        foreach (var p in wr.PlayerSpatialGrid.GetNearbyPlayers(prevX, prevY, excludeDisconnected: false)) {
            oldV[p.PlayerId] = p;
        }

        foreach (var p in wr.PlayerSpatialGrid.GetNearbyPlayers(newX, newY, excludeDisconnected: false)) {
            newV[p.PlayerId] = p;
        }

        var monsterMoved = NetworkManager.CreateMonsterMoved(monster.MonsterId, prevX, prevY, newX, newY, monster.MovementSpeedMs, monster.FacingDirection);
        foreach (var kv in oldV) {
            if (!newV.ContainsKey(kv.Key)) {
                continue;
            }

            var viewer = kv.Value;
            if (viewer.Disconnected) {
                continue;
            }

            NetworkManager.SendToPlayer(viewer, monsterMoved);
        }

        var enteredPayload = NetworkManager.CreateMonstersEnteredRange(monster);
        foreach (var kv in newV) {
            if (oldV.ContainsKey(kv.Key)) {
                continue;
            }

            var viewer = kv.Value;
            if (viewer.Disconnected) {
                continue;
            }

            NetworkManager.SendToPlayer(viewer, enteredPayload);
            viewer.AddMonsterInRange(monster.MonsterId);
        }

        var hasLeaver = false;
        foreach (var kv in oldV) {
            if (newV.ContainsKey(kv.Key)) {
                continue;
            }

            if (!kv.Value.Disconnected) {
                hasLeaver = true;
                break;
            }
        }

        if (hasLeaver) {
            var leftMessage = NetworkManager.CreateMonstersLeftRange(monster.MonsterId);
            foreach (var kv in oldV) {
                if (newV.ContainsKey(kv.Key)) {
                    continue;
                }

                var viewer = kv.Value;
                if (viewer.Disconnected) {
                    continue;
                }

                NetworkManager.SendToPlayer(viewer, leftMessage);
                viewer.RemoveMonsterInRange(monster.MonsterId);
            }
        }

        MonsterChase.EvaluateChaseForMonster(wr, monster, newV);
        monster.ReplacePlayersInRange(newV.Keys);
    }
}
