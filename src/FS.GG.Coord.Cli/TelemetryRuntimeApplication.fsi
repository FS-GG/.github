namespace FS.GG.Coord.Cli

open FS.GG.Coord

module TelemetryRuntimeApplication =
    val runObservedCodexExecWithPrelaunch:
        executable: string ->
        assignment: TelemetryRuntime.Assignment ->
        parentContext: TelemetryRuntime.InvocationContext option ->
        relation: TelemetryRuntime.InvocationRelation ->
        storeRoot: string option ->
        lateAfterSeconds: int64 ->
        workspaceBinding: (string * string * string) option ->
        codexArgs: string list ->
        publish: (byte array -> Result<string, string list>) ->
        prelaunch: (TelemetryRuntime.InvocationContext -> Result<unit, string list>) option ->
            int

    val runObservedCodexExecWith:
        executable: string ->
        assignment: TelemetryRuntime.Assignment ->
        parentContext: TelemetryRuntime.InvocationContext option ->
        relation: TelemetryRuntime.InvocationRelation ->
        storeRoot: string option ->
        lateAfterSeconds: int64 ->
        workspaceBinding: (string * string * string) option ->
        codexArgs: string list ->
        publish: (byte array -> Result<string, string list>) ->
            int

    val runCodexExecWith:
        executable: string ->
        assignment: TelemetryRuntime.Assignment ->
        codexArgs: string list ->
        publish: (byte array -> Result<string, string list>) ->
            int

    val runCodexExec: args: string list -> int
    val capabilityStatus: args: string list -> int
