namespace FS.GG.Coord.GitHub

/// THE WRITE PATH (ADR-0040 Phase B) — where the nineteen remaining defects die, by construction.
///
/// ADR-0040 counted them. The engine's flip retired **four to six** issues: the schedulability predicate,
/// and that is all it ever could retire, because a pure core cannot fix a mutation. The other **nineteen**
/// — `widen` rewriting a live holder's touch-set (#706), a PATCH that precedes its own re-check (#523),
/// `done --flip` closing an open parent (#614), a heartbeat that forgets the column it overwrote (#550) —
/// are write-path defects, and every one of them survived the flip untouched.
///
/// THE ORGANISING IDEA, AND IT IS THE WHOLE MODULE: **a precondition is an ARGUMENT, not a check.**
///
/// A check can be forgotten. `widen` forgot to verify that the caller held the claim, and rewrote a live
/// holder's touch-set (#706) — and #646 then proposed to keep it that way. No amount of review reliably
/// catches a missing `if`, because a missing `if` looks exactly like code that was never needed.
///
/// An argument cannot be forgotten. It does not compile. So `widen` does not TAKE a ref and CHECK a claim;
/// it takes a `Held` — a capability that can only be obtained by winning the CAS or by re-reading the
/// marker and confirming it is ours. There is no constructor for it. #706 is not fixed here. It is
/// *unexpressible*.
///
/// The same move retires #523: `widen` PATCHed the body and re-checked it afterwards, so on an exhausted
/// budget the declaration was already rewritten when the refusal arrived. Here the re-check PRODUCES the
/// value the PATCH consumes — `rewrite` returns a `Rewritten`, and `patchBody` accepts nothing else. A
/// PATCH that precedes its own validation cannot be written down.
///
/// WHAT IS HERE, AND WHAT IS NOT — STATED, BECAUSE A MODULE THAT OVERSTATES ITS SCOPE IS A MODULE THAT
/// GETS TRUSTED FOR THINGS IT DOES NOT DO.
///
/// Here: the claim CAS, `verifyHeld`, `widen`, `heartbeat`, `release`, `say`, `child`. These are the
/// ISSUE-side writes — REST, comment- and body-shaped, on the budget that survives, and they are where the
/// capability discipline actually bites.
///
/// The BOARD-side writes — `set-field`, its aliased batch (#448), the deferred-write queue — live in
/// `Board`, because they are Projects v2 mutations over GraphQL and they are metered on the budget that
/// dies first. The split is the REST/GraphQL line, and that line is a correctness boundary: **a lock may
/// never live on the budget that dies first** (ADR-0034 §3, re-ratified by ADR-0040 C4).
///
/// The DONE-STAMP — `done --flip` and `epic_rollup` — lives in `Done`, because it is not more writes: it is
/// a PRECONDITION ENGINE with a write at the end of it, and its preconditions are PURE (`Done.verify` is a
/// total function over facts, so it cannot mistake a failed read for a satisfied precondition — which is
/// how that whole family of bugs was born).
module Writes =

    open FS.GG.Coord.Types
    open Errors
    open Transport

    /// PROOF THAT THIS WORKER HOLDS THIS ITEM'S LOCK, RIGHT NOW.
    ///
    /// The type is ABSTRACT and there is no public constructor. The only ways to hold one are to win the
    /// CAS (`claim`) or to re-read the markers and confirm the live winner is us (`verifyHeld`). That is
    /// the entire mechanism, and it is what turns a class of forgettable `if` statements into a class of
    /// compile errors.
    ///
    /// It carries the marker id because every subsequent operation needs it: a heartbeat PATCHes that
    /// comment, a release DELETEs it, and #550 is what happens when you find the marker by WORKER STRING
    /// instead — a twin with the same id deletes a lock it does not hold.
    [<Sealed>]
    type Held =
        /// The item this lock is on.
        member Ref: Ref

        /// Who holds it.
        member Worker: WorkerId

        /// The comment id of OUR marker. The lock IS this number.
        member MarkerId: int64

        /// The board column this claim overwrote, so `release` can put it back rather than guessing
        /// `Ready` (#481). `None` means the claim recorded none — and a column nobody recorded cannot be
        /// restored, so `release` says so instead of inventing one.
        member PreviousStatus: BoardStatus option

    /// What happened when we went for the lock.
    ///
    /// NOT A BOOL, AND NOT AN OPTION. Each case is a different instruction to the worker, and collapsing
    /// them is how "somebody else has it" became indistinguishable from "we could not tell" — which,
    /// under the CAS, must be read as a LOSS.
    type ClaimOutcome =
        /// We won. Here is the proof.
        | Won of Held

        /// Another worker holds it, and their lock is live. Their id, so the worker can `say` to them.
        | Lost of WorkerId

        /// **WE CANNOT TELL, AND THAT IS A LOSS.** The re-read failed, or our own marker was not in it.
        ///
        /// This is the CAS's sharpest rule. Our marker is already posted by the time we re-read, so every
        /// exit from here must either KEEP it (we won) or REMOVE it (we lost, or we cannot tell) — never
        /// abort in between and leave it orphaned. A "cannot tell" that resolved to Won would hand two
        /// workers the same files.
        | Undecided of reason: string

        /// The item carries a marker we could not parse a worker out of — a claim held by NOBODY, which
        /// BLOCKS. A half-written lock fails closed: if it vanished, the item would read as free.
        | BlockedByUnparseableMarker

    /// TAKE THE LOCK. The comment-order compare-and-swap, and it is a REAL one.
    ///
    /// GitHub issues comment ids from a single server-side sequence, so "the lowest live marker id wins" is
    /// a total order that every racer observes identically — a genuine CAS with a genuine linearisation
    /// point. ADR-0034 §3 established this and ADR-0040 C4 re-ratifies it: **this port changes the
    /// language, not the substrate.** The CAS is re-expressed, never re-designed.
    ///
    /// It lives on REST, deliberately. The GraphQL budget is the first thing to die under fan-out (#418),
    /// and **a lock may never live on the budget that dies first.**
    ///
    /// The protocol: read the live markers; refuse if another holds one; post ours; RE-READ; take the
    /// lowest live id as the winner; if that is not us, delete ours and back off.
    val claim:
        transport: IGitHubTransport ->
        leaseMinutes: int ->
        worker: WorkerId ->
        session: SessionId option ->
        ref: Ref ->
        previousStatus: BoardStatus option ->
            IoResult<ClaimOutcome>

    /// Re-read the markers and confirm the live winner is US.
    ///
    /// The other door to a `Held`, for a worker that took its lock in an earlier process — every command
    /// after `claim` is a fresh invocation, so without this the capability could not survive the one thing
    /// it exists to guard.
    ///
    /// It fails CLOSED: an unreadable marker set yields an error, never a `Held`. Manufacturing a
    /// capability from a failed read would be the fail-open this entire type exists to prevent, sitting
    /// inside its own constructor.
    val verifyHeld:
        transport: IGitHubTransport ->
        leaseMinutes: int ->
        worker: WorkerId ->
        ref: Ref ->
            IoResult<Held option>

    /// A touch-set that has been VALIDATED — every token is matchable.
    ///
    /// Abstract, and produced only by `validate`. An unmatchable token reserves NOTHING, so it conflicts
    /// with nothing and would read as DISJOINT against every other worker (#273) — a lock that succeeds
    /// under exactly the conditions it exists to prevent. A declaration that cannot be reserved may not be
    /// written to an issue body, and here it cannot be: `rewrite` takes one of these and nothing else.
    [<Sealed>]
    type Validated =
        member Tokens: string list

    /// Validate a touch-set. This is the ONLY way to make a `Validated`.
    ///
    /// A refusal names what WOULD have been accepted — a refusal that does not is a refusal that only moves
    /// the worker's confusion one step later.
    val validate: tokens: string list -> Result<Validated, string>

    /// A body that has been REWRITTEN and is ready to PATCH.
    ///
    /// Abstract, and produced only by `rewrite`. This is #523's fix, and it is structural: `widen` used to
    /// PATCH the body and re-check it afterwards, so on an exhausted budget the declaration was already
    /// gone when the refusal arrived. Now the re-check PRODUCES the value the PATCH consumes. **A PATCH
    /// that precedes its own validation cannot be written down.**
    [<Sealed>]
    type Rewritten =
        member Body: string

    /// Rewrite an issue body's `Paths:` declaration. Fence-aware — a `Paths:` inside a fenced code block is
    /// PROSE, not a declaration, and rewriting it would corrupt an example into a reservation.
    val rewrite: body: string -> paths: Validated -> Rewritten

    /// WIDEN A TOUCH-SET. Takes the `Held`, so #706 is unexpressible.
    ///
    /// The defect: `widen` never checked that the caller held the claim, so a worker rewrote a LIVE
    /// holder's touch-set by accident — and the item's own reservation, the thing protecting the files
    /// somebody was standing in, was changed out from under them. There is no `if` here to forget: the
    /// capability is the first argument.
    ///
    /// It takes a `Rewritten`, so #523 is unexpressible too. Nothing is PATCHed that has not already been
    /// validated and rendered.
    val widen: transport: IGitHubTransport -> held: Held -> rewritten: Rewritten -> IoResult<unit>

    /// RENEW THE LEASE. Takes the `Held`.
    ///
    /// It rewrites the WHOLE marker body, which is why the `Held` carries `PreviousStatus`: the marker is
    /// replaced, so every field it held must be re-emitted. #550 is what happens when a heartbeat picks its
    /// marker by the WORKER STRING alone — a twin deletes a lock it does not hold — and it is why the
    /// marker is addressed by its COMMENT ID here, which is the only thing that identifies it uniquely.
    ///
    /// A 2-hour-beating claim that forgot the column it overwrote is the same bug one turn later: the
    /// marker is rewritten from the capability, not from a guess.
    val heartbeat: transport: IGitHubTransport -> leaseMinutes: int -> held: Held -> IoResult<Held>

    /// DROP THE LOCK. Takes the `Held`.
    ///
    /// What the board column becomes afterwards is a decision, and it has four outcomes — not two. A Status
    /// we could not READ is not a Status we may OVERWRITE (#481), and once the marker is gone the lease is
    /// already dropped, so nothing below may abort the release: a board that cannot be read or written
    /// leaves the column alone and REPORTS it, rather than failing and leaving a lock behind.
    val release: transport: IGitHubTransport -> held: Held -> IoResult<BoardStatus option>

    /// Post a message to another worker (`fsgg:msg`).
    ///
    /// Does NOT take a `Held` — deliberately. A worker who has just LOST a race, or who is warning the
    /// holder about an overlap, must still be able to speak. Requiring the lock to send a message would
    /// silence exactly the worker with something urgent to say.
    val say: transport: IGitHubTransport -> from: WorkerId -> toWorker: WorkerId -> ref: Ref -> text: string -> IoResult<unit>

    /// Attach a child issue to a parent (`sub_issues`).
    ///
    /// The child's REST INTEGER ID, never its number — two repos can each have an issue #7, and posting a
    /// number where an id belongs attaches the wrong issue silently. It is sent as a JSON NUMBER, not a
    /// string: the string form collects a 422, which is what `gh api -F` (rather than `-f`) was for.
    val child: transport: IGitHubTransport -> parent: Ref -> childId: int64 -> IoResult<unit>
