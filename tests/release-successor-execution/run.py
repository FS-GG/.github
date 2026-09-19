#!/usr/bin/env python3
"""Fake-provider recovery and denial controls for UTEL-REL-01."""

from __future__ import annotations

import importlib.util
import hashlib
import json
import pathlib
import sys
import unittest

MODULE = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "release_successor_execution.py"
spec = importlib.util.spec_from_file_location("release_successor_execution", MODULE)
assert spec and spec.loader
engine = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = engine
spec.loader.exec_module(engine)


def manifest() -> dict:
    descriptor = {
            "policyVersion": "release-successor/1",
            "sourceSha": "b" * 40,
            "standaloneTelemetry": {"qualificationSha256": "c" * 64},
            "packages": [
                {
                    "id": package,
                    "artifact": {"sha256": str(index) * 64, "payloadSha256": "sha256:" + str(index) * 64},
                }
                for index, package in enumerate(engine.PACKAGES, 1)
            ],
    }
    content_id = "sha256:" + hashlib.sha256(
        json.dumps(descriptor, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()
    ).hexdigest()
    return {"contentId": content_id, "descriptor": descriptor}


class FakeJournal:
    def __init__(self, content_id: str):
        self.state = engine.JournalState(1, content_id, {})
        self.writes = []
        self.conflict = False

    def read(self):
        return self.state

    def compare_and_swap(self, expected, effect, state):
        if self.conflict or expected != self.state:
            return False
        self.state = engine.JournalState(
            expected.generation + 1, expected.content_id, {**expected.effects, effect: state}
        )
        self.writes.append((effect, state))
        return True


class FakeAdmission:
    def __init__(self):
        self.calls = []
        self.denied = set()

    def authorize(self, content_id, effect, action, request_digest):
        self.calls.append((content_id, effect, action, request_digest))
        return (effect, action) not in self.denied


class FakeProvider:
    def __init__(self):
        self.visible = {}
        self.calls = []
        self.delay = set()
        self.indeterminate = set()

    def observe(self, effect):
        observed = self.visible.get(effect.identity)
        if observed is None:
            return engine.Observation("absent")
        if observed == "unknown":
            return engine.Observation("unknown")
        return engine.Observation("matched" if observed == effect.target_digest else "mismatched", observed)

    def dispatch(self, effect):
        self.calls.append(effect.identity)
        if effect.identity not in self.delay:
            self.visible[effect.identity] = effect.target_digest
        return engine.Dispatch("unknown" if effect.identity in self.indeterminate else "applied")


class ExecutionTests(unittest.TestCase):
    def setUp(self):
        self.manifest = manifest()
        self.journal = FakeJournal(self.manifest["contentId"])
        self.admission = FakeAdmission()
        self.provider = FakeProvider()

    def advance(self):
        return engine.advance(self.manifest, self.journal, self.admission, self.provider)

    def test_every_effect_is_admitted_then_read_back_before_completion(self):
        effects = engine.ordered_effects(self.manifest)
        for effect in effects:
            self.assertEqual(self.advance(), "waiting")
            self.assertEqual(self.provider.calls[-1], effect.identity)
            self.assertEqual(self.advance(), "verified")
        self.assertEqual(self.advance(), "complete")
        self.assertEqual(len(self.provider.calls), 9)
        self.assertEqual(len(self.journal.writes), 18)
        self.assertEqual([effect.identity for effect in effects], self.provider.calls)
        self.assertEqual(len(self.admission.calls), 27)

    def test_denied_intent_or_dispatch_has_zero_provider_mutations(self):
        for action, expected_writes in (("intent", 0), ("dispatch", 1)):
            with self.subTest(action=action):
                self.setUp()
                self.admission.denied.add(("tag", action))
                with self.assertRaisesRegex(engine.Refused, "admission denied"):
                    self.advance()
                self.assertEqual(self.provider.calls, [])
                self.assertEqual(len(self.journal.writes), expected_writes)

    def test_old_candidate_and_journal_conflict_refuse_before_provider(self):
        self.journal.state = engine.JournalState(1, "sha256:" + "f" * 64, {})
        with self.assertRaisesRegex(engine.Refused, "does not bind"):
            self.advance()
        self.assertEqual(self.provider.calls, [])

    def test_changed_descriptor_or_archive_digest_refuses_before_provider(self):
        self.manifest["descriptor"]["sourceSha"] = "c" * 40
        with self.assertRaisesRegex(engine.Refused, "qualified three-package"):
            self.advance()
        self.assertEqual(self.provider.calls, [])

    def test_out_of_order_or_duplicate_inflight_journal_refuses(self):
        self.journal.state = engine.JournalState(2, self.manifest["contentId"], {"promote": "verified"})
        with self.assertRaisesRegex(engine.Refused, "out-of-order"):
            self.advance()
        self.journal.state = engine.JournalState(
            3, self.manifest["contentId"], {"tag": "intent", "draft": "intent"}
        )
        with self.assertRaisesRegex(engine.Refused, "multiple in-flight"):
            self.advance()
        self.assertEqual(self.provider.calls, [])
        self.setUp()
        self.journal.conflict = True
        with self.assertRaisesRegex(engine.Refused, "CAS conflict"):
            self.advance()
        self.assertEqual(self.provider.calls, [])

    def test_delayed_indexing_and_unknown_response_never_repush_blindly(self):
        self.provider.delay.add("tag")
        self.provider.indeterminate.add("tag")
        self.assertEqual(self.advance(), "waiting")
        self.assertEqual(self.advance(), "waiting")
        self.assertEqual(self.provider.calls, ["tag"])
        self.provider.visible["tag"] = "b" * 40
        self.assertEqual(self.advance(), "verified")
        self.assertEqual(self.provider.calls, ["tag"])

    def test_partial_one_feed_cannot_promote(self):
        for _ in range(10):  # tag, draft, and the three GitHub Packages effects
            self.advance()
        self.assertEqual(self.journal.state.effects["github:FS.GG.Drivers"], "verified")
        self.provider.delay.add("nuget:FS.GG.Coord.Cli")
        self.assertEqual(self.advance(), "waiting")
        for _ in range(3):
            self.assertEqual(self.advance(), "waiting")
        self.assertNotIn("promote", self.provider.calls)
        self.assertEqual(self.provider.calls.count("nuget:FS.GG.Coord.Cli"), 1)

    def test_mismatch_or_unknown_readback_cannot_settle(self):
        self.assertEqual(self.advance(), "waiting")
        self.provider.visible["tag"] = "wrong"
        with self.assertRaisesRegex(engine.Refused, "mismatched"):
            self.advance()
        self.assertEqual(self.journal.state.effects["tag"], "intent")
        self.provider.visible["tag"] = "unknown"
        with self.assertRaisesRegex(engine.Refused, "unknown"):
            self.advance()

    def test_revocation_between_intent_and_dispatch_stops_mutation(self):
        self.admission.denied.add(("tag", "dispatch"))
        with self.assertRaisesRegex(engine.Refused, "dispatch admission denied"):
            self.advance()
        self.assertEqual(self.provider.calls, [])

    def test_interruption_before_or_after_provider_write_preserves_uncertain_intent(self):
        for applied_before_interrupt in (False, True):
            with self.subTest(applied_before_interrupt=applied_before_interrupt):
                self.setUp()

                def interrupted(effect):
                    if applied_before_interrupt:
                        self.provider.visible[effect.identity] = effect.target_digest
                    raise RuntimeError("interrupted")

                original = self.provider.dispatch
                self.provider.dispatch = interrupted
                with self.assertRaisesRegex(RuntimeError, "interrupted"):
                    self.advance()
                self.provider.dispatch = original
                self.assertEqual(self.journal.state.effects["tag"], "intent")
                self.assertEqual(self.advance(), "verified" if applied_before_interrupt else "waiting")
                self.assertEqual(self.provider.calls, [])

    def test_promotion_retry_reads_back_without_duplicate_dispatch(self):
        for _ in range(16):  # eight predecessors, each with dispatch and settlement
            self.advance()
        self.provider.indeterminate.add("promote")
        self.assertEqual(self.advance(), "waiting")
        self.assertEqual(self.advance(), "verified")
        self.assertEqual(self.advance(), "complete")
        self.assertEqual(self.provider.calls.count("promote"), 1)


if __name__ == "__main__":
    unittest.main()
