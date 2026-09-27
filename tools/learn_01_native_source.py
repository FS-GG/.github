"""Bounded native App Server census and token-usage source capture.

The capture retains exact response bytes and selected rollout record bytes in a
private artifact.  It proves only the native census and usage visible at the
capture boundary.  Assignment, whole-original accounting, shared costs, and
comparative qualification are deliberately outside this module.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import pathlib
import re
import select
import stat
import subprocess
import sys
import tempfile
import time
from dataclasses import dataclass
from typing import Any, Protocol

CAPTURE_SCHEMA = "fsgg.learn.native-source-capture/1"
SNAPSHOT_SCHEMA = "fsgg.telemetry.native-source-snapshot/1"
VERIFY_SCHEMA = "fsgg.learn.native-source-verification/1"
POSITIVE_OUTCOME = "native-census-and-usage-reconciled-at-capture"
UUID = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\Z")
TERMINAL = frozenset({"completed", "failed", "interrupted"})
COUNTERS = ("input_tokens", "cached_input_tokens", "output_tokens", "reasoning_output_tokens", "total_tokens")
SOURCE_KINDS = ("subAgent", "subAgentThreadSpawn")
MAX_THREADS = 128
MAX_PAGES = 16
PAGE_LIMIT = 100
MAX_RESPONSE_BYTES = 1024 * 1024
APP_SERVER_READ_TIMEOUT = 8.0
MAX_ROLLOUT_BYTES = 32 * 1024 * 1024
MAX_RECORD_BYTES = 1024 * 1024
MAX_CAPTURE_BYTES = 64 * 1024 * 1024


class NativeSourceError(Exception):
    """The source cannot establish the bounded native observation."""


def _canonical(value: object) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode("ascii")


def _digest(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def _b64(value: bytes) -> str:
    return base64.b64encode(value).decode("ascii")


def _unb64(value: object, label: str, bound: int) -> bytes:
    if not isinstance(value, str) or len(value) > (bound * 4 // 3) + 8:
        raise NativeSourceError(f"{label} bytes are malformed")
    try:
        raw = base64.b64decode(value, validate=True)
    except (ValueError, TypeError) as error:
        raise NativeSourceError(f"{label} bytes are malformed") from error
    if len(raw) > bound:
        raise NativeSourceError(f"{label} exceeds its byte bound")
    return raw


def _loads(raw: bytes, label: str) -> object:
    def closed_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
        value: dict[str, object] = {}
        for key, item in pairs:
            if key in value:
                raise NativeSourceError(f"{label} contains a duplicate JSON key")
            value[key] = item
        return value
    try:
        return json.loads(raw, object_pairs_hook=closed_object)
    except (UnicodeError, json.JSONDecodeError) as error:
        raise NativeSourceError(f"{label} JSON is malformed") from error


def _uuid(value: object, label: str) -> str:
    if not isinstance(value, str) or not UUID.fullmatch(value):
        raise NativeSourceError(f"{label} is malformed")
    return value


@dataclass(frozen=True)
class _Exchange:
    request: bytes
    response: bytes
    result: dict[str, object]


class _Transport(Protocol):
    def request(self, method: str, params: dict[str, object]) -> _Exchange: ...
    def close(self) -> None: ...


class _AppServerTransport:
    """Internal fixed transport; public CLI cannot inject another source."""

    def __init__(self, command: str = "codex") -> None:
        self._next = 1
        self._pending = bytearray()
        try:
            self._process = subprocess.Popen(
                [command, "app-server"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                stderr=subprocess.DEVNULL, bufsize=0,
            )
            self.request("initialize", {
                "clientInfo": {"name": "fsgg_native_source", "title": "FS-GG Native Source", "version": "1"},
                "capabilities": {"experimentalApi": True},
            })
            assert self._process.stdin is not None
            self._process.stdin.write(b'{"method":"initialized","params":{}}\n')
            self._process.stdin.flush()
        except (OSError, AssertionError, NativeSourceError) as error:
            self.close()
            raise NativeSourceError("Codex App Server is unavailable") from error

    def request(self, method: str, params: dict[str, object]) -> _Exchange:
        process = getattr(self, "_process", None)
        if process is None or process.stdin is None or process.stdout is None:
            raise NativeSourceError("Codex App Server stream is unavailable")
        request_id = self._next
        self._next += 1
        request = _canonical({"id": request_id, "method": method, "params": params}) + b"\n"
        process.stdin.write(request)
        process.stdin.flush()
        deadline = time.monotonic() + APP_SERVER_READ_TIMEOUT
        while time.monotonic() < deadline:
            newline = self._pending.find(b"\n")
            if newline < 0:
                if len(self._pending) > MAX_RESPONSE_BYTES:
                    raise NativeSourceError("App Server response is missing or oversized")
                ready = select.select([process.stdout], [], [], max(0.0, deadline - time.monotonic()))[0]
                if not ready:
                    break
                chunk = os.read(process.stdout.fileno(), min(65536, MAX_RESPONSE_BYTES + 1 - len(self._pending)))
                if not chunk:
                    raise NativeSourceError("App Server response is missing or oversized")
                self._pending.extend(chunk)
                continue
            raw = bytes(self._pending[:newline + 1])
            del self._pending[:newline + 1]
            if len(raw) > MAX_RESPONSE_BYTES:
                raise NativeSourceError("App Server response is missing or oversized")
            try:
                envelope = _loads(raw, "App Server response")
            except NativeSourceError as error:
                raise NativeSourceError("App Server returned malformed JSON") from error
            if not isinstance(envelope, dict):
                raise NativeSourceError("App Server returned a non-object message")
            if envelope.get("id") != request_id:
                continue
            result = envelope.get("result")
            if envelope.get("error") is not None or not isinstance(result, dict):
                raise NativeSourceError("App Server refused a fixed read-only request")
            return _Exchange(request, raw, result)
        raise NativeSourceError("App Server read timed out")

    def close(self) -> None:
        process = getattr(self, "_process", None)
        if process is None:
            return
        process.terminate()
        try:
            process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=2)


class _ReplayTransport:
    def __init__(self, rows: object) -> None:
        if not isinstance(rows, list) or len(rows) > MAX_THREADS * (3 * MAX_PAGES + 2) + 1:
            raise NativeSourceError("retained App Server exchange inventory is malformed")
        self._rows = rows
        self._offset = 0

    def request(self, method: str, params: dict[str, object]) -> _Exchange:
        if self._offset >= len(self._rows):
            raise NativeSourceError("retained App Server exchanges are incomplete")
        row = self._rows[self._offset]
        self._offset += 1
        if not isinstance(row, dict) or set(row) != {"method", "requestBytes", "responseBytes", "requestDigest", "responseDigest"}:
            raise NativeSourceError("retained App Server exchange is malformed")
        request = _unb64(row["requestBytes"], "request", MAX_RESPONSE_BYTES)
        response = _unb64(row["responseBytes"], "response", MAX_RESPONSE_BYTES)
        if row["requestDigest"] != _digest(request) or row["responseDigest"] != _digest(response):
            raise NativeSourceError("retained App Server exchange digest disagrees")
        request_value = _loads(request, "retained App Server request")
        response_value = _loads(response, "retained App Server response")
        if (not isinstance(request_value, dict) or set(request_value) != {"id", "method", "params"} or
                request_value.get("method") != method or
                request_value.get("params") != params or row["method"] != method):
            raise NativeSourceError("retained App Server request differs from the fixed request")
        result = response_value.get("result") if isinstance(response_value, dict) else None
        if (not isinstance(request_value.get("id"), int) or
                not isinstance(response_value, dict) or response_value.get("id") != request_value["id"] or
                not isinstance(result, dict) or response_value.get("error") is not None):
            raise NativeSourceError("retained App Server response is malformed")
        return _Exchange(request, response, result)

    def finish(self) -> None:
        if self._offset != len(self._rows):
            raise NativeSourceError("retained App Server exchanges contain trailing calls")

    def close(self) -> None:
        return


def _exchange_row(method: str, exchange: _Exchange) -> dict[str, object]:
    if len(exchange.request) > MAX_RESPONSE_BYTES or len(exchange.response) > MAX_RESPONSE_BYTES:
        raise NativeSourceError("App Server exchange exceeds its byte bound")
    return {
        "method": method,
        "requestBytes": _b64(exchange.request),
        "responseBytes": _b64(exchange.response),
        "requestDigest": _digest(exchange.request),
        "responseDigest": _digest(exchange.response),
    }


def _paged(transport: _Transport, calls: list[dict[str, object]], method: str,
           params: dict[str, object]) -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    cursor: str | None = None
    seen: set[str] = set()
    for _ in range(MAX_PAGES):
        request_params = {**params, "cursor": cursor, "limit": PAGE_LIMIT}
        exchange = transport.request(method, request_params)
        calls.append(_exchange_row(method, exchange))
        data = exchange.result.get("data")
        next_cursor = exchange.result.get("nextCursor")
        if not isinstance(data, list) or len(data) > PAGE_LIMIT or any(not isinstance(row, dict) for row in data):
            raise NativeSourceError(f"{method} returned malformed page data")
        rows.extend(data)
        if len(rows) > MAX_THREADS * PAGE_LIMIT:
            raise NativeSourceError(f"{method} inventory exceeds its bound")
        if next_cursor is None:
            return rows
        if (not isinstance(next_cursor, str) or not next_cursor or len(next_cursor) > 1024 or
                next_cursor == cursor or next_cursor in seen):
            raise NativeSourceError(f"{method} pagination is invalid")
        seen.add(next_cursor)
        cursor = next_cursor
    raise NativeSourceError(f"{method} pagination is truncated")


def _thread_read(transport: _Transport, calls: list[dict[str, object]], thread_id: str) -> dict[str, object]:
    exchange = transport.request("thread/read", {"threadId": thread_id, "includeTurns": False})
    calls.append(_exchange_row("thread/read", exchange))
    thread = exchange.result.get("thread")
    if not isinstance(thread, dict) or thread.get("id") != thread_id:
        raise NativeSourceError("thread/read identity is missing or foreign")
    return thread


def _child_binding(thread: dict[str, object], expected_parent: str) -> tuple[str, str]:
    if thread.get("parentThreadId") != expected_parent:
        raise NativeSourceError("descendant has a foreign parent identity")
    source = thread.get("source")
    subagent = source.get("subAgent") if isinstance(source, dict) else None
    spawn = subagent.get("thread_spawn") if isinstance(subagent, dict) else None
    if (not isinstance(spawn, dict) or spawn.get("parent_thread_id") != expected_parent or
            not isinstance(spawn.get("agent_path"), str) or not spawn["agent_path"].startswith("/root/") or
            len(spawn["agent_path"]) > 512):
        raise NativeSourceError("descendant source binding is unsupported or foreign")
    return "subAgentThreadSpawn", spawn["agent_path"]


def _turn_inventory(transport: _Transport, calls: list[dict[str, object]], thread_id: str) -> list[dict[str, object]]:
    turns = _paged(transport, calls, "thread/turns/list", {
        "threadId": thread_id, "sortDirection": "asc", "itemsView": "notLoaded",
    })
    result: list[dict[str, object]] = []
    seen: set[str] = set()
    for sequence, turn in enumerate(turns, 1):
        turn_id = _uuid(turn.get("id"), "turn id")
        status = turn.get("status")
        if turn_id in seen or not isinstance(status, str) or status not in TERMINAL:
            raise NativeSourceError("turn roster is duplicate, non-terminal, or unsupported")
        seen.add(turn_id)
        result.append({"turnId": turn_id, "turnSequence": sequence, "status": status})
    if not result:
        raise NativeSourceError("thread has no terminal turns")
    return result


def _capture_host(transport: _Transport, root_thread_id: str) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    calls: list[dict[str, object]] = []
    root = _thread_read(transport, calls, root_thread_id)
    pending: list[tuple[str, list[str]]] = [(root_thread_id, [root_thread_id])]
    metadata: dict[str, tuple[dict[str, object], str | None, list[str], bool | None, str]] = {
        root_thread_id: (root, None, [root_thread_id], None, "root")
    }
    order: list[str] = []
    while pending:
        parent_id, parent_chain = pending.pop(0)
        order.append(parent_id)
        found: dict[str, bool] = {}
        for archived in (False, True):
            children = _paged(transport, calls, "thread/list", {
                "parentThreadId": parent_id, "sourceKinds": list(SOURCE_KINDS), "archived": archived,
            })
            for summary in children:
                child_id = _uuid(summary.get("id"), "descendant thread id")
                if child_id in found or child_id in metadata:
                    raise NativeSourceError("descendant appears more than once in the fixed census")
                found[child_id] = archived
                child = _thread_read(transport, calls, child_id)
                source_kind, agent_path = _child_binding(child, parent_id)
                if isinstance(child.get("archived"), bool) and child["archived"] != archived:
                    raise NativeSourceError("descendant archive state disagrees with its census bucket")
                chain = parent_chain + [child_id]
                metadata[child_id] = (child, parent_id, chain, archived, source_kind + ":" + agent_path)
                pending.append((child_id, chain))
                if len(metadata) > MAX_THREADS:
                    raise NativeSourceError("native descendant census exceeds its bound")
    threads: list[dict[str, object]] = []
    for thread_id in order:
        thread, parent_id, chain, archived, source = metadata[thread_id]
        provider = thread.get("modelProvider")
        path = thread.get("path")
        if (not isinstance(provider, str) or not provider.strip() or len(provider) > 128 or
                not isinstance(path, str) or not path or len(path) > 4096):
            raise NativeSourceError("thread provider or rollout source is unavailable")
        threads.append({
            "threadId": thread_id,
            "parentThreadId": parent_id,
            "parentChain": chain,
            "archived": archived,
            "source": source,
            "provider": provider,
            "model": thread.get("model") if isinstance(thread.get("model"), str) else None,
            "effort": thread.get("reasoningEffort") if isinstance(thread.get("reasoningEffort"), str) else None,
            "rolloutPath": path,
            "turns": _turn_inventory(transport, calls, thread_id),
        })
    return threads, calls


def _same_roster(first: list[dict[str, object]], second: list[dict[str, object]]) -> bool:
    def stable(row: dict[str, object]) -> object:
        return {key: row[key] for key in ("threadId", "parentThreadId", "parentChain", "archived", "source", "provider", "model", "effort", "rolloutPath", "turns")}
    return [stable(row) for row in first] == [stable(row) for row in second]


def _open_rollout(path_value: str, codex_home: pathlib.Path) -> tuple[int, str]:
    sessions = (codex_home / "sessions").absolute()
    source = pathlib.Path(path_value)
    if not source.is_absolute():
        raise NativeSourceError("rollout path is not absolute")
    try:
        relative = source.relative_to(sessions)
    except ValueError as error:
        raise NativeSourceError("rollout path is outside the private sessions root") from error
    parts = relative.parts
    if not parts or any(part in ("", ".", "..") for part in parts):
        raise NativeSourceError("rollout path is malformed")
    if not hasattr(os, "O_DIRECTORY") or not hasattr(os, "O_NOFOLLOW"):
        raise NativeSourceError("descriptor-safe rollout access is unsupported on this host")
    flags_dir = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW
    flags_file = os.O_RDONLY | os.O_NOFOLLOW
    opened: list[int] = []
    try:
        current = os.open(sessions, flags_dir)
        opened.append(current)
        for component in parts[:-1]:
            current = os.open(component, flags_dir, dir_fd=current)
            opened.append(current)
        file_fd = os.open(parts[-1], flags_file, dir_fd=current)
        opened.append(file_fd)
        for descriptor in opened[:-1]:
            os.close(descriptor)
        return file_fd, relative.as_posix()
    except OSError as error:
        for descriptor in reversed(opened):
            try: os.close(descriptor)
            except OSError: pass
        raise NativeSourceError("rollout source cannot be opened without following links") from error


def _counters(value: object) -> dict[str, int]:
    if not isinstance(value, dict) or set(value) != set(COUNTERS):
        raise NativeSourceError("native usage counters are malformed")
    result: dict[str, int] = {}
    for key in COUNTERS:
        number = value[key]
        if type(number) is not int or number < 0:
            raise NativeSourceError("native usage counters are malformed")
        result[key] = number
    if (result["cached_input_tokens"] > result["input_tokens"] or
            result["reasoning_output_tokens"] > result["output_tokens"] or
            result["input_tokens"] + result["output_tokens"] != result["total_tokens"]):
        raise NativeSourceError("native usage counters are inconsistent")
    return result


def _scan_rollout(thread: dict[str, object], codex_home: pathlib.Path) -> tuple[dict[str, object], list[dict[str, object]]]:
    thread_id = str(thread["threadId"])
    turn_ids = {str(row["turnId"]) for row in thread["turns"]}
    fd, relative = _open_rollout(str(thread["rolloutPath"]), codex_home)
    try:
        before = os.fstat(fd)
        if not stat.S_ISREG(before.st_mode) or before.st_size > MAX_ROLLOUT_BYTES:
            raise NativeSourceError("rollout source is not a bounded regular file")
        raw = bytearray()
        while len(raw) < before.st_size:
            chunk = os.read(fd, min(1024 * 1024, before.st_size - len(raw)))
            if not chunk:
                break
            raw.extend(chunk)
        after = os.fstat(fd)
        if (len(raw) != before.st_size or before.st_dev != after.st_dev or before.st_ino != after.st_ino or
                before.st_size != after.st_size or before.st_mtime_ns != after.st_mtime_ns):
            raise NativeSourceError("rollout source changed during capture")
    finally:
        os.close(fd)
    evidence: list[dict[str, object]] = []
    offset = 0
    for line in bytes(raw).splitlines(keepends=True):
        length = len(line)
        if length > MAX_RECORD_BYTES:
            raise NativeSourceError("rollout record exceeds its bound")
        if b'"token_usage_record"' in line:
            record = _loads(line, "token usage record")
            payload = record.get("payload") if isinstance(record, dict) else None
            if record.get("type") != "token_usage_record" or not isinstance(payload, dict):
                raise NativeSourceError("token usage record is malformed")
            if payload.get("thread_id") != thread_id or payload.get("turn_id") not in turn_ids:
                raise NativeSourceError("token usage record has a foreign thread or turn identity")
            response_id = payload.get("response_id")
            if not isinstance(response_id, str) or not response_id or len(response_id) > 256:
                raise NativeSourceError("token usage response identity is malformed")
            _counters(payload.get("usage")); _counters(payload.get("turn_token_usage"))
            evidence.append({
                "offset": offset, "length": length, "recordBytes": _b64(line), "recordDigest": _digest(line),
            })
        offset += length
    if offset != len(raw):
        raise NativeSourceError("rollout scan did not reach its fixed bound")
    identity = {
        "threadId": thread_id, "relativePath": relative,
        "device": before.st_dev, "inode": before.st_ino, "size": before.st_size,
        "mtimeNs": before.st_mtime_ns, "scannedBytes": len(raw), "fileDigest": _digest(bytes(raw)),
    }
    return identity, evidence


def _usage_from_records(thread: dict[str, object], records: list[dict[str, object]]) -> list[dict[str, object]]:
    by_turn: dict[str, list[tuple[int, dict[str, object]]]] = {}
    previous_end = 0
    for retained in records:
        if (not isinstance(retained, dict) or set(retained) != {"offset", "length", "recordBytes", "recordDigest"} or
                type(retained["offset"]) is not int or type(retained["length"]) is not int or
                retained["offset"] < previous_end or retained["length"] <= 0):
            raise NativeSourceError("retained token usage record index is malformed")
        raw = _unb64(retained["recordBytes"], "token usage record", MAX_RECORD_BYTES)
        if len(raw) != retained["length"] or retained["recordDigest"] != _digest(raw):
            raise NativeSourceError("retained token usage record digest disagrees")
        record = _loads(raw, "retained token usage record")
        payload = record.get("payload") if isinstance(record, dict) else None
        if (record.get("type") != "token_usage_record" or not isinstance(payload, dict) or
                payload.get("thread_id") != thread["threadId"]):
            raise NativeSourceError("retained token usage record has foreign identity")
        turn_id = payload.get("turn_id"); response_id = payload.get("response_id")
        if (turn_id not in {row["turnId"] for row in thread["turns"]} or
                not isinstance(response_id, str) or not response_id):
            raise NativeSourceError("retained token usage record has foreign turn identity")
        usage = _counters(payload.get("usage")); total = _counters(payload.get("turn_token_usage"))
        by_turn.setdefault(turn_id, []).append((retained["offset"], {"response": response_id, "usage": usage, "total": total}))
        previous_end = retained["offset"] + retained["length"]
    result: list[dict[str, object]] = []
    for turn in thread["turns"]:
        rows = by_turn.get(str(turn["turnId"]), [])
        if not rows:
            raise NativeSourceError("terminal turn has no retained native usage")
        corrected: dict[str, dict[str, int]] = {}
        for _, row in rows:
            corrected[str(row["response"])] = row["usage"]  # later offsets replace the same response
        summed = {key: sum(value[key] for value in corrected.values()) for key in COUNTERS}
        if summed != rows[-1][1]["total"]:
            raise NativeSourceError("final native turn total does not reconcile corrected responses")
        result.append({"threadId": thread["threadId"], "turnId": turn["turnId"],
                       "turnSequence": turn["turnSequence"], "usage": summed})
    return result


def _artifact_digest(artifact: dict[str, object]) -> str:
    projected = {key: value for key, value in artifact.items() if key != "captureDigest"}
    return _digest(_canonical(projected))


def _capture_with_transport(root_thread_id: str, codex_home: pathlib.Path, transport: _Transport) -> dict[str, object]:
    _uuid(root_thread_id, "root thread id")
    first, first_calls = _capture_host(transport, root_thread_id)
    rollout_rows = []
    usage = []
    for thread in first:
        identity, records = _scan_rollout(thread, codex_home)
        rollout_rows.append({"identity": identity, "records": records})
        usage.extend(_usage_from_records(thread, records))
    second, second_calls = _capture_host(transport, root_thread_id)
    if not _same_roster(first, second):
        raise NativeSourceError("native census or terminal turn roster changed during capture")
    projection_threads = [{key: row[key] for key in
                           ("threadId", "parentThreadId", "parentChain", "archived", "source", "provider", "model", "effort", "turns")}
                          for row in first]
    artifact: dict[str, object] = {
        "schema": CAPTURE_SCHEMA, "outcome": POSITIVE_OUTCOME, "rootThreadId": root_thread_id,
        "limits": {"maxThreads": MAX_THREADS, "maxPages": MAX_PAGES, "pageLimit": PAGE_LIMIT,
                   "maxResponseBytes": MAX_RESPONSE_BYTES, "maxRolloutBytes": MAX_ROLLOUT_BYTES,
                   "maxRecordBytes": MAX_RECORD_BYTES, "maxCaptureBytes": MAX_CAPTURE_BYTES},
        "initialExchanges": first_calls, "confirmationExchanges": second_calls,
        "rollouts": rollout_rows,
        "projection": {"threads": projection_threads, "turnUsage": usage},
    }
    artifact["captureDigest"] = _artifact_digest(artifact)
    if len(_canonical(artifact)) > MAX_CAPTURE_BYTES:
        raise NativeSourceError("native source capture exceeds its artifact bound")
    return artifact


def capture(root_thread_id: str, codex_home: pathlib.Path, *, _transport: _Transport | None = None) -> dict[str, object]:
    """Capture from the fixed native source. `_transport` is test-internal."""
    transport = _transport or _AppServerTransport()
    try:
        return _capture_with_transport(root_thread_id, codex_home, transport)
    finally:
        transport.close()


def _replay_host(rows: object, root_thread_id: str) -> list[dict[str, object]]:
    replay = _ReplayTransport(rows)
    threads, calls = _capture_host(replay, root_thread_id)
    replay.finish()
    if len(calls) != len(rows):
        raise NativeSourceError("retained exchange replay is incomplete")
    return threads


def _verify_capture(artifact: object) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    if (not isinstance(artifact, dict) or artifact.get("schema") != CAPTURE_SCHEMA or
            artifact.get("outcome") != POSITIVE_OUTCOME or set(artifact) != {
                "schema", "outcome", "rootThreadId", "limits", "initialExchanges", "confirmationExchanges",
                "rollouts", "projection", "captureDigest"}):
        raise NativeSourceError("native source capture envelope is malformed")
    if len(_canonical(artifact)) > MAX_CAPTURE_BYTES or artifact.get("captureDigest") != _artifact_digest(artifact):
        raise NativeSourceError("native source capture digest disagrees")
    expected_limits = {"maxThreads": MAX_THREADS, "maxPages": MAX_PAGES, "pageLimit": PAGE_LIMIT,
                       "maxResponseBytes": MAX_RESPONSE_BYTES, "maxRolloutBytes": MAX_ROLLOUT_BYTES,
                       "maxRecordBytes": MAX_RECORD_BYTES, "maxCaptureBytes": MAX_CAPTURE_BYTES}
    if artifact.get("limits") != expected_limits:
        raise NativeSourceError("native source capture limits differ from the verifier")
    root = _uuid(artifact.get("rootThreadId"), "root thread id")
    first = _replay_host(artifact.get("initialExchanges"), root)
    second = _replay_host(artifact.get("confirmationExchanges"), root)
    if not _same_roster(first, second):
        raise NativeSourceError("replayed native census or turn roster drifted")
    rollouts = artifact.get("rollouts")
    if not isinstance(rollouts, list) or len(rollouts) != len(first):
        raise NativeSourceError("retained rollout inventory is incomplete")
    usage: list[dict[str, object]] = []
    for thread, retained in zip(first, rollouts):
        if not isinstance(retained, dict) or set(retained) != {"identity", "records"}:
            raise NativeSourceError("retained rollout evidence is malformed")
        identity = retained["identity"]
        if (not isinstance(identity, dict) or set(identity) != {"threadId", "relativePath", "device", "inode",
                "size", "mtimeNs", "scannedBytes", "fileDigest"} or
                identity.get("threadId") != thread["threadId"] or
                not isinstance(identity.get("relativePath"), str) or not identity["relativePath"] or
                pathlib.PurePosixPath(identity["relativePath"]).is_absolute() or
                any(part in ("", ".", "..") for part in pathlib.PurePosixPath(identity["relativePath"]).parts) or
                any(type(identity.get(key)) is not int or identity[key] < 0
                    for key in ("device", "inode", "size", "mtimeNs")) or
                type(identity.get("scannedBytes")) is not int or identity["scannedBytes"] < 0 or
                identity["scannedBytes"] > MAX_ROLLOUT_BYTES or identity.get("size") != identity["scannedBytes"] or
                not isinstance(identity.get("fileDigest"), str) or not re.fullmatch(r"[0-9a-f]{64}", identity["fileDigest"])):
            raise NativeSourceError("retained rollout identity is malformed")
        records = retained["records"]
        if not isinstance(records, list):
            raise NativeSourceError("retained rollout records are malformed")
        for record in records:
            if isinstance(record, dict) and record.get("offset", -1) + record.get("length", 0) > identity["scannedBytes"]:
                raise NativeSourceError("retained token usage record exceeds the scanned file bound")
        usage.extend(_usage_from_records(thread, records))
    projected_threads = [{key: row[key] for key in
                          ("threadId", "parentThreadId", "parentChain", "archived", "source", "provider", "model", "effort", "turns")}
                         for row in first]
    projection = artifact.get("projection")
    if projection != {"threads": projected_threads, "turnUsage": usage}:
        raise NativeSourceError("native source projection differs from retained bytes")
    return projected_threads, usage


def verify(artifact: object, snapshot: object) -> dict[str, object]:
    threads, usage = _verify_capture(artifact)
    if (not isinstance(snapshot, dict) or set(snapshot) != {"schema", "rootThreadId", "threads", "turnUsage"} or
            snapshot.get("schema") != SNAPSHOT_SCHEMA or snapshot.get("rootThreadId") != artifact["rootThreadId"] or
            not isinstance(snapshot.get("threads"), list) or not isinstance(snapshot.get("turnUsage"), list)):
        raise NativeSourceError("supplied telemetry snapshot is malformed")
    native_threads = {row["threadId"]: row for row in threads}
    supplied_threads: dict[str, dict[str, object]] = {}
    for row in snapshot["threads"]:
        if (not isinstance(row, dict) or set(row) != {"threadId", "parentThreadId", "parentChain", "provider", "turnIds"} or
                row.get("threadId") in supplied_threads or not isinstance(row.get("turnIds"), list) or
                not isinstance(row.get("parentChain"), list) or not isinstance(row.get("provider"), str)):
            raise NativeSourceError("supplied telemetry thread inventory is malformed")
        thread_id = _uuid(row.get("threadId"), "supplied telemetry thread id")
        if row.get("parentThreadId") is not None:
            _uuid(row.get("parentThreadId"), "supplied telemetry parent thread id")
        if (any(not isinstance(value, str) or not UUID.fullmatch(value) for value in row["parentChain"]) or
                any(not isinstance(value, str) or not UUID.fullmatch(value) for value in row["turnIds"]) or
                len(set(row["turnIds"])) != len(row["turnIds"])):
            raise NativeSourceError("supplied telemetry thread inventory is malformed")
        supplied_threads[thread_id] = row
    missing_descendants = sorted(set(native_threads) - set(supplied_threads))
    foreign_descendants = sorted(set(supplied_threads) - set(native_threads))
    mismatched_threads = []
    missing_turns = []
    foreign_turns = []
    for thread_id in sorted(set(native_threads) & set(supplied_threads)):
        native = native_threads[thread_id]; supplied = supplied_threads[thread_id]
        expected_ids = [turn["turnId"] for turn in native["turns"]]
        if (supplied["parentThreadId"] != native["parentThreadId"] or
                supplied["parentChain"] != native["parentChain"] or supplied["provider"] != native["provider"]):
            mismatched_threads.append(thread_id)
        if len(set(supplied["turnIds"])) != len(supplied["turnIds"]):
            raise NativeSourceError("supplied telemetry turn inventory contains duplicates")
        missing_turns.extend(f"{thread_id}:{turn}" for turn in expected_ids if turn not in supplied["turnIds"])
        foreign_turns.extend(f"{thread_id}:{turn}" for turn in supplied["turnIds"] if turn not in expected_ids)
        if [turn for turn in supplied["turnIds"] if turn in expected_ids] != [turn for turn in expected_ids if turn in supplied["turnIds"]]:
            mismatched_threads.append(thread_id)
    native_usage = {(row["threadId"], row["turnId"]): row for row in usage}
    supplied_usage: dict[tuple[object, object], dict[str, object]] = {}
    for row in snapshot["turnUsage"]:
        if (not isinstance(row, dict) or set(row) != {"threadId", "turnId", "turnSequence", "usage"} or
                (row.get("threadId"), row.get("turnId")) in supplied_usage or
                type(row.get("turnSequence")) is not int or row["turnSequence"] <= 0):
            raise NativeSourceError("supplied telemetry usage inventory is malformed")
        _uuid(row.get("threadId"), "supplied usage thread id")
        _uuid(row.get("turnId"), "supplied usage turn id")
        _counters(row.get("usage"))
        supplied_usage[(row["threadId"], row["turnId"])] = row
    missing_usage = sorted(f"{key[0]}:{key[1]}" for key in set(native_usage) - set(supplied_usage))
    foreign_usage = sorted(f"{key[0]}:{key[1]}" for key in set(supplied_usage) - set(native_usage))
    mismatched_usage = sorted(f"{key[0]}:{key[1]}" for key in set(native_usage) & set(supplied_usage)
                              if supplied_usage[key] != native_usage[key])
    discrepancies = any((missing_descendants, foreign_descendants, mismatched_threads, missing_turns,
                         foreign_turns, missing_usage, foreign_usage, mismatched_usage))
    return {
        "schema": VERIFY_SCHEMA,
        "status": "incomplete" if discrepancies else "verified",
        "outcome": None if discrepancies else POSITIVE_OUTCOME,
        "captureDigest": artifact["captureDigest"],
        "missingDescendants": missing_descendants,
        "foreignDescendants": foreign_descendants,
        "mismatchedThreads": sorted(set(mismatched_threads)),
        "missingTurns": sorted(missing_turns),
        "foreignTurns": sorted(foreign_turns),
        "missingUsage": missing_usage,
        "foreignUsage": foreign_usage,
        "mismatchedUsage": mismatched_usage,
    }


def snapshot_from_capture(artifact: object) -> dict[str, object]:
    """Test/integration adapter: derive the strict telemetry comparison shape."""
    threads, usage = _verify_capture(artifact)
    return {
        "schema": SNAPSHOT_SCHEMA, "rootThreadId": artifact["rootThreadId"],
        "threads": [{"threadId": row["threadId"], "parentThreadId": row["parentThreadId"],
                     "parentChain": row["parentChain"], "provider": row["provider"],
                     "turnIds": [turn["turnId"] for turn in row["turns"]]} for row in threads],
        "turnUsage": usage,
    }


def _read_private(path: pathlib.Path) -> object:
    nofollow = getattr(os, "O_NOFOLLOW", None)
    if nofollow is None:
        raise NativeSourceError("host cannot provide no-follow private input access")
    try:
        fd = os.open(path, os.O_RDONLY | nofollow)
    except OSError as error:
        raise NativeSourceError("private input is unavailable") from error
    try:
        before = os.fstat(fd)
        if (not stat.S_ISREG(before.st_mode) or before.st_size > MAX_CAPTURE_BYTES or
                (os.name != "nt" and stat.S_IMODE(before.st_mode) != 0o600)):
            raise NativeSourceError("private input identity, bounds, or permissions are invalid")
        chunks: list[bytes] = []
        remaining = before.st_size
        while remaining:
            chunk = os.read(fd, min(remaining, 1024 * 1024))
            if not chunk:
                raise NativeSourceError("private input was truncated while reading")
            chunks.append(chunk)
            remaining -= len(chunk)
        after = os.fstat(fd)
        if ((before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) !=
                (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns)):
            raise NativeSourceError("private input changed while reading")
        return _loads(b"".join(chunks), "private input")
    finally:
        os.close(fd)


def _write_private(path: pathlib.Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    if path.exists() or path.is_symlink():
        raise NativeSourceError("output already exists")
    raw = _canonical(value) + b"\n"
    if len(raw) > MAX_CAPTURE_BYTES:
        raise NativeSourceError("output exceeds its bound")
    fd, temporary = tempfile.mkstemp(prefix=".native-source-", dir=path.parent)
    try:
        os.fchmod(fd, 0o600)
        with os.fdopen(fd, "wb") as stream:
            stream.write(raw); stream.flush(); os.fsync(stream.fileno())
        os.replace(temporary, path)
    except Exception:
        try: os.unlink(temporary)
        except OSError: pass
        raise


def parser() -> argparse.ArgumentParser:
    root = argparse.ArgumentParser(description="Capture or verify bounded native source evidence")
    commands = root.add_subparsers(dest="command", required=True)
    capture_cmd = commands.add_parser("capture")
    capture_cmd.add_argument("--root-thread-id", required=True)
    capture_cmd.add_argument("--codex-home", type=pathlib.Path, required=True)
    capture_cmd.add_argument("--output", type=pathlib.Path, required=True)
    verify_cmd = commands.add_parser("verify")
    verify_cmd.add_argument("--capture", type=pathlib.Path, required=True)
    verify_cmd.add_argument("--telemetry-snapshot", type=pathlib.Path, required=True)
    return root


def main(argv: list[str] | None = None) -> int:
    args = parser().parse_args(argv)
    try:
        if args.command == "capture":
            value = capture(args.root_thread_id, args.codex_home)
            _write_private(args.output, value)
            print(json.dumps({"schema": CAPTURE_SCHEMA, "status": "captured", "outcome": POSITIVE_OUTCOME,
                              "captureDigest": value["captureDigest"]}, separators=(",", ":")))
            return 0
        artifact = _read_private(args.capture)
        snapshot = _read_private(args.telemetry_snapshot)
        result = verify(artifact, snapshot)
        print(json.dumps(result, sort_keys=True, separators=(",", ":")))
        return 0 if result["status"] == "verified" else 2
    except NativeSourceError as error:
        print(f"native-source-refused: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
