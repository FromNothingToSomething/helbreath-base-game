using System.Text.Json;
using Server.Nocthemar.Core;
using Server.World.Game;

namespace Server.Nocthemar.Persistence;

/// <summary>
/// Dev character store: one <c>{networkId}.json</c> per character under the saves directory (same layout and JSON options as the original <c>Chars/</c> saves).
/// Writes are atomic (temp file in the same directory, then rename), so a crash leaves either the old or the new save, never a partial file.
/// </summary>
public sealed class JsonCharacterStore : ICharacterStore {
    // Same options as the original Server.cs save code (indented, default naming).
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string directory;
    private readonly CharacterMigrator migrator;
    private readonly Action<string> log;

    public JsonCharacterStore(string charsDirectory, CharacterMigrator migrator, Action<string>? log = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(charsDirectory);
        this.migrator = migrator ?? throw new ArgumentNullException(nameof(migrator));
        this.log = log ?? Console.Error.WriteLine;
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(charsDirectory));
    }

    /// <summary>Test hook: runs after the temp file is fully written and before it replaces the save.</summary>
    internal Action<string>? AfterTempWritten { get; set; }

    public async Task<PlayerPersistenceState?> LoadAsync(string networkId, CancellationToken ct = default) {
        if (!NetworkIdValidator.TryResolveSavePath(directory, networkId, out var path)) {
            log("[CharacterStore] Rejected load for an invalid network id.");
            return null;
        }

        if (!File.Exists(path)) {
            return null;
        }

        PlayerPersistenceState? state;
        try {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            state = await JsonSerializer.DeserializeAsync<PlayerPersistenceState>(stream, cancellationToken: ct);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            log($"[CharacterStore] Failed to load '{path}': {ex.Message}");
            return null;
        }

        if (state is null) {
            log($"[CharacterStore] Save '{path}' is empty.");
            return null;
        }

        try {
            return migrator.Migrate(state);
        } catch (CharacterSchemaTooNewException ex) {
            log($"[CharacterStore] Refusing '{path}': {ex.Message} The file is left untouched.");
            throw;
        }
    }

    public async Task SaveAsync(string networkId, PlayerPersistenceState state, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(state);
        if (!NetworkIdValidator.TryResolveSavePath(directory, networkId, out var path)) {
            throw new ArgumentException("Invalid network id.", nameof(networkId));
        }

        if (state.SchemaVersion > CharacterSchema.Current) {
            throw new CharacterSchemaTooNewException(state.SchemaVersion, CharacterSchema.Current);
        }

        // Serialize fully before touching the disk so a serialization failure cannot leave a partial file.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state with { SchemaVersion = CharacterSchema.Current }, WriteOptions);

        Directory.CreateDirectory(directory);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try {
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough)) {
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
                stream.Flush(true);
            }

            AfterTempWritten?.Invoke(tempPath);
            File.Move(tempPath, path, overwrite: true);
        } catch {
            TryDelete(tempPath);
            throw;
        }
    }

    public Task<bool> ExistsAsync(string networkId, CancellationToken ct = default) =>
        Task.FromResult(NetworkIdValidator.TryResolveSavePath(directory, networkId, out var path) && File.Exists(path));

    private static void TryDelete(string path) {
        try {
            File.Delete(path);
        } catch (IOException) {
            // Best effort: a leftover temp file is ignored by LoadAsync (different name from the save).
        } catch (UnauthorizedAccessException) {
        }
    }
}
