using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.World.Game;

namespace Server.Nocthemar.Persistence;

/// <summary>Login/disconnect policy on top of <see cref="ICharacterStore"/>: what a failure means for the player and for the file on disk.</summary>
public static class CharacterAccess {
    /// <summary>
    /// Loads a character for login. A non-null refusal means the login must be rejected and the save left alone:
    /// a newer schema, a save that exists but cannot be read (the next save would overwrite it), or a store failure.
    /// A missing save is a new character (null state, null refusal). Cancellation propagates.
    /// A race id that is not in <paramref name="races"/> is NOT a refusal: the state comes back with the default race and a warning is logged
    /// (network id and original id); nothing is written here, the file keeps its content until the next regular save.
    /// </summary>
    public static async Task<(PlayerPersistenceState? State, string? Refusal)> TryLoadAsync(
        ICharacterStore store,
        string networkId,
        CancellationToken cancellationToken = default,
        Action<string>? log = null,
        RaceConfig? races = null) {
        ArgumentNullException.ThrowIfNull(store);
        log ??= Console.Error.WriteLine;
        try {
            var state = await store.LoadAsync(networkId, cancellationToken);
            if (state is null && await store.ExistsAsync(networkId, cancellationToken)) {
                log($"[Server] Refusing login for '{networkId}': the save exists but could not be read; it is left untouched.");
                return (null, "Character data is unreadable. Contact an administrator.");
            }

            return (ReplaceUnknownRace(state, networkId, races, log), null);
        } catch (CharacterSchemaTooNewException ex) {
            log($"[Server] Refusing login for '{networkId}': {ex.Message}");
            return (null, "This character was saved by a newer server version.");
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            log($"[Server] Refusing login for '{networkId}': character store failure: {ex}");
            return (null, "Character data is temporarily unavailable.");
        }
    }

    private static PlayerPersistenceState? ReplaceUnknownRace(PlayerPersistenceState? state, string networkId, RaceConfig? races, Action<string> log) {
        if (races is null || state?.Noc?.RaceId is not int raceId || races.Contains(raceId)) {
            return state;
        }

        log(CharacterLog.FormatUnknownRaceWarning(networkId, raceId, races.DefaultRaceId));
        return state with { Noc = state.Noc with { RaceId = races.DefaultRaceId } };
    }

    /// <summary>
    /// Saves a character; any failure (invalid id, I/O, newer schema) is logged and reported as false, never thrown,
    /// so one bad save cannot break a disconnect, a world transfer or the shutdown loop. The store guarantees the previous file survives a failed write.
    /// </summary>
    public static async Task<bool> TrySaveAsync(ICharacterStore store, string networkId, PlayerPersistenceState state, Action<string>? log = null) {
        ArgumentNullException.ThrowIfNull(store);
        log ??= Console.Error.WriteLine;
        try {
            await store.SaveAsync(networkId, state);
            return true;
        } catch (Exception ex) {
            log($"[Server] Failed to save player '{networkId}' ({ex.GetType().Name}): {ex.Message}");
            return false;
        }
    }
}
