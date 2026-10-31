#!/usr/bin/env python3
"""Advisory PR check: run the v1-writer census structural check only when the PR touches a tracked file.

Refs #4730. The census (docs/coordination/v1-writer-census.json) records a sha256 identity per tracked
source, and `check-v1-writer-census.py --structural` refuses when a source changed without its row being
rehashed. It ran nowhere on a pull request (ADR-0091 keeps backward-validity proofs out of ordinary
delivery; the nightly canary is scheduled only), so three drifts in two days (#4991, #5017, #5096) each
cost a follow-up PR found a day late. Root decided (option 2 on #4730) on an ADVISORY check, not a gate.

WHAT IT DOES.
  1. Derive the tracked paths from the census itself, at run time: every `path` value anywhere in
     docs/coordination/v1-writer-census.json. There is no second list to drift.
  2. Read the PR's change set from git (`git diff --raw -z <base> <head>`).
  3. The PR is TOUCHING when it changes a tracked path, the census file, this checker, or ADDS an
     executable file (the structural check also counts the tracked executable population, so a new
     executable with no row is drift too). Otherwise: print one line and exit 0 without running anything.
  4. When touching, run `check-v1-writer-census.py --structural` and report its verdict. A refusal is exit 1
     with the remedy: review the file's diff for a new sink, writer or permission, then rehash that row.

NOT REQUIRED. The workflow that calls this is deliberately not a required status check. A red result means
"rehash before the next engine release", exactly as the nightly canary says, but on the PR that caused it.

Exit: 0 = not touching, or touching and the census passes; 1 = touching and the census refuses; 3 = no
verdict (census unreadable, git unreadable). "I could not check" never shares a code with "clean".
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

CENSUS = "docs/coordination/v1-writer-census.json"
CHECKER = "scripts/check-v1-writer-census.py"
SELF = "scripts/check-v1-writer-census-advisory.py"
OK, FLAG, NO_VERDICT = 0, 1, 3


class NoVerdict(Exception):
    pass


def tracked_paths(root: Path) -> set[str]:
    """Every `path` string anywhere in the census: the one list of what it tracks."""
    try:
        data = json.loads((root / CENSUS).read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise NoVerdict(f"cannot read {CENSUS}: {error}") from error
    found: set[str] = set()

    def walk(node: object) -> None:
        if isinstance(node, dict):
            for key, value in node.items():
                if key == "path" and isinstance(value, str):
                    found.add(value)
                else:
                    walk(value)
        elif isinstance(node, list):
            for item in node:
                walk(item)

    walk(data)
    if not found:
        raise NoVerdict(f"{CENSUS} names no tracked path; an empty census would no-op every PR")
    return found


def changes(root: Path, base: str, head: str) -> list[tuple[str, str]]:
    """(path, new-mode) for every file the PR changes. Renames/copies count under their new name."""
    try:
        raw = subprocess.run(
            ["git", "-C", str(root), "diff", "--raw", "-z", "--no-renames", base, head],
            check=True, capture_output=True,
        ).stdout.decode("utf-8", "surrogateescape")
    except (OSError, subprocess.CalledProcessError) as error:
        raise NoVerdict(f"cannot read the change set {base}..{head}: {error}") from error
    parts = raw.split("\0")
    out: list[tuple[str, str]] = []
    i = 0
    while i + 1 < len(parts) and parts[i].startswith(":"):
        meta = parts[i].split()  # :oldmode newmode oldsha newsha status
        out.append((parts[i + 1], meta[1]))
        i += 2
    return out


def touching(tracked: set[str], diff: list[tuple[str, str]]) -> list[str]:
    reasons: list[str] = []
    for path, new_mode in diff:
        if path in tracked:
            reasons.append(f"{path} (census-tracked)")
        elif path in (CENSUS, CHECKER, SELF):
            reasons.append(f"{path} (the census or its checker)")
        elif new_mode == "100755":
            reasons.append(f"{path} (executable; the structural check counts the executable population)")
    return reasons


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--root", default=".")
    ap.add_argument("--base", default="HEAD^1", help="PR base (default: first parent of the merge commit)")
    ap.add_argument("--head", default="HEAD")
    args = ap.parse_args(argv)
    root = Path(args.root).resolve()
    try:
        tracked = tracked_paths(root)
        reasons = touching(tracked, changes(root, args.base, args.head))
    except NoVerdict as error:
        print(f"::error::census advisory: no verdict, {error}")
        return NO_VERDICT
    if not reasons:
        print(f"census advisory: no-op. This PR touches none of the {len(tracked)} census-tracked paths, "
              "adds no executable and does not edit the census, so the structural check was not run.")
        return OK
    print("census advisory: this PR touches " + str(len(reasons)) + " census-relevant file(s):")
    for reason in reasons:
        print(f"  - {reason}")
    result = subprocess.run(
        [sys.executable, str(root / CHECKER), "--structural", "--root", str(root)],
        capture_output=True, text=True,
    )
    sys.stdout.write(result.stdout)
    sys.stderr.write(result.stderr)
    if result.returncode == 0:
        print("census advisory: the structural check passes with this PR's changes.")
        return OK
    print(
        "::error title=census advisory: rehash needed::A census-tracked file changed and "
        f"{CENSUS} no longer matches it. Review the file's diff for a new sink, writer or permission; "
        "if there is none, recompute that row's sha256 and keep its disposition and justification. "
        "This check is advisory (not required): it exists so the drift is found on the PR that caused it."
    )
    return FLAG if result.returncode == 1 else NO_VERDICT


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
