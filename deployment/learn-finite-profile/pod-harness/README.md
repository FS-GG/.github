# The fdev pod harness r1

`learn-pod-harness-r1.py` replaces Home's harness r3b, whose source is not in custody (design Unknown 7). It drives operator r6's
re-qualification (design section 10), the N5b `--cgroups=split` spike and the pod qualification on a synthetic snapshot (design section 11, step 7).
Refs FS-GG/.github#5184 and FS-GG/.github#4647.

**Source only. Nothing here has run on fdev.** Root installs the file from a root-owned copy and admits every run. The `plan` command prints the
admission request; nothing else in this directory has an effect.

## Commands

```text
learn-pod-harness-r1.py plan    --run-id <id> [--argv-json <synthetic argv> --source-identity <hex>]   any uid, no effect: the admission request
learn-pod-harness-r1.py case    --run-id <id> --case <case> --admission-ref <comment URL> [...]        uid 30034: one case
learn-pod-harness-r1.py summary --run-id <id>                                                           uid 30034, read only
learn-pod-harness-r1.py synth-tree --out <new dir> --nonce <nonce>                                      writes only <new dir>
learn-pod-harness-r1.py spike-cgroups --run-id <id>        internal: the main process of the N5b spike unit
```

A run id is 4 to 12 lowercase letters and digits. Every unit, pod and container the harness creates is named `hq-<run id>-<case>`.

## Rules

- It runs only as uid 30034 and only from a root-owned copy closed to group and other writers.
- One case per invocation. A case never retries. Its RESULT is written once (`O_EXCL`) under `~/.local/state/learn-harness/<run id>/`.
- A census before and after each case must be clean: no `learn-*` unit other than inactive, no `learn-*` pod or container.
- The first failed case writes `STOP`, and every later case of the run refuses.
- It stops and resets only the units it started, by exact name. Every unit it starts has its own `RuntimeMaxSec`, so nothing outlives a dead harness.
- A refusal before anything runs writes nothing and does not stop the run. A refusal after something ran is a failure.

## The cases

| Phase | Case | What runs (uid 30034) | Expected |
|---|---|---|---|
| A | `census` | `systemctl --user list-units`, `podman pod ps`, `podman ps`, `podman image exists` (reader image r4, base image) | clean; both images present; operator r6 hashes to `16b6da1e…` |
| A | `a27-unacked` | operator r6 `--precheck-only --id hq-<id>-a27` | exit 5 with exactly `retirement-failure-unacknowledged: a0-20261022t192443z-f1-pre`; no precheck op. Applies only **before** INSTALL-FDEV step 2; afterwards `not-applicable` and r6 is not run |
| A | `a0-summary` | operator r6 `--precheck-only --id hq-<id>-a0` | exit 0; `failures == acked == [f1-pre]`, `unacked == []`; every precheck assertion true; the precheck record `learn-op-success/1` bound to the nonce |
| A | `exclusion-reader` | a dummy `learn-pod-hq-<id>-x` unit (`sleep`), r6 `--precheck-only`, the dummy stopped | exit 5, `exclusion refused: a pod op is active: learn-pod-hq-<id>-x` |
| A | `a29-cap` | two dummy `learn-op-hq-<id>-c1/c2` units, r6 `--precheck-only`, both stopped | exit 5, `concurrency refused: 2 ops already active (max 2)` |
| A | `a0-r4-smoke` | **held** | Home's A0 r4 harness (`12ec5ebf…`) is not in this repository; it pins operator r5 |
| B | `n5b-cgroups` | the spike unit (below) | the spike completes and removes its pod; `members-and-infra-under-unit` passes, `not-under-unit` or `split-refused` is a finding |
| C | `syn-precheck`, `syn-run`, `syn-reuse` | the pod operator on a synthetic claim | **held** until the pod operator's image pins exist (a pin-only revision after INSTALL-FDEV step 7) |

The exclusion case in the other direction (an active reader op refuses a pod op) needs the pod start past its image-pin gate, so it waits with
phase C. The A27b negative with a wrong digest cannot be produced on fdev: ack writer r2 writes only the admitted digest. Both are covered by the
r6 and pod-start test suites.

## The N5b spike

**Question.** Does `--cgroups=split` place the pod's members, their `conmon` and the infra container under the pod unit's cgroup? The pod operator's
driver asserts it (`assert-cgroups`) and aborts if not, and finalizer r5's retirement reads the pod unit's cgroup. The precheck cannot answer it,
because the precheck pod runs with default cgroups outside any unit.

**How.** `systemd-run --user --wait --collect` starts `learn-pod-hq-<id>-n5b.service` with the pod unit's cgroup properties (`Delegate=yes`,
`KillMode=control-group`, its own `RuntimeMaxSec`). Its main process is the harness in `spike-cgroups` mode, which:

1. refuses unless its own cgroup is that unit's;
2. creates the pod the way the pod start creates its own (`--network=none --share=net`, a named infra container);
3. starts two members from the base image (`20a6df20…`, already in the account's storage) with `--cgroups=split` and the run's hardening options,
   running `sleep`;
4. reads `/proc/<pid>/cgroup` of each member, each `conmon` and the infra container, and `podman pod inspect`'s cgroup path;
5. removes the pod on every path, reads back that it is gone, and lists any process left in the unit's cgroup tree.

It writes `n5b-spike.json` (`learn-pod-harness-spike/1`). Verdicts: `members-and-infra-under-unit`, `not-under-unit`, `split-refused` (podman
refused the option in a pod), or `incomplete` (the pod was not removed, a process was left, or the spike was not inside its unit).

It needs no claim, no input, no volume, no credential and neither image of the pod. The recovery path end to end (checkpoints, restarts, kills and
the replay) is `syn-run`: the whole plan on a synthetic claim, so no real claim is consumed.

## The receipt op holds a synthetic claim

Finalizer r5 needs a live claim at retirement, so a no-input op would always end as `learn-op-retirement-failure/1`. The rule chosen: **the install
receipt op is a synthetic-input op**, the first `syn-run`. It is explicit at every hop:

| Hop | The declaration |
|---|---|
| seal and claim | `learn_seal_claim.py --mode synthetic` (#5192): its own record types and directories, `realSpoolProvenance: false` |
| admission | the argv-json `["learn-pod-plan/1","<plan sha256>","input:synthetic"]`; the admission's `argvJsonSha256` binds it |
| pod operator | the kind comes only from that argv; it reads `synthetic-claims/` and `synthetic-inputs/` and mounts the synthetic snapshot at `/in`; the RESULT says `inputProvenance` |
| driver | `--drive ... --input synthetic`; it refuses an admission given for the other kind |
| finalizer r5 | `--id <op> --source-identity <hex> --input synthetic`; every record says `inputProvenance` |

A synthetic op cannot read a real claim and a real op cannot read a synthetic one, so neither can be retired as the other. The alternative, a
finalizer rule for an explicitly declared no-input op, was not chosen. It would add a second retirement path without a sealed snapshot to
re-verify, and the harness needs the synthetic path anyway.

This changes finalizer r5's bytes (the new sha256 is in the pod operator's `PROD`). #5175 also changes finalizer r5; the two changes touch disjoint
functions. Whichever merges second rebases and re-pins.

## The admission request (for Root)

Run order, interleaved with INSTALL-FDEV.md. Every harness command runs as uid 30034 through the host's administrative route, from
`/usr/local/libexec/fsgg-learn/learn-pod-harness-r1.py` (root:root 0755, journalled like the other installed files).

| Step | Who | What |
|---|---|---|
| 1 | Root | install the harness (its sha256 from the merged commit) and record a journal entry |
| 2 | Root | INSTALL-FDEV step 3 (operator r6) **before** step 2 (the ack), so A27 negative can run against the real f1-pre record |
| 3 | uid 30034 | `plan --run-id <id>`: post the RESULT sha256 as the admitted case list |
| 4 | uid 30034 | `case census`, then `case a27-unacked` |
| 5 | Root | INSTALL-FDEV steps 1 and 2 (ack writer r2, the f1-pre ack) |
| 6 | uid 30034 | `case a0-summary`, `case exclusion-reader`, `case a29-cap`, `case n5b-cgroups`, then `summary` |
| later | uid 30034 and Root | phase C after the image pins and the plan decision: `synth-tree`, the synthetic seal, the admission, the volume, `syn-precheck`, the synthetic claim, `syn-run`, `syn-reuse` |

Expected receipts per case are in `plan`'s output. Writes: the harness state directory; r6's precheck op `hq-<id>-a0-pre` writes custody and
evidence as any precheck op does. Units: `learn-op-hq-<id>-a0-pre` and `learn-fin-hq-<id>-a0-pre` (r6), the dummy units, and the spike unit. Pods
and containers: the reader precheck container (r6) and the spike's pod. Nothing touches `/srv`, a claim, an ack, the real spool or the network.

## What is NOT established

- Everything above has run only against fakes. The A27, exclusion and A29 verdicts come from operator r6's own bytes in a fake world; the A0 summary's
  success RESULT is scripted, because r6's precheck needs a container runtime.
- Whether the base image has `/usr/bin/sleep` (the spike's member command); a missing one is a start error, reported as such.
- Phase C's verdict logic is tested on scripted RESULTs and records only.

## Tests

`python3 tests/learn-pod-harness/run.py` (workflow `learn-pod-harness-selftest`).
