using System.Collections.Generic;
using System.Linq;
using Mmorpg.Network;
using Server;
using Server.World;
using Server.World.Game;

namespace Server.Utils;

/// <summary>Factory helpers for <see cref="ServerMessage"/> payloads sent from game world logic to clients.</summary>
public static class NetworkManager {
    public static void SendToPlayer(GameWorldPlayer player, ServerMessage message) {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(message);
        player.Send(message);
    }

    public static ServerMessage CreateWorldsList(IEnumerable<GameWorldConfig> gameWorlds) {
        ArgumentNullException.ThrowIfNull(gameWorlds);

        var worldsList = new WorldsList();
        foreach (var gameWorld in gameWorlds) {
            worldsList.Worlds.Add(new GameWorldListEntry {
                Id = gameWorld.Id,
                Name = gameWorld.Name,
                Map = gameWorld.Map,
            });
        }

        return new ServerMessage {
            WorldsList = worldsList,
        };
    }

    public static ServerMessage CreateMonstersList(IEnumerable<MonsterConfig> monsters) {
        ArgumentNullException.ThrowIfNull(monsters);

        var monstersList = new MonstersList();
        foreach (var monster in monsters) {
            monstersList.Monsters.Add(new MonsterListEntry {
                Name = monster.Name,
                Sprite = monster.Sprite,
            });
        }

        return new ServerMessage {
            MonstersList = monstersList,
        };
    }

    /// <summary>Spells, item directory, and session-scoped player tunables; spells omitted on some world transfers, item directory always populated.</summary>
    public static ServerMessage CreateInitialState(
        IEnumerable<SpellConfig> spells,
        IEnumerable<ItemConfig> itemsDirectory,
        IEnumerable<InventoryItemState> bagItems,
        IEnumerable<KeyValuePair<string, InventoryItemState>> equippedItems,
        long playerId,
        int movementSpeedMs,
        int baseMovementSpeedMs,
        bool runningMode,
        int pingIntervalMs,
        bool attackMode,
        int attackRangeCells,
        int attackDamage,
        int attackSpeedMs,
        int arrowSpeedPxPerSec,
        int hp,
        int maxHp,
        int playerPickupAnimationTimeMs,
        int playerBowAnimationDurationMs,
        int attackStunDurationMs,
        int castSpeedMs,
        int attackType,
        bool allowDashAttack,
        PlayerGender gender,
        PlayerSkinColor skinColor,
        int hairStyleIndex,
        int underwearColorIndex,
        IEnumerable<NpcConfig> npcDirectory,
        NocSnapshot noc,
        NocCatalog? nocCatalog = null) {
        ArgumentNullException.ThrowIfNull(noc);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(itemsDirectory);
        ArgumentNullException.ThrowIfNull(bagItems);
        ArgumentNullException.ThrowIfNull(equippedItems);
        ArgumentNullException.ThrowIfNull(npcDirectory);

        var initialState = new InitialState {
            PlayerId = playerId,
            MovementSpeedMs = movementSpeedMs,
            BaseMovementSpeedMs = baseMovementSpeedMs,
            RunningMode = runningMode,
            PingIntervalMs = pingIntervalMs,
            AttackMode = attackMode,
            AttackRangeCells = attackRangeCells,
            AttackDamage = attackDamage,
            AttackSpeedMs = attackSpeedMs,
            ArrowSpeedPxPerSec = arrowSpeedPxPerSec,
            Hp = hp,
            MaxHp = maxHp,
            PlayerPickupAnimationTimeMs = playerPickupAnimationTimeMs,
            PlayerBowAnimationDurationMs = playerBowAnimationDurationMs,
            AttackStunDurationMs = attackStunDurationMs,
            CastSpeedMs = castSpeedMs,
            AttackType = attackType,
            AllowDashAttack = allowDashAttack,
            Gender = gender,
            SkinColor = skinColor,
            HairStyleIndex = hairStyleIndex,
            UnderwearColorIndex = underwearColorIndex,
            Noc = noc,
            NocCatalog = nocCatalog,
        };
        foreach (var spell in spells) {
            var entry = new SpellEntry {
                Id = spell.Id,
                Name = spell.Name,
            };
            if (spell.DamageType.HasValue) {
                entry.DamageType = spell.DamageType.Value;
            }
            if (spell.TemporaryEffects is not null) {
                foreach (var fx in spell.TemporaryEffects) {
                    var te = new SpellTemporaryEffectSpec {
                        Type = fx.Type,
                        DurationMs = fx.Duration,
                        Group = fx.Group,
                    };
                    if (fx.MovementSpeedModifier.HasValue) {
                        te.MovementSpeedModifier = fx.MovementSpeedModifier.Value;
                    }
                    if (fx.AttackSpeedModifier.HasValue) {
                        te.AttackSpeedModifier = fx.AttackSpeedModifier.Value;
                    }
                    if (fx.CastSpeedModifier.HasValue) {
                        te.CastSpeedModifier = fx.CastSpeedModifier.Value;
                    }

                    entry.TemporaryEffects.Add(te);
                }
            }
            if (spell.AoeRadius.HasValue) {
                entry.AoeRadius = spell.AoeRadius.Value;
            }
            if (spell.ProjectileSpeed.HasValue) {
                entry.ProjectileSpeed = spell.ProjectileSpeed.Value;
            }
            if (spell.EmissionSteps.HasValue) {
                entry.EmissionSteps = spell.EmissionSteps.Value;
            }
            if (spell.StartRadius.HasValue) {
                entry.StartRadius = spell.StartRadius.Value;
            }
            if (spell.EndRadius.HasValue) {
                entry.EndRadius = spell.EndRadius.Value;
            }
            if (spell.StartShards.HasValue) {
                entry.StartShards = spell.StartShards.Value;
            }
            if (spell.EndShards.HasValue) {
                entry.EndShards = spell.EndShards.Value;
            }
            if (spell.Duration.HasValue) {
                entry.DurationMs = spell.Duration.Value;
            }
            if (spell.ProjectileDistance.HasValue) {
                entry.ProjectileDistancePx = spell.ProjectileDistance.Value;
            }
            if (spell.AimAssist == true) {
                entry.AimAssist = true;
            }
            initialState.Spells.Add(entry);
        }

        foreach (var item in itemsDirectory.OrderBy(i => i.Id)) {
            var dirEntry = new ItemDirectoryEntry {
                Id = item.Id,
                Name = item.Name,
                ItemType = item.ItemType,
            };
            if (item.BlockedItemSlots is not null) {
                foreach (var slot in item.BlockedItemSlots) {
                    dirEntry.BlockedItemSlots.Add(slot);
                }
            }
            if (item.Stackable == true) {
                dirEntry.Stackable = true;
            }
            if (item.Consumable == true) {
                dirEntry.Consumable = true;
            }
            if (item.Effects is not null) {
                foreach (var fx in item.Effects) {
                    var fe = new ItemEffectEntry {
                        Effect = fx.Effect,
                    };
                    if (fx.EffectColor is int ec) {
                        fe.EffectColor = (uint)ec;
                    }
                    dirEntry.Effects.Add(fe);
                }
            }
            if (item.WeaponType is int wt) {
                dirEntry.WeaponType = wt;
            }
            if (item.Gender is int ig) {
                dirEntry.EquipGender = (PlayerGender)ig;
            }
            initialState.ItemsDirectory.Add(dirEntry);
        }

        foreach (var bagItem in bagItems.OrderBy(item => item.BagZIndex)) {
            initialState.BagItems.Add(ToInventoryItemEntry(bagItem));
        }
        foreach (var equippedItem in equippedItems.OrderBy(entry => entry.Key, StringComparer.Ordinal)) {
            initialState.EquippedItems.Add(ToEquippedInventoryItemEntry(equippedItem.Key, equippedItem.Value));
        }

        foreach (var npc in npcDirectory.OrderBy(n => n.Id)) {
            initialState.NpcDirectory.Add(new NpcDirectoryEntry {
                Id = npc.Id,
                Name = npc.Name,
            });
        }

        return new ServerMessage {
            InitialState = initialState,
        };
    }

    /// <summary>Map snapshot for each load: grid position, facing, teleports, music, death flag, weather.</summary>
    public static ServerMessage CreateInitialGameWorldState(
        string gameWorldId,
        string mapName,
        string? musicFile,
        int playerX,
        int playerY,
        int playerDirection,
        IEnumerable<GameWorldTeleportSet>? teleportLocs = null,
        bool dead = false,
        WeatherMode weather = WeatherMode.Dry) {
        var snapshot = new InitialGameWorldState {
            GameWorldId = gameWorldId,
            MapName = mapName,
            MusicFile = musicFile ?? string.Empty,
            PlayerX = playerX,
            PlayerY = playerY,
            PlayerDirection = playerDirection,
            Dead = dead,
            Weather = weather,
        };
        if (teleportLocs is not null) {
            foreach (var teleportLoc in teleportLocs) {
                var teleportLocationSet = new TeleportLocationSet {
                    Target = new TeleportTarget {
                        WorldId = teleportLoc.Target.WorldId,
                        MapName = teleportLoc.Target.MapName,
                        Loc = new WorldLocation {
                            X = teleportLoc.Target.Loc.X,
                            Y = teleportLoc.Target.Loc.Y,
                        },
                    },
                };
                foreach (var loc in teleportLoc.Locs) {
                    teleportLocationSet.Locs.Add(new WorldLocation {
                        X = loc.X,
                        Y = loc.Y,
                    });
                }
                snapshot.TeleportLocs.Add(teleportLocationSet);
            }
        }

        return new ServerMessage {
            InitialGameWorldState = snapshot,
        };
    }

    /// <summary>Broadcast when a game world&apos;s weather changes; all clients in that world receive it.</summary>
    public static ServerMessage CreateWeatherChanged(WeatherMode weather) {
        return new ServerMessage {
            WeatherChanged = new WeatherChanged {
                Weather = weather,
            },
        };
    }

    public static PlayerEnteredRange ToPlayerEnteredRangeSnapshot(GameWorldPlayer p) {
        ArgumentNullException.ThrowIfNull(p);
        var snapshot = new PlayerEnteredRange {
            PlayerId = p.PlayerId,
            X = p.PosX,
            Y = p.PosY,
            MovementSpeedMs = p.MovementSpeedMs,
            RunningMode = p.RunningMode,
            SpawnProtection = p.SpawnProtection,
            Direction = p.FacingDirection,
            AttackMode = p.AttackMode,
            Dead = p.IsDead,
        };
        foreach (var equippedItem in p.InventoryManager.EquippedItems) {
            if (!InventoryManager.IsVisibleAppearanceSlot(equippedItem.Key)) {
                continue;
            }

            snapshot.VisibleEquippedItems.Add(ToVisibleEquippedItemEntry(equippedItem.Key, equippedItem.Value));
        }
        snapshot.Gender = (PlayerGender)p.GenderValue;
        snapshot.SkinColor = (PlayerSkinColor)p.SkinColorValue;
        snapshot.HairStyleIndex = p.HairStyleIndex;
        snapshot.UnderwearColorIndex = p.UnderwearColorIndex;
        snapshot.CharacterName = p.CharacterName;
        snapshot.RaceId = p.RaceId;
        if (p.FactionId is int factionId) {
            snapshot.FactionId = factionId;
        }

        snapshot.AttackSpeedMs = p.AttackSpeedMs;
        snapshot.CastSpeedMs = p.CastSpeedMs;
        p.FillActiveTemporaryEffects(snapshot);
        return snapshot;
    }

    public static ServerMessage CreateTemporaryEffectApplied(
        TemporaryEffectEntityKind entityKind,
        long entityId,
        TemporaryEffectType temporaryEffectType,
        int movementSpeedMs,
        int attackSpeedMs,
        int? castSpeedMs) {
        var applied = new TemporaryEffectApplied {
            EntityKind = entityKind,
            EntityId = entityId,
            TemporaryEffectType = temporaryEffectType,
        };
        // Explicit setters ensure optional-field presence bits are set for wire serialization.
        applied.MovementSpeedMs = movementSpeedMs;
        applied.AttackSpeedMs = attackSpeedMs;
        if (castSpeedMs.HasValue) {
            applied.CastSpeedMs = castSpeedMs.Value;
        }

        return new ServerMessage {
            TemporaryEffectApplied = applied,
        };
    }

    public static ServerMessage CreateTemporaryEffectExpired(
        TemporaryEffectEntityKind entityKind,
        long entityId,
        TemporaryEffectType temporaryEffectType,
        int movementSpeedMs,
        int attackSpeedMs,
        int? castSpeedMs) {
        var expired = new TemporaryEffectExpired {
            EntityKind = entityKind,
            EntityId = entityId,
            TemporaryEffectType = temporaryEffectType,
        };
        expired.MovementSpeedMs = movementSpeedMs;
        expired.AttackSpeedMs = attackSpeedMs;
        if (castSpeedMs.HasValue) {
            expired.CastSpeedMs = castSpeedMs.Value;
        }

        return new ServerMessage {
            TemporaryEffectExpired = expired,
        };
    }

    /// <summary>Observers only: another player joined a faction.</summary>
    public static ServerMessage CreatePlayerFactionChanged(long playerId, int? factionId) {
        var changed = new PlayerFactionChanged { PlayerId = playerId };
        if (factionId.HasValue) {
            changed.FactionId = factionId.Value;
        }

        return new ServerMessage { PlayerFactionChanged = changed };
    }

    /// <summary>Observers only: another player’s gender/skin/hair/underwear changed.</summary>
    public static ServerMessage CreatePlayerAppearanceChanged(
        long playerId,
        PlayerGender gender,
        PlayerSkinColor skinColor,
        int hairStyleIndex,
        int underwearColorIndex) {
        return new ServerMessage {
            PlayerAppearanceChanged = new PlayerAppearanceChanged {
                PlayerId = playerId,
                Gender = gender,
                SkinColor = skinColor,
                HairStyleIndex = hairStyleIndex,
                UnderwearColorIndex = underwearColorIndex,
            },
        };
    }

    public static InventoryItemEntry ToInventoryItemEntry(InventoryItemState item) {
        ArgumentNullException.ThrowIfNull(item);
        var entry = new InventoryItemEntry {
            ItemId = item.ItemId,
            ItemUid = item.ItemUid,
        };
        if (item.BagX.HasValue) {
            entry.BagX = item.BagX.Value;
        }
        if (item.BagY.HasValue) {
            entry.BagY = item.BagY.Value;
        }
        if (item.Quantity != 1) {
            entry.Quantity = item.Quantity;
        }
        foreach (var effectOverride in item.EffectOverrides ?? Array.Empty<ItemEffectConfig>()) {
            var effectEntry = new ItemEffectEntry {
                Effect = effectOverride.Effect,
            };
            if (effectOverride.EffectColor is int effectColor) {
                effectEntry.EffectColor = (uint)effectColor;
            }
            entry.EffectOverrides.Add(effectEntry);
        }
        entry.BagZIndex = item.BagZIndex;
        return entry;
    }

    public static GroundItemEntry ToGroundItemEntry(GroundItemState item) {
        ArgumentNullException.ThrowIfNull(item);
        var entry = new GroundItemEntry {
            ItemId = item.ItemId,
            ItemUid = item.ItemUid,
        };
        if (item.Quantity != 1) {
            entry.Quantity = item.Quantity;
        }
        foreach (var effectOverride in item.EffectOverrides ?? Array.Empty<ItemEffectConfig>()) {
            var effectEntry = new ItemEffectEntry {
                Effect = effectOverride.Effect,
            };
            if (effectOverride.EffectColor is int effectColor) {
                effectEntry.EffectColor = (uint)effectColor;
            }
            entry.EffectOverrides.Add(effectEntry);
        }
        return entry;
    }

    public static EquippedInventoryItemEntry ToEquippedInventoryItemEntry(string slot, InventoryItemState item) {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(item);
        return new EquippedInventoryItemEntry {
            Slot = slot,
            Item = ToInventoryItemEntry(item),
        };
    }

    public static VisibleEquippedItemEntry ToVisibleEquippedItemEntry(string slot, InventoryItemState item) {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(item);
        var entry = new VisibleEquippedItemEntry {
            Slot = slot,
            ItemId = item.ItemId,
        };
        foreach (var effectOverride in item.EffectOverrides ?? Array.Empty<ItemEffectConfig>()) {
            var effectEntry = new ItemEffectEntry {
                Effect = effectOverride.Effect,
            };
            if (effectOverride.EffectColor is int effectColor) {
                effectEntry.EffectColor = (uint)effectColor;
            }
            entry.EffectOverrides.Add(effectEntry);
        }
        return entry;
    }

    public static ServerMessage CreatePlayersEnteredRange(GameWorldPlayer player) {
        ArgumentNullException.ThrowIfNull(player);
        var payload = new PlayersEnteredRange();
        payload.Players.Add(ToPlayerEnteredRangeSnapshot(player));
        return new ServerMessage {
            PlayersEnteredRange = payload,
        };
    }

    public static ServerMessage CreatePlayersEnteredRange(IEnumerable<GameWorldPlayer> players) {
        ArgumentNullException.ThrowIfNull(players);

        var payload = new PlayersEnteredRange();
        foreach (var p in players) {
            payload.Players.Add(ToPlayerEnteredRangeSnapshot(p));
        }

        return new ServerMessage {
            PlayersEnteredRange = payload,
        };
    }

    private static void AddMonsterToMonstersEnteredRange(MonstersEnteredRange payload, GameWorldMonster m, DateTimeOffset now) {
        var mir = new MonsterInRange {
            MonsterId = m.MonsterId,
            Sprite = m.Sprite,
            X = m.PosX,
            Y = m.PosY,
            State = m.State,
            Name = m.Name,
            RangedAttack = m.RangedAttack,
            Hp = m.Hp,
            MaxHp = m.MaxHp,
            Dead = m.Dead,
            CorpseDecayTimeLeftMs = m.GetCorpseDecayTimeLeftMs(now),
            Direction = m.FacingDirection,
            MovementSpeedMs = m.MovementSpeedMs,
            AttackSpeedMs = m.AttackSpeedMs,
            AttackDamage = m.AttackDamageMin,
            AttackType = (int)m.AttackType,
            Allegiance = (int)m.Allegiance,
        };
        m.FillActiveTemporaryEffects(mir);
        payload.Monsters.Add(mir);
    }

    public static ServerMessage CreateMonstersEnteredRange(GameWorldMonster monster) {
        ArgumentNullException.ThrowIfNull(monster);
        var now = DateTimeOffset.UtcNow;
        var payload = new MonstersEnteredRange();
        AddMonsterToMonstersEnteredRange(payload, monster, now);
        return new ServerMessage {
            MonstersEnteredRange = payload,
        };
    }

    public static ServerMessage CreateMonstersEnteredRange(IEnumerable<GameWorldMonster> monsters) {
        ArgumentNullException.ThrowIfNull(monsters);

        var now = DateTimeOffset.UtcNow;
        var payload = new MonstersEnteredRange();
        foreach (var m in monsters) {
            AddMonsterToMonstersEnteredRange(payload, m, now);
        }

        return new ServerMessage {
            MonstersEnteredRange = payload,
        };
    }

    public static ServerMessage CreateMonstersLeftRange(long monsterId) {
        var payload = new MonstersLeftRange();
        payload.MonsterIds.Add(monsterId);
        return new ServerMessage {
            MonstersLeftRange = payload,
        };
    }

    public static ServerMessage CreateMonstersLeftRange(IEnumerable<long> monsterIds) {
        ArgumentNullException.ThrowIfNull(monsterIds);

        var payload = new MonstersLeftRange();
        foreach (var monsterId in monsterIds) {
            payload.MonsterIds.Add(monsterId);
        }

        return new ServerMessage {
            MonstersLeftRange = payload,
        };
    }

    public static ServerMessage CreateNpcsEnteredRange(GameWorldNPC npc) {
        ArgumentNullException.ThrowIfNull(npc);
        var payload = new NpcsEnteredRange();
        payload.Npcs.Add(new NpcInRange {
            NpcId = npc.NpcId,
            CatalogNpcId = npc.CatalogNpcId,
            X = npc.PosX,
            Y = npc.PosY,
            Direction = npc.FacingDirection,
        });
        return new ServerMessage {
            NpcsEnteredRange = payload,
        };
    }

    public static ServerMessage CreateNpcsEnteredRange(IEnumerable<GameWorldNPC> npcs) {
        ArgumentNullException.ThrowIfNull(npcs);

        var payload = new NpcsEnteredRange();
        foreach (var n in npcs) {
            payload.Npcs.Add(new NpcInRange {
                NpcId = n.NpcId,
                CatalogNpcId = n.CatalogNpcId,
                X = n.PosX,
                Y = n.PosY,
                Direction = n.FacingDirection,
            });
        }

        return new ServerMessage {
            NpcsEnteredRange = payload,
        };
    }

    public static ServerMessage CreateNpcsLeftRange(long npcId) {
        var payload = new NpcsLeftRange();
        payload.NpcIds.Add(npcId);
        return new ServerMessage {
            NpcsLeftRange = payload,
        };
    }

    public static ServerMessage CreateNpcsLeftRange(IEnumerable<long> npcIds) {
        ArgumentNullException.ThrowIfNull(npcIds);

        var payload = new NpcsLeftRange();
        foreach (var npcId in npcIds) {
            payload.NpcIds.Add(npcId);
        }

        return new ServerMessage {
            NpcsLeftRange = payload,
        };
    }

    public static ServerMessage CreatePlayersLeftRange(long playerId) {
        var payload = new PlayersLeftRange();
        payload.PlayerIds.Add(playerId);
        return new ServerMessage {
            PlayersLeftRange = payload,
        };
    }

    public static ServerMessage CreatePlayersLeftRange(IEnumerable<long> playerIds) {
        ArgumentNullException.ThrowIfNull(playerIds);

        var payload = new PlayersLeftRange();
        foreach (var id in playerIds) {
            payload.PlayerIds.Add(id);
        }

        return new ServerMessage {
            PlayersLeftRange = payload,
        };
    }

    /// <summary>One ground effect, no items (avoids allocating a single-element array for callers that remove one effect).</summary>
    public static ServerMessage CreateGroundStatesLeftRange(GameWorldRef wr, GroundEffectState effect) {
        ArgumentNullException.ThrowIfNull(effect);

        var payload = new GroundStatesLeftRange();
        var statesByCell = wr.GroundStatesLeftByCellScratch;
        statesByCell.Clear();
        var key = (effect.PosX, effect.PosY);
        var stateCell = new GroundStateCellRemoved {
            Loc = new WorldLocation {
                X = effect.PosX,
                Y = effect.PosY,
            },
        };
        stateCell.GroundEffectIds.Add(effect.GroundEffectId);
        statesByCell[key] = stateCell;
        payload.States.Add(stateCell);

        return new ServerMessage {
            GroundStatesLeftRange = payload,
        };
    }

    /// <summary>Empty ground effects; one ground item (avoids allocating a single-element array for the item list).</summary>
    public static ServerMessage CreateGroundStatesEnteredRange(GameWorldRef wr, GroundItemState groundItem) {
        ArgumentNullException.ThrowIfNull(groundItem);

        var payload = new GroundStatesEnteredRange();
        var statesByCell = wr.GroundStatesEnteredByCellScratch;
        statesByCell.Clear();
        var key = (groundItem.PosX, groundItem.PosY);
        var stateCell = new Mmorpg.Network.GroundStateCell {
            Loc = new WorldLocation {
                X = groundItem.PosX,
                Y = groundItem.PosY,
            },
        };
        statesByCell[key] = stateCell;
        payload.States.Add(stateCell);
        stateCell.GroundItem = ToGroundItemEntry(groundItem);

        return new ServerMessage {
            GroundStatesEnteredRange = payload,
        };
    }

    /// <summary>Empty ground effects; one ground item (avoids allocating a single-element array for the item list).</summary>
    public static ServerMessage CreateGroundStatesLeftRange(GameWorldRef wr, GroundItemState groundItem) {
        ArgumentNullException.ThrowIfNull(groundItem);

        var payload = new GroundStatesLeftRange();
        var statesByCell = wr.GroundStatesLeftByCellScratch;
        statesByCell.Clear();
        var key = (groundItem.PosX, groundItem.PosY);
        var stateCell = new GroundStateCellRemoved {
            Loc = new WorldLocation {
                X = groundItem.PosX,
                Y = groundItem.PosY,
            },
        };
        stateCell.GroundItemUid = groundItem.ItemUid;
        statesByCell[key] = stateCell;
        payload.States.Add(stateCell);

        return new ServerMessage {
            GroundStatesLeftRange = payload,
        };
    }

    public static ServerMessage CreateGroundStatesEnteredRange(GameWorldRef wr, IEnumerable<GroundEffectState> effects, IEnumerable<GroundItemState>? groundItems = null) {
        ArgumentNullException.ThrowIfNull(effects);

        var payload = new GroundStatesEnteredRange();
        var statesByCell = wr.GroundStatesEnteredByCellScratch;
        statesByCell.Clear();
        foreach (var effect in effects) {
            var key = (effect.PosX, effect.PosY);
            if (!statesByCell.TryGetValue(key, out var stateCell)) {
                stateCell = new Mmorpg.Network.GroundStateCell {
                    Loc = new WorldLocation {
                        X = effect.PosX,
                        Y = effect.PosY,
                    },
                };
                statesByCell[key] = stateCell;
                payload.States.Add(stateCell);
            }

            stateCell.Effects.Add(new GroundEffectEntry {
                GroundEffectId = effect.GroundEffectId,
                EffectType = (Mmorpg.Network.GroundEffectType)effect.EffectType,
            });
        }
        if (groundItems is not null) {
            foreach (var groundItem in groundItems) {
                var key = (groundItem.PosX, groundItem.PosY);
                if (!statesByCell.TryGetValue(key, out var stateCell)) {
                    stateCell = new Mmorpg.Network.GroundStateCell {
                        Loc = new WorldLocation {
                            X = groundItem.PosX,
                            Y = groundItem.PosY,
                        },
                    };
                    statesByCell[key] = stateCell;
                    payload.States.Add(stateCell);
                }

                stateCell.GroundItem = ToGroundItemEntry(groundItem);
            }
        }

        return new ServerMessage {
            GroundStatesEnteredRange = payload,
        };
    }

    public static ServerMessage CreateGroundStatesLeftRange(GameWorldRef wr, IEnumerable<GroundEffectState> effects, IEnumerable<GroundItemState>? groundItems = null) {
        ArgumentNullException.ThrowIfNull(effects);

        var payload = new GroundStatesLeftRange();
        var statesByCell = wr.GroundStatesLeftByCellScratch;
        statesByCell.Clear();
        foreach (var effect in effects) {
            var key = (effect.PosX, effect.PosY);
            if (!statesByCell.TryGetValue(key, out var stateCell)) {
                stateCell = new GroundStateCellRemoved {
                    Loc = new WorldLocation {
                        X = effect.PosX,
                        Y = effect.PosY,
                    },
                };
                statesByCell[key] = stateCell;
                payload.States.Add(stateCell);
            }

            stateCell.GroundEffectIds.Add(effect.GroundEffectId);
        }
        if (groundItems is not null) {
            foreach (var groundItem in groundItems) {
                var key = (groundItem.PosX, groundItem.PosY);
                if (!statesByCell.TryGetValue(key, out var stateCell)) {
                    stateCell = new GroundStateCellRemoved {
                        Loc = new WorldLocation {
                            X = groundItem.PosX,
                            Y = groundItem.PosY,
                        },
                    };
                    statesByCell[key] = stateCell;
                    payload.States.Add(stateCell);
                }

                stateCell.GroundItemUid = groundItem.ItemUid;
            }
        }

        return new ServerMessage {
            GroundStatesLeftRange = payload,
        };
    }

    public static ServerMessage CreateItemAddedToBag(InventoryItemState item) {
        ArgumentNullException.ThrowIfNull(item);
        return new ServerMessage {
            ItemAddedToBag = new ItemAddedToBag {
                Item = ToInventoryItemEntry(item),
            },
        };
    }

    public static ServerMessage CreateItemRemovedFromBag(long itemUid) {
        return new ServerMessage {
            ItemRemovedFromBag = new ItemRemovedFromBag {
                ItemUid = itemUid,
            },
        };
    }

    public static ServerMessage CreateItemMovedInBag(InventoryItemState item) {
        ArgumentNullException.ThrowIfNull(item);
        var payload = new ItemMovedInBag {
            ItemUid = item.ItemUid,
            BagZIndex = item.BagZIndex,
        };
        if (item.BagX.HasValue) {
            payload.BagX = item.BagX.Value;
        }
        if (item.BagY.HasValue) {
            payload.BagY = item.BagY.Value;
        }
        return new ServerMessage {
            ItemMovedInBag = payload,
        };
    }

    public static ServerMessage CreateItemEquipped(long playerId, string slot, InventoryItemState item) {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(item);
        return new ServerMessage {
            ItemEquipped = new ItemEquipped {
                PlayerId = playerId,
                EquippedItem = ToEquippedInventoryItemEntry(slot, item),
            },
        };
    }

    public static ServerMessage CreateItemUnequipped(long playerId, string slot, long itemUid) {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        return new ServerMessage {
            ItemUnequipped = new ItemUnequipped {
                PlayerId = playerId,
                Slot = slot,
                ItemUid = itemUid,
            },
        };
    }

    public static ServerMessage CreatePlayerMoved(long playerId, int curX, int curY, int destX, int destY, int movementSpeedMs, bool runningMode, bool dashAttack, bool teleport = false) {
        return new ServerMessage {
            PlayerMoved = new PlayerMoved {
                PlayerId = playerId,
                CurX = curX,
                CurY = curY,
                DestX = destX,
                DestY = destY,
                MovementSpeedMs = movementSpeedMs,
                RunningMode = runningMode,
                DashAttack = dashAttack,
                Teleport = teleport,
            },
        };
    }

    /// <summary>Confirms an admin-requested server teleport for the requesting client; <paramref name="x"/>/<paramref name="y"/> are authoritative grid coordinates.</summary>
    public static ServerMessage CreatePlayerTeleported(int x, int y) {
        return new ServerMessage {
            PlayerTeleported = new PlayerTeleported {
                X = x,
                Y = y,
            },
        };
    }

    public static ServerMessage CreateMonsterMoved(long monsterId, int curX, int curY, int destX, int destY, int movementSpeedMs, int direction) {
        return new ServerMessage {
            MonsterMoved = new MonsterMoved {
                MonsterId = monsterId,
                CurX = curX,
                CurY = curY,
                DestX = destX,
                DestY = destY,
                MovementSpeedMs = movementSpeedMs,
                Direction = direction,
            },
        };
    }

    /// <summary>Monster swing toward a player for client animation sync; <paramref name="worldX"/>/<paramref name="worldY"/> are the monster's authoritative grid cell.</summary>
    public static ServerMessage CreateMonsterAttacked(long monsterId, int direction, int attackSpeedMs, bool rangedAttack, long targetPlayerId, int worldX, int worldY) {
        return new ServerMessage {
            MonsterAttacked = new MonsterAttacked {
                MonsterId = monsterId,
                Direction = direction,
                AttackSpeedMs = attackSpeedMs,
                RangedAttack = rangedAttack,
                TargetPlayerId = targetPlayerId,
                WorldX = worldX,
                WorldY = worldY,
            },
        };
    }

    /// <summary>Monster swing toward another monster for client animation sync; <paramref name="worldX"/>/<paramref name="worldY"/> are the attacker's authoritative grid cell.</summary>
    public static ServerMessage CreateMonsterAttackedMonster(long monsterId, int direction, int attackSpeedMs, bool rangedAttack, long targetMonsterId, int worldX, int worldY) {
        return new ServerMessage {
            MonsterAttackedMonster = new MonsterAttackedMonster {
                MonsterId = monsterId,
                Direction = direction,
                AttackSpeedMs = attackSpeedMs,
                RangedAttack = rangedAttack,
                TargetMonsterId = targetMonsterId,
                WorldX = worldX,
                WorldY = worldY,
            },
        };
    }

    /// <summary>Notifies nearby clients that a player attacked (melee or bow) for animation sync; <paramref name="monsterId"/> supports ranged arrow VFX; <paramref name="worldX"/>/<paramref name="worldY"/> are the attacker's authoritative grid cell.</summary>
    public static ServerMessage CreatePlayerAttackedMonster(long playerId, int direction, int attackSpeedMs, bool rangedAttack, long monsterId, int worldX, int worldY, int attackType) {
        return new ServerMessage {
            PlayerAttackedMonster = new PlayerAttackedMonster {
                PlayerId = playerId,
                Direction = direction,
                AttackSpeedMs = attackSpeedMs,
                RangedAttack = rangedAttack,
                MonsterId = monsterId,
                WorldX = worldX,
                WorldY = worldY,
                AttackType = attackType,
            },
        };
    }

    /// <summary>Notifies nearby clients that a player attacked another player (melee or bow) for animation sync; <paramref name="worldX"/>/<paramref name="worldY"/> are the attacker's authoritative grid cell.</summary>
    public static ServerMessage CreatePlayerAttackedPlayer(long playerId, int direction, int attackSpeedMs, bool rangedAttack, long targetPlayerId, int worldX, int worldY, int attackType) {
        return new ServerMessage {
            PlayerAttackedPlayer = new PlayerAttackedPlayer {
                PlayerId = playerId,
                Direction = direction,
                AttackSpeedMs = attackSpeedMs,
                RangedAttack = rangedAttack,
                TargetPlayerId = targetPlayerId,
                WorldX = worldX,
                WorldY = worldY,
                AttackType = attackType,
            },
        };
    }

    public static ServerMessage CreateMonsterTakeDamage(
        long monsterId,
        int damage,
        AttackType attackType,
        int hp,
        int stunlockDurationMs = 0,
        int? knockbackDurationMs = null,
        int? destX = null,
        int? destY = null,
        int? knockbackFromX = null,
        int? knockbackFromY = null) {
        var mtd = new MonsterTakeDamage {
            MonsterId = monsterId,
            Damage = damage,
            AttackType = (int)attackType,
            Hp = hp,
            StunlockDurationMs = stunlockDurationMs,
        };
        if (knockbackDurationMs.HasValue) {
            mtd.KnockbackDurationMs = knockbackDurationMs.Value;
        }
        if (destX.HasValue) {
            mtd.DestX = destX.Value;
        }
        if (destY.HasValue) {
            mtd.DestY = destY.Value;
        }
        if (knockbackFromX.HasValue) {
            mtd.KnockbackFromX = knockbackFromX.Value;
        }
        if (knockbackFromY.HasValue) {
            mtd.KnockbackFromY = knockbackFromY.Value;
        }

        return new ServerMessage {
            MonsterTakeDamage = mtd,
        };
    }

    public static ServerMessage CreateMonsterTakeDamageByMonster(
        long targetMonsterId,
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
        var payload = new MonsterTakeDamageByMonster {
            TargetMonsterId = targetMonsterId,
            Damage = damage,
            AttackerMonsterId = attackerMonsterId,
            AttackType = (int)attackType,
            Hp = hp,
            StunlockDurationMs = stunlockDurationMs,
        };
        if (knockbackDurationMs.HasValue) {
            payload.KnockbackDurationMs = knockbackDurationMs.Value;
        }
        if (destX.HasValue) {
            payload.DestX = destX.Value;
        }
        if (destY.HasValue) {
            payload.DestY = destY.Value;
        }
        if (knockbackFromX.HasValue) {
            payload.KnockbackFromX = knockbackFromX.Value;
        }
        if (knockbackFromY.HasValue) {
            payload.KnockbackFromY = knockbackFromY.Value;
        }

        return new ServerMessage {
            MonsterTakeDamageByMonster = payload,
        };
    }

    public static ServerMessage CreateMonsterDied(long monsterId, int corpseDecayTimeMs) {
        return new ServerMessage {
            MonsterDied = new MonsterDied {
                MonsterId = monsterId,
                CorpseDecayTimeMs = corpseDecayTimeMs,
            },
        };
    }

    public static ServerMessage CreatePlayerReceiveDamage(
        long playerId,
        int damage,
        long monsterId,
        AttackType attackType,
        int stunDurationMs,
        int knockbackDurationMs = 0,
        int destX = -1,
        int destY = -1,
        int? knockbackFromX = null,
        int? knockbackFromY = null) {
        var prd = new PlayerReceiveDamage {
            PlayerId = playerId,
            Damage = damage,
            MonsterId = monsterId,
            AttackType = (int)attackType,
            StunDurationMs = stunDurationMs,
            KnockbackDurationMs = knockbackDurationMs,
            DestX = destX,
            DestY = destY,
        };
        if (knockbackFromX.HasValue && knockbackFromY.HasValue) {
            prd.KnockbackFromX = knockbackFromX.Value;
            prd.KnockbackFromY = knockbackFromY.Value;
        }

        return new ServerMessage {
            PlayerReceiveDamage = prd,
        };
    }

    public static ServerMessage CreatePlayerTakeDamage(
        long targetPlayerId,
        int damage,
        long attackerPlayerId,
        AttackType attackType,
        int stunDurationMs,
        int knockbackDurationMs = 0,
        int destX = -1,
        int destY = -1,
        int? knockbackFromX = null,
        int? knockbackFromY = null) {
        var ptd = new PlayerTakeDamage {
            TargetPlayerId = targetPlayerId,
            Damage = damage,
            AttackerPlayerId = attackerPlayerId,
            AttackType = (int)attackType,
            StunDurationMs = stunDurationMs,
            KnockbackDurationMs = knockbackDurationMs,
            DestX = destX,
            DestY = destY,
        };
        if (knockbackFromX.HasValue && knockbackFromY.HasValue) {
            ptd.KnockbackFromX = knockbackFromX.Value;
            ptd.KnockbackFromY = knockbackFromY.Value;
        }

        return new ServerMessage {
            PlayerTakeDamage = ptd,
        };
    }

    public static NocSnapshot CreateNocSnapshot(
        int level, long xp, long xpToNext, int mana, int maxMana, int raceId = 0,
        int? classId = null, int skillPoints = 0, IReadOnlyList<int>? skills = null,
        IReadOnlyList<NocMimicState>? mimics = null, IReadOnlyList<int>? mimicEquipped = null, int mimicSlots = 0,
        int? factionId = null, IReadOnlyList<NocReputation>? reputations = null,
        IReadOnlyList<NocQuestState>? quests = null, IReadOnlyList<int>? questsDone = null) {
        var snapshot = new NocSnapshot {
            Level = level,
            Xp = xp,
            XpToNext = xpToNext,
            Mana = mana,
            MaxMana = maxMana,
            RaceId = raceId,
            SkillPoints = skillPoints,
        };
        if (classId.HasValue) {
            snapshot.ClassId = classId.Value;
        }

        if (skills is not null) {
            snapshot.Skills.AddRange(skills);
        }

        if (mimics is not null) {
            snapshot.Mimics.AddRange(mimics);
        }

        if (mimicEquipped is not null) {
            snapshot.MimicEquipped.AddRange(mimicEquipped);
        }

        snapshot.MimicSlots = mimicSlots;
        if (factionId.HasValue) {
            snapshot.FactionId = factionId.Value;
        }

        if (reputations is not null) {
            snapshot.Reputations.AddRange(reputations);
        }

        if (quests is not null) {
            snapshot.Quests.AddRange(quests);
        }

        if (questsDone is not null) {
            snapshot.QuestsDone.AddRange(questsDone);
        }

        return snapshot;
    }

    public static ServerMessage CreateNocActionRefused(string reason) {
        return new ServerMessage {
            NocActionRefused = new NocActionRefused {
                Reason = reason ?? "",
            },
        };
    }

    public static ServerMessage CreateNocStateUpdated(NocSnapshot state) {
        ArgumentNullException.ThrowIfNull(state);
        return new ServerMessage {
            NocStateUpdated = new NocStateUpdated {
                State = state,
            },
        };
    }

    public static ServerMessage CreateHpUpdated(int hp, int maxHp) {
        return new ServerMessage {
            HpUpdated = new HpUpdated {
                Hp = hp,
                MaxHp = maxHp,
            },
        };
    }

    public static ServerMessage CreatePlayerDied(long playerId, int x, int y) {
        return new ServerMessage {
            PlayerDied = new PlayerDied {
                PlayerId = playerId,
                X = x,
                Y = y,
            },
        };
    }

    public static ServerMessage CreatePlayerResurrected(long playerId, int x, int y, int hp, int maxHp) {
        return new ServerMessage {
            PlayerResurrected = new PlayerResurrected {
                PlayerId = playerId,
                X = x,
                Y = y,
                Hp = hp,
                MaxHp = maxHp,
            },
        };
    }

    /// <summary>Sent when a pending timed logout is cleared server-side (e.g. combat damage).</summary>
    public static ServerMessage CreateLogoutCancelled() {
        return new ServerMessage {
            LogoutCancelled = new LogoutCancelled(),
        };
    }

    public static ServerMessage CreatePingResponse(uint sequence, int gameWorldQueueLength = 0, int playersInMap = 0, double pingVariance = 0) {
        return new ServerMessage {
            PingResponse = new PingResponse {
                Sequence = sequence,
                GameWorldQueueLength = gameWorldQueueLength,
                PlayersInMap = playersInMap,
                PingVariance = pingVariance,
            },
        };
    }

    public static ServerMessage CreateResetPosition(string gameWorldId, int x, int y, int remainingStunlockMs = 0) {
        return new ServerMessage {
            ResetPosition = new ResetPosition {
                X = x,
                Y = y,
                GameWorldId = gameWorldId,
                RemainingStunlockMs = remainingStunlockMs,
            },
        };
    }

    public static ServerMessage CreatePositionCorrected(string gameWorldId, int curX, int curY, int destX, int destY) {
        return new ServerMessage {
            PositionCorrected = new PositionCorrected {
                CurX = curX,
                CurY = curY,
                DestX = destX,
                DestY = destY,
                GameWorldId = gameWorldId,
            },
        };
    }

    public static ServerMessage CreatePlayerMovementStateChanged(long playerId, bool runningMode, int movementSpeedMs) {
        return new ServerMessage {
            PlayerMovementStateChanged = new PlayerMovementStateChanged {
                PlayerId = playerId,
                RunningMode = runningMode,
                MovementSpeedMs = movementSpeedMs,
            },
        };
    }

    public static ServerMessage CreatePlayerAttackModeChanged(long playerId, bool attackMode) {
        return new ServerMessage {
            PlayerAttackModeChanged = new PlayerAttackModeChanged {
                PlayerId = playerId,
                AttackMode = attackMode,
            },
        };
    }

    public static ServerMessage CreatePlayerIdleDirectionChanged(long playerId, int direction) {
        return new ServerMessage {
            PlayerIdleDirectionChanged = new PlayerIdleDirectionChanged {
                PlayerId = playerId,
                Direction = direction,
            },
        };
    }

    /// <summary>Notifies nearby clients that a player performed a pickup (animation sync); <paramref name="animationTimeMs"/> is the full configured animation duration.</summary>
    public static ServerMessage CreatePlayerPickupPerformed(long playerId, int direction, int animationTimeMs) {
        return new ServerMessage {
            PlayerPickupPerformed = new PlayerPickupPerformed {
                PlayerId = playerId,
                Direction = direction,
                AnimationTimeMs = animationTimeMs,
            },
        };
    }

    /// <summary>Notifies nearby clients that a player entered bow stance (peace mode); <paramref name="animationTimeMs"/> is the full configured duration.</summary>
    public static ServerMessage CreatePlayerBowStancePerformed(long playerId, int direction, int animationTimeMs) {
        return new ServerMessage {
            PlayerBowStancePerformed = new PlayerBowStancePerformed {
                PlayerId = playerId,
                Direction = direction,
                AnimationTimeMs = animationTimeMs,
            },
        };
    }

    public static ServerMessage CreateSpellCastStarted(long playerId, string spellName, int castSpeedMs) {
        return new ServerMessage {
            SpellCastStarted = new SpellCastStarted {
                PlayerId = playerId,
                SpellName = spellName,
                CastSpeedMs = castSpeedMs,
            },
        };
    }

    public static ServerMessage CreateSpellCastCancelled(long playerId) {
        return new ServerMessage {
            SpellCastCancelled = new SpellCastCancelled {
                PlayerId = playerId,
            },
        };
    }

    /// <summary>Self only: cast request violated minimum interval after cast start.</summary>
    public static ServerMessage CreateSpellCastFailed() {
        return new ServerMessage {
            SpellCastFailed = new SpellCastFailed(),
        };
    }

    public static ServerMessage CreateCastAoeSpell(long playerId, int spellId, int x, int y) {
        return new ServerMessage {
            CastAoeSpell = new CastAoeSpell {
                PlayerId = playerId,
                SpellId = spellId,
                X = x,
                Y = y,
            },
        };
    }

    public static ServerMessage CreateCastDirectionalAoeSpell(long playerId, int spellId, int casterX, int casterY, int targetX, int targetY) {
        return new ServerMessage {
            CastDirectionalAoeSpell = new CastDirectionalAoeSpell {
                PlayerId = playerId,
                SpellId = spellId,
                CasterX = casterX,
                CasterY = casterY,
                TargetX = targetX,
                TargetY = targetY,
            },
        };
    }

    /// <summary>Server-authoritative AoE spell cast from a monster; <paramref name="monsterId"/> identifies the caster for client VFX.</summary>
    public static ServerMessage CreateMonsterCastAoeSpell(long monsterId, int spellId, int x, int y) {
        return new ServerMessage {
            MonsterCastAoeSpell = new MonsterCastAoeSpell {
                MonsterId = monsterId,
                SpellId = spellId,
                X = x,
                Y = y,
            },
        };
    }

    /// <summary>Server-authoritative directional AoE spell cast from a monster; positions are grid cells.</summary>
    public static ServerMessage CreateMonsterCastDirectionalAoeSpell(long monsterId, int spellId, int casterX, int casterY, int targetX, int targetY) {
        return new ServerMessage {
            MonsterCastDirectionalAoeSpell = new MonsterCastDirectionalAoeSpell {
                MonsterId = monsterId,
                SpellId = spellId,
                CasterX = casterX,
                CasterY = casterY,
                TargetX = targetX,
                TargetY = targetY,
            },
        };
    }

    /// <summary>One-shot VFX at a grid cell; <paramref name="effectKey"/> matches client <c>Effects.ts</c> keys.</summary>
    public static ServerMessage CreateCastEffect(string gameWorldId, string effectKey, int x, int y) {
        return new ServerMessage {
            CastEffect = new CastEffect {
                GameWorldId = gameWorldId,
                EffectKey = effectKey,
                X = x,
                Y = y,
            },
        };
    }

    public static ServerMessage CreatePlayerDisconnected(long playerId) {
        return new ServerMessage {
            PlayerDisconnected = new PlayerDisconnected {
                PlayerId = playerId,
            },
        };
    }

    public static ServerMessage CreatePlayerReconnected(long playerId) {
        return new ServerMessage {
            PlayerReconnected = new PlayerReconnected {
                PlayerId = playerId,
            },
        };
    }

    public static ServerMessage CreateSendMessage(string message) {
        return new ServerMessage {
            SendMessage = new SendMessage {
                Message = message,
            },
        };
    }

    public static ServerMessage CreateChatMessageReceived(string senderCharacterName, long timestampMs, string message) {
        return new ServerMessage {
            ChatMessageReceived = new ChatMessageReceived {
                SenderCharacterName = senderCharacterName,
                TimestampMs = timestampMs,
                Message = message,
            },
        };
    }

    public static ServerMessage CreatePlayerParalyzed(int durationSeconds) {
        return new ServerMessage {
            PlayerParalyzed = new PlayerParalyzed {
                DurationSeconds = durationSeconds,
            },
        };
    }

    public static ServerMessage CreateSpawnProtectionEnabled(long playerId) {
        return new ServerMessage {
            SpawnProtectionEnabled = new SpawnProtectionEnabled {
                PlayerId = playerId,
            },
        };
    }

    public static ServerMessage CreateSpawnProtectionDisabled(long playerId) {
        return new ServerMessage {
            SpawnProtectionDisabled = new SpawnProtectionDisabled {
                PlayerId = playerId,
            },
        };
    }
}
