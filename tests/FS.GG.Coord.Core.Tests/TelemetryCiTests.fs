namespace FS.GG.Coord.Tests

open System.Text
open Xunit
open FS.GG.Coord

module TelemetryCiTests =
    let private interval startAt endAt =
        TelemetryCi.interval (Some startAt) (Some endAt) |> Option.get

    let private assignment (value: string) =
        value |> Encoding.UTF8.GetBytes |> TelemetryCi.parseAssignment

    [<Fact>]
    let ``UTEL-06D CI assignment accepts only its literal schema and safe identifiers`` () =
        let valid =
            """{"schema":"fsgg.telemetry.ci-assignment/1","featureId":"UTEL-06","itemId":"UTEL-06.4","attemptId":"attempt-1","parentAttemptId":null,"producerStream":"routine-delivery"}"""

        Assert.True(assignment valid |> Result.isOk)

        Assert.True(
            assignment (valid.Replace("ci-assignment/1", "ci-assignment/2"))
            |> Result.isError
        )

        Assert.True(
            assignment (valid.Replace("\"producerStream\"", "\"verdict\":\"pass\",\"producerStream\""))
            |> Result.isError
        )

        Assert.True(assignment (valid.Replace("UTEL-06.4", "../unsafe")) |> Result.isError)

    [<Fact>]
    let ``UTEL-04A parallel jobs sum runner time but union wall time`` () =
        let jobs =
            [
                interval "2026-01-01T00:00:00Z" "2026-01-01T00:01:00Z"
                interval "2026-01-01T00:00:00Z" "2026-01-01T00:00:40Z"
            ]

        Assert.Equal(Some 60L, TelemetryCi.unionSeconds jobs)

        Assert.Equal(
            100L,
            jobs
            |> List.sumBy (fun value -> int64 (value.EndUtc - value.StartUtc).TotalSeconds)
        )

    [<Fact>]
    let ``UTEL-04A administrative time subtracts useful overlap from wait union`` () =
        let waits =
            [
                interval "2026-01-01T00:00:00Z" "2026-01-01T00:02:00Z"
                interval "2026-01-01T00:00:30Z" "2026-01-01T00:01:30Z"
            ]

        let usefulAndProductive =
            [
                interval "2026-01-01T00:00:20Z" "2026-01-01T00:01:40Z"
                interval "2026-01-01T00:00:00Z" "2026-01-01T00:00:10Z"
            ]

        Assert.Equal(Some 30L, TelemetryCi.subtractSeconds waits usefulAndProductive)

    [<Fact>]
    let ``UTEL-04A missing or reversed endpoints stay unknown`` () =
        Assert.Equal(None, TelemetryCi.interval None (Some "2026-01-01T00:00:00Z"))
        Assert.Equal(None, TelemetryCi.interval (Some "2026-01-01T00:01:00Z") (Some "2026-01-01T00:00:00Z"))

    [<Fact>]
    let ``UTEL-06.8 correction contract is closed bounded and roundtrips exact assignment and predecessor`` () =
        let assignment feature item attempt : TelemetryCi.Assignment =
            { FeatureId = feature; ItemId = item; AttemptId = attempt; ParentAttemptId = None; ProducerStream = "routine-delivery" }
        let request : TelemetryCi.CorrectionRequest =
            { CorrectionId = "repair-one"; ExpectedPredecessor = None; Repository = "o/r"; PullRequest = 7L
              BaseRef = "main"; BaseSha = String.replicate 40 "d"; Head = String.replicate 40 "a"; MergeCommit = String.replicate 40 "b"
              Prior = assignment "wrong" "wrong-item" "wrong-attempt"; Effective = assignment "genuine" "genuine-item" "genuine-attempt"
              EvidenceSha256 = String.replicate 64 "e"; Reason = "source proof"; OperatorSource = "private-proof"; ObservedAt = "2026-10-06T12:00:00Z" }
        let plan : TelemetryCi.CorrectionPlan =
            { StoreId = String.replicate 32 "f"; Request = request
              Targets = [ { Table = "native_item_outcomes"; Identity = "native-one"; Revision = 1L; Digest = String.replicate 64 "c" } ] }
        let json = TelemetryCi.correctionPlanJson plan
        Assert.Equal(Ok plan, TelemetryCi.parseCorrectionPlan (Encoding.UTF8.GetBytes json))
        for invalid in
            [ json.Replace("\"storeId\":", "\"extra\":true,\"storeId\":")
              json.Replace("\"storeId\":", "\"storeId\":\"duplicate\",\"storeId\":")
              json.Replace("native_item_outcomes", "usage_observations")
              json.Replace("genuine-item", "../unsafe")
              json.Replace("ci-correction-plan/1", "ci-correction-plan/2")
              json.Replace("ci-correction-request/1", "ci-correction-request/2")
              json.Replace("\"prior\":", "\"prior\":null,\"prior\":") ] do
            Assert.True(TelemetryCi.parseCorrectionPlan (Encoding.UTF8.GetBytes invalid) |> Result.isError)

module ProviderCheckpointTests =
    open System
    open System.Text
    open FS.GG.Coord.TelemetryCi.ProviderCheckpoint

    let private now = DateTimeOffset.Parse "2026-10-12T00:00:00Z"
    let private hash value = Encoding.UTF8.GetBytes(value: string) |> digest
    let private identity =
        {
            Assignment = { FeatureId = "UTEL"; ItemId = "UTEL-06.3"; AttemptId = "fixture"; ParentAttemptId = None; ProducerStream = "routine-delivery" };
            AssignmentDigest = hash "assignment";
            StoreId = "store-1";
            Collection = "collection-1";
            ApiOrigin = "https://api.github.com";
            Repository = "FS-GG/.github";
            RepositoryId = 123L;
            PullRequest = 4435L;
            BaseRef = "main";
            BaseSha = String.replicate 40 "a";
            Head = String.replicate 40 "b";
            ProfileDigest = hash "profile";
            ParserContract = "ci-provider/1";
        }
    let private resource =
        {
            Key = { Kind = Admission; RunId = 0L; SuiteId = 0L; Attempt = 0; Page = 1 };
            Body = "{}";
            BodyDigest = hash "{}";
            Validator = "\"page-1\"";
            Next = "";
            ProjectionDigest = hash "projection";
            ObservedAt = now;
            ValidatedAt = now;
        }
    let private checkpoint =
        {
            Identity = identity;
            Generation = 1L;
            CreatedAt = now;
            ObservedThrough = now;
            PriorCheckpointDigest = "";
            CandidateDigest = identityDigest identity;
            Phase = Acquiring;
            Cursor = 0;
            Resources = [ resource ];
            FirstSweepDigest = "";
            SecondSweepDigest = "";
            Revision = 0L;
            PendingBatchDigest = "";
            PendingBatchBase64 = "";
            FactReferences = [];
        }
    let private encoded value =
        match encode identity now value with
        | Ok bytes -> bytes
        | Error errors -> failwith (String.concat "," errors)
    let private refused value = Assert.True(validate identity now value |> Result.isError)

    [<Fact>]
    let ``provider checkpoint roundtrip retains exact page body and private cursor`` () =
        let actual = parse identity now (encoded checkpoint)
        Assert.Equal<Result<Checkpoint, string list>>(Ok checkpoint, actual)

    [<Fact>]
    let ``provider checkpoint refuses foreign profile head base and assignment`` () =
        for changed in [ { identity with ProfileDigest = hash "foreign" }
                         { identity with Head = String.replicate 40 "c" }
                         { identity with BaseSha = String.replicate 40 "d" }
                         { identity with AssignmentDigest = hash "another" } ] do
            refused { checkpoint with Identity = changed }

    [<Fact>]
    let ``provider checkpoint refuses changed body duplicate resource and malformed key`` () =
        refused { checkpoint with Resources = [ { resource with Body = "{\"changed\":true}" } ] }
        refused { checkpoint with Resources = [ resource; resource ] }
        refused { checkpoint with Resources = [ { resource with Key = { resource.Key with Page = 2 } } ] }

    [<Fact>]
    let ``provider checkpoint rejects unknown duplicate and missing codec fields`` () =
        let text = Encoding.UTF8.GetString(encoded checkpoint)
        for changed in [ text.Replace("\"schema\":", "\"extra\":true,\"schema\":")
                         text.Replace("\"generation\":1", "\"generation\":1,\"generation\":1")
                         text.Replace("\"cursor\":0,", "") ] do
            Assert.True(parse identity now (Encoding.UTF8.GetBytes changed) |> Result.isError)

    [<Fact>]
    let ``provider checkpoint expiry and future observation never authorize cached data`` () =
        Assert.True(parse identity (now.AddDays 2.0) (encoded checkpoint) |> Result.isError)
        refused { checkpoint with CreatedAt = now.AddSeconds 1.0 }
        refused { checkpoint with Resources = [ { resource with ValidatedAt = now.AddSeconds 1.0 } ] }

    [<Fact>]
    let ``provider checkpoint stability requires two exact full population digests`` () =
        let complete = populationDigest checkpoint.Resources
        let stable =
            { checkpoint with
                Phase = Stable;
                Cursor = 1;
                Revision = 1L;
                FirstSweepDigest = complete;
                SecondSweepDigest = complete;
            }
        Assert.True(validate identity now stable |> Result.isOk)
        refused { stable with Cursor = 0 }
        refused { stable with SecondSweepDigest = hash "older" }
        refused { stable with Resources = [ { resource with Next = "new-page" } ] }
        refused { stable with Phase = PublicationPending }
        Assert.True(validate identity now { stable with Phase = PublicationPending; PendingBatchDigest = hash "batch"; PendingBatchBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes "batch") } |> Result.isOk)

    [<Fact>]
    let ``provider resource fingerprint detects same count replacement and changed page topology`` () =
        let original = populationDigest [ resource ]
        Assert.NotEqual<string>(original, populationDigest [ { resource with ProjectionDigest = hash "same-count-different-id" } ])
        Assert.NotEqual<string>(original, populationDigest [ { resource with Next = "https://api.github.com/new-page" } ])
        Assert.Equal(original, populationDigest [ { resource with ValidatedAt = now.AddSeconds 1.0 } ])

    [<Fact>]
    let ``provider logical revision exceeds both stored floors and refuses overflow`` () =
        Assert.Equal<Result<int64, string list>>(Ok 12L, nextRevision 8L 11L)
        Assert.Equal<Result<int64, string list>>(Ok 12L, nextRevision 11L 8L)
        Assert.True(nextRevision Int64.MaxValue 1L |> Result.isError)
        Assert.True(nextRevision -1L 1L |> Result.isError)

    [<Fact>]
    let ``provider checkpoint refuses oversized bodies and out of range progress`` () =
        let oversized = String.replicate (maximumBodyBytes + 1) "x"
        refused { checkpoint with Resources = [ { resource with Body = oversized; BodyDigest = hash oversized } ] }
        refused { checkpoint with Cursor = 2 }
        refused { checkpoint with Generation = 0L }

    [<Fact>]
    let ``provider private pending intent retains exact batch and refuses altered or foreign facts`` () =
        let complete = populationDigest checkpoint.Resources
        let pending =
            { checkpoint with
                Phase = PublicationPending;
                Cursor = 1;
                Revision = 2L;
                FirstSweepDigest = complete;
                SecondSweepDigest = complete;
                PendingBatchDigest = hash "batch";
                PendingBatchBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes "batch");
                FactReferences = [ { Kind = "ci-run"; Identity = "ci-run-one"; Revision = 2L; Digest = hash "fact" } ];
            }
        Assert.Equal<Result<Checkpoint, string list>>(Ok pending, parse identity now (encoded pending))
        refused { pending with PendingBatchBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes "different") }
        refused { pending with FactReferences = [ { pending.FactReferences.Head with Kind = "dispatch" } ] }
        refused { pending with FactReferences = [ { pending.FactReferences.Head with Revision = 3L } ] }
        refused { pending with FactReferences = pending.FactReferences @ pending.FactReferences }
        refused { pending with PriorCheckpointDigest = "unknown" }

    [<Fact>]
    let ``provider pure sweep rules require every resource twice and invalidate a changed response`` () =
        let first = match beginSweep checkpoint with Ok state -> state | Error error -> failwithf "%A" error
        Assert.True(finishSweep first |> Result.isError)
        Assert.True(advanceSweep { resource.Key with Kind = Checks } false first |> Result.isError)
        let firstComplete = match advanceSweep resource.Key false first with Ok state -> state | Error error -> failwithf "%A" error
        let second = match finishSweep firstComplete with Ok state -> state | Error error -> failwithf "%A" error
        Assert.Equal(SecondSweep, second.Phase)
        let invalidated = match advanceSweep resource.Key true second with Ok state -> state | Error error -> failwithf "%A" error
        Assert.Equal(Acquiring, invalidated.Phase)
        Assert.Equal("", invalidated.FirstSweepDigest)
        let secondComplete = match advanceSweep resource.Key false second with Ok state -> state | Error error -> failwithf "%A" error
        let barrier = match finishSweep secondComplete with Ok state -> state | Error error -> failwithf "%A" error
        Assert.Equal(FinalBarrier, barrier.Phase)
        Assert.True(beginSweep { checkpoint with Phase = PublicationPending } |> Result.isError)

    [<Fact>]
    let ``provider suite graph codec refuses prior version and cross kind suite identity`` () =
        let suiteResource = { resource with Key = { resource.Key with Kind = SuiteChecks; SuiteId = 123L } }
        let value = { checkpoint with Resources = [ suiteResource ] }
        let bytes = encoded value
        Assert.True(parse identity now bytes |> Result.isOk)
        let prior = Encoding.UTF8.GetString(bytes).Replace("provider-checkpoint/2", "provider-checkpoint/1") |> Encoding.UTF8.GetBytes
        Assert.True(parse identity now prior |> Result.isError)
        refused { value with Resources = [ { suiteResource with Key = { suiteResource.Key with SuiteId = 0L } } ] }
        refused { value with Resources = [ { suiteResource with Key = { suiteResource.Key with Kind = Jobs; RunId = 1L; Attempt = 1 } } ] }

    [<Fact>]
    let ``C3 pending reference contract retains every emitted kind and refuses unsupported or changed shape`` () =
        let kinds = [ "ci-population-admission"; "ci-binding"; "ci-page"; "ci-run"; "ci-job"; "ci-step"; "ci-coverage"; "ci-check"; "ci-population-coverage"; "diagnostic"; "ci-suite-page/1"; "ci-suite-seal/1" ]
        let reference kind: FactReference = { Kind = kind; Identity = "fact-" + kind; Revision = 1L; Digest = hash kind }
        let value = { checkpoint with Revision = 10L; FactReferences = kinds |> List.map reference }
        let roundtrip = parse identity now (encoded value)
        Assert.Equal<Result<Checkpoint, string list>>(Ok value, roundtrip)
        for kind in [ "ci-suite-page/2"; "ci-suite-seal/2"; "runtime-admission"; "*"; "ci-unknown" ] do
            refused { value with FactReferences = [ reference kind ] }
        let prior = reference "ci-suite-page/1"
        for changed in [ { prior with Revision = 0L }; { prior with Revision = 11L }
                         { prior with Digest = String.replicate 63 "a" }; { prior with Digest = String.replicate 64 "A" }
                         { prior with Identity = "" } ] do
            refused { value with FactReferences = [ changed ] }
        refused { value with FactReferences = [ prior; prior ] }
