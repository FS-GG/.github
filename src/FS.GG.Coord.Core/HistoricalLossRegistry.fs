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
    let SchemaV2 = "fsgg.coord.historical-loss-registry/v2"

    [<Literal>]
    let SchemaV3 = "fsgg.coord.historical-loss-registry/v3"

    [<Literal>]
    let ApprovalEnvelopeSchemaV2 = "fsgg.coord.historical-loss-approval/v2"

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

    type ApprovalBindingV2 =
        {
            Subject: string
            PullRequest: int
            BaseSha: string
            RegistryPath: string
        }

    type EntryV2 =
        {
            Family: string
            Scope: string
            Cutoff: string
            AuditedSources: AuditedSource list
            CensusFirst: NativeCensus
            CensusSecond: NativeCensus
            Consequence: string
            Approval: ApprovalBindingV2
        }

    type RegistryV2 = { Schema: string; Entries: EntryV2 list }

    type RecoverySourceRole =
        | RecoveredWriterSource
        | ProtocolAuthoringSource

    type RecoverySource =
        {
            ProducerId: string
            Revision: string
            Path: string
            BlobSha: string
            BytesSha256: string
            Role: RecoverySourceRole
        }

    type RetainedEnumerationKind =
        | DirectRepositoryEnumeration
        | SearchOnly
        | AuditNotFoundInference

    type RepositoryIdentityV3 =
        {
            FullName: string
            DatabaseId: int64
            NodeId: string
        }

    type RetainedSubjectV3 =
        {
            Repository: RepositoryIdentityV3
            NativeId: string
            Family: string
            CreatedAt: string
            PayloadBlobSha: string
            SessionOperationId: string option
            LiveClaim: bool
        }

    type RetainedPageV3 =
        {
            Repository: RepositoryIdentityV3
            Index: int
            ItemCount: int
            RawSha256: string
            Terminal: bool
        }

    type RetainedNativeCensusV3 =
        {
            SelectedRepositories: RepositoryIdentityV3 list
            ObservationHorizon: string
            Revision: string
            Enumeration: RetainedEnumerationKind
            Complete: bool
            DeclaredCount: int
            Pages: RetainedPageV3 list
            Subjects: RetainedSubjectV3 list
            HistoricalEmissions: string
            HistoricalDeletions: string
            LostCount: string
            ProducerDeploymentEnd: string
            Digest: string
        }

    type EntryV3 =
        {
            Family: string
            Scope: string
            ObservationHorizon: string
            RecoverySources: RecoverySource list
            KnownSurvivorIds: string list
            CensusFirst: RetainedNativeCensusV3
            CensusSecond: RetainedNativeCensusV3
            ExclusionAppliesToLiveClaims: bool
            Consequence: string
            Approval: ApprovalBindingV2
        }

    type RegistryV3 = { Schema: string; Entries: EntryV3 list }

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

    type NativeMergedPullRequestV2 =
        {
            PullRequest: NativePullRequest
            MergedAt: string
        }

    type NativeApprovalEnvelopeCommentV2 =
        {
            DatabaseId: int64
            NodeId: string
            Url: string
            CreatedAt: string
            Body: string
            BodySha256: string
        }

    type NativeApprovalReadbackV2 =
        {
            PullRequestFirst: NativeMergedPullRequestV2
            PullRequestSecond: NativeMergedPullRequestV2
            ReviewCommentsFirst: NativeReviewComment list
            ReviewCommentsSecond: NativeReviewComment list
            ReviewCommentsComplete: bool
            ReviewCommentsTerminal: bool
            ApprovalEnvelopeFirst: NativeApprovalEnvelopeCommentV2
            ApprovalEnvelopeSecond: NativeApprovalEnvelopeCommentV2
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

    type BoundLossV3 =
        {
            Family: string
            Scope: string
            ObservationHorizon: string
            SelectedRepositories: RepositoryIdentityV3 list
            RetainedCount: int
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

    let retainedCensusDigestV3 (census: RetainedNativeCensusV3) =
        [
            yield!
                census.SelectedRepositories
                |> List.collect (fun repository -> [ repository.FullName; string repository.DatabaseId; repository.NodeId ])
            census.ObservationHorizon
            census.Revision
            string census.Enumeration
            string census.Complete
            string census.DeclaredCount
            yield!
                census.Pages
                |> List.collect (fun page ->
                    [ page.Repository.FullName; string page.Repository.DatabaseId; page.Repository.NodeId; string page.Index; string page.ItemCount; page.RawSha256; string page.Terminal ])
            yield!
                census.Subjects
                |> List.collect (fun subject ->
                    [
                        subject.Repository.FullName
                        string subject.Repository.DatabaseId
                        subject.Repository.NodeId
                        subject.NativeId
                        subject.Family
                        subject.CreatedAt
                        subject.PayloadBlobSha
                        defaultArg subject.SessionOperationId ""
                        string subject.LiveClaim
                    ])
            census.HistoricalEmissions
            census.HistoricalDeletions
            census.LostCount
            census.ProducerDeploymentEnd
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

    let private integer64 name value =
        prop name value
        |> Result.bind (fun item ->
            let mutable parsed = 0L

            if item.ValueKind = JsonValueKind.Number && item.TryGetInt64(&parsed) then
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
                        ({
                            ProducerId = producer
                            Revision = revision
                            Path = path
                            BlobSha = blob
                            BytesSha256 = bytes
                            Role = parsed
                        }: AuditedSource))
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

    let private parseApproval value : Result<ApprovalBinding, string> =
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

    let private parseApprovalV2 value : Result<ApprovalBindingV2, string> =
        exactMembers [ "subject"; "pullRequest"; "baseSha"; "registryPath" ] value
        |> Result.bind (fun value ->
            let subject, pr, baseSha, path =
                text "subject" value,
                integer "pullRequest" value,
                text "baseSha" value,
                text "registryPath" value

            combine
                [
                    subject |> Result.map ignore
                    pr |> Result.map ignore
                    baseSha |> Result.map ignore
                    path |> Result.map ignore
                ]
                (fun () ->
                    match subject, pr, baseSha, path with
                    | Ok a, Ok b, Ok c, Ok d ->
                        Ok
                            {
                                Subject = a
                                PullRequest = b
                                BaseSha = c
                                RegistryPath = d
                            }
                    | _ -> failwith "checked"))

    let private parseEntry value : Result<Entry, string> =
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

    let private parseEntryV2 value : Result<EntryV2, string> =
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
            let approval = prop "approval" value |> Result.bind parseApprovalV2

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

    let private parseRecoveryRole =
        function
        | "recovered-writer-source" -> Ok RecoveredWriterSource
        | "protocol-authoring-source" -> Ok ProtocolAuthoringSource
        | _ -> Error "recovery source role is unknown"

    let private parseRecoverySource value =
        exactMembers [ "producerId"; "revision"; "path"; "blobSha"; "bytesSha256"; "role" ] value
        |> Result.bind (fun value ->
            let producer, revision, path, blob, bytes, role =
                text "producerId" value,
                text "revision" value,
                text "path" value,
                text "blobSha" value,
                text "bytesSha256" value,
                text "role" value

            combine
                [ producer; revision; path; blob; bytes; role ]
                (fun () ->
                    match producer, revision, path, blob, bytes, role with
                    | Ok a, Ok b, Ok c, Ok d, Ok e, Ok f ->
                        parseRecoveryRole f
                        |> Result.map (fun parsed ->
                            ({
                                ProducerId = a
                                Revision = b
                                Path = c
                                BlobSha = d
                                BytesSha256 = e
                                Role = parsed
                            }: RecoverySource))
                    | _ -> failwith "checked"))

    let private parseEnumeration =
        function
        | "direct-repository-enumeration" -> Ok DirectRepositoryEnumeration
        | "search-only" -> Ok SearchOnly
        | "audit-not-found-inference" -> Ok AuditNotFoundInference
        | _ -> Error "retained enumeration kind is unknown"

    let private parseRepositoryIdentityV3 value =
        exactMembers [ "fullName"; "databaseId"; "nodeId" ] value
        |> Result.bind (fun value ->
            let fullName, databaseId, nodeId =
                text "fullName" value, integer64 "databaseId" value, text "nodeId" value

            combine
                [ fullName |> Result.map ignore; databaseId |> Result.map ignore; nodeId |> Result.map ignore ]
                (fun () ->
                    match fullName, databaseId, nodeId with
                    | Ok a, Ok b, Ok c -> Ok { FullName = a; DatabaseId = b; NodeId = c }
                    | _ -> failwith "checked"))

    let private parseRetainedPageV3 value =
        exactMembers [ "repository"; "index"; "itemCount"; "rawSha256"; "terminal" ] value
        |> Result.bind (fun value ->
            let repository, index, count, raw, terminal =
                prop "repository" value |> Result.bind parseRepositoryIdentityV3,
                integer "index" value,
                integer "itemCount" value,
                text "rawSha256" value,
                boolean "terminal" value

            combine
                [ repository |> Result.map ignore; index |> Result.map ignore; count |> Result.map ignore; raw |> Result.map ignore; terminal |> Result.map ignore ]
                (fun () ->
                    match repository, index, count, raw, terminal with
                    | Ok a, Ok b, Ok c, Ok d, Ok e ->
                        Ok { Repository = a; Index = b; ItemCount = c; RawSha256 = d; Terminal = e }
                    | _ -> failwith "checked"))

    let private parseRetainedSubjectV3 value =
        exactMembers [ "repository"; "nativeId"; "family"; "createdAt"; "payloadBlobSha"; "sessionOperationId"; "liveClaim" ] value
        |> Result.bind (fun value ->
            let repository, nativeId, family, createdAt, blob, operation, live =
                prop "repository" value |> Result.bind parseRepositoryIdentityV3,
                text "nativeId" value,
                text "family" value,
                text "createdAt" value,
                text "payloadBlobSha" value,
                optionalText "sessionOperationId" value,
                boolean "liveClaim" value

            combine
                [ repository |> Result.map ignore; nativeId |> Result.map ignore; family |> Result.map ignore; createdAt |> Result.map ignore; blob |> Result.map ignore; operation |> Result.map ignore; live |> Result.map ignore ]
                (fun () ->
                    match repository, nativeId, family, createdAt, blob, operation, live with
                    | Ok a, Ok b, Ok c, Ok d, Ok e, Ok f, Ok g ->
                        Ok
                            {
                                Repository = a
                                NativeId = b
                                Family = c
                                CreatedAt = d
                                PayloadBlobSha = e
                                SessionOperationId = f
                                LiveClaim = g
                            }
                    | _ -> failwith "checked"))

    let private parseStringArray name value =
        array name value (fun item ->
            if item.ValueKind = JsonValueKind.String then Ok(item.GetString()) else Error $"{name} entries must be strings")

    let private parseRetainedCensusV3 value =
        exactMembers
            [
                "selectedRepositories"; "observationHorizon"; "revision"; "enumeration"; "complete"
                "declaredCount"; "pages"; "subjects"; "historicalEmissions"; "historicalDeletions"
                "lostCount"; "producerDeploymentEnd"; "digest"
            ]
            value
        |> Result.bind (fun value ->
            let repositories = array "selectedRepositories" value parseRepositoryIdentityV3
            let horizon = text "observationHorizon" value
            let revision = text "revision" value
            let enumeration = text "enumeration" value |> Result.bind parseEnumeration
            let complete = boolean "complete" value
            let count = integer "declaredCount" value
            let pages = array "pages" value parseRetainedPageV3
            let subjects = array "subjects" value parseRetainedSubjectV3
            let emissions = text "historicalEmissions" value
            let deletions = text "historicalDeletions" value
            let lost = text "lostCount" value
            let deployment = text "producerDeploymentEnd" value
            let digest = text "digest" value

            combine
                [ repositories |> Result.map ignore; horizon |> Result.map ignore; revision |> Result.map ignore; enumeration |> Result.map ignore; complete |> Result.map ignore; count |> Result.map ignore; pages |> Result.map ignore; subjects |> Result.map ignore; emissions |> Result.map ignore; deletions |> Result.map ignore; lost |> Result.map ignore; deployment |> Result.map ignore; digest |> Result.map ignore ]
                (fun () ->
                    match repositories, horizon, revision, enumeration, complete, count, pages, subjects, emissions, deletions, lost, deployment, digest with
                    | Ok a, Ok b, Ok c, Ok d, Ok e, Ok f, Ok g, Ok h, Ok i, Ok j, Ok k, Ok l, Ok m ->
                        Ok
                            {
                                SelectedRepositories = a
                                ObservationHorizon = b
                                Revision = c
                                Enumeration = d
                                Complete = e
                                DeclaredCount = f
                                Pages = g
                                Subjects = h
                                HistoricalEmissions = i
                                HistoricalDeletions = j
                                LostCount = k
                                ProducerDeploymentEnd = l
                                Digest = m
                            }
                    | _ -> failwith "checked"))

    let private parseEntryV3 value : Result<EntryV3, string> =
        exactMembers
            [
                "family"; "scope"; "observationHorizon"; "recoverySources"; "knownSurvivorIds"
                "censusFirst"; "censusSecond"; "exclusionAppliesToLiveClaims"; "consequence"; "approval"
            ]
            value
        |> Result.bind (fun value ->
            let family = text "family" value
            let scope = text "scope" value
            let horizon = text "observationHorizon" value
            let sources = array "recoverySources" value parseRecoverySource
            let known = parseStringArray "knownSurvivorIds" value
            let first = prop "censusFirst" value |> Result.bind parseRetainedCensusV3
            let second = prop "censusSecond" value |> Result.bind parseRetainedCensusV3
            let live = boolean "exclusionAppliesToLiveClaims" value
            let consequence = text "consequence" value
            let approval = prop "approval" value |> Result.bind parseApprovalV2

            combine
                [ family |> Result.map ignore; scope |> Result.map ignore; horizon |> Result.map ignore; sources |> Result.map ignore; known |> Result.map ignore; first |> Result.map ignore; second |> Result.map ignore; live |> Result.map ignore; consequence |> Result.map ignore; approval |> Result.map ignore ]
                (fun () ->
                    match family, scope, horizon, sources, known, first, second, live, consequence, approval with
                    | Ok a, Ok b, Ok c, Ok d, Ok e, Ok f, Ok g, Ok h, Ok i, Ok j ->
                        Ok
                            {
                                Family = a
                                Scope = b
                                ObservationHorizon = c
                                RecoverySources = d
                                KnownSurvivorIds = e
                                CensusFirst = f
                                CensusSecond = g
                                ExclusionAppliesToLiveClaims = h
                                Consequence = i
                                Approval = j
                            }
                    | _ -> failwith "checked"))

    let parse (raw: string) : Result<Registry, string list> =
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
                | Ok actual, Ok values when actual = Schema ->
                    Ok({ Schema = actual; Entries = values } : Registry)
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

    let parseV2 (raw: string) : Result<RegistryV2, string list> =
        try
            use document =
                JsonDocument.Parse(
                    raw,
                    JsonDocumentOptions(AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow)
                )

            exactMembers [ "schema"; "entries" ] document.RootElement
            |> Result.bind (fun root ->
                let schema = text "schema" root
                let entries = array "entries" root parseEntryV2

                match schema, entries with
                | Ok actual, Ok values when actual = SchemaV2 ->
                    Ok({ Schema = actual; Entries = values } : RegistryV2)
                | Ok _, Ok _ -> Error $"schema must be {SchemaV2}"
                | _ ->
                    Error(
                        String.concat
                            "; "
                            [
                                match schema with
                                | Error e -> yield e
                                | _ -> ()

                                match entries with
                                | Error e -> yield e
                                | _ -> ()
                            ]
                    ))
            |> Result.mapError (fun error -> [ error ])
        with :? JsonException as error ->
            Error [ $"invalid registry JSON: {error.Message}" ]

    let parseV3 (raw: string) : Result<RegistryV3, string list> =
        try
            use document =
                JsonDocument.Parse(
                    raw,
                    JsonDocumentOptions(AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow)
                )

            exactMembers [ "schema"; "entries" ] document.RootElement
            |> Result.bind (fun root ->
                let schema = text "schema" root
                let entries = array "entries" root parseEntryV3

                match schema, entries with
                | Ok actual, Ok values when actual = SchemaV3 ->
                    Ok({ Schema = actual; Entries = values } : RegistryV3)
                | Ok _, Ok _ -> Error $"schema must be {SchemaV3}"
                | _ ->
                    Error(
                        String.concat
                            "; "
                            [
                                match schema with
                                | Error e -> yield e
                                | _ -> ()

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

    type private ApprovalEnvelopeV2 =
        {
            Subject: string
            PullRequest: int
            BaseSha: string
            ReviewedHeadSha: string
            MergeCommitSha: string
            RegistryPath: string
            RegistryBlobSha: string
            RegistryBytesSha256: string
            AcceptedReviewDigest: string
        }

    let private parseApprovalEnvelopeV2 (body: string) =
        let marker = "<!-- fsgg:historical-loss-approval/v2 -->"

        try
            if String.IsNullOrEmpty body || not (body.StartsWith(marker + "\n", StringComparison.Ordinal)) then
                Error "historical-loss-approval-envelope-marker"
            else
                let raw = body.Substring(marker.Length + 1)
                use document =
                    JsonDocument.Parse(
                        raw,
                        JsonDocumentOptions(AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow)
                    )

                let root = document.RootElement

                exactMembers
                    [
                        "schema"
                        "subject"
                        "pullRequest"
                        "baseSha"
                        "reviewedHeadSha"
                        "mergeCommitSha"
                        "registryPath"
                        "registryBlobSha"
                        "registryBytesSha256"
                        "acceptedReviewDigest"
                    ]
                    root
                |> Result.bind (fun root ->
                    let schema = text "schema" root
                    let subject = text "subject" root
                    let pullRequest = integer "pullRequest" root
                    let baseSha = text "baseSha" root
                    let reviewedHeadSha = text "reviewedHeadSha" root
                    let mergeCommitSha = text "mergeCommitSha" root
                    let registryPath = text "registryPath" root
                    let registryBlobSha = text "registryBlobSha" root
                    let registryBytesSha256 = text "registryBytesSha256" root
                    let acceptedReviewDigest = text "acceptedReviewDigest" root

                    combine
                        [
                            schema |> Result.map ignore
                            subject |> Result.map ignore
                            pullRequest |> Result.map ignore
                            baseSha |> Result.map ignore
                            reviewedHeadSha |> Result.map ignore
                            mergeCommitSha |> Result.map ignore
                            registryPath |> Result.map ignore
                            registryBlobSha |> Result.map ignore
                            registryBytesSha256 |> Result.map ignore
                            acceptedReviewDigest |> Result.map ignore
                        ]
                        (fun () ->
                            match
                                schema,
                                subject,
                                pullRequest,
                                baseSha,
                                reviewedHeadSha,
                                mergeCommitSha,
                                registryPath,
                                registryBlobSha,
                                registryBytesSha256,
                                acceptedReviewDigest
                            with
                            | Ok schema,
                              Ok subject,
                              Ok pullRequest,
                              Ok baseSha,
                              Ok reviewedHeadSha,
                              Ok mergeCommitSha,
                              Ok registryPath,
                              Ok registryBlobSha,
                              Ok registryBytesSha256,
                              Ok acceptedReviewDigest when schema = ApprovalEnvelopeSchemaV2 ->
                                Ok
                                    {
                                        Subject = subject
                                        PullRequest = pullRequest
                                        BaseSha = baseSha
                                        ReviewedHeadSha = reviewedHeadSha
                                        MergeCommitSha = mergeCommitSha
                                        RegistryPath = registryPath
                                        RegistryBlobSha = registryBlobSha
                                        RegistryBytesSha256 = registryBytesSha256
                                        AcceptedReviewDigest = acceptedReviewDigest
                                    }
                            | Ok _, _, _, _, _, _, _, _, _, _ ->
                                Error "historical-loss-approval-envelope-schema"
                            | _ -> failwith "checked"))
        with :? JsonException ->
            Error "historical-loss-approval-envelope-json"

    let private approvalEnvelopeCommentValid pullRequest (comment: NativeApprovalEnvelopeCommentV2) =
        let expectedUrl = $"https://github.com/FS-GG/.github/pull/{pullRequest}#issuecomment-"

        comment.DatabaseId > 0L
        && not (String.IsNullOrWhiteSpace comment.NodeId)
        && comment.Url.StartsWith(expectedUrl, StringComparison.Ordinal)
        && validInstant comment.CreatedAt
        && comment.BodySha256 = shaText comment.Body

    let bindV2
        (expectedFamily: string)
        (expectedScope: string)
        (expectedCutoff: string)
        (registryBytes: byte array)
        (entry: EntryV2)
        (native: NativeApprovalReadbackV2)
        =
        let errors = ResizeArray<string>()

        let refuse condition message =
            if condition then
                errors.Add message

        try
            let raw = UTF8Encoding(false, true).GetString registryBytes

            match parseV2 raw with
            | Error _ -> errors.Add "historical-loss-registry-v2-parse"
            | Ok registry ->
                let matches = registry.Entries |> List.filter ((=) entry)

                if matches.Length <> 1 then
                    errors.Add "historical-loss-registry-v2-entry-readback"
        with :? DecoderFallbackException ->
            errors.Add "historical-loss-registry-v2-encoding"

        refuse (entry.Family <> expectedFamily) "historical-loss-family-mismatch"
        refuse (entry.Scope <> expectedScope) "historical-loss-scope-mismatch"
        refuse (entry.Cutoff <> expectedCutoff) "historical-loss-cutoff-mismatch"
        refuse (not (validInstant entry.Cutoff)) "historical-loss-cutoff-invalid"

        refuse
            (entry.CensusFirst.Survivors @ entry.CensusSecond.Survivors
             |> List.exists (fun survivor ->
                 survivor.Family = "claim-marker" && survivor.SessionOperationId.IsNone))
            "historical-loss-sessionless-claim-refused"

        refuse (not (Set.contains entry.Family allowedFamilies)) "historical-loss-family-out-of-scope"
        refuse (entry.Consequence <> RequiredConsequence) "historical-loss-consequence"
        refuse entry.AuditedSources.IsEmpty "historical-loss-audit-empty"

        refuse
            (entry.AuditedSources
             |> List.exists (fun source ->
                 String.IsNullOrWhiteSpace source.ProducerId
                 || String.IsNullOrWhiteSpace source.Path
                 || not (oid 40 source.Revision)
                 || not (oid 40 source.BlobSha)
                 || not (oid 64 source.BytesSha256)))
            "historical-loss-audit-identity"

        let sourceKeys =
            entry.AuditedSources
            |> List.map (fun source ->
                source.ProducerId, source.Revision, source.Path, source.BlobSha, source.BytesSha256)

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
               |> List.forall (fun survivor ->
                   not (String.IsNullOrWhiteSpace survivor.NativeId)
                   && oid 40 survivor.PayloadBlobSha
                   && survivor.Family = entry.Family)
            && census.Digest = censusDigest census

        refuse
            (not (censusValid entry.CensusFirst) || not (censusValid entry.CensusSecond))
            "historical-loss-census-incomplete"

        refuse (entry.CensusFirst <> entry.CensusSecond) "historical-loss-census-drift"
        let approval = entry.Approval
        let expectedSubjectSuffix = $"/pr/{approval.PullRequest}"

        refuse
            (approval.PullRequest <= 0
             || not (approval.Subject.StartsWith("FS-GG/.github#", StringComparison.Ordinal))
             || not (approval.Subject.EndsWith(expectedSubjectSuffix, StringComparison.Ordinal))
             || not (oid 40 approval.BaseSha)
             || String.IsNullOrWhiteSpace approval.RegistryPath)
            "historical-loss-approval-v2-binding"

        refuse (native.PullRequestFirst <> native.PullRequestSecond) "historical-loss-pr-drift"
        let nativePr = native.PullRequestFirst
        let pr = nativePr.PullRequest

        refuse
            (pr.Repository <> "FS-GG/.github"
             || pr.PullRequest <> approval.PullRequest
             || pr.State <> "closed"
             || not pr.Merged
             || pr.BaseSha <> approval.BaseSha
             || not (oid 40 pr.HeadSha)
             || not (oid 40 pr.MergeCommitSha)
             || not (validInstant nativePr.MergedAt))
            "historical-loss-pr-readback"

        refuse
            (native.ReviewCommentsFirst <> native.ReviewCommentsSecond
             || not native.ReviewCommentsComplete
             || not native.ReviewCommentsTerminal
             || native.ReviewCommentsFirst |> List.exists (commentValid >> not))
            "historical-loss-review-readback"

        let mutable acceptedReviewDigest = None

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
                    && accepted.HeadSha = pr.HeadSha
                    && accepted.BaseSha = Some approval.BaseSha
                    && accepted.ClaimGeneration |> Option.exists (String.IsNullOrWhiteSpace >> not)
                    && List.contains ("registry-blob:" + native.File.BlobSha) accepted.DiffAuditReceipts
                    ->
                    acceptedReviewDigest <- Some accepted.Digest
                | _ -> errors.Add "historical-loss-review-not-approved"

        refuse
            (native.ApprovalEnvelopeFirst <> native.ApprovalEnvelopeSecond
             || not (approvalEnvelopeCommentValid approval.PullRequest native.ApprovalEnvelopeFirst))
            "historical-loss-approval-envelope-readback"

        let envelope =
            match parseApprovalEnvelopeV2 native.ApprovalEnvelopeFirst.Body with
            | Ok value -> Some value
            | Error reason ->
                errors.Add reason
                None

        let file = native.File
        let blob = native.Blob

        refuse
            (file.Repository <> "FS-GG/.github"
             || file.Path <> approval.RegistryPath
             || not (oid 40 file.Revision)
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

        match envelope with
        | Some value ->
            refuse
                (value.Subject <> approval.Subject
                 || value.PullRequest <> approval.PullRequest
                 || value.BaseSha <> approval.BaseSha
                 || value.ReviewedHeadSha <> pr.HeadSha
                 || value.MergeCommitSha <> pr.MergeCommitSha
                 || value.MergeCommitSha <> file.Revision
                 || value.RegistryPath <> approval.RegistryPath
                 || value.RegistryBlobSha <> file.BlobSha
                 || value.RegistryBytesSha256 <> sha256 registryBytes
                 || Some value.AcceptedReviewDigest <> acceptedReviewDigest
                 || not (oid 40 value.ReviewedHeadSha)
                 || not (oid 40 value.MergeCommitSha)
                 || not (oid 40 value.RegistryBlobSha)
                 || not (oid 64 value.RegistryBytesSha256)
                 || not (oid 64 value.AcceptedReviewDigest))
                "historical-loss-approval-envelope-binding"

            let mutable mergedAt = DateTimeOffset.MinValue
            let mutable approvedAt = DateTimeOffset.MinValue
            let mergedParsed =
                DateTimeOffset.TryParseExact(
                    nativePr.MergedAt,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    &mergedAt
                )
            let approvedParsed =
                DateTimeOffset.TryParseExact(
                    native.ApprovalEnvelopeFirst.CreatedAt,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    &approvedAt
                )

            refuse
                (not mergedParsed || not approvedParsed || approvedAt <= mergedAt)
                "historical-loss-approval-envelope-not-post-merge"
        | None -> ()

        if errors.Count > 0 then
            Error(List.ofSeq errors)
        else
            Ok
                {
                    Family = entry.Family
                    Scope = entry.Scope
                    Cutoff = entry.Cutoff
                    CensusDigest = entry.CensusFirst.Digest
                    ApprovalDigest = native.ApprovalEnvelopeFirst.BodySha256
                    Consequence = entry.Consequence
                }

    let bindV3
        (expectedFamily: string)
        (expectedScope: string)
        (expectedObservationHorizon: string)
        (expectedRepositories: RepositoryIdentityV3 list)
        (registryBytes: byte array)
        (entry: EntryV3)
        (native: NativeApprovalReadbackV2)
        =
        let errors = ResizeArray<string>()
        let refuse condition message = if condition then errors.Add message

        try
            let raw = UTF8Encoding(false, true).GetString registryBytes
            match parseV3 raw with
            | Error _ -> errors.Add "historical-loss-registry-v3-parse"
            | Ok registry when registry.Entries |> List.filter ((=) entry) |> List.length <> 1 ->
                errors.Add "historical-loss-registry-v3-entry-readback"
            | Ok _ -> ()
        with :? DecoderFallbackException ->
            errors.Add "historical-loss-registry-v3-encoding"

        refuse (entry.Family <> expectedFamily) "historical-loss-family-mismatch"
        refuse (entry.Scope <> expectedScope) "historical-loss-scope-mismatch"
        refuse (entry.ObservationHorizon <> expectedObservationHorizon) "historical-loss-observation-horizon-mismatch"
        refuse (not (validInstant entry.ObservationHorizon)) "historical-loss-observation-horizon-invalid"
        refuse (not (Set.contains entry.Family allowedFamilies)) "historical-loss-family-out-of-scope"
        refuse (entry.Consequence <> RequiredConsequence) "historical-loss-consequence"
        refuse entry.ExclusionAppliesToLiveClaims "historical-loss-live-claim-exclusion"

        let sourceRoles = entry.RecoverySources |> List.map _.Role |> Set.ofList
        refuse
            (not (Set.contains RecoveredWriterSource sourceRoles)
             || not (Set.contains ProtocolAuthoringSource sourceRoles))
            "historical-loss-recovery-source-role"

        refuse
            (entry.RecoverySources.IsEmpty
             || entry.RecoverySources
                |> List.exists (fun source ->
                    String.IsNullOrWhiteSpace source.ProducerId
                    || String.IsNullOrWhiteSpace source.Path
                    || not (oid 40 source.Revision)
                    || not (oid 40 source.BlobSha)
                    || not (oid 64 source.BytesSha256)))
            "historical-loss-recovery-source-identity"

        let sourceKeys =
            entry.RecoverySources
            |> List.map (fun source -> source.ProducerId, source.Revision, source.Path, source.BlobSha, source.BytesSha256)
        refuse (sourceKeys.Length <> (sourceKeys |> Set.ofList |> Set.count)) "historical-loss-recovery-source-duplicate"

        refuse
            (expectedRepositories.IsEmpty
             || expectedRepositories <> (expectedRepositories |> List.distinct |> List.sort)
             || expectedRepositories
                |> List.exists (fun repository ->
                    not (repository.FullName.StartsWith("FS-GG/", StringComparison.Ordinal))
                    || repository.DatabaseId <= 0L
                    || String.IsNullOrWhiteSpace repository.NodeId))
            "historical-loss-selected-repositories-invalid"

        let censusValid (census: RetainedNativeCensusV3) =
            let repositorySet = census.SelectedRepositories |> Set.ofList
            let subjectIds = census.Subjects |> List.map (fun subject -> subject.Repository, subject.NativeId)
            let nativeIds = census.Subjects |> List.map _.NativeId
            let pagesSequential =
                census.Pages
                |> List.mapi (fun index page ->
                    page.Index = index + 1
                    && page.ItemCount >= 0
                    && oid 64 page.RawSha256
                    && Set.contains page.Repository repositorySet
                    && page.Terminal = (index = census.Pages.Length - 1))
                |> List.forall id

            let subjectsValid =
                census.Subjects
                |> List.forall (fun subject ->
                    Set.contains subject.Repository repositorySet
                    && subject.Family = entry.Family
                    && not (String.IsNullOrWhiteSpace subject.NativeId)
                    && oid 40 subject.PayloadBlobSha
                    && validInstant subject.CreatedAt
                    && DateTimeOffset.Parse(subject.CreatedAt, CultureInfo.InvariantCulture) <= DateTimeOffset.Parse(entry.ObservationHorizon, CultureInfo.InvariantCulture)
                    && not subject.LiveClaim)

            census.SelectedRepositories = expectedRepositories
            && census.ObservationHorizon = entry.ObservationHorizon
            && oid 40 census.Revision
            && census.Enumeration = DirectRepositoryEnumeration
            && census.Complete
            && census.DeclaredCount = census.Subjects.Length
            && not census.Pages.IsEmpty
            && (census.Pages |> List.sumBy _.ItemCount) = census.Subjects.Length
            && pagesSequential
            && subjectsValid
            && subjectIds.Length = (subjectIds |> Set.ofList |> Set.count)
            && nativeIds.Length = (nativeIds |> Set.ofList |> Set.count)
            && census.HistoricalEmissions = "unknown"
            && census.HistoricalDeletions = "unknown"
            && census.LostCount = "unknown"
            && census.ProducerDeploymentEnd = "unknown"
            && census.Digest = retainedCensusDigestV3 census

        refuse
            (not (censusValid entry.CensusFirst) || not (censusValid entry.CensusSecond))
            "historical-loss-retained-census-incomplete"
        refuse (entry.CensusFirst <> entry.CensusSecond) "historical-loss-retained-census-drift"

        let retainedIds = entry.CensusFirst.Subjects |> List.map _.NativeId |> Set.ofList
        refuse
            (entry.KnownSurvivorIds.IsEmpty
             || entry.KnownSurvivorIds.Length <> (entry.KnownSurvivorIds |> Set.ofList |> Set.count)
             || entry.KnownSurvivorIds |> List.exists (fun nativeId -> not (Set.contains nativeId retainedIds)))
            "historical-loss-known-survivor-missing"

        let approval = entry.Approval
        let expectedSubjectSuffix = $"/pr/{approval.PullRequest}"
        refuse
            (approval.PullRequest <= 0
             || not (approval.Subject.StartsWith("FS-GG/.github#", StringComparison.Ordinal))
             || not (approval.Subject.EndsWith(expectedSubjectSuffix, StringComparison.Ordinal))
             || not (oid 40 approval.BaseSha)
             || String.IsNullOrWhiteSpace approval.RegistryPath)
            "historical-loss-approval-v3-binding"

        refuse (native.PullRequestFirst <> native.PullRequestSecond) "historical-loss-pr-drift"
        let nativePr = native.PullRequestFirst
        let pr = nativePr.PullRequest
        refuse
            (pr.Repository <> "FS-GG/.github"
             || pr.PullRequest <> approval.PullRequest
             || pr.State <> "closed"
             || not pr.Merged
             || pr.BaseSha <> approval.BaseSha
             || not (oid 40 pr.HeadSha)
             || not (oid 40 pr.MergeCommitSha)
             || not (validInstant nativePr.MergedAt))
            "historical-loss-pr-readback"

        refuse
            (native.ReviewCommentsFirst <> native.ReviewCommentsSecond
             || not native.ReviewCommentsComplete
             || not native.ReviewCommentsTerminal
             || native.ReviewCommentsFirst |> List.exists (commentValid >> not))
            "historical-loss-review-readback"

        let mutable acceptedReviewDigest = None
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
                    && accepted.HeadSha = pr.HeadSha
                    && accepted.BaseSha = Some approval.BaseSha
                    && accepted.ClaimGeneration |> Option.exists (String.IsNullOrWhiteSpace >> not)
                    && List.contains ("registry-blob:" + native.File.BlobSha) accepted.DiffAuditReceipts ->
                    acceptedReviewDigest <- Some accepted.Digest
                | _ -> errors.Add "historical-loss-review-not-approved"

        refuse
            (native.ApprovalEnvelopeFirst <> native.ApprovalEnvelopeSecond
             || not (approvalEnvelopeCommentValid approval.PullRequest native.ApprovalEnvelopeFirst))
            "historical-loss-approval-envelope-readback"

        let envelope =
            match parseApprovalEnvelopeV2 native.ApprovalEnvelopeFirst.Body with
            | Ok value -> Some value
            | Error reason -> errors.Add reason; None

        let file, blob = native.File, native.Blob
        refuse
            (file.Repository <> "FS-GG/.github"
             || file.Path <> approval.RegistryPath
             || not (oid 40 file.Revision)
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

        let gitBytes = Array.append (Encoding.ASCII.GetBytes($"blob {registryBytes.LongLength}\u0000")) registryBytes
        let gitSha = gitBytes |> SHA1.HashData |> Convert.ToHexString |> _.ToLowerInvariant()
        refuse (gitSha <> file.BlobSha) "historical-loss-registry-blob"

        match envelope with
        | Some value ->
            refuse
                (value.Subject <> approval.Subject
                 || value.PullRequest <> approval.PullRequest
                 || value.BaseSha <> approval.BaseSha
                 || value.ReviewedHeadSha <> pr.HeadSha
                 || value.MergeCommitSha <> pr.MergeCommitSha
                 || value.MergeCommitSha <> file.Revision
                 || value.RegistryPath <> approval.RegistryPath
                 || value.RegistryBlobSha <> file.BlobSha
                 || value.RegistryBytesSha256 <> sha256 registryBytes
                 || Some value.AcceptedReviewDigest <> acceptedReviewDigest)
                "historical-loss-approval-envelope-binding"

            let mutable mergedAt, approvedAt = DateTimeOffset.MinValue, DateTimeOffset.MinValue
            let mergedParsed = DateTimeOffset.TryParseExact(nativePr.MergedAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, &mergedAt)
            let approvedParsed = DateTimeOffset.TryParseExact(native.ApprovalEnvelopeFirst.CreatedAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, &approvedAt)
            refuse (not mergedParsed || not approvedParsed || approvedAt <= mergedAt) "historical-loss-approval-envelope-not-post-merge"
        | None -> ()

        if errors.Count > 0 then Error(List.ofSeq errors)
        else
            Ok
                {
                    Family = entry.Family
                    Scope = entry.Scope
                    ObservationHorizon = entry.ObservationHorizon
                    SelectedRepositories = expectedRepositories
                    RetainedCount = entry.CensusFirst.DeclaredCount
                    CensusDigest = entry.CensusFirst.Digest
                    ApprovalDigest = native.ApprovalEnvelopeFirst.BodySha256
                    Consequence = entry.Consequence
                }
