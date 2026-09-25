#!/usr/bin/env python3
"""Read-only, reproducible source and workflow invocation census.

Run with: uv run --with pyyaml==6.0.3 python3 scripts/fsc-census.py --root .
The output is source evidence, not a semantic effective-step or installed-byte proof.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
from pathlib import Path

import yaml


def tracked(root: Path) -> list[tuple[str, str]]:
    result = subprocess.run(
        ["git", "-C", str(root), "ls-files", "-s", "-z"],
        check=True, capture_output=True,
    )
    entries = []
    for item in result.stdout.split(b"\0"):
        if item:
            metadata, path = item.split(b"\t", 1)
            entries.append((path.decode("utf-8", "surrogateescape"), metadata.split()[0].decode()))
    return sorted(entries)


def script_kind(path: str, first_line: str, executable: bool) -> str | None:
    if path.endswith(".fsx"):
        return "fsharp"
    if path.endswith(".ps1"):
        return "powershell"
    if path.endswith(".mjs"):
        return "node"
    if path.endswith(".py"):
        return "python"
    if path.endswith(".sh"):
        return "shell"
    if "." not in Path(path).name and first_line.startswith("#!"):
        if "python" in first_line:
            return "extensionless-python"
        if any(shell in first_line for shell in ("bash", "sh")):
            return "extensionless-shell"
        return "extensionless-other"
    if first_line.startswith("#!"):
        return "other-shebang"
    if executable:
        return "executable-unknown"
    return None


def lines_with_refs(root: Path, paths: set[str], source: str) -> list[dict]:
    try:
        content = (root / source).read_text(encoding="utf-8")
    except (UnicodeError, OSError):
        return []
    refs = []
    for number, line in enumerate(content.splitlines(), 1):
        for token in re.findall(r"(?<![\w./-])(?:\./)?scripts/[A-Za-z0-9_.+/-]+", line):
            target = token.removeprefix("./").rstrip(".,;:)\"'")
            if target in paths:
                refs.append({"source": source, "line": number, "target": target})
    return refs


def run_targets(run: str, paths: set[str]) -> list[str]:
    return sorted(set(
        token.removeprefix("./").rstrip(".,;:)\"'")
        for token in re.findall(r"(?<![\w./-])(?:\./)?scripts/[A-Za-z0-9_.+/-]+", run)
        if token.removeprefix("./").rstrip(".,;:)\"'") in paths
    ))


def interpreter_hints(shell: str, run: str) -> list[str]:
    """Lexical hints only; shell reachability and dynamic calls need owner proof."""
    hints = set()
    if shell == "runner-default" or shell.split()[0] in ("bash", "sh"):
        hints.add("shell")
    if re.search(r"(?m)(?:^|[;&|])\s*(?:python(?:3(?:\.\d+)?)?|py)\b", run):
        hints.add("python")
    if re.search(r"(?m)(?:^|[;&|])\s*(?:bash|sh)\b", run):
        hints.add("shell")
    if re.search(r"(?m)(?:^|[;&|])\s*dotnet\s+fsi\b", run):
        hints.add("fsharp")
    return sorted(hints)


def workflow_steps(root: Path, paths: set[str], names: list[str]) -> tuple[list[dict], list[dict]]:
    steps = []
    action_refs = []
    for name in names:
        try:
            data = yaml.load((root / name).read_text(encoding="utf-8"), Loader=yaml.BaseLoader)
        except (yaml.YAMLError, UnicodeError, OSError) as error:
            steps.append({"workflow": name, "error": str(error)})
            continue
        if not isinstance(data, dict):
            steps.append({"workflow": name, "error": "workflow root is not a mapping"})
            continue
        jobs = data.get("jobs") or {}
        if not isinstance(jobs, dict):
            steps.append({"workflow": name, "error": "jobs is not a mapping"})
            continue
        for job_name, job in jobs.items():
            if not isinstance(job, dict):
                continue
            job_steps = job.get("steps") or []
            if not isinstance(job_steps, list):
                steps.append({"workflow": name, "job": job_name, "error": "steps is not a sequence"})
                continue
            for index, step in enumerate(job_steps, 1):
                if not isinstance(step, dict):
                    continue
                run = step.get("run")
                uses = step.get("uses")
                if isinstance(run, str):
                    shell = step.get("shell", "runner-default")
                    if not isinstance(shell, str) or not shell.strip():
                        steps.append({"workflow": name, "job": job_name, "step": index,
                                      "error": "workflow run shell is not a scalar"})
                        continue
                    targets = run_targets(run, paths)
                    steps.append({"workflow": name, "job": job_name, "step": index,
                                  "shell": shell, "interpreter_hints": interpreter_hints(shell, run),
                                  "targets": targets, "run": run})
                if isinstance(uses, str):
                    action_refs.append({"workflow": name, "job": job_name, "step": index, "uses": uses})
            if isinstance(job.get("uses"), str):
                action_refs.append({"workflow": name, "job": job_name, "uses": job["uses"]})
    return steps, action_refs


def composite_action_steps(root: Path, paths: set[str], names: list[str]) -> tuple[list[dict], list[dict]]:
    steps = []
    refs = []
    for name in names:
        try:
            data = yaml.load((root / name).read_text(encoding="utf-8"), Loader=yaml.BaseLoader)
        except (yaml.YAMLError, UnicodeError, OSError) as error:
            steps.append({"action": name, "error": str(error)})
            continue
        if not isinstance(data, dict) or not isinstance(data.get("runs"), dict):
            steps.append({"action": name, "error": "action runs is not a mapping"})
            continue
        runs = data["runs"]
        if runs.get("using") != "composite":
            continue
        sequence = runs.get("steps")
        if not isinstance(sequence, list):
            steps.append({"action": name, "error": "composite action steps is not a sequence"})
            continue
        for index, step in enumerate(sequence, 1):
            if not isinstance(step, dict):
                steps.append({"action": name, "step": index, "error": "composite action step is not a mapping"})
                continue
            run = step.get("run")
            uses = step.get("uses")
            if isinstance(run, str):
                shell = step.get("shell", "runner-default")
                if not isinstance(shell, str) or not shell.strip():
                    steps.append({"action": name, "step": index,
                                  "error": "composite action run shell is not a scalar"})
                    continue
                steps.append({"action": name, "step": index, "shell": shell,
                              "interpreter_hints": interpreter_hints(shell, run),
                              "targets": run_targets(run, paths), "run": run})
            if isinstance(uses, str):
                refs.append({"action": name, "step": index, "uses": uses})
    return steps, refs


def census(root: Path) -> dict:
    entries = tracked(root)
    names = [path for path, _ in entries]
    modes = dict(entries)
    scripts = []
    for path in names:
        if modes[path] == "120000":
            continue
        try:
            with (root / path).open("rb") as handle:
                first_line = handle.readline(256).decode("utf-8", "replace").strip()
        except OSError:
            continue
        kind = script_kind(path, first_line, modes[path] == "100755")
        if kind:
            scripts.append({"path": path, "kind": kind, "shebang": first_line if first_line.startswith("#!") else None,
                            "executable": modes[path] == "100755"})
    paths = set(names)
    workflow_names = [name for name in names if name.startswith(".github/workflows/") and name.endswith((".yml", ".yaml"))]
    steps, action_refs = workflow_steps(root, paths, workflow_names)
    action_names = [name for name in names if name.startswith(".github/actions/") and name.endswith(("/action.yml", "/action.yaml"))]
    action_steps, nested_action_refs = composite_action_steps(root, paths, action_names)
    issues = [f"{item['workflow']}: {item['error']}" for item in steps if "error" in item]
    issues.extend(f"{item['action']}: {item['error']}" for item in action_steps if "error" in item)
    issues.extend(f"{item['path']}: executable interpreter unknown" for item in scripts
                  if item["kind"] == "executable-unknown")
    for item in scripts:
        declared = item["kind"]
        shebang = item["shebang"] or ""
        if shebang and ((declared == "python" and "python" not in shebang)
                        or (declared == "shell" and not any(shell in shebang for shell in ("bash", "sh")))):
            issues.append(f"{item['path']}: extension/shebang interpreter mismatch")
        if item["executable"] and not shebang and declared != "executable-unknown":
            issues.append(f"{item['path']}: executable source has no shebang")
    refs = []
    for name in names:
        if name.startswith((".github/workflows/", "scripts/", "eng/", "tests/")) and name.endswith((".yml", ".yaml", ".sh", ".py")):
            refs.extend(lines_with_refs(root, paths, name))
    try:
        pins = json.loads((root / ".config/dotnet-tools.json").read_text())["tools"]
    except (OSError, ValueError, KeyError):
        pins = {}
    head = subprocess.run(["git", "-C", str(root), "rev-parse", "HEAD"], check=True, capture_output=True, text=True).stdout.strip()
    return {"root": str(root), "head": head, "tracked_files": len(names), "scripts": scripts,
            "workflows": workflow_names, "workflow_steps": steps, "workflow_action_refs": action_refs,
            "composite_actions": action_names, "action_steps": action_steps,
            "nested_action_refs": nested_action_refs,
            "lexical_script_refs": refs, "dotnet_tool_pins": {key: value.get("version") for key, value in pins.items()},
            "issues": issues}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("."))
    args = parser.parse_args()
    result = census(args.root.resolve())
    print(json.dumps(result, indent=2, sort_keys=True))
    if result["issues"]:
        raise SystemExit(2)


if __name__ == "__main__":
    main()
