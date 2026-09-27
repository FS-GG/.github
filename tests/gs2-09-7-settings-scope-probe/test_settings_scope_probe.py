import hashlib
import importlib.util
import json
import tempfile
import unittest
import urllib.error
from email.message import Message
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/gs2-09-7-settings-scope-probe.py"
WORKFLOW = ROOT / ".github/workflows/github-substrate-v2-settings-scope-probe.yml"
spec = importlib.util.spec_from_file_location("settings_scope_probe", SCRIPT)
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


class Response:
    def __init__(self, status, value, headers=None):
        self.status = status
        self.raw = json.dumps(value, separators=(",", ":")).encode()
        self.headers = Message()
        for key, item in (headers or {}).items():
            self.headers[key] = item

    def read(self, _limit):
        return self.raw


class Opener:
    def __init__(self, values):
        self.values = dict(values)
        self.requests = []

    def open(self, request, timeout):
        self.requests.append(request)
        value = self.values[request.full_url]
        if isinstance(value, Exception):
            raise value
        return value


def mint_proof(token):
    return {
        "schema": "fsgg.github-substrate-v2.sandbox-mint-grants/1",
        "appId": 77,
        "appSlug": probe.APP_SLUG,
        "actor": {"login": "fs-gg-cross-repo-dispatch[bot]", "databaseId": 297630107},
        "installationId": 88,
        "repositorySelection": "selected",
        "repository": {"id": probe.REPOSITORY_ID, "nodeId": probe.REPOSITORY_NODE_ID,
                       "fullName": f"{probe.OWNER}/{probe.REPOSITORY}"},
        "permissions": {**probe.EXPECTED_WRITES, "metadata": "read"},
        "mintResponseSha256": "a" * 64,
        "tokenSha256": hashlib.sha256(token.encode()).hexdigest(),
    }


def provider_values():
    values = {}
    for name, path, _, _ in probe.STATIC_PROBES:
        body = {}
        if name == "repository-identity":
            body = {"id": probe.REPOSITORY_ID, "node_id": probe.REPOSITORY_NODE_ID,
                    "full_name": f"{probe.OWNER}/{probe.REPOSITORY}", "private": True,
                    "owner": {"login": probe.OWNER, "id": 1, "node_id": "O_1"}}
        elif name == "repository-environments":
            body = {"total_count": 1, "environments": [{"id": 9, "node_id": "ENV_9", "name": "protected/test"}]}
        elif name == "repository-rulesets":
            body = [{"id": 17, "node_id": "RRS_17", "source_type": "Organization"}]
        elif name == "organization-custom-property-schema":
            body = []
        values[probe.API + path] = Response(200, body)
    values[probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/environments/protected%2Ftest/secrets?per_page=100"] = \
        Response(200, {"total_count": 1, "secrets": [{"name": "DO_NOT_RETAIN"}]})
    return values


class SettingsScopeProbeTests(unittest.TestCase):
    def test_report_binds_identity_and_never_retains_sensitive_payloads(self):
        token = "ghs_" + "t" * 40
        opener = Opener(provider_values())
        with tempfile.TemporaryDirectory() as directory:
            proof = Path(directory) / "mint.json"
            proof.write_text(json.dumps(mint_proof(token)), encoding="utf-8")
            with mock.patch.object(probe, "source_binding", return_value={"headSha": "b" * 40}):
                report = probe.build_report(token, proof, ROOT, "b" * 40, opener)
        encoded = json.dumps(report, sort_keys=True)
        self.assertFalse(report["activation"])
        self.assertEqual("unknown", report["authority"]["installedAppGrant"])
        self.assertEqual(
            [{"kind": "environment", "id": 9, "nodeId": "ENV_9"},
             {"kind": "ruleset", "id": 17, "nodeId": "RRS_17", "sourceType": "Organization"}],
            report["conditionalParents"])
        self.assertNotIn(token, encoded)
        self.assertNotIn("DO_NOT_RETAIN", encoded)
        self.assertTrue(all(request.get_method() == "GET" for request in opener.requests))
        self.assertTrue(all(request.headers["X-github-api-version"] == probe.API_VERSION
                            for request in opener.requests))

    def test_denials_preserve_access_and_unknown_applicability(self):
        headers = Message()
        denied = urllib.error.HTTPError(
            probe.API + f"/orgs/{probe.OWNER}/actions/permissions", 403, "forbidden", headers, None)
        denied.read = lambda _limit: b'{"message":"permission or plan"}'
        opener = Opener({probe.API + f"/orgs/{probe.OWNER}/actions/permissions": denied})
        result, bodies = probe.probe_pages("organization-actions",
                                           f"/orgs/{probe.OWNER}/actions/permissions", False,
                                           "organization_administration:read", "token", opener)
        self.assertEqual([], bodies)
        self.assertEqual("forbidden", result["classification"]["access"])
        self.assertEqual("unknown", result["classification"]["plan"])
        self.assertEqual("unknown", result["classification"]["feature"])
        self.assertEqual(403, result["pages"][0]["status"])

    def test_allowlist_rejects_mutation_hosts_and_pagination_escape(self):
        self.assertFalse(probe.allowed_url("https://evil.example/repos/FS-GG/x"))
        self.assertFalse(probe.allowed_url(probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/actions/permissions?unexpected=1"))
        self.assertFalse(probe.allowed_url(probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}?page=2"))
        self.assertFalse(probe.allowed_url(probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/environments/x/secrets"))
        self.assertFalse(probe.allowed_url(probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/environments/x/secrets/name"))
        with self.assertRaisesRegex(ValueError, "allowlist"):
            probe.get("https://evil.example/value", "token", Opener({}))
        path = f"/repos/{probe.OWNER}/{probe.REPOSITORY}/environments?per_page=100"
        escaped = Response(200, {"environments": []}, {"Link": '<https://evil.example/page=2>; rel="next"'})
        with self.assertRaisesRegex(ValueError, "allowlist"):
            probe.probe_pages("repository-environments", path, True, "actions:read", "token",
                              Opener({probe.API + path: escaped}))

    def test_mint_proof_refuses_extra_write_and_token_mismatch(self):
        token = "ghs_" + "a" * 40
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "proof.json"
            changed = mint_proof(token)
            changed["permissions"]["environments"] = "write"
            path.write_text(json.dumps(changed), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "unexpected write"):
                probe.load_mint_proof(path, token)
            path.write_text(json.dumps(mint_proof(token)), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "differs"):
                probe.load_mint_proof(path, token + "x")

    def test_workflow_keeps_protected_q4_controls_and_revokes_on_all_outcomes(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("timeout-minutes: 5", text)
        self.assertIn("timeout-minutes: 10", text)
        self.assertIn("needs: preflight", text)
        self.assertIn("environment: github-substrate-v2-sandbox", text)
        self.assertIn("group: github-substrate-v2-registered-sandbox", text)
        self.assertIn("always() && steps.mint.outputs.token != ''", text)
        self.assertIn("--method DELETE installation/token", text)
        self.assertIn("if-no-files-found: error", text)
        self.assertIn("python3 -m unittest", text)
        self.assertNotIn("permission-actions:", text)
        self.assertNotIn("permission-environments:", text)


if __name__ == "__main__":
    unittest.main()
