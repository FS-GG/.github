#!/usr/bin/env python3
"""Mutation controls for the board-currency merge gate in tools/routine-delivery.py (Refs #5094).

Each mutant is one deliberate defect in the gate or its post-merge Status transition. BoardGateTests is
re-run against a mutated copy of the helper (ROUTINE_DELIVERY_UNDER_TEST); a mutant that leaves every
test green means no test pins that behaviour, and this script exits 1 naming it. The unmutated helper
must pass first.
"""
from __future__ import annotations

import os
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
SOURCE = ROOT / "tools/routine-delivery.py"

# (name, exact original text, replacement). Every original must occur exactly once.
MUTANTS = [
    ("enforce refusal still merges", 'if board_gate == "enforce" and gate.verdict == "refused":', "if False:"),
    ("enforce unknown merges (fail-open)",
     'if board_gate == "enforce" and gate.verdict in ("unknown", "unavailable"):',
     'if board_gate == "enforce" and gate.verdict in ("unavailable",):'),
    ("override evaluated after the board read",
     "        if rules.override_of(pr, roots) is not None:\n            return BoardVerdict(\"overridden\", \"valid-override\")\n",
     "        if False:\n            pass\n"),
    ("Unknown linkage treated as refusal", 'return BoardVerdict("unknown", f"U {unknown[0]}")',
     'return BoardVerdict("refused", f"R1 {unknown[0]}")'),
    ("moved body between reads trusted", "if (not isinstance(pr, dict) or pr.get(\"body\") != rest_body or pr.get(\"state\") != \"OPEN\"",
     "if (not isinstance(pr, dict) or pr.get(\"state\") != \"OPEN\""),
    ("Blocked preimage is writable", 'BOARD_WRITABLE_PREIMAGES = ("Backlog", "Ready", "In progress", "In review")',
     'BOARD_WRITABLE_PREIMAGES = ("Backlog", "Ready", "In progress", "In review", "Blocked")'),
    ("equal preimage still sends", '    if before["status"] == target:\n        return "noop"\n', ""),
    ("closed Delivery is written", '    if before["state"] == "CLOSED":\n        return "not-applicable"\n', ""),
    ("closing keyword ignored", "        if delivery.lower() in board_closing_refs(rules, repo, body):\n            return \"not-applicable\"\n", ""),
    ("advisory writes", '    if mode != "enforce":\n        return f"would-write:{target}"\n', ""),
    ("continues marker ignored", 'target = "In progress" if BOARD_CONTINUES in rules.visible_text(body) else "In review"',
     'target = "In review"'),
    ("continues marker counted in code spans", 'BOARD_CONTINUES in rules.visible_text(body)', "BOARD_CONTINUES in str(body)"),
    ("adjacent preimage not compared", "    if adjacent != before:\n        return \"refused-preimage\"\n", ""),
    ("readback not verified", 'return "written" if after == dict(before, status=target) else "uncertain"', 'return "written"'),
    ("failed send is retried",
     "    except Exception:\n        return \"uncertain\"\n    try:\n        after = api.board_item(delivery)",
     "    except Exception:\n        api.board_set_status(before[\"projectId\"], before[\"itemId\"], before[\"statusFieldId\"], options[target])\n"
     "        return \"uncertain\"\n    try:\n        after = api.board_item(delivery)"),
    ("transition runs for overridden PRs", 'if gate is not None and gate.verdict == "linked" and gate.delivery is not None:',
     'if gate is not None and gate.verdict in ("linked", "overridden"):'),
    ("possible-send recorded after the send",
     '    print("fsgg routine board transition: possible-send recorded; one Status send follows", file=sys.stderr)\n    try:\n        api.board_set_status(before["projectId"], before["itemId"], before["statusFieldId"], options[target])\n',
     '    try:\n        api.board_set_status(before["projectId"], before["itemId"], before["statusFieldId"], options[target])\n        print("fsgg routine board transition: possible-send recorded; one Status send follows", file=sys.stderr)\n'),
    ("pre-gate prior results rejected", "    if keys in (current, current - BOARD_RESULT_FIELDS):", "    if keys == current:"),
    ("truncated nested value trusted", '            raise RuntimeError("board item value is truncated")', "            pass"),
    ("live option identity not checked", 'if not isinstance(status, str) or by_name.get(status) != status_nodes[0].get("optionId"):',
     "if not isinstance(status, str):"),
    ("exiting rules module ends the helper", "    except (Exception, SystemExit):\n        # A broken", "    except Exception:\n        # A broken"),
]


def run(source: Path) -> bool:
    env = dict(os.environ, ROUTINE_DELIVERY_UNDER_TEST=str(source), PYTHONDONTWRITEBYTECODE="1")
    done = subprocess.run([sys.executable, str(HERE / "routine-delivery.py"), "BoardGateTests"],
                          env=env, capture_output=True, text=True, timeout=300, check=False)
    return done.returncode == 0


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")
    with tempfile.TemporaryDirectory(prefix="routine-delivery-board-mutate-") as scratch:
        # A link farm of the checkout with a real tools/routine-delivery.py: its sibling loaders
        # (pr-lane-admission, the evidence reader, scripts/board-currency-rules.py) resolve unchanged.
        mirror = Path(scratch)
        for entry in ROOT.iterdir():
            if entry.name != "tools":
                (mirror / entry.name).symlink_to(entry)
        (mirror / "tools").mkdir()
        for entry in (ROOT / "tools").iterdir():
            if entry.name != "routine-delivery.py":
                (mirror / "tools" / entry.name).symlink_to(entry)
        target = mirror / "tools/routine-delivery.py"
        target.write_text(text, encoding="utf-8")
        if not run(target):
            print("routine-delivery board mutate: the unmutated helper fails BoardGateTests", file=sys.stderr)
            return 1
        survivors = []
        for name, original, replacement in MUTANTS:
            if text.count(original) != 1:
                print(f"routine-delivery board mutate: mutant anchor not unique: {name}", file=sys.stderr)
                return 1
            target.write_text(text.replace(original, replacement), encoding="utf-8")
            killed = not run(target)
            print(f"{'killed  ' if killed else 'SURVIVED'} {name}")
            if not killed:
                survivors.append(name)
    print(f"routine-delivery board mutate: {len(MUTANTS) - len(survivors)}/{len(MUTANTS)} mutants killed")
    return 1 if survivors else 0


if __name__ == "__main__":
    sys.exit(main())
