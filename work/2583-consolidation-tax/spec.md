---
schemaVersion: 1
workId: 2583-consolidation-tax
title: Consolidation Tax
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Consolidation Tax Specification

Prose status: specified

## User Value

An agent consolidating the board can fold another row's cause into an existing item body without
de-scheduling that row, while an edit that redefines what the route decision judged still stales the
receipt and re-opens the route decision.

**Measured, on this item's own live body, at `cb33188c` on 2026-08-14.** `.github#2583`'s recorded
receipt carries `subjectRevision: 7a1157a6…3e` and `delivery-route show .github#2583 --json` returns
`kind: "current"`. Replaying `Client.fs`'s own computation (`deliveryRouteSubject` →
`hashHex`, validated first against `.github#2512`'s already-accepted receipt — CONTROL MATCH) over four
candidate edits of that body:

| edit | recomputed subject revision | verdict today |
| --- | --- | --- |
| A — append `## Folded from …`, adds only | `591081ac…9e` | **STALE** |
| B — insert `## Folded from …` before `## Dedupe`, adds only | `e663c869…4e` | **STALE** |
| C — widen the `Paths:` line (`.github#2392`'s fix) | `7a1157a6…3e` | current |
| D — change `Severity: High` to `Severity: Low` | `b6a9e19e…d6` | **STALE** |

A and B are the two shapes a consolidation actually takes, and both cost the row its schedulability:
`DeliveryRoute.Stale` is mapped by `Schedulability.fs:146-148` to `AwaitingDeliveryRouteDecision`, which
removes the row from `batch`. D is a genuine scope change and must keep costing exactly that. C is
`.github#2392` working, and must keep working. Today A, B and D are the same answer.

The same tax is visible without any hypothetical: `delivery-route show .github#1858 --json` returns
`malformed response reading FS-GG/.github#1858: subjectRevision is stale. That is a FAILED READ`.

## Scope

- SB-001: The subject-revision **candidate set** and the delivery-route receipt **comment envelope** in
  `src/FS.GG.Coord.Cli/Client.fs`, plus command-boundary coverage in `tests/FS.GG.Coord.Cli.Tests`.
- SB-002: `DeliveryRoute.decide`/`validate` policy in `FS.GG.Coord.Core` is unchanged, as is
  `Schedulability`'s mapping of `Stale` to `AwaitingDeliveryRouteDecision`, and `.github#2392`'s
  volatile-line exclusion and `legacyDeliveryRouteRevision` migration bridge.
- SB-003: The rule is **structural**, stated once as a property of the edit rather than as a shape of
  the diff: the judged subject must survive as an ordered subsequence of the current subject.
- SB-004: The scheme lives in `Client.fs` beside the canonical and legacy candidates because it must.
  `deliveryRouteSubject` is built on `Markdown.classify`, and `.github#2392`'s own comment records that
  `DeliveryRoute.fs` compiles **ahead of** `Markdown.fs` in `FS.GG.Coord.Core.fsproj`; a Core-side rule
  would have to reorder that compile graph or duplicate the subject filter. This is a compile-graph
  fact, not a diff-size preference.

## Non-Goals

- SB-005: No semantic judgement of *what* was inserted. The rule cannot read intent, and this
  specification does not pretend otherwise — it discharges that limit through FR-004's visibility
  requirement rather than hiding it.
- SB-006: No narrowing of the hashed subject. Hashing fewer lines would buy consolidation by making a
  genuine scope change invisible, which is the property the receipt exists to provide.
- SB-007: No retroactive upgrade of already-recorded receipts. A receipt recorded before this change
  carries no judged-line record and therefore keeps exactly today's behaviour until it is next
  recorded — the same posture `.github#2392` AC5 took for its own legacy bridge.

## User Stories

- US-001 (P1): As a board-consolidating agent, I can fold another row's cause into an existing item body
  and leave that row schedulable, so that consolidation is not priced above filing.
- US-002 (P1): As a delivery-route reader, I still get `Stale` whenever an edit changed, removed, or
  reordered something the route decision judged, so that a route affirmed against one subject is never
  silently reused for another.
- US-003 (P2): As an agent reading a route receipt, I can tell an additively-resolved read from a
  byte-identical one, so that the one edge this rule cannot judge is visible rather than silent.

## Acceptance Scenarios

- AC-001 [US-001] [FR-001]: Given a receipt recorded against a body, when the body is edited so that new
  subject lines are inserted and no existing subject line is changed, removed, or reordered, then the
  verdict is `Current`. Holds for insertion at the end, at the start, and in the middle.
- AC-002 [US-002] [FR-002]: Given the same receipt, when any judged subject line is modified, deleted,
  or moved relative to the others, then the verdict is `Stale` and carries `subjectRevision is stale`.
- AC-003 [US-002] [FR-003]: Given any body/receipt pair that resolves `Current` before this change —
  including a `Paths:`/`Class:`/`Blocked on:`/`Blocked by:` edit and a pre-`.github#2392` whole-body
  receipt — then it still resolves `Current` after it.
- AC-004 [US-003] [FR-004]: Given a read that resolved through the additive candidate, then **both**
  boundaries an agent inspects or acts on report it: `delivery-route show --json` names the additive
  match and the inserted-line count and writes a note to stderr, and the claim/take mutation boundary
  writes the same note. The per-candidate scheduling read stays silent by stated choice, and the source
  says so rather than claiming reporting is universal.
- AC-005 [US-001] [FR-005]: Given the additive candidate is removed from the candidate set, when the
  command-boundary suite runs over its corpus of real issue bodies, then the additive legs fail. The
  corpus is non-empty and its size is asserted, so the leg cannot pass vacuously.
- AC-006 [US-001] [FR-006]: Given `delivery-route record` posts a receipt, then the posted comment
  carries the judged-line record derived from the very body whose `subjectRevision` was just validated,
  and the agent-authored receipt JSON is byte-unchanged within that comment.
- AC-007 [US-002] [FR-007]: Given a receipt comment with no judged-line record, or one whose record is
  malformed, then the verdict is exactly what it is today — the additive candidate is not consulted and
  nothing is inferred from its absence.
- AC-008 [US-002] [FR-008]: Given a receipt recorded against a body whose subject is EMPTY — every line
  a volatile declaration or blank — then no later body resolves `Current` through the additive
  candidate, including a wholesale replacement by unrelated content and a strictly additive edit. A body
  whose subject is still empty remains `Current` through the canonical candidate, unchanged. The refusal
  additionally carries its **own named diagnosis**, distinct from an ordinary stale receipt, so a reader
  is told the receipt judged nothing rather than sent hunting for a damaged locator record.

## Functional Requirements

- FR-001: A body edit whose only effect is to insert new subject lines leaves an otherwise-current receipt `Current`, for insertion at any position, not only at the end. (Stories: US-001; Acceptance: AC-001)
- FR-002: A body edit that modifies, removes, or reorders any subject line the receipt judged still returns `Stale`. (Stories: US-002; Acceptance: AC-002)
- FR-003: No body and receipt pair that resolves `Current` before this change resolves `Stale` after it. (Stories: US-002; Acceptance: AC-003)
- FR-004: A read that resolves through the additive candidate reports that fact and the number of inserted subject lines at every boundary that inspects or acts on the row — `delivery-route show` and the claim/take mutation boundary — and stays silent on the per-candidate scheduling read by stated choice. (Stories: US-003; Acceptance: AC-004)
- FR-005: Removing the additive candidate makes the new command-boundary legs fail on a non-empty corpus of real issue bodies. (Stories: US-001; Acceptance: AC-005)
- FR-006: `delivery-route record` derives the judged-line record from the body it validated the receipt's `subjectRevision` against, and posts it without altering the agent-authored receipt JSON. (Stories: US-001; Acceptance: AC-006)
- FR-007: A receipt with a missing or malformed judged-line record decides exactly as it does today, fail-closed, with the additive candidate never consulted. (Stories: US-002; Acceptance: AC-007)
- FR-008: An empty judged subject never authorises an additive match against any body, because it constrains nothing. (Stories: US-002; Acceptance: AC-008)

## Ambiguities

- AMB-001: A pure insertion **can** redefine scope (`## Also: migrate every downstream repo` inserts
  cleanly). The structural rule accepts it. Is that acceptable, and if so what pays for it?
- AMB-002: How wide must each judged-line digest be? A truncated digest admits a modified line that
  collides with the original — a scope change made invisible, which SB-002 forbids — while a full
  SHA-256 per line makes the receipt comment a multi-kilobyte wall of hex.
- AMB-003: Who authors the judged-line record: the agent, in the receipt JSON, or `record`, derived?
- AMB-004: Where does the judged-line record live: inside the receipt JSON object, or beside it in the
  marker comment?
- AMB-005: An EMPTY judged subject makes the full-width verification hold **by construction**
  (`hashHex ""` compared against a recorded revision that *is* `hashHex ""`), so it would authorise an
  additive match against any body at all, with no hash collision. Is that a special case bolted on, or
  an instance of a rule this specification already states?
- AMB-006: The additive notice is the entire consideration for AMB-001's trade. Which boundaries must
  pay it? The per-candidate scheduling read runs many times per command, so "everywhere" is not free.

## Review History

**The degenerate case's shape is ported, not invented.** `scripts/check-gate-finding-history.py` is this
repository's only other anti-vacuity floor (`DEFAULT_MIN_RUNS = 10`) and it does not exclude its
degenerate case: zero runs gets its own verdicts with their own detail strings, decided *before* the
floor is consulted (`NEVER-RAN`, `REUSABLE-ELSEWHERE`), and `--min-runs 0` is refused outright with the
reason recorded at the site. The mapping was verified arm by arm before porting rather than assumed:

| that gate's arm | maps here? |
| --- | --- |
| degenerate case gets its own verdict *before* the floor | **yes** — `AdditiveOutcome.JudgedNothing`, decided before alignment, carrying its own reason |
| `LOW-SAMPLE` for below-floor-but-nonzero | **no** — and inventing one would force a fit. Sample size is a gradient for run history; it is not one for a judged subject. The full-width hash over one judged line is exactly as strong as over seventy. Zero is a discontinuity, not a small sample |
| refuse a floor configured to zero | **in reasoning only** — there is no runtime knob here to refuse, so the port is a consumed-site assertion that both corpus floors exceed zero, with the reason at the site |

Independent review round 1 (`merlin-0da3`, head `f090fb48`) upheld the AMB-001 ruling explicitly and
returned two material findings against this specification's own new arm: the empty-subject false
positive (AMB-005 / FR-008) and the notice never reaching the boundaries that act on the row
(AMB-006 / FR-004). Both are repaired in this package rather than filed as new rows, because both causes
are created by this work.

## Public Or Tool-Facing Impact

- `delivery-route show --json` gains two additive output fields on the existing
  `fsgg.coord.delivery-route-result/v1` envelope. No field is removed or retyped.
- The `<!-- fsgg:delivery-route/v1 -->` comment gains an optional sibling marker line. Receipts written
  before this change parse unchanged.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2583-consolidation-tax`.
