"""Project completed native child usage from the local Codex host.

Only App Server identity/turn metadata and token_usage_record rollout entries are
retained. Conversation items and other rollout entries are never decoded.
"""

from __future__ import annotations

import base64
import hashlib
import json
import os
import pathlib
import re
import select
import stat
import subprocess
import time
from collections import defaultdict
from datetime import datetime, timezone


UUID = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\Z")
COUNTS = ("input_tokens", "cached_input_tokens", "output_tokens", "reasoning_output_tokens", "total_tokens")
TERMINAL_TURN_STATUSES = frozenset({"completed", "failed", "interrupted"})
INVENTORY_PROVENANCE = "codex-app-server-thread-turns-list"
USAGE_PROVENANCE = "codex-native-token-usage-record"
INVENTORY_HOST_SOURCE = "codex-app-server:thread/turns/list"
COLLECTOR_PRODUCER = "fsgg-work-roadmap-native-collector/1"
MAX_EVIDENCE_BYTES = 512 * 1024


class HostUnavailable(Exception):
    """The host cannot prove an exact child identity or final usage."""


def inventory_digest(thread_id: str, inventory: list[dict[str, object]]) -> str:
    roster = [{key: row[key] for key in ("turnId", "turnSequence", "status", "terminal")}
              for row in inventory]
    projection = json.dumps({"threadId": thread_id, "turnInventory": roster}, sort_keys=True,
                            separators=(",", ":"), ensure_ascii=True).encode("ascii")
    return hashlib.sha256(projection).hexdigest()


def exact_evidence_digest(chunks: list[bytes], captured_at: str) -> str:
    digest = hashlib.sha256()
    binding = json.dumps({"collectorProducer": COLLECTOR_PRODUCER,
                          "capturedAt": captured_at, "hostSource": INVENTORY_HOST_SOURCE},
                         sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode("ascii")
    for chunk in [binding, *chunks]:
        digest.update(len(chunk).to_bytes(8, "big"))
        digest.update(chunk)
    return digest.hexdigest()


def counts(value: object) -> dict[str, int]:
    if not isinstance(value, dict):
        raise HostUnavailable("native usage counters are unavailable")
    result = {}
    for key in COUNTS:
        number = value.get(key)
        if not isinstance(number, int) or isinstance(number, bool) or number < 0:
            raise HostUnavailable("native usage counters are malformed")
        result[key] = number
    if result["cached_input_tokens"] > result["input_tokens"] or result["reasoning_output_tokens"] > result["output_tokens"]:
        raise HostUnavailable("native usage subsets exceed their parent counts")
    if result["input_tokens"] + result["output_tokens"] != result["total_tokens"]:
        raise HostUnavailable("native usage total does not equal input plus output")
    return result


class AppServer:
    def __init__(self, command: str = "codex") -> None:
        try:
            self.process = subprocess.Popen(
                [command, "app-server"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                stderr=subprocess.DEVNULL, text=False, bufsize=0,
            )
            self.request(1, "initialize", {
                "clientInfo": {"name": "fsgg_telemetry", "title": "FS-GG Telemetry", "version": "1"},
                "capabilities": {"experimentalApi": True},
            })
            assert self.process.stdin is not None
            self.process.stdin.write(b'{"method":"initialized","params":{}}\n')
            self.process.stdin.flush()
        except (OSError, ValueError, AssertionError, HostUnavailable) as error:
            self.close()
            raise HostUnavailable("Codex App Server is unavailable") from error

    def request_with_evidence(self, request_id: int, method: str,
                              params: dict[str, object]) -> tuple[dict[str, object], bytes]:
        if self.process.stdin is None or self.process.stdout is None:
            raise HostUnavailable("Codex App Server stream is unavailable")
        self.process.stdin.write(json.dumps(
            {"id": request_id, "method": method, "params": params},
            separators=(",", ":")).encode("utf-8") + b"\n")
        self.process.stdin.flush()
        deadline = time.monotonic() + 8
        while time.monotonic() < deadline:
            if not select.select([self.process.stdout], [], [], max(0, deadline - time.monotonic()))[0]:
                break
            line = self.process.stdout.readline()
            if not line or len(line) > 1024 * 1024:
                break
            try:
                response = json.loads(line)
            except (UnicodeError, json.JSONDecodeError):
                continue
            if response.get("id") == request_id:
                result = response.get("result")
                if not isinstance(result, dict) or response.get("error") is not None:
                    raise HostUnavailable("Codex App Server refused a read-only usage request")
                return result, line
        raise HostUnavailable("Codex App Server read timed out")

    def request(self, request_id: int, method: str, params: dict[str, object]) -> dict[str, object]:
        return self.request_with_evidence(request_id, method, params)[0]

    def close(self) -> None:
        process = getattr(self, "process", None)
        if process is None:
            return
        process.terminate()
        try:
            process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=2)

    def __enter__(self) -> "AppServer":
        return self

    def __exit__(self, *_: object) -> None:
        self.close()


def request_with_evidence(server: AppServer, request_id: int, method: str,
                          params: dict[str, object]) -> tuple[dict[str, object], bytes]:
    exact = getattr(server, "request_with_evidence", None)
    if callable(exact):
        return exact(request_id, method, params)
    result = server.request(request_id, method, params)
    # Test doubles use this deterministic wire representation. Production is
    # always AppServer.request_with_evidence and retains the exact line read.
    return result, json.dumps({"id": request_id, "result": result},
                              separators=(",", ":"), ensure_ascii=True).encode("ascii") + b"\n"


def page_with_evidence(server: AppServer, method: str, request_id: int,
                       params: dict[str, object]) -> tuple[list[dict[str, object]], list[dict[str, object]], list[bytes]]:
    rows: list[dict[str, object]] = []
    paging: list[dict[str, object]] = []
    evidence: list[bytes] = []
    cursor: str | None = None
    seen_cursors: set[str] = set()
    for offset in range(10):
        request = dict(params, cursor=cursor, limit=100)
        result, raw = request_with_evidence(server, request_id + offset, method, request)
        evidence.append(raw)
        if sum(map(len, evidence)) > MAX_EVIDENCE_BYTES:
            raise HostUnavailable("native App Server evidence exceeds the bound")
        data = result.get("data")
        if not isinstance(data, list) or any(not isinstance(row, dict) for row in data):
            raise HostUnavailable("Codex App Server returned malformed pagination")
        rows.extend(data)
        if len(rows) > 1000:
            raise HostUnavailable("native child inventory exceeds the bound")
        next_cursor = result.get("nextCursor")
        paging.append({"page": offset + 1, "requestCursor": cursor,
                       "nextCursor": next_cursor, "rowCount": len(data)})
        if next_cursor is None:
            return rows, paging, evidence
        if (not isinstance(next_cursor, str) or not next_cursor or next_cursor == cursor or
                next_cursor in seen_cursors):
            raise HostUnavailable("Codex App Server pagination is invalid")
        seen_cursors.add(next_cursor)
        cursor = next_cursor
    raise HostUnavailable("Codex App Server pagination exceeds the bound")


def page(server: AppServer, method: str, request_id: int,
         params: dict[str, object]) -> list[dict[str, object]]:
    """Retain the original list-returning helper contract for existing callers."""
    return page_with_evidence(server, method, request_id, params)[0]


def _open_directory_chain(path: pathlib.Path) -> int:
    absolute = pathlib.Path(os.path.abspath(path))
    descriptor = os.open("/", os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC)
    try:
        for component in absolute.parts[1:]:
            next_descriptor = os.open(
                component, os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC | os.O_NOFOLLOW,
                dir_fd=descriptor)
            os.close(descriptor)
            descriptor = next_descriptor
        return descriptor
    except (OSError, ValueError):
        os.close(descriptor)
        raise


def _open_rollout(path: str, codex_home: pathlib.Path):
    source = pathlib.Path(os.path.abspath(path))
    sessions = pathlib.Path(os.path.abspath(codex_home / "sessions"))
    try:
        relative = source.absolute().relative_to(sessions)
    except ValueError as error:
        raise HostUnavailable("native usage rollout is unavailable or outside the private host") from error
    if not relative.parts:
        raise HostUnavailable("native usage rollout is unavailable or outside the private host")
    descriptors: list[int] = []
    try:
        current = _open_directory_chain(sessions)
        descriptors.append(current)
        for component in relative.parts[:-1]:
            current = os.open(component, os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC | os.O_NOFOLLOW,
                              dir_fd=current)
            descriptors.append(current)
        file_descriptor = os.open(relative.parts[-1], os.O_RDONLY | os.O_CLOEXEC | os.O_NOFOLLOW,
                                  dir_fd=current)
        opened = os.fstat(file_descriptor)
        if not stat.S_ISREG(opened.st_mode) or opened.st_size > 128 * 1024 * 1024:
            os.close(file_descriptor)
            raise HostUnavailable("native usage rollout is unavailable or outside the private host")
        return os.fdopen(file_descriptor, "rb", closefd=True)
    except (OSError, ValueError) as error:
        raise HostUnavailable("native usage rollout is unavailable or outside the private host") from error
    finally:
        for descriptor in reversed(descriptors):
            os.close(descriptor)


def rollout_usage(path: str, thread_id: str, turn_ids: set[str], codex_home: pathlib.Path
                  ) -> tuple[dict[str, list[dict[str, object]]], list[bytes]]:
    records: dict[str, list[dict[str, object]]] = defaultdict(list)
    evidence: list[bytes] = []
    with _open_rollout(path, codex_home) as stream:
        for line in stream:
            if len(line) > 1024 * 1024 or not re.search(rb'"type"\s*:\s*"token_usage_record"', line[:256]):
                continue
            try:
                row = json.loads(line)
            except (UnicodeError, json.JSONDecodeError):
                raise HostUnavailable("native usage record is malformed") from None
            if row.get("type") != "token_usage_record" or not isinstance(row.get("payload"), dict):
                raise HostUnavailable("native usage record is malformed")
            value = row["payload"]
            turn = value.get("turn_id")
            response = value.get("response_id")
            if value.get("thread_id") != thread_id or turn not in turn_ids or not isinstance(response, str) or not response:
                raise HostUnavailable("native usage record belongs to another thread or turn")
            evidence.append(line)
            if sum(map(len, evidence)) > MAX_EVIDENCE_BYTES:
                raise HostUnavailable("native rollout evidence exceeds the bound")
            records[turn].append({"response": response, "usage": counts(value.get("usage")),
                                  "turnTotal": counts(value.get("turn_token_usage"))})
    return records, evidence


def collect(parent_thread_id: str, native_id: str, *, command: str = "codex",
            codex_home: pathlib.Path | None = None) -> dict[str, object]:
    if not UUID.fullmatch(parent_thread_id) or not re.fullmatch(r"[A-Za-z0-9_-]{1,128}", native_id):
        raise HostUnavailable("native parent or child identity is unavailable")
    home = codex_home or pathlib.Path(os.environ.get("CODEX_HOME", pathlib.Path.home() / ".codex"))
    with AppServer(command) as server:
        children, _, app_evidence = page_with_evidence(
            server, "thread/list", 100, {"parentThreadId": parent_thread_id,
                                         "sourceKinds": ["subAgent", "subAgentThreadSpawn"]})
        matches = []
        for child in children:
            child_id = child.get("id")
            if not isinstance(child_id, str) or not UUID.fullmatch(child_id):
                continue
            thread_result, thread_raw = request_with_evidence(
                server, 200, "thread/read", {"threadId": child_id, "includeTurns": False})
            app_evidence.append(thread_raw)
            if sum(map(len, app_evidence)) > MAX_EVIDENCE_BYTES:
                raise HostUnavailable("native App Server evidence exceeds the bound")
            thread = thread_result.get("thread")
            if not isinstance(thread, dict):
                raise HostUnavailable("native child thread metadata is unavailable")
            spawn = ((thread.get("source") or {}).get("subAgent") or {}).get("thread_spawn")
            if (thread.get("parentThreadId") == parent_thread_id and isinstance(spawn, dict) and
                    spawn.get("parent_thread_id") == parent_thread_id and
                    isinstance(spawn.get("agent_path"), str) and
                    spawn["agent_path"].endswith("/" + native_id)):
                matches.append(thread)
        if len(matches) != 1:
            raise HostUnavailable("native child identity is missing or ambiguous")
        thread = matches[0]
        thread_id = thread["id"]
        turns, inventory_paging, turn_evidence = page_with_evidence(
            server, "thread/turns/list", 300,
            {"threadId": thread_id, "sortDirection": "asc", "itemsView": "notLoaded"})
        app_evidence.extend(turn_evidence)
        if sum(map(len, app_evidence)) > MAX_EVIDENCE_BYTES:
            raise HostUnavailable("native App Server evidence exceeds the bound")
        inventory_captured_at = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    provider = thread.get("modelProvider")
    if provider is not None and (not isinstance(provider, str) or provider != provider.strip() or
                                 not provider or len(provider) > 128):
        raise HostUnavailable("native provider metadata is malformed")
    provider_provenance = "codex-app-server-thread.modelProvider" if provider is not None else None
    model, effort = thread.get("model"), thread.get("reasoningEffort")
    profile_available = all(isinstance(value, str) and value == value.strip() and value and len(value) <= 128
                            for value in (provider, model, effort))
    app_records = [{"sha256": hashlib.sha256(raw).hexdigest(),
                    "bytesBase64": base64.b64encode(raw).decode("ascii")} for raw in app_evidence]
    if not turns:
        inventory = []
        roster_digest = inventory_digest(thread_id, inventory)
        evidence_digest = exact_evidence_digest(app_evidence, inventory_captured_at)
        return {"threadId": thread_id, "turns": [], "allTurnIds": [], "turnInventory": inventory,
                "inventoryProvenance": INVENTORY_PROVENANCE, "usageProvenance": USAGE_PROVENANCE,
                "inventoryHostSource": INVENTORY_HOST_SOURCE, "inventoryPaging": inventory_paging,
                "inventoryCapturedAt": inventory_captured_at, "inventoryRosterDigest": roster_digest,
                "inventorySourceDigest": evidence_digest,
                "complete": False, "provider": provider, "providerProvenance": provider_provenance,
                "model": model, "effort": effort, "collectorProducer": COLLECTOR_PRODUCER,
                "appServerResponses": app_records, "rolloutRecords": []}
    ids = [turn.get("id") for turn in turns]
    if len(set(ids)) != len(ids) or any(not isinstance(value, str) or not UUID.fullmatch(value) for value in ids):
        raise HostUnavailable("native turn identities are malformed")
    records, rollout_evidence = rollout_usage(str(thread.get("path")), thread_id, set(ids), home)
    observations = []
    inventory = []
    complete = profile_available and len(inventory_paging) == 1
    for sequence, turn in enumerate(turns, 1):
        status = turn.get("status")
        if not isinstance(status, str) or not status or len(status) > 64:
            raise HostUnavailable("native turn status is malformed")
        row = {"turnId": turn["id"], "turnSequence": sequence, "status": status,
               "terminal": status in TERMINAL_TURN_STATUSES, "usageAvailable": False}
        inventory.append(row)
        if status not in TERMINAL_TURN_STATUSES:
            complete = False
            continue
        rows = records.get(turn["id"], [])
        if not rows:
            complete = False
            continue
        responses = {}
        for usage_row in rows:
            # A later native record may correct the same response. Its final
            # counters replace the earlier observation, never add to it.
            responses[usage_row["response"]] = usage_row["usage"]
        total = {key: sum(value[key] for value in responses.values()) for key in COUNTS}
        if total != rows[-1]["turnTotal"]:
            complete = False
            continue
        row["usageAvailable"] = True
        observations.append({"turnId": turn["id"], "turnSequence": sequence, "usage": total})
    roster_digest = inventory_digest(thread_id, inventory)
    evidence_digest = exact_evidence_digest(app_evidence + rollout_evidence, inventory_captured_at)
    rollout_records = [{"sha256": hashlib.sha256(raw).hexdigest(),
                        "bytesBase64": base64.b64encode(raw).decode("ascii")} for raw in rollout_evidence]
    return {"threadId": thread_id, "turns": observations, "allTurnIds": ids,
            "turnInventory": inventory, "inventoryProvenance": INVENTORY_PROVENANCE,
            "usageProvenance": USAGE_PROVENANCE, "complete": complete,
            "inventoryHostSource": INVENTORY_HOST_SOURCE, "inventoryPaging": inventory_paging,
            "inventoryCapturedAt": inventory_captured_at, "inventoryRosterDigest": roster_digest,
            "inventorySourceDigest": evidence_digest,
            "provider": provider, "providerProvenance": provider_provenance,
            "model": model, "effort": effort, "collectorProducer": COLLECTOR_PRODUCER,
            "appServerResponses": app_records, "rolloutRecords": rollout_records}
