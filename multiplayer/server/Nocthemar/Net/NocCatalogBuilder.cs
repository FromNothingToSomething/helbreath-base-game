using Mmorpg.Network;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;

namespace Server.Nocthemar.Net;

/// <summary>Builds the wire form of the class and skill catalogs (<see cref="NocCatalog"/>), sent once per session in <c>InitialState</c>.</summary>
public static class NocCatalogBuilder {
    public static NocCatalog Build(ClassConfig classes, SkillConfig skills, SkillService rules) {
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

        return catalog;
    }
}
