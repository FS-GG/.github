namespace FS.GG.Coord.GitHub

/// The budgets — GraphQL points and REST requests — and the classifier that says which one just died.
///
/// THE FLEET SHARES BOTH, AND EITHER CAN GO FIRST. This module's header used to read "the 5,000 pt/hr
/// GraphQL budget — the one the whole fleet shares, and the first one to die", and every message it
/// produced was written from that premise. It is not reliably true. Measured live on 2026-07-16: REST core
/// sat at 0/5000 and 403'd every read — `take`, `claim`, `who`, `issues`, the whole lock — while GraphQL
/// had 3,639 points still on the clock. `/pnext-item`'s own doctrine ("read issues over REST, it's free";
/// "when GraphQL is gone, REST is still up") is what drains core, so this is a state the recipe MANUFACTURES
/// rather than an exotic one. Whether the lock should still live on REST is ADR-0034 §3's call, not this
/// module's; what this module owes the worker either way is the NAME OF THE BUDGET THAT ACTUALLY DIED.
///
/// #418 is the founding incident: five workers looping `take` drained the primary budget in ~15 minutes,
/// and everything downstream of an exhausted budget then reported the wrong fact — "not on board" (#421),
/// "nothing schedulable" (#440), "the write succeeded" (#510). The budget is not a performance concern
/// here. It is a CORRECTNESS concern, because every read that fails on it fails into a lie.
///
/// TWO BUDGETS, AND THEY ARE NOT INTERCHANGEABLE. GraphQL is metered by NODES REQUESTED (5,000 points an
/// hour), so batching reads saves nothing; REST is metered by REQUESTS (5,000 an hour, one point each)
/// and 304s are free. That asymmetry is why the lock lives on REST — **a lock may never live on the
/// budget that dies first** (ADR-0034 §3, re-ratified by ADR-0040 C4).
module Budget =

    type RestObservation =
        { Resource: string
          Limit: int option
          Remaining: int option
          Used: int option
          ResetAt: System.DateTimeOffset option
          ObservedAt: System.DateTimeOffset
          Source: string }

    /// Persist a credential-hashed observation from real REST response headers. Missing or malformed
    /// headers are not inferred into a resource reading.
    val observeRestHeaders: token: string -> header: (string -> string option) -> unit

    /// Read the credential-scoped real-resource observation, if one has been recorded.
    val readRestObservation: token: string -> RestObservation option

    /// Read all independent REST resource observations for the credential. A healthy resource never
    /// overwrites an exhausted sibling resource.
    val readRestObservations: token: string -> RestObservation list

    /// Configured REST requests retained for guarded landing and cleanup. Invalid values retain the
    /// conservative default rather than disabling admission control.
    val dispatchReserve: unit -> int

    /// A conservative projection across every observed REST resource.
    type FleetState =
        | Healthy
        | Constrained
        | Exhausted
        | Unknown

    val fleetState: observations: RestObservation list -> FleetState
    val fleetStateText: state: FleetState -> string

    open Errors

    /// What a GraphQL response told us about the meter.
    ///
    /// Every query document MUST select `rateLimit { cost remaining }`. It is a field of the QUERY root,
    /// so a MUTATION cannot carry it — `set-field --batch`'s document is therefore the one call whose cost
    /// the meter cannot read, which is fine precisely because that document's cost is the thing it makes
    /// constant (one aliased document = one point, at the floor).
    type Meter =
        { Cost: int
          Remaining: int }

    /// GitHub bills `cost = max(1, nodes/100)` — a ONE-POINT FLOOR per request.
    ///
    /// This is what makes `set-field --batch` (#448) worth ~6×: a Projects v2 field mutation returns ~1
    /// node, so it hits the floor, and the cost of a placement pass therefore tracks the REQUEST COUNT and
    /// nothing else. Six fields on one item is six requests and six points; aliased into one document it is
    /// one request and ONE point.
    ///
    /// `docs/coordination/graphql-budget.md` used to claim the opposite. That claim is true of QUERIES,
    /// whose cost scales with the nodes they return, and false of these mutations, which are pinned to the
    /// floor.
    val cost: nodes: int -> int

    /// Is this failure the budget, rather than something else?
    ///
    /// GitHub does not give you a machine-readable "you are rate limited" on every shape of this failure:
    /// the primary limit, the SECONDARY limit, and the abuse detector all arrive as a 403 with DIFFERENT
    /// prose, and `gh project item-edit` renders it differently again from `gh api graphql`. The bash
    /// client matched on the text of all of them, and the corpus pins that both wordings are recognised —
    /// so the classifier is text, and it stays text.
    ///
    /// FAIL-CLOSED ORDER, AND IT MATTERS: this test runs BEFORE the partial-apply arm, or an exhausted
    /// budget would be misreported as a half-written board.
    ///
    /// It answers "is this a rate limit", and DELIBERATELY not "which one" — see `isSecondaryLimit`.
    val isRateLimited: body: string -> bool

    /// Is this specifically a SECONDARY (abuse-detection) limit, rather than the primary budget?
    ///
    /// The prose above says the three mechanisms "all arrive as a 403 with DIFFERENT prose" — and for its
    /// whole life this module matched all five wordings into one bool and threw the distinction away. It
    /// was never unknowable; it was computed and discarded.
    ///
    /// #1666 is the bill. A secondary limit is triggered by burst CONCURRENCY, not cumulative quota, so it
    /// fires while the primary counter is nearly untouched — and it does not appear in `/rate_limit` at
    /// all. Reported as "REST budget EXHAUSTED … resets in ~6m", it sent four investigations to the wrong
    /// conclusion: the operator checked the primary counter, found 62% / 87% / 88% free, and inferred a
    /// phantom internal counter. The remedies are opposite in kind — a primary limit is waited out, a
    /// secondary limit is cleared by REDUCING CONCURRENCY, and waiting at full fan-out re-triggers it.
    val isSecondaryLimit: body: string -> bool

    /// Classify a rate limit reported inside a GraphQL `errors` array. `None` when none of the messages is
    /// a rate limit at all, which is the caller's cue to report `GraphQlErrors`.
    ///
    /// THIS EXISTS BECAUSE THE DECISION WAS TRIPLICATED. `Board.fs`, `Scan.fs` and `Done.fs` each wrote
    /// `if messages |> List.exists Budget.isRateLimited then Error(RateLimited(GraphQlBudget, None))` —
    /// and `isRateLimited` matches the SECONDARY wordings too. So a secondary limit hitting any of the
    /// three rendered as "GraphQL budget EXHAUSTED … REST-only work may still run": the precise wrong
    /// advice #1666 exists to remove, and worse here than on the REST path, because a secondary limit is
    /// account-wide and burst-triggered, so "REST-only work may still run" sends the fan-out straight back
    /// into the detector that just fired. These are the Projects-v2 mutation paths — the fan-out that
    /// CAUSES secondary limits in the first place.
    ///
    /// One function, so a fourth call site cannot reintroduce it by omission.
    val ofGraphQlErrors: messages: string list -> IoError option

    /// Read `rateLimit { cost remaining }` off a GraphQL response body.
    ///
    /// `None` when the document did not carry it (a mutation) or the body did not parse. It is NOT an
    /// error: the meter is OBSERVATIONAL. The tool never refuses a call because it predicts exhaustion —
    /// it reacts to the 403. A predictive throttle would be a fourth place for the budget model to be
    /// wrong, and it would refuse work on an estimate.
    val readMeter: body: string -> Meter option

    /// The warning threshold. Below this, the operator is told.
    ///
    /// 500 points is roughly 70 full-board scans' worth of headroom on the live 640-item board — enough to
    /// act on, and little enough to mean it.
    [<Literal>]
    val WarnBelow: int = 500

    /// Classify a failed call into an `IoError`, given the subject it was about, its status and its body.
    ///
    /// `subject` is what was being READ (`FS-GG/FS.GG.SDD#42`, `the board scan`) — never the response
    /// payload. An error that names its payload instead of its subject is unusable in a fan-out log, where
    /// the only question anyone asks is *which item did this happen to*.
    ///
    /// THE ONE RULE: a 403 that is a rate limit and a 403 that is a permissions failure are DIFFERENT
    /// FACTS with different remedies — one is "wait", the other is "your token is wrong" — and telling a
    /// worker to wait out a token error is an infinite loop that reports progress.
    ///
    /// `header` looks up a RESPONSE header on the failing call, case-insensitively, and it is what lets a
    /// rate limit name the budget that died (`X-RateLimit-Resource`), the reset it named
    /// (`X-RateLimit-Reset`) and — since #1666 — a secondary limit's own `Retry-After`. Without it this
    /// function could not tell a REST 403 from a GraphQL one, so `explain` said "GraphQL" for both — and
    /// then recommended REST, on a REST limit, in the sentence telling you what to do next.
    ///
    /// TWO THINGS IT WILL NOT DO, both learned from #1666:
    ///
    /// It will not consult `GET /rate_limit` to second-guess a 403. That endpoint is free, and the item
    /// that prompted this change asked for exactly that — but a SECONDARY limit does not appear in it, so
    /// "the counter looks healthy" is not evidence the refusal was false. Overriding a real 403 on that
    /// reading would convert a genuine refusal into a resume, at full fan-out, into the detector that just
    /// fired. Worse, on this account `/rate_limit`'s `core` figure disagrees with the counter real requests
    /// are billed against (measured 2026-07-16: it reported 2431/5000 while every real read 403'd with
    /// `remaining: 0`, naming a different reset instant). The failing response's OWN headers are the only
    /// reading definitionally about the request that failed.
    ///
    /// And it will not attach `X-RateLimit-Reset` to a secondary limit. That header describes the primary
    /// window; presenting it as the resume signal for a limit that does not use it is what produced
    /// "resets in ~6m" ahead of a retry that succeeded in seconds.
    val classify: subject: string -> status: int -> body: string -> header: (string -> string option) -> IoError
