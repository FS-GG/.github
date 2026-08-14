#!/usr/bin/env python3
"""Collect the canonical weekly health evidence for coordination compatibility retirement.

The collector owns the windows and every measurement.  Its output may be consumed by
``coordination-retirement-readiness.py``; callers choose only the repository and output
directory, never a verdict, period, count, or classification.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timedelta, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile


REPOSITORY = "FS-GG/.github"
CANONICAL_OUTPUT = "docs/reports/evidence/coordination-health"
FIRST_PERIOD = datetime(2026, 8, 17, tzinfo=timezone.utc)
PERIODS = 3
BEHAVIOUR_PREFIXES = ("src/", "tests/", "scripts/", ".github/", "policy/")
GENERATED_PREFIXES = ("docs/reports/evidence/", "readiness/", "work/")
IMPLEMENTATION_PREFIXES = ("src/", "tests/", "scripts/", "policy/", ".github/actions/", ".github/workflows/")
HEALTH_WORKFLOW = "coord-board-reconcile.yml"
INCIDENT_QUERIES = {
    "intent_reversals": ['repo:FS-GG/.github is:issue "LIFECYCLE-PROJECTION-LAG"'],
    "partial_success_reads": ["repo:FS-GG/.github is:issue GraphQL pagination",
                              'repo:FS-GG/.github is:issue "partial read"'],
}
SUCCESSOR_QUERIES = [
    'repo:FS-GG/.github is:open is:issue "LIFECYCLE-PROJECTION-LAG"',
    "repo:FS-GG/.github is:open is:issue GraphQL pagination",
    'repo:FS-GG/.github is:open is:issue "partial read"',
    'repo:FS-GG/.github is:open is:issue "feed coherence"',
    'repo:FS-GG/.github is:open is:issue "partial publish"',
    'repo:FS-GG/.github is:open is:issue "body hash"',
    'repo:FS-GG/.github is:open is:issue "delivery-route receipt"',
    'repo:FS-GG/.github is:open is:issue "legacy-only"',
    'repo:FS-GG/.github is:open is:issue "statement" "projection"',
    'repo:FS-GG/.github is:open is:issue "bulky evidence"',
]
# Every non-blocking result is an explicit, reviewed disposition.  A newly returned
# issue is blocking by default; query drift can therefore never silently authorize M6.
NOT_SAME_CLASS = {
    54: "Dependency dashboard; its body only aggregates unrelated query terms.",
    1858: "Claim-lock executor identity defect, outside the M1-M5 compatibility boundaries.",
    2130: "Broader deterministic-protocol epic, not an M6 compatibility implementation.",
    2230: "Routes-plugin dispatch slice, not legacy structured-decision evidence.",
    2249: "Receiver engine-pin drift, not a compatibility reader or writer.",
    2381: "Standing release-debt measurement, not a superseded publication implementation.",
    2551: "Generic gate-inversion discipline, not a compatibility path.",
    2555: "Test-selector vocabulary defect, outside M6 compatibility scope.",
    2556: "Unwired fixtures, outside M6 compatibility scope.",
    2557: "Review predicate mutation coverage, not the v1 review reader.",
    2584: "Root-cause operating-process proposal, not compatibility retirement.",
}
KNOWN_BLOCKERS = {
    2569: "Private raw-GraphQL compatibility shims remain open work.",
}


def run(argv: list[str], root: Path, *, binary: bool = False) -> str | bytes:
    completed = subprocess.run(argv, cwd=root, check=True, capture_output=True,
                               text=not binary)
    return completed.stdout


def gh_json(args: list[str], root: Path) -> object:
    return json.loads(str(run(["gh", "api", *args], root)))


def search(query: str, root: Path) -> list[dict]:
    pages = gh_json(["--method", "GET", "search/issues", "-f", f"q={query}",
                     "-f", "per_page=100", "--paginate", "--slurp"], root)
    if not isinstance(pages, list) or not pages:
        raise ValueError("GitHub Search returned no complete response envelope")
    items = [item for page in pages for item in page.get("items", [])]
    if any(page.get("incomplete_results") is not False for page in pages):
        raise ValueError(f"GitHub Search reported incomplete results for {query!r}")
    totals = {page.get("total_count") for page in pages}
    if len(totals) != 1 or next(iter(totals)) != len({item.get("html_url") for item in items}):
        raise ValueError(f"GitHub Search result count is incomplete or capped for {query!r}")
    return items


def iso(value: datetime) -> str:
    return value.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def observed_at(value: str | None) -> datetime | None:
    if not value:
        return None
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def exact_windows(now: datetime) -> list[tuple[datetime, datetime]]:
    windows = [(FIRST_PERIOD + timedelta(days=7 * i),
                FIRST_PERIOD + timedelta(days=7 * (i + 1))) for i in range(PERIODS)]
    if windows[-1][1] > now:
        raise ValueError(f"three canonical UTC weeks have not elapsed; earliest collection is {iso(windows[-1][1])}")
    return windows


def first_parent_sha(root: Path, source_sha: str, instant: datetime) -> str:
    value = str(run(["git", "rev-list", "--first-parent", "-1", f"--before={iso(instant)}",
                     source_sha], root)).strip()
    if not re.fullmatch(r"[0-9a-f]{40}", value):
        raise ValueError(f"no first-parent commit exists at {iso(instant)}")
    return value


def tree_paths(root: Path, sha: str) -> list[str]:
    return str(run(["git", "ls-tree", "-r", "--name-only", sha], root)).splitlines()


def tree_bytes(root: Path, sha: str, prefixes: tuple[str, ...]) -> int:
    total = 0
    for line in str(run(["git", "ls-tree", "-rl", sha], root)).splitlines():
        metadata, path = line.split("\t", 1)
        if path.startswith(prefixes):
            size = metadata.rsplit(" ", 1)[-1]
            if size.isdigit():
                total += int(size)
    return total


def blob_json(root: Path, sha: str, path: str) -> object:
    return json.loads(str(run(["git", "show", f"{sha}:{path}"], root)))


def inventory(root: Path, sha: str) -> dict[str, object]:
    paths = tree_paths(root, sha)
    policy_inventory = blob_json(root, sha, "policy/coordination-health-inventory.json")
    implementations = policy_inventory.get("implementations") if isinstance(policy_inventory, dict) else None
    if not isinstance(implementations, list) or not implementations:
        raise ValueError(f"{sha}: policy implementation inventory is missing or empty")
    identities: set[tuple[str, str]] = set()
    ids: set[str] = set()
    for row in implementations:
        if not isinstance(row, dict) or not row.get("id") or not row.get("path") or not row.get("marker"):
            raise ValueError(f"{sha}: invalid policy implementation inventory row")
        if row["id"] in ids or (row["path"], row["marker"]) in identities:
            raise ValueError(f"{sha}: duplicate policy implementation identity")
        ids.add(row["id"]); identities.add((row["path"], row["marker"]))
        if row["path"] not in paths or row["marker"] not in str(run(["git", "show", f"{sha}:{row['path']}"], root)):
            raise ValueError(f"{sha}: policy implementation inventory names an absent path")
    discoveries = policy_inventory.get("discoveries")
    if not isinstance(discoveries, list) or not discoveries:
        raise ValueError(f"{sha}: policy implementation discovery rules are missing")
    for discovery in discoveries:
        marker, expected, roots = discovery.get("marker"), discovery.get("paths"), discovery.get("roots")
        if not marker or not isinstance(expected, list) or not isinstance(roots, list) or not roots:
            raise ValueError(f"{sha}: invalid policy discovery rule")
        found = subprocess.run(["git", "grep", "-l", "-F", marker, sha, "--", *roots], cwd=root, text=True, capture_output=True)
        actual = sorted(line.split(":", 1)[-1] for line in found.stdout.splitlines()) if found.returncode in (0, 1) else None
        if actual != sorted(expected):
            raise ValueError(f"{sha}: policy discovery {discovery.get('id')} drifted: {actual} != {sorted(expected)}")
    checks = sorted(path for path in paths if path.startswith("scripts/check-") and not path.endswith(".pyc"))
    workflows = sorted(path for path in paths if path.startswith(".github/workflows/") and path.endswith((".yml", ".yaml")))
    return {
        "policy_implementations": implementations,
        "check_scripts": checks,
        "workflows": workflows,
    }


def commit_measure(root: Path, start: datetime, end: datetime, end_sha: str) -> tuple[int, int, list[dict]]:
    """Measure the schema-v3 critique repair population used by #2587."""
    rows: list[dict] = []
    seen: set[str] = set()
    artifacts = [path for path in tree_paths(root, end_sha)
                 if path.startswith("reviews/roadmap/") and path.endswith(".json")]
    for artifact in artifacts:
        document = blob_json(root, end_sha, artifact)
        if not isinstance(document, dict) or document.get("schema_version") != 3:
            continue
        reviewed = document.get("reviewed_commits")
        rounds = document.get("repair_rounds")
        if not isinstance(reviewed, list) or not isinstance(rounds, int) or len(reviewed) != rounds + 1:
            raise ValueError(f"{artifact}: invalid schema-v3 reviewed commit population")
        for previous, commit in zip(reviewed, reviewed[1:]):
            if commit in seen:
                continue
            commit_doc = gh_json(["--method", "GET", f"repos/{REPOSITORY}/commits/{commit}"], root)
            stamp = observed_at(commit_doc.get("commit", {}).get("committer", {}).get("date"))
            if stamp is None or not start <= stamp < end:
                continue
            comparison = gh_json(["--method", "GET", f"repos/{REPOSITORY}/compare/{previous}...{commit}",
                                  "-f", "per_page=100"], root)
            files = comparison.get("files")
            if (comparison.get("merge_base_commit", {}).get("sha") != previous or not isinstance(files, list)
                    or len(files) >= 300):
                raise ValueError(f"{artifact}: repair comparison {previous}...{commit} is incomplete or non-linear")
            seen.add(commit)
            paths = [row["filename"] for row in files]
            subject = commit_doc.get("commit", {}).get("message", "").splitlines()[0]
            def behavioural(row: dict) -> bool:
                path = row["filename"]
                if not path.startswith(BEHAVIOUR_PREFIXES):
                    return False
                if path.endswith((".fsproj", ".props", ".targets")):
                    patch = row.get("patch", "")
                    changed = [line[1:] for line in patch.splitlines() if line.startswith(("+", "-")) and not line.startswith(("+++", "---"))]
                    tags = [line for line in changed if "<" in line and not line.lstrip().startswith("<!--")]
                    return not ("PackageReleaseNotes" in patch and all("PackageReleaseNotes" in line for line in tags))
                return True
            statement_only = bool(files) and not any(behavioural(row) for row in files)
            rows.append({"sha": commit, "previous_reviewed_sha": previous, "artifact": artifact,
                         "subject": subject, "paths": paths, "files": files,
                         "statement_only": statement_only})
    return len(rows), sum(row["statement_only"] for row in rows), rows


def period_items(query: str, field: str, start: datetime, end: datetime, root: Path) -> list[dict]:
    date_range = f"{start.date()}..{end.date()}"
    rows = search(f"{query} {field.split('_')[0]}:{date_range}", root)
    return [item for item in rows if (stamp := observed_at(item.get(field))) is not None and start <= stamp < end]


def issue_counts(start: datetime, end: datetime, root: Path) -> tuple[list[dict], list[dict]]:
    created = period_items("org:FS-GG type:issue", "created_at", start, end, root)
    closed = period_items("org:FS-GG type:issue", "closed_at", start, end, root)
    return created, closed


def incident_events(kind: str, start: datetime, end: datetime, root: Path) -> list[dict]:
    rows = {item["html_url"]: item for query in INCIDENT_QUERIES[kind]
            for item in period_items(query, "created_at", start, end, root)}
    return [rows[url] for url in sorted(rows)]


def workflow_health(start: datetime, end: datetime, root: Path) -> tuple[list[dict], list[dict]]:
    """Read live reducer/typed-read observations produced by the reconciliation workflow."""
    runs: list[dict] = []
    unexpected: list[dict] = []
    for day in range(7):
        day_start, day_end = start + timedelta(days=day), start + timedelta(days=day + 1)
        pages = gh_json(["--method", "GET", f"repos/{REPOSITORY}/actions/workflows/{HEALTH_WORKFLOW}/runs",
                         "-f", f"created={iso(day_start)}..{iso(day_end)}", "-f", "status=completed",
                         "-f", "per_page=100", "--paginate", "--slurp"], root)
        candidates = [row for page in pages for row in page.get("workflow_runs", [])
                      if (stamp := observed_at(row.get("run_started_at"))) is not None and day_start <= stamp < day_end]
        observed = None
        for candidate in sorted(candidates, key=lambda row: row["id"], reverse=True):
            if (candidate.get("conclusion") != "success" or candidate.get("head_branch") != "main"
                    or candidate.get("head_repository", {}).get("full_name") != REPOSITORY):
                continue
            artifacts = gh_json(["--method", "GET", f"repos/{REPOSITORY}/actions/runs/{candidate['id']}/artifacts"], root)
            matches = [row for row in artifacts.get("artifacts", []) if row.get("name") == f"coordination-health-{candidate['id']}" and not row.get("expired")]
            if not matches:
                continue
            digest = matches[0].get("digest")
            if not isinstance(digest, str) or not re.fullmatch(r"sha256:[0-9a-f]{64}", digest):
                raise ValueError(f"health artifact for run {candidate['id']} has no content digest")
            with tempfile.TemporaryDirectory() as work:
                archive = Path(work) / "health.zip"
                archive.write_bytes(run(["gh", "api", f"repos/{REPOSITORY}/actions/artifacts/{matches[0]['id']}/zip"], root, binary=True))
                if "sha256:" + hashlib.sha256(archive.read_bytes()).hexdigest() != digest:
                    raise ValueError(f"health artifact digest mismatch for run {candidate['id']}")
                import zipfile
                with zipfile.ZipFile(archive) as zipped:
                    shadow = json.loads(zipped.read("lifecycle-shadow.json"))
                    payload = json.loads(zipped.read("health-observation.json"))
            subjects = payload.get("subjects") if isinstance(payload, dict) else None
            if (not isinstance(shadow, list) or payload.get("schemaVersion") != 1
                    or payload.get("applicationMode") != "verified-apply"
                    or payload.get("completeReadBoundary") != "typed-complete-success/1"
                    or not isinstance(subjects, list) or payload.get("subjectCount") != len(subjects)
                    or any(not isinstance(row, dict) or not row.get("subject") or not row.get("intent")
                           or not row.get("intended") or not row.get("applied") or not isinstance(row.get("reversed"), bool)
                           or not isinstance(row.get("readComplete"), bool)
                           for row in subjects)):
                raise ValueError(f"health artifact for run {candidate['id']} is incomplete")
            observed = {"run_id": candidate["id"], "head_sha": candidate["head_sha"],
                        "run_started_at": candidate["run_started_at"], "artifact_id": matches[0]["id"],
                        "artifact_sha256": digest, "shadow": shadow, "health": payload}
            unexpected.extend({"run_id": candidate["id"], **row} for row in subjects if row["reversed"])
            break
        if observed is None:
            raise ValueError(f"no complete machine health observation for UTC day {day_start.date()}")
        runs.append(observed)
    return runs, unexpected


def classify_release_manifest(manifest: dict, channel: dict, components: set[str], version: str) -> tuple[bool, bool]:
    descriptor, state = manifest.get("descriptor", {}), manifest.get("state", {})
    package_rows = descriptor.get("packages", [])
    package_ids = {row.get("id") for row in package_rows if isinstance(row, dict)}
    required = {"FS.GG.Coord.Cli", "FS.GG.Kit", "FS.GG.Drivers"}
    feeds = state.get("feeds", {})
    try:
        feed_ok = all(set(feeds.get(feed, {})) == required and all(
            value.get("state") == "verified" and value.get("externalPayloadSha256") == next(
                package["artifact"]["payloadSha256"] for package in package_rows if package["id"] == package_id)
            for package_id, value in feeds[feed].items()) for feed in ("github", "nuget"))
    except (KeyError, StopIteration, TypeError):
        feed_ok = False
    promotion = state.get("channelPromotion", {})
    identity_ok = (descriptor.get("version") == version and descriptor.get("releaseId") == f"github:{version}"
                   and descriptor.get("policyVersion") == "release-saga/1"
                   and package_ids == required and channel.get("version") == version
                   and channel.get("sourceSha") == descriptor.get("sourceSha")
                   and channel.get("contentId") == manifest.get("contentId")
                   and promotion.get("state") == "promoted"
                   and promotion.get("receipt", {}).get("contentId") == channel.get("contentId"))
    return identity_ok, feed_ok and components == {"coord-engine", "kit", "drivers"}


def registry_release_version(root: Path, sha: str) -> str:
    text = str(run(["git", "show", f"{sha}:registry/dependencies.yml"], root))
    match = re.search(r"(?ms)^\s*- id: coord-engine\s+.*?^\s+package-version:\s*[\"']?([^\"'\s#]+)", text)
    if not match:
        raise ValueError(f"{sha}: coord-engine package-version authority is missing")
    return match.group(1)


def release_outcomes(start: datetime, end: datetime, start_sha: str, end_sha: str, root: Path) -> tuple[list[str], list[dict]]:
    releases = gh_json(["--method", "GET", f"repos/{REPOSITORY}/releases", "-f", "per_page=100", "--paginate", "--slurp"], root)
    flattened = [row for page in releases for row in page]
    in_period = [row for row in flattened if (stamp := observed_at(row.get("published_at"))) is not None and start <= stamp < end]
    components: dict[str, set[str]] = {}
    coherent_releases: dict[str, dict] = {}
    for row in in_period:
        tag = row.get("tag_name", "")
        match = re.fullmatch(r"(coord-engine|kit|drivers)/v(.+)", tag)
        if match:
            components.setdefault(match.group(2), set()).add(match.group(1))
        match = re.fullmatch(r"coherent-set/v(.+)", tag)
        if match and not row.get("draft") and not row.get("prerelease"):
            coherent_releases[match.group(1)] = row
    details: list[dict] = []
    coherent: set[str] = set()
    for version, release in sorted(coherent_releases.items()):
        with tempfile.TemporaryDirectory() as work:
            run(["gh", "release", "download", release["tag_name"], "--repo", REPOSITORY,
                 "--dir", work, "--pattern", "release-manifest.json", "--pattern", "stable-channel.json"], root)
            manifest_path, channel_path = Path(work) / "release-manifest.json", Path(work) / "stable-channel.json"
            manifest, channel = json.loads(manifest_path.read_text()), json.loads(channel_path.read_text())
            descriptor = manifest.get("descriptor", {})
            run(["python3", "scripts/release-saga.py", "assert-identity", "--manifest", str(manifest_path),
                 "--release-id", f"github:{version}", "--version", version,
                 "--source-sha", str(descriptor.get("sourceSha", "")),
                 "--policy-version", "release-saga/1"], root)
            identity_ok, feed_ok = classify_release_manifest(manifest, channel, components.get(version, set()), version)
            manifest_sha = hashlib.sha256(manifest_path.read_bytes()).hexdigest()
            channel_sha = hashlib.sha256(channel_path.read_bytes()).hexdigest()
        ok = identity_ok and feed_ok
        if ok:
            coherent.add(version)
        details.append({"version": version, "components": sorted(components.get(version, set())),
                        "manifest_sha256": manifest_sha, "stable_channel_sha256": channel_sha,
                        "identity_ok": identity_ok, "dual_feed_payloads_verified": feed_ok,
                        "coherent": ok})
    for version in sorted(set(components) - set(coherent_releases)):
        details.append({"version": version, "components": sorted(components[version]),
                        "manifest_sha256": None, "stable_channel_sha256": None,
                        "identity_ok": False, "dual_feed_payloads_verified": False,
                        "coherent": False})
    ambiguous = sorted((set(components) | set(coherent_releases)) - coherent)
    outcomes = ["coherent"] if coherent else []
    if not components and not coherent_releases and registry_release_version(root, start_sha) == registry_release_version(root, end_sha):
        outcomes = ["no-release-owed"]
    elif ambiguous and not coherent:
        outcomes = []  # incomplete release without an authenticated saga receipt is ambiguous, never resumable
    return outcomes, details


def successor_census(root: Path) -> list[dict]:
    candidates: dict[int, str] = {}
    for query in SUCCESSOR_QUERIES:
        for item in search(query, root):
            candidates[int(item["number"])] = item["html_url"]
    rows = []
    for number, url in sorted(candidates.items()):
        if number in NOT_SAME_CLASS:
            rows.append({"url": url, "disposition": "not-same-class", "reason": NOT_SAME_CLASS[number]})
        else:
            rows.append({"url": url, "disposition": "blocking",
                         "reason": KNOWN_BLOCKERS.get(number, "Unclassified fixed-query result; fail closed pending reviewed disposition.")})
    return rows


def collect(root: Path, output: Path, now: datetime) -> Path:
    run(["gh", "api", "user"], root)
    source_sha = str(run(["git", "rev-parse", "HEAD"], root)).strip()
    remote_sha = gh_json([f"repos/{REPOSITORY}/commits/main"], root)["sha"]
    if source_sha != remote_sha:
        raise ValueError(f"HEAD {source_sha} is not current authenticated {REPOSITORY} main {remote_sha}")
    windows = exact_windows(now)
    prose_check = subprocess.run(["python3", "scripts/check-prose-citations.py", "--root", "."],
                                 cwd=root, text=True, capture_output=True)
    if prose_check.returncode != 0:
        raise ValueError("the landed prose-citation boundary is red; statement classification is not authoritative")

    try:
        output_relative = output.relative_to(root)
    except ValueError as error:
        raise ValueError("output directory must be inside the repository") from error
    output.mkdir(parents=True, exist_ok=True)
    rows = []
    for index, (start, end) in enumerate(windows, 1):
        start_sha, end_sha = first_parent_sha(root, source_sha, start), first_parent_sha(root, source_sha, end)
        created, closed = issue_counts(start, end, root)
        repairs, statements, commit_rows = commit_measure(root, start, end, end_sha)
        health_runs, machine_reversals = workflow_health(start, end, root)
        reversal_incidents = incident_events("intent_reversals", start, end, root)
        partial_incidents = incident_events("partial_success_reads", start, end, root)
        incomplete_reads = [{"run_id": run_row["run_id"], **subject} for run_row in health_runs
                            for subject in run_row["health"]["subjects"] if not subject["readComplete"]]
        release_values, release_rows = release_outcomes(start, end, start_sha, end_sha, root)
        before, after = inventory(root, start_sha), inventory(root, end_sha)
        ambiguous = sum(not row["coherent"] for row in release_rows)
        outcomes = release_values
        generated_start, generated_end = tree_bytes(root, start_sha, GENERATED_PREFIXES), tree_bytes(root, end_sha, GENERATED_PREFIXES)
        implementation_start, implementation_end = tree_bytes(root, start_sha, IMPLEMENTATION_PREFIXES), tree_bytes(root, end_sha, IMPLEMENTATION_PREFIXES)
        reproduce = ["python3", "scripts/coordination-health-collector.py", "--root", ".",
                     "--output-dir", CANONICAL_OUTPUT]
        observation = {
            "schema_version": 1, "source_sha": source_sha, "measured_at": iso(now),
            "period_id": f"week-{index}", "start": iso(start), "end": iso(end),
            "start_sha": start_sha, "end_sha": end_sha,
            "issues_created": len(created), "issues_closed": len(closed),
            "repair_commits": repairs, "statement_only_repairs": statements,
            "intent_reversals": len(machine_reversals) + len(reversal_incidents),
            "partial_success_reads": len(incomplete_reads) + len(partial_incidents),
            "ambiguous_release_states": ambiguous, "release_outcomes": outcomes,
            "policy_implementations_start": len(before["policy_implementations"]),
            "policy_implementations_end": len(after["policy_implementations"]),
            "check_scripts_start": len(before["check_scripts"]), "check_scripts_end": len(after["check_scripts"]),
            "workflows_start": len(before["workflows"]), "workflows_end": len(after["workflows"]),
            "generated_evidence_bytes_delta": generated_end - generated_start,
            "core_and_test_bytes_delta": implementation_end - implementation_start,
            "reproduce": reproduce,
            "raw": {"created": created, "closed": closed, "repair_classification": commit_rows,
                    "intent_reversal_events": machine_reversals + reversal_incidents,
                    "partial_success_events": incomplete_reads + partial_incidents,
                    "machine_health_runs": health_runs,
                    "release_classification": release_rows,
                    "inventory_snapshots": {"start": {"sha": start_sha, **before}, "end": {"sha": end_sha, **after}},
                    "byte_snapshots": {"generated_prefixes": GENERATED_PREFIXES,
                        "implementation_prefixes": IMPLEMENTATION_PREFIXES,
                        "start": {"sha": start_sha, "generated": generated_start, "implementation": implementation_start},
                        "end": {"sha": end_sha, "generated": generated_end, "implementation": implementation_end}},
                    "prose_citation_gate": prose_check.stdout.strip()},
        }
        payload = json.dumps(observation, sort_keys=True, separators=(",", ":")).encode()
        digest = hashlib.sha256(payload).hexdigest()
        artifact = output / f"week-{index}-{digest}.json"
        artifact.write_bytes(payload)
        row = {key: observation[key] for key in (
            "period_id", "start", "end", "start_sha", "end_sha", "issues_created", "issues_closed", "repair_commits",
            "statement_only_repairs", "intent_reversals", "partial_success_reads",
            "ambiguous_release_states", "release_outcomes", "policy_implementations_start",
            "policy_implementations_end", "check_scripts_start", "check_scripts_end",
            "workflows_start", "workflows_end", "generated_evidence_bytes_delta",
            "core_and_test_bytes_delta")}
        row["id"] = row.pop("period_id")
        row["verification"] = ["canonical collector schema 1", prose_check.stdout.strip()]
        relative = artifact.relative_to(root)
        row["provenance"] = {"artifact": str(relative), "sha256": digest, "reproduce": reproduce}
        rows.append(row)

    census = successor_census(root)
    document = {
        "schema_version": 1,
        "cycle_id": "roadmap-coordination-churn-redesign-m6-retire-compatibility",
        "measured_at": iso(now), "source_sha": source_sha, "verdict": "candidate",
        "collector": {"schema_version": 1, "command": ["python3", "scripts/coordination-health-collector.py",
                    "--root", ".", "--output-dir", CANONICAL_OUTPUT]},
        "candidate_periods": rows, "successor_queries": SUCCESSOR_QUERIES,
        "successor_census": census,
        "same_class_open": [{"url": row["url"], "reason": row["reason"]} for row in census if row["disposition"] == "blocking"],
    }
    destination = output / "retirement-readiness.json"
    destination.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    return destination


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("."))
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    try:
        root = args.root.resolve(strict=True)
        output = args.output_dir if args.output_dir.is_absolute() else root / args.output_dir
        destination = collect(root, output.resolve(), datetime.now(timezone.utc).replace(microsecond=0))
    except (OSError, ValueError, KeyError, json.JSONDecodeError, subprocess.CalledProcessError, AssertionError) as error:
        print(f"coordination health collector: BLOCKED: {error}", file=sys.stderr)
        return 1
    print(destination)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
