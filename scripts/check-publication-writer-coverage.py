#!/usr/bin/env python3
"""Static coverage check for the org publication writer lock (ADR-0097).

A job of a registered workflow that can write (an effect token, or `contents`/`packages`/
`id-token: write` permission) must contain exactly one unconditional lock step, named
`GATE_STEP`, using the lock action, positioned before every effect token. A job that is read-only
by contract is listed under the registry row's `observationJobs`. Every workflow with such a job
must be registered in `registry/publication-writers.json` (or exempted there with a reason).

  --local            check the workflows of this checkout against the registry (CI gate)
  --remote           also fetch each registered/exempt repository's workflows from `main`
                     and check them (needs network; reports unmet adoption, not a CI gate)
  --file PATH ...    check named workflow files and print problems
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys
import urllib.request

sys.dont_write_bytecode = True
ROOT = pathlib.Path(__file__).resolve().parents[1]
GATE_STEP = "Acquire org publication writer lock"
REMOTE_ACTION = re.compile(r"^FS-GG/\.github/\.github/actions/publication-writer-lock@[0-9a-f]{40}(\s+#.*)?$")
LOCAL_ACTION = "./.github/actions/publication-writer-lock"
EFFECT = re.compile(r"dotnet nuget push|NuGet/login@|gh release (?:create|upload|edit)|softprops/action-gh-release")


def executable_lines(text: str) -> list:
    return [line for line in text.splitlines() if not re.match(r"^\s*#", line)]


def split_jobs(text: str) -> dict:
    """job key -> executable job text, for the top-level `jobs:` mapping (2-space keys)."""
    lines = executable_lines(text)
    try:
        start = lines.index("jobs:") + 1
    except ValueError:
        return {}
    jobs, key, body = {}, None, []
    for line in lines[start:]:
        if line and not line.startswith(" "):
            break
        match = re.fullmatch(r"  ([A-Za-z0-9_-]+):\s*", line)
        if match:
            if key is not None:
                jobs[key] = "\n".join(body)
            key, body = match.group(1), []
        elif key is not None:
            body.append(line)
    if key is not None:
        jobs[key] = "\n".join(body)
    return jobs


def split_steps(job: str) -> list:
    """Step texts of one job (6-space `- ` items under the 4-space `steps:` key)."""
    lines = job.splitlines()
    try:
        start = lines.index("    steps:") + 1
    except ValueError:
        return []
    steps, current = [], None
    for line in lines[start:]:
        if re.match(r"^      - ", line):
            current = [line]
            steps.append(current)
        elif current is not None:
            current.append(line)
    return ["\n".join(step) for step in steps]


def step_name(step: str):
    match = re.search(r"^      - name:\s*(.+?)\s*$|^        name:\s*(.+?)\s*$", step, re.MULTILINE)
    return (match.group(1) or match.group(2)).strip("\"'") if match else None


def permission_writes(block: str) -> bool:
    return bool(re.search(r"^\s*(?:contents|packages|id-token):\s*write\b|^\s*permissions:\s*write-all\b|^\s+write-all\b", block, re.MULTILINE))


def workflow_permissions(lines: list) -> str:
    """The top-level `permissions:` block (or the empty string)."""
    out, inside = [], False
    for line in lines:
        if re.fullmatch(r"permissions:\s*(?:write-all)?", line):
            inside = True
            out.append(line)
        elif inside and (line.startswith(" ") or not line):
            out.append(line)
        elif inside:
            break
    return "\n".join(out)


def job_permissions(job: str):
    """The job's own `permissions:` block, or None when it inherits."""
    lines = job.splitlines()
    for i, line in enumerate(lines):
        if re.fullmatch(r"    permissions:\s*(?:write-all)?", line):
            block = [line]
            for follow in lines[i + 1:]:
                if follow.startswith("      ") or not follow.strip():
                    block.append(follow)
                else:
                    break
            return "\n".join(block)
    return None


def check_workflow(text: str, repository: str, observers=()) -> dict:
    """{'gated': [job keys], 'problems': [str]} for one workflow's bytes."""
    problems, gated = [], []
    inherited = workflow_permissions(executable_lines(text))
    for key, job in split_jobs(text).items():
        if re.search(r"^    uses:", job, re.MULTILINE):
            continue  # a reusable-workflow caller; the called workflow's jobs carry the gate
        steps = split_steps(job)
        gate_ix = [i for i, s in enumerate(steps) if step_name(s) == GATE_STEP]
        effect_ix = [i for i, s in enumerate(steps) if EFFECT.search(s)]
        own = job_permissions(job)
        writes = permission_writes(inherited if own is None else own)
        if key in observers:
            if gate_ix:
                problems.append(f"job {key}: declared an observer but takes the lock")
            continue
        if not gate_ix and not effect_ix and not writes:
            continue
        if not effect_ix:
            if not gate_ix:
                problems.append(f"job {key}: write-capable (permissions) but has no '{GATE_STEP}' step; list it under observationJobs if it is read-only by contract")
            else:
                gated.append(key)
            continue
        if len(gate_ix) != 1:
            problems.append(f"job {key}: effect steps need exactly one '{GATE_STEP}' step (found {len(gate_ix)})")
            continue
        gate = steps[gate_ix[0]]
        uses = re.search(r"^\s+(?:- )?uses:\s*(.+?)\s*$", gate, re.MULTILINE)
        action = uses.group(1) if uses else ""
        if repository == "FS-GG/.github":
            ok = action == LOCAL_ACTION or bool(REMOTE_ACTION.fullmatch(action))
        else:
            ok = bool(REMOTE_ACTION.fullmatch(action))
        if not ok:
            problems.append(f"job {key}: lock step must use the pinned FS-GG/.github lock action (got {action!r})")
        if re.search(r"^\s+if:", gate, re.MULTILINE) or re.search(r"^\s+continue-on-error:", gate, re.MULTILINE):
            problems.append(f"job {key}: lock step must be unconditional and may not continue on error")
        if gate_ix[0] > min(effect_ix):
            problems.append(f"job {key}: lock step must precede the first effect step '{step_name(steps[min(effect_ix)])}'")
        if re.search(r"^    continue-on-error:", job, re.MULTILINE):
            problems.append(f"job {key}: job-level continue-on-error could hide a refused lock")
        gated.append(key)
    return {"gated": gated, "problems": problems}


def load_registry(path: pathlib.Path) -> dict:
    document = json.loads(path.read_text())
    sys.path.insert(0, str(ROOT / "scripts"))
    import publication_writer_lock as lock  # noqa: E402  (local module)
    lock.validate_registry(document)
    return document


def audit(files: dict, repository: str, registry: dict) -> list:
    """files: workflow path -> text, for one repository."""
    rows = [row for row in registry["writers"] if row["repo"] == repository]
    writers = {w for row in rows for w in row["workflows"]}
    observers = {w: set(jobs) for row in rows for w, jobs in row.get("observationJobs", {}).items()}
    exempt = {w for row in registry.get("exempt", []) if row["repo"] == repository for w in row["workflows"]}
    errors = []
    for path, text in sorted(files.items()):
        result = check_workflow(text, repository, observers.get(path, ()))
        has_effect = any(EFFECT.search(line) for line in executable_lines(text))
        if path in exempt:
            if result["gated"]:
                errors.append(f"{repository}:{path}: exempt workflow is gated; move it to writers")
            continue
        if path in writers or has_effect or result["gated"]:
            for problem in result["problems"]:
                errors.append(f"{repository}:{path}: {problem}")
        if path in writers and not result["gated"] and not result["problems"]:
            errors.append(f"{repository}:{path}: registered writer has no gated job (every job is read-only?)")
        if path not in writers and (has_effect or result["gated"]):
            errors.append(f"{repository}:{path}: publishes (or is gated) but is neither registered nor exempt")
    for path in sorted(writers - set(files)):
        errors.append(f"{repository}:{path}: registered writer workflow does not exist")
    return errors


def fetch(path: str, token: str = "") -> object:
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "fsgg-publication-writer-coverage"}
    if token:
        headers["Authorization"] = "Bearer " + token
    with urllib.request.urlopen(urllib.request.Request("https://api.github.com" + path, headers=headers), timeout=30) as response:
        return json.loads(response.read())


def remote_files(repository: str, token: str) -> dict:
    listing = fetch(f"/repos/{repository}/contents/.github/workflows?ref=main", token)
    files = {}
    for item in listing:
        if item["type"] == "file" and item["name"].endswith((".yml", ".yaml")):
            body = fetch(f"/repos/{repository}/contents/{item['path']}?ref=main", token)
            import base64
            files[item["path"]] = base64.b64decode(body["content"]).decode()
    return files


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--registry", default=str(ROOT / "registry/publication-writers.json"))
    parser.add_argument("--local", action="store_true")
    parser.add_argument("--remote", action="store_true")
    parser.add_argument("--file", nargs="*", default=[])
    args = parser.parse_args(argv)
    errors = []
    if args.file:
        for name in args.file:
            result = check_workflow(pathlib.Path(name).read_text(), "FS-GG/.github")
            errors += [f"{name}: {p}" for p in result["problems"]]
    registry = load_registry(pathlib.Path(args.registry))
    if args.local:
        files = {f".github/workflows/{p.name}": p.read_text() for p in sorted((ROOT / ".github/workflows").glob("*.y*ml"))}
        errors += audit(files, "FS-GG/.github", registry)
    if args.remote:
        import os
        token = os.environ.get("GH_TOKEN", "")
        repos = {row["repo"] for row in registry["writers"]} | {row["repo"] for row in registry["exempt"]}
        for repository in sorted(repos - {"FS-GG/.github"}):
            errors += audit(remote_files(repository, token), repository, registry)
    for error in errors:
        print("UNMET " + error)
    print(f"publication writer coverage: {len(errors)} unmet")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
