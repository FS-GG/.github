namespace FS.GG.Coord

module Intake =
    [<Literal>]
    let Schema = "fsgg.coord.intake/v1"

    type Disposition = Create | Reuse

    type Draft =
        { Schema: string; Id: string; Owner: string; Repository: string; Title: string
          Observed: string; RootCause: string; Acceptance: string; Verification: string
          Paths: string list; Class: string; Status: string; Disposition: Disposition option }

    type Finding = { Field: string; Detail: string }

    let private required field value =
        if System.String.IsNullOrWhiteSpace value then Some { Field = field; Detail = "is required" } else None

    let validate (draft: Draft) =
        let findings =
            [ if draft.Schema <> Schema then yield { Field = "schema"; Detail = $"must be '{Schema}'" }
              for field, value in [ "id", draft.Id; "owner", draft.Owner; "repository", draft.Repository; "title", draft.Title; "observed", draft.Observed; "rootCause", draft.RootCause; "acceptance", draft.Acceptance; "verification", draft.Verification; "class", draft.Class; "status", draft.Status ] do
                  match required field value with | Some finding -> yield finding | None -> ()
              if List.isEmpty draft.Paths then yield { Field = "paths"; Detail = "must declare at least one path" }
              if draft.Paths |> List.exists System.String.IsNullOrWhiteSpace then yield { Field = "paths"; Detail = "must not contain an empty path" } ]
        if List.isEmpty findings then Ok draft else Error findings
