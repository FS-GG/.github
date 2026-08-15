---
schemaVersion: 1
workId: 2662-critic-succession-ledger
title: Critic Succession Ledger
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2662-critic-succession-ledger/spec.md
sourceClarifications: work/2662-critic-succession-ledger/clarifications.md
sourceChecklist: work/2662-critic-succession-ledger/checklist.md
publicOrToolFacingImpact: true
---

# Critic Succession Ledger Plan

Prose status: planned

## Source Snapshot
- spec: work/2662-critic-succession-ledger/spec.md sha256:67008f5c0e5ca2a0531698a240ea2712f3c423411cc0ac396d913ffd6956c78f schemaVersion:1
- clarifications: work/2662-critic-succession-ledger/clarifications.md sha256:79b172ac8a0307c8daa5247c64bdce2816c1cf0c9babd4a6148912a66b00ce1c schemaVersion:1
- checklist: work/2662-critic-succession-ledger/checklist.md sha256:4d9efe784c99a5959fbef49da2a4cc32683440098eb6c2ed2c02dee4768e25fe schemaVersion:1

## Plan Scope
- Work item 2662-critic-succession-ledger is planned from the current specification, clarification, and checklist facts.
- Requirement count: 11.
- Clarification decision count: 7.
- Checklist result count: 12.

## Plan Decisions

The change is **one additive, optional field and one widened admission**. Nothing already reached is
re-ordered: an ordinary record takes exactly the path it takes today, byte for byte, and the new branch
is consulted only on a record that carries a grant. That is why FR-004 and FR-007 hold by construction
rather than by care.

- PD-001 [AC-001] [FR-001] complete: Add `StructuredDecision.SuccessionGrant = { OriginalCritic; GrantedBy; GrantUrl }` and `ReviewRecord.Succession: SuccessionGrant option`. In `validateReviewLedger`, replace the unconditional continuity conjunct with a two-armed one: with no grant the existing test and the existing message are unchanged; with a grant, admit the critic change and rebind the running `generationCritic` to the record's own `critic`. The rebinding is the whole mechanism — every later record in the generation, including the host's `acceptance`, then binds the successor through the same unchanged conjunct.
- PD-002 [AC-002] [FR-002] complete: Key the admission on `Kind` being `Confirmation`, `Escalation` or `RepairPhase` — the three kinds a live generation can append while the continuity rule is in force. A `Confirmation`-only exemption was the tempting narrow fix and is wrong: the conjunct is keyed on `Kind <> Initial`, so a confirmation-only exemption leaves a successor able to pass a chain but not to escalate it into the repair phase, which was measured on a live chain rather than inferred.
- PD-003 [AC-003] [FR-003] complete: Encode the grant as a nested `succession` object with camel-cased keys `originalCritic`, `grantedBy`, `grantUrl`, decoded through a reader that names `succession.<field>` in its refusal so a malformed grant fails closed at the wire and says which field. `Driver.encodeStructuredReview` projects the option through an anonymous record so the JSON keys are the wire's, not F#'s, and emits an explicit `null` when absent — the same spelling the encoder already uses for `previousDigest`, `initialReview` and `precedingReview`.
- PD-004 [AC-004] [FR-004] complete: Leave the no-grant arm textually identical, including the message `every record in one review generation must bind the same critic`. `tests/FS.GG.Coord.Core.Tests/StructuredDecisionTests.fs`'s `{ confirmation with Critic = "different-critic" }` case must pass UNMODIFIED; if it needs an edit, the design is wrong and the edit is the finding.
- PD-005 [AC-005] [FR-005] complete: Refuse a grant on the first failing conjunct with its own message: outgoing critic not the generation critic in force; blank `grantedBy`; blank `grantUrl`; or a generic route identity in the outgoing, successor or granting slot. Genericness is `isGenericCriticIdentity`, promoted from `Driver.fs`'s private copy to an exported `StructuredDecision.isGenericCriticIdentity` — the module that owns the record owns the predicate about its `critic` field — with `Driver.fs` calling the export instead of keeping its own. `Review.fs` keeps its copy and its code is untouched, because that copy's exact source lines are pinned as gate-inversion anchors by `tests/review-critic-succession-wire/run.sh` and moving them would silently disarm five existing legs; only its doc comment's citation of the now-deleted `Driver.fs` copy is corrected.
- PD-006 [AC-006] [FR-006] complete: Refuse a grant carried by an `initial` or `acceptance` record, and refuse one carried by a record whose `critic` already equals the generation critic. Succession is an exception to a rule; a record that does not trip that rule has no exception to claim, and a decorative grant would assert a provenance no reader could distinguish from a real one.
- PD-007 [AC-007] [FR-007] complete: In `reviewDigest`, append the three framed grant fields to the END of the existing eighteen-field list, and only when a grant is present. `digest` joins with `|`, so an absent grant appends nothing and the joined string — hence the digest — is byte-identical for every record already written. The already-posted records on PRs #2650 and #2655 are the live regression corpus for this.
- PD-008 [AC-008] [FR-008] complete: The same digest rule is what makes a pre-field engine fail CLOSED: it ignores the unknown `succession` key, recomputes over eighteen fields, and reports `digest does not match its structured inputs`. This is asserted directly rather than reasoned about — a leg recomputes the eighteen-field digest by hand over a succession record and requires the ledger to refuse it.
- PD-009 [AC-009] [FR-009] complete: Publish the generation critic as the identity IN FORCE. `Driver.reviewPhaseFacts` reads it from the last structured record rather than from the generation's opening `initial`, and `Driver.parseStructuredComments` sets `ReviewChain.CriticIdentity` from the live generation's last record. For every ledger without a grant the two definitions are the same string — the unwidened conjunct forces it — so this is a correction for the case that is newly reachable, not a change to any case that already was, and a leg pins that equivalence rather than asserting it. The existing `isGenericCriticIdentity first.Critic` check at acceptance keeps reading the opening record and is not touched.
- PD-010 [AC-010] [FR-010] complete: Give `tests/review-critic-succession-wire/run.sh` a fourth section that drives the REAL ledger write — `fsgg-coord-engine review record` against the loopback `tests/coord-engine-e2e/stateful_server.py`, no token and no network, the same vehicle `tests/coord-engine-e2e/writes.sh` already uses for this command. Legs: a granted successor's `confirmation` is POSTED; the same record without a grant is REFUSED with the unchanged message and posts nothing; `escalation` and `repair-phase` are accepted under the same grant; and each refusal conjunct of PD-005/PD-006 is refused. Then extend the gate-inversion section to mutate `StructuredDecision.fs` in the scratch tree — deleting the succession arm — rebuild, and require the accepting ledger leg to RED. Raise the fixture's non-vacuity leg-count floor by the number of legs added, so deleting them is a red gate rather than a quiet one.
- PD-011 [AC-011] [FR-011] complete: State the successor's record shape concretely in both `independent-review.md` mirrors — which are byte-identical and must stay so — and in `docs/coordination/structured-decisions.md`. Say what the record is (`confirmation`/`escalation`/`repair-phase` under the successor's own minted identity, carrying `succession`), what it is not (never a record bearing the despawned critic's id, and never a second `initial`), and state the bound explicitly: the engine checks that `grantUrl` is present, never that it resolves, and a grant is bound to one exact head, so a moved head needs a new grant.
- PD-012 [DEC-008] acceptedDeferral: A durable, machine-readable marker for the GRANT itself stays deferred. Recording the grant's URL in the review record is the prerequisite for any such marker and is what this work delivers; building the marker would be a second schema with its own writer and refusals, widening a repair three parked chains are waiting on.
- PD-013 [CR-012] acceptedDeferral: The checklist-stage mirror of DEC-008 carries the same disposition as PD-012 and needs no separate task: one deferral observed at two stages, not two.

### Why the grant is IN the record and not a parameter

`Driver.structuredReviewLedger` makes the sole `validateReviewLedger` call and is the sole entry for
`reviewPhaseFacts`, the live/retired generation split, and `parseReviewComments` /
`parseEffectiveReviewComments`. Those are the host's acceptance-time and `landable`-time readers, and
they see only the comment ledger — no snapshot, no out-of-band fact. A succession passed as a parameter
would let the record be written and then refused later by the very path that has to accept it. So the
legibility FR-003 asks for is not decoration on top of FR-001; it is what makes FR-001 reachable.

### Why this is not a schema version bump

The field is optional, absent on every ordinary record, and digest-conditional. A post-change reader
parses a pre-change record unchanged; a pre-change reader parses a post-change ordinary record
unchanged, and fails closed on a succession record by digest mismatch. There is no record that both
readers interpret differently and accept. The engine-freshness guard that already refuses board writes
from a stale engine is the mechanism that keeps the two apart, so `fsgg.coord.review-decision/v2` stays
`v2` and `PolicyVersion` stays `structured-decisions/1`.

## Contract Impact
- PC-001 [PD-001] command report: `fsgg-sdd plan`, `work/2662-critic-succession-ledger/plan.md`, and command-report JSON are tool-facing and compatibility-preserving.
- PC-002 [PD-003] review record envelope: `fsgg.coord.review-decision/v2` gains an optional `succession` object (`originalCritic`, `grantedBy`, `grantUrl`). Additive; no field is removed or retyped, no schema identifier moves, and no already-written record changes meaning or digest.
- PC-003 [PD-001] public signature: `StructuredDecision.fsi` gains the `SuccessionGrant` type, the `Succession` field on `ReviewRecord`, and the `isGenericCriticIdentity` predicate. This is a declared public-surface change under constitution III; `Driver.fsi` and `Review.fsi` are unchanged.
- PC-004 [PD-011] agent-skill contract: `.claude/skills/pnext-item/references/independent-review.md` is kit-published skill source, so the change carries a `FS.GG.Kit` version bump and a publish/verify obligation before merge.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: `dotnet test tests/FS.GG.Coord.Core.Tests` and `tests/FS.GG.Coord.Cli.Tests` green at the candidate head, plus `tests/review-critic-succession-wire/run.sh` and `tests/coord-engine-e2e/writes.sh` against the Release engine built from that head.
- VO-002 [PD-007] [PC-002] semanticTest: Digest stability measured, not asserted — recompute `reviewDigest` under the new code over the structured records already posted to PRs #2650 and #2655 and require the recorded `digest` to match byte for byte, and require the same for every record the pre-change fixtures construct.
- VO-003 [PD-010] [PC-002] semanticTest: Gate inversion for the one gate this change ADDS. Delete the succession arm from `validateReviewLedger` in a scratch tree, rebuild, and require the accepting ledger leg to red while every refusal leg stays green. A surviving inversion is a material finding by definition (`.github#2551`); an anchor that no longer matches grades NOT MEASURED and fails.
- VO-004 [PD-004] [PC-002] semanticTest: Regression floor for continuity. `StructuredDecisionTests.fs`'s existing differing-critic case passes UNMODIFIED, and a leg asserts the refusal message text is the same string it is today.
- VO-005 [PD-009] [PC-002] semanticTest: Equivalence floor for FR-009. Over every grant-free ledger the fixtures construct, the last-record definition of the generation critic equals the opening-record definition, so the correction changes no existing answer.
- VO-006 [PD-010] [PC-002] semanticTest: Non-vacuity. The fixture's leg-count floor rises by exactly the number of legs added, so a fixture that silently lost the new legs fails instead of printing a smaller green.
- VO-007 [PD-011] [PC-004] semanticTest: The two `independent-review.md` mirrors are byte-identical after the edit (`diff` clean), and the changed paths reach the workflows that run these suites — confirmed on the live pull request rather than inferred from the `paths:` filters.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-002] diagnoseOnly: There is no data migration and no rewrite of any posted record. The envelope change is forward-compatible in both directions for ordinary records and deliberately fail-closed in one direction for succession records, which is the property PD-008 measures. Records written before this change keep their exact digests and are never re-recorded.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2662-critic-succession-ledger/work-model.json` regenerates from these plan sources. No registry, projection, or emitted-contract version moves: the `fsgg-protocol:review-policy` and `fsgg-protocol:lifecycle-policy` generated blocks in the `independent-review.md` mirrors are projections of `Protocol.fs`, which this change does not touch, so the prose added by PD-011 lives outside those markers.

## Accepted Deferrals
- DEC-008 acceptedDeferral: The grant's own durable on-PR marker is deferred with the grant URL recorded in its place, visible to tasks and evidence as a stated boundary rather than an oversight.
- CR-012 acceptedDeferral: The checklist-stage mirror of DEC-008; same disposition, discharged once by PD-012, no separate obligation.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2662-critic-succession-ledger`.
