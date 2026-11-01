"""Fixtures for scripts/board-currency-rules.py and scripts/board-currency-fetch.py (Refs #4730).

Every rule has a leg that fires, a leg that stays silent on the nearest passing board, and, where the
rule needs input that can be missing, a leg that returns Unknown instead of a verdict. The mutation
controls (mutate.py) re-run this file against deliberately broken copies of the rules module, so a
rule whose verdict no test pins is caught. BOARD_CURRENCY_RULES overrides the module path for them.
"""
from __future__ import annotations

import copy
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RULES_PATH = Path(os.environ.get("BOARD_CURRENCY_RULES", ROOT / "scripts/board-currency-rules.py"))
FETCH_PATH = ROOT / "scripts/board-currency-fetch.py"


def load(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


R = load(RULES_PATH, "board_currency_rules")
NOW = "2026-10-31T12:00:00Z"
RECENT = "2026-10-31T10:00:00Z"
OLD = "2026-10-20T10:00:00Z"
ROOT_LOGIN = "EHotwagner"


def issue(number, *, repo="FS-GG/.github", level="Delivery", status="In progress", state="OPEN", track="Active delivery",
          parent=None, subs=None, blocked=None, comments=None, reason=None, closed_at=None, updated=RECENT,
          private=False, sub_total=None, blocked_total=None):
    subs = subs or []
    blocked = blocked or []
    return {
        "itemId": f"PVTI_{repo}_{number}", "type": "Issue", "repo": repo, "repoPrivate": private, "number": number,
        "title": f"card {number}", "state": state, "stateReason": reason, "closedAt": closed_at,
        "contentUpdatedAt": updated, "status": status, "statusUpdatedAt": updated, "track": track,
        "workLevel": level, "itemUpdatedAt": updated,
        "parent": {"repo": parent[0], "number": parent[1]} if parent else None,
        "subIssues": {"total": len(subs) if sub_total is None else sub_total,
                      "nodes": [{"repo": r, "number": n, "state": s} for r, n, s in subs]},
        "blockedBy": {"total": len(blocked) if blocked_total is None else blocked_total,
                      "nodes": [{"repo": r, "number": n, "state": s} for r, n, s in blocked]},
        "comments": comments,
    }


def pr(number, body, *, repo="FS-GG/.github", state="OPEN", draft=False, updated=RECENT, merged_at=None, private=False):
    return {"repo": repo, "number": number, "draft": draft, "title": f"pr {number}", "state": state,
            "updatedAt": updated, "mergedAt": merged_at, "body": body, "repoPrivate": private}


def comment(body, *, author=ROOT_LOGIN, at=RECENT, cid=1):
    return {"id": cid, "author": author, "createdAt": at, "body": body}


ROUTINE = "\n<!-- fsgg:routine-development/v1 head=" + "a" * 40 + " operation=source-change -->"


def healthy():
    """A board on which every rule is silent: one Outcome, one active Delivery under it with an open PR."""
    outcome = issue(1, level="Outcome", status="In progress", subs=[("FS-GG/.github", 2, "OPEN")], comments=[])
    delivery = issue(2, parent=("FS-GG/.github", 1), comments=[])
    return {
        "schema": R.SNAPSHOT_SCHEMA, "complete": True, "fetchedAt": NOW,
        "project": {"owner": "FS-GG", "number": 3, "totalCount": 2},
        "items": [outcome, delivery],
        "pullRequests": [pr(10, "Refs #2" + ROUTINE)],
        "refStates": {},
    }


def board(items, prs=(), ref_states=None):
    snap = healthy()
    snap["items"] = items
    snap["project"]["totalCount"] = len(items)
    snap["pullRequests"] = list(prs)
    snap["refStates"] = ref_states or {}
    return snap


def ids(snapshot, params=None):
    return [f["id"] for f in R.evaluate(snapshot, params)]


def rules_of(snapshot):
    return sorted({f["rule"] for f in R.evaluate(snapshot)})


class Baseline(unittest.TestCase):
    def test_healthy_board_has_no_findings(self):
        self.assertEqual(ids(healthy()), [])

    def test_evaluation_is_deterministic_and_does_not_mutate_its_input(self):
        snap = healthy()
        snap["items"].append(issue(3, status="Blocked", parent=("FS-GG/.github", 1), comments=[]))
        snap["project"]["totalCount"] = 3
        before = copy.deepcopy(snap)
        self.assertEqual(R.evaluate(snap), R.evaluate(snap))
        self.assertEqual(snap, before)

    def test_finding_ids_carry_no_ages(self):
        snap = healthy()
        snap["items"][1]["status"] = "Blocked"
        first = ids(snap)
        snap["fetchedAt"] = "2026-11-02T12:00:00Z"
        self.assertEqual([i for i in ids(snap) if i.startswith("R3")], [i for i in first if i.startswith("R3")])


class Validation(unittest.TestCase):
    def assertNoVerdict(self, snap):
        with self.assertRaises(R.NoVerdict):
            R.evaluate(snap)

    def test_incomplete_snapshot(self):
        snap = healthy(); snap["complete"] = False
        self.assertNoVerdict(snap)

    def test_count_mismatch(self):
        snap = healthy(); snap["project"]["totalCount"] = 3
        self.assertNoVerdict(snap)

    def test_empty_board(self):
        self.assertNoVerdict(board([]))

    def test_unknown_schema(self):
        snap = healthy(); snap["schema"] = "fsgg:board-drift-snapshot/v1"
        self.assertNoVerdict(snap)

    def test_missing_clock(self):
        snap = healthy(); snap["fetchedAt"] = None
        self.assertNoVerdict(snap)

    def test_cli_check_exits_no_verdict_and_writes_nothing(self):
        with tempfile.TemporaryDirectory() as tmp:
            src, out = Path(tmp) / "s.json", Path(tmp) / "f.json"
            snap = healthy(); snap["complete"] = False
            src.write_text(json.dumps(snap))
            proc = subprocess.run([sys.executable, str(RULES_PATH), "check", "--snapshot", str(src), "--out", str(out)],
                                  capture_output=True, text=True)
            self.assertEqual(proc.returncode, 2)
            self.assertFalse(out.exists())


class R1PullRequestLinkage(unittest.TestCase):
    def test_outcome_only_reference_fires(self):
        snap = healthy(); snap["pullRequests"] = [pr(10, "Refs #1" + ROUTINE)]
        self.assertIn("R1:FS-GG/.github#10:no-delivery", ids(snap))

    def test_reference_off_board_fires(self):
        snap = healthy(); snap["pullRequests"] = [pr(10, "Refs #99" + ROUTINE)]
        self.assertIn("R1:FS-GG/.github#10:no-delivery", ids(snap))

    def test_two_deliveries_fire(self):
        snap = healthy()
        snap["items"].append(issue(3, parent=("FS-GG/.github", 1), comments=[]))
        snap["items"][0]["subIssues"] = {"total": 2, "nodes": [{"repo": "FS-GG/.github", "number": n, "state": "OPEN"} for n in (2, 3)]}
        snap["project"]["totalCount"] = 3
        snap["pullRequests"] = [pr(10, "Refs #2, #3" + ROUTINE)]
        self.assertIn("R1:FS-GG/.github#10:multiple-deliveries", ids(snap))

    def test_one_delivery_plus_its_outcome_passes(self):
        snap = healthy(); snap["pullRequests"] = [pr(10, "Refs #2 and #1" + ROUTINE)]
        self.assertFalse([i for i in ids(snap) if i.startswith("R1")])

    def test_cross_repo_reference_and_closing_keyword_count(self):
        snap = healthy(); snap["pullRequests"] = [pr(10, "Closes FS-GG/.github#2" + ROUTINE, repo="FS-GG/FS.GG.Game")]
        self.assertFalse([i for i in ids(snap) if i.startswith("R1")])

    def test_reference_inside_code_span_is_not_a_reference(self):
        snap = healthy(); snap["pullRequests"] = [pr(10, "`Refs #2`" + ROUTINE)]
        self.assertIn("R1:FS-GG/.github#10:no-delivery", ids(snap))

    def test_closed_or_done_delivery_fires_while_pr_open(self):
        snap = healthy(); snap["items"][1]["state"] = "CLOSED"; snap["items"][1]["status"] = "Done"
        self.assertIn("R1:FS-GG/.github#10:delivery-closed", ids(snap))
        snap = healthy(); snap["items"][1]["status"] = "Done"
        self.assertIn("R1:FS-GG/.github#10:delivery-done", ids(snap))

    def test_closed_delivery_is_fine_for_a_merged_pr(self):
        snap = healthy(); snap["items"][1]["state"] = "CLOSED"; snap["items"][1]["status"] = "Done"
        snap["pullRequests"] = [pr(10, "Refs #2" + ROUTINE, state="MERGED", merged_at=RECENT)]
        self.assertFalse([i for i in ids(snap) if i.startswith("R1")])

    def test_delivery_without_parent_outcome_fires(self):
        snap = healthy(); snap["items"][1]["parent"] = None
        self.assertIn("R1:FS-GG/.github#10:no-parent-outcome", ids(snap))
        snap = healthy(); snap["items"][0]["workLevel"] = "Delivery"
        self.assertIn("R1:FS-GG/.github#10:no-parent-outcome", ids(snap))

    def test_scope_is_routine_nondraft_open_or_recently_merged(self):
        for p in (pr(10, "Refs #1"),  # not routine
                  pr(10, "Refs #1" + ROUTINE, draft=True),
                  pr(10, "Refs #1" + ROUTINE, state="MERGED", merged_at=OLD),
                  pr(10, "Refs #1" + ROUTINE, state="CLOSED")):
            snap = healthy(); snap["pullRequests"] = [p]
            self.assertFalse([i for i in ids(snap) if i.startswith("R1")], p)
        snap = healthy(); snap["pullRequests"] = [pr(10, "Refs #1" + ROUTINE, state="MERGED", merged_at=RECENT)]
        self.assertIn("R1:FS-GG/.github#10:no-delivery", ids(snap))

    def test_unreadable_referenced_card_is_unknown(self):
        snap = healthy(); snap["items"][1]["unreadable"] = True
        found = R.evaluate(snap)
        self.assertIn("U:FS-GG/.github#10:r1-reference-unreadable", [f["id"] for f in found])
        self.assertFalse([f for f in found if f["rule"] == "R1"])


class R2ClosureAndDone(unittest.TestCase):
    def test_closed_not_done_fires(self):
        snap = healthy(); snap["items"][1]["state"] = "CLOSED"; snap["pullRequests"] = []
        self.assertIn("R2:FS-GG/.github#2:closed-not-done", ids(snap))

    def test_done_open_fires(self):
        snap = healthy(); snap["items"][1]["status"] = "Done"; snap["pullRequests"] = []
        self.assertIn("R2:FS-GG/.github#2:done-open", ids(snap))

    def test_closed_done_passes(self):
        snap = healthy(); snap["items"][1].update(state="CLOSED", status="Done", stateReason="COMPLETED"); snap["pullRequests"] = []
        self.assertFalse([i for i in ids(snap) if i.startswith("R2")])

    def _closed_outcome(self, comments, closed_at=RECENT, reason="COMPLETED"):
        o = issue(1, level="Outcome", status="Done", state="CLOSED", reason=reason, closed_at=closed_at, comments=comments)
        return board([o])

    def test_outcome_closed_without_acceptance_marker_fires(self):
        self.assertIn("R2:FS-GG/.github#1:outcome-unaccepted", ids(self._closed_outcome([])))

    def test_acceptance_marker_from_root_passes_but_not_from_others_or_in_code(self):
        ok = self._closed_outcome([comment("<!-- fsgg:outcome-accepted/v1 -->")])
        self.assertFalse([i for i in ids(ok) if i.startswith("R2")])
        other = self._closed_outcome([comment("<!-- fsgg:outcome-accepted/v1 -->", author="someone")])
        self.assertIn("R2:FS-GG/.github#1:outcome-unaccepted", ids(other))
        quoted = self._closed_outcome([comment("`<!-- fsgg:outcome-accepted/v1 -->`")])
        self.assertIn("R2:FS-GG/.github#1:outcome-unaccepted", ids(quoted))

    def test_acceptance_rule_is_not_retroactive_and_ignores_not_planned(self):
        self.assertFalse([i for i in ids(self._closed_outcome([], closed_at=OLD)) if i.startswith("R2")])
        self.assertFalse([i for i in ids(self._closed_outcome([], reason="NOT_PLANNED")) if i.startswith("R2")])

    def test_unread_comments_are_unknown(self):
        found = ids(self._closed_outcome(None))
        self.assertIn("U:FS-GG/.github#1:r2-comments-unread", found)
        self.assertNotIn("R2:FS-GG/.github#1:outcome-unaccepted", found)


class R3Blocked(unittest.TestCase):
    def _blocked(self, **kw):
        snap = healthy(); snap["pullRequests"] = []
        snap["items"][1].update(status="Blocked", **kw)
        return snap

    def test_no_blocker_fires(self):
        self.assertIn("R3:FS-GG/.github#2:no-blocker", ids(self._blocked()))

    def test_open_native_blocker_passes(self):
        snap = self._blocked(blockedBy={"total": 1, "nodes": [{"repo": "FS-GG/x", "number": 5, "state": "OPEN"}]})
        self.assertFalse([i for i in ids(snap) if i.startswith("R3") or i.startswith("U")])

    def test_all_native_blockers_closed_fires(self):
        snap = self._blocked(blockedBy={"total": 1, "nodes": [{"repo": "FS-GG/x", "number": 5, "state": "CLOSED"}]})
        self.assertIn("R3:FS-GG/.github#2:blockers-closed", ids(snap))

    def test_marker_blocker_open_off_board_passes_and_closed_fires(self):
        marker = [comment("<!-- fsgg:blocked-on/v1 FS-GG/x#5 -->")]
        snap = self._blocked(comments=marker); snap["refStates"] = {"FS-GG/x#5": "OPEN"}
        self.assertFalse([i for i in ids(snap) if i.startswith("R3")])
        snap = self._blocked(comments=marker); snap["refStates"] = {"FS-GG/x#5": "CLOSED"}
        self.assertIn("R3:FS-GG/.github#2:blockers-closed", ids(snap))

    def test_newest_marker_wins_and_an_empty_marker_clears(self):
        marker = [comment("<!-- fsgg:blocked-on/v1 FS-GG/x#5 -->", at=OLD, cid=1),
                  comment("<!-- fsgg:blocked-on/v1 -->", at=RECENT, cid=2)]
        snap = self._blocked(comments=marker); snap["refStates"] = {"FS-GG/x#5": "OPEN"}
        self.assertIn("R3:FS-GG/.github#2:no-blocker", ids(snap))

    def test_non_root_marker_is_ignored(self):
        snap = self._blocked(comments=[comment("<!-- fsgg:blocked-on/v1 FS-GG/x#5 -->", author="bot")])
        snap["refStates"] = {"FS-GG/x#5": "OPEN"}
        self.assertIn("R3:FS-GG/.github#2:no-blocker", ids(snap))

    def test_unresolved_marker_blocker_is_unknown(self):
        snap = self._blocked(comments=[comment("<!-- fsgg:blocked-on/v1 FS-GG/x#5 -->")])
        found = ids(snap)
        self.assertIn("U:FS-GG/.github#2:r3-blockers-unread", found)
        self.assertFalse([i for i in found if i.startswith("R3")])

    def test_truncated_native_list_is_unknown_not_closed(self):
        snap = self._blocked(blockedBy={"total": 2, "nodes": [{"repo": "FS-GG/x", "number": 5, "state": "CLOSED"}]})
        self.assertIn("U:FS-GG/.github#2:r3-blockers-unread", ids(snap))

    def test_outcome_blocked_through_a_blocked_child_delivery_passes(self):
        snap = healthy(); snap["pullRequests"] = []
        snap["items"][0]["status"] = "Blocked"
        snap["items"][1].update(status="Blocked", blockedBy={"total": 1, "nodes": [{"repo": "FS-GG/x", "number": 5, "state": "OPEN"}]})
        self.assertFalse([i for i in ids(snap) if i.startswith("R3")])


class R4ActiveEvidence(unittest.TestCase):
    def test_in_progress_with_open_draft_pr_passes(self):
        snap = healthy(); snap["pullRequests"] = [pr(10, "Refs #2" + ROUTINE, draft=True, updated=OLD)]
        snap["items"][1].update(contentUpdatedAt=OLD, statusUpdatedAt=OLD)
        self.assertFalse([i for i in ids(snap) if i.startswith("R4")])

    def test_stale_without_pr_fires_and_recent_activity_passes(self):
        snap = healthy(); snap["pullRequests"] = []
        snap["items"][1].update(contentUpdatedAt=OLD, statusUpdatedAt=OLD)
        self.assertIn("R4:FS-GG/.github#2:stale-active", ids(snap))
        snap["items"][1]["comments"] = [comment("progress", author="anyone", at=RECENT)]
        self.assertFalse([i for i in ids(snap) if i.startswith("R4")])

    def test_in_review_needs_a_non_draft_pr(self):
        snap = healthy(); snap["pullRequests"] = [pr(10, "Refs #2" + ROUTINE, draft=True, updated=OLD)]
        snap["items"][1].update(status="In review", contentUpdatedAt=OLD, statusUpdatedAt=OLD)
        self.assertIn("R4:FS-GG/.github#2:stale-active", ids(snap))

    def test_activity_window_boundary(self):
        snap = healthy(); snap["pullRequests"] = []
        edge = "2026-10-29T12:00:00Z"  # exactly 48h before NOW: not stale
        snap["items"][1].update(contentUpdatedAt=edge, statusUpdatedAt=edge)
        self.assertFalse([i for i in ids(snap) if i.startswith("R4")])
        snap["items"][1].update(contentUpdatedAt="2026-10-29T11:59:59Z", statusUpdatedAt="2026-10-29T11:59:59Z")
        self.assertIn("R4:FS-GG/.github#2:stale-active", ids(snap))

    def test_outcome_in_progress_needs_an_active_child(self):
        snap = healthy(); snap["items"][1]["status"] = "Ready"; snap["pullRequests"] = []
        self.assertIn("R4:FS-GG/.github#1:outcome-no-active-child", ids(snap))

    def test_outcome_in_review_with_child_not_in_review_fires(self):
        snap = healthy(); snap["items"][0]["status"] = "In review"
        self.assertIn("R4:FS-GG/.github#1:outcome-review-open-child", ids(snap))
        snap["items"][1]["status"] = "In review"
        self.assertFalse([i for i in ids(snap) if i.startswith("R4")])


class R5R6OutcomeChildren(unittest.TestCase):
    def test_all_children_closed_fires_r5_not_r6(self):
        snap = healthy(); snap["pullRequests"] = []
        snap["items"][0]["subIssues"]["nodes"][0]["state"] = "CLOSED"
        snap["items"][1].update(state="CLOSED", status="Done")
        found = ids(snap)
        self.assertIn("R5:FS-GG/.github#1:children-complete", found)
        self.assertFalse([i for i in found if i.startswith("R6")])

    def test_closure_proposal_marks_pending_without_silencing(self):
        snap = healthy(); snap["pullRequests"] = []
        snap["items"][0]["subIssues"]["nodes"][0]["state"] = "CLOSED"
        snap["items"][1].update(state="CLOSED", status="Done")
        snap["items"][0]["comments"] = [comment("<!-- fsgg:outcome-closure-proposed/v1 -->")]
        r5 = [f for f in R.evaluate(snap) if f["rule"] == "R5"]
        self.assertEqual(len(r5), 1)
        self.assertEqual(r5[0]["pending"], "closure-proposed")

    def test_outcome_without_children_fires_r6(self):
        o = issue(1, level="Outcome", status="Ready", comments=[])
        self.assertIn("R6:FS-GG/.github#1:no-open-delivery", ids(board([o])))

    def test_open_child_that_is_not_a_board_delivery_fires_r6(self):
        o = issue(1, level="Outcome", status="Ready", comments=[], subs=[("FS-GG/.github", 7, "OPEN")])
        self.assertIn("R6:FS-GG/.github#1:no-open-delivery", ids(board([o])))

    def test_truncated_children_are_unknown(self):
        o = issue(1, level="Outcome", status="Ready", comments=[], subs=[("FS-GG/.github", 7, "CLOSED")], sub_total=150)
        found = ids(board([o]))
        self.assertIn("U:FS-GG/.github#1:r6-children-unread", found)
        self.assertFalse([i for i in found if i.startswith(("R5", "R6"))])

    def test_healthy_outcome_is_silent(self):
        self.assertFalse([i for i in ids(healthy()) if i.startswith(("R5", "R6"))])


class R7Fields(unittest.TestCase):
    def test_missing_fields_fire_with_a_stable_reason(self):
        snap = healthy(); snap["items"][1]["track"] = None
        self.assertIn("R7:FS-GG/.github#2:missing-track", ids(snap))
        snap["items"][1]["status"] = None; snap["items"][1]["workLevel"] = None
        self.assertIn("R7:FS-GG/.github#2:missing-status-worklevel-track", ids(snap))

    def test_draft_card_fires(self):
        snap = healthy()
        snap["items"].append({"itemId": "PVTI_d", "type": "DraftIssue", "title": "x", "status": "Ready",
                              "track": "Follow-up", "workLevel": "Delivery"})
        snap["project"]["totalCount"] = 3
        found = R.evaluate(snap)
        draft = [f for f in found if f["id"] == "R7:draft:PVTI_d:draft"]
        self.assertEqual(len(draft), 1)
        self.assertFalse(draft[0]["public"])

    def test_unreadable_card_is_unknown(self):
        snap = healthy()
        snap["items"].append({"itemId": "PVTI_u", "unreadable": True})
        snap["project"]["totalCount"] = 3
        self.assertIn("U:draft:PVTI_u:unreadable", ids(snap))


class R8DeliveryVsPr(unittest.TestCase):
    def test_open_pr_on_pre_action_status_fires(self):
        for status in ("Backlog", "Ready", "Blocked"):
            snap = healthy(); snap["items"][1]["status"] = status
            snap["items"][1]["blockedBy"] = {"total": 1, "nodes": [{"repo": "FS-GG/x", "number": 5, "state": "OPEN"}]}
            self.assertIn("R8:FS-GG/.github#2:open-pr-pre-action", ids(snap), status)

    def test_draft_pr_does_not_fire(self):
        snap = healthy(); snap["items"][1]["status"] = "Ready"
        snap["pullRequests"] = [pr(10, "Refs #2" + ROUTINE, draft=True)]
        self.assertFalse([i for i in ids(snap) if i.startswith("R8")])

    def test_closed_delivery_with_open_pr_fires(self):
        snap = healthy(); snap["items"][1].update(state="CLOSED", status="Done")
        self.assertIn("R8:FS-GG/.github#2:pr-open-on-closed", ids(snap))


class R9Hierarchy(unittest.TestCase):
    def test_orphan_off_board_and_wrong_parent(self):
        snap = healthy(); snap["pullRequests"] = []; snap["items"][1]["parent"] = None
        self.assertIn("R9:FS-GG/.github#2:orphan-delivery", ids(snap))
        snap["items"][1]["parent"] = {"repo": "FS-GG/.github", "number": 77}
        self.assertIn("R9:FS-GG/.github#2:parent-off-board", ids(snap))
        snap["items"][0]["workLevel"] = "Delivery"; snap["items"][1]["parent"] = {"repo": "FS-GG/.github", "number": 1}
        self.assertIn("R9:FS-GG/.github#2:parent-not-outcome", ids(snap))

    def test_outcome_under_delivery_fires(self):
        snap = healthy(); snap["pullRequests"] = []
        snap["items"].append(issue(3, level="Outcome", status="Ready", parent=("FS-GG/.github", 2), comments=[]))
        snap["project"]["totalCount"] = 3
        self.assertIn("R9:FS-GG/.github#3:outcome-under-delivery", ids(snap))

    def test_closed_orphan_delivery_is_not_a_finding(self):
        snap = healthy(); snap["pullRequests"] = []
        snap["items"][1].update(parent=None, state="CLOSED", status="Done")
        snap["items"][0]["subIssues"]["nodes"][0]["state"] = "CLOSED"
        self.assertFalse([i for i in ids(snap) if i.startswith("R9")])


class Report(unittest.TestCase):
    def _result(self):
        snap = healthy(); snap["pullRequests"] = []
        snap["items"][1]["status"] = "Blocked"
        snap["items"].append(issue(3, repo="FS-GG/Secret", status="Blocked", private=True, parent=("FS-GG/.github", 1), comments=[]))
        snap["items"].append(issue(4, repo="FS-GG/Unknown", status="Blocked", private=None, parent=("FS-GG/.github", 1), comments=[]))
        snap["project"]["totalCount"] = 4
        return R.check(snap)

    def test_counts_cover_everything_but_only_public_subjects_are_named(self):
        result = self._result()
        text = R.render_report(result)
        self.assertEqual(result["counts"]["R3"], 3)
        self.assertIn("R3:FS-GG/.github#2:no-blocker", text)
        self.assertNotIn("Secret", text)
        self.assertNotIn("Unknown#", text)
        self.assertIn("2 finding(s) in private or unknown-visibility repositories", text)

    def test_report_writes_no_link(self):
        text = R.render_report(self._result())
        for line in text.splitlines():
            if line.startswith("- `R"):
                self.assertNotRegex(line.split("`")[2], r"#\d")

    def test_result_records_ruleset_and_params(self):
        result = self._result()
        self.assertEqual(result["ruleset"], "fsgg:board-currency-rules/v2")
        self.assertEqual(result["params"]["activityHours"], 48)


class Fetch(unittest.TestCase):
    """The fetch through a recording `gh` stub: no token, no network."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)
        self.stub = self.dir / "gh"
        self.responses = self.dir / "responses.json"

    def tearDown(self):
        self.tmp.cleanup()

    def run_fetch(self, responses):
        self.responses.write_text(json.dumps(responses))
        self.stub.write_text(
            "#!/usr/bin/env python3\n"
            "import json,sys\n"
            f"rules=json.load(open({str(self.responses)!r}))\n"
            "args=' '.join(sys.argv[1:])\n"
            "for key,(code,out) in rules.items():\n"
            "    if key in args:\n"
            "        sys.stdout.write(out); sys.exit(code)\n"
            "sys.exit(9)\n")
        self.stub.chmod(0o755)
        out = self.dir / "snapshot.json"
        env = dict(os.environ, BOARD_CURRENCY_GH=str(self.stub))
        proc = subprocess.run([sys.executable, str(FETCH_PATH), "--out", str(out)], capture_output=True, text=True, env=env)
        return proc, out

    @staticmethod
    def page(nodes, total=None, has_next=False):
        return json.dumps({"data": {"organization": {"projectV2": {"id": "P", "title": "t", "items": {
            "totalCount": len(nodes) if total is None else total,
            "pageInfo": {"hasNextPage": has_next, "endCursor": "c" if has_next else None}, "nodes": nodes}}}}})

    NODE = {"id": "PVTI_1", "updatedAt": RECENT, "status": {"name": "Ready", "updatedAt": RECENT},
            "track": {"name": "Follow-up"}, "workLevel": {"name": "Delivery"},
            "content": {"__typename": "Issue", "number": 2, "title": "t", "state": "OPEN", "stateReason": None,
                        "updatedAt": RECENT, "closedAt": None,
                        "repository": {"nameWithOwner": "FS-GG/.github", "isPrivate": False},
                        "parent": {"number": 1, "repository": {"nameWithOwner": "FS-GG/.github"}},
                        "subIssues": {"totalCount": 0, "nodes": []}, "blockedBy": {"totalCount": 0, "nodes": []}}}
    EMPTY_SEARCH = json.dumps({"incomplete_results": False, "total_count": 0, "items": []})

    def test_board_is_read_in_pages_of_50_and_refused_beyond_500_items(self):
        # #5119: the board limit is 500 items. Pages stay at 50 (pages of 100 cost more points), and an eleventh page
        # is never requested: a board that still has more is refused, not read partially.
        log = self.dir / "calls.log"
        self.stub.write_text(
            "#!/usr/bin/env python3\n"
            "import json,sys\n"
            f"open({str(log)!r},'a').write(json.dumps(sys.argv[1:])+'\\n')\n"
            f"sys.stdout.write({self.page([self.NODE], total=600, has_next=True)!r})\n")
        self.stub.chmod(0o755)
        out = self.dir / "snapshot.json"
        env = dict(os.environ, BOARD_CURRENCY_GH=str(self.stub))
        proc = subprocess.run([sys.executable, str(FETCH_PATH), "--out", str(out)], capture_output=True, text=True, env=env)
        self.assertNotEqual(proc.returncode, 0)
        self.assertFalse(out.exists())
        calls = [json.loads(line) for line in log.read_text().splitlines()]
        graphql = [call for call in calls if call[:2] == ["api", "graphql"]]
        self.assertEqual(len(graphql), 10)
        self.assertTrue(all(any("items(first:50, after:$cursor)" in arg for arg in call) for call in graphql))

    def test_success_normalizes_hierarchy(self):
        proc, out = self.run_fetch({"graphql": [0, self.page([self.NODE])], "search/issues": [0, self.EMPTY_SEARCH]})
        self.assertEqual(proc.returncode, 0, proc.stderr)
        snap = json.loads(out.read_text())
        item = snap["items"][0]
        self.assertEqual(item["parent"], {"repo": "FS-GG/.github", "number": 1})
        self.assertEqual(item["subIssues"], {"total": 0, "nodes": []})
        self.assertIsNone(item["comments"])  # Ready Delivery needs no marker read
        self.assertEqual(snap["schema"], R.SNAPSHOT_SCHEMA)

    def test_partial_board_writes_nothing(self):
        proc, out = self.run_fetch({"graphql": [0, self.page([self.NODE], total=2)], "search/issues": [0, self.EMPTY_SEARCH]})
        self.assertNotEqual(proc.returncode, 0)
        self.assertFalse(out.exists())

    def test_graphql_errors_write_nothing(self):
        proc, out = self.run_fetch({"graphql": [0, json.dumps({"errors": [{"message": "FS-GG/Secret"}]})]})
        self.assertNotEqual(proc.returncode, 0)
        self.assertFalse(out.exists())
        self.assertNotIn("Secret", proc.stderr)

    def test_incomplete_search_writes_nothing(self):
        bad = json.dumps({"incomplete_results": True, "total_count": 0, "items": []})
        proc, out = self.run_fetch({"graphql": [0, self.page([self.NODE])], "search/issues": [0, bad]})
        self.assertNotEqual(proc.returncode, 0)
        self.assertFalse(out.exists())

    def test_failed_call_writes_nothing_and_names_no_argument(self):
        proc, out = self.run_fetch({"graphql": [1, ""]})
        self.assertNotEqual(proc.returncode, 0)
        self.assertFalse(out.exists())
        self.assertNotIn("FS-GG", proc.stderr)

    def test_truncated_sub_issue_connection_is_kept_visible(self):
        node = copy.deepcopy(self.NODE)
        node["content"]["subIssues"] = {"totalCount": 150, "nodes": [{"number": 3, "state": "OPEN",
                                                                     "repository": {"nameWithOwner": "FS-GG/.github"}}]}
        proc, out = self.run_fetch({"graphql": [0, self.page([node])], "search/issues": [0, self.EMPTY_SEARCH]})
        self.assertEqual(proc.returncode, 0, proc.stderr)
        self.assertEqual(json.loads(out.read_text())["items"][0]["subIssues"]["total"], 150)

    def test_failure_removes_an_earlier_snapshot(self):
        (self.dir / "snapshot.json").write_text("{}")
        proc, out = self.run_fetch({"graphql": [1, ""]})
        self.assertNotEqual(proc.returncode, 0)
        self.assertFalse(out.exists())

    def test_root_comments_are_kept_whole_and_other_long_comments_are_flagged(self):
        node = copy.deepcopy(self.NODE)
        node["status"] = {"name": "Blocked", "updatedAt": RECENT}
        marker = "<!-- fsgg:blocked-on/v1 FS-GG/x#5 -->"
        root_long = json.dumps({"id": 1, "user": {"login": ROOT_LOGIN}, "created_at": RECENT, "body": "x" * 9000 + marker})
        other_long = json.dumps({"id": 2, "user": {"login": "someone"}, "created_at": RECENT, "body": "y" * 9000})
        short = json.dumps({"id": 3, "user": {"login": "someone"}, "created_at": RECENT, "body": "ok"})
        proc, out = self.run_fetch({"graphql": [0, self.page([node])], "search/issues": [0, self.EMPTY_SEARCH],
                                    "/comments": [0, "\n".join((root_long, other_long, short)) + "\n"]})
        self.assertEqual(proc.returncode, 0, proc.stderr)
        comments = json.loads(out.read_text())["items"][0]["comments"]
        self.assertEqual([c["truncated"] for c in comments], [False, True, False])
        self.assertTrue(comments[0]["body"].endswith(marker))
        self.assertEqual(len(comments[1]["body"]), 4000)

    def test_override_bearing_pr_gets_its_editor_and_author_from_graphql(self):
        body = 'x\n<!-- fsgg:board-gate-override/v1 rule=R1 reason="r" -->\n'
        search = json.dumps({"incomplete_results": False, "total_count": 1, "items": [{
            "repository_url": "https://api.github.com/repos/FS-GG/.github", "number": 7, "title": "t",
            "state": "open", "updated_at": RECENT, "body": body, "user": {"login": "searcher"}}]})
        editors = json.dumps({"data": {"p0": {"pullRequest": {"lastEditedAt": RECENT, "editor": {"login": ROOT_LOGIN},
                                                              "author": {"login": ROOT_LOGIN}}}}})
        proc, out = self.run_fetch({"pullRequest(number": [0, editors], "graphql": [0, self.page([self.NODE])],
                                    "is:open": [0, search], "search/issues": [0, self.EMPTY_SEARCH],
                                    "repos/FS-GG/.github": [0, "false"]})
        self.assertEqual(proc.returncode, 0, proc.stderr)
        pr7 = [p for p in json.loads(out.read_text())["pullRequests"] if p["number"] == 7][0]
        self.assertEqual((pr7["author"], pr7["edited"], pr7["editor"]), (ROOT_LOGIN, True, ROOT_LOGIN))

    def test_failed_override_editor_read_fails_the_fetch(self):
        body = 'x\n<!-- fsgg:board-gate-override/v1 rule=R1 reason="r" -->\n'
        search = json.dumps({"incomplete_results": False, "total_count": 1, "items": [{
            "repository_url": "https://api.github.com/repos/FS-GG/.github", "number": 7, "title": "t",
            "state": "open", "updated_at": RECENT, "body": body, "user": {"login": ROOT_LOGIN}}]})
        for editors in ([1, ""], [0, json.dumps({"data": {"p0": {"pullRequest": None}}})]):
            proc, out = self.run_fetch({"pullRequest(number": editors, "graphql": [0, self.page([self.NODE])],
                                        "is:open": [0, search], "search/issues": [0, self.EMPTY_SEARCH]})
            self.assertNotEqual(proc.returncode, 0, editors)
            self.assertFalse(out.exists())

    def test_crlf_override_pr_gets_its_editor_read(self):
        # The fetch must select override PRs with the same normalisation as override_of(), or a CRLF
        # override is never given its editor and is silently not honoured.
        body = 'x\r\n<!-- fsgg:board-gate-override/v1 rule=R1 reason="why" -->\r\n'
        search = json.dumps({"incomplete_results": False, "total_count": 1, "items": [{
            "repository_url": "https://api.github.com/repos/FS-GG/.github", "number": 7, "title": "t",
            "state": "open", "updated_at": RECENT, "body": body, "user": {"login": ROOT_LOGIN}}]})
        editors = json.dumps({"data": {"p0": {"pullRequest": {"lastEditedAt": None, "editor": None,
                                                              "author": {"login": ROOT_LOGIN}}}}})
        proc, out = self.run_fetch({"pullRequest(number": [0, editors], "graphql": [0, self.page([self.NODE])],
                                    "is:open": [0, search], "search/issues": [0, self.EMPTY_SEARCH]})
        self.assertEqual(proc.returncode, 0, proc.stderr)
        pr7 = [p for p in json.loads(out.read_text())["pullRequests"] if p["number"] == 7][0]
        self.assertIs(pr7["edited"], False)
        self.assertIsNotNone(R.override_of(pr7, {"ehotwagner"}))

    def test_fetch_selection_and_override_of_share_one_helper(self):
        text = FETCH_PATH.read_text()
        self.assertIn("rules.override_text(", text)
        self.assertNotIn("rules.visible_text(", text)

    def test_carry_search_is_chunked_by_day(self):
        F = load(FETCH_PATH, "board_currency_fetch_unit")
        calls = []
        F.search_prs = lambda query, merged: calls.append(query) or []
        start = __import__("datetime").datetime(2026, 11, 1, tzinfo=__import__("datetime").timezone.utc)
        end = start + __import__("datetime").timedelta(days=6, hours=5)
        F.carry_search("FS-GG", "M", start, end)
        self.assertEqual(len(calls), 7)
        self.assertTrue(calls[0].endswith("merged:2026-11-01T00:00:00Z..2026-11-02T00:00:00Z"))
        self.assertTrue(calls[-1].endswith("merged:2026-11-07T00:00:00Z..2026-11-07T05:00:00Z"))

    def test_the_fetch_writes_no_board_field(self):
        text = FETCH_PATH.read_text()
        for forbidden in ("mutation", "item-edit", "updateProjectV2", "-X POST", "--method"):
            self.assertNotIn(forbidden, text)


class UnknownLegs(unittest.TestCase):
    """Every rule whose input can be missing says Unknown, never a verdict, and never silently passes."""

    def test_r1_unreadable_parent_is_unknown(self):
        snap = healthy(); snap["items"][0]["unreadable"] = True
        found = R.evaluate(snap)
        self.assertIn("U:FS-GG/.github#10:r1-parent-unreadable", [f["id"] for f in found])
        self.assertFalse([f for f in found if f["rule"] == "R1"])

    def test_r2_truncated_root_comment_without_visible_marker_is_unknown(self):
        o = issue(1, level="Outcome", status="Done", state="CLOSED", reason="COMPLETED", closed_at=RECENT,
                  comments=[dict(comment("long note"), truncated=True)])
        found = ids(board([o]))
        self.assertIn("U:FS-GG/.github#1:r2-comments-unread", found)
        self.assertNotIn("R2:FS-GG/.github#1:outcome-unaccepted", found)
        o["comments"] = [dict(comment("<!-- fsgg:outcome-accepted/v1 --> long"), truncated=True)]
        self.assertEqual(ids(board([o])), [])

    def test_r3_truncation_voids_marker_blockers_but_not_native_ones(self):
        snap = healthy(); snap["pullRequests"] = []
        snap["items"][1].update(status="Blocked", comments=[dict(comment("<!-- fsgg:blocked-on/v1 FS-GG/x#5 -->"), truncated=True)])
        snap["refStates"] = {"FS-GG/x#5": "OPEN"}
        found = ids(snap)
        self.assertIn("U:FS-GG/.github#2:r3-blockers-unread", found)
        self.assertFalse([i for i in found if i.startswith("R3")])
        snap["items"][1]["blockedBy"] = {"total": 1, "nodes": [{"repo": "FS-GG/y", "number": 6, "state": "OPEN"}]}
        self.assertFalse([i for i in ids(snap) if i.startswith(("R3", "U"))])

    def test_r4_unread_comments_are_unknown_without_pr_evidence(self):
        snap = healthy(); snap["pullRequests"] = []
        snap["items"][1].update(comments=None, contentUpdatedAt=RECENT, statusUpdatedAt=RECENT)
        found = ids(snap)
        self.assertIn("U:FS-GG/.github#2:r4-comments-unread", found)
        self.assertFalse([i for i in found if i.startswith("R4")])
        snap = healthy(); snap["items"][1]["comments"] = None  # open PR is evidence on its own
        self.assertFalse([i for i in ids(snap) if i.startswith(("R4", "U"))])

    def test_r5_truncated_closed_children_are_unknown(self):
        o = issue(1, level="Outcome", status="In progress", comments=[], subs=[("FS-GG/.github", 7, "CLOSED")], sub_total=2)
        found = ids(board([o]))
        self.assertIn("U:FS-GG/.github#1:r6-children-unread", found)
        self.assertNotIn("R5:FS-GG/.github#1:children-complete", found)

    def test_r7_unreadable_card_is_unknown_not_missing_fields(self):
        snap = healthy()
        snap["items"].append({"itemId": "PVTI_x", "unreadable": True, "status": None, "track": None, "workLevel": None})
        snap["project"]["totalCount"] = 3
        found = ids(snap)
        self.assertIn("U:draft:PVTI_x:unreadable", found)
        self.assertFalse([i for i in found if i.startswith("R7")])

    def test_r8_unreadable_delivery_is_unknown(self):
        snap = healthy(); snap["items"][1].update(unreadable=True, status="Ready")
        found = ids(snap)
        self.assertIn("U:FS-GG/.github#2:unreadable", found)
        self.assertFalse([i for i in found if i.startswith(("R8", "R1"))])

    def test_r9_unreadable_parent_is_unknown(self):
        snap = healthy(); snap["pullRequests"] = []; snap["items"][0]["unreadable"] = True
        found = ids(snap)
        self.assertIn("U:FS-GG/.github#2:r9-parent-unreadable", found)
        self.assertFalse([i for i in found if i.startswith("R9")])


class StaleOutput(unittest.TestCase):
    def test_check_removes_an_earlier_findings_file_on_no_verdict(self):
        with tempfile.TemporaryDirectory() as tmp:
            src, out = Path(tmp) / "s.json", Path(tmp) / "f.json"
            out.write_text("{\"stale\": true}")
            snap = healthy(); snap["complete"] = False
            src.write_text(json.dumps(snap))
            proc = subprocess.run([sys.executable, str(RULES_PATH), "check", "--snapshot", str(src), "--out", str(out)],
                                  capture_output=True, text=True)
            self.assertEqual(proc.returncode, 2)
            self.assertFalse(out.exists())


OVR = '\n<!-- fsgg:board-gate-override/v1 rule=R1 reason="GitHub outage, Delivery card pending" -->'


class R1Override(unittest.TestCase):
    """Rule-set v2: the exact override grammar and Root authorship (design §1)."""

    ROOTS = {"ehotwagner"}

    def pr(self, body, **kw):
        base = {"repo": "FS-GG/.github", "number": 10, "state": "OPEN", "body": body, "author": ROOT_LOGIN, "edited": False}
        base.update(kw)
        return base

    def test_valid_override(self):
        self.assertEqual(R.override_of(self.pr(OVR), self.ROOTS), "GitHub outage, Delivery card pending")

    def test_grammar_refusals(self):
        bad = [
            OVR + OVR,  # two matches
            '\n<!-- fsgg:board-gate-override/v1 rule=R1 reason="" -->',
            '\n<!-- fsgg:board-gate-override/v1 rule=R1 reason="   " -->',
            '\n<!-- fsgg:board-gate-override/v1 rule=R2 reason="x" -->',
            '\n<!-- fsgg:board-gate-override/v1 rule=R1 reason=x -->',
            '\n  <!-- fsgg:board-gate-override/v1 rule=R1 reason="x" -->',  # not at line start
            '\n`<!-- fsgg:board-gate-override/v1 rule=R1 reason="x" -->`',  # quoted in a code span
            '\n```\n<!-- fsgg:board-gate-override/v1 rule=R1 reason="x" -->\n```\n',  # quoted in a fence
            '\n<!-- fsgg:board-gate-override/v1 rule=R1 reason="' + "x" * 201 + '" -->',
        ]
        for body in bad:
            self.assertIsNone(R.override_of(self.pr(body), self.ROOTS), body)

    def test_crlf_and_cr_line_endings_are_normalised(self):
        self.assertIsNotNone(R.override_of(self.pr(OVR.replace("\n", "\r\n") + "\r\n"), self.ROOTS))
        self.assertIsNotNone(R.override_of(self.pr("x\r" + OVR.lstrip("\n") + "\r"), self.ROOTS))

    def test_authorship(self):
        self.assertIsNone(R.override_of(self.pr(OVR, author="someone"), self.ROOTS))
        self.assertIsNone(R.override_of(self.pr(OVR, edited=True, editor="someone"), self.ROOTS))
        self.assertIsNone(R.override_of(self.pr(OVR, edited=True, editor=None), self.ROOTS))
        self.assertIsNone(R.override_of({k: v for k, v in self.pr(OVR).items() if k != "edited"}, self.ROOTS))
        self.assertIsNotNone(R.override_of(self.pr(OVR, edited=True, editor=ROOT_LOGIN), self.ROOTS))

    def _snap(self, body, **kw):
        snap = healthy()
        p = pr(10, body + ROUTINE, **kw)
        p.update(author=ROOT_LOGIN, edited=False)
        snap["pullRequests"] = [p]
        return snap

    def test_unlinked_override_is_reported_as_overridden(self):
        found = R.evaluate(self._snap("Refs #1" + OVR))
        r1 = [f for f in found if f["rule"] == "R1"]
        self.assertEqual([f["id"] for f in r1], ["R1:FS-GG/.github#10:overridden"])

    def test_linked_pr_with_override_has_no_finding(self):
        self.assertFalse([i for i in ids(self._snap("Refs #2" + OVR)) if i.startswith("R1")])

    def test_override_wins_over_unknown_for_the_observer(self):
        snap = self._snap("Refs #2" + OVR); snap["items"][1]["unreadable"] = True
        found = ids(snap)
        self.assertIn("R1:FS-GG/.github#10:overridden", found)
        self.assertNotIn("U:FS-GG/.github#10:r1-reference-unreadable", found)


class R1Carry(unittest.TestCase):
    """Rule-set v2: merged routine PRs stay under R1 past the 24h window while unlinked."""

    def snap(self, merged_at, body="Refs #1", **params):
        s = healthy()
        p = pr(10, body + ROUTINE, state="MERGED", merged_at=merged_at)
        p.update(author=ROOT_LOGIN, edited=False)
        s["pullRequests"] = [p]
        return s

    P = {"carryFrom": "2026-10-25T00:00:00Z"}

    def test_unlinked_pr_inside_carry_window_is_carried(self):
        found = [f for f in R.evaluate(self.snap("2026-10-27T12:00:00Z"), self.P) if f["rule"] == "R1"]
        self.assertEqual([(f["id"], f.get("carried")) for f in found], [("R1:FS-GG/.github#10:no-delivery", True)])

    def test_carry_window_bounds(self):
        self.assertFalse([f for f in R.evaluate(self.snap("2026-10-24T11:59:59Z"), {"carryFrom": "2026-10-20T00:00:00Z"})
                          if f["rule"] == "R1"])  # older than carryDays
        self.assertFalse([f for f in R.evaluate(self.snap("2026-10-24T23:00:00Z"), self.P) if f["rule"] == "R1"])  # before carryFrom
        self.assertTrue([f for f in R.evaluate(self.snap("2026-10-24T12:00:00Z"), {"carryFrom": "2026-10-20T00:00:00Z"})
                         if f["rule"] == "R1"])  # exactly carryDays old

    def test_default_carry_starts_in_the_future_so_v1_window_holds(self):
        self.assertFalse([f for f in R.evaluate(self.snap("2026-10-27T12:00:00Z")) if f["rule"] == "R1"])

    def test_overridden_pr_is_carried_at_any_age(self):
        found = [f for f in R.evaluate(self.snap("2026-09-01T00:00:00Z", body="Refs #1" + OVR)) if f["rule"] == "R1"]
        self.assertEqual([(f["id"], f.get("carried")) for f in found], [("R1:FS-GG/.github#10:overridden", True)])

    def test_linked_carried_pr_clears(self):
        self.assertFalse([f for f in R.evaluate(self.snap("2026-10-27T12:00:00Z", body="Refs #2"), self.P) if f["rule"] == "R1"])

    def test_duplicate_pr_rows_give_one_finding(self):
        s = self.snap(RECENT)
        s["pullRequests"].append(copy.deepcopy(s["pullRequests"][0]))
        self.assertEqual(len([f for f in R.evaluate(s) if f["rule"] == "R1"]), 1)


class AnonymousUnreadable(unittest.TestCase):
    """An unreadable card with no identity could be any issue, so a lookup miss is Unknown."""

    def snap(self):
        s = healthy()
        s["items"].append({"itemId": "PVTI_anon", "unreadable": True})
        s["project"]["totalCount"] = 3
        return s

    def test_reference_miss_is_unknown(self):
        s = self.snap(); s["pullRequests"] = [pr(10, "Refs #2, #99" + ROUTINE)]
        found = ids(s)
        self.assertIn("U:FS-GG/.github#10:r1-reference-unreadable", found)
        self.assertFalse([i for i in found if i.startswith("R1")])

    def test_r1_parent_miss_is_unknown(self):
        s = self.snap(); s["items"][1]["parent"] = {"repo": "FS-GG/.github", "number": 77}
        found = ids(s)
        self.assertIn("U:FS-GG/.github#10:r1-parent-unreadable", found)
        self.assertNotIn("R1:FS-GG/.github#10:no-parent-outcome", found)

    def test_r9_parent_miss_is_unknown(self):
        s = self.snap(); s["pullRequests"] = []; s["items"][1]["parent"] = {"repo": "FS-GG/.github", "number": 77}
        found = ids(s)
        self.assertIn("U:FS-GG/.github#2:r9-parent-unreadable", found)
        self.assertNotIn("R9:FS-GG/.github#2:parent-off-board", found)

    def test_r6_child_miss_is_unknown(self):
        s = self.snap(); s["pullRequests"] = []
        s["items"][0]["subIssues"] = {"total": 1, "nodes": [{"repo": "FS-GG/.github", "number": 88, "state": "OPEN"}]}
        s["items"][1]["parent"] = None; s["items"][1].update(state="CLOSED", status="Done")
        found = ids(s)
        self.assertIn("U:FS-GG/.github#1:r6-children-unread", found)
        self.assertFalse([i for i in found if i.startswith("R6")])

    def test_without_anonymous_card_a_miss_is_a_verdict(self):
        s = healthy(); s["pullRequests"] = [pr(10, "Refs #99" + ROUTINE)]
        self.assertIn("R1:FS-GG/.github#10:no-delivery", ids(s))

    def test_identified_unreadable_card_does_not_poison_lookups(self):
        s = healthy()
        s["items"].append({"itemId": "PVTI_x", "type": "Issue", "repo": "FS-GG/.github", "number": 50, "unreadable": True})
        s["project"]["totalCount"] = 3
        s["pullRequests"] = [pr(10, "Refs #99" + ROUTINE)]
        self.assertIn("R1:FS-GG/.github#10:no-delivery", ids(s))


class PullRequestCards(unittest.TestCase):
    """A PR card on the board is never a Delivery, as in board-linkage and the merge gate."""

    def test_pr_card_with_delivery_level_does_not_link(self):
        snap = healthy()
        snap["items"].append({"itemId": "PVTI_pr", "type": "PullRequest", "repo": "FS-GG/.github", "number": 30,
                              "state": "OPEN", "status": "In progress", "track": "Follow-up", "workLevel": "Delivery",
                              "repoPrivate": False, "title": "pr card"})
        snap["project"]["totalCount"] = 3
        snap["pullRequests"] = [pr(10, "Refs #30" + ROUTINE)]
        self.assertIn("R1:FS-GG/.github#10:no-delivery", ids(snap))
        snap["pullRequests"] = [pr(10, "Refs #30, #2" + ROUTINE)]
        self.assertFalse([i for i in ids(snap) if i.startswith("R1")])


class LinkageFunction(unittest.TestCase):
    """linkage_of is the one R1 implementation the check and the gate import."""

    def test_linked_returns_the_delivery(self):
        snap = healthy(); board = {f"FS-GG/.github#{i['number']}": i for i in snap["items"]}
        failures, d = R.linkage_of(snap["pullRequests"][0], R.RefIndex(board).get)
        self.assertEqual((failures, d["number"]), ([], 2))

    def test_failure_lists_every_reason(self):
        snap = healthy(); snap["items"][1].update(status="Done", parent=None)
        board = {f"FS-GG/.github#{i['number']}": i for i in snap["items"]}
        failures, _ = R.linkage_of(snap["pullRequests"][0], R.RefIndex(board).get)
        self.assertEqual(failures, [("R1", "delivery-done"), ("R1", "no-parent-outcome")])
class CanonicalReferences(unittest.TestCase):
    """References compare case-insensitively on owner/repo and numerically on N, as the F# reader does."""

    def test_canonical_ref(self):
        self.assertEqual(R.canonical_ref("FS-GG/X#01"), "fs-gg/x#1")
        self.assertEqual(R.canonical_ref(R.ref_key("FS-GG/.github", 5136)), "fs-gg/.github#5136")
        index = R.RefIndex({"FS-GG/x#1": "card"})
        self.assertEqual((index.get("fs-gg/x#001"), "FS-GG/X#1" in index, index["fs-gg/x#1"]), ("card", True, "card"))

    def test_pr_refs_normalise_a_lone_cr(self):
        self.assertEqual(R.pr_refs("FS-GG/x", "`x\rRefs #1`"), {"FS-GG/x#1"})


class TendLinkageConformance(unittest.TestCase):
    """The shared corpus V2TendReadsTests runs against the F# linkage: both must give the same verdict."""

    def test_corpus_agrees_with_linkage_of(self):
        corpus = json.loads((ROOT / "tests/coord-board-v2-projection/tend-linkage-corpus.json").read_text(encoding="utf-8"))
        self.assertGreaterEqual(len(corpus["cases"]), 16)
        for case in corpus["cases"]:
            with self.subTest(case=case["name"]):
                cards = {}
                for ref, card in case["cards"].items():
                    repo, number = ref.split("#")
                    entry = {"workLevel": card.get("workLevel"), "state": card.get("state"), "status": card.get("status")}
                    if card.get("unreadable"):
                        entry["unreadable"] = True
                    if card.get("type"):
                        entry["type"] = card["type"]
                    if card.get("parent"):
                        parent_repo, parent_number = card["parent"].split("#")
                        entry["parent"] = {"repo": parent_repo, "number": int(parent_number)}
                    cards[ref] = entry
                pr = dict(case["pr"])
                failures, delivery = R.linkage_of(pr, R.RefIndex(cards).get,
                                                  anonymous_unreadable=case.get("anonymousUnreadable", False))
                got = failures[0][1] if failures else R.ref_key(delivery_repo(delivery, cards), delivery_number(delivery, cards))
                self.assertEqual(got, case["expect"])


def delivery_repo(delivery, cards):
    return next(ref for ref, card in cards.items() if card is delivery).split("#")[0]


def delivery_number(delivery, cards):
    return int(next(ref for ref, card in cards.items() if card is delivery).split("#")[1])


if __name__ == "__main__":
    unittest.main()
