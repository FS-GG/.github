#!/usr/bin/env python3
"""Offline failure controls for the read-only coherent candidate gate."""

from __future__ import annotations

import importlib.util
import pathlib
import sys
import unittest
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
MODULE = ROOT / "scripts" / "check-release-candidate-uniqueness.py"
spec = importlib.util.spec_from_file_location("candidate_uniqueness", MODULE)
assert spec and spec.loader
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class CandidateUniquenessTests(unittest.TestCase):
    def setUp(self) -> None:
        self.github = patch.object(gate, "feed_versions", return_value=["0.91.2"])
        self.nuget = patch.object(gate, "nuget_org_versions", return_value=["0.91.2"])
        self.github_read = self.github.start()
        self.nuget_read = self.nuget.start()
        self.addCleanup(self.github.stop)
        self.addCleanup(self.nuget.stop)

    def test_six_fresh_reads_are_required_for_one_coherent_candidate(self) -> None:
        rows = gate.check("0.91.3", "0.91.2", "test-token")
        self.assertEqual(len(rows), 6)
        self.assertEqual(
            [call.args[0] for call in self.github_read.call_args_list],
            list(gate.PACKAGES),
        )
        self.assertEqual(
            [call.args[0] for call in self.nuget_read.call_args_list],
            list(gate.PACKAGES),
        )

    def test_an_occupied_version_on_either_feed_refuses(self) -> None:
        for feed in (self.github_read, self.nuget_read):
            with self.subTest(feed=feed):
                feed.return_value = ["0.91.2", "0.91.3"]
                with self.assertRaisesRegex(gate.GateError, "already exists"):
                    gate.check("0.91.3", "0.91.2", "test-token")
                feed.return_value = ["0.91.2"]

    def test_a_newer_unexpected_feed_frontier_refuses(self) -> None:
        self.nuget_read.return_value = ["0.91.2", "0.91.4"]
        with self.assertRaisesRegex(gate.GateError, "not predecessor"):
            gate.check("0.91.3", "0.91.2", "test-token")

    def test_unreadable_feed_refuses(self) -> None:
        self.github_read.side_effect = gate.GateError("HTTP 403")
        with self.assertRaisesRegex(gate.GateError, "403"):
            gate.check("0.91.3", "0.91.2", "test-token")

    def test_missing_token_and_nonforward_version_refuse_before_feed_reads(self) -> None:
        for version, token in (("0.91.3", ""), ("0.91.2", "test-token"), ("0.91.3-preview", "test-token")):
            with self.subTest(version=version, token=bool(token)):
                with self.assertRaises(gate.GateError):
                    gate.check(version, "0.91.2", token)
        self.github_read.assert_not_called()
        self.nuget_read.assert_not_called()


class SuccessorRailBindingTests(unittest.TestCase):
    def test_candidate_binds_the_next_version_and_exact_predecessor(self) -> None:
        workflow = (ROOT / ".github/workflows/release-successor-candidate.yml").read_text()
        self.assertIn('test "$version" = 0.93.0', workflow)
        self.assertIn("gh release download coherent-set/v0.92.0", workflow)
        self.assertIn("--tag-source eb0f73186a07daf26e0f7bb5d4c1ee2054a8adaf", workflow)
        self.assertEqual(workflow.count("--predecessor 0.92.0"), 2)
        self.assertIn("dotnet tool install FS.GG.Coord.Cli", workflow)
        self.assertIn("skill telemetry-config discover", workflow)
        self.assertIn("test ! -e \"$store\"", workflow)
        self.assertIn("skill preflight assess", workflow)

    def test_publisher_and_journal_bind_one_unused_successor(self) -> None:
        publisher = (ROOT / "scripts/release-successor-publish.py").read_text()
        journal = (ROOT / "scripts/release_successor_journal.py").read_text()
        self.assertEqual(publisher.count('"version": "0.93.0"'), 1)
        self.assertIn('manifest["descriptor"]["version"] == "0.93.0"', publisher)
        self.assertIn('"--version", "0.93.0", "--predecessor", "0.92.0"', publisher)
        self.assertIn('REF = "refs/heads/fsgg/v2/journal/release/utel-rel-08"', journal)


if __name__ == "__main__":
    unittest.main()
