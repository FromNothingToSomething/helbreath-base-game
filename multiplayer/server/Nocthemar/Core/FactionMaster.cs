namespace Server.Nocthemar.Core;

/// <summary>The faction masters: one NPC per faction (<c>NPCs.json</c> ids 11 to 14, in faction id order) next to which a character can join that faction.</summary>
public static class FactionMaster {
    /// <summary>NPC catalog id of the master of faction 0; the master of faction n is <c>FirstNpcCatalogId + n</c>.</summary>
    public const int FirstNpcCatalogId = 11;

    public static int NpcCatalogIdOf(int factionId) => FirstNpcCatalogId + factionId;

    /// <summary>True for a faction id the shipped masters cover (the catalog may hold up to <c>FactionConfig.MaxFactions</c>, only these have an NPC).</summary>
    public static bool IsValidFaction(int factionId) => factionId >= 0 && factionId < Count;

    /// <summary>Number of masters shipped in <c>NPCs.json</c>.</summary>
    public const int Count = 4;

    public static bool IsInRange(int playerX, int playerY, int npcX, int npcY) => NpcInteraction.IsInRange(playerX, playerY, npcX, npcY);
}
