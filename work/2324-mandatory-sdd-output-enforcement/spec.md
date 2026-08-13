---
schemaVersion: 1
workId: 2324-mandatory-sdd-output-enforcement
title: Mandatory Sdd Output Enforcement
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Mandatory Sdd Output Enforcement Specification

Prose status: specified

## User Value
A worker claiming an `sdd-required` item can produce the `work/<workId>/` and `readiness/<workId>/`
package the route itself obliges it to produce without `verify-paths` reporting that mandatory output
as touch-set `DRIFT`. Today every one of those items pays a mid-flight `widen` — and a `widen` runs a
board-wide collision scan and PATCHes the issue body — solely to declare two directory names derived
from the item's own delivery-route receipt, which nothing but that item's claim holder can ever author.

## Scope
- SB-001: A pure derivation, in `src/FS.GG.Coord.Core/DeliveryRoute.fs`/`.fsi`, of the package
  directories the `sdd-required` route is guaranteed to produce, from the receipt's own
  `sddWorkId`/`specHome` facts.
- SB-002: `verify-paths` (`src/FS.GG.Coord.Cli/Client.fs`) reports files under the implemented item's
  OWN current `sdd-required` package directories in a distinct expected bucket, alongside ADR-0044's
  existing `regenerated (expected)` bucket, and excludes them from the `undeclared` set that decides
  the verdict.
- SB-003: Regression coverage for the derivation (`tests/FS.GG.Coord.Core.Tests`) and for the
  `verify-paths` command boundary (`tests/FS.GG.Coord.Cli.Tests`), each shipped with recorded
  gate-inversion evidence.

## Non-Goals
- SB-004: Do NOT make declaring `work/<workId>/` or `readiness/<workId>/` illegal. ADR-0044 refuses a
  generated artifact at `widen`/`set-paths` (`Client.fs` `TouchSet.generatedTokens`) because nobody
  authors it; an SDD package IS authored, by the item's claim holder, so declaring it stays a legal
  choice. The four items that already declare theirs (`.github#2306`, `#2305`, `#2366`, `#2324`) keep
  working byte-unchanged, and an item that wants the reservation may still take it.
- SB-005: Do NOT auto-declare the paths at `delivery-route record`, `claim`, or `take` time (remedy arm
  (a) in the filed body). See the Rejected Alternative below; this is a decided non-goal, not an
  oversight.
- SB-006: Do NOT change `widen`, `set-paths`, `TouchSet`'s grammar or overlap rule, the scheduler
  (`Schedulability`/`Lanes`), or the `sddEvidenceErrors` advisory readiness report.
- SB-007: Do NOT retro-edit the three still-exposed rows' bodies (`.github#2249`, `#2343`) — a body
  edit is a host action, and after this change they no longer need one.

## Rejected Alternative (arm (a): auto-declare at record/claim time)
Recorded here because the delivery-route receipt states that choosing between the filed body's two
remedy arms is what this spec phase decides.

- RA-001: `widen`/`set-paths` are the only code paths that write a `Paths:` declaration, and both are
  gated on `Writes.verifyHeld` — the caller must HOLD the item's claim (`Client.fs`, `#706`). A
  coordinator running `delivery-route record` holds no claim, so arm (a) at record time needs a new,
  claim-less body-write path: a strictly larger and more dangerous surface than a read-side exemption.
- RA-002: Every declaration write is gated on `activeCollisions`, a live board-wide scan
  (`Client.fs`, `#523`/`#353`). Arm (a) therefore spends one board scan — the fleet's scarcest
  resource — per `sdd-required` item, to reserve two directories whose names are derived from the item
  id and which only that item's claim holder can ever author. Arm (b) spends none.
- RA-003: Arm (a) at claim time PATCHes the issue body inside `take`/`claim` — the hottest, most
  contended write in the protocol, and the one whose body rewrite the filed evidence already blames for
  five staled route receipts.
- RA-004: The reservation arm (a) would buy is empty in the only case that matters: an item's package
  directory is derived from its own `sddWorkId`, and the item's claim lock already excludes every other
  worker from authoring it. The residual case — another item declaring an over-broad ancestor such as
  `work/**` — is a declaration the lane-steward protocol already treats as a defect, and SB-004 leaves
  the explicit declaration available to any item that wants it.

## User Stories
- US-001 (P1): As a worker claiming an `sdd-required` item, I want the SDD package the route obliges me
  to author to be reported as expected route output rather than as touch-set drift, so I do not have to
  spend a `widen` (and the board scan and body PATCH it carries) on paths derived from my own item.
- US-002 (P2): As a reviewer reading a `verify-paths` verdict, I want the SDD package files named in
  their own labelled bucket rather than silently vanishing from the output, so that "expected" is a
  reported fact I can check rather than an invisible subtraction.
- US-003 (P2): As an operator, I want an unreadable, stale, or non-`sdd-required` route receipt to
  subtract exactly nothing and say so, so that "I could not ask what the route obliges" never reads as
  "the route obliges nothing".

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a PR that implements an item whose current delivery-route receipt is
  `sdd-required` with `sddWorkId: X` and `specHome: work/X/spec.md`, and whose changed files include
  `work/X/spec.md` and `readiness/X/analysis.json` while the item's declared `Paths:` names neither,
  when `verify-paths` runs, then the verdict is `FSGG-PATHS OK` and both files are reported under an
  `sdd package (expected)` heading rather than under `undeclared (review)`.
- AC-002 [US-001] [FR-002]: Given the same PR also changes a file that is neither declared nor part of
  that package, when `verify-paths` runs, then the verdict is `FSGG-PATHS DRIFT`, `undeclared (review)`
  names exactly that one file, and the package files are still reported under `sdd package (expected)`.
- AC-003 [US-003] [FR-003]: Given a PR whose implemented item's current receipt route is `lightweight`,
  when `verify-paths` runs over changed files under `work/<n>/`, then nothing is subtracted and those
  files are reported as `undeclared (review)` exactly as they are today.
- AC-004 [US-003] [FR-004]: Given the delivery-route receipt read fails or yields no current receipt,
  when `verify-paths` runs on a PR that has drift, then nothing is subtracted, the drift is reported
  exactly as it is today, and stderr names that nothing was subtracted for the `sdd-required` route's
  mandatory output.
- AC-005 [US-002] [FR-005]: Given a receipt naming `sddWorkId: X`, when a changed file lies under a
  DIFFERENT item's package (`work/Y/...` or `readiness/Y/...`, `Y <> X`), then that file is `undeclared
  (review)` — the exemption is bound to the implemented item's own receipt, never to `work/`/`readiness/`
  as roots.
- AC-006 [US-003] [FR-006]: Given a receipt whose `specHome` has no directory component, is absolute,
  contains a `..` segment or a backslash, or whose `sddWorkId` is blank or path-shaped, when the
  mandatory-output derivation runs, then it yields the empty list — no path is ever exempted from a fact
  it could not read cleanly.
- AC-007 [US-002] [FR-007]: Given a PR with NO drift at all, when `verify-paths` runs, then no
  delivery-route receipt read is issued and no `sdd package (expected)` heading is printed — the same
  "ask only when there is drift to subtract from" rule ADR-0044's generated-artifact subtraction already
  follows, for the same reason: a diagnostic about a subtraction that was not needed lands in the sticky
  comment of a green PR and teaches readers the output is noise.

## Functional Requirements
- FR-001: `verify-paths` excludes, from the `undeclared` set that decides its verdict, every changed file covered by a package directory derived from the implemented item's own current `sdd-required` delivery-route receipt, and reports those files under a distinct `sdd package (expected)` heading on both the `OK` and `DRIFT` verdicts. (Stories: US-001; Acceptance: AC-001)
- FR-002: The exemption removes only those files: any other undeclared changed file still produces `FSGG-PATHS DRIFT` and is still named under `undeclared (review)`. (Stories: US-001; Acceptance: AC-002)
- FR-003: A current receipt whose route is not `sdd-required` yields no exemption at all. (Stories: US-003; Acceptance: AC-003)
- FR-004: A receipt read that errors, or a verdict that is not `Current`, subtracts nothing and emits a stderr line naming that nothing was subtracted — the same fail-closed asymmetry `generatedPaths` already states ("I could not ask what is generated" and "nothing is generated" are opposite facts). (Stories: US-003; Acceptance: AC-004)
- FR-005: The derivation is bound to the receipt's OWN `sddWorkId`/`specHome`; a file under another work id's package directory is never exempted. (Stories: US-002; Acceptance: AC-005)
- FR-006: The derivation in `DeliveryRoute` fails closed to the empty list on any receipt fact it cannot read as a clean repo-relative package location — missing route/workId/specHome, a blank or path-shaped workId, a `specHome` with no directory component, an absolute path, a `..` segment, or a backslash. (Stories: US-003; Acceptance: AC-006)
- FR-007: The receipt read is issued only when the drift set is non-empty, so a green PR pays neither the network call nor a diagnostic about a subtraction it did not need. (Stories: US-002; Acceptance: AC-007)

## Ambiguities
No material ambiguities recorded. The one genuine decision — which of the filed body's two remedy arms
to take — is decided in `Rejected Alternative` above and carried into `clarifications.md` as DEC-001.

## Public Or Tool-Facing Impact
- `src/FS.GG.Coord.Core/DeliveryRoute.fsi` is published coord-engine surface that receiver repos pin;
  adding a `val` to it is a public-contract addition and is stated in the signature file's own doc.
- `verify-paths` is the tool-facing merge gate `.github/workflows/touch-set-drift.yml` executes on every
  PR, and its `FSGG-PATHS *` verdict vocabulary is scraped by that workflow. The four verdict tokens are
  unchanged by this item; only a new reported bucket is added beneath them.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2324-mandatory-sdd-output-enforcement`.
