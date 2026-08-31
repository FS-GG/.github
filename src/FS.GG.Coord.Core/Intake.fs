namespace FS.GG.Coord

open System.Text.RegularExpressions

module Intake =
    [<Literal>]
    let Schema = "fsgg.coord.intake/v1"

    type Disposition = Create | Reuse

    type Draft =
        { Schema: string; Id: string; Owner: string; Repository: string; Title: string
          Observed: string; RootCause: string; Acceptance: string; Verification: string
          Paths: string list; Class: string; Status: string; Disposition: Disposition option
          Phase: string option; Severity: string option; BlockedBy: string option
          BlockedOn: string option; BacklogReason: string option; JudgementQuestion: string option }

    type Finding = { Field: string; Detail: string }

    let private canonicalSeverity value =
        [ "Critical"; "High"; "Medium"; "Low"; "Unset" ]
        |> List.tryFind (fun candidate ->
            System.String.Equals(candidate, value, System.StringComparison.OrdinalIgnoreCase))
        |> function
            | Some severity -> Ok severity
            | None -> Error "must be Critical, High, Medium, Low or Unset"

    let private required field value =
        if System.String.IsNullOrWhiteSpace value then Some { Field = field; Detail = "is required" } else None

    let private validPath (path: string) =
        not (System.String.IsNullOrWhiteSpace path)
        && not (path.StartsWith "/")
        && not (path.Contains "\\")
        && path.Split('/') |> Array.forall (fun segment -> segment <> "" && segment <> "." && segment <> "..")

    let validate (draft: Draft) =
        let findings =
            [ if draft.Schema <> Schema then yield { Field = "schema"; Detail = $"must be '{Schema}'" }
              for field, value in [ "id", draft.Id; "owner", draft.Owner; "repository", draft.Repository; "title", draft.Title; "observed", draft.Observed; "rootCause", draft.RootCause; "acceptance", draft.Acceptance; "verification", draft.Verification; "class", draft.Class; "status", draft.Status ] do
                  match required field value with | Some finding -> yield finding | None -> ()
              if List.isEmpty draft.Paths then yield { Field = "paths"; Detail = "must declare at least one path" }
              if draft.Paths |> List.exists System.String.IsNullOrWhiteSpace then yield { Field = "paths"; Detail = "must not contain an empty path" } ]
            @ [ if draft.Owner.Contains "/" || draft.Owner.Contains " " then yield { Field = "owner"; Detail = "must be an owner name, not a repository ref" }
                if draft.Repository.Contains "/" || draft.Repository.Contains " " then yield { Field = "repository"; Detail = "must be a repository name, not a repository ref" }
                if draft.Id |> Seq.exists (fun c -> not (System.Char.IsLetterOrDigit c || c = '-' || c = '_' || c = '.')) then
                    yield { Field = "id"; Detail = "must contain only letters, digits, '-', '_' or '.'" }
                if draft.Paths |> List.exists (validPath >> not) then
                    yield { Field = "paths"; Detail = "must be relative repository paths without empty, '.' or '..' segments" }
                match Types.itemClassOfWireName draft.Class with
                | Some parsed when Types.itemClassWireName parsed = draft.Class -> ()
                | Some parsed ->
                    yield
                        { Field = "class"
                          Detail = $"must use canonical board value '%s{Types.itemClassWireName parsed}'" }
                | None ->
                    let known =
                        Class.legalClasses
                        |> List.map Types.itemClassWireName
                        |> String.concat ", "
                    yield { Field = "class"; Detail = $"must be one of %s{known}" }
                match draft.Severity with
                | Some value ->
                    match canonicalSeverity value with
                    | Ok canonical when canonical = value -> ()
                    | Ok canonical -> yield { Field = "severity"; Detail = $"must use canonical board value '{canonical}'" }
                    | Error detail -> yield { Field = "severity"; Detail = detail }
                | None -> ()
                match draft.Disposition with
                | None -> yield { Field = "disposition"; Detail = "must explicitly be create or reuse" }
                | Some _ -> ()
                match draft.Status with
                | "Backlog" ->
                    match draft.BacklogReason with
                    | Some ("parked" | "awaiting-judgement" | "epic" | "not-yet-actionable") -> ()
                    | _ -> yield { Field = "backlogReason"; Detail = "must be parked, awaiting-judgement, epic or not-yet-actionable for Backlog" }
                | "Blocked" ->
                    match draft.BlockedBy, draft.BlockedOn with
                    | Some dependency, None when Regex.IsMatch(dependency, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+#[1-9][0-9]*$") -> ()
                    | None, Some ("human/action" | "human/decision") -> ()
                    | _ -> yield { Field = "blockedBy"; Detail = "Blocked requires exactly one canonical dependency or human/action|human/decision park" }
                | "Ready" ->
                    if draft.BlockedBy.IsSome || draft.BlockedOn.IsSome || draft.JudgementQuestion.IsSome then
                        yield { Field = "status"; Detail = "Ready cannot carry a dependency, human park or judgement question" }
                | _ -> yield { Field = "status"; Detail = "must be Backlog, Ready or Blocked" } ]
        if List.isEmpty findings then Ok draft else Error findings
