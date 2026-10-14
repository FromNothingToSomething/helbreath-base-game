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

export interface CatalogModel {
    classes: ClassView[];
    skills: SkillView[];
    skillsById: Map<number, SkillView>;
}

const KINDS: SkillKind[] = ['passive', 'active', 'ultimate'];

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
    return { classes, skills, skillsById: new Map(skills.map((s) => [s.id, s])) };
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
