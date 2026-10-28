# Incorporating prior art into current delivery

Status: selected evaluation and execution directions; implementation and native
acceptance remain open. The owning issues retain current assignments and results.

The existing [v3 research](../roadmaps/fsgg-v3/02-prior-art-and-research.md) already
covers durable workflows, reconciliation, idempotency, builds and provenance.
These decisions turn relevant mechanisms into bounded evaluations. They neither
adopt every referenced platform nor add all evaluations to current completion
requirements. Benefits remain unmeasured.

## Prove one native execution path

The [reuse plan](https://github.com/FS-GG/.github/issues/4778#issuecomment-6469407081)
prioritizes a small Home run over further simulated operating-system behavior.
Keep fast tests for pure input, policy, journal and reproducible contract failures.
Use real kernel or service checks for boundaries where the simulation repeatedly
diverged. Preserve any current attempt's custody before changing routes.

The first selected route uses a finite systemd-owned controller and direct-QEMU
service, consuming an independently established allocation. The
[route decision](https://github.com/FS-GG/.github/issues/4778#issuecomment-6470299540)
follows a bounded libvirt 12.8.0 source review: held pre-exec children and exact
prebirth scope naming are useful, but the examined creation paths use numeric
leader PIDs without the PIDFD variant needed for the selected delayed-registration
identity contract. This is a scoped integration finding, not a general rejection
of libvirt or installed-runtime qualification.

The [OCI create/start lifecycle](https://github.com/opencontainers/runtime-spec/blob/main/runtime.md)
is a reference for preparing an environment before releasing its user program;
it does not reserve host capacity. [Slurm's cgroup design](https://slurm.schedmd.com/cgroup_v2.html)
informs separation of supervisor, jobs and allocation holder, while this route
still requires finite ownership. Where integration needs a real operating system,
[systemd's integration suite](https://github.com/systemd/systemd/blob/main/test/integration-tests/README.md)
provides a reusable mkosi/VM pattern. None of these references selects a scheduler,
container substitute for the VM, or additional test platform by itself.

Standard manager ownership replaces a requirement for a particular Bash parent,
timer wrapper or custom gate only through an explicit versioned contract. Before
birth, bind the grant to operation, unit, immutable input/source and finite policy.
After activation, bind actual unit generation, process birth, PIDFD and enforcing
resource domain. A manager limit is not reserved capacity: CPU placement, physical
Guest allocation, Host roles, competing demand, backing and collection all need
their actual admission.

[systemd job timeouts](https://www.freedesktop.org/software/systemd/man/latest/systemd.unit.html#JobTimeoutSec=)
cancel jobs without stopping the unit. [Service runtime limits](https://www.freedesktop.org/software/systemd/man/latest/systemd.service.html#RuntimeMaxSec=)
start from service activation. Neither a relative timeout nor a caller-side deadline proves an
absolute bound on a delayed request before manager receipt. The selected lifetime
implementation must join preexisting finite ownership, dependency behavior,
generation/reference custody, stop/drain margins and lost-effect handling.
An uncertain effect retains its original hold; it is not permission to recreate a
unit or release resources.

The [bounded workload profile](https://github.com/FS-GG/.github/issues/4778#issuecomment-6470537115)
is distinct from full runner qualification, whose available-memory and storage
gates remain unchanged. The
[managed Guest decision](https://github.com/FS-GG/.github/issues/4778#issuecomment-6471715693)
selects the real authenticated Guest provider and a PID1-owned rootless payload
service. It does not produce substitute success receipts for the wider legacy
Home/Root/staging role graph. The actual production entry selects the profile;
its result qualifies only that implemented path. Physical memory, address-space
limits, affinity width and CPU quota remain different quantities.

For this one-file profile, the
[artifact-transfer decision](https://github.com/FS-GG/.github/issues/4778#issuecomment-6470832315)
uses the existing authenticated Guest channel. Settle actual payload writers,
transfer bounded exact bytes and manifest into a preheld exclusive Host spool,
then fsync, close writers, restrict and cold-verify actual Host pins and content.
Use the unchanged typed export constructor, copy helper, durable commit and first
cleanup permission. Destination and journal remain outside all retiring devices.
Partial or lost replies retain the original Unknown; hashes alone do not prove
execution or writer settlement. Full BAR/image block export retains its existing
detach/read-only-view requirements.

The same integrator owns the complete native path and the remaining-call audit.
Prepared Guest image, boot/channel entry, actual payload, native adapters and
independent collector are implementation prerequisites, not evidence supplied by
a passing simulation. A signed cloud base is not a prepared execution image.
Standard image provisioning can help, but its actual filesystem, tools, bytes and
boot behavior must join the selected recipe.

The next acceptance result is a fresh production-entrypoint trace through
provisioning/reservation, sealed input, execution, exact output transfer, journal
commit and collection/cleanup, with a precise remaining-gap list and whole-candidate
review. Full BAR/image, reboot, two-machine, publication, installed adoption and
recovery acceptance remain applicable later obligations.

## Bounded evaluations in existing work

| Owning work | Reuse direction and next useful proof | Boundary retained |
| --- | --- | --- |
| [Release recovery, 4753](https://github.com/FS-GG/.github/issues/4753#issuecomment-6470052133) | Map a genuinely needed immutable candidate to [SLSA provenance](https://slsa.dev/spec/v1.2/provenance). Reuse current controls for unrelated admitted source additions, revocation, conflicting intent and lost replies. | Provenance is not publication authorization or external writer fencing. Preserve archive, payload and manifest digest distinctions, current writer exclusion and destination reconciliation before any retry. |
| [Coordination, 4730](https://github.com/FS-GG/.github/issues/4730#issuecomment-6470052750) | Apply observe, decide, persist intent, act and reconcile to an existing transition. Exercise duplicate/out-of-order/missing events and crashes before dispatch, after effect and before acknowledgement. | Delivery IDs deduplicate notifications; business operation IDs bind effects. Akka delivery acknowledgements do not make external effects exactly once. Compare the existing stack and the v3 PostgreSQL-worker baseline before another engine. |
| [Portable builds, 3010](https://github.com/FS-GG/.github/issues/3010#issuecomment-6470053435) | Declare toolchain, libc/linker/sysroot, architecture, dependencies and environment inputs. Build one generated project twice in clean pinned environments, then run the retained artifact in its declared consumer. | Reproducibility does not prove runtime compatibility or resource enforcement. Keep current ABI qualification; no automatic Bazel adoption or ABI change. |
| [Accessibility, 4817](https://github.com/FS-GG/.github/issues/4817#issuecomment-6470054163) | Adapt applicable [ARIA-AT](https://github.com/w3c-cg/aria-at) expectations to one actual game journey with focus, dialog/menu and announcements. Execute required browser/screen-reader coverage. | Automated checks do not establish actual assistive-technology behavior. Keep IME as separate real input-method qualification; preserve accepted rendering work. |
| [Telemetry, 4638](https://github.com/FS-GG/.github/issues/4638#issuecomment-6470054902) | Compare existing UTEL failure tests with [Collector resilience](https://opentelemetry.io/docs/collector/resiliency/) across outages, restart, queue/disk exhaustion and lost acknowledgements. Add only a demonstrated missing case. | Retain immutable batches, receipts, deduplication, attribution and corrections. No additional collector framework is selected. |
| [WASM interfaces, 4646](https://github.com/FS-GG/.github/issues/4646#issuecomment-6470055613) | Separately evaluate one [WIT](https://component-model.bytecodealliance.org/design/wit.html) interface and native [Wasmtime fuel](https://docs.wasmtime.dev/examples-interrupting-wasm.html). Compare copying, ownership, errors, bindings and compatibility. | A Component Model profile is versioned work, not a drop-in browser ABI. Native fuel is not browser fuel or deterministic host imports, clocks and randomness. Neither evaluation becomes a new current acceptance dependency. |

The [release source evaluation](https://github.com/FS-GG/.github/issues/4753#issuecomment-6470790144)
found that several proposed retry/crash controls already exist. Inspect those
controls before creating a second suite or execution engine. Retain any genuine
coverage gap and the operational publication requirement; source inspection is
not a new successful run.

Resource availability, actual installed consumers, licensed journeys and independent
qualification machines still require operational evidence. Prior art can simplify
obtaining that evidence; it cannot supply it. Keep current owning issues authoritative,
retain failures and uncertainty, and capture concise findings without copying
private payloads or logs into public knowledge.
