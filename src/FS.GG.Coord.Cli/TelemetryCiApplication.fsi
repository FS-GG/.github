namespace FS.GG.Coord.Cli

open FS.GG.Coord

module TelemetryCiApplication =
    val canCreateAdmission: outcome: string -> codeDelivery: string -> observedHead: string option -> expectedHead: string -> bool
    val runWithAssessment: assessment: TelemetryStore.DurabilityAssessment -> action: string -> args: string list -> int
    val runWithWorkspaceForTesting<'binding>:
        resolve: (string option -> string option -> Result<'binding,string list>) ->
        publish: ('binding -> byte array -> Result<string,string list>) ->
        drain: ('binding -> Result<string,string list>) ->
        localStoreRoot: ('binding -> Result<string option,string list>) ->
        action: string -> args: string list -> int
    val run: action: string -> args: string list -> int
