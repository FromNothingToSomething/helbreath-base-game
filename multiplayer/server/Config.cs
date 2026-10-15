using System.Text.Json;
using Mmorpg.Network;
using Server.Nocthemar.Data;
using Server.Nocthemar.Persistence;
using Server.Utils;

namespace Server;

/// <summary>
/// Loads JSON configuration from the <c>Config/</c> directory next to the process working directory.
/// Validates <see cref="SettingsConfig"/> invariants after deserialization.
/// </summary>
public static class Config {
    /// <summary>Deserializer options shared by all config file loads.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Reads a JSON file from <c>Config/{fileName}</c> and deserializes it, or throws if null.</summary>
    private static async Task<T> LoadJsonAsync<T>(string fileName, string description) where T : class {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "Config", fileName);
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to load {description} from {path}.");
    }

    public static Task<GameWorldConfig[]> LoadGameWorldsConfig() =>
        LoadJsonAsync<GameWorldConfig[]>("GameWorlds.json", "game worlds");

    public static Task<MonsterConfig[]> LoadMonstersConfig() =>
        LoadJsonAsync<MonsterConfig[]>("Monsters.json", "monsters");

    public static Task<SpellConfig[]> LoadSpellsConfig() =>
        LoadJsonAsync<SpellConfig[]>("Spells.json", "spells");

    public static Task<ItemConfig[]> LoadItemsConfig() =>
        LoadJsonAsync<ItemConfig[]>("Items.json", "items");

    public static Task<NpcConfig[]> LoadNpcsConfig() =>
        LoadJsonAsync<NpcConfig[]>("NPCs.json", "npcs");

    /// <summary>Validates NPC catalog entries and builds id lookup for summon and <see cref="Mmorpg.Network.InitialState"/> directory.</summary>
    public static IReadOnlyDictionary<int, NpcConfig> BuildNpcCatalog(NpcConfig[] npcs) {
        ArgumentNullException.ThrowIfNull(npcs);
        if (npcs.Length == 0) {
            return new Dictionary<int, NpcConfig>();
        }

        var idSet = new HashSet<int>();
        for (var i = 0; i < npcs.Length; i++) {
            var n = npcs[i];
            if (string.IsNullOrWhiteSpace(n.Name)) {
                throw new InvalidOperationException($"NPCs.json entry at index {i} has an empty name.");
            }
            if (!idSet.Add(n.Id)) {
                throw new InvalidOperationException($"Duplicate NPC id {n.Id} in NPCs.json.");
            }
        }

        for (var i = 0; i < npcs.Length; i++) {
            if (!idSet.Contains(i)) {
                throw new InvalidOperationException(
                    $"NPCs.json ids must be the contiguous range 0..{npcs.Length - 1} (missing id {i}).");
            }
        }

        return npcs.ToDictionary(n => n.Id);
    }

    /// <summary>Validates monster entries and builds sprite and numeric-id lookup tables used by summon and dwell spawning.</summary>
    public static (IReadOnlyDictionary<string, MonsterConfig> BySprite, IReadOnlyDictionary<int, MonsterConfig> ById) BuildMonsterCatalog(MonsterConfig[] monsters) {
        ArgumentNullException.ThrowIfNull(monsters);
        if (monsters.Length == 0) {
            return (
                new Dictionary<string, MonsterConfig>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<int, MonsterConfig>());
        }

        var idSet = new HashSet<int>();
        for (var i = 0; i < monsters.Length; i++) {
            var m = monsters[i];
            if (string.IsNullOrWhiteSpace(m.Name)) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has an empty name.");
            }
            if (string.IsNullOrWhiteSpace(m.Sprite)) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has an empty sprite.");
            }
            if (m.MovementSpeed < 0) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has negative movementSpeed ({m.MovementSpeed}).");
            }
            if (!idSet.Add(m.Id)) {
                throw new InvalidOperationException($"Duplicate monster id {m.Id} in Monsters.json.");
            }
            if (m.Xp is int xp && xp < 0) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has negative xp ({xp}).");
            }
            if (m.FactionId is int fid && fid < 0) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has negative factionId ({fid}).");
            }
            if (m.RepOnKill is int rep && (rep < 0 || rep > MaxRepOnKill)) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has repOnKill ({rep}) outside 0-{MaxRepOnKill}.");
            }
            if (m.RepOnKill is not null && m.FactionId is null) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has repOnKill but no factionId.");
            }
            if (m.ChaseDistance is int cd && cd < 0) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has negative chaseDistance ({cd}).");
            }
            if (m.ChaseMaxDistance is int cmd && cmd < 0) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has negative chaseMaxDistance ({cmd}).");
            }
            if (m.AttackRange is int ar && ar < 0) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has negative attackRange ({ar}).");
            }
            if (m.AttackSpeed is int asp && asp <= 0) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has non-positive attackSpeed ({asp}).");
            }
            if (m.AttackDamageMin is int dmin && m.AttackDamageMax is int dmax && dmin > dmax) {
                throw new InvalidOperationException(
                    $"Monsters.json entry at index {i} has attackDamageMin ({dmin}) greater than attackDamageMax ({dmax}).");
            }
            if (m.AttackRecoveryTime is int art && art < 0) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has negative attackRecoveryTime ({art}).");
            }
            if (m.MinIdleTime is int mint && mint < 0) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has negative minIdleTime ({mint}).");
            }
            if (m.MaxIdleTime is int maxit && maxit < 0) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has negative maxIdleTime ({maxit}).");
            }
            if (m.MinIdleTime is int mint2 && m.MaxIdleTime is int maxit2 && mint2 > maxit2) {
                throw new InvalidOperationException(
                    $"Monsters.json entry at index {i} has minIdleTime ({mint2}) greater than maxIdleTime ({maxit2}).");
            }
            if (m.AttackType is int aty && aty != (int)AttackType.NoInterrupt && aty != (int)AttackType.Interrupt && aty != (int)AttackType.Stun && aty != (int)AttackType.Knockback) {
                throw new InvalidOperationException(
                    $"Monsters.json entry at index {i} has attackType ({aty}); expected {(int)AttackType.NoInterrupt}, {(int)AttackType.Interrupt}, {(int)AttackType.Stun}, or {(int)AttackType.Knockback}.");
            }
            if (m.Allegiance is int allegiance &&
                allegiance != (int)MonsterAllegiance.Hostile &&
                allegiance != (int)MonsterAllegiance.Neutral &&
                allegiance != (int)MonsterAllegiance.Friendly) {
                throw new InvalidOperationException(
                    $"Monsters.json entry at index {i} has allegiance ({allegiance}); expected {(int)MonsterAllegiance.Hostile}, {(int)MonsterAllegiance.Neutral}, or {(int)MonsterAllegiance.Friendly}.");
            }
            if (m.AttackStunDuration is int asd && asd < 0) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has negative attackStunDuration ({asd}).");
            }
            if (m.Hp is int hpp && hpp <= 0) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has non-positive hp ({hpp}).");
            }
            if (m.CorpseDecayTime is int cdt && cdt < 0) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has negative corpseDecayTime ({cdt}).");
            }
            if (m.RespawnTime is int rt && rt < 0) {
                throw new InvalidOperationException($"Monsters.json entry at index {i} has negative respawnTime ({rt}).");
            }
            if (m.Spells is { Length: > 0 } spellList) {
                for (var s = 0; s < spellList.Length; s++) {
                    var e = spellList[s];
                    if (e.CastProbability < 0 || e.CastProbability > 1) {
                        throw new InvalidOperationException(
                            $"Monsters.json entry at index {i} spells[{s}] has castProbability ({e.CastProbability}); expected 0..1.");
                    }
                }
            }
        }

        for (var i = 0; i < monsters.Length; i++) {
            if (!idSet.Contains(i)) {
                throw new InvalidOperationException(
                    $"Monsters.json ids must be the contiguous range 0..{monsters.Length - 1} (missing id {i}).");
            }
        }

        var byId = monsters.ToDictionary(m => m.Id);
        var bySprite = monsters
            .GroupBy(m => m.Sprite.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return (bySprite, byId);
    }

    /// <summary>Highest <c>repOnKill</c> a monster may declare.</summary>
    public const int MaxRepOnKill = 1000;

    /// <summary>Ensures each monster <c>factionId</c> is a faction of <c>Factions.json</c>.</summary>
    public static void ValidateMonsterFactionReferences(IReadOnlyDictionary<int, MonsterConfig> monstersById, FactionConfig factions) {
        ArgumentNullException.ThrowIfNull(monstersById);
        ArgumentNullException.ThrowIfNull(factions);
        foreach (var monster in monstersById.Values) {
            if (monster.FactionId is int factionId && !factions.Contains(factionId)) {
                throw new InvalidOperationException($"Monsters.json entry id {monster.Id} references unknown factionId {factionId}.");
            }
        }
    }

    /// <summary>Ensures each monster <c>spells</c> entry references a defined spell and rejects <see cref="DamageType.GroundEffect"/> (monster ground effects are not implemented).</summary>
    public static void ValidateMonsterSpellReferences(IReadOnlyDictionary<int, MonsterConfig> monstersById, IReadOnlyDictionary<int, SpellConfig> spellsById) {
        ArgumentNullException.ThrowIfNull(monstersById);
        ArgumentNullException.ThrowIfNull(spellsById);
        foreach (var kv in monstersById) {
            var m = kv.Value;
            if (m.Spells is not { Length: > 0 } spellList) {
                continue;
            }

            for (var s = 0; s < spellList.Length; s++) {
                var e = spellList[s];
                if (!spellsById.TryGetValue(e.SpellId, out var spell)) {
                    throw new InvalidOperationException(
                        $"Monsters.json entry id {m.Id} spells[{s}] references unknown spellId {e.SpellId}.");
                }

                if (!spell.DamageType.HasValue) {
                    throw new InvalidOperationException(
                        $"Monsters.json entry id {m.Id} spells[{s}] uses buff spell {e.SpellId} ({spell.Name}); monster spells cannot use buff spells.");
                }

                if (spell.DamageType == (int)DamageType.GroundEffect) {
                    throw new InvalidOperationException(
                        $"Monsters.json entry id {m.Id} spells[{s}] uses spellId {e.SpellId} ({spell.Name}) with GroundEffect; monster spells cannot use ground-effect spells.");
                }
            }
        }
    }

    /// <summary>Validates optional <c>movementSpeedModifier</c> / <c>attackSpeedModifier</c> / <c>castSpeedModifier</c> on a spell temporary-effect row.</summary>
    private static void ValidateSpellTimedEffectSpeedModifiers(int spellIndex, int spellId, SpellTimedEffectSpec row) {
        CheckOptionalSpeedModifier(spellIndex, spellId, "movementSpeedModifier", row.MovementSpeedModifier);
        CheckOptionalSpeedModifier(spellIndex, spellId, "attackSpeedModifier", row.AttackSpeedModifier);
        CheckOptionalSpeedModifier(spellIndex, spellId, "castSpeedModifier", row.CastSpeedModifier);
    }

    private static void CheckOptionalSpeedModifier(int spellIndex, int spellId, string jsonName, double? value) {
        if (!value.HasValue) {
            return;
        }

        var v = value.Value;
        if (double.IsNaN(v) || double.IsInfinity(v)) {
            throw new InvalidOperationException(
                $"Spells.json entry at index {spellIndex} (id {spellId}) temporaryEffects has non-finite {jsonName}.");
        }

        if (v < TemporaryEffectSpeedModifierMath.MinModifier || v > TemporaryEffectSpeedModifierMath.MaxModifier) {
            throw new InvalidOperationException(
                $"Spells.json entry at index {spellIndex} (id {spellId}) temporaryEffects {jsonName} ({v}) must be between {TemporaryEffectSpeedModifierMath.MinModifier} and {TemporaryEffectSpeedModifierMath.MaxModifier}.");
        }
    }

    /// <summary>Validates spell entries and builds an id lookup table used by spell casting handlers and server config packets.</summary>
    public static IReadOnlyDictionary<int, SpellConfig> BuildSpellCatalog(SpellConfig[] spells) {
        ArgumentNullException.ThrowIfNull(spells);
        if (spells.Length == 0) {
            return new Dictionary<int, SpellConfig>();
        }

        var idSet = new HashSet<int>();
        for (var i = 0; i < spells.Length; i++) {
            var spell = spells[i];
            if (string.IsNullOrWhiteSpace(spell.Name)) {
                throw new InvalidOperationException($"Spells.json entry at index {i} has an empty name.");
            }
            if (!idSet.Add(spell.Id)) {
                throw new InvalidOperationException($"Duplicate spell id {spell.Id} in Spells.json.");
            }
            if (!spell.DamageType.HasValue) {
                if (spell.TemporaryEffects is not { Length: > 0 } buffRows) {
                    throw new InvalidOperationException(
                        $"Spells.json entry at index {i} (id {spell.Id}) has no damageType and must define temporaryEffects.");
                }
                foreach (var row in buffRows) {
                    ValidateSpellTimedEffectSpeedModifiers(i, spell.Id, row);
                    if (row.Group < 0) {
                        throw new InvalidOperationException(
                            $"Spells.json entry at index {i} (id {spell.Id}) has negative group ({row.Group}).");
                    }
                    if (row.Duration <= 0) {
                        throw new InvalidOperationException(
                            $"Spells.json entry at index {i} (id {spell.Id}) temporary effect must define positive duration (ms).");
                    }
                }
                if (spell.AoeRadius is not null) {
                    throw new InvalidOperationException(
                        $"Spells.json entry at index {i} is a buff spell and must not define aoeRadius.");
                }
            } else if (spell.DamageType != (int)DamageType.RectangleAoe &&
                spell.DamageType != (int)DamageType.ConeAoe &&
                spell.DamageType != (int)DamageType.LinearAoe &&
                spell.DamageType != (int)DamageType.SingleCell &&
                spell.DamageType != (int)DamageType.GroundEffect) {
                throw new InvalidOperationException(
                    $"Spells.json entry at index {i} has damageType ({spell.DamageType}); expected {(int)DamageType.RectangleAoe}, {(int)DamageType.ConeAoe}, {(int)DamageType.LinearAoe}, {(int)DamageType.SingleCell}, or {(int)DamageType.GroundEffect}.");
            }
            if (spell.DamageType.HasValue && spell.TemporaryEffects is not null) {
                foreach (var row in spell.TemporaryEffects) {
                    ValidateSpellTimedEffectSpeedModifiers(i, spell.Id, row);
                    if (row.Type == (int)TemporaryEffectType.Invisibility) {
                        throw new InvalidOperationException(
                            $"Spells.json entry at index {i} (id {spell.Id}) must not define Invisibility in temporaryEffects on a damage spell.");
                    }
                    if (row.Type != (int)TemporaryEffectType.Chill) {
                        throw new InvalidOperationException(
                            $"Spells.json entry at index {i} (id {spell.Id}) has unsupported on-hit temporary effect type {row.Type}.");
                    }
                    if (row.Group < 0) {
                        throw new InvalidOperationException(
                            $"Spells.json entry at index {i} (id {spell.Id}) has negative group ({row.Group}).");
                    }
                    if (row.Duration <= 0) {
                        throw new InvalidOperationException(
                            $"Spells.json entry at index {i} (id {spell.Id}) temporary effect must define positive duration (ms).");
                    }
                }
            }
            if (spell.AoeRadius is int aoeRadius && aoeRadius < 0) {
                throw new InvalidOperationException($"Spells.json entry at index {i} has negative aoeRadius ({aoeRadius}).");
            }
            if (spell.Group is int group && group < 0) {
                throw new InvalidOperationException($"Spells.json entry at index {i} has negative group ({group}).");
            }
            if (spell.ProjectileSpeed is int projectileSpeed && projectileSpeed <= 0) {
                throw new InvalidOperationException($"Spells.json entry at index {i} has non-positive projectileSpeed ({projectileSpeed}).");
            }
            if (spell.ProjectileDistance is int projectileDistance && projectileDistance <= 0) {
                throw new InvalidOperationException($"Spells.json entry at index {i} has non-positive projectileDistance ({projectileDistance}).");
            }
            if (spell.ProjectileDistance is not null && spell.ProjectileSpeed is null) {
                throw new InvalidOperationException(
                    $"Spells.json entry at index {i} defines projectileDistance but does not define projectileSpeed (required for travel-time delay).");
            }
            if (spell.EmissionSteps is int emissionSteps && emissionSteps <= 0) {
                throw new InvalidOperationException($"Spells.json entry at index {i} has non-positive emissionSteps ({emissionSteps}).");
            }
            if (spell.StartRadius is int startRadius && startRadius < 0) {
                throw new InvalidOperationException($"Spells.json entry at index {i} has negative startRadius ({startRadius}).");
            }
            if (spell.EndRadius is int endRadius && endRadius < 0) {
                throw new InvalidOperationException($"Spells.json entry at index {i} has negative endRadius ({endRadius}).");
            }
            if (spell.StartRadius is int startRadiusValue &&
                spell.EndRadius is int endRadiusValue &&
                startRadiusValue > endRadiusValue) {
                throw new InvalidOperationException(
                    $"Spells.json entry at index {i} has startRadius ({startRadiusValue}) greater than endRadius ({endRadiusValue}).");
            }
            if (spell.StartShards is int startShards && startShards < 0) {
                throw new InvalidOperationException($"Spells.json entry at index {i} has negative startShards ({startShards}).");
            }
            if (spell.EndShards is int endShards && endShards < 0) {
                throw new InvalidOperationException($"Spells.json entry at index {i} has negative endShards ({endShards}).");
            }

            if (spell.DamageType == (int)DamageType.ConeAoe) {
                if (spell.ProjectileSpeed is null ||
                    spell.EmissionSteps is null ||
                    spell.StartRadius is null ||
                    spell.EndRadius is null ||
                    spell.StartShards is null ||
                    spell.EndShards is null) {
                    throw new InvalidOperationException(
                        $"Spells.json entry at index {i} uses ConeAoe and must define projectileSpeed, emissionSteps, startRadius, endRadius, startShards, and endShards.");
                }
            }

            if (spell.DamageType == (int)DamageType.LinearAoe) {
                if (spell.Duration is null) {
                    throw new InvalidOperationException(
                        $"Spells.json entry at index {i} uses LinearAoe and must define duration (ms).");
                }
                if (spell.Duration <= 0) {
                    throw new InvalidOperationException($"Spells.json entry at index {i} has non-positive duration ({spell.Duration}).");
                }
            }

            if (spell.DamageType == (int)DamageType.SingleCell) {
                if (spell.AoeRadius is not null) {
                    throw new InvalidOperationException(
                        $"Spells.json entry at index {i} uses SingleCell and must not define aoeRadius.");
                }
                if (spell.Duration is not null) {
                    throw new InvalidOperationException(
                        $"Spells.json entry at index {i} uses SingleCell and must not define duration (not a linear AoE spell).");
                }
            }

            if (spell.DamageType == (int)DamageType.GroundEffect) {
                if (spell.Group is null) {
                    throw new InvalidOperationException(
                        $"Spells.json entry at index {i} uses GroundEffect and must define group.");
                }
                if (spell.Duration is null) {
                    throw new InvalidOperationException(
                        $"Spells.json entry at index {i} uses GroundEffect and must define duration (ms).");
                }
                var duration = spell.Duration.Value;
                if (duration <= 0) {
                    throw new InvalidOperationException($"Spells.json entry at index {i} has non-positive duration ({spell.Duration}).");
                }
                if (spell.TickRate is int tickRate) {
                    if (tickRate <= 0) {
                        throw new InvalidOperationException($"Spells.json entry at index {i} has non-positive tickRate ({spell.TickRate}).");
                    }
                    if (duration % tickRate != 0) {
                        throw new InvalidOperationException(
                            $"Spells.json entry at index {i} uses GroundEffect and must define duration divisible by tickRate.");
                    }
                }
            }

            if (spell.AttackType is int saty &&
                saty != (int)AttackType.NoInterrupt &&
                saty != (int)AttackType.Interrupt &&
                saty != (int)AttackType.Stun &&
                saty != (int)AttackType.Knockback) {
                throw new InvalidOperationException(
                    $"Spells.json entry at index {i} has attackType ({saty}); expected {(int)AttackType.NoInterrupt}, {(int)AttackType.Interrupt}, {(int)AttackType.Stun}, or {(int)AttackType.Knockback}.");
            }
        }

        return spells.ToDictionary(spell => spell.Id);
    }

    /// <summary>Validates item entries and builds an id lookup table used for <see cref="Mmorpg.Network.InitialState"/> item directory payloads.</summary>
    public static IReadOnlyDictionary<int, ItemConfig> BuildItemCatalog(ItemConfig[] items) {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Length == 0) {
            return new Dictionary<int, ItemConfig>();
        }

        var idSet = new HashSet<int>();
        for (var i = 0; i < items.Length; i++) {
            var item = items[i];
            if (string.IsNullOrWhiteSpace(item.Name)) {
                throw new InvalidOperationException($"Items.json entry at index {i} has an empty name.");
            }
            if (string.IsNullOrWhiteSpace(item.ItemType)) {
                throw new InvalidOperationException($"Items.json entry at index {i} has an empty itemType.");
            }
            if (!idSet.Add(item.Id)) {
                throw new InvalidOperationException($"Duplicate item id {item.Id} in Items.json.");
            }
            if (item.WeaponType is int wt &&
                wt != (int)ItemWeaponType.Melee &&
                wt != (int)ItemWeaponType.Bow) {
                throw new InvalidOperationException(
                    $"Items.json entry id {item.Id} has weaponType {wt}; expected {(int)ItemWeaponType.Melee} or {(int)ItemWeaponType.Bow}.");
            }
            if (item.Gender is int g && g is not (0 or 1)) {
                throw new InvalidOperationException(
                    $"Items.json entry id {item.Id} has gender {g}; expected 0 (male) or 1 (female).");
            }
        }

        return items.ToDictionary(entry => entry.Id);
    }

    /// <summary>Ensures dwell area entries reference catalog ids and sane counts before worlds are constructed.</summary>
    public static void ValidateGameWorldDwellAreas(GameWorldConfig gw, IReadOnlyDictionary<int, MonsterConfig> monstersById) {
        ArgumentNullException.ThrowIfNull(gw);
        ArgumentNullException.ThrowIfNull(monstersById);
        if (gw.DwellAreas is null || gw.DwellAreas.Length == 0) {
            return;
        }

        for (var i = 0; i < gw.DwellAreas.Length; i++) {
            var d = gw.DwellAreas[i];
            if (d.Count < 1) {
                throw new InvalidOperationException(
                    $"Game world '{gw.Id}' dwellAreas[{i}].count must be at least 1.");
            }
            if (!monstersById.ContainsKey(d.MonsterId)) {
                throw new InvalidOperationException(
                    $"Game world '{gw.Id}' dwellAreas[{i}].monsterId {d.MonsterId} is not defined in Monsters.json.");
            }
        }
    }

    /// <summary>Validates optional <c>npcs</c> entries: catalog id, facing 0–7, and unique grid cells per world.</summary>
    public static void ValidateGameWorldNpcPlacements(GameWorldConfig gw, IReadOnlyDictionary<int, NpcConfig> npcsById) {
        ArgumentNullException.ThrowIfNull(gw);
        ArgumentNullException.ThrowIfNull(npcsById);
        if (gw.Npcs is null || gw.Npcs.Length == 0) {
            return;
        }

        var seen = new HashSet<(int X, int Y)>();
        for (var i = 0; i < gw.Npcs.Length; i++) {
            var p = gw.Npcs[i];
            if (p.Direction < 0 || p.Direction > 7) {
                throw new InvalidOperationException(
                    $"Game world '{gw.Id}' npcs[{i}].direction must be 0–7 (got {p.Direction}).");
            }

            if (!npcsById.ContainsKey(p.NpcId)) {
                throw new InvalidOperationException(
                    $"Game world '{gw.Id}' npcs[{i}].npcId {p.NpcId} is not defined in NPCs.json.");
            }

            if (!seen.Add((p.X, p.Y))) {
                throw new InvalidOperationException(
                    $"Game world '{gw.Id}' has two or more npcs at ({p.X}, {p.Y}).");
            }
        }
    }

    /// <summary>Rejects NPC placements on cells listed in <see cref="GameWorldConfig.TeleportLocs"/> (server-declared teleport sources).</summary>
    public static void ValidateGameWorldNpcNotOnTeleportCells(GameWorldConfig gw) {
        ArgumentNullException.ThrowIfNull(gw);
        if (gw.Npcs is null || gw.Npcs.Length == 0) {
            return;
        }

        if (gw.TeleportLocs is null || gw.TeleportLocs.Length == 0) {
            return;
        }

        var teleportCells = new HashSet<(int X, int Y)>();
        foreach (var tl in gw.TeleportLocs) {
            foreach (var loc in tl.Locs) {
                teleportCells.Add((loc.X, loc.Y));
            }
        }

        for (var i = 0; i < gw.Npcs.Length; i++) {
            var p = gw.Npcs[i];
            if (teleportCells.Contains((p.X, p.Y))) {
                throw new InvalidOperationException(
                    $"Game world '{gw.Id}' npcs[{i}] at ({p.X}, {p.Y}) conflicts with a teleport source cell.");
            }
        }
    }

    /// <summary>Ensures each configured NPC lies within the loaded map dimensions.</summary>
    public static void ValidateGameWorldNpcBounds(GameWorldConfig gw, GameWorldOccupancyTracker tracker) {
        ArgumentNullException.ThrowIfNull(gw);
        ArgumentNullException.ThrowIfNull(tracker);
        if (gw.Npcs is null || gw.Npcs.Length == 0) {
            return;
        }

        var maxX = tracker.SizeX - 1;
        var maxY = tracker.SizeY - 1;
        for (var i = 0; i < gw.Npcs.Length; i++) {
            var p = gw.Npcs[i];
            if (p.X < 0 || p.X > maxX || p.Y < 0 || p.Y > maxY) {
                throw new InvalidOperationException(
                    $"Game world '{gw.Id}' npcs[{i}] at ({p.X}, {p.Y}) is outside map bounds 0..{maxX} x 0..{maxY}.");
            }
        }
    }

    public static async Task<SettingsConfig> LoadSettings() {
        var settings = await LoadJsonAsync<SettingsConfig>("Settings.json", "settings");
        if (settings.Port is < 1 or > 65535) {
            throw new ArgumentOutOfRangeException(nameof(settings.Port), "Port must be between 1 and 65535 inclusive.");
        }
        if (settings.LogoutTime < 0) {
            throw new ArgumentOutOfRangeException(nameof(settings.LogoutTime), "Logout time must be zero or greater.");
        }
        ArgumentNullException.ThrowIfNull(settings.Threads);
        var th = settings.Threads;
        if (th.GameWorldWorkers < 1) {
            throw new ArgumentOutOfRangeException(nameof(th.GameWorldWorkers), "Game world workers must be at least 1.");
        }
        if (th.GlobalWorldWorkerThread is int globalWorldWorkerThread && globalWorldWorkerThread < 0) {
            throw new ArgumentOutOfRangeException(nameof(th.GlobalWorldWorkerThread), "Global world worker thread must be zero or greater when set.");
        }
        if (settings.ChatMessageMaxLength <= 0) {
            throw new ArgumentOutOfRangeException(nameof(settings.ChatMessageMaxLength), "Chat message max length must be greater than zero.");
        }
        ArgumentNullException.ThrowIfNull(settings.GameWorld);
        var gwHost = settings.GameWorld;
        if (gwHost.TickInterval <= 0) {
            throw new ArgumentOutOfRangeException(nameof(gwHost.TickInterval), "Game world tick interval must be greater than zero.");
        }
        if (gwHost.IncomingMessagesQueueSize <= 0) {
            throw new ArgumentOutOfRangeException(nameof(gwHost.IncomingMessagesQueueSize), "Game world incoming messages queue size must be greater than zero.");
        }
        if (gwHost.IncomingMessagesBatchSizePerDispatch <= 0) {
            throw new ArgumentOutOfRangeException(nameof(gwHost.IncomingMessagesBatchSizePerDispatch), "Game world incoming messages batch size per dispatch must be greater than zero.");
        }
        if (settings.MaxCellsJumpDistance < 0) {
            throw new ArgumentOutOfRangeException(nameof(settings.MaxCellsJumpDistance), "Max cells jump distance must be zero or greater.");
        }
        if (settings.MaxConsecutiveOutboundSendFailures < 0) {
            throw new ArgumentOutOfRangeException(
                nameof(settings.MaxConsecutiveOutboundSendFailures),
                "Max consecutive outbound send failures must be zero or greater (zero disables the circuit breaker).");
        }
        ArgumentNullException.ThrowIfNull(settings.Radius);
        var rad = settings.Radius;
        if (rad.ViewRadiusX < 0) {
            throw new ArgumentOutOfRangeException(nameof(rad.ViewRadiusX), "View radius X must be zero or greater.");
        }
        if (rad.ViewRadiusY < 0) {
            throw new ArgumentOutOfRangeException(nameof(rad.ViewRadiusY), "View radius Y must be zero or greater.");
        }
        if (rad.CameraRadiusX < 0) {
            throw new ArgumentOutOfRangeException(nameof(rad.CameraRadiusX), "Camera radius X must be zero or greater.");
        }
        if (rad.CameraRadiusY < 0) {
            throw new ArgumentOutOfRangeException(nameof(rad.CameraRadiusY), "Camera radius Y must be zero or greater.");
        }
        ArgumentNullException.ThrowIfNull(settings.Ping);
        var ping = settings.Ping;
        if (ping.VarianceSampleSize <= 0) {
            throw new ArgumentOutOfRangeException(nameof(ping.VarianceSampleSize), "Ping variance sample size must be greater than zero.");
        }
        if (ping.AllowedVariance < 0) {
            throw new ArgumentOutOfRangeException(nameof(ping.AllowedVariance), "Ping allowed variance must be zero or greater.");
        }
        if (ping.Timeout <= 0) {
            throw new ArgumentOutOfRangeException(nameof(ping.Timeout), "Ping timeout must be greater than zero.");
        }
        ArgumentNullException.ThrowIfNull(settings.Timings);
        var tm = settings.Timings;
        if (tm.DisconnectTime <= 0) {
            throw new ArgumentOutOfRangeException(nameof(tm.DisconnectTime), "Disconnect time must be greater than zero.");
        }
        if (tm.SpawnProtectionTime < 0) {
            throw new ArgumentOutOfRangeException(nameof(tm.SpawnProtectionTime), "Spawn protection time must be zero or greater.");
        }
        if (tm.KnockbackTimeMs <= 0) {
            throw new ArgumentOutOfRangeException(nameof(tm.KnockbackTimeMs), "Knockback time must be greater than zero.");
        }
        var vc = settings.MovementSpeedViolationsChecker;
        if (vc.Limit <= 0) {
            throw new ArgumentOutOfRangeException(nameof(vc.Limit), "Movement speed violation limit must be greater than zero.");
        }
        if (vc.Window <= 0) {
            throw new ArgumentOutOfRangeException(nameof(vc.Window), "Movement speed violation window must be greater than zero.");
        }
        if (vc.SegmentsPerWindow <= 0) {
            throw new ArgumentOutOfRangeException(nameof(vc.SegmentsPerWindow), "Movement speed violation segments per window must be greater than zero.");
        }
        if (vc.ParalysisDuration < 0) {
            throw new ArgumentOutOfRangeException(nameof(vc.ParalysisDuration), "Paralysis duration must be zero or greater.");
        }
        if (vc.MaxPingVariance < 0) {
            throw new ArgumentOutOfRangeException(nameof(vc.MaxPingVariance), "Max ping variance must be zero or greater.");
        }
        ArgumentNullException.ThrowIfNull(settings.Debug);
        ArgumentNullException.ThrowIfNull(settings.MonsterDefaults);
        var md = settings.MonsterDefaults;
        if (md.ChaseDistance < 0) {
            throw new ArgumentOutOfRangeException(nameof(md.ChaseDistance), "Default monster chase distance must be zero or greater.");
        }
        if (md.ChaseMaxDistance is int dmm && dmm < 0) {
            throw new ArgumentOutOfRangeException(nameof(md.ChaseMaxDistance), "Default monster chase max distance must be zero or greater when set.");
        }
        if (md.AttackSpeed <= 0) {
            throw new ArgumentOutOfRangeException(nameof(md.AttackSpeed), "Default monster attack speed must be greater than zero.");
        }
        if (md.AttackDamageMin > md.AttackDamageMax) {
            throw new ArgumentOutOfRangeException(
                nameof(md.AttackDamageMin),
                "Default monster attack damage min must not exceed default monster attack damage max.");
        }
        if (md.AttackRecoveryTime < 0) {
            throw new ArgumentOutOfRangeException(nameof(md.AttackRecoveryTime), "Default monster attack recovery time must be zero or greater.");
        }
        if (md.MinIdleTime < 0) {
            throw new ArgumentOutOfRangeException(nameof(md.MinIdleTime), "Default monster min idle time must be zero or greater.");
        }
        if (md.MaxIdleTime < 0) {
            throw new ArgumentOutOfRangeException(nameof(md.MaxIdleTime), "Default monster max idle time must be zero or greater.");
        }
        if (md.MinIdleTime > md.MaxIdleTime) {
            throw new ArgumentOutOfRangeException(
                nameof(md.MinIdleTime),
                "Default monster min idle time must not exceed default monster max idle time.");
        }
        if (tm.ArrowSpeed <= 0) {
            throw new ArgumentOutOfRangeException(nameof(tm.ArrowSpeed), "Arrow speed must be greater than zero.");
        }
        if (md.Hp <= 0) {
            throw new ArgumentOutOfRangeException(nameof(md.Hp), "Default monster HP must be greater than zero.");
        }
        if (md.CorpseDecayTime <= 0) {
            throw new ArgumentOutOfRangeException(nameof(md.CorpseDecayTime), "Default monster corpse decay time must be greater than zero.");
        }
        if (md.RespawnTime <= 0) {
            throw new ArgumentOutOfRangeException(nameof(md.RespawnTime), "Default monster respawn time must be greater than zero.");
        }
        if (tm.PlayerPickupAnimationTime <= 0) {
            throw new ArgumentOutOfRangeException(nameof(tm.PlayerPickupAnimationTime), "Player pickup animation time must be greater than zero.");
        }
        if (tm.PlayerBowAnimationTime <= 0) {
            throw new ArgumentOutOfRangeException(nameof(tm.PlayerBowAnimationTime), "Player bow animation time must be greater than zero.");
        }
        if (settings.MaxDroppedItemsInStack <= 0) {
            throw new ArgumentOutOfRangeException(nameof(settings.MaxDroppedItemsInStack), "Max dropped items in stack must be greater than zero.");
        }
        if (tm.BlizzardSpellDamageDelayMs < 0) {
            throw new ArgumentOutOfRangeException(nameof(tm.BlizzardSpellDamageDelayMs), "Blizzard spell damage delay must be zero or greater.");
        }
        if (tm.AntiHackTimingLagFactor < 0 || tm.AntiHackTimingLagFactor > 1) {
            throw new ArgumentOutOfRangeException(nameof(tm.AntiHackTimingLagFactor), "Anti-hack timing lag factor must be between zero and one inclusive.");
        }
        return settings;
    }
}

/// <summary>Single world-grid coordinate used in config-defined teleports.</summary>
public record WorldLocationConfig(int X, int Y);

/// <summary>Teleport destination world id plus preferred spawn cell in that world.</summary>
public record GameWorldTeleportTargetConfig(string WorldId, WorldLocationConfig Loc);

/// <summary>Config-defined mapping from one or more source cells to a destination world/cell.</summary>
public record GameWorldTeleportConfig(WorldLocationConfig[] Locs, GameWorldTeleportTargetConfig Target);

/// <summary>Resolved teleport destination with the target world's map asset name for client preloading.</summary>
public record GameWorldTeleportTarget(string WorldId, string MapName, WorldLocationConfig Loc);

/// <summary>Resolved teleport set for one world: many source cells that share one target destination.</summary>
public record GameWorldTeleportSet(WorldLocationConfig[] Locs, GameWorldTeleportTarget Target);

/// <summary>Server-authoritative NPC catalog entry for summon UI and validation; client maps <c>id</c> to sprite locally.</summary>
public record NpcConfig(int Id, string Name);

/// <summary>Optional spell cast entry for <see cref="MonsterConfig.Spells"/>: <c>spellId</c> matches <c>Spells.json</c>; <c>castProbability</c> is an independent roll each AI tick (0–1).</summary>
public record MonsterSpellEntry(int SpellId, double CastProbability);

/// <summary>Server-authoritative monster catalog entry: stable id for world dwell config, display name, client sprite id, default step duration in ms (0 = no movement; melee when a player is in range still applies), optional chase distance in Chebyshev cells (default at spawn when omitted), optional max chase follow distance in cells, optional melee attack range in cells (default 1 when omitted), optional attack animation duration in ms, optional damage roll bounds (defaults from settings when omitted), optional post-hit AI idle gate extension in ms (defaults from settings when omitted), optional wander rest duration bounds in ms (defaults from settings when omitted), optional hit mode (<see cref="AttackType"/>, default <see cref="AttackType.NoInterrupt"/>), optional auto-aggro allegiance (<see cref="MonsterAllegiance"/>, default <see cref="MonsterAllegiance.Hostile"/>), optional player stunlock duration in ms after a <see cref="AttackType.Stun"/> hit only (ignored for <see cref="AttackType.Interrupt"/>; default 100 when omitted; JSON <c>attackStunDuration</c>), optional ranged attacks (JSON <c>rangedAttack</c>) using <c>arrowSpeed</c> for damage delay, optional <see cref="Spells"/> for AI spell casts.</summary>
public record MonsterConfig(
    int Id,
    string Name,
    string Sprite,
    int MovementSpeed,
    int? ChaseDistance = null,
    int? ChaseMaxDistance = null,
    int? AttackRange = null,
    int? AttackSpeed = null,
    int? AttackDamageMin = null,
    int? AttackDamageMax = null,
    int? AttackRecoveryTime = null,
    int? MinIdleTime = null,
    int? MaxIdleTime = null,
    int? AttackType = null,
    int? Allegiance = null,
    int? AttackStunDuration = null,
    /// <summary>When true, damage to players uses arrow travel delay after half swing (see <c>arrowSpeed</c> in settings); clients show arrow VFX from <see cref="Mmorpg.Network.MonsterAttacked"/>.</summary>
    bool? RangedAttack = null,
    /// <summary>Max HP when omitted; uses <c>monsterDefaults.hp</c> in <c>Settings.json</c>.</summary>
    int? Hp = null,
    /// <summary>Corpse linger duration in ms before server removal when omitted; uses <c>monsterDefaults.corpseDecayTime</c> in <c>Settings.json</c>.</summary>
    int? CorpseDecayTime = null,
    /// <summary>Delay in ms before a dwell-spawned monster respawns after corpse removal when omitted; uses <c>monsterDefaults.respawnTime</c> in <c>Settings.json</c>.</summary>
    int? RespawnTime = null,
    /// <summary>Optional AI spell list; each entry references <c>Spells.json</c> by id (ground-effect spells are rejected at startup).</summary>
    MonsterSpellEntry[]? Spells = null,
    /// <summary>XP granted to the player who lands the killing blow; when omitted uses <c>defaultMonsterXp</c> in <c>Config/Nocthemar/Progression.json</c>. Must be zero or greater.</summary>
    int? Xp = null,
    /// <summary>Faction of the monster (<c>Factions.json</c> id); null = none. Killing it costs that faction reputation and pays its rivals, for a killer who joined a faction (JSON <c>factionId</c>).</summary>
    int? FactionId = null,
    /// <summary>Reputation of a kill, 0-1000; when omitted uses <c>reputation.killBase</c> in <c>Factions.json</c>. Needs <see cref="FactionId"/> (JSON <c>repOnKill</c>).</summary>
    int? RepOnKill = null);

/// <summary>Ranged vs melee for catalog <c>weaponType</c> (JSON and <see cref="Mmorpg.Network.ItemDirectoryEntry"/>).</summary>
public enum ItemWeaponType {
    Melee = 0,
    Bow = 1,
}

/// <summary>One effect row in <c>Items.json</c>; <c>effect</c> matches the client ItemEffect index (0 = STORM_BRINGER … 5 = TINT_APPEARANCE).</summary>
public record ItemEffectConfig(int Effect, int? EffectColor = null);

/// <summary>Server-authoritative item row from <c>Items.json</c> (stable id matches the client sprite registry).</summary>
public record ItemConfig(
    int Id,
    string Name,
    string ItemType,
    string[]? BlockedItemSlots = null,
    bool? Stackable = null,
    bool? Consumable = null,
    ItemEffectConfig[]? Effects = null,
    /// <summary>0 = melee, 1 = bow; omit from JSON for melee.</summary>
    int? WeaponType = null,
    /// <summary>When set, only this gender may equip the item; 0 = male, 1 = female (matches <see cref="Mmorpg.Network.PlayerGender"/>).</summary>
    int? Gender = null);

/// <summary>One timed effect row from <c>Spells.json</c> <c>temporaryEffects</c>; <c>duration</c> is ms. Optional modifiers are additive to 1 for speed: effective duration ms = base / (1 + sum(modifiers)); see <see cref="TemporaryEffectSpeedModifierMath"/>.</summary>
public record SpellTimedEffectSpec(
    int Type,
    int Group,
    int Duration,
    double? MovementSpeedModifier = null,
    double? AttackSpeedModifier = null,
    double? CastSpeedModifier = null);

/// <summary>Server-authoritative spell catalog entry loaded from <c>Spells.json</c>.</summary>
/// <remarks>For <see cref="DamageType.LinearAoe"/>, <c>projectileSpeed</c> is optional (omitted when the server does not need travel-time delay for damage; clients may use it for visuals when set). For <see cref="DamageType.SingleCell"/>, omit <c>aoeRadius</c> and <c>duration</c>; damage resolves immediately on cast. For <see cref="DamageType.GroundEffect"/>, define <c>group</c> and <c>duration</c>; <c>tickRate</c> is optional and, when set, makes the effect deal periodic damage. When <c>tickRate</c> is omitted, the effect is step-on-only until expiry. <c>aoeRadius</c> is optional and expands placement around the target cell. For <see cref="DamageType.RectangleAoe"/> with projectile-delayed damage, when <c>projectileDistance</c> is set, travel time uses that fixed pixel distance instead of caster-to-target distance. Optional <c>attackType</c> matches <see cref="AttackType"/> (default <see cref="AttackType.Interrupt"/> when omitted). <see cref="DamageType.GroundEffect"/> with <see cref="AttackType.Knockback"/> is applied as <see cref="AttackType.Stun"/> using the caster&apos;s <c>attackStunDuration</c>. Buff-only spells omit <c>damageType</c> and use <c>temporaryEffects</c> (Invisibility). Damage spells may list <c>temporaryEffects</c> for on-hit debuffs (e.g. Chill). For <see cref="DamageType.GroundEffect"/>, those debuffs apply each time damage is delivered (each periodic tick or step-on hit), subject to group stacking rules.</remarks>
public record SpellConfig(
    int Id,
    string Name,
    /// <summary>Omitted for buff-only spells (see <c>temporaryEffects</c>).</summary>
    int? DamageType = null,
    /// <summary>How spell damage applies on targets (<see cref="AttackType"/>); default <see cref="AttackType.Interrupt"/> when omitted from JSON.</summary>
    int? AttackType = null,
    int? AoeRadius = null,
    int? Group = null,
    int? TickRate = null,
    int? ProjectileSpeed = null,
    /// <summary>When set with <c>projectileSpeed</c> for rectangle AoE, damage delay uses this pixel distance (same units as <see cref="Server.Helpers.Projectile"/> tile math) instead of caster-to-target distance.</summary>
    int? ProjectileDistance = null,
    int? EmissionSteps = null,
    int? StartRadius = null,
    int? EndRadius = null,
    int? StartShards = null,
    int? EndShards = null,
    /// <summary>Linear AoE: destination linger duration in ms; server applies damage after <c>duration / 2</c> from cast resolution.</summary>
    int? Duration = null,
    /// <summary>When true, clients may send optional aim-assist target ids on <c>SpellCastRequest</c> to snap the cast cell to that entity.</summary>
    bool? AimAssist = null,
    /// <summary>Buff-only and/or on-hit timed effects; JSON <c>temporaryEffects</c>; each <c>duration</c> is ms.</summary>
    SpellTimedEffectSpec[]? TemporaryEffects = null);

/// <summary>Optional axis-aligned dwell rectangle in map tiles; when omitted, worlds use full map bounds for that dwell entry.</summary>
public record GameWorldDwellAreaBoundsConfig(int X1, int Y1, int X2, int Y2);

/// <summary>Spawn rule: place <see cref="Count"/> instances of the catalog monster inside the area (or whole map).</summary>
public record GameWorldDwellAreaConfig(int MonsterId, int Count, GameWorldDwellAreaBoundsConfig? Area = null);

/// <summary>One catalog NPC placed at world creation; <see cref="Direction"/> is grid facing 0–7 (matches client direction indices).</summary>
public record GameWorldNpcPlacementConfig(int NpcId, int X, int Y, int Direction);

/// <summary>Static definition of one playable world instance (id, display name, map asset, optional fixed worker, optional teleports, optional monster dwell spawns, optional fixed NPC placements).</summary>
public record GameWorldConfig(
    string Id,
    string Name,
    string Map,
    string? Music = null,
    int? WorkerThread = null,
    GameWorldTeleportConfig[]? TeleportLocs = null,
    GameWorldDwellAreaConfig[]? DwellAreas = null,
    GameWorldNpcPlacementConfig[]? Npcs = null,
    bool Raid = false);

/// <summary>Thresholds for detecting impossibly fast movement and applying server-side paralysis.</summary>
public record MovementSpeedViolationCheckConfig(bool Verbose, int Limit, int Window, int SegmentsPerWindow, int ParalysisDuration, int MaxPingVariance);

/// <summary>Ping policy from <c>ping</c> in <c>Settings.json</c> (RTT sampling, disconnect thresholds).</summary>
public record PingConfig(int AllowedVariance, int Timeout, int Interval, int VarianceSampleSize);

/// <summary>Tick cadence and per-<see cref="Server.World.Game.GameWorld"/> incoming message channel sizing; JSON <c>gameWorld</c> in <c>Settings.json</c>.</summary>
public record GameWorldRuntimeSettings(int TickInterval, int IncomingMessagesQueueSize, int IncomingMessagesBatchSizePerDispatch);

/// <summary>Developer diagnostics; JSON <c>debug</c> in <c>Settings.json</c>.</summary>
/// <remarks>When <see cref="ProfileMonstersAILoop"/> is true, each game world logs aggregate monster AI loop time about once per second.</remarks>
public record DebugConfig(bool EnableGcLogs, bool ProfileMonstersAILoop);

/// <summary>View and spell-target radii in grid cells; JSON <c>radius</c> in <c>Settings.json</c>.</summary>
/// <remarks><see cref="CameraRadiusX"/> and <see cref="CameraRadiusY"/> bound how far from the caster’s tile a spell cast’s requested target coordinates may be (axis-aligned, same convention as view radii).</remarks>
public record RadiusConfig(int ViewRadiusX, int ViewRadiusY, int CameraRadiusX, int CameraRadiusY);

/// <summary>Session, animation, and anti-cheat timing slack; JSON <c>timings</c> in <c>Settings.json</c>.</summary>
public record TimingsConfig(
    int DisconnectTime,
    int ArrowSpeed,
    int BlizzardSpellDamageDelayMs,
    int PlayerPickupAnimationTime,
    int PlayerBowAnimationTime,
    int SpawnProtectionTime,
    int KnockbackTimeMs,
    double AntiHackTimingLagFactor);

/// <summary>Worker pool layout; JSON <c>threads</c> in <c>Settings.json</c>.</summary>
public record ThreadsConfig(int GameWorldWorkers, int? GlobalWorldWorkerThread);

/// <summary>Defaults under <c>monsterDefaults</c> in <c>Settings.json</c> when <c>Monsters.json</c> omits the corresponding field.</summary>
/// <remarks><see cref="ChaseMaxDistance"/> when <see langword="null"/> (omit from JSON) applies no server-wide max follow default—only catalog <c>chaseMaxDistance</c> and visibility; when set, used when the catalog omits <c>chaseMaxDistance</c>.</remarks>
public record MonsterDefaultsConfig(
    int ChaseDistance = 6,
    int? ChaseMaxDistance = null,
    int AttackSpeed = 600,
    int AttackDamageMin = 1,
    int AttackDamageMax = 5,
    int AttackRecoveryTime = 400,
    int MinIdleTime = 0,
    int MaxIdleTime = 5000,
    int Hp = 100,
    int CorpseDecayTime = 3000,
    int RespawnTime = 3000);

/// <summary>Runtime tuning for networking, visibility, tick rate, spawn, and anti-cheat checks.</summary>
/// <remarks><see cref="Port"/> is the HTTP listener port (all interfaces). <see cref="MonsterDefaults"/> (<see cref="MonsterDefaultsConfig"/>) supplies server-wide monster catalog fallbacks. <see cref="MonsterDefaultsConfig.ChaseMaxDistance"/> when <see langword="null"/> applies no max-follow default for omitted catalog <c>chaseMaxDistance</c>. <see cref="Radius"/> (<see cref="RadiusConfig"/>) defines visibility view radii and camera-bounded spell targets.</remarks>
public record SettingsConfig(
    int Port,
    TimingsConfig Timings,
    ThreadsConfig Threads,
    int ChatMessageMaxLength,
    int LogoutTime,
    PingConfig Ping,
    GameWorldRuntimeSettings GameWorld,
    bool CourseCorrection,
    RadiusConfig Radius,
    string InitialMap,
    bool SpawnToRandomMap,
    MovementSpeedViolationCheckConfig MovementSpeedViolationsChecker,
    DebugConfig Debug,
    MonsterDefaultsConfig MonsterDefaults,
    bool SpawnInMiddle = true,
    int MaxCellsJumpDistance = 3,
    /// <summary>Maximum number of dropped bag entries retained on one ground cell; only the newest top entry is visible.</summary>
    int MaxDroppedItemsInStack = 10,
    /// <summary>When true, serialize outbound protobuf with <c>MessageExtensions.WriteTo(Span&lt;byte&gt;)</c> instead of <see cref="System.IO.MemoryStream"/> + <see cref="Google.Protobuf.CodedOutputStream"/>; JSON <c>enableZeroCopyProtobufTransfer</c> in <c>Settings.json</c>. Produces less garbage, but in benchmarks reduces throughput versus the stream path.</summary>
    bool EnableZeroCopyProtobufTransfer = false,
    /// <summary>After this many consecutive outbound encode/send failures (excluding cancellation), cancel the connection receive loop. Zero disables the circuit breaker. JSON <c>maxConsecutiveOutboundSendFailures</c>.</summary>
    int MaxConsecutiveOutboundSendFailures = 10,
    /// <summary>Character persistence provider; JSON <c>persistence</c> in <c>Settings.json</c>. Null selects the JSON file store.</summary>
    PersistenceConfig? Persistence = null,
    /// <summary>NOCTHEMAR switches (debug requests); JSON <c>nocthemar</c> in <c>Settings.json</c>. Null means everything off.</summary>
    NocthemarSettings? Nocthemar = null);
