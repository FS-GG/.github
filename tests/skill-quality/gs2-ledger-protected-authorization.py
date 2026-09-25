#!/usr/bin/env python3
import pathlib
import subprocess
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/gs2-ledger-protected-authorization.yml"


class ProtectedAuthorizationWorkflowTests(unittest.TestCase):
    def validation_script(self):
        source = WORKFLOW.read_text()
        step = source.split("      - name: Validate exact public bindings\n", 1)[1]
        block = step.split("        run: |\n", 1)[1].split("      - name:", 1)[0]
        return "\n".join(line[10:] for line in block.splitlines())

    def test_validation_refuses_wrong_execution_context_and_inputs(self):
        valid = {
            "GITHUB_REPOSITORY": "FS-GG/.github",
            "GITHUB_EVENT_NAME": "workflow_dispatch",
            "GITHUB_REF": "refs/heads/main",
            "GITHUB_RUN_ATTEMPT": "1",
            "COORDINATION_REVISION": "a" * 40,
            "INPUT_SHA256": "b" * 64,
            "OPERATION_ID": "cutover-initialization",
        }
        script = self.validation_script()

        def status(values):
            return subprocess.run(["bash", "-c", script], env=values,
                                  capture_output=True, text=True).returncode

        self.assertEqual(status(valid), 0)
        for key, value in (
            ("GITHUB_REPOSITORY", "FS-GG/other"),
            ("GITHUB_EVENT_NAME", "push"),
            ("GITHUB_REF", "refs/heads/feature"),
            ("GITHUB_RUN_ATTEMPT", "2"),
            ("COORDINATION_REVISION", "a" * 39),
            ("INPUT_SHA256", "b" * 63),
            ("OPERATION_ID", "bad id"),
        ):
            with self.subTest(key=key):
                self.assertNotEqual(status({**valid, key: value}), 0)

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
