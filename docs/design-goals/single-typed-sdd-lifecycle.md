---
title: One Typed SDD lifecycle
category: Design
categoryindex: 4
index: 35
description: The target single lifecycle and issue-to-model change flow.
---

# One Typed SDD lifecycle

The target is one lifecycle: Quint-backed Typed SDD. Freeform, structured SDD,
and direct Quint describe how much formal machinery a user sees or authors; they
do not select different semantic authorities.

Each workspace has one modular `WorkspaceModel` covering product behavior,
decisions, repository profile, CI obligations, external contracts, and evidence.
Every issue receives a `ChangeProposal` bound to the exact accepted model
fingerprint. Filing or discussing an issue never changes accepted truth. Only an
accepted pull request with a readable semantic diff and required evidence reduces
a proposal into the next model revision.

GitHub remains authoritative for event identities: issue, pull request, commit,
review, workflow run, and merge. Quint owns their declared lifecycle meaning and
relationships.

Existing `none`, `sdd`, `typed-sdd`, and `spec-kit` workspaces require a versioned
compatibility and migration window. Old tokens must remain inspectable and
migratable; they must not be silently aliased or reinterpreted.

## Optional preflight for recurring preparation defects

When missing artifacts, observation bounds or deadline/refusal ordering repeatedly
cause failed attempts, Typed SDD may use the
[V2-PREFLIGHT-01 strategy](../github-substrate-v2-roadmap.md#v2-preflight-01--typed-prerequisite-admission-next-item-2026-10-03).
This is a proposed mechanism, selected from actual defect history and avoidable
cost through the [pipeline preflight guidance](../../.agents/skills/pipeline-preflight/SKILL.md).
Existing checks may suffice; each workspace does not need a new model.

A private-constructor `PreparedAttempt` can bind the exact command, configuration,
transitive artifacts, observed prerequisites and deadlines. Preparation checks
the actual assembled capsule without launching the workload, including imports
and test discovery. Missing, malformed, timed-out or unknown observations cannot
establish readiness. The execution adapter revalidates mutable identities and
time-sensitive facts immediately before their dependent effect, and places
writable output outside immutable inputs.

Small Quint modules can cover artifact preparation and invalidation, admission
and deadlines, and owned process observation and cleanup. Bind their inputs to
the real runner with drift checks, replay relevant traces through the F# reducers,
and retain reachable success cases and causal mutations. Invalid inputs before
admission preserve state; a valid clock advance that expires existing work must
retain retirement, timer, settlement and required cleanup effects even when the
new request refuses. Qualify each actual packaged adapter independently.

Types, bounded sampling and shared tests alone do not establish native acceptance
or installed adoption. Record cold and warm cost, detected defects and remaining
coverage in the existing implementation evidence; claim savings only when
measured. Source delivery, publication, installed adoption and operation remain
distinct. Selecting this mechanism adds no issue, claim, approval or reporting
ceremony and changes no lifecycle default.

Tracking authority: [FS.GG.SDD #927](https://github.com/FS-GG/FS.GG.SDD/issues/927).
