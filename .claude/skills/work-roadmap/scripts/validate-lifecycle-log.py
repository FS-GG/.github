#!/usr/bin/env python3
"""Validate an append-only roadmap phase/time/token JSONL ledger."""

from __future__ import annotations

import argparse
import copy
import json
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

IDENTIFIER = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*$")
LOWER_IDENTIFIER = re.compile(r"^[a-z0-9][a-z0-9._-]*$")
SHA = re.compile(r"^[0-9a-f]{40}$")
REPO = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
EVENTS = {"started", "completed", "blocked", "resumed"}
FIELDS = {
    "schema_version", "run_id", "unit_id", "item", "sequence", "phase_order", "phase",
    "event", "at", "actor", "model", "source", "evidence", "actual_minutes",
    "historical_durations_minutes", "historical_average_minutes", "token_usage", "tooling",
}


class InvalidLog(ValueError):
    pass


def fail(message: str) -> None:
    raise InvalidLog(message)


def utc(value: object, line: int) -> datetime:
    if not isinstance(value, str) or not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z", value):
        fail(f"line {line}: at must be canonical UTC YYYY-MM-DDTHH:MM:SSZ")
    try:
        return datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc)
    except ValueError as error:
        fail(f"line {line}: invalid at timestamp: {error}")


def nonempty(value: object, label: str, line: int) -> str:
    if not isinstance(value, str) or not value.strip():
        fail(f"line {line}: {label} must be a non-empty string")
    return value


def validate_tokens(value: object, terminal: bool, line: int) -> None:
    if not isinstance(value, dict) or not isinstance(value.get("status"), str):
        fail(f"line {line}: token_usage must be an object with status")
    status = value["status"]
    if not terminal:
        if value != {"status": "pending"}:
            fail(f"line {line}: started/resumed token_usage must be exactly pending")
        return
    if status == "pending":
        if value != {"status": "pending"}:
            fail(f"line {line}: pending token_usage must contain only status")
    elif status == "measured":
        expected = {"status", "input", "cached_input", "cache_write_input", "output", "reasoning",
                    "total", "source", "session_ids", "turn_ids"}
        if set(value) != expected:
            fail(f"line {line}: measured token_usage has missing or unexpected fields")
        counts = [value[name] for name in ("input", "cached_input", "cache_write_input", "output",
                                           "reasoning", "total")]
        if any(isinstance(count, bool) or not isinstance(count, int) or count < 0 for count in counts):
            fail(f"line {line}: measured token counts must be non-negative integers")
        if value["total"] != value["input"] + value["output"]:
            fail(f"line {line}: measured token total must equal input + output")
        if value["cached_input"] + value["cache_write_input"] > value["input"]:
            fail(f"line {line}: measured cache counts exceed input")
        if value["reasoning"] > value["output"]:
            fail(f"line {line}: measured reasoning exceeds output")
        nonempty(value["source"], "token_usage.source", line)
        for field in ("session_ids", "turn_ids"):
            identifiers = value[field]
            if not isinstance(identifiers, list) or not identifiers or any(
                    not isinstance(item, str) or not item for item in identifiers):
                fail(f"line {line}: measured {field} must be a non-empty string array")
    elif status == "unavailable":
        if set(value) != {"status", "reason", "source"}:
            fail(f"line {line}: unavailable token_usage has missing or unexpected fields")
        nonempty(value["reason"], "token_usage.reason", line)
        nonempty(value["source"], "token_usage.source", line)
    else:
        fail(f"line {line}: terminal token_usage must be pending, measured, or unavailable; estimates are forbidden")


def validate_model(value: object, line: int) -> None:
    if not isinstance(value, dict) or not isinstance(value.get("status"), str):
        fail(f"line {line}: model must be an object with status")
    if value["status"] == "recorded":
        if set(value) not in ({"status", "provider", "name", "source"},
                              {"status", "provider", "name", "effort", "source"}):
            fail(f"line {line}: recorded model has missing or unexpected fields")
        nonempty(value["provider"], "model.provider", line)
        nonempty(value["name"], "model.name", line)
        nonempty(value["source"], "model.source", line)
    elif value["status"] == "unavailable":
        if set(value) != {"status", "reason", "source"}:
            fail(f"line {line}: unavailable model has missing or unexpected fields")
        nonempty(value["reason"], "model.reason", line)
        nonempty(value["source"], "model.source", line)
    else:
        fail(f"line {line}: model status must be recorded or unavailable; inference is forbidden")


def validate_tooling(value: object, line: int) -> None:
    expected = {"ledger_schema", "runtime", "coordination", "sdd", "contracts"}
    if not isinstance(value, dict) or set(value) != expected or value.get("ledger_schema") != 1:
        fail(f"line {line}: tooling must contain ledger_schema 1 and all four tool components")
    for component in ("runtime", "coordination", "sdd", "contracts"):
        item = value[component]
        if not isinstance(item, dict) or not isinstance(item.get("status"), str):
            fail(f"line {line}: tooling.{component} must be a status object")
        status = item["status"]
        if status == "recorded":
            if set(item) != {"status", "name", "version", "source"}:
                fail(f"line {line}: recorded tooling.{component} has missing or unexpected fields")
            nonempty(item["name"], f"tooling.{component}.name", line)
            nonempty(item["version"], f"tooling.{component}.version", line)
            nonempty(item["source"], f"tooling.{component}.source", line)
        elif status in {"unavailable", "not_applicable"}:
            if set(item) != {"status", "name", "reason", "source"}:
                fail(f"line {line}: {status} tooling.{component} has missing or unexpected fields")
            nonempty(item["name"], f"tooling.{component}.name", line)
            nonempty(item["reason"], f"tooling.{component}.reason", line)
            nonempty(item["source"], f"tooling.{component}.source", line)
        else:
            fail(f"line {line}: tooling.{component}.status is invalid")


def validate_source(value: object, line: int) -> None:
    if not isinstance(value, dict):
        fail(f"line {line}: source must be an object")
    repository = nonempty(value.get("repository"), "source.repository", line)
    if not REPO.fullmatch(repository):
        fail(f"line {line}: source.repository must be owner/repo")
    has_revision = "revision" in value
    has_reason = "unavailable_reason" in value
    if has_revision == has_reason:
        fail(f"line {line}: source must contain exactly one of revision or unavailable_reason")
    expected = {"repository", "revision"} if has_revision else {"repository", "unavailable_reason"}
    if set(value) != expected:
        fail(f"line {line}: source has unexpected fields")
    if has_revision:
        revision = value["revision"]
        if not isinstance(revision, str) or not SHA.fullmatch(revision):
            fail(f"line {line}: source.revision must be a lowercase 40-hex commit")
    else:
        nonempty(value["unavailable_reason"], "source.unavailable_reason", line)


def validate_lines(records: list[object], run_id: str, unit_id: str,
                   require_terminal: bool = False, require_reconciled: bool = False,
                   required_phases: list[str] | None = None) -> None:
    if not records:
        fail("log is empty")
    if not LOWER_IDENTIFIER.fullmatch(run_id):
        fail("run id must be lowercase and path-safe")
    if not IDENTIFIER.fullmatch(unit_id):
        fail("unit id must be path-safe")

    item_identity: tuple[str, int, str] | None = None
    phases: dict[str, dict[str, object]] = {}
    active: str | None = None
    blocked: str | None = None
    previous_at: datetime | None = None

    for index, raw in enumerate(records, 1):
        if not isinstance(raw, dict):
            fail(f"line {index}: entry must be a JSON object")
        if set(raw) != FIELDS:
            fail(f"line {index}: entry has missing or unexpected fields")
        if raw["schema_version"] != 1:
            fail(f"line {index}: schema_version must be 1")
        if raw["run_id"] != run_id or raw["unit_id"] != unit_id:
            fail(f"line {index}: run_id/unit_id does not match validator arguments")
        if raw["sequence"] != index:
            fail(f"line {index}: sequence must be contiguous and equal line number")

        item = raw["item"]
        if not isinstance(item, dict) or set(item) != {"repo", "number", "url"}:
            fail(f"line {index}: item must contain exactly repo, number, and url")
        repo = item["repo"]
        number = item["number"]
        url = item["url"]
        if not isinstance(repo, str) or not REPO.fullmatch(repo):
            fail(f"line {index}: item.repo must be owner/repo")
        if isinstance(number, bool) or not isinstance(number, int) or number <= 0:
            fail(f"line {index}: item.number must be a positive integer")
        if url != f"https://github.com/{repo}/issues/{number}":
            fail(f"line {index}: item.url must be the canonical GitHub issue URL")
        current_item = (repo, number, url)
        if item_identity is None:
            item_identity = current_item
        elif item_identity != current_item:
            fail(f"line {index}: item identity changed within the ledger")

        phase = raw["phase"]
        event = raw["event"]
        order = raw["phase_order"]
        if not isinstance(phase, str) or not LOWER_IDENTIFIER.fullmatch(phase):
            fail(f"line {index}: phase must be a lowercase path-safe identifier")
        if event not in EVENTS:
            fail(f"line {index}: unknown event")
        nonempty(raw["actor"], "actor", index)
        validate_model(raw["model"], index)
        validate_tooling(raw["tooling"], index)
        validate_source(raw["source"], index)
        evidence = raw["evidence"]
        if not isinstance(evidence, list) or not evidence or any(not isinstance(v, str) or not v.strip() for v in evidence):
            fail(f"line {index}: evidence must be a non-empty string array")

        timestamp = utc(raw["at"], index)
        if previous_at is not None and timestamp < previous_at:
            fail(f"line {index}: timestamps must be nondecreasing")
        previous_at = timestamp

        terminal_event = event in {"completed", "blocked"}
        validate_tokens(raw["token_usage"], terminal_event, index)
        history = raw["historical_durations_minutes"]
        average = raw["historical_average_minutes"]
        if not isinstance(history, list) or any(isinstance(v, bool) or not isinstance(v, int) or v < 0 for v in history):
            fail(f"line {index}: historical durations must be non-negative whole minutes")
        if event != "completed" and (history or average is not None):
            fail(f"line {index}: only completed events may carry historical average evidence")
        if event == "completed":
            expected_average = None if not history else (2 * sum(history) + len(history)) // (2 * len(history))
            if average != expected_average:
                fail(f"line {index}: historical_average_minutes does not match its basis")

        if event == "started":
            if phase in phases:
                fail(f"line {index}: phase may be started only once")
            if active is not None or blocked is not None:
                fail(f"line {index}: another phase is active or blocked")
            expected_order = len(phases) + 1
            if order != expected_order:
                fail(f"line {index}: phase_order must be contiguous in first-seen order")
            if raw["actual_minutes"] is not None:
                fail(f"line {index}: started actual_minutes must be null")
            phases[phase] = {"order": order, "status": "active", "started": timestamp, "model": raw["model"]}
            active = phase
        elif phase not in phases or phases[phase]["order"] != order:
            fail(f"line {index}: event references an unknown phase/order")
        elif phases[phase]["model"] != raw["model"]:
            fail(f"line {index}: model changed within one phase; start a distinct continuation phase")
        elif event == "resumed":
            if blocked != phase or active is not None or phases[phase]["status"] != "blocked":
                fail(f"line {index}: only the blocked phase may resume")
            if raw["actual_minutes"] is not None:
                fail(f"line {index}: resumed actual_minutes must be null")
            blocked = None
            active = phase
            phases[phase]["status"] = "active"
        else:
            if active != phase or phases[phase]["status"] != "active":
                fail(f"line {index}: only the active phase may {event}")
            elapsed = int((timestamp - phases[phase]["started"]).total_seconds())
            expected_minutes = (elapsed + 30) // 60
            actual = raw["actual_minutes"]
            if isinstance(actual, bool) or not isinstance(actual, int) or actual != expected_minutes:
                fail(f"line {index}: actual_minutes must equal rounded elapsed wall time ({expected_minutes})")
            active = None
            phases[phase]["status"] = event
            if event == "blocked":
                blocked = phase

    required = required_phases or []
    missing = [phase for phase in required if phase not in phases]
    if missing:
        fail("missing required phases: " + ", ".join(missing))
    if require_terminal:
        if active is not None or blocked is not None:
            fail("terminal log must have no active or blocked phase")
        incomplete = [name for name, value in phases.items() if value["status"] != "completed"]
        if incomplete:
            fail("terminal log has incomplete phases: " + ", ".join(incomplete))
    if require_reconciled:
        pending = [raw["phase"] for raw in records if isinstance(raw, dict)
                   and raw.get("event") in {"completed", "blocked"}
                   and raw.get("token_usage") == {"status": "pending"}]
        if pending:
            fail("terminal token usage still pending reconciliation: " + ", ".join(pending))


def load(path: Path) -> list[object]:
    records: list[object] = []
    for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip():
            fail(f"line {line_number}: blank lines are not allowed")
        try:
            records.append(json.loads(line))
        except json.JSONDecodeError as error:
            fail(f"line {line_number}: invalid JSON: {error.msg}")
    return records


def valid_fixture() -> list[object]:
    item = {"repo": "FS-GG/.github", "number": 42, "url": "https://github.com/FS-GG/.github/issues/42"}
    source = {"repository": "FS-GG/.github", "revision": "a" * 40}
    common = {"schema_version": 1, "run_id": "roadmap-v2", "unit_id": "GS2-01.1", "item": item,
              "actor": "worker-1234", "model": {"status": "recorded", "provider": "OpenAI",
              "name": "gpt-test", "source": "runtime receipt"}, "source": source,
              "tooling": {"ledger_schema": 1,
                           "runtime": {"status": "recorded", "name": "codex", "version": "1.2.3", "source": "session"},
                           "coordination": {"status": "recorded", "name": "fsgg-coord", "version": "4.5.6", "source": "cli"},
                           "sdd": {"status": "recorded", "name": "fsgg-sdd", "version": "7.8.9", "source": "cli"},
                           "contracts": {"status": "recorded", "name": "fsgg-contracts", "version": "10.0.0", "source": "registry"}},
              "historical_average_minutes": None}
    return [
        {**common, "sequence": 1, "phase_order": 1, "phase": "claim", "event": "started",
         "at": "2026-09-04T08:00:00Z", "evidence": ["issue URL"], "actual_minutes": None,
         "historical_durations_minutes": [], "token_usage": {"status": "pending"}},
        {**common, "sequence": 2, "phase_order": 1, "phase": "claim", "event": "completed",
         "at": "2026-09-04T08:01:29Z", "evidence": ["claim receipt"], "actual_minutes": 1,
         "historical_durations_minutes": [],
         "token_usage": {"status": "measured", "input": 10, "cached_input": 4,
                         "cache_write_input": 0, "output": 5, "reasoning": 2, "total": 15,
                         "source": "provider receipt", "session_ids": ["session-1"],
                         "turn_ids": ["turn-1"]}},
        {**common, "sequence": 3, "phase_order": 2, "phase": "implement", "event": "started",
         "at": "2026-09-04T08:01:29Z", "evidence": ["commit base"], "actual_minutes": None,
         "historical_durations_minutes": [], "token_usage": {"status": "pending"}},
        {**common, "sequence": 4, "phase_order": 2, "phase": "implement", "event": "completed",
         "at": "2026-09-04T08:04:00Z", "evidence": ["green tests"], "actual_minutes": 3,
         "historical_durations_minutes": [2, 3], "historical_average_minutes": 3,
         "token_usage": {"status": "unavailable", "reason": "host exposes no phase counters", "source": "host usage API"}},
    ]


def self_test() -> None:
    base = valid_fixture()
    validate_lines(base, "roadmap-v2", "GS2-01.1", True, True, ["claim", "implement"])
    mutations = {
        "sequence gap": lambda rows: rows[2].__setitem__("sequence", 4),
        "wrong issue URL": lambda rows: rows[0]["item"].__setitem__("url", "https://example.invalid/42"),
        "overlapping phase": lambda rows: rows[2].__setitem__("at", "2026-09-04T08:00:30Z"),
        "wrong duration": lambda rows: rows[1].__setitem__("actual_minutes", 9),
        "wrong average": lambda rows: rows[3].__setitem__("historical_average_minutes", 2),
        "wrong token total": lambda rows: rows[1]["token_usage"].__setitem__("total", 99),
        "estimated tokens": lambda rows: rows[1]["token_usage"].__setitem__("status", "estimated"),
        "inferred model": lambda rows: rows[1]["model"].__setitem__("status", "inferred"),
        "missing tool version": lambda rows: rows[1]["tooling"]["sdd"].__setitem__("version", ""),
        "model changed in phase": lambda rows: rows[1].__setitem__("model", {"status": "recorded", "provider": "OpenAI", "name": "other", "source": "runtime receipt"}),
        "bad revision": lambda rows: rows[0]["source"].__setitem__("revision", "HEAD"),
        "empty evidence": lambda rows: rows[0].__setitem__("evidence", []),
        "phase order gap": lambda rows: rows[2].__setitem__("phase_order", 3),
        "active terminal": lambda rows: rows.pop(),
        "missing required phase": lambda rows: None,
    }
    for name, mutate in mutations.items():
        rows = copy.deepcopy(base)
        mutate(rows)
        try:
            required = ["claim", "implement", "acceptance"] if name == "missing required phase" else ["claim", "implement"]
            validate_lines(rows, "roadmap-v2", "GS2-01.1", True, True, required)
        except InvalidLog:
            continue
        fail(f"self-test mutation was accepted: {name}")
    print(f"lifecycle-log self-test: pass ({len(mutations)} rejection cases)")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".")
    parser.add_argument("--run")
    parser.add_argument("--unit")
    parser.add_argument("--log")
    parser.add_argument("--require-terminal", action="store_true")
    parser.add_argument("--require-reconciled", action="store_true")
    parser.add_argument("--required-phase", action="append", default=[])
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    try:
        if args.self_test:
            self_test()
            return 0
        if not args.run or not args.unit or not args.log:
            fail("--run, --unit, and --log are required unless --self-test is used")
        root = Path(args.root).resolve()
        path = (root / args.log).resolve()
        try:
            relative = path.relative_to(root)
        except ValueError:
            fail("log path escapes root")
        parts = relative.parts
        if len(parts) < 5 or parts[:2] != ("logs", "roadmap") or parts[-2] != args.run or parts[-1] != f"{args.unit}.jsonl":
            fail("log path must be logs/roadmap/<roadmap-slug>/<run-id>/<unit-id>.jsonl")
        if not path.is_file():
            fail(f"log does not exist: {relative}")
        tracked = subprocess.run(["git", "-C", str(root), "ls-files", "--error-unmatch", str(relative)],
                                 text=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if tracked.returncode != 0:
            fail(f"log is not tracked: {relative}")
        validate_lines(load(path), args.run, args.unit, args.require_terminal, args.require_reconciled,
                       args.required_phase)
        state = "terminal" if args.require_terminal else "valid"
        print(f"lifecycle-log: {state} — {relative}")
        return 0
    except (InvalidLog, OSError) as error:
        print(f"lifecycle-log: invalid — {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
