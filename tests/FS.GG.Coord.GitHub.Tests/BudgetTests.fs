module FS.GG.Coord.GitHub.Tests.BudgetTests

open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors

// ---- #421: an exhausted budget is NOT an absent item ----------------------------------------------
//
// This is the sharpest single incident in the read path, and it is worth stating in full because the
// classifier below is the only thing standing between it and a recurrence.
//
// `item_id` looked up an issue's board item. Under an exhausted GraphQL budget the lookup FAILED, and the
// failure came back as the empty string — which the caller read as "this issue is not on the board". It
// then printed a remediation telling the worker to run `item-add`, which CREATED A SECOND BOARD ITEM for
// an issue that already had one. A budget failure did not merely report the wrong thing; it corrupted the
// board, and it did so while sounding helpful.

[<Fact>]
let ``#421 a rate-limited failure classifies as RateLimited, never as NotFound`` () =
    let error =
        Budget.classify "FS.GG.SDD#42" 403 """{"message":"API rate limit exceeded for user ID 1."}"""

    match error with
    | RateLimited _ -> ()
    | other -> failwith $"an exhausted budget must never read as an absence — got %A{other}"

[<Fact>]
let ``#421 ...and it carries EX_RATE (75), the back-off signal - not a generic 1`` () =
    // 75 is EX_TEMPFAIL, and it means TRY AGAIN LATER. `take` returns it WITHOUT retrying: an exhausted
    // budget is not a lost race, and three more attempts just spend three more calls confirming the same
    // 403. A generic exit 1 would be indistinguishable from a protocol error, and the worker would treat a
    // temporary condition as a permanent one.
    let error = RateLimited None
    Assert.Equal(75, exitCode error)
    Assert.Equal(Errors.ExRate, exitCode error)

[<Fact>]
let ``both rate-limit WORDINGS are recognised - primary and secondary are one remedy`` () =
    // GitHub does not report this failure one way. The primary limit, the SECONDARY limit and the abuse
    // detector all arrive as a 403 with different prose, and `gh project` renders it differently again from
    // `gh api graphql`. The corpus injects two distinct wordings on purpose and asserts the client
    // recognises BOTH — because a wording we do not recognise becomes a PERMANENT failure, a permanent
    // failure is never queued, and the board write is then silently dropped. That is #510 arriving through
    // the classifier instead of through the queue.
    Assert.True(Budget.isRateLimited "API rate limit exceeded for user ID 1.")
    Assert.True(Budget.isRateLimited "You have exceeded a secondary rate limit.")
    Assert.True(Budget.isRateLimited "was submitted too quickly")
    Assert.True(Budget.isRateLimited "rate limit already exceeded")

[<Fact>]
let ``a 403 that is NOT a rate limit is Unauthorized - the remedies are opposite`` () =
    // "Wait" and "your token is wrong" are different instructions, and telling a worker to wait out a
    // permissions failure is an infinite loop that reports progress the whole way.
    let error =
        Budget.classify "FS.GG.SDD#42" 403 """{"message":"Resource not accessible by integration"}"""

    match error with
    | Unauthorized _ -> ()
    | other -> failwith $"a permissions failure must not read as a budget failure — got %A{other}"

[<Fact>]
let ``the classifier NAMES ITS SUBJECT, not its payload`` () =
    // An error that names the response body instead of the thing it was reading is unusable in a fan-out
    // log, where the only question anyone ever asks is *which item did this happen to*.
    match Budget.classify "FS-GG/FS.GG.SDD#42" 404 """{"message":"Not Found"}""" with
    | NotFound subject -> Assert.Equal("FS-GG/FS.GG.SDD#42", subject)
    | other -> failwith $"expected NotFound — got %A{other}"

// ---- #510: only ONE failure may be queued ---------------------------------------------------------

[<Fact>]
let ``#510 an exhausted budget is queueable - it is the ONLY thing that is`` () =
    Assert.True(isQueueable (RateLimited None))

[<Fact>]
let ``#510 every OTHER failure is permanent, and a permanent failure may NOT be queued`` () =
    // `flush` would replay it forever; each replay would fail identically; the queue would never drain; the
    // refusal would never reach the operator; and the tool would report success over a write it had
    // dropped. The bug was that `claim` tested this and `set-field` did not — so the same "the write is
    // QUEUED" sentence was printed over failures that could never be kept.
    Assert.False(isQueueable (NotFound "FS.GG.SDD#42"))
    Assert.False(isQueueable (Unauthorized "FS.GG.SDD#42"))
    Assert.False(isQueueable (Malformed("FS.GG.SDD#42", "not JSON")))
    Assert.False(isQueueable (Transport "connection reset"))
    Assert.False(isQueueable (Http(500, "boom")))

[<Fact>]
let ``a PARTIAL write is never queued - replaying it would rewrite the half that landed`` () =
    // GraphQL executes mutations SERIALLY and reports a mid-document failure as HTTP 200 carrying both
    // `data` and `errors`. So the aliases before the failure DID land. Replaying the whole document would
    // write them a second time — and `EX_PARTIAL` exists precisely so that this is a distinct, loud,
    // un-queueable outcome rather than a retry.
    let error = Partial([ "f0" ], [ "f1", "No such field" ])
    Assert.False(isQueueable error)
    Assert.Equal(Errors.ExPartial, exitCode error)

// ---- the cost model -------------------------------------------------------------------------------

[<Fact>]
let ``#448 the cost model has a ONE-POINT FLOOR - which is what makes the aliased batch a 6x win`` () =
    // GitHub bills `cost = max(1, nodes/100)`. A Projects v2 field mutation returns ~1 node, so it hits the
    // floor — meaning the cost of a placement pass tracks the REQUEST COUNT and nothing else. Six fields on
    // one item is six requests and six points; aliased into one document it is one request and ONE point.
    //
    // The org's own budget doc used to claim the opposite. That claim is true of QUERIES, whose cost scales
    // with the nodes they return, and false of these mutations, which are pinned to the floor.
    Assert.Equal(1, Budget.cost 1)
    Assert.Equal(1, Budget.cost 99)
    Assert.Equal(1, Budget.cost 100)
    Assert.Equal(6, Budget.cost 640)

[<Fact>]
let ``the meter is read from the response, and a HALF meter is no meter`` () =
    // `rateLimit { cost remaining }` is selected by every query document. A `rateLimit` object carrying only
    // one of the two is a document we do not understand — and reporting half a meter as a whole one is a
    // confident number with nothing behind it.
    match Budget.readMeter """{"data":{"rateLimit":{"cost":1,"remaining":4999}}}""" with
    | Some m ->
        Assert.Equal(1, m.Cost)
        Assert.Equal(4999, m.Remaining)
    | None -> failwith "a complete meter must be read"

    Assert.True((Budget.readMeter """{"data":{"rateLimit":{"cost":1}}}""") .IsNone)

[<Fact>]
let ``a MUTATION carries no meter, and that is not an error`` () =
    // `rateLimit` is a field of the QUERY root, not the Mutation root, so selecting it in a mutation is a
    // document that does not parse. The batch document is therefore the one call whose cost the meter
    // cannot read — which is fine, because its cost is the very thing it makes constant.
    Assert.True((Budget.readMeter """{"data":{"updateProjectV2ItemFieldValue":{}}}""").IsNone)

[<Fact>]
let ``a body that is not JSON yields NO meter reading - never a reading of zero`` () =
    // The meter does not get to invent a number out of bytes it could not read. Zero remaining is a
    // catastrophic reading — it would mean the fleet is out of budget — and arriving at it by way of a
    // parse failure is the confident-empty-answer, one more time, in the one place that reports how much
    // room is left.
    Assert.True((Budget.readMeter "<html>502 Bad Gateway</html>").IsNone)
