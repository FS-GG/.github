"""Optional bounded evidence reader for routine-delivery; no execution authority."""
from __future__ import annotations

import io
import json
import re
import zipfile
import zlib
from datetime import datetime, timezone
from typing import Any


# Exact native workflow and job pairs; the old producer remains readable for retained artifacts.
PRODUCERS = {
    ".github/workflows/coherence.yml": ("projection", "github-pr-checkout", "projection job entry"),
    ".github/workflows/skill-quality.yml": ("semantic-skill-quality", "skill-quality-pilot", "skill-quality job entry"),
}


def collect(summary: Any, run_id: int | None, *, api: Any, native: Any) -> dict:
    """Collect pilot evidence using the delivery owner's native identity validators."""
    head_of, base_of = native.head_of, native.base_of
    merged_commit_of, SHA_RE = native.merged_commit_of, native.SHA_RE
    is_merged = native.is_merged
    WATCH_FIELDS, WATCH_MAX_CHECKS = native.WATCH_FIELDS, native.WATCH_MAX_CHECKS
    repo, number = summary.repo, summary.pr
    result = {"schema": "fsgg.routine-delivery-evidence/1", "scope": "github-pr-checkout",
              "candidate": summary.expectedHead, "base": summary.baseSha,
              "deliveredCommit": summary.mergeCommit, "deliveredTree": None,
              "testedCheckout": None, "parity": "unavailable", "changedFiles": None,
              "requiredChecks": None, "run": None, "exceptions": [],
              "observedAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")}
    def read(endpoint):
        return api._watch_read(["gh", "api", f"repos/{repo}/{endpoint}"], (0,))[1]
    def attempt(label, action):
        try:
            return action()
        except (RuntimeError, ValueError, KeyError, TypeError, AttributeError, OSError, zipfile.BadZipFile, zlib.error, EOFError):
            result["exceptions"].append(label + "-unavailable")
            return None
    before = None
    def snapshot():
        pr = api.get_pr(repo, number)
        if (head_of(pr) != summary.expectedHead or base_of(pr) != (summary.baseRef, summary.baseSha)
                or (summary.mergeCommit is not None and merged_commit_of(pr) != summary.mergeCommit)
                or (summary.codeDelivery == "delivered" and not is_merged(pr))
                or (before is not None and (pr.get("head", {}).get("ref") != before.get("head", {}).get("ref")
                                           or is_merged(pr) != is_merged(before)))):
            raise ValueError("evidence differs")
        return pr
    before = attempt("pr-identity", snapshot)
    if before is None:
        return result
    def commit(sha):
        value = read(f"git/commits/{sha}")
        if value.get("sha") != sha or not SHA_RE.fullmatch(value["tree"]["sha"]):
            raise ValueError("evidence differs")
        return value
    if summary.mergeCommit is not None:
        delivered = attempt("delivered-tree", lambda: commit(summary.mergeCommit))
        if delivered is not None:
            result["deliveredTree"] = delivered["tree"]["sha"]
    def files():
        count = before.get("changed_files")
        if type(count) is not int or not 0 <= count <= 100:
            raise ValueError("evidence differs")
        rows = read(f"pulls/{number}/files?per_page=100")
        if not isinstance(rows, list) or len(rows) != count:
            raise ValueError("evidence differs")
        output = []
        for row in rows:
            if not isinstance(row.get("filename"), str) or not isinstance(row.get("status"), str):
                raise ValueError("evidence differs")
            output.append({key: row.get(key) for key in ("filename", "previous_filename", "status")})
        if len({row["filename"] for row in output}) != count:
            raise ValueError("evidence differs")
        return output
    result["changedFiles"] = attempt("changed-files", files)
    def checks():
        _, rows = api._watch_read(["gh", "pr", "checks", str(number), "--repo", repo,
                                   "--required", "--json", WATCH_FIELDS], (0, 1, 8))
        if (not isinstance(rows, list) or len(rows) > WATCH_MAX_CHECKS
                or any(not isinstance(row, dict) or set(row) != set(WATCH_FIELDS.split(","))
                       or any(not isinstance(value, str) for value in row.values())
                       or not row["name"] or not row["state"]
                       or row["bucket"] not in {"pass", "fail", "pending", "skipping", "cancel"}
                       for row in rows)):
            raise ValueError("evidence differs")
        return rows
    result["requiredChecks"] = attempt("required-checks", checks)
    automatic = run_id is None
    selected_projection = None
    def checkout():
        nonlocal run_id, selected_projection
        if run_id is None:
            # Use the live receiver-required context, never a newest-run guess.
            rows = result["requiredChecks"]
            if rows is None:
                raise ValueError("required projection reference unavailable")
            matches = [row for row in rows if row["name"] == "projection"]
            if len(matches) != 1 or matches[0]["event"] != "pull_request" or matches[0]["workflow"] != "coherence":
                raise ValueError("required projection reference ambiguous")
            link = re.fullmatch(re.escape(f"https://github.com/{repo}/actions/runs/")
                                + r"([1-9][0-9]*)/job/([1-9][0-9]*)", matches[0]["link"])
            if link is None:
                raise ValueError("required projection reference malformed")
            run_id = int(link[1])
            selected_projection = matches[0]
            selected_job = int(link[2])
        run = read(f"actions/runs/{run_id}")
        attempt_number = run.get("run_attempt")
        producer = PRODUCERS.get(run.get("path"))
        if producer is None or (automatic and run.get("path") != ".github/workflows/coherence.yml"):
            raise ValueError("unsupported checkout producer")
        links = run.get("pull_requests")
        if not isinstance(links, list) or len(links) > 100:
            raise ValueError("run PR link population differs")
        linked, seen = False, set()
        for row in links:
            if (not isinstance(row, dict) or type(row.get("number")) is not int or row["number"] < 1
                    or row["number"] in seen or not isinstance(row.get("head"), dict)
                    or not isinstance(row.get("base"), dict)
                    or any(not isinstance(row[side].get("sha"), str)
                           or not SHA_RE.fullmatch(row[side]["sha"]) for side in ("head", "base"))):
                raise ValueError("run PR link population differs")
            seen.add(row["number"])
            if row["number"] == number:
                if row["head"]["sha"] != summary.expectedHead or row["base"]["sha"] != summary.baseSha:
                    raise ValueError("run PR link identity differs")
                linked = True
        # GitHub can remove this association after merge (observed on PR4597).
        # The fallback additionally requires the artifact's native exact-parent merge below.
        merged_link = (links == [] and summary.codeDelivery == "delivered" and is_merged(before)
            and summary.mergeCommit == merged_commit_of(before)
            and isinstance(before.get("head", {}).get("ref"), str) and bool(before["head"]["ref"])
            and run.get("head_branch") == before["head"]["ref"])
        if (run.get("id") != run_id or run.get("head_sha") != summary.expectedHead
                or run.get("event") != "pull_request"
                or run.get("repository", {}).get("full_name") != repo
                or type(attempt_number) is not int or attempt_number < 1
                or not (linked or merged_link)):
            raise ValueError("evidence differs")
        if automatic:
            job = read(f"actions/jobs/{selected_job}")
            expected_job = {"id": selected_job, "run_id": run_id, "run_attempt": attempt_number,
                            "name": producer[0], "head_sha": summary.expectedHead}
            if any(job.get(key) != value or type(job.get(key)) is not type(value)
                   for key, value in expected_job.items()):
                raise ValueError("required projection job differs")
        result["scope"] = producer[1]
        result["run"] = {key: run.get(key) for key in
                         ("id", "run_attempt", "head_sha", "head_branch", "path", "event", "status", "conclusion", "html_url")}
        artifacts = read(f"actions/runs/{run_id}/artifacts?per_page=100")
        rows = artifacts.get("artifacts")
        if (not isinstance(rows, list) or artifacts.get("total_count") != len(rows) or len(rows) > 100):
            raise ValueError("evidence differs")
        matches = [row for row in rows if row.get("name") == f"tested-checkout-{run_id}-{attempt_number}"]
        if (len(matches) != 1 or matches[0].get("expired") is not False
                or type(matches[0].get("id")) is not int
                or type(matches[0].get("size_in_bytes")) is not int
                or not 0 < matches[0]["size_in_bytes"] <= 65536):
            raise ValueError("evidence differs")
        artifact_id = matches[0]["id"]
        command = ["gh", "api", f"repos/{repo}/actions/artifacts/{artifact_id}/zip"]
        code, raw = api.query(command, api.deadline, clock=api.clock)
        if code != 0 or len(raw) > 65536:
            raise ValueError("evidence differs")
        with zipfile.ZipFile(io.BytesIO(raw)) as archive:
            entries = archive.infolist()
            if len(entries) != 1 or entries[0].filename != "checkout.json" or entries[0].file_size > 4096:
                raise ValueError("evidence differs")
            def unique(pairs):
                value = {}
                for key, item in pairs:
                    if key in value:
                        raise ValueError("evidence differs")
                    value[key] = item
                return value
            fact = json.loads(archive.read(entries[0]), object_pairs_hook=unique)
        expected = {"schema": "fsgg.tested-checkout/1", "repository": repo,
                    "pr": number, "candidate": summary.expectedHead, "base": summary.baseSha,
                    "run": run_id, "attempt": attempt_number, "job": producer[0]}
        if any(fact.get(key) != value or type(fact.get(key)) is not type(value) for key, value in expected.items()):
            raise ValueError("evidence differs")
        if not all(isinstance(fact.get(key), str) and SHA_RE.fullmatch(fact[key]) for key in ("commit", "tree")):
            raise ValueError("evidence differs")
        checkout_commit = commit(fact["commit"])
        if checkout_commit["tree"]["sha"] != fact["tree"]:
            raise ValueError("evidence differs")
        if fact["commit"] == summary.expectedHead:
            kind = "candidate"
        elif [parent.get("sha") for parent in checkout_commit.get("parents", [])] == [summary.baseSha, summary.expectedHead]:
            kind = "synthetic-pr-merge"
        else:
            raise ValueError("evidence differs")
        if not linked and kind != "synthetic-pr-merge":
            raise ValueError("merged run association requires exact checkout parents")
        return {"commit": fact["commit"], "tree": fact["tree"], "kind": kind,
                "association": "native-pr-link" if linked else "merged-pr-exact-checkout-parents",
                "run": run_id, "attempt": attempt_number, "job": fact["job"], "artifact": artifact_id,
                "coverage": producer[2]}
    result["testedCheckout"] = attempt("tested-checkout", checkout)
    if automatic and selected_projection is not None:
        final_checks = attempt("final-required-checks", checks)
        final_projection = ([row for row in final_checks if row["name"] == "projection"]
                            if final_checks is not None else [])
        if (len(final_projection) != 1 or any(final_projection[0][key] != selected_projection[key]
                for key in ("link", "event", "workflow"))):
            result["exceptions"].append("required-projection-changed")
            result["testedCheckout"] = None
        result["requiredChecks"] = final_checks
        def final_run():
            latest = read(f"actions/runs/{run_id}")
            if result["run"] is None or any(latest.get(key) != result["run"][key]
                    for key in ("id", "run_attempt", "head_sha", "path", "event", "status", "conclusion")):
                raise ValueError("producer run changed")
            return True
        if attempt("final-producer-run", final_run) is None:
            result["testedCheckout"] = None
    if attempt("final-pr-identity", snapshot) is None:
        result.update(testedCheckout=None, changedFiles=None, requiredChecks=None)
        return result
    if result["testedCheckout"] is not None and result["deliveredTree"] is not None:
        result["parity"] = ("identical-tree" if result["testedCheckout"]["tree"] == result["deliveredTree"]
                            else "different-tree")
    elif summary.codeDelivery == "not-delivered":
        result["parity"] = "not-delivered"
    return result


def assemble(summary: Any, evidence: dict, *, max_bytes: int = 65280) -> dict:
    """Append a compact assessment without changing the original result or its authority."""
    from dataclasses import asdict
    # Reserve 256 bytes for the existing optional dashboard health field and newline.
    output = asdict(summary)
    evidence = dict(evidence)
    def assess():
        issues = list(evidence.get("exceptions", []))
        if summary.outcome not in {"ready", "delivered", "delivered-after-readback"}:
            issues.append("delivery-" + summary.outcome)
        if summary.publication == "pending":
            issues.append("publication-pending")
        if summary.coherentValidation != "passed":
            issues.append("coherent-validation-" + summary.coherentValidation)
        if summary.routineEligibility not in {"passed", "not-required"}:
            issues.append("routine-eligibility-" + summary.routineEligibility)
        if evidence.get("testedCheckout") is None:
            issues.append("tested-checkout-unavailable")
        checks = evidence.get("requiredChecks")
        if not checks or any(row["bucket"] != "pass" for row in checks):
            issues.append("required-checks-not-passed")
        run = evidence.get("run")
        if not run or run.get("status") != "completed" or run.get("conclusion") != "success":
            issues.append("producer-run-not-passed")
        if summary.codeDelivery == "delivered" and evidence.get("parity") != "identical-tree":
            issues.append("delivered-tree-not-verified")
        evidence["assessment"] = "attention" if issues else ("delivered" if summary.codeDelivery == "delivered" else "ready")
        evidence["issues"] = sorted(set(issues))
    assess()
    output["deliveryEvidence"] = evidence
    def size():
        return len((json.dumps(output, separators=(",", ":"), sort_keys=True) + "\n").encode())
    if size() > max_bytes:
        # Preserve custody bounds and essential identities; raw evidence remains native.
        evidence.update(changedFiles=None, requiredChecks=None)
        evidence["exceptions"] = list(evidence.get("exceptions", [])) + ["result-populations-over-budget"]
        assess()
    if size() > max_bytes:
        evidence = {"schema": "fsgg.routine-delivery-evidence/1", "scope": "github-pr-checkout",
                    "parity": "unavailable", "exceptions": ["result-over-budget"],
                    "assessment": "attention", "issues": ["result-over-budget"]}
        output["deliveryEvidence"] = evidence
    return output
