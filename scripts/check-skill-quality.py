#!/usr/bin/env python3
"""Unified semantic quality gate for the synchronized FS-GG skill catalog."""

from __future__ import annotations

import argparse
import importlib.machinery
import importlib.util
import json
import os
import re
import shlex
import subprocess
import sys
from pathlib import Path


ROOTS = (".claude/skills", ".agents/skills")
# The single statement of the contract schema this gate understands. Stated ONCE on purpose
# (.github#1574): two literals let two readers of the same contract disagree about whether they
# are looking at a supported document, and one of them then declines to check anything.
CONTRACT_SCHEMA = "fsgg.coord.commands/1"
EXPLICIT_ONLY = {"cut-nuget-release", "drive-board", "p-add", "padd-item", "work-roadmap", "work-programme"}
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
    import yaml
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


def kit_skill_ids(root: Path, errors: list[str]) -> set[str]:
    """Every `kind: skill` id in `registry/repos.yml`'s `kit:` block — the directories a
    coordination-kit receiver actually materializes (repos.yml's own `kit:` comment;
    `scripts/coordination-sync`). Read, never restated: a new kit skill row is covered the day it
    lands, with no second list to keep in step (mirrors `check-kit-published-coherence.py`'s
    `kit_sources`, which answers the sibling "every kit source" question this narrows to skills).
    """
    path = root / "registry/repos.yml"
    import yaml
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as exc:
        fail(errors, f"cannot read {path}: {exc}")
        return set()
    try:
        roster = yaml.safe_load(text)
    except yaml.YAMLError as exc:
        fail(errors, f"{path} is not parsable as YAML: {exc}")
        return set()
    if not isinstance(roster, dict):
        fail(errors, f"{path} is not a YAML mapping")
        return set()
    kit = roster.get("kit")
    if not isinstance(kit, list) or not kit:
        fail(errors, f"{path}: `kit:` is missing or not a non-empty list")
        return set()
    ids: set[str] = set()
    for index, row in enumerate(kit):
        if not isinstance(row, dict):
            fail(errors, f"{path}: kit[{index}] is not a mapping")
            continue
        if row.get("kind") != "skill":
            continue
        source = row.get("source")
        if not isinstance(source, str) or not source.strip():
            fail(errors, f"{path}: kit[{index}] is kind: skill with no usable source")
            continue
        ids.add(Path(source.strip()).name)
    return ids


def validate_receiver_safe_links(root: Path, errors: list[str]) -> None:
    """A coordination-kit receiver materializes ONLY the `kit:` skill directories
    `registry/repos.yml` declares (repos.yml's own `kit:` comment; `scripts/coordination-sync`) —
    never `docs/`, never a non-kit skill. `validate_links` above only proves a relative link
    resolves somewhere in THIS checkout, which is exactly the gap .github#2343 fell through: a link
    climbing out to `docs/coordination/...` or `docs/adr/...` resolves fine here and dangles in
    every receiver, because `docs/` is never part of the kit transport. This proves the STRONGER,
    receiver-true fact instead: every relative link inside a kit skill's markdown resolves inside
    the union of kit-delivered skill directories under the SAME runtime root the link is read from
    (a link to a sibling kit skill is fine — every kit skill ships together — a link to anything
    else is not).
    """
    ids = kit_skill_ids(root, errors)
    if not ids:
        return
    for runtime in ROOTS:
        skill_root = root / runtime
        allowed_roots = [(skill_root / kit_id).resolve() for kit_id in sorted(ids)]
        for kit_id in sorted(ids):
            directory = skill_root / kit_id
            if not directory.is_dir():
                continue  # validate_openai_metadata/the manifest gate already report absence
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
                    if not any(
                        resolved == allowed or allowed in resolved.parents
                        for allowed in allowed_roots
                    ):
                        fail(
                            errors,
                            f"{source.relative_to(root)}: receiver-unsafe relative link {raw!r} "
                            f"resolves outside every kit-delivered {runtime}/<id> directory — a "
                            "coordination-kit receiver never materializes that target, so this "
                            "link would dangle there (rewrite to an absolute URL, or point at a "
                            "sibling kit skill)",
                        )


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
    # Telemetry is the local deterministic command family composed ahead of the
    # network-capable Options/Client dispatcher, so it is intentionally absent
    # from that dispatcher's command-contract projection. Keep its documented
    # option surface explicit here so packaged lifecycle recipes receive the
    # same unknown-command/unknown-flag audit.
    commands["telemetry"] = {
        "--session-file", "--snapshot", "--task", "--turn-id", "--since", "--until",
        "--format", "--append", "--output", "--coord-version", "--sdd-version",
        "--contracts-version", "--run", "--unit", "--comments", "--draft", "--usage",
        "--history-report", "--existing", "--log", "--required-phase", "--require-terminal",
        "--require-reconciled", "--cycle", "--artifact", "--head", "--report", "--audit",
        "--phases", "--checkpoint", "--all-responses",
    }
    # Board V2 is composed in Program.main before the Options dispatcher too.
    # BoardV2Application.parseArguments owns these two operations and this exact
    # flag surface; audit their names as well as their options.
    for operation in ("inspect", "refresh"):
        commands[f"board-v2 {operation}"] = {
            "--binding-file", "--previous-report-file", "--report-file",
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
                name_match = re.match(r"(room\s+open|board-v2\s+[a-z][a-z0-9-]*|[a-z][a-z0-9-]*)", command)
                if name_match is None:
                    continue
                name = name_match.group(1)
                allowed = commands.get(name)
                if allowed is None:
                    fail(errors, f"{source.relative_to(root)}:{line}: unknown documented command {name!r}")
                    continue
                if name.startswith("board-v2 "):
                    # Match the pure syntax owned by parseArguments. Filesystem
                    # existence/new-output custody belongs to the live command.
                    try:
                        arguments = shlex.split(command)[2:]
                    except ValueError as exc:
                        fail(errors, f"{source.relative_to(root)}:{line}: invalid Board V2 arguments: {exc}")
                        continue
                    values: dict[str, str] = {}
                    while arguments:
                        if (len(arguments) < 2 or arguments[0] not in allowed
                                or arguments[0] in values or not arguments[1].strip()
                                or arguments[1].startswith("--")):
                            fail(errors, f"{source.relative_to(root)}:{line}: unknown, duplicate or incomplete Board V2 argument")
                            break
                        values[arguments[0]] = arguments[1]
                        arguments = arguments[2:]
                    else:
                        binding = values.get("--binding-file")
                        report = values.get("--report-file")
                        if (not binding or not report or binding == report
                                or values.get("--previous-report-file") == report):
                            fail(errors, f"{source.relative_to(root)}:{line}: Board V2 requires distinct --binding-file and --report-file paths")
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


def validate_organization_consumer(root: Path, errors: list[str]) -> None:
    """Selected organization callers retain accepted and historical contracts without V1 fallback."""
    for runtime in ROOTS:
        base = root / runtime
        contract = (base / "check-board/references/organization-v2-inspection.md").read_text(encoding="utf-8")
        drive = (base / "drive-board/SKILL.md").read_text(encoding="utf-8")
        host = (base / "drive-board/references/host-loop.md").read_text(encoding="utf-8")
        triage = (base / "drive-board/references/backlog-triage.md").read_text(encoding="utf-8")
        cross = (base / "cross-repo-coordination/SKILL.md").read_text(encoding="utf-8")
        check = (base / "check-board/SKILL.md").read_text(encoding="utf-8")
        obligations = (
            ("check-board scope", check, (
                "Historical organization examples are data only", "No historical organization live opt-in exists",
                "Before any product command, verify the actual product target/binding and configured product repository",
                "Refuse missing or organization-default target facts", "queue-wide replay has no repository filter",
            )),
            ("inspection", contract, (
                "This is .9/.11 consumer source preparation",
                "none grants organization-v4 inspection, dispatch or field-write permission",
                "actual installed executable", "final authenticated binding", "never invent IDs",
                "Only accepted cutover retires manual v3 current-status upkeep",
                "`bindingVersion: 4`", "`schemaVersion: 2`", "`fsgg.coord.board-v2-inspection/2`",
                "--integrator-stdin", "`fsgg.coord.board-v2-integrator-observations/1`",
                "`integrator-reported`", "do not authenticate their author",
                "accountable parent verifies the genuine suppliers",
                "Counts mean remaining capacity after active work",
                "Self exemption requires the exact slice/attempt/operation/PR/head",
                "Recheck determining issue/body/owner/action",
                "beside actual owner execution", "with no V1 fallback",
            )),
            ("driver", drive, ("[$work-board](../work-board/SKILL.md)", "organization", "scope only")),
        )
        for name, text, required in obligations:
            normalized = " ".join(text.split())
            for clause in required:
                if clause not in normalized:
                    fail(errors, f"organization successor: {name} contract lost {clause!r}")
        # Retired machine authority must not reappear, even inside the historical host text.
        if "fsgg:wave-model:v1" in host or "<!-- BEGIN GENERATED: fsgg-protocol:wave-policy -->" in host:
            fail(errors, "organization successor: retired generated wave authority returned")
        current_text = "\n".join((drive, cross, host.partition("## Historical host mechanics")[0],
                                  triage.partition("## Historical triage mechanics")[0], contract,
                                  check.partition("## Separately qualified product scope")[0]))
        if (re.search(r"(?:scripts/)?fsgg-coord(?![\w-])\s+(?:intake|claim|take|batch|driver|reconcile|set-field|done|flush)(?:\s|$)", current_text)
                or re.search(r"(?:scripts/)?fsgg-coord-report(?![\w-])(?:\s|$)", current_text)):
            fail(errors, "organization successor: V1 operation prescribed in current consumer guidance")
        for _, segment in code_segments(check):
            command = INVOCATION.search(segment)
            if not command:
                continue
            tokens = shlex.split(command[1])
            if tokens and tokens[0] in ("reconcile", "lint"):
                if "--repo" not in tokens or tokens[tokens.index("--repo") + 1:tokens.index("--repo") + 2] != ["<configured-product-repo>"]:
                    fail(errors, "organization successor: check-board recipe lacks explicit qualified product repository")
            if tokens and tokens[0] == "flush":
                fail(errors, "organization successor: check-board cannot authorize queue-wide replay")


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

    validate_organization_consumer(root, errors)

    validate_lifecycle_routes(root, errors)

    validate_work_board_coordinator(root, errors)


def validate_lifecycle_routes(root: Path, errors: list[str]) -> None:
    """Validate the actual authored routing closure, not historical helper vocabulary.

    Required links must resolve from each carrier. Current organization instructions cannot execute
    retired V1 writes. Explicit heavyweight/product references are excluded from the active closure:
    keeping an archived contract must neither revive it nor cause a blanket compatibility deletion.
    """
    routes = {
        "work-board/SKILL.md": ("work-board/references/lifecycle-contract.md", "work-board/references/intake.md", "pnext-item/SKILL.md", "work-roadmap/SKILL.md"),
        "p-add/SKILL.md": ("work-board/references/intake.md",),
        "padd-item/SKILL.md": ("work-board/references/intake.md", "padd-item/references/legacy-product-intake.md"),
        "pnext-item/SKILL.md": ("work-board/SKILL.md",),
        "work-roadmap/SKILL.md": ("pnext-item/SKILL.md",),
        "board-analyst/SKILL.md": ("work-board/references/intake.md",),
        "lane-steward/SKILL.md": ("work-board/SKILL.md",),
        "intra-repo-parallel-work/SKILL.md": ("pnext-item/SKILL.md", "work-board/SKILL.md"),
        "drive-board/references/host-loop.md": ("work-board/references/host-loop.md",),
        "pnext-item/references/findings-and-filing.md": ("work-board/references/intake.md",),
    }
    for runtime in ROOTS:
        base = root / runtime
        for carrier, targets in routes.items():
            path = base / carrier
            if not path.is_file():
                fail(errors, f"lifecycle route: missing carrier {path.relative_to(root)}")
                continue
            text = path.read_text(encoding="utf-8")
            links = set()
            canonical = "https://github.com/FS-GG/.github/blob/main/.agents/skills/"
            for link in LINK.findall(text):
                link = link.split("#")[0]
                if link.startswith(canonical):
                    links.add((base / link.removeprefix(canonical)).resolve())
                elif not link.startswith(("http:", "https:", "#")):
                    links.add((path.parent / link).resolve())
            for target in targets:
                if (base / target).resolve() not in links or not (base / target).is_file():
                    fail(errors, f"lifecycle route: {carrier} must reach {target}")
            for _, segment in code_segments(text):
                if re.search(r"(?:scripts/)?fsgg-coord\s+(?:intake|whoami|heartbeat|claim|take|batch|set-paths|widen|done|flush|add|reconcile)\b", segment):
                    fail(errors, f"lifecycle route: {carrier} prescribes retired V1 operation")
        for carrier, clauses in {
            "p-add/SKILL.md": ("reuse an existing matching issue", "does not implement"),
            "padd-item/SKILL.md": ("Never silently fall back", "do not implement"),
            "pnext-item/references/findings-and-filing.md": ("The current owner may dispose and file the finding directly.",),
            "pnext-item/SKILL.md": ("including publication, installed, participant and recovery obligations", "close only its fulfilled promise", "continues clearing"),
            "work-board/SKILL.md": ("complete unfiltered All items", "return to selection", "Quiescence is not board completion", "older deferred work", "stops new selection immediately"),
        }.items():
            text = " ".join((base / carrier).read_text(encoding="utf-8").split())
            for clause in clauses:
                if clause not in text:
                    label = "p-add: filing contract lost" if carrier == "p-add/SKILL.md" else "padd-item: workspace filing contract lost" if carrier == "padd-item/SKILL.md" else f"lifecycle route: {carrier} lost"
                    fail(errors, f"{label} {clause!r}")
    for role in ("worker", "critic", "analyst"):
        for variant in ("normal", "best"):
            path = root / ".claude/agents" / f"fsgg-{role}-{variant}.md"
            if not path.is_file():
                fail(errors, f"lifecycle route: missing runtime caller {path.name}")
                continue
            text = path.read_text(encoding="utf-8")
            entrypoint = "board-analyst" if role == "analyst" else "pnext-item"
            if entrypoint not in text or re.search(r"scripts/fsgg-coord\s+(?:whoami|take|claim|done)\b", text):
                fail(errors, f"lifecycle route: runtime caller {path.name} bypasses canonical executor")


def work_board_instruction_text(root: Path, runtime: str, errors: list[str]) -> str:
    """Read the actual entrypoint and its required progressive lifecycle contract.

    A file existing beside the skill is insufficient: the entrypoint must load it. Keep the
    reporting/recovery assertions on those delivered instructions instead of requiring duplicated
    prose in SKILL.md or accepting an unreachable reference as governing text.
    """
    directory = root / runtime / "work-board"
    entry = directory / "SKILL.md"
    relative = "references/lifecycle-contract.md"
    try:
        body = entry.read_text(encoding="utf-8")
    except OSError as error:
        fail(errors, f"work-board coordinator: unreadable entrypoint {entry.relative_to(root)}: {error}")
        return ""
    if relative not in LINK.findall(body):
        fail(errors, "work-board coordinator: entrypoint must load references/lifecycle-contract.md")
        return ""
    try:
        contract = (directory / relative).read_text(encoding="utf-8")
    except OSError as error:
        fail(errors, f"work-board coordinator: unreadable lifecycle contract {runtime}: {error}")
        return ""
    return body + "\n" + contract


def validate_work_board_coordinator(root: Path, errors: list[str]) -> None:
    """Source boundary checks complement independent realistic consumer journeys."""
    for runtime in ROOTS:
        base = root / runtime
        instructions = work_board_instruction_text(root, runtime, errors)
        required = (
            "Project 3", "binding4/schema2/inspection2", "binding5/schema3/inspection3/integrator2", "qualified installed caller",
            "productBoard", "binding3", "remaining-after-active capacity",
            "439 inherited reservations", "46 later originals", "one independent source",
            "tools/routine-delivery.py", "actual tested", "roadmap-telemetry",
            "Source delivery, publication, installation", "No fixed polling",
            "Preserve the complete Done when; a merged slice narrows the remainder without closing its parent",
        )
        normalized = " ".join(instructions.split())
        for clause in required:
            if clause not in normalized:
                fail(errors, f"work-board coordinator: missing {clause!r}")
        current = "\n".join((base / (name + "/SKILL.md")).read_text(encoding="utf-8") for name in
                            ("work-board", "drive-board", "work-roadmap", "continue-from-handoff", "stop-with-handoff", "routine-decisions"))
        if re.search(r"\$work-programme|\]\([^)]*work-programme/SKILL\.md", current):
            fail(errors, "work-board coordinator: active caller returned to retired programme workflow")
        for name in ("work-board", "drive-board"):
            host = (base / name / "references/host-loop.md").read_text(encoding="utf-8")
            if "fsgg:wave-model:v1" in host or "BEGIN GENERATED: fsgg-protocol:wave-policy" in host:
                fail(errors, "organization successor: retired generated wave authority returned")
        retired = (base / "work-programme/SKILL.md").read_text(encoding="utf-8")
        if "[$work-board](../work-board/SKILL.md)" not in retired or "## Default workflow" in retired:
            fail(errors, "work-board coordinator: retired entrypoint is an active or broken workflow")


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
    parser.add_argument("--contract")
    parser.add_argument("--links-only", action="store_true",
                        help="check relative links only; no YAML, engine contract or CLR")
    args = parser.parse_args()
    root = Path(args.root).resolve()
    errors: list[str] = []
    if args.links_only:
        if args.contract:
            parser.error("--links-only does not accept --contract; full validation remains separate")
        directory = root / ROOTS[0]
        if not directory.is_dir():
            parser.error("--links-only requires the declared skill root")
        names = {path.name for path in directory.iterdir() if path.is_dir()}
        if not names:
            parser.error("--links-only requires a nonempty skill population")
        for name in sorted(names):
            if not (directory / name / "SKILL.md").is_file():
                fail(errors, f"{name}: --links-only requires SKILL.md")
        validate_links(root, names, errors)
        for error in errors:
            print(f"::error::skill-quality: {error}", file=sys.stderr)
        if errors:
            return 1
        print(f"skill-quality: relative links pass for {len(names)} skills; other obligations not evaluated")
        return 0
    if not args.contract:
        parser.error("full validation requires --contract")

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
    validate_receiver_safe_links(root, errors)
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
