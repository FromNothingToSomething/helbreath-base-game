import {
    CastAoeSpell,
    CastDirectionalAoeSpell,
    ChatMessageReceived,
    ClientMessage,
    EquippedInventoryItemEntry,
    GroundStatesEnteredRange,
    GroundStatesLeftRange,
    HpUpdated,
    InitialGameWorldState,
    InitialState,
    InventoryItemEntry,
    ItemAddedToBag,
    ItemEquipped,
    ItemMovedInBag,
    ItemRemovedFromBag,
    ItemUnequipped,
    MonsterAttacked,
    MonsterAttackedMonster,
    MonsterCastAoeSpell,
    MonsterCastDirectionalAoeSpell,
    MonsterDied,
    MonsterEntityState,
    MonsterMoved,
    MonstersEnteredRange,
    MonstersLeftRange,
    MonstersList,
    NpcsEnteredRange,
    NpcsLeftRange,
    MonsterTakeDamageByMonster,
    PingResponse,
    PlayerAppearanceChanged,
    PlayerAttackModeChanged,
    PlayerAttackedMonster,
    PlayerAttackedPlayer,
    PlayerDisconnected,
    PlayerIdleDirectionChanged,
    PlayerMoved,
    PlayerMovementStateChanged,
    PlayerReceiveDamage,
    PlayerReconnected,
    PlayersEnteredRange,
    PlayersLeftRange,
    PlayerTakeDamage,
    PlayerGender,
    PlayerSkinColor,
    PlayerTeleported,
    ServerMessage,
    SpellCastCancelled,
    SpellCastFailed,
    SpellCastRequest,
    SpellCastStarted,
    VisibleEquippedItemEntry,
    WorldsList,
    TemporaryEffectApplied,
    type TemporaryEffectExpired,
    TemporaryEffectEntityKind,
    CastEffect,
    WeatherChanged,
    WeatherMode as WeatherModeProto,
} from '../proto/generated/network';
import type { NocCharacterCreation, NocSnapshot } from '../proto/generated/nocthemar';
import { encodeAuthenticateRequest } from './Authentication';
import { toNocViewModel } from './NocView';
import { toCatalogModel } from './NocCatalogView';
import { EventBus, type ToastRequestedEvent } from '../game/EventBus';
import { LOAD_PLAYER_ITEM_APPEARANCE_ASSETS_ON_DEMAND } from '../Config';
import { runSafeSync } from './SafeEntry';
import {
    NOC_STATE_RECEIVED,
    NOC_STATE_RESET,
    NOC_CATALOG_RECEIVED,
    NOC_ACTION_REFUSED_RECEIVED,
    CAST_AOE_SPELL_RECEIVED,
    CAST_DIRECTIONAL_AOE_SPELL_RECEIVED,
    MONSTER_CAST_AOE_SPELL_RECEIVED,
    MONSTER_CAST_DIRECTIONAL_AOE_SPELL_RECEIVED,
    CHAT_MESSAGE_RECEIVED,
    GROUND_STATES_ENTERED_RANGE_RECEIVED,
    GROUND_STATES_LEFT_RANGE_RECEIVED,
    HP_UPDATED_RECEIVED,
    INITIAL_GAME_WORLD_STATE_RECEIVED,
    MONSTER_ATTACKED_MONSTER_RECEIVED,
    MONSTER_ATTACKED_RECEIVED,
    MONSTER_DIED_RECEIVED,
    MONSTER_ENTERED_RANGE_RECEIVED,
    NPC_ENTERED_RANGE_RECEIVED,
    NPCS_LEFT_RANGE_RECEIVED,
    MONSTER_MOVED_RECEIVED,
    MONSTERS_LEFT_RANGE_RECEIVED,
    MONSTER_TAKE_DAMAGE_BY_MONSTER_RECEIVED,
    MONSTER_TAKE_DAMAGE_RECEIVED,
    OUT_UI_SET_ALLOW_DASH_ATTACK,
    OUT_UI_SET_ATTACK_MODE,
    OUT_UI_SET_ATTACK_RANGE,
    OUT_UI_SET_ATTACK_SPEED_MS,
    OUT_UI_SET_ATTACK_TYPE,
    OUT_UI_SET_CAST_SPEED,
    OUT_UI_SET_DAMAGE,
    OUT_UI_SET_GENDER,
    OUT_UI_SET_GAME_WORLDS,
    OUT_UI_SET_MONSTERS,
    OUT_UI_SET_NPC_DIRECTORY,
    OUT_UI_SET_MOVEMENT_SPEED,
    OUT_UI_SET_RUN_MODE,
    OUT_UI_SET_HAIR_STYLE,
    OUT_UI_SET_SKIN_COLOR,
    OUT_UI_SET_SPELLS,
    OUT_UI_SET_STUN_DURATION_MS,
    OUT_UI_SET_UNDERWEAR_COLOR,
    PLAYER_ITEM_APPEARANCE_PREFETCH_REQUESTED,
    PLAYER_APPEARANCE_CHANGED_RECEIVED,
    PLAYER_ATTACKED_MONSTER_RECEIVED,
    PLAYER_ATTACKED_PLAYER_RECEIVED,
    PLAYER_ATTACK_MODE_CHANGED_RECEIVED,
    PLAYER_BOW_STANCE_PERFORMED_RECEIVED,
    PLAYER_DIED_RECEIVED,
    PLAYER_DISCONNECTED_RECEIVED,
    PLAYER_IDLE_DIRECTION_CHANGED_RECEIVED,
    PLAYER_JOINED_RECEIVED,
    PLAYER_LEFT_RECEIVED,
    PLAYER_MOVED_RECEIVED,
    PLAYER_MOVEMENT_STATE_CHANGED_RECEIVED,
    PLAYER_PARALYZED_RECEIVED,
    PLAYER_PICKUP_PERFORMED_RECEIVED,
    PLAYER_RECEIVE_DAMAGE_RECEIVED,
    PLAYER_RECONNECTED_RECEIVED,
    PLAYER_RESURRECTED_RECEIVED,
    PLAYER_SPAWN_PROTECTION_DISABLED_RECEIVED,
    PLAYER_SPAWN_PROTECTION_ENABLED_RECEIVED,
    PLAYER_TAKE_DAMAGE_RECEIVED,
    PLAYER_TELEPORTED_RECEIVED,
    POSITION_CORRECTED_RECEIVED,
    REMOTE_PLAYER_ITEM_EQUIPPED_RECEIVED,
    REMOTE_PLAYER_ITEM_UNEQUIPPED_RECEIVED,
    RESET_POSITION_RECEIVED,
    SERVER_INVENTORY_SNAPSHOT_RECEIVED,
    SERVER_ITEM_ADDED_TO_BAG_RECEIVED,
    SERVER_ITEM_EQUIPPED_RECEIVED,
    SERVER_ITEM_MOVED_IN_BAG_RECEIVED,
    SERVER_ITEM_REMOVED_FROM_BAG_RECEIVED,
    SERVER_ITEM_UNEQUIPPED_RECEIVED,
    SERVER_MESSAGE_RECEIVED,
    SOCKET_DISCONNECTED,
    SPELL_CAST_CANCELLED_RECEIVED,
    SPELL_CAST_FAILED_RECEIVED,
    SPELL_CAST_STARTED_RECEIVED,
    TOAST_REQUESTED,
    OUT_UI_LOGOUT_COUNTDOWN_CHANGED,
    TEMPORARY_EFFECT_APPLIED_FOR_PLAYER_RECEIVED,
    TEMPORARY_EFFECT_EXPIRED_FOR_PLAYER_RECEIVED,
    TEMPORARY_EFFECT_APPLIED_FOR_MONSTER_RECEIVED,
    TEMPORARY_EFFECT_EXPIRED_FOR_MONSTER_RECEIVED,
    CAST_EFFECT_RECEIVED,
    OUT_WEATHER_SYNCED,
} from '../constants/EventNames';
import type { MonsterCatalogEntry } from '../ui/store/MonsterDialog.store';
import { serverDialogStore } from '../ui/store/ServerDialog.store';
import { setLogoutSecondsRemaining, type GameWorld } from '../ui/store/ControlsDialog.store';
import {
    Gender,
    SkinColor,
    type CastAoeSpellEventData,
    type CastDirectionalAoeSpellEventData,
    type GroundEffectEventData,
    type GroundStateCellEventData,
    type GroundStateCellRemovedEventData,
    type InitialGameWorldStateEventData,
    type InventorySnapshotEventData,
    type ItemEquippedEventData,
    type ItemUnequippedEventData,
    type MonsterAttackedEventData,
    type MonsterAttackedMonsterEventData,
    type MonsterCastAoeSpellEventData,
    type MonsterCastDirectionalAoeSpellEventData,
    type MonsterDiedEventData,
    type MonsterEnteredRangeEventData,
    type MonsterMovedEventData,
    type MonsterTakeDamageByMonsterEventData,
    type MonsterTakeDamageEventData,
    type NetworkPlayer,
    type NpcEnteredRangeEventData,
    type PlayerAppearanceChangedEventData,
    type PlayerAttackModeChangedEventData,
    type PlayerAttackedMonsterEventData,
    type PlayerAttackedPlayerEventData,
    type PlayerBowStancePerformedEventData,
    type PlayerConnectionStateChangedEventData,
    type PlayerDiedEventData,
    type PlayerIdleDirectionChangedEventData,
    type PlayerMovedEventData,
    type PlayerMovementStateChangedEventData,
    type PlayerPickupPerformedEventData,
    type PlayerReceiveDamageEventData,
    type PlayerResurrectedEventData,
    type PlayerTakeDamageEventData,
    type SpellCastCancelledEventData,
    type SpellCastStartedEventData,
    type SpellEntry,
    type TeleportLoc,
    type TeleportLocSet,
} from '../Types';
import { Direction } from './CoordinateUtils';
import {
    ItemTypes,
    applyItemDirectory,
    effectsFromDirectoryEntries,
    isEquipmentSlot,
    type Effect,
    type EquipmentSlot,
    type InventoryItem,
} from '../constants/Items';
import type { WeatherMode } from '../ui/store/MapDialog.store';
import { collectEquippedItemAppearanceSpriteBasenamesForPrefetch } from './ItemAssets';

function appearanceGenderToClient(g: PlayerGender): Gender {
    return g === PlayerGender.PLAYER_GENDER_FEMALE ? Gender.FEMALE : Gender.MALE;
}

function appearanceSkinToClient(s: PlayerSkinColor): SkinColor {
    switch (s) {
        case PlayerSkinColor.PLAYER_SKIN_COLOR_TANNED:
            return SkinColor.Tanned;
        case PlayerSkinColor.PLAYER_SKIN_COLOR_DARK:
            return SkinColor.Dark;
        default:
            return SkinColor.Light;
    }
}

function clientGenderToProto(g: Gender): PlayerGender {
    return g === Gender.FEMALE ? PlayerGender.PLAYER_GENDER_FEMALE : PlayerGender.PLAYER_GENDER_MALE;
}

function clientSkinToProto(s: SkinColor): PlayerSkinColor {
    switch (s) {
        case SkinColor.Tanned:
            return PlayerSkinColor.PLAYER_SKIN_COLOR_TANNED;
        case SkinColor.Dark:
            return PlayerSkinColor.PLAYER_SKIN_COLOR_DARK;
        default:
            return PlayerSkinColor.PLAYER_SKIN_COLOR_LIGHT;
    }
}

export function weatherModeToProto(mode: WeatherMode): WeatherModeProto {
    switch (mode) {
        case 'dry':
            return WeatherModeProto.WEATHER_MODE_DRY;
        case 'rain-light':
            return WeatherModeProto.WEATHER_MODE_RAIN_LIGHT;
        case 'rain-medium':
            return WeatherModeProto.WEATHER_MODE_RAIN_MEDIUM;
        case 'rain-heavy':
            return WeatherModeProto.WEATHER_MODE_RAIN_HEAVY;
        case 'snow-light':
            return WeatherModeProto.WEATHER_MODE_SNOW_LIGHT;
        case 'snow-medium':
            return WeatherModeProto.WEATHER_MODE_SNOW_MEDIUM;
        case 'snow-heavy':
            return WeatherModeProto.WEATHER_MODE_SNOW_HEAVY;
        default: {
            const _exhaustive: never = mode;
            return _exhaustive;
        }
    }
}

export function weatherModeFromProto(mode: WeatherModeProto): WeatherMode | undefined {
    switch (mode) {
        case WeatherModeProto.WEATHER_MODE_DRY:
            return 'dry';
        case WeatherModeProto.WEATHER_MODE_RAIN_LIGHT:
            return 'rain-light';
        case WeatherModeProto.WEATHER_MODE_RAIN_MEDIUM:
            return 'rain-medium';
        case WeatherModeProto.WEATHER_MODE_RAIN_HEAVY:
            return 'rain-heavy';
        case WeatherModeProto.WEATHER_MODE_SNOW_LIGHT:
            return 'snow-light';
        case WeatherModeProto.WEATHER_MODE_SNOW_MEDIUM:
            return 'snow-medium';
        case WeatherModeProto.WEATHER_MODE_SNOW_HEAVY:
            return 'snow-heavy';
        default:
            return undefined;
    }
}

function inventoryItemFromEntry(entry: InventoryItemEntry): InventoryItem {
    return {
        itemId: entry.itemId,
        itemUid: entry.itemUid.toString(),
        bagX: entry.bagX,
        bagY: entry.bagY,
        quantity: entry.quantity,
        bagZIndex: entry.bagZIndex,
        effectOverrides: effectsFromDirectoryEntries(entry.effectOverrides),
    };
}

function equippedItemsFromEntries(entries: EquippedInventoryItemEntry[]): Partial<Record<EquipmentSlot, InventoryItem>> {
    const equippedItems: Partial<Record<EquipmentSlot, InventoryItem>> = {};
    for (const entry of entries) {
        if (!isEquipmentSlot(entry.slot) || !entry.item) {
            continue;
        }

        equippedItems[entry.slot] = inventoryItemFromEntry(entry.item);
    }
    return equippedItems;
}

function visibleEquippedItemsFromEntries(entries: VisibleEquippedItemEntry[]): Partial<Record<ItemTypes, { itemId: number; effectOverrides?: Effect[] }>> {
    const visibleEquippedItems: Partial<Record<ItemTypes, { itemId: number; effectOverrides?: Effect[] }>> = {};
    for (const entry of entries) {
        if (!isEquipmentSlot(entry.slot)) {
            continue;
        }
        if (!Object.values(ItemTypes).includes(entry.slot as ItemTypes)) {
            continue;
        }

        visibleEquippedItems[entry.slot as ItemTypes] = {
            itemId: entry.itemId,
            effectOverrides: effectsFromDirectoryEntries(entry.effectOverrides),
        };
    }
    return visibleEquippedItems;
}

function effectToProtoIndex(effect: Effect['effect']): number {
    switch (effect) {
        case 'STORM_BRINGER':
            return 0;
        case 'STAR_TWINKLE':
            return 1;
        case 'GLARE':
            return 2;
        case 'GLOW':
            return 3;
        case 'TINT_INVENTORY':
            return 4;
        case 'TINT_APPEARANCE':
            return 5;
    }

    throw new Error(`Unsupported item effect '${effect}'.`);
}

function normalizeTeleportLocs(teleportLocs: InitialGameWorldState['teleportLocs']): TeleportLocSet[] {
    return teleportLocs.map((teleportLoc) => ({
        locs: teleportLoc.locs.map((loc) => ({ x: loc.x, y: loc.y })),
        target: {
            worldId: teleportLoc.target?.worldId ?? '',
            mapName: teleportLoc.target?.mapName ?? '',
            loc: {
                x: teleportLoc.target?.loc?.x ?? 0,
                y: teleportLoc.target?.loc?.y ?? 0,
            },
        },
    }));
}

/**
 * Unique source tile coordinates from `InitialGameWorldState.teleportLocs` (server-authored teleport triggers).
 * Used for debug overlay on the client map.
 */
export function getTeleportSourceCellsFromLocSets(teleportLocs: TeleportLocSet[]): TeleportLoc[] {
    const seen = new Set<string>();
    const out: TeleportLoc[] = [];
    for (const set of teleportLocs) {
        for (const loc of set.locs) {
            const key = `${loc.x},${loc.y}`;
            if (seen.has(key)) {
                continue;
            }
            seen.add(key);
            out.push({ x: loc.x, y: loc.y });
        }
    }
    return out;
}

/**
 * Client WebSocket + protobuf router: authentication, ping, world/monster/inventory packets,
 * and `EventBus` emissions for gameplay consumers (`GameWorld`, stores).
 */
export class NetworkManager {
    private socket: WebSocket | undefined;
    private pingIntervalId: number | undefined;
    private pingIntervalMs = 1000;
    private pingSentAt: number | undefined;
    private latestPing: number | undefined;
    private latestPingVariance: number | undefined;
    private latestGameWorldQueueLength: number | undefined;
    private latestPlayersInMap: number | undefined;
    private currentGameWorldId: string | undefined;
    private nextPingSequence = 1;
    private pendingPingSequence: number | undefined;
    private selfPlayerId: string | undefined;
    private otherPlayersById = new Map<string, NetworkPlayer>();
    /** Authoritative in-view monsters from server packets; GameWorld syncs sprites from this after map load. */
    private monstersInViewById = new Map<string, MonsterEnteredRangeEventData>();
    /** Authoritative in-view NPCs from server packets; GameWorld spawns after map load (join packets can arrive before the scene subscribes to EventBus). */
    private npcsInViewById = new Map<string, NpcEnteredRangeEventData>();
    /** Authoritative in-view ground states from server packets; keyed by "x,y" cell. */
    private groundStatesInViewByCell = new Map<string, GroundStateCellEventData>();
    /**
     * When MonsterMoved arrives before MonstersEnteredRange, there is no in-view row yet; we stash the
     * authoritative destination cell so the subsequent enter packet can spawn at the correct tile (occupancy matches server).
     */
    private pendingMonsterPositionBeforeEnter = new Map<string, { destX: number; destY: number }>();
    /**
     * When PlayerMoved arrives before PlayersEnteredRange, avoid synthesizing a fake `NetworkPlayer` row.
     * Stash the move; merge spawn position on enter and emit `PLAYER_MOVED_RECEIVED` after join (same idea as monsters).
     */
    private pendingPlayerMoveBeforeEnter = new Map<string, Omit<PlayerMovedEventData, 'attackMode'>>();
    private gameWorlds: GameWorld[] = [];
    /** Catalog from `monsters_list` (connect); powers summon/UI. Separate from in-view instances in `monstersInViewById`. */
    private monsters: MonsterCatalogEntry[] = [];
    /** Catalog id → display name from InitialState npc_directory (client maps id to sprite locally). */
    private npcDirectoryByCatalogId = new Map<number, string>();
    private spells: SpellEntry[] = [];
    private authenticateCharacterName = '';
    private authenticateCreation: NocCharacterCreation | undefined;
    private hasSentAuthentication = false;
    private logoutPending = false;
    private logoutIntervalId: ReturnType<typeof setInterval> | undefined;
    private pendingSpawnProtectionForSelf = false;
    private pendingInitialGameWorldState: InitialGameWorldStateEventData | undefined;
    private latestInventorySnapshot: InventorySnapshotEventData | undefined;
    /** Authoritative self HP/max from server; updated by InitialState and hp_updated; used when merging map-only InitialGameWorldState. */
    private lastSelfHp: number | undefined;
    private lastSelfMaxHp: number | undefined;
    /** Snapshot from InitialState for merging into each InitialGameWorldState (map load). */
    private initialStateMergeBase:
        | Pick<
            InitialGameWorldStateEventData,
            | 'playerId'
            | 'movementSpeedMs'
            | 'runMode'
            | 'attackMode'
            | 'attackType'
            | 'allowDashAttack'
            | 'attackRangeCells'
            | 'attackDamage'
            | 'attackSpeedMs'
            | 'attackStunDurationMs'
            | 'castSpeedMs'
            | 'arrowSpeedPxPerSec'
            | 'hp'
            | 'maxHp'
            | 'playerPickupAnimationTimeMs'
            | 'playerBowAnimationDurationMs'
            | 'gender'
            | 'skinColor'
            | 'hairStyleIndex'
            | 'underwearColorIndex'
        >
        | undefined;

    constructor(private readonly networkId: string) {
    }

    public connect(ip: string, port: number, characterName: string, creation?: NocCharacterCreation): Promise<void> {
        return new Promise((resolve, reject) => {
            try {
                this.authenticateCharacterName = characterName.trim();
                this.authenticateCreation = creation;
                const websocketUrl = `ws://${ip}:${port}/ws`;
                const socket = new WebSocket(websocketUrl);
                socket.binaryType = 'arraybuffer';

                this.socket = socket;

                socket.addEventListener('open', () => {
                    runSafeSync('NetworkManager:open', () => {
                        console.log(`[NetworkManager] Connected to ${websocketUrl}`);
                        this.sendAuthentication();
                        resolve();
                    });
                }, { once: true });

                socket.addEventListener('message', (event: MessageEvent) => {
                    try {
                        const latency = serverDialogStore.state.incomingLatency;
                        const fluctuation = serverDialogStore.state.incomingFluctuation;
                        const extra = fluctuation > 0 ? Math.random() * fluctuation : 0;
                        const totalDelay = latency + extra;
                        if (totalDelay > 0) {
                            this.sleep(totalDelay)
                                .then(() => {
                                    runSafeSync('NetworkManager:handleMessage', () => this.handleMessage(event));
                                })
                                .catch((error) => {
                                    console.error('[NetworkManager] message delay scheduling failed', error);
                                });
                        } else {
                            runSafeSync('NetworkManager:handleMessage', () => this.handleMessage(event));
                        }
                    } catch (error) {
                        console.error('[NetworkManager] message listener', error);
                    }
                });

                socket.addEventListener('close', (event: CloseEvent) => {
                    runSafeSync('NetworkManager:close', () => {
                        console.log('[NetworkManager] WebSocket connection closed.');
                        this.clearPingInterval();
                        this.pingSentAt = undefined;
                        this.latestPing = undefined;
                        this.latestPingVariance = undefined;
                        this.latestGameWorldQueueLength = undefined;
                        this.latestPlayersInMap = undefined;
                        this.currentGameWorldId = undefined;
                        this.pendingPingSequence = undefined;
                        this.selfPlayerId = undefined;
                        this.pendingSpawnProtectionForSelf = false;
                        this.pendingInitialGameWorldState = undefined;
                        this.lastSelfHp = undefined;
                        this.lastSelfMaxHp = undefined;
                        EventBus.emit(NOC_STATE_RESET);
                        this.initialStateMergeBase = undefined;
                        this.clearOtherPlayersState();
                        this.clearMonstersInViewState();
                        this.clearNpcsInViewState();
                        this.clearGroundStatesInViewState();
                        this.gameWorlds = [];
                        this.monsters = [];
                        this.spells = [];
                        this.authenticateCharacterName = '';
                        this.authenticateCreation = undefined;
                        this.hasSentAuthentication = false;
                        this.logoutPending = false;
                        this.clearLogoutCountdown();
                        if (this.socket === socket) {
                            this.socket = undefined;
                        }
                        const reason = event.reason;
                        console.log(`[NetworkManager] WebSocket connection closed: ${reason}`);
                        if (reason && reason !== 'Closing connection') {
                            EventBus.emit(SERVER_MESSAGE_RECEIVED, { message: reason });
                        }
                        EventBus.emit(OUT_UI_SET_GAME_WORLDS, []);
                        EventBus.emit(OUT_UI_SET_MONSTERS, []);
                        EventBus.emit(OUT_UI_SET_SPELLS, []);
                        EventBus.emit(SOCKET_DISCONNECTED);
                    });
                });

                socket.addEventListener('error', (event) => {
                    runSafeSync('NetworkManager:error', () => {
                        console.warn(`[NetworkManager] Failed to connect to ${websocketUrl}`, event);
                        if (this.socket === socket) {
                            this.socket = undefined;
                        }
                        EventBus.emit(SERVER_MESSAGE_RECEIVED, {
                            message: `Failed to connect to the server at ${ip}:${port}.`,
                        });
                        reject(new Error(`[NetworkManager] Failed to connect to ${websocketUrl}`));
                    });
                }, { once: true });
            } catch (error) {
                console.warn('[NetworkManager] Failed to create WebSocket connection.', error);
                this.socket = undefined;
                EventBus.emit(SERVER_MESSAGE_RECEIVED, {
                    message: `Failed to connect to the server at ${ip}:${port}.`,
                });
                reject(error);
            }
        });
    }

    public getSocket(): WebSocket | undefined {
        return this.socket;
    }

    public getLatestPing(): number | undefined {
        return this.latestPing;
    }

    public getLatestPingVariance(): number | undefined {
        return this.latestPingVariance;
    }

    public getLatestQueueLengths(): { gameWorldQueueLength: number; playersInMap: number } | undefined {
        if (this.latestGameWorldQueueLength === undefined && this.latestPlayersInMap === undefined) {
            return undefined;
        }
        return {
            gameWorldQueueLength: this.latestGameWorldQueueLength ?? 0,
            playersInMap: this.latestPlayersInMap ?? 0,
        };
    }

    public requestMovement(
        curX: number,
        curY: number,
        destX: number,
        destY: number,
        options: { dashAttack: boolean; monsterId?: string; playerId?: string; attackType?: number },
    ): void {
        if (!this.currentGameWorldId) {
            return;
        }
        const command = ClientMessage.encode({
            payload: {
                $case: 'requestMovement',
                value: {
                    curX,
                    curY,
                    destX,
                    destY,
                    gameWorldId: this.currentGameWorldId,
                    dashAttack: options.dashAttack,
                    monsterId: options.monsterId ? BigInt(options.monsterId) : undefined,
                    playerId: options.playerId ? BigInt(options.playerId) : undefined,
                    attackType: options.attackType,
                },
            },
        }).finish();
        this.sendPacket(command);
    }

    public changePlayerMovementSpeed(movementSpeedMs: number): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'changePlayerMovementSpeedRequest',
                value: { movementSpeedMs },
            },
        }).finish();
        this.sendPacket(command);
    }

    public requestWorldChange(worldId: string, commitImmediately = false, validateTeleport = false): void {
        if (!this.currentGameWorldId) {
            return;
        }
        const command = ClientMessage.encode({
            payload: {
                $case: 'worldChangeRequest',
                value: { worldId, gameWorldId: this.currentGameWorldId, validateTeleport },
            },
        }).finish();
        this.sendPacket(command);
        if (commitImmediately) {
            // Teleport transfers restart immediately, so start treating the target world as authoritative right away.
            this.currentGameWorldId = worldId;
        }
    }

    public sendChatMessage(message: string): void {
        const trimmedMessage = message.trim();
        if (!trimmedMessage) {
            return;
        }

        const command = ClientMessage.encode({
            payload: {
                $case: 'chatMessageSendRequest',
                value: { message: trimmedMessage },
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendWeatherChangeRequest(mode: WeatherMode): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'weatherChangeRequest',
                value: { weather: weatherModeToProto(mode) },
            },
        }).finish();
        this.sendPacket(command);
    }

    public requestPlayerMovementStateChange(runningMode: boolean): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'playerMovementStateChangeRequest',
                value: { runningMode },
            },
        }).finish();
        this.sendPacket(command);
    }

    public requestPlayerAttackModeChange(attackMode: boolean): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'playerAttackModeChangeRequest',
                value: { attackMode },
            },
        }).finish();
        this.sendPacket(command);
    }

    public changePlayerAttackStunDuration(attackStunDurationMs: number): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'changePlayerAttackStunDurationRequest',
                value: { attackStunDurationMs },
            },
        }).finish();
        this.sendPacket(command);
    }

    public changePlayerAttackSpeed(attackSpeedMs: number): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'changePlayerAttackSpeedRequest',
                value: { attackSpeedMs },
            },
        }).finish();
        this.sendPacket(command);
    }

    public changePlayerCastSpeed(castSpeedMs: number): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'changePlayerCastSpeedRequest',
                value: { castSpeedMs },
            },
        }).finish();
        this.sendPacket(command);
    }

    public changePlayerAttackType(attackType: number): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'changePlayerAttackTypeRequest',
                value: { attackType },
            },
        }).finish();
        this.sendPacket(command);
    }

    public changePlayerAllowDashAttack(allowDashAttack: boolean): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'changePlayerAllowDashAttackRequest',
                value: { allowDashAttack },
            },
        }).finish();
        this.sendPacket(command);
    }

    public changePlayerAppearance(
        gender: Gender,
        skinColor: SkinColor,
        hairStyleIndex: number,
        underwearColorIndex: number,
    ): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'changePlayerAppearanceRequest',
                value: {
                    gender: clientGenderToProto(gender),
                    skinColor: clientSkinToProto(skinColor),
                    hairStyleIndex: Math.max(0, Math.min(7, Math.round(hairStyleIndex))),
                    underwearColorIndex: Math.max(0, Math.min(7, Math.round(underwearColorIndex))),
                },
            },
        }).finish();
        this.sendPacket(command);
    }

    public changePlayerAttackRange(attackRangeCells: number): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'changePlayerAttackRangeRequest',
                value: { attackRangeCells },
            },
        }).finish();
        this.sendPacket(command);
    }

    public changePlayerAttackDamage(attackDamage: number): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'changePlayerAttackDamageRequest',
                value: { attackDamage },
            },
        }).finish();
        this.sendPacket(command);
    }

    public requestChangePlayerIdleDirection(direction: number): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'changePlayerIdleDirectionRequest',
                value: { direction },
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendLogoutRequest(): void {
        if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
            return;
        }
        if (this.logoutPending) {
            return;
        }
        this.logoutPending = true;
        const command = ClientMessage.encode({
            payload: {
                $case: 'logoutRequest',
                value: {},
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendMakeCellOccupiedRequest(x: number, y: number): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'makeServerCellOccupiedRequest',
                value: { x, y },
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendPlayerTeleportRequested(x: number, y: number): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'playerTeleportRequested',
                value: { x, y },
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendSummonMonsterRequested(
        sprite: string,
        movementSpeedMs: number,
        direction: number,
        attackType: number,
        allegiance: number,
        stunDurationMs: number,
        maxHp: number,
        attackDamage: number,
        attackSpeedMs: number,
        attackRecoveryMs: number,
        chaseRangeCells: number,
        attackRangeCells: number,
        summonCount: number,
    ): void {
        if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
            return;
        }
        const command = ClientMessage.encode({
            payload: {
                $case: 'summonMonsterRequested',
                value: {
                    sprite,
                    movementSpeedMs,
                    direction,
                    attackType,
                    allegiance,
                    stunDurationMs,
                    maxHp,
                    attackDamage,
                    attackSpeedMs,
                    attackRecoveryMs,
                    chaseRangeCells,
                    attackRangeCells,
                    summonCount,
                },
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendKillAllMonstersRequested(): void {
        if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
            return;
        }
        const command = ClientMessage.encode({
            payload: {
                $case: 'killAllMonstersRequested',
                value: {},
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendSummonNpcRequest(catalogNpcId: number, direction: number): void {
        if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
            return;
        }
        const command = ClientMessage.encode({
            payload: {
                $case: 'summonNpcRequest',
                value: {
                    catalogNpcId,
                    direction,
                },
            },
        }).finish();
        this.sendPacket(command);
    }

    /** Debug only: the server ignores it unless Settings.json nocthemar.debug is true. */
    public sendNocGrantXpRequest(amount: bigint): void {
        if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
            return;
        }
        const command = ClientMessage.encode({
            payload: {
                $case: 'nocGrantXpRequest',
                value: { amount },
            },
        }).finish();
        this.sendPacket(command);
    }

    /** Debug only: the server ignores it unless Settings.json nocthemar.debug is true. Quantity 1..100. */
    public sendNocCreateItemRequest(itemId: number, quantity: number): void {
        this.sendNocPayload({ $case: 'nocCreateItemRequest', value: { itemId, quantity } });
    }

    /** Chooses the class once; refused by the server (NocActionRefused) when one is already chosen. */
    public sendNocChooseClassRequest(classId: number): void {
        this.sendNocPayload({ $case: 'nocChooseClassRequest', value: { classId } });
    }

    public sendNocLearnSkillRequest(skillId: number): void {
        this.sendNocPayload({ $case: 'nocLearnSkillRequest', value: { skillId } });
    }

    /** Debug only: the server ignores it unless Settings.json nocthemar.debug is true. */
    public sendNocRespecRequest(): void {
        this.sendNocPayload({ $case: 'nocRespecRequest', value: {} });
    }

    private sendNocPayload(payload: NonNullable<Parameters<typeof ClientMessage.encode>[0]['payload']>): void {
        if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
            return;
        }
        this.sendPacket(ClientMessage.encode({ payload }).finish());
    }

    public sendKillAllNpcsRequest(): void {
        if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
            return;
        }
        const command = ClientMessage.encode({
            payload: {
                $case: 'killAllNpcsRequest',
                value: {},
            },
        }).finish();
        this.sendPacket(command);
    }

    public disconnect(): void {
        runSafeSync('NetworkManager:disconnect', () => {
            if (!this.socket) {
                return;
            }

            this.clearPingInterval();
            this.clearLogoutCountdown();
            this.pendingPingSequence = undefined;
            this.currentGameWorldId = undefined;
            this.selfPlayerId = undefined;
            this.lastSelfHp = undefined;
            this.lastSelfMaxHp = undefined;
            EventBus.emit(NOC_STATE_RESET);
            this.initialStateMergeBase = undefined;
            this.latestInventorySnapshot = undefined;
            this.clearOtherPlayersState();
            this.clearMonstersInViewState();
            this.clearNpcsInViewState();
            this.clearGroundStatesInViewState();
            this.gameWorlds = [];
            this.monsters = [];
            this.npcDirectoryByCatalogId.clear();
            this.spells = [];
            EventBus.emit(OUT_UI_SET_GAME_WORLDS, []);
            EventBus.emit(OUT_UI_SET_MONSTERS, []);
            EventBus.emit(OUT_UI_SET_NPC_DIRECTORY, []);
            EventBus.emit(OUT_UI_SET_SPELLS, []);
            this.hasSentAuthentication = false;
            this.logoutPending = false;
            this.socket.close();
            this.socket = undefined;
        });
    }

    private clearLogoutCountdown(): void {
        if (this.logoutIntervalId) {
            clearInterval(this.logoutIntervalId);
            this.logoutIntervalId = undefined;
        }
        this.syncLogoutCountdownUi(undefined);
    }

    private syncLogoutCountdownUi(secondsRemaining: number | undefined): void {
        setLogoutSecondsRemaining(secondsRemaining);
        EventBus.emit(OUT_UI_LOGOUT_COUNTDOWN_CHANGED, { secondsLeft: secondsRemaining });
    }

    public cancelLogout(): void {
        if (!this.logoutPending || !this.logoutIntervalId) {
            return;
        }
        this.clearLogoutCountdown();
        this.logoutPending = false;
        this.sendLogoutCancelled();
    }

    private sendLogoutCancelled(): void {
        if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
            return;
        }
        const command = ClientMessage.encode({
            payload: {
                $case: 'logoutCancelledRequest',
                value: {},
            },
        }).finish();
        this.sendPacket(command);
    }

    public getOtherPlayersState(): NetworkPlayer[] {
        return Array.from(this.otherPlayersById.values());
    }

    public getMonstersInViewState(): MonsterEnteredRangeEventData[] {
        return Array.from(this.monstersInViewById.values());
    }

    public getNpcsInViewState(): NpcEnteredRangeEventData[] {
        return Array.from(this.npcsInViewById.values());
    }

    public getGroundStatesInViewState(): GroundStateCellEventData[] {
        return Array.from(this.groundStatesInViewByCell.values());
    }

    public getLatestInventorySnapshot(): InventorySnapshotEventData | undefined {
        return this.latestInventorySnapshot;
    }

    private clearOtherPlayersState(): void {
        this.otherPlayersById.clear();
        this.pendingPlayerMoveBeforeEnter.clear();
    }

    private clearMonstersInViewState(): void {
        this.monstersInViewById.clear();
        this.pendingMonsterPositionBeforeEnter.clear();
    }

    private clearNpcsInViewState(): void {
        this.npcsInViewById.clear();
    }

    private clearGroundStatesInViewState(): void {
        this.groundStatesInViewByCell.clear();
    }

    private getGroundStateCellKey(x: number, y: number): string {
        return `${x},${y}`;
    }

    public getGameWorlds(): GameWorld[] {
        return [...this.gameWorlds];
    }

    public getWorldById(worldId: string): GameWorld | undefined {
        return this.gameWorlds.find((world) => world.id === worldId);
    }

    public getSpellById(spellId: number): SpellEntry | undefined {
        return this.spells.find((spell) => spell.id === spellId);
    }

    public clearPendingInitialGameWorldState(): void {
        this.pendingInitialGameWorldState = undefined;
    }

    public getAndClearPendingInitialGameWorldState(): InitialGameWorldStateEventData | undefined {
        const pendingInitialGameWorldState = this.pendingInitialGameWorldState;
        this.pendingInitialGameWorldState = undefined;
        return pendingInitialGameWorldState;
    }

    public sendSpellCastStartRequest(spellId: number): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'spellCastStartRequest',
                value: { spellId },
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendSpellCastCancelRequest(): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'spellCastCancelRequest',
                value: {},
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendSpellCastRequest(x: number, y: number, aimAssistPlayerId?: bigint, aimAssistMonsterId?: bigint): void {
        const value: SpellCastRequest = { x, y };
        if (aimAssistPlayerId !== undefined) {
            value.playerId = aimAssistPlayerId;
        }
        if (aimAssistMonsterId !== undefined) {
            value.monsterId = aimAssistMonsterId;
        }
        const command = ClientMessage.encode({
            payload: {
                $case: 'spellCastRequest',
                value,
            },
        }).finish();
        this.sendPacket(command);
    }

    private startPingInterval(): void {
        this.clearPingInterval();
        this.pingIntervalId = window.setInterval(() => {
            runSafeSync('NetworkManager:pingInterval', () => this.sendPing());
        }, this.pingIntervalMs);
    }

    private clearPingInterval(): void {
        if (this.pingIntervalId) {
            clearInterval(this.pingIntervalId);
            this.pingIntervalId = undefined;
        }
    }

    private sendPing(): void {
        if (!this.socket || this.socket.readyState !== WebSocket.OPEN || this.pingSentAt !== undefined) {
            return;
        }

        const sequence = this.nextPingSequence++;
        const command = ClientMessage.encode({
            payload: {
                $case: 'pingRequest',
                value: {
                    sequence,
                },
            },
        }).finish();

        this.pendingPingSequence = sequence;
        this.pingSentAt = performance.now();
        this.sendPacket(command);
    }

    private sleep(ms: number): Promise<void> {
        return new Promise((resolve) => setTimeout(resolve, ms));
    }

    private sendAuthentication(): void {
        this.hasSentAuthentication = true;
        const command = encodeAuthenticateRequest(this.networkId, this.authenticateCharacterName, this.authenticateCreation);
        this.sendPacket(command, true);
    }

    private sendPacket(command: Uint8Array, allowBeforeAuthentication = false): void {
        if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
            return;
        }
        if (!allowBeforeAuthentication && !this.hasSentAuthentication) {
            return;
        }
        const latency = serverDialogStore.state.outgoingLatency;
        const fluctuation = serverDialogStore.state.outgoingFluctuation;
        const extra = fluctuation > 0 ? Math.random() * fluctuation : 0;
        const totalDelay = latency + extra;
        const sendNow = () => {
            runSafeSync('NetworkManager:sendPacket', () => {
                if (this.socket && this.socket.readyState === WebSocket.OPEN) {
                    this.socket.send(command);
                }
            });
        };
        if (totalDelay > 0) {
            this.sleep(totalDelay)
                .then(() => {
                    sendNow();
                })
                .catch((error) => {
                    console.error('[NetworkManager] sendPacket delayed send failed', error);
                });
        } else {
            sendNow();
        }
    }

    private handleMessage(event: MessageEvent): void {
        if (!(event.data instanceof ArrayBuffer)) {
            return;
        }

        try {
            const message = ServerMessage.decode(new Uint8Array(event.data));
            switch (message.payload?.$case) {
                case 'pingResponse':
                    this.handlePingResponse(message.payload.value);
                    break;
                case 'initialGameWorldState':
                    this.handleInitialGameWorldState(message.payload.value);
                    break;
                case 'playerTeleported':
                    this.handlePlayerTeleported(message.payload.value);
                    break;
                case 'resetPosition':
                    this.handleResetPosition(message.payload.value);
                    break;
                case 'positionCorrected':
                    this.handlePositionCorrected(message.payload.value);
                    break;
                case 'playersEnteredRange':
                    this.handlePlayersEnteredRange(message.payload.value);
                    break;
                case 'playersLeftRange':
                    this.handlePlayersLeftRange(message.payload.value);
                    break;
                case 'groundStatesEnteredRange':
                    this.handleGroundStatesEnteredRange(message.payload.value);
                    break;
                case 'groundStatesLeftRange':
                    this.handleGroundStatesLeftRange(message.payload.value);
                    break;
                case 'playerMoved':
                    this.handlePlayerMoved(message.payload.value);
                    break;
                case 'playerMovementStateChanged':
                    this.handlePlayerMovementStateChanged(message.payload.value);
                    break;
                case 'playerAttackModeChanged':
                    this.handlePlayerAttackModeChanged(message.payload.value);
                    break;
                case 'playerIdleDirectionChanged':
                    this.handlePlayerIdleDirectionChanged(message.payload.value);
                    break;
                case 'playerAppearanceChanged':
                    this.handlePlayerAppearanceChanged(message.payload.value);
                    break;
                case 'playerDisconnected':
                    this.handlePlayerDisconnected(message.payload.value);
                    break;
                case 'playerReconnected':
                    this.handlePlayerReconnected(message.payload.value);
                    break;
                case 'sendMessage':
                    this.handleSendMessage(message.payload.value);
                    break;
                case 'chatMessageReceived':
                    this.handleChatMessageReceived(message.payload.value);
                    break;
                case 'weatherChanged':
                    this.handleWeatherChanged(message.payload.value);
                    break;
                case 'playerParalyzed':
                    this.handlePlayerParalyzed(message.payload.value);
                    break;
                case 'logoutResponse':
                    this.handleLogoutResponse(message.payload.value);
                    break;
                case 'logoutCancelled':
                    this.handleLogoutCancelled();
                    break;
                case 'worldsList':
                    this.handleWorldsList(message.payload.value);
                    break;
                case 'monstersList':
                    this.handleMonstersList(message.payload.value);
                    break;
                case 'initialState':
                    this.handleInitialState(message.payload.value);
                    break;
                case 'itemAddedToBag':
                    this.handleItemAddedToBag(message.payload.value);
                    break;
                case 'itemRemovedFromBag':
                    this.handleItemRemovedFromBag(message.payload.value);
                    break;
                case 'itemMovedInBag':
                    this.handleItemMovedInBag(message.payload.value);
                    break;
                case 'itemEquipped':
                    this.handleItemEquipped(message.payload.value);
                    break;
                case 'itemUnequipped':
                    this.handleItemUnequipped(message.payload.value);
                    break;
                case 'castDirectionalAoeSpell':
                    this.handleCastDirectionalAoeSpell(message.payload.value);
                    break;
                case 'monstersEnteredRange':
                    this.handleMonstersEnteredRange(message.payload.value);
                    break;
                case 'monstersLeftRange':
                    this.handleMonstersLeftRange(message.payload.value);
                    break;
                case 'npcsEnteredRange':
                    this.handleNpcsEnteredRange(message.payload.value);
                    break;
                case 'npcsLeftRange':
                    this.handleNpcsLeftRange(message.payload.value);
                    break;
                case 'monsterMoved':
                    this.handleMonsterMoved(message.payload.value);
                    break;
                case 'monsterAttacked':
                    this.handleMonsterAttacked(message.payload.value);
                    break;
                case 'monsterAttackedMonster':
                    this.handleMonsterAttackedMonster(message.payload.value);
                    break;
                case 'playerReceiveDamage':
                    this.handlePlayerReceiveDamage(message.payload.value);
                    break;
                case 'playerTakeDamage':
                    this.handlePlayerTakeDamage(message.payload.value);
                    break;
                case 'hpUpdated':
                    this.handleHpUpdated(message.payload.value);
                    break;
                case 'nocStateUpdated':
                    this.handleNocSnapshot(message.payload.value.state);
                    break;
                case 'nocActionRefused':
                    EventBus.emit(NOC_ACTION_REFUSED_RECEIVED, { reason: message.payload.value.reason });
                    break;
                case 'playerDied':
                    this.handlePlayerDied(message.payload.value);
                    break;
                case 'playerResurrected':
                    this.handlePlayerResurrected(message.payload.value);
                    break;
                case 'monsterTakeDamage':
                    this.handleMonsterTakeDamage(message.payload.value);
                    break;
                case 'monsterTakeDamageByMonster':
                    this.handleMonsterTakeDamageByMonster(message.payload.value);
                    break;
                case 'monsterDied':
                    this.handleMonsterDied(message.payload.value);
                    break;
                case 'playerAttackedMonster':
                    this.handlePlayerAttackedMonster(message.payload.value);
                    break;
                case 'playerAttackedPlayer':
                    this.handlePlayerAttackedPlayer(message.payload.value);
                    break;
                case 'playerPickupPerformed':
                    this.handlePlayerPickupPerformed(message.payload.value);
                    break;
                case 'playerBowStancePerformed':
                    this.handlePlayerBowStancePerformed(message.payload.value);
                    break;
                case 'spellCastStarted':
                    this.handleSpellCastStarted(message.payload.value);
                    break;
                case 'spellCastCancelled':
                    this.handleSpellCastCancelled(message.payload.value);
                    break;
                case 'spellCastFailed':
                    this.handleSpellCastFailed(message.payload.value);
                    break;
                case 'castAoeSpell':
                    this.handleCastAoeSpell(message.payload.value);
                    break;
                case 'monsterCastAoeSpell':
                    this.handleMonsterCastAoeSpell(message.payload.value);
                    break;
                case 'monsterCastDirectionalAoeSpell':
                    this.handleMonsterCastDirectionalAoeSpell(message.payload.value);
                    break;
                case 'spawnProtectionEnabled':
                    this.handleSpawnProtectionEnabled(message.payload.value);
                    break;
                case 'spawnProtectionDisabled':
                    this.handleSpawnProtectionDisabled(message.payload.value);
                    break;
                case 'temporaryEffectApplied':
                    this.handleTemporaryEffectApplied(message.payload.value);
                    break;
                case 'temporaryEffectExpired':
                    this.handleTemporaryEffectExpired(message.payload.value);
                    break;
                case 'castEffect':
                    this.handleCastEffect(message.payload.value);
                    break;
            }
        } catch (error) {
            console.warn('[NetworkManager] Failed to handle WebSocket message.', error);
        }
    }

    private handlePingResponse(pingResponse: PingResponse): void {
        if (this.pingSentAt === undefined || this.pendingPingSequence !== pingResponse.sequence) {
            return;
        }
        this.latestPing = Math.round(performance.now() - this.pingSentAt);
        this.latestPingVariance = pingResponse.pingVariance;
        this.latestGameWorldQueueLength = pingResponse.gameWorldQueueLength;
        this.latestPlayersInMap = pingResponse.playersInMap;
        this.pendingPingSequence = undefined;
        this.pingSentAt = undefined;
    }

    private handleInitialState(data: InitialState): void {
        this.selfPlayerId = String(data.playerId);
        this.handleNocSnapshot(data.noc);
        if (data.nocCatalog) {
            EventBus.emit(NOC_CATALOG_RECEIVED, toCatalogModel(data.nocCatalog));
        }
        this.npcDirectoryByCatalogId.clear();
        for (const row of data.npcDirectory) {
            this.npcDirectoryByCatalogId.set(row.id, row.name);
        }
        EventBus.emit(
            OUT_UI_SET_NPC_DIRECTORY,
            data.npcDirectory.map((row) => ({ id: row.id, name: row.name })),
        );
        applyItemDirectory(data.itemsDirectory);
        this.latestInventorySnapshot = {
            bagItems: data.bagItems
                .map((entry) => inventoryItemFromEntry(entry))
                .sort((a, b) => (a.bagZIndex ?? 0) - (b.bagZIndex ?? 0)),
            equippedItems: equippedItemsFromEntries(data.equippedItems),
        };
        EventBus.emit(SERVER_INVENTORY_SNAPSHOT_RECEIVED, this.latestInventorySnapshot);
        if (LOAD_PLAYER_ITEM_APPEARANCE_ASSETS_ON_DEMAND) {
            const prefetchGender = appearanceGenderToClient(data.gender);
            const spriteNames = collectEquippedItemAppearanceSpriteBasenamesForPrefetch(
                this.latestInventorySnapshot.equippedItems,
                prefetchGender,
            );
            if (spriteNames.length > 0) {
                EventBus.emit(PLAYER_ITEM_APPEARANCE_PREFETCH_REQUESTED, { spriteNames });
            }
        }
        if (data.spells.length > 0) {
            this.spells = data.spells.map((spell) => {
                const entry: SpellEntry = {
                    id: spell.id,
                    name: spell.name,
                    aoeRadius: spell.aoeRadius,
                    projectileSpeed: spell.projectileSpeed,
                    emissionSteps: spell.emissionSteps,
                    startRadius: spell.startRadius,
                    endRadius: spell.endRadius,
                    startShards: spell.startShards,
                    endShards: spell.endShards,
                    durationMs: spell.durationMs,
                    projectileDistancePx: spell.projectileDistancePx,
                    aimAssist: spell.aimAssist,
                };
                if (spell.damageType !== undefined) {
                    entry.damageType = spell.damageType;
                }
                if (spell.temporaryEffects?.length) {
                    entry.temporaryEffects = spell.temporaryEffects.map((fx) => ({
                        type: fx.type,
                        durationMs: fx.durationMs,
                        group: fx.group,
                    }));
                }
                return entry;
            });
            EventBus.emit(OUT_UI_SET_SPELLS, [...this.spells]);
        }

        this.lastSelfHp = data.hp;
        this.lastSelfMaxHp = data.maxHp;

        if (data.pingIntervalMs > 0) {
            this.pingIntervalMs = data.pingIntervalMs;
            this.startPingInterval();
            this.sendPing();
        }

        const runMode = data.runningMode;
        this.initialStateMergeBase = {
            playerId: this.selfPlayerId,
            movementSpeedMs: data.movementSpeedMs,
            runMode,
            attackMode: data.attackMode,
            attackType: data.attackType >= 0 && data.attackType <= 3 ? data.attackType : undefined,
            allowDashAttack: data.allowDashAttack,
            attackRangeCells: data.attackRangeCells > 0 ? data.attackRangeCells : undefined,
            attackDamage: data.attackDamage > 0 ? data.attackDamage : undefined,
            attackSpeedMs: data.attackSpeedMs > 0 ? data.attackSpeedMs : undefined,
            attackStunDurationMs:
                data.attackStunDurationMs >= 100 && data.attackStunDurationMs <= 2000
                    ? data.attackStunDurationMs
                    : undefined,
            castSpeedMs:
                data.castSpeedMs >= 200 && data.castSpeedMs <= 2000 ? data.castSpeedMs : undefined,
            arrowSpeedPxPerSec: data.arrowSpeedPxPerSec > 0 ? data.arrowSpeedPxPerSec : undefined,
            hp: data.hp,
            maxHp: data.maxHp,
            playerPickupAnimationTimeMs: data.playerPickupAnimationTimeMs,
            playerBowAnimationDurationMs: data.playerBowAnimationDurationMs,
            gender: appearanceGenderToClient(data.gender),
            skinColor: appearanceSkinToClient(data.skinColor),
            hairStyleIndex: Math.max(0, Math.min(7, data.hairStyleIndex)),
            underwearColorIndex: Math.max(0, Math.min(7, data.underwearColorIndex)),
        };

        if (data.baseMovementSpeedMs > 0) {
            EventBus.emit(OUT_UI_SET_MOVEMENT_SPEED, data.baseMovementSpeedMs);
        }
        EventBus.emit(OUT_UI_SET_RUN_MODE, runMode);
        EventBus.emit(OUT_UI_SET_ATTACK_MODE, data.attackMode);
        if (data.attackType >= 0 && data.attackType <= 3) {
            EventBus.emit(OUT_UI_SET_ATTACK_TYPE, data.attackType);
        }
        EventBus.emit(OUT_UI_SET_ALLOW_DASH_ATTACK, data.allowDashAttack);
        if (data.attackRangeCells > 0) {
            EventBus.emit(OUT_UI_SET_ATTACK_RANGE, data.attackRangeCells);
        }
        if (data.attackDamage > 0) {
            EventBus.emit(OUT_UI_SET_DAMAGE, data.attackDamage);
        }
        if (data.attackSpeedMs > 0) {
            EventBus.emit(OUT_UI_SET_ATTACK_SPEED_MS, data.attackSpeedMs);
        }
        if (data.attackStunDurationMs >= 100 && data.attackStunDurationMs <= 2000) {
            EventBus.emit(OUT_UI_SET_STUN_DURATION_MS, data.attackStunDurationMs);
        }
        if (data.castSpeedMs >= 200 && data.castSpeedMs <= 2000) {
            EventBus.emit(OUT_UI_SET_CAST_SPEED, data.castSpeedMs);
        }

        const gender = appearanceGenderToClient(data.gender);
        const skinColor = appearanceSkinToClient(data.skinColor);
        const hairIdx = Math.max(0, Math.min(7, data.hairStyleIndex));
        const underwearIdx = Math.max(0, Math.min(7, data.underwearColorIndex));
        EventBus.emit(OUT_UI_SET_GENDER, gender);
        EventBus.emit(OUT_UI_SET_SKIN_COLOR, skinColor);
        EventBus.emit(OUT_UI_SET_HAIR_STYLE, hairIdx);
        EventBus.emit(OUT_UI_SET_UNDERWEAR_COLOR, underwearIdx);
    }

    private handleItemAddedToBag(data: ItemAddedToBag): void {
        if (!data.item) {
            return;
        }

        EventBus.emit(SERVER_ITEM_ADDED_TO_BAG_RECEIVED, {
            item: inventoryItemFromEntry(data.item),
        });
    }

    private handleItemRemovedFromBag(data: ItemRemovedFromBag): void {
        EventBus.emit(SERVER_ITEM_REMOVED_FROM_BAG_RECEIVED, {
            itemUid: data.itemUid.toString(),
        });
    }

    private handleItemMovedInBag(data: ItemMovedInBag): void {
        EventBus.emit(SERVER_ITEM_MOVED_IN_BAG_RECEIVED, {
            itemUid: data.itemUid.toString(),
            bagX: data.bagX,
            bagY: data.bagY,
            bagZIndex: data.bagZIndex,
        });
    }

    private handleItemEquipped(data: ItemEquipped): void {
        if (!data.equippedItem || !data.equippedItem.item || !isEquipmentSlot(data.equippedItem.slot)) {
            return;
        }

        const payload: ItemEquippedEventData = {
            playerId: data.playerId.toString(),
            slot: data.equippedItem.slot,
            item: inventoryItemFromEntry(data.equippedItem.item),
        };
        if (payload.playerId === this.selfPlayerId) {
            EventBus.emit(SERVER_ITEM_EQUIPPED_RECEIVED, payload);
            return;
        }

        const existing = this.otherPlayersById.get(payload.playerId);
        if (existing && Object.values(ItemTypes).includes(payload.slot as ItemTypes)) {
            this.otherPlayersById.set(payload.playerId, {
                ...existing,
                visibleEquippedItems: {
                    ...existing.visibleEquippedItems,
                    [payload.slot as ItemTypes]: {
                        itemId: payload.item.itemId,
                        effectOverrides: payload.item.effectOverrides,
                    },
                },
            });
        }
        EventBus.emit(REMOTE_PLAYER_ITEM_EQUIPPED_RECEIVED, payload);
    }

    private handleItemUnequipped(data: ItemUnequipped): void {
        if (!isEquipmentSlot(data.slot)) {
            return;
        }

        const payload: ItemUnequippedEventData = {
            playerId: data.playerId.toString(),
            slot: data.slot,
            itemUid: data.itemUid.toString(),
        };
        if (payload.playerId === this.selfPlayerId) {
            EventBus.emit(SERVER_ITEM_UNEQUIPPED_RECEIVED, payload);
            return;
        }

        const existing = this.otherPlayersById.get(payload.playerId);
        if (existing && Object.values(ItemTypes).includes(payload.slot as ItemTypes)) {
            const nextVisibleEquippedItems = { ...existing.visibleEquippedItems };
            delete nextVisibleEquippedItems[payload.slot as ItemTypes];
            this.otherPlayersById.set(payload.playerId, {
                ...existing,
                visibleEquippedItems: nextVisibleEquippedItems,
            });
        }
        EventBus.emit(REMOTE_PLAYER_ITEM_UNEQUIPPED_RECEIVED, payload);
    }

    private handleInitialGameWorldState(data: InitialGameWorldState): void {
        const base = this.initialStateMergeBase;
        if (!base) {
            console.warn('[NetworkManager] InitialGameWorldState received before InitialState.');
            return;
        }
        this.currentGameWorldId = data.gameWorldId;
        this.clearOtherPlayersState();
        this.clearMonstersInViewState();
        this.clearNpcsInViewState();
        this.clearGroundStatesInViewState();
        const runMode = base.runMode;
        const weather = weatherModeFromProto(data.weather);
        if (weather === undefined) {
            console.warn('[NetworkManager] InitialGameWorldState has unrecognized weather', data.weather);
        }
        const initialGameWorldStateEventData: InitialGameWorldStateEventData = {
            gameWorldId: data.gameWorldId,
            mapName: data.mapName,
            musicFile: data.musicFile || undefined,
            playerX: data.playerX,
            playerY: data.playerY,
            playerId: base.playerId,
            movementSpeedMs: base.movementSpeedMs,
            runMode,
            attackMode: base.attackMode,
            attackType: base.attackType,
            allowDashAttack: base.allowDashAttack,
            teleportLocs: normalizeTeleportLocs(data.teleportLocs),
            attackRangeCells: base.attackRangeCells,
            attackDamage: base.attackDamage,
            attackSpeedMs: base.attackSpeedMs,
            attackStunDurationMs: base.attackStunDurationMs,
            castSpeedMs: base.castSpeedMs,
            arrowSpeedPxPerSec: base.arrowSpeedPxPerSec,
            hp: this.lastSelfHp ?? base.hp,
            maxHp: this.lastSelfMaxHp ?? base.maxHp,
            playerPickupAnimationTimeMs: base.playerPickupAnimationTimeMs,
            playerBowAnimationDurationMs: base.playerBowAnimationDurationMs,
            dead: data.dead,
            playerDirection: data.playerDirection,
            weather,
            gender: base.gender,
            skinColor: base.skinColor,
            hairStyleIndex: base.hairStyleIndex,
            underwearColorIndex: base.underwearColorIndex,
        };
        this.pendingInitialGameWorldState = initialGameWorldStateEventData;
        EventBus.emit(INITIAL_GAME_WORLD_STATE_RECEIVED, initialGameWorldStateEventData);
    }

    private handleWorldsList(data: WorldsList): void {
        this.gameWorlds = data.worlds.map((world) => ({
            id: world.id,
            name: world.name,
            map: world.map,
        }));
        EventBus.emit(OUT_UI_SET_GAME_WORLDS, this.getGameWorlds());
    }

    private handleMonstersList(data: MonstersList): void {
        this.monsters = data.monsters.map((m) => ({
            name: m.name,
            sprite: m.sprite,
        }));
        EventBus.emit(OUT_UI_SET_MONSTERS, [...this.monsters]);
    }

    private handleMonstersEnteredRange(data: MonstersEnteredRange): void {
        const payload: MonsterEnteredRangeEventData[] = data.monsters.map((m) => ({
            monsterId: m.monsterId.toString(),
            sprite: m.sprite,
            x: m.x,
            y: m.y,
            state: m.state,
            name: m.name,
            rangedAttack: m.rangedAttack,
            hp: m.hp,
            maxHp: m.maxHp,
            dead: m.dead,
            corpseDecayTimeLeftMs: m.corpseDecayTimeLeftMs,
            direction: m.direction,
            movementSpeedMs: m.movementSpeedMs,
            attackSpeedMs: m.attackSpeedMs,
            attackDamage: m.attackDamage,
            allegiance: m.allegiance,
            attackType: m.attackType,
            activeTemporaryEffects: m.activeTemporaryEffects?.length ? [...m.activeTemporaryEffects] : [],
        }));
        if (payload.length === 0) {
            return;
        }
        const mergedPayload: MonsterEnteredRangeEventData[] = [];
        for (const entry of payload) {
            const pending = this.pendingMonsterPositionBeforeEnter.get(entry.monsterId);
            const merged: MonsterEnteredRangeEventData = pending
                ? { ...entry, x: pending.destX, y: pending.destY }
                : entry;
            this.monstersInViewById.set(entry.monsterId, merged);
            mergedPayload.push(merged);
            if (pending) {
                this.pendingMonsterPositionBeforeEnter.delete(entry.monsterId);
            }
        }
        EventBus.emit(MONSTER_ENTERED_RANGE_RECEIVED, mergedPayload);
    }

    private handleMonstersLeftRange(data: MonstersLeftRange): void {
        const ids = data.monsterIds.map((id) => id.toString());
        if (ids.length === 0) {
            return;
        }
        for (const id of ids) {
            this.monstersInViewById.delete(id);
            this.pendingMonsterPositionBeforeEnter.delete(id);
        }
        EventBus.emit(MONSTERS_LEFT_RANGE_RECEIVED, ids);
    }

    private handleNpcsEnteredRange(data: NpcsEnteredRange): void {
        const payload: NpcEnteredRangeEventData[] = [];
        for (const n of data.npcs) {
            const catalogNpcId = n.catalogNpcId;
            const displayName = this.npcDirectoryByCatalogId.get(catalogNpcId) ?? `NPC ${catalogNpcId}`;
            payload.push({
                npcId: n.npcId.toString(),
                catalogNpcId,
                x: n.x,
                y: n.y,
                direction: n.direction,
                displayName,
            });
        }
        if (payload.length === 0) {
            return;
        }
        for (const entry of payload) {
            this.npcsInViewById.set(entry.npcId, entry);
        }
        EventBus.emit(NPC_ENTERED_RANGE_RECEIVED, payload);
    }

    private handleNpcsLeftRange(data: NpcsLeftRange): void {
        const ids = data.npcIds.map((id) => id.toString());
        if (ids.length === 0) {
            return;
        }
        for (const id of ids) {
            this.npcsInViewById.delete(id);
        }
        EventBus.emit(NPCS_LEFT_RANGE_RECEIVED, ids);
    }

    private handleGroundStatesEnteredRange(data: GroundStatesEnteredRange): void {
        const batch: GroundStateCellEventData[] = [];
        for (const state of data.states) {
            const x = state.loc?.x;
            const y = state.loc?.y;
            if (x === undefined || y === undefined) {
                continue;
            }

            const key = this.getGroundStateCellKey(x, y);
            const existing = this.groundStatesInViewByCell.get(key);
            const effectsById = new Map<string, GroundEffectEventData>();
            for (const existingEffect of existing?.effects ?? []) {
                effectsById.set(existingEffect.groundEffectId, existingEffect);
            }

            for (const effect of state.effects) {
                effectsById.set(effect.groundEffectId.toString(), {
                    groundEffectId: effect.groundEffectId.toString(),
                    effectType: effect.effectType,
                });
            }

            const mergedState: GroundStateCellEventData = {
                x,
                y,
                effects: Array.from(effectsById.values()),
                groundItem: state.groundItem
                    ? {
                        itemId: state.groundItem.itemId,
                        itemUid: state.groundItem.itemUid.toString(),
                        quantity: state.groundItem.quantity ?? 1,
                        effectOverrides: effectsFromDirectoryEntries(state.groundItem.effectOverrides),
                    }
                    : existing?.groundItem,
            };
            this.groundStatesInViewByCell.set(key, mergedState);
            batch.push(mergedState);
        }

        if (batch.length > 0) {
            EventBus.emit(GROUND_STATES_ENTERED_RANGE_RECEIVED, batch);
        }
    }

    private handleGroundStatesLeftRange(data: GroundStatesLeftRange): void {
        const batch: GroundStateCellRemovedEventData[] = [];
        for (const state of data.states) {
            const x = state.loc?.x;
            const y = state.loc?.y;
            if (x === undefined || y === undefined) {
                continue;
            }

            const key = this.getGroundStateCellKey(x, y);
            const existing = this.groundStatesInViewByCell.get(key);
            const removedIds = state.groundEffectIds.map((id) => id.toString());
            const removedGroundItemUid = state.groundItemUid !== undefined
                ? state.groundItemUid.toString()
                : undefined;
            if (removedIds.length === 0 && removedGroundItemUid === undefined) {
                continue;
            }

            if (existing) {
                const removedIdSet = new Set(removedIds);
                const remainingEffects = existing.effects.filter((effect) => !removedIdSet.has(effect.groundEffectId));
                const remainingGroundItem = existing.groundItem?.itemUid === removedGroundItemUid
                    ? undefined
                    : existing.groundItem;
                if (remainingEffects.length === 0 && !remainingGroundItem) {
                    this.groundStatesInViewByCell.delete(key);
                } else {
                    this.groundStatesInViewByCell.set(key, {
                        ...existing,
                        effects: remainingEffects,
                        groundItem: remainingGroundItem,
                    });
                }
            }

            batch.push({
                x,
                y,
                groundEffectIds: removedIds,
                ...(removedGroundItemUid !== undefined && { groundItemUid: removedGroundItemUid }),
            });
        }

        if (batch.length > 0) {
            EventBus.emit(GROUND_STATES_LEFT_RANGE_RECEIVED, batch);
        }
    }

    private handleMonsterMoved(data: MonsterMoved): void {
        const monsterId = data.monsterId.toString();
        const movementSpeedMs = data.movementSpeedMs;
        const existing = this.monstersInViewById.get(monsterId);
        if (existing) {
            this.monstersInViewById.set(monsterId, {
                ...existing,
                x: data.destX,
                y: data.destY,
                state: existing.dead ? existing.state : MonsterEntityState.MONSTER_ENTITY_STATE_MOVE,
                direction: data.direction,
                movementSpeedMs,
            });
        } else {
            this.pendingMonsterPositionBeforeEnter.set(monsterId, { destX: data.destX, destY: data.destY });
        }
        const eventData: MonsterMovedEventData = {
            monsterId,
            curX: data.curX,
            curY: data.curY,
            destX: data.destX,
            destY: data.destY,
            movementSpeedMs,
            direction: data.direction,
        };
        EventBus.emit(MONSTER_MOVED_RECEIVED, eventData);
    }

    private handleMonsterAttacked(data: MonsterAttacked): void {
        const monsterId = data.monsterId.toString();
        this.markMonsterAsAttacking(monsterId);
        EventBus.emit(MONSTER_ATTACKED_RECEIVED, {
            monsterId,
            direction: data.direction,
            attackSpeedMs: data.attackSpeedMs,
            rangedAttack: data.rangedAttack,
            targetPlayerId: data.targetPlayerId.toString(),
            worldX: data.worldX,
            worldY: data.worldY,
        } satisfies MonsterAttackedEventData);
    }

    private handleMonsterAttackedMonster(data: MonsterAttackedMonster): void {
        const monsterId = data.monsterId.toString();
        this.markMonsterAsAttacking(monsterId);
        EventBus.emit(MONSTER_ATTACKED_MONSTER_RECEIVED, {
            monsterId,
            direction: data.direction,
            attackSpeedMs: data.attackSpeedMs,
            rangedAttack: data.rangedAttack,
            targetMonsterId: data.targetMonsterId.toString(),
            worldX: data.worldX,
            worldY: data.worldY,
        } satisfies MonsterAttackedMonsterEventData);
    }

    private handlePlayerReceiveDamage(data: PlayerReceiveDamage): void {
        EventBus.emit(PLAYER_RECEIVE_DAMAGE_RECEIVED, {
            playerId: data.playerId.toString(),
            damage: data.damage,
            monsterId: data.monsterId.toString(),
            attackType: data.attackType,
            stunDurationMs: data.stunDurationMs,
            knockbackDurationMs: data.knockbackDurationMs,
            destX: data.destX,
            destY: data.destY,
            knockbackFromX: data.knockbackFromX,
            knockbackFromY: data.knockbackFromY,
        } satisfies PlayerReceiveDamageEventData);
    }

    private handlePlayerTakeDamage(data: PlayerTakeDamage): void {
        EventBus.emit(PLAYER_TAKE_DAMAGE_RECEIVED, {
            targetPlayerId: data.targetPlayerId.toString(),
            damage: data.damage,
            attackerPlayerId: data.attackerPlayerId.toString(),
            attackType: data.attackType,
            stunDurationMs: data.stunDurationMs,
            knockbackDurationMs: data.knockbackDurationMs,
            destX: data.destX,
            destY: data.destY,
            knockbackFromX: data.knockbackFromX,
            knockbackFromY: data.knockbackFromY,
        } satisfies PlayerTakeDamageEventData);
    }

    /** Level/XP/mana of the local player (InitialState.noc or NocStateUpdated.state): converted once, then handed to the UI store via EventBus. */
    private handleNocSnapshot(snapshot: NocSnapshot | undefined): void {
        if (!snapshot) {
            return;
        }
        EventBus.emit(NOC_STATE_RECEIVED, toNocViewModel(snapshot));
    }

    private handleHpUpdated(data: HpUpdated): void {
        this.lastSelfHp = data.hp;
        this.lastSelfMaxHp = data.maxHp;
        EventBus.emit(HP_UPDATED_RECEIVED, { hp: data.hp, maxHp: data.maxHp });
    }

    private handlePlayerDied(data: { playerId: bigint; x: number; y: number }): void {
        const playerId = data.playerId.toString();
        const existing = this.otherPlayersById.get(playerId);
        if (existing) {
            this.otherPlayersById.set(playerId, { ...existing, x: data.x, y: data.y, dead: true, activeTemporaryEffects: [] });
        }
        EventBus.emit(PLAYER_DIED_RECEIVED, {
            playerId,
            x: data.x,
            y: data.y,
        } satisfies PlayerDiedEventData);
    }

    private handlePlayerResurrected(data: { playerId: bigint; x: number; y: number; hp: number; maxHp: number }): void {
        const playerId = data.playerId.toString();
        if (playerId === this.selfPlayerId) {
            this.lastSelfHp = data.hp;
            this.lastSelfMaxHp = data.maxHp;
        }
        const existing = this.otherPlayersById.get(playerId);
        if (existing) {
            this.otherPlayersById.set(playerId, { ...existing, x: data.x, y: data.y, dead: false });
        }
        EventBus.emit(PLAYER_RESURRECTED_RECEIVED, {
            playerId,
            x: data.x,
            y: data.y,
            hp: data.hp,
            maxHp: data.maxHp,
        } satisfies PlayerResurrectedEventData);
    }

    public requestPlayerResurrectedRequest(): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'playerResurrectedRequest',
                value: {},
            },
        }).finish();
        this.sendPacket(command);
    }

    private handleMonsterTakeDamage(data: {
        monsterId: bigint;
        damage: number;
        attackType: number;
        stunlockDurationMs: number;
        hp: number;
        knockbackDurationMs?: number;
        destX?: number;
        destY?: number;
        knockbackFromX?: number;
        knockbackFromY?: number;
    }): void {
        EventBus.emit(MONSTER_TAKE_DAMAGE_RECEIVED, {
            monsterId: data.monsterId.toString(),
            damage: data.damage,
            attackType: data.attackType,
            stunlockDurationMs: data.stunlockDurationMs,
            hp: data.hp,
            knockbackDurationMs: data.knockbackDurationMs,
            destX: data.destX,
            destY: data.destY,
            knockbackFromX: data.knockbackFromX,
            knockbackFromY: data.knockbackFromY,
        } satisfies MonsterTakeDamageEventData);
    }

    private handleMonsterTakeDamageByMonster(data: MonsterTakeDamageByMonster): void {
        EventBus.emit(MONSTER_TAKE_DAMAGE_BY_MONSTER_RECEIVED, {
            targetMonsterId: data.targetMonsterId.toString(),
            damage: data.damage,
            attackerMonsterId: data.attackerMonsterId.toString(),
            attackType: data.attackType,
            stunlockDurationMs: data.stunlockDurationMs,
            hp: data.hp,
            knockbackDurationMs: data.knockbackDurationMs,
            destX: data.destX,
            destY: data.destY,
            knockbackFromX: data.knockbackFromX,
            knockbackFromY: data.knockbackFromY,
        } satisfies MonsterTakeDamageByMonsterEventData);
    }

    private markMonsterAsAttacking(monsterId: string): void {
        const existing = this.monstersInViewById.get(monsterId);
        if (!existing) {
            return;
        }

        this.monstersInViewById.set(monsterId, {
            ...existing,
            state: existing.dead ? existing.state : MonsterEntityState.MONSTER_ENTITY_STATE_ATTACK,
        });
    }

    private handleMonsterDied(data: MonsterDied): void {
        const monsterId = data.monsterId.toString();
        const existing = this.monstersInViewById.get(monsterId);
        if (existing) {
            this.monstersInViewById.set(monsterId, {
                ...existing,
                dead: true,
            });
        }
        EventBus.emit(MONSTER_DIED_RECEIVED, {
            monsterId,
        } satisfies MonsterDiedEventData);
    }

    private handlePlayerAttackedMonster(data: PlayerAttackedMonster): void {
        const eventData: PlayerAttackedMonsterEventData = {
            playerId: data.playerId.toString(),
            direction: data.direction,
            attackSpeedMs: data.attackSpeedMs,
            rangedAttack: data.rangedAttack,
            monsterId: data.monsterId.toString(),
            worldX: data.worldX,
            worldY: data.worldY,
            attackType: data.attackType,
        };
        EventBus.emit(PLAYER_ATTACKED_MONSTER_RECEIVED, eventData);
    }

    private handlePlayerAttackedPlayer(data: PlayerAttackedPlayer): void {
        EventBus.emit(PLAYER_ATTACKED_PLAYER_RECEIVED, {
            playerId: data.playerId.toString(),
            direction: data.direction,
            attackSpeedMs: data.attackSpeedMs,
            rangedAttack: data.rangedAttack,
            targetPlayerId: data.targetPlayerId.toString(),
            worldX: data.worldX,
            worldY: data.worldY,
            attackType: data.attackType,
        } satisfies PlayerAttackedPlayerEventData);
    }

    public sendPlayerAttackedMonster(monsterId: string, rangedAttack: boolean, attackType: number): void {
        const id = BigInt(monsterId);
        if (id === 0n) {
            return;
        }
        const command = ClientMessage.encode({
            payload: {
                $case: 'playerAttackedMonsterRequest',
                value: { monsterId: id, rangedAttack, attackType },
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendPlayerAttackedPlayer(targetPlayerId: string, rangedAttack: boolean, attackType: number): void {
        const id = BigInt(targetPlayerId);
        if (id === 0n) {
            return;
        }
        const command = ClientMessage.encode({
            payload: {
                $case: 'playerAttackedPlayerRequest',
                value: { targetPlayerId: id, rangedAttack, attackType },
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendPlayerPickupRequested(direction: number): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'playerPickupRequested',
                value: { direction },
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendPlayerItemDropRequested(itemUid: string): void {
        const id = BigInt(itemUid);
        if (id === 0n) {
            return;
        }

        const command = ClientMessage.encode({
            payload: {
                $case: 'playerItemDropRequested',
                value: { itemUid: id },
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendPlayerItemPickupRequested(): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'playerItemPickupRequested',
                value: {},
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendPlayerBowStanceRequested(direction: number): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'playerBowStanceRequested',
                value: { direction },
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendCreateItemRequest(itemId: number, effectOverrides?: Effect[]): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'createItemRequest',
                value: {
                    itemId,
                    effectOverrides: (effectOverrides ?? []).map((effectOverride) => ({
                        effect: effectToProtoIndex(effectOverride.effect),
                        effectColor: effectOverride.effectColor,
                    })),
                },
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendMoveItemInBagRequest(itemUid: string, bagX?: number, bagY?: number): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'moveItemInBagRequest',
                value: {
                    itemUid: BigInt(itemUid),
                    bagX,
                    bagY,
                },
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendEquipItemRequest(itemUid: string, targetSlot?: EquipmentSlot): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'equipItemRequest',
                value: {
                    itemUid: BigInt(itemUid),
                    targetSlot,
                },
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendUnequipItemRequest(slot: EquipmentSlot, itemUid: string, bagX?: number, bagY?: number): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'unequipItemRequest',
                value: {
                    slot,
                    itemUid: BigInt(itemUid),
                    bagX,
                    bagY,
                },
            },
        }).finish();
        this.sendPacket(command);
    }

    public sendConsumeItemRequest(itemUid: string): void {
        const command = ClientMessage.encode({
            payload: {
                $case: 'consumeItemRequest',
                value: {
                    itemUid: BigInt(itemUid),
                },
            },
        }).finish();
        this.sendPacket(command);
    }

    private handlePlayerPickupPerformed(data: { playerId: bigint; direction: number; animationTimeMs: number }): void {
        EventBus.emit(PLAYER_PICKUP_PERFORMED_RECEIVED, {
            playerId: data.playerId.toString(),
            direction: data.direction,
            animationTimeMs: data.animationTimeMs,
        } satisfies PlayerPickupPerformedEventData);
    }

    private handlePlayerBowStancePerformed(data: { playerId: bigint; direction: number; animationTimeMs: number }): void {
        EventBus.emit(PLAYER_BOW_STANCE_PERFORMED_RECEIVED, {
            playerId: data.playerId.toString(),
            direction: data.direction,
            animationTimeMs: data.animationTimeMs,
        } satisfies PlayerBowStancePerformedEventData);
    }

    private handleSpellCastStarted(data: SpellCastStarted): void {
        EventBus.emit(SPELL_CAST_STARTED_RECEIVED, {
            playerId: data.playerId.toString(),
            spellName: data.spellName,
            castSpeedMs: data.castSpeedMs,
        } satisfies SpellCastStartedEventData);
    }

    private handleSpellCastCancelled(data: SpellCastCancelled): void {
        EventBus.emit(SPELL_CAST_CANCELLED_RECEIVED, {
            playerId: data.playerId.toString(),
        } satisfies SpellCastCancelledEventData);
    }

    private handleSpellCastFailed(_data: SpellCastFailed): void {
        EventBus.emit(SPELL_CAST_FAILED_RECEIVED);
    }

    private handleCastAoeSpell(data: CastAoeSpell): void {
        EventBus.emit(CAST_AOE_SPELL_RECEIVED, {
            playerId: data.playerId.toString(),
            spellId: data.spellId,
            x: data.x,
            y: data.y,
        } satisfies CastAoeSpellEventData);
    }

    private handleCastDirectionalAoeSpell(data: CastDirectionalAoeSpell): void {
        EventBus.emit(CAST_DIRECTIONAL_AOE_SPELL_RECEIVED, {
            playerId: data.playerId.toString(),
            spellId: data.spellId,
            casterX: data.casterX,
            casterY: data.casterY,
            targetX: data.targetX,
            targetY: data.targetY,
        } satisfies CastDirectionalAoeSpellEventData);
    }

    private handleMonsterCastAoeSpell(data: MonsterCastAoeSpell): void {
        EventBus.emit(MONSTER_CAST_AOE_SPELL_RECEIVED, {
            monsterId: data.monsterId.toString(),
            spellId: data.spellId,
            x: data.x,
            y: data.y,
        } satisfies MonsterCastAoeSpellEventData);
    }

    private handleMonsterCastDirectionalAoeSpell(data: MonsterCastDirectionalAoeSpell): void {
        EventBus.emit(MONSTER_CAST_DIRECTIONAL_AOE_SPELL_RECEIVED, {
            monsterId: data.monsterId.toString(),
            spellId: data.spellId,
            casterX: data.casterX,
            casterY: data.casterY,
            targetX: data.targetX,
            targetY: data.targetY,
        } satisfies MonsterCastDirectionalAoeSpellEventData);
    }

    private handlePlayerTeleported(data: PlayerTeleported): void {
        EventBus.emit(PLAYER_TELEPORTED_RECEIVED, { x: data.x, y: data.y });
    }

    private handleResetPosition(data: { x: number; y: number; gameWorldId: string; remainingStunlockMs: number }): void {
        if (!this.shouldAcceptWorldScopedPacket(data.gameWorldId, 'reset-position')) {
            return;
        }
        EventBus.emit(RESET_POSITION_RECEIVED, { x: data.x, y: data.y, remainingStunlockMs: data.remainingStunlockMs });
    }

    private handlePositionCorrected(data: { curX: number; curY: number; destX: number; destY: number; gameWorldId: string }): void {
        if (!this.shouldAcceptWorldScopedPacket(data.gameWorldId, 'position-corrected')) {
            return;
        }
        EventBus.emit(POSITION_CORRECTED_RECEIVED, { curX: data.curX, curY: data.curY, destX: data.destX, destY: data.destY });
    }

    private shouldAcceptWorldScopedPacket(packetWorldId: string, packetName: string): boolean {
        if (!packetWorldId || !this.currentGameWorldId) {
            return true;
        }
        if (packetWorldId === this.currentGameWorldId) {
            return true;
        }

        console.log(`[NetworkManager] Ignoring stale ${packetName} packet for world '${packetWorldId}' while active world is '${this.currentGameWorldId}'.`);
        return false;
    }

    private handlePlayersEnteredRange(data: PlayersEnteredRange): void {
        const batch: NetworkPlayer[] = [];
        const pendingMovesToEmit: Array<{ playerId: string; move: Omit<PlayerMovedEventData, 'attackMode'> }> = [];
        for (const p of data.players) {
            const movementSpeedMs = p.movementSpeedMs > 0 ? p.movementSpeedMs : 220;
            const runningMode = p.runningMode;
            const gender = appearanceGenderToClient(p.gender);
            const skinColor = appearanceSkinToClient(p.skinColor);
            const hairStyleIndex = Math.max(0, Math.min(7, p.hairStyleIndex));
            const underwearColorIndex = Math.max(0, Math.min(7, p.underwearColorIndex));
            const eventData: NetworkPlayer = {
                playerId: String(p.playerId),
                x: p.x,
                y: p.y,
                movementSpeedMs,
                attackSpeedMs: p.attackSpeedMs,
                castSpeedMs: p.castSpeedMs,
                runningMode,
                attackMode: p.attackMode,
                disconnected: false,
                dead: p.dead ?? false,
                spawnProtection: p.spawnProtection ?? false,
                direction: p.direction,
                visibleEquippedItems: visibleEquippedItemsFromEntries(p.visibleEquippedItems),
                gender,
                skinColor,
                hairStyleIndex,
                underwearColorIndex,
                characterName: p.characterName,
                activeTemporaryEffects: p.activeTemporaryEffects?.length ? [...p.activeTemporaryEffects] : [],
            };
            if (eventData.playerId === this.selfPlayerId) {
                continue;
            }

            const pendingMove = this.pendingPlayerMoveBeforeEnter.get(eventData.playerId);
            if (pendingMove) {
                this.pendingPlayerMoveBeforeEnter.delete(eventData.playerId);
                eventData.x = pendingMove.teleport ? pendingMove.destX : pendingMove.curX;
                eventData.y = pendingMove.teleport ? pendingMove.destY : pendingMove.curY;
                pendingMovesToEmit.push({ playerId: eventData.playerId, move: pendingMove });
            }

            this.otherPlayersById.set(eventData.playerId, eventData);
            batch.push(eventData);
        }
        if (batch.length === 0) {
            return;
        }
        EventBus.emit(PLAYER_JOINED_RECEIVED, batch);
        for (const { playerId, move } of pendingMovesToEmit) {
            const row = this.otherPlayersById.get(playerId);
            if (row) {
                const moveEvent: PlayerMovedEventData = {
                    ...move,
                    attackMode: row.attackMode,
                };
                EventBus.emit(PLAYER_MOVED_RECEIVED, moveEvent);
            }
        }
        console.log(`[NetworkManager] Players entered view radius: ${batch.map((b) => b.playerId).join(', ')}`);
    }

    private handlePlayerMoved(data: PlayerMoved): void {
        const movementSpeedMs = data.movementSpeedMs > 0 ? data.movementSpeedMs : 220;
        const runningMode = data.runningMode;
        const playerId = String(data.playerId);
        if (playerId === this.selfPlayerId) {
            return;
        }

        const existingMoved = this.otherPlayersById.get(playerId);
        if (!existingMoved) {
            this.pendingPlayerMoveBeforeEnter.set(playerId, {
                playerId,
                curX: data.curX,
                curY: data.curY,
                destX: data.destX,
                destY: data.destY,
                movementSpeedMs,
                runningMode,
                dashAttack: data.dashAttack,
                teleport: data.teleport,
            });
            return;
        }

        this.pendingPlayerMoveBeforeEnter.delete(playerId);
        const attackMode = existingMoved.attackMode;
        const eventData: PlayerMovedEventData = {
            playerId,
            curX: data.curX,
            curY: data.curY,
            destX: data.destX,
            destY: data.destY,
            movementSpeedMs,
            runningMode,
            attackMode,
            dashAttack: data.dashAttack,
            teleport: data.teleport,
        };

        this.otherPlayersById.set(eventData.playerId, {
            playerId: eventData.playerId,
            x: eventData.destX,
            y: eventData.destY,
            movementSpeedMs,
            attackSpeedMs: existingMoved.attackSpeedMs,
            castSpeedMs: existingMoved.castSpeedMs,
            runningMode,
            attackMode,
            disconnected: existingMoved.disconnected ?? false,
            dead: existingMoved.dead ?? false,
            direction: existingMoved.direction ?? Direction.NorthEast,
            spawnProtection: existingMoved.spawnProtection ?? false,
            visibleEquippedItems: existingMoved.visibleEquippedItems ?? {},
            gender: existingMoved.gender ?? Gender.MALE,
            skinColor: existingMoved.skinColor ?? SkinColor.Light,
            hairStyleIndex: existingMoved.hairStyleIndex ?? 0,
            underwearColorIndex: existingMoved.underwearColorIndex ?? 0,
            characterName: existingMoved.characterName ?? '',
            activeTemporaryEffects: existingMoved.activeTemporaryEffects ?? [],
        });
        EventBus.emit(PLAYER_MOVED_RECEIVED, eventData);
    }

    private handlePlayersLeftRange(data: PlayersLeftRange): void {
        const ids = data.playerIds.map((id) => id.toString());
        if (ids.length === 0) {
            return;
        }
        for (const id of ids) {
            this.otherPlayersById.delete(id);
            this.pendingPlayerMoveBeforeEnter.delete(id);
        }
        EventBus.emit(PLAYER_LEFT_RECEIVED, ids);
        console.log(`[NetworkManager] Players left view radius: ${ids.join(', ')}`);
    }

    private handleSendMessage(data: { message: string }): void {
        EventBus.emit(SERVER_MESSAGE_RECEIVED, { message: data.message });
    }

    private handleChatMessageReceived(data: ChatMessageReceived): void {
        EventBus.emit(CHAT_MESSAGE_RECEIVED, {
            senderCharacterName: data.senderCharacterName,
            timestampMs: Number(data.timestampMs),
            message: data.message,
        });
    }

    private handleWeatherChanged(data: WeatherChanged): void {
        const mode = weatherModeFromProto(data.weather);
        if (mode === undefined) {
            console.warn('[NetworkManager] weather_changed has unrecognized weather', data.weather);
            return;
        }
        EventBus.emit(OUT_WEATHER_SYNCED, mode);
    }

    private handlePlayerParalyzed(data: { durationSeconds: number }): void {
        EventBus.emit(PLAYER_PARALYZED_RECEIVED, { durationSeconds: data.durationSeconds });
    }

    /** Server cleared pending logout (e.g. combat damage); do not send LogoutCancelledRequest. */
    private handleLogoutCancelled(): void {
        this.clearLogoutCountdown();
        this.logoutPending = false;
        const toastEvent: ToastRequestedEvent = {
            message: 'Logout cancelled — you took damage.',
            severity: 'error',
        };
        EventBus.emit(TOAST_REQUESTED, toastEvent);
    }

    private handleLogoutResponse(data: { wait: number }): void {
        const waitSeconds = data.wait;
        if (waitSeconds > 0) {
            let remaining = waitSeconds;
            this.syncLogoutCountdownUi(remaining);
            this.logoutIntervalId = setInterval(() => {
                runSafeSync('NetworkManager:logoutCountdown', () => {
                    remaining -= 1;
                    this.syncLogoutCountdownUi(remaining > 0 ? remaining : undefined);
                    if (remaining <= 0) {
                        this.clearLogoutCountdown();
                        this.logoutPending = false;
                        this.disconnect();
                    }
                });
            }, 1000);
        } else {
            this.disconnect();
        }
    }

    private handlePlayerMovementStateChanged(data: PlayerMovementStateChanged): void {
        const eventData: PlayerMovementStateChangedEventData = {
            playerId: String(data.playerId),
            runningMode: data.runningMode,
            movementSpeedMs: data.movementSpeedMs > 0 ? data.movementSpeedMs : 220,
        };

        const existing = this.otherPlayersById.get(eventData.playerId);
        if (existing) {
            this.otherPlayersById.set(eventData.playerId, {
                ...existing,
                movementSpeedMs: eventData.movementSpeedMs,
                runningMode: eventData.runningMode,
            });
        }
        EventBus.emit(PLAYER_MOVEMENT_STATE_CHANGED_RECEIVED, eventData);
    }

    private handlePlayerAttackModeChanged(data: PlayerAttackModeChanged): void {
        const eventData: PlayerAttackModeChangedEventData = {
            playerId: String(data.playerId),
            attackMode: data.attackMode,
        };
        if (eventData.playerId === this.selfPlayerId) {
            return;
        }

        const existing = this.otherPlayersById.get(eventData.playerId);
        if (existing) {
            this.otherPlayersById.set(eventData.playerId, {
                ...existing,
                attackMode: eventData.attackMode,
            });
        }
        EventBus.emit(PLAYER_ATTACK_MODE_CHANGED_RECEIVED, eventData);
    }

    private handlePlayerIdleDirectionChanged(data: PlayerIdleDirectionChanged): void {
        const eventData: PlayerIdleDirectionChangedEventData = {
            playerId: String(data.playerId),
            direction: data.direction,
        };
        if (eventData.playerId === this.selfPlayerId) {
            return;
        }

        const existing = this.otherPlayersById.get(eventData.playerId);
        if (existing) {
            this.otherPlayersById.set(eventData.playerId, {
                ...existing,
                direction: eventData.direction,
            });
        }
        EventBus.emit(PLAYER_IDLE_DIRECTION_CHANGED_RECEIVED, eventData);
    }

    private handlePlayerAppearanceChanged(data: PlayerAppearanceChanged): void {
        const eventData: PlayerAppearanceChangedEventData = {
            playerId: String(data.playerId),
            gender: appearanceGenderToClient(data.gender),
            skinColor: appearanceSkinToClient(data.skinColor),
            hairStyleIndex: Math.max(0, Math.min(7, data.hairStyleIndex)),
            underwearColorIndex: Math.max(0, Math.min(7, data.underwearColorIndex)),
        };
        if (eventData.playerId === this.selfPlayerId) {
            return;
        }

        const existing = this.otherPlayersById.get(eventData.playerId);
        if (existing) {
            this.otherPlayersById.set(eventData.playerId, {
                ...existing,
                gender: eventData.gender,
                skinColor: eventData.skinColor,
                hairStyleIndex: eventData.hairStyleIndex,
                underwearColorIndex: eventData.underwearColorIndex,
            });
        }
        EventBus.emit(PLAYER_APPEARANCE_CHANGED_RECEIVED, eventData);
    }

    private handlePlayerDisconnected(data: PlayerDisconnected): void {
        const eventData: PlayerConnectionStateChangedEventData = {
            playerId: String(data.playerId),
        };
        if (eventData.playerId === this.selfPlayerId) {
            return;
        }

        const existing = this.otherPlayersById.get(eventData.playerId);
        if (!existing) {
            return;
        }

        this.otherPlayersById.set(eventData.playerId, { ...existing, disconnected: true });
        EventBus.emit(PLAYER_DISCONNECTED_RECEIVED, eventData);
    }

    private handlePlayerReconnected(data: PlayerReconnected): void {
        const eventData: PlayerConnectionStateChangedEventData = {
            playerId: String(data.playerId),
        };
        if (eventData.playerId === this.selfPlayerId) {
            return;
        }

        const existing = this.otherPlayersById.get(eventData.playerId);
        if (!existing) {
            return;
        }

        this.otherPlayersById.set(eventData.playerId, { ...existing, disconnected: false });
        EventBus.emit(PLAYER_RECONNECTED_RECEIVED, eventData);
    }

    private handleSpawnProtectionEnabled(data: { playerId: bigint }): void {
        const playerIdStr = String(data.playerId);
        if (playerIdStr === this.selfPlayerId) {
            this.pendingSpawnProtectionForSelf = true;
        }
        EventBus.emit(PLAYER_SPAWN_PROTECTION_ENABLED_RECEIVED, { playerId: playerIdStr });
    }

    private handleSpawnProtectionDisabled(data: { playerId: bigint }): void {
        const playerIdStr = String(data.playerId);
        if (playerIdStr === this.selfPlayerId) {
            this.pendingSpawnProtectionForSelf = false;
        }
        EventBus.emit(PLAYER_SPAWN_PROTECTION_DISABLED_RECEIVED, { playerId: playerIdStr });
    }

    /** Returns and clears pending spawn protection for self. Used when creating player in case event arrived before listeners were ready. */
    public getAndClearPendingSpawnProtectionForSelf(): boolean {
        const had = this.pendingSpawnProtectionForSelf;
        if (had) {
            this.pendingSpawnProtectionForSelf = false;
        }
        return had;
    }

    private handleTemporaryEffectApplied(data: TemporaryEffectApplied): void {
        const temporaryEffectType = data.temporaryEffectType as number;
        const entityId = data.entityId.toString();
        if (data.entityKind === TemporaryEffectEntityKind.TEMPORARY_EFFECT_ENTITY_KIND_PLAYER) {
            if (entityId !== this.selfPlayerId) {
                const existing = this.otherPlayersById.get(entityId);
                if (existing) {
                    const next = new Set(existing.activeTemporaryEffects ?? []);
                    next.add(temporaryEffectType);
                    this.otherPlayersById.set(entityId, {
                        ...existing,
                        activeTemporaryEffects: Array.from(next),
                        movementSpeedMs: data.movementSpeedMs ?? existing.movementSpeedMs,
                        attackSpeedMs: data.attackSpeedMs ?? existing.attackSpeedMs,
                        castSpeedMs: data.castSpeedMs ?? existing.castSpeedMs,
                    });
                }
            }
            EventBus.emit(TEMPORARY_EFFECT_APPLIED_FOR_PLAYER_RECEIVED, {
                playerId: entityId,
                temporaryEffectType,
                movementSpeedMs: data.movementSpeedMs,
                attackSpeedMs: data.attackSpeedMs,
                castSpeedMs: data.castSpeedMs,
            });
        } else {
            const existing = this.monstersInViewById.get(entityId);
            if (existing) {
                const next = new Set(existing.activeTemporaryEffects ?? []);
                next.add(temporaryEffectType);
                this.monstersInViewById.set(entityId, {
                    ...existing,
                    activeTemporaryEffects: Array.from(next),
                    movementSpeedMs: data.movementSpeedMs ?? existing.movementSpeedMs,
                    attackSpeedMs: data.attackSpeedMs ?? existing.attackSpeedMs,
                });
            }
            EventBus.emit(TEMPORARY_EFFECT_APPLIED_FOR_MONSTER_RECEIVED, {
                monsterId: entityId,
                temporaryEffectType,
                movementSpeedMs: data.movementSpeedMs,
                attackSpeedMs: data.attackSpeedMs,
            });
        }
    }

    private handleTemporaryEffectExpired(data: TemporaryEffectExpired): void {
        const temporaryEffectType = data.temporaryEffectType as number;
        const entityId = data.entityId.toString();
        if (data.entityKind === TemporaryEffectEntityKind.TEMPORARY_EFFECT_ENTITY_KIND_PLAYER) {
            if (entityId !== this.selfPlayerId) {
                const existing = this.otherPlayersById.get(entityId);
                if (existing) {
                    const next = new Set(existing.activeTemporaryEffects ?? []);
                    next.delete(temporaryEffectType);
                    this.otherPlayersById.set(entityId, {
                        ...existing,
                        activeTemporaryEffects: Array.from(next),
                        movementSpeedMs: data.movementSpeedMs ?? existing.movementSpeedMs,
                        attackSpeedMs: data.attackSpeedMs ?? existing.attackSpeedMs,
                        castSpeedMs: data.castSpeedMs ?? existing.castSpeedMs,
                    });
                }
            }
            EventBus.emit(TEMPORARY_EFFECT_EXPIRED_FOR_PLAYER_RECEIVED, {
                playerId: entityId,
                temporaryEffectType,
                movementSpeedMs: data.movementSpeedMs,
                attackSpeedMs: data.attackSpeedMs,
                castSpeedMs: data.castSpeedMs,
            });
        } else {
            const existing = this.monstersInViewById.get(entityId);
            if (existing) {
                const next = new Set(existing.activeTemporaryEffects ?? []);
                next.delete(temporaryEffectType);
                this.monstersInViewById.set(entityId, {
                    ...existing,
                    activeTemporaryEffects: Array.from(next),
                    movementSpeedMs: data.movementSpeedMs ?? existing.movementSpeedMs,
                    attackSpeedMs: data.attackSpeedMs ?? existing.attackSpeedMs,
                });
            }
            EventBus.emit(TEMPORARY_EFFECT_EXPIRED_FOR_MONSTER_RECEIVED, {
                monsterId: entityId,
                temporaryEffectType,
                movementSpeedMs: data.movementSpeedMs,
                attackSpeedMs: data.attackSpeedMs,
            });
        }
    }

    private handleCastEffect(data: CastEffect): void {
        if (!this.shouldAcceptWorldScopedPacket(data.gameWorldId, 'cast-effect')) {
            return;
        }
        EventBus.emit(CAST_EFFECT_RECEIVED, {
            effectKey: data.effectKey,
            x: data.x,
            y: data.y,
        });
    }
}
