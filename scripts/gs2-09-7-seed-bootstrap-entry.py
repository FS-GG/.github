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
import subprocess
import tempfile
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


def write_private(path: Path, raw: bytes) -> None:
    require(path.is_absolute() and path.parent.is_dir() and not path.parent.is_symlink(),
            "private-output-path")
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL |
                         getattr(os, "O_NOFOLLOW", 0), 0o600)
    with os.fdopen(descriptor, "wb") as stream:
        stream.write(raw)


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


def require_postmint_ready(host_checkout: Path, candidate_checkout: Path,
                           workflow_sha: str) -> tuple[object, object, object]:
    """Check every locally installed operation dependency before App mint."""
    binding = load_sibling("gs2_seed_execution_binding", "gs2-09-7-seed-execution-binding.py")
    cas = load_sibling("gs2_seed_native_cas", "gs2-09-7-seed-native-cas.py")
    admission = load_sibling("gs2_seed_bootstrap_admission", "gs2-09-7-seed-bootstrap-admission.py")
    require(binding.INSTALLATION_STATUS == "installed-fixed-main-workflow"
            and cas.INSTALLATION_STATUS == "installed-protected-host",
            "postmint-source-uninstalled")
    try:
        admission.check_port(FINAL_ADMISSION_PORT)
    except admission.Refused as error:
        raise Refused(f"final-{error}") from error
    require(PRESTATE_PRODUCER_PORT is not None
            and callable(getattr(PRESTATE_PRODUCER_PORT, "describe", None))
            and callable(getattr(PRESTATE_PRODUCER_PORT, "produce", None)),
            "prestate-producer-uninstalled")
    require(PRESTATE_PRODUCER_PORT.describe() == {
        "schema": "fsgg.gs2-09-7.sandbox-seed-prestate-producer/1",
        "repositoryId": 1353050537,
        "projectNodeId": "PVT_kwDOEYAWY84BiESo",
        "source": "fresh-native-issue-and-project-pages",
        "credentialScope": "protected-host-only",
        "candidateCanWrite": False,
    }, "prestate-producer-identity")
    regular_inside(candidate_checkout, COORDINATION_CLI_PROJECT)
    require(binding.checked_out_provenance(workflow_sha)["checkoutHead"] == workflow_sha,
            "protected-binding-provenance")
    for relative in ("scripts/gs2-09-7-seed-bootstrap-entry.py",
                     "scripts/gs2-09-7-seed-bootstrap-admission.py",
                     "scripts/gs2-09-7-mint-sandbox-token.py"):
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


def produce_prestate(token: str, facts: dict) -> bytes:
    try:
        result = PRESTATE_PRODUCER_PORT.produce(token, facts)
    except Exception as error:
        raise Refused("prestate-producer-readback") from error
    require(type(result) is dict and set(result) == {
        "runNonce", "raw", "captureId", "observedAt", "source"}
        and result["runNonce"] == facts["runNonce"]
        and type(result["raw"]) is bytes
        and type(result["captureId"]) is str
        and HEX64.fullmatch(result["captureId"]) is not None
        and result["source"] == "fresh-native-issue-and-project-pages",
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
    return result["raw"]


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


def seal_with_coordination(candidate_checkout: Path, facts: dict, private_root: Path,
                           source: dict, s2_path: Path, proof_path: Path,
                           prestate_path: Path) -> Path:
    output = private_root / "sealed"
    require(not output.exists(), "seal-output-exists")
    source_root = candidate_checkout / Path(SOURCE_MANIFEST).parent
    command = ["dotnet", "run", "--project", COORDINATION_CLI_PROJECT, "--",
               "seed-bootstrap-artifacts", "seal",
               "--candidate-sha", facts["candidateSha"],
               "--workflow-run-id", str(facts["runId"]),
               "--workflow-run-attempt", str(facts["runAttempt"]),
               "--workflow-sha", facts["workflowSha"],
               "--source-manifest", str(source_root / "source-manifest.json"),
               "--s2-declaration", str(s2_path),
               "--mint-proof", str(proof_path),
               "--seed-plan", str(source_root / source["seedPlan"]["path"]),
               "--corpus", str(source_root / source["corpus"]["path"]),
               "--prestate", str(prestate_path),
               "--output-dir", str(output)]
    # Candidate code receives only SDK/runtime settings. In particular, the
    # App key, installation token and Actions runtime credentials stay outside
    # the Coordination process and its child processes.
    allowed = {"PATH", "HOME", "DOTNET_ROOT", "DOTNET_CLI_HOME", "NUGET_PACKAGES",
               "LANG", "LC_ALL", "CI"}
    clean_env = {key: value for key, value in os.environ.items() if key in allowed}
    try:
        completed = subprocess.run(command, cwd=candidate_checkout, env=clean_env,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                   timeout=180, check=False)
    except (OSError, subprocess.SubprocessError) as error:
        raise Refused("coordination-seal-unavailable") from error
    require(completed.returncode == 0 and len(completed.stdout) <= 4096
            and completed.stdout.strip() == str(output / DERIVED_MANIFEST).encode(),
            "coordination-seal-refused")
    return output


def run_protected(host_checkout: Path, candidate_checkout: Path,
                  environment: dict[str, str]) -> dict:
    """One protected run; all missing installation dependencies refuse before mint."""
    facts = preflight(host_checkout, candidate_checkout, environment)
    binding, cas, _ = require_postmint_ready(host_checkout, candidate_checkout,
                                             facts["workflowSha"])
    source, retained = validate_source_artifacts(candidate_checkout, facts)
    sanitized_dir = Path(environment["FSGG_SEED_SANITIZED_EVIDENCE_DIR"])
    require(sanitized_dir.is_absolute() and not sanitized_dir.exists(),
            "sanitized-output-path")
    mint_module = load_sibling("gs2_seed_mint", "gs2-09-7-mint-sandbox-token.py")
    token = None
    with tempfile.TemporaryDirectory(prefix="gs2-seed-bootstrap-") as temporary:
        private_root = Path(temporary).resolve()
        os.chmod(private_root, 0o700)
        try:
            token, proof = mint_private(mint_module, environment)
            proof_path = private_root / "mint-grants.json"
            write_private(proof_path, proof)
            plan_path = private_root / "seed-plan.json"
            corpus_path = private_root / "corpus.json"
            write_private(plan_path, retained["seed-plan.json"])
            write_private(corpus_path, retained["corpus.json"])
            prestate = produce_prestate(token, facts)
            prestate_path = private_root / "prestate.json"
            write_private(prestate_path, prestate)
            with temporary_environment({
                "FSGG_SANDBOX_EVIDENCE_DIR": str(private_root),
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
            s2_path = private_root / "s2-declaration.json"
            write_private(s2_path, declaration)
            sealed_dir = seal_with_coordination(candidate_checkout, facts, private_root,
                                                source, s2_path, proof_path, prestate_path)
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
            with cas.authenticated_port(token) as git_port:
                report = execute_source(environment, candidate_checkout, sealed_dir,
                                        proof, token, repository_raw, project_raw,
                                        FINAL_ADMISSION_PORT, git_port,
                                        dt.datetime.now(dt.timezone.utc))
        finally:
            if token is not None:
                mint_module.revoke_token(token)
        # A complete native envelope is retained only after token revocation.
        # Pending readback is likewise retained, but remains non-authorizing.
        sanitized_dir.mkdir(mode=0o700)
        write_private(sanitized_dir / "native-readback.json", cas.canonical(report))
        require(report.get("complete") is True, "native-readback-pending")
        return report


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
        require(arguments in (["preflight"], ["run"]), "mode")
        if arguments == ["preflight"]:
            preflight(Path("host"), Path("coordination"), dict(os.environ))
        else:
            run_protected(Path("host"), Path("coordination"), dict(os.environ))
    except (Refused, OSError, KeyError, ValueError, TypeError, subprocess.SubprocessError) as error:
        print(f"GS2-SEED-BOOTSTRAP: refused {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
