#!/usr/bin/env python3
"""T23, third implementation: decodes the committed fixture with protobuf-python and compares it with expected.json.

Usage: decode.py --gen <dir with network_pb2.py and nocthemar_pb2.py> --bin <file> --expected <file>
Exit code 1 on any mismatch (decoded fields, payload case, or re-encoded bytes differing from the input).
"""
import argparse
import json
import sys


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--gen", required=True)
    ap.add_argument("--bin", required=True)
    ap.add_argument("--expected", required=True)
    args = ap.parse_args()
    sys.path.insert(0, args.gen)
    import network_pb2  # noqa: E402  (generated, path set above)

    data = open(args.bin, "rb").read()
    message = network_pb2.ServerMessage()
    message.ParseFromString(data)

    case = message.WhichOneof("payload")
    if case != "noc_state_updated":
        print(f"FAIL: payload case is {case!r}, expected 'noc_state_updated'")
        return 1

    s = message.noc_state_updated.state
    actual = {
        "payload": case,
        "state": {"level": s.level, "xp": str(s.xp), "xp_to_next": str(s.xp_to_next), "mana": s.mana, "max_mana": s.max_mana},
    }
    expected = json.load(open(args.expected))
    if actual != expected:
        print("FAIL: decoded value differs from expected.json")
        print("  actual:  ", json.dumps(actual, sort_keys=True))
        print("  expected:", json.dumps(expected, sort_keys=True))
        return 1

    # Deterministic serialization of a fixed message must reproduce the fixture byte for byte.
    reencoded = message.SerializeToString(deterministic=True)
    if reencoded != data:
        print(f"FAIL: re-encoded bytes differ from input ({reencoded.hex()} vs {data.hex()})")
        return 1

    print(f"OK python: {len(data)} bytes, {json.dumps(actual, sort_keys=True)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
