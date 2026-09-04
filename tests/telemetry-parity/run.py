#!/usr/bin/env python3
"""Stage-A black-box differential gate for every frozen helper surface (#3208)."""

from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.dont_write_bytecode = True
ENGINE = ROOT / "src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine.dll"
WORK = ROOT / ".agents/skills/work-roadmap/scripts"
PNEXT = ROOT / ".agents/skills/pnext-item/scripts"


def run(args: list[str], *, cwd: Path = ROOT, check: bool = False) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(args, cwd=cwd, text=True, capture_output=True)
    if check and result.returncode:
        raise SystemExit(f"failed ({result.returncode}): {' '.join(args)}\n{result.stderr}")
    return result


def engine(*args: str, cwd: Path = ROOT) -> subprocess.CompletedProcess[str]:
    return run(["dotnet", str(ENGINE), *args], cwd=cwd)


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def same_verdict(label: str, python: subprocess.CompletedProcess[str], compiled: subprocess.CompletedProcess[str], expected: bool) -> None:
    if (python.returncode == 0, compiled.returncode == 0) != (expected, expected):
        raise SystemExit(
            f"{label} differential verdict failed: python={python.returncode}, compiled={compiled.returncode}\n"
            f"python stderr={python.stderr}\ncompiled stderr={compiled.stderr}"
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


def collector_parity(directory: Path) -> tuple[int, int, Path]:
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
    common = ["--task", "repo#1/claim", "--coord-version", "4.5.6", "--sdd-version", "7.8.9", "--contracts-version", "10.0.0"]
    runtimes = [("codex", "--session-file", session), ("claude", "--snapshot", snapshot)]
    positive = 0
    for runtime, source_flag, source in runtimes:
        for output_format in ("csv", "json"):
            args = [runtime, source_flag, str(source), *common, "--format", output_format]
            python = run(["python3", str(PNEXT / "collect-runtime-usage.py"), *args], check=True)
            compiled = engine("telemetry", "usage", "collect", *args)
            if compiled.returncode or compiled.stdout != python.stdout:
                raise SystemExit(f"collector {runtime}/{output_format} parity failed\npython={python.stdout!r}\ncompiled={compiled.stdout!r}\n{compiled.stderr}")
            positive += 1

            python_path = directory / "python" / runtime / output_format / "nested" / f"usage.{output_format}l"
            compiled_path = directory / "compiled" / runtime / output_format / "nested" / f"usage.{output_format}l"
            for target, prefix in ((python_path, ["python3", str(PNEXT / "collect-runtime-usage.py")]),
                                   (compiled_path, ["dotnet", str(ENGINE), "telemetry", "usage", "collect"])):
                append_args = [*prefix, *args, "--append", str(target)]
                run(append_args, check=True)
                run(append_args, check=True)
            if python_path.read_bytes() != compiled_path.read_bytes():
                raise SystemExit(f"collector {runtime}/{output_format} append parity failed")
            positive += 1

    bad_lines = session.read_text(encoding="utf-8").splitlines()
    bad = json.loads(bad_lines[-1])
    bad["payload"]["turn_token_usage"]["total_tokens"] = 99
    bad_lines[-1] = json.dumps(bad)
    invalid = directory / "bad-session.jsonl"
    invalid.write_text("\n".join(bad_lines) + "\n", encoding="utf-8")
    args = ["codex", "--session-file", str(invalid), *common]
    same_verdict("collector invalid arithmetic",
                 run(["python3", str(PNEXT / "collect-runtime-usage.py"), *args]),
                 engine("telemetry", "usage", "collect", *args), False)

    latest = json.loads(session.read_text(encoding="utf-8").splitlines()[-1])
    latest.pop("timestamp")
    latest["payload"]["response_id"] = "response-latest-without-timestamp"
    malformed_latest = directory / "malformed-latest-session.jsonl"
    malformed_latest.write_text(session.read_text(encoding="utf-8") + json.dumps(latest) + "\n", encoding="utf-8")
    args = ["codex", "--session-file", str(malformed_latest), *common]
    same_verdict("collector malformed latest response cannot fall back to older response",
                 run(["python3", str(PNEXT / "collect-runtime-usage.py"), *args]),
                 engine("telemetry", "usage", "collect", *args), False)
    return positive, 2, session


def lifecycle_parity(directory: Path, session: Path) -> tuple[int, int]:
    oracle = load("lifecycle_oracle", PNEXT / "validate-lifecycle-log.py")
    usage = directory / "usage.csv"
    collected = run(["python3", str(PNEXT / "collect-runtime-usage.py"), "codex", "--session-file", str(session),
                     "--task", "FS-GG/.github#42/claim", "--coord-version", "4.5.6",
                     "--sdd-version", "7.8.9", "--contracts-version", "10.0.0"], check=True)
    usage.write_text(collected.stdout, encoding="utf-8")
    source = "runtime-usage-csv:sha256:" + hashlib.sha256(usage.read_bytes()).hexdigest()
    base = oracle.valid_fixture()
    base[1]["token_usage"]["source"] = source
    oracle.seal(base)
    log = directory / "lifecycle.jsonl"
    log.write_text("\n".join(json.dumps(row, sort_keys=True, separators=(",", ":")) for row in base) + "\n", encoding="utf-8")

    shared = ["--run", "roadmap-v2", "--unit", "GS2-01.1", "--log", str(log), "--usage", str(usage),
              "--required-phase", "claim", "--required-phase", "implement", "--require-terminal"]
    same_verdict("lifecycle validate",
                 run(["python3", str(PNEXT / "validate-lifecycle-log.py"), *shared]),
                 engine("telemetry", "lifecycle", "validate", *shared), True)
    positive = 1

    chain_fields = {"sequence", "revision", "previous_digest", "digest"}
    existing = directory / "existing.jsonl"
    existing.write_text(json.dumps(base[0], sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")
    draft = directory / "draft.json"
    draft.write_text(json.dumps({key: value for key, value in base[1].items() if key not in chain_fields},
                                sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")
    seal_args = ["--run", "roadmap-v2", "--unit", "GS2-01.1", "--existing", str(existing), "--usage", str(usage)]
    python = run(["python3", str(PNEXT / "validate-lifecycle-log.py"), "--seal-successor", str(draft), *seal_args])
    compiled = engine("telemetry", "lifecycle", "seal-successor", "--draft", str(draft), *seal_args)
    if python.returncode or compiled.returncode or python.stdout != compiled.stdout:
        raise SystemExit(f"lifecycle seal parity failed\npython={python.stdout!r} {python.stderr}\ncompiled={compiled.stdout!r} {compiled.stderr}")
    positive += 1

    def comment(event: dict, number: int, updated: bool = False) -> dict:
        body = "<!-- fsgg:item-lifecycle/v1 -->\n```json\n" + json.dumps(event, sort_keys=True, separators=(",", ":")) + "\n```\n"
        return {"id": number, "created_at": "2026-09-04T08:00:00Z",
                "updated_at": "2026-09-04T08:01:00Z" if updated else "2026-09-04T08:00:00Z", "body": body}

    comments = directory / "comments.json"
    comments.write_text(json.dumps([[comment(base[0], 1)]]), encoding="utf-8")
    export_args = ["--run", "roadmap-v2", "--unit", "GS2-01.1"]
    python = run(["python3", str(PNEXT / "validate-lifecycle-log.py"), *export_args, "--export-comments", str(comments)])
    compiled = engine("telemetry", "lifecycle", "export-comments", *export_args, "--comments", str(comments))
    if python.returncode or compiled.returncode or python.stdout != compiled.stdout:
        raise SystemExit("lifecycle export parity failed")
    positive += 1

    fork = copy.deepcopy(base[0])
    fork["actor"] = "critic-2"
    fork["digest"] = oracle.canonical_digest(fork)
    comments.write_text(json.dumps([[comment(base[0], 1), comment(fork, 2)]]), encoding="utf-8")
    python = run(["python3", str(PNEXT / "validate-lifecycle-log.py"), *export_args, "--export-comments", str(comments)])
    compiled = engine("telemetry", "lifecycle", "export-comments", *export_args, "--comments", str(comments))
    if python.returncode or compiled.returncode or python.stdout != compiled.stdout:
        raise SystemExit("lifecycle deterministic-fork export parity failed")
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
    rejected = 0
    for name, mutate in mutations.items():
        rows = copy.deepcopy(base)
        mutate(rows)
        candidate = directory / f"lifecycle-negative-{rejected}.jsonl"
        candidate.write_text("\n".join(json.dumps(row, sort_keys=True, separators=(",", ":")) for row in rows) + "\n", encoding="utf-8")
        phases = ["claim", "implement", "acceptance"] if name == "missing required phase" else ["claim", "implement"]
        phase_args = [value for phase in phases for value in ("--required-phase", phase)]
        args = ["--run", "roadmap-v2", "--unit", "GS2-01.1", "--log", str(candidate), "--usage", str(usage), *phase_args, "--require-terminal"]
        same_verdict(f"lifecycle rejection: {name}",
                     run(["python3", str(PNEXT / "validate-lifecycle-log.py"), *args]),
                     engine("telemetry", "lifecycle", "validate", *args), False)
        rejected += 1

    provenance = {
        "forged token report join": lambda rows: rows[1]["token_usage"].update({"input": 1_000_000_999, "total": 1_000_001_004}),
        "forged tooling version": lambda rows: rows[1]["tooling"]["sdd"].update({"version": "999.999.999"}),
        "invented historical corpus": lambda rows: rows[3].update({"historical_durations_minutes": [999], "historical_average_minutes": 999}),
    }
    for name, mutate in provenance.items():
        rows = copy.deepcopy(base)
        mutate(rows)
        oracle.seal(rows)
        candidate = directory / f"lifecycle-provenance-{rejected}.jsonl"
        candidate.write_text("\n".join(json.dumps(row, sort_keys=True, separators=(",", ":")) for row in rows) + "\n", encoding="utf-8")
        args = ["--run", "roadmap-v2", "--unit", "GS2-01.1", "--log", str(candidate), "--usage", str(usage),
                "--required-phase", "claim", "--required-phase", "implement", "--require-terminal"]
        same_verdict(f"lifecycle rejection: {name}",
                     run(["python3", str(PNEXT / "validate-lifecycle-log.py"), *args]),
                     engine("telemetry", "lifecycle", "validate", *args), False)
        rejected += 1

    invalid_forks = []
    bad_digest = copy.deepcopy(fork); bad_digest["digest"] = "f" * 64; invalid_forks.append((bad_digest, False))
    alternate = copy.deepcopy(fork); alternate["previous_digest"] = "e" * 64; alternate["digest"] = oracle.canonical_digest(alternate); invalid_forks.append((alternate, False))
    missing_actor = copy.deepcopy(fork); del missing_actor["actor"]; missing_actor["digest"] = oracle.canonical_digest(missing_actor); invalid_forks.append((missing_actor, False))
    invalid_forks.append((base[0], True))
    for event, edited in invalid_forks:
        comments.write_text(json.dumps([[comment(base[0], 1), comment(event, 2, edited)]]), encoding="utf-8")
        same_verdict("lifecycle rejected fork/comment",
                     run(["python3", str(PNEXT / "validate-lifecycle-log.py"), *export_args, "--export-comments", str(comments)]),
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
        python = run(["python3", str(WORK / "validate-critique-state.py"), "--root", str(ROOT), "--cycle", cycle, "--artifact", str(artifact.relative_to(ROOT))])
        compiled = engine("telemetry", "critique", "validate", "--cycle", cycle, "--head", head, "--artifact", str(artifact))
        same_verdict(f"critique positive {artifact.name}", python, compiled, True)
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
        same_verdict(f"critique rejection: {name}",
                     run(["python3", str(WORK / "validate-critique-state.py"), "--root", str(directory), "--cycle", cycle, "--artifact", str(relative)]),
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
        python = run(["python3", str(WORK / "validate-feedback-state.py"), "--root", str(ROOT), "--cycle", cycle.group(1), "--report", str(report.relative_to(ROOT)), "--audit", str(audit.relative_to(ROOT)), "--phases", phase_value])
        compiled = engine("telemetry", "feedback", "validate", "--cycle", cycle.group(1), "--report", str(report.relative_to(ROOT)), "--audit", str(audit.relative_to(ROOT)), "--phases", phase_value)
        same_verdict(f"feedback positive {report.name}", python, compiled, True)
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
        python = run(["python3", str(WORK / "validate-feedback-state.py"), "--root", str(directory), *common])
        compiled_args = ["telemetry", "feedback", "validate", *common]
        if checkpoint_path.exists():
            compiled_args.extend(["--checkpoint", str(checkpoint_rel)])
        same_verdict(label, python, engine(*compiled_args, cwd=directory), expected)

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
    run(["dotnet", "build", str(ROOT / "src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj"), "-c", "Release", "--no-restore"], check=True)
    run(["python3", str(PNEXT / "collect-runtime-usage.py"), "--self-test"], check=True)
    run(["python3", str(PNEXT / "validate-lifecycle-log.py"), "--self-test"], check=True)
    positive = rejected = 0
    with tempfile.TemporaryDirectory(prefix="fsgg-3208-parity-") as path:
        directory = Path(path)
        p, r, session = collector_parity(directory); positive += p; rejected += r
        p, r = lifecycle_parity(directory, session); positive += p; rejected += r
        p, r = critique_parity(directory / "critique"); positive += p; rejected += r
        p, r = feedback_parity(directory / "feedback-root"); positive += p; rejected += r
    print(f"telemetry-parity: pass ({positive} positive and {rejected} rejection differential cases across the full frozen helper corpus)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
