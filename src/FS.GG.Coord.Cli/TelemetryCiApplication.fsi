namespace FS.GG.Coord.Cli

module TelemetryCiApplication =
    val canCreateAdmission: outcome: string -> codeDelivery: string -> observedHead: string option -> expectedHead: string -> bool
    val run: action: string -> args: string list -> int
