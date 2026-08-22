#!/usr/bin/env python3
"""Derive the seven roadmap-health results from raw records and exact Git objects."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from datetime import datetime, timedelta
from pathlib import Path

IDS = (
    "issue-flow",
    "behaviourless-repairs",
    "scheduling-intent",
    "complete-reads",
    "release-coherence",
    "artifact-trend",
    "evidence-growth",
)
ROOT = Path(__file__).resolve().parents[1]
ROADMAP = ROOT / "docs/reports/2026-08-14-090508-coordination-churn-redesign-roadmap.md"
DURABLE_REF = re.compile(
    r"https://github\.com/[^/]+/[^/]+/(?:issues/[1-9][0-9]*(?:#issuecomment-[1-9][0-9]*)?|actions/runs/[1-9][0-9]*|releases/tag/[^/#?]+)"
)
MILESTONE_TABLE = re.compile(
    r"<!-- roadmap-health-milestone-scores:v1 -->\n(.*?)\n<!-- /roadmap-health-milestone-scores:v1 -->",
    re.DOTALL,
)
MILESTONE_GAPS = {
    ("main-green", "unverified"): "No complete required-check census source is bound to the window.",
    ("repairs-settled", "unverified"): "No complete open-repair census source is bound to the window.",
    ("baseline-reproducible", "met"): "Raw issue census and exact Git objects reproduce the baseline.",
    ("reconciliation-intent", "violated"): "A typed scheduling-reversal incident contradicts survival of deliberate parks.",
    ("reconciliation-intent", "met"): "A complete typed scheduling-reversal census contains no incidents.",
    ("reconciliation-intent", "unverified"): "No scheduling-reversal incident is recorded, but the census is incomplete.",
    ("complete-read-boundary", "violated"): "A typed partial-read incident contradicts the complete-read boundary.",
    ("complete-read-boundary", "met"): "A complete typed partial-read census contains no incidents.",
    ("complete-read-boundary", "unverified"): "No partial-read incident is recorded, but the census is incomplete.",
    ("coherent-release", "violated"): "A typed ambiguous-release incident contradicts coherent resumable release.",
    ("coherent-release", "met"): "A complete typed ambiguous-release census contains no incidents.",
    ("coherent-release", "unverified"): "No ambiguous-release incident is recorded, but the census is incomplete.",
    ("structured-decisions", "unverified"): "No complete effective-decision census source is bound to the window.",
    ("artifact-decline", "violated"): "Exact Git-derived check and workflow counts rose; independent policy-implementation count is unverified.",
    ("artifact-decline", "unverified"): "Known counters declined, but the independent policy-implementation count is unverified.",
    ("healthy-cycles", "violated"): "At least one active health measure is violated; the three-cycle composite is not met.",
    ("healthy-cycles", "unverified"): "No active health measure is violated, but at least one remains unverified.",
    ("healthy-cycles", "met"): "Every active health measure is met, including three consecutive issue-flow periods.",
    ("no-open-successor", "violated"): "The bounded successor census contains open issues.",
    ("no-open-successor", "met"): "The bounded successor census contains no open issues.",
}


def fail(message: str) -> None:
    raise ValueError(message)


def utc(value: object) -> datetime:
    if not isinstance(value, str) or not value.endswith("Z"):
        fail("timestamps must be RFC3339 UTC")
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        fail("timestamps must be RFC3339 UTC")
    if parsed.utcoffset() != timedelta(0):
        fail("timestamps must be RFC3339 UTC")
    return parsed


def canonical_sha256(value: object) -> str:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":")).encode()
    return hashlib.sha256(encoded).hexdigest()


def repository_path(repository: Path, relative: object) -> Path:
    if not isinstance(relative, str) or not relative:
        fail("sourceBoundary.snapshotPath must be a nonempty repository-relative path")
    root = repository.resolve()
    path = (root / relative).resolve()
    try:
        path.relative_to(root)
    except ValueError:
        fail("sourceBoundary.snapshotPath must stay inside the repository")
    return path


def read_fixture(path: Path, repository: Path = ROOT) -> dict:
    try:
        data = json.loads(path.read_text())
    except (OSError, json.JSONDecodeError) as error:
        fail(f"cannot read health fixture {path}: {error}")
    if not isinstance(data, dict) or data.get("schema") != "fsgg.coord.roadmap-health-input/v2":
        fail("fixture schema must be fsgg.coord.roadmap-health-input/v2")
    window = data.get("window", {})
    if (
        not isinstance(window, dict)
        or set(window) != {"start", "end"}
        or utc(window.get("start")) >= utc(window.get("end"))
        or utc(window["end"]) - utc(window["start"]) != timedelta(days=21)
    ):
        fail("window must be one ordered 21-day UTC interval")
    boundary = data.get("sourceBoundary")
    if (
        not isinstance(boundary, dict)
        or set(boundary) != {"repository", "query", "asOf", "method", "snapshotPath", "recordsSha256"}
        or not all(isinstance(boundary.get(key), str) and boundary[key] for key in boundary)
        or not re.fullmatch(r"[0-9a-f]{64}", boundary["recordsSha256"])
        or utc(boundary["asOf"]) < utc(window["end"])
    ):
        fail("sourceBoundary must bind a complete post-window raw issue census")
    snapshot_path = repository_path(repository, boundary["snapshotPath"])
    try:
        snapshot = json.loads(snapshot_path.read_text())
    except (OSError, json.JSONDecodeError) as error:
        fail(f"cannot read issue census snapshot {snapshot_path}: {error}")
    if (
        not isinstance(snapshot, dict)
        or set(snapshot) != {"schema", "records"}
        or snapshot.get("schema") != "fsgg.coord.issue-census/v1"
        or not isinstance(snapshot.get("records"), list)
    ):
        fail("issue census must contain a typed raw record list")
    records = snapshot["records"]
    if canonical_sha256(records) != boundary["recordsSha256"]:
        fail("issue census raw-record digest does not match sourceBoundary")
    as_of = utc(boundary["asOf"])
    seen: set[int] = set()
    for record in records:
        if not isinstance(record, dict) or set(record) != {"number", "createdAt", "closedAt"}:
            fail("issue census records must contain exactly number, createdAt, and closedAt")
        number = record["number"]
        if not isinstance(number, int) or isinstance(number, bool) or number <= 0 or number in seen:
            fail("issue census issue numbers must be unique positive integers")
        seen.add(number)
        created = utc(record["createdAt"])
        closed_value = record["closedAt"]
        closed = None if closed_value is None else utc(closed_value)
        if created > as_of or (closed is not None and (closed < created or closed > as_of)):
            fail("issue census timestamps must follow creation/closure/as-of semantics")
    measures = data.get("measures")
    if not isinstance(measures, dict) or set(measures) != set(IDS):
        fail("fixture must contain exactly the seven typed measure inputs")
    for identifier in ("issue-flow", "behaviourless-repairs", "artifact-trend", "evidence-growth"):
        if measures[identifier] != {}:
            fail(f"{identifier} accepts no asserted summary values")
    git_boundary = data.get("gitBoundary")
    if not isinstance(git_boundary, dict) or set(git_boundary) != {"base", "head"}:
        fail("gitBoundary must contain exact base and head revisions")
    if "milestoneEvidence" in data:
        fail("milestoneEvidence verdict inputs are forbidden; milestone states derive from raw evidence")
    successor_numbers = {266, 2752, 2691}
    if {record["number"] for record in records if record["number"] in successor_numbers} != successor_numbers:
        fail("raw issue census must contain exactly the named M6 successor inventory #266, #2752, and #2691")
    data["_issueRecords"] = records
    return data


def row(identifier: str, verdict: str, reason: str, value=None) -> dict:
    result = {"id": identifier, "verdict": verdict, "reason": reason}
    if value is not None:
        result["value"] = value
    return result


def git(repository: Path, *args: str) -> str:
    completed = subprocess.run(
        ["git", "-C", str(repository), *args], text=True, capture_output=True
    )
    if completed.returncode != 0:
        fail(f"git {' '.join(args)} failed: {completed.stderr.strip()}")
    return completed.stdout


def resolve_commit(repository: Path, value: object, field: str) -> str:
    if not isinstance(value, str) or not value.strip():
        fail(f"{field} must be a nonempty resolvable Git revision")
    resolved = git(repository, "rev-parse", "--verify", f"{value}^{{commit}}").strip()
    if not re.fullmatch(r"[0-9a-f]{40}", resolved):
        fail(f"{field} must resolve to one exact commit")
    return resolved


def tree_count(repository: Path, revision: str, prefix: str, matcher) -> int:
    paths = git(repository, "ls-tree", "-r", "--name-only", revision, "--", prefix).splitlines()
    return sum(1 for path in paths if matcher(path))


def net_lines(repository: Path, base: str, head: str, paths: tuple[str, ...]) -> dict:
    additions = deletions = 0
    for line in git(repository, "diff", "--numstat", base, head, "--", *paths).splitlines():
        fields = line.split("\t", 2)
        if len(fields) != 3 or fields[0] == "-" or fields[1] == "-":
            fail(f"line census for {','.join(paths)} encountered an unreadable or binary diff")
        additions += int(fields[0])
        deletions += int(fields[1])
    return {
        "paths": list(paths),
        "method": "git diff --numstat; net = additions - deletions",
        "additions": additions,
        "deletions": deletions,
        "net": additions - deletions,
    }


def incident_measure(data: dict, identifier: str, label: str) -> dict:
    value = data["measures"][identifier]
    if not isinstance(value, dict) or set(value) != {"incidents", "census"}:
        fail(f"{identifier} must contain typed incidents and census")
    incidents, census = value["incidents"], value["census"]
    if not isinstance(incidents, list) or not isinstance(census, dict):
        fail(f"{identifier} incidents/census must be typed")
    if (
        set(census) != {"complete", "asOf", "method"}
        or not isinstance(census.get("complete"), bool)
        or not isinstance(census.get("method"), str)
        or not census["method"].strip()
        or utc(census.get("asOf")) < utc(data["window"]["end"])
    ):
        fail(f"{identifier}.census must be a typed post-window census")
    for incident in incidents:
        if (
            not isinstance(incident, dict)
            or set(incident) != {"ref", "date"}
            or not isinstance(incident.get("ref"), str)
            or DURABLE_REF.fullmatch(incident["ref"]) is None
            or not (utc(data["window"]["start"]) <= utc(incident.get("date")) < utc(data["window"]["end"]))
        ):
            fail(f"{identifier} incidents require accountable durable refs and dates inside the window")
    if incidents:
        return row(identifier, "violated", f"Derived from {len(incidents)} typed {label} incident(s).", incidents)
    if census["complete"]:
        return row(identifier, "met", f"Derived from a complete typed {label} census.", census)
    return row(identifier, "unverified", f"Incident register is empty, but the typed {label} census is incomplete.", census)


def milestone_predicate(identifier: str, text: str, verdict: str, evidence: list[str]) -> dict:
    if not evidence or (identifier, verdict) not in MILESTONE_GAPS:
        fail(f"cannot derive milestone gap for {identifier}/{verdict} without typed evidence")
    return {"id": identifier, "exitPredicate": text, "verdict": verdict, "gap": MILESTONE_GAPS[(identifier, verdict)], "evidence": evidence}


def milestone_score(identifier: str, predicates: list[dict]) -> dict:
    verdicts = [predicate["verdict"] for predicate in predicates]
    verdict = "violated" if "violated" in verdicts else ("unverified" if "unverified" in verdicts else "met")
    return {"id": identifier, "predicates": predicates, "verdict": verdict, "checkboxExpected": verdict == "met"}


def health_cycles_predicate(measures: list[dict]) -> dict:
    active = [measure for measure in measures if measure["verdict"] != "retired"]
    expected_active = [identifier for identifier in IDS if identifier != "behaviourless-repairs"]
    if [measure["id"] for measure in active] != expected_active:
        fail("M6 health-cycle composite must contain every active non-retired health measure exactly once")
    if any(measure["verdict"] not in {"met", "violated", "unverified"} for measure in active):
        fail("active health measures require met, violated, or unverified verdicts")
    issue_periods = active[0].get("value")
    if not isinstance(issue_periods, list) or len(issue_periods) != 3:
        fail("M6 health-cycle composite requires three derived issue-flow periods")
    verdict = (
        "violated" if any(measure["verdict"] == "violated" for measure in active)
        else "unverified" if any(measure["verdict"] == "unverified" for measure in active)
        else "met"
    )
    evidence = [f"{measure['id']}:{measure['verdict']}" for measure in active]
    evidence.append("issue-flow-periods:" + ",".join(f"{period['opened']}/{period['closed']}" for period in issue_periods))
    return milestone_predicate("healthy-cycles", "Three consecutive operating cycles meet the health measures below", verdict, evidence)


def derive_milestone_scores(data: dict, measures: list[dict]) -> list[dict]:
    by_id = {measure["id"]: measure for measure in measures}
    successor_numbers = (266, 2752, 2691)
    issue_records = {record["number"]: record for record in data["_issueRecords"]}
    successors = [
        {
            "ref": f"https://github.com/FS-GG/.github/issues/{number}",
            "state": "open" if issue_records[number]["closedAt"] is None else "closed",
        }
        for number in successor_numbers
    ]
    def incident_evidence(identifier: str) -> list[str]:
        value = by_id[identifier].get("value")
        if isinstance(value, list):
            return [item["ref"] for item in value]
        if isinstance(value, dict):
            return [value["method"]]
        return []
    return [
        milestone_score("M0", [
            milestone_predicate("main-green", "Main has no standing red checks", "unverified", ["missing-source:required-check-census"]),
            milestone_predicate("repairs-settled", "Open repair PRs are mergeable or explicitly superseded", "unverified", ["missing-source:open-repair-census"]),
            milestone_predicate("baseline-reproducible", "Baseline is reproducible", "met", [data["sourceBoundary"]["recordsSha256"], data["gitBoundary"]["base"], data["gitBoundary"]["head"]]),
        ]),
        milestone_score("M1", [
            milestone_predicate("reconciliation-intent", "Reconciliation is idempotent; explicit Backlog and human parks survive; replay differences are explained; rollback is a projection switch", by_id["scheduling-intent"]["verdict"], incident_evidence("scheduling-intent")),
        ]),
        milestone_score("M2", [
            milestone_predicate("complete-read-boundary", "No production call site handles raw GraphQL envelopes; incomplete reads cannot be returned as success", by_id["complete-reads"]["verdict"], incident_evidence("complete-reads")),
        ]),
        milestone_score("M3", [
            milestone_predicate("coherent-release", "One full coherent-set release reaches both feeds without manual recovery; forced mid-publish failure resumes safely with identical hashes", by_id["release-coherence"]["verdict"], incident_evidence("release-coherence")),
        ]),
        milestone_score("M4", [
            milestone_predicate("structured-decisions", "Body-only edits neither grant nor revoke machine authorization; every effective decision is bound to structured inputs and a revision", "unverified", ["missing-source:effective-decision-census"]),
        ]),
        milestone_score("M5", [
            milestone_predicate("artifact-decline", "Material policy has one source; bulky evidence leaves Git; checker/workflow count and duplicated policy decline without coverage loss", by_id["artifact-trend"]["verdict"], [data["gitBoundary"]["base"], data["gitBoundary"]["head"]]),
        ]),
        milestone_score("M6", [
            health_cycles_predicate(measures),
            milestone_predicate("no-open-successor", "No same-class successor issue remains open", "violated" if any(item["state"] == "open" for item in successors) else "met", [f"{item['ref']}:{item['state']}" for item in successors]),
        ]),
    ]


def parse_milestone_table(roadmap: str) -> list[dict]:
    match = MILESTONE_TABLE.search(roadmap)
    if match is None:
        fail("roadmap must contain the typed milestone score table block")
    lines = match.group(1).splitlines()
    if lines[:2] != [
        "| milestone | verdict | checkboxExpected | predicates |",
        "|---|---|---|---|",
    ] or len(lines) != 9:
        fail("roadmap milestone score table must have the exact v1 header and seven rows")
    parsed = []
    for line in lines[2:]:
        cells = [cell.strip() for cell in line.split("|")[1:-1]]
        if len(cells) != 4 or cells[2] not in {"true", "false"}:
            fail("roadmap milestone score table row is malformed")
        try:
            predicates = json.loads(cells[3].replace("&vert;", "|"))
        except json.JSONDecodeError:
            fail("roadmap milestone score predicates must be canonical JSON")
        parsed.append({"id": cells[0], "verdict": cells[1], "checkboxExpected": cells[2] == "true", "predicates": predicates})
    return parsed


def render_milestone_table(scores: list[dict]) -> str:
    lines = [
        "<!-- roadmap-health-milestone-scores:v1 -->",
        "| milestone | verdict | checkboxExpected | predicates |",
        "|---|---|---|---|",
    ]
    for score in scores:
        predicates = json.dumps(score["predicates"], sort_keys=True, separators=(",", ":")).replace("|", "&vert;")
        checkbox = str(score["checkboxExpected"]).lower()
        lines.append(f"| {score['id']} | {score['verdict']} | {checkbox} | {predicates} |")
    lines.append("<!-- /roadmap-health-milestone-scores:v1 -->")
    return "\n".join(lines)


def validate_milestone_scores(scores: object, expected: list[dict], roadmap: str) -> None:
    if not isinstance(scores, list) or len(scores) != 7 or [item.get("id") if isinstance(item, dict) else None for item in scores] != [f"M{index}" for index in range(7)]:
        fail("milestoneScores must contain exactly ordered unique M0-M6 entries")
    if scores != expected:
        fail("milestoneScores do not match the derived predicate verdict, gap, or evidence authority")
    if parse_milestone_table(roadmap) != scores or MILESTONE_TABLE.sub(render_milestone_table(scores), roadmap) != roadmap:
        fail("roadmap milestone score table does not exactly match derived milestoneScores")
    checkbox_rows = re.findall(r"^- \[([ xX])\] \*\*(M[0-6]) —", roadmap, re.MULTILINE)
    if len(checkbox_rows) != 7 or [identifier for _, identifier in checkbox_rows] != [f"M{index}" for index in range(7)]:
        fail("roadmap must contain exactly ordered unique M0-M6 checkboxes")
    actual = {identifier: marker.lower() == "x" for marker, identifier in checkbox_rows}
    for score in scores:
        if score["verdict"] not in {"met", "violated", "unverified"} or score["checkboxExpected"] != (score["verdict"] == "met") or actual[score["id"]] != score["checkboxExpected"]:
            fail(f"roadmap checkbox for {score['id']} must equal its typed milestone verdict")


def report(data: dict, repository: Path = ROOT, roadmap: str | None = None) -> dict:
    start = utc(data["window"]["start"])
    records = data.get("_issueRecords")
    if not isinstance(records, list):
        fail("raw issue census was not loaded and validated")
    periods = []
    for index in range(3):
        period_start = start + timedelta(days=index * 7)
        period_end = period_start + timedelta(days=7)
        periods.append(
            {
                "start": period_start.isoformat().replace("+00:00", "Z"),
                "end": period_end.isoformat().replace("+00:00", "Z"),
                "opened": sum(1 for issue in records if period_start <= utc(issue["createdAt"]) < period_end),
                "closed": sum(1 for issue in records if issue["closedAt"] is not None and period_start <= utc(issue["closedAt"]) < period_end),
            }
        )
    issue = row(
        "issue-flow",
        "met" if all(period["opened"] < period["closed"] for period in periods) else "violated",
        f"Derived {len(records)} raw issue records into exact half-open contiguous UTC windows.",
        periods,
    )
    retired = row("behaviourless-repairs", "retired", "Retired 2026-08-22 by operator-delegated host: no authoritative behaviour-changing classifier exists.")

    boundary = data["gitBoundary"]
    base = resolve_commit(repository, boundary.get("base"), "gitBoundary.base")
    head = resolve_commit(repository, boundary.get("head"), "gitBoundary.head")
    checks = {
        "baseline": tree_count(repository, base, "scripts", lambda path: Path(path).name.startswith("check-")),
        "current": tree_count(repository, head, "scripts", lambda path: Path(path).name.startswith("check-")),
        "method": "git ls-tree -r --name-only REV -- scripts; count every file whose basename starts check-",
    }
    workflows = {
        "baseline": tree_count(repository, base, ".github/workflows", lambda path: True),
        "current": tree_count(repository, head, ".github/workflows", lambda path: True),
        "method": "git ls-tree -r --name-only REV -- .github/workflows; count every file",
    }
    artifact_value = {
        "base": base,
        "head": head,
        "policyImplementations": {"verdict": "unverified", "reason": "No authoritative independent-policy-implementation enumerator is defined."},
        "checks": checks,
        "workflows": workflows,
    }
    artifact = row(
        "artifact-trend",
        "violated" if checks["current"] >= checks["baseline"] or workflows["current"] >= workflows["baseline"] else "unverified",
        "Known exact Git-derived check/workflow counters did not both decline; policy-implementation count remains unverified.",
        artifact_value,
    )
    generated = net_lines(repository, base, head, ("work", "readiness"))
    implementation_and_tests = net_lines(repository, base, head, ("src", "tests"))
    evidence_value = {
        "base": base,
        "head": head,
        "generatedEvidence": generated,
        "implementationAndTests": implementation_and_tests,
    }
    evidence = row(
        "evidence-growth",
        "met" if generated["net"] < implementation_and_tests["net"] else "violated",
        "Derived both net line counts from git diff --numstat at one exact boundary.",
        evidence_value,
    )
    source = dict(data["sourceBoundary"])
    source["resultCount"] = len(records)
    measures = [
        issue,
        retired,
        incident_measure(data, "scheduling-intent", "scheduling reversal"),
        incident_measure(data, "complete-reads", "partial-read"),
        incident_measure(data, "release-coherence", "ambiguous release"),
        artifact,
        evidence,
    ]
    scores = derive_milestone_scores(data, measures)
    if roadmap is None:
        roadmap = ROADMAP.read_text()
    validate_milestone_scores(scores, scores, roadmap)
    return {
        "schema": "fsgg.coord.roadmap-health/v2",
        "window": data["window"],
        "sourceBoundary": source,
        "measures": measures,
        "milestoneScores": scores,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--repo", type=Path, default=ROOT)
    parser.add_argument("--roadmap", type=Path, default=ROADMAP)
    parser.add_argument("--format", choices=("json",), default="json")
    args = parser.parse_args()
    try:
        print(json.dumps(report(read_fixture(args.fixture, args.repo), args.repo, args.roadmap.read_text()), indent=2, sort_keys=True))
    except ValueError as error:
        print(f"report-roadmap-health: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
