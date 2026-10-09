"""Retired public writers refuse before private input/provider/service access."""
import hashlib
import importlib.util
import json
import pathlib
import unittest
from unittest import mock
from types import SimpleNamespace

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("retired_feed", ROOT / "tools/telemetry-dashboard.py")
D = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(D)


class RetiredFeedTests(unittest.TestCase):
    def test_direct_publication_refuses_before_any_github_read_or_write(self):
        for branch in ("telemetry-data", "telemetry-data-current"):
            with self.subTest(branch=branch), mock.patch.object(D, "github") as github:
                with self.assertRaisesRegex(D.HostSourceError, "PUBLIC_TELEMETRY_FEED_RETIRED"):
                    D.publish("FS-GG/.github", branch, "host.json", "unused", {})
                github.assert_not_called()

    def test_setup_refuses_before_reading_local_inputs(self):
        for branch in ("telemetry-data", "telemetry-data-current"):
            with mock.patch.object(D, "config") as config:
                with self.assertRaisesRegex(D.HostSourceError, "PUBLIC_TELEMETRY_FEED_RETIRED"):
                    D.publisher_setup(SimpleNamespace(branch=branch))
                config.assert_not_called()
            with self.assertRaisesRegex(D.HostSourceError, "PUBLIC_TELEMETRY_FEED_RETIRED"):
                # Other missing fields would fail immediately if filesystem work began.
                D.handoff_setup(SimpleNamespace(branch=branch))

    def test_cli_refuses_recurring_and_default_publishers_before_local_or_provider_io(self):
        commands = [
            ["host-snapshot", "--output", "/unread/output", "--repo", "FS-GG/.github"],
            ["publisher-setup", "--labels", "/unread/labels"],
            ["publisher-event", "--config", "/unread/config"],
            ["handoff-publish", "--state-dir", "/unread/state"],
            ["handoff-setup", "--outgoing", "/unread/outgoing", "--state-dir", "/unread/state",
             "--producer-uid", "1", "--handoff-gid", "1", "--approve-labels", "0" * 64,
             "--candidate-digest", "0" * 64],
        ]
        for command in commands:
            with self.subTest(command=command), mock.patch.object(D.sys, "argv", ["dashboard", *command]), \
                    mock.patch.object(D, "config") as config, mock.patch.object(D, "build_host") as build, \
                    mock.patch.object(D, "publication_token") as token, mock.patch.object(D, "atomic") as write:
                with self.assertRaisesRegex(D.HostSourceError, "PUBLIC_TELEMETRY_FEED_RETIRED"):
                    D.main()
                for operation in (config, build, token, write):
                    operation.assert_not_called()

    def test_local_dry_run_remains_separate_from_public_publication(self):
        with mock.patch.object(D.sys, "argv", ["dashboard", "host-snapshot", "--dry-run", "--output", "/mock/output"]), \
                mock.patch.object(D, "build_host", return_value={}) as build, mock.patch.object(D, "atomic") as write, \
                mock.patch.object(D, "publish") as publish:
            self.assertEqual(D.main(), 0)
            build.assert_called_once()
            write.assert_called_once()
            publish.assert_not_called()

    def test_frozen_snapshot_bytes_and_provenance_are_bound(self):
        manifest = json.loads((ROOT / "telemetry-dashboard/historical-host.provenance.json").read_text())
        raw = (ROOT / manifest["snapshotPath"]).read_bytes()
        self.assertEqual(hashlib.sha256(raw).hexdigest(), manifest["snapshotSha256"])
        self.assertEqual(manifest["snapshotSha256"], "69b3a60f4e94c076c2e843a9c5f106eff81025b32bc219a1b4ff6a1a4757516c")
        self.assertEqual(manifest["sourceCommit"], "e5f16db2c647a4227d51168bf0ae983eb00a228c")
        D.validate_host(json.loads(raw))

    def test_workflow_reads_only_checked_in_historical_host_and_site_labels_it(self):
        workflow = (ROOT / ".github/workflows/telemetry-dashboard.yml").read_text()
        self.assertNotIn("git/ref/heads/telemetry-data-current", workflow)
        self.assertNotIn("contents/host.json?ref=", workflow)
        self.assertIn("cp telemetry-dashboard/historical-host.json host.json", workflow)
        self.assertIn('manifest["snapshotSha256"]', workflow)
        self.assertIn("BUILT_HOST_REVISION", workflow)
        html = (ROOT / "telemetry-dashboard/index.html").read_text()
        self.assertIn('id="historical-host-notice"', html)
        self.assertIn("Frozen historical host snapshot", html)


if __name__ == "__main__":
    unittest.main()
