import { Scene } from 'phaser';
import {
    appendPendingPlayerItemAppearancePrefetch,
    clearPendingPlayerItemAppearancePrefetch,
    createGameStateManager,
    getGameStateManager,
    getInventoryManager,
    setInitialGameWorldState,
    setNetworkManager,
} from '../../utils/RegistryUtils';
import {
    CURRENT_SCENE_READY,
    INITIAL_GAME_WORLD_STATE_RECEIVED,
    IN_UI_CONNECT_TO_SERVER,
    NATIVE_OVERLAY_LOGIN_BACKGROUND_HIDDEN,
    NATIVE_OVERLAY_LOGIN_BACKGROUND_SHOWN,
    OUT_UI_SET_SELECTED_MAP,
    PLAYER_ITEM_APPEARANCE_PREFETCH_REQUESTED,
    SOCKET_DISCONNECTED,
} from '../../constants/EventNames';
import type { ConnectToServerPayload, PlayerItemAppearancePrefetchEventData } from '../../constants/EventNames';
import { SPLASH_BACKGROUND_IMAGE_SRC } from '../../constants/SceneOverlays';
import { EventBus } from '../EventBus';
import { NetworkManager } from '../../utils/NetworkManager';
import type { InitialGameWorldStateEventData } from '../../Types';
import { setConnectingDialogOpen } from '../../ui/store/ConnectingDialog.store';
import { openConnectDialogForLogin, setConnectDialogOpen } from '../../ui/store/ConnectDialog.store';

/**
 * Login screen scene. Displays title and opens the Connect dialog to join the server.
 * Creates GameStateManager and transitions to GameWorld after a successful connection.
 */
export class LoginScreen extends Scene {
    private isConnecting = false;
    private pendingInitialGameWorldStateListener: ((data: InitialGameWorldStateEventData) => void) | undefined;
    /** When set, login is waiting for initial state after TCP connect; auth failure closes the socket first. */
    private loginPendingDisconnectHandler: (() => void) | undefined;
    private connectToServerHandler: ((payload: ConnectToServerPayload) => void) | undefined;
    private prefetchPlayerItemAppearanceHandler: ((payload: PlayerItemAppearancePrefetchEventData) => void) | undefined;

    constructor() {
        super('LoginScreen');
    }

    public init() {
        this.clearPendingInitialGameWorldStateListener();
        this.clearLoginPendingDisconnectListener();
        this.clearConnectToServerListener();
        this.isConnecting = false;

        this.cameras.main.setBackgroundColor(0x000000);

        EventBus.emit(NATIVE_OVERLAY_LOGIN_BACKGROUND_SHOWN, { imageSrc: SPLASH_BACKGROUND_IMAGE_SRC });

        createGameStateManager(this.game);

        this.events.once('shutdown', () => {
            this.clearPendingInitialGameWorldStateListener();
            this.clearLoginPendingDisconnectListener();
            this.clearConnectToServerListener();
            this.clearPrefetchPlayerItemAppearanceListener();
            this.isConnecting = false;
            setConnectingDialogOpen(false);
            setConnectDialogOpen(false);
            EventBus.emit(NATIVE_OVERLAY_LOGIN_BACKGROUND_HIDDEN);
        });
    }

    public create() {
        const gsm = getGameStateManager(this.game);
        openConnectDialogForLogin(gsm.getCharacterName() ?? '');

            const handleConnectToServer = async (payload: ConnectToServerPayload) => {
            if (this.isConnecting) {
                return;
            }

            this.isConnecting = true;
            clearPendingPlayerItemAppearancePrefetch(this.game);
            this.clearPendingInitialGameWorldStateListener();
            setConnectingDialogOpen(true);

            // `connect()` resolves when the WebSocket opens; auth runs after that. If the server rejects
            // (e.g. duplicate session), the socket closes without initial state — recover the Connect UI.
            const handleSocketDisconnectedDuringLogin = () => {
                if (!this.pendingInitialGameWorldStateListener) {
                    return;
                }
                this.clearPendingInitialGameWorldStateListener();
                this.clearLoginPendingDisconnectListener();
                this.isConnecting = false;
                setConnectingDialogOpen(false);
                setConnectDialogOpen(true);
                setNetworkManager(this.game, undefined);
                console.warn('[LoginScreen] Connection closed before initial game world state (e.g. auth rejected).');
            };

            const handleInitialGameWorldStateReceived = (data: InitialGameWorldStateEventData) => {
                this.clearLoginPendingDisconnectListener();
                this.pendingInitialGameWorldStateListener = undefined;
                this.isConnecting = false;
                setConnectingDialogOpen(false);
                gsm.setCharacterName(payload.characterName);
                setInitialGameWorldState(this.game, {
                    gameWorldId: data.gameWorldId,
                    mapName: `${data.mapName}.amd`,
                    musicFile: data.musicFile || undefined,
                    playerX: data.playerX,
                    playerY: data.playerY,
                    playerId: data.playerId,
                    movementSpeedMs: data.movementSpeedMs,
                    runMode: data.runMode,
                    attackMode: data.attackMode,
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
                });
                if (data.gameWorldId) {
                    EventBus.emit(OUT_UI_SET_SELECTED_MAP, data.gameWorldId);
                }
                getInventoryManager(this.game);
                this.scene.start('GameWorld');
            };

            this.pendingInitialGameWorldStateListener = handleInitialGameWorldStateReceived;
            EventBus.once(INITIAL_GAME_WORLD_STATE_RECEIVED, handleInitialGameWorldStateReceived);

            const networkManager = new NetworkManager(gsm.getNetworkId());
            setNetworkManager(this.game, networkManager);

            try {
                await networkManager.connect(payload.host, payload.port, payload.characterName, payload.creation);
                this.loginPendingDisconnectHandler = handleSocketDisconnectedDuringLogin;
                EventBus.on(SOCKET_DISCONNECTED, handleSocketDisconnectedDuringLogin);
            } catch (error) {
                this.clearPendingInitialGameWorldStateListener();
                this.clearLoginPendingDisconnectListener();
                this.isConnecting = false;
                setConnectingDialogOpen(false);
                setConnectDialogOpen(true);
                console.error('[LoginScreen] Failed to connect to the server.', error);
                setNetworkManager(this.game, undefined);
            }
        };

        this.connectToServerHandler = handleConnectToServer;
        EventBus.on(IN_UI_CONNECT_TO_SERVER, handleConnectToServer);

        const queuePrefetch = (prefetch: PlayerItemAppearancePrefetchEventData) => {
            appendPendingPlayerItemAppearancePrefetch(this.game, prefetch.spriteNames);
        };
        this.prefetchPlayerItemAppearanceHandler = queuePrefetch;
        EventBus.on(PLAYER_ITEM_APPEARANCE_PREFETCH_REQUESTED, queuePrefetch);

        EventBus.emit(CURRENT_SCENE_READY, this);
    }

    private clearConnectToServerListener(): void {
        if (!this.connectToServerHandler) {
            return;
        }

        EventBus.off(IN_UI_CONNECT_TO_SERVER, this.connectToServerHandler);
        this.connectToServerHandler = undefined;
    }

    private clearPendingInitialGameWorldStateListener(): void {
        if (!this.pendingInitialGameWorldStateListener) {
            return;
        }

        EventBus.off(INITIAL_GAME_WORLD_STATE_RECEIVED, this.pendingInitialGameWorldStateListener);
        this.pendingInitialGameWorldStateListener = undefined;
    }

    private clearLoginPendingDisconnectListener(): void {
        if (!this.loginPendingDisconnectHandler) {
            return;
        }

        EventBus.off(SOCKET_DISCONNECTED, this.loginPendingDisconnectHandler);
        this.loginPendingDisconnectHandler = undefined;
    }

    private clearPrefetchPlayerItemAppearanceListener(): void {
        if (!this.prefetchPlayerItemAppearanceHandler) {
            return;
        }

        EventBus.off(PLAYER_ITEM_APPEARANCE_PREFETCH_REQUESTED, this.prefetchPlayerItemAppearanceHandler);
        this.prefetchPlayerItemAppearanceHandler = undefined;
    }
}
