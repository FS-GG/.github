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

    def test_selected_095_window_requires_the_094_frontier_on_all_six_coordinates(self) -> None:
        self.github_read.return_value = ["0.94.0"]
        self.nuget_read.return_value = ["0.94.0"]
        self.assertEqual(len(gate.check("0.95.0", "0.94.0", "test-token")), 6)
        for feed in (self.github_read, self.nuget_read):
            with self.subTest(feed=feed):
                feed.return_value = ["0.94.0", "0.95.0"]
                with self.assertRaisesRegex(gate.GateError, "already exists"):
                    gate.check("0.95.0", "0.94.0", "test-token")
                feed.return_value = ["0.94.0"]
        self.nuget_read.return_value = ["0.93.0"]
        with self.assertRaisesRegex(gate.GateError, "not predecessor"):
            gate.check("0.95.0", "0.94.0", "test-token")

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
        self.assertIn('test "$version" = 0.95.0', workflow)
        self.assertIn("gh release download coherent-set/v0.94.0", workflow)
        self.assertIn("--tag-source 337b6a1d53571b07ca8e1417e18e52546ad319a7", workflow)
        self.assertEqual(workflow.count("--predecessor 0.94.0"), 2)
        self.assertIn("--release-tag coherent-set/v0.94.0", workflow)
        for namespace in ("coherent-set", "kit", "drivers", "coord-engine"):
            self.assertIn(f"refs/tags/{namespace}/v0.95.0", workflow)
        for project in ("FS.GG.Coord.Cli", "FS.GG.Kit", "FS.GG.Drivers"):
            self.assertEqual(workflow.count(f"dotnet pack src/{project}/"), 1)
        self.assertIn("--policy-version release-successor/1", workflow)
        self.assertNotIn("--bridge-binding", workflow)
        self.assertNotIn("--bridge-qualification", workflow)
        self.assertIn("dotnet tool install FS.GG.Coord.Cli", workflow)
        self.assertIn("skill telemetry-config discover", workflow)
        self.assertIn("test ! -e \"$store\"", workflow)
        self.assertIn("skill preflight assess", workflow)

    def test_publisher_and_journal_bind_one_unused_successor(self) -> None:
        publisher = (ROOT / "scripts/release-successor-publish.py").read_text()
        journal = (ROOT / "scripts/release_successor_journal.py").read_text()
        self.assertEqual(publisher.count('"version": "0.95.0"'), 1)
        self.assertIn('manifest["descriptor"]["version"] == "0.95.0"', publisher)
        self.assertIn('"--version", "0.95.0", "--predecessor", "0.94.0"', publisher)
        self.assertIn('api.get("repos/FS-GG/.github/git/ref/tags/coherent-set/v0.95.0")', publisher)
        self.assertIn('REF = "refs/heads/fsgg/v2/journal/release/utel-rel-10"', journal)
        self.assertNotIn("utel-rel-09", journal)
        workflow = (ROOT / ".github/workflows/release-successor-publish.yml").read_text()
        self.assertIn("github.ref == 'refs/heads/main' && github.actor == 'EHotwagner'", workflow)
        self.assertIn("environment: release-successor", workflow)
        self.assertIn("default: false", workflow)
        self.assertNotIn("version:", workflow)
        self.assertIn("fresh publication requires candidate and publisher source to match", publisher)
        self.assertIn("recovery publisher does not descend from candidate source", publisher)


if __name__ == "__main__":
    unittest.main()
