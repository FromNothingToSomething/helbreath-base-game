// T85: factions, reputation and quests: wire format -> view models, tier maths and quest state previews.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ClientMessage, InitialState, PlayerEnteredRange, ServerMessage } from '../../multiplayer/mp-client/src/proto/generated/network';
import { NocCatalog } from '../../multiplayer/mp-client/src/proto/generated/nocthemar';
import { makeNocSnapshot } from './support';
import { toCatalogModel } from '../../multiplayer/mp-client/src/utils/NocCatalogView';
import { toNocViewModel } from '../../multiplayer/mp-client/src/utils/NocView';
import { reputationBar, reputationOf, tierName, tierOf, type ReputationRules } from '../../multiplayer/mp-client/src/utils/FactionView';
import { objectiveText, questState } from '../../multiplayer/mp-client/src/utils/QuestView';
import { FACTION_COLORS, factionColor, factionColorLight, tierColor } from '../../multiplayer/mp-client/src/ui/factionTheme';

const catalog = (): NocCatalog => NocCatalog.fromPartial({
    factions: [
        { id: 0, key: 'chant_de_fer', name: 'Chant de Fer', hq: 'Aresden', symbol: 'anvil', colors: ['#c0c4cc', '#222222'], rivals: [1], desc: 'd0', perks: [{ tier: 2, modifiers: [{ stat: 3, kind: 1, value: 0.05 }] }] },
        { id: 1, key: 'choeur_abyssal', name: 'Choeur Abyssal', hq: 'Elvine', symbol: 'eye', colors: ['#7b4fbf'], rivals: [0], desc: 'd1', perks: [] },
    ],
    reputationRules: { min: -10000, max: 10000, joinLevel: 10, killBase: 10, neutral: -1000, friendly: 1000, honored: 3000, exalted: 6000 },
    quests: [
        { id: 0, key: 'q0', name: 'Fer', factionId: 0, minTier: 1, kind: 0, targetId: 51, count: 5, rewardXp: 500n, rewardRep: 50, rewardItems: [{ itemId: 35, quantity: 100 }], desc: 'k' },
        { id: 1, key: 'q1', name: 'Livraison', factionId: 0, minTier: 2, kind: 1, targetId: 5100, count: 3, rewardXp: 900n, rewardRep: 80, rewardItems: [], desc: 'l' },
    ],
    questMaxActive: 2,
});

const rules = (): ReputationRules => toCatalogModel(catalog()).reputationRules!;

test('catalog factions, rules and quests survive the wire', () => {
    const bytes = InitialState.encode(InitialState.fromPartial({ nocCatalog: catalog() })).finish();
    const model = toCatalogModel(InitialState.decode(bytes).nocCatalog!);
    assert.equal(model.factions.length, 2);
    assert.equal(model.factionsById.get(0)!.color, '#c0c4cc');
    assert.deepEqual(model.factionsById.get(0)!.rivals, [1]);
    assert.equal(model.factionsById.get(0)!.perks[0].modifiers[0].value, 0.05);
    assert.equal(model.reputationRules!.joinLevel, 10);
    assert.equal(model.quests.length, 2);
    assert.equal(model.questsById.get(0)!.kind, 'kill');
    assert.equal(model.questsById.get(1)!.kind, 'deliver');
    assert.equal(model.questsById.get(0)!.rewardXp, 500);
    assert.equal(model.questMaxActive, 2);
});

test('a server without factions gives an empty model', () => {
    const model = toCatalogModel({ classes: [], skills: [] } as unknown as NocCatalog);
    assert.equal(model.factions.length, 0);
    assert.equal(model.reputationRules, null);
    assert.equal(model.questMaxActive, 0);
});

test('snapshot faction, reputations, quests and quests_done reach the view model', () => {
    const view = toNocViewModel(makeNocSnapshot({
        factionId: 1,
        reputations: [{ factionId: 0, value: -50 }, { factionId: 1, value: 1200 }],
        quests: [{ questId: 3, progress: 2 }],
        questsDone: [0, 4],
    }));
    assert.equal(view.factionId, 1);
    assert.equal(reputationOf(view.reputations, 1), 1200);
    assert.equal(reputationOf(view.reputations, 2), 0);
    assert.deepEqual(view.quests, [{ questId: 3, progress: 2 }]);
    assert.deepEqual(view.questsDone, [0, 4]);
});

test('no faction in the snapshot means null', () => {
    assert.equal(toNocViewModel(makeNocSnapshot()).factionId, null);
});

test('tiers use the server thresholds, a floor belongs to the higher tier', () => {
    const r = rules();
    assert.equal(tierOf(r, -10000), 0);
    assert.equal(tierOf(r, -1001), 0);
    assert.equal(tierOf(r, -1000), 1);
    assert.equal(tierOf(r, 999), 1);
    assert.equal(tierOf(r, 1000), 2);
    assert.equal(tierOf(r, 3000), 3);
    assert.equal(tierOf(r, 6000), 4);
    assert.equal(tierOf(r, 10000), 4);
    assert.equal(tierName(3), 'Honored');
    assert.equal(tierName(9), 'Tier 9');
});

test('reputation bar progress inside the tier', () => {
    const r = rules();
    const mid = reputationBar(r, 2000);
    assert.equal(mid.tier, 2);
    assert.equal(mid.ratio, 0.5);
    assert.equal(mid.to, 3000);
    const top = reputationBar(r, 99999);
    assert.equal(top.tier, 4);
    assert.equal(top.ratio, 1);
    assert.equal(top.to, null);
    assert.equal(reputationBar(r, -99999).tier, 0);
});

test('quest state mirrors the server accept rules in order', () => {
    const model = toCatalogModel(catalog());
    const q0 = model.questsById.get(0)!;
    const q1 = model.questsById.get(1)!;
    const ctx = (over: Partial<Parameters<typeof questState>[1]> = {}) => ({ factionId: 0, tier: 2, active: [], done: [], maxActive: 2, ...over });

    assert.equal(questState(q0, ctx()).status, 'available');
    assert.equal(questState(q0, ctx({ factionId: null })).reason, 'Join a faction first.');
    assert.equal(questState(q0, ctx({ factionId: 1 })).reason, 'Quest of another faction.');
    assert.equal(questState(q1, ctx({ tier: 1 })).reason, 'Requires Friendly standing.');
    assert.equal(questState(q0, ctx({ active: [{ questId: 1, progress: 0 }, { questId: 9, progress: 0 }] })).reason, 'Too many active quests.');
    assert.equal(questState(q0, ctx({ done: [0] })).status, 'done');
    assert.deepEqual(questState(q0, ctx({ active: [{ questId: 0, progress: 2 }] })), { status: 'active', progress: 2 });
    assert.deepEqual(questState(q0, ctx({ active: [{ questId: 0, progress: 99 }] })), { status: 'complete', progress: 5 });
});

test('objective text clamps progress', () => {
    const q0 = toCatalogModel(catalog()).questsById.get(0)!;
    assert.equal(objectiveText(q0, 7, 'Orc'), 'Defeat Orc: 5 / 5');
    assert.equal(objectiveText(toCatalogModel(catalog()).questsById.get(1)!, -1, 'Vial'), 'Deliver Vial: 0 / 3');
});

test('faction theme: silver, violet, dark green, tarnished gold; unknown ids fall back', () => {
    assert.deepEqual(Object.values(FACTION_COLORS), ['#c0c4cc', '#7b4fbf', '#3f6b3a', '#b8923a']);
    assert.equal(factionColor(1), '#7b4fbf');
    assert.equal(factionColor(null), factionColor(42));
    assert.notEqual(factionColorLight(2), factionColor(2));
    assert.equal(tierColor(99), factionColor(null));
});

test('theme colors match the server catalog first colors', () => {
    const fs = toCatalogModel(catalog()).factions;
    assert.equal(fs[0].color, FACTION_COLORS[0]);
    assert.equal(fs[1].color, FACTION_COLORS[1]);
});

test('client messages 108-112 and PlayerFactionChanged round-trip', () => {
    const msgs = [
        { $case: 'nocFactionJoinRequest' as const, value: { factionId: 2 } },
        { $case: 'nocQuestAcceptRequest' as const, value: { questId: 4 } },
        { $case: 'nocQuestAbandonRequest' as const, value: { questId: 4 } },
        { $case: 'nocQuestTurnInRequest' as const, value: { questId: 4 } },
        { $case: 'nocReputationAddRequest' as const, value: { factionId: 1, delta: -300 } },
    ];
    for (const payload of msgs) {
        const decoded = ClientMessage.decode(ClientMessage.encode(ClientMessage.fromPartial({ payload })).finish());
        assert.deepEqual(decoded.payload, payload);
    }
    const sm = ServerMessage.decode(ServerMessage.encode(ServerMessage.fromPartial({
        payload: { $case: 'playerFactionChanged', value: { playerId: 7n, factionId: 3 } },
    })).finish());
    assert.equal(sm.payload?.$case, 'playerFactionChanged');
    assert.equal(sm.payload?.$case === 'playerFactionChanged' ? sm.payload.value.factionId : -1, 3);
    const none = ServerMessage.decode(ServerMessage.encode(ServerMessage.fromPartial({
        payload: { $case: 'playerFactionChanged', value: { playerId: 7n } },
    })).finish());
    assert.equal(none.payload?.$case === 'playerFactionChanged' ? none.payload.value.factionId : -1, undefined);
});

test('PlayerEnteredRange carries faction_id as field 20', () => {
    const withF = PlayerEnteredRange.decode(PlayerEnteredRange.encode(PlayerEnteredRange.fromPartial({ playerId: 5n, factionId: 2 })).finish());
    assert.equal(withF.factionId, 2);
    const without = PlayerEnteredRange.decode(PlayerEnteredRange.encode(PlayerEnteredRange.fromPartial({ playerId: 5n })).finish());
    assert.equal(without.factionId, undefined);
});
