"""Draft creation at the real helper boundary, with only gh replaced by its fixture."""
import json
import unittest

import test_pr_lane_admission as existing


class DraftCreationTests(unittest.TestCase):
    invoke = existing.PullRequestLaneAdmissionTests.invoke

    @classmethod
    def setUpClass(cls):
        existing.PullRequestLaneAdmissionTests.setUpClass()

    def draft_state(self):
        body = (
            "Draft source\n\n"
            "<!-- fsgg:routine-development/v1 head=" + existing.EXPECTED_SHA + " operation=source-change -->\n"
            "<!-- fsgg:delivery-continues/v1 -->\n"
        )
        state = existing.PullRequestLaneAdmissionTests().update_state()
        state["pull"].update(draft=True, body=body + "\n" + existing.managed_body("campaign-7", "chain-3").split("\n\n")[1])
        state["pull"]["head"]["sha"] = existing.EXPECTED_SHA
        state["open_pages"] = [[]]
        state["created"] = {"number": 42, "html_url": state["pull"]["html_url"], "draft": True}
        return state, body

    def test_draft_is_created_once_with_native_flag_and_exact_metadata_readback(self):
        state, body = self.draft_state()
        completed, result, calls = self.invoke(state, body, "routine/fixture", extra=("--draft",))
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual("created", result["action"])
        self.assertIs(True, result["draft"])
        self.assertEqual(["GET", "GET", "POST", "GET"], [c["arguments"][2] for c in calls])
        payload = json.loads(calls[2]["stdin"])
        self.assertIs(True, payload["draft"])
        self.assertEqual(state["pull"]["body"], payload["body"])
        self.assertEqual("routine/fixture", payload["head"])
        self.assertEqual("main", payload["base"])
        self.assertTrue(calls[-1]["arguments"][-1].endswith("/pulls/42"))

    def test_default_creation_preserves_payload_and_requires_no_draft_readback(self):
        completed, result, calls = self.invoke({"head_sha": existing.EXPECTED_SHA, "open_pages": [[]]})
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertNotIn("draft", result)
        self.assertEqual(["GET", "GET", "POST"], [c["arguments"][2] for c in calls])
        self.assertNotIn("draft", json.loads(calls[-1]["stdin"]))

    def test_draft_inspection_and_preconditions_never_write(self):
        state, body = self.draft_state()
        completed, result, calls = self.invoke(state, body, "routine/fixture", extra=("--draft", "--inspect"))
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual("admitted", result["action"])
        self.assertTrue(all(c["arguments"][2] == "GET" for c in calls))
        state["head_sha"] = "b" * 40
        completed, result, calls = self.invoke(state, body, "routine/fixture", extra=("--draft",))
        self.assertEqual("head-moved", result["reason"])
        self.assertTrue(all(c["arguments"][2] == "GET" for c in calls))
        state, body = self.draft_state()
        state["open_pages"] = [[{"number": 9, "head": {"ref": "feature/owned"},
            "body": existing.managed_body("campaign-7", "chain-3")}]]
        completed, result, calls = self.invoke(state, body, "routine/fixture", extra=("--draft",))
        self.assertEqual("same-chain-open", result["reason"])
        self.assertTrue(all(c["arguments"][2] == "GET" for c in calls))

    def test_draft_is_rejected_for_metadata_update_and_known_roster_before_reads(self):
        state, body = self.draft_state()
        for extra in [("--update-pr", "42", "--previous-head-sha", "b" * 40, "--draft"),
                      ("--adopt-known-roster", "/not-read.json", "--draft")]:
            with self.subTest(extra=extra):
                completed, result, calls = self.invoke(state, body, "routine/fixture", extra=extra)
                self.assertEqual(2, completed.returncode, completed.stderr)
                self.assertIn("draft", result["reason"])
                self.assertEqual([], calls)

    def test_uncertain_draft_create_or_readback_never_retries_or_reports_success(self):
        cases = [
            ("create_fail_after", True), ("fail_read_after_create", True),
            ("created", {"number": True, "draft": True}),
            ("created", {"number": 42, "draft": False}),
            ("created", {"number": 42}),
        ]
        for key, value in cases:
            with self.subTest(key=key, value=value):
                state, body = self.draft_state()
                state[key] = value
                completed, result, calls = self.invoke(state, body, "routine/fixture", extra=("--draft",))
                self.assertEqual(3, completed.returncode, completed.stderr)
                self.assertEqual("draft-create-unresolved", result["reason"])
                self.assertIs(True, result["writeAttempted"])
                self.assertEqual(1, sum(c["arguments"][2] == "POST" for c in calls))
                self.assertFalse(any(c["arguments"][2] == "PATCH" for c in calls))

    def test_native_draft_readback_conflict_cannot_succeed_or_trigger_conversion(self):
        changes = [
            ("draft", False), ("draft", 1), ("draft", None), ("state", "closed"),
            ("number", 43), ("body", "changed metadata"), ("title", "changed title"),
            ("head.sha", "b" * 40), ("head.ref", "routine/other"),
            ("head.repo.full_name", "FS-GG/other"), ("base.ref", "other"),
            ("base.repo.full_name", "FS-GG/other"),
        ]
        for path, value in changes:
            with self.subTest(path=path, value=value):
                state, body = self.draft_state()
                target = state["pull"]
                parts = path.split(".")
                for part in parts[:-1]:
                    target = target[part]
                target[parts[-1]] = value
                completed, result, calls = self.invoke(state, body, "routine/fixture", extra=("--draft",))
                self.assertEqual(3, completed.returncode, completed.stderr)
                self.assertEqual("draft-create-unresolved", result["reason"])
                self.assertIs(True, result["writeAttempted"])
                self.assertEqual(1, sum(c["arguments"][2] == "POST" for c in calls))
                self.assertFalse(any(c["arguments"][2] == "PATCH" for c in calls))

    def test_raw_draft_responses_reject_duplicate_keys_and_malformed_json_without_resend(self):
        for stage, key in [("create", "raw_create_response"), ("readback", "raw_create_readback")]:
            state, body = self.draft_state()
            native = json.dumps(state["created"] if stage == "create" else state["pull"])
            raw_cases = [
                native.replace('"draft": true', '"draft": false, "draft": true'),
                native.replace('"number": 42', '"number": 41, "number": 42'),
                native[:-1] + ', "nested": {"identity": "wrong", "identity": "accepted"}}',
                native[:-1] + ', "invalid": NaN}',
                native[:-1] + ', "invalid": Infinity}',
                native[:-1] + ', "invalid": -Infinity}',
                native[:-1],
                native + '{}',
            ]
            if stage == "readback":
                raw_cases += [native.replace('"sha": "' + existing.EXPECTED_SHA + '"',
                    '"sha": "' + 'b' * 40 + '", "sha": "' + existing.EXPECTED_SHA + '"', 1)]
            for raw in raw_cases:
                with self.subTest(stage=stage, raw=raw):
                    candidate = dict(state)
                    candidate[key] = raw
                    completed, result, calls = self.invoke(candidate, body, "routine/fixture", extra=("--draft",))
                    self.assertEqual(3, completed.returncode, completed.stderr)
                    self.assertEqual("draft-create-unresolved", result["reason"])
                    self.assertIs(True, result["writeAttempted"])
                    self.assertEqual("create" if stage == "create" else "readback", result["stage"])
                    self.assertEqual(["GET", "GET", "POST"] + (["GET"] if stage == "readback" else []),
                                     [c["arguments"][2] for c in calls])
                    self.assertFalse(any(c["arguments"][2] == "PATCH" for c in calls))
