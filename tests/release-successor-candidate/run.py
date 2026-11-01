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
import release_route  # noqa: E402  (scripts/ is on sys.path above)

# The release the source declares and a synthetic promoted predecessor below it. Derived, never pinned: a
# version bump changes both and no test here names a real version (#5156).
CURRENT = release_route.props_version()


def below(version: str) -> str:
    major, minor, patch = release_route.parts(version)
    return f"{major}.{minor}.{patch - 1}" if patch else f"{major}.{minor - 1}.0" if minor else f"{major - 1}.0.0"


PREVIOUS = below(CURRENT)


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

    def test_current_version_requires_its_predecessor_on_all_six_feed_coordinates(self) -> None:
        self.github_read.return_value = [PREVIOUS]
        self.nuget_read.return_value = [PREVIOUS]
        rows = gate.check(CURRENT, PREVIOUS, "inert-token")
        self.assertEqual(len(rows), 6)
        for feed in (self.github_read, self.nuget_read):
            with self.subTest(feed=feed):
                feed.return_value = [PREVIOUS, CURRENT]
                with self.assertRaisesRegex(gate.GateError, "already exists"):
                    gate.check(CURRENT, PREVIOUS, "inert-token")
                feed.return_value = [PREVIOUS]
        self.nuget_read.return_value = ["0.108.0"]
        with self.assertRaisesRegex(gate.GateError, "not predecessor"):
            gate.check(CURRENT, PREVIOUS, "inert-token")
        self.github_read.side_effect = gate.GateError("HTTP 403: occupancy Unknown")
        with self.assertRaisesRegex(gate.GateError, "occupancy Unknown"):
            gate.check(CURRENT, PREVIOUS, "inert-token")

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


class RouteDerivationTests(unittest.TestCase):
    """scripts/release_route.py: every value a rebind PR used to hand-edit, derived and refused (#5156)."""
    channel = {"version": PREVIOUS, "sourceSha": "3" * 40, "contentId": "sha256:" + "3" * 64,
               "promotedAt": "2026-10-31T15:54:06Z"}

    def test_version_is_the_single_coherent_set_property(self) -> None:
        props = (ROOT / "Directory.Build.props").read_text()
        self.assertEqual(release_route.coherent_version(props), CURRENT)
        for text, reason in (("", "0 times"), ("<FsggCoherentSetVersion>1.2.3</FsggCoherentSetVersion>" * 2, "2 times"),
                             ("<FsggCoherentSetVersion></FsggCoherentSetVersion>", "canonical stable"),
                             ("<FsggCoherentSetVersion>0.115.0-rc.1</FsggCoherentSetVersion>", "canonical stable"),
                             ("<FsggCoherentSetVersion>0.0115.0</FsggCoherentSetVersion>", "canonical stable")):
            with self.subTest(text=text), self.assertRaisesRegex(release_route.RouteRefused, reason):
                release_route.coherent_version(text)

    def test_journal_namespace_is_unique_per_version_and_keeps_hand_allocations(self) -> None:
        names = [release_route.journal_name(f"0.{minor}.{patch}") for minor in range(100, 130) for patch in range(3)]
        self.assertEqual(len(names), len(set(names)))
        self.assertEqual(release_route.journal_name("0.114.1"), "coord1141-board-reader-20261101-01")
        self.assertEqual(release_route.journal_name("0.114.0"), "coord114-enrollment-context-20261031-01")
        self.assertEqual(release_route.journal_name("0.114.2"), "coherent-set-v0.114.2")
        self.assertEqual(len(set(release_route.ALLOCATED.values())), len(release_route.ALLOCATED))
        value = release_route.current("0.115.0")
        self.assertEqual(value, {"version": "0.115.0",
                                 "logicalRef": "refs/heads/fsgg/v2/journal/release/coherent-set-v0.115.0",
                                 "directory": "state/releases/coherent-set-v0.115.0"})
        # The journal module derives its current release from the same props, at import.
        from release_successor_journal import CURRENT_RELEASE
        self.assertEqual({key: CURRENT_RELEASE[key] for key in ("version", "logicalRef", "directory")},
                         release_route.current(CURRENT))

    def test_predecessor_is_the_newest_promoted_coherent_release_below_the_version(self) -> None:
        def row(tag, draft=False, prerelease=False):
            return {"tag_name": tag, "draft": draft, "prerelease": prerelease}
        rows = [row("coherent-set/v0.113.0"), row("coherent-set/v0.114.0"), row("coherent-set/v0.114.1", draft=True),
                row("coherent-set/v0.114.5", prerelease=True), row("telemetry-host/v9.0.0"), row("kit/v0.200.0")]
        self.assertEqual(release_route.select_predecessor(rows, "0.114.1"), "0.114.0")
        for extra, reason in ((row("coherent-set/v0.114.1"), "not older than"), (row("coherent-set/v0.115.0"), "not older than")):
            with self.subTest(extra=extra), self.assertRaisesRegex(release_route.RouteRefused, reason):
                release_route.select_predecessor(rows + [extra], "0.114.1")
        # A re-run of the version's own (already promoted) release may still find its predecessor.
        self.assertEqual(release_route.select_predecessor(rows + [row("coherent-set/v0.114.1")], "0.114.1",
                                                          allow_current=True), "0.114.0")
        for bad, reason in (({}, "not a JSON array"), ([1], "not an object"), ([], "no promoted"),
                            ([row("coherent-set/vnext")], "canonical stable")):
            with self.subTest(bad=bad), self.assertRaisesRegex(release_route.RouteRefused, reason):
                release_route.select_predecessor(bad, "0.114.1")

    def test_route_keeps_every_fail_closed_check(self) -> None:
        import json
        identity = release_route.channel_identity(json.dumps(self.channel))
        derived = release_route.route(CURRENT, identity, "3" * 40)
        self.assertEqual(derived["predecessorContentId"], self.channel["contentId"])
        self.assertNotEqual(derived["directory"], release_route.current(PREVIOUS)["directory"])
        mutants = [
            ({**self.channel, "version": CURRENT}, "3" * 40, "not strictly greater"),
            ({**self.channel, "version": "9.0.0"}, "3" * 40, "not strictly greater"),
            (self.channel, "4" * 40, "is not its tag's commit"),
            (self.channel, "3" * 39, "tag commit is malformed"),
        ]
        for channel, tag, reason in mutants:
            with self.subTest(reason=reason), self.assertRaisesRegex(release_route.RouteRefused, reason):
                release_route.route(CURRENT, release_route.channel_identity(json.dumps(channel)), tag)
        for key, value, reason in (("contentId", "sha256:x", "contentId"), ("sourceSha", "3" * 41, "sourceSha"),
                                   ("promotedAt", "2026-10-31", "promotedAt"), ("version", "0.114", "canonical")):
            with self.subTest(key=key), self.assertRaisesRegex(release_route.RouteRefused, reason):
                release_route.channel_identity(json.dumps({**self.channel, key: value}))

    def test_current_release_refuses_an_unbound_or_rebound_predecessor(self) -> None:
        value = release_route.CurrentRelease(release_route.current(CURRENT))
        with self.assertRaisesRegex(release_route.RouteRefused, "not bound"):
            value["predecessor"]
        self.assertIsNone(value.get("predecessor"))
        with self.assertRaises(KeyError):
            value["unknown"]
        identity = {"version": PREVIOUS, "sourceSha": "3" * 40, "contentId": "sha256:" + "3" * 64}
        for bad, reason in (({**identity, "version": CURRENT}, "not strictly greater"),
                            ({**identity, "sourceSha": "x"}, "source is malformed"),
                            ({**identity, "contentId": "sha256:"}, "content id is malformed")):
            with self.subTest(reason=reason), self.assertRaisesRegex(release_route.RouteRefused, reason):
                release_route.CurrentRelease(release_route.current(CURRENT)).bind(bad)
        value.bind(identity)
        value.bind(identity)  # idempotent for the same predecessor
        self.assertEqual(value["predecessorSource"], "3" * 40)
        with self.assertRaisesRegex(release_route.RouteRefused, "different predecessor"):
            value.bind({**identity, "contentId": "sha256:" + "4" * 64})

    def test_the_next_release_needs_only_the_version_bump(self) -> None:
        """Proof by construction: bump the props and every route value follows, with no other edit."""
        import json, tempfile
        major, minor, patch = release_route.parts(CURRENT)
        for bumped in (f"{major}.{minor}.{patch + 1}", f"{major}.{minor + 1}.0"):
            with self.subTest(bumped=bumped), tempfile.TemporaryDirectory() as directory:
                props = pathlib.Path(directory) / "Directory.Build.props"
                props.write_text((ROOT / "Directory.Build.props").read_text().replace(
                    f"<FsggCoherentSetVersion>{CURRENT}</FsggCoherentSetVersion>",
                    f"<FsggCoherentSetVersion>{bumped}</FsggCoherentSetVersion>"))
                releases = [{"tag_name": f"coherent-set/v{CURRENT}", "draft": False, "prerelease": False}]
                version = release_route.props_version(props)
                self.assertEqual(version, bumped)
                self.assertEqual(release_route.select_predecessor(releases, version), CURRENT)
                channel = {"version": CURRENT, "sourceSha": "5" * 40, "contentId": "sha256:" + "5" * 64,
                           "promotedAt": "2026-11-01T05:13:40Z"}
                derived = release_route.route(version, release_route.channel_identity(json.dumps(channel)), "5" * 40)
                self.assertEqual(derived["logicalRef"], f"refs/heads/fsgg/v2/journal/release/coherent-set-v{bumped}")
                self.assertNotEqual(derived["directory"], release_route.current(CURRENT)["directory"])
                self.assertEqual(release_route.current_release(props)["version"], bumped)


VERSION_LITERAL = __import__("re").compile(r"(?<![\w.])0\.(?:9\d|1\d\d)\.\d+(?![\w.])")


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
        # Derived, not rebound (#5156): the coherent branch reads the version bump and the promoted
        # predecessor, and the workflow names no coherent version at all.
        self.assertIn('version="$(python3 scripts/release_route.py version)"', workflow)
        self.assertIn('predecessor="$(python3 scripts/release_route.py predecessor', workflow)
        self.assertIn('--version "$version" --predecessor "$predecessor"', workflow)
        self.assertIsNone(VERSION_LITERAL.search(workflow.replace("Host 0.9", "")))
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

    def test_actual_declared_predecessor_command_consumes_the_derived_route(self) -> None:
        """Run the workflow's own predecessor commands over fixture reads, then refuse every mutant."""
        import contextlib, io, json, re, runpy, shlex, tempfile
        saga = runpy.run_path(str(ROOT / "scripts/release-saga.py"))
        workflow = (ROOT / ".github/workflows/release-successor-candidate.yml").read_text()
        step = workflow.split("      - name: Verify the stable predecessor and unused package version", 1)[1] \
            .split("      - name: Test the telemetry producer", 1)[0].replace(chr(92) + "\n", " ")
        source = "3" * 40
        channel = {"version": PREVIOUS, "sourceSha": source, "contentId": "sha256:" + "3" * 64,
                   "promotedAt": "2026-10-31T15:54:06.802607Z"}
        releases = [{"tag_name": f"coherent-set/v{PREVIOUS}", "draft": False, "prerelease": False},
                    {"tag_name": f"coherent-set/v{CURRENT}", "draft": True, "prerelease": False},
                    {"tag_name": "telemetry-host/v9.9.9", "draft": False, "prerelease": False},
                    {"tag_name": "coherent-set/v0.1.0", "draft": False, "prerelease": False}]

        def run(declared, *, props=None, rows=releases, receipt=channel, tag=source):
            """The step's derivations, in order, with the reads it makes replaced by fixture bytes."""
            with tempfile.TemporaryDirectory() as directory:
                path = pathlib.Path(directory)
                (path / "props").write_text(props if props is not None else
                                            f"<FsggCoherentSetVersion>{CURRENT}</FsggCoherentSetVersion>")
                (path / "releases.json").write_text(json.dumps(rows))
                (path / "channel.json").write_text(json.dumps(receipt))
                predecessor_cmd = re.search(r'predecessor="\$\(python3 scripts/release_route\.py (predecessor .+?)\)"', declared)
                self.assertIsNotNone(predecessor_cmd)
                env = {"VERSION": release_route.props_version(path / "props"), "RUNNER_TEMP": directory}
                argv = [arg.replace("$RUNNER_TEMP", directory).replace("$VERSION", env["VERSION"])
                        for arg in shlex.split(predecessor_cmd.group(1))] + ["--props", str(path / "props")]
                with contextlib.redirect_stdout(io.StringIO()) as out, contextlib.redirect_stderr(io.StringIO()) as err:
                    code = release_route.main(argv)
                if code:
                    raise release_route.RouteRefused(err.getvalue())
                predecessor = out.getvalue().strip()
                self.assertIn('gh release download "coherent-set/v$predecessor"', declared)
                self.assertIn('git rev-parse --verify "refs/tags/coherent-set/v$predecessor^{commit}"', declared)
                saga_cmd = re.search(r"python3 scripts/release-saga\.py predecessor\s+(.+?)\s+> artifacts/predecessor/identity\.json", declared, re.S)
                self.assertIsNotNone(saga_cmd)
                values = {"$predecessor": predecessor, "$tag_source": tag}
                saga_argv = []
                for arg in shlex.split(saga_cmd.group(1)):
                    for name, value in values.items():
                        arg = arg.replace(name, value)
                    saga_argv.append(arg)
                args = saga["parser"]().parse_args(["predecessor", *saga_argv])
                args.channel = str(path / "channel.json")
                with contextlib.redirect_stdout(io.StringIO()) as output:
                    args.run(args)
                identity = json.loads(output.getvalue())
                route_cmd = re.search(r"python3 scripts/release_route\.py (route .+?)\s+> \"\$RUNNER_TEMP/route\.json\"", declared, re.S)
                self.assertIsNotNone(route_cmd)
                route_argv = []
                for arg in shlex.split(route_cmd.group(1)):
                    arg = arg.replace("$VERSION", env["VERSION"]).replace("$tag_source", tag)
                    route_argv.append(str(path / "channel.json") if arg.endswith("stable-channel.json") else arg)
                with contextlib.redirect_stdout(io.StringIO()) as out, contextlib.redirect_stderr(io.StringIO()) as err:
                    code = release_route.main(route_argv + ["--props", str(path / "props")])
                if code:
                    raise release_route.RouteRefused(err.getvalue())
                return predecessor, identity, json.loads(out.getvalue())

        predecessor, identity, derived = run(step)
        self.assertEqual(predecessor, PREVIOUS)
        self.assertEqual(identity, {key: channel[key] for key in ("version", "sourceSha", "contentId")})
        self.assertEqual(derived, {**release_route.current(CURRENT), "predecessor": PREVIOUS,
                                   "predecessorSource": source, "predecessorContentId": channel["contentId"]})
        # Mutants: each must be refused for the reason it introduces.
        bump = f"<FsggCoherentSetVersion>{PREVIOUS}</FsggCoherentSetVersion>"
        with self.assertRaisesRegex(release_route.RouteRefused, "not older than"):
            run(step, props=bump)  # the version is not strictly greater than the promoted predecessor
        with self.assertRaisesRegex(release_route.RouteRefused, "not older than"):
            run(step, rows=releases + [{"tag_name": f"coherent-set/v{CURRENT}", "draft": False, "prerelease": False}])
        with self.assertRaisesRegex(release_route.RouteRefused, "declares FsggCoherentSetVersion 2 times"):
            run(step, props=f"<FsggCoherentSetVersion>{CURRENT}</FsggCoherentSetVersion>" * 2)
        with self.assertRaisesRegex(release_route.RouteRefused, "no promoted coherent-set release"):
            run(step, rows=[row for row in releases if row["tag_name"] != f"coherent-set/v{PREVIOUS}"
                            and row["tag_name"] != "coherent-set/v0.1.0"])
        with self.assertRaisesRegex(ValueError, "does not match tag source"):
            run(step, tag="0d85830e7329a2718ee57ff0f1a16e6c751a469c")
        with self.assertRaisesRegex(ValueError, "does not match release tag"):
            run(step, receipt={**channel, "version": "0.1.0"})
        with self.assertRaises(AssertionError):
            run(step.replace('"coherent-set/v$predecessor"', '"coherent-set/v0.1.0"', 1))  # a literal predecessor
        older = dict(releases[0], tag_name="coherent-set/v0.1.0")
        self.assertEqual(release_route.select_predecessor([older], CURRENT), "0.1.0")

    def test_candidate_binds_the_derived_version_and_promoted_predecessor(self) -> None:
        workflow = (ROOT / ".github/workflows/release-successor-candidate.yml").read_text()
        self.assertIsNone(VERSION_LITERAL.search(workflow), "the candidate names a version literal again")
        self.assertIn('version="$(python3 scripts/release_route.py version)"', workflow)
        self.assertIn('-getProperty:Version | tr -d \'[:space:]\')" = "$version"', workflow)
        self.assertIn('gh release download "coherent-set/v$predecessor"', workflow)
        self.assertIn('--tag-source "$tag_source"', workflow)
        self.assertIn('--release-tag "coherent-set/v$predecessor"', workflow)
        self.assertEqual(workflow.count('--predecessor "$predecessor"'), 1)
        self.assertEqual(workflow.count('--predecessor "$PREDECESSOR"'), 1)
        self.assertIn("PREDECESSOR: ${{ steps.predecessor.outputs.predecessor }}", workflow)
        for namespace in ("coherent-set", "kit", "drivers", "coord-engine"):
            self.assertIn(f'"refs/tags/{namespace}/v$VERSION"', workflow)
        for project in ("FS.GG.Coord.Cli", "FS.GG.Kit", "FS.GG.Drivers"):
            self.assertEqual(workflow.count(f"dotnet pack src/{project}/"), 1)
        initial=workflow.split('      - name: Verify the stable predecessor and unused package version',1)[1].split('      - name: Test the telemetry producer',1)[0]
        retention=workflow.split('      - name: Refuse a changed release closure or newly occupied version before retention',1)[1].split('      - uses: actions/upload-artifact',1)[0]
        self.assertIn('releases/tags/coherent-set/v$VERSION',initial)
        self.assertIn('git ls-remote',initial)
        self.assertIn('python3 scripts/release_source_closure.py check --route coherent-set --source "$SOURCE_SHA"',retention)
        self.assertEqual(workflow.count('release_source_closure.py check --route coherent-set'),2)
        self.assertNotIn('git ls-remote origin refs/heads/main',workflow)
        self.assertIn('--predecessor "$PREDECESSOR"',retention)
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
        self.assertNotIn('\nSUCCESSOR_PREDECESSOR =', publisher)  # bound from the promoted channel (#5156)
        self.assertIn('identity = bind_current_predecessor(api, (candidate / "previous-stable-channel.json").read_bytes(), version)', publisher)
        self.assertIn('SUCCESSOR_REF = CURRENT_RELEASE["logicalRef"]', publisher)
        self.assertIn('CURRENT_RELEASE = release_route.current_release()', journal)
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
