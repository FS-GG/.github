namespace FS.GG.Coord.Tests

open System
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

    let private reseal record =
        let draft =
            { record with
                StructuredDecision.ReviewRecord.Digest = ""
            }

        { draft with
            Digest = StructuredDecision.reviewDigest draft
        }

    let private reviewChain blobSha =
        let initial =
            ({
                Schema = StructuredDecision.ReviewSchema
                Subject = subject
                Revision = 1
                PreviousDigest = None
                HeadSha = headSha
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
                BaseSha = Some baseSha
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
