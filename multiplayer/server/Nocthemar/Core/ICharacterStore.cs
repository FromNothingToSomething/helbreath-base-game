using Server.World.Game;

namespace Server.Nocthemar.Core;

/// <summary>
/// Single access point for persisted character data. No other code may read or write character saves directly.
/// Implementations: <c>JsonCharacterStore</c> (dev, <c>Chars/*.json</c>) and <c>PgCharacterStore</c> (production).
/// </summary>
public interface ICharacterStore {
    /// <summary>Loads and migrates the character to <see cref="CharacterSchema.Current"/>; null when absent or unreadable.</summary>
    Task<PlayerPersistenceState?> LoadAsync(string networkId, CancellationToken ct = default);

    /// <summary>Persists the character stamped with <see cref="CharacterSchema.Current"/>; must be atomic (a failed write leaves the previous save intact).</summary>
    Task SaveAsync(string networkId, PlayerPersistenceState state, CancellationToken ct = default);

    /// <summary>True when a save exists for <paramref name="networkId"/>; false for invalid ids.</summary>
    Task<bool> ExistsAsync(string networkId, CancellationToken ct = default);
}
