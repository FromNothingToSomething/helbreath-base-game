import type { NocCharacterCreation } from '../proto/generated/nocthemar';
import { getDefaultRace, getRace, type RaceAppearance, type RaceInfo } from '../constants/Races';

/** The four appearance axes, named like the fields of NocCharacterCreation. */
export type AppearanceAxis = 'gender' | 'skinColor' | 'hairStyleIndex' | 'underwearColorIndex';

export interface CreationDraft {
    raceId: number;
    gender: number;
    skinColor: number;
    hairStyleIndex: number;
    underwearColorIndex: number;
}

const AXIS_VALUES: Record<AppearanceAxis, keyof RaceAppearance> = {
    gender: 'genders',
    skinColor: 'skinColors',
    hairStyleIndex: 'hairStyles',
    underwearColorIndex: 'underwearColors',
};

const AXES = Object.keys(AXIS_VALUES) as AppearanceAxis[];

/** Values the race allows on one axis, in ascending order. */
export function allowedValues(race: RaceInfo, axis: AppearanceAxis): readonly number[] {
    return race.appearance[AXIS_VALUES[axis]];
}

/** Default race, default appearance (what the server gives a character created without a choice). */
export function defaultDraft(): CreationDraft {
    return constrainDraft({ raceId: getDefaultRace().id, gender: 0, skinColor: 0, hairStyleIndex: 0, underwearColorIndex: 0 });
}

/**
 * Returns a draft the server will accept: an unknown race becomes the default race, and every axis the race does not allow
 * moves to the closest allowed value (the lower one on a tie). A draft that is already valid is returned unchanged.
 */
export function constrainDraft(draft: CreationDraft): CreationDraft {
    const race = getRace(draft.raceId) ?? getDefaultRace();
    const next: CreationDraft = { ...draft, raceId: race.id };
    for (const axis of AXES) {
        const values = allowedValues(race, axis);
        if (!values.includes(next[axis])) {
            next[axis] = closest(values, next[axis]);
        }
    }
    return next;
}

function closest(values: readonly number[], wanted: number): number {
    let best = values[0];
    for (const value of values) {
        if (Math.abs(value - wanted) < Math.abs(best - wanted)) {
            best = value;
        }
    }
    return best;
}

/** Changing the race re-constrains the appearance to what the new race allows. */
export function withRace(draft: CreationDraft, raceId: number): CreationDraft {
    return constrainDraft({ ...draft, raceId });
}

/** A value the race does not allow is not applied. */
export function withAxis(draft: CreationDraft, axis: AppearanceAxis, value: number): CreationDraft {
    const race = getRace(draft.raceId) ?? getDefaultRace();
    return allowedValues(race, axis).includes(value) ? { ...draft, [axis]: value } : draft;
}

/** True when the server would accept the draft. */
export function isDraftValid(draft: CreationDraft): boolean {
    const race = getRace(draft.raceId);
    return race !== undefined && AXES.every((axis) => allowedValues(race, axis).includes(draft[axis]));
}

/** Wire form of a draft; always constrained first, so the message is never one the server would refuse for the known catalog. */
export function toCreationMessage(draft: CreationDraft): NocCharacterCreation {
    const valid = constrainDraft(draft);
    return {
        raceId: valid.raceId,
        gender: valid.gender,
        skinColor: valid.skinColor,
        hairStyleIndex: valid.hairStyleIndex,
        underwearColorIndex: valid.underwearColorIndex,
    };
}
