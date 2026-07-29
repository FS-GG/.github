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
let ``every rate-limit WORDING is recognised - an unrecognised one would be dropped, not queued`` () =
    // GitHub does not report this failure one way. The primary limit, the SECONDARY limit and the abuse
    // detector all arrive as a 403 with different prose, and `gh project` renders it differently again from
    // `gh api graphql`. The corpus injects two distinct wordings on purpose and asserts the client
    // recognises BOTH — because a wording we do not recognise becomes a PERMANENT failure, a permanent
    // failure is never queued, and the board write is then silently dropped. That is #510 arriving through
    // the classifier instead of through the queue.
    //
    // This test used to be titled "...primary and secondary are one remedy". THEY ARE NOT (#1666), and the
    // title was the claim that licensed collapsing them. `isRateLimited` still answers only "is it a rate
    // limit at all" — which is the question the fail-closed ordering in `classify` needs — and
    // `isSecondaryLimit` below carries the distinction the remedy depends on.
    Assert.True(Budget.isRateLimited "API rate limit exceeded for user ID 1.")
    Assert.True(Budget.isRateLimited "You have exceeded a secondary rate limit.")
    Assert.True(Budget.isRateLimited "was submitted too quickly")
    Assert.True(Budget.isRateLimited "rate limit already exceeded")

// ---- #1666: a SECONDARY limit is a different mechanism with a different remedy ---------------------
//
// THE INCIDENT, measured across 2026-07-27/28 in a six-worker fan-out. `claim`/`take`/`who` refused with
//
//     REST budget EXHAUSTED. The budget resets in ~6m. ...
//
// while `gh api rate_limit` reported 62%, then 87%, then 88% of the primary REST budget still free — and a
// retry issued SECONDS later succeeded. The board driver reasoned from this to two different wrong
// conclusions (first that the account was exhausted and the fleet should be halved; then that the engine
// had an internal counter of its own), and three separate diagnoses were filed and withdrawn.
//
// The engine never had an internal counter. Every one of those refusals was a REAL 403 from GitHub — a
// SECONDARY (abuse-detection) limit, which is triggered by concurrent request BURSTS rather than by
// cumulative quota. That is why the primary counter looked healthy: it was healthy. A secondary limit does
// not appear in `/rate_limit` at all, so the number everyone reached for could never have settled it.
//
// Two defects follow, and these tests pin both:
//   1. the mechanism was not classified, so one message served all three limits; and
//   2. the reset was read off `X-RateLimit-Reset` — the PRIMARY window — which is why "resets in ~6m"
//      could precede a successful retry seconds later. `Retry-After`, the header that DOES describe a
//      secondary limit, was read nowhere in the codebase.

[<Fact>]
let ``#1666 a SECONDARY limit is classified as one - not as the REST budget`` () =
    // The exact shape measured: a secondary 403 that still carries `X-RateLimit-Resource: core`, because
    // the primary bucket is real and reported on every response. Reading that header alone says "REST
    // budget", and the body is the only thing that says "secondary".
    let error =
        Budget.classify
            "FS-GG/.github#1666"
            403
            """{"message":"You have exceeded a secondary rate limit. Please wait a few minutes before you try again."}"""
            (headers [ "X-RateLimit-Resource", "core"; "X-RateLimit-Remaining", "4382" ])

    match error with
    | RateLimited(SecondaryLimit(Some "core", _), _) -> ()
    | other -> failwith $"a secondary-limit body must classify as SecondaryLimit — got %A{other}"

    let sentence = explain error
    Assert.Contains("SECONDARY rate limit", sentence)
    Assert.Contains("REDUCE CONCURRENCY", sentence)

    // THE REGRESSION, PINNED. This is the sentence that halted a fleet three times: it named the primary
    // budget, and it told the operator to wait for a window that did not describe this limit.
    Assert.DoesNotContain("REST budget EXHAUSTED", sentence)
    Assert.DoesNotContain("GraphQL budget EXHAUSTED", sentence)

[<Fact>]
let ``#1666 a SECONDARY limit NEVER carries the primary reset - that is the false countdown`` () =
    // `X-RateLimit-Reset` is present on the response and names a real instant — for the PRIMARY bucket,
    // which was never exhausted. Attaching it here is what produced "The budget resets in ~6m" in front of
    // a retry that succeeded within seconds, and a countdown that sat at "~1m" across several minutes
    // without reaching zero. `resetAt` is None BY CONSTRUCTION on this arm.
    let inSixMinutes = System.DateTimeOffset.UtcNow.AddMinutes 6.0

    let error =
        Budget.classify
            "FS-GG/.github#1666"
            403
            """{"message":"You have exceeded a secondary rate limit."}"""
            (headers
                [ "X-RateLimit-Resource", "core"
                  "X-RateLimit-Reset", string (inSixMinutes.ToUnixTimeSeconds()) ])

    match error with
    | RateLimited(SecondaryLimit _, None) -> ()
    | other -> failwith $"a secondary limit must not inherit the PRIMARY reset — got %A{other}"

    let sentence = explain error
    Assert.DoesNotContain("resets in ~6m", sentence)
    Assert.DoesNotContain("The budget resets in", sentence)

[<Fact>]
let ``#1666 Retry-After is READ, and it is the reset a secondary limit actually uses`` () =
    // Nothing in this codebase read `Retry-After` before #1666 — `grep -rn 'Retry-After' src/` matched
    // nothing — while it is the ONLY header that describes the limit that was firing.
    let error =
        Budget.classify
            "FS-GG/.github#1666"
            403
            """{"message":"You have exceeded a secondary rate limit."}"""
            (headers [ "X-RateLimit-Resource", "core"; "Retry-After", "45" ])

    match error with
    | RateLimited(SecondaryLimit(_, Some after), None) -> Assert.Equal(45.0, after.TotalSeconds, 3)
    | other -> failwith $"`Retry-After: 45` must be read — got %A{other}"

    Assert.Contains("Retry-After: 45s", explain error)

[<Fact>]
let ``#1666 a secondary limit with NO Retry-After says so - it does not fall back to the primary window`` () =
    let error =
        Budget.classify "FS-GG/.github#1666" 403 """{"message":"was submitted too quickly"}""" noHeaders

    match error with
    | RateLimited(SecondaryLimit(None, None), None) -> ()
    | other -> failwith $"an unheadered secondary limit is (None, None) — got %A{other}"

    let sentence = explain error
    Assert.Contains("no `Retry-After`", sentence)
    Assert.Contains("back off with increasing delay", sentence)

[<Fact>]
let ``#1666 a HOSTILE Retry-After is NO reading - never 'retry now', never a fabricated window`` () =
    // Same discipline as `X-RateLimit-Reset` below: a header we cannot read is not a delay of zero, and a
    // value we do not believe is not a figure to hand a worker as fact. Zero and negatives would render as
    // "retry now" on a limit that just fired — straight back into the detector.
    // "23:59" IS THE ONE THAT GOT THROUGH, and it is here because review caught it, not the suite. The
    // first draft used a loose `DateTimeOffset.TryParse` for the HTTP-date form, which accepts far more
    // than an HTTP-date: "23:59" parsed as *today at 23:59* and was rendered as
    // "GitHub sent `Retry-After: 72213s`" — a fabricated ~20-hour delay attributed to GitHub, one branch
    // after the numeric arm had rejected "999999999" for being unbelievable. The original list held only
    // strings that fail BOTH parsers, so it could not have caught it. `TryParseExact` over RFC 9110's three
    // formats is the fix; these are the inputs that pin it.
    // "2026" is deliberately NOT in this list, though it looks like a bare year: `Retry-After` is
    // delta-SECONDS first, and 2026 seconds (~34m) is a perfectly legitimate value. Rejecting it because
    // it also parses as a date would lose a real delay. The numeric reading wins, and that is correct.
    for hostile in [ "0"; "-30"; "not-a-number"; "999999999"; ""; "23:59"; "Oct 21" ] do
        let error =
            Budget.classify
                "FS-GG/.github#1666"
                403
                """{"message":"You have exceeded a secondary rate limit."}"""
                (headers [ "Retry-After", hostile ])

        match error with
        | RateLimited(SecondaryLimit(_, None), _) -> ()
        | other -> failwith $"a hostile Retry-After (%s{hostile}) must read as NO delay — got %A{other}"

[<Fact>]
let ``#1666 a legitimate HTTP-date Retry-After IS still read - exactness is not blanket refusal`` () =
    // The mirror of the hostile case: tightening to `TryParseExact` must not silently stop reading the
    // form RFC 9110 actually permits, or the repair would be a second way to lose the header.
    let at = System.DateTimeOffset.UtcNow.AddMinutes 3.0

    let error =
        Budget.classify
            "s"
            403
            """{"message":"You have exceeded a secondary rate limit."}"""
            (headers [ "Retry-After", at.ToString("r", System.Globalization.CultureInfo.InvariantCulture) ])

    match error with
    | RateLimited(SecondaryLimit(_, Some after), _) -> Assert.InRange(after.TotalSeconds, 60.0, 200.0)
    | other -> failwith $"an RFC 9110 HTTP-date Retry-After must still be read — got %A{other}"

// ---- #1666: the GraphQL `errors` paths must make the SAME distinction --------------------------------
//
// Found in review, not by the suite. `Board.fs`, `Scan.fs` and `Done.fs` each open a GraphQL `errors`
// array and each wrote the same line — `if messages |> List.exists Budget.isRateLimited then
// Error(RateLimited(GraphQlBudget, None))`. `isRateLimited` matches the SECONDARY wordings too, so a
// secondary limit arriving on any of those three rendered as
//
//     GraphQL budget EXHAUSTED. The reset time could not be read. ... REST-only work may still run.
//
// which is worse than the REST case it was filed for: a secondary limit is account-wide and burst-
// triggered, so "REST-only work may still run" sends the fan-out straight back into the detector that
// just fired. And these are the Projects-v2 MUTATION paths — the fan-out that causes secondary limits.
//
// The decision now lives in ONE function so a fourth site cannot omit it.

[<Fact>]
let ``#1666 a SECONDARY limit inside a GraphQL errors array is not the GraphQL budget`` () =
    match Budget.ofGraphQlErrors [ "You have exceeded a secondary rate limit." ] with
    | Some(RateLimited(SecondaryLimit(None, None), None) as error) ->
        let sentence = explain error
        Assert.Contains("SECONDARY rate limit", sentence)
        // THE REGRESSION, PINNED — the advice that would re-trigger the limit.
        Assert.DoesNotContain("REST-only work may still run", sentence)
        Assert.DoesNotContain("GraphQL budget EXHAUSTED", sentence)
    | other -> failwith $"a secondary limit in a GraphQL errors array must classify as one — got %A{other}"

[<Fact>]
let ``#1666 a PRIMARY limit inside a GraphQL errors array is still the GraphQL budget`` () =
    match Budget.ofGraphQlErrors [ "API rate limit exceeded" ] with
    | Some(RateLimited(GraphQlBudget, None)) -> ()
    | other -> failwith $"a primary limit on the GraphQL path is the GraphQL budget — got %A{other}"

[<Fact>]
let ``#1666 a GraphQL errors array with NO rate limit is not a rate limit`` () =
    // `None` is the caller's cue to report `GraphQlErrors`. Answering `Some` here would turn every
    // ordinary GraphQL failure into a back-off signal.
    Assert.True((Budget.ofGraphQlErrors [ "Field 'foo' doesn't exist"; "(no message)" ]).IsNone)

[<Fact>]
let ``#1666 the three fixture cases the item asked for, as one table`` () =
    // The acceptance criteria name three states that must be told apart. `EX_RATE` is correct for all
    // three — every one of them is a real, temporary refusal — so what is asserted here is that the
    // DIAGNOSIS differs, which is the thing that was collapsed.
    let secondaryHealthyPrimary =
        Budget.classify
            "s"
            403
            """{"message":"You have exceeded a secondary rate limit."}"""
            (headers [ "X-RateLimit-Resource", "core"; "X-RateLimit-Remaining", "4382" ])

    let primaryExhausted =
        Budget.classify
            "s"
            403
            """{"message":"API rate limit exceeded for user ID 1."}"""
            (headers [ "X-RateLimit-Resource", "core"; "X-RateLimit-Remaining", "0" ])

    let unclassifiable =
        Budget.classify "s" 403 """{"message":"rate limit already exceeded"}""" noHeaders

    // All three are the back-off signal. None of them is a protocol error.
    for error in [ secondaryHealthyPrimary; primaryExhausted; unclassifiable ] do
        Assert.Equal(Errors.ExRate, exitCode error)
        Assert.True(isQueueable error)

    // ...and all three say something DIFFERENT about what to do.
    Assert.Contains("SECONDARY rate limit", explain secondaryHealthyPrimary)
    Assert.Contains("REST budget EXHAUSTED", explain primaryExhausted)
    Assert.Contains("WHICH bucket is unknown", explain unclassifiable)

[<Fact>]
let ``#1666 an UNCLASSIFIED limit fails closed - it advises the conservative union, not the primary`` () =
    // #266's rule: an unreadable classification is not a safe one to resume from. GitHub named no resource
    // and the wording did not say which mechanism fired, so a secondary limit CANNOT BE RULED OUT — and
    // advising "wait for the reset" alone would send the fleet back in at full concurrency.
    let error =
        Budget.classify "s" 403 """{"message":"API rate limit exceeded"}""" noHeaders

    match error with
    | RateLimited(UnknownBudget, _) -> ()
    | other -> failwith $"an unnamed limit must not be attributed to a budget — got %A{other}"

    let sentence = explain error
    // It states exactly what it knows: the WORDING reads as primary, the BUCKET is unnamed. An earlier
    // draft claimed the wording was ambiguous too — false in every reachable case, and over-claiming
    // uncertainty is the same defect as over-claiming certainty.
    Assert.Contains("reads as the PRIMARY budget", sentence)
    Assert.Contains("WHICH bucket is unknown", sentence)
    Assert.DoesNotContain("REST budget EXHAUSTED", sentence)

[<Fact>]
let ``#1666 the classifier does NOT consult /rate_limit to override a real 403`` () =
    // The item's body asked for exactly this ("`GET /rate_limit` is free — so the engine can check the real
    // number before halting a fleet on an estimate"). It must not be implemented, and this test is the
    // guard.
    //
    // A SECONDARY limit does not appear in `/rate_limit` at all. So the healthy primary counter that made
    // this look like a false alarm is precisely what a real secondary limit looks like from that endpoint —
    // and an engine that resumed on it would convert a genuine refusal into a resume, at full fan-out, into
    // the detector that had just fired. On this account the endpoint is not even reliable for the primary
    // bucket (measured 2026-07-16: `/rate_limit` said 2431/5000 while every real read 403'd with
    // `remaining: 0`).
    //
    // `classify` takes a body and a header lookup. It has no transport, so it CANNOT make that call — the
    // absence is structural, and this test pins the structure rather than a comment.
    let error =
        Budget.classify
            "s"
            403
            """{"message":"You have exceeded a secondary rate limit."}"""
            (headers [ "X-RateLimit-Resource", "core"; "X-RateLimit-Remaining", "4382" ])

    // A 403 with 4382 primary requests remaining is STILL a refusal. It is not downgraded, not retried
    // silently, and not reported as healthy.
    Assert.Equal(Errors.ExRate, exitCode error)

    match error with
    | RateLimited(SecondaryLimit _, _) -> ()
    | other -> failwith $"a real 403 must stand regardless of the primary counter — got %A{other}"

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
    | RateLimited(RestBudget(Some "core"), _) -> ()
    | other -> failwith $"a `core` 403 is the REST budget, and it must NAME `core` — got %A{other}"

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
    // A secondary/abuse-detector 403 carries no `X-RateLimit-Resource`, so the BUCKET is not knowable and
    // inventing one is the defect this arm removes. That invariant is unchanged and is asserted below.
    //
    // WHAT CHANGED (#1666): this case is no longer `UnknownBudget`. The body says "secondary rate limit",
    // and that is the MECHANISM — a fact the classifier always had and used to throw away, along with the
    // remedy that depends on it. Its old comment ("the remedy is the same (wait)") is the refuted claim.
    // So the honest answer here is richer than it was: the bucket is still unknown (`None`), but the limit
    // is identified.
    let error =
        Budget.classify "FS-GG/.github#1" 403 """{"message":"You have exceeded a secondary rate limit."}""" noHeaders

    match error with
    | RateLimited(SecondaryLimit(None, _), _) -> ()
    | other -> failwith $"an unnamed secondary limit keeps a `None` bucket, and is not guessed — got %A{other}"

    let sentence = explain error
    Assert.Contains("SECONDARY rate limit", sentence)

    // The bucket is still never invented, and neither budget is ever named on this evidence.
    Assert.DoesNotContain("GitHub named the resource", sentence)
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
    | RateLimited(RestBudget(Some "core"), Some at) -> Assert.Equal(inTenMinutes.ToUnixTimeSeconds(), at.ToUnixTimeSeconds())
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
    | RateLimited(RestBudget(Some "core"), None) -> Assert.Contains("could not be read", explain error)
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
        | RateLimited(RestBudget(Some "core"), None) -> Assert.Contains("could not be read", explain error)
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
let ``every REST resource is the REST budget - and it NAMES the bucket rather than collapsing it`` () =
    // GitHub meters `search` and `code_search` separately from `core`, but they are all REST, so the
    // REST-vs-GraphQL distinction still holds. WHAT CHANGED IS THE DISCARD.
    //
    // This test previously asserted `RateLimited(RestBudget, _)` — a shape with nowhere to put the bucket
    // name — and its comment said the sub-counter was not a distinction a worker needs. #1666 is the
    // measured refutation. `core` is the bucket every operator checks (`gh api rate_limit`'s top level), so
    // a 403 from `search` reported as the flat words "REST budget EXHAUSTED" sends them to a counter that
    // is 87% free, from which the only available conclusion is that the tool is lying. It was not lying; it
    // was declining to say which bucket. Four investigations were filed on that reading, three of them
    // withdrawn by their own authors.
    for resource in [ "core"; "search"; "code_search"; "integration_manifest" ] do
        match Budget.classify "s" 403 """{"message":"API rate limit exceeded"}""" (headers [ "X-RateLimit-Resource", resource ]) with
        | RateLimited(RestBudget(Some named), _) ->
            Assert.Equal(resource, named)
            // and it must reach the OPERATOR, not merely the type.
            Assert.Contains($"`%s{resource}`", explain (RateLimited(RestBudget(Some named), None)))
        | other -> failwith $"`%s{resource}` is a REST resource and must be NAMED — got %A{other}"

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
