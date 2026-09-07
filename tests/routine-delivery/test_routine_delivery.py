#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import pathlib
import sys
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "routine_delivery", ROOT / "tools/routine-delivery.py"
)
assert SPEC and SPEC.loader
MODULE = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)

HEAD = "a" * 40
MERGE = "b" * 40


def opened(head: str = HEAD) -> dict:
    return {
        "head": {"sha": head}, "state": "open", "draft": False, "merged": False,
        "merged_at": None, "mergeable": True, "mergeable_state": "clean",
    }


def merged(head: str = HEAD) -> dict:
    return {
        "head": {"sha": head}, "state": "closed", "draft": False, "merged": True,
        "merged_at": "2026-09-07T00:00:00Z", "merge_commit_sha": MERGE,
    }


class FakeApi:
    def __init__(self, reads: list[dict], writes: list[object] | None = None):
        self.reads = iter(reads)
        self.writes = iter(writes or [])
        self.attempts = 0

    def get_pr(self, repo: str, pr: int) -> dict:
        return next(self.reads)

    def merge(self, repo: str, pr: int, head: str, method: str) -> dict:
        self.attempts += 1
        value = next(self.writes)
        if isinstance(value, BaseException):
            raise value
        return value


class RoutineDeliveryTests(unittest.TestCase):
    def call(self, api: FakeApi, *, apply: bool = True, publication: bool = False):
        return MODULE.summarize(
            api, repo="FS-GG/.github", pr_number=1, expected_head=HEAD,
            merge_method="squash", publication_required=publication, apply=apply,
        )

    def test_dry_run_is_ready_without_a_write(self):
        api = FakeApi([opened()])
        code, result = self.call(api, apply=False)
        self.assertEqual((code, result.outcome, api.attempts), (0, "ready", 0))

    def test_changed_head_refuses_before_a_write(self):
        api = FakeApi([opened("c" * 40)])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, api.attempts), (2, "refused", 0))
        self.assertIn("changed head", result.reason)

    def test_success_requires_native_merged_readback(self):
        api = FakeApi([opened(), merged()], [{"merged": True, "sha": MERGE}])
        code, result = self.call(api, publication=True)
        self.assertEqual((code, result.codeDelivery, result.publication), (0, "delivered", "pending"))
        self.assertEqual((result.mergeCommit, result.attempts), (MERGE, 1))

    def test_ambiguous_write_reads_back_before_retry(self):
        api = FakeApi([opened(), merged()], [MODULE.AmbiguousWrite("timeout")])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, api.attempts), (0, "delivered-after-readback", 1))

    def test_definitely_unmerged_readback_allows_one_retry(self):
        api = FakeApi(
            [opened(), opened(), merged()],
            [MODULE.AmbiguousWrite("timeout"), {"merged": True, "sha": MERGE}],
        )
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, api.attempts), (0, "delivered", 2))

    def test_two_ambiguous_writes_stop_indeterminate(self):
        api = FakeApi(
            [opened(), opened(), opened()],
            [MODULE.AmbiguousWrite("timeout"), MODULE.AmbiguousWrite("timeout")],
        )
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, result.codeDelivery, api.attempts),
                         (3, "indeterminate", "unknown", 2))

    def test_retry_requires_current_native_merge_eligibility(self):
        blocked = {**opened(), "mergeable_state": "blocked"}
        api = FakeApi([opened(), blocked], [MODULE.AmbiguousWrite("timeout")])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, api.attempts), (2, "refused", 1))
        self.assertIn("blocked", result.reason)


if __name__ == "__main__":
    unittest.main()
