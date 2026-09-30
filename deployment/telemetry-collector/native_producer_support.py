#!/usr/bin/env python3
"""Closed support primitives for the V2-HOST-01.8 native operation."""

from __future__ import annotations

import hashlib
import json
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


FIXED_PROMPT = "Use the native collaboration spawn tool exactly once. Spawn one child with model gpt-5.6-sol and reasoning effort medium. Give it only this task: return exactly NATIVE-CHILD-ACK; do not read files, use any other tool, contact a service, or spawn another agent. Wait for that child to reach one terminal turn. Do not send a follow-up or retry. Then return exactly NATIVE-PARENT-ACK."


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
    closed(native, {"executable", "sha256", "version", "config", "configSha256", "protocolSchemaSha256", "sessionFlagsSha256"}, "native")
    closed(producer, {"executable", "version", "executableVersion", "coherentPayloadSha256", "coherentMarker", "telemetryConfig",
                      "workspaceId", "producerId", "streamId", "repository", "receiverOrigin", "credentialReference",
                      "credentialEnvironment", "spoolRoot", "feature", "item", "rootAttempt", "childAttempt"}, "producer")
    closed(runtime, {"home", "codexHome", "cwd", "timeoutSeconds", "maximumLineBytes", "maximumEvents", "python"}, "runtime")
    closed(network, {"policySchema", "policyId", "privateNetworkId", "httpsProxy", "noProxy"}, "network")
    require(native == {
        "executable": "/opt/fsgg/codex/codex", "sha256": "167c0148a849d2444f1b5a7fb5f8bb2de1de5ae13a2a504b833fc765980f5cd9",
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
    required = {"PATH": parent.get("PATH", "/usr/local/bin:/usr/bin:/bin"), "HOME": profile["runtime"]["home"],
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
    environment["CODEX_THREAD_ID"] = parent_thread
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
    root_token: str | None = None
    child_token: str | None = None
    child_terminal: bool = False
    parent_terminal: bool = False
    spawn_count: int = 0
    wait_count: int = 0
    child_ack: bool = False
    parent_ack: bool = False
    child_turn_id: str | None = None
    parent_turn_id: str | None = None
    child_turn_readback: bool = False
    parent_turn_readback: bool = False

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

    def spawn(self, item: dict) -> str:
        require(self.child_token is not None, "spawn preceded prospective child begin")
        require(item.get("type") == "collabAgentToolCall" and item.get("tool") == "spawnAgent", "unexpected collaboration operation")
        require(item.get("status") == "completed" and item.get("senderThreadId") == self.parent_thread, "spawn result differs")
        require(item.get("model") == "gpt-5.6-sol" and item.get("reasoningEffort") == "medium", "spawn model profile differs")
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
        require(receivers == [self.child_thread] and item.get("senderThreadId") == self.parent_thread, "collaboration wait identity differs")
        self.add("child-wait-completed")
        return None

    def acknowledge(self, thread_id: str, text: str) -> None:
        if thread_id == self.child_thread:
            require(text == "NATIVE-CHILD-ACK", "child acknowledgement differs"); self.child_ack = True
        elif thread_id == self.parent_thread:
            require(text == "NATIVE-PARENT-ACK", "parent acknowledgement differs"); self.parent_ack = True

    def bind_child(self, thread: dict) -> str:
        require(thread.get("id") == self.child_thread and thread.get("parentThreadId") == self.parent_thread, "child thread parent differs")
        source = thread.get("source", {}).get("subAgent", {}).get("thread_spawn", {})
        selector = source.get("agent_path")
        require(source.get("parent_thread_id") == self.parent_thread and isinstance(selector, str) and re.fullmatch(r"/[a-z0-9_/-]{1,128}", selector), "native agent selector differs")
        require(selector != self.child_thread, "native selector conflated with child UUID")
        self.native_selector = selector; self.add("child-source-bound", childThreadId=self.child_thread, nativeAgent=selector)
        return selector

    def terminal(self, thread_id: str, turn: dict) -> None:
        require(turn.get("status") == "completed", "native turn did not complete")
        if thread_id == self.child_thread:
            require(not self.child_terminal, "extra terminal child turn refused"); self.child_terminal = True; self.child_turn_id = turn.get("id"); self.add("child-terminal", turnId=self.child_turn_id)
        elif thread_id == self.parent_thread:
            require(not self.parent_terminal, "extra terminal parent turn refused"); self.parent_terminal = True; self.parent_turn_id = turn.get("id"); self.add("parent-terminal", turnId=self.parent_turn_id)

    def verify_turn_history(self, relation: str, thread: dict) -> None:
        expected_thread = self.child_thread if relation == "child" else self.parent_thread
        expected_turn = self.child_turn_id if relation == "child" else self.parent_turn_id
        turns = thread.get("turns")
        require(thread.get("id") == expected_thread and isinstance(turns, list) and len(turns) == 1
                and turns[0].get("id") == expected_turn and turns[0].get("status") == "completed", f"{relation} turn history differs")
        if relation == "child": self.child_turn_readback = True
        else: self.parent_turn_readback = True
        self.add(f"{relation}-single-turn-readback")

    def finish_result(self, relation: str, result: dict) -> None:
        require(result.get("schema") == "fsgg.telemetry.roadmap-dispatch/1" and result.get("status") == "terminal"
                and result.get("outcome") == "completed", f"{relation} terminal receipt differs")
        require(self.child_terminal if relation == "child" else self.parent_terminal, f"{relation} finish preceded actual terminal")
        self.add(f"{relation}-finish-applied")

    def result(self) -> dict:
        require(self.spawn_count == 1 and self.wait_count == 1 and self.native_selector and self.child_terminal and self.parent_terminal
                and self.child_ack and self.parent_ack and self.child_turn_readback and self.parent_turn_readback, "native operation incomplete")
        return {"schema": "fsgg.telemetry.native-operation-result/1", "status": "qualified", "operationId": self.operation_id,
                "runNonce": self.nonce, "parentThreadId": self.parent_thread, "childThreadId": self.child_thread,
                "nativeAgent": self.native_selector, "spawnCount": self.spawn_count, "childTerminalTurns": 1,
                "waitCount": self.wait_count, "followups": 0, "automaticRetries": 0, "events": self.events}


class JsonLineAppServer:
    def __init__(self, command: list[str], environment: dict[str, str], cwd: pathlib.Path, timeout: int, maximum_line: int):
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                        cwd=cwd, env=environment, text=False, start_new_session=True)
        self.timeout, self.maximum_line, self.next_id, self.notifications = timeout, maximum_line, 1, []

    def close(self) -> None:
        if self.process.poll() is None:
            os.killpg(self.process.pid, signal.SIGTERM)
            try: self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(self.process.pid, signal.SIGKILL); self.process.wait(timeout=5)
        for stream in (self.process.stdin, self.process.stdout, self.process.stderr):
            if stream: stream.close()

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
        remaining = deadline - time.monotonic(); require(remaining > 0, "app-server timeout")
        ready, _, _ = select.select([self.process.stdout], [], [], remaining)
        require(bool(ready), "app-server timeout")
        line = self.process.stdout.readline(self.maximum_line + 1)
        require(line and len(line) <= self.maximum_line and line.endswith(b"\n"), "app-server protocol line refused")
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
            self.notifications.append(value)

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
