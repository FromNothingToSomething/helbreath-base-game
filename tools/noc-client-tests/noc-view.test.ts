// Client-side NOCTHEMAR logic that needs no browser: snapshot -> view model, and the wire format as decoded by the generated TS code.
// Run: pnpm run proto:generate && node --import tsx --test noc-client-tests/*.test.ts   (from tools/)
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { toNocViewModel } from '../../multiplayer/mp-client/src/utils/NocView';
import { InitialState, ServerMessage } from '../../multiplayer/mp-client/src/proto/generated/network';
import { NocSnapshot } from '../../multiplayer/mp-client/src/proto/generated/nocthemar';

const snapshot = (over: Partial<NocSnapshot> = {}): NocSnapshot => ({
    level: 7,
    xp: 250n,
    xpToNext: 1000n,
    mana: 40,
    maxMana: 160,
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
