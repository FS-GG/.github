namespace FS.GG.Coord.Cli

/// Process-facing commands supplied by the compiled skill conversion.
module SkillCommandApplication =
    /// Handles skill commands and writes their contracted stdout and stderr bytes.
    /// Returns None for unrelated command families.
    val tryRun: string list -> int option
