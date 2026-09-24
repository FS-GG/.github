---
title: "Roadmap: V2-CI-I1 unattended credential execution"
category: Roadmap
categoryindex: 3
description: "Six bounded milestones for a trusted post-merge ordinary-v2 settlement path."
---

# V2-CI-I1 — unattended credential execution

Status: **01–02 source merged; 03 secret-free predecessor observed on protected main; 04 installer published; dedicated custody and installed execution pending**.

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
  `docs/operations/v2-ci-ordinary-credential-review.md`. Milestone 03 replaces the initial fixture-only
  evidence producer and provisional check names with native API reads and observed check identities.
- [x] **02 — Typed settlement-only Coordination command.** In `FS.GG.Coordination`, compose the existing typed
  readers and transport behind one non-interactive command. Add expected-absent shared-shard journal initialization,
  canonical signed intent, a stable original plan/operation/attempt identity across workflow reruns, one CAS
  attempt, independent readback and same-attempt reconciliation for unknown replies. Refuse altered intent,
  wrong key/anchor, duplicates, stale authority and unsafe replay. `OrdinaryGitHubRuntime.readJournalCore`
  currently assumes `refs/heads/fsgg/v2/journal/operation/{first2digest}` exists before it stores
  `ordinary/{digest}.json`; this milestone reuses `LedgerInitializationAdapter`'s expected-absent/ref
  reconciliation patterns and preserves other shard entries while closing that first-use gap.
  [Coordination #515](https://github.com/FS-GG/FS.GG.Coordination/pull/515) merged source
  `a6c1155591836582e28a0651def31cb6afeb8859` as protected commit
  `57f1328345fd58915da3f398e5c442dbd40b891a`. Its exact-head hosted gates passed after
  one targeted rerun of an Apalache startup timeout. The zero-argument production command refuses
  missing receipt or custody; a separate `rehearse` command pins sandbox policy, keys, rulesets and
  synthetic epoch. Both commands keep incomplete outcomes nonzero, and a later epoch cannot mint a
  second operation identity for the same source.
- [ ] **03 — Trusted two-job workflow.** Add a push-to-main-only workflow whose secret-free predecessor invokes
  milestone 01 and whose dependent credential job invokes only the pinned milestone 02 artifact. Prove the
  actual job dependency and environment/permission boundary, including good, deliberately broken and
  unavailable-tool preflight cases. Implement and bind the required checks to their real exact identities, or
  revise the prospective policy names before installation. Install the synthetic test; do not add request or
  manual triggers. The prepared workflow now observes a unique merged PR and independently fetches its PR-head
  check runs for the exact live main-protection population plus the two typed settlement checks. It binds a
  receipt to the
  protected push, policy digest, workflow revision and run identity; the dependent environment job rederives
  the same receipt. Its credential effect stays gated inactive and refuses if enabled without the pinned
  Coordination provider. A native read against merged PR #3663 and its exact head succeeded under the initial
  source-only policy; focused good/stale/wrong-app/missing-population cases pass. The first actual protected-main
  [run 36019271890](https://github.com/FS-GG/.github/actions/runs/36019271890) succeeded on
  merge `bbadbba529891af9a969a561936badb654563330`: the preflight emitted public
  artifact `10815877698`
  (`sha256:f5be586f8f0cc28a28d3445e8879f068895c4f76748170d9f3d9bbc3a3167f92`), and the
  credential job was skipped because activation is false. This is hosted source qualification,
  not the installed two-job success required to check off 03. An activation review found that the provisional
  initial two-check list omitted the other live main-protection gates and that a PR-head result did not by
  itself prove the merged tree. The observer now requires the exact live eight-check population and App ID,
  equality of PR-head and merged Git trees, native workflow/run/job/attempt identity for each check, and
  current-main policy/workflow bytes plus installed-anchor bytes before revalidating a receipt. A changed
  activation policy thus revokes an older queued run or rerun. These are source-level fences;
  the credential job stays inactive until its installed provider and hosted matrix qualify. The first
  hardened [run 36022292819](https://github.com/FS-GG/.github/actions/runs/36022292819)
  refused a duplicate successful coherence check while the credential job remained skipped. The
  corrected producer selection then succeeded on protected-main
  [run 36025187067](https://github.com/FS-GG/.github/actions/runs/36025187067), merge
  `bd88ef6a2bce98ce3274cc716004ed759b528dd9`, with public artifact `10819737108`
  (`sha256:1af1e223d90b7dba5ff1c74d6db7e627043317fbab9fb0e6603a8981cdbb36b1`).
  Its receipt binds the exact merged tree, both settlement checks, all eight live branch gates,
  and each selected native run/job/check identity. The dependent credential job was skipped.
  The pinned CLI and isolated rehearsal source merged in
  [`.github` #3667](https://github.com/FS-GG/.github/pull/3667) as `bc083c69f1071179c238d738f830e725953ce8fc`.
  Its protected-main [run 36044032033](https://github.com/FS-GG/.github/actions/runs/36044032033)
  qualified the public receipt (`sha256:9571ee7e657f91ac44fd52be5f882c209a3d635ea5cfcb8460c849ef364bfb64`)
  with two settlement checks and eight branch gates. The credential job was correctly skipped because
  production activation remains false. This verifies installation and secret-free gating, while the
  installed two-job success remains pending dedicated custody and hosted qualification.
- [ ] **04 — Dedicated App, keys and immutable publication.** Through the preconfigured browser registration and protected setup path,
  create the dedicated ordinary-v2 App and authorizer identities, accept the public anchor, provision only the
  named `ordinary-v2` environment secrets, publish the same prepared installer bytes to both feeds and read back
  environment, installation, permission and artifact state. Do not reuse v1 or callable keys.
  NuGet.org may add its signature to the served archive; compare the package payload and pin the final
  served archive digest used by the workflow.
  [Coordination #516](https://github.com/FS-GG/FS.GG.Coordination/pull/516) and
  [#517](https://github.com/FS-GG/FS.GG.Coordination/pull/517) merged the protected 0.1.2 publisher
  and its retained-artifact recovery path. Preparation run `36028503111` retained exact archive
  `sha256:5633d9be2263e77437619a75a2411e4e82e48d763124df472b8e1b7c9e1dfd68`;
  the first publication attempt refused before effects on a different checkout-root build digest.
  [Run 36041319746](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36041319746)
  reproduced the retained bytes at the original hosted root and pushed them to GitHub Packages and
  nuget.org; anonymous installation waited for nuget.org indexing. Recovery
  [run 36042503421](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36042503421)
  verified both served feeds, anonymous installation, tag `v0.1.2` at protected source merge
  `57f1328345fd58915da3f398e5c442dbd40b891a`, and the
  [release](https://github.com/FS-GG/FS.GG.Coordination/releases/tag/v0.1.2). Nuget.org's signed
  served archive is `sha256:627d9f54d038d47ef59635f92bd4fd4af2da7bfc971a938b6de1292503b0307e`;
  its package payload matches the retained archive. Dedicated App and authorizer enrollment, public
  anchors and protected environment secrets remain absent, so this milestone stays unchecked.
- [ ] **05 — Isolated hosted installed matrix.** With bounded non-production refs, exercise success,
  wrong-key/anchor, stale evidence/authority, altered payload, wrong workflow/environment, duplicate attempt,
  crash-before-write, crash-after-write/unknown reply and stable replay reconciliation. Preserve one effect
  identity and a public receipt/readback; a real protected operation is not a fixture.
  The isolated sandbox shell is now `FS-GG/FS.GG.Coordination.Authority.Sandbox` (repo ID
  `1385801070`, seed `fe6292e9…`), with active writer/integrity rulesets `23947019`/`23947025` and
  separate main-only `ordinary-v2-rehearsal` environment `22669445419`. Both rulesets currently have
  zero App bypass and the environment has zero secrets. Its separate synthetic OpenV2 epoch ref
  `refs/heads/ordinary-v2-rehearsal-epoch` points at `4f02add98e091cd268979468f9c15ffe59435d43`;
  the readback binds aggregate `fleet-cutover:fs-gg-v2-rehearsal`, generation 1 and the event digest.
  A distinct rehearsal App/key and compiled profile are required so a synthetic run cannot mint a
  production Authority token.
  A manual [rehearsal run 36044184438](https://github.com/FS-GG/.github/actions/runs/36044184438)
  refused in its secret-free preflight on the absent public rehearsal anchor; the dependent sandbox
  credential job was skipped. This is the expected missing-anchor negative case, not the installed matrix.
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

The first `.github` ordinary-v2 preflight run `36019271890` adds one after-source observation: its job was
created at 15:19:01Z, started at 15:20:15Z and completed at 15:20:25Z, yielding about 74 seconds of
queue and 10 seconds of hosted execution. Its credential job was skipped and consumed no runner. This one
inactive run is neither an installed-route timing nor a comparable after-cohort for the Coordination baseline.
The hardened run `36025187067` queued for about 40 seconds and executed its preflight for 16 seconds;
its credential job also remained skipped. Neither run demonstrates installed-path latency.
The pinned-installer source run `36044032033` queued for about two seconds and executed preflight for
23 seconds; its credential job was also skipped. The three inactive observations are too few and lack
an installed credential stage to support an after-cohort or net efficiency claim.

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
