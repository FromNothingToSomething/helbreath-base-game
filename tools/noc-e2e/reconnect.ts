/// <reference types="node" />
/**
 * T21 (state part): N simulated clients grant themselves XP (nocthemar.debug must be true), then reconnect; the NocSnapshot
 * received in InitialState must equal the one the server last sent before the disconnect, field by field.
 *
 * Usage (from tools/):
 *   node --import tsx ./noc-e2e/reconnect.ts --mode grant  --state state.json [--clients 20] [--ip 127.0.0.1] [--port 1337]
 *   node --import tsx ./noc-e2e/reconnect.ts --mode verify --state state.json [--ip ...] [--port ...]
 *
 * grant:  connect, grant XP, record S1 (last NocStateUpdated), disconnect, reconnect at once (session still in its disconnect
 *         grace: in-memory state) and compare, disconnect again, write the state file.
 * verify: run after a graceful server restart (state reloaded from disk): reconnect with the same ids and compare with the state file.
 * Exit code 1 on any mismatch; the metric is the equality of (level, xp, xpToNext, mana, maxMana), printed per client.
 */
import { readFileSync, writeFileSync } from 'node:fs';
import process from 'node:process';
import { ClientMessage, ServerMessage } from '../../multiplayer/mp-client/src/proto/generated/network.ts';
import type { NocSnapshot } from '../../multiplayer/mp-client/src/proto/generated/nocthemar.ts';

interface Snap { level: number; xp: string; xpToNext: string; mana: number; maxMana: number }
interface SavedClient { id: string; name: string; amount: string; state: Snap }

const args = new Map<string, string>();
for (let i = 2; i < process.argv.length; i += 2) args.set(process.argv[i].replace(/^--/, ''), process.argv[i + 1]);
const mode = args.get('mode');
const statePath = args.get('state') ?? 'noc-e2e-state.json';
const ip = args.get('ip') ?? '127.0.0.1';
const port = Number(args.get('port') ?? 1337);
const clientCount = Number(args.get('clients') ?? 20);

const toSnap = (s: NocSnapshot): Snap => ({ level: s.level, xp: s.xp.toString(), xpToNext: s.xpToNext.toString(), mana: s.mana, maxMana: s.maxMana });
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
    updates = 0;

    constructor(readonly id: string, readonly name: string) {}

    connect(): Promise<void> {
        this.initial = undefined;
        this.last = undefined;
        this.updates = 0;
        const done = new Promise<void>((res, rej) => { this.ready = res; this.failed = rej; });
        this.ws = new WebSocket(`ws://${ip}:${port}/ws`);
        this.ws.binaryType = 'arraybuffer';
        const timeout = setTimeout(() => this.failed(new Error(`${this.name}: no InitialGameWorldState within 15 s`)), 15000);
        this.ws.addEventListener('open', () => this.send({ payload: { $case: 'authenticateRequest', value: { id: this.id, characterName: this.name } } }));
        this.ws.addEventListener('close', () => this.failed(new Error(`${this.name}: closed before ready`)));
        this.ws.addEventListener('error', () => this.failed(new Error(`${this.name}: connection error`)));
        this.ws.addEventListener('message', (ev) => {
            const m = ServerMessage.decode(new Uint8Array(ev.data as ArrayBuffer));
            switch (m.payload?.$case) {
                case 'initialState':
                    if (m.payload.value.noc) this.initial = toSnap(m.payload.value.noc);
                    break;
                case 'nocStateUpdated':
                    if (m.payload.value.state) { this.last = toSnap(m.payload.value.state); this.updates++; }
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

async function grant(): Promise<void> {
    const saved: SavedClient[] = [];
    await Promise.all(Array.from({ length: clientCount }, async (_, i) => {
        const bot = new Bot(crypto.randomUUID(), `NocE2E ${i + 1}`);
        const amount = 100n + 40n * BigInt(i);
        await bot.connect();
        const before = bot.initial;
        if (!before) throw new Error(`${bot.name}: InitialState carried no noc snapshot`);
        bot.grant(amount);
        await waitFor(() => bot.updates > 0, 5000, `${bot.name}: no NocStateUpdated after the grant (is nocthemar.debug true?)`);
        await sleep(700); // 14 ticks: last state wins, mana regen included
        const s1 = bot.last!;
        if (s1.level <= before.level) throw new Error(`${bot.name}: no level-up from ${amount} XP (before=${JSON.stringify(before)}, after=${JSON.stringify(s1)})`);
        await bot.close();

        await bot.connect(); // same id, session still in its disconnect grace
        report(bot.name, 'reconnect in grace period', s1, bot.initial);
        await bot.close();
        saved.push({ id: bot.id, name: bot.name, amount: amount.toString(), state: s1 });
    }));
    saved.sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true }));
    writeFileSync(statePath, JSON.stringify(saved, null, 1));
}

async function verify(): Promise<void> {
    const saved: SavedClient[] = JSON.parse(readFileSync(statePath, 'utf8'));
    await Promise.all(saved.map(async (c) => {
        const bot = new Bot(c.id, c.name);
        await bot.connect();
        report(c.name, 'after server restart (loaded from disk)', c.state, bot.initial);
        await bot.close();
    }));
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
