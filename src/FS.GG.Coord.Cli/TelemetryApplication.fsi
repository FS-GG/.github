namespace FS.GG.Coord.Cli

/// Local, deterministic telemetry and bounded roadmap-projection command family.
module TelemetryApplication =
    /// Pure command-shape parser used by documentation gates; never reads or writes artifacts.
    val validateInvocation: argv: string list -> Result<unit, string> option
    /// Returns Some exit-code when argv belongs to this command family; otherwise None.
    val tryRun: argv: string list -> int option
