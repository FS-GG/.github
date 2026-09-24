---
title: "Roadmap: V2-CI-I1 unattended credential execution"
category: Roadmap
categoryindex: 3
description: "Six bounded milestones for a trusted post-merge ordinary-v2 settlement path."
---

# V2-CI-I1 — unattended credential execution

Status: **ready window 01 complete; feature not delivered; credential publication and activation pending**.

This is the executable subroadmap for the [V2-CI-I1 design](../coordination/2026-09-24-v2-unattended-ci-credential-interlude.md),
[ADR-0088](../adr/0088-ci-owned-unattended-credential-execution.md) and the
[unified roadmap part](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index).
It preserves the current v1 admission and `OpenV2` human gates. Source delivery supplies no protected-write
authority. Routine work uses one accountable owner and one PR per ready window; protected environment,
credential, anchor, publication and receiver effects retain their own authority and readback.

## Outcome and boundary

The selected operation class is `ordinary-post-merge-delivery-settlement`. An automatic protected-main push
must resolve through `GET /repos/FS-GG/.github/commits/{sha}/pulls` to exactly one merged PR whose merge commit
is the pushed SHA. A secret-free predecessor qualifies immutable public evidence. A separate GitHub-hosted
ephemeral job may later use the dedicated `ordinary-v2` environment to perform one typed settlement and
independent readback. Request PRs, manual dispatch, local/self-hosted runners, arbitrary commands, v1 keys and
callable-operation keys are outside the class.

## Milestones

- [x] **01 — Policy and qualification boundary.** Add the versioned class/trigger/workflow/environment policy,
  public credential inventory, fixed reviewer checklist and a hermetic validator. Accept the live single-PR
  association example (`.github` PR #3662, merge `53a0f6c8…`) and refuse wrong event, source, workflow,
  environment, runner, operation class, association population and stale/failed evidence. Record the bounded
  baseline below. Evidence: `policy/v2-ci-ordinary-settlement.json`,
  `tools/v2-ci-ordinary-qualification.py`, `tests/v2-ci-ordinary-qualification/run.py` and
  `docs/operations/v2-ci-ordinary-credential-review.md`.
- [ ] **02 — Typed settlement-only Coordination command.** In `FS.GG.Coordination`, compose the existing typed
  readers and transport behind one non-interactive command. Add expected-absent shared-shard journal initialization,
  canonical signed intent, a stable original plan/operation/attempt identity across workflow reruns, one CAS
  attempt, independent readback and same-attempt reconciliation for unknown replies. Refuse altered intent,
  wrong key/anchor, duplicates, stale authority and unsafe replay. `OrdinaryGitHubRuntime.readJournalCore`
  currently assumes `refs/heads/fsgg/v2/journal/operation/{first2digest}` exists before it stores
  `ordinary/{digest}.json`; this milestone reuses `LedgerInitializationAdapter`'s expected-absent/ref
  reconciliation patterns and preserves other shard entries while closing that first-use gap.
- [ ] **03 — Trusted two-job workflow.** Add a push-to-main-only workflow whose secret-free predecessor invokes
  milestone 01 and whose dependent credential job invokes only the pinned milestone 02 artifact. Prove the
  actual job dependency and environment/permission boundary, including good, deliberately broken and
  unavailable-tool preflight cases. Install the synthetic test; do not add request or manual triggers.
- [ ] **04 — Dedicated App, keys and immutable publication.** Through the browser manifest/protected setup path,
  create the dedicated ordinary-v2 App and authorizer identities, accept the public anchor, provision only the
  named `ordinary-v2` environment secrets, publish byte-identical pinned installer artifacts and read back
  environment, installation, permission and artifact state. Do not reuse v1 or callable keys.
- [ ] **05 — Isolated hosted installed matrix.** With bounded non-production refs, exercise success,
  wrong-key/anchor, stale evidence/authority, altered payload, wrong workflow/environment, duplicate attempt,
  crash-before-write, crash-after-write/unknown reply and stable replay reconciliation. Preserve one effect
  identity and a public receipt/readback; a real protected operation is not a fixture.
- [ ] **06 — Receiver and candidate disposition.** Measure before/after critical path, runner time and narrow
  administrative overhead; record coverage and sample limits. Adopt the exact receiver/profile before GS2-10
  freeze only if installed evidence is complete, otherwise explicitly defer it. Source merge alone cannot mark
  the feature delivered or the writer open.

## Pipeline preflight decision and bounded baseline

Milestone 01 uses a static, pure-stdlib validator and focused mutation tests. A custom state model would add a
second representation before interacting jobs exist, so it is deferred. Milestone 03 must bind a static
dependency check to the actual workflow and separately test failed/missing predecessor outcomes before any
credential job can start. Expected reuse is every change to this policy/workflow boundary; the initial warm
test budget is under 60 seconds. This check prevents an ineligible source from reaching the later credential
job, while coherent qualification still runs and remains the authority for its own evidence.

The baseline is an exact-source snapshot, not a savings or duplication claim. For Coordination main
`316ca817…` on 2026-09-24:

| Existing run | Trigger/result | Wall time | Sum of non-skipped job elapsed |
|---|---|---:|---:|
| [Bootstrap 36004284181](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36004284181) | push / success; reuse decision and manifest ran | 1m41s | 1m34s |
| [Optimistic 36004284186](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36004284186) | push / failed formal aggregate | 33m39s | 161m09s |
| [Optimistic 36006551856](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36006551856) | manual recovery on same SHA / success | 35m03s | 158m45s |

The public-repository timing API reported zero billed duration, which does not mean zero compute; the table sums
job timestamps instead. Queue, setup, useful checks, retry cost and critical path stay separate. These three
runs do not establish duplicated compiler/formal work or a bureaucracy percentage. Milestone 06 needs a larger,
attributed before/after cohort.

## Workspace impact

Affected families are `.github` policy/workflows and the installed Coordination command/receiver. Milestone 01
changes only source policy, review guidance and secret-free validation. Milestone 02 adds a disabled producer
command. Milestone 03 first adds a workflow, but credential execution remains unavailable until milestone 04
publishes and provisions exact identities. Milestone 05 proves installed behavior; milestone 06 alone chooses
receiver/candidate adoption. Fresh workspaces and existing workspaces keep current v1 defaults throughout this
interlude. Clean-creation and upgrade proof belong to receiver adoption, not to the source milestones.

The rootless fdev development path consumes no host wallet, DBus/Secret Service, SSH agent, host Podman socket,
private key, JWT or installation token. The observed `ordinary-v2` environment shell has a custom `main` branch
policy, no required reviewers and no secrets; that observation is not activation.
