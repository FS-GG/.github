#!/usr/bin/env python3
"""Conservatively decide whether an expensive CI self-test is affected.

The live gates remain authoritative.  This classifier controls only expensive
self-tests and fails closed: unreadable or ambiguous input means "run".
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
from pathlib import Path


HEX_SHA = re.compile(r"^[0-9a-fA-F]{40}$")

SUBJECTS = {
    "signature-doc": (
        "src/",
        "scripts/check-signature-doc-siting.py",
        "scripts/ci-gate-impact.py",
        "tests/signature-doc-siting/",
        ".github/workflows/signature-doc-siting.yml",
    ),
    "shell-fixture": (
        "scripts/ci-gate-impact.py",
        "scripts/install-shellcheck.sh",
        "scripts/lint-shell.sh",
        "scripts/lib/extract-workflow-shell.py",
        "scripts/lib/filter-sc2050.py",
        "tests/shell-lint/",
        ".github/workflows/shell-lint.yml",
    ),
}


def relevant(path: str, subjects: tuple[str, ...]) -> bool:
    return any(path == subject or (subject.endswith("/") and path.startswith(subject)) for subject in subjects)


def changed_paths(root: Path, base: str | None, head: str | None, paths_file: str | None) -> tuple[list[str] | None, str | None]:
    if paths_file:
        try:
            return [line for line in Path(paths_file).read_text(encoding="utf-8").splitlines() if line], None
        except (OSError, UnicodeError) as error:
            return None, f"paths-input-unreadable:{type(error).__name__}"

    if not base or not head or not HEX_SHA.fullmatch(base) or not HEX_SHA.fullmatch(head) or set(base) == {"0"}:
        return None, "revision-input-unavailable"

    try:
        result = subprocess.run(
            ["git", "diff", "--name-only", "--no-renames", "-z", base, head, "--"],
            cwd=root,
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
    except OSError as error:
        return None, f"git-diff-unavailable:{type(error).__name__}"
    if result.returncode != 0:
        return None, f"git-diff-refused:rc-{result.returncode}"
    try:
        paths = [item.decode("utf-8") for item in result.stdout.split(b"\0") if item]
    except UnicodeDecodeError:
        return None, "git-path-not-utf8"
    return paths, None


def write_output(path: str | None, decision: dict[str, object]) -> None:
    if not path:
        return
    reason = str(decision["reason"]).replace("\r", " ").replace("\n", " ")
    matched = ",".join(str(item) for item in decision["matched"])
    with open(path, "a", encoding="utf-8") as stream:
        stream.write(f"run={'true' if decision['run'] else 'false'}\n")
        stream.write(f"reason={reason}\n")
        stream.write(f"matched={matched}\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("gate", choices=sorted(SUBJECTS))
    parser.add_argument("--root", default=".")
    parser.add_argument("--base")
    parser.add_argument("--head")
    parser.add_argument("--paths-file")
    parser.add_argument("--github-output", default=os.environ.get("GITHUB_OUTPUT"))
    args = parser.parse_args()

    root = Path(args.root).resolve()
    paths, error = changed_paths(root, args.base, args.head, args.paths_file)
    if error:
        decision: dict[str, object] = {
            "schema": "fsgg.ci-gate-impact/1",
            "gate": args.gate,
            "run": True,
            "reason": error,
            "matched": [],
        }
    else:
        assert paths is not None
        matched = sorted(path for path in paths if relevant(path, SUBJECTS[args.gate]))
        decision = {
            "schema": "fsgg.ci-gate-impact/1",
            "gate": args.gate,
            "run": bool(matched),
            "reason": "affected-subject" if matched else "unrelated-change",
            "matched": matched,
        }

    write_output(args.github_output, decision)
    print(json.dumps(decision, sort_keys=True, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
