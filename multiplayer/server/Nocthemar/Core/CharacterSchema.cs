namespace Server.Nocthemar.Core;

/// <summary>Versions of the persisted character JSON (<c>PlayerPersistenceState.SchemaVersion</c>).</summary>
public static class CharacterSchema {
    /// <summary>Saves written before versioning existed: the field is absent and deserializes to 0.</summary>
    public const int Legacy = 0;

    /// <summary>First versioned format: may carry an optional <c>Noc</c> block; a null <c>Noc</c> is valid and means "create defaults at login".</summary>
    public const int V1 = 1;

    /// <summary>Current format: like <see cref="V1"/>, and a present <c>Noc</c> block always has a <c>RaceId</c> (migration 1 -> 2 sets legacy characters to <see cref="NocState.LegacyRaceId"/>).</summary>
    public const int Current = 2;
}
