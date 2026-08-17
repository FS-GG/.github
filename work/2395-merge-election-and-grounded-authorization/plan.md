---
schemaVersion: 1
workId: 2395-merge-election-and-grounded-authorization
title: Merge Election And Grounded Authorization
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2395-merge-election-and-grounded-authorization/spec.md
sourceClarifications: work/2395-merge-election-and-grounded-authorization/clarifications.md
sourceChecklist: work/2395-merge-election-and-grounded-authorization/checklist.md
publicOrToolFacingImpact: true
---

# Merge Election And Grounded Authorization Plan

Prose status: planned

## Source Snapshot
- spec: work/2395-merge-election-and-grounded-authorization/spec.md sha256:7c432a2c49e4c92ae0f396379f93128c4f77b068405a87b63fec663dbae73103 schemaVersion:1
- clarifications: work/2395-merge-election-and-grounded-authorization/clarifications.md sha256:f3a2b038f532739510984530bb1096deb0402d6550679d78eb0e0b465b3b04fc schemaVersion:1
- checklist: work/2395-merge-election-and-grounded-authorization/checklist.md sha256:346efa502101a9681214c8f5cf5b62e6b5803c6bb3861622ad325bc39fa72910 schemaVersion:1

## Plan Scope
- Work item 2395-merge-election-and-grounded-authorization is planned from the current specification, clarification, and checklist facts.
- Requirement count: 8.
- Clarification decision count: 4.
- Checklist result count: 9.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `Client.authorizationMarker` gains `opkey` and `grant` parameters and renders all six fields in the gate's own order (`v item gen opkey grant head`); `Client.rebindAuthorization` takes the same two and keeps its strip-then-append rule, so exactly one marker survives however many were there before. Both signatures move in `Client.fsi` with them.
- PD-002 [AC-002] [FR-002] complete: `DeliveryApplication` gains the pure half — the election marker's text and the parse that recognises one — and `Client` gains the IO half, `electionGrounding`, which `ensureAuthorization` calls before it reads the pull-request body. The election is appended through the already-exported `Writes.postIssueComment`, which returns the server-assigned comment id that becomes `grant`. The comment body BEGINS with the marker because the fence anchors its match at byte 0 of the raw comment body and does not trim.
- PD-003 [AC-003] [AC-004] [FR-003] complete: The election records `pr=` in addition to the six fields the fence requires, and `electionGrounding` reuses an election bearing this operation key AND this pull request rather than posting a second. `pr=` is the reuse discriminator and it is load-bearing in both directions: without it a repeated `delivery` call would post a strictly higher-id election and the authorization would name a loser, and with a laxer rule (reuse ANY election for the key) a second executor delivering the same item under one generation would inherit the first executor's grant and both would pass — which is precisely the "at most one merge per item, generation and receiver" guarantee the fence exists to provide. The fence ignores unknown fields, so `pr=` costs the reader nothing.
- PD-004 [AC-005] [FR-004] complete: The lowest-id selection is asked of `Reads.lowestId`. `Reads` has no election record type and this work does not declare `Reads.fs`, so the candidates are projected onto `Reads.Marker` — a projection in which only `Id` is meaningful and every other field is a documented placeholder the call never reads back — and the winning id is mapped back to its election. That obeys the design's "the ordering rule must not be written twice" literally rather than by resemblance; a `List.sortBy`/`List.minBy` in the CLI layer would be the forbidden second copy.
- PD-005 [AC-006] [FR-005] complete: Every failure in the grounding path propagates and the pull-request body PATCH is never reached. A refused `Operation.compose` becomes a `Malformed` naming each refusal; a failed comment read or election post propagates its own `IoError`. There is no four-field fallback, because a marker the wider gate calls ungrounded is the decorative case the design names and a failed read must not masquerade as an answer.
- PD-006 [AC-007] [AC-008] [FR-006] complete: No migration mechanism is added, because the measured facts make one unnecessary — `claim-generation` is the only marker reader among `main`'s required contexts, it requires four fields and accepts supersets, and the six-field reader is observe-only. Existing four-field markers are upgraded in place by `rebindAuthorization` on the next `delivery` call. The one genuine bite is the equality assertion in the receiver-validate fixture, handled by PD-008.
- PD-007 [AC-009] [FR-007] complete: Reachability is demonstrated by executing `scripts/check-claim-fence.py`'s own `classify` over a body composed by the production writer, with the item read stubbed, in four legs plus a control: grounded and passing; no election bearing the key; a competing election exactly one comment id lower, which is check 4's `min` BOUNDARY rather than an existence test; and a winning election recording a different receiver. The control is that today's four-field marker still stops at check 1 on the same harness, so the four legs are evidence about the field set rather than about a harness that grades everything.
- PD-008 [AC-010] [FR-008] complete: `tests/receiver-validate/run.sh` leg F2 becomes a SUBSET assertion — the receiver gate's required fields must all be fields the producer writes — because the receiver gate documents itself as tolerating additional pairs, so equality over-stated the contract and would red on any forward-compatible producer. A mutation leg is added beside it that drops a required field from the parsed producer field set and asserts F2 is caught, so the corrected assertion still ships with evidence it can fail.
- PD-009 [DEC-005] acceptedDeferral: Accepted deferral DEC-005 remains visible to task generation.
- PD-010 [CR-009] acceptedDeferral: Accepted deferral CR-009 remains visible to task generation.

## Contract Impact
- PC-001 [PD-001] marker wire form: the `fsgg:pr-authorization` marker gains `opkey=` and `grant=`. Its three readers — `scripts/check-claim-generation.py`, `scripts/check-claim-fence.py`, and the receiver-side validation job in `.github/workflows/kit-materialize.yml` — all tolerate additional pairs, so the widening is additive for every one of them.
- PC-002 [PD-002] marker wire form: the `fsgg:merge-election` marker gains its first producer. Its spelling is the one `scripts/check-claim-fence.py` already fixed as a reader; the claim CAS matches `fsgg:claim` and nothing else, so a new prefix decides no lock.
- PC-003 [PD-001] [PD-002] internal library signature: `Client.authorizationMarker`, `Client.rebindAuthorization` and the new `Client.electionGrounding` are surfaced through `Client.fsi`, and `DeliveryApplication.fsi` gains the election marker's text and parse. `FS.GG.Coord.Core` and `FS.GG.Coord.GitHub` are `IsPackable=false` and ship inside the `FS.GG.Coord.Cli` pack.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] semanticTest: `tests/FS.GG.Coord.Cli.Tests` drives `ensureAuthorization` against a scripted transport and asserts the six-field marker, the election POST, its byte-0 anchoring, and the reuse and lowest-id rules — each as a named test whose title states the mutation it would catch.
- VO-002 [PD-005] semanticTest: a scripted transport that refuses the item read, and one that refuses the election POST, each assert that ZERO pull-request PATCH requests were issued.
- VO-003 [PD-007] executedGateRun: `scripts/check-claim-fence.py` is executed over production-composed bodies with the item read stubbed, and the four legs plus the four-field control are recorded with their exact diagnoses.
- VO-004 [PD-008] fixture: `tests/receiver-validate/run.sh` runs green with the six-field producer, and its new mutation leg proves the corrected F2 still catches a producer that drops a required field.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] compatibleSuperset: no cutover and no rebinding campaign. `main`'s required contexts are read live before merge; `claim-generation` requires four fields and accepts supersets, and the six-field reader is not armed. Existing four-field markers keep passing and are upgraded in place by the next `delivery` call.
- PM-002 [PC-002] additive: the election marker has no prior producer, so nothing can be stale against it; its readers already exist and already tolerate its absence by reporting a check 4 finding.

## Generated View Impact
- GV-001 [PD-001] [PD-007] workModel: the derived work model is the only generated view this change touches, and it moves for a bookkeeping reason rather than a behavioural one — this row adds tasks and evidence obligations but no new artifact kind. The engine's own generated surfaces (`registry/driver-skill-manifest.json` and the projected skill guidance) are untouched, because no skill text, agent roster or command contract changes here.

## Accepted Deferrals
- DEC-005 acceptedDeferral: the permanent producer-versus-fence agreement leg belongs to `.github#2719`, which declares the fence script and its corpus; it must be written after this row lands because it reds until the six-field marker exists.
- CR-009 acceptedDeferral: the checklist mirror of DEC-005, carried so task generation and evidence can both see it.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2395-merge-election-and-grounded-authorization`.
