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

    // Operator evidence plus exact delivery truth; never an observation or token record.
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

    /// Producer-private provider progress. It is not a receiver event or database ingest cursor.
    module ProviderCheckpoint =
        type Identity =
            { Assignment: Assignment
              AssignmentDigest: string
              StoreId: string
              Collection: string
              ApiOrigin: string
              Repository: string
              RepositoryId: int64
              PullRequest: int64
              BaseRef: string
              BaseSha: string
              Head: string
              ProfileDigest: string
              ParserContract: string }

        type ResourceKind =
            | Admission
            | Runs
            | Attempt
            | Jobs
            | Checks
            | Suites
            | SuiteChecks

        type ResourceKey =
            { Kind: ResourceKind
              RunId: int64
              SuiteId: int64
              Attempt: int
              Page: int }

        type Resource =
            { Key: ResourceKey
              Body: string
              BodyDigest: string
              Validator: string
              Next: string
              ProjectionDigest: string
              ObservedAt: DateTimeOffset
              ValidatedAt: DateTimeOffset }

        type Phase =
            | Acquiring
            | FirstSweep
            | SecondSweep
            | FinalBarrier
            | Stable
            | PublicationPending

        type FactReference =
            { Kind: string
              Identity: string
              Revision: int64
              Digest: string }

        type Checkpoint =
            { Identity: Identity
              Generation: int64
              CreatedAt: DateTimeOffset
              ObservedThrough: DateTimeOffset
              PriorCheckpointDigest: string
              CandidateDigest: string
              Phase: Phase
              Cursor: int
              Resources: Resource list
              FirstSweepDigest: string
              SecondSweepDigest: string
              Revision: int64
              PendingBatchDigest: string
              PendingBatchBase64: string
              FactReferences: FactReference list }

        let maximumResources = 4096
        let maximumBodyBytes = 4 * 1024 * 1024
        let maximumAggregateBodyBytes = 64 * 1024 * 1024
        let maximumCheckpointBytes = 96 * 1024 * 1024
        let maximumAge = TimeSpan.FromDays 1.0

        let digest (bytes: byte array) =
            System.Security.Cryptography.SHA256.HashData bytes
            |> Convert.ToHexString
            |> _.ToLowerInvariant()

        let private utf8 = System.Text.UTF8Encoding(false, true)
        let private sha value = not (isNull value) && Regex.IsMatch(value, "^[0-9a-f]{64}$")
        let private optionalSha value = value = "" || sha value

        let identityDigest (identity: Identity) =
            let values: string array =
                [| AssignmentSchema
                   identity.Assignment.FeatureId
                   identity.Assignment.ItemId
                   identity.Assignment.AttemptId
                   (identity.Assignment.ParentAttemptId |> Option.defaultValue "")
                   identity.Assignment.ProducerStream
                   identity.AssignmentDigest
                   identity.StoreId
                   identity.Collection
                   identity.ApiOrigin
                   identity.Repository
                   string identity.RepositoryId
                   string identity.PullRequest
                   identity.BaseRef
                   identity.BaseSha
                   identity.Head
                   identity.ProfileDigest
                   identity.ParserContract |]
            JsonSerializer.Serialize<string array>(values) |> utf8.GetBytes |> digest
        let private commit value = not (isNull value) && Regex.IsMatch(value, "^[0-9a-f]{40}$")
        let private text value = not (String.IsNullOrWhiteSpace value) && value.Length <= 512

        let private kindName = function
            | Admission -> "admission"
            | Runs -> "runs"
            | Attempt -> "attempt"
            | Jobs -> "jobs"
            | Checks -> "checks"
            | Suites -> "suites"
            | SuiteChecks -> "suite-checks"

        let private phaseName = function
            | Acquiring -> "acquiring"
            | FirstSweep -> "first-sweep"
            | SecondSweep -> "second-sweep"
            | FinalBarrier -> "final-barrier"
            | Stable -> "stable"
            | PublicationPending -> "publication-pending"

        let private validKey (key: ResourceKey) =
            key.Page > 0
            && key.Page <= maximumResources
            && match key.Kind with
               | SuiteChecks -> key.RunId = 0L && key.SuiteId > 0L && key.Attempt = 0
               | Admission -> key.SuiteId = 0L && key.RunId = 0L && key.Attempt = 0 && key.Page = 1
               | Runs | Checks | Suites -> key.SuiteId = 0L && key.RunId = 0L && key.Attempt = 0
               | Attempt -> key.SuiteId = 0L && key.RunId > 0L && key.Attempt > 0 && key.Page = 1
               | Jobs -> key.SuiteId = 0L && key.RunId > 0L && key.Attempt > 0

        /// Fingerprint every retained resource, including its exact body and next-page topology.
        /// Observation clocks are excluded so an unchanged re-read can match the prior sweep.
        let populationDigest (resources: Resource list) =
            resources
            |> List.sortBy _.Key
            |> List.map (fun resource ->
                let values: string array =
                    [| kindName resource.Key.Kind
                       string resource.Key.RunId
                       string resource.Key.SuiteId
                       string resource.Key.Attempt
                       string resource.Key.Page
                       resource.BodyDigest
                       Convert.ToBase64String(utf8.GetBytes resource.Next)
                       resource.ProjectionDigest |]
                String.Join("\n", values))
            |> String.concat "\n\n"
            |> utf8.GetBytes
            |> digest

        /// Validate private state before any cache reuse. Provider bodies must additionally be re-parsed
        /// by the provider adapter; a structurally valid checkpoint is never provider authority.
        let private validateState (expected: Identity) (now: DateTimeOffset) (checkpoint: Checkpoint) =
            let errors = ResizeArray<string>()
            let refuse condition code = if not condition then errors.Add code
            let identity = checkpoint.Identity
            refuse (identity = expected) "checkpoint-identity-mismatch"
            refuse (safe identity.Assignment.FeatureId && safe identity.Assignment.ItemId
                    && safe identity.Assignment.AttemptId && safe identity.Assignment.ProducerStream
                    && (identity.Assignment.ParentAttemptId |> Option.forall safe)) "checkpoint-assignment-shape"
            refuse (checkpoint.CandidateDigest = identityDigest identity && optionalSha checkpoint.PriorCheckpointDigest) "checkpoint-candidate-or-prior-digest"
            refuse (sha identity.AssignmentDigest && sha identity.ProfileDigest) "checkpoint-identity-digest"
            refuse (text identity.StoreId && text identity.Collection && text identity.ParserContract) "checkpoint-identity-text"
            refuse (Regex.IsMatch(identity.Repository, "^[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9_.-]+$")
                    && (identity.Repository.Split('/') |> Array.forall (fun part -> part <> "." && part <> ".."))) "checkpoint-repository"
            refuse (identity.RepositoryId >= 0L && identity.PullRequest > 0L && identity.PullRequest <= int64 Int32.MaxValue) "checkpoint-native-identity"
            refuse (text identity.BaseRef && commit identity.BaseSha && commit identity.Head) "checkpoint-head-base"
            let mutable origin = Unchecked.defaultof<Uri>
            refuse (Uri.TryCreate(identity.ApiOrigin, UriKind.Absolute, &origin)
                    && origin.Scheme = "https" && origin.UserInfo = "" && origin.Query = ""
                    && origin.Fragment = "" && identity.ApiOrigin = origin.GetLeftPart(UriPartial.Authority)) "checkpoint-api-origin"
            refuse (checkpoint.Generation > 0L && checkpoint.Revision >= 0L) "checkpoint-generation-revision"
            refuse (checkpoint.CreatedAt <= checkpoint.ObservedThrough && checkpoint.ObservedThrough <= now
                    && now - checkpoint.CreatedAt <= maximumAge) "checkpoint-expired-or-future"
            refuse (checkpoint.Resources.Length <= maximumResources) "checkpoint-resource-bound"
            refuse (checkpoint.Cursor >= 0 && checkpoint.Cursor <= checkpoint.Resources.Length) "checkpoint-cursor"
            refuse (optionalSha checkpoint.FirstSweepDigest && optionalSha checkpoint.SecondSweepDigest
                    && optionalSha checkpoint.PendingBatchDigest) "checkpoint-proof-digest"
            let keys = checkpoint.Resources |> List.map _.Key
            refuse ((Set.ofList keys).Count = keys.Length) "checkpoint-duplicate-resource"
            let mutable aggregate = 0L
            for resource in checkpoint.Resources do
                refuse (validKey resource.Key) "checkpoint-resource-key"
                let bodyBytes = utf8.GetBytes resource.Body
                aggregate <- aggregate + int64 bodyBytes.Length
                refuse (bodyBytes.Length <= maximumBodyBytes && digest bodyBytes = resource.BodyDigest) "checkpoint-body-drift-or-bound"
                refuse (sha resource.ProjectionDigest) "checkpoint-projection-digest"
                refuse (not (isNull resource.Validator) && resource.Validator.Length <= 4096
                        && not (resource.Validator.Contains '\r' || resource.Validator.Contains '\n')) "checkpoint-validator"
                refuse (not (isNull resource.Next) && resource.Next.Length <= 4096) "checkpoint-next-bound"
                refuse (resource.ObservedAt >= checkpoint.CreatedAt && resource.ObservedAt <= now
                        && resource.ValidatedAt >= resource.ObservedAt && resource.ValidatedAt <= checkpoint.ObservedThrough) "checkpoint-resource-clock"
            refuse (aggregate <= int64 maximumAggregateBodyBytes) "checkpoint-aggregate-bound"
            match checkpoint.Phase with
            | Stable | PublicationPending ->
                let current = populationDigest checkpoint.Resources
                refuse (checkpoint.Cursor = checkpoint.Resources.Length && not checkpoint.Resources.IsEmpty
                        && checkpoint.FirstSweepDigest = current && checkpoint.SecondSweepDigest = current) "checkpoint-incomplete-stability"
                refuse (checkpoint.Revision > 0L) "checkpoint-stable-revision"
            | _ -> ()
            refuse ((checkpoint.Phase = PublicationPending) = (checkpoint.PendingBatchDigest <> "")
                    && (checkpoint.PendingBatchDigest <> "") = (checkpoint.PendingBatchBase64 <> "")) "checkpoint-pending-publication-shape"
            if checkpoint.PendingBatchBase64 <> "" then
                refuse (checkpoint.PendingBatchBase64.Length <= 12 * 1024 * 1024) "checkpoint-pending-batch-encoded-bound"
                let pending = Convert.FromBase64String checkpoint.PendingBatchBase64
                refuse (pending.Length <= 8 * 1024 * 1024 && digest pending = checkpoint.PendingBatchDigest) "checkpoint-pending-batch-drift-or-bound"
            refuse (checkpoint.FactReferences.Length <= maximumResources) "checkpoint-fact-reference-bound"
            let factKeys = checkpoint.FactReferences |> List.map (fun fact -> fact.Kind, fact.Identity)
            refuse ((Set.ofList factKeys).Count = factKeys.Length) "checkpoint-duplicate-fact-reference"
            for fact in checkpoint.FactReferences do
                refuse (Set.contains fact.Kind (set [ "ci-population-admission"; "ci-binding"; "ci-page"; "ci-run"; "ci-job"; "ci-step"; "ci-coverage"; "ci-check"; "ci-population-coverage"; "diagnostic"; "ci-suite-page/1"; "ci-suite-seal/1" ]) && text fact.Identity && fact.Revision > 0L && fact.Revision <= checkpoint.Revision
                        && sha fact.Digest) "checkpoint-fact-reference-shape"
            if errors.Count = 0 then Ok checkpoint else Error(List.ofSeq errors)

        let validate expected now checkpoint =
            try validateState expected now checkpoint
            with _ -> Error [ "checkpoint-malformed" ]

        /// Pure sweep transitions operate on validated local state, without publishing receiver facts.
        let beginSweep (checkpoint: Checkpoint) =
            if checkpoint.Phase = PublicationPending then Error [ "checkpoint-publication-readback-required" ]
            else
                Ok
                    { checkpoint with
                        Phase = FirstSweep;
                        Cursor = 0;
                        FirstSweepDigest = populationDigest checkpoint.Resources;
                        SecondSweepDigest = "";
                    }

        let advanceSweep resourceKey changed (checkpoint: Checkpoint) =
            if checkpoint.Phase <> FirstSweep && checkpoint.Phase <> SecondSweep then
                Error [ "checkpoint-not-validating" ]
            elif changed then
                Ok { checkpoint with Phase = Acquiring; Cursor = 0; FirstSweepDigest = ""; SecondSweepDigest = "" }
            else
                let resources = checkpoint.Resources |> List.sortBy _.Key
                if checkpoint.Cursor < 0 || checkpoint.Cursor >= resources.Length
                   || resources[checkpoint.Cursor].Key <> resourceKey then Error [ "checkpoint-validation-cursor-mismatch" ]
                else Ok { checkpoint with Cursor = checkpoint.Cursor + 1 }

        let finishSweep (checkpoint: Checkpoint) =
            let current = populationDigest checkpoint.Resources
            if checkpoint.Cursor <> checkpoint.Resources.Length then Error [ "checkpoint-sweep-incomplete" ]
            elif checkpoint.Phase = FirstSweep && checkpoint.FirstSweepDigest = current then
                Ok { checkpoint with Phase = SecondSweep; Cursor = 0; SecondSweepDigest = current }
            elif checkpoint.Phase = SecondSweep && checkpoint.FirstSweepDigest = current && checkpoint.SecondSweepDigest = current then
                Ok { checkpoint with Phase = FinalBarrier; Cursor = 0 }
            elif checkpoint.Phase = FirstSweep || checkpoint.Phase = SecondSweep then
                Ok { checkpoint with Phase = Acquiring; Cursor = 0; FirstSweepDigest = ""; SecondSweepDigest = "" }
            else Error [ "checkpoint-not-validating" ]

        /// Revision advancement is logical and checked; provider timestamps never choose this value.
        let nextRevision (prior: int64) (storeFloor: int64) =
            let floor = max prior storeFloor
            if prior < 0L || storeFloor < 0L || floor = Int64.MaxValue then Error [ "checkpoint-revision-exhausted" ]
            else Ok(floor + 1L)

        let private closed (names: string list) (element: JsonElement) =
            if element.ValueKind <> JsonValueKind.Object then invalidOp "checkpoint-object"
            let actual = element.EnumerateObject() |> Seq.map _.Name |> Seq.toList
            if actual.Length <> names.Length || Set.ofList actual <> Set.ofList names then
                invalidOp "checkpoint-unknown-missing-or-duplicate-field"

        let private stringValue (name: string) (element: JsonElement) =
            let value = element.GetProperty name
            if value.ValueKind <> JsonValueKind.String then invalidOp "checkpoint-string"
            value.GetString()

        let private int64Value (name: string) (element: JsonElement) = element.GetProperty(name).GetInt64()
        let private intValue (name: string) (element: JsonElement) = element.GetProperty(name).GetInt32()
        let private clockValue (name: string) (element: JsonElement) =
            DateTimeOffset.ParseExact(stringValue name element, "O", Globalization.CultureInfo.InvariantCulture)

        let parse (expected: Identity) (now: DateTimeOffset) (bytes: byte array) =
            if bytes.Length > maximumCheckpointBytes then Error [ "checkpoint-byte-bound" ]
            else
                try
                    use document = JsonDocument.Parse(bytes, JsonDocumentOptions(MaxDepth = 16))
                    let root = document.RootElement
                    closed [ "schema"; "identity"; "generation"; "createdAt"; "phase"; "cursor"; "resources"
                             "observedThrough"; "priorCheckpointDigest"; "candidateDigest"; "firstSweepDigest"; "secondSweepDigest"
                             "revision"; "pendingBatchDigest"; "pendingBatchBase64"; "factReferences" ] root
                    if stringValue "schema" root <> "fsgg.telemetry.provider-checkpoint/2" then invalidOp "checkpoint-schema"
                    let identity = root.GetProperty "identity"
                    closed [ "assignment"; "assignmentDigest"; "storeId"; "collection"; "apiOrigin"; "repository"; "repositoryId"
                             "pullRequest"; "baseRef"; "baseSha"; "head"; "profileDigest"; "parserContract" ] identity
                    let assignment = identity.GetProperty "assignment"
                    let parsedAssignment =
                        match parseAssignment (utf8.GetBytes(assignment.GetRawText())) with
                        | Ok parsed -> parsed
                        | Error _ -> invalidOp "checkpoint-assignment-shape"
                    closed [ "schema"; "featureId"; "itemId"; "attemptId"; "parentAttemptId"; "producerStream" ] assignment
                    let factReferences = root.GetProperty "factReferences"
                    if factReferences.ValueKind <> JsonValueKind.Array || factReferences.GetArrayLength() > maximumResources then
                        invalidOp "checkpoint-fact-reference-bound"
                    let parsedFacts =
                        factReferences.EnumerateArray()
                        |> Seq.map (fun fact ->
                            closed [ "kind"; "identity"; "revision"; "digest" ] fact
                            {
                                Kind = stringValue "kind" fact;
                                Identity = stringValue "identity" fact;
                                Revision = int64Value "revision" fact;
                                Digest = stringValue "digest" fact;
                            })
                        |> Seq.toList
                    let resources = root.GetProperty "resources"
                    if resources.ValueKind <> JsonValueKind.Array || resources.GetArrayLength() > maximumResources then
                        invalidOp "checkpoint-resource-bound"
                    let parsedResources =
                        resources.EnumerateArray()
                        |> Seq.map (fun resource ->
                            closed [ "kind"; "runId"; "suiteId"; "attempt"; "page"; "body"; "bodyDigest"; "validator"; "next"
                                     "projectionDigest"; "observedAt"; "validatedAt" ] resource
                            let kind =
                                match stringValue "kind" resource with
                                | "admission" -> Admission | "runs" -> Runs | "attempt" -> Attempt
                                | "jobs" -> Jobs | "checks" -> Checks | "suites" -> Suites | "suite-checks" -> SuiteChecks | _ -> invalidOp "checkpoint-resource-kind"
                            {
                                Key =
                                    {
                                        Kind = kind;
                                        RunId = int64Value "runId" resource;
                                        SuiteId = int64Value "suiteId" resource;
                                        Attempt = intValue "attempt" resource;
                                        Page = intValue "page" resource;
                                    }
                                Body = stringValue "body" resource; BodyDigest = stringValue "bodyDigest" resource
                                Validator = stringValue "validator" resource; Next = stringValue "next" resource
                                ProjectionDigest = stringValue "projectionDigest" resource
                                ObservedAt = clockValue "observedAt" resource; ValidatedAt = clockValue "validatedAt" resource
                            })
                        |> Seq.toList
                    let phase =
                        match stringValue "phase" root with
                        | "acquiring" -> Acquiring | "first-sweep" -> FirstSweep | "second-sweep" -> SecondSweep
                        | "final-barrier" -> FinalBarrier | "stable" -> Stable
                        | "publication-pending" -> PublicationPending | _ -> invalidOp "checkpoint-phase"
                    {
                        Identity =
                            {
                                Assignment = parsedAssignment;
                                AssignmentDigest = stringValue "assignmentDigest" identity;
                                StoreId = stringValue "storeId" identity;
                                Collection = stringValue "collection" identity;
                                ApiOrigin = stringValue "apiOrigin" identity;
                                Repository = stringValue "repository" identity;
                                RepositoryId = int64Value "repositoryId" identity;
                                PullRequest = int64Value "pullRequest" identity;
                                BaseRef = stringValue "baseRef" identity;
                                BaseSha = stringValue "baseSha" identity;
                                Head = stringValue "head" identity;
                                ProfileDigest = stringValue "profileDigest" identity;
                                ParserContract = stringValue "parserContract" identity;
                            }
                        Generation = int64Value "generation" root; CreatedAt = clockValue "createdAt" root
                        ObservedThrough = clockValue "observedThrough" root; PriorCheckpointDigest = stringValue "priorCheckpointDigest" root
                        CandidateDigest = stringValue "candidateDigest" root
                        Phase = phase; Cursor = intValue "cursor" root; Resources = parsedResources
                        FirstSweepDigest = stringValue "firstSweepDigest" root; SecondSweepDigest = stringValue "secondSweepDigest" root
                        Revision = int64Value "revision" root; PendingBatchDigest = stringValue "pendingBatchDigest" root
                        PendingBatchBase64 = stringValue "pendingBatchBase64" root; FactReferences = parsedFacts
                    }
                    |> validate expected now
                with _ -> Error [ "checkpoint-malformed" ]

        let encode (expected: Identity) (now: DateTimeOffset) (checkpoint: Checkpoint) =
            validate expected now checkpoint
            |> Result.bind (fun valid ->
                use stream = new IO.MemoryStream()
                use writer = new Utf8JsonWriter(stream)
                let text (name: string) (value: string) = writer.WriteString(name, value)
                let number (name: string) (value: int64) = writer.WriteNumber(name, value)
                writer.WriteStartObject()
                text "schema" "fsgg.telemetry.provider-checkpoint/2"
                writer.WriteStartObject("identity")
                let identity = valid.Identity
                writer.WriteStartObject("assignment")
                text "schema" AssignmentSchema
                text "featureId" identity.Assignment.FeatureId
                text "itemId" identity.Assignment.ItemId
                text "attemptId" identity.Assignment.AttemptId
                text "producerStream" identity.Assignment.ProducerStream
                match identity.Assignment.ParentAttemptId with
                | Some parent -> text "parentAttemptId" parent
                | None -> writer.WriteNull("parentAttemptId")
                writer.WriteEndObject()
                for name, value in [ "assignmentDigest", identity.AssignmentDigest; "storeId", identity.StoreId
                                     "collection", identity.Collection; "apiOrigin", identity.ApiOrigin
                                     "repository", identity.Repository; "baseRef", identity.BaseRef
                                     "baseSha", identity.BaseSha; "head", identity.Head
                                     "profileDigest", identity.ProfileDigest; "parserContract", identity.ParserContract ] do text name value
                number "repositoryId" identity.RepositoryId
                number "pullRequest" identity.PullRequest
                writer.WriteEndObject()
                number "generation" valid.Generation
                text "createdAt" (valid.CreatedAt.ToString("O"))
                text "observedThrough" (valid.ObservedThrough.ToString("O"))
                text "priorCheckpointDigest" valid.PriorCheckpointDigest
                text "candidateDigest" valid.CandidateDigest
                text "phase" (phaseName valid.Phase)
                number "cursor" (int64 valid.Cursor)
                writer.WriteStartArray("resources")
                for resource in valid.Resources do
                    writer.WriteStartObject()
                    text "kind" (kindName resource.Key.Kind)
                    number "runId" resource.Key.RunId
                    number "suiteId" resource.Key.SuiteId
                    number "attempt" (int64 resource.Key.Attempt)
                    number "page" (int64 resource.Key.Page)
                    for name, value in [ "body", resource.Body; "bodyDigest", resource.BodyDigest
                                         "validator", resource.Validator; "next", resource.Next
                                         "projectionDigest", resource.ProjectionDigest
                                         "observedAt", resource.ObservedAt.ToString("O")
                                         "validatedAt", resource.ValidatedAt.ToString("O") ] do text name value
                    writer.WriteEndObject()
                writer.WriteEndArray()
                text "firstSweepDigest" valid.FirstSweepDigest
                text "secondSweepDigest" valid.SecondSweepDigest
                number "revision" valid.Revision
                text "pendingBatchDigest" valid.PendingBatchDigest
                text "pendingBatchBase64" valid.PendingBatchBase64
                writer.WriteStartArray("factReferences")
                for fact in valid.FactReferences do
                    writer.WriteStartObject()
                    text "kind" fact.Kind
                    text "identity" fact.Identity
                    number "revision" fact.Revision
                    text "digest" fact.Digest
                    writer.WriteEndObject()
                writer.WriteEndArray()
                writer.WriteEndObject()
                writer.Flush()
                let bytes = stream.ToArray()
                if bytes.Length > maximumCheckpointBytes then Error [ "checkpoint-byte-bound" ] else Ok bytes)
