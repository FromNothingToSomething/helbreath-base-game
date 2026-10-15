/** Display colors per faction id (Factions.json colors[0]): silver, violet, dark green, tarnished gold. */
export const FACTION_COLORS: Record<number, string> = {
    0: '#c0c4cc',
    1: '#7b4fbf',
    2: '#3f6b3a',
    3: '#b8923a',
};

/** Light variants for text on dark panels. */
export const FACTION_COLORS_LIGHT: Record<number, string> = {
    0: '#e4e7ee',
    1: '#b08ae8',
    2: '#7fb878',
    3: '#e0c070',
};

export const FACTION_FALLBACK_COLOR = '#9a8f7a';

export const TIER_COLORS = ['#e07a5f', '#c9c9c9', '#7fb878', '#5aa0ff', '#ffb347'] as const;

export function factionColor(factionId: number | null | undefined): string {
    return factionId === null || factionId === undefined ? FACTION_FALLBACK_COLOR : (FACTION_COLORS[factionId] ?? FACTION_FALLBACK_COLOR);
}

export function factionColorLight(factionId: number | null | undefined): string {
    return factionId === null || factionId === undefined ? FACTION_FALLBACK_COLOR : (FACTION_COLORS_LIGHT[factionId] ?? FACTION_FALLBACK_COLOR);
}

export function tierColor(tier: number): string {
    return TIER_COLORS[tier] ?? FACTION_FALLBACK_COLOR;
}
