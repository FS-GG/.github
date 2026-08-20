#!/usr/bin/env python3
"""Check open issue ``Paths:`` against the live tree and maintaining-lane facts.

Absence alone is not stale: a declaration may name a planned new file. A missing token is reported
only when it names a retired runtime root or Git rename history proves that its source moved to a
different destination which still exists. Exact-count baselines declare their maintaining issues
or an explicit exemption in a machine-readable ``board-paths-live`` comment.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import subprocess
import sys

RETIRED_ROOTS = (".codex/skills",)
PATHS = re.compile(r"(?m)^\s{0,3}Paths:\s*(.+?)\s*$")
BASELINE_DECLARATION = re.compile(r"(?m)^# board-paths-live:\s*(maintaining-issues|exempt)=(\S.*)$")
ISSUE_REF = re.compile(r"(?:FS-GG/)?\.github#([1-9][0-9]*)$")


class EvidenceError(RuntimeError):
    """An input required for a verdict could not be read."""


def load_json(path: Path, subject: str) -> object:
    try:
        with path.open(encoding="utf-8") as stream:
            return json.load(stream)
    except (OSError, json.JSONDecodeError) as error:
        raise EvidenceError(f"cannot read {subject}: {error}") from error


def declaration_tokens(body: str) -> list[str]:
    match = PATHS.search(body)
    if not match:
        return []
    return [token.rstrip("/") for token in re.split(r"[\s,]+", match.group(1)) if token]


def filesystem_token(token: str) -> str:
    if token.endswith("/**"):
        return token[:-3].rstrip("/")
    if token.endswith("/*"):
        return token[:-2].rstrip("/")
    return token.rstrip("/")


def token_covers(token: str, path: str) -> bool:
    base = filesystem_token(token)
    return path == base or path.startswith(base + "/")


def run_git(root: Path, args: list[str]) -> str:
    try:
        result = subprocess.run(
            ["git", "-C", str(root), *args], check=False, text=True,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        )
    except OSError as error:
        raise EvidenceError(f"cannot execute git: {error}") from error
    if result.returncode != 0:
        detail = result.stderr.strip() or f"exit {result.returncode}"
        raise EvidenceError(f"cannot read rename history: {detail}")
    return result.stdout


def live_rename_destination(root: Path, token: str) -> str | None:
    source = filesystem_token(token)
    commits = run_git(root, ["log", "--all", "--diff-filter=D", "--format=%H", "--", source]).splitlines()
    for commit in commits:
        if not re.fullmatch(r"[0-9a-fA-F]{40}", commit):
            continue
        changes = run_git(root, ["diff-tree", "-r", "-M", "--name-status", f"{commit}^", commit])
        for line in changes.splitlines():
            fields = line.split("\t")
            if len(fields) != 3 or not fields[0].startswith("R"):
                continue
            old, new = fields[1:]
            if (old == source or old.startswith(source + "/")) and (root / new).exists():
                return new
    return None


def is_exact_count_baseline(text: str) -> bool:
    return "THIS FILE ONLY EVER SHRINKS" in text and "EXACTLY" in text


def check_baselines(root: Path, baseline_root: Path, open_items: dict[int, dict]) -> int:
    reports = 0
    try:
        candidates = sorted(baseline_root.rglob("baseline.txt"))
    except OSError as error:
        raise EvidenceError(f"cannot enumerate exact-count baselines: {error}") from error
    for baseline in candidates:
        try:
            text = baseline.read_text(encoding="utf-8")
        except OSError as error:
            raise EvidenceError(f"cannot read exact-count baseline {baseline}: {error}") from error
        if not is_exact_count_baseline(text):
            continue
        try:
            baseline_path = baseline.resolve().relative_to(root.resolve()).as_posix()
        except ValueError as error:
            raise EvidenceError(f"exact-count baseline is outside repository root: {baseline}") from error
        declarations = BASELINE_DECLARATION.findall(text)
        if len(declarations) != 1:
            print(f"::error::board-paths-live: {baseline_path} must carry exactly one machine-readable maintaining-issues or exempt declaration")
            reports += 1
            continue
        kind, value = declarations[0]
        if kind == "exempt":
            if len(value.strip()) < 20:
                print(f"::error::board-paths-live: {baseline_path} exemption has no accountable reason")
                reports += 1
            continue
        refs = [part.strip() for part in value.split(",") if part.strip()]
        if not refs:
            print(f"::error::board-paths-live: {baseline_path} names no maintaining issue")
            reports += 1
            continue
        for ref in refs:
            match = ISSUE_REF.fullmatch(ref)
            if not match:
                print(f"::error::board-paths-live: {baseline_path} has invalid maintaining issue {ref}")
                reports += 1
                continue
            number = int(match.group(1))
            item = open_items.get(number)
            if item is None:
                continue
            tokens = declaration_tokens(item["body"])
            if not any(token_covers(token, baseline_path) for token in tokens):
                print(f"::error::board-paths-live: #{number} maintains {baseline_path} but its Paths declaration does not cover that baseline")
                reports += 1
    return reports


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--items", required=True, type=Path, help="JSON [{number, state, body}] from the board reader")
    parser.add_argument("--root", type=Path, default=Path.cwd(), help="repository root")
    parser.add_argument("--baseline-root", type=Path, help="tree containing exact-count baselines (default: ROOT/tests)")
    args = parser.parse_args()
    root = args.root.resolve()
    baseline_root = (args.baseline_root or root / "tests").resolve()
    try:
        raw_items = load_json(args.items, "board items")
        if not isinstance(raw_items, list):
            raise EvidenceError("board items are not a list")
        open_items: dict[int, dict] = {}
        for index, item in enumerate(raw_items):
            if not isinstance(item, dict):
                raise EvidenceError(f"board item at index {index} is not an object")
            state = item.get("state")
            if not isinstance(state, str) or state.upper() not in ("OPEN", "CLOSED"):
                raise EvidenceError(f"board item at index {index} has no readable issue state")
            if state.upper() != "OPEN" or "pull_request" in item:
                continue
            if not isinstance(item.get("number"), int) or not isinstance(item.get("body"), str):
                raise EvidenceError(f"open board item at index {index} lacks number or body")
            open_items[item["number"]] = item

        reports = 0
        rename_cache: dict[str, str | None] = {}
        for number, item in open_items.items():
            for token in declaration_tokens(item["body"]):
                subject = filesystem_token(token)
                if any(subject == retired or subject.startswith(retired + "/") for retired in RETIRED_ROOTS):
                    print(f"::error::board-paths-live: #{number} declares retired root token {token}")
                    reports += 1
                    continue
                if not subject or subject == "none" or (root / subject).exists():
                    continue
                if subject not in rename_cache:
                    rename_cache[subject] = live_rename_destination(root, subject)
                destination = rename_cache[subject]
                if destination:
                    print(f"::error::board-paths-live: #{number} declares moved-away token {token}; rename history points to live {destination}")
                    reports += 1
        reports += check_baselines(root, baseline_root, open_items)
    except EvidenceError as error:
        print(f"::error::board-paths-live: {error}", file=sys.stderr)
        return 2
    print(f"board-paths-live: {reports} coherence violation(s); absent paths without rename evidence remain allowed as planned files")
    return 1 if reports else 0


if __name__ == "__main__":
    raise SystemExit(main())
