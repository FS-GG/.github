#!/usr/bin/env python3
"""Offline failure controls for the read-only coherent candidate gate."""

from __future__ import annotations

import importlib.util
import pathlib
import subprocess
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

    def test_selected_096_window_requires_the_095_frontier_on_all_six_coordinates(self) -> None:
        self.github_read.return_value = ["0.95.0"]
        self.nuget_read.return_value = ["0.95.0"]
        self.assertEqual(len(gate.check("0.96.0", "0.95.0", "test-token")), 6)
        for feed in (self.github_read, self.nuget_read):
            with self.subTest(feed=feed):
                feed.return_value = ["0.95.0", "0.96.0"]
                with self.assertRaisesRegex(gate.GateError, "already exists"):
                    gate.check("0.96.0", "0.95.0", "test-token")
                feed.return_value = ["0.95.0"]
        self.nuget_read.return_value = ["0.94.0"]
        with self.assertRaisesRegex(gate.GateError, "not predecessor"):
            gate.check("0.96.0", "0.95.0", "test-token")

    def test_selected_0971_window_requires_the_097_frontier_on_all_six_coordinates(self) -> None:
        self.github_read.return_value = ["0.97.0"]
        self.nuget_read.return_value = ["0.97.0"]
        self.assertEqual(len(gate.check("0.97.1", "0.97.0", "test-token")), 6)
        for feed in (self.github_read, self.nuget_read):
            with self.subTest(feed=feed):
                feed.return_value = ["0.97.0", "0.97.1"]
                with self.assertRaisesRegex(gate.GateError, "already exists"):
                    gate.check("0.97.1", "0.97.0", "test-token")
                feed.return_value = ["0.97.0"]
        self.nuget_read.return_value = ["0.95.0"]
        with self.assertRaisesRegex(gate.GateError, "not predecessor"):
            gate.check("0.97.1", "0.97.0", "test-token")

    def test_selected_098_window_requires_the_0971_frontier_on_all_six_coordinates(self) -> None:
        self.github_read.return_value = ["0.97.1"]
        self.nuget_read.return_value = ["0.97.1"]
        self.assertEqual(len(gate.check("0.98.0", "0.97.1", "test-token")), 6)
        for feed in (self.github_read, self.nuget_read):
            with self.subTest(feed=feed):
                feed.return_value = ["0.97.1", "0.98.0"]
                with self.assertRaisesRegex(gate.GateError, "already exists"):
                    gate.check("0.98.0", "0.97.1", "test-token")
                feed.return_value = ["0.97.1"]
        self.nuget_read.return_value = ["0.97.0"]
        with self.assertRaisesRegex(gate.GateError, "not predecessor"):
            gate.check("0.98.0", "0.97.1", "test-token")

    def test_selected_0101_window_requires_0100_on_all_six_coordinates(self) -> None:
        self.github_read.return_value = ["0.100.0"]
        self.nuget_read.return_value = ["0.100.0"]
        self.assertEqual(len(gate.check("0.101.0", "0.100.0", "test-token")), 6)
        for feed in (self.github_read, self.nuget_read):
            with self.subTest(feed=feed):
                feed.return_value = ["0.100.0", "0.101.0"]
                with self.assertRaisesRegex(gate.GateError, "already exists"):
                    gate.check("0.101.0", "0.100.0", "test-token")
                feed.return_value = ["0.100.0"]
        self.nuget_read.return_value = ["0.99.0"]
        with self.assertRaisesRegex(gate.GateError, "not predecessor"):
            gate.check("0.101.0", "0.100.0", "test-token")
        self.github_read.side_effect = gate.GateError("HTTP 403")
        with self.assertRaisesRegex(gate.GateError, "403"):
            gate.check("0.101.0", "0.100.0", "test-token")

    def test_prospective_0104_probe_requires_exact_0102_frontier_on_both_feeds(self) -> None:
        self.github_read.return_value = ["0.102.0"]
        self.nuget_read.return_value = ["0.102.0"]
        rows = gate.check("0.104.0", "0.102.0", "read-token")
        self.assertEqual(len(rows), 6)
        self.assertEqual({row["latest"] for row in rows}, {"0.102.0"})
        self.assertEqual(self.github_read.call_count, 3)
        self.assertEqual(self.nuget_read.call_count, 3)
        self.nuget_read.return_value = ["0.102.0", "0.103.0"]
        with self.assertRaisesRegex(gate.GateError, "not predecessor"):
            gate.check("0.104.0", "0.102.0", "read-token")
        self.github_read.side_effect = gate.GateError("HTTP 403")
        with self.assertRaisesRegex(gate.GateError, "403"):
            gate.check("0.104.0", "0.102.0", "read-token")

    def test_selected110_requires_exact109_on_all_six_feed_coordinates(self) -> None:
        self.github_read.return_value = ["0.113.0"]
        self.nuget_read.return_value = ["0.113.0"]
        rows = gate.check("0.114.0", "0.113.0", "inert-token")
        self.assertEqual(len(rows), 6)
        for feed in (self.github_read, self.nuget_read):
            with self.subTest(feed=feed):
                feed.return_value = ["0.113.0", "0.114.0"]
                with self.assertRaisesRegex(gate.GateError, "already exists"):
                    gate.check("0.114.0", "0.113.0", "inert-token")
                feed.return_value = ["0.113.0"]
        self.nuget_read.return_value = ["0.108.0"]
        with self.assertRaisesRegex(gate.GateError, "not predecessor"):
            gate.check("0.114.0", "0.113.0", "inert-token")
        self.github_read.side_effect = gate.GateError("HTTP 403: occupancy Unknown")
        with self.assertRaisesRegex(gate.GateError, "occupancy Unknown"):
            gate.check("0.114.0", "0.113.0", "inert-token")

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


def packaged_v2_workflow_contract(workflow):
    required = (
        "python3 tests/coord-board-v2-cli-join/packaged_v2.py",
        '--installed-cli "$RUNNER_TEMP/fsgg-coord-engine/fsgg-coord-engine"',
        '--package "$PWD/artifacts/packages/FS.GG.Coord.Cli.${{ steps.identity.outputs.version }}.nupkg"',
        '--source-sha "$GITHUB_SHA" --run-id "$GITHUB_RUN_ID" --run-attempt "$GITHUB_RUN_ATTEMPT"',
        '--output "$RUNNER_TEMP/board-v2-packaged-qualification.json"',
        'name: board-v2-packaged-qualification-${{ github.sha }}-${{ github.run_id }}',
        'path: ${{ runner.temp }}/board-v2-packaged-qualification.json',
        'name: release-successor-candidate-${{ github.sha }}-${{ github.run_id }}',
        'path: artifacts/packages/',
    )
    for literal in required:
        if workflow.count(literal) != 1:
            raise ValueError("missing or duplicated fixed V2 candidate composition")
    if workflow.index(required[0]) < workflow.index("dotnet tool install FS.GG.Coord.Cli"):
        raise ValueError("packaged controls must follow actual isolated installation")
    artifact = workflow.split("      - name: Retain packaged V2 refusal qualification separately\n", 1)[1].split("      - name:", 1)[0]
    if "if: ${{ always() }}" not in artifact:
        raise ValueError("failed qualification evidence must be retained independently")
    if 'path: |' in workflow:
        raise ValueError("publisher artifact must retain its exact package directory")


class PackagedV2CompositionTests(unittest.TestCase):
    def test_actual_workflow_and_removed_identity_or_changed_custody_mutants(self):
        workflow = (ROOT / ".github/workflows/release-successor-candidate.yml").read_text()
        packaged_v2_workflow_contract(workflow)
        for original, replacement in (
            ("python3 tests/coord-board-v2-cli-join/packaged_v2.py", "true"),
            ('--source-sha "$GITHUB_SHA"', '--source-sha "unbound"'),
            ('--run-attempt "$GITHUB_RUN_ATTEMPT"', '--run-attempt "1"'),
            ('--installed-cli "$RUNNER_TEMP/fsgg-coord-engine/fsgg-coord-engine"', '--installed-cli "source-build"'),
            ('path: ${{ runner.temp }}/board-v2-packaged-qualification.json', 'path: artifacts/packages/'),
            ('path: artifacts/packages/', 'path: |'),
            ('if: ${{ always() }}', 'if: ${{ success() }}'),
        ):
            with self.subTest(original=original):
                with self.assertRaises(ValueError):
                    packaged_v2_workflow_contract(workflow.replace(original, replacement))

    def test_packaged_qualifier_inert_failure_controls(self):
        # This closed helper imports only stdlib and calls qualify with fake runners.
        # It never starts dotnet, the installed candidate, a browser or a network.
        result = subprocess.run([sys.executable, "-B", str(ROOT / "tests/coord-board-v2-cli-join/test_packaged_v2.py")],
                                capture_output=True, timeout=30, check=False)
        self.assertEqual(result.returncode, 0, result.stderr.decode())
        self.assertIn(b"Ran 18 tests", result.stderr)


class SuccessorRailBindingTests(unittest.TestCase):
    def test_prospective_feed_probe_has_only_main_read_permissions_and_no_release_effect(self) -> None:
        workflow = (ROOT / ".github/workflows/release-successor-feed-readiness.yml").read_text()
        self.assertIn("  workflow_dispatch:", workflow)
        self.assertIn("  contents: read", workflow)
        self.assertIn("  packages: read", workflow)
        self.assertEqual(workflow.count("permissions:"), 1)
        self.assertIn(
            "permissions:\n  contents: read\n  packages: read\n\nconcurrency:",
            workflow,
        )
        self.assertIn("if: github.ref == 'refs/heads/main'", workflow)
        self.assertIn("  probe:\n    name: release-successor-feed-readiness\n", workflow)
        self.assertIn("persist-credentials: false", workflow)
        self.assertIn('test "$(git ls-remote origin refs/heads/main | cut -f1)" = "$SOURCE_SHA"', workflow)
        self.assertIn("python3 tests/release-successor-candidate/run.py", workflow)
        self.assertIn("python3 scripts/check-release-candidate-uniqueness.py", workflow)
        self.assertIn("--version 0.114.0 --predecessor 0.113.0", workflow)
        for forbidden in (
            "packages: write", "contents: write", "id-token:", "environment:", "secrets.",
            "release-successor-publish.py", "release-saga.py", "dotnet pack",
            "gh release create", "git push", "oidc",
        ):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, workflow.lower())
        # Default coherent freshness remains free of artifact retention. The explicit
        # Host branch retains only its bounded already-published package observation.
        default = workflow.split("      - name: Observe the published Host 0.9 org archive once", 1)[0]
        self.assertNotIn("upload-artifact", default)
        host_spec = importlib.util.spec_from_file_location(
            "host_read_workflow_contract", ROOT / "tests/telemetry-host-published-org-read/run.py")
        host_contract = importlib.util.module_from_spec(host_spec)
        host_spec.loader.exec_module(host_contract)
        host_contract.workflow_contract(workflow)

    def test_actual_declared_predecessor_command_joins_promoted110_and_current_binding(self) -> None:
        import contextlib, io, json, re, runpy, shlex, tempfile
        from release_successor_journal import CURRENT_RELEASE
        saga = runpy.run_path(str(ROOT / "scripts/release-saga.py"))
        workflow = (ROOT / ".github/workflows/release-successor-candidate.yml").read_text()
        # Genuine promoted113 channel identity; fixture loading makes no native observation.
        receipt = {"version":"0.113.0", "sourceSha":"79db622ff4a51498e22ae5820eeb2b470fc743c5",
                   "contentId":"sha256:14c8ba3f954347278a646bb0d227cbbea820845bf5596ad3d7e7fcaaac1ab908",
                   "promotedAt":"2026-10-23T14:15:51.268419Z"}
        expected = {"version":CURRENT_RELEASE["predecessor"], "sourceSha":CURRENT_RELEASE["predecessorSource"],
                    "contentId":CURRENT_RELEASE["predecessorContentId"]}
        def consume(declared, channel):
            command = re.search(r"python3 scripts/release-saga\.py predecessor\s+(.+?)\s+> artifacts/predecessor/identity\.json", declared.replace(chr(92)+"\n", " "), re.S)
            self.assertIsNotNone(command)
            # Parse the workflow's actual arguments through the maintained CLI parser/consumer.
            argv = shlex.split(command.group(1))
            args = saga["parser"]().parse_args(["predecessor", *argv])
            with tempfile.TemporaryDirectory() as directory:
                path = pathlib.Path(directory)/"channel.json"; path.write_text(json.dumps(channel))
                args.channel = str(path)
                with contextlib.redirect_stdout(io.StringIO()) as output:
                    args.run(args)
                identity = json.loads(output.getvalue())
            self.assertEqual(identity, expected)
            self.assertIn("gh release download coherent-set/v"+identity["version"], declared)
        consume(workflow, receipt)
        old_source = "0d85830e7329a2718ee57ff0f1a16e6c751a469c"
        with self.assertRaisesRegex(ValueError, "does not match tag source"):
            consume(workflow.replace("--tag-source "+receipt["sourceSha"], "--tag-source "+old_source), receipt)
        with self.assertRaisesRegex(ValueError, "does not match release tag"):
            consume(workflow.replace("--release-tag coherent-set/v0.113.0", "--release-tag coherent-set/v0.110.0"), receipt)
        with self.assertRaises(AssertionError):
            consume(workflow, {**receipt,"contentId":"sha256:a3ff6fd1f998e32a750efe25bbb4255c5bd9d9b28ff5b21af441194d44e128ce"})

    def test_candidate_binds_the_next_version_and_exact_predecessor(self) -> None:
        workflow = (ROOT / ".github/workflows/release-successor-candidate.yml").read_text()
        self.assertIn('test "$version" = 0.114.0', workflow)
        self.assertIn("gh release download coherent-set/v0.113.0", workflow)
        from release_successor_journal import CURRENT_RELEASE
        self.assertIn("--tag-source " + CURRENT_RELEASE["predecessorSource"], workflow)
        self.assertEqual(workflow.count("--predecessor 0.113.0"), 2)
        self.assertIn("--release-tag coherent-set/v0.113.0", workflow)
        for namespace in ("coherent-set", "kit", "drivers", "coord-engine"):
            self.assertIn(f"refs/tags/{namespace}/v0.114.0", workflow)
        for project in ("FS.GG.Coord.Cli", "FS.GG.Kit", "FS.GG.Drivers"):
            self.assertEqual(workflow.count(f"dotnet pack src/{project}/"), 1)
        initial=workflow.split('      - name: Verify the stable predecessor and unused package version',1)[1].split('      - name: Test the telemetry producer',1)[0]
        retention=workflow.split('      - name: Refuse a changed release closure or newly occupied version before retention',1)[1].split('      - uses: actions/upload-artifact',1)[0]
        self.assertIn('releases/tags/coherent-set/v0.114.0',initial)
        self.assertIn('git ls-remote',initial)
        self.assertIn('python3 scripts/release_source_closure.py check --route coherent-set --source "$SOURCE_SHA"',retention)
        self.assertEqual(workflow.count('release_source_closure.py check --route coherent-set'),2)
        self.assertNotIn('git ls-remote origin refs/heads/main',workflow)
        self.assertIn('--predecessor 0.113.0',retention)
        self.assertNotIn('releases/tags/',retention)
        self.assertNotIn('refs/tags/',retention)
        self.assertIn('timeout-minutes: 45',workflow)
        self.assertIn('name: board-v2-packaged-qualification-${{ github.sha }}-${{ github.run_id }}',workflow)
        self.assertIn("--policy-version release-successor/1", workflow)
        self.assertNotIn("--bridge-binding", workflow)
        self.assertNotIn("--bridge-qualification", workflow)
        self.assertIn("dotnet tool install FS.GG.Coord.Cli", workflow)
        self.assertIn("skill telemetry-config discover", workflow)
        self.assertIn("test ! -e \"$store\"", workflow)
        self.assertIn("skill preflight assess", workflow)
        self.assertIn("FSGG_PACKAGE_REQUIRE_CAUSAL_RECEIVER=1", workflow)

    def test_packed_causal_qualifier_preserves_candidate_retention_and_requires_reopen(self) -> None:
        package = (ROOT / "tests/standalone-telemetry-package/run.sh").read_text()
        probe = (ROOT / "tests/standalone-telemetry-dashboard/store-probe.fsx").read_text()
        self.assertIn('REQUIRE_CAUSAL_RECEIVER="${FSGG_PACKAGE_REQUIRE_CAUSAL_RECEIVER:-0}"', package)
        self.assertIn('for phase in ingest reopen', package)
        self.assertIn('result["causalReceiver"]', package.replace("'causalReceiver'", '"causalReceiver"'))
        self.assertIn("required packed causal receiver qualification was completed", package)
        self.assertIn("causal receipt replay changed canonical revision", probe)
        self.assertIn("causal refusal changed canonical revision", probe)
        self.assertIn('"fsgg.telemetry.item-detail/2"', probe)
        self.assertIn('schemaVersion = 14', probe)
        self.assertNotIn("receiver-artifact", package + probe)
        self.assertNotIn("programme-resume", package + probe)

    def test_original_candidate_is_an_opt_in_profile_without_a_version_or_ref_override(self) -> None:
        workflow = (ROOT / ".github/workflows/release-successor-publish.yml").read_text()
        block = workflow.split("      original_0103_candidate:\n", 1)[1].split("      recovery_checkpoint:", 1)[0]
        self.assertIn("default: false", block)
        self.assertIn("type: boolean", block)
        self.assertIn('profile+=(--original-0103-candidate)', workflow)
        self.assertIn('profile+=(--recovery-checkpoint)', workflow)
        self.assertNotIn('profile=(--recovery-checkpoint)', workflow)
        self.assertIn('ref: ${{ github.sha }}', workflow)
        self.assertNotIn('publisher_revision:', workflow)

    def test_publisher_and_journal_bind_one_unused_successor(self) -> None:
        publisher = (ROOT / "scripts/release-successor-publish.py").read_text()
        journal = (ROOT / "scripts/release_successor_journal.py").read_text()
        self.assertIn('SUCCESSOR_VERSION = CURRENT_RELEASE["version"]', publisher)
        self.assertIn('SUCCESSOR_PREDECESSOR = CURRENT_RELEASE["predecessor"]', publisher)
        self.assertIn('SUCCESSOR_REF = CURRENT_RELEASE["logicalRef"]', publisher)
        self.assertIn('manifest["descriptor"]["version"] == version', publisher)
        self.assertIn('"--version", version, "--predecessor", predecessor', publisher)
        self.assertIn('coherent-set/v{manifest', publisher)
        self.assertIn('REF = "refs/heads/fsgg/v2/journal/release/utel-rel-19"', journal)
        self.assertNotIn("utel-rel-10", journal)
        self.assertNotIn("utel-rel-13", journal)
        self.assertNotIn("utel-rel-14", journal)
        self.assertNotIn("utel-rel-15", journal)
        self.assertNotIn("utel-rel-16", journal)
        workflow = (ROOT / ".github/workflows/release-successor-publish.yml").read_text()
        self.assertIn("github.ref == 'refs/heads/main' && github.actor == 'EHotwagner'", workflow)
        self.assertIn("environment: release-successor", workflow)
        self.assertIn("default: false", workflow)
        self.assertNotIn("version:", workflow)
        self.assertIn("fresh publication requires candidate and publisher source to match", publisher)
        self.assertIn("recovery publisher does not descend from candidate source", publisher)


if __name__ == "__main__":
    unittest.main()
