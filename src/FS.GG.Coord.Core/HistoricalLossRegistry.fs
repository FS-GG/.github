namespace FS.GG.Coord

module HistoricalLossRegistry =
    open System
    open System.Globalization
    open System.Security.Cryptography
    open System.Text
    open System.Text.Json

    [<Literal>]
    let Schema = "fsgg.coord.historical-loss-registry/v1"

    [<Literal>]
    let RequiredConsequence =
        "exclude-unverifiable-history-and-block-positive-provenance"

    type SourceRole =
        | ProtectedParserOnly
        | LocalCacheOnly

    type AuditedSource =
        {
            ProducerId: string
            Revision: string
            Path: string
            BlobSha: string
            BytesSha256: string
            Role: SourceRole
        }

    type NativeSurvivor =
        {
            NativeId: string
            Family: string
            PayloadBlobSha: string
            SessionOperationId: string option
        }

    type NativeCensus =
        {
            Scope: string
            ObservedThrough: string
            Revision: string
            Complete: bool
            Terminal: bool
            DeclaredCount: int
            RawBlobShas: string list
            Survivors: NativeSurvivor list
            Digest: string
        }

    type ApprovalBinding =
        {
            Subject: string
            PullRequest: int
            BaseSha: string
            HeadSha: string
            MergeCommitSha: string
            RegistryPath: string
        }

    type Entry =
        {
            Family: string
            Scope: string
            Cutoff: string
            AuditedSources: AuditedSource list
            CensusFirst: NativeCensus
            CensusSecond: NativeCensus
            Consequence: string
            Approval: ApprovalBinding
        }

    type Registry = { Schema: string; Entries: Entry list }

    type NativePullRequest =
        {
            Repository: string
            PullRequest: int
            State: string
            Merged: bool
            BaseSha: string
            HeadSha: string
            MergeCommitSha: string
        }

    type NativeReviewComment =
        {
            DatabaseId: int64
            NodeId: string
            Url: string
            Body: string
            BodySha256: string
        }

    type NativeFileReadback =
        {
            Repository: string
            Path: string
            Revision: string
            BlobSha: string
            Bytes: byte array
            BytesSha256: string
        }

    type NativeBlobReadback =
        {
            Repository: string
            BlobSha: string
            Bytes: byte array
            BytesSha256: string
        }

    type NativeApprovalReadback =
        {
            PullRequestFirst: NativePullRequest
            PullRequestSecond: NativePullRequest
            ReviewCommentsFirst: NativeReviewComment list
            ReviewCommentsSecond: NativeReviewComment list
            ReviewCommentsComplete: bool
            ReviewCommentsTerminal: bool
            File: NativeFileReadback
            Blob: NativeBlobReadback
        }

    type BoundLoss =
        {
            Family: string
            Scope: string
            Cutoff: string
            CensusDigest: string
            ApprovalDigest: string
            Consequence: string
        }

    let private sha256 (bytes: byte array) =
        bytes |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()

    let private shaText (value: string) =
        value |> Encoding.UTF8.GetBytes |> sha256

    let private frame (value: string) =
        $"{Encoding.UTF8.GetByteCount value}:{value}"

    let private oid length (value: string) =
        not (String.IsNullOrWhiteSpace value)
        && value.Length = length
        && value = value.ToLowerInvariant()
        && value |> Seq.forall Uri.IsHexDigit

    let censusDigest (census: NativeCensus) =
        [
            census.Scope
            census.ObservedThrough
            census.Revision
            string census.Complete
            string census.Terminal
            string census.DeclaredCount
            yield! census.RawBlobShas
            yield!
                census.Survivors
                |> List.collect (fun survivor ->
                    [
                        survivor.NativeId
                        survivor.Family
                        survivor.PayloadBlobSha
                        defaultArg survivor.SessionOperationId ""
                    ])
        ]
        |> List.map frame
        |> String.concat ""
        |> shaText

    let private prop (name: string) (value: JsonElement) =
        let mutable found = Unchecked.defaultof<JsonElement>

        if value.ValueKind = JsonValueKind.Object && value.TryGetProperty(name, &found) then
            Ok found
        else
            Error $"missing or invalid {name}"

    let private text name value =
        prop name value
        |> Result.bind (fun item ->
            if item.ValueKind = JsonValueKind.String then
                Ok(item.GetString())
            else
                Error $"{name} must be a string")

    let private boolean name value =
        prop name value
        |> Result.bind (fun item ->
            match item.ValueKind with
            | JsonValueKind.True -> Ok true
            | JsonValueKind.False -> Ok false
            | _ -> Error $"{name} must be a boolean")

    let private integer name value =
        prop name value
        |> Result.bind (fun item ->
            let mutable parsed = 0

            if item.ValueKind = JsonValueKind.Number && item.TryGetInt32(&parsed) then
                Ok parsed
            else
                Error $"{name} must be an integer")

    let private optionalText name value =
        prop name value
        |> Result.bind (fun item ->
            match item.ValueKind with
            | JsonValueKind.Null -> Ok None
            | JsonValueKind.String -> Ok(Some(item.GetString()))
            | _ -> Error $"{name} must be a string or null")

    let private array name value parseItem =
        prop name value
        |> Result.bind (fun item ->
            if item.ValueKind <> JsonValueKind.Array then
                Error $"{name} must be an array"
            else
                item.EnumerateArray()
                |> Seq.fold
                    (fun state current ->
                        state
                        |> Result.bind (fun values ->
                            parseItem current |> Result.map (fun parsed -> parsed :: values)))
                    (Ok [])
                |> Result.map List.rev)

    let private exactMembers expected (value: JsonElement) =
        if value.ValueKind <> JsonValueKind.Object then
            Error "object expected"
        else
            let actual = value.EnumerateObject() |> Seq.map _.Name |> Seq.toList

            if actual.Length <> (actual |> Set.ofList |> Set.count) then
                Error "duplicate JSON member"
            elif Set.ofList actual <> Set.ofList expected then
                Error "unknown or missing JSON member"
            else
                Ok value

    let private combine results build =
        let errors =
            results
            |> List.choose (function
                | Error error -> Some error
                | Ok _ -> None)

        if List.isEmpty errors then
            build ()
        else
            Error(String.concat "; " errors)

    let private parseRole =
        function
        | "protected-parser-only" -> Ok ProtectedParserOnly
        | "local-cache-only" -> Ok LocalCacheOnly
        | _ -> Error "source role must describe non-producer audit evidence"

    let private parseSource value =
        exactMembers [ "producerId"; "revision"; "path"; "blobSha"; "bytesSha256"; "role" ] value
        |> Result.bind (fun value ->
            let values =
                [
                    text "producerId" value
                    text "revision" value
                    text "path" value
                    text "blobSha" value
                    text "bytesSha256" value
                    text "role" value
                ]

            combine values (fun () ->
                match values with
                | [ Ok producer; Ok revision; Ok path; Ok blob; Ok bytes; Ok role ] ->
                    parseRole role
                    |> Result.map (fun parsed ->
                        {
                            ProducerId = producer
                            Revision = revision
                            Path = path
                            BlobSha = blob
                            BytesSha256 = bytes
                            Role = parsed
                        })
                | _ -> failwith "checked"))

    let private parseSurvivor value =
        exactMembers [ "nativeId"; "family"; "payloadBlobSha"; "sessionOperationId" ] value
        |> Result.bind (fun value ->
            let a, b, c, d =
                text "nativeId" value,
                text "family" value,
                text "payloadBlobSha" value,
                optionalText "sessionOperationId" value

            combine
                [
                    a |> Result.map ignore
                    b |> Result.map ignore
                    c |> Result.map ignore
                    d |> Result.map ignore
                ]
                (fun () ->
                    match a, b, c, d with
                    | Ok a, Ok b, Ok c, Ok d ->
                        Ok
                            {
                                NativeId = a
                                Family = b
                                PayloadBlobSha = c
                                SessionOperationId = d
                            }
                    | _ -> failwith "checked"))

    let private parseCensus value =
        exactMembers
            [
                "scope"
                "observedThrough"
                "revision"
                "complete"
                "terminal"
                "declaredCount"
                "rawBlobShas"
                "survivors"
                "digest"
            ]
            value
        |> Result.bind (fun value ->
            let scope, cutoff, revision =
                text "scope" value, text "observedThrough" value, text "revision" value

            let complete, terminal = boolean "complete" value, boolean "terminal" value
            let declaredCount = integer "declaredCount" value

            let blobs =
                array "rawBlobShas" value (fun item ->
                    if item.ValueKind = JsonValueKind.String then
                        Ok(item.GetString())
                    else
                        Error "rawBlobShas entries must be strings")

            let survivors = array "survivors" value parseSurvivor
            let digest = text "digest" value

            combine
                [
                    scope |> Result.map ignore
                    cutoff |> Result.map ignore
                    revision |> Result.map ignore
                    complete |> Result.map ignore
                    terminal |> Result.map ignore
                    declaredCount |> Result.map ignore
                    blobs |> Result.map ignore
                    survivors |> Result.map ignore
                    digest |> Result.map ignore
                ]
                (fun () ->
                    match scope, cutoff, revision, complete, terminal, declaredCount, blobs, survivors, digest with
                    | Ok a, Ok b, Ok c, Ok d, Ok e, Ok f, Ok g, Ok h, Ok i ->
                        Ok
                            {
                                Scope = a
                                ObservedThrough = b
                                Revision = c
                                Complete = d
                                Terminal = e
                                DeclaredCount = f
                                RawBlobShas = g
                                Survivors = h
                                Digest = i
                            }
                    | _ -> failwith "checked"))

    let private parseApproval value =
        exactMembers
            [
                "subject"
                "pullRequest"
                "baseSha"
                "headSha"
                "mergeCommitSha"
                "registryPath"
            ]
            value
        |> Result.bind (fun value ->
            let subject, pr, baseSha, head, merge, path =
                text "subject" value,
                integer "pullRequest" value,
                text "baseSha" value,
                text "headSha" value,
                text "mergeCommitSha" value,
                text "registryPath" value

            combine
                [
                    subject |> Result.map ignore
                    pr |> Result.map ignore
                    baseSha |> Result.map ignore
                    head |> Result.map ignore
                    merge |> Result.map ignore
                    path |> Result.map ignore
                ]
                (fun () ->
                    match subject, pr, baseSha, head, merge, path with
                    | Ok a, Ok b, Ok c, Ok d, Ok e, Ok f ->
                        Ok
                            {
                                Subject = a
                                PullRequest = b
                                BaseSha = c
                                HeadSha = d
                                MergeCommitSha = e
                                RegistryPath = f
                            }
                    | _ -> failwith "checked"))

    let private parseEntry value =
        exactMembers
            [
                "family"
                "scope"
                "cutoff"
                "auditedSources"
                "censusFirst"
                "censusSecond"
                "consequence"
                "approval"
            ]
            value
        |> Result.bind (fun value ->
            let family, scope, cutoff =
                text "family" value, text "scope" value, text "cutoff" value

            let sources = array "auditedSources" value parseSource
            let first = prop "censusFirst" value |> Result.bind parseCensus
            let second = prop "censusSecond" value |> Result.bind parseCensus
            let consequence = text "consequence" value
            let approval = prop "approval" value |> Result.bind parseApproval

            combine
                [
                    family |> Result.map ignore
                    scope |> Result.map ignore
                    cutoff |> Result.map ignore
                    sources |> Result.map ignore
                    first |> Result.map ignore
                    second |> Result.map ignore
                    consequence |> Result.map ignore
                    approval |> Result.map ignore
                ]
                (fun () ->
                    match family, scope, cutoff, sources, first, second, consequence, approval with
                    | Ok a, Ok b, Ok c, Ok d, Ok e, Ok f, Ok g, Ok h ->
                        Ok
                            {
                                Family = a
                                Scope = b
                                Cutoff = c
                                AuditedSources = d
                                CensusFirst = e
                                CensusSecond = f
                                Consequence = g
                                Approval = h
                            }
                    | _ -> failwith "checked"))

    let parse (raw: string) =
        try
            use document =
                JsonDocument.Parse(
                    raw,
                    JsonDocumentOptions(AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow)
                )

            exactMembers [ "schema"; "entries" ] document.RootElement
            |> Result.bind (fun root ->
                let schema = text "schema" root
                let entries = array "entries" root parseEntry

                match schema, entries with
                | Ok actual, Ok values when actual = Schema -> Ok { Schema = actual; Entries = values }
                | Ok _, Ok _ -> Error $"schema must be {Schema}"
                | _ ->
                    Error(
                        String.concat
                            "; "
                            [
                                match schema with
                                | Error e -> yield e
                                | _ ->
                                    ()

                                    match entries with
                                    | Error e -> yield e
                                    | _ -> ()
                            ]
                    ))
            |> Result.mapError (fun error -> [ error ])
        with :? JsonException as error ->
            Error [ $"invalid registry JSON: {error.Message}" ]

    let private allowedFamilies =
        Set.ofList [ "delivery-receipt"; "intake-receipt"; "legacy-done-receipt" ]

    let private validInstant value =
        let mutable parsed = DateTimeOffset.MinValue
        DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, &parsed)

    let private reviewRecords (comments: NativeReviewComment list) =
        comments
        |> List.choose (fun comment ->
            let marker = "<!-- fsgg:review-decision/v2 -->"

            if comment.Body.StartsWith(marker + "\n", StringComparison.Ordinal) then
                Some(Driver.decodeStructuredReview (comment.Body.Substring(marker.Length).Trim()))
            elif comment.Body.Contains("fsgg:review-decision", StringComparison.Ordinal) then
                Some(Error "unknown review-decision marker")
            else
                None)
        |> List.fold
            (fun state item ->
                match state, item with
                | Ok values, Ok value -> Ok(value :: values)
                | Error errors, Error error -> Error(error :: errors)
                | Error errors, _ -> Error errors
                | _, Error error -> Error [ error ])
            (Ok [])
        |> Result.map List.rev

    let private commentValid (comment: NativeReviewComment) =
        comment.DatabaseId > 0L
        && not (String.IsNullOrWhiteSpace comment.NodeId)
        && Uri.IsWellFormedUriString(comment.Url, UriKind.Absolute)
        && comment.BodySha256 = shaText comment.Body

    let bind
        (expectedFamily: string)
        (expectedScope: string)
        (expectedCutoff: string)
        (registryBytes: byte array)
        (entry: Entry)
        (native: NativeApprovalReadback)
        =
        let errors = ResizeArray<string>()

        let refuse condition message =
            if condition then
                errors.Add message

        try
            let raw = UTF8Encoding(false, true).GetString registryBytes

            match parse raw with
            | Error _ -> errors.Add "historical-loss-registry-parse"
            | Ok registry ->
                let matches = registry.Entries |> List.filter ((=) entry)

                if matches.Length <> 1 then
                    errors.Add "historical-loss-registry-entry-readback"
        with :? DecoderFallbackException ->
            errors.Add "historical-loss-registry-encoding"

        refuse (entry.Family <> expectedFamily) "historical-loss-family-mismatch"
        refuse (entry.Scope <> expectedScope) "historical-loss-scope-mismatch"
        refuse (entry.Cutoff <> expectedCutoff) "historical-loss-cutoff-mismatch"
        refuse (not (validInstant entry.Cutoff)) "historical-loss-cutoff-invalid"

        refuse
            (entry.CensusFirst.Survivors @ entry.CensusSecond.Survivors
             |> List.exists (fun s -> s.Family = "claim-marker" && s.SessionOperationId.IsNone))
            "historical-loss-sessionless-claim-refused"

        refuse (not (Set.contains entry.Family allowedFamilies)) "historical-loss-family-out-of-scope"
        refuse (entry.Consequence <> RequiredConsequence) "historical-loss-consequence"
        refuse entry.AuditedSources.IsEmpty "historical-loss-audit-empty"

        refuse
            (entry.AuditedSources
             |> List.exists (fun s ->
                 String.IsNullOrWhiteSpace s.ProducerId
                 || String.IsNullOrWhiteSpace s.Path
                 || not (oid 40 s.Revision)
                 || not (oid 40 s.BlobSha)
                 || not (oid 64 s.BytesSha256)))
            "historical-loss-audit-identity"

        let sourceKeys =
            entry.AuditedSources
            |> List.map (fun s -> s.ProducerId, s.Revision, s.Path, s.BlobSha, s.BytesSha256)

        refuse (sourceKeys.Length <> (sourceKeys |> Set.ofList |> Set.count)) "historical-loss-audit-duplicate"

        let censusValid (census: NativeCensus) =
            census.Scope = entry.Scope
            && census.ObservedThrough = entry.Cutoff
            && census.Complete
            && census.Terminal
            && oid 40 census.Revision
            && census.DeclaredCount = census.Survivors.Length
            && not census.RawBlobShas.IsEmpty
            && census.RawBlobShas |> List.forall (oid 40)
            && census.RawBlobShas.Length = (census.RawBlobShas |> Set.ofList |> Set.count)
            && census.Survivors
               |> List.forall (fun s ->
                   not (String.IsNullOrWhiteSpace s.NativeId)
                   && oid 40 s.PayloadBlobSha
                   && s.Family = entry.Family)
            && census.Digest = censusDigest census

        refuse
            (not (censusValid entry.CensusFirst) || not (censusValid entry.CensusSecond))
            "historical-loss-census-incomplete"

        refuse (entry.CensusFirst <> entry.CensusSecond) "historical-loss-census-drift"
        let approval: ApprovalBinding = entry.Approval
        let expectedSubjectSuffix = $"/pr/{approval.PullRequest}"

        refuse
            (approval.PullRequest <= 0
             || not (approval.Subject.StartsWith("FS-GG/.github#", StringComparison.Ordinal))
             || not (approval.Subject.EndsWith(expectedSubjectSuffix, StringComparison.Ordinal))
             || not (oid 40 approval.BaseSha)
             || not (oid 40 approval.HeadSha)
             || not (oid 40 approval.MergeCommitSha)
             || String.IsNullOrWhiteSpace approval.RegistryPath)
            "historical-loss-approval-binding"

        refuse (native.PullRequestFirst <> native.PullRequestSecond) "historical-loss-pr-drift"
        let pr = native.PullRequestFirst

        refuse
            (pr.Repository <> "FS-GG/.github"
             || pr.PullRequest <> approval.PullRequest
             || pr.State <> "closed"
             || not pr.Merged
             || pr.BaseSha <> approval.BaseSha
             || pr.HeadSha <> approval.HeadSha
             || pr.MergeCommitSha <> approval.MergeCommitSha)
            "historical-loss-pr-readback"

        refuse
            (native.ReviewCommentsFirst <> native.ReviewCommentsSecond
             || not native.ReviewCommentsComplete
             || not native.ReviewCommentsTerminal
             || native.ReviewCommentsFirst |> List.exists (commentValid >> not))
            "historical-loss-review-readback"

        match reviewRecords native.ReviewCommentsFirst with
        | Error _ -> errors.Add "historical-loss-review-ledger"
        | Ok records ->
            match StructuredDecision.validateReviewLedger approval.Subject records with
            | Error _ -> errors.Add "historical-loss-review-ledger"
            | Ok validated ->
                match List.tryLast validated with
                | Some accepted when
                    accepted.Kind = StructuredDecision.Acceptance
                    && accepted.Verdict = StructuredDecision.Accepted
                    && accepted.HeadSha = approval.HeadSha
                    && accepted.BaseSha = Some approval.BaseSha
                    && accepted.ClaimGeneration |> Option.exists (String.IsNullOrWhiteSpace >> not)
                    && List.contains ("registry-blob:" + native.File.BlobSha) accepted.DiffAuditReceipts
                    ->
                    ()
                | _ -> errors.Add "historical-loss-review-not-approved"

        let file = native.File
        let blob = native.Blob

        refuse
            (file.Repository <> "FS-GG/.github"
             || file.Path <> approval.RegistryPath
             || file.Revision <> approval.MergeCommitSha
             || not (oid 40 file.BlobSha)
             || file.BytesSha256 <> sha256 file.Bytes
             || file.Bytes <> registryBytes)
            "historical-loss-file-readback"

        refuse
            (blob.Repository <> "FS-GG/.github"
             || blob.BlobSha <> file.BlobSha
             || blob.BytesSha256 <> sha256 blob.Bytes
             || blob.Bytes <> registryBytes
             || blob.Bytes <> file.Bytes)
            "historical-loss-blob-readback"

        let gitBytes =
            Array.append (Encoding.ASCII.GetBytes($"blob {registryBytes.LongLength}\u0000")) registryBytes

        let gitSha =
            gitBytes |> SHA1.HashData |> Convert.ToHexString |> _.ToLowerInvariant()

        refuse (gitSha <> file.BlobSha) "historical-loss-registry-blob"

        if errors.Count > 0 then
            Error(List.ofSeq errors)
        else
            Ok
                {
                    Family = entry.Family
                    Scope = entry.Scope
                    Cutoff = entry.Cutoff
                    CensusDigest = entry.CensusFirst.Digest
                    ApprovalDigest = (native.ReviewCommentsFirst |> List.last).BodySha256
                    Consequence = entry.Consequence
                }
