namespace FS.GG.Coord.Cli

/// Local, deterministic telemetry and bounded roadmap-projection command family.
module TelemetryApplication =
    /// Returns Some exit-code when argv belongs to this command family; otherwise None.
    val tryRun: argv: string list -> int option
