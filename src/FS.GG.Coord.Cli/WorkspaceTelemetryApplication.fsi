namespace FS.GG.Coord.Cli

module WorkspaceTelemetryApplication =
    type Binding

    /// Inert transport injection for existing spool/receipt flow tests; production uses RemoteClient.
    type RemoteOperations =
        { Lookup: FS.GG.Coord.TelemetryReceipt.Scope -> string -> string -> System.Threading.CancellationToken -> System.Threading.Tasks.Task<FS.GG.Telemetry.RemoteClient.Result>
          Submit: FS.GG.Coord.TelemetryReceipt.Scope -> byte array -> System.Threading.CancellationToken -> System.Threading.Tasks.Task<FS.GG.Telemetry.RemoteClient.Result> }

    type LocalDashboardBinding =
        {
            ConfigPath: string
            Repository: string
            WorkspaceId: string
            StoreRoot: string
            AssociationDigest: string
        }

    /// Read the exact bounded, caller-owned private prelaunch claim descriptor.
    /// Read a bounded private descriptor without following a final symlink or
    /// accepting a device/FIFO. Both descriptor and named identity must stay fixed.
    val readEfficiencyClaimTemplate: path: string -> Result<byte array, string list>

    /// Reuse the same bounded no-follow descriptor reader for explicit passive selections.
    val readPassiveSelection: path: string -> Result<byte array, string list>

    val resolveBinding: configArg: string option -> repositoryArg: string option -> Result<Binding, string list>

    val resolveLocalDashboard:
        configArg: string option -> repositoryArg: string option -> Result<LocalDashboardBinding, string list>

    /// Resolve the existing local producer association without granting or upgrading authority.
    val resolveEfficiencyProducer:
        configArg: string option -> repositoryArg: string option ->
            Result<string * FS.GG.Coord.TelemetryReceipt.Principal * string, string list>

    val tryPublishBinding: binding: Binding -> payload: byte array -> Result<string, string list>
    val tryDrainBinding: binding: Binding -> Result<string, string list>
    val tryLocalStoreRootBound: binding: Binding -> Result<string option, string list>

    val tryPublish:
        configArg: string option -> repositoryArg: string option -> payload: byte array -> Result<string, string list>

    /// Enqueue immutable remote spool bytes only; no receiver/credential operations.
    val tryEnqueueBound:
        configArg: string option -> repositoryArg: string option -> expectedProducer: string option ->
        expectedBinding: string option -> payload: byte array -> Result<string, string list>

    val passiveProducer:
        configArg: string option -> repositoryArg: string option ->
            Result<string * FS.GG.Coord.TelemetryReceipt.Scope * string, string list>

    val tryPublishExpected:
        configArg: string option ->
        repositoryArg: string option ->
        expectedProducer: string option ->
        payload: byte array ->
            Result<string, string list>

    /// Same bounded eligible-head selection used by the actual drain, initially without its advisory cursor.
    val selectReadyFilesForTesting: files: string array -> Result<string list, string list>

    val tryDrainWithOperationsForTesting:
        configArg: string option -> repositoryArg: string option -> operations: RemoteOperations -> Result<string, string list>

    val tryDrain: configArg: string option -> repositoryArg: string option -> Result<string, string list>

    val tryDrainExpected:
        configArg: string option ->
        repositoryArg: string option ->
        expectedBinding: string option ->
            Result<string, string list>

    val privateStateRoot: configArg: string option -> repositoryArg: string option -> Result<string, string list>

    val tryLocalStoreRoot:
        configArg: string option -> repositoryArg: string option -> Result<string option, string list>

    val isConfigured: configArg: string option -> bool
    val tryBinding: configArg: string option -> repositoryArg: string option -> (string * string * string) option
    val configuredPath: configArg: string option -> string
    val selectedProducer: configArg: string option -> repositoryArg: string option -> string option

    val tryPublishBound:
        configArg: string option ->
        repositoryArg: string option ->
        expectedProducer: string option ->
        expectedBinding: string option ->
        payload: byte array ->
            Result<string, string list>

    val run: action: string -> args: string list -> int
