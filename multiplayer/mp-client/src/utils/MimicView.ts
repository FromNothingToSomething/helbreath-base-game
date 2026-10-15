import type { CatalogModel, FusionView, MimicKind, MimicRarity, MimicRules, MimicView } from './NocCatalogView';
import type { MimicStateView, NocViewModel } from './NocView';

/** Pure client-side preview of the mimicry rules (Soul Forge, bar, levels). The server stays authoritative and re-checks everything; the wording of the refusals is the server's. */

export const MIMIC_KIND_LABEL: Record<MimicKind, string> = {
    offensive: 'Offensive',
    defensive: 'Defensive',
    utility: 'Utility',
    control: 'Control',
    ultimate: 'Ultimate',
};

export const MIMIC_RARITY_LABEL: Record<MimicRarity, string> = {
    common: 'Common',
    rare: 'Rare',
    epic: 'Epic',
    legendary: 'Legendary',
    mythic: 'Mythic',
};

/** A bag row; stackable essences may be spread over several rows. */
export interface BagStack {
    itemId: number;
    quantity?: number;
}

export interface Preview {
    ok: boolean;
    /** Same wording as the server's refusal; undefined when ok. */
    reason?: string;
}

export interface FusePreview extends Preview {
    /** The ability the recipe gives, when the recipe exists. */
    result?: MimicView;
}

const OK: Preview = { ok: true };
const refuse = (reason: string): Preview => ({ ok: false, reason });

/** True when a character of `classId` (null = no class yet) may learn and equip the ability. */
export function classAllows(ability: MimicView, classId: number | null): boolean {
    return ability.classIds.length === 0 || (classId !== null && ability.classIds.includes(classId));
}

export function isLearned(view: NocViewModel, abilityId: number): boolean {
    return view.mimics.some((m) => m.id === abilityId);
}

/** Essences in the bag by ability id (only abilities with at least one). */
export function essenceCounts(model: CatalogModel, bag: readonly BagStack[]): Map<number, number> {
    const byItem = new Map<number, number>();
    for (const row of bag) {
        byItem.set(row.itemId, (byItem.get(row.itemId) ?? 0) + (row.quantity ?? 1));
    }
    const counts = new Map<number, number>();
    for (const ability of model.mimics) {
        const count = ability.essenceItemId === null ? 0 : (byItem.get(ability.essenceItemId) ?? 0);
        if (count > 0) {
            counts.set(ability.id, count);
        }
    }
    return counts;
}

function learningRefusal(model: CatalogModel, view: NocViewModel, abilityId: number, byFusion: boolean): string | undefined {
    const ability = model.mimicsById.get(abilityId);
    if (!ability) {
        return 'Unknown ability.';
    }
    if (ability.isFusion !== byFusion) {
        return byFusion ? 'Unknown ability.' : 'This ability can only be obtained by fusion.';
    }
    if (!classAllows(ability, view.classId)) {
        return 'This ability is reserved to another class.';
    }
    return isLearned(view, abilityId) ? 'Ability already learned.' : undefined;
}

/** Soul Forge, consume: the rules of the server (the essence itself and the forge range are checked there). */
export function consumePreview(model: CatalogModel, view: NocViewModel, abilityId: number): Preview {
    const reason = learningRefusal(model, view, abilityId, false);
    return reason === undefined ? OK : refuse(reason);
}

/** The recipe of two abilities, in either order. */
export function fusionOf(model: CatalogModel, a: number, b: number): FusionView | undefined {
    const [low, high] = a < b ? [a, b] : [b, a];
    return model.fusions.find((f) => f.a === low && f.b === high);
}

/** Soul Forge, fuse: the rules of the server, with the ability the recipe gives. */
export function fusePreview(model: CatalogModel, view: NocViewModel, a: number, b: number): FusePreview {
    if (a === b) {
        return refuse('Choose two different essences.');
    }
    const first = model.mimicsById.get(a);
    const second = model.mimicsById.get(b);
    if (!first || !second) {
        return refuse('Unknown essence.');
    }
    if (first.isFusion || second.isFusion) {
        return refuse('A fused ability cannot be fused again.');
    }
    const recipe = fusionOf(model, a, b);
    if (!recipe) {
        return refuse('These essences do not fuse.');
    }
    const result = model.mimicsById.get(recipe.result);
    const reason = learningRefusal(model, view, recipe.result, true);
    return reason === undefined ? { ok: true, result } : { ok: false, reason, result };
}

/** Equip on the bar (changing the bar needs no Soul Forge). */
export function equipPreview(model: CatalogModel, view: NocViewModel, abilityId: number): Preview {
    const ability = model.mimicsById.get(abilityId);
    if (!ability) {
        return refuse('Unknown ability.');
    }
    if (!isLearned(view, abilityId)) {
        return refuse('Ability not learned.');
    }
    if (!classAllows(ability, view.classId)) {
        return refuse('This ability is reserved to another class.');
    }
    if (view.mimicEquipped.includes(abilityId)) {
        return refuse('Ability already equipped.');
    }
    if (view.mimicEquipped.length >= view.mimicSlots) {
        return refuse('No free mimicry slot.');
    }
    return OK;
}

export interface LevelProgress {
    level: number;
    maxLevel: number;
    atMax: boolean;
    /** XP gained inside the current level. */
    into: number;
    /** XP the current level takes; 0 at the maximum level. */
    needed: number;
    /** 0..1; 1 at the maximum level. */
    ratio: number;
}

/** Level reached with `xp` total experience: 1 + the number of thresholds reached. */
export function mimicLevelOf(rules: MimicRules, xp: number): number {
    let level = 1;
    for (const threshold of rules.thresholds) {
        if (xp < threshold) {
            break;
        }
        level++;
    }
    return level;
}

export function levelProgress(rules: MimicRules, xp: number): LevelProgress {
    const level = mimicLevelOf(rules, xp);
    if (level >= rules.maxLevel) {
        return { level, maxLevel: rules.maxLevel, atMax: true, into: 0, needed: 0, ratio: 1 };
    }
    const floor = level === 1 ? 0 : rules.thresholds[level - 2];
    const ceiling = rules.thresholds[level - 1];
    const needed = ceiling - floor;
    const into = xp - floor;
    return { level, maxLevel: rules.maxLevel, atMax: false, into, needed, ratio: needed > 0 ? Math.min(1, Math.max(0, into / needed)) : 1 };
}

/** `1 + powerPerLevel x (level - 1)`, the level clamped to 1..maxLevel. */
export function powerMultiplier(rules: MimicRules, level: number): number {
    return 1 + rules.powerPerLevel * (Math.min(Math.max(level, 1), rules.maxLevel) - 1);
}

/** Character level at which the bar slot `index` (0-based) opens; null beyond the level-based slots (only passives give those). */
export function slotUnlockLevel(rules: MimicRules, index: number): number | null {
    if (index < 0 || index >= rules.slotsMax) {
        return null;
    }
    return index < rules.slotsFirst ? 1 : 1 + (index + 1 - rules.slotsFirst) * rules.levelsPerSlot;
}

export type SlotState = 'filled' | 'free' | 'locked';

export interface SlotView {
    index: number;
    state: SlotState;
    abilityId: number | null;
    /** Character level that opens a locked slot; null when no level does. */
    unlockLevel: number | null;
}

/** The bar: equipped abilities, free slots, then the locked ones with the level that opens them. */
export function buildSlots(rules: MimicRules, view: NocViewModel): SlotView[] {
    const total = Math.max(rules.slotsMax, view.mimicSlots);
    const slots: SlotView[] = [];
    for (let index = 0; index < total; index++) {
        const abilityId = view.mimicEquipped[index] ?? null;
        if (abilityId !== null) {
            slots.push({ index, state: 'filled', abilityId, unlockLevel: null });
        } else if (index < view.mimicSlots) {
            slots.push({ index, state: 'free', abilityId: null, unlockLevel: null });
        } else {
            slots.push({ index, state: 'locked', abilityId: null, unlockLevel: slotUnlockLevel(rules, index) });
        }
    }
    return slots;
}

export interface OwnedAbility {
    ability: MimicView;
    state: MimicStateView;
    equipped: boolean;
}

/** Learned abilities the catalog knows, ascending by id (an id this client does not know is skipped). */
export function ownedAbilities(model: CatalogModel, view: NocViewModel): OwnedAbility[] {
    const owned: OwnedAbility[] = [];
    for (const state of view.mimics) {
        const ability = model.mimicsById.get(state.id);
        if (ability) {
            owned.push({ ability, state, equipped: view.mimicEquipped.includes(state.id) });
        }
    }
    return owned.sort((x, y) => x.ability.id - y.ability.id);
}

export function formatCooldown(ms: number): string {
    return ms % 1000 === 0 ? `${ms / 1000} s` : `${(ms / 1000).toFixed(1)} s`;
}
