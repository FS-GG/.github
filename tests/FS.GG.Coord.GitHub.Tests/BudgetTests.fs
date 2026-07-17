module FS.GG.Coord.GitHub.Tests.BudgetTests

open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors

/// A response with NO headers — the shape a secondary/abuse-detector 403 actually arrives in.
///
/// `classify` reads `X-RateLimit-Resource`/`X-RateLimit-Reset` off the failing response, so a test that
/// supplies none is asserting the arm where GitHub told us nothing: rate-limited, budget UNNAMED.
let private noHeaders: string -> string option = fun _ -> None

/// A response carrying the headers GitHub really sends on a limited call.
let private headers (pairs: (string * string) list) : string -> string option =
    fun name ->
        pairs
        |> List.tryFind (fun (k, _) -> System.String.Equals(k, name, System.StringComparison.OrdinalIgnoreCase))
        |> Option.map snd

// ---- #421: an exhausted budget is NOT an absent item ----------------------------------------------
//
// This is the sharpest single incident in the read path, and it is worth stating in full because the
// classifier below is the only thing standing between it and a recurrence.
//
// `item_id` looked up an issue's board item. Under an exhausted GraphQL budget the lookup FAILED, and the
// failure came back as the empty string — which the caller read as "this issue is not on the board". It
// then printed a remediation telling the worker to run `item-add` for an issue that already had a board
// item. A budget failure did not merely report the wrong thing; it turned "I could not ask" into "the
// answer is no", and it did so while sounding helpful.
//
// It did NOT create a second board item (#871): `addProjectV2ItemById` is idempotent server-side, so the
// remediation would have been a no-op. #421's text was explicitly counterfactual — a duplicate "would
// have" been created "had I followed it" — and that hedge hardened into a fact as it was copied into the
// source. The classifier below is load-bearing for the reason #421 actually earned, which needs no
// duplicate: unreachable is not absent.

[<Fact>]
let ``#421 a rate-limited failure classifies as RateLimited, never as NotFound`` () =
    let error =
        Budget.classify "FS.GG.SDD#42" 403 """{"message":"API rate limit exceeded for user ID 1."}""" noHeaders

    match error with
    | RateLimited _ -> ()
    | other -> failwith $"an exhausted budget must never read as an absence — got %A{other}"

[<Fact>]
let ``#421 ...and it carries EX_RATE (75), the back-off signal - not a generic 1`` () =
    // 75 is EX_TEMPFAIL, and it means TRY AGAIN LATER. `take` returns it WITHOUT retrying: an exhausted
    // budget is not a lost race, and three more attempts just spend three more calls confirming the same
    // 403. A generic exit 1 would be indistinguishable from a protocol error, and the worker would treat a
    // temporary condition as a permanent one.
    let error = RateLimited(UnknownBudget, None)
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
        Budget.classify "FS.GG.SDD#42" 403 """{"message":"Resource not accessible by integration"}""" noHeaders

    match error with
    | Unauthorized _ -> ()
    | other -> failwith $"a permissions failure must not read as a budget failure — got %A{other}"

[<Fact>]
let ``the classifier NAMES ITS SUBJECT, not its payload`` () =
    // An error that names the response body instead of the thing it was reading is unusable in a fan-out
    // log, where the only question anyone ever asks is *which item did this happen to*.
    match Budget.classify "FS-GG/FS.GG.SDD#42" 404 """{"message":"Not Found"}""" noHeaders with
    | NotFound subject -> Assert.Equal("FS-GG/FS.GG.SDD#42", subject)
    | other -> failwith $"expected NotFound — got %A{other}"

// ---- the message must NAME THE BUDGET THAT DIED ---------------------------------------------------
//
// THE INCIDENT, measured live on 2026-07-16. REST core sat at 0/5000 and 403'd every read; GraphQL had
// 3,639/5,000 still on the clock. Every board read — `take`, `who`, `next`, `batch`, `issues` — died, and
// each one said:
//
//     GraphQL budget EXHAUSTED. The reset time could not be read. ... REST-only work still runs.
//
// Three lies in one sentence. It named the budget that was FINE; it could not name a reset that was
// sitting in the response headers; and it then recommended REST — the budget that had actually stopped —
// as the way to keep working. A worker following that advice does exactly the wrong thing, and
// `/pnext-item` §1's doctrine ("read issues over REST, it's free"; "when GraphQL is gone, REST is still
// up") is built on the same inverted premise.
//
// This is #266's signature in the error text: a confident verdict about a subject the code could not see.
// `classify` was never given the response headers, so it COULD NOT tell a REST 403 from a GraphQL one —
// and `explain` hardcoded "GraphQL" for both.

[<Fact>]
let ``a REST rate limit says REST - it must NEVER be reported as the GraphQL budget`` () =
    // The exact headers GitHub sent on the live 403 (`X-RateLimit-Resource: core`).
    let error =
        Budget.classify
            "FS-GG/.github#1"
            403
            """{"message":"API rate limit exceeded for user ID 1645484."}"""
            (headers [ "X-RateLimit-Resource", "core"; "X-RateLimit-Remaining", "0" ])

    match error with
    | RateLimited(RestBudget, _) -> ()
    | other -> failwith $"a `core` 403 is the REST budget — got %A{other}"

    let sentence = explain error
    Assert.Contains("REST budget EXHAUSTED", sentence)

    // THE REGRESSION, PINNED. The old sentence opened with the wrong budget and closed by recommending
    // the dead one. Neither may ever appear on a REST limit again.
    Assert.DoesNotContain("GraphQL budget EXHAUSTED", sentence)
    Assert.DoesNotContain("REST-only work", sentence)

[<Fact>]
let ``a GraphQL rate limit still says GraphQL`` () =
    let error =
        Budget.classify
            "the board scan"
            403
            """{"message":"API rate limit exceeded"}"""
            (headers [ "X-RateLimit-Resource", "graphql" ])

    match error with
    | RateLimited(GraphQlBudget, _) -> ()
    | other -> failwith $"a `graphql` 403 is the GraphQL budget — got %A{other}"

    let sentence = explain error
    Assert.Contains("GraphQL budget EXHAUSTED", sentence)

    // "MAY still run", never "still runs". A 403 is evidence about the bucket that refused THIS call and
    // nothing else — both budgets can be dead at once, and the old assertive wording is what let this
    // sentence promise REST at the exact moment REST was the thing that had stopped. The hedge is the
    // difference between reporting an observation and certifying a guess.
    Assert.Contains("REST-only work may still run", sentence)
    Assert.DoesNotContain("REST-only work still runs", sentence)

[<Fact>]
let ``the resource is READ from the header, not inferred - and an UNNAMED budget is not guessed`` () =
    // A secondary/abuse-detector 403 carries no `X-RateLimit-Resource`. The remedy is the same (wait), but
    // the NAME is not knowable — and inventing one is the precise defect this whole change removes. Saying
    // "I do not know which" is the honest answer, and it is better than a confident wrong one.
    let error =
        Budget.classify "FS-GG/.github#1" 403 """{"message":"You have exceeded a secondary rate limit."}""" noHeaders

    match error with
    | RateLimited(UnknownBudget, _) -> ()
    | other -> failwith $"an unnamed limit must not be attributed to a budget — got %A{other}"

    let sentence = explain error
    Assert.Contains("did not name which budget", sentence)
    Assert.DoesNotContain("GraphQL budget EXHAUSTED", sentence)
    Assert.DoesNotContain("REST budget EXHAUSTED", sentence)

[<Fact>]
let ``the reset is read from X-RateLimit-Reset - the header that was there the whole time`` () =
    // `/pnext-item` §1 tells the worker to "back off until the reset it names". On a REST limit the tool
    // could never name one: `readResetAt` parsed only the BODY, looking for a GraphQL `resetAt`, while
    // REST sends the reset as an epoch-seconds HEADER. So every REST limit said "the reset time could not
    // be read" — with the answer sitting in the response it had just received.
    let inTenMinutes = System.DateTimeOffset.UtcNow.AddMinutes 10.0

    let error =
        Budget.classify
            "FS-GG/.github#1"
            403
            """{"message":"API rate limit exceeded for user ID 1645484."}"""
            (headers
                [ "X-RateLimit-Resource", "core"
                  "X-RateLimit-Reset", string (inTenMinutes.ToUnixTimeSeconds()) ])

    match error with
    | RateLimited(RestBudget, Some at) -> Assert.Equal(inTenMinutes.ToUnixTimeSeconds(), at.ToUnixTimeSeconds())
    | other -> failwith $"the reset header must be read — got %A{other}"

    Assert.Contains("resets in ~10m", explain error)
    Assert.DoesNotContain("could not be read", explain error)

[<Fact>]
let ``an UNPARSEABLE reset header is no reset - never 1970, which renders as 'retry now'`` () =
    // A garbage header must not become `FromUnixTimeSeconds 0`. That would print "the window has already
    // reset — retry now" on a limit that just fired: the one answer guaranteed to be wrong, and one that
    // sends the worker straight back into the 403 it is meant to be backing off from.
    let error =
        Budget.classify
            "FS-GG/.github#1"
            403
            """{"message":"API rate limit exceeded."}"""
            (headers [ "X-RateLimit-Resource", "core"; "X-RateLimit-Reset", "not-a-number" ])

    match error with
    | RateLimited(RestBudget, None) -> Assert.Contains("could not be read", explain error)
    | other -> failwith $"an unreadable reset is None, not an epoch of 0 — got %A{other}"

[<Fact>]
let ``an OUT-OF-RANGE reset header does not CRASH - Int64 parses where DateTimeOffset cannot convert`` () =
    // `FromUnixTimeSeconds` throws `ArgumentOutOfRangeException` outside 0001..9999, and an Int64 that
    // parses is not an epoch that converts. This runs on the FAILURE path, inside a transport `try` that
    // catches only HttpRequestException/TaskCanceledException — so an unguarded conversion escapes and
    // kills the process. A garbage header would turn "back off for 3 minutes" into a crash, in the code
    // whose whole job is to explain why nothing can run.
    for hostile in [ "99999999999999"; "-99999999999999"; "9223372036854775807" ] do
        let error =
            Budget.classify
                "FS-GG/.github#1"
                403
                """{"message":"API rate limit exceeded."}"""
                (headers [ "X-RateLimit-Resource", "core"; "X-RateLimit-Reset", hostile ])

        match error with
        | RateLimited(RestBudget, None) -> Assert.Contains("could not be read", explain error)
        | other -> failwith $"an out-of-range epoch (%s{hostile}) must read as NO reset — got %A{other}"

[<Fact>]
let ``a GraphQL body's resetAt answers when no header does - INCLUDING inside errors[]`` () =
    // The body fallback stays for a GraphQL 403 that carries `resetAt` where no header does — but it only
    // started WORKING with this change. The search descended objects and returned None for an array, and a
    // rate-limited GraphQL response nulls `data` and puts everything in `errors[]`. So the fallback could
    // not reach the only shape it exists for, and silently answered "no reset" every time.
    let at = System.DateTimeOffset.UtcNow.AddMinutes 5.0

    let body =
        $$"""{"errors":[{"message":"API rate limit exceeded","resetAt":"{{at.ToString "o"}}"}]}"""

    match Budget.classify "the board scan" 403 body (headers [ "X-RateLimit-Resource", "graphql" ]) with
    | RateLimited(GraphQlBudget, Some _) -> ()
    | other -> failwith $"the body's resetAt must still be read when no header carries one — got %A{other}"

[<Fact>]
let ``every REST resource is the REST budget - search and code_search are not GraphQL`` () =
    // GitHub meters `search` and `code_search` separately from `core`, but they are all REST. The
    // distinction a worker needs is which of the TWO transports just died — not which REST sub-counter.
    for resource in [ "core"; "search"; "code_search"; "integration_manifest" ] do
        match Budget.classify "s" 403 """{"message":"API rate limit exceeded"}""" (headers [ "X-RateLimit-Resource", resource ]) with
        | RateLimited(RestBudget, _) -> ()
        | other -> failwith $"`%s{resource}` is a REST resource — got %A{other}"

// ---- #510: only ONE failure may be queued ---------------------------------------------------------

[<Fact>]
let ``#510 an exhausted budget is queueable - it is the ONLY thing that is`` () =
    Assert.True(isQueueable (RateLimited(UnknownBudget, None)))

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
