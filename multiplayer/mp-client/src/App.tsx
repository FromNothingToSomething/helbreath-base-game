import { useRef, useState, useEffect } from 'react';
import { useStore } from '@tanstack/react-store';
import { DndContext } from '@dnd-kit/core';
import { toast } from 'react-toastify';
import { IRefPhaserGame, PhaserGame } from './PhaserGame';
import { ControlsDialog } from './ui/dialogs/ControlsDialog';
import { MapDialog } from './ui/dialogs/MapDialog';
import { CameraDialog } from './ui/dialogs/CameraDialog';
import { MinimapDialog } from './ui/dialogs/MinimapDialog';
import { AssetDebugOverlay } from './ui/overlays/AssetDebugOverlay';
import { InventoryItemHoverOverlay } from './ui/overlays/InventoryItemHoverOverlay';
import { MonsterHoverOverlay } from './ui/overlays/MonsterHoverOverlay';
import { NpcHoverOverlay } from './ui/overlays/NpcHoverOverlay';
import { PlayerHoverOverview } from './ui/overlays/PlayerHoverOverview';
import { SoundDialog } from './ui/dialogs/SoundDialog';
import { MonsterDialog } from './ui/dialogs/MonsterDialog';
import { NPCDialog } from './ui/dialogs/NPCDialog';
import { EffectDialog } from './ui/dialogs/EffectDialog';
import { CastDialog } from './ui/dialogs/CastDialog';
import { PlayerDialog } from './ui/dialogs/PlayerDialog';
import { InventoryDialog } from './ui/dialogs/InventoryDialog';
import { ItemDialog } from './ui/dialogs/ItemDialog';
import { ServerDialog } from './ui/dialogs/ServerDialog';
import { PerformanceDialog } from './ui/dialogs/PerformanceDialog';
import { ConnectDialog } from './ui/dialogs/ConnectDialog';
import { DeathDialog } from './ui/dialogs/DeathDialog';
import { NocHudDialog } from './ui/dialogs/NocHudDialog';
import { ConnectingDialog } from './ui/dialogs/ConnectingDialog';
import { ServerMessageDialog } from './ui/dialogs/ServerMessageDialog';
import { ChatDialog } from './ui/dialogs/ChatDialog';
import { EventBus, type ToastRequestedEvent } from './game/EventBus';
import { OUT_MAP_LOADED, TOAST_REQUESTED } from './constants/EventNames';
import { DIALOG_START_X, DIALOG_START_Y } from './Config';
import { mapDialogStore, setMapDialogOpen } from './ui/store/MapDialog.store';
import { cameraDialogStore, setCameraDialogOpen } from './ui/store/CameraDialog.store';
import { minimapDialogStore, setMinimapDialogOpen } from './ui/store/MinimapDialog.store';
import { soundDialogStore, setSoundDialogOpen } from './ui/store/SoundDialog.store';
import { monsterDialogStore } from './ui/store/MonsterDialog.store';
import { npcDialogStore } from './ui/store/NPCDialog.store';
import { effectDialogStore } from './ui/store/EffectDialog.store';
import { castDialogStore } from './ui/store/CastDialog.store';
import { controlsDialogStore, setControlsDialogOpen } from './ui/store/ControlsDialog.store';
import { playerDialogStore, setPlayerDialogOpen } from './ui/store/PlayerDialog.store';
import { inventoryDialogStore, setInventoryDialogOpen } from './ui/store/InventoryDialog.store';
import { itemDialogStore, setItemDialogOpen } from './ui/store/ItemDialog.store';
import { serverDialogStore, setServerDialogOpen } from './ui/store/ServerDialog.store';
import { performanceDialogStore, setPerformanceDialogOpen } from './ui/store/PerformanceDialog.store';
import { appStore, setCursorSpriteKey } from './ui/store/App.store';
import { CURSOR_GRAB_1, CURSOR_GRAB_2 } from './constants/SpriteKeys';
import { deathDialogStore } from './ui/store/DeathDialog.store';
import { nocStore } from './ui/store/Noc.store';
import { getNetworkManager } from './utils/RegistryUtils';
import { connectingDialogStore } from './ui/store/ConnectingDialog.store';
import { connectDialogStore } from './ui/store/ConnectDialog.store';
import { serverMessageDialogStore, setServerMessageDialogOpen } from './ui/store/ServerMessageDialog.store';
import { chatDialogStore, setChatDialogOpen } from './ui/store/ChatDialog.store';

const DIALOG_STACK_POSITION = { x: DIALOG_START_X, y: DIALOG_START_Y };
/** Position for dialogs opened from Controls - placed next to ControlsDialog (min-width 350px at x=20), ~310px from left */
const CHILD_DIALOG_POSITION = { x: 310, y: DIALOG_START_Y };
const CONNECTING_DIALOG_Z_INDEX = 9999;
const SERVER_MESSAGE_DIALOG_Z_INDEX = 19000;
/** Level/XP/mana HUD sits under the draggable dialogs (they start at 10000). */
const NOC_HUD_Z_INDEX = 9000;

function App()
{
    // PhaserGame ref — exposes `game` and active `scene`
    const phaserRef = useRef<IRefPhaserGame | null>(null);
    const [dialogPosition, setDialogPosition] = useState(DIALOG_STACK_POSITION);
    const [mapDialogPosition, setMapDialogPosition] = useState(CHILD_DIALOG_POSITION);
    const [cameraDialogPosition, setCameraDialogPosition] = useState(CHILD_DIALOG_POSITION);
    const [playerDialogPosition, setPlayerDialogPosition] = useState(CHILD_DIALOG_POSITION);
    const [minimapDialogPosition, setMinimapDialogPosition] = useState({ x: 1060, y: 20 });
    const [soundDialogPosition, setSoundDialogPosition] = useState(CHILD_DIALOG_POSITION);
    const [monsterDialogPosition, setMonsterDialogPosition] = useState(CHILD_DIALOG_POSITION);
    const [npcDialogPosition, setNPCDialogPosition] = useState(CHILD_DIALOG_POSITION);
    const [effectDialogPosition, setEffectDialogPosition] = useState(CHILD_DIALOG_POSITION);
    const [castDialogPosition, setCastDialogPosition] = useState(CHILD_DIALOG_POSITION);
    const [inventoryDialogPosition, setInventoryDialogPosition] = useState(CHILD_DIALOG_POSITION);
    const [itemDialogPosition, setItemDialogPosition] = useState(CHILD_DIALOG_POSITION);
    const [serverDialogPosition, setServerDialogPosition] = useState(CHILD_DIALOG_POSITION);
    const [performanceDialogPosition, setPerformanceDialogPosition] = useState(CHILD_DIALOG_POSITION);
    const [chatDialogPosition, setChatDialogPosition] = useState(CHILD_DIALOG_POSITION);
    // Death dialog: initial center (matches dialog size below)
    const [deathDialogPosition, setDeathDialogPosition] = useState(() => {
        if (typeof window === 'undefined') {
            return { x: 0, y: 0 };
        }
        const dialogWidth = 280;
        const dialogHeight = 120;
        return {
            x: Math.max(0, (window.innerWidth - dialogWidth) / 2),
            y: Math.max(0, (window.innerHeight - dialogHeight) / 2)
        };
    });
    const [connectingDialogPosition, setConnectingDialogPosition] = useState(() => {
        if (typeof window === 'undefined') {
            return { x: 0, y: 0 };
        }
        const dialogWidth = 280;
        const dialogHeight = 80;
        return {
            x: Math.max(0, (window.innerWidth - dialogWidth) / 2),
            y: Math.max(0, (window.innerHeight - dialogHeight) / 2)
        };
    });
    const [serverMessageDialogPosition, setServerMessageDialogPosition] = useState(() => {
        if (typeof window === 'undefined') {
            return { x: 0, y: 0 };
        }
        const dialogWidth = 400;
        const dialogHeight = 120;
        return {
            x: Math.max(0, (window.innerWidth - dialogWidth) / 2),
            y: Math.max(0, (window.innerHeight - dialogHeight) / 2)
        };
    });
    const [connectDialogPosition, setConnectDialogPosition] = useState(() => {
        if (typeof window === 'undefined') {
            return { x: 0, y: 0 };
        }
        const dialogWidth = 320;
        const dialogHeight = 280;
        return {
            x: Math.max(0, (window.innerWidth - dialogWidth) / 2),
            y: Math.max(0, (window.innerHeight - dialogHeight) / 2)
        };
    });
    // Level/XP/mana HUD: bottom-left, fixed (not draggable).
    const nocHudPosition = {
        x: 20,
        y: typeof window === 'undefined' ? 0 : Math.max(0, window.innerHeight - 100),
    };
    const handleDebugGrantXp = () => {
        const game = phaserRef.current?.game;
        if (!game) {
            return;
        }
        getNetworkManager(game)?.sendNocGrantXpRequest(100n);
    };
    // Z-index state for bringing dialogs to front
    const [dialogZIndex, setDialogZIndex] = useState(10000);
    const [mapDialogZIndex, setMapDialogZIndex] = useState(10001);
    const [cameraDialogZIndex, setCameraDialogZIndex] = useState(10002);
    const [playerDialogZIndex, setPlayerDialogZIndex] = useState(10003);
    const [minimapDialogZIndex, setMinimapDialogZIndex] = useState(10004);
    const [soundDialogZIndex, setSoundDialogZIndex] = useState(10005);
    const [monsterDialogZIndex, setMonsterDialogZIndex] = useState(10006);
    const [npcDialogZIndex, setNPCDialogZIndex] = useState(10007);
    const [effectDialogZIndex, setEffectDialogZIndex] = useState(10008);
    const [castDialogZIndex, setCastDialogZIndex] = useState(10009);
    const [inventoryDialogZIndex, setInventoryDialogZIndex] = useState(10010);
    const [itemDialogZIndex, setItemDialogZIndex] = useState(10011);
    const [serverDialogZIndex, setServerDialogZIndex] = useState(10014);
    const [performanceDialogZIndex, setPerformanceDialogZIndex] = useState(10016);
    const [chatDialogZIndex, setChatDialogZIndex] = useState(10019);
    const [deathDialogZIndex, setDeathDialogZIndex] = useState(10013);
    const [connectDialogZIndex, setConnectDialogZIndex] = useState(10018);
    const nextZIndexRef = useRef(10020);
    const showControlsDialog = useStore(controlsDialogStore, (state) => state.isOpen);
    const showMapDialog = useStore(mapDialogStore, (state) => state.isOpen);
    const showCameraDialog = useStore(cameraDialogStore, (state) => state.isOpen);
    const showPlayerDialog = useStore(playerDialogStore, (state) => state.isOpen);
    const showMinimapDialog = useStore(minimapDialogStore, (state) => state.isOpen && state.minimapAvailable);
    const showSoundDialog = useStore(soundDialogStore, (state) => state.isOpen);
    const showMonsterDialog = useStore(monsterDialogStore, (state) => state.isOpen);
    const showNPCDialog = useStore(npcDialogStore, (state) => state.isOpen);
    const showEffectDialog = useStore(effectDialogStore, (state) => state.isOpen);
    const showCastDialog = useStore(castDialogStore, (state) => state.isOpen);
    const showInventoryDialog = useStore(inventoryDialogStore, (state) => state.isOpen);
    const showItemDialog = useStore(itemDialogStore, (state) => state.isOpen);
    const showServerDialog = useStore(serverDialogStore, (state) => state.isOpen);
    const showPerformanceDialog = useStore(performanceDialogStore, (state) => state.isOpen);
    const showChatDialog = useStore(chatDialogStore, (state) => state.isOpen);
    const chatMessages = useStore(chatDialogStore, (state) => state.messages);
    const showDeathDialog = useStore(deathDialogStore, (state) => state.isOpen);
    const showNocHud = useStore(nocStore, (state) => state.isOpen);
    const showConnectingDialog = useStore(connectingDialogStore, (state) => state.isOpen);
    const showConnectDialog = useStore(connectDialogStore, (state) => state.isOpen);
    const showServerMessageDialog = useStore(serverMessageDialogStore, (state) => state.isOpen);
    const serverMessageDialogMessage = useStore(serverMessageDialogStore, (state) => state.message);
    const spriteFrameMap = useStore(appStore, (state) => state.spriteFrameMap);
    const cursorSpriteKey = useStore(appStore, (state) => state.cursorSpriteKey);
    const cursorImage = spriteFrameMap.get(cursorSpriteKey);
    const [isMapLoaded, setIsMapLoaded] = useState(false);

    const hasInitialMapLoadRef = useRef(false);

    // Listen to map loaded events from Phaser via EventBus
    useEffect(() => {
        const handleMapLoaded = () => {
            setIsMapLoaded(true);
            setControlsDialogOpen(true);
            if (!hasInitialMapLoadRef.current) {
                hasInitialMapLoadRef.current = true;
                setMinimapDialogOpen(true);
            }
            // Position minimap in top right corner
            const minimapWidth = 300; // Approximate minimap dialog width
            const margin = 20;
            setMinimapDialogPosition({
                x: window.innerWidth - minimapWidth - margin,
                y: margin,
            });
        };

        EventBus.on(OUT_MAP_LOADED, handleMapLoaded);

        return () => {
            EventBus.off(OUT_MAP_LOADED, handleMapLoaded);
        };
    }, []);

    useEffect(() => {
        const handleToastRequested = ({
            message,
            severity,
            autoClose,
        }: ToastRequestedEvent) => {
            const toastMessage = <span className="rpg-toast-message">{message}</span>;
            const options = autoClose !== undefined ? { autoClose } : {};

            switch (severity) {
                case 'success':
                    toast.success(toastMessage, options);
                    break;
                case 'warning':
                    toast.warning(toastMessage, options);
                    break;
                case 'error':
                    toast.error(toastMessage, options);
                    break;
                case 'info':
                    toast.info(toastMessage, options);
                    break;
            }
        };

        EventBus.on(TOAST_REQUESTED, handleToastRequested);

        return () => {
            EventBus.off(TOAST_REQUESTED, handleToastRequested);
        };
    }, []);

    // Bring dialogs to front when they're opened
    useEffect(() => {
        if (showMapDialog) {
            bringDialogToFront('map-dialog');
        }
    }, [showMapDialog]);

    useEffect(() => {
        if (showCameraDialog) {
            bringDialogToFront('camera-dialog');
        }
    }, [showCameraDialog]);

    useEffect(() => {
        if (showPlayerDialog) {
            bringDialogToFront('player-dialog');
        }
    }, [showPlayerDialog]);

    useEffect(() => {
        if (showMinimapDialog) {
            bringDialogToFront('minimap-dialog');
        }
    }, [showMinimapDialog]);

    useEffect(() => {
        if (showSoundDialog) {
            bringDialogToFront('sound-dialog');
        }
    }, [showSoundDialog]);

    useEffect(() => {
        if (showMonsterDialog) {
            bringDialogToFront('monster-dialog');
        }
    }, [showMonsterDialog]);

    useEffect(() => {
        if (showNPCDialog) {
            bringDialogToFront('npc-dialog');
        }
    }, [showNPCDialog]);

    useEffect(() => {
        if (showEffectDialog) {
            bringDialogToFront('effect-dialog');
        }
    }, [showEffectDialog]);

    useEffect(() => {
        if (showCastDialog) {
            bringDialogToFront('cast-dialog');
        }
    }, [showCastDialog]);

    useEffect(() => {
        if (showInventoryDialog) {
            bringDialogToFront('inventory-dialog');
        }
    }, [showInventoryDialog]);

    useEffect(() => {
        if (showItemDialog) {
            bringDialogToFront('item-dialog');
        }
    }, [showItemDialog]);

    useEffect(() => {
        if (showServerDialog) {
            bringDialogToFront('server-dialog');
        }
    }, [showServerDialog]);

    useEffect(() => {
        if (showPerformanceDialog) {
            bringDialogToFront('performance-dialog');
        }
    }, [showPerformanceDialog]);

    useEffect(() => {
        if (showChatDialog) {
            bringDialogToFront('chat-dialog');
        }
    }, [showChatDialog]);

    useEffect(() => {
        if (showConnectDialog) {
            bringDialogToFront('connect-dialog');
        }
    }, [showConnectDialog]);

    useEffect(() => {
        if (showDeathDialog) {
            bringDialogToFront('death-dialog');
        }
    }, [showDeathDialog]);

    // Center Connect dialog on viewport when shown or window resizes
    useEffect(() => {
        if (!showConnectDialog) {
            return;
        }

        const centerDialog = () => {
            const dialogElement = document.querySelector('[data-dialog-id="connect-dialog"]');
            if (dialogElement instanceof HTMLElement) {
                const rect = dialogElement.getBoundingClientRect();
                const dialogWidth = rect.width || dialogElement.offsetWidth || 320;
                const dialogHeight = rect.height || dialogElement.offsetHeight || 280;
                const newX = (window.innerWidth - dialogWidth) / 2;
                const newY = (window.innerHeight - dialogHeight) / 2;
                setConnectDialogPosition({
                    x: Math.max(0, newX),
                    y: Math.max(0, newY)
                });
            } else {
                const dialogWidth = 320;
                const dialogHeight = 280;
                setConnectDialogPosition({
                    x: Math.max(0, (window.innerWidth - dialogWidth) / 2),
                    y: Math.max(0, (window.innerHeight - dialogHeight) / 2)
                });
            }
        };

        const rafId = requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                centerDialog();
            });
        });
        window.addEventListener('resize', centerDialog);
        return () => {
            cancelAnimationFrame(rafId);
            window.removeEventListener('resize', centerDialog);
        };
    }, [showConnectDialog]);

    // Center Death dialog on viewport when shown or window resizes
    useEffect(() => {
        if (!showDeathDialog) {
            return;
        }

        const centerDialog = () => {
            const dialogElement = document.querySelector('[data-dialog-id="death-dialog"]');
            if (dialogElement instanceof HTMLElement) {
                const rect = dialogElement.getBoundingClientRect();
                const dialogWidth = rect.width || dialogElement.offsetWidth || 280;
                const dialogHeight = rect.height || dialogElement.offsetHeight || 120;
                const newX = (window.innerWidth - dialogWidth) / 2;
                const newY = (window.innerHeight - dialogHeight) / 2;
                setDeathDialogPosition({
                    x: Math.max(0, newX),
                    y: Math.max(0, newY)
                });
            } else {
                const dialogWidth = 280;
                const dialogHeight = 120;
                setDeathDialogPosition({
                    x: Math.max(0, (window.innerWidth - dialogWidth) / 2),
                    y: Math.max(0, (window.innerHeight - dialogHeight) / 2)
                });
            }
        };

        const rafId = requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                centerDialog();
            });
        });
        window.addEventListener('resize', centerDialog);
        return () => {
            cancelAnimationFrame(rafId);
            window.removeEventListener('resize', centerDialog);
        };
    }, [showDeathDialog]);

    // Center Connecting dialog on viewport when shown or window resizes
    useEffect(() => {
        if (!showConnectingDialog) {
            return;
        }

        const centerDialog = () => {
            const dialogElement = document.querySelector('[data-dialog-id="connecting-dialog"]');
            if (dialogElement instanceof HTMLElement) {
                const rect = dialogElement.getBoundingClientRect();
                const dialogWidth = rect.width || dialogElement.offsetWidth || 280;
                const dialogHeight = rect.height || dialogElement.offsetHeight || 80;
                const newX = (window.innerWidth - dialogWidth) / 2;
                const newY = (window.innerHeight - dialogHeight) / 2;
                setConnectingDialogPosition({
                    x: Math.max(0, newX),
                    y: Math.max(0, newY)
                });
            } else {
                const dialogWidth = 280;
                const dialogHeight = 80;
                setConnectingDialogPosition({
                    x: Math.max(0, (window.innerWidth - dialogWidth) / 2),
                    y: Math.max(0, (window.innerHeight - dialogHeight) / 2)
                });
            }
        };

        const rafId = requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                centerDialog();
            });
        });
        window.addEventListener('resize', centerDialog);
        return () => {
            cancelAnimationFrame(rafId);
            window.removeEventListener('resize', centerDialog);
        };
    }, [showConnectingDialog]);

    // Center Server Message dialog on viewport when shown or window resizes
    useEffect(() => {
        if (!showServerMessageDialog) {
            return;
        }

        const centerDialog = () => {
            const dialogElement = document.querySelector('[data-dialog-id="server-message-dialog"]');
            if (dialogElement instanceof HTMLElement) {
                const rect = dialogElement.getBoundingClientRect();
                const dialogWidth = rect.width || dialogElement.offsetWidth || 400;
                const dialogHeight = rect.height || dialogElement.offsetHeight || 120;
                const newX = (window.innerWidth - dialogWidth) / 2;
                const newY = (window.innerHeight - dialogHeight) / 2;
                setServerMessageDialogPosition({
                    x: Math.max(0, newX),
                    y: Math.max(0, newY)
                });
            } else {
                const dialogWidth = 400;
                const dialogHeight = 120;
                setServerMessageDialogPosition({
                    x: Math.max(0, (window.innerWidth - dialogWidth) / 2),
                    y: Math.max(0, (window.innerHeight - dialogHeight) / 2)
                });
            }
        };

        const rafId = requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                centerDialog();
            });
        });
        window.addEventListener('resize', centerDialog);
        return () => {
            cancelAnimationFrame(rafId);
            window.removeEventListener('resize', centerDialog);
        };
    }, [showServerMessageDialog]);

    const bringDialogToFront = (dialogId: string) => {
        const currentZIndex = nextZIndexRef.current;
        nextZIndexRef.current += 1;
        
        switch (dialogId) {
            case 'main-dialog':
                setDialogZIndex(currentZIndex);
                break;
            case 'map-dialog':
                setMapDialogZIndex(currentZIndex);
                break;
            case 'camera-dialog':
                setCameraDialogZIndex(currentZIndex);
                break;
            case 'player-dialog':
                setPlayerDialogZIndex(currentZIndex);
                break;
            case 'minimap-dialog':
                setMinimapDialogZIndex(currentZIndex);
                break;
            case 'sound-dialog':
                setSoundDialogZIndex(currentZIndex);
                break;
            case 'monster-dialog':
                setMonsterDialogZIndex(currentZIndex);
                break;
            case 'npc-dialog':
                setNPCDialogZIndex(currentZIndex);
                break;
            case 'effect-dialog':
                setEffectDialogZIndex(currentZIndex);
                break;
            case 'cast-dialog':
                setCastDialogZIndex(currentZIndex);
                break;
            case 'inventory-dialog':
                setInventoryDialogZIndex(currentZIndex);
                break;
            case 'item-dialog':
                setItemDialogZIndex(currentZIndex);
                break;
            case 'server-dialog':
                setServerDialogZIndex(currentZIndex);
                break;
            case 'performance-dialog':
                setPerformanceDialogZIndex(currentZIndex);
                break;
            case 'chat-dialog':
                setChatDialogZIndex(currentZIndex);
                break;
            case 'connect-dialog':
                setConnectDialogZIndex(currentZIndex);
                break;
            case 'death-dialog':
                setDeathDialogZIndex(currentZIndex);
                break;
        }
    };

    const handleDragEnd = (event: any) => {
        const { active, delta } = event;
        const dialogId = active.id;
        const GRID_SIZE = 10; // 10x10 pixel snap grid
        
        // Get dialog element and its dimensions
        const dialogElement = document.querySelector(`[data-dialog-id="${dialogId}"]`);
        if (!(dialogElement instanceof HTMLElement)) return;
        
        const dialogWidth = dialogElement.offsetWidth || 200;
        const dialogHeight = dialogElement.offsetHeight || 150;
        
        // Determine which dialog is being dragged and get its current position
        let currentPosition: { x: number; y: number };
        switch (dialogId) {
            case 'main-dialog':
                currentPosition = dialogPosition;
                break;
            case 'map-dialog':
                currentPosition = mapDialogPosition;
                break;
            case 'camera-dialog':
                currentPosition = cameraDialogPosition;
                break;
            case 'player-dialog':
                currentPosition = playerDialogPosition;
                break;
            case 'minimap-dialog':
                currentPosition = minimapDialogPosition;
                break;
            case 'sound-dialog':
                currentPosition = soundDialogPosition;
                break;
            case 'monster-dialog':
                currentPosition = monsterDialogPosition;
                break;
            case 'npc-dialog':
                currentPosition = npcDialogPosition;
                break;
            case 'effect-dialog':
                currentPosition = effectDialogPosition;
                break;
            case 'cast-dialog':
                currentPosition = castDialogPosition;
                break;
            case 'inventory-dialog':
                currentPosition = inventoryDialogPosition;
                break;
            case 'item-dialog':
                currentPosition = itemDialogPosition;
                break;
            case 'server-dialog':
                currentPosition = serverDialogPosition;
                break;
            case 'performance-dialog':
                currentPosition = performanceDialogPosition;
                break;
            case 'chat-dialog':
                currentPosition = chatDialogPosition;
                break;
            case 'connect-dialog':
                currentPosition = connectDialogPosition;
                break;
            case 'death-dialog':
                currentPosition = deathDialogPosition;
                break;
            case 'connecting-dialog':
                currentPosition = connectingDialogPosition;
                break;
            case 'server-message-dialog':
                currentPosition = serverMessageDialogPosition;
                break;
            default:
                const rect = dialogElement.getBoundingClientRect();
                currentPosition = { x: rect.left, y: rect.top };
        }
        
        // Calculate new position
        let newX = currentPosition.x + delta.x;
        let newY = currentPosition.y + delta.y;
        
        // Snap to grid (10x10 pixels)
        newX = Math.round(newX / GRID_SIZE) * GRID_SIZE;
        newY = Math.round(newY / GRID_SIZE) * GRID_SIZE;
        
        // Get viewport dimensions
        const viewportWidth = window.innerWidth;
        const viewportHeight = window.innerHeight;
        
        // Clamp position to keep dialog fully within viewport
        newX = Math.max(0, Math.min(newX, viewportWidth - dialogWidth));
        newY = Math.max(0, Math.min(newY, viewportHeight - dialogHeight));
        
        // Update the appropriate dialog position
        const position = { x: newX, y: newY };
        switch (dialogId) {
            case 'main-dialog':
                setDialogPosition(position);
                break;
            case 'map-dialog':
                setMapDialogPosition(position);
                break;
            case 'camera-dialog':
                setCameraDialogPosition(position);
                break;
            case 'player-dialog':
                setPlayerDialogPosition(position);
                break;
            case 'minimap-dialog':
                setMinimapDialogPosition(position);
                break;
            case 'sound-dialog':
                setSoundDialogPosition(position);
                break;
            case 'monster-dialog':
                setMonsterDialogPosition(position);
                break;
            case 'npc-dialog':
                setNPCDialogPosition(position);
                break;
            case 'effect-dialog':
                setEffectDialogPosition(position);
                break;
            case 'cast-dialog':
                setCastDialogPosition(position);
                break;
            case 'inventory-dialog':
                setInventoryDialogPosition(position);
                break;
            case 'item-dialog':
                setItemDialogPosition(position);
                break;
            case 'server-dialog':
                setServerDialogPosition(position);
                break;
            case 'performance-dialog':
                setPerformanceDialogPosition(position);
                break;
            case 'chat-dialog':
                setChatDialogPosition(position);
                break;
            case 'connect-dialog':
                setConnectDialogPosition(position);
                break;
            case 'death-dialog':
                setDeathDialogPosition(position);
                break;
            case 'connecting-dialog':
                setConnectingDialogPosition(position);
                break;
            case 'server-message-dialog':
                setServerMessageDialogPosition(position);
                break;
        }
    };


    // Set custom cursor for the entire viewport using CSS variable
    useEffect(() => {
        if (cursorImage) {
            // Set CSS variable instead of directly overwriting cursor style
            document.documentElement.style.setProperty('--custom-cursor', `url(${cursorImage}), auto`);
        }

        return () => {
            // Remove CSS variable when component unmounts or cursorImage changes
            document.documentElement.style.removeProperty('--custom-cursor');
        };
    }, [cursorImage]);

    // Re-apply cursor every 500ms to recover from browser reverting to default, and animate grab cursor.
    // In fullscreen, hovering the browser's "exit fullscreen" toast resets cursor to OS default.
    // Force a brief state change (grab for 1ms then back) to kick the browser into re-applying our cursor.
    useEffect(() => {
        let forceRefreshTimeout: ReturnType<typeof setTimeout> | null = null;

        const interval = setInterval(() => {
            const { cursorSpriteKey: key, spriteFrameMap: map } = appStore.state;
            const isFullscreen = !!document.fullscreenElement;

            const isGrabCursor = key === CURSOR_GRAB_1 || key === CURSOR_GRAB_2;

            if (isFullscreen && !isGrabCursor) {
                // Non-grab cursor in fullscreen: browser toast can reset cursor to OS default when hovered.
                // Force a state change (grab for 1ms then back) to kick the browser into re-applying our cursor.
                if (forceRefreshTimeout) clearTimeout(forceRefreshTimeout);
                const correctKey = key;
                const flashImage = map.get(CURSOR_GRAB_1);
                if (flashImage) {
                    setCursorSpriteKey(CURSOR_GRAB_1);
                    document.documentElement.style.setProperty('--custom-cursor', `url(${flashImage}), auto`);
                }
                forceRefreshTimeout = setTimeout(() => {
                    setCursorSpriteKey(correctKey);
                    const image = map.get(correctKey);
                    if (image) {
                        document.documentElement.style.setProperty('--custom-cursor', `url(${image}), auto`);
                    }
                    forceRefreshTimeout = null;
                }, 1);
            } else {
                // Normal: animate grab cursor (toggle every 500ms) and re-apply. Also recovers from OS default in fullscreen.
                let imageKey = key;
                if (key === CURSOR_GRAB_1) {
                    imageKey = CURSOR_GRAB_2;
                    setCursorSpriteKey(CURSOR_GRAB_2);
                } else if (key === CURSOR_GRAB_2) {
                    imageKey = CURSOR_GRAB_1;
                    setCursorSpriteKey(CURSOR_GRAB_1);
                }
                const image = map.get(imageKey);
                if (image) {
                    document.documentElement.style.setProperty('--custom-cursor', `url(${image}), auto`);
                }
            }
        }, 500);

        return () => {
            clearInterval(interval);
            if (forceRefreshTimeout) clearTimeout(forceRefreshTimeout);
        };
    }, []);

    return (
        <DndContext onDragEnd={handleDragEnd}>
            <div id="app">
                <PhaserGame ref={phaserRef} />
                
                {showControlsDialog && (
                    <ControlsDialog
                        position={dialogPosition}
                        phaserRef={phaserRef}
                        zIndex={dialogZIndex}
                        onBringToFront={() => bringDialogToFront('main-dialog')}
                    />
                )}
                
                {showMapDialog && (
                    <MapDialog
                        position={mapDialogPosition}
                        onClose={() => setMapDialogOpen(false)}
                        zIndex={mapDialogZIndex}
                        onBringToFront={() => bringDialogToFront('map-dialog')}
                    />
                )}
                
                {showCameraDialog && (
                    <CameraDialog
                        position={cameraDialogPosition}
                        phaserRef={phaserRef}
                        onClose={() => setCameraDialogOpen(false)}
                        zIndex={cameraDialogZIndex}
                        onBringToFront={() => bringDialogToFront('camera-dialog')}
                    />
                )}
                
                {showPlayerDialog && (
                    <PlayerDialog
                        position={playerDialogPosition}
                        onClose={() => setPlayerDialogOpen(false)}
                        zIndex={playerDialogZIndex}
                        onBringToFront={() => bringDialogToFront('player-dialog')}
                    />
                )}
                
                {isMapLoaded && showMinimapDialog && (
                    <MinimapDialog
                        position={minimapDialogPosition}
                        onClose={() => setMinimapDialogOpen(false)}
                        onPositionChange={(newPosition) => setMinimapDialogPosition(newPosition)}
                        zIndex={minimapDialogZIndex}
                        onBringToFront={() => bringDialogToFront('minimap-dialog')}
                    />
                )}
                
                {showSoundDialog && (
                    <SoundDialog
                        position={soundDialogPosition}
                        onClose={() => setSoundDialogOpen(false)}
                        zIndex={soundDialogZIndex}
                        onBringToFront={() => bringDialogToFront('sound-dialog')}
                    />
                )}
                
                {showMonsterDialog && (
                    <MonsterDialog
                        position={monsterDialogPosition}
                        phaserRef={phaserRef}
                        zIndex={monsterDialogZIndex}
                        onBringToFront={() => bringDialogToFront('monster-dialog')}
                    />
                )}
                
                {showNPCDialog && (
                    <NPCDialog
                        position={npcDialogPosition}
                        zIndex={npcDialogZIndex}
                        onBringToFront={() => bringDialogToFront('npc-dialog')}
                    />
                )}
                
                {showEffectDialog && (
                    <EffectDialog
                        position={effectDialogPosition}
                        zIndex={effectDialogZIndex}
                        onBringToFront={() => bringDialogToFront('effect-dialog')}
                    />
                )}
                
                {showCastDialog && (
                    <CastDialog
                        position={castDialogPosition}
                        zIndex={castDialogZIndex}
                        onBringToFront={() => bringDialogToFront('cast-dialog')}
                    />
                )}
                
                {showInventoryDialog && (
                    <InventoryDialog
                        position={inventoryDialogPosition}
                        onClose={() => setInventoryDialogOpen(false)}
                        zIndex={inventoryDialogZIndex}
                        onBringToFront={() => bringDialogToFront('inventory-dialog')}
                    />
                )}
                
                {showItemDialog && (
                    <ItemDialog
                        position={itemDialogPosition}
                        onClose={() => setItemDialogOpen(false)}
                        zIndex={itemDialogZIndex}
                        onBringToFront={() => bringDialogToFront('item-dialog')}
                    />
                )}
                
                {showServerDialog && (
                    <ServerDialog
                        position={serverDialogPosition}
                        onClose={() => setServerDialogOpen(false)}
                        zIndex={serverDialogZIndex}
                        onBringToFront={() => bringDialogToFront('server-dialog')}
                    />
                )}
                
                {showPerformanceDialog && (
                    <PerformanceDialog
                        position={performanceDialogPosition}
                        onClose={() => setPerformanceDialogOpen(false)}
                        zIndex={performanceDialogZIndex}
                        onBringToFront={() => bringDialogToFront('performance-dialog')}
                    />
                )}

                {showChatDialog && (
                    <ChatDialog
                        messages={chatMessages}
                        position={chatDialogPosition}
                        phaserRef={phaserRef}
                        onClose={() => setChatDialogOpen(false)}
                        zIndex={chatDialogZIndex}
                        onBringToFront={() => bringDialogToFront('chat-dialog')}
                    />
                )}
                
                <AssetDebugOverlay />
                <InventoryItemHoverOverlay />
                <MonsterHoverOverlay />
                <NpcHoverOverlay />
                <PlayerHoverOverview />
                
                {showNocHud && (
                    <NocHudDialog
                        position={nocHudPosition}
                        zIndex={NOC_HUD_Z_INDEX}
                        onDebugGrantXp={import.meta.env.DEV ? handleDebugGrantXp : undefined}
                    />
                )}

                {showDeathDialog && (
                    <DeathDialog
                        position={deathDialogPosition}
                        zIndex={deathDialogZIndex}
                        onBringToFront={() => bringDialogToFront('death-dialog')}
                    />
                )}

                {showConnectingDialog && (
                    <ConnectingDialog
                        position={connectingDialogPosition}
                        zIndex={CONNECTING_DIALOG_Z_INDEX}
                    />
                )}

                {showConnectDialog && (
                    <ConnectDialog
                        position={connectDialogPosition}
                        zIndex={connectDialogZIndex}
                        onBringToFront={() => bringDialogToFront('connect-dialog')}
                    />
                )}
                {showServerMessageDialog && (
                    <ServerMessageDialog
                        message={serverMessageDialogMessage}
                        position={serverMessageDialogPosition}
                        onClose={() => setServerMessageDialogOpen(false)}
                        zIndex={SERVER_MESSAGE_DIALOG_Z_INDEX}
                    />
                )}
            </div>
        </DndContext>
    )
}

export default App
