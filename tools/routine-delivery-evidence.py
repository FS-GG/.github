"""Optional bounded evidence reader for routine-delivery; no execution authority."""
from __future__ import annotations

import io
import json
import zipfile
import zlib
from datetime import datetime, timezone
from typing import Any


def collect(summary: Any, run_id: int, *, api: Any, native: Any) -> dict:
    """Collect pilot evidence using the delivery owner's native identity validators."""
    head_of, base_of = native.head_of, native.base_of
    merged_commit_of, SHA_RE = native.merged_commit_of, native.SHA_RE
    WATCH_FIELDS, WATCH_MAX_CHECKS = native.WATCH_FIELDS, native.WATCH_MAX_CHECKS
    repo, number = summary.repo, summary.pr
    result = {"schema": "fsgg.routine-delivery-evidence/1", "scope": "skill-quality-pilot",
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
    def snapshot():
        pr = api.get_pr(repo, number)
        if (head_of(pr) != summary.expectedHead or base_of(pr) != (summary.baseRef, summary.baseSha)
                or (summary.mergeCommit is not None and merged_commit_of(pr) != summary.mergeCommit)):
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
    def checkout():
        run = read(f"actions/runs/{run_id}")
        attempt_number = run.get("run_attempt")
        if (run.get("id") != run_id or run.get("head_sha") != summary.expectedHead
                or run.get("event") != "pull_request"
                or run.get("path") != ".github/workflows/skill-quality.yml"
                or run.get("repository", {}).get("full_name") != repo
                or type(attempt_number) is not int or attempt_number < 1
                or not any(row.get("number") == number and row.get("head", {}).get("sha") == summary.expectedHead
                           and row.get("base", {}).get("sha") == summary.baseSha for row in run.get("pull_requests", []))):
            raise ValueError("evidence differs")
        result["run"] = {key: run.get(key) for key in
                         ("id", "run_attempt", "head_sha", "path", "event", "status", "conclusion", "html_url")}
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
                    "run": run_id, "attempt": attempt_number, "job": "semantic-skill-quality"}
        if any(fact.get(key) != value or type(fact.get(key)) is not type(value) for key, value in expected.items()):
            raise ValueError("evidence differs")
        if not all(isinstance(fact.get(key), str) and SHA_RE.fullmatch(fact[key]) for key in ("commit", "tree")):
            raise ValueError("evidence differs")
        native = commit(fact["commit"])
        if native["tree"]["sha"] != fact["tree"]:
            raise ValueError("evidence differs")
        if fact["commit"] == summary.expectedHead:
            kind = "candidate"
        elif [parent.get("sha") for parent in native.get("parents", [])] == [summary.baseSha, summary.expectedHead]:
            kind = "synthetic-pr-merge"
        else:
            raise ValueError("evidence differs")
        return {"commit": fact["commit"], "tree": fact["tree"], "kind": kind,
                "run": run_id, "attempt": attempt_number, "job": fact["job"], "artifact": artifact_id,
                "coverage": "skill-quality job entry"}
    result["testedCheckout"] = attempt("tested-checkout", checkout)
    if attempt("final-pr-identity", snapshot) is None:
        result.update(testedCheckout=None, changedFiles=None, requiredChecks=None)
        return result
    if result["testedCheckout"] is not None and result["deliveredTree"] is not None:
        result["parity"] = ("identical-tree" if result["testedCheckout"]["tree"] == result["deliveredTree"]
                            else "different-tree")
    elif summary.codeDelivery == "not-delivered":
        result["parity"] = "not-delivered"
    return result

