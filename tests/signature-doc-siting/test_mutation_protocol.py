#!/usr/bin/env python3
"""Focused current fixture ABI preflight; never launches the real fixture or sweep."""
from __future__ import annotations

import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("mutation_protocol_subject", HERE / "mutants.py")
subject = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = subject
spec.loader.exec_module(subject)
# These are the ACTUAL shell flag/finalizer/assertion/process/grep branches. No synthetic
# replacement finalizer can prove them. Stop before any corpus construction or real-tree scan.
PREFIX = (HERE / "run.sh").read_text().split("# ---- a synthetic tree", 1)[0]


def record(mode="full", complete=1, passed=1, failed=0, status="passed"):
    return f"fixture-result-v1 mode={mode} complete={complete} passed={passed} failed={failed} status={status}\n"


class ProtocolTests(unittest.TestCase):
    def test_only_consistent_actual_exit_and_record_qualify(self):
        cases = [(0, record(), False, "PASS"),
                 (1, record(passed=0, failed=2, status="assertion-failed"), False, "ASSERTION_FAILURE"),
                 (1, record("first-failure", 0, 0, 1, "assertion-failed"), True, "ASSERTION_FAILURE"),
                 (0, record("first-failure"), True, "PASS")]
        for rc, text, mode, expected in cases:
            with self.subTest(rc=rc, text=text):
                self.assertEqual(expected, subject.classify_fixture(rc, text, stop_on_failure=mode).disposition)

    def test_ambiguous_missing_signal_reserved_or_inconsistent_is_unknown(self):
        cases = [(1, ""), (-9, record(failed=1, status="assertion-failed")),
                 (124, record(failed=1, status="assertion-failed")),
                 (126, record(failed=1, status="assertion-failed")),
                 (127, record(failed=1, status="assertion-failed")),
                 (0, record(complete=0)), (1, record()), (0, record(failed=1)),
                 (0, record() * 2), (0, record() + "conflicting tail\n"),
                 (0, record().replace("passed=1", "passed=-1")),
                 (0, record().replace("passed=1", "passed=0001")),
                 (0, record(passed=0)), (70, record(status="infrastructure-error")),
                 (1, record("first-failure", 1, 0, 1, "assertion-failed")),
                 (1, record("first-failure", 0, 0, 2, "assertion-failed"))]
        for rc, text in cases:
            with self.subTest(rc=rc, text=text):
                self.assertEqual("INCONCLUSIVE", subject.classify_fixture(rc, text).disposition)
                self.assertEqual("INCONCLUSIVE", subject.classify_fixture(rc, text, stop_on_failure=True).disposition)

    def shell(self, body, *, stop=False, injected="", arguments=None):
        with tempfile.TemporaryDirectory(prefix="mutation-protocol-") as tmp:
            args = ["--python", sys.executable]
            if stop:
                args.append("--mutation-stop-on-failure")
            if arguments is not None:
                args = arguments
            proc = subprocess.run(["/bin/bash", "-c", injected + PREFIX + body, "fixture", *args],
                                  cwd=HERE, env={**os.environ, "TMPDIR": tmp},
                                  capture_output=True, text=True, timeout=10)
        result = subject.classify_fixture(proc.returncode, proc.stdout + proc.stderr, stop_on_failure=stop)
        return proc, result

    def test_actual_first_failure_stops_and_full_failure_keeps_independent_assertions(self):
        body = '\nok before\nbad "same first failure"\nok later\nfinalize 1 assertion-failed 1\n'
        full, result = self.shell(body)
        self.assertEqual("ASSERTION_FAILURE", result.disposition)
        self.assertTrue(result.complete)
        self.assertIn("PASS  later", full.stdout)
        early, result = self.shell(body, stop=True)
        self.assertEqual("ASSERTION_FAILURE", result.disposition)
        self.assertFalse(result.complete)
        self.assertEqual(1, result.failed)
        self.assertIn("FAIL  same first failure", early.stdout)
        self.assertNotIn("PASS  later", early.stdout)

    def test_success_modes_execute_every_assertion(self):
        for stop in (False, True):
            proc, result = self.shell('\nok first\nok later\nfinalize 1 passed 0\n', stop=stop)
            self.assertEqual("PASS", result.disposition)
            self.assertTrue(result.complete)
            self.assertEqual(2, result.passed)
            self.assertIn("PASS  later", proc.stdout)

    def test_invalid_flag_refuses_before_workload(self):
        proc, result = self.shell('\necho WORKLOAD\n', arguments=["--unknown"])
        self.assertEqual(64, proc.returncode)
        self.assertNotIn("WORKLOAD", proc.stdout)
        self.assertEqual("INCONCLUSIVE", result.disposition)

    def test_setup_grep_cleanup_and_missing_interpreter_never_kill(self):
        cases = [('mktemp() { return 1; }\n', '\nok unreachable\n'),
                 ('grep() { return 2; }\n', '\nif matches x x; then ok yes; else bad wrong; fi\n'),
                 ('rm() { return 1; }\n', '\nok yes\nfinalize 1 passed 0\n'),
                 ('', '\nPYTHON=/missing/interpreter\nrun_gate unused\nbad unreachable\n')]
        for injected, body in cases:
            with self.subTest(injected=injected, body=body):
                _, result = self.shell(body, stop=True, injected=injected)
                self.assertEqual("INCONCLUSIVE", result.disposition)

    def test_ordinary_gate_exit_mutants_still_reach_actual_assertion(self):
        # Actual run_gate/check_process/must_exit/bad; ordinary rc4 is an exit-code mutant.
        body = '\nGATE="$WORK/exit-four.py"\nprintf "raise SystemExit(4)\\n" > "$GATE"\nmust_exit changed-exit 0 "" unused\n'
        proc, result = self.shell(body, stop=True)
        self.assertEqual("ASSERTION_FAILURE", result.disposition)
        self.assertIn("wanted exit 0, got 4", proc.stdout)

    def test_actual_runner_source_drift_or_unavailable_launch_is_inconclusive(self):
        with tempfile.TemporaryDirectory(prefix="mutation-protocol-runner-") as tmp:
            gate = Path(tmp) / subject.GATE_REL
            fixture = Path(tmp) / subject.FIXTURE_REL
            gate.parent.mkdir(parents=True)
            fixture.parent.mkdir(parents=True)
            # This controlled fixture is ONLY a custody negative, never evidence of a kill.
            fixture.write_text('printf changed > "' + str(gate) + '"\nprintf "' + record().replace('\n', '\\n') + '"\n')
            result = subject.run_fixture(tmp, "original")
            self.assertEqual("INCONCLUSIVE", result.disposition)
            self.assertIn("changed underneath", result.diagnostic)
            fixture.unlink()
            result = subject.run_fixture(tmp, "original")
            self.assertEqual("INCONCLUSIVE", result.disposition)


if __name__ == "__main__":
    unittest.main()
