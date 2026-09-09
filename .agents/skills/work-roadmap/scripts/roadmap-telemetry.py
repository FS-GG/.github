#!/usr/bin/env python3
"""Record repository-owned roadmap dispatches without claiming native tool interception."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import re
import subprocess
import sys
import uuid
from datetime import datetime, timezone

sys.dont_write_bytecode = True

from fsgg_telemetry_defaults import (
    CI_ASSIGNMENT_SCHEMA,
    ConfigurationError,
    HostConfig,
    create_assignment,
    discover_config,
    validate_identity,
    write_private_json,
)


BATCH_SCHEMA = "fsgg.telemetry.ingest/1"
STATE_SCHEMA = "fsgg.telemetry.roadmap-dispatch-state/1"
RUNTIME = "collaboration-spawn-agent"
REVIEW_SCHEMA = "fsgg.telemetry.process-review-input/1"
ACTIVITY_SCHEMA = "fsgg.telemetry.activity-span-input/1"
ATTRIBUTION_SCHEMA = "fsgg.telemetry.activity-usage-attribution-input/1"
COMPLICATION_SCHEMA = "fsgg.telemetry.complication-input/1"
DASHBOARD_HEALTH_SCHEMA = "fsgg.telemetry.dashboard-event-health/1"


def now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def event(kind: str, identity: str, item: str | None, **values: object) -> dict[str, object]:
    return {"kind": kind, "identity": identity, "itemId": item, "revision": 0, **values}


def digest(prefix: str, *values: str) -> str:
    value = "\x1f".join(values).encode()
    return prefix + hashlib.sha256(value).hexdigest()[:32]


def publish(config: HostConfig, state: dict[str, object], events: list[dict[str, object]]) -> None:
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
    batch_path = write_private_json(config.store_root / "orchestrator-publish", f"batch-{invocation}-{sequence}", batch)
    try:
        completed = subprocess.run(
            [config.engine, "telemetry", "store", "publish", "--store-root", str(config.store_root), "--input", str(batch_path)],
            text=True,
            capture_output=True,
            timeout=20,
            check=False,
        )
    finally:
        batch_path.unlink(missing_ok=True)
    if completed.returncode != 0:
        raise ConfigurationError(completed.stderr.strip() or "telemetry batch publication failed")


def state_path(config: HostConfig, token: str) -> pathlib.Path:
    if not isinstance(token, str) or not re.fullmatch(r"[0-9a-f]{32}", token):
        raise ConfigurationError("token must be the opaque 32-hex dispatch token")
    return config.store_root / "orchestrator-dispatches" / f"{token}.json"


def read_state(config: HostConfig, token: str) -> dict[str, object]:
    path = state_path(config, token)
    try:
        if path.is_symlink() or not path.is_file() or path.stat().st_size > 8192:
            raise ConfigurationError("dispatch state is unavailable")
        if os.name != "nt" and (path.stat().st_mode & 0o777) != 0o600:
            raise ConfigurationError("dispatch state permissions must be 0600")
        value = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(value, dict) or value.get("schema") != STATE_SCHEMA or value.get("token") != token:
            raise ConfigurationError("dispatch state is malformed")
        return value
    except (OSError, json.JSONDecodeError) as error:
        raise ConfigurationError(f"dispatch state is unreadable: {error}") from error


def save_state(config: HostConfig, state: dict[str, object]) -> None:
    write_private_json(config.store_root / "orchestrator-dispatches", str(state["token"]), state)


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


def begin(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    feature = validate_identity("feature", args.feature)
    item = validate_identity("item", args.item)
    attempt = validate_identity("attempt", args.attempt)
    parent_attempt = validate_identity("parent attempt", args.parent_attempt, optional=True)
    producer = validate_identity("producer", args.producer)
    model = validate_identity("model", args.model)
    effort = validate_identity("effort", args.effort)
    if args.late_after_seconds < 0:
        raise ConfigurationError("late-after-seconds must be non-negative")
    token, activation, dispatch, invocation = (uuid.uuid4().hex for _ in range(4))
    parent_dispatch = parent_invocation = None
    root_invocation = invocation
    relation = "root"
    if args.parent_token:
        parent = read_state(config, args.parent_token)
        if parent.get("phase") not in {"started", "terminal"}:
            raise ConfigurationError("parent dispatch must be started before a child is expected")
        if parent.get("itemId") != item:
            raise ConfigurationError("parent and child dispatches must share the item identity")
        activation = str(parent["activationId"])
        parent_dispatch = str(parent["dispatchId"])
        parent_invocation = str(parent["invocationId"])
        root_invocation = str(parent["rootInvocationId"])
        relation = args.relation
    elif args.relation != "root":
        raise ConfigurationError("child and follow-up dispatches require --parent-token")
    timestamp = now()
    state: dict[str, object] = {
        "schema": STATE_SCHEMA,
        "token": token,
        "phase": "expected",
        "sequence": 0,
        "featureId": feature,
        "itemId": item,
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
        "nativeId": None,
    }
    events = []
    if relation == "root":
        events.extend([
            event("feature", feature, None, name=feature),
            event("item", item, item, featureId=feature),
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
    publish(config, state, events)
    save_state(config, state)
    return {"schema": "fsgg.telemetry.roadmap-dispatch/1", "status": "expected", "token": token,
            "coverage": "native-collaboration-usage-unsupported"}


def started(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    state = read_state(config, args.token)
    if state.get("phase") != "expected":
        raise ConfigurationError("dispatch must be expected exactly once before start")
    native_id = validate_identity("native id", args.native_id)
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
        event("runtime-gap", f"runtime-gap-{invocation}-native-usage", item, invocationId=invocation,
              code="native-collaboration-usage-unsupported"),
        event("runtime-gap", f"runtime-gap-{invocation}-native-process", item, invocationId=invocation,
              code="native-process-id-unavailable"),
    ]
    publish(config, state, events)
    state["phase"], state["nativeId"] = "started", native_id
    save_state(config, state)
    return {"schema": "fsgg.telemetry.roadmap-dispatch/1", "status": "started", "token": args.token,
            "coverage": "native-collaboration-usage-unsupported"}


def finish(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    state = read_state(config, args.token)
    if state.get("phase") != "started":
        raise ConfigurationError("dispatch must be started exactly once before terminal")
    item, invocation = str(state["itemId"]), str(state["invocationId"])
    timestamp = now()
    exit_code = args.exit_code if args.exit_code is not None else (0 if args.outcome == "completed" else 1)
    if exit_code < 0:
        raise ConfigurationError("exit-code must be non-negative")
    events = [
        event("runtime-terminal", f"runtime-terminal-{invocation}", item, invocationId=invocation,
              threadId=state["nativeId"], outcome=args.outcome, exitCode=exit_code),
        event("event-time", f"event-time-{invocation}-terminal", item, invocationId=invocation,
              event="terminal", occurredAt=timestamp, occurredClockProvenance="host-wall",
              observedAt=timestamp, observedClockProvenance="host-wall"),
    ]
    publish(config, state, events)
    state["phase"], state["outcome"] = "terminal", args.outcome
    save_state(config, state)
    drain = subprocess.run(
        [config.engine, "telemetry", "store", "drain", "--store-root", str(config.store_root)],
        text=True, capture_output=True, timeout=30, check=False,
    )
    result={"schema": "fsgg.telemetry.roadmap-dispatch/1", "status": "terminal", "token": args.token,
            "outcome": args.outcome, "coverage": "native-collaboration-usage-unsupported",
            "drain": "complete" if drain.returncode == 0 else "pending"}
    if drain.returncode==0 and args.outcome=="completed" and state.get("relation")=="root":
        result["dashboardPublication"]=refresh_dashboard(config)
    return result


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
    publish(config, state, [value])
    save_state(config, state)
    drain = subprocess.run([config.engine, "telemetry", "store", "drain", "--store-root", str(config.store_root)],
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
    begin_parser.add_argument("--parent-token")
    begin_parser.add_argument("--relation", choices=("root", "child", "follow-up"), default="root")
    begin_parser.add_argument("--producer", default="roadmap-orchestrator")
    begin_parser.add_argument("--late-after-seconds", type=int, default=60)
    started_parser = commands.add_parser("started")
    started_parser.add_argument("--token", required=True)
    started_parser.add_argument("--native-id", required=True)
    finish_parser = commands.add_parser("finish")
    finish_parser.add_argument("--token", required=True)
    finish_parser.add_argument("--outcome", choices=("completed", "failed", "cancelled", "blocked"), required=True)
    finish_parser.add_argument("--exit-code", type=int)
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
            completed = subprocess.run([config.engine, "telemetry", "store", "status", "--store-root", str(config.store_root)],
                                       text=True, capture_output=True, timeout=20, check=False)
            print(json.dumps({"schema": "fsgg.telemetry.host-status/1",
                              "status": "ready" if completed.returncode == 0 else "unavailable"}, separators=(",", ":")))
            return 0 if completed.returncode == 0 else 1
        handlers = {"begin": begin, "started": started, "finish": finish, "ci-assignment": create_ci,
                    "review": review, "activity": activity, "usage-attribution": attribution, "complication": complication}
        value = handlers[args.command](config, args)
        print(json.dumps(value, separators=(",", ":")))
        return 0
    except (ConfigurationError, OSError, subprocess.SubprocessError) as error:
        print(f"fsgg roadmap telemetry: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
