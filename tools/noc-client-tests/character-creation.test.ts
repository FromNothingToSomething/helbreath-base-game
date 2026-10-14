// T34: creation draft constraints (the UI only offers what the race allows) and the AuthenticateRequest wire format built by the client.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { RACES, type RaceInfo } from '../../multiplayer/mp-client/src/constants/Races';
import {
    allowedValues,
    constrainDraft,
    defaultDraft,
    isDraftValid,
    toCreationMessage,
    withAxis,
    withRace,
    type AppearanceAxis,
} from '../../multiplayer/mp-client/src/utils/CharacterCreation';
import { encodeAuthenticateRequest } from '../../multiplayer/mp-client/src/utils/Authentication';
import { ClientMessage } from '../../multiplayer/mp-client/src/proto/generated/network';

const AXES: AppearanceAxis[] = ['gender', 'skinColor', 'hairStyleIndex', 'underwearColorIndex'];

// A narrow race, as the server may one day ship: Races.ts is the only catalog the UI knows, so the constraint logic is also
// exercised through a hand-built race by narrowing the appearance of a copy and checking the helper against it.
const narrow: RaceInfo = {
    id: 1,
    key: 'narrow',
    name: 'Narrow',
    appearance: { genders: [1], skinColors: [2], hairStyles: [0, 1], underwearColors: [3] },
    art: { native: true },
};

test('default draft is the default race with the default appearance, and is valid', () => {
    const draft = defaultDraft();
    assert.deepEqual(draft, { raceId: 0, gender: 0, skinColor: 0, hairStyleIndex: 0, underwearColorIndex: 0 });
    assert.equal(isDraftValid(draft), true);
});

test('every shipped race accepts every value it allows, and the draft stays valid', () => {
    for (const race of RACES) {
        for (const axis of AXES) {
            for (const value of allowedValues(race, axis)) {
                const draft = withAxis(withRace(defaultDraft(), race.id), axis, value);
                assert.equal(draft[axis], value);
                assert.equal(isDraftValid(draft), true);
            }
        }
    }
});

test('a value the race does not allow is not applied', () => {
    const draft = defaultDraft();
    assert.deepEqual(withAxis(draft, 'hairStyleIndex', 8), draft);
    assert.deepEqual(withAxis(draft, 'gender', 2), draft);
    assert.deepEqual(withAxis(draft, 'skinColor', -1), draft);
    assert.deepEqual(withAxis(draft, 'underwearColorIndex', 99), draft);
});

test('changing the race keeps the appearance when allowed and keeps the draft valid', () => {
    const base = { ...defaultDraft(), gender: 1, skinColor: 2, hairStyleIndex: 5, underwearColorIndex: 7 };
    for (const race of RACES) {
        const draft = withRace(base, race.id);
        assert.equal(draft.raceId, race.id);
        assert.equal(isDraftValid(draft), true);
        assert.equal(draft.hairStyleIndex, 5); // shipped races allow the whole domain
    }
});

test('an unknown race becomes the default race', () => {
    assert.equal(constrainDraft({ ...defaultDraft(), raceId: 99 }).raceId, 0);
    assert.equal(constrainDraft({ ...defaultDraft(), raceId: -1 }).raceId, 0);
});

test('out of domain values move to the closest allowed value', () => {
    const draft = constrainDraft({ raceId: 0, gender: 5, skinColor: -3, hairStyleIndex: 12, underwearColorIndex: 8 });
    assert.deepEqual(draft, { raceId: 0, gender: 1, skinColor: 0, hairStyleIndex: 7, underwearColorIndex: 7 });
    assert.equal(isDraftValid(draft), true);
});

test('an invalid draft is reported invalid, a constrained one never is', () => {
    assert.equal(isDraftValid({ raceId: 99, gender: 0, skinColor: 0, hairStyleIndex: 0, underwearColorIndex: 0 }), false);
    assert.equal(isDraftValid({ raceId: 0, gender: 0, skinColor: 0, hairStyleIndex: 9, underwearColorIndex: 0 }), false);
    for (let race = -2; race < 8; race++) {
        assert.equal(isDraftValid(constrainDraft({ raceId: race, gender: race * 3, skinColor: race, hairStyleIndex: race * 2, underwearColorIndex: -race })), true);
    }
});

test('allowedValues of a narrow race lists only its values', () => {
    assert.deepEqual(allowedValues(narrow, 'gender'), [1]);
    assert.deepEqual(allowedValues(narrow, 'hairStyleIndex'), [0, 1]);
});

test('toCreationMessage carries the five fields and is always constrained', () => {
    assert.deepEqual(toCreationMessage({ raceId: 3, gender: 1, skinColor: 2, hairStyleIndex: 5, underwearColorIndex: 7 }), {
        raceId: 3,
        gender: 1,
        skinColor: 2,
        hairStyleIndex: 5,
        underwearColorIndex: 7,
    });
    assert.equal(toCreationMessage({ raceId: 42, gender: 0, skinColor: 0, hairStyleIndex: 0, underwearColorIndex: 0 }).raceId, 0);
});

test('AuthenticateRequest built by the client carries the creation and decodes back', () => {
    const creation = toCreationMessage({ raceId: 5, gender: 1, skinColor: 2, hairStyleIndex: 5, underwearColorIndex: 7 });
    const bytes = encodeAuthenticateRequest('0f8fad5b-d9cb-469f-a165-70867728950e', 'Zoë-Ægir', creation);

    const decoded = ClientMessage.decode(bytes);

    assert.equal(decoded.payload?.$case, 'authenticateRequest');
    if (decoded.payload?.$case !== 'authenticateRequest') {
        return;
    }
    assert.equal(decoded.payload.value.id, '0f8fad5b-d9cb-469f-a165-70867728950e');
    assert.equal(decoded.payload.value.characterName, 'Zoë-Ægir');
    assert.deepEqual(decoded.payload.value.creation, creation);
});

test('without a creation the field is absent on the wire (default race and appearance on the server)', () => {
    const bytes = encodeAuthenticateRequest('abc', 'Old');
    const decoded = ClientMessage.decode(bytes);

    assert.equal(decoded.payload?.$case, 'authenticateRequest');
    if (decoded.payload?.$case !== 'authenticateRequest') {
        return;
    }
    assert.equal(decoded.payload.value.creation, undefined);
    assert.equal(decoded.payload.value.characterName, 'Old');
    // Same bytes as the pre-race client: id and character_name only.
    assert.equal(bytes.length, 2 + (2 + 3 + 2 + 3)); // oneof wrapper + id + character_name
    assert.deepEqual(ClientMessage.encode(decoded).finish(), bytes);
});

test('creation with only the default race is still sent explicitly when the user confirmed it', () => {
    const bytes = encodeAuthenticateRequest('abc', 'N', toCreationMessage(defaultDraft()));
    const decoded = ClientMessage.decode(bytes);

    assert.equal(decoded.payload?.$case, 'authenticateRequest');
    if (decoded.payload?.$case !== 'authenticateRequest') {
        return;
    }
    assert.notEqual(decoded.payload.value.creation, undefined); // an empty message is present, not absent
});
