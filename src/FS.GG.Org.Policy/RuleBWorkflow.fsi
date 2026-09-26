namespace FS.GG.Org.Policy

/// Rule B observations reduced independently across one workflow's declared event filters.
type RuleBWorkflowObservation =
    { Subjects: Set<string>
      Omitted: Map<string, RuleBExceptions.Disposition> }

/// Pure per-workflow Rule B reducer over caller-supplied project graph facts.
module RuleBWorkflow =
    /// Inspect supplied workflow text and classify each dependency omitted by its filters.
    val inspect:
        path: string ->
        text: string ->
        graph: Map<string, string list> ->
        Result<RuleBWorkflowObservation, SyntaxDiagnostic>
