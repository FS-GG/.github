from __future__ import annotations

import base64
import copy
import hashlib
import importlib.util
import json
import os
import pathlib
import sys
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("learn_native", ROOT / "tools" / "learn_01_native_source.py")
assert SPEC and SPEC.loader
native = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = native
SPEC.loader.exec_module(native)

ROOT_ID = "11111111-1111-4111-8111-111111111111"
CHILD = "22222222-2222-4222-8222-222222222222"
ARCHIVED = "33333333-3333-4333-8333-333333333333"
GRANDCHILD = "44444444-4444-4444-8444-444444444444"
TURN_ROOT_1 = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1"
TURN_ROOT_2 = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa2"
TURN_CHILD = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb1"
TURN_ARCHIVED = "cccccccc-cccc-4ccc-8ccc-ccccccccccc1"
TURN_GRANDCHILD = "dddddddd-dddd-4ddd-8ddd-ddddddddddd1"


def counters(inp: int, out: int, cached: int = 0, reasoning: int = 0):
    return {"input_tokens": inp, "cached_input_tokens": cached, "output_tokens": out,
            "reasoning_output_tokens": reasoning, "total_tokens": inp + out}


def usage_record(thread: str, turn: str, response: str, usage: dict, total: dict):
    return {"type": "token_usage_record", "payload": {
        "thread_id": thread, "turn_id": turn, "response_id": response,
        "usage": usage, "turn_token_usage": total,
    }}


class FakeTransport:
    def __init__(self, paths: dict[str, str], *, cycle=False, drift=False, foreign_parent=False):
        self.paths = paths
        self.cycle = cycle
        self.drift = drift
        self.foreign_parent = foreign_parent
        self.epoch = 0
        self.request_id = 0
        self.closed = False
        self.threads = {
            ROOT_ID: {"id": ROOT_ID, "parentThreadId": None, "source": {}, "path": paths[ROOT_ID],
                      "modelProvider": "openai", "model": "gpt-test", "reasoningEffort": "medium"},
            CHILD: self.child(CHILD, ROOT_ID, "/root/worker", False),
            ARCHIVED: self.child(ARCHIVED, ROOT_ID, "/root/archived", True),
            GRANDCHILD: self.child(GRANDCHILD, CHILD, "/root/worker/grandchild", False),
        }
        if foreign_parent:
            self.threads[CHILD]["parentThreadId"] = ARCHIVED
        self.turns = {
            ROOT_ID: [{"id": TURN_ROOT_1, "status": "completed"}, {"id": TURN_ROOT_2, "status": "failed"}],
            CHILD: [{"id": TURN_CHILD, "status": "completed"}],
            ARCHIVED: [{"id": TURN_ARCHIVED, "status": "interrupted"}],
            GRANDCHILD: [{"id": TURN_GRANDCHILD, "status": "completed"}],
        }

    def child(self, thread_id, parent, path, archived):
        return {"id": thread_id, "parentThreadId": parent, "archived": archived,
                "source": {"subAgent": {"thread_spawn": {"parent_thread_id": parent, "agent_path": path}}},
                "path": self.paths[thread_id], "modelProvider": "openai", "model": "gpt-test",
                "reasoningEffort": "medium"}

    def exchange(self, method, params, result):
        self.request_id += 1
        request = native._canonical({"id": self.request_id, "method": method, "params": params}) + b"\n"
        # Deliberately retain non-canonical whitespace as source bytes.
        response = json.dumps({"id": self.request_id, "result": result}, separators=(", ", ": ")).encode() + b"\n"
        return native._Exchange(request, response, result)

    def request(self, method, params):
        if method == "thread/read":
            thread_id = params["threadId"]
            if thread_id == ROOT_ID:
                self.epoch += 1
            return self.exchange(method, params, {"thread": copy.deepcopy(self.threads[thread_id])})
        if method == "thread/list":
            parent = params["parentThreadId"]
            archived = params["archived"]
            cursor = params["cursor"]
            if self.cycle and parent == ROOT_ID and not archived:
                return self.exchange(method, params, {"data": [], "nextCursor": "same"})
            rows = []
            if parent == ROOT_ID and not archived:
                rows = [{"id": CHILD}]
            elif parent == ROOT_ID and archived:
                rows = [{"id": ARCHIVED}]
            elif parent == CHILD and not archived:
                rows = [{"id": GRANDCHILD}]
            return self.exchange(method, params, {"data": rows, "nextCursor": None})
        if method == "thread/turns/list":
            thread_id = params["threadId"]
            cursor = params["cursor"]
            turns = copy.deepcopy(self.turns[thread_id])
            if self.drift and self.epoch >= 2 and thread_id == CHILD:
                turns.append({"id": "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee1", "status": "completed"})
            if thread_id == ROOT_ID:
                if cursor is None:
                    return self.exchange(method, params, {"data": turns[:1], "nextCursor": "root-page-2"})
                if cursor == "root-page-2":
                    return self.exchange(method, params, {"data": turns[1:], "nextCursor": None})
            return self.exchange(method, params, {"data": turns, "nextCursor": None})
        raise AssertionError((method, params))

    def close(self):
        self.closed = True


class NativeSourceTests(unittest.TestCase):
    def make_sources(self, base: pathlib.Path, mutate=None):
        sessions = base / "sessions" / "2026" / "09"
        sessions.mkdir(parents=True)
        mapping = {}
        by_thread = {
            ROOT_ID: [(TURN_ROOT_1, [
                usage_record(ROOT_ID, TURN_ROOT_1, "r1", counters(10, 2), counters(10, 2)),
                usage_record(ROOT_ID, TURN_ROOT_1, "r1", counters(12, 3), counters(12, 3)),
                usage_record(ROOT_ID, TURN_ROOT_1, "r2", counters(4, 1), counters(16, 4)),
            ]), (TURN_ROOT_2, [usage_record(ROOT_ID, TURN_ROOT_2, "r3", counters(2, 1), counters(2, 1))])],
            CHILD: [(TURN_CHILD, [usage_record(CHILD, TURN_CHILD, "c1", counters(5, 2), counters(5, 2))])],
            ARCHIVED: [(TURN_ARCHIVED, [usage_record(ARCHIVED, TURN_ARCHIVED, "a1", counters(3, 1), counters(3, 1))])],
            GRANDCHILD: [(TURN_GRANDCHILD, [usage_record(GRANDCHILD, TURN_GRANDCHILD, "g1", counters(7, 2), counters(7, 2))])],
        }
        for index, (thread, groups) in enumerate(by_thread.items()):
            path = sessions / f"rollout-{index}.jsonl"
            records = [{"type": "session_meta", "payload": {"redacted": True}}]
            for _, rows in groups:
                records.extend(rows)
            if mutate:
                records = mutate(thread, records)
            path.write_bytes(b"".join(json.dumps(row, separators=(",", ":")).encode() + b"\n" for row in records))
            mapping[thread] = str(path)
        return mapping

    def capture(self, base, **transport_args):
        paths = self.make_sources(base)
        transport = FakeTransport(paths, **transport_args)
        artifact = native.capture(ROOT_ID, base, _transport=transport)
        self.assertTrue(transport.closed)
        return artifact

    def test_root_child_grandchild_archived_and_multi_page_reconcile(self):
        with tempfile.TemporaryDirectory() as scratch:
            artifact = self.capture(pathlib.Path(scratch))
            self.assertEqual(artifact["outcome"], native.POSITIVE_OUTCOME)
            snapshot = native.snapshot_from_capture(artifact)
            result = native.verify(artifact, snapshot)
            self.assertEqual(result["status"], "verified")
            self.assertEqual(result["outcome"], native.POSITIVE_OUTCOME)
            threads = artifact["projection"]["threads"]
            self.assertEqual([row["threadId"] for row in threads], [ROOT_ID, CHILD, ARCHIVED, GRANDCHILD])
            self.assertEqual(threads[3]["parentChain"], [ROOT_ID, CHILD, GRANDCHILD])
            self.assertTrue(threads[2]["archived"])
            self.assertEqual(len(threads[0]["turns"]), 2)
            exchanges = artifact["initialExchanges"]
            requests = [json.loads(base64.b64decode(row["requestBytes"])) for row in exchanges]
            root_turn_requests = [row for row in requests if row["method"] == "thread/turns/list" and row["params"]["threadId"] == ROOT_ID]
            self.assertEqual([row["params"]["cursor"] for row in root_turn_requests], [None, "root-page-2"])

    def test_later_response_correction_replaces_instead_of_double_counting(self):
        with tempfile.TemporaryDirectory() as scratch:
            artifact = self.capture(pathlib.Path(scratch))
            usage = next(row["usage"] for row in artifact["projection"]["turnUsage"] if row["turnId"] == TURN_ROOT_1)
            self.assertEqual(usage, counters(16, 4))

    def test_verifier_reports_omitted_descendant_and_turn(self):
        with tempfile.TemporaryDirectory() as scratch:
            artifact = self.capture(pathlib.Path(scratch))
            snapshot = native.snapshot_from_capture(artifact)
            snapshot["threads"] = [row for row in snapshot["threads"] if row["threadId"] != GRANDCHILD]
            snapshot["threads"][0]["turnIds"].pop()
            snapshot["turnUsage"] = [row for row in snapshot["turnUsage"] if row["turnId"] not in {TURN_ROOT_2, TURN_GRANDCHILD}]
            result = native.verify(artifact, snapshot)
            self.assertEqual(result["status"], "incomplete")
            self.assertIn(GRANDCHILD, result["missingDescendants"])
            self.assertTrue(any(TURN_ROOT_2 in row for row in result["missingTurns"]))
            self.assertIsNone(result["outcome"])

    def test_cursor_cycle_refuses(self):
        with tempfile.TemporaryDirectory() as scratch:
            base = pathlib.Path(scratch); paths = self.make_sources(base)
            with self.assertRaisesRegex(native.NativeSourceError, "pagination is invalid"):
                native.capture(ROOT_ID, base, _transport=FakeTransport(paths, cycle=True))

    def test_second_capture_roster_drift_refuses(self):
        with tempfile.TemporaryDirectory() as scratch:
            base = pathlib.Path(scratch); paths = self.make_sources(base)
            with self.assertRaisesRegex(native.NativeSourceError, "changed during capture"):
                native.capture(ROOT_ID, base, _transport=FakeTransport(paths, drift=True))

    def test_retained_response_or_record_tamper_refuses(self):
        with tempfile.TemporaryDirectory() as scratch:
            artifact = self.capture(pathlib.Path(scratch))
            tampered = copy.deepcopy(artifact)
            raw = bytearray(base64.b64decode(tampered["initialExchanges"][0]["responseBytes"]))
            raw[-2] ^= 1
            tampered["initialExchanges"][0]["responseBytes"] = base64.b64encode(raw).decode()
            tampered["captureDigest"] = native._artifact_digest(tampered)
            with self.assertRaisesRegex(native.NativeSourceError, "digest disagrees"):
                native.snapshot_from_capture(tampered)

            tampered = copy.deepcopy(artifact)
            tampered["rollouts"][0]["records"][0]["recordDigest"] = "0" * 64
            tampered["captureDigest"] = native._artifact_digest(tampered)
            with self.assertRaisesRegex(native.NativeSourceError, "digest disagrees"):
                native.snapshot_from_capture(tampered)

    def test_final_symlink_and_symlink_ancestor_refuse(self):
        with tempfile.TemporaryDirectory() as scratch:
            base = pathlib.Path(scratch); paths = self.make_sources(base)
            source = pathlib.Path(paths[ROOT_ID]); outside = base / "outside"; outside.write_bytes(source.read_bytes())
            source.unlink(); source.symlink_to(outside)
            with self.assertRaisesRegex(native.NativeSourceError, "without following links"):
                native.capture(ROOT_ID, base, _transport=FakeTransport(paths))
        with tempfile.TemporaryDirectory() as scratch:
            base = pathlib.Path(scratch); paths = self.make_sources(base)
            real = base / "real"; (base / "sessions" / "2026").rename(real)
            (base / "sessions" / "2026").symlink_to(real, target_is_directory=True)
            with self.assertRaisesRegex(native.NativeSourceError, "without following links"):
                native.capture(ROOT_ID, base, _transport=FakeTransport(paths))

    def test_rollout_bound_refuses_before_read(self):
        with tempfile.TemporaryDirectory() as scratch:
            base = pathlib.Path(scratch); paths = self.make_sources(base)
            with open(paths[ROOT_ID], "r+b") as stream:
                stream.truncate(native.MAX_ROLLOUT_BYTES + 1)
            with self.assertRaisesRegex(native.NativeSourceError, "bounded regular file"):
                native.capture(ROOT_ID, base, _transport=FakeTransport(paths))

    def test_foreign_identity_and_malformed_counters_refuse(self):
        def foreign(thread, rows):
            if thread == CHILD:
                rows[1]["payload"]["thread_id"] = ROOT_ID
            return rows
        with tempfile.TemporaryDirectory() as scratch:
            base = pathlib.Path(scratch); paths = self.make_sources(base, foreign)
            with self.assertRaisesRegex(native.NativeSourceError, "foreign thread"):
                native.capture(ROOT_ID, base, _transport=FakeTransport(paths))

        def malformed(thread, rows):
            if thread == CHILD:
                rows[1]["payload"]["usage"]["cached_input_tokens"] = 999
            return rows
        with tempfile.TemporaryDirectory() as scratch:
            base = pathlib.Path(scratch); paths = self.make_sources(base, malformed)
            with self.assertRaisesRegex(native.NativeSourceError, "inconsistent"):
                native.capture(ROOT_ID, base, _transport=FakeTransport(paths))

    def test_foreign_parent_chain_refuses(self):
        with tempfile.TemporaryDirectory() as scratch:
            base = pathlib.Path(scratch); paths = self.make_sources(base)
            with self.assertRaisesRegex(native.NativeSourceError, "foreign parent"):
                native.capture(ROOT_ID, base, _transport=FakeTransport(paths, foreign_parent=True))

    def test_public_cli_has_no_support_roster_or_digest_inputs(self):
        help_text = native.parser().format_help()
        capture_help = native.parser()._subparsers._group_actions[0].choices["capture"].format_help()
        joined = help_text + capture_help
        self.assertNotIn("support", joined)
        self.assertNotIn("expected-roster", joined)
        self.assertNotIn("digest", joined)


if __name__ == "__main__":
    unittest.main()
