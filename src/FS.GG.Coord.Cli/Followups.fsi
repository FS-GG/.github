namespace FS.GG.Coord.Cli

/// THE FOLLOW-UP QUEUE — a worker's promise to itself, at an altitude something can test.
///
/// `/pnext-item` §4 case 2 is a disposition: *"I can fix this, just not in THIS PR."* The worker who makes
/// that call is the only one who has the context, so the recipe's answer is *"take it next"* — and for as
/// long as that promise existed, **nothing carried it**. §6's last line was a bare `/pnext-item`, which
/// re-scans the board and lets `take` pick; `take` is not bound by what you filed, and the items most
/// likely to overlap a just-finished one are its own follow-ups. So the happy path handed the promise to a
/// scheduler that never heard it (#1061).
///
/// #1061 gave §6 a queue. It implemented it as **hand-rolled shell in the recipe**, and that is #1063: the
/// exact altitude `/pnext-item` itself argues against, in the file that argues it.
///
/// > **Nothing executes a recipe, so nothing tests one.** [...] the logic now lives in **one tested place**
/// > [...] A fifth copy is not discouraged; it is unwritable.
///
/// That text is about the merge gate (#724), which was wrong FOUR times in four copies, each fix editing a
/// copy. The argument was never specific to merging — it is about **stateful logic in prose that nothing
/// executes**. #1061's ten lines of shell were wrong four ways on first writing (a shared default path
/// under a fan-out; control flow in the comments and not the shell; a pop that spent the ref before the
/// take, so an `EX_RATE` dropped the promise; a hardcoded owner re-arming the bare-ref trap). All four were
/// caught by one review and fixed by hand — which is the point. **A one-off manual drive is not a gate.**
///
/// So the queue moves here, and the recipe calls instead of copies.
///
/// WHY NOT IN `Client.fs`, on `Chores`' terms exactly: the queue carries a correctness argument of its own —
/// four properties below, each of which fails silently if quietly dropped — and `Client.fs` is the org's
/// most contended file (#979: "a collision magnet"), where an argument is one the next editor re-litigates
/// by accident. A module with an `.fsi` is the argument written where it can be read. It compiles AFTER
/// `Client.fs` for one reason: `parseRefIn` and the exit constants live there, and this queue must reuse
/// both rather than grow a second answer to "what is a ref?" or "what does empty exit?" (#485).
///
/// THE FOUR PROPERTIES, and each is a bug #1061 shipped and this type forbids:
///
/// 1. **PER-WORKER BY CONSTRUCTION.** The path is keyed on the RESOLVED worker id (`Identity.resolve`), not
///    on an env var the caller may have skipped. A queue N workers share is a queue that races: two pops
///    read one head, the item is handed out TWICE (the four-workers-one-item waste of #888), and the next
///    ref is deleted having been handed to NOBODY — the queue losing the very promise it exists to keep.
///    That is the shared `flush` queue's shape, and the worker id is what dissolves it.
///
/// 2. **A QUEUED REF IS QUALIFIED, ALWAYS.** `add` refuses a bare `1063`. A queue entry is read back from a
///    DIFFERENT checkout than the one that wrote it — that is the whole reason the queue outlives the
///    worktree — so a ref that resolves against "where I am standing" is a ref that means something else by
///    the time it is popped. And it does not fail loudly: every target repo's numbering sits ENTIRELY inside
///    `.github`'s, so a bare number always resolves onto a real, unrelated, usually-closed `.github` row
///    (#548 made the old loud refusal quiet). The refusal is the only thing between a promise and a
///    silently-wrong claim.
///
/// 3. **EMPTY IS NOT UNREADABLE.** `Empty` is "I looked, and you owe yourself nothing" — `take`'s `EX_NONE`,
///    the same constant, deliberately not 0 (#585 reversed #480 on exactly this). `Unreadable` is "I could
///    not look." Collapsing them is #266's signature: a check reporting green over a subject it never read,
///    which here means a worker walking away from a promise the queue simply failed to open.
///
/// 4. **A POP IS ATOMIC.** One exclusive handle does the read and the rewrite. #1061's shell popped with a
///    `grep` followed by a separate `sed`, and was safe only because property 1 made the file private — a
///    verb must not inherit "nobody else is looking" as its ONLY defence, because that is precisely the
///    assumption property 1 was added to stop relying on.
module Followups =

    open System
    open FS.GG.Coord.Types
    open FS.GG.Coord.Cli.Identity

    /// What the caller asked the queue to do. Parsed from `Args` so the arity refusal is this module's,
    /// not a positional silently ignored (`Options`' residue rule, one level down).
    type Action =
        /// `followup add <ref>` — append a promise. Refuses an unqualified or unparseable ref.
        | Add of raw: string
        /// `followup peek` — the head, WITHOUT removing it.
        | Peek
        /// `followup pop` — the head, removed atomically.
        | Pop
        /// `followup list` — everything you owe yourself, in order.
        | List
        /// `followup audit` — inspect every local worker queue without consuming one.
        | Audit

    /// One queue the local fleet audit could read.
    type AuditedQueue =
        { Worker: string
          Age: TimeSpan
          Refs: Ref list }

    /// The local fact before GitHub correlation: queues old enough to need a live-claim check, or files
    /// that could not be read. `Unreadable` is never represented as an empty queue.
    type Audit =
        { Stale: AuditedQueue list
          Fresh: AuditedQueue list
          Unreadable: (string * string) list }

    /// The answer, as a TYPE — not a bool, not an option, and never an int at this layer.
    ///
    /// `ExitContractTests` says why the int is wrong here: threading exit codes through modules is how
    /// `take` ended up with a documented contract nothing could check for totality, and the remedy it names
    /// is "return a typed verdict rather than an int". This is that remedy, taken at the one place where it
    /// is still cheap — a new verb, before anything depends on its digits.
    type Outcome =
        /// Appended. Carries the CANONICAL ref, so the caller echoes what was stored, not what was typed.
        | Added of Ref
        /// The head, still queued (`peek`).
        | Head of Ref
        /// The head, now removed (`pop`).
        | Popped of Ref
        /// Every entry, in order (`list`). NEVER empty — an empty queue is `Empty`, so a caller cannot read
        /// "nothing owed" out of a successful listing by accident.
        | Listed of Ref list
        /// A LOOK THAT SUCCEEDED and found nothing. `take`'s EX_NONE, for `take`'s reason.
        | Empty
        /// The INPUT was refused — a bare ref, junk, the wrong arity. Never retryable; names its own remedy.
        | Refused of why: string
        /// The queue could not be READ or WRITTEN. "I could not look" — never `Empty`, never a silent 0.
        | Unreadable of why: string
        | Audited of Audit

    /// Parse `Args` into an action. `Error` carries a message already fit to print.
    val parse: args: string list -> Result<Action, string>

    /// Where this worker's queue lives: `<cache root>/followups/<worker id>.txt`.
    ///
    /// Under `Cache.root()` — so `FSGG_COORD_CACHE` isolates it exactly as it isolates every other piece of
    /// worker-local state, and a fixture that already redirects the cache gets this for free rather than
    /// needing a second env var nobody would remember to set.
    ///
    /// `Error` on a worker whose id is EMPTY. `Identity.resolve` slugs its input and returns `Ok` for a slug
    /// that trims to nothing (`--worker '///'`), which would key every such caller onto ONE file — property
    /// 1 defeated by the id itself. Refusing is the fail-closed read of #419: an id that cannot name a
    /// worker must not name a queue.
    val path: worker: Worker -> Result<string, string>

    /// Run an action against this worker's queue. The one IO surface; totally determined by `Outcome`.
    val apply: worker: Worker -> action: Action -> Outcome

    /// Enumerate every worker-keyed local queue at `now`, without consuming a ref.
    val audit: now: DateTimeOffset -> Audit

    /// The lines an outcome prints, and where. `fst` is stdout — the ref a caller consumes; `snd` is stderr
    /// — the commentary. Split here rather than in `run` so the projection is testable without capturing a
    /// console, and so `followup pop | xargs` can never be handed a diagnostic.
    val render: outcome: Outcome -> string list * string list

    /// An outcome → its exit code. `Client`'s constants, never a fresh digit: `Empty` IS `take`'s EX_NONE,
    /// and a second definition of 5 is a second thing to keep in step (#485).
    val exitCode: outcome: Outcome -> int

    /// The verb, as `Program` dispatches it — mirroring `Client.whoami`, and local for the same reason: the
    /// queue is a file, so it needs no token, no board and no budget. That is a property worth keeping: the
    /// promise must be poppable in exactly the state that strands a worker — an exhausted budget (§1 tells
    /// you to EXPECT one), where every board read is gone.
    val run: opts: Options.Options -> int
