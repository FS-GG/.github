namespace FS.GG.Coord.GitHub

open System
open System.Text.Json
open FS.GG.Coord

module QualificationEvidence =
    [<Literal>]
    let HostedSchema = "fsgg.qualification.hosted-observation/1"

    [<Literal>]
    let ObligationSchema = "fsgg.qualification.obligations/1"

    [<Literal>]
    let private Marker = "<!-- fsgg:qualification-obligations/v1 -->"

    type HostedScope = WorkflowRun | Job | CheckRun
    type HostedState = Queued | InProgress | Completed of conclusion: string
    type HostedItem = { Scope: HostedScope; Id: string; HeadSha: string; State: HostedState }
    type HostedSnapshot = { Complete: bool; Items: HostedItem list }

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
        | :? JsonException as error -> Error [ $"invalid hosted observation JSON: %s{error.Message}" ]
        | :? FormatException as error -> Error [ error.Message ]

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

    let private kindAndIds = function
        | Qualification.NoObligations -> "none", []
        | Qualification.Obligations ids -> "some", ids

    let renderObligationComment (headSha: string) declaration =
        let kind, ids = kindAndIds declaration
        let payload =
            JsonSerializer.SerializeToUtf8Bytes
                {| schema = ObligationSchema; headSha = headSha; kind = kind; ids = ids |}
            |> CanonicalJson.canonicalize
            |> Result.defaultWith invalidOp
        $"%s{Marker}\n%s{payload}\n"

    let private strictObject expected (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.Object then Error "obligation payload must be an object" else
        let expectedSet = Set.ofList expected
        let observed = element.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
        let missing, unknown = Set.difference expectedSet observed, Set.difference observed expectedSet
        let missingText, unknownText = String.concat "," (Set.toList missing), String.concat "," (Set.toList unknown)
        if not missing.IsEmpty then Error($"obligation payload is missing fields: %s{missingText}")
        elif not unknown.IsEmpty then Error($"obligation payload has unknown fields: %s{unknownText}")
        else Ok()

    let private parse (body: string) =
        try
            let payload = body.Substring(Marker.Length).Trim()
            use document = JsonDocument.Parse payload
            let root = document.RootElement
            strictObject [ "schema"; "headSha"; "kind"; "ids" ] root
            |> Result.bind (fun () ->
                let schema, head, kind = root.GetProperty("schema").GetString(), root.GetProperty("headSha").GetString(), root.GetProperty("kind").GetString()
                let idsElement = root.GetProperty "ids"
                if schema <> ObligationSchema then Error($"obligation schema must be '%s{ObligationSchema}'")
                elif String.IsNullOrWhiteSpace head then Error "obligation headSha must be non-empty"
                elif idsElement.ValueKind <> JsonValueKind.Array then Error "obligation ids must be an array"
                else
                    let ids = idsElement.EnumerateArray() |> Seq.map (fun value -> value.GetString()) |> List.ofSeq
                    if ids |> List.exists String.IsNullOrWhiteSpace then Error "obligation ids must be non-empty strings"
                    else
                        match kind, ids with
                        | "none", [] -> Ok(head, Qualification.NoObligations)
                        | "some", _ :: _ -> Ok(head, Qualification.Obligations ids)
                        | _ -> Error($"invalid obligation kind/ids combination '%s{kind}'"))
        with error -> Error($"invalid obligation comment: %s{error.Message}")

    let readObligationComments (expectedHead: string) (bodies: string list) : Result<Qualification.ObligationObservation, string list> =
        let parsed =
            bodies
            |> List.filter (fun body -> body.TrimStart().StartsWith(Marker, StringComparison.Ordinal))
            |> List.map parse
        let errors = parsed |> List.choose (function Error value -> Some value | _ -> None)
        if not errors.IsEmpty then Error errors else
        let values = parsed |> List.choose (function Ok value -> Some value | _ -> None)
        let current = values |> List.filter (fst >> (=) expectedHead)
        match current, values with
        | [], [] -> Ok { HeadSha = expectedHead; Declarations = [] }
        | [], (head, declaration) :: _ -> Ok { HeadSha = head; Declarations = [ declaration ] }
        | matches, _ -> Ok { HeadSha = expectedHead; Declarations = matches |> List.map snd }
