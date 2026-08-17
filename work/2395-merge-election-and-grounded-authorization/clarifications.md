---
schemaVersion: 1
workId: 2395-merge-election-and-grounded-authorization
title: Merge Election And Grounded Authorization
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/2395-merge-election-and-grounded-authorization/spec.md
publicOrToolFacingImpact: true
---

# Merge Election And Grounded Authorization Clarifications

## Source Specification
- work/2395-merge-election-and-grounded-authorization/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking open: Who posts the election and when — one `delivery` call or two —
  and what happens if the second write fails after the first succeeded?
- CQ-002 [AMB:AMB-002] blocking open: How are `opkey` and `grant` derived, and what makes them
  verifiable rather than decorative?
- CQ-003 [AMB:AMB-003] blocking open: Every open pull request today carries a four-field marker. Does
  the fence accept both shapes during a transition, is there a cutover, or do existing pull requests
  get rebound?
- CQ-004 [AMB:AMB-004] blocking open: What proves check 4 is now reachable, rather than merely
  differently unreachable?

## Answers

- CQ-001: One `delivery` call, two REST writes, in the design's own order — the election first, the
  authorization second. They cannot be atomic, so the order is chosen for the failure window rather
  than for symmetry, and the election is made idempotent so that repeating the call is the recovery.
- CQ-002: `opkey` comes from `FS.GG.Coord.Operation.compose`, the closed-vocabulary key slice 1
  landed; `grant` is the comment id GitHub assigns to the election, which no caller chooses.
- CQ-003: Neither a cutover nor a rebinding campaign. The armed gate requires four fields and accepts
  supersets, the wider gate is observe-only, and the marker self-heals on the next `delivery` call.
- CQ-004: An executed gate run over a body composed by the production writer, with controls, showing
  check 4 reached and failing at the lowest-election comparison itself.

## Decisions

- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-002] [FR-003] [FR-005] [AC-002] [AC-003] [AC-006]: One
  `delivery` call performs both acts, election first. GitHub offers no atomic multi-write, so the
  ordering is the design: an election is append-only, carries no lease and is never deleted, so a
  failure after posting it and before writing the authorization leaves a durable, reusable fact and
  the next `delivery` call completes the pair. The reverse order has no such property — it would
  write an authorization naming an election that does not exist, and `grant` would name an id that
  may later belong to an unrelated comment. This is deliberately unlike the `claim --force`
  non-atomicity measured on 2026-08-16, where the first act was a DESTRUCTIVE delete and a transport
  failure between the two left the row with no holder at all: here both acts are non-destructive, an
  append and an idempotent replace-in-place, so the intermediate state is strictly weaker than the
  final one rather than worse than the initial one. Two consequences are load-bearing. First,
  posting unconditionally would be a correctness DEFECT, not merely wasteful: a second election for
  the same key carries a strictly higher comment id, so an authorization naming it would lose the
  gate's own lowest-id comparison and the pull request would be refused forever under that
  generation. Second, a grounding that cannot be established refuses rather than degrades — no
  four-field fallback is written — because a marker the wider gate calls ungrounded is exactly the
  decorative case the design names, and a failed read must never masquerade as an answer.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-001] [FR-004] [AC-001] [AC-005]: `opkey` is
  `Operation.compose item generation receiver Merge` — `sha256(item \n gen \n receiver \n op)` in
  lowercase hex — taken from `src/FS.GG.Coord.Core/Operation.fsi` rather than re-expressed, so the
  producer and `scripts/check-claim-fence.py`'s check 5 cannot disagree about a key by construction.
  `item` is the fully-qualified `owner/repo#n`, `generation` is the winning claim marker's comment
  id, and `receiver` is the repository the merge lands in. `grant` is the comment id GitHub assigns
  to the election marker. What makes the pair verifiable rather than decorative is that neither is
  chooseable by whoever writes the marker: check 5 recomputes `opkey` from the marker's own `item`
  and `gen` plus the evaluating repository, and check 4 re-reads the item and re-derives the lowest
  election id for that key. A forger who invents an id fails an existence read; a forger who posts a
  real election to obtain a real id has entered the election and can only be lowest once. The
  ordering that decides "lowest" is asked of `Reads.lowestId`, exported by slice 2 for exactly this
  read; a second `sortBy id |> tryHead` in the CLI layer is the copy the design forbids.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-006] [AC-007] [AC-008]: No cutover, no dual-shape
  acceptance, no rebinding campaign — and this is a measured fact about the live branch protection,
  not an assumption. The only marker reader that is a required status context on `main` is
  `claim-generation`; its required field set is four and its own docstring commits to accepting
  additional pairs, so a six-field marker passes it and an existing four-field marker keeps passing
  it. `scripts/check-claim-fence.py` is the six-field reader, and it is observe-only — arming it is
  slice 8 — so no pull request can fail a required check because of this change. Existing pull
  requests are upgraded by the flow that already exists: `rebindAuthorization` replaces any marker
  that is not byte-identical to the freshly rendered one, so the next `delivery` call on a live claim
  rewrites a four-field marker into a six-field one with exactly one marker remaining. The one place
  the change genuinely bites is a cross-check nobody would find by reading the gates:
  `tests/receiver-validate/run.sh` asserts SET EQUALITY between the receiver gate's required fields
  and the producer's written fields, which reds the moment the producer writes a superset. That
  assertion mis-states the contract — the receiver gate is documented as tolerating additional pairs
  — so it is corrected to a subset assertion here, in this row's declared paths, rather than left to
  red on merge.
- **DEC-004** [CQ-004] [AMB:AMB-004] [FR-007] [AC-009]: Reachability is demonstrated by executing
  `scripts/check-claim-fence.py` against a pull-request body composed by the production writer and an
  item whose comments carry the election that writer would post, with the gate's own network reads
  stubbed. Four observations are required and a fifth is the control. Check 4 must be REACHED and
  PASS on the grounded body. Check 4 must FAIL when no election bears the key. Check 4 must FAIL at
  its own BOUNDARY — a competing election exactly one comment id lower, so the failure is the
  `min`-comparison rather than an existence test that a different bug would also produce. Check 4
  must FAIL when the winning election records a different receiver, which is the recorded-fields arm.
  The control is that today's four-field marker still stops at check 1 on the same harness, which is
  what makes the four observations evidence about the marker's field set rather than about a harness
  that grades everything. A six-field marker with no executed gate run would move the problem rather
  than close it, which is the specific failure this row exists to repair.

## Accepted Deferrals

- **DEC-005** [CQ-004] [FR-007]: The permanent, committed producer-versus-fence agreement leg —
  pinning this producer's field set against `REQUIRED_AUTH_FIELDS` in both directions inside the
  fence's own corpus — is deferred to `.github#2719`, which declares `scripts/check-claim-fence.py`
  and `tests/claim-fence` and must write it after this row lands, because the leg reds until the
  six-field marker exists. Recorded rather than dropped: this row's obligation is the executed
  demonstration of DEC-004, not that leg.

## Remaining Ambiguity
- None. AMB-001, AMB-002, AMB-003 and AMB-004 are resolved by the decisions above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2395-merge-election-and-grounded-authorization`.
