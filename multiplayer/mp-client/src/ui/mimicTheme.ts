import type { MimicRarity } from '../utils/NocCatalogView';

/** Violet of the mimicry UI (bar and Soul Forge), distinct from the brown of the other panels. */
export const MIMIC_PURPLE = '#7b4fbf';
export const MIMIC_PURPLE_LIGHT = '#b08ae8';
export const MIMIC_PURPLE_DARK = 'rgba(38, 18, 66, 0.92)';

export const MIMIC_RARITY_COLOR: Record<MimicRarity, string> = {
    common: '#c9c9c9',
    rare: '#5aa0ff',
    epic: '#b07cff',
    legendary: '#ffb347',
    mythic: '#ff5a5a',
};
