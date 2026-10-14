using Server.Nocthemar.Core;
using Server.World.Game;

namespace Server.Nocthemar.Persistence;

/// <summary>
/// PostgreSQL character store (production). Stub: the contract is in place, the implementation is a later module.
/// Every member throws <see cref="NotSupportedException"/>; <see cref="CharacterStoreFactory"/> refuses to start with it.
/// </summary>
public sealed class PgCharacterStore : ICharacterStore {
    internal const string NotImplementedMessage = "PgCharacterStore is not implemented yet; set persistence.provider to \"json\" in Settings.json.";

    public PgCharacterStore(string connectionString) {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
    }

    public Task<PlayerPersistenceState?> LoadAsync(string networkId, CancellationToken ct = default) =>
        throw new NotSupportedException(NotImplementedMessage);

    public Task SaveAsync(string networkId, PlayerPersistenceState state, CancellationToken ct = default) =>
        throw new NotSupportedException(NotImplementedMessage);

    public Task<bool> ExistsAsync(string networkId, CancellationToken ct = default) =>
        throw new NotSupportedException(NotImplementedMessage);
}
