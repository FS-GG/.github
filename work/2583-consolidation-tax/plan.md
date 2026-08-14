---
schemaVersion: 1
workId: 2583-consolidation-tax
title: Consolidation Tax
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2583-consolidation-tax/spec.md
sourceClarifications: work/2583-consolidation-tax/clarifications.md
sourceChecklist: work/2583-consolidation-tax/checklist.md
publicOrToolFacingImpact: true
---

# Consolidation Tax Plan

Prose status: planned

## Source Snapshot
- spec: work/2583-consolidation-tax/spec.md sha256:539d885e05352d6df6f360fe2073711b394fdc92b75381ba197c9bffbf3b9fc3 schemaVersion:1
- clarifications: work/2583-consolidation-tax/clarifications.md sha256:fb17e82429e2f3c0f8fa21ba8b13ad83946304486be8aec65edcafeba3ed6053 schemaVersion:1
- checklist: work/2583-consolidation-tax/checklist.md sha256:4d579461c2e2535fc178e7a261d2745c6cd239a48db404d7ff7901e91849b15f schemaVersion:1

## Plan Scope
- Work item 2583-consolidation-tax is planned from the current specification, clarification, and checklist facts.
- Requirement count: 7.
- Clarification decision count: 4.
- Checklist result count: 8.

## Plan Decisions

The whole change is a **third candidate** in `Client.fs`'s existing candidate chain, joining
`.github#2392`'s canonical and legacy candidates. Nothing already reached is re-ordered: canonical is
still tried first, legacy second, and the new candidate is consulted only when both have declined —
which is why FR-003 holds by construction rather than by care.

- PD-001 [AC-001] [FR-001] complete: Add `deliveryRouteSubjectLines : string -> string list`, the same `Markdown.classify`-filtered lines `deliveryRouteSubject` already joins, exposed as a list. `deliveryRouteSubject` is re-expressed as `String.concat "
" << deliveryRouteSubjectLines` so the two can never disagree — one filter, two shapes, never two copies of the rule (#485).
- PD-002 [AC-002] [FR-002] complete: Add `additiveDeliveryRouteMatch : recorded: string list -> body: string -> receipt -> int option`. It locates the recorded locator digests as an ordered subsequence of the current subject's locator digests with one greedy leftmost scan, joins the **matched current lines**, hashes them with the unchanged `hashHex`, and accepts only when that equals `receipt.SubjectRevision`. Modification, deletion, and reordering all fail the scan or the hash. Returns the inserted-line count on acceptance.
- PD-003 [AC-003] [FR-003] complete: Wire the candidate as the third arm of `decideDeliveryRoute`, reached only after both existing arms decline, and re-decide through `DeliveryRoute.decide` passing `receipt.SubjectRevision` as the expected revision — the same shape the legacy arm already uses — so every other `validate` rule (schema, subject, agent, timestamp, route, SDD binding) still runs and only the revision comparison is satisfied by the proof just established.
- PD-004 [AC-004] [FR-004] complete: `decideDeliveryRoute` is split into `decideDeliveryRouteMatch` returning `Verdict * SubjectMatch`, with `decideDeliveryRoute` as its `fst`. `delivery-route show` consumes the pair and renders `subjectMatch` and `addedSubjectLines`, and — with `requireCurrentDeliveryRoute`, the claim/take mutation boundary — writes the note from ONE shared spelling, `additiveSubjectNote`. The per-candidate scheduling reads (`readDeliveryRouteVerdict`, `sddPackageTokens`) keep the `fst` form and stay silent, with the reason stated on `decideDeliveryRoute` itself rather than left as an accident. The AMB-001 trade is paid where it is spent: at the boundary a worker commits from, not only where an agent may choose to look.
- PD-005 [AC-005] [FR-005] complete: FOUR gate-inversion mutations, one per guard this work adds: the third candidate arm, the full-width verification, the empty-judged-subject refusal, and the claim-boundary notice. The corpus is real issue bodies under `tests/FS.GG.Coord.Cli.Tests`, with a stated size floor (#436). The degenerate empty-subject legs are held OUTSIDE that floor deliberately — review round 1 established that `MinimumSubjectLinesPerBody` excluded the very shape that carried a permanent false positive, so a non-vacuity floor on a CORPUS is not a non-vacuity guarantee for the CODE.
- PD-006 [AC-006] [FR-006] complete: `deliveryRouteCmd`'s `record` arm derives the locator line from the same `body` it computed `revision` from, and posts `marker + "
" + locatorLine + "
" + raw.Trim()`. The agent's `raw` stays byte-verbatim.
- PD-007 [AC-007] [FR-007] complete: `latestDeliveryRouteReceipt` returns `(Receipt * string list option)`. The locator line is consumed only when the first line after the marker starts with `<!-- fsgg:delivery-route-subject-lines/v1 ` and ends with `-->`; anything else leaves the remainder to JSON decoding untouched and yields `None`, which disables the third arm entirely. Absence is never read as permission.
- PD-008 [DEC-005] acceptedDeferral: Receipts recorded before this change carry no locator line, so the third arm is never consulted for them and they keep exactly today's behaviour until re-recorded. No migration writes to existing receipt comments; re-recording is itself a route re-affirmation and is the correct act for a subject an agent wants re-judged.
- PD-009 [CR-008] acceptedDeferral: The checklist's mirror of DEC-005 carries the same disposition as PD-008 and needs no separate task: it is one deferral observed at two stages, not two.

### Why the locator width is not the safety boundary

The locator digests are 16 hex characters of the line's SHA-256. They select *which* current lines
correspond to the judged ones; they never decide acceptance. Acceptance is the full-width `hashHex` of
the reconstructed subsequence against the receipt's own `subjectRevision` — a 256-bit check, the same
one `.github#2392` already rests on. A locator collision can therefore only mis-select an alignment,
which then fails that check: a false **negative**, never a false positive. A false positive would need a
full SHA-256 collision.

The same fact makes the single greedy scan exact. In the no-collision case a locator is a full-strength
identity of one line, so every valid alignment reconstructs byte-identical text and there is nothing for
a search to find that greedy leftmost matching would miss.

### Why this is not in `FS.GG.Coord.Core`

`deliveryRouteSubject` is built on `Markdown.classify`, and `.github#2392`'s own source comment records
that `DeliveryRoute.fs` compiles **ahead of** `Markdown.fs` in `FS.GG.Coord.Core.fsproj`. A Core-side
rule would have to reorder that compile graph or keep a second copy of the subject filter. The subject
*scheme* has always been `Client.fs`'s; `DeliveryRoute.decide` owns receipt *policy* and is unchanged.
- PD-010 [AC-008] [FR-008] complete: Give `additiveSubjectMatch` a three-case `AdditiveOutcome` and decide the empty-`recorded` case as `JudgedNothing` BEFORE alignment, appending its own diagnosis to the refusal via `withReason`. The shape is PORTED from `scripts/check-gate-finding-history.py` — this repository's only other anti-vacuity floor — whose zero-runs arm is decided before the floor is consulted and carries its own detail string; its `LOW-SAMPLE` arm deliberately does NOT port, because a judged subject has no sample-size gradient. The first cut refused in the right place but returned a bare `None`, reporting the degenerate case as an ordinary stale receipt. Without it `align` consumes an empty want-list vacuously and the full-width check compares `hashHex ""` against a recorded revision that IS `hashHex ""` — satisfied by construction, for every possible body, with no collision. The guard is load-bearing, not defensive, and the safety claim on `subjectLineLocator` is rewritten to carry its condition rather than assert the unconditional form that shipped in review round 1.
- PD-011 [CR-009] acceptedDeferral: The checklist-stage mirror of DEC-005, arriving a second time with FR-008's regeneration; same disposition as PD-008/PD-009 and no separate obligation.

## Contract Impact
- PC-001 [PD-001] command report: `fsgg-sdd plan`, `work/2583-consolidation-tax/plan.md`, and command-report JSON are tool-facing and compatibility-preserving.
- PC-002 [PD-004] command report: `delivery-route show --json` gains `subjectMatch` (`canonical` | `legacy` | `additive`) and `addedSubjectLines` on the existing `fsgg.coord.delivery-route-result/v1` envelope. Both are additive; no field is removed or retyped, so the envelope version is unchanged.
- PC-003 [PD-006] receipt envelope: the `<!-- fsgg:delivery-route/v1 -->` comment gains an optional sibling `<!-- fsgg:delivery-route-subject-lines/v1 … -->` line. Receipts written before this change parse byte-unchanged, and `DeliveryRouteApplication.decode` is not touched.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: `deliveryRouteSubjectLines` and `deliveryRouteSubject` agree on every corpus body — the joined lines equal the string the existing function returns, so the single-filter claim is measured rather than asserted. Driven through the command boundary (`DeliveryRouteCliTests`) against a scripted transport, and separately through the shared Release engine with `dotnet fsi` over live bodies fetched with `gh api`.
- VO-002 [PD-005] [PC-002] semanticTest: Gate-inversion. Delete the third candidate arm from `decideDeliveryRoute`, run `tests/FS.GG.Coord.Cli.Tests`, and record the exact mutation and the observed failing legs. A surviving inversion is a material finding by definition (`.github#2551`).
- VO-003 [PD-003] [PC-002] semanticTest: Regression floor for `.github#2392`. A `Paths:`/`Class:`/`Blocked on:`/`Blocked by:` edit and a pre-`.github#2392` whole-body receipt both still resolve `Current`, and the legacy arm is exercised on a receipt the canonical arm rejects.
- VO-004 [PD-005] [PC-002] semanticTest: Non-vacuity. The corpus of real issue bodies is asserted non-empty and at a fixed floor, so a fixture that silently emptied cannot pass the additive legs.
- VO-006 [PD-010] [PC-002] semanticTest: FIVE mutations now, the fifth being the ported NAMING: deleting `withReason`'s diagnosis leaves the refusal correct and reds only the leg that reads it, so the naming is shown load-bearing independently of the refusal. A discriminator leg asserts an ordinary stale receipt does NOT carry the diagnosis. Degenerate-body legs, outside the corpus floor: an empty-subject receipt refuses a wholesale body replacement and refuses a strictly additive edit, while an unchanged empty-subject body stays `Current` through the CANONICAL arm. Inverting the empty guard reds the two refusal legs and only those.
- VO-007 [PD-004] [PC-002] semanticTest: The claim/take mutation boundary emits the additive notice, stays silent on a canonical match, and still refuses a modified judged line — the third leg being the discriminator that stops the first two being explained by "the route check never runs". Inverting the notice reds the first leg and only it.
- VO-005 [PD-004] [PC-002] semanticTest: The workflow that runs these legs is reached by the changed paths. `.github/workflows/coord-engine.yml` lists `src/FS.GG.Coord.Cli/**` and `tests/FS.GG.Coord.Cli.Tests/**` in both its `pull_request` and `push` `paths:` filters; confirm on the live pull request that `coord-engine` actually ran.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-003] diagnoseOnly: There is no data migration. The receipt envelope change is forward-compatible in both directions — a post-change reader parses a pre-change comment (no locator line, third arm disabled), and a pre-change reader would fail to JSON-decode a post-change comment, which is why the engine-freshness guard that already refuses board writes from a stale engine is the mechanism that keeps the two apart rather than a new check.

## Generated View Impact
- GV-001 [PD-004] workModel: `readiness/2583-consolidation-tax/work-model.json` is regenerated from these plan sources. No other generated view is affected: `delivery-route show`'s two new fields are additive on an unchanged envelope version, so no registry, projection, or emitted-contract version moves with this change.

## Accepted Deferrals
- DEC-005 acceptedDeferral: Retroactive upgrade of pre-change receipts is deferred, with no recoverable data to upgrade from; visible to tasks and evidence as a stated limitation, not an oversight.
- CR-008 acceptedDeferral: The checklist-stage mirror of DEC-005; same disposition, no separate obligation.
- CR-009 acceptedDeferral: The checklist-stage mirror of DEC-005 regenerated alongside FR-008; one deferral observed at two stages, discharged once by PD-008 and visible to tasks and evidence under the same disposition.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2583-consolidation-tax`.
