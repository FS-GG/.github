#!/usr/bin/env python3
import json
import pathlib
import subprocess
import tempfile
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/gs2-open-v2-owner-approval.yml"


class OwnerApprovalPreparationTests(unittest.TestCase):
    def script_for(self, step_name):
        source = WORKFLOW.read_text()
        step = source.split(f"      - name: {step_name}\n", 1)[1]
        block = step.split("        run: |\n", 1)[1].split("      - name:", 1)[0]
        return "\n".join(line[10:] for line in block.splitlines())

    def test_inactive_receipt_only_boundary(self):
        source = WORKFLOW.read_text()
        self.assertIn("if: ${{ false }}", source)
        self.assertIn("environment: fleet-cutover-owner", source)
        self.assertIn("workflow_dispatch:", source)
        self.assertIn("contents: read", source)
        self.assertIn("actions: read", source)
        self.assertIn("decisionPacketSha256", source)
        self.assertIn("verifiedEpochCommit", source)
        self.assertIn("runAttempt", source)
        self.assertIn("fsgg.open-v2-owner-approval-run-envelope/1", source)
        self.assertIn("retention-days: 1", source)
        self.assertNotIn("approvedAt", source)
        self.assertNotIn("expiresAt", source)
        self.assertNotIn('conclusion:"success"', source)
        for forbidden in ("secrets.", "contents: write", "gh api", "curl ",
                          "git push", "repository_dispatch", "pending_deployments"):
            self.assertNotIn(forbidden, source.lower())

    def test_public_bindings_refuse_wrong_run_and_intent(self):
        script = self.script_for("Validate exact public bindings")
        valid = {
            "GITHUB_REPOSITORY": "FS-GG/.github",
            "GITHUB_EVENT_NAME": "workflow_dispatch",
            "GITHUB_REF": "refs/heads/main",
            "GITHUB_RUN_ATTEMPT": "1",
            "COORDINATION_REVISION": "a" * 40,
            "MANIFEST_SHA256": "b" * 64,
            "PACKET_SHA256": "c" * 64,
            "VERIFIED_EPOCH_COMMIT": "d" * 40,
            "OPERATION_ID": "open-v2:fs-gg-production",
        }

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
            ("MANIFEST_SHA256", "b" * 63),
            ("PACKET_SHA256", "c" * 63),
            ("VERIFIED_EPOCH_COMMIT", "d" * 39),
            ("OPERATION_ID", "open-v2:other-fleet"),
        ):
            with self.subTest(key=key):
                self.assertNotEqual(status({**valid, key: value}), 0)

    def test_run_envelope_binds_inputs_without_claiming_native_approval(self):
        values = {
            "GITHUB_RUN_ID": "42",
            "GITHUB_RUN_ATTEMPT": "1",
            "GITHUB_SHA": "e" * 40,
            "COORDINATION_REVISION": "a" * 40,
            "MANIFEST_SHA256": "b" * 64,
            "PACKET_SHA256": "c" * 64,
            "VERIFIED_EPOCH_COMMIT": "d" * 40,
            "OPERATION_ID": "open-v2:fs-gg-production",
        }
        with tempfile.TemporaryDirectory() as path:
            result = subprocess.run(["bash", "-c", self.script_for("Materialize run envelope")],
                                    env=values, cwd=path, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            receipt = json.loads((pathlib.Path(path) / "open-v2-run-envelope.json").read_text())
        self.assertEqual(receipt["runId"], 42)
        self.assertEqual(receipt["runAttempt"], 1)
        self.assertEqual(receipt["workflowRevision"], values["GITHUB_SHA"])
        self.assertEqual(receipt["candidateManifestSha256"], values["MANIFEST_SHA256"])
        self.assertEqual(receipt["decisionPacketSha256"], values["PACKET_SHA256"])
        self.assertEqual(receipt["verifiedEpochCommit"], values["VERIFIED_EPOCH_COMMIT"])
        for absent in ("approvedAt", "expiresAt", "conclusion", "reviewerId"):
            self.assertNotIn(absent, receipt)


if __name__ == "__main__":
    unittest.main()
