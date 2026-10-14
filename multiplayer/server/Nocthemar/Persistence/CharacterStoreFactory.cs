using Server.Nocthemar.Core;
using Server.Nocthemar.Data;

namespace Server.Nocthemar.Persistence;

/// <summary>Builds the <see cref="ICharacterStore"/> selected by <c>persistence.provider</c>; fails at startup on an unusable choice.</summary>
public static class CharacterStoreFactory {
    /// <exception cref="InvalidOperationException">Unknown provider, or <c>pg</c> (not implemented yet).</exception>
    public static ICharacterStore Create(PersistenceConfig? config, string charsDirectory, ProgressionConfig progression, Action<string>? log = null) {
        ArgumentNullException.ThrowIfNull(progression);
        var provider = (config?.Provider ?? PersistenceConfig.JsonProvider).Trim();

        if (provider.Equals(PersistenceConfig.JsonProvider, StringComparison.OrdinalIgnoreCase)) {
            return new JsonCharacterStore(charsDirectory, CharacterMigrator.CreateDefault(progression), log);
        }

        if (provider.Equals(PersistenceConfig.PgProvider, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"persistence.provider \"pg\" is selected but unavailable: {PgCharacterStore.NotImplementedMessage}");
        }

        throw new InvalidOperationException($"Unknown persistence.provider \"{provider}\"; expected \"json\" or \"pg\".");
    }
}
