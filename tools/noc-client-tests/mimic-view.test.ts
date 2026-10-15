// Client-side mimicry logic (T69): catalog and snapshot -> view models, the Soul Forge / bar previews, levels and slots, and the new wire cases.
// Run: pnpm run proto:generate && node --import tsx --test noc-client-tests/*.test.ts   (from tools/)
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { toCatalogModel, type CatalogModel } from '../../multiplayer/mp-client/src/utils/NocCatalogView';
import { toNocViewModel, type NocViewModel } from '../../multiplayer/mp-client/src/utils/NocView';
import {
    buildSlots,
    classAllows,
    consumePreview,
    equipPreview,
    essenceCounts,
    formatCooldown,
    fusePreview,
    fusionOf,
    levelProgress,
    mimicLevelOf,
    ownedAbilities,
    powerMultiplier,
    slotUnlockLevel,
} from '../../multiplayer/mp-client/src/utils/MimicView';
import { ClientMessage } from '../../multiplayer/mp-client/src/proto/generated/network';
import { NocCatalog, NocMimicInfo } from '../../multiplayer/mp-client/src/proto/generated/nocthemar';
import { makeNocSnapshot } from './support';

const essence = (id: number) => 5000 + (id - 2000);

const mimic = (id: number, key: string, over: Partial<NocMimicInfo> = {}): NocMimicInfo =>
    NocMimicInfo.fromPartial({
        id,
        key,
        name: key.toUpperCase(),
        kind: 0,
        rarity: 0,
        monsterName: id < 2100 ? `Monster ${id}` : '',
        classIds: [],
        requiresRaid: false,
        cooldownMs: 30000,
        manaCost: 30,
        desc: `desc ${key}`,
        ready: true,
        essenceItemId: id < 2100 ? essence(id) : 0,
        ...over,
    });

// Shipped values: thresholds 10..450 (levels 1-10, +5 % power per level), slots 1 + 1 per 10 levels up to 9.
const catalogMessage = (): NocCatalog =>
    NocCatalog.fromPartial({
        mimics: [
            mimic(2001, 'morsure', { ready: false }),
            mimic(2003, 'peau_de_fer', { kind: 1, rarity: 1, classIds: [0, 4], cooldownMs: 40000, manaCost: 50 }),
            mimic(2005, 'frenesie', { rarity: 1 }),
            mimic(2008, 'putrefaction', { ready: false }),
            mimic(2102, 'bete_immortelle', { rarity: 2, ready: false }),
        ],
        fusions: [{ a: 2005, b: 2008, result: 2102 }],
        mimicXpThresholds: [10n, 30n, 60n, 100n, 150n, 210n, 280n, 360n, 450n],
        mimicPowerPerLevel: 0.05,
        mimicSlotsFirst: 1,
        mimicLevelsPerSlot: 10,
        mimicSlotsMax: 9,
    });

const model = (): CatalogModel => toCatalogModel(NocCatalog.decode(NocCatalog.encode(catalogMessage()).finish()));

const view = (over: Partial<Parameters<typeof makeNocSnapshot>[0]> = {}): NocViewModel =>
    toNocViewModel(makeNocSnapshot({ level: 5, classId: 0, mimicSlots: 1, ...over }));

test('the mimicry catalog survives the wire and converts to view objects', () => {
    const m = model();

    assert.equal(m.mimics.length, 5);
    const iron = m.mimicsById.get(2003)!;
    assert.equal(iron.kind, 'defensive');
    assert.equal(iron.rarity, 'rare');
    assert.deepEqual(iron.classIds, [0, 4]);
    assert.equal(iron.cooldownMs, 40000);
    assert.equal(iron.essenceItemId, 5003);
    assert.equal(iron.monsterName, 'Monster 2003');
    assert.equal(iron.isFusion, false);

    const fused = m.mimicsById.get(2102)!;
    assert.equal(fused.isFusion, true);
    assert.equal(fused.monsterName, null);
    assert.equal(fused.essenceItemId, null);

    assert.deepEqual(m.fusions, [{ a: 2005, b: 2008, result: 2102 }]);
    assert.deepEqual(m.mimicRules, { thresholds: [10, 30, 60, 100, 150, 210, 280, 360, 450], maxLevel: 10, powerPerLevel: 0.05, slotsFirst: 1, levelsPerSlot: 10, slotsMax: 9 });
});

test('an unknown kind or rarity degrades instead of throwing', () => {
    const m = toCatalogModel(NocCatalog.fromPartial({ mimics: [mimic(2001, 'x', { kind: 42, rarity: 99 })] }));

    assert.equal(m.mimics[0].kind, 'utility');
    assert.equal(m.mimics[0].rarity, 'common');
});

test('a server without mimicry gives an empty catalog and no rules', () => {
    const m = toCatalogModel(NocCatalog.fromPartial({}));

    assert.deepEqual(m.mimics, []);
    assert.deepEqual(m.fusions, []);
    assert.equal(m.mimicRules, null);
});

test('the snapshot carries learned abilities, the bar and the slots', () => {
    const v = view({ mimics: [{ id: 2003, xp: 35n, level: 3 }, { id: 2005, xp: 0n, level: 1 }], mimicEquipped: [2005], mimicSlots: 3 });

    assert.deepEqual(v.mimics, [{ id: 2003, xp: 35, level: 3 }, { id: 2005, xp: 0, level: 1 }]);
    assert.deepEqual(v.mimicEquipped, [2005]);
    assert.equal(v.mimicSlots, 3);
});

test('a snapshot from a server without mimicry has no abilities and no slots', () => {
    const v = toNocViewModel(makeNocSnapshot({ level: 3 }));

    assert.deepEqual(v.mimics, []);
    assert.deepEqual(v.mimicEquipped, []);
    assert.equal(v.mimicSlots, 0);
});

test('class exclusivities', () => {
    const m = model();
    const iron = m.mimicsById.get(2003)!;

    assert.equal(classAllows(iron, 0), true);
    assert.equal(classAllows(iron, 4), true);
    assert.equal(classAllows(iron, 1), false);
    assert.equal(classAllows(iron, null), false); // no class yet
    assert.equal(classAllows(m.mimicsById.get(2005)!, null), true); // open to everyone
});

test('consume preview follows the server rules, in order', () => {
    const m = model();

    assert.deepEqual(consumePreview(m, view(), 2005), { ok: true });
    assert.deepEqual(consumePreview(m, view(), 4242), { ok: false, reason: 'Unknown ability.' });
    assert.deepEqual(consumePreview(m, view(), 2102), { ok: false, reason: 'This ability can only be obtained by fusion.' });
    assert.deepEqual(consumePreview(m, view({ classId: 1 }), 2003), { ok: false, reason: 'This ability is reserved to another class.' });
    assert.deepEqual(consumePreview(m, view({ mimics: [{ id: 2005, xp: 0n, level: 1 }] }), 2005), { ok: false, reason: 'Ability already learned.' });
});

test('fuse preview: recipe, order, and the same one-per-monster and class rules on the result', () => {
    const m = model();

    assert.equal(fusionOf(m, 2008, 2005)?.result, 2102);
    assert.equal(fusionOf(m, 2001, 2005), undefined);

    const ok = fusePreview(m, view(), 2008, 2005);
    assert.equal(ok.ok, true);
    assert.equal(ok.result?.id, 2102);

    assert.deepEqual(fusePreview(m, view(), 2005, 2005), { ok: false, reason: 'Choose two different essences.' });
    assert.deepEqual(fusePreview(m, view(), 2005, 4242), { ok: false, reason: 'Unknown essence.' });
    assert.deepEqual(fusePreview(m, view(), 2102, 2005), { ok: false, reason: 'A fused ability cannot be fused again.' });
    assert.deepEqual(fusePreview(m, view(), 2001, 2005), { ok: false, reason: 'These essences do not fuse.' });
    assert.equal(fusePreview(m, view({ mimics: [{ id: 2102, xp: 0n, level: 1 }] }), 2005, 2008).reason, 'Ability already learned.');
});

test('equip preview', () => {
    const m = model();
    const learned = { mimics: [{ id: 2003, xp: 0n, level: 1 }, { id: 2005, xp: 0n, level: 1 }] };

    assert.deepEqual(equipPreview(m, view(learned), 2003), { ok: true });
    assert.deepEqual(equipPreview(m, view(), 2003), { ok: false, reason: 'Ability not learned.' });
    assert.deepEqual(equipPreview(m, view({ ...learned, mimicEquipped: [2003] }), 2003), { ok: false, reason: 'Ability already equipped.' });
    assert.deepEqual(equipPreview(m, view({ ...learned, mimicEquipped: [2003] }), 2005), { ok: false, reason: 'No free mimicry slot.' });
    assert.deepEqual(equipPreview(m, view({ ...learned, mimicEquipped: [2003], mimicSlots: 2 }), 2005), { ok: true });
    assert.deepEqual(equipPreview(m, view(learned), 4242), { ok: false, reason: 'Unknown ability.' });
});

test('essences in the bag are counted by ability, across stacks, ignoring other items', () => {
    const m = model();

    const counts = essenceCounts(m, [
        { itemId: essence(2005), quantity: 2 },
        { itemId: essence(2005) }, // no quantity = 1
        { itemId: essence(2008), quantity: 1 },
        { itemId: 5100, quantity: 9 }, // Essence Vial: not an essence
        { itemId: 17, quantity: 3 },
    ]);

    assert.deepEqual([...counts.entries()].sort((a, b) => a[0] - b[0]), [[2005, 3], [2008, 1]]);
    assert.equal(essenceCounts(m, []).size, 0);
});

test('ability levels follow the experience thresholds', () => {
    const rules = model().mimicRules!;

    assert.deepEqual([0, 9, 10, 29, 30, 449, 450, 10_000].map((xp) => mimicLevelOf(rules, xp)), [1, 1, 2, 2, 3, 9, 10, 10]);

    assert.deepEqual(levelProgress(rules, 0), { level: 1, maxLevel: 10, atMax: false, into: 0, needed: 10, ratio: 0 });
    assert.deepEqual(levelProgress(rules, 35), { level: 3, maxLevel: 10, atMax: false, into: 5, needed: 30, ratio: 5 / 30 });
    assert.deepEqual(levelProgress(rules, 450), { level: 10, maxLevel: 10, atMax: true, into: 0, needed: 0, ratio: 1 });
});

test('power grows 5 % per level and the level is clamped', () => {
    const rules = model().mimicRules!;

    assert.equal(powerMultiplier(rules, 1), 1);
    assert.ok(Math.abs(powerMultiplier(rules, 10) - 1.45) < 1e-9);
    assert.equal(powerMultiplier(rules, 0), 1);
    assert.equal(powerMultiplier(rules, 99), powerMultiplier(rules, 10));
});

test('slots open at level 1, 11, 21 ... 81 and match the server rule', () => {
    const rules = model().mimicRules!;

    assert.deepEqual([0, 1, 2, 8].map((i) => slotUnlockLevel(rules, i)), [1, 11, 21, 81]);
    assert.equal(slotUnlockLevel(rules, 9), null);
    assert.equal(slotUnlockLevel(rules, -1), null);

    // The server's SlotsAt(level) = min(max, first + (level - 1) / levelsPerSlot) agrees with the unlock levels.
    for (const level of [1, 10, 11, 20, 21, 80, 81, 100]) {
        const slots = Math.min(rules.slotsMax, rules.slotsFirst + Math.floor((level - 1) / rules.levelsPerSlot));
        const open = [...Array(rules.slotsMax).keys()].filter((i) => slotUnlockLevel(rules, i)! <= level).length;
        assert.equal(open, slots, `level ${level}`);
    }
});

test('the bar shows filled, free and locked slots', () => {
    const rules = model().mimicRules!;
    const slots = buildSlots(rules, view({ mimicSlots: 2, mimicEquipped: [2003] }));

    assert.equal(slots.length, 9);
    assert.deepEqual(slots[0], { index: 0, state: 'filled', abilityId: 2003, unlockLevel: null });
    assert.deepEqual(slots[1], { index: 1, state: 'free', abilityId: null, unlockLevel: null });
    assert.deepEqual(slots[2], { index: 2, state: 'locked', abilityId: null, unlockLevel: 21 });
    assert.equal(slots.filter((s) => s.state === 'locked').length, 7);
});

test('passives can open slots beyond the level-based ceiling', () => {
    const rules = model().mimicRules!;
    const slots = buildSlots(rules, view({ mimicSlots: 11 }));

    assert.equal(slots.length, 11);
    assert.equal(slots.every((s) => s.state === 'free'), true);
});

test('owned abilities are listed by id with their state; an unknown id is skipped', () => {
    const m = model();
    const v = view({ mimics: [{ id: 2005, xp: 12n, level: 2 }, { id: 4242, xp: 0n, level: 1 }, { id: 2003, xp: 0n, level: 1 }], mimicEquipped: [2005] });

    const owned = ownedAbilities(m, v);

    assert.deepEqual(owned.map((o) => [o.ability.id, o.equipped, o.state.xp]), [[2003, false, 0], [2005, true, 12]]);
});

test('cooldowns are shown in seconds', () => {
    assert.equal(formatCooldown(40000), '40 s');
    assert.equal(formatCooldown(32500), '32.5 s');
});

test('the Soul Forge and bar requests use the agreed client field numbers', () => {
    const cases: [ClientMessage, number, string][] = [
        [{ payload: { $case: 'nocCreateItemRequest', value: { itemId: 5003, quantity: 2 } } }, 104, 'nocCreateItemRequest'],
        [{ payload: { $case: 'nocForgeConsumeRequest', value: { abilityId: 2003 } } }, 105, 'nocForgeConsumeRequest'],
        [{ payload: { $case: 'nocForgeFuseRequest', value: { abilityA: 2005, abilityB: 2008 } } }, 106, 'nocForgeFuseRequest'],
        [{ payload: { $case: 'nocMimicEquipRequest', value: { abilityId: 2003, equipped: true } } }, 107, 'nocMimicEquipRequest'],
    ];
    for (const [message, field, name] of cases) {
        const bytes = ClientMessage.encode(message).finish();
        const tag = (field << 3) | 2; // length-delimited, two-byte varint for field >= 16
        assert.equal(bytes[0], (tag & 0x7f) | 0x80, name);
        assert.equal(bytes[1], tag >> 7, name);
        assert.equal(ClientMessage.decode(bytes).payload?.$case, name);
    }
    const fuse = ClientMessage.decode(ClientMessage.encode(cases[2][0]).finish()).payload;
    assert.deepEqual(fuse?.$case === 'nocForgeFuseRequest' ? fuse.value : null, { abilityA: 2005, abilityB: 2008 });
});
