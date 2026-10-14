using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime;
using System.Text.Json;
using System.Threading.Channels;
using Google.Protobuf;
using Microsoft.Extensions.Hosting;
using Mmorpg.Network;
using Server;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Nocthemar.Net;
using Server.Nocthemar.Persistence;
using Server.Nocthemar.Skills;
using Server.World;
using Server.World.Game;
using Server.World.Global;
using Server.Utils;

/// <summary>Hard cap for a single assembled inbound WebSocket binary message (anti-OOM).</summary>
const int MaxIncomingWebSocketMessageBytes = 4096;

// ASP.NET Core host: accepts WebSocket clients, authenticates once per connection, forwards gameplay
// packets to the appropriate worker-owned world via WorldRegistry, and runs background cleanup and world-transfer loops.

try {
    GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
    Console.WriteLine($"[Server] GC configured: server={GCSettings.IsServerGC}, latency={GCSettings.LatencyMode}");
} catch (InvalidOperationException ex) {
    Console.Error.WriteLine($"[Server] Failed to enable sustained low-latency GC mode: {ex.Message}");
}

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var settings = await Config.LoadSettings();
var gcMonitor = settings.Debug.EnableGcLogs ? new GarbageCollectorMonitor() : null;
var progression = await ProgressionConfig.LoadAsync();
var races = await RaceConfig.LoadAsync();
var classes = await ClassConfig.LoadAsync();
var classSkills = await SkillConfig.LoadAsync(classes);
classes.ValidateRaces(races);
// Skill handlers are registered here; NocContent fails fast when a ready skill names a handler that is not.
var nocContent = new NocContent(classes, classSkills, progression, BuiltinSkillHandlers.Register(new SkillHandlerRegistry()));
var charsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Chars");
// Fails fast on an unusable persistence.provider, before any world worker starts.
var characterStore = CharacterStoreFactory.Create(settings.Persistence, charsDirectory, progression);
var worldRegistry = new WorldRegistry(settings, progression, races, workerCount: settings.Threads.GameWorldWorkers, tickInterval: TimeSpan.FromMilliseconds(settings.GameWorld.TickInterval), nocContent: nocContent);
var sessionsByNetworkId = new ConcurrentDictionary<string, PlayerSession>(StringComparer.Ordinal);
var sessionsByServerId = new ConcurrentDictionary<Guid, PlayerSession>();

var gameWorlds = await Config.LoadGameWorldsConfig();
var gameWorldsById = gameWorlds.ToDictionary(gameWorld => gameWorld.Id, StringComparer.Ordinal);
var worldsListMessage = NetworkManager.CreateWorldsList(gameWorlds);
var monstersConfig = await Config.LoadMonstersConfig();
var monstersListMessage = NetworkManager.CreateMonstersList(monstersConfig);
var spellsConfig = await Config.LoadSpellsConfig();
var (monsterCatalog, monstersById) = Config.BuildMonsterCatalog(monstersConfig);
var spellsById = Config.BuildSpellCatalog(spellsConfig);
Config.ValidateMonsterSpellReferences(monstersById, spellsById);
var itemsConfig = await Config.LoadItemsConfig();
var itemsById = Config.BuildItemCatalog(itemsConfig);
var npcsConfig = await Config.LoadNpcsConfig();
var npcsById = Config.BuildNpcCatalog(npcsConfig);
var mapsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Config", "maps");
foreach (var gw in gameWorlds) {
    Config.ValidateGameWorldDwellAreas(gw, monstersById);
    Config.ValidateGameWorldNpcPlacements(gw, npcsById);
    Config.ValidateGameWorldNpcNotOnTeleportCells(gw);
    var teleportLocs = ResolveTeleportLocs(gw, gameWorldsById);
    var occupancyTracker = Map.LoadOccupancy(
        mapsDirectory,
        gw.Map,
        teleportLocs.SelectMany(teleportLoc => teleportLoc.Locs));
    Config.ValidateGameWorldNpcBounds(gw, occupancyTracker);
    var world = worldRegistry.RegisterGameWorld(
        gw.Id,
        gw.Map,
        gw.Music,
        occupancyTracker,
        monsterCatalog,
        monstersById,
        spellsById,
        itemsById,
        npcsById,
        gw.DwellAreas,
        teleportLocs,
        gw.Npcs,
        gw.WorkerThread);
    Console.WriteLine($"[Server] Loaded game world '{gw.Id}' ({gw.Name}): map {gw.Map}, size {occupancyTracker.SizeX}x{occupancyTracker.SizeY}, {occupancyTracker.OccupiedCount} blocked cells, worker thread {world.WorkerThreadId}");
}
var globalWorld = worldRegistry.RegisterGlobalWorld(new GlobalWorld("global", settings), settings.Threads.GlobalWorldWorkerThread);
Console.WriteLine($"[Server] Loaded global world 'global': worker thread {globalWorld.WorkerThreadId}");

ValueTask RouteClientPacketAsync(PlayerSession session, ClientMessage clientMessage, CancellationToken cancellationToken) {
    if (GlobalPacketRouting.ShouldRouteToGlobalWorld(clientMessage)) {
        return worldRegistry.RouteGlobalMessageAsync(
            new GlobalClientPacketMessage(session.SessionId, clientMessage),
            cancellationToken);
    }

    return worldRegistry.RouteGameWorldMessageAsync(
        GetCurrentGameWorldId(session),
        new ClientPacketMessage(session.SessionId, clientMessage),
        cancellationToken);
}

using var disconnectedPlayerCleanupCts = CancellationTokenSource.CreateLinkedTokenSource(appLifetime.ApplicationStopping);
using var worldTransferCts = CancellationTokenSource.CreateLinkedTokenSource(appLifetime.ApplicationStopping);
var worldTransferRequests = Channel.CreateUnbounded<WorldTransferRequest>(new UnboundedChannelOptions {
    SingleReader = true,
    SingleWriter = false,
});
var disconnectedPlayerCleanupTask = RunDisconnectedPlayerCleanupLoopAsync(
    worldRegistry,
    sessionsByNetworkId,
    sessionsByServerId,
    disconnectedPlayerCleanupCts.Token);
var worldTransferTask = RunWorldTransferLoopAsync(
    worldRegistry,
    sessionsByServerId,
    worldTransferRequests.Reader,
    characterStore,
    worldTransferCts.Token);
app.Lifetime.ApplicationStopping.Register(() => {
    // Persist before disposing the registry: workers stop processing mailboxes on dispose, so WebSocket
    // teardown can no longer route SavePlayerStateRequestMessage and would skip saves on CTRL+C.
    try {
        // Run persistence on the thread pool so we await async work without sync-over-async on the host
        // stopping callback (avoids SynchronizationContext deadlocks).
        Task.Run(() => PersistAllPlayerStatesOnShutdownAsync(worldRegistry, sessionsByServerId, characterStore))
            .GetAwaiter()
            .GetResult();
    } catch (Exception ex) {
        Console.Error.WriteLine($"[Server] Error persisting player state during shutdown: {ex}");
    }
    disconnectedPlayerCleanupCts.Cancel();
    worldTransferCts.Cancel();
    worldRegistry.Dispose();
    gcMonitor?.Dispose();
});

app.UseWebSockets();

// Per-connection state machine: authenticate → route binary ClientMessage to GameWorld; teardown notifies world and drains send queue.
app.Map("/ws", async context => {
    if (!context.WebSockets.IsWebSocketRequest) {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Expected a WebSocket request.");
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    Console.WriteLine($"[Server] WebSocket opened from {context.Connection.RemoteIpAddress}:{context.Connection.RemotePort}");

    var currentGameWorldId = settings.InitialMap;
    PlayerSession? authenticatedSession = null;
    var disconnectRequested = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, appLifetime.ApplicationStopping);
    using var authenticationTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(receiveCts.Token);
    authenticationTimeoutCts.CancelAfter(settings.Ping.Timeout);
    using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, appLifetime.ApplicationStopping);
    using var sendLock = new SemaphoreSlim(1, 1);
    var receiveBuffer = ArrayPool<byte>.Shared.Rent(MaxIncomingWebSocketMessageBytes);
    var messageScratch = ArrayPool<byte>.Shared.Rent(MaxIncomingWebSocketMessageBytes);
    var outgoingMessages = Channel.CreateUnbounded<ServerMessage>(new UnboundedChannelOptions {
        SingleReader = true,
        SingleWriter = false,
    });
    var disconnectCloseTask = SendServerDisconnectAsync(webSocket, sendLock, disconnectRequested.Task);
    var sendLoopTask = SendOutgoingMessagesAsync(
        webSocket,
        outgoingMessages.Reader,
        sendLock,
        settings.EnableZeroCopyProtobufTransfer,
        settings.MaxConsecutiveOutboundSendFailures,
        receiveCts,
        sendCts.Token);
    var isConnectedToGameWorld = false;

    void EnqueueOutgoingMessage(ServerMessage responseMessage) {
        outgoingMessages.Writer.TryWrite(responseMessage);
    }

    void RequestDisconnect(string? message) {
        disconnectRequested.TrySetResult(message);
    }

    void RequestWorldChange(WorldTransferDestination destination) {
        if (authenticatedSession is null || string.IsNullOrWhiteSpace(destination.WorldId)) {
            return;
        }

        worldTransferRequests.Writer.TryWrite(
            new WorldTransferRequest(
                authenticatedSession.SessionId,
                destination.WorldId,
                destination.SpawnX,
                destination.SpawnY));
    }

    EnqueueOutgoingMessage(worldsListMessage);
    EnqueueOutgoingMessage(monstersListMessage);

    try {
        while (webSocket.State == WebSocketState.Open) {
            var receiveToken = isConnectedToGameWorld ? receiveCts.Token : authenticationTimeoutCts.Token;
            var (messageType, payload) = await ReceiveMessageAsync(webSocket, receiveBuffer, messageScratch, receiveToken);
            if (messageType == WebSocketMessageType.Close) {
                await SendCloseFrameAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closing connection",
                    webSocket,
                    sendLock,
                    receiveCts.Token);
                return;
            }

            if (messageType != WebSocketMessageType.Binary) {
                if (!isConnectedToGameWorld) {
                    RequestDisconnect("Send binary authentication message before sending other traffic.");
                    return;
                }

                continue;
            }

            ClientMessage clientMessage;
            try {
                clientMessage = ClientMessage.Parser.ParseFrom(payload.Span);
            } catch (InvalidProtocolBufferException) {
                RequestDisconnect("Failed to parse client message.");
                return;
            }

            if (!isConnectedToGameWorld) {
                if (clientMessage.PayloadCase != ClientMessage.PayloadOneofCase.AuthenticateRequest) {
                    RequestDisconnect("Authenticate before sending other messages.");
                    return;
                }

                if (!NetworkIdValidator.IsValid(clientMessage.AuthenticateRequest.Id)) {
                    RequestDisconnect("Invalid authentication id.");
                    return;
                }

                var initialGameWorldId = settings.SpawnToRandomMap && gameWorlds.Length > 0
                    ? gameWorlds[Random.Shared.Next(gameWorlds.Length)].Id
                    : settings.InitialMap;
                if (!TryAuthenticatePlayer(
                    clientMessage.AuthenticateRequest.Id,
                    clientMessage.AuthenticateRequest.CharacterName,
                    webSocket,
                    initialGameWorldId,
                    sessionsByNetworkId,
                    sessionsByServerId,
                    out authenticatedSession,
                    out var isReconnect,
                    out var authenticationError)) {
                    RequestDisconnect(authenticationError);
                    return;
                }

                var session = authenticatedSession ?? throw new InvalidOperationException("Authenticated session was not created.");
                PlayerPersistenceState? loadedPlayerState = null;
                NewCharacterChoice? newCharacter = null;
                if (!isReconnect) {
                    string? loadRefusal;
                    (loadedPlayerState, loadRefusal) = await CharacterAccess.TryLoadAsync(characterStore, session.NetworkId, receiveCts.Token, races: races);
                    if (loadRefusal is not null) {
                        // The session was registered by TryAuthenticatePlayer but never joined a world: release it, and leave the save untouched.
                        sessionsByNetworkId.TryRemove(new KeyValuePair<string, PlayerSession>(session.NetworkId, session));
                        sessionsByServerId.TryRemove(new KeyValuePair<Guid, PlayerSession>(session.SessionId, session));
                        authenticatedSession = null;
                        RequestDisconnect(loadRefusal);
                        return;
                    }

                    // T30: the creation block only counts for a character without a save; an existing save ignores it.
                    var creationResult = CharacterCreationService.Resolve(clientMessage.AuthenticateRequest.Creation, loadedPlayerState is not null, races);
                    if (creationResult.Refusal is not null) {
                        sessionsByNetworkId.TryRemove(new KeyValuePair<string, PlayerSession>(session.NetworkId, session));
                        sessionsByServerId.TryRemove(new KeyValuePair<Guid, PlayerSession>(session.SessionId, session));
                        authenticatedSession = null;
                        RequestDisconnect(creationResult.Refusal);
                        return;
                    }

                    newCharacter = creationResult.Choice;

                    if (loadedPlayerState is not null) {
                        var (resolvedGameWorldId, resolvedPlayerState) = ResolveLoadedPlayerJoin(
                            loadedPlayerState,
                            worldRegistry,
                            gameWorldsById,
                            initialGameWorldId);
                        loadedPlayerState = resolvedPlayerState;
                        lock (session.SyncRoot) {
                            session.CurrentGameWorldId = resolvedGameWorldId;
                        }
                    }
                }

                currentGameWorldId = session.CurrentGameWorldId;
                lock (session.SyncRoot) {
                    session.SendMessage = EnqueueOutgoingMessage;
                    session.RequestDisconnect = RequestDisconnect;
                    session.RequestWorldChange = RequestWorldChange;
                }
                GameWorldMessage gameWorldMessage = isReconnect
                    ? new PlayerReconnectedMessage(session.SessionId, EnqueueOutgoingMessage, RequestDisconnect, RequestWorldChange, session.CharacterName)
                    : new PlayerConnectedMessage(
                        session.SessionId,
                        EnqueueOutgoingMessage,
                        RequestDisconnect,
                        RequestWorldChange,
                        loadedPlayerState,
                        session.CharacterName,
                        CreateInterruptLogoutDueToCombat(session),
                        newCharacter);
                GlobalWorldMessage globalWorldMessage = isReconnect
                    ? new GlobalPlayerReconnectedMessage(session.SessionId, EnqueueOutgoingMessage, session.CharacterName)
                    : new GlobalPlayerConnectedMessage(session.SessionId, EnqueueOutgoingMessage, session.CharacterName);
                await worldRegistry.RouteGameWorldMessageAsync(currentGameWorldId, gameWorldMessage, receiveCts.Token);
                await worldRegistry.RouteGlobalMessageAsync(globalWorldMessage, receiveCts.Token);
                isConnectedToGameWorld = true;
                continue;
            }

            if (clientMessage.PayloadCase == ClientMessage.PayloadOneofCase.AuthenticateRequest) {
                RequestDisconnect("Authenticate may only be sent once per connection.");
                return;
            }

            if (clientMessage.PayloadCase == ClientMessage.PayloadOneofCase.LogoutRequest) {
                var waitSeconds = settings.LogoutTime;
                var logoutAllowedAt = waitSeconds > 0
                    ? DateTimeOffset.UtcNow.AddSeconds(waitSeconds).AddMilliseconds(-500)
                    : DateTimeOffset.UtcNow;
                lock (authenticatedSession!.SyncRoot) {
                    authenticatedSession.LogoutAllowedAtUtc = logoutAllowedAt;
                }
                EnqueueOutgoingMessage(new ServerMessage { LogoutResponse = new LogoutResponse { Wait = waitSeconds } });
                continue;
            }

            if (clientMessage.PayloadCase == ClientMessage.PayloadOneofCase.LogoutCancelledRequest) {
                lock (authenticatedSession!.SyncRoot) {
                    authenticatedSession.LogoutAllowedAtUtc = null;
                }
                continue;
            }

            currentGameWorldId = GetCurrentGameWorldId(authenticatedSession!);
            await RouteClientPacketAsync(authenticatedSession!, clientMessage, receiveCts.Token);
        }
    } catch (OperationCanceledException) when (!isConnectedToGameWorld &&
        authenticationTimeoutCts.IsCancellationRequested &&
        !receiveCts.IsCancellationRequested) {
        RequestDisconnect("Authentication request not received in time.");
    } catch (OperationCanceledException) {
        // Do not close here; let finally drain the send queue first
    } catch (WebSocketException ex) when (ex.WebSocketErrorCode is WebSocketError.ConnectionClosedPrematurely
        or WebSocketError.InvalidState
        or WebSocketError.Faulted) {
        // Client closed abruptly; treat as normal disconnect, handled in finally
    } catch (WebSocketException ex) {
        Console.Error.WriteLine($"[Server] WebSocket error ({ex.WebSocketErrorCode}): {ex.Message}");
    } catch (IncomingWebSocketMessageTooLargeException) {
        Console.Error.WriteLine($"[Server] WebSocket closed: incoming message exceeded {MaxIncomingWebSocketMessageBytes} bytes.");
    } catch (Exception ex) {
        Console.Error.WriteLine($"[Server] Unexpected error: {ex}");
    } finally {
        if (authenticatedSession is not null) {
            var shouldNotifyWorld = false;
            var sessionRemainsActive = false;
            lock (authenticatedSession.SyncRoot) {
                if (ReferenceEquals(authenticatedSession.WebSocket, webSocket)) {
                    authenticatedSession.WebSocket = null;
                    authenticatedSession.SendMessage = null;
                    authenticatedSession.RequestDisconnect = null;
                    authenticatedSession.RequestWorldChange = null;
                    var now = DateTimeOffset.UtcNow;
                    var allowedAt = authenticatedSession.LogoutAllowedAtUtc;
                    if (allowedAt.HasValue && now >= allowedAt.Value) {
                        authenticatedSession.DisconnectDeadlineUtc = now;
                    } else {
                        authenticatedSession.DisconnectDeadlineUtc = now.AddSeconds(settings.Timings.DisconnectTime);
                    }
                    sessionRemainsActive = authenticatedSession.DisconnectDeadlineUtc > now;
                    authenticatedSession.LogoutAllowedAtUtc = null;
                    authenticatedSession.CleanupStarted = false;
                    currentGameWorldId = authenticatedSession.CurrentGameWorldId;
                    shouldNotifyWorld = true;
                }
            }

            if (shouldNotifyWorld) {
                try {
                    await worldRegistry.RouteGameWorldMessageAsync(
                        currentGameWorldId,
                        new PlayerDisconnectedMessage(authenticatedSession.SessionId, sessionRemainsActive),
                        CancellationToken.None);
                    await worldRegistry.RouteGlobalMessageAsync(
                        new GlobalPlayerDisconnectedMessage(authenticatedSession.SessionId, sessionRemainsActive),
                        CancellationToken.None);
                    var persistedState = await CapturePlayerPersistenceStateAsync(
                        worldRegistry,
                        currentGameWorldId,
                        authenticatedSession.SessionId,
                        CancellationToken.None);
                    if (persistedState is not null) {
                        await CharacterAccess.TrySaveAsync(characterStore, authenticatedSession.NetworkId, persistedState);
                    }
                } catch (Exception exception) when (exception is ObjectDisposedException or KeyNotFoundException) {
                } catch (Exception ex) {
                    Console.Error.WriteLine($"[Server] Failed to persist player '{authenticatedSession.NetworkId}' on disconnect: {ex}");
                }
            }
        }

        disconnectRequested.TrySetResult(null);
        outgoingMessages.Writer.TryComplete();
        try {
            await sendLoopTask;
        } catch (OperationCanceledException) {
        }
        try {
            await disconnectCloseTask;
        } catch (OperationCanceledException) {
        }

        if (webSocket.State == WebSocketState.Open) {
            await SendCloseFrameAsync(WebSocketCloseStatus.NormalClosure, "Closing connection", webSocket, sendLock, CancellationToken.None);
        }

        Console.WriteLine($"[Server] WebSocket disconnected from {context.Connection.RemoteIpAddress}:{context.Connection.RemotePort}");
        ArrayPool<byte>.Shared.Return(messageScratch);
        ArrayPool<byte>.Shared.Return(receiveBuffer);
    }
});

await app.RunAsync($"http://0.0.0.0:{settings.Port}");
disconnectedPlayerCleanupCts.Cancel();
worldTransferCts.Cancel();
try {
    await disconnectedPlayerCleanupTask;
} catch (OperationCanceledException) {
}
try {
    await worldTransferTask;
} catch (OperationCanceledException) {
}
gcMonitor?.Dispose();

/// <summary>
/// Creates a new session or reattaches an existing one within the reconnect grace window.
/// Duplicate live connections and expired reconnect windows are rejected.
/// </summary>
static bool TryAuthenticatePlayer(
    string networkId,
    string characterName,
    WebSocket webSocket,
    string initialGameWorldId,
    ConcurrentDictionary<string, PlayerSession> sessionsByNetworkId,
    ConcurrentDictionary<Guid, PlayerSession> sessionsByServerId,
    out PlayerSession? session,
    out bool isReconnect,
    out string? errorMessage) {
    session = null;
    isReconnect = false;
    errorMessage = null;

    if (string.IsNullOrWhiteSpace(networkId)) {
        errorMessage = "Authentication id is required.";
        return false;
    }

    var trimmedCharacterName = characterName.Trim();
    if (string.IsNullOrEmpty(trimmedCharacterName)) {
        errorMessage = "Character name is required.";
        return false;
    }

    while (true) {
        if (sessionsByNetworkId.TryGetValue(networkId, out var existingSession)) {
            lock (existingSession.SyncRoot) {
                if (existingSession.WebSocket is not null) {
                    errorMessage = "This player is already connected.";
                    return false;
                }
                if (existingSession.CleanupStarted) {
                    errorMessage = "Reconnect window has expired.";
                    return false;
                }
                if (!existingSession.DisconnectDeadlineUtc.HasValue) {
                    errorMessage = "This player is already connected.";
                    return false;
                }
                if (existingSession.DisconnectDeadlineUtc.Value <= DateTimeOffset.UtcNow) {
                    existingSession.CleanupStarted = true;
                    errorMessage = "Reconnect window has expired.";
                    return false;
                }

                existingSession.WebSocket = webSocket;
                existingSession.DisconnectDeadlineUtc = null;
                existingSession.CleanupStarted = false;
                existingSession.CharacterName = trimmedCharacterName;
                session = existingSession;
                isReconnect = true;
                return true;
            }
        }

        var newSession = new PlayerSession(networkId, Guid.NewGuid(), initialGameWorldId, webSocket, trimmedCharacterName);
        if (!sessionsByNetworkId.TryAdd(networkId, newSession)) {
            continue;
        }
        if (!sessionsByServerId.TryAdd(newSession.SessionId, newSession)) {
            sessionsByNetworkId.TryRemove(networkId, out _);
            continue;
        }

        session = newSession;
        return true;
    }
}

static GameWorldTeleportSet[] ResolveTeleportLocs(
    GameWorldConfig gameWorld,
    IReadOnlyDictionary<string, GameWorldConfig> gameWorldsById) {
    var configuredTeleportLocs = gameWorld.TeleportLocs;
    if (configuredTeleportLocs is null || configuredTeleportLocs.Length == 0) {
        return Array.Empty<GameWorldTeleportSet>();
    }

    var resolvedTeleportLocs = new GameWorldTeleportSet[configuredTeleportLocs.Length];
    for (var index = 0; index < configuredTeleportLocs.Length; index++) {
        var teleportLoc = configuredTeleportLocs[index];
        if (!gameWorldsById.TryGetValue(teleportLoc.Target.WorldId, out var targetWorld)) {
            throw new InvalidOperationException(
                $"Game world '{gameWorld.Id}' references unknown teleport target world '{teleportLoc.Target.WorldId}'.");
        }

        resolvedTeleportLocs[index] = new GameWorldTeleportSet(
            teleportLoc.Locs,
            new GameWorldTeleportTarget(
                teleportLoc.Target.WorldId,
                targetWorld.Map,
                teleportLoc.Target.Loc));
    }

    return resolvedTeleportLocs;
}

static string GetCurrentGameWorldId(PlayerSession session) {
    lock (session.SyncRoot) {
        return session.CurrentGameWorldId;
    }
}

static (string WorldId, PlayerPersistenceState State) ResolveLoadedPlayerJoin(
    PlayerPersistenceState loadedState,
    WorldRegistry worldRegistry,
    IReadOnlyDictionary<string, GameWorldConfig> gameWorldsById,
    string defaultWorldId) {
    ArgumentNullException.ThrowIfNull(loadedState);
    const string requestedFallbackWorldId = "aresden";

    if (gameWorldsById.ContainsKey(loadedState.GameWorldId) &&
        worldRegistry.TryGetGameWorld(loadedState.GameWorldId, out var loadedWorld) &&
        loadedWorld is not null) {
        return (loadedState.GameWorldId, loadedState);
    }

    if (gameWorldsById.TryGetValue(requestedFallbackWorldId, out var fallbackGameWorld) &&
        worldRegistry.TryGetGameWorld(requestedFallbackWorldId, out var fallbackWorld) &&
        fallbackWorld is not null) {
        var center = fallbackWorld.GetCenterSpawnHint();
        return (requestedFallbackWorldId, loadedState with {
            GameWorldId = fallbackGameWorld.Id,
            X = center.X,
            Y = center.Y,
        });
    }

    Console.Error.WriteLine(
        $"[Server] Saved world '{loadedState.GameWorldId}' was not found and fallback world '{requestedFallbackWorldId}' is unavailable. Using defaults.");
    if (gameWorldsById.TryGetValue(defaultWorldId, out var defaultGameWorld) &&
        worldRegistry.TryGetGameWorld(defaultWorldId, out var defaultWorld) &&
        defaultWorld is not null) {
        var center = defaultWorld.GetCenterSpawnHint();
        return (defaultWorldId, loadedState with {
            GameWorldId = defaultGameWorld.Id,
            X = center.X,
            Y = center.Y,
        });
    }

    return (defaultWorldId, loadedState);
}

static async Task<PlayerPersistenceState?> CapturePlayerPersistenceStateAsync(
    WorldRegistry worldRegistry,
    string gameWorldId,
    Guid sessionId,
    CancellationToken cancellationToken) {
    var completion = new TaskCompletionSource<PlayerPersistenceState?>(TaskCreationOptions.RunContinuationsAsynchronously);
    await worldRegistry.RouteGameWorldMessageAsync(
        gameWorldId,
        new SavePlayerStateRequestMessage(sessionId, completion),
        cancellationToken);
    return await completion.Task.WaitAsync(cancellationToken);
}

/// <summary>
/// Snapshots every known session while <see cref="WorldRegistry"/> is still processing mailboxes,
/// so orderly server shutdown (e.g. CTRL+C) does not drop in-memory progress when websockets tear down after dispose.
/// </summary>
static async Task PersistAllPlayerStatesOnShutdownAsync(
    WorldRegistry worldRegistry,
    ConcurrentDictionary<Guid, PlayerSession> sessionsByServerId,
    ICharacterStore characterStore) {
    foreach (var session in sessionsByServerId.Values.ToArray()) {
        string worldId;
        Guid sessionId;
        string networkId;
        lock (session.SyncRoot) {
            worldId = session.CurrentGameWorldId;
            sessionId = session.SessionId;
            networkId = session.NetworkId;
        }

        try {
            var persistedState = await CapturePlayerPersistenceStateAsync(
                worldRegistry,
                worldId,
                sessionId,
                CancellationToken.None).ConfigureAwait(false);
            if (persistedState is not null) {
                var saved = await CharacterAccess.TrySaveAsync(characterStore, networkId, persistedState).ConfigureAwait(false);
                Console.WriteLine(CharacterLog.FormatShutdownLine(networkId, persistedState, saved));
            }
        } catch (Exception exception) when (exception is ObjectDisposedException or KeyNotFoundException) {
        } catch (Exception ex) {
            Console.Error.WriteLine($"[Server] Failed to persist player '{networkId}' on shutdown: {ex}");
        }
    }
}

/// <summary>
/// Periodically removes sessions whose disconnect deadline passed and notifies worlds via <see cref="RemoveDisconnectedPlayerMessage"/>.
/// </summary>
static async Task RunDisconnectedPlayerCleanupLoopAsync(
    WorldRegistry worldRegistry,
    ConcurrentDictionary<string, PlayerSession> sessionsByNetworkId,
    ConcurrentDictionary<Guid, PlayerSession> sessionsByServerId,
    CancellationToken cancellationToken) {
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
    while (await timer.WaitForNextTickAsync(cancellationToken)) {
        try {
            var currentTime = DateTimeOffset.UtcNow;
            foreach (var session in sessionsByServerId.Values) {
                var shouldRemovePlayer = false;
                string worldIdForCleanup = string.Empty;
                lock (session.SyncRoot) {
                    if (session.WebSocket is not null ||
                        !session.DisconnectDeadlineUtc.HasValue ||
                        session.DisconnectDeadlineUtc.Value > currentTime ||
                        session.CleanupStarted) {
                        continue;
                    }

                    session.CleanupStarted = true;
                    shouldRemovePlayer = true;
                    worldIdForCleanup = session.CurrentGameWorldId;
                }

                if (!shouldRemovePlayer) {
                    continue;
                }

                try {
                    await worldRegistry.RouteGameWorldMessageAsync(
                        worldIdForCleanup,
                        new RemoveDisconnectedPlayerMessage(session.SessionId),
                        cancellationToken);
                    await worldRegistry.RouteGlobalMessageAsync(
                        new GlobalRemoveDisconnectedPlayerMessage(session.SessionId),
                        cancellationToken);
                } catch (Exception exception) when (exception is ObjectDisposedException or KeyNotFoundException) {
                } catch (Exception ex) {
                    Console.Error.WriteLine($"[Server] Error routing remove-disconnected for session '{session.SessionId}': {ex}");
                }

                sessionsByServerId.TryRemove(session.SessionId, out _);
                sessionsByNetworkId.TryRemove(session.NetworkId, out _);
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            Console.Error.WriteLine($"[Server] Error in disconnected player cleanup loop: {ex}");
        }
    }
}

/// <summary>
/// Serializes cross-world moves: remove player from source world, capture transferable state, join target world on the same connection callbacks.
/// </summary>
static async Task RunWorldTransferLoopAsync(
    WorldRegistry worldRegistry,
    ConcurrentDictionary<Guid, PlayerSession> sessionsByServerId,
    ChannelReader<WorldTransferRequest> worldTransferRequests,
    ICharacterStore characterStore,
    CancellationToken cancellationToken) {
    await foreach (var request in worldTransferRequests.ReadAllAsync(cancellationToken)) {
        PlayerSession? session = null;
        try {
            if (!sessionsByServerId.TryGetValue(request.SessionId, out session)) {
                continue;
            }

            string sourceWorldId;
            Action<ServerMessage>? sendMessage;
            Action<string?>? requestDisconnect;
            Action<WorldTransferDestination>? requestWorldChange;
            lock (session.SyncRoot) {
                if (session.IsWorldTransferPending ||
                    string.Equals(session.CurrentGameWorldId, request.TargetWorldId, StringComparison.Ordinal) ||
                    session.SendMessage is null ||
                    session.RequestDisconnect is null ||
                    session.RequestWorldChange is null ||
                    session.WebSocket is null) {
                    continue;
                }

                sourceWorldId = session.CurrentGameWorldId;
                sendMessage = session.SendMessage;
                requestDisconnect = session.RequestDisconnect;
                requestWorldChange = session.RequestWorldChange;
                session.IsWorldTransferPending = true;
            }

            if (!worldRegistry.TryGetGameWorld(request.TargetWorldId, out _)) {
                sendMessage!(new ServerMessage {
                    SendMessage = new SendMessage {
                        Message = $"World '{request.TargetWorldId}' was not found.",
                    },
                });
                lock (session.SyncRoot) {
                    session.IsWorldTransferPending = false;
                }
                continue;
            }

            var transferCompletion = new TaskCompletionSource<TransferredPlayerState?>(TaskCreationOptions.RunContinuationsAsynchronously);
            try {
                await worldRegistry.RouteGameWorldMessageAsync(
                    sourceWorldId,
                    new TransferPlayerOutMessage(request.SessionId, request.TargetWorldId, transferCompletion),
                    cancellationToken);

                var transferState = await transferCompletion.Task.WaitAsync(cancellationToken);
                if (transferState is null) {
                    continue;
                }

                lock (session.SyncRoot) {
                    session.CurrentGameWorldId = request.TargetWorldId;
                }

                await worldRegistry.RouteGameWorldMessageAsync(
                    request.TargetWorldId,
                    new TransferPlayerInMessage(
                        transferState,
                        request.SpawnX,
                        request.SpawnY,
                        sendMessage!,
                        requestDisconnect!,
                        requestWorldChange!,
                        CreateInterruptLogoutDueToCombat(session)),
                    cancellationToken);
                var persistedState = await CapturePlayerPersistenceStateAsync(
                    worldRegistry,
                    request.TargetWorldId,
                    request.SessionId,
                    cancellationToken);
                if (persistedState is not null) {
                    await CharacterAccess.TrySaveAsync(characterStore, session.NetworkId, persistedState);
                }
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                Console.Error.WriteLine($"[Server] Failed to transfer player '{request.SessionId}' to world '{request.TargetWorldId}': {ex}");
            } finally {
                lock (session.SyncRoot) {
                    session.IsWorldTransferPending = false;
                }
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            Console.Error.WriteLine($"[Server] Unexpected error in world transfer loop for session '{request.SessionId}': {ex}");
            if (session is not null) {
                lock (session.SyncRoot) {
                    session.IsWorldTransferPending = false;
                }
            }
        }
    }
}

/// <summary>Clears <see cref="PlayerSession.LogoutAllowedAtUtc"/> and notifies the client when combat cancels a pending logout.</summary>
/// <remarks>
/// Uses <see cref="PlayerSession.SendMessage"/> at invoke time (not a captured enqueue delegate) so reconnect
/// replaces the socket while <see cref="GameWorldPlayer"/> still uses the same interrupt callback from first join.
/// </remarks>
static Action CreateInterruptLogoutDueToCombat(PlayerSession session) {
    ArgumentNullException.ThrowIfNull(session);
    return () => {
        Action<ServerMessage>? send;
        lock (session.SyncRoot) {
            if (!session.LogoutAllowedAtUtc.HasValue) {
                return;
            }

            session.LogoutAllowedAtUtc = null;
            send = session.SendMessage;
        }

        send?.Invoke(NetworkManager.CreateLogoutCancelled());
    };
}

/// <summary>Accumulates multi-frame WebSocket messages into <paramref name="messageScratch"/> when needed; total size is capped.</summary>
static async Task<(WebSocketMessageType MessageType, ReadOnlyMemory<byte> Payload)> ReceiveMessageAsync(
    WebSocket webSocket,
    byte[] receiveBuffer,
    byte[] messageScratch,
    CancellationToken cancellationToken) {
    if (receiveBuffer.Length < MaxIncomingWebSocketMessageBytes || messageScratch.Length < MaxIncomingWebSocketMessageBytes) {
        throw new InvalidOperationException("Receive buffers must be at least MaxIncomingWebSocketMessageBytes.");
    }

    var assembled = 0;
    while (true) {
        var result = await webSocket.ReceiveAsync(receiveBuffer, cancellationToken);
        if (result.MessageType == WebSocketMessageType.Close) {
            return (result.MessageType, ReadOnlyMemory<byte>.Empty);
        }

        if (result.Count > MaxIncomingWebSocketMessageBytes) {
            throw new IncomingWebSocketMessageTooLargeException();
        }

        if (assembled + result.Count > MaxIncomingWebSocketMessageBytes) {
            throw new IncomingWebSocketMessageTooLargeException();
        }

        if (result.EndOfMessage && assembled == 0) {
            return (result.MessageType, receiveBuffer.AsMemory(0, result.Count));
        }

        receiveBuffer.AsSpan(0, result.Count).CopyTo(messageScratch.AsSpan(assembled, result.Count));
        assembled += result.Count;
        if (result.EndOfMessage) {
            return (result.MessageType, messageScratch.AsMemory(0, assembled));
        }
    }
}

/// <summary>
/// When protobuf serialization disagrees with <see cref="IMessage.CalculateSize"/>, the send loop logs once and aborts the
/// connection instead of retrying (avoids log-spam from a corrupted or inconsistent in-memory message).
/// </summary>
const string ProtobufEncodeInvariantViolationMessagePrefix = "Protobuf encode invariant violated: ";

static bool IsProtobufEncodeInvariantViolation(Exception ex) =>
    ex is InvalidOperationException ioe && ioe.Message.StartsWith(ProtobufEncodeInvariantViolationMessagePrefix, StringComparison.Ordinal);

/// <summary>
/// Drains the per-connection outbound channel, encodes protobuf with pooled buffers, and sends under <paramref name="sendLock"/>.
/// </summary>
static async Task SendOutgoingMessagesAsync(
    WebSocket webSocket,
    ChannelReader<ServerMessage> outgoingMessages,
    SemaphoreSlim sendLock,
    bool enableZeroCopyProtobufTransfer,
    int maxConsecutiveSendFailures,
    CancellationTokenSource? abortConnectionOnSendCircuitBreaker,
    CancellationToken cancellationToken) {
    var consecutiveSendFailures = 0;
    while (await outgoingMessages.WaitToReadAsync(cancellationToken)) {
        while (outgoingMessages.TryRead(out var message)) {
            if (webSocket.State != WebSocketState.Open) {
                return;
            }

            try {
                var payloadSize = message.CalculateSize();
                byte[]? rentedPayload = null;
                try {
                    ReadOnlyMemory<byte> payloadMemory;
                    if (payloadSize == 0) {
                        payloadMemory = ReadOnlyMemory<byte>.Empty;
                    } else {
                        rentedPayload = ArrayPool<byte>.Shared.Rent(payloadSize);
                        if (enableZeroCopyProtobufTransfer) {
                            // Span write produces less garbage than MemoryStream + CodedOutputStream, but in benchmarks
                            // throughput is lower than the stream-based encoder. But this setting can reduce the overall ping latency due to needing less garbage collections.
                            try {
                                message.WriteTo(rentedPayload.AsSpan(0, payloadSize));
                            } catch (InvalidOperationException ex) {
                                throw new InvalidOperationException(
                                    $"{ProtobufEncodeInvariantViolationMessagePrefix}zero-copy write failed (CalculateSize was {payloadSize}).",
                                    ex);
                            }
                            payloadMemory = rentedPayload.AsMemory(0, payloadSize);
                        } else {
                            // This approach actually produces more garbage and increases Gen0 GC collections and occasional 3 digit Gen2 GC pauses,
                            // but in stress testing, this approach has slightly higher throughput (Gen0 GCs are very fast).
                            using var payloadStream = new MemoryStream(rentedPayload, 0, payloadSize, writable: true, publiclyVisible: true);
                            using (var codedOutput = new CodedOutputStream(payloadStream, leaveOpen: true)) {
                                message.WriteTo(codedOutput);
                                codedOutput.Flush();
                            }

                            if (payloadStream.Position != payloadSize) {
                                throw new InvalidOperationException(
                                    $"{ProtobufEncodeInvariantViolationMessagePrefix}encoded length {payloadStream.Position} does not match CalculateSize {payloadSize}.");
                            }

                            payloadMemory = rentedPayload.AsMemory(0, payloadSize);
                        }
                    }

                    await sendLock.WaitAsync(cancellationToken);
                    try {
                        if (webSocket.State != WebSocketState.Open) {
                            return;
                        }

                        await webSocket.SendAsync(
                            payloadMemory,
                            WebSocketMessageType.Binary,
                            true,
                            cancellationToken);
                    } finally {
                        sendLock.Release();
                    }
                } finally {
                    if (rentedPayload is not null) {
                        ArrayPool<byte>.Shared.Return(rentedPayload);
                    }
                }

                consecutiveSendFailures = 0;
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            } catch (Exception ex) when (IsProtobufEncodeInvariantViolation(ex)) {
                Console.Error.WriteLine($"[Server] Fatal protobuf encode error; closing connection: {ex}");
                abortConnectionOnSendCircuitBreaker?.Cancel();
                return;
            } catch (Exception ex) {
                consecutiveSendFailures++;
                Console.Error.WriteLine($"[Server] Error sending outbound message: {ex}");
                if (webSocket.State != WebSocketState.Open) {
                    return;
                }

                if (maxConsecutiveSendFailures > 0 && consecutiveSendFailures >= maxConsecutiveSendFailures) {
                    Console.Error.WriteLine(
                        $"[Server] Outbound send circuit breaker: {consecutiveSendFailures} consecutive failures; aborting connection.");
                    abortConnectionOnSendCircuitBreaker?.Cancel();
                    return;
                }
            }
        }
    }
}

static async Task SendServerDisconnectAsync(
    WebSocket webSocket,
    SemaphoreSlim sendLock,
    Task<string?> disconnectRequestedTask) {
    try {
        var disconnectReason = await disconnectRequestedTask;
        if (string.IsNullOrEmpty(disconnectReason)) {
            return;
        }

        await SendCloseFrameAsync(WebSocketCloseStatus.NormalClosure, disconnectReason, webSocket, sendLock, CancellationToken.None);
    } catch (Exception ex) {
        Console.Error.WriteLine($"[Server] Error sending server-initiated disconnect: {ex}");
    }
}

static async Task SendCloseFrameAsync(
    WebSocketCloseStatus closeStatus,
    string closeDescription,
    WebSocket webSocket,
    SemaphoreSlim sendLock,
    CancellationToken cancellationToken) {
    await sendLock.WaitAsync(cancellationToken);
    try {
        if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived) {
            await webSocket.CloseOutputAsync(closeStatus, closeDescription, cancellationToken);
        }
    } catch (WebSocketException ex) when (ex.WebSocketErrorCode is WebSocketError.ConnectionClosedPrematurely
        or WebSocketError.InvalidState
        or WebSocketError.Faulted) {
    } finally {
        sendLock.Release();
    }
}

/// <summary>Thrown when an assembled inbound WebSocket message exceeds <see cref="MaxIncomingWebSocketMessageBytes"/>.</summary>
sealed class IncomingWebSocketMessageTooLargeException : Exception {
    public IncomingWebSocketMessageTooLargeException() : base("Incoming WebSocket message exceeds maximum allowed size.") { }
}

/// <summary>
/// Authoritative server-side session: maps client network id to stable <see cref="SessionId"/>, holds the live socket and outbound enqueue delegate,
/// and tracks logout/reconnect/world-transfer coordination guarded by <see cref="SyncRoot"/>.
/// </summary>
public sealed class PlayerSession {
    public PlayerSession(string networkId, Guid sessionId, string currentGameWorldId, WebSocket webSocket, string characterName) {
        NetworkId = networkId;
        SessionId = sessionId;
        CurrentGameWorldId = currentGameWorldId;
        WebSocket = webSocket;
        CharacterName = characterName;
    }

    /// <summary>Per-session lock for fields mutated from the WebSocket loop and background tasks.</summary>
    public object SyncRoot { get; } = new();
    /// <summary>Client-supplied stable identity string (e.g. from authenticate payload).</summary>
    public string NetworkId { get; }
    /// <summary>Display name from the client authenticate payload.</summary>
    public string CharacterName { get; set; }
    /// <summary>Server-generated id used in world messages and dictionaries.</summary>
    public Guid SessionId { get; }
    /// <summary>Logical world the player is joined to; updated after successful transfer-in.</summary>
    public string CurrentGameWorldId { get; set; }
    /// <summary>Active socket for this session when connected; null while in reconnect grace.</summary>
    public WebSocket? WebSocket { get; set; }
    /// <summary>Enqueues protobuf <see cref="ServerMessage"/> to the connection send loop.</summary>
    public Action<ServerMessage>? SendMessage { get; set; }
    /// <summary>Requests an orderly close with optional reason shown to the client.</summary>
    public Action<string?>? RequestDisconnect { get; set; }
    /// <summary>Queues an asynchronous world change handled by <c>RunWorldTransferLoopAsync</c>.</summary>
    public Action<WorldTransferDestination>? RequestWorldChange { get; set; }
    /// <summary>When set and in the past, session dictionaries may be purged unless still within grace.</summary>
    public DateTimeOffset? DisconnectDeadlineUtc { get; set; }
    /// <summary>Earliest UTC instant logout is allowed; cleared if logout is cancelled.</summary>
    public DateTimeOffset? LogoutAllowedAtUtc { get; set; }
    /// <summary>Prevents double cleanup when the periodic remover has already started for this session.</summary>
    public bool CleanupStarted { get; set; }
    /// <summary>True while a transfer is in flight to avoid overlapping world moves.</summary>
    public bool IsWorldTransferPending { get; set; }
}

/// <summary>Work item for the world-transfer channel: move <see cref="SessionId"/> to <see cref="TargetWorldId"/> and spawn near the authoritative target cell.</summary>
public sealed record WorldTransferRequest(Guid SessionId, string TargetWorldId, int? SpawnX, int? SpawnY);
