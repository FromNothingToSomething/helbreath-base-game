// T33: the client mirror (Races.ts) must match the server's Races.json: ids, keys, names, allowed appearance, art.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { DEFAULT_RACE_ID, RACES, getDefaultRace, getRace, raceName } from '../../multiplayer/mp-client/src/constants/Races';

interface RaceJson {
    id: number;
    key: string;
    name: string;
    appearance: { genders: number[]; skinColors: number[]; hairStyles: number[]; underwearColors: number[] };
    art: { native: boolean; tint?: string };
}

const file = JSON.parse(readFileSync(join(process.cwd(), '..', 'multiplayer', 'server', 'Config', 'Nocthemar', 'Races.json'), 'utf8')) as {
    defaultRaceId: number;
    races: RaceJson[];
};

const sorted = (values: readonly number[]) => [...values].sort((a, b) => a - b);

test('same set of race ids, in the same order', () => {
    assert.deepEqual(RACES.map((r) => r.id), file.races.map((r) => r.id));
});

test('same keys and names', () => {
    assert.deepEqual(RACES.map((r) => [r.id, r.key, r.name]), file.races.map((r) => [r.id, r.key, r.name]));
});

test('same allowed appearance for every race and axis', () => {
    for (const json of file.races) {
        const race = getRace(json.id);
        assert.ok(race, `race ${json.id} missing from Races.ts`);
        assert.deepEqual(sorted(race.appearance.genders), sorted(json.appearance.genders), `genders of ${json.key}`);
        assert.deepEqual(sorted(race.appearance.skinColors), sorted(json.appearance.skinColors), `skinColors of ${json.key}`);
        assert.deepEqual(sorted(race.appearance.hairStyles), sorted(json.appearance.hairStyles), `hairStyles of ${json.key}`);
        assert.deepEqual(sorted(race.appearance.underwearColors), sorted(json.appearance.underwearColors), `underwearColors of ${json.key}`);
    }
});

test('same art: native flag and tint', () => {
    for (const json of file.races) {
        const race = getRace(json.id);
        assert.ok(race);
        assert.equal(race.art.native, json.art.native, `native of ${json.key}`);
        assert.equal(race.art.tint?.toLowerCase(), json.art.tint?.toLowerCase(), `tint of ${json.key}`);
    }
});

test('same default race, and it is listed', () => {
    assert.equal(DEFAULT_RACE_ID, file.defaultRaceId);
    assert.equal(getDefaultRace().id, file.defaultRaceId);
});

test('ids and keys are unique', () => {
    assert.equal(new Set(RACES.map((r) => r.id)).size, RACES.length);
    assert.equal(new Set(RACES.map((r) => r.key)).size, RACES.length);
});

test('a race this client does not know is shown by id, not guessed', () => {
    assert.equal(getRace(99), undefined);
    assert.equal(raceName(99), 'Race 99');
    assert.equal(raceName(0), 'Valdorniens');
});
