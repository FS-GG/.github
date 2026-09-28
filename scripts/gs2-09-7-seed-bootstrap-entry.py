#!/usr/bin/env python3
"""Fail-closed protected Q4 journal bootstrap credential-host entry point.

Independent admission and the separate credential-free sealer remain explicit
installation boundaries. The host never executes Coordination candidate code.
"""

from __future__ import annotations

import hashlib
import importlib.util
import json
import os
import re
import sys
import datetime as dt
import tempfile
import stat
from contextlib import contextmanager
from pathlib import Path


HOST = "FS-GG/.github"
WORKFLOW = ".github/workflows/github-substrate-v2-sandbox-qualification.yml"
SOURCE_MANIFEST = "evidence/gs2-09-7/seed-bootstrap/source-manifest.json"
SOURCE_SCHEMA = "fsgg.gs2-09-7.sandbox-seed-source-artifacts/1"
DERIVED_MANIFEST = "bootstrap-manifest.json"
DERIVED_SCHEMA = "fsgg.gs2-09-7.sandbox-seed-bootstrap-artifacts/1"
HEX40 = re.compile(r"[0-9a-f]{40}\Z")
HEX64 = re.compile(r"[0-9a-f]{64}\Z")
INSTALLATION_STATUS = "source-only-uninstalled"
PREPARE_ADMISSION_PORT = None
FINAL_ADMISSION_PORT = None
PRESTATE_PRODUCER_PORT = None
TRUSTED_COORDINATION_BOOTSTRAP_PORT = None
TRUSTED_COORDINATION_BOOTSTRAP_STATUS = "source-only-uninstalled"
PINNED_COORDINATION_BOOTSTRAP_SHA256 = ""
COORDINATION_CLI_PROJECT = "src/FS.GG.Coordination.Cli/FS.GG.Coordination.Cli.fsproj"
PRESTATE_SCHEMA = "fsgg.gs2-09-7.sandbox-seed-prestate/1"


def load_sibling(name: str, filename: str):
    location = Path(__file__).with_name(filename)
    spec = importlib.util.spec_from_file_location(name, location)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class Refused(ValueError):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


def digest(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def require_bootstrap_runtime():
    """A reviewed protected runtime must own S1 verification and PushExact."""
    port = TRUSTED_COORDINATION_BOOTSTRAP_PORT
    require(TRUSTED_COORDINATION_BOOTSTRAP_STATUS == "installed-protected-verified-runtime"
            and type(PINNED_COORDINATION_BOOTSTRAP_SHA256) is str
            and HEX64.fullmatch(PINNED_COORDINATION_BOOTSTRAP_SHA256) is not None
            and port is not None and callable(getattr(port, "describe", None))
            and callable(getattr(port, "establish_and_write", None)),
            "coordination-bootstrap-runtime-uninstalled")
    require(port.describe() == {
        "schema": "fsgg.gs2-09-7.trusted-bootstrap-runtime/1",
        "sha256": PINNED_COORDINATION_BOOTSTRAP_SHA256,
        "candidateCode": False, "credentialHostOnly": True,
        "bootstrapVerifier": "VerifyBootstrapExact",
        "admission": "establishBootstrapAdmission",
        "nativeTransport": "PushExact",
        "readback": "writeGenesisAndRead",
        "privatePrestateEvidence": True,
    }, "coordination-bootstrap-runtime-identity")
    return port


def strict_json(raw: bytes, limit: int = 1024 * 1024) -> dict:
    require(type(raw) is bytes and 0 < len(raw) <= limit, "manifest-size")
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "duplicate-json-member")
            result[key] = value
        return result
    try:
        value = json.loads(raw, object_pairs_hook=unique,
                           parse_constant=lambda _: (_ for _ in ()).throw(Refused("nonfinite-number")))
    except (UnicodeError, ValueError) as error:
        raise Refused("manifest-json") from error
    require(type(value) is dict, "manifest-object")
    return value


def regular_inside(checkout: Path, relative: str) -> Path:
    path = checkout
    for part in Path(relative).parts:
        require(part not in ("", ".", ".."), "artifact-path")
        path = path / part
        require(not path.is_symlink(), "artifact-symlink")
    require(path.is_file(), "artifact-unavailable")
    return path


def write_private(path: Path, raw: bytes) -> None:
    require(path.is_absolute() and path.parent.is_dir() and not path.parent.is_symlink(),
            "private-output-path")
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL |
                         getattr(os, "O_NOFOLLOW", 0), 0o600)
    with os.fdopen(descriptor, "wb") as stream:
        stream.write(raw)


def read_private(path: Path, limit: int = 1024 * 1024) -> bytes:
    require(path.is_absolute() and not path.is_symlink(), "private-input-path")
    descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    with os.fdopen(descriptor, "rb") as stream:
        info = os.fstat(stream.fileno())
        require(stat.S_ISREG(info.st_mode) and stat.S_IMODE(info.st_mode) == 0o600,
                "private-input-mode")
        raw = stream.read(limit + 1)
    require(0 < len(raw) <= limit, "private-input-size")
    return raw


@contextmanager
def temporary_environment(values: dict[str, str]):
    prior = {name: os.environ.get(name) for name in values}
    try:
        os.environ.update(values)
        yield
    finally:
        for name, old in prior.items():
            if old is None:
                os.environ.pop(name, None)
            else:
                os.environ[name] = old


FILES = ["s2-declaration.json", "seed-plan.json", "corpus.json",
         "journal/state.json", "journal/tree.raw", "journal/commit.raw"]


def validate_source_artifacts(checkout: Path, facts: dict) -> tuple[dict, dict[str, bytes]]:
    try:
        path = regular_inside(checkout, SOURCE_MANIFEST)
    except Refused as error:
        raise Refused("coordination-source-producer-uninstalled") from error
    raw = path.read_bytes()
    require(digest(raw) == facts["manifestSha256"], "source-manifest-digest")
    manifest = strict_json(raw)
    require(set(manifest) == {"schema", "status", "candidateSha",
                              "seedPlan", "corpus",
                              "postMintSealRequired", "bootstrapAuthority",
                              "providerEffectsAuthorized"}
            and manifest["schema"] == SOURCE_SCHEMA
            and manifest["status"] == "source-only-no-authority"
            and manifest["candidateSha"] == facts["candidateSha"]
            and manifest["postMintSealRequired"] is True
            and manifest["bootstrapAuthority"] is False
            and manifest["providerEffectsAuthorized"] is False,
            "source-manifest-binding")
    # The Coordination producer emits canonical bytes without a self-digest.
    # The retained raw manifest is the approved source preimage read by S1.
    require(raw == json.dumps(manifest, separators=(",", ":"),
                             ensure_ascii=False, allow_nan=False).encode("utf-8"),
            "source-manifest-noncanonical")
    retained = {}
    for label, expected in (("seedPlan", "seed-plan.json"), ("corpus", "corpus.json")):
        row = manifest[label]
        require(type(row) is dict and set(row) == {"path", "byteLength", "sha256"}
                and row["path"] == expected and type(row["byteLength"]) is int
                and 0 < row["byteLength"] <= 64 * 1024 * 1024
                and type(row["sha256"]) is str and HEX64.fullmatch(row["sha256"]) is not None,
                "source-file-entry")
        try:
            item = regular_inside(checkout, str(Path(SOURCE_MANIFEST).parent / expected))
        except Refused as error:
            raise Refused("source-file-unavailable") from error
        content = item.read_bytes()
        require(len(content) == row["byteLength"] and digest(content) == row["sha256"],
                "source-file-drift")
        retained[expected] = content
    manifest["approvedArtifactSourceSha256"] = digest(raw)
    return manifest, retained


def validate_artifacts(runtime_dir: Path, facts: dict) -> tuple[dict, dict[str, bytes]]:
    require(runtime_dir.is_absolute(), "derived-artifact-directory")
    try:
        manifest_path = regular_inside(runtime_dir, DERIVED_MANIFEST)
    except Refused as error:
        raise Refused("coordination-postmint-seal-uninstalled") from error
    raw = manifest_path.read_bytes()
    manifest = strict_json(raw)
    require(set(manifest) == {"schema", "status", "candidateSha", "workflowRunId",
                              "workflowRunAttempt", "workflowSha", "runNonce", "refName",
                              "journalGeneration", "stateGeneration", "expectedParent",
                              "stateSha256", "blobOid", "treeOid", "commitOid", "files",
                              "bootstrapAuthority", "providerEffectsAuthorized"}
            and manifest["schema"] == DERIVED_SCHEMA
            and manifest["status"] == "source-only-no-authority"
            and manifest["bootstrapAuthority"] is False
            and manifest["providerEffectsAuthorized"] is False
            and manifest["candidateSha"] == facts["candidateSha"]
            and manifest["workflowSha"] == facts["workflowSha"]
            and manifest["workflowRunId"] == facts["runId"]
            and manifest["workflowRunAttempt"] == facts["runAttempt"]
            and manifest["runNonce"] == facts["runNonce"]
            and manifest["refName"] == f'refs/heads/gs2-09-7/{facts["runNonce"]}/seed-journal'
            and manifest["journalGeneration"] == 0
            and manifest["stateGeneration"] == 0
            and manifest["expectedParent"] is None
            and all(type(manifest[name]) is str and HEX40.fullmatch(manifest[name]) is not None
                    for name in ("blobOid", "treeOid", "commitOid"))
            and type(manifest["stateSha256"]) is str
            and HEX64.fullmatch(manifest["stateSha256"]) is not None,
            "manifest-binding")
    rows = manifest["files"]
    require(type(rows) is list and len(rows) == len(FILES), "manifest-files")
    retained = {}
    for expected, row in zip(FILES, rows):
        require(type(row) is dict and set(row) == {"path", "byteLength", "sha256"}
                and row["path"] == expected and type(row["byteLength"]) is int
                and 0 < row["byteLength"] <= 64 * 1024 * 1024
                and type(row["sha256"]) is str and HEX64.fullmatch(row["sha256"]) is not None,
                "manifest-file-entry")
        try:
            path = regular_inside(runtime_dir, expected)
        except Refused as error:
            raise Refused("manifest-file-unavailable") from error
        content = path.read_bytes()
        require(len(content) == row["byteLength"] and digest(content) == row["sha256"],
                "manifest-file-drift")
        retained[expected] = content
    require(digest(retained["journal/state.json"]) == manifest["stateSha256"],
            "manifest-state-digest")
    return manifest, retained


def context(environment: dict[str, str]) -> dict:
    required = {"GITHUB_ACTIONS", "GITHUB_EVENT_NAME", "GITHUB_REPOSITORY",
                "GITHUB_REF", "GITHUB_WORKFLOW_REF", "GITHUB_WORKFLOW_SHA", "GITHUB_SHA",
                "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "FSGG_CANDIDATE_SHA",
                "FSGG_PROTECTED_SHA", "FSGG_SEED_SOURCE_MANIFEST_SHA256"}
    require(required.issubset(environment), "protected-context-missing")
    try:
        run_id = int(environment["GITHUB_RUN_ID"])
        attempt = int(environment["GITHUB_RUN_ATTEMPT"])
    except ValueError as error:
        raise Refused("protected-run-number") from error
    candidate = environment["FSGG_CANDIDATE_SHA"]
    workflow_sha = environment["GITHUB_SHA"]
    manifest_sha = environment["FSGG_SEED_SOURCE_MANIFEST_SHA256"]
    require(environment["GITHUB_ACTIONS"] == "true"
            and environment["GITHUB_EVENT_NAME"] == "workflow_dispatch"
            and environment["GITHUB_REPOSITORY"] == HOST
            and environment["GITHUB_REF"] == "refs/heads/main"
            and environment["GITHUB_WORKFLOW_REF"] == f"{HOST}/{WORKFLOW}@refs/heads/main"
            and HEX40.fullmatch(workflow_sha) is not None
            and environment["GITHUB_WORKFLOW_SHA"] == workflow_sha
            and environment["FSGG_PROTECTED_SHA"] == workflow_sha
            and run_id > 0 and attempt > 0
            and HEX40.fullmatch(candidate) is not None
            and HEX64.fullmatch(manifest_sha) is not None, "protected-context")
    return {"workflowSha": workflow_sha, "candidateSha": candidate,
            "runId": run_id, "runAttempt": attempt,
            "runNonce": f"{run_id}-{attempt}-{candidate}",
            "manifestSha256": manifest_sha}


def preflight(host_checkout: Path, candidate_checkout: Path,
              environment: dict[str, str], prepare_port=None) -> dict:
    facts = context(environment)
    require(host_checkout.resolve().is_dir() and candidate_checkout.resolve().is_dir(),
            "checkout-unavailable")
    require(INSTALLATION_STATUS == "installed-protected-bootstrap",
            "bootstrap-source-uninstalled")
    source, _ = validate_source_artifacts(candidate_checkout, facts)
    admission = load_sibling("gs2_seed_bootstrap_admission", "gs2-09-7-seed-bootstrap-admission.py")
    try:
        admission.require_prepare_admitted(
            PREPARE_ADMISSION_PORT if prepare_port is None else prepare_port,
            facts, facts["manifestSha256"],
            source["approvedArtifactSourceSha256"], dt.datetime.now(dt.timezone.utc))
    except admission.Refused as error:
        raise Refused(f"prepare-{error}") from error
    return facts


def require_postmint_ready(host_checkout: Path, candidate_checkout: Path,
                           workflow_sha: str, final_port=None,
                           prestate_port=None) -> tuple[object, object, object]:
    """Check every locally installed operation dependency before App mint."""
    binding = load_sibling("gs2_seed_execution_binding", "gs2-09-7-seed-execution-binding.py")
    cas = load_sibling("gs2_seed_native_cas", "gs2-09-7-seed-native-cas.py")
    admission = load_sibling("gs2_seed_bootstrap_admission", "gs2-09-7-seed-bootstrap-admission.py")
    require(binding.INSTALLATION_STATUS == "installed-fixed-main-workflow",
            "postmint-source-uninstalled")
    require_bootstrap_runtime()
    try:
        if final_port is None:
            require(all(type(value) is str and value for value in (
                admission.PINNED_ORIGIN, admission.PINNED_RESOURCE_ID,
                admission.PINNED_ENDPOINT)), "final-admission-unconfigured")
        else:
            admission.check_port(final_port)
    except admission.Refused as error:
        raise Refused(f"final-{error}") from error
    selected_prestate = PRESTATE_PRODUCER_PORT if prestate_port is None else prestate_port
    require(selected_prestate is not None
            and callable(getattr(selected_prestate, "describe", None))
            and callable(getattr(selected_prestate, "produce", None)),
            "prestate-producer-uninstalled")
    require(selected_prestate.describe() == {
        "schema": "fsgg.gs2-09-7.sandbox-seed-prestate-producer/1",
        "repositoryId": 1353050537,
        "projectNodeId": "PVT_kwDOEYAWY84BiESo",
        "source": "fresh-native-issue-project-and-ref-pages",
        "credentialScope": "protected-host-only",
        "candidateCanWrite": False,
    }, "prestate-producer-identity")
    regular_inside(candidate_checkout, COORDINATION_CLI_PROJECT)
    require(binding.checked_out_provenance(workflow_sha)["checkoutHead"] == workflow_sha,
            "protected-binding-provenance")
    for relative in ("scripts/gs2-09-7-seed-bootstrap-entry.py",
                     "scripts/gs2-09-7-seed-bootstrap-admission.py",
                     "scripts/gs2-09-7-mint-sandbox-token.py",
                     "scripts/gs2-09-7-seed-prestate.py",
                     "scripts/gs2-09-7-seed-admission-bridge.py",
                     "scripts/gs2-09-7-seed-seal-exchange.py",
                     "scripts/gs2-09-7-seed-admission-authorizer.py",
                     "scripts/gs2-09-7-seed-admission-native-read.py"):
        current = binding.read_regular(regular_inside(host_checkout, relative), 1024 * 1024)
        committed = binding.git_bytes(host_checkout, ["show", f"{workflow_sha}:{relative}"],
                                      1024 * 1024)
        require(current == committed, "protected-bootstrap-helper-drift")
    return binding, cas, admission


def validate_prestate(raw: bytes, facts: dict) -> None:
    value = strict_json(raw)
    require(set(value) == {"schema", "complete", "repositoryId", "projectNodeId",
                           "nonceIssueCount", "nonceProjectItemCount", "snapshotSha256"}
            and value["schema"] == PRESTATE_SCHEMA
            and value["complete"] is True
            and value["repositoryId"] == 1353050537
            and value["projectNodeId"] == "PVT_kwDOEYAWY84BiESo"
            and type(value["nonceIssueCount"]) is int and value["nonceIssueCount"] == 0
            and type(value["nonceProjectItemCount"]) is int
            and value["nonceProjectItemCount"] == 0
            and type(value["snapshotSha256"]) is str
            and HEX64.fullmatch(value["snapshotSha256"]) is not None,
            "fresh-prestate")


def produce_prestate_capture(token: str, facts: dict, producer_port=None) -> dict:
    selected = PRESTATE_PRODUCER_PORT if producer_port is None else producer_port
    require(selected is not None, "prestate-producer-uninstalled")
    try:
        result = selected.produce(token, facts)
    except Exception as error:
        raise Refused("prestate-producer-readback") from error
    require(type(result) is dict and set(result) == {
        "runNonce", "raw", "captureId", "observedAt", "source", "evidenceRaw"}
        and result["runNonce"] == facts["runNonce"]
        and type(result["raw"]) is bytes
        and type(result["captureId"]) is str
        and HEX64.fullmatch(result["captureId"]) is not None
        and type(result["evidenceRaw"]) is bytes
        and 0 < len(result["evidenceRaw"]) <= 64 * 1024 * 1024
        and digest(result["evidenceRaw"]) == result["captureId"]
        and result["source"] == "fresh-native-issue-project-and-ref-pages",
        "prestate-producer-capture")
    try:
        observed = dt.datetime.fromisoformat(result["observedAt"].replace("Z", "+00:00"))
    except (TypeError, ValueError, OverflowError) as error:
        raise Refused("prestate-capture-time") from error
    now = dt.datetime.now(dt.timezone.utc)
    require(result["observedAt"].endswith("Z")
            and observed.tzinfo is not None
            and now - dt.timedelta(minutes=5) <= observed <= now,
            "prestate-capture-time")
    validate_prestate(result["raw"], facts)
    evidence = strict_json(result["evidenceRaw"], 64 * 1024 * 1024)
    summary = strict_json(result["raw"])
    require(set(evidence) == {"schema", "runNonce", "refName", "repositoryId",
                              "projectNodeId", "expectedRefAbsent", "snapshotSha256",
                              "summarySha256", "observedAt", "passes", "complete"}
            and evidence["schema"] == "fsgg.gs2-09-7.sandbox-seed-prestate-evidence/1"
            and evidence["runNonce"] == facts["runNonce"]
            and evidence["refName"] ==
                f'refs/heads/gs2-09-7/{facts["runNonce"]}/seed-journal'
            and evidence["repositoryId"] == 1353050537
            and evidence["projectNodeId"] == "PVT_kwDOEYAWY84BiESo"
            and evidence["expectedRefAbsent"] is True
            and evidence["complete"] is True
            and evidence["observedAt"] == result["observedAt"]
            and evidence["summarySha256"] == digest(result["raw"])
            and evidence["snapshotSha256"] == summary["snapshotSha256"]
            and type(evidence["passes"]) is list and len(evidence["passes"]) == 2
            and evidence["passes"][0]["snapshot"] == evidence["passes"][1]["snapshot"]
            and all(type(item) is dict and set(item) == {"snapshot", "requests"}
                    and type(item["requests"]) is list and len(item["requests"]) >= 6
                    and item["snapshot"].get("ref", {}).get("expectedAbsent") is True
                    and item["requests"][-1].get("status") == 404
                    for item in evidence["passes"]), "prestate-evidence-binding")
    return result


def produce_prestate(token: str, facts: dict, producer_port=None) -> bytes:
    return produce_prestate_capture(token, facts, producer_port)["raw"]


def attach_final_prestate(subject: dict, root: Path, sealed: Path,
                          facts: dict) -> dict:
    """Bind the initial S1 prestate and private native capture to final admission."""
    summary_raw = regular_inside(sealed, "prestate.json").read_bytes()
    validate_prestate(summary_raw, facts)
    evidence_raw = read_private(root / "prestate-evidence.private.json",
                                64 * 1024 * 1024)
    evidence = strict_json(evidence_raw, 64 * 1024 * 1024)
    summary = strict_json(summary_raw)
    require(evidence.get("schema") ==
            "fsgg.gs2-09-7.sandbox-seed-prestate-evidence/1"
            and evidence.get("runNonce") == facts["runNonce"]
            and evidence.get("refName") == subject["refName"]
            and evidence.get("repositoryId") == 1353050537
            and evidence.get("projectNodeId") == "PVT_kwDOEYAWY84BiESo"
            and evidence.get("expectedRefAbsent") is True
            and evidence.get("complete") is True
            and evidence.get("summarySha256") == digest(summary_raw)
            and evidence.get("snapshotSha256") == summary["snapshotSha256"]
            and type(evidence.get("passes")) is list
            and len(evidence["passes"]) == 2
            and evidence["passes"][0].get("snapshot") ==
                evidence["passes"][1].get("snapshot")
            and all(type(item) is dict and type(item.get("requests")) is list
                    and len(item["requests"]) >= 6
                    and item["requests"][-1].get("status") == 404
                    for item in evidence["passes"]), "final-prestate-evidence")
    require(summary_raw == read_private(root / "prestate.json"),
            "final-prestate-summary-drift")
    require(subject["approvedArtifactSourceSha256"] == facts["manifestSha256"],
            "final-source-manifest-digest")
    return {**subject, "sourceManifestSha256": facts["manifestSha256"],
            "prestateSha256": digest(summary_raw),
            "prestateSnapshotSha256": summary["snapshotSha256"],
            "prestateEvidenceSha256": digest(evidence_raw),
            "expectedRefAbsent": True}


def mint_private(mint_module, environment: dict[str, str]) -> tuple[str, bytes]:
    """Keep provider raw responses and the token in this protected process."""
    app_id = int(environment["FSGG_DISPATCH_APP_ID"])
    private_key = environment["FSGG_DISPATCH_APP_PRIVATE_KEY"]
    require(app_id == 4166418 and "PRIVATE KEY" in private_key, "protected-app-identity")
    jwt = mint_module.app_jwt(app_id, private_key)
    app, _ = mint_module.request_json("GET", "/app", jwt)
    require(app.get("id") == app_id and app.get("slug") == mint_module.APP_SLUG,
            "app-readback")
    installation, _ = mint_module.request_json(
        "GET", f"/repos/{mint_module.OWNER}/{mint_module.REPOSITORY}/installation", jwt)
    require(installation.get("id") == 143110413
            and installation.get("app_id") == app_id
            and installation.get("app_slug") == mint_module.APP_SLUG
            and installation.get("account", {}).get("login") == mint_module.OWNER,
            "installation-readback")
    response, mint_raw = mint_module.request_json(
        "POST", "/app/installations/143110413/access_tokens", jwt,
        {"repository_ids": [1353050537], "permissions": mint_module.PERMISSIONS})
    token = response.get("token")
    require(type(token) is str, "token-unavailable")
    try:
        proof = mint_module.validate_mint_response(response)
        viewer_response, viewer_raw = mint_module.request_json(
            "POST", "/graphql", token, {"query": "{viewer{login databaseId}}"})
        viewer = viewer_response.get("data", {}).get("viewer", {})
        require(not viewer_response.get("errors")
                and viewer.get("login") == mint_module.APP_ACTOR
                and viewer.get("databaseId") == mint_module.APP_ACTOR_ID,
                "mint-actor")
        proof.update({
            "schema": "fsgg.github-substrate-v2.sandbox-mint-grants/1",
            "appId": app_id, "appSlug": mint_module.APP_SLUG,
            "actor": {"login": viewer["login"], "databaseId": viewer["databaseId"]},
            "installationId": 143110413,
            "mintResponseSha256": digest(mint_raw),
            "viewerResponseSha256": digest(viewer_raw),
        })
        return token, (json.dumps(proof, sort_keys=True, separators=(",", ":")) + "\n").encode()
    except BaseException:
        mint_module.revoke_token(token)
        raise


def private_root(environment: dict[str, str], *, create: bool = False) -> Path:
    root = Path(environment["FSGG_SEED_PRIVATE_DIR"])
    require(root.is_absolute() and not root.is_symlink(), "private-root")
    if create:
        require(not root.exists(), "private-root-exists")
        root.mkdir(mode=0o700)
    require(root.is_dir() and stat.S_IMODE(root.stat().st_mode) == 0o700,
            "private-root-mode")
    return root


def native_host_start(port, facts: dict) -> tuple[int, dt.datetime]:
    bridge = bridge_module()
    listing = bridge.get_json(
        port, f'{bridge.REPO}/actions/runs/{facts["runId"]}/attempts/'
              f'{facts["runAttempt"]}/jobs?per_page=100')
    require(type(listing) is dict and type(listing.get("total_count")) is int
            and type(listing.get("jobs")) is list
            and listing["total_count"] == len(listing["jobs"]), "host-jobs-page")
    matches = [job for job in listing["jobs"] if type(job) is dict
               and job.get("name") == "gs2-09-7-seed-bootstrap-admission"]
    require(len(matches) == 1, "host-job-unique")
    job = matches[0]
    require(type(job.get("id")) is int and job["id"] > 0
            and job.get("run_id") == facts["runId"]
            and job.get("head_sha") == facts["workflowSha"]
            and job.get("status") == "in_progress"
            and job.get("conclusion") is None,
            "host-job-binding")
    raw = job.get("started_at")
    require(type(raw) is str and raw.endswith("Z"), "host-job-start")
    try:
        started = dt.datetime.fromisoformat(raw.replace("Z", "+00:00"))
    except (ValueError, OverflowError) as error:
        raise Refused("host-job-start") from error
    now = dt.datetime.now(dt.timezone.utc)
    require(now >= started and now - started < dt.timedelta(minutes=35),
            "host-job-start-expired")
    return job["id"], started


def deadline_remaining(root: Path, facts: dict, *, reserve_seconds: int = 0,
                       now: dt.datetime | None = None) -> int:
    record = strict_json(read_private(root / "deadline.json", 4096))
    require(set(record) == {"schema", "jobId", "runId", "runAttempt", "workflowSha",
                            "startedAt", "deadlineAt"}
            and record["schema"] == "fsgg.gs2-09-7.seed-host-deadline/1",
            "host-deadline")
    try:
        started = dt.datetime.fromisoformat(record["startedAt"].replace("Z", "+00:00"))
        deadline = dt.datetime.fromisoformat(record["deadlineAt"].replace("Z", "+00:00"))
    except (TypeError, ValueError, OverflowError) as error:
        raise Refused("host-deadline") from error
    current = now or dt.datetime.now(dt.timezone.utc)
    require(type(record["jobId"]) is int and record["jobId"] > 0
            and record["runId"] == facts["runId"]
            and record["runAttempt"] == facts["runAttempt"]
            and record["workflowSha"] == facts["workflowSha"]
            and type(reserve_seconds) is int and 0 <= reserve_seconds <= 10 * 60
            and started.tzinfo is not None and deadline.tzinfo is not None
            and current.tzinfo is not None
            and started <= current < deadline
            and deadline - started == dt.timedelta(minutes=35)
            and (deadline - current).total_seconds() > reserve_seconds,
            "host-deadline-expired")
    return max(1, int((deadline - current).total_seconds() - reserve_seconds))


def exchange_module():
    return load_sibling("gs2_seed_seal_exchange", "gs2-09-7-seed-seal-exchange.py")


def prepare_subject(facts: dict, source: dict) -> dict:
    return {
        "workflowRepository": HOST, "workflowPath": WORKFLOW,
        "environment": "github-substrate-v2-sandbox",
        "workflowSha": facts["workflowSha"], "candidateSha": facts["candidateSha"],
        "runId": facts["runId"], "runAttempt": facts["runAttempt"],
        "runNonce": facts["runNonce"],
        "sourceManifestSha256": facts["manifestSha256"],
        "approvedArtifactSourceSha256": source["approvedArtifactSourceSha256"],
        "sandboxRepositoryId": 1353050537,
        "sandboxRepositoryNodeId": "R_kgDOUKXpqQ",
        "projectNodeId": "PVT_kwDOEYAWY84BiESo",
        "appId": 4166418, "installationId": 143110413,
        "operation": "prepare-only-no-effect",
    }


def bridge_module():
    return load_sibling("gs2_seed_admission_bridge", "gs2-09-7-seed-admission-bridge.py")


def request_file(root: Path, phase: str) -> Path:
    require(phase in ("prepare", "final"), "request-phase")
    return root / phase / "seed-admission-request.json"


def stage_prepare_request(host_checkout: Path, candidate_checkout: Path,
                          environment: dict[str, str]) -> dict:
    """Publish source-only credential request before any App secret access."""
    require(INSTALLATION_STATUS == "installed-protected-bootstrap",
            "bootstrap-source-uninstalled")
    facts = context(environment)
    source, _ = validate_source_artifacts(candidate_checkout, facts)
    bridge = bridge_module()
    subject = prepare_subject(facts, source)
    root = private_root(environment, create=True)
    port = bridge.GitHubReadPort(environment["GH_TOKEN"])
    job_id, started = native_host_start(port, facts)
    deadline = started + dt.timedelta(minutes=35)
    write_private(root / "deadline.json", (json.dumps({
        "schema": "fsgg.gs2-09-7.seed-host-deadline/1",
        "jobId": job_id, "runId": facts["runId"],
        "runAttempt": facts["runAttempt"], "workflowSha": facts["workflowSha"],
        "startedAt": started.isoformat().replace("+00:00", "Z"),
        "deadlineAt": deadline.isoformat().replace("+00:00", "Z")},
        sort_keys=True, separators=(",", ":")) + "\n").encode())
    deadline_remaining(root, facts)
    folder = root / "prepare"
    folder.mkdir(mode=0o700)
    write_private(request_file(root, "prepare"), bridge.request_bytes("prepare", subject))
    return subject


def read_request(root: Path, phase: str) -> dict:
    bridge = bridge_module()
    raw = read_private(request_file(root, phase))
    value = strict_json(raw)
    require(raw == bridge.request_bytes(phase, value.get("subject")),
            "request-bytes")
    return value["subject"]


def request_info(environment: dict[str, str], phase: str) -> dict:
    """Publish native artifact ID/digest for the independent owner's dispatch."""
    bridge = bridge_module()
    root = private_root(environment)
    subject = read_request(root, phase)
    port = bridge.GitHubReadPort(environment["GH_TOKEN"])
    artifact = bridge.request_artifact(port, phase, subject)
    fields = bridge.owner_dispatch_fields(phase, subject, artifact)
    summary = Path(environment["GITHUB_STEP_SUMMARY"])
    require(summary.is_absolute() and summary.parent.is_dir()
            and not summary.is_symlink(),
            "step-summary")
    with summary.open("a", encoding="utf-8") as stream:
        stream.write("\nGS2-09.7 independent owner dispatch inputs:\n\n```json\n")
        stream.write(json.dumps(fields, sort_keys=True, separators=(",", ":")))
        stream.write("\n```\n")
    return fields


def target_readbacks(mint_module, token: str) -> tuple[bytes, bytes]:
    repository, repository_raw = mint_module.request_json(
        "GET", "/repos/FS-GG/FS.GG.GitHub.Substrate.Sandbox", token)
    require(type(repository) is dict, "sandbox-repository-readback")
    project_response, _ = mint_module.request_json(
        "POST", "/graphql", token,
        {"query": "query($owner:String!,$number:Int!){organization(login:$owner){projectV2(number:$number){id title closed public}}}",
         "variables": {"owner": "FS-GG", "number": 2}})
    require(not project_response.get("errors")
            and type(project_response.get("data")) is dict
            and type(project_response["data"].get("organization")) is dict
            and type(project_response["data"]["organization"].get("projectV2")) is dict,
            "sandbox-project-readback")
    project_raw = json.dumps(project_response["data"]["organization"]["projectV2"],
                             sort_keys=True, separators=(",", ":")).encode()
    return repository_raw, project_raw


def require_returned_seal_inputs(files: dict[str, bytes], retained: dict[str, bytes],
                                 declaration: bytes) -> None:
    require(files["s2-declaration.json"] == declaration
            and files["seed-plan.json"] == retained["seed-plan.json"]
            and files["corpus.json"] == retained["corpus.json"],
            "seal-returned-input-drift")


def stage_seal_input(host_checkout: Path, candidate_checkout: Path,
                     environment: dict[str, str]) -> dict:
    """After prepare admission, mint and export only sanitized seal inputs."""
    require(INSTALLATION_STATUS == "installed-protected-bootstrap",
            "bootstrap-source-uninstalled")
    facts = context(environment)
    root = private_root(environment)
    bridge = bridge_module()
    exchange = exchange_module()
    prestate = load_sibling("gs2_seed_prestate", "gs2-09-7-seed-prestate.py")
    binding, _, _ = require_postmint_ready(
        host_checkout, candidate_checkout, facts["workflowSha"], prestate_port=prestate)
    source, retained = validate_source_artifacts(candidate_checkout, facts)
    expected = prepare_subject(facts, source)
    require(read_request(root, "prepare") == expected, "prepare-request-drift")
    actions_read = bridge.GitHubReadPort(environment["GH_TOKEN"])
    bridge.require_protected_main(actions_read, facts["workflowSha"])
    port = bridge.wait_decision(actions_read, "prepare", expected, facts["workflowSha"],
                                max_seconds=min(13 * 60, deadline_remaining(root, facts)))
    deadline_remaining(root, facts)
    bridge.require_protected_main(actions_read, facts["workflowSha"])
    preflight(host_checkout, candidate_checkout, environment, port)
    mint_module = load_sibling("gs2_seed_mint", "gs2-09-7-mint-sandbox-token.py")
    token = None
    token_path = root / "installation-token.private"
    try:
        token, proof = mint_private(mint_module, environment)
        # Persist first. Every later failure leaves a retryable private token
        # until native revoke confirms success.
        write_private(token_path, token.encode("ascii"))
        proof_path = root / "mint-grants.json"
        plan_path = root / "seed-plan.json"
        corpus_path = root / "corpus.json"
        prestate_path = root / "prestate.json"
        write_private(proof_path, proof)
        write_private(plan_path, retained["seed-plan.json"])
        write_private(corpus_path, retained["corpus.json"])
        prestate_capture = produce_prestate_capture(token, facts, prestate)
        write_private(prestate_path, prestate_capture["raw"])
        # Raw issue/Project/ref response custody stays on the protected host.
        # Candidate sealer receives only the typed seven-field summary.
        write_private(root / "prestate-evidence.private.json",
                      prestate_capture["evidenceRaw"])
        deadline_remaining(root, facts)
        with temporary_environment({
            "FSGG_SANDBOX_EVIDENCE_DIR": str(root),
            "FSGG_SEED_PLAN_PATH": str(plan_path),
            "FSGG_SEED_CORPUS_PATH": str(corpus_path),
            "FSGG_SANDBOX_MINT_PROOF": str(proof_path),
            "FSGG_SANDBOX_TOKEN": token,
            "FSGG_APPROVED_ARTIFACT_SOURCE_SHA256": source["approvedArtifactSourceSha256"],
            "FSGG_PROTECTED_ENVIRONMENT": "github-substrate-v2-sandbox",
            "FSGG_SANDBOX_RUN_NONCE": facts["runNonce"],
            "FSGG_SANDBOX_REPOSITORY_ID": "1353050537",
            "FSGG_SANDBOX_REPOSITORY_NODE_ID": "R_kgDOUKXpqQ",
            "FSGG_SANDBOX_PROJECT_NUMBER": "2",
            "FSGG_SANDBOX_PROJECT_NODE_ID": "PVT_kwDOEYAWY84BiESo",
            "FSGG_SEED_JOURNAL_REF": f'refs/heads/gs2-09-7/{facts["runNonce"]}/seed-journal',
        }):
            declaration = binding.build_document()
        write_private(root / "s2-declaration.json", declaration)
        # Upload-artifact receives only this allowlisted directory. The token
        # and raw mint/viewer responses never enter this tree.
        folder = root / "seal-input"
        folder.mkdir(mode=0o700)
        files = {
            "source-manifest.json": regular_inside(candidate_checkout, SOURCE_MANIFEST).read_bytes(),
            "s2-declaration.json": declaration,
            "mint-grants.json": proof,
            "seed-plan.json": retained["seed-plan.json"],
            "corpus.json": retained["corpus.json"],
            "prestate.json": read_private(prestate_path),
        }
        require(tuple(files) == exchange.INPUT_FILES, "seal-input-allowlist")
        write_private(folder / "seal-input-manifest.json", exchange.manifest(facts, files))
        for name, raw in files.items():
            write_private(folder / name, raw)
        return facts
    except BaseException:
        if token is not None and token_path.exists():
            revoke_private_token(root)
        elif token is not None:
            mint_module.revoke_token(token)
        raise


def stage_consume_seal_and_final_request(host_checkout: Path,
                                         candidate_checkout: Path,
                                         environment: dict[str, str]) -> dict:
    """Accept only exact sealer-job success and byte-identical protected inputs."""
    require(INSTALLATION_STATUS == "installed-protected-bootstrap",
            "bootstrap-source-uninstalled")
    facts = context(environment)
    root = private_root(environment)
    bridge = bridge_module()
    exchange = exchange_module()
    actions_read = bridge.GitHubReadPort(environment["GH_TOKEN"])
    files, _ = exchange.wait(actions_read, "output", facts, deadline_remaining(root, facts),
                             require_job=True)
    deadline_remaining(root, facts)
    sealed = root / "sealed"
    require(not sealed.exists(), "seal-output-exists")
    sealed.mkdir(mode=0o700)
    for name in exchange.OUTPUT_FILES:
        destination = sealed / name
        destination.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
        write_private(destination, files[name])
    write_private(sealed / "prestate.json", files["prestate.json"])
    validate_artifacts(sealed, facts)
    source, retained = validate_source_artifacts(candidate_checkout, facts)
    require_returned_seal_inputs(files, retained,
                                 read_private(root / "s2-declaration.json"))
    input_files = {name: read_private(root / "seal-input" / name, 64 * 1024 * 1024)
                   for name in exchange.INPUT_FILES}
    exchange.validate_manifest(read_private(root / "seal-input" / "seal-input-manifest.json"),
                               facts, input_files)
    input_artifact = exchange.artifact(actions_read, "input", facts)
    require(input_artifact is not None, "seal-input-artifact-missing")
    receipt_bytes = files["seal-exchange-receipt.json"]
    receipt = exchange.strict_json(receipt_bytes)
    require(receipt_bytes == exchange.canonical(receipt)
            and receipt == {
                "schema": exchange.SCHEMA, "status": "source-only-no-authority",
                "candidateSha": facts["candidateSha"],
                "workflowSha": facts["workflowSha"],
                "runId": facts["runId"], "runAttempt": facts["runAttempt"],
                "inputArtifactId": input_artifact["id"],
                "inputArchiveDigest": input_artifact["digest"],
                "inputManifestSha256": digest(read_private(root / "seal-input" / "seal-input-manifest.json")),
                "s2DeclarationSha256": digest(input_files["s2-declaration.json"]),
                "seedPlanSha256": digest(input_files["seed-plan.json"]),
                "corpusSha256": digest(input_files["corpus.json"]),
                "prestateSha256": digest(input_files["prestate.json"]),
                "bootstrapAuthority": False,
            }, "seal-exchange-receipt-drift")
    require(files["prestate.json"] == input_files["prestate.json"]
            and input_files["prestate.json"] == read_private(root / "prestate.json")
            and input_files["mint-grants.json"] == read_private(root / "mint-grants.json")
            and input_files["source-manifest.json"] == regular_inside(candidate_checkout, SOURCE_MANIFEST).read_bytes(),
            "seal-input-drift")
    token = read_private(root / "installation-token.private", 4096).decode("ascii")
    mint_module = load_sibling("gs2_seed_mint", "gs2-09-7-mint-sandbox-token.py")
    repository_raw, project_raw = target_readbacks(mint_module, token)
    deadline_remaining(root, facts)
    _, _, _, subject = inspect_source(environment, candidate_checkout, sealed,
                                      input_files["mint-grants.json"], token,
                                      repository_raw, project_raw,
                                      dt.datetime.now(dt.timezone.utc))
    subject = attach_final_prestate(subject, root, sealed, facts)
    bridge.require_protected_main(actions_read, facts["workflowSha"])
    deadline_remaining(root, facts)
    folder = root / "final"
    folder.mkdir(mode=0o700)
    write_private(request_file(root, "final"), bridge.request_bytes("final", subject))
    return subject


def revoke_private_token(root: Path) -> bool:
    path = root / "installation-token.private"
    if not path.exists():
        return False
    token = read_private(path, 4096).decode("ascii")
    mint_module = load_sibling("gs2_seed_mint", "gs2-09-7-mint-sandbox-token.py")
    # An unknown revoke response preserves the token file for the cleanup step
    # or a later bounded owner reconciliation. Never erase custody on failure.
    mint_module.revoke_token(token)
    path.unlink()
    promote_readback(root)
    return True


def write_revocation_pending(root: Path, report: dict,
                             environment: dict[str, str]) -> None:
    """Retain a sanitized nonauthorizing CAS observation before token revoke."""
    sanitized = Path(environment["FSGG_SEED_SANITIZED_EVIDENCE_DIR"])
    require(sanitized.is_absolute() and not sanitized.exists(), "sanitized-output-path")
    sanitized.mkdir(mode=0o700)
    cas = load_sibling("gs2_seed_native_cas", "gs2-09-7-seed-native-cas.py")
    raw = cas.canonical({
        "schema": "fsgg.gs2-09-7.sandbox-seed-revocation-pending/1",
        "status": "revocation-pending", "activation": False,
        "tokenRevocation": "unconfirmed", "nativeCasReadback": report,
    })
    require(len(raw) <= 1024 * 1024, "pending-readback-size")
    write_private(sanitized / "revocation-pending.json", raw)


def promote_readback(root: Path) -> None:
    pending_path = root / "cas-readback.private.json"
    if not pending_path.exists():
        return
    require(not (root / "installation-token.private").exists(),
            "readback-revocation-pending")
    report = read_private(pending_path)
    sanitized = Path(os.environ["FSGG_SEED_SANITIZED_EVIDENCE_DIR"])
    require(sanitized.is_absolute() and not sanitized.is_symlink(), "sanitized-output-path")
    if not sanitized.exists():
        sanitized.mkdir(mode=0o700)
    require(sanitized.is_dir() and stat.S_IMODE(sanitized.stat().st_mode) == 0o700,
            "sanitized-output-path")
    write_private(sanitized / "native-readback.json", report)
    (sanitized / "revocation-pending.json").unlink(missing_ok=True)
    pending_path.unlink()


def stage_final_apply(host_checkout: Path, candidate_checkout: Path,
                      environment: dict[str, str]) -> dict:
    """Fresh final owner decision and exact sealed S1 precede one journal CAS."""
    require(INSTALLATION_STATUS == "installed-protected-bootstrap",
            "bootstrap-source-uninstalled")
    facts = context(environment)
    root = private_root(environment)
    subject = read_request(root, "final")
    bridge = bridge_module()
    read_port = bridge.GitHubReadPort(environment["GH_TOKEN"])
    token = read_private(root / "installation-token.private", 4096).decode("ascii")
    report = None
    try:
        bridge.require_protected_main(read_port, facts["workflowSha"])
        final_port = bridge.wait_decision(
            read_port, "final", subject, facts["workflowSha"],
            max_seconds=min(13 * 60, deadline_remaining(root, facts)))
        decision = final_port.read_decision(facts["runId"], facts["runAttempt"])
        write_private(root / "final-admission.private.json",
                      bridge.authorizer.canonical(decision))
        deadline_remaining(root, facts)
        bridge.require_protected_main(read_port, facts["workflowSha"])
        require_postmint_ready(
            host_checkout, candidate_checkout, facts["workflowSha"],
            final_port=final_port,
            prestate_port=load_sibling("gs2_seed_prestate", "gs2-09-7-seed-prestate.py"))
        mint_module = load_sibling("gs2_seed_mint", "gs2-09-7-mint-sandbox-token.py")
        # Recheck the exact nonce census after final admission, immediately
        # before the journal operation. It remains GET-only and must be zero.
        fresh_prestate = produce_prestate_capture(token, facts,
            load_sibling("gs2_seed_prestate", "gs2-09-7-seed-prestate.py"))
        write_private(root / "final-prestate.private.json", fresh_prestate["raw"])
        write_private(root / "final-prestate-evidence.private.json",
                      fresh_prestate["evidenceRaw"])
        deadline_remaining(root, facts)
        proof = read_private(root / "mint-grants.json")
        repository_raw, project_raw = target_readbacks(mint_module, token)
        deadline_remaining(root, facts)
        sealed = root / "sealed"
        _, _, _, actual_subject = inspect_source(
            environment, candidate_checkout, sealed, proof, token,
            repository_raw, project_raw, dt.datetime.now(dt.timezone.utc))
        actual_subject = attach_final_prestate(actual_subject, root, sealed, facts)
        require(actual_subject == subject, "final-request-drift")
        deadline_remaining(root, facts, reserve_seconds=5 * 60)
        report = execute_source(environment, candidate_checkout, sealed,
                                proof, token, repository_raw, project_raw,
                                final_port, dt.datetime.now(dt.timezone.utc))
        cas = load_sibling("gs2_seed_native_cas", "gs2-09-7-seed-native-cas.py")
        write_private(root / "cas-readback.private.json", cas.canonical(report))
        write_revocation_pending(root, report, environment)
    finally:
        revoke_private_token(root)
    require(report is not None and report.get("complete") is True,
            "native-readback-pending")
    return report


def inspect_source(environment: dict[str, str], candidate_checkout: Path,
                   runtime_dir: Path, mint_proof_bytes: bytes, token: str,
                   repository_readback_bytes: bytes,
                   project_readback_bytes: bytes, now: dt.datetime) -> tuple:
    """Bind immutable S2/S1 and live targets before publishing final request."""
    require(INSTALLATION_STATUS == "installed-protected-bootstrap",
            "bootstrap-source-uninstalled")
    facts = context(environment)
    source_manifest, source_retained = validate_source_artifacts(candidate_checkout, facts)
    manifest, retained = validate_artifacts(runtime_dir, facts)
    require(retained["seed-plan.json"] == source_retained["seed-plan.json"]
            and retained["corpus.json"] == source_retained["corpus.json"],
            "postmint-source-drift")
    declaration_bytes = retained["s2-declaration.json"]
    seed_plan_bytes = retained["seed-plan.json"]
    cas = load_sibling("gs2_seed_native_cas", "gs2-09-7-seed-native-cas.py")
    binding = load_sibling("gs2_seed_execution_binding", "gs2-09-7-seed-execution-binding.py")
    import base64
    proposal = {
        "runId": facts["runId"], "runAttempt": facts["runAttempt"],
        "candidateSha": facts["candidateSha"], "workflowSha": facts["workflowSha"],
        "runNonce": facts["runNonce"], "refName": manifest["refName"],
        "journalGeneration": 0, "stateGeneration": 0, "expectedParent": None,
        "stateSha256": manifest["stateSha256"], "blobOid": manifest["blobOid"],
        "treeOid": manifest["treeOid"], "commitOid": manifest["commitOid"],
        "stateBytesBase64": base64.b64encode(retained["journal/state.json"]).decode(),
        "treeBytesBase64": base64.b64encode(retained["journal/tree.raw"]).decode(),
        "commitBytesBase64": base64.b64encode(retained["journal/commit.raw"]).decode(),
        "s2DeclarationSha256": digest(declaration_bytes),
        "seedPlanSha256": digest(seed_plan_bytes),
    }
    item = cas.inspect_proposal(proposal)
    cas.verify_declaration(declaration_bytes, seed_plan_bytes, proposal)
    require(proposal["workflowSha"] == facts["workflowSha"]
            and proposal["candidateSha"] == facts["candidateSha"]
            and proposal["runId"] == facts["runId"]
            and proposal["runAttempt"] == facts["runAttempt"]
            and proposal["runNonce"] == facts["runNonce"], "proposal-run-binding")
    mint = binding.validate_mint(mint_proof_bytes, token, now)
    repository = strict_json(repository_readback_bytes)
    require(repository.get("id") == 1353050537
            and repository.get("node_id") == "R_kgDOUKXpqQ"
            and repository.get("full_name") == "FS-GG/FS.GG.GitHub.Substrate.Sandbox"
            and repository.get("private") is True
            and repository.get("fork") is False,
            "sandbox-repository-readback")
    project = strict_json(project_readback_bytes)
    require(project.get("id") == "PVT_kwDOEYAWY84BiESo"
            and project.get("title") == "fsgg-sandbox-gs2-04-9"
            and project.get("closed") is False and project.get("public") is False,
            "sandbox-project-readback")
    declaration = cas.strict_json(declaration_bytes)
    source = declaration["source"]
    require(source["approvedArtifactSourceSha256"]
            == source_manifest["approvedArtifactSourceSha256"], "s2-approved-source-drift")
    subject = {
        "workflowRepository": HOST, "workflowPath": WORKFLOW,
        "environment": "github-substrate-v2-sandbox",
        "workflowSha": facts["workflowSha"], "runId": facts["runId"],
        "runAttempt": facts["runAttempt"], "candidateSha": facts["candidateSha"],
        "runNonce": facts["runNonce"],
        "approvedArtifactSourceSha256": source["approvedArtifactSourceSha256"],
        "sandboxRepositoryId": 1353050537,
        "sandboxRepositoryNodeId": "R_kgDOUKXpqQ",
        "projectNodeId": "PVT_kwDOEYAWY84BiESo",
        "appId": mint["appId"], "installationId": mint["installationId"],
        "mintProofSha256": mint["proofSha256"],
        "tokenSha256": mint["tokenSha256"],
        "seedPlanSha256": proposal["seedPlanSha256"],
        "s2DeclarationSha256": proposal["s2DeclarationSha256"],
        "refName": item["ref"], "blobOid": proposal["blobOid"],
        "treeOid": proposal["treeOid"], "commitOid": proposal["commitOid"],
        "expectedOldOid": None, "operation": "genesis-nonce-seed-journal",
    }
    return proposal, declaration_bytes, seed_plan_bytes, subject


def execute_source(environment: dict[str, str], candidate_checkout: Path,
                   runtime_dir: Path, mint_proof_bytes: bytes, token: str,
                   repository_readback_bytes: bytes, project_readback_bytes: bytes,
                   admission_port, now: dt.datetime) -> dict:
    proposal, declaration_bytes, seed_plan_bytes, subject = inspect_source(
        environment, candidate_checkout, runtime_dir, mint_proof_bytes, token,
        repository_readback_bytes, project_readback_bytes, now)
    root = private_root(environment)
    facts = context(environment)
    subject = attach_final_prestate(subject, root, runtime_dir, facts)
    admission = load_sibling("gs2_seed_bootstrap_admission", "gs2-09-7-seed-bootstrap-admission.py")
    decision = admission.require_admitted(admission_port, subject, now)
    decision_bytes = read_private(root / "final-admission.private.json")
    authorizer = load_sibling("gs2_seed_admission_authorizer",
                              "gs2-09-7-seed-admission-authorizer.py")
    require(decision_bytes == authorizer.canonical(decision),
            "final-admission-private-drift")
    initial_summary = read_private(root / "prestate.json")
    initial_evidence = read_private(root / "prestate-evidence.private.json",
                                    64 * 1024 * 1024)
    fresh_summary = read_private(root / "final-prestate.private.json")
    fresh_evidence = read_private(root / "final-prestate-evidence.private.json",
                                  64 * 1024 * 1024)
    validate_prestate(fresh_summary, facts)
    fresh = strict_json(fresh_evidence, 64 * 1024 * 1024)
    require(fresh.get("runNonce") == facts["runNonce"]
            and fresh.get("refName") == subject["refName"]
            and fresh.get("expectedRefAbsent") is True
            and fresh.get("summarySha256") == digest(fresh_summary)
            and fresh.get("snapshotSha256") == subject["prestateSnapshotSha256"],
            "final-prestate-drift")
    runtime = require_bootstrap_runtime()
    report = runtime.establish_and_write(
        proposal=proposal, binding_bytes=declaration_bytes,
        seed_plan_bytes=seed_plan_bytes, final_admission_bytes=decision_bytes,
        prestate_bytes=initial_summary, initial_evidence_bytes=initial_evidence,
        fresh_prestate_bytes=fresh_summary, fresh_evidence_bytes=fresh_evidence,
        token=token, facts=facts)
    require(type(report) is dict and report.get("schema") ==
            "fsgg.gs2-09-7.sandbox-nonce-ref-cas-readback/1"
            and report.get("repositoryId") == 1353050537
            and report.get("refName") == subject["refName"]
            and report.get("runNonce") == facts["runNonce"]
            and report.get("newOid") == proposal["commitOid"],
            "trusted-bootstrap-readback")
    return report


def main(arguments: list[str]) -> int:
    try:
        require(arguments in (["preflight"], ["prepare-request"], ["seal-input"],
                              ["consume-seal-and-request-final"],
                              ["request-info", "prepare"], ["request-info", "final"],
                              ["final-apply"],
                              ["revoke-private"]), "mode")
        if arguments == ["preflight"]:
            preflight(Path("host"), Path("coordination"), dict(os.environ))
        elif arguments == ["prepare-request"]:
            stage_prepare_request(Path("host"), Path("coordination"), dict(os.environ))
        elif arguments[:1] == ["request-info"]:
            request_info(dict(os.environ), arguments[1])
        elif arguments == ["seal-input"]:
            stage_seal_input(Path("host"), Path("coordination"), dict(os.environ))
        elif arguments == ["consume-seal-and-request-final"]:
            stage_consume_seal_and_final_request(Path("host"), Path("coordination"), dict(os.environ))
        elif arguments == ["final-apply"]:
            stage_final_apply(Path("host"), Path("coordination"), dict(os.environ))
        else:
            try:
                root = private_root(dict(os.environ))
                if not revoke_private_token(root):
                    promote_readback(root)
            except Refused as error:
                if str(error) != "private-root-mode":
                    raise
    except (Refused, OSError, KeyError, ValueError, TypeError) as error:
        print(f"GS2-SEED-BOOTSTRAP: refused {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
