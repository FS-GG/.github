namespace FS.GG.Coord.Cli

/// Restricted Observation refresh and read-only programme inspection; files carry identity/history only.
module BoardV2Application =
    open FS.GG.Coord.GitHub

    /// Decode bounded closed historical v2/product v3 shapes and the v4-only private enrollment envelope.
    /// Binding UTF-8 is at most256KiB/depth16; private decoded payload at most128KiB/depth16.
    /// Exact original bytes/nonce must match the current protected public commitment before enrollment.
    /// Product mode requires its selected organization-owned project, one repository and native cohort;
    /// no organization default, user-owned execution or legacy board fallback is introduced.
    val parseBinding: json: string -> Result<V2Projection.Binding, string>
    /// Decode ephemeral organization binding4 observations/1 or binding5 observations/2.
    /// Version2 requires the current source-produced hierarchySha256 in every slice join.
    /// The schema is closed at every object: duplicate, unknown and missing keys are errors;
    /// null explicitly means a missing capacity/reservation observation, never an empty population.
    /// Bounds: 262144 UTF-8 bytes, depth 12, 64 unique slices, 256 reservations, 128 paths or
    /// prerequisites per slice, 16 evidence references per observation and nonnegative counts <=10000.
    /// Exact project/schema/recipe/artifact/population, enrollmentCommitmentSha256 and enrolled issue identities are mandatory.
    /// Native issue ID/update/body/source-plan revision and PR ID/update/head are joined after reads.
    /// Original observation/expiry intervals are retained; decoding never renews a supplier fact.
    /// InputSha256 hashes the strict UTF-8 encoding of the received document text, not an authentication token.
    /// Supplier/evidence text and the exact input digest are integrator-reported, not authentication
    /// or native acceptance. Errors never echo raw input. Current native joins remain mandatory.
    val parseIntegratorObservations:
        binding: V2Projection.Binding -> json: string -> Result<V2ProjectionSource.SelectionContext, string>
    /// The bounded refresh/inspection transport: it stops before dispatch once the binding's scaled request ceiling
    /// (`V2Projection.refreshRequestCeiling`) or deadline (`V2Projection.refreshDeadlineSeconds`) is reached, reporting
    /// coverage Unknown, and never replays a durable mutation. `elapsed` is the invocation's own clock.
    /// Both limits are scaled from inspect measurements; a programme refresh batches its reads to fit them (#5131).
    val boundedWith:
        elapsed: (unit -> System.TimeSpan) -> binding: V2Projection.Binding -> transport: FS.GG.Coord.GitHub.Transport.IGitHubTransport -> FS.GG.Coord.GitHub.Transport.IGitHubTransport
    /// Emit an explicit report wire shape, including gaps and independent acceptance facts.
    val encodeReport: report: V2ProjectionSource.BatchReport -> string
    /// Encode structurally read-only inspection, human planning values, native PR facts and action-specific selection.
    /// Product inspection/1 and binding4 inspection/2 retain their closed wire shapes.
    /// Binding5 inspection/3 alone exposes explicit work classification, native hierarchy and unverified closure.
    /// Without --integrator-stdin the CLI reports missing attributed action/resource observations.
    val encodeInspection: report: V2ProjectionSource.InspectionReport -> string
    /// Existing integrators may supply attributed current action facts; no command/input can mint acceptance.
    /// Context is caller-asserted evidence, not authentication; every derived action still grants no dispatch authority.
    /// Per-slice output preserves stable IDs, owners, operation demand, native joins and reported provenance.
    /// Explicit ordered selection consumes only selected operation demand; unselected alternatives are not holds.
    val encodeInspectionWithContext:
        report: V2ProjectionSource.InspectionReport -> context: V2ProjectionSource.SelectionContext option -> now: System.DateTimeOffset -> string
    /// Decode historical verification evidence. It cannot authorize a current observation or write.
    val decodePreviousReport: json: string -> Result<V2ProjectionSource.BatchReport, string>
    /// Validate the production Board V2 argument grammar without reading files or creating a transport.
    /// Return None for other command families; normalized-path and output custody remain runtime checks.
    val validateInvocation: arguments: string list -> Result<unit, string> option
    /// Production inspect composition with one bounded --integrator-stdin observation document.
    /// The injected reader avoids process-global Console changes; input is consumed once and never cached.
    /// No flag leaves the reader untouched. Refresh and product bindings refuse the flag before reading it.
    /// Native queries/permission reads retain the existing shared request/time budget and no-disk intake gate.
    /// V4 file acquisition is bounded on one opened handle; binding/report parents must be private and
    /// nonsymlink. Reports are exclusive0600 with the supported directory barrier, never raw payload/nonce.
    /// Missing runtime suppliers and relevant native gaps remain attributed Unknown, without blocking inspection.
    /// `lockDirectory` holds the shared writer lock refresh takes; fixtures pass a private directory so no test
    /// opens the production lock (#5126). Production (`tryRun`) uses `$XDG_RUNTIME_DIR/fsgg` (`V2WriterLock`).
    val runWithTransportAndInput:
        lockDirectory: string -> transport: Transport.IGitHubTransport -> reader: System.IO.TextReader -> arguments: string list -> int
    /// Fixed refresh or read-only inspect command against an injected transport, for offline fixtures.
    val runWithTransport: lockDirectory: string -> transport: Transport.IGitHubTransport -> arguments: string list -> int
    /// Trusted inert custody/time seam for the same actual migration caller. Production command
    /// composition always binds the real supported directory barrier and TimeProvider.System.
    /// A callback is test/provider infrastructure, never approval or a filesystem qualification claim.
    /// `lockDirectory` holds the shared writer lock migration-apply takes, as for refresh.
    val runMigrationWithCustody:
        lockDirectory: string -> connection: V2MigrationTransport.IConnection -> syncDirectory: (string -> unit) -> clock: System.TimeProvider -> arguments: string list -> int
    /// Actual packaged migration composition against an inert/provider connection.
    /// Commands (without the board-v2 prefix):
    /// migration-prepare --scope-file PATH --report-file NEW_PATH;
    /// migration-apply --prepared-file PATH --prepared-sha256 SHA256 --accountable-owner TEXT
    /// --selection-reference TEXT --planning-window-until RFC3339;
    /// migration-readback --prepared-file ORIGINAL_PATH.
    /// Planning input is a closed 256 KiB object; prepared input is bounded to 4 MiB.
    /// Executing CLI/adapter bytes and their source metadata must match the selected identities.
    /// Native preparation uses complete pages and two stable determining snapshots. It supplies no
    /// final enrollment/binding IDs until actual native readback; migration reports are not v4 bindings.
    /// Inputs and exclusive outputs require a private Unix parent and refuse symlink/reparse paths. Production durability uses
    /// Linux x64 directory open/fsync after file Flush(true), including new-directory/parent barriers;
    /// unavailable/error barriers refuse before dispatch. Kernel acknowledgements do not establish
    /// physical-filesystem or crash qualification; those remain runtime custody requirements.
    /// A prepare refusal after it owns NEW_PATH writes private NEW_PATH.prepare-failure.json with stage,
    /// bounded code, typed class/reason and exception type only; an existing record refuses unchanged.
    /// Apply binds the separately selected exact prepared-file byte digest and a finite planning window.
    /// It reserves ORIGINAL_PATH.invocation-reserved and writes ORIGINAL_PATH.invocation evidence;
    /// reservations and original intent/response files are never overwritten, removed or replayed.
    /// Each effect receives fresh full determining facts, flushed exact request bytes/digest, and an
    /// independent complete readback. A created view's returned ID must match its unique native readback
    /// before an original semantic filter becomes a dependent exact-ID update in that same invocation.
    /// All reads/effects share the connection's 500-request/270-second ceiling. Lost/partial/error
    /// responses and post-send custody failure stop further writes; reopening an invocation reads only.
    /// No Status/Track/Roadmap item values, issue/PR data, enrollment or unsupported view grouping/sort
    /// configuration is written. No atomic CAS or distributed exactly-once guarantee is asserted.
    /// Public diagnostics are fixed bounded labels; original private native outcomes stay in custody. The explicit
    /// operator invocation binds prepared bytes, executing source/assemblies, principal/scope and
    /// fresh native preconditions. Private exclusive evidence precedes every send; retained invocations
    /// permit readback only. Owner/reference text is accountability, not authentication or a JSON grant.
    val runMigrationWithConnection:
        lockDirectory: string -> connection: V2MigrationTransport.IConnection -> arguments: string list -> int
    /// `board-v2 tend --plan-file NEW [--rules R1,R2,...]` against an injected transport and
    /// clock: plan-only and read-only (#5136 phase 2 PR 3). The default rules are the starting set W1h,T1f.
    val runTendWithTransport: transport: Transport.IGitHubTransport -> clock: System.TimeProvider -> arguments: string list -> int
    /// Recognize board-v2 after validation; refresh composes the qualified writer, inspect only canonical reads.
    val tryRun: arguments: string list -> int option
