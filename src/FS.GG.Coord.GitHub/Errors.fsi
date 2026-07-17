namespace FS.GG.Coord.GitHub

/// What can go wrong at the impure edge — as a TYPE, so it cannot arrive as an empty string.
///
/// THIS MODULE IS THE WHOLE POINT OF THE IO PORT. `FS.GG.Coord.Core` cannot fail open because it cannot
/// read anything; every one of epic #266's 51 children lives out here instead, in the layer that can. The
/// bash client's substrate makes the mistake nearly free — an error, an empty result, and a legitimate
/// "no" are all the empty string — and it paid for that with #344 (a confident empty board), #421 (a
/// rate-limited lookup reported as "not on board", complete with a remediation for the absence it had
/// invented), and #461 (a truncated page read as "nobody holds this lock").
///
/// So there is no function here that returns a bare value. A read returns `IoResult`, and the caller
/// cannot get at the value without saying what it will do when there isn't one.
module Errors =

    /// WHICH budget a rate limit was about.
    ///
    /// GitHub says this itself, in `X-RateLimit-Resource` on the 403 — so it is READ, never inferred from
    /// which call site we happened to be in. That distinction is the whole fix: the caller's belief about
    /// which budget it was spending is exactly the thing that was wrong.
    ///
    /// The two are NOT interchangeable and they do not die together. Measured on 2026-07-16, live: REST
    /// core sat at 0/5000 and 403'd every read while GraphQL had 3,639/5,000 still on the clock — the
    /// precise inverse of the asymmetry `Budget.fsi` is written around ("the GraphQL budget — the one the
    /// whole fleet shares, and the first one to die").
    type RateLimitResource =
        /// `X-RateLimit-Resource: graphql`. The points budget.
        | GraphQlBudget

        /// `X-RateLimit-Resource: core` (or any other REST resource — `search`, `code_search`, …). The
        /// request budget, and the one the CLAIM LOCK lives on by ADR-0034 §3.
        | RestBudget

        /// GitHub did not name a resource. A secondary/abuse-detector 403 arrives this way.
        ///
        /// This case exists so the tool can say "a rate limit, and I do not know which" rather than
        /// guessing. An invented budget name is worse than an absent one, for the same reason an invented
        /// reset is: it will be believed.
        | UnknownBudget

    /// Why a read or a write did not produce an answer.
    ///
    /// Each case is a distinct fact the caller acts on DIFFERENTLY, and the bugs came from collapsing
    /// them. `NotFound` and `RateLimited` are the pair that cost the most: #421 conflated them, so an
    /// exhausted budget looked exactly like an absent board item, and the tool's own remediation advice
    /// then added a second copy of an item that was already there.
    type IoError =
        /// A GitHub budget (or a secondary limit) is exhausted. TEMPORARY — try again later.
        ///
        /// This is `EX_RATE` (75, `EX_TEMPFAIL`), and it is NOT a protocol error. It is the one failure a
        /// board write may be QUEUED on (#510): every other failure is permanent, and replaying a
        /// permanent failure forever is how `flush` came to report success over writes it had dropped.
        ///
        /// `Resource` NAMES THE BUDGET THAT DIED, and it is why this case carries two fields instead of
        /// one. It used to carry only `resetAt`, and `explain` therefore hardcoded the word "GraphQL" for
        /// every rate limit it ever rendered — including REST ones. That is not a cosmetic slip: the
        /// sentence went on to recommend REST ("REST-only work still runs") at the exact moment REST was
        /// the budget that had stopped, which is the tool confidently pointing the worker at the one
        /// remedy that cannot work. #266's signature — a verdict about a subject the check cannot see —
        /// arriving in the error text instead of the read.
        ///
        /// `ResetAt` is read from the FAILING RESPONSE'S OWN `X-RateLimit-Reset` header, falling back to a
        /// GraphQL body's `resetAt`. Both are attached to the 403 itself, so they cost nothing and they
        /// describe the bucket that actually refused the call. This docstring used to claim the reset was
        /// "read from the FREE `rate_limit` endpoint" — nothing ever did that, and on this account that
        /// endpoint reports a DIFFERENT core counter than the one real requests are billed against, so
        /// believing it would have been worse than not reading it.
        | RateLimited of resource: RateLimitResource * resetAt: System.DateTimeOffset option

        /// The subject is not there. 404, and it MEANS it — the server said so.
        ///
        /// Never inferred from a failed read. That inference is #421.
        | NotFound of subject: string

        /// The token cannot see the subject. 401/403 that is NOT a rate limit.
        | Unauthorized of subject: string

        /// We got bytes, and they are not what they claim to be — a truncated page, a proxy's HTML error
        /// body, a 5xx rendered as text. **HTTP 200 with a body that is not JSON.**
        ///
        /// This is #461 exactly. `gh` exits 0, `jq` prints nothing and exits 0, and the empty string that
        /// falls out reads as "the lock is free". A malformed body is a FAILED READ and it is never an
        /// empty set.
        | Malformed of subject: string * detail: string

        /// A GraphQL response carrying `errors` — including the HTTP-200-with-errors shape, which is how
        /// GraphQL reports a partial mutation (see `Partial`).
        | GraphQlErrors of messages: string list

        /// A mutation document whose EARLIER aliases landed and whose later ones did not.
        ///
        /// GraphQL executes mutations SERIALLY and reports the failure as HTTP 200 carrying BOTH `data`
        /// and `errors`, with `errors[].path[0]` naming the failing alias. So the body says exactly which
        /// writes took effect. This is `EX_PARTIAL` (9), and it is the one failure that must NEVER be
        /// queued for replay: replaying the document would rewrite the half that already landed.
        | Partial of applied: string list * failed: (string * string) list

        /// The transport itself failed — DNS, connection reset, timeout. We never reached an answer.
        | Transport of detail: string

        /// An HTTP status we have no specific reading for. Carries the body, because a refusal nobody can
        /// read is a refusal that did not happen.
        | Http of status: int * body: string

    /// Every IO operation returns this. There is no accessor that hands back a bare value.
    type IoResult<'a> = Result<'a, IoError>

    /// The client's exit code for an error — the contract the corpus pins, and the reason it is a
    /// function rather than a convention.
    ///
    /// `EX_RATE` is 75 (`EX_TEMPFAIL`) and it is the BACK-OFF signal: `take` returns it without retrying,
    /// because an exhausted budget is not a lost race and three more attempts just spend REST calls
    /// confirming the same 403.
    val exitCode: error: IoError -> int

    // These three derive from `FS.GG.Coord.ExitCode.toInt` now (#918, ADR-0046) — one number space,
    // one declaration site — so they are plain `val`s, not `[<Literal>]`s pinning a digit here.

    /// `EX_RATE` (75) — the budget is exhausted. Try again later.
    val ExRate: int

    /// `EX_OFFBOARD` (8) — not an item on the board. A different fact from a RED verdict, and a
    /// PERMANENT one. Was `3` until #918 moved it off the verdict code it collided with.
    val ExOffboard: int

    /// `EX_PARTIAL` (9) — a batch document that applied some aliases and failed others. Was `4` until
    /// #918 moved it off the NO-VERDICT code it collided with.
    val ExPartial: int

    /// The operator-facing sentence. It must NAME the condition, because the whole failure class this
    /// port exists to end is one where the tool said nothing and the caller assumed the best.
    val explain: error: IoError -> string

    /// Is this error the one — and the only one — that a board write may be DEFERRED on?
    ///
    /// A predicate rather than a `match` at each call site, because #510 was exactly this test being
    /// written in one place and not the other: `claim` queued on an exhausted budget, `set-field` and
    /// `done --flip` printed the same "the write is QUEUED" promise and dropped the write on the floor.
    val isQueueable: error: IoError -> bool
