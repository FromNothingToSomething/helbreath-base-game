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
    public NocContent(ClassConfig classes, SkillConfig skills, ProgressionConfig progression, SkillHandlerRegistry handlers) {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(progression);
        Handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        handlers.ValidateCatalog(skills);
        Classes = classes;
        Skills = skills;
        Rules = new SkillService(classes, skills, progression);
        Catalog = NocCatalogBuilder.Build(classes, skills, Rules);
    }

    public ClassConfig Classes { get; }
    public SkillConfig Skills { get; }
    public SkillService Rules { get; }
    public SkillHandlerRegistry Handlers { get; }

    /// <summary>The wire catalog, built once; shared by every <c>InitialState</c> (read-only: never modify it).</summary>
    public NocCatalog Catalog { get; }
}
