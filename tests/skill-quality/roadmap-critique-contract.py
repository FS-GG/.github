#!/usr/bin/env python3
"""Exercise the ten-round work-roadmap critique boundary and escalation state."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
RUNTIMES = (".agents", ".claude")
CYCLE = "roadmap-example-m1-example"
SHA = [f"{index:040x}" for index in range(1, 13)]
sys.dont_write_bytecode = True


def load_validator(path: Path):
    spec = importlib.util.spec_from_file_location("critique_validator", path)
    if spec is None or spec.loader is None:
        raise AssertionError(f"cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def artifact(rounds: int, *, escalation: bool = False) -> dict:
    commits = SHA[: rounds + 1]
    return {
        "schema_version": 2,
        "cycle_id": CYCLE,
        "milestone": "M1 — example",
        "critic": "fresh critic identity",
        "initial_reviewed_commit": commits[0],
        "scope": ["requirements", "diff", "tests", "architecture", "roadmap-evidence"],
        "initial_verdict": "pass" if rounds == 0 else "changes-required",
        "repair_rounds": rounds,
        "reviewed_commits": commits,
        "findings": [] if rounds == 0 else [{
            "id": "F1",
            "severity": "major",
            "summary": "material failure",
            "evidence": ["test failed"],
            "disposition": "resolved",
            "resolution_evidence": ["fixed and retested"],
        }],
        "confirmation": {
            "reviewed_commit": commits[-1],
            "verdict": "changes-required" if escalation else "pass",
            "unresolved_blocker_major": ["F1"] if escalation else [],
        },
        "human_escalation": {
            "reviewed_commit": commits[-1],
            "unresolved_blocker_major": ["F1"],
            "action_required": "human must decide the acceptance boundary",
    } if escalation else None,
    }


def main() -> None:
    validators = []
    for runtime in RUNTIMES:
        path = ROOT / runtime / "skills/work-roadmap/scripts/validate-critique-state.py"
        validators.append((runtime, path.read_bytes(), load_validator(path)))
    assert validators[0][1] == validators[1][1], "critique validators differ between authored roots"

    for runtime, _, validator in validators:
        assert validator.validate(artifact(0), CYCLE) == [], f"{runtime}: zero-round pass rejected"
        assert validator.validate(artifact(10), CYCLE) == [], f"{runtime}: tenth-round pass rejected"

        too_many = artifact(10)
        too_many["repair_rounds"] = 11
        too_many["reviewed_commits"] = SHA[:12]
        errors = validator.validate(too_many, CYCLE)
        assert any("0 through 10" in error for error in errors), f"{runtime}: round eleven accepted"

        broken_chain = artifact(10)
        broken_chain["reviewed_commits"][-1] = broken_chain["reviewed_commits"][-2]
        errors = validator.validate(broken_chain, CYCLE)
        assert any("unique ordered" in error for error in errors), f"{runtime}: broken SHA chain accepted"

        terminal = artifact(10, escalation=True)
        terminal["findings"][0]["disposition"] = "unresolved"
        errors = validator.validate(terminal, CYCLE)
        assert any("human escalation is terminal" in error for error in errors), (
            f"{runtime}: terminal escalation accepted as milestone completion"
        )
        assert not any("must match" in error for error in errors), f"{runtime}: coherent escalation rejected"

        missing_escalation = artifact(10, escalation=True)
        missing_escalation["findings"][0]["disposition"] = "unresolved"
        missing_escalation["human_escalation"] = None
        errors = validator.validate(missing_escalation, CYCLE)
        assert any("is required after a failed round 10" in error for error in errors), (
            f"{runtime}: failed tenth round did not require escalation evidence"
        )

        mismatched = artifact(10, escalation=True)
        mismatched["findings"][0]["disposition"] = "unresolved"
        mismatched["human_escalation"]["unresolved_blocker_major"] = ["F2"]
        errors = validator.validate(mismatched, CYCLE)
        assert any("must match" in error for error in errors), f"{runtime}: mismatched escalation IDs accepted"

    print("roadmap-critique-contract: ten rounds, ordered SHAs, and terminal human escalation hold")


if __name__ == "__main__":
    main()
