using Server.Nocthemar.Core;
using Mmorpg.Network;
using Server.Nocthemar.Data;
using Server.Nocthemar.Net;

namespace Server.Nocthemar.Skills;

/// <summary>
/// The class and skill content shared by every world and player: the validated catalogs, the skill rules and the handler registry.
/// Passing none (null) to a world or a player keeps the game exactly as it was before classes existed (no class, no skills, nothing sent).
/// </summary>
public sealed class NocContent {
    public NocContent(ClassConfig classes, SkillConfig skills, ProgressionConfig progression, SkillHandlerRegistry handlers, MimicConfig? mimic = null, MimicHandlerRegistry? mimicHandlers = null, FactionConfig? factions = null) {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(progression);
        Handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        handlers.ValidateCatalog(skills);
        Classes = classes;
        Skills = skills;
        Rules = new SkillService(classes, skills, progression);
        Catalog = NocCatalogBuilder.Build(classes, skills, Rules, mimic, factions);
        Mimic = mimic is null ? null : new MimicService(mimic);
        Reputation = factions is null ? null : new ReputationService(factions);
        Factions = Reputation is null ? null : new FactionService(Reputation);
        MimicHandlers = mimicHandlers ?? new MimicHandlerRegistry();
        if (mimic is not null) {
            MimicHandlers.ValidateCatalog(mimic);
        }
    }

    public ClassConfig Classes { get; }
    public SkillConfig Skills { get; }
    public SkillService Rules { get; }
    public SkillHandlerRegistry Handlers { get; }

    /// <summary>Mimicry rules; null when the content has no mimicry catalog (no ability can be learned).</summary>
    public MimicService? Mimic { get; }

    /// <summary>Faction and reputation rules; null when the content has no faction catalog (no reputation is checked or earned).</summary>
    public ReputationService? Reputation { get; }

    /// <summary>Joining a faction and its perks; null together with <see cref="Reputation"/>.</summary>
    public FactionService? Factions { get; }

    /// <summary>Handlers of the mimicry abilities (empty when none were given).</summary>
    public MimicHandlerRegistry MimicHandlers { get; }

    /// <summary>The wire catalog, built once; shared by every <c>InitialState</c> (read-only: never modify it).</summary>
    public NocCatalog Catalog { get; }
}
