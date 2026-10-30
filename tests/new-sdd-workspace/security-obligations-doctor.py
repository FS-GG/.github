#!/usr/bin/env python3
"""Causal controls: the Wizard's default-coordination state must leave `fsgg-sdd doctor` coherent
(FS-GG/.github#4964, under #3010).

The validator is the REAL public SDD CLI (2.5.3 required), not a model of its schema. The workspace
provenance is real SDD 2.5.3 output (fixtures/sdd-2.5.3-go-cli-scaffold-provenance.json: a go-cli creation
by the public Wizard with the two Wizard members removed). The discriminating diagnostic is
`scaffold.provenanceMalformed`; the reduced fixture has no seeded artifacts, so `doctor.driftDetected` is
present in every case and is not the signal here.

  A  OLD SHAPE   the members Wizard 0.23.0 wrote into the SDD-owned provenance => the real doctor rejects
                 them (the control: the validator discriminates, so B and C are not vacuous).
  B  NEW WRITER  the compiled production writer (recordSecurityObligations) => the same fixture stays
                 byte-identical, the obligations file carries the state, and the real doctor accepts it.
  C  MIGRATION   a workspace already carrying the legacy members: the real doctor rejects it; the next
                 Wizard run (`secure`, the resume route, with a gh double) migrates it, resumes the
                 obligations from the new file and the real doctor accepts it.

usage: security-obligations-doctor.py --dll PATH/new-sdd-workspace.dll [--sdd-cli PATH/fsgg-sdd]
"""
from __future__ import annotations

import argparse
import json
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile

HERE = pathlib.Path(__file__).resolve().parent
FIXTURE = HERE / "fixtures/sdd-2.5.3-go-cli-scaffold-provenance.json"
REQUIRED_SDD = "2.5.3"
failures: list[str] = []


def check(name: str, ok: bool, detail: str = "") -> None:
    print(("PASS  " if ok else "FAIL  ") + name)
    if not ok:
        failures.append(name)
        if detail:
            print("    | " + detail[-1500:].replace("\n", "\n    | "))


def legacy_members() -> dict:
    """The content Wizard 0.23.0 wrote at step 5b: a pending repository obligation (here for a named
    repository, so the resume can match it) and a pending Project obligation, under the
    `securityObligations` member of the SDD-owned provenance."""
    return {"securityObligations": [
        {"kind": "repository-issue-policy", "target": "acme/app", "targetPolicy": "COLLABORATORS_ONLY",
         "resume": "new-sdd-workspace secure '/w/ws' '--repo' 'acme/app'", "state": "pending"},
        {"kind": "project-access", "target": "FS-GG/Coordination", "requestedVisibility": "preserve",
         "expectedBasePermission": "READ", "trustedWriters": [],
         "humanVerification": "Project → Settings → Manage access; verify base permission Read and that the effective/exclusive Write actor set exactly equals the explicit trusted writer allowlist.",
         "resume": "new-sdd-workspace secure '/w/ws' '--project' 'FS-GG/Coordination' '--trusted-writers' ''",
         "state": "pending-human-verification"}]}


def workspace(root: pathlib.Path, with_legacy: bool) -> pathlib.Path:
    ws = root / "ws"
    (ws / ".fsgg").mkdir(parents=True)
    provenance = json.loads(FIXTURE.read_text())
    if with_legacy:
        provenance.update(legacy_members())
    (ws / ".fsgg/scaffold-provenance.json").write_text(json.dumps(provenance, separators=(",", ":"), ensure_ascii=False))
    return ws


def doctor(sdd: str, ws: pathlib.Path) -> dict:
    run = subprocess.run([sdd, "doctor", "--root", str(ws)], capture_output=True, text=True, timeout=120, cwd=ws)
    return json.loads(run.stdout)


def malformed(report: dict) -> list[str]:
    return [d["message"] for d in report["diagnostics"] if d["id"] == "scaffold.provenanceMalformed"]


def env_for(root: pathlib.Path, extra: dict[str, str] | None = None) -> dict[str, str]:
    home = root / "home"
    home.mkdir(exist_ok=True)
    env = {"PATH": os.environ["PATH"], "HOME": str(home), "DOTNET_CLI_HOME": str(home), "DOTNET_NOLOGO": "1", "NO_COLOR": "1",
           "DOTNET_ROOT": os.environ.get("DOTNET_ROOT", "/usr/share/dotnet"), "TMPDIR": str(root)}
    env.update(extra or {})
    return env


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dll", required=True)
    parser.add_argument("--sdd-cli", default=shutil.which("fsgg-sdd"))
    args = parser.parse_args()
    sdd, dll = args.sdd_cli, args.dll
    if not sdd:
        print("FAIL  the real public fsgg-sdd is required")
        return 1
    version = subprocess.run([sdd, "--version"], capture_output=True, text=True).stdout.strip()
    check(f"the validator is the real public SDD CLI {REQUIRED_SDD}", version == REQUIRED_SDD, version)

    with tempfile.TemporaryDirectory(prefix="obligations-doctor-") as tmp:
        tmp_path = pathlib.Path(tmp)

        # A · old shape
        root = tmp_path / "a"; root.mkdir()
        ws = workspace(root, with_legacy=True)
        report = doctor(sdd, ws)
        check("A: the real SDD doctor rejects the Wizard 0.23.0 provenance shape (Unknown property)",
              any("Unknown property" in m for m in malformed(report)) and report["doctor"]["hasProvenance"], json.dumps(report["diagnostics"])[:600])
        root = tmp_path / "a0"; root.mkdir()
        clean = workspace(root, with_legacy=False)
        check("A: the same provenance without the Wizard members is accepted (the members are the cause)",
              malformed(doctor(sdd, clean)) == [])

        # B · the new production writer
        root = tmp_path / "b"; root.mkdir()
        ws = workspace(root, with_legacy=False)
        before = (ws / ".fsgg/scaffold-provenance.json").read_bytes()
        run = subprocess.run(["dotnet", "fsi", f"--reference:{dll}", str(HERE / "security-obligations-record.fsx")],
                             env=env_for(root, {"KIT_TARGET": str(ws)}), capture_output=True, text=True, timeout=600)
        check("B: the compiled production writer ran", "RECORDED" in run.stdout, run.stdout + run.stderr)
        check("B: SDD-owned provenance is byte-identical after the Wizard step", (ws / ".fsgg/scaffold-provenance.json").read_bytes() == before)
        state = json.loads((ws / ".fsgg/security-obligations.json").read_text())
        kinds = {row["kind"]: row for row in state["securityObligations"]}
        check("B: the pending state, target, policy, human-verification text and resume commands are in the Wizard-owned file",
              kinds["repository-issue-policy"]["state"] == "pending" and kinds["repository-issue-policy"]["targetPolicy"] == "COLLABORATORS_ONLY"
              and kinds["project-access"]["state"] == "pending-human-verification" and "Manage access" in kinds["project-access"]["humanVerification"]
              and kinds["project-access"]["resume"].startswith("new-sdd-workspace secure ") and state["schema"] == "fsgg.new-sdd-workspace-security-obligations/1")
        check("B: the real SDD doctor accepts the workspace the new writer produced", malformed(doctor(sdd, ws)) == [], json.dumps(doctor(sdd, ws)["diagnostics"])[:600])

        # C · migration and resume through the production `secure` route
        root = tmp_path / "c"; root.mkdir()
        ws = workspace(root, with_legacy=True)
        check("C: the legacy workspace is rejected before the next Wizard run", bool(malformed(doctor(sdd, ws))))
        fake = root / "bin"; fake.mkdir()
        gh = fake / "gh"
        gh.write_text("#!/usr/bin/env bash\ncase \" $* \" in\n  *'mutation('* ) printf '%s\\n' '{\"data\":{\"updateRepository\":{\"repository\":{\"issueCreationPolicy\":\"COLLABORATORS_ONLY\"}}}}' ;;\n"
                      "  * ) printf '%s\\n' '{\"data\":{\"viewer\":{\"login\":\"fixture\"},\"repository\":{\"id\":\"R_1\",\"issueCreationPolicy\":\"COLLABORATORS_ONLY\"}}}' ;;\nesac\n")
        gh.chmod(0o755)
        dotnet_dir = str(pathlib.Path(shutil.which("dotnet")).parent)
        env = env_for(root)
        env["PATH"] = f"{fake}:{dotnet_dir}:/usr/bin:/bin"
        resume = subprocess.run(["dotnet", dll, "secure", str(ws), "--repo", "acme/app"], env=env, capture_output=True, text=True, timeout=300)
        check("C: the resume command succeeded", resume.returncode == 0 and "verified:" in resume.stdout, resume.stdout + resume.stderr)
        check("C: the next Wizard run announced the migration", "moved 2 security obligation(s)" in resume.stdout.replace("\n", " ") or "moved 2 security" in resume.stdout, resume.stdout)
        provenance = json.loads((ws / ".fsgg/scaffold-provenance.json").read_text())
        check("C: the legacy members are gone from the SDD-owned provenance and every SDD member is intact",
              "securityObligations" not in provenance and "verifiedSecurityReceipts" not in provenance
              and provenance == json.loads(FIXTURE.read_text()))
        state = json.loads((ws / ".fsgg/security-obligations.json").read_text())
        check("C: the matching repository obligation was cleared by the resume and the receipt persisted; the Project obligation survived",
              not any(r["kind"] == "repository-issue-policy" for r in state["securityObligations"])
              and any(r["kind"] == "project-access" and r["state"] == "pending-human-verification" for r in state["securityObligations"])
              and any(r["kind"] == "repository-issue-policy" and r["finalPolicy"] == "COLLABORATORS_ONLY" for r in state["verifiedSecurityReceipts"]),
              json.dumps(state)[:800])
        check("C: the real SDD doctor accepts the migrated workspace", malformed(doctor(sdd, ws)) == [])

    print(f"\n{len(failures)} failure(s)")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
