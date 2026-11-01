"""Fixtures for scripts/board-linkage.py and .github/workflows/board-linkage.yml (Refs #5094).

The check is driven through a recording `gh` stub (no token, no network). Every verdict has a leg:
skip (non-routine passes), overridden (Project 3 never read), linked, refused, unknown; advisory mode
exits 0 on all of them, required mode exits 1 on refused and unknown; output names no referenced issue.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = Path(os.environ.get("BOARD_LINKAGE_SCRIPT", ROOT / "scripts/board-linkage.py"))
ROUTINE = "\n<!-- fsgg:routine-development/v1 head=" + "a" * 40 + " operation=source-change -->"
OVR = '\n<!-- fsgg:board-gate-override/v1 rule=R1 reason="outage" -->'


def pr_doc(body, author="EHotwagner", edited=None, editor=None, state="OPEN"):
    return {"data": {"repository": {"pullRequest": {
        "number": 10, "state": state, "isDraft": False, "body": body, "lastEditedAt": edited,
        "author": {"login": author}, "editor": {"login": editor} if editor else None}}}}


def card(level, status="In progress", state="OPEN", parent=None, project=3, owner="FS-GG", total=None):
    nodes = [{"project": {"number": project, "owner": {"login": owner}}, "status": {"name": status},
              "workLevel": {"name": level}}]
    return {"__typename": "Issue", "state": state,
            "parent": {"number": parent[1], "repository": {"nameWithOwner": parent[0]}} if parent else None,
            "projectItems": {"totalCount": len(nodes) if total is None else total, "nodes": nodes}}


class Check(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)
        self.log = self.dir / "calls.log"

    def tearDown(self):
        self.tmp.cleanup()

    def run_check(self, pr, issues=None, parents=None, mode="advisory", fail_issue_read=False):
        """`issues`: alias responses for the first card read; `parents`: for the second."""
        stub = self.dir / "gh"
        responses = {"pr": pr, "issues": {"data": issues or {}}, "parents": {"data": parents or {}}}
        (self.dir / "r.json").write_text(json.dumps(responses))
        stub.write_text(
            "#!/usr/bin/env python3\n"
            "import json,sys,os\n"
            f"r=json.load(open({str(self.dir / 'r.json')!r}))\n"
            f"log={str(self.log)!r}\n"
            "args=' '.join(sys.argv[1:])\n"
            "n=sum(1 for _ in open(log)) if os.path.exists(log) else 0\n"
            "open(log,'a').write(args.replace(chr(10),' ')+chr(10))\n"
            "if 'pullRequest(number:$number)' in args: print(json.dumps(r['pr'])); sys.exit(0)\n"
            f"if {fail_issue_read!r}: sys.exit(1)\n"
            "print(json.dumps(r['issues'] if n == 1 else r['parents']))\n")
        stub.chmod(0o755)
        env = dict(os.environ, BOARD_LINKAGE_GH=str(stub))
        env.pop("GITHUB_STEP_SUMMARY", None)
        return subprocess.run([sys.executable, str(SCRIPT), "--repo", "FS-GG/.github", "--pr", "10", "--mode", mode],
                              capture_output=True, text=True, env=env)

    def calls(self):
        return self.log.read_text().splitlines() if self.log.exists() else []

    def test_non_routine_pr_is_skipped_and_passes_in_both_modes(self):
        for mode in ("advisory", "required"):
            proc = self.run_check(pr_doc("Refs #1"), mode=mode)
            self.assertEqual(proc.returncode, 0)
            self.assertIn("skip", proc.stdout)
        self.assertEqual(len(self.calls()), 2)  # one PR read per run, never a board read

    def test_linked(self):
        proc = self.run_check(pr_doc("Refs #2" + ROUTINE),
                              issues={"r0": {"issueOrPullRequest": card("Delivery", parent=("FS-GG/.github", 1))}},
                              parents={"r0": {"issueOrPullRequest": card("Outcome")}}, mode="required")
        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
        self.assertIn("linked", proc.stdout)

    def test_outcome_only_is_refused_advisory_passes_required_fails(self):
        issues = {"r0": {"issueOrPullRequest": card("Outcome")}}
        adv = self.run_check(pr_doc("Refs #1" + ROUTINE), issues=issues)
        self.assertEqual(adv.returncode, 0)
        self.assertIn("::warning", adv.stdout)
        self.assertIn("refused (no-delivery)", adv.stdout)
        req = self.run_check(pr_doc("Refs #1" + ROUTINE), issues=issues, mode="required")
        self.assertEqual(req.returncode, 1)
        self.assertIn("::error", req.stdout)

    def test_issue_on_another_project_is_not_a_delivery(self):
        issues = {"r0": {"issueOrPullRequest": card("Delivery", project=7)}}
        proc = self.run_check(pr_doc("Refs #2" + ROUTINE), issues=issues)
        self.assertIn("refused (no-delivery)", proc.stdout)

    def test_delivery_without_outcome_parent_is_refused(self):
        issues = {"r0": {"issueOrPullRequest": card("Delivery")}}
        proc = self.run_check(pr_doc("Refs #2" + ROUTINE), issues=issues)
        self.assertIn("refused (no-parent-outcome)", proc.stdout)

    def test_valid_override_passes_without_reading_the_board(self):
        proc = self.run_check(pr_doc("Refs #1" + ROUTINE + OVR), fail_issue_read=True, mode="required")
        self.assertEqual(proc.returncode, 0)
        self.assertIn("overridden", proc.stdout)
        self.assertEqual(len(self.calls()), 1)

    def test_override_edited_by_someone_else_is_not_honoured(self):
        proc = self.run_check(pr_doc("Refs #1" + ROUTINE + OVR, edited="2026-10-31T00:00:00Z", editor="bot"),
                              issues={"r0": {"issueOrPullRequest": card("Outcome")}})
        self.assertIn("refused (no-delivery)", proc.stdout)

    def test_board_read_failure_is_unknown(self):
        adv = self.run_check(pr_doc("Refs #2" + ROUTINE), fail_issue_read=True)
        self.assertEqual(adv.returncode, 0)
        self.assertIn("unknown (board-read)", adv.stdout)
        req = self.run_check(pr_doc("Refs #2" + ROUTINE), fail_issue_read=True, mode="required")
        self.assertEqual(req.returncode, 1)

    def test_truncated_project_items_are_unknown(self):
        issues = {"r0": {"issueOrPullRequest": card("Delivery", total=30)}}
        proc = self.run_check(pr_doc("Refs #2" + ROUTINE), issues=issues)
        self.assertIn("unknown (board-read)", proc.stdout)

    def test_missing_issue_is_unknown(self):
        proc = self.run_check(pr_doc("Refs #2" + ROUTINE), issues={"r0": {"issueOrPullRequest": None}})
        self.assertIn("unknown (board-read)", proc.stdout)

    def test_unreadable_pr_is_unknown(self):
        proc = self.run_check({"errors": [{"message": "FS-GG/Secret"}]}, mode="required")
        self.assertEqual(proc.returncode, 1)
        self.assertIn("unknown (pr-read)", proc.stdout)
        self.assertNotIn("Secret", proc.stdout + proc.stderr)

    def test_output_never_names_a_referenced_issue(self):
        issues = {"r0": {"issueOrPullRequest": card("Outcome")}}
        proc = self.run_check(pr_doc("Refs FS-GG/PrivateThing#77" + ROUTINE), issues=issues)
        self.assertNotIn("PrivateThing", proc.stdout + proc.stderr)
        self.assertNotIn("#77", proc.stdout + proc.stderr)

    def precheck(self, payload):
        event = self.dir / "event.json"
        out = self.dir / "out.txt"
        out.unlink(missing_ok=True)
        event.write_text(payload if isinstance(payload, str) else json.dumps(payload))
        env = dict(os.environ, GITHUB_OUTPUT=str(out), BOARD_LINKAGE_GH=str(self.dir / "absent-gh"))
        proc = subprocess.run([sys.executable, str(SCRIPT), "--precheck-event", str(event)],
                              capture_output=True, text=True, env=env)
        return proc, out.read_text() if out.exists() else ""

    def test_precheck_skips_a_non_routine_pr_without_any_call(self):
        proc, out = self.precheck({"pull_request": {"body": "Refs #1"}})
        self.assertEqual((proc.returncode, out), (0, "routine=false\n"))
        self.assertEqual(self.calls(), [])

    def test_precheck_evaluates_a_routine_pr(self):
        proc, out = self.precheck({"pull_request": {"body": "Refs #2" + ROUTINE}})
        self.assertEqual((proc.returncode, out), (0, "routine=true\n"))

    def test_precheck_with_an_unreadable_payload_does_not_skip(self):
        for payload in ("not json", {"no_pr": True}, {"pull_request": None}):
            proc, out = self.precheck(payload)
            self.assertEqual((proc.returncode, out), (0, "routine=true\n"), payload)

    def test_null_body_is_not_routine(self):
        proc, out = self.precheck({"pull_request": {"body": None}})
        self.assertEqual(out, "routine=false\n")

    def test_the_check_writes_nothing(self):
        text = SCRIPT.read_text()
        for forbidden in ("mutation", "item-edit", "-X POST", "--method", "gh issue", "gh pr comment"):
            self.assertNotIn(forbidden, text)


class Workflow(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        import yaml
        cls.text = (ROOT / ".github/workflows/board-linkage.yml").read_text()
        cls.doc = yaml.safe_load(cls.text)
        cls.on = cls.doc.get("on", cls.doc.get(True))

    def test_trigger_and_concurrency(self):
        self.assertEqual(list(self.on), ["pull_request_target"])
        self.assertEqual(self.on["pull_request_target"]["types"], ["opened", "synchronize", "reopened", "edited"])
        # Head-keyed (#5159): a body edit keys on its own run id, so it never cancels the head's run.
        self.assertEqual(self.doc["concurrency"]["group"],
                         "board-linkage-${{ contains(fromJSON('[\"opened\",\"synchronize\",\"reopened\"]'), "
                         "github.event.action) && github.event.pull_request.number || github.run_id }}")

    def test_one_light_job_named_for_its_context(self):
        self.assertEqual(list(self.doc["jobs"]), ["board-linkage"])
        job = self.doc["jobs"]["board-linkage"]
        self.assertEqual(job["name"], "board-linkage")
        self.assertLessEqual(job["timeout-minutes"], 5)
        self.assertEqual(job["permissions"], {"contents": "read"})
        self.assertEqual(self.doc["permissions"], {"contents": "read"})

    def test_advisory_only_and_no_candidate_checkout(self):
        job = self.doc["jobs"]["board-linkage"]
        runs = [s["run"] for s in job["steps"] if "run" in s]
        self.assertEqual(len(runs), 2)
        self.assertIn("--precheck-event", runs[0])
        self.assertIn("--mode advisory", runs[1])
        for run in runs:
            self.assertIn("set -euo pipefail", run)
        for step in job["steps"]:
            self.assertNotIn("ref", step.get("with", {}))  # checkout stays on the default branch
        for forbidden in ("github.event.pull_request.body", "head.sha", "head.ref", "continue-on-error",
                          ": write", "--mode required"):
            self.assertNotIn(forbidden, self.text.split("jobs:", 1)[1], forbidden)

    def test_skip_is_decided_before_the_token_is_minted(self):
        steps = self.doc["jobs"]["board-linkage"]["steps"]
        names = [s.get("name", "") for s in steps]
        pre = names.index("Decide skip from the event payload (no token)")
        mint = next(i for i, s in enumerate(steps) if "create-github-app-token" in s.get("uses", ""))
        self.assertLess(pre, mint)
        self.assertNotIn("secrets", json.dumps(steps[pre]))
        self.assertNotIn("GH_TOKEN", json.dumps(steps[pre]))
        for i, step in enumerate(steps):
            if i > pre and ("create-github-app-token" in step.get("uses", "") or "run" in step):
                self.assertEqual(step.get("if"), "steps.precheck.outputs.routine == 'true'", step.get("name"))

    def test_read_only_token_and_pinned_actions(self):
        import re
        steps = self.doc["jobs"]["board-linkage"]["steps"]
        mint = [s for s in steps if "create-github-app-token" in s.get("uses", "")]
        self.assertEqual(len(mint), 1)
        scopes = {k: v for k, v in mint[0]["with"].items() if k.startswith("permission-")}
        self.assertEqual(scopes, {"permission-organization-projects": "read", "permission-issues": "read",
                                  "permission-pull-requests": "read"})
        for uses in re.findall(r"uses:\s*(\S+)", self.text):
            self.assertRegex(uses, r"@[0-9a-f]{40}$", uses)

    def test_not_in_the_distributed_producer_inventory(self):
        inventory = (ROOT / "src/FS.GG.Drivers/workspace-inventory.json").read_text()
        self.assertNotIn("board-linkage", inventory)


if __name__ == "__main__":
    unittest.main()
