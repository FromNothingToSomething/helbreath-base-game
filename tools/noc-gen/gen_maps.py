#!/usr/bin/env python3
"""Checks, copies and generates .amd map files (module 6, T92).

  gen_maps.py check <file.amd>...                        print size / blocked cells; exit 1 on a malformed file
  gen_maps.py copy  <source.amd> <name> [--out DIR]      copy a map under a new name (default DIR: multiplayer/server/Config/maps and
                                                         multiplayer/mp-client/public/assets/maps when it exists), after checking it
  gen_maps.py blank <name> <sizeX> <sizeY> [--sprite N] [--out DIR]
                                                         write an all-walkable map with a blocked border (a test map, not a zone)

Format (see sp-client/docs/MAP_RENDERING.md, server World/Game/MapFile.cs): 256-byte ASCII header "MAPSIZEX = <n> MAPSIZEY = <n> TILESIZE = <n>"
padded with NULs, then sizeY rows of sizeX tiles of TILESIZE bytes; tile = int16 sprite, int16 frame, int16 object sprite, int16 object frame,
byte flags (0x80 blocked, 0x40 teleport, 0x20 farming) + padding. The shipped maps all use TILESIZE 10 and have exactly header + X*Y*TILESIZE bytes.
"""
import os
import re
import shutil
import struct
import sys

HEADER = 256
MIN_TILE = 9
MAX_SIDE = 1000
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SERVER_MAPS = os.path.join(ROOT, "multiplayer", "server", "Config", "maps")
CLIENT_MAPS = os.path.join(ROOT, "multiplayer", "mp-client", "public", "assets", "maps")
NAME = re.compile(r"^[a-z][a-z0-9_-]{1,31}$")


def inspect(data, name):
    """Returns (sizeX, sizeY, tileSize, blocked); raises ValueError listing the problems."""
    if len(data) < HEADER:
        raise ValueError(f"{name}: {len(data)} bytes is smaller than the {HEADER}-byte header")
    head = data[:HEADER].decode("ascii", "replace").replace("\0", " ")
    vals = {k: int(v) for k, v in re.findall(r"(MAPSIZEX|MAPSIZEY|TILESIZE)\s*=\s*(\d+)", head)}
    sx, sy, ts = vals.get("MAPSIZEX", 0), vals.get("MAPSIZEY", 0), vals.get("TILESIZE", 0)
    errors = []
    if not 1 <= sx <= MAX_SIDE:
        errors.append(f"MAPSIZEX must be in [1, {MAX_SIDE}] (got {sx})")
    if not 1 <= sy <= MAX_SIDE:
        errors.append(f"MAPSIZEY must be in [1, {MAX_SIDE}] (got {sy})")
    if not MIN_TILE <= ts <= 64:
        errors.append(f"TILESIZE must be in [{MIN_TILE}, 64] (got {ts})")
    if errors:
        raise ValueError(f"{name}: " + "; ".join(errors))
    expected = HEADER + sx * sy * ts
    if len(data) != expected:
        raise ValueError(f"{name}: {len(data)} bytes, expected exactly {expected} for {sx}x{sy} tiles of {ts} bytes")
    blocked = sum(1 for off in range(HEADER + 8, len(data), ts) if data[off] & 0x80)
    return sx, sy, ts, blocked


def header(sx, sy, ts):
    text = f"MAPSIZEX = {sx:4d} MAPSIZEY = {sy:4d} TILESIZE = {ts}".encode("ascii")
    return text + b"\0" * (HEADER - len(text))


def out_dirs(args):
    if "--out" in args:
        return [args[args.index("--out") + 1]]
    return [d for d in (SERVER_MAPS, CLIENT_MAPS) if os.path.isdir(d)]


def check_name(name):
    if not NAME.match(name):
        sys.exit(f"map name '{name}' must match {NAME.pattern}")


def cmd_check(files):
    status = 0
    for path in files:
        try:
            sx, sy, ts, blocked = inspect(open(path, "rb").read(), os.path.basename(path))
            print(f"OK   {os.path.basename(path)}: {sx}x{sy}, tile {ts} bytes, {blocked} blocked cells")
        except ValueError as e:
            print(f"FAIL {e}")
            status = 1
    return status


def cmd_copy(args):
    source, name = args[0], args[1]
    check_name(name)
    data = open(source, "rb").read()
    inspect(data, os.path.basename(source))
    for d in out_dirs(args):
        target = os.path.join(d, name + ".amd")
        if os.path.exists(target):
            sys.exit(f"{target} already exists; remove it first")
        shutil.copyfile(source, target)
        print("wrote", target)
    return 0


def cmd_blank(args):
    name, sx, sy = args[0], int(args[1]), int(args[2])
    check_name(name)
    sprite = int(args[args.index("--sprite") + 1]) if "--sprite" in args else 0
    ts = 10
    rows = []
    for y in range(sy):
        row = bytearray()
        for x in range(sx):
            border = x in (0, sx - 1) or y in (0, sy - 1)
            row += struct.pack("<hhhhB", sprite, 0, 0, 0, 0x80 if border else 0) + b"\0" * (ts - 9)
        rows.append(bytes(row))
    data = header(sx, sy, ts) + b"".join(rows)
    inspect(data, name)
    for d in out_dirs(args):
        target = os.path.join(d, name + ".amd")
        if os.path.exists(target):
            sys.exit(f"{target} already exists; remove it first")
        open(target, "wb").write(data)
        print("wrote", target)
    return 0


def main(argv):
    if len(argv) < 2 or argv[1] not in ("check", "copy", "blank"):
        print(__doc__)
        return 2
    cmd, rest = argv[1], argv[2:]
    if cmd == "check":
        return cmd_check(rest)
    if cmd == "copy" and len(rest) >= 2:
        return cmd_copy(rest)
    if cmd == "blank" and len(rest) >= 3:
        return cmd_blank(rest)
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
