#!/usr/bin/env python3
"""Validate the owner-authorized M6 new-only cutover evidence, fail closed."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

SHA = re.compile(r"^[0-9a-f]{40}$")
HASH = re.compile(r"^[0-9a-f]{64}$")
REQUIRED_TESTS = {
    "core", "cli", "github", "engine-e2e", "engine-writes", "engine-parity",
    "engine-replay", "graphql-boundary", "structured-decisions", "release-saga",
    "evidence-retention", "shellcheck", "policy-projections", "wiring-non-vacuity",
    "live-new-only-smoke",
}
REQUIRED_MUTATIONS = {
    "lifecycle-old-reducer", "graphql-raw-envelope", "route-v1-authority",
    "review-prose-authority", "release-local-pack", "evidence-manifest-byte-drift",
    "acceptance-missing-binding",
}


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def relative_file(root: Path, value: object, where: str, failures: list[str]) -> Path | None:
    if not isinstance(value, str) or not value:
        failures.append(f"{where}: require a repository-relative path")
        return None
    candidate = Path(value)
    if candidate.is_absolute() or ".." in candidate.parts:
        failures.append(f"{where}: path escapes repository")
        return None
    path = root / candidate
    try:
        resolved_root = root.resolve(strict=True)
        resolved = path.resolve(strict=True)
    except FileNotFoundError:
        failures.append(f"{where}: file does not exist: {value}")
        return None
    try:
        resolved.relative_to(resolved_root)
    except ValueError:
        failures.append(f"{where}: resolved path escapes repository: {value}")
        return None
    if resolved != path.absolute():
        failures.append(f"{where}: symbolic links are not accepted: {value}")
        return None
    if not resolved.is_file():
        failures.append(f"{where}: file does not exist: {value}")
        return None
    return resolved


def bound_json(root: Path, binding: object, where: str, failures: list[str]) -> dict | None:
    if not isinstance(binding, dict):
        failures.append(f"{where}: require path and sha256 binding")
        return None
    path = relative_file(root, binding.get("path"), f"{where}.path", failures)
    expected = binding.get("sha256")
    if not isinstance(expected, str) or not HASH.fullmatch(expected):
        failures.append(f"{where}.sha256: require exact lowercase SHA-256")
        return None
    if path is None:
        return None
    if digest(path) != expected:
        failures.append(f"{where}: SHA-256 mismatch")
        return None
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        failures.append(f"{where}: invalid JSON: {error}")
        return None
    if not isinstance(value, dict):
        failures.append(f"{where}: artifact root must be an object")
        return None
    return value


def commit_exists(root: Path, sha: object) -> bool:
    return isinstance(sha, str) and SHA.fullmatch(sha) is not None and subprocess.run(
        ["git", "cat-file", "-e", f"{sha}^{{commit}}"], cwd=root,
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
    ).returncode == 0


def is_ancestor(root: Path, older: object, newer: object) -> bool:
    return isinstance(older, str) and isinstance(newer, str) and subprocess.run(
        ["git", "merge-base", "--is-ancestor", str(older), str(newer)], cwd=root,
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
    ).returncode == 0


def validate(document: object, root: Path) -> list[str]:
    failures: list[str] = []
    if not isinstance(document, dict):
        return ["root must be an object"]
    if document.get("schema_version") != 1:
        failures.append("schema_version must be 1")
    if document.get("status") != "terminal-new-only-release-live-acceptance-complete":
        failures.append("status must declare terminal new-only release/live acceptance complete")

    decision = document.get("owner_decision")
    if not isinstance(decision, dict) or decision.get("supersedes_calendar_gate") is not True:
        failures.append("owner_decision must explicitly supersede the calendar-only gate")
    elif not decision.get("authorized_at") or not decision.get("reason"):
        failures.append("owner_decision must bind authorization time and reason")

    implementation = document.get("implementation")
    implementation_sha = implementation.get("sha") if isinstance(implementation, dict) else None
    if not isinstance(implementation_sha, str) or not SHA.fullmatch(implementation_sha):
        failures.append("implementation.sha must be exact lowercase 40-hex")
    else:
        exists = commit_exists(root, implementation_sha)
        if not exists:
            failures.append("implementation.sha is not a commit in this repository")
        elif not is_ancestor(root, implementation_sha, "HEAD"):
            failures.append("implementation.sha is not an ancestor of the validated checkout")

    history = document.get("superseded_history")
    old = bound_json(root, history, "superseded_history", failures)
    if isinstance(history, dict) and history.get("disposition") != "superseded-not-passed":
        failures.append("superseded_history.disposition must preserve the old gate as not passed")
    if old is not None and old.get("candidate_periods") is None:
        failures.append("superseded_history does not contain the historical calendar evidence")

    inventory = document.get("deletion_inventory")
    if not isinstance(inventory, dict):
        failures.append("deletion_inventory must be an object")
    else:
        paths = inventory.get("absent_paths")
        if not isinstance(paths, list) or not paths:
            failures.append("deletion_inventory.absent_paths must be non-empty")
        else:
            for value in paths:
                if not isinstance(value, str) or Path(value).is_absolute() or ".." in Path(value).parts:
                    failures.append(f"deletion_inventory invalid path: {value!r}")
                elif (root / value).exists():
                    failures.append(f"retired path still exists: {value}")
        scans = inventory.get("absent_markers")
        if not isinstance(scans, list) or not scans:
            failures.append("deletion_inventory.absent_markers must be non-empty")
        else:
            for index, scan in enumerate(scans):
                if not isinstance(scan, dict) or not scan.get("marker") or not isinstance(scan.get("roots"), list):
                    failures.append(f"deletion_inventory.absent_markers[{index}] is invalid")
                    continue
                marker = scan["marker"]
                for root_name in scan["roots"]:
                    base = root / root_name
                    if not base.exists():
                        failures.append(f"deletion scan root does not exist: {root_name}")
                        continue
                    candidates = [base] if base.is_file() else base.rglob("*")
                    for path in candidates:
                        if path.is_file() and ".git" not in path.parts:
                            try:
                                if marker in path.read_text(encoding="utf-8"):
                                    failures.append(f"retired marker {marker!r} remains in {path.relative_to(root)}")
                            except UnicodeDecodeError:
                                pass

    census = bound_json(root, document.get("decision_census"), "decision_census", failures)
    if census is not None:
        required = {
            "pagination_complete": True, "board_rows": 108,
            "active_route_legacy_only": 0, "active_review_legacy_only": 0,
            "inert_route_legacy_only": 76, "preserve_inert_comments": True,
        }
        for field, expected in required.items():
            if census.get(field) != expected:
                failures.append(f"decision_census.{field}: expected {expected!r}, got {census.get(field)!r}")
        if census.get("implementation_sha") != implementation_sha:
            failures.append("decision_census does not bind implementation.sha")

    seed = bound_json(root, document.get("lifecycle_seed"), "lifecycle_seed", failures)
    if seed is not None:
        if seed.get("implementation_sha") != implementation_sha or not seed.get("collected_at"):
            failures.append("lifecycle_seed does not bind implementation.sha and collection time")
        if seed.get("policy_version") != "intent-status/v1":
            failures.append("lifecycle_seed policy is not intent-status/v1")
        result = seed.get("result", {})
        if result.get("posted") != 24 or result.get("failed") != 0 or result.get("second_pass_would_post") != 0 or result.get("second_pass_exact_existing") != 24:
            failures.append("lifecycle_seed does not prove the 24-row exact/idempotent seed")
        if "never derives intent from mutable Project Status" not in str(seed.get("decision", "")):
            failures.append("lifecycle_seed does not state the no-status-derived-intent rule")
        replay_binding = seed.get("authenticated_replay")
        replay = bound_json(root, replay_binding, "lifecycle_seed.authenticated_replay", failures)
        if replay is not None:
            if replay.get("implementation_sha") != implementation_sha:
                failures.append("lifecycle seed replay does not bind implementation.sha")
            rows = replay.get("comments")
            board = replay.get("board", {})
            second = replay.get("second_pass", {})
            if not isinstance(rows, list) or len(rows) != 24:
                failures.append("lifecycle seed replay does not bind 24 comment observations")
            if board.get("pagination_complete") is not True or board.get("rows") != 108 or board.get("unique_refs") != 108:
                failures.append("lifecycle seed replay does not bind the complete board population")
            if second.get("would_post") != 0 or second.get("exact_existing") != 24 or second.get("conflicts") != 0:
                failures.append("lifecycle seed replay does not bind an idempotent second pass")
            if isinstance(replay_binding, dict):
                seed_path = str(document.get("lifecycle_seed", {}).get("path", ""))
                replay_path = str(replay_binding.get("path", ""))
                check = subprocess.run(
                    [sys.executable, "scripts/verify-m6-live-intent-seed.py", seed_path, replay_path,
                     "--implementation-sha", str(implementation_sha)],
                    cwd=root, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                )
                if check.returncode != 0:
                    failures.append("lifecycle seed replay fails offline structural verification")

    archive = bound_json(root, document.get("trx_archive"), "trx_archive", failures)
    if archive is not None:
        release = archive.get("release", {})
        payload = archive.get("archive", {})
        if release.get("immutable") is not True or release.get("immutable_releases_setting") != "enabled":
            failures.append("TRX archive release is not bound as immutable")
        if release.get("server_digest") != "sha256:" + str(payload.get("sha256")):
            failures.append("TRX archive server digest does not bind archive bytes")
        if archive.get("file_count") != 29 or archive.get("source_bytes") != 27532297:
            failures.append("TRX archive inventory is not the exact 29 files / 27,532,297 bytes")
    tracked_trx = subprocess.run(
        ["git", "ls-files", "*.trx"], cwd=root, check=True, text=True, stdout=subprocess.PIPE,
    ).stdout.splitlines()
    if tracked_trx:
        failures.append(f"tracked historical TRX remains: {tracked_trx[0]}")

    results = document.get("test_results")
    seen_tests: set[str] = set()
    if not isinstance(results, list):
        failures.append("test_results must be an array")
    else:
        for index, row in enumerate(results):
            if not isinstance(row, dict):
                failures.append(f"test_results[{index}] must be an object")
                continue
            name = row.get("family")
            if isinstance(name, str):
                seen_tests.add(name)
            if row.get("outcome") != "pass" or row.get("implementation_sha") != implementation_sha:
                failures.append(f"test_results[{index}] is not a passing exact-implementation result")
            if row.get("tree_sha") != implementation_sha or not isinstance(row.get("command"), list) or not row.get("command"):
                failures.append(f"test_results[{index}] must bind exact tree and command argv")
            if row.get("expected_exit") != 0 or row.get("observed_exit") != 0:
                failures.append(f"test_results[{index}] must bind expected/observed exit 0")
            if not isinstance(row.get("counts"), dict) or not row.get("counts") or not HASH.fullmatch(str(row.get("stdout_sha256", ""))):
                failures.append(f"test_results[{index}] must bind result counts and stdout SHA-256")
            bound_json(root, row.get("artifact"), f"test_results[{index}].artifact", failures)
    missing_tests = REQUIRED_TESTS - seen_tests
    if missing_tests:
        failures.append("missing required test families: " + ", ".join(sorted(missing_tests)))

    engine_mutations = bound_json(
        root, document.get("engine_mutation_matrix"), "engine_mutation_matrix", failures
    )
    if engine_mutations is not None:
        summary = engine_mutations.get("result", {})
        if engine_mutations.get("implementation_sha") != implementation_sha:
            failures.append("engine_mutation_matrix does not bind implementation.sha")
        if summary.get("legs") != 11 or summary.get("justified") != 11:
            failures.append("engine_mutation_matrix must bind all 11 justified legs")
        if summary.get("decorative") != 0 or summary.get("not_measured") != 0:
            failures.append("engine_mutation_matrix contains an unprotected or unmeasured leg")
        legs = engine_mutations.get("legs")
        if not isinstance(legs, list) or len(legs) != 11:
            failures.append("engine_mutation_matrix must retain exactly 11 leg records")
        elif any(
            row.get("verdict") != "JUSTIFIED"
            or row.get("control_rc") != 0
            or row.get("mutant_rc") == 0
            for row in legs if isinstance(row, dict)
        ):
            failures.append("engine_mutation_matrix contains a non-green control or surviving mutant")

    mutations = document.get("mutation_results")
    seen_mutations: set[str] = set()
    if not isinstance(mutations, list):
        failures.append("mutation_results must be an array")
    else:
        for index, row in enumerate(mutations):
            if not isinstance(row, dict):
                failures.append(f"mutation_results[{index}] must be an object")
                continue
            name = row.get("mutation")
            if isinstance(name, str):
                seen_mutations.add(name)
            if row.get("outcome") != "red" or row.get("implementation_sha") != implementation_sha:
                failures.append(f"mutation_results[{index}] is not a red exact-implementation inversion")
            if row.get("tree_sha") != implementation_sha or not isinstance(row.get("command"), list) or not row.get("command"):
                failures.append(f"mutation_results[{index}] must bind exact tree and command argv")
            observed_exit = row.get("observed_exit")
            if row.get("expected_exit") != "nonzero" or not isinstance(observed_exit, int) or isinstance(observed_exit, bool) or observed_exit == 0:
                failures.append(f"mutation_results[{index}] must bind expected nonzero and observed nonzero")
            if not isinstance(row.get("counts"), dict) or not row.get("counts") or not HASH.fullmatch(str(row.get("stdout_sha256", ""))):
                failures.append(f"mutation_results[{index}] must bind result counts and stdout SHA-256")
            artifact = bound_json(root, row.get("artifact"), f"mutation_results[{index}].artifact", failures)
            if artifact is not None:
                if artifact.get("implementation_sha") != implementation_sha:
                    failures.append(f"mutation_results[{index}] artifact does not bind implementation.sha")
                if not artifact.get("setup") or not artifact.get("restore") or not artifact.get("runner"):
                    failures.append(f"mutation_results[{index}] artifact lacks setup/restore/runner provenance")
                observed = artifact.get("results")
                matches = [
                    result for result in observed
                    if isinstance(observed, list) and isinstance(result, dict)
                    and result.get("mutation") == name
                ] if isinstance(observed, list) else []
                if len(matches) != 1:
                    failures.append(f"mutation_results[{index}] has no unique executable artifact row")
                else:
                    match = matches[0]
                    if (
                        match.get("command") != row.get("command")
                        or match.get("expected_exit") != row.get("expected_exit")
                        or match.get("observed_exit") != row.get("observed_exit")
                        or match.get("stdout_sha256") != row.get("stdout_sha256")
                    ):
                        failures.append(f"mutation_results[{index}] contradicts its executable artifact row")
    missing_mutations = REQUIRED_MUTATIONS - seen_mutations
    if missing_mutations:
        failures.append("missing required mutations: " + ", ".join(sorted(missing_mutations)))

    release = document.get("release")
    if not isinstance(release, dict):
        failures.append("release must be an object")
    else:
        release_sha = release.get("source_sha")
        if release.get("version") != "0.58.0" or not is_ancestor(root, implementation_sha, release_sha):
            failures.append("release must bind coherent set 0.58.0 to a descendant of implementation.sha")
        if release.get("tag") != "coherent-set/v0.58.0" or release.get("immutable") is not True:
            failures.append("release must bind the immutable coherent-set/v0.58.0 release")
        if not HASH.fullmatch(str(release.get("manifest_sha256", ""))) or not HASH.fullmatch(str(release.get("stable_channel_sha256", ""))):
            failures.append("release must bind manifest and stable-channel byte hashes")
        for field in ("prepared_manifest_verified", "package_bytes_identical", "github_feed_observed", "nuget_feed_observed", "promoted", "adopted_and_pinned"):
            if release.get(field) is not True:
                failures.append(f"release.{field} must be true")
        release_evidence = bound_json(root, release.get("evidence"), "release.evidence", failures)
        if release_evidence is not None:
            observed_release = release_evidence.get("release", {})
            for field in (
                "version", "source_sha", "tag", "immutable", "manifest_sha256",
                "stable_channel_sha256", "prepared_manifest_verified", "package_bytes_identical",
                "github_feed_observed", "nuget_feed_observed", "promoted", "adopted_and_pinned",
                "content_id", "feeds", "package_payload_sha256",
            ):
                if release.get(field) != observed_release.get(field):
                    failures.append(f"release.{field} contradicts terminal evidence")
            if not HASH.fullmatch(str(observed_release.get("content_id", "")).removeprefix("sha256:")):
                failures.append("release evidence lacks an exact content ID")
            if observed_release.get("feeds") != ["GitHub Packages", "NuGet.org"]:
                failures.append("release evidence does not bind both feeds")
            payloads = observed_release.get("package_payload_sha256", {})
            if set(payloads) != {"FS.GG.Coord.Cli", "FS.GG.Drivers", "FS.GG.Kit"} or any(
                not HASH.fullmatch(str(value)) for value in payloads.values()
            ):
                failures.append("release evidence does not bind all three package payload hashes")

    live = document.get("live_acceptance")
    if not isinstance(live, dict):
        failures.append("live_acceptance must be an object")
    else:
        live_sha = live.get("verified_main_sha")
        release_sha = release.get("source_sha") if isinstance(release, dict) else None
        if not is_ancestor(root, release_sha, live_sha) or not is_ancestor(root, live_sha, "HEAD"):
            failures.append("live_acceptance must bind a verified main descendant of release source and ancestor of this checkout")
        if live.get("new_only_smoke") is not True or live.get("same_class_open_issues") != 0 or live.get("issue_2569_closed") is not True:
            failures.append("live_acceptance must bind new-only smoke, zero successors, and closed #2569")
        if "commands" in live:
            failures.append("live_acceptance commands must come only from its content-addressed terminal evidence")
        live_evidence = bound_json(root, live.get("evidence"), "live_acceptance.evidence", failures)
        if live_evidence is not None:
            observed_live = live_evidence.get("live", {})
            if observed_live.get("verified_main_sha") != live_sha or observed_live.get("same_class_open_issues") != 0 or observed_live.get("issue_2569", {}).get("state") != "closed":
                failures.append("live_acceptance contradicts terminal evidence")
            commands = observed_live.get("commands")
            required_commands = {
                "release-build", "ready-complete-pagination", "reconcile-idempotent",
                "typed-graphql-project-visibility", "typed-graphql-project-id",
                "typed-graphql-repository-policy", "typed-graphql-meter",
                "typed-graphql-roster", "typed-graphql-archive-scan",
                "immutable-promotion-replay", "public-nuget-clean-install", "installed-cli-help",
            }
            if not isinstance(commands, list) or len(commands) != 12 or len({row.get("name") for row in commands if isinstance(row, dict)}) != 12 or {row.get("name") for row in commands if isinstance(row, dict)} != required_commands or any(
                not isinstance(row, dict) or row.get("observed_exit") != 0 or not HASH.fullmatch(str(row.get("stdout_sha256", "")))
                for row in commands if isinstance(commands, list)
            ):
                failures.append("live evidence does not bind the exact successful new-only command matrix")
            required_counts = {
                "ready-complete-pagination": {"rows": 108},
                "reconcile-idempotent": {"chores": 0},
                "typed-graphql-roster": {"rows": 108},
                "typed-graphql-archive-scan": {"pages": 2, "items": 108},
            }
            observed_counts = {
                row.get("name"): row.get("counts") for row in commands if isinstance(row, dict) and "counts" in row
            } if isinstance(commands, list) else {}
            if observed_counts != required_counts:
                failures.append("live evidence command counts do not prove 108-row complete reads and zero-chore idempotence")
            commands_hash = hashlib.sha256(json.dumps(commands, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
            if live.get("command_matrix_sha256") != commands_hash:
                failures.append("live_acceptance command matrix digest contradicts terminal evidence")
            searches = observed_live.get("same_class_searches")
            search_prefix = ["gh", "search", "issues", "--repo", "FS-GG/.github", "--state", "open", "--match", "title,body", "--limit", "100", "--json", "number,title,url,updatedAt", "--"]
            search_terms = ["graphql_complete_read", "raw GraphQL envelope", "GraphQL compatibility decoder"]
            expected_searches = [{"command": search_prefix + [term], "results": []} for term in search_terms]
            if searches != expected_searches:
                failures.append("live evidence does not bind three empty same-class GitHub searches")
            searches_hash = hashlib.sha256(json.dumps(searches, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
            if live.get("same_class_searches_sha256") != searches_hash:
                failures.append("live_acceptance same-class search digest contradicts terminal evidence")
    return failures


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("evidence")
    parser.add_argument("--root", default=".")
    args = parser.parse_args()
    root = Path(args.root).resolve()
    try:
        document = json.loads(Path(args.evidence).read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        print(f"M6 cutover acceptance: FAIL\n- {error}", file=sys.stderr)
        return 1
    failures = validate(document, root)
    if failures:
        print("M6 cutover acceptance: FAIL", file=sys.stderr)
        for failure in failures:
            print(f"- {failure}", file=sys.stderr)
        return 1
    print("M6 cutover acceptance: PASS — exact new-only implementation, evidence, release, and live state bound")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
