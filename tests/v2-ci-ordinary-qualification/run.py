#!/usr/bin/env python3
from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
POLICY_PATH = ROOT / "policy/v2-ci-ordinary-settlement.json"
TOOL = ROOT / "tools/v2-ci-ordinary-qualification.py"
SPEC = importlib.util.spec_from_file_location("v2_ci_qualification", TOOL)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)
SOURCE = "53a0f6c8f03bb8c4a60d55c3ce8a38c78a26b1b5"
HEAD = "353ff86a50808959770a73863385646efaf69969"


class OrdinarySettlementQualificationTests(unittest.TestCase):
    def setUp(self):
        self.policy = json.loads(POLICY_PATH.read_text())
        self.digest = hashlib.sha256(POLICY_PATH.read_bytes()).hexdigest()
        self.runtime = {
            "schema": "fsgg.github.v2-ci-runtime/1",
            "eventName": "push",
            "repository": "FS-GG/.github",
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
                {"name": "contract-coherence / coherence", "conclusion": "success", "sourceSha": HEAD, "appId": 15368},
                {"name": "routine-eligibility", "conclusion": "success", "sourceSha": HEAD, "appId": 15368},
            ],
        }

    def qualify(self, runtime=None, associations=None, evidence=None):
        return MODULE.qualify(self.policy, self.digest, runtime or self.runtime,
                              associations if associations is not None else self.associations,
                              evidence or self.evidence)

    def refuses(self, *, runtime=None, associations=None, evidence=None, contains=None):
        with self.assertRaisesRegex(MODULE.Refusal, contains or "."):
            self.qualify(runtime, associations, evidence)

    def test_accepts_live_single_merged_pr_association_and_emits_secret_free_receipt(self):
        receipt = self.qualify()
        self.assertEqual(3662, receipt["pullRequest"])
        self.assertEqual(SOURCE, receipt["mergeCommitSha"])
        self.assertFalse(receipt["credentialAccess"])

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

    def test_policy_inventory_is_new_unprovisioned_and_keeps_current_gates(self):
        self.assertEqual("source-qualified-not-installed", self.policy["status"])
        self.assertEqual("trusted-main-push-predecessor", self.policy["qualification"]["producerStatus"])
        self.assertEqual("native-API-derived-evidence",
                         self.policy["qualification"]["currentValidatorRole"])
        self.assertEqual(15368, self.policy["qualification"]["requiredCheckAppId"])
        inventory = self.policy["credentialInventory"]
        self.assertTrue(inventory)
        self.assertTrue(all(item["generation"] == "new-v2-dedicated" for item in inventory))
        self.assertTrue(all(item["provisioned"] is False for item in inventory))
        self.assertEqual({"v1-admission-genesis", "OpenV2"}, set(self.policy["unchangedGates"]))
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
        self.assertEqual(0, observation["secretCount"])
        self.assertEqual("inert-shell-not-activation", observation["disposition"])


if __name__ == "__main__":
    unittest.main()
