namespace Server.Nocthemar.Core;

/// <summary>The Soul Forge (Forge d'Âmes): the NPC next to which essences are consumed and fused. The mimicry bar itself can be changed anywhere.</summary>
public static class SoulForge {
    /// <summary>Id of the NPC in <c>NPCs.json</c>.</summary>
    public const int NpcCatalogId = 10;

    /// <summary>Largest Chebyshev distance, in cells, between the player and the forge NPC.</summary>
    public const int RangeCells = 3;

    public const string NoForgeInRange = "No Soul Forge in range.";

    public static bool IsInRange(int playerX, int playerY, int npcX, int npcY) =>
        Math.Max(Math.Abs(playerX - npcX), Math.Abs(playerY - npcY)) <= RangeCells;
}
