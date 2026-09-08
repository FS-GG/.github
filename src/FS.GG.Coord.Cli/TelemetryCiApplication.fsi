namespace FS.GG.Coord.Cli

open FS.GG.Coord

module TelemetryCiApplication =
    val canCreateAdmission: outcome: string -> codeDelivery: string -> observedHead: string option -> expectedHead: string -> bool
    val runWithAssessment: assessment: TelemetryStore.DurabilityAssessment -> action: string -> args: string list -> int
    val run: action: string -> args: string list -> int
