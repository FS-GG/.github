#!/usr/bin/env python3
from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import pathlib
import unittest
from unittest import mock
import io
from contextlib import redirect_stdout


ROOT = pathlib.Path(__file__).resolve().parents[2]
POLICY_PATH = ROOT / "policy/v2-ci-ordinary-settlement.json"
TOOL = ROOT / "tools/v2-ci-ordinary-qualification.py"
SPEC = importlib.util.spec_from_file_location("v2_ci_qualification", TOOL)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)
APP_SPEC = importlib.util.spec_from_file_location("app_readback", ROOT / "tools/v2-ci-ordinary-app-readback.py")
APP = importlib.util.module_from_spec(APP_SPEC)
APP_SPEC.loader.exec_module(APP)
SOURCE = "53a0f6c8f03bb8c4a60d55c3ce8a38c78a26b1b5"
HEAD = "353ff86a50808959770a73863385646efaf69969"


class OrdinarySettlementQualificationTests(unittest.TestCase):
    def setUp(self):
        self.policy = json.loads(POLICY_PATH.read_text())
        self.digest = hashlib.sha256(POLICY_PATH.read_bytes()).hexdigest()
        self.runtime = {
            "schema": "fsgg.github.v2-ci-runtime/1",
            "eventName": "push",
            "sourceProfile": "dotgithub-v1",
            "repository": "FS-GG/.github",
            "repositoryId": 1269292704,
            "ref": "refs/heads/main",
            "eventAfter": SOURCE,
            "sourceSha": SOURCE,
            "workflowPath": ".github/workflows/v2-ci-ordinary-settlement.yml",
            "workflowRevision": SOURCE,
            "environment": "ordinary-v2",
            "runner": "github-hosted-ephemeral",
            "operationClass": "ordinary-post-merge-delivery-settlement",
            "phase": "secret-free-predecessor",
            "credentialAccess": False,
        }
        self.associations = [{
            "number": 3662,
            "node_id": "PR_kwDOOrdinary3662",
            "state": "closed",
            "merged_at": "2026-09-24T13:09:45Z",
            "merge_commit_sha": SOURCE,
            "head": {"sha": HEAD},
            "base": {"ref": "main", "sha": "af5a748d075d6578300822c8b64251c7c85b3f91", "repo": {"full_name": "FS-GG/.github"}},
        }]
        def check(name, index):
            producer = self.policy["qualification"]["checkProducers"][name]
            return {"name": name, "conclusion": "success", "sourceSha": HEAD, "appId": 15368,
                    "checkRunId": index + 1, "workflowRunId": 1000 + index,
                    "runAttempt": 1, "checkSuiteId": 2000 + index,
                    "workflowId": producer["workflowId"], "workflowPath": producer["path"]}
        self.evidence = {
            "schema": "fsgg.github.v2-ci-qualification-evidence/1",
            "status": "passed",
            "subject": {
                "sourceSha": SOURCE,
                "qualificationSha": HEAD,
                "workflowPath": ".github/workflows/v2-ci-ordinary-settlement.yml",
                "workflowRevision": SOURCE,
                "environment": "ordinary-v2",
                "operationClass": "ordinary-post-merge-delivery-settlement",
                "policySha256": self.digest,
            },
            "checks": [
                check(name, index)
                for index, name in enumerate(self.policy["qualification"]["requiredChecks"])
            ],
            "gateChecks": [
                check(name, index)
                for index, name in enumerate(self.policy["qualification"]["requiredGateChecks"])
            ],
        }

    def qualify(self, runtime=None, associations=None, evidence=None):
        return MODULE.qualify(self.policy, self.digest, runtime or self.runtime,
                              associations if associations is not None else self.associations,
                              evidence or self.evidence)

    def select_audio(self):
        profile = MODULE.AUDIO_SOURCE_PROFILE
        self.runtime["sourceProfile"] = profile["key"]
        self.runtime["repository"] = profile["repository"]
        self.runtime["repositoryId"] = profile["repositoryId"]
        self.associations[0]["base"]["repo"]["full_name"] = profile["repository"]

        def check(name, index):
            producer = profile["checkProducers"][name]
            return {"name": name, "conclusion": "success", "sourceSha": HEAD, "appId": 15368,
                    "checkRunId": index + 1, "workflowRunId": 1000 + index,
                    "runAttempt": 1, "checkSuiteId": 2000 + index,
                    "workflowId": producer["workflowId"], "workflowPath": producer["path"]}

        self.evidence["checks"] = [check(name, index)
                                   for index, name in enumerate(profile["requiredChecks"])]
        self.evidence["gateChecks"] = [check(name, index)
                                       for index, name in enumerate(profile["requiredGateChecks"])]

    def refuses(self, *, runtime=None, associations=None, evidence=None, contains=None):
        with self.assertRaisesRegex(MODULE.Refusal, contains or "."):
            self.qualify(runtime, associations, evidence)

    def test_accepts_live_single_merged_pr_association_and_emits_secret_free_receipt(self):
        receipt = self.qualify()
        self.assertEqual(3662, receipt["pullRequest"])
        self.assertEqual("dotgithub-v1", receipt["sourceProfile"])
        self.assertEqual(1269292704, receipt["sourceRepositoryId"])
        self.assertEqual(SOURCE, receipt["mergeCommitSha"])
        self.assertFalse(receipt["credentialAccess"])
        self.assertEqual({"contract-coherence / coherence", "routine-eligibility"},
                         {check["name"] for check in receipt["requiredChecks"]})
        self.assertEqual(8, len(receipt["requiredGateChecks"]))

    def test_audio_profile_uses_only_code_owned_identity_checks_and_producers(self):
        self.select_audio()
        receipt = MODULE.qualify(
            self.policy, self.digest, self.runtime, self.associations, self.evidence, "audio-v1")
        self.assertEqual("audio-v1", receipt["sourceProfile"])
        self.assertEqual("FS-GG/FS.GG.Audio", receipt["sourceRepository"])
        self.assertEqual(1292226968, receipt["sourceRepositoryId"])
        self.assertEqual(5, len(receipt["requiredChecks"]))
        self.assertEqual(4, len(receipt["requiredGateChecks"]))

        runtime = copy.deepcopy(self.runtime)
        runtime["sourceProfile"] = "dotgithub-v1"
        with self.assertRaisesRegex(MODULE.Refusal, "wrong source profile"):
            MODULE.qualify(self.policy, self.digest, runtime, self.associations,
                           self.evidence, "audio-v1")
        runtime = copy.deepcopy(self.runtime)
        runtime["repositoryId"] = 1269292704
        with self.assertRaisesRegex(MODULE.Refusal, "wrong source repository identity"):
            MODULE.qualify(self.policy, self.digest, runtime, self.associations,
                           self.evidence, "audio-v1")
        with self.assertRaisesRegex(MODULE.Refusal, "unknown ordinary-v2 source profile"):
            MODULE.qualify(self.policy, self.digest, self.runtime, self.associations,
                           self.evidence, "untrusted-runtime-profile")

    def test_refuses_request_and_manual_events(self):
        for event in ("pull_request", "pull_request_target", "workflow_dispatch", "repository_dispatch"):
            runtime = copy.deepcopy(self.runtime)
            runtime["eventName"] = event
            self.refuses(runtime=runtime, contains="wrong event")

    def test_refuses_wrong_source_and_non_main(self):
        runtime = copy.deepcopy(self.runtime)
        runtime["eventAfter"] = "a" * 40
        self.refuses(runtime=runtime, contains="wrong source")
        runtime = copy.deepcopy(self.runtime)
        runtime["ref"] = "refs/heads/feature"
        self.refuses(runtime=runtime, contains="protected main")
        associations = copy.deepcopy(self.associations)
        associations[0]["merge_commit_sha"] = "b" * 40
        self.refuses(associations=associations, contains="associated merge commit")

    def test_refuses_zero_multiple_unmerged_and_wrong_repository_associations(self):
        self.refuses(associations=[], contains="exactly one")
        self.refuses(associations=self.associations * 2, contains="exactly one")
        associations = copy.deepcopy(self.associations)
        associations[0]["merged_at"] = None
        self.refuses(associations=associations, contains="not merged")
        associations = copy.deepcopy(self.associations)
        associations[0]["base"]["repo"]["full_name"] = "FS-GG/other"
        self.refuses(associations=associations, contains="wrong repository")

    def test_refuses_wrong_workflow_environment_runner_phase_and_class(self):
        cases = {
            "workflowPath": ("other.yml", "wrong workflow"),
            "workflowRevision": ("c" * 40, "wrong workflow revision"),
            "environment": ("v1-admission", "wrong environment"),
            "runner": ("self-hosted", "wrong runner class"),
            "phase": ("credential-job", "credential use is forbidden"),
            "credentialAccess": (True, "credential use is forbidden"),
            "operationClass": ("cutover", "unsupported operation class"),
        }
        for field, (value, message) in cases.items():
            runtime = copy.deepcopy(self.runtime)
            runtime[field] = value
            self.refuses(runtime=runtime, contains=message)

    def test_refuses_stale_or_failed_qualification_evidence(self):
        for field, value in (
            ("sourceSha", "d" * 40),
            ("qualificationSha", "d" * 40),
            ("workflowRevision", "e" * 40),
            ("environment", "old-environment"),
            ("operationClass", "release"),
            ("policySha256", "f" * 64),
        ):
            evidence = copy.deepcopy(self.evidence)
            evidence["subject"][field] = value
            self.refuses(evidence=evidence, contains="stale or mismatched")
        evidence = copy.deepcopy(self.evidence)
        evidence["checks"][0]["conclusion"] = "failure"
        self.refuses(evidence=evidence, contains="failed or stale")
        evidence = copy.deepcopy(self.evidence)
        evidence["checks"].pop()
        self.refuses(evidence=evidence, contains="population")
        evidence = copy.deepcopy(self.evidence)
        evidence["gateChecks"].pop()
        self.refuses(evidence=evidence, contains="population")
        evidence = copy.deepcopy(self.evidence)
        evidence["checks"][0]["workflowId"] = 1
        self.refuses(evidence=evidence, contains="native producer")

    def test_policy_inventory_is_enrolled_and_keeps_current_gates(self):
        self.assertEqual("installed", self.policy["status"])
        self.assertTrue(self.policy["credentialJob"]["installed"])
        self.assertEqual("trusted-main-push-predecessor", self.policy["qualification"]["producerStatus"])
        self.assertEqual("native-API-derived-evidence",
                         self.policy["qualification"]["currentValidatorRole"])
        self.assertEqual(15368, self.policy["qualification"]["requiredCheckAppId"])
        inventory = self.policy["credentialInventory"]
        anchor = json.loads((ROOT / "policy/v2-ci-ordinary-settlement-anchor.json").read_text())
        self.assertTrue(inventory)
        self.assertTrue(all(item["generation"] == "new-v2-dedicated" for item in inventory))
        self.assertTrue(all(item["provisioned"] is True for item in inventory))
        self.assertEqual(5064713, anchor["writer"]["appId"])
        self.assertEqual(164553252, anchor["writer"]["installationId"])
        self.assertTrue(all(str(anchor["writer"]["appId"]) in item["publicIdentity"]
                            for item in inventory if item["name"] != "V2_ORDINARY_AUTHORIZER_PRIVATE_KEY"))
        self.assertTrue(any(anchor["authorizer"]["publicKeySpkiSha256"] in item["publicIdentity"]
                            for item in inventory))
        self.assertEqual(["OpenV2"], self.policy["unchangedGates"])
        names = {item["name"] for item in inventory}
        self.assertFalse(names.intersection(self.policy["forbiddenCredentialReuse"]))
        observation = self.policy["credentialJob"]["liveObservation"]
        self.assertEqual(60918716, observation["branchPolicyId"])
        self.assertEqual("main", observation["customBranchPolicy"])
        self.assertEqual(1, observation["customBranchPolicyCount"])
        self.assertFalse(observation["protectedBranches"])
        self.assertTrue(observation["customBranchPolicies"])
        self.assertTrue(observation["canAdminsBypass"])
        self.assertEqual(0, observation["requiredReviewerCount"])
        self.assertEqual(3, observation["secretCount"])
        self.assertEqual("dedicated-custody-enrolled-pending-isolated-rehearsal", observation["disposition"])

    def test_activation_requires_matching_active_and_inactive_status(self):
        self.policy["credentialJob"]["installed"] = False
        self.refuses(contains="activation status differs")
        self.policy["status"] = "source-qualified-not-installed"
        self.assertEqual("qualified", self.qualify()["status"])
        self.policy["credentialJob"]["installed"] = True
        self.refuses(contains="activation status differs")
        self.policy["status"] = "installed"
        self.assertEqual("qualified", self.qualify()["status"])

    def test_rehearsal_custody_is_disjoint_from_production(self):
        production = json.loads((ROOT / "policy/v2-ci-ordinary-settlement-anchor.json").read_text())
        rehearsal = json.loads((ROOT / "policy/v2-ci-ordinary-settlement-rehearsal-anchor.json").read_text())
        self.assertNotEqual(production["writer"]["appId"], rehearsal["writer"]["appId"])
        self.assertNotEqual(production["writer"]["installationId"], rehearsal["writer"]["installationId"])
        self.assertNotEqual(production["authorizer"]["publicKeySpkiSha256"],
                            rehearsal["authorizer"]["publicKeySpkiSha256"])
        self.assertEqual("FS-GG/FS.GG.Coordination.Authority.Sandbox", rehearsal["writer"]["repository"])
        self.assertEqual(1385801070, rehearsal["writer"]["repositoryId"])
        self.assertEqual([{"actorId": rehearsal["writer"]["appId"], "actorType": "Integration", "bypassMode": "always"}],
                         rehearsal["rulesets"]["writer"]["bypassActors"])
        self.assertEqual([], rehearsal["rulesets"]["integrity"]["bypassActors"])


class MainAppReadbackTests(unittest.TestCase):
    def native(self, path, token, body=None):
        self.calls.append((path, token, body))
        if path.endswith("/rulesets/24802693"):
            return {"id": 24802693, "bypass_actors": [
                {"actor_id": 4882140, "actor_type": "Integration", "bypass_mode": "always"},
                {"actor_id": 5064713, "actor_type": "Integration", "bypass_mode": "always"}]}
        if path.endswith("/rulesets/24802698"):
            return {"id": 24802698, "bypass_actors": []}
        if path.endswith("/rules/branches/main"):
            return [{"type": kind, "ruleset_id": identifier, "ruleset_source_type": "Repository",
                     "ruleset_source": "FS-GG/FS.GG.Coordination.Authority"}
                    for kind, identifier in (("creation", 24802693), ("update", 24802693),
                                             ("deletion", 24802698), ("non_fast_forward", 24802698))]
        raise AssertionError(path)

    def setUp(self):
        self.calls = []

    def test_present_empty_and_missing_bypass_rosters_are_distinct(self):
        with mock.patch.object(APP, "request", side_effect=self.native):
            complete = APP.main_rules_readback("FS-GG/FS.GG.Coordination.Authority", "selected-token")
        self.assertTrue(complete["bypassRosterAvailable"])
        self.assertEqual([], complete["rulesets"][1]["bypassActors"])
        self.assertEqual([5, 0, 6, 4, 7, 1, 3], complete["rulesets"][0]["bypassActors"][1]["actorIdDigits"])
        original = self.native
        def redacted(path, token, body=None):
            result = original(path, token, body)
            if path.endswith("/rulesets/24802693"):
                result.pop("bypass_actors")
            return result
        with mock.patch.object(APP, "request", side_effect=redacted):
            missing = APP.main_rules_readback("FS-GG/FS.GG.Coordination.Authority", "selected-token")
        self.assertFalse(missing["bypassRosterAvailable"])
        self.assertFalse(missing["rulesets"][0]["bypassActorsFieldPresent"])
        self.assertIsNone(missing["rulesets"][0]["bypassActors"])

    def test_http_refusal_is_unknown_and_independent_reads_continue(self):
        def refused(path, token, body=None):
            if path.endswith("/rulesets/24802693"):
                self.calls.append((path, token, body))
                raise APP.ReadbackUnavailable(path, 403)
            return self.native(path, token, body)
        with mock.patch.object(APP, "request", side_effect=refused):
            result = APP.main_rules_readback("FS-GG/FS.GG.Coordination.Authority", "selected-token")
        self.assertFalse(result["observationComplete"])
        self.assertFalse(result["bypassRosterAvailable"])
        self.assertEqual(403, result["rulesets"][0]["httpStatus"])
        self.assertEqual(3, len(self.calls))
        self.assertTrue(all(token == "selected-token" and body is None for _, token, body in self.calls))

    def test_probe_uses_narrowed_production_token_and_does_not_print_credentials(self):
        repository = "FS-GG/FS.GG.Coordination.Authority"
        permissions = {"contents": "write", "metadata": "read"}
        def api(path, token, body=None):
            if path == "/app":
                return {"id": 5064713, "name": "FS-GG Ordinary V2 Settlement", "owner": {"login": "FS-GG"}, "permissions": permissions, "events": []}
            if path.endswith("/installation"):
                return {"id": 164553252, "app_id": 5064713, "account": {"login": "FS-GG"}, "repository_selection": "selected", "permissions": permissions, "events": [], "suspended_at": None}
            if path.endswith("/access_tokens"):
                self.calls.append((path, token, body))
                return {"token": "selected-token" if "repository_ids" in body else "metadata-token", "permissions": permissions}
            if path.startswith("/installation/repositories"):
                self.calls.append((path, token, body))
                return {"total_count": 1, "repositories": [{"id": 1351660651, "full_name": repository}]}
            return self.native(path, token, body)
        env = {"FSGG_APP_ID": "5064713", "FSGG_EXPECTED_APP_ID": "5064713", "FSGG_EXPECTED_NAME": "FS-GG Ordinary V2 Settlement",
               "FSGG_EXPECTED_REPOSITORY": repository, "FSGG_EXPECTED_REPOSITORY_ID": "1351660651", "FSGG_APP_PRIVATE_KEY": "private-key-sentinel", "FSGG_MAIN_RULE_READBACK": "1"}
        output = io.StringIO()
        with mock.patch.dict(APP.os.environ, env, clear=True), mock.patch.object(APP, "app_jwt", return_value="jwt-sentinel"), \
             mock.patch.object(APP, "request", side_effect=api), redirect_stdout(output):
            self.assertEqual(0, APP.main())
        self.assertIn(("/app/installations/164553252/access_tokens", "jwt-sentinel", {"repository_ids": [1351660651], "permissions": {"contents": "write"}}), self.calls)
        rules = [(path, token, body) for path, token, body in self.calls if "/rules" in path]
        self.assertEqual(3, len(rules))
        self.assertTrue(all(token == "selected-token" and body is None for _, token, body in rules))
        for secret in ("private-key-sentinel", "jwt-sentinel", "selected-token", "metadata-token"):
            self.assertNotIn(secret, output.getvalue())
        self.assertTrue(json.loads(output.getvalue())["mainRuleReadback"]["bypassRosterAvailable"])

    def test_production_selection_does_not_exercise_retired_rehearsal_secrets(self):
        workflow = (ROOT / ".github/workflows/v2-ci-ordinary-app-readback.yml").read_text()
        self.assertIn("default: production", workflow)
        production, retired = workflow.split("  rehearsal:", 1)
        self.assertIn("inputs.profile == 'production'", production)
        self.assertIn('FSGG_MAIN_RULE_READBACK: "1"', production)
        self.assertNotIn("secrets.", retired)
        self.assertNotIn("environment:", retired)
        self.assertIn("exit 2", retired)


if __name__ == "__main__":
    unittest.main()
