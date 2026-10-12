namespace FS.GG.Coord.Cli

open FS.GG.Coord

module TelemetryCiApplication =
    val continuationPrivateCustodyForTesting:
        directory: string -> action: ((unit -> unit) -> Result<'T, string list>) -> Result<'T, string list>

    val continuationExactIntentForTesting:
        directory: string -> name: string -> bytes: byte array -> Result<byte array, string list>

    val prepareContinuationPendingForTesting:
        expected: FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Identity -> now: System.DateTimeOffset ->
        existingReceiverFacts: FS.GG.Coord.TelemetryStore.Fact list ->
        checkpoint: FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Checkpoint ->
        phases: (byte array list * byte array list * byte array list) -> Result<byte array, string list>

    val recoverContinuationPendingForTesting:
        expected: FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Identity -> now: System.DateTimeOffset ->
        existingReceiverFacts: FS.GG.Coord.TelemetryStore.Fact list -> checkpointBytes: byte array ->
            Result<byte array option * int * int, string list>

    val advanceContinuationPendingForTesting:
        expected: FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Identity -> now: System.DateTimeOffset ->
        scope: FS.GG.Coord.TelemetryReceipt.Scope -> existingReceiverFacts: FS.GG.Coord.TelemetryStore.Fact list ->
        checkpointBytes: byte array -> receiptJson: string -> Result<byte array * bool, string list>

    val receiverContinuationReadbackForTesting:
        path: string -> assessment: FS.GG.Coord.TelemetryStore.DurabilityAssessment -> itemId: string ->
        scope: FS.GG.Coord.TelemetryReceipt.Scope ->
            Result<string * string * int64 * FS.GG.Coord.TelemetryStore.Fact list, string list>

    val acquireContinuationForTesting:
        transport: FS.GG.Coord.GitHub.Transport.IDeadlineSinglePageGitHubTransport ->
        expected: FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Identity -> clock: (unit -> System.DateTimeOffset) ->
        admitted: bool -> receiverRevisionFloor: int64 ->
        original: FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Checkpoint option ->
            Result<FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Identity * byte array * FS.GG.Coord.GitHub.CiReads.ContinuationResult * int, string list>

    val publishContinuationPendingForTesting:
        directory: string -> name: string -> path: string -> assessment: FS.GG.Coord.TelemetryStore.DurabilityAssessment ->
        expected: FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Identity -> clock: (unit -> System.DateTimeOffset) ->
        scope: FS.GG.Coord.TelemetryReceipt.Scope -> checkpointBytes: byte array ->
        publishPayload: (byte array -> Result<string, string list>) -> drainPending: (unit -> Result<unit, string list>) ->
        hook: (string -> unit) -> Result<byte array * string, string list>

    val continueCollectionForTesting:
        directory: string -> name: string -> path: string -> assessment: FS.GG.Coord.TelemetryStore.DurabilityAssessment ->
        expected: FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Identity -> clock: (unit -> System.DateTimeOffset) ->
        scope: FS.GG.Coord.TelemetryReceipt.Scope -> transport: FS.GG.Coord.GitHub.Transport.IDeadlineSinglePageGitHubTransport ->
        publishPayload: (byte array -> Result<string, string list>) -> drainPending: (unit -> Result<unit, string list>) ->
        hook: (string -> unit) -> Result<string * int * FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Checkpoint, string list>

    val canCreateAdmission:
        outcome: string -> codeDelivery: string -> observedHead: string option -> expectedHead: string -> bool

    val classifyProfileForTesting:
        bytes: byte array -> workflow: string -> job: string -> step: string -> Result<string * string, string list>

    val projectPopulationForTesting:
        profileBytes: byte array option -> assignment: TelemetryCi.Assignment ->
        population: FS.GG.Coord.GitHub.CiReads.PopulationSnapshot -> Result<byte array list, string list>

    val runConfiguredContinuationForTesting:
        assessment: TelemetryStore.DurabilityAssessment -> clock: (unit -> System.DateTimeOffset) ->
        transport: FS.GG.Coord.GitHub.Transport.IDeadlineSinglePageGitHubTransport -> args: string list -> int

    val runWithAssessment: assessment: TelemetryStore.DurabilityAssessment -> action: string -> args: string list -> int

    val runWithWorkspaceForTesting<'binding> :
        resolve: (string option -> string option -> Result<'binding, string list>) ->
        publish: ('binding -> byte array -> Result<string, string list>) ->
        drain: ('binding -> Result<string, string list>) ->
        localStoreRoot: ('binding -> Result<string option, string list>) ->
        action: string ->
        args: string list ->
            int

    val run: action: string -> args: string list -> int

    /// Pure exact-LF byte preparation; performs no persistence or publication.
    val normalizePendingBytesForTesting: bytes: byte array -> byte array
    /// Deterministic private phase projection; caller must establish independent receiver/custody authority.
    val projectContinuationForTesting:
        expected: TelemetryCi.ProviderCheckpoint.Identity -> now: System.DateTimeOffset -> admitted: bool ->
        receiverRevisionFloor: int64 -> profileBytes: byte array option -> existingReceiverFacts: TelemetryStore.Fact list ->
        checkpoint: TelemetryCi.ProviderCheckpoint.Checkpoint ->
        population: FS.GG.Coord.GitHub.CiReads.PopulationSnapshot ->
            Result<(byte array list * byte array list * byte array list), string list>
