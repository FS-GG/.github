---
schemaVersion: 1
workId: 2409-coherent-set-release
title: Coherent Set Release
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2409-coherent-set-release/spec.md
sourceClarifications: work/2409-coherent-set-release/clarifications.md
sourceChecklist: work/2409-coherent-set-release/checklist.md
publicOrToolFacingImpact: true
---

# Coherent Set Release Plan

Prose status: planned

## Source Snapshot
- spec: work/2409-coherent-set-release/spec.md sha256:3a6026d172a968f8de051d59c5bb221bd957187fc1b7cbc56f77d641a6d38f28 schemaVersion:1
- clarifications: work/2409-coherent-set-release/clarifications.md sha256:ec250b3c79824e44f6f1f6ee277eb73eabc325aca936f67f1340bf4d4948e931 schemaVersion:1
- checklist: work/2409-coherent-set-release/checklist.md sha256:3f0ddc9d2931308942b0e79cfdeb623b4d51f34d5f4e6fe9886afc0874e1655a schemaVersion:1

## Plan Scope
- Work item 2409-coherent-set-release is planned from the current specification, clarification, and checklist facts.
- Requirement count: 3.
- Clarification decision count: 5.
- Checklist result count: 3.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Per DEC-004's final form (after two revisions, each triggered by
  re-reading a source workflow header in full and finding a load-bearing external contract a rename
  would silently break — nuget.org's per-filename Trusted Publishing OIDC binding, then the
  receiver-side `kit-bump-shape` reporter's dependence on the literal `kit/v*` tag namespace) — change
  NEITHER the three filenames NOR their existing tag namespaces NOR their `workflow_dispatch` shape.
  `.github/workflows/release-kit.yml`, `release-drivers.yml` and `release-coord-engine.yml` keep
  `kit/v*` / `drivers/v*` / `coord-engine/v*` exactly as today. Add ONE new precondition to each file's
  existing "Resolve version + publish decision" step, alongside its existing .github#1772 own-tag check
  (both the tag-push arm and the `workflow_dispatch publish=true` arm): `git ls-remote` the OTHER TWO
  packages' tags at the SAME evaluated version and refuse (fail closed, naming the missing sibling tag
  and the remedy) unless both exist and resolve to the SAME commit SHA as the version being packed. A
  maintainer who tags and pushes only `kit/v0.50.0` can no longer complete a publish — the new
  precondition refuses until `drivers/v0.50.0` and `coord-engine/v0.50.0` also exist at that commit.
  Pushing all three refs in one `git push origin kit/v0.50.0 drivers/v0.50.0 coord-engine/v0.50.0` is
  the "one shared trigger" AC1 asks for, expressed as one push event carrying all three existing tag
  namespaces rather than a new one. This changes zero bytes of any contract this worker cannot see or
  verify the other end of (nuget.org OIDC policy, the `kit-bump-shape` reporter, the tag-immutability
  rulesets), while directly closing AC2's stated complaint ("nothing stops a maintainer tagging one
  without the other two").
- PD-002 [AC-001] [FR-002] complete: Cut the real release. Push all three tags — `kit/v0.50.0`,
  `drivers/v0.50.0`, `coord-engine/v0.50.0` (the current `$(FsggCoherentSetVersion)`,
  `Directory.Build.props:85`) — at the merged commit, in one `git push` carrying all three refs; observe
  all three workflow runs publish to both feeds (each now gated on the new sibling-tag precondition);
  flip `registry/dependencies.yml`'s `coord-engine` row
  (`version`/`package-version`, currently `0.23.0` at `registry/dependencies.yml:901-902`) to `0.50.0`
  with the same live-verification discipline that row's existing entries use (feed SHA-256s, `dotnet tool
  restore` proof, not a source-build inference); add analogous registry evidence rows/notes for Kit and
  Drivers only if the registry schema already carries them (per DEC-002, it does not — Kit/Drivers carry
  no `registry/dependencies.yml` contract row today, so this is a doc-level verification, not a new
  registry row). This is an execution action gated on the new consolidated release workflow existing on
  `main` first — a post-merge obligation (see Accepted Deferrals below), not deferred out of scope.
- PD-003 [AC-001] [FR-003] complete: Write the gate re-confirmation (DEC-002, already authored in
  clarifications.md) into the PR body / `docs/registry/compatibility.md`'s migration note, naming each
  of the eight now-relevant gates (the seven DEC-002 evaluated plus `check-coherent-set-version.py`) and
  its keep-and-why, per AC3. Extend the "Coherent-set versioning" section of
  `docs/registry/compatibility.md` (added by .github#2402) with a new dated entry recording: the cut
  version (0.50.0), the consolidated workflow's existence, the publish evidence (feed digests, restore
  proof) once the real release cut executes, and DEC-003's rollout finding (a coordinated fan-out is
  owed regardless of .github#2396's eventual bound) verbatim.

## Contract Impact
- PC-001 [PD-001] command report: the three files' filenames, tag namespaces and `workflow_dispatch`
  inputs are byte-for-byte unchanged — no external contract surface (nuget.org Trusted Publishing OIDC
  policy binding, the receiver-side `kit-bump-shape` reporter's `kit/v<version>` resolution, the
  `kit-release-tags-are-immutable`-class tag rulesets) moves. The only new tool-facing surface is the
  sibling-tag precondition's failure message format (named tag missing + remedy command), additive and
  non-breaking. Grepped for existing consumers of the tag patterns outside the three workflow files
  themselves: none found in `scripts/`, `tests/`, or `docs/` in THIS tree; the `kit-bump-shape` consumer
  is confirmed to exist only by the source header's own prose (it lives in a receiver repo this item's
  `Paths:` does not reach), which is exactly why DEC-004's final form changes nothing it depends on
  rather than asserting compatibility this worker cannot verify.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: all three changed workflow files are validated for YAML
  well-formedness (actionlint or `yamllint` if configured in CI, matched against existing workflow
  gates) before merge; the new sibling-tag precondition is exercised with gate-inversion evidence (a
  push/dispatch with only one or two of the three tags present must refuse; all three present at the
  same commit must proceed) — see this item's own gate-inversion obligation; the coherence gates named
  in [PD-003] (`check-coherent-set-version.py` and the
  seven DEC-002 gates) are run against the changed tree and confirmed still green (none asserts drift
  this workflow change could introduce, per DEC-002); [PD-002]'s real release execution is verified by live
  evidence (feed API queries, a real `dotnet tool restore` in an isolated environment) rather than
  inferred from a green workflow run, matching the discipline the existing `coord-engine` registry row
  already demonstrates.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: No new tag namespace is introduced (DEC-004's final form), so no new
  repository-level tag ruleset is owed — the existing `kit-release-tags-are-immutable`-class rulesets
  already cover every tag this plan pushes. This is a materially smaller migration footprint than the
  earlier drafts of DEC-004 would have required, and is recorded here as a positive consequence of the
  final design rather than left for a reader to infer.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2409-coherent-set-release/work-model.json` refreshes from the
  plan sources above ([PD-001], [PD-002], [PD-003], [PC-001], [VO-001], [PM-001]) via `fsgg-sdd analyze`/`fsgg-sdd refresh`;
  a stale view after this edit is expected until the next `analyze` run regenerates it, not a defect.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2409-coherent-set-release`.
