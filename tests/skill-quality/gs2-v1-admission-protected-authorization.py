#!/usr/bin/env python3
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/gs2-v1-admission-protected-authorization.yml"
PINNED_CUTOVER = ROOT / ".github/workflows/gs2-ledger-protected-authorization.yml"


class V1AdmissionProtectedAuthorizationWorkflowTests(unittest.TestCase):
    def test_separate_manual_read_only_environment_gate(self):
        source = WORKFLOW.read_text()
        self.assertIn("workflow_dispatch:", source)
        self.assertIn("environment: fleet-v1-admission-owner", source)
        self.assertNotIn("environment: fleet-cutover", source)
        self.assertIn("contents: read", source)
        self.assertIn("actions: read", source)
        self.assertIn("test \"$GITHUB_REF\" = 'refs/heads/main'", source)
        self.assertIn("test \"$GITHUB_RUN_ATTEMPT\" = 1", source)
        self.assertNotIn("contents: write", source)
        self.assertNotIn("pull-requests: write", source)
        self.assertNotIn("schedule:", source)
        self.assertNotEqual(source, PINNED_CUTOVER.read_text())

    def test_receipt_binds_source_tree_intent_workflow_run_and_expiry(self):
        source = WORKFLOW.read_text()
        for token in (
            "coordination_revision", "coordination_tree", "genesis_intent_sha256", "operation_id",
            "coordinationRevision", "coordinationTree", "genesisIntentSha256", "workflowRevision",
            "runId", "approvedAt", "expiresAt", "fsgg.v1-admission-genesis-protected-authorization/2",
            "retention-days: 1", "if-no-files-found: error",
        ):
            self.assertIn(token, source)
        self.assertIn("^[0-9a-f]{40}$", source)
        self.assertIn("^[0-9a-f]{64}$", source)

    def test_workflow_has_no_secret_or_mutation_surface(self):
        source = WORKFLOW.read_text().lower()
        for forbidden in ("secrets.", "private key", "github_token", "gh api", "curl ", "git push", "repository_dispatch"):
            self.assertNotIn(forbidden, source)


if __name__ == "__main__":
    unittest.main()
