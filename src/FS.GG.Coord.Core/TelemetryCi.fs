namespace FS.GG.Coord

open System
open System.Text.Json
open System.Text.RegularExpressions

module TelemetryCi =
    [<Literal>]
    let AssignmentSchema = "fsgg.telemetry.ci-assignment/1"

    type Assignment =
        {
            FeatureId: string
            ItemId: string
            AttemptId: string
            ParentAttemptId: string option
            ProducerStream: string
        }

    /// Operator evidence plus exact delivery truth; never an observation or token record.
    type CorrectionRequest =
        {
            CorrectionId: string
            ExpectedPredecessor: string option
            Repository: string
            PullRequest: int64
            BaseRef: string
            BaseSha: string
            Head: string
            MergeCommit: string
            Prior: Assignment
            Effective: Assignment
            EvidenceSha256: string
            Reason: string
            OperatorSource: string
            ObservedAt: string
        }

    type CorrectionTarget =
        {
            Table: string
            Identity: string
            Revision: int64
            Digest: string
        }

    type CorrectionPlan =
        {
            StoreId: string
            Request: CorrectionRequest
            Targets: CorrectionTarget list
        }

    type Interval =
        {
            StartUtc: DateTimeOffset
            EndUtc: DateTimeOffset
        }

    type Timing =
        {
            RunnerSeconds: int64 option
            WallSeconds: int64 option
            AdministrativeSeconds: int64 option
            CriticalPathSeconds: int64 option
        }

    let private safe value =
        not (String.IsNullOrWhiteSpace value)
        && Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")

    let parseAssignment (bytes: byte array) =
        try
            use document = JsonDocument.Parse bytes
            let root = document.RootElement

            let allowed =
                Set
                    [
                        "schema"
                        "featureId"
                        "itemId"
                        "attemptId"
                        "parentAttemptId"
                        "producerStream"
                    ]

            let unknown =
                root.EnumerateObject()
                |> Seq.map _.Name
                |> Seq.filter (allowed.Contains >> not)
                |> Seq.toList

            let text (name: string) =
                match root.TryGetProperty name with
                | true, value when value.ValueKind = JsonValueKind.String && safe (value.GetString()) ->
                    Some(value.GetString())
                | _ -> None

            let schema =
                match root.TryGetProperty "schema" with
                | true, value when value.ValueKind = JsonValueKind.String && value.GetString() = AssignmentSchema ->
                    Some AssignmentSchema
                | _ -> None

            let parent =
                match root.TryGetProperty "parentAttemptId" with
                | false, _ -> Some None
                | true, value when value.ValueKind = JsonValueKind.Null -> Some None
                | true, value when value.ValueKind = JsonValueKind.String && safe (value.GetString()) ->
                    Some(Some(value.GetString()))
                | _ -> None

            match schema, text "featureId", text "itemId", text "attemptId", parent, text "producerStream" with
            | Some _, Some feature, Some item, Some attempt, Some parentAttempt, Some producer when unknown.IsEmpty ->
                Ok
                    {
                        FeatureId = feature
                        ItemId = item
                        AttemptId = attempt
                        ParentAttemptId = parentAttempt
                        ProducerStream = producer
                    }
            | _ when not unknown.IsEmpty -> Error [ "assignment contains unknown fields: " + String.concat "," unknown ]
            | _ -> Error [ "assignment must be closed, schema-current, and contain safe identifiers" ]
        with :? JsonException as error ->
            Error [ "invalid assignment JSON: " + error.Message ]

    let private closed (fields: string list) (value: JsonElement) =
        if value.ValueKind <> JsonValueKind.Object then invalidOp "correction object required"
        let names = value.EnumerateObject() |> Seq.map _.Name |> Seq.toList
        if names.Length <> (Set.ofList names).Count || Set.ofList names <> Set.ofList fields then
            invalidOp "correction object has missing, duplicate or unknown fields"

    let private requiredText (name: string) (value: JsonElement) =
        let entry = value.GetProperty name
        if entry.ValueKind <> JsonValueKind.String then invalidOp ("correction string required: " + name)
        entry.GetString()

    let private boundedText name maximum value =
        let text = requiredText name value
        if String.IsNullOrWhiteSpace text || text.Length > maximum || text |> Seq.exists Char.IsControl then
            invalidOp ("invalid correction text: " + name)
        text

    let private digestText name length value =
        let text = requiredText name value
        if not (Regex.IsMatch(text, "^[0-9a-f]{" + string length + "}$")) then
            invalidOp ("invalid correction digest: " + name)
        text

    let private correctionRequestElement (root: JsonElement) =
        closed [ "schema"; "correctionId"; "expectedPredecessor"; "repository"; "pullRequest";
                 "baseRef"; "baseSha"; "head"; "mergeCommit"; "prior"; "effective";
                 "evidenceSha256"; "reason"; "operatorSource"; "observedAt" ] root
        if requiredText "schema" root <> "fsgg.telemetry.ci-correction-request/1" then
            invalidOp "unsupported correction request schema"
        let identifier name =
            let text = requiredText name root
            if not (safe text) then invalidOp ("unsafe correction identifier: " + name)
            text
        let assignment (name: string) =
            let element = root.GetProperty name
            closed [ "schema"; "featureId"; "itemId"; "attemptId"; "parentAttemptId"; "producerStream" ] element
            match parseAssignment (System.Text.Encoding.UTF8.GetBytes(element.GetRawText())) with
            | Ok value -> value
            | Error errors -> invalidOp (String.concat "; " errors)
        let predecessor = root.GetProperty "expectedPredecessor"
        let repository = boundedText "repository" 256 root
        if not (Regex.IsMatch(repository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) then
            invalidOp "invalid correction repository"
        let pr = root.GetProperty("pullRequest").GetInt64()
        if pr <= 0L then invalidOp "invalid correction pull request"
        let observed = boundedText "observedAt" 64 root
        match DateTimeOffset.TryParse observed with
        | true, value when value.Offset = TimeSpan.Zero -> ()
        | _ -> invalidOp "correction observedAt must be UTC"
        let prior, effective = assignment "prior", assignment "effective"
        if prior = effective then invalidOp "correction must change assignment"
        { CorrectionId = identifier "correctionId"
          ExpectedPredecessor = if predecessor.ValueKind = JsonValueKind.Null then None else Some(identifier "expectedPredecessor")
          Repository = repository; PullRequest = pr; BaseRef = boundedText "baseRef" 256 root
          BaseSha = digestText "baseSha" 40 root; Head = digestText "head" 40 root
          MergeCommit = digestText "mergeCommit" 40 root
          Prior = prior; Effective = effective; EvidenceSha256 = digestText "evidenceSha256" 64 root
          Reason = boundedText "reason" 1024 root; OperatorSource = boundedText "operatorSource" 1024 root
          ObservedAt = observed }

    let private parseCorrection parser (bytes: byte array) =
        try
            if bytes.Length > 1048576 then invalidOp "correction exceeds 1 MiB"
            use document = JsonDocument.Parse(bytes, JsonDocumentOptions(MaxDepth = 16))
            Ok(parser document.RootElement)
        with error -> Error [ error.Message ]

    let parseCorrectionRequest bytes = parseCorrection correctionRequestElement bytes

    let parseCorrectionPlan bytes =
        parseCorrection (fun root ->
            closed [ "schema"; "storeId"; "request"; "targets" ] root
            if requiredText "schema" root <> "fsgg.telemetry.ci-correction-plan/1" then
                invalidOp "unsupported correction plan schema"
            let store = requiredText "storeId" root
            if not (Regex.IsMatch(store, "^[0-9a-f]{32}$")) then invalidOp "invalid correction store identity"
            let elements = root.GetProperty "targets"
            if elements.ValueKind <> JsonValueKind.Array || elements.GetArrayLength() < 1 || elements.GetArrayLength() > 4096 then
                invalidOp "correction target population must be 1–4096"
            let tables = set [ "native_item_outcomes"; "ci_bindings"; "ci_population_admissions";
                               "ci_pages"; "ci_runs"; "ci_jobs"; "ci_steps"; "ci_check_runs";
                               "ci_coverage"; "ci_population_coverage" ]
            let targets =
                [ for element in elements.EnumerateArray() do
                    closed [ "table"; "identity"; "revision"; "digest" ] element
                    let table = requiredText "table" element
                    let identity = boundedText "identity" 256 element
                    let revision = element.GetProperty("revision").GetInt64()
                    if not (tables.Contains table) || not (safe identity) || revision < 0L then
                        invalidOp "invalid correction target"
                    yield { Table = table; Identity = identity; Revision = revision; Digest = digestText "digest" 64 element } ]
            if targets |> List.map _.Identity |> Set.ofList |> Set.count <> targets.Length then
                invalidOp "duplicate correction target"
            { StoreId = store; Request = correctionRequestElement (root.GetProperty "request"); Targets = targets }) bytes

    let correctionPlanJson (plan: CorrectionPlan) =
        let assignment (value: Assignment) =
            {| schema = AssignmentSchema; featureId = value.FeatureId; itemId = value.ItemId
               attemptId = value.AttemptId; parentAttemptId = value.ParentAttemptId; producerStream = value.ProducerStream |}
        let value = plan.Request
        JsonSerializer.Serialize
            {| schema = "fsgg.telemetry.ci-correction-plan/1"; storeId = plan.StoreId
               request = {| schema = "fsgg.telemetry.ci-correction-request/1"; correctionId = value.CorrectionId
                            expectedPredecessor = value.ExpectedPredecessor; repository = value.Repository; pullRequest = value.PullRequest
                            baseRef = value.BaseRef; baseSha = value.BaseSha; head = value.Head; mergeCommit = value.MergeCommit
                            prior = assignment value.Prior; effective = assignment value.Effective
                            evidenceSha256 = value.EvidenceSha256; reason = value.Reason; operatorSource = value.OperatorSource
                            observedAt = value.ObservedAt |}
               targets = plan.Targets |> List.map (fun target -> {| table = target.Table; identity = target.Identity; revision = target.Revision; digest = target.Digest |}) |}

    let interval (startUtc: string option) (endUtc: string option) =
        match startUtc, endUtc with
        | Some startText, Some endText ->
            match DateTimeOffset.TryParse startText, DateTimeOffset.TryParse endText with
            | (true, startAt), (true, endAt) when endAt >= startAt -> Some { StartUtc = startAt; EndUtc = endAt }
            | _ -> None
        | _ -> None

    let private merged intervals =
        intervals
        |> List.sortBy _.StartUtc
        |> List.fold
            (fun acc next ->
                match acc with
                | current :: tail when next.StartUtc <= current.EndUtc ->
                    { current with
                        EndUtc = max current.EndUtc next.EndUtc
                    }
                    :: tail
                | _ -> next :: acc)
            []
        |> List.rev

    let unionSeconds intervals =
        if List.isEmpty intervals then
            None
        else
            merged intervals
            |> List.sumBy (fun value -> int64 (value.EndUtc - value.StartUtc).TotalSeconds)
            |> Some

    let subtractSeconds source excluded =
        if List.isEmpty source then
            None
        else
            let sourceSeconds = unionSeconds source |> Option.defaultValue 0L

            let overlaps =
                [
                    for a in merged source do
                        for b in merged excluded do
                            let startAt = max a.StartUtc b.StartUtc
                            let endAt = min a.EndUtc b.EndUtc

                            if endAt > startAt then
                                yield { StartUtc = startAt; EndUtc = endAt }
                ]

            Some(sourceSeconds - (unionSeconds overlaps |> Option.defaultValue 0L))
