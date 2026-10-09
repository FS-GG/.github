namespace FS.GG.Coord.Cli

open FS.GG.Coord

module TelemetryCiApplication =
    val canCreateAdmission:
        outcome: string -> codeDelivery: string -> observedHead: string option -> expectedHead: string -> bool

    val classifyProfileForTesting:
        bytes: byte array -> workflow: string -> job: string -> step: string -> Result<string * string, string list>

    val projectPopulationForTesting:
        profileBytes: byte array option -> assignment: TelemetryCi.Assignment ->
        population: FS.GG.Coord.GitHub.CiReads.PopulationSnapshot -> Result<byte array list, string list>

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
