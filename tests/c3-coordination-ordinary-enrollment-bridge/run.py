#!/usr/bin/env python3
from __future__ import annotations

import base64
import copy
import hashlib
import importlib.util
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
TOOL = ROOT / "tools/c3-coordination-ordinary-enrollment-bridge.py"
WORKFLOW = ROOT / ".github/workflows/c3-coordination-ordinary-enrollment-bridge.yml"
SPEC = importlib.util.spec_from_file_location("c3_coordination_enrollment_bridge", TOOL)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class BridgeTests(unittest.TestCase):
    def setUp(self):
        self.env = {
            "GITHUB_REPOSITORY": "FS-GG/.github",
            "GITHUB_REPOSITORY_ID": "1269292704",
            "GITHUB_EVENT_NAME": "workflow_dispatch",
            "GITHUB_REF": "refs/heads/main",
            "GITHUB_WORKFLOW_REF": (
                "FS-GG/.github/.github/workflows/c3-coordination-ordinary-enrollment-bridge.yml@refs/heads/main"
            ),
            "GITHUB_SHA": "a" * 40,
            "GITHUB_RUN_ID": "1234",
            "GITHUB_RUN_ATTEMPT": "1",
            "V2_ORDINARY_APP_ID": "5064713",
            "V2_ORDINARY_APP_PRIVATE_KEY": "fake-app-private-key",
            "V2_ORDINARY_AUTHORIZER_PRIVATE_KEY": "fake-authorizer-private-key",
        }
        self.overrides = {}
        self.calls = []

    def api(self, path, token=None, body=None):
        self.calls.append((path, token, body))
        if path in self.overrides:
            return self.overrides[path]
        fixtures = {
            "repositories/1269292704": {
                "id": 1269292704, "full_name": "FS-GG/.github", "default_branch": "main",
            },
            "repos/FS-GG/.github/git/ref/heads/main": {
                "ref": "refs/heads/main", "object": {"sha": "a" * 40},
            },
            "repositories/1346720714": {
                "id": 1346720714, "full_name": "FS-GG/FS.GG.Coordination", "default_branch": "main",
                "archived": False, "disabled": False,
            },
            "repos/FS-GG/FS.GG.Coordination/environments/ordinary-v2": {
                "id": 22954548572, "node_id": "EN_kwDOUEVTys8AAAAFWDKdXA", "name": "ordinary-v2",
                "can_admins_bypass": True,
                "protection_rules": [{"id": 67004485, "node_id": "GA_kwDOUEVTys4D_mhF", "type": "branch_policy"}],
                "deployment_branch_policy": {"protected_branches": False, "custom_branch_policies": True},
            },
            "repos/FS-GG/FS.GG.Coordination/environments/ordinary-v2/deployment-branch-policies": {
                "total_count": 1,
                "branch_policies": [{"id": 61333204,
                    "node_id": "MDE2OkdhdGVCcmFuY2hQb2xpY3k2MTMzMzIwNA==",
                    "name": "main", "type": "branch"}],
            },
            "app": {
                "id": 5064713, "name": "FS-GG Ordinary V2 Settlement", "owner": {"login": "FS-GG"},
                "permissions": {"contents": "write", "metadata": "read"}, "events": [],
            },
            "repos/FS-GG/FS.GG.Coordination.Authority/installation": {
                "id": 164553252, "app_id": 5064713, "account": {"login": "FS-GG"},
                "repository_selection": "selected", "permissions": {"contents": "write", "metadata": "read"},
                "events": [], "suspended_at": None,
            },
            "app/installations/164553252/access_tokens": {"token": "ephemeral-installation-token"},
            "installation/repositories?per_page=100": {
                "total_count": 1,
                "repositories": [{"id": 1351660651, "full_name": "FS-GG/FS.GG.Coordination.Authority"}],
            },
        }
        return copy.deepcopy(fixtures[path])

    @staticmethod
    def signer(_key, _message):
        return b"fake-signature"

    @staticmethod
    def seal(_key, plaintext):
        return b"S" * 48 + plaintext

    @staticmethod
    def authorizer_signer(_key, message):
        return hashlib.sha256(message).digest()

    def packet(self, env=None):
        return MODULE.create_packet(
            copy.deepcopy(env or self.env), api=self.api, signer=self.signer,
            authorizer_reader=lambda _key: (MODULE.SOURCE_AUTHORIZER_SPKI_SHA256, b"fake-spki"),
            authorizer_signer=self.authorizer_signer,
            seal=self.seal,
        )

    def test_emits_only_three_fixed_ciphertexts_for_exact_destination(self):
        packet = self.packet()
        self.assertEqual("fsgg.github.c3-coordination-ordinary-enrollment-ciphertexts/1", packet["schema"])
        self.assertEqual(1346720714, packet["destination"]["repositoryId"])
        self.assertEqual(22954548572, packet["destination"]["environmentId"])
        self.assertEqual("3380204578043523366", packet["destination"]["publicKeyId"])
        self.assertEqual(list(MODULE.SECRET_NAMES), [item["name"] for item in packet["secrets"]])
        self.assertNotIn("fake-app-private-key", str(packet))
        self.assertNotIn("fake-authorizer-private-key", str(packet))
        self.assertEqual("RSA-PSS-SHA256", packet["attestation"]["algorithm"])
        self.assertEqual(MODULE.SOURCE_AUTHORIZER_SPKI_SHA256,
                         packet["attestation"]["publicKeySpkiSha256"])
        self.assertTrue(base64.b64decode(packet["attestation"]["signature"], validate=True))
        for item, name in zip(packet["secrets"], MODULE.SECRET_NAMES):
            self.assertEqual("3380204578043523366", item["key_id"])
            ciphertext = base64.b64decode(item["encrypted_value"], validate=True)
            self.assertEqual(len(self.env[name].encode()) + 48, len(ciphertext))
            self.assertTrue(ciphertext.startswith(b"S" * 48))

    def test_refuses_wrong_source_runtime_and_noncanonical_destination_key(self):
        for field, value in (
            ("GITHUB_REPOSITORY", "FS-GG/FS.GG.Coordination"),
            ("GITHUB_REPOSITORY_ID", "1346720714"),
            ("GITHUB_EVENT_NAME", "push"),
            ("GITHUB_REF", "refs/heads/feature"),
            ("GITHUB_WORKFLOW_REF", "FS-GG/.github/other.yml@refs/heads/main"),
        ):
            env = copy.deepcopy(self.env)
            env[field] = value
            with self.subTest(field=field), self.assertRaisesRegex(MODULE.Refusal, "protected-main"):
                self.packet(env)
        for key_id, public_key in (("999", base64.b64encode(b"A" * 32).decode()),
                                   ("3380204578043523366", MODULE.DESTINATION_PUBLIC_KEY_BASE64)):
            env = copy.deepcopy(self.env)
            env["FSGG_DESTINATION_KEY_ID"] = key_id
            env["FSGG_DESTINATION_PUBLIC_KEY"] = public_key
            env["FSGG_DESTINATION_PUBLIC_KEY_SHA256"] = "0" * 64
            with self.assertRaisesRegex(MODULE.Refusal, "caller-selected"):
                self.packet(env)

    def test_refuses_source_identity_and_destination_policy_drift(self):
        env = copy.deepcopy(self.env)
        env["V2_ORDINARY_APP_ID"] = "5065136"
        with self.assertRaisesRegex(MODULE.Refusal, "source App ID"):
            self.packet(env)
        with self.assertRaisesRegex(MODULE.Refusal, "authorizer identity"):
            MODULE.create_packet(
                copy.deepcopy(self.env), api=self.api, signer=self.signer,
                authorizer_reader=lambda _key: ("0" * 64, b"wrong"),
                authorizer_signer=self.authorizer_signer, seal=self.seal,
            )
        self.overrides["repos/FS-GG/FS.GG.Coordination/environments/ordinary-v2/deployment-branch-policies"] = {
            "total_count": 1, "branch_policies": [{"id": 1, "name": "release", "type": "branch"}],
        }
        with self.assertRaisesRegex(MODULE.Refusal, "only to main"):
            self.packet()

    def test_refuses_a_stale_protected_main_dispatch(self):
        self.overrides["repos/FS-GG/.github/git/ref/heads/main"] = {
            "ref": "refs/heads/main", "object": {"sha": "b" * 40},
        }
        with self.assertRaisesRegex(MODULE.Refusal, "no longer current protected main"):
            self.packet()

    def test_refuses_incomplete_custody_and_bad_ciphertext_shape(self):
        env = copy.deepcopy(self.env)
        env.pop("V2_ORDINARY_APP_PRIVATE_KEY")
        with self.assertRaisesRegex(MODULE.Refusal, "custody is incomplete"):
            self.packet(env)
        with self.assertRaisesRegex(MODULE.Refusal, "ciphertext has an invalid shape"):
            MODULE.create_packet(
                copy.deepcopy(self.env), api=self.api, signer=self.signer,
                authorizer_reader=lambda _key: (MODULE.SOURCE_AUTHORIZER_SPKI_SHA256, b"fake-spki"),
                authorizer_signer=self.authorizer_signer,
                seal=lambda _key, plaintext: plaintext,
            )

    def test_attestation_binds_every_ciphertext_and_pinned_authorizer(self):
        original_digest = MODULE.SOURCE_AUTHORIZER_SPKI_SHA256
        MODULE.SOURCE_AUTHORIZER_SPKI_SHA256 = hashlib.sha256(b"fake-spki").hexdigest()
        try:
            packet = self.packet()
        finally:
            MODULE.SOURCE_AUTHORIZER_SPKI_SHA256 = original_digest

        def verifier(_public_key, message, signature):
            return signature == hashlib.sha256(message).digest()

        MODULE.SOURCE_AUTHORIZER_SPKI_SHA256 = hashlib.sha256(b"fake-spki").hexdigest()
        try:
            MODULE.verify_packet_attestation(packet, verifier)
            altered = copy.deepcopy(packet)
            altered["secrets"][0]["encrypted_value"] = base64.b64encode(b"A" * 49).decode()
            with self.assertRaisesRegex(MODULE.Refusal, "signature is invalid"):
                MODULE.verify_packet_attestation(altered, verifier)
            wrong_key = copy.deepcopy(packet)
            wrong_key["attestation"]["publicKeySpki"] = base64.b64encode(b"attacker-spki").decode()
            with self.assertRaisesRegex(MODULE.Refusal, "public key differs"):
                MODULE.verify_packet_attestation(wrong_key, verifier)
        finally:
            MODULE.SOURCE_AUTHORIZER_SPKI_SHA256 = original_digest

    def test_source_app_probe_uses_only_exact_installation_and_metadata_scope(self):
        self.packet()
        token_call = next(call for call in self.calls if call[0].endswith("/access_tokens"))
        self.assertEqual("app/installations/164553252/access_tokens", token_call[0])
        self.assertTrue(token_call[1].startswith("eyJ"))
        self.assertEqual({
            "repositories": ["FS.GG.Coordination.Authority"],
            "permissions": {"metadata": "read"},
        }, token_call[2])
        repository_call = next(call for call in self.calls if call[0] == "installation/repositories?per_page=100")
        self.assertEqual("ephemeral-installation-token", repository_call[1])
        self.assertIsNone(repository_call[2])

    def test_workflow_has_no_destination_selector_token_or_secret_effect(self):
        workflow = WORKFLOW.read_text()
        self.assertIn("environment: ordinary-v2", workflow)
        self.assertIn("runs-on: ubuntu-24.04", workflow)
        self.assertIn("libsodium23", workflow)
        self.assertIn("github.ref == 'refs/heads/main'", workflow)
        self.assertIn("retention-days: 1", workflow)
        for name in MODULE.SECRET_NAMES:
            self.assertIn("${{ secrets." + name + " }}", workflow)
        self.assertNotIn("destination_repository", workflow)
        self.assertNotIn("destination_environment", workflow)
        self.assertNotIn("destination_public_key", workflow)
        self.assertNotIn("destination_key_id", workflow)
        self.assertNotIn("GH_TOKEN", workflow)
        self.assertNotIn("GITHUB_TOKEN", workflow)
        self.assertNotIn("encrypted_value", workflow)
        self.assertNotIn("PUT", workflow)
        self.assertNotIn("curl", workflow)


if __name__ == "__main__":
    unittest.main()
