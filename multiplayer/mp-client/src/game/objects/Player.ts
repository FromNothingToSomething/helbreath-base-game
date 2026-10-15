import type { Scene } from 'phaser';
import { GameObject, GameObjectState } from './GameObject';
import { Direction, getDistance, getDirectionOffset, getNextDirection, convertPixelPosToWorldPos, toDirection, worldCellCenterPixelX, worldCellCenterPixelY } from '../../utils/CoordinateUtils';
import type { HBMap } from '../assets/HBMap';
import type { Monster } from './Monster';
import { ShadowManager } from '../../utils/ShadowManager';
import { DEFAULT_PLAYER_ATTACK_RANGE, HIGH_DEPTH, PLAYER_HEALTH_BAR_HEIGHT, PLAYER_HEALTH_BAR_WIDTH } from '../../Config';
import { TILE_SIZE } from '../assets/HBMap';
import { CriticalStrikeProjectile } from '../effects/CriticalStrikeProjectile';
import { ArrowProjectile } from '../effects/ArrowProjectile';
import { StormBringerEffect } from '../effects/StormBringerEffect';
import { drawEffect, drawEffectAtPixelCoords } from '../../utils/EffectUtils';
import { computeOtherPlayerSpatialConfig } from '../../utils/SpatialAudioUtils';
import { EFFECT_RESURRECTION, EFFECT_CASTING_CIRCLE, EFFECT_SPARKLE, EFFECT_FOOTSTEPS_DRY, EFFECT_WET_SPLASH } from '../../constants/Effects';
import { getEffectByKey } from '../../constants/Effects';
import { SoundManager } from '../../utils/SoundManager';
import { mapDialogStore } from '../../ui/store/MapDialog.store';
import { playerDialogStore } from '../../ui/store/PlayerDialog.store';
import { PLAYER_RUNNING, PLAYER_WALKING, PLAYER_MELEE_ATTACK, PLAYER_TAKE_UNARMED_DAMAGE, PLAYER_CAST, SPELL_CAST_FAILED, MALE_CRITICAL_ATTACK, FEMALE_CRITICAL_ATTACK, MALE_DEATH, FEMALE_DEATH, MALE_RESET_POSITION, FEMALE_RESET_POSITION } from '../../constants/SoundFileNames';
import { EventBus } from '../EventBus';
import { PLAYER_POSITION_CHANGED, TILE_OCCUPANCY_REAPPLY_REQUESTED, OUT_UI_PLAYER_DIED, OUT_UI_CAST_STARTED, OUT_UI_CAST_READY, OUT_UI_CAST_REMOVED, PLAYER_CAST_ANIMATION_STARTED, PLAYER_CONFIRM_SPELL_TARGET, EQUIP_ITEM, IN_UI_CHANGE_GENDER, IN_UI_CHANGE_SKIN_COLOR, IN_UI_CHANGE_UNDERWEAR_COLOR, IN_UI_CHANGE_HAIR_STYLE, NATIVE_OVERLAY_HEALTH_BAR_HIDDEN, NATIVE_OVERLAY_HEALTH_BAR_UPDATED } from '../../constants/EventNames';
import { AttackType, Gender, MonsterAttackType, SkinColor, TemporaryEffectType } from '../../Types';
import { calculateAnimationDuration, calculateFrameRateFromDuration } from '../../utils/AnimationUtils';
import { FloatingText } from '../effects/FloatingText';
import { ItemTypes, ItemEffect, WeaponType, RING_SLOT_LEFT, RING_SLOT_RIGHT, getItemById, hasEquippedItemEffect, type Effect, type EquipmentSlot, type InventoryItem, type Item } from '../../constants/Items';
import { getInventoryManager, getNetworkManager, setPlayerPosition } from '../../utils/RegistryUtils';
import { DEFAULT_GEAR, GearConfig, PlayerAppearanceManager, type PlayerAppearanceAnimationConfig, PlayerState } from '../../utils/PlayerAppearanceManager';
import { PlayerMovementManager, type PendingSyncCommand } from '../../utils/PlayerMovementManager';
import { PlayerRangedCombatManager } from '../../utils/PlayerRangedCombatManager';
import type { GameWorld as GameWorldScene } from '../scenes/GameWorld';

type CombatTarget = Monster | Player;

function isMonsterCombatTarget(target: CombatTarget): target is Monster {
    return 'getMonsterId' in target && typeof target.getMonsterId === 'function';
}

function attackTypeFromNetworkValue(value: number): AttackType {
    if (
        value === AttackType.NoInterrupt ||
        value === AttackType.Interrupt ||
        value === AttackType.Stun ||
        value === AttackType.Knockback
    ) {
        return value;
    }
    console.warn('[Player] Invalid remote attack attackType', value);
    return AttackType.Stun;
}

/**
 * Represents the player character in the game.
 * Extends GameObject with combat (melee, bow, spell casting), movement (run, walk, dash),
 * equipment via PlayerAppearanceManager/InventoryManager, health/damage, and appearance
 * customization (gender, skin color, hair, underwear). Listens to EventBus for equip and
 * appearance changes.
 */

export class Player extends GameObject {
    private readonly appearanceManager: PlayerAppearanceManager;
    private readonly isLocalPlayer: boolean;
    private readonly movement = new PlayerMovementManager();
    private readonly rangedCombat = new PlayerRangedCombatManager();

    /** Handler for EQUIP_ITEM - stored for cleanup on destroy */
    private equipItemHandler?: (payload: { itemType: string; itemId?: number; itemUid: string; effectOverrides?: Effect[] }) => void;

    /** Handler for IN_UI_CHANGE_GENDER - stored for cleanup on destroy */
    private genderChangeHandler?: (gender: Gender) => void;
    /** Handler for IN_UI_CHANGE_SKIN_COLOR - stored for cleanup on destroy */
    private skinColorChangeHandler?: (skinColor: SkinColor) => void;
    /** Handler for IN_UI_CHANGE_UNDERWEAR_COLOR - stored for cleanup on destroy */
    private underwearColorChangeHandler?: (index: number) => void;
    /** Handler for IN_UI_CHANGE_HAIR_STYLE - stored for cleanup on destroy */
    private hairStyleChangeHandler?: (index: number) => void;

    /** Current animation state */
    private currentState: PlayerState;

    /** Attack mode: when true, idle uses combat stance; when false, idle uses peace stance */
    private attackMode: boolean = true;

    /** Run mode: when true, run at full speed; when false, walk at half speed */
    private runMode: boolean = true;

    /** Attack range in cells (Chebyshev distance) */
    private attackRange: number = DEFAULT_PLAYER_ATTACK_RANGE;

    /** Attack type - whether damage interrupts the target */
    private attackType: AttackType = AttackType.Stun;

    /** Attack animation frame rate (frames per second); default matches ~600 ms full swing. */
    private attackSpeed: number = calculateFrameRateFromDuration(8, 600);

    /** Arrow travel speed (px/s) from InitialGameWorldState; matches ranged hit timing. */
    private arrowSpeedPxPerSec = 1000;

    /** Full pickup animation duration (ms) from InitialGameWorldState; overwrites the default when the scene applies world state. */
    private playerPickupAnimationMs = 400;

    /** Duration for the current synced pickup animation. */
    private remotePickupAnimationDurationMs: number | undefined = undefined;

    /** Full bow stance animation duration (ms) from InitialGameWorldState. */
    private playerBowAnimationDurationMs = 400;

    /** Duration for the current synced bow stance animation. */
    private remoteBowStanceAnimationDurationMs: number | undefined = undefined;


    /** Previous attack FPS restored after a synced attack animation. */
    private remoteAttackSpeedBackup: number | undefined = undefined;

    /**
     * Cast animation / bar duration (ms). Local: from `setCastDurationMs` (Player dialog / `InitialGameWorldState`).
     * Remote: default `1200` when not observing a cast; each `queueRemoteSpellCastStart` overwrites from server `cast_speed_ms`, then resets to `1200` when leaving Cast/CastReady.
     */
    private castSpeed: number = 1200;

    /** Casting circle effect instance (created when entering Cast state) */
    private castingCircleEffect: ReturnType<typeof drawEffect> | undefined = undefined;

    /** SoundManager instance for playing sound effects */
    private readonly soundManager: SoundManager;

    /** When true, the next state switch skips its sound side effects. */
    private suppressNextStateSound = false;

    /** Monster or player targeted for attack when out of range (pathfind towards on release) */
    private attackTarget: CombatTarget | undefined = undefined;
    private playerId: string | undefined = undefined;
    /** Remote: server `PlayerEnteredRange.character_name` for UI hover. */
    private characterName = '';
    private activeSpellName: string | undefined = undefined;

    /** When true, player is dashing: moving with attack animation instead of run animation */
    private dashMode: boolean = false;
    /** Dash flag to apply on the next deferred movement step once this sprite is aligned. */
    private queuedDashModeForNextMove: boolean | undefined = undefined;

    /** Pending spell ID when cast is commanded from UI (targeting or CastReady) */
    private pendingSpellId: number | undefined = undefined;

    /** Queued spell cast when player is moving - executed when reaching next cell */
    private queuedCastSpellId: number | undefined = undefined;


    /**
     * Local player only: wall-clock time (ms, Date.now()) after which movement requests are allowed again.
     * Matches the interrupt-stunlock deadline without relying on frame delta or TakeDamage* state.
     * 0 means no movement stunlock from this mechanism.
     */
    private localPlayerMovementStunlockUntilUnixMs = 0;

    /** Frame rate for idle animations (always 10 FPS) */
    private readonly IDLE_FRAME_RATE: number = 10;

    /** Number of frames in running animation (standard for all player animations) */
    private readonly RUNNING_FRAME_COUNT: number = 8;

    /** Whether the player is dead (in Die state) */
    private dead: boolean = false;

    /** Whether this player is visually disconnected due to a temporary disconnect. */
    public disconnected = false;

    /** Accumulator for STAR_TWINKLE spawn interval (ms). Spawns sparkles above player when equipped. */
    private starTwinkleAccumulatorMs: number = 0;

    /** Per-player equipped items used for passive/effect checks so remote players do not read the local inventory manager. */
    private equippedItemsForEffects: Partial<Record<EquipmentSlot, InventoryItem>> = {};

    /** Start offset for the current course-correction step. */
    private correctionStartOffsetX: number | undefined = undefined;
    private correctionStartOffsetY: number | undefined = undefined;
    private correctionDurationMs: number | undefined = undefined;

    /** Timestamp (ms) when paralysis ends. Movement commands are blocked until then. */
    private paralysisUntil: number | undefined = undefined;

    /** When set, TakeDamage states stretch animation to this duration from the damage packet. */
    private takeDamageAnimationDurationMs: number | undefined = undefined;

    /**
     * When TakeDamage / TakeDamageOnMove begins (`performance.now()` ms).
     * Keeps those states from exiting on the first frame the primary asset reports not playing
     * before the stretched take-damage clip has started (jarring when the stun duration is short).
     */
    private takeDamageVisualEnteredAtMs = 0;

    /**
     * Creates a new Player instance.
     *
     * @param scene - The Phaser scene to add the player to
     * @param worldX - X coordinate in world map position
     * @param worldY - Y coordinate in world map position
     * @param direction - Facing for directional sprites; for remote players use `PlayerEnteredRange.direction` (0–7, CoordinateUtils.Direction).
     * @param soundManager - SoundManager instance for playing sound effects
     * @param map - HBMap instance for collision checking
     * @param gear - Initial gear config; resolved from local inventory or remote visible equipment when not provided
     * @param movementSpeedMs - Per-tile step duration in ms (server or dialog); clamped 100–1000 like {@link setMovementSpeed}.
     */
    constructor(
        scene: Scene,
        worldX: number,
        worldY: number,
        direction: Direction = Direction.NorthEast,
        soundManager: SoundManager,
        map: HBMap,
        gear: GearConfig = DEFAULT_GEAR,
        movementSpeedMs: number,
        isLocalPlayer: boolean = true,
        initialVisibleEquippedItems: Partial<Record<ItemTypes, { itemId: number; effectOverrides?: Effect[] }>> = {},
        remoteAppearance?: { gender: Gender; skinColor: SkinColor; underwearColorIndex: number; hairStyleIndex: number },
    ) {
        // Local: Player dialog store (server sync via OUT_UI). Remote: server snapshot.
        const initialGender =
            !isLocalPlayer && remoteAppearance !== undefined ? remoteAppearance.gender : playerDialogStore.state.gender;
        const initialSkinColor =
            !isLocalPlayer && remoteAppearance !== undefined ? remoteAppearance.skinColor : playerDialogStore.state.skinColor;
        const initialGear: GearConfig = {
            ...gear,
            human: PlayerAppearanceManager.getHumanSpriteName(initialGender, initialSkinColor),
            underwearColorIndex:
                !isLocalPlayer && remoteAppearance !== undefined
                    ? remoteAppearance.underwearColorIndex
                    : playerDialogStore.state.underwearColorIndex,
            hairStyleIndex:
                !isLocalPlayer && remoteAppearance !== undefined
                    ? remoteAppearance.hairStyleIndex
                    : playerDialogStore.state.hairStyleIndex,
        };
        const resolvedGear = PlayerAppearanceManager.resolveGearFromEquippedItems(
            initialGear,
            isLocalPlayer ? getInventoryManager(scene.game).equippedItems : initialVisibleEquippedItems,
            initialGender,
        );
        const { configs: assetConfigs, assetIndices } = PlayerAppearanceManager.buildAssetConfigs(
            direction,
            PlayerState.IdlePeaceMode,
            resolvedGear,
        );

        // Add animation frame change callback to the weapon asset for attack damage/sound/arrows.
        const weaponConfig = assetIndices.weaponAssetIndex >= 0 ? assetConfigs[assetIndices.weaponAssetIndex] : undefined;
        if (weaponConfig) {
            weaponConfig.onAnimationFrameChange = (relativeFrameIndex: number) =>
                this.onWeaponAnimationFrameChange(relativeFrameIndex);
        }

        const clampedMovementSpeedMs = Phaser.Math.Clamp(movementSpeedMs, 100, 1000);
        super(scene, {
            x: worldX,
            y: worldY,
            assets: assetConfigs,
            soundManager,
            map,
            movementSpeedMs: clampedMovementSpeedMs,
            stunlockDurationMs: 0,
        });

        this.isLocalPlayer = isLocalPlayer;
        if (!isLocalPlayer) {
            this.autoSwitchToIdle = false;
        }
        this.appearanceManager = new PlayerAppearanceManager(
            this.assets,
            initialGender,
            resolvedGear,
            assetIndices,
            scene,
            () => this.switchPlayerState(this.currentState, true),
        );
        this.soundManager = soundManager;
        this.hp = 1000;
        this.maxHp = 1000;

        this.direction = direction;
        this.currentState = PlayerState.IdlePeaceMode;

        // Create shadow manager
        const initialShadowSpriteSheetIndex = this.appearanceManager.getShadowSpriteSheetIndex(PlayerState.IdlePeaceMode, direction);
        this.shadowManager = new ShadowManager({
            scene,
            shadowSpriteName: this.appearanceManager.getHumanSpriteName(),
            shadowSpriteSheetIndex: initialShadowSpriteSheetIndex,
            worldX,
            worldY,
            frameRate: this.IDLE_FRAME_RATE,
        });

        // Center the player in the initial cell
        this.updatePixelPosition();

        // Create health bar (20px wide, 2 cells above player when alive)
        if (this.isLocalPlayer) {
            // Listen for gender change from UI
            this.genderChangeHandler = (gender: Gender) => {
                this.applyAppearanceChange(gender, playerDialogStore.state.skinColor);
            };
            EventBus.on(IN_UI_CHANGE_GENDER, this.genderChangeHandler);

            // Listen for skin color change from UI
            this.skinColorChangeHandler = (skinColor: SkinColor) => {
                this.applyAppearanceChange(playerDialogStore.state.gender, skinColor);
            };
            EventBus.on(IN_UI_CHANGE_SKIN_COLOR, this.skinColorChangeHandler);

            // Listen for underwear color change from UI
            this.underwearColorChangeHandler = (underwearColorIndex: number) => {
                this.applyAppearanceChange(
                    playerDialogStore.state.gender,
                    playerDialogStore.state.skinColor,
                    underwearColorIndex,
                );
            };
            EventBus.on(IN_UI_CHANGE_UNDERWEAR_COLOR, this.underwearColorChangeHandler);

            // Listen for hair style change from UI
            this.hairStyleChangeHandler = (hairStyleIndex: number) => {
                this.applyAppearanceChange(
                    playerDialogStore.state.gender,
                    playerDialogStore.state.skinColor,
                    undefined,
                    hairStyleIndex,
                );
            };
            EventBus.on(IN_UI_CHANGE_HAIR_STYLE, this.hairStyleChangeHandler);

            // Listen for equip events from InventoryManager
            const equipItemHandler = (payload: { itemType: string; itemId?: number; itemUid: string; effectOverrides?: Effect[] }) => {
                if (this.isEquipmentSlotKey(payload.itemType)) {
                    this.syncTrackedEquippedItem(payload.itemType, payload.itemId, payload.itemUid, payload.effectOverrides);
                }
                if (payload.itemType === ItemTypes.WEAPON ||
                    payload.itemType === ItemTypes.SHIELD ||
                    payload.itemType === ItemTypes.ARMOR ||
                    payload.itemType === ItemTypes.HAUBERK ||
                    payload.itemType === ItemTypes.LEGGINGS ||
                    payload.itemType === ItemTypes.BOOTS ||
                    payload.itemType === ItemTypes.HELMET ||
                    payload.itemType === ItemTypes.CAPE ||
                    payload.itemType === ItemTypes.ACCESSORY) {
                    this.onEquipItem(payload.itemType, payload.itemId, payload.effectOverrides);
                }
            };
            this.equipItemHandler = equipItemHandler;
            EventBus.on(EQUIP_ITEM, equipItemHandler);

            const equipped = getInventoryManager(scene.game).equippedItems;
            for (const [slot, item] of Object.entries(equipped)) {
                if (item && this.isEquipmentSlotKey(slot)) {
                    this.syncTrackedEquippedItem(slot, item.itemId, item.itemUid, item.effectOverrides);
                }
            }
            this.onEquipItem(ItemTypes.WEAPON, equipped[ItemTypes.WEAPON]?.itemId, equipped[ItemTypes.WEAPON]?.effectOverrides);
            this.onEquipItem(ItemTypes.SHIELD, equipped[ItemTypes.SHIELD]?.itemId, equipped[ItemTypes.SHIELD]?.effectOverrides);
            this.onEquipItem(ItemTypes.ARMOR, equipped[ItemTypes.ARMOR]?.itemId, equipped[ItemTypes.ARMOR]?.effectOverrides);
            this.onEquipItem(ItemTypes.HAUBERK, equipped[ItemTypes.HAUBERK]?.itemId, equipped[ItemTypes.HAUBERK]?.effectOverrides);
            this.onEquipItem(ItemTypes.LEGGINGS, equipped[ItemTypes.LEGGINGS]?.itemId, equipped[ItemTypes.LEGGINGS]?.effectOverrides);
            this.onEquipItem(ItemTypes.BOOTS, equipped[ItemTypes.BOOTS]?.itemId, equipped[ItemTypes.BOOTS]?.effectOverrides);
            this.onEquipItem(ItemTypes.HELMET, equipped[ItemTypes.HELMET]?.itemId, equipped[ItemTypes.HELMET]?.effectOverrides);
            this.onEquipItem(ItemTypes.CAPE, equipped[ItemTypes.CAPE]?.itemId, equipped[ItemTypes.CAPE]?.effectOverrides);
            this.onEquipItem(ItemTypes.ACCESSORY, equipped[ItemTypes.ACCESSORY]?.itemId, equipped[ItemTypes.ACCESSORY]?.effectOverrides);
        } else {
            this.onEquipItem(ItemTypes.WEAPON, initialVisibleEquippedItems[ItemTypes.WEAPON]?.itemId, initialVisibleEquippedItems[ItemTypes.WEAPON]?.effectOverrides);
            this.onEquipItem(ItemTypes.SHIELD, initialVisibleEquippedItems[ItemTypes.SHIELD]?.itemId, initialVisibleEquippedItems[ItemTypes.SHIELD]?.effectOverrides);
            this.onEquipItem(ItemTypes.ARMOR, initialVisibleEquippedItems[ItemTypes.ARMOR]?.itemId, initialVisibleEquippedItems[ItemTypes.ARMOR]?.effectOverrides);
            this.onEquipItem(ItemTypes.HAUBERK, initialVisibleEquippedItems[ItemTypes.HAUBERK]?.itemId, initialVisibleEquippedItems[ItemTypes.HAUBERK]?.effectOverrides);
            this.onEquipItem(ItemTypes.LEGGINGS, initialVisibleEquippedItems[ItemTypes.LEGGINGS]?.itemId, initialVisibleEquippedItems[ItemTypes.LEGGINGS]?.effectOverrides);
            this.onEquipItem(ItemTypes.BOOTS, initialVisibleEquippedItems[ItemTypes.BOOTS]?.itemId, initialVisibleEquippedItems[ItemTypes.BOOTS]?.effectOverrides);
            this.onEquipItem(ItemTypes.HELMET, initialVisibleEquippedItems[ItemTypes.HELMET]?.itemId, initialVisibleEquippedItems[ItemTypes.HELMET]?.effectOverrides);
            this.onEquipItem(ItemTypes.CAPE, initialVisibleEquippedItems[ItemTypes.CAPE]?.itemId, initialVisibleEquippedItems[ItemTypes.CAPE]?.effectOverrides);
            this.onEquipItem(ItemTypes.ACCESSORY, initialVisibleEquippedItems[ItemTypes.ACCESSORY]?.itemId, initialVisibleEquippedItems[ItemTypes.ACCESSORY]?.effectOverrides);
            for (const [slot, item] of Object.entries(initialVisibleEquippedItems)) {
                if (item && this.isEquipmentSlotKey(slot)) {
                    this.syncTrackedEquippedItem(slot, item.itemId, '', item.effectOverrides);
                }
            }
        }
    }

    private onEquipItem(itemType: ItemTypes, itemId: number | undefined, effectOverrides?: Effect[]): void {
        this.appearanceManager.handleEquip(itemType, itemId, effectOverrides);
        this.switchPlayerState(this.currentState, true);
        this.updatePixelPosition();
    }

    public setRemoteVisibleEquippedItem(itemType: ItemTypes, itemId: number | undefined, effectOverrides?: Effect[]): void {
        if (this.isLocalPlayer) {
            return;
        }
        if (!this.isEquipmentSlotKey(itemType)) {
            return;
        }

        this.syncTrackedEquippedItem(itemType, itemId, this.equippedItemsForEffects[itemType]?.itemUid ?? '', effectOverrides);
        this.onEquipItem(itemType, itemId, effectOverrides);
    }

    private isEquipmentSlotKey(value: string): value is EquipmentSlot {
        return value === ItemTypes.WEAPON ||
            value === ItemTypes.SHIELD ||
            value === ItemTypes.ARMOR ||
            value === ItemTypes.HAUBERK ||
            value === ItemTypes.LEGGINGS ||
            value === ItemTypes.HELMET ||
            value === ItemTypes.CAPE ||
            value === ItemTypes.BOOTS ||
            value === ItemTypes.ACCESSORY ||
            value === ItemTypes.NECKLACE ||
            value === RING_SLOT_LEFT ||
            value === RING_SLOT_RIGHT;
    }

    private syncTrackedEquippedItem(slot: EquipmentSlot, itemId: number | undefined, itemUid: string, effectOverrides?: Effect[]): void {
        if (itemId === undefined) {
            this.equippedItemsForEffects[slot] = undefined;
            return;
        }

        this.equippedItemsForEffects[slot] = {
            itemId,
            itemUid,
            ...(effectOverrides?.length && { effectOverrides }),
        };
    }

    private getTrackedWeaponDef(): Item | undefined {
        const equippedWeapon = this.equippedItemsForEffects[ItemTypes.WEAPON];
        return equippedWeapon ? getItemById(equippedWeapon.itemId) : undefined;
    }

    private applyAppearanceChange(gender: Gender, skinColor: SkinColor, underwearColorIndex?: number, hairStyleIndex?: number): void {
        const inventoryManager = getInventoryManager(this.scene.game);
        this.appearanceManager.applyAppearanceChange(gender, skinColor, inventoryManager.equippedItems, this.currentState, this.direction, this.shadowManager, underwearColorIndex, hairStyleIndex);
        this.switchPlayerState(this.currentState, true);
    }

    private getEquippedItemsForRemoteAppearance(): Partial<Record<ItemTypes, { itemId: number; effectOverrides?: Effect[] }>> {
        const out: Partial<Record<ItemTypes, { itemId: number; effectOverrides?: Effect[] }>> = {};
        for (const slot of Object.values(ItemTypes)) {
            if (!this.isEquipmentSlotKey(slot)) {
                continue;
            }
            const item = this.equippedItemsForEffects[slot];
            if (item) {
                out[slot] = { itemId: item.itemId, effectOverrides: item.effectOverrides };
            }
        }
        return out;
    }

    /** Remote players: apply server-driven gender/skin/hair/underwear without touching local persistence. */
    public applyAppearance(
        gender: Gender,
        skinColor: SkinColor,
        underwearColorIndex: number,
        hairStyleIndex: number,
    ): void {
        if (this.isLocalPlayer) {
            return;
        }
        const uw = Math.max(0, Math.min(7, underwearColorIndex));
        const hair = Math.max(0, Math.min(7, hairStyleIndex));
        this.appearanceManager.applyAppearanceChange(
            gender,
            skinColor,
            this.getEquippedItemsForRemoteAppearance(),
            this.currentState,
            this.direction,
            this.shadowManager,
            uw,
            hair,
        );
        this.switchPlayerState(this.currentState, true);
    }

    protected override updateDepth(): void {
        this.appearanceManager.updateDepth(this.worldY, this.direction, this.currentState);
    }

    protected override updatePixelPosition(): void {
        const finalPixelX = this.getAnimatedPixelX();
        const finalPixelY = this.getAnimatedPixelY();
        this.updateDepth();
        const ghostConfig = this.getGhostConfig();
        this.appearanceManager.updateAssetPositions(finalPixelX, finalPixelY, ghostConfig);
        this.updateShadowPosition();
        this.updateShadowDepth();
    }

    /**
     * Local player: movement stunlock uses wall clock against the armed deadline only (no ping-variance subtraction).
     * Other players: packet playback drives their actions, so the client does not block on stunlock.
     */
    protected override isStunlocked(): boolean {
        if (!this.isLocalPlayer) {
            return false;
        }
        const until = this.localPlayerMovementStunlockUntilUnixMs;
        return until > 0 && Date.now() < until;
    }

    private armLocalPlayerMovementStunlockFromNow(durationMs: number): void {
        if (!this.isLocalPlayer || durationMs <= 0) {
            return;
        }
        //const halfPingMs = Math.round((nm?.getLatestPing() ?? 0) / 2);
        //const nextUntil = Date.now() + durationMs + MOVEMENT_STUNLOCK_CLIENT_BUFFER_MS + halfPingMs;
        const nextUntil = Date.now() + durationMs;
        this.localPlayerMovementStunlockUntilUnixMs = Math.max(this.localPlayerMovementStunlockUntilUnixMs, nextUntil);
    }

    /**
     * Remote players keep delta stunlock for animation completion; local player uses wall clock only (see {@link isStunlocked}).
     */
    protected override updateStunlock(delta: number): void {
        if (this.isLocalPlayer) {
            if (this.localPlayerMovementStunlockUntilUnixMs > 0 && Date.now() >= this.localPlayerMovementStunlockUntilUnixMs) {
                this.localPlayerMovementStunlockUntilUnixMs = 0;
                this.onStunlockComplete();
            }
            return;
        }
        super.updateStunlock(delta);
    }

    /**
     * Returns ghost config while dashing and moving (trail behind sprite).
     */
    private getGhostConfig(): { enabled: boolean; offsetX: number; offsetY: number } | undefined {
        const showGhost = this.dashMode;
        if (!showGhost || !this.moving || (!this.isInMovementState() && !this.dashMode)) {
            return undefined;
        }
        const [dx, dy] = getDirectionOffset(this.direction);
        const progress = Math.min(this.movementElapsedTime / this.activeStepDurationMs, 1);
        const ghostDistance = 16 * (1 - progress);
        return {
            enabled: true,
            offsetX: -dx * ghostDistance,
            offsetY: -dy * ghostDistance,
        };
    }

    /**
     * Updates sound effects based on the player's state.
     * Movement sounds continue playing during direction changes; only stop when actually leaving movement.
     * Other players: no walking sounds; running sounds play spatially relative to self player.
     */
    private updateSound(newState: PlayerState): void {
        switch (newState) {
            case PlayerState.Run:
            case PlayerState.WalkPeaceMode:
            case PlayerState.WalkCombatMode: {
                if (!this.isLocalPlayer && (newState === PlayerState.WalkPeaceMode || newState === PlayerState.WalkCombatMode)) {
                    this.stopMovementSounds();
                    break;
                }
                const wasInMovementState = this.isInMovementState();
                const movementTypeChanged = wasInMovementState && (
                    (this.currentState === PlayerState.Run) !== (newState === PlayerState.Run)
                );
                if (!wasInMovementState || movementTypeChanged) {
                    this.stopMovementSounds();
                }
                // Always refresh loop timing (SoundTracker updates rate when interval unchanged but ms changed).
                // Needed for same-state movement steps (Run→Run / walk→walk) after step duration or run/walk toggle.
                this.playMovementLoopSoundFromMovementConfig();
                break;
            }
            case PlayerState.Cast:
                this.stopMovementSounds();
                // Play cast sound with duration matching castSpeed (in milliseconds)
                // Track by state so it can be stopped when casting is cancelled
                if (!this.isLocalPlayer) {
                    const spatialConfig = computeOtherPlayerSpatialConfig(
                        this.scene.game,
                        this.worldX,
                        this.worldY,
                        this.offsetX,
                        this.offsetY,
                        TILE_SIZE,
                    );
                    this.soundTracker.playOnce(PLAYER_CAST, this.castSpeed, spatialConfig, PlayerState.Cast);
                } else {
                    this.soundTracker.playOnce(PLAYER_CAST, this.castSpeed, undefined, PlayerState.Cast);
                }
                break;
            case PlayerState.MeleeAttack:
            case PlayerState.BowAttack:
            case PlayerState.BowStance:
            case PlayerState.IdlePeaceMode:
            case PlayerState.IdleCombatMode:
            case PlayerState.TakeDamage:
            case PlayerState.TakeDamageOnMove:
            case PlayerState.TakeDamageWithKnockback:
            case PlayerState.CastReady:
            case PlayerState.PickUp:
                this.stopMovementSounds();
                break;
            case PlayerState.Die: {
                const deathSound = this.getGender() === Gender.FEMALE ? FEMALE_DEATH : MALE_DEATH;
                if (!this.isLocalPlayer) {
                    const spatialConfig = computeOtherPlayerSpatialConfig(
                        this.scene.game,
                        this.worldX,
                        this.worldY,
                        this.offsetX,
                        this.offsetY,
                        TILE_SIZE,
                    );
                    this.soundTracker.playOnce(deathSound, undefined, spatialConfig);
                } else {
                    this.soundTracker.playOnce(deathSound);
                }
                break;
            }
            default:
                this.soundTracker.stopAllSounds();
        }
    }

    /**
     * Stops run and walk sounds.
     */
    private stopMovementSounds(): void {
        this.soundTracker.stopSound(PlayerState.Run);
        this.soundTracker.stopSound(PlayerState.WalkPeaceMode);
    }

    /**
     * Starts or refreshes the run/walk loop sound from {@link getMovementConfig} (e.g. after tile duration changes while already moving).
     * When a loop is already tracked, {@link SoundTracker.playInLoop} updates playback rate to match the new interval.
     */
    private playMovementLoopSoundFromMovementConfig(): void {
        const config = this.getMovementConfig();
        if (!this.isLocalPlayer && (config.state === PlayerState.WalkPeaceMode || config.state === PlayerState.WalkCombatMode)) {
            return;
        }
        const soundStateKey = config.state === PlayerState.Run ? PlayerState.Run : PlayerState.WalkPeaceMode;
        if (!this.isLocalPlayer && config.state === PlayerState.Run) {
            const spatialConfig = computeOtherPlayerSpatialConfig(
                this.scene.game,
                this.worldX,
                this.worldY,
                this.offsetX,
                this.offsetY,
                TILE_SIZE,
            );
            this.soundTracker.playInLoop(soundStateKey, config.soundKey, config.soundIntervalMs, spatialConfig);
        } else {
            this.soundTracker.playInLoop(soundStateKey, config.soundKey, config.soundIntervalMs);
        }
    }

    /**
     * Switches the player's animation state.
     * Updates all assets to use the new sprite sheet index corresponding to the state.
     */
    private switchPlayerState(newState: PlayerState, forceUpdate: boolean = false): void {
        if (this.currentState === newState && !forceUpdate) {
            return;
        }

        if (this.suppressNextStateSound) {
            this.suppressNextStateSound = false;
            this.stopMovementSounds();
        } else {
            this.updateSound(newState);
        }

        const previousState = this.currentState;

        if (previousState === PlayerState.BowAttack && newState !== PlayerState.BowAttack) {
            this.cancelPendingBowArrowSpawn();
        }

        if (!this.isLocalPlayer &&
            (previousState === PlayerState.MeleeAttack || previousState === PlayerState.BowAttack) &&
            newState !== PlayerState.MeleeAttack && newState !== PlayerState.BowAttack &&
            this.remoteAttackSpeedBackup !== undefined) {
            this.attackSpeed = this.remoteAttackSpeedBackup;
            this.remoteAttackSpeedBackup = undefined;
        }

        if (!this.isLocalPlayer &&
            (previousState === PlayerState.Cast || previousState === PlayerState.CastReady) &&
            newState !== PlayerState.Cast && newState !== PlayerState.CastReady) {
            this.castSpeed = 1200;
        }

        this.currentState = newState;

        if (newState === PlayerState.TakeDamage || newState === PlayerState.TakeDamageOnMove) {
            this.takeDamageVisualEnteredAtMs = performance.now();
        }

        if (newState !== PlayerState.TakeDamage && newState !== PlayerState.TakeDamageOnMove && newState !== PlayerState.TakeDamageWithKnockback) {
            this.takeDamageAnimationDurationMs = undefined;
        }

        if (previousState === PlayerState.PickUp && newState !== PlayerState.PickUp) {
            this.remotePickupAnimationDurationMs = undefined;
        }

        if (previousState === PlayerState.BowStance && newState !== PlayerState.BowStance) {
            this.remoteBowStanceAnimationDurationMs = undefined;
        }

        // Create casting circle effect when entering Cast state
        if (newState === PlayerState.Cast && previousState !== PlayerState.Cast) {
            this.createCastingCircleEffect();
            // Create floating text with spell name in green color
            this.createSpellNameFloatingText();
        }

        // Destroy casting circle effect when leaving Cast state
        if (previousState === PlayerState.Cast && newState !== PlayerState.Cast) {
            this.destroyCastingCircleEffect();
        }

        const stepMsForAnim = this.moving ? this.activeStepDurationMs : this.movementSpeedMs;
        const effectiveAttackSpeed = (this.dashMode && newState === PlayerState.MeleeAttack)
            ? calculateFrameRateFromDuration(this.RUNNING_FRAME_COUNT, stepMsForAnim)
            : this.attackSpeed;
        const appearanceAnimConfig: PlayerAppearanceAnimationConfig = {
            movementSpeedMs: stepMsForAnim,
            attackSpeed: effectiveAttackSpeed,
            castSpeed: this.castSpeed,
            idleFrameRate: this.IDLE_FRAME_RATE,
        };
        if (this.takeDamageAnimationDurationMs !== undefined) {
            appearanceAnimConfig.takeDamageAnimationDurationMs = this.takeDamageAnimationDurationMs;
        }
        if (newState === PlayerState.PickUp) {
            if (this.isLocalPlayer) {
                appearanceAnimConfig.pickupAnimationDurationMs = this.playerPickupAnimationMs;
            } else if (this.remotePickupAnimationDurationMs !== undefined) {
                appearanceAnimConfig.pickupAnimationDurationMs = this.remotePickupAnimationDurationMs;
            }
        }
        if (newState === PlayerState.BowStance) {
            if (this.isLocalPlayer) {
                appearanceAnimConfig.bowStanceAnimationDurationMs = this.playerBowAnimationDurationMs;
            } else if (this.remoteBowStanceAnimationDurationMs !== undefined) {
                appearanceAnimConfig.bowStanceAnimationDurationMs = this.remoteBowStanceAnimationDurationMs;
            }
        }
        this.appearanceManager.applyStateAppearance(newState, this.direction, appearanceAnimConfig);

        if (newState === PlayerState.Run || previousState === PlayerState.Run ||
            newState === PlayerState.MeleeAttack || previousState === PlayerState.MeleeAttack ||
            newState === PlayerState.BowAttack || previousState === PlayerState.BowAttack) {
            this.updateDepth();
        }
        this.appearanceManager.updateShadow(this.shadowManager, this.currentState, this.direction, appearanceAnimConfig);
    }

    /**
     * Called when the weapon asset's animation reaches a new frame (via onAnimationFrameChange callback).
     * At frame 2 (melee): attack sound, crit VFX, Storm Bringer; damage lands from `monster_take_damage`.
     * Bow release is timed at half swing via {@link scheduleBowArrowSpawn}.
     */
    private onWeaponAnimationFrameChange(relativeFrameIndex: number): void {
        if (this.currentState === PlayerState.MeleeAttack && relativeFrameIndex === 2) {
            // Always play regular attack sound
            this.soundTracker.playOnce(PLAYER_MELEE_ATTACK, calculateAnimationDuration(this.RUNNING_FRAME_COUNT, this.attackSpeed));
            // Play critical attack sound only for Knockback (melee or bow)
            const shouldPlayCriticalSound = this.attackType === AttackType.Knockback;
            if (shouldPlayCriticalSound && this.attackTarget) {
                const criticalSound = this.getGender() === Gender.FEMALE ? FEMALE_CRITICAL_ATTACK : MALE_CRITICAL_ATTACK;
                if (this.isLocalPlayer) {
                    this.soundTracker.playOnce(criticalSound);
                } else {
                    const spatialConfig = computeOtherPlayerSpatialConfig(
                        this.scene.game,
                        this.worldX,
                        this.worldY,
                        this.offsetX,
                        this.offsetY,
                        TILE_SIZE,
                    );
                    this.soundTracker.playOnce(criticalSound, undefined, spatialConfig);
                }
            }
            // Create critical strike projectile for melee knockback only
            if (this.attackType === AttackType.Knockback && this.attackTarget) {
                const sourcePixelX = this.getAnimatedPixelX();
                const sourcePixelY = this.getAnimatedPixelY() - TILE_SIZE;
                const targetPixelX = this.attackTarget.getAnimatedPixelX();
                const targetPixelY = this.attackTarget.getAnimatedPixelY() - this.attackTarget.getHeight() / 2;
                new CriticalStrikeProjectile(this.scene, {
                    sourcePixelX: sourcePixelX,
                    sourcePixelY: sourcePixelY,
                    targetPixelX: targetPixelX,
                    targetPixelY: targetPixelY,
                });
            }
            // Storm Bringer effect: create homing projectile when equipped weapon has STORM_BRINGER (melee only)
            const weaponDef = this.getTrackedWeaponDef();
            const equippedWeapon = this.equippedItemsForEffects[ItemTypes.WEAPON];
            if (equippedWeapon && this.attackTarget) {
                if (weaponDef?.effects?.some((e) => e.effect === ItemEffect.STORM_BRINGER)) {
                    new StormBringerEffect(this.scene, {
                        originPixelX: this.getAnimatedPixelX(),
                        originPixelY: this.getAnimatedPixelY(),
                        target: this.attackTarget,
                        speed: 500,
                    });
                }
            }
            // Melee damage is applied by `monster_take_damage`; bow damage uses the same path (rangedAttack + scheduled delay).
        }
    }

    private cancelPendingBowArrowSpawn(): void {
        this.rangedCombat.cancelPendingBowArrowSpawn();
    }

    /**
     * Bow: release sound and arrow VFX at half attack animation duration; damage lands through `monster_take_damage` just like melee.
     */
    private scheduleBowArrowSpawn(): void {
        this.cancelPendingBowArrowSpawn();
        const halfMs = calculateAnimationDuration(this.RUNNING_FRAME_COUNT, this.attackSpeed) / 2;
        this.rangedCombat.pendingBowArrowTimer = this.scene.time.delayedCall(halfMs, () => {
            this.rangedCombat.pendingBowArrowTimer = undefined;
            if (this.currentState !== PlayerState.BowAttack || !this.attackTarget) {
                return;
            }
            const weaponDef = this.getTrackedWeaponDef();
            if (weaponDef?.weaponType !== WeaponType.BOW) {
                return;
            }
            const target = this.attackTarget;
            const attackSoundDuration = calculateAnimationDuration(this.RUNNING_FRAME_COUNT, this.attackSpeed);
            this.soundTracker.playOnce(PLAYER_MELEE_ATTACK, attackSoundDuration);
            if (this.attackType === AttackType.Knockback) {
                const criticalSound = this.getGender() === Gender.FEMALE ? FEMALE_CRITICAL_ATTACK : MALE_CRITICAL_ATTACK;
                this.soundTracker.playOnce(criticalSound);
            }
            new ArrowProjectile(this.scene, {
                originPixelX: this.getAnimatedPixelX(),
                originPixelY: this.getAnimatedPixelY(),
                target,
                speed: this.arrowSpeedPxPerSec,
            });
        });
    }

    /**
     * Player reset/snap/course correction can clear a tile another actor still uses (one boolean per tile).
     * GameWorld re-applies monster and all player cells on HBMap.
     */
    private emitTileOccupancyReapplyRequested(): void {
        EventBus.emit(TILE_OCCUPANCY_REAPPLY_REQUESTED);
    }

    /**
     * Authoritative snap from server admin teleport (no reset-position sound, no stunlock).
     * Matches reset/cancel cleanup so movement, casting, and tile occupancy stay consistent.
     */
    public applyTeleport(x: number, y: number): void {
        this.cancelPendingBowArrowSpawn();
        this.attackTarget = undefined;
        this.dashMode = false;
        this.queuedDashModeForNextMove = undefined;
        this.correctionStartOffsetX = undefined;
        this.correctionStartOffsetY = undefined;
        this.correctionDurationMs = undefined;
        this.clearSpellState();
        if (this.currentState === PlayerState.Cast) {
            this.soundTracker.stopSound(PlayerState.Cast);
        }
        EventBus.emit(OUT_UI_CAST_REMOVED);
        this.stopMovementSounds();
        this.cancelMovement();
        this.pendingStunlockAfterMovement = false;
        this.movement.pendingSyncCommands = [];
        this.moving = false;
        this.offsetX = 0;
        this.offsetY = 0;
        this.destinationX = -1;
        this.destinationY = -1;
        this.moveReady = true;
        this.markCurrentTileFree();
        this.worldX = x;
        this.worldY = y;
        this.markCurrentTileOccupied();
        this.switchToIdle();
        this.updatePixelPosition();
        this.onPositionChanged(this.worldX, this.worldY);
        this.emitTileOccupancyReapplyRequested();
    }

    /**
     * Resets whatever state the player is in, switches to idle mode, moves to the given world
     * coordinates, and plays the reset position sound (C12 for male, C13 for female).
     * When reset-position sends remaining stunlock ms (e.g. stunlock movement violation), applies TakeDamage
     * animation and take-damage sound for that duration and stunlock so input stays blocked.
     */
    public resetPosition(x: number, y: number, remainingStunlockMs?: number): void {
        this.cancelPendingBowArrowSpawn();
        this.attackTarget = undefined;
        this.dashMode = false;
        this.queuedDashModeForNextMove = undefined;
        this.correctionStartOffsetX = undefined;
        this.correctionStartOffsetY = undefined;
        this.correctionDurationMs = undefined;
        this.clearSpellState();
        if (this.currentState === PlayerState.Cast) {
            this.soundTracker.stopSound(PlayerState.Cast);
        }
        EventBus.emit(OUT_UI_CAST_REMOVED);
        this.stopMovementSounds();
        this.cancelMovement();
        this.pendingStunlockAfterMovement = false;
        this.movement.pendingSyncCommands = [];
        this.moving = false;
        this.offsetX = 0;
        this.offsetY = 0;
        this.destinationX = -1;
        this.destinationY = -1;
        this.moveReady = true;
        this.markCurrentTileFree();
        this.worldX = x;
        this.worldY = y;
        this.markCurrentTileOccupied();
        this.switchToIdle();
        this.updatePixelPosition();
        this.onPositionChanged(this.worldX, this.worldY);
        this.emitTileOccupancyReapplyRequested();
        const resetSound = this.getGender() === Gender.FEMALE ? FEMALE_RESET_POSITION : MALE_RESET_POSITION;
        this.soundTracker.playOnce(resetSound);

        if (remainingStunlockMs !== undefined && remainingStunlockMs > 0) {
            // if (this.isLocalPlayer) {
            //     this.soundTracker.playOnce(PLAYER_TAKE_UNARMED_DAMAGE);
            // } else {
            //     const spatialConfig = this.calculateOtherPlayerSpatialConfig();
            //     this.soundTracker.playOnce(PLAYER_TAKE_UNARMED_DAMAGE, undefined, spatialConfig);
            // }
            this.applyInterruptDamageVisualAndStunlock(remainingStunlockMs);
        }
    }

    public startMovementStep(curX: number, curY: number, destX: number, destY: number, dashAttack: boolean): void {
        if (this.dead) {
            return;
        }

        this.movement.pendingRemoteIdleSwitchMs = undefined;
        const startPixelX = worldCellCenterPixelX(curX);
        const startPixelY = worldCellCenterPixelY(curY);
        const currentPixelX = this.getAnimatedPixelX();
        const currentPixelY = this.getAnimatedPixelY();
        const pixelDelta = Phaser.Math.Distance.Between(currentPixelX, currentPixelY, startPixelX, startPixelY);
        const isPixelPerfect = currentPixelX === startPixelX && currentPixelY === startPixelY;
        if (pixelDelta > TILE_SIZE) {
            this.snapToWorldPosition(curX, curY);
        } else if (!isPixelPerfect) {
            this.setOrReplaceDeferredMovement({ type: 'movementStep', curX, curY, destX, destY, dashAttack });
            return;
        }

        const direction = getNextDirection(curX, curY, destX, destY);
        if (direction === Direction.None) {
            return;
        }

        this.clearDeferredMovements();
        this.destinationX = destX;
        this.destinationY = destY;
        this.isDirectMovementMode = false;
        this.dashMode = dashAttack;
        super.move(direction);
    }

    /**
     * Remote players: instant move to authoritative cell (e.g. server teleport) without walk/run animation.
     */
    public snapRemoteToAuthoritativeCell(x: number, y: number): void {
        if (this.dead) {
            return;
        }
        this.snapToWorldPosition(x, y);
    }

    private snapToWorldPosition(x: number, y: number): void {
        this.attackTarget = undefined;
        this.dashMode = false;
        this.queuedDashModeForNextMove = undefined;
        this.correctionStartOffsetX = undefined;
        this.correctionStartOffsetY = undefined;
        this.correctionDurationMs = undefined;
        this.clearSpellState();
        this.movement.pendingSyncCommands = [];
        this.movement.pendingRemoteIdleSwitchMs = undefined;
        if (this.currentState === PlayerState.Cast) {
            this.soundTracker.stopSound(PlayerState.Cast);
        }
        this.stopMovementSounds();
        this.moving = false;
        this.moveReady = true;
        this.offsetX = 0;
        this.offsetY = 0;
        this.destinationX = -1;
        this.destinationY = -1;
        this.markCurrentTileFree();
        this.worldX = x;
        this.worldY = y;
        this.markCurrentTileOccupied();
        this.switchToIdle();
        this.updatePixelPosition();
        this.emitTileOccupancyReapplyRequested();
    }

    /**
     * Remote players: if the sprite is not at the center of the packet cell (e.g. mid-move lerp),
     * snap before playing attack so melee/bow origin matches the latest position.
     */
    private snapRemoteToAttackCellIfNeeded(worldX: number, worldY: number): void {
        const centerX = worldCellCenterPixelX(worldX);
        const centerY = worldCellCenterPixelY(worldY);
        if (this.getAnimatedPixelX() === centerX && this.getAnimatedPixelY() === centerY) {
            return;
        }
        this.snapToWorldPosition(worldX, worldY);
    }

    /**
     * Adjusts course to the corrected destination from the packet.
     * Cancels current state (attack, cast, etc.), then redirects movement from the current
     * pixel position towards the corrected cell without jumping. Recalculates offset and
     * direction from the current position.
     */
    public adjustCourse(curX: number, curY: number, destX: number, destY: number): void {
        if (this.dead) {
            return;
        }
        const isStillEnRouteToBlockedCell =
            this.moving &&
            getDistance(curX, curY, this.worldX, this.worldY) === 1 &&
            (this.offsetX !== 0 || this.offsetY !== 0);

        if (!isStillEnRouteToBlockedCell) {
            this.resetPosition(curX, curY);
            this.startCourseCorrectionStep(
                worldCellCenterPixelX(curX),
                worldCellCenterPixelY(curY),
                curX,
                curY,
                destX,
                destY
            );
            return;
        }

        const px = this.getAnimatedPixelX();
        const py = this.getAnimatedPixelY();
        this.correctionStartOffsetX = undefined;
        this.correctionStartOffsetY = undefined;

        this.attackTarget = undefined;
        this.dashMode = false;
        this.queuedDashModeForNextMove = undefined;
        this.clearSpellState();
        if (this.currentState === PlayerState.Cast) {
            this.soundTracker.stopSound(PlayerState.Cast);
        }
        EventBus.emit(OUT_UI_CAST_REMOVED);
        this.stopMovementSounds();
        this.cancelMovement();
        this.destinationX = -1;
        this.destinationY = -1;
        this.moveReady = false;
        this.switchToIdle();

        this.startCourseCorrectionStep(
            px,
            py,
            convertPixelPosToWorldPos(px),
            convertPixelPosToWorldPos(py),
            destX,
            destY
        );
    }

    private startCourseCorrectionStep(
        startPixelX: number,
        startPixelY: number,
        fromCellX: number,
        fromCellY: number,
        destX: number,
        destY: number
    ): void {
        const direction = getNextDirection(fromCellX, fromCellY, destX, destY);
        if (direction === Direction.None) {
            this.map.setTileOccupied(this.worldX, this.worldY, false);
            this.worldX = fromCellX;
            this.worldY = fromCellY;
            this.offsetX = 0;
            this.offsetY = 0;
            this.correctionStartOffsetX = undefined;
            this.correctionStartOffsetY = undefined;
            this.correctionDurationMs = undefined;
            this.map.setTileOccupied(fromCellX, fromCellY, true);
            this.updatePixelPosition();
            this.emitTileOccupancyReapplyRequested();
            return;
        }

        const [dx, dy] = getDirectionOffset(direction);
        const nextCellX = fromCellX + dx;
        const nextCellY = fromCellY + dy;
        const nextCenterX = worldCellCenterPixelX(nextCellX);
        const nextCenterY = worldCellCenterPixelY(nextCellY);

        this.map.setTileOccupied(this.worldX, this.worldY, false);
        this.map.setTileOccupied(destX, destY, true);

        this.worldX = nextCellX;
        this.worldY = nextCellY;
        this.direction = direction;
        this.destinationX = destX;
        this.destinationY = destY;
        this.moving = true;
        this.moveReady = false;

        this.offsetX = startPixelX - nextCenterX;
        this.offsetY = startPixelY - nextCenterY;
        this.correctionStartOffsetX = this.offsetX;
        this.correctionStartOffsetY = this.offsetY;
        const remainingDistanceRatio = Math.max(Math.abs(this.offsetX), Math.abs(this.offsetY)) / TILE_SIZE;
        this.correctionDurationMs = Math.max(1, remainingDistanceRatio * this.activeStepDurationMs);
        this.movementElapsedTime = 0;
        this.switchToMovement(true);
        this.updatePixelPosition();
        this.emitTileOccupancyReapplyRequested();
    }

    /**
     * Switches to idle state based on attack mode.
     * When attackMode is true: IdleCombatMode. When false: IdlePeaceMode.
     */
    public switchToIdle(): void {
        const idleState = this.attackMode ? PlayerState.IdleCombatMode : PlayerState.IdlePeaceMode;
        this.switchPlayerState(idleState, true);
    }

    /**
     * Sets attack mode (true = combat stance when idle, false = peace stance).
     * If currently in an idle state, updates the displayed stance immediately.
     */
    public setAttackMode(enabled: boolean): void {
        this.attackMode = enabled;
        if (this.currentState === PlayerState.IdlePeaceMode || this.currentState === PlayerState.IdleCombatMode) {
            this.switchToIdle();
        } else if (this.isInMovementState()) {
            this.switchToMovement(true);
        }
    }

    /**
     * Switches to movement state (Run, WalkPeaceMode, or WalkCombatMode).
     * Determines state, speed, sound, and animation based on runMode and attackMode.
     */
    public switchToMovement(forceUpdate: boolean = false): void {
        const config = this.getMovementConfig();
        this.switchPlayerState(config.state, forceUpdate);
    }

    /**
     * Returns true when in a movement state (Run, WalkPeaceMode, WalkCombatMode).
     */
    private isInMovementState(): boolean {
        return this.currentState === PlayerState.Run ||
            this.currentState === PlayerState.WalkPeaceMode ||
            this.currentState === PlayerState.WalkCombatMode;
    }

    /**
     * Gets movement config: state, duration, frame rate, sound key, and sound interval.
     * Sprite frame rate uses the active step duration while mid-move so the cycle finishes at cell arrival.
     * Footstep loop interval uses {@link movementSpeedMs} so run/walk toggles and speed changes apply immediately.
     */
    private getMovementConfig(): {
        state: PlayerState;
        movementSpeedMs: number;
        frameRate: number;
        soundKey: string;
        soundIntervalMs: number;
    } {
        const stepMsForAnim = this.moving ? this.activeStepDurationMs : this.movementSpeedMs;
        const frameRate = calculateFrameRateFromDuration(this.RUNNING_FRAME_COUNT, stepMsForAnim);
        const soundFrameRate = calculateFrameRateFromDuration(this.RUNNING_FRAME_COUNT, this.movementSpeedMs);
        const soundIntervalMs = calculateAnimationDuration(this.RUNNING_FRAME_COUNT, soundFrameRate) / 2;
        if (this.runMode) {
            return {
                state: PlayerState.Run,
                movementSpeedMs: stepMsForAnim,
                frameRate,
                soundKey: PLAYER_RUNNING,
                soundIntervalMs,
            };
        }
        const walkState = this.attackMode ? PlayerState.WalkCombatMode : PlayerState.WalkPeaceMode;
        return {
            state: walkState,
            movementSpeedMs: stepMsForAnim,
            frameRate,
            soundKey: PLAYER_WALKING,
            soundIntervalMs,
        };
    }

    /**
     * Sets run mode (true = run, false = walk at half speed).
     * If currently in a movement state, updates immediately.
     */
    public setRunMode(enabled: boolean): void {
        this.runMode = enabled;
        if (this.isInMovementState()) {
            this.switchToMovement(true);
        }
    }

    /**
     * Sets run/walk mode and effective per-tile duration together (UI toggle, server movement-state sync).
     * Single refresh avoids transient mismatch if {@link setMovementSpeed} and {@link setRunMode} run separately.
     */
    public setRunModeAndMovementSpeed(enabled: boolean, effectiveMovementSpeedMs: number): void {
        const clampedMs = Phaser.Math.Clamp(effectiveMovementSpeedMs, 100, 1000);
        this.runMode = enabled;
        this.movementSpeedMs = clampedMs;
        if (this.isInMovementState()) {
            this.switchToMovement(true);
        }
    }

    /**
     * Checks if the player is currently in attack state.
     */
    public isAttacking(): boolean {
        return this.currentState === PlayerState.MeleeAttack || this.currentState === PlayerState.BowAttack;
    }

    /**
     * Returns true when the player is in BowStance state (peace mode bow pose, no damage).
     */
    public isInBowStance(): boolean {
        return this.currentState === PlayerState.BowStance;
    }

    /**
     * Returns true when the player is in Cast state (spell cast animation playing).
     */
    public isCasting(): boolean {
        return this.currentState === PlayerState.Cast;
    }

    /**
     * Returns true when the player is in CastReady state (cast animation done, waiting for left click to target).
     */
    public isCastReady(): boolean {
        return this.currentState === PlayerState.CastReady;
    }

    /**
     * Sets the paralysis end timestamp. Movement commands are blocked until this time.
     */
    public setParalysisUntil(timestampMs: number): void {
        this.paralysisUntil = timestampMs;
    }

    /**
     * Returns true when the player is paralyzed (movement blocked by a packet timer).
     */
    public isParalyzed(): boolean {
        if (this.paralysisUntil === undefined) {
            return false;
        }
        return Date.now() < this.paralysisUntil;
    }

    /**
     * Returns true when a spell is pending (either in Cast, CastReady, or queued while moving).
     */
    public hasPendingSpell(): boolean {
        return this.pendingSpellId !== undefined || this.queuedCastSpellId !== undefined;
    }

    /**
     * Called when cast is commanded from UI. Always plays the cast animation before entering CastReady.
     * When moving, queues the cast until the player reaches the next cell.
     */
    public requestCast(spellId: number): void {
        if (this.dead || this.hasPendingSpell()) {
            return;
        }
        const spellName = this.resolveSpellName(spellId);
        if (!spellName) {
            return;
        }
        this.activeSpellName = spellName;
        if (this.moving) {
            this.queuedCastSpellId = spellId;
            this.cancelMovement();
            return;
        }
        this.pendingSpellId = spellId;
        this.cancelMovement();
        this.switchPlayerState(PlayerState.Cast, true);
        this.emitCastStarted(spellId);
    }

    /**
     * Called when left click occurs in CastReady. Confirms spell target and emits
     * PLAYER_CONFIRM_SPELL_TARGET. Returns true if handled.
     */
    public onLeftClickAt(cursorPixelX: number, cursorPixelY: number): boolean {
        if (this.pendingSpellId === undefined || this.currentState !== PlayerState.CastReady) {
            return false;
        }
        const spellId = this.pendingSpellId;
        this.pendingSpellId = undefined;
        if (this.currentState === PlayerState.CastReady) {
            this.switchToIdle();
        }
        this.activeSpellName = undefined;
        
        // Turn player towards the spell target direction (same logic as right-click in idle mode)
        const originPixelX = this.getAnimatedPixelX();
        const originPixelY = this.getAnimatedPixelY();
        const targetWorldX = convertPixelPosToWorldPos(cursorPixelX);
        const targetWorldY = convertPixelPosToWorldPos(cursorPixelY);
        
        const direction = getNextDirection(
            this.worldX,
            this.worldY,
            targetWorldX,
            targetWorldY
        );
        
        // Turn player towards cursor direction
        if (direction !== Direction.None) {
            this.turnTowardsDirection(direction);
        }
        
        EventBus.emit(PLAYER_CONFIRM_SPELL_TARGET, {
            spellId,
            originPixelX,
            originPixelY,
            targetPixelX: cursorPixelX,
            targetPixelY: cursorPixelY,
        });
        EventBus.emit(OUT_UI_CAST_REMOVED);
        return true;
    }

    /**
     * Self only: server rejected the cast request (arrived before minimum interval after cast start).
     */
    public onSpellCastRejected(): void {
        if (!this.isLocalPlayer) {
            return;
        }

        new FloatingText(this.scene, {
            text: 'Cast failed!',
            x: this.getAnimatedPixelX(),
            y: this.getAnimatedPixelY() - 3 * TILE_SIZE + 20,
            fontSize: 16,
            color: '#df5d2c',
            bold: true,
            horizontalOffset: -2,
            upwardTravelPxPerSec: 30,
            totalDurationMs: 2000,
            fadeDurationMs: 1000,
        });
        this.soundTracker.playOnceUntracked(SPELL_CAST_FAILED);

        if (!this.hasPendingSpell()) {
            return;
        }
        this.clearSpellState();
        if (this.currentState === PlayerState.Cast || this.currentState === PlayerState.CastReady) {
            if (this.currentState === PlayerState.Cast) {
                this.soundTracker.stopSound(PlayerState.Cast);
            }
            this.switchToIdle();
        }
        EventBus.emit(OUT_UI_CAST_REMOVED);
    }

    /**
     * Called when right click occurs. Cancels pending or queued spell. Returns true if handled.
     */
    public onRightClick(): boolean {
        if (!this.hasPendingSpell()) {
            return false;
        }
        this.clearSpellState();
        if (this.currentState === PlayerState.Cast || this.currentState === PlayerState.CastReady) {
            // Stop cast sound if currently casting
            if (this.currentState === PlayerState.Cast) {
                this.soundTracker.stopSound(PlayerState.Cast);
            }
            this.switchToIdle();
        }
        if (this.isLocalPlayer) {
            getNetworkManager(this.scene.game)?.sendSpellCastCancelRequest();
        }
        EventBus.emit(OUT_UI_CAST_REMOVED);
        return true;
    }

    /**
     * Cancels pending or queued spell without emitting. Called on shutdown.
     */
    public cancelPendingCast(): void {
        if (!this.hasPendingSpell()) {
            return;
        }
        this.clearSpellState();
        if (this.currentState === PlayerState.CastReady) {
            this.switchToIdle();
        }
        EventBus.emit(OUT_UI_CAST_REMOVED);
    }

    public queueRemoteSpellCastStart(spellName: string, castSpeedMs: number): void {
        if (this.isLocalPlayer) {
            return;
        }
        this.activeSpellName = spellName;
        this.castSpeed = Phaser.Math.Clamp(castSpeedMs, 200, 2000);
        this.snapRemoteToAttackCellIfNeeded(this.worldX, this.worldY);
        this.switchPlayerState(PlayerState.Cast, true);
        this.updatePixelPosition();
    }

    public clearRemoteSpellCast(): void {
        if (this.isLocalPlayer) {
            return;
        }
        this.activeSpellName = undefined;
        if (this.currentState === PlayerState.Cast || this.currentState === PlayerState.CastReady) {
            if (this.currentState === PlayerState.Cast) {
                this.soundTracker.stopSound(PlayerState.Cast);
            }
            this.switchToIdle();
            this.updatePixelPosition();
        }
    }

    /**
     * Self: floating damage text only (HP comes from world-state and `hp_updated`). Others: subtract HP, death, and floating text.
     */
    public override acceptDamage(damage: number): void {
        if (this.isLocalPlayer) {
            this.createDamageFloatingText(damage, this.getAnimatedPixelY() - 3 * TILE_SIZE + 10);
            return;
        }
        this.hp -= damage;
        if (this.hp < 1) {
            this.announceDeath();
        }
        this.createDamageFloatingText(damage, this.getAnimatedPixelY() - 3 * TILE_SIZE + 10);
    }

    /**
     * Self HP from InitialGameWorldState or `hp_updated`.
     */
    public setHp(hp: number, maxHp: number): void {
        this.hp = hp;
        this.maxHp = maxHp;
        if (this.isLocalPlayer) {
            this.updateHealthBar();
        }
    }

    /**
     * Monster damage packet: for self, HP comes from `hp_updated`; take-damage sound and interrupt/stun visuals happen here.
     * {@link MonsterAttackType.Interrupt} uses the same animation as stun but never applies stunlock (`0` ms in the packet).
     * {@link MonsterAttackType.Knockback} applies stunlock timing like stun plus knockback interpolation toward dest when fields are present.
     */
    public applyMonsterDamage(
        damage: number,
        attackType: number,
        stunDurationMs: number,
        knockbackDurationMs?: number,
        destX?: number,
        destY?: number,
        knockbackFromX?: number,
        knockbackFromY?: number,
    ): void {
        if (this.dead) {
            return;
        }

        if (attackType !== MonsterAttackType.NoInterrupt) {
            this.cancelPickupAndBowStanceFromMonsterInterrupt();
        }

        this.acceptDamage(damage);
        if (this.dead) {
            return;
        }

        const hasKnockback =
            attackType === MonsterAttackType.Knockback &&
            knockbackDurationMs !== undefined &&
            knockbackDurationMs > 0 &&
            destX !== undefined &&
            destY !== undefined;

        const hasStunlockStun = attackType === MonsterAttackType.Stun && stunDurationMs > 0;
        const playsInterruptAnimation = attackType === MonsterAttackType.Interrupt || hasStunlockStun;

        if (hasKnockback) {
            if (this.isLocalPlayer) {
                this.soundTracker.playOnce(PLAYER_TAKE_UNARMED_DAMAGE);
            } else {
                const spatialConfig = computeOtherPlayerSpatialConfig(
                    this.scene.game,
                    this.worldX,
                    this.worldY,
                    this.offsetX,
                    this.offsetY,
                    TILE_SIZE,
                );
                this.soundTracker.playOnce(PLAYER_TAKE_UNARMED_DAMAGE, undefined, spatialConfig);
            }
            this.applyMonsterKnockback(stunDurationMs, knockbackDurationMs, destX, destY, knockbackFromX, knockbackFromY);
            return;
        }

        if (playsInterruptAnimation) {
            if (this.isLocalPlayer) {
                this.soundTracker.playOnce(PLAYER_TAKE_UNARMED_DAMAGE);
            } else {
                const spatialConfig = computeOtherPlayerSpatialConfig(
                    this.scene.game,
                    this.worldX,
                    this.worldY,
                    this.offsetX,
                    this.offsetY,
                    TILE_SIZE,
                );
                this.soundTracker.playOnce(PLAYER_TAKE_UNARMED_DAMAGE, undefined, spatialConfig);
            }
            if (attackType === MonsterAttackType.Interrupt) {
                this.applyInterruptDamage(0);
            } else {
                this.applyInterruptDamage(stunDurationMs);
            }
        }
    }

    /**
     * Knockback hit: stunlock and take-damage animation use `stunDurationMs`; knockback interpolation uses `knockbackDurationMs`.
     */
    private applyMonsterKnockback(
        stunDurationMs: number,
        knockbackDurationMs: number,
        destX: number,
        destY: number,
        knockbackFromX?: number,
        knockbackFromY?: number,
    ): void {
        this.interruptDamageFromSpellsAndTarget();
        this.stunlockDurationMs = stunDurationMs;
        this.takeDamageAnimationDurationMs = stunDurationMs > 0 ? stunDurationMs : undefined;
        this.armLocalPlayerMovementStunlockFromNow(stunDurationMs);
        const facingFromX =
            knockbackFromX !== undefined && knockbackFromY !== undefined ? knockbackFromX : this.worldX;
        const facingFromY =
            knockbackFromX !== undefined && knockbackFromY !== undefined ? knockbackFromY : this.worldY;
        const knockbackFacing = getNextDirection(facingFromX, facingFromY, destX, destY);
        if (knockbackFacing !== Direction.None) {
            this.direction = knockbackFacing;
        }
        this.applyKnockbackMovement(destX, destY, knockbackDurationMs, knockbackFromX, knockbackFromY);
        this.switchPlayerState(PlayerState.TakeDamageWithKnockback, true);
        this.emitTileOccupancyReapplyRequested();
    }

    /**
     * Overrides GameObject.announceDeath. Remote-only HP path; local lethal damage uses {@link applyDeath}.
     */
    protected override announceDeath(): void {
        this.enterDeathState();
    }

    /**
     * Death: frees tile, enters Die state, and opens the death dialog for self only.
     */
    public applyDeath(): void {
        if (this.dead) {
            return;
        }
        this.enterDeathState();
        if (this.isLocalPlayer) {
            EventBus.emit(OUT_UI_PLAYER_DIED);
        }
    }

    public applySpawnedDeathState(): void {
        if (this.dead) {
            return;
        }
        this.enterDeathState(true);
    }

    private enterDeathState(skipAnimationAndSound: boolean = false): void {
        if (this.dead) {
            return;
        }
        this.clearTemporaryEffects();
        this.dead = true;
        this.localPlayerMovementStunlockUntilUnixMs = 0;
        this.attackTarget = undefined;
        this.dashMode = false;
        this.queuedDashModeForNextMove = undefined;
        this.soundTracker.stopAllSounds();
        this.cancelMovement();
        this.markCurrentTileFree();
        if (this.shadowManager) {
            this.shadowManager.destroy();
            this.shadowManager = undefined;
        }
        if (skipAnimationAndSound) {
            this.suppressNextStateSound = true;
        }
        this.switchPlayerState(PlayerState.Die, true);
        if (skipAnimationAndSound) {
            this.setCurrentStateToFinalFrame();
        }
    }

    private setCurrentStateToFinalFrame(): void {
        for (const asset of this.assets) {
            const frames = asset.sprite.anims.currentAnim?.frames;
            if (!frames || frames.length === 0) {
                continue;
            }
            asset.sprite.anims.setCurrentFrame(frames[frames.length - 1]);
            asset.sprite.anims.stop();
        }
    }

    /**
     * Returns true if the player is in dead state.
     */
    public isDead(): boolean {
        return this.dead;
    }

    public getGender(): Gender {
        return this.appearanceManager.getGender();
    }

    /**
     * Resurrection: restores position, HP, shadow, idle state, and resurrection VFX.
     */
    public applyResurrect(x: number, y: number, hp: number, maxHp: number): void {
        if (!this.dead) {
            return;
        }
        this.dead = false;
        this.setHp(hp, maxHp);
        this.worldX = x;
        this.worldY = y;
        this.updatePixelPosition();
        this.onPositionChanged(this.worldX, this.worldY);
        this.markCurrentTileOccupied();

        this.clearSpellState();

        const initialShadowSpriteSheetIndex = this.appearanceManager.getShadowSpriteSheetIndex(PlayerState.IdlePeaceMode, this.direction);
        this.shadowManager = new ShadowManager({
            scene: this.scene,
            shadowSpriteName: this.appearanceManager.getHumanSpriteName(),
            shadowSpriteSheetIndex: initialShadowSpriteSheetIndex,
            worldX: this.worldX,
            worldY: this.worldY,
            frameRate: this.IDLE_FRAME_RATE,
        });

        this.switchToIdle();

        drawEffect(this.scene, this.worldX, this.worldY, EFFECT_RESURRECTION);
    }

    /**
     * Drops queued remote pickup/bow stance and exits those states when interrupting monster damage arrives (`AttackType` is not `NoInterrupt`).
     */
    private cancelPickupAndBowStanceFromMonsterInterrupt(): void {
        this.movement.pendingSyncCommands = this.movement.pendingSyncCommands.filter((c) => c.type !== 'pickup' && c.type !== 'bowStance');
        if (this.currentState === PlayerState.PickUp) {
            this.remotePickupAnimationDurationMs = undefined;
            this.switchToIdle();
        } else if (this.currentState === PlayerState.BowStance) {
            this.remoteBowStanceAnimationDurationMs = undefined;
            this.attackTarget = undefined;
            this.switchToIdle();
        }
    }

    /**
     * Cancels attack target and pending spell/cast UI when damage interrupts the player.
     */
    private interruptDamageFromSpellsAndTarget(): void {
        this.attackTarget = undefined;

        if (this.hasPendingSpell()) {
            this.clearSpellState();
            if (this.currentState === PlayerState.Cast) {
                this.soundTracker.stopSound(PlayerState.Cast);
            }
            EventBus.emit(OUT_UI_CAST_REMOVED);
        }
    }

    /**
     * Sets stunlock duration for visuals, stretches take-damage animation to match, and switches to TakeDamage or TakeDamageOnMove.
     * Local player: arms movement stunlock deadline (wall clock + buffer); remote players use delta stunlock via GameObject hooks only when not local.
     */
    private applyInterruptDamageVisualAndStunlock(stunDurationMs: number): void {
        this.stunlockDurationMs = stunDurationMs;
        this.takeDamageAnimationDurationMs = stunDurationMs > 0 ? stunDurationMs : undefined;
        if (this.moving) {
            this.switchPlayerState(PlayerState.TakeDamageOnMove, true);
        } else {
            this.switchPlayerState(PlayerState.TakeDamage, true);
            if (stunDurationMs > 0 && !this.isLocalPlayer) {
                this.startStunlock();
            }
        }
        this.armLocalPlayerMovementStunlockFromNow(stunDurationMs);
    }

    /**
     * Packet-driven interrupt/stunlock (sound already played by caller). Does not cancel in-flight movement interpolation.
     */
    private applyInterruptDamage(stunDurationMs: number): void {
        this.interruptDamageFromSpellsAndTarget();
        this.applyInterruptDamageVisualAndStunlock(stunDurationMs);
    }

    /**
     * Gets the attack range in cells.
     */
    public getAttackRange(): number {
        return this.attackRange;
    }

    /**
     * Sets the attack speed from slider value (1-100).
     * Maps to attack animation FPS: 5 (min) to 30 (max).
     */
    public setAttackSpeed(sliderValue: number): void {
        const clampedValue = Phaser.Math.Clamp(sliderValue, 1, 100);
        this.attackSpeed = 5 + (clampedValue / 100) * (30 - 5);
    }

    /**
     * Sets melee animation rate from the full-swing duration in ms (matches `InitialGameWorldState.attack_speed_ms`).
     */
    public setAttackSpeedFromDurationMs(durationMs: number): void {
        const ms = Phaser.Math.Clamp(durationMs, 1, 60_000);
        this.attackSpeed = calculateFrameRateFromDuration(this.RUNNING_FRAME_COUNT, ms);
    }

    /** `arrow_speed_px_per_sec` from InitialGameWorldState. */
    public setArrowSpeedPxPerSec(pxPerSec: number): void {
        this.arrowSpeedPxPerSec = Phaser.Math.Clamp(pxPerSec, 1, 1_000_000);
    }

    /**
     * Plays another player's attack from a snapshot (melee or bow). Bow spawns a cosmetic arrow at half swing when the target monster exists in the scene.
     * `attack_type` drives knockback crit sound and melee {@link CriticalStrikeProjectile} via {@link onWeaponAnimationFrameChange}.
     */
    public playRemoteAttack(
        arrowSpeedPxPerSec: number,
        data: {
            direction: number;
            attackSpeedMs: number;
            ranged: boolean;
            target?: CombatTarget;
            worldX: number;
            worldY: number;
            attackType: number;
        },
    ): void {
        if (this.isLocalPlayer) {
            return;
        }
        this.cancelPendingBowArrowSpawn();
        this.snapRemoteToAttackCellIfNeeded(data.worldX, data.worldY);
        this.direction = toDirection(data.direction);
        if (this.remoteAttackSpeedBackup === undefined) {
            this.remoteAttackSpeedBackup = this.attackSpeed;
        }
        this.setAttackSpeedFromDurationMs(data.attackSpeedMs);
        this.attackType = attackTypeFromNetworkValue(data.attackType);
        if (data.target) {
            this.attackTarget = data.target;
        }
        if (data.ranged) {
            this.switchPlayerState(PlayerState.BowAttack, true);
            if (data.target) {
                const target = data.target;
                const attackTypeForBow = data.attackType;
                const halfMs = calculateAnimationDuration(this.RUNNING_FRAME_COUNT, this.attackSpeed) / 2;
                this.rangedCombat.pendingBowArrowTimer = this.scene.time.delayedCall(halfMs, () => {
                    this.rangedCombat.pendingBowArrowTimer = undefined;
                    if (this.currentState !== PlayerState.BowAttack) {
                        return;
                    }
                    if (attackTypeForBow === AttackType.Knockback) {
                        const criticalSound = this.getGender() === Gender.FEMALE ? FEMALE_CRITICAL_ATTACK : MALE_CRITICAL_ATTACK;
                        const spatialConfig = computeOtherPlayerSpatialConfig(
                            this.scene.game,
                            this.worldX,
                            this.worldY,
                            this.offsetX,
                            this.offsetY,
                            TILE_SIZE,
                        );
                        this.soundTracker.playOnce(criticalSound, undefined, spatialConfig);
                    }
                    new ArrowProjectile(this.scene, {
                        originPixelX: this.getAnimatedPixelX(),
                        originPixelY: this.getAnimatedPixelY(),
                        target,
                        speed: arrowSpeedPxPerSec,
                        onReachDestination: () => {
                            /* Cosmetic only; combat resolves from packets. */
                        },
                    });
                });
            }
        } else {
            this.switchPlayerState(PlayerState.MeleeAttack, true);
        }
        this.updatePixelPosition();
    }

    /**
     * Sets the attack range in cells (1-30).
     */
    public setAttackRange(range: number): void {
        this.attackRange = Phaser.Math.Clamp(range, 1, 30);
    }

    /**
     * Enables or disables spawn protection (green glow on the base body sprite only). Berserk overlay can show at the same time.
     */
    public setSpawnProtectionEffect(enabled: boolean): void {
        this.appearanceManager.setSpawnProtectionEffect(enabled);
        this.appearanceManager.getHumanBodyAsset()?.setSpawnProtectionGlow(enabled);
    }

    /** True while server spawn protection is active (not a valid PvP target for others). */
    public hasSpawnProtection(): boolean {
        return this.appearanceManager.hasSpawnProtectionEffect();
    }

    /**
     * Sets or clears the disconnected visual state for remote players.
     */
    public setDisconnected(disconnected: boolean): void {
        this.disconnected = disconnected;
        this.appearanceManager.setDisconnectedEffect(disconnected);
    }

    /**
     * Sets the attack type (NoInterrupt, Stun, or Knockback).
     */
    public setAttackType(attackType: AttackType): void {
        this.attackType = attackType;
    }

    public setPlayerId(playerId: string): void {
        this.playerId = playerId;
    }

    public getPlayerId(): string | undefined {
        return this.playerId;
    }

    public isLocalCharacter(): boolean {
        return this.isLocalPlayer;
    }

    public hasInvisibilityBuff(): boolean {
        return this.hasTemporaryEffect(TemporaryEffectType.Invisibility);
    }

    protected override onTemporaryEffectsChanged(): void {
        this.applyInvisibilityBuffIfPresent();
        this.appearanceManager.setChilledEffect(this.hasTemporaryEffect(TemporaryEffectType.Chill));
        this.appearanceManager.setBerserkEffect(this.hasTemporaryEffect(TemporaryEffectType.Berserk));
    }

    private applyInvisibilityBuffIfPresent(): void {
        const inv = this.hasInvisibilityBuff();
        if (this.isLocalPlayer) {
            this.appearanceManager.setInvisibilityLocalHalfOpacity(inv);
            this.appearanceManager.setInvisibilityRemoteHidden(false);
        } else {
            this.appearanceManager.setInvisibilityLocalHalfOpacity(false);
            this.appearanceManager.setInvisibilityRemoteHidden(inv);
        }
        if (this.shadowManager) {
            if (!this.isLocalPlayer && inv) {
                this.shadowManager.setAlpha(0);
            } else {
                this.shadowManager.setAlpha(1);
            }
        }
    }

    public setCharacterName(name: string): void {
        this.characterName = name.trim();
    }

    public getCharacterName(): string {
        return this.characterName;
    }

    /**
     * Gets the current attack target (monster or player to pathfind towards when out of range).
     */
    public getAttackTarget(): CombatTarget | undefined {
        return this.attackTarget;
    }

    /**
     * Clears the attack target (e.g., when target monster dies).
     */
    public clearAttackTarget(): void {
        this.attackTarget = undefined;
    }

    /**
     * Attempts to attack the specified monster.
     * When moving between cells: stores target so attack triggers when reaching next cell.
     * When in attack state or bow stance: rejects (no new commands).
     * When at cell and in range: switches to attack state (combat mode) or bow stance (peace mode).
     *
     * @param target - The monster or player to attack
     */
    public attack(target: CombatTarget): void {
        if (this.dead ||
            target.isDead() ||
            this.isAttacking() ||
            this.isInBowStance() ||
            this.isCastReady() ||
            this.currentState === PlayerState.TakeDamage ||
            this.currentState === PlayerState.TakeDamageOnMove ||
            this.currentState === PlayerState.TakeDamageWithKnockback ||
            this.isStunlocked()) {
            return;
        }
        if (this.moving) {
            // Store target - processMovement will check range when we reach the next cell
            this.attackTarget = target;
            return;
        }

        const distance = getDistance(this.worldX, this.worldY, target.getWorldX(), target.getWorldY());

        if (distance <= this.attackRange) {
            if (this.attackMode) {
                this.startAttack(target);
            } else {
                this.startBowStance(target);
            }
        } else {
            this.attackTarget = target;
        }
    }

    /**
     * Starts the melee attack animation facing the monster.
     */
    private startAttack(target: CombatTarget): void {
        if (this.dead) {
            return;
        }
        this.cancelMovement();
        this.attackTarget = target;

        // Clear movement state when switching to attack; startAttack can be called from
        // processMovement() when reaching a cell (isMoving still true in that path). If we
        // don't clear it, after attack ends super.update() will run the movement block and
        // animate from the adjacent cell into the current cell.
        this.moving = false;
        this.offsetX = 0;
        this.offsetY = 0;

        const attackDirection = getNextDirection(this.worldX, this.worldY, target.getWorldX(), target.getWorldY());
        if (attackDirection !== Direction.None && attackDirection !== this.direction) {
            this.direction = attackDirection;
            this.updateDepth();
        }

        const weaponDef = this.getTrackedWeaponDef();
        const attackState = weaponDef?.weaponType === WeaponType.BOW ? PlayerState.BowAttack : PlayerState.MeleeAttack;
        this.switchPlayerState(attackState, true);
        if (this.isLocalPlayer && !this.dead) {
            const ranged = weaponDef?.weaponType === WeaponType.BOW;
            if (isMonsterCombatTarget(target)) {
                getNetworkManager(this.scene.game)?.sendPlayerAttackedMonster(target.getMonsterId(), ranged, this.attackType);
            } else {
                const targetPlayerId = target.getPlayerId();
                if (targetPlayerId) {
                    getNetworkManager(this.scene.game)?.sendPlayerAttackedPlayer(targetPlayerId, ranged, this.attackType);
                }
            }
        }
        if (weaponDef?.weaponType === WeaponType.BOW) {
            this.scheduleBowArrowSpawn();
        }
        // Refresh sprite positions after state switch; different animation frames use different
        // pivot offsets which can cause a visual jump if base position isn't synced
        this.updatePixelPosition();
    }

    /**
     * Starts the bow stance animation facing the monster (peace mode only).
     * No damage is delivered; armaments are hidden during the animation.
     */
    private startBowStance(target: CombatTarget): void {
        this.cancelMovement();
        this.attackTarget = target;

        this.moving = false;
        this.offsetX = 0;
        this.offsetY = 0;

        const attackDirection = getNextDirection(this.worldX, this.worldY, target.getWorldX(), target.getWorldY());
        if (attackDirection !== Direction.None && attackDirection !== this.direction) {
            this.direction = attackDirection;
            this.updateDepth();
        }

        if (this.isLocalPlayer) {
            getNetworkManager(this.scene.game)?.sendPlayerBowStanceRequested(this.direction);
        }

        this.switchPlayerState(PlayerState.BowStance, true);
        this.updatePixelPosition();
    }

    /**
     * Switches to PickUp state when the player clicks on their current cell.
     * Plays the pickup animation once at idle speed, then returns to idle.
     * Repeated clicks on the same cell will trigger PickUp again (looping).
     * Armaments are hidden during PickUp (no animations for them) and restored when returning to idle.
     */
    public requestPickUp(): void {
        if (this.dead ||
            this.isAttacking() ||
            this.isInBowStance() ||
            this.isCasting() ||
            this.isCastReady() ||
            this.currentState === PlayerState.PickUp ||
            this.currentState === PlayerState.TakeDamageOnMove ||
            this.currentState === PlayerState.TakeDamageWithKnockback ||
            this.isStunlocked() ||
            this.moving) {
            return;
        }
        this.cancelMovement();
        if (this.isLocalPlayer) {
            getNetworkManager(this.scene.game)?.sendPlayerPickupRequested(this.direction);
            getNetworkManager(this.scene.game)?.sendPlayerItemPickupRequested();
        }
        this.switchPlayerState(PlayerState.PickUp, true);
    }

    /** Local: pickup duration from InitialGameWorldState. */
    public setPlayerPickupAnimationMs(ms: number): void {
        this.playerPickupAnimationMs = ms;
    }

    /** Local: bow stance duration from InitialGameWorldState. */
    public setPlayerBowAnimationDurationMs(ms: number): void {
        this.playerBowAnimationDurationMs = ms;
    }

    /** Remote: enqueue a pickup animation to play when idle and not blocked by other states. */
    public queueRemotePickup(directionValue: number, animationTimeMs: number): void {
        if (this.isLocalPlayer) {
            return;
        }
        this.movement.pendingSyncCommands.push({ type: 'pickup', direction: directionValue, animationTimeMs });
    }

    /** Remote: enqueue a bow stance animation to play when idle and not blocked by other states. */
    public queueRemoteBowStance(directionValue: number, animationTimeMs: number): void {
        if (this.isLocalPlayer) {
            return;
        }
        this.movement.pendingSyncCommands.push({ type: 'bowStance', direction: directionValue, animationTimeMs });
    }

    /**
     * Turns the player to face the specified direction without moving.
     * Only allowed when idle (IdlePeaceMode or IdleCombatMode); blocked when in take damage, stunlock, or other states.
     * Returns true when the facing value changed (used to avoid spamming idle-direction packets while the mouse is held).
     */
    public turnTowardsDirection(direction: Direction): boolean {
        if (this.dead ||
            (this.currentState !== PlayerState.IdlePeaceMode && this.currentState !== PlayerState.IdleCombatMode) ||
            direction === Direction.None ||
            this.isStunlocked()) {
            return false;
        }

        if (this.direction !== direction) {
            this.direction = direction;
            this.updateDepth();
            this.switchToIdle();
            return true;
        }
        return false;
    }

    /** Local player: defer idle facing + packet send until movement has fully stopped. */
    public queueLocalIdleDirectionForWhenStopped(direction: Direction): void {
        if (!this.isLocalPlayer || direction === Direction.None) {
            return;
        }
        this.movement.pendingSyncCommands.push({ type: 'idleDirection', direction });
    }

    /** Remote: idle facing arrived while still moving; applied when movement stops. */
    public queueIdleFacingForWhenAligned(directionValue: number): void {
        if (this.isLocalPlayer) {
            return;
        }
        this.movement.pendingSyncCommands.push({ type: 'idleDirection', direction: directionValue });
    }

    private setOrReplaceDeferredMovement(step: Extract<PendingSyncCommand, { type: 'movementStep' }>): void {
        const idx = this.movement.pendingSyncCommands.findIndex((c) => c.type === 'movementStep');
        if (idx >= 0) {
            this.movement.pendingSyncCommands[idx] = step;
        } else {
            this.movement.pendingSyncCommands.unshift(step);
        }
    }

    private clearDeferredMovements(): void {
        this.movement.pendingSyncCommands = this.movement.pendingSyncCommands.filter((c) => c.type !== 'movementStep');
    }

    private drainLocalPendingIdleDirectionsWhenIdleAndStopped(): void {
        if (!this.isLocalPlayer || this.dead || this.moving) {
            return;
        }
        if (
            (this.currentState !== PlayerState.IdlePeaceMode && this.currentState !== PlayerState.IdleCombatMode) ||
            this.isStunlocked()
        ) {
            return;
        }
        while (this.movement.pendingSyncCommands.length > 0) {
            const head = this.movement.pendingSyncCommands[0];
            if (head.type !== 'idleDirection') {
                break;
            }
            this.movement.pendingSyncCommands.shift();
            const d = toDirection(head.direction);
            if (d !== Direction.None) {
                this.turnTowardsDirection(d);
                getNetworkManager(this.scene.game)?.requestChangePlayerIdleDirection(head.direction);
            }
        }
    }

    private drainRemotePendingCommandsWhenStopped(): void {
        if (this.isLocalPlayer || this.dead || this.moving) {
            return;
        }
        while (this.movement.pendingSyncCommands.length > 0) {
            const head = this.movement.pendingSyncCommands[0];
            if (head.type === 'idleDirection') {
                this.movement.pendingSyncCommands.shift();
                const rd = toDirection(head.direction);
                if (rd !== Direction.None) {
                    this.applyIdleFacing(rd);
                }
                continue;
            }
            if (head.type === 'pickup') {
                if (!this.canApplyRemotePickupNow()) {
                    break;
                }
                this.movement.pendingSyncCommands.shift();
                this.applyRemotePickup(head.direction, head.animationTimeMs);
                continue;
            }
            if (head.type === 'bowStance') {
                if (!this.canApplyRemoteBowStanceNow()) {
                    break;
                }
                this.movement.pendingSyncCommands.shift();
                this.applyRemoteBowStance(head.direction, head.animationTimeMs);
                continue;
            }
            break;
        }
    }

    private canApplyRemotePickupNow(): boolean {
        return (
            !this.dead &&
            !this.isAttacking() &&
            !this.isInBowStance() &&
            !this.isCasting() &&
            !this.isCastReady() &&
            this.currentState !== PlayerState.PickUp &&
            this.currentState !== PlayerState.TakeDamageOnMove &&
            this.currentState !== PlayerState.TakeDamageWithKnockback &&
            !this.isStunlocked()
        );
    }

    private applyRemotePickup(directionValue: number, animationTimeMs: number): void {
        const rd = toDirection(directionValue);
        if (rd !== Direction.None && rd !== this.direction) {
            this.direction = rd;
            this.updateDepth();
        }
        this.remotePickupAnimationDurationMs = animationTimeMs;
        this.switchPlayerState(PlayerState.PickUp, true);
        this.updatePixelPosition();
    }

    private canApplyRemoteBowStanceNow(): boolean {
        return (
            !this.dead &&
            !this.isAttacking() &&
            !this.isInBowStance() &&
            !this.isCasting() &&
            !this.isCastReady() &&
            this.currentState !== PlayerState.PickUp &&
            this.currentState !== PlayerState.TakeDamageOnMove &&
            this.currentState !== PlayerState.TakeDamageWithKnockback &&
            !this.isStunlocked()
        );
    }

    private applyRemoteBowStance(directionValue: number, animationTimeMs: number): void {
        const rd = toDirection(directionValue);
        if (rd !== Direction.None && rd !== this.direction) {
            this.direction = rd;
            this.updateDepth();
        }
        this.remoteBowStanceAnimationDurationMs = animationTimeMs;
        this.switchPlayerState(PlayerState.BowStance, true);
        this.updatePixelPosition();
    }

    /** Remote: apply idle facing from a packet (any non-local state). */
    public applyIdleFacing(direction: Direction): void {
        if (this.isLocalPlayer || direction === Direction.None) {
            return;
        }
        if (this.direction === direction) {
            return;
        }
        this.direction = direction;
        this.updateDepth();
        if (this.currentState === PlayerState.IdlePeaceMode || this.currentState === PlayerState.IdleCombatMode) {
            this.switchToIdle();
        } else {
            this.switchPlayerState(this.currentState, true);
        }
        this.updatePixelPosition();
    }

    /**
     * Overrides move to send `request_movement` before moving to the next cell.
     */
    protected override move(direction: Direction): void {
        if (this.dead) {
            return;
        }
        if (this.isLocalPlayer && this.isParalyzed()) {
            return;
        }
        if (this.isLocalPlayer && this.isStunlocked()) {
            return;
        }
        const [dx, dy] = getDirectionOffset(direction);
        const nextX = this.worldX + dx;
        const nextY = this.worldY + dy;
        if (this.isLocalPlayer) {
            const dashAttackMonsterId =
                this.dashMode && this.attackTarget && isMonsterCombatTarget(this.attackTarget)
                    ? this.attackTarget.getMonsterId()
                    : undefined;
            const dashAttackPlayerId =
                this.dashMode && this.attackTarget && !isMonsterCombatTarget(this.attackTarget)
                    ? this.attackTarget.getPlayerId()
                    : undefined;
            getNetworkManager(this.scene.game)?.requestMovement(this.worldX, this.worldY, nextX, nextY, {
                dashAttack: dashAttackMonsterId !== undefined || dashAttackPlayerId !== undefined,
                monsterId: dashAttackMonsterId,
                playerId: dashAttackPlayerId,
                attackType: dashAttackMonsterId !== undefined || dashAttackPlayerId !== undefined ? this.attackType : undefined,
            });
            const gameWorldScene = this.scene as GameWorldScene;
            if (gameWorldScene.tryBeginTeleportAt(nextX, nextY)) {
                return;
            }
        }
        super.move(direction);
    }

    /**
     * Overrides beforeMove to enter dash mode when moving one cell toward attack target in run mode.
     */
    protected override beforeMove(direction: Direction): boolean {
        if (!this.isLocalPlayer && this.queuedDashModeForNextMove !== undefined) {
            this.dashMode = this.queuedDashModeForNextMove;
            this.queuedDashModeForNextMove = undefined;
            return false;
        }
        if (this.attackTarget?.isDead()) {
            this.attackTarget = undefined;
            return false;
        }
        if (!this.attackTarget || !this.attackMode || !this.runMode || !playerDialogStore.state.allowDashAttack) {
            return false;
        }
        const weaponDef = this.getTrackedWeaponDef();
        if (weaponDef?.weaponType === WeaponType.BOW) {
            return false;
        }
        const distance = getDistance(this.worldX, this.worldY, this.attackTarget.getWorldX(), this.attackTarget.getWorldY());
        if (distance !== this.attackRange + 1) {
            return false;
        }
        this.dashMode = true;
        this.move(direction);
        return true;
    }

    /**
     * Overrides processMovement to check attack range when reaching a cell.
     * If attack target is in range, attack instead of moving.
     */
    protected override processMovement(): void {
        if (this.dead) {
            return;
        }
        if (this.isLocalPlayer && this.isStunlocked()) {
            return;
        }
        if (this.dashMode) {
            this.dashMode = false;
            const hasQueuedRemoteStep =
                !this.isLocalPlayer &&
                this.destinationX !== -1 &&
                this.destinationY !== -1 &&
                (this.worldX !== this.destinationX || this.worldY !== this.destinationY);
            if (hasQueuedRemoteStep) {
                super.processMovement();
                return;
            }
            this.attackTarget = undefined;
            this.destinationX = -1;
            this.destinationY = -1;
            this.isDirectMovementMode = false;
            this.moving = false;
            this.moveReady = true;
            this.offsetX = 0;
            this.offsetY = 0;
            return;
        }
        if (this.attackTarget?.isDead()) {
            this.attackTarget = undefined;
        }
        if (this.attackTarget &&
            this.currentState !== PlayerState.TakeDamage &&
            this.currentState !== PlayerState.TakeDamageOnMove &&
            this.currentState !== PlayerState.TakeDamageWithKnockback &&
            !this.isStunlocked()) {
            const distance = getDistance(this.worldX, this.worldY, this.attackTarget.getWorldX(), this.attackTarget.getWorldY());
            if (distance <= this.attackRange) {
                if (this.attackMode) {
                    this.startAttack(this.attackTarget);
                } else {
                    this.startBowStance(this.attackTarget);
                }
                return;
            }
        }
        super.processMovement();
    }

    /**
     * Overrides setDestination to reject when attacking.
     */
    public override setDestination(
        destinationX: number,
        destinationY: number,
        useDirectMovement: boolean = false,
        cameraCenterPixelX?: number,
        cameraCenterPixelY?: number,
        cursorPixelX?: number,
        cursorPixelY?: number
    ): void {
        if (this.isParalyzed() ||
            this.dead ||
            this.isAttacking() ||
            this.isInBowStance() ||
            this.isCasting() ||
            this.isCastReady() ||
            this.currentState === PlayerState.PickUp ||
            this.currentState === PlayerState.TakeDamageOnMove ||
            this.currentState === PlayerState.TakeDamageWithKnockback ||
            this.isStunlocked()) {
            return;
        }
        super.setDestination(destinationX, destinationY, useDirectMovement, cameraCenterPixelX, cameraCenterPixelY, cursorPixelX, cursorPixelY);
    }

    /**
     * Overrides cancelMovement to reject when attacking.
     */
    public override cancelMovement(): void {
        if (this.isAttacking() || this.isInBowStance()) {
            return;
        }
        super.cancelMovement();
        if (this.isLocalPlayer) {
            this.movement.pendingSyncCommands = this.movement.pendingSyncCommands.filter((c) => c.type !== 'idleDirection');
        }
    }

    /**
     * Overrides update to handle attack and take damage animation completion.
     */
    public override update(delta: number): void {
        if (this.dead) {
            this.hideHealthBar();
            const accessoryAssetIndex = this.appearanceManager.getAccessoryAssetIndex();
            if (accessoryAssetIndex >= 0 &&
                this.appearanceManager.hasAccessory() &&
                this.assets[accessoryAssetIndex].sprite.visible &&
                !this.assets[accessoryAssetIndex].isAnimationPlaying()) {
                this.assets[accessoryAssetIndex].setVisible(false);
            }
            return;
        }
        this.updateHealthBar();
        this.updateStarTwinkle(delta);
        if ((this.currentState === PlayerState.MeleeAttack || this.currentState === PlayerState.BowAttack) && !this.dashMode) {
            if (!this.isPrimaryAssetAnimationPlaying()) {
                this.attackTarget = undefined;
                this.switchToIdle();
            }
            return;
        }

        if (this.currentState === PlayerState.BowStance && !this.isPrimaryAssetAnimationPlaying()) {
            this.attackTarget = undefined;
            this.switchToIdle();
            return;
        }

        if (this.currentState === PlayerState.PickUp && !this.isPrimaryAssetAnimationPlaying()) {
            this.switchToIdle();
            return;
        }

        if (this.currentState === PlayerState.Cast && !this.isPrimaryAssetAnimationPlaying()) {
            this.switchPlayerState(PlayerState.CastReady);
            if (this.isLocalPlayer) {
                EventBus.emit(OUT_UI_CAST_READY);
            }
            return;
        }

        if (this.currentState === PlayerState.TakeDamage || this.currentState === PlayerState.TakeDamageOnMove) {
            const minDwellMs = this.takeDamageAnimationDurationMs ?? 0;
            const elapsedMs = performance.now() - this.takeDamageVisualEnteredAtMs;
            const dwellSatisfied = minDwellMs <= 0 ? true : elapsedMs >= minDwellMs;
            const animationDone = !this.isPrimaryAssetAnimationPlaying();
            const canLeaveTakeDamageVisual =
                dwellSatisfied && (minDwellMs > 0 ? true : animationDone);
            if (canLeaveTakeDamageVisual) {
                if (this.moving) {
                    if (this.stunlockDurationMs > 0 && !this.isLocalPlayer) {
                        this.setPendingStunlockAfterMovement();
                    }
                    this.switchToMovement(true);
                } else {
                    this.switchToIdle();
                }
            }
        }

        if (this.currentState === PlayerState.TakeDamageWithKnockback && this.isKnockbackActive()) {
            this.updateKnockbackVisual(delta);
        }

        if (this.currentState === PlayerState.TakeDamageWithKnockback &&
            !this.isKnockbackActive() && !this.isPrimaryAssetAnimationPlaying()) {
            if (!this.isLocalPlayer && this.stunlockElapsedMs < 0 && this.stunlockDurationMs > 0) {
                this.startStunlock();
            }
            this.switchToIdle();
        }

        if (!this.updateCourseCorrectionMovement(delta)) {
            // GameObject.update overwrites offsetX/Y every frame while isMoving; that destroys knockback slide offsets.
            const knockbackSlideActive =
                this.currentState === PlayerState.TakeDamageWithKnockback && this.isKnockbackActive();
            if (!knockbackSlideActive) {
                super.update(delta);
            }
        }

        this.drainLocalPendingIdleDirectionsWhenIdleAndStopped();
        this.drainRemotePendingCommandsWhenStopped();

        if (!this.isLocalPlayer &&
            !this.moving &&
            this.isInMovementState() &&
            this.movement.pendingRemoteIdleSwitchMs !== undefined) {
            this.movement.pendingRemoteIdleSwitchMs = Math.max(0, this.movement.pendingRemoteIdleSwitchMs - delta);
            if (this.movement.pendingRemoteIdleSwitchMs === 0) {
                this.movement.pendingRemoteIdleSwitchMs = undefined;
                this.switchToIdle();
            }
        }

        this.updateStunlock(delta);
    }

    /**
     * Ticks the redirected movement step from the player's current pixel position.
     * This keeps the sideways offset intact until the corrected cell is reached.
     */
    private updateCourseCorrectionMovement(delta: number): boolean {
        if (!this.moving || this.correctionStartOffsetX === undefined || this.correctionStartOffsetY === undefined) {
            return false;
        }

        this.movementElapsedTime += delta;
        const correctionDurationMs = this.correctionDurationMs ?? this.activeStepDurationMs;
        const progress = Math.min(this.movementElapsedTime / correctionDurationMs, 1.0);
        this.offsetX = this.correctionStartOffsetX * (1 - progress);
        this.offsetY = this.correctionStartOffsetY * (1 - progress);
        this.updatePixelPosition();

        if (progress < 1.0) {
            return true;
        }

        this.moveReady = true;
        this.movementElapsedTime = 0;
        this.offsetX = 0;
        this.offsetY = 0;
        this.correctionStartOffsetX = undefined;
        this.correctionStartOffsetY = undefined;
        this.correctionDurationMs = undefined;

        const shouldPause = this.shouldPauseMovementWhenCellReached();
        this.onCellReached();

        if (this.destinationX === -1 || this.destinationY === -1) {
            this.moving = false;
            this.switchState(GameObjectState.Idle);
            this.updatePixelPosition();
        } else if (this.worldX === this.destinationX && this.worldY === this.destinationY) {
            this.moving = false;
            this.destinationX = -1;
            this.destinationY = -1;
            this.isPrevMoveBlocked = false;
            this.prevMoveX = -1;
            this.prevMoveY = -1;
            if (this.autoSwitchToIdle) {
                this.switchState(GameObjectState.Idle);
            }
            this.updatePixelPosition();
        } else if (shouldPause) {
            this.moving = false;
            this.switchState(GameObjectState.Idle);
            this.updatePixelPosition();
        } else {
            this.processMovement();
            this.updatePixelPosition();
        }

        return true;
    }

    /**
     * When STAR_TWINKLE is equipped, spawns random sparkles above the player at intervals.
     */
    private updateStarTwinkle(delta: number): void {
        if (!hasEquippedItemEffect(this.equippedItemsForEffects, ItemEffect.STAR_TWINKLE)) {
            this.starTwinkleAccumulatorMs = 0;
            return;
        }
        this.starTwinkleAccumulatorMs += delta;
        const nextIntervalMs = 150 + Phaser.Math.FloatBetween(0, 200);
        if (this.starTwinkleAccumulatorMs < nextIntervalMs) {
            return;
        }
        this.starTwinkleAccumulatorMs = 0;
        const px = this.getAnimatedPixelX();
        const py = this.getAnimatedPixelY();
        const offsetX = Phaser.Math.FloatBetween(-15, 15);
        const offsetY = Phaser.Math.FloatBetween(-60, 0);
        drawEffectAtPixelCoords(this.scene, px + offsetX, py + offsetY, EFFECT_SPARKLE, {
            usePlayerDepthForDepth: true,
            playerWorldY: this.worldY,
        });
    }

    /**
     * Implements abstract method from GameObject to switch state.
     * When switching to Idle after reaching a cell with a queued cast, executes the cast instead.
     */
    protected override switchState(state: GameObjectState, _forceUpdate: boolean = false): void {
        switch (state) {
            case GameObjectState.Idle:
                if (this.queuedCastSpellId !== undefined) {
                    // Player reached next cell with queued cast - stop run and switch to cast
                    const spellId = this.queuedCastSpellId;
                    this.queuedCastSpellId = undefined;
                    this.pendingSpellId = spellId;
                    this.switchPlayerState(PlayerState.Cast, true);
                    this.emitCastStarted(spellId);
                } else {
                    this.switchToIdle();
                }
                break;
            case GameObjectState.Move:
                // Always refresh movement appearance each step: same PlayerState (e.g. Run→Run) when
                // direction unchanged would otherwise skip applyStateAppearance, leaving animation FPS
                // tied to the previous step (e.g. after run/walk toggle updates movementSpeedMs).
                if (this.dashMode) {
                    this.switchPlayerState(PlayerState.MeleeAttack, true);
                } else {
                    this.switchToMovement(true);
                }
                break;
        }
    }

    /**
     * Overrides hook method from GameObject to update registry and emit when position changes.
     */
    protected override onPositionChanged(newX: number, newY: number): void {
        if (!this.isLocalPlayer) {
            return;
        }

        setPlayerPosition(this.scene.game, newX, newY);
        EventBus.emit(PLAYER_POSITION_CHANGED, { x: newX, y: newY });
    }

    /**
     * Overrides hook from GameObject. When player reaches a new cell while moving, spawns footsteps effect.
     * Uses wet splash when raining, otherwise dry footsteps.
     */
    protected override onCellReached(): void {
        if (!this.dead && this.currentState === PlayerState.Run) {
            const weather = mapDialogStore.state.weather;
            const isRaining = weather === 'rain-light' || weather === 'rain-medium' || weather === 'rain-heavy';
            const effectKey = isRaining ? EFFECT_WET_SPLASH : EFFECT_FOOTSTEPS_DRY;
            drawEffect(this.scene, this.worldX, this.worldY, effectKey);
        }

        if (!this.isLocalPlayer && this.currentState === PlayerState.Run) {
            const spatialConfig = computeOtherPlayerSpatialConfig(
                this.scene.game,
                this.worldX,
                this.worldY,
                this.offsetX,
                this.offsetY,
                TILE_SIZE,
            );
            if (spatialConfig) {
                this.soundTracker.setSpatialConfig(PlayerState.Run, spatialConfig);
            }
        }

        if (this.isInTakeDamageOnMoveState() || this.pendingStunlockAfterMovement) {
            this.pendingStunlockAfterMovement = false;
            if (!this.isLocalPlayer && this.stunlockDurationMs > 0 && this.stunlockElapsedMs < 0) {
                this.startStunlock();
            }
        }

        let continuedQueuedMovement = false;
        if (!this.isLocalPlayer) {
            const head = this.movement.pendingSyncCommands[0];
            if (
                head?.type === 'movementStep' &&
                this.worldX === head.curX &&
                this.worldY === head.curY
            ) {
                this.movement.pendingSyncCommands.shift();
                this.destinationX = head.destX;
                this.destinationY = head.destY;
                this.isDirectMovementMode = false;
                this.queuedDashModeForNextMove = head.dashAttack;
                this.movement.pendingRemoteIdleSwitchMs = undefined;
                continuedQueuedMovement = true;
            }
        }

        if (!this.isLocalPlayer && !continuedQueuedMovement) {
            this.movement.pendingRemoteIdleSwitchMs = this.movement.remoteIdleContinuationGraceMs;
        }
    }

    /**
     * Returns true when in TakeDamageOnMove state.
     */
    protected override isInTakeDamageOnMoveState(): boolean {
        return this.currentState === PlayerState.TakeDamageOnMove;
    }

    /**
     * When stunlock duration is 0 (interrupt-only), do not pause between cells or start a stunlock timer at cell boundaries.
     */
    protected override shouldPauseMovementWhenCellReached(): boolean {
        if (this.stunlockDurationMs <= 0) {
            return false;
        }
        return super.shouldPauseMovementWhenCellReached();
    }

    /**
     * Clears destination when stunlock ends so player stays at cell.
     */
    protected override onStunlockComplete(): void {
        if (this.destinationX >= 0 && this.destinationY >= 0 && !this.moving) {
            this.destinationX = -1;
            this.destinationY = -1;
        }
    }

    /**
     * Updates the remote idle continuation grace period in ms.
     * Used for remote players to delay switching to idle when movement/action ends.
     */
    public setRemoteIdleContinuationGraceMs(ms: number): void {
        this.movement.remoteIdleContinuationGraceMs = Math.max(0, Math.min(500, Math.round(ms)));
    }

    /**
     * Returns per-tile step duration in ms (align with proto `movementSpeedMs`).
     */
    public getMovementSpeedMs(): number {
        return this.movementSpeedMs;
    }

    /**
     * Updates the player's movement duration. Takes the effective duration in ms (use the packet value directly).
     */
    public setMovementSpeed(movementSpeedMs: number): void {
        const clampedMs = Phaser.Math.Clamp(movementSpeedMs, 100, 1000);

        this.movementSpeedMs = clampedMs;
        if (this.isInMovementState()) {
            this.switchToMovement(true);
        }
    }

    /**
     * Applies authoritative movement / attack / cast durations from server snapshots or temporary-effect packets (e.g. Chill).
     * Only updates fields that are provided.
     */
    public applySpeedsMs(opts: {
        movementSpeedMs?: number;
        attackSpeedMs?: number;
        castSpeedMs?: number;
    }): void {
        if (typeof opts.movementSpeedMs === 'number') {
            this.setMovementSpeed(opts.movementSpeedMs);
        }
        if (typeof opts.attackSpeedMs === 'number') {
            this.setAttackSpeedFromDurationMs(opts.attackSpeedMs);
        }
        if (typeof opts.castSpeedMs === 'number') {
            this.castSpeed = Phaser.Math.Clamp(opts.castSpeedMs, 200, 2000);
        }
    }

    /**
     * Local player only: full spell cast bar duration in ms (200–2000) from Player dialog and `InitialGameWorldState.cast_speed_ms`.
     * Ignored for remote players so their `castSpeed` is not overwritten by self world state; remotes use `queueRemoteSpellCastStart` for observed casts.
     */
    public setCastDurationMs(durationMs: number): void {
        if (!this.isLocalPlayer) {
            return;
        }
        this.castSpeed = Phaser.Math.Clamp(durationMs, 200, 2000);
    }

    /**
     * Renders the health bar anchored to the camera viewport center (local player only).
     */
    private updateHealthBar(): void {
        if (!this.isLocalPlayer) {
            return;
        }

        const camera = this.scene.cameras.main;
        const zoom = camera.zoom || 1;
        const hpRatio = Phaser.Math.Clamp(this.hp / this.maxHp, 0, 1);

        EventBus.emit(NATIVE_OVERLAY_HEALTH_BAR_UPDATED, {
            centerX: Math.round(camera.x + camera.width / 2),
            centerY: Math.round(camera.y + camera.height / 2 - (2 * TILE_SIZE + 10) * zoom),
            width: PLAYER_HEALTH_BAR_WIDTH * zoom,
            height: PLAYER_HEALTH_BAR_HEIGHT * zoom,
            hpRatio,
            trackColor: '#333333',
            fillColor: '#ff0000',
            borderColor: '#660000',
        });
    }

    private hideHealthBar(): void {
        if (this.isLocalPlayer) {
            EventBus.emit(NATIVE_OVERLAY_HEALTH_BAR_HIDDEN);
        }
    }

    /**
     * Creates the casting circle effect at the player's location.
     * Effect duration matches castSpeed and does not loop.
     */
    private createCastingCircleEffect(): void {
        // Get the effect config to determine frame count
        const effectConfig = getEffectByKey(EFFECT_CASTING_CIRCLE);
        if (!effectConfig) {
            return;
        }

        // Get texture to determine frame count
        const textureKey = `sprite-${effectConfig.sprite}-${effectConfig.spriteSheetIndex}`;
        const texture = this.scene.textures.get(textureKey);
        if (!texture) {
            return;
        }

        const frameCount = Object.keys(texture.frames).length;
        if (frameCount === 0) {
            return;
        }

        // Calculate frame rate to match castSpeed duration
        const frameRate = calculateFrameRateFromDuration(frameCount, this.castSpeed);

        // Create the effect with calculated frame rate, no looping
        this.castingCircleEffect = drawEffect(
            this.scene,
            this.worldX,
            this.worldY,
            EFFECT_CASTING_CIRCLE,
            {
                soundManager: this.soundManager,
                playerWorldX: this.worldX,
                playerWorldY: this.worldY,
                infiniteLoop: false,
                frameRate: frameRate,
            }
        );
    }

    /**
     * Destroys the casting circle effect if it exists.
     */
    private destroyCastingCircleEffect(): void {
        if (this.castingCircleEffect) {
            this.castingCircleEffect.destroy();
            this.castingCircleEffect = undefined;
        }
    }

    /**
     * Creates a floating text with the spell name above the player in green color.
     * Positioned 3 cells above the player, similar to damage text.
     */
    private createSpellNameFloatingText(): void {
        if (!this.activeSpellName) {
            return;
        }

        new FloatingText(this.scene, {
            text: this.activeSpellName,
            x: this.getAnimatedPixelX(),
            y: this.getAnimatedPixelY() - 3 * TILE_SIZE + 20,
            fontSize: 16,
            color: '#00ff00',
            bold: true,
            horizontalOffset: -2,
            upwardTravelPxPerSec: 30,
            totalDurationMs: 2000,
            fadeDurationMs: 1000,
        });
    }

    private resolveSpellName(spellId: number): string | undefined {
        const manager = getNetworkManager(this.scene.game);
        return manager?.getSpellById(spellId)?.name ?? manager?.getMimicAbilityName(spellId);
    }

    private emitCastStarted(spellId: number): void {
        EventBus.emit(OUT_UI_CAST_STARTED);
        EventBus.emit(PLAYER_CAST_ANIMATION_STARTED, { spellId });
    }

    private clearSpellState(): void {
        this.pendingSpellId = undefined;
        this.queuedCastSpellId = undefined;
        this.activeSpellName = undefined;
    }

    /**
     * Destroys the player and all associated resources including the shadow sprite.
     */
    public destroy(): void {
        if (this.equipItemHandler) {
            EventBus.off(EQUIP_ITEM, this.equipItemHandler);
        }
        if (this.genderChangeHandler) {
            EventBus.off(IN_UI_CHANGE_GENDER, this.genderChangeHandler);
        }
        if (this.skinColorChangeHandler) {
            EventBus.off(IN_UI_CHANGE_SKIN_COLOR, this.skinColorChangeHandler);
        }
        if (this.underwearColorChangeHandler) {
            EventBus.off(IN_UI_CHANGE_UNDERWEAR_COLOR, this.underwearColorChangeHandler);
        }
        if (this.hairStyleChangeHandler) {
            EventBus.off(IN_UI_CHANGE_HAIR_STYLE, this.hairStyleChangeHandler);
        }
        this.soundTracker.stopAllSounds();
        this.cancelPendingBowArrowSpawn();
        this.destroyCastingCircleEffect();
        this.hideHealthBar();
        this.movement.pendingSyncCommands = [];
        super.destroy();
    }
}
