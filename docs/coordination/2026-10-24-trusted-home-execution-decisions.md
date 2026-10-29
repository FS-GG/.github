# Trusted Home export and dedicated qualification

Status: accepted architecture; implementation and runtime acceptance remain separate.

The user accepted three execution changes on 24 October 2026. The durable ruling is
[Coordination Delivery 4820](https://github.com/FS-GG/.github/issues/4820#issuecomment-6380220325).
This decision applies to the trusted Home route. It preserves the complete acceptance
of Delivery 4820 and Outcome 4650, including original outcomes and first causes,
durability, recovery, actual resource retirement, publication and adoption.

## Accepted changes

A verified durable export may survive termination without preserving the original
source filesystem's device, inode or owner identity. Commit must bind the original
operation, selected content and exact retained copy-observation bytes. Recovery
verifies the committed export; a pathname or a reserialized receipt is insufficient.

Home is the trusted local execution owner for this route. A separate privileged
reader, UID 30034, signing key or production reader attestation is not a prerequisite
for it. This does not claim protection against a compromised Home owner or remove
other profiles' requirements.

Native qualification uses a dedicated, preconfigured Linux runner. Home supplies
the machine/provider proposal and its capacity/access evidence. The coordinator
owns the protected workflow and shared source integration. Provider selection,
source delivery, actual provisioning, qualification and disposal remain distinct.

## Journal and recovery boundary

Retain the original context, idempotency key, journal and compare-and-swap revision.
An occupied legacy entry does not authorize overwriting, rekeying or replay. Quiesce
writers while their output remains available, then copy and commit the exact cold
observation bytes before starting removal.

Read is observational and never grants cleanup. The first successful BeginCleanup
reply supplies a consumed permission; a later cold Read can confirm the recorded
intent but cannot recreate that permission. Missing replies, expired windows and
uncertain effects retain the original hold.

Cleanup after expiry or reboot needs an explicitly admitted finite cleanup-only
continuation bound to the same original and committed export, with current resource
observations. It must not renew useful work or repeat copying. Repository/manifest
image digests and image configuration IDs are different identities; any adapter must
join them through actual image metadata rather than compare their digest tails.

## Delivered runner source and remaining acceptance

[Coordination PR 1063](https://github.com/FS-GG/FS.GG.Coordination/pull/1063)
delivered the reviewed runner source at
`a6196dc36bda5813e8a237f29196605a2d1724a3`. Its reference candidate is four CPUs,
16 GiB guest memory and 256 GiB backing storage. The actual 14 GiB available-memory
and 80 GiB free-space gates remain independent of nominal allocation. The fixed
runner group/label, original job budgets, rootless/no-new-privileges requirements
and source-controlled refusal of unselected image/provider inputs remain in force.

This merge creates no machine or runner registration. Actual supported capacity and
write-domain enforcement, authenticated complete input closure, guest and SDK
compatibility, original-owned lifecycle implementation, and independent retirement
observations remain required. A version label, source test, registration heartbeat
or current free-memory reading cannot establish those effects. One VM does not
establish the separate two-independent-machine acceptance.

Original failures and unavailable historical observations remain retained at their
actual scopes. Consult the live owning issues and
[worker channels](worker-channels.md) for current owners, pending qualification and
mailbox requests; this decision is not a second operational ledger.

## First bounded execution path — 27 October

The [owning resource Delivery](https://github.com/FS-GG/.github/issues/4778) carries
one minimal execution contract and one failing end-to-end test. Its first result
is one bounded rootless workload through launch, reservation, sealed input,
execution, collection, committed export and cleanup. Full BAR image export,
runner qualification, reboot and two-machine requirements remain applicable
later stages. Neither a passing component count nor this bounded path completes
them. The [consolidated decision](https://github.com/FS-GG/.github/issues/4516#issuecomment-6445277806)
retains the same integrator and disjoint contributors. Interfaces change only
when a determining path failure requires repair; updates follow meaningful
milestones rather than unchanged board reads.

Root selects PID1-owned provisioning before helper release: an exclusive CPU
partition, locked Guest RAM, fully allocated backing and complete accounting for
Host and Guest roles. Unknown capacity, complement policy, installed capability
or input joins refuse native execution. Host authentication infrastructure stays
separate from genuinely observed Guest roles; selected phase reservations bind
actual births, sources, scopes and original cuts. A bounded bootstrap may move
into its actual selected caller scope within the same birth and slot, without
renewal or ordinary authority during an uncertain transition.

The original block-export route requires Guest output writers to quiesce before clean unmount and complete detach. The
unchanged export helper reads an exclusively Host-owned read-only view using
actual Host pins. Its context is materialized from real allocation and produced
output facts, preserving the preselected original clocks. Retained archive
custody remains explicitly accounted after VM cleanup; it is not a released
reservation. These [lifetime decisions](https://github.com/FS-GG/.github/issues/4778#issuecomment-6447191874)
retain later export adoption, recovery and policy-release acceptance.

The [28 October reuse decisions](2026-10-28-prior-art-incorporation.md)
supersede this transfer mechanism only for the explicit one-file bounded profile:
the existing authenticated channel supplies an exclusive reserved Host spool,
followed by the same cold pins, typed journal, commit and cleanup boundary.
That profile uses standard managed Host/Guest services and does not qualify the
wider legacy role graph or full BAR/image export. Current source, image and native
readiness remain facts to establish at the owning issue.

The [journal successor](https://github.com/FS-GG/.github/issues/4820) is explicitly
new source, not reconstruction of unavailable historical bytes. It preserves
same-original/key/CAS, exact observations, committed cold read, first process-bound
cleanup permission and observation-only Read. Typed F# policy and executable
Quint correspondence remain required for accepted delivery. Working artifacts
and source fixtures do not establish publication, installation or native success.

The test must enter the actual production main, including input, timer and output
guards. Parent-only OS substitution does not reach independently executing child
guards. A [bounded source harness](https://github.com/FS-GG/.github/issues/4778#issuecomment-6447294015)
may control only encountered raw external kernel/transport facts while executing
actual child startup, policy and collection. It cannot invent successful role
receipts, workload output or native enforcement. Unassembled child boundaries
remain source work, not an external supplier excuse.

Known requirements and the actual entrypoint need checking before implementation.
A completed worker needs an actual runtime resume, not queued mail alone. Preserve
first failures and narrower earlier test scope when repairing either mistake.
These are supported process improvements; savings have not been measured. Live
issues retain execution status and remaining gaps; Atlas records the decisions.

## Replay, collection and evidence — 28 October

The accepted [development replay decision](https://github.com/FS-GG/.github/issues/4778#issuecomment-6452716961)
allows reuse of immutable inputs, source and built artifacts with explicit hashes,
contract versions and prerequisites. Each receipt names the stages actually run.
A historical input reference is not a claim that those bytes were executed.
Processes, reservations, leases, clocks and cleanup observations must be fresh;
stored identities or receipts cannot establish current validity. Periodic full
runs check interface drift. Final acceptance still needs a fresh production
entrypoint through launch, reservation, sealed input, execution, collection and
cleanup, then review of the frozen whole.

Pure reads and publication batches may reuse an exact closed immutable set
between full validation boundaries. Charge every selected item before effects;
retain original deadlines, current identity and stable byte/custody checks.
No result, pin or permission escapes before successful final validation. This
does not authorize caching live resource, birth, capacity or lease observations.
Fixture metadata is not a durable journal: the [ephemeral Source-sidecar decision](https://github.com/FS-GG/.github/issues/4778#issuecomment-6458336311)
preserves atomic locked updates and fresh permissions without changing any
production data, input, export or journal durability requirement.

The [stream and terminal-custody decision](https://github.com/FS-GG/.github/issues/4778#issuecomment-6454517050)
selects explicit bounded VM output pipes and an accounted drainer. Retain the
exact PID1 unit/invocation through terminal observation; observe actual EOF and
complete holder/group/cgroup settlement independently. Terminal state is not
EOF. Derive [VM termination and collection reserves](https://github.com/FS-GG/.github/issues/4778#issuecomment-6455426401)
inside the original controller cutoff, including both stop windows, draining and
outside wait. The keeper may remain accounted with an archive hold after VM
cleanup; that is not keeper retirement or full resource release. Source-policy
budget examples do not qualify native capacity or installed interfaces.

An [outside collector's selected lifetime](https://github.com/FS-GG/.github/issues/4778#issuecomment-6455873828)
can exceed the retiring Root original only within its independently authenticated
provider envelope. Bind the exact role, source, scope and current identities;
retain its own earlier cutoff. Ordinary Root roles and arbitrary scopes gain no
extension. Actual activation must fit the selected work, TERM, KILL and return
cuts before release; incompatible clocks refuse rather than renew.

Source harnesses retain actual receiving identities separately from controlled
Guest observations. A [bounded cross-exec attachment](https://github.com/FS-GG/.github/issues/4778#issuecomment-6457195056)
executes unchanged selected entrypoints, preserves the one original deadline and
enforces the modeled Guest permissions. Instrumentation cannot hide extra
descriptors or unknown participants from production guards. An unimplemented
adapter or source boundary is owned integration work, not an external supplier
hold without a determining supplier fact.

Retain actual exit status, raw waits, PIDFD coldness, group absence, pipe EOF and
cleanup at their separate scopes. Empty command lines after exit do not prove
argument drift; a kill status alone does not identify its source. Unavailable
executable or wait observations stay unavailable. A later byte recheck cannot
erase an earlier custody gap. Correct causal wording on the existing owning
issue, preserve first failures and update only when results or suppliers change.
These decisions do not establish a complete trace, native readiness, publication,
adoption or measured savings.

## Administrative preparation boundary — 28 October

Root selected [complete preparation retirement before fresh workload admission](https://github.com/FS-GG/.github/issues/4516#issuecomment-6483556011).
Preparation retains verified protected immutable files after its processes,
writers, descriptors and units settle. Final execution receives new resources,
identities and original clocks. Preparation is an administrative operation, not
a shorter invocation of the final-only execution profile. The consumer review
found no need for another resident aggregate holder or a production schema change.
An empty slice enforces resource properties but is not a process lifetime timer.

The independently reviewed [preparation retention decision](https://github.com/FS-GG/.github/issues/4516#issuecomment-6483852472)
uses standard manager state: `Type=exec`, `ExitType=cgroup`,
`RemainAfterExit=yes`, `Restart=no` and `CollectMode=inactive`. Successful
active/exited state and failed state retain metadata for external collection.
This supersedes the earlier preparation-only demand for a persistent IPC
reference; final allocator and collector reference guards are unchanged.
The rejected default `ExitType=main` proposal could enter exited state while
children remained and disable its runtime timer. That was a source-review
finding, not an observed Home failure. The [pinned service implementation](https://github.com/systemd/systemd/blob/73612ea502d53baf9b0df4336cfc296a7ee0d524/src/core/service.c)
and [unit collection rules](https://github.com/systemd/systemd/blob/73612ea502d53baf9b0df4336cfc296a7ee0d524/src/core/unit.c)
support the narrower mechanism.

The exact command remains unqualified. Relative runtime limits exclude earlier
queue/start time; the actual owner must enforce the selected absolute preparation
cut and leave stop plus independent verification time. Late-copy refusal alone
does not bound a stuck helper. Capture original generation, exit and cold
settlement evidence before stopping. A matched completed stop job and independent
settlement may join subsequent inactivity or garbage collection; absence alone
proves neither success nor cleanup. Lost or late effects retain their original
custody and forbid final launch. No restart, reset or replacement identity is
selected by this decision.

Runtime selection must join the actual generated input and all consumers.
A package-version label or apphost hash does not identify a framework-dependent
journal producer: its managed assemblies, dependency metadata and selected
runtime belong to the closure. Preserved source bundles and private artifact
manifests support custody without becoming publication or installation evidence.
Owning issues [4778](https://github.com/FS-GG/.github/issues/4778) and
[4820](https://github.com/FS-GG/.github/issues/4820) retain current supply and
acceptance. This decision establishes no native trace or broader qualification.

## Runtime bootstrap and preparation stop budget — 29 October

The selected .NET 10.0.12 runtime reaches the actual typed decoder in an ordinary
source check without the optional LTTng provider DSO. The pinned runtime's
[trace-provider loader](https://github.com/dotnet/dotnet/blob/95017c711e6afc1085133d440e42b4bd78155701/src/runtime/src/coreclr/pal/src/misc/tracepointprovider.cpp)
permits that omission for this no-tracing path; it is not a general runtime-file
exclusion rule. Original full supply remains retained. A one-MiB file-size limit
caused CoreLib startup failure; increasing address space from eight to sixteen
GiB did not repair it. A 64-MiB file-size limit with the original eight-GiB address
space and 256-MiB GC heap reached decoding. The pinned
[double-mapping implementation](https://github.com/dotnet/dotnet/blob/95017c711e6afc1085133d440e42b4bd78155701/src/runtime/src/coreclr/minipal/Unix/doublemapping.cpp)
and [dependency resolver](https://github.com/dotnet/dotnet/blob/95017c711e6afc1085133d440e42b4bd78155701/src/runtime/src/native/corehost/hostpolicy/deps_resolver.cpp)
remain part of the selected source evidence. Child limits clamp to inherited
parent limits, so the parent must permit the selected eight-GiB address space.
This is bootstrap/decoder evidence only, not a positive journal transaction,
Home receiving acceptance or native qualification. The 262,144-byte wire cap,
one-MiB spool and four-GiB physical envelope remain unchanged; the complete
input branch is still being assembled.

The reviewed [preparation stop correction](https://github.com/FS-GG/.github/issues/4516#issuecomment-6485956818)
explicitly supersedes the earlier two-times-50-second interpretation without
rewriting it as historical success. Pinned systemd 262.1 can traverse
`STOP_SIGTERM`, `STOP_SIGKILL`, `FINAL_SIGTERM` and `FINAL_SIGKILL` even without
`ExecStopPost`. Preparation therefore selects `TimeoutStopSec=20s`, terminate
start/stop failure modes, `SendSIGKILL=yes`, `KillMode=control-group` and
`Restart=no`, with no stop hooks, watchdog or notify-timeout extensions. Four
nominal 20-second phases leave 20 seconds within the existing 100-second
retirement budget, followed by 20 seconds of independent observation. The
1,020-second total and 900-second copy budget remain unchanged. This does not
change final-workload profiles or establish guaranteed process death.

The actual outside absolute cut remains controlling. Scheduling/coalescing,
suspend, teardown delays or unkillable survivors retain Unknown results and
resource charges when settlement misses that cut. A proposed phase-cap timer
replacement was rejected: manager monotonic time differs from this operation's
boottime clock, job timeouts also apply to stop jobs, and dispatch/accuracy gaps
prevent the claimed exact bound. No new timer or 30-second job timeout is selected.

Standard `systemctl --show-transaction stop` watches the matched job, but its
[wait implementation](https://github.com/systemd/systemd/blob/73612ea502d53baf9b0df4336cfc296a7ee0d524/src/shared/bus-wait-for-jobs.c)
returns zero for both `done` and `skipped`. Require the matched `done` result and
independent process/unit/descriptor/writer settlement; command exit zero alone
is insufficient. The [command implementation](https://github.com/systemd/systemd/blob/73612ea502d53baf9b0df4336cfc296a7ee0d524/src/systemctl/systemctl-start-unit.c)
and pinned service state machine establish this source boundary, not an observed
Home cleanup or permission to launch the final workload.

## Reviewed preparation command boundaries — 29 October

The complete administrative command is source-reviewed; the
[delivered artifact](https://github.com/FS-GG/.github/issues/4516#issuecomment-6486784470)
and owning issue retain the exact candidate and subsequent acceptance. This does
not establish an available operator, executed preparation, protected receiving
runtime or native workload. Original command/review failures remain preserved.
Complete generated consumer input must fit its unchanged cap; a fitting mandatory
subtree alone is not a whole-input proof. No tentative path layout is selected here.

The [scoped settlement decision](https://github.com/FS-GG/.github/issues/4516#issuecomment-6486135708)
applies only to the exact closed trusted foreground copy body: matched original
generation/terminal/stop facts, an empty recursive original cgroup, closed relevant
outside-observer descriptors and maintained exclusive protected stage custody
replace a host-wide writer/descriptor census. Confinement and reviewed executable
closure are prerequisites, not inferred from absence of observed escape. Any
migration, descriptor transfer, outside helper, surviving process or custody drift
retains Unknown and charges. Final production settlement guards are unchanged.
The [capability correction](https://github.com/FS-GG/.github/issues/4516#issuecomment-6486217402)
selects only `CAP_DAC_READ_SEARCH`: UID 0 without it cannot necessarily traverse
or read the supplied user-owned protected files. No permission widening,
`DAC_OVERRIDE`, `SYS_ADMIN` or `SYS_PTRACE` is selected for this preparer.

The [single-service mechanism](https://github.com/FS-GG/.github/issues/4516#issuecomment-6486339237)
uses its own bounded standard service under the existing system slice, without a
new preparation aggregate, resident holder or separately protected body file.
Pass the exact reviewed nonsecret body as one literal shell argument, with manager
environment expansion disabled and exact decoded argv/flags checked. Preserve
body bytes and argument positions; no evaluation or unquoted interpolation is
admitted. Fresh stage creation follows service birth under the verified protected
parent. The [existing operator route](https://github.com/FS-GG/.github/issues/4516#issuecomment-6486589703)
uses exact reviewed code argv and bounded selection stdin rather than new opaque
proof files. Actual operator authority, finite ancestor/outside-owner lifetime,
lineage and custody remain necessary: JSON, selection and source hashes bind
bytes but cannot create them.

The [cooperative accounting decision](https://github.com/FS-GG/.github/issues/4516#issuecomment-6486388170)
accepts residual Host-pressure/ENOSPC risk for this separate preparation and a
two-GiB storage accounting allowance. This is neither a quota nor exclusive
physical reservation or a guarantee against other actors' growth. Refresh actual
free blocks/inodes and relevant interference before effects; retain actual
allocation and output charges afterward. Known conflicts, excessive output or an
unfitting concrete command still block the operation. Service ceilings and free
memory are not reservations. No broader filesystem/resource promise, final
aggregate, absolute-cut guard or full qualification is changed by these decisions.

## Failed preparation and source-path repair — 29 October

The first actual preparation started at 04:07:41 UTC and its copy body exited 1.
The original stderr was discarded, so the secondary observer exception is not
its cause. That observer misclassified the same failed generation's empty cgroup
as foreign and withheld Stop. The reviewed observer correction permits only the
matched original stop; it grants neither successful copy nor cold-tree acceptance.
Original failed state, partial files, evidence and charges remain retained.
No workload, VM, result transfer, positive journal Commit or cleanup is accepted.
The [owning issue](https://github.com/FS-GG/.github/issues/4778) retains the original
attempt and subsequent operation selections separately from earlier source review.

The [first missing destination](https://github.com/FS-GG/.github/issues/4517#issuecomment-6487930635)
corresponded to a supplied source symlink whose canonical regular target matched
the selected bytes. This supports the nofollow/source-path inference without
reconstructing lost stderr. The [complete source audit](https://github.com/FS-GG/.github/issues/4517#issuecomment-6488075017)
verified all 495 canonical regular files through nofollow opens and full hashes;
eight source aliases were corrected. Preserve payloads, row order, destination
names, sizes, modes and hashes; resolve the source path rather than weakening
nofollow or silently accepting changed content. Root independently verified the
packet, table and mapping. The canonical-table/bounded-failure-marker command
successor was selected but remained unreviewed and unexecuted at this capture.
A cleanup-only recovery design was reviewed, without selection of a native effect.

Keep primary body failure, secondary observation failure and later read-only
metadata at their actual scopes. A bounded failure marker can aid the next
attempt, but cannot recreate the original stderr or grant success. The complete
261,904-byte recipe remains conditional on its declared widths and complete
64-Host-PID-slot bound under the unchanged cap; encoded size is not genuine pin
or execution proof.

The secondary channel carried Home's failure receipt on time. Root's
[role-prefix display filter hid it](https://github.com/FS-GG/.github/issues/4517#issuecomment-6487918846);
the missing-receipt inference was withdrawn. Inspect complete new and edited
comment bodies before attributing silence or dispatch failure, and use the
existing secondary channel when the primary is full. Preserve original reads
and avoid duplicate effects, reconstructed receipts or another status ledger.
