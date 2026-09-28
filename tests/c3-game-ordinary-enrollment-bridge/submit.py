#!/usr/bin/env python3
import base64
import copy
import importlib.util
import io
import json
import pathlib
import unittest
from contextlib import redirect_stdout
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parents[2]
PATH = ROOT / "tools/c3-game-ordinary-enrollment-submit.py"
SPEC = importlib.util.spec_from_file_location("submit", PATH)
M = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(M)


class SubmitValidationTests(unittest.TestCase):
    def setUp(self):
        self.packet = {
            "schema": "fsgg.github.c3-game-ordinary-enrollment-ciphertexts/1",
            "source": {
                "repository": "FS-GG/.github", "repositoryId": 1269292704,
                "workflow": ".github/workflows/c3-game-ordinary-enrollment-bridge.yml",
                "sha": "a" * 40, "runId": 1234, "runAttempt": 1,
                "appId": 5064713, "installationId": 164553252,
                "authorizerKeyId": M.SOURCE_AUTHORIZER_KEY_ID,
                "authorizerSpkiSha256": M.SOURCE_AUTHORIZER_SPKI_SHA256,
            },
            "destination": {
                "repository": M.REPOSITORY, "repositoryId": M.REPOSITORY_ID,
                "environment": M.ENVIRONMENT, "environmentId": M.ENVIRONMENT_ID,
                "publicKeyId": M.PUBLIC_KEY_ID, "publicKeySha256": M.PUBLIC_KEY_SHA256,
            },
            "secrets": [
                {"name": name, "key_id": M.PUBLIC_KEY_ID,
                 "encrypted_value": base64.b64encode(b"X" * 49).decode()}
                for name in sorted(M.EXPECTED_NAMES)
            ],
            "attestation": {
                "algorithm": M.SIGNING_ALGORITHM, "keyId": M.SOURCE_AUTHORIZER_KEY_ID,
                "publicKeySpki": "ignored-by-injected-verifier",
                "publicKeySpkiSha256": M.SOURCE_AUTHORIZER_SPKI_SHA256,
                "signature": "ignored-by-injected-verifier",
            },
        }
        self.overrides = {}
        self.calls = []

    def request(self, path, token=None, method="GET", body=None):
        self.calls.append((path, token, method, body))
        if path in self.overrides:
            return copy.deepcopy(self.overrides[path])
        fixtures = {
            "repos/FS-GG/.github/git/ref/heads/main": {
                "ref": "refs/heads/main", "object": {"sha": "a" * 40}},
            "repos/FS-GG/.github/actions/runs/1234": {
                "id": 1234, "run_attempt": 1, "event": "workflow_dispatch", "head_branch": "main",
                "head_sha": "a" * 40, "path": M.SOURCE_WORKFLOW, "status": "completed",
                "conclusion": "success", "repository": {"id": 1269292704, "full_name": "FS-GG/.github"}},
            "repositories/1290990429/environments/ordinary-v2": {
                "id": M.ENVIRONMENT_ID, "node_id": M.ENVIRONMENT_NODE_ID, "name": M.ENVIRONMENT,
                "can_admins_bypass": True,
                "protection_rules": [{"id": M.PROTECTION_RULE_ID,
                    "node_id": M.PROTECTION_RULE_NODE_ID, "type": "branch_policy"}],
                "deployment_branch_policy": {"protected_branches": False, "custom_branch_policies": True}},
            "repositories/1290990429/environments/ordinary-v2/deployment-branch-policies": {
                "total_count": 1, "branch_policies": [{"id": M.BRANCH_POLICY_ID,
                    "node_id": M.BRANCH_POLICY_NODE_ID, "name": "main", "type": "branch"}]},
            "repositories/1290990429/environments/ordinary-v2/secrets/public-key": {
                "key_id": M.PUBLIC_KEY_ID, "key": "qHLGHajIzw9p4Tu5vi/dmm33COA8hYArOZDgiLFx0jY="},
            "repositories/1290990429/environments/ordinary-v2/secrets?per_page=100": {
                "total_count": 0, "secrets": []},
        }
        return copy.deepcopy(fixtures[path])

    def validate(self, packet=None, verifier=lambda _packet: None):
        return M.validate_packet(copy.deepcopy(packet or self.packet), "admin-token",
                                 request=self.request, signature_verifier=verifier)

    def test_accepts_only_after_all_exact_reads_and_empty_inventory(self):
        self.assertEqual(3, len(self.validate()))
        self.assertEqual("secrets?per_page=100", self.calls[-1][0].split("/")[-1])
        self.assertTrue(all(call[2] == "GET" for call in self.calls))

    def test_replay_or_partial_inventory_refuses_before_any_put(self):
        path = "repositories/1290990429/environments/ordinary-v2/secrets?per_page=100"
        self.overrides[path] = {"total_count": 1, "secrets": [{"name": "V2_ORDINARY_APP_ID"}]}
        with self.assertRaisesRegex(M.Refusal, "replay or partial"):
            self.validate()
        self.assertTrue(all(call[2] == "GET" for call in self.calls))

    def test_failed_run_and_policy_drift_refuse(self):
        path = "repos/FS-GG/.github/actions/runs/1234"
        self.overrides[path] = self.request(path)
        self.overrides[path]["conclusion"] = "failure"
        self.calls.clear()
        with self.assertRaisesRegex(M.Refusal, "successful protected-main"):
            self.validate()
        self.overrides.clear()
        path = "repositories/1290990429/environments/ordinary-v2"
        self.overrides[path] = self.request(path, "admin-token")
        self.overrides[path]["protection_rules"] = []
        self.calls.clear()
        with self.assertRaisesRegex(M.Refusal, "environment policy differs"):
            self.validate()

    def test_source_and_public_key_drift_refuse(self):
        packet = copy.deepcopy(self.packet)
        packet["source"]["runAttempt"] = 2
        with self.assertRaisesRegex(M.Refusal, "successful protected-main"):
            self.validate(packet)
        self.overrides.clear()
        self.calls.clear()
        path = "repositories/1290990429/environments/ordinary-v2/secrets/public-key"
        self.overrides[path] = {"key_id": "999", "key": "qHLGHajIzw9p4Tu5vi/dmm33COA8hYArOZDgiLFx0jY="}
        with self.assertRaisesRegex(M.Refusal, "current Game environment key"):
            self.validate()

    def test_bad_signature_refuses_before_live_reads(self):
        def reject(_packet):
            raise M.Refusal("signature invalid")
        with self.assertRaisesRegex(M.Refusal, "signature invalid"):
            self.validate(verifier=reject)
        self.assertEqual([], self.calls)

    def test_sibling_receiver_packets_refuse_before_live_reads(self):
        for sibling, repository_id in (("rendering", 1269292235), ("net", 1305845505)):
            for field in ("schema", "workflow", "destination"):
                packet = copy.deepcopy(self.packet)
                if field == "schema":
                    packet["schema"] = f"fsgg.github.c3-{sibling}-ordinary-enrollment-ciphertexts/1"
                elif field == "workflow":
                    packet["source"]["workflow"] = f".github/workflows/c3-{sibling}-ordinary-enrollment-bridge.yml"
                else:
                    packet["destination"]["repository"] = "FS-GG/FS.GG." + sibling.capitalize()
                    packet["destination"]["repositoryId"] = repository_id
                with self.subTest(sibling=sibling, field=field), self.assertRaises(M.Refusal):
                    self.validate(packet)
                self.assertEqual([], self.calls)

    def test_helper_has_fd3_token_input_and_verify_only_default(self):
        source = PATH.read_text()
        self.assertIn('os.read(3, 65536)', source)
        self.assertIn('parser.add_argument("--submit", action="store_true")', source)
        self.assertIn("if not args.submit:", source)
        self.assertNotIn("GH_TOKEN", source)
        self.assertNotIn("GITHUB_TOKEN", source)
        self.assertNotIn("os.environ", source)

    def test_submit_performs_exactly_three_encrypted_puts_then_readback(self):
        secrets = copy.deepcopy(self.packet["secrets"])
        calls = []

        def api(path, token=None, method="GET", body=None):
            calls.append((path, token, method, body))
            if method == "PUT":
                return {}
            return {"total_count": 3, "secrets": [
                {"name": name} for name in sorted(M.EXPECTED_NAMES)
            ]}

        output = io.StringIO()
        with (
            mock.patch.object(M.os, "read", return_value=b"administrator-token\n"),
            mock.patch("builtins.open", mock.mock_open(read_data=json.dumps(self.packet))),
            mock.patch.object(M, "validate_packet", return_value=secrets),
            mock.patch.object(M, "api", side_effect=api),
            mock.patch.object(M.sys, "argv", ["submit", "packet.json", "--submit"]),
            redirect_stdout(output),
        ):
            M.main()

        puts = [call for call in calls if call[2] == "PUT"]
        self.assertEqual(3, len(puts))
        self.assertEqual(M.EXPECTED_NAMES, {call[0].rsplit("/", 1)[-1] for call in puts})
        self.assertTrue(all(call[1] == "administrator-token" for call in calls))
        self.assertEqual({"encrypted_value", "key_id"}, set(puts[0][3]))
        self.assertEqual("GET", calls[-1][2])
        self.assertTrue(calls[-1][0].endswith("secrets?per_page=100"))
        self.assertNotIn("administrator-token", output.getvalue())
        for secret in secrets:
            self.assertNotIn(secret["encrypted_value"], output.getvalue())


if __name__ == "__main__":
    unittest.main()
