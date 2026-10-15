using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Channels;
using Mmorpg.Network;
using Server;
using Server.Helpers;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Nocthemar.Net;
using Server.Nocthemar.Skills;
using Server.Utils;

namespace Server.World.Game;

/// <summary>
/// Bundle of dependencies and scratch buffers passed into helpers (<see cref="Server.Helpers.Movement"/>, <see cref="Server.Helpers.Spawn"/>, <see cref="Server.Helpers.Combat"/>, <see cref="Server.Helpers.Casting"/>, etc.)
/// so they can run without capturing a wide closure. Scratch collections are owned by <see cref="GameWorld"/> and must not be retained across awaits.
/// </summary>
public struct GameWorldRef {
    /// <summary>Owning world instance for lookups (e.g. TryGetPlayerBySessionId).</summary>
    public GameWorld World;
    /// <summary>Walkable vs blocked cells for movement validation.</summary>
    public GameWorldOccupancyTracker OccupancyTracker;
    /// <summary>Server-wide tunables (radii, anti-cheat, ping policy).</summary>
    public SettingsConfig Settings;
    /// <summary>Registered world id (matches config and client routing).</summary>
    public string WorldId;
    /// <summary>Map asset name for initial state payloads.</summary>
    public string Map;
    /// <summary>Music file the client should play for this world.</summary>
    public string? Music;
    /// <summary>Config-defined teleport metadata sent to clients and used for authoritative transfer checks.</summary>
    public IReadOnlyList<GameWorldTeleportSet> TeleportLocs;
    /// <summary>Spatial index of players for neighborhood queries.</summary>
    public PlayersSpatialGrid PlayerSpatialGrid;
    /// <summary>Spatial index of monsters for neighborhood queries.</summary>
    public MonstersSpatialGrid MonsterSpatialGrid;
    /// <summary>Tracks long-lived ground effects per cell and schedules their periodic ticks or expiry callbacks.</summary>
    public GroundStateTracker GroundStateTracker;
    /// <summary>Reader for this world's mailbox; used for diagnostics (e.g. queue depth in ping responses).</summary>
    public ChannelReader<GameWorldMessage> IncomingReader;
    /// <summary>Deferred callbacks (spawn protection expiry, repeating ping checks).</summary>
    public Scheduler Scheduler;
    /// <summary>Spell catalog for <see cref="Mmorpg.Network.InitialState"/> payloads (stable id order).</summary>
    public IReadOnlyDictionary<int, SpellConfig> SpellsById;
    /// <summary>Item catalog for <see cref="Mmorpg.Network.InitialState"/> <c>items_directory</c> (stable id order in the wire payload).</summary>
    public IReadOnlyDictionary<int, ItemConfig> ItemsById;
    /// <summary>NPC catalog for <see cref="Mmorpg.Network.InitialState"/> <c>npc_directory</c> (display names; client maps ids to sprites).</summary>
    public IReadOnlyDictionary<int, NpcConfig> NpcsById;
    /// <summary>Spatial index of NPCs for neighborhood queries.</summary>
    public NpcsSpatialGrid NpcSpatialGrid;
    /// <summary>All NPCs on this map keyed by <see cref="GameWorldNPC.NpcId"/>.</summary>
    public Dictionary<long, GameWorldNPC> NpcsByNpcId;
    /// <summary>Scratch: NPCs near a cell within view radii.</summary>
    public Dictionary<long, GameWorldNPC> NearbyNpcsByIdScratch;
    /// <summary>Scratch: NPC ids a player saw before a visibility refresh.</summary>
    public HashSet<long> NpcsPreviouslyInRangeScratch;
    /// <summary>Scratch: NPCs that entered a player’s view during <see cref="Npc.SyncPlayerNpcVisibilityAfterMovement"/>.</summary>
    public List<GameWorldNPC> PlayerNpcVisibilityEnteredScratch;
    /// <summary>Scratch: NPC ids that left a player’s view during <see cref="Npc.SyncPlayerNpcVisibilityAfterMovement"/>.</summary>
    public List<long> PlayerNpcVisibilityLeftNpcIdsScratch;
    /// <summary>Reused map of nearby player ids when building visibility updates.</summary>
    public Dictionary<long, GameWorldPlayer> NearbyPlayersByIdScratch;
    /// <summary>Reused set of who was in range before a movement for diffing enter/leave.</summary>
    public HashSet<long> PlayersPreviouslyInRangeScratch;
    /// <summary>All monsters on this map keyed by <see cref="GameWorldMonster.MonsterId"/>.</summary>
    public Dictionary<long, GameWorldMonster> MonstersByMonsterId;
    /// <summary>Scratch: monsters near a cell within view radii.</summary>
    public Dictionary<long, GameWorldMonster> NearbyMonstersByIdScratch;
    /// <summary>Scratch: monster ids a player saw before a visibility refresh.</summary>
    public HashSet<long> MonstersPreviouslyInRangeScratch;
    /// <summary>Scratch: players who could see a monster at its previous cell before a step.</summary>
    public Dictionary<long, GameWorldPlayer> MonsterStepOldViewersScratch;
    /// <summary>Scratch: players who can see a monster at its new cell after a step.</summary>
    public Dictionary<long, GameWorldPlayer> MonsterStepNewViewersScratch;
    /// <summary>Scratch: monsters that entered a player’s view during <see cref="Server.Helpers.MonsterVisibility.SyncPlayerMonsterVisibilityAfterMovement"/>.</summary>
    public List<GameWorldMonster> PlayerMonsterVisibilityEnteredScratch;
    /// <summary>Scratch: monster ids that left a player’s view during <see cref="Server.Helpers.MonsterVisibility.SyncPlayerMonsterVisibilityAfterMovement"/>.</summary>
    public List<long> PlayerMonsterVisibilityLeftMonsterIdsScratch;
    /// <summary>Scratch: players newly in range of the mover during <see cref="Server.Helpers.Movement.SyncPlayerVisibilityAfterMovement"/>.</summary>
    public List<GameWorldPlayer> MovementNewNeighborsScratch;
    /// <summary>Scratch: neighbor player ids no longer in range after a move during <see cref="Server.Helpers.Movement.SyncPlayerVisibilityAfterMovement"/>.</summary>
    public List<long> MovementLeftNeighborIdsScratch;
    /// <summary>Scratch: unique spell-affected grid cells for the current spell resolution.</summary>
    public HashSet<(int X, int Y)> SpellAffectedCellsScratch;
    /// <summary>Scratch: ground effects near a player within view radii.</summary>
    public Dictionary<long, GroundEffectState> NearbyGroundEffectsByIdScratch;
    /// <summary>Scratch: ground effect ids a player saw before a visibility refresh.</summary>
    public HashSet<long> GroundEffectsPreviouslyInRangeScratch;
    /// <summary>Scratch: ground effects newly entered into a player's view or newly created near viewers.</summary>
    public List<GroundEffectState> GroundEffectsEnteredScratch;
    /// <summary>Scratch: ground effects that left a player's view or expired for viewers.</summary>
    public List<GroundEffectState> GroundEffectsLeftScratch;
    /// <summary>Scratch: nearby viewers collected while broadcasting created or expired ground effects.</summary>
    public Dictionary<long, GameWorldPlayer> GroundEffectsViewersScratch;
    /// <summary>Scratch: top-most ground items near a player within view radii.</summary>
    public Dictionary<long, GroundItemState> NearbyGroundItemsByIdScratch;
    /// <summary>Scratch: top-most ground item ids a player saw before a visibility refresh.</summary>
    public HashSet<long> GroundItemsPreviouslyInRangeScratch;
    /// <summary>Scratch: top-most ground items newly entered into a player's view or newly revealed near viewers.</summary>
    public List<GroundItemState> GroundItemsEnteredScratch;
    /// <summary>Scratch: top-most ground items that left a player's view or were removed/replaced for viewers.</summary>
    public List<GroundItemState> GroundItemsLeftScratch;
    /// <summary>Scratch: nearby viewers collected while broadcasting dropped-item visibility changes.</summary>
    public Dictionary<long, GameWorldPlayer> GroundItemsViewersScratch;
    /// <summary>Scratch: groups <see cref="GroundStatesEnteredRange"/> payload cells by grid coordinate while building wire messages.</summary>
    public Dictionary<(int X, int Y), Mmorpg.Network.GroundStateCell> GroundStatesEnteredByCellScratch;
    /// <summary>Scratch: groups <see cref="GroundStatesLeftRange"/> payload cells by grid coordinate while building wire messages.</summary>
    public Dictionary<(int X, int Y), GroundStateCellRemoved> GroundStatesLeftByCellScratch;
}

/// <summary>
/// Single map instance: owns players, occupancy, spatial index, and inbound message mailbox. Mutated only on its assigned <see cref="WorldWorker"/> thread.
/// </summary>
public sealed class GameWorld : IWorkerWorld {
    private const int TeleportValidationRadius = 3;
    private readonly Channel<GameWorldMessage> incomingMessages;
    private readonly Dictionary<Guid, GameWorldPlayer> playersBySessionId = new();
    private readonly Dictionary<long, GameWorldPlayer> playersMap = new();
    /// <summary>Caps messages handled per worker wake; from <see cref="GameWorldRuntimeSettings.IncomingMessagesBatchSizePerDispatch"/> (<c>Settings.json</c> <c>gameWorld.incomingMessagesBatchSizePerDispatch</c>).</summary>
    private readonly int maxMessagesPerDispatch;
    private readonly SettingsConfig settings;
    private readonly Scheduler scheduler = new();
    private readonly PlayersSpatialGrid playerSpatialGrid;
    private readonly MonstersSpatialGrid monsterSpatialGrid;
    private readonly NpcsSpatialGrid npcSpatialGrid;
    private readonly GroundStateTracker groundStateTracker;
    private readonly Dictionary<long, GameWorldPlayer> nearbyPlayersByIdScratch = new();
    private readonly HashSet<long> playersPreviouslyInRangeScratch = new();
    private readonly Dictionary<long, GameWorldMonster> nearbyMonstersByIdScratch = new();
    private readonly HashSet<long> monstersPreviouslyInRangeScratch = new();
    private readonly Dictionary<long, GameWorldPlayer> monsterStepOldViewersScratch = new();
    private readonly Dictionary<long, GameWorldPlayer> monsterStepNewViewersScratch = new();
    private readonly List<GameWorldMonster> playerMonsterVisibilityEnteredScratch = new();
    private readonly List<long> playerMonsterVisibilityLeftMonsterIdsScratch = new();
    /// <summary>Scratch: snapshot of <see cref="monstersByMonsterId"/> values for one AI tick so <see cref="RemoveMonster"/> during <see cref="GameWorldMonster.TickAi"/> cannot invalidate enumeration.</summary>
    private readonly List<GameWorldMonster> monsterAiTickScratch = new();
    private readonly Dictionary<long, GameWorldNPC> nearbyNpcsByIdScratch = new();
    private readonly HashSet<long> npcsPreviouslyInRangeScratch = new();
    private readonly List<GameWorldNPC> playerNpcVisibilityEnteredScratch = new();
    private readonly List<long> playerNpcVisibilityLeftNpcIdsScratch = new();
    private readonly List<GameWorldPlayer> movementNewNeighborsScratch = new();
    private readonly List<long> movementLeftNeighborIdsScratch = new();
    private readonly HashSet<(int X, int Y)> spellAffectedCellsScratch = new();
    private readonly Dictionary<long, GroundEffectState> nearbyGroundEffectsByIdScratch = new();
    private readonly HashSet<long> groundEffectsPreviouslyInRangeScratch = new();
    private readonly List<GroundEffectState> groundEffectsEnteredScratch = new();
    private readonly List<GroundEffectState> groundEffectsLeftScratch = new();
    private readonly Dictionary<long, GameWorldPlayer> groundEffectsViewersScratch = new();
    private readonly Dictionary<long, GroundItemState> nearbyGroundItemsByIdScratch = new();
    private readonly HashSet<long> groundItemsPreviouslyInRangeScratch = new();
    private readonly List<GroundItemState> groundItemsEnteredScratch = new();
    private readonly List<GroundItemState> groundItemsLeftScratch = new();
    private readonly Dictionary<long, GameWorldPlayer> groundItemsViewersScratch = new();
    private readonly Dictionary<(int X, int Y), Mmorpg.Network.GroundStateCell> groundStatesEnteredByCellScratch = new();
    private readonly Dictionary<(int X, int Y), GroundStateCellRemoved> groundStatesLeftByCellScratch = new();
    private readonly Random monsterAiRandom = new();
    private readonly int viewRadiusX;
    private readonly int viewRadiusY;
    private WorldWorker? worker;
    /// <summary>0/1 flag: whether this world is already queued on the worker's ready queue.</summary>
    private int isScheduled;
    private readonly string id;
    private readonly string map;
    private readonly string? music;
    private readonly GameWorldOccupancyTracker occupancyTracker;
    private readonly IReadOnlyList<GameWorldTeleportSet> teleportLocs;
    private readonly Dictionary<(int X, int Y), GameWorldTeleportTarget> teleportTargetsBySourceCell = new();
    private readonly GameWorldRef gameWorldRef;
    private readonly IReadOnlyDictionary<string, MonsterConfig> monsterCatalog;
    private readonly IReadOnlyDictionary<int, MonsterConfig> monstersById;
    private readonly IReadOnlyDictionary<int, SpellConfig> spellsById;
    private readonly IReadOnlyDictionary<int, ItemConfig> itemsById;
    private readonly IReadOnlyDictionary<int, NpcConfig> npcsById;
    private readonly ProgressionConfig progression;
    private readonly RaceConfig races;
    private readonly NocDispatcher nocDispatcher;
    /// <summary>Class and skill content given to every player of this world; null = classes disabled.</summary>
    private readonly NocContent? nocContent;
    /// <summary>Mythic essences only drop in a world flagged <c>raid</c> in GameWorlds.json.</summary>
    private readonly bool raid;
    /// <summary>Rolls essence drops; null when the world has no mimicry catalog.</summary>
    private readonly EssenceDropper? essenceDropper;
    /// <summary>Ground items that are essence orbs (dropped by a monster) and still need to be captured.</summary>
    private readonly EssenceOrbRegistry essenceOrbs = new();
    private readonly Dictionary<long, GameWorldMonster> monstersByMonsterId = new();
    private readonly Dictionary<long, GameWorldNPC> npcsByNpcId = new();
    /// <summary>Diagnostics: sum of per-loop <see cref="TimeSpan.TotalMilliseconds"/> since last 1s log; only on worker thread.</summary>
    private double monsterAiProfileMillisSum;
    /// <summary>Diagnostics: number of loop timings accumulated into the profile sums.</summary>
    private int monsterAiProfileSampleCount;
    /// <summary>Diagnostics: first moment after which a 1s aggregate log may be emitted; null until the first tick with monsters.</summary>
    private DateTimeOffset? monsterAiProfileWindowEndUtc;
    /// <summary>Authoritative weather for this map; defaults to dry and is broadcast to all players when changed.</summary>
    private WeatherMode currentWeather = WeatherMode.Dry;

    /// <summary>Current weather mode for snapshots and <see cref="WeatherChanged"/> broadcasts.</summary>
    public WeatherMode CurrentWeather => currentWeather;

    public GameWorld(
        string id,
        string map,
        string? music,
        GameWorldOccupancyTracker occupancyTracker,
        SettingsConfig settings,
        IReadOnlyDictionary<string, MonsterConfig> monsterCatalog,
        IReadOnlyDictionary<int, MonsterConfig> monstersById,
        IReadOnlyDictionary<int, SpellConfig> spellsById,
        IReadOnlyDictionary<int, ItemConfig> itemsById,
        IReadOnlyDictionary<int, NpcConfig> npcsById,
        ProgressionConfig progression,
        RaceConfig races,
        IReadOnlyList<GameWorldDwellAreaConfig>? dwellAreas = null,
        IReadOnlyList<GameWorldTeleportSet>? teleportLocs = null,
        IReadOnlyList<GameWorldNpcPlacementConfig>? initialNpcs = null,
        NocContent? nocContent = null,
        bool raid = false,
        Func<double>? essenceRoll = null) {
        if (string.IsNullOrWhiteSpace(id)) {
            throw new ArgumentException("Game world id is required.", nameof(id));
        }
        if (string.IsNullOrWhiteSpace(map)) {
            throw new ArgumentException("Map name is required.", nameof(map));
        }
        ArgumentNullException.ThrowIfNull(occupancyTracker);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(monsterCatalog);
        ArgumentNullException.ThrowIfNull(monstersById);
        ArgumentNullException.ThrowIfNull(spellsById);
        ArgumentNullException.ThrowIfNull(itemsById);
        ArgumentNullException.ThrowIfNull(npcsById);
        this.progression = progression ?? throw new ArgumentNullException(nameof(progression));
        this.races = races ?? throw new ArgumentNullException(nameof(races));
        this.nocContent = nocContent;
        this.raid = raid;
        if (nocContent?.Mimic is { } mimicService) {
            essenceDropper = new EssenceDropper(mimicService.Config, essenceRoll ?? Random.Shared.NextDouble);
        }

        nocDispatcher = new NocDispatcher(settings.Nocthemar?.Debug ?? false, soulForgeInRange: IsSoulForgeInRange);
        this.monsterCatalog = monsterCatalog;
        this.monstersById = monstersById;
        this.spellsById = spellsById;
        this.itemsById = itemsById;
        this.npcsById = npcsById;
        var incomingQueueSize = settings.GameWorld.IncomingMessagesQueueSize;
        var batchPerDispatch = settings.GameWorld.IncomingMessagesBatchSizePerDispatch;

        this.id = id;
        this.map = map;
        this.music = music;
        this.occupancyTracker = occupancyTracker;
        this.settings = settings;
        this.teleportLocs = teleportLocs ?? Array.Empty<GameWorldTeleportSet>();
        viewRadiusX = settings.Radius.ViewRadiusX;
        viewRadiusY = settings.Radius.ViewRadiusY;
        playerSpatialGrid = new PlayersSpatialGrid(viewRadiusX, viewRadiusY);
        monsterSpatialGrid = new MonstersSpatialGrid(viewRadiusX, viewRadiusY);
        npcSpatialGrid = new NpcsSpatialGrid(viewRadiusX, viewRadiusY);
        groundStateTracker = new GroundStateTracker(
            occupancyTracker.SizeX,
            occupancyTracker.SizeY,
            viewRadiusX,
            viewRadiusY,
            settings.MaxDroppedItemsInStack,
            scheduler,
            HandleGroundEffectTick,
            HandleGroundEffectExpired);
        maxMessagesPerDispatch = batchPerDispatch;
        foreach (var teleportLoc in this.teleportLocs) {
            foreach (var sourceLoc in teleportLoc.Locs) {
                if (!teleportTargetsBySourceCell.TryAdd((sourceLoc.X, sourceLoc.Y), teleportLoc.Target)) {
                    throw new InvalidOperationException(
                        $"Game world '{id}' has duplicate teleport source cell ({sourceLoc.X}, {sourceLoc.Y}).");
                }
            }
        }
        scheduler.SetInterval(1000, RunPingVarianceCheck);
        incomingMessages = Channel.CreateBounded<GameWorldMessage>(new BoundedChannelOptions(incomingQueueSize) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        gameWorldRef = new GameWorldRef {
            World = this,
            OccupancyTracker = occupancyTracker,
            Settings = settings,
            WorldId = id,
            Map = map,
            Music = music,
            TeleportLocs = this.teleportLocs,
            SpellsById = spellsById,
            ItemsById = itemsById,
            NpcsById = npcsById,
            PlayerSpatialGrid = playerSpatialGrid,
            MonsterSpatialGrid = monsterSpatialGrid,
            NpcSpatialGrid = npcSpatialGrid,
            NpcsByNpcId = npcsByNpcId,
            NearbyNpcsByIdScratch = nearbyNpcsByIdScratch,
            NpcsPreviouslyInRangeScratch = npcsPreviouslyInRangeScratch,
            PlayerNpcVisibilityEnteredScratch = playerNpcVisibilityEnteredScratch,
            PlayerNpcVisibilityLeftNpcIdsScratch = playerNpcVisibilityLeftNpcIdsScratch,
            GroundStateTracker = groundStateTracker,
            IncomingReader = incomingMessages.Reader,
            Scheduler = scheduler,
            NearbyPlayersByIdScratch = nearbyPlayersByIdScratch,
            PlayersPreviouslyInRangeScratch = playersPreviouslyInRangeScratch,
            MonstersByMonsterId = monstersByMonsterId,
            NearbyMonstersByIdScratch = nearbyMonstersByIdScratch,
            MonstersPreviouslyInRangeScratch = monstersPreviouslyInRangeScratch,
            MonsterStepOldViewersScratch = monsterStepOldViewersScratch,
            MonsterStepNewViewersScratch = monsterStepNewViewersScratch,
            PlayerMonsterVisibilityEnteredScratch = playerMonsterVisibilityEnteredScratch,
            PlayerMonsterVisibilityLeftMonsterIdsScratch = playerMonsterVisibilityLeftMonsterIdsScratch,
            MovementNewNeighborsScratch = movementNewNeighborsScratch,
            MovementLeftNeighborIdsScratch = movementLeftNeighborIdsScratch,
            SpellAffectedCellsScratch = spellAffectedCellsScratch,
            NearbyGroundEffectsByIdScratch = nearbyGroundEffectsByIdScratch,
            GroundEffectsPreviouslyInRangeScratch = groundEffectsPreviouslyInRangeScratch,
            GroundEffectsEnteredScratch = groundEffectsEnteredScratch,
            GroundEffectsLeftScratch = groundEffectsLeftScratch,
            GroundEffectsViewersScratch = groundEffectsViewersScratch,
            NearbyGroundItemsByIdScratch = nearbyGroundItemsByIdScratch,
            GroundItemsPreviouslyInRangeScratch = groundItemsPreviouslyInRangeScratch,
            GroundItemsEnteredScratch = groundItemsEnteredScratch,
            GroundItemsLeftScratch = groundItemsLeftScratch,
            GroundItemsViewersScratch = groundItemsViewersScratch,
            GroundStatesEnteredByCellScratch = groundStatesEnteredByCellScratch,
            GroundStatesLeftByCellScratch = groundStatesLeftByCellScratch,
        };

        SpawnConfiguredNpcs(initialNpcs);
        if (dwellAreas is not null && dwellAreas.Count > 0) {
            SpawnDwellAreaMonsters(dwellAreas, monstersById);
        }
    }

    public bool TryGetMonsterByMonsterId(long monsterId, out GameWorldMonster monster) {
        return monstersByMonsterId.TryGetValue(monsterId, out monster!);
    }

    /// <summary>Removes a corpse after decay (tile was freed when the monster died): drops spatial index, notifies viewers with <see cref="MonstersLeftRange"/>; dwell spawns schedule a respawn after <see cref="MonsterDefaultsConfig.RespawnTime"/> defaults or catalog <c>respawnTime</c>.</summary>
    public void RemoveMonster(GameWorldMonster monster) {
        ArgumentNullException.ThrowIfNull(monster);
        if (!monstersByMonsterId.TryGetValue(monster.MonsterId, out var existing) || !ReferenceEquals(existing, monster)) {
            return;
        }

        var scheduleDwellRespawn = monster.HasDwellArea;
        var respawnCatalogId = monster.CatalogMonsterId;
        var respawnDwell = monster.DwellArea;
        var respawnDelayMs = settings.MonsterDefaults.RespawnTime;
        if (monstersById.TryGetValue(respawnCatalogId, out var respawnTemplate)) {
            respawnDelayMs = respawnTemplate.RespawnTime ?? settings.MonsterDefaults.RespawnTime;
        }

        var monstersLeftRangeMessage = NetworkManager.CreateMonstersLeftRange(monster.MonsterId);
        foreach (var playerId in monster.PlayersInRange) {
            if (!TryGetConnectedPlayerById(playerId, out var viewer) || viewer.Disconnected) {
                continue;
            }

            viewer.RemoveMonsterInRange(monster.MonsterId);
            NetworkManager.SendToPlayer(viewer, monstersLeftRangeMessage);
        }

        monster.ClearPlayersInRange();
        monsterSpatialGrid.Remove(monster);
        monstersByMonsterId.Remove(monster.MonsterId);

        if (scheduleDwellRespawn) {
            scheduler.SetTimeout(respawnDelayMs, () => TryRespawnDwellMonster(respawnCatalogId, respawnDwell));
        }
    }

    /// <summary>Spawns one instance of a catalog monster inside <paramref name="dwell"/> after a dwell instance was removed (summons never call this).</summary>
    private void TryRespawnDwellMonster(int catalogMonsterId, MonsterDwellArea dwell) {
        if (!monstersById.TryGetValue(catalogMonsterId, out var template)) {
            Console.WriteLine($"[GameWorld:{id}] Monster respawn: catalog id {catalogMonsterId} is not defined.");
            return;
        }

        var movementSpeedMs = template.MovementSpeed > 0 ? template.MovementSpeed : (template.MovementSpeed == 0 ? 0 : 220);
        if (!TryFindFreeCellInDwell(dwell, monsterAiRandom, out var sx, out var sy)) {
            Console.WriteLine($"[GameWorld:{id}] Monster respawn: no free cell in dwell for catalog id {catalogMonsterId}.");
            return;
        }

        if (!TrySpawnMonster(template, sx, sy, movementSpeedMs, dwell, hasDwellArea: true, initialFacingDirection: 4, attackTypeOverride: null, allegianceOverride: null, stunDurationMsOverride: null, maxHpOverride: null, attackDamageOverride: null, attackSpeedMsOverride: null)) {
            Console.WriteLine($"[GameWorld:{id}] Monster respawn: failed to occupy cell ({sx},{sy}) for catalog id {catalogMonsterId}.");
        }
    }

    /// <summary>Removes the player from every monster's visibility set and clears the player's monster and NPC range sets. Call before <see cref="GameWorldPlayer.DetachConnection"/> or world removal.</summary>
    private void UnlinkPlayerFromAllMonstersVisibility(GameWorldPlayer player) {
        foreach (var monsterId in player.MonstersInRange) {
            if (monstersByMonsterId.TryGetValue(monsterId, out var monster)) {
                monster.RemovePlayerInRange(player.PlayerId);
            }
        }

        player.ClearMonstersInRange();
        player.ClearNpcsInRange();
    }

    public int ConnectedPlayerCount => playersBySessionId.Count;
    public int WorkerThreadId => worker?.ManagedThreadId ?? throw new InvalidOperationException($"Game world '{id}' is not yet attached to a worker.");
    public bool RequiresPeriodicUpdate => true;

    /// <summary>Posts a message to the world's single-reader channel and wakes the worker if needed.</summary>
    public ValueTask EnqueueAsync(GameWorldMessage message, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(message);
        if (worker is null) {
            throw new InvalidOperationException($"Game world '{id}' must be registered to a worker before it can receive messages.");
        }

        if (incomingMessages.Writer.TryWrite(message)) {
            worker.Schedule(this);
            return ValueTask.CompletedTask;
        }

        return EnqueueSlowAsync(message, cancellationToken);
    }

    public void AttachToWorker(WorldWorker value) {
        ArgumentNullException.ThrowIfNull(value);
        if (worker is not null) {
            throw new InvalidOperationException($"Game world '{id}' is already attached to worker '{worker.Name}'.");
        }

        worker = value;
    }

    /// <summary>Returns true if this thread should enqueue the world onto the worker (first schedule wins).</summary>
    public bool TryMarkScheduled() {
        return Interlocked.Exchange(ref isScheduled, 1) == 0;
    }

    /// <summary>Drains up to the configured max messages per dispatch and re-schedules if the mailbox still has work.</summary>
    public void ProcessPendingMessages() {
        Volatile.Write(ref isScheduled, 0);

        var processedMessages = 0;
        while (processedMessages < maxMessagesPerDispatch &&
               incomingMessages.Reader.TryRead(out var message)) {
            HandleMessage(message);
            processedMessages++;
        }

        if (incomingMessages.Reader.TryPeek(out _)) {
            worker!.Schedule(this);
        }
    }

    /// <summary>World tick hook: scheduler jobs and monster AI run once per worker tick (<see cref="WorldWorker"/>).</summary>
    public void Update(TimeSpan _) {
        try {
            OnWorldTick();
        } catch (Exception ex) {
            Console.Error.WriteLine($"[GameWorld:{id}] Error during world tick: {ex}");
        }
    }

    /// <summary>Async wait when the bounded mailbox is full (backpressure).</summary>
    private async ValueTask EnqueueSlowAsync(GameWorldMessage message, CancellationToken cancellationToken) {
        await incomingMessages.Writer.WriteAsync(message, cancellationToken);
        worker!.Schedule(this);
    }

    /// <summary>Dispatches one mailbox item to the appropriate handler; logs and continues on handler exceptions.</summary>
    private void HandleMessage(GameWorldMessage message) {
        try {
            switch (message) {
                case PlayerConnectedMessage connectedMessage:
                    HandlePlayerConnected(connectedMessage);
                    break;
                case PlayerReconnectedMessage reconnectedMessage:
                    HandlePlayerReconnected(reconnectedMessage);
                    break;
                case PlayerDisconnectedMessage disconnectedMessage:
                    HandlePlayerDisconnected(disconnectedMessage);
                    break;
                case RemoveDisconnectedPlayerMessage removeDisconnectedPlayerMessage:
                    HandleRemoveDisconnectedPlayer(removeDisconnectedPlayerMessage);
                    break;
                case SavePlayerStateRequestMessage savePlayerStateRequestMessage:
                    HandleSavePlayerStateRequest(savePlayerStateRequestMessage);
                    break;
                case TransferPlayerOutMessage transferPlayerOutMessage:
                    HandleTransferPlayerOut(transferPlayerOutMessage);
                    break;
                case TransferPlayerInMessage transferPlayerInMessage:
                    HandleTransferPlayerIn(transferPlayerInMessage);
                    break;
                case ClientPacketMessage packetMessage:
                    HandleClientPacket(packetMessage);
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled message type '{message.GetType().Name}' in world '{id}'.");
            }
        } catch (Exception ex) {
            Console.WriteLine($"[GameWorld:{id}] Error handling message type '{message.GetType().Name}': {ex}");
        }
    }

    /// <summary>Creates the in-world entity and runs the standard join flow (spawn, visibility, protection).</summary>
    private void HandlePlayerConnected(PlayerConnectedMessage connectedMessage) {
        var player = CreatePlayer(
            connectedMessage.SessionId,
            connectedMessage.SendMessage,
            connectedMessage.RequestDisconnect,
            connectedMessage.RequestWorldChange,
            connectedMessage.InterruptLogoutDueToCombat,
            connectedMessage.PersistedState?.X,
            connectedMessage.PersistedState?.Y);
        player.SetCharacterName(connectedMessage.CharacterName);
        if (connectedMessage.PersistedState is not null) {
            player.ApplyPersistedState(connectedMessage.PersistedState);
        } else if (connectedMessage.NewCharacter is not null) {
            player.ApplyNewCharacter(connectedMessage.NewCharacter);
        }
        Console.WriteLine($"[GameWorld:{id}] Player connected. Players on world: {playersBySessionId.Count}");
        Spawn.CompletePlayerJoin(gameWorldRef, player, includeSpellsInInitialState: true);
    }

    /// <summary>Rebinds send/disconnect callbacks, sends self state, and notifies nearby players of reconnection visibility.</summary>
    private void HandlePlayerReconnected(PlayerReconnectedMessage reconnectedMessage) {
        if (!playersBySessionId.TryGetValue(reconnectedMessage.SessionId, out var reconnectedPlayer)) {
            Console.WriteLine($"[GameWorld:{id}] Received reconnect for unknown session '{reconnectedMessage.SessionId}'.");
            return;
        }

        reconnectedPlayer.SetCharacterName(reconnectedMessage.CharacterName);
        reconnectedPlayer.AttachConnection(reconnectedMessage.SendMessage, reconnectedMessage.RequestDisconnect);
        Spawn.SendInitialState(gameWorldRef, reconnectedPlayer, includeSpells: true);
        Spawn.SendInitialGameWorldState(gameWorldRef, reconnectedPlayer);

        Movement.FillNearbyPlayersById(
            playerSpatialGrid,
            reconnectedPlayer.PosX,
            reconnectedPlayer.PosY,
            reconnectedPlayer.SessionId,
            nearbyPlayersByIdScratch);
        var nearbyPlayers = nearbyPlayersByIdScratch;
        var playerReconnectedMessage = NetworkManager.CreatePlayerReconnected(reconnectedPlayer.PlayerId);
        Movement.SendPlayersSnapshotsBulk(reconnectedPlayer, nearbyPlayers.Values);
        foreach (var nearbyPlayer in nearbyPlayers.Values) {
            if (!nearbyPlayer.Disconnected) {
                NetworkManager.SendToPlayer(nearbyPlayer, playerReconnectedMessage);
            }
        }

        reconnectedPlayer.ReplacePlayersInRange(nearbyPlayers.Keys);
        MonsterVisibility.SendMonstersInRangeOnPlayerJoin(gameWorldRef, reconnectedPlayer);
        Npc.SendNpcsInRangeOnPlayerJoin(gameWorldRef, reconnectedPlayer);
        GroundStateVisibility.SendGroundStatesInRangeOnPlayerJoin(gameWorldRef, reconnectedPlayer);
        Console.WriteLine($"[GameWorld:{id}] Player reconnected. Players on world: {playersBySessionId.Count}");
    }

    /// <summary>Detaches the socket; if the session remains in grace, broadcasts disconnected state to viewers in range.</summary>
    private void HandlePlayerDisconnected(PlayerDisconnectedMessage disconnectedMessage) {
        if (playersBySessionId.TryGetValue(disconnectedMessage.SessionId, out var disconnectedPlayer)) {
            disconnectedPlayer.DetachConnection();
            if (disconnectedMessage.SessionRemainsActive) {
                var playerDisconnectedMessage = NetworkManager.CreatePlayerDisconnected(disconnectedPlayer.PlayerId);
                foreach (var nearbyPlayer in playerSpatialGrid.GetNearbyPlayers(disconnectedPlayer.PosX, disconnectedPlayer.PosY, disconnectedPlayer.SessionId)) {
                    NetworkManager.SendToPlayer(nearbyPlayer, playerDisconnectedMessage);
                }
            } else {
                UnlinkPlayerFromAllMonstersVisibility(disconnectedPlayer);
            }
        }
        Console.WriteLine($"[GameWorld:{id}] Player disconnected. Players on world: {playersBySessionId.Count}");
    }

    /// <summary>Final removal after grace: notifies range, frees the cell, and drops player from maps and grid.</summary>
    private void HandleRemoveDisconnectedPlayer(RemoveDisconnectedPlayerMessage removeDisconnectedPlayerMessage) {
        if (!playersBySessionId.TryGetValue(removeDisconnectedPlayerMessage.SessionId, out var disconnectedPlayer)) {
            return;
        }
        if (!disconnectedPlayer.Disconnected) {
            return;
        }

        UnlinkPlayerFromAllMonstersVisibility(disconnectedPlayer);

        var removedLeftMessage = NetworkManager.CreatePlayersLeftRange(disconnectedPlayer.PlayerId);
        foreach (var nearbyPlayer in playerSpatialGrid.GetNearbyPlayers(disconnectedPlayer.PosX, disconnectedPlayer.PosY, disconnectedPlayer.SessionId)) {
            NetworkManager.SendToPlayer(nearbyPlayer, removedLeftMessage);
            nearbyPlayer.RemovePlayerInRange(disconnectedPlayer.PlayerId);
        }

        occupancyTracker.SetFree(disconnectedPlayer.PosX, disconnectedPlayer.PosY);
        playerSpatialGrid.Remove(disconnectedPlayer);
        playersMap.Remove(disconnectedPlayer.PlayerId);
        playersBySessionId.Remove(removeDisconnectedPlayerMessage.SessionId);
        Console.WriteLine($"[GameWorld:{id}] Removed disconnected player after grace period. Players on world: {playersBySessionId.Count}");
    }

    /// <summary>Strips the player from this world and completes <see cref="TransferPlayerOutMessage.Completion"/> with state for the target world.</summary>
    private void HandleTransferPlayerOut(TransferPlayerOutMessage transferPlayerOutMessage) {
        if (!playersBySessionId.TryGetValue(transferPlayerOutMessage.SessionId, out var player)) {
            transferPlayerOutMessage.Completion.TrySetResult(null);
            return;
        }

        UnlinkPlayerFromAllMonstersVisibility(player);

        var transferredLeftMessage = NetworkManager.CreatePlayersLeftRange(player.PlayerId);
        foreach (var nearbyPlayer in playerSpatialGrid.GetNearbyPlayers(player.PosX, player.PosY, player.SessionId)) {
            NetworkManager.SendToPlayer(nearbyPlayer, transferredLeftMessage);
            nearbyPlayer.RemovePlayerInRange(player.PlayerId);
        }

        occupancyTracker.SetFree(player.PosX, player.PosY);
        playerSpatialGrid.Remove(player);
        playersMap.Remove(player.PlayerId);
        playersBySessionId.Remove(transferPlayerOutMessage.SessionId);
        player.DetachConnection();
        transferPlayerOutMessage.Completion.TrySetResult(
            new TransferredPlayerState(
                transferPlayerOutMessage.SessionId,
                player.CreatePersistenceState(id)));
        Console.WriteLine($"[GameWorld:{id}] Player transferred to world '{transferPlayerOutMessage.TargetWorldId}'. Players on world: {playersBySessionId.Count}");
    }

    /// <summary>Creates a fresh in-world player from transfer state and runs the same join path as a new connection.</summary>
    private void HandleTransferPlayerIn(TransferPlayerInMessage transferPlayerInMessage) {
        var player = CreatePlayer(
            transferPlayerInMessage.Player.SessionId,
            transferPlayerInMessage.SendMessage,
            transferPlayerInMessage.RequestDisconnect,
            transferPlayerInMessage.RequestWorldChange,
            transferPlayerInMessage.InterruptLogoutDueToCombat,
            transferPlayerInMessage.SpawnX,
            transferPlayerInMessage.SpawnY);
        player.ApplyPersistedState(transferPlayerInMessage.Player.State);
        Console.WriteLine($"[GameWorld:{id}] Player transferred in. Players on world: {playersBySessionId.Count}");
        Spawn.CompletePlayerJoin(gameWorldRef, player, includeSpellsInInitialState: false);
    }

    /// <summary>Returns the player's latest authoritative snapshot for immediate persistence in <c>Server.cs</c>.</summary>
    private void HandleSavePlayerStateRequest(SavePlayerStateRequestMessage savePlayerStateRequestMessage) {
        if (!playersBySessionId.TryGetValue(savePlayerStateRequestMessage.SessionId, out var player)) {
            savePlayerStateRequestMessage.Completion.TrySetResult(null);
            return;
        }

        savePlayerStateRequestMessage.Completion.TrySetResult(player.CreatePersistenceState(id));
    }

    /// <summary>Routes deserialized client payloads to movement, combat, ping, world change, or admin occupancy requests.</summary>
    private void HandleClientPacket(ClientPacketMessage message) {
        if (!playersBySessionId.TryGetValue(message.SessionId, out var playerConnection)) {
            Console.WriteLine($"[GameWorld:{id}] Received packet for unknown session '{message.SessionId}'.");
            return;
        }

        switch (message.Message.PayloadCase) {
            case ClientMessage.PayloadOneofCase.PingRequest:
                HandlePingRequest(playerConnection, message.Message.PingRequest);
                break;
            case ClientMessage.PayloadOneofCase.RequestMovement:
                if (!IsRequestForCurrentWorld(message.Message.RequestMovement.GameWorldId)) {
                    break;
                }
                Movement.HandleRequestMovement(gameWorldRef, playerConnection, message.Message.RequestMovement);
                break;
            case ClientMessage.PayloadOneofCase.MakeServerCellOccupiedRequest: {
                    var occ = message.Message.MakeServerCellOccupiedRequest;
                    Movement.HandleMakeServerCellOccupiedRequest(gameWorldRef, occ.X, occ.Y, id);
                    break;
                }
            case ClientMessage.PayloadOneofCase.PlayerTeleportRequested:
                Movement.HandlePlayerTeleportRequested(gameWorldRef, playerConnection, message.Message.PlayerTeleportRequested, id);
                break;
            case ClientMessage.PayloadOneofCase.ChangePlayerMovementSpeedRequest:
                Movement.HandleChangePlayerMovementSpeed(gameWorldRef, playerConnection, message.Message.ChangePlayerMovementSpeedRequest);
                break;
            case ClientMessage.PayloadOneofCase.ChangePlayerAttackStunDurationRequest:
                HandleChangePlayerAttackStunDuration(playerConnection, message.Message.ChangePlayerAttackStunDurationRequest);
                break;
            case ClientMessage.PayloadOneofCase.ChangePlayerAttackSpeedRequest:
                HandleChangePlayerAttackSpeed(playerConnection, message.Message.ChangePlayerAttackSpeedRequest);
                break;
            case ClientMessage.PayloadOneofCase.ChangePlayerCastSpeedRequest:
                HandleChangePlayerCastSpeed(playerConnection, message.Message.ChangePlayerCastSpeedRequest);
                break;
            case ClientMessage.PayloadOneofCase.ChangePlayerAttackTypeRequest:
                HandleChangePlayerAttackType(playerConnection, message.Message.ChangePlayerAttackTypeRequest);
                break;
            case ClientMessage.PayloadOneofCase.ChangePlayerAllowDashAttackRequest:
                HandleChangePlayerAllowDashAttack(playerConnection, message.Message.ChangePlayerAllowDashAttackRequest);
                break;
            case ClientMessage.PayloadOneofCase.ChangePlayerAppearanceRequest:
                HandleChangePlayerAppearance(playerConnection, message.Message.ChangePlayerAppearanceRequest);
                break;
            case ClientMessage.PayloadOneofCase.CreateItemRequest:
                Inventory.HandleCreateItemRequest(gameWorldRef, playerConnection, message.Message.CreateItemRequest);
                break;
            case ClientMessage.PayloadOneofCase.MoveItemInBagRequest:
                Inventory.HandleMoveItemInBagRequest(gameWorldRef, playerConnection, message.Message.MoveItemInBagRequest);
                break;
            case ClientMessage.PayloadOneofCase.EquipItemRequest:
                Inventory.HandleEquipItemRequest(gameWorldRef, playerConnection, message.Message.EquipItemRequest);
                break;
            case ClientMessage.PayloadOneofCase.UnequipItemRequest:
                Inventory.HandleUnequipItemRequest(gameWorldRef, playerConnection, message.Message.UnequipItemRequest);
                break;
            case ClientMessage.PayloadOneofCase.ConsumeItemRequest:
                Inventory.HandleConsumeItemRequest(gameWorldRef, playerConnection, message.Message.ConsumeItemRequest);
                break;
            case ClientMessage.PayloadOneofCase.PlayerItemDropRequested:
                HandlePlayerItemDropRequested(playerConnection, message.Message.PlayerItemDropRequested);
                break;
            case ClientMessage.PayloadOneofCase.PlayerItemPickupRequested:
                HandlePlayerItemPickupRequested(playerConnection);
                break;
            case ClientMessage.PayloadOneofCase.ChangePlayerAttackRangeRequest:
                HandleChangePlayerAttackRange(playerConnection, message.Message.ChangePlayerAttackRangeRequest);
                break;
            case ClientMessage.PayloadOneofCase.ChangePlayerAttackDamageRequest:
                HandleChangePlayerAttackDamage(playerConnection, message.Message.ChangePlayerAttackDamageRequest);
                break;
            case ClientMessage.PayloadOneofCase.PlayerMovementStateChangeRequest:
                Movement.HandlePlayerMovementStateChange(gameWorldRef, playerConnection, message.Message.PlayerMovementStateChangeRequest);
                break;
            case ClientMessage.PayloadOneofCase.PlayerAttackModeChangeRequest:
                Movement.HandlePlayerAttackModeChange(gameWorldRef, playerConnection, message.Message.PlayerAttackModeChangeRequest);
                break;
            case ClientMessage.PayloadOneofCase.ChangePlayerIdleDirectionRequest:
                Movement.HandleChangePlayerIdleDirection(gameWorldRef, playerConnection, message.Message.ChangePlayerIdleDirectionRequest);
                break;
            case ClientMessage.PayloadOneofCase.WorldChangeRequest:
                HandleWorldChangeRequest(playerConnection, message.Message.WorldChangeRequest);
                break;
            case ClientMessage.PayloadOneofCase.SummonMonsterRequested:
                HandleSummonMonsterRequested(playerConnection, message.Message.SummonMonsterRequested);
                break;
            case ClientMessage.PayloadOneofCase.KillAllMonstersRequested:
                Combat.HandleKillAllMonstersRequested(gameWorldRef, playerConnection);
                break;
            case ClientMessage.PayloadOneofCase.SummonNpcRequest:
                Npc.HandleSummonNpcRequest(gameWorldRef, id, playerConnection, message.Message.SummonNpcRequest);
                break;
            case ClientMessage.PayloadOneofCase.KillAllNpcsRequest:
                Npc.HandleKillAllNpcsRequest(gameWorldRef, playerConnection);
                break;
            case ClientMessage.PayloadOneofCase.PlayerAttackedMonsterRequest:
                Combat.HandlePlayerAttackedMonsterRequest(gameWorldRef, id, playerConnection, message.Message.PlayerAttackedMonsterRequest);
                break;
            case ClientMessage.PayloadOneofCase.PlayerAttackedPlayerRequest:
                Combat.HandlePlayerAttackedPlayerRequest(gameWorldRef, id, playerConnection, message.Message.PlayerAttackedPlayerRequest);
                break;
            case ClientMessage.PayloadOneofCase.PlayerResurrectedRequest:
                HandlePlayerResurrectRequest(playerConnection);
                break;
            case ClientMessage.PayloadOneofCase.PlayerPickupRequested:
                HandlePlayerPickupRequested(playerConnection, message.Message.PlayerPickupRequested);
                break;
            case ClientMessage.PayloadOneofCase.PlayerBowStanceRequested:
                HandlePlayerBowStanceRequested(playerConnection, message.Message.PlayerBowStanceRequested);
                break;
            case ClientMessage.PayloadOneofCase.SpellCastStartRequest:
                Casting.HandleSpellCastStartRequest(gameWorldRef, spellsById, playerConnection, message.Message.SpellCastStartRequest);
                break;
            case ClientMessage.PayloadOneofCase.SpellCastCancelRequest:
                Casting.HandleSpellCastCancelRequest(gameWorldRef, playerConnection);
                break;
            case ClientMessage.PayloadOneofCase.SpellCastRequest:
                Casting.HandleSpellCastRequest(gameWorldRef, id, spellsById, playerConnection, message.Message.SpellCastRequest);
                break;
            case ClientMessage.PayloadOneofCase.WeatherChangeRequest:
                HandleWeatherChangeRequest(playerConnection, message.Message.WeatherChangeRequest);
                break;
            case ClientMessage.PayloadOneofCase.AuthenticateRequest:
                playerConnection.RequestDisconnect("Authenticate messages are only allowed before joining the game world.");
                break;
            case ClientMessage.PayloadOneofCase.NocChooseClassRequest:
            case ClientMessage.PayloadOneofCase.NocLearnSkillRequest:
            case ClientMessage.PayloadOneofCase.NocRespecRequest:
            case ClientMessage.PayloadOneofCase.NocGrantXpRequest:
            case ClientMessage.PayloadOneofCase.NocCreateItemRequest:
            case ClientMessage.PayloadOneofCase.NocForgeConsumeRequest:
            case ClientMessage.PayloadOneofCase.NocForgeFuseRequest:
            case ClientMessage.PayloadOneofCase.NocMimicEquipRequest:
                nocDispatcher.Handle(playerConnection, message.Message);
                break;
            case ClientMessage.PayloadOneofCase.None:
                break;
            default:
                throw new InvalidOperationException(
                    $"Unhandled client payload '{message.Message.PayloadCase}' in world '{id}'.");
        }
    }

    private void HandlePingRequest(GameWorldPlayer playerConnection, PingRequest pingRequest) {
        Ping.HandlePingRequest(gameWorldRef, playerConnection, pingRequest);
    }

    /// <summary>Updates authoritative weather and broadcasts <see cref="WeatherChanged"/> to every connected player in this world.</summary>
    private void HandleWeatherChangeRequest(GameWorldPlayer player, WeatherChangeRequest request) {
        var mode = request.Weather;
        if (!IsWeatherModeDefined(mode)) {
            Console.WriteLine($"[GameWorld:{id}] Ignoring weather change request with unknown mode {(int)mode}.");
            return;
        }

        if (mode == currentWeather) {
            return;
        }

        currentWeather = mode;
        var msg = NetworkManager.CreateWeatherChanged(mode);
        foreach (var recipient in playersBySessionId.Values) {
            if (recipient.Disconnected) {
                continue;
            }

            NetworkManager.SendToPlayer(recipient, msg);
        }
    }

    private static bool IsWeatherModeDefined(WeatherMode mode) {
        return mode switch {
            WeatherMode.Dry or WeatherMode.RainLight or WeatherMode.RainMedium or WeatherMode.RainHeavy
                or WeatherMode.SnowLight or WeatherMode.SnowMedium or WeatherMode.SnowHeavy => true,
            _ => false,
        };
    }

    private void RunPingVarianceCheck() {
        Ping.CheckPingVarianceAndDisconnectExcessive(gameWorldRef);
    }

    public IEnumerable<GameWorldPlayer> EnumerateConnectedPlayers() => playersBySessionId.Values;

    /// <summary>Frees the corpse tile, drops monster chase on this player, and fans out <see cref="PlayerDied"/> to viewers (including the victim).</summary>
    public void HandlePlayerDeath(GameWorldRef wr, GameWorldPlayer player) {
        if (!player.IsDead) {
            return;
        }

        player.ClearAllTemporaryEffects(wr);

        occupancyTracker.SetFree(player.PosX, player.PosY);
        foreach (var monster in monstersByMonsterId.Values) {
            monster.StopChasingPlayerIfTarget(player.PlayerId);
        }

        var diedMsg = NetworkManager.CreatePlayerDied(player.PlayerId, player.PosX, player.PosY);
        foreach (var recipient in wr.PlayerSpatialGrid.GetNearbyPlayers(player.PosX, player.PosY, excludeDisconnected: true)) {
            NetworkManager.SendToPlayer(recipient, diedMsg);
        }
    }

    /// <summary>Places the dead player on a nearby free cell, restores HP, and fans out <see cref="PlayerResurrected"/> to viewers.</summary>
    private void HandlePlayerResurrectRequest(GameWorldPlayer player) {
        if (!player.IsDead) {
            return;
        }

        var maxRadius = Math.Max(occupancyTracker.SizeX, occupancyTracker.SizeY);
        var loc = Location.FindNearestFreeLocation(occupancyTracker.IsFreeAndNotTeleportCell, player.PosX, player.PosY, maxRadius);
        if (!loc.HasValue) {
            Console.WriteLine($"[GameWorld:{id}] Resurrect failed: no free cell near ({player.PosX},{player.PosY}) for player {player.PlayerId}.");
            return;
        }

        var prevX = player.PosX;
        var prevY = player.PosY;
        var rx = loc.Value.X;
        var ry = loc.Value.Y;
        occupancyTracker.SetOccupied(rx, ry);
        Movement.SetPlayerPosition(gameWorldRef, player, rx, ry);
        player.ApplyResurrection();
        Movement.SyncPlayerVisibilityAfterMovement(
            gameWorldRef,
            player,
            prevX,
            prevY,
            rx,
            ry,
            broadcastPlayerMoved: true);
        var resMsg = NetworkManager.CreatePlayerResurrected(player.PlayerId, rx, ry, player.Hp, player.MaxHp);
        foreach (var recipient in gameWorldRef.PlayerSpatialGrid.GetNearbyPlayers(rx, ry, excludeDisconnected: true)) {
            NetworkManager.SendToPlayer(recipient, resMsg);
        }
    }

    private void HandleChangePlayerAttackStunDuration(GameWorldPlayer player, ChangePlayerAttackStunDurationRequest request) {
        player.SetAttackStunDurationMs(request.AttackStunDurationMs);
    }

    private void HandleChangePlayerAttackSpeed(GameWorldPlayer player, ChangePlayerAttackSpeedRequest request) {
        player.SetAttackSpeedMs(request.AttackSpeedMs);
    }

    private void HandleChangePlayerCastSpeed(GameWorldPlayer player, ChangePlayerCastSpeedRequest request) {
        player.SetCastSpeedMs(request.CastSpeedMs);
    }

    private void HandleChangePlayerAttackType(GameWorldPlayer player, ChangePlayerAttackTypeRequest request) {
        player.SetAttackType(request.AttackType);
    }

    private void HandleChangePlayerAllowDashAttack(GameWorldPlayer player, ChangePlayerAllowDashAttackRequest request) {
        player.SetAllowDashAttack(request.AllowDashAttack);
    }

    /// <summary>Persists appearance on the player, strips gender-incompatible equipment (broadcast to self and nearby), and fans out <see cref="Mmorpg.Network.PlayerAppearanceChanged"/> to nearby observers (excluding the actor).</summary>
    private void HandleChangePlayerAppearance(GameWorldPlayer player, ChangePlayerAppearanceRequest request) {
        if (!RaceAppearanceRules.TryApply(player, races, (int)request.Gender, (int)request.SkinColor, request.HairStyleIndex, request.UnderwearColorIndex)) {
            return; // not allowed for the player's race (T31): nothing changes, nothing is broadcast
        }

        Inventory.UnequipItemsInvalidForCurrentGender(gameWorldRef, player);
        var msg = NetworkManager.CreatePlayerAppearanceChanged(
            player.PlayerId,
            request.Gender,
            request.SkinColor,
            player.HairStyleIndex,
            player.UnderwearColorIndex);
        foreach (var nearbyPlayer in gameWorldRef.PlayerSpatialGrid.GetNearbyPlayers(player.PosX, player.PosY, player.SessionId)) {
            NetworkManager.SendToPlayer(nearbyPlayer, msg);
        }
    }

    private void HandleChangePlayerAttackRange(GameWorldPlayer player, ChangePlayerAttackRangeRequest request) {
        player.SetAttackRangeCells(request.AttackRangeCells);
    }

    private void HandleChangePlayerAttackDamage(GameWorldPlayer player, ChangePlayerAttackDamageRequest request) {
        player.SetAttackDamage(request.AttackDamage);
    }

    /// <summary>Removes one bag item from the player, places it on the current cell as the new top-most dropped item, and broadcasts the resulting visibility change.</summary>
    /// <remarks>Bag removal and ground placement are separate steps: they are not atomic and are not transactional. If a later step fails after an earlier one succeeded, the stack can be lost. Keep this in mind for any future changes here.</remarks>
    private void HandlePlayerItemDropRequested(GameWorldPlayer player, PlayerItemDropRequested request) {
        if (player.IsDead) {
            return;
        }

        if (!Inventory.TryRemoveBagItemForGroundDrop(gameWorldRef, player, request.ItemUid, out var droppedItem) || droppedItem is null) {
            return;
        }
        if (!groundStateTracker.TryAddDroppedItem(droppedItem, player.PosX, player.PosY, out var previousTopItem, out var addedItem) || addedItem is null) {
            return;
        }

        GroundStateVisibility.BroadcastGroundItemTopStateChanged(gameWorldRef, previousTopItem, addedItem);
    }

    /// <summary>Authoritative pickup: locks out other actions for animation ms minus ping variance; fans out <see cref="Mmorpg.Network.PlayerPickupPerformed"/> to nearby observers (excluding the actor).</summary>
    private void HandlePlayerPickupRequested(GameWorldPlayer player, PlayerPickupRequested request) {
        if (player.IsDead) {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (player.IsPickupOrBowStanceLockoutActive(now)) {
            return;
        }

        var d = request.Direction;
        if (d < 0 || d > 7) {
            return;
        }

        player.SetFacingDirection(d);
        player.BeginPickupActionLockout(settings.Timings.PlayerPickupAnimationTime);
        var msg = NetworkManager.CreatePlayerPickupPerformed(player.PlayerId, d, settings.Timings.PlayerPickupAnimationTime);
        foreach (var nearbyPlayer in gameWorldRef.PlayerSpatialGrid.GetNearbyPlayers(player.PosX, player.PosY, player.SessionId)) {
            NetworkManager.SendToPlayer(nearbyPlayer, msg);
        }
    }

    /// <summary>Moves the current-cell top-most ground item into the player's bag and reveals the next stack entry if present.</summary>
    /// <remarks>Ground removal and bag add are separate steps: they are not atomic and are not transactional. If a later step fails after an earlier one succeeded, the stack can be lost. Keep this in mind for any future changes here.</remarks>
    private void HandlePlayerItemPickupRequested(GameWorldPlayer player) {
        if (player.IsDead) {
            return;
        }

        // An essence orb (not an essence a player dropped) is captured with an Essence Vial or a capture passive; otherwise it stays on the ground.
        var capture = EssenceCapture.None;
        if (groundStateTracker.TryPeekTopDroppedItem(player.PosX, player.PosY, out var topItem) && topItem is not null && essenceOrbs.IsOrb(topItem.ItemUid)) {
            capture = player.PlanEssenceCapture();
            if (capture == EssenceCapture.Refused) {
                NetworkManager.SendToPlayer(player, NetworkManager.CreateNocActionRefused("You need an Essence Vial to capture this essence."));
                return;
            }
        }

        if (!groundStateTracker.TryRemoveTopDroppedItem(player.PosX, player.PosY, out var removedItem, out var revealedTopItem) || removedItem is null) {
            return;
        }
        if (!Inventory.TryAddGroundItemToBag(gameWorldRef, player, removedItem)) {
            return;
        }

        if (capture != EssenceCapture.None) {
            essenceOrbs.Release(removedItem.ItemUid);
            if (capture == EssenceCapture.UseVial) {
                Inventory.TryRemoveBagItemsById(gameWorldRef, player, MimicIds.EssenceVialItemId, 1);
            }
        }

        GroundStateVisibility.BroadcastGroundItemTopStateChanged(gameWorldRef, removedItem, revealedTopItem);
    }

    /// <summary>Authoritative bow stance (peace mode, ceremonial): valid grid direction; locks out other actions; fans out <see cref="Mmorpg.Network.PlayerBowStancePerformed"/> to nearby observers (excluding the actor).</summary>
    private void HandlePlayerBowStanceRequested(GameWorldPlayer player, PlayerBowStanceRequested request) {
        if (player.IsDead) {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (player.IsPickupOrBowStanceLockoutActive(now)) {
            return;
        }

        if (player.AttackMode) {
            return;
        }

        var gridDir = request.Direction;
        if (gridDir < 0 || gridDir > 7) {
            return;
        }

        player.SetFacingDirection(gridDir);
        player.BeginBowStanceActionLockout(settings.Timings.PlayerBowAnimationTime);
        var msg = NetworkManager.CreatePlayerBowStancePerformed(player.PlayerId, gridDir, settings.Timings.PlayerBowAnimationTime);
        foreach (var nearbyPlayer in gameWorldRef.PlayerSpatialGrid.GetNearbyPlayers(player.PosX, player.PosY, player.SessionId)) {
            NetworkManager.SendToPlayer(nearbyPlayer, msg);
        }
    }

    /// <summary>Debug summon: validates request fields, then spawns up to <c>summon_count</c> catalog monsters on free cells near the player.</summary>
    private void HandleSummonMonsterRequested(GameWorldPlayer player, SummonMonsterRequested request) {
        if (player.IsDead) {
            return;
        }
        if (player.IsPickupOrBowStanceLockoutActive(DateTimeOffset.UtcNow)) {
            return;
        }
        if (string.IsNullOrWhiteSpace(request.Sprite)) {
            return;
        }

        if (!monsterCatalog.TryGetValue(request.Sprite.Trim(), out var catalogEntry)) {
            Console.WriteLine($"[GameWorld:{id}] Unknown monster sprite '{request.Sprite}' from player '{player.PlayerId}'.");
            return;
        }

        var summonSearchRadius = Math.Max(occupancyTracker.SizeX, occupancyTracker.SizeY);
        var maxX = Math.Max(0, occupancyTracker.SizeX - 1);
        var maxY = Math.Max(0, occupancyTracker.SizeY - 1);
        var dwell = new MonsterDwellArea(0, 0, maxX, maxY);
        var speedMs = request.MovementSpeedMs;
        if (speedMs == 2000) {
            speedMs = 0;
        } else if (speedMs <= 0) {
            speedMs = 0;
        }
        var facing = request.Direction;
        if (facing < 0 || facing > 7) {
            return;
        }
        var atk = request.AttackType;
        if (atk < 0 || atk > 3) {
            return;
        }
        var allegianceValue = request.Allegiance;
        if (allegianceValue < (int)MonsterAllegiance.Hostile || allegianceValue > (int)MonsterAllegiance.Friendly) {
            return;
        }
        var stunMs = request.StunDurationMs;
        if (stunMs < 100 || stunMs > 2000) {
            return;
        }
        var maxHpReq = request.MaxHp;
        if (maxHpReq < 1 || maxHpReq > 1000) {
            return;
        }
        var attackDamage = request.AttackDamage;
        if (attackDamage < 1 || attackDamage > 1000) {
            return;
        }
        var attackSpeedMsReq = request.AttackSpeedMs;
        if (attackSpeedMsReq < 200 || attackSpeedMsReq > 2000) {
            return;
        }
        int? attackRecoveryMsOverride = null;
        if (request.HasAttackRecoveryMs) {
            var recoveryMs = request.AttackRecoveryMs;
            if (recoveryMs < 0 || recoveryMs > 2000) {
                return;
            }
            attackRecoveryMsOverride = recoveryMs;
        }
        int? chaseMaxDistanceCellsOverride = null;
        if (request.HasChaseRangeCells) {
            var chaseRange = request.ChaseRangeCells;
            if (chaseRange < 1 || chaseRange > 20) {
                return;
            }
            chaseMaxDistanceCellsOverride = chaseRange;
        }
        int? attackRangeCellsOverride = null;
        if (request.HasAttackRangeCells) {
            var attackRange = request.AttackRangeCells;
            if (attackRange < 1 || attackRange > 20) {
                return;
            }
            attackRangeCellsOverride = attackRange;
        }
        var summonCount = request.SummonCount;
        if (summonCount < 1 || summonCount > 1000) {
            return;
        }

        var summoned = 0;
        for (var i = 0; i < summonCount; i++) {
            var freeCell = Location.FindNearestFreeLocation(
                occupancyTracker.IsFreeAndNotTeleportCell,
                player.PosX,
                player.PosY,
                summonSearchRadius);
            if (!freeCell.HasValue) {
                if (summoned == 0) {
                    Console.WriteLine($"[GameWorld:{id}] No free cell near player '{player.PlayerId}' for monster summon.");
                }
                break;
            }

            var spawnX = freeCell.Value.X;
            var spawnY = freeCell.Value.Y;
            if (!TrySpawnMonster(
                    catalogEntry,
                    spawnX,
                    spawnY,
                    speedMs,
                    dwell,
                    hasDwellArea: false,
                    facing,
                    (AttackType)atk,
                    (MonsterAllegiance)allegianceValue,
                    stunMs,
                    maxHpReq,
                    attackDamage,
                    attackSpeedMsReq,
                    attackRecoveryMsOverride,
                    chaseMaxDistanceCellsOverride,
                    attackRangeCellsOverride)) {
                Console.WriteLine($"[GameWorld:{id}] Summon failed to place monster at ({spawnX},{spawnY}) for player '{player.PlayerId}'.");
                break;
            }

            summoned++;
        }

        if (summoned > 0) {
            Console.WriteLine(
                $"[GameWorld:{id}] Summoned {summoned} monster(s) '{catalogEntry.Name}' ({catalogEntry.Sprite}) near player '{player.PlayerId}'.");
        }
    }

    private void HandleWorldChangeRequest(GameWorldPlayer player, WorldChangeRequest request) {
        if (player.IsDead) {
            return;
        }
        if (player.IsPickupOrBowStanceLockoutActive(DateTimeOffset.UtcNow)) {
            return;
        }
        if (!IsRequestForCurrentWorld(request.GameWorldId)) {
            return;
        }
        if (string.IsNullOrWhiteSpace(request.WorldId) ||
            string.Equals(request.WorldId, id, StringComparison.Ordinal)) {
            return;
        }
        if (!request.ValidateTeleport) {
            player.RequestWorldChange(new WorldTransferDestination(request.WorldId, null, null));
            return;
        }
        var teleportTarget = ResolveTeleportTargetNearPlayer(player, request.WorldId);
        if (teleportTarget is null) {
            Console.WriteLine(
                $"[GameWorld:{id}] Invalid teleport coordinates from player '{player.PlayerId}' at ({player.PosX}, {player.PosY}) for requested world '{request.WorldId}'.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.WorldId) &&
            !string.Equals(request.WorldId, teleportTarget.WorldId, StringComparison.Ordinal)) {
            Console.WriteLine(
                $"[GameWorld:{id}] Teleport world mismatch for player '{player.PlayerId}' at ({player.PosX}, {player.PosY}): requested '{request.WorldId}', authoritative '{teleportTarget.WorldId}'.");
        }

        player.RequestWorldChange(new WorldTransferDestination(
            teleportTarget.WorldId,
            teleportTarget.Loc.X,
            teleportTarget.Loc.Y));
    }

    /// <summary>Rejects stale client packets that were sent from another world but arrived after the session had already transferred.</summary>
    private bool IsRequestForCurrentWorld(string requestWorldId) {
        if (string.Equals(requestWorldId, id, StringComparison.Ordinal)) {
            return true;
        }

        return false;
    }

    /// <summary>Finds the nearest configured teleport source cell within a small Chebyshev radius so ordered packet handling can tolerate slight position lag.</summary>
    private GameWorldTeleportTarget? ResolveTeleportTargetNearPlayer(GameWorldPlayer player, string requestedWorldId) {
        GameWorldTeleportTarget? matchedTeleportTarget = null;
        var bestDistance = int.MaxValue;

        foreach (var ((sourceX, sourceY), candidateTarget) in teleportTargetsBySourceCell) {
            if (!string.IsNullOrWhiteSpace(requestedWorldId) &&
                !string.Equals(candidateTarget.WorldId, requestedWorldId, StringComparison.Ordinal)) {
                continue;
            }

            var distance = Location.GetDistance(player.PosX, player.PosY, sourceX, sourceY);
            if (distance > TeleportValidationRadius || distance >= bestDistance) {
                continue;
            }

            bestDistance = distance;
            matchedTeleportTarget = candidateTarget;
        }

        return matchedTeleportTarget;
    }

    /// <summary>Allocates spawn cell, constructs <see cref="GameWorldPlayer"/>, registers maps and spatial grid.</summary>
    private GameWorldPlayer CreatePlayer(
        Guid sessionId,
        Action<ServerMessage> sendMessage,
        Action<string?> requestDisconnect,
        Action<WorldTransferDestination> requestWorldChange,
        Action interruptLogoutDueToCombat,
        int? preferredSpawnX = null,
        int? preferredSpawnY = null) {
        var spawnLocation = preferredSpawnX.HasValue && preferredSpawnY.HasValue
            ? Spawn.GetSpawnLocation(gameWorldRef, preferredSpawnX.Value, preferredSpawnY.Value)
            : Spawn.GetSpawnLocation(gameWorldRef);
        var player = new GameWorldPlayer(
            sessionId,
            sendMessage,
            requestDisconnect,
            requestWorldChange,
            interruptLogoutDueToCombat,
            itemsById,
            settings.MovementSpeedViolationsChecker,
            settings.Ping.VarianceSampleSize,
            settings.Timings.AntiHackTimingLagFactor,
            progression,
            races,
            nocContent);
        player.SetInitialState(spawnLocation.X, spawnLocation.Y);
        occupancyTracker.SetOccupied(spawnLocation.X, spawnLocation.Y);
        playersBySessionId[sessionId] = player;
        playersMap[player.PlayerId] = player;
        playerSpatialGrid.Add(player, player.PosX, player.PosY);
        return player;
    }

    /// <summary>
    /// Credits the killing blow of a player on a monster with its XP (<c>xp</c> in Monsters.json, else <c>defaultMonsterXp</c>), scaled by the killer's XP gain modifiers (race),
    /// and with the reputation of the monster's faction (<c>factionId</c>/<c>repOnKill</c>; only for a killer who joined a faction). Only the killer is credited.
    /// Called only from the player-attack paths in <see cref="Server.Helpers.Combat"/>: monster-vs-monster kills and the admin kill-all never reach it.
    /// </summary>
    public void OnMonsterKilledByPlayer(GameWorldPlayer killer, GameWorldMonster monster) {
        ArgumentNullException.ThrowIfNull(killer);
        ArgumentNullException.ThrowIfNull(monster);
        monstersById.TryGetValue(monster.CatalogMonsterId, out var monsterConfig);
        killer.RegisterKill();
        if (monsterConfig?.FactionId is int monsterFactionId) {
            killer.RegisterFactionKill(monsterFactionId, monsterConfig.RepOnKill);
        }

        killer.GrantKillXp(progression.ResolveMonsterXp(monsterConfig?.Xp));
        DropEssence(monster);
    }

    /// <summary>True when a Soul Forge NPC of this world stands within <see cref="SoulForge.RangeCells"/> of the player.</summary>
    private bool IsSoulForgeInRange(GameWorldPlayer player) {
        foreach (var npc in npcsByNpcId.Values) {
            if (npc.CatalogNpcId == SoulForge.NpcCatalogId && SoulForge.IsInRange(player.PosX, player.PosY, npc.PosX, npc.PosY)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Leaves on the monster's cell, when the dice allow it, an Essence Vial (any monster; <c>essenceVialDropChance</c>) and the essence orb of the monster's ability (raid worlds only for a mythic).
    /// The orb is dropped last so it stays on top; it must be captured to be picked up, the vial is an ordinary item.
    /// </summary>
    private void DropEssence(GameWorldMonster monster) {
        if (essenceDropper is null) {
            return;
        }

        if (essenceDropper.RollVial()) {
            LeaveOnGround(MimicIds.EssenceVialItemId, monster, isOrb: false);
        }

        var ability = essenceDropper.Roll(monster.CatalogMonsterId, raid);
        if (ability?.EssenceItemId is int essenceItemId) {
            LeaveOnGround(essenceItemId, monster, isOrb: true);
        }
    }

    private void LeaveOnGround(int itemId, GameWorldMonster monster, bool isOrb) {
        if (!itemsById.ContainsKey(itemId)) {
            return;
        }

        var item = new InventoryItemState(itemId, InventoryManager.CreateItemUid(), bagX: null, bagY: null, quantity: 1, bagZIndex: 0, effectOverrides: null);
        if (!groundStateTracker.TryAddDroppedItem(item, monster.PosX, monster.PosY, out var previousTopItem, out var addedItem) || addedItem is null) {
            return;
        }

        if (isOrb) {
            essenceOrbs.Mark(addedItem.ItemUid);
        }

        GroundStateVisibility.BroadcastGroundItemTopStateChanged(gameWorldRef, previousTopItem, addedItem);
    }

    public bool TryGetPlayerBySessionId(Guid sessionId, out GameWorldPlayer player) {
        return playersBySessionId.TryGetValue(sessionId, out player!);
    }

    public bool TryGetConnectedPlayerById(long playerId, out GameWorldPlayer player) {
        return playersMap.TryGetValue(playerId, out player!);
    }

    /// <summary>Returns the approximate center cell for fallback login placement; actual spawn still uses nearest-free lookup.</summary>
    public (int X, int Y) GetCenterSpawnHint() {
        return (occupancyTracker.SizeX / 2, occupancyTracker.SizeY / 2);
    }

    /// <summary>Runs due <see cref="Scheduler"/> callbacks and one monster AI pass per world tick.</summary>
    private void OnWorldTick() {
        scheduler.TriggerDueItems();
        var now = DateTimeOffset.UtcNow;
        var nowMs = now.ToUnixTimeMilliseconds();
        foreach (var player in playersBySessionId.Values) {
            player.TickMana(nowMs);
            player.TickHp(nowMs);
            player.TickBuffs(nowMs);
            player.TryFlushNocUpdates();
        }

        var profileMonsterAi = settings.Debug.ProfileMonstersAILoop;
        if (monstersByMonsterId.Count > 0) {
            if (profileMonsterAi) {
                monsterAiProfileWindowEndUtc ??= now.AddSeconds(1);
            }
            var loopStart = profileMonsterAi ? DateTimeOffset.UtcNow : default;
            monsterAiTickScratch.Clear();
            foreach (var monster in monstersByMonsterId.Values) {
                monsterAiTickScratch.Add(monster);
            }

            foreach (var monster in monsterAiTickScratch) {
                monster.TickAi(gameWorldRef, monsterAiRandom, now);
            }
            if (profileMonsterAi) {
                monsterAiProfileMillisSum += (DateTimeOffset.UtcNow - loopStart).TotalMilliseconds;
                monsterAiProfileSampleCount++;
            }
        }

        if (profileMonsterAi) {
            FlushMonsterAiProfileIfDue(now);
            if (monstersByMonsterId.Count == 0 && monsterAiProfileSampleCount == 0) {
                monsterAiProfileWindowEndUtc = null;
            }
        }
    }

    /// <summary>When the current 1s window has ended, logs total and mean loop time in ms, then starts a new window from <paramref name="now"/>.</summary>
    private void FlushMonsterAiProfileIfDue(DateTimeOffset now) {
        if (monsterAiProfileWindowEndUtc is null || now < monsterAiProfileWindowEndUtc.Value) {
            return;
        }

        if (monsterAiProfileSampleCount > 0) {
            var totalMillis = monsterAiProfileMillisSum;
            var averageMillis = totalMillis / monsterAiProfileSampleCount;
            Console.WriteLine(
                $"[GameWorld:{id}] Monster AI loop (1s window): total {totalMillis:F3} ms, avg {averageMillis:F3} ms, {monsterAiProfileSampleCount} samples");
            monsterAiProfileMillisSum = 0;
            monsterAiProfileSampleCount = 0;
        }

        monsterAiProfileWindowEndUtc = now.AddSeconds(1);
    }

    /// <summary>Spawns <see cref="GameWorldConfig.Npcs"/> after the world ref is initialized and before dwell monsters so random spawns cannot take the same walkable cell.</summary>
    private void SpawnConfiguredNpcs(IReadOnlyList<GameWorldNpcPlacementConfig>? configs) {
        if (configs is null || configs.Count == 0) {
            return;
        }

        foreach (var p in configs) {
            Npc.SpawnWorldNpcAtCell(gameWorldRef, p.NpcId, p.X, p.Y, p.Direction);
        }
    }

    /// <summary>Places configured dwell populations after the world ref is initialized; logs and skips cells that stay blocked.</summary>
    private void SpawnDwellAreaMonsters(IReadOnlyList<GameWorldDwellAreaConfig> configs, IReadOnlyDictionary<int, MonsterConfig> catalogByMonsterId) {
        var maxX = Math.Max(0, occupancyTracker.SizeX - 1);
        var maxY = Math.Max(0, occupancyTracker.SizeY - 1);
        foreach (var cfg in configs) {
            if (!catalogByMonsterId.TryGetValue(cfg.MonsterId, out var template)) {
                continue;
            }

            var dwell = ClampDwellBoundsToArea(cfg.Area, maxX, maxY);
            var movementSpeedMs = template.MovementSpeed > 0 ? template.MovementSpeed : (template.MovementSpeed == 0 ? 0 : 220);
            for (var i = 0; i < cfg.Count; i++) {
                if (!TryFindFreeCellInDwell(dwell, monsterAiRandom, out var sx, out var sy)) {
                    Console.WriteLine(
                        $"[GameWorld:{id}] Dwell spawn: no free cell for monster id {cfg.MonsterId} ({i + 1}/{cfg.Count}) in configured area.");
                    continue;
                }

                if (!TrySpawnMonster(template, sx, sy, movementSpeedMs, dwell, hasDwellArea: true, initialFacingDirection: 4, attackTypeOverride: null, allegianceOverride: null, stunDurationMsOverride: null, maxHpOverride: null, attackDamageOverride: null, attackSpeedMsOverride: null)) {
                    Console.WriteLine(
                        $"[GameWorld:{id}] Dwell spawn: failed to occupy cell ({sx},{sy}) for monster id {cfg.MonsterId}.");
                }
            }
        }
    }

    private static MonsterDwellArea ClampDwellBoundsToArea(GameWorldDwellAreaBoundsConfig? area, int maxX, int maxY) {
        int rawX1;
        int rawY1;
        int rawX2;
        int rawY2;
        if (area is null) {
            rawX1 = 0;
            rawY1 = 0;
            rawX2 = maxX;
            rawY2 = maxY;
        } else {
            rawX1 = area.X1;
            rawY1 = area.Y1;
            rawX2 = area.X2;
            rawY2 = area.Y2;
        }

        var xLo = Math.Clamp(Math.Min(rawX1, rawX2), 0, maxX);
        var xHi = Math.Clamp(Math.Max(rawX1, rawX2), 0, maxX);
        var yLo = Math.Clamp(Math.Min(rawY1, rawY2), 0, maxY);
        var yHi = Math.Clamp(Math.Max(rawY1, rawY2), 0, maxY);
        return new MonsterDwellArea(xLo, yLo, xHi, yHi);
    }

    /// <summary>Random then scan for a walkable non-teleport cell inside the inclusive dwell rectangle.</summary>
    private bool TryFindFreeCellInDwell(MonsterDwellArea dwell, Random random, out int spawnX, out int spawnY) {
        var xMin = Math.Min(dwell.X1, dwell.X2);
        var xMax = Math.Max(dwell.X1, dwell.X2);
        var yMin = Math.Min(dwell.Y1, dwell.Y2);
        var yMax = Math.Max(dwell.Y1, dwell.Y2);
        const int maxRandomAttempts = 400;
        for (var attempt = 0; attempt < maxRandomAttempts; attempt++) {
            var rx = random.Next(xMin, xMax + 1);
            var ry = random.Next(yMin, yMax + 1);
            if (occupancyTracker.IsFreeAndNotTeleportCell(rx, ry)) {
                spawnX = rx;
                spawnY = ry;
                return true;
            }
        }

        for (var ry = yMin; ry <= yMax; ry++) {
            for (var rx = xMin; rx <= xMax; rx++) {
                if (occupancyTracker.IsFreeAndNotTeleportCell(rx, ry)) {
                    spawnX = rx;
                    spawnY = ry;
                    return true;
                }
            }
        }

        spawnX = 0;
        spawnY = 0;
        return false;
    }

    /// <summary>Creates a monster, occupies the cell, indexes it in maps, and notifies nearby players. <paramref name="initialFacingDirection"/> is authoritative grid facing 0–7 (matches client direction indices). When <paramref name="attackTypeOverride"/> is set (summon dialog), it overrides the catalog&apos;s attack type. When <paramref name="allegianceOverride"/> is set, it overrides catalog <c>allegiance</c> (hostile auto-aggro vs neutral retaliate-only). When <paramref name="stunDurationMsOverride"/> is set, it overrides <c>attackStunDuration</c> from the catalog for player stunlock duration. When <paramref name="maxHpOverride"/> is set, it overrides catalog <c>hp</c> for initial max/current HP. When <paramref name="attackDamageOverride"/> is set, both <see cref="GameWorldMonster.AttackDamageMin"/> and <see cref="GameWorldMonster.AttackDamageMax"/> are set to that value. When <paramref name="attackSpeedMsOverride"/> is set, it overrides catalog <c>attackSpeed</c> (full swing duration in ms). When <paramref name="attackRecoveryMsOverride"/> is set, it overrides catalog <c>attackRecoveryTime</c> (post-hit idle gate in ms, plus half swing). When <paramref name="chaseMaxDistanceCellsOverride"/> is set, it overrides catalog <c>chaseMaxDistance</c> (max Chebyshev cells before chase is dropped). When <paramref name="attackRangeCellsOverride"/> is set, it overrides catalog <c>attackRange</c> (Chebyshev cells for melee reach).</summary>
    private bool TrySpawnMonster(
        MonsterConfig template,
        int spawnX,
        int spawnY,
        int movementSpeedMs,
        MonsterDwellArea dwell,
        bool hasDwellArea,
        int initialFacingDirection,
        AttackType? attackTypeOverride,
        MonsterAllegiance? allegianceOverride,
        int? stunDurationMsOverride,
        int? maxHpOverride,
        int? attackDamageOverride,
        int? attackSpeedMsOverride,
        int? attackRecoveryMsOverride = null,
        int? chaseMaxDistanceCellsOverride = null,
        int? attackRangeCellsOverride = null) {
        if (!occupancyTracker.IsFreeAndNotTeleportCell(spawnX, spawnY)) {
            return false;
        }

        var monsterGuid = Guid.NewGuid();
        var chaseDistanceCells = template.ChaseDistance ?? settings.MonsterDefaults.ChaseDistance;
        var chaseMaxDistanceCells = chaseMaxDistanceCellsOverride ?? template.ChaseMaxDistance ?? settings.MonsterDefaults.ChaseMaxDistance;
        var attackRangeCells = attackRangeCellsOverride ?? template.AttackRange ?? 1;
        var attackSpeedMs = attackSpeedMsOverride ?? template.AttackSpeed ?? settings.MonsterDefaults.AttackSpeed;
        int attackDamageMin;
        int attackDamageMax;
        if (attackDamageOverride.HasValue) {
            attackDamageMin = attackDamageOverride.Value;
            attackDamageMax = attackDamageOverride.Value;
        } else {
            attackDamageMin = template.AttackDamageMin ?? settings.MonsterDefaults.AttackDamageMin;
            attackDamageMax = template.AttackDamageMax ?? settings.MonsterDefaults.AttackDamageMax;
        }
        var attackRecoveryMs = attackRecoveryMsOverride ?? template.AttackRecoveryTime ?? settings.MonsterDefaults.AttackRecoveryTime;
        var minIdleTimeMs = template.MinIdleTime ?? settings.MonsterDefaults.MinIdleTime;
        var maxIdleTimeMs = template.MaxIdleTime ?? settings.MonsterDefaults.MaxIdleTime;
        var attackType = attackTypeOverride ?? (AttackType)(template.AttackType ?? 0);
        var allegiance = allegianceOverride ?? (MonsterAllegiance)(template.Allegiance ?? 0);
        var stunDurationMs = stunDurationMsOverride ?? template.AttackStunDuration ?? 100;
        var rangedAttack = template.RangedAttack ?? false;
        var maxHp = maxHpOverride ?? template.Hp ?? settings.MonsterDefaults.Hp;
        var corpseDecayMs = template.CorpseDecayTime ?? settings.MonsterDefaults.CorpseDecayTime;
        var monster = new GameWorldMonster(
            monsterGuid,
            template.Name,
            template.Sprite,
            spawnX,
            spawnY,
            movementSpeedMs,
            chaseDistanceCells,
            chaseMaxDistanceCells,
            attackRangeCells,
            attackSpeedMs,
            attackDamageMin,
            attackDamageMax,
            attackRecoveryMs,
            minIdleTimeMs,
            maxIdleTimeMs,
            dwell,
            hasDwellArea,
            attackType,
            allegiance,
            stunDurationMs,
            rangedAttack,
            MonsterEntityState.Idle,
            maxHp,
            corpseDecayMs,
            template.Id,
            initialFacingDirection,
            template.Spells ?? Array.Empty<MonsterSpellEntry>());
        occupancyTracker.SetOccupied(spawnX, spawnY);
        monstersByMonsterId[monster.MonsterId] = monster;
        monsterSpatialGrid.Add(monster, spawnX, spawnY);
        MonsterVisibility.BroadcastMonsterSpawnToNearbyPlayers(gameWorldRef, monster);
        return true;
    }

    /// <summary>Ground-effect scheduler callback for periodic fields: damages any player or monster currently occupying the effect cell.</summary>
    private void HandleGroundEffectTick(GroundEffectState effect) {
        foreach (var targetPlayer in gameWorldRef.PlayerSpatialGrid.GetPlayersInRectangle(effect.PosX, effect.PosY, effect.PosX, effect.PosY, excludeDisconnected: false)) {
            if (targetPlayer.PosX != effect.PosX || targetPlayer.PosY != effect.PosY) {
                continue;
            }

            Combat.ApplyGroundEffectDamageToPlayer(gameWorldRef, effect.CasterPlayerId, effect.DamagePerTick, targetPlayer, effect.SpellAttackType, effect.SpellId);
        }

        foreach (var targetMonster in gameWorldRef.MonsterSpatialGrid.GetMonstersInRectangle(effect.PosX, effect.PosY, effect.PosX, effect.PosY)) {
            if (targetMonster.PosX != effect.PosX || targetMonster.PosY != effect.PosY || targetMonster.Dead) {
                continue;
            }

            Combat.ApplyGroundEffectDamageToMonster(gameWorldRef, effect.CasterPlayerId, effect.DamagePerTick, targetMonster, effect.SpellAttackType, effect.SpellId);
        }
    }

    /// <summary>Ground-effect expiry callback: removes the expired effect from nearby clients while leaving other cell effects intact.</summary>
    private void HandleGroundEffectExpired(GroundEffectState effect) {
        GroundStateVisibility.BroadcastGroundEffectsRemoved(gameWorldRef, effect);
    }

}
