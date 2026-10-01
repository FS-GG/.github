#!/usr/bin/env python3
"""Closed support primitives for the V2-HOST-01.8 native operation."""

from __future__ import annotations

import hashlib
import json
import math
import os
import pathlib
import re
import select
import signal
import stat
import subprocess
import time
from dataclasses import dataclass, field


class Refusal(Exception):
    pass


FIXED_CHILD_PROMPT = "Return exactly NATIVE-CHILD-ACK; do not read files, use any other tool, contact a service, or spawn another agent."
FIXED_TASK_NAME = "native_ack"
FIXED_WAIT_TIMEOUT_MS = 30000
FIXED_PROMPT = ("Use the native collaboration spawn_agent tool exactly once with exactly these arguments: "
                f"message={json.dumps(FIXED_CHILD_PROMPT)}, task_name={FIXED_TASK_NAME}, model=gpt-5.6-sol, "
                "reasoning_effort=medium, fork_turns=none. Then use wait_agent exactly once with timeout_ms=30000. "
                "Do not use list_agents, send_message, followup_task, interrupt_agent, any other tool, a retry, or a second wait. "
                "After the child has one completed turn containing exactly NATIVE-CHILD-ACK, return exactly NATIVE-PARENT-ACK.")
UUID = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}")
AGENT_PATH = re.compile(r"/[A-Za-z0-9_-]{1,128}(?:/[A-Za-z0-9_-]{1,128}){0,7}")
ROLLOUT_MAX_BYTES = 8 * 1024 * 1024
ROLLOUT_MAX_LINES = 4096


def require(value: bool, message: str) -> None:
    if not value:
        raise Refusal(message)


def canonical_bytes(value: object) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode()


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def strict_json(encoded: bytes, label: str) -> object:
    def unique(pairs: list[tuple[str, object]]) -> dict:
        value = {}
        for key, item in pairs:
            require(key not in value, f"duplicate {label} key")
            value[key] = item
        return value
    def finite_float(raw: str) -> float:
        value = float(raw)
        require(math.isfinite(value), f"nonfinite {label} value")
        return value
    return json.loads(encoded.decode("utf-8"), object_pairs_hook=unique, parse_float=finite_float,
                      parse_constant=lambda _value: (_ for _ in ()).throw(Refusal(f"nonfinite {label} value")))


def _private_rollout_snapshot(path_value: object, sessions_root: pathlib.Path) -> tuple[list[dict], dict]:
    require(isinstance(path_value, str) and path_value.startswith("/"), "original rollout path unavailable")
    path = pathlib.Path(path_value); require(path == pathlib.Path(os.path.normpath(path)), "original rollout path differs")
    require(sessions_root.is_absolute() and sessions_root == pathlib.Path(os.path.normpath(sessions_root)), "sessions root custody differs")
    root = sessions_root
    require(path.is_relative_to(root) and path != root, "original rollout outside sessions root")
    relative = path.relative_to(root); require(all(part not in {"", ".", ".."} for part in relative.parts),
                                               "original rollout path differs")
    held: list[tuple[int, tuple[int, int, int, int]]] = []
    owned_directories: list[int] = []
    links: list[tuple[int, str, tuple[int, int]]] = []
    descriptor = None
    try:
        root_descriptor = os.open(root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
        owned_directories.append(root_descriptor)
        root_info = os.fstat(root_descriptor)
        require(stat.S_ISDIR(root_info.st_mode) and root_info.st_uid == os.geteuid()
                and root_info.st_mode & 0o022 == 0, "sessions root custody differs")
        held.append((root_descriptor, (root_info.st_dev, root_info.st_ino, root_info.st_uid, root_info.st_mode)))
        parent_descriptor = root_descriptor
        for component in relative.parts[:-1]:
            child_descriptor = os.open(component, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC,
                                       dir_fd=parent_descriptor)
            owned_directories.append(child_descriptor)
            info = os.fstat(child_descriptor)
            require(stat.S_ISDIR(info.st_mode) and info.st_uid == os.geteuid() and info.st_mode & 0o022 == 0,
                    "original rollout ancestry differs")
            links.append((parent_descriptor, component, (info.st_dev, info.st_ino)))
            held.append((child_descriptor, (info.st_dev, info.st_ino, info.st_uid, info.st_mode)))
            parent_descriptor = child_descriptor
        descriptor = os.open(relative.parts[-1], os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC,
                             dir_fd=parent_descriptor)
        before = os.fstat(descriptor)
        require(stat.S_ISREG(before.st_mode) and before.st_uid == os.geteuid() and before.st_nlink == 1
                and before.st_mode & 0o777 == 0o600 and 0 < before.st_size <= ROLLOUT_MAX_BYTES,
                "original rollout custody differs")
        chunks, total = [], 0
        while True:
            chunk = os.read(descriptor, min(65536, ROLLOUT_MAX_BYTES + 1 - total))
            if not chunk: break
            chunks.append(chunk); total += len(chunk); require(total <= ROLLOUT_MAX_BYTES, "original rollout capacity exceeded")
        after = os.fstat(descriptor)
        linked = os.stat(relative.parts[-1], dir_fd=parent_descriptor, follow_symlinks=False)
        require((before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns, before.st_ctime_ns)
                == (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns, after.st_ctime_ns)
                and (before.st_dev, before.st_ino) == (linked.st_dev, linked.st_ino), "original rollout changed during audit")
        for held_descriptor, identity in held:
            info = os.fstat(held_descriptor)
            require((info.st_dev, info.st_ino, info.st_uid, info.st_mode) == identity,
                    "original rollout ancestry changed during audit")
        for parent_fd, component, identity in links:
            linked_directory = os.stat(component, dir_fd=parent_fd, follow_symlinks=False)
            require(stat.S_ISDIR(linked_directory.st_mode)
                    and (linked_directory.st_dev, linked_directory.st_ino) == identity,
                    "original rollout ancestry changed during audit")
        linked_root = root.lstat()
        require((linked_root.st_dev, linked_root.st_ino) == held[0][1][:2] and not root.is_symlink(),
                "sessions root changed during audit")
    except OSError as error:
        raise Refusal("original rollout custody differs") from error
    finally:
        if descriptor is not None: os.close(descriptor)
        for owned_descriptor in reversed(owned_directories): os.close(owned_descriptor)
    data = b"".join(chunks); require(data.endswith(b"\n"), "original rollout has incomplete tail")
    encoded_lines = data.splitlines(); require(1 <= len(encoded_lines) <= ROLLOUT_MAX_LINES, "original rollout record capacity exceeded")
    rows = []
    for encoded in encoded_lines:
        require(0 < len(encoded) <= 1024 * 1024, "original rollout line capacity exceeded")
        row = strict_json(encoded, "rollout"); require(isinstance(row, dict), "original rollout row differs"); rows.append(row)
    return rows, {"bytes": len(data), "records": len(rows), "sha256": hashlib.sha256(data).hexdigest()}


def _response_kind(payload: object) -> str:
    require(isinstance(payload, dict) and isinstance(payload.get("type"), str), "original response item differs")
    return payload["type"]


def _bounded_string(value: object, label: str, maximum: int = 4096) -> str:
    require(isinstance(value, str) and 0 < len(value) <= maximum, f"{label} differs")
    return value


def _stamped_turn(item: dict, expected_turn: str) -> None:
    metadata = item.get("internal_chat_message_metadata_passthrough")
    require(isinstance(metadata, dict)
            and set(metadata) <= {"turn_id", "create_time", "content_item_kinds"}
            and metadata.get("turn_id") == expected_turn, "original response turn differs")
    if "create_time" in metadata:
        create_time = metadata["create_time"]
        require(type(create_time) in {int, float} and math.isfinite(create_time) and create_time >= 0,
                "original response create time differs")
    if "content_item_kinds" in metadata:
        kinds = metadata["content_item_kinds"]
        require(isinstance(kinds, list) and len(kinds) <= 8
                and all(isinstance(kind, str) and 1 <= len(kind) <= 128 for kind in kinds),
                "original response content kinds differ")


def _response_id(item: dict) -> None:
    if "id" in item:
        _bounded_string(item["id"], "original response ID", 128)


def _harness_metadata(row: dict) -> None:
    if "metadata" not in row: return
    metadata = row["metadata"]
    require(isinstance(metadata, dict) and set(metadata) <= {"client_authored", "user_input_order"},
            "original harness metadata differs")
    if "client_authored" in metadata:
        require(type(metadata["client_authored"]) is bool and metadata["client_authored"] is False,
                "original harness metadata differs")
    if "user_input_order" in metadata:
        require(type(metadata["user_input_order"]) is int and 0 <= metadata["user_input_order"] <= 4096,
                "original harness metadata differs")


def _text_content(value: object, content_type: str, expected: str) -> None:
    require(isinstance(value, list) and len(value) == 1 and isinstance(value[0], dict)
            and set(value[0]) == {"type", "text"} and value[0].get("type") == content_type
            and value[0].get("text") == expected, "original response content differs")


def _message(item: dict, expected_turn: str, role: str, content_type: str, expected: str) -> None:
    require(set(item) <= {"type", "id", "role", "content", "phase", "internal_chat_message_metadata_passthrough"}
            and item.get("type") == "message" and item.get("role") == role, "original message differs")
    _response_id(item); _stamped_turn(item, expected_turn)
    if "phase" in item:
        require(item["phase"] in {"commentary", "final_answer"}, "original message phase differs")
    _text_content(item.get("content"), content_type, expected)


def _agent_message(item: dict, expected_turn: str, author: str, recipient: str, expected: str) -> None:
    require(set(item) <= {"type", "id", "author", "recipient", "content",
                          "internal_chat_message_metadata_passthrough"}
            and item.get("type") == "agent_message" and item.get("author") == author
            and item.get("recipient") == recipient, "original agent message differs")
    _response_id(item); _stamped_turn(item, expected_turn)
    _text_content(item.get("content"), "input_text", expected)


def _reasoning(item: dict, expected_turn: str) -> None:
    require(set(item) <= {"type", "id", "summary", "content", "encrypted_content",
                          "internal_chat_message_metadata_passthrough"}
            and item.get("type") == "reasoning", "original reasoning differs")
    _response_id(item); _stamped_turn(item, expected_turn)
    summary = item.get("summary")
    require(isinstance(summary, list) and len(summary) <= 32
            and all(isinstance(part, dict) and set(part) == {"type", "text"}
                    and part.get("type") == "summary_text" and isinstance(part.get("text"), str)
                    and len(part["text"]) <= 65536 for part in summary), "original reasoning summary differs")
    content = item.get("content")
    require(content is None or (isinstance(content, list) and len(content) <= 32
            and all(isinstance(part, dict) and set(part) == {"type", "text"}
                    and part.get("type") in {"reasoning_text", "text"} and isinstance(part.get("text"), str)
                    and len(part["text"]) <= 65536 for part in content)), "original reasoning content differs")
    encrypted = item.get("encrypted_content")
    require(encrypted is None or (isinstance(encrypted, str) and len(encrypted) <= 1024 * 1024),
            "original reasoning encryption differs")


def _strict_spawn_arguments(arguments: object) -> None:
    require(isinstance(arguments, dict)
            and set(arguments) == {"message", "task_name", "model", "reasoning_effort", "fork_turns"}
            and arguments.get("message") == FIXED_CHILD_PROMPT
            and arguments.get("task_name") == FIXED_TASK_NAME
            and arguments.get("model") == "gpt-5.6-sol"
            and arguments.get("reasoning_effort") == "medium"
            and arguments.get("fork_turns") == "none", "spawn arguments differ")


def _strict_wait_arguments(arguments: object) -> None:
    require(isinstance(arguments, dict) and set(arguments) == {"timeout_ms"}
            and type(arguments.get("timeout_ms")) is int
            and arguments["timeout_ms"] == FIXED_WAIT_TIMEOUT_MS, "wait arguments differ")


def _strict_wait_output(value: object) -> None:
    require(isinstance(value, dict) and set(value) == {"message", "timed_out"}
            and value.get("message") == "Wait completed."
            and type(value.get("timed_out")) is bool and value["timed_out"] is False, "wait did not complete")


def _completed_message_event(payload: dict, expected_thread: str, expected_turn: str,
                             response_items: dict[str, tuple[int, str, str | None]]) -> tuple[str, int]:
    require(set(payload) in ({"type", "thread_id", "turn_id", "item", "completed_at_ms"},
                             {"type", "thread_id", "turn_id", "item", "started_at_ms", "completed_at_ms"})
            and payload.get("type") == "item_completed" and payload.get("thread_id") == expected_thread
            and payload.get("turn_id") == expected_turn, "original completed-item identity differs")
    completed_at = payload.get("completed_at_ms")
    require(type(completed_at) is int and completed_at >= 0, "original completed-item time differs")
    if "started_at_ms" in payload:
        started_at = payload["started_at_ms"]
        require(started_at is None or (type(started_at) is int and 0 <= started_at <= completed_at),
                "original completed-item time differs")
    item = payload.get("item")
    require(isinstance(item, dict)
            and set(item) <= {"type", "id", "content", "phase", "memory_citation", "delivery", "questions"}
            and set(item) >= {"type", "id", "content"} and item.get("type") == "AgentMessage",
            "unknown original completed item")
    identifier = _bounded_string(item.get("id"), "original completed-item ID", 128)
    require(identifier in response_items, "unknown original completed item")
    response_index, response_text, response_phase = response_items[identifier]
    require(item.get("content") == [{"type": "Text", "text": response_text}]
            and item.get("phase") == response_phase
            and item.get("memory_citation") in (None, {}) and item.get("delivery") is None
            and item.get("questions") in (None, []), "original completed message differs")
    return identifier, response_index


def _validate_identity(meta: object, context: object, relation: str, ordinal_mode: bool,
                       evidence: "OperationEvidence", profile: dict) -> tuple[str, str]:
    require(isinstance(meta, dict) and isinstance(context, dict), "original identity record differs")
    expected_thread = evidence.parent_thread if relation == "parent" else evidence.child_thread
    expected_parent = None if relation == "parent" else evidence.parent_thread
    expected_turn = evidence.parent_turn_id if relation == "parent" else evidence.child_turn_id
    require(isinstance(expected_thread, str) and isinstance(expected_turn, str), "original live identity unavailable")
    require(meta.get("id") == expected_thread and meta.get("session_id") == evidence.parent_thread
            and meta.get("parent_thread_id") == expected_parent and meta.get("cli_version") == "0.158.0"
            and meta.get("model_provider") == profile["provider"]
            and meta.get("history_base") is None and meta.get("forked_from_id") is None
            and meta.get("forked_from_ordinal_exclusive") is None
            and meta.get("history_mode") in ("legacy", "paginated"), "original session identity differs")
    require((meta.get("history_mode") == "paginated") == ordinal_mode, "original ordinal/history mode differs")
    if relation == "child":
        require(meta.get("agent_path") == evidence.native_selector_path
                and meta.get("multi_agent_version") in ("v2", "V2"), "original child source differs")
        source = meta.get("source", {}).get("subagent", {}).get("thread_spawn", {}) if isinstance(meta.get("source"), dict) else {}
        require(source.get("parent_thread_id") == evidence.parent_thread and type(source.get("depth")) is int
                and source["depth"] == 1 and source.get("agent_path") == evidence.native_selector_path,
                "original child source differs")
    else:
        require(meta.get("source") == "mcp", "original parent source differs")
    require(context.get("turn_id") == expected_turn and context.get("model") == profile["model"]
            and context.get("effort") == profile["effort"]
            and context.get("multi_agent_version") in ("v2", "V2"), "original turn settings differ")
    require(context.get("root_turn_id") == (None if relation == "parent" else evidence.parent_turn_id),
            "original root-turn attribution differs")
    return expected_thread, expected_turn


def audit_original_rollouts(parent_path: object, child_path: object, sessions_root: pathlib.Path,
                            evidence: "OperationEvidence", profile: dict) -> dict:
    """Audit original persisted calls privately; return only bounded predicates/digests."""
    snapshots = []
    for relation, path_value in (("parent", parent_path), ("child", child_path)):
        rows, snapshot = _private_rollout_snapshot(path_value, sessions_root); snapshots.append(snapshot)
        metadata: list[tuple[int, object]] = []
        contexts: list[tuple[int, object]] = []
        started: list[tuple[int, object]] = []
        completed: list[tuple[int, object]] = []
        calls: list[tuple[int, dict]] = []
        outputs: dict[str, tuple[int, dict]] = {}
        messages: list[tuple[int, str, str]] = []
        communications: list[tuple[int, bool, dict]] = []
        response_messages: dict[str, tuple[int, str, str | None]] = {}
        completed_messages: dict[str, tuple[int, int]] = {}
        pending_communication: tuple[int, bool] | None = None
        ordinal_mode = None; next_ordinal = 0
        expected_turn = evidence.parent_turn_id if relation == "parent" else evidence.child_turn_id
        require(isinstance(expected_turn, str), "original live turn unavailable")
        for index, row in enumerate(rows):
            kind = row.get("type")
            allowed_outer = {"timestamp", "ordinal", "type", "payload"} | ({"metadata"} if kind == "response_item" else set())
            require(set(row) <= allowed_outer and set(row) >= {"timestamp", "type", "payload"}, "original rollout envelope differs")
            require(isinstance(row["timestamp"], str) and 1 <= len(row["timestamp"]) <= 64, "original rollout timestamp differs")
            if "ordinal" in row:
                require(type(row["ordinal"]) is int and row["ordinal"] == next_ordinal, "original rollout ordinal differs")
                next_ordinal += 1; require(ordinal_mode is not False, "mixed rollout ordinal mode"); ordinal_mode = True
            else:
                require(ordinal_mode is not True, "mixed rollout ordinal mode"); ordinal_mode = False
            payload = row["payload"]
            if pending_communication is not None:
                require(kind == "response_item" and isinstance(payload, dict)
                        and payload.get("type") == "agent_message", "original communication pair differs")
            if kind == "session_meta":
                require(pending_communication is None, "original communication pair differs")
                metadata.append((index, payload))
            elif kind == "turn_context":
                require(pending_communication is None, "original communication pair differs")
                contexts.append((index, payload))
            elif kind == "inter_agent_communication_metadata":
                require(pending_communication is None and isinstance(payload, dict)
                        and set(payload) == {"trigger_turn"} and type(payload["trigger_turn"]) is bool,
                        "original communication metadata differs")
                pending_communication = (index, payload["trigger_turn"])
            elif kind == "event_msg":
                require(pending_communication is None and isinstance(payload, dict), "original lifecycle event differs")
                event_type = payload.get("type")
                require(event_type in {"turn_started", "turn_complete", "item_completed",
                                       "token_count", "thread_settings_applied"}, "unknown original lifecycle event")
                if "turn_id" in payload:
                    require(payload["turn_id"] == expected_turn, "original event turn differs")
                if event_type == "turn_started": started.append((index, payload))
                elif event_type == "turn_complete": completed.append((index, payload))
                elif event_type == "item_completed":
                    expected_thread = evidence.parent_thread if relation == "parent" else evidence.child_thread
                    identifier, response_index = _completed_message_event(
                        payload, expected_thread, expected_turn, response_messages)
                    require(identifier not in completed_messages and response_index < index,
                            "duplicate or misordered original completed item")
                    completed_messages[identifier] = (index, response_index)
            elif kind == "response_item":
                _harness_metadata(row)
                response_kind = _response_kind(payload)
                if response_kind == "function_call":
                    require(pending_communication is None, "original communication pair differs")
                    calls.append((index, payload))
                elif response_kind == "function_call_output":
                    require(pending_communication is None, "original communication pair differs")
                    call_id = payload.get("call_id")
                    require(isinstance(call_id, str) and call_id not in outputs, "duplicate original call output")
                    outputs[call_id] = (index, payload)
                elif response_kind == "message":
                    require(pending_communication is None, "original communication pair differs")
                    role = payload.get("role")
                    if role == "user":
                        _message(payload, expected_turn, "user", "input_text", FIXED_PROMPT)
                        message_value = FIXED_PROMPT
                    else:
                        expected_ack = "NATIVE-PARENT-ACK" if relation == "parent" else "NATIVE-CHILD-ACK"
                        _message(payload, expected_turn, "assistant", "output_text", expected_ack)
                        message_value = expected_ack
                        identifier = _bounded_string(payload.get("id"), "original response ID", 128)
                        require(identifier not in response_messages, "duplicate original response ID")
                        response_messages[identifier] = (index, expected_ack, payload.get("phase"))
                    messages.append((index, role, message_value))
                elif response_kind == "agent_message":
                    require(pending_communication is not None, "unpaired original agent message")
                    metadata_index, trigger_turn = pending_communication; pending_communication = None
                    if relation == "child":
                        require(trigger_turn is True, "child spawn communication did not trigger turn")
                        _agent_message(payload, expected_turn, "/root", evidence.native_selector_path, FIXED_CHILD_PROMPT)
                    else:
                        require(trigger_turn is False, "parent completion communication triggered turn")
                        parent_path = evidence.native_selector_path.rsplit("/", 1)[0]
                        completion = (f"Message Type: FINAL_ANSWER\nTask name: {parent_path}\n"
                                      f"Sender: {evidence.native_selector_path}\nPayload:\nNATIVE-CHILD-ACK")
                        _agent_message(payload, expected_turn, evidence.native_selector_path, parent_path, completion)
                    communications.append((metadata_index, trigger_turn, payload))
                elif response_kind == "reasoning":
                    require(pending_communication is None, "original communication pair differs")
                    _reasoning(payload, expected_turn)
                else:
                    raise Refusal("prohibited original executable item")
            elif kind in {"token_usage_record", "world_state", "security_risk_score"}:
                require(pending_communication is None and isinstance(payload, dict), "original auxiliary record differs")
            else:
                raise Refusal("unknown original rollout record")
        require(pending_communication is None, "original communication pair incomplete")
        require(len(metadata) == 1 and len(contexts) == 1 and len(started) == 1 and len(completed) == 1,
                "original rollout lifecycle differs")
        _expected_thread, expected_turn = _validate_identity(metadata[0][1], contexts[0][1], relation,
                                                              ordinal_mode is True, evidence, profile)
        start_index, start = started[0]; complete_index, complete = completed[0]
        require(metadata[0][0] < contexts[0][0] < start_index < complete_index
                and start.get("turn_id") == expected_turn and complete.get("turn_id") == expected_turn
                and complete.get("error") in (None, {})
                and complete.get("last_agent_message") == ("NATIVE-PARENT-ACK" if relation == "parent" else "NATIVE-CHILD-ACK"),
                "original terminal boundary differs")
        assistant = [item for item in messages if item[1] == "assistant"]
        require(len(assistant) == 1 and len(completed_messages) == 1,
                f"{relation} original completed-message census differs")
        completed_index, response_index = next(iter(completed_messages.values()))
        require(response_index == assistant[0][0] and assistant[0][0] < completed_index < complete_index,
                f"{relation} original completed-message order differs")
        if relation == "child":
            require(not calls and not outputs and len(communications) == 1 and communications[0][1] is True
                    and len(messages) == 1 and len(assistant) == 1
                    and start_index < communications[0][0] < assistant[0][0] < complete_index,
                    "child original census differs")
            continue
        user = [item for item in messages if item[1] == "user"]
        require(len(user) == 1 and len(assistant) == 1 and len(communications) == 1
                and communications[0][1] is False, "parent original message census differs")
        require([call.get("name") for _index, call in calls] == ["spawn_agent", "wait_agent"]
                and all(isinstance(call.get("call_id"), str) for _index, call in calls),
                "original parent call census differs")
        require(len({call["call_id"] for _index, call in calls}) == 2
                and set(outputs) == {call["call_id"] for _index, call in calls}, "original call/output join differs")
        for _index, call in calls:
            require(set(call) <= {"type", "id", "name", "namespace", "arguments", "encrypted_function_args",
                                  "call_id", "internal_chat_message_metadata_passthrough"}
                    and (call.get("namespace") is None or call.get("namespace") in ("", "functions"))
                    and call.get("encrypted_function_args") in (None, [])
                    and isinstance(call.get("arguments"), str), "original function call differs")
            _response_id(call); _stamped_turn(call, expected_turn)
            arguments = strict_json(call["arguments"].encode(), "call arguments")
            if call["name"] == "spawn_agent":
                _strict_spawn_arguments(arguments)
                require(call["call_id"] == evidence.spawn_call_id, "spawn activity/call join differs")
            else:
                _strict_wait_arguments(arguments)
                require(call["call_id"] == evidence.wait_call_id, "wait activity/call join differs")
                output = outputs[call["call_id"]][1].get("output")
                require(isinstance(output, str), "wait output differs")
                _strict_wait_output(strict_json(output.encode(), "wait output"))
        for call_id, (_index, output_item) in outputs.items():
            require(set(output_item) <= {"type", "id", "call_id", "name", "namespace", "output",
                                         "internal_chat_message_metadata_passthrough"}
                    and output_item.get("type") == "function_call_output"
                    and output_item.get("call_id") == call_id and isinstance(output_item.get("output"), str),
                    "original function output differs")
            _response_id(output_item); _stamped_turn(output_item, expected_turn)
        spawn_output = outputs[evidence.spawn_call_id][1].get("output")
        parsed_spawn = strict_json(spawn_output.encode(), "spawn output")
        require(isinstance(parsed_spawn, dict) and set(parsed_spawn) in ({"task_name"}, {"task_name", "nickname"})
                and parsed_spawn.get("task_name") == evidence.native_selector_path
                and ("nickname" not in parsed_spawn or isinstance(parsed_spawn["nickname"], str)),
                "spawn output/source join differs")
        spawn_index, spawn_call = calls[0]; wait_index, wait_call = calls[1]
        spawn_output_index = outputs[spawn_call["call_id"]][0]
        wait_output_index = outputs[wait_call["call_id"]][0]
        require(start_index < user[0][0] < spawn_index < spawn_output_index < wait_index < wait_output_index
                < assistant[0][0] < complete_index
                and start_index < communications[0][0] < assistant[0][0],
                "original parent execution order differs")
    evidence.original_audit = True
    evidence.add("original-rollouts-audited", parentRecords=snapshots[0]["records"], childRecords=snapshots[1]["records"],
                 parentSha256=snapshots[0]["sha256"], childSha256=snapshots[1]["sha256"])
    return {"parent": snapshots[0], "child": snapshots[1], "persistenceHealth": "bounded-stderr-clean"}


def regular(path: pathlib.Path, maximum: int, executable: bool = False) -> None:
    info = path.lstat()
    require(stat.S_ISREG(info.st_mode) and not path.is_symlink(), f"regular file required: {path}")
    require(0 < info.st_size <= maximum, f"file size refused: {path}")
    require(not executable or os.access(path, os.X_OK), f"executable required: {path}")


def closed(value: dict, keys: set[str], label: str) -> None:
    require(isinstance(value, dict) and set(value) == keys, f"{label} shape differs")


def load_profile(path: pathlib.Path) -> dict:
    regular(path, 64 * 1024)
    profile = json.loads(path.read_text(encoding="utf-8"))
    closed(profile, {"schema", "operationId", "supportedOperations", "provider", "model", "effort", "prompt", "native", "producer", "runtime", "network"}, "profile")
    require(profile["schema"] == "fsgg.telemetry.native-operation-profile/1", "profile schema differs")
    require(profile["operationId"] == "v2-host-01.8a-native-collaboration-v1", "operation differs")
    require(profile["supportedOperations"] == ["v2-host-01.8a-native-collaboration-v1", "v2-host-01.8a-readonly-source-compatibility-v1"], "supported operations differ")
    require((profile["provider"], profile["model"], profile["effort"]) == ("openai", "gpt-5.6-sol", "medium"), "fixed model profile differs")
    require(profile["prompt"] == FIXED_PROMPT, "fixed prompt differs")
    native, producer, runtime, network = profile["native"], profile["producer"], profile["runtime"], profile["network"]
    closed(native, {"executable", "sha256", "bytes", "version", "config", "configSha256", "protocolSchemaSha256", "sessionFlagsSha256"}, "native")
    closed(producer, {"executable", "version", "executableVersion", "coherentPayloadSha256", "coherentMarker", "telemetryConfig",
                      "workspaceId", "producerId", "streamId", "repository", "receiverOrigin", "credentialReference",
                      "credentialEnvironment", "spoolRoot", "feature", "item", "rootAttempt", "childAttempt"}, "producer")
    closed(runtime, {"home", "codexHome", "cwd", "timeoutSeconds", "maximumLineBytes", "maximumEvents", "python"}, "runtime")
    closed(network, {"policySchema", "policyId", "privateNetworkId", "httpsProxy", "noProxy"}, "network")
    require(native == {
        "executable": "/opt/fsgg/codex/codex", "sha256": "167c0148a849d2444f1b5a7fb5f8bb2de1de5ae13a2a504b833fc765980f5cd9", "bytes": 286594376,
        "version": "0.158.0", "config": "/opt/fsgg/native-producer-config.toml",
        "configSha256": "11171d89fdf531a772c063df9c126538ebf62c47ebf8bae3f319d28d70966fb0",
        "protocolSchemaSha256": "5742a9a7dd41a8b44dca3138f506e013620d4a93573c792b1e5881c053f169a7",
        "sessionFlagsSha256": "6961dbfc2ddce5988b3e567a8d3e4a9680450f25b7648029f790e6a7975fbe49"}, "native pin differs")
    require(producer["executable"] == "/opt/fsgg/coord/fsgg-coord-engine" and producer["version"] == "0.94.0"
            and producer["executableVersion"] == "0.94.0.0" and producer["coherentMarker"] == "/opt/fsgg/coord/coherent-content.sha256"
            and producer["coherentPayloadSha256"] == "9b9486a54e014fd5d21b65ed71a00b9021562a56909a1303f89c9646bca4a585", "producer pin differs")
    require({key: producer[key] for key in ("workspaceId", "producerId", "streamId", "repository", "receiverOrigin",
                                             "credentialReference", "credentialEnvironment", "spoolRoot")} == {
        "workspaceId": "v2-host-native-qualification", "producerId": "native-prospective-v1", "streamId": "roadmap",
        "repository": "FS-GG/.github", "receiverOrigin": "https://native-receiver:7443/",
        "credentialReference": "native-prospective-v1",
        "credentialEnvironment": "FSGG_TELEMETRY_CREDENTIAL_NATIVE_PROSPECTIVE_V1",
        "spoolRoot": "/qualification/native/telemetry/spool"}, "producer receiver binding differs")
    require(runtime == {"home": "/qualification/native", "codexHome": "/qualification/native/.codex", "cwd": "/qualification/native/work",
                        "timeoutSeconds": 300, "maximumLineBytes": 1048576, "maximumEvents": 4096, "python": "3.14.0"}, "runtime profile differs")
    require(network == {"policySchema": "fsgg.telemetry.native-network-policy/1", "policyId": "fsgg-native-egress-v1", "privateNetworkId": "fsgg-native-private-v1",
                        "httpsProxy": "http://native-egress:3128", "noProxy": "localhost,127.0.0.1,[::1],native-receiver"}, "network profile differs")
    return profile


def clean_environment(profile: dict, parent: dict[str, str] | None = None) -> dict[str, str]:
    parent = parent or os.environ
    required = {"PATH": "/usr/local/bin:/usr/bin:/bin", "HOME": profile["runtime"]["home"],
                "CODEX_HOME": profile["runtime"]["codexHome"], "HTTPS_PROXY": profile["network"]["httpsProxy"],
                "NO_PROXY": profile["network"]["noProxy"], "LANG": "C.UTF-8", "LC_ALL": "C.UTF-8"}
    require("HTTP_PROXY" not in required and "ALL_PROXY" not in required, "proxy bypass present")
    return required


def producer_environment(profile: dict, parent_thread: str, parent: dict[str, str] | None = None) -> dict[str, str]:
    parent = parent or os.environ
    credential_name = profile["producer"]["credentialEnvironment"]
    unexpected = [name for name in parent if name.startswith("FSGG_TELEMETRY_CREDENTIAL_") and name != credential_name]
    require(not unexpected, "unreviewed telemetry credential environment refused")
    credential = parent.get(credential_name)
    require(isinstance(credential, str) and 0 < len(credential) <= 16 * 1024, "fixed telemetry credential is unavailable")
    environment = clean_environment(profile, parent)
    environment["PATH"] = "/opt/fsgg/coord:" + environment["PATH"]
    environment["CODEX_THREAD_ID"] = parent_thread
    environment["FSGG_TELEMETRY_REPOSITORY"] = profile["producer"]["repository"]
    environment[credential_name] = credential
    return environment


@dataclass
class OperationEvidence:
    operation_id: str
    nonce: str
    events: list[dict] = field(default_factory=list)
    parent_thread: str | None = None
    child_thread: str | None = None
    native_selector: str | None = None
    native_selector_path: str | None = None
    root_token: str | None = None
    child_token: str | None = None
    child_terminal: bool = False
    parent_terminal: bool = False
    spawn_count: int = 0
    wait_count: int = 0
    child_ack: bool = False
    parent_ack: bool = False
    child_ack_notifications: int = 0
    parent_ack_notifications: int = 0
    child_terminal_notifications: int = 0
    child_terminal_evidence: str | None = None
    child_turn_id: str | None = None
    parent_turn_id: str | None = None
    child_turn_readback: bool = False
    parent_turn_readback: bool = False
    history_parent_spawn: int | None = None
    history_parent_wait: int | None = None
    history_parent_ack: int | None = None
    history_child_ack: int | None = None
    history_prohibited_tools: int | None = None
    history_followups: int | None = None
    protocol_mode: str | None = None
    spawn_call_id: str | None = None
    wait_call_id: str | None = None
    activity_pairs: dict[str, dict] = field(default_factory=dict)
    completion_activity: bool = False
    completion_activity_id: str | None = None
    parent_rollout_path: str | None = None
    child_rollout_path: str | None = None
    original_audit: bool = False

    def add(self, kind: str, **values: object) -> None:
        self.events.append({"sequence": len(self.events) + 1, "kind": kind, **values})

    def begin(self, relation: str, result: dict) -> str:
        require(result.get("schema") == "fsgg.telemetry.roadmap-dispatch/1" and result.get("status") == "expected", f"{relation} begin was not applied")
        token = result.get("token")
        require(isinstance(token, str) and re.fullmatch(r"[0-9a-f]{32}", token) is not None, f"{relation} token differs")
        if relation == "root": self.root_token = token
        else:
            require(self.root_token is not None and self.parent_thread is not None, "child begin preceded parent identity")
            self.child_token = token
        self.add(f"{relation}-begin-applied", tokenSha256=hashlib.sha256(token.encode()).hexdigest())
        return token

    def thread_started(self, response: dict) -> str:
        thread = response.get("thread", response.get("result", {}).get("thread", {}))
        identifier = thread.get("id")
        require(isinstance(identifier, str) and re.fullmatch(r"[0-9a-f-]{36}", identifier), "parent thread UUID differs")
        require(thread.get("ephemeral") is False, "parent thread must persist")
        self.parent_thread = identifier; self.add("parent-thread-started", threadId=identifier)
        return identifier

    def bind_rollout_path(self, relation: str, thread: dict) -> None:
        require(isinstance(thread, dict), f"{relation} original rollout path unavailable")
        expected = self.parent_thread if relation == "parent" else self.child_thread
        path = thread.get("path")
        require(thread.get("id") == expected and isinstance(path, str) and 1 <= len(path) <= 4096,
                f"{relation} original rollout path unavailable")
        previous = self.parent_rollout_path if relation == "parent" else self.child_rollout_path
        require(previous in {None, path}, f"{relation} original rollout path changed")
        if relation == "parent": self.parent_rollout_path = path
        else: self.child_rollout_path = path

    def spawn(self, item: dict) -> str:
        require(self.protocol_mode in {None, "legacy"}, "mixed collaboration protocol refused"); self.protocol_mode = "legacy"
        require(self.child_token is not None, "spawn preceded prospective child begin")
        require(item.get("type") == "collabAgentToolCall" and item.get("tool") == "spawnAgent", "unexpected collaboration operation")
        require(item.get("status") == "completed" and item.get("senderThreadId") == self.parent_thread, "spawn result differs")
        require(item.get("model") == "gpt-5.6-sol" and item.get("reasoningEffort") == "medium"
                and item.get("prompt") == FIXED_CHILD_PROMPT, "spawn model profile differs")
        receivers = item.get("receiverThreadIds")
        require(isinstance(receivers, list) and len(receivers) == 1 and re.fullmatch(r"[0-9a-f-]{36}", receivers[0]), "spawn child UUID differs")
        state = item.get("agentsStates", {}).get(receivers[0], {}).get("status")
        require(state in {"running", "completed"}, "spawn result did not start child")
        self.spawn_count += 1; require(self.spawn_count == 1, "extra child refused")
        self.child_thread = receivers[0]; self.add("actual-spawn-result", childThreadId=self.child_thread)
        return self.child_thread

    def collaboration(self, item: dict) -> str | None:
        tool = item.get("tool")
        if tool == "spawnAgent": return self.spawn(item)
        require(tool == "wait" and self.child_thread is not None and item.get("status") == "completed", "unexpected collaboration operation")
        self.wait_count += 1; require(self.wait_count == 1, "extra collaboration wait refused")
        receivers = item.get("receiverThreadIds")
        if self.protocol_mode == "v2":
            require(receivers == [] and item.get("agentsStates") == {} and item.get("senderThreadId") == self.parent_thread,
                    "v2 wait projection differs")
            identifier = item.get("id")
            require(isinstance(identifier, str) and 1 <= len(identifier) <= 128
                    and self.wait_call_id in {None, identifier}, "v2 wait identity differs")
            self.wait_call_id = identifier
        else:
            require(receivers == [self.child_thread] and item.get("senderThreadId") == self.parent_thread, "collaboration wait identity differs")
            require(item.get("agentsStates", {}).get(self.child_thread, {}).get("status") == "completed",
                    "collaboration wait did not observe a terminal child")
        self.add("child-wait-completed")
        return None

    def activity(self, method: str, item: dict) -> str | None:
        require(self.protocol_mode in {None, "v2"}, "mixed collaboration protocol refused"); self.protocol_mode = "v2"
        require(method in {"item/started", "item/completed"}, "sub-agent activity envelope differs")
        closed(item, {"type", "id", "kind", "agentThreadId", "agentPath"}, "sub-agent activity")
        identifier, kind, child, path = item["id"], item["kind"], item["agentThreadId"], item["agentPath"]
        require(isinstance(identifier, str) and 1 <= len(identifier) <= 128 and isinstance(child, str) and UUID.fullmatch(child) is not None
                and isinstance(path, str) and len(path) <= 512 and AGENT_PATH.fullmatch(path) is not None,
                "sub-agent activity identity differs")
        require(isinstance(kind, str) and kind in {"started", "completed"}, "prohibited sub-agent activity")
        signature = {"kind": kind, "child": child, "path": path}
        pair = self.activity_pairs.setdefault(identifier, {})
        phase = "started" if method == "item/started" else "completed"
        require(phase not in pair and all(value == signature for value in pair.values()), "sub-agent activity pair differs")
        pair[phase] = signature
        if phase != "completed": return None
        require(set(pair) == {"started", "completed"}, "sub-agent activity completion preceded start")
        if kind == "started":
            require(self.child_token is not None and self.spawn_count == 0, "extra child refused")
            self.spawn_count = 1; self.spawn_call_id = identifier; self.child_thread = child
            self.native_selector_path = path; self.native_selector = path.rsplit("/", 1)[-1]
            self.add("actual-v2-spawn-activity", childThreadId=child, nativeAgent=self.native_selector)
            return child
        require(child == self.child_thread and path == self.native_selector_path and identifier.startswith("subagent-completed-"),
                "completed child activity differs")
        self.completion_activity = True; self.completion_activity_id = identifier; self.add("actual-v2-completed-activity")
        return None

    def acknowledge(self, thread_id: str, text: str) -> None:
        if thread_id == self.child_thread:
            self.child_ack_notifications += 1; require(self.child_ack_notifications == 1, "extra child acknowledgement refused")
            require(text == "NATIVE-CHILD-ACK", "child acknowledgement differs"); self.child_ack = True
        elif thread_id == self.parent_thread:
            self.parent_ack_notifications += 1; require(self.parent_ack_notifications == 1, "extra parent acknowledgement refused")
            require(text == "NATIVE-PARENT-ACK", "parent acknowledgement differs"); self.parent_ack = True
        else:
            raise Refusal("agent acknowledgement preceded a bound thread identity")

    def bind_child(self, thread: dict) -> str:
        require(thread.get("id") == self.child_thread and thread.get("parentThreadId") == self.parent_thread, "child thread parent differs")
        source = thread.get("source", {}).get("subAgent", {}).get("thread_spawn", {})
        selector_path = source.get("agent_path")
        require(source.get("parent_thread_id") == self.parent_thread and isinstance(selector_path, str)
                and len(selector_path) <= 512
                and re.fullmatch(r"/[A-Za-z0-9_-]{1,128}(?:/[A-Za-z0-9_-]{1,128}){0,7}", selector_path), "native agent selector differs")
        require(self.protocol_mode != "v2" or selector_path == self.native_selector_path, "activity/source agent path differs")
        selector = selector_path.rsplit("/", 1)[-1]
        require(re.fullmatch(r"[A-Za-z0-9_-]{1,128}", selector) is not None and selector != self.child_thread,
                "native selector conflated with child UUID")
        self.native_selector_path = selector_path; self.native_selector = selector
        self.add("child-source-bound", childThreadId=self.child_thread, nativeAgent=selector, nativeAgentPath=selector_path)
        return selector

    def parent_turn_started(self, turn_id: str) -> None:
        require(self.parent_thread is not None and self.parent_turn_id is None
                and isinstance(turn_id, str) and 0 < len(turn_id) <= 128, "parent turn identity differs")
        self.parent_turn_id = turn_id
        self.add("parent-turn-started", turnId=turn_id)

    def terminal(self, thread_id: str, turn: dict) -> None:
        require(isinstance(turn, dict) and turn.get("status") == "completed"
                and isinstance(turn.get("id"), str) and 1 <= len(turn["id"]) <= 128, "native turn did not complete")
        if thread_id == self.child_thread:
            self.child_terminal_notifications += 1; require(self.child_terminal_notifications == 1, "extra terminal child turn refused")
            if self.child_terminal:
                require(turn.get("id") == self.child_turn_id, "extra terminal child turn refused")
                self.child_terminal_evidence = "wait-history+event"
            else:
                self.child_terminal = True; self.child_turn_id = turn.get("id"); self.child_terminal_evidence = "event"
                self.add("child-terminal", turnId=self.child_turn_id)
        elif thread_id == self.parent_thread:
            require(not self.parent_terminal and self.parent_turn_id is not None and turn.get("id") == self.parent_turn_id,
                    "parent terminal turn identity differs")
            self.parent_terminal = True; self.add("parent-terminal", turnId=self.parent_turn_id)
        else:
            raise Refusal("terminal event preceded a bound thread identity")

    def verify_turn_history(self, relation: str, thread: dict) -> None:
        expected_thread = self.child_thread if relation == "child" else self.parent_thread
        expected_turn = self.child_turn_id if relation == "child" else self.parent_turn_id
        turns = thread.get("turns")
        require(thread.get("id") == expected_thread and isinstance(turns, list) and len(turns) == 1
                and turns[0].get("status") == "completed", f"{relation} turn history differs")
        turn = turns[0]
        if relation == "child" and expected_turn is None:
            self.child_turn_id = turn.get("id"); self.child_terminal = True; expected_turn = self.child_turn_id
            self.child_terminal_evidence = "wait-history"
            self.add("child-terminal-from-authoritative-history", turnId=self.child_turn_id)
        require(isinstance(expected_turn, str) and turn.get("id") == expected_turn, f"{relation} turn history differs")
        items = turn.get("items"); require(isinstance(items, list) and 1 <= len(items) <= 32, f"{relation} item history differs")
        item_ids = [item.get("id") for item in items if isinstance(item, dict)]
        require(all(isinstance(identifier, str) and 0 < len(identifier) <= 128 for identifier in item_ids)
                and len(item_ids) == len(set(item_ids)), f"{relation} item history differs")
        permitted = ({"userMessage", "reasoning", "agentMessage", "collabAgentToolCall", "subAgentActivity"}
                     if relation == "root" else {"userMessage", "reasoning", "agentMessage"})
        require(all(isinstance(item, dict) and isinstance(item.get("type"), str) and item.get("type") in permitted for item in items),
                f"{relation} item history differs")
        messages = [item for item in items if item.get("type") == "agentMessage"]
        require(len(messages) == 1 and messages[0].get("text") == ("NATIVE-CHILD-ACK" if relation == "child" else "NATIVE-PARENT-ACK"),
                f"{relation} acknowledgement history differs")
        if relation == "child":
            prohibited = sum(item.get("type") not in {"userMessage", "reasoning", "agentMessage"} for item in items)
            require(prohibited == 0, "child collaboration history refused")
            require(self.child_ack or not self.child_turn_readback, "child notification/history acknowledgement differs")
            self.child_ack = True; self.child_turn_readback = True
            self.history_child_ack = len(messages); self.history_prohibited_tools = prohibited
        else:
            collab = [item for item in items if item.get("type") == "collabAgentToolCall"]
            spawn = [item for item in collab if item.get("tool") == "spawnAgent"]
            waits = [item for item in collab if item.get("tool") == "wait"]
            prohibited = [item for item in collab if item.get("tool") not in {"spawnAgent", "wait"}]
            followups = [item for item in collab if item.get("tool") == "followupTask"]
            if self.protocol_mode == "v2":
                activities = [item for item in items if item.get("type") == "subAgentActivity"]
                require(not spawn and len(collab) == 1 and not prohibited and len(waits) == self.wait_count == 1
                        and len(activities) == 2 and all(isinstance(item.get("kind"), str) for item in activities)
                        and {item["kind"] for item in activities} == {"started", "completed"}
                        and all(item.get("agentThreadId") == self.child_thread and item.get("agentPath") == self.native_selector_path for item in activities)
                        and waits[0].get("status") == "completed" and waits[0].get("senderThreadId") == self.parent_thread
                        and waits[0].get("receiverThreadIds") == [] and waits[0].get("agentsStates") == {}
                        and waits[0].get("id") == self.wait_call_id,
                        "root v2 collaboration history differs")
                for activity in activities:
                    closed(activity, {"type", "id", "kind", "agentThreadId", "agentPath"}, "history sub-agent activity")
                    pair = self.activity_pairs.get(activity["id"])
                    signature = {"kind": activity["kind"], "child": activity["agentThreadId"], "path": activity["agentPath"]}
                    require(isinstance(pair, dict) and set(pair) == {"started", "completed"}
                            and all(value == signature for value in pair.values()), "activity history/live disagreement")
                spawn = [activities[0]]
            else:
                require(len(collab) == 2 and not prohibited and len(spawn) == self.spawn_count == 1
                        and len(waits) == self.wait_count == 1,
                        "root collaboration history differs")
                require(spawn[0].get("status") == "completed" and spawn[0].get("senderThreadId") == self.parent_thread
                        and spawn[0].get("receiverThreadIds") == [self.child_thread]
                        and spawn[0].get("model") == "gpt-5.6-sol" and spawn[0].get("reasoningEffort") == "medium"
                        and spawn[0].get("prompt") == FIXED_CHILD_PROMPT
                        and waits[0].get("status") == "completed" and waits[0].get("senderThreadId") == self.parent_thread
                        and waits[0].get("receiverThreadIds") == [self.child_thread]
                        and waits[0].get("agentsStates", {}).get(self.child_thread, {}).get("status") == "completed",
                        "root collaboration history differs")
            require(self.parent_ack, "parent notification/history acknowledgement differs")
            self.parent_turn_readback = True
            self.history_parent_spawn = len(spawn); self.history_parent_wait = len(waits); self.history_parent_ack = len(messages)
            self.history_prohibited_tools = (self.history_prohibited_tools or 0) + len(prohibited)
            self.history_followups = len(followups)
        self.add(f"{relation}-single-turn-readback")

    def finish_result(self, relation: str, result: dict) -> None:
        require(result.get("schema") == "fsgg.telemetry.roadmap-dispatch/1" and result.get("status") == "terminal"
                and result.get("outcome") == "completed", f"{relation} terminal receipt differs")
        require(self.child_terminal if relation == "child" else self.parent_terminal, f"{relation} finish preceded actual terminal")
        self.add(f"{relation}-finish-applied")

    def result(self) -> dict:
        self.validate_complete()
        return {"schema": "fsgg.telemetry.native-operation-result/1", "status": "qualified", "operationId": self.operation_id,
                "runNonce": self.nonce, "parentThreadId": self.parent_thread, "childThreadId": self.child_thread,
                "nativeAgent": self.native_selector, "nativeAgentPath": self.native_selector_path,
                "spawnCount": self.spawn_count, "childTerminalTurns": 1,
                "protocolMode": self.protocol_mode,
                "originalRolloutAudit": "bounded-complete" if self.original_audit else "legacy-public-history",
                "waitCount": self.wait_count, "followups": self.history_followups,
                "automaticRetries": self.history_parent_spawn - self.spawn_count,
                "childTerminalEvidence": self.child_terminal_evidence,
                "observedNotifications": {"childAcknowledgements": self.child_ack_notifications,
                                          "parentAcknowledgements": self.parent_ack_notifications,
                                          "childTerminal": self.child_terminal_notifications},
                "authoritativeHistory": {"parentSpawn": self.history_parent_spawn, "parentWait": self.history_parent_wait,
                                         "parentAcknowledgements": self.history_parent_ack,
                                         "childAcknowledgements": self.history_child_ack,
                                         "prohibitedTools": self.history_prohibited_tools,
                                         "followups": self.history_followups},
                "events": self.events}

    def validate_complete(self) -> None:
        require(self.spawn_count == 1 and self.wait_count == 1 and self.native_selector and self.child_terminal and self.parent_terminal
                and self.child_ack and self.parent_ack and self.child_turn_readback and self.parent_turn_readback
                and (self.protocol_mode != "v2" or self.original_audit)
                and (self.protocol_mode != "v2" or (self.completion_activity
                     and self.completion_activity_id == "subagent-completed-" + self.child_turn_id))
                and (self.history_parent_spawn, self.history_parent_wait, self.history_parent_ack,
                     self.history_child_ack, self.history_prohibited_tools, self.history_followups) == (1, 1, 1, 1, 0, 0),
                "native operation incomplete")


class JsonLineAppServer:
    def __init__(self, command: list[str], environment: dict[str, str], cwd: pathlib.Path, timeout: int, maximum_line: int):
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                        cwd=cwd, env=environment, text=False, start_new_session=True, bufsize=0)
        self.timeout, self.maximum_line, self.next_id, self.notifications = timeout, maximum_line, 1, []
        self.stdout_buffer, self.stderr_buffer = bytearray(), bytearray()
        self.stdout_total = self.stderr_total = 0
        self.maximum_stdout, self.maximum_stderr = maximum_line * 16, maximum_line
        self.stderr_open = True
        os.set_blocking(self.process.stdout.fileno(), False)
        os.set_blocking(self.process.stderr.fileno(), False)

    def close(self) -> None:
        if self.process.poll() is None:
            os.killpg(self.process.pid, signal.SIGTERM)
            try: self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(self.process.pid, signal.SIGKILL); self.process.wait(timeout=5)
        for stream in (self.process.stdin, self.process.stdout, self.process.stderr):
            if stream and not stream.closed: stream.close()

    def shutdown_and_verify_persistence(self, deadline: float) -> dict:
        require(self.process.stdin is not None and not self.process.stdin.closed, "app-server stdin unavailable")
        self.process.stdin.close(); stdout_open = True
        while self.process.poll() is None or stdout_open or self.stderr_open:
            remaining = deadline - time.monotonic(); require(remaining > 0, "app-server shutdown timeout")
            streams = ([] if not stdout_open else [self.process.stdout]) + ([] if not self.stderr_open else [self.process.stderr])
            if not streams:
                self.process.wait(timeout=remaining); break
            ready, _, _ = select.select(streams, [], [], min(remaining, 0.25))
            for stream in ready:
                chunk = os.read(stream.fileno(), 65536)
                if stream is self.process.stdout:
                    if chunk:
                        self.stdout_total += len(chunk); require(self.stdout_total <= self.maximum_stdout, "app-server stdout capacity exceeded")
                        self.stdout_buffer.extend(chunk)
                    else: stdout_open = False
                elif chunk:
                    self.stderr_total += len(chunk); require(self.stderr_total <= self.maximum_stderr, "app-server stderr capacity exceeded")
                    self.stderr_buffer.extend(chunk)
                else: self.stderr_open = False
        require(self.process.returncode == 0 and not self.stdout_buffer, "app-server graceful shutdown differs")
        try: health = self.stderr_buffer.decode("utf-8")
        except UnicodeDecodeError as error: raise Refusal("app-server stderr health unreadable") from error
        require(not self.stderr_buffer or self.stderr_buffer.endswith(b"\n"), "app-server stderr health truncated")
        require(health == "", "unknown app-server persistence health stderr refused")
        return {"bytes": len(self.stderr_buffer), "sha256": hashlib.sha256(self.stderr_buffer).hexdigest(),
                "knownPersistenceErrors": 0, "serverExit": 0}

    def send(self, method: str, params: dict) -> int:
        identifier = self.next_id; self.next_id += 1
        payload = canonical_bytes({"jsonrpc": "2.0", "id": identifier, "method": method, "params": params})
        require(len(payload) <= self.maximum_line, "app-server request exceeded bound")
        self.process.stdin.write(payload); self.process.stdin.flush()
        return identifier

    def notify(self, method: str, params: dict | None = None) -> None:
        payload = canonical_bytes({"jsonrpc": "2.0", "method": method, "params": params or {}})
        self.process.stdin.write(payload); self.process.stdin.flush()

    def read(self, deadline: float) -> dict:
        require(deadline - time.monotonic() > 0, "app-server timeout")
        while b"\n" not in self.stdout_buffer:
            remaining = deadline - time.monotonic(); require(remaining > 0, "app-server timeout")
            streams = [self.process.stdout] + ([self.process.stderr] if self.stderr_open else [])
            ready, _, _ = select.select(streams, [], [], remaining)
            require(bool(ready), "app-server timeout")
            for stream in ready:
                chunk = os.read(stream.fileno(), 65536)
                if stream is self.process.stdout:
                    require(bool(chunk), "app-server stdout closed before a complete message")
                    self.stdout_total += len(chunk); require(self.stdout_total <= self.maximum_stdout, "app-server stdout capacity exceeded")
                    self.stdout_buffer.extend(chunk)
                    newline = self.stdout_buffer.find(b"\n")
                    require(newline < 0 or newline + 1 <= self.maximum_line, "app-server protocol line refused")
                    require(newline >= 0 or len(self.stdout_buffer) <= self.maximum_line, "app-server protocol line refused")
                elif chunk:
                    self.stderr_total += len(chunk); require(self.stderr_total <= self.maximum_stderr, "app-server stderr capacity exceeded")
                    self.stderr_buffer.extend(chunk)
                else:
                    self.stderr_open = False
        newline = self.stdout_buffer.index(b"\n")
        line = bytes(self.stdout_buffer[:newline + 1]); del self.stdout_buffer[:newline + 1]
        require(len(line) <= self.maximum_line, "app-server protocol line refused")
        value = json.loads(line)
        require(isinstance(value, dict), "app-server message shape differs")
        return value

    def response(self, identifier: int, deadline: float) -> dict:
        while True:
            value = self.read(deadline)
            if value.get("id") == identifier:
                require("error" not in value and isinstance(value.get("result"), dict), "app-server request refused")
                return value["result"]
            require("method" in value, "unmatched app-server response")
            self.notifications.append(value); require(len(self.notifications) <= 4096, "app-server notification capacity exceeded")

    def request(self, method: str, params: dict, deadline: float) -> dict:
        return self.response(self.send(method, params), deadline)


def telemetry(engine: pathlib.Path, config: pathlib.Path, environment: dict[str, str], arguments: list[str], timeout: int = 30) -> dict:
    completed = subprocess.run([str(engine), "skill", "roadmap-telemetry", "--config", str(config), *arguments],
                               env=environment, capture_output=True, timeout=timeout, check=False)
    require(len(completed.stdout) <= 64 * 1024 and len(completed.stderr) <= 64 * 1024, "telemetry response exceeded bound")
    require(completed.returncode == 0 and not completed.stderr, "telemetry operation refused")
    value = json.loads(completed.stdout)
    require(isinstance(value, dict), "telemetry response shape differs")
    return value
