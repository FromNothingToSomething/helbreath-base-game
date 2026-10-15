/// <reference types="node" />
/**
 * T21 (state part), extended by T36 to the races: N simulated clients create a character (client i takes race i % 6, with its own appearance),
 * grant themselves XP (nocthemar.debug must be true), then reconnect; the state received in InitialState must equal the one the server
 * last sent before the disconnect, field by field: level, xp, xpToNext, mana, maxMana, raceId, max HP and the appearance.
 *
 * T54 extends it to the classes: after the XP grant each client chooses a class (client i takes class i % 5), learns the first ready skill of that class
 * (class 3 has none ready at slot 0 and learns nothing), and a second class choice must be refused. classId, skillPoints and the learned skills are part of the
 * compared state, so they must also survive the reconnect and the server restart.
 *
 * T71 extends it to the mimicry (module 4): each client gets the essences of one open-to-its-class ability and of a fusion pair through the debug item request
 * (capture itself, a monster kill and a pickup, has no bot equivalent and is covered by the server tests), summons a Soul Forge next to itself, consumes the first essence,
 * fuses the other two, equips the learned ability (a second equip must be refused: one slot below level 11) and, for classes 0 and 4 (Iron Skin, a self buff), casts it once.
 * The learned abilities, their experience, the bar and the slot count are part of the compared state: they must survive the reconnect and the server restart.
 *
 * Usage (from tools/):
 *   node --import tsx ./noc-e2e/reconnect.ts --mode grant  --state state.json [--clients 20] [--ip 127.0.0.1] [--port 1337]
 *   node --import tsx ./noc-e2e/reconnect.ts --mode verify --state state.json [--ip ...] [--port ...]
 *
 * grant:  connect with a creation block (race i % 6), check the race was applied, grant XP, record S1 (last NocStateUpdated + HP),
 *         disconnect, reconnect at once (session still in its disconnect grace: in-memory state) and compare, disconnect again, write the state file.
 * verify: run after a graceful server restart (state reloaded from disk): reconnect with the same ids and compare with the state file.
 *         Each client sends a creation block for ANOTHER race: the save exists, so the server must ignore it (the race is immutable).
 * Needs at least 6 clients so that every race is covered; both phases also fail when a race is missing from the run, or when a client's
 * max HP is not exactly what its race and level give (Progression.json + the race's MaxHp modifiers in Races.json; max HP grows with the level).
 * Exit code 1 on any mismatch; "ALL IDENTICAL" is printed only when everything matched.
 */
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import process from 'node:process';
import { ClientMessage, ServerMessage } from '../../multiplayer/mp-client/src/proto/generated/network.ts';
import type { NocSnapshot } from '../../multiplayer/mp-client/src/proto/generated/nocthemar.ts';

interface Snap {
    level: number; xp: string; xpToNext: string; mana: number; maxMana: number;
    raceId: number; maxHp: number; gender: number; skinColor: number; hairStyleIndex: number; underwearColorIndex: number;
    classId: number | null; skillPoints: number; skills: number[];
    mimics: { id: number; xp: string; level: number }[]; mimicEquipped: number[]; mimicSlots: number;
}
interface Creation { raceId: number; gender: number; skinColor: number; hairStyleIndex: number; underwearColorIndex: number }
interface SavedClient { id: string; name: string; amount: string; race: number; creation: Creation; state: Snap }

const RACE_COUNT = 6;
const CLASS_COUNT = 5;
// First ready skill of each class at level 1 (Skills.json): knight 1000, singer 1100, shapeshifter 1200, class 3 none, forgemaster 1410.
const FIRST_SKILL: (number | null)[] = [1000, 1100, 1200, null, 1410];
// Mimicry (Mimic.json): 2001 Morsure infectée (open, not castable yet), 2003 Peau de fer (classes 0 and 4, buff handler), 2005 + 2008 fuse into 2102 Bête Immortelle.
// A mimicry ability's essence is the Items.json item 5000 + (id - 2000); the Soul Forge is NPC 10 and works within 3 cells.
const FORGE_NPC = 10;
const essenceItem = (abilityId: number) => 5000 + (abilityId - 2000);
const castableClass = (cls: number) => cls === 0 || cls === 4;
const consumeFor = (cls: number) => (castableClass(cls) ? 2003 : 2001);
const FUSE_A = 2005;
const FUSE_B = 2008;
const FUSED = 2102;
const NO_FORGE = 'No Soul Forge in range.';
// Every shipped race allows the whole appearance domain, so these always pass the server's validation; each client gets its own mix.
const creationFor = (index: number, raceId: number): Creation => ({
    raceId,
    gender: index % 2,
    skinColor: index % 3,
    hairStyleIndex: (index * 3) % 8,
    underwearColorIndex: (index * 5) % 8,
});

const args = new Map<string, string>();
for (let i = 2; i < process.argv.length; i += 2) args.set(process.argv[i].replace(/^--/, ''), process.argv[i + 1]);
const mode = args.get('mode');
const statePath = args.get('state') ?? 'noc-e2e-state.json';
const ip = args.get('ip') ?? '127.0.0.1';
const port = Number(args.get('port') ?? 1337);
const clientCount = Number(args.get('clients') ?? 20);

interface Look { maxHp: number; gender: number; skinColor: number; hairStyleIndex: number; underwearColorIndex: number }
const toSnap = (s: NocSnapshot, look: Look): Snap => ({
    level: s.level, xp: s.xp.toString(), xpToNext: s.xpToNext.toString(), mana: s.mana, maxMana: s.maxMana,
    raceId: s.raceId, maxHp: look.maxHp, gender: look.gender, skinColor: look.skinColor,
    hairStyleIndex: look.hairStyleIndex, underwearColorIndex: look.underwearColorIndex,
    classId: s.classId ?? null, skillPoints: s.skillPoints, skills: [...s.skills],
    mimics: s.mimics.map((m) => ({ id: m.id, xp: m.xp.toString(), level: m.level })), mimicEquipped: [...s.mimicEquipped], mimicSlots: s.mimicSlots,
});
const same = (a: Snap, b: Snap) => JSON.stringify(a) === JSON.stringify(b);
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

class Bot {
    private ws!: WebSocket;
    private ping: ReturnType<typeof setInterval> | undefined;
    private ready!: () => void;
    private failed!: (e: Error) => void;
    private pingSeq = 1;
    initial: Snap | undefined;
    last: Snap | undefined;
    private look: Look | undefined;
    updates = 0;
    refusals: string[] = [];
    bagAdds = 0;
    castFailures = 0;
    castSpeedMs = 600;

    constructor(readonly id: string, readonly name: string) {}

    connect(creation?: Creation): Promise<void> {
        this.initial = undefined;
        this.last = undefined;
        this.look = undefined;
        this.updates = 0;
        this.refusals = [];
        this.bagAdds = 0;
        this.castFailures = 0;
        const done = new Promise<void>((res, rej) => { this.ready = res; this.failed = rej; });
        this.ws = new WebSocket(`ws://${ip}:${port}/ws`);
        this.ws.binaryType = 'arraybuffer';
        const timeout = setTimeout(() => this.failed(new Error(`${this.name}: no InitialGameWorldState within 15 s`)), 15000);
        this.ws.addEventListener('open', () => this.send({ payload: { $case: 'authenticateRequest', value: { id: this.id, characterName: this.name, creation } } }));
        this.ws.addEventListener('close', () => this.failed(new Error(`${this.name}: closed before ready`)));
        this.ws.addEventListener('error', () => this.failed(new Error(`${this.name}: connection error`)));
        this.ws.addEventListener('message', (ev) => {
            const m = ServerMessage.decode(new Uint8Array(ev.data as ArrayBuffer));
            switch (m.payload?.$case) {
                case 'initialState':
                    this.castSpeedMs = m.payload.value.castSpeedMs;
                    this.look = {
                        maxHp: m.payload.value.maxHp, gender: m.payload.value.gender, skinColor: m.payload.value.skinColor,
                        hairStyleIndex: m.payload.value.hairStyleIndex, underwearColorIndex: m.payload.value.underwearColorIndex,
                    };
                    if (m.payload.value.noc) this.initial = toSnap(m.payload.value.noc, this.look);
                    break;
                case 'hpUpdated':
                    if (this.look) this.look = { ...this.look, maxHp: m.payload.value.maxHp };
                    break;
                case 'nocStateUpdated':
                    if (m.payload.value.state && this.look) { this.last = toSnap(m.payload.value.state, this.look); this.updates++; }
                    break;
                case 'nocActionRefused':
                    this.refusals.push(m.payload.value.reason);
                    break;
                case 'itemAddedToBag':
                    this.bagAdds++;
                    break;
                case 'spellCastFailed':
                    this.castFailures++;
                    break;
                case 'initialGameWorldState':
                    clearTimeout(timeout);
                    this.ping = setInterval(() => this.send({ payload: { $case: 'pingRequest', value: { sequence: this.pingSeq++ } } }), 1000);
                    this.ready();
                    break;
            }
        });
        return done;
    }

    send(m: ClientMessage): void {
        if (this.ws.readyState === WebSocket.OPEN) this.ws.send(ClientMessage.encode(m).finish());
    }

    grant(amount: bigint): void {
        this.send({ payload: { $case: 'nocGrantXpRequest', value: { amount } } });
    }

    chooseClass(classId: number): void {
        this.send({ payload: { $case: 'nocChooseClassRequest', value: { classId } } });
    }

    learn(skillId: number): void {
        this.send({ payload: { $case: 'nocLearnSkillRequest', value: { skillId } } });
    }

    /** Debug only (nocthemar.debug): puts `quantity` of an item in the bag. */
    createItem(itemId: number, quantity: number): void {
        this.send({ payload: { $case: 'nocCreateItemRequest', value: { itemId, quantity } } });
    }

    summonNpc(catalogNpcId: number): void {
        this.send({ payload: { $case: 'summonNpcRequest', value: { catalogNpcId, direction: 0 } } });
    }

    forgeConsume(abilityId: number): void {
        this.send({ payload: { $case: 'nocForgeConsumeRequest', value: { abilityId } } });
    }

    forgeFuse(abilityA: number, abilityB: number): void {
        this.send({ payload: { $case: 'nocForgeFuseRequest', value: { abilityA, abilityB } } });
    }

    equip(abilityId: number, equipped: boolean): void {
        this.send({ payload: { $case: 'nocMimicEquipRequest', value: { abilityId, equipped } } });
    }

    async close(): Promise<void> {
        clearInterval(this.ping);
        const ws = this.ws;
        this.failed = () => {};
        await new Promise<void>((res) => { ws.addEventListener('close', () => res()); ws.close(); setTimeout(res, 2000); });
    }
}

async function waitFor(cond: () => boolean, ms: number, what: string): Promise<void> {
    const end = Date.now() + ms;
    while (!cond()) {
        if (Date.now() > end) throw new Error(`timeout: ${what}`);
        await sleep(25);
    }
}

/**
 * Sends a Soul Forge request until `done` holds. The summoned forge appears on a later world tick, so "No Soul Forge in range." and silence are retried;
 * any other refusal ends the attempt with an error.
 */
async function viaForge(bot: Bot, send: () => void, done: () => boolean, what: string): Promise<void> {
    for (let attempt = 0; attempt < 8; attempt++) {
        bot.refusals = [];
        send();
        const end = Date.now() + 700;
        while (Date.now() < end && !done()) await sleep(25);
        if (done()) return;
        if (bot.refusals.some((r) => r !== NO_FORGE)) throw new Error(`${bot.name}: ${what} refused (${bot.refusals.join('; ')})`);
        await sleep(300);
    }
    throw new Error(`${bot.name}: ${what}: no effect after 8 attempts (Soul Forge never in range?)`);
}

const learned = (bot: Bot, abilityId: number) => bot.last?.mimics.some((m) => m.id === abilityId) === true;
const abilityXp = (bot: Bot, abilityId: number) => bot.last?.mimics.find((m) => m.id === abilityId)?.xp ?? '0';

/** Essences, Soul Forge, fusion, bar and (classes 0 and 4) one cast; returns the cast experience the ability must have. */
async function mimicry(bot: Bot, cls: number): Promise<{ learnedIds: number[]; castXp: string }> {
    const consumeId = consumeFor(cls);
    bot.createItem(essenceItem(consumeId), 1);
    bot.createItem(essenceItem(FUSE_A), 1);
    bot.createItem(essenceItem(FUSE_B), 1);
    await waitFor(() => bot.bagAdds >= 3, 5000, `${bot.name}: the three essences were not added to the bag (is nocthemar.debug true?)`);

    bot.summonNpc(FORGE_NPC);
    await sleep(300);
    await viaForge(bot, () => bot.forgeConsume(consumeId), () => learned(bot, consumeId), `consume ${consumeId}`);
    await viaForge(bot, () => bot.forgeFuse(FUSE_A, FUSE_B), () => learned(bot, FUSED), `fuse ${FUSE_A}+${FUSE_B}`);

    // One slot below level 11, so the second equip must be refused (the bar is changed without a forge).
    bot.refusals = [];
    bot.equip(consumeId, true);
    await waitFor(() => bot.last?.mimicEquipped.includes(consumeId) === true, 5000, `${bot.name}: ability ${consumeId} not equipped (${bot.refusals.join('; ')})`);
    bot.equip(FUSED, true);
    await waitFor(() => bot.refusals.length > 0, 5000, `${bot.name}: the second equip was not refused`);
    if (bot.last!.mimicSlots === 1 && bot.last!.mimicEquipped.length !== 1) throw new Error(`${bot.name}: bar ${JSON.stringify(bot.last!.mimicEquipped)} with 1 slot`);

    let castXp = '0';
    if (castableClass(cls)) {
        bot.refusals = [];
        bot.send({ payload: { $case: 'spellCastStartRequest', value: { spellId: consumeId } } });
        await sleep(bot.castSpeedMs + 300); // the server refuses a cast that comes before the cast bar is over
        bot.send({ payload: { $case: 'spellCastRequest', value: { x: 0, y: 0 } } });
        await waitFor(() => abilityXp(bot, consumeId) !== '0' || bot.castFailures > 0, 5000, `${bot.name}: no experience after casting ${consumeId}`);
        if (bot.castFailures > 0) throw new Error(`${bot.name}: cast of ${consumeId} failed (${bot.refusals.join('; ')})`);
        castXp = abilityXp(bot, consumeId);
        if (castXp !== '1') throw new Error(`${bot.name}: ability ${consumeId} has ${castXp} xp after one cast, expected 1`);
    }
    return { learnedIds: [consumeId, FUSED].sort((a, b) => a - b), castXp };
}

let failures = 0;
const report = (name: string, phase: string, expected: Snap, actual: Snap | undefined) => {
    const ok = actual !== undefined && same(expected, actual);
    if (!ok) failures++;
    console.log(`${ok ? 'OK  ' : 'FAIL'} ${name} [${phase}] expected=${JSON.stringify(expected)} actual=${JSON.stringify(actual)}`);
};

interface RaceJson { id: number; key: string; modifiers: { stat: string; kind: string; value: number }[] }
const nocConfigDir = join(process.cwd(), '..', 'multiplayer', 'server', 'Config', 'Nocthemar');
const races: RaceJson[] = (JSON.parse(readFileSync(join(nocConfigDir, 'Races.json'), 'utf8')) as { races: RaceJson[] }).races;
const progression = JSON.parse(readFileSync(join(nocConfigDir, 'Progression.json'), 'utf8')) as { maxLevel: number; hp: { base: number; perLevel: number } };

/**
 * Max HP the server must report: the level's base (Progression.json hp.base + hp.perLevel * (level - 1)) resolved through the race's MaxHp
 * modifiers in the order of StatPipeline: (base + flat) * (1 + sum of PercentAdd) * product of (1 + PercentMult), rounded half away from zero.
 * Max HP depends on the level, so clients of the same race but of different levels legitimately differ.
 */
function expectedMaxHp(raceId: number, level: number): number {
    const clamped = Math.min(Math.max(level, 1), progression.maxLevel);
    let flat = 0, add = 0, mult = 1;
    for (const m of races.find((r) => r.id === raceId)?.modifiers.filter((x) => x.stat === 'MaxHp') ?? []) {
        if (m.kind === 'Flat') flat += m.value;
        else if (m.kind === 'PercentAdd') add += m.value;
        else mult *= Math.max(0, 1 + m.value);
    }
    return Math.max(1, Math.round((progression.hp.base + progression.hp.perLevel * (clamped - 1) + flat) * Math.max(0, 1 + add) * mult));
}

/** Every race is present, and every client's max HP is exactly what its race and level give (same race and level, same max HP). */
function checkRaces(phase: string, clients: { name: string; race: number; level: number; maxHp: number }[]): void {
    const seen = new Set(clients.map((c) => c.race));
    const problems: string[] = [];
    const missing = Array.from({ length: RACE_COUNT }, (_, r) => r).filter((r) => !seen.has(r));
    if (missing.length > 0) problems.push(`races missing from the run: ${missing.join(',')}`);
    for (const c of clients) {
        const expected = expectedMaxHp(c.race, c.level);
        if (c.maxHp !== expected) problems.push(`${c.name}: race ${c.race} level ${c.level} has max HP ${c.maxHp}, expected ${expected}`);
    }
    for (let r = 0; r < RACE_COUNT; r++) {
        const mine = clients.filter((c) => c.race === r);
        const byLevel = [...new Set(mine.map((c) => c.level))].sort((a, b) => a - b).map((l) => `L${l}=${mine.find((c) => c.level === l)!.maxHp}`);
        console.log(`     race ${r} (${races.find((x) => x.id === r)?.key ?? '?'}): ${mine.length} client(s), max HP ${byLevel.join(' ') || '-'}`);
    }
    if (problems.length > 0) {
        failures += problems.length;
        for (const p of problems) console.log(`FAIL [${phase}] ${p}`);
    } else {
        console.log(`races covered [${phase}]: ${seen.size}/${RACE_COUNT}, max HP as expected for race and level`);
    }
}

async function grant(): Promise<void> {
    if (clientCount < RACE_COUNT) throw new Error(`--clients must be at least ${RACE_COUNT} to cover every race (got ${clientCount})`);
    const saved: SavedClient[] = [];
    await Promise.all(Array.from({ length: clientCount }, async (_, i) => {
        const bot = new Bot(crypto.randomUUID(), `NocE2E ${i + 1}`);
        const amount = 100n + 40n * BigInt(i);
        const race = i % RACE_COUNT;
        const creation = creationFor(i, race);
        await bot.connect(creation); // new character: the creation block applies
        const before = bot.initial;
        if (!before) throw new Error(`${bot.name}: InitialState carried no noc snapshot`);
        const applied = { raceId: before.raceId, gender: before.gender, skinColor: before.skinColor, hairStyleIndex: before.hairStyleIndex, underwearColorIndex: before.underwearColorIndex };
        if (JSON.stringify(applied) !== JSON.stringify(creation)) throw new Error(`${bot.name}: creation not applied, requested=${JSON.stringify(creation)} got=${JSON.stringify(applied)}`);
        bot.grant(amount);
        await waitFor(() => bot.updates > 0, 5000, `${bot.name}: no NocStateUpdated after the grant (is nocthemar.debug true?)`);
        await sleep(700); // 14 ticks: last state wins, mana regen included

        const cls = i % CLASS_COUNT;
        const skill = FIRST_SKILL[cls];
        bot.chooseClass(cls);
        await waitFor(() => bot.last?.classId === cls, 5000, `${bot.name}: class ${cls} not applied (${bot.refusals.join('; ')})`);
        if (skill !== null) {
            bot.learn(skill);
            await waitFor(() => bot.last?.skills.includes(skill) === true, 5000, `${bot.name}: skill ${skill} not learned (${bot.refusals.join('; ')})`);
        }
        bot.chooseClass((cls + 1) % CLASS_COUNT); // the class is final: refused, state unchanged
        await waitFor(() => bot.refusals.length > 0, 5000, `${bot.name}: second class choice was not refused`);

        const mimic = await mimicry(bot, cls);
        await sleep(700);
        const s1 = bot.last!;
        if (JSON.stringify(s1.mimics.map((m) => m.id)) !== JSON.stringify(mimic.learnedIds) || s1.mimicEquipped[0] !== consumeFor(cls) || s1.mimicEquipped.length !== 1) {
            throw new Error(`${bot.name}: unexpected mimicry state ${JSON.stringify({ mimics: s1.mimics, equipped: s1.mimicEquipped })}, expected abilities ${mimic.learnedIds} and ${consumeFor(cls)} equipped`);
        }
        if (abilityXp(bot, consumeFor(cls)) !== mimic.castXp) throw new Error(`${bot.name}: cast experience changed after the cast`);
        const expectedPoints = s1.level - 1 - (skill === null ? 0 : 1); // 1 point per level from level 2, 1 spent
        if (s1.classId !== cls || s1.skillPoints !== expectedPoints || JSON.stringify(s1.skills) !== JSON.stringify(skill === null ? [] : [skill])) {
            throw new Error(`${bot.name}: unexpected class state ${JSON.stringify({ classId: s1.classId, skillPoints: s1.skillPoints, skills: s1.skills })}, expected class ${cls} points ${expectedPoints} skill ${skill}`);
        }
        if (s1.level <= before.level) throw new Error(`${bot.name}: no level-up from ${amount} XP (before=${JSON.stringify(before)}, after=${JSON.stringify(s1)})`);
        await bot.close();

        await bot.connect(); // same id, session still in its disconnect grace
        report(bot.name, 'reconnect in grace period', s1, bot.initial);
        await bot.close();
        saved.push({ id: bot.id, name: bot.name, amount: amount.toString(), race, creation, state: s1 });
    }));
    saved.sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true }));
    writeFileSync(statePath, JSON.stringify(saved, null, 1));
    checkRaces('before restart', saved.map((c) => ({ name: c.name, race: c.state.raceId, level: c.state.level, maxHp: c.state.maxHp })));
}

async function verify(): Promise<void> {
    const saved: SavedClient[] = JSON.parse(readFileSync(statePath, 'utf8'));
    const seen: { name: string; race: number; level: number; maxHp: number }[] = [];
    await Promise.all(saved.map(async (c) => {
        const bot = new Bot(c.id, c.name);
        // A save exists now: a creation block for another race must be ignored (the race is immutable).
        await bot.connect(creationFor(c.creation.hairStyleIndex + 1, (c.race + 1) % RACE_COUNT));
        report(c.name, 'after server restart (loaded from disk)', c.state, bot.initial);
        if (bot.initial) seen.push({ name: c.name, race: bot.initial.raceId, level: bot.initial.level, maxHp: bot.initial.maxHp });
        await bot.close();
    }));
    checkRaces('after restart', seen);
}

async function main(): Promise<void> {
    try {
        if (mode === 'grant') await grant();
        else if (mode === 'verify') await verify();
        else throw new Error('--mode grant|verify');
    } catch (e) {
        console.error(`ERROR: ${(e as Error).message}`);
        process.exit(1);
    }
    console.log(failures === 0 ? 'ALL IDENTICAL' : `${failures} MISMATCH(ES)`);
    process.exit(failures === 0 ? 0 : 1);
}

void main();
