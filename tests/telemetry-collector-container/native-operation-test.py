#!/usr/bin/env python3
import importlib.util
import json
import os
import pathlib
import tempfile
import time
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
SUPPORT_PATH = ROOT / "deployment/telemetry-collector/native_producer_support.py"
DRIVER_PATH = ROOT / "deployment/telemetry-collector/qualify_native.py"
PROFILE_PATH = ROOT / "deployment/telemetry-collector/native-operation-v1.json"

spec = importlib.util.spec_from_file_location("native_producer_support", SUPPORT_PATH)
import sys
support = importlib.util.module_from_spec(spec); sys.modules["native_producer_support"] = support; spec.loader.exec_module(support)
driver_spec = importlib.util.spec_from_file_location("qualify_native", DRIVER_PATH)
driver = importlib.util.module_from_spec(driver_spec); driver_spec.loader.exec_module(driver)

PARENT = "11111111-1111-7111-8111-111111111111"
CHILD = "22222222-2222-7222-8222-222222222222"


def receipt(status="expected", token="a" * 32, outcome=None):
    value = {"schema": "fsgg.telemetry.roadmap-dispatch/1", "status": status, "token": token}
    if outcome is not None: value["outcome"] = outcome
    return value


def parent_response():
    return {"thread": {"id": PARENT, "ephemeral": False}}


def spawn(receiver=CHILD, tool="spawnAgent", model="gpt-5.6-sol", effort="medium", prompt=support.FIXED_CHILD_PROMPT):
    return {"type": "collabAgentToolCall", "tool": tool, "status": "completed", "senderThreadId": PARENT,
            "receiverThreadIds": [receiver], "model": model, "reasoningEffort": effort,
            "prompt": prompt,
            "agentsStates": {receiver: {"status": "completed" if tool == "wait" else "running"}},
            "id": "call-wait" if tool == "wait" else "call-spawn"}


def child_thread(selector="/root/native_ack"):
    return {"id": CHILD, "parentThreadId": PARENT,
            "source": {"subAgent": {"thread_spawn": {"parent_thread_id": PARENT, "agent_path": selector, "depth": 1}}}}


class NativeOperationTests(unittest.TestCase):
    def ready(self):
        value = support.OperationEvidence("v2-host-01.8a-native-collaboration-v1", "owner-nonce-01")
        value.thread_started(parent_response())
        root = value.begin("root", receipt(token="a" * 32))
        value.begin("child", receipt(token="b" * 32))
        return value, root

    def test_exact_profile_and_config_are_closed(self):
        profile = support.load_profile(PROFILE_PATH)
        self.assertEqual(("openai", "gpt-5.6-sol", "medium"), (profile["provider"], profile["model"], profile["effort"]))
        args = driver.config_arguments(ROOT / "deployment/telemetry-collector/native-producer-config.toml", profile["native"]["configSha256"])
        joined = " ".join(args)
        self.assertIn("features.multi_agent=true", joined)
        for disabled in ("shell_tool", "unified_exec", "apps", "image_generation", "view_image"):
            self.assertIn(f"features.{disabled}=false", joined)

    def test_profile_mutation_and_unknown_config_refuse(self):
        profile = json.loads(PROFILE_PATH.read_text())
        profile["model"] = "caller-model"
        with tempfile.TemporaryDirectory() as temporary:
            path = pathlib.Path(temporary) / "profile.json"; path.write_text(json.dumps(profile))
            with self.assertRaisesRegex(support.Refusal, "fixed model"):
                support.load_profile(path)
            config = pathlib.Path(temporary) / "config.toml"; config.write_text("unknown = true\n")
            with self.assertRaisesRegex(support.Refusal, "native config keys"):
                driver.config_arguments(config, support.sha256(config))

    def test_clean_environment_drops_credentials_and_bypass_proxies(self):
        profile = support.load_profile(PROFILE_PATH)
        environment = support.clean_environment(profile, {"PATH": "/bin", "GITHUB_TOKEN": "secret", "HTTP_PROXY": "bad", "ALL_PROXY": "bad", "OPENAI_API_KEY": "secret",
                                                          "FSGG_TELEMETRY_CREDENTIAL_NATIVE_PROSPECTIVE_V1": "receiver-secret"})
        self.assertEqual({"PATH", "HOME", "CODEX_HOME", "HTTPS_PROXY", "NO_PROXY", "LANG", "LC_ALL"}, set(environment))
        self.assertEqual("/usr/local/bin:/usr/bin:/bin", environment["PATH"])
        self.assertEqual("localhost,127.0.0.1,[::1],native-receiver", environment["NO_PROXY"])

    def test_producer_environment_passes_only_fixed_credential(self):
        profile = support.load_profile(PROFILE_PATH)
        name = "FSGG_TELEMETRY_CREDENTIAL_NATIVE_PROSPECTIVE_V1"
        parent = {"PATH": "/bin", name: "receiver-secret", "OPENAI_API_KEY": "model-secret"}
        environment = support.producer_environment(profile, PARENT, parent)
        self.assertEqual("receiver-secret", environment[name])
        self.assertEqual(PARENT, environment["CODEX_THREAD_ID"])
        self.assertEqual("/opt/fsgg/coord:/usr/local/bin:/usr/bin:/bin", environment["PATH"])
        self.assertEqual("FS-GG/.github", environment["FSGG_TELEMETRY_REPOSITORY"])
        self.assertNotIn("OPENAI_API_KEY", environment)
        with self.assertRaisesRegex(support.Refusal, "unavailable"):
            support.producer_environment(profile, PARENT, {"PATH": "/bin"})
        parent["FSGG_TELEMETRY_CREDENTIAL_OTHER"] = "wrong-scope"
        with self.assertRaisesRegex(support.Refusal, "unreviewed telemetry credential"):
            support.producer_environment(profile, PARENT, parent)

    def test_workspace_config_is_exact_and_contains_no_secret(self):
        profile = support.load_profile(PROFILE_PATH)
        producer = profile["producer"]
        value = {"schema": "fsgg.telemetry.workspace-config/1", "engine": "fsgg-coord-engine", "associations": [{
            "workspaceId": producer["workspaceId"], "producerId": producer["producerId"], "streamId": producer["streamId"],
            "repositories": [producer["repository"]], "destination": {"kind": "remote", "endpoint": producer["receiverOrigin"],
            "credentialReference": producer["credentialReference"], "spoolRoot": producer["spoolRoot"]}}], "retiredAssociations": []}
        driver.workspace_config_value(value, profile)
        encoded = json.dumps(value)
        self.assertNotIn("receiver-secret", encoded)
        changed = json.loads(encoded); changed["associations"][0]["destination"]["endpoint"] = "https://native-receiver:7443"
        with self.assertRaisesRegex(support.Refusal, "fixed receiver binding"):
            driver.workspace_config_value(changed, profile)

    def test_begin_must_be_applied_and_child_precedes_spawn(self):
        value = support.OperationEvidence("op", "nonce")
        with self.assertRaisesRegex(support.Refusal, "not applied"):
            value.begin("root", receipt(status="unknown"))
        value.thread_started(parent_response()); value.begin("root", receipt())
        with self.assertRaisesRegex(support.Refusal, "prospective child begin"):
            value.spawn(spawn())

    def test_actual_spawn_binds_distinct_uuid_and_native_selector(self):
        value, _ = self.ready()
        self.assertEqual(CHILD, value.spawn(spawn()))
        selector = value.bind_child(child_thread())
        self.assertEqual("native_ack", selector)
        self.assertEqual("/root/native_ack", value.native_selector_path)
        self.assertNotEqual(CHILD, selector)

    def test_invented_or_mismatched_identity_refuses(self):
        value, _ = self.ready(); value.spawn(spawn())
        with self.assertRaisesRegex(support.Refusal, "child thread parent"):
            value.bind_child({**child_thread(), "parentThreadId": CHILD})
        with self.assertRaisesRegex(support.Refusal, "native agent selector"):
            value.bind_child(child_thread(selector=CHILD))

    def test_extra_child_followup_and_requested_profile_refuse(self):
        value, _ = self.ready(); value.spawn(spawn()); value.bind_child(child_thread())
        with self.assertRaisesRegex(support.Refusal, "extra child"):
            value.spawn(spawn("33333333-3333-7333-8333-333333333333"))
        other, _ = self.ready()
        with self.assertRaisesRegex(support.Refusal, "unexpected collaboration"):
            other.spawn(spawn(tool="followupTask"))
        wrong, _ = self.ready()
        with self.assertRaisesRegex(support.Refusal, "spawn model"):
            wrong.spawn(spawn(model="other"))
        wrong_prompt, _ = self.ready()
        with self.assertRaisesRegex(support.Refusal, "spawn model"):
            wrong_prompt.spawn(spawn(prompt="read the repository"))

    def test_terminal_receipt_requires_actual_terminal(self):
        value, _ = self.ready(); value.spawn(spawn()); value.bind_child(child_thread())
        with self.assertRaisesRegex(support.Refusal, "finish preceded"):
            value.finish_result("child", receipt(status="terminal", outcome="completed"))
        value.terminal(CHILD, {"id": "turn-child", "status": "completed"})
        value.finish_result("child", receipt(status="terminal", outcome="completed"))
        with self.assertRaisesRegex(support.Refusal, "extra terminal"):
            value.terminal(CHILD, {"id": "turn-child-2", "status": "completed"})

    def test_complete_result_has_one_child_and_no_captured_prompt(self):
        value, _ = self.ready(); value.spawn(spawn()); value.bind_child(child_thread())
        value.acknowledge(CHILD, "NATIVE-CHILD-ACK")
        value.terminal(CHILD, {"id": "turn-child", "status": "completed"})
        value.finish_result("child", receipt(status="terminal", outcome="completed"))
        value.collaboration(spawn(tool="wait"))
        value.acknowledge(PARENT, "NATIVE-PARENT-ACK")
        value.parent_turn_started("turn-parent")
        value.terminal(PARENT, {"id": "turn-parent", "status": "completed"})
        value.verify_turn_history("child", {"id": CHILD, "turns": [{"id": "turn-child", "status": "completed", "items": [
            {"type": "userMessage", "id": "cu"}, {"type": "reasoning", "id": "cr"},
            {"type": "agentMessage", "id": "ca", "text": "NATIVE-CHILD-ACK"}]}]})
        value.verify_turn_history("root", {"id": PARENT, "turns": [{"id": "turn-parent", "status": "completed", "items": [
            {"type": "userMessage", "id": "pu"}, spawn(), spawn(tool="wait"),
            {"type": "agentMessage", "id": "pa", "text": "NATIVE-PARENT-ACK"}]}]})
        value.finish_result("root", receipt(status="terminal", outcome="completed"))
        result = value.result(); encoded = json.dumps(result)
        self.assertEqual((1, 1, 1, 0, 0), (result["spawnCount"], result["childTerminalTurns"], result["waitCount"], result["followups"], result["automaticRetries"]))
        for private in ("NATIVE-CHILD-ACK", "NATIVE-PARENT-ACK", "prompt", "auth.json", "OPENAI_API_KEY"):
            self.assertNotIn(private, encoded)

    def test_completed_wait_and_authoritative_history_cover_missing_child_notifications(self):
        value, _ = self.ready(); value.spawn(spawn()); value.bind_child(child_thread()); value.collaboration(spawn(tool="wait"))
        value.verify_turn_history("child", {"id": CHILD, "turns": [{"id": "turn-child", "status": "completed", "items": [
            {"type": "userMessage", "id": "u"}, {"type": "agentMessage", "id": "a", "text": "NATIVE-CHILD-ACK"}]}]})
        self.assertTrue(value.child_terminal); self.assertTrue(value.child_ack)
        self.assertEqual("wait-history", value.child_terminal_evidence)
        self.assertEqual((0, 0), (value.child_ack_notifications, value.child_terminal_notifications))

    def test_parent_history_refuses_extra_followup(self):
        value, _ = self.ready(); value.spawn(spawn()); value.bind_child(child_thread())
        value.acknowledge(CHILD, "NATIVE-CHILD-ACK"); value.terminal(CHILD, {"id": "turn-child", "status": "completed"})
        value.collaboration(spawn(tool="wait")); value.acknowledge(PARENT, "NATIVE-PARENT-ACK")
        value.parent_turn_started("turn-parent")
        value.terminal(PARENT, {"id": "turn-parent", "status": "completed"})
        followup = spawn(tool="followupTask"); followup["id"] = "call-followup"
        with self.assertRaisesRegex(support.Refusal, "root collaboration history"):
            value.verify_turn_history("root", {"id": PARENT, "turns": [{"id": "turn-parent", "status": "completed", "items": [
                {"type": "userMessage", "id": "pu"}, spawn(), spawn(tool="wait"), followup,
                {"type": "agentMessage", "id": "pa", "text": "NATIVE-PARENT-ACK"}]}]})

    def test_missing_child_or_failed_turn_never_qualifies(self):
        value, _ = self.ready()
        with self.assertRaisesRegex(support.Refusal, "incomplete"):
            value.result()
        value.spawn(spawn()); value.bind_child(child_thread())
        with self.assertRaisesRegex(support.Refusal, "did not complete"):
            value.terminal(CHILD, {"id": "turn", "status": "failed"})

    def test_timeout_process_is_cleaned_up(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            sleeper = root / "sleep.py"
            sleeper.write_text("#!/usr/bin/env python3\nimport time\ntime.sleep(60)\n")
            sleeper.chmod(0o700)
            server = support.JsonLineAppServer([str(sleeper)], {"PATH": os.environ["PATH"]}, root, 1, 1024)
            with self.assertRaisesRegex(support.Refusal, "timeout"):
                server.read(time.monotonic() + 0.01)
            server.close()
            self.assertIsNotNone(server.process.poll())

    def test_protocol_reader_preserves_prefetched_lines(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary); writer = root / "writer.py"
            writer.write_text("#!/usr/bin/env python3\nimport os,time\nos.write(1,b'{\\\"one\\\":1}\\n{\\\"two\\\":2}\\n')\ntime.sleep(60)\n")
            writer.chmod(0o700)
            server = support.JsonLineAppServer([str(writer)], {"PATH": os.environ["PATH"]}, root, 1, 1024)
            try:
                self.assertEqual({"one": 1}, server.read(time.monotonic() + 1))
                self.assertEqual({"two": 2}, server.read(time.monotonic() + 0.05))
            finally: server.close()

    def test_protocol_reader_refuses_oversized_prefetched_second_line(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary); writer = root / "writer.py"
            writer.write_text("#!/usr/bin/env python3\nimport os,time\nos.write(1,b'{}\\n{\\\"x\\\":\\\"'+b'x'*1024+b'\\\"}\\n')\ntime.sleep(60)\n")
            writer.chmod(0o700)
            server = support.JsonLineAppServer([str(writer)], {"PATH": os.environ["PATH"]}, root, 1, 1024)
            try:
                self.assertEqual({}, server.read(time.monotonic() + 1))
                with self.assertRaisesRegex(support.Refusal, "protocol line refused"):
                    server.read(time.monotonic() + 1)
            finally: server.close()

    def test_protocol_reader_bounds_partial_lines_and_stderr(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            partial = root / "partial.py"; partial.write_text("#!/usr/bin/env python3\nimport os,time\nos.write(1,b'{')\ntime.sleep(60)\n"); partial.chmod(0o700)
            server = support.JsonLineAppServer([str(partial)], {"PATH": os.environ["PATH"]}, root, 1, 1024)
            try:
                with self.assertRaisesRegex(support.Refusal, "timeout"):
                    server.read(time.monotonic() + 0.03)
            finally: server.close()
            noisy = root / "noisy.py"; noisy.write_text("#!/usr/bin/env python3\nimport os,time\nos.write(2,b'x'*2048)\ntime.sleep(60)\n"); noisy.chmod(0o700)
            server = support.JsonLineAppServer([str(noisy)], {"PATH": os.environ["PATH"]}, root, 1, 1024)
            try:
                with self.assertRaisesRegex(support.Refusal, "stderr capacity"):
                    server.read(time.monotonic() + 1)
            finally: server.close()

    def test_native_size_pin_accepts_exact_size_and_refuses_bound(self):
        profile = support.load_profile(PROFILE_PATH)
        self.assertEqual(286594376, profile["native"]["bytes"])
        self.assertGreaterEqual(driver.NATIVE_ELF_MAXIMUM, profile["native"]["bytes"])
        with tempfile.TemporaryDirectory() as temporary:
            path = pathlib.Path(temporary) / "codex"; path.touch(); path.chmod(0o700)
            path.write_bytes(b"x"); os.truncate(path, profile["native"]["bytes"])
            support.regular(path, driver.NATIVE_ELF_MAXIMUM, True)
            with self.assertRaisesRegex(support.Refusal, "digest differs"):
                driver.pinned_native(path, profile)
            os.truncate(path, driver.NATIVE_ELF_MAXIMUM + 1)
            with self.assertRaisesRegex(support.Refusal, "file size refused"):
                driver.pinned_native(path, profile)

    def test_exact_applied_receipt_is_required(self):
        token, invocation, producer = "a" * 32, "b" * 32, "native-prospective-v1"
        batch = invocation + "-000003"
        state = {"schema": "fsgg.telemetry.roadmap-dispatch-state/1", "token": token,
                 "associationProducer": producer, "invocationId": invocation, "sequence": 3}
        outcome = {"schema": "fsgg.telemetry.workspace-outcome/1", "batchId": batch,
                   "digest": "c" * 64, "status": "applied", "code": None}
        self.assertEqual(batch, driver.applied_receipt_value(state, outcome, token, producer)["batchId"])
        for status in ("durably-received", "rejected", "expired"):
            changed = {**outcome, "status": status}
            with self.assertRaisesRegex(support.Refusal, "did not apply"):
                driver.applied_receipt_value(state, changed, token, producer)
        with self.assertRaisesRegex(support.Refusal, "not settled"):
            driver.applied_receipt_value({**state, "pendingPublication": {}}, outcome, token, producer)

    def test_prebind_child_events_are_bounded_and_identity_checked(self):
        buffer = []
        event = {"method": "turn/completed", "params": {"threadId": CHILD, "turn": {"id": "child-turn", "status": "completed"}}}
        remaining = driver.buffer_prebind_event(buffer, event, PARENT, 2048)
        self.assertEqual([event], buffer); self.assertLess(remaining, 2048)
        with self.assertRaisesRegex(support.Refusal, "identity differs"):
            driver.buffer_prebind_event([], {"method": "turn/completed", "params": {"threadId": PARENT}}, PARENT, 2048)

    def test_input_surface_has_no_prompt_model_url_or_executable(self):
        text = DRIVER_PATH.read_text()
        for forbidden in ('add_argument("--prompt"', 'add_argument("--model"', 'add_argument("--url"', 'add_argument("--executable"', 'add_argument("--shell"'):
            self.assertNotIn(forbidden, text)

    def test_readonly_result_is_zero_turn_and_drops_private_source_fields(self):
        rows = [{"id": PARENT, "source": "appServer", "ephemeral": False, "path": "/private/source.jsonl", "preview": "private prompt"},
                {"id": CHILD, "source": {"subAgent": {"thread_spawn": {"agent_path": "/root/private"}}}, "ephemeral": False}]
        result = driver.source_readback_result(rows, [], "owner-nonce-01")
        self.assertEqual((2, 0, 0), (result["threadCount"], result["threadStarts"], result["turnStarts"]))
        encoded = json.dumps(result)
        for private in (PARENT, CHILD, "/private", "private prompt", "agent_path"):
            self.assertNotIn(private, encoded)
        with self.assertRaisesRegex(support.Refusal, "lifecycle events"):
            driver.source_readback_result([], [{"method": "thread/started"}], "owner-nonce-01")
        compatible = driver.source_readback_result([], [{"method": "configWarning"}, {"method": "remoteControl/status/changed"}], "owner-nonce-01")
        self.assertEqual((0, 0), (compatible["threadStarts"], compatible["turnStarts"]))


if __name__ == "__main__":
    unittest.main()
