namespace FS.GG.Coord.Core.Tests

open System
open Xunit
open FS.GG.Coord

module ReviewWaitTests =
    let entered = DateTimeOffset.Parse "2026-08-21T10:00:00Z"
    let receipt: ReviewWait.WaitReceipt =
        { Item = "FS-GG/.github#2756"
          ClaimGeneration = "5361756295"
          ReviewGeneration = "review-2"
          Kind = ReviewWait.RepairConfirmation
          EnteredAt = entered
          ExpiresAt = entered.AddHours 4.0
          EvidenceRef = "https://github.com/FS-GG/.github/pull/2792" }

    [<Fact>]
    let ``2756 entering a queue writes a round-trippable receipt`` () =
        let body = ReviewWait.encode (ReviewWait.Enter receipt)
        Assert.StartsWith(ReviewWait.Marker, body)
        match ReviewWait.tryDecode body with
        | Ok (Some (ReviewWait.Enter decoded)) -> Assert.Equal(receipt, decoded)
        | other -> failwithf "entry marker did not parse: %A" other

    [<Fact>]
    let ``2756 a current receipt reserves after the active lease duration`` () =
        match ReviewWait.project receipt.Item (Some receipt.ClaimGeneration) true (entered.AddHours 3.0) [ ReviewWait.Enter receipt ] with
        | ReviewWait.Waiting current -> Assert.Equal(receipt.ReviewGeneration, current.ReviewGeneration)
        | other -> failwithf "expected durable waiting state, got %A" other

    [<Fact>]
    let ``2756 a changed claim generation never resurrects mutation authority`` () =
        match ReviewWait.project receipt.Item (Some "new-claim") true (entered.AddHours 1.0) [ ReviewWait.Enter receipt ] with
        | ReviewWait.Recoverable (_, reason) -> Assert.Contains("reacquire", reason)
        | other -> failwithf "expected recoverable claim mismatch, got %A" other

    [<Fact>]
    let ``2756 a terminal event cannot hide a changed claim generation`` () =
        let completed = ReviewWait.Complete(receipt.ReviewGeneration, entered.AddMinutes 1.0, "old-claim-pass")
        match ReviewWait.project receipt.Item (Some "new-claim") true (entered.AddHours 1.0)
                  [ ReviewWait.Enter receipt; completed ] with
        | ReviewWait.Recoverable (_, reason) -> Assert.Contains("reacquire", reason)
        | other -> failwithf "an old-claim terminal hid the replacement claim: %A" other

    [<Fact>]
    let ``2756 bounded timeout returns an explicit recoverable state`` () =
        match ReviewWait.project receipt.Item (Some receipt.ClaimGeneration) true receipt.ExpiresAt [ ReviewWait.Enter receipt ] with
        | ReviewWait.Recoverable (_, reason) -> Assert.Contains("expired", reason)
        | other -> failwithf "expected recoverable timeout, got %A" other

    [<Fact>]
    let ``2756 completion recorded before expiry wins a later timeout race`` () =
        let completed = ReviewWait.Complete(receipt.ReviewGeneration, receipt.ExpiresAt.AddSeconds(-1.0), "https://review/pass")
        let timeout = ReviewWait.Timeout(receipt.ReviewGeneration, receipt.ExpiresAt, "timer")
        match ReviewWait.project receipt.Item (Some receipt.ClaimGeneration) true (receipt.ExpiresAt.AddMinutes 1.0) [ ReviewWait.Enter receipt; completed; timeout ] with
        | ReviewWait.Completed (_, evidence) -> Assert.Equal("https://review/pass", evidence)
        | other -> failwithf "completion lost the timeout race: %A" other

    [<Fact>]
    let ``2756 first durable terminal wins even when a later comment is backdated`` () =
        let timeout = ReviewWait.Timeout(receipt.ReviewGeneration, receipt.ExpiresAt, "timer")
        let backdated = ReviewWait.Complete(receipt.ReviewGeneration, receipt.ExpiresAt.AddHours(-1.0), "late-comment")
        match ReviewWait.project receipt.Item (Some receipt.ClaimGeneration) true receipt.ExpiresAt [ ReviewWait.Enter receipt; timeout; backdated ] with
        | ReviewWait.Recoverable (_, evidence) -> Assert.Equal("timer", evidence)
        | other -> failwithf "a backdated later comment stole the durable race: %A" other

    [<Fact>]
    let ``2756 receipt is bounded and cannot reserve forever`` () =
        let unbounded = ReviewWait.Enter { receipt with ExpiresAt = entered.AddHours 25.0 }
        match ReviewWait.validate unbounded with
        | Error errors -> Assert.Contains("at most 24 hours", String.concat "; " errors)
        | Ok _ -> failwith "an unbounded wait was accepted"

    [<Fact>]
    let ``2756 a premature timeout cannot consume the bounded wait`` () =
        let early = ReviewWait.Timeout(receipt.ReviewGeneration, receipt.ExpiresAt.AddSeconds(-1.0), "early-timer")
        match ReviewWait.project receipt.Item (Some receipt.ClaimGeneration) true (entered.AddHours 1.0) [ ReviewWait.Enter receipt; early ] with
        | ReviewWait.Invalid errors -> Assert.Contains("timeout predates expiresAt", String.concat "; " errors)
        | other -> failwithf "a premature timeout consumed the wait: %A" other

    [<Fact>]
    let ``2756 an orphan terminal is invalid rather than no receipt`` () =
        let orphan = ReviewWait.Complete("missing-generation", entered, "orphan")
        match ReviewWait.project receipt.Item (Some receipt.ClaimGeneration) true entered [ orphan ] with
        | ReviewWait.Invalid errors -> Assert.Contains("no entry receipt", String.concat "; " errors)
        | other -> failwithf "an orphan terminal was hidden as empty state: %A" other

    [<Fact>]
    let ``2756 a malformed authoritative marker fails closed`` () =
        match ReviewWait.tryDecode (ReviewWait.Marker + "\n{\"schema\":") with
        | Error _ -> ()
        | other -> failwithf "a malformed marker was ignored: %A" other

    [<Fact>]
    let ``2756 two different unconsumed generations are invalid rather than latest wins`` () =
        let second =
            { receipt with
                ReviewGeneration = "review-3"
                EnteredAt = receipt.EnteredAt.AddMinutes 1.0
                ExpiresAt = receipt.ExpiresAt.AddMinutes 1.0 }
        match ReviewWait.project receipt.Item (Some receipt.ClaimGeneration) true (entered.AddHours 1.0)
                  [ ReviewWait.Enter receipt; ReviewWait.Enter second ] with
        | ReviewWait.Invalid errors -> Assert.Contains("multiple review generations are unconsumed", String.concat "; " errors)
        | other -> failwithf "a later unconsumed generation silently replaced the first: %A" other

    [<Fact>]
    let ``2756 a new generation is admitted after the preceding one is consumed`` () =
        let completed = ReviewWait.Complete(receipt.ReviewGeneration, entered.AddMinutes 1.0, "first-complete")
        let second =
            { receipt with
                ReviewGeneration = "review-3"
                EnteredAt = receipt.EnteredAt.AddMinutes 2.0
                ExpiresAt = receipt.ExpiresAt.AddMinutes 2.0 }
        match ReviewWait.project receipt.Item (Some receipt.ClaimGeneration) true (entered.AddHours 1.0)
                  [ ReviewWait.Enter receipt; completed; ReviewWait.Enter second ] with
        | ReviewWait.Waiting active -> Assert.Equal(second.ReviewGeneration, active.ReviewGeneration)
        | other -> failwithf "the consumed predecessor blocked the next generation: %A" other
