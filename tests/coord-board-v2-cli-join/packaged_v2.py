#!/usr/bin/env python3
"""Hosted, isolated packaged-process refusal controls; never installed adoption.

No real credential is inherited. Synthetic bindings are local decoder controls,
not enrollment or native-success evidence. Scope validation and native success remain separately unqualified here.
"""
from __future__ import annotations

import argparse
import base64
from contextlib import ExitStack
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import stat
import subprocess
import sys
import tempfile
import time
import zipfile
import xml.etree.ElementTree as ET

SCHEMA = "fsgg.coord.packaged-v2-qualification/1"
MEMBERS = ("fsgg-coord-engine.dll", "FS.GG.Coord.GitHub.dll")
MAX_OUTPUT = 32768


class QualificationFailure(ValueError):
    """A closed diagnostic written by this helper, never native output."""


def require(condition, message):
    if not condition:
        raise QualificationFailure(message)


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def tree(root):
    result = {}
    for path in sorted(root.rglob("*")):
        mode = path.lstat().st_mode
        require(stat.S_ISREG(mode) or stat.S_ISDIR(mode), "unexpected control-tree object")
        result[str(path.relative_to(root))] = (stat.S_IMODE(mode), sha(path) if path.is_file() else None)
    return result


def installed_identity(engine, package):
    require(engine.is_file() and not engine.is_symlink() and os.access(engine, os.X_OK), "regular executable installed shim required")
    root = engine.parent
    require(package.stat().st_size <= 256 * 1024 * 1024, "candidate package bound")
    with zipfile.ZipFile(package) as archive:
        require(len(archive.infolist()) <= 512, "candidate member bound")
        result = {}
        for member in MEMBERS:
            names = [name for name in archive.namelist() if name.startswith("tools/net10.0/any/") and name.endswith("/" + member)]
            if len(names) != 1:
                # Bound the actual package-layout diagnostic instead of hiding the
                # failed identity behind a zero-case generic refusal. Never aliases.
                candidates = [name for name in archive.namelist()
                              if name.startswith("tools/net10.0/any/") and name.lower().endswith(".dll")]
                failure = QualificationFailure("unique package assembly required")
                failure.package_inventory = {"requiredAssembly": member, "matchingEntries": len(names),
                    "observedDllEntries": [name if re.fullmatch(r"[A-Za-z0-9._/-]{1,160}", name) else "<nonliteral-member>"
                                           for name in candidates[:8]],
                    "omittedDllEntries": max(0, len(candidates) - 8)}
                raise failure
            require(archive.getinfo(names[0]).file_size <= 128 * 1024 * 1024, "candidate assembly bound")
            expected = hashlib.sha256(archive.read(names[0])).hexdigest()
            paths = list(root.rglob(member))
            require(len(paths) == 1 and not paths[0].is_symlink() and paths[0].is_file(), "unique installed assembly required")
            require(sha(paths[0]) == expected, "installed assembly differs from candidate")
            result[member] = {"packageMember": names[0], "installedRelativePath": str(paths[0].relative_to(root)), "sha256": expected}
    return {"shimSha256": sha(engine), "assemblies": result,
            "basis": "exact-package-members-and-invoked-installed-shim; adapter-also-checked-by-binding-refusal"}


def binding(adapter_sha):
    return {"bindingVersion": 3, "schemaVersion": 1, "importRecipeRevision": "e" * 40,
            "importArtifactSha256": "f" * 64, "selectedIssues": {"I_synthetic": "ExampleOrg/Widget#1"},
            "recipeRevision": "a" * 40, "populationRevision": "c" * 40, "organizationId": "O_synthetic",
            "artifactSha256": adapter_sha, "ownerKind": "organization", "owner": "ExampleOrg",
            "projectNumber": 77, "projectTitle": "Synthetic product V2", "projectId": "PVT_synthetic_product",
            "status": {"id": "status", "options": dict(zip(["Backlog", "Ready", "In progress", "Blocked", "Done"], "brixd"))},
            "roadmapFieldId": "roadmap", "track": {"id": "track", "options": {"Active delivery": "a", "Follow-up": "f"}},
            "observation": {"id": "observation", "options": {"Verified": "v", "Stale": "s", "Unknown": "u"}},
            "repositories": ["ExampleOrg/Widget"]}


def process(engine, arguments, home, capture):
    # WNOWAIT observes exit without releasing the root PID that pins its group.
    # Never call poll()/wait() before the single owned-group cleanup signal.
    require(sys.platform == "linux", "owned-group qualification is Linux-only")
    require(all(hasattr(os, name) for name in ("waitid", "WNOWAIT", "WEXITED", "WNOHANG", "P_PID")),
            "non-reaping process observation unavailable")
    environment = {key: os.environ[key] for key in ("PATH", "DOTNET_ROOT", "DOTNET_ROOT_X64") if key in os.environ}
    environment.update(HOME=str(home), XDG_CONFIG_HOME=str(home), DOTNET_CLI_HOME=str(home),
                       DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    result = {"exitCode": None, "stdout": b"", "stderr": b"", "firstFailure": None,
              "cleanup": {"status": "not-started", "failures": []}, "outputComplete": False, "observedBytes": {"stdout": 0, "stderr": 0}}
    def close_capture(stream):
        try:
            stream.close()
        except Exception:
            result["cleanup"]["failures"].append("capture-close-unavailable")
            result["cleanup"]["status"] = "Unknown"
            if result["firstFailure"] is None:
                result["firstFailure"] = "capture-close-unavailable"
    with ExitStack() as files:
        stdout = (capture / "stdout").open("w+b")
        files.callback(close_capture, stdout)
        stderr = (capture / "stderr").open("w+b")
        files.callback(close_capture, stderr)
        child = None
        custody = True
        deadline = time.monotonic() + 20
        try:
            # Stdin remains open and unwritten until cleanup; EOF would hide reads.
            child = subprocess.Popen([str(engine), "board-v2", *arguments], stdin=subprocess.PIPE,
                                     stdout=stdout, stderr=stderr, cwd=home, env=environment, start_new_session=True)
            while True:
                require(time.monotonic() < deadline, "packaged control deadline or stdin read")
                require(os.fstat(stdout.fileno()).st_size <= MAX_OUTPUT and os.fstat(stderr.fileno()).st_size <= MAX_OUTPUT,
                        "packaged output bound")
                observed = os.waitid(os.P_PID, child.pid, os.WEXITED | os.WNOHANG | os.WNOWAIT)
                if observed is not None:
                    break
                time.sleep(0.02)
        except Exception as error:
            if isinstance(error, ChildProcessError):
                custody = False
            result["firstFailure"] = str(error) if isinstance(error, QualificationFailure) else "process-acquisition-unavailable"
        finally:
            cleanup = result["cleanup"]
            if child is None:
                cleanup["status"] = "Unknown"
                cleanup["failures"].append("spawn-custody-not-returned")
            else:
                cleanup_deadline = time.monotonic() + 5
                if custody:
                    try:
                        # Root is still unreaped, including after its observed exit.
                        os.killpg(child.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                    except Exception:
                        cleanup["failures"].append("owned-group-signal-unavailable")
                else:
                    cleanup["failures"].append("root-custody-lost-no-signal")
                try:
                    remaining = cleanup_deadline - time.monotonic()
                    require(remaining > 0, "cleanup deadline")
                    result["exitCode"] = child.wait(timeout=remaining)
                except Exception:
                    cleanup["failures"].append("root-reap-unconfirmed")
                # Only read-only probes after reaping: never signal a reused PID.
                absent = False
                if custody and result["exitCode"] is not None:
                    try:
                        while time.monotonic() < cleanup_deadline:
                            try:
                                os.killpg(child.pid, 0)
                            except ProcessLookupError:
                                absent = True
                                break
                            time.sleep(0.02)
                    except Exception:
                        cleanup["failures"].append("group-readback-unavailable")
                if not absent:
                    cleanup["failures"].append("group-absence-unconfirmed")
                try:
                    child.stdin.close()
                except Exception:
                    cleanup["failures"].append("stdin-close-unavailable")
                cleanup["status"] = "owned-group-absent" if absent and not cleanup["failures"] else "Unknown"
            # Preserve original bounded bytes even after timeout/overflow/cleanup failure.
            lengths = []
            for name, stream in (("stdout", stdout), ("stderr", stderr)):
                try:
                    size = os.fstat(stream.fileno()).st_size
                    stream.seek(0)
                    result[name] = stream.read(MAX_OUTPUT)
                    after_size = os.fstat(stream.fileno()).st_size
                    result["observedBytes"][name] = max(size, after_size)
                    lengths.append(size <= MAX_OUTPUT and after_size == size)
                except Exception:
                    lengths.append(False)
                    if result["firstFailure"] is None:
                        result["firstFailure"] = "output-acquisition-unavailable"
            result["outputComplete"] = all(lengths) and cleanup["status"] in ("owned-group-absent", "not-started")
            if not all(lengths) and result["firstFailure"] is None:
                result["firstFailure"] = "bounded-output-incomplete"
    return result


def _qualify(engine, package, source, run_id, attempt, runner, evidence):
    require(bool(re.fullmatch(r"[0-9a-f]{40}", source)), "exact source required")
    require(run_id.isdecimal() and int(run_id) > 0 and attempt == "1", "first-attempt hosted run required")
    package_sha = sha(package)
    evidence["packageSha256"] = package_sha
    with zipfile.ZipFile(package) as archive:
        nuspecs = [name for name in archive.namelist() if name.endswith(".nuspec")]
        require(len(nuspecs) == 1, "unique package metadata required")
        metadata = ET.fromstring(archive.read(nuspecs[0]))
        repositories = [node for node in metadata.iter() if node.tag.rsplit("}", 1)[-1] == "repository"]
        versions = [node.text for node in metadata.iter() if node.tag.rsplit("}", 1)[-1] == "version"]
        require(len(versions) == 1 and bool(re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", versions[0] or "")), "stable candidate package version required")
        ids = [node.text for node in metadata.iter() if node.tag.rsplit("}", 1)[-1] == "id"]
        require(ids == ["FS.GG.Coord.Cli"] and len(repositories) == 1 and repositories[0].get("commit") == source,
                "package metadata does not bind selected source")
    identity = installed_identity(engine, package)
    evidence.update(packageId="FS.GG.Coord.Cli", packageVersion=versions[0], installedIdentity=identity)
    before_install = tree(engine.parent)
    rows = evidence["outcomes"]
    with tempfile.TemporaryDirectory(prefix="packaged-v2-") as temporary:
        root = Path(temporary)
        home, capture = root / "home", root / "capture"
        home.mkdir(mode=0o700)
        capture.mkdir()
        product = binding(identity["assemblies"][MEMBERS[1]]["sha256"])
        cases = [
            ("missing-binding", None, "inspect", "could not read refresh input", False, 1),
            ("malformed-binding", "{}", "inspect", "unknown, missing or duplicate properties", False, 1),
            ("product-missing-credential", product, "inspect", "read-only V2 inspection token", False, 1),
            ("product-stdin", product, "inspect", "integrator stdin is restricted to organization binding 4 inspection", True, 1),
            ("adapter-mismatch", dict(product, artifactSha256="0" * 64), "inspect", "selected binding does not match the loaded adapter assembly SHA-256", True, 1),
            ("old-schema", dict(product, bindingVersion=4), "inspect", "binding and schema versions disagree", False, 1),
            ("old-p3-product", dict(product, owner="FS-GG", projectNumber=3, projectId="PVT_kwDOEYAWY84Bldpa"), "inspect", "repository binding cannot select the organization", False, 1),
            ("refresh-stdin", product, "refresh", "integrator observations are read-only organization inspect input", True, 1),
            ("migration-syntax", None, "migration-prepare", "", False, 1),
        ]
        for name, value, command, diagnostic, stdin, code in cases:
            path, report = home / "input.json", home / "report.json"
            if path.exists():
                path.unlink()
            if value is not None:
                path.write_text(value if isinstance(value, str) else json.dumps(value), encoding="utf-8")
                path.chmod(0o600)
            if name == "migration-syntax":
                arguments = [command, "--unknown", "value"]
            else:
                arguments = [command, "--binding-file", str(path), "--report-file", str(report)] + (["--integrator-stdin"] if stdin else [])
            before = tree(home)
            try:
                outcome = runner(engine, arguments, home, capture)
            except Exception:
                outcome = {"exitCode": None, "stdout": b"", "stderr": b"", "firstFailure": "process-acquisition-unavailable",
                           "cleanup": {"status": "Unknown", "failures": ["runner-did-not-return-custody"]}, "outputComplete": False, "observedBytes": {"stdout": 0, "stderr": 0}}
            actual, out, err = outcome["exitCode"], outcome["stdout"], outcome["stderr"]
            matched = outcome["outputComplete"] and actual == code and out == b"" and (diagnostic.encode() in err if diagnostic else err == b"")
            row = {"name": name, "exitCode": actual, "expectedRefusal": matched, "controlTreeUnchanged": None,
                   "firstFailure": outcome["firstFailure"], "cleanup": outcome["cleanup"], "outputComplete": outcome["outputComplete"]}
            for stream, raw in (("stdout", out), ("stderr", err)):
                row[stream + "ObservedBytes"] = outcome["observedBytes"][stream]
                row[stream + "RetainedBytes"] = len(raw)
                row[stream + "OmittedObservedBytes"] = max(0, outcome["observedBytes"][stream] - len(raw))
                row[stream + "RetainedSha256"] = hashlib.sha256(raw).hexdigest()
                row[stream + "RetainedBase64"] = base64.b64encode(raw).decode("ascii")
            rows.append(row)
            try:
                unchanged = tree(home) == before
                row["controlTreeUnchanged"] = unchanged
            except Exception:
                unchanged = False
                row["firstFailure"] = row["firstFailure"] or "control-tree-readback-unavailable"
            failure = row["firstFailure"]
            if failure is None and outcome["cleanup"]["status"] != "owned-group-absent":
                failure = "cleanup-unconfirmed"
            if failure is None and not outcome["outputComplete"]:
                failure = "bounded-output-incomplete"
            if failure is None and not (matched and unchanged):
                failure = "refusal-or-control-tree-differs"
            if failure and evidence["firstFailure"] is None:
                evidence["firstFailure"] = {"case": name, "reason": failure}
            if row["firstFailure"] or outcome["cleanup"]["status"] != "owned-group-absent":
                if evidence["firstFailure"] is None:
                    evidence["firstFailure"] = {"case": name, "reason": "cleanup-unconfirmed"}
                break
    require(sha(package) == package_sha, "candidate package changed during controls")
    require(tree(engine.parent) == before_install, "installed candidate changed during controls")
    require(installed_identity(engine, package) == identity, "candidate identities changed during controls")
    evidence["passed"] = len(rows) == len(cases) and evidence["firstFailure"] is None
    return evidence


def qualify(engine, package, source, run_id, attempt, runner=process):
    evidence = {"schema": SCHEMA, "sourceSha": source, "runId": run_id, "runAttempt": attempt,
                "repository": "FS-GG/.github", "workflow": ".github/workflows/release-successor-candidate.yml",
                "provenanceBasis": "workflow-supplied; authenticate retained Actions artifact separately",
                "passed": False, "outcomes": [], "firstFailure": None,
                "scope": "isolated-packaged-process-local-refusals-only; owned-process-group-cleanup",
                "escapedProcessTreeQualified": False, "migrationScopeValidationQualified": False,
                "nativePositiveQualified": False, "activeDeveloperInstallQualified": False,
                "migrationEffectAdmission": False, "publisherAdmission": False}
    try:
        return _qualify(engine, package, source, run_id, attempt, runner, evidence)
    except Exception as error:
        evidence["failureClass"] = type(error).__name__
        if isinstance(error, QualificationFailure) and hasattr(error, "package_inventory"):
            evidence["packageAssemblyInventory"] = error.package_inventory
        if evidence["firstFailure"] is None:
            evidence["firstFailure"] = {"case": None, "reason": str(error) if isinstance(error, QualificationFailure) else "qualification-unavailable"}
        return evidence


def main():
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument("--installed-cli", type=Path, required=True)
    for flag in ("package", "output"):
        parser.add_argument("--" + flag, type=Path, required=True)
    for flag in ("source-sha", "run-id", "run-attempt"):
        parser.add_argument("--" + flag, required=True)
    args = parser.parse_args()
    require(not args.output.exists(), "new evidence path required")
    try:
        result = qualify(args.installed_cli.absolute(), args.package, args.source_sha, args.run_id, args.run_attempt)
    except Exception as error:
        result = {"schema": SCHEMA, "passed": False, "failureClass": type(error).__name__,
                  "failureReason": str(error) if isinstance(error, QualificationFailure) else "qualification unavailable",
                  "sourceSha": args.source_sha, "runId": args.run_id, "runAttempt": args.run_attempt}
    try:
        with args.output.open("x", encoding="utf-8") as stream:
            json.dump(result, stream, sort_keys=True)
            stream.write("\n")
    except OSError:
        # Reporting failure does not replace the original acquisition/cleanup cause.
        print("packaged-v2: evidence-write-unavailable; qualification not accepted", file=sys.stderr)
        return 1
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
