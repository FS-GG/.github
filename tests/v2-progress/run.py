#!/usr/bin/env python3
"""Inert fixtures execute the maintained collector main; --render also exercises actual FSI."""
import datetime as dt
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
RENDER = "--render" in sys.argv
sys.argv = [arg for arg in sys.argv if arg != "--render"]
UTC = dt.timezone.utc


class OfflineTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.sessions = self.root / "sessions"
        self.sessions.mkdir()
        self.now = dt.datetime.now(UTC)
        self.started = self.now - dt.timedelta(minutes=25)
        self.meta = self.root / "metadata.json"
        self.out = self.root / "snapshot.json"
        self.sentinel = self.root / "receiver-invoked"
        receiver = self.root / "fdev-telemetry"
        receiver.write_text(f"#!/bin/sh\ntouch '{self.sentinel}'\nexit 97\n")
        receiver.chmod(0o700)
        lanes = [{"id": "root", "role": "orchestrator", "reservation": "general"},
                 {"id": "worker", "role": "worker", "reservation": "direct-v2"}]
        for lane in lanes:
            lane.update(currentWork="inert fixture", state="active", launchEvidenceId="explicit-fixture")
        self.metadata = {"metadataObservedAt": self.now.isoformat(), "roadmapHead": "a" * 40,
                         "lanes": lanes, "declaredCounts": {"totalLanes": 2, "activeLanes": 2,
                         "reservedDirectV2": 1, "activeReservedDirectV2": 1, "gpt6Sol": 2,
                         "prs": 0, "evidence": 0}, "workstreams": [], "telemetry": {
                         "workspaceId": "fixture", "health": {"authenticated": True, "ready": True},
                         "workspace": {"pending": 0}}, "protectedHolds": ["Original Unknown retained"],
                         "checks": [], "risks": [], "nextActions": [], "completions": []}

    def source(self, values=(0, 50, 50, 80), tail="", parent=None):
        events = [{"timestamp": self.started.isoformat(), "type": "session_meta",
                   "payload": {"id": "root", "parent_thread_id": parent}}]
        for i, total in enumerate(values):
            events.append({"timestamp": (self.started + dt.timedelta(minutes=i * 6)).isoformat(),
                           "type": "event_msg", "payload": {"type": "token_count", "info": {
                           "total_token_usage": {"input_tokens": total, "cached_input_tokens": 0,
                           "output_tokens": 0, "total_tokens": total}}}})
        (self.sessions / "root.jsonl").write_text("".join(json.dumps(x) + "\n" for x in events) + tail)

    def collect(self):
        self.meta.write_text(json.dumps(self.metadata))
        env = dict(os.environ, PATH=str(self.root) + os.pathsep + os.environ["PATH"])
        run = subprocess.run([sys.executable, str(ROOT / "scripts/v2-progress/collect-local.py"),
            "--metadata", str(self.meta), "--output", str(self.out), "--root-session", "root",
            "--repository", "FS-GG/.github", "--sessions-dir", str(self.sessions)],
            env=env, capture_output=True, text=True, timeout=10)
        self.assertEqual(0, run.returncode, run.stderr)
        self.assertFalse(self.sentinel.exists())
        return json.loads(self.out.read_text())

    def test_offline_main_discards_metadata_health_and_keeps_cumulative_samples(self):
        self.source()
        report = self.collect()
        self.assertIsNone(report["telemetry"]["health"])
        self.assertIsNone(report["telemetry"]["workspace"])
        samples = report["localCounterWindow"]["sessions"][0]["events"]
        self.assertEqual([0, 50, 50, 80], [e["counters"]["totalTokens"] for e in samples])
        self.assertNotIn(str(self.sessions), self.out.read_text())
        self.assertNotIn("CollectorVerified", self.out.read_text())

    def test_partial_or_malformed_tail_is_unknown_without_losing_observed_samples(self):
        for tail in ['{"raw_private_prompt":', 'not-json\n']:
            self.source(tail=tail)
            report = self.collect()
            self.assertIsNone(report["localCounterWindow"])
            self.assertEqual(4, report["localCounterCoverage"]["observedSamples"])
            self.assertTrue(report["localCounterCoverage"]["gaps"])
            self.assertNotIn("raw_private_prompt", self.out.read_text())

    def test_reset_missing_baseline_and_unproved_root_parent_do_not_make_totals(self):
        for values, parent in [((0, 50, 10), None), ((10, 50), None), ((0, 50), "missing")]:
            self.source(values=values, parent=parent)
            report = self.collect()
            self.assertIsNone(report["localCounterWindow"])
            self.assertTrue(report["localCounterCoverage"]["gaps"])

    def test_missing_and_young_session_are_unknown_not_zero_usage(self):
        report = self.collect()
        self.assertIsNone(report["localCounterWindow"])
        self.assertIn("root session metadata unavailable", report["localCounterCoverage"]["gaps"])
        self.started = self.now - dt.timedelta(minutes=2)
        self.source(values=(0,))
        report = self.collect()
        self.assertIsNone(report["localCounterWindow"])
        self.assertEqual(1, report["localCounterCoverage"]["observedSamples"])

    def test_modern_only_records_have_unknown_diagnostic_totals(self):
        self.source(values=())
        path = self.sessions / "root.jsonl"
        with path.open("a") as stream:
            stream.write(json.dumps({"type": "token_usage_record", "payload": {
                "thread_id": "root", "response_id": "response", "usage": {"total_tokens": 547}}}) + "\n")
        report = self.collect()
        self.assertIsNone(report["localCounterWindow"])
        self.assertFalse(report["localCounterCoverage"]["completeDiagnostic"])

    def test_oversized_line_and_duplicate_identity_leave_bounded_gaps(self):
        self.source()
        path = self.sessions / "root.jsonl"
        with path.open("a") as stream:
            stream.write('x' * (1024 * 1024 + 1) + "\n")
        report = self.collect()
        self.assertIsNone(report["localCounterWindow"])
        self.assertLess(self.out.stat().st_size, 20000)
        self.assertNotIn('xxxxx', self.out.read_text())
        self.source()
        (self.sessions / "duplicate.jsonl").write_bytes(path.read_bytes())
        report = self.collect()
        self.assertIsNone(report["localCounterWindow"])

    @unittest.skipUnless(RENDER, "actual FSI qualification selected by hosted workflow only")
    def test_actual_fsharp_adapter_offline_and_strict_health(self):
        self.source()
        report = self.collect()
        command = ["dotnet", "fsi", str(ROOT / "scripts/v2-progress/render.fsx"), str(self.out)]
        rendered = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=45)
        self.assertEqual(0, rendered.returncode, rendered.stderr)
        self.assertIn("Current queue: Unknown", rendered.stdout)
        self.assertNotIn("pending=0", rendered.stdout)
        self.assertIn("no end-to-end capture acceptance claimed", rendered.stdout)
        self.assertIn("local JSONL diagnostic", rendered.stdout)
        self.assertIn("total=80", rendered.stdout)  # cumulative snapshots were not summed
        report["telemetry"] = {"workspaceId": "fixture", "health": {
            "authenticated": True, "ready": True, "collectorVerified": True,
            "observedAt": report["asOf"], "evidenceId": "fixture-health"}, "workspace": {
            "configured": True, "collectorVerified": True, "observedAt": report["asOf"],
            "evidenceId": "fixture-workspace", "pending": 0, "pendingUnacknowledged": 0,
            "unacknowledgedLossy": False}}
        self.out.write_text(json.dumps(report))
        healthy = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=45)
        self.assertEqual(0, healthy.returncode, healthy.stderr)
        self.assertIn("Telemetry readiness: 🟢 Active/Healthy", healthy.stdout)
        report["telemetry"]["health"]["observedAt"] = (self.now - dt.timedelta(hours=1)).isoformat()
        self.out.write_text(json.dumps(report))
        stale = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=45)
        self.assertNotEqual(0, stale.returncode)
        self.assertIn("fresh authenticated health", stale.stderr)


if __name__ == "__main__":
    unittest.main()
