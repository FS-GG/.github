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


def spawn(receiver=CHILD, tool="spawnAgent", model="gpt-5.6-sol", effort="medium"):
    return {"type": "collabAgentToolCall", "tool": tool, "status": "completed", "senderThreadId": PARENT,
            "receiverThreadIds": [receiver], "model": model, "reasoningEffort": effort, "agentsStates": {receiver: {"status": "running"}}, "id": "call-1"}


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
        environment = support.clean_environment(profile, {"PATH": "/bin", "GITHUB_TOKEN": "secret", "HTTP_PROXY": "bad", "ALL_PROXY": "bad", "OPENAI_API_KEY": "secret"})
        self.assertEqual({"PATH", "HOME", "CODEX_HOME", "HTTPS_PROXY", "NO_PROXY", "LANG", "LC_ALL"}, set(environment))

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
        self.assertEqual("/root/native_ack", selector)
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
        value.terminal(PARENT, {"id": "turn-parent", "status": "completed"})
        value.verify_turn_history("child", {"id": CHILD, "turns": [{"id": "turn-child", "status": "completed"}]})
        value.verify_turn_history("root", {"id": PARENT, "turns": [{"id": "turn-parent", "status": "completed"}]})
        value.finish_result("root", receipt(status="terminal", outcome="completed"))
        result = value.result(); encoded = json.dumps(result)
        self.assertEqual((1, 1, 1, 0, 0), (result["spawnCount"], result["childTerminalTurns"], result["waitCount"], result["followups"], result["automaticRetries"]))
        for private in ("NATIVE-CHILD-ACK", "NATIVE-PARENT-ACK", "prompt", "auth"):
            self.assertNotIn(private, encoded)

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


if __name__ == "__main__":
    unittest.main()
