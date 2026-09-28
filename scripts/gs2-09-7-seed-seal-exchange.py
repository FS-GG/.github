#!/usr/bin/env python3
"""Bounded, sanitized artifact handoff to a separate credential-free seal job."""

from __future__ import annotations

import datetime as dt
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import time
import zipfile

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location(
    "q4_bridge_for_seal", HERE / "gs2-09-7-seed-admission-bridge.py")
if SPEC is None or SPEC.loader is None:
    raise RuntimeError("seal read bridge unavailable")
bridge = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(bridge)

SCHEMA = "fsgg.gs2-09-7.sandbox-seed-seal-exchange/1"
INPUT_FILES = ("source-manifest.json", "s2-declaration.json", "mint-grants.json",
               "seed-plan.json", "corpus.json", "prestate.json")
OUTPUT_FILES = ("bootstrap-manifest.json", "s2-declaration.json", "seed-plan.json",
                "corpus.json", "journal/state.json", "journal/tree.raw", "journal/commit.raw")
PUBLISH_FILES = ("seal-exchange-receipt.json", "prestate.json") + OUTPUT_FILES
MAX_MEMBER = 64 * 1024 * 1024
MAX_ARCHIVE = 128 * 1024 * 1024
POLL = 10
HEX40 = re.compile(r"[0-9a-f]{40}\Z")
HEX64 = re.compile(r"[0-9a-f]{64}\Z")


class Refused(ValueError):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


def canonical(value: object) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True) + "\n").encode("ascii")


def digest(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def strict_json(raw: bytes) -> dict:
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "duplicate-json-member")
            result[key] = value
        return result
    require(type(raw) is bytes and 0 < len(raw) <= MAX_MEMBER, "exchange-json-size")
    try:
        value = json.loads(raw, object_pairs_hook=unique)
    except (UnicodeError, ValueError, RecursionError) as error:
        raise Refused("exchange-json") from error
    require(type(value) is dict, "exchange-json-object")
    return value


def name(kind: str, facts: dict) -> str:
    require(kind in ("input", "output") and type(facts["runId"]) is int
            and facts["runId"] > 0 and type(facts["runAttempt"]) is int
            and facts["runAttempt"] > 0, "exchange-name")
    return f'gs2-09-7-seed-seal-{kind}-{facts["runId"]}-{facts["runAttempt"]}'


def manifest(facts: dict, files: dict[str, bytes]) -> bytes:
    require(tuple(files) == INPUT_FILES, "seal-input-files")
    require(type(facts["candidateSha"]) is str and HEX40.fullmatch(facts["candidateSha"])
            and type(facts["workflowSha"]) is str and HEX40.fullmatch(facts["workflowSha"])
            and type(facts["manifestSha256"]) is str and HEX64.fullmatch(facts["manifestSha256"]),
            "seal-input-run")
    rows = []
    for path, raw in files.items():
        require(type(raw) is bytes and 0 < len(raw) <= MAX_MEMBER, "seal-input-file")
        rows.append({"path": path, "byteLength": len(raw), "sha256": digest(raw)})
    return canonical({"schema": SCHEMA, "status": "source-only-no-authority",
                      "candidateSha": facts["candidateSha"],
                      "workflowSha": facts["workflowSha"],
                      "runId": facts["runId"], "runAttempt": facts["runAttempt"],
                      "runNonce": facts["runNonce"],
                      "sourceManifestSha256": facts["manifestSha256"],
                      "files": rows, "credentialPresent": False,
                      "bootstrapAuthority": False})


def validate_manifest(raw: bytes, facts: dict, files: dict[str, bytes]) -> None:
    value = strict_json(raw)
    require(raw == canonical(value) and set(value) == {
        "schema", "status", "candidateSha", "workflowSha", "runId", "runAttempt",
        "runNonce", "sourceManifestSha256", "files", "credentialPresent",
        "bootstrapAuthority"}, "seal-input-manifest")
    require(value == strict_json(manifest(facts, files)), "seal-input-manifest-drift")


def safe_archive(raw: bytes, expected: tuple[str, ...]) -> dict[str, bytes]:
    require(type(raw) is bytes and 0 < len(raw) <= MAX_ARCHIVE, "seal-archive-size")
    try:
        with zipfile.ZipFile(io.BytesIO(raw)) as bundle:
            members = bundle.infolist()
            require(len(members) == len(expected) and
                    {item.filename for item in members} == set(expected),
                    "seal-archive-members")
            result = {}
            for item in members:
                mode = (item.external_attr >> 16) & 0xFFFF
                require(not item.is_dir() and not stat.S_ISLNK(mode)
                        and 0 < item.file_size <= MAX_MEMBER, "seal-archive-member")
                value = bundle.read(item)
                require(len(value) == item.file_size, "seal-archive-member-size")
                result[item.filename] = value
            return result
    except (OSError, zipfile.BadZipFile, RuntimeError) as error:
        raise Refused("seal-archive-unavailable") from error


def artifact(port, kind: str, facts: dict) -> dict | None:
    target = name(kind, facts)
    listing = bridge.get_json(port, f"{bridge.REPO}/actions/artifacts?name={target}&per_page=100")
    require(type(listing) is dict and type(listing.get("total_count")) is int
            and type(listing.get("artifacts")) is list
            and listing["total_count"] == len(listing["artifacts"])
            and listing["total_count"] <= 1, "seal-artifact-unique")
    if listing["total_count"] == 0:
        return None
    item = listing["artifacts"][0]
    require(type(item) is dict, "seal-artifact-binding")
    binding = item.get("workflow_run") or {}
    require(type(binding) is dict and item.get("name") == target
            and type(item.get("id")) is int and item["id"] > 0
            and item.get("expired") is False
            and type(item.get("digest")) is str
            and bridge.ARCHIVE_DIGEST.fullmatch(item["digest"]) is not None
            and binding.get("id") == facts["runId"]
            and binding.get("head_sha") == facts["workflowSha"]
            and binding.get("head_branch") == "main"
            and binding.get("repository_id") == bridge.authorizer.REPOSITORY_ID
            and binding.get("head_repository_id") == bridge.authorizer.REPOSITORY_ID,
            "seal-artifact-binding")
    return item


def native_job_state(port, facts: dict) -> str:
    listing = bridge.get_json(
        port, f'{bridge.REPO}/actions/runs/{facts["runId"]}/attempts/'
              f'{facts["runAttempt"]}/jobs?per_page=100')
    require(type(listing) is dict and type(listing.get("total_count")) is int
            and type(listing.get("jobs")) is list
            and listing["total_count"] == len(listing["jobs"]), "seal-jobs-page")
    matches = [job for job in listing["jobs"] if type(job) is dict
               and job.get("name") == "gs2-09-7-seed-seal"]
    require(len(matches) <= 1, "seal-job-unique")
    if not matches:
        return "pending"
    job = matches[0]
    require(job.get("run_id") == facts["runId"]
            and job.get("head_sha") == facts["workflowSha"], "seal-job-binding")
    if job.get("status") == "completed":
        require(job.get("conclusion") == "success", "seal-job-failed")
        return "success"
    require(job.get("status") in {"queued", "in_progress", "waiting", "requested", "pending"}
            and job.get("conclusion") is None, "seal-job-status")
    return "pending"


def download(port, item: dict, expected: tuple[str, ...]) -> dict[str, bytes]:
    raw = port.get(f'{bridge.REPO}/actions/artifacts/{item["id"]}/zip', limit=MAX_ARCHIVE)
    require("sha256:" + digest(raw) == item["digest"], "seal-archive-digest")
    return safe_archive(raw, expected)


def wait(port, kind: str, facts: dict, seconds: int, *, clock=time.monotonic,
         pause=time.sleep, require_job: bool = False) -> tuple[dict[str, bytes], dict]:
    require(type(seconds) is int and 0 < seconds <= 35 * 60, "seal-wait-bound")
    deadline = clock() + seconds
    observed = None
    while True:
        item = artifact(port, kind, facts)
        state = native_job_state(port, facts) if require_job else "success"
        if item is not None:
            identity = (item["id"], item["digest"])
            if observed is None:
                observed = identity
            require(identity == observed, "seal-artifact-drift")
            if state == "success":
                files = (("seal-input-manifest.json",) + INPUT_FILES) if kind == "input" else PUBLISH_FILES
                return download(port, item, files), item
        elif observed is not None:
            raise Refused("seal-artifact-disappeared")
        left = deadline - clock()
        require(left > 0, "seal-wait-timeout")
        pause(min(POLL, left))


def run_seal(facts: dict, port, candidate: Path, output: Path) -> None:
    """Run only on the separate runner, after checking its exact source input."""
    require(os.environ.get("FSGG_DISPATCH_APP_PRIVATE_KEY") is None
            and os.environ.get("FSGG_SANDBOX_TOKEN") is None,
            "seal-runner-credential")
    inputs, input_artifact = wait(port, "input", facts, 20 * 60)
    sealed_manifest = inputs.pop("seal-input-manifest.json")
    ordered = {path: inputs[path] for path in INPUT_FILES}
    validate_manifest(sealed_manifest, facts, ordered)
    require(digest(ordered["source-manifest.json"]) == facts["manifestSha256"],
            "seal-source-digest")
    from importlib.util import module_from_spec, spec_from_file_location
    spec = spec_from_file_location("q4_entry_for_seal", HERE / "gs2-09-7-seed-bootstrap-entry.py")
    entry = module_from_spec(spec)
    spec.loader.exec_module(entry)
    require(entry.INSTALLATION_STATUS == "installed-protected-bootstrap",
            "seal-source-uninstalled")
    source, retained = entry.validate_source_artifacts(candidate, facts)
    require(ordered["source-manifest.json"] ==
            (candidate / entry.SOURCE_MANIFEST).read_bytes()
            and ordered["seed-plan.json"] == retained["seed-plan.json"]
            and ordered["corpus.json"] == retained["corpus.json"],
            "seal-candidate-source-drift")
    require(not output.exists(), "seal-output-exists")
    output.mkdir(mode=0o700, parents=True)
    for path, raw in ordered.items():
        target = output / path
        descriptor = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(raw)
    result = output / "derived"
    command = ["dotnet", "run", "--project", "src/FS.GG.Coordination.Cli/FS.GG.Coordination.Cli.fsproj", "--",
               "seed-bootstrap-artifacts", "seal", "--candidate-sha", facts["candidateSha"],
               "--workflow-run-id", str(facts["runId"]),
               "--workflow-run-attempt", str(facts["runAttempt"]),
               "--workflow-sha", facts["workflowSha"],
               "--source-manifest", str(output / "source-manifest.json"),
               "--s2-declaration", str(output / "s2-declaration.json"),
               "--mint-proof", str(output / "mint-grants.json"),
               "--seed-plan", str(output / "seed-plan.json"),
               "--corpus", str(output / "corpus.json"),
               "--prestate", str(output / "prestate.json"),
               "--output-dir", str(result)]
    allowed = {"PATH", "HOME", "DOTNET_ROOT", "DOTNET_CLI_HOME", "NUGET_PACKAGES", "LANG", "LC_ALL", "CI"}
    clean = {key: value for key, value in os.environ.items() if key in allowed}
    completed = subprocess.run(command, cwd=candidate, env=clean, stdout=subprocess.PIPE,
                               stderr=subprocess.PIPE, timeout=180, check=False)
    require(completed.returncode == 0
            and completed.stdout.strip() == str(result / "bootstrap-manifest.json").encode(),
            "coordination-seal-refused")
    entry.validate_artifacts(result, facts)
    for path in OUTPUT_FILES:
        require((result / path).is_file() and not (result / path).is_symlink(), "seal-output-file")
    publish = output / "publish"
    publish.mkdir(mode=0o700)
    receipt = {"schema": SCHEMA, "status": "source-only-no-authority",
               "candidateSha": facts["candidateSha"], "workflowSha": facts["workflowSha"],
               "runId": facts["runId"], "runAttempt": facts["runAttempt"],
               "inputArtifactId": input_artifact["id"],
               "inputArchiveDigest": input_artifact["digest"],
               "inputManifestSha256": digest(sealed_manifest),
               "s2DeclarationSha256": digest(ordered["s2-declaration.json"]),
               "seedPlanSha256": digest(ordered["seed-plan.json"]),
               "corpusSha256": digest(ordered["corpus.json"]),
               "prestateSha256": digest(ordered["prestate.json"]),
               "bootstrapAuthority": False}
    (publish / "seal-exchange-receipt.json").write_bytes(canonical(receipt))
    (publish / "prestate.json").write_bytes(ordered["prestate.json"])
    for path in OUTPUT_FILES:
        target = publish / path
        target.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
        target.write_bytes((result / path).read_bytes())
    print(str(publish))


def main(argv: list[str]) -> int:
    try:
        require(argv == ["run-seal"], "seal-mode")
        entry_spec = importlib.util.spec_from_file_location(
            "q4_entry_for_seal_context", HERE / "gs2-09-7-seed-bootstrap-entry.py")
        entry = importlib.util.module_from_spec(entry_spec)
        entry_spec.loader.exec_module(entry)
        facts = entry.context(dict(os.environ))
        require(entry.INSTALLATION_STATUS == "installed-protected-bootstrap",
                "seal-source-uninstalled")
        require(Path("host").resolve().is_dir() and Path("coordination").resolve().is_dir(),
                "seal-checkout")
        require(subprocess.check_output(["git", "-C", "host", "rev-parse", "HEAD"],
                                        stderr=subprocess.DEVNULL).decode().strip()
                == facts["workflowSha"], "seal-host-head")
        require(subprocess.check_output(["git", "-C", "coordination", "rev-parse", "HEAD"],
                                        stderr=subprocess.DEVNULL).decode().strip()
                == facts["candidateSha"], "seal-candidate-head")
        port = bridge.GitHubReadPort(os.environ["GH_TOKEN"])
        output = Path(os.environ["FSGG_SEED_SEAL_OUTPUT_DIR"])
        require(output.is_absolute(), "seal-output-path")
        run_seal(facts, port, Path("coordination").resolve(), output)
        return 0
    except (Refused, OSError, KeyError, TypeError, ValueError, subprocess.SubprocessError) as error:
        print(f"GS2-SEED-SEAL: refused {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
