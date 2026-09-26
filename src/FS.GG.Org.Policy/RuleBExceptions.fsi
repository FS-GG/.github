namespace FS.GG.Org.Policy

/// Pure classification of path-bound allow-uncovered comments in supplied workflow text.
module RuleBExceptions =
    /// The exception state for one omitted project directory.
    type Disposition =
        | Uncovered
        | Unsigned
        | Signed of reason: string

    /// Match comments only to directories present in the supplied coverage observation.
    val inspect:
        path: string ->
        text: string ->
        coverage: RuleBCoverage ->
        Result<Map<string, Disposition>, SyntaxDiagnostic>
