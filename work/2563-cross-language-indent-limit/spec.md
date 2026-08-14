---
schemaVersion: 1
workId: 2563-cross-language-indent-limit
title: Cross Language Indent Limit
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Cross Language Indent Limit Specification

Prose status: specified

## User Value
An engineer who changes the CommonMark indent limit for a delivery marker's leading line on ONE side
of the F#/Python boundary is told mechanically that the other side did not move. Today they are not:
`DeliveryApplication.leadingLine` and `check-kit-published-coherence.py`'s `_leading_line` each carry
their own copy of the rule, each side pins that copy with its own fixture legs, and the only thing
coupling them is two prose sentences that nothing reads. A ONE-SIDED edit reds that side's fixtures; a
COORDINATED one-sided edit — moving one language's constant AND updating that same language's legs to
match — passes both. That is not an exotic failure; it is what a careful engineer does when they
believe they are fixing a bug, and the fixtures agree with them the whole way.

The cost of the drift it would let through is the failure `.github#2544` exists to prevent: an
obligation declaration that one side of the fleet can see and the other cannot. The dangerous
direction is documented on the gate itself — a gate STRICTER than the engine reads a live declaration
as absent and is silently unflagged.

## Scope
- SB-001: **One shared, authored corpus** at `tests/delivery-leading-line/corpus.json`: comment
  bodies paired with the single verdict both sides must return — `declares` or `inert`. It is neutral
  ground; neither language owns it, and neither language generates it. This is the in-repo shape
  already used by `tests/skill-union/skillmirror.fixtures.json`, which one F# `fsi` driver, one shell
  conformance script and two Python gates all consume.
- SB-002: **The F# consumer**, in `tests/FS.GG.Coord.Cli.Tests/DeliveryApplicationTests.fs`, driving
  the engine's REAL entry point `DeliveryApplication.obligationsFromComments` — not the private
  `leadingLine` helper — once per corpus entry, and asserting BOTH halves of criterion 5: `declares`
  yields `Ok [obligation]`, `inert` yields `Error` whose reason names the leading-line rule.
- SB-003: **The Python consumer**, in `tests/kit-published-coherence/run.sh`, driving the gate's REAL
  entry point through `check-kit-published-coherence.py --obligation-arm` — which calls
  `obligation_declarations` — once per corpus entry.
- SB-004: **Non-vacuity on both consumers.** Each independently asserts the corpus is readable, that
  its entry count equals a stated literal, that both verdict classes are present, and that every entry
  it read was actually executed. A missing, empty, truncated or one-sided corpus is a red on each
  side separately, not a silent green on either.
- SB-005: **Reachability.** `tests/delivery-leading-line/**` is added to BOTH `paths:` copies of
  `.github/workflows/coord-engine.yml`, so an edit to the corpus alone starts the workflow that runs
  the F# consumer. `.github/workflows/kit-published-coherence.yml` is already deliberately unfiltered
  on `pull_request` (`.github#1597`), so it starts on every PR and needs no change.
- SB-006: **Retire the per-language duplicates the corpus replaces.** The single-body indent legs that
  today exist twice — once in `DeliveryApplicationTests.fs`, once in `run.sh` — are replaced by the
  corpus-driven legs, so the boundary is stated in exactly one place.
- SB-007: **Both implementations' prose is corrected to name the mechanism** rather than assert the
  coupling. `check-kit-published-coherence.py:459` and `_leading_line`'s docstring, and
  `DeliveryApplication.fs`'s comment above `leadingLine`, point at the corpus that now enforces what
  they used to only claim.

## Non-Goals
- SB-101: Does NOT change what declares. 0–3 spaces and leading blank lines declare; 4+ spaces or any
  tab is inert AND NAMED. The 18-shape agreement measured on `.github#2544` is reproduced before the
  change as the baseline and after the change as the regression bar.
- SB-102: Does NOT introduce a generated shared constant. Rejected under AC-006.
- SB-103: Does NOT introduce a source-text cross-language check that reads either implementation's
  literals. Rejected under AC-006.
- SB-104: Does NOT eliminate the Python implementation. Rejected under AC-006; criterion 4 of the item
  requires the gate to remain able to answer.
- SB-105: Does NOT change `obligation_declarations`' semantics, the `MERGE_AUTOMATION` table, the
  `#1772` tag arm, or any other arm of `check-kit-published-coherence.py`.
- SB-106: Does NOT remove the engine-only multi-comment legs from `DeliveryApplicationTests.fs` — the
  bystander-destruction leg, the declaration+receipt `Verified` pair, and the conditional-advice leg
  are multi-comment engine behaviours that a one-body-one-verdict corpus cannot express, and they stay
  where they are.
- SB-107: Does NOT re-file `.github#2551`.
- SB-108: Does not implement Governance policy enforcement.

## User Stories
- US-001 (P1): As an engineer who believes the indent limit is wrong and changes it in `F#` together
  with every `F#` leg that pins it, I get a red check telling me the Python side still holds the old
  limit, instead of a green PR that has silently split one rule into two.
- US-002 (P1): As the same engineer making the mirror-image edit in Python, I get a red check from the
  F# side — and the workflow that produces it actually starts, because the file I had to edit is
  declared in its trigger set.
- US-003 (P1): As a reviewer, I can read the boundary in ONE place and know both implementations are
  graded against it, rather than comparing two fixture lists in two languages and trusting a docstring.
- US-004 (P1): As the fleet, I keep the exact `#2544` behaviour: a bystander's indented code sample
  still cannot destroy somebody else's valid declaration, and an author who indents a real declaration
  four spaces is still TOLD so rather than silently ignored.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given the shared corpus and both consumers, when the F# indent limit is
  changed from `>= 4` to `>= 8` and the corpus is updated so the F# suite is green, then the
  `kit-published-coherence` fixture goes RED on the corpus legs whose verdict moved. Demonstrated by
  executing that mutation, not by reasoning about it.
- AC-002 [US-002] [FR-002]: Given the same, when the Python limit is changed from `>= 4` to `>= 8` and
  the corpus is updated so the fixture is green, then the F# suite goes RED. Demonstrated by executing
  that mutation.
- AC-003 [US-002] [FR-003]: Given a change confined to `tests/delivery-leading-line/corpus.json`, when
  the workflow trigger sets are evaluated, then BOTH `coord-engine` (via its declared
  `tests/delivery-leading-line/**` path, present in the `pull_request` and `push` copies alike) and
  `kit-published-coherence` (unfiltered) start.
- AC-004 [US-003] [FR-004]: Given the corpus is deleted, emptied, truncated below its declared entry
  count, or reduced to a single verdict class, when either consumer runs, then that consumer FAILS
  rather than passing over a smaller corpus than it claims to check.
- AC-005 [US-004] [FR-005]: Given the 18-body corpus measured on `.github#2544`, when it is run
  through `DeliveryApplication.obligationsFromComments` and through
  `obligation_declarations` after the change, then the verdicts are identical to the pre-change
  baseline: 7 declare, 11 inert, and every inert case is `Error` naming the leading-line rule.
- AC-006 [US-003] [FR-006]: Given the four shapes the item names, when the design decision is
  recorded, then the chosen one is stated with its residual limitation and each rejected one is stated
  with the specific property that disqualified it.
- AC-007 [US-004] [FR-007]: Given the engine-only multi-comment behaviours from `#2544` round 1, when
  the change lands, then those legs still exist and still pass unchanged.

## Functional Requirements
- FR-001: A coordinated one-sided edit of the F# limit plus every F# leg is caught by a red check. (Stories: US-001; Acceptance: AC-001)
- FR-002: A coordinated one-sided edit of the Python limit plus every Python leg is caught by a red check. (Stories: US-002; Acceptance: AC-002)
- FR-003: An edit to the shared corpus alone starts both grading workflows. (Stories: US-002; Acceptance: AC-003)
- FR-004: Neither consumer can pass over a missing, empty, short, or single-class corpus. (Stories: US-003; Acceptance: AC-004)
- FR-005: What declares is unchanged, and every inert case is still named. (Stories: US-004; Acceptance: AC-005)
- FR-006: The design choice among the four candidate shapes is recorded with rejections. (Stories: US-003; Acceptance: AC-006)
- FR-007: The engine-only multi-comment legs from `#2544` round 1 survive unchanged. (Stories: US-004; Acceptance: AC-007)

## Ambiguities
- AMB-001: WHICH of the four candidate shapes couples the two implementations — a cross-language
  coherence check, a shared generated constant, a fixture corpus both sides consume, or elimination of
  the second implementation. This is the question the row exists to settle on the record; the shapes
  have materially different blast radii, so it is resolved in `clarifications.md` rather than decided
  by whichever diff is smallest.
- AMB-002: WHERE the coupling artifact lives, which is not cosmetic but a reachability question: a
  gate keyed on `paths:` is selectively silent, so the file's location decides whether an edit to it
  starts the workflows that grade it (`.github#2551`).

## Public Or Tool-Facing Impact
- `tests/delivery-leading-line/corpus.json` becomes a contract surface: it is the single statement of
  the delivery-marker leading-line boundary, and two consumers in two languages grade against it.
- `.github/workflows/coord-engine.yml` gains one path in each of its two `paths:` copies. They must
  stay identical or `paths-coherence` reds (`.github#880`).
- No runtime behaviour of `scripts/fsgg-coord` or of the published kit changes.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2563-cross-language-indent-limit`.
