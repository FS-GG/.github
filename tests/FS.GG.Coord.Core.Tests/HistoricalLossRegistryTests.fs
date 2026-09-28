namespace FS.GG.Coord.Tests

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open Xunit
open FS.GG.Coord

module HistoricalLossRegistryTests =
    let private hex character count =
        String.replicate count (string character)

    let private sha256 (bytes: byte array) =
        bytes |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()

    let private shaText (value: string) =
        value |> Encoding.UTF8.GetBytes |> sha256

    let private gitBlobSha (bytes: byte array) =
        Array.append (Encoding.ASCII.GetBytes($"blob {bytes.LongLength}\u0000")) bytes
        |> SHA1.HashData
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    let private cutoff = "2026-09-27T00:00:00.0000000+00:00"
    let private scope = "FS-GG/FS.GG.Coordination"
    let private baseSha = hex 'a' 40
    let private headSha = hex 'b' 40
    let private mergeSha = hex 'c' 40
    let private sourceRevision = hex 'd' 40
    let private sourceBlob = hex 'e' 40
    let private rawBlob = hex 'f' 40
    let private subject = "FS-GG/.github#4000/pr/4001"

    let private census family (survivors: HistoricalLossRegistry.NativeSurvivor list) =
        let draft: HistoricalLossRegistry.NativeCensus =
            {
                Scope = scope
                ObservedThrough = cutoff
                Revision = sourceRevision
                Complete = true
                Terminal = true
                RawBlobShas = [ rawBlob ]
                DeclaredCount = survivors.Length
                Survivors = survivors
                Digest = ""
            }

        { draft with
            Digest = HistoricalLossRegistry.censusDigest draft
        }

    let private entry family survivors =
        let evidence = census family survivors

        ({
            Family = family
            Scope = scope
            Cutoff = cutoff
            AuditedSources =
                [
                    {
                        ProducerId = "coordination-source-audit"
                        Revision = sourceRevision
                        Path = "src/parser.fs"
                        BlobSha = sourceBlob
                        BytesSha256 = hex '1' 64
                        Role = HistoricalLossRegistry.ProtectedParserOnly
                    }
                ]
            CensusFirst = evidence
            CensusSecond = evidence
            Consequence = HistoricalLossRegistry.RequiredConsequence
            Approval =
                {
                    Subject = subject
                    PullRequest = 4001
                    BaseSha = baseSha
                    HeadSha = headSha
                    MergeCommitSha = mergeSha
                    RegistryPath = "policy/historical-loss-registry.json"
                }
        }
        : HistoricalLossRegistry.Entry)

    let private roleText =
        function
        | HistoricalLossRegistry.ProtectedParserOnly -> "protected-parser-only"
        | HistoricalLossRegistry.LocalCacheOnly -> "local-cache-only"

    let private quoted value =
        System.Text.Json.JsonSerializer.Serialize(value: string)

    let private expectError =
        function
        | Error errors -> errors
        | Ok _ -> failwith "expected refusal"

    let private registryJson (entry: HistoricalLossRegistry.Entry) =
        let source = entry.AuditedSources.Head

        let renderSurvivor (survivor: HistoricalLossRegistry.NativeSurvivor) =
            let session =
                survivor.SessionOperationId |> Option.map quoted |> Option.defaultValue "null"

            $"{{\"nativeId\":{quoted survivor.NativeId},\"family\":{quoted survivor.Family},\"payloadBlobSha\":{quoted survivor.PayloadBlobSha},\"sessionOperationId\":{session}}}"

        let renderCensus (value: HistoricalLossRegistry.NativeCensus) =
            let blobs = value.RawBlobShas |> List.map quoted |> String.concat ","
            let survivors = value.Survivors |> List.map renderSurvivor |> String.concat ","
            $"{{\"scope\":{quoted value.Scope},\"observedThrough\":{quoted value.ObservedThrough},\"revision\":{quoted value.Revision},\"complete\":{value.Complete.ToString().ToLowerInvariant()},\"terminal\":{value.Terminal.ToString().ToLowerInvariant()},\"declaredCount\":{value.DeclaredCount},\"rawBlobShas\":[{blobs}],\"survivors\":[{survivors}],\"digest\":{quoted value.Digest}}}"

        $"{{\"schema\":{quoted HistoricalLossRegistry.Schema},\"entries\":[{{\"family\":{quoted entry.Family},\"scope\":{quoted entry.Scope},\"cutoff\":{quoted entry.Cutoff},\"auditedSources\":[{{\"producerId\":{quoted source.ProducerId},\"revision\":{quoted source.Revision},\"path\":{quoted source.Path},\"blobSha\":{quoted source.BlobSha},\"bytesSha256\":{quoted source.BytesSha256},\"role\":{quoted (roleText source.Role)}}}],\"censusFirst\":{renderCensus entry.CensusFirst},\"censusSecond\":{renderCensus entry.CensusSecond},\"consequence\":{quoted entry.Consequence},\"approval\":{{\"subject\":{quoted entry.Approval.Subject},\"pullRequest\":{entry.Approval.PullRequest},\"baseSha\":{quoted entry.Approval.BaseSha},\"headSha\":{quoted entry.Approval.HeadSha},\"mergeCommitSha\":{quoted entry.Approval.MergeCommitSha},\"registryPath\":{quoted entry.Approval.RegistryPath}}}}}]}}"

    let private entryV2 family survivors actualBase =
        let evidence = census family survivors

        ({
            Family = family
            Scope = scope
            Cutoff = cutoff
            AuditedSources =
                [
                    {
                        ProducerId = "coordination-source-audit"
                        Revision = sourceRevision
                        Path = "src/parser.fs"
                        BlobSha = sourceBlob
                        BytesSha256 = hex '1' 64
                        Role = HistoricalLossRegistry.ProtectedParserOnly
                    }
                ]
            CensusFirst = evidence
            CensusSecond = evidence
            Consequence = HistoricalLossRegistry.RequiredConsequence
            Approval =
                {
                    Subject = subject
                    PullRequest = 4001
                    BaseSha = actualBase
                    RegistryPath = "policy/historical-loss-registry.json"
                }
        }
        : HistoricalLossRegistry.EntryV2)

    let private registryJsonV2 (entry: HistoricalLossRegistry.EntryV2) =
        let source = entry.AuditedSources.Head

        let renderSurvivor (survivor: HistoricalLossRegistry.NativeSurvivor) =
            let session =
                survivor.SessionOperationId |> Option.map quoted |> Option.defaultValue "null"

            $"{{\"nativeId\":{quoted survivor.NativeId},\"family\":{quoted survivor.Family},\"payloadBlobSha\":{quoted survivor.PayloadBlobSha},\"sessionOperationId\":{session}}}"

        let renderCensus (value: HistoricalLossRegistry.NativeCensus) =
            let blobs = value.RawBlobShas |> List.map quoted |> String.concat ","
            let survivors = value.Survivors |> List.map renderSurvivor |> String.concat ","
            $"{{\"scope\":{quoted value.Scope},\"observedThrough\":{quoted value.ObservedThrough},\"revision\":{quoted value.Revision},\"complete\":{value.Complete.ToString().ToLowerInvariant()},\"terminal\":{value.Terminal.ToString().ToLowerInvariant()},\"declaredCount\":{value.DeclaredCount},\"rawBlobShas\":[{blobs}],\"survivors\":[{survivors}],\"digest\":{quoted value.Digest}}}"

        $"{{\"schema\":{quoted HistoricalLossRegistry.SchemaV2},\"entries\":[{{\"family\":{quoted entry.Family},\"scope\":{quoted entry.Scope},\"cutoff\":{quoted entry.Cutoff},\"auditedSources\":[{{\"producerId\":{quoted source.ProducerId},\"revision\":{quoted source.Revision},\"path\":{quoted source.Path},\"blobSha\":{quoted source.BlobSha},\"bytesSha256\":{quoted source.BytesSha256},\"role\":{quoted (roleText source.Role)}}}],\"censusFirst\":{renderCensus entry.CensusFirst},\"censusSecond\":{renderCensus entry.CensusSecond},\"consequence\":{quoted entry.Consequence},\"approval\":{{\"subject\":{quoted entry.Approval.Subject},\"pullRequest\":{entry.Approval.PullRequest},\"baseSha\":{quoted entry.Approval.BaseSha},\"registryPath\":{quoted entry.Approval.RegistryPath}}}}}]}}"

    let private reseal record =
        let draft =
            { record with
                StructuredDecision.ReviewRecord.Digest = ""
            }

        { draft with
            Digest = StructuredDecision.reviewDigest draft
        }

    let private reviewChainFor reviewedBase reviewedHead blobSha =
        let initial =
            ({
                Schema = StructuredDecision.ReviewSchema
                Subject = subject
                Revision = 1
                PreviousDigest = None
                HeadSha = reviewedHead
                ClaimGeneration = None
                BaseSha = None
                Critic = "loss-reviewer-1"
                Verdict = StructuredDecision.Pass
                AcceptedExceptions = []
                RouteApplicability = "not-meaningful"
                RouteEvidence = [ "pure historical-loss evidence review" ]
                PolicyVersion = StructuredDecision.PolicyVersion
                Kind = StructuredDecision.Initial
                Round = 0
                InitialReview = None
                PrecedingReview = None
                DiffAuditRequired = false
                DiffAuditReceipts = []
                Succession = None
                RepairPhaseReceipt = None
                Timestamp = "2026-09-27T01:00:00.0000000+00:00"
                Digest = ""
            }
            : StructuredDecision.ReviewRecord)
            |> reseal

        let acceptance =
            ({ initial with
                Revision = 2
                PreviousDigest = Some initial.Digest
                ClaimGeneration = Some "loss-approval-session-1"
                BaseSha = Some reviewedBase
                Verdict = StructuredDecision.Accepted
                Kind = StructuredDecision.Acceptance
                InitialReview = Some "https://github.com/FS-GG/.github/pull/4001#issuecomment-1"
                PrecedingReview = Some "https://github.com/FS-GG/.github/pull/4001#issuecomment-1"
                DiffAuditReceipts = [ "registry-blob:" + blobSha ]
                Timestamp = "2026-09-27T01:01:00.0000000+00:00"
                Digest = ""
            })
            |> reseal

        [ initial; acceptance ]

    let private reviewChain blobSha = reviewChainFor baseSha headSha blobSha

    let private comment id record =
        let body =
            "<!-- fsgg:review-decision/v2 -->\n" + Driver.encodeStructuredReview record

        ({
            DatabaseId = id
            NodeId = $"IC_{id}"
            Url = $"https://github.com/FS-GG/.github/pull/4001#issuecomment-{id}"
            Body = body
            BodySha256 = shaText body
        }
        : HistoricalLossRegistry.NativeReviewComment)

    let private approvalEnvelopeBody
        (entry: HistoricalLossRegistry.EntryV2)
        reviewedHead
        mergeCommit
        registryBlob
        registryBytesSha256
        acceptedReviewDigest
        =
        "<!-- fsgg:historical-loss-approval/v2 -->\n"
        + $"{{\"schema\":{quoted HistoricalLossRegistry.ApprovalEnvelopeSchemaV2},\"subject\":{quoted entry.Approval.Subject},\"pullRequest\":{entry.Approval.PullRequest},\"baseSha\":{quoted entry.Approval.BaseSha},\"reviewedHeadSha\":{quoted reviewedHead},\"mergeCommitSha\":{quoted mergeCommit},\"registryPath\":{quoted entry.Approval.RegistryPath},\"registryBlobSha\":{quoted registryBlob},\"registryBytesSha256\":{quoted registryBytesSha256},\"acceptedReviewDigest\":{quoted acceptedReviewDigest}}}"

    let private approvalEnvelopeComment createdAt body =
        ({
            DatabaseId = 3L
            NodeId = "IC_3"
            Url = "https://github.com/FS-GG/.github/pull/4001#issuecomment-3"
            CreatedAt = createdAt
            Body = body
            BodySha256 = shaText body
        }
        : HistoricalLossRegistry.NativeApprovalEnvelopeCommentV2)

    let private runGit directory arguments =
        let start = ProcessStartInfo("/usr/bin/git")
        start.WorkingDirectory <- directory
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        start.UseShellExecute <- false
        start.Environment.Clear()
        start.Environment["PATH"] <- "/usr/bin"
        start.Environment["LC_ALL"] <- "C"

        for argument in arguments do
            start.ArgumentList.Add argument

        use childProcess = Process.Start start
        let output = childProcess.StandardOutput.ReadToEnd()
        let error = childProcess.StandardError.ReadToEnd()
        childProcess.WaitForExit()

        if childProcess.ExitCode <> 0 then
            failwithf "git %A failed: %s" arguments error

        output.Trim()

    let private fixture entry =
        let bytes = registryJson entry |> Encoding.UTF8.GetBytes
        let blobSha = gitBlobSha bytes

        let comments =
            reviewChain blobSha
            |> List.mapi (fun index record -> comment (int64 index + 1L) record)

        let pr: HistoricalLossRegistry.NativePullRequest =
            {
                Repository = "FS-GG/.github"
                PullRequest = 4001
                State = "closed"
                Merged = true
                BaseSha = baseSha
                HeadSha = headSha
                MergeCommitSha = mergeSha
            }

        let file: HistoricalLossRegistry.NativeFileReadback =
            {
                Repository = "FS-GG/.github"
                Path = entry.Approval.RegistryPath
                Revision = mergeSha
                BlobSha = blobSha
                Bytes = bytes
                BytesSha256 = sha256 bytes
            }

        bytes,
        ({
            PullRequestFirst = pr
            PullRequestSecond = pr
            ReviewCommentsFirst = comments
            ReviewCommentsSecond = comments
            ReviewCommentsComplete = true
            ReviewCommentsTerminal = true
            File = file
            Blob =
                {
                    Repository = "FS-GG/.github"
                    BlobSha = blobSha
                    Bytes = bytes
                    BytesSha256 = sha256 bytes
                }
        }
        : HistoricalLossRegistry.NativeApprovalReadback)

    [<Fact>]
    let ``exact source census review merge file and blob readback bind a bounded loss`` () =
        let item = entry "delivery-receipt" []
        let bytes, native = fixture item
        let parsed = HistoricalLossRegistry.parse (Encoding.UTF8.GetString bytes)

        let expected: HistoricalLossRegistry.Registry =
            {
                Schema = HistoricalLossRegistry.Schema
                Entries = [ item ]
            }

        Assert.Equal(Ok expected, parsed)

        match HistoricalLossRegistry.bind item.Family item.Scope item.Cutoff bytes item native with
        | Ok bound ->
            Assert.Equal(item.Family, bound.Family)
            Assert.Equal(HistoricalLossRegistry.RequiredConsequence, bound.Consequence)
        | Error errors -> failwithf "exact evidence refused: %A" errors

    [<Fact>]
    let ``v2 post merge envelope binds real git identities without a registry self reference`` () =
        let directory = Path.Combine(Path.GetTempPath(), "fsgg-loss-v2-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory directory |> ignore

        try
            runGit directory [ "init"; "--initial-branch=main" ] |> ignore
            File.WriteAllText(Path.Combine(directory, "README.md"), "base\n")
            runGit directory [ "add"; "README.md" ] |> ignore
            runGit directory
                [ "-c"; "user.name=FS.GG Test"; "-c"; "user.email=test@fs.gg"
                  "commit"; "-m"; "base" ]
            |> ignore
            let actualBase = runGit directory [ "rev-parse"; "HEAD" ]
            let item = entryV2 "delivery-receipt" [] actualBase
            let bytes = registryJsonV2 item |> Encoding.UTF8.GetBytes
            runGit directory [ "switch"; "-c"; "loss-registry" ] |> ignore
            let registryFile = Path.Combine(directory, item.Approval.RegistryPath)
            Directory.CreateDirectory(Path.GetDirectoryName registryFile) |> ignore
            File.WriteAllBytes(registryFile, bytes)
            runGit directory [ "add"; item.Approval.RegistryPath ] |> ignore
            runGit directory
                [ "-c"; "user.name=FS.GG Test"; "-c"; "user.email=test@fs.gg"
                  "commit"; "-m"; "add v2 registry" ]
            |> ignore
            let actualHead = runGit directory [ "rev-parse"; "HEAD" ]
            runGit directory [ "switch"; "main" ] |> ignore
            runGit directory
                [ "-c"; "user.name=FS.GG Test"; "-c"; "user.email=test@fs.gg"
                  "merge"; "--no-ff"; "loss-registry"; "-m"; "merge v2 registry" ]
            |> ignore
            let actualMerge = runGit directory [ "rev-parse"; "HEAD" ]
            let actualBlob =
                runGit directory [ "rev-parse"; $"{actualMerge}:{item.Approval.RegistryPath}" ]
            let mergedBytes = File.ReadAllBytes registryFile
            Assert.Equal<byte>(bytes, mergedBytes)
            let reviews = reviewChainFor actualBase actualHead actualBlob
            let acceptedReviewDigest = reviews |> List.last |> _.Digest
            let reviewComments =
                reviews |> List.mapi (fun index record -> comment (int64 index + 1L) record)
            let envelopeBody =
                approvalEnvelopeBody
                    item
                    actualHead
                    actualMerge
                    actualBlob
                    (sha256 bytes)
                    acceptedReviewDigest
            let envelope = approvalEnvelopeComment "2026-09-27T01:03:00.0000000+00:00" envelopeBody
            let pr: HistoricalLossRegistry.NativePullRequest =
                {
                    Repository = "FS-GG/.github"
                    PullRequest = 4001
                    State = "closed"
                    Merged = true
                    BaseSha = actualBase
                    HeadSha = actualHead
                    MergeCommitSha = actualMerge
                }
            let native: HistoricalLossRegistry.NativeApprovalReadbackV2 =
                {
                    PullRequestFirst =
                        { PullRequest = pr; MergedAt = "2026-09-27T01:02:00.0000000+00:00" }
                    PullRequestSecond =
                        { PullRequest = pr; MergedAt = "2026-09-27T01:02:00.0000000+00:00" }
                    ReviewCommentsFirst = reviewComments
                    ReviewCommentsSecond = reviewComments
                    ReviewCommentsComplete = true
                    ReviewCommentsTerminal = true
                    ApprovalEnvelopeFirst = envelope
                    ApprovalEnvelopeSecond = envelope
                    File =
                        {
                            Repository = "FS-GG/.github"
                            Path = item.Approval.RegistryPath
                            Revision = actualMerge
                            BlobSha = actualBlob
                            Bytes = bytes
                            BytesSha256 = sha256 bytes
                        }
                    Blob =
                        {
                            Repository = "FS-GG/.github"
                            BlobSha = actualBlob
                            Bytes = bytes
                            BytesSha256 = sha256 bytes
                        }
                }

            Assert.Equal(
                Ok(
                    ({ Schema = HistoricalLossRegistry.SchemaV2; Entries = [ item ] }
                    : HistoricalLossRegistry.RegistryV2)
                ),
                HistoricalLossRegistry.parseV2 (Encoding.UTF8.GetString bytes)
            )

            match HistoricalLossRegistry.bindV2 item.Family item.Scope item.Cutoff bytes item native with
            | Ok bound -> Assert.Equal(envelope.BodySha256, bound.ApprovalDigest)
            | Error errors -> failwithf "real git v2 evidence refused: %A" errors

            let registryText = Encoding.UTF8.GetString bytes
            Assert.DoesNotContain(actualHead, registryText)
            Assert.DoesNotContain(actualMerge, registryText)
            Assert.DoesNotContain(actualBlob, registryText)
            Assert.DoesNotContain(sha256 bytes, registryText)

            let tamperedBody = envelopeBody.Replace(actualHead, hex '9' 40)
            let tamperedEnvelope =
                approvalEnvelopeComment "2026-09-27T01:03:00.0000000+00:00" tamperedBody
            let tampered =
                { native with
                    ApprovalEnvelopeFirst = tamperedEnvelope
                    ApprovalEnvelopeSecond = tamperedEnvelope }
            let tamperErrors =
                HistoricalLossRegistry.bindV2 item.Family item.Scope item.Cutoff bytes item tampered
                |> expectError
            Assert.Contains("historical-loss-approval-envelope-binding", tamperErrors)

            let wrongMergeBody = envelopeBody.Replace(actualMerge, hex '8' 40)
            let wrongMergeEnvelope =
                approvalEnvelopeComment "2026-09-27T01:03:00.0000000+00:00" wrongMergeBody
            let wrongMerge =
                { native with
                    ApprovalEnvelopeFirst = wrongMergeEnvelope
                    ApprovalEnvelopeSecond = wrongMergeEnvelope }
            let mergeErrors =
                HistoricalLossRegistry.bindV2 item.Family item.Scope item.Cutoff bytes item wrongMerge
                |> expectError
            Assert.Contains("historical-loss-approval-envelope-binding", mergeErrors)

            let changedEnvelopeBytes =
                { native with
                    ApprovalEnvelopeFirst =
                        { envelope with BodySha256 = hex '7' 64 }
                    ApprovalEnvelopeSecond =
                        { envelope with BodySha256 = hex '7' 64 } }
            let envelopeErrors =
                HistoricalLossRegistry.bindV2
                    item.Family item.Scope item.Cutoff bytes item changedEnvelopeBytes
                |> expectError
            Assert.Contains("historical-loss-approval-envelope-readback", envelopeErrors)

            let prematureEnvelope =
                { envelope with CreatedAt = native.PullRequestFirst.MergedAt }
            let premature =
                { native with
                    ApprovalEnvelopeFirst = prematureEnvelope
                    ApprovalEnvelopeSecond = prematureEnvelope }
            let chronologyErrors =
                HistoricalLossRegistry.bindV2 item.Family item.Scope item.Cutoff bytes item premature
                |> expectError
            Assert.Contains("historical-loss-approval-envelope-not-post-merge", chronologyErrors)

            let changedBytes = Encoding.UTF8.GetBytes("changed")
            let changed =
                { native with
                    File = { native.File with Bytes = changedBytes; BytesSha256 = sha256 changedBytes } }
            let contentErrors =
                HistoricalLossRegistry.bindV2 item.Family item.Scope item.Cutoff bytes item changed
                |> expectError
            Assert.Contains("historical-loss-file-readback", contentErrors)
        finally
            Directory.Delete(directory, true)

    [<Fact>]
    let ``unknown fields and caller invented protected producer roles do not parse`` () =
        let item = entry "delivery-receipt" []
        let raw = registryJson item

        Assert.True(
            HistoricalLossRegistry.parse (raw.Replace("\"entries\":", "\"unknown\":true,\"entries\":"))
            |> Result.isError
        )

        Assert.True(
            HistoricalLossRegistry.parse (raw.Replace("protected-parser-only", "protected-producer"))
            |> Result.isError
        )

    [<Fact>]
    let ``scope cutoff census and native approval drift all refuse`` () =
        let item = entry "intake-receipt" []
        let bytes, native = fixture item

        let drifted =
            { native with
                PullRequestSecond =
                    { native.PullRequestSecond with
                        HeadSha = hex '9' 40
                    }
            }

        let errors =
            HistoricalLossRegistry.bind item.Family "FS-GG/other" "2026-09-28T00:00:00.0000000+00:00" bytes item drifted
            |> expectError

        Assert.Contains("historical-loss-scope-mismatch", errors)
        Assert.Contains("historical-loss-cutoff-mismatch", errors)
        Assert.Contains("historical-loss-pr-drift", errors)

    [<Fact>]
    let ``sessionless claim evidence is explicitly refused before family scope`` () =
        let survivor: HistoricalLossRegistry.NativeSurvivor =
            {
                NativeId = "issue-comment:1"
                Family = "claim-marker"
                PayloadBlobSha = hex '2' 40
                SessionOperationId = None
            }

        let item = entry "claim-marker" [ survivor ]
        let bytes, native = fixture item

        let errors =
            HistoricalLossRegistry.bind item.Family item.Scope item.Cutoff bytes item native
            |> expectError

        Assert.Contains("historical-loss-sessionless-claim-refused", errors)
        Assert.Contains("historical-loss-family-out-of-scope", errors)

    [<Fact>]
    let ``historical review shape and absent blob receipt cannot approve`` () =
        let item = entry "legacy-done-receipt" []
        let bytes, native = fixture item
        let comments = native.ReviewCommentsFirst
        let accepted = reviewChain native.File.BlobSha |> List.last

        let historical =
            { accepted with
                ClaimGeneration = None
                BaseSha = None
                DiffAuditReceipts = []
                Digest = ""
            }
            |> reseal

        let replaced = [ comments.Head; comment 2L historical ]

        let changed =
            { native with
                ReviewCommentsFirst = replaced
                ReviewCommentsSecond = replaced
            }

        let errors =
            HistoricalLossRegistry.bind item.Family item.Scope item.Cutoff bytes item changed
            |> expectError

        Assert.Contains("historical-loss-review-not-approved", errors)

    [<Fact>]
    let ``incomplete census and changed file bytes refuse`` () =
        let original = entry "delivery-receipt" []

        let incompleteDraft =
            { original.CensusFirst with
                Complete = false
                Digest = ""
            }

        let incomplete =
            { incompleteDraft with
                Digest = HistoricalLossRegistry.censusDigest incompleteDraft
            }

        let item =
            { original with
                CensusFirst = incomplete
                CensusSecond = incomplete
            }

        let bytes, native = fixture item

        let changed =
            { native with
                File =
                    { native.File with
                        Bytes = Encoding.UTF8.GetBytes("changed")
                    }
            }

        let errors =
            HistoricalLossRegistry.bind item.Family item.Scope item.Cutoff bytes item changed
            |> expectError

        Assert.Contains("historical-loss-census-incomplete", errors)
        Assert.Contains("historical-loss-file-readback", errors)

    let private v3Repository: HistoricalLossRegistry.RepositoryIdentityV3 =
        { FullName = "FS-GG/FS.GG.Coordination"; DatabaseId = 1353050537L; NodeId = "R_fixture_coordination" }

    let private v3Census enumeration subjects =
        let draft: HistoricalLossRegistry.RetainedNativeCensusV3 =
            {
                SelectedRepositories = [ v3Repository ]
                ObservationHorizon = cutoff
                Revision = sourceRevision
                Enumeration = enumeration
                Complete = true
                DeclaredCount = List.length subjects
                Pages =
                    [
                        {
                            Repository = v3Repository
                            Index = 1
                            ItemCount = List.length subjects
                            RawSha256 = hex '6' 64
                            Terminal = true
                        }
                    ]
                Subjects = subjects
                HistoricalEmissions = "unknown"
                HistoricalDeletions = "unknown"
                LostCount = "unknown"
                ProducerDeploymentEnd = "unknown"
                Digest = ""
            }

        { draft with Digest = HistoricalLossRegistry.retainedCensusDigestV3 draft }

    let private v3Entry () =
        let subjectRow: HistoricalLossRegistry.RetainedSubjectV3 =
            {
                Repository = v3Repository
                NativeId = "retained-subject:fixture-1"
                Family = "delivery-receipt"
                CreatedAt = "2026-09-26T23:00:00.0000000+00:00"
                PayloadBlobSha = hex '2' 40
                SessionOperationId = Some "delivery-operation-197"
                LiveClaim = false
            }
        let retained = v3Census HistoricalLossRegistry.DirectRepositoryEnumeration [ subjectRow ]

        ({
            Family = "delivery-receipt"
            Scope = scope
            ObservationHorizon = cutoff
            RecoverySources =
                [
                    {
                        ProducerId = "recovered-writer"
                        Revision = sourceRevision
                        Path = "scripts/legacy-writer.py"
                        BlobSha = sourceBlob
                        BytesSha256 = hex '3' 64
                        Role = HistoricalLossRegistry.RecoveredWriterSource
                    }
                    {
                        ProducerId = "protocol-author"
                        Revision = sourceRevision
                        Path = "docs/receipt-protocol.md"
                        BlobSha = hex '4' 40
                        BytesSha256 = hex '5' 64
                        Role = HistoricalLossRegistry.ProtocolAuthoringSource
                    }
                ]
            KnownSurvivorIds = [ subjectRow.NativeId ]
            CensusFirst = retained
            CensusSecond = retained
            ExclusionAppliesToLiveClaims = false
            Consequence = HistoricalLossRegistry.RequiredConsequence
            Approval =
                {
                    Subject = subject
                    PullRequest = 4001
                    BaseSha = baseSha
                    RegistryPath = "policy/historical-loss-registry.json"
                }
        }: HistoricalLossRegistry.EntryV3)

    let private registryJsonV3 (item: HistoricalLossRegistry.EntryV3) =
        let renderRole = function
            | HistoricalLossRegistry.RecoveredWriterSource -> "recovered-writer-source"
            | HistoricalLossRegistry.ProtocolAuthoringSource -> "protocol-authoring-source"
        let renderSource (source: HistoricalLossRegistry.RecoverySource) =
            $"{{\"producerId\":{quoted source.ProducerId},\"revision\":{quoted source.Revision},\"path\":{quoted source.Path},\"blobSha\":{quoted source.BlobSha},\"bytesSha256\":{quoted source.BytesSha256},\"role\":{quoted (renderRole source.Role)}}}"
        let renderEnumeration = function
            | HistoricalLossRegistry.DirectRepositoryEnumeration -> "direct-repository-enumeration"
            | HistoricalLossRegistry.SearchOnly -> "search-only"
            | HistoricalLossRegistry.AuditNotFoundInference -> "audit-not-found-inference"
        let renderRepository (repository: HistoricalLossRegistry.RepositoryIdentityV3) =
            $"{{\"fullName\":{quoted repository.FullName},\"databaseId\":{repository.DatabaseId},\"nodeId\":{quoted repository.NodeId}}}"
        let renderPage (page: HistoricalLossRegistry.RetainedPageV3) =
            $"{{\"repository\":{renderRepository page.Repository},\"index\":{page.Index},\"itemCount\":{page.ItemCount},\"rawSha256\":{quoted page.RawSha256},\"terminal\":{page.Terminal.ToString().ToLowerInvariant()}}}"
        let renderSubject (retained: HistoricalLossRegistry.RetainedSubjectV3) =
            let operation = retained.SessionOperationId |> Option.map quoted |> Option.defaultValue "null"
            $"{{\"repository\":{renderRepository retained.Repository},\"nativeId\":{quoted retained.NativeId},\"family\":{quoted retained.Family},\"createdAt\":{quoted retained.CreatedAt},\"payloadBlobSha\":{quoted retained.PayloadBlobSha},\"sessionOperationId\":{operation},\"liveClaim\":{retained.LiveClaim.ToString().ToLowerInvariant()}}}"
        let renderCensus (value: HistoricalLossRegistry.RetainedNativeCensusV3) =
            let repositories = value.SelectedRepositories |> List.map renderRepository |> String.concat ","
            let pages = value.Pages |> List.map renderPage |> String.concat ","
            let retained = value.Subjects |> List.map renderSubject |> String.concat ","
            $"{{\"selectedRepositories\":[{repositories}],\"observationHorizon\":{quoted value.ObservationHorizon},\"revision\":{quoted value.Revision},\"enumeration\":{quoted (renderEnumeration value.Enumeration)},\"complete\":{value.Complete.ToString().ToLowerInvariant()},\"declaredCount\":{value.DeclaredCount},\"pages\":[{pages}],\"subjects\":[{retained}],\"historicalEmissions\":{quoted value.HistoricalEmissions},\"historicalDeletions\":{quoted value.HistoricalDeletions},\"lostCount\":{quoted value.LostCount},\"producerDeploymentEnd\":{quoted value.ProducerDeploymentEnd},\"digest\":{quoted value.Digest}}}"
        let sources = item.RecoverySources |> List.map renderSource |> String.concat ","
        let known = item.KnownSurvivorIds |> List.map quoted |> String.concat ","
        $"{{\"schema\":{quoted HistoricalLossRegistry.SchemaV3},\"entries\":[{{\"family\":{quoted item.Family},\"scope\":{quoted item.Scope},\"observationHorizon\":{quoted item.ObservationHorizon},\"recoverySources\":[{sources}],\"knownSurvivorIds\":[{known}],\"censusFirst\":{renderCensus item.CensusFirst},\"censusSecond\":{renderCensus item.CensusSecond},\"exclusionAppliesToLiveClaims\":{item.ExclusionAppliesToLiveClaims.ToString().ToLowerInvariant()},\"consequence\":{quoted item.Consequence},\"approval\":{{\"subject\":{quoted item.Approval.Subject},\"pullRequest\":{item.Approval.PullRequest},\"baseSha\":{quoted item.Approval.BaseSha},\"registryPath\":{quoted item.Approval.RegistryPath}}}}}]}}"

    let private v3Fixture item =
        let bytes = registryJsonV3 item |> Encoding.UTF8.GetBytes
        let blobSha = gitBlobSha bytes
        let decisions = reviewChain blobSha
        let reviewComments = decisions |> List.mapi (fun index record -> comment (int64 index + 1L) record)
        let accepted = decisions |> List.last |> _.Digest
        let envelopeBody =
            "<!-- fsgg:historical-loss-approval/v2 -->\n"
            + $"{{\"schema\":{quoted HistoricalLossRegistry.ApprovalEnvelopeSchemaV2},\"subject\":{quoted item.Approval.Subject},\"pullRequest\":{item.Approval.PullRequest},\"baseSha\":{quoted item.Approval.BaseSha},\"reviewedHeadSha\":{quoted headSha},\"mergeCommitSha\":{quoted mergeSha},\"registryPath\":{quoted item.Approval.RegistryPath},\"registryBlobSha\":{quoted blobSha},\"registryBytesSha256\":{quoted (sha256 bytes)},\"acceptedReviewDigest\":{quoted accepted}}}"
        let envelope = approvalEnvelopeComment "2026-09-27T01:03:00.0000000+00:00" envelopeBody
        let pr: HistoricalLossRegistry.NativePullRequest =
            { Repository = "FS-GG/.github"; PullRequest = 4001; State = "closed"; Merged = true; BaseSha = baseSha; HeadSha = headSha; MergeCommitSha = mergeSha }
        let native: HistoricalLossRegistry.NativeApprovalReadbackV2 =
            {
                PullRequestFirst = { PullRequest = pr; MergedAt = "2026-09-27T01:02:00.0000000+00:00" }
                PullRequestSecond = { PullRequest = pr; MergedAt = "2026-09-27T01:02:00.0000000+00:00" }
                ReviewCommentsFirst = reviewComments
                ReviewCommentsSecond = reviewComments
                ReviewCommentsComplete = true
                ReviewCommentsTerminal = true
                ApprovalEnvelopeFirst = envelope
                ApprovalEnvelopeSecond = envelope
                File = { Repository = "FS-GG/.github"; Path = item.Approval.RegistryPath; Revision = mergeSha; BlobSha = blobSha; Bytes = bytes; BytesSha256 = sha256 bytes }
                Blob = { Repository = "FS-GG/.github"; BlobSha = blobSha; Bytes = bytes; BytesSha256 = sha256 bytes }
            }
        bytes, native

    let private bindV3
        (item: HistoricalLossRegistry.EntryV3)
        (bytes: byte array)
        (native: HistoricalLossRegistry.NativeApprovalReadbackV2)
        =
        HistoricalLossRegistry.bindV3 item.Family item.Scope item.ObservationHorizon [ v3Repository ] bytes item native

    [<Fact>]
    let ``v3 positive binding stays unavailable without native census proof`` () =
        let item = v3Entry ()
        let bytes, native = v3Fixture item
        Assert.Equal(Ok({ Schema = HistoricalLossRegistry.SchemaV3; Entries = [ item ] }: HistoricalLossRegistry.RegistryV3), HistoricalLossRegistry.parseV3 (Encoding.UTF8.GetString bytes))
        Assert.Contains("historical-loss-native-census-proof-unavailable", bindV3 item bytes native |> expectError)

    [<Fact>]
    let ``v3 malformed observation horizon returns errors instead of throwing`` () =
        let original = v3Entry ()
        let malformed = "not-an-instant"
        let draft = { original.CensusFirst with ObservationHorizon = malformed; Digest = "" }
        let census = { draft with Digest = HistoricalLossRegistry.retainedCensusDigestV3 draft }
        let item =
            { original with
                ObservationHorizon = malformed
                CensusFirst = census
                CensusSecond = census }
        let bytes, native = v3Fixture item
        let errors = bindV3 item bytes native |> expectError
        Assert.Contains("historical-loss-observation-horizon-invalid", errors)
        Assert.Contains("historical-loss-retained-census-incomplete", errors)
        Assert.Contains("historical-loss-native-census-proof-unavailable", errors)

    [<Fact>]
    let ``v3 validates pagination independently for each repository before proof refusal`` () =
        let original = v3Entry ()
        let repositoryB: HistoricalLossRegistry.RepositoryIdentityV3 =
            { FullName = "FS-GG/FS.GG.Tools"; DatabaseId = 1353050538L; NodeId = "R_fixture_tools" }
        let subjectA = original.CensusFirst.Subjects.Head
        let subjectB = { subjectA with Repository = repositoryB; NativeId = "retained-subject:fixture-2" }
        let draft =
            { original.CensusFirst with
                SelectedRepositories = [ v3Repository; repositoryB ]
                DeclaredCount = 2
                Pages =
                    [
                        { Repository = v3Repository; Index = 1; ItemCount = 0; RawSha256 = hex '6' 64; Terminal = false }
                        { Repository = v3Repository; Index = 2; ItemCount = 1; RawSha256 = hex '7' 64; Terminal = true }
                        { Repository = repositoryB; Index = 1; ItemCount = 0; RawSha256 = hex '8' 64; Terminal = false }
                        { Repository = repositoryB; Index = 2; ItemCount = 1; RawSha256 = hex '9' 64; Terminal = true }
                    ]
                Subjects = [ subjectA; subjectB ]
                Digest = "" }
        let census = { draft with Digest = HistoricalLossRegistry.retainedCensusDigestV3 draft }
        let item =
            { original with
                KnownSurvivorIds = [ subjectA.NativeId; subjectB.NativeId ]
                CensusFirst = census
                CensusSecond = census }
        let bytes, native = v3Fixture item
        let errors =
            HistoricalLossRegistry.bindV3 item.Family item.Scope item.ObservationHorizon [ v3Repository; repositoryB ] bytes item native
            |> expectError
        Assert.Equal<string list>([ "historical-loss-native-census-proof-unavailable" ], errors)

        let missingPageDraft = { census with Pages = census.Pages |> List.removeAt 1; Digest = "" }
        let missingPage = { missingPageDraft with Digest = HistoricalLossRegistry.retainedCensusDigestV3 missingPageDraft }
        let missingItem = { item with CensusFirst = missingPage; CensusSecond = missingPage }
        let missingBytes, missingNative = v3Fixture missingItem
        let missingErrors =
            HistoricalLossRegistry.bindV3 missingItem.Family missingItem.Scope missingItem.ObservationHorizon [ v3Repository; repositoryB ] missingBytes missingItem missingNative
            |> expectError
        Assert.Contains("historical-loss-retained-census-incomplete", missingErrors)

        let duplicatePageDraft = { census with Pages = census.Pages.Head :: census.Pages; Digest = "" }
        let duplicatePage = { duplicatePageDraft with Digest = HistoricalLossRegistry.retainedCensusDigestV3 duplicatePageDraft }
        let duplicateItem = { item with CensusFirst = duplicatePage; CensusSecond = duplicatePage }
        let duplicateBytes, duplicateNative = v3Fixture duplicateItem
        let duplicateErrors =
            HistoricalLossRegistry.bindV3 duplicateItem.Family duplicateItem.Scope duplicateItem.ObservationHorizon [ v3Repository; repositoryB ] duplicateBytes duplicateItem duplicateNative
            |> expectError
        Assert.Contains("historical-loss-retained-census-incomplete", duplicateErrors)

    [<Fact>]
    let ``v3 represents an observed-zero intake census without a fabricated writer or survivor`` () =
        let original = v3Entry ()
        let draft =
            { original.CensusFirst with
                DeclaredCount = 0
                Pages = [ { original.CensusFirst.Pages.Head with ItemCount = 0 } ]
                Subjects = []
                Digest = "" }
        let census = { draft with Digest = HistoricalLossRegistry.retainedCensusDigestV3 draft }
        let item =
            { original with
                Family = "intake-receipt"
                RecoverySources = original.RecoverySources |> List.filter (fun source -> source.Role = HistoricalLossRegistry.ProtocolAuthoringSource)
                KnownSurvivorIds = []
                CensusFirst = census
                CensusSecond = census }
        let bytes, native = v3Fixture item
        let errors =
            HistoricalLossRegistry.bindV3 item.Family item.Scope item.ObservationHorizon [ v3Repository ] bytes item native
            |> expectError
        Assert.Equal<string list>([ "historical-loss-native-census-proof-unavailable" ], errors)

    [<Fact>]
    let ``v3 refuses search audit absence pagination loss and missing known survivors`` () =
        let original = v3Entry ()
        let cases: (string * (HistoricalLossRegistry.EntryV3 -> HistoricalLossRegistry.EntryV3)) list =
            [
                "historical-loss-retained-census-incomplete", fun item ->
                    let census = v3Census HistoricalLossRegistry.SearchOnly item.CensusFirst.Subjects
                    { item with CensusFirst = census; CensusSecond = census }
                "historical-loss-retained-census-incomplete", fun item ->
                    let census = v3Census HistoricalLossRegistry.AuditNotFoundInference []
                    { item with CensusFirst = census; CensusSecond = census }
                "historical-loss-retained-census-incomplete", fun item ->
                    let draft = { item.CensusFirst with Pages = [ { item.CensusFirst.Pages.Head with Terminal = false } ]; Digest = "" }
                    let census = { draft with Digest = HistoricalLossRegistry.retainedCensusDigestV3 draft }
                    { item with CensusFirst = census; CensusSecond = census }
                "historical-loss-known-survivor-missing", fun item -> { item with KnownSurvivorIds = [ "issue-comment:missing" ] }
            ]
        for expected, change in cases do
            let item = change original
            let bytes, native = v3Fixture item
            Assert.Contains(expected, bindV3 item bytes native |> expectError)

    [<Fact>]
    let ``v3 refuses foreign or post-horizon subjects historical guesses and live-claim exclusion`` () =
        let original = v3Entry ()
        let resealCensus
            (item: HistoricalLossRegistry.EntryV3)
            (change: HistoricalLossRegistry.RetainedNativeCensusV3 -> HistoricalLossRegistry.RetainedNativeCensusV3)
            =
            let draft = change item.CensusFirst |> fun value -> { value with Digest = "" }
            let census = { draft with Digest = HistoricalLossRegistry.retainedCensusDigestV3 draft }
            { item with CensusFirst = census; CensusSecond = census }
        let cases: (string * (HistoricalLossRegistry.EntryV3 -> HistoricalLossRegistry.EntryV3)) list =
            [
                "historical-loss-retained-census-incomplete", fun item -> resealCensus item (fun census -> { census with Subjects = [ { census.Subjects.Head with Repository = { census.Subjects.Head.Repository with FullName = "other/repo" } } ] })
                "historical-loss-retained-census-incomplete", fun item -> resealCensus item (fun census -> { census with Subjects = [ { census.Subjects.Head with Family = "intake-receipt" } ] })
                "historical-loss-retained-census-incomplete", fun item -> resealCensus item (fun census -> { census with Subjects = [ { census.Subjects.Head with CreatedAt = "2026-09-28T00:00:00.0000000+00:00" } ] })
                "historical-loss-retained-census-incomplete", fun item -> resealCensus item (fun census -> { census with ProducerDeploymentEnd = cutoff })
                "historical-loss-retained-census-incomplete", fun item -> resealCensus item (fun census -> { census with HistoricalEmissions = "197" })
                "historical-loss-retained-census-incomplete", fun item -> resealCensus item (fun census -> { census with HistoricalDeletions = "0" })
                "historical-loss-retained-census-incomplete", fun item -> resealCensus item (fun census -> { census with LostCount = "134" })
                "historical-loss-retained-census-incomplete", fun item -> resealCensus item (fun census -> { census with Subjects = [ { census.Subjects.Head with LiveClaim = true } ] })
                "historical-loss-live-claim-exclusion", fun item -> { item with ExclusionAppliesToLiveClaims = true }
            ]
        for expected, change in cases do
            let item = change original
            let bytes, native = v3Fixture item
            Assert.Contains(expected, bindV3 item bytes native |> expectError)

    [<Fact>]
    let ``v3 detached approval still refuses a forged reviewed head`` () =
        let item = v3Entry ()
        let bytes, native = v3Fixture item
        let forgedBody = native.ApprovalEnvelopeFirst.Body.Replace(headSha, hex '9' 40)
        let forged = approvalEnvelopeComment native.ApprovalEnvelopeFirst.CreatedAt forgedBody
        let changed = { native with ApprovalEnvelopeFirst = forged; ApprovalEnvelopeSecond = forged }
        Assert.Contains("historical-loss-approval-envelope-binding", bindV3 item bytes changed |> expectError)
