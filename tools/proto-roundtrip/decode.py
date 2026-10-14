#!/usr/bin/env python3
"""T23, third implementation: decodes a committed fixture with protobuf-python and compares it with its expected JSON.

Usage: decode.py --gen <dir with network_pb2.py and nocthemar_pb2.py> --bin <file> --expected <file>
The expected file's "payload" selects the message: noc_state_updated (ServerMessage) or authenticate_request (ClientMessage).
Exit code 1 on any mismatch (payload case, decoded fields, or re-encoded bytes differing from the input).
"""
import argparse
import json
import sys


def snapshot(s):
    return {"level": s.level, "xp": str(s.xp), "xp_to_next": str(s.xp_to_next), "mana": s.mana, "max_mana": s.max_mana, "race_id": s.race_id}


def creation(c):
    return {"race_id": c.race_id, "gender": c.gender, "skin_color": c.skin_color,
            "hair_style_index": c.hair_style_index, "underwear_color_index": c.underwear_color_index}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--gen", required=True)
    ap.add_argument("--bin", required=True)
    ap.add_argument("--expected", required=True)
    args = ap.parse_args()
    sys.path.insert(0, args.gen)
    import network_pb2  # noqa: E402  (generated, path set above)

    data = open(args.bin, "rb").read()
    expected = json.load(open(args.expected, encoding="utf-8"))
    wanted = expected.get("payload")

    if wanted == "noc_state_updated":
        message = network_pb2.ServerMessage()
        message.ParseFromString(data)
        case = message.WhichOneof("payload")
        actual = {"payload": case, "state": snapshot(message.noc_state_updated.state)} if case == wanted else {"payload": case}
    elif wanted == "authenticate_request":
        message = network_pb2.ClientMessage()
        message.ParseFromString(data)
        case = message.WhichOneof("payload")
        if case == wanted:
            r = message.authenticate_request
            actual = {"payload": case, "id": r.id, "character_name": r.character_name, "creation": creation(r.creation)}
        else:
            actual = {"payload": case}
    else:
        print(f"FAIL: unknown payload {wanted!r} in {args.expected}")
        return 1

    if actual != expected:
        print("FAIL: decoded value differs from the expected json")
        print("  actual:  ", json.dumps(actual, sort_keys=True, ensure_ascii=False))
        print("  expected:", json.dumps(expected, sort_keys=True, ensure_ascii=False))
        return 1

    # Deterministic serialization of a fixed message must reproduce the fixture byte for byte.
    reencoded = message.SerializeToString(deterministic=True)
    if reencoded != data:
        print(f"FAIL: re-encoded bytes differ from input ({reencoded.hex()} vs {data.hex()})")
        return 1

    print(f"OK python: {wanted}, {len(data)} bytes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
