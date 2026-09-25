#!/usr/bin/env python3
"""Offline Python-vs-F# permission policy corpus over identical supplied YAML bytes.

The gh executable is a local fixture, and the F# side receives declarative provider facts.
Known stricter F# refusals and fleet-enumeration gaps are printed separately from parity failures.
"""

from __future__ import annotations

import copy
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
PYTHON_GATE = ROOT / "scripts/check-workflow-permissions.py"
FSHARP_RUNNER = HERE / "bin/Debug/net10.0/DifferentialRunner.dll"
AUTHORITY = "FS-GG/.github"
EXPECTED_PYTHON = {
    "exact": "OK",
    "caller_undergrant": "FINDING",
    "caller_absent": "FINDING",
    "job_override_narrows": "FINDING",
    "callee_inherits_without_floor": "OK",
    "multi_app_override": "OK",
    "multi_app_override_undergrants": "FINDING",
    "unselected_app_identity": "NO_VERDICT",
    "unselected_app_identity_would_pass_default": "NO_VERDICT",
    "unsupported_vars_app_identity": "NO_VERDICT",
    "unsupported_literal_app_identity": "NO_VERDICT",
    "malformed_caller_yaml": "NO_VERDICT",
    "duplicate_caller_key": "NO_VERDICT",
    "duplicate_callee_event": "NO_VERDICT",
    "duplicate_app_permission": "NO_VERDICT",
    "dynamic_app_request": "NO_VERDICT",
    "rostered_second_caller_undergrants": "FINDING",
    "external_rostered_caller_undergrants": "FINDING",
    "pinned_ref_uses_fetched_callee": "OK",
    "non_org_call": "NO_VERDICT",
    "callee_not_callable": "NO_VERDICT",
    "authority_workflow_omitted": "NO_VERDICT",
    "authority_job_omitted": "NO_VERDICT",
    "authority_app_step_omitted": "NO_VERDICT",
    "authority_roster_omitted_workflow": "NO_VERDICT",
    "authority_roster_duplicate_workflow": "NO_VERDICT",
}
KNOWN_PAIRS = {}

CALLEE = """on: { workflow_call: {} }
permissions: { contents: read }
jobs:
  x:
    steps:
      - run: echo ready
"""
APP = """jobs:
  mint:
    steps:
      - uses: actions/create-github-app-token@v3
        with:
          permission-contents: read
"""


def caller(top="permissions: { contents: read }", job="", target="cal.yml@main", extra=""):
    return f"{top}\njobs:\n  sync:\n{job}    uses: FS-GG/.github/.github/workflows/{target}\n{extra}"


BASE = {
    "source_ref": "fixture-head",
    "caller_repository": "FS-GG/R",
    "caller_yaml": caller(),
    "callee_yaml": CALLEE,
    "roster_repositories": ["FS-GG/R"],
    "expected_caller_workflows": [
        {"repository": "FS-GG/R", "source_ref": "fixture-r-head",
         "paths": [".github/workflows/caller.yml"]},
    ],
    "caller_call_facts": [
        {"repository": "FS-GG/R", "path": ".github/workflows/caller.yml",
         "job_id": "sync", "callee": "cal.yml", "ref": "main", "inventory_id": "default"},
    ],
    "authority_workflows": [
        {"path": ".github/workflows/cal.yml", "text": CALLEE},
        {"path": ".github/workflows/app.yml", "text": APP},
    ],
    "expected_workflows": [
        {"path": ".github/workflows/cal.yml", "jobs": [
            {"job_id": "x", "reusable": False, "steps": 1, "app_steps": []}]},
        {"path": ".github/workflows/app.yml", "jobs": [
            {"job_id": "mint", "reusable": False, "steps": 1, "app_steps": [1]}]},
    ],
    "inventories": [{"id": "default", "grants": {"contents": "read"}}],
    "additional_callers": {},
    "pinned_callees": {},
}


def cases():
    out = []

    def add(name, change=None, known=None, python_reason=None):
        scenario = copy.deepcopy(BASE)
        if change:
            change(scenario)
        scenario["name"] = name
        scenario["known_divergence"] = known
        scenario["expected_python_reason"] = python_reason
        out.append(scenario)

    add("exact")
    add("caller_undergrant", lambda s: s.update(caller_yaml=caller("permissions: { contents: none }")))
    add("caller_absent", lambda s: s.update(caller_yaml=caller("")))
    add("job_override_narrows", lambda s: s.update(caller_yaml=caller(job="    permissions: { contents: none }\n")))

    def inherited_callee(s):
        s["callee_yaml"] = CALLEE.replace("permissions: { contents: read }\n", "")
        s["authority_workflows"][0]["text"] = s["callee_yaml"]

    add("callee_inherits_without_floor", inherited_callee)

    def app_override(s):
        s["authority_workflows"][1]["text"] = APP.replace(
            "permission-contents: read",
            "client-id: ${{ secrets.DEDICATED_APP_CLIENT_ID }}\n          permission-issues: write",
        )
        s["inventories"].append({"id": "DEDICATED_APP_CLIENT_ID", "grants": {"issues": "write"}})

    add("multi_app_override", app_override)

    def narrow_override(s):
        app_override(s)
        s["inventories"][1]["grants"]["issues"] = "read"

    add("multi_app_override_undergrants", narrow_override)

    def missing_app_override(s):
        app_override(s)
        s["inventories"].pop()

    missing_identity_reason = "no App grant inventory selects app-id secret 'DEDICATED_APP_CLIENT_ID'"
    add("unselected_app_identity", missing_app_override, python_reason=missing_identity_reason)

    def missing_app_override_with_broad_default(s):
        s["authority_workflows"][1]["text"] = APP.replace(
            "permission-contents: read",
            "client-id: ${{ secrets.DEDICATED_APP_CLIENT_ID }}\n          permission-contents: read",
        )

    add("unselected_app_identity_would_pass_default", missing_app_override_with_broad_default,
        python_reason=missing_identity_reason)
    unsupported_identity_reason = "App identity must be a static secrets.NAME selector"
    add("unsupported_vars_app_identity", lambda s: s["authority_workflows"][1].update(
        text=APP.replace("permission-contents: read",
                         "app-id: ${{ vars.ORDINARY_APP_ID }}\n          permission-contents: read")),
        python_reason=unsupported_identity_reason)
    add("unsupported_literal_app_identity", lambda s: s["authority_workflows"][1].update(
        text=APP.replace("permission-contents: read",
                         "app-id: 123\n          permission-contents: read")),
        python_reason=unsupported_identity_reason)
    add("malformed_caller_yaml", lambda s: s.update(caller_yaml="jobs: { sync: [\n"))
    duplicate_reason = "duplicate YAML mapping key"
    add("duplicate_caller_key", lambda s: s.update(caller_yaml=
        "permissions: { contents: none }\npermissions: { contents: read }\n"
        "jobs: { sync: { uses: 'FS-GG/.github/.github/workflows/cal.yml@main' } }\n"),
        python_reason=duplicate_reason)
    def duplicate_callee_event(s):
        s["callee_yaml"] = CALLEE.replace("on: { workflow_call: {} }",
                                          "on: { workflow_call: {}, workflow_call: {} }")
        s["authority_workflows"][0]["text"] = s["callee_yaml"]

    add("duplicate_callee_event", duplicate_callee_event, python_reason=duplicate_reason)
    add("duplicate_app_permission", lambda s: s["authority_workflows"][1].update(
        text=APP.replace("permission-contents: read",
                         "permission-contents: none\n          permission-contents: read")),
        python_reason=duplicate_reason)
    add("dynamic_app_request", lambda s: s["authority_workflows"][1].update(
        text=APP.replace("permission-contents: read", "permission-contents: ${{ inputs.level }}")))

    def extra_rostered_repo(s, repo="FS-GG/S"):
        s["roster_repositories"].append(repo)
        s["additional_callers"][repo] = [caller("permissions: { contents: none }")]
        s["expected_caller_workflows"].append(
            {"repository": repo, "source_ref": "fixture-s-head",
             "paths": [".github/workflows/caller-1.yml"]})
        s["caller_call_facts"].append(
            {"repository": repo, "path": ".github/workflows/caller-1.yml",
             "job_id": "sync", "callee": "cal.yml", "ref": "main", "inventory_id": "default"})

    add("rostered_second_caller_undergrants", extra_rostered_repo)
    add("external_rostered_caller_undergrants",
        lambda s: extra_rostered_repo(s, "EHotwagner/S.I.R."))

    def pinned(s):
        s["caller_yaml"] = caller(target="cal.yml@v1")
        s["caller_call_facts"][0]["ref"] = "v1"
        s["pinned_callees"]["v1"] = CALLEE
        s["callee_yaml"] = CALLEE
        s["authority_workflows"][0]["text"] = CALLEE.replace(
            "contents: read", "contents: read, packages: read")

    add("pinned_ref_uses_fetched_callee", pinned)
    add("non_org_call", lambda s: s.update(caller_yaml=
        "permissions: { contents: read }\njobs: { sync: { uses: 'Other/.github/.github/workflows/cal.yml@main' } }\n"))

    def not_callable(s):
        s["callee_yaml"] = CALLEE.replace("workflow_call", "push")
        s["authority_workflows"][0]["text"] = s["callee_yaml"]

    add("callee_not_callable", not_callable)
    add("authority_workflow_omitted", lambda s: s["authority_workflows"].pop(),
        python_reason="authority roster workflow set mismatch")
    add("authority_job_omitted", lambda s: s["authority_workflows"][1].update(
        text=APP.replace("mint:", "other:")),
        python_reason="authority roster job shape mismatch")
    add("authority_app_step_omitted", lambda s: s["authority_workflows"][1].update(
        text=APP.replace("uses: actions/create-github-app-token@v3",
                         "run: echo no-token")),
        python_reason="authority roster job shape mismatch")
    add("authority_roster_omitted_workflow", lambda s: s["expected_workflows"].pop(),
        python_reason="authority roster workflow set mismatch")
    add("authority_roster_duplicate_workflow", lambda s: s["expected_workflows"].append(
        copy.deepcopy(s["expected_workflows"][0])),
        python_reason="authority roster workflow entry invalid")
    return out


GH_STUB = """#!/usr/bin/env python3
import os
from pathlib import Path
import sys

world = Path(os.environ['DIFF_WORLD'])
path = next((arg for arg in sys.argv if arg.startswith('repos/')), '')
if not path:
    print('gh: Not Found (HTTP 404)', file=sys.stderr); sys.exit(1)
rest = path.removeprefix('repos/')
marker = '/contents/.github/workflows'
if marker not in rest:
    repo = rest
    if (world / repo.replace('/', '__')).exists():
        print(repo); sys.exit(0)
    print('gh: Not Found (HTTP 404)', file=sys.stderr); sys.exit(1)
repo, tail = rest.split(marker, 1)
if not tail:
    folder = world / repo.replace('/', '__')
    if folder.is_dir():
        print('\\n'.join(sorted(path.name for path in folder.iterdir() if path.is_file())))
        sys.exit(0)
else:
    name, _, ref = tail.lstrip('/').partition('?ref=')
    file = world / 'refs' / ref / name if ref else world / repo.replace('/', '__') / name
    if file.is_file():
        sys.stdout.write(file.read_text(encoding='utf-8')); sys.exit(0)
print('gh: Not Found (HTTP 404)', file=sys.stderr); sys.exit(1)
"""


def write(path: Path, text: str):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def normalize_python(code: int):
    return {0: "OK", 1: "FINDING", 2: "NO_VERDICT", 3: "NO_VERDICT"}.get(code, "CRASH")


def run_case(scenario, temp: Path, stub: Path):
    fixture = temp / scenario["name"]
    root, world = fixture / "root", fixture / "world"
    for workflow in scenario["authority_workflows"]:
        write(root / workflow["path"], workflow["text"])
    roster = "repos:\n" + "".join(
        f"  - {{ full: {repo} }}\n" for repo in scenario["roster_repositories"])
    write(root / "registry/repos.yml", roster)
    authority_roster = root / "registry/authority-workflows.yml"
    write(authority_roster, json.dumps({
        "schemaVersion": 1,
        "repository": AUTHORITY,
        "sourceRef": scenario["source_ref"],
        "workflows": scenario["expected_workflows"],
    }))
    write(world / "FS-GG__R/caller.yml", scenario["caller_yaml"])
    for repo, workflows in scenario["additional_callers"].items():
        for index, text in enumerate(workflows, 1):
            write(world / repo.replace("/", "__") / f"caller-{index}.yml", text)
    for ref, text in scenario["pinned_callees"].items():
        write(world / "refs" / ref / "cal.yml", text)
    data = fixture / "scenario.json"
    write(data, json.dumps(scenario, indent=2))

    default = next(item for item in scenario["inventories"] if item["id"] == "default")
    grant_arg = ",".join(f"{scope}:{level}" for scope, level in default["grants"].items())
    command = [sys.executable, str(PYTHON_GATE), "--root", str(root),
               "--app-grants", grant_arg, "--require-app-identity-grants",
               "--authority-workflow-roster", str(authority_roster),
               "--authority-source-ref", scenario["source_ref"]]
    for item in scenario["inventories"]:
        if item["id"] != "default":
            grants = ",".join(f"{scope}:{level}" for scope, level in item["grants"].items())
            command += ["--app-grants-for", f"{item['id']}={grants}"]
    env = os.environ.copy()
    env.update(PATH=f"{stub.parent}:{env['PATH']}", DIFF_WORLD=str(world),
               FSGG_PERMS_TRIES="1", FSGG_PERMS_RETRY_DELAY="0")
    python_result = subprocess.run(command, text=True, capture_output=True, env=env)
    fsharp_result = subprocess.run(["dotnet", str(FSHARP_RUNNER), str(data)],
                                   text=True, capture_output=True)
    if fsharp_result.returncode != 0:
        raise RuntimeError(f"{scenario['name']}: F# runner failed: {fsharp_result.stderr}")
    output = fsharp_result.stdout.strip().splitlines()
    if not output or not output[-1].startswith("DIFF|"):
        raise RuntimeError(f"{scenario['name']}: no F# verdict: {fsharp_result.stdout}")
    _, fsharp_class, reason = output[-1].split("|", 2)
    return normalize_python(python_result.returncode), fsharp_class, reason, python_result.stdout + python_result.stderr


def main():
    if not FSHARP_RUNNER.exists():
        raise SystemExit("build DifferentialRunner.fsproj first")
    failures = 0
    known = 0
    with tempfile.TemporaryDirectory(prefix="fsc03-differential-") as folder:
        temp = Path(folder)
        stub = temp / "stub/gh"
        write(stub, GH_STUB)
        stub.chmod(0o755)
        for scenario in cases():
            py, fs, reason, output = run_case(scenario, temp, stub)
            name = scenario["name"]
            expected_reason = scenario.get("expected_python_reason")
            if py != EXPECTED_PYTHON[name] or (expected_reason and expected_reason not in output):
                state = "MISMATCH"
            elif py == fs:
                state = "MATCH"
            elif scenario["known_divergence"] and (py, fs) == KNOWN_PAIRS[name]:
                state = "KNOWN"
            else:
                state = "MISMATCH"
            print(f"{state:8} {scenario['name']:35} Python={py:10} F#={fs:10} {reason}")
            if state == "KNOWN":
                known += 1
                print(f"         {scenario['known_divergence']}")
            elif state == "MISMATCH":
                failures += 1
                print(f"         Expected Python={EXPECTED_PYTHON[name]} reason={expected_reason!r}"
                      f"; known pair={KNOWN_PAIRS.get(name)}")
                print("         Python output:", output.replace("\n", " | ")[:400])
    print(f"differential corpus: {len(cases())} cases, {failures} unexpected mismatch(es), {known} known divergence(s)")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
