#!/usr/bin/env python3
"""Record repository-owned roadmap dispatches without claiming native tool interception."""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import pathlib
import re
import subprocess
import sys
import uuid
from collections import defaultdict
from datetime import datetime, timezone

sys.dont_write_bytecode = True

from fsgg_telemetry_defaults import (
    CI_ASSIGNMENT_SCHEMA,
    ConfigurationError,
    HostConfig,
    create_assignment,
    discover_config,
    validate_identity,
    validate_workspace,
    workspace_mutation_command,
    write_private_json,
)
from native_collaboration_usage import HostUnavailable, collect as collect_native_usage


BATCH_SCHEMA = "fsgg.telemetry.ingest/1"
STATE_SCHEMA = "fsgg.telemetry.roadmap-dispatch-state/1"
RUNTIME = "collaboration-spawn-agent"
REVIEW_SCHEMA = "fsgg.telemetry.process-review-input/1"
ACTIVITY_SCHEMA = "fsgg.telemetry.activity-span-input/1"
ATTRIBUTION_SCHEMA = "fsgg.telemetry.activity-usage-attribution-input/1"
COMPLICATION_SCHEMA = "fsgg.telemetry.complication-input/1"
DASHBOARD_HEALTH_SCHEMA = "fsgg.telemetry.dashboard-event-health/1"
ORIGINAL_ASSIGNMENTS = "docs/coordination/telemetry-original-item-assignments.json"
ORIGINAL_ASSIGNMENTS_SCHEMA = "fsgg.telemetry.original-item-assignments/1"
ORIGINAL_BINDING_STATE_SCHEMA = "fsgg.telemetry.original-binding-state/1"


def now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def event(kind: str, identity: str, item: str | None, **values: object) -> dict[str, object]:
    return {"kind": kind, "identity": identity, "itemId": item, "revision": 0, **values}


def digest(prefix: str, *values: str) -> str:
    value = "\x1f".join(values).encode()
    return prefix + hashlib.sha256(value).hexdigest()[:32]


def authorized_original(feature: str, item: str, original: str) -> str:
    """Bind a non-self original to an immutable read of protected .github/main."""
    def github(endpoint: str) -> dict[str, object]:
        try:
            result = subprocess.run(
                ["gh", "api", endpoint], capture_output=True, text=True, timeout=10, check=False,
            )
        except (OSError, subprocess.SubprocessError) as error:
            raise ConfigurationError("protected original-item assignment is unavailable") from error
        if result.returncode != 0 or len(result.stdout.encode("utf-8")) > 131072:
            raise ConfigurationError("protected original-item assignment is unavailable")
        try:
            value = json.loads(result.stdout)
        except json.JSONDecodeError as error:
            raise ConfigurationError("protected original-item assignment is malformed") from error
        if not isinstance(value, dict):
            raise ConfigurationError("protected original-item assignment is malformed")
        return value

    ref = github("repos/FS-GG/.github/git/ref/heads/main")
    commit = ref.get("object")
    if (ref.get("ref") != "refs/heads/main" or not isinstance(commit, dict) or
            commit.get("type") != "commit" or not isinstance(commit.get("sha"), str) or
            not re.fullmatch(r"[0-9a-f]{40}", commit["sha"])):
        raise ConfigurationError("protected original-item revision is malformed")
    revision = commit["sha"]
    content = github(f"repos/FS-GG/.github/contents/{ORIGINAL_ASSIGNMENTS}?ref={revision}")
    if (content.get("type") != "file" or content.get("path") != ORIGINAL_ASSIGNMENTS or
            content.get("encoding") != "base64" or not isinstance(content.get("content"), str)):
        raise ConfigurationError("protected original-item assignment is malformed")
    try:
        raw = base64.b64decode(content["content"].replace("\n", ""), validate=True)
        if len(raw) > 65536:
            raise ValueError("oversize")
        document = json.loads(raw)
    except (ValueError, json.JSONDecodeError) as error:
        raise ConfigurationError("protected original-item assignment is malformed") from error
    if (not isinstance(document, dict) or set(document) != {"schema", "assignments"} or
            document["schema"] != ORIGINAL_ASSIGNMENTS_SCHEMA or
            not isinstance(document["assignments"], list) or len(document["assignments"]) > 200):
        raise ConfigurationError("protected original-item assignment is malformed")
    matches = []
    for row in document["assignments"]:
        if (not isinstance(row, dict) or set(row) != {"featureId", "itemId", "originalItemId"} or
                any(not isinstance(row[key], str) for key in row)):
            raise ConfigurationError("protected original-item assignment is malformed")
        if row["featureId"] == feature and row["itemId"] == item:
            matches.append(row["originalItemId"])
    if matches != [original]:
        raise ConfigurationError("original item is not authorized by the protected assignment")
    return revision + ":" + hashlib.sha256(raw).hexdigest()


def prepare_publication(
    config: HostConfig,
    state: dict[str, object],
    operation: str,
    next_phase: str,
    events: list[dict[str, object]],
) -> None:
    pending = state.get("pendingPublication")
    if pending is not None:
        if (not isinstance(pending, dict) or pending.get("operation") != operation or
                pending.get("nextPhase") != next_phase or
                not isinstance(pending.get("batch"), dict) or pending["batch"].get("events") != events):
            raise ConfigurationError("a different telemetry publication is already pending")
        return
    sequence = int(state["sequence"]) + 1
    state["sequence"] = sequence
    invocation = str(state["invocationId"])
    batch = {
        "schema": BATCH_SCHEMA,
        "ingestId": f"{invocation}-{sequence:06d}",
        "sourceIdentity": str(state["producerStream"]),
        "generation": invocation,
        "cursor": str(sequence),
        "eventCount": len(events),
        "events": events,
    }
    state["pendingPublication"] = {"operation": operation, "nextPhase": next_phase, "batch": batch}
    save_state(config, state)


def publish_pending(config: HostConfig, state: dict[str, object]) -> bool:
    validate_workspace(config)
    pending = state.get("pendingPublication")
    if not isinstance(pending, dict) or set(pending) != {"operation", "nextPhase", "batch"}:
        raise ConfigurationError("telemetry publication intent is unavailable")
    batch = pending["batch"]
    if (not isinstance(batch, dict) or batch.get("schema") != BATCH_SCHEMA or
            batch.get("generation") != state.get("invocationId") or
            batch.get("cursor") != str(state.get("sequence"))):
        raise ConfigurationError("telemetry publication intent is malformed")
    invocation = str(state["invocationId"])
    sequence = int(state["sequence"])
    batch_path = write_private_json(config.store_root / "orchestrator-publish", f"batch-{invocation}-{sequence}", batch)
    try:
        command = ([config.engine, "telemetry", "workspace", "submit", "--config", str(config.path),
                    "--repository", str(config.repository), "--producer", str(state.get("associationProducer")),
                    "--binding-digest", str(state.get("associationDigest")), "--input", str(batch_path)] if config.workspace else
                   [config.engine, "telemetry", "store", "publish", "--store-root", str(config.store_root), "--input", str(batch_path)])
        completed = subprocess.run(
            workspace_mutation_command(config, command),
            text=True,
            capture_output=True,
            timeout=20,
            check=False,
        )
    finally:
        batch_path.unlink(missing_ok=True)
    if completed.returncode != 0:
        message = completed.stderr.strip() or "telemetry batch publication failed"
        if "invalid-request" in message:
            # The engine has definitively rejected these bytes, so they cannot
            # have been applied. Retaining that intent would permanently fence
            # every corrected observation behind an unreplayable batch. Roll
            # back only the unpublished cursor; unknown delivery outcomes keep
            # their exact durable batch and continue to use normal replay.
            state["sequence"] = sequence - 1
            del state["pendingPublication"]
            if str(pending["operation"]).startswith("native-usage:"):
                state.pop("usageIntent", None)
            if str(pending["operation"]).startswith("native-roster:"):
                state.pop("rosterIntent", None)
            save_state(config, state)
        raise ConfigurationError(message)
    if pending["operation"] == "population-only":
        if not config.workspace:
            raise ConfigurationError("original binding requires a receipt-scoped workspace")
        response = completed.stdout.strip()
        if response.startswith("{"):
            try:
                receipt = json.loads(response)
            except json.JSONDecodeError as error:
                raise ConfigurationError("original binding receipt is malformed") from error
            if (not isinstance(receipt, dict) or receipt.get("schema") != "fsgg.telemetry.receipt/1" or
                    receipt.get("batchId") != batch.get("ingestId")):
                raise ConfigurationError("original binding receipt is malformed")
            response = receipt.get("status")
        if response != "applied":
            if response == "durably-received":
                raise ConfigurationError("original binding receipt is not applied; retry the exact binding")
            raise ConfigurationError("original binding receipt did not apply")
    state["phase"] = pending["nextPhase"]
    del state["pendingPublication"]
    save_state(config, state)
    return True


def publish(
    config: HostConfig,
    state: dict[str, object],
    events: list[dict[str, object]],
    *,
    operation: str = "observation",
    next_phase: str | None = None,
) -> bool:
    prepare_publication(config, state, operation, next_phase or str(state["phase"]), events)
    return publish_pending(config, state)


def state_path(config: HostConfig, token: str) -> pathlib.Path:
    if not isinstance(token, str) or not re.fullmatch(r"[0-9a-f]{32}", token):
        raise ConfigurationError("token must be the opaque 32-hex dispatch token")
    return config.store_root / "orchestrator-dispatches" / f"{token}.json"


def drain_command(config: HostConfig) -> list[str]:
    if config.workspace:
        return workspace_mutation_command(config, [
            config.engine, "telemetry", "workspace", "drain", "--config", str(config.path),
            "--repository", str(config.repository), "--binding-digest", str(config.binding_digest),
        ])
    return [config.engine, "telemetry", "store", "drain", "--store-root", str(config.store_root)]


def read_state(config: HostConfig, token: str) -> dict[str, object]:
    path = state_path(config, token)
    try:
        if path.is_symlink() or not path.is_file() or path.stat().st_size > 262144:
            raise ConfigurationError("dispatch state is unavailable")
        if os.name != "nt" and (path.stat().st_mode & 0o777) != 0o600:
            raise ConfigurationError("dispatch state permissions must be 0600")
        value = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(value, dict) or value.get("schema") != STATE_SCHEMA or value.get("token") != token:
            raise ConfigurationError("dispatch state is malformed")
        if config.workspace and (value.get("associationProducer") != config.producer or value.get("associationDigest") != config.binding_digest):
            raise ConfigurationError("dispatch state belongs to a retired workspace association")
        return value
    except (OSError, json.JSONDecodeError) as error:
        raise ConfigurationError(f"dispatch state is unreadable: {error}") from error


def save_state(config: HostConfig, state: dict[str, object]) -> None:
    directory = ("orchestrator-original-bindings" if state.get("schema") == ORIGINAL_BINDING_STATE_SCHEMA
                 else "orchestrator-dispatches")
    write_private_json(config.store_root / directory, str(state["token"]), state)


def population_only(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    """Bind an observed codex-exec member without creating a second expected root."""
    feature = validate_identity("feature", args.feature)
    item = validate_identity("item", args.item)
    original = validate_identity("original item", args.original_item)
    producer = validate_identity("producer", args.producer)
    if not config.workspace:
        raise ConfigurationError("population-only requires a receipt-scoped workspace")
    if item == original:
        raise ConfigurationError("population-only requires a distinct protected original item")
    token = hashlib.sha256(f"population-only\x1f{feature}\x1f{item}".encode()).hexdigest()[:32]
    path = config.store_root / "orchestrator-original-bindings" / f"{token}.json"
    expected = {
        "schema": ORIGINAL_BINDING_STATE_SCHEMA,
        "token": token,
        "featureId": feature,
        "itemId": item,
        "originalItemId": original,
        "producerStream": producer,
        "associationProducer": config.producer,
        "associationDigest": config.binding_digest,
    }
    if path.exists() or path.is_symlink():
        try:
            if path.is_symlink() or not path.is_file() or path.stat().st_size > 262144:
                raise ConfigurationError("original binding state is unavailable")
            if os.name != "nt" and (path.stat().st_mode & 0o777) != 0o600:
                raise ConfigurationError("original binding state permissions must be 0600")
            state = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            raise ConfigurationError("original binding state is unreadable") from error
        if not isinstance(state, dict) or any(state.get(key) != value for key, value in expected.items()):
            raise ConfigurationError("original binding retry differs from its protected identity")
        if (state.get("invocationId") != "original-binding-" + token or
                not isinstance(state.get("originalAssignmentDigest"), str) or
                not re.fullmatch(r"[0-9a-f]{40}:[0-9a-f]{64}", state["originalAssignmentDigest"]) or
                state.get("sequence") != 1):
            raise ConfigurationError("original binding state is malformed")
        if state.get("phase") == "applied" and state.get("pendingPublication") is None:
            authorized_original(feature, item, original)
            return {"schema": "fsgg.telemetry.original-binding-result/1", "status": "applied"}
        if state.get("phase") != "pending":
            raise ConfigurationError("original binding state is malformed")
        publish_pending(config, state)
    else:
        assignment_digest = authorized_original(feature, item, original)
        state = {**expected, "originalAssignmentDigest": assignment_digest,
                 "phase": "pending", "sequence": 0,
                 "invocationId": "original-binding-" + token}
        key = digest("", item, original)
        events = [
            event("feature", feature, None, name=feature),
            event("item", item, item, featureId=feature),
            event("budget-population", "budget-population-" + key, item,
                  originalItemId=original, state="open", sourceKind="native-item",
                  sourceRef="roadmap-dispatch:" + key),
        ]
        publish(config, state, events, operation="population-only", next_phase="applied")
    return {"schema": "fsgg.telemetry.original-binding-result/1", "status": "applied"}


def matching_dispatch(config: HostConfig, expected: dict[str, object]) -> dict[str, object] | None:
    directory = config.store_root / "orchestrator-dispatches"
    if not directory.exists():
        return None
    paths = list(directory.glob("*.json"))
    if len(paths) > 4096:
        raise ConfigurationError("dispatch state inventory exceeds the recovery bound")
    identity = {name: expected[name] for name in ("featureId", "itemId", "attemptId")}
    matches = []
    for path in paths:
        if not re.fullmatch(r"[0-9a-f]{32}\.json", path.name):
            continue
        state = read_state(config, path.stem)
        if all(state.get(name) == value for name, value in identity.items()):
            matches.append(state)
    if len(matches) > 1:
        raise ConfigurationError("dispatch identity is ambiguous in private state")
    if not matches:
        return None
    if not all((matches[0].get(name, matches[0].get("itemId")) if name == "originalItemId"
                else matches[0].get(name)) == value for name, value in expected.items()):
        raise ConfigurationError("dispatch attempt retry differs from its durable identity")
    return matches[0]


def refresh_dashboard(config: HostConfig) -> dict[str, object]:
    dashboard = pathlib.Path(__file__).resolve().parents[4] / "tools" / "telemetry-dashboard.py"
    try:
        completed = subprocess.run(
            [sys.executable, str(dashboard), "publisher-event", "--config", str(config.path)],
            text=True, capture_output=True, timeout=60, check=False,
        )
        if completed.returncode != 0 or len(completed.stdout.encode("utf-8")) > 8192:
            return {"status": "advisory-failure", "reason": "publisher-event-subprocess-failed"}
        value = json.loads(completed.stdout)
        fields={"schema","status","reason","observedAt","publicRevision","commit"}
        if not isinstance(value,dict) or set(value)!=fields or value.get("schema")!=DASHBOARD_HEALTH_SCHEMA:
            return {"status": "advisory-failure", "reason": "publisher-event-result-invalid"}
        return {"status":"observed","health":value}
    except (OSError,subprocess.SubprocessError,UnicodeError,json.JSONDecodeError):
        return {"status": "advisory-failure", "reason": "publisher-event-subprocess-failed"}


def prospective_coverage(state: dict[str, object]) -> str:
    return ("native-collaboration-usage-unknown" if state.get("hostParentThreadId")
            else "native-collaboration-usage-unsupported")


def native_snapshot(value: object) -> dict[str, object]:
    """Validate the complete bounded collector projection before retaining it."""
    if not isinstance(value, dict):
        raise ConfigurationError("native usage snapshot is malformed")
    thread_id = value.get("threadId")
    identifiers = value.get("allTurnIds")
    inventory = value.get("turnInventory")
    turns = value.get("turns")
    paging = value.get("inventoryPaging")
    captured_at = value.get("inventoryCapturedAt")
    roster_digest = value.get("inventoryRosterDigest")
    source_digest = value.get("inventorySourceDigest")
    if (not isinstance(thread_id, str) or not re.fullmatch(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}", thread_id) or
            not isinstance(identifiers, list) or len(identifiers) > 1000 or
            any(not isinstance(turn, str) or not re.fullmatch(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}", turn)
                for turn in identifiers) or len(set(identifiers)) != len(identifiers) or
            not isinstance(inventory, list) or len(inventory) != len(identifiers) or
            not isinstance(turns, list) or len(turns) > len(identifiers) or
            value.get("inventoryProvenance") != "codex-app-server-thread-turns-list" or
            value.get("inventoryHostSource") != "codex-app-server:thread/turns/list" or
            value.get("usageProvenance") != "codex-native-token-usage-record" or
            value.get("collectorProducer") != "fsgg-work-roadmap-native-collector/1" or
            type(value.get("complete")) is not bool):
        raise ConfigurationError("native usage snapshot is malformed")
    if (not isinstance(paging, list) or not 1 <= len(paging) <= 10 or
            not isinstance(captured_at, str) or len(captured_at) > 40 or not captured_at.endswith("Z") or
            not isinstance(roster_digest, str) or not re.fullmatch(r"[0-9a-f]{64}", roster_digest) or
            not isinstance(source_digest, str) or not re.fullmatch(r"[0-9a-f]{64}", source_digest)):
        raise ConfigurationError("native inventory evidence is malformed")
    try:
        if datetime.fromisoformat(captured_at.replace("Z", "+00:00")).utcoffset() is None:
            raise ValueError
    except ValueError as error:
        raise ConfigurationError("native inventory evidence is malformed") from error
    previous_cursor = None
    for number, page_row in enumerate(paging, 1):
        if (not isinstance(page_row, dict) or
                set(page_row) != {"page", "requestCursor", "nextCursor", "rowCount"} or
                page_row.get("page") != number or page_row.get("requestCursor") != previous_cursor or
                type(page_row.get("rowCount")) is not int or not 0 <= page_row["rowCount"] <= 100 or
                (page_row.get("nextCursor") is not None and
                 (not isinstance(page_row["nextCursor"], str) or not page_row["nextCursor"] or
                  len(page_row["nextCursor"]) > 1024))):
            raise ConfigurationError("native inventory paging evidence is malformed")
        if number < len(paging) and page_row["nextCursor"] is None:
            raise ConfigurationError("native inventory paging evidence is incomplete")
        previous_cursor = page_row["nextCursor"]
    if paging[-1]["nextCursor"] is not None or sum(row["rowCount"] for row in paging) != len(inventory):
        raise ConfigurationError("native inventory paging evidence disagrees with the roster")
    for sequence, row in enumerate(inventory, 1):
        if (not isinstance(row, dict) or
                set(row) != {"turnId", "turnSequence", "status", "terminal", "usageAvailable"} or
                row.get("turnId") != identifiers[sequence - 1] or row.get("turnSequence") != sequence or
                not isinstance(row.get("status"), str) or not row["status"] or
                type(row.get("terminal")) is not bool or
                row["terminal"] != (row["status"] in {"completed", "failed", "interrupted"}) or
                type(row.get("usageAvailable")) is not bool):
            raise ConfigurationError("native turn inventory is malformed")
    observed = []
    usage_fields = {"input_tokens", "cached_input_tokens", "output_tokens",
                    "reasoning_output_tokens", "total_tokens"}
    for row in turns:
        if (not isinstance(row, dict) or set(row) != {"turnId", "turnSequence", "usage"} or
                row.get("turnId") not in identifiers or
                row.get("turnSequence") != identifiers.index(row["turnId"]) + 1 or
                row["turnId"] in observed or not isinstance(row.get("usage"), dict)):
            raise ConfigurationError("native turn usage inventory is malformed")
        usage = row["usage"]
        if (set(usage) != usage_fields or
                any(type(usage.get(field)) is not int or usage[field] < 0 for field in usage_fields) or
                usage["cached_input_tokens"] > usage["input_tokens"] or
                usage["reasoning_output_tokens"] > usage["output_tokens"] or
                usage["input_tokens"] + usage["output_tokens"] != usage["total_tokens"]):
            raise ConfigurationError("native turn usage counters are malformed")
        observed.append(row["turnId"])
    for row in inventory:
        if row["usageAvailable"] != (row["turnId"] in observed) or (row["usageAvailable"] and not row["terminal"]):
            raise ConfigurationError("native turn inventory and usage disagree")
    roster = [{key: row[key] for key in ("turnId", "turnSequence", "status", "terminal")}
              for row in inventory]
    projected = json.dumps({"threadId": thread_id, "turnInventory": roster}, sort_keys=True,
                           separators=(",", ":"), ensure_ascii=True).encode("ascii")
    if hashlib.sha256(projected).hexdigest() != roster_digest:
        raise ConfigurationError("native inventory roster digest disagrees with the roster")
    app_collection = value.get("appServerResponses")
    rollout_collection = value.get("rolloutRecords")
    if (not isinstance(app_collection, list) or not 3 <= len(app_collection) <= 1024 or
            not isinstance(rollout_collection, list) or len(rollout_collection) > 1024):
        raise ConfigurationError("native App Server source evidence is incomplete")
    evidence_chunks = []
    app_entries = []
    total_bytes = 0
    app_fields = {"method", "threadId", "requestCursor", "requestSha256", "requestBytesBase64",
                  "responseSha256", "responseBytesBase64"}
    try:
        for record in app_collection:
            if not isinstance(record, dict) or set(record) != app_fields:
                raise ConfigurationError("native App Server source evidence is malformed")
            request_raw = base64.b64decode(record["requestBytesBase64"], validate=True)
            response_raw = base64.b64decode(record["responseBytesBase64"], validate=True)
            total_bytes += len(request_raw) + len(response_raw)
            if (total_bytes > 512 * 1024 or not request_raw or not response_raw or
                    record["requestSha256"] != hashlib.sha256(request_raw).hexdigest() or
                    record["responseSha256"] != hashlib.sha256(response_raw).hexdigest()):
                raise ConfigurationError("native App Server source evidence is malformed")
            request_document, response_document = json.loads(request_raw), json.loads(response_raw)
            params = request_document.get("params") if isinstance(request_document, dict) else None
            method = request_document.get("method") if isinstance(request_document, dict) else None
            if (method not in {"thread/list", "thread/read", "thread/turns/list"} or
                    not isinstance(params, dict) or record["method"] != method or
                    record["threadId"] != params.get("threadId") or
                    record["requestCursor"] != params.get("cursor") or
                    not isinstance(response_document, dict) or
                    response_document.get("id") != request_document.get("id") or
                    not isinstance(response_document.get("result"), dict)):
                raise ConfigurationError("App Server response is not bound to its exact request")
            evidence_chunks.extend((request_raw, response_raw))
            app_entries.append((record, request_document, response_document))
    except (TypeError, ValueError, UnicodeError, json.JSONDecodeError) as error:
        raise ConfigurationError("native App Server source evidence is malformed") from error
    rollout_raws = []
    total_bytes = 0
    try:
        for record in rollout_collection:
            if not isinstance(record, dict) or set(record) != {"sha256", "bytesBase64"}:
                raise ConfigurationError("native source byte evidence is malformed")
            raw = base64.b64decode(record["bytesBase64"], validate=True)
            total_bytes += len(raw)
            if total_bytes > 512 * 1024 or not raw or record["sha256"] != hashlib.sha256(raw).hexdigest():
                raise ConfigurationError("native source byte evidence is malformed")
            evidence_chunks.append(raw)
            rollout_raws.append(raw)
    except (TypeError, ValueError) as error:
        raise ConfigurationError("native source byte evidence is malformed") from error
    source_binding = value.get("sourceBinding")
    binding_fields = {"schema", "producerIdentity", "sha256", "bytesBase64"}
    try:
        if (not isinstance(source_binding, dict) or set(source_binding) != binding_fields or
                source_binding.get("schema") != "fsgg.telemetry.native-inventory-source-binding/1" or
                source_binding.get("producerIdentity") != value["collectorProducer"]):
            raise ConfigurationError("native source binding is malformed")
        binding = base64.b64decode(source_binding["bytesBase64"], validate=True)
        binding_document = json.loads(binding)
        expected_binding_fields = {"schema", "producerIdentity", "capturedAt", "hostSource",
                                   "rootInvocationId", "invocationId", "parentThreadId", "threadId",
                                   "orderedTurnIds", "revision"}
        if (not binding or len(binding) > 16384 or
                source_binding["sha256"] != hashlib.sha256(binding).hexdigest() or
                not isinstance(binding_document, dict) or set(binding_document) != expected_binding_fields or
                json.dumps(binding_document, sort_keys=True, separators=(",", ":"),
                           ensure_ascii=True).encode("ascii") != binding or
                binding_document["schema"] != source_binding["schema"] or
                binding_document["producerIdentity"] != source_binding["producerIdentity"] or
                binding_document["capturedAt"] != captured_at or
                binding_document["hostSource"] != value["inventoryHostSource"] or
                binding_document["threadId"] != thread_id or
                binding_document["orderedTurnIds"] != identifiers or
                not isinstance(binding_document["rootInvocationId"], str) or
                not binding_document["rootInvocationId"] or len(binding_document["rootInvocationId"]) > 256 or
                not isinstance(binding_document["invocationId"], str) or
                not binding_document["invocationId"] or len(binding_document["invocationId"]) > 256 or
                not isinstance(binding_document["parentThreadId"], str) or
                not re.fullmatch(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}",
                                 binding_document["parentThreadId"]) or
                type(binding_document["revision"]) is not int or binding_document["revision"] < 0):
            raise ConfigurationError("native source binding is malformed")
    except (KeyError, TypeError, ValueError, UnicodeError, json.JSONDecodeError) as error:
        raise ConfigurationError("native source binding is malformed") from error
    exact_digest = hashlib.sha256()
    for raw in [binding, *evidence_chunks]:
        exact_digest.update(len(raw).to_bytes(8, "big"))
        exact_digest.update(raw)
    if exact_digest.hexdigest() != source_digest:
        raise ConfigurationError("native inventory source digest disagrees with retained bytes")
    provider = value.get("provider")
    provider_provenance = value.get("providerProvenance")
    if ((provider is None) != (provider_provenance is None) or
            (provider is not None and (not isinstance(provider, str) or provider != provider.strip() or
                                       not provider or len(provider) > 128)) or
            (provider_provenance is not None and provider_provenance != "codex-app-server-thread.modelProvider")):
        raise ConfigurationError("native provider observation is malformed")
    for field in ("model", "effort"):
        label = value.get(field)
        if label is not None and (not isinstance(label, str) or label != label.strip() or
                                  not label or len(label) > 128):
            raise ConfigurationError(f"native {field} observation is malformed")
    thread_sources = [response["result"].get("thread") for record, _, response in app_entries
                      if record["method"] == "thread/read" and record["threadId"] == thread_id]
    if not any(isinstance(thread, dict) and thread.get("id") == thread_id and
               thread.get("modelProvider") == provider and thread.get("model") == value.get("model") and
               thread.get("reasoningEffort") == value.get("effort") for thread in thread_sources):
        raise ConfigurationError("native provider/profile does not match retained App Server bytes")
    retained_turns = []
    actual_paging = []
    previous_cursor = None
    seen_cursors = set()
    turn_entries = [(record, response["result"]) for record, _, response in app_entries
                    if record["method"] == "thread/turns/list" and record["threadId"] == thread_id]
    for number, (record, result) in enumerate(turn_entries, 1):
        data, next_cursor = result.get("data"), result.get("nextCursor")
        if (record["requestCursor"] != previous_cursor or not isinstance(data, list) or
                any(not isinstance(row, dict) for row in data) or
                (next_cursor is not None and (not isinstance(next_cursor, str) or not next_cursor or
                                              next_cursor in seen_cursors)) or
                (number < len(turn_entries) and next_cursor is None) or
                (number == len(turn_entries) and next_cursor is not None)):
            raise ConfigurationError("retained App Server paging chain is incomplete")
        actual_paging.append({"page": number, "requestCursor": previous_cursor,
                              "nextCursor": next_cursor, "rowCount": len(data)})
        retained_turns.extend(data)
        if next_cursor is not None:
            seen_cursors.add(next_cursor)
        previous_cursor = next_cursor
    if actual_paging != paging:
        raise ConfigurationError("native inventory paging evidence disagrees with retained response bytes")
    if [(row.get("id"), row.get("status")) for row in retained_turns] != [
            (row["turnId"], row["status"]) for row in inventory]:
        raise ConfigurationError("native turn inventory does not match retained App Server bytes")
    native_records = defaultdict(list)
    try:
        for raw in rollout_raws:
            document = json.loads(raw)
            payload = document.get("payload") if isinstance(document, dict) else None
            if not isinstance(document, dict) or document.get("type") != "token_usage_record" or not isinstance(payload, dict) or \
                    payload.get("thread_id") != thread_id or payload.get("turn_id") not in identifiers:
                raise ConfigurationError("retained rollout bytes cross thread or turn identity")
            native_records[payload["turn_id"]].append(payload)
    except (UnicodeError, json.JSONDecodeError) as error:
        raise ConfigurationError("retained rollout bytes are malformed") from error
    observations_by_turn = {observation["turnId"]: observation for observation in turns}

    def retained_counts(candidate):
        if (not isinstance(candidate, dict) or set(candidate) != usage_fields or
                any(type(candidate.get(field)) is not int or candidate[field] < 0 for field in usage_fields) or
                candidate["cached_input_tokens"] > candidate["input_tokens"] or
                candidate["reasoning_output_tokens"] > candidate["output_tokens"] or
                candidate["input_tokens"] + candidate["output_tokens"] != candidate["total_tokens"]):
            raise ConfigurationError("retained rollout usage is malformed")
        return candidate

    for inventory_row in inventory:
        responses = {}
        turn_id = inventory_row["turnId"]
        rows = native_records[turn_id]
        for row in rows:
            response = row.get("response_id")
            if not isinstance(response, str) or not response or not isinstance(row.get("usage"), dict):
                raise ConfigurationError("retained rollout usage is malformed")
            responses[response] = retained_counts(row["usage"])
            retained_counts(row.get("turn_token_usage"))
        if rows:
            derived = {field: sum(response[field] for response in responses.values())
                       for field in usage_fields}
            available = rows[-1].get("turn_token_usage") == derived
        else:
            derived, available = None, False
        observation = observations_by_turn.get(turn_id)
        if inventory_row["usageAvailable"] != available or \
                (available and (observation is None or observation["usage"] != derived)) or \
                (not available and observation is not None):
            raise ConfigurationError("native usage does not match retained rollout bytes")
    derived_complete = (bool(identifiers) and len(paging) == 1 and provider is not None and
                        value.get("model") is not None and value.get("effort") is not None and
                        all(row["terminal"] and row["usageAvailable"] for row in inventory))
    if value["complete"] != derived_complete:
        raise ConfigurationError("native usage completeness is inconsistent")
    return value


def begin(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    feature = validate_identity("feature", args.feature)
    item = validate_identity("item", args.item)
    original_item = validate_identity("original item", args.original_item or item)
    attempt = validate_identity("attempt", args.attempt)
    parent_attempt = validate_identity("parent attempt", args.parent_attempt, optional=True)
    producer = validate_identity("producer", args.producer)
    model = validate_identity("model", args.model)
    effort = validate_identity("effort", args.effort)
    if args.late_after_seconds < 0:
        raise ConfigurationError("late-after-seconds must be non-negative")
    parent_dispatch = parent_invocation = None
    relation = "root"
    assignment_digest = None
    if args.parent_token:
        parent = read_state(config, args.parent_token)
        if args.original_item is None:
            original_item = str(parent.get("originalItemId", item))
        if parent.get("phase") not in {"started", "terminal"}:
            raise ConfigurationError("parent dispatch must be started before a child is expected")
        if parent.get("itemId") != item:
            raise ConfigurationError("parent and child dispatches must share the item identity")
        if parent.get("originalItemId", item) != original_item:
            raise ConfigurationError("parent and child dispatches must share the original item identity")
        parent_dispatch = str(parent["dispatchId"])
        parent_invocation = str(parent["invocationId"])
        assignment_digest = parent.get("originalAssignmentDigest")
        relation = args.relation
        if relation == "follow-up" and not isinstance(parent.get("usageLedger", {}), dict):
            raise ConfigurationError("follow-up usage baseline is malformed")
    elif args.relation != "root":
        raise ConfigurationError("child and follow-up dispatches require --parent-token")
    elif original_item != item:
        assignment_digest = authorized_original(feature, item, original_item)
    expected = {
        "featureId": feature, "itemId": item, "originalItemId": original_item,
        "originalAssignmentDigest": assignment_digest,
        "attemptId": attempt, "parentAttemptId": parent_attempt,
        "producerStream": producer, "model": model, "effort": effort, "relation": relation,
        "parentDispatchId": parent_dispatch, "parentInvocationId": parent_invocation,
        "lateAfterSeconds": args.late_after_seconds,
    }
    host_parent = os.environ.get("CODEX_THREAD_ID") if relation != "root" else None
    if host_parent and not re.fullmatch(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}", host_parent):
        host_parent = None
    existing = matching_dispatch(config, expected)
    if existing is not None:
        if existing.get("phase") == "begin-pending":
            publish_pending(config, existing)
        elif existing.get("phase") != "expected":
            raise ConfigurationError("dispatch attempt already progressed beyond expectation")
        return {"schema": "fsgg.telemetry.roadmap-dispatch/1", "status": "expected",
                "token": existing["token"], "coverage": prospective_coverage(existing)}
    if (args.parent_token and parent.get("phase") != "started" and
            not (relation == "follow-up" and parent.get("phase") == "terminal")):
        raise ConfigurationError("parent dispatch must be started before a child is expected")
    baseline_ids: list[str] = []
    baseline_known = relation != "follow-up"
    baseline_thread = baseline_provenance = None
    baseline_evidence: dict[str, object] = {}
    if relation == "follow-up" and host_parent and parent.get("hostParentThreadId") == host_parent:
        try:
            prior = native_snapshot(collect_native_usage(
                host_parent, str(parent["nativeId"]),
                root_invocation_id=str(parent["rootInvocationId"]),
                invocation_id=str(parent["invocationId"]), revision=0))
            baseline_ids = list(prior["allTurnIds"])
            baseline_thread = prior["threadId"]
            baseline_provenance = prior["inventoryProvenance"]
            baseline_evidence = {
                "baselineHostSource": prior["inventoryHostSource"],
                "baselinePaging": prior["inventoryPaging"],
                "baselineCapturedAt": prior["inventoryCapturedAt"],
                "baselineRosterDigest": prior["inventoryRosterDigest"],
                "baselineSourceDigest": prior["inventorySourceDigest"],
                "baselineSourceBinding": prior["sourceBinding"],
                "baselineProducerStream": parent["producerStream"],
                "baselineCollectorProducer": prior["collectorProducer"],
                "baselineAppServerResponses": prior["appServerResponses"],
                "baselineRolloutRecords": prior["rolloutRecords"],
            }
            baseline_known = True
        except (HostUnavailable, ConfigurationError, OSError, subprocess.SubprocessError):
            pass
    token, activation, dispatch, invocation = (uuid.uuid4().hex for _ in range(4))
    root_invocation = invocation
    if args.parent_token:
        activation = str(parent["activationId"])
        root_invocation = str(parent["rootInvocationId"])
    timestamp = now()
    state: dict[str, object] = {
        "schema": STATE_SCHEMA,
        "token": token,
        "phase": "begin-pending",
        "sequence": 0,
        "featureId": feature,
        "itemId": item,
        "originalItemId": original_item,
        "originalAssignmentDigest": assignment_digest,
        "attemptId": attempt,
        "parentAttemptId": parent_attempt,
        "producerStream": producer,
        "model": model,
        "effort": effort,
        "activationId": activation,
        "dispatchId": dispatch,
        "invocationId": invocation,
        "rootInvocationId": root_invocation,
        "parentDispatchId": parent_dispatch,
        "parentInvocationId": parent_invocation,
        "relation": relation,
        "lateAfterSeconds": args.late_after_seconds,
        "nativeId": None,
        "hostParentThreadId": host_parent,
        "baselineTurnIds": baseline_ids,
        "baselineThreadId": baseline_thread,
        "baselineProvenance": baseline_provenance,
        **baseline_evidence,
        "usageBaselineKnown": baseline_known,
        "associationProducer": config.producer,
        "associationDigest": config.binding_digest,
    }
    events = []
    if relation == "root":
        events.extend([
            event("feature", feature, None, name=feature),
            event("item", item, item, featureId=feature),
            event("budget-population", digest("budget-population-", item, original_item), item,
                  originalItemId=original_item, state="open", sourceKind="native-item",
                  sourceRef=digest("roadmap-dispatch:", item, original_item)),
            event("operational-activation", f"operational-activation-{activation}", item,
                  activationId=activation, scope="explicit-future-dispatches", runtime=RUNTIME,
                  activatedAt=timestamp, clockProvenance="host-wall", lateAfterSeconds=args.late_after_seconds),
        ])
    if parent_attempt:
        events.append(event("parent-child", digest("parent-child-", parent_attempt, attempt), item,
                            parentId=parent_attempt, childId=attempt))
    events.append(event("expected-dispatch", f"expected-dispatch-{dispatch}", item,
                        dispatchId=dispatch, activationId=activation, relation=relation,
                        parentDispatchId=parent_dispatch, runtime=RUNTIME,
                        expectedAt=timestamp, clockProvenance="host-wall"))
    publish(config, state, events, operation="begin", next_phase="expected")
    return {"schema": "fsgg.telemetry.roadmap-dispatch/1", "status": "expected", "token": token,
            "coverage": prospective_coverage(state)}


def started(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    state = read_state(config, args.token)
    native_id = validate_identity("native id", args.native_id)
    if state.get("phase") == "started":
        if state.get("nativeId") != native_id:
            raise ConfigurationError("started dispatch belongs to a different native identity")
        return {"schema": "fsgg.telemetry.roadmap-dispatch/1", "status": "started", "token": args.token,
                "coverage": prospective_coverage(state)}
    if state.get("phase") == "start-pending":
        if state.get("nativeId") != native_id:
            raise ConfigurationError("pending start belongs to a different native identity")
        publish_pending(config, state)
        return {"schema": "fsgg.telemetry.roadmap-dispatch/1", "status": "started", "token": args.token,
                "coverage": prospective_coverage(state)}
    if state.get("phase") != "expected":
        raise ConfigurationError("dispatch must be expected before start")
    item, invocation = str(state["itemId"]), str(state["invocationId"])
    timestamp = now()
    events = [
        event("invocation-lineage", f"invocation-lineage-{invocation}", item,
              dispatchId=state["dispatchId"], invocationId=invocation, relation=state["relation"],
              parentInvocationId=state["parentInvocationId"], rootInvocationId=state["rootInvocationId"], runtime=RUNTIME),
        event("runtime-admission", f"runtime-admission-{invocation}", item,
              invocationId=invocation, featureId=state["featureId"], attemptId=state["attemptId"],
              parentAttemptId=state["parentAttemptId"], producerStream=state["producerStream"],
              requestedModel=state["model"], requestedEffort=state["effort"], backend="codex-collaboration"),
        event("runtime-start", f"runtime-process-{invocation}", item, invocationId=invocation,
              threadId=native_id, turnId=None, turnSequence=None, processId=0, phase="process"),
        event("event-time", f"event-time-{invocation}-admission", item, invocationId=invocation,
              event="admission", occurredAt=timestamp, occurredClockProvenance="host-wall",
              observedAt=timestamp, observedClockProvenance="host-wall"),
        event("event-time", f"event-time-{invocation}-start", item, invocationId=invocation,
              event="start", occurredAt=timestamp, occurredClockProvenance="host-wall",
              observedAt=timestamp, observedClockProvenance="host-wall"),
        event("runtime-gap", f"runtime-gap-{invocation}-native-process", item, invocationId=invocation,
              code="native-process-id-unavailable"),
    ]
    if not state.get("hostParentThreadId"):
        events.append(event("runtime-gap", f"runtime-gap-{invocation}-native-usage", item,
                            invocationId=invocation, code="native-collaboration-usage-unsupported"))
    state["phase"], state["nativeId"] = "start-pending", native_id
    publish(config, state, events, operation="started", next_phase="started")
    return {"schema": "fsgg.telemetry.roadmap-dispatch/1", "status": "started", "token": args.token,
            "coverage": prospective_coverage(state)}


def _finish_roster_intent(config: HostConfig, state: dict[str, object]) -> None:
    intent = state.get("rosterIntent")
    if not isinstance(intent, dict):
        return
    roster = state.get("expectedTurnRoster")
    published = state.get("turnRosterPublishedCount", 0)
    entries = intent.get("entries")
    if (not isinstance(roster, list) or type(published) is not int or published < 0 or
            not isinstance(entries, list) or intent.get("offset") != published):
        raise ConfigurationError("native turn roster intent is malformed")
    thread_id = state.get("nativeThreadId")
    provenance = state.get("expectedTurnRosterProvenance")
    baseline = state.get("baselineTurnIds", [])
    if (not isinstance(thread_id, str) or provenance != "codex-app-server-thread-turns-list" or
            not isinstance(baseline, list)):
        raise ConfigurationError("native turn roster intent is malformed")
    for position, entry in enumerate(entries, published + 1):
        if not isinstance(entry, dict) or set(entry) != {"turnId", "turnSequence", "hash"}:
            raise ConfigurationError("native turn roster intent is malformed")
        sequence = len(baseline) + position
        fingerprint = hashlib.sha256(json.dumps(
            [thread_id, entry.get("turnId"), sequence, provenance],
            separators=(",", ":"), ensure_ascii=True).encode("ascii")).hexdigest()
        if (position > len(roster) or entry["turnId"] != roster[position - 1] or
                entry.get("turnSequence") != sequence or entry.get("hash") != fingerprint):
            raise ConfigurationError("native turn roster intent differs from expected population")
    state["turnRosterPublishedCount"] = published + len(entries)
    del state["rosterIntent"]
    save_state(config, state)


def _resume_native_publication(config: HostConfig, state: dict[str, object]) -> None:
    pending = state.get("pendingPublication")
    if not isinstance(pending, dict):
        return
    operation = pending.get("operation")
    try:
        publish_pending(config, state)
    except ConfigurationError:
        if not state.get("pendingPublication"):
            if isinstance(operation, str) and operation.startswith("native-roster:"):
                state.pop("rosterIntent", None)
            save_state(config, state)
        raise
    if operation == "native-thread":
        state["nativeThreadPublished"] = True
        save_state(config, state)
    elif operation == "native-inventory-authority":
        state["nativeInventoryPublished"] = True
        if isinstance(state.get("nativeInventoryIntegration"), dict):
            state["nativeInventoryIntegration"]["status"] = "published"
        save_state(config, state)
    elif isinstance(operation, str) and operation.startswith("native-roster:"):
        _finish_roster_intent(config, state)


def _publish_roster_entries(config: HostConfig, state: dict[str, object], native: dict[str, object],
                            eligible_ids: list[str]) -> None:
    published = state.setdefault("turnRosterPublishedCount", 0)
    if type(published) is not int or published < 0 or published > len(eligible_ids):
        raise ConfigurationError("native turn roster publication cursor is malformed")
    inventory = {row["turnId"]: row for row in native["turnInventory"]}
    missing = eligible_ids[published:]
    for offset in range(0, len(missing), 64):
        entries = []
        events = []
        for turn_id in missing[offset:offset + 64]:
            row = inventory[turn_id]
            fingerprint = hashlib.sha256(json.dumps(
                [native["threadId"], turn_id, row["turnSequence"], native["inventoryProvenance"]],
                separators=(",", ":"), ensure_ascii=True).encode("ascii")).hexdigest()
            entries.append({"turnId": turn_id, "turnSequence": row["turnSequence"], "hash": fingerprint})
            events.append(event(
                "runtime-start", digest("runtime-turn-", str(state["invocationId"]), turn_id),
                str(state["itemId"]), invocationId=state["invocationId"], threadId=native["threadId"],
                turnId=turn_id, turnSequence=row["turnSequence"], processId=0, phase="turn"))
        state["rosterIntent"] = {"offset": state["turnRosterPublishedCount"], "entries": entries}
        operation = "native-roster:" + hashlib.sha256(json.dumps(entries, sort_keys=True).encode()).hexdigest()
        prepare_publication(config, state, operation, str(state["phase"]), events)
        publish_pending(config, state)
        _finish_roster_intent(config, state)


def _publish_turn_roster(config: HostConfig, state: dict[str, object], native: dict[str, object],
                         eligible_ids: list[str]) -> None:
    binding_record = native.get("sourceBinding")
    binding = json.loads(base64.b64decode(binding_record["bytesBase64"], validate=True))
    if (binding.get("rootInvocationId") != state.get("rootInvocationId") or
            binding.get("invocationId") != state.get("invocationId") or
            binding.get("parentThreadId") != state.get("hostParentThreadId") or
            binding.get("threadId") != native.get("threadId") or
            binding.get("orderedTurnIds") != native.get("allTurnIds") or
            binding.get("revision") != 0):
        raise ConfigurationError("native source binding differs from durable dispatch identity")
    old = state.get("expectedTurnRoster", [])
    if (not isinstance(old, list) or any(not isinstance(value, str) for value in old) or
            eligible_ids[:len(old)] != old):
        raise ConfigurationError("native expected turn roster changed or reordered")
    if state.get("nativeInventoryPublished"):
        if eligible_ids != old:
            raise ConfigurationError("published native inventory roster cannot change")
        _publish_roster_entries(config, state, native, eligible_ids)
        return
    state["expectedTurnRoster"] = eligible_ids
    state["expectedTurnRosterProvenance"] = native["inventoryProvenance"]
    state["nativeInventoryHostSource"] = native["inventoryHostSource"]
    state["nativeInventoryPaging"] = native["inventoryPaging"]
    state["nativeInventoryCapturedAt"] = native["inventoryCapturedAt"]
    state["nativeInventoryRosterDigest"] = native["inventoryRosterDigest"]
    state["nativeInventorySourceDigest"] = native["inventorySourceDigest"]
    state["nativeInventoryProducerStream"] = state["producerStream"]
    state["nativeInventoryBindingDigest"] = binding_record["sha256"]
    state["nativeInventorySourceBinding"] = binding_record
    state["nativeInventoryCollectorProducer"] = native["collectorProducer"]
    state["nativeInventoryAppServerResponses"] = native["appServerResponses"]
    state["nativeInventoryRolloutRecords"] = native["rolloutRecords"]
    observed_provider = native.get("provider")
    observed_provider_provenance = native.get("providerProvenance")
    if (state.get("nativeProvider") is not None and
            (state["nativeProvider"] != observed_provider or
             state.get("nativeProviderProvenance") != observed_provider_provenance)):
        raise ConfigurationError("native provider observation changed for a thread")
    if observed_provider is not None or "nativeProvider" not in state:
        state["nativeProvider"] = observed_provider
        state["nativeProviderProvenance"] = observed_provider_provenance
    fact_ready = (observed_provider is not None and native.get("model") == state.get("model") and
                  native.get("effort") == state.get("effort") and len(native["inventoryPaging"]) == 1)
    inventory_id = digest("native-inventory-", str(state["invocationId"]))
    inventory_fact = event(
        "runtime-native-inventory/1",
        digest("runtime-native-inventory-", str(state["invocationId"])),
        str(state["itemId"]), inventoryId=inventory_id,
        originalItemId=state["originalItemId"], invocationId=state["invocationId"],
        page=1, pages=1, expectedTurnIds=eligible_ids,
        expectedProvider=observed_provider, requestedModel=state["model"],
        requestedEffort=state["effort"], support="provider-native-final-turn-counters",
        followupBaseline=len(state.get("baselineTurnIds", [])),
        capturedAt=native["inventoryCapturedAt"],
        sourceKind="provider-capability-and-dispatch-roster",
        sourceDigest=native["inventorySourceDigest"]) if fact_ready else None
    source_fact = event(
        "runtime-native-inventory-source/1",
        digest("runtime-native-inventory-source-", str(state["invocationId"])),
        str(state["itemId"]), inventoryId=inventory_id,
        originalItemId=state["originalItemId"], invocationId=state["invocationId"],
        sourceDigest=native["inventorySourceDigest"], sourceBinding=binding_record) if fact_ready else None
    state["nativeInventoryIntegration"] = {
        "status": "ready" if fact_ready else "incomplete-source-provenance",
        "reason": (None if fact_ready else
                   "provider/profile provenance or a stable single-page inventory is unavailable"),
        "collectorProducer": native["collectorProducer"],
        "sourceDigest": native["inventorySourceDigest"],
        "sourceBinding": binding_record,
        "fact": inventory_fact,
        "sourceFact": source_fact,
    }
    save_state(config, state)
    if fact_ready:
        publish(config, state, [inventory_fact, source_fact], operation="native-inventory-authority")
        state["nativeInventoryPublished"] = True
        state["nativeInventoryIntegration"]["status"] = "published"
        save_state(config, state)
    _publish_roster_entries(config, state, native, eligible_ids)


def reconcile_usage(config: HostConfig, state: dict[str, object]) -> str:
    """Retain and publish the exact native turn population before its usage."""
    if state.get("phase") != "terminal" or not state.get("hostParentThreadId"):
        return "native-collaboration-usage-unsupported"
    if not state.get("usageBaselineKnown", state.get("relation") != "follow-up"):
        return "native-collaboration-usage-unknown"
    _resume_native_publication(config, state)
    if isinstance(state.get("rosterIntent"), dict):
        # No pending batch means publish_pending durably recorded an
        # acknowledgement before the process stopped.
        _finish_roster_intent(config, state)
    intent = state.get("usageIntent")
    if isinstance(intent, dict):
        if state.get("pendingPublication"):
            try:
                publish_pending(config, state)
            except ConfigurationError:
                if not state.get("pendingPublication"):
                    state.pop("usageIntent", None)
                    save_state(config, state)
                raise
        ledger = state.setdefault("usageLedger", {})
        ledger[intent["turnId"]] = {"revision": intent["revision"], "hash": intent["hash"]}
        del state["usageIntent"]
        save_state(config, state)
    try:
        native = native_snapshot(collect_native_usage(
            str(state["hostParentThreadId"]), str(state["nativeId"]),
            root_invocation_id=str(state["rootInvocationId"]),
            invocation_id=str(state["invocationId"]), revision=0))
    except (HostUnavailable, ConfigurationError, OSError, subprocess.SubprocessError):
        return "native-collaboration-usage-unknown"
    ledger = state.setdefault("usageLedger", {})
    if not isinstance(ledger, dict):
        raise ConfigurationError("native usage ledger is malformed")
    if state.get("nativeThreadId") and state["nativeThreadId"] != native["threadId"]:
        raise ConfigurationError("native child thread changed for a dispatch")
    if not state.get("nativeThreadPublished"):
        if not state.get("nativeThreadId"):
            state["nativeThreadId"] = native["threadId"]
            save_state(config, state)
        publish(config, state, [event("runtime-start", f"runtime-thread-{state['invocationId']}",
                                      str(state["itemId"]), invocationId=state["invocationId"],
                                      threadId=native["threadId"], turnId=None, turnSequence=None,
                                      processId=0, phase="thread")], operation="native-thread")
        state["nativeThreadPublished"] = True
        save_state(config, state)
    baseline = state.get("baselineTurnIds", [])
    if (not isinstance(baseline, list) or len(set(baseline)) != len(baseline) or
            native["allTurnIds"][:len(baseline)] != baseline or
            (state.get("baselineThreadId") and state["baselineThreadId"] != native["threadId"])):
        return "native-collaboration-usage-unknown"
    eligible_ids = list(native["allTurnIds"][len(baseline):])
    _publish_turn_roster(config, state, native, eligible_ids)
    eligible_set = set(eligible_ids)
    eligible = [turn for turn in native["turns"] if turn["turnId"] in eligible_set]
    for turn in eligible:
        usage = turn["usage"]
        turn_id = str(turn["turnId"])
        fields = [turn["turnSequence"], state.get("nativeProvider"), state.get("nativeProviderProvenance"),
                  native.get("model"), native.get("effort"), native["usageProvenance"], usage]
        fingerprint = hashlib.sha256(json.dumps(fields, sort_keys=True).encode()).hexdigest()
        previous = ledger.get(turn_id)
        if previous and previous.get("hash") == fingerprint:
            continue
        revision = 0 if previous is None else int(previous["revision"]) + 1
        observation = event("runtime-turn-usage", digest("runtime-turn-usage-", str(state["invocationId"]), turn_id),
                            str(state["itemId"]), invocationId=state["invocationId"],
                            threadId=native["threadId"], turnId=turn_id, turnSequence=turn["turnSequence"],
                            provider=state.get("nativeProvider"), requestedModel=state["model"],
                            observedModel=native.get("model"), requestedEffort=state["effort"],
                            observedEffort=native.get("effort"), backend="codex-collaboration", scope="turn",
                            provenance=native["usageProvenance"], input=usage["input_tokens"],
                            cachedInput=usage["cached_input_tokens"], output=usage["output_tokens"],
                            reasoning=usage["reasoning_output_tokens"], total=usage["total_tokens"])
        observation["revision"] = revision
        state["usageIntent"] = {"turnId": turn_id, "revision": revision, "hash": fingerprint}
        prepare_publication(config, state, f"native-usage:{turn_id}:{revision}",
                            str(state["phase"]), [observation])
        publish_pending(config, state)
        ledger[turn_id] = {"revision": revision, "hash": fingerprint}
        del state["usageIntent"]
        save_state(config, state)
    observed_ids = [turn["turnId"] for turn in eligible]
    state["nativeInventoryIntegration"]["locallyReconciled"] = (
        native["complete"] and bool(eligible_ids) and observed_ids == eligible_ids
        and set(ledger) == set(eligible_ids)
        and state.get("turnRosterPublishedCount") == len(eligible_ids))
    save_state(config, state)
    return "native-collaboration-usage-unknown"


def finish(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    state = read_state(config, args.token)
    exit_code = args.exit_code if args.exit_code is not None else (0 if args.outcome == "completed" else 1)
    if exit_code < 0:
        raise ConfigurationError("exit-code must be non-negative")
    if state.get("phase") in {"terminal-pending", "terminal"}:
        if state.get("phase") == "terminal" and "exitCode" not in state:
            if args.exit_code is not None:
                raise ConfigurationError("legacy terminal state cannot verify an explicit exit-code retry")
            state["exitCode"] = exit_code
            save_state(config, state)
        if state.get("outcome") != args.outcome or state.get("exitCode") != exit_code:
            raise ConfigurationError("terminal retry differs from the durable terminal intent")
        if state.get("phase") == "terminal-pending":
            publish_pending(config, state)
    elif state.get("phase") == "started":
        item, invocation = str(state["itemId"]), str(state["invocationId"])
        timestamp = now()
        events = [
            event("runtime-terminal", f"runtime-terminal-{invocation}", item, invocationId=invocation,
                  threadId=state["nativeId"], outcome=args.outcome, exitCode=exit_code),
            event("event-time", f"event-time-{invocation}-terminal", item, invocationId=invocation,
                  event="terminal", occurredAt=timestamp, occurredClockProvenance="host-wall",
                  observedAt=timestamp, observedClockProvenance="host-wall"),
        ]
        state["phase"], state["outcome"], state["exitCode"] = "terminal-pending", args.outcome, exit_code
        publish(config, state, events, operation="finish", next_phase="terminal")
    else:
        raise ConfigurationError("dispatch must be started before terminal")
    try:
        coverage = reconcile_usage(config, state)
    except ConfigurationError:
        coverage = "native-collaboration-usage-unknown"
    drain = subprocess.run(
        drain_command(config),
        text=True, capture_output=True, timeout=30, check=False,
    )
    result={"schema": "fsgg.telemetry.roadmap-dispatch/1", "status": "terminal", "token": args.token,
            "outcome": args.outcome, "coverage": coverage,
            "drain": "complete" if drain.returncode == 0 else "pending"}
    if drain.returncode==0 and args.outcome=="completed" and state.get("relation")=="root":
        result["dashboardPublication"]=refresh_dashboard(config)
    return result


def usage_reconcile(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    state = read_state(config, args.token)
    coverage = reconcile_usage(config, state)
    drain = subprocess.run(drain_command(config), text=True, capture_output=True, timeout=30, check=False)
    return {"schema": "fsgg.telemetry.roadmap-usage/1", "status": "reconciled", "token": args.token,
            "coverage": coverage, "drain": "complete" if drain.returncode == 0 else "pending"}


def read_contract(path: str, schema: str, fields: set[str]) -> dict[str, object]:
    source = pathlib.Path(path)
    if source.is_symlink() or not source.is_file() or source.stat().st_size > 32768:
        raise ConfigurationError("private observation input must be a regular file of at most 32768 bytes")
    try:
        value = json.loads(source.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ConfigurationError(f"private observation input is unreadable: {error}") from error
    if not isinstance(value, dict) or value.get("schema") != schema or set(value) != fields | {"schema"}:
        raise ConfigurationError(f"private observation input must have the exact {schema} shape")
    return value


def record_event(config: HostConfig, state: dict[str, object], value: dict[str, object]) -> dict[str, object]:
    operation = f"observation:{value.get('kind')}:{value.get('identity')}"
    publish(config, state, [value], operation=operation)
    drain = subprocess.run(drain_command(config),
                           text=True, capture_output=True, timeout=30, check=False)
    if drain.returncode != 0:
        raise ConfigurationError(drain.stderr.strip() or "telemetry observation drain failed")
    result={"schema": "fsgg.telemetry.roadmap-observation/1", "status": "recorded", "kind": value["kind"]}
    if state.get("phase")=="terminal" and state.get("relation")=="root":
        result["dashboardPublication"]=refresh_dashboard(config)
    return result


def review(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    state = read_state(config, args.token)
    if state.get("phase") != "terminal":
        raise ConfigurationError("process review requires a terminal attempt")
    if args.scope == "item" and state.get("relation") != "root":
        raise ConfigurationError("item process review requires the root dispatch token")
    fields = {"revision", "outcomeSynopsis", "wentWell", "problems", "avoidableDelayOrRework",
              "processObservations", "remainingRisks", "concreteImprovements", "evidence",
              "evidenceCoverage", "populationCoverage", "confidence", "reviewerModel", "reviewerEffort",
              "reviewedAt", "durationSeconds"}
    value = read_contract(args.input, REVIEW_SCHEMA, fields)
    subject = str(state["attemptId"]) if args.scope == "attempt" else str(state["itemId"])
    observation = {"kind": "process-review", "identity": digest("process-review-", args.scope, str(state["itemId"]), subject),
                   "itemId": state["itemId"], "scope": args.scope,
                   "attemptId": state["attemptId"] if args.scope == "attempt" else None,
                   **{name: value[name] for name in fields}}
    return record_event(config, state, observation)


def activity(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    state = read_state(config, args.token)
    if state.get("phase") not in {"started", "terminal"}:
        raise ConfigurationError("activity span requires a started attempt")
    fields = {"revision", "activityId", "category", "startedAt", "endedAt", "clockProvenance", "evidence", "summary"}
    value = read_contract(args.input, ACTIVITY_SCHEMA, fields)
    activity_id = validate_identity("activity", value["activityId"])
    observation = {"kind": "activity-span", "identity": digest("activity-span-", str(state["itemId"]), activity_id),
                   "itemId": state["itemId"], "invocationId": state["invocationId"], "attemptId": state["attemptId"],
                   **{name: value[name] for name in fields}}
    return record_event(config, state, observation)


def attribution(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    state = read_state(config, args.token)
    if state.get("phase") != "terminal":
        raise ConfigurationError("usage attribution requires a terminal attempt")
    fields = {"revision", "usageIdentity", "activityId", "classification", "input", "cachedInput", "output", "reasoning", "total"}
    value = read_contract(args.input, ATTRIBUTION_SCHEMA, fields)
    usage = validate_identity("usage identity", value["usageIdentity"])
    observation = {"kind": "activity-usage-attribution", "identity": digest("activity-usage-", str(state["itemId"]), usage),
                   "itemId": state["itemId"], **{name: value[name] for name in fields}}
    return record_event(config, state, observation)


def complication(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    state = read_state(config, args.token)
    if state.get("phase") not in {"started", "terminal"}:
        raise ConfigurationError("complication requires a started attempt")
    fields = {"revision", "complicationId", "activityId", "trigger", "cause", "occurredAt", "synopsis", "evidence"}
    value = read_contract(args.input, COMPLICATION_SCHEMA, fields)
    complication_id = validate_identity("complication", value["complicationId"])
    observation = {"kind": "complication", "identity": digest("complication-", str(state["itemId"]), complication_id),
                   "itemId": state["itemId"], "attemptId": state["attemptId"],
                   **{name: value[name] for name in fields if name != "complicationId"}}
    return record_event(config, state, observation)


def create_ci(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    path = create_assignment(config, CI_ASSIGNMENT_SCHEMA, feature=args.feature, item=args.item,
                             attempt=args.attempt, parent_attempt=args.parent_attempt, producer=args.producer)
    return {"schema": "fsgg.telemetry.assignment-result/1", "status": "ready", "assignment": str(path)}


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--config", help="explicit private host telemetry configuration")
    commands = result.add_subparsers(dest="command", required=True)
    begin_parser = commands.add_parser("begin")
    for name in ("feature", "item", "attempt", "model", "effort"):
        begin_parser.add_argument(f"--{name}", required=True)
    begin_parser.add_argument("--parent-attempt")
    begin_parser.add_argument("--original-item", help="stable original item shared by distinct member items")
    begin_parser.add_argument("--parent-token")
    begin_parser.add_argument("--relation", choices=("root", "child", "follow-up"), default="root")
    begin_parser.add_argument("--producer", default="roadmap-orchestrator")
    begin_parser.add_argument("--late-after-seconds", type=int, default=60)
    population_parser = commands.add_parser("population-only")
    for name in ("feature", "item", "original-item"):
        population_parser.add_argument(f"--{name}", required=True)
    population_parser.add_argument("--producer", default="roadmap-orchestrator")
    started_parser = commands.add_parser("started")
    started_parser.add_argument("--token", required=True)
    started_parser.add_argument("--native-id", required=True)
    finish_parser = commands.add_parser("finish")
    finish_parser.add_argument("--token", required=True)
    finish_parser.add_argument("--outcome", choices=("completed", "failed", "cancelled", "blocked"), required=True)
    finish_parser.add_argument("--exit-code", type=int)
    usage_parser = commands.add_parser("usage-reconcile")
    usage_parser.add_argument("--token", required=True)
    ci_parser = commands.add_parser("ci-assignment")
    for name in ("feature", "item", "attempt"):
        ci_parser.add_argument(f"--{name}", required=True)
    ci_parser.add_argument("--parent-attempt")
    ci_parser.add_argument("--producer", default="routine-delivery")
    review_parser = commands.add_parser("review")
    review_parser.add_argument("--token", required=True)
    review_parser.add_argument("--scope", choices=("attempt", "item"), required=True)
    review_parser.add_argument("--input", required=True)
    for command in ("activity", "usage-attribution", "complication"):
        observation_parser = commands.add_parser(command)
        observation_parser.add_argument("--token", required=True)
        observation_parser.add_argument("--input", required=True)
    commands.add_parser("status")
    return result


def main(argv: list[str]) -> int:
    args = parser().parse_args(argv)
    try:
        config = discover_config(args.config)
        if config is None:
            print(json.dumps({"schema": "fsgg.telemetry.host-status/1", "status": "not-configured"}, separators=(",", ":")))
            return 2
        if args.command == "status":
            command = ([config.engine, "telemetry", "workspace", "status", "--config", str(config.path),
                        "--repository", str(config.repository)] if config.workspace else
                       [config.engine, "telemetry", "store", "status", "--store-root", str(config.store_root)])
            completed = subprocess.run(command,
                                       text=True, capture_output=True, timeout=20, check=False)
            print(json.dumps({"schema": "fsgg.telemetry.host-status/1",
                              "status": "ready" if completed.returncode == 0 else "unavailable"}, separators=(",", ":")))
            return 0 if completed.returncode == 0 else 1
        handlers = {"begin": begin, "population-only": population_only,
                    "started": started, "finish": finish, "usage-reconcile": usage_reconcile,
                    "ci-assignment": create_ci,
                    "review": review, "activity": activity, "usage-attribution": attribution, "complication": complication}
        value = handlers[args.command](config, args)
        print(json.dumps(value, separators=(",", ":")))
        return 0
    except (ConfigurationError, OSError, subprocess.SubprocessError) as error:
        print(f"fsgg roadmap telemetry: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
