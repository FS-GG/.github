#!/usr/bin/env python3
"""Fail-closed protected Q4 journal bootstrap entry point.

The Coordination S1 library has no installed CLI producing the retained exact
proposal and source declaration. Independent admission has no installed port.
This pre-mint check makes either absence terminal before App secret use.
"""

from __future__ import annotations

import hashlib
import importlib.util
import json
import os
import re
import sys
import datetime as dt
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


def strict_json(raw: bytes) -> dict:
    require(0 < len(raw) <= 1024 * 1024, "manifest-size")
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


FILES = ["s2-declaration.json", "seed-plan.json", "corpus.json",
         "journal/state.json", "journal/tree.raw", "journal/commit.raw"]


def approved_source(candidate: str, plan_sha: str, corpus_sha: str) -> str:
    raw = ("fsgg.gs2-09-7.sandbox-seed-approved-source/1\n"
           f"candidateSha={candidate}\nseed-plan.json={plan_sha}\n"
           f"corpus.json={corpus_sha}\n").encode()
    return digest(raw)


def validate_source_artifacts(checkout: Path, facts: dict) -> tuple[dict, dict[str, bytes]]:
    try:
        path = regular_inside(checkout, SOURCE_MANIFEST)
    except Refused as error:
        raise Refused("coordination-source-producer-uninstalled") from error
    raw = path.read_bytes()
    require(digest(raw) == facts["manifestSha256"], "source-manifest-digest")
    manifest = strict_json(raw)
    require(set(manifest) == {"schema", "status", "candidateSha",
                              "approvedArtifactSourceSha256", "seedPlan", "corpus",
                              "postMintSealRequired", "bootstrapAuthority",
                              "providerEffectsAuthorized"}
            and manifest["schema"] == SOURCE_SCHEMA
            and manifest["status"] == "source-only-no-authority"
            and manifest["candidateSha"] == facts["candidateSha"]
            and manifest["postMintSealRequired"] is True
            and manifest["bootstrapAuthority"] is False
            and manifest["providerEffectsAuthorized"] is False,
            "source-manifest-binding")
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
    require(manifest["approvedArtifactSourceSha256"] == approved_source(
        facts["candidateSha"], manifest["seedPlan"]["sha256"],
        manifest["corpus"]["sha256"]), "source-approved-digest")
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
              environment: dict[str, str]) -> dict:
    facts = context(environment)
    require(host_checkout.resolve().is_dir() and candidate_checkout.resolve().is_dir(),
            "checkout-unavailable")
    require(INSTALLATION_STATUS == "installed-protected-bootstrap",
            "bootstrap-source-uninstalled")
    source, _ = validate_source_artifacts(candidate_checkout, facts)
    admission = load_sibling("gs2_seed_bootstrap_admission", "gs2-09-7-seed-bootstrap-admission.py")
    try:
        admission.require_prepare_admitted(
            PREPARE_ADMISSION_PORT, facts, facts["manifestSha256"],
            source["approvedArtifactSourceSha256"], dt.datetime.now(dt.timezone.utc))
    except admission.Refused as error:
        raise Refused(f"prepare-{error}") from error
    return facts


def execute_source(environment: dict[str, str], candidate_checkout: Path,
                   runtime_dir: Path,
                   mint_proof_bytes: bytes,
                   token: str, repository_readback_bytes: bytes,
                   project_readback_bytes: bytes, admission_port, git_port,
                   now: dt.datetime) -> dict:
    """Compose exact-source checks; installation must supply protected ports."""
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
    admission = load_sibling("gs2_seed_bootstrap_admission", "gs2-09-7-seed-bootstrap-admission.py")
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
    admission.require_admitted(admission_port, subject, now)
    # The CAS module's separate installation flag must also be armed by the
    # protected owner. No caller-controlled manifest can switch it on.
    return cas.apply(proposal, declaration_bytes, seed_plan_bytes, git_port,
                     protected_grant_verified=True)


def main(arguments: list[str]) -> int:
    try:
        require(arguments == ["preflight"], "mode")
        preflight(Path("host"), Path("coordination"), dict(os.environ))
    except (Refused, OSError) as error:
        print(f"GS2-SEED-BOOTSTRAP: refused {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
