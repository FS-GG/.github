import importlib.util
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("dashboard_refresh", ROOT / "scripts/telemetry-dashboard-refresh.py")
D = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(D)


class RefreshDecisionTests(unittest.TestCase):
    def test_changed_revision_builds_exact_public_commit(self):
        new, old = "a" * 40, "b" * 40
        self.assertEqual(D.refresh_decision(new, new, old),
                         {"build": "true", "reason": "revision-needed", "revision": new})

    def test_repeated_dispatch_after_deployment_is_no_op(self):
        revision = "a" * 40
        self.assertEqual(D.refresh_decision(revision, revision, revision),
                         {"build": "false", "reason": "already-deployed", "revision": ""})

    def test_failed_deployment_can_retry_same_revision(self):
        revision = "a" * 40
        self.assertEqual(D.refresh_decision(revision, revision, None)["build"], "true")
        with self.assertRaisesRegex(ValueError, "invalid deployed host revision"):
            D.refresh_decision(revision, revision, "invalid Pages JSON")

    def test_stale_dispatch_cannot_build_newer_public_commit(self):
        self.assertEqual(D.refresh_decision("b" * 40, "a" * 40, None),
                         {"build": "false", "reason": "stale-public-revision", "revision": ""})

    def test_manual_and_scheduled_refresh_still_build(self):
        self.assertEqual(D.refresh_decision("a" * 40, None, "a" * 40),
                         {"build": "true", "reason": "ordinary-refresh", "revision": "a" * 40})
        self.assertEqual(D.refresh_decision(None, None, None),
                         {"build": "true", "reason": "ordinary-refresh", "revision": ""})

    def test_invalid_requested_revision_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "invalid requested host revision"):
            D.refresh_decision("a" * 40, "$(unsafe)", None)


if __name__ == "__main__":
    unittest.main()
