/**
 * NPC sprite basenames for asset loading; index matches server NPC catalog id (0…n-1). Names for UI come from `npc_directory`.
 */
export const NPC_SPRITE_NAMES: readonly string[] = [
    'shopkpr',
    'gandlf',
    'howard',
    'tom',
    'william',
    'kennedy',
    'gail',
    'mcgaffin',
    'perry',
    'devlin',
    'gandlf', // 10: Forge d'Âmes (provisional sprite, shared with Gandalf)
];

/**
 * Returns the sprite asset name for a catalog id, or undefined if unknown.
 */
export function getSpriteForCatalogNpcId(catalogNpcId: number): string | undefined {
    if (!Number.isInteger(catalogNpcId) || catalogNpcId < 0 || catalogNpcId >= NPC_SPRITE_NAMES.length) {
        return undefined;
    }
    return NPC_SPRITE_NAMES[catalogNpcId];
}
