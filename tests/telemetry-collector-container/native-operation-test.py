#!/usr/bin/env python3
import importlib.util
import json
import os
import pathlib
import subprocess
import tempfile
import time
import unittest
from unittest import mock


ROOT = pathlib.Path(__file__).resolve().parents[2]
SUPPORT_PATH = ROOT / "deployment/telemetry-collector/native_producer_support.py"
DRIVER_PATH = ROOT / "deployment/telemetry-collector/qualify_native.py"
PROFILE_PATH = ROOT / "deployment/telemetry-collector/native-operation-v1.json"
PROVENANCE_PATH = ROOT / "tests/telemetry-collector-container/native-rollout-v2-provenance.json"

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


def activity(kind="started", identifier="call-spawn"):
    return {"type": "subAgentActivity", "id": identifier, "kind": kind,
            "agentThreadId": CHILD, "agentPath": "/root/native_ack"}


def rollout_row(kind, payload, ordinal):
    return {"timestamp": "2026-10-01T00:00:00.000Z", "ordinal": ordinal, "type": kind, "payload": payload}


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
        self.assertIn("fork_turns=none", profile["prompt"]); self.assertIn("timeout_ms=30000", profile["prompt"])
        provenance = json.loads(PROVENANCE_PATH.read_text())
        self.assertEqual(("0.158.0", "rust-v0.158.0", "synthetic-source-derived-not-runtime-evidence"),
                         (provenance["native"]["version"], provenance["upstream"]["tag"], provenance["fixtureClassification"]))
        self.assertEqual({"session/mod.rs", "agent/control/api.rs", "agent/control/spawn.rs",
                          "agent/control/completion.rs", "session_prefix.rs",
                          "context/inter_agent_completion_message.rs", "items.rs"},
                         set(provenance["sourceBlobs"]) - {"protocol.rs", "history/rollout_payload.rs",
                         "rollout/policy.rs", "rollout/recorder.rs", "protocol/models.rs",
                         "multi_agents_v2/spawn.rs", "multi_agents_v2/wait.rs"})
        self.assertTrue(all(len(value) == 64 for value in provenance["sourceBlobs"].values()))

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

    def test_readonly_source_uses_only_its_writable_run_root_for_app_state(self):
        profile = support.load_profile(PROFILE_PATH); captured = {}
        class FakeServer:
            def __init__(self, command, environment, cwd, timeout, maximum_line):
                captured.update(environment); self.notifications = []
            def request(self, method, params, deadline):
                if method == "thread/list": return {"data": []}
                return {}
            def notify(self, method): pass
            def close(self): pass
        with tempfile.TemporaryDirectory() as temporary, \
             mock.patch.object(driver, "pinned_native"), \
             mock.patch.object(driver, "private_directory"), \
             mock.patch.object(driver, "config_arguments", return_value=[]), \
             mock.patch.object(driver, "effective_config"), \
             mock.patch.object(driver, "JsonLineAppServer", FakeServer):
            run_root = pathlib.Path(temporary).resolve()
            result = driver.readonly_source_compatibility(profile, "owner-nonce-01", run_root)
        self.assertEqual(str(run_root), captured.pop("CODEX_HOME"))
        expected = support.clean_environment(profile); expected.pop("CODEX_HOME")
        self.assertEqual(expected, captured)
        self.assertEqual(("compatible", 0), (result["status"], result["threadCount"]))

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

    def test_v2_activity_pairs_bind_one_child_and_empty_wait_is_not_terminal_proof(self):
        value, _ = self.ready(); started = activity()
        self.assertIsNone(value.activity("item/started", started)); self.assertEqual(CHILD, value.activity("item/completed", started))
        value.bind_child(child_thread())
        wait = spawn(tool="wait"); wait["receiverThreadIds"] = []; wait["agentsStates"] = {}
        value.collaboration(wait)
        self.assertFalse(value.child_terminal)
        completed = activity("completed", "subagent-completed-turn-child")
        value.activity("item/started", completed); value.activity("item/completed", completed)
        self.assertTrue(value.completion_activity)
        for changed in ({**activity(kind="interacted"), "id": "bad"}, {**activity(), "extra": True}):
            with self.assertRaises(support.Refusal): value.activity("item/started", changed)
        for malformed in ({**activity(), "agentThreadId": []}, {**activity(), "kind": {}}, {**activity(), "id": 3}):
            fresh,_=self.ready()
            with self.assertRaises(support.Refusal): fresh.activity("item/started", malformed)

    def test_strict_rollout_json_refuses_duplicate_keys_and_nonfinite_numbers(self):
        for encoded in (b'{"type":"response_item","type":"event_msg"}', b'{"value":NaN}'):
            with self.assertRaises(support.Refusal): support.strict_json(encoded, "fixture")

    def _write_rollouts(self, root, parent_call_mutator=None, child_extra=None, terminate=True):
        sessions = root / "sessions"; sessions.mkdir(mode=0o700); year=sessions/"2026";year.mkdir(mode=0o700);parent_dir=year/"10";parent_dir.mkdir(mode=0o700)
        common_context = {"turn_id": "turn-parent", "root_turn_id": None, "cwd": "/qualification/native/work",
                          "approval_policy": "never", "sandbox_policy": {"type": "read-only"},
                          "model": "gpt-5.6-sol", "multi_agent_version": "v2", "effort": "medium", "summary": "auto"}
        parent_meta = {"session_id": PARENT, "id": PARENT, "forked_from_id": None,
                       "forked_from_ordinal_exclusive": None, "parent_thread_id": None,
                       "timestamp": "2026-10-01T00:00:00Z", "cwd": "/qualification/native/work",
                       "originator": "app-server", "cli_version": "0.158.0", "source": "mcp", "model_provider": "openai",
                       "history_mode": "paginated", "history_base": None, "multi_agent_version": "v2"}
        def stamp(turn):
            return {"turn_id": turn, "create_time": 1790812800.25}
        def message(identifier, turn, role, kind, text, phase=None):
            value={"type":"message","id":identifier,"role":role,"content":[{"type":kind,"text":text}],
                   "internal_chat_message_metadata_passthrough":stamp(turn)}
            if phase is not None:value["phase"]=phase
            return value
        def agent_message(identifier, turn, author, recipient, text):
            return {"type":"agent_message","id":identifier,"author":author,"recipient":recipient,
                    "content":[{"type":"input_text","text":text}],
                    "internal_chat_message_metadata_passthrough":stamp(turn)}
        def reasoning(identifier, turn, sentinel):
            return {"type":"reasoning","id":identifier,"summary":[{"type":"summary_text","text":sentinel}],
                    "content":None,"encrypted_content":None,
                    "internal_chat_message_metadata_passthrough":stamp(turn)}
        def completed_item(thread,turn,item):
            return {"type":"item_completed","thread_id":thread,"turn_id":turn,
                    "item":item,
                    "completed_at_ms":1790812800250}
        def completed_message(thread,turn,identifier,text):
            return completed_item(thread,turn,{"type":"AgentMessage","id":identifier,
                                  "content":[{"type":"Text","text":text}],"phase":"final_answer"})
        def completed_reasoning(thread,turn,identifier,sentinel):
            return completed_item(thread,turn,{"type":"Reasoning","id":identifier,
                                  "summary_text":[sentinel],"raw_content":[]})
        spawn_call = {"type": "function_call", "id":"response-spawn", "name": "spawn_agent", "namespace": "functions", "call_id": "call-spawn",
                      "arguments": json.dumps({"message": support.FIXED_CHILD_PROMPT, "task_name": support.FIXED_TASK_NAME,
                                               "model": "gpt-5.6-sol", "reasoning_effort": "medium", "fork_turns": "none"}, separators=(",", ":")),
                      "internal_chat_message_metadata_passthrough":stamp("turn-parent")}
        wait_call = {"type": "function_call", "id":"response-wait", "name": "wait_agent", "namespace": None, "call_id": "call-wait",
                     "arguments": json.dumps({"timeout_ms": support.FIXED_WAIT_TIMEOUT_MS}, separators=(",", ":")),
                     "internal_chat_message_metadata_passthrough":stamp("turn-parent")}
        if parent_call_mutator: parent_call_mutator(spawn_call, wait_call)
        completion=("Message Type: FINAL_ANSWER\nTask name: /root\nSender: /root/native_ack\n"
                    "Payload:\nNATIVE-CHILD-ACK")
        parent_payloads = [("session_meta", parent_meta), ("turn_context", common_context),
                           ("event_msg", {"type": "turn_started", "turn_id": "turn-parent"}),
                           ("response_item", message("parent-user","turn-parent","user","input_text",support.FIXED_PROMPT)),
                           ("event_msg", completed_item(PARENT,"turn-parent",{"type":"UserMessage","id":"parent-user",
                                          "content":[{"type":"text","text":support.FIXED_PROMPT,"text_elements":[]}]})),
                           ("response_item", spawn_call),
                           ("event_msg", completed_item(PARENT,"turn-parent",{"type":"SubAgentActivity","id":"call-spawn",
                                          "kind":"started","agent_thread_id":CHILD,"agent_path":"/root/native_ack"})),
                           ("response_item", {"type": "function_call_output", "id":"response-spawn-output", "call_id": "call-spawn",
                                              "output": '{"task_name":"/root/native_ack"}',
                                              "internal_chat_message_metadata_passthrough":stamp("turn-parent")}),
                           ("response_item", wait_call),
                           ("event_msg", completed_item(PARENT,"turn-parent",{"type":"SubAgentActivity",
                                          "id":"subagent-completed-turn-child","kind":"completed",
                                          "agent_thread_id":CHILD,"agent_path":"/root/native_ack"})),
                           ("inter_agent_communication_metadata", {"trigger_turn":False}),
                           ("response_item", agent_message("parent-completion","turn-parent","/root/native_ack","/root",completion)),
                           ("event_msg", completed_item(PARENT,"turn-parent",{"type":"CollabAgentToolCall","id":"call-wait",
                                          "tool":"wait","status":"completed","sender_thread_id":PARENT,
                                          "receiver_thread_ids":[],"receiver_agents":[],"agents_states":{}})),
                           ("response_item", {"type": "function_call_output", "id":"response-wait-output", "call_id": "call-wait",
                                              "output": '{"message":"Wait completed.","timed_out":false}',
                                              "internal_chat_message_metadata_passthrough":stamp("turn-parent")}),
                           ("response_item", reasoning("parent-reasoning","turn-parent","PRIVATE-PARENT-SENTINEL")),
                           ("event_msg", completed_reasoning(PARENT,"turn-parent","parent-reasoning","PRIVATE-PARENT-SENTINEL")),
                           ("response_item", message("parent-ack","turn-parent","assistant","output_text","NATIVE-PARENT-ACK","final_answer")),
                           ("event_msg", completed_message(PARENT,"turn-parent","parent-ack","NATIVE-PARENT-ACK")),
                           ("event_msg", {"type": "turn_complete", "turn_id": "turn-parent", "last_agent_message": "NATIVE-PARENT-ACK", "error": None})]
        child_meta = {**parent_meta, "id": CHILD, "parent_thread_id": PARENT, "agent_path": "/root/native_ack",
                      "source": {"subagent": {"thread_spawn": {"parent_thread_id": PARENT, "depth": 1,
                                                                   "agent_path": "/root/native_ack"}}}}
        child_context = {**common_context, "turn_id": "turn-child", "root_turn_id": "turn-parent"}
        child_payloads = [("session_meta", child_meta), ("turn_context", child_context),
                          ("event_msg", {"type": "turn_started", "turn_id": "turn-child", "root_turn_id": "turn-parent"}),
                          ("inter_agent_communication_metadata", {"trigger_turn":True}),
                          ("response_item", agent_message("child-initial","turn-child","/root","/root/native_ack",support.FIXED_CHILD_PROMPT)),
                          ("response_item", reasoning("child-reasoning","turn-child","PRIVATE-CHILD-SENTINEL")),
                          ("event_msg", completed_reasoning(CHILD,"turn-child","child-reasoning","PRIVATE-CHILD-SENTINEL"))]
        if child_extra: child_payloads.append(("response_item", child_extra))
        child_payloads.extend([
            ("response_item", message("child-ack","turn-child","assistant","output_text","NATIVE-CHILD-ACK","final_answer")),
            ("event_msg", completed_message(CHILD,"turn-child","child-ack","NATIVE-CHILD-ACK")),
            ("event_msg", {"type": "turn_complete", "turn_id": "turn-child", "last_agent_message": "NATIVE-CHILD-ACK", "error": None})])
        paths=[]
        for name,payloads in (("parent.jsonl",parent_payloads),("child.jsonl",child_payloads)):
            path=parent_dir/name
            data=b"".join((json.dumps(rollout_row(kind,payload,index),separators=(",", ":"))+"\n").encode() for index,(kind,payload) in enumerate(payloads))
            if not terminate and name=="child.jsonl":data=data[:-1]
            path.write_bytes(data);path.chmod(0o600);paths.append(path)
        return sessions,*paths

    def _v2_audit_evidence(self):
        value,_=self.ready(); value.activity("item/started",activity());value.activity("item/completed",activity());value.bind_child(child_thread())
        wait=spawn(tool="wait");wait["receiverThreadIds"]=[];wait["agentsStates"]={};value.collaboration(wait)
        value.parent_turn_started("turn-parent");value.child_turn_id="turn-child"
        completed=activity("completed","subagent-completed-turn-child")
        value.activity("item/started",completed);value.activity("item/completed",completed);value.child_terminal=True
        return value

    def test_original_rollout_audit_joins_exact_calls_settings_and_private_sources(self):
        profile=support.load_profile(PROFILE_PATH)
        with tempfile.TemporaryDirectory() as temporary:
            root=pathlib.Path(temporary);sessions,parent,child=self._write_rollouts(root);value=self._v2_audit_evidence()
            result=support.audit_original_rollouts(str(parent),str(child),sessions,value,profile)
        self.assertTrue(value.original_audit);self.assertEqual((19,10),(result["parent"]["records"],result["child"]["records"]))
        encoded=json.dumps(result)
        for private in ("PRIVATE-PARENT-SENTINEL","PRIVATE-CHILD-SENTINEL",str(parent),support.FIXED_CHILD_PROMPT):self.assertNotIn(private,encoded)

    def _mutate_rollout(self, path, mutation):
        rows=[json.loads(line) for line in path.read_text().splitlines()]
        mutation(rows)
        for ordinal,row in enumerate(rows):row["ordinal"]=ordinal
        path.write_text("".join(json.dumps(row,separators=(",", ":"))+"\n" for row in rows));path.chmod(0o600)

    def test_native_inter_agent_communications_are_mandatory_typed_and_paired(self):
        profile=support.load_profile(PROFILE_PATH)
        scenarios=(
            lambda rows: rows.__delitem__(next(index for index,row in enumerate(rows) if row["type"]=="inter_agent_communication_metadata")),
            lambda rows: next(row for row in rows if row["type"]=="response_item" and row["payload"].get("type")=="agent_message")["payload"]["content"][0].update(type="output_text"),
            lambda rows: next(row for row in rows if row["type"]=="inter_agent_communication_metadata")["payload"].update(trigger_turn=False),
        )
        for mutation in scenarios:
            with self.subTest(case=scenarios.index(mutation)),tempfile.TemporaryDirectory() as temporary:
                root=pathlib.Path(temporary);sessions,parent,child=self._write_rollouts(root);self._mutate_rollout(child,mutation)
                with self.assertRaises(support.Refusal):
                    support.audit_original_rollouts(str(parent),str(child),sessions,self._v2_audit_evidence(),profile)

    def test_original_rollout_refuses_foreign_turn_order_extra_ack_and_wait_id_disagreement(self):
        profile=support.load_profile(PROFILE_PATH)
        def foreign_turn(rows):
            call=next(row["payload"] for row in rows if row["type"]=="response_item" and row["payload"].get("name")=="spawn_agent")
            call["internal_chat_message_metadata_passthrough"]["turn_id"]="foreign-turn"
        def terminal_first(rows):
            terminal=rows.pop(next(index for index,row in enumerate(rows) if row["type"]=="event_msg" and row["payload"].get("type")=="turn_complete"))
            rows.insert(1,terminal)
        def extra_ack(rows):
            ack=next(row for row in rows if row["type"]=="response_item" and row["payload"].get("role")=="assistant")
            extra=json.loads(json.dumps(ack));extra["payload"]["id"]="foreign-ack";extra["payload"]["content"][0]["text"]="NOT-THE-ACK"
            rows.insert(rows.index(ack),extra)
        for mutation in (foreign_turn,terminal_first,extra_ack):
            with self.subTest(case=mutation.__name__),tempfile.TemporaryDirectory() as temporary:
                root=pathlib.Path(temporary);sessions,parent,child=self._write_rollouts(root);self._mutate_rollout(parent,mutation)
                with self.assertRaises(support.Refusal):
                    support.audit_original_rollouts(str(parent),str(child),sessions,self._v2_audit_evidence(),profile)
        with tempfile.TemporaryDirectory() as temporary:
            root=pathlib.Path(temporary);sessions,parent,child=self._write_rollouts(root);evidence=self._v2_audit_evidence();evidence.wait_call_id="foreign-wait"
            with self.assertRaisesRegex(support.Refusal,"wait"):
                support.audit_original_rollouts(str(parent),str(child),sessions,evidence,profile)

    def test_original_completed_message_event_joins_identity_thread_turn_and_ack(self):
        profile=support.load_profile(PROFILE_PATH)
        def contradictory(rows):
            terminal=next(index for index,row in enumerate(rows) if row["type"]=="event_msg" and row["payload"].get("type")=="turn_complete")
            rows.insert(terminal,{"timestamp":"2026-10-01T00:00:00.000Z","type":"event_msg","payload":{
                "type":"item_completed","thread_id":CHILD,"turn_id":"turn-child",
                "item":{"type":"AgentMessage","id":"foreign-ack","content":[{"type":"Text","text":"NOT-THE-ACK"}],"phase":"final_answer"},
                "completed_at_ms":1}})
        def wrong_thread(rows):
            event=next(row["payload"] for row in rows if row["type"]=="event_msg" and row["payload"].get("type")=="item_completed")
            event["thread_id"]=PARENT
        def unknown_item(rows):
            event=next(row["payload"] for row in rows if row["type"]=="event_msg" and row["payload"].get("type")=="item_completed")
            event["item"]={"type":"CommandExecution","id":"child-ack","command":"private"}
        for mutation in (contradictory,wrong_thread,unknown_item):
            with self.subTest(case=mutation.__name__),tempfile.TemporaryDirectory() as temporary:
                root=pathlib.Path(temporary);sessions,parent,child=self._write_rollouts(root);self._mutate_rollout(child,mutation)
                with self.assertRaises(support.Refusal):
                    support.audit_original_rollouts(str(parent),str(child),sessions,self._v2_audit_evidence(),profile)

    def test_original_completed_subagent_activity_joins_live_identity_and_order(self):
        profile=support.load_profile(PROFILE_PATH)
        def completed_activity(rows):
            return next((index,row["payload"]) for index,row in enumerate(rows)
                         if row["type"]=="event_msg" and row["payload"].get("type")=="item_completed"
                         and row["payload"].get("item",{}).get("type")=="SubAgentActivity"
                         and row["payload"]["item"].get("kind")=="completed")
        def wrong_child(rows): completed_activity(rows)[1]["item"]["agent_thread_id"]=PARENT
        def wrong_path(rows): completed_activity(rows)[1]["item"]["agent_path"]="/root/foreign"
        def wrong_turn(rows): completed_activity(rows)[1]["item"]["id"]="subagent-completed-foreign-turn"
        def wrong_kind(rows): completed_activity(rows)[1]["item"]["kind"]="interacted"
        def malformed(rows): completed_activity(rows)[1]["item"]["agent_thread_id"]={}
        def duplicate(rows):
            index,_event=completed_activity(rows);rows.insert(index+1,json.loads(json.dumps(rows[index])))
        def before_started_activity(rows):
            index,_event=completed_activity(rows);record=rows.pop(index)
            started=next(index for index,row in enumerate(rows)
                         if row["type"]=="event_msg" and row["payload"].get("type")=="item_completed"
                         and row["payload"].get("item",{}).get("type")=="SubAgentActivity"
                         and row["payload"]["item"].get("kind")=="started")
            rows.insert(started,record)
        for mutation in (wrong_child,wrong_path,wrong_turn,wrong_kind,malformed,duplicate,before_started_activity):
            with self.subTest(case=mutation.__name__),tempfile.TemporaryDirectory() as temporary:
                root=pathlib.Path(temporary);sessions,parent,child=self._write_rollouts(root);self._mutate_rollout(parent,mutation)
                with self.assertRaises(support.Refusal):
                    support.audit_original_rollouts(str(parent),str(child),sessions,self._v2_audit_evidence(),profile)

    def test_original_completed_subagent_activity_can_arrive_late_without_terminal_authority(self):
        profile=support.load_profile(PROFILE_PATH)
        with tempfile.TemporaryDirectory() as temporary:
            root=pathlib.Path(temporary);sessions,parent,child=self._write_rollouts(root)
            def move_after_terminal(rows):
                index=next(index for index,row in enumerate(rows)
                           if row["type"]=="event_msg" and row["payload"].get("type")=="item_completed"
                           and row["payload"].get("item",{}).get("type")=="SubAgentActivity"
                           and row["payload"]["item"].get("kind")=="completed")
                rows.append(rows.pop(index))
            self._mutate_rollout(parent,move_after_terminal)
            value=self._v2_audit_evidence()
            result=support.audit_original_rollouts(str(parent),str(child),sessions,value,profile)
        self.assertTrue(value.original_audit);self.assertEqual(19,result["parent"]["records"])

    def test_original_rollout_refuses_numeric_type_substitution_and_overflow(self):
        profile=support.load_profile(PROFILE_PATH)
        for value in (30000.0,True,-1,30001):
            with self.subTest(timeout=value),tempfile.TemporaryDirectory() as temporary:
                root=pathlib.Path(temporary)
                def mutate(_spawn,wait):wait["arguments"]=json.dumps({"timeout_ms":value},separators=(",", ":"))
                sessions,parent,child=self._write_rollouts(root,mutate)
                with self.assertRaisesRegex(support.Refusal,"wait arguments"):
                    support.audit_original_rollouts(str(parent),str(child),sessions,self._v2_audit_evidence(),profile)
        with tempfile.TemporaryDirectory() as temporary:
            root=pathlib.Path(temporary);sessions,parent,child=self._write_rollouts(root)
            def numeric_bool(rows):
                output=next(row["payload"] for row in rows if row["type"]=="response_item" and row["payload"].get("call_id")=="call-wait" and "output" in row["payload"])
                output["output"]='{"message":"Wait completed.","timed_out":0}'
            self._mutate_rollout(parent,numeric_bool)
            with self.assertRaisesRegex(support.Refusal,"wait did not complete"):
                support.audit_original_rollouts(str(parent),str(child),sessions,self._v2_audit_evidence(),profile)
        with self.assertRaisesRegex(support.Refusal,"nonfinite"):
            support.strict_json(b'{"value":1e999}',"overflow")

    def test_original_rollout_audit_refuses_hidden_tools_bad_fork_child_calls_and_incomplete_files(self):
        profile=support.load_profile(PROFILE_PATH)
        scenarios=(
            (lambda _s,w:w.update(name="list_agents"),None,True),
            (lambda s,_w:s.update(arguments=json.dumps({"message":support.FIXED_CHILD_PROMPT,"task_name":support.FIXED_TASK_NAME,"model":"gpt-5.6-sol","reasoning_effort":"medium"})),None,True),
            (None,{"type":"function_call","name":"list_agents","call_id":"hidden","arguments":"{}"},True),
            (None,None,False))
        for mutate,child_extra,terminate in scenarios:
            with self.subTest(mutate=bool(mutate),child=bool(child_extra),terminate=terminate), tempfile.TemporaryDirectory() as temporary:
                root=pathlib.Path(temporary);sessions,parent,child=self._write_rollouts(root,mutate,child_extra,terminate)
                with self.assertRaises(support.Refusal):support.audit_original_rollouts(str(parent),str(child),sessions,self._v2_audit_evidence(),profile)

    def test_original_rollout_custody_refuses_hardlink_and_persistence_health_is_bounded(self):
        profile=support.load_profile(PROFILE_PATH)
        with tempfile.TemporaryDirectory() as temporary:
            root=pathlib.Path(temporary);sessions,parent,child=self._write_rollouts(root);os.link(child,child.with_name("other.jsonl"))
            with self.assertRaisesRegex(support.Refusal,"custody"):
                support.audit_original_rollouts(str(parent),str(child),sessions,self._v2_audit_evidence(),profile)
        with tempfile.TemporaryDirectory() as temporary:
            root=pathlib.Path(temporary);worker=root/"health.py"
            worker.write_text("#!/usr/bin/env python3\nimport os,sys\nsys.stdin.buffer.read()\nos.write(2,b'failed to flush rollout writer\\n')\n") ;worker.chmod(0o700)
            server=support.JsonLineAppServer([str(worker)],{"PATH":os.environ["PATH"]},root,2,4096)
            try:
                with self.assertRaisesRegex(support.Refusal,"persistence health"):
                    server.shutdown_and_verify_persistence(time.monotonic()+2)
            finally:server.close()
            clean=root/"clean.py";clean.write_text("#!/usr/bin/env python3\nimport sys\nsys.stdin.buffer.read()\n");clean.chmod(0o700)
            server=support.JsonLineAppServer([str(clean)],{"PATH":os.environ["PATH"]},root,2,4096)
            try:self.assertEqual(0,server.shutdown_and_verify_persistence(time.monotonic()+2)["knownPersistenceErrors"])
            finally:server.close()
            unknown=root/"unknown.py";unknown.write_text("#!/usr/bin/env python3\nimport os,sys\nsys.stdin.buffer.read()\nos.write(2,b'UNRECOGNIZED WRITER DIAGNOSTIC\\n')\n");unknown.chmod(0o700)
            server=support.JsonLineAppServer([str(unknown)],{"PATH":os.environ["PATH"]},root,2,4096)
            try:
                with self.assertRaisesRegex(support.Refusal,"unknown app-server persistence health"):
                    server.shutdown_and_verify_persistence(time.monotonic()+2)
            finally:server.close()

    def test_original_rollout_descriptor_walk_refuses_ancestor_symlink_swap(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=pathlib.Path(temporary);sessions,parent,_child=self._write_rollouts(root)
            outside=root/"outside";outside.mkdir(mode=0o700)
            outside_file=outside/parent.name;outside_file.write_bytes(parent.read_bytes());outside_file.chmod(0o600)
            original_open=os.open;month=parent.parent;renamed=month.with_name("10-held");swapped=False
            def swapping_open(path,*args,dir_fd=None,**kwargs):
                nonlocal swapped
                if path==parent.name and dir_fd is not None and not swapped:
                    month.rename(renamed);month.symlink_to(outside,target_is_directory=True);swapped=True
                return original_open(path,*args,dir_fd=dir_fd,**kwargs)
            with mock.patch.object(support.os,"open",side_effect=swapping_open):
                with self.assertRaisesRegex(support.Refusal,"ancestry changed"):
                    support._private_rollout_snapshot(str(parent),sessions)
            self.assertTrue(swapped)

    def test_original_rollout_directory_failures_close_every_open_descriptor(self):
        def fd_count():return len(os.listdir("/proc/self/fd"))
        for target in ("root","intermediate"):
            with self.subTest(target=target),tempfile.TemporaryDirectory() as temporary:
                root=pathlib.Path(temporary);sessions,parent,_child=self._write_rollouts(root)
                (sessions if target=="root" else parent.parent).chmod(0o777)
                before=fd_count()
                with mock.patch.object(support.os,"read") as read:
                    with self.assertRaises(support.Refusal):support._private_rollout_snapshot(str(parent),sessions)
                self.assertEqual(before,fd_count());read.assert_not_called()
        for failing_fstat in (1,2):
            with self.subTest(fstat=failing_fstat),tempfile.TemporaryDirectory() as temporary:
                root=pathlib.Path(temporary);sessions,parent,_child=self._write_rollouts(root)
                actual_fstat=support.os.fstat;calls=0
                def broken(descriptor):
                    nonlocal calls
                    calls+=1
                    if calls==failing_fstat:raise OSError("synthetic fstat refusal")
                    return actual_fstat(descriptor)
                before=fd_count()
                with mock.patch.object(support.os,"fstat",side_effect=broken):
                    with self.assertRaisesRegex(support.Refusal,"custody"):
                        support._private_rollout_snapshot(str(parent),sessions)
                self.assertEqual(before,fd_count())

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

    def test_rejected_item_diagnostic_is_closed_bounded_and_content_free(self):
        evidence, _ = self.ready()
        sentinel = "PRIVATE item text /tmp/private --secret credential argument"
        cases = (
            ("item/started", {"threadId": PARENT, "item": {"type": "webSearch", "text": sentinel, "arguments": {"secret": sentinel}}},
             ("started", "object", "token", "webSearch", "parent")),
            ("item/completed", {"threadId": CHILD, "item": {}},
             ("completed", "object", "missing", None, "other")),
            ("item/started", {"threadId": PARENT, "item": [sentinel]},
             ("started", "non-object", "missing", None, "parent")),
            ("item/completed", {"threadId": PARENT, "item": {"type": {"private": sentinel}}},
             ("completed", "object", "non-string", None, "parent")),
            ("item/completed", {"item": {"type": sentinel * 8}},
             ("completed", "object", "overlength", None, "unbound")),
            ("item/started", {"threadId": PARENT, "item": {"type": "unsafe/type", sentinel: sentinel}},
             ("started", "object", "invalid", None, "parent")),
        )
        for method, params, expected in cases:
            with self.subTest(method=method, expected=expected):
                with self.assertRaises(driver.RejectedItemRefusal) as caught:
                    driver.checked_item(method, params, evidence)
                diagnostic = caught.exception.diagnostic
                self.assertEqual({"schema", "eventKind", "itemShape", "itemMemberCount",
                                  "itemMemberCountTruncated", "discriminatorShape", "relationship"}
                                 | ({"discriminator"} if expected[3] else set()), set(diagnostic))
                self.assertEqual(expected, (diagnostic["eventKind"], diagnostic["itemShape"],
                                             diagnostic["discriminatorShape"], diagnostic.get("discriminator"),
                                             diagnostic["relationship"]))
                encoded = json.dumps(driver.error_envelope(caught.exception), separators=(",", ":"))
                self.assertLess(len(encoded.encode("utf-8")), 1024)
                self.assertNotIn(sentinel, encoded)
                for forbidden in ("text", "arguments", "secret", "/tmp/private", PARENT, CHILD):
                    self.assertNotIn(forbidden, encoded)
        evidence.spawn(spawn()); evidence.bind_child(child_thread())
        for thread_id, relationship in ((CHILD, "child"), ("33333333-3333-7333-8333-333333333333", "other")):
            with self.assertRaises(driver.RejectedItemRefusal) as caught:
                driver.checked_item("item/started", {"threadId": thread_id, "item": {"type": "unknownItem"}}, evidence)
            self.assertEqual(relationship, caught.exception.diagnostic["relationship"])
            self.assertNotIn(thread_id, json.dumps(caught.exception.diagnostic))

    def test_item_guard_preserves_permitted_types_and_generic_error_shape(self):
        evidence, _ = self.ready()
        for item_type in driver.PERMITTED_ITEM_TYPES:
            item = {"type": item_type, "text": "private", "arguments": {"private": True}}
            self.assertIs(item, driver.checked_item("item/completed", {"threadId": PARENT, "item": item}, evidence))
        generic = driver.error_envelope(support.Refusal("fixed refusal"))
        self.assertEqual({"schema": "fsgg.telemetry.native-operation-error/1",
                          "code": "qualification-refused", "message": "fixed refusal"}, generic)

    def test_rejected_item_count_is_capped_without_key_or_value_disclosure(self):
        evidence, _ = self.ready()
        item = {f"private-{index}": f"secret-{index}" for index in range(80)}
        item["type"] = "unknownItem"
        with self.assertRaises(driver.RejectedItemRefusal) as caught:
            driver.checked_item("item/completed", {"threadId": PARENT, "item": item}, evidence)
        diagnostic = caught.exception.diagnostic
        self.assertEqual((64, True, "unknownItem"),
                         (diagnostic["itemMemberCount"], diagnostic["itemMemberCountTruncated"],
                          diagnostic["discriminator"]))
        encoded = json.dumps(diagnostic)
        self.assertNotIn("private-", encoded); self.assertNotIn("secret-", encoded)

    def test_perform_event_loop_closes_and_stops_on_both_rejected_item_events(self):
        profile = support.load_profile(PROFILE_PATH)
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            marker = root / "marker"; marker.write_text(profile["producer"]["coherentPayloadSha256"] + "\n")
            profile = json.loads(json.dumps(profile)); profile["producer"]["coherentMarker"] = str(marker)
            profile["native"]["executable"] = str(root / "native")
            profile["native"]["config"] = str(root / "config")
            profile["producer"]["executable"] = str(root / "engine")
            profile["producer"]["telemetryConfig"] = str(root / "telemetry.json")
            profile["runtime"]["home"] = str(root / "home")
            profile["runtime"]["codexHome"] = str(root / "codex-home")
            profile["runtime"]["cwd"] = str(root / "cwd")
            for method in ("item/started", "item/completed"):
                with self.subTest(method=method):
                    class FakeServer:
                        def __init__(self): self.notifications = []; self.closed = False
                        def notify(self, _method): pass
                        def request(self, name, _params, _deadline):
                            if name == "initialize": return {}
                            if name == "config/read": return {}
                            if name == "thread/start": return parent_response()
                            if name == "thread/read": return {"thread": {**parent_response()["thread"], "path": str(root / "parent.jsonl")}}
                            if name == "turn/start": return {"turn": {"id": "turn-parent"}}
                            raise AssertionError(f"unexpected request after refusal: {name}")
                        def read(self, _deadline):
                            return {"method": method, "params": {"threadId": PARENT,
                                    "item": {"type": "webSearch", "text": "PRIVATE-SENTINEL"}}}
                        def close(self): self.closed = True
                    server = FakeServer(); commands = []
                    def command_result(_engine, _config, _environment, arguments):
                        commands.append(arguments)
                        if arguments[0] == "begin":
                            begin_count = sum(row[0] == "begin" for row in commands)
                            return receipt(token=("a" if begin_count == 1 else "b") * 32)
                        if arguments[0] == "started": return {"status": "started"}
                        raise AssertionError(f"unexpected command after refusal: {arguments[0]}")
                    version = subprocess.CompletedProcess([], 0, profile["producer"]["executableVersion"] + "\n", "")
                    with mock.patch.object(driver, "pinned_native"), \
                         mock.patch.object(driver, "regular"), \
                         mock.patch.object(driver, "private_workspace_config"), \
                         mock.patch.object(driver, "private_directory"), \
                         mock.patch.object(driver, "clean_environment", return_value={}), \
                         mock.patch.object(driver, "config_arguments", return_value=[]), \
                         mock.patch.object(driver, "effective_config"), \
                         mock.patch.object(driver, "producer_environment", return_value={}), \
                         mock.patch.object(driver, "workspace_binding", return_value={}), \
                         mock.patch.object(driver, "require_applied", return_value={}), \
                         mock.patch.object(driver, "command_result", side_effect=command_result), \
                         mock.patch.object(driver, "JsonLineAppServer", return_value=server), \
                         mock.patch.object(driver.subprocess, "run", return_value=version), \
                         mock.patch.object(driver, "private_write") as private_write:
                        with self.assertRaises(driver.RejectedItemRefusal) as caught:
                            driver.perform(profile, "owner-nonce-01", root)
                    self.assertEqual((method.split("/")[1], "webSearch", "parent"),
                                     (caught.exception.diagnostic["eventKind"],
                                      caught.exception.diagnostic["discriminator"],
                                      caught.exception.diagnostic["relationship"]))
                    self.assertTrue(server.closed); private_write.assert_not_called()
                    self.assertEqual(["begin", "started", "begin"], [row[0] for row in commands])

    def test_perform_v2_event_loop_defers_finishes_until_health_and_original_audit(self):
        profile = support.load_profile(PROFILE_PATH)
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary); marker = root / "marker"; marker.write_text(profile["producer"]["coherentPayloadSha256"] + "\n")
            profile = json.loads(json.dumps(profile)); profile["producer"]["coherentMarker"] = str(marker)
            profile["native"]["executable"] = str(root / "native"); profile["native"]["config"] = str(root / "config")
            profile["producer"]["executable"] = str(root / "engine"); profile["producer"]["telemetryConfig"] = str(root / "telemetry.json")
            profile["runtime"]["home"] = str(root / "home"); profile["runtime"]["codexHome"] = str(root / "codex-home"); profile["runtime"]["cwd"] = str(root / "cwd")
            wait = spawn(tool="wait"); wait["receiverThreadIds"] = []; wait["agentsStates"] = {}
            complete_activity = activity("completed", "subagent-completed-turn-child")
            events = [
                {"method":"item/started","params":{"threadId":PARENT,"item":activity()}},
                {"method":"item/completed","params":{"threadId":PARENT,"item":activity()}},
                {"method":"item/completed","params":{"threadId":CHILD,"item":{"type":"agentMessage","id":"child-ack","text":"NATIVE-CHILD-ACK"}}},
                {"method":"turn/completed","params":{"threadId":CHILD,"turn":{"id":"turn-child","status":"completed"}}},
                {"method":"item/started","params":{"threadId":PARENT,"item":{**wait,"status":"inProgress"}}},
                {"method":"item/completed","params":{"threadId":PARENT,"item":wait}},
                {"method":"item/started","params":{"threadId":PARENT,"item":complete_activity}},
                {"method":"item/completed","params":{"threadId":PARENT,"item":complete_activity}},
                {"method":"item/completed","params":{"threadId":PARENT,"item":{"type":"agentMessage","id":"parent-ack","text":"NATIVE-PARENT-ACK"}}},
                {"method":"turn/completed","params":{"threadId":PARENT,"turn":{"id":"turn-parent","status":"completed"}}},
            ]
            child_read={**child_thread(),"path":str(root/"codex-home/sessions/child.jsonl")}
            child_read["turns"]=[{"id":"turn-child","status":"completed","items":[{"type":"userMessage","id":"cu"},{"type":"agentMessage","id":"ca","text":"NATIVE-CHILD-ACK"}]}]
            parent_read={"id":PARENT,"ephemeral":False,"path":str(root/"codex-home/sessions/parent.jsonl"),"turns":[{"id":"turn-parent","status":"completed","items":[
                {"type":"userMessage","id":"pu"},activity(),wait,complete_activity,{"type":"agentMessage","id":"pa","text":"NATIVE-PARENT-ACK"}]}]}
            class FakeServer:
                def __init__(self):self.notifications=[];self.closed=False;self.health=False
                def notify(self,_method):pass
                def request(self,name,params,_deadline):
                    if name=="initialize":return {}
                    if name=="config/read":return {}
                    if name=="thread/start":return parent_response()
                    if name=="turn/start":return {"turn":{"id":"turn-parent"}}
                    if name=="thread/read":
                        if params["threadId"]==CHILD:return {"thread":child_read if params["includeTurns"] else {key:value for key,value in child_read.items() if key!="turns"}}
                        return {"thread":parent_read if params["includeTurns"] else {key:value for key,value in parent_read.items() if key!="turns"}}
                    raise AssertionError(name)
                def read(self,_deadline):return events.pop(0)
                def shutdown_and_verify_persistence(self,_deadline):self.health=True;return {"bytes":0,"sha256":"e"*64,"knownPersistenceErrors":0,"serverExit":0}
                def close(self):self.closed=True
            server=FakeServer();commands=[]
            def command_result(_engine,_config,_environment,arguments):
                commands.append((arguments[0],server.health))
                if arguments[0]=="begin":return receipt(token=("a" if sum(row[0]=="begin" for row in commands)==1 else "b")*32)
                if arguments[0]=="started":return {"status":"started"}
                if arguments[0]=="finish":return receipt(status="terminal",token="a"*32,outcome="completed")
                raise AssertionError(arguments)
            def audit(_parent,_child,_root,evidence,_profile):
                self.assertTrue(server.health);evidence.original_audit=True;return {}
            version=subprocess.CompletedProcess([],0,profile["producer"]["executableVersion"]+"\n","")
            with mock.patch.object(driver,"pinned_native"),mock.patch.object(driver,"regular"),mock.patch.object(driver,"private_workspace_config"), \
                 mock.patch.object(driver,"private_directory"),mock.patch.object(driver,"clean_environment",return_value={}), \
                 mock.patch.object(driver,"config_arguments",return_value=[]),mock.patch.object(driver,"effective_config"), \
                 mock.patch.object(driver,"producer_environment",return_value={}),mock.patch.object(driver,"workspace_binding",return_value={}), \
                 mock.patch.object(driver,"require_applied",return_value={}),mock.patch.object(driver,"command_result",side_effect=command_result), \
                 mock.patch.object(driver,"JsonLineAppServer",return_value=server),mock.patch.object(driver,"audit_original_rollouts",side_effect=audit), \
                 mock.patch.object(driver.subprocess,"run",return_value=version),mock.patch.object(driver,"private_write"):
                result=driver.perform(profile,"owner-nonce-01",root)
            self.assertEqual(("qualified","v2","bounded-complete"),(result["status"],result["protocolMode"],result["originalRolloutAudit"]))
            finishes=[health for command,health in commands if command=="finish"];self.assertEqual([True,True],finishes)
            self.assertTrue(server.closed);self.assertFalse(events)

    def test_perform_v2_late_completion_mismatch_has_zero_completed_finishes(self):
        profile=support.load_profile(PROFILE_PATH)
        with tempfile.TemporaryDirectory() as temporary:
            root=pathlib.Path(temporary);marker=root/"marker";marker.write_text(profile["producer"]["coherentPayloadSha256"]+"\n")
            profile=json.loads(json.dumps(profile));profile["producer"]["coherentMarker"]=str(marker)
            profile["native"].update(executable=str(root/"native"),config=str(root/"config"))
            profile["producer"].update(executable=str(root/"engine"),telemetryConfig=str(root/"telemetry.json"))
            profile["runtime"].update(home=str(root/"home"),codexHome=str(root/"codex-home"),cwd=str(root/"cwd"))
            wait=spawn(tool="wait");wait["receiverThreadIds"]=[];wait["agentsStates"]={}
            completion=activity("completed","subagent-completed-foreign-turn")
            events=[
                {"method":"item/started","params":{"threadId":PARENT,"item":activity()}},
                {"method":"item/completed","params":{"threadId":PARENT,"item":activity()}},
                {"method":"item/completed","params":{"threadId":CHILD,"item":{"type":"agentMessage","id":"child-ack","text":"NATIVE-CHILD-ACK"}}},
                {"method":"turn/completed","params":{"threadId":CHILD,"turn":{"id":"turn-child","status":"completed"}}},
                {"method":"item/started","params":{"threadId":PARENT,"item":{**wait,"status":"inProgress"}}},
                {"method":"item/completed","params":{"threadId":PARENT,"item":wait}},
                {"method":"item/started","params":{"threadId":PARENT,"item":completion}},
                {"method":"item/completed","params":{"threadId":PARENT,"item":completion}},
                {"method":"item/completed","params":{"threadId":PARENT,"item":{"type":"agentMessage","id":"parent-ack","text":"NATIVE-PARENT-ACK"}}},
                {"method":"turn/completed","params":{"threadId":PARENT,"turn":{"id":"turn-parent","status":"completed"}}},]
            child_read={**child_thread(),"path":str(root/"codex-home/sessions/child.jsonl"),"turns":[{"id":"turn-child","status":"completed","items":[{"type":"userMessage","id":"cu"},{"type":"agentMessage","id":"ca","text":"NATIVE-CHILD-ACK"}]}]}
            parent_read={"id":PARENT,"ephemeral":False,"path":str(root/"codex-home/sessions/parent.jsonl"),"turns":[{"id":"turn-parent","status":"completed","items":[{"type":"userMessage","id":"pu"},activity(),wait,completion,{"type":"agentMessage","id":"pa","text":"NATIVE-PARENT-ACK"}]}]}
            class FakeServer:
                def __init__(self):self.notifications=[];self.closed=False
                def notify(self,_method):pass
                def request(self,name,params,_deadline):
                    if name=="initialize":return {}
                    if name=="config/read":return {}
                    if name=="thread/start":return parent_response()
                    if name=="turn/start":return {"turn":{"id":"turn-parent"}}
                    if name=="thread/read":
                        selected=child_read if params["threadId"]==CHILD else parent_read
                        return {"thread":selected if params["includeTurns"] else {key:value for key,value in selected.items() if key!="turns"}}
                    raise AssertionError(name)
                def read(self,_deadline):return events.pop(0)
                def shutdown_and_verify_persistence(self,_deadline):return {"bytes":0,"sha256":"e"*64,"knownPersistenceErrors":0,"serverExit":0}
                def close(self):self.closed=True
            server=FakeServer();commands=[]
            def command_result(_engine,_config,_environment,arguments):
                commands.append(arguments[0])
                if arguments[0]=="begin":return receipt(token=("a" if commands.count("begin")==1 else "b")*32)
                if arguments[0]=="started":return {"status":"started"}
                if arguments[0]=="finish":return receipt(status="terminal",token="a"*32,outcome="completed")
                raise AssertionError(arguments)
            def audit(_parent,_child,_root,evidence,_profile):evidence.original_audit=True;return {}
            version=subprocess.CompletedProcess([],0,profile["producer"]["executableVersion"]+"\n","")
            with mock.patch.object(driver,"pinned_native"),mock.patch.object(driver,"regular"),mock.patch.object(driver,"private_workspace_config"), \
                 mock.patch.object(driver,"private_directory"),mock.patch.object(driver,"clean_environment",return_value={}), \
                 mock.patch.object(driver,"config_arguments",return_value=[]),mock.patch.object(driver,"effective_config"), \
                 mock.patch.object(driver,"producer_environment",return_value={}),mock.patch.object(driver,"workspace_binding",return_value={}), \
                 mock.patch.object(driver,"require_applied",return_value={}),mock.patch.object(driver,"command_result",side_effect=command_result), \
                 mock.patch.object(driver,"JsonLineAppServer",return_value=server),mock.patch.object(driver,"audit_original_rollouts",side_effect=audit), \
                 mock.patch.object(driver.subprocess,"run",return_value=version),mock.patch.object(driver,"private_write"):
                with self.assertRaisesRegex(support.Refusal,"native operation incomplete"):
                    driver.perform(profile,"owner-nonce-01",root)
            self.assertNotIn("finish",commands);self.assertTrue(server.closed);self.assertFalse(events)

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
