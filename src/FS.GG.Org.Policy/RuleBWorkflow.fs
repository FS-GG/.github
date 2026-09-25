namespace FS.GG.Org.Policy

/// Per-workflow Rule (b) observation over supplied project-reference facts. This is not a fleet
/// verdict: project enumeration, workflow discovery, and installed receiver proof are absent.
type RuleBWorkflowObservation =
    { Subjects: Set<string>
      Omitted: Map<string, RuleBExceptions.Disposition> }

module RuleBWorkflow =
    let private invalid path code message =
        Error { Path = path; Code = code; Message = message }

    let private inspectTrigger path name (graph: Map<string, string list>) (trigger: TriggerSyntax) =
        match trigger.Paths with
        | Missing -> Ok None
        | Invalid why -> invalid path "paths-shape" (name + ".paths: " + why)
        | Sequence [] -> invalid path "paths-empty" (name + ".paths is empty")
        | Sequence patterns ->
            RuleB.inspect patterns graph
            |> Result.mapError (fun diagnostic -> { diagnostic with Path = path })
            |> Result.map Some

    /// Reduce each declared event filter independently. One-sided workflows remain in Rule (b)
    /// scope, even though Rule (a) deliberately does not compare them.
    let inspect path text graph : Result<RuleBWorkflowObservation, SyntaxDiagnostic> =
        WorkflowSyntax.inspect path text
        |> Result.bind (fun syntax ->
            inspectTrigger path "pull_request" graph syntax.PullRequest
            |> Result.bind (fun pullRequest ->
                inspectTrigger path "push" graph syntax.Push
                |> Result.bind (fun push ->
                    let coverage =
                        [ pullRequest; push ]
                        |> List.choose id
                        |> List.fold (fun state next ->
                            { Subjects = state.Subjects @ next.Subjects
                              Uncovered = state.Uncovered @ next.Uncovered })
                            { Subjects = []; Uncovered = [] }
                    RuleBExceptions.inspect path text coverage
                    |> Result.map (fun omitted ->
                        { Subjects = Set.ofList coverage.Subjects; Omitted = omitted }))))
