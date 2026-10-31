"""Fixture for scripts/coord-board-archive.py, the Project 3 Done-retention archiver (#5119).

The tool archives cards off the shared board, so every guard has a negative case that proves it can say NO,
written so that deleting the guard turns the case red. The planner is pure; the IO runs against a recording
`gh` stub (COORD_ARCHIVE_GH), with no token and no network.
"""
from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import os
import stat
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("coord_board_archive", ROOT / "scripts" / "coord-board-archive.py")
A = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(A)

PROJECT = "PVT_kwDOEYAWY84Bldpa"


def row(n: int, closed: str | None = None, **kw) -> dict:
    base = {"itemId": f"PVTI_{n}", "archived": False, "status": "Done", "kind": "Issue", "contentId": f"I_{n}",
            "repo": "FS-GG/.github", "number": n, "state": "CLOSED",
            "closedAt": closed if closed is not None else f"2026-10-{1 + n % 28:02d}T{n % 24:02d}:00:00Z"}
    base.update(kw)
    return base


def node(r: dict) -> dict:
    content = {"__typename": r["kind"], "id": r["contentId"]}
    if r["kind"] in ("Issue", "PullRequest"):
        content.update(number=r["number"], state=r["state"], closedAt=r["closedAt"], repository={"nameWithOwner": r["repo"]})
    return {"id": r["itemId"], "isArchived": r["archived"], "status": {"name": r["status"]} if r["status"] else None, "content": content}


def scan_page(rows: list[dict], total: int | None = None, board: int = 222, has_next: bool = False, cursor: str | None = "c1") -> dict:
    return {"data": {"organization": {"projectV2": {"id": PROJECT, "title": "Coordination V2", "all": {"totalCount": board},
            "items": {"totalCount": len(rows) if total is None else total,
                      "pageInfo": {"hasNextPage": has_next, "endCursor": cursor if has_next else None},
                      "nodes": [node(r) for r in rows]}}}}}


def item_read(r: dict, archived: bool | None = None, project: str = PROJECT, status: str = "Done", **content) -> dict:
    c = {"__typename": r["kind"], "id": r["contentId"], "state": r["state"], "closedAt": r["closedAt"]}
    c.update(content)
    return {"data": {"node": {"id": r["itemId"], "isArchived": r["archived"] if archived is None else archived,
                              "project": {"id": project}, "status": {"name": status}, "content": c}}}


STUB = r'''#!/usr/bin/env python3
import json, sys
state_path = sys.argv[0] + ".state.json"
state = json.load(open(state_path))
args = sys.argv[1:]
query = next((a[len("query="):] for a in args if a.startswith("query=")), "")
kind = "mutation" if "archiveProjectV2Item" in query else "item" if "node(id:" in query else "scan"
state["calls"].append({"kind": kind, "args": args})
queue = state["responses"][kind]
reply = queue.pop(0) if queue else {"exit": 9}
json.dump(state, open(state_path, "w"))
if reply.get("exit"):
    sys.exit(reply["exit"])
sys.stdout.write(json.dumps(reply["body"]))
'''


class Stub:
    def __init__(self, test: unittest.TestCase, scan=(), item=(), mutation=()):
        self.dir = tempfile.TemporaryDirectory()
        test.addCleanup(self.dir.cleanup)
        self.path = Path(self.dir.name, "gh")
        self.path.write_text(STUB)
        self.path.chmod(self.path.stat().st_mode | stat.S_IXUSR)
        wrap = lambda xs: [x if isinstance(x, dict) and "exit" in x else {"body": x} for x in xs]
        self.state = Path(str(self.path) + ".state.json")
        self.state.write_text(json.dumps({"calls": [], "responses": {"scan": wrap(scan), "item": wrap(item), "mutation": wrap(mutation)}}))
        previous = os.environ.get("COORD_ARCHIVE_GH")
        os.environ["COORD_ARCHIVE_GH"] = str(self.path)
        test.addCleanup(lambda: os.environ.__setitem__("COORD_ARCHIVE_GH", previous) if previous else os.environ.pop("COORD_ARCHIVE_GH", None))
        self.custody = Path(self.dir.name, "custody")
        self.binding = Path(self.dir.name, "binding.json")

    def calls(self, kind: str | None = None) -> list[dict]:
        calls = json.loads(self.state.read_text())["calls"]
        return [c for c in calls if kind is None or c["kind"] == kind]

    def records(self) -> list[dict]:
        path = self.custody / "custody.jsonl"
        return [json.loads(line) for line in path.read_text().splitlines()] if path.exists() else []


def run(args: list[str]) -> tuple[int, str, str]:
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        try:
            code = A.main(args)
        except SystemExit as exc:
            code = exc.code
    return code, out.getvalue(), err.getvalue()


class Plan(unittest.TestCase):
    def test_keeps_the_newest_by_close_time_and_plans_only_the_older_rest(self):
        rows = [row(n, closed=f"2026-{1 + n // 28:02d}-{1 + n % 28:02d}T00:00:00Z") for n in range(1, 206)]
        archive, kept, unranked = A.plan(list(reversed(rows)), 200)
        self.assertEqual((len(archive), len(kept), unranked), (5, 200, []))
        newest_archived = max(A.parse_time(r["closedAt"]) for r in archive)
        oldest_kept = min(A.parse_time(r["closedAt"]) for r in kept)
        self.assertLess(newest_archived, oldest_kept)
        self.assertEqual([r["number"] for r in archive], [5, 4, 3, 2, 1])

    def test_at_or_below_the_bound_nothing_is_planned(self):
        rows = [row(n) for n in range(1, 201)]
        self.assertEqual(A.plan(rows, 200)[0], [])
        self.assertEqual(len(A.plan(rows + [row(201)], 200)[0]), 1)

    def test_ties_never_depend_on_connection_order(self):
        same = "2026-10-01T00:00:00Z"
        rows = [row(3, same, repo="FS-GG/b"), row(1, same, repo="FS-GG/a"), row(2, same, repo="FS-GG/a")]
        first = A.plan(rows, 1)
        second = A.plan(list(reversed(rows)), 1)
        self.assertEqual([r["itemId"] for r in first[0]], [r["itemId"] for r in second[0]])
        self.assertEqual([r["itemId"] for r in first[1]], ["PVTI_1"])

    def test_every_guard_keeps_its_card_visible_with_a_reason(self):
        old = "2020-01-01T00:00:00Z"
        cases = {
            "not Done": row(1, old, status="Ready"),
            "open issue": row(2, old, state="OPEN"),
            "merged issue": row(3, old, state="MERGED"),
            "draft": row(4, old, kind="DraftIssue"),
            "no closedAt": row(5, None, closedAt=None),
            "bad closedAt": row(6, "yesterday"),
            "naive closedAt": row(7, "2020-01-01T00:00:00"),
            "already archived": row(8, old, archived=True),
            "archive state unknown": row(9, old, archived=None),
            "no item id": row(10, old, itemId=None),
            "no content id": row(11, old, contentId=None),
            "no number": row(12, old, number=None),
        }
        for name, case in cases.items():
            archive, kept, unranked = A.plan([case], 0)
            self.assertEqual(archive, [], name)
            self.assertEqual(len(unranked), 1, name)
        merged_pr = row(13, old, kind="PullRequest", state="MERGED")
        self.assertEqual(len(A.plan([merged_pr], 0)[0]), 1)

    def test_negative_keep_is_refused(self):
        with self.assertRaises(ValueError):
            A.plan([], -1)


class Scan(unittest.TestCase):
    def test_reads_done_only_in_pages_of_100(self):
        rows = [row(n) for n in range(1, 151)]
        stub = Stub(self, scan=[scan_page(rows[:100], total=150, has_next=True), scan_page(rows[100:], total=150)])
        project, read = A.scan("FS-GG", 3)
        self.assertEqual((project["id"], len(read)), (PROJECT, 150))
        calls = stub.calls("scan")
        self.assertEqual(len(calls), 2)
        self.assertTrue(all(any('items(first:100, after:$cursor, query:"status:Done")' in a for a in c["args"]) for c in calls))

    def test_every_incomplete_read_refuses(self):
        rows = [row(1), row(2)]
        bad = {
            "over the board limit": [scan_page(rows, board=501)],
            "count mismatch": [scan_page(rows, total=3)],
            "partial page set": [scan_page(rows, has_next=True)] * 5,
            "next without cursor": [scan_page(rows, total=4, has_next=True, cursor=None)],
            "count changed": [scan_page(rows, total=4, has_next=True), scan_page(rows, total=5)],
            "duplicate card": [scan_page([rows[0], rows[0]])],
            "errors": [{"errors": [{"message": "x"}]}],
            "no project": [{"data": {"organization": {"projectV2": None}}}],
            "failed call": [{"exit": 1}],
        }
        for name, replies in bad.items():
            Stub(self, scan=replies)
            with self.assertRaises(A.Refused, msg=name):
                A.scan("FS-GG", 3)


class Execute(unittest.TestCase):
    def setUp(self):
        self.rows = [row(n, closed=f"2026-10-{n:02d}T00:00:00Z") for n in range(1, 6)]  # 5 Done, keep 2: 3 planned
        self.planned = sorted(self.rows, key=lambda r: r["closedAt"])[:3]  # oldest three, archived newest first
        self.order = list(reversed(self.planned))

    def args(self, stub: Stub, *extra: str) -> list[str]:
        stub.binding.write_text(json.dumps({"selectedIssues": {self.order[0]["contentId"]: "FS-GG/.github#1"}}))
        return ["--keep", "2", "--execute", "--expect-project-id", PROJECT, "--max-archive", "10",
                "--enrollment-binding", str(stub.binding), "--custody-dir", str(stub.custody), *extra]

    def test_dry_run_is_the_default_and_sends_nothing(self):
        stub = Stub(self, scan=[scan_page(self.rows)])
        code, out, _ = run(["--keep", "2"])
        self.assertEqual(code, 0)
        self.assertIn("DRY RUN", out)
        self.assertEqual((stub.calls("item"), stub.calls("mutation")), ([], []))

    def test_counts_only_prints_no_repository_or_number(self):
        rows = self.rows + [row(40, "2026-01-01T00:00:00Z", repo="FS-GG/Secret.Repo", state="OPEN")]
        Stub(self, scan=[scan_page(rows)])
        code, out, _ = run(["--keep", "2", "--counts-only"])
        self.assertEqual(code, 0)
        self.assertIn("plan to archive 3", out)
        self.assertNotIn("FS-GG/", out)
        self.assertNotIn("#", out)
        stub = Stub(self, scan=[scan_page(self.rows)])
        self.assertEqual(run(self.args(stub, "--counts-only"))[0], 2)
        self.assertEqual(stub.calls(), [])

    def test_execute_requires_the_project_id_a_bound_and_the_enrollment_binding(self):
        for missing in ("--expect-project-id", "--max-archive", "--enrollment-binding"):
            stub = Stub(self, scan=[scan_page(self.rows)])
            argv = self.args(stub)
            index = argv.index(missing)
            del argv[index:index + 2]
            code, _, _ = run(argv)
            self.assertEqual(code, 2, missing)
            self.assertEqual(stub.calls(), [], missing)

    def test_a_different_project_id_refuses_before_any_send(self):
        stub = Stub(self, scan=[scan_page(self.rows)])
        argv = self.args(stub)
        argv[argv.index(PROJECT)] = "PVT_other"
        self.assertEqual(run(argv)[0], 1)
        self.assertEqual((stub.calls("item"), stub.calls("mutation")), ([], []))

    def test_preimage_one_send_readback_and_custody_for_each_card(self):
        items, mutations = [], []
        for r in self.order:
            items += [item_read(r), item_read(r, archived=True)]
            mutations.append({"data": {"archiveProjectV2Item": {"item": {"id": r["itemId"], "isArchived": True}}}})
        stub = Stub(self, scan=[scan_page(self.rows)], item=items, mutation=mutations)
        code, out, _ = run(self.args(stub))
        self.assertEqual(code, 0, out)
        self.assertEqual(len(stub.calls("mutation")), 3)
        sent = [next(a for a in c["args"] if a.startswith("item=")) for c in stub.calls("mutation")]
        self.assertEqual(sent, [f"item={r['itemId']}" for r in self.order])
        steps = [(rec["step"], rec.get("itemId")) for rec in stub.records()]
        expected = [("run", None)] + [s for r in self.order for s in (("intent", r["itemId"]), ("archived", r["itemId"]))] + [("finished", None)]
        self.assertEqual(steps, expected)
        enrolled = {rec["itemId"]: rec["enrolled"] for rec in stub.records() if rec["step"] == "archived"}
        self.assertEqual(enrolled, {self.order[0]["itemId"]: True, self.order[1]["itemId"]: False, self.order[2]["itemId"]: False})
        self.assertEqual(stat.S_IMODE((stub.custody / "custody.jsonl").stat().st_mode), 0o600)

    def test_a_changed_preimage_is_skipped_without_a_send(self):
        first = self.order[0]
        changes = [item_read(first, status="In progress"), item_read(first, archived=True), item_read(first, project="PVT_other"),
                   item_read(first, state="OPEN"), item_read(first, closedAt="2026-10-30T00:00:00Z"), {"data": {"node": None}}]
        for change in changes:
            stub = Stub(self, scan=[scan_page(self.rows)], item=[change])
            code, _, _ = run(self.args(stub, "--max-archive", "1"))
            self.assertEqual(code, 0)
            self.assertEqual(stub.calls("mutation"), [], change)
            self.assertEqual([rec["step"] for rec in stub.records()], ["run", "skipped", "finished"])

    def test_an_uncertain_send_is_recorded_never_resent_and_stops_the_run(self):
        first = self.order[0]
        for mutation, readback in [({"exit": 1}, None), ({"errors": [{"message": "x"}]}, None),
                                   ({"data": {"archiveProjectV2Item": {"item": {"id": first["itemId"], "isArchived": True}}}}, item_read(first, archived=False)),
                                   ({"data": {"archiveProjectV2Item": {"item": {"id": first["itemId"], "isArchived": True}}}}, {"exit": 1})]:
            items = [item_read(first)] + ([readback] if readback else [])
            stub = Stub(self, scan=[scan_page(self.rows)], item=items, mutation=[mutation])
            code, _, err = run(self.args(stub))
            self.assertEqual(code, 3, err)
            self.assertEqual(len(stub.calls("mutation")), 1)
            self.assertEqual([rec["step"] for rec in stub.records()], ["run", "intent", "uncertain", "stopped"])

    def test_max_archive_bounds_the_sends(self):
        r = self.order[0]
        stub = Stub(self, scan=[scan_page(self.rows)], item=[item_read(r), item_read(r, archived=True)],
                    mutation=[{"data": {"archiveProjectV2Item": {"item": {"id": r["itemId"], "isArchived": True}}}}])
        argv = self.args(stub)
        argv[argv.index("--max-archive") + 1] = "1"
        self.assertEqual(run(argv)[0], 0)
        self.assertEqual(len(stub.calls("mutation")), 1)


if __name__ == "__main__":
    unittest.main()
