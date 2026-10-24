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
MARKER = "[org-publication-writer]"
REMOTE_ACTION = re.compile(r"^FS-GG/\.github/\.github/actions/publication-writer-lock@[0-9a-f]{40}(\s+#.*)?$")
LOCAL_ACTION = "./.github/actions/publication-writer-lock"
# A repository whose Actions allow-list admits no third-party action cannot use the composite action. It
# fetches the lock script from FS-GG/.github at a pinned commit, verifies a pinned sha256 and runs it
# (the same script the action runs). The whole step must be exactly this text; only the four values vary.
SCRIPT_GATE_TEMPLATE = (
    "      - name: Acquire org publication writer lock\n"
    "        shell: bash\n"
    "        timeout-minutes: <<timeout>>\n"
    "        env:\n"
    "          GH_TOKEN: ${{ github.token }}\n"
    "          LOCK_COMMIT: <<commit>>\n"
    "          LOCK_SCRIPT_SHA256: <<digest>>\n"
    "          LOCK_MAX_WAIT_MINUTES: '<<wait>>'\n"
    "        run: |\n"
    "          set -euo pipefail\n"
    "          lock=\"$RUNNER_TEMP/publication_writer_lock.py\"\n"
    "          curl --fail --silent --show-error --location --output \"$lock\" \\\n"
    "            \"https://raw.githubusercontent.com/FS-GG/.github/${LOCK_COMMIT}/scripts/publication_writer_lock.py\"\n"
    "          echo \"${LOCK_SCRIPT_SHA256}  ${lock}\" | sha256sum --check --strict\n"
    "          python3 -I -B \"$lock\" acquire --max-wait-minutes \"$LOCK_MAX_WAIT_MINUTES\""
)
SCRIPT_GATE_RE = re.compile(
    re.escape(SCRIPT_GATE_TEMPLATE)
    .replace(re.escape("<<timeout>>"), r"(?P<timeout>\d+)")
    .replace(re.escape("<<commit>>"), r"(?P<commit>[0-9a-f]{40})")
    .replace(re.escape("<<digest>>"), r"(?P<digest>[0-9a-f]{64})")
    .replace(re.escape("<<wait>>"), r"(?P<wait>\d+)"))
# An environment that can redirect the commands the pinned step runs defeats the digest check.
HIJACK_ENV = re.compile(r"^\s*(?:BASH_ENV|ENV|PATH|LD_PRELOAD|LD_LIBRARY_PATH|BASH_FUNC[A-Za-z0-9_%]*|GITHUB_PATH):", re.MULTILINE)
# Publication tokens decide whether a workflow must be registered; the broader write tokens decide
# which of a registered workflow's jobs need the gate (a credential exchange or any ref/API write).
PUBLISH_EFFECT = re.compile(r"dotnet nuget push|NuGet/login@|gh release (?:create|upload|edit|delete)|softprops/action-gh-release|git push[^\n]*(?:--tags|refs/tags|--atomic)")
EFFECT = re.compile(PUBLISH_EFFECT.pattern + r"|create-github-app-token@|\bgit push\b|gh api[^\n]*(?:-X|--method)[ =](?:POST|PATCH|PUT|DELETE)")


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


def parse_script_gate(gate: str):
    """{'timeout','commit','digest','wait'} when `gate` is exactly the pinned-script step, else None."""
    match = SCRIPT_GATE_RE.fullmatch(gate.strip("\n").rstrip())
    return match.groupdict() if match else None


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
            name = re.search(r"^    name:\s*(.+?)\s*$", job, re.MULTILINE)
            if gate_ix or (name and MARKER in name.group(1)):
                problems.append(f"job {key}: declared an observer but takes the lock or carries the marker")
            if any(PUBLISH_EFFECT.search(x) for x in steps):
                problems.append(f"job {key}: declared an observer but carries a publication token")
            continue
        if not gate_ix and not effect_ix and not writes:
            continue
        if not effect_ix and not gate_ix:
            problems.append(f"job {key}: write-capable (permissions) but has no '{GATE_STEP}' step; list it under observationJobs if it is read-only by contract")
            continue
        if len(gate_ix) != 1:
            problems.append(f"job {key}: write-capable jobs need exactly one '{GATE_STEP}' step (found {len(gate_ix)})")
            continue
        gate = steps[gate_ix[0]]
        before = [x for x in steps[:gate_ix[0]] if not re.search(r"^\s+(?:- )?uses:\s*actions/checkout@", x, re.MULTILINE)]
        if before:
            problems.append(f"job {key}: only checkout may precede the lock step (found '{step_name(before[0]) or before[0].strip()[:40]}')")
        name = re.search(r"^    name:\s*(.+?)\s*$", job, re.MULTILINE)
        value = re.sub(r"\s+#.*$", "", name.group(1)).strip("\"'") if name else ""
        if MARKER not in value:
            problems.append(f"job {key}: the job name must contain '{MARKER}' so other publishers can see it (the API step list is unreliable)")
        uses = re.search(r"^\s+(?:- )?uses:\s*(.+?)\s*$", gate, re.MULTILINE)
        action = uses.group(1) if uses else ""
        if repository == "FS-GG/.github":
            ok = action == LOCAL_ACTION or bool(REMOTE_ACTION.fullmatch(action))
        else:
            ok = bool(REMOTE_ACTION.fullmatch(action))
        if not uses and repository != "FS-GG/.github":
            values = parse_script_gate(gate)
            ok = values is not None
            if values is None:
                problems.append(f"job {key}: the lock step is neither the pinned composite action nor exactly the pinned-script step")
            elif HIJACK_ENV.search("\n".join(executable_lines(text))):
                ok = False
                problems.append(f"job {key}: BASH_ENV, PATH or a loader variable is set in the workflow, which could redirect the pinned-script lock step")
            wait = re.search(r"LOCK_MAX_WAIT_MINUTES:\s*['\"]?(\d+)", gate)
        else:
            wait = re.search(r"max-wait-minutes:\s*(\d+)", gate)
        if not ok and (uses or repository == "FS-GG/.github"):
            problems.append(f"job {key}: lock step must use the pinned FS-GG/.github lock action (got {action!r})")
        limit = re.search(r"^\s+timeout-minutes:\s*(\d+)\s*$", gate, re.MULTILINE)
        if not wait or not limit or int(limit.group(1)) <= int(wait.group(1)):
            problems.append(f"job {key}: the lock step needs max-wait-minutes and a larger step timeout-minutes (a degraded API must not outlive the wait)")
        if re.search(r"^\s+[\"']?if[\"']?\s*:", gate, re.MULTILINE) or re.search(r"^\s+[\"']?continue-on-error[\"']?\s*:", gate, re.MULTILINE):
            problems.append(f"job {key}: lock step must be unconditional and may not continue on error")
        if effect_ix and gate_ix[0] > min(effect_ix):
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
        has_effect = any(PUBLISH_EFFECT.search(line) for line in executable_lines(text))
        if path in exempt:
            if result["gated"]:
                errors.append(f"{repository}:{path}: exempt workflow is gated; move it to writers")
            continue
        if path in writers or has_effect or result["gated"]:
            for problem in result["problems"]:
                errors.append(f"{repository}:{path}: {problem}")
        callers_only = bool(re.search(r"^    uses:", text, re.MULTILINE)) and not result["gated"]
        if path in writers and not result["gated"] and not result["problems"] and not callers_only:
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


def script_pin_errors(files: dict, repository: str, script_at) -> list:
    """Every pinned-script gate must name a real FS-GG/.github commit whose lock script has the pinned sha256.

    The pins are taken from the same strict parse that check_workflow accepts, so a quoted or duplicated
    value cannot be audited as one thing and executed as another."""
    import hashlib
    errors = []
    for path, text in sorted(files.items()):
        pairs, steps_seen = [], 0
        for key, job in split_jobs(text).items():
            for step in split_steps(job):
                if step_name(step) != GATE_STEP:
                    continue
                values = parse_script_gate(step)
                if values is not None:
                    pairs.append((values["commit"], values["digest"]))
        steps_seen = sum(1 for line in executable_lines(text) if re.match(r"^\s+LOCK_COMMIT:", line))
        if steps_seen != len(pairs):
            errors.append(f"{repository}:{path}: {steps_seen - len(pairs)} LOCK_COMMIT pin(s) are outside an exact pinned-script gate and are not verified")
        for commit, digest in sorted(set(pairs)):
            try:
                actual = hashlib.sha256(script_at(commit)).hexdigest()
            except Exception as error:  # noqa: BLE001 - any unreadable pin is unmet
                errors.append(f"{repository}:{path}: pinned lock commit {commit} is unreadable: {error}")
                continue
            if actual != digest:
                errors.append(f"{repository}:{path}: pinned sha256 {digest} is not the lock script at {commit} ({actual})")
    return errors


def lock_script_at(commit: str, token: str = "") -> bytes:
    import base64
    body = fetch(f"/repos/FS-GG/.github/contents/scripts/publication_writer_lock.py?ref={commit}", token)
    return base64.b64decode(body["content"])


def remote_files(repository: str, token: str) -> dict:
    branch = fetch(f"/repos/{repository}", token)["default_branch"]  # not every repository uses main (RecoilEngine: master)
    listing = fetch(f"/repos/{repository}/contents/.github/workflows?ref={branch}", token)
    files = {}
    for item in listing:
        if item["type"] == "file" and item["name"].endswith((".yml", ".yaml")):
            body = fetch(f"/repos/{repository}/contents/{item['path']}?ref={branch}", token)
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
            files = remote_files(repository, token)
            errors += audit(files, repository, registry)
            errors += script_pin_errors(files, repository, lambda commit: lock_script_at(commit, token))
    for error in errors:
        print("UNMET " + error)
    print(f"publication writer coverage: {len(errors)} unmet")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
