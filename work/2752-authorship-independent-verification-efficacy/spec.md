---
schemaVersion: 1
workId: 2752-authorship-independent-verification-efficacy
title: Authorship Independent Verification Efficacy
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Authorship Independent Verification Efficacy Specification

Prose status: specified

## User Value

An FS-GG critic reviewing a change that adds or modifies a verification artifact can **execute or
measure** a bounded, named set of checks that answer one question — *was this artifact's proof of
efficacy drawn from an oracle independent of the artifact's author?* — instead of reading an inversion
table that is affirmative, detailed, and generated from the same wrong model as the artifact it
certifies.

`.github#266`'s fifth admitted mechanism is that relation. The first four describe *what the artifact
does wrong*; this one describes *why nobody caught it*. It was admitted on 2026-08-17 from a pass over
the `.github#2691` packet register in which **13 packets folded onto it**, across two surfaces that are
one cause at this altitude:

- **the mutation sweep is drawn from the author's own model** — a gate shipped with **ten**
  authoring-time inversions, all ten of which fired correctly, none of which could reach the blind
  spot, because no mutation in the space asked the question the wrong premise had foreclosed
  (`.github#2691` comment `5311706988`). Later quantified at **65 survivors where four expert hand
  sweeps found 5** (`5313644118`).
- **the gate's fixture writes the subject the gate reads** — the `§11.2` fencing sequence produced
  **four** artifacts of exactly this shape, audited at `.github#1858` comment `5316937299`:
  `OpLock.acquire` with zero production callers, an `fsgg:merge-election` reader with zero writers, a
  broker that refuses every real request, and a six-field gate whose only producer emitted four
  fields. Every one passed its own tests.

Today the contract those critics work under — `.claude/skills/pnext-item/references/independent-review.md`
— asks for gate inversion (§`Gate-inversion evidence`, nine numbered steps) and says nothing about
whether the *oracle* that graded the inversion was independent of the person who wrote it. The escapes
above all sit inside a completed, correct-looking, nine-step sweep.

## Scope

- SB-001: `.claude/skills/pnext-item/references/independent-review.md`, and only that file. Two
  regions of it:
  - the `## Gate-inversion evidence` section, which is this org's existing efficacy procedure and the
    anchor `pnext-item` `SKILL.md` §3 already links; the new material is sited **inside** it so the
    wiring needs no second file;
  - the materiality list in `## Root cause, dedupe, and materiality`, which is what makes a new
    requirement blocking rather than decorative.

## Non-Goals

- SB-002: **`.github#266`'s issue body is not edited.** AC1 asks that the epic's admitted-mechanism
  table carry the fifth mechanism. It already does, stated as the authorship relation with both
  surfaces as instances. AC1 is discharged by verification. `.github#2695` makes the analyst the sole
  filing authority in any case.
- SB-003: **`tests/claim-fence/`, `scripts/check-claim-fence.py` and
  `.github/workflows/fsgg-claim-fence.yml` are not edited.** They are AC2's first debtor and are owned
  by `.github#2719`, which has a live worker on them. Declaring them here would put one test leg inside
  two rows' declarations — the individually-green-jointly-red shape the filing pass caught itself
  authoring once and corrected.
- SB-004: **`tests/receiver-validate/run.sh` is not edited.** It is AC2's *reference implementation*,
  and citation is the whole use this work makes of it.
- SB-005: **No kit source other than the declared file, `SKILL.md` included.** The siting decision in
  SB-001 exists precisely so no second edit is owed.
- SB-006: **The owed `FS.GG.Kit` release is declared, not cut.** Editing a kit source obliges a
  coherent-set republish; publishing needs per-cut operator authorisation. `.github#2333`'s permanently
  wrong `kit/v0.47.0` is what performing an unauthorised, mis-attributed cut produces.
- SB-007: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories

- US-001 (P1): As a critic reviewing a gate that reads a marker, record or serialized state, I can
  require and check one leg that compares the gate's required field set against **the production
  writer's** emitted field set, so that a reader with no writer cannot pass its own suite green.
- US-002 (P1): As a critic reviewing a mutation sweep, I can require that each mutation names the test
  that redded for it, so that a sweep whose enumeration silently shrank cannot report `0 survivors`
  and `0 mutants` in the same bytes.
- US-003 (P1): As a critic offered a "second reading" as an independence argument, I can grade it on an
  ordered ladder and say which rung it reaches, so that a disclosure about a shared *library* is not
  mistaken for independence of the *value* or the *key*.
- US-004 (P1): As a critic reviewing a sweep-shaped remedy, I know that the two detection methods are
  mutually blind and that neither substitutes for the other, so I do not accept re-runs in one
  environment as coverage of an environment-dependent escape, nor a different environment as coverage
  of a completeness predicate that is wrong everywhere.
- US-005 (P1): As a critic reading a control, I can tell a control that discriminates from one that is
  always green — and from one that matched the expected answer for the wrong reason — because the
  control asserts up front that its input really has the shape it assumes.
- US-006 (P1): As an implementer on a row where a leg genuinely cannot be constructed, I can close the
  round with a declared, evidenced boundary rather than loop, because each requirement states its bound
  and its terminal disposition.

## Acceptance Scenarios

- AC-001 [US-001] [FR-001]: Given a diff that adds or modifies a gate whose subject is a marker,
  record or serialized state emitted by a production writer in a repository the reviewer can read,
  when the critic applies the contract, then a missing producer-agreement leg is a material finding,
  and the leg is satisfied only by one that parses the writer, states the relation it asserts
  (equality or containment) and why, refuses an empty set on either side, and ships a mutation showing
  the assertion red.
- AC-002 [US-001] [FR-001]: Given the same, but the producer is outside every repository the reviewer
  can read, when the critic applies the contract, then the declared boundary plus the reason is the
  required disposition and the leg is graded `NOT_MEASURED`, which closes the round.
- AC-003 [US-002] [FR-002]: Given a mutation sweep, when any mutation's only witness is the sweep's own
  aggregate count, then the sweep fails; and when every mutation names a test whose title describes
  that mutation, the sweep's witness requirement is met.
- AC-004 [US-003] [FR-003]: Given any second reading offered as an oracle, when the critic grades it,
  then the review record names the highest rung of the ladder it reaches — value, key, or
  library/runtime — and states the residual at the rungs it does not escape.
- AC-005 [US-004] [FR-004]: Given a sweep-shaped remedy, when only one of the two mutually blind
  methods was applied, then the other is owed, and the review record says which was applied and which
  was not.
- AC-006 [US-005] [FR-005]: Given the two legs above, when their controls are read, then each carries a
  negative control that reds on a deliberately wrong model **and** a positive control that is admitted,
  and each control asserts before measuring that its input has the shape it assumes. A control that
  cannot produce both answers is a semantic no-op and is a material finding.
- AC-007 [US-006] [FR-006]: Given any requirement added by this work, when a critic or implementer
  reads it, then its bound (what it is owed on) and its terminal disposition (what closes it) are both
  stated in the contract text, and no requirement is expressed in a form that admits no terminal state.
- AC-008 [US-001..US-006] [FR-007]: Given this work's own change, when its efficacy is checked, then
  the control set discriminating the new rules is drawn from artifacts **other agents already measured
  and recorded**, with the verdict fixed before this work existed, and the contract says so in terms.

## Functional Requirements

- FR-001: The producer-agreement leg — a gate whose subject is a marker, record or serialized state that some production writer emits carries a leg asserting, in both directions, the declared relation between the field set the gate requires and the field set the writer emits, with that relation named and justified, a liveness term refusing an empty set on either side, and a mutation leg showing the assertion go red; owed only where the producer is readable, otherwise NOT_MEASURED with the reason. (covers AC-001, AC-002)
- FR-002: The per-mutation named-test witness — a mutation sweep records, per mutation, the named test that redded for it, using a witness case at the mutated predicate's boundary rather than merely inside its branch, and the sweep fails when any mutation's only witness is the sweep's own aggregate count. (covers AC-003)
- FR-003: The independence ladder — every second reading offered as an oracle is graded on the ordered rungs value, key, then library or runtime, and the record names the highest rung reached and the residual it does not escape. (covers AC-004)
- FR-004: The two mutually blind methods — a sweep-shaped remedy is measured both by execution in a different environment and by breaking its own completeness predicate, neither substitutes for the other, and the record says which was applied. (covers AC-005)
- FR-005: Controls that discriminate — each leg carries a negative control that reds on a deliberately wrong model and a positive control that is admitted, and each control asserts up front that its input has the shape it assumes. (covers AC-006)
- FR-006: Bound and terminal disposition — every requirement added states what it is owed on and what closes it, so a legitimate row on which a leg cannot be constructed closes rather than loops. (covers AC-007)
- FR-007: The work's own efficacy evidence is drawn from another author's model — the control set demonstrating that the new rules discriminate is composed of artifacts measured and recorded by other agents before this work began. (covers AC-008)

## Ambiguities

- AMB-001: **Is `coverage of a branch is not coverage of its predicate` a fourth requirement or a
  clause of FR-002?** It was earned expensively — a fixture reached a branch but never its boundary,
  three times on one row, and passed review each time — and it is a property of the *witness case*,
  not of the sweep's accounting. Resolved in clarification.
- AMB-002: **Where does the new material sit — a new top-level section, or inside
  `Gate-inversion evidence`?** A new top-level section is more citable; siting inside the existing one
  keeps `SKILL.md` §3's existing anchor live and avoids an out-of-lane edit. Resolved in clarification.
- AMB-003: **How is a prose contract's own efficacy demonstrated at all**, given that it adds no
  executable gate to the repository? Resolved in clarification.

## Public Or Tool-Facing Impact

- This specification is an SDD lifecycle artifact and command-report contract input.
- The declared file is a **kit source** (`.claude/skills/pnext-item`, `registry/repos.yml` `kit:`
  block). Editing it obliges an `FS.GG.Kit` coherent-set republish, declared as a post-merge obligation
  on the pull request in the machine form and **not** performed by this work.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2752-authorship-independent-verification-efficacy`.
