import type { NocCatalog } from '../proto/generated/nocthemar';

export type SkillKind = 'passive' | 'active' | 'ultimate';

export interface BranchView {
    id: number;
    key: string;
    name: string;
    role: string;
}

export interface ClassView {
    id: number;
    key: string;
    name: string;
    role: string;
    branches: BranchView[];
    /** Hint only: the server accepts any race x class pair. */
    recommendedRaceIds: number[];
}

export interface SkillView {
    id: number;
    key: string;
    name: string;
    classId: number;
    branch: number;
    /** 0-7 inside the branch; 7 is the ultimate. */
    slot: number;
    level: number;
    kind: SkillKind;
    cost: number;
    desc: string;
    /** False: listed in the tree but not learnable yet. */
    ready: boolean;
    /** Skill to learn first; null = none. */
    prerequisiteId: number | null;
}

export type MimicKind = 'offensive' | 'defensive' | 'utility' | 'control' | 'ultimate';
export type MimicRarity = 'common' | 'rare' | 'epic' | 'legendary' | 'mythic';

/** One mimicry ability: a monster ability (id 2000-2099, learned from an essence) or a fusion result (id 2100-2199). */
export interface MimicView {
    id: number;
    key: string;
    name: string;
    kind: MimicKind;
    rarity: MimicRarity;
    /** Source monster; null for a fusion result. */
    monsterName: string | null;
    /** Classes allowed to learn and equip it; empty = every class. */
    classIds: number[];
    requiresRaid: boolean;
    cooldownMs: number;
    manaCost: number;
    desc: string;
    /** False: listed but not castable yet. */
    ready: boolean;
    /** Items.json id of the essence; null for a fusion result. */
    essenceItemId: number | null;
    isFusion: boolean;
}

/** Two monster abilities that fuse into a third (`a` is the lower id). */
export interface FusionView {
    a: number;
    b: number;
    result: number;
}

export interface MimicRules {
    /** Total ability XP needed for level i + 2. */
    thresholds: number[];
    maxLevel: number;
    /** Power of an ability is 1 + powerPerLevel x (level - 1). */
    powerPerLevel: number;
    slotsFirst: number;
    levelsPerSlot: number;
    slotsMax: number;
}

export interface CatalogModel {
    classes: ClassView[];
    skills: SkillView[];
    skillsById: Map<number, SkillView>;
    /** Empty on a server without mimicry. */
    mimics: MimicView[];
    mimicsById: Map<number, MimicView>;
    fusions: FusionView[];
    /** Null on a server without mimicry. */
    mimicRules: MimicRules | null;
}

const KINDS: SkillKind[] = ['passive', 'active', 'ultimate'];
const MIMIC_KINDS: MimicKind[] = ['offensive', 'defensive', 'utility', 'control', 'ultimate'];
const MIMIC_RARITIES: MimicRarity[] = ['common', 'rare', 'epic', 'legendary', 'mythic'];
/** First id of the fusion results (monster abilities are below it). */
const MIMIC_FUSION_BASE = 2100;

/** Skill rule shared with the server: at most this many branches of a class may hold learned skills. */
export const MAX_BRANCHES_WITH_SKILLS = 2;

/** Converts the wire catalog (sent once per session) to plain view objects; unknown skill kinds degrade to passive. */
export function toCatalogModel(catalog: NocCatalog): CatalogModel {
    const classes: ClassView[] = catalog.classes.map((c) => ({
        id: c.id,
        key: c.key,
        name: c.name,
        role: c.role,
        branches: c.branches.map((b) => ({ id: b.id, key: b.key, name: b.name, role: b.role })),
        recommendedRaceIds: [...c.recommendedRaceIds],
    }));
    const skills: SkillView[] = catalog.skills.map((s) => ({
        id: s.id,
        key: s.key,
        name: s.name,
        classId: s.classId,
        branch: s.branch,
        slot: s.slot,
        level: s.level,
        kind: KINDS[s.kind] ?? 'passive',
        cost: s.cost,
        desc: s.desc,
        ready: s.ready,
        prerequisiteId: s.prerequisiteId > 0 ? s.prerequisiteId : null,
    }));
    const mimics: MimicView[] = (catalog.mimics ?? []).map((m) => ({
        id: m.id,
        key: m.key,
        name: m.name,
        kind: MIMIC_KINDS[m.kind] ?? 'utility',
        rarity: MIMIC_RARITIES[m.rarity] ?? 'common',
        monsterName: m.monsterName === '' ? null : m.monsterName,
        classIds: [...m.classIds],
        requiresRaid: m.requiresRaid,
        cooldownMs: m.cooldownMs,
        manaCost: m.manaCost,
        desc: m.desc,
        ready: m.ready,
        essenceItemId: m.essenceItemId > 0 ? m.essenceItemId : null,
        isFusion: m.id >= MIMIC_FUSION_BASE,
    }));
    const thresholds = (catalog.mimicXpThresholds ?? []).map((t) => Number(t));
    const mimicRules: MimicRules | null =
        mimics.length === 0
            ? null
            : {
                  thresholds,
                  maxLevel: thresholds.length + 1,
                  powerPerLevel: catalog.mimicPowerPerLevel ?? 0,
                  slotsFirst: catalog.mimicSlotsFirst ?? 0,
                  levelsPerSlot: catalog.mimicLevelsPerSlot ?? 0,
                  slotsMax: catalog.mimicSlotsMax ?? 0,
              };
    return {
        classes,
        skills,
        skillsById: new Map(skills.map((s) => [s.id, s])),
        mimics,
        mimicsById: new Map(mimics.map((m) => [m.id, m])),
        fusions: (catalog.fusions ?? []).map((f) => ({ a: f.a, b: f.b, result: f.result })),
        mimicRules,
    };
}

export function classById(model: CatalogModel, id: number): ClassView | undefined {
    return model.classes.find((c) => c.id === id);
}

/** Skills of one branch in slot order. */
export function branchSkills(model: CatalogModel, classId: number, branch: number): SkillView[] {
    return model.skills.filter((s) => s.classId === classId && s.branch === branch).sort((a, b) => a.slot - b.slot);
}

export function isRecommendedRace(cls: ClassView, raceId: number): boolean {
    return cls.recommendedRaceIds.includes(raceId);
}

/** Classes recommended for a race first (catalog order inside each group); the others stay selectable. */
export function sortClassesForRace(classes: ClassView[], raceId: number): ClassView[] {
    const recommended = classes.filter((c) => isRecommendedRace(c, raceId));
    return [...recommended, ...classes.filter((c) => !isRecommendedRace(c, raceId))];
}

export type SkillStatus = 'learned' | 'learnable' | 'locked' | 'soon';

export interface SkillState {
    status: SkillStatus;
    /** Why a locked skill cannot be learned yet; undefined otherwise. Same wording as the server's refusals. */
    reason?: string;
}

export interface LearnContext {
    level: number;
    skills: readonly number[];
    skillPoints: number;
}

/**
 * Client-side preview of the server's learn rules, to enable or grey out the button. The server stays authoritative and re-checks everything.
 */
export function skillState(model: CatalogModel, skill: SkillView, ctx: LearnContext): SkillState {
    if (ctx.skills.includes(skill.id)) {
        return { status: 'learned' };
    }
    if (!skill.ready) {
        return { status: 'soon', reason: 'This skill is not available yet.' };
    }
    if (ctx.level < skill.level) {
        return { status: 'locked', reason: `Requires level ${skill.level}.` };
    }
    if (skill.prerequisiteId !== null && !ctx.skills.includes(skill.prerequisiteId)) {
        const prerequisite = model.skillsById.get(skill.prerequisiteId);
        return { status: 'locked', reason: `Learn ${prerequisite?.name ?? 'the previous skill'} first.` };
    }
    const branches = new Set<number>();
    for (const id of ctx.skills) {
        const learned = model.skillsById.get(id);
        if (learned && learned.classId === skill.classId) {
            branches.add(learned.branch);
        }
    }
    if (!branches.has(skill.branch) && branches.size >= MAX_BRANCHES_WITH_SKILLS) {
        return { status: 'locked', reason: 'Only two branches can be combined.' };
    }
    if (ctx.skillPoints < skill.cost) {
        return { status: 'locked', reason: 'Not enough skill points.' };
    }
    return { status: 'learnable' };
}
