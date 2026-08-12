#!/usr/bin/env python3
"""Compare one command's actual JSON output against a fixture's checked-in expected output.

Strips the same wall-clock fields `record-board-fixture.py` already stripped when it wrote the
expectation (`fixture_lib.strip_volatile_output` — `driver --events`'s `renderedAt`/`observedAt`), so a
fixture replayed a second later still compares equal. Exits 0 on match, 1 on a real mismatch (a diff is
printed), 2 on malformed input.
"""

import difflib
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import fixture_lib as lib  # noqa: E402


def main():
    if len(sys.argv) != 3:
        print("usage: compare.py <actual.json> <expected.json>", file=sys.stderr)
        sys.exit(2)
    actual_file, expected_file = sys.argv[1], sys.argv[2]

    try:
        with open(actual_file) as f:
            actual = lib.strip_volatile_output(json.load(f))
    except json.JSONDecodeError as e:
        print(f"compare: actual output ({actual_file}) is not valid JSON: {e}", file=sys.stderr)
        sys.exit(2)

    try:
        with open(expected_file) as f:
            expected = json.load(f)
    except json.JSONDecodeError as e:
        print(f"compare: expected output ({expected_file}) is not valid JSON: {e}", file=sys.stderr)
        sys.exit(2)

    if actual == expected:
        sys.exit(0)

    a_lines = json.dumps(actual, indent=2, sort_keys=True).splitlines()
    e_lines = json.dumps(expected, indent=2, sort_keys=True).splitlines()
    diff = "\n".join(
        difflib.unified_diff(e_lines, a_lines, fromfile="expected", tofile="actual", lineterm="")
    )
    print(diff)
    sys.exit(1)


if __name__ == "__main__":
    main()
