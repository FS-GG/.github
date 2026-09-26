namespace FS.GG.Org.Policy

/// Project subjects selected by path filters and dependency edges those filters omit.
/// Empty subjects are unscoped, not an authoritative fleet-wide agreement.
type RuleBCoverage =
    { Subjects: string list
      Uncovered: (string * string) list }

/// Pure source-only path coverage reduction over a caller-supplied closed project graph.
/// Discovery, project XML, and workflow YAML are separate inputs.
module RuleB =
    /// Refuse malformed facts, then report selected subjects and uncovered dependencies.
    /// Every referenced project needs a graph node, but the enumerator remains unauthenticated.
    /// Only a literal prefix equal to a project's directory selects that project; broad source
    /// globs and unsupported GitHub filter operators do not silently become project identities.
    val inspect:
        patterns: string list ->
        graph: Map<string, string list> ->
        Result<RuleBCoverage, SyntaxDiagnostic>
