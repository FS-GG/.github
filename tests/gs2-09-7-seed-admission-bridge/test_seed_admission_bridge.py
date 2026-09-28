import datetime as dt
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "seed_admission_bridge", ROOT / "scripts/gs2-09-7-seed-admission-bridge.py")
bridge = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(bridge)


class FakeRead:
    def __init__(self, rows):
        self.rows = rows
        self.calls = []

    def get(self, route, *, limit=bridge.MAX_READ):
        self.calls.append(route)
        value = self.rows[route]
        if callable(value):
            value = value()
        return value if isinstance(value, bytes) else json.dumps(value).encode()


class BridgeTests(unittest.TestCase):
    def setUp(self):
        candidate = "b" * 40
        self.subject = {
            "workflowRepository": "FS-GG/.github",
            "workflowPath": ".github/workflows/github-substrate-v2-sandbox-qualification.yml",
            "environment": "github-substrate-v2-sandbox",
            "workflowSha": "a" * 40, "candidateSha": candidate,
            "runId": 12345, "runAttempt": 2,
            "runNonce": "12345-2-" + candidate,
            "sourceManifestSha256": "c" * 64,
            "approvedArtifactSourceSha256": "d" * 64,
            "sandboxRepositoryId": 1353050537,
            "sandboxRepositoryNodeId": "R_kgDOUKXpqQ",
            "projectNodeId": "PVT_kwDOEYAWY84BiESo",
            "appId": 4166418, "installationId": 143110413,
            "operation": "prepare-only-no-effect",
        }
        self.name = bridge.request_name("prepare", self.subject)
        self.artifact = {
            "id": 9876, "name": self.name, "expired": False,
            "digest": "sha256:" + "e" * 64,
            "workflow_run": {"id": 12345, "head_sha": "a" * 40,
                             "head_branch": "main", "repository_id": 1269292704,
                             "head_repository_id": 1269292704},
        }
        self.route = (f"{bridge.REPO}/actions/artifacts?name={self.name}&per_page=100")

    def test_request_is_canonical_sanitized_and_owner_fields_are_exact(self):
        raw = bridge.request_bytes("prepare", self.subject)
        self.assertEqual({"schema": bridge.authorizer.REQUEST_SCHEMA,
                          "phase": "prepare", "subject": self.subject}, json.loads(raw))
        self.assertEqual(raw, bridge.authorizer.canonical(json.loads(raw)))
        self.assertNotIn(b"token", raw.lower())
        fields = bridge.owner_dispatch_fields("prepare", self.subject, self.artifact)
        self.assertEqual("9876", fields["executor_request_artifact_id"])
        self.assertEqual(self.artifact["digest"], fields["executor_request_archive_digest"])
        self.assertEqual("refs/heads/main", fields["ref"])

    def test_request_artifact_is_native_run_and_digest_bound(self):
        port = FakeRead({self.route: {"total_count": 1, "artifacts": [self.artifact]}})
        self.assertEqual(self.artifact, bridge.request_artifact(port, "prepare", self.subject))
        changed = {**self.artifact, "workflow_run": {**self.artifact["workflow_run"],
                                                     "head_sha": "f" * 40}}
        port.rows[self.route] = {"total_count": 1, "artifacts": [changed]}
        with self.assertRaisesRegex(bridge.Refused, "request-artifact-binding"):
            bridge.request_artifact(port, "prepare", self.subject)

    def test_wait_remains_pending_without_independent_decision(self):
        decision_route = (f"{bridge.REPO}/actions/artifacts?name="
                          f"{bridge.decision_name('prepare', self.subject)}&per_page=100")
        port = FakeRead({self.route: {"total_count": 1, "artifacts": [self.artifact]},
                         decision_route: {"total_count": 0, "artifacts": []}})
        ticks = iter((0, 0, 5, 5, 10))
        with self.assertRaisesRegex(bridge.Refused, "owner-authorization-pending"):
            bridge.wait_decision(port, "prepare", self.subject, "a" * 40,
                                 max_seconds=5, clock=lambda: next(ticks),
                                 pause=lambda _: None)
        self.assertTrue(all("/actions/artifacts" in path for path in port.calls))

    def test_decision_is_passed_through_full_native_verifier(self):
        decision_name = bridge.decision_name("prepare", self.subject)
        decision = {**self.artifact, "id": 4567, "name": decision_name,
                    "workflow_run": {**self.artifact["workflow_run"], "id": 5555}}
        listing = {"total_count": 1, "artifacts": [decision]}
        rows = {
            f"{bridge.REPO}/actions/runs/5555": {"id": 5555, "run_attempt": 1},
            f"{bridge.REPO}/actions/runs/5555/approvals": [],
            f"{bridge.REPO}/environments/fleet-v1-admission-owner": {},
            f"{bridge.REPO}/environments/fleet-v1-admission-owner/deployment-branch-policies": {},
            f"{bridge.REPO}/actions/artifacts/4567/zip": b"zip bytes",
        }
        verified = {"state": "admitted", "executorRequest": {
            "artifactId": self.artifact["id"], "archiveDigest": self.artifact["digest"]}}
        with mock.patch.object(bridge.reader, "verify_decision", return_value=verified) as verify, \
             mock.patch.object(bridge.reader, "NativeDecisionPort",
                               side_effect=lambda value, phase: SimpleNamespace(value=value, phase=phase)):
            result = bridge.verified_decision(FakeRead(rows), "prepare", self.subject,
                                              "a" * 40, decision, listing,
                                              dt.datetime.now(dt.timezone.utc), self.artifact)
            with self.assertRaisesRegex(bridge.Refused, "decision-request-readback-drift"):
                bridge.verified_decision(FakeRead(rows), "prepare", self.subject,
                                         "a" * 40, decision, listing,
                                         dt.datetime.now(dt.timezone.utc),
                                         {**self.artifact, "digest": "sha256:" + "f" * 64})
        self.assertEqual("prepare", result.phase)
        self.assertEqual(self.subject, verify.call_args.kwargs["expected_subject"])
        self.assertEqual("a" * 40, verify.call_args.kwargs["expected_producer_sha"])
        self.assertEqual(5555, verify.call_args.kwargs["producer_run_id"])

    def decision_run(self, *, status="completed", conclusion="success"):
        return {"id": 5555, "run_attempt": 1,
                "path": bridge.authorizer.AUTHORIZER_WORKFLOW,
                "head_sha": "a" * 40, "head_branch": "main",
                "event": "workflow_dispatch", "status": status,
                "conclusion": conclusion,
                "repository": {"id": bridge.authorizer.REPOSITORY_ID},
                "actor": bridge.authorizer.OWNER}

    def test_visible_artifact_waits_for_exact_completed_success(self):
        name = bridge.decision_name("prepare", self.subject)
        artifact = {**self.artifact, "id": 4567, "name": name,
                    "workflow_run": {**self.artifact["workflow_run"], "id": 5555}}
        decision_route = f"{bridge.REPO}/actions/artifacts?name={name}&per_page=100"
        run_route = f"{bridge.REPO}/actions/runs/5555"
        states = iter((self.decision_run(status="in_progress", conclusion=None),
                       self.decision_run(status="completed", conclusion="success")))
        port = FakeRead({self.route: {"total_count": 1, "artifacts": [self.artifact]},
                         decision_route: {"total_count": 1, "artifacts": [artifact]},
                         run_route: lambda: next(states)})
        accepted = object()
        with mock.patch.object(bridge, "verified_decision", return_value=accepted) as verify:
            result = bridge.wait_decision(port, "prepare", self.subject, "a" * 40,
                                          max_seconds=30, clock=lambda: 0,
                                          pause=lambda _: self.assertEqual(0, verify.call_count))
        self.assertIs(accepted, result)
        self.assertEqual(2, port.calls.count(run_route))
        verify.assert_called_once()

    def test_terminal_failure_and_producer_drift_refuse_without_verifier(self):
        name = bridge.decision_name("prepare", self.subject)
        artifact = {**self.artifact, "id": 4567, "name": name,
                    "workflow_run": {**self.artifact["workflow_run"], "id": 5555}}
        decision_route = f"{bridge.REPO}/actions/artifacts?name={name}&per_page=100"
        run_route = f"{bridge.REPO}/actions/runs/5555"
        rows = {self.route: {"total_count": 1, "artifacts": [self.artifact]},
                decision_route: {"total_count": 1, "artifacts": [artifact]},
                run_route: self.decision_run(status="completed", conclusion="failure")}
        with mock.patch.object(bridge, "verified_decision") as verify:
            with self.assertRaisesRegex(bridge.Refused, "decision-producer-failed"):
                bridge.wait_decision(FakeRead(rows), "prepare", self.subject, "a" * 40,
                                     max_seconds=1, clock=lambda: 0)
            rows[run_route] = {**self.decision_run(), "head_sha": "f" * 40}
            with self.assertRaisesRegex(bridge.Refused, "decision-producer-binding"):
                bridge.wait_decision(FakeRead(rows), "prepare", self.subject, "a" * 40,
                                     max_seconds=1, clock=lambda: 0)
        verify.assert_not_called()

    def test_visible_decision_artifact_identity_cannot_change_while_pending(self):
        name = bridge.decision_name("prepare", self.subject)
        artifact = {**self.artifact, "id": 4567, "name": name,
                    "workflow_run": {**self.artifact["workflow_run"], "id": 5555}}
        changed = {**artifact, "digest": "sha256:" + "f" * 64}
        decision_route = f"{bridge.REPO}/actions/artifacts?name={name}&per_page=100"
        states = iter(({"total_count": 1, "artifacts": [artifact]},
                       {"total_count": 1, "artifacts": [changed]}))
        port = FakeRead({self.route: {"total_count": 1, "artifacts": [self.artifact]},
                         decision_route: lambda: next(states),
                         f"{bridge.REPO}/actions/runs/5555": self.decision_run(
                             status="in_progress", conclusion=None)})
        with self.assertRaisesRegex(bridge.Refused, "decision-artifact-drift"):
            bridge.wait_decision(port, "prepare", self.subject, "a" * 40,
                                 max_seconds=30, clock=lambda: 0, pause=lambda _: None)

    def test_read_port_rejects_foreign_route_without_subprocess(self):
        port = bridge.GitHubReadPort("t" * 30)
        with mock.patch.object(bridge.subprocess, "run") as run:
            with self.assertRaisesRegex(bridge.Refused, "native-read-route"):
                port.get("repos/FS-GG/production/actions/artifacts")
        run.assert_not_called()

    def test_protected_main_drift_refuses_before_next_phase(self):
        route = f"{bridge.REPO}/commits/main"
        bridge.require_protected_main(FakeRead({route: {"sha": "a" * 40}}), "a" * 40)
        with self.assertRaisesRegex(bridge.Refused, "protected-main-drift"):
            bridge.require_protected_main(FakeRead({route: {"sha": "f" * 40}}), "a" * 40)


if __name__ == "__main__":
    unittest.main()
