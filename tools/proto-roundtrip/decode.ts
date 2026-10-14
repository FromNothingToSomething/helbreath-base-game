/// <reference types="node" />
/**
 * T23: decodes a serialized message with the generated TS types and compares it with its expected JSON.
 * Usage (from tools/): node --import tsx ./proto-roundtrip/decode.ts --bin <file> --expected <file>
 * The expected file's "payload" selects the message: noc_state_updated (ServerMessage) or authenticate_request (ClientMessage).
 * Exit code 1 on any mismatch (payload case, field values, or re-encoded bytes differing from the input).
 */
import { readFileSync } from 'node:fs';
import process from 'node:process';
import { ClientMessage, ServerMessage } from '../../multiplayer/mp-client/src/proto/generated/network.ts';

function arg(name: string): string {
    const i = process.argv.indexOf(`--${name}`);
    if (i < 0 || i + 1 >= process.argv.length) {
        console.error(`missing --${name}`);
        process.exit(2);
    }
    return process.argv[i + 1];
}

function sortKeys(value: unknown): unknown {
    if (Array.isArray(value)) return value.map(sortKeys);
    if (value && typeof value === 'object') {
        return Object.fromEntries(Object.entries(value as Record<string, unknown>).sort(([a], [b]) => a.localeCompare(b)).map(([k, v]) => [k, sortKeys(v)]));
    }
    return value;
}

function fail(message: string): never {
    console.error(`FAIL: ${message}`);
    process.exit(1);
}

const bytes = new Uint8Array(readFileSync(arg('bin')));
const expected = JSON.parse(readFileSync(arg('expected'), 'utf8'));

let actual: unknown;
let reencoded: Uint8Array;

if (expected.payload === 'noc_state_updated') {
    const message = ServerMessage.decode(bytes);
    if (message.payload?.$case !== 'nocStateUpdated') fail(`payload case is ${message.payload?.$case}, expected nocStateUpdated`);
    const s = message.payload.value.state;
    if (!s) fail('state is missing');
    // int64 is a bigint with forceLong=bigint; the expected json carries int64 as decimal strings.
    actual = {
        payload: 'noc_state_updated',
        state: { level: s.level, xp: s.xp.toString(), xp_to_next: s.xpToNext.toString(), mana: s.mana, max_mana: s.maxMana, race_id: s.raceId },
    };
    reencoded = ServerMessage.encode(message).finish();
} else if (expected.payload === 'authenticate_request') {
    const message = ClientMessage.decode(bytes);
    if (message.payload?.$case !== 'authenticateRequest') fail(`payload case is ${message.payload?.$case}, expected authenticateRequest`);
    const r = message.payload.value;
    const c = r.creation;
    if (!c) fail('creation is missing');
    actual = {
        payload: 'authenticate_request',
        id: r.id,
        character_name: r.characterName,
        creation: { race_id: c.raceId, gender: c.gender, skin_color: c.skinColor, hair_style_index: c.hairStyleIndex, underwear_color_index: c.underwearColorIndex },
    };
    reencoded = ClientMessage.encode(message).finish();
} else {
    fail(`unknown payload ${JSON.stringify(expected.payload)}`);
}

if (JSON.stringify(sortKeys(actual)) !== JSON.stringify(sortKeys(expected))) {
    console.error('FAIL: decoded value differs from the expected json');
    console.error('  actual:  ', JSON.stringify(sortKeys(actual)));
    console.error('  expected:', JSON.stringify(sortKeys(expected)));
    process.exit(1);
}

if (Buffer.compare(Buffer.from(reencoded), Buffer.from(bytes)) !== 0) {
    fail(`re-encoded bytes differ (${Buffer.from(reencoded).toString('hex')} vs ${Buffer.from(bytes).toString('hex')})`);
}

console.log(`OK ts: ${expected.payload}, ${bytes.length} bytes`);
