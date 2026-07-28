#!/usr/bin/env python3
"""Adjudicate gates by MUTATION: break what each one claims to protect, and watch whether it fires.

THE DECISION THIS IMPLEMENTS (.github#1810, harness generalised per #1808, taken 2026-07-28).
`#1582` measured 30 workflows across eight FS-GG repos that have run ten or more times and never once
been red. **"It has never fired" is equally consistent with "nothing was ever wrong" and "it cannot
fire."** Only mutation separates them — and mutation is the method that found ten dead checks on
2026-07-27/28, in ten subsystems, none of them by reading. Several were in code written by people who
were specifically looking for that defect.

So this driver does not read gates. It breaks their subjects and reports one of three verdicts per
gate: **JUSTIFIED** (fired), **DECORATIVE** (could not fire), **NOT_MEASURED** (no measurement
obtained). The third is never collapsed into either of the others — `#266`: "I could not evaluate
this" is NEVER "I evaluated it and it passed."

IT NEVER REMOVES ANYTHING. A `DECORATIVE` verdict is the JUSTIFICATION for a removal or repair, filed
and decided separately (`#1810` AC3). "Mutating it was hard" is a `NOT_MEASURED` with a reason, not a
licence. This tool only ever writes to the file it is mutating, and restores it in a `finally`.

USAGE::

    scripts/gate-mutate.py                      # every leg in tests/gate-mutation/specs.yml
    scripts/gate-mutate.py --only touch-set-drift-selftest
    scripts/gate-mutate.py --json | jq '.counts'
    scripts/gate-mutate.py --markdown           # the ledger body for docs/reports/

The mechanism, its four-point specification, and the four harnesses that paid for each point are
documented in ``scripts/lib/mutation.py``.
"""

from __future__ import annotations

import json
import os
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lib.gate import ExitCode, GateError, base_parser, run  # noqa: E402
from lib.mutation import SpecError, Verdict, adjudicate, exit_code, load_specs, tally_counters  # noqa: E402

NAME = "gate-mutate"

DEFAULT_SPECS = "tests/gate-mutation/specs.yml"

_MEANING = {
    Verdict.JUSTIFIED.value: "fired on a mutant that broke its subject — keep it",
    Verdict.DECORATIVE.value: "stayed green on a mutant that broke its subject — it cannot fire",
    Verdict.NOT_MEASURED.value: "no measurement obtained — NOT a pass and NOT a failure (#266)",
}


def render_text(results, counts) -> None:
    for r in results:
        print(f"{r.verdict.value:<13} {r.mutation.id}  [{r.mutation.gate}]")
        print(f"              broke: {r.mutation.breaks}")
        print(f"              {r.reason}")
        print(f"              control rc={r.control_rc} anchor={r.control_anchor!r}; "
              f"mutant rc={r.mutant_rc} anchor={r.mutant_anchor!r}  ({r.seconds:.0f}s)")
        if r.mutation.note:
            print(f"              note: {r.mutation.note}")
        print()
    print(f"{NAME}: {len(results)} leg(s) — " + ", ".join(f"{k}={v}" for k, v in counts.items()))
    for k, v in counts.items():
        if v:
            print(f"  {k}: {_MEANING[k]}")


def render_markdown(results, counts) -> None:
    print("| verdict | gate | mutation | what it broke | evidence |")
    print("| --- | --- | --- | --- | --- |")
    for r in results:
        ev = f"control rc={r.control_rc}, mutant rc={r.mutant_rc}. {r.reason}"
        print(f"| **{r.verdict.value}** | `{r.mutation.gate}` | `{r.mutation.id}` | "
              f"{r.mutation.breaks} | {ev.replace('|', '/')} |")
    print()
    print(f"**{len(results)} leg(s)** — " + ", ".join(f"{k}: {v}" for k, v in counts.items()))


def main(argv) -> int:
    ap = base_parser(__doc__.splitlines()[0])
    ap.add_argument("--specs", default=DEFAULT_SPECS, help=f"mutation spec file (default: {DEFAULT_SPECS})")
    ap.add_argument("--only", action="append", default=[],
                    help="run only legs whose id or gate contains this substring (repeatable)")
    ap.add_argument("--json", action="store_true", help="machine-readable report")
    ap.add_argument("--markdown", action="store_true", help="ledger rows for docs/reports/")
    args = ap.parse_args(argv)

    root = Path(args.root).resolve()
    specs_path = Path(args.specs)
    if not specs_path.is_absolute():
        specs_path = root / specs_path

    try:
        mutations = load_specs(specs_path, root)
    except SpecError as e:
        # A refused spec is a no-verdict, never a finding about a gate: the harness could not measure.
        raise GateError(str(e)) from e

    if args.only:
        mutations = [m for m in mutations if any(k in m.id or k in m.gate for k in args.only)]
        if not mutations:
            raise GateError(f"--only {args.only} selected no legs out of {specs_path}")

    results = [adjudicate(m, root) for m in mutations]
    counts = tally_counters(results)
    code = exit_code(results)

    if args.json:
        print(json.dumps({"specs": str(specs_path.relative_to(root)) if specs_path.is_relative_to(root) else str(specs_path),
                          "counts": counts, "exit": code,
                          "legs": [r.to_json() for r in results]}, indent=2, sort_keys=True))
    elif args.markdown:
        render_markdown(results, counts)
    else:
        render_text(results, counts)

    if code == int(ExitCode.FINDING):
        print(f"::error::{NAME}: {counts[Verdict.DECORATIVE.value]} gate(s) DECORATIVE — they cannot "
              f"fire on the defect they claim to protect against", file=sys.stderr)
    return code


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name=NAME))
