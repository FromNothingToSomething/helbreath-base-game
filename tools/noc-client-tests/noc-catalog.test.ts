// T51: catalog wire format -> view model, and the client-side preview of the learn rules.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ClientMessage, InitialState, ServerMessage } from '../../multiplayer/mp-client/src/proto/generated/network';
import { NocCatalog, NocSnapshot } from '../../multiplayer/mp-client/src/proto/generated/nocthemar';
import {
    branchSkills,
    classById,
    skillState,
    sortClassesForRace,
    toCatalogModel,
} from '../../multiplayer/mp-client/src/utils/NocCatalogView';
import { toNocViewModel } from '../../multiplayer/mp-client/src/utils/NocView';

const skill = (id: number, branch: number, slot: number, over: Record<string, unknown> = {}) => ({
    id,
    key: `s${id}`,
    name: `Skill ${id}`,
    classId: 0,
    branch,
    slot,
    level: 1,
    kind: slot === 7 ? 2 : slot % 2,
    cost: 1,
    desc: `d${id}`,
    ready: true,
    prerequisiteId: 0,
    ...over,
});

const catalog = (): NocCatalog => ({
    classes: [
        { id: 0, key: 'knight', name: 'Chevalier', role: 'tank', branches: [{ id: 0, key: 'a', name: 'A', role: '' }], recommendedRaceIds: [0, 2] },
        { id: 1, key: 'singer', name: 'Chanteur', role: 'support', branches: [], recommendedRaceIds: [1] },
        { id: 2, key: 'rogue', name: 'Voleur', role: 'dps', branches: [], recommendedRaceIds: [] },
    ],
    skills: [
        skill(1000, 0, 0),
        skill(1001, 0, 1, { level: 3, prerequisiteId: 1000 }),
        skill(1003, 0, 3, { level: 10, ready: false, prerequisiteId: 1001 }),
        skill(1010, 1, 0),
        skill(1020, 2, 0),
    ],
});

test('catalog survives the wire and converts to a view model', () => {
    const bytes = InitialState.encode(InitialState.fromPartial({ nocCatalog: catalog() })).finish();
    const decoded = InitialState.decode(bytes).nocCatalog!;
    const model = toCatalogModel(decoded);

    assert.equal(model.classes.length, 3);
    assert.equal(model.skills.length, 5);
    assert.equal(model.skillsById.get(1001)!.prerequisiteId, 1000);
    assert.equal(model.skillsById.get(1000)!.prerequisiteId, null);
    assert.equal(model.skillsById.get(1000)!.kind, 'passive');
    assert.equal(model.skillsById.get(1001)!.kind, 'active');
    assert.equal(classById(model, 1)!.name, 'Chanteur');
    assert.deepEqual(branchSkills(model, 0, 0).map((s) => s.id), [1000, 1001, 1003]);
});

test('an unknown skill kind degrades to passive', () => {
    const model = toCatalogModel({ classes: [], skills: [skill(1000, 0, 0, { kind: 9 })] });
    assert.equal(model.skills[0].kind, 'passive');
});

test('classes recommended for the race come first and the others stay selectable', () => {
    const model = toCatalogModel(catalog());
    assert.deepEqual(sortClassesForRace(model.classes, 2).map((c) => c.id), [0, 1, 2]);
    assert.deepEqual(sortClassesForRace(model.classes, 1).map((c) => c.id), [1, 0, 2]);
    assert.deepEqual(sortClassesForRace(model.classes, 5).map((c) => c.id), [0, 1, 2]);
});

test('skill state mirrors the server rules in order', () => {
    const model = toCatalogModel(catalog());
    const s = (id: number) => model.skillsById.get(id)!;
    const ctx = (level: number, skills: number[], skillPoints: number) => ({ level, skills, skillPoints });

    assert.deepEqual(skillState(model, s(1000), ctx(1, [1000], 0)), { status: 'learned' });
    assert.equal(skillState(model, s(1003), ctx(30, [1000, 1001], 9)).status, 'soon');
    assert.deepEqual(skillState(model, s(1001), ctx(2, [1000], 5)), { status: 'locked', reason: 'Requires level 3.' });
    assert.deepEqual(skillState(model, s(1001), ctx(3, [], 5)), { status: 'locked', reason: 'Learn Skill 1000 first.' });
    assert.deepEqual(skillState(model, s(1000), ctx(1, [], 0)), { status: 'locked', reason: 'Not enough skill points.' });
    assert.deepEqual(skillState(model, s(1000), ctx(1, [], 1)), { status: 'learnable' });
});

test('only two branches can be combined', () => {
    const model = toCatalogModel(catalog());
    const third = model.skillsById.get(1020)!;

    assert.deepEqual(skillState(model, third, { level: 5, skills: [1000, 1010], skillPoints: 3 }), {
        status: 'locked',
        reason: 'Only two branches can be combined.',
    });
    assert.equal(skillState(model, third, { level: 5, skills: [1000], skillPoints: 3 }).status, 'learnable');
});

test('snapshot class, points and skills reach the view model', () => {
    const snapshot: NocSnapshot = { level: 5, xp: 0n, xpToNext: 10n, mana: 1, maxMana: 2, raceId: 1, classId: 2, skillPoints: 3, skills: [1000, 1010] };
    const decoded = NocSnapshot.decode(NocSnapshot.encode(snapshot).finish());
    const view = toNocViewModel(decoded);

    assert.equal(view.classId, 2);
    assert.equal(view.skillPoints, 3);
    assert.deepEqual(view.skills, [1000, 1010]);
    assert.equal(toNocViewModel({ ...snapshot, classId: undefined }).classId, null);
});

test('class and skill requests and the refusal use the agreed wire cases', () => {
    const choose = ClientMessage.decode(ClientMessage.encode({ payload: { $case: 'nocChooseClassRequest', value: { classId: 3 } } }).finish());
    const learn = ClientMessage.decode(ClientMessage.encode({ payload: { $case: 'nocLearnSkillRequest', value: { skillId: 1001 } } }).finish());
    const refused = ServerMessage.decode(ServerMessage.encode({ payload: { $case: 'nocActionRefused', value: { reason: 'Requires level 3.' } } }).finish());

    assert.deepEqual(choose.payload, { $case: 'nocChooseClassRequest', value: { classId: 3 } });
    assert.deepEqual(learn.payload, { $case: 'nocLearnSkillRequest', value: { skillId: 1001 } });
    assert.deepEqual(refused.payload, { $case: 'nocActionRefused', value: { reason: 'Requires level 3.' } });
});
