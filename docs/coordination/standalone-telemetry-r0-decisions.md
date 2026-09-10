# Standalone telemetry implementation decisions (R0)

Decision date: 2026-09-09. Scope: the [standalone telemetry roadmap](../roadmaps/2026-09-09-190726-standalone-telemetry-host-and-orchestration.md).
The accountable implementation owner selects these defaults under the user's instruction to complete that roadmap and make necessary decisions. This record freezes implementation inputs; it is not evidence of package release, host installation or operational acceptance.

## Ownership and distribution

`FS-GG/.github` owns the existing telemetry engine, new transport contracts, local integration and shared dashboard. Keep the current `FS.GG.Coord.Cli` / `fsgg-coord-engine` workspace distribution and its coherent `FS.GG.Kit` and `FS.GG.Drivers` release policy. Publish audience-facing packages through nuget.org as well as the existing coherent secondary feed; standalone acceptance must use an anonymous clean restore. No product application project acquires telemetry dependencies.

`EHotwagner/SystemAdmin` owns Main accounts, immutable release staging, systemd units, TLS/firewall, launcher enrollment and recovery integration. It consumes exact published artifacts and never forks the telemetry engine. Orchestration application contracts retain their existing Coordination ownership and Unified Roadmap gates; O0 must extend that authority before implementing a host. There are no source-project references across repositories.

The first supported production profile is Linux x64 with .NET 10 and qualified local durable storage. Framework builds use the repository's SDK selection (`10.0.302`, `latestFeature`); release evidence records the resolved SDK. Optional ASP.NET Core hosting must declare its runtime prerequisite explicitly. Windows, macOS and ARM64 are unsupported until their storage, packaging and browser journeys are qualified; they must refuse activation rather than apply Linux assumptions. Mount-free containers use remote intake, with explicitly lossy unacknowledged local spooling.

Keep Contracts, Client, Local and Dashboard below the optional Hosting/Host dependency boundary. Reuse Core validators and the existing SQLite application implementation; extract shared implementation within this repository where necessary rather than copying it. Host actors belong only in the optional `FS.GG.Telemetry.Host` tool. The independent orchestration tool remains `FS.GG.Orchestration.Host`. Neither host is a workspace prerequisite. Pin and qualify the actual Akka dependency in H1; no untested version is selected here.

## Reviewed immutable inputs and reuse inventory

| Input | Exact revision and implementation boundary |
|---|---|
| `.github` implementation | `4ba1935deb2e11671b024e855824846ff319c5df`; coherent source version `0.87.0` (not a claim of feed availability) |
| SystemAdmin design and hardening consequences | `c688224bf42fce812ede35eee82679dcc53e8bd8`; `docs/main-telemetry-actor-service-design-2026-09-09T160851Z.md` and `docs/fsharp-dev-hardening-consequences-2026-09-09T155613Z.md` |
| Observation contract | `src/FS.GG.Coord.Core/TelemetryStore.fs` / `.fsi`, closed `fsgg.telemetry.ingest/1`; 64 KiB, 64 facts, native identity/revision and canonical digest |
| Persistence | `src/FS.GG.Coord.Cli/TelemetryStoreApplication.fs`; migrations 1–8, SQLite WAL/FULL, Linux file/directory sync and writer lock; Microsoft.Data.Sqlite `10.0.11`, SQLitePCLRaw bundle `3.0.5` |
| Runtime and CI | Core `TelemetryRuntime`, `TelemetryCi`, application adapters and `tools/roadmap-telemetry.py`; preserve prospective activation, expected population, unsupported native usage and clock provenance |
| Projection | `tools/telemetry-dashboard.py`, existing dashboard assets/tests and typed item-detail queries; port/package the private UI path with parity fixtures, leaving the approved publisher intact |
| Qualification | ADR-0084 and `tools/routine-delivery.py`; exact-head required checks and native merge readback remain the source boundary |

The old inbox has batch/fact acceptance but does not supply the complete lifetime producer/batch receipt contract. R1 adds that boundary around the existing fact engine. Keep migrations 1–8 byte-identical. A migration must preserve existing acceptance and correction history; previously unassociated facts remain legacy/unassigned, inaccessible to new workspace-scoped projections.

## Identity, version and receipt amendment

Use a new closed `fsgg.telemetry.envelope/1` around the unchanged ingest payload. Fields are schema, workspace ID, logical producer ID, stream ID, batch ID and payload. IDs are opaque stable enrollment values; no paths, display names, repository URL or assignment ID confers authority. Credentials resolve to an operator-owned allowlist of producer/workspace/stream tuples. Rotation preserves those tuples; revocation changes credential authority, never receipt identity.

An explicit private workspace association may name several canonical repository identities. Clones/worktrees join only through that association. Rename/transfer changes the authorized mapping explicitly and preserves workspace identity. Losing enrollment refuses access. Local-to-remote cutover selects one destination prospectively, records the cutover and reconciles pending inputs; import is a separate identity-preserving operation. Never infer old record ownership or silently upload a database.

R1 must freeze UTF-8 canonical representation and SHA-256 vectors over the complete envelope, rejecting duplicate JSON properties, invalid encoding, unknown fields and unsupported values before hashing. Canonicalization must be shared with the engine where compatible and independently exercised by both adapters. The receipt key is logical producer plus batch ID, with full accepted envelope digest bound immutably. Lookup requires current scope authorization. Changed content conflicts for the identity's lifetime, even after detail expiry. Credential replacement cannot change the namespace.

Protocol major versions negotiate explicitly: `/v1` accepts only envelope v1 plus ingest v1. Unknown versions return `unsupported-version`; old local ingest remains available for existing local callers but cannot masquerade as enrolled remote input. Rollout is R1 core, adapter conformance, exact producer package publication, then configured consumer adoption. No simultaneous upgrade of unrelated workspaces is required.

Durable admission means an fsynced recoverable obligation; application means the existing facts/acceptance/cursor transaction committed. Terminal rejection never creates coverage. Unknown response is retried with the same identity/content. Receipt recovery precedes readiness. Terminal detail expires after 30 days and quarantine payload after 7 days; identity/digest tombstones never expire. Unresolved obligations never expire. Capacity pressure refuses new admission rather than dropping accepted identities.

## Initial qualification budgets

These finite limits are selected implementation defaults, to be measured in L2/H2 before production. Exceeding a performance target requires a recorded budget decision, not an invented passing result.

| Boundary | Default/target |
|---|---|
| Envelope/body | 72 KiB envelope, existing 64 KiB/64-fact payload limit |
| Registry/admission | 128 enrolled producers, 16 concurrent requests, 10-second request deadline |
| Pending capacity | 128 batches and 8 MiB per producer; 1,024 batches and 64 MiB globally; existing retry identities checked before reserving new capacity |
| Drain | At most 128 batches/8 MiB per pass, round-robin producer selection |
| Identity capacity | 1,000,000 retained identities; storage reserve and disk failure refuse admission; no automatic tombstone deletion |
| Quarantine/diagnostics | 64 MiB quarantine, 1,024 bounded diagnostics, no raw observations in logs |
| Retry | At most 5 attempts per invocation, jittered exponential delay capped at 5 seconds; preserve unaccepted spool for later bounded retry |
| Incremental local distribution | At most 10 MiB compressed / 30 MiB installed over the exact baseline package |
| Local performance | Dashboard ready within 2 seconds p95; idle working set at most 100 MiB; warmed in-process 64 KiB durable admission within 100 ms p95; whole cold CLI 64 KiB durable submission within 1,000 ms p95 on qualified local SSD |
| Clean restore | At most 30 seconds on the recorded qualification network; report bytes, cache state and environment alongside elapsed time |
| Main recovery | Daily coherent backup, target RPO 24 hours / RTO 30 minutes; disclose post-backup acknowledgements unavailable without retained replay inputs |

Performance fixtures use at least 100 submissions and 20 cold starts. Do not infer power-loss guarantees from process-crash fixtures. H2 records storage/filesystem, quotas, artifact digests, observed results and the exact deployment plan before activation.

The cold CLI budget was separated after the first accepted-source 0.88.0 release
preparation measured 100 one-shot processes at 665.433 ms median and 892.810 ms
p95. A 1,000 ms p95 limit preserves a finite caller-observed bound with about 12
percent headroom over that real failure. It does not replace the 100 ms warmed
Store target. Qualification measures that target directly against the Store
assembly from the installed package, after warm-up, and records drain/application
latency separately. It also retains a bounded accumulating-pending series because
receipt recovery cost grows with the outstanding backlog.

## Operational decisions and remaining evidence

P1 publication migration is selected as of 2026-09-10. Retain the incumbent
public publisher until the replacement is independently qualified, its exact
cutover is authorized, prior state is reconciled, and recurrence is disabled;
selection alone changes no live publisher. Select private local dashboards and,
for Main, separately authenticated workspace-scoped browser access. No producer
credential grants browsing. Choose paused startup for the initial trusted
orchestration pilot, one runner, no untrusted contributors and no automatic
budget renewal. O0/O2 must bind actual item scope and finite inference budget
to existing upstream authority before dispatch.

SystemAdmin prerequisites are independent: safe replacement/reuse, actual container-to-Main route and TLS, narrowly scoped credential provisioning, privileged service management and an isolated recovery/browser test environment. H2 must inspect available host configuration and produce exact addresses/accounts/paths and artifact digests; these cannot be fabricated from a design. H3 uses disposable producer fixtures and never deletes the user's working container. No host mount, D-Bus or Podman socket is restored.

R0 verification: inspected the exact source files and SystemAdmin revision above; routine eligibility and operation-boundary fixtures run in the owning PR. All subsequent roadmap exits remain independently pending, including public package availability, measured budgets, actual host reachability and orchestration upstream acceptance.
