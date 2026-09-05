namespace FS.GG.Coord.GitHub

open System
open System.Text.Json
open System.Text.RegularExpressions
open FS.GG.Coord

module QualificationEvidence =
    [<Literal>]
    let HostedSchema = "fsgg.qualification.hosted-observation/1"

    [<Literal>]
    let ObligationReadbackSchema = "fsgg.qualification.obligation-readback/1"

    type HostedScope = WorkflowRun | Job | CheckRun
    type HostedState = Queued | InProgress | Completed of conclusion: string
    type HostedItem = { Scope: HostedScope; Id: string; HeadSha: string; State: HostedState }
    type HostedSnapshot = { Complete: bool; Items: HostedItem list }
    type ObligationComment = { CommentId: int64; Url: string; Author: string; Body: string }

    let private githubPullPathPattern =
        Regex("^/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/pull/[1-9][0-9]*$", RegexOptions.CultureInvariant)
    let parseHostedSnapshot (bytes: byte array) =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            let root = document.RootElement
            let keys (element: JsonElement) = element.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
            let require (expected: string list) (element: JsonElement) (label: string) =
                if element.ValueKind <> JsonValueKind.Object then raise (FormatException($"%s{label} must be an object"))
                let missing, unknown = Set.difference (Set.ofList expected) (keys element), Set.difference (keys element) (Set.ofList expected)
                if not missing.IsEmpty then raise (FormatException($"%s{label} has missing fields"))
                if not unknown.IsEmpty then raise (FormatException($"%s{label} has unknown fields"))
            require [ "schema"; "complete"; "items" ] root "hosted observation"
            if root.GetProperty("schema").GetString() <> HostedSchema then raise (FormatException($"hosted schema must be '%s{HostedSchema}'"))
            let itemsElement = root.GetProperty "items"
            if itemsElement.ValueKind <> JsonValueKind.Array then raise (FormatException("hosted items must be an array"))
            let items =
                itemsElement.EnumerateArray()
                |> Seq.mapi (fun index item ->
                    let label = $"hosted item %d{index}"
                    require [ "scope"; "id"; "headSha"; "state"; "conclusion" ] item label
                    let value (name: string) = item.GetProperty(name).GetString()
                    let scope = match value "scope" with "run" -> WorkflowRun | "job" -> Job | "check" -> CheckRun | other -> raise (FormatException($"unknown hosted scope '%s{other}'"))
                    let state =
                        match value "state", item.GetProperty "conclusion" with
                        | "queued", conclusion when conclusion.ValueKind = JsonValueKind.Null -> Queued
                        | "in_progress", conclusion when conclusion.ValueKind = JsonValueKind.Null -> InProgress
                        | "completed", conclusion when conclusion.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(conclusion.GetString())) -> Completed(conclusion.GetString())
                        | observed, _ -> raise (FormatException($"invalid hosted state/conclusion '%s{observed}'"))
                    { Scope = scope; Id = value "id"; HeadSha = value "headSha"; State = state })
                |> List.ofSeq
            Ok { Complete = root.GetProperty("complete").GetBoolean(); Items = items }
        with
        | error -> Error [ $"invalid hosted observation JSON: %s{error.Message}" ]

    let observeHosted (snapshot: HostedSnapshot) : Qualification.HostedObservation =
        let scope = function WorkflowRun -> "run" | Job -> "job" | CheckRun -> "check"
        let state = function
            | Queued -> "queued", ""
            | InProgress -> "in_progress", ""
            | Completed conclusion -> "completed", conclusion
        { Complete = snapshot.Complete
          Checks =
            snapshot.Items
            |> List.map (fun item ->
                let status, conclusion = state item.State
                { Qualification.HostedCheck.Scope = scope item.Scope; Id = item.Id
                  SubjectRevision = item.HeadSha; State = status; Conclusion = conclusion }) }

    let renderObligationComment (headSha: string) declaration =
        match declaration with
        | Qualification.NoObligations -> $"<!-- fsgg:delivery-obligations none head=%s{headSha} -->\n"
        | Qualification.Obligation obligation ->
            $"<!-- fsgg:delivery-obligation id=%s{obligation.Id} kind=%s{obligation.Kind} head=%s{headSha} -->\n"

    let private strictObject expected (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.Object then Error "obligation payload must be an object" else
        let expectedSet = Set.ofList expected
        let observed = element.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
        let missing, unknown = Set.difference expectedSet observed, Set.difference observed expectedSet
        let missingText, unknownText = String.concat "," (Set.toList missing), String.concat "," (Set.toList unknown)
        if not missing.IsEmpty then Error($"obligation payload is missing fields: %s{missingText}")
        elif not unknown.IsEmpty then Error($"obligation payload has unknown fields: %s{unknownText}")
        else Ok()

    let parseObligationReadback (bytes: byte array) =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            let root = document.RootElement
            strictObject [ "schema"; "commentId"; "url"; "author"; "body" ] root
            |> Result.bind (fun () ->
                let schema = root.GetProperty("schema").GetString()
                let commentId = root.GetProperty("commentId").GetInt64()
                let url = root.GetProperty("url").GetString()
                let author = root.GetProperty("author").GetString()
                let body = root.GetProperty("body").GetString()
                let validUrl =
                    match Uri.TryCreate(url, UriKind.Absolute) with
                    | true, uri ->
                        uri.Scheme = Uri.UriSchemeHttps
                        && uri.Host = "github.com"
                        && githubPullPathPattern.IsMatch(uri.AbsolutePath)
                        && uri.Fragment = $"#issuecomment-%d{commentId}"
                    | _ -> false
                if schema <> ObligationReadbackSchema then Error($"obligation readback schema must be '%s{ObligationReadbackSchema}'")
                elif commentId <= 0L then Error "obligation readback commentId must be positive"
                elif not validUrl then Error "obligation readback url must be an exact GitHub PR issuecomment URL for commentId"
                elif String.IsNullOrWhiteSpace author then Error "obligation readback author must be non-empty"
                elif String.IsNullOrWhiteSpace body then Error "obligation readback body must be non-empty"
                else Ok { CommentId = commentId; Url = url; Author = author; Body = body })
            |> Result.mapError List.singleton
        with error -> Error [ $"invalid obligation readback JSON: %s{error.Message}" ]
