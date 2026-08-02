# CLI surface three-way reconciliation — healthcheck leg 13

**Date:** 2026-08-02

**Scope:** the `fsgg-coord` command surface. This is a bounded healthcheck
definition and evidence report for `.github#2017`; it does not introduce a
second parser, renderer, usage table, or gate contract.

## The three authoritative observations

Leg 13 reconciles one promised CLI capability through three independently
observable surfaces:

1. **Usage text** is the human-facing promise in `Options.usage`. A command or
   projection that works but is absent from `--help` is not discoverable.
2. **Parser/contract** is the machine-readable `command-contract` output and
   the argument parser that accepts or refuses each flag.
3. **Renderer** is `renderSupport`: whether a command actually has a JSON and/or
   human projection, including its bare-form default.

`tests/FS.GG.Coord.Cli.Tests/CommandSurfaceTests.fs` is the executable owner.
It does not compare hand-maintained snapshots. Its `#1523` assertions compare
every command's emitted render flags with its renderer support and then execute
the parser in both directions: advertised flags must parse, and unadvertised
flags must be refused as residue. Its `#1548` assertion reads each anchored
usage line and compares its `--json`/`--text` flags with the emitted contract.

Together these checks close the historical gap where a flag could be advertised
and parsed but ignored by the renderer (`#1517`/`#1523`), or where the renderer
and contract were correct but the help text concealed a usable projection
(`#1548`). The command-surface inventory also ensures that every dispatched
verb has a usage entry, rather than allowing an omitted command to disappear
from the reconciliation population.

The reports and tests intentionally retain one explicit migration boundary:
`renderUsageExemptions` is asserted equal to the *observed* set of usage/contract
disagreements. It is not a silent allow-list: a new omission fails, and
documenting an existing exemption fails until the exemption is removed. The
four commands found in `#1548` are not exempt.

## Verdict and no-verdict discipline

An executable successor must reuse `ExitCode`, `GateError`, and `run` from
`scripts/lib/gate.py`; it must not re-spell the gate contract. A fully observed
agreement is exit `0`; a readable disagreement is exit `1`; and an inability to
establish all three surfaces is a permanent **no-verdict at exit `3`** via
`GateError`. In particular, a missing usage subject, malformed emitted contract,
or incomplete command inventory cannot become a green result merely because no
comparison was made.

The current F# test suite fails directly rather than returning a process
verdict, but it preserves the same semantic split: a missing anchored usage
line is an assertion failure, not an empty flag set; an emitted row cannot be
silently dropped; and a parser refusal unrelated to the expected residue
refusal is a failure, not evidence that the flag was correctly rejected.

## Negative controls

The negative controls are structural and are exercised by the existing tests:

- Adding a renderer capability without advertising the corresponding flag, or
  advertising a flag for a text-only/JSON-only command, makes the `#1523`
  renderer/contract reconciliation fail.
- Letting the parser accept an unadvertised projection, or refuse an advertised
  one, makes the parser/contract round trip fail with the command and flag.
- Editing a usage line so its render flags differ from the contract makes the
  `#1548` comparison fail unless it is deliberately represented by the
  self-checking migration boundary above.
- Removing a command from usage fails the anchored command-surface inventory;
  it cannot reduce the comparison population and read as clean.

These controls distinguish a real mismatch from an ungradable surface. They
therefore prevent the `#266` shape: green over a missing subject.

## Boundary and evidence

This leg concerns render/usage/parser agreement. The separate question whether
an engine verb's declared **write** behaviour matches the requests it makes is
owned by `.github#1569` and `tests/coord-engine-e2e/writes.sh`; it must not be
misrepresented as a fourth copy of this CLI projection contract.

When kit-delivery history is relevant to this healthcheck, the corrected
`.github#1565` measurement is **16 opened / 4 merged**. The superseded
`12 opened / 0 merged` figure is not valid evidence.

This report records the existing executable owner and controls. It makes no
organisation-wide clean-health claim.
