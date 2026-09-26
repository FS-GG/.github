namespace FS.GG.Org.Policy

/// A syntax refusal produced before a policy verdict can be trusted.
type SyntaxDiagnostic =
    { Code: string
      Path: string
      Message: string }

/// Distinguishes an absent path filter from a valid sequence or an unreadable value.
type PathsSyntax =
    | Missing
    | Sequence of string list
    | Invalid of string

/// The bounded syntax retained for one GitHub Actions event.
type TriggerSyntax =
    { Declared: bool
      Paths: PathsSyntax
      HasPathsIgnore: bool }

/// The workflow facts consumed by the source-only organization policy rules.
type WorkflowSyntax =
    { PullRequest: TriggerSyntax
      Push: TriggerSyntax
      RunScalars: string list }

/// A path-agreement result or a refusal to issue one.
type RuleAVerdict =
    | Agreement
    | Finding of string
    | NoVerdict of SyntaxDiagnostic

/// Pure YAML syntax reader for the bounded Actions fields used by policy evaluation.
module WorkflowSyntax =
    /// Parse exactly one supplied workflow document without reading the filesystem.
    val inspect: path: string -> text: string -> Result<WorkflowSyntax, SyntaxDiagnostic>
