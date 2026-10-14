/// <reference types="node" />
/**
 * T21 (state part), extended by T36 to the races: N simulated clients create a character (client i takes race i % 6, with its own appearance),
 * grant themselves XP (nocthemar.debug must be true), then reconnect; the state received in InitialState must equal the one the server
 * last sent before the disconnect, field by field: level, xp, xpToNext, mana, maxMana, raceId, max HP and the appearance.
 *
 * Usage (from tools/):
 *   node --import tsx ./noc-e2e/reconnect.ts --mode grant  --state state.json [--clients 20] [--ip 127.0.0.1] [--port 1337]
 *   node --import tsx ./noc-e2e/reconnect.ts --mode verify --state state.json [--ip ...] [--port ...]
 *
 * grant:  connect with a creation block (race i % 6), check the race was applied, grant XP, record S1 (last NocStateUpdated + HP),
 *         disconnect, reconnect at once (session still in its disconnect grace: in-memory state) and compare, disconnect again, write the state file.
 * verify: run after a graceful server restart (state reloaded from disk): reconnect with the same ids and compare with the state file.
 *         Each client sends a creation block for ANOTHER race: the save exists, so the server must ignore it (the race is immutable).
 * Needs at least 6 clients so that every race is covered; both phases also fail when a race is missing from the run, or when
 * races with different max-HP modifiers (Races.json) end up with the same max HP, or races with the same ones with different max HP.
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
}
interface Creation { raceId: number; gender: number; skinColor: number; hairStyleIndex: number; underwearColorIndex: number }
interface SavedClient { id: string; name: string; amount: string; race: number; creation: Creation; state: Snap }

const RACE_COUNT = 6;
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

    constructor(readonly id: string, readonly name: string) {}

    connect(creation?: Creation): Promise<void> {
        this.initial = undefined;
        this.last = undefined;
        this.look = undefined;
        this.updates = 0;
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

let failures = 0;
const report = (name: string, phase: string, expected: Snap, actual: Snap | undefined) => {
    const ok = actual !== undefined && same(expected, actual);
    if (!ok) failures++;
    console.log(`${ok ? 'OK  ' : 'FAIL'} ${name} [${phase}] expected=${JSON.stringify(expected)} actual=${JSON.stringify(actual)}`);
};

interface RaceJson { id: number; key: string; modifiers: { stat: string; kind: string; value: number }[] }
const races: RaceJson[] = (JSON.parse(readFileSync(join(process.cwd(), '..', 'multiplayer', 'server', 'Config', 'Nocthemar', 'Races.json'), 'utf8')) as { races: RaceJson[] }).races;
const maxHpSignature = (raceId: number): string =>
    JSON.stringify(races.find((r) => r.id === raceId)?.modifiers.filter((m) => m.stat === 'MaxHp').map((m) => [m.kind, m.value]).sort() ?? null);

/** Every race is present, and max HP follows the race: same max-HP modifiers, same max HP; different modifiers, different max HP. */
function checkRaces(phase: string, clients: { name: string; race: number; maxHp: number }[]): void {
    const seen = new Set(clients.map((c) => c.race));
    const missing = Array.from({ length: RACE_COUNT }, (_, r) => r).filter((r) => !seen.has(r));
    const problems: string[] = [];
    if (missing.length > 0) problems.push(`races missing from the run: ${missing.join(',')}`);
    const bySignature = new Map<string, Set<number>>();
    for (const c of clients) {
        const sig = maxHpSignature(c.race);
        if (!bySignature.has(sig)) bySignature.set(sig, new Set());
        bySignature.get(sig)!.add(c.maxHp);
    }
    for (const [sig, values] of bySignature) if (values.size !== 1) problems.push(`max HP differs for races with the same max-HP modifiers ${sig}: ${[...values].join(',')}`);
    const distinct = new Set([...bySignature.values()].map((v) => [...v][0]));
    if (distinct.size !== bySignature.size) problems.push('races with different max-HP modifiers share the same max HP (race not applied?)');
    for (let r = 0; r < RACE_COUNT; r++) {
        const mine = clients.filter((c) => c.race === r);
        console.log(`     race ${r} (${races.find((x) => x.id === r)?.key ?? '?'}): ${mine.length} client(s), max HP ${[...new Set(mine.map((c) => c.maxHp))].join('/') || '-'}`);
    }
    if (problems.length > 0) {
        failures += problems.length;
        for (const p of problems) console.log(`FAIL [${phase}] ${p}`);
    } else {
        console.log(`races covered [${phase}]: ${seen.size}/${RACE_COUNT}`);
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
        const s1 = bot.last!;
        if (s1.level <= before.level) throw new Error(`${bot.name}: no level-up from ${amount} XP (before=${JSON.stringify(before)}, after=${JSON.stringify(s1)})`);
        await bot.close();

        await bot.connect(); // same id, session still in its disconnect grace
        report(bot.name, 'reconnect in grace period', s1, bot.initial);
        await bot.close();
        saved.push({ id: bot.id, name: bot.name, amount: amount.toString(), race, creation, state: s1 });
    }));
    saved.sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true }));
    writeFileSync(statePath, JSON.stringify(saved, null, 1));
    checkRaces('before restart', saved.map((c) => ({ name: c.name, race: c.state.raceId, maxHp: c.state.maxHp })));
}

async function verify(): Promise<void> {
    const saved: SavedClient[] = JSON.parse(readFileSync(statePath, 'utf8'));
    const seen: { name: string; race: number; maxHp: number }[] = [];
    await Promise.all(saved.map(async (c) => {
        const bot = new Bot(c.id, c.name);
        // A save exists now: a creation block for another race must be ignored (the race is immutable).
        await bot.connect(creationFor(c.creation.hairStyleIndex + 1, (c.race + 1) % RACE_COUNT));
        report(c.name, 'after server restart (loaded from disk)', c.state, bot.initial);
        if (bot.initial) seen.push({ name: c.name, race: bot.initial.raceId, maxHp: bot.initial.maxHp });
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
