---
title: "V2-CI-I1 installed qualification"
category: Operations
categoryindex: 5
description: "Public hosted evidence and bounded cost for the isolated ordinary-v2 settlement receiver."
---

# V2-CI-I1 installed qualification — 2026-09-24

The ordinary post-merge settlement route is **installed and qualified in the isolated Authority sandbox**. The production App, keys, public anchor and ruleset binding are enrolled; the production workflow remains inactive (`credentialJob.installed=false`) while the real cutover ledger is at `OperatingV1`. Its separate protected `OpenV2` decision remains required before production settlement can run. This record selects the qualified receiver profile for GS2-10 candidate preparation; it does not declare a frozen candidate or open the v2 writer.

## Custody and source binding

The immutable Coordination CLI [0.1.2 release](https://github.com/FS-GG/FS.GG.Coordination/releases/tag/v0.1.2) is installed by both workflows from the nuget.org served archive pinned to `sha256:627d9f54d038d47ef59635f92bd4fd4af2da7bfc971a938b6de1292503b0307e`. The production anchor was merged in [`.github` #3671](https://github.com/FS-GG/.github/pull/3671): App `5064713`, installation `164553252`, Authority repository `1351660651`, authorizer SPKI digest `6121c3f2ab38acf38a37f782eb7775da78598b9cfabc0054917d3d59fe50e3ee`. The separate rehearsal anchor was merged in [#3673](https://github.com/FS-GG/.github/pull/3673): App `5065136`, installation `164565492`, sandbox repository `1385801070`, authorizer SPKI digest `eb605151c5e400f82c0aeebee97f49374c3299b8709e7421d515a5bfc9b5f895`. Both installations were read back through the protected environment workflow with one selected repository, Contents write and Metadata read, and no subscribed events. Each environment has only its three named App ID, App private key and authorizer private key secrets. The public anchors bind the effective writer and integrity rulesets; the integrity rulesets have no bypass actor.

The production [protected-main run 36050715279](https://github.com/FS-GG/.github/actions/runs/36050715279) qualified the secret-free predecessor and skipped the inactive credential job. The published CLI's exact-head tests in [Coordination #515](https://github.com/FS-GG/FS.GG.Coordination/pull/515) cover signed intent, altered payload, stale plan/source/epoch, wrong key and anchor, duplicate attempts, before-journal/after-intent/after-effect cuts, unknown reply and same-attempt replay. The `.github` observer tests cover wrong event, workflow, environment, receipt and check identity. The hosted installed cases below use protected `.github` source and real scoped App installation tokens against the public sandbox only.

## Hosted installed matrix

All six final runs use protected source `b9cbb4687e1e73dbef1cf35245893e58c5f9526a` and one aggregate digest `493256ac65f8a34ec268fd34319bcbc00ccfcfbd38a7122c44fef6af8c421490` at `refs/heads/fsgg/v2/journal/operation/49`. Each run produced an exact-run public predecessor receipt and independently rechecked current source before credential use. The four refusals checked the aggregate absent both before and after the attempt.

| Case | Hosted evidence | Public result and independent readback |
|---|---|
| Wrong App ID | [run 36055202516](https://github.com/FS-GG/.github/actions/runs/36055202516) | `app-id-anchor`; absent |
| Wrong authorizer | [run 36055337896](https://github.com/FS-GG/.github/actions/runs/36055337896) | `authorizer-spki`; absent |
| Stale receipt | [run 36055483756](https://github.com/FS-GG/.github/actions/runs/36055483756) | `current-source-observer-refused`; absent |
| Wrong workflow revision | [run 36055604298](https://github.com/FS-GG/.github/actions/runs/36055604298) | `current-source-observer-refused`; absent |
| Lost ref reply and replay | [run 36055727062](https://github.com/FS-GG/.github/actions/runs/36055727062) | `SettlementPending "effect-response-unknown"`; pending-effect, then complete after no-fault replay |
| Duplicate no-fault attempt | [run 36055992102](https://github.com/FS-GG/.github/actions/runs/36055992102) | Successful terminal outcome; complete readback on the same aggregate |

The recovery run's public receipt is `sha256:8ff9f2cd54fae843afe1f3bb80be3e77439debcd3673919af66063d165107d5c`, bound to source `b9cbb468…`, run `36055727062`, attempt 1. Earlier protected source `4a5fc5bf…` also passed [ref-conflict refusal](https://github.com/FS-GG/.github/actions/runs/36052869960), [lost-reply recovery](https://github.com/FS-GG/.github/actions/runs/36053251998) and [no-fault replay](https://github.com/FS-GG/.github/actions/runs/36053546877). The initial negative-control attempt [36054748990](https://github.com/FS-GG/.github/actions/runs/36054748990) reached the correct `app-id-anchor` refusal but failed a workflow assertion that expected an unwrapped CLI message; [#3677](https://github.com/FS-GG/.github/pull/3677) corrected that assertion before the six final runs.

## Time and administrative cost

GitHub run/job timestamps for the six final hosted runs report end-to-end wall times of **60, 70, 53, 58, 133 and 61 seconds** (median 60.5 seconds; 435 seconds total). Their secret-free predecessor jobs consumed 105 elapsed job seconds in total and their isolated credential jobs consumed 280; the 50-second difference from wall time includes queue and dependency gaps. The 133-second case includes unknown-result reconciliation and replay. The earlier inactive production [run 36044032033](https://github.com/FS-GG/.github/actions/runs/36044032033) executed only a 23-second predecessor; it cannot measure the installed production path. The pre-existing Coordination bootstrap/formal baseline is in the [subroadmap](../roadmaps/v2-ci-i1-unattended-credential-execution.md#pipeline-preflight-decision-and-bounded-baseline). It runs different work, so no before/after savings percentage is inferred.

The six rehearsal runs were manually dispatched for controlled fault selection. The production workflow is push-to-main only, has zero required environment reviewers, and structurally requires no per-operation human prompt, host signing session, token relay or receipt-only PR. One-time App registration, installation, secrets and ruleset enrollment were manual setup work; their elapsed operator time was not instrumented. Therefore the 5% target and 10% administrative ceiling have **no measured production percentage** yet. GS2-10 must retain the cost and latency observation as a candidate criterion rather than treating this small, deliberately faulted cohort as proof of efficiency.

## Disposition

Use the exact published CLI, two-job workflow, public anchors, and isolated profile as the V2-CI-I1 receiver candidate in GS2-10. Preserve `credentialJob.installed=false` for production until the protected cutover ledger reaches `OpenV2` and the GS2-10 exact candidate qualifies it. The real ledger was independently observed at `OperatingV1` on 2026-09-24. Routine operation after activation needs no per-run human interaction; the protected one-time `OpenV2` decision and exceptional incident handling retain their existing owners.
