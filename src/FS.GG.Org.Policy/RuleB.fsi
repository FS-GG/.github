namespace FS.GG.Org.Policy

/// Project subjects selected by path filters and dependency edges those filters omit.
type RuleBCoverage =
    { Subjects: string list
      Uncovered: (string * string) list }

/// Pure path coverage reduction over a caller-supplied closed project graph.
module RuleB =
    /// Refuse malformed facts, then report selected subjects and uncovered dependencies.
    val inspect:
        patterns: string list ->
        graph: Map<string, string list> ->
        Result<RuleBCoverage, SyntaxDiagnostic>
