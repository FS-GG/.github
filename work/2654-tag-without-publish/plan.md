---
schemaVersion: 1
workId: 2654-tag-without-publish
title: Tag Without Publish
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2654-tag-without-publish/spec.md
sourceClarifications: work/2654-tag-without-publish/clarifications.md
sourceChecklist: work/2654-tag-without-publish/checklist.md
publicOrToolFacingImpact: true
---

# Tag Without Publish Plan

Prose status: planned

## Source Snapshot
- spec: work/2654-tag-without-publish/spec.md sha256:a7f12d81542c23cc638a549371a97c5035ef5829f22a1541f3909e2a99c8e728 schemaVersion:1
- clarifications: work/2654-tag-without-publish/clarifications.md sha256:ff757d94dc5ca5abd6a2490a185d91727eb0c448670c2c6ae61a035a1c748e76 schemaVersion:1
- checklist: work/2654-tag-without-publish/checklist.md sha256:b049ffb76002a19ab8051f70112e191e6b6e5f2387f3884fe960728334c5a912 schemaVersion:1

## Plan Scope
- Work item 2654-tag-without-publish is planned from the current specification, clarification, and checklist facts.
- Requirement count: 7.
- Clarification decision count: 3.
- Checklist result count: 8.

## Plan Decisions
- PD-001 [AC-001] [AC-002] [AC-010] [FR-001] complete: Order the tag write behind preparation in the JOB GRAPH of kit-auto-publish.yml, not inside decide(). Three jobs — decide, prepare, tag — where prepare calls release-saga-prepare.yml and tag declares needs on both. A needs job that is skipped or that fails skips its dependents, so a failed preparation cannot be followed by a tag push; identical if conditions on the two jobs close the other half, where an action could select the tag write without selecting preparation. decide() is deliberately untouched: a preparedRelease fact would be permanently false on the very line it was added for, because release-saga-ci.sh init asserts the manifest sourceSha equals the tag's commit and that commit does not exist until the merge does.
- PD-002 [AC-003] [FR-002] complete: Add on.workflow_call to release-saga-prepare.yml with one optional string input, source-sha, defaulting to empty. Resolve it once at workflow level into FSGG_SOURCE_SHA and use that for the checkout ref, the manifest binding, the draft release target and the printed operator tag commands. The operator workflow_dispatch path is unchanged and falls back to github.sha. Raise kit-auto-publish's workflow-level permissions to contents write plus packages read, because a called workflow's token is capped by its caller.
- PD-003 [AC-004] [FR-003] complete: Replace the header sentence claiming publishing starts only when an operator pushes all three tags with both actors named, and record why that sentence was already false when it was written: kit-auto-publish has pushed the trio unattended since .github#2495.
- PD-004 [AC-005] [AC-006] [FR-004] complete: Rewrite the patch-line row so the automated act is prepare-then-tag rather than tag alone, and add the third correction in the .github#2533 and .github#2571 series — a tag is not a publication. Add a discharge rule making release-verification a FEED read, since kit-auto-publish's own run was green while nothing published. Copy the .claude file byte-for-byte to the .agents mirror and leave the generated release-columns region untouched.
- PD-005 [AC-007] [FR-005] complete: Add a hermetic topology checker to tests/kit-auto-publish/run.sh that reads the two real workflow files and scores seven clauses: a tagging job exists, a preparing job exists, every tagging job's transitive needs closure reaches a preparing job, their if conditions are identical, neither uses always or cancelled or failure, the callee admits workflow_call and declares every input the caller passes, and the callee binds that input rather than reaching for GITHUB_SHA. Jobs are found by what their steps DO and what they CALL, never by a hardcoded id, so a rename performed while decoupling still reds.
- PD-006 [AC-008] [FR-006] complete: Ship one committed mutation per clause — decoupled needs, drifted condition, always, deleted preparing job, dispatch-only triggers, undeclared input, ignored input — each asserted red AND red for its own reason through a required needle in the finding text, so a mutation that reds for a neighbouring clause fails the leg rather than passing it. Write each callee mutant under its original basename, because the checker matches a preparing job by that basename and a renamed mutant would red on a different clause.
- PD-007 [AC-009] [FR-007] complete: Record the 0.58.1 disposition as RECOVERY at a415652f rather than abandonment. Zero packages are published, so no immutable package version is wrong and the version is still completable from the commit its three tags already name; preparing at any other commit fails closed on the sibling-tag precondition instead of publishing mismatched bytes. Name the exact route, the reason a worker may not execute it, and the rows that already carry the remainder.
- PD-008 [DEC-004] acceptedDeferral: Executing the 0.58.1 recovery stays with an operator. release-saga-prepare's dispatch path is for humans by design, and the three release workflows must then be re-run against an exact source_sha; both are irreversible-adjacent acts against public feeds. This work records the route and surfaces it rather than performing it.
- PD-009 [CR-008] acceptedDeferral: The coherent-set version bump that would let these kit bytes ship is not taken here. Directory.Build.props is held by a live claim on .github#2551, widen refused with OVERLAP, and the debt is already boarded on .github#2648 and .github#2661.

## Contract Impact
- PC-001 [PD-001] command report: release-saga-prepare.yml's workflow_call input set becomes a caller-facing contract, and its job id prepare becomes a published reusable-workflow name that check-reusable-job-ids.py holds stable from this merge onward. merge-and-release.md is coordination-kit packed content read by every worker declaring a post-merge obligation. Both changes are compatibility-preserving: no existing trigger, input, job id or obligation token is removed.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: tests/kit-auto-publish/run.sh must pass against the shipped workflow files and must fail against each of the seven committed mutations, with the finding naming the clause under test. Supporting hermetic gates that must stay green: workflow-timeouts, job-uniqueness, reusable-job-ids, paths-coherence, sparse-checkout-closure, prose-citations, graphql-monopoly, skill-union-assert, repos-registry, release-saga, and kit-published-coherence.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: No migration. The tag step's script crosses the job boundary unchanged apart from comments, and its existing TOCTOU guard is retained and documented as MORE load-bearing, because preparation widens the window between observation and write from seconds to minutes.

## Generated View Impact
- GV-001 [PD-001] workModel: readiness/2654-tag-without-publish/work-model.json is regenerated from these plan sources; a stale one reports staleGeneratedView rather than being read as current.

## Accepted Deferrals
- DEC-004 acceptedDeferral: The 0.58.1 recovery execution is operator-gated, recorded here rather than performed.
- CR-008 acceptedDeferral: The coherent-set version bump is blocked by a live overlapping claim and is boarded elsewhere.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- The residual state — tags pushed, preparation done, a release workflow still failing — gets no new machinery: decide() already reads it as tag-exists-without-both-feed-publication and has escalated it for 60 consecutive runs. It was detected all along; it was only ever unrepairable.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2654-tag-without-publish`.
