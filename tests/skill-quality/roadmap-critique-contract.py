#!/usr/bin/env python3
"""Exercise the ten-round work-roadmap critique boundary and escalation state."""

from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
RUNTIMES = (".agents", ".claude")
CYCLE = "roadmap-example-m1-example"
SHA = [f"{index:040x}" for index in range(1, 13)]
PRECEDENCE = "owns the review/repair count and supersedes `$pnext-item`'s normal three-round cap"
PRESERVED_DISCIPLINE = "all other applicable `$pnext-item` planning, review-evidence, exact-SHA, merge,"
sys.dont_write_bytecode = True
ENGINE = Path(os.environ["FSGG_COORD_ENGINE_BIN"])


class CompiledValidator:
    @staticmethod
    def validate(value: dict, cycle: str) -> list[str]:
        with tempfile.TemporaryDirectory(prefix="critique-contract-") as directory:
            artifact_path = Path(directory) / "critique.json"
            artifact_path.write_text(json.dumps(value), encoding="utf-8")
            result = subprocess.run(
                [str(ENGINE), "telemetry", "critique", "validate",
                 "--cycle", cycle, "--artifact", str(artifact_path)],
                cwd=ROOT, text=True, capture_output=True, check=False,
            )
            return [] if result.returncode == 0 else result.stderr.splitlines()


def artifact(rounds: int, *, escalation: bool = False) -> dict:
    commits = SHA[: rounds + 1]
    return {
        "schema_version": 3,
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
        "game_functionality": False,
        "player_journeys": [],
        "uncovered_functionality": [],
        "entry_point_not_test_ownable": False,
        "entry_point_not_test_ownable_reason": None,
    }


def compliant_journey() -> dict:
    return {
        "functionality": "rogue3 starting-room descent",
        "entry_point": "product-boot",
        "input_surface": "player-control-messages",
        "reached": True,
        "evidence": ["headless run: reached starting room, descended one level"],
    }


def main() -> None:
    validator = CompiledValidator()
    for runtime in RUNTIMES:
        skill = " ".join((ROOT / runtime / "skills/work-roadmap/SKILL.md").read_text().split())
        critique_contract = " ".join(
            (ROOT / runtime / "skills/work-roadmap/references/critique-contract.md").read_text().split()
        )
        assert PRECEDENCE in skill, f"{runtime}: main contract omits pnext-item precedence"
        assert PRECEDENCE in critique_contract, f"{runtime}: critique contract omits pnext-item precedence"
        assert PRESERVED_DISCIPLINE in skill, f"{runtime}: main contract narrows inherited discipline"
        assert PRESERVED_DISCIPLINE in critique_contract, f"{runtime}: critique contract narrows inherited discipline"
        assert "scripts/fsgg-coord telemetry critique validate" in critique_contract, (
            f"{runtime}: critique contract does not call the compiled validator"
        )

    for runtime in RUNTIMES:
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
        assert errors, f"{runtime}: failed tenth round without escalation was accepted"

        mismatched = artifact(10, escalation=True)
        mismatched["findings"][0]["disposition"] = "unresolved"
        mismatched["human_escalation"]["unresolved_blocker_major"] = ["F2"]
        errors = validator.validate(mismatched, CYCLE)
        assert errors, f"{runtime}: mismatched escalation IDs accepted"

        # .github#2087 — the bot-driven player journey gate. Every leg below is falsifiable: it is
        # run once and MUST report an error, not merely written and trusted.

        # AC1: a game-functionality milestone with no journey evidence at all is blocked, not a silent pass.
        no_evidence = artifact(0)
        no_evidence["game_functionality"] = True
        errors = validator.validate(no_evidence, CYCLE)
        assert any("player_journeys must contain an entry" in error for error in errors), (
            f"{runtime}: game milestone with zero journeys was not blocked"
        )

        # AC2: a journey driven by direct Msg injection (bypassing the real input surface) is REJECTED
        # by the gate itself, not merely discouraged in prose.
        bypass = artifact(0)
        bypass["game_functionality"] = True
        bypass_journey = compliant_journey()
        bypass_journey["input_surface"] = "direct-msg-injection"
        bypass["player_journeys"] = [bypass_journey]
        errors = validator.validate(bypass, CYCLE)
        assert any("input_surface must be player-control-messages" in error for error in errors), (
            f"{runtime}: direct-Msg-injection journey was accepted as evidence"
        )

        # AC3/AC6: the Rogue3 starting-room shape — a journey seeded into a mid-game state that still
        # CLAIMS the functionality was reached. Every other field is compliant; only the entry point is
        # the unreachable-start defect this gate exists to catch. Must go red even though "reached" is
        # true, proving the gate does not accept reachability claimed from an unreachable start.
        unreachable_start = artifact(0)
        unreachable_start["game_functionality"] = True
        seeded_journey = compliant_journey()
        seeded_journey["entry_point"] = "seeded-mid-game"
        unreachable_start["player_journeys"] = [seeded_journey]
        errors = validator.validate(unreachable_start, CYCLE)
        assert any("entry_point must be product-boot" in error for error in errors), (
            f"{runtime}: seeded mid-game start was accepted as the product's real entry point"
        )

        # AC7: an entry point that is not yet test-ownable fails closed with a named reason instead of
        # silently passing by absence of journeys.
        not_ownable_missing_reason = artifact(0)
        not_ownable_missing_reason["game_functionality"] = True
        not_ownable_missing_reason["entry_point_not_test_ownable"] = True
        errors = validator.validate(not_ownable_missing_reason, CYCLE)
        assert any("entry_point_not_test_ownable_reason must be non-empty" in error for error in errors), (
            f"{runtime}: not-test-ownable escape hatch accepted with no reason"
        )

        not_ownable_with_reason = artifact(0)
        not_ownable_with_reason["game_functionality"] = True
        not_ownable_with_reason["entry_point_not_test_ownable"] = True
        not_ownable_with_reason["entry_point_not_test_ownable_reason"] = "entry point not yet test-ownable, FS.GG.Game#565 follow-on"
        assert validator.validate(not_ownable_with_reason, CYCLE) == [], (
            f"{runtime}: fail-closed not-test-ownable declaration with a reason was rejected"
        )

        # Positive control: a fully compliant journey (real entry, real input surface, reached) passes.
        compliant = artifact(0)
        compliant["game_functionality"] = True
        compliant["player_journeys"] = [compliant_journey()]
        assert validator.validate(compliant, CYCLE) == [], f"{runtime}: fully compliant journey was rejected"

        # A non-game milestone must not carry journeys — they would be unreviewed, unfalsifiable prose.
        stray_journey = artifact(0)
        stray_journey["player_journeys"] = [compliant_journey()]
        errors = validator.validate(stray_journey, CYCLE)
        assert any("player_journeys must be empty when game_functionality is false" in error for error in errors), (
            f"{runtime}: non-game milestone accepted an unreviewed journey"
        )

    print(
        "roadmap-critique-contract: ten rounds, ordered SHAs, terminal human escalation, and the "
        "bot-driven player journey gate all hold"
    )


if __name__ == "__main__":
    main()
