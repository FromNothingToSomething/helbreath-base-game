namespace Server.Nocthemar.Core;

/// <summary>Versions of the persisted character JSON (<c>PlayerPersistenceState.SchemaVersion</c>).</summary>
public static class CharacterSchema {
    /// <summary>Saves written before versioning existed: the field is absent and deserializes to 0.</summary>
    public const int Legacy = 0;

    /// <summary>First versioned format: may carry an optional <c>Noc</c> block; a null <c>Noc</c> is valid and means "create defaults at login".</summary>
    public const int V1 = 1;

    /// <summary>A present <c>Noc</c> block always has a <c>RaceId</c> (migration 1 -> 2 sets legacy characters to <see cref="NocState.LegacyRaceId"/>).</summary>
    public const int V2 = 2;

    /// <summary>Current format: like <see cref="V2"/>, and the <c>Noc</c> block may carry a class, learned skills and a kill counter (migration 2 -> 3 only raises the block layout, nobody has a class yet).</summary>
    public const int Current = 3;
}
