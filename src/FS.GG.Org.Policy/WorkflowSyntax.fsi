namespace FS.GG.Org.Policy

/// A syntax refusal produced before a policy verdict can be trusted. Later evaluators may add
/// findings without changing these parser failures.
type SyntaxDiagnostic =
    { Code: string
      Path: string
      Message: string }

/// Distinguishes an absent path filter from a valid sequence or an unreadable value such as
/// an explicit YAML null.
type PathsSyntax =
    | Missing
    | Sequence of string list
    | Invalid of string

/// The bounded syntax retained for one GitHub Actions event.
type TriggerSyntax =
    { Declared: bool
      Paths: PathsSyntax
      HasPathsIgnore: bool }

/// The bounded workflow facts consumed by source-only organization policy rules. Run scalars
/// remain data and never become YAML comments.
type WorkflowSyntax =
    { PullRequest: TriggerSyntax
      Push: TriggerSyntax
      RunScalars: string list }

/// A path-agreement result or a refusal to issue one; this source-only scaffold does not
/// install an agreement gate.
type RuleAVerdict =
    | Agreement
    | Finding of string
    | NoVerdict of SyntaxDiagnostic

/// Pure YAML syntax reader for the bounded Actions fields used by policy evaluation.
module WorkflowSyntax =
    /// Parse exactly one supplied workflow document without reading files, resolving
    /// Actions expressions, or judging trigger parity.
    val inspect: path: string -> text: string -> Result<WorkflowSyntax, SyntaxDiagnostic>
