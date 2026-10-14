/**
 * Client mirror of server/Config/Nocthemar/Races.json (ids, keys, names, allowed appearance, art).
 * Pure data, no Phaser/React import, so that the client-logic tests can load it.
 * Gameplay modifiers and traits stay server-side (authoritative). Keep in sync: tools/noc-client-tests/races-parity.test.ts fails on any difference.
 */

export interface RaceAppearance {
    /** PlayerGender: 0 = male, 1 = female. */
    readonly genders: readonly number[];
    /** PlayerSkinColor: 0 = light, 1 = tanned, 2 = dark. */
    readonly skinColors: readonly number[];
    /** Hair style index 0-7 (client style 1-8). */
    readonly hairStyles: readonly number[];
    /** Underwear palette index 0-7. */
    readonly underwearColors: readonly number[];
}

export interface RaceArt {
    /** True when the race has its own sprites; otherwise human sprites are drawn with `tint`. */
    readonly native: boolean;
    /** `#RRGGBB`; present when `native` is false. */
    readonly tint?: string;
}

export interface RaceInfo {
    readonly id: number;
    readonly key: string;
    readonly name: string;
    readonly appearance: RaceAppearance;
    readonly art: RaceArt;
}

/** Race of a character without a choice, and of every legacy character. */
export const DEFAULT_RACE_ID = 0;

const ALL_APPEARANCE: RaceAppearance = {
    genders: [0, 1],
    skinColors: [0, 1, 2],
    hairStyles: [0, 1, 2, 3, 4, 5, 6, 7],
    underwearColors: [0, 1, 2, 3, 4, 5, 6, 7],
};

// TODO(art): every non-native race uses human sprites + tint until its own sprites exist.
export const RACES: readonly RaceInfo[] = [
    { id: 0, key: 'valdornien', name: 'Valdorniens', appearance: ALL_APPEARANCE, art: { native: true } },
    { id: 1, key: 'spectral', name: 'Spectraux', appearance: ALL_APPEARANCE, art: { native: false, tint: '#9FB4C8' } },
    { id: 2, key: 'sang_mele', name: 'Sang-mêlés', appearance: ALL_APPEARANCE, art: { native: false, tint: '#8B4A3C' } },
    { id: 3, key: 'reanime', name: 'Réanimés', appearance: ALL_APPEARANCE, art: { native: false, tint: '#7A8F6A' } },
    { id: 4, key: 'chimere', name: 'Chimères', appearance: ALL_APPEARANCE, art: { native: false, tint: '#4A4F57' } },
    { id: 5, key: 'lychen', name: 'Lychens', appearance: ALL_APPEARANCE, art: { native: false, tint: '#6B5A3E' } },
];

export function getRace(id: number): RaceInfo | undefined {
    return RACES.find((race) => race.id === id);
}

/** The default race; always listed (guarded by the parity test). */
export function getDefaultRace(): RaceInfo {
    const race = getRace(DEFAULT_RACE_ID);
    if (!race) {
        throw new Error(`Default race ${DEFAULT_RACE_ID} is missing from RACES.`);
    }
    return race;
}

/** Display name; an id this client does not know (newer server) is shown as `Race <id>` rather than guessed. */
export function raceName(id: number): string {
    return getRace(id)?.name ?? `Race ${id}`;
}
