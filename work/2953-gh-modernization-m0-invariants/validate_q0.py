#!/usr/bin/env python3
"""Fail-closed, source-bound controls for the GS2-00 Q0 evidence."""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import re
import subprocess
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[2]
SHA256 = re.compile(r"^[0-9a-f]{64}$")
GIT_SHA = re.compile(r"^[0-9a-f]{40}$")
TREE_INVENTORIES = {
    "workflows": ".github/workflows", "scripts": "scripts", "core": "src/FS.GG.Coord.Core",
    "github": "src/FS.GG.Coord.GitHub", "cli": "src/FS.GG.Coord.Cli",
    "lifecycle": "src/FS.GG.Coord.Cli.Lifecycle", "boardOps": "src/FS.GG.Coord.Cli.BoardOps",
    "registry": "registry", "skills": ".agents/skills",
}
COMMAND_INVENTORIES = {
    "protocolFacts": ["scripts/fsgg-coord", "facts", "--json"],
    "commandContract": ["scripts/fsgg-coord", "command-contract", "--json"],
}
REQUIRED_AUTHORITY = {
    "issueBody", "comment", "project", "registry", "workflow", "command", "jsonContract",
    "environment", "file", "package", "schedule", "setting", "external",
}
REQUIRED_MUTATION = {"command", "workflow", "release", "repairScript", "administrative", "appRoute"}
REQUIRED_CORPUS = {
    "claim", "touchSet", "dependency", "hierarchy", "intake", "review", "delivery", "merge",
    "release", "pagination", "rateLimit", "partialWrite", "staleRead", "selfHosting", "churn72h",
    "mutationEntry", "protocolString", "replay", "omission", "misclassification", "byteCompatibility",
}
REQUIRED_CLASSIFICATIONS = {"Preserve", "Migrate", "Seal", "Retire"}
REQUIRED_HANDOFF = {
    "v2-unit", "v2-blocker", "parallel-product", "candidate-input-change",
    "superseded-inventory", "cutover-deferred",
}
REQUIRED_THREAT_BOUNDARIES = {
    "protectedEpoch", "administrativePrincipal", "githubMutableState",
    "packageSupplyChain", "crossRepositoryReceiver",
}
REQUIRED_REVIEW_ROLES = {"architecture", "security", "operations", "crossRepository"}
REVIEW_ROLE_TEXT = {
    "architecture": "architecture",
    "security": "security",
    "operations": "operations",
    "crossRepository": "cross-repository",
}
REVIEW_REPOSITORY = "FS-GG/.github"
REVIEW_PULL_REQUEST = 3002
AUTHORIZED_REVIEW_AUTHORS = {"EHotwagner"}
AUTHORIZED_REVIEW_ASSOCIATIONS = {"OWNER", "MEMBER", "COLLABORATOR"}
# These hashes bind the independently adjudicated semantic baseline, not merely its
# presence in the role-signed subject. Recomputing reviewFingerprint after weakening a
# verdict or deletion obligation therefore remains red before another review is sought.
REQUIRED_CORPUS_CONTRACT_SHA256 = "654902d795efc673353df4778f65fd43546b970e831a78f087e367dd6b9e59ef"
REQUIRED_DELETION_CONTRACT_SHA256 = "17a05d11c1378ab9dc44c535c3d25f80b5e44e93eea3ae20f6cb5a179ea6161d"


def digest_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def canonical_digest(value: Any) -> str:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()
    return digest_bytes(encoded)


def run_bytes(command: list[str]) -> bytes:
    return subprocess.run(command, cwd=ROOT, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE).stdout


def parse_role_comment(comment: dict[str, Any], live_head: str, fingerprint: str) -> dict[str, str] | None:
    """Parse exact positive fields; negated prose and arbitrary substrings have no authority."""
    if comment.get("created_at") != comment.get("updated_at"):
        return None
    author = comment.get("user")
    if (
        not isinstance(author, dict)
        or author.get("login") not in AUTHORIZED_REVIEW_AUTHORS
        or author.get("type") != "User"
        or comment.get("author_association") not in AUTHORIZED_REVIEW_ASSOCIATIONS
    ):
        return None
    body = str(comment.get("body", ""))
    parsed: dict[str, str] | None = None
    for role, heading in REVIEW_ROLE_TEXT.items():
        pattern = (
            rf"\A## Q0 {re.escape(heading)} role review\n\n"
            rf"- Reviewer identity: `([^`\n]+)`\n"
            rf"- Role: `{re.escape(role)}`\n"
            rf"- Verdict: \*\*accepted\*\*\n"
            rf"- Exact PR head: `{re.escape(live_head)}`\n"
            rf"- Canonical Q0 fingerprint: `{re.escape(fingerprint)}`\n?\Z"
        )
        match = re.match(pattern, body)
        if match:
            parsed = {
                "reviewer": match.group(1), "role": role, "verdict": "accepted",
                "headSha": live_head, "fingerprint": fingerprint,
            }
            break
    if parsed is None:
        return None
    url = str(comment.get("html_url", ""))
    expected_url = rf"https://github\.com/FS-GG/\.github/pull/{REVIEW_PULL_REQUEST}#issuecomment-[1-9][0-9]*"
    if not re.fullmatch(expected_url, url):
        return None
    parsed["evidenceRef"] = url
    return parsed


def discover_live_reviews(fingerprint: str) -> tuple[list[dict[str, str]], list[str]]:
    try:
        pull = json.loads(run_bytes(["gh", "api", f"repos/{REVIEW_REPOSITORY}/pulls/{REVIEW_PULL_REQUEST}"]))
        live_head = str(pull["head"]["sha"])
        pages = json.loads(run_bytes([
            "gh", "api", "--paginate", "--slurp",
            f"repos/{REVIEW_REPOSITORY}/issues/{REVIEW_PULL_REQUEST}/comments?per_page=100",
        ]))
    except (subprocess.CalledProcessError, json.JSONDecodeError, KeyError, TypeError) as error:
        return [], [f"reviews: complete live PR/comment ledger is unreadable: {error}"]
    comments = [comment for page in pages for comment in page]
    parsed = [review for comment in comments if (review := parse_role_comment(comment, live_head, fingerprint)) is not None]
    by_role: dict[str, list[dict[str, str]]] = {
        role: [review for review in parsed if review["role"] == role] for role in REQUIRED_REVIEW_ROLES
    }
    errors = [f"reviews[{role}]: expected exactly one unedited accepted current-head attestation, found {len(rows)}" for role, rows in by_role.items() if len(rows) != 1]
    winners = [rows[0] for rows in by_role.values() if len(rows) == 1]
    if len({row["reviewer"] for row in winners}) != len(winners):
        errors.append("reviews: role attestations do not use distinct independent reviewer identities")
    if len({row["evidenceRef"] for row in winners}) != len(winners):
        errors.append("reviews: role attestations do not use distinct live comments")
    return winners, errors


def git_paths(commit: str, root: str) -> list[str]:
    return run_bytes(["git", "ls-tree", "-r", "--name-only", commit, "--", root]).decode().splitlines()


def review_subject(data: dict[str, Any]) -> dict[str, Any]:
    """The exact non-circular Q0 material every role signs."""
    return {
        key: data[key]
        for key in (
            "sourceBase", "inventories", "authorities", "mutations", "corpus",
            "compatibilityDeletion", "handoff", "runtimeDecision", "liveProjection", "threatModel",
            "adminSettingsReport", "governingArtifacts", "corpusArtifact",
            "reviewAuthority",
        )
    }


def ids_unique(rows: list[dict[str, Any]], label: str, errors: list[str]) -> None:
    ids = [row.get("id") for row in rows]
    if None in ids or len(ids) != len(set(ids)):
        errors.append(f"{label}: missing or duplicate id")


def require_fields(rows: list[dict[str, Any]], fields: set[str], label: str, errors: list[str]) -> None:
    for index, row in enumerate(rows):
        missing = sorted(field for field in fields if not row.get(field))
        if missing:
            errors.append(f"{label}[{index}]: missing {','.join(missing)}")


def validate(data: dict[str, Any], acceptance: bool = False) -> list[str]:
    errors: list[str] = []
    if data.get("schema") != "fsgg.github-substrate.q0-evidence/v1":
        errors.append("schema: unsupported")

    source_base = data.get("sourceBase", "")
    if not GIT_SHA.fullmatch(source_base):
        errors.append("sourceBase: expected exact lowercase 40-hex git object")
    else:
        try:
            run_bytes(["git", "cat-file", "-e", f"{source_base}^{{commit}}"])
        except subprocess.CalledProcessError:
            errors.append("sourceBase: commit is unavailable")

    governing = data.get("governingArtifacts", [])
    require_fields(governing, {"path", "sha256"}, "governingArtifacts", errors)
    governing_by_path = {row.get("path"): row for row in governing}
    required_governing = {
        "docs/adr/0078-github-substrate-v2-new-only-coordination-authority.md",
        "docs/coordination/2026-08-25-github-substrate-v2-fleet-cutover-design.md",
        "docs/github-substrate-v2-roadmap.md",
    }
    if set(governing_by_path) != required_governing or len(governing) != len(required_governing):
        errors.append("governingArtifacts: exact ADR/design/roadmap set is required")
    for path, row in governing_by_path.items():
        artifact_path = ROOT / str(path)
        if not artifact_path.is_file():
            errors.append(f"governingArtifacts[{path}]: source file missing")
        elif row.get("sha256") != digest_bytes(artifact_path.read_bytes()):
            errors.append(f"governingArtifacts[{path}]: digest does not match source bytes")
        if not SHA256.fullmatch(str(row.get("sha256", ""))):
            errors.append(f"governingArtifacts[{path}]: invalid SHA-256")

    inventories = data.get("inventories", [])
    ids_unique(inventories, "inventories", errors)
    require_fields(inventories, {"id", "root", "count", "pathListSha256"}, "inventories", errors)
    by_id = {row.get("id"): row for row in inventories}
    expected_inventory_ids = set(TREE_INVENTORIES) | set(COMMAND_INVENTORIES)
    if set(by_id) != expected_inventory_ids:
        errors.append(f"inventories: id mismatch missing={sorted(expected_inventory_ids-set(by_id))} extra={sorted(set(by_id)-expected_inventory_ids)}")
    if GIT_SHA.fullmatch(source_base):
        for inventory_id, root in TREE_INVENTORIES.items():
            if inventory_id not in by_id:
                continue
            paths = git_paths(source_base, root)
            expected_digest = digest_bytes(("\n".join(paths) + "\n").encode())
            row = by_id[inventory_id]
            if row.get("root") != root or row.get("count") != len(paths) or row.get("pathListSha256") != expected_digest:
                errors.append(f"inventories[{inventory_id}]: does not match independently enumerated sourceBase tree")
    for inventory_id, command in COMMAND_INVENTORIES.items():
        if inventory_id not in by_id:
            continue
        try:
            output = run_bytes(command)
            parsed = json.loads(output)
            count = 1 if inventory_id == "protocolFacts" else len(parsed.get("commands", []))
            row = by_id[inventory_id]
            if row.get("root") != " ".join(command) or row.get("count") != count or row.get("pathListSha256") != digest_bytes(output):
                errors.append(f"inventories[{inventory_id}]: does not match independently executed command output")
        except (subprocess.CalledProcessError, json.JSONDecodeError) as error:
            errors.append(f"inventories[{inventory_id}]: derivation failed: {error}")
    for row in inventories:
        if not SHA256.fullmatch(str(row.get("pathListSha256", ""))):
            errors.append(f"inventories[{row.get('id')}]: invalid SHA-256")

    authorities = data.get("authorities", [])
    ids_unique(authorities, "authorities", errors)
    require_fields(authorities, {"id", "category", "subject", "authority", "revision", "completeness", "owner", "disposition", "unit"}, "authorities", errors)
    present_authority = {row.get("category") for row in authorities}
    if present_authority != REQUIRED_AUTHORITY:
        errors.append(f"authorities: category mismatch missing={sorted(REQUIRED_AUTHORITY-present_authority)} extra={sorted(present_authority-REQUIRED_AUTHORITY)}")
    invalid_dispositions = {row.get("disposition") for row in authorities} - REQUIRED_CLASSIFICATIONS
    if invalid_dispositions:
        errors.append(f"authorities: unsupported dispositions {sorted(invalid_dispositions)}")

    mutations = data.get("mutations", [])
    ids_unique(mutations, "mutations", errors)
    require_fields(mutations, {"id", "class", "routes", "endpoint", "precondition", "permission", "v2", "unit"}, "mutations", errors)
    mutation_classes = {row.get("class") for row in mutations}
    if mutation_classes != REQUIRED_MUTATION:
        errors.append(f"mutations: class mismatch missing={sorted(REQUIRED_MUTATION-mutation_classes)} extra={sorted(mutation_classes-REQUIRED_MUTATION)}")
    if any(not isinstance(row.get("routes"), list) or not row["routes"] for row in mutations):
        errors.append("mutations: empty or invalid route coverage")

    corpus = data.get("corpus", [])
    ids_unique(corpus, "corpus", errors)
    require_fields(corpus, {"id", "kind", "source", "expected", "artifact", "originalBytesSha256"}, "corpus", errors)
    corpus_kinds = {row.get("kind") for row in corpus}
    if corpus_kinds != REQUIRED_CORPUS:
        errors.append(f"corpus: kind mismatch missing={sorted(REQUIRED_CORPUS-corpus_kinds)} extra={sorted(corpus_kinds-REQUIRED_CORPUS)}")
    corpus_contract = [{key: row.get(key) for key in ("id", "kind", "expected")} for row in corpus]
    if canonical_digest(corpus_contract) != REQUIRED_CORPUS_CONTRACT_SHA256:
        errors.append("corpus: typed verdict contract differs from the adjudicated baseline")
    corpus_artifact = data.get("corpusArtifact", {})
    require_fields([corpus_artifact], {"path", "sha256"}, "corpusArtifact", errors)
    corpus_path = ROOT / str(corpus_artifact.get("path", ""))
    originals: dict[str, Any] = {}
    if not corpus_path.is_file():
        errors.append("corpusArtifact: source file missing")
    else:
        raw_corpus = corpus_path.read_bytes()
        if corpus_artifact.get("sha256") != digest_bytes(raw_corpus):
            errors.append("corpusArtifact: digest does not match source bytes")
        try:
            decoded_corpus = json.loads(raw_corpus)
            if decoded_corpus.get("schema") != "fsgg.github-substrate.q0-corpus-originals/v2" or decoded_corpus.get("sourceCommit") != source_base:
                errors.append("corpusArtifact: unsupported schema or source commit")
            entries = decoded_corpus.get("entries", [])
            originals = {entry.get("id"): entry for entry in entries}
            if len(originals) != len(entries) or set(originals) != {row.get("id") for row in corpus}:
                errors.append("corpusArtifact: original-byte id census does not match typed corpus")
        except (json.JSONDecodeError, AttributeError) as error:
            errors.append(f"corpusArtifact: unreadable JSON: {error}")
    if not SHA256.fullmatch(str(corpus_artifact.get("sha256", ""))):
        errors.append("corpusArtifact: invalid SHA-256")
    for row in corpus:
        original = originals.get(row.get("id"))
        if not isinstance(original, dict):
            continue
        path = str(original.get("path", ""))
        source_ref = f"git:{source_base}:{path}"
        try:
            original_bytes = run_bytes(["git", "show", f"{source_base}:{path}"])
            blob_sha = run_bytes(["git", "rev-parse", f"{source_base}:{path}"]).decode().strip()
        except subprocess.CalledProcessError:
            errors.append(f"corpus[{row.get('id')}]: immutable git provenance is unreadable")
            continue
        original_digest = digest_bytes(original_bytes)
        if not original.get("mediaType") or original.get("sourceRef") != source_ref or row.get("source") != source_ref:
            errors.append(f"corpus[{row.get('id')}]: provenance metadata does not match typed row")
        if original.get("gitBlobSha1") != blob_sha or original.get("byteLength") != len(original_bytes):
            errors.append(f"corpus[{row.get('id')}]: git blob identity or byte length mismatch")
        if original.get("sha256") != original_digest or row.get("originalBytesSha256") != original_digest:
            errors.append(f"corpus[{row.get('id')}]: original bytes are not digest-bound")
        if row.get("artifact") != f"q0-corpus-originals.json#{row.get('id')}":
            errors.append(f"corpus[{row.get('id')}]: artifact locator mismatch")
        if not SHA256.fullmatch(str(row.get("originalBytesSha256", ""))):
            errors.append(f"corpus[{row.get('id')}]: invalid SHA-256")
        expected = row.get("expected")
        allowed_decisions = {"Accept", "Refuse", "Converge", "Preserve", "Indeterminate"}
        allowed_authorities = {"protocol", "adapter", "release", "corpus"}
        expected_fields = {"decisionClass", "predicateId", "authority", "detail"}
        allowed_fields = expected_fields | ({"metrics"} if row.get("id") == "C-churn" else set())
        if not isinstance(expected, dict) or set(expected) != allowed_fields or expected.get("decisionClass") not in allowed_decisions or expected.get("authority") not in allowed_authorities or not expected.get("predicateId") or not expected.get("detail"):
            errors.append(f"corpus[{row.get('id')}]: expected result is not a complete typed verdict")
        if row.get("id") == "C-churn":
            required_metrics = {"windowHours": 72, "opened": 54, "closed": 32, "net": 22, "commits": 156}
            required_phrases = [b"starting 72-hour board window", b"54 issues opened", b"32 issues closed", b"net row growth of 22", b"156 repository commits"]
            if expected.get("metrics") != required_metrics or any(phrase not in original_bytes for phrase in required_phrases):
                errors.append("corpus[C-churn]: immutable source does not prove the typed 72-hour baseline")

    deletion = data.get("compatibilityDeletion", [])
    ids_unique(deletion, "compatibilityDeletion", errors)
    require_fields(deletion, {"id", "surface", "classification", "deleteUnit", "absenceTest"}, "compatibilityDeletion", errors)
    classifications = {row.get("classification") for row in deletion}
    if classifications != REQUIRED_CLASSIFICATIONS:
        errors.append(f"compatibilityDeletion: classification mismatch missing={sorted(REQUIRED_CLASSIFICATIONS-classifications)} extra={sorted(classifications-REQUIRED_CLASSIFICATIONS)}")
    deletion_contract = [
        {key: row.get(key) for key in ("id", "surface", "classification", "deleteUnit", "absenceTest")}
        for row in deletion
    ]
    if canonical_digest(deletion_contract) != REQUIRED_DELETION_CONTRACT_SHA256:
        errors.append("compatibilityDeletion: contract differs from the adjudicated deletion baseline")

    handoff = data.get("handoff", [])
    require_fields(handoff, {"ref", "classification", "decision"}, "handoff", errors)
    if len({row.get("ref") for row in handoff}) != len(handoff):
        errors.append("handoff: duplicate ref")
    handoff_classes = {row.get("classification") for row in handoff}
    if not handoff_classes <= REQUIRED_HANDOFF:
        errors.append(f"handoff: unsupported classifications {sorted(handoff_classes-REQUIRED_HANDOFF)}")
    if not {"v2-blocker", "parallel-product", "candidate-input-change", "superseded-inventory", "cutover-deferred"} <= handoff_classes:
        errors.append("handoff: required disposition class absent")

    runtime = data.get("runtimeDecision", {})
    require_fields([runtime], {"posture", "hostedBoundary", "owner", "availability", "secrets", "ingress", "observability", "upgrades", "incidentResponse", "retention", "cost", "disasterRecovery"}, "runtimeDecision", errors)
    if runtime.get("posture") != "scheduled-audit-authoritative" or runtime.get("hostedBoundary") != "rejected-for-cutover":
        errors.append("runtimeDecision: unratified cutover posture")

    threat = data.get("threatModel", {})
    require_fields([threat], {"path", "sha256", "boundaries"}, "threatModel", errors)
    threat_path = ROOT / str(threat.get("path", ""))
    if not threat_path.is_file():
        errors.append("threatModel: source file missing")
    elif threat.get("sha256") != digest_bytes(threat_path.read_bytes()):
        errors.append("threatModel: digest does not match source bytes")
    if not SHA256.fullmatch(str(threat.get("sha256", ""))):
        errors.append("threatModel: invalid SHA-256")
    if set(threat.get("boundaries", [])) != REQUIRED_THREAT_BOUNDARIES:
        errors.append("threatModel: protected-boundary census mismatch")

    admin_report = data.get("adminSettingsReport", {})
    require_fields([admin_report], {"path", "sha256"}, "adminSettingsReport", errors)
    admin_report_path = ROOT / str(admin_report.get("path", ""))
    if not admin_report_path.is_file():
        errors.append("adminSettingsReport: source file missing")
    elif admin_report.get("sha256") != digest_bytes(admin_report_path.read_bytes()):
        errors.append("adminSettingsReport: digest does not match source bytes")
    if not SHA256.fullmatch(str(admin_report.get("sha256", ""))):
        errors.append("adminSettingsReport: invalid SHA-256")

    projection = data.get("liveProjection", {})
    if set(projection.get("blockedBy", {})) != {"FS-GG/.github#2963", "FS-GG/.github#2964", "FS-GG/.github#2965"}:
        errors.append("liveProjection: program dependency census incomplete")
    expected_children = {f"FS-GG/.github#{number}" for number in range(2954, 2966)}
    if set(projection.get("childBoundaryCitations", [])) != expected_children:
        errors.append("liveProjection: ADR/rollback child citation census incomplete")
    bootstrap = projection.get("bootstrapRepository", {})
    if bootstrap.get("ref") != "FS-GG/FS.GG.Coordination" or bootstrap.get("visibility") != "PUBLIC" or bootstrap.get("defaultBranch") != "main" or bootstrap.get("state") != "inert" or not GIT_SHA.fullmatch(str(bootstrap.get("initialHead", ""))):
        errors.append("liveProjection: early bootstrap repository receipt is incomplete or active")
    if projection.get("pendingBoardWrites") != 0:
        errors.append("liveProjection: pending board writes")

    try:
        expected_review_fingerprint = canonical_digest(review_subject(data))
    except KeyError as error:
        errors.append(f"reviewFingerprint: missing subject {error.args[0]}")
        expected_review_fingerprint = ""
    if not SHA256.fullmatch(str(data.get("reviewFingerprint", ""))) or data.get("reviewFingerprint") != expected_review_fingerprint:
        errors.append("reviewFingerprint: does not bind the canonical Q0 subject")

    required_reviews = set(data.get("reviewsRequired", []))
    if data.get("reviewAuthority") != "live-unedited-current-head-pr-comments":
        errors.append("reviewAuthority: live current-head PR ledger is mandatory")
    if required_reviews != REQUIRED_REVIEW_ROLES or len(data.get("reviewsRequired", [])) != len(REQUIRED_REVIEW_ROLES):
        errors.append("reviewsRequired: exact independent role policy is mandatory")

    if acceptance:
        reviews = data.get("reviews", [])
        if reviews:
            errors.append("reviews: checked-in review claims are not authority; leave rows empty and resolve the live unedited PR ledger")
        if required_reviews == REQUIRED_REVIEW_ROLES and not reviews and expected_review_fingerprint:
            _, live_errors = discover_live_reviews(expected_review_fingerprint)
            errors.extend(live_errors)
    return errors


def self_test(data: dict[str, Any]) -> list[str]:
    failures: list[str] = []
    candidate_mutations = [
        ("inventory digest", lambda d: d["inventories"][0].__setitem__("pathListSha256", "not-a-digest")),
        ("governing artifact", lambda d: d["governingArtifacts"][0].__setitem__("sha256", "0" * 64)),
        ("unknown writer", lambda d: d["mutations"].append({**d["mutations"][0], "id": "M-unknown", "class": "unknownWriter"})),
        ("corpus subject", lambda d: d["corpus"][0]["expected"].__setitem__("decisionClass", "Accept")),
        ("corpus original bytes", lambda d: d["corpus"][0].__setitem__("originalBytesSha256", "0" * 64)),
        ("deletion unit", lambda d: d["compatibilityDeletion"][0].__setitem__("deleteUnit", "GS2-never")),
        ("threat source", lambda d: d["threatModel"].__setitem__("sha256", "0" * 64)),
        ("administrator report", lambda d: d["adminSettingsReport"].__setitem__("sha256", "0" * 64)),
        ("pending projection", lambda d: d["liveProjection"].__setitem__("pendingBoardWrites", 1)),
    ]
    for name, mutate in candidate_mutations:
        candidate = copy.deepcopy(data)
        mutate(candidate)
        if name in {"corpus subject", "deletion unit"}:
            candidate["reviewFingerprint"] = canonical_digest(review_subject(candidate))
        if not validate(candidate):
            failures.append(f"mutation survived: {name}")

    acceptance = copy.deepcopy(data)
    acceptance["reviews"] = [
        {"role": role, "verdict": "accepted", "fingerprint": "x", "evidenceRef": "https://example.invalid/review", "reviewer": role}
        for role in acceptance["reviewsRequired"]
    ]
    if not validate(acceptance, acceptance=True):
        failures.append("mutation survived: arbitrary reviewer fingerprint")
    erased = copy.deepcopy(data)
    erased["reviewsRequired"] = []
    erased["reviews"] = []
    if not validate(erased, acceptance=True):
        failures.append("mutation survived: erased review policy")
    forged = copy.deepcopy(data)
    forged["reviews"] = [
        {"role": role, "verdict": "accepted", "fingerprint": data["reviewFingerprint"],
         "evidenceRef": "https://example.invalid/review", "reviewer": "same-reviewer"}
        for role in forged["reviewsRequired"]
    ]
    if not validate(forged, acceptance=True):
        failures.append("mutation survived: forged or non-independent role reviews")
    nonexistent = copy.deepcopy(data)
    nonexistent["reviews"] = [
        {"role": role, "verdict": "accepted", "fingerprint": data["reviewFingerprint"],
         "evidenceRef": f"https://github.com/FS-GG/.github/pull/{REVIEW_PULL_REQUEST}#issuecomment-{index}",
         "reviewer": f"invented-{index}", "headSha": "0" * 40}
        for index, role in enumerate(nonexistent["reviewsRequired"], start=1)
    ]
    if not validate(nonexistent, acceptance=True):
        failures.append("mutation survived: nonexistent canonical-looking role comments")
    parser_head = "1" * 40
    parser_fingerprint = "2" * 64
    parser_body = (
        "## Q0 security role review\n\n"
        "- Reviewer identity: `parser-reviewer`\n"
        "- Role: `security`\n"
        "- Verdict: **accepted**\n"
        f"- Exact PR head: `{parser_head}`\n"
        f"- Canonical Q0 fingerprint: `{parser_fingerprint}`\n"
    )
    parser_comment = {
        "body": parser_body,
        "html_url": f"https://github.com/FS-GG/.github/pull/{REVIEW_PULL_REQUEST}#issuecomment-1",
        "created_at": "2026-08-26T00:00:00Z",
        "updated_at": "2026-08-26T00:00:00Z",
        "user": {"login": "EHotwagner", "type": "User"},
        "author_association": "MEMBER",
    }
    if parse_role_comment(parser_comment, parser_head, parser_fingerprint) is None:
        failures.append("review parser rejected an exact accepted attestation")
    exhausted_pr = {
        **parser_comment,
        "html_url": "https://github.com/FS-GG/.github/pull/3001#issuecomment-1",
    }
    if parse_role_comment(exhausted_pr, parser_head, parser_fingerprint) is not None:
        failures.append("review parser accepted an attestation from the exhausted PR")
    outsider = {
        **parser_comment,
        "user": {"login": "public-outsider", "type": "User"},
        "author_association": "NONE",
    }
    if parse_role_comment(outsider, parser_head, parser_fingerprint) is not None:
        failures.append("review parser accepted an outsider-authored attestation")
    retracted = {**parser_comment, "body": parser_body + "This is not an attestation.\n"}
    if parse_role_comment(retracted, parser_head, parser_fingerprint) is not None:
        failures.append("review parser accepted a trailing retraction")
    negated = {**parser_comment, "body": parser_body.replace("**accepted**", "**not accepted**")}
    if parse_role_comment(negated, parser_head, parser_fingerprint) is not None:
        failures.append("review parser accepted a negated verdict")
    if parse_role_comment(parser_comment, "0" * 40, parser_fingerprint) is not None:
        failures.append("review parser accepted an arbitrary non-current head")
    edited = {**parser_comment, "updated_at": "2026-08-26T00:01:00Z"}
    if parse_role_comment(edited, parser_head, parser_fingerprint) is not None:
        failures.append("review parser accepted an edited attestation")
    fenced = {**parser_comment, "body": "```markdown\n" + parser_body + "```\n"}
    if parse_role_comment(fenced, parser_head, parser_fingerprint) is not None:
        failures.append("review parser accepted a fenced historical example")
    disclaimed = {**parser_comment, "body": "This is not an attestation.\n\n" + parser_body}
    if parse_role_comment(disclaimed, parser_head, parser_fingerprint) is not None:
        failures.append("review parser accepted a disclaimed copied attestation")
    return failures


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("evidence", type=Path)
    parser.add_argument("--acceptance", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    data = json.loads(args.evidence.read_text(encoding="utf-8"))
    errors = validate(data, args.acceptance)
    if args.self_test:
        errors.extend(self_test(data))
    if errors:
        for error in errors:
            print(f"Q0-RED: {error}")
        return 1
    mode = "acceptance" if args.acceptance else "candidate"
    print(f"Q0-GREEN: {mode}; authorities={len(data['authorities'])}; mutations={len(data['mutations'])}; corpus={len(data['corpus'])}; deletions={len(data['compatibilityDeletion'])}; controls={'on' if args.self_test else 'off'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
