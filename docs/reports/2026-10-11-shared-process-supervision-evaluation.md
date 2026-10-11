# Shared process supervision: bounded evaluation decision

Date: **2026-10-11**. Scope: **V2-PROC-01.1**.
Owner: Coordination execution maintainer; `.github` programme integration.
Plan: [Shared process execution and supervision](../roadmaps/2026-10-05-shared-process-supervision.md).

Retain the existing routine and contained implementations within their previously accepted scopes.
The bounded inventory and comparison supports that decision; it does not qualify a replacement.
Stop the selected research window. Shared contracts, new backends, consumer migration, publication
and installed adoption in milestones .2–.8 remain undelivered. Existing implementation gaps remain
real work, including bounded cleanup in the affected FsQuint paths.

## Required layers and concrete consumers

The routine runner needs literal arguments, concurrent byte-bounded output, independent process-exit
and reader-EOF observations, preserved first failure, caller cancellation and one original work/cleanup
budget. A contained runner additionally needs membership before target effects, stable process and
scope ownership, aggregate resource limits, bounded observation and retirement, reaping, and an
independent outer owner or explicit refusal when those guarantees are unavailable.

| Consumer inventory pin | Behavior the decision must preserve |
|---|---|
| [SDD TestShared, 4f35ef88](https://github.com/FS-GG/FS.GG.SDD/blob/4f35ef88e7254937f9c3d85d9506ff5484d112ac/tests/Shared/TestShared.fs) | Child execution timeout differs from leader exit while inherited writers hold pipes. Concurrent reads, a separate bounded drain grace, typed held-pipe failure and best-effort tree termination remain distinct. |
| [FsQuint Tooling, fa1dc39e](https://github.com/FS-GG/FsQuint/blob/fa1dc39e61e5e1d83a44d0fbf0b7ab59c8a45381/src/FsQuint.Tooling/Tooling.fs) | Executable hash/version and Quint result policy stay in the adapter. Concurrent output charging, caller cancellation, process exit, EOF and cleanup remain separate; task settlement cannot repair an unbounded cleanup path. |

These are pinned inventory mappings. Neither consumer was migrated or executed by this evaluation.

## Candidate comparison

| Candidate and exact input | Routine layer | Contained layer and disposition |
|---|---|---|
| CliWrap 3.10.5, [source 45819e3](https://github.com/Tyrrrz/CliWrap/blob/45819e3bc44d9be4d778e91a0ec5cf6a5176de56/CliWrap/Command.Execution.cs) | Useful immutable command and concurrent-pipe composition. The selected implementation waits for process exit without a cancellable wait, including finalization. A faulted/cancelled public task does not supply an independent exit result. | It supplies no whole-workload containment or aggregate-limit proof. Retain the current runner; future public-API adaptation needs independent bounded lifecycle ownership. |
| ProcessKit 2.12.0, [source b4528be3](https://github.com/ZelAnton/ProcessKit-fSharp/tree/b4528be3ad4021ce1514933fcfdbf52f8a0babc0) | Public result codes do not expose independent reader EOF or complete cleanup. Historical observations are insufficient for new acceptance. | Its documented Linux process-group default permits `setsid` escape; direct-child parent-death behavior is weaker than whole-scope cleanup. Resource-limit requests require usable cgroups. Membership drain is distinct from reaping. Retain existing implementations. |
| Meziantou ControlGroups, source version 2.0.2, [exact db940dd4](https://github.com/meziantou/Meziantou.Framework/tree/db940dd4d4f05435c571ae01a2322a9aa505e0bd/src/Meziantou.Framework.Unix.ControlGroups) | A focused cgroup mechanism, not a command runner. | Exact source supports controller configuration, typed scalar observations, freeze and subtree-kill submission. Placement, ownership, bounded reads and retirement/reaping remain integration work. Retain the current contained implementation; focused reuse remains a separately selected .4 option. |

CliWrap and ProcessKit package metadata name the same commits as the pinned source archives. This
identity join does not prove reproducible binaries. Their package licenses are MIT; the selected
net10 CliWrap asset has no package dependencies, while ProcessKit includes FSharp.Core and Microsoft
dependency-injection/logging abstractions. No maintenance or performance advantage is established.

ProcessKit's package version 2.12.0 differs from the source project's 2.11.0 seed. The exact
[release workflow](https://github.com/ZelAnton/ProcessKit-fSharp/blob/b4528be3ad4021ce1514933fcfdbf52f8a0babc0/.github/workflows/release.yml)
computes a version from tags, overrides `Version` during packing and subsequently updates the seed.
That explains the source labels; the actual release run and binary reproduction remain unverified.
No additional native campaign is required to resolve that label discrepancy for this retain decision.

## Exact Meziantou mechanism review

The original GET-only acquisition retained three metadata responses and twelve declared source files
at the exact commit. All body SHA-256, Git-blob identities, declared sizes and response receipts matched.
Metadata retained 64,050 bytes; source retained 67,372 bytes, totaling 131,422 within the combined
4 MiB ceiling. Returned source was read as data, never built or executed; no cgroup was created or changed.

The selected [CGroup2 implementation](https://github.com/meziantou/Meziantou.Framework/blob/db940dd4d4f05435c571ae01a2322a9aa505e0bd/src/Meziantou.Framework.Unix.ControlGroups/CGroup2.cs)
migrates a numeric PID through `cgroup.procs`; its `Process` overload forwards the PID. The
[example](https://github.com/meziantou/Meziantou.Framework/blob/db940dd4d4f05435c571ae01a2322a9aa505e0bd/src/Meziantou.Framework.Unix.ControlGroups/readme.md)
starts a process before associating it. Those APIs do not provide placement before target effects
or a generation-bound process lease. Stored paths and ordinary whole-file reads/writes do not provide
held-directory protection, byte caps or original-deadline clipping.

`Kill` writes to `cgroup.kill`; `Delete` removes the directory. Neither operation establishes a
bounded empty-scope observation, owned-child reaping or supervisor-death cleanup. CPU quota, memory,
cpuset and `pids.max` are useful mechanisms; kernel-task counts cannot implement a semantic process
or CLR count. The four `CGroupValue` states distinguish configured, unlimited, unavailable and invalid
scalar values. Observation adapters still need to preserve stronger requirements: missing freezer
events return false, and CPU-stat parsing can leave absent/malformed numeric fields at zero.

The fetched license is MIT. The selected project declares version 2.0.2 and obtains net11.0/net10.0
targets from shared properties; those properties reference Meziantou.Polyfill 1.0.165 and the project
uses Meziantou.NET.Sdk. Imported SDK/targets, package binaries and servicing cadence were not qualified.
The evaluator's pinned .NET 10.0.12 runtime stayed unchanged.

## Original native coverage and limits

The retained compiled evaluator used unchanged source/runtime inputs. Accepted BCL observations
establish only the named finite ordinary cases and independent guard custody; domain cleanup remains
unknown. They do not establish BCL as a selected shared backend.

| Original case | Retained result |
|---|---|
| BCL failing stdout sink | Sink IOException, stdout failure without EOF and independently observed stderr EOF; output charged before retention. Guard custody accepted; domain cleanup unknown. |
| BCL inherited held pipe | Leader exit 0 differs from unresolved reader tasks and incomplete output at the evaluator deadline. Guard custody accepted; domain cleanup unknown. |
| BCL output overflow | IOException and incomplete output with charged bytes exceeding retained bytes. Guard custody accepted; domain cleanup unknown. |
| BCL caller cancellation | Cancellation cause preserved with independently observed exit/read completion. Guard custody accepted; domain cleanup unknown. |
| BCL deadline | Original guard `outer-port-error:TimeoutError`; no evaluator result. Guard terminal/reaping/descriptor custody accepted separately. Exact timeout phase, message and frame were not retained. The failed original was not retried. |
| CliWrap held pipe | Guard `causal-acquisition-error`, FileNotFoundError errno 2 while acquiring a causal child. No candidate result: CliWrap behavior is inconclusive. Original owners entered indefinite passive custody holds after the original window; separately admitted finite retirement disposed only the three exact holders. Original unheld-descendant uncertainty remains. |
| CliWrap failing sink | Prerequisite-blocked under the bounded research stop; no original comparison was launched. This is missing coverage, not a pass or candidate failure. |

The finite administrative retirement did not renew the original window, repair its receipts, prove
whole-scope absence or release unresolved resource reservations. GET acquisition custody was sampled
at 100 ms and observed an empty owned group; it does not establish hard whole-tree containment.

Immediate/double fork, reparenting, `setsid`, PID reuse, namespace/UID drift, channel loss,
supervisor death, aggregate hard caps and protected cgroup controls remain unqualified in this window.
Historical fifteen-case results remain historical. Optional diagnostic rows were omitted in named
native cases, and total native-error coverage remains unknown. No repeated benchmark or speedup claim
is made. The prospective missing-timeout diagnostic repair remains separate from the original failure.

## Bounded exit and evidence

The source gaps suffice to retain both layers. Repairing the supervisor or expanding native coverage
would introduce work beyond the selected comparison budget without selecting an adoptable replacement.
The plan's bounded-evaluation stop therefore closes this inventory/reuse decision with truthful
failed, inconclusive and blocked coverage. It does not complete lifecycle conformance, dependency
adoption or milestones .2–.8. Further work requires a separately selected milestone and its own admission.

Private evidence is retained outside repositories; this report contains sanitized summaries and digests:

| Evidence | SHA-256 |
|---|---|
| Exact source-data response/custody reconciliation | `5ba625b637a4b0e779c8e850a22147d4a78f3f324d41948aa06dfe9cbdd7a672` |
| Exact Meziantou mechanism comparison | `309dedd8f3cc5aff04c8b3359e4ba69091b058cae3acfe39026170015ad94269` |
| Original CliWrap guard failure/unknown custody | `75338d5dabb007126e8cbcffc42b1eefb5abff187c2cc476f1ecb6e89f6e6d49` |
| Limited exact-holder retirement reconciliation | `0ed56bd7ee7261a8f2aac9e0fca4189b8dc591c429bbc3e820795636737b9da8` |

Programme telemetry dispatch publication was refused by the full producer inbox; no token was issued
for this follow-up. Model-token and whole-family usage remain unknown, not zero.
