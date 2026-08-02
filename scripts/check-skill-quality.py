#!/usr/bin/env python3
"""Unified semantic quality gate for the synchronized FS-GG skill catalog."""

from __future__ import annotations

import argparse
import importlib.machinery
import importlib.util
import json
import os
import re
import sys
from pathlib import Path

import yaml

ROOTS = (".claude/skills", ".agents/skills")
# The single statement of the contract schema this gate understands. Stated ONCE on purpose
# (.github#1574): two literals let two readers of the same contract disagree about whether they
# are looking at a supported document, and one of them then declines to check anything.
CONTRACT_SCHEMA = "fsgg.coord.commands/1"
EXPLICIT_ONLY = {"cut-nuget-release", "drive-board", "p-add", "padd-item", "work-board", "work-roadmap"}
LINK = re.compile(r"\[[^\]]+\]\(([^)]+)\)")
INVOCATION = re.compile(r"(?:scripts/)?fsgg-coord(?![\w-])\s+(.+)")
INLINE = re.compile(r"`([^`]+)`")
FLAG = re.compile(r"(?<![\w-])--[a-z][a-z0-9-]*")


def fail(errors: list[str], message: str) -> None:
    errors.append(message)


def load_generator(root: Path):
    path = root / "scripts/generate-driver-manifest"
    loader = importlib.machinery.SourceFileLoader("skill_manifest_generator", str(path))
    spec = importlib.util.spec_from_loader(loader.name, loader)
    if spec is None:
        raise RuntimeError(f"cannot import {path}")
    module = importlib.util.module_from_spec(spec)
    loader.exec_module(module)
    return module


def validate_openai_metadata(root: Path, names: set[str], errors: list[str]) -> None:
    for name in sorted(names):
        path = root / ROOTS[0] / name / "agents/openai.yaml"
        if not path.is_file():
            fail(errors, f"{name}: missing agents/openai.yaml")
            continue
        try:
            doc = yaml.safe_load(path.read_text(encoding="utf-8"))
        except yaml.YAMLError as exc:
            fail(errors, f"{path.relative_to(root)}: invalid YAML: {exc}")
            continue
        if not isinstance(doc, dict):
            fail(errors, f"{path.relative_to(root)}: metadata must be a mapping")
            continue
        unknown = set(doc) - {"interface", "dependencies", "policy"}
        if unknown:
            fail(errors, f"{name}: unknown openai.yaml keys: {sorted(unknown)}")
        interface = doc.get("interface")
        if not isinstance(interface, dict):
            fail(errors, f"{name}: interface must be a mapping")
            continue
        for key in ("display_name", "short_description", "default_prompt"):
            if not isinstance(interface.get(key), str) or not interface[key].strip():
                fail(errors, f"{name}: interface.{key} must be a non-empty string")
        short = interface.get("short_description", "")
        if isinstance(short, str) and not 25 <= len(short) <= 64:
            fail(errors, f"{name}: short_description must be 25..64 characters")
        prompt = interface.get("default_prompt", "")
        if isinstance(prompt, str) and f"${name}" not in prompt:
            fail(errors, f"{name}: default_prompt must explicitly select ${name}")
        policy = doc.get("policy", {})
        if not isinstance(policy, dict):
            fail(errors, f"{name}: policy must be a mapping")
            policy = {}
        actual = policy.get("allow_implicit_invocation", True)
        expected = name not in EXPLICIT_ONLY
        if actual is not expected:
            fail(errors, f"{name}: allow_implicit_invocation must be {str(expected).lower()}")
        dependencies = doc.get("dependencies")
        if dependencies is not None:
            if not isinstance(dependencies, dict) or not isinstance(dependencies.get("tools"), list):
                fail(errors, f"{name}: dependencies.tools must be a list")
            else:
                for index, tool in enumerate(dependencies["tools"]):
                    if not isinstance(tool, dict) or tool.get("type") != "mcp":
                        fail(errors, f"{name}: dependencies.tools[{index}] must declare type: mcp")
                    for key in ("value", "description"):
                        if not isinstance(tool.get(key), str) or not tool[key].strip():
                            fail(errors, f"{name}: dependencies.tools[{index}].{key} is required")


def validate_links(root: Path, names: set[str], errors: list[str]) -> None:
    for name in sorted(names):
        directory = root / ROOTS[0] / name
        for source in directory.rglob("*.md"):
            text = source.read_text(encoding="utf-8")
            for raw in LINK.findall(text):
                target = raw.split("#", 1)[0].strip()
                if (
                    not target
                    or "…" in target
                    or "://" in target
                    or target.startswith(("mailto:", "/"))
                ):
                    continue
                resolved = (source.parent / target).resolve()
                external = f"{os.sep}fs-gg-sdd-lifecycle{os.sep}" in str(resolved)
                if not external and not resolved.exists():
                    fail(errors, f"{source.relative_to(root)}: broken relative link {raw!r}")


def code_segments(text: str):
    fenced = False
    continued = ""
    continued_at = 0
    for number, raw in enumerate(text.splitlines(), 1):
        line = raw.lstrip()
        if line.startswith(">"):
            line = line[1:].lstrip()
        if line.startswith("```"):
            if continued:
                yield continued_at, continued
                continued = ""
            fenced = not fenced
            continue
        if fenced and not line.startswith("#"):
            line = line[2:] if line.startswith("$ ") else line
            if continued:
                continued += " " + line
            else:
                continued, continued_at = line, number
            if continued.rstrip().endswith("\\"):
                continued = continued.rstrip()[:-1]
            else:
                yield continued_at, continued
                continued = ""
        if not fenced:
            for match in INLINE.finditer(line):
                yield number, match.group(1)
    if continued:
        yield continued_at, continued


def validate_invocations(root: Path, contract_path: Path, errors: list[str]) -> None:
    try:
        contract = json.loads(contract_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail(errors, f"command contract unreadable: {exc}")
        return
    if contract.get("schema") != CONTRACT_SCHEMA:
        fail(errors, f"command contract has unsupported schema {contract.get('schema')!r}")
        return
    commands = {
        row["name"]: set(row["flags"])
        for row in contract.get("commands", [])
        if isinstance(row, dict) and isinstance(row.get("name"), str) and isinstance(row.get("flags"), list)
    }
    if len(commands) < 35:
        fail(errors, f"command contract is implausibly small ({len(commands)} commands)")
        return

    files: list[Path] = []
    for runtime in ROOTS:
        skill_root = root / runtime
        if not skill_root.is_dir():
            fail(errors, f"missing invocation corpus root {runtime}")
        else:
            files.extend(skill_root.rglob("*.md"))
    docs = root / "docs/coordination"
    if not docs.is_dir():
        fail(errors, "missing invocation corpus root docs/coordination")
    else:
        files.extend(docs.rglob("*.md"))

    found = 0
    for source in sorted(files):
        for line, segment in code_segments(source.read_text(encoding="utf-8")):
            for hit in INVOCATION.finditer(segment):
                command = hit.group(1)
                command = re.split(r"\s(?:#(?!\d)|&&|\|\||[;|>])", command, maxsplit=1)[0]
                name_match = re.match(r"(room\s+open|[a-z][a-z0-9-]*)", command)
                if name_match is None:
                    continue
                name = name_match.group(1)
                allowed = commands.get(name)
                if allowed is None:
                    fail(errors, f"{source.relative_to(root)}:{line}: unknown documented command {name!r}")
                    continue
                found += 1
                for token in FLAG.findall(command):
                    if token not in allowed:
                        fail(
                            errors,
                            f"{source.relative_to(root)}:{line}: {token} is not a flag of {name} "
                            f"(engine contract: {sorted(allowed)})",
                        )
    if found < 50:
        fail(errors, f"documented-invocation audit examined only {found} commands; expected at least 50")


def validate_semantics(root: Path, contract_path: Path, errors: list[str]) -> None:
    try:
        contract = json.loads(contract_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        # validate_invocations already emits the actionable contract diagnostic, and this path is
        # reached only once it has ALREADY failed. Distinct from the schema check below: that one
        # must speak for itself, because a supported-but-newer schema is a state in which the
        # sibling's error is repairable by editing one literal while this gate stays disarmed.
        return
    if contract.get("schema") != CONTRACT_SCHEMA:
        fail(
            errors,
            "semantic polarity gate cannot run: command contract schema "
            f"{contract.get('schema')!r} is not {CONTRACT_SCHEMA}; the --paths/--apply/--dry-run "
            "polarity assertions were NOT made — port them before moving the schema id",
        )
        return
    commands = {row["name"]: set(row["flags"]) for row in contract["commands"]}
    required_flags = {
        "widen": {"--paths"},
        "set-paths": {"--paths"},
        "reap": {"--apply"},
        "reconcile": {"--apply"},
        "flush": {"--dry-run"},
    }
    for command, flags in required_flags.items():
        if not flags <= commands.get(command, set()):
            fail(errors, f"{command}: engine contract lost semantic flag(s) {sorted(flags)}")
    forbidden_flags = {
        "widen": {"--apply"},
        "set-paths": {"--apply"},
        "lint": {"--apply"},
        "reap": {"--dry-run"},
        "flush": {"--apply"},
    }
    for command, forbidden in forbidden_flags.items():
        overlap = commands.get(command, set()) & forbidden
        if overlap:
            fail(errors, f"{command}: dangerous polarity collision: {sorted(overlap)}")

    lane = (root / ROOTS[0] / "lane-steward/SKILL.md").read_text(encoding="utf-8")
    if "scripts/fsgg-coord set-paths <issue> --paths" not in lane or "additive expansion only" not in lane:
        fail(errors, "lane-steward: narrowing/additive verb distinction is missing")
    if 'fsgg-coord widen <issue> --paths "<the above>"' in lane:
        fail(errors, "lane-steward: additive widen is prescribed for narrowing")

    publishing = (root / ROOTS[0] / "publishing-and-deployment/SKILL.md").read_text(encoding="utf-8")
    for stale in ("`-preview` always", "Public nuget.org (decided, wiring pending", "`coherent: false` until wired"):
        if stale in publishing:
            fail(errors, f"publishing-and-deployment: stale current-truth claim returned: {stale}")
    for required in (
        "stable channel",
        "There is no local-feed fallback",
        "byte-identical `.nupkg` to nuget.org",
        "Historical rollout record",
    ):
        if required not in publishing:
            fail(errors, f"publishing-and-deployment: missing current-truth statement: {required}")

    drive_root = root / ROOTS[0] / "drive-board"
    drive = (drive_root / "SKILL.md").read_text(encoding="utf-8")
    host_loop = (drive_root / "references/host-loop.md").read_text(encoding="utf-8")
    triage = (drive_root / "references/backlog-triage.md").read_text(encoding="utf-8")
    if "backlog-triage" not in drive:
        fail(errors, "drive-board: triggered body does not route through backlog triage")
    planning_order = ("four-part `check-board` result", "backlog-triage", "batch")
    positions = [host_loop.find(term) for term in planning_order]
    if any(position < 0 for position in positions) or positions != sorted(positions):
        fail(errors, "drive-board: host loop must order check-board result, backlog triage, then batch")
    for required in (
        "Promote to Ready",
        "Retain in Backlog",
        "Set Blocked",
        "Await human judgement",
        "follow-up filed by the preceding wave",
        "An empty Ready batch is not completion",
        "never use",
        "`--include-backlog`",
    ):
        if required not in triage:
            fail(errors, f"drive-board: backlog planning contract lost {required!r}")

    p_add = (root / ROOTS[0] / "p-add/SKILL.md").read_text(encoding="utf-8")
    p_add_order = ("check-board", "ready --status Backlog", "fsgg-coord add")
    p_add_positions = [p_add.find(term) for term in p_add_order]
    if any(position < 0 for position in p_add_positions) or p_add_positions != sorted(p_add_positions):
        fail(errors, "p-add: must reconcile, triage backlog, then add the issue in that order")
    for required in (
        "reconcile --json",
        "lint --json",
        "issues <repo> --state open --refresh",
        "reuse an existing matching issue",
        "Paths:",
        "pendingBoardWrites: 0",
        "`<item> — <new status>: <one-line summary>`",
        "`Active: <item> — <current activity/gate>; ...`",
        "does not implement the item",
    ):
        if required not in p_add:
            fail(errors, f"p-add: filing contract lost {required!r}")

    padd_item = (root / ROOTS[0] / "padd-item/SKILL.md").read_text(encoding="utf-8")
    padd_item_order = ("Prove workspace wiring", "reconcile --repo", "ready --repo", "fsgg-coord add")
    padd_item_positions = [padd_item.find(term) for term in padd_item_order]
    if any(position < 0 for position in padd_item_positions) or padd_item_positions != sorted(padd_item_positions):
        fail(errors, "padd-item: must prove wiring, reconcile, triage backlog, then add in that order")
    for required in (
        "FSGG_COORD_OWNER_TYPE",
        "Never silently fall back",
        "issues <this-repo> --state open --refresh",
        "Reuse an existing semantic match",
        "cross-repo-coordination",
        "Paths:",
        "pendingBoardWrites: 0",
        "`<item> — <new status>: <one-line summary>`",
        "`Active: <item> — <current activity/gate>; ...`",
        "do not implement",
    ):
        if required not in padd_item:
            fail(errors, f"padd-item: workspace filing contract lost {required!r}")

    work_root = root / ROOTS[0] / "work-board"
    work = (work_root / "SKILL.md").read_text(encoding="utf-8")
    work_host_loop = (work_root / "references/host-loop.md").read_text(encoding="utf-8")
    work_triage = (work_root / "references/backlog-triage.md").read_text(encoding="utf-8")
    if "backlog-triage" not in work:
        fail(errors, "work-board: triggered body does not route through backlog triage")
    work_order = ("four-part `check-board` result", "backlog-triage", "batch")
    work_positions = [work_host_loop.find(term) for term in work_order]
    if any(position < 0 for position in work_positions) or work_positions != sorted(work_positions):
        fail(errors, "work-board: host loop must order check-board result, backlog triage, then batch")
    for required in (
        "without mutating the board",
        "Promote to Ready",
        "Retain in Backlog",
        "Set Blocked",
        "Await human judgement",
        "Promotion changes eligibility, not assignment",
        "touch-set-disjoint",
        "simple-versus-complex SDD lifecycle branch",
        "schema-v2 development-feedback report",
        "follow-up filed by the preceding wave",
        "An empty Ready batch is not completion",
        "Do not spin",
        "`--include-backlog`",
    ):
        if required not in work_triage:
            fail(errors, f"work-board: backlog planning contract lost {required!r}")

    # .github#2088: two concurrent waves, three implementer slots each, two RESERVED review slots,
    # a testable consolidation threshold, and an explicit fleet-wide EX_RATE stop. Stated once per
    # driver family in host-loop.md; #916 is why a hand-restated copy elsewhere is the failure mode,
    # not a convenience — so this also asserts the two host-loop copies say it identically and that
    # the routed variants inherit rather than restate it.
    wave_model_required = (
        "Run two waves in parallel, not sequentially",
        "six implementer slots total across both waves",
        "RESERVED, not advisory",
        "filling all eight slots with",
        "a contract violation, not an efficiency gain",
        "Three or fewer consolidates",
        "Four or more runs two full waves as designed",
        "fresh reconcile/triage, not a re-slice of the",
        "fleet-wide stop for BOTH waves",
        "flush --dry-run` before resuming either",
    )
    for driver_name, contract_text in (("drive-board", host_loop), ("work-board", work_host_loop)):
        for required in wave_model_required:
            if required not in contract_text:
                fail(errors, f"{driver_name}: host-loop lost two-wave contract statement {required!r}")

    def _wave_model_block(text: str) -> str:
        marker = "**Two concurrent waves, a fixed eight-slot cap.**"
        sentinel = "wave.\n"
        start = text.find(marker)
        if start < 0:
            return ""
        end = text.find(sentinel, start)
        if end < 0:
            return ""
        return text[start:end + len(sentinel)]

    drive_wave_block = _wave_model_block(host_loop)
    work_wave_block = _wave_model_block(work_host_loop)
    if not drive_wave_block or not work_wave_block:
        fail(errors, "two-wave contract: could not locate the wave-model block in one or both host-loop copies")
    elif drive_wave_block != work_wave_block:
        fail(
            errors,
            "two-wave contract: drive-board and work-board host-loop copies state the wave model "
            "differently — the #916 near-duplicate-drift failure this gate exists to catch",
        )

    variant_wave_terms = ("implementer slot", "review slot", "consolidat", "eight-slot", "EX_RATE")
    for variant in ("drive-board-best", "drive-board-normal", "work-board-best", "work-board-normal"):
        variant_body = (root / ROOTS[0] / variant / "SKILL.md").read_text(encoding="utf-8")
        for term in variant_wave_terms:
            if term.lower() in variant_body.lower():
                fail(
                    errors,
                    f"{variant}: restates the two-wave contract ({term!r}) instead of inheriting it "
                    "from the canonical driver",
                )

    feedback_contracts = {
        "work-board": (
            "item-<issue-number>-<slug>",
            "validate-checkpoints feedback/checkpoints/<cycle-id>.jsonl",
            "no cycle feedback artifacts is incomplete",
        ),
        "work-roadmap": (
            "roadmap-<roadmap-slug>-m<milestone>-<slug>",
            "validate-checkpoints feedback/checkpoints/<cycle-id>.jsonl",
            "final roll-up",
        ),
    }
    for driver, required in feedback_contracts.items():
        driver_root = root / ROOTS[0] / driver
        body = (driver_root / "SKILL.md").read_text(encoding="utf-8")
        contract = (driver_root / "references/feedback-contract.md").read_text(encoding="utf-8")
        gate = driver_root / "scripts/validate-feedback-state.py"
        if "feedback-contract" not in body:
            fail(errors, f"{driver}: triggered body does not route through the feedback contract")
        if not gate.is_file():
            fail(errors, f"{driver}: feedback-state validator is missing")
        for statement in required:
            if statement not in contract:
                fail(errors, f"{driver}: feedback completion contract lost {statement!r}")
        for statement in (
            "fs-gg-feedback-report",
            "schema-v2 report",
            "--audit feedback/audits/<report-stem>.audit.json",
            "unbound-audit",
            "Do not create a fake defect or positive pattern",
        ):
            if statement not in contract:
                fail(errors, f"{driver}: feedback completion contract lost {statement!r}")


def validate_forward_corpus(root: Path, names: set[str], errors: list[str]) -> None:
    path = root / "tests/skill-quality/forward-triggers.json"
    try:
        doc = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail(errors, f"{path.relative_to(root)}: cannot read: {exc}")
        return
    cases = doc.get("cases", []) if isinstance(doc, dict) else []
    required = {"coordination-diagnosis", "one-worker-loop", "parallel-fanout", "release-train", "markdown-roadmap", "spectre-ci"}
    actual = {case.get("class") for case in cases if isinstance(case, dict)}
    if actual != required:
        fail(errors, f"forward trigger classes differ; missing={sorted(required-actual)}, extra={sorted(actual-required)}")
    for case in cases:
        if not isinstance(case, dict):
            fail(errors, "forward trigger case must be an object")
            continue
        prompt, expected, selector = case.get("prompt"), case.get("expected"), case.get("selector")
        if not isinstance(prompt, str) or not prompt.strip():
            fail(errors, f"{case.get('class')}: prompt must be non-empty")
            continue
        if expected is not None and expected not in names:
            fail(errors, f"{case.get('class')}: unknown expected skill {expected!r}")
        if isinstance(expected, str) and (
            expected.lower() in prompt.lower() or expected.replace("-", " ").lower() in prompt.lower()
        ):
            fail(errors, f"{case.get('class')}: prompt leaks expected skill name {expected!r}")
        if expected in EXPLICIT_ONLY:
            if selector != f"${expected}":
                fail(errors, f"{case.get('class')}: explicit-only skill {expected} needs selector ${expected}")
        elif selector is not None:
            fail(errors, f"{case.get('class')}: implicitly routed skill must not carry a selector")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".")
    parser.add_argument("--contract", required=True)
    args = parser.parse_args()
    root = Path(args.root).resolve()
    errors: list[str] = []

    generator = load_generator(root)
    report, catalog_errors = generator.validate_catalog(str(root))
    errors.extend(catalog_errors)
    names = {path.name for path in (root / ROOTS[0]).iterdir() if path.is_dir()}
    descriptions = []
    for name in sorted(names):
        metadata, _ = generator.frontmatter(str(root / ROOTS[0] / name / "SKILL.md"))
        descriptions.append(metadata.get("description"))
    duplicates = sorted({d for d in descriptions if d and descriptions.count(d) > 1})
    if duplicates:
        fail(errors, f"duplicate skill descriptions: {duplicates}")

    contract_path = Path(args.contract).resolve()
    validate_openai_metadata(root, names, errors)
    validate_links(root, names, errors)
    validate_invocations(root, contract_path, errors)
    validate_semantics(root, contract_path, errors)
    validate_forward_corpus(root, names, errors)

    if errors:
        for error in errors:
            print(f"::error::skill-quality: {error}", file=sys.stderr)
        return 1
    print(
        "skill-quality: OK — "
        f"{len(names)} skills; metadata, mirrors, links, modes, budgets, commands, semantics, and triggers verified"
    )
    print(json.dumps(report["codexEffective"], sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
