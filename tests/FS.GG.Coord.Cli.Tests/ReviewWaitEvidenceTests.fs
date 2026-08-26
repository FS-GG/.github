module FS.GG.Coord.Cli.Tests.ReviewWaitEvidenceTests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.GitHub
open FS.GG.Coord.Cli.Lifecycle

let private record kind critic digest : StructuredDecision.ReviewRecord =
    { Schema = StructuredDecision.ReviewSchema
      Subject = "FS-GG/FS.GG.Coordination#1/pr/3"
      Revision = if kind = StructuredDecision.RepairPhase then 2 else 3
      PreviousDigest = None
      HeadSha = String.replicate 40 "a"
      ClaimGeneration = None
      BaseSha = None
      Critic = critic
      Verdict = if kind = StructuredDecision.RepairPhase then StructuredDecision.ChangesRequired else StructuredDecision.Pass
      AcceptedExceptions = []
      RouteApplicability = "not-meaningful"
      RouteEvidence = [ "fixture" ]
      PolicyVersion = StructuredDecision.PolicyVersion
      Kind = kind
      Round = 1
      InitialReview = Some "https://example.test/reviews/1"
      PrecedingReview = Some "https://example.test/reviews/1"
      DiffAuditRequired = false
      DiffAuditReceipts = []
      Succession = None
      RepairPhaseReceipt = None
      Timestamp = "2026-08-26T00:00:00Z"
      Digest = digest }

let private repairDigest = String.replicate 64 "b"
let private confirmationDigest = String.replicate 64 "c"

let private candidates =
    [ ({ Id = 20L; Url = "https://example.test/reviews/20"; Body = "repair phase" }: Reads.CommentBody),
      record StructuredDecision.RepairPhase "wren-19af" repairDigest
      ({ Id = 21L; Url = "https://example.test/reviews/21"; Body = "confirmation" }: Reads.CommentBody),
      record StructuredDecision.Confirmation "brant-fe2a" confirmationDigest ]

[<Theory>]
[<InlineData("https://example.test/reviews/21")>]
[<InlineData("21")>]
let ``exact evidence selects confirmation when repair phase shares its generation`` evidence =
    let actual = LiveHandlers.selectCompletionEvidence "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:repair-confirmation:1" evidence candidates
    Assert.Equal(Ok "https://example.test/reviews/21", actual)

[<Fact>]
let ``digest evidence selects confirmation when repair phase shares its generation`` () =
    let actual = LiveHandlers.selectCompletionEvidence "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:repair-confirmation:1" confirmationDigest candidates
    Assert.Equal(Ok "https://example.test/reviews/21", actual)

[<Fact>]
let ``wrong evidence remains fail closed when a generation has multiple records`` () =
    let actual = LiveHandlers.selectCompletionEvidence "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:repair-confirmation:1" "not-a-record" candidates
    match actual with
    | Error error ->
        Assert.Contains("does not identify one structured review-decision record", error)
        Assert.Contains("https://example.test/reviews/20", error)
        Assert.Contains("https://example.test/reviews/21", error)
    | Ok url -> failwith $"expected refusal, got %s{url}"

[<Fact>]
let ``a duplicated digest remains ambiguous`` () =
    let duplicateDigestCandidates =
        candidates
        |> List.map (fun (comment, review) -> comment, { review with Digest = confirmationDigest })
    let actual = LiveHandlers.selectCompletionEvidence "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:repair-confirmation:1" confirmationDigest duplicateDigestCandidates
    match actual with
    | Error error -> Assert.Contains("matches multiple structured review-decision records", error)
    | Ok url -> failwith $"expected refusal, got %s{url}"
