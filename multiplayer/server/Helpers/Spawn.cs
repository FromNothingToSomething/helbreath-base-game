using System.Collections.Generic;
using System.Linq;
using Mmorpg.Network;
using Server;
using Server.World;
using Server.Utils;
using Server.World.Game;

namespace Server.Helpers;

/// <summary>Spawn selection, initial world snapshot, spawn protection timing, and related broadcasts.</summary>
public static class Spawn {
    public static (int X, int Y) GetSpawnLocation(GameWorldRef wr) {
        int startX, startY;
        if (wr.Settings.SpawnInMiddle) {
            startX = wr.OccupancyTracker.SizeX / 2;
            startY = wr.OccupancyTracker.SizeY / 2;
        } else {
            startX = Random.Shared.Next(wr.OccupancyTracker.SizeX);
            startY = Random.Shared.Next(wr.OccupancyTracker.SizeY);
        }

        return GetSpawnLocation(wr, startX, startY);
    }

    public static (int X, int Y) GetSpawnLocation(GameWorldRef wr, int startX, int startY) {
        var maxRadius = Math.Max(wr.OccupancyTracker.SizeX, wr.OccupancyTracker.SizeY);
        return Location.FindNearestFreeLocation(wr.OccupancyTracker.IsFreeAndNotTeleportCell, startX, startY, maxRadius)
            ?? throw new InvalidOperationException($"Game world '{wr.WorldId}' could not find a free spawn location.");
    }

    /// <summary>Sends spell entries when <paramref name="includeSpells"/> is true, item directory on every send, plus session-scoped player tunables; called on every join (spells omitted on world transfer).</summary>
    public static void SendInitialState(GameWorldRef wr, GameWorldPlayer player, bool includeSpells) {
        IEnumerable<SpellConfig> spells = includeSpells
            ? wr.SpellsById.OrderBy(kv => kv.Key).Select(kv => kv.Value)
            : Array.Empty<SpellConfig>();
        NetworkManager.SendToPlayer(
            player,
            NetworkManager.CreateInitialState(
                spells,
                wr.ItemsById.Values.OrderBy(i => i.Id),
                player.InventoryManager.BagItems,
                player.InventoryManager.EquippedItems,
                player.PlayerId,
                player.MovementSpeedMs,
                player.BaseMovementSpeedMs,
                player.RunningMode,
                wr.Settings.Ping.Interval,
                player.AttackMode,
                player.AttackRange,
                player.Damage,
                player.AttackSpeedMs,
                wr.Settings.Timings.ArrowSpeed,
                player.Hp,
                player.MaxHp,
                wr.Settings.Timings.PlayerPickupAnimationTime,
                wr.Settings.Timings.PlayerBowAnimationTime,
                player.AttackStunDurationMs,
                player.CastSpeedMs,
                player.AttackType,
                player.AllowDashAttack,
                (PlayerGender)player.GenderValue,
                (PlayerSkinColor)player.SkinColorValue,
                player.HairStyleIndex,
                player.UnderwearColorIndex,
                wr.NpcsById.Values.OrderBy(n => n.Id),
                player.CreateNocSnapshot(),
                includeSpells ? player.NocCatalog : null));
    }

    /// <summary>Map snapshot on load: position, teleports, music, death flag, weather.</summary>
    public static void SendInitialGameWorldState(GameWorldRef wr, GameWorldPlayer player) {
        NetworkManager.SendToPlayer(
            player,
            NetworkManager.CreateInitialGameWorldState(
                wr.WorldId,
                wr.Map,
                wr.Music,
                player.PosX,
                player.PosY,
                player.FacingDirection,
                wr.TeleportLocs,
                player.IsDead,
                wr.World.CurrentWeather));
    }

    /// <summary>Sends <see cref="Mmorpg.Network.InitialState"/> (spells only on first join / reconnect), then <see cref="Mmorpg.Network.InitialGameWorldState"/>, spawn protection, visibility.</summary>
    public static void CompletePlayerJoin(GameWorldRef wr, GameWorldPlayer player, bool includeSpellsInInitialState) {
        SendInitialState(wr, player, includeSpellsInInitialState);
        SendInitialGameWorldState(wr, player);

        var periodSeconds = wr.Settings.Timings.SpawnProtectionTime;
        if (periodSeconds > 0) {
            player.SetSpawnProtection(true);
            var sessionId = player.SessionId;
            wr.Scheduler.SetTimeout(periodSeconds * 1000, () => OnSpawnProtectionTimeout(wr, sessionId));
        }

        Movement.FillNearbyPlayersById(wr.PlayerSpatialGrid, player.PosX, player.PosY, player.SessionId, wr.NearbyPlayersByIdScratch);
        var nearbyPlayers = wr.NearbyPlayersByIdScratch;
        Movement.SendPlayersSnapshotsBulk(player, nearbyPlayers.Values);
        var joinerEnteredForOthers = NetworkManager.CreatePlayersEnteredRange(player);
        foreach (var nearbyPlayer in nearbyPlayers.Values) {
            if (!nearbyPlayer.Disconnected) {
                NetworkManager.SendToPlayer(nearbyPlayer, joinerEnteredForOthers);
                nearbyPlayer.AddPlayerInRange(player.PlayerId);
            }
        }

        if (periodSeconds > 0) {
            NetworkManager.SendToPlayer(player, NetworkManager.CreateSpawnProtectionEnabled(player.PlayerId));
        }

        player.ReplacePlayersInRange(nearbyPlayers.Keys);
        MonsterVisibility.SendMonstersInRangeOnPlayerJoin(wr, player);
        Npc.SendNpcsInRangeOnPlayerJoin(wr, player);
        GroundStateVisibility.SendGroundStatesInRangeOnPlayerJoin(wr, player);
    }

    private static void OnSpawnProtectionTimeout(GameWorldRef wr, Guid sessionId) {
        if (!wr.World.TryGetPlayerBySessionId(sessionId, out var player)) {
            return;
        }
        if (!player.SpawnProtection) {
            return;
        }
        DisableSpawnProtectionAndNotify(wr, player);
    }

    public static void DisableSpawnProtectionAndNotify(GameWorldRef wr, GameWorldPlayer player) {
        player.SetSpawnProtection(false);
        var spawnProtectionDisabledMessage = NetworkManager.CreateSpawnProtectionDisabled(player.PlayerId);
        NetworkManager.SendToPlayer(player, spawnProtectionDisabledMessage);
        foreach (var nearbyPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(player.PosX, player.PosY, player.SessionId)) {
            NetworkManager.SendToPlayer(nearbyPlayer, spawnProtectionDisabledMessage);
        }
    }
}
