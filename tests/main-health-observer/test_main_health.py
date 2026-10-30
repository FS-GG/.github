"""Fixtures for the main-health observer (Refs #4730).

Legs: derivation on synthetic workflow trees (each exclusion has a positive and a negative leg); drift between
the derived list and the observer workflow; collect for green, red, changed and unknown states; the report
planner for the outcomes that matter (leave alone, rewrite, close, refuse); collect and report driven through a
recording `gh` stub; and the observer workflow file read to prove the credential split and the absence of any
path from an unjudged check to a closed issue. The real repository is read-only input for the last group.
"""
from __future__ import annotations

import contextlib
import copy
import datetime as dt
import importlib.util
import io
import json
import os
import re
import stat
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "main-health-observer.py"


def load():
    spec = importlib.util.spec_from_file_location("main_health_observer", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


MH = load()
NOW = dt.datetime(2026, 10, 31, 8, 47, tzinfo=dt.timezone.utc)

OBSERVER_SKELETON = """\
name: main-health-observer
on:
  workflow_dispatch:
jobs:
  # BEGIN GENERATED CHECK JOBS
  # END GENERATED CHECK JOBS
  main-health-collect:
    needs:
      # BEGIN GENERATED NEEDS
      # END GENERATED NEEDS
    runs-on: ubuntu-latest
"""


def wf(on: str, jobs: str = "", perms: str = "permissions:\n  contents: read\n", name: str = "w") -> str:
    jobs = jobs or "  j:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo ok\n"
    return f"name: {name}\non:\n{textwrap.indent(textwrap.dedent(on).strip(), '  ')}\n{perms}jobs:\n{jobs}"


PUSH_PATHS = "push:\n  branches: [main]\n  paths: ['a/**']\nworkflow_call:\n"


class Tree:
    """A temporary repository root with .github/workflows."""

    def __init__(self, files: dict[str, str], observer: str | None = OBSERVER_SKELETON):
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)
        self.wf = self.root / ".github" / "workflows"
        self.wf.mkdir(parents=True)
        for name, text in files.items():
            (self.wf / name).write_text(text)
        if observer is not None:
            (self.wf / MH.OBSERVER_WORKFLOW).write_text(observer)

    def synced(self) -> "Tree":
        plan = MH.derive(self.root)
        MH.write_workflow(plan, self.wf / MH.OBSERVER_WORKFLOW)
        return self

    def close(self):
        self._tmp.cleanup()


def tree(files, observer=OBSERVER_SKELETON):
    t = Tree(files, observer)
    return t


class Derivation(unittest.TestCase):
    def derive(self, files):
        t = Tree(files)
        self.addCleanup(t.close)
        return MH.derive(t.root)

    def test_eligible_and_covered_requires_workflow_call(self):
        plan = self.derive({
            "optin.yml": wf(PUSH_PATHS),
            "plain.yml": wf("push:\n  branches: [main]\n  paths: ['a/**']\n"),
        })
        self.assertEqual(plan["candidates"], ["optin.yml", "plain.yml"])
        self.assertEqual(plan["eligible"], ["optin.yml", "plain.yml"])
        self.assertEqual(plan["covered"], ["optin.yml"])
        self.assertEqual(plan["uncovered"], ["plain.yml"])

    def test_not_a_candidate_without_push_paths_or_main(self):
        plan = self.derive({
            "nopaths.yml": wf("push:\n  branches: [main]\n"),
            "otherbranch.yml": wf("push:\n  branches: [release]\n  paths: ['a/**']\n"),
            "pronly.yml": wf("pull_request:\n  paths: ['a/**']\n"),
        })
        self.assertEqual(plan["candidates"], [])
        self.assertEqual(plan["latestPush"], ["nopaths.yml"])  # still read on the latest-run side

    def test_push_without_branch_filter_counts_as_main(self):
        plan = self.derive({"any.yml": wf("push:\n  paths: ['a/**']\n")})
        self.assertEqual(plan["candidates"], ["any.yml"])

    def test_scheduled_workflows_are_read_for_the_latest_schedule_run(self):
        plan = self.derive({"canary.yml": wf("schedule:\n  - cron: '1 2 * * *'\nworkflow_dispatch:\n")})
        self.assertEqual(plan["latestSchedule"], ["canary.yml"])
        self.assertEqual(plan["candidates"], [])

    def excluded(self, **kw):
        files = {"x.yml": wf(PUSH_PATHS, **kw)}
        plan = self.derive(files)
        return plan["excluded"].get("x.yml", []), plan

    def test_each_exclusion_has_a_leg_that_says_no(self):
        cases = {
            "secret": dict(jobs="  j:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo ${{ secrets.APP_KEY }}\n"),
            "inherit": dict(jobs="  j:\n    runs-on: ubuntu-latest\n    env:\n      A: ${{ secrets.OTHER }}\n    steps:\n      - run: x\n"),
            "self-hosted": dict(jobs="  j:\n    runs-on: [self-hosted, linux]\n    steps:\n      - run: x\n"),
            "environment": dict(jobs="  j:\n    runs-on: ubuntu-latest\n    environment: prod\n    steps:\n      - run: x\n"),
            "write": dict(perms="permissions:\n  contents: read\n  issues: write\n"),
            "job-write": dict(jobs="  j:\n    runs-on: ubuntu-latest\n    permissions:\n      pull-requests: write\n    steps:\n      - run: x\n"),
            "inputs": dict(jobs="  j:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo ${{ inputs.thing }}\n"),
            "payload": dict(jobs="  j:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo ${{ github.event.pull_request.number }}\n"),
            "base-ref": dict(jobs="  j:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo ${{ github.base_ref }}\n"),
            "job-payload": dict(jobs="  j:\n    runs-on: ubuntu-latest\n    env:\n      B: ${{ github.event.before }}\n    steps:\n      - run: x\n"),
            "nested-call": dict(jobs="  j:\n    uses: ./.github/workflows/other.yml\n"),
            "pr-only-job": dict(jobs="  j:\n    if: github.event_name == 'pull_request'\n    runs-on: ubuntu-latest\n    steps:\n      - run: x\n"),
            "opaque-if": dict(jobs="  j:\n    if: contains(github.event.pull_request.labels.*.name, 'x')\n    runs-on: ubuntu-latest\n    steps:\n      - run: x\n"),
        }
        for label, kw in cases.items():
            with self.subTest(label):
                reasons, plan = self.excluded(**kw)
                self.assertTrue(reasons, f"{label} must be excluded")
                self.assertNotIn("x.yml", plan["eligible"])
                self.assertNotIn("x.yml", plan["covered"])

    def test_workflow_run_trigger_is_excluded(self):
        files = {"x.yml": wf(PUSH_PATHS + "workflow_run:\n  workflows: [a]\n")}
        self.assertIn("workflow_run trigger", self.derive(files)["excluded"]["x.yml"])

    def test_negative_legs_stay_eligible(self):
        ok = {
            "guarded-step": dict(jobs="  j:\n    runs-on: ubuntu-latest\n    steps:\n      - if: github.event_name == 'pull_request'\n        run: echo ${{ github.event.pull_request.base.sha }}\n      - run: ok\n"),
            "fallback": dict(jobs="  j:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo ${{ github.event.pull_request.head.sha || github.sha }}\n"),
            "github-token": dict(jobs="  j:\n    runs-on: ubuntu-latest\n    env:\n      GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}\n    steps:\n      - run: x\n"),
            "read-scope": dict(perms="permissions:\n  contents: read\n  packages: read\n"),
            "not-pr-job": dict(jobs="  j:\n    if: github.event_name != 'pull_request'\n    runs-on: ubuntu-latest\n    steps:\n      - run: x\n"),
            "schedule-job": dict(jobs="  j:\n    if: github.event_name == 'schedule' || github.event_name == 'workflow_dispatch'\n    runs-on: ubuntu-latest\n    steps:\n      - run: x\n"),
            "one-pr-only-one-runs": dict(jobs="  a:\n    if: github.event_name == 'pull_request'\n    runs-on: ubuntu-latest\n    steps:\n      - run: x\n  b:\n    runs-on: ubuntu-latest\n    steps:\n      - run: x\n"),
            "comment-mentions-secret": dict(jobs="  j:\n    runs-on: ubuntu-latest\n    steps:\n      # secrets.NOPE is not used\n      - run: x\n"),
        }
        for label, kw in ok.items():
            with self.subTest(label):
                reasons, plan = self.excluded(**kw)
                self.assertEqual(reasons, [])
                self.assertEqual(plan["covered"], ["x.yml"])

    def test_call_scopes_are_exactly_the_callees_read_scopes(self):
        plan = self.derive({"x.yml": wf(PUSH_PATHS, perms="permissions:\n  contents: read\n  packages: read\n")})
        self.assertEqual(plan["callScopes"]["x.yml"], ["contents", "packages"])
        rendered = MH.render_check_jobs(plan["covered"], plan["callScopes"])
        self.assertIn("      packages: read\n", rendered)
        self.assertNotIn("write", rendered)

    def test_invalid_yaml_and_empty_tree_are_no_verdict(self):
        t = Tree({"bad.yml": "name: [unclosed\n"})
        self.addCleanup(t.close)
        with self.assertRaises(MH.NoVerdict):
            MH.derive(t.root)
        empty = tempfile.TemporaryDirectory()
        self.addCleanup(empty.cleanup)
        (Path(empty.name) / ".github" / "workflows").mkdir(parents=True)
        with self.assertRaises(MH.NoVerdict):
            MH.derive(Path(empty.name))

    def test_job_id_that_cannot_be_a_stem_is_no_verdict(self):
        t = Tree({"Bad_Name.yml": wf(PUSH_PATHS)})
        self.addCleanup(t.close)
        with self.assertRaises(MH.NoVerdict):
            MH.derive(t.root)

    def test_event_name_evaluator(self):
        ev = MH.job_runs_on_schedule
        self.assertTrue(ev(None))
        self.assertTrue(ev("github.event_name == 'schedule'"))
        self.assertFalse(ev("github.event_name == 'pull_request'"))
        self.assertTrue(ev("${{ github.event_name != 'pull_request' }}"))
        self.assertFalse(ev("github.event_name == 'push' && github.event_name == 'schedule'"))
        self.assertTrue(ev("!cancelled()"))
        self.assertIsNone(ev("github.ref == 'refs/heads/main'"))
        self.assertIsNone(ev("__import__('os').system('x')"))


class Drift(unittest.TestCase):
    def synced(self, files):
        t = Tree(files)
        self.addCleanup(t.close)
        t.synced()
        return t

    def problems(self, t):
        plan = MH.derive(t.root)
        return MH.workflow_problems(plan, (t.wf / MH.OBSERVER_WORKFLOW).read_text())

    def test_in_sync_is_clean_and_write_is_idempotent(self):
        t = self.synced({"a.yml": wf(PUSH_PATHS), "b.yml": wf(PUSH_PATHS)})
        self.assertEqual(self.problems(t), [])
        self.assertFalse(MH.write_workflow(MH.derive(t.root), t.wf / MH.OBSERVER_WORKFLOW))
        text = (t.wf / MH.OBSERVER_WORKFLOW).read_text()
        self.assertIn("  check-a:\n    uses: ./.github/workflows/a.yml\n", text)
        self.assertIn("      - check-b\n", text)
        yaml.safe_load(text)

    def test_new_opt_in_without_regeneration_is_drift(self):
        t = self.synced({"a.yml": wf(PUSH_PATHS)})
        (t.wf / "b.yml").write_text(wf(PUSH_PATHS))
        self.assertTrue(any("differ" in p for p in self.problems(t)))

    def test_removed_opt_in_is_drift(self):
        t = self.synced({"a.yml": wf(PUSH_PATHS)})
        (t.wf / "a.yml").write_text(wf("push:\n  branches: [main]\n  paths: ['a/**']\n"))
        self.assertTrue(self.problems(t))

    def test_covered_workflow_that_gained_a_secret_is_drift(self):
        t = self.synced({"a.yml": wf(PUSH_PATHS)})
        (t.wf / "a.yml").write_text(wf(PUSH_PATHS, jobs="  j:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo ${{ secrets.K }}\n"))
        problems = self.problems(t)
        self.assertTrue(any("a.yml declares workflow_call but cannot run standalone" in p for p in problems), problems)

    def test_missing_regions_are_reported(self):
        t = Tree({"a.yml": wf(PUSH_PATHS)}, observer="name: x\non: {workflow_dispatch: }\njobs: {}\n")
        self.addCleanup(t.close)
        self.assertEqual(sum("no `" in p for p in self.problems(t)), 2)
        with self.assertRaises(MH.NoVerdict):
            MH.write_workflow(MH.derive(t.root), t.wf / MH.OBSERVER_WORKFLOW)

    def test_more_than_fifty_covered_is_refused(self):
        files = {f"w{i:02d}.yml": wf(PUSH_PATHS) for i in range(MH.MAX_UNIQUE_CALLS + 1)}
        t = self.synced(files)
        self.assertTrue(any("exceed GitHub's limit" in p for p in self.problems(t)))

    def test_plan_cli_check_exit_codes(self):
        t = self.synced({"a.yml": wf(PUSH_PATHS)})
        ok = subprocess.run([sys.executable, "-I", str(SCRIPT), "plan", "--root", str(t.root), "--check"], capture_output=True, text=True)
        self.assertEqual(ok.returncode, 0, ok.stderr)
        (t.wf / "b.yml").write_text(wf(PUSH_PATHS))
        bad = subprocess.run([sys.executable, "-I", str(SCRIPT), "plan", "--root", str(t.root), "--check"], capture_output=True, text=True)
        self.assertEqual(bad.returncode, 1)
        self.assertIn("DRIFT", bad.stderr)
        broken = subprocess.run([sys.executable, "-I", str(SCRIPT), "plan", "--root", str(t.root / "nope"), "--check"], capture_output=True, text=True)
        self.assertEqual(broken.returncode, 2)


# --------------------------------------------------------------------------------------------------
# collect
# --------------------------------------------------------------------------------------------------

def run(conclusion, rid, sha="abcdef1234567", event="push"):
    return {"id": rid, "conclusion": conclusion, "head_sha": sha, "html_url": f"https://github.com/FS-GG/.github/actions/runs/{rid}",
            "updated_at": "2026-10-30T10:00:00Z", "event": event}


class FakeGh:
    """A recording stand-in for `gh api`. Routes match on a substring of the request path."""

    def __init__(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self._tmp.name)
        self.routes: list[dict] = []
        self.log = self.dir / "calls.jsonl"
        self.script = self.dir / "gh"
        self.script.write_text(textwrap.dedent(f"""\
            #!{sys.executable}
            import json, sys, os
            routes = json.load(open({str(self.dir / 'routes.json')!r}))
            argv = sys.argv[1:]
            stdin = sys.stdin.read() if "--input" in argv else None
            with open({str(self.log)!r}, "a") as f:
                f.write(json.dumps({{"argv": argv, "stdin": stdin}}) + "\\n")
            path = argv[1] if len(argv) > 1 else ""
            for r in routes:
                if r["match"] in " ".join(argv):
                    sys.stdout.write(r.get("stdout", ""))
                    sys.stderr.write(r.get("stderr", ""))
                    sys.exit(r.get("rc", 0))
            sys.stderr.write("no route for " + " ".join(argv))
            sys.exit(77)
            """))
        self.script.chmod(self.script.stat().st_mode | stat.S_IXUSR)
        self.set([])

    def set(self, routes):
        self.routes = routes
        (self.dir / "routes.json").write_text(json.dumps(routes))
        self.log.write_text("")

    def calls(self):
        return [json.loads(line) for line in self.log.read_text().splitlines() if line.strip()]

    def writes(self):
        return [c for c in self.calls() if "-X" in c["argv"] or "--input" in c["argv"]]

    def env(self):
        return {"MAIN_HEALTH_GH": str(self.script)}

    def close(self):
        self._tmp.cleanup()


def runs_route(workflow, event, runs, rc=0, stdout=None):
    return {"match": f"workflows/{workflow}/runs?branch=main&event={event}",
            "stdout": stdout if stdout is not None else json.dumps({"workflow_runs": runs}), "rc": rc}


class Collect(unittest.TestCase):
    def setUp(self):
        self.t = Tree({
            "fresh-a.yml": wf(PUSH_PATHS, name="Fresh A"),
            "fresh-b.yml": wf(PUSH_PATHS, name="Fresh B"),
            "plain.yml": wf("push:\n  branches: [main]\n  paths: ['p/**']\n", name="Plain"),
            "canary.yml": wf("schedule:\n  - cron: '1 2 * * *'\n", name="Canary"),
            "always.yml": wf("push:\n  branches: [main]\nschedule:\n  - cron: '3 4 * * *'\n", name="Always"),
        })
        self.addCleanup(self.t.close)
        self.t.synced()
        self.plan = MH.derive(self.t.root)
        self.gh = FakeGh()
        self.addCleanup(self.gh.close)
        self.addCleanup(lambda: os.environ.pop("MAIN_HEALTH_GH", None))
        os.environ["MAIN_HEALTH_GH"] = str(self.gh.script)

    def needs(self, **results):
        out = {}
        for name in self.plan["covered"]:
            out[MH.CHECK_PREFIX + MH.stem(name)] = {"result": results.get(MH.stem(name), "success"), "outputs": {}}
        return out

    def green_routes(self):
        routes = []
        for wfname in self.plan["latestPush"]:
            routes.append(runs_route(wfname, "push", [run("success", 1)]))
        for wfname in self.plan["latestSchedule"]:
            routes.append(runs_route(wfname, "schedule", [run("success", 2, event="schedule")]))
        return routes

    def collect(self, needs=None, routes=None):
        self.gh.set(routes if routes is not None else self.green_routes())
        return MH.collect(self.plan, self.needs() if needs is None else needs, "FS-GG/.github", "999", NOW)

    # --- green ---
    def test_all_green(self):
        doc = self.collect()
        self.assertEqual(doc["findings"], [])
        self.assertEqual(doc["unknown"], [])
        self.assertEqual(doc["counts"]["fresh"], 2)
        self.assertEqual(doc["counts"]["latestRunReads"], len(self.plan["latestPush"]) + len(self.plan["latestSchedule"]))
        self.assertEqual(doc["digest"], MH.digest([]))

    # --- red ---
    def test_red_fresh_run_is_a_finding_with_failed_job_names(self):
        routes = self.green_routes() + [{"match": "runs/999/jobs", "stdout":
            json.dumps({"name": "check-fresh-a / the-job", "conclusion": "failure"}) + "\n"
            + json.dumps({"name": "check-fresh-b / other", "conclusion": "success"}) + "\n"}]
        doc = self.collect(self.needs(**{"fresh-a": "failure"}), routes)
        self.assertEqual([f["key"] for f in doc["findings"]], ["wf:fresh-a.yml"])
        finding = doc["findings"][0]
        self.assertEqual(finding["failedJobs"], ["check-fresh-a / the-job"])
        self.assertEqual(finding["evidence"][0]["event"], "fresh")
        self.assertEqual(doc["unknown"], [])

    def test_red_latest_push_run_of_an_uncovered_workflow(self):
        routes = [r for r in self.green_routes() if "plain.yml" not in r["match"]]
        routes += [runs_route("plain.yml", "push", [run("failure", 50, sha="deadbeef00123")]), {"match": "runs/50/jobs", "stdout": ""}]
        doc = self.collect(routes=routes)
        self.assertEqual([f["key"] for f in doc["findings"]], ["wf:plain.yml"])
        self.assertEqual(doc["findings"][0]["evidence"][0]["sha"], "deadbeef00")

    def test_red_scheduled_canary_is_a_finding(self):
        routes = [r for r in self.green_routes() if "workflows/canary.yml" not in r["match"]]
        routes += [runs_route("canary.yml", "schedule", [run("failure", 60, event="schedule")]), {"match": "runs/60/jobs", "stdout": ""}]
        doc = self.collect(routes=routes)
        self.assertEqual([f["key"] for f in doc["findings"]], ["wf:canary.yml"])

    def test_cancelled_latest_run_is_skipped_for_the_previous_conclusive_one(self):
        routes = [r for r in self.green_routes() if "plain.yml" not in r["match"]]
        routes += [runs_route("plain.yml", "push", [run("cancelled", 71), run("skipped", 70), run("failure", 69)]), {"match": "runs/69/jobs", "stdout": ""}]
        doc = self.collect(routes=routes)
        self.assertEqual(doc["findings"][0]["evidence"][0]["runId"], 69)
        routes[-2:] = [runs_route("plain.yml", "push", [run("cancelled", 71), run("success", 70)])]
        self.assertEqual(self.collect(routes=routes)["findings"], [])

    def test_a_green_fresh_run_supersedes_a_stale_red_push_run(self):
        routes = [r for r in self.green_routes() if "workflows/fresh-a.yml/runs?branch=main&event=push" not in r["match"]]
        routes.append(runs_route("fresh-a.yml", "push", [run("failure", 80)]))
        doc = self.collect(routes=routes)
        self.assertEqual(doc["findings"], [])
        self.assertEqual(doc["superseded"], {"fresh-a.yml": [80]})

    def test_a_red_fresh_run_is_not_masked_by_a_green_push_run(self):
        doc = self.collect(self.needs(**{"fresh-b": "failure"}), self.green_routes() + [{"match": "runs/999/jobs", "stdout": ""}])
        self.assertEqual([f["key"] for f in doc["findings"]], ["wf:fresh-b.yml"])

    def test_no_run_is_not_unknown_and_is_listed(self):
        routes = [r for r in self.green_routes() if "plain.yml" not in r["match"]] + [runs_route("plain.yml", "push", [])]
        doc = self.collect(routes=routes)
        self.assertEqual(doc["unknown"], [])
        self.assertIn("plain.yml:push", doc["noRun"])

    def test_failed_job_names_unavailable_keeps_the_verdict_red(self):
        routes = self.green_routes() + [{"match": "runs/999/jobs", "rc": 1, "stderr": "boom"}]
        doc = self.collect(self.needs(**{"fresh-a": "failure"}), routes)
        self.assertEqual([f["key"] for f in doc["findings"]], ["wf:fresh-a.yml"])
        self.assertEqual(doc["findings"][0]["failedJobs"], ["(failed job names unavailable)"])

    # --- fail closed ---
    def test_unjudged_fresh_results_are_unknown(self):
        for result in ("cancelled", "skipped", None, "weird"):
            with self.subTest(result):
                needs = self.needs()
                if result is None:
                    del needs["check-fresh-a"]
                else:
                    needs["check-fresh-a"] = {"result": result}
                doc = self.collect(needs)
                self.assertEqual([(u["workflow"], u["source"]) for u in doc["unknown"]], [("fresh-a.yml", "fresh")])

    def test_api_error_is_unknown_and_never_green(self):
        routes = [r for r in self.green_routes() if "plain.yml" not in r["match"]] + [runs_route("plain.yml", "push", [], rc=1, stdout="")]
        doc = self.collect(routes=routes)
        self.assertEqual([(u["workflow"], u["source"]) for u in doc["unknown"]], [("plain.yml", "push")])
        self.assertEqual(MH.plan_report(doc, {"number": 5, "state": "open", "body": MH.marker([])})["action"], "refuse")

    def test_unreadable_and_misshapen_responses_are_unknown(self):
        for stdout in ("not json", json.dumps({"workflow_runs": "nope"}), json.dumps([1, 2])):
            with self.subTest(stdout):
                routes = [r for r in self.green_routes() if "plain.yml" not in r["match"]] + [runs_route("plain.yml", "push", [], stdout=stdout)]
                self.assertEqual(len(self.collect(routes=routes)["unknown"]), 1)

    def test_error_text_never_carries_response_or_argv(self):
        routes = [r for r in self.green_routes() if "plain.yml" not in r["match"]] + [runs_route("plain.yml", "push", [], rc=1, stdout="SECRET-BODY")]
        doc = self.collect(routes=routes)
        self.assertNotIn("SECRET-BODY", json.dumps(doc))
        self.assertEqual(doc["unknown"][0]["why"], "gh api call failed (exit 1)")

    def test_needs_that_is_not_an_object_or_has_extras_is_no_verdict(self):
        with self.assertRaises(MH.NoVerdict):
            MH.fresh_results(self.plan, ["not", "an", "object"])
        needs = self.needs()
        needs["check-not-derived"] = {"result": "success"}
        with self.assertRaises(MH.NoVerdict):
            MH.fresh_results(self.plan, needs)

    # --- the CLI ---
    def cli(self, needs_env, extra=()):
        out = self.gh.dir / "findings.json"
        env = {**os.environ, **self.gh.env(), "MAIN_HEALTH_NEEDS": needs_env}
        proc = subprocess.run([sys.executable, "-I", str(SCRIPT), "collect", "--root", str(self.t.root), "--repo", "FS-GG/.github",
                               "--run-id", "999", "--out", str(out), "--now", "2026-10-31T08:47:00Z", *extra],
                              capture_output=True, text=True, env=env)
        return proc, out

    def test_cli_writes_findings_on_a_judged_run(self):
        self.gh.set(self.green_routes())
        proc, out = self.cli(json.dumps(self.needs()))
        self.assertEqual(proc.returncode, 0, proc.stderr)
        doc = json.loads(out.read_text())
        self.assertEqual(doc["schema"], MH.FINDINGS_SCHEMA)
        self.assertEqual(doc["evaluatedAt"], "2026-10-31T08:47:00Z")

    def test_cli_no_verdict_and_no_findings_file(self):
        self.gh.set(self.green_routes())
        for label, needs_env in (("empty", ""), ("garbage", "{oops"), ("extra", json.dumps({**self.needs(), "check-zzz": {"result": "success"}}))):
            with self.subTest(label):
                proc, out = self.cli(needs_env)
                self.assertEqual(proc.returncode, MH.NO_VERDICT, proc.stderr)
                self.assertFalse(out.exists())

    def test_cli_refuses_when_the_observer_workflow_drifted(self):
        self.gh.set(self.green_routes())
        (self.t.wf / "fresh-c.yml").write_text(wf(PUSH_PATHS))
        proc, out = self.cli(json.dumps(self.needs()))
        self.assertEqual(proc.returncode, MH.NO_VERDICT)
        self.assertIn("no longer matches", proc.stderr)
        self.assertFalse(out.exists())


# --------------------------------------------------------------------------------------------------
# report planner and writer
# --------------------------------------------------------------------------------------------------

def finding(file="a.yml", event="push", rid=1, jobs=()):
    return {"key": f"wf:{file}", "workflow": file[:-4], "file": file,
            "evidence": [{"event": event, "conclusion": "failure", "runId": rid, "url": f"https://github.com/FS-GG/.github/actions/runs/{rid}", "sha": "abc", "at": None}],
            "failedJobs": list(jobs)}


def document(findings=(), unknown=()):
    return {"schema": MH.FINDINGS_SCHEMA, "evaluatedAt": "2026-10-31T08:47:00Z", "repo": "FS-GG/.github",
            "counts": {"fresh": 3, "latestRunReads": 9}, "unknown": list(unknown), "findings": list(findings),
            "digest": MH.digest(list(findings))}


def issue(doc, state="open", number=77):
    return {"number": number, "state": state, "body": MH.render_body(doc)}


class Planner(unittest.TestCase):
    def test_nothing_red_and_no_issue_is_a_noop(self):
        self.assertEqual(MH.plan_report(document(), None)["action"], "noop")

    def test_red_with_no_issue_creates_one(self):
        plan = MH.plan_report(document([finding("a.yml")]), None)
        self.assertEqual(plan["action"], "create")
        self.assertIn("a.yml", plan["body"])
        self.assertIn(MH.MARKER_PREFIX, plan["body"])

    def test_unchanged_set_leaves_the_issue_alone_even_with_new_run_ids(self):
        before = document([finding("a.yml", rid=1), finding("b.yml", rid=2)])
        after = document([finding("b.yml", rid=222), finding("a.yml", event="fresh", rid=111, jobs=["j"])])
        plan = MH.plan_report(after, issue(before))
        self.assertEqual(plan["action"], "noop")
        self.assertNotIn("body", plan)
        self.assertNotIn("comment", plan)

    def test_changed_set_rewrites_and_comments_once_with_new_and_cleared(self):
        before = document([finding("a.yml"), finding("b.yml")])
        after = document([finding("b.yml"), finding("c.yml")])
        plan = MH.plan_report(after, issue(before))
        self.assertEqual(plan["action"], "update")
        self.assertIn("1 newly red, 1 cleared, 2 red now", plan["comment"])
        self.assertIn("newly red `wf:c.yml`", plan["comment"])
        self.assertIn("cleared `wf:a.yml`", plan["comment"])
        self.assertIn("c.yml", plan["body"])
        self.assertNotIn("a.yml", plan["body"].split("-->", 1)[1])

    def test_issue_closed_by_a_human_stays_closed_while_the_set_is_unchanged(self):
        doc = document([finding("a.yml")])
        self.assertEqual(MH.plan_report(doc, issue(doc, state="closed"))["action"], "noop")
        reopened = MH.plan_report(document([finding("a.yml"), finding("b.yml")]), issue(doc, state="closed"))
        self.assertEqual(reopened["action"], "reopen")

    def test_everything_green_closes_the_open_issue_once(self):
        red = document([finding("a.yml")])
        plan = MH.plan_report(document(), issue(red))
        self.assertEqual(plan["action"], "close")
        self.assertEqual(MH.parse_marker(plan["body"])["count"], 0)
        self.assertEqual(MH.plan_report(document(), issue(document(), state="closed"))["action"], "noop")

    def test_anything_unjudged_refuses_and_never_closes_or_rewrites(self):
        unknown = [{"workflow": "x.yml", "source": "fresh", "why": "caller job result is 'cancelled', not a verdict"}]
        red = document([finding("a.yml")])
        self.assertEqual(MH.plan_report(document(unknown=unknown), issue(red))["action"], "refuse")
        self.assertEqual(MH.plan_report(document([finding("b.yml")], unknown), issue(red))["action"], "refuse")
        self.assertEqual(MH.plan_report(document(unknown=unknown), None)["action"], "refuse")

    def test_digest_ignores_order_and_evidence(self):
        a = [finding("a.yml", rid=1), finding("b.yml", rid=2)]
        b = [finding("b.yml", event="fresh", rid=9), finding("a.yml", rid=8, jobs=["z"])]
        self.assertEqual(MH.digest(a), MH.digest(b))
        self.assertNotEqual(MH.digest(a), MH.digest(a[:1]))

    def test_marker_stays_bounded_for_a_huge_set(self):
        many = [finding(f"w{i:04d}-{'x' * 40}.yml") for i in range(600)]
        body = MH.render_body(document(many))
        marker = MH.parse_marker(body)
        self.assertEqual(marker["digest"], MH.digest(many))
        self.assertLessEqual(len(body), MH.BODY_LIMIT)
        self.assertIn("more red workflow(s) not shown", body)

    def test_body_has_no_automatic_references(self):
        body = MH.render_body(document([finding("a.yml", jobs=["check-a / j"])]))
        self.assertNotRegex(body.replace("FS-GG/.github#4730", ""), r"(?<![\w/])#\d+")
        self.assertIn("`FS-GG/.github#4730`", body)


class Report(unittest.TestCase):
    def setUp(self):
        self.gh = FakeGh()
        self.addCleanup(self.gh.close)
        self.addCleanup(lambda: os.environ.pop("MAIN_HEALTH_GH", None))
        os.environ["MAIN_HEALTH_GH"] = str(self.gh.script)

    def issues_route(self, *issues):
        return {"match": "issues?labels=main-health", "stdout": "".join(json.dumps(i) + "\n" for i in issues)}

    def cli(self, doc, *extra):
        path = self.gh.dir / "findings.json"
        path.write_text(json.dumps(doc))
        env = {**os.environ, **self.gh.env()}
        return subprocess.run([sys.executable, "-I", str(SCRIPT), "report", "--findings", str(path), "--repo", "FS-GG/.github", *extra],
                              capture_output=True, text=True, env=env)

    def test_create_writes_label_then_issue(self):
        self.gh.set([self.issues_route(), {"match": "/labels", "stdout": "{}"}, {"match": "/issues", "stdout": json.dumps({"number": 12})}])
        proc = self.cli(document([finding("a.yml")]))
        self.assertEqual(proc.returncode, 0, proc.stderr)
        self.assertIn("created #12", proc.stdout)
        sent = [json.loads(c["stdin"]) for c in self.gh.writes() if c["stdin"]]
        self.assertEqual(sent[-1]["title"], MH.REPORT_TITLE)
        self.assertEqual(sent[-1]["labels"], [MH.REPORT_LABEL])

    def test_unchanged_set_makes_no_write_at_all(self):
        doc = document([finding("a.yml")])
        existing = {"number": 7, "state": "open", "title": MH.REPORT_TITLE, "body": MH.render_body(doc)}
        self.gh.set([self.issues_route(existing)])
        proc = self.cli(doc)
        self.assertEqual(proc.returncode, 0, proc.stderr)
        self.assertIn("noop", proc.stdout)
        self.assertEqual(self.gh.writes(), [])

    def test_changed_set_patches_then_comments(self):
        old, new = document([finding("a.yml")]), document([finding("a.yml"), finding("b.yml")])
        existing = {"number": 7, "state": "open", "title": MH.REPORT_TITLE, "body": MH.render_body(old)}
        self.gh.set([self.issues_route(existing), {"match": "/labels", "stdout": "{}"}, {"match": "issues/7", "stdout": "{}"}])
        proc = self.cli(new)
        self.assertEqual(proc.returncode, 0, proc.stderr)
        writes = [c["argv"][1] for c in self.gh.writes()]
        self.assertEqual(writes[-2:], ["repos/FS-GG/.github/issues/7", "repos/FS-GG/.github/issues/7/comments"])

    def test_green_closes_with_a_comment_and_completed_reason(self):
        existing = {"number": 7, "state": "open", "title": MH.REPORT_TITLE, "body": MH.render_body(document([finding("a.yml")]))}
        self.gh.set([self.issues_route(existing), {"match": "issues/7", "stdout": "{}"}])
        proc = self.cli(document())
        self.assertEqual(proc.returncode, 0, proc.stderr)
        patch = [json.loads(c["stdin"]) for c in self.gh.writes() if "PATCH" in c["argv"]][0]
        self.assertEqual((patch["state"], patch["state_reason"]), ("closed", "completed"))

    def test_unknown_fails_the_run_and_writes_nothing(self):
        unknown = [{"workflow": "x.yml", "source": "push", "why": "gh api call failed (exit 1)"}]
        existing = {"number": 7, "state": "open", "title": MH.REPORT_TITLE, "body": MH.render_body(document([finding("a.yml")]))}
        self.gh.set([self.issues_route(existing)])
        proc = self.cli(document(unknown=unknown))
        self.assertEqual(proc.returncode, MH.NO_VERDICT)
        self.assertEqual(self.gh.writes(), [])
        self.assertIn("UNKNOWN x.yml", proc.stderr)

    def test_dry_run_writes_nothing_but_prints_the_plan(self):
        self.gh.set([self.issues_route()])
        proc = self.cli(document([finding("a.yml")]), "--dry-run")
        self.assertEqual(proc.returncode, 0, proc.stderr)
        self.assertIn('"action": "create"', proc.stdout)
        self.assertEqual(self.gh.writes(), [])

    def test_api_failure_on_the_write_leaves_the_run_red(self):
        self.gh.set([self.issues_route(), {"match": "/labels", "stdout": "{}"}, {"match": "/issues", "rc": 1, "stderr": "forbidden"}])
        proc = self.cli(document([finding("a.yml")]))
        self.assertEqual(proc.returncode, 1)
        self.assertIn("gh api call failed", proc.stderr)

    def test_listing_failure_is_red_not_a_blank_slate(self):
        self.gh.set([{"match": "issues?labels", "rc": 1, "stderr": "nope"}])
        proc = self.cli(document())
        self.assertEqual(proc.returncode, 1)

    def test_two_open_report_issues_are_refused(self):
        a = {"number": 1, "state": "open", "title": MH.REPORT_TITLE, "body": ""}
        b = {"number": 2, "state": "open", "title": MH.REPORT_TITLE, "body": ""}
        self.gh.set([self.issues_route(a, b)])
        proc = self.cli(document([finding("a.yml")]))
        self.assertEqual(proc.returncode, 1)
        self.assertEqual(self.gh.writes(), [])

    def test_prs_and_other_titles_are_not_the_report_issue(self):
        pr = {"number": 1, "state": "open", "title": MH.REPORT_TITLE, "body": "", "pull_request": {}}
        other = {"number": 2, "state": "open", "title": "something else", "body": ""}
        self.gh.set([self.issues_route(pr, other), {"match": "/labels", "stdout": "{}"}, {"match": "/issues", "stdout": json.dumps({"number": 3})}])
        proc = self.cli(document([finding("a.yml")]))
        self.assertIn("created #3", proc.stdout)

    def test_not_a_findings_document_is_no_verdict(self):
        self.gh.set([self.issues_route()])
        for bad in ({"schema": "other"}, {"schema": MH.FINDINGS_SCHEMA, "findings": []}):
            with self.subTest(bad):
                proc = self.cli(bad)
                self.assertEqual(proc.returncode, MH.NO_VERDICT)
        path = self.gh.dir / "findings.json"
        path.write_text("{garbage")
        proc = subprocess.run([sys.executable, "-I", str(SCRIPT), "report", "--findings", str(path)], capture_output=True, text=True,
                              env={**os.environ, **self.gh.env()})
        self.assertEqual(proc.returncode, MH.NO_VERDICT)


# --------------------------------------------------------------------------------------------------
# the real repository and the real observer workflow
# --------------------------------------------------------------------------------------------------

WORKFLOW = ROOT / ".github" / "workflows" / MH.OBSERVER_WORKFLOW
SHA = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}(\s+#.*)?$")


class RealRepository(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.plan = MH.derive(ROOT)
        cls.doc = yaml.safe_load(WORKFLOW.read_text())
        cls.jobs = cls.doc["jobs"]

    def test_observer_workflow_matches_the_derived_list(self):
        self.assertEqual(MH.workflow_problems(self.plan, WORKFLOW.read_text()), [])

    def test_the_named_checks_are_covered_or_read(self):
        covered = set(self.plan["covered"])
        for name in ("skill-registry-coherence.yml", "coord-github.yml", "skill-union-bundle.yml", "skill-union-selftest.yml",
                     "projections.yml"):
            self.assertIn(name, covered)
        # Not path-filtered, or needing secrets: read from their latest scheduled (and push) run instead.
        for name in ("v1-writer-census-drift.yml", "kit-published-coherence.yml", "permission-coherence.yml",
                     "skill-registry-coherence.yml"):
            self.assertIn(name, self.plan["latestSchedule"], name)
        self.assertIn("kit-published-coherence.yml", self.plan["latestPush"])
        self.assertIn("projections.yml", self.plan["latestPush"])

    def test_every_covered_workflow_declares_workflow_call_and_is_eligible(self):
        for name in self.plan["covered"]:
            doc = yaml.safe_load((ROOT / ".github" / "workflows" / name).read_text())
            self.assertIn("workflow_call", MH.on_block(doc), name)
            self.assertIn(name, self.plan["eligible"])
        self.assertEqual(self.plan["coveredButIneligible"], [])
        self.assertLessEqual(len(self.plan["covered"]), MH.MAX_UNIQUE_CALLS)

    def test_observer_and_its_own_selftest_are_not_unobserved(self):
        self.assertNotIn(MH.OBSERVER_WORKFLOW, self.plan["covered"])
        self.assertIn("main-health-observer-selftest.yml", self.plan["covered"])

    def test_triggers_are_nightly_schedule_and_dispatch_only(self):
        on = MH.on_block(self.doc)
        self.assertEqual(sorted(on), ["schedule", "workflow_dispatch"])
        self.assertEqual(len(on["schedule"]), 1)
        self.assertEqual(self.doc["concurrency"]["cancel-in-progress"], False)
        self.assertEqual(self.doc["permissions"], {"contents": "read"})

    def test_check_jobs_hold_read_scopes_only(self):
        checks = {k: v for k, v in self.jobs.items() if k.startswith(MH.CHECK_PREFIX)}
        self.assertEqual(len(checks), len(self.plan["covered"]))
        for name, job in checks.items():
            self.assertEqual(set(job), {"uses", "permissions"}, name)
            self.assertNotIn("secrets", job)
            self.assertTrue(all(level == "read" for level in job["permissions"].values()), name)
            self.assertTrue(job["uses"].startswith("./.github/workflows/"))

    def test_credential_split_collect_reads_report_writes_and_neither_does_both(self):
        collect, report = self.jobs["main-health-collect"], self.jobs["main-health-report"]
        self.assertEqual(collect["permissions"], {"contents": "read", "actions": "read"})
        self.assertEqual(report["permissions"], {"contents": "read", "issues": "write"})
        for job in (collect, report):
            self.assertFalse([v for v in job["permissions"].values() if v not in ("read", "write")])
        self.assertNotIn("issues", collect["permissions"])
        self.assertNotIn("actions", report["permissions"])
        text = WORKFLOW.read_text()
        self.assertNotIn("secrets.", text)
        self.assertNotIn("create-github-app-token", text)

    def test_unjudged_checks_cannot_reach_a_green_report(self):
        collect, report = self.jobs["main-health-collect"], self.jobs["main-health-report"]
        # Collect must run when a check is red, but not when the run was cancelled: `!cancelled()`, never `always()`.
        self.assertEqual(collect["if"].replace(" ", ""), "${{!cancelled()}}")
        self.assertEqual(collect["needs"], [MH.CHECK_PREFIX + MH.stem(n) for n in self.plan["covered"]])
        # Report waits on collect succeeding (no `if:`, so the implicit success() applies).
        self.assertEqual(report["needs"], "main-health-collect")
        self.assertNotIn("if", report)
        for job in (collect, report):
            for step in job["steps"]:
                self.assertNotIn("continue-on-error", step)
                self.assertNotIn("always()", str(step.get("if", "")))
        self.assertIn("toJSON(needs)", WORKFLOW.read_text())

    def test_actions_are_pinned_and_jobs_are_bounded(self):
        for name in ("main-health-collect", "main-health-report"):
            job = self.jobs[name]
            self.assertLessEqual(job["timeout-minutes"], 15)
            for step in job["steps"]:
                uses = step.get("uses")
                if uses and not uses.startswith("./"):
                    self.assertRegex(uses, SHA, uses)

    def test_the_selftest_covers_every_input_it_reads(self):
        selftest = yaml.safe_load((ROOT / ".github" / "workflows" / "main-health-observer-selftest.yml").read_text())
        on = MH.on_block(selftest)
        self.assertEqual(on["push"]["paths"], on["pull_request"]["paths"])
        paths = on["push"]["paths"]
        self.assertIn(".github/workflows/**", paths)  # the derivation reads every workflow
        for needed in ("scripts/main-health-observer.py", "tests/main-health-observer/**", ".github/actions/setup-policy-python/**"):
            self.assertIn(needed, paths)

    def test_plan_cli_check_passes_on_the_repository(self):
        proc = subprocess.run([sys.executable, "-I", str(SCRIPT), "plan", "--root", str(ROOT), "--check"], capture_output=True, text=True)
        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)


if __name__ == "__main__":
    unittest.main()
