namespace FS.GG.Telemetry.Host

/// Reads only inactive preparation bytes and metadata; never opens credentials or starts a verifier.
module InactivePreparation =
    /// Closed success or unavailable JSON bytes, including a terminal newline, and CLI exit code.
    val read : configPath:string -> int * byte array
