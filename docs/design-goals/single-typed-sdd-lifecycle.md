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

Tracking authority: [FS.GG.SDD #927](https://github.com/FS-GG/FS.GG.SDD/issues/927).
