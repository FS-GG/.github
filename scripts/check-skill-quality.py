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

ROOTS = (".claude/skills", ".codex/skills", ".agents/skills")
EXPLICIT_ONLY = {"cut-nuget-release", "drive-board", "work-board", "work-roadmap"}
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
    if contract.get("schema") != "fsgg.coord.commands/1":
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
        # validate_invocations already emits the actionable contract diagnostic.
        return
    if contract.get("schema") != "fsgg.coord.commands/1":
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
