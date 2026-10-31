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

TEST_KEY = "unit-test-hmac-key-0123456789abcdef"
os.environ["BOARD_DRIFT_HMAC_KEY"] = TEST_KEY


def analyze(snap, now, **kw):
    """OBS.analyze with the placeholder key supplied, as the workflow's check step does."""
    kw.setdefault("hmac_key", TEST_KEY.encode())
    return OBS.analyze(snap, now, **kw)


NOW = dt.datetime(2026, 10, 30, 18, 0, tzinfo=dt.timezone.utc)
REPO = "FS-GG/.github"


def hours_ago(h: float) -> str:
    return (NOW - dt.timedelta(hours=h)).strftime("%Y-%m-%dT%H:%M:%SZ")


def item(number=100, status="Ready", state="OPEN", repo=REPO, **kw):
    base = {
        "itemId": f"PVTI_{number}", "status": status, "statusUpdatedAt": hours_ago(48), "track": "Active delivery",
        "workLevel": "Delivery", "itemUpdatedAt": hours_ago(48), "type": "Issue", "title": f"card {number}",
        "repo": repo, "repoPrivate": False, "number": number, "state": state, "closedAt": None, "contentUpdatedAt": hours_ago(48),
        "comments": [],
    }
    base.update(kw)
    return base


def pr(number=900, body="Refs #100", repo=REPO, draft=False, updated=1.0, **kw):
    base = {"repo": repo, "repoPrivate": False, "title": "feat: change", "number": number, "draft": draft, "state": "open", "updatedAt": hours_ago(updated),
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
    return [(f["rule"], f["ref"]) for f in analyze(snapshot(items, prs), NOW, **kw)]


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
        found = analyze(snapshot([item()], [pr(900), pr(901, body="Refs #100, #7")]), NOW)
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
        found = analyze(snapshot([item(status=None, track=None, workLevel=None)]), NOW)
        self.assertEqual(found[0]["missing"], ["Status", "Track", "Work level"])

    def test_negative_complete_card(self):
        self.assertEqual(rules([item()]), [])

    def test_draft_issue_without_status_is_named_by_item_id(self):
        draft = {"itemId": "PVTI_draft", "status": None, "track": "Follow-up", "workLevel": "Delivery",
                 "type": "DraftIssue", "title": "d", "comments": []}
        found = rules([draft])
        self.assertEqual([r for r, _ in found], ["d"])
        self.assertTrue(found[0][1].startswith("private:"), "a draft card is never published by name")
        self.assertEqual(rules([draft], reveal_private=True), [("d", "draft:PVTI_draft")])


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
        ack = comment(2, hours=1, body="[board-drift-ack] e:* no Status change is owed; still Blocked on the supplier.")
        self.assertEqual(rules([item(status="Blocked", comments=[comment(1, hours=2), ack])]), [])

    def test_ack_before_the_ruling_does_not_clear_it(self):
        ack = comment(1, hours=3, body="[board-drift-ack] e:* earlier")
        self.assertEqual(rules([item(status="Blocked", comments=[ack, comment(2, hours=2)])]), [("e", f"{REPO}#100")])

    def test_one_finding_per_card_keyed_by_newest_ruling(self):
        found = analyze(snapshot([item(status="Blocked", comments=[comment(1, hours=3), comment(2, hours=2)])]), NOW)
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
        found = rules([node])
        self.assertEqual([r for r, _ in found], ["x"])
        self.assertTrue(found[0][1].startswith("private:"))
        self.assertEqual(rules([node], reveal_private=True), [("x", "draft:PVTI_x")])


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
        return analyze(snapshot([item(n, status=None) for n in numbers]), NOW)

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
        a = analyze(snapshot([item(status="In progress", statusUpdatedAt=hours_ago(24 * 8), contentUpdatedAt=hours_ago(24 * 8))]), NOW)
        b = analyze(snapshot([item(status="In progress", statusUpdatedAt=hours_ago(24 * 9), contentUpdatedAt=hours_ago(24 * 9))]), NOW)
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

    def test_does_not_reopen_an_issue_root_closed_while_the_digest_is_unchanged(self):
        f = self.findings(1)
        plan = OBS.plan_report(f, self.existing(f, state="closed"), self.META)
        self.assertEqual(plan["action"], "noop")
        self.assertIn("not reopened", plan["reason"])

    def test_reopens_a_closed_issue_only_when_the_digest_changes(self):
        plan = OBS.plan_report(self.findings(1, 2), self.existing(self.findings(1), state="closed"), self.META)
        self.assertEqual(plan["action"], "reopen")

    def test_closing_for_empty_findings_records_the_empty_set_so_recurrence_reopens(self):
        f = self.findings(1)
        close = OBS.plan_report([], self.existing(f), self.META)
        self.assertEqual(close["action"], "close")
        closed_by_us = {"number": 77, "state": "closed", "body": close["body"]}
        self.assertEqual(OBS.parse_marker(close["body"])["digest"], OBS.digest([]))
        self.assertEqual(OBS.plan_report(f, closed_by_us, self.META)["action"], "reopen")

    def test_a_hand_edited_body_without_the_marker_is_rewritten(self):
        f = self.findings(1)
        plan = OBS.plan_report(f, {"number": 77, "state": "open", "body": "edited by hand"}, self.META)
        self.assertEqual(plan["action"], "update")

    def test_report_text_writes_no_reference_mention_or_closing_keyword(self):
        f = analyze(snapshot([item(status=None, title="fixes #12 and @someone")]), NOW)
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
    if "search_pages" in state:
        import re
        page = int(re.search(r"&page=(\d+)", args[1]).group(1))
        out(state["search_pages"][min(page, len(state["search_pages"])) - 1])
    out(state["search"])
if args[0] == "api" and args[1].startswith("repos/") and args[1].count("/") == 2 and "--jq" in args:
    visibility = state.get("visibility", {}).get(args[1][len("repos/"):])
    if visibility == "error":
        sys.stderr.write("HTTP 404")
        sys.exit(1)
    out(json.dumps(visibility))
if args[0] == "api" and "/comments?since=" in args[1]:
    key = args[1].split("/issues/")[1].split("/")[0]
    out("\n".join(json.dumps(c) for c in state["comments"].get(key, [])))
if args[0] == "api" and "/issues?labels=" in args[1]:
    out("\n".join(json.dumps(i) for i in state["issues"]))
if args[0] == "api":
    out({"number": 99})
sys.exit(2)
'''


def gh_item(item_id, number, status, status_at, state="OPEN", track="Active delivery", level="Delivery", repo=REPO,
            private=False):
    return {
        "id": item_id, "updatedAt": status_at,
        "status": {"name": status, "updatedAt": status_at} if status else None,
        "track": {"name": track} if track else None, "workLevel": {"name": level} if level else None,
        "content": {"__typename": "Issue", "number": number, "title": f"t{number}", "state": state,
                    "updatedAt": status_at, "closedAt": None, "repository": {"nameWithOwner": repo, "isPrivate": private}},
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


class FetchHelper:
    def fetch(self, extra=()):
        with tempfile.TemporaryDirectory() as d:
            out = Path(d, "snapshot.json")
            err = io.StringIO()
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(err):
                rc = FETCH.main(["--out", str(out), *extra])
            self.stderr = err.getvalue()
            return rc, out.exists(), (json.loads(out.read_text()) if out.exists() else None)


class FetchFailClosed(FetchHelper, unittest.TestCase):

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
        # and the analysis accepts it end to end, at the fixtures' own frozen clock: the PR above changed
        # hours_ago(...) before NOW, so the wall clock would push it out of the 24h window a day later.
        self.assertEqual([f["rule"] for f in analyze(snap, NOW)], ["a"])

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


class FetchMutantsAndVisibility(FetchHelper, unittest.TestCase):
    """Kills the surviving mutants (PR count below total, total over the cap, errors beside partial data)
    and proves visibility is fetched, with unknown kept unknown."""

    def search_doc(self, total, items):
        return json.dumps({"incomplete_results": False, "total_count": total, "items": items})

    @staticmethod
    def raw_pr(n, repo="FS-GG/.github", title="t"):
        return {"repository_url": f"https://api.github.com/repos/{repo}", "number": n, "draft": False, "state": "open",
                "updated_at": hours_ago(1), "html_url": "u", "body": f"Refs #{n}", "title": title}

    def board(self):
        return [gql_page([gh_item("I1", 100, "Ready", hours_ago(5))], 1)]

    def test_a_pr_count_below_total_count_is_refused(self):
        GhStub(self, graphql_pages=self.board(),
               search_pages=[self.search_doc(2, [self.raw_pr(1)]), self.search_doc(2, [])])
        self.assertEqual(self.fetch()[:2], (1, False))

    def test_a_search_total_over_the_1000_cap_is_refused(self):
        GhStub(self, graphql_pages=self.board(), search=self.search_doc(1001, [self.raw_pr(1)]))
        self.assertEqual(self.fetch()[:2], (1, False))
        self.assertIn("cap is 1000", self.stderr)  # refused by the cap itself, not by the later count check
        GhStub(self, graphql_pages=self.board(), search=self.search_doc(1000, [self.raw_pr(1)]))
        self.fetch()
        self.assertNotIn("cap is 1000", self.stderr)  # exactly 1000 is within the cap

    def test_errors_beside_partial_data_are_refused(self):
        page = json.loads(self.board()[0])
        page["errors"] = [{"message": "partial: some nodes resolved to null"}]
        GhStub(self, graphql_pages=[json.dumps(page)])
        self.assertEqual(self.fetch()[:2], (1, False))

    def test_a_search_that_pages_collects_every_pr(self):
        GhStub(self, graphql_pages=self.board(),
               search_pages=[self.search_doc(2, [self.raw_pr(1)]), self.search_doc(2, [self.raw_pr(2)])])
        rc, exists, snap = self.fetch()
        self.assertEqual((rc, exists, [p["number"] for p in snap["pullRequests"]]), (0, True, [1, 2]))

    def test_repository_visibility_and_pr_titles_reach_the_snapshot(self):
        nodes = [gh_item("I1", 100, "Ready", hours_ago(5), private=True, repo="FS-GG/Secret"),
                 gh_item("I2", 101, "Ready", hours_ago(5))]
        prs = [self.raw_pr(1, "FS-GG/Secret", "docs: x"), self.raw_pr(2, "FS-GG/.github"), self.raw_pr(3, "FS-GG/Other"),
               self.raw_pr(4, "FS-GG/Broken")]
        stub = GhStub(self, graphql_pages=[gql_page(nodes, 2)], search=self.search_doc(4, prs),
                      visibility={"FS-GG/Other": True, "FS-GG/Broken": "error"})
        rc, exists, snap = self.fetch()
        self.assertEqual((rc, exists), (0, True))
        self.assertEqual([i["repoPrivate"] for i in snap["items"]], [True, False])
        by = {p["number"]: p for p in snap["pullRequests"]}
        self.assertEqual((by[1]["repoPrivate"], by[2]["repoPrivate"], by[3]["repoPrivate"], by[4]["repoPrivate"]),
                         (True, False, True, None))
        self.assertEqual(by[1]["title"], "docs: x")
        # visibility came from the board where known: only the two unknown repositories were read
        reads = [c[1] for c in stub.calls if c[1].startswith("repos/") and c[1].count("/") == 2]
        self.assertEqual(sorted(reads), ["repos/FS-GG/Broken", "repos/FS-GG/Other"])

    def test_a_failed_private_comments_read_publishes_no_repo_number_or_response_text(self):
        node = gh_item("I1", 4242, "Blocked", hours_ago(5), private=True, repo="FS-GG/Secret.Repo")
        leak = "HTTP 403 repos/FS-GG/Secret.Repo/issues/4242 SECRET-RESPONSE-BODY"
        GhStub(self, graphql_pages=[gql_page([node], 1)], fail={"/comments?since=": leak})
        self.assertEqual(self.fetch()[:2], (1, False))
        for secret in ("Secret.Repo", "4242", "SECRET-RESPONSE-BODY", "403", "repos/", "issues"):
            self.assertNotIn(secret, self.stderr)
        self.assertIn("comments call failed (exit 1)", self.stderr)

    def test_failed_graphql_and_search_calls_name_only_a_label_and_exit_code(self):
        GhStub(self, graphql_pages=[], fail={"graphql": "HTTP 502 organization(login: FS-GG) SECRET-RESPONSE-BODY"})
        self.assertEqual(self.fetch()[:2], (1, False))
        self.assertIn("graphql call failed (exit 1)", self.stderr)
        self.assertNotIn("SECRET-RESPONSE-BODY", self.stderr)
        self.assertNotIn("FS-GG", self.stderr)
        GhStub(self, graphql_pages=self.board(), fail={"search/issues": "HTTP 422 org:FS-GG SECRET-RESPONSE-BODY"})
        self.assertEqual(self.fetch()[:2], (1, False))
        self.assertIn("search call failed (exit 1)", self.stderr)
        self.assertNotIn("SECRET-RESPONSE-BODY", self.stderr)

    def test_graphql_error_text_and_unexpected_exceptions_are_not_echoed(self):
        errors = json.dumps({"errors": [{"message": "Could not resolve FS-GG/Secret.Repo#4242 SECRET-RESPONSE-BODY"}]})
        GhStub(self, graphql_pages=[errors])
        self.assertEqual(self.fetch()[:2], (1, False))
        for secret in ("Secret.Repo", "4242", "SECRET-RESPONSE-BODY"):
            self.assertNotIn(secret, self.stderr)
        bad = json.loads(self.board()[0])
        bad["data"]["organization"]["projectV2"]["items"]["nodes"] = ["SECRET-RESPONSE-BODY"]
        GhStub(self, graphql_pages=[json.dumps(bad)])
        self.assertEqual(self.fetch()[:2], (1, False))
        self.assertNotIn("SECRET-RESPONSE-BODY", self.stderr)
        self.assertIn("unexpected", self.stderr)

    def test_the_observer_report_error_also_names_no_response_text(self):
        GhStub(self, issues=[], fail={"issues?labels=": "HTTP 403 SECRET-RESPONSE-BODY"})
        err = io.StringIO()
        with tempfile.TemporaryDirectory() as d, contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(err):
            f = Path(d, "f.json")
            f.write_text(json.dumps({"schema": OBS.FINDINGS_SCHEMA, "meta": {}, "findings": [OBS.finding("d", item(), "Track", "t")]}))
            rc = OBS.main(["report", "--findings", str(f)])
        self.assertEqual(rc, 1)
        self.assertNotIn("SECRET-RESPONSE-BODY", err.getvalue())

    def test_a_non_boolean_is_private_value_is_stored_as_unknown(self):
        nodes = [gh_item("I1", 100, "Ready", hours_ago(5))]
        for value in ("false", 1, "", None):
            nodes[0]["content"]["repository"]["isPrivate"] = value
            GhStub(self, graphql_pages=[gql_page(nodes, 1)])
            self.assertIsNone(self.fetch()[2]["items"][0]["repoPrivate"], repr(value))

    def test_closed_done_complete_cards_get_no_comment_read_but_incomplete_ones_do(self):
        nodes = [gh_item("I1", 100, "Done", hours_ago(5), state="CLOSED"),
                 gh_item("I2", 101, "Done", hours_ago(5), state="CLOSED", track=None)]
        stub = GhStub(self, graphql_pages=[gql_page(nodes, 2)])
        self.assertEqual(self.fetch()[:2], (0, True))
        reads = [c[1] for c in stub.calls if "/comments?since=" in c[1]]
        self.assertEqual(len(reads), 1)
        self.assertIn("/issues/101/", reads[0])


SECRET_REPO, SECRET_TITLE, SECRET_PR_TITLE = "FS-GG/Secret.Repo", "SECRET-CARD-TITLE", "SECRET-PR-TITLE"


def private_board(visibility):
    """Every rule fires on a card in a repo whose visibility is `visibility` (False public, True, or absent)."""
    def card(number, **kw):
        base = dict(repo=SECRET_REPO, title=SECRET_TITLE, **kw)
        if visibility != "absent":
            base["repoPrivate"] = visibility
        else:
            base["repoPrivate"] = None
        return item(number, **base)
    items = [
        card(100, status="Blocked"),                                              # a
        card(101, status="Ready", state="CLOSED"),                                # b
        card(102, status="In progress", statusUpdatedAt=hours_ago(24 * 9), contentUpdatedAt=hours_ago(24 * 9)),  # c
        card(103, track=None),                                                    # d
        card(104, status="Blocked", comments=[comment(55)]),                      # e
        card(105, unreadable=True),                                               # x
    ]
    items[5].pop("repo"); items[5].pop("number")
    prs = [pr(900, body=f"Refs {SECRET_REPO}#100", repo=SECRET_REPO, title=SECRET_PR_TITLE, repoPrivate=visibility if visibility != "absent" else None)]
    return snapshot(items, prs)


class Privacy(unittest.TestCase):
    """.github is public; Project 3 holds private cards. Nothing private may reach findings, body, logs."""

    def published(self, snap, **kw):
        found = analyze(snap, NOW, **kw)
        with tempfile.TemporaryDirectory() as d:
            sp, out = Path(d, "s.json"), Path(d, "f.json")
            sp.write_text(json.dumps(snap))
            buf = io.StringIO()
            with contextlib.redirect_stdout(buf):
                OBS.main(["check", "--snapshot", str(sp), "--out", str(out), "--now", iso_now()])
            artifact = out.read_text()
        body = OBS.render_body(found, OBS.snapshot_meta(snap))
        plan = OBS.plan_report(found, None, OBS.snapshot_meta(snap))
        return found, "\n".join([json.dumps(found), body, artifact, buf.getvalue(), plan["body"]])

    def test_private_repo_publishes_only_opaque_placeholders_for_every_rule(self):
        found, text = self.published(private_board(True))
        self.assertEqual(sorted({f["rule"] for f in found}), ["a", "b", "c", "d", "e", "x"])
        for secret in (SECRET_REPO, "Secret.Repo", SECRET_TITLE, SECRET_PR_TITLE, "pull/900"):
            self.assertNotIn(secret, text)
        self.assertNotRegex(text, r"FS-GG/Secret")
        for f in found:
            self.assertTrue(f["ref"].startswith("private:"), f)
            self.assertNotIn("title", f)
            self.assertTrue(f.get("private"))

    def test_unknown_visibility_counts_as_private(self):
        found, text = self.published(private_board("absent"))
        self.assertEqual(len(found), 6)
        for secret in (SECRET_REPO, SECRET_TITLE, SECRET_PR_TITLE):
            self.assertNotIn(secret, text)

    def test_a_missing_visibility_key_is_unknown_not_public(self):
        node = item(100, status=None, repo=SECRET_REPO, title=SECRET_TITLE)
        del node["repoPrivate"]
        found, text = self.published(snapshot([node]))
        self.assertNotIn(SECRET_TITLE, text)
        self.assertTrue(found[0]["ref"].startswith("private:"))

    def test_draft_cards_are_never_published_by_title(self):
        draft = {"itemId": "PVTI_draft", "status": None, "track": "Follow-up", "workLevel": "Delivery",
                 "type": "DraftIssue", "title": SECRET_TITLE, "comments": []}
        found, text = self.published(snapshot([draft]))
        self.assertNotIn(SECRET_TITLE, text)
        self.assertNotIn("PVTI_draft", text)
        self.assertTrue(found[0]["ref"].startswith("private:"))

    def test_public_card_keeps_its_name_but_a_private_or_unknown_pr_is_masked(self):
        for pr_visibility in (True, None):
            snap = snapshot([item(100, status="Blocked", title="public title")],
                            [pr(900, body="Refs FS-GG/.github#100", repo=SECRET_REPO, title=SECRET_PR_TITLE,
                                repoPrivate=pr_visibility)])
            found, text = self.published(snap)
            self.assertEqual(found[0]["ref"], f"{REPO}#100")
            self.assertEqual(found[0]["title"], "public title")
            self.assertNotIn(SECRET_REPO, text)
            self.assertNotIn(SECRET_PR_TITLE, text)
            self.assertTrue(found[0]["prs"][0].startswith("private:"))

    def test_public_repo_is_published_in_full(self):
        found, text = self.published(private_board(False))
        self.assertIn(SECRET_REPO, text)
        self.assertIn(SECRET_TITLE, text)

    def test_placeholders_are_stable_so_the_finding_set_dedupes(self):
        a = [f["key"] for f in analyze(private_board(True), NOW)]
        b = [f["key"] for f in analyze(private_board(True), NOW + dt.timedelta(minutes=5))]
        self.assertEqual(a, b)

    def test_reveal_private_is_an_explicit_local_switch(self):
        found = analyze(private_board(True), NOW, reveal_private=True)
        self.assertIn(f"{SECRET_REPO}#100", [f["ref"] for f in found])
        self.assertIn(SECRET_TITLE, [f.get("title") for f in found])

    def test_placeholders_are_keyed_not_a_plain_hash(self):
        import hashlib
        found = analyze(private_board(True), NOW)
        raw = f"{SECRET_REPO}#100"
        plain = "private:" + hashlib.sha256(raw.encode()).hexdigest()[:12]
        self.assertNotIn(plain, [f["ref"] for f in found])
        other = analyze(private_board(True), NOW, hmac_key=b"a-different-key-0123456789")
        self.assertNotEqual([f["ref"] for f in found], [f["ref"] for f in other])
        self.assertEqual(OBS.opaque(raw, TEST_KEY.encode()), [f["ref"] for f in found if f["rule"] == "a"][0])

    def test_without_a_usable_key_the_check_fails_closed_and_writes_nothing(self):
        for value in (None, "", "short"):
            with tempfile.TemporaryDirectory() as d:
                sp, out = Path(d, "s.json"), Path(d, "f.json")
                sp.write_text(json.dumps(snapshot([item()])))  # nothing private: the key is still required
                old = os.environ.pop("BOARD_DRIFT_HMAC_KEY", None)
                if value is not None:
                    os.environ["BOARD_DRIFT_HMAC_KEY"] = value
                try:
                    err = io.StringIO()
                    with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(err):
                        rc = OBS.main(["check", "--snapshot", str(sp), "--out", str(out), "--now", iso_now()])
                finally:
                    os.environ["BOARD_DRIFT_HMAC_KEY"] = old if old is not None else TEST_KEY
                self.assertEqual((rc, out.exists()), (OBS.NO_VERDICT, False), repr(value))
                self.assertIn("BOARD_DRIFT_HMAC_KEY", err.getvalue())

    def test_reveal_private_needs_no_key_for_local_use(self):
        found = OBS.analyze(private_board(True), NOW, reveal_private=True)
        self.assertEqual(len(found), 6)

    def test_urls_are_dropped_for_private_findings_and_kept_for_public_ones(self):
        private = analyze(private_board(True), NOW)
        for f in private:
            self.assertNotIn("commentUrl", f)
            self.assertNotIn("prUrl", f)
        self.assertNotIn("https://", json.dumps(private))
        public = analyze(private_board(False), NOW)
        self.assertTrue([f for f in public if f["rule"] == "e"][0].get("commentUrl"))

    def test_a_non_boolean_is_private_flag_is_unknown_and_so_private(self):
        for value in ("false", 0, "", [], {}, None):
            node = item(100, status=None, title=SECRET_TITLE, repo=SECRET_REPO)
            node["repoPrivate"] = value
            found = analyze(snapshot([node]), NOW)
            self.assertTrue(found[0]["ref"].startswith("private:"), repr(value))
            self.assertNotIn(SECRET_TITLE, json.dumps(found))

    def test_the_workflow_never_passes_reveal_private(self):
        self.assertNotIn("reveal", (ROOT / ".github/workflows/board-drift-observer.yml").read_text())


def iso_now() -> str:
    return iso_time(NOW)


def iso_time(value: dt.datetime) -> str:
    return value.strftime("%Y-%m-%dT%H:%M:%SZ")


class Dispositions(unittest.TestCase):
    """F2/F3: every rule has a recorded-disposition route, newer than the evidence it covers."""

    def ack(self, text, hours=0.5, author="EHotwagner", cid=900):
        return comment(cid, hours=hours, body=f"[board-drift-ack] {text} reason recorded", author=author)

    def test_an_ack_comment_is_never_itself_a_ruling(self):
        only_ack = comment(9, hours=1, body="Root/fdev ruling, disposition: [board-drift-ack] e:1 no Status change owed")
        self.assertTrue(OBS.RULING_MARKER.search(only_ack["body"]), "the fixture must otherwise look like a ruling")
        self.assertEqual(rules([item(status="Blocked", comments=[only_ack])]), [])

    def test_a_disposition_written_with_the_ack_clears_itself_and_does_not_fire_forever(self):
        ruling = comment(1, hours=3)
        ack = self.ack("e:1", hours=2)
        self.assertEqual(rules([item(status="Blocked", comments=[ruling, ack])]), [])
        later = comment(2, hours=1, body="Root/fdev ruling: a second decision")
        self.assertEqual(rules([item(status="Blocked", comments=[ruling, ack, later])]), [("e", f"{REPO}#100")])

    def test_rule_a_ack_covers_the_acked_pr_set_and_a_subset_of_it(self):
        both = [pr(900), pr(901)]
        card = lambda ack: item(status="Blocked", comments=[ack])
        self.assertEqual(rules([card(self.ack(f"a:{REPO}#900+{REPO}#901"))], both), [])
        self.assertEqual(rules([card(self.ack(f"a:{REPO}#900+{REPO}#901"))], [pr(900)]), [])
        self.assertEqual(rules([card(self.ack(f"a:{REPO}#900"))], both), [("a", f"{REPO}#100")])
        self.assertEqual(rules([card(self.ack("a:*"))], both), [])

    def test_ack_must_be_newer_than_the_evidence_it_covers(self):
        old = self.ack("a:*", hours=60)
        self.assertEqual(rules([item(status="Blocked", statusUpdatedAt=hours_ago(2), comments=[old])], [pr()]),
                         [("a", f"{REPO}#100")])

    def test_ack_routes_for_b_c_d_and_x(self):
        closed = item(status="Ready", state="CLOSED", closedAt=hours_ago(10), comments=[self.ack("b:closed")])
        self.assertEqual(rules([closed]), [])
        open_done = item(status="Done", state="OPEN", comments=[self.ack("b:open-done")])
        self.assertEqual(rules([open_done]), [])
        idle = dict(status="In progress", statusUpdatedAt=hours_ago(24 * 9), contentUpdatedAt=hours_ago(24 * 9))
        self.assertEqual(rules([item(**idle, comments=[self.ack("c:idle", hours=24)])]), [])
        self.assertEqual(rules([item(**idle, comments=[self.ack("c:idle", hours=24 * 8)])]), [("c", f"{REPO}#100")])
        self.assertEqual(rules([item(track=None, comments=[self.ack("d:Track")])]), [])
        node = {"itemId": "I", "status": "Ready", "track": "Follow-up", "workLevel": "Delivery", "unreadable": True,
                "repo": REPO, "repoPrivate": False, "number": 7, "itemUpdatedAt": hours_ago(48),
                "comments": [self.ack("x:unreadable")]}
        self.assertEqual(rules([node]), [])

    def test_the_ack_key_is_the_one_printed_on_the_report_line(self):
        f = analyze(snapshot([item(track=None)]), NOW)[0]
        self.assertEqual(f["ack"], "[board-drift-ack] d:Track")
        self.assertIn("[board-drift-ack] d:Track", OBS.render_body([f], OBS.snapshot_meta(snapshot([item()]))))

    def test_wrong_rule_wrong_key_or_non_root_ack_does_not_clear(self):
        for text, author in (("d:Track", "EHotwagner"), ("b:closed", "EHotwagner"), ("d:Status", "EHotwagner"),
                             ("d:*", "stranger")):
            hit = rules([item(track=None, comments=[self.ack(text, author=author)])])
            if text in ("d:Track",):
                self.assertEqual(hit, [])
            else:
                self.assertEqual(hit, [("d", f"{REPO}#100")], text)

    def test_a_prose_word_after_the_token_is_not_an_ack(self):
        c = comment(5, hours=0.5, body="[board-drift-ack] a stale note without a rule key")
        self.assertEqual(rules([item(track=None, comments=[c])]), [("d", f"{REPO}#100")])


class DownRankAndTruncation(unittest.TestCase):
    META = {"number": 3, "fetchedAt": "2026-10-30T18:00:00Z", "items": 1}

    def test_docs_titled_prs_rank_low_and_are_listed_apart(self):
        docs = [pr(900, title="docs(atlas): record"), pr(901, title="docs: more")]
        found = analyze(snapshot([item(status="Blocked")], docs), NOW)
        self.assertEqual([f["rank"] for f in found], ["low"])
        body = OBS.render_body(found, self.META)
        self.assertIn("lower confidence", body.lower())

    def test_one_non_docs_pr_keeps_normal_rank_and_documentation_is_not_docs(self):
        mixed = analyze(snapshot([item(status="Blocked")], [pr(900, title="docs: x"), pr(901, title="fix: y")]), NOW)
        self.assertEqual(mixed[0]["rank"], "normal")
        word = analyze(snapshot([item(status="Blocked")], [pr(900, title="documentation overhaul")]), NOW)
        self.assertEqual(word[0]["rank"], "normal")

    def big(self, n):
        return analyze(snapshot([item(1000 + i, status=None, title="t" * 200) for i in range(n)]), NOW)

    def test_a_huge_finding_set_is_truncated_under_the_issue_limit_with_an_n_more_line(self):
        found = self.big(1000)
        body = OBS.render_body(found, self.META)
        self.assertLess(len(body), 65536)
        self.assertRegex(body, r"\.\.\. \d+ more finding\(s\) not shown")
        shown = body.count("\n- `")
        self.assertEqual(int(re.search(r"(\d+) more finding", body).group(1)), len(found) - shown)
        marker = OBS.parse_marker(body)
        self.assertEqual(marker["digest"], OBS.digest(found))
        self.assertEqual(marker["count"], len(found))

    def test_a_truncated_set_still_dedupes_and_plans_without_failing(self):
        found = self.big(1000)
        existing = {"number": 7, "state": "open", "body": OBS.render_body(found, self.META)}
        self.assertEqual(OBS.plan_report(found, existing, self.META)["action"], "noop")
        plan = OBS.plan_report(self.big(999), existing, self.META)
        self.assertEqual(plan["action"], "update")
        self.assertLess(len(plan["body"]), 65536)
        self.assertLess(len(plan["comment"]), 65536)

    def test_a_small_set_is_not_truncated(self):
        self.assertNotIn("more finding", OBS.render_body(self.big(3), self.META))


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
                    "body": OBS.render_body(analyze(snapshot([item(100, status=None, title="t100")]), NOW),
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

    def test_download_artifact_is_pinned_with_its_version_and_the_selftest_names_real_jobs(self):
        self.assertRegex(self.text, r"actions/download-artifact@[0-9a-f]{40} # v\d")
        header = (ROOT / ".github/workflows/board-drift-observer-selftest.yml").read_text().split("name:", 1)[0]
        self.assertNotIn("`observe`", header)
        self.assertNotIn("`report`", header)
        for job in self.doc["jobs"]:
            self.assertIn(f"`{job}`", header)

    def test_only_findings_json_is_uploaded_never_the_scratch_directory(self):
        uploads = [s for j in self.doc["jobs"].values() for s in j["steps"] if "upload-artifact" in s.get("uses", "")]
        self.assertEqual(len(uploads), 1)
        path = uploads[0]["with"]["path"]
        self.assertTrue(path.endswith("/findings.json") and "\n" not in path, path)
        self.assertEqual(path, "${{ steps.scratch.outputs.dir }}/findings.json")
        self.assertNotIn("snapshot", path)
        self.assertEqual(uploads[0]["with"]["if-no-files-found"], "error")

    def test_the_hmac_key_reaches_only_the_check_step_of_the_read_job(self):
        read, report = self.doc["jobs"]["board-drift-read"], self.doc["jobs"]["board-drift-report"]
        holders = [s.get("name") for s in read["steps"] if "BOARD_DRIFT_HMAC_KEY" in json.dumps(s)]
        self.assertEqual(holders, ["Evaluate the drift rules"])
        self.assertNotIn("BOARD_DRIFT_HMAC_KEY", json.dumps(report))
        self.assertEqual(self.text.count("secrets.BOARD_DRIFT_HMAC_KEY"), 1)

    def test_the_workflow_writes_no_board_field(self):
        for forbidden in ("item-edit", "updateProjectV2", "mutation", "set-field", "projects: write", "permission-organization-projects: write"):
            self.assertNotIn(forbidden, self.text)
            self.assertNotIn(forbidden, (ROOT / "scripts/board-drift-fetch.py").read_text().replace("never writes a board field", ""))


def currency_doc(*findings, schema="fsgg:board-currency-findings/v1"):
    return {"schema": schema, "ruleset": "fsgg:board-currency-rules/v1", "findings": list(findings)}


def cfind(rule="R3", subject="FS-GG/.github#2", reason="no-blocker", public=True, **kw):
    base = {"id": f"{rule}:{subject}:{reason}", "rule": rule, "subject": subject, "reason": reason,
            "text": "Blocked without a native blocked-by issue", "public": public, "title": "card 2"}
    base.update(kw)
    return base


class CurrencyConvergence(unittest.TestCase):
    """Phase 2: board-currency findings share the one report issue, digest and writer (Root decision 3)."""

    KEY = TEST_KEY.encode()
    META = {"number": 3, "fetchedAt": "2026-10-31T09:00:00Z", "items": 98, "currencyRuleset": "fsgg:board-currency-rules/v1"}

    def test_public_subject_is_named_private_and_unknown_and_draft_are_placeholders(self):
        out = OBS.currency_findings(currency_doc(
            cfind(), cfind(subject="FS-GG/Secret#5", public=False, title="secret title"),
            cfind(subject="FS-GG/Unknown#6", public=None), cfind(rule="R7", subject="draft:PVTI_x", reason="draft",
                                                                public=False)), self.KEY)
        refs = [r["ref"] for r in out]
        self.assertIn("FS-GG/.github#2", refs)
        dump = json.dumps(out)
        for leaked in ("Secret", "secret title", "Unknown#6", "PVTI_x"):
            self.assertNotIn(leaked, dump)
        self.assertEqual(sum(1 for r in out if r["ref"].startswith("private:")), 3)
        self.assertTrue(all(r["key"].startswith(r["rule"] + "|" + r["ref"] + "|") for r in out))

    def test_placeholder_is_the_drift_placeholder_and_stable(self):
        a = OBS.currency_findings(currency_doc(cfind(subject="FS-GG/Secret#5", public=False)), self.KEY)
        b = OBS.currency_findings(currency_doc(cfind(subject="FS-GG/Secret#5", public=False)), self.KEY)
        self.assertEqual(a, b)
        self.assertEqual(a[0]["ref"], OBS.opaque("FS-GG/Secret#5", self.KEY))

    def test_refuses_without_key_and_on_a_foreign_document(self):
        with self.assertRaises(OBS.NoVerdict):
            OBS.currency_findings(currency_doc(cfind()), None)
        with self.assertRaises(OBS.NoVerdict):
            OBS.currency_findings({"schema": "fsgg:board-drift-findings/v1", "findings": []}, self.KEY)
        with self.assertRaises(OBS.NoVerdict):
            OBS.currency_findings(currency_doc(cfind(rule="a")), self.KEY)

    def test_v2_documents_are_accepted_and_pending_is_shown(self):
        out = OBS.currency_findings(currency_doc(cfind(rule="R5", reason="children-complete", pending="closure-proposed"),
                                                 schema="fsgg:board-currency-findings/v2"), self.KEY)
        self.assertIn("closure-proposed", out[0]["text"])

    def test_body_has_currency_sections_without_ack_and_names_both_rule_sets(self):
        findings = OBS.currency_findings(currency_doc(cfind(), cfind(rule="R1", subject="FS-GG/.github#10",
                                                                     reason="no-delivery")), self.KEY)
        body = OBS.render_body(findings, self.META)
        self.assertIn("### R1. Routine PR references exactly one open Delivery", body)
        self.assertIn("### R3. Blocked names an open blocker", body)
        self.assertIn("fsgg:board-currency-rules/v1", body)
        for line in body.splitlines():
            if line.startswith("- `FS-GG"):
                self.assertNotIn("Ack:", line)

    def test_a_currency_change_changes_the_digest_and_rewrites_the_issue(self):
        one = OBS.currency_findings(currency_doc(cfind()), self.KEY)
        two = OBS.currency_findings(currency_doc(cfind(), cfind(subject="FS-GG/.github#3")), self.KEY)
        existing = {"number": 4985, "state": "open", "title": OBS.REPORT_TITLE, "body": OBS.render_body(one, self.META)}
        self.assertEqual(OBS.plan_report(one, existing, self.META)["action"], "noop")
        self.assertEqual(OBS.plan_report(two, existing, self.META)["action"], "update")

    def _existing(self, findings, state="open"):
        return {"number": 4985, "state": state, "title": OBS.REPORT_TITLE, "body": OBS.render_body(findings, self.META)}

    def test_r1_only_changes_rewrite_the_body_without_comment_or_reopen(self):
        base = OBS.currency_findings(currency_doc(cfind()), self.KEY)
        more = OBS.currency_findings(currency_doc(cfind(), cfind(rule="R1", subject="FS-GG/.github#10", reason="no-delivery")), self.KEY)
        for state in ("open", "closed"):
            added = OBS.plan_report(more, self._existing(base, state), self.META)
            self.assertEqual((added["action"], added["comment"]), ("update", None), state)
            removed = OBS.plan_report(base, self._existing(more, state), self.META)
            self.assertEqual((removed["action"], removed["comment"]), ("update", None), state)
        self.assertIn("(1)", added["body"])  # R1 shown as a count plus the list

    def test_new_non_r1_currency_finding_comments_and_lists_no_r1_key(self):
        base = OBS.currency_findings(currency_doc(cfind(rule="R1", subject="FS-GG/.github#10", reason="no-delivery")), self.KEY)
        more = OBS.currency_findings(currency_doc(
            cfind(rule="R1", subject="FS-GG/.github#10", reason="no-delivery"),
            cfind(rule="R1", subject="FS-GG/.github#11", reason="no-delivery"), cfind()), self.KEY)
        plan = OBS.plan_report(more, self._existing(base), self.META)
        self.assertEqual(plan["action"], "update")
        self.assertIn("R3|FS-GG/.github#2|no-blocker", plan["comment"])
        self.assertNotIn("R1|", plan["comment"])
        self.assertIn("R1: 1 new, 0 cleared", plan["comment"])
        reopened = OBS.plan_report(more, self._existing(base, "closed"), self.META)
        self.assertEqual(reopened["action"], "reopen")

    def test_cleared_non_r1_currency_finding_is_quiet(self):
        base = OBS.currency_findings(currency_doc(cfind()), self.KEY)
        plan = OBS.plan_report(OBS.currency_findings(currency_doc(cfind(rule="R1", subject="FS-GG/.github#10",
                                                                       reason="no-delivery")), self.KEY),
                               self._existing(base), self.META)
        self.assertEqual(plan["comment"], None)

    def test_any_drift_change_comments(self):
        cur = OBS.currency_findings(currency_doc(cfind(rule="R1", subject="FS-GG/.github#10", reason="no-delivery")), self.KEY)
        drift = analyze(snapshot([item(100, status=None)]), NOW)
        with_drift = OBS.plan_report(cur + drift, self._existing(cur), self.META)
        self.assertTrue(with_drift["comment"])
        cleared = OBS.plan_report(cur, self._existing(cur + drift), self.META)
        self.assertTrue(cleared["comment"])

    def test_apply_update_without_comment_posts_no_comment(self):
        calls = self.record()
        out = OBS.apply_plan("FS-GG/.github", {"action": "update", "body": "b", "comment": None}, {"number": 4985})
        self.assertEqual(out, "updated #4985 (body only)")
        self.assertFalse([a for a, _ in calls if a[0].endswith("/comments")])
        self.assertEqual([p for a, p in calls if "PATCH" in a], [{"body": "b", "title": OBS.REPORT_TITLE}])

    def test_legacy_title_is_renamed_even_when_nothing_changed(self):
        one = OBS.currency_findings(currency_doc(cfind()), self.KEY)
        existing = {"number": 4985, "state": "open", "title": "Board drift: Project 3", "body": OBS.render_body(one, self.META)}
        self.assertEqual(OBS.plan_report(one, existing, self.META)["action"], "rename")
        self.assertEqual(OBS.plan_report([], dict(existing, state="closed"), self.META)["action"], "rename")

    def record(self):
        calls = []
        original = OBS.gh_api
        def fake(args, payload=None, *, allow_status=()):
            calls.append((args, payload))
            if args and args[0].endswith("/issues") and payload is not None:
                return json.dumps({"number": 99})
            return ""
        OBS.gh_api = fake
        self.addCleanup(setattr, OBS, "gh_api", original)
        return calls

    def test_apply_rename_and_every_patch_carries_the_new_title(self):
        calls = self.record()
        self.assertEqual(OBS.apply_plan("FS-GG/.github", {"action": "rename", "reason": "x"}, {"number": 4985}),
                         "renamed #4985")
        self.assertEqual(calls[-1][1], {"title": OBS.REPORT_TITLE})
        OBS.apply_plan("FS-GG/.github", {"action": "update", "body": "b", "comment": "c"}, {"number": 4985})
        patches = [p for a, p in calls if "PATCH" in a]
        self.assertEqual(patches[-1]["title"], OBS.REPORT_TITLE)
        OBS.apply_plan("FS-GG/.github", {"action": "close", "body": "b", "comment": "c"}, {"number": 4985})
        patches = [p for a, p in calls if "PATCH" in a]
        self.assertEqual(patches[-1]["title"], OBS.REPORT_TITLE)
        OBS.apply_plan("FS-GG/.github", {"action": "create", "body": "b"}, None)
        self.assertEqual([p for a, p in calls if a[0].endswith("/issues")][-1]["title"], "Board findings: Project 3")

    def test_find_report_issue_recognises_the_legacy_title(self):
        stub = GhStub(self, issues=[{"number": 4985, "state": "open", "title": "Board drift: Project 3", "body": ""}])
        self.assertEqual(OBS.find_report_issue("FS-GG/.github")["number"], 4985)

    def test_check_merges_currency_findings_and_records_the_ruleset(self):
        with tempfile.TemporaryDirectory() as d:
            snap, cur, out = Path(d, "s.json"), Path(d, "c.json"), Path(d, "f.json")
            snap.write_text(json.dumps(snapshot([item(100)])))
            cur.write_text(json.dumps(currency_doc(cfind(subject="FS-GG/Secret#5", public=False))))
            with contextlib.redirect_stdout(io.StringIO()) as printed:
                rc = OBS.main(["check", "--snapshot", str(snap), "--currency", str(cur), "--out", str(out),
                               "--now", "2026-10-30T18:00:00Z"])
            self.assertEqual(rc, 0)
            doc = json.loads(out.read_text())
            self.assertEqual(doc["meta"]["currencyRuleset"], "fsgg:board-currency-rules/v1")
            self.assertEqual([f["rule"] for f in doc["findings"]], ["R3"])
            self.assertNotIn("Secret", out.read_text() + printed.getvalue())

    def test_check_with_a_bad_currency_file_is_no_verdict_and_writes_nothing(self):
        with tempfile.TemporaryDirectory() as d:
            snap, cur, out = Path(d, "s.json"), Path(d, "c.json"), Path(d, "f.json")
            snap.write_text(json.dumps(snapshot([item(100)])))
            cur.write_text("{}")
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                rc = OBS.main(["check", "--snapshot", str(snap), "--currency", str(cur), "--out", str(out)])
            self.assertEqual(rc, OBS.NO_VERDICT)
            self.assertFalse(out.exists())

    def test_workflow_reads_currency_before_evaluating_and_merges_it_in_the_keyed_step(self):
        import yaml
        doc = yaml.safe_load((ROOT / ".github/workflows/board-drift-observer.yml").read_text())
        steps = doc["jobs"]["board-drift-read"]["steps"]
        names = [s.get("name", "") for s in steps]
        fetch = names.index("Read Project 3 for the board-currency rules (read-only)")
        evaluate = names.index("Evaluate the drift rules")
        self.assertLess(fetch, evaluate)
        self.assertIn("board-currency-fetch.py", steps[fetch]["run"])
        run = steps[evaluate]["run"]
        self.assertLess(run.index("board-currency-rules.py check"), run.index("board-drift-observer.py check"))
        self.assertIn('--currency "$SCRATCH/currency.json"', run)
        self.assertFalse((ROOT / ".github/workflows/board-currency-shadow.yml").exists())


if __name__ == "__main__":
    unittest.main()
