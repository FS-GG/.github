import hashlib
import importlib.util
import json
import tempfile
import unittest
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
        if isinstance(value, list):
            value = value.pop(0)
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
        "permissions": {**probe.EXPECTED_WRITES, **probe.EXPECTED_READS},
        "mintResponseSha256": "a" * 64,
        "tokenSha256": hashlib.sha256(token.encode()).hexdigest(),
    }


def provider_values():
    values = {}
    for name, path, _, _ in probe.STATIC_PROBES:
        body = {}
        if name == "organization-identity":
            body = {"login": probe.OWNER, "id": 1, "node_id": "O_1", "plan": {"name": "team"}}
        elif name == "repository-identity":
            body = {"id": probe.REPOSITORY_ID, "node_id": probe.REPOSITORY_NODE_ID,
                    "full_name": f"{probe.OWNER}/{probe.REPOSITORY}", "private": True,
                    "visibility": "private", "fork": False, "source": None,
                    "default_branch": "main", "updated_at": "2026-09-28T00:00:00Z",
                    "owner": {"login": probe.OWNER, "id": 1, "node_id": "O_1"}}
        elif name == "repository-environments":
            body = {"total_count": 1, "environments": [{"id": 9, "node_id": "ENV_9", "name": "protected/test"}]}
        elif name == "repository-rulesets":
            body = [{"id": 17, "node_id": "RRS_17", "source_type": "Repository",
                     "source": f"{probe.OWNER}/{probe.REPOSITORY}"}]
        elif name == "organization-custom-property-schema":
            body = [
                {"property_name": "sensitive_team_property", "value_type": "single_select", "required": False,
                 "default_value": "core", "allowed_values": ["core", "edge"],
                 "values_editable_by": "org_and_repo_actors", "require_explicit_values": False,
                 "source_type": "organization",
                 "url": f"{probe.API}/orgs/{probe.OWNER}/properties/schema/sensitive_team_property"},
                {"property_name": "sensitive_tier_property", "value_type": "single_select", "required": False,
                 "default_value": None, "allowed_values": ["gold", "silver"],
                 "values_editable_by": "org_actors", "require_explicit_values": False,
                 "source_type": "organization",
                 "url": f"{probe.API}/orgs/{probe.OWNER}/properties/schema/sensitive_tier_property"},
                {"property_name": "sensitive_omitted_property", "value_type": "string", "required": False,
                 "default_value": "fallback", "allowed_values": [],
                 "values_editable_by": "org_actors", "require_explicit_values": False,
                 "source_type": "organization",
                 "url": f"{probe.API}/orgs/{probe.OWNER}/properties/schema/sensitive_omitted_property"},
            ]
        elif name == "repository-custom-property-values":
            body = [{"property_name": "sensitive_team_property", "value": "core"},
                    {"property_name": "sensitive_tier_property", "value": "gold"}]
        values[probe.API + path] = Response(200, body)
    values[probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/environments/protected%2Ftest/secrets?per_page=100"] = \
        Response(200, {"total_count": 1, "secrets": [{"name": "DO_NOT_RETAIN"}]})
    values[probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/rulesets/17"] = \
        Response(200, ruleset_detail())
    return values


def links(**relations):
    return {"Link": ", ".join(f'<{url}>; rel="{relation}"' for relation, url in relations.items())}


def ruleset_detail(**changes):
    value = {
        "id": 17, "node_id": "RRS_17", "name": "protected branch", "target": "branch",
        "source_type": "Repository", "source": f"{probe.OWNER}/{probe.REPOSITORY}",
        "enforcement": "active", "bypass_actors": [],
        "conditions": {"ref_name": {"include": ["~DEFAULT_BRANCH"], "exclude": []}},
        "rules": [{"type": "non_fast_forward"}],
    }
    value.update(changes)
    return value


class SettingsScopeProbeTests(unittest.TestCase):
    def build(self, values):
        token = "ghs_" + "v" * 40
        with tempfile.TemporaryDirectory() as directory:
            proof = Path(directory) / "mint.json"
            proof.write_text(json.dumps(mint_proof(token)), encoding="utf-8")
            with mock.patch.object(probe, "source_binding", return_value={"headSha": "b" * 40}):
                return probe.build_report(token, proof, ROOT, "b" * 40, Opener(values))

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
        self.assertEqual("fsgg.github-substrate-v2.settings-scope-probe/2", report["schema"])
        self.assertTrue(report["diagnostic"]["collected"])
        self.assertTrue(report["diagnostic"]["expectedStatusSet"])
        self.assertFalse(report["diagnostic"]["canonicalAuthorityQualified"])
        self.assertEqual("unknown", report["authority"]["installedAppGrant"])
        self.assertFalse(report["authority"]["qualified"])
        self.assertEqual("qualified-current-token-read", report["customProperties"]["verdict"])
        self.assertEqual("missing-from-mint-proof",
                         report["customProperties"]["permissionEvidence"]["disposition"])
        self.assertIsNone(report["customProperties"]["permissionEvidence"]["observedMintGrant"])
        self.assertEqual(
            {"definitionCount": 3,
             "definitionTypes": {"multi_select": 0, "single_select": 2,
                                 "string": 1, "true_false": 0, "url": 0},
             "equalToDefaultProvenanceUnknownCount": 1,
             "omittedProvenanceUnknownCount": 1,
             "providerProvenExplicitCount": 1,
             "returnedValueCount": 2},
            report["customProperties"]["summary"])
        self.assertEqual(
            [{"kind": "environment", "id": 9, "nodeId": "ENV_9"}],
            report["conditionalParents"])
        self.assertEqual("qualified-current-token-read", report["repositoryRulesets"]["verdict"])
        self.assertTrue(report["repositoryRulesets"]["bypassActorsVisible"])
        self.assertEqual([200], report["repositoryRulesets"]["detailStatuses"])
        self.assertNotIn(token, encoded)
        self.assertNotIn("DO_NOT_RETAIN", encoded)
        self.assertNotIn("fallback", encoded)
        self.assertNotIn("gold", encoded)
        self.assertNotIn("sensitive_team_property", encoded)
        self.assertNotIn("protected branch", encoded)
        self.assertTrue(all(request.get_method() == "GET" for request in opener.requests))
        self.assertTrue(all(request.data is None for request in opener.requests))
        self.assertTrue(all(request.headers["X-github-api-version"] == probe.API_VERSION
                            for request in opener.requests))
        custom_urls = {
            probe.API + f"/orgs/{probe.OWNER}/properties/schema",
            probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/properties/values",
        }
        self.assertEqual({url: 2 for url in custom_urls},
                         {url: sum(request.full_url == url for request in opener.requests)
                          for url in custom_urls})
        self.assertTrue(all(page["terminal"] and page["links"] == {}
                            for item in report["probes"] if "custom-property" in item["name"]
                            for page in item["pages"]))

    def test_denials_preserve_access_and_unknown_applicability(self):
        denied = Response(403, {"message": "permission or plan"})
        opener = Opener({probe.API + f"/orgs/{probe.OWNER}/actions/permissions": denied})
        result, bodies = probe.probe_pages("organization-actions",
                                           f"/orgs/{probe.OWNER}/actions/permissions", False,
                                           "organization_administration:read", "token", opener)
        self.assertEqual([], bodies)
        self.assertEqual("forbidden", result["classification"]["access"])
        self.assertEqual("unknown", result["classification"]["plan"])
        self.assertEqual("unknown", result["classification"]["feature"])
        self.assertEqual(403, result["pages"][0]["status"])

        unauthorized = Response(401, {"message": "credential"})
        result, _ = probe.probe_pages(
            "organization-custom-property-schema",
            f"/orgs/{probe.OWNER}/properties/schema", False,
            "organization_custom_properties:read", "token",
            Opener({probe.API + f"/orgs/{probe.OWNER}/properties/schema": unauthorized}))
        self.assertEqual("unauthorized", result["classification"]["access"])

        missing = Response(404, {"message": "ambiguous"})
        result, _ = probe.probe_pages(
            "repository-custom-property-values",
            f"/repos/{probe.OWNER}/{probe.REPOSITORY}/properties/values", False,
            "metadata:read", "token",
            Opener({probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/properties/values": missing}))
        self.assertEqual("not-found", result["classification"]["access"])
        self.assertEqual("unknown", result["classification"]["inheritance"])

    def test_allowlist_rejects_mutation_hosts_and_pagination_escape(self):
        self.assertFalse(probe.allowed_url("https://evil.example/repos/FS-GG/x"))
        self.assertFalse(probe.allowed_url(probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/actions/permissions?unexpected=1"))
        self.assertFalse(probe.allowed_url(probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}?page=2"))
        self.assertFalse(probe.allowed_url(probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/environments/x/secrets"))
        self.assertFalse(probe.allowed_url(probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/environments/x/secrets/name"))
        self.assertFalse(probe.allowed_url(
            probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/properties/values?per_page=100"))
        self.assertTrue(probe.allowed_url(
            probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/rulesets/17"))
        self.assertFalse(probe.allowed_url(
            probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/rulesets/17?includes_parents=true"))
        self.assertFalse(probe.allowed_url(
            probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/rulesets/%31%37"))
        with self.assertRaisesRegex(ValueError, "allowlist"):
            probe.get("https://evil.example/value", "token", Opener({}))
        path = f"/repos/{probe.OWNER}/{probe.REPOSITORY}/environments?per_page=100"
        escaped = Response(200, {"environments": []}, {"Link": '<https://evil.example/page=2>; rel="next"'})
        with self.assertRaisesRegex(ValueError, "allowlist"):
            probe.probe_pages("repository-environments", path, True, "actions:read", "token",
                              Opener({probe.API + path: escaped}))
        encoded_path = probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/%72ulesets?includes_parents=true&per_page=100&page=2"
        with self.assertRaisesRegex(ValueError, "escaped the allowlist"):
            probe.link_relations({"link": f'<{encoded_path}>; rel="next"'})

    def test_skipped_page_and_short_page_next_refuse(self):
        path = f"/repos/{probe.OWNER}/{probe.REPOSITORY}/rulesets?includes_parents=true&per_page=100"
        url = probe.API + path
        page4 = url + "&page=4"
        skipped = Response(200, [{}] * 100, links(next=page4, last=page4))
        with self.assertRaisesRegex(ValueError, "not sequential"):
            probe.probe_pages("repository-rulesets", path, True, "administration:read", "token",
                              Opener({url: skipped}))
        page2 = url + "&page=2"
        short = Response(200, [{}], links(next=page2, last=page2))
        with self.assertRaisesRegex(ValueError, "short page"):
            probe.probe_pages("repository-rulesets", path, True, "administration:read", "token",
                              Opener({url: short}))

    def test_duplicate_and_malformed_link_relations_refuse(self):
        path = f"/repos/{probe.OWNER}/{probe.REPOSITORY}/rulesets?includes_parents=true&per_page=100"
        url = probe.API + path
        page2 = url + "&page=2"
        duplicate = Response(200, [{}] * 100,
                             {"Link": f'<{page2}>; rel="next", <{page2}>; rel="next", <{page2}>; rel="last"'})
        with self.assertRaisesRegex(ValueError, "duplicate next"):
            probe.probe_pages("repository-rulesets", path, True, "administration:read", "token",
                              Opener({url: duplicate}))
        malformed = Response(200, [{}] * 100, {"Link": f'<{page2}>; next'})
        with self.assertRaisesRegex(ValueError, "malformed"):
            probe.probe_pages("repository-rulesets", path, True, "administration:read", "token",
                              Opener({url: malformed}))

    def test_terminal_page_requires_prior_relations_and_declared_last(self):
        path = f"/repos/{probe.OWNER}/{probe.REPOSITORY}/rulesets?includes_parents=true&per_page=100"
        url = probe.API + path
        page1 = url + "&page=1"
        page2 = url + "&page=2"
        first = Response(200, [{}] * 100, links(next=page2, last=page2))
        terminal = Response(200, [], links(first=page1, prev=page1))
        result, _ = probe.probe_pages(
            "repository-rulesets", path, True, "administration:read", "token",
            Opener({url: first, page2: terminal}))
        self.assertTrue(result["pages"][-1]["terminal"])
        self.assertEqual({"first": page1, "prev": page1}, result["pages"][-1]["links"])
        missing_prior = Response(200, [], links(first=page1))
        with self.assertRaisesRegex(ValueError, "prior pagination relations are incomplete"):
            probe.probe_pages("repository-rulesets", path, True, "administration:read", "token",
                              Opener({url: first, page2: missing_prior}))

    def test_custom_property_pages_refuse_partial_and_escaped_continuations(self):
        path = f"/repos/{probe.OWNER}/{probe.REPOSITORY}/properties/values"
        url = probe.API + path
        page2 = url + "?page=2"
        partial = Response(200, [{"property_name": "partial", "value": "x"}],
                           links(next=page2, last=page2))
        with self.assertRaisesRegex(ValueError, "allowlist|unexpectedly paginated"):
            probe.probe_pages("repository-custom-property-values", path, False, "metadata:read", "token",
                              Opener({url: partial}))
        escaped = Response(200, [{}] * 100,
                           links(next=probe.API + f"/repos/{probe.OWNER}/foreign/properties/values?page=2",
                                 last=page2))
        with self.assertRaisesRegex(ValueError, "allowlist"):
            probe.probe_pages("repository-custom-property-values", path, False, "metadata:read", "token",
                              Opener({url: escaped}))

    def test_custom_property_bytes_and_typed_values_fail_closed(self):
        with self.assertRaisesRegex(ValueError, "duplicate JSON member"):
            probe.decode_collection(
                b'[{"property_name":"one","property_name":"two"}]', None,
                "repository-custom-property-values")

        schema = provider_values()[
            probe.API + f"/orgs/{probe.OWNER}/properties/schema"].raw
        unknown = json.dumps([{"property_name": "foreign", "value": "x"}]).encode()
        with self.assertRaisesRegex(ValueError, "unknown or duplicated"):
            probe.custom_property_summary([schema], [unknown])
        invalid = json.dumps([{"property_name": "sensitive_tier_property", "value": "bronze"}]).encode()
        with self.assertRaisesRegex(ValueError, "violates"):
            probe.custom_property_summary([schema], [invalid])

    def test_second_read_requires_exact_raw_bytes(self):
        token = "ghs_" + "s" * 40
        values = provider_values()
        path = probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/properties/values"
        first = Response(200, [{"property_name": "sensitive_team_property", "value": "core"},
                               {"property_name": "sensitive_tier_property", "value": "gold"}])
        second = Response(200, [])
        second.raw = b'[ {"property_name":"sensitive_team_property","value":"core"},{"property_name":"sensitive_tier_property","value":"gold"} ]'
        values[path] = [first, second]
        with tempfile.TemporaryDirectory() as directory:
            proof = Path(directory) / "mint.json"
            proof.write_text(json.dumps(mint_proof(token)), encoding="utf-8")
            with mock.patch.object(probe, "source_binding", return_value={"headSha": "b" * 40}):
                with self.assertRaisesRegex(ValueError, "response digest|raw response changed"):
                    probe.build_report(token, proof, ROOT, "b" * 40, Opener(values))

    def test_ruleset_bypass_omission_is_partial_and_detail_drift_refuses(self):
        detail_url = probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/rulesets/17"
        values = provider_values()
        omitted = ruleset_detail()
        omitted.pop("bypass_actors")
        values[detail_url] = Response(200, omitted)
        report = self.build(values)
        self.assertEqual("partial-bypass-actors-omitted", report["repositoryRulesets"]["verdict"])
        self.assertFalse(report["repositoryRulesets"]["bypassActorsVisible"])

        values = provider_values()
        values[detail_url] = [Response(200, ruleset_detail()),
                              Response(200, ruleset_detail(enforcement="disabled"))]
        with self.assertRaisesRegex(ValueError, "detail request URI|detail bytes changed"):
            self.build(values)

    def test_ruleset_detail_rejects_inherited_push_and_unknown_shapes(self):
        cases = {
            "inherited": (ruleset_detail(source_type="Organization", source=probe.OWNER), "inherited or foreign"),
            "push": (ruleset_detail(target="push"), "push or unknown"),
            "conditions": (ruleset_detail(conditions={"repository_name": {}}), "conditions have an unknown"),
            "rule": (ruleset_detail(rules=[{"type": "future_rule"}]), "unknown rule shape"),
            "actor": (ruleset_detail(bypass_actors=[{"actor_id": 1, "actor_type": "FutureActor",
                                                       "bypass_mode": "always"}]), "actor is unsupported"),
        }
        for name, (detail, message) in cases.items():
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, message):
                values = provider_values()
                values[probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/rulesets/17"] = Response(200, detail)
                self.build(values)

        values = provider_values()
        list_url = probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/rulesets?includes_parents=true&per_page=100"
        values[list_url] = Response(200, [{"id": 17, "node_id": "RRS_17",
                                           "source_type": "Organization", "source": probe.OWNER}])
        with self.assertRaisesRegex(ValueError, "inherited or foreign ruleset list"):
            self.build(values)

    def test_ruleset_detail_denials_are_not_authority(self):
        expected = {401: "refused-unauthorized", 403: "refused-forbidden", 404: "unknown-not-found"}
        detail_url = probe.API + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/rulesets/17"
        for status, verdict in expected.items():
            values = provider_values()
            values[detail_url] = Response(status, {"message": "unavailable"})
            with self.subTest(status=status):
                report = self.build(values)
                self.assertEqual(verdict, report["repositoryRulesets"]["verdict"])
                self.assertFalse(report["repositoryRulesets"]["bypassActorsVisible"])

    def test_missing_custom_property_grant_and_private_ruleset_403_are_diagnostic_success(self):
        values = provider_values()
        schema_url = probe.API + f"/orgs/{probe.OWNER}/properties/schema"
        rulesets_url = (probe.API
                        + f"/repos/{probe.OWNER}/{probe.REPOSITORY}/rulesets"
                        + "?includes_parents=true&per_page=100")
        values[schema_url] = Response(403, {"message": "permission unavailable"})
        values[rulesets_url] = Response(403, {"message": "private plan or permission unavailable"})

        report = self.build(values)

        self.assertTrue(report["diagnostic"]["collected"])
        self.assertTrue(report["diagnostic"]["expectedStatusSet"])
        self.assertFalse(report["authority"]["qualified"])
        self.assertEqual("missing-from-mint-proof",
                         report["customProperties"]["permissionEvidence"]["disposition"])
        self.assertEqual("refused-forbidden", report["customProperties"]["verdict"])
        self.assertEqual("permission-or-plan", report["customProperties"]["refusalReason"])
        self.assertEqual(403, report["customProperties"]["schemaStatus"])
        self.assertEqual("refused-forbidden", report["repositoryRulesets"]["verdict"])
        self.assertEqual("permission-or-plan", report["repositoryRulesets"]["refusalReason"])
        self.assertEqual(403, report["repositoryRulesets"]["listStatus"])
        self.assertEqual([], report["repositoryRulesets"]["detailStatuses"])
        self.assertIsNone(report["customProperties"]["summary"])

    def test_malformed_parent_rows_cannot_claim_observed_absence(self):
        token = "ghs_" + "r" * 40
        values = provider_values()
        rules_path = next(path for name, path, _, _ in probe.STATIC_PROBES if name == "repository-rulesets")
        values[probe.API + rules_path] = Response(200, [{"id": 17, "source_type": "Organization"}])
        with tempfile.TemporaryDirectory() as directory:
            proof = Path(directory) / "mint.json"
            proof.write_text(json.dumps(mint_proof(token)), encoding="utf-8")
            with mock.patch.object(probe, "source_binding", return_value={"headSha": "b" * 40}):
                with self.assertRaisesRegex(ValueError, "ruleset parent identity is incomplete"):
                    probe.build_report(token, proof, ROOT, "b" * 40, Opener(values))

    def test_mint_proof_preserves_missing_optional_grant_and_refuses_extra_write_and_token_mismatch(self):
        token = "ghs_" + "a" * 40
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "proof.json"
            changed = mint_proof(token)
            changed["permissions"]["environments"] = "write"
            path.write_text(json.dumps(changed), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "unexpected write"):
                probe.load_mint_proof(path, token)
            changed = mint_proof(token)
            path.write_text(json.dumps(changed), encoding="utf-8")
            loaded = probe.load_mint_proof(path, token)
            self.assertNotIn("organization_custom_properties", loaded["permissions"])
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
        self.assertIn(".diagnostic.collected", text)
        self.assertIn(".diagnostic.expectedStatusSet", text)
        self.assertIn(".authority.qualified", text)
        self.assertIn(".repositoryRulesets.permissionEvidence.administration", text)
        self.assertNotIn(".app.permissions.organization_custom_properties", text)
        self.assertNotIn(".customProperties.verdict", text)
        self.assertNotIn(".repositoryRulesets.verdict", text)
        self.assertNotIn("permission-actions:", text)
        self.assertNotIn("permission-environments:", text)


if __name__ == "__main__":
    unittest.main()
