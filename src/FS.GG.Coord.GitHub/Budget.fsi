namespace FS.GG.Coord.GitHub

/// The 5,000 pt/hr GraphQL budget — the one the whole fleet shares, and the first one to die.
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
    val isRateLimited: body: string -> bool

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
    val classify: subject: string -> status: int -> body: string -> IoError
