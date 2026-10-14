#!/usr/bin/env python3
"""Freezes protobuf field numbers.

  proto_fields.py dump  <file.proto>...              print "Container.field = number" lines (enum values prefixed with "enum ")
  proto_fields.py check <baseline.txt> <file.proto>...  fail when a baseline entry is missing or renumbered,
                                                     or when a number is used twice inside one message / enum

New fields and messages are allowed; only existing numbers are frozen.
"""
import re
import sys

TOKEN = re.compile(r"\b(message|enum|oneof|service|extend)\s+(\w+)\s*\{|\}|([^;{}]+);")
FIELD = re.compile(r"^(?:(?:repeated|optional|required)\s+)?(?:map\s*<[^>]*>|[\w.]+)\s+(\w+)\s*=\s*(\d+)\b")
ENUM_VALUE = re.compile(r"^(\w+)\s*=\s*(-?\d+)\b")
SKIP = re.compile(r"^(reserved|option|import|syntax|package|extensions)\b")


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def parse(path):
    """Returns (entries, errors); entries maps "Msg.Nested.field" (or "enum Name.VALUE") to its number."""
    text = strip_comments(open(path, encoding="utf-8").read())
    stack = []  # (kind, name); oneof shares the number space of its message
    entries, errors = {}, []
    used = {}  # container key -> {number: name}
    for m in TOKEN.finditer(text):
        if m.group(1):
            stack.append((m.group(1), m.group(2)))
        elif m.group(0) == "}":
            if not stack:
                errors.append(f"{path}: unbalanced '}}'")
            else:
                stack.pop()
        else:
            stmt = " ".join(m.group(3).split())
            if not stack or SKIP.match(stmt):
                continue
            kind = stack[-1][0]
            owners = [(k, n) for k, n in stack if k in ("message", "enum")]
            if not owners:
                continue
            container = ".".join(n for _, n in owners)
            if kind == "enum":
                f = ENUM_VALUE.match(stmt)
                key_prefix = "enum "
            elif kind in ("message", "oneof"):
                f = FIELD.match(stmt)
                key_prefix = ""
            else:
                continue
            if not f:
                errors.append(f"{path}: cannot parse statement in {container}: '{stmt}'")
                continue
            name, number = f.group(1), int(f.group(2))
            key = f"{key_prefix}{container}.{name}"
            entries[key] = number
            owner_key = f"{key_prefix}{container}"
            clash = used.setdefault(owner_key, {}).get(number)
            allow_alias = kind == "enum"  # allow_alias enums may repeat numbers
            if clash is not None and not allow_alias:
                errors.append(f"{path}: {owner_key} uses number {number} for both '{clash}' and '{name}'")
            used[owner_key][number] = name
    if stack:
        errors.append(f"{path}: unclosed block {stack[-1]}")
    return entries, errors


def main(argv):
    if len(argv) < 3 or argv[1] not in ("dump", "check"):
        print(__doc__)
        return 2
    if argv[1] == "dump":
        entries = {}
        for path in argv[2:]:
            e, errs = parse(path)
            if errs:
                print("\n".join(errs), file=sys.stderr)
                return 1
            entries.update(e)
        for key in sorted(entries):
            print(f"{key} = {entries[key]}")
        return 0

    baseline = {}
    for line in open(argv[2], encoding="utf-8"):
        line = line.strip()
        if line:
            key, _, number = line.rpartition(" = ")
            baseline[key] = int(number)
    current, problems = {}, []
    for path in argv[3:]:
        e, errs = parse(path)
        problems += errs
        current.update(e)
    for key, number in sorted(baseline.items()):
        if key not in current:
            problems.append(f"removed or renamed: {key} = {number}")
        elif current[key] != number:
            problems.append(f"renumbered: {key} was {number}, now {current[key]}")
    for p in problems:
        print(f"::error::{p}")
    if problems:
        return 1
    added = sorted(set(current) - set(baseline))
    print(f"proto fields OK: {len(baseline)} frozen, {len(added)} added")
    for key in added:
        print(f"  + {key} = {current[key]}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
