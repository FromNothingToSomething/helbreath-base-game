using Mmorpg.Network;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;

namespace Server.Nocthemar.Net;

/// <summary>Builds the wire form of the class and skill catalogs (<see cref="NocCatalog"/>), sent once per session in <c>InitialState</c>.</summary>
public static class NocCatalogBuilder {
    public static NocCatalog Build(ClassConfig classes, SkillConfig skills, SkillService rules, MimicConfig? mimic = null, FactionConfig? factions = null) {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(rules);
        var catalog = new NocCatalog();
        foreach (var definition in classes.Classes) {
            var info = new NocClassInfo { Id = definition.Id, Key = definition.Key, Name = definition.Name, Role = definition.Role };
            foreach (var branch in definition.Branches) {
                info.Branches.Add(new NocBranchInfo { Id = branch.Id, Key = branch.Key, Name = branch.Name, Role = branch.Role });
            }

            info.RecommendedRaceIds.AddRange(definition.RecommendedRaceIds);
            catalog.Classes.Add(info);
        }

        foreach (var skill in skills.Skills) {
            catalog.Skills.Add(new NocSkillInfo {
                Id = skill.Id,
                Key = skill.Key,
                Name = skill.Name,
                ClassId = skill.ClassId,
                Branch = skill.Branch,
                Slot = skill.Slot,
                Level = skill.Level,
                Kind = (int)skill.Kind,
                Cost = skill.Cost,
                Desc = skill.Description,
                Ready = skill.IsReady,
                PrerequisiteId = rules.PrerequisiteOf(skill)?.Id ?? 0,
            });
        }

        if (mimic is not null) {
            AddMimicry(catalog, mimic);
        }

        if (factions is not null) {
            AddFactions(catalog, factions);
        }

        return catalog;
    }

    private static void AddFactions(NocCatalog catalog, FactionConfig factions) {
        var rules = factions.Rules;
        catalog.ReputationRules = new NocReputationRules {
            Min = rules.Min,
            Max = rules.Max,
            JoinLevel = rules.JoinLevel,
            KillBase = rules.KillBase,
            Neutral = rules.Neutral,
            Friendly = rules.Friendly,
            Honored = rules.Honored,
            Exalted = rules.Exalted,
        };
        foreach (var faction in factions.Factions) {
            var info = new NocFactionInfo { Id = faction.Id, Key = faction.Key, Name = faction.Name, Hq = faction.Hq, Symbol = faction.Symbol, Desc = faction.Desc };
            info.Colors.AddRange(faction.Colors);
            info.Rivals.AddRange(faction.Rivals);
            foreach (var tier in new[] { ReputationTier.Friendly, ReputationTier.Honored, ReputationTier.Exalted }) {
                var perk = new NocFactionPerk { Tier = (int)tier };
                foreach (var modifier in faction.PerksOf(tier)) {
                    perk.Modifiers.Add(new NocPerkModifier { Stat = (int)modifier.Stat, Kind = (int)modifier.Kind, Value = modifier.Value });
                }

                if (perk.Modifiers.Count > 0) {
                    info.Perks.Add(perk);
                }
            }

            catalog.Factions.Add(info);
        }
    }

    private static void AddMimicry(NocCatalog catalog, MimicConfig mimic) {
        foreach (var ability in mimic.Abilities) {
            var info = new NocMimicInfo {
                Id = ability.Id,
                Key = ability.Key,
                Name = ability.Name,
                Kind = (int)ability.Kind,
                Rarity = (int)ability.Rarity,
                MonsterName = ability.MonsterName ?? "",
                RequiresRaid = ability.RequiresRaid,
                CooldownMs = ability.CooldownMs,
                ManaCost = ability.ManaCost,
                Desc = ability.Desc,
                Ready = ability.IsReady,
                EssenceItemId = ability.EssenceItemId ?? 0,
            };
            info.ClassIds.AddRange(ability.ClassIds);
            catalog.Mimics.Add(info);
        }

        foreach (var fusion in mimic.Fusions) {
            catalog.Fusions.Add(new NocFusionInfo { A = fusion.A, B = fusion.B, Result = fusion.Result });
        }

        catalog.MimicXpThresholds.AddRange(mimic.Xp.Thresholds.Select(t => (long)t));
        catalog.MimicPowerPerLevel = mimic.Xp.PowerPerLevel;
        catalog.MimicSlotsFirst = mimic.Slots.First;
        catalog.MimicLevelsPerSlot = mimic.Slots.LevelsPerSlot;
        catalog.MimicSlotsMax = mimic.Slots.Max;
    }
}
