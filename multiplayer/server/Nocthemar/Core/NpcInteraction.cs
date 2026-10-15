namespace Server.Nocthemar.Core;

/// <summary>Distance rule shared by the NOCTHEMAR NPCs (Soul Forge, faction masters): a player can use an NPC from up to <see cref="DefaultRangeCells"/> cells away.</summary>
public static class NpcInteraction {
    /// <summary>Largest Chebyshev distance, in cells, between a player and an NPC they interact with.</summary>
    public const int DefaultRangeCells = 3;

    /// <summary>True when the cells are at most <paramref name="rangeCells"/> apart in Chebyshev distance (the diagonal counts as one step).</summary>
    public static bool IsInRange(int playerX, int playerY, int npcX, int npcY, int rangeCells = DefaultRangeCells) =>
        Math.Max(Math.Abs(playerX - npcX), Math.Abs(playerY - npcY)) <= rangeCells;
}
