namespace FS.GG.Coord.GitHub

module V2Enrollment =

    open System
    open System.Text
    open System.Text.Json
    open System.Text.Json.Nodes
    open System.Text.RegularExpressions

    let rowCap = 100

    type Refusal =
        | DuplicateIssue
        | AlreadyEnrolled
        | IsPullRequest
        | NodeIdMismatch
        | NotProjectMember
        | RepositoryOutsideBinding
        | PredecessorCommitmentMismatch
        | PrefixNotPreserved
        | PlanNotAdmitted
        | PlanBlobAbsentOnMain
        | RowCapExceeded
        | ConflictingHumanValue
        | PartialVisibility
        | OutputExists
        | MalformedInput

    let refusalCode (refusal: Refusal) =
        match refusal with
        | DuplicateIssue -> "DuplicateIssue"
        | AlreadyEnrolled -> "AlreadyEnrolled"
        | IsPullRequest -> "IsPullRequest"
        | NodeIdMismatch -> "NodeIdMismatch"
        | NotProjectMember -> "NotProjectMember"
        | RepositoryOutsideBinding -> "RepositoryOutsideBinding"
        | PredecessorCommitmentMismatch -> "PredecessorCommitmentMismatch"
        | PrefixNotPreserved -> "PrefixNotPreserved"
        | PlanNotAdmitted -> "PlanNotAdmitted"
        | PlanBlobAbsentOnMain -> "PlanBlobAbsentOnMain"
        | RowCapExceeded -> "RowCapExceeded"
        | ConflictingHumanValue -> "ConflictingHumanValue"
        | PartialVisibility -> "PartialVisibility"
        | OutputExists -> "OutputExists"
        | MalformedInput -> "MalformedInput"

    type PlanSpec =
        | ProgrammeIssue3008
        | ProtectedDoc of repository: string * path: string

    type Classification =
        | Outcome
        | Delivery

    type RequestIssue =
        { Ref: string
          Slice: string
          Plan: PlanSpec
          Classification: Classification }

    type Request =
        { Cohort: string
          PredecessorPrivatePath: string
          PredecessorMergeSha: string
          Issues: RequestIssue list }

    type NativeKind =
        | NativeIssue
        | NativePullRequest

    type NativeIssueFact =
        { Ref: string
          NodeId: string
          Kind: NativeKind
          Repository: string
          ProjectMember: bool
          Complete: bool
          HumanRoadmap: string option }

    type PlanBlobFact =
        { Repository: string
          Path: string
          OnMain: bool }

    type NativeFacts =
        { ProjectId: string
          Issues: NativeIssueFact list
          PlanBlobs: PlanBlobFact list }

    type PlannedRow =
        { Issue: string
          NodeId: string
          Roadmap: string
          Slice: string
          Classification: Classification }

    type SuccessorPlan =
        { TemplateJson: string
          Payload: byte array
          Appended: PlannedRow list
          PredecessorRows: int
          TotalRows: int }

    type Seams =
        { Grammar: Set<string> -> string -> Result<string * string * string, string>
          Splice: byte array -> byte array -> byte array }

    type PairVerdict =
        { PrefixPreserved: bool
          HeaderPreserved: bool
          RecomputedCommitment: bool
          PredecessorRows: int
          AppendedRows: int
          TotalRows: int }

    // ---- strict closed JSON reading (no value from the input is ever echoed) ----

    let private strictUtf8 = UTF8Encoding(false, true)
    let private inputBound = 1048576

    let private malformed () : 'a = raise (FormatException "input")

    let rec private uniqueKeys (value: JsonElement) =
        match value.ValueKind with
        | JsonValueKind.Object ->
            let names = Collections.Generic.HashSet<string>(StringComparer.Ordinal)
            for entry in value.EnumerateObject() do
                if not (names.Add entry.Name) then malformed ()
                uniqueKeys entry.Value
        | JsonValueKind.Array -> for entry in value.EnumerateArray() do uniqueKeys entry
        | _ -> ()

    let private parseStrict (json: string) : JsonDocument =
        if isNull json || json.Length = 0 || json.[0] = '﻿' then malformed ()
        let bytes = strictUtf8.GetBytes json
        if bytes.Length = 0 || bytes.Length > inputBound then malformed ()
        let document = JsonDocument.Parse(ReadOnlyMemory<byte>(bytes), JsonDocumentOptions(MaxDepth = 16))
        try
            uniqueKeys document.RootElement
            document
        with _ ->
            document.Dispose()
            reraise ()

    let private closed (names: string list) (value: JsonElement) =
        if value.ValueKind <> JsonValueKind.Object then malformed ()
        let actual = value.EnumerateObject() |> Seq.map _.Name |> Seq.toList
        if actual.Length <> names.Length || Set.ofList actual <> Set.ofList names then malformed ()

    let private text (name: string) (value: JsonElement) =
        let property = value.GetProperty name
        if property.ValueKind <> JsonValueKind.String then malformed ()
        property.GetString()

    let private flag (name: string) (value: JsonElement) =
        match (value.GetProperty name).ValueKind with
        | JsonValueKind.True -> true
        | JsonValueKind.False -> false
        | _ -> malformed ()

    let private array (name: string) (value: JsonElement) =
        let property = value.GetProperty name
        if property.ValueKind <> JsonValueKind.Array then malformed ()
        property.EnumerateArray() |> Seq.toList

    let private matches (pattern: string) (value: string) =
        not (isNull value) && Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant)

    let private cohortPattern = @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z"
    let private shaPattern = @"\A[0-9a-f]{40}\z"
    let private repositoryPattern = @"\A[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}\z"
    let private issuePattern = @"\A[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}#[1-9][0-9]{0,9}\z"
    let private slicePattern = @"\A[a-z0-9][a-z0-9._-]{0,79}\z"
    let private nodePattern = @"\A[A-Za-z0-9_-]{1,200}\z"
    let private docPathPattern = @"\A[A-Za-z0-9._/-]{1,200}\z"
    let private roadmapPattern = @"\Ahttps://github\.com/[A-Za-z0-9_./-]{1,400}\z"
    let private maxIssues = 1000

    let decodeRequest (json: string) : Result<Request, Refusal> =
        try
            use document = parseStrict json
            let root = document.RootElement
            closed [ "schema"; "cohort"; "predecessor"; "issues" ] root
            if text "schema" root <> "fsgg.board-v2-successor-request/1" then malformed ()
            let cohort = text "cohort" root
            if not (matches cohortPattern cohort) then malformed ()
            let predecessor = root.GetProperty "predecessor"
            closed [ "privatePath"; "mergeSha" ] predecessor
            let path = text "privatePath" predecessor
            let sha = text "mergeSha" predecessor
            if String.IsNullOrWhiteSpace path || path.Length > 1024 || path |> Seq.exists Char.IsControl || not (matches shaPattern sha) then malformed ()
            let issues =
                array "issues" root |> List.map (fun row ->
                    closed [ "ref"; "slice"; "plan"; "classification" ] row
                    let reference = text "ref" row
                    let slice = text "slice" row
                    if not (matches issuePattern reference) || not (matches slicePattern slice) then malformed ()
                    let plan = row.GetProperty "plan"
                    let spec =
                        match text "kind" plan with
                        | "programmeIssue3008" ->
                            closed [ "kind" ] plan
                            ProgrammeIssue3008
                        | "protectedDoc" ->
                            closed [ "kind"; "repo"; "path" ] plan
                            let repository = text "repo" plan
                            let docPath = text "path" plan
                            if not (matches repositoryPattern repository) || not (matches docPathPattern docPath) then malformed ()
                            ProtectedDoc(repository, docPath)
                        | _ -> malformed ()
                    let classification =
                        match text "classification" row with
                        | "Outcome" -> Outcome
                        | "Delivery" -> Delivery
                        | _ -> malformed ()
                    { Ref = reference; Slice = slice; Plan = spec; Classification = classification })
            if issues.IsEmpty || issues.Length > maxIssues then malformed ()
            Ok { Cohort = cohort; PredecessorPrivatePath = path; PredecessorMergeSha = sha; Issues = issues }
        with _ -> Error MalformedInput

    let decodeNativeFacts (json: string) : Result<NativeFacts, Refusal> =
        try
            use document = parseStrict json
            let root = document.RootElement
            closed [ "schema"; "projectId"; "issues"; "planBlobs" ] root
            if text "schema" root <> "fsgg.board-v2-native-facts/1" then malformed ()
            let projectId = text "projectId" root
            if not (matches @"\APVT_[A-Za-z0-9_-]{1,200}\z" projectId) then malformed ()
            let issues =
                array "issues" root |> List.map (fun row ->
                    closed [ "ref"; "nodeId"; "kind"; "repository"; "projectMember"; "visibility"; "humanRoadmap" ] row
                    let reference = text "ref" row
                    let node = text "nodeId" row
                    let repository = text "repository" row
                    if not (matches issuePattern reference) || not (matches nodePattern node) || not (matches repositoryPattern repository) then malformed ()
                    let kind =
                        match text "kind" row with
                        | "Issue" -> NativeIssue
                        | "PullRequest" -> NativePullRequest
                        | _ -> malformed ()
                    let complete =
                        match text "visibility" row with
                        | "complete" -> true
                        | "partial" -> false
                        | _ -> malformed ()
                    let human =
                        let value = row.GetProperty "humanRoadmap"
                        match value.ValueKind with
                        | JsonValueKind.Null -> None
                        | JsonValueKind.String when value.GetString().Length <= 4096 -> Some(value.GetString())
                        | _ -> malformed ()
                    { Ref = reference; NodeId = node; Kind = kind; Repository = repository
                      ProjectMember = flag "projectMember" row; Complete = complete; HumanRoadmap = human })
            let blobs =
                array "planBlobs" root |> List.map (fun row ->
                    closed [ "repository"; "path"; "onMain" ] row
                    let repository = text "repository" row
                    let path = text "path" row
                    if not (matches repositoryPattern repository) || not (matches docPathPattern path) then malformed ()
                    ({ Repository = repository; Path = path; OnMain = flag "onMain" row }: PlanBlobFact))
            if issues.Length > maxIssues || blobs.Length > maxIssues then malformed ()
            Ok { ProjectId = projectId; Issues = issues; PlanBlobs = blobs }
        with _ -> Error MalformedInput

    // ---- raw-byte splice ----

    let private closing = [| byte ']'; byte '}' |]

    let private endsWithClosing (payload: byte array) =
        payload.Length > 2 && payload.[payload.Length - 2] = closing.[0] && payload.[payload.Length - 1] = closing.[1]

    // predecessor[0 .. n-2) ++ "," ++ rows ++ "]}"; the prefix is never re-serialized.
    let private rawSplice (predecessor: byte array) (rows: byte array) : byte array =
        Array.concat [ predecessor.[0 .. predecessor.Length - 3]; [| byte ',' |]; rows; closing ]

    let defaultSeams: Seams =
        { Grammar = V2ProjectionSource.programmePlanBlobGrammar
          Splice = rawSplice }

    // The predecessor payload must be splice-able: it ends with `]}` and `items` is its last
    // top-level member, so everything before the final `]` is the header and prior rows.
    let private spliceable (payload: byte array) =
        try
            if not (endsWithClosing payload) then false
            else
                use document = JsonDocument.Parse(ReadOnlyMemory<byte>(payload))
                let members = document.RootElement.EnumerateObject() |> Seq.toList
                not members.IsEmpty && members.[members.Length - 1].Name = "items"
                && members.[members.Length - 1].Value.ValueKind = JsonValueKind.Array
                && members.[members.Length - 1].Value.GetArrayLength() > 0
        with _ -> false

    let private payloadOf (binding: V2Projection.Binding) =
        match binding.ProgrammeEnrollment with
        | Some enrollment -> Convert.FromBase64String enrollment.PayloadBase64
        | None -> malformed ()

    let private startsWithPrefix (successor: byte array) (predecessor: byte array) =
        endsWithClosing predecessor
        && successor.Length > predecessor.Length
        && successor.AsSpan(0, predecessor.Length - 2).SequenceEqual(predecessor.AsSpan(0, predecessor.Length - 2))
        && successor.[predecessor.Length - 2] = byte ','
        && endsWithClosing successor

    let private rowFragment (rows: PlannedRow list) =
        // Every emitted character is validated against a closed ASCII pattern before this point,
        // so no JSON escaping is required and none is performed.
        rows
        |> List.map (fun row -> "{\"issue\":\"" + row.Issue + "\",\"nodeId\":\"" + row.NodeId + "\",\"roadmap\":\"" + row.Roadmap + "\"}")
        |> String.concat ","
        |> strictUtf8.GetBytes

    // ---- planner ----

    type private ResultBuilder() =
        member _.Bind(value: Result<'a, Refusal>, body: 'a -> Result<'b, Refusal>) = Result.bind body value
        member _.Return(value: 'a) : Result<'a, Refusal> = Ok value
        member _.ReturnFrom(value: Result<'a, Refusal>) = value

    let private result = ResultBuilder()

    let private ensure (condition: bool) (refusal: Refusal) : Result<unit, Refusal> =
        if condition then Ok() else Error refusal

    let private repositoryOf (reference: string) = reference.Substring(0, reference.IndexOf '#')

    let private lower (value: string) = value.ToLowerInvariant()

    let planWith
        (seams: Seams)
        (predecessorPrivateJson: string)
        (predecessorPublicJson: string)
        (request: Request)
        (facts: NativeFacts)
        : Result<SuccessorPlan, Refusal> =
        try
            result {
                // 1. Authenticate the predecessor pair through the installed authenticator.
                let! binding =
                    match V2Projection.decodeBinding predecessorPrivateJson with
                    | Ok binding when V2Projection.isProgrammeBinding binding && binding.ProgrammeEnrollment.IsSome -> Ok binding
                    | _ -> Error PredecessorCommitmentMismatch
                let! predecessorRows =
                    V2Projection.authenticateProgrammeEnrollment binding predecessorPublicJson
                    |> Result.mapError (fun _ -> PredecessorCommitmentMismatch)
                let predecessorPayload = payloadOf binding
                let issues = request.Issues
                // 2. Request shape against the binding, then the plan grammar, before any construction.
                let refs = issues |> List.map (fun issue -> lower issue.Ref)
                do! ensure ((Set.ofList refs).Count = refs.Length) DuplicateIssue
                do! ensure (issues |> List.forall (fun issue -> binding.Repositories.Contains(repositoryOf issue.Ref))) RepositoryOutsideBinding
                let roadmapOf (issue: RequestIssue) : Result<string, Refusal> =
                    match issue.Plan with
                    | ProgrammeIssue3008 ->
                        if binding.Repositories.Contains "FS-GG/.github" then Ok V2ProjectionSource.programmePlanIssueUrl
                        else Error PlanNotAdmitted
                    | ProtectedDoc(repository, path) ->
                        let url = "https://github.com/" + repository + "/blob/main/" + path
                        match seams.Grammar binding.Repositories url with
                        | Ok(admittedRepository, "main", admittedPath)
                            when admittedRepository = repository && admittedPath = path && matches roadmapPattern url -> Ok url
                        | _ -> Error PlanNotAdmitted
                let! roadmaps =
                    issues |> List.map roadmapOf
                    |> List.fold (fun accumulated next ->
                        match accumulated, next with
                        | Error refusal, _ -> Error refusal
                        | Ok _, Error refusal -> Error refusal
                        | Ok values, Ok url -> Ok(values @ [ url ])) (Ok [])
                // 3. Native facts: visibility, kind, identity, enrollment, membership.
                let factFor (issue: RequestIssue) =
                    facts.Issues |> List.tryFind (fun fact -> String.Equals(fact.Ref, issue.Ref, StringComparison.OrdinalIgnoreCase))
                let found = issues |> List.map factFor
                do! ensure (found |> List.forall (fun fact -> fact.IsSome && fact.Value.Complete)) PartialVisibility
                let paired = List.zip3 roadmaps issues (found |> List.map Option.get)
                let first (check: string * RequestIssue * NativeIssueFact -> Refusal option) =
                    match paired |> List.tryPick check with
                    | Some refusal -> Error refusal
                    | None -> Ok()
                do! first (fun (_, _, fact) -> if fact.Kind = NativePullRequest then Some IsPullRequest else None)
                let nodes = paired |> List.map (fun (_, _, fact) -> fact.NodeId)
                let enrolledByNode = predecessorRows |> List.map (fun (issue, node, _) -> node, issue) |> Map.ofList
                let enrolledByIssue = predecessorRows |> List.map (fun (issue, node, _) -> lower issue, node) |> Map.ofList
                do! ensure ((Set.ofList nodes).Count = nodes.Length) NodeIdMismatch
                do! first (fun (_, issue, fact) ->
                        let consistentWithPredecessor =
                            match Map.tryFind fact.NodeId enrolledByNode, Map.tryFind (lower issue.Ref) enrolledByIssue with
                            | None, None -> true
                            | Some enrolledIssue, Some enrolledNode -> enrolledNode = fact.NodeId && lower enrolledIssue = lower issue.Ref
                            | _ -> false
                        if not (fact.NodeId.StartsWith("I_", StringComparison.Ordinal))
                           || lower fact.Repository <> lower (repositoryOf issue.Ref)
                           || not consistentWithPredecessor then Some NodeIdMismatch
                        else None)
                do! first (fun (_, issue, fact) ->
                        if Map.containsKey fact.NodeId enrolledByNode || Map.containsKey (lower issue.Ref) enrolledByIssue then Some AlreadyEnrolled
                        else None)
                do! ensure (facts.ProjectId = binding.ProjectId) NotProjectMember
                do! first (fun (_, _, fact) -> if not fact.ProjectMember then Some NotProjectMember else None)
                // 4. Offline plan-existence fact for protected documents.
                do! ensure
                        (issues |> List.forall (fun issue ->
                            match issue.Plan with
                            | ProgrammeIssue3008 -> true
                            | ProtectedDoc(repository, path) ->
                                facts.PlanBlobs |> List.exists (fun blob -> blob.Repository = repository && blob.Path = path && blob.OnMain)))
                        PlanBlobAbsentOnMain
                // 5. Never overwrite a differing human Roadmap value.
                do! first (fun (roadmap, _, fact) ->
                        match fact.HumanRoadmap with
                        | Some value when value <> roadmap -> Some ConflictingHumanValue
                        | _ -> None)
                // 6. The installed row ceiling and the splice precondition.
                do! ensure (predecessorRows.Length + issues.Length <= rowCap) RowCapExceeded
                do! ensure (spliceable predecessorPayload) PrefixNotPreserved
                // 7. Construction: raw-byte splice, prefix verification, then the installed payload validator.
                let appended =
                    paired |> List.map (fun (roadmap, issue, fact) ->
                        { Issue = issue.Ref; NodeId = fact.NodeId; Roadmap = roadmap; Slice = issue.Slice; Classification = issue.Classification })
                let successor = seams.Splice predecessorPayload (rowFragment appended)
                do! ensure (startsWithPrefix successor predecessorPayload) PrefixNotPreserved
                let template = JsonNode.Parse predecessorPrivateJson
                template.AsObject().Remove "programmeEnrollment" |> ignore
                template.["populationRevision"] <- JsonValue.Create request.PredecessorMergeSha
                let selected = template.["selectedIssues"].AsObject()
                appended |> List.iter (fun row -> selected.Add(row.NodeId, JsonValue.Create row.Issue))
                let templateJson = template.ToJsonString()
                // The installed constructor validates the template and payload; a fixed placeholder
                // nonce is used, the result is discarded and the placeholder is never emitted.
                do! V2Projection.prepareProgrammeBinding templateJson successor (fun () -> Array.zeroCreate 32)
                    |> Result.map ignore
                    |> Result.mapError (fun _ -> PrefixNotPreserved)
                return { TemplateJson = templateJson; Payload = successor; Appended = appended
                         PredecessorRows = predecessorRows.Length; TotalRows = predecessorRows.Length + appended.Length }
            }
        with _ -> Error MalformedInput

    let plan predecessorPrivateJson predecessorPublicJson request facts =
        planWith defaultSeams predecessorPrivateJson predecessorPublicJson request facts

    // ---- independent pair verification ----

    let private noVerdict =
        { PrefixPreserved = false; HeaderPreserved = false; RecomputedCommitment = false
          PredecessorRows = 0; AppendedRows = 0; TotalRows = 0 }

    let private indexOf (needle: byte array) (haystack: byte array) =
        let index = haystack.AsSpan().IndexOf(ReadOnlySpan<byte>(needle))
        index

    let verifyPair (predecessorPrivateJson: string) (successorPrivateJson: string) (successorPublicJson: string) : PairVerdict =
        try
            match V2Projection.decodeBinding predecessorPrivateJson, V2Projection.decodeBinding successorPrivateJson with
            | Ok predecessor, Ok successor ->
                let before = payloadOf predecessor
                let after = payloadOf successor
                let prefix = startsWithPrefix after before
                // The header is every byte up to and including `"items":[`.
                let marker = strictUtf8.GetBytes "\"items\":["
                let at = indexOf marker before
                let headerBytes = at >= 0 && indexOf marker after = at && after.AsSpan(0, at + marker.Length).SequenceEqual(before.AsSpan(0, at + marker.Length))
                let strip (json: string) =
                    let node = JsonNode.Parse json
                    let selected = node.["selectedIssues"].AsObject() |> Seq.map (fun entry -> entry.Key, entry.Value.GetValue<string>()) |> Seq.toList
                    let rest = node.AsObject()
                    for name in [ "programmeEnrollment"; "selectedIssues"; "populationRevision" ] do rest.Remove name |> ignore
                    rest :> JsonNode, selected
                let beforeRest, beforeSelected = strip predecessorPrivateJson
                let afterRest, afterSelected = strip successorPrivateJson
                let orderPreserved =
                    afterSelected.Length >= beforeSelected.Length && (afterSelected |> List.take beforeSelected.Length) = beforeSelected
                let header = headerBytes && JsonNode.DeepEquals(beforeRest, afterRest) && orderPreserved
                let rows =
                    match V2Projection.authenticateProgrammeEnrollment successor successorPublicJson with
                    | Ok rows -> Some rows
                    | Error _ -> None
                let predecessorRows = match V2Projection.programmeRows predecessor with Ok rows -> rows.Length | Error _ -> 0
                let total = match V2Projection.programmeRows successor with Ok rows -> rows.Length | Error _ -> 0
                { PrefixPreserved = prefix
                  HeaderPreserved = header
                  RecomputedCommitment = rows.IsSome
                  PredecessorRows = predecessorRows
                  AppendedRows = max 0 (total - predecessorRows)
                  TotalRows = total }
            | _ -> noVerdict
        with _ -> noVerdict
