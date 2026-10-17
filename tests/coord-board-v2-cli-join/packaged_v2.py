#!/usr/bin/env python3
"""Hosted, isolated packaged-process refusal controls; never installed adoption.

No real credential is inherited. Synthetic bindings are local decoder controls,
not enrollment or native-success evidence. Scope validation and native success remain separately unqualified here.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import stat
import subprocess
import tempfile
import time
import zipfile
import xml.etree.ElementTree as ET

SCHEMA = "fsgg.coord.packaged-v2-qualification/1"
MEMBERS = ("FS.GG.Coord.Cli.dll", "FS.GG.Coord.GitHub.dll")
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
            require(len(names) == 1, "unique package assembly required")
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
    # An open, unwritten stdin pipe detects accidental blocking reads. No test uses
    # communicate(), which would close stdin and hide that regression.
    environment = {key: os.environ[key] for key in ("PATH", "DOTNET_ROOT", "DOTNET_ROOT_X64") if key in os.environ}
    environment.update(HOME=str(home), XDG_CONFIG_HOME=str(home), DOTNET_CLI_HOME=str(home),
                       DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    with (capture / "stdout").open("w+b") as stdout, (capture / "stderr").open("w+b") as stderr:
        child = subprocess.Popen([str(engine), "board-v2", *arguments], stdin=subprocess.PIPE,
                                 stdout=stdout, stderr=stderr, cwd=home, env=environment, start_new_session=True)
        try:
            deadline = time.monotonic() + 20
            while child.poll() is None:
                require(time.monotonic() < deadline, "packaged control deadline or stdin read")
                require(os.fstat(stdout.fileno()).st_size <= MAX_OUTPUT and os.fstat(stderr.fileno()).st_size <= MAX_OUTPUT, "packaged output bound")
                time.sleep(0.02)
            stdout.seek(0)
            stderr.seek(0)
            out, err = stdout.read(MAX_OUTPUT + 1), stderr.read(MAX_OUTPUT + 1)
            require(len(out) <= MAX_OUTPUT and len(err) <= MAX_OUTPUT, "packaged output bound")
            return child.returncode, out, err
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
                child.wait(timeout=5)
            child.stdin.close()


def qualify(engine, package, source, run_id, attempt, runner=process):
    require(bool(re.fullmatch(r"[0-9a-f]{40}", source)), "exact source required")
    require(run_id.isdecimal() and int(run_id) > 0 and attempt == "1", "first-attempt hosted run required")
    package_sha = sha(package)
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
    before_install = tree(engine.parent)
    rows = []
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
                actual, out, err = runner(engine, arguments, home, capture)
            except Exception as error:
                reason = str(error) if isinstance(error, QualificationFailure) else "process acquisition unavailable"
                raise QualificationFailure(name + ": " + reason) from None
            matched = actual == code and out == b"" and (diagnostic.encode() in err if diagnostic else err == b"")
            unchanged = tree(home) == before
            rows.append({"name": name, "exitCode": actual, "stdoutBytes": len(out), "stdoutSha256": hashlib.sha256(out).hexdigest(),
                         "stderrBytes": len(err), "stderrSha256": hashlib.sha256(err).hexdigest(), "expectedRefusal": matched, "controlTreeUnchanged": unchanged})
    require(sha(package) == package_sha, "candidate package changed during controls")
    require(tree(engine.parent) == before_install, "installed candidate changed during controls")
    require(installed_identity(engine, package) == identity, "candidate identities changed during controls")
    return {"schema": SCHEMA, "sourceSha": source, "runId": run_id, "runAttempt": attempt,
            "repository": "FS-GG/.github", "workflow": ".github/workflows/release-successor-candidate.yml",
            "provenanceBasis": "workflow-supplied; authenticate retained Actions artifact separately",
            "packageId": "FS.GG.Coord.Cli", "packageVersion": versions[0], "packageSha256": package_sha, "installedIdentity": identity, "outcomes": rows,
            "passed": all(row["expectedRefusal"] and row["controlTreeUnchanged"] for row in rows), "scope": "isolated-packaged-process-local-refusals-only",
            "migrationScopeValidationQualified": False, "nativePositiveQualified": False, "activeDeveloperInstallQualified": False,
            "migrationEffectAdmission": False, "publisherAdmission": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    for flag in ("engine", "package", "output"):
        parser.add_argument("--" + flag, type=Path, required=True)
    for flag in ("source-sha", "run-id", "run-attempt"):
        parser.add_argument("--" + flag, required=True)
    args = parser.parse_args()
    require(not args.output.exists(), "new evidence path required")
    try:
        result = qualify(args.engine.absolute(), args.package, args.source_sha, args.run_id, args.run_attempt)
    except Exception as error:
        result = {"schema": SCHEMA, "passed": False, "failureClass": type(error).__name__,
                  "failureReason": str(error) if isinstance(error, QualificationFailure) else "qualification unavailable",
                  "sourceSha": args.source_sha, "runId": args.run_id, "runAttempt": args.run_attempt}
    with args.output.open("x", encoding="utf-8") as stream:
        json.dump(result, stream, sort_keys=True)
        stream.write("\n")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
