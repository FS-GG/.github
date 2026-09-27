#!/usr/bin/env python3
from __future__ import annotations

import json
import pathlib
import sys
import tempfile
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.dont_write_bytecode = True
sys.path.insert(0, str(ROOT / ".claude/skills/work-roadmap/scripts"))
import native_collaboration_usage as native


class NativeUsageTests(unittest.TestCase):
    def test_completed_child_deduplicates_responses_and_keeps_subsets_inside_total(self):
        parent = "11111111-1111-4111-8111-111111111111"
        child = "22222222-2222-4222-8222-222222222222"
        turn = "33333333-3333-4333-8333-333333333333"
        turn2 = "44444444-4444-4444-8444-444444444444"
        one = {"input_tokens": 30, "cached_input_tokens": 20, "output_tokens": 7,
               "reasoning_output_tokens": 2, "total_tokens": 37}
        two = {"input_tokens": 10, "cached_input_tokens": 4, "output_tokens": 3,
               "reasoning_output_tokens": 1, "total_tokens": 13}
        corrected = dict(one, input_tokens=31, total_tokens=38)
        total = {key: corrected[key] + two[key] for key in native.COUNTS}
        with tempfile.TemporaryDirectory() as scratch:
            home = pathlib.Path(scratch)
            path = home / "sessions" / "fixture.jsonl"
            path.parent.mkdir()
            def record(response, usage, cumulative, turn_id=turn):
                return {"type": "token_usage_record", "payload": {"thread_id": child,
                        "turn_id": turn_id, "response_id": response, "usage": usage,
                        "turn_token_usage": cumulative}}
            path.write_text("\n".join(json.dumps(row, separators=(",", ":")) for row in [
                {"type": "message", "payload": "private content must not be read"},
                record("a", one, one), record("a", corrected, corrected), record("b", two, total),
                record("c", two, two, turn2),
            ]) + "\n")
            class FakeServer:
                def __init__(self, *_): pass
                def __enter__(self): return self
                def __exit__(self, *_): pass
                def request(self, _id, method, params):
                    if method == "thread/list":
                        return {"data": [{"id": child}], "nextCursor": None}
                    if method == "thread/read":
                        return {"thread": {"id": child, "parentThreadId": parent,
                                "source": {"subAgent": {"thread_spawn": {"parent_thread_id": parent,
                                           "agent_path": "/root/worker"}}}, "path": str(path),
                                "modelProvider": "openai", "model": "gpt-6-astra",
                                "reasoningEffort": "high"}}
                    if method == "thread/turns/list":
                        return {"data": [{"id": turn, "status": "completed"},
                                         {"id": turn2, "status": "completed"}], "nextCursor": None}
                    raise AssertionError(method)
            with mock.patch.object(native, "AppServer", FakeServer):
                result = native.collect(parent, "worker", codex_home=home)
            self.assertTrue(result["complete"])
            self.assertEqual([row["usage"]["total_tokens"] for row in result["turns"]], [51, 13])
            self.assertEqual(result["turns"][0]["usage"]["cached_input_tokens"], 24)
            self.assertEqual(result["turns"][0]["usage"]["reasoning_output_tokens"], 3)
            self.assertEqual(result["allTurnIds"], [turn, turn2])
            self.assertTrue(all(row["terminal"] and row["usageAvailable"]
                                for row in result["turnInventory"]))
            self.assertEqual(result["inventoryHostSource"], "codex-app-server:thread/turns/list")
            self.assertEqual(result["inventoryPaging"], [{"page": 1, "requestCursor": None,
                                                          "nextCursor": None, "rowCount": 2}])
            self.assertRegex(result["inventoryCapturedAt"], r"Z$")
            self.assertRegex(result["inventoryRosterDigest"], r"^[0-9a-f]{64}$")
            self.assertRegex(result["inventorySourceDigest"], r"^[0-9a-f]{64}$")
            self.assertEqual(result["provider"], "openai")
            self.assertEqual(result["providerProvenance"], "codex-app-server-thread.modelProvider")
            self.assertGreaterEqual(len(result["appServerResponses"]), 3)
            self.assertEqual(len(result["rolloutRecords"]), 4)
            class NoProviderServer(FakeServer):
                def request(self, request_id, method, params):
                    result = super().request(request_id, method, params)
                    if method == "thread/read":
                        result["thread"].pop("modelProvider")
                    return result
            with mock.patch.object(native, "AppServer", NoProviderServer):
                no_provider = native.collect(parent, "worker", codex_home=home)
            self.assertFalse(no_provider["complete"])

            class MultiPageServer(FakeServer):
                def request(self, request_id, method, params):
                    if method == "thread/turns/list":
                        if params["cursor"] is None:
                            return {"data": [{"id": turn, "status": "completed"}], "nextCursor": "page-2"}
                        return {"data": [{"id": turn2, "status": "completed"}], "nextCursor": None}
                    return super().request(request_id, method, params)
            with mock.patch.object(native, "AppServer", MultiPageServer):
                multi_page = native.collect(parent, "worker", codex_home=home)
            self.assertFalse(multi_page["complete"])
            self.assertEqual(len(multi_page["inventoryPaging"]), 2)
            path.write_text(json.dumps(record("a", one, one)) + "\n")
            with mock.patch.object(native, "AppServer", FakeServer):
                partial = native.collect(parent, "worker", codex_home=home)
            self.assertFalse(partial["complete"])
            self.assertEqual(len(partial["turns"]), 1)
            self.assertEqual(partial["allTurnIds"], [turn, turn2])
            self.assertFalse(partial["turnInventory"][1]["usageAvailable"])

    def test_provider_requires_explicit_app_server_metadata_and_paging_cycles_refuse(self):
        class ProviderServer:
            def __init__(self, provider): self.provider = provider
            def __enter__(self): return self
            def __exit__(self, *_): pass
            def request(self, _id, method, _params):
                if method == "thread/list":
                    return {"data": [{"id": "22222222-2222-4222-8222-222222222222"}], "nextCursor": None}
                if method == "thread/read":
                    return {"thread": {"id": "22222222-2222-4222-8222-222222222222",
                            "parentThreadId": "11111111-1111-4111-8111-111111111111",
                            "source": {"subAgent": {"thread_spawn": {
                                "parent_thread_id": "11111111-1111-4111-8111-111111111111",
                                "agent_path": "/root/worker"}}}, "path": "/unused",
                            "modelProvider": self.provider}}
                if method == "thread/turns/list":
                    return {"data": [], "nextCursor": None}
                raise AssertionError(method)
        with mock.patch.object(native, "AppServer", lambda *_: ProviderServer("azure-openai")):
            result = native.collect("11111111-1111-4111-8111-111111111111", "worker")
        self.assertEqual(result["provider"], "azure-openai")
        self.assertEqual(result["providerProvenance"], "codex-app-server-thread.modelProvider")
        with mock.patch.object(native, "AppServer", lambda *_: ProviderServer({"name": "invented"})), \
             self.assertRaisesRegex(native.HostUnavailable, "provider metadata"):
            native.collect("11111111-1111-4111-8111-111111111111", "worker")

        class CyclingServer:
            def request(self, _id, _method, params):
                cursor = params["cursor"]
                return {"data": [], "nextCursor": "a" if cursor != "a" else "b" if cursor != "b" else "a"}
        with self.assertRaisesRegex(native.HostUnavailable, "pagination is invalid"):
            native.page(CyclingServer(), "thread/list", 1, {})

    def test_invalid_counts_and_outside_private_rollout_refuse(self):
        with self.assertRaises(native.HostUnavailable):
            native.counts({"input_tokens": 1, "cached_input_tokens": 2,
                           "output_tokens": 0, "reasoning_output_tokens": 0, "total_tokens": 1})
        with tempfile.TemporaryDirectory() as scratch:
            path = pathlib.Path(scratch) / "outside.jsonl"
            path.write_text("")
            with self.assertRaises(native.HostUnavailable):
                native.rollout_usage(str(path), "thread", set(), pathlib.Path(scratch))

    def test_rollout_open_is_descriptor_anchored_against_symlink_swap(self):
        with tempfile.TemporaryDirectory() as scratch:
            home = pathlib.Path(scratch)
            sessions = home / "sessions"
            sessions.mkdir()
            source = sessions / "fixture.jsonl"
            outside = home / "outside.jsonl"
            source.write_text("{}\n")
            outside.write_text("{}\n")
            real_open = native.os.open
            swapped = False

            def racing_open(path, flags, *args, **kwargs):
                nonlocal swapped
                if path == "fixture.jsonl" and not swapped:
                    swapped = True
                    source.unlink()
                    source.symlink_to(outside)
                return real_open(path, flags, *args, **kwargs)

            with mock.patch.object(native.os, "open", side_effect=racing_open), \
                 self.assertRaisesRegex(native.HostUnavailable, "outside the private host"):
                native.rollout_usage(str(source), "thread", set(), home)
            self.assertTrue(swapped)

            outside_dir = home / "outside-directory"
            outside_dir.mkdir()
            (outside_dir / "fixture.jsonl").write_text("{}\n")
            (sessions / "linked").symlink_to(outside_dir, target_is_directory=True)
            with self.assertRaisesRegex(native.HostUnavailable, "outside the private host"):
                native.rollout_usage(str(sessions / "linked" / "fixture.jsonl"), "thread", set(), home)


if __name__ == "__main__":
    unittest.main()
