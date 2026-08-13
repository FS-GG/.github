---
schemaVersion: 1
workId: 2527-post-acceptance-head-move
title: Post Acceptance Head Move
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2527-post-acceptance-head-move/spec.md
sourceClarifications: work/2527-post-acceptance-head-move/clarifications.md
sourceChecklist: work/2527-post-acceptance-head-move/checklist.md
publicOrToolFacingImpact: true
---

# Post Acceptance Head Move Plan

Prose status: planned

## Source Snapshot
- spec: work/2527-post-acceptance-head-move/spec.md sha256:94026f95ab19539a0127e336ec36fd75d53c5de8dfbd04fbc999e8775eefe027 schemaVersion:1
- clarifications: work/2527-post-acceptance-head-move/clarifications.md sha256:4cbe88d98679a414a3e0ca162a5501bb784d689d5496d55050a1bd1cc3063084 schemaVersion:1
- checklist: work/2527-post-acceptance-head-move/checklist.md sha256:ad0c111e78fd79e444cec8fcce2bdad1e8ec7724dc64b27aa5cc44060a5e3ade schemaVersion:1

## Plan Scope
- Work item 2527-post-acceptance-head-move is planned from the current specification, clarification, and checklist facts.
- Requirement count: 7.
- Clarification decision count: 2.
- Checklist result count: 7.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add `Driver.liveReviewComments currentHead comments` — a pure read-time partition built on the EXISTING `classifyMarkers` groups and the existing private `field` reader, so no second marker parser is introduced (`.github#2175` acceptance 11). It returns the live comment list, the retired chains, and the near-miss diagnostics below. `Review.classify` calls it once and feeds `outcome.Live` to BOTH `Driver.reviewPhaseFacts` and `acceptanceOutcome`'s `Driver.parseReviewCommentsWithFacts`, so the two parsers never disagree about which chain they are reading.
- PD-002 [AC-002] [AC-003] [AC-004] [AC-010] [FR-002] complete: A chain is retired if and only if a host-acceptance marker (a) carries `Protocol.lifecyclePolicy.HostAcceptanceFields[1]` (`initial-review:`) equal to the URL of an initial-review comment present on this PR, and (b) carries `HostAcceptanceFields[0]` (`accepted-head:`) different from the binding's current head. The comments retired with it are that initial comment, that acceptance comment, and every confirmation whose own `initial-review:` names the same URL — the back-reference `Driver.parseReviewCommentsCore` already requires and validates (`initialUrl = first.Url`), reused rather than reinvented. An initial comment with a blank URL can never be named and is therefore never retired.
- PD-003 [AC-002] [FR-002] complete: **Retirement fires only when more than one canonical initial marker is present.** This is a hard compatibility boundary, not a convenience: `tests/FS.GG.Coord.Core.Tests/ReviewTests.fs` `#2175 a changed head after acceptance invalidates the prior accepted evidence` pins the SINGLE-chain accepted-then-moved case to `MalformedEvidence`/`Park`, and that file is declared by a different live claim (`.github#2525`) and cannot be edited here. Gating on the competing-marker condition makes the change provably behaviour-preserving for every PR that carries one chain, and reduces the mechanism to what it is actually for: choosing between two chains, never re-classifying one.
- PD-004 [AC-005] [FR-003] complete: The `InitialCount > 1` refusal keeps its current leading sentence byte-for-byte — `ReviewTests.fs` asserts the substring `"2 comments"` and that assertion must keep passing — and APPENDS the retirement rule plus the specific near miss found (`an acceptance names initial review <url>, but its accepted-head <sha> is the current head`; `an acceptance's initial-review <url> names no initial marker on this PR`; or, when no acceptance is present at all, that fact). Same near-miss-naming convention as `malformedVerdictReason` (`.github#2369`) and `resumeSameCriticReason` (`.github#2417`).
- PD-005 [AC-006] [FR-004] complete: `Review.Verdict` gains `RetiredChains: Driver.ChainRetirement list`, populated by `makeVerdict`; `ReviewApplication.fs` serializes it as a `retiredChains` array of `{initialReview, acceptedHead, acceptanceCommentId}`. Reusing `Driver`'s record rather than minting a second one keeps one description of a retired chain. The list is empty for every verdict that retires nothing, so every existing consumer is unaffected and `advance`'s freshness-token/action-key pair is untouched (the digest is over the token, state, and action only).
- PD-006 [AC-007] [FR-005] complete: No change to `acceptanceOutcome`. Once the retired chain's acceptance is out of the live set, `phaseFacts.AcceptancePresent` is false for the surviving chain, `acceptanceOutcome` is never reached, and no `Accept`/`AcceptedReceipt` is produced — `acceptedReceipt` stays null. The pre-existing head-mismatch refusal inside `acceptanceOutcome` remains as the second, independent line of defence and is asserted rather than removed.
- PD-007 [AC-008] [FR-006] complete: `liveReviewComments` returns new lists; it never mutates, reorders, or rewrites the supplied comments, and nothing in this change writes to GitHub. Retirement is therefore invisible at the source: the retired critic's marker, its confirmations, and the acceptance stay exactly as posted, which is what makes this mechanism compatible with the append-only rule that competing markers exist to protect.
- PD-008 [AC-009] [FR-007] complete: `tests/review-post-acceptance-head-move/run.sh` drives the COMPILED `fsgg-coord-engine review --snapshot --json` — the pure decision path, no board, no token, no network — over crafted snapshots, exactly as `tests/review-critic-succession-wire/run.sh` does for `.github#2417`, and for the identical reason: the natural xunit home (`tests/FS.GG.Coord.Core.Tests`) is declared by a different live claim (`.github#2525`) and `widen` refuses on OVERLAP rather than silently taking it. The fixture is WIRED into `.github/workflows/coord-engine.yml` (step plus both `paths:` lists) so `scripts/test` does not report it UNWIRED — a gate CI never runs is the `#266` defect this repository exists to end.
- PD-009 [AC-005] [FR-003] complete: `independent-review.md` is edited once and the byte-identical `.agents/skills` kit mirror updated to match. The new subsection states the case, the retirement rule, the two conditions, the explicit statement that the retired chain is never rewritten, the fresh critic's obligation to perform a genuinely full review (never a confirmation of the retired critic's finding), and the manual close-and-reopen fallback for when the evidence is absent. No `Protocol.reviewPolicy` fact changes, so no `generate-projections` region in that file moves.

## Contract Impact
- PC-001 [PD-001] [PD-005] public-surface: `FS.GG.Coord.Core.Driver` gains one record type and one function; `FS.GG.Coord.Core.Review.Verdict` gains one field; the `review --json` wire gains one array. All additive: no existing type, case, field, or required JSON key changes shape, and the live `review <ref> --pr N` path in `Client.fs` needs no edit because `Review.inspect`'s signature is unchanged.
- PC-002 [PD-009] kit-content: `.claude`/`.agents` `pnext-item/references/independent-review.md` is content-addressed coordination-kit source. The change owes a coherent-set `<Version>` bump and a published kit before merge; `kit-published-coherence` reds `main` until the changed kit is published.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-005] [PD-006] semanticTest: `tests/review-post-acceptance-head-move/run.sh` covers, at minimum: (1) RECOVERY — acceptance at H1 naming chain A, second initial marker reviewing H2, binding at H2, yields a non-malformed state classified from chain B with `acceptedReceipt` null and `retiredChains` naming chain A; (2) NO ACCEPTANCE — the same two initial markers without any acceptance still park as malformed; (3) ACCEPTANCE STILL BINDING — an acceptance whose `accepted-head` equals the current head retires nothing and still parks; (4) DANGLING REFERENCE — an acceptance whose `initial-review` names no initial marker present retires nothing and still parks; (5) the refusal in legs 2–4 names the retirement rule, not the bare count alone; (6) GATE INVERSION — with the retirement rule reduced to the identity function, leg 1 reds.
- VO-002 [PD-003] regressionTest: `dotnet test tests/FS.GG.Coord.Core.Tests` and `dotnet test tests/FS.GG.Coord.Cli.Tests` must be green UNCHANGED — in particular `#2175 a changed head after acceptance invalidates the prior accepted evidence`, `#2175 a duplicate initial marker across two comments is malformed`, and `#2175 a duplicate acceptance marker is malformed`, which are this change's compatibility boundary and its already-authored controlled counterpart for AC5.
- VO-003 [PD-009] projectionCheck: `scripts/generate-projections --check` must stay clean, proving the prose added to `independent-review.md` sits outside every generated region and that no `Protocol.fs` fact was implicitly changed.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive-no-migration: Every surface change is additive; no stored snapshot, marker text, or persisted artifact requires migration. The kit-content edit requires a version bump and republish, which is a release step, not a data migration.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2527-post-acceptance-head-move/work-model.json` and `analysis.json` are regenerated by `fsgg-sdd analyze`/`tasks` from this plan's PD/PC/VO/PM entries, so the decisions above stay the single source a reviewer reads.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- `Directory.Build.props`, which carries `FsggCoherentSetVersion` and is where PC-002's bump lands, is declared by live claim `.github#2512` (worker `rook-94e0`) — the very item whose PR produced this defect. `set-paths` refused it with OVERLAP and it is deliberately NOT in this item's declaration. The bump is sequenced after that claim releases; until then no shared path is edited.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2527-post-acceptance-head-move`.
