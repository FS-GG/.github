#!/usr/bin/env python3
"""Run the single reviewed V2-HOST native collaboration operation."""

from __future__ import annotations

import argparse
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


def readonly_source_compatibility(profile: dict, nonce: str) -> dict:
    native = pathlib.Path(profile["native"]["executable"]); config = pathlib.Path(profile["native"]["config"])
    regular(native, 256 * 1024 * 1024, True); require(sha256(native) == profile["native"]["sha256"], "native executable digest differs")
    environment = clean_environment(profile); cwd = pathlib.Path(profile["runtime"]["cwd"]); private_directory(cwd)
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
    regular(native, 256 * 1024 * 1024, True); require(sha256(native) == profile["native"]["sha256"], "native executable digest differs")
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
        root = evidence.begin("root", command_result(engine, telemetry_config, telemetry_environment,
                              begin_arguments(profile, profile["producer"]["rootAttempt"], nonce)))
        root_started = command_result(engine, telemetry_config, telemetry_environment, ["started", "--token", root, "--native-id", parent])
        require(root_started.get("status") == "started", "root start was not applied"); receipt_rows.append(root_started)
        child = evidence.begin("child", command_result(engine, telemetry_config, telemetry_environment,
                               begin_arguments(profile, profile["producer"]["childAttempt"], nonce, root)))
        turn = server.request("turn/start", {"threadId": parent, "model": profile["model"], "effort": profile["effort"],
                              "approvalPolicy": "never", "input": [{"type": "text", "text": profile["prompt"]}]}, deadline)
        parent_turn = turn.get("turn", {}).get("id"); require(isinstance(parent_turn, str), "parent turn identity differs")
        pending = list(server.notifications); server.notifications.clear(); event_count = 0; child_finished = False
        while not (evidence.parent_terminal and child_finished):
            value = pending.pop(0) if pending else (server.notifications.pop(0) if server.notifications else server.read(deadline)); event_count += 1
            require(event_count <= profile["runtime"]["maximumEvents"], "app-server event capacity exceeded")
            method, params = value.get("method"), value.get("params", {})
            if method in {"item/started", "item/completed"}:
                item = params.get("item", {})
                require(item.get("type") in {"userMessage", "agentMessage", "reasoning", "collabAgentToolCall"}, "prohibited or unknown native item observed")
                if item.get("type") == "collabAgentToolCall" and method == "item/completed":
                    child_thread = evidence.collaboration(item)
                    if child_thread:
                        child_read = server.request("thread/read", {"threadId": child_thread, "includeTurns": False}, deadline)
                        selector = evidence.bind_child(child_read.get("thread", {}))
                        started = command_result(engine, telemetry_config, telemetry_environment, ["started", "--token", child, "--native-id", selector])
                        require(started.get("status") == "started", "child start was not applied"); receipt_rows.append(started)
                if item.get("type") == "agentMessage" and method == "item/completed":
                    evidence.acknowledge(params.get("threadId"), item.get("text"))
            elif method == "turn/completed":
                evidence.terminal(params.get("threadId"), params.get("turn", {}))
                if params.get("threadId") == evidence.child_thread and not child_finished:
                    finished = command_result(engine, telemetry_config, telemetry_environment, ["finish", "--token", child, "--outcome", "completed"])
                    evidence.finish_result("child", finished); receipt_rows.append(finished); child_finished = True
            elif method in {"turn/started", "item/agentMessage/delta", "item/started", "thread/status/changed", "thread/tokenUsage/updated"}:
                pass
            elif "id" in value:
                raise Refusal("unexpected app-server response")
        child_history = server.request("thread/read", {"threadId": evidence.child_thread, "includeTurns": True}, deadline)
        evidence.verify_turn_history("child", child_history.get("thread", {}))
        parent_history = server.request("thread/read", {"threadId": evidence.parent_thread, "includeTurns": True}, deadline)
        evidence.verify_turn_history("root", parent_history.get("thread", {}))
        root_finished = command_result(engine, telemetry_config, telemetry_environment, ["finish", "--token", root, "--outcome", "completed"])
        evidence.finish_result("root", root_finished); receipt_rows.append(root_finished)
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
                  else readonly_source_compatibility(profile, args.run_nonce))
        private_write(run_root / "result.json", canonical_bytes(result))
        print(json.dumps({"schema": result["schema"], "status": result["status"], "resultSha256": sha256(run_root / "result.json")}, separators=(",", ":")))
        return 0
    except (Refusal, OSError, ValueError, json.JSONDecodeError) as error:
        print(json.dumps({"schema": "fsgg.telemetry.native-operation-error/1", "code": "qualification-refused", "message": str(error)}, separators=(",", ":")), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
