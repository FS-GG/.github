"""Fixtures for the board-drift observer (Refs #4730).

Every rule (a)-(e) has a positive and a negative test; the report planner is tested for the three
outcomes that matter (leave alone, rewrite, close); fetch and report are driven through a recording
`gh` stub; and the workflow file is read to prove the credential split and the absence of any path
from a failed read to a green "no drift".
"""
from __future__ import annotations

import contextlib
import copy
import re
import datetime as dt
import importlib.util
import io
import json
import os
import stat
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def load(name: str, filename: str):
    spec = importlib.util.spec_from_file_location(name, ROOT / "scripts" / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


OBS = load("board_drift_observer", "board-drift-observer.py")
FETCH = load("board_drift_fetch", "board-drift-fetch.py")

NOW = dt.datetime(2026, 10, 30, 18, 0, tzinfo=dt.timezone.utc)
REPO = "FS-GG/.github"


def hours_ago(h: float) -> str:
    return (NOW - dt.timedelta(hours=h)).strftime("%Y-%m-%dT%H:%M:%SZ")


def item(number=100, status="Ready", state="OPEN", repo=REPO, **kw):
    base = {
        "itemId": f"PVTI_{number}", "status": status, "statusUpdatedAt": hours_ago(48), "track": "Active delivery",
        "workLevel": "Delivery", "itemUpdatedAt": hours_ago(48), "type": "Issue", "title": f"card {number}",
        "repo": repo, "number": number, "state": state, "closedAt": None, "contentUpdatedAt": hours_ago(48),
        "comments": [],
    }
    base.update(kw)
    return base


def pr(number=900, body="Refs #100", repo=REPO, draft=False, updated=1.0, **kw):
    base = {"repo": repo, "number": number, "draft": draft, "state": "open", "updatedAt": hours_ago(updated),
            "url": f"https://github.com/{repo}/pull/{number}", "body": body}
    base.update(kw)
    return base


def comment(cid=1, hours=1.0, body="Root/fdev ruling, 2026-10-30: this unblocks the item.", author="EHotwagner"):
    return {"id": cid, "author": author, "createdAt": hours_ago(hours), "url": f"https://x/{cid}", "body": body}


def snapshot(items, prs=(), **kw):
    doc = {
        "schema": OBS.SNAPSHOT_SCHEMA, "complete": True, "fetchedAt": hours_ago(0),
        "project": {"owner": "FS-GG", "number": 3, "totalCount": len(items)},
        "items": list(items), "pullRequests": list(prs),
    }
    doc.update(kw)
    return doc


def rules(items, prs=(), **kw):
    return [(f["rule"], f["ref"]) for f in OBS.analyze(snapshot(items, prs), NOW, **kw)]


class RuleA(unittest.TestCase):
    def test_positive_blocked_with_fresh_open_pr_that_refs_it(self):
        for status in ("Blocked", "Backlog", "Ready"):
            self.assertEqual(rules([item(status=status)], [pr()]), [("a", f"{REPO}#100")], status)

    def test_positive_cross_repo_refs_form(self):
        other = pr(repo="FS-GG/FS.GG.SDD", body="Refs FS-GG/.github#100")
        self.assertEqual(rules([item()], [other]), [("a", f"{REPO}#100")])

    def test_negative_draft_pr(self):
        self.assertEqual(rules([item()], [pr(draft=True)]), [])

    def test_negative_pr_older_than_24h(self):
        self.assertEqual(rules([item()], [pr(updated=25)]), [])
        self.assertEqual(rules([item()], [pr(updated=25)], pr_window_hours=48), [("a", f"{REPO}#100")])

    def test_negative_card_already_in_progress_or_review(self):
        for status in ("In progress", "In review"):
            self.assertEqual(rules([item(status=status)], [pr()]), [], status)

    def test_negative_bare_ref_in_another_repo_names_that_repo(self):
        self.assertEqual(rules([item()], [pr(repo="FS-GG/FS.GG.SDD", body="Refs #100")]), [])

    def test_negative_ref_in_code_or_without_the_word_refs(self):
        for body in ("see `Refs #100`", "```\nRefs #100\n```", "mentions #100 only"):
            self.assertEqual(rules([item()], [pr(body=body)]), [], body)

    def test_one_finding_per_card_lists_every_pr(self):
        found = OBS.analyze(snapshot([item()], [pr(900), pr(901, body="Refs #100, #7")]), NOW)
        self.assertEqual([f["rule"] for f in found], ["a"])
        self.assertEqual(found[0]["prs"], [f"{REPO}#900", f"{REPO}#901"])


class RuleB(unittest.TestCase):
    def test_positive_closed_not_done(self):
        self.assertEqual(rules([item(state="CLOSED", status="Ready")]), [("b", f"{REPO}#100")])
        self.assertEqual(rules([item(state="MERGED", status="In review", type="PullRequest")]), [("b", f"{REPO}#100")])

    def test_positive_open_done(self):
        self.assertEqual(rules([item(state="OPEN", status="Done")]), [("b", f"{REPO}#100")])

    def test_negative_closed_done_and_open_live(self):
        self.assertEqual(rules([item(state="CLOSED", status="Done"), item(101, state="OPEN", status="Ready")]), [])


class RuleC(unittest.TestCase):
    def idle(self, **kw):
        base = dict(status="In progress", statusUpdatedAt=hours_ago(24 * 8), contentUpdatedAt=hours_ago(24 * 8))
        base.update(kw)
        return item(**base)

    def test_positive_idle_more_than_seven_days(self):
        self.assertEqual(rules([self.idle()]), [("c", f"{REPO}#100")])

    def test_negative_recent_status_issue_comment_or_pr_activity(self):
        self.assertEqual(rules([item(status="In progress", statusUpdatedAt=hours_ago(24 * 6))]), [])
        self.assertEqual(rules([self.idle(comments=[comment(hours=24 * 2, author="someone")])]), [])
        self.assertEqual(rules([self.idle(contentUpdatedAt=hours_ago(5))]), [])
        self.assertEqual(rules([self.idle()], [pr(updated=30)]), [])
        self.assertEqual(rules([self.idle(commits=[{"committedAt": hours_ago(3)}])]), [])

    def test_threshold_is_configurable_and_other_statuses_are_exempt(self):
        self.assertEqual(rules([self.idle()], stale_days=9), [])
        self.assertEqual(rules([item(status="Ready", statusUpdatedAt=hours_ago(24 * 30))]), [])


class RuleD(unittest.TestCase):
    def test_positive_missing_status_track_or_work_level(self):
        self.assertEqual(rules([item(status=None, state="OPEN")]), [("d", f"{REPO}#100")])
        self.assertEqual(rules([item(track=None)]), [("d", f"{REPO}#100")])
        self.assertEqual(rules([item(workLevel=None)]), [("d", f"{REPO}#100")])
        found = OBS.analyze(snapshot([item(status=None, track=None, workLevel=None)]), NOW)
        self.assertEqual(found[0]["missing"], ["Status", "Track", "Work level"])

    def test_negative_complete_card(self):
        self.assertEqual(rules([item()]), [])

    def test_draft_issue_without_status_is_named_by_item_id(self):
        draft = {"itemId": "PVTI_draft", "status": None, "track": "Follow-up", "workLevel": "Delivery",
                 "type": "DraftIssue", "title": "d", "comments": []}
        self.assertEqual(rules([draft]), [("d", "draft:PVTI_draft")])


class RuleE(unittest.TestCase):
    def test_positive_root_ruling_newer_than_status_change(self):
        found = rules([item(status="Blocked", comments=[comment(hours=2)])])
        self.assertEqual(found, [("e", f"{REPO}#100")])

    def test_positive_durable_record_marker(self):
        c = comment(body="**Owner ruling (durable record), 2026-10-30:** dont require second machine")
        self.assertEqual(rules([item(status="Blocked", comments=[c])]), [("e", f"{REPO}#100")])

    def test_negative_comment_older_than_the_status_change(self):
        self.assertEqual(rules([item(status="Blocked", statusUpdatedAt=hours_ago(1), comments=[comment(hours=2)])]), [])

    def test_negative_progress_note_without_a_ruling_marker(self):
        c = comment(body="Root/fdev, 2026-10-30: **PR merged as abc123**, CI green.")
        self.assertEqual(rules([item(status="Blocked", comments=[c])]), [])

    def test_negative_not_a_root_login(self):
        self.assertEqual(rules([item(status="Blocked", comments=[comment(author="stranger")])]), [])

    def test_negative_acknowledged_by_a_later_root_comment(self):
        ack = comment(2, hours=1, body="[board-drift-ack] no Status change is owed; still Blocked on the supplier.")
        self.assertEqual(rules([item(status="Blocked", comments=[comment(1, hours=2), ack])]), [])

    def test_ack_before_the_ruling_does_not_clear_it(self):
        ack = comment(1, hours=3, body="[board-drift-ack] earlier")
        self.assertEqual(rules([item(status="Blocked", comments=[ack, comment(2, hours=2)])]), [("e", f"{REPO}#100")])

    def test_one_finding_per_card_keyed_by_newest_ruling(self):
        found = OBS.analyze(snapshot([item(status="Blocked", comments=[comment(1, hours=3), comment(2, hours=2)])]), NOW)
        self.assertEqual(len(found), 1)
        self.assertTrue(found[0]["key"].endswith("|2"))

    def test_done_cards_are_exempt(self):
        self.assertEqual(rules([item(status="Done", state="CLOSED", comments=[comment()])]), [])

    def test_marker_matches_todays_root_openings_and_not_progress_notes(self):
        hits = ["Root/fdev ruling, 2026-10-30 (owner's standing decision grant): **x**",
                "Root/fdev decision, 2026-10-30, under the user ruling recorded on #3010.",
                "**User ruling (durable record), 2026-10-30, about 14:20 UTC.**",
                "Root/fdev, 2026-10-30: **owner answer.** No screen-reader user evaluation is wanted",
                "Root/fdev, 2026-10-30. **Owner direction: \"prevent the board from going stale again\".**"]
        misses = ["Root/fdev, 2026-10-30: **FS.GG.UI 0.32.4 PUBLISHED and read back.**",
                  "Root/fdev: **registry reconciled.** .github#4952 was merged",
                  "a stray comment that says the ruling was fine"]
        for text in hits:
            self.assertTrue(OBS.RULING_MARKER.search(text), text)
        for text in misses:
            self.assertFalse(OBS.RULING_MARKER.search(text), text)


class Unreadable(unittest.TestCase):
    def test_null_content_is_a_finding_not_silence(self):
        node = {"itemId": "PVTI_x", "status": "Ready", "track": "Follow-up", "workLevel": "Delivery",
                "unreadable": True, "comments": []}
        self.assertEqual(rules([node]), [("x", "draft:PVTI_x")])


class FailClosedAnalysis(unittest.TestCase):
    def run_check(self, doc):
        with tempfile.TemporaryDirectory() as d:
            snap, out = Path(d, "s.json"), Path(d, "f.json")
            snap.write_text(json.dumps(doc))
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                rc = OBS.main(["check", "--snapshot", str(snap), "--out", str(out), "--now", "2026-10-30T18:00:00Z"])
            return rc, out.exists()

    def test_complete_snapshot_is_accepted(self):
        self.assertEqual(self.run_check(snapshot([item()])), (0, True))

    def test_incomplete_miscounted_empty_or_foreign_snapshots_have_no_verdict(self):
        good = snapshot([item()])
        bad = []
        partial = copy.deepcopy(good); partial["complete"] = False; bad.append(partial)
        short = copy.deepcopy(good); short["project"]["totalCount"] = 5; bad.append(short)
        empty = snapshot([]); bad.append(empty)
        foreign = copy.deepcopy(good); foreign["schema"] = "other"; bad.append(foreign)
        for doc in bad:
            self.assertEqual(self.run_check(doc), (OBS.NO_VERDICT, False))

    def test_unreadable_file_has_no_verdict(self):
        with tempfile.TemporaryDirectory() as d:
            with contextlib.redirect_stderr(io.StringIO()):
                rc = OBS.main(["check", "--snapshot", str(Path(d, "missing.json")), "--out", str(Path(d, "o.json"))])
            self.assertEqual(rc, OBS.NO_VERDICT)


class ReportPlan(unittest.TestCase):
    META = {"number": 3, "fetchedAt": "2026-10-30T18:00:00Z", "items": 1}

    def findings(self, *numbers):
        return OBS.analyze(snapshot([item(n, status=None) for n in numbers]), NOW)

    def existing(self, findings, state="open"):
        return {"number": 77, "state": state, "body": OBS.render_body(findings, self.META)}

    def test_creates_when_findings_and_no_issue(self):
        plan = OBS.plan_report(self.findings(1), None, self.META)
        self.assertEqual(plan["action"], "create")
        self.assertIn("Missing Status", plan["body"])

    def test_leaves_the_issue_alone_when_nothing_changed(self):
        f = self.findings(1, 2)
        later = dict(self.META, fetchedAt="2026-10-30T19:00:00Z")
        self.assertEqual(OBS.plan_report(f, self.existing(f), later)["action"], "noop")

    def test_volatile_values_do_not_change_the_set(self):
        a = OBS.analyze(snapshot([item(status="In progress", statusUpdatedAt=hours_ago(24 * 8), contentUpdatedAt=hours_ago(24 * 8))]), NOW)
        b = OBS.analyze(snapshot([item(status="In progress", statusUpdatedAt=hours_ago(24 * 9), contentUpdatedAt=hours_ago(24 * 9))]), NOW)
        self.assertEqual(OBS.digest(a), OBS.digest(b))

    def test_rewrites_and_comments_once_when_the_set_changes(self):
        old, new = self.findings(1, 2), self.findings(2, 3)
        plan = OBS.plan_report(new, self.existing(old), self.META)
        self.assertEqual(plan["action"], "update")
        self.assertIn("1 new, 1 cleared", plan["comment"])
        self.assertIn("d|FS-GG/.github#3|Status", plan["comment"])

    def test_closes_when_findings_are_empty(self):
        plan = OBS.plan_report([], self.existing(self.findings(1)), self.META)
        self.assertEqual(plan["action"], "close")

    def test_empty_with_no_open_issue_does_nothing(self):
        self.assertEqual(OBS.plan_report([], None, self.META)["action"], "noop")
        f = self.findings(1)
        self.assertEqual(OBS.plan_report([], self.existing(f, state="closed"), self.META)["action"], "noop")

    def test_reopens_a_closed_issue_when_drift_returns(self):
        f = self.findings(1)
        self.assertEqual(OBS.plan_report(f, self.existing(f, state="closed"), self.META)["action"], "reopen")

    def test_a_hand_edited_body_without_the_marker_is_rewritten(self):
        f = self.findings(1)
        plan = OBS.plan_report(f, {"number": 77, "state": "open", "body": "edited by hand"}, self.META)
        self.assertEqual(plan["action"], "update")

    def test_report_text_writes_no_reference_mention_or_closing_keyword(self):
        f = OBS.analyze(snapshot([item(status=None, title="fixes #12 and @someone")]), NOW)
        body = OBS.render_body(f, self.META)
        marker, text = body.split("-->", 1)
        self.assertNotIn("#", marker.replace("<!-- fsgg:board-drift/v1", ""))
        outside_code = re.sub(r"`[^`]*`", "", text)
        self.assertIsNone(re.search(r"#\d|@\w", outside_code), outside_code)
        self.assertNotRegex(outside_code, r"(?i)\b(close[sd]?|fix(es|ed)?|resolve[sd]?)\s+(?:[\w./-]*)#\d")


FAKE_GH = r'''#!/usr/bin/env python3
import json, os, sys
state_path = os.environ["FAKE_GH_STATE"]
state = json.load(open(state_path))
args = sys.argv[1:]
state["calls"].append(args)
json.dump(state, open(state_path, "w"))
joined = " ".join(args)
def out(value):
    sys.stdout.write(value if isinstance(value, str) else json.dumps(value))
    sys.exit(0)
fail = state.get("fail", {})
for needle, message in fail.items():
    if needle in joined:
        sys.stderr.write(message)
        sys.exit(1)
if args[:2] == ["api", "graphql"]:
    pages = state["graphql_pages"]
    cursor = None
    for i, a in enumerate(args):
        if a == "-f" and args[i + 1].startswith("cursor="):
            cursor = args[i + 1][7:]
    out(pages[0 if cursor is None else int(cursor)])
if args[0] == "api" and args[1].startswith("search/issues"):
    out(state["search"])
if args[0] == "api" and "/comments?since=" in args[1]:
    key = args[1].split("/issues/")[1].split("/")[0]
    out("\n".join(json.dumps(c) for c in state["comments"].get(key, [])))
if args[0] == "api" and "/issues?labels=" in args[1]:
    out("\n".join(json.dumps(i) for i in state["issues"]))
if args[0] == "api":
    out({"number": 99})
sys.exit(2)
'''


def gh_item(item_id, number, status, status_at, state="OPEN", track="Active delivery", level="Delivery", repo=REPO):
    return {
        "id": item_id, "updatedAt": status_at,
        "status": {"name": status, "updatedAt": status_at} if status else None,
        "track": {"name": track} if track else None, "workLevel": {"name": level} if level else None,
        "content": {"__typename": "Issue", "number": number, "title": f"t{number}", "state": state,
                    "updatedAt": status_at, "closedAt": None, "repository": {"nameWithOwner": repo}},
    }


def gql_page(nodes, total, has_next=False, cursor="1"):
    return json.dumps({"data": {"organization": {"projectV2": {"id": "PVT_x", "title": "Coordination V2", "items": {
        "totalCount": total, "pageInfo": {"hasNextPage": has_next, "endCursor": cursor}, "nodes": nodes}}}}})


class GhStub:
    def __init__(self, test: unittest.TestCase, **state):
        self.dir = tempfile.TemporaryDirectory()
        test.addCleanup(self.dir.cleanup)
        self.path = Path(self.dir.name, "gh")
        self.path.write_text(FAKE_GH)
        self.path.chmod(self.path.stat().st_mode | stat.S_IXUSR)
        self.state_path = Path(self.dir.name, "state.json")
        base = {"calls": [], "graphql_pages": [], "search": json.dumps({"incomplete_results": False, "total_count": 0, "items": []}),
                "comments": {}, "issues": [], "fail": {}}
        base.update(state)
        self.state_path.write_text(json.dumps(base))
        self.env = {"BOARD_DRIFT_GH": str(self.path), "FAKE_GH_STATE": str(self.state_path)}
        test.addCleanup(self.restore, {k: os.environ.get(k) for k in self.env})
        os.environ.update(self.env)

    @staticmethod
    def restore(previous):
        for key, value in previous.items():
            if value is None:
                os.environ.pop(key, None)
            else:
                os.environ[key] = value

    @property
    def calls(self):
        return json.loads(self.state_path.read_text())["calls"]

    def writes(self):
        return [c for c in self.calls if "-X" in c or "--input" in c]


class FetchFailClosed(unittest.TestCase):
    def fetch(self, extra=()):
        with tempfile.TemporaryDirectory() as d:
            out = Path(d, "snapshot.json")
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                rc = FETCH.main(["--out", str(out), *extra])
            return rc, out.exists(), (json.loads(out.read_text()) if out.exists() else None)

    def test_success_builds_a_complete_snapshot_with_comments_since_the_status_change(self):
        nodes = [gh_item("I1", 100, "Blocked", hours_ago(5)), gh_item("I2", 101, "Done", hours_ago(5), state="CLOSED")]
        search = json.dumps({"incomplete_results": False, "total_count": 1, "items": [
            {"repository_url": "https://api.github.com/repos/FS-GG/.github", "number": 900, "draft": False, "state": "open",
             "updated_at": hours_ago(1), "html_url": "u", "body": "Refs #100"}]})
        stub = GhStub(self, graphql_pages=[gql_page(nodes, 2)], search=search,
                      comments={"100": [{"id": 5, "user": {"login": "EHotwagner"}, "created_at": hours_ago(1), "html_url": "c", "body": "x"}]})
        rc, exists, snap = self.fetch()
        self.assertEqual((rc, exists), (0, True))
        self.assertTrue(snap["complete"])
        self.assertEqual(snap["project"]["totalCount"], 2)
        self.assertEqual(snap["items"][0]["comments"][0]["author"], "EHotwagner")
        self.assertEqual(snap["items"][1]["comments"], [])  # Done: no comment read
        self.assertEqual(snap["pullRequests"][0]["repo"], "FS-GG/.github")
        reads = [c for c in stub.calls if "/comments?since=" in c[1]]
        self.assertEqual(len(reads), 1)
        self.assertTrue(stub.writes() == [])
        # and the analysis accepts it end to end
        self.assertEqual([f["rule"] for f in OBS.analyze(snap, dt.datetime.now(dt.timezone.utc))], ["a"])

    def test_a_failed_graphql_call_writes_no_snapshot(self):
        GhStub(self, graphql_pages=[], fail={"graphql": "HTTP 502"})
        self.assertEqual(self.fetch()[:2], (1, False))

    def test_graphql_errors_in_a_200_response_write_no_snapshot(self):
        GhStub(self, graphql_pages=[json.dumps({"errors": [{"message": "RATE_LIMITED"}]})])
        self.assertEqual(self.fetch()[:2], (1, False))

    def test_a_null_project_means_scope_or_number_is_wrong_and_writes_nothing(self):
        GhStub(self, graphql_pages=[json.dumps({"data": {"organization": {"projectV2": None}}})])
        self.assertEqual(self.fetch()[:2], (1, False))

    def test_a_partial_page_set_is_refused(self):
        nodes = [gh_item("I1", 100, "Ready", hours_ago(5))]
        GhStub(self, graphql_pages=[gql_page(nodes, 3, has_next=True, cursor="0")])
        self.assertEqual(self.fetch(["--max-pages", "1"])[:2], (1, False))

    def test_a_count_mismatch_is_refused(self):
        GhStub(self, graphql_pages=[gql_page([gh_item("I1", 100, "Ready", hours_ago(5))], 2)])
        self.assertEqual(self.fetch()[:2], (1, False))

    def test_incomplete_search_results_are_refused(self):
        GhStub(self, graphql_pages=[gql_page([gh_item("I1", 100, "Ready", hours_ago(5))], 1)],
               search=json.dumps({"incomplete_results": True, "total_count": 0, "items": []}))
        self.assertEqual(self.fetch()[:2], (1, False))

    def test_a_failed_comment_read_is_refused(self):
        GhStub(self, graphql_pages=[gql_page([gh_item("I1", 100, "Ready", hours_ago(5))], 1)], fail={"/comments?since=": "HTTP 403"})
        self.assertEqual(self.fetch()[:2], (1, False))

    def test_null_content_is_kept_as_an_unreadable_item(self):
        node = gh_item("I1", 100, "Ready", hours_ago(5)); node["content"] = None
        GhStub(self, graphql_pages=[gql_page([node], 1)])
        rc, exists, snap = self.fetch()
        self.assertEqual((rc, exists), (0, True))
        self.assertTrue(snap["items"][0]["unreadable"])


class ReportApply(unittest.TestCase):
    def run_pipeline(self, nodes, issues, prs=(), extra=()):
        """fetch -> check -> report against the stub; returns (stub, rc of each stage)."""
        stub = GhStub(self, graphql_pages=[gql_page(nodes, len(nodes))], issues=list(issues),
                      search=json.dumps({"incomplete_results": False, "total_count": len(prs), "items": list(prs)}))
        d = tempfile.mkdtemp()
        self.addCleanup(lambda: __import__("shutil").rmtree(d, ignore_errors=True))
        snap, found = str(Path(d, "s.json")), str(Path(d, "f.json"))
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            rcs = [FETCH.main(["--out", snap]), OBS.main(["check", "--snapshot", snap, "--out", found]),
                   OBS.main(["report", "--findings", found, *extra])]
        return stub, rcs

    def test_creates_the_single_labelled_issue_then_leaves_it_alone(self):
        nodes = [gh_item("I1", 100, None, hours_ago(5))]
        stub, rcs = self.run_pipeline(nodes, [])
        self.assertEqual(rcs, [0, 0, 0])
        creates = [c for c in stub.writes() if c[1].endswith("/issues")]
        self.assertEqual(len(creates), 1)
        body = json.loads(json.dumps(creates[0]))  # recorded args only; the payload is on stdin
        existing = {"number": 7, "state": "open", "title": OBS.REPORT_TITLE, "labels": [{"name": OBS.REPORT_LABEL}],
                    "body": OBS.render_body(OBS.analyze(snapshot([item(100, status=None, title="t100")]), NOW),
                                            {"number": 3, "fetchedAt": "x", "items": 1})}
        stub2, rcs2 = self.run_pipeline(nodes, [existing])
        self.assertEqual(rcs2, [0, 0, 0])
        self.assertEqual(stub2.writes(), [], "an unchanged finding set must not touch the issue")

    def test_closes_the_open_issue_when_findings_are_empty(self):
        nodes = [gh_item("I1", 100, "Ready", hours_ago(5))]
        existing = {"number": 7, "state": "open", "title": OBS.REPORT_TITLE, "body": "old"}
        stub, rcs = self.run_pipeline(nodes, [existing])
        self.assertEqual(rcs, [0, 0, 0])
        joined = [" ".join(c) for c in stub.writes()]
        self.assertTrue(any("issues/7/comments" in c for c in joined))
        self.assertTrue(any("issues/7 -X PATCH" in c for c in joined))

    def test_dry_run_prints_the_plan_and_writes_nothing(self):
        nodes = [gh_item("I1", 100, None, hours_ago(5))]
        stub, rcs = self.run_pipeline(nodes, [], extra=["--dry-run"])
        self.assertEqual(rcs, [0, 0, 0])
        self.assertEqual(stub.writes(), [])

    def test_two_open_report_issues_refuse_to_guess(self):
        nodes = [gh_item("I1", 100, None, hours_ago(5))]
        dup = [{"number": n, "state": "open", "title": OBS.REPORT_TITLE, "body": ""} for n in (7, 8)]
        stub, rcs = self.run_pipeline(nodes, dup)
        self.assertEqual(rcs, [0, 0, 1])
        self.assertEqual(stub.writes(), [])

    def test_a_failed_read_never_reaches_the_report_and_never_reports_no_drift(self):
        stub = GhStub(self, graphql_pages=[], fail={"graphql": "HTTP 502"},
                      issues=[{"number": 7, "state": "open", "title": OBS.REPORT_TITLE, "body": "old"}])
        with tempfile.TemporaryDirectory() as d:
            snap, found = str(Path(d, "s.json")), str(Path(d, "f.json"))
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                fetch_rc = FETCH.main(["--out", snap])
                check_rc = OBS.main(["check", "--snapshot", snap, "--out", found])
            self.assertNotEqual(fetch_rc, 0)
            self.assertEqual(check_rc, OBS.NO_VERDICT)
            self.assertFalse(Path(found).exists())
        self.assertEqual(stub.writes(), [])


class WorkflowShape(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        import yaml
        cls.doc = yaml.safe_load((ROOT / ".github/workflows/board-drift-observer.yml").read_text())
        cls.text = (ROOT / ".github/workflows/board-drift-observer.yml").read_text()
        cls.triggers = cls.doc.get(True) or cls.doc.get("on")

    def test_scheduled_hourly_and_dispatchable(self):
        self.assertEqual(self.triggers["schedule"][0]["cron"].split()[1:], ["*", "*", "*", "*"])
        self.assertIn("workflow_dispatch", self.triggers)

    def test_every_job_is_bounded_and_every_action_is_sha_pinned(self):
        import re
        for name, job in self.doc["jobs"].items():
            self.assertIsInstance(job["timeout-minutes"], int, name)
            self.assertIn("permissions", job, name)
        for uses in re.findall(r"uses:\s*(\S+)", self.text):
            self.assertRegex(uses, r"@[0-9a-f]{40}$", uses)

    def test_credentials_are_split_and_the_app_token_has_no_write_scope(self):
        observe, report = self.doc["jobs"]["board-drift-read"], self.doc["jobs"]["board-drift-report"]
        self.assertNotIn("issues", [k for k, v in observe["permissions"].items() if v == "write"])
        self.assertEqual(report["permissions"], {"contents": "read", "issues": "write"})
        mint = [s for s in observe["steps"] if "create-github-app-token" in s.get("uses", "")]
        self.assertEqual(len(mint), 1)
        scopes = {k: v for k, v in mint[0]["with"].items() if k.startswith("permission-")}
        self.assertEqual(scopes, {"permission-organization-projects": "read", "permission-issues": "read",
                                  "permission-pull-requests": "read"})
        self.assertFalse(any("create-github-app-token" in s.get("uses", "") for s in report["steps"]))
        self.assertNotIn("secrets.FSGG", json.dumps(report))

    def test_report_runs_only_after_a_successful_observation(self):
        report = self.doc["jobs"]["board-drift-report"]
        self.assertEqual(report["needs"], "board-drift-read")
        self.assertNotIn("if", report)
        self.assertNotIn("always()", self.text.replace("# ", "#").split("jobs:", 1)[1].replace("`always()`", ""))
        self.assertNotIn("continue-on-error", self.text.split("jobs:", 1)[1])

    def test_shell_steps_fail_fast_and_the_fetch_precedes_the_check(self):
        steps = self.doc["jobs"]["board-drift-read"]["steps"]
        for step in steps + self.doc["jobs"]["board-drift-report"]["steps"]:
            if "run" in step:
                self.assertIn("set -euo pipefail", step["run"], step.get("name"))
        names = [s.get("name", "") for s in steps]
        self.assertLess(names.index("Read Project 3 (read-only)"), names.index("Evaluate the drift rules"))

    def test_the_workflow_writes_no_board_field(self):
        for forbidden in ("item-edit", "updateProjectV2", "mutation", "set-field", "projects: write", "permission-organization-projects: write"):
            self.assertNotIn(forbidden, self.text)
            self.assertNotIn(forbidden, (ROOT / "scripts/board-drift-fetch.py").read_text().replace("never writes a board field", ""))


if __name__ == "__main__":
    unittest.main()
