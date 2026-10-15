/**
 * Type definitions for shared data structures across the application.
 */

import type { GroundEffectType, MonsterEntityState } from './proto/generated/network';
import type { Effect, EquipmentSlot, InventoryItem, ItemTypes } from './constants/Items';
import type { WeatherMode } from './ui/store/MapDialog.store';

/**
 * Player gender for selecting equipped sprite variants (e.g. weapon male/female).
 */
export enum Gender {
    MALE = 'male',
    FEMALE = 'female',
}

/**
 * Player skin color for base sprite variant (wm/ym/bm for male, ww/yw/bw for female).
 */
export enum SkinColor {
    Light = 'light',
    Tanned = 'tanned',
    Dark = 'dark',
}

export interface TeleportLoc {
    x: number;
    y: number;
}

export interface TeleportTarget {
    worldId: string;
    mapName: string;
    loc: TeleportLoc;
}

export interface TeleportLocSet {
    locs: TeleportLoc[];
    target: TeleportTarget;
}

/**
 * Event payload when hovering over a monster. Emitted every 100ms while hovered.
 * Undefined when no monster is hovered or monster becomes dead.
 * overlayScreenX/Y: screen position for overlay anchor (bottom-center of monster, in DOM pixels).
 */
export interface MonsterHoverInfo {
    name: string;
    hp: number;
    maxHp: number;
    allegiance: MonsterAllegiance;
    overlayScreenX: number;
    overlayScreenY: number;
}

/** Remote player under cursor: character name and optional spawn-protection line. */
export interface PlayerHoverInfo {
    characterName: string;
    spawnProtection: boolean;
    overlayScreenX: number;
    overlayScreenY: number;
}

/** NPC under cursor: display name and overlay anchor (same screen space as monster hover). */
export interface NpcHoverInfo {
    name: string;
    overlayScreenX: number;
    overlayScreenY: number;
}

export enum MonsterAllegiance {
    Hostile = 0,
    Neutral = 1,
    Friendly = 2,
}

export const MONSTER_ALLEGIANCE_LABELS: Record<MonsterAllegiance, string> = {
    [MonsterAllegiance.Hostile]: 'Hostile',
    [MonsterAllegiance.Neutral]: 'Neutral',
    [MonsterAllegiance.Friendly]: 'Friendly',
};

/**
 * Event payload for summoning an NPC. UI forwards catalog id to Phaser; Phaser sends it to the server.
 */
export interface SummonNPCEvent {
    catalogNpcId: number;
    /** Grid facing 0–7 (matches Direction). */
    direction: number;
}

/**
 * Event payload when the user requests a monster summon. Phaser forwards sprite id to the server; spawning is server-driven.
 */
export interface SummonMonsterEvent {
    spriteName: string;
    /** Per-tile movement duration in ms (200–2000 per Summon Monster dialog; 2000 = Immobile, server maps to 0). */
    movementSpeed: number;
    /** Grid facing 0–7 (matches Direction); server applies as initial monster facing. */
    direction: number;
    /** Monster melee hit mode on players; matches server `AttackType` / proto `attack_type`. */
    attackType: AttackType;
    /** Monster hostility mode; 0 = Hostile, 1 = Neutral, 2 = Friendly. */
    allegiance: MonsterAllegiance;
    /** Player stunlock duration from this monster’s Stun/Knockback hits (ms, 100–2000); maps to `GameWorldMonster.StunDurationMs`. */
    stunDurationMs: number;
    /** Max HP at spawn (1–1000); matches health slider; server sets current HP to this value. */
    maxHp: number;
    /** Melee damage (1–1000); server sets both min and max damage to this value. */
    attackDamage: number;
    /** Full melee swing duration in ms (200–2000); maps to `GameWorldMonster.AttackSpeedMs`. */
    attackSpeedMs: number;
    /** Post-hit idle gate in ms (0–2000); maps to `GameWorldMonster.AttackRecoveryMs`. */
    attackRecoveryMs: number;
    /** Max Chebyshev chase distance in cells (1–20); maps to `GameWorldMonster.ChaseMaxDistanceCells`. */
    chaseRangeCells: number;
    /** Melee Chebyshev reach in cells (1–20); maps to `GameWorldMonster.AttackRangeCells`. */
    attackRangeCells: number;
    /** How many monsters to spawn (1–1000); maps to proto `summon_count`. */
    summonCount: number;
}

/**
 * Event payload for casting a spell. Emitted from UI to Phaser.
 */
export interface CastSpellEvent {
    spellId: number;
}

export enum DamageType {
    RectangleAoe = 0,
    ConeAoe = 1,
    LinearAoe = 2,
    /** One target cell only; this spell entry has no aoeRadius. */
    SingleCell = 3,
    /** Long-lived immobile field that ticks damage on occupants. */
    GroundEffect = 4,
}

/** Matches proto `TemporaryEffectType` / server spell catalog. */
export enum TemporaryEffectType {
    Invisibility = 0,
    Chill = 1,
    Berserk = 2,
}

export interface SpellTimedEffectSpec {
    type: number;
    durationMs: number;
    group: number;
}

export interface SpellEntry {
    id: number;
    name: string;
    /** Omitted for buff-only spells (use `temporaryEffects`). */
    damageType?: number;
    aoeRadius?: number;
    projectileSpeed?: number;
    emissionSteps?: number;
    startRadius?: number;
    endRadius?: number;
    startShards?: number;
    endShards?: number;
    /** Linear AoE: destination linger duration (ms), from spell config. */
    durationMs?: number;
    /** Rectangle AoE with projectile delay: fixed drop/travel distance in pixels when set (matches server `projectileDistance`). */
    projectileDistancePx?: number;
    /** When true, cursor over a player/monster at cast confirm may send aim-assist ids on the spell cast request. */
    aimAssist?: boolean;
    /** Buff-only and/or on-hit timed effects from server `temporaryEffects`. */
    temporaryEffects?: SpellTimedEffectSpec[];
}

/**
 * Event payload when player confirms spell target. Emitted from Player to GameWorld.
 */
export interface PlayerConfirmSpellTargetEvent {
    spellId: number;
    originPixelX: number;
    originPixelY: number;
    targetPixelX: number;
    targetPixelY: number;
}

/**
 * Event payload when a monster's attack hits the player.
 */
export interface MonsterAttackPlayerEvent {
    monsterId: string;
    attackType: AttackType;
    /** Damage amount dealt by the monster's attack */
    attackDamage: number;
    /** When true, spawn ArrowProjectile from monster toward player instead of dealing damage immediately */
    bowAttack?: boolean;
}

/** Server monster melee hit mode on players (matches proto PlayerReceiveDamage.attack_type). */
export enum MonsterAttackType {
    NoInterrupt = 0,
    Interrupt = 1,
    Stun = 2,
    Knockback = 3,
}

export enum AttackType {
    /** Damage does not interrupt (e.g. target continues current action) */
    NoInterrupt = 0,
    /** Same take-damage animation as Stun but no stunlock (server sends stun duration 0). */
    Interrupt = 1,
    /** Damage stuns the target (take damage animation + stunlock while duration lasts). */
    Stun = 2,
    /** Knockback vs monsters; see server `AttackType` / `MonsterTakeDamage` knockback fields. */
    Knockback = 3,
}

/** Human-readable labels for AttackType. */
export const ATTACK_TYPE_LABELS: Record<AttackType, string> = {
    [AttackType.NoInterrupt]: 'No Interrupt',
    [AttackType.Interrupt]: 'Interrupt',
    [AttackType.Stun]: 'Stun',
    [AttackType.Knockback]: 'Knockback',
};

/**
 * Represents a cached minimap with its data URL, scale factor, and original size.
 */
export interface CachedMinimap {
    dataUrl: string;
    scale: number;
    originalSize: number;
}

/**
 * Represents pivot point data for a single sprite frame.
 * Pivot points define the rotation/transformation origin for sprites.
 */
export type PivotFrame = { pivotX: number; pivotY: number; width: number; height: number };

/**
 * Contains pivot data for all sprite sheets in a sprite file.
 * Each sprite sheet has an array of pivot frames.
 */
export type PivotData = {
    spriteSheetPivots: PivotFrame[][];
};

// --- NetworkManager / GameWorld event payloads (EventBus and NetworkManager API) ---

export interface PlayerAppearanceChangedEventData {
    playerId: string;
    gender: Gender;
    skinColor: SkinColor;
    hairStyleIndex: number;
    underwearColorIndex: number;
}

export interface InitialGameWorldStateEventData {
    gameWorldId: string;
    mapName: string;
    musicFile?: string;
    playerX: number;
    playerY: number;
    playerId: string;
    movementSpeedMs: number;
    runMode: boolean;
    attackMode: boolean;
    attackType?: number;
    allowDashAttack?: boolean;
    /** Server-defined teleport sources; overlay cells via `getTeleportSourceCellsFromLocSets`. */
    teleportLocs: TeleportLocSet[];
    /** Authoritative melee reach (cells) when present. */
    attackRangeCells?: number;
    /** Authoritative melee damage vs monsters when present. */
    attackDamage?: number;
    /** Authoritative melee cadence in ms when present. */
    attackSpeedMs?: number;
    /** Monster stunlock duration (ms) for Stun hits when present (100–2000). */
    attackStunDurationMs?: number;
    /** Full spell cast bar duration in ms when present (200–2000). */
    castSpeedMs?: number;
    /** Arrow travel speed in pixels per second (InitialGameWorldState). */
    arrowSpeedPxPerSec?: number;
    /** Authoritative self HP (InitialGameWorldState). */
    hp: number;
    /** Authoritative self max HP (InitialGameWorldState). */
    maxHp: number;
    /** Full pickup animation duration in ms (InitialGameWorldState). */
    playerPickupAnimationTimeMs: number;
    /** Full bow stance animation duration in ms (InitialGameWorldState). */
    playerBowAnimationDurationMs: number;
    /** Whether the local player is already dead when the world snapshot is sent. */
    dead: boolean;
    /** Authoritative grid facing 0–7 (InitialGameWorldState.player_direction). */
    playerDirection: number;
    /** Authoritative weather when present in the snapshot. */
    weather?: WeatherMode;
    gender?: Gender;
    skinColor?: SkinColor;
    hairStyleIndex?: number;
    underwearColorIndex?: number;
}

/** Remote player snapshot from server (entered range / in-view list); not the Phaser `Player` class. */
export interface NetworkPlayer {
    playerId: string;
    x: number;
    y: number;
    movementSpeedMs: number;
    /** Effective melee swing duration (ms); from `PlayerEnteredRange` / temp-effect updates. */
    attackSpeedMs?: number;
    /** Effective spell cast bar duration (ms); from `PlayerEnteredRange` / temp-effect updates. */
    castSpeedMs?: number;
    runningMode: boolean;
    attackMode: boolean;
    disconnected: boolean;
    dead: boolean;
    spawnProtection?: boolean;
    /** Server grid facing; same values as CoordinateUtils Direction (0–7). */
    direction: number;
    visibleEquippedItems: Partial<Record<ItemTypes, { itemId: number; effectOverrides?: Effect[] }>>;
    gender: Gender;
    skinColor: SkinColor;
    hairStyleIndex: number;
    underwearColorIndex: number;
    /** Display name from server `PlayerEnteredRange.character_name`. */
    characterName: string;
    /** Faction of the player (Factions.json id); undefined when none. */
    factionId?: number;
    /** Active temporary effect types (proto `TemporaryEffectType` values). */
    activeTemporaryEffects: number[];
}

export interface InventorySnapshotEventData {
    bagItems: InventoryItem[];
    equippedItems: Partial<Record<EquipmentSlot, InventoryItem>>;
}

export interface ItemEquippedEventData {
    playerId: string;
    slot: EquipmentSlot;
    item: InventoryItem;
}

export interface ItemUnequippedEventData {
    playerId: string;
    slot: EquipmentSlot;
    itemUid: string;
}

export interface PlayerLeftEventData {
    playerId: string;
}

export interface NpcEnteredRangeEventData {
    npcId: string;
    catalogNpcId: number;
    x: number;
    y: number;
    /** Authoritative grid facing 0–7 (matches Direction). */
    direction: number;
    /** Display name from InitialState npc_directory. */
    displayName: string;
}

export interface MonsterEnteredRangeEventData {
    monsterId: string;
    sprite: string;
    x: number;
    y: number;
    state: MonsterEntityState;
    name: string;
    rangedAttack: boolean;
    hp: number;
    maxHp: number;
    dead: boolean;
    corpseDecayTimeLeftMs: number;
    /** Authoritative grid facing 0–7 (matches Direction). */
    direction: number;
    /** Per-tile step duration in ms (0 = immobile). */
    movementSpeedMs: number;
    /** Full melee swing duration in ms. */
    attackSpeedMs: number;
    /** Typical melee damage (server snapshot min). */
    attackDamage: number;
    /** Server `MonsterAllegiance` enum value. */
    allegiance: number;
    /** Server `AttackType` enum value. */
    attackType: number;
    /** Active temporary effect types (proto `TemporaryEffectType` values). */
    activeTemporaryEffects: number[];
}

export interface PlayerMovedEventData {
    playerId: string;
    curX: number;
    curY: number;
    destX: number;
    destY: number;
    movementSpeedMs: number;
    runningMode: boolean;
    attackMode: boolean;
    dashAttack: boolean;
    /** Server snap (e.g. admin teleport); observers snap instead of walk animation. */
    teleport: boolean;
}

export interface MonsterMovedEventData {
    monsterId: string;
    /** Server grid cell before this step (matches `PlayerMoved` cur). */
    curX: number;
    curY: number;
    destX: number;
    destY: number;
    movementSpeedMs: number;
    /** Authoritative facing for this step (0–7). */
    direction: number;
}

export interface MonsterAttackedEventData {
    monsterId: string;
    /** Server grid facing 0–7 (matches `Direction`). */
    direction: number;
    attackSpeedMs: number;
    rangedAttack: boolean;
    /** Chase target for this swing; arrow VFX homes toward this player when `rangedAttack` is true. */
    targetPlayerId: string;
    /** Authoritative monster grid cell at swing time. */
    worldX: number;
    worldY: number;
}

export interface MonsterAttackedMonsterEventData {
    monsterId: string;
    direction: number;
    attackSpeedMs: number;
    rangedAttack: boolean;
    targetMonsterId: string;
    worldX: number;
    worldY: number;
}

export interface PlayerReceiveDamageEventData {
    playerId: string;
    damage: number;
    monsterId: string;
    /** Matches proto `PlayerReceiveDamage.attack_type` (see server `AttackType`). */
    attackType: number;
    stunDurationMs: number;
    /** Present when server applies knockback (ms); movement interpolation duration. */
    knockbackDurationMs?: number;
    destX?: number;
    destY?: number;
    /** Server cell before knockback; use for slide/facing when set (local prediction may differ). */
    knockbackFromX?: number;
    knockbackFromY?: number;
}

export interface MonsterTakeDamageEventData {
    monsterId: string;
    damage: number;
    /** Matches proto `MonsterTakeDamage.attack_type` (server `AttackType`). */
    attackType: number;
    /** Matches proto `MonsterTakeDamage.stunlock_duration_ms`; >0 when Stun was applied server-side. */
    stunlockDurationMs: number;
    /** Authoritative remaining HP after this hit. */
    hp: number;
    /** Present when server applied knockback (ms). */
    knockbackDurationMs?: number;
    destX?: number;
    destY?: number;
    knockbackFromX?: number;
    knockbackFromY?: number;
}

export interface MonsterTakeDamageByMonsterEventData {
    targetMonsterId: string;
    damage: number;
    attackerMonsterId: string;
    attackType: number;
    stunlockDurationMs: number;
    hp: number;
    knockbackDurationMs?: number;
    destX?: number;
    destY?: number;
    knockbackFromX?: number;
    knockbackFromY?: number;
}

export interface MonsterDiedEventData {
    monsterId: string;
}

export interface PlayerDiedEventData {
    playerId: string;
    x: number;
    y: number;
}

export interface PlayerResurrectedEventData {
    playerId: string;
    x: number;
    y: number;
    hp: number;
    maxHp: number;
}

/** Server sync for another player's attack (melee or bow). */
export interface PlayerAttackedMonsterEventData {
    playerId: string;
    direction: number;
    attackSpeedMs: number;
    rangedAttack: boolean;
    monsterId: string;
    /** Authoritative attacker grid cell at swing time. */
    worldX: number;
    worldY: number;
    /** Matches proto `PlayerAttackedMonster.attack_type` (server `AttackType`). */
    attackType: number;
}

/** Server sync for another player's attack against a player target (melee or bow). */
export interface PlayerAttackedPlayerEventData {
    playerId: string;
    direction: number;
    attackSpeedMs: number;
    rangedAttack: boolean;
    targetPlayerId: string;
    worldX: number;
    worldY: number;
    attackType: number;
}

export interface PlayerTakeDamageEventData {
    targetPlayerId: string;
    damage: number;
    attackerPlayerId: string;
    attackType: number;
    stunDurationMs: number;
    knockbackDurationMs?: number;
    destX?: number;
    destY?: number;
    knockbackFromX?: number;
    knockbackFromY?: number;
}

export interface PlayerMovementStateChangedEventData {
    playerId: string;
    runningMode: boolean;
    movementSpeedMs: number;
}

export interface PlayerAttackModeChangedEventData {
    playerId: string;
    attackMode: boolean;
}

export interface PlayerIdleDirectionChangedEventData {
    playerId: string;
    direction: number;
}

export interface PlayerPickupPerformedEventData {
    playerId: string;
    direction: number;
    animationTimeMs: number;
}

export interface PlayerBowStancePerformedEventData {
    playerId: string;
    direction: number;
    animationTimeMs: number;
}

export interface SpellCastStartedEventData {
    playerId: string;
    spellName: string;
    /** Caster cast bar duration (ms); used to sync remote cast animation. */
    castSpeedMs: number;
}

export interface SpellCastCancelledEventData {
    playerId: string;
}

export interface CastAoeSpellEventData {
    playerId: string;
    spellId: number;
    x: number;
    y: number;
}

export interface CastDirectionalAoeSpellEventData {
    playerId: string;
    spellId: number;
    casterX: number;
    casterY: number;
    targetX: number;
    targetY: number;
}

export interface MonsterCastAoeSpellEventData {
    monsterId: string;
    spellId: number;
    x: number;
    y: number;
}

export interface MonsterCastDirectionalAoeSpellEventData {
    monsterId: string;
    spellId: number;
    casterX: number;
    casterY: number;
    targetX: number;
    targetY: number;
}

export interface GroundEffectEventData {
    groundEffectId: string;
    effectType: GroundEffectType;
}

export interface GroundItemEventData {
    itemId: number;
    itemUid: string;
    quantity: number;
    effectOverrides?: Effect[];
}

export interface GroundStateCellEventData {
    x: number;
    y: number;
    effects: GroundEffectEventData[];
    groundItem?: GroundItemEventData;
}

export interface GroundStateCellRemovedEventData {
    x: number;
    y: number;
    groundEffectIds: string[];
    groundItemUid?: string;
}

export interface PlayerConnectionStateChangedEventData {
    playerId: string;
}

/** Phaser `GameWorld` scene `init()` payload. */
export interface GameWorldInitData {
    initialGameWorldState?: import('./utils/RegistryUtils').InitialGameWorldState;
}

export interface TemporaryEffectPlayerEventData {
    playerId: string;
    temporaryEffectType: number;
    movementSpeedMs?: number;
    attackSpeedMs?: number;
    castSpeedMs?: number;
}

export interface TemporaryEffectMonsterEventData {
    monsterId: string;
    temporaryEffectType: number;
    movementSpeedMs?: number;
    attackSpeedMs?: number;
}
