# ADR-0066: `Class` is a body line and the board field is its projection

- **Status:** Accepted
- **Date:** 2026-07-27
- **Affects:** FS-GG/.github (the protocol, the engine, `lint`, `reconcile`, `drive-board`); every repo whose items are scheduled by `fsgg-coord`
- **Interacts with:** [ADR-0045](0045-machine-readable-sentinels-for-human-block-and-chore.md) — this record does **not** reverse its rejection of a Projects v2 field; see *Decision*.

## Context

The Coordination board carries `Status`, `Phase`, `Workstream`, `Effort` and `Repo Scope`. It has
vocabulary for *when*, *where* and *how big*, and none for **how bad**. That gap is why an unattended
burn-down cannot terminate on its own terms.

Measured on 2026-07-27 ([#1588](https://github.com/FS-GG/.github/issues/1588)): the board went from 5
non-Done rows to **34** during a single run in which 35+ items merged. The growth was *healthy* — every new
row was a real, evidenced finding produced by fixing the previous one. But `drive-board`'s termination
contract was *"a fresh reconcile and backlog triage have no startable item"*, and under that rule a run in
which fixing one thing files two never stops. It ended because a human intervened.

Three obligations look identical to `batch`, to `ready` and to that stopping rule:

- a gate RED on `main` ([#722](https://github.com/FS-GG/.github/issues/722)),
- a test file's running commentary skipping two version entries ([#724](https://github.com/FS-GG/.github/issues/724)),
- three digest implementations disagreeing on CRLF, where somebody must pick ([#1547](https://github.com/FS-GG/.github/issues/1547)).

A human sorted them by reading the titles in seconds. Severity was knowable and simply nowhere in the data.

## Decision

**A `Class: defect|hardening|decision` body line is the authority. The Projects v2 `Class` field is a
read-only projection of it, written by `reconcile` and read by nobody as an input.**

- `defect` — something is broken now.
- `hardening` — nothing is broken; the change removes a way it could break.
- `decision` — a human must choose before any work is authorable. Surfaced, never dispatched.

The grammar is ADR-0045's, unchanged: up to three leading spaces, outside fenced code blocks, value
normalised for case and space. Two **derivations** need no new line, so the fact is never written twice:
a `[decision]` title prefix, and ADR-0045's own `Blocked on: human/decision` sentinel.

`lint` reports a `Ready`/`Backlog` open row whose text declares no class as `CLASS-UNSET`. It never
defaults. `drive-board`'s stopping test becomes **no startable `defect`**, with an unclassed row counted as
a **possible** defect.

### Why this does not reverse ADR-0045

ADR-0045 rejected *"a dedicated Projects v2 board field for the human-block reason"* on the grounds of
board-schema churn, a field every filer would have to learn, and a value `lint` cannot enforce as cheaply as
a body line it already reads. All three objections are objections to a field as an **input**. None of them
applies to a field nobody writes by hand:

- no filer learns it — filers write the same kind of body line ADR-0045 already asks for;
- `lint` enforces the body line, exactly as cheaply as ADR-0045 predicted;
- the schema churn is one field, once, and it is guarded (below).

#1588's own prose proposed the opposite direction — *"the existing `Blocked on: human/decision` sentinel
becomes derivable **from the field**"* — while its acceptance criteria say the sentinel and the title prefix
are *evidence* and *"if a row's class is inferable from a label, a title convention or a sentinel, derive it
there"*. The criteria are right, and not merely because criteria win: field-as-authority would have made
ADR-0045's sentinel a projection of a board column, silently reversing an Accepted ADR, and would have done
it by rewriting ~50 issue bodies — a change nobody could review.

### The two-fact split

`Item` carries `Class` (what the item's text declares) and `BoardClass` (what the column renders). The chore
`CLASS-PROJECTION-LAG` is derived exactly where they disagree, which is what lets it **retire**: once the
write lands, the next scan sees agreement and derives nothing. Collapsed into one field the disagreement
would be inexpressible, and `reconcile` would report the same finding forever.

### Creating the field is not a guarded migration

`scripts/project-field-options` exists to fence `updateProjectV2Field`, which has historically recreated
options and cleared every item value. Creating `Class` is `createProjectV2Field` on a field that does not
exist: there are no assignments to lose and no snapshot precondition is meaningful. The guarded
`add-option` path becomes relevant only for a *later* fourth option. `check --field Class` gates the option
set against the closed `ItemClass` vocabulary offline, so the three words cannot drift between the union,
the board and the docs.

## Consequences

- A burn-down terminates on evidence rather than on exhaustion, and `hardening` stops being the thing that
  makes a run unterminating.
- **An unclassed row is a possible defect, not a minor one.** This is the load-bearing consequence. A driver
  keying on `defect` over unpopulated rows would read every one as not-a-defect and stop immediately —
  leaving live defects, the exact failure this record exists to prevent, arriving through the fix. #266's
  rule on a new axis: a subject you could not evaluate is never a subject that passed.
- Population is a human obligation that `lint` drives, one row at a time, and it is deliberately not a
  migration: classing ~50 rows mechanically would mean guessing ~50 severities.
- The engine gains a fourth body-line sentinel family. [ADR-0059](0059-freeze-the-coordination-taxonomy-two-instances-before-a-class.md)'s
  restraint rule asks for two real instances before a new class; the two here are the burn-down that could
  not terminate and the driver that had to ask a human where to stop, both on the same day and both
  measured. It adds a **field**, not a scope class, which is the shape ADR-0059 explicitly prefers.

## Alternatives considered

- **The board field as the authority**, with the sentinel derived from it — #1588's prose. Rejected: it
  reverses ADR-0045 (above), makes the field a fourth hand-maintained copy of a fact the body already
  carries, and requires a ~50-body rewrite to bootstrap.
- **A GitHub label** (`defect`/`hardening`/`decision`). Rejected: labels are per-repo, and the board spans
  eight repos, so the vocabulary would have to be created and kept in step eight times — the fan-out
  [ADR-0062](0062-versioned-kit-package-replaces-byte-copy-sync.md) is removing everywhere else.
- **Infer severity from the title text.** Rejected: it is a guess, and a guess that looks like a fact is
  worse than a gap. `[decision]` is admitted only because it is an existing *convention* an author opted
  into, not a reading of prose.
- **Land the driver's stopping rule first and populate later.** Rejected as actively unsafe: it is the
  early-termination failure named in *Consequences*, which is why the unclassed-row rule is part of this
  record rather than a later refinement.

<!-- HOUSE RULES: derive, don't restate (ADR-0058). This record states the direction and the reason; the
     grammar lives in `Class.fsi`, the vocabulary in `Types.fsi`, the stopping rule in `drive-board`, and
     the option table in `docs/coordination/board-schema.md`. Do not copy any of them here. -->
