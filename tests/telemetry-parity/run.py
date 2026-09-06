#!/usr/bin/env python3
"""Stage-C black-box gate over the frozen helper corpus (#3208)."""

from __future__ import annotations

import copy
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.dont_write_bytecode = True
ENGINE = Path(os.environ["FSGG_COORD_ENGINE_BIN"])
FIXTURES = ROOT / "tests/telemetry-parity/fixtures"


def helper_absence() -> None:
    removed = (
        "collect-runtime-" + "usage.py",
        "validate-lifecycle-" + "log.py",
        "validate-critique-" + "state.py",
        "validate-feedback-" + "state.py",
    )
    live_roots = (
        ROOT / ".agents/skills",
        ROOT / ".claude/skills",
        ROOT / "src",
        ROOT / ".github/workflows",
    )
    findings: list[str] = []
    for live_root in live_roots:
        for path in live_root.rglob("*"):
            if not path.is_file():
                continue
            relative = path.relative_to(ROOT).as_posix()
            if {"bin", "obj"} & set(path.relative_to(live_root).parts):
                continue
            if any(name in relative for name in removed):
                findings.append(relative)
                continue
            try:
                text = path.read_text(encoding="utf-8")
            except UnicodeDecodeError:
                continue
            if any(name in text for name in removed):
                findings.append(relative)
    for manifest in (ROOT / "registry/driver-skill-manifest.json", ROOT / "registry/coordination-kit-skill-manifest.json"):
        text = manifest.read_text(encoding="utf-8")
        if any(name in text for name in removed):
            findings.append(manifest.relative_to(ROOT).as_posix())
    if findings:
        raise SystemExit("removed telemetry compatibility helper remains in a live/package surface: " + ", ".join(sorted(set(findings))))


def run(args: list[str], *, cwd: Path = ROOT, check: bool = False) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(args, cwd=cwd, text=True, capture_output=True)
    if check and result.returncode:
        raise SystemExit(f"failed ({result.returncode}): {' '.join(args)}\n{result.stderr}")
    return result


def engine(*args: str, cwd: Path = ROOT) -> subprocess.CompletedProcess[str]:
    return run([str(ENGINE), *args], cwd=cwd)


def expect_verdict(label: str, compiled: subprocess.CompletedProcess[str], expected: bool) -> None:
    if (compiled.returncode == 0) != expected:
        raise SystemExit(
            f"{label} frozen verdict failed: compiled={compiled.returncode}\n"
            f"compiled stderr={compiled.stderr}"
        )


def codex_session(path: Path) -> None:
    counts = {"input_tokens": 10, "cached_input_tokens": 4, "cache_write_input_tokens": 0,
              "output_tokens": 5, "reasoning_output_tokens": 2, "total_tokens": 15}
    rows = [
        {"timestamp": "2026-01-01T00:00:00Z", "type": "session_meta", "payload": {"cli_version": "1.2.3"}},
        {"timestamp": "2026-01-01T00:00:00Z", "type": "turn_context", "payload": {"turn_id": "turn-1", "model": "gpt-test", "effort": "high"}},
        {"timestamp": "2026-01-01T00:01:00Z", "type": "token_usage_record", "payload": {
            "thread_id": "thread-1", "turn_id": "turn-1", "session_id": "session-1",
            "response_id": "response-1", "usage": counts, "turn_token_usage": counts,
            "thread_token_usage": counts}},
    ]
    path.write_text("\n".join(json.dumps(row, separators=(",", ":")) for row in rows) + "\n", encoding="utf-8")


def canonical_digest(event: dict) -> str:
    payload = {key: value for key, value in event.items() if key != "digest"}
    return hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def seal(events: list[dict]) -> None:
    previous = None
    for index, event in enumerate(events, 1):
        event["sequence"] = index
        event["revision"] = index
        event["previous_digest"] = previous
        event["digest"] = canonical_digest(event)
        previous = event["digest"]


def collector_parity(directory: Path) -> tuple[int, int, Path, Path]:
    session = directory / "session.jsonl"
    codex_session(session)
    snapshot = directory / "claude.json"
    snapshot.write_text(json.dumps({
        "timestamp": "2026-01-01T00:02:00Z", "session_id": "claude-session",
        "prompt_id": "prompt-1", "version": "2.3.4", "model": {"id": "claude-test"},
        "effort": {"level": "high"}, "context_window": {"current_usage": {
            "input_tokens": 7, "cache_read_input_tokens": 2,
            "cache_creation_input_tokens": 1, "output_tokens": 3}},
    }, separators=(",", ":")), encoding="utf-8")
    receipt_store = directory / "durable-state" / "usage"
    common = ["--task", "repo#1/claim", "--coord-version", "4.5.6", "--sdd-version", "7.8.9", "--contracts-version", "10.0.0", "--receipt-store", str(receipt_store)]
    runtimes = [("codex", "--session-file", session), ("claude", "--snapshot", snapshot)]
    positive = 0
    frozen = {
        "codex": {
            "response_id": "response-1", "source": "codex-session-jsonl:sha256:bea5edd51a341797657e24c0fec9d0f4519ea6e543823042fb858d3fce0ae750",
            "provider": "OpenAI", "model": "gpt-test", "runtime_version": "1.2.3", "input": 10,
            "cached_input": 4, "cache_write_input": 0, "output": 5, "reasoning": 2, "total": 15,
        },
        "claude": {
            "response_id": "claude-a1130b5757513393219ca24fd4146dd62ff53576907763cf13ff39f88fc021a0",
            "source": "claude-statusline-json:sha256:f0f4542d369b3608ea1b99150bed10b5128fd0f830631f5056e29a1abc70adb0",
            "provider": "Anthropic", "model": "claude-test", "runtime_version": "2.3.4", "input": 10,
            "cached_input": 2, "cache_write_input": 1, "output": 3, "reasoning": "", "total": 13,
        },
    }
    for runtime, source_flag, source in runtimes:
        for output_format in ("csv", "json"):
            args = [runtime, source_flag, str(source), *common, "--format", output_format]
            compiled = engine("telemetry", "usage", "collect", *args)
            if compiled.returncode:
                raise SystemExit(f"collector {runtime}/{output_format} frozen positive failed\n{compiled.stderr}")
            expected = frozen[runtime]
            if output_format == "json":
                row = json.loads(compiled.stdout)
                for key, value in expected.items():
                    if row.get(key) != value:
                        raise SystemExit(f"collector {runtime} changed frozen {key}: {row.get(key)!r} != {value!r}")
                if json.dumps(row, sort_keys=True, separators=(",", ":")) + "\n" != compiled.stdout:
                    raise SystemExit(f"collector {runtime} JSON is not canonical")
            else:
                lines = compiled.stdout.splitlines()
                if len(lines) != 2 or lines[0] != "timestamp,task,session_id,thread_id,turn_id,response_id,provider,model,effort,runtime_version,coordination_version,sdd_version,contracts_version,ledger_schema,input,cached_input,cache_write_input,output,reasoning,total,turn_input,turn_cached_input,turn_cache_write_input,turn_output,turn_reasoning,turn_total,thread_input,thread_cached_input,thread_cache_write_input,thread_output,thread_reasoning,thread_total,source":
                    raise SystemExit(f"collector {runtime} changed the frozen CSV contract")
                if expected["response_id"] not in lines[1] or expected["source"] not in lines[1]:
                    raise SystemExit(f"collector {runtime} CSV lost frozen identity bindings")
            positive += 1

            compiled_path = directory / "compiled" / runtime / output_format / "nested" / f"usage.{output_format}l"
            append_args = [str(ENGINE), "telemetry", "usage", "collect", *args, "--append", str(compiled_path)]
            run(append_args, check=True)
            first = compiled_path.read_bytes()
            run(append_args, check=True)
            if compiled_path.read_bytes() != first:
                raise SystemExit(f"collector {runtime}/{output_format} append is not idempotent")
            positive += 1

    bad_lines = session.read_text(encoding="utf-8").splitlines()
    bad = json.loads(bad_lines[-1])
    bad["payload"]["turn_token_usage"]["total_tokens"] = 99
    bad_lines[-1] = json.dumps(bad)
    invalid = directory / "bad-session.jsonl"
    invalid.write_text("\n".join(bad_lines) + "\n", encoding="utf-8")
    args = ["codex", "--session-file", str(invalid), *common]
    expect_verdict("collector invalid arithmetic", engine("telemetry", "usage", "collect", *args), False)

    latest = json.loads(session.read_text(encoding="utf-8").splitlines()[-1])
    latest.pop("timestamp")
    latest["payload"]["response_id"] = "response-latest-without-timestamp"
    malformed_latest = directory / "malformed-latest-session.jsonl"
    malformed_latest.write_text(session.read_text(encoding="utf-8") + json.dumps(latest) + "\n", encoding="utf-8")
    args = ["codex", "--session-file", str(malformed_latest), *common]
    expect_verdict("collector malformed latest response cannot fall back to older response", engine("telemetry", "usage", "collect", *args), False)

    args = ["codex", "--session-file", str(session), *common, "--unknown-flag"]
    expect_verdict("collector unknown option", engine("telemetry", "usage", "collect", *args), False)
    receipts = sorted(receipt_store.rglob("*.csv"))
    if len(receipts) != 2:
        raise SystemExit(f"collector did not archive one canonical receipt per runtime: {receipts}")
    for receipt in receipts:
        source = "runtime-usage-csv:sha256:" + receipt.stem
        resolved = engine("telemetry", "usage", "resolve", "--source", source, "--receipt-store", str(receipt_store))
        if resolved.returncode or resolved.stdout.encode() != receipt.read_bytes():
            raise SystemExit("usage resolver did not return exact canonical bytes")
        positive += 1
    tampered = receipts[0]
    original = tampered.read_bytes()
    tampered.write_text("tampered", encoding="utf-8")
    source = "runtime-usage-csv:sha256:" + tampered.stem
    expect_verdict("usage resolver rejects canonical tampering", engine("telemetry", "usage", "resolve", "--source", source, "--receipt-store", str(receipt_store)), False)
    tampered.write_bytes(original)
    expect_verdict("usage archive rejects temporary retention", engine("telemetry", "usage", "archive", "--input", str(tampered), "--receipt-store", tempfile.gettempdir()), False)
    return positive, 5, session, receipt_store


def lifecycle_parity(directory: Path, session: Path, receipt_store: Path) -> tuple[int, int]:
    usage = directory / "usage.csv"
    collected = engine("telemetry", "usage", "collect", "codex", "--session-file", str(session),
                       "--task", "FS-GG/.github#42/claim", "--coord-version", "4.5.6",
                       "--sdd-version", "7.8.9", "--contracts-version", "10.0.0", "--receipt-store", str(receipt_store))
    if collected.returncode:
        raise SystemExit(collected.stderr)
    usage.write_text(collected.stdout, encoding="utf-8")
    source = "runtime-usage-csv:sha256:" + hashlib.sha256(usage.read_bytes()).hexdigest()
    base = [json.loads(line) for line in (FIXTURES / "lifecycle-valid.jsonl").read_text(encoding="utf-8").splitlines()]
    base[1]["token_usage"]["source"] = source
    seal(base)
    log = directory / "lifecycle.jsonl"
    log.write_text("\n".join(json.dumps(row, sort_keys=True, separators=(",", ":")) for row in base) + "\n", encoding="utf-8")

    shared = ["--run", "roadmap-v2", "--unit", "GS2-01.1", "--log", str(log), "--usage", str(usage),
              "--required-phase", "claim", "--required-phase", "implement", "--require-terminal"]
    expect_verdict("lifecycle validate", engine("telemetry", "lifecycle", "validate", *shared), True)
    positive, rejected = 1, 0
    auto_shared = [value for value in shared if value not in ("--usage", str(usage))]
    auto_shared.extend(["--receipt-store", str(receipt_store)])
    auto = engine("telemetry", "lifecycle", "validate", *auto_shared)
    expect_verdict("lifecycle resolves canonical receipt by digest", auto, True)
    if json.loads(auto.stdout).get("excludedUsageSources") != []:
        raise SystemExit("ordinary lifecycle validation unexpectedly excluded usage")
    positive += 1

    # A first-class black-box recovery: the frontier's measured receipt is intentionally
    # unavailable, the checkpoint replaces only that pre-frontier evidence obligation, and
    # the ordinary phase after the new anchor remains strict.
    extraordinary = copy.deepcopy(base)
    extraordinary[3]["token_usage"]["source"] = "runtime-usage-csv:sha256:" + "c" * 64
    seal(extraordinary)
    frontier = extraordinary[-1]

    def checkpoint_proof(repository: str = "FS-GG/.github", revision: int = 4,
                         digest: str = frontier["digest"], status: str = "passed") -> dict:
        proof = {
            "schema": "fsgg.telemetry.synthetic-checkpoint/v1",
            "scope": {"repository": repository, "issue": 42, "run_id": "roadmap-v2", "unit_id": "GS2-01.1"},
            "frontier": {"revision": revision, "digest": digest},
            "reason": "tool-version-incompatibility",
            "authorization": {"decision": "authorize-synthetic-checkpoint", "by": "human/accountable-owner",
                              "url": "https://github.com/FS-GG/.github/issues/42#issuecomment-123"},
            "missing_provenance_required": False,
            "reconstruct_missing_data": False,
            "functional_verification": [{"name": "functional-route", "status": status,
                                         "evidence": ["sha256:" + "b" * 64]}],
        }
        proof["digest"] = hashlib.sha256(json.dumps(proof, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
        return proof

    proof = checkpoint_proof()
    checkpoint_start = copy.deepcopy(base[2])
    checkpoint_start.update({"phase_order": 3, "phase": "synthetic-evidence-checkpoint", "event": "started",
                             "at": "2026-09-04T08:05:00Z", "evidence": ["synthetic-checkpoint:sha256:" + proof["digest"]],
                             "actual_minutes": None, "token_usage": {"status": "pending"}})
    checkpoint_complete = copy.deepcopy(base[3])
    checkpoint_complete.update({"phase_order": 3, "phase": "synthetic-evidence-checkpoint", "event": "completed",
                                "at": "2026-09-04T08:06:00Z", "evidence": ["checkpoint-complete"],
                                "actual_minutes": 1, "token_usage": {"status": "unavailable", "reason": "post-completion collector schema validation failed: total field missing", "source": "collector"}})
    normal_start = copy.deepcopy(checkpoint_start)
    normal_start.update({"phase_order": 4, "phase": "normal", "at": "2026-09-04T08:06:00Z", "evidence": ["normal"]})
    normal_complete = copy.deepcopy(checkpoint_complete)
    normal_complete.update({"phase_order": 4, "phase": "normal", "at": "2026-09-04T08:07:00Z", "evidence": ["normal-complete"]})
    extraordinary.extend([checkpoint_start, checkpoint_complete, normal_start, normal_complete])
    seal(extraordinary)
    extraordinary_log = directory / "lifecycle-synthetic-checkpoint.jsonl"
    extraordinary_log.write_text("\n".join(json.dumps(row, sort_keys=True, separators=(",", ":")) for row in extraordinary) + "\n", encoding="utf-8")
    proof_path = directory / "synthetic-checkpoint.json"
    proof_path.write_text(json.dumps(proof, sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")
    checkpoint_args = ["--run", "roadmap-v2", "--unit", "GS2-01.1", "--log", str(extraordinary_log),
                       "--synthetic-checkpoint", str(proof_path), "--required-phase", "normal",
                       "--require-terminal", "--require-reconciled"]
    recovered = engine("telemetry", "lifecycle", "validate", *checkpoint_args)
    expect_verdict("synthetic checkpoint resumes strict lifecycle", recovered, True)
    if json.loads(recovered.stdout).get("syntheticCheckpoint") != extraordinary[5]["digest"]:
        raise SystemExit("synthetic checkpoint did not expose its new trusted anchor")
    positive += 1
    expect_verdict("synthetic checkpoint requires authorization proof",
                   engine("telemetry", "lifecycle", "validate", *[arg for arg in checkpoint_args if arg not in ("--synthetic-checkpoint", str(proof_path))]), False)
    rejected += 1
    expect_verdict("synthetic checkpoint proof is one-time and non-ambiguous",
                   engine("telemetry", "lifecycle", "validate", *checkpoint_args, "--synthetic-checkpoint", str(proof_path)), False)
    rejected += 1

    wrong_scope = checkpoint_proof(repository="FS-GG/other")
    wrong_scope_path = directory / "synthetic-checkpoint-wrong-scope.json"
    wrong_scope_path.write_text(json.dumps(wrong_scope, sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")
    expect_verdict("synthetic checkpoint scope is exact",
                   engine("telemetry", "lifecycle", "validate", *[wrong_scope_path.as_posix() if arg == proof_path.as_posix() else arg for arg in checkpoint_args]), False)
    rejected += 1

    chain_fields = {"sequence", "revision", "previous_digest", "digest"}
    existing = directory / "existing.jsonl"
    existing.write_text(json.dumps(base[0], sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")
    draft = directory / "draft.json"
    draft.write_text(json.dumps({key: value for key, value in base[1].items() if key not in chain_fields},
                                sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")
    seal_args = ["--run", "roadmap-v2", "--unit", "GS2-01.1", "--existing", str(existing), "--usage", str(usage)]
    compiled = engine("telemetry", "lifecycle", "seal-successor", "--draft", str(draft), *seal_args)
    if compiled.returncode or json.loads(compiled.stdout) != base[1]:
        raise SystemExit(f"lifecycle seal frozen output failed\n{compiled.stdout!r} {compiled.stderr}")
    positive += 1

    def comment(event: dict, number: int, updated: bool = False) -> dict:
        body = "<!-- fsgg:item-lifecycle/v1 -->\n```json\n" + json.dumps(event, sort_keys=True, separators=(",", ":")) + "\n```\n"
        return {"id": number, "created_at": "2026-09-04T08:00:00Z",
                "updated_at": "2026-09-04T08:01:00Z" if updated else "2026-09-04T08:00:00Z", "body": body}

    comments = directory / "comments.json"
    comments.write_text(json.dumps([[comment(base[0], 1)]]), encoding="utf-8")
    export_args = ["--run", "roadmap-v2", "--unit", "GS2-01.1"]
    compiled = engine("telemetry", "lifecycle", "export-comments", *export_args, "--comments", str(comments))
    if compiled.returncode or compiled.stdout != json.dumps(base[0], sort_keys=True, separators=(",", ":")) + "\n":
        raise SystemExit("lifecycle export changed frozen output")
    positive += 1

    fork = copy.deepcopy(base[0])
    fork["actor"] = "critic-2"
    fork["digest"] = canonical_digest(fork)
    comments.write_text(json.dumps([[comment(base[0], 1), comment(fork, 2)]]), encoding="utf-8")
    compiled = engine("telemetry", "lifecycle", "export-comments", *export_args, "--comments", str(comments))
    if compiled.returncode or compiled.stdout != json.dumps(base[0], sort_keys=True, separators=(",", ":")) + "\n":
        raise SystemExit("lifecycle deterministic-fork export changed frozen election")
    positive += 1

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
        candidate = directory / f"lifecycle-negative-{rejected}.jsonl"
        candidate.write_text("\n".join(json.dumps(row, sort_keys=True, separators=(",", ":")) for row in rows) + "\n", encoding="utf-8")
        phases = ["claim", "implement", "acceptance"] if name == "missing required phase" else ["claim", "implement"]
        phase_args = [value for phase in phases for value in ("--required-phase", phase)]
        args = ["--run", "roadmap-v2", "--unit", "GS2-01.1", "--log", str(candidate), "--usage", str(usage), *phase_args, "--require-terminal"]
        expect_verdict(f"lifecycle rejection: {name}", engine("telemetry", "lifecycle", "validate", *args), False)
        rejected += 1

    provenance = {
        "forged token report join": lambda rows: rows[1]["token_usage"].update({"input": 1_000_000_999, "total": 1_000_001_004}),
        "forged tooling version": lambda rows: rows[1]["tooling"]["sdd"].update({"version": "999.999.999"}),
        "invented historical corpus": lambda rows: rows[3].update({"historical_durations_minutes": [999], "historical_average_minutes": 999}),
        "extra source member": lambda rows: rows[0]["source"].update({"unexpected": "must-be-rejected"}),
        "blank source unavailable reason": lambda rows: rows[0].__setitem__("source", {"repository": "FS-GG/.github", "unavailable_reason": ""}),
        "blank tooling unavailable reason": lambda rows: rows[0]["tooling"].__setitem__("sdd", {"status": "unavailable", "name": "fsgg-sdd", "reason": "", "source": "runtime"}),
    }
    for name, mutate in provenance.items():
        rows = copy.deepcopy(base)
        mutate(rows)
        seal(rows)
        candidate = directory / f"lifecycle-provenance-{rejected}.jsonl"
        candidate.write_text("\n".join(json.dumps(row, sort_keys=True, separators=(",", ":")) for row in rows) + "\n", encoding="utf-8")
        args = ["--run", "roadmap-v2", "--unit", "GS2-01.1", "--log", str(candidate), "--usage", str(usage),
                "--required-phase", "claim", "--required-phase", "implement", "--require-terminal"]
        expect_verdict(f"lifecycle rejection: {name}", engine("telemetry", "lifecycle", "validate", *args), False)
        rejected += 1

    invalid_forks = []
    bad_digest = copy.deepcopy(fork); bad_digest["digest"] = "f" * 64; invalid_forks.append((bad_digest, False))
    alternate = copy.deepcopy(fork); alternate["previous_digest"] = "e" * 64; alternate["digest"] = canonical_digest(alternate); invalid_forks.append((alternate, False))
    missing_actor = copy.deepcopy(fork); del missing_actor["actor"]; missing_actor["digest"] = canonical_digest(missing_actor); invalid_forks.append((missing_actor, False))
    invalid_forks.append((base[0], True))
    for event, edited in invalid_forks:
        comments.write_text(json.dumps([[comment(base[0], 1), comment(event, 2, edited)]]), encoding="utf-8")
        expect_verdict("lifecycle rejected fork/comment",
                       engine("telemetry", "lifecycle", "export-comments", *export_args, "--comments", str(comments)), False)
        rejected += 1
    return positive, rejected


def critique_parity(directory: Path) -> tuple[int, int]:
    positive = 0
    for artifact in sorted((ROOT / "reviews/roadmap").glob("*.json")):
        data = json.loads(artifact.read_text(encoding="utf-8"))
        cycle, head = data.get("cycle_id"), data.get("confirmation", {}).get("reviewed_commit")
        if not isinstance(cycle, str) or not isinstance(head, str):
            continue
        compiled = engine("telemetry", "critique", "validate", "--cycle", cycle, "--head", head, "--artifact", str(artifact))
        expect_verdict(f"critique positive {artifact.name}", compiled, True)
        positive += 1

    source = next(path for path in sorted((ROOT / "reviews/roadmap").glob("*.json")) if json.loads(path.read_text())["findings"])
    base = json.loads(source.read_text(encoding="utf-8"))
    cycle = base["cycle_id"]
    head = base["confirmation"]["reviewed_commit"]
    relative = Path("reviews/roadmap") / f"{cycle}.json"
    target = directory / relative
    target.parent.mkdir(parents=True)
    mutations = {
        "missing finding evidence": lambda data: data["findings"][0].pop("evidence"),
        "empty finding evidence": lambda data: data["findings"][0].__setitem__("evidence", []),
        "wrong cycle": lambda data: data.__setitem__("cycle_id", "roadmap-parity-m1-wrong"),
        "wrong confirmation head": lambda data: data["confirmation"].__setitem__("reviewed_commit", "f" * 40),
        "repair rounds mismatch": lambda data: data.__setitem__("repair_rounds", data["repair_rounds"] + 1),
        "invalid player journey": lambda data: data.update({"game_functionality": True, "entry_point_not_test_ownable": False,
                                                              "player_journeys": [{"functionality": "x", "entry_point": "seeded-state", "input_surface": "direct-msg", "reached": True, "evidence": ["x"]}]}),
        "unresolved confirmation": lambda data: data["confirmation"].__setitem__("unresolved_blocker_major", ["x"]),
    }
    rejected = 0
    for name, mutate in mutations.items():
        candidate = copy.deepcopy(base)
        mutate(candidate)
        target.write_text(json.dumps(candidate), encoding="utf-8")
        expect_verdict(f"critique rejection: {name}",
                       engine("telemetry", "critique", "validate", "--cycle", cycle, "--head", head, "--artifact", str(relative), cwd=directory), False)
        rejected += 1
    return positive, rejected


def feedback_parity(directory: Path) -> tuple[int, int]:
    positive = 0
    for report in sorted((ROOT / "feedback").glob("*.md")):
        text = report.read_text(encoding="utf-8")
        cycle = re.search(r"(?m)^cycle:\s*(\S+)\s*$", text)
        phases = re.search(r"(?m)^- \*\*phases:\*\*\s*(.+?)\s*$", text)
        audit = ROOT / "feedback/audits" / (report.stem + ".audit.json")
        if not cycle or not phases or not audit.is_file():
            continue
        phase_value = phases.group(1)
        compiled = engine("telemetry", "feedback", "validate", "--cycle", cycle.group(1), "--report", str(report.relative_to(ROOT)), "--audit", str(audit.relative_to(ROOT)), "--phases", phase_value)
        expect_verdict(f"feedback positive {report.name}", compiled, True)
        positive += 1

    cycle = "roadmap-parity-m1-feedback"
    report_rel = Path("feedback/parity.md")
    audit_rel = Path("feedback/audits/parity.audit.json")
    checkpoint_rel = Path("feedback/checkpoints") / f"{cycle}.jsonl"
    report_path, audit_path, checkpoint_path = directory / report_rel, directory / audit_rel, directory / checkpoint_rel
    report_path.parent.mkdir(parents=True, exist_ok=True)
    audit_path.parent.mkdir(parents=True, exist_ok=True)
    checkpoint_path.parent.mkdir(parents=True, exist_ok=True)

    def report_text(*, report_cycle: str = cycle, phases: str = "claim, implementation", events: int = 0, reason: str = "exercised surfaces produced no material event") -> str:
        return (f"---\nfeedbackSchema: 2\ncycle: {report_cycle}\n---\n"
                "## §1 Provenance and confidence\n- **activation:** active\n"
                f"- **phases:** {phases}\n- **material events:** {events}\n- **zero-event reason:** {reason}\n"
                "## §2 Findings\nNone.\n")

    def write_case(text: str, checkpoint: str | None, *, bad_audit: bool = False) -> None:
        report_path.write_text(text, encoding="utf-8")
        digest = "0" * 64 if bad_audit else hashlib.sha256(text.encode()).hexdigest()
        audit_path.write_text(json.dumps({"auditSchema": 1, "report": str(report_rel).replace("\\", "/"),
                                          "reportSha256": digest, "findings": []}), encoding="utf-8")
        if checkpoint is None:
            if checkpoint_path.exists():
                checkpoint_path.unlink()
        else:
            checkpoint_path.write_text(checkpoint, encoding="utf-8")

    def verdict(label: str, expected: bool) -> None:
        common = ["--cycle", cycle, "--report", str(report_rel), "--audit", str(audit_rel), "--phases", "claim, implementation"]
        compiled_args = ["telemetry", "feedback", "validate", *common]
        if checkpoint_path.exists():
            compiled_args.extend(["--checkpoint", str(checkpoint_rel)])
        expect_verdict(label, engine(*compiled_args, cwd=directory), expected)

    write_case(report_text(), None)
    verdict("feedback synthetic zero-event positive", True)
    positive += 1
    write_case(report_text(events=1, reason="n/a"), json.dumps({"cycle": cycle}) + "\n")
    verdict("feedback synthetic material-event positive", True)
    positive += 1

    cases = [
        ("wrong report cycle", report_text(report_cycle="roadmap-parity-m1-wrong"), None, False),
        ("bad audit digest", report_text(), None, True),
        ("wrong phases", report_text(phases="implementation, claim"), None, False),
        ("zero event with checkpoint", report_text(), json.dumps({"cycle": cycle}) + "\n", False),
        ("invalid checkpoint JSON", report_text(events=1, reason="n/a"), "not-json\n", False),
        ("wrong checkpoint cycle", report_text(events=1, reason="n/a"), json.dumps({"cycle": "other"}) + "\n", False),
        ("blank checkpoint row", report_text(events=1, reason="n/a"), "\n", False),
    ]
    rejected = 0
    for name, text_value, checkpoint, bad_audit in cases:
        write_case(text_value, checkpoint, bad_audit=bad_audit)
        verdict(f"feedback rejection: {name}", False)
        rejected += 1
    return positive, rejected


def main() -> int:
    helper_absence()
    positive = rejected = 0
    test_state = Path.home() / ".local" / "state" / "fsgg" / "test-runs"
    test_state.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="fsgg-3208-parity-", dir=test_state) as path:
        directory = Path(path)
        p, r, session, receipt_store = collector_parity(directory); positive += p; rejected += r
        p, r = lifecycle_parity(directory, session, receipt_store); positive += p; rejected += r
        p, r = critique_parity(directory / "critique"); positive += p; rejected += r
        p, r = feedback_parity(directory / "feedback-root"); positive += p; rejected += r
    print(f"telemetry-parity: pass ({positive} positive and {rejected} rejection cases across the full frozen helper corpus)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
