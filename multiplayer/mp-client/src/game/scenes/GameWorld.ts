import { Scene } from 'phaser';
import { GameAsset } from '../objects/GameAsset';
import { GroundItem } from '../objects/GroundItem';
import { DEFAULT_GEAR } from '../../utils/PlayerAppearanceManager';
import { Player } from '../objects/Player';
import { Monster } from '../objects/Monster';
import { ArrowProjectile, isProjectileTarget } from '../effects/ArrowProjectile';
import { NPC } from '../objects/NPC';
import { HBMap } from '../assets/HBMap';
import { FireInstance } from '../spells/FireInstance';
import { PoisonCloudInstance } from '../spells/PoisonCloudInstance';
import { SpikeFieldInstance } from '../spells/SpikeFieldInstance';
import { createSpikeField } from '../spells/SpikeField';
import { IceStorm } from '../spells/IceStorm';
import { EventBus } from '../EventBus';
import { canvasToScreenPosition, convertWorldPosToPixelPos, convertPixelPosToWorldPos, getNextDirection, Direction, findMovableLocation, getDistance, isCellMovable, toDirection, worldCellCenterPixelX, worldCellCenterPixelY } from '../../utils/CoordinateUtils';
import {
    DEPTH_MULTIPLIER,
    GAME_STATS_UPDATE_INTERVAL_MS,
    LOAD_PLAYER_ITEM_APPEARANCE_ASSETS_ON_DEMAND,
    MONSTER_HOVER_OVERLAY_ANCHOR_OFFSET_Y,
    MONSTER_PLACEHOLDER_SPRITE,
    PLAYER_HOVER_OVERLAY_ANCHOR_OFFSET_Y,
} from '../../Config';
import { InputManager } from '../../utils/InputManager';
import { CameraManager } from '../../utils/CameraManager';
import {
    getMusicManager,
    getGameStateManager,
    getNetworkManager,
    getAndRemoveInitialGameWorldState,
    setDebugModeEnabled,
    setDisplayLargeItemsEnabled,
    setInitialGameWorldState,
    setPlayerPosition,
    setSoundManager,
    takePendingPlayerItemAppearancePrefetch,
    getMapIfPresent,
} from '../../utils/RegistryUtils';
import type { InitialGameWorldState } from '../../utils/RegistryUtils';
import { cancelPlayerDialogPhaserNotificationDebouncers, playerDialogStore } from '../../ui/store/PlayerDialog.store';
import { MapManager } from '../../utils/MapManager';
import { prepareMapForGameWorld, shouldLoadMapAssetsOnDemand } from '../../utils/MapAssets';
import { loadPlayerItemAppearanceOnDemand } from '../../utils/ItemAssets';
import { SoundManager } from '../../utils/SoundManager';
import { getMonsterData } from '../../constants/Monsters';
import { getSpriteForCatalogNpcId } from '../../constants/NPCs';
import {
    CURRENT_SCENE_READY,
    INITIAL_GAME_WORLD_STATE_RECEIVED,
    PLAYER_POSITION_CHANGED,
    PLAYER_ITEM_APPEARANCE_PREFETCH_REQUESTED,
    TILE_OCCUPANCY_REAPPLY_REQUESTED,
    MONSTER_ENTERED_RANGE_RECEIVED,
    MONSTER_ATTACKED_MONSTER_RECEIVED,
    MONSTER_ATTACKED_RECEIVED,
    MONSTER_DIED_RECEIVED,
    GROUND_STATES_ENTERED_RANGE_RECEIVED,
    GROUND_STATES_LEFT_RANGE_RECEIVED,
    MONSTER_MOVED_RECEIVED,
    MONSTERS_LEFT_RANGE_RECEIVED,
    PLAYER_JOINED_RECEIVED,
    PLAYER_LEFT_RECEIVED,
    PLAYER_MOVED_RECEIVED,
    PLAYER_ATTACKED_MONSTER_RECEIVED,
    PLAYER_ATTACKED_PLAYER_RECEIVED,
    PLAYER_PICKUP_PERFORMED_RECEIVED,
    PLAYER_BOW_STANCE_PERFORMED_RECEIVED,
    PLAYER_DISCONNECTED_RECEIVED,
    PLAYER_MOVEMENT_STATE_CHANGED_RECEIVED,
    PLAYER_ATTACK_MODE_CHANGED_RECEIVED,
    PLAYER_IDLE_DIRECTION_CHANGED_RECEIVED,
    PLAYER_APPEARANCE_CHANGED_RECEIVED,
    PLAYER_PARALYZED_RECEIVED,
    PLAYER_RECEIVE_DAMAGE_RECEIVED,
    PLAYER_TAKE_DAMAGE_RECEIVED,
    HP_UPDATED_RECEIVED,
    MONSTER_TAKE_DAMAGE_RECEIVED,
    MONSTER_TAKE_DAMAGE_BY_MONSTER_RECEIVED,
    PLAYER_RECONNECTED_RECEIVED,
    PLAYER_SPAWN_PROTECTION_ENABLED_RECEIVED,
    PLAYER_SPAWN_PROTECTION_DISABLED_RECEIVED,
    TEMPORARY_EFFECT_APPLIED_FOR_PLAYER_RECEIVED,
    TEMPORARY_EFFECT_EXPIRED_FOR_PLAYER_RECEIVED,
    TEMPORARY_EFFECT_APPLIED_FOR_MONSTER_RECEIVED,
    TEMPORARY_EFFECT_EXPIRED_FOR_MONSTER_RECEIVED,
    CAST_EFFECT_RECEIVED,
    REMOTE_PLAYER_ITEM_EQUIPPED_RECEIVED,
    REMOTE_PLAYER_ITEM_UNEQUIPPED_RECEIVED,
    SPELL_CAST_STARTED_RECEIVED,
    SPELL_CAST_CANCELLED_RECEIVED,
    SPELL_CAST_FAILED_RECEIVED,
    CAST_AOE_SPELL_RECEIVED,
    CAST_DIRECTIONAL_AOE_SPELL_RECEIVED,
    MONSTER_CAST_AOE_SPELL_RECEIVED,
    MONSTER_CAST_DIRECTIONAL_AOE_SPELL_RECEIVED,
    POSITION_CORRECTED_RECEIVED,
    RESET_POSITION_RECEIVED,
    PLAYER_TELEPORTED_RECEIVED,
    MONSTER_DEAD,
    IN_UI_REQUEST_PLAYER_LOGOUT,
    SOCKET_DISCONNECTED,
    IN_UI_PLAYER_RESURRECT,
    IN_UI_REQUEST_SERVER_RESURRECT,
    PLAYER_DIED_RECEIVED,
    PLAYER_RESURRECTED_RECEIVED,
    IN_UI_CAST_SPELL,
    IN_UI_CHANGE_MOVEMENT_SPEED,
    IN_UI_CHANGE_ATTACK_SPEED,
    IN_UI_CHANGE_ATTACK_RANGE,
    IN_UI_CHANGE_STUN_DURATION,
    IN_UI_CHANGE_DAMAGE,
    IN_UI_CHANGE_ATTACK_TYPE,
    IN_UI_CHANGE_ALLOW_DASH_ATTACK,
    IN_UI_CHANGE_CAST_SPEED,
    IN_UI_CHANGE_ATTACK_MODE,
    IN_UI_CHANGE_RUN_MODE,
    IN_UI_CHANGE_GENDER,
    IN_UI_CHANGE_SKIN_COLOR,
    IN_UI_CHANGE_UNDERWEAR_COLOR,
    IN_UI_CHANGE_HAIR_STYLE,
    IN_UI_CHANGE_MUSIC_VOLUME,
    IN_UI_CHANGE_SOUND_VOLUME,
    IN_UI_CHANGE_MAP,
    IN_UI_TOGGLE_RENDER_MAP_TILES,
    IN_UI_TOGGLE_RENDER_MAP_OBJECTS,
    IN_UI_TOGGLE_DEBUG_MODE,
    IN_UI_TOGGLE_NON_MOVABLE_CELLS_HIGHLIGHT,
    IN_UI_TOGGLE_TELEPORT_CELLS_HIGHLIGHT,
    IN_UI_TOGGLE_SERVER_TELEPORT_CELLS_HIGHLIGHT,
    IN_UI_TOGGLE_WATER_CELLS_HIGHLIGHT,
    IN_UI_TOGGLE_FARMABLE_CELLS_HIGHLIGHT,
    IN_UI_TOGGLE_GRID_DISPLAY,
    IN_UI_TOGGLE_DISPLAY_LARGE_ITEMS,
    IN_UI_CHANGE_WEATHER,
    IN_UI_PLAY_MUSIC,
    IN_UI_CHANGE_PLAY_MAP_MUSIC,
    IN_UI_SUMMON_MONSTER,
    IN_UI_SUMMON_NPC,
    IN_UI_KILL_ALL_NPCS,
    NPC_ENTERED_RANGE_RECEIVED,
    NPCS_LEFT_RANGE_RECEIVED,
    IN_UI_MAKE_SERVER_CELL_OCCUPIED_MODE,
    IN_UI_PLAYER_TELEPORT_REQUEST_MODE,
    IN_UI_CHANGE_GRACE_PERIOD,
    PLAYER_CAST_ANIMATION_STARTED,
    PLAYER_CONFIRM_SPELL_TARGET,
    NPC_DEAD,
    OUT_UI_GAME_STATS_UPDATE,
    OUT_UI_HOVER_ATTACKABLE_TARGET,
    OUT_UI_HOVER_GROUND_ITEM,
    OUT_UI_HOVER_GROUND_ITEM_INFO,
    OUT_UI_HOVER_MONSTER,
    OUT_UI_HOVER_NPC,
    OUT_UI_HOVER_PLAYER,
    OUT_UI_SET_SELECTED_MUSIC,
    OUT_UI_SET_SELECTED_MAP,
    OUT_MAP_LOADED,
    OUT_UI_LOGOUT_COUNTDOWN_CHANGED,
    NATIVE_OVERLAY_HEALTH_BAR_HIDDEN,
    NATIVE_OVERLAY_LOGOUT_COUNTDOWN_HIDDEN,
    NATIVE_OVERLAY_LOGOUT_COUNTDOWN_UPDATED,
    NATIVE_OVERLAY_MAP_LOADING_HIDDEN,
    NATIVE_OVERLAY_MAP_LOADING_SHOWN,
    OUT_WEATHER_SYNCED,
    type PlayerItemAppearancePrefetchEventData,
} from '../../constants/EventNames';
import {
    AttackType,
    CastAoeSpellEventData,
    CastDirectionalAoeSpellEventData,
    CastSpellEvent,
    GameWorldInitData,
    Gender,
    GroundStateCellEventData,
    GroundStateCellRemovedEventData,
    InitialGameWorldStateEventData,
    ItemEquippedEventData,
    ItemUnequippedEventData,
    MonsterAllegiance,
    MonsterAttackedEventData,
    MonsterAttackedMonsterEventData,
    MonsterCastAoeSpellEventData,
    MonsterCastDirectionalAoeSpellEventData,
    MonsterDiedEventData,
    MonsterEnteredRangeEventData,
    MonsterHoverInfo,
    MonsterMovedEventData,
    MonsterTakeDamageByMonsterEventData,
    MonsterTakeDamageEventData,
    NetworkPlayer,
    NpcEnteredRangeEventData,
    NpcHoverInfo,
    PlayerAppearanceChangedEventData,
    PlayerAttackModeChangedEventData,
    PlayerAttackedMonsterEventData,
    PlayerAttackedPlayerEventData,
    PlayerBowStancePerformedEventData,
    PlayerConnectionStateChangedEventData,
    PlayerConfirmSpellTargetEvent,
    PlayerDiedEventData,
    PlayerHoverInfo,
    PlayerIdleDirectionChangedEventData,
    PlayerLeftEventData,
    PlayerMovedEventData,
    PlayerMovementStateChangedEventData,
    PlayerPickupPerformedEventData,
    PlayerReceiveDamageEventData,
    PlayerResurrectedEventData,
    PlayerTakeDamageEventData,
    SkinColor,
    SpellCastCancelledEventData,
    SpellCastStartedEventData,
    SummonMonsterEvent,
    SummonNPCEvent,
    TeleportLocSet,
    TeleportTarget,
    TemporaryEffectMonsterEventData,
    TemporaryEffectPlayerEventData,
    TemporaryEffectType,
} from '../../Types';
import { ItemTypes, type Effect } from '../../constants/Items';
import { CastManager } from '../../utils/CastManager';
import { WeatherManager } from '../../utils/WeatherManager';
import { setDeathDialogOpen } from '../../ui/store/DeathDialog.store';
import { mapDialogStore, syncWeather, type WeatherMode } from '../../ui/store/MapDialog.store';
import { serverDialogStore } from '../../ui/store/ServerDialog.store';
import { performLogoutCleanup } from '../../utils/LogoutUtils';
import {
    getGroundItemUnderPointer,
    getMonsterUnderWorldPixel,
    getMonsterUnderWorldPixelForHoverUi,
    getNpcUnderWorldPixelForHover,
    getOtherPlayerUnderWorldPixel,
    getPlayerUnderWorldPixelForHover,
} from '../../utils/PointerUtils';
import { getTeleportSourceCellsFromLocSets } from '../../utils/NetworkManager';
import { runSafeSync, subscribeSafe } from '../../utils/SafeEntry';
import { drawEffect, type DrawEffectOptions } from '../../utils/EffectUtils';
import { GroundEffectType, MonsterEntityState } from '../../proto/generated/network';
import {
    areMonsterAssetsLoaded,
    loadMonsterAssetsOnDemand,
    shouldLoadMonsterAssetsOnDemand,
} from '../../utils/MonsterAssets';

/**
 * Main game scene. Manages player, monsters, NPCs, ground items, map, camera, input,
 * spell casting, weather, and UI event handling. Loads map on init, spawns objects, and runs the game loop.
 */
export class GameWorld extends Scene {
    private updateInterval: number | undefined = undefined;
    /** Last known cursor position (document coords) - used for elementFromPoint when cursor is over DOM overlays */
    private lastCursorPosition: { x: number; y: number } | undefined = undefined;
    private cursorPositionCleanup: (() => void) | undefined = undefined;
    /** Player instance - cleaned up in shutdown() */
    private player: Player | undefined = undefined;
    /** Current player's network id, when provided by the server */
    private selfPlayerId: string | undefined = undefined;
    /** All spawned players keyed by network id */
    private playersById = new Map<string, Player>();
    /** List of monster instances - cleaned up in shutdown() */
    private monsters: Monster[] = [];
    /** List of NPC instances - cleaned up in shutdown() */
    private npcs: NPC[] = [];
    /** Camera manager - handles follow, zoom, bounds, and UI-driven movement */
    private cameraManager: CameraManager | undefined = undefined;
    /** Reused for camera follow to avoid per-frame `{ x, y }` allocations */
    private readonly cameraFollowScratch = new Phaser.Math.Vector2();
    /** Mouse/pointer input manager */
    private inputManager: InputManager | undefined = undefined;
    /** Set of map objects that are currently colliding with the player */
    private collidingMapObjects: Set<GameAsset> = new Set();
    private logoutCountdownSeconds: number | undefined = undefined;

    private readonly logoutCountdownChangedHandler = (payload: { secondsLeft?: number }): void => {
        runSafeSync('GameWorld:logoutCountdownChanged', () => {
            const secondsLeft = payload.secondsLeft;
            if (secondsLeft === undefined || secondsLeft <= 0) {
                this.hideLogoutCountdownOverlay();
            } else {
                this.showOrUpdateLogoutCountdownOverlay(secondsLeft);
            }
        });
    };

    /** Whether scene initialization has started (deferred to first update) */
    private initializationStarted = false;
    /** Map manager - handles map loading, rendering, and minimap capture */
    private mapManager: MapManager | undefined = undefined;
    /** Sound manager instance */
    private soundManager: SoundManager;
    /** Whether to play map music when map loads */
    private playMapMusic = true;
    /** Whether the map is currently loading */
    private loadingMap = true;
    /** Cast manager - handles effects, spells, and cleanup */
    private castManager: CastManager | undefined = undefined;
    /** Map that is currently displayed (for proper cleanup on shutdown - gameStateManager may already point to new map) */
    private displayedMap: HBMap | undefined = undefined;
    /** Ground items (dropped loot) - cleaned up in shutdown() */
    private groundItems: GroundItem[] = [];
    /** Active ground-effect visuals keyed by authoritative server id. */
    private groundEffectsById = new Map<string, FireInstance | PoisonCloudInstance | SpikeFieldInstance | IceStorm>();
    /** Weather manager - rain particles and sound */
    private weatherManager: WeatherManager | undefined = undefined;
    /** When true, next left click sends cell coords to server as "make occupied" */
    private awaitingMakeServerCellOccupiedClick = false;
    /** When true, next left click sends cell coords to server as a player teleport request */
    private awaitingPlayerTeleportClick = false;
    /** Pending course corrections to process at start of next frame (before player update) */
    private pendingCourseCorrections: { curX: number; curY: number; destX: number; destY: number }[] = [];
    /** Initial state from server (map name, player position) */
    private initialGameWorldState: InitialGameWorldState | undefined;
    /** Game world ID from server (e.g. aresden, bisle) - used in logs */
    private gameWorldId: string | undefined;
    /** Loaded map waiting for authoritative server state before scene setup completes */
    private pendingLoadedMap: HBMap | undefined = undefined;
    /** True while switching worlds and waiting for the next InitialGameWorldState packet */
    private awaitingTransferredWorldState = false;
    /** Prevents duplicate predictive teleport restarts while one transfer is already in flight. */
    private pendingPredictedWorldTransfer = false;
    /** Active listener for map-dialog requested world changes; waits for authoritative server state before restarting. */
    private pendingRequestedWorldChangeListener: ((data: InitialGameWorldStateEventData) => void) | undefined = undefined;
    /** While true, ignore left-click movement until the current confirm click has been released. */
    private suppressLeftMouseMovementUntilRelease = false;
    /** Spawn protection enabled for self before player was created (apply when player is created) */
    private pendingSpawnProtectionForSelf = false;
    /** Teleport lookup for the currently loaded world, keyed as "x,y". */
    private teleportTargetsBySourceCell = new Map<string, TeleportTarget>();

    /** Latest `teleportLocs` from server (InitialGameWorldState); drives server teleport cell overlay. */
    private lastTeleportLocSets: TeleportLocSet[] = [];
    /** Arrow speed from InitialGameWorldState (px/s); used for monster bow FX toward the player. */
    private arrowSpeedPxPerSec = 1000;


    /** One arrow instance so `EventBus.off` gets the same reference as `on` (new inline `() => …` each time would not). */
    private readonly syncPlayerAppearanceHandler = () => {
        try {
            this.syncPlayerAppearance();
        } catch (error) {
            console.error('[GameWorld:syncPlayerAppearance]', error);
        }
    };

    private readonly playerItemAppearancePrefetchHandler = (payload: PlayerItemAppearancePrefetchEventData) => {
        if (!LOAD_PLAYER_ITEM_APPEARANCE_ASSETS_ON_DEMAND) {
            return;
        }
        for (const name of payload.spriteNames) {
            void loadPlayerItemAppearanceOnDemand(this, name);
        }
    };

    constructor() {
        super('GameWorld');
        this.soundManager = new SoundManager(this);
    }

    public init(data?: GameWorldInitData) {
        runSafeSync('GameWorld:init', () => {
            this.clearPendingRequestedWorldChangeListener();
            // Prefer registry over Phaser init data: `scene.restart({ initialGameWorldState })` can leave stale
            // `data` on the Scene when returning via `scene.start('GameWorld')` without args (e.g. after logout),
            // which would ignore fresh `setInitialGameWorldState` from LoginScreen.
            this.initialGameWorldState =
                getAndRemoveInitialGameWorldState(this.game) ?? data?.initialGameWorldState;
            this.gameWorldId = this.initialGameWorldState?.gameWorldId;
            this.setTeleportLocs(this.initialGameWorldState?.teleportLocs);
            this.awaitingTransferredWorldState = this.initialGameWorldState?.awaitTransferredWorldState === true;
            this.pendingPredictedWorldTransfer = this.awaitingTransferredWorldState;
            this.pendingLoadedMap = undefined;
            if (this.awaitingTransferredWorldState) {
                this.tryConsumePendingTransferredWorldState();
            }

            setSoundManager(this.game, this.soundManager);
            // Reset initialization state
            this.initializationStarted = false;
            this.loadingMap = true;

            this.weatherManager = new WeatherManager(this, this.soundManager);
            const snapshotWeather = this.initialGameWorldState?.weather;
            const weather = snapshotWeather ?? mapDialogStore.state.weather;
            this.weatherManager.setWeather(weather);
            if (snapshotWeather !== undefined) {
                syncWeather(snapshotWeather);
            }
            this.setupMapManager();
            this.setupCameraManager();
            this.setupControlDialogEventListeners();
            this.setupSoundDialogEventListeners();
            this.setupMapDialogEventListeners();
            this.setupServerDialogEventListeners();
            this.setupSummonDialogEventListeners();
            this.setupNPCEventListeners();
            this.setupCastManager();
            this.setupSpellRequestListener();
            this.setupPlayerEventListeners();
            this.setupMonsterEventListeners();
            this.setupInputManager();
            this.setupCameraStatsUpdateInterval();
            EventBus.on(OUT_UI_LOGOUT_COUNTDOWN_CHANGED, this.logoutCountdownChangedHandler);
            EventBus.on(PLAYER_ITEM_APPEARANCE_PREFETCH_REQUESTED, this.playerItemAppearancePrefetchHandler);

            this.events.once('shutdown', () => {
                runSafeSync('GameWorld:shutdownEvent', () => this.shutdown());
            });
        });
    }

    public create() {
        runSafeSync('GameWorld:create', () => {
            this.cameras.main.setBackgroundColor('#000');
            if (LOAD_PLAYER_ITEM_APPEARANCE_ASSETS_ON_DEMAND) {
                const pending = takePendingPlayerItemAppearancePrefetch(this.game);
                for (const name of pending) {
                    void loadPlayerItemAppearanceOnDemand(this, name);
                }
            }
            EventBus.emit(CURRENT_SCENE_READY, this);
        });
    }

    private setupMapManager(): void {
        this.mapManager = new MapManager({
            scene: this,
            initialMapName: this.initialGameWorldState?.mapName,
            initialMusicFile: this.initialGameWorldState?.musicFile,
            playMapMusic: this.playMapMusic,
        });
    }

    private setupCameraManager(): void {
        this.cameraManager = new CameraManager({
            scene: this,
            isCapturingMinimap: () => this.mapManager?.isCapturingMinimap() ?? false,
            getFollowTarget: () => {
                if (!this.player) {
                    return undefined;
                }
                this.cameraFollowScratch.set(
                    this.player.getAnimatedPixelX(),
                    this.player.getAnimatedPixelY(),
                );
                return this.cameraFollowScratch;
            },
        });
        this.cameraManager.setupEventListeners();
        this.mapManager?.setCameraManager(this.cameraManager);
    }

    private setupControlDialogEventListeners(): void {
        // Listen for player movement speed changes from React (ms-based, 100-500 base)
        subscribeSafe('GameWorld', IN_UI_CHANGE_MOVEMENT_SPEED, (payload: { speed: number; previousSpeed: number }) => {
            const baseMs = payload.speed;
            const oldBaseMs = payload.previousSpeed;
            if (this.player) {
                const runMode = playerDialogStore.state.runMode;
                const cur = this.player.getMovementSpeedMs();
                const oldUnbuffed = runMode ? oldBaseMs : oldBaseMs * 2;
                const newUnbuffed = runMode ? baseMs : baseMs * 2;
                const effectiveMs =
                    oldUnbuffed > 0 ? Math.round((cur / oldUnbuffed) * newUnbuffed) : newUnbuffed;
                this.player.setMovementSpeed(effectiveMs);
            }
            if (serverDialogStore.state.syncWithServer) {
                getNetworkManager(this.game)?.changePlayerMovementSpeed(baseMs);
            }
        });

        // Listen for player attack speed changes from React
        subscribeSafe('GameWorld', IN_UI_CHANGE_ATTACK_SPEED, (attackSpeedMs: number) => {
            if (this.player) {
                this.player.setAttackSpeedFromDurationMs(attackSpeedMs);
            }
            if (serverDialogStore.state.syncWithServer) {
                getNetworkManager(this.game)?.changePlayerAttackSpeed(playerDialogStore.state.attackSpeedMs);
            }
        });

        // Listen for player cast speed changes from React (full cast bar duration in ms)
        subscribeSafe('GameWorld', IN_UI_CHANGE_CAST_SPEED, (castSpeedMs: number) => {
            if (this.player) {
                this.player.setCastDurationMs(castSpeedMs);
            }
            if (serverDialogStore.state.syncWithServer) {
                getNetworkManager(this.game)?.changePlayerCastSpeed(playerDialogStore.state.castSpeedMs);
            }
        });

        // Listen for player attack range changes from React
        subscribeSafe('GameWorld', IN_UI_CHANGE_ATTACK_RANGE, (range: number) => {
            if (this.player) {
                this.player.setAttackRange(range);
            }
            if (serverDialogStore.state.syncWithServer) {
                getNetworkManager(this.game)?.changePlayerAttackRange(playerDialogStore.state.attackRange);
            }
        });

        subscribeSafe('GameWorld', IN_UI_CHANGE_DAMAGE, () => {
            if (serverDialogStore.state.syncWithServer) {
                getNetworkManager(this.game)?.changePlayerAttackDamage(playerDialogStore.state.damage);
            }
        });

        subscribeSafe('GameWorld', IN_UI_CHANGE_STUN_DURATION, (ms: number) => {
            if (serverDialogStore.state.syncWithServer) {
                getNetworkManager(this.game)?.changePlayerAttackStunDuration(ms);
            }
        });

        subscribeSafe('GameWorld', IN_UI_CHANGE_ATTACK_TYPE, (attackType: AttackType) => {
            if (this.player) {
                this.player.setAttackType(attackType);
            }
            if (serverDialogStore.state.syncWithServer) {
                getNetworkManager(this.game)?.changePlayerAttackType(attackType);
            }
        });

        subscribeSafe('GameWorld', IN_UI_CHANGE_ALLOW_DASH_ATTACK, (enabled: boolean) => {
            if (serverDialogStore.state.syncWithServer) {
                getNetworkManager(this.game)?.changePlayerAllowDashAttack(enabled);
            }
        });

        subscribeSafe('GameWorld', IN_UI_CHANGE_ATTACK_MODE, (enabled: boolean) => {
            if (this.player) {
                this.player.setAttackMode(enabled);
            }
            if (serverDialogStore.state.syncWithServer) {
                getNetworkManager(this.game)?.requestPlayerAttackModeChange(enabled);
            }
        });

        subscribeSafe('GameWorld', IN_UI_CHANGE_RUN_MODE, (enabled: boolean) => {
            if (this.player) {
                const cur = this.player.getMovementSpeedMs();
                const nextMs = enabled ? Math.max(100, Math.round(cur / 2)) : Math.min(1000, cur * 2);
                this.player.setRunModeAndMovementSpeed(enabled, nextMs);
            }
            if (serverDialogStore.state.syncWithServer) {
                getNetworkManager(this.game)?.requestPlayerMovementStateChange(enabled);
            }
        });

        EventBus.on(IN_UI_CHANGE_GENDER, this.syncPlayerAppearanceHandler);
        EventBus.on(IN_UI_CHANGE_SKIN_COLOR, this.syncPlayerAppearanceHandler);
        EventBus.on(IN_UI_CHANGE_UNDERWEAR_COLOR, this.syncPlayerAppearanceHandler);
        EventBus.on(IN_UI_CHANGE_HAIR_STYLE, this.syncPlayerAppearanceHandler);

        // Listen for map change events from React
        subscribeSafe('GameWorld', IN_UI_CHANGE_MAP, (worldId: string) => {
            if (!worldId || worldId === this.gameWorldId) {
                return;
            }

            const networkManager = getNetworkManager(this.game);
            const targetWorld = networkManager?.getWorldById(worldId);
            if (!networkManager || !targetWorld) {
                console.warn(`[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Cannot change world: unknown world '${worldId}'`);
                return;
            }

            this.beginRequestedWorldChange(worldId);
        });

        subscribeSafe('GameWorld', IN_UI_REQUEST_SERVER_RESURRECT, () => {
            getNetworkManager(this.game)?.requestPlayerResurrectedRequest();
        });
        subscribeSafe('GameWorld', PLAYER_DIED_RECEIVED, (data: PlayerDiedEventData) => {
            const p = this.playersById.get(data.playerId);
            p?.applyDeath();
        });
        subscribeSafe('GameWorld', PLAYER_RESURRECTED_RECEIVED, (data: PlayerResurrectedEventData) => {
            const p = this.playersById.get(data.playerId);
            p?.applyResurrect(data.x, data.y, data.hp, data.maxHp);
            if (data.playerId === this.selfPlayerId) {
                EventBus.emit(IN_UI_PLAYER_RESURRECT);
            }
        });

        // Listen for player logout events from React
        subscribeSafe('GameWorld', IN_UI_REQUEST_PLAYER_LOGOUT, () => {
            // Stop music before saving state
            getMusicManager(this).stopMusic();

            // Save game state before logout (map and position are server-authoritative)
            getGameStateManager(this.game).saveGameState();

            // Navigate back to LoginScreen
            this.scene.start('LoginScreen');
        });

        // Listen for socket disconnection (server shutdown, network loss, etc.) - same behavior as Log out button
        subscribeSafe('GameWorld', SOCKET_DISCONNECTED, () => {
            performLogoutCleanup(this.game);
        });
    }

    private setupSoundDialogEventListeners(): void {
        // Listen for music play requests from React
        subscribeSafe('GameWorld', IN_UI_PLAY_MUSIC, (musicFile: string) => {
            getMusicManager(this).playMusic(musicFile);
            // Emit event to notify React layer of music change
            EventBus.emit(OUT_UI_SET_SELECTED_MUSIC, musicFile);
        });

        // Listen for play map music setting changes from React
        subscribeSafe('GameWorld', IN_UI_CHANGE_PLAY_MAP_MUSIC, (enabled: boolean) => {
            this.playMapMusic = enabled;
        });

        // Listen for music volume changes from React
        subscribeSafe('GameWorld', IN_UI_CHANGE_MUSIC_VOLUME, (volume: number) => {
            this.restorePhaserSoundMasterIfMuted();
            getMusicManager(this).setMusicVolume(volume);
            // Update GameStateManager
            getGameStateManager(this.game).setMusicVolume(volume);
        });

        // Listen for sound volume changes from React
        subscribeSafe('GameWorld', IN_UI_CHANGE_SOUND_VOLUME, (volume: number) => {
            this.restorePhaserSoundMasterIfMuted();
            if (this.soundManager) {
                this.soundManager.setSoundVolume(volume);
            }
            // Update GameStateManager
            getGameStateManager(this.game).setSoundVolume(volume);
        });
    }

    /**
     * Phaser applies game.sound.volume as a master gain on top of per-sound volumes.
     * PhaserGame sets it to 0 on window blur; if that stacks badly, sliders only update
     * MusicManager/SoundManager and cannot make audio audible until master is non-zero.
     */
    private restorePhaserSoundMasterIfMuted(): void {
        if (this.game.sound.volume === 0) {
            this.game.sound.volume = 1;
        }
    }

    private setupSummonDialogEventListeners(): void {
        // Summon is server-authoritative; UI forwards sprite, movement, and facing for the spawn packet.
        subscribeSafe('GameWorld', IN_UI_SUMMON_MONSTER, (data: SummonMonsterEvent) => {
            const nm = getNetworkManager(this.game);
            if (!nm) {
                return;
            }
            nm.sendSummonMonsterRequested(
                data.spriteName,
                data.movementSpeed,
                data.direction,
                data.attackType,
                data.allegiance,
                data.stunDurationMs,
                data.maxHp,
                data.attackDamage,
                data.attackSpeedMs,
                data.attackRecoveryMs,
                data.chaseRangeCells,
                data.attackRangeCells,
                data.summonCount,
            );
        });

        subscribeSafe('GameWorld', IN_UI_SUMMON_NPC, (data: SummonNPCEvent) => {
            const nm = getNetworkManager(this.game);
            if (!nm) {
                return;
            }
            let dir = data.direction;
            if (dir === Direction.None) {
                dir = Direction.South;
            }
            nm.sendSummonNpcRequest(data.catalogNpcId, dir);
        });
    }

    private setupNPCEventListeners(): void {
        subscribeSafe('GameWorld', NPC_DEAD, (data: { npcId: string }) => {
            const npcIndex = this.npcs.findIndex((n) => n.getNPCId() === data.npcId);
            if (npcIndex !== -1) {
                const npc = this.npcs[npcIndex];
                npc.destroy();
                this.npcs.splice(npcIndex, 1);
            }
        });

        subscribeSafe('GameWorld', IN_UI_KILL_ALL_NPCS, () => {
            getNetworkManager(this.game)?.sendKillAllNpcsRequest();
        });

        subscribeSafe('GameWorld', NPC_ENTERED_RANGE_RECEIVED, (entries: NpcEnteredRangeEventData[]) => {
            for (const entry of entries) {
                this.handleNpcEnteredRange(entry);
            }
        });
        subscribeSafe('GameWorld', NPCS_LEFT_RANGE_RECEIVED, (npcIds: string[]) => {
            this.handleNpcsLeftRange(npcIds);
        });
    }

    private setupCastManager(): void {
        this.castManager = new CastManager({
            scene: this,
            soundManager: this.soundManager,
            cameraManager: this.cameraManager,
            getPlayerWorldPos: () =>
                this.player ? { x: this.player.getWorldX(), y: this.player.getWorldY() } : undefined,
        });
        this.castManager.setupEventListeners();
    }

    private setupSpellRequestListener(): void {
        subscribeSafe('GameWorld', IN_UI_CAST_SPELL, (data: CastSpellEvent) => {
            this.player?.requestCast(data.spellId);
        });
        subscribeSafe('GameWorld', PLAYER_CAST_ANIMATION_STARTED, (data: { spellId: number }) => {
            getNetworkManager(this.game)?.sendSpellCastStartRequest(data.spellId);
        });
        subscribeSafe('GameWorld', PLAYER_CONFIRM_SPELL_TARGET, (data: PlayerConfirmSpellTargetEvent) => {
            const nm = getNetworkManager(this.game);
            const spellEntry = nm?.getSpellById(data.spellId);
            let aimAssistPlayerId: bigint | undefined;
            let aimAssistMonsterId: bigint | undefined;
            if (spellEntry?.aimAssist) {
                const ids = this.getSpellAimAssistTargetIds(data.spellId, data.targetPixelX, data.targetPixelY);
                aimAssistPlayerId = ids.playerId;
                aimAssistMonsterId = ids.monsterId;
            }
            nm?.sendSpellCastRequest(
                convertPixelPosToWorldPos(data.targetPixelX),
                convertPixelPosToWorldPos(data.targetPixelY),
                aimAssistPlayerId,
                aimAssistMonsterId,
            );
        });
    }

    private setupPlayerEventListeners(): void {
        // Listen for player position changes to update monster spatial audio
        subscribeSafe('GameWorld', PLAYER_POSITION_CHANGED, (data: { x: number; y: number }) => {
            this.updateMonsterSpatialAudio(data.x, data.y);
        });
        subscribeSafe('GameWorld', TILE_OCCUPANCY_REAPPLY_REQUESTED, () => {
            this.reapplyTileOccupancyOnMap();
        });
        subscribeSafe('GameWorld', MONSTER_ENTERED_RANGE_RECEIVED, (data: MonsterEnteredRangeEventData[]) => {
            for (const entry of data) {
                this.handleMonsterEnteredRange(entry);
            }
        });
        subscribeSafe('GameWorld', MONSTERS_LEFT_RANGE_RECEIVED, (monsterIds: string[]) => {
            this.handleMonstersLeftRange(monsterIds);
        });
        subscribeSafe('GameWorld', GROUND_STATES_ENTERED_RANGE_RECEIVED, (states: GroundStateCellEventData[]) => {
            this.handleGroundStatesEnteredRange(states);
        });
        subscribeSafe('GameWorld', GROUND_STATES_LEFT_RANGE_RECEIVED, (states: GroundStateCellRemovedEventData[]) => {
            this.handleGroundStatesLeftRange(states);
        });
        subscribeSafe('GameWorld', MONSTER_MOVED_RECEIVED, (data: MonsterMovedEventData) => {
            this.handleMonsterMoved(data);
        });
        subscribeSafe('GameWorld', MONSTER_ATTACKED_RECEIVED, (data: MonsterAttackedEventData) => {
            this.handleMonsterAttacked(data);
        });
        subscribeSafe('GameWorld', MONSTER_ATTACKED_MONSTER_RECEIVED, (data: MonsterAttackedMonsterEventData) => {
            this.handleMonsterAttackedMonster(data);
        });
        subscribeSafe('GameWorld', MONSTER_DIED_RECEIVED, (data: MonsterDiedEventData) => {
            this.handleMonsterDied(data);
        });
        subscribeSafe('GameWorld', PLAYER_RECEIVE_DAMAGE_RECEIVED, (data: PlayerReceiveDamageEventData) => {
            this.handlePlayerReceiveDamage(data);
        });
        subscribeSafe('GameWorld', PLAYER_TAKE_DAMAGE_RECEIVED, (data: PlayerTakeDamageEventData) => {
            this.handlePlayerTakeDamage(data);
        });
        subscribeSafe('GameWorld', HP_UPDATED_RECEIVED, (data: { hp: number; maxHp: number }) => {
            this.player?.setHp(data.hp, data.maxHp);
        });
        subscribeSafe('GameWorld', MONSTER_TAKE_DAMAGE_RECEIVED, (data: MonsterTakeDamageEventData) => {
            this.handleMonsterTakeDamage(data);
        });
        subscribeSafe('GameWorld', MONSTER_TAKE_DAMAGE_BY_MONSTER_RECEIVED, (data: MonsterTakeDamageByMonsterEventData) => {
            this.handleMonsterTakeDamageByMonster(data);
        });
        subscribeSafe('GameWorld', PLAYER_JOINED_RECEIVED, (data: NetworkPlayer[]) => {
            for (const p of data) {
                this.handlePlayerEnteredRange(p);
            }
        });
        subscribeSafe('GameWorld', REMOTE_PLAYER_ITEM_EQUIPPED_RECEIVED, (data: ItemEquippedEventData) => {
            this.handleRemotePlayerItemEquipped(data);
        });
        subscribeSafe('GameWorld', REMOTE_PLAYER_ITEM_UNEQUIPPED_RECEIVED, (data: ItemUnequippedEventData) => {
            this.handleRemotePlayerItemUnequipped(data);
        });
        subscribeSafe('GameWorld', PLAYER_LEFT_RECEIVED, (playerIds: string[]) => {
            for (const playerId of playerIds) {
                this.handlePlayerLeftRange({ playerId });
            }
        });
        subscribeSafe('GameWorld', PLAYER_MOVED_RECEIVED, (data: PlayerMovedEventData) => {
            this.handlePlayerMoved(data);
        });
        subscribeSafe('GameWorld', PLAYER_ATTACKED_MONSTER_RECEIVED, (data: PlayerAttackedMonsterEventData) => {
            this.handlePlayerAttackedMonster(data);
        });
        subscribeSafe('GameWorld', PLAYER_ATTACKED_PLAYER_RECEIVED, (data: PlayerAttackedPlayerEventData) => {
            this.handlePlayerAttackedPlayer(data);
        });
        subscribeSafe('GameWorld', PLAYER_PICKUP_PERFORMED_RECEIVED, (data: PlayerPickupPerformedEventData) => {
            this.handlePlayerPickupPerformed(data);
        });
        subscribeSafe('GameWorld', PLAYER_BOW_STANCE_PERFORMED_RECEIVED, (data: PlayerBowStancePerformedEventData) => {
            this.handlePlayerBowStancePerformed(data);
        });
        subscribeSafe('GameWorld', SPELL_CAST_STARTED_RECEIVED, (data: SpellCastStartedEventData) => {
            this.handleSpellCastStarted(data);
        });
        subscribeSafe('GameWorld', SPELL_CAST_CANCELLED_RECEIVED, (data: SpellCastCancelledEventData) => {
            this.handleSpellCastCancelled(data);
        });
        subscribeSafe('GameWorld', SPELL_CAST_FAILED_RECEIVED, () => {
            this.player?.onSpellCastRejected();
        });
        subscribeSafe('GameWorld', CAST_AOE_SPELL_RECEIVED, (data: CastAoeSpellEventData) => {
            this.handleCastAoeSpell(data);
        });
        subscribeSafe('GameWorld', CAST_DIRECTIONAL_AOE_SPELL_RECEIVED, (data: CastDirectionalAoeSpellEventData) => {
            this.handleCastDirectionalAoeSpell(data);
        });
        subscribeSafe('GameWorld', MONSTER_CAST_AOE_SPELL_RECEIVED, (data: MonsterCastAoeSpellEventData) => {
            this.handleMonsterCastAoeSpell(data);
        });
        subscribeSafe('GameWorld', MONSTER_CAST_DIRECTIONAL_AOE_SPELL_RECEIVED, (data: MonsterCastDirectionalAoeSpellEventData) => {
            this.handleMonsterCastDirectionalAoeSpell(data);
        });
        subscribeSafe('GameWorld', PLAYER_MOVEMENT_STATE_CHANGED_RECEIVED, (data: PlayerMovementStateChangedEventData) => {
            this.handlePlayerMovementStateChanged(data);
        });
        subscribeSafe('GameWorld', PLAYER_ATTACK_MODE_CHANGED_RECEIVED, (data: PlayerAttackModeChangedEventData) => {
            this.handlePlayerAttackModeChanged(data);
        });
        subscribeSafe('GameWorld', PLAYER_IDLE_DIRECTION_CHANGED_RECEIVED, (data: PlayerIdleDirectionChangedEventData) => {
            this.handlePlayerIdleDirectionChanged(data);
        });
        subscribeSafe('GameWorld', PLAYER_APPEARANCE_CHANGED_RECEIVED, (data: PlayerAppearanceChangedEventData) => {
            this.handlePlayerAppearanceChanged(data);
        });
        subscribeSafe('GameWorld', PLAYER_DISCONNECTED_RECEIVED, (data: PlayerConnectionStateChangedEventData) => {
            this.handlePlayerDisconnected(data);
        });
        subscribeSafe('GameWorld', PLAYER_RECONNECTED_RECEIVED, (data: PlayerConnectionStateChangedEventData) => {
            this.handlePlayerReconnected(data);
        });
        subscribeSafe('GameWorld', RESET_POSITION_RECEIVED, (data: { x: number; y: number; remainingStunlockMs?: number }) => {
            this.player?.resetPosition(data.x, data.y, data.remainingStunlockMs);
        });
        subscribeSafe('GameWorld', PLAYER_TELEPORTED_RECEIVED, (data: { x: number; y: number }) => {
            this.player?.applyTeleport(data.x, data.y);
        });
        subscribeSafe('GameWorld', POSITION_CORRECTED_RECEIVED, (data: { curX: number; curY: number; destX: number; destY: number }) => {
            this.pendingCourseCorrections.push({ curX: data.curX, curY: data.curY, destX: data.destX, destY: data.destY });
        });
        subscribeSafe('GameWorld', PLAYER_PARALYZED_RECEIVED, (data: { durationSeconds: number }) => {
            this.player?.setParalysisUntil(Date.now() + data.durationSeconds * 1000);
            this.player?.cancelMovement();
        });
        subscribeSafe('GameWorld', PLAYER_SPAWN_PROTECTION_ENABLED_RECEIVED, (data: { playerId: string }) => {
            this.handleSpawnProtectionEnabled(data);
        });
        subscribeSafe('GameWorld', PLAYER_SPAWN_PROTECTION_DISABLED_RECEIVED, (data: { playerId: string }) => {
            this.handleSpawnProtectionDisabled(data);
        });
        subscribeSafe('GameWorld', TEMPORARY_EFFECT_APPLIED_FOR_PLAYER_RECEIVED, (data: TemporaryEffectPlayerEventData) => {
            this.handleTemporaryEffectAppliedForPlayer(data);
        });
        subscribeSafe('GameWorld', TEMPORARY_EFFECT_EXPIRED_FOR_PLAYER_RECEIVED, (data: TemporaryEffectPlayerEventData) => {
            this.handleTemporaryEffectExpiredForPlayer(data);
        });
        subscribeSafe('GameWorld', TEMPORARY_EFFECT_APPLIED_FOR_MONSTER_RECEIVED, (data: TemporaryEffectMonsterEventData) => {
            this.handleTemporaryEffectAppliedForMonster(data);
        });
        subscribeSafe('GameWorld', TEMPORARY_EFFECT_EXPIRED_FOR_MONSTER_RECEIVED, (data: TemporaryEffectMonsterEventData) => {
            this.handleTemporaryEffectExpiredForMonster(data);
        });
        subscribeSafe('GameWorld', CAST_EFFECT_RECEIVED, (data: { effectKey: string; x: number; y: number }) => {
            this.handleCastEffectAtCell(data);
        });
    }

    private setupMonsterEventListeners(): void {
        // Listen for monster death events to remove them from the game
        subscribeSafe('GameWorld', MONSTER_DEAD, (data: { monsterId: string }) => {
            const monsterIndex = this.monsters.findIndex(m => m.getMonsterId() === data.monsterId);
            if (monsterIndex !== -1) {
                const monster = this.monsters[monsterIndex];

                // Clear player's attack target if it was this monster
                if (this.player && this.player.getAttackTarget() === monster) {
                    this.player.clearAttackTarget();
                }

                // Destroy monster and remove from list
                monster.destroy();
                this.monsters.splice(monsterIndex, 1);
            }
        });

    }

    private setupMapDialogEventListeners(): void {
        // Listen for non-movable cells highlight toggle events from React
        subscribeSafe('GameWorld', IN_UI_TOGGLE_NON_MOVABLE_CELLS_HIGHLIGHT, (enabled: boolean) => {
            const currentMap = this.getCurrentMap();
            if (enabled) {
                currentMap.enableNonMovableCellsHighlight(this);
            } else {
                currentMap.disableNonMovableCellsHighlight();
            }
        });

        // Listen for teleport cells highlight toggle events from React
        subscribeSafe('GameWorld', IN_UI_TOGGLE_TELEPORT_CELLS_HIGHLIGHT, (enabled: boolean) => {
            const currentMap = this.getCurrentMap();
            if (enabled) {
                currentMap.enableTeleportCellsHighlight(this);
            } else {
                currentMap.disableTeleportCellsHighlight();
            }
        });

        subscribeSafe('GameWorld', IN_UI_TOGGLE_SERVER_TELEPORT_CELLS_HIGHLIGHT, (enabled: boolean) => {
            const currentMap = this.getCurrentMap();
            if (enabled) {
                currentMap.enableServerTeleportCellsHighlight(this);
            } else {
                currentMap.disableServerTeleportCellsHighlight();
            }
        });

        // Listen for water cells highlight toggle events from React
        subscribeSafe('GameWorld', IN_UI_TOGGLE_WATER_CELLS_HIGHLIGHT, (enabled: boolean) => {
            const currentMap = this.getCurrentMap();
            if (enabled) {
                currentMap.enableWaterCellsHighlight(this);
            } else {
                currentMap.disableWaterCellsHighlight();
            }
        });

        // Listen for farmable cells highlight toggle events from React
        subscribeSafe('GameWorld', IN_UI_TOGGLE_FARMABLE_CELLS_HIGHLIGHT, (enabled: boolean) => {
            const currentMap = this.getCurrentMap();
            if (enabled) {
                currentMap.enableFarmableCellsHighlight(this);
            } else {
                currentMap.disableFarmableCellsHighlight();
            }
        });

        // Listen for map tiles render toggle events from React
        subscribeSafe('GameWorld', IN_UI_TOGGLE_RENDER_MAP_TILES, (enabled: boolean) => {
            const currentMap = this.getCurrentMap();
            if (enabled) {
                currentMap.renderMapTiles(this);
            } else {
                currentMap.destroyMapTiles(this);
            }
        });

        // Listen for map objects render toggle events from React
        subscribeSafe('GameWorld', IN_UI_TOGGLE_RENDER_MAP_OBJECTS, (enabled: boolean) => {
            const currentMap = this.getCurrentMap();
            if (enabled) {
                currentMap.renderMapObjects(this);
            } else {
                currentMap.destroyMapObjects();
            }
        });

        // Listen for debug mode toggle events from React
        subscribeSafe('GameWorld', IN_UI_TOGGLE_DEBUG_MODE, (enabled: boolean) => {
            setDebugModeEnabled(this, enabled);
        });

        // Listen for grid display toggle events from React
        subscribeSafe('GameWorld', IN_UI_TOGGLE_GRID_DISPLAY, (enabled: boolean) => {
            const currentMap = this.getCurrentMap();
            if (enabled) {
                currentMap.enableGridDisplay(this);
            } else {
                currentMap.disableGridDisplay();
            }
        });

        // Listen for display large items toggle events from React
        subscribeSafe('GameWorld', IN_UI_TOGGLE_DISPLAY_LARGE_ITEMS, (enabled: boolean) => {
            setDisplayLargeItemsEnabled(this, enabled);
        });

        // Listen for weather change events from React (local preview + server request)
        subscribeSafe('GameWorld', IN_UI_CHANGE_WEATHER, (weather: WeatherMode) => {
            this.weatherManager?.setWeather(weather);
            getNetworkManager(this.game)?.sendWeatherChangeRequest(weather);
        });
        subscribeSafe('GameWorld', OUT_WEATHER_SYNCED, (weather: WeatherMode) => {
            this.weatherManager?.setWeather(weather);
            syncWeather(weather);
        });
    }

    private setupServerDialogEventListeners(): void {
        subscribeSafe('GameWorld', IN_UI_MAKE_SERVER_CELL_OCCUPIED_MODE, () => {
            this.awaitingMakeServerCellOccupiedClick = true;
            this.awaitingPlayerTeleportClick = false;
            this.player?.cancelMovement();
        });

        subscribeSafe('GameWorld', IN_UI_PLAYER_TELEPORT_REQUEST_MODE, () => {
            this.awaitingPlayerTeleportClick = true;
            this.awaitingMakeServerCellOccupiedClick = false;
            this.player?.cancelMovement();
        });

        subscribeSafe('GameWorld', IN_UI_CHANGE_GRACE_PERIOD, (ms: number) => {
            for (const player of this.playersById.values()) {
                if (player !== this.player) {
                    player.setRemoteIdleContinuationGraceMs(ms);
                }
            }
            for (const monster of this.monsters) {
                monster.setRemoteIdleContinuationGraceMs(ms);
            }
        });
    }

    private setupInputManager(): void {
        this.inputManager = new InputManager({
            scene: this,
            isEnabled: () => !this.loadingMap,
            acceptLeftMouseDown: () => !this.loadingMap,
            onPointerMove: (worldPixelX, worldPixelY) => {
                this.getCurrentMap().updateHoverCell(this, worldPixelX, worldPixelY);
            },
            onPointerDown: (pointer) => {
                if (this.awaitingMakeServerCellOccupiedClick || this.awaitingPlayerTeleportClick) {
                    return;
                }
                if (pointer.leftButtonDown() && this.player && this.cameras?.main) {
                    if (this.castManager?.getPendingEffectKey() || this.player.hasPendingSpell()) {
                        return;
                    }
                    const attackTarget = this.getAttackableTargetUnderPointer(pointer);
                    if (attackTarget) {
                        this.player.attack(attackTarget);
                    }
                } else if (pointer.rightButtonDown() && this.player) {
                    if (this.castManager?.getPendingEffectKey()) {
                        this.castManager.clearPendingEffect();
                        return;
                    }
                    if (this.player.hasPendingSpell()) {
                        this.player.onRightClick();
                        return;
                    }
                    this.player.cancelMovement();
                }
            },
            onPointerUp: (pointer) => {
                if (this.awaitingMakeServerCellOccupiedClick) {
                    this.awaitingMakeServerCellOccupiedClick = false;
                    const camera = this.cameras?.main;
                    if (camera) {
                        const worldPixelX = pointer.x + camera.scrollX;
                        const worldPixelY = pointer.y + camera.scrollY;
                        const destX = convertPixelPosToWorldPos(worldPixelX);
                        const destY = convertPixelPosToWorldPos(worldPixelY);
                        getNetworkManager(this.game)?.sendMakeCellOccupiedRequest(destX, destY);
                    }
                    return;
                }
                if (this.awaitingPlayerTeleportClick) {
                    this.awaitingPlayerTeleportClick = false;
                    const camera = this.cameras?.main;
                    if (camera) {
                        const worldPixelX = pointer.x + camera.scrollX;
                        const worldPixelY = pointer.y + camera.scrollY;
                        const destX = convertPixelPosToWorldPos(worldPixelX);
                        const destY = convertPixelPosToWorldPos(worldPixelY);
                        getNetworkManager(this.game)?.sendPlayerTeleportRequested(destX, destY);
                    }
                    return;
                }
                if (!this.player || !this.cameras?.main) {
                    return;
                }
                if (this.suppressLeftMouseMovementUntilRelease) {
                    this.suppressLeftMouseMovementUntilRelease = false;
                    return;
                }
                if (this.castManager?.getPendingEffectKey() || this.player.hasPendingSpell()) {
                    return;
                }
                if (this.castManager?.getCastReady()) {
                    this.castManager.setCastReady(false);
                    return;
                }
                const attackTarget = this.getAttackableTargetUnderPointer(pointer);
                if (attackTarget) {
                    this.player.attack(attackTarget);
                    if (!this.player.isParalyzed() &&
                        getDistance(
                            this.player.getWorldX(),
                            this.player.getWorldY(),
                            attackTarget.getWorldX(),
                            attackTarget.getWorldY()
                        ) > this.player.getAttackRange()) {
                        this.player.setDestination(attackTarget.getWorldX(), attackTarget.getWorldY(), false);
                    }
                    return;
                }
                const camera = this.cameras.main;
                const worldPixelX = pointer.x + camera.scrollX;
                const worldPixelY = pointer.y + camera.scrollY;
                let destX = convertPixelPosToWorldPos(worldPixelX);
                let destY = convertPixelPosToWorldPos(worldPixelY);
                const distanceToDest = getDistance(
                    this.player.getWorldX(),
                    this.player.getWorldY(),
                    destX,
                    destY
                );
                if (distanceToDest === 0) {
                    // Click on own cell: switch to PickUp mode (play pickup animation)
                    this.player.requestPickUp();
                    return;
                }
                if (distanceToDest < 2) {
                    // If player is moving towards adjacent cell, stop the movement, instead of trying to find pathfinded route
                    this.player.cancelMovement();
                    return;
                }
                const map = this.getCurrentMap();
                if (!isCellMovable(map, destX, destY)) {
                    const movableLocation = findMovableLocation(map, destX, destY);
                    if (movableLocation) {
                        destX = movableLocation.x;
                        destY = movableLocation.y;
                    }
                }
                if (!this.player.isParalyzed()) {
                    this.player.setDestination(destX, destY, false);
                }
            }
        });
        this.inputManager.setup();
    }

    private setupCameraStatsUpdateInterval(): void {
        // Track actual cursor position for elementFromPoint - Phaser's pointer can be stale when cursor is over DOM overlays (e.g. inventory dialog)
        const handleMouseMove = (e: MouseEvent) => {
            try {
                this.lastCursorPosition = { x: e.clientX, y: e.clientY };
            } catch (error) {
                console.error('[GameWorld:documentMouseMove]', error);
            }
        };
        document.addEventListener('mousemove', handleMouseMove, { passive: true });
        this.cursorPositionCleanup = () => {
            document.removeEventListener('mousemove', handleMouseMove);
        };

        // Set up interval to emit FPS, camera position, and player position updates every 20ms
        this.updateInterval = window.setInterval(() => {
            // Check if game, cameras, and main camera are still valid
            if (!this.game || !this.cameras || !this.cameras.main) {
                return;
            }

            try {
                const fps = Math.round(this.game.loop.actualFps);
                const networkManager = getNetworkManager(this.game);
                const ping = networkManager?.getLatestPing();
                const pingVariance = networkManager?.getLatestPingVariance();
                const queueLengths = networkManager?.getLatestQueueLengths();
                const camX = Math.round(this.cameras.main.scrollX);
                const camY = Math.round(this.cameras.main.scrollY);

                // Include player position if player exists
                let playerSceneX: number | undefined = undefined;
                let playerSceneY: number | undefined = undefined;
                let playerWorldX: number | undefined = undefined;
                let playerWorldY: number | undefined = undefined;

                let playerGender: Gender | undefined = undefined;
                if (this.player) {
                    playerSceneX = Math.round(this.player.getPixelX());
                    playerSceneY = Math.round(this.player.getPixelY());
                    playerWorldX = this.player.getWorldX();
                    playerWorldY = this.player.getWorldY();
                    playerGender = this.player.getGender();
                }

                EventBus.emit(OUT_UI_GAME_STATS_UPDATE, {
                    fps,
                    ping,
                    pingVariance,
                    gameWorldQueueLength: queueLengths?.gameWorldQueueLength,
                    playersInMap: queueLengths?.playersInMap,
                    cameraX: camX,
                    cameraY: camY,
                    playerSceneX,
                    playerSceneY,
                    playerWorldX,
                    playerWorldY,
                    playerGender,
                });

                // Broadcast combat-target and ground-item hover state
                const pointer = this.input.activePointer;
                const hoveredAttackTarget = pointer ? this.getAttackableTargetUnderPointer(pointer) : undefined;
                const hoveredMonster =
                    pointer && this.cameras?.main
                        ? getMonsterUnderWorldPixelForHoverUi(
                            this.monsters,
                            pointer.x + this.cameras.main.scrollX,
                            pointer.y + this.cameras.main.scrollY,
                        )
                        : undefined;
                const liveMonsterForHover = hoveredMonster && !hoveredMonster.isDead() ? hoveredMonster : undefined;
                const hoveredNpcForHover =
                    pointer && this.cameras?.main
                        ? getNpcUnderWorldPixelForHover(
                            this.npcs,
                            pointer.x + this.cameras.main.scrollX,
                            pointer.y + this.cameras.main.scrollY,
                        )
                        : undefined;
                const liveNpcForHover = hoveredNpcForHover && !hoveredNpcForHover.isDead() ? hoveredNpcForHover : undefined;
                const hoveredPlayerForHover =
                    pointer && this.cameras?.main
                        ? getPlayerUnderWorldPixelForHover(
                            this.playersById,
                            pointer.x + this.cameras.main.scrollX,
                            pointer.y + this.cameras.main.scrollY,
                        )
                        : undefined;
                const hoveredGroundItem =
                    pointer && this.cameras?.main
                        ? getGroundItemUnderPointer(this.groundItems, pointer, this.cameras.main)
                        : undefined;
                EventBus.emit(OUT_UI_HOVER_ATTACKABLE_TARGET, !!hoveredAttackTarget);
                EventBus.emit(OUT_UI_HOVER_GROUND_ITEM, !!hoveredGroundItem);
                // Use actual cursor position for elementFromPoint - Phaser's pointer can be stale when cursor is over DOM overlays (inventory, etc.)
                const checkX = this.lastCursorPosition?.x ?? (pointer ? canvasToScreenPosition(pointer.x, pointer.y, this.game).screenX : 0);
                const checkY = this.lastCursorPosition?.y ?? (pointer ? canvasToScreenPosition(pointer.x, pointer.y, this.game).screenY : 0);
                const el = document.elementFromPoint(checkX, checkY);
                if (el === this.game.canvas && pointer) {
                    const { screenX, screenY } = canvasToScreenPosition(pointer.x, pointer.y, this.game);
                    EventBus.emit(
                        OUT_UI_HOVER_GROUND_ITEM_INFO,
                        hoveredGroundItem ? hoveredGroundItem.getHoverInfo(screenX, screenY) : undefined
                    );
                }
                let showMonsterHover = false;
                let showNpcHover = false;
                let showPlayerHover = false;
                let monsterHoverInfo: MonsterHoverInfo | undefined;
                let npcHoverInfo: NpcHoverInfo | undefined;
                let playerHoverInfo: PlayerHoverInfo | undefined;

                type HoverPick =
                    | { kind: 'npc'; depth: number; npc: NPC }
                    | { kind: 'monster'; depth: number; monster: Monster }
                    | { kind: 'player'; depth: number; player: Player };

                const hoverCandidates: HoverPick[] = [];
                if (liveNpcForHover) {
                    hoverCandidates.push({ kind: 'npc', depth: liveNpcForHover.getDepth(), npc: liveNpcForHover });
                }
                if (liveMonsterForHover) {
                    hoverCandidates.push({
                        kind: 'monster',
                        depth: liveMonsterForHover.getDepth(),
                        monster: liveMonsterForHover,
                    });
                }
                if (hoveredPlayerForHover) {
                    hoverCandidates.push({
                        kind: 'player',
                        depth: hoveredPlayerForHover.getDepth(),
                        player: hoveredPlayerForHover,
                    });
                }

                if (hoverCandidates.length > 0) {
                    const top = hoverCandidates.reduce((a, b) => (a.depth >= b.depth ? a : b));
                    switch (top.kind) {
                        case 'npc': {
                            showNpcHover = true;
                            const anchorX = top.npc.getAnimatedPixelX();
                            const anchorY = top.npc.getAnimatedPixelY() + MONSTER_HOVER_OVERLAY_ANCHOR_OFFSET_Y;
                            const camera = this.cameras.main;
                            const canvasX = anchorX - camera.scrollX;
                            const canvasY = anchorY - camera.scrollY;
                            const { screenX: overlayScreenX, screenY: overlayScreenY } = canvasToScreenPosition(
                                canvasX,
                                canvasY,
                                this.game,
                            );
                            npcHoverInfo = {
                                name: top.npc.getDisplayName(),
                                overlayScreenX,
                                overlayScreenY,
                            };
                            break;
                        }
                        case 'monster': {
                            showMonsterHover = true;
                            const anchorX = top.monster.getAnimatedPixelX();
                            const anchorY = top.monster.getAnimatedPixelY() + MONSTER_HOVER_OVERLAY_ANCHOR_OFFSET_Y;
                            const camera = this.cameras.main;
                            const canvasX = anchorX - camera.scrollX;
                            const canvasY = anchorY - camera.scrollY;
                            const { screenX: overlayScreenX, screenY: overlayScreenY } = canvasToScreenPosition(
                                canvasX,
                                canvasY,
                                this.game,
                            );
                            monsterHoverInfo = {
                                name: top.monster.getDisplayName(),
                                hp: top.monster.getHp(),
                                maxHp: top.monster.getMaxHp(),
                                allegiance: top.monster.getAllegiance(),
                                overlayScreenX,
                                overlayScreenY,
                            };
                            break;
                        }
                        case 'player': {
                            showPlayerHover = true;
                            const anchorX = top.player.getAnimatedPixelX();
                            const anchorY = top.player.getAnimatedPixelY() + PLAYER_HOVER_OVERLAY_ANCHOR_OFFSET_Y;
                            const camera = this.cameras.main;
                            const canvasX = anchorX - camera.scrollX;
                            const canvasY = anchorY - camera.scrollY;
                            const { screenX: overlayScreenX, screenY: overlayScreenY } = canvasToScreenPosition(
                                canvasX,
                                canvasY,
                                this.game,
                            );
                            playerHoverInfo = {
                                characterName: top.player.getCharacterName(),
                                spawnProtection: top.player.hasSpawnProtection(),
                                overlayScreenX,
                                overlayScreenY,
                            };
                            break;
                        }
                    }
                }

                EventBus.emit(OUT_UI_HOVER_MONSTER, showMonsterHover ? monsterHoverInfo : undefined);
                EventBus.emit(OUT_UI_HOVER_NPC, showNpcHover ? npcHoverInfo : undefined);
                EventBus.emit(OUT_UI_HOVER_PLAYER, showPlayerHover ? playerHoverInfo : undefined);
            } catch (error) {
                console.warn('Failed to update game stats:', error);
            }
        }, GAME_STATS_UPDATE_INTERVAL_MS);
    }

    private initializeGameObjects(): void {
        const gameStateManager = getGameStateManager(this.game);
        const map = this.getCurrentMap();

        let playerWorldX: number;
        let playerWorldY: number;

        if (this.initialGameWorldState) {
            if (this.initialGameWorldState.playerX === -1 || this.initialGameWorldState.playerY === -1) {
                playerWorldX = Math.floor(map.sizeX / 2);
                playerWorldY = Math.floor(map.sizeY / 2);
                console.log(`[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Map change, using center position:`, { playerWorldX, playerWorldY });
            } else {
                playerWorldX = this.initialGameWorldState.playerX;
                playerWorldY = this.initialGameWorldState.playerY;
                console.log(`[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Using server-provided coordinates:`, { playerWorldX, playerWorldY });
            }
        } else {
            playerWorldX = Math.floor(map.sizeX / 2);
            playerWorldY = Math.floor(map.sizeY / 2);
            console.log(`[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] No initial state, using center position:`, { playerWorldX, playerWorldY });
        }

        // Check if the calculated position is movable, if not find nearest movable location
        const initialTile = map.getTile(playerWorldX, playerWorldY);
        if (!initialTile || !initialTile.isMoveAllowed) {
            console.log(`[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Initial position is not movable, searching for movable location...`);
            const movableLocation = findMovableLocation(map, playerWorldX, playerWorldY);
            if (movableLocation) {
                playerWorldX = movableLocation.x;
                playerWorldY = movableLocation.y;
                console.log(`[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Found movable location:`, { playerWorldX, playerWorldY });
            } else {
                console.warn(`[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] No movable location found near initial position, using original coordinates`);
            }
        }

        this.selfPlayerId = this.initialGameWorldState?.playerId;

        let initialPlayerDirection = Direction.NorthEast;
        const direction = this.initialGameWorldState?.playerDirection;
        if (direction !== undefined) {
            const resolvedDirection = toDirection(direction);
            if (resolvedDirection !== Direction.None) {
                initialPlayerDirection = resolvedDirection;
            }
        }

        const initialMovementSpeedMs =
            this.initialGameWorldState?.movementSpeedMs !== undefined && this.initialGameWorldState.movementSpeedMs > 0
                ? this.initialGameWorldState.movementSpeedMs
                : playerDialogStore.state.movementSpeed;

        // Create player at server-provided or fallback center position
        this.player = new Player(
            this,
            playerWorldX,
            playerWorldY,
            initialPlayerDirection,
            this.soundManager,
            map,
            this.createDefaultPlayerGear(),
            initialMovementSpeedMs,
        );
        if (this.selfPlayerId) {
            this.player.setPlayerId(this.selfPlayerId);
            this.playersById.set(this.selfPlayerId, this.player);
        }
        const savedCharacterName = getGameStateManager(this.game).getCharacterName();
        if (savedCharacterName) {
            this.player.setCharacterName(savedCharacterName);
        }
        setPlayerPosition(this.game, playerWorldX, playerWorldY);

        const runMode = this.initialGameWorldState?.runMode ?? playerDialogStore.state.runMode;
        this.player.setRunMode(runMode);

        const igw = this.initialGameWorldState;
        if (igw?.attackRangeCells !== undefined && igw.attackRangeCells > 0) {
            this.player.setAttackRange(igw.attackRangeCells);
        } else {
            this.player.setAttackRange(playerDialogStore.state.attackRange);
        }
        if (igw?.attackSpeedMs !== undefined && igw.attackSpeedMs > 0) {
            this.player.setAttackSpeedFromDurationMs(igw.attackSpeedMs);
        } else {
            this.player.setAttackSpeedFromDurationMs(playerDialogStore.state.attackSpeedMs);
        }
        if (igw?.arrowSpeedPxPerSec !== undefined && igw.arrowSpeedPxPerSec > 0) {
            this.arrowSpeedPxPerSec = igw.arrowSpeedPxPerSec;
            this.player.setArrowSpeedPxPerSec(igw.arrowSpeedPxPerSec);
        } else {
            this.arrowSpeedPxPerSec = 1000;
            this.player.setArrowSpeedPxPerSec(1000);
        }
        const attackType = igw?.attackType !== undefined
            ? igw.attackType as AttackType
            : playerDialogStore.state.attackType;
        this.player.setAttackType(attackType);
        if (igw?.castSpeedMs !== undefined && igw.castSpeedMs >= 200 && igw.castSpeedMs <= 2000) {
            this.player.setCastDurationMs(igw.castSpeedMs);
        } else {
            this.player.setCastDurationMs(playerDialogStore.state.castSpeedMs);
        }
        const igwHp = this.initialGameWorldState?.hp;
        const igwMaxHp = this.initialGameWorldState?.maxHp;
        if (igwHp !== undefined && igwMaxHp !== undefined) {
            this.player.setHp(igwHp, igwMaxHp);
        }
        if (this.initialGameWorldState?.dead) {
            this.player.applySpawnedDeathState();
        }
        setDeathDialogOpen(this.initialGameWorldState?.dead === true);
        const attackMode = this.initialGameWorldState?.attackMode !== undefined
            ? this.initialGameWorldState.attackMode
            : playerDialogStore.state.attackMode;
        this.player.setAttackMode(attackMode);

        const pickupMs = this.initialGameWorldState?.playerPickupAnimationTimeMs;
        if (pickupMs !== undefined && pickupMs > 0) {
            this.player.setPlayerPickupAnimationMs(pickupMs);
        }
        const bowMs = this.initialGameWorldState?.playerBowAnimationDurationMs;
        if (bowMs !== undefined && bowMs > 0) {
            this.player.setPlayerBowAnimationDurationMs(bowMs);
        }

        const networkManager = getNetworkManager(this.game);
        const applySpawnProtection = this.pendingSpawnProtectionForSelf || networkManager?.getAndClearPendingSpawnProtectionForSelf();
        if (applySpawnProtection) {
            this.pendingSpawnProtectionForSelf = false;
            this.player.setSpawnProtectionEffect(true);
        }

        // Apply saved music volume from GameStateManager
        const savedMusicVolume = gameStateManager.getMusicVolume();
        getMusicManager(this).setMusicVolume(savedMusicVolume);

        // Apply saved sound volume from GameStateManager
        const savedSoundVolume = gameStateManager.getSoundVolume();
        this.soundManager.setSoundVolume(savedSoundVolume);

        // Center camera around player
        this.cameraManager?.centerOn(convertWorldPosToPixelPos(playerWorldX), convertWorldPosToPixelPos(playerWorldY));
    }

    /**
     * Initializes game objects (player and NPCs) after minimap capture is complete.
     * This is called by captureMinimap() to ensure objects don't appear in the minimap.
     */
    private createDefaultPlayerGear() {
        return {
            ...DEFAULT_GEAR,
            underwearColorIndex: playerDialogStore.state.underwearColorIndex,
            hairStyleIndex: playerDialogStore.state.hairStyleIndex,
        };
    }

    private syncMonstersFromNetworkState(): void {
        const inView = getNetworkManager(this.game)?.getMonstersInViewState() ?? [];
        for (const entry of inView) {
            this.handleMonsterEnteredRange(entry);
        }
    }

    private syncNpcsFromNetworkState(): void {
        const inView = getNetworkManager(this.game)?.getNpcsInViewState() ?? [];
        for (const entry of inView) {
            this.handleNpcEnteredRange(entry);
        }
    }

    private syncGroundStatesFromNetworkState(): void {
        const groundStates = getNetworkManager(this.game)?.getGroundStatesInViewState() ?? [];
        this.handleGroundStatesEnteredRange(groundStates);
    }

    private syncOtherPlayersFromNetworkState(): void {
        const otherPlayers = getNetworkManager(this.game)?.getOtherPlayersState() ?? [];
        for (const otherPlayer of otherPlayers) {
            this.handlePlayerEnteredRange(otherPlayer);
        }
    }

    private tryFinalizeMapSetup(): void {
        if (this.awaitingTransferredWorldState) {
            this.tryConsumePendingTransferredWorldState();
        }
        if (!this.pendingLoadedMap) {
            return;
        }
        if (this.awaitingTransferredWorldState) {
            return;
        }

        const map = this.pendingLoadedMap;
        this.pendingLoadedMap = undefined;
        this.setupMap(map);
        EventBus.emit(OUT_MAP_LOADED);
        if (this.gameWorldId) {
            EventBus.emit(OUT_UI_SET_SELECTED_MAP, this.gameWorldId);
        }
    }

    /**
     * Completes map setup after minimap capture by initializing game objects
     * and setting up overlay removal timing.
     * This is called AFTER the minimap snapshot has been taken, so it's safe to apply the saved zoom here.
     */
    private setupMap(map: HBMap): void {
        this.displayedMap = map;
        // Now initialize game objects (player, NPCs, etc.)
        this.initializeGameObjects();

        // Apply camera zoom AFTER minimap snapshot has been taken
        // This ensures the zoom is applied to the main camera, not the minimap snapshot camera
        // Get camera zoom from GameStateManager (saved zoom level as percentage 20-200, where 100 = zoom 1.0)
        const gameStateManager = getGameStateManager(this.game);
        const savedCameraZoom = gameStateManager.getCameraZoom();
        // Convert percentage to zoom value (e.g., 100% = 1.0, 50% = 0.5, 200% = 2.0)
        const cameraZoom = savedCameraZoom / 100;
        this.cameraManager?.setZoom(cameraZoom);
        console.log(`[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Applied saved camera zoom after minimap snapshot:`, savedCameraZoom, '% =', cameraZoom);

        // Map has been fully loaded
        this.loadingMap = false;
        this.hideNativeOverlayMapLoading();
        this.syncMonstersFromNetworkState();
        this.syncNpcsFromNetworkState();
        this.syncGroundStatesFromNetworkState();
        this.syncOtherPlayersFromNetworkState();
        this.tryPushWorldTeleportCellsToCurrentMap();
    }

    private setTeleportLocs(teleportLocs: TeleportLocSet[] | undefined): void {
        this.teleportTargetsBySourceCell.clear();
        this.lastTeleportLocSets = teleportLocs ? [...teleportLocs] : [];
        if (!teleportLocs?.length) {
            this.tryPushWorldTeleportCellsToCurrentMap();
            return;
        }

        for (const teleportLoc of teleportLocs) {
            for (const loc of teleportLoc.locs) {
                this.teleportTargetsBySourceCell.set(this.getTeleportCellKey(loc.x, loc.y), teleportLoc.target);
            }
        }
        this.tryPushWorldTeleportCellsToCurrentMap();
    }

    /**
     * Pushes server teleport source cells (from InitialGameWorldState via NetworkManager) onto the loaded map for debug overlay.
     */
    private tryPushWorldTeleportCellsToCurrentMap(): void {
        if (!this.mapManager) {
            return;
        }
        try {
            const mapName = this.mapManager.getCurrentMapName();
            const currentMap = getMapIfPresent(this, mapName);
            if (!currentMap) {
                // Teleport locs often arrive in init before lazy `prepareMapForGameWorld` registers the map;
                // `setupMap` calls this again once the HBMap exists.
                return;
            }
            currentMap.setServerTeleportSourceCells(getTeleportSourceCellsFromLocSets(this.lastTeleportLocSets));
            if (mapDialogStore.state.showServerTeleportCells) {
                currentMap.enableServerTeleportCellsHighlight(this);
            } else {
                currentMap.disableServerTeleportCellsHighlight();
            }
        } catch (error) {
            console.warn(
                `[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Could not sync server teleport cells:`,
                error,
            );
        }
    }

    private getTeleportCellKey(x: number, y: number): string {
        return `${x},${y}`;
    }

    private tryConsumePendingTransferredWorldState(): boolean {
        const pendingInitialGameWorldState = getNetworkManager(this.game)?.getAndClearPendingInitialGameWorldState();
        if (!pendingInitialGameWorldState) {
            return false;
        }

        this.applyTransferredWorldState(pendingInitialGameWorldState);
        return true;
    }

    private applyTransferredWorldState(data: InitialGameWorldStateEventData): void {
        this.awaitingTransferredWorldState = false;
        this.pendingPredictedWorldTransfer = false;
        this.initialGameWorldState = toRegistryInitialGameWorldState(data);
        setInitialGameWorldState(this.game, this.initialGameWorldState);
        this.mapManager?.setInitialMusicFile(data.musicFile);
        if (this.playMapMusic && data.musicFile) {
            this.mapManager?.playInitialMusic();
        }
        this.gameWorldId = data.gameWorldId;
        this.setTeleportLocs(data.teleportLocs);
        if (data.weather !== undefined) {
            this.weatherManager?.setWeather(data.weather);
            syncWeather(data.weather);
        }
    }

    private beginWorldTransfer(worldId: string, mapName: string): void {
        if (!worldId || worldId === this.gameWorldId) {
            return;
        }
        if (this.pendingPredictedWorldTransfer || this.awaitingTransferredWorldState) {
            return;
        }

        const networkManager = getNetworkManager(this.game);
        if (!networkManager) {
            console.warn(`[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Cannot change world: network manager not found`);
            return;
        }

        this.pendingPredictedWorldTransfer = true;
        getGameStateManager(this.game).saveGameState();
        networkManager.clearPendingInitialGameWorldState();
        networkManager.requestWorldChange(worldId, true, true);

        const pendingInitialGameWorldState: InitialGameWorldState = {
            gameWorldId: worldId,
            mapName: `${mapName}.amd`,
            playerX: -1,
            playerY: -1,
            teleportLocs: [],
            awaitTransferredWorldState: true,
        };
        setInitialGameWorldState(this.game, pendingInitialGameWorldState);
        this.scene.restart({ initialGameWorldState: pendingInitialGameWorldState });
    }

    private beginRequestedWorldChange(worldId: string, validateTeleport = false): void {
        const networkManager = getNetworkManager(this.game);
        if (!networkManager) {
            console.warn(`[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Cannot request world change: network manager not found`);
            return;
        }

        this.clearPendingRequestedWorldChangeListener();
        this.pendingRequestedWorldChangeListener = (data: InitialGameWorldStateEventData) => {
            try {
                if (data.gameWorldId !== worldId) {
                    return;
                }

                this.clearPendingRequestedWorldChangeListener();
                const initialGameWorldState = toRegistryInitialGameWorldState(data);
                getGameStateManager(this.game).saveGameState();
                setInitialGameWorldState(this.game, initialGameWorldState);
                this.scene.restart({ initialGameWorldState });
            } catch (error) {
                console.error('[GameWorld:pendingRequestedWorldChange]', error);
            }
        };
        EventBus.on(INITIAL_GAME_WORLD_STATE_RECEIVED, this.pendingRequestedWorldChangeListener);
        networkManager.requestWorldChange(worldId, false, validateTeleport);
    }

    private syncPlayerAppearance(): void {
        getNetworkManager(this.game)?.changePlayerAppearance(
            playerDialogStore.state.gender,
            playerDialogStore.state.skinColor,
            playerDialogStore.state.hairStyleIndex,
            playerDialogStore.state.underwearColorIndex,
        );
    }

    /**
     * Called every frame by Phaser. Updates game objects.
     * 
     * @param _time - Total elapsed time in milliseconds (unused)
     * @param delta - Time elapsed since last frame in milliseconds
     */
    public update(_time: number, delta: number): void {
        runSafeSync('GameWorld:update', () => {
            if (this.awaitingTransferredWorldState && this.tryConsumePendingTransferredWorldState()) {
                if (this.pendingLoadedMap) {
                    this.tryFinalizeMapSetup();
                }
            }
            this.handleOverlayUpdate();

            // Defer initialization to first update() call so overlay is visible first frame
            if (!this.initializationStarted) {
                this.drawLoadingOverlay(() => {
                    void this.runDeferredMapLoad();
                });
                return; // Return early to let overlay render
            }

            // Update player movement
            if (this.player) {
                // Process pending course corrections before player update to avoid snapping to blocked cell
                for (const correction of this.pendingCourseCorrections) {
                    this.player.adjustCourse(correction.curX, correction.curY, correction.destX, correction.destY);
                }
                this.pendingCourseCorrections = [];

                this.player.update(delta);
                this.handleLeftMouseButton();
                this.handleRightMouseButton();
                this.cameraManager?.update();
                this.handleMapObjectCollisions();
            }

            for (const [playerId, player] of this.playersById) {
                if (playerId === this.selfPlayerId) {
                    continue;
                }

                player.update(delta);
            }

            // Update monsters
            for (const monster of this.monsters) {
                monster.update(delta);
            }

            // Update weather (rain particles, sound)
            if (this.weatherManager && this.cameras?.main) {
                const cam = this.cameras.main;
                this.weatherManager.update(delta, cam.scrollX, cam.scrollY, cam.width, cam.height);
            }
        });
    }

    private handleOverlayUpdate(): void {
        if (this.loadingMap) {
            this.showNativeOverlayMapLoading();
        }
    }

    private drawLoadingOverlay(callback: () => void): void {
        this.initializationStarted = true;
        this.showNativeOverlayMapLoading();
        this.time.delayedCall(0, callback);
    }

    private showNativeOverlayMapLoading(): void {
        EventBus.emit(NATIVE_OVERLAY_MAP_LOADING_SHOWN, { text: 'Loading map...' });
        EventBus.emit(NATIVE_OVERLAY_HEALTH_BAR_HIDDEN);
    }

    private hideNativeOverlayMapLoading(): void {
        EventBus.emit(NATIVE_OVERLAY_MAP_LOADING_HIDDEN);
    }

    private hideLogoutCountdownOverlay(): void {
        this.logoutCountdownSeconds = undefined;
        EventBus.emit(NATIVE_OVERLAY_LOGOUT_COUNTDOWN_HIDDEN);
    }

    private showOrUpdateLogoutCountdownOverlay(secondsLeft: number): void {
        this.logoutCountdownSeconds = secondsLeft;
        EventBus.emit(NATIVE_OVERLAY_LOGOUT_COUNTDOWN_UPDATED, {
            text: `Logging out in ${secondsLeft} seconds.`,
        });
    }

    /**
     * When map assets load on demand, fetches the current `.amd` and tile packs before the normal minimap path.
     */
    private async runDeferredMapLoad(): Promise<void> {
        try {
            if (shouldLoadMapAssetsOnDemand()) {
                await prepareMapForGameWorld(this, this.mapManager!.getCurrentMapName());
            }
        } catch (error) {
            console.error('[GameWorld] Map on-demand load failed:', error);
            throw error;
        }

        runSafeSync('GameWorld:deferredMapLoad', () => {
            this.displayedMap = this.mapManager!.getCurrentMap();
            this.mapManager!.startMinimapCapture((map) => {
                runSafeSync('GameWorld:minimapCapture', () => {
                    map.renderMapObjects(this, true); // Third pass (with trees)
                    this.pendingLoadedMap = map;
                    this.tryFinalizeMapSetup();
                });
            });
        });
    }

    private handleLeftMouseButton(): void {
        if (this.awaitingMakeServerCellOccupiedClick || this.awaitingPlayerTeleportClick) {
            return;
        }
        const inputManager = this.inputManager;
        if (!inputManager) {
            return;
        }
        if (!inputManager.isLeftMouseDown()) {
            this.suppressLeftMouseMovementUntilRelease = false;
            return;
        }
        if (!this.loadingMap && inputManager.getActivePointer() && this.cameras?.main && this.player) {
            if (this.suppressLeftMouseMovementUntilRelease) {
                return;
            }
            if (this.player.isCasting()) {
                return;
            }

            const pointer = inputManager.getActivePointer()!;

            if (this.player.hasPendingSpell()) {
                const camera = this.cameras.main;
                const cursorPixelX = pointer.x + camera.scrollX;
                const cursorPixelY = pointer.y + camera.scrollY;
                if (this.player.onLeftClickAt(cursorPixelX, cursorPixelY)) {
                    this.suppressLeftMouseMovementUntilRelease = true;
                    return;
                }
            }

            // Pending effect summon: create effect at cursor position (overrides movement)
            if (this.castManager?.getPendingEffectKey()) {
                const camera = this.cameras.main;
                const worldPixelX = pointer.x + camera.scrollX;
                const worldPixelY = pointer.y + camera.scrollY;
                const worldX = convertPixelPosToWorldPos(worldPixelX);
                const worldY = convertPixelPosToWorldPos(worldPixelY);
                if (this.castManager.tryPlaceEffect(worldX, worldY)) {
                    return;
                }
            }

            const attackTarget = this.getAttackableTargetUnderPointer(pointer);

            // Skip attack and movement when we just placed an effect (castReady)
            if (this.castManager?.getCastReady()) {
                return;
            }

            // When holding over a combat target: attack when in range.
            if (attackTarget) {
                this.player.attack(attackTarget);
                if (this.player.isAttacking() || this.player.isInBowStance()) {
                    return;
                }
                // Out of range: pathfind to the target's position (player will attack when reaching adjacent cell).
                if (getDistance(
                    this.player.getWorldX(),
                    this.player.getWorldY(),
                    attackTarget.getWorldX(),
                    attackTarget.getWorldY()
                ) > this.player.getAttackRange()) {
                    this.player.setDestination(attackTarget.getWorldX(), attackTarget.getWorldY(), false);
                }
                return;
            }

            // Not over monster: move towards cursor (throttled)
            if (inputManager.canAcceptMovementCommand()) {
                const camera = this.cameras.main;
                const worldPixelX = pointer.x + camera.scrollX;
                const worldPixelY = pointer.y + camera.scrollY;
                // Use player's anchor point (where they appear on screen) as center for direction calculation
                const playerAnchorPixelX = this.player.getAnimatedPixelX();
                const playerAnchorPixelY = this.player.getAnimatedPixelY();

                const commandedDestX = convertPixelPosToWorldPos(worldPixelX);
                const commandedDestY = convertPixelPosToWorldPos(worldPixelY);

                this.player.setDestination(
                    commandedDestX,
                    commandedDestY,
                    true,
                    playerAnchorPixelX,
                    playerAnchorPixelY,
                    worldPixelX,
                    worldPixelY
                );
                inputManager.recordMovementCommand();
            }
        }
    }

    private handleRightMouseButton(): void {
        const inputManager = this.inputManager;
        if (!this.loadingMap && inputManager?.isRightMouseDown() && inputManager.getActivePointer() && this.cameras?.main && this.player) {
            if (this.player.hasPendingSpell()) {
                return;
            }
            const pointer = inputManager.getActivePointer()!;
            const camera = this.cameras.main;
            const worldPixelX = pointer.x + camera.scrollX;
            const worldPixelY = pointer.y + camera.scrollY;
            const direction = getNextDirection(
                this.player.getWorldX(),
                this.player.getWorldY(),
                convertPixelPosToWorldPos(worldPixelX),
                convertPixelPosToWorldPos(worldPixelY)
            );
            if (direction === Direction.None) {
                return;
            }
            if (!this.player.isMoving()) {
                if (this.player.turnTowardsDirection(direction)) {
                    getNetworkManager(this.game)?.requestChangePlayerIdleDirection(direction);
                }
            } else {
                this.player.queueLocalIdleDirectionForWhenStopped(direction);
            }
        }
    }

    private getCurrentMap(): HBMap {
        return this.mapManager!.getCurrentMap();
    }

    /**
     * Re-sets HBMap occupancy for living monsters and all players (self + others). Player reset/course correction
     * can clear a tile another actor still uses (single boolean per cell).
     */
    private reapplyTileOccupancyOnMap(): void {
        if (!this.mapManager || this.loadingMap) {
            return;
        }
        const map = this.getCurrentMap();
        for (const monster of this.monsters) {
            if (!monster.isDead()) {
                map.setTileOccupied(monster.getWorldX(), monster.getWorldY(), true);
            }
        }
        for (const p of this.playersById.values()) {
            if (!p.isDead()) {
                map.setTileOccupied(p.getWorldX(), p.getWorldY(), true);
            }
        }
    }

    private getOtherPlayerUnderPointer(pointer: Phaser.Input.Pointer): Player | undefined {
        if (!this.cameras?.main) {
            return undefined;
        }
        const camera = this.cameras.main;
        return getOtherPlayerUnderWorldPixel(
            this.player,
            this.playersById,
            pointer.x + camera.scrollX,
            pointer.y + camera.scrollY,
        );
    }

    /** Other player under cursor for attacks/spells only; spawn-protected and invisible remotes are not valid targets. */
    private getAttackableOtherPlayerUnderPointer(pointer: Phaser.Input.Pointer): Player | undefined {
        const p = this.getOtherPlayerUnderPointer(pointer);
        if (!p || p.hasSpawnProtection() || p.hasInvisibilityBuff()) {
            return undefined;
        }
        return p;
    }

    /** Same as {@link getAttackableOtherPlayerUnderPointer} in world pixel space (non-buff aim assist). */
    private getAttackableOtherPlayerUnderWorldPixel(worldPixelX: number, worldPixelY: number): Player | undefined {
        const p = getOtherPlayerUnderWorldPixel(this.player, this.playersById, worldPixelX, worldPixelY);
        if (!p || p.hasSpawnProtection() || p.hasInvisibilityBuff()) {
            return undefined;
        }
        return p;
    }

    private filterMonsterForSpellAimAssist(spellId: number, m: Monster | undefined): Monster | undefined {
        if (!m || m.isDead()) {
            return undefined;
        }
        const spell = getNetworkManager(this.game)?.getSpellById(spellId);
        const isBuffSpell = spell?.damageType === undefined && (spell?.temporaryEffects?.length ?? 0) > 0;
        if (!m.hasInvisibilityBuff()) {
            return m;
        }
        if (isBuffSpell && m.getAllegiance() === MonsterAllegiance.Friendly) {
            return m;
        }
        return undefined;
    }

    private getPlayerUnderWorldPixelForSpellAimAssist(spellId: number, worldPixelX: number, worldPixelY: number): Player | undefined {
        const spell = getNetworkManager(this.game)?.getSpellById(spellId);
        const isBuffSpell = spell?.damageType === undefined && (spell?.temporaryEffects?.length ?? 0) > 0;
        if (isBuffSpell) {
            const p = getPlayerUnderWorldPixelForHover(this.playersById, worldPixelX, worldPixelY);
            if (!p || p.isDead()) {
                return undefined;
            }
            if (p !== this.player && p.hasSpawnProtection()) {
                return undefined;
            }
            if (p !== this.player && p.hasInvisibilityBuff()) {
                return undefined;
            }
            return p;
        }
        return this.getAttackableOtherPlayerUnderWorldPixel(worldPixelX, worldPixelY);
    }

    /**
     * When the cursor is over another player or a living monster, returns ids for spell aim assist (top-most by depth).
     */
    private getSpellAimAssistTargetIds(spellId: number, worldPixelX: number, worldPixelY: number): { playerId?: bigint; monsterId?: bigint } {
        const hoveredMonster = this.filterMonsterForSpellAimAssist(
            spellId,
            getMonsterUnderWorldPixel(this.monsters, worldPixelX, worldPixelY),
        );
        const liveMonster = hoveredMonster && !hoveredMonster.isDead() ? hoveredMonster : undefined;
        const hoveredPlayer = this.getPlayerUnderWorldPixelForSpellAimAssist(spellId, worldPixelX, worldPixelY);
        if (!liveMonster) {
            const pid = hoveredPlayer?.getPlayerId();
            if (pid) {
                return { playerId: BigInt(pid) };
            }
            return {};
        }
        if (!hoveredPlayer) {
            return { monsterId: BigInt(liveMonster.getMonsterId()) };
        }
        if (hoveredPlayer.getDepth() > liveMonster.getDepth()) {
            const pid = hoveredPlayer.getPlayerId();
            if (pid) {
                return { playerId: BigInt(pid) };
            }
            return {};
        }
        return { monsterId: BigInt(liveMonster.getMonsterId()) };
    }

    /** Invisible monsters are not valid melee/ranged targets (server also rejects); hover uses {@link getMonsterUnderWorldPixelForHoverUi}. */
    private getMonsterUnderWorldPixelForCombatTargeting(worldPixelX: number, worldPixelY: number): Monster | undefined {
        const m = getMonsterUnderWorldPixel(this.monsters, worldPixelX, worldPixelY);
        if (!m || m.isDead()) {
            return undefined;
        }
        if (m.hasInvisibilityBuff()) {
            return undefined;
        }
        return m;
    }

    private getMonsterUnderPointerForCombatTargeting(pointer: Phaser.Input.Pointer): Monster | undefined {
        if (!this.cameras?.main) {
            return undefined;
        }
        const camera = this.cameras.main;
        return this.getMonsterUnderWorldPixelForCombatTargeting(pointer.x + camera.scrollX, pointer.y + camera.scrollY);
    }

    private getAttackableTargetUnderPointer(pointer: Phaser.Input.Pointer): Monster | Player | undefined {
        const hoveredMonster = this.getMonsterUnderPointerForCombatTargeting(pointer);
        const liveMonster = hoveredMonster && !hoveredMonster.isDead() ? hoveredMonster : undefined;
        const hoveredPlayer = this.getAttackableOtherPlayerUnderPointer(pointer);
        if (!liveMonster) {
            return hoveredPlayer;
        }
        if (!hoveredPlayer) {
            return liveMonster;
        }
        return hoveredPlayer.getDepth() > liveMonster.getDepth() ? hoveredPlayer : liveMonster;
    }

    private handleNpcEnteredRange(entry: NpcEnteredRangeEventData): void {
        if (this.npcs.some((n) => n.getNPCId() === entry.npcId)) {
            return;
        }
        const sprite = getSpriteForCatalogNpcId(entry.catalogNpcId);
        if (!sprite) {
            console.warn(
                `[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Unknown NPC catalog id ${entry.catalogNpcId} for instance ${entry.npcId}`,
            );
            return;
        }
        const map = this.getCurrentMap();
        const npc = new NPC(this, {
            x: entry.x,
            y: entry.y,
            spriteName: sprite,
            displayName: entry.displayName,
            direction: entry.direction,
            soundManager: this.soundManager,
            map,
            npcId: entry.npcId,
        });
        this.npcs.push(npc);
    }

    private handleNpcsLeftRange(npcIds: string[]): void {
        for (const id of npcIds) {
            const idx = this.npcs.findIndex((n) => n.getNPCId() === id);
            if (idx === -1) {
                continue;
            }
            const npc = this.npcs[idx];
            npc.destroy();
            this.npcs.splice(idx, 1);
        }
    }

    private handleMonsterEnteredRange(data: MonsterEnteredRangeEventData): void {
        if (this.monsters.some((m) => m.getMonsterId() === data.monsterId)) {
            return;
        }
        if (!this.mapManager || this.loadingMap || !this.soundManager) {
            return;
        }
        if (!this.player) {
            console.warn(
                `[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Ignoring monster enter before player exists (id=${data.monsterId})`);
            return;
        }

        const monsterTemplate = getMonsterData(data.sprite);
        if (!monsterTemplate) {
            console.warn(
                `[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Unknown monster sprite '${data.sprite}' from server (id=${data.monsterId})`);
            return;
        }

        if (
            data.state !== MonsterEntityState.MONSTER_ENTITY_STATE_IDLE &&
            data.state !== MonsterEntityState.MONSTER_ENTITY_STATE_MOVE &&
            data.state !== MonsterEntityState.MONSTER_ENTITY_STATE_ATTACK
        ) {
            console.warn(
                `[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Ignoring monster enter: unsupported entity state ${data.state} (id=${data.monsterId})`);
            return;
        }

        const map = this.getCurrentMap();
        const facing = toDirection(data.direction);
        if (facing === Direction.None) {
            console.warn(
                `[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Ignoring monster enter: invalid direction ${data.direction} (id=${data.monsterId})`);
            return;
        }
        if (data.attackType < 0 || data.attackType > 3) {
            console.warn(
                `[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Ignoring monster enter: invalid attack_type ${data.attackType} (id=${data.monsterId})`);
            return;
        }
        if (data.allegiance < MonsterAllegiance.Hostile || data.allegiance > MonsterAllegiance.Friendly) {
            console.warn(
                `[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Ignoring monster enter: invalid allegiance ${data.allegiance} (id=${data.monsterId})`);
            return;
        }
        const lazyMonsterAssets = shouldLoadMonsterAssetsOnDemand();
        const concreteAssetsReady = !lazyMonsterAssets || areMonsterAssetsLoaded(this, data.sprite);
        const visualSpriteName = concreteAssetsReady ? data.sprite : MONSTER_PLACEHOLDER_SPRITE;
        const visualTemplate = concreteAssetsReady ? monsterTemplate : getMonsterData(MONSTER_PLACEHOLDER_SPRITE);
        if (!visualTemplate) {
            console.warn(
                `[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Missing placeholder monster sprite '${MONSTER_PLACEHOLDER_SPRITE}'`);
            return;
        }

        try {
            const monster = new Monster(this, {
                x: data.x,
                y: data.y,
                spriteName: visualSpriteName,
                displayName: data.name,
                direction: facing,
                soundManager: this.soundManager,
                map,
                states: visualTemplate.states,
                movementSpeedMs: data.movementSpeedMs,
                attackSpeedMs: data.attackSpeedMs,
                playerX: this.player.getWorldX(),
                playerY: this.player.getWorldY(),
                attackType: data.attackType as AttackType,
                allegiance: data.allegiance as MonsterAllegiance,
                hp: data.hp,
                maxHp: data.maxHp,
                attackDamage: data.attackDamage,
                monsterId: data.monsterId,
                temporalCoefficient: monsterTemplate.temporalCoefficient,
                shadow: visualTemplate.shadow,
                opacity: monsterTemplate.opacity,
                height: monsterTemplate.height,
                dead: data.dead,
                state: data.state,
            });
            monster.setRemoteIdleContinuationGraceMs(serverDialogStore.state.gracePeriod);
            monster.syncActiveTemporaryEffects(data.activeTemporaryEffects ?? []);
            if (this.player && this.player.getAttackTarget() === monster && monster.hasInvisibilityBuff()) {
                this.player.clearAttackTarget();
            }
            this.monsters.push(monster);

            if (!concreteAssetsReady) {
                loadMonsterAssetsOnDemand(this, data.sprite)
                    .then(() => {
                        const currentMonster = this.monsters.find((entry) => entry.getMonsterId() === data.monsterId);
                        if (!currentMonster) {
                            return;
                        }
                        currentMonster.applyLoadedMonsterAssets({
                            spriteName: data.sprite,
                            states: monsterTemplate.states,
                            shadow: monsterTemplate.shadow,
                            height: monsterTemplate.height,
                        });
                    })
                    .catch((error) => {
                        console.error(
                            `[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Failed to lazy-load monster assets for '${data.sprite}' (id=${data.monsterId})`,
                            error,
                        );
                    });
            }

        } catch (error) {
            console.error(
                `[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Failed to spawn monster '${data.sprite}' (id=${data.monsterId})`,
                error,
            );
        }
    }

    private handleTemporaryEffectAppliedForPlayer(data: TemporaryEffectPlayerEventData): void {
        if (!this.mapManager || this.loadingMap) {
            return;
        }
        const p = this.playersById.get(data.playerId);
        if (!p) {
            return;
        }
        p.applyTemporaryEffect(data.temporaryEffectType);
        p.applySpeedsMs({
            movementSpeedMs: data.movementSpeedMs,
            attackSpeedMs: data.attackSpeedMs,
            castSpeedMs: data.castSpeedMs,
        });
        if (data.temporaryEffectType === TemporaryEffectType.Invisibility && this.player?.getAttackTarget() === p) {
            this.player.clearAttackTarget();
        }
    }

    private handleTemporaryEffectExpiredForPlayer(data: TemporaryEffectPlayerEventData): void {
        if (!this.mapManager || this.loadingMap) {
            return;
        }
        const p = this.playersById.get(data.playerId);
        if (!p) {
            return;
        }
        p.removeTemporaryEffect(data.temporaryEffectType);
        p.applySpeedsMs({
            movementSpeedMs: data.movementSpeedMs,
            attackSpeedMs: data.attackSpeedMs,
            castSpeedMs: data.castSpeedMs,
        });
    }

    private handleTemporaryEffectAppliedForMonster(data: TemporaryEffectMonsterEventData): void {
        if (!this.mapManager || this.loadingMap) {
            return;
        }
        const m = this.monsters.find((x) => x.getMonsterId() === data.monsterId);
        if (!m) {
            return;
        }
        m.applyTemporaryEffect(data.temporaryEffectType);
        m.applySpeedsMs(data.movementSpeedMs, data.attackSpeedMs);
        if (data.temporaryEffectType === TemporaryEffectType.Invisibility && this.player?.getAttackTarget() === m) {
            this.player.clearAttackTarget();
        }
    }

    private handleTemporaryEffectExpiredForMonster(data: TemporaryEffectMonsterEventData): void {
        if (!this.mapManager || this.loadingMap) {
            return;
        }
        const m = this.monsters.find((x) => x.getMonsterId() === data.monsterId);
        if (!m) {
            return;
        }
        m.removeTemporaryEffect(data.temporaryEffectType);
        m.applySpeedsMs(data.movementSpeedMs, data.attackSpeedMs);
    }

    private handleCastEffectAtCell(data: { effectKey: string; x: number; y: number }): void {
        if (!this.mapManager || this.loadingMap || !this.soundManager) {
            return;
        }
        const options: DrawEffectOptions = { soundManager: this.soundManager };
        if (this.player) {
            options.playerWorldX = this.player.getWorldX();
            options.playerWorldY = this.player.getWorldY();
        }
        drawEffect(this, data.x, data.y, data.effectKey, options);
    }

    private handleMonstersLeftRange(monsterIds: string[]): void {
        for (const monsterId of monsterIds) {
            const monsterIndex = this.monsters.findIndex((m) => m.getMonsterId() === monsterId);
            if (monsterIndex === -1) {
                continue;
            }
            const monster = this.monsters[monsterIndex];
            if (this.player && this.player.getAttackTarget() === monster) {
                this.player.clearAttackTarget();
            }
            if (monster.isDead()) {
                monster.beginRemovalFade();
            } else {
                monster.destroy();
                this.monsters.splice(monsterIndex, 1);
            }
        }
    }

    private handleGroundStatesEnteredRange(states: GroundStateCellEventData[]): void {
        if (!this.mapManager || this.loadingMap) {
            return;
        }

        for (const state of states) {
            for (const effect of state.effects) {
                this.upsertGroundEffectVisual(state.x, state.y, effect.groundEffectId, effect.effectType);
            }
            if (state.groundItem) {
                this.upsertGroundItemVisual(state.x, state.y, state.groundItem.itemId, state.groundItem.itemUid, state.groundItem.quantity, state.groundItem.effectOverrides);
            }
        }
    }

    private handleGroundStatesLeftRange(states: GroundStateCellRemovedEventData[]): void {
        for (const state of states) {
            for (const groundEffectId of state.groundEffectIds) {
                this.removeGroundEffectVisual(groundEffectId);
            }
            if (state.groundItemUid) {
                this.removeGroundItemVisualByUid(state.groundItemUid);
            }
        }
    }

    private upsertGroundItemVisual(
        worldX: number,
        worldY: number,
        itemId: number,
        itemUid: string,
        quantity: number,
        effectOverrides?: Effect[],
    ): void {
        this.removeGroundItemVisualAtCell(worldX, worldY);

        const playerGender = playerDialogStore.state.gender;
        try {
            const groundItem = new GroundItem(
                this,
                worldX,
                worldY,
                itemId,
                itemUid,
                quantity,
                playerGender,
                undefined,
                effectOverrides,
            );
            groundItem.setDepth(worldY * DEPTH_MULTIPLIER - DEPTH_MULTIPLIER / 2);
            this.groundItems.push(groundItem);
        } catch (error) {
            console.warn(`[GameWorld${this.gameWorldId ? `:${this.gameWorldId}` : ''}] Failed to create GroundItem:`, error);
        }
    }

    private removeGroundItemVisualAtCell(worldX: number, worldY: number): void {
        const existingIndex = this.groundItems.findIndex((groundItem) => groundItem.worldX === worldX && groundItem.worldY === worldY);
        if (existingIndex < 0) {
            return;
        }

        const [removed] = this.groundItems.splice(existingIndex, 1);
        removed.destroy();
    }

    private removeGroundItemVisualByUid(itemUid: string): void {
        const existingIndex = this.groundItems.findIndex((groundItem) => groundItem.itemUid === itemUid);
        if (existingIndex < 0) {
            return;
        }

        const [removed] = this.groundItems.splice(existingIndex, 1);
        removed.destroy();
    }

    private upsertGroundEffectVisual(
        worldX: number,
        worldY: number,
        groundEffectId: string,
        effectType: GroundEffectType
    ): void {
        this.removeGroundEffectVisual(groundEffectId);

        const pixelX = worldCellCenterPixelX(worldX);
        const pixelY = worldCellCenterPixelY(worldY);
        switch (effectType) {
            case GroundEffectType.GROUND_EFFECT_TYPE_FIRE:
                this.groundEffectsById.set(
                    groundEffectId,
                    new FireInstance(this, pixelX, pixelY)
                );
                break;
            case GroundEffectType.GROUND_EFFECT_TYPE_POISON:
                this.groundEffectsById.set(
                    groundEffectId,
                    new PoisonCloudInstance(this, pixelX, pixelY)
                );
                break;
            case GroundEffectType.GROUND_EFFECT_TYPE_SPIKE_FIELD:
                this.groundEffectsById.set(
                    groundEffectId,
                    createSpikeField(this, pixelX, pixelY)
                );
                break;
            case GroundEffectType.GROUND_EFFECT_TYPE_ICE_STORM:
                this.groundEffectsById.set(groundEffectId, new IceStorm(this, pixelX, pixelY, {}));
                break;
            default:
                console.warn('[GameWorld] Unsupported ground effect type from server.', { effectType, groundEffectId, worldX, worldY });
                break;
        }
    }

    private removeGroundEffectVisual(groundEffectId: string): void {
        const existing = this.groundEffectsById.get(groundEffectId);
        if (!existing) {
            return;
        }

        existing.destroy();
        this.groundEffectsById.delete(groundEffectId);
    }

    private handleMonsterMoved(data: MonsterMovedEventData): void {
        if (!this.mapManager || this.loadingMap) {
            return;
        }
        const monster = this.monsters.find((m) => m.getMonsterId() === data.monsterId);
        if (!monster) {
            return;
        }
        if (monster.isDead()) {
            return;
        }
        monster.startMovement(data.curX, data.curY, data.destX, data.destY, data.movementSpeedMs, data.direction);
    }

    private handleMonsterAttacked(data: MonsterAttackedEventData): void {
        this.playMonsterAttack(data, () => this.resolvePlayerProjectileTarget(data.targetPlayerId));
    }

    private handleMonsterAttackedMonster(data: MonsterAttackedMonsterEventData): void {
        this.playMonsterAttack(data, () => this.resolveMonsterById(data.targetMonsterId) ?? null);
    }

    private handlePlayerReceiveDamage(data: PlayerReceiveDamageEventData): void {
        const monster = this.monsters.find((m) => m.getMonsterId() === data.monsterId);
        if (monster) {
            monster.playAttackImpactSound();
        }

        const target = this.resolvePlayerById(data.playerId);
        if (!target) {
            return;
        }
        target.applyMonsterDamage(
            data.damage,
            data.attackType,
            data.stunDurationMs,
            data.knockbackDurationMs,
            data.destX,
            data.destY,
            data.knockbackFromX,
            data.knockbackFromY,
        );
    }

    private handlePlayerTakeDamage(data: PlayerTakeDamageEventData): void {
        const target = this.resolvePlayerById(data.targetPlayerId);
        if (!target) {
            return;
        }
        target.applyMonsterDamage(
            data.damage,
            data.attackType,
            data.stunDurationMs,
            data.knockbackDurationMs,
            data.destX,
            data.destY,
            data.knockbackFromX,
            data.knockbackFromY,
        );
    }

    private handleMonsterTakeDamage(data: MonsterTakeDamageEventData): void {
        this.applyMonsterDamage(data.monsterId, data);
    }

    private handleMonsterTakeDamageByMonster(data: MonsterTakeDamageByMonsterEventData): void {
        const attacker = this.resolveMonsterById(data.attackerMonsterId);
        if (attacker) {
            attacker.playAttackImpactSound();
        }

        this.applyMonsterDamage(data.targetMonsterId, data);
    }

    private playMonsterAttack(
        data: { monsterId: string; direction: number; attackSpeedMs: number; rangedAttack: boolean; worldX: number; worldY: number },
        resolveTarget: () => Player | Monster | null,
    ): void {
        if (!this.mapManager || this.loadingMap) {
            return;
        }

        const monster = this.resolveMonsterById(data.monsterId);
        if (!monster || monster.isDead()) {
            return;
        }

        monster.startAttackAnimation(data.direction, data.attackSpeedMs, data.worldX, data.worldY);
        if (!data.rangedAttack) {
            return;
        }

        const initialTarget = resolveTarget();
        if (!initialTarget || !isProjectileTarget(initialTarget)) {
            return;
        }

        const halfMs = data.attackSpeedMs / 2;
        const monsterId = data.monsterId;
        const arrowSpeed = this.arrowSpeedPxPerSec;
        this.time.delayedCall(halfMs, () => {
            const target = resolveTarget();
            const attacker = this.resolveMonsterById(monsterId);
            if (!target || !attacker || !isProjectileTarget(target)) {
                return;
            }

            new ArrowProjectile(this, {
                originPixelX: attacker.getAnimatedPixelX(),
                originPixelY: attacker.getAnimatedPixelY(),
                target,
                speed: arrowSpeed,
            });
        });
    }

    private applyMonsterDamage(
        monsterId: string,
        data: {
            damage: number;
            attackType: number;
            stunlockDurationMs: number;
            hp: number;
            knockbackDurationMs?: number;
            destX?: number;
            destY?: number;
            knockbackFromX?: number;
            knockbackFromY?: number;
        },
    ): void {
        if (!this.mapManager || this.loadingMap) {
            return;
        }

        const monster = this.resolveMonsterById(monsterId);
        if (!monster) {
            return;
        }

        monster.takeDamage(
            data.damage,
            data.attackType,
            data.stunlockDurationMs,
            data.knockbackDurationMs,
            data.destX,
            data.destY,
            data.knockbackFromX,
            data.knockbackFromY,
            data.hp,
        );
    }

    private resolveMonsterById(monsterId: string): Monster | undefined {
        return this.monsters.find((monster) => monster.getMonsterId() === monsterId);
    }

    private resolvePlayerProjectileTarget(playerId: string): Player | null {
        const isSelfTarget = playerId === this.selfPlayerId || playerId === this.initialGameWorldState?.playerId;
        if (isSelfTarget) {
            return this.player ?? null;
        }

        return this.playersById.get(playerId) ?? null;
    }

    private handleMonsterDied(data: MonsterDiedEventData): void {
        if (!this.mapManager || this.loadingMap) {
            return;
        }
        const monster = this.monsters.find((m) => m.getMonsterId() === data.monsterId);
        if (!monster) {
            return;
        }
        monster.applyDeath();
    }

    private handlePlayerEnteredRange(data: NetworkPlayer): void {
        if (data.playerId === this.selfPlayerId || this.playersById.has(data.playerId)) {
            return;
        }
        if (!this.player || !this.mapManager || this.loadingMap) {
            return;
        }

        const movementSpeedMs = data.movementSpeedMs;
        const runningMode = data.runningMode;
        const otherPlayer = this.createOtherPlayer(
            data.playerId,
            data.x,
            data.y,
            movementSpeedMs,
            runningMode,
            data.attackMode,
            data.disconnected,
            data.dead,
            data.direction,
            data.visibleEquippedItems,
            {
                gender: data.gender,
                skinColor: data.skinColor,
                underwearColorIndex: data.underwearColorIndex,
                hairStyleIndex: data.hairStyleIndex,
            },
            data.characterName,
        );
        otherPlayer.setFactionId(data.factionId);
        if (data.spawnProtection) {
            otherPlayer.setSpawnProtectionEffect(true);
        }
        otherPlayer.syncActiveTemporaryEffects(data.activeTemporaryEffects ?? []);
        otherPlayer.applySpeedsMs({
            attackSpeedMs: data.attackSpeedMs ?? 600,
            castSpeedMs: data.castSpeedMs ?? 1200,
        });
        if (this.player && this.player.getAttackTarget() === otherPlayer && otherPlayer.hasInvisibilityBuff()) {
            this.player.clearAttackTarget();
        }
        this.playersById.set(data.playerId, otherPlayer);
    }

    private handleRemotePlayerItemEquipped(data: ItemEquippedEventData): void {
        if (data.playerId === this.selfPlayerId) {
            return;
        }

        const otherPlayer = this.playersById.get(data.playerId);
        if (!otherPlayer) {
            return;
        }
        if (!Object.values(ItemTypes).includes(data.slot as ItemTypes)) {
            return;
        }

        otherPlayer.setRemoteVisibleEquippedItem(data.slot as ItemTypes, data.item.itemId, data.item.effectOverrides);
    }

    private handleRemotePlayerItemUnequipped(data: ItemUnequippedEventData): void {
        if (data.playerId === this.selfPlayerId) {
            return;
        }

        const otherPlayer = this.playersById.get(data.playerId);
        if (!otherPlayer) {
            return;
        }
        if (!Object.values(ItemTypes).includes(data.slot as ItemTypes)) {
            return;
        }

        otherPlayer.setRemoteVisibleEquippedItem(data.slot as ItemTypes, undefined);
    }

    private handlePlayerMoved(data: PlayerMovedEventData): void {
        if (data.playerId === this.selfPlayerId || !this.player || !this.mapManager || this.loadingMap) {
            return;
        }

        const movementSpeedMs = data.movementSpeedMs;
        const runningMode = data.runningMode;
        let otherPlayer = this.playersById.get(data.playerId);
        const spawnX = data.teleport ? data.destX : data.curX;
        const spawnY = data.teleport ? data.destY : data.curY;
        if (!otherPlayer) {
            const snap = getNetworkManager(this.game)?.getOtherPlayersState().find((player) => player.playerId === data.playerId);
            const visibleEquippedItems = snap?.visibleEquippedItems ?? {};
            const appearance = snap
                ? {
                    gender: snap.gender,
                    skinColor: snap.skinColor,
                    underwearColorIndex: snap.underwearColorIndex,
                    hairStyleIndex: snap.hairStyleIndex,
                }
                : {
                    gender: Gender.MALE,
                    skinColor: SkinColor.Light,
                    underwearColorIndex: 0,
                    hairStyleIndex: 0,
                };
            otherPlayer = this.createOtherPlayer(
                data.playerId,
                spawnX,
                spawnY,
                movementSpeedMs,
                runningMode,
                data.attackMode,
                false,
                false,
                Direction.NorthEast,
                visibleEquippedItems,
                appearance,
                snap?.characterName ?? '',
            );
            otherPlayer.syncActiveTemporaryEffects(snap?.activeTemporaryEffects ?? []);
            otherPlayer.applySpeedsMs({
                attackSpeedMs: snap?.attackSpeedMs ?? 600,
                castSpeedMs: snap?.castSpeedMs ?? 1200,
            });
            if (this.player && this.player.getAttackTarget() === otherPlayer && otherPlayer.hasInvisibilityBuff()) {
                this.player.clearAttackTarget();
            }
            this.playersById.set(data.playerId, otherPlayer);
        } else {
            otherPlayer.setRunModeAndMovementSpeed(runningMode, movementSpeedMs);
            otherPlayer.setAttackMode(data.attackMode);
        }

        if (data.teleport) {
            otherPlayer.snapRemoteToAuthoritativeCell(data.destX, data.destY);
        } else {
            otherPlayer.startMovementStep(data.curX, data.curY, data.destX, data.destY, data.dashAttack);
        }
    }

    private handlePlayerAttackedMonster(data: PlayerAttackedMonsterEventData): void {
        if (data.playerId === this.selfPlayerId || !this.player || !this.mapManager || this.loadingMap) {
            return;
        }

        const otherPlayer = this.playersById.get(data.playerId);
        if (!otherPlayer) {
            return;
        }

        const monster = data.monsterId !== '0' ? this.monsters.find((m) => m.getMonsterId() === data.monsterId) : undefined;
        otherPlayer.playRemoteAttack(this.arrowSpeedPxPerSec, {
            direction: data.direction,
            attackSpeedMs: data.attackSpeedMs,
            ranged: data.rangedAttack,
            target: monster,
            worldX: data.worldX,
            worldY: data.worldY,
            attackType: data.attackType,
        });
    }

    private handlePlayerAttackedPlayer(data: PlayerAttackedPlayerEventData): void {
        if (data.playerId === this.selfPlayerId || !this.player || !this.mapManager || this.loadingMap) {
            return;
        }

        const otherPlayer = this.playersById.get(data.playerId);
        const targetPlayer = this.resolvePlayerById(data.targetPlayerId);
        if (!otherPlayer || !targetPlayer) {
            return;
        }

        otherPlayer.playRemoteAttack(this.arrowSpeedPxPerSec, {
            direction: data.direction,
            attackSpeedMs: data.attackSpeedMs,
            ranged: data.rangedAttack,
            target: targetPlayer,
            worldX: data.worldX,
            worldY: data.worldY,
            attackType: data.attackType,
        });
    }

    private handlePlayerPickupPerformed(data: PlayerPickupPerformedEventData): void {
        if (data.playerId === this.selfPlayerId || !this.player || !this.mapManager || this.loadingMap) {
            return;
        }

        const otherPlayer = this.playersById.get(data.playerId);
        if (!otherPlayer) {
            return;
        }

        otherPlayer.queueRemotePickup(data.direction, data.animationTimeMs);
    }

    private handlePlayerBowStancePerformed(data: PlayerBowStancePerformedEventData): void {
        if (data.playerId === this.selfPlayerId || !this.player || !this.mapManager || this.loadingMap) {
            return;
        }

        const otherPlayer = this.playersById.get(data.playerId);
        if (!otherPlayer) {
            return;
        }

        otherPlayer.queueRemoteBowStance(data.direction, data.animationTimeMs);
    }

    private handleSpellCastStarted(data: SpellCastStartedEventData): void {
        if (data.playerId === this.selfPlayerId || !this.player || !this.mapManager || this.loadingMap) {
            return;
        }

        const otherPlayer = this.playersById.get(data.playerId);
        if (!otherPlayer) {
            return;
        }

        otherPlayer.queueRemoteSpellCastStart(data.spellName, data.castSpeedMs);
    }

    private handleSpellCastCancelled(data: SpellCastCancelledEventData): void {
        if (data.playerId === this.selfPlayerId || !this.player || !this.mapManager || this.loadingMap) {
            return;
        }

        const otherPlayer = this.playersById.get(data.playerId);
        if (!otherPlayer) {
            return;
        }

        otherPlayer.clearRemoteSpellCast();
    }

    private handleCastAoeSpell(data: CastAoeSpellEventData): void {
        if (!this.player || !this.mapManager || this.loadingMap || !this.castManager) {
            return;
        }

        const caster = this.resolvePlayerById(data.playerId);
        if (!caster) {
            return;
        }

        if (data.playerId !== this.selfPlayerId) {
            caster.clearRemoteSpellCast();
        }

        this.castManager.dispatchNetworkPlayerAoeSpell(caster, data);
    }

    private handleCastDirectionalAoeSpell(data: CastDirectionalAoeSpellEventData): void {
        if (!this.player || !this.mapManager || this.loadingMap || !this.castManager) {
            return;
        }

        const caster = this.resolvePlayerById(data.playerId);
        if (!caster) {
            return;
        }

        if (data.playerId !== this.selfPlayerId) {
            caster.clearRemoteSpellCast();
        }

        this.castManager.dispatchNetworkPlayerDirectionalAoeSpell(data);
    }

    private handleMonsterCastAoeSpell(data: MonsterCastAoeSpellEventData): void {
        if (!this.player || !this.mapManager || this.loadingMap || !this.castManager) {
            return;
        }

        const monster = this.resolveMonsterById(data.monsterId);
        if (!monster || monster.isDead()) {
            return;
        }

        this.castManager.dispatchNetworkMonsterAoeSpell(monster, data);
    }

    private handleMonsterCastDirectionalAoeSpell(data: MonsterCastDirectionalAoeSpellEventData): void {
        if (!this.player || !this.mapManager || this.loadingMap || !this.castManager) {
            return;
        }

        const monster = this.resolveMonsterById(data.monsterId);
        if (!monster || monster.isDead()) {
            return;
        }

        this.castManager.dispatchNetworkMonsterDirectionalAoeSpell(data);
    }

    private handlePlayerLeftRange(data: PlayerLeftEventData): void {
        if (data.playerId === this.selfPlayerId) {
            return;
        }

        const player = this.playersById.get(data.playerId);
        if (!player) {
            return;
        }

        if (this.player && this.player.getAttackTarget() === player) {
            this.player.clearAttackTarget();
        }
        player.destroy();
        this.playersById.delete(data.playerId);
    }

    private handlePlayerMovementStateChanged(data: PlayerMovementStateChangedEventData): void {
        if (!this.mapManager || this.loadingMap) {
            return;
        }
        if (data.playerId === this.selfPlayerId) {
            if (!this.player) {
                return;
            }
            this.player.setRunModeAndMovementSpeed(data.runningMode, data.movementSpeedMs);
            return;
        }

        const otherPlayer = this.playersById.get(data.playerId);
        if (!otherPlayer) {
            return;
        }

        otherPlayer.setRunModeAndMovementSpeed(data.runningMode, data.movementSpeedMs);
    }

    private handlePlayerAttackModeChanged(data: PlayerAttackModeChangedEventData): void {
        if (data.playerId === this.selfPlayerId || !this.player || !this.mapManager || this.loadingMap) {
            return;
        }

        const otherPlayer = this.playersById.get(data.playerId);
        if (!otherPlayer) {
            return;
        }

        otherPlayer.setAttackMode(data.attackMode);
    }

    private handlePlayerIdleDirectionChanged(data: PlayerIdleDirectionChangedEventData): void {
        if (data.playerId === this.selfPlayerId || !this.player || !this.mapManager || this.loadingMap) {
            return;
        }

        const otherPlayer = this.playersById.get(data.playerId);
        if (!otherPlayer) {
            return;
        }

        const facing = toDirection(data.direction);
        if (facing === Direction.None) {
            return;
        }
        if (otherPlayer.isMoving()) {
            otherPlayer.queueIdleFacingForWhenAligned(data.direction);
        } else {
            otherPlayer.applyIdleFacing(facing);
        }
    }

    private handlePlayerAppearanceChanged(data: PlayerAppearanceChangedEventData): void {
        if (data.playerId === this.selfPlayerId || !this.mapManager || this.loadingMap) {
            return;
        }

        const otherPlayer = this.playersById.get(data.playerId);
        if (!otherPlayer) {
            return;
        }

        otherPlayer.applyAppearance(
            data.gender,
            data.skinColor,
            data.underwearColorIndex,
            data.hairStyleIndex,
        );
    }

    private handleSpawnProtectionEnabled(data: { playerId: string }): void {
        const isSelf = data.playerId === this.selfPlayerId || data.playerId === this.initialGameWorldState?.playerId;
        if (isSelf) {
            if (this.player && this.mapManager && !this.loadingMap) {
                this.player.setSpawnProtectionEffect(true);
            } else {
                this.pendingSpawnProtectionForSelf = true;
            }
            return;
        }
        if (!this.player || !this.mapManager || this.loadingMap) {
            return;
        }
        const targetPlayer = this.playersById.get(data.playerId);
        if (targetPlayer) {
            targetPlayer.setSpawnProtectionEffect(true);
        }
    }

    private handleSpawnProtectionDisabled(data: { playerId: string }): void {
        const isSelf = data.playerId === this.selfPlayerId || data.playerId === this.initialGameWorldState?.playerId;
        if (isSelf) {
            if (this.player && this.mapManager && !this.loadingMap) {
                this.player.setSpawnProtectionEffect(false);
            }
            return;
        }
        if (!this.player || !this.mapManager || this.loadingMap) {
            return;
        }
        const targetPlayer = this.playersById.get(data.playerId);
        if (targetPlayer) {
            targetPlayer.setSpawnProtectionEffect(false);
        }
    }

    private handlePlayerDisconnected(data: PlayerConnectionStateChangedEventData): void {
        if (data.playerId === this.selfPlayerId || !this.player || !this.mapManager || this.loadingMap) {
            return;
        }

        const otherPlayer = this.playersById.get(data.playerId);
        if (!otherPlayer) {
            return;
        }

        otherPlayer.setDisconnected(true);
    }

    private handlePlayerReconnected(data: PlayerConnectionStateChangedEventData): void {
        if (data.playerId === this.selfPlayerId || !this.player || !this.mapManager || this.loadingMap) {
            return;
        }

        const otherPlayer = this.playersById.get(data.playerId);
        if (!otherPlayer) {
            return;
        }

        otherPlayer.setDisconnected(false);
    }

    private createOtherPlayer(
        playerId: string,
        worldX: number,
        worldY: number,
        movementSpeedMs: number = 220,
        runningMode: boolean = true,
        attackMode: boolean = true,
        disconnected: boolean = false,
        dead: boolean = false,
        direction: number = Direction.NorthEast,
        visibleEquippedItems: Partial<Record<ItemTypes, { itemId: number; effectOverrides?: Effect[] }>> = {},
        appearance: { gender: Gender; skinColor: SkinColor; underwearColorIndex: number; hairStyleIndex: number } = {
            gender: Gender.MALE,
            skinColor: SkinColor.Light,
            underwearColorIndex: 0,
            hairStyleIndex: 0,
        },
        characterName: string = '',
    ): Player {
        const resolvedDirection = toDirection(direction);
        const initialDirection = resolvedDirection === Direction.None ? Direction.NorthEast : resolvedDirection;
        const player = new Player(
            this,
            worldX,
            worldY,
            initialDirection,
            this.soundManager,
            this.getCurrentMap(),
            this.createDefaultPlayerGear(),
            movementSpeedMs,
            false,
            visibleEquippedItems,
            appearance,
        );
        player.setPlayerId(playerId);
        player.setCharacterName(characterName);
        player.setRunMode(runningMode);
        player.setAttackMode(attackMode);
        player.setDisconnected(disconnected);
        player.setRemoteIdleContinuationGraceMs(serverDialogStore.state.gracePeriod);
        if (dead) {
            player.applySpawnedDeathState();
        }
        return player;
    }

    private resolvePlayerById(playerId: string): Player | undefined {
        if (playerId === this.selfPlayerId) {
            return this.player;
        }
        return this.playersById.get(playerId);
    }

    /**
     * Updates player position for all monsters and their spatial audio.
     * 
     * @param playerX - Player's world X coordinate
     * @param playerY - Player's world Y coordinate
     */
    private updateMonsterSpatialAudio(playerX: number, playerY: number): void {
        for (const monster of this.monsters) {
            monster.updatePlayerPosition(playerX, playerY);
        }
    }

    public tryBeginTeleportAt(worldX: number, worldY: number): boolean {
        if (this.pendingPredictedWorldTransfer || this.awaitingTransferredWorldState) {
            return true;
        }

        const teleportTarget = this.teleportTargetsBySourceCell.get(this.getTeleportCellKey(worldX, worldY));
        if (!teleportTarget) {
            return false;
        }

        this.beginWorldTransfer(teleportTarget.worldId, teleportTarget.mapName);
        return true;
    }

    /**
     * Checks for collisions between the player and static map objects.
     * If the player collides with a map object and the player is behind it (lower depth),
     * makes the map object 50% transparent.
     * 
     * Uses spatial grid for efficient object lookup:
     * - Phase 1: Get objects within 20 grid cells using spatial grid (fast)
     * - Phase 2: Filter by accurate 10-cell radius distance (precise)
     * - Phase 3: Check pixel-perfect collision (accurate)
     */
    private handleMapObjectCollisions(): void {
        if (!this.player || !this.mapManager) {
            return;
        }

        this.collidingMapObjects = this.mapManager.updateMapObjectCollisionsForPlayer(
            this.player,
            this.collidingMapObjects,
        );
    }

    private clearPendingRequestedWorldChangeListener(): void {
        if (!this.pendingRequestedWorldChangeListener) {
            return;
        }

        EventBus.off(INITIAL_GAME_WORLD_STATE_RECEIVED, this.pendingRequestedWorldChangeListener);
        this.pendingRequestedWorldChangeListener = undefined;
    }

    public shutdown() {
        runSafeSync('GameWorld:shutdown', () => {
            EventBus.off(OUT_UI_LOGOUT_COUNTDOWN_CHANGED, this.logoutCountdownChangedHandler);
            this.hideLogoutCountdownOverlay();
            EventBus.emit(OUT_UI_HOVER_GROUND_ITEM, false);
            EventBus.emit(OUT_UI_HOVER_GROUND_ITEM_INFO, undefined);
            EventBus.emit(OUT_UI_HOVER_MONSTER, undefined);
            EventBus.emit(OUT_UI_HOVER_NPC, undefined);
            EventBus.emit(OUT_UI_HOVER_PLAYER, undefined);
            this.castManager?.destroy();
            this.castManager = undefined;
            if (this.player?.hasPendingSpell()) {
                this.player.cancelPendingCast();
            }
            this.inputManager?.destroy();
            this.inputManager = undefined;
            if (this.updateInterval) {
                clearInterval(this.updateInterval);
                this.updateInterval = undefined;
            }
            this.cursorPositionCleanup?.();
            this.cursorPositionCleanup = undefined;

            if (this.player) {
                this.player.destroy();
                this.player = undefined;
            }

            for (const [playerId, player] of this.playersById) {
                if (playerId === this.selfPlayerId) {
                    continue;
                }

                player.destroy();
            }
            this.playersById.clear();
            this.selfPlayerId = undefined;

            // Destroy all monsters
            for (const monster of this.monsters) {
                monster.destroy();
            }
            this.monsters = [];

            // Destroy all NPCs
            for (const npc of this.npcs) {
                npc.destroy();
            }
            this.npcs = [];

            // Destroy all ground items
            for (const groundItem of this.groundItems) {
                groundItem.destroy();
            }
            this.groundItems = [];
            for (const groundEffect of this.groundEffectsById.values()) {
                groundEffect.destroy();
            }
            this.groundEffectsById.clear();

            this.hideNativeOverlayMapLoading();
            this.hideLogoutCountdownOverlay();

            this.weatherManager?.destroy();
            this.weatherManager = undefined;

            if (this.soundManager) {
                this.soundManager.stopAllSounds();
            }

            this.initializationStarted = false;
            this.loadingMap = true;
            this.awaitingTransferredWorldState = false;
            this.pendingPredictedWorldTransfer = false;
            this.initialGameWorldState = undefined;
            this.pendingLoadedMap = undefined;
            this.teleportTargetsBySourceCell.clear();
            this.lastTeleportLocSets = [];
            this.clearPendingRequestedWorldChangeListener();
            this.mapManager?.resetCapturingState();
            this.collidingMapObjects.clear();

            this.cameraManager?.destroyEventListeners();
            this.cameraManager = undefined;
            EventBus.off(IN_UI_TOGGLE_NON_MOVABLE_CELLS_HIGHLIGHT);
            EventBus.off(IN_UI_TOGGLE_TELEPORT_CELLS_HIGHLIGHT);
            EventBus.off(IN_UI_TOGGLE_SERVER_TELEPORT_CELLS_HIGHLIGHT);
            EventBus.off(IN_UI_TOGGLE_WATER_CELLS_HIGHLIGHT);
            EventBus.off(IN_UI_TOGGLE_FARMABLE_CELLS_HIGHLIGHT);
            EventBus.off(IN_UI_TOGGLE_RENDER_MAP_TILES);
            EventBus.off(IN_UI_TOGGLE_RENDER_MAP_OBJECTS);
            EventBus.off(IN_UI_TOGGLE_DEBUG_MODE);
            EventBus.off(IN_UI_TOGGLE_GRID_DISPLAY);
            EventBus.off(IN_UI_TOGGLE_DISPLAY_LARGE_ITEMS);
            EventBus.off(IN_UI_CHANGE_WEATHER);
            EventBus.off(OUT_WEATHER_SYNCED);
            cancelPlayerDialogPhaserNotificationDebouncers();
            EventBus.off(IN_UI_CHANGE_MOVEMENT_SPEED);
            EventBus.off(IN_UI_CHANGE_ATTACK_SPEED);
            EventBus.off(IN_UI_CHANGE_ATTACK_RANGE);
            EventBus.off(IN_UI_CHANGE_STUN_DURATION);
            EventBus.off(IN_UI_CHANGE_DAMAGE);
            EventBus.off(IN_UI_CHANGE_ATTACK_TYPE);
            EventBus.off(IN_UI_CHANGE_ALLOW_DASH_ATTACK);
            EventBus.off(IN_UI_CHANGE_CAST_SPEED);
            EventBus.off(IN_UI_CHANGE_ATTACK_MODE);
            EventBus.off(IN_UI_CHANGE_RUN_MODE);
            // `off(event)` alone drops every listener; appearance events are shared (e.g. InventoryManager on gender, Player on all four), so remove only this scene's handler.
            EventBus.off(IN_UI_CHANGE_GENDER, this.syncPlayerAppearanceHandler);
            EventBus.off(IN_UI_CHANGE_SKIN_COLOR, this.syncPlayerAppearanceHandler);
            EventBus.off(IN_UI_CHANGE_UNDERWEAR_COLOR, this.syncPlayerAppearanceHandler);
            EventBus.off(IN_UI_CHANGE_HAIR_STYLE, this.syncPlayerAppearanceHandler);
            EventBus.off(IN_UI_REQUEST_PLAYER_LOGOUT);
            EventBus.off(SOCKET_DISCONNECTED);
            EventBus.off(PLAYER_ITEM_APPEARANCE_PREFETCH_REQUESTED, this.playerItemAppearancePrefetchHandler);
            EventBus.off(IN_UI_PLAYER_RESURRECT);
            EventBus.off(IN_UI_REQUEST_SERVER_RESURRECT);
            EventBus.off(PLAYER_DIED_RECEIVED);
            EventBus.off(PLAYER_RESURRECTED_RECEIVED);
            EventBus.off(IN_UI_PLAY_MUSIC);
            EventBus.off(IN_UI_CHANGE_PLAY_MAP_MUSIC);
            EventBus.off(IN_UI_CHANGE_MUSIC_VOLUME);
            EventBus.off(IN_UI_CHANGE_SOUND_VOLUME);
            EventBus.off(IN_UI_SUMMON_MONSTER);
            EventBus.off(IN_UI_SUMMON_NPC);
            EventBus.off(IN_UI_CAST_SPELL);
            EventBus.off(PLAYER_CAST_ANIMATION_STARTED);
            EventBus.off(PLAYER_CONFIRM_SPELL_TARGET);
            EventBus.off(IN_UI_KILL_ALL_NPCS);
            EventBus.off(IN_UI_CHANGE_MAP);
            EventBus.off(IN_UI_MAKE_SERVER_CELL_OCCUPIED_MODE);
            EventBus.off(IN_UI_PLAYER_TELEPORT_REQUEST_MODE);
            EventBus.off(IN_UI_CHANGE_GRACE_PERIOD);
            EventBus.off(PLAYER_POSITION_CHANGED);
            EventBus.off(TILE_OCCUPANCY_REAPPLY_REQUESTED);
            EventBus.off(MONSTER_ENTERED_RANGE_RECEIVED);
            EventBus.off(MONSTER_MOVED_RECEIVED);
            EventBus.off(MONSTER_ATTACKED_RECEIVED);
            EventBus.off(MONSTER_ATTACKED_MONSTER_RECEIVED);
            EventBus.off(MONSTER_DIED_RECEIVED);
            EventBus.off(GROUND_STATES_ENTERED_RANGE_RECEIVED);
            EventBus.off(GROUND_STATES_LEFT_RANGE_RECEIVED);
            EventBus.off(PLAYER_RECEIVE_DAMAGE_RECEIVED);
            EventBus.off(PLAYER_TAKE_DAMAGE_RECEIVED);
            EventBus.off(HP_UPDATED_RECEIVED);
            EventBus.off(MONSTER_TAKE_DAMAGE_RECEIVED);
            EventBus.off(MONSTER_TAKE_DAMAGE_BY_MONSTER_RECEIVED);
            EventBus.off(MONSTERS_LEFT_RANGE_RECEIVED);
            EventBus.off(NPC_ENTERED_RANGE_RECEIVED);
            EventBus.off(NPCS_LEFT_RANGE_RECEIVED);
            EventBus.off(PLAYER_JOINED_RECEIVED);
            EventBus.off(PLAYER_LEFT_RECEIVED);
            EventBus.off(PLAYER_MOVED_RECEIVED);
            EventBus.off(PLAYER_ATTACKED_MONSTER_RECEIVED);
            EventBus.off(PLAYER_ATTACKED_PLAYER_RECEIVED);
            EventBus.off(PLAYER_PICKUP_PERFORMED_RECEIVED);
            EventBus.off(PLAYER_BOW_STANCE_PERFORMED_RECEIVED);
            EventBus.off(SPELL_CAST_STARTED_RECEIVED);
            EventBus.off(SPELL_CAST_CANCELLED_RECEIVED);
            EventBus.off(SPELL_CAST_FAILED_RECEIVED);
            EventBus.off(CAST_AOE_SPELL_RECEIVED);
            EventBus.off(MONSTER_CAST_AOE_SPELL_RECEIVED);
            EventBus.off(MONSTER_CAST_DIRECTIONAL_AOE_SPELL_RECEIVED);
            EventBus.off(CAST_DIRECTIONAL_AOE_SPELL_RECEIVED);
            EventBus.off(PLAYER_MOVEMENT_STATE_CHANGED_RECEIVED);
            EventBus.off(PLAYER_ATTACK_MODE_CHANGED_RECEIVED);
            EventBus.off(PLAYER_IDLE_DIRECTION_CHANGED_RECEIVED);
            EventBus.off(PLAYER_APPEARANCE_CHANGED_RECEIVED);
            EventBus.off(PLAYER_DISCONNECTED_RECEIVED);
            EventBus.off(PLAYER_RECONNECTED_RECEIVED);
            EventBus.off(PLAYER_SPAWN_PROTECTION_ENABLED_RECEIVED);
            EventBus.off(PLAYER_SPAWN_PROTECTION_DISABLED_RECEIVED);
            EventBus.off(TEMPORARY_EFFECT_APPLIED_FOR_PLAYER_RECEIVED);
            EventBus.off(TEMPORARY_EFFECT_EXPIRED_FOR_PLAYER_RECEIVED);
            EventBus.off(TEMPORARY_EFFECT_APPLIED_FOR_MONSTER_RECEIVED);
            EventBus.off(TEMPORARY_EFFECT_EXPIRED_FOR_MONSTER_RECEIVED);
            EventBus.off(CAST_EFFECT_RECEIVED);
            EventBus.off(RESET_POSITION_RECEIVED);
            EventBus.off(PLAYER_TELEPORTED_RECEIVED);
            EventBus.off(POSITION_CORRECTED_RECEIVED);
            EventBus.off(PLAYER_PARALYZED_RECEIVED);
            EventBus.off(MONSTER_DEAD);
            EventBus.off(NPC_DEAD);
            // Destroy the map that was actually displayed (not getCurrentMap - gameStateManager
            // may already point to the new map after IN_UI_CHANGE_MAP)
            const mapToCleanup = this.displayedMap;
            this.displayedMap = undefined;
            if (mapToCleanup) {
                mapToCleanup.destroyAllHighlights();
                mapToCleanup.destroyMapTiles(this);
                mapToCleanup.destroyMapObjects();
            }
            this.mapManager = undefined;
        });
    }

}



function toRegistryInitialGameWorldState(data: InitialGameWorldStateEventData): InitialGameWorldState {
    return {
        gameWorldId: data.gameWorldId,
        mapName: `${data.mapName}.amd`,
        musicFile: data.musicFile,
        playerX: data.playerX,
        playerY: data.playerY,
        playerId: data.playerId,
        movementSpeedMs: data.movementSpeedMs,
        runMode: data.runMode,
        attackMode: data.attackMode,
        attackType: data.attackType,
        allowDashAttack: data.allowDashAttack,
        teleportLocs: data.teleportLocs,
        attackRangeCells: data.attackRangeCells,
        attackDamage: data.attackDamage,
        attackSpeedMs: data.attackSpeedMs,
        attackStunDurationMs: data.attackStunDurationMs,
        castSpeedMs: data.castSpeedMs,
        arrowSpeedPxPerSec: data.arrowSpeedPxPerSec,
        hp: data.hp,
        maxHp: data.maxHp,
        playerPickupAnimationTimeMs: data.playerPickupAnimationTimeMs,
        playerBowAnimationDurationMs: data.playerBowAnimationDurationMs,
        dead: data.dead,
        playerDirection: data.playerDirection,
        gender: data.gender,
        skinColor: data.skinColor,
        hairStyleIndex: data.hairStyleIndex,
        underwearColorIndex: data.underwearColorIndex,
        weather: data.weather,
    };
}
