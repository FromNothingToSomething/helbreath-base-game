import type { NocFactionInfo, NocQuestInfo, NocReputationRules } from '../proto/generated/nocthemar';

/** ReputationTier of the server (0..4). */
export type TierId = 0 | 1 | 2 | 3 | 4;
export const TIER_NAMES = ['Hostile', 'Neutral', 'Friendly', 'Honored', 'Exalted'] as const;

export interface PerkModifierView {
    stat: number;
    /** 0 flat, 1 percent add, 2 percent mult. */
    kind: number;
    value: number;
}

export interface PerkView {
    tier: number;
    modifiers: PerkModifierView[];
}

export interface FactionView {
    id: number;
    key: string;
    name: string;
    hq: string;
    symbol: string;
    /** First color of Factions.json (display color); '' when none. */
    color: string;
    rivals: number[];
    desc: string;
    perks: PerkView[];
}

export interface ReputationRules {
    min: number;
    max: number;
    joinLevel: number;
    killBase: number;
    /** Lowest value of each tier: index = tier id; hostile starts at min. */
    floors: [number, number, number, number, number];
}

export type QuestKind = 'kill' | 'deliver';

export interface QuestView {
    id: number;
    key: string;
    name: string;
    factionId: number;
    minTier: number;
    kind: QuestKind;
    targetId: number;
    count: number;
    rewardXp: number;
    rewardRep: number;
    rewardItems: { itemId: number; quantity: number }[];
    desc: string;
}

export function toFactionModels(list: readonly NocFactionInfo[]): FactionView[] {
    return list.map((f) => ({
        id: f.id,
        key: f.key,
        name: f.name,
        hq: f.hq,
        symbol: f.symbol,
        color: f.colors[0] ?? '',
        rivals: [...f.rivals],
        desc: f.desc,
        perks: f.perks.map((p) => ({
            tier: p.tier,
            modifiers: p.modifiers.map((m) => ({ stat: m.stat, kind: m.kind, value: m.value })),
        })),
    }));
}

export function toQuestModels(list: readonly NocQuestInfo[]): QuestView[] {
    return list.map((q) => ({
        id: q.id,
        key: q.key,
        name: q.name,
        factionId: q.factionId,
        minTier: q.minTier,
        kind: q.kind === 1 ? 'deliver' : 'kill',
        targetId: q.targetId,
        count: q.count,
        rewardXp: Number(q.rewardXp),
        rewardRep: q.rewardRep,
        rewardItems: q.rewardItems.map((i) => ({ itemId: i.itemId, quantity: i.quantity })),
        desc: q.desc,
    }));
}

export function toReputationRules(rules: NocReputationRules | undefined): ReputationRules | null {
    if (!rules) {
        return null;
    }
    return {
        min: rules.min,
        max: rules.max,
        joinLevel: rules.joinLevel,
        killBase: rules.killBase,
        floors: [rules.min, rules.neutral, rules.friendly, rules.honored, rules.exalted],
    };
}

/** Tier of a reputation value; same thresholds as the server (a value equal to a floor belongs to the higher tier). */
export function tierOf(rules: ReputationRules, value: number): TierId {
    if (value >= rules.floors[4]) return 4;
    if (value >= rules.floors[3]) return 3;
    if (value >= rules.floors[2]) return 2;
    if (value >= rules.floors[1]) return 1;
    return 0;
}

export function tierName(tier: number): string {
    return TIER_NAMES[tier] ?? `Tier ${tier}`;
}

export interface ReputationBar {
    tier: TierId;
    tierName: string;
    /** 0..1 progress inside the current tier; 1 at exalted. */
    ratio: number;
    /** Value at the start of the tier, and at the start of the next one (null at exalted). */
    from: number;
    to: number | null;
}

/** Position of a value inside its tier, for the reputation bar. */
export function reputationBar(rules: ReputationRules, value: number): ReputationBar {
    const clamped = Math.min(rules.max, Math.max(rules.min, value));
    const tier = tierOf(rules, clamped);
    const from = rules.floors[tier];
    const to = tier === 4 ? null : rules.floors[tier + 1];
    const ratio = to === null || to <= from ? 1 : Math.min(1, Math.max(0, (clamped - from) / (to - from)));
    return { tier, tierName: tierName(tier), ratio, from, to };
}

export function reputationOf(entries: readonly { factionId: number; value: number }[], factionId: number): number {
    return entries.find((e) => e.factionId === factionId)?.value ?? 0;
}
