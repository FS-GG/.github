namespace FS.GG.Coord.GitHub

/// What can go wrong at the impure edge — as a TYPE, so it cannot arrive as an empty string.
///
/// THIS MODULE IS THE WHOLE POINT OF THE IO PORT. `FS.GG.Coord.Core` cannot fail open because it cannot
/// read anything; every one of epic #266's 51 children lives out here instead, in the layer that can. The
/// bash client's substrate makes the mistake nearly free — an error, an empty result, and a legitimate
/// "no" are all the empty string — and it paid for that with #344 (a confident empty board), #421 (a
/// rate-limited lookup reported as "not on board", with a remediation that CREATES A DUPLICATE), and #461
/// (a truncated page read as "nobody holds this lock").
///
/// So there is no function here that returns a bare value. A read returns `IoResult`, and the caller
/// cannot get at the value without saying what it will do when there isn't one.
module Errors =

    /// Why a read or a write did not produce an answer.
    ///
    /// Each case is a distinct fact the caller acts on DIFFERENTLY, and the bugs came from collapsing
    /// them. `NotFound` and `RateLimited` are the pair that cost the most: #421 conflated them, so an
    /// exhausted budget looked exactly like an absent board item, and the tool's own remediation advice
    /// then added a second copy of an item that was already there.
    type IoError =
        /// The GraphQL budget (or a secondary limit) is exhausted. TEMPORARY — try again later.
        ///
        /// This is `EX_RATE` (75, `EX_TEMPFAIL`), and it is NOT a protocol error. It is the one failure a
        /// board write may be QUEUED on (#510): every other failure is permanent, and replaying a
        /// permanent failure forever is how `flush` came to report success over writes it had dropped.
        ///
        /// `ResetAt` is read from the FREE `rate_limit` endpoint — the meter read does not itself spend
        /// budget, which is what makes "back off until the reset" a strategy rather than a guess.
        | RateLimited of resetAt: System.DateTimeOffset option

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
        /// writes took effect. This is `EX_PARTIAL` (4), and it is the one failure that must NEVER be
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

    /// `EX_RATE` — the budget is exhausted. Try again later.
    [<Literal>]
    val ExRate: int = 75

    /// `EX_OFFBOARD` — not an item on the board. A different fact, and a PERMANENT one.
    [<Literal>]
    val ExOffboard: int = 3

    /// `EX_PARTIAL` — a batch document that applied some aliases and failed others.
    [<Literal>]
    val ExPartial: int = 4

    /// The operator-facing sentence. It must NAME the condition, because the whole failure class this
    /// port exists to end is one where the tool said nothing and the caller assumed the best.
    val explain: error: IoError -> string

    /// Is this error the one — and the only one — that a board write may be DEFERRED on?
    ///
    /// A predicate rather than a `match` at each call site, because #510 was exactly this test being
    /// written in one place and not the other: `claim` queued on an exhausted budget, `set-field` and
    /// `done --flip` printed the same "the write is QUEUED" promise and dropped the write on the floor.
    val isQueueable: error: IoError -> bool
