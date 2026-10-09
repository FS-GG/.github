#!/usr/bin/env python3
from __future__ import annotations

import copy
import importlib.util
import json
import pathlib
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "protected_intake_preflight", ROOT / "tools/protected-intake-preflight.py"
)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


def evidence() -> dict:
    _, anchor = MODULE.static_candidate()
    source = "a" * 40
    return {
        "schema": "fsgg.github.protected-intake-live-evidence/1",
        "sourceSha": source,
        "sourceFiles": {
            str(path.relative_to(ROOT)): MODULE.sha256(path.read_bytes())
            for path in (MODULE.POLICY_PATH, MODULE.ANCHOR_PATH, MODULE.WORKFLOW_PATH, MODULE.SCRIPT_PATH)
        },
        "pullRequest": {
            "number": 4000,
            "baseSha": "b" * 40,
            "headSha": "c" * 40,
            "mergeCommitSha": source,
        },
        "reviewCommentsComplete": True,
        "reviewDecisionRecords": [],
        "writerRuleset": copy.deepcopy(anchor["rulesets"]["writer"]),
        "integrityRuleset": copy.deepcopy(anchor["rulesets"]["integrity"]),
        "bypassActorsReadbackComplete": True,
        "effectiveRules": copy.deepcopy(anchor["effectiveRules"]),
        "refBefore": "absent",
    }


class ProtectedIntakePreflightTests(unittest.TestCase):
    def paged(self, pages):
        calls = []

        def fetch(path):
            calls.append(path)
            return pages[len(calls) - 1]

        values = MODULE._paged_dicts("/repos/FS-GG/.github/issues/1/comments", "comments", fetch)
        return values, calls

    def test_candidate_is_inert_intake_specific_and_not_an_accepted_anchor(self):
        policy, anchor = MODULE.static_candidate()
        self.assertFalse(policy["activation"])
        self.assertEqual("candidate-source-only", policy["status"])
        self.assertEqual("candidate-not-accepted", anchor["status"])
        self.assertEqual("retained-intake-journal", anchor["operationClass"])
        self.assertEqual("refs/heads/fsgg/v2/journal/operation/13", anchor["ref"])
        self.assertEqual(5064713, anchor["writer"]["appId"])
        self.assertEqual(164553252, anchor["writer"]["installationId"])
        self.assertIsNone(anchor["nativeAuthorization"])
        self.assertIsNone(anchor["acceptedAt"])
        self.assertIsNone(anchor["authorizer"]["keyId"])
        self.assertNotIn("ordinary-post-merge-delivery-settlement", str(policy) + str(anchor))
        self.assertNotIn("ordinary-v2-production", str(policy) + str(anchor))

    def test_exact_public_protection_and_absence_still_cannot_activate(self):
        receipt = MODULE.evaluate(evidence())
        self.assertTrue(receipt["protectionMatchesCandidate"])
        self.assertTrue(receipt["expectedAbsentObserved"])
        self.assertFalse(receipt["activation"])
        self.assertEqual(6, len(receipt["blockers"]))
        self.assertIn("native-review-decision-v2-not-bound", receipt["blockers"])
        self.assertIn("intake-app-credential-not-installed", receipt["blockers"])

    def test_unbound_review_records_cannot_upgrade_the_candidate(self):
        observed = evidence()
        observed["reviewDecisionRecords"] = [
            {"id": 123, "bodySha256": "d" * 64}
        ]
        receipt = MODULE.evaluate(observed)
        self.assertEqual(1, receipt["nativeReviewRecordCount"])
        self.assertFalse(receipt["activation"])
        self.assertIn("native-review-decision-v2-not-bound", receipt["blockers"])

    def test_public_actor_redaction_is_incomplete_and_stays_blocked(self):
        observed = evidence()
        observed["writerRuleset"]["bypassAppIds"] = []
        observed["bypassActorsReadbackComplete"] = False
        receipt = MODULE.evaluate(observed)
        self.assertTrue(receipt["visibleProtectionMatchesCandidate"])
        self.assertFalse(receipt["exactBypassRosterReadback"])
        self.assertFalse(receipt["protectionMatchesCandidate"])
        self.assertIn("exact-bypass-roster-readback-unavailable", receipt["blockers"])

    def test_effective_rule_api_shape_is_normalized_and_ordered(self):
        raw = [
            {"type": "deletion", "ruleset_id": 21872115, "ruleset_source_type": "Repository", "ruleset_source": "FS-GG/FS.GG.Coordination.Authority"},
            {"type": "non_fast_forward", "ruleset_id": 21872115, "ruleset_source_type": "Repository", "ruleset_source": "FS-GG/FS.GG.Coordination.Authority"},
            {"type": "creation", "ruleset_id": 21872113, "ruleset_source_type": "Repository", "ruleset_source": "FS-GG/FS.GG.Coordination.Authority"},
            {"type": "update", "ruleset_id": 21872113, "ruleset_source_type": "Repository", "ruleset_source": "FS-GG/FS.GG.Coordination.Authority"},
        ]
        _, anchor = MODULE.static_candidate()
        self.assertEqual(anchor["effectiveRules"], MODULE.normalize_effective_rules(raw))

    def test_receipt_verifier_requires_public_roster_incompleteness(self):
        observed = evidence()
        observed["writerRuleset"]["bypassAppIds"] = []
        observed["bypassActorsReadbackComplete"] = False
        receipt = MODULE.evaluate(observed)
        path = ROOT / "tests/protected-intake-preflight/.receipt.tmp.json"
        try:
            path.write_text(json.dumps(receipt), encoding="utf-8")
            self.assertEqual(0, MODULE.main(["verify", str(path)]))
            receipt["exactBypassRosterReadback"] = True
            receipt["protectionMatchesCandidate"] = True
            receipt["blockers"].remove("exact-bypass-roster-readback-unavailable")
            path.write_text(json.dumps(receipt), encoding="utf-8")
            with self.assertRaisesRegex(MODULE.Refusal, "exact blocked candidate"):
                MODULE.verify_receipt(path)
        finally:
            path.unlink(missing_ok=True)

    def test_ruleset_roster_timestamp_effective_rules_and_absence_drift_refuse(self):
        mutations = []
        changed = evidence()
        changed["writerRuleset"]["bypassAppIds"] = [5064713]
        mutations.append((changed, "writer ruleset drift"))
        changed = evidence()
        changed["writerRuleset"]["updatedAt"] = "2026-09-25T00:00:00.000000Z"
        mutations.append((changed, "writer ruleset drift"))
        changed = evidence()
        changed["effectiveRules"] = changed["effectiveRules"][:-1]
        mutations.append((changed, "effective rules drift"))
        changed = evidence()
        changed["refBefore"] = "present-or-unreadable"
        mutations.append((changed, "operation/13 is not proven absent"))
        for value, message in mutations:
            with self.subTest(message=message):
                with self.assertRaisesRegex(MODULE.Refusal, message):
                    MODULE.evaluate(value)

    def test_partial_source_pr_and_comment_reads_refuse(self):
        changed = evidence()
        changed["sourceFiles"].pop("tools/protected-intake-preflight.py")
        with self.assertRaisesRegex(MODULE.Refusal, "source byte readback drift"):
            MODULE.evaluate(changed)
        changed = evidence()
        changed["pullRequest"]["mergeCommitSha"] = "e" * 40
        with self.assertRaisesRegex(MODULE.Refusal, "pull request readback drift"):
            MODULE.evaluate(changed)
        changed = evidence()
        changed["reviewCommentsComplete"] = False
        with self.assertRaisesRegex(MODULE.Refusal, "comment readback incomplete"):
            MODULE.evaluate(changed)

    def test_historical_workflow_remains_one_read_only_protected_main_preflight(self):
        source = (ROOT / "tests/protected-intake-preflight/fixtures/historical-workflow.yml").read_text(encoding="utf-8")
        self.assertIn("  push:\n    branches: [main]", source)
        self.assertIn("permissions: {}", source)
        self.assertIn("protected-intake-preflight.py static", source)
        self.assertIn("protected-intake-preflight.py produce", source)
        self.assertIn("protected-intake-preflight.py verify", source)
        self.assertIn('assert value["activation"] is False', source)
        self.assertEqual(1, source.count("  preflight:\n"))
        lowered = source.lower()
        for forbidden in (
            "workflow_dispatch:", "repository_dispatch:", "pull_request:",
            "secrets.", "github.token", "gh_token", "authorization:",
            "contents: write", "git push", "environment:",
        ):
            self.assertNotIn(forbidden, lowered)

    def test_current_workflow_cannot_automatically_read_or_replay_authority(self):
        source = MODULE.WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assertIn("  workflow_dispatch:", source)
        self.assertIn("permissions: {}", source)
        self.assertIn("no authority read or activation performed", source)
        for forbidden in ("  push:", "  schedule:", "  repository_dispatch:",
                          "  pull_request:", "  pull_request_target:", "produce", "git fetch",
                          "urllib", "gh api", "secrets.", "github.token"):
            self.assertNotIn(forbidden, source)

    def test_retired_produce_refuses_before_live_read_or_output_write(self):
        with mock.patch.object(MODULE, "collect_live") as collect, \
             mock.patch.object(pathlib.Path, "write_text") as write, \
             mock.patch("sys.stderr") as stderr:
            self.assertEqual(3, MODULE.main(["produce", "/must-not-write.json"]))
        collect.assert_not_called()
        write.assert_not_called()
        self.assertIn("live candidate preflight retired", "".join(str(c) for c in stderr.write.call_args_list))

    def test_source_uses_only_unauthenticated_bounded_reads(self):
        source = MODULE.SCRIPT_PATH.read_text(encoding="utf-8")
        self.assertIn("urllib.request.urlopen", source)
        self.assertIn('"User-Agent": "fsgg-protected-intake-preflight/1"', source)
        self.assertIn("for _ in range(100)", source)
        request_source = source.split("def _get", 1)[1].split("def _json", 1)[0]
        self.assertNotIn("Authorization", request_source)
        self.assertNotIn("GH_TOKEN", source)
        self.assertNotIn("subprocess", source)
        self.assertNotIn("/git/refs", source)
        self.assertIn("/git/ref/heads/fsgg/v2/journal/operation/13", source)

    def test_short_page_with_next_is_followed_until_terminal_link(self):
        endpoint = "https://api.github.com/repos/FS-GG/.github/issues/1/comments"
        values, calls = self.paged(
            [
                (200, [{"id": 1}], {"Link": f'<{endpoint}?per_page=100&page=2>; rel="next"'}),
                (200, [{"id": 2}], {}),
            ]
        )
        self.assertEqual([{"id": 1}, {"id": 2}], values)
        self.assertEqual(2, len(calls))
        self.assertTrue(calls[1].endswith("page=2"))

    def test_malformed_population_row_refuses_complete_census(self):
        with self.assertRaisesRegex(MODULE.Refusal, "malformed row"):
            self.paged([(200, [{"id": 1}, "not-an-object"], {})])

    def test_escaped_link_refuses_complete_census(self):
        escaped = (
            'https://api.github.com/repos/FS-GG/.github/issues/1/comments'
            '?per_page=100&page=%32'
        )
        with self.assertRaisesRegex(MODULE.Refusal, "malformed or escaped"):
            self.paged([(200, [{"id": 1}], {"Link": f'<{escaped}>; rel="next"'})])

    def test_multi_page_population_validates_scope_sequence_and_terminal_page(self):
        endpoint = "https://api.github.com/repos/FS-GG/.github/issues/1/comments"
        links = [
            f'<{endpoint}?per_page=100&page=2>; rel="next", <{endpoint}?per_page=100&page=3>; rel="last"',
            f'<{endpoint}?per_page=100&page=1>; rel="first", <{endpoint}?per_page=100&page=1>; rel="prev", <{endpoint}?per_page=100&page=3>; rel="next", <{endpoint}?per_page=100&page=3>; rel="last"',
            f'<{endpoint}?per_page=100&page=1>; rel="first", <{endpoint}?per_page=100&page=2>; rel="prev", <{endpoint}?per_page=100&page=3>; rel="last"',
        ]
        values, calls = self.paged(
            [(200, [{"id": page}], {"Link": links[page - 1]}) for page in range(1, 4)]
        )
        self.assertEqual([1, 2, 3], [item["id"] for item in values])
        self.assertEqual(3, len(calls))

    def test_duplicate_page_link_refuses_complete_census(self):
        endpoint = "https://api.github.com/repos/FS-GG/.github/issues/1/comments"
        link = f'<{endpoint}?per_page=100&page=1>; rel="next"'
        with self.assertRaisesRegex(MODULE.Refusal, "repeated or skipped"):
            self.paged([(200, [{"id": 1}], {"Link": link})])


if __name__ == "__main__":
    unittest.main()
