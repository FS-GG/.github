#!/usr/bin/env python3
"""Source-only Q4 S2 binding for a protected sandbox seed execution.

No workflow calls this helper. Its protected-workflow and journal installation
pins are deliberately empty, so build and verify remain unavailable until a
separate protected-main join installs both before any seed write.
"""

from __future__ import annotations

import datetime as dt
import hashlib
import json
import os
import re
import stat
import sys
from pathlib import Path


SCHEMA = "fsgg.github-substrate-v2.sandbox-seed-execution-binding/1"
STATUS_SCHEMA = "fsgg.github-substrate-v2.sandbox-seed-execution-binding-source/1"
MINT_SCHEMA = "fsgg.github-substrate-v2.sandbox-mint-grants/1"
HOST_REPOSITORY = "FS-GG/.github"
WORKFLOW_PATH = ".github/workflows/github-substrate-v2-sandbox-qualification.yml"
WORKFLOW_REF = "refs/heads/main"
BUILDER_PATH = "scripts/gs2-09-7-seed-execution-binding.py"
SANDBOX_ID = 1353050537
SANDBOX_NODE = "R_kgDOUKXpqQ"
SANDBOX_NAME = "FS-GG/FS.GG.GitHub.Substrate.Sandbox"
PROJECT_NODE = "PVT_kwDOEYAWY84BiESo"
APP_ID = 4166418
INSTALLATION_ID = 143110413
APP_SLUG = "fs-gg-cross-repo-dispatch"
ACTOR = {"login": "fs-gg-cross-repo-dispatch[bot]", "databaseId": 297630107}
REQUIRED_GRANTS = {
    "administration": "write",
    "contents": "write",
    "issues": "write",
    "metadata": "read",
    "organization_projects": "write",
    "pull_requests": "write",
}
ALLOWED_EFFECT_KINDS = [
    "CreateNonceIssue",
    "AddProjectMembership",
    "RemoveProjectMembership",
    "DeleteNonceIssue",
]
HEX40 = re.compile(r"[0-9a-f]{40}\Z")
HEX64 = re.compile(r"[0-9a-f]{64}\Z")
MAX_JSON_BYTES = 1024 * 1024
MAX_ARTIFACT_BYTES = 64 * 1024 * 1024

# These values can only be installed with the fixed protected-main workflow
# and its protected journal. Environment or CLI values never substitute.
INSTALLATION_STATUS = "source-only-uninstalled"
PINNED_WORKFLOW_SHA = ""
PINNED_JOURNAL_IDENTITY = ""

FORBIDDEN_CONTEXT_ENV = {
    "FSGG_EXECUTION_BINDING_CONTEXT",
    "FSGG_WORKFLOW_REPOSITORY",
    "FSGG_WORKFLOW_PATH",
    "FSGG_WORKFLOW_REF",
    "FSGG_WORKFLOW_SHA",
}


class Refused(Exception):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


def unique_pairs(pairs: list[tuple[str, object]]) -> dict:
    value = {}
    for name, item in pairs:
        require(name not in value, "duplicate-json-member")
        value[name] = item
    return value


def reject_nonfinite(_value: str) -> object:
    raise Refused("nonfinite-number")


def strict_json(raw: bytes) -> dict:
    require(type(raw) is bytes and 0 < len(raw) <= MAX_JSON_BYTES, "json-size")
    try:
        value = json.loads(raw.decode("utf-8"), object_pairs_hook=unique_pairs,
                           parse_constant=reject_nonfinite)
    except (UnicodeError, ValueError, TypeError, RecursionError) as error:
        raise Refused("malformed-json") from error
    require(type(value) is dict, "object-required")
    return value


def sha256(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def canonical(value: dict) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":"),
                       ensure_ascii=True, allow_nan=False) + "\n").encode("ascii")


def utc_now() -> dt.datetime:
    return dt.datetime.now(dt.timezone.utc)


def read_regular(path: Path, limit: int) -> bytes:
    flags = os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0)
    try:
        descriptor = os.open(path, flags)
    except OSError as error:
        raise Refused("retained-bytes-unavailable") from error
    with os.fdopen(descriptor, "rb") as stream:
        require(stat.S_ISREG(os.fstat(stream.fileno()).st_mode), "retained-bytes-nonregular")
        raw = stream.read(limit + 1)
    require(0 < len(raw) <= limit, "retained-bytes-size")
    return raw


def retained_artifact(evidence_dir: Path, env_name: str, label: str) -> tuple[dict, bytes]:
    raw_path = os.environ.get(env_name, "")
    require(bool(raw_path), f"{label}-path")
    path = Path(raw_path)
    require(path.is_absolute(), f"{label}-path")
    try:
        relative = path.relative_to(evidence_dir)
    except ValueError as error:
        raise Refused(f"{label}-path") from error
    require(len(relative.parts) == 1 and relative.name not in ("", ".", ".."), f"{label}-path")
    raw = read_regular(path, MAX_ARTIFACT_BYTES)
    return {
        "request": env_name,
        "path": relative.as_posix(),
        "contentSchema": "opaque-retained-bytes-pending-coordination-s1",
        "byteLength": len(raw),
        "sha256": sha256(raw),
    }, raw


def source_status() -> dict:
    return {
        "schema": STATUS_SCHEMA,
        "installation": INSTALLATION_STATUS,
        "protectedWorkflowPinConfigured": bool(HEX40.fullmatch(PINNED_WORKFLOW_SHA)),
        "journalPinConfigured": bool(PINNED_JOURNAL_IDENTITY),
        "writesEnabled": False,
        "authority": "unavailable",
    }


def protected_context() -> dict:
    require(INSTALLATION_STATUS == "installed-fixed-main-workflow", "execution-binding-uninstalled")
    require(HEX40.fullmatch(PINNED_WORKFLOW_SHA) is not None, "protected-workflow-pin")
    require(bool(PINNED_JOURNAL_IDENTITY)
            and PINNED_JOURNAL_IDENTITY.strip() == PINNED_JOURNAL_IDENTITY
            and len(PINNED_JOURNAL_IDENTITY) <= 256, "journal-identity-pin")
    require(not any(name in os.environ for name in FORBIDDEN_CONTEXT_ENV), "caller-context-spoof")
    required = {
        "GITHUB_ACTIONS", "CI", "GITHUB_EVENT_NAME", "GITHUB_REPOSITORY", "GITHUB_REF",
        "GITHUB_WORKFLOW_REF", "GITHUB_SHA", "FSGG_PROTECTED_SHA", "GITHUB_RUN_ID",
        "GITHUB_RUN_ATTEMPT", "FSGG_CANDIDATE_SHA", "FSGG_SANDBOX_RUN_NONCE",
        "FSGG_SANDBOX_REPOSITORY_ID", "FSGG_SANDBOX_REPOSITORY_NODE_ID",
        "FSGG_SANDBOX_PROJECT_NODE_ID", "FSGG_SEED_JOURNAL_IDENTITY",
    }
    require(all(name in os.environ for name in required), "protected-context-missing")
    try:
        run_id = int(os.environ["GITHUB_RUN_ID"])
        run_attempt = int(os.environ["GITHUB_RUN_ATTEMPT"])
        repository_id = int(os.environ["FSGG_SANDBOX_REPOSITORY_ID"])
    except ValueError as error:
        raise Refused("protected-context-number") from error
    workflow_sha = os.environ["GITHUB_SHA"]
    candidate_sha = os.environ["FSGG_CANDIDATE_SHA"]
    nonce = f"{run_id}-{run_attempt}-{candidate_sha}"
    expected_workflow_ref = f"{HOST_REPOSITORY}/{WORKFLOW_PATH}@{WORKFLOW_REF}"
    require(os.environ["GITHUB_ACTIONS"] == "true" and os.environ["CI"] == "true"
            and os.environ["GITHUB_EVENT_NAME"] == "workflow_dispatch"
            and os.environ["GITHUB_REPOSITORY"] == HOST_REPOSITORY
            and os.environ["GITHUB_REF"] == WORKFLOW_REF
            and os.environ["GITHUB_WORKFLOW_REF"] == expected_workflow_ref
            and HEX40.fullmatch(workflow_sha) is not None
            and workflow_sha == PINNED_WORKFLOW_SHA
            and os.environ["FSGG_PROTECTED_SHA"] == workflow_sha
            and run_id > 0 and run_attempt > 0
            and HEX40.fullmatch(candidate_sha) is not None
            and os.environ["FSGG_SANDBOX_RUN_NONCE"] == nonce
            and repository_id == SANDBOX_ID
            and os.environ["FSGG_SANDBOX_REPOSITORY_NODE_ID"] == SANDBOX_NODE
            and os.environ["FSGG_SANDBOX_PROJECT_NODE_ID"] == PROJECT_NODE
            and os.environ["FSGG_SEED_JOURNAL_IDENTITY"] == PINNED_JOURNAL_IDENTITY,
            "protected-context")
    return {
        "workflowRepository": HOST_REPOSITORY,
        "workflowPath": WORKFLOW_PATH,
        "workflowRef": WORKFLOW_REF,
        "workflowRefPath": expected_workflow_ref,
        "workflowSha": workflow_sha,
        "runId": run_id,
        "runAttempt": run_attempt,
        "candidateSha": candidate_sha,
        "runNonce": nonce,
    }


def validate_mint(raw: bytes, token: str, now: dt.datetime) -> dict:
    require(type(token) is str and len(token) > 20 and token.isascii()
            and not any(character.isspace() for character in token), "token")
    require(type(now) is dt.datetime and now.tzinfo is not None
            and now.utcoffset() is not None, "clock")
    proof = strict_json(raw)
    require(set(proof) == {
        "schema", "appId", "appSlug", "actor", "installationId", "repositorySelection",
        "repository", "permissions", "expiresAt", "tokenSha256", "mintResponseSha256",
        "viewerResponseSha256",
    }, "mint-proof-shape")
    require(proof["schema"] == MINT_SCHEMA and proof["appId"] == APP_ID
            and proof["appSlug"] == APP_SLUG and proof["actor"] == ACTOR
            and type(proof["installationId"]) is int
            and proof["installationId"] == INSTALLATION_ID
            and proof["repositorySelection"] == "selected"
            and proof["repository"] == {"id": SANDBOX_ID, "nodeId": SANDBOX_NODE,
                                         "fullName": SANDBOX_NAME}
            and proof["permissions"] == REQUIRED_GRANTS,
            "mint-proof-identity")
    token_digest = sha256(token.encode("ascii"))
    require(proof["tokenSha256"] == token_digest
            and all(type(proof[name]) is str and HEX64.fullmatch(proof[name])
                    for name in ("tokenSha256", "mintResponseSha256", "viewerResponseSha256")),
            "mint-proof-digest")
    expiry_text = proof["expiresAt"]
    require(type(expiry_text) is str and expiry_text.endswith("Z"), "mint-proof-expiry")
    try:
        expiry = dt.datetime.fromisoformat(expiry_text[:-1] + "+00:00")
    except (ValueError, OverflowError) as error:
        raise Refused("mint-proof-expiry") from error
    require(now.astimezone(dt.timezone.utc) < expiry <= now.astimezone(dt.timezone.utc) + dt.timedelta(hours=2),
            "mint-proof-expiry")
    return {
        "schema": MINT_SCHEMA,
        "proofSha256": sha256(raw),
        "tokenSha256": token_digest,
        "expiresAt": expiry_text,
        "appId": APP_ID,
        "installationId": INSTALLATION_ID,
    }


def build_document() -> bytes:
    context = protected_context()
    evidence_dir_text = os.environ.get("FSGG_SANDBOX_EVIDENCE_DIR", "")
    require(bool(evidence_dir_text), "evidence-directory")
    evidence_dir = Path(evidence_dir_text)
    require(evidence_dir.is_absolute() and evidence_dir.is_dir(), "evidence-directory")
    plan, _ = retained_artifact(evidence_dir, "FSGG_SEED_PLAN_PATH", "seed-plan")
    corpus, _ = retained_artifact(evidence_dir, "FSGG_SEED_CORPUS_PATH", "seed-corpus")
    require(plan["path"] != corpus["path"] and plan["sha256"] != corpus["sha256"],
            "retained-artifacts-ambiguous")
    mint_path_text = os.environ.get("FSGG_SANDBOX_MINT_PROOF", "")
    require(bool(mint_path_text), "mint-proof-path")
    mint_path = Path(mint_path_text)
    try:
        mint_relative = mint_path.relative_to(evidence_dir)
    except ValueError as error:
        raise Refused("mint-proof-path") from error
    require(len(mint_relative.parts) == 1, "mint-proof-path")
    mint_raw = read_regular(mint_path, MAX_JSON_BYTES)
    mint = validate_mint(mint_raw, os.environ.get("FSGG_SANDBOX_TOKEN", ""), utc_now())
    workspace_text = os.environ.get("GITHUB_WORKSPACE", "")
    require(bool(workspace_text), "workspace")
    workspace = Path(workspace_text)
    builder = workspace / BUILDER_PATH
    builder_raw = read_regular(builder, MAX_JSON_BYTES)
    binding = {
        "schema": SCHEMA,
        "status": "bound-no-write-authority",
        "activation": False,
        "source": {
            "repository": HOST_REPOSITORY,
            "builderPath": BUILDER_PATH,
            "builderSha256": sha256(builder_raw),
            **context,
        },
        "sandbox": {
            "repositoryId": SANDBOX_ID,
            "repositoryNodeId": SANDBOX_NODE,
            "projectNodeId": PROJECT_NODE,
        },
        "mint": mint,
        "artifacts": {"seedPlan": plan, "corpus": corpus},
        "journal": {
            "identity": PINNED_JOURNAL_IDENTITY,
            "allowedClosedEffectKinds": ALLOWED_EFFECT_KINDS,
        },
        "schemaJoin": "pending-coordination-s1-final",
        "authority": "unavailable-without-protected-workflow-verification",
    }
    binding["fingerprint"] = sha256(canonical(binding))
    return canonical(binding)


def write_new(path: Path, raw: bytes) -> None:
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_CLOEXEC", 0)
    try:
        descriptor = os.open(path, flags, 0o600)
    except OSError as error:
        raise Refused("ambiguous-output") from error
    with os.fdopen(descriptor, "wb") as stream:
        stream.write(raw)


def verify_document(path: Path) -> None:
    actual = read_regular(path, MAX_JSON_BYTES)
    strict_json(actual)
    require(actual == build_document(), "execution-binding-mismatch")


def main(arguments: list[str]) -> int:
    try:
        require(len(arguments) == 1 and arguments[0] in ("status", "build", "verify"), "mode")
        if arguments[0] == "status":
            print(canonical(source_status()).decode("ascii"), end="")
            return 0
        output_text = os.environ.get("FSGG_SEED_EXECUTION_BINDING", "")
        require(bool(output_text), "output-path")
        output = Path(output_text)
        if arguments[0] == "build":
            write_new(output, build_document())
        else:
            verify_document(output)
        print(f"GS2-SEED-EXECUTION-BINDING: {arguments[0]} complete")
        return 0
    except (Refused, OSError, KeyError, TypeError, ValueError, OverflowError) as error:
        reason = str(error) if isinstance(error, Refused) else "unavailable-or-malformed"
        print(f"GS2-SEED-EXECUTION-BINDING: refused {reason}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
