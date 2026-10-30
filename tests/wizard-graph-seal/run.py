#!/usr/bin/env python3
"""Controls for scripts/check-wizard-graph-seal.py (.github#4730, #3010).

One tree, one pinned seal, one variable at a time:

  * a current seal passes;
  * the SAME pinned seal against a tree with ONE leaf changed is stale; on a change that touches the
    Wizard release surface it FAILS, on an unrelated sealed-graph change it WARNS and passes, on a
    reports-only change it is informational;
  * the gate cannot be argued into passing by an unusable input (bad base, unreadable tree).

The transcript of each control is printed so a CI log shows the causal chain, not just a green count.
"""

from __future__ import annotations

import contextlib
import copy
import importlib.util
import io
import os
import pathlib
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/check-wizard-graph-seal.py"
spec = importlib.util.spec_from_file_location("check_wizard_graph_seal", SCRIPT)
gate = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = gate
spec.loader.exec_module(gate)
SUITE = gate.load_suite()
ADMISSION = SUITE.source_admission

RELEASE_FILE = "scripts/new_sdd_workspace_successor_admission.py"
UNRELATED_FILE = "scripts/NewSddWorkspace/Program.fs"
REPORT_FILE = "docs/reports/progressReports/2099-01-01-000000Z-control.md"


def transcript(title, text):
    print(f"--- {title}\n{text.rstrip()}", file=sys.stderr)


class Control(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tree = SUITE.working_source_tree()
        cls.sealed = ADMISSION.graph_seal(cls.tree)
        cls.drifted = copy.deepcopy(cls.tree)
        cls.drifted[UNRELATED_FILE]["sha"] = "9" * 40  # exactly one leaf differs
        cls.drifted_seal = ADMISSION.graph_seal(cls.drifted)

    def run_gate(self, tree, pinned, changed, *extra, github=False):
        out = io.StringIO()
        summary = tempfile.NamedTemporaryFile("w+", suffix=".md", delete=False)
        summary.close()
        environment = {"GITHUB_STEP_SUMMARY": summary.name}
        if github:
            environment["GITHUB_ACTIONS"] = "true"
        arguments = [] if changed is None else [a for path in changed for a in ("--changed-file", path)]
        fake = type("Suite", (), {"source_admission": ADMISSION, "working_source_tree": staticmethod(lambda: tree),
                                  "immutable_fixture_tree": staticmethod(lambda rev: tree)})
        try:
            with patch.object(ADMISSION, "KNOWN_GRAPH_SEAL", pinned), patch.dict(os.environ, environment):
                code = gate.main([*arguments, *extra], out=out, suite=fake)
            return code, out.getvalue(), pathlib.Path(summary.name).read_text()
        finally:
            os.unlink(summary.name)

    def test_the_controls_differ_in_exactly_one_leaf(self):
        self.assertNotEqual(self.sealed, self.drifted_seal)
        changed = [p for p in self.tree if self.tree[p] != self.drifted.get(p)]
        self.assertEqual(changed, [UNRELATED_FILE])

    def test_current_seal_passes_even_on_a_release_surface_change(self):
        code, out, summary = self.run_gate(self.tree, self.sealed, [RELEASE_FILE], github=True)
        transcript("current seal, release-surface change", out)
        self.assertEqual(code, 0)
        self.assertIn(f"computed {self.sealed}", out)
        self.assertIn(f"pinned   {self.sealed}", out)
        self.assertIn("verdict current", out)
        self.assertNotIn("::warning", out)
        self.assertNotIn("::error", out)
        self.assertIn("ok", summary)

    def test_stale_seal_on_a_release_surface_change_fails(self):
        for path in (RELEASE_FILE, "scripts/new_sdd_workspace_successor_execution.py",
                     "scripts/new_sdd_workspace_successor_provider.py",
                     "scripts/new-sdd-workspace-successor-publish.py",
                     "scripts/creator023-coord-dependencies.json",
                     ".github/workflows/release-new-sdd-workspace.yml",
                     ".github/workflows/release-new-sdd-workspace-successor-candidate.yml"):
            with self.subTest(path=path):
                code, out, summary = self.run_gate(self.drifted, self.sealed, [UNRELATED_FILE, path], github=True)
                self.assertEqual(code, 1)
                self.assertIn(f"computed {self.drifted_seal}", out)
                self.assertIn(f"pinned   {self.sealed}", out)
                self.assertIn("verdict stale-release (exit 1)", out)
                self.assertIn("::error title=Wizard seal is stale::", out)
                self.assertIn(path, out)
                self.assertIn("FAILED", summary)
        transcript("stale seal, release-surface change (last subtest)", out)

    def test_stale_seal_on_an_unrelated_change_warns_and_passes(self):
        code, out, summary = self.run_gate(self.drifted, self.sealed, [UNRELATED_FILE], github=True)
        transcript("stale seal, unrelated sealed-graph change", out)
        self.assertEqual(code, 0)
        self.assertIn("verdict stale-unrelated (exit 0)", out)
        self.assertIn("::warning title=Wizard seal is stale::" + gate.STALE_MESSAGE, out)
        self.assertNotIn("::error", out)
        self.assertIn(gate.STALE_MESSAGE, summary)
        self.assertIn(f"computed `{self.drifted_seal}`", summary)
        self.assertIn(f"pinned `{self.sealed}`", summary)

    def test_stale_seal_on_a_reports_only_change_is_informational(self):
        code, out, summary = self.run_gate(self.drifted, self.sealed, [REPORT_FILE], github=True)
        transcript("stale seal, progress-report-only change", out)
        self.assertEqual(code, 0)
        self.assertIn("verdict stale-not-touched (exit 0)", out)
        self.assertNotIn("::warning", out)
        self.assertNotIn("::error", out)

    def test_report_directory_is_not_blanket_exempt(self):
        # Only an admitted report (right name, plain blob) is outside the seal; a script placed there is sealed.
        code, out, _ = self.run_gate(self.drifted, self.sealed,
                                     ["docs/reports/progressReports/not-a-report.sh"], github=True)
        self.assertEqual(code, 0)
        self.assertIn("verdict stale-unrelated", out)

    def test_without_change_information_a_stale_seal_warns_and_strict_fails(self):
        code, out, _ = self.run_gate(self.drifted, self.sealed, None, github=True)
        self.assertEqual(code, 0)
        self.assertIn("verdict stale-unclassified", out)
        self.assertIn("::warning", out)
        code, out, _ = self.run_gate(self.drifted, self.sealed, None, "--strict", github=True)
        transcript("stale seal, --strict, no change information", out)
        self.assertEqual(code, 1)
        self.assertIn("::error", out)
        code, out, _ = self.run_gate(self.tree, self.sealed, None, "--strict")
        self.assertEqual(code, 0)

    def test_the_release_surface_names_are_the_ones_the_spec_lists(self):
        self.assertEqual(gate.release_paths([
            "scripts/new_sdd_workspace_successor_admission.py", "scripts/new_sdd_workspace_successor_execution.py",
            "scripts/new_sdd_workspace_successor_provider.py", "scripts/new-sdd-workspace-successor-publish.py",
            "scripts/creator021-coord-dependencies.json", "scripts/creator023-coord-dependencies.json",
            ".github/workflows/release-new-sdd-workspace.yml",
            ".github/workflows/release-new-sdd-workspace-successor-candidate.yml"]).__len__(), 8)
        self.assertEqual(gate.release_paths([
            "scripts/NewSddWorkspace/Program.fs", "scripts/creator-current-coord-dependencies.json",
            ".github/workflows/new-sdd-workspace-selftest.yml", "scripts/check-wizard-graph-seal.py",
            "tests/new-sdd-workspace-successor/run.py", "scripts/release_successor_provider.py"]), [])

    def test_the_gate_recomputes_with_the_suites_own_readers(self):
        # No second sealing algorithm: the computed value is exactly the suite's graph_seal over its tree.
        out = io.StringIO()
        with patch.dict(os.environ, {"GITHUB_STEP_SUMMARY": ""}):
            gate.main(["--changed-file", UNRELATED_FILE], out=out)
        self.assertIn(f"computed {ADMISSION.graph_seal(SUITE.working_source_tree())}", out.getvalue())
        source = SCRIPT.read_text()
        self.assertNotIn("hashlib", source)
        self.assertNotIn("sha256", source)

    def test_unusable_input_is_exit_two_never_a_pass(self):
        for base in ("-p", "--output=x", "a b", "x" * 300):
            with self.subTest(base=base):
                out = io.StringIO()
                with patch.dict(os.environ, {"GITHUB_STEP_SUMMARY": ""}):
                    self.assertEqual(gate.main([f"--base={base}"], out=out), 2, out.getvalue())
        out = io.StringIO()
        with patch.dict(os.environ, {"GITHUB_STEP_SUMMARY": ""}):
            self.assertEqual(gate.main(["--base", "0" * 40], out=out), 2)
        self.assertIn("UNUSABLE", out.getvalue())

    def test_rev_seals_an_immutable_commit_tree_by_its_full_sha(self):
        seen = []
        fake = type("Suite", (), {"source_admission": ADMISSION,
                                  "working_source_tree": staticmethod(lambda: self.fail("working tree must not be read")),
                                  "immutable_fixture_tree": staticmethod(lambda rev: seen.append(rev) or self.tree)})
        out = io.StringIO()
        with patch.dict(os.environ, {"GITHUB_STEP_SUMMARY": ""}):
            gate.main(["--rev", "HEAD", "--changed-file", UNRELATED_FILE], out=out, suite=fake)
        self.assertEqual(len(seen), 1)
        self.assertRegex(seen[0], r"[0-9a-f]{40}")
        self.assertIn(f"computed {self.sealed}", out.getvalue())
        out = io.StringIO()
        self.assertEqual(gate.main(["--rev=--output=x"], out=out, suite=fake), 2)

    def test_changed_paths_reads_a_real_git_diff(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = pathlib.Path(directory)
            def git(*args):
                subprocess.run(["git", "-C", directory, "-c", "user.name=t", "-c", "user.email=t@example.invalid",
                                "-c", "commit.gpgsign=false", *args], check=True, capture_output=True)
            git("init", "-q")
            (repo / "keep.txt").write_text("1")
            (repo / "gone.txt").write_text("1")
            (repo / "old name.txt").write_text("rename me, content must stay long enough to be detected")
            git("add", "."); git("commit", "-q", "-m", "base"); git("tag", "base")
            (repo / "keep.txt").write_text("2")
            (repo / "gone.txt").unlink()
            (repo / "old name.txt").rename(repo / "new name.txt")
            (repo / "added.txt").write_text("1")
            git("add", "-A"); git("commit", "-q", "-m", "change")
            self.assertEqual(gate.changed_paths("base", repo),
                             ["added.txt", "gone.txt", "keep.txt", "new name.txt", "old name.txt"])

    def test_the_real_script_end_to_end_on_the_real_tree(self):
        """Subprocess the shipped file on this checkout. The tree's true state decides stale or current, so the
        assertions state the rule rather than a constant: the exit code is a function of (stale, change)."""
        def run(*arguments):
            done = subprocess.run([sys.executable, str(SCRIPT), *arguments], capture_output=True, text=True,
                                  env={**os.environ, "GITHUB_ACTIONS": "true", "GITHUB_STEP_SUMMARY": ""}, timeout=120)
            transcript("real script " + " ".join(arguments), done.stdout + done.stderr)
            return done
        computed = ADMISSION.graph_seal(SUITE.working_source_tree())
        stale = computed != ADMISSION.KNOWN_GRAPH_SEAL
        release = run("--changed-file", RELEASE_FILE)
        unrelated = run("--changed-file", UNRELATED_FILE)
        for done in (release, unrelated):
            self.assertIn(f"computed {computed}", done.stdout)
            self.assertIn(f"pinned   {ADMISSION.KNOWN_GRAPH_SEAL}", done.stdout)
        self.assertEqual(release.returncode, 1 if stale else 0)
        self.assertEqual(unrelated.returncode, 0)
        self.assertEqual("::warning title=Wizard seal is stale::" in unrelated.stdout, stale)
        self.assertEqual("::error title=Wizard seal is stale" in release.stdout, stale)


if __name__ == "__main__":
    unittest.main(verbosity=2)
