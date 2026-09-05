namespace FS.GG.Coord.Cli

open FS.GG.Coord

/// Deterministic local execution boundary for a typed qualification input.
module QualificationApplication =
    val run: inputPath: string -> executionPath: string -> Result<Qualification.Accepted, string list>
    val runBoundToTree: expectedTree: string -> inputPath: string -> executionPath: string -> Result<Qualification.Accepted, string list>
