/// <reference types="node" />
/**
 * T23: decodes a serialized message with the generated TS types and compares it with its expected JSON.
 * Usage (from tools/): node --import tsx ./proto-roundtrip/decode.ts --bin <file> --expected <file>
 * The expected file's "payload" selects the message: noc_state_updated (ServerMessage) or authenticate_request (ClientMessage).
 * Exit code 1 on any mismatch (payload case, field values, or a re-encoding that decodes to another message).
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
let decodeAgain: (data: Uint8Array) => unknown;

/** Structural form of a decoded message: int64 (bigint) as decimal strings, bytes as hex, keys sorted. */
function structural(value: unknown): string {
    return JSON.stringify(sortKeys(value), (_key, v) => (typeof v === 'bigint' ? v.toString() : v instanceof Uint8Array ? Buffer.from(v).toString('hex') : v));
}

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
    decodeAgain = (data) => ServerMessage.decode(data);
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
    decodeAgain = (data) => ClientMessage.decode(data);
} else {
    fail(`unknown payload ${JSON.stringify(expected.payload)}`);
}

if (JSON.stringify(sortKeys(actual)) !== JSON.stringify(sortKeys(expected))) {
    console.error('FAIL: decoded value differs from the expected json');
    console.error('  actual:  ', JSON.stringify(sortKeys(actual)));
    console.error('  expected:', JSON.stringify(sortKeys(expected)));
    process.exit(1);
}

// ts-proto writes an empty packed repeated field (e.g. NocSnapshot.skills) as tag + length 0, which is valid proto3 but not what protoc-based
// encoders (C#, Python) produce. So the re-encoding is not required to be byte-identical (the C# bytes are already compared byte for byte
// with the committed fixture by the workflow); it must decode to exactly the same message, and may only differ by such empty fields.
if (Buffer.compare(Buffer.from(reencoded), Buffer.from(bytes)) !== 0) {
    if (structural(decodeAgain(reencoded)) !== structural(decodeAgain(bytes))) {
        fail(`re-encoded bytes decode to another message (${Buffer.from(reencoded).toString('hex')} vs ${Buffer.from(bytes).toString('hex')})`);
    }
    if (reencoded.length < bytes.length) {
        fail(`re-encoded bytes are shorter than the input (${Buffer.from(reencoded).toString('hex')} vs ${Buffer.from(bytes).toString('hex')})`);
    }
    console.log(`note ts: re-encoding differs only by empty packed repeated fields (${reencoded.length} vs ${bytes.length} bytes)`);
}

console.log(`OK ts: ${expected.payload}, ${bytes.length} bytes`);
