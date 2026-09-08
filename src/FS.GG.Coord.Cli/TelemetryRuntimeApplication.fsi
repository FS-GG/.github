namespace FS.GG.Coord.Cli

open FS.GG.Coord

module TelemetryRuntimeApplication =
    val runCodexExecWith:
        executable: string ->
        assignment: TelemetryRuntime.Assignment ->
        codexArgs: string list ->
        publish: (byte array -> Result<string, string list>) -> int
    val runCodexExec: args: string list -> int
    val capabilityStatus: args: string list -> int
