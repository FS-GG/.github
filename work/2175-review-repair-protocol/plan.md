---
schemaVersion: 1
workId: 2175-review-repair-protocol
title: Review Repair Protocol
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2175-review-repair-protocol/spec.md
sourceClarifications: work/2175-review-repair-protocol/clarifications.md
sourceChecklist: work/2175-review-repair-protocol/checklist.md
publicOrToolFacingImpact: true
---

# Review Repair Protocol Plan

Prose status: planned

## Source Snapshot
- spec: work/2175-review-repair-protocol/spec.md sha256:b1ff10e45210062317a87c40b411dc0a0a53ef97ee57264f01914004e67e8c39 schemaVersion:1
- clarifications: work/2175-review-repair-protocol/clarifications.md sha256:f59e75c84af16a22719ac9675aff745e027c2a97203db1cc5d410465eeb502b9 schemaVersion:1
- checklist: work/2175-review-repair-protocol/checklist.md sha256:d3b82585b89f7c4b906df90cbdd7d9650ccbd9ba0633c8e9c50e3d1575c62a01 schemaVersion:1

## Plan Scope
- Work item 2175-review-repair-protocol is planned from the current specification, clarification, and checklist facts.
- Requirement count: 13.
- Clarification decision count: 2.
- Checklist result count: 13.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: New `FS.GG.Coord.Core.Review` module (`Review.fs`/`Review.fsi`) defines a closed `State` DU — `AwaitingInitialReview`, `ChangesRequiringRepair`, `AwaitingImplementerRepair`, `AwaitingSameCriticConfirmation`, `PassedAwaitingChecks`, `AwaitingHostAcceptance`, `OrdinaryExhaustion`, `RepairPhaseSetup`, `RepairPhaseActive`, `Accepted`, `TerminalHumanPark`, plus `MalformedEvidence`/`GuardViolation` for fail-closed cases DEC-001 requires — added alongside `Driver`, not inside it, so `Driver`'s existing marker-block parser and its 20+ existing tests are untouched.
- PD-002 [AC-002] [FR-002] complete: `Review.inspect` takes a `Binding` (item/PR/head/claim identity facts) and `Facts` (comments, checks state, head-sha, repair-phase provenance, `repairRouteAvailable`) and returns `Result<Verdict, string list>` where `Verdict = { State; NextAction; FreshnessToken; ActionKey }`; every branch is total (no wildcard defaulting to an absent-review state) and unreadable facts return `Error` carrying named reasons, never a silently permissive state.
- PD-003 [AC-003] [FR-003] complete: Closed `NextAction` DU — `DispatchCritic`, `ResumeImplementer`, `ResumeSameCritic`, `AwaitChecks`, `RequestHostAcceptance`, `EnterRepairPhase`, `Accept`, `Park` — one constructor per named action in the issue; `inspect` selects exactly one per call.
- PD-004 [AC-004] [FR-004] complete: `Binding` carries `ItemRef`, `Pr`, `HeadSha`, `ClaimGeneration`, `ImplementerIdentity`, `Phase`, `Round`; `FreshnessToken`/`ActionKey` are SHA-256 digests over the full binding plus state/action (mirroring `Delivery.freshnessToken`/`Delivery.next`'s existing digest pattern), so a changed head SHA changes the token and invalidates any prior transition.
- PD-005 [AC-005] [FR-005] complete: `inspect` first checks `binding.ImplementerIdentity = criticIdentity` (from `Driver.reviewPhaseFacts`, PD-011) and fails closed with `GuardViolation` before any state classification; same-critic continuity for automated confirmation rounds is already enforced by the reused `Driver.parseReviewCommentsWithFacts` (PD-011) and is not re-implemented.
- PD-006 [AC-006] [FR-006] complete: At `OrdinaryExhaustion` (confirmation round count at `Protocol.reviewPolicy.MaxAutomatedRepairRounds`, no acceptance), `inspect` returns `RepairPhaseSetup`/`EnterRepairPhase` carrying a `RepairPhaseReceipt` (exhausted PR id, escalation-marker comment id, new claim generation, branch/PR, implementer, fresh critic, candidate head) only when `facts.RepairPhaseGranted` is `None` and `facts.RepairRouteAvailable` is `true`; a `RepairPhaseGranted` already present is reused idempotently (PD-009) rather than minting a second phase.
- PD-007 [AC-007] [FR-007] complete: `RepairRouteAvailable = false`, or the repair-phase round ceiling (`Protocol.reviewPolicy.RepairPhaseMaxRounds`) reached without acceptance, returns `TerminalHumanPark reason` with the same full binding provenance; `Delivery`/callers must treat this identically to any other non-`Accepted` state (never inferred as passing).
- PD-008 [AC-008] [FR-008] complete: `Review.Facts` embeds the raw `Result<Driver.ReviewChain, string list>` from the reused parser, and every `MalformedEvidence`/`ChangesRequiringRepair`/`OrdinaryExhaustion`/repair-phase state carries the underlying string list rather than a boolean or `None`; DEC-001 fixes the one case (unreadable `reviewed-head`) that would otherwise need to guess.
- PD-009 [AC-009] [FR-009] complete: `FreshnessToken`/`ActionKey` are pure digests of the inspected facts (same technique as `Delivery.advance`); a new `Review.advance freshnessToken actionKey binding facts` re-inspects and only returns `Next` when both tokens still match current facts, so a stale replay after restart cannot re-dispatch a critic or re-mint a repair phase — it re-converges on the same verdict instead.
- PD-010 [AC-010] [FR-010] complete: `Review.AcceptedReceipt` carries exactly the fields `Driver.ReviewChain` already carries (`HeadSha`, `CriticIdentity`, `Rounds`, `RepairPhase`, `ChecksGreen`, `HostAccepted`, `RuntimeRouteEvidence`, `DiffAuditRequired`, `DiffAuditHead`); `Delivery.fsi`/`Delivery.fs` gain one additive, non-breaking function `Delivery.fromReviewAcceptance : Review.AcceptedReceipt -> Snapshot -> Snapshot` that plugs the receipt into `Snapshot.Review`/`ReviewProblem`; `Delivery.Stage`/`Delivery.Action`/`Delivery.inspect` are unchanged, so no existing `DeliveryTests.fs` case changes meaning.
- PD-011 [AC-011] [FR-011] complete: `Driver.fsi`/`Driver.fs` gain one additive function, `reviewPhaseFacts : ReviewComment list -> ReviewPhaseFacts`, built from the same `ordered`/`initial`/`confirmations`/`escalations`/`repairPhases`/`acceptances` locals `parseReviewCommentsCore` already computes (no new marker-block/quoting scan); `parseReviewComments`/`parseReviewCommentsWithAudit`/`parseReviewCommentsWithFacts` keep their existing signatures unchanged. `Review.fs` calls only these reused Driver entry points and `Protocol.reviewPolicy`'s existing round-ceiling constants; it defines no marker text and no hand-maintained ceiling of its own.
- PD-012 [AC-012] [FR-012] [GV-002] complete: New CLI `ReviewApplication.fs`/`.fsi` mirrors `DeliveryApplication`'s pure snapshot-JSON contract (`fsgg.coord.review/1`) so the #2135 event projection and any script can inspect the typed protocol without a live GitHub call; `Options.fs`/`Program.fs` add a `review` command routed to `ReviewApplication.run` on `--snapshot`/stdin and to a new, additive `Client.fs` live handler otherwise (reusing the same `Reads.*` calls the existing `delivery` live path already makes — no change to the guarded-landing code path itself); `pnext-item`, `drive-board`, and `work-board` skill guidance (`.agents/skills` and `.claude/skills` copies) gain a short reference to `fsgg-coord review` alongside the existing qualitative review guidance, which is not removed.
- PD-013 [AC-013] [FR-013] complete: `tests/FS.GG.Coord.Core.Tests/ReviewTests.fs` (new) covers the full AC-013 matrix; `DriverTests.fs` gains cases for `reviewPhaseFacts`; `DeliveryTests.fs` gains cases for `fromReviewAcceptance`; `tests/FS.GG.Coord.Cli.Tests/ReviewApplicationTests.fs` (new) covers the CLI JSON contract; gate-inversion evidence is captured for the critic-independence, one-fresh-repair-phase, and freshness-token guards per pnext-item's gate-inversion requirement.

## Contract Impact
- PC-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PD-006] [PD-007] [PD-008] [PD-009] internal: `FS.GG.Coord.Core.Review` is a new pure module; additive to `FS.GG.Coord.Core`'s public surface, no existing type or function signature changes.
- PC-002 [PD-010] [PD-011] additive: `Driver.reviewPhaseFacts` and `Delivery.fromReviewAcceptance` are additive functions on existing modules; existing `Driver`/`Delivery` public surface (types, existing function signatures) is unchanged, so `docs/api-surface` `.fsi` baselines for those modules only grow.
- PC-003 [PD-012] command report: `fsgg-coord review` (`ReviewApplication`) is a new tool-facing CLI command and JSON schema `fsgg.coord.review/1`, compatibility-preserving with the existing `delivery` command contract it parallels.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] semanticTest: `ReviewTests.fs` unit-tests every `State`/`NextAction` branch and the fail-closed no-verdict paths.
- VO-002 [PD-005] [PD-006] [PD-007] semanticTest: `ReviewTests.fs` gate-inversion cases for the critic-independence guard, the one-fresh-repair-phase enforcement, and the terminal-park routing, each inverted and observed red per pnext-item's gate-inversion evidence requirement.
- VO-003 [PD-009] semanticTest: `ReviewTests.fs` covers restart/duplicate-advance idempotency (`Review.advance` called twice against the same and against changed facts).
- VO-004 [PD-010] [PD-011] semanticTest: `DriverTests.fs`/`DeliveryTests.fs` additive cases for `reviewPhaseFacts` and `fromReviewAcceptance`; full existing `DriverTests.fs`/`DeliveryTests.fs` suites stay green unmodified.
- VO-005 [PD-012] semanticTest: `ReviewApplicationTests.fs` and a CLI smoke run (`dotnet run -- review --snapshot ...`) cover the JSON contract; `tests/FS.GG.Coord.GitHub.Tests` covers any new `Reads` usage the live handler needs.
- VO-006 [PD-013] semanticTest: `dotnet test` across `FS.GG.Coord.Core.Tests`, `FS.GG.Coord.Cli.Tests`, and `FS.GG.Coord.GitHub.Tests` is green before task closure.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-002] diagnoseOnly: `Driver.reviewPhaseFacts` and `Delivery.fromReviewAcceptance` are purely additive functions with no removed or renamed existing public member, so no caller migration is required; the `docs/api-surface` `.fsi` baselines for `FS.GG.Coord.Core` are refreshed (`fsgg-sdd surface`) rather than treated as a breaking-change migration.

## Generated View Impact
- GV-001 [PD-001] [PD-013] workModel: `readiness/2175-review-repair-protocol/work-model.json` is regenerated by `fsgg-sdd refresh`/each authoring command from the current `spec.md`/`clarifications.md`/`checklist.md`/`plan.md`/`tasks.yml` sources after every edit in this plan, so it is never hand-edited and never goes stale relative to those sources.
- GV-002 [PD-012] apiSurface: `fsgg-sdd surface --check` (run 2026-08-10) reports `docs/api-surface` empty for all 55 `.fsi` files in this repository — a pre-existing, repo-wide absence this item did not introduce and is out of scope to establish. Deferred rather than refreshed: no baseline exists for this item's new/changed `.fsi` files to drift from, so there is nothing to update, and bootstrapping the baseline for the other 51 unrelated `.fsi` files is a separate, repo-wide change.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2175-review-repair-protocol`.
