#!/usr/bin/env python3
"""Run the single reviewed V2-HOST native collaboration operation."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import re
import stat
import subprocess
import sys
import time
import tomllib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from native_producer_support import (JsonLineAppServer, OperationEvidence, Refusal, canonical_bytes,
                                     clean_environment, load_profile, producer_environment, regular, require, sha256, telemetry)


NATIVE_ELF_MAXIMUM = 320 * 1024 * 1024


def pinned_native(path: pathlib.Path, profile: dict) -> None:
    regular(path, NATIVE_ELF_MAXIMUM, True)
    require(path.stat().st_size == profile["native"]["bytes"], "native executable size differs")
    require(sha256(path) == profile["native"]["sha256"], "native executable digest differs")


def private_directory(path: pathlib.Path, create: bool = False) -> None:
    require(path.is_absolute() and path == pathlib.Path(os.path.normpath(path)), "normalized absolute directory required")
    if create: os.mkdir(path, 0o700)
    info = path.lstat()
    require(path.is_dir() and not path.is_symlink() and info.st_uid == os.geteuid()
            and info.st_mode & 0o077 == 0, "private directory custody differs")


def private_write(path: pathlib.Path, value: bytes) -> None:
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "wb") as stream:
        stream.write(value); stream.flush(); os.fsync(stream.fileno())


def workspace_config_value(value: object, profile: dict) -> None:
    require(isinstance(value, dict), "telemetry workspace config must be an object")
    producer = profile["producer"]
    destination = {"kind": "remote", "endpoint": producer["receiverOrigin"],
                   "credentialReference": producer["credentialReference"], "spoolRoot": producer["spoolRoot"]}
    association = {"workspaceId": producer["workspaceId"], "producerId": producer["producerId"],
                   "streamId": producer["streamId"], "repositories": [producer["repository"]], "destination": destination}
    expected = {"schema": "fsgg.telemetry.workspace-config/1", "engine": "fsgg-coord-engine",
                "associations": [association], "retiredAssociations": []}
    require(value == expected, "telemetry workspace config differs from fixed receiver binding")


def private_workspace_config(path: pathlib.Path, profile: dict) -> None:
    require(path.is_absolute() and path == pathlib.Path(os.path.normpath(path)), "normalized absolute telemetry config required")
    for ancestor in (path, *path.parents[:-1]):
        info = ancestor.lstat()
        require(not ancestor.is_symlink(), f"telemetry config symlink ancestry refused: {ancestor}")
        if ancestor == path:
            require(stat.S_ISREG(info.st_mode) and info.st_uid == os.geteuid() and info.st_mode & 0o777 == 0o600,
                    "telemetry config must be owner-private 0600")
            require(0 < info.st_size <= 64 * 1024, "telemetry config size refused")
        else:
            require(stat.S_ISDIR(info.st_mode) and info.st_mode & 0o022 == 0,
                    f"unsafe telemetry config ancestor refused: {ancestor}")
    workspace_config_value(json.loads(path.read_text(encoding="utf-8")), profile)


def toml_argument(value: object) -> str:
    if isinstance(value, bool): return "true" if value else "false"
    if isinstance(value, int): return str(value)
    if isinstance(value, str): return json.dumps(value)
    raise Refusal("unsupported native config value")


def config_arguments(path: pathlib.Path, expected_sha: str) -> list[str]:
    regular(path, 64 * 1024); require(sha256(path) == expected_sha, "native config digest differs")
    value = tomllib.loads(path.read_text(encoding="utf-8"))
    require(set(value) == {"model", "model_provider", "model_reasoning_effort", "approval_policy", "sandbox_mode", "web_search", "agents", "features"}, "native config keys differ")
    require(set(value["agents"]) == {"enabled", "max_depth", "max_threads"}, "agent config keys differ")
    permitted_features = {"apps", "auth_elicitation", "browser_use", "browser_use_external", "browser_use_full_cdp_access", "code_mode_host",
                          "computer_use", "daemon_auto_start", "enable_mcp_apps", "goals", "guardian_approval", "hooks", "image_generation",
                          "in_app_browser", "in_app_chat", "in_app_dictation", "in_app_local_automation", "in_app_updates", "multi_agent", "multi_agent_v2",
                          "plugin_sharing", "plugins", "realtime_conversation", "remote_plugin", "shell_tool", "shell_snapshot", "skill_mcp_dependency_install",
                          "skill_search", "sleep_tool", "standalone_web_search", "system_proxy_fallback", "tool_call_mcp_elicitation", "tool_suggest",
                          "unbounded_connection_retries", "unified_exec", "unified_exec_tty", "unified_exec_zsh_fork", "view_image", "web_search_cached",
                          "web_search_request", "workspace_dependencies", "worktrees", "write_stdin_approval"}
    require(set(value["features"]) == permitted_features and value["features"]["multi_agent"] is True
            and all(not selected for name, selected in value["features"].items() if name != "multi_agent"), "tool feature config differs")
    result = []
    for key in sorted(k for k in value if k not in {"agents", "features"}): result += ["-c", f"{key}={toml_argument(value[key])}"]
    for table in ("agents", "features"):
        for key in sorted(value[table]): result += ["-c", f"{table}.{key}={toml_argument(value[table][key])}"]
    return result


def command_result(engine: pathlib.Path, config: pathlib.Path, environment: dict[str, str], args: list[str]) -> dict:
    return telemetry(engine, config, environment, args)


def engine_json(engine: pathlib.Path, environment: dict[str, str], arguments: list[str], deadline: float) -> dict:
    remaining = deadline - time.monotonic(); require(remaining > 0, "telemetry operation timeout")
    completed = subprocess.run([str(engine), *arguments], env=environment, capture_output=True,
                               timeout=min(30, remaining), check=False)
    require(len(completed.stdout) <= 128 * 1024 and len(completed.stderr) <= 128 * 1024,
            "telemetry operation output exceeded bound")
    require(completed.returncode == 0 and not completed.stderr, "telemetry operation refused")
    value = json.loads(completed.stdout)
    require(isinstance(value, dict), "telemetry operation result differs")
    return value


def workspace_binding(engine: pathlib.Path, config: pathlib.Path, environment: dict[str, str], profile: dict,
                      deadline: float) -> dict:
    producer = profile["producer"]
    value = engine_json(engine, environment,
                        ["telemetry", "workspace", "binding", "--config", str(config),
                         "--repository", producer["repository"]], deadline)
    require(set(value) == {"schema", "configPath", "repository", "producerId", "bindingDigest", "destination", "privateStateRoot"}
            and value["schema"] == "fsgg.telemetry.workspace-binding/1"
            and value["configPath"] == str(config) and value["repository"] == producer["repository"]
            and value["producerId"] == producer["producerId"] and value["destination"] == "remote"
            and isinstance(value["bindingDigest"], str) and re.fullmatch(r"[0-9a-f]{64}", value["bindingDigest"])
            and value["privateStateRoot"] == producer["spoolRoot"], "telemetry workspace binding differs")
    return value


def applied_receipt_value(state: object, outcome: object, token: str, producer_id: str) -> dict:
    require(isinstance(state, dict) and state.get("schema") == "fsgg.telemetry.roadmap-dispatch-state/1"
            and state.get("token") == token and state.get("associationProducer") == producer_id
            and "pendingPublication" not in state, "telemetry dispatch state is not settled")
    invocation, sequence = state.get("invocationId"), state.get("sequence")
    require(isinstance(invocation, str) and re.fullmatch(r"[0-9a-f]{32}", invocation)
            and isinstance(sequence, int) and 1 <= sequence <= 64, "telemetry dispatch publication identity differs")
    batch = f"{invocation}-{sequence:06d}"
    require(isinstance(outcome, dict) and set(outcome) == {"schema", "batchId", "digest", "status", "code"}
            and outcome["schema"] == "fsgg.telemetry.workspace-outcome/1" and outcome["batchId"] == batch
            and isinstance(outcome["digest"], str) and re.fullmatch(r"[0-9a-f]{64}", outcome["digest"])
            and outcome["status"] == "applied" and outcome["code"] is None,
            "telemetry receiver did not apply the exact publication")
    return {"batchId": batch, "digest": outcome["digest"], "status": "applied"}


def private_json(path: pathlib.Path, maximum: int, label: str) -> dict:
    for ancestor in path.parents[:-1]:
        info = ancestor.lstat()
        require(stat.S_ISDIR(info.st_mode) and not ancestor.is_symlink() and info.st_mode & 0o022 == 0,
                f"private {label} ancestry differs")
    info = path.lstat()
    require(stat.S_ISREG(info.st_mode) and not path.is_symlink() and info.st_uid == os.geteuid()
            and info.st_mode & 0o777 == 0o600 and 0 < info.st_size <= maximum, f"private {label} custody differs")
    value = json.loads(path.read_text(encoding="utf-8")); require(isinstance(value, dict), f"private {label} shape differs")
    return value


def require_applied(engine: pathlib.Path, config: pathlib.Path, environment: dict[str, str], profile: dict,
                    binding: dict, token: str, deadline: float) -> dict:
    spool = pathlib.Path(binding["privateStateRoot"])
    state_path = spool / "orchestrator-dispatches" / f"{token}.json"
    state = private_json(state_path, 256 * 1024, "dispatch state")
    invocation, sequence = state.get("invocationId"), state.get("sequence")
    require(isinstance(invocation, str) and isinstance(sequence, int), "telemetry dispatch publication identity differs")
    batch = f"{invocation}-{sequence:06d}"
    key = hashlib.sha256((profile["producer"]["producerId"] + "\n" + batch).encode()).hexdigest()
    outcome_path = spool / "outcomes" / f"{key}.json"
    for _ in range(8):
        if outcome_path.exists():
            break
        drain = engine_json(engine, environment,
                            ["telemetry", "workspace", "drain", "--config", str(config),
                             "--repository", profile["producer"]["repository"],
                             "--binding-digest", binding["bindingDigest"]], deadline)
        require(set(drain) == {"schema", "processed", "awaitingApplication"}
                and drain["schema"] == "fsgg.telemetry.workspace-drain/1"
                and isinstance(drain["processed"], int) and 0 <= drain["processed"] <= 16
                and isinstance(drain["awaitingApplication"], int) and 0 <= drain["awaitingApplication"] <= 16,
                "telemetry workspace drain result differs")
        if outcome_path.exists():
            break
        time.sleep(min(0.25, max(0, deadline - time.monotonic())))
    outcome = private_json(outcome_path, 4096, "receiver outcome")
    receipt = applied_receipt_value(state, outcome, token, profile["producer"]["producerId"])
    require(not any(spool.glob("*.ready")), "telemetry spool retained an unsettled publication")
    return receipt


def begin_arguments(profile: dict, attempt: str, nonce: str, parent: str | None = None) -> list[str]:
    producer = profile["producer"]
    arguments = ["begin", "--feature", producer["feature"], "--item", producer["item"], "--attempt", f"{attempt}-{nonce}",
                 "--model", profile["model"], "--effort", profile["effort"], "--original-item", producer["item"]]
    if parent: arguments += ["--parent-token", parent, "--relation", "child", "--parent-attempt", f"{producer['rootAttempt']}-{nonce}"]
    return arguments


def effective_config(value: dict, profile: dict) -> None:
    config = value.get("config", {})
    expected = {"model": profile["model"], "model_provider": profile["provider"], "model_reasoning_effort": profile["effort"],
                "approval_policy": "never", "sandbox_mode": "read-only", "web_search": "disabled"}
    require(all(config.get(key) == item for key, item in expected.items()), "effective native config differs")
    agents = config.get("agents", {})
    require(agents.get("enabled") is True and agents.get("max_depth") == 1 and agents.get("max_concurrent_threads_per_session") == 1
            and all(agents.get(key) is None for key in ("default_subagent_model", "default_subagent_reasoning_effort", "job_max_runtime_seconds")), "effective agent limits differ")
    require(config.get("mcp_servers") == {} and config.get("plugins") == {} and config.get("model_providers") == {}, "external providers must be absent")
    require(all(config.get(key) is None for key in ("apps", "skills", "hooks", "goals", "browser_use", "computer_use", "tools", "openai_base_url")), "effective tool config differs")
    layers = value.get("layers")
    require(isinstance(layers, list) and len(layers) == 3, "effective config layers differ")
    by_type = {row.get("name", {}).get("type"): row for row in layers}
    require(set(by_type) == {"sessionFlags", "user", "system"}, "effective config layer source differs")
    require(by_type["sessionFlags"].get("version") == "sha256:" + profile["native"]["sessionFlagsSha256"], "session flags digest differs")
    empty = "sha256:44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a"
    require(by_type["user"].get("version") == empty and by_type["system"].get("version") == empty, "unreviewed native config layer refused")


def source_readback_result(rows: object, notifications: list[dict], nonce: str) -> dict:
    require(isinstance(rows, list) and len(rows) <= 100, "read-only thread inventory differs")
    require(len(notifications) <= 8 and all(row.get("method") in {"configWarning", "remoteControl/status/changed"}
                                               for row in notifications), "readback unexpectedly emitted lifecycle events")
    summary = [{"sourceKind": "subAgent" if isinstance(row.get("source"), dict) and "subAgent" in row["source"] else row.get("source"),
                "persistent": row.get("ephemeral") is False} for row in rows]
    require(all(row["sourceKind"] in {"cli", "vscode", "exec", "appServer", "unknown", "subAgent"} for row in summary), "readback source kind differs")
    return {"schema": "fsgg.telemetry.native-source-readback/1", "status": "compatible", "operationId": "v2-host-01.8a-readonly-source-compatibility-v1",
            "runNonce": nonce, "threadCount": len(rows), "threads": summary, "threadStarts": 0, "turnStarts": 0}


def buffer_prebind_event(buffer: list[dict], value: dict, parent_thread: str, maximum_bytes: int) -> int:
    method, params = value.get("method"), value.get("params", {})
    thread_id = params.get("threadId") if isinstance(params, dict) else None
    require(method in {"item/started", "item/completed", "item/agentMessage/delta", "turn/started", "turn/completed",
                       "thread/status/changed", "thread/tokenUsage/updated"}
            and isinstance(thread_id, str) and thread_id != parent_thread
            and re.fullmatch(r"[0-9a-f-]{36}", thread_id), "unbound app-server event identity differs")
    require(len(buffer) < 32, "pre-bind child event capacity exceeded")
    size = len(canonical_bytes(value)); require(size <= maximum_bytes, "pre-bind child event capacity exceeded")
    buffer.append(value)
    return maximum_bytes - size


def readonly_source_compatibility(profile: dict, nonce: str, run_root: pathlib.Path) -> dict:
    native = pathlib.Path(profile["native"]["executable"]); config = pathlib.Path(profile["native"]["config"])
    pinned_native(native, profile)
    environment = clean_environment(profile); cwd = pathlib.Path(profile["runtime"]["cwd"]); private_directory(cwd)
    # Codex 0.158 initializes writable SQLite and installation identity state
    # before opening the stdio transport. Keep the source mount read-only and
    # place this probe's empty, disposable home in its bounded output tmpfs.
    environment["CODEX_HOME"] = str(run_root)
    command = [str(native), "app-server", "--strict-config", "--listen", "stdio://", *config_arguments(config, profile["native"]["configSha256"])]
    server = JsonLineAppServer(command, environment, cwd, 30, profile["runtime"]["maximumLineBytes"])
    deadline = time.monotonic() + 30
    try:
        server.request("initialize", {"clientInfo": {"name": "fsgg-native-readback", "title": "FS.GG native readback", "version": "1.0.0"}, "capabilities": {}}, deadline)
        server.notify("initialized")
        effective_config(server.request("config/read", {"cwd": str(cwd), "includeLayers": True}, deadline), profile)
        listed = server.request("thread/list", {"limit": 100, "archived": False, "useStateDbOnly": True}, deadline)
        return source_readback_result(listed.get("data"), server.notifications, nonce)
    finally:
        server.close()


def perform(profile: dict, nonce: str, run_root: pathlib.Path) -> dict:
    native = pathlib.Path(profile["native"]["executable"]); engine = pathlib.Path(profile["producer"]["executable"])
    config = pathlib.Path(profile["native"]["config"]); telemetry_config = pathlib.Path(profile["producer"]["telemetryConfig"])
    pinned_native(native, profile)
    regular(engine, 256 * 1024 * 1024, True); private_workspace_config(telemetry_config, profile)
    private_directory(pathlib.Path(profile["runtime"]["home"])); private_directory(pathlib.Path(profile["runtime"]["codexHome"]))
    cwd = pathlib.Path(profile["runtime"]["cwd"]); private_directory(cwd)
    environment = clean_environment(profile)
    marker = pathlib.Path(profile["producer"]["coherentMarker"]); regular(marker, 128)
    require(marker.read_text(encoding="ascii") == profile["producer"]["coherentPayloadSha256"] + "\n", "producer coherent payload marker differs")
    version = subprocess.run([str(engine), "--version"], env=environment, capture_output=True, text=True, timeout=10, check=False)
    require(version.returncode == 0 and version.stderr == "" and version.stdout == profile["producer"]["executableVersion"] + "\n", "producer executable version differs")
    command = [str(native), "app-server", "--strict-config", "--listen", "stdio://", *config_arguments(config, profile["native"]["configSha256"])]
    evidence = OperationEvidence(profile["operationId"], nonce)
    server = JsonLineAppServer(command, environment, cwd, profile["runtime"]["timeoutSeconds"], profile["runtime"]["maximumLineBytes"])
    deadline = time.monotonic() + profile["runtime"]["timeoutSeconds"]
    receipt_rows = []
    try:
        initialized = server.request("initialize", {"clientInfo": {"name": "fsgg-native-qualification", "title": "FS.GG native qualification", "version": "1.0.0"}, "capabilities": {}}, deadline)
        require(isinstance(initialized, dict), "initialize response differs"); server.notify("initialized")
        effective_config(server.request("config/read", {"cwd": str(cwd), "includeLayers": True}, deadline), profile)
        thread_result = server.request("thread/start", {"cwd": str(cwd), "model": profile["model"], "modelProvider": profile["provider"],
                                "approvalPolicy": "never", "sandbox": "read-only", "ephemeral": False,
                                "baseInstructions": "Perform only the fixed collaboration acknowledgement operation. Do not read files or use non-collaboration tools."}, deadline)
        parent = evidence.thread_started(thread_result)
        telemetry_environment = producer_environment(profile, parent)
        binding = workspace_binding(engine, telemetry_config, telemetry_environment, profile, deadline)
        root = evidence.begin("root", command_result(engine, telemetry_config, telemetry_environment,
                              begin_arguments(profile, profile["producer"]["rootAttempt"], nonce)))
        receipt_rows.append({"operation": "root-begin", **require_applied(engine, telemetry_config, telemetry_environment,
                                                                           profile, binding, root, deadline)})
        root_started = command_result(engine, telemetry_config, telemetry_environment, ["started", "--token", root, "--native-id", parent])
        require(root_started.get("status") == "started", "root start was not applied")
        receipt_rows.append({"operation": "root-started", **require_applied(engine, telemetry_config, telemetry_environment,
                                                                             profile, binding, root, deadline)})
        child = evidence.begin("child", command_result(engine, telemetry_config, telemetry_environment,
                               begin_arguments(profile, profile["producer"]["childAttempt"], nonce, root)))
        receipt_rows.append({"operation": "child-begin", **require_applied(engine, telemetry_config, telemetry_environment,
                                                                            profile, binding, child, deadline)})
        turn = server.request("turn/start", {"threadId": parent, "model": profile["model"], "effort": profile["effort"],
                              "approvalPolicy": "never", "input": [{"type": "text", "text": profile["prompt"]}]}, deadline)
        parent_turn = turn.get("turn", {}).get("id"); evidence.parent_turn_started(parent_turn)
        pending = list(server.notifications); server.notifications.clear(); event_count = 0; child_finished = False
        prebind_events: list[dict] = []; prebind_bytes = 2 * 1024 * 1024
        while not (evidence.parent_terminal and child_finished):
            value = pending.pop(0) if pending else (server.notifications.pop(0) if server.notifications else server.read(deadline)); event_count += 1
            require(event_count <= profile["runtime"]["maximumEvents"], "app-server event capacity exceeded")
            method, params = value.get("method"), value.get("params", {})
            thread_id = params.get("threadId") if isinstance(params, dict) else None
            if evidence.child_thread is None and thread_id not in {None, evidence.parent_thread}:
                prebind_bytes = buffer_prebind_event(prebind_events, value, evidence.parent_thread, prebind_bytes)
                continue
            if method in {"item/started", "item/completed"}:
                item = params.get("item", {})
                require(item.get("type") in {"userMessage", "agentMessage", "reasoning", "collabAgentToolCall"}, "prohibited or unknown native item observed")
                if item.get("type") == "collabAgentToolCall":
                    require(item.get("tool") in {"spawnAgent", "wait"}, "prohibited collaboration operation observed")
                if item.get("type") == "collabAgentToolCall" and method == "item/completed":
                    child_thread = evidence.collaboration(item)
                    if child_thread:
                        child_read = server.request("thread/read", {"threadId": child_thread, "includeTurns": False}, deadline)
                        selector = evidence.bind_child(child_read.get("thread", {}))
                        started = command_result(engine, telemetry_config, telemetry_environment, ["started", "--token", child, "--native-id", selector])
                        require(started.get("status") == "started", "child start was not applied")
                        receipt_rows.append({"operation": "child-started", **require_applied(engine, telemetry_config, telemetry_environment,
                                                                                             profile, binding, child, deadline)})
                        require(all(row.get("params", {}).get("threadId") == child_thread for row in prebind_events),
                                "pre-bind event belongs to an unexpected child")
                        pending = prebind_events + pending; prebind_events = []
                    elif item.get("tool") == "wait":
                        child_history = server.request("thread/read", {"threadId": evidence.child_thread, "includeTurns": True}, deadline)
                        evidence.verify_turn_history("child", child_history.get("thread", {}))
                        if not child_finished:
                            finished = command_result(engine, telemetry_config, telemetry_environment,
                                                      ["finish", "--token", child, "--outcome", "completed"])
                            evidence.finish_result("child", finished)
                            receipt_rows.append({"operation": "child-finished", **require_applied(
                                engine, telemetry_config, telemetry_environment, profile, binding, child, deadline)})
                            child_finished = True
                if item.get("type") == "agentMessage" and method == "item/completed":
                    evidence.acknowledge(params.get("threadId"), item.get("text"))
            elif method == "turn/completed":
                evidence.terminal(params.get("threadId"), params.get("turn", {}))
                if params.get("threadId") == evidence.child_thread and not child_finished:
                    finished = command_result(engine, telemetry_config, telemetry_environment, ["finish", "--token", child, "--outcome", "completed"])
                    evidence.finish_result("child", finished)
                    receipt_rows.append({"operation": "child-finished", **require_applied(engine, telemetry_config, telemetry_environment,
                                                                                           profile, binding, child, deadline)})
                    child_finished = True
            elif method in {"turn/started", "item/agentMessage/delta", "item/started", "thread/status/changed", "thread/tokenUsage/updated"}:
                pass
            elif "id" in value:
                raise Refusal("unexpected app-server response")
        require(not prebind_events, "unresolved pre-bind child events")
        child_history = server.request("thread/read", {"threadId": evidence.child_thread, "includeTurns": True}, deadline)
        evidence.verify_turn_history("child", child_history.get("thread", {}))
        parent_history = server.request("thread/read", {"threadId": evidence.parent_thread, "includeTurns": True}, deadline)
        evidence.verify_turn_history("root", parent_history.get("thread", {}))
        root_finished = command_result(engine, telemetry_config, telemetry_environment, ["finish", "--token", root, "--outcome", "completed"])
        evidence.finish_result("root", root_finished)
        receipt_rows.append({"operation": "root-finished", **require_applied(engine, telemetry_config, telemetry_environment,
                                                                              profile, binding, root, deadline)})
        result = evidence.result()
        private_write(run_root / "telemetry-receipts.json", canonical_bytes({"schema": "fsgg.telemetry.native-operation-receipts/1", "receipts": receipt_rows}))
        private_write(run_root / "protocol.ndjson", b"".join(canonical_bytes(row) for row in evidence.events))
        return result
    finally:
        server.close()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--operation-id", required=True)
    parser.add_argument("--profile", required=True, type=pathlib.Path)
    parser.add_argument("--output-root", required=True, type=pathlib.Path)
    parser.add_argument("--run-nonce", required=True)
    args = parser.parse_args()
    try:
        require(re.fullmatch(r"[a-z0-9][a-z0-9-]{7,63}", args.run_nonce) is not None, "run nonce refused")
        profile = load_profile(args.profile); require(args.operation_id in profile["supportedOperations"], "operation ID refused")
        private_directory(args.output_root)
        run_root = args.output_root / args.run_nonce; require(not run_root.exists() and not run_root.is_symlink(), "run output already exists")
        private_directory(run_root, create=True)
        result = (perform(profile, args.run_nonce, run_root) if args.operation_id == profile["operationId"]
                  else readonly_source_compatibility(profile, args.run_nonce, run_root))
        private_write(run_root / "result.json", canonical_bytes(result))
        print(json.dumps({"schema": result["schema"], "status": result["status"], "resultSha256": sha256(run_root / "result.json")}, separators=(",", ":")))
        return 0
    except (Refusal, OSError, ValueError, json.JSONDecodeError, subprocess.TimeoutExpired) as error:
        print(json.dumps({"schema": "fsgg.telemetry.native-operation-error/1", "code": "qualification-refused", "message": str(error)}, separators=(",", ":")), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
