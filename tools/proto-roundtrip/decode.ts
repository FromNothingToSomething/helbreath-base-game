/// <reference types="node" />
/**
 * T23: decodes a serialized ServerMessage with the generated TS types and compares it with expected.json.
 * Usage (from tools/): node --import tsx ./proto-roundtrip/decode.ts --bin <file> --expected <file>
 * Exit code 1 on any mismatch (payload case, field values, or re-encoded bytes differing from the input).
 */
import { readFileSync } from 'node:fs';
import process from 'node:process';
import { ServerMessage } from '../../multiplayer/mp-client/src/proto/generated/network.ts';

function arg(name: string): string {
    const i = process.argv.indexOf(`--${name}`);
    if (i < 0 || i + 1 >= process.argv.length) {
        console.error(`missing --${name}`);
        process.exit(2);
    }
    return process.argv[i + 1];
}

const bytes = new Uint8Array(readFileSync(arg('bin')));
const expected = JSON.parse(readFileSync(arg('expected'), 'utf8'));

const message = ServerMessage.decode(bytes);
if (message.payload?.$case !== 'nocStateUpdated') {
    console.error(`FAIL: payload case is ${message.payload?.$case}, expected nocStateUpdated`);
    process.exit(1);
}

const s = message.payload.value.state;
if (!s) {
    console.error('FAIL: state is missing');
    process.exit(1);
}

// int64 is a bigint with forceLong=bigint; expected.json carries int64 as decimal strings.
const actual = {
    payload: 'noc_state_updated',
    state: { level: s.level, xp: s.xp.toString(), xp_to_next: s.xpToNext.toString(), mana: s.mana, max_mana: s.maxMana },
};

const same = JSON.stringify(sortKeys(actual)) === JSON.stringify(sortKeys(expected));
if (!same) {
    console.error('FAIL: decoded value differs from expected.json');
    console.error('  actual:  ', JSON.stringify(sortKeys(actual)));
    console.error('  expected:', JSON.stringify(sortKeys(expected)));
    process.exit(1);
}

const reencoded = ServerMessage.encode(message).finish();
if (Buffer.compare(Buffer.from(reencoded), Buffer.from(bytes)) !== 0) {
    console.error(`FAIL: re-encoded bytes differ (${Buffer.from(reencoded).toString('hex')} vs ${Buffer.from(bytes).toString('hex')})`);
    process.exit(1);
}

console.log(`OK ts: ${bytes.length} bytes, ${JSON.stringify(sortKeys(actual))}`);

function sortKeys(value: unknown): unknown {
    if (Array.isArray(value)) return value.map(sortKeys);
    if (value && typeof value === 'object') {
        return Object.fromEntries(Object.entries(value as Record<string, unknown>).sort(([a], [b]) => a.localeCompare(b)).map(([k, v]) => [k, sortKeys(v)]));
    }
    return value;
}
