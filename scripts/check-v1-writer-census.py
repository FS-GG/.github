#!/usr/bin/env python3
"""Fail closed when a tracked v1 write entry point is absent from the census."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import stat
import subprocess
import sys
from pathlib import Path
from typing import Any


SCHEMA = "fsgg.v1-writer-census/1"
CONTRACT_SCHEMA = "fsgg.coord.commands/1"
WRITER_DISPOSITIONS = {
    "remote-writer",
    "conditional-remote-writer",
    "protected-admin-writer",
    "publish-writer",
}
NON_WRITER_DISPOSITIONS = {
    "local-only",
    "read-only",
    "declaration-only",
    "build-only",
    "guard-only",
    "instruction-only",
    "test-only",
}
ALL_DISPOSITIONS = WRITER_DISPOSITIONS | NON_WRITER_DISPOSITIONS | {"typed-command-catalogue"}

# These are deliberately source patterns, not a hand-picked directory exclusion. Every tracked
# executable is inspected, as are every product source, workflow/action, registry, release/build
# declaration, and executable skill/build wrapper. Tests remain in the tracked executable population;
# a fixture that contains a real-looking sink must receive an explicit test-only census disposition.
POPULATION_PREFIXES = (
    "src/FS.GG.Coord",
    "src/FS.GG.Telemetry",
    "scripts/",
    "tools/",
    ".github/workflows/",
    ".github/actions/",
    "dist/",
    "registry/",
    ".agents/skills/",
    ".claude/skills/",
)
BUILD_DECLARATIONS = {
    "Directory.Build.props",
    "Directory.Packages.props",
    "global.json",
    "dist/dotnet/Directory.Build.props",
}
MANDATORY_SOURCES = {
    "src/FS.GG.Coord.Cli.Kernel/Options.fs",
    "src/FS.GG.Coord.Cli/Program.fs",
    "tools/routine-delivery.py",
    "src/FS.GG.Coord.Cli/TelemetryApplication.fs",
    "src/FS.GG.Coord.Cli/TelemetryRuntimeApplication.fs",
    "src/FS.GG.Coord.Cli/TelemetryStoreApplication.fs",
    "src/FS.GG.Coord.GitHub/Transport.fs",
    ".github/workflows/coord-engine.yml",
    ".github/workflows/release-coord-engine.yml",
    "dist/skill-union-assert.sh",
    "registry/repos.yml",
    "registry/dependencies.yml",
    "registry/skills.yml",
    *BUILD_DECLARATIONS,
}

SINK_PATTERNS: tuple[tuple[str, re.Pattern[str]], ...] = (
    ("git-push", re.compile(r"\bgit\s+push\b")),
    ("gh-pr-merge", re.compile(r"\bgh\s+pr\s+merge\b")),
    ("gh-release-write", re.compile(r"\bgh\s+release\s+(?:create|upload|edit|delete)\b")),
    ("workflow-dispatch", re.compile(r"\bgh\s+workflow\s+run\b")),
    ("package-publish", re.compile(r"\bdotnet\s+nuget\s+push\b|\bnpm\s+publish\b")),
    ("rest-write", re.compile(r"\bgh\s+api\b[\s\S]{0,240}?(?:-X|--method)\s+(?:POST|PUT|PATCH|DELETE)\b", re.I)),
    ("curl-write", re.compile(r"\bcurl\b[\s\S]{0,240}?(?:-X|--request)\s+(?:POST|PUT|PATCH|DELETE)\b", re.I)),
    ("graphql-mutation", re.compile(r"\bmutation\s*(?:\(|\{)", re.I)),
    ("http-write", re.compile(r"HttpMethod\.(?:Post|Put|Patch|Delete)|\.(?:Post|Put|Patch|Delete)Async\s*\(")),
    ("dynamic-process", re.compile(r"(?:subprocess\.(?:run|Popen|check_call|check_output)|ProcessStartInfo)[\s\S]{0,360}?[\"']gh[\"'][\s\S]{0,80}?[\"']api[\"'][\s\S]{0,240}?(?:PUT|POST|PATCH|DELETE)", re.I)),
)


class CensusError(RuntimeError):
    pass


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_json(path: Path, label: str) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise CensusError(f"{label} is unreadable or malformed: {error}") from error
    if not isinstance(value, dict):
        raise CensusError(f"{label} must be one JSON object")
    return value


def tracked_files(root: Path) -> list[tuple[str, bool]]:
    result = subprocess.run(
        ["git", "-C", str(root), "ls-files", "--stage", "-z"],
        check=False,
        capture_output=True,
    )
    if result.returncode != 0:
        raise CensusError("tracked executable population is unreadable: git ls-files failed")
    rows: list[tuple[str, bool]] = []
    for raw in result.stdout.split(b"\0"):
        if not raw:
            continue
        try:
            metadata, name = raw.decode("utf-8").split("\t", 1)
            mode = metadata.split(" ", 1)[0]
        except (UnicodeDecodeError, ValueError) as error:
            raise CensusError("tracked executable population contains an undecodable row") from error
        rows.append((name, bool(int(mode, 8) & stat.S_IXUSR)))
    return rows


def source_text(path: Path) -> str:
    try:
        text = path.read_text(encoding="utf-8")
    except (OSError, UnicodeError):
        return ""
    # Comment-only prose must not turn an inert recipe explanation into a writer. Quoted commands,
    # heredocs and inline executable text remain visible and therefore require a disposition.
    return "\n".join(
        line for line in text.splitlines()
        if not re.match(r"^\s*(?:#(?!\!)|//|/\*|\*|<!--)", line)
    )


def discover(root: Path) -> tuple[dict[str, set[str]], int]:
    found: dict[str, set[str]] = {}
    population = tracked_files(root)
    for name, executable in population:
        if name in {"scripts/check-v1-writer-census.py", "docs/coordination/v1-writer-census.json"}:
            continue
        if not (executable or name.startswith(POPULATION_PREFIXES) or name in BUILD_DECLARATIONS):
            continue
        # The compiled command contract is the exhaustive entry-point authority for Coord. Internal
        # helpers and transports are not independently callable roots. Registries are declarations,
        # while their writer automations are separately scanned scripts/workflows.
        if name.startswith("src/FS.GG.Coord") or name.startswith("registry/"):
            text = ""
        else:
            text = source_text(root / name)
        kinds = {kind for kind, pattern in SINK_PATTERNS if pattern.search(text)}
        if kinds or name in MANDATORY_SOURCES:
            found[name] = kinds
    return found, sum(1 for _, executable in population if executable)


def validate_source_rows(root: Path, census: dict[str, Any]) -> tuple[int, int]:
    rows = census.get("sources")
    if not isinstance(rows, list):
        raise CensusError("census.sources must be an array")
    by_path: dict[str, dict[str, Any]] = {}
    for index, row in enumerate(rows):
        if not isinstance(row, dict):
            raise CensusError(f"census.sources[{index}] must be an object")
        path = row.get("path")
        disposition = row.get("disposition")
        if not isinstance(path, str) or not path or path in by_path:
            raise CensusError(f"census source path is missing or duplicated at index {index}")
        if disposition not in ALL_DISPOSITIONS:
            raise CensusError(f"census source {path} has unknown disposition {disposition!r}")
        expected_kinds = row.get("sinkKinds")
        if not isinstance(expected_kinds, list) or any(not isinstance(kind, str) for kind in expected_kinds):
            raise CensusError(f"census source {path} has malformed sinkKinds")
        if expected_kinds != sorted(set(expected_kinds)):
            raise CensusError(f"census source {path} sinkKinds must be unique and sorted")
        digest = row.get("sha256")
        if not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise CensusError(f"census source {path} has malformed sha256")
        by_path[path] = row

    discovered, executable_count = discover(root)
    missing = sorted(set(discovered) - set(by_path))
    stale = sorted(set(by_path) - set(discovered))
    if missing:
        raise CensusError("unknown or unresolved writer source(s): " + ", ".join(missing))
    if stale:
        raise CensusError("census names source(s) outside the current executable population: " + ", ".join(stale))

    for path, kinds in sorted(discovered.items()):
        row = by_path[path]
        actual_kinds = sorted(kinds)
        if row["sinkKinds"] != actual_kinds:
            raise CensusError(
                f"census source {path} sink discovery changed: expected {row['sinkKinds']}, got {actual_kinds}"
            )
        if kinds and row["disposition"] not in WRITER_DISPOSITIONS | {"typed-command-catalogue"}:
            justification = row.get("nonWriterJustification")
            if not isinstance(justification, str) or len(justification.strip()) < 12:
                raise CensusError(
                    f"census source {path} contains {actual_kinds} but its non-writer disposition lacks a specific justification"
                )
        actual_digest = sha256(root / path)
        if row["sha256"] != actual_digest:
            raise CensusError(f"census source identity changed for {path}: expected {row['sha256']}, got {actual_digest}")

    direct = by_path.get("tools/routine-delivery.py", {})
    if direct.get("disposition") != "remote-writer" or "dynamic-process" not in direct.get("sinkKinds", []):
        raise CensusError("tools/routine-delivery.py direct REST PUT merge must remain an explicit remote writer")
    for path in (
        "src/FS.GG.Coord.Cli/TelemetryApplication.fs",
        "src/FS.GG.Coord.Cli/TelemetryRuntimeApplication.fs",
        "src/FS.GG.Coord.Cli/TelemetryStoreApplication.fs",
    ):
        if by_path.get(path, {}).get("disposition") != "local-only":
            raise CensusError(f"{path} must retain its explicit local-only telemetry disposition")
    mandatory_dispositions = {
        ".github/workflows/coord-engine.yml": "build-only",
        ".github/workflows/dispatch-sender.yml": "conditional-remote-writer",
        ".github/workflows/fsgg-dispatch-broker.yml": "conditional-remote-writer",
        ".github/workflows/github-substrate-v2-authority-qualification.yml": "protected-admin-writer",
        ".github/workflows/release-coord-engine.yml": "publish-writer",
        "scripts/NewSddWorkspace/Program.fs": "protected-admin-writer",
        "registry/dependencies.yml": "declaration-only",
        "registry/repos.yml": "declaration-only",
        "registry/skills.yml": "declaration-only",
        "src/FS.GG.Coord.Cli.Kernel/Options.fs": "typed-command-catalogue",
        "src/FS.GG.Coord.Cli/Program.fs": "typed-command-catalogue",
        "src/FS.GG.Coord.GitHub/Transport.fs": "typed-command-catalogue",
    }
    for path, disposition in mandatory_dispositions.items():
        if by_path.get(path, {}).get("disposition") != disposition:
            raise CensusError(f"{path} must remain classified {disposition}")
    return len(discovered), executable_count


def command_rows(census: dict[str, Any]) -> dict[str, str]:
    rows = census.get("commandRoots")
    if not isinstance(rows, list):
        raise CensusError("census.commandRoots must be an array")
    result: dict[str, str] = {}
    for index, row in enumerate(rows):
        if not isinstance(row, dict) or not isinstance(row.get("name"), str) or row.get("writes") not in {"always", "conditional", "never"}:
            raise CensusError(f"census.commandRoots[{index}] is malformed")
        name = row["name"]
        if name in result:
            raise CensusError(f"duplicate command root {name!r}")
        result[name] = row["writes"]
    return result


def read_contract(args: argparse.Namespace) -> dict[str, Any] | None:
    if args.structural:
        return None
    if args.contract:
        return load_json(args.contract, "command contract")
    if not args.candidate:
        raise CensusError("typed validation needs --candidate or --contract (use --structural only for the cheap pre-build gate)")
    result = subprocess.run([str(args.candidate), "command-contract"], check=False, capture_output=True, text=True)
    if result.returncode != 0 or not result.stdout.strip():
        detail = result.stderr.strip() or f"exit {result.returncode} with empty output"
        raise CensusError(f"candidate-built command contract is unavailable: {detail}")
    try:
        value = json.loads(result.stdout)
    except json.JSONDecodeError as error:
        raise CensusError(f"candidate-built command contract is malformed: {error}") from error
    if not isinstance(value, dict):
        raise CensusError("candidate-built command contract must be one JSON object")
    return value


def validate_contract(census_rows: dict[str, str], contract: dict[str, Any]) -> None:
    if contract.get("schema") != CONTRACT_SCHEMA or not isinstance(contract.get("commands"), list):
        raise CensusError(f"candidate-built command contract must use {CONTRACT_SCHEMA}")
    actual: dict[str, str] = {}
    for row in contract["commands"]:
        if not isinstance(row, dict) or not isinstance(row.get("name"), str) or row.get("writes") not in {"always", "conditional", "never"}:
            raise CensusError("candidate-built command contract contains an unresolved writer classification")
        if row["name"] in actual:
            raise CensusError(f"candidate-built command contract duplicates {row['name']!r}")
        actual[row["name"]] = row["writes"]
    if actual != census_rows:
        omitted = sorted(set(actual) - set(census_rows))
        removed = sorted(set(census_rows) - set(actual))
        changed = sorted(name for name in actual.keys() & census_rows.keys() if actual[name] != census_rows[name])
        raise CensusError(
            "candidate-built command roots disagree with census"
            f"; omitted/new={omitted}; absent={removed}; misclassified={changed}"
        )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--census", type=Path)
    source = parser.add_mutually_exclusive_group()
    source.add_argument("--candidate", type=Path)
    source.add_argument("--contract", type=Path)
    source.add_argument("--structural", action="store_true")
    args = parser.parse_args()
    root = args.root.resolve()
    census_path = args.census or root / "docs/coordination/v1-writer-census.json"
    try:
        census = load_json(census_path, "writer census")
        if census.get("schema") != SCHEMA:
            raise CensusError(f"writer census must use {SCHEMA}")
        commands = command_rows(census)
        source_count, executable_count = validate_source_rows(root, census)
        contract = read_contract(args)
        if contract is not None:
            validate_contract(commands, contract)
        counts = {kind: list(commands.values()).count(kind) for kind in ("always", "conditional", "never")}
        mode = "structural" if contract is None else "candidate-built"
        print(
            f"v1-writer-census: PASS ({mode}; {len(commands)} command roots: "
            f"{counts['always']} always, {counts['conditional']} conditional, {counts['never']} never; "
            f"{source_count} source identities; {executable_count} tracked executables)"
        )
        return 0
    except CensusError as error:
        print(f"v1-writer-census: REFUSED: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
