#!/usr/bin/env python3
"""Build the GATE-INVERSION input for tests/skill-registry/run.sh case 77 (.github#2545).

Reads the SHIPPED `registry/skills.delivery-channels.yml` and writes a copy with the
`fs-gg-rendering` / `product` entry — the one class this item is about — removed.

The mutation is a file this fixture WRITES, never an edit to the tree under test: a gate-inversion
that mutates the real declaration in place is one interrupted run away from leaving the repository
red for reasons nobody can reconstruct.

It ASSERTS the entry was found before removing it. A "mutation" that silently changed nothing would
make case 77 pass by removing nothing, which is the exact shape of an inversion that proves the gate
works while measuring nothing at all.

    invert-rendering-channel.py <shipped-declaration> <output-path>

Exit: 0 written; 1 on a shipped declaration that does not carry the entry (or cannot be read).
"""

from __future__ import annotations

import sys

import yaml

REMOVE = ("fs-gg-rendering", "product")


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        sys.stderr.write("usage: invert-rendering-channel.py <shipped-declaration> <output>\n")
        return 1
    source, target = argv
    with open(source) as handle:
        doc = yaml.safe_load(handle)

    classes = doc.get("classes")
    if not isinstance(classes, list):
        sys.stderr.write(f"invert-rendering-channel: {source} has no `classes` list\n")
        return 1

    kept = [
        entry
        for entry in classes
        if not (
            isinstance(entry, dict)
            and (entry.get("owner"), entry.get("scope")) == REMOVE
        )
    ]
    if len(kept) != len(classes) - 1:
        sys.stderr.write(
            f"invert-rendering-channel: expected exactly one {REMOVE[0]}/{REMOVE[1]} entry in "
            f"{source}, removed {len(classes) - len(kept)} — the inversion would measure nothing\n"
        )
        return 1

    doc["classes"] = kept
    with open(target, "w") as handle:
        yaml.safe_dump(doc, handle, sort_keys=False)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
