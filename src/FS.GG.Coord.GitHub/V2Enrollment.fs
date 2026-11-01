namespace FS.GG.Coord.GitHub

module V2Enrollment =

    open System
    open System.Text
    open System.Text.Json
    open System.Text.Json.Nodes
    open System.Text.RegularExpressions

    let rowCap = V2Projection.maximumProgrammeRows
    let legacyRowCap = V2Projection.legacyProgrammeRowCap
    let previousRowCap = V2Projection.previousProgrammeRowCap

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
        | InputUnreadable
        | CustodyUnavailable
        | GitReadFailed
        | NativeReadFailed
        | SuccessorNotRederived
        | ReaderNotVerified
        | ReaderAlreadyBound

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
        | InputUnreadable -> "InputUnreadable"
        | CustodyUnavailable -> "CustodyUnavailable"
        | GitReadFailed -> "GitReadFailed"
        | NativeReadFailed -> "NativeReadFailed"
        | SuccessorNotRederived -> "SuccessorNotRederived"
        | ReaderNotVerified -> "ReaderNotVerified"
        | ReaderAlreadyBound -> "ReaderAlreadyBound"

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

    [<StructuredFormatDisplay("{Display}")>]
    type SuccessorPlan =
        { TemplateJson: string
          Payload: byte array
          Appended: PlannedRow list
          PredecessorRows: int
          TotalRows: int }
        // Generic printing (%A, ToString) must never reach the private template or the payload bytes.
        member this.Display = "SuccessorPlan(rows=" + string this.TotalRows + "; appended=" + string this.Appended.Length + "; content withheld)"
        override this.ToString() = this.Display

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

    // Every value is JSON-escaped here, whatever validation preceded it: the splice is raw bytes into a
    // document the installed reader parses, so a quote or backslash must never be able to end a string.
    let private jsonString (value: string) = "\"" + JsonEncodedText.Encode(value).ToString() + "\""

    let private rowFragment (rows: PlannedRow list) =
        rows
        |> List.map (fun row -> "{\"issue\":" + jsonString row.Issue + ",\"nodeId\":" + jsonString row.NodeId + ",\"roadmap\":" + jsonString row.Roadmap + "}")
        |> String.concat ","
        |> strictUtf8.GetBytes

    // ---- planner ----

    type Finding = { Refusal: Refusal; Issue: string option }

    type Analysis =
        { PredecessorRows: int
          PredecessorPayloadBytes: int
          Findings: Finding list }

    let private repositoryOf (reference: string) = reference.Substring(0, reference.IndexOf '#')

    let private lower (value: string) = value.ToLowerInvariant()

    let private authenticate (predecessorPrivateJson: string) (predecessorPublicJson: string) =
        match V2Projection.decodeBinding predecessorPrivateJson with
        | Ok binding when V2Projection.isProgrammeBinding binding && binding.ProgrammeEnrollment.IsSome ->
            match V2Projection.authenticateProgrammeEnrollment binding predecessorPublicJson with
            | Ok rows -> Ok(binding, rows)
            | Error _ -> Error PredecessorCommitmentMismatch
        | _ -> Error PredecessorCommitmentMismatch

    let private roadmapFor (seams: Seams) (binding: V2Projection.Binding) (issue: RequestIssue) : string option =
        match issue.Plan with
        | ProgrammeIssue3008 ->
            if binding.Repositories.Contains "FS-GG/.github" then Some V2ProjectionSource.programmePlanIssueUrl else None
        | ProtectedDoc(repository, path) ->
            let url = "https://github.com/" + repository + "/blob/main/" + path
            match seams.Grammar binding.Repositories url with
            | Ok(admittedRepository, "main", admittedPath)
                when admittedRepository = repository && admittedPath = path && matches roadmapPattern url -> Some url
            | _ -> None

    // Every independent per-row and request-wide finding, in the planner's own check order: check-major,
    // then row order. The planner refuses with the first; the preflight reports all. One implementation.
    let private examine
        (seams: Seams)
        (binding: V2Projection.Binding)
        (predecessorRows: (string * string * string) list)
        (predecessorPayload: byte array)
        (request: Request)
        (facts: NativeFacts)
        : Finding list * (string * RequestIssue * NativeIssueFact) list option =
        let findings = ResizeArray<Finding>()
        let add (refusal: Refusal) (issue: string option) = findings.Add { Refusal = refusal; Issue = issue }
        let issues = request.Issues
        // 1. Request shape against the binding, then the plan grammar, before any construction.
        let seen = Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        for issue in issues do
            if not (seen.Add(lower issue.Ref)) then add DuplicateIssue (Some issue.Ref)
        for issue in issues do
            if not (binding.Repositories.Contains(repositoryOf issue.Ref)) then add RepositoryOutsideBinding (Some issue.Ref)
        let roadmaps = issues |> List.map (roadmapFor seams binding)
        List.zip issues roadmaps |> List.iter (fun (issue, roadmap) -> if roadmap.IsNone then add PlanNotAdmitted (Some issue.Ref))
        // 2. Native facts: visibility, kind, identity, enrollment, membership.
        let factFor (issue: RequestIssue) =
            facts.Issues |> List.tryFind (fun fact -> String.Equals(fact.Ref, issue.Ref, StringComparison.OrdinalIgnoreCase))
        let found = issues |> List.map factFor
        List.zip issues found |> List.iter (fun (issue, fact) ->
            match fact with
            | Some value when value.Complete -> ()
            | _ -> add PartialVisibility (Some issue.Ref))
        let rows =
            List.zip3 roadmaps issues found
            |> List.choose (fun (roadmap, issue, fact) ->
                match fact with
                | Some value when value.Complete -> Some(roadmap, issue, value)
                | _ -> None)
        for _, issue, fact in rows do
            if fact.Kind = NativePullRequest then add IsPullRequest (Some issue.Ref)
        let enrolledByNode = predecessorRows |> List.map (fun (issue, node, _) -> node, issue) |> Map.ofList
        let enrolledByIssue = predecessorRows |> List.map (fun (issue, node, _) -> lower issue, node) |> Map.ofList
        let nodeCounts = rows |> List.countBy (fun (_, _, fact) -> fact.NodeId) |> Map.ofList
        for _, issue, fact in rows do
            let consistentWithPredecessor =
                match Map.tryFind fact.NodeId enrolledByNode, Map.tryFind (lower issue.Ref) enrolledByIssue with
                | None, None -> true
                | Some enrolledIssue, Some enrolledNode -> enrolledNode = fact.NodeId && lower enrolledIssue = lower issue.Ref
                | _ -> false
            if nodeCounts.[fact.NodeId] > 1
               || not (fact.NodeId.StartsWith("I_", StringComparison.Ordinal))
               || lower fact.Repository <> lower (repositoryOf issue.Ref)
               || not consistentWithPredecessor then add NodeIdMismatch (Some issue.Ref)
        for _, issue, fact in rows do
            if Map.containsKey fact.NodeId enrolledByNode || Map.containsKey (lower issue.Ref) enrolledByIssue then add AlreadyEnrolled (Some issue.Ref)
        if facts.ProjectId <> binding.ProjectId then add NotProjectMember None
        for _, issue, fact in rows do
            if not fact.ProjectMember then add NotProjectMember (Some issue.Ref)
        // 3. Offline plan-existence fact for protected documents.
        for issue in issues do
            match issue.Plan with
            | ProgrammeIssue3008 -> ()
            | ProtectedDoc(repository, path) ->
                if not (facts.PlanBlobs |> List.exists (fun blob -> blob.Repository = repository && blob.Path = path && blob.OnMain)) then
                    add PlanBlobAbsentOnMain (Some issue.Ref)
        // 4. Never overwrite a differing human Roadmap value.
        for roadmap, issue, fact in rows do
            match roadmap, fact.HumanRoadmap with
            | Some expected, Some value when value <> expected -> add ConflictingHumanValue (Some issue.Ref)
            | _ -> ()
        // 5. The installed row ceiling and the splice precondition.
        if predecessorRows.Length + issues.Length > rowCap then add RowCapExceeded None
        if not (spliceable predecessorPayload) then add PrefixNotPreserved None
        let paired =
            if roadmaps |> List.forall Option.isSome && found |> List.forall (fun fact -> fact.IsSome && fact.Value.Complete) then
                Some(List.zip3 roadmaps issues found |> List.map (fun (roadmap, issue, fact) -> roadmap.Value, issue, fact.Value))
            else None
        List.ofSeq findings, paired

    let analyzeWith
        (seams: Seams)
        (predecessorPrivateJson: string)
        (predecessorPublicJson: string)
        (request: Request)
        (facts: NativeFacts)
        : Result<Analysis, Refusal> =
        try
            match authenticate predecessorPrivateJson predecessorPublicJson with
            | Error refusal -> Error refusal
            | Ok(binding, predecessorRows) ->
                let payload = payloadOf binding
                let findings, _ = examine seams binding predecessorRows payload request facts
                Ok { PredecessorRows = predecessorRows.Length; PredecessorPayloadBytes = payload.Length; Findings = findings }
        with _ -> Error MalformedInput

    let analyze predecessorPrivateJson predecessorPublicJson request facts =
        analyzeWith defaultSeams predecessorPrivateJson predecessorPublicJson request facts

    let planWith
        (seams: Seams)
        (predecessorPrivateJson: string)
        (predecessorPublicJson: string)
        (request: Request)
        (facts: NativeFacts)
        : Result<SuccessorPlan, Refusal> =
        try
            match authenticate predecessorPrivateJson predecessorPublicJson with
            | Error refusal -> Error refusal
            | Ok(binding, predecessorRows) ->
                let predecessorPayload = payloadOf binding
                match examine seams binding predecessorRows predecessorPayload request facts with
                | first :: _, _ -> Error first.Refusal
                | [], None -> Error MalformedInput
                | [], Some paired ->
                    // Construction: raw-byte splice, prefix verification, then the installed payload validator.
                    let appended =
                        paired |> List.map (fun (roadmap, issue, fact) ->
                            { Issue = issue.Ref; NodeId = fact.NodeId; Roadmap = roadmap; Slice = issue.Slice; Classification = issue.Classification })
                    let successor = seams.Splice predecessorPayload (rowFragment appended)
                    if not (startsWithPrefix successor predecessorPayload) then Error PrefixNotPreserved
                    else
                        let template = JsonNode.Parse predecessorPrivateJson
                        template.AsObject().Remove "programmeEnrollment" |> ignore
                        template.["populationRevision"] <- JsonValue.Create request.PredecessorMergeSha
                        let selected = template.["selectedIssues"].AsObject()
                        appended |> List.iter (fun row -> selected.Add(row.NodeId, JsonValue.Create row.Issue))
                        let templateJson = template.ToJsonString()
                        // The installed constructor validates the template and payload; a fixed placeholder
                        // nonce is used, the result is discarded and the placeholder is never emitted.
                        match V2Projection.prepareProgrammeBinding templateJson successor (fun () -> Array.zeroCreate 32) with
                        | Error _ -> Error PrefixNotPreserved
                        | Ok _ ->
                            Ok { TemplateJson = templateJson; Payload = successor; Appended = appended
                                 PredecessorRows = predecessorRows.Length; TotalRows = predecessorRows.Length + appended.Length }
        with _ -> Error MalformedInput

    let plan predecessorPrivateJson predecessorPublicJson request facts =
        planWith defaultSeams predecessorPrivateJson predecessorPublicJson request facts

    // ---- re-derivation of supplied successor inputs ----

    let verifyPreparedWith
        (seams: Seams)
        (predecessorPrivateJson: string)
        (predecessorPublicJson: string)
        (request: Request)
        (facts: NativeFacts)
        (template: string)
        (payload: byte array)
        : Result<unit, Refusal> =
        match planWith seams predecessorPrivateJson predecessorPublicJson request facts with
        | Error refusal -> Error refusal
        | Ok planned ->
            try
                let binding =
                    match V2Projection.decodeBinding predecessorPrivateJson with
                    | Ok binding -> binding
                    | Error _ -> malformed ()
                let predecessorPayload = payloadOf binding
                if not (startsWithPrefix payload predecessorPayload) then Error PrefixNotPreserved
                else
                    // The supplied rows are read from the supplied bytes themselves, not from the plan.
                    let suppliedRows =
                        use document = JsonDocument.Parse(ReadOnlyMemory<byte>(payload))
                        document.RootElement.GetProperty("items").EnumerateArray()
                        |> Seq.map (fun row -> text "issue" row, text "nodeId" row, text "roadmap" row)
                        |> Seq.toList
                    let appended = suppliedRows |> List.skip (min planned.PredecessorRows suppliedRows.Length)
                    let admitted (roadmap: string) =
                        (roadmap = V2ProjectionSource.programmePlanIssueUrl && binding.Repositories.Contains "FS-GG/.github")
                        || (match seams.Grammar binding.Repositories roadmap with
                            | Ok(_, "main", _) -> matches roadmapPattern roadmap
                            | _ -> false)
                    let joined (issue: string, node: string) =
                        facts.Issues |> List.exists (fun fact -> String.Equals(fact.Ref, issue, StringComparison.OrdinalIgnoreCase) && fact.NodeId = node)
                    if appended |> List.exists (fun (_, _, roadmap) -> not (admitted roadmap)) then Error PlanNotAdmitted
                    elif appended |> List.exists (fun (issue, node, _) -> not (joined (issue, node))) then Error NodeIdMismatch
                    elif payload <> planned.Payload || template <> planned.TemplateJson then Error SuccessorNotRederived
                    else Ok()
            with _ -> Error SuccessorNotRederived

    let verifyPrepared predecessorPrivateJson predecessorPublicJson request facts template payload =
        verifyPreparedWith defaultSeams predecessorPrivateJson predecessorPublicJson request facts template payload

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
