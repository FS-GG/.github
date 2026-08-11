---
schemaVersion: 1
workId: 2249-receiver-coord-cli-pin-realignment
title: Receiver fs.gg.coord.cli pin realignment
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2249-receiver-coord-cli-pin-realignment/spec.md
sourceClarifications: work/2249-receiver-coord-cli-pin-realignment/clarifications.md
sourceChecklist: work/2249-receiver-coord-cli-pin-realignment/checklist.md
publicOrToolFacingImpact: true
---

# Receiver fs.gg.coord.cli pin realignment Plan

Prose status: planned

## Source Snapshot
- spec: work/2249-receiver-coord-cli-pin-realignment/spec.md sha256:b4f25fff64a07c61dcb1029e5f7b4a0454d65fa5de1975dd15722494a469d447 schemaVersion:1
- clarifications: work/2249-receiver-coord-cli-pin-realignment/clarifications.md sha256:6512095c9f08caaec3e1c677656c7044081135a3878c85ed2270280abee51a3a schemaVersion:1
- checklist: work/2249-receiver-coord-cli-pin-realignment/checklist.md sha256:5604e442ee9b9a908b0c178e742217e6c220a008e34cea0b2853f74931352c91 schemaVersion:1

## Plan Scope
- Work item 2249-receiver-coord-cli-pin-realignment is planned from the current specification, clarification, and checklist facts.
- Requirement count: 1.
- Clarification decision count: 0.
- Checklist result count: 1.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: No new production code is authored. FR-001's comparison behavior
  (scripts/repos-audit.sh's engine-manifest sweep grading every coordination-kit receiver's own
  `.config/dotnet-tools.json` `fs.gg.coord.cli` pin against `registry/dependencies.yml`'s declared
  `coord-engine` version) is already implemented and merged via FS-GG/.github#2277 (2026-08-08,
  closing child #2273), with its gate-inversion regression suite already in
  `tests/repos-audit/run.sh` (224 assertions, including a mutation that reds the suite when the
  declared version fixture changes). This plan's decision is to re-derive FR-001's satisfaction from
  fresh live evidence — the current scheduled `repos-audit.yml` run's own log output and a fresh
  `tests/repos-audit/run.sh` execution in this worktree — rather than re-implement or duplicate the
  comparison. **Correction after round-1 independent review**: this plan originally also asserted
  `.github#2249`'s AC1-AC2 (coherent receiver fan-out) were independently completed. That assertion
  was WRONG and the critic disproved it with this same worktree's own evidence: the six receivers
  (FS.GG.SDD#846, Rendering#1220, Governance#388, Templates#387, Game#585, Audio#241) merged
  2026-08-10T17:46Z to `0.22.1`, but FS.GG.Net#68 merged **two days earlier**, 2026-08-08T16:41Z, to
  `0.21.1` — two different target versions on two different dates, which is piecemeal by AC2's own
  wording ("moved together, not piecemeal"), independent of anything that happened afterward. The
  registry's declared `coord-engine` has since advanced again to `0.23.0` (.github#2345,
  2026-08-11T07:13Z), so as of this plan revision **all seven receivers are behind the live declared
  version and the live `engine-pin` sweep is red for every one of them** (confirmed:
  `repos-audit.yml` run 31476651882). AC4's fallback (record the lag as deliberate and encode a
  permitted-lag comparison instead of equality in the gate) does not apply either: no such decision
  exists anywhere, and `scripts/repos-audit.sh`'s comparison is strict equality with no lag
  mechanism. FR-001 (the gate exists and correctly detects divergence) remains TRUE and is this plan's
  only in-scope requirement; AC1/AC2 (the receivers are AT the declared version, together) are FALSE
  at the current live state and this work item's `Paths:` contain no receiver repository, so this
  plan cannot discharge them — the correction is to stop asserting otherwise, not to invent a fix.
  This PR therefore does not close #2249; #2249 should stay open until either a fresh, genuinely
  coherent receiver bump lands across all seven together (out of this repo's `Paths:` — cross-repo
  work in FS.GG.SDD/Rendering/Governance/Templates/Game/Audio/Net), or a human records an explicit
  deliberate-lag decision and AC4's gate encoding is built for it. Neither is this plan's decision to
  make unilaterally.

## Contract Impact
- PC-001 [PD-001] command report: No CLI, `.fsi`, registry-shape, or workflow-trigger change is made.
  The only artifact this work item produces under its declared `Paths:` is this SDD package itself
  (`work/2249-receiver-coord-cli-pin-realignment/`) plus the readiness evidence under `readiness/`.
  `scripts/repos-audit.sh`, `tests/repos-audit/run.sh`, `registry/dependencies.yml`, and
  `registry/repos.yml` are read for verification but not edited.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Run `tests/repos-audit/run.sh` in this worktree and confirm
  it is green (proving FR-001's suite half is live, not merely historically merged). Independently
  read the latest scheduled `repos-audit.yml` run's own log to confirm the engine-manifest sweep
  currently reports drift for exactly the receivers whose live pin differs from the live declared
  `coord-engine` version, and `ok` for none until they catch up — i.e. the gate is exercised on live
  data, not only on its own fixtures.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: No schema, manifest, or registry migration is introduced. Plan
  schemaVersion 1 is accepted as-is.

## Generated View Impact
- GV-001 [PD-001] workModel: readiness/2249-receiver-coord-cli-pin-realignment/work-model.json
  refreshes from this plan's sources; no other generated view (e.g. driver manifests, dashboards) is
  affected because no source file under a generator's inputs changes.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2249-receiver-coord-cli-pin-realignment`.
