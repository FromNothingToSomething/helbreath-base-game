import type { NocSnapshot } from '../proto/generated/nocthemar';
import { getRace, raceName } from '../constants/Races';

/** UI-ready view of the server's NocSnapshot: plain numbers and clamped ratios. */
export interface NocViewModel {
    level: number;
    /** XP accumulated toward the next level. */
    xp: number;
    /** XP needed for the next level; 0 at max level. */
    xpToNext: number;
    /** 0..1 progress toward the next level; 1 at max level. */
    xpRatio: number;
    atMaxLevel: boolean;
    mana: number;
    maxMana: number;
    /** 0..1; 0 when maxMana is 0. */
    manaRatio: number;
    raceId: number;
    /** Key from Races.json; null when this client does not know the id (newer server). */
    raceKey: string | null;
    /** Display name; `Race <id>` for an id this client does not know. */
    raceName: string;
}

const clamp01 = (value: number): number => (Number.isFinite(value) ? Math.min(1, Math.max(0, value)) : 0);

/** Converts the wire snapshot (int64 as bigint) to a view model; the server is authoritative, only presentation clamping happens here. */
export function toNocViewModel(snapshot: NocSnapshot): NocViewModel {
    const xp = Number(snapshot.xp);
    const xpToNext = Number(snapshot.xpToNext);
    const atMaxLevel = xpToNext <= 0;
    return {
        level: snapshot.level,
        xp,
        xpToNext,
        xpRatio: atMaxLevel ? 1 : clamp01(xp / xpToNext),
        atMaxLevel,
        mana: snapshot.mana,
        maxMana: snapshot.maxMana,
        manaRatio: snapshot.maxMana > 0 ? clamp01(snapshot.mana / snapshot.maxMana) : 0,
        raceId: snapshot.raceId,
        raceKey: getRace(snapshot.raceId)?.key ?? null,
        raceName: raceName(snapshot.raceId),
    };
}
