// Client-side NOCTHEMAR logic that needs no browser: snapshot -> view model, and the wire format as decoded by the generated TS code.
// Run: pnpm run proto:generate && node --import tsx --test noc-client-tests/*.test.ts   (from tools/)
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { toNocViewModel } from '../../multiplayer/mp-client/src/utils/NocView';
import { ClientMessage, InitialState, PlayerEnteredRange, ServerMessage } from '../../multiplayer/mp-client/src/proto/generated/network';
import { NocSnapshot } from '../../multiplayer/mp-client/src/proto/generated/nocthemar';
import { makeNocSnapshot } from './support';

const snapshot = (over: Partial<NocSnapshot> = {}): NocSnapshot => makeNocSnapshot({
    level: 7,
    xp: 250n,
    xpToNext: 1000n,
    mana: 40,
    maxMana: 160,
    raceId: 0,
    classId: undefined,
    skillPoints: 0,
    skills: [],
    ...over,
});

test('view model converts bigint xp and computes ratios', () => {
    const view = toNocViewModel(snapshot());
    assert.equal(view.level, 7);
    assert.equal(view.xp, 250);
    assert.equal(view.xpToNext, 1000);
    assert.equal(view.xpRatio, 0.25);
    assert.equal(view.atMaxLevel, false);
    assert.equal(view.mana, 40);
    assert.equal(view.maxMana, 160);
    assert.equal(view.manaRatio, 0.25);
});

test('max level: xpToNext 0 means a full bar and atMaxLevel', () => {
    const view = toNocViewModel(snapshot({ level: 100, xp: 0n, xpToNext: 0n }));
    assert.equal(view.atMaxLevel, true);
    assert.equal(view.xpRatio, 1);
});

test('ratios are clamped to 0..1', () => {
    const over = toNocViewModel(snapshot({ xp: 5000n, xpToNext: 1000n, mana: 999, maxMana: 100 }));
    assert.equal(over.xpRatio, 1);
    assert.equal(over.manaRatio, 1);
    const under = toNocViewModel(snapshot({ xp: -5n, mana: -3 }));
    assert.equal(under.xpRatio, 0);
    assert.equal(under.manaRatio, 0);
});

test('zero max mana gives a zero mana ratio, not NaN', () => {
    const view = toNocViewModel(snapshot({ mana: 0, maxMana: 0 }));
    assert.equal(view.manaRatio, 0);
});

test('NocStateUpdated is field 100 on the wire and decodes with the generated TS types', () => {
    const bytes = ServerMessage.encode({
        payload: { $case: 'nocStateUpdated', value: { state: snapshot() } },
    }).finish();
    // Tag (100 << 3) | 2 = 802 -> varint A2 06
    assert.equal(bytes[0], 0xa2);
    assert.equal(bytes[1], 0x06);

    const decoded = ServerMessage.decode(bytes);
    assert.equal(decoded.payload?.$case, 'nocStateUpdated');
    if (decoded.payload?.$case !== 'nocStateUpdated') {
        return;
    }
    assert.deepEqual(decoded.payload.value.state, snapshot());
});

test('InitialState carries the noc block as field 28', () => {
    const encoded = InitialState.encode({ ...InitialState.fromPartial({ playerId: 9n }), noc: snapshot() }).finish();
    const decoded = InitialState.decode(encoded);
    assert.deepEqual(decoded.noc, snapshot());
    assert.equal(InitialState.decode(InitialState.encode(InitialState.fromPartial({ playerId: 9n })).finish()).noc, undefined);
});

test('large xp survives the wire as bigint', () => {
    const bytes = ServerMessage.encode({
        payload: { $case: 'nocStateUpdated', value: { state: snapshot({ xp: 9_007_199_254_740_993n }) } },
    }).finish();
    const decoded = ServerMessage.decode(bytes);
    assert.equal(decoded.payload?.$case === 'nocStateUpdated' ? decoded.payload.value.state?.xp : undefined, 9_007_199_254_740_993n);
});

test('NocGrantXpRequest is client field 100 on the wire', () => {
    const bytes = ClientMessage.encode({
        payload: { $case: 'nocGrantXpRequest', value: { amount: 100n } },
    }).finish();
    assert.equal(bytes[0], 0xa2);
    assert.equal(bytes[1], 0x06);

    const decoded = ClientMessage.decode(bytes);
    assert.equal(decoded.payload?.$case, 'nocGrantXpRequest');
    assert.equal(decoded.payload?.$case === 'nocGrantXpRequest' ? decoded.payload.value.amount : undefined, 100n);
});

test('AuthenticateRequest carries the character creation as field 3', () => {
    const creation = { raceId: 4, gender: 1, skinColor: 2, hairStyleIndex: 6, underwearColorIndex: 3 };
    const bytes = ClientMessage.encode({
        payload: { $case: 'authenticateRequest', value: { id: 'abc', characterName: 'Zoë', creation } },
    }).finish();

    const decoded = ClientMessage.decode(bytes);
    assert.equal(decoded.payload?.$case, 'authenticateRequest');
    if (decoded.payload?.$case !== 'authenticateRequest') {
        return;
    }
    assert.equal(decoded.payload.value.id, 'abc');
    assert.equal(decoded.payload.value.characterName, 'Zoë');
    assert.deepEqual(decoded.payload.value.creation, creation);
});

test('AuthenticateRequest without creation still decodes (older clients)', () => {
    const bytes = ClientMessage.encode({
        payload: { $case: 'authenticateRequest', value: { id: 'abc', characterName: 'Zoë', creation: undefined } },
    }).finish();

    const decoded = ClientMessage.decode(bytes);
    assert.equal(decoded.payload?.$case === 'authenticateRequest' ? decoded.payload.value.creation : 'wrong case', undefined);
});

test('race id 0 is not on the wire and an absent race id decodes to 0', () => {
    const withoutRace = ServerMessage.encode({
        payload: { $case: 'nocStateUpdated', value: { state: snapshot({ raceId: 0 }) } },
    }).finish();
    const withRace = ServerMessage.encode({
        payload: { $case: 'nocStateUpdated', value: { state: snapshot({ raceId: 3 }) } },
    }).finish();

    // Field 6, varint: tag 0x30. Two bytes longer with a race (tag + value), identical to the old layout without one.
    assert.equal(withRace.length, withoutRace.length + 2);
    const decoded = ServerMessage.decode(withoutRace);
    assert.equal(decoded.payload?.$case === 'nocStateUpdated' ? decoded.payload.value.state?.raceId : undefined, 0);
});

test('PlayerEnteredRange carries race_id as field 19', () => {
    const encoded = PlayerEnteredRange.encode(PlayerEnteredRange.fromPartial({ playerId: 5n, raceId: 2 })).finish();

    assert.equal(PlayerEnteredRange.decode(encoded).raceId, 2);
    assert.equal(PlayerEnteredRange.decode(PlayerEnteredRange.encode(PlayerEnteredRange.fromPartial({ playerId: 5n })).finish()).raceId, 0);
});

test('T35: view model carries the race id, key and name', () => {
    const view = toNocViewModel(snapshot({ raceId: 3 }));
    assert.equal(view.raceId, 3);
    assert.equal(view.raceKey, 'reanime');
    assert.equal(view.raceName, 'Réanimés');
});

test('T35: race 0 (absent on the wire) is the default race', () => {
    const view = toNocViewModel(snapshot());
    assert.equal(view.raceId, 0);
    assert.equal(view.raceKey, 'valdornien');
    assert.equal(view.raceName, 'Valdorniens');
});

test('T35: a race id this client does not know is shown by id with no key', () => {
    const view = toNocViewModel(snapshot({ raceId: 42 }));
    assert.equal(view.raceId, 42);
    assert.equal(view.raceKey, null);
    assert.equal(view.raceName, 'Race 42');
});

test('T35: every shipped race gets its own name in the view model, and the other fields are unchanged', () => {
    const names = new Set<string>();
    for (let raceId = 0; raceId <= 5; raceId++) {
        const view = toNocViewModel(snapshot({ raceId }));
        names.add(view.raceName);
        assert.equal(view.level, 7);
        assert.equal(view.xpRatio, 0.25);
        assert.equal(view.manaRatio, 0.25);
    }
    assert.equal(names.size, 6);
});

test('T35: the race survives the wire into the view model', () => {
    const bytes = ServerMessage.encode({
        payload: { $case: 'nocStateUpdated', value: { state: snapshot({ raceId: 5 }) } },
    }).finish();
    const decoded = ServerMessage.decode(bytes);
    assert.equal(decoded.payload?.$case, 'nocStateUpdated');
    if (decoded.payload?.$case !== 'nocStateUpdated' || !decoded.payload.value.state) {
        return;
    }
    assert.equal(toNocViewModel(decoded.payload.value.state).raceName, 'Lychens');
});
