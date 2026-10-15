using System.Collections.Concurrent;
using Server;
using Server.Nocthemar.Data;
using Server.Nocthemar.Skills;
using Server.Utils;
using Server.World.Global;
using Server.World.Game;

namespace Server.World;

/// <summary>
/// Owns all registered playable <see cref="GameWorld"/> instances, the singleton <see cref="GlobalWorld"/>, their worker threads, and routing into their mailboxes.
/// </summary>
public sealed class WorldRegistry : IDisposable {
    private readonly ConcurrentDictionary<string, GameWorld> gameWorldsById;
    private readonly WorldWorker[] workers;
    private readonly SettingsConfig settings;
    private readonly ProgressionConfig progression;
    private readonly RaceConfig races;
    private readonly NocContent? nocContent;
    /// <summary>Round-robin counter for assigning new worlds when no explicit worker index is configured.</summary>
    private int nextWorkerIndex;
    private bool disposed;

    public WorldRegistry(
        SettingsConfig settings,
        ProgressionConfig progression,
        RaceConfig races,
        int workerCount,
        TimeSpan tickInterval,
        NocContent? nocContent = null) {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(races);
        if (workerCount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(workerCount), "Worker count must be greater than zero.");
        }

        gameWorldsById = new ConcurrentDictionary<string, GameWorld>(StringComparer.Ordinal);
        workers = new WorldWorker[workerCount];
        this.settings = settings;
        this.progression = progression;
        this.races = races;
        this.nocContent = nocContent;

        for (var index = 0; index < workerCount; index++) {
            workers[index] = new WorldWorker($"game-world-worker-{index}", tickInterval);
            workers[index].Start();
        }
    }

    public GlobalWorld? GlobalWorld { get; private set; }

    /// <summary>Constructs a playable world, registers it by <paramref name="id"/>, and attaches it to a worker (pinned or round-robin).</summary>
    public GameWorld RegisterGameWorld(
        string id,
        string map,
        string? music,
        GameWorldOccupancyTracker occupancyTracker,
        IReadOnlyDictionary<string, MonsterConfig> monsterCatalog,
        IReadOnlyDictionary<int, MonsterConfig> monstersById,
        IReadOnlyDictionary<int, SpellConfig> spellsById,
        IReadOnlyDictionary<int, ItemConfig> itemsById,
        IReadOnlyDictionary<int, NpcConfig> npcsById,
        IReadOnlyList<GameWorldDwellAreaConfig>? dwellAreas = null,
        IReadOnlyList<GameWorldTeleportSet>? teleportLocs = null,
        IReadOnlyList<GameWorldNpcPlacementConfig>? initialNpcs = null,
        int? workerThread = null,
        bool raid = false) {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(occupancyTracker);
        ArgumentNullException.ThrowIfNull(monsterCatalog);
        ArgumentNullException.ThrowIfNull(monstersById);
        ArgumentNullException.ThrowIfNull(spellsById);
        ArgumentNullException.ThrowIfNull(itemsById);
        ArgumentNullException.ThrowIfNull(npcsById);

        var world = new GameWorld(id, map, music, occupancyTracker, settings, monsterCatalog, monstersById, spellsById, itemsById, npcsById, progression, races, dwellAreas, teleportLocs, initialNpcs, nocContent, raid);
        if (!gameWorldsById.TryAdd(id, world)) {
            throw new InvalidOperationException($"A game world with id '{id}' is already registered.");
        }

        AttachWorldToWorker(world, workerThread);
        return world;
    }

    /// <summary>Registers the singleton <see cref="GlobalWorld"/> on a worker (pinned or round-robin).</summary>
    public GlobalWorld RegisterGlobalWorld(GlobalWorld world, int? workerThread = null) {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(world);
        if (GlobalWorld is not null) {
            throw new InvalidOperationException("The global world has already been registered.");
        }

        GlobalWorld = world;
        AttachWorldToWorker(world, workerThread);
        return world;
    }

    public bool TryGetGameWorld(string worldId, out GameWorld? world) {
        return gameWorldsById.TryGetValue(worldId, out world);
    }

    /// <summary>Looks up a playable world by id and enqueues the message on that world's mailbox.</summary>
    public ValueTask RouteGameWorldMessageAsync(
        string worldId,
        GameWorldMessage message,
        CancellationToken cancellationToken = default) {
        ThrowIfDisposed();

        if (!gameWorldsById.TryGetValue(worldId, out var world)) {
            throw new KeyNotFoundException($"No game world is registered for world '{worldId}'.");
        }

        return world.EnqueueAsync(message, cancellationToken);
    }

    /// <summary>Enqueues a message on the singleton global world's mailbox.</summary>
    public ValueTask RouteGlobalMessageAsync(
        GlobalWorldMessage message,
        CancellationToken cancellationToken = default) {
        ThrowIfDisposed();

        if (GlobalWorld is null) {
            throw new KeyNotFoundException("The global world has not been registered.");
        }

        return GlobalWorld.EnqueueAsync(message, cancellationToken);
    }

    public void Dispose() {
        if (disposed) {
            return;
        }

        disposed = true;
        foreach (var worker in workers) {
            worker.Dispose();
        }
    }

    private void AttachWorldToWorker(IWorkerWorld world, int? workerThread) {
        var worker = ResolveWorker(workerThread);
        worker.RegisterWorld(world);
    }

    private WorldWorker ResolveWorker(int? workerThread) {
        if (workerThread.HasValue) {
            if (workerThread.Value < 0) {
                throw new ArgumentOutOfRangeException(nameof(workerThread), "Worker thread index must be non-negative.");
            }

            return workers[workerThread.Value % workers.Length];
        }

        return GetNextWorker();
    }

    private WorldWorker GetNextWorker() {
        var workerIndex = Interlocked.Increment(ref nextWorkerIndex) - 1;
        return workers[workerIndex % workers.Length];
    }

    private void ThrowIfDisposed() {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
