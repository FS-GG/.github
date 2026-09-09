#!/usr/bin/env python3
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/gs2-ledger-protected-authorization.yml"


class ProtectedAuthorizationWorkflowTests(unittest.TestCase):
    def test_workflow_is_manual_read_only_and_environment_gated(self):
        source = WORKFLOW.read_text()
        self.assertIn("workflow_dispatch:", source)
        self.assertIn("environment: fleet-cutover", source)
        self.assertIn("contents: read", source)
        self.assertIn("actions: read", source)
        self.assertNotIn("contents: write", source)
        self.assertNotIn("pull-requests: write", source)
        self.assertNotIn("schedule:", source)

    def test_receipt_binds_both_repositories_and_exact_payload(self):
        source = WORKFLOW.read_text()
        for token in ("coordination_revision", "initializer_input_sha256", "operation_id",
                      "dotgithubRevision", "coordinationRevision", "inputSha256",
                      "fsgg.github-ledger-protected-authorization/1"):
            self.assertIn(token, source)
        self.assertIn("retention-days: 1", source)

    def test_workflow_has_no_secret_or_mutation_surface(self):
        source = WORKFLOW.read_text().lower()
        for forbidden in ("secrets.", "private key", "github_token", "gh api", "curl ", "git push", "repository_dispatch"):
            self.assertNotIn(forbidden, source)


if __name__ == "__main__":
    unittest.main()
