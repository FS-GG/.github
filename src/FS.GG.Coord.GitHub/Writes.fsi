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
/// The same move enforces #523's GRAMMAR half by construction: a bad token cannot reach the write, because
/// `rewrite` returns a `Rewritten`, `patchBody` accepts nothing else, and a PATCH that precedes its own
/// grammar validation cannot be written down. #523 PROPER — the COLLISION re-check (#353, "the colliding
/// workers are never told") — is a SEQUENCING obligation the type system does not carry, and it lives in
/// `Client.widen`: the overlap scan runs against the proposed touch-set and GATES the PATCH, so an unreadable
/// scan (an exhausted GraphQL budget) REFUSES with the body untouched rather than landing a declaration no
/// one verified. Retired there, not here; see the Phase-D payoff disposition
/// (docs/2026-07-16-phase-d-payoff-disposition.md).
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

    /// Append a durable, caller-owned issue comment.  The caller supplies a fully validated marker;
    /// this boundary returns its identity so subsequent live reads can bind evidence to the exact write.
    val postIssueComment:
        transport: Transport.IGitHubTransport -> ref: FS.GG.Coord.Types.Ref -> body: string -> Errors.IoResult<int64>

    /// Result of an idempotent durable protocol-comment write. Both cases are authoritative: the exact
    /// body was observed in a complete post-write comment census. A response alone is never the receipt.
    type DurableCommentWrite =
        | CommentWritten of commentId: int64
        | CommentAlreadyPresent

    /// Append one immutable protocol receipt exactly once. `marker` identifies its ledger slot; an
    /// existing different body at that marker is a conflict. The function re-reads after success and
    /// after a transport error, so response loss converges and unreadable/conflicting state fails closed.
    val writeDurableComment:
        transport: Transport.IGitHubTransport ->
        ref: FS.GG.Coord.Types.Ref ->
        marker: string ->
        body: string ->
            Errors.IoResult<DurableCommentWrite>

    /// Validate an intake draft before issuing its REST issue-create request. Invalid drafts spend no IO.
    val createIntake: transport: Transport.IGitHubTransport -> draft: FS.GG.Coord.Intake.Draft -> Errors.IoResult<FS.GG.Coord.Types.Ref>

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

        /// The session recorded in the marker, so `heartbeat` re-emits `session=` rather than dropping it
        /// (#1149). A PATCH rewrites the whole body, and a field the capability does not hold is a field the
        /// rewrite forgets — the way `heartbeat` used to forget `prev=` (#550). Without it, the first
        /// heartbeat left a SESSIONLESS marker, which `twinSession` cannot tell from a human's, so a same-id
        /// twin's `claim` read `Renewed` instead of `Twin` and both workers held the item. `None` is a marker
        /// that genuinely carries no session (a human, a pre-#419 marker, a harness that exports none).
        member Session: SessionId option

        /// The board column this claim overwrote, so `release` can put it back rather than guessing
        /// `Ready` (#481). `None` means the claim recorded none — and a column nobody recorded cannot be
        /// restored, so `release` says so instead of inventing one.
        member PreviousStatus: BoardStatus option

        /// The repository in which this claim's path tokens live. New markers record it so active
        /// collision reads need no board query; `None` is a legacy marker and falls back conservatively.
        member PathRepo: string option

        /// The agent-contract digest captured at dispatch. Heartbeats re-emit this exact value rather than
        /// replacing work attribution with whatever contract happens to be active at renewal time.
        /// `None` preserves a legacy marker's absence.
        member AgentContract: string option

    /// **MAY THIS CLAIM TAKE AN ITEM ANOTHER WORKER IS HOLDING RIGHT NOW?** (#1620)
    ///
    /// A TYPE RATHER THAN A `bool`, because the two answers are not "more" and "less" of one thing — one of
    /// them DELETES another worker's lock, and a positional boolean at a call site says nothing about which
    /// way round it is. `Chores.offer` and `claim` are the only callers, and both should read as English.
    ///
    /// It exists because the org had no sanctioned recovery route for a holder that DIED mid-item. `reap`
    /// refuses an item with an open `item/<n>-*` PR (#581), `adopt` refuses a claim that is not stale — both
    /// correctly — and both told the recovering worker to run `claim --force`, which refused identically with
    /// and without the flag. A documented dead end is a standing invitation to invent an undocumented route,
    /// and the two that were found and declined were impersonation (`release --worker <the dead worker>`) and
    /// faking staleness with a shrunken `--lease`. Give death a sanctioned path, and make it loud.
    type ClaimForce =
        /// The ordinary claim. Another worker's live lock is a refusal — `Lost`, before we post anything.
        | RefuseLiveHolder

        /// `claim --force`. EVICT the live holder's marker and take the item, reporting `Stolen` so the
        /// caller can announce the theft. Deliberately NOT a liveness heuristic: a blanket steal with loud
        /// accounting was preferred over guessing whether a slow worker is a dead one.
        ///
        /// It does NOT override the two refusals that are about a broken IDENTITY rather than a contested
        /// item: a `Twin` marker (our id, another session — #419) and an unparseable marker (a lock held by
        /// nobody) still refuse under it, because forcing there deletes a lock somebody may be working
        /// behind, and the remedy for a broken identity is a new identity, not a bigger hammer.
        | StealLiveHolder

    /// **WHO THE CALLING PROCESS IS, AS OPPOSED TO WHO IT SAYS IT IS** (#1646).
    ///
    /// `--worker <id>` is an ASSERTION of identity, never a proof of one. `session=` was added by #419/#1031
    /// to be the second factor that turns "your id is not proof" into something checkable — but every
    /// subagent of one Claude Code session shares one session id, so under a fan-out the second factor is one
    /// every sibling also holds. `twinSession` concludes twin only when both sessions are KNOWN and DIFFER,
    /// so a caller passing another worker's id while sharing their session matched on both legs and was
    /// handed the capability that authorises PATCHing and DELETING their marker — quieter, and one flag
    /// shorter, than the sanctioned `claim --force` steal (#1620) it was meant to be displaced by.
    ///
    /// So the marker check gets a THIRD fact, and it is the one `--worker` cannot restate: **the identity
    /// this process resolves for ITSELF, with the flag taken away.** A caller that derives `kite-461` and
    /// asks to act as `vole-418` is not failing a session comparison; it is naming somebody else, which is
    /// the only thing that shape is ever needed for.
    ///
    /// A TYPE RATHER THAN A `WorkerId option`, for `ClaimForce`'s reason: `None` is the fail-OPEN answer
    /// here, and an `option` lets a call site reach it by omission. Each caller has to say which of the two
    /// it means.
    ///
    /// **IT IS NOT A PROOF, AND MUST NOT BE DESCRIBED AS ONE.** `$FSGG_WORKER` is an assertion too, so a
    /// caller determined to impersonate can still export the victim's id and be believed. What this removes
    /// is the property that made the hole worth closing: that impersonating was the QUIETEST route, reachable
    /// by adding one flag to a command the worker was already running. Direction (a) in #1646 — a per-claim
    /// secret the marker is bound to — is the only one of the three that would be a proof, and this is not it.
    type SelfIdentity =
        /// This process resolves an identity of its own without being told: `$FSGG_WORKER`, or a harness
        /// session id. Naming a DIFFERENT worker on a marker that worker holds is then an impersonation, by
        /// the caller's own account of itself.
        | Derives of WorkerId

        /// This process resolves nothing: a human operator, a harness that exports no session and sets no
        /// `$FSGG_WORKER`. `--worker` is the ONLY way such a caller can say who it is, so there is nothing to
        /// measure it against and the impersonation question is UNASKABLE — never answered "no" by default.
        /// #1646 states this residue rather than pretending it away: a caller that unsets its own identity
        /// first lands here, and the tool cannot tell it from the operator this case exists for.
        | DerivesNothing

    /// One marker in a complete authoritative census surrounding a forced-claim transition.
    type ClaimMarkerObservation =
        { MarkerId: int64
          Worker: WorkerId
          Live: bool }

    /// The complete ordered marker set and its existing comment-order winner.
    type ClaimMarkerCensus =
        { WinnerMarkerId: int64 option
          Markers: ClaimMarkerObservation list }

    /// The authoritative observations governing a forced-claim result. `After = None` means the
    /// post-operation census was unreadable; it never means the item was empty.
    type ForcedClaimCensuses =
        { Before: ClaimMarkerCensus
          After: ClaimMarkerCensus option }

    /// What happened when we went for the lock.
    ///
    /// NOT A BOOL, AND NOT AN OPTION. Each case is a different instruction to the worker, and collapsing
    /// them is how "somebody else has it" became indistinguishable from "we could not tell" — which,
    /// under the CAS, must be read as a LOSS.
    type ClaimOutcome =
        /// We won. Here is the proof — and the workers whose STALE markers we COLLECTED to win it.
        ///
        /// A stale marker is a lapsed lease, and the next claimant must COLLECT it, never merely out-order
        /// it: an ignored stale marker is exactly what `heartbeat` later resurrects underneath the new
        /// holder — two live markers, one item, the double-hold the whole protocol exists to prevent. So a
        /// won claim DELETES the stale debris on the item (a 404 is success — a peer may have collected the
        /// same marker first), and hands back the OTHER workers it evicted so the caller can TELL them, on
        /// their own item, that their expired claim was collected. Our OWN stale marker (a renew of a claim
        /// that went stale) is collected too — so exactly one marker survives — but is NOT in this list:
        /// you do not message yourself. Collection is best-effort; a stale marker we could not delete is
        /// LEFT for `reap`, never a reason to fail a claim we have already won.
        | Won of held: Held * collected: WorkerId list

        /// **WE ALREADY HELD IT — this is a RE-CLAIM, not a fresh win.** A live marker already bearing our
        /// worker id is renewed IN PLACE (a PATCH of the one marker we have, never a second POST that we
        /// would then lose to our own first), so a slow worker re-claiming ends with ONE marker, and `take`
        /// retries stay idempotent. It carries the collected stale debris exactly as `Won` does.
        ///
        /// It is a SEPARATE outcome from `Won` because this path **bypasses the CAS entirely** — and an id
        /// is not proof of ownership (#419: rules 4/5 can hand one id to several workers). So a caller whose
        /// id is shared must WARN here that it adopted an existing marker without running the CAS, the one
        /// place the fresh-claim path's shared-id warning would otherwise never be reached.
        | Renewed of held: Held * collected: WorkerId list

        /// **WE TOOK IT FROM A LIVE HOLDER** — the `--force` steal (#1620), and the ONLY outcome that
        /// reports one. `from` is every worker whose LIVE marker we evicted to clear the way; `collected` is
        /// the ordinary stale debris, exactly as `Won` carries it, and the two are separate lists because
        /// they are separate facts: one is a lapsed lease being tidied up, the other is a working worker
        /// being displaced.
        ///
        /// IT IS A CASE OF ITS OWN RATHER THAN A `Won` WITH A LONGER LIST, because a caller that scripts
        /// this needs to tell "I was handed a free item" from "I took a working worker's item" — the second
        /// is a decision somebody may have to answer for. A `Won` that quietly meant "and I deleted
        /// someone's lock" is the silent-transfer bug, pre-installed.
        ///
        /// The DUTY to announce the theft does not ride on this case, though: it rides on `onEvict` below,
        /// because a lock can be destroyed on executions that never reach here.
        | Stolen of held: Held * from: WorkerId list * collected: WorkerId list * censuses: ForcedClaimCensuses

        /// A response-lost replacement POST was found by authoritative census and now wins, but the old
        /// holder disappeared before this process deleted it. No theft is invented; the forced replacement
        /// receipt still carries the final pre/post censuses.
        | ReplacementWon of held: Held * collected: WorkerId list * censuses: ForcedClaimCensuses

        /// Replacement creation failed, a complete post-census proves the incumbent still wins, and no
        /// incumbent deletion was attempted. This is a typed old-holder-standing result, not raw transport.
        | ReplacementPostFailed of holder: WorkerId * holderMarkerId: int64 * reason: string * censuses: ForcedClaimCensuses

        /// The replacement marker was posted before destructive cleanup began, but deleting one incumbent
        /// failed. `replacement` remains on the item; `removed` names only deletions observed successful;
        /// `failed`/`failedMarkerId` name the marker that still stands. Re-running `claim --force` with the
        /// same identity reconciles this deterministic two-marker state instead of posting another marker.
        | CleanupRequired of replacement: Held * removed: WorkerId list * failed: WorkerId * failedMarkerId: int64 * reason: string * censuses: ForcedClaimCensuses

        /// Incumbent cleanup completed, but the complete post-operation census could not be read. The
        /// replacement is deliberately retained: withdrawing it after destructive cleanup could create the
        /// zero-marker state this transition forbids. Retry is authorized only to re-read/reconcile.
        | PostStateUnreadable of replacement: Held option * removed: WorkerId list * reason: string * censuses: ForcedClaimCensuses

        /// The post-operation census is complete, the replacement marker is absent, and a foreign live
        /// holder remains authoritative. The caller may report that the old holder stands; it may not infer
        /// that retry is safe merely from a transport error.
        | OldHolderStands of replacementMarkerId: int64 * holder: WorkerId * holderMarkerId: int64 * removed: WorkerId list * censuses: ForcedClaimCensuses

        /// The complete post-census was readable and contained no live marker, including no replacement.
        /// This is an observed anomaly rather than an ordinary loss; the marker id names the capability that
        /// vanished so the caller can report the exact failed transition.
        | NoHolderRemaining of replacementMarkerId: int64 option * removed: WorkerId list * censuses: ForcedClaimCensuses

        /// Cleanup completed, but a fresh foreign marker won the unchanged comment-order election. The
        /// complete pre/post census makes this distinct from an ordinary refusal by the original holder.
        | ForcedClaimLost of winner: WorkerId * censuses: ForcedClaimCensuses

        /// Another worker holds it, and their lock is live. Their id, so the worker can `say` to them.
        ///
        /// Under `StealLiveHolder` this outcome still happens — it just means a DIFFERENT thing: not "the
        /// holder refused us" but "we cleared the item and then lost the fresh race to a worker who arrived
        /// after the eviction". Comment order decides that, as it decides every other claim.
        | Lost of WorkerId

        /// **THE MARKER CARRIES OUR WORKER ID, BUT A DIFFERENT SESSION.** An id two workers share is not a
        /// lock (#419): agents that derived — or were handed — the same id are TWINS, and adopting their
        /// live lock as a heartbeat would silently put both of them on one item. Refuse instead, and carry
        /// the OTHER session so the caller can report it. We conclude "twin" ONLY when both sessions are
        /// known: a sessionless marker (a human, a pre-#419 marker) is genuinely indistinguishable from ours
        /// and keeps the old behaviour, and our OWN session re-claiming is a heartbeat, not a twin.
        | Twin of theirs: SessionId

        /// **WE ASKED TO CLAIM AS A WORKER WE ARE NOT** (#1646). `claim` is `Held`'s OTHER door, and it is
        /// refused for the WHOLE function rather than on the arm where the hole was first found:
        ///
        ///   * the RE-CLAIM arm renewed a live marker on the strength of its id alone and handed back
        ///     `Renewed` — the same impersonation `verifyHeld` grants, through the door #1031 did not have to
        ///     consider, and invisible to `twinSession` because the impersonator's session IS the holder's;
        ///   * the `--force` STEAL arm evicted a live holder and posted under the named id, so the notice
        ///     #1620 requires was written over a THIRD party's name. That does not bypass the accounting;
        ///     it falsifies it, in the only surviving record of a lock that no longer exists;
        ///   * the FRESH-CAS arm planted a marker under a name whose own creator then cannot heartbeat or
        ///     release it, because every verb that would operate it goes through `verifyHeld`.
        ///
        /// Refusing three of the four would leave `claim` and `verifyHeld` disagreeing about one question,
        /// which is exactly what `impersonated` is factored to make impossible. So it is asked ONCE, before
        /// the marker read: the refusal spends nothing and touches nothing.
        | Impersonates of derived: WorkerId * named: WorkerId

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
    ///
    /// `readPreviousStatus` is the pre-claim board column (#481), and it is a THUNK, not a value, for one
    /// reason: the read costs a GraphQL point, and it may be spent ONLY when we are actually about to post a
    /// fresh marker. A lost race and an idempotent re-claim NEVER call it — a lost race because the item is
    /// already someone else's, a re-claim because it inherits the column the superseded marker recorded. The
    /// claim is the hottest path on the scarcest budget in the org (#418), so paying this read on every
    /// losing attempt would be the exact regression #481 is careful not to introduce.
    /// `force` decides ONE arm of the match below — the one where another worker's marker is live. Under
    /// `StealLiveHolder` posts a replacement before deleting foreign live markers, then runs the unchanged
    /// comment-order election. A failed post leaves the incumbent untouched; a failed cleanup retains the
    /// replacement; a retry reuses it rather than posting marker debris. Force grants no election priority.
    ///
    /// `onEvict` is called with exactly the workers whose LIVE marker deletion was confirmed, after the
    /// replacement post and before the final election result. It is never called on the ordinary path, and
    /// never with an empty list.
    ///
    /// **IT IS NOT THE SAME EVENT AS THE `Stolen` OUTCOME, AND THAT IS THE POINT.** `Stolen` says "we
    /// evicted somebody AND got the item"; `onEvict` says "somebody's live lock is gone", which is true of
    /// strictly more executions — cleanup can stop after one of several deletions, the post-census can be
    /// unreadable, or a newcomer can win. A replacement-post failure invokes no callback because no
    /// incumbent was touched.
    val claimScoped:
        transport: IGitHubTransport ->
        leaseMinutes: int ->
        force: ClaimForce ->
        onEvict: (WorkerId list -> unit) ->
        worker: WorkerId ->
        self: SelfIdentity ->
        session: SessionId option ->
        ref: Ref ->
        readPreviousStatus: (unit -> BoardStatus option) ->
        readPathRepo: (unit -> string option) ->
        admitNew: (unit -> IoResult<unit>) ->
            IoResult<ClaimOutcome>

    /// Backward-compatible claim for subjects with no board path-scope projection.
    val claim:
        transport: IGitHubTransport ->
        leaseMinutes: int ->
        force: ClaimForce ->
        onEvict: (WorkerId list -> unit) ->
        worker: WorkerId ->
        self: SelfIdentity ->
        session: SessionId option ->
        ref: Ref ->
        readPreviousStatus: (unit -> BoardStatus option) ->
            IoResult<ClaimOutcome>

    /// Merge an open pull request only if GitHub still sees the inspected head SHA.  A changed head is a
    /// refusal, never a merge of code the delivery receipt did not inspect.
    val mergeAtHead: transport: IGitHubTransport -> ref: Ref -> pr: int -> headSha: string -> IoResult<bool>

    /// What re-reading the markers says about whether WE hold the lock.
    ///
    /// NOT AN OPTION, for `ClaimOutcome`'s reason: each case is a different instruction to the worker, and
    /// `None` collapsed two of them. "You do not hold this" and "your TWIN holds this" have opposite
    /// remedies — re-claim it, versus do not go anywhere near it until you have a new identity — and the
    /// caller cannot tell them apart by worker id, because a twin's id is ours (#419).
    type HeldOutcome =
        /// The live winner is us. Here is the proof.
        | Holds of Held

        /// The live winner is not us — somebody else's marker, or no live marker at all (a lapsed lease).
        /// The worker may re-claim.
        | DoesNotHold

        /// **THE LIVE MARKER CARRIES OUR WORKER ID, BUT A DIFFERENT SESSION.** A twin holds this lock
        /// (#419), and their id is ours, so every id-keyed question about it answers "yes, that's you".
        ///
        /// It is a case of its own precisely because `DoesNotHold` would be MISREAD: a caller diagnosing it
        /// re-reads the markers, finds our own id on the live winner, and concludes the only other thing
        /// that fits — "your lease expired, re-claim it" — which is advice to go take a lock a twin is
        /// working behind. Carries the OTHER session so the caller can name it, as `Twin` does.
        | TwinHolds of theirs: SessionId

        /// **THE LIVE MARKER BELONGS TO THE WORKER WE NAMED, AND WE ARE NOT THAT WORKER** (#1646).
        ///
        /// The caller resolved `derived` for itself and asked to act as `named`; `named` holds the live lock.
        /// Under a shared harness session this passed both existing legs — the id matched because it was
        /// COPIED off the board, and `twinSession` could not call it a twin because the sessions were equal —
        /// and the capability that authorises deleting that marker was handed over.
        ///
        /// **IT IS NOT `DoesNotHold`, AND THAT DISTINCTION IS THE POINT** (#1646 AC 3). By far the commonest
        /// way to name an id that is not yours is a TYPO, and the remedy for a typo is "check the flag" — so
        /// the two must not share a message. This case fires only when the named id holds the LIVE lock,
        /// which is exactly when the mistake is not a typo and the cost is another worker's lock. A named id
        /// that holds nothing stays `DoesNotHold`, where the caller can say "and that is not your id either"
        /// without accusing anybody of impersonation.
        ///
        /// It carries both ids because a refusal that names neither cannot be acted on: the caller has to see
        /// who it actually is beside who it asked to be.
        ///
        /// Spelled apart from `ClaimOutcome.Impersonates` for `TwinHolds`'s reason: two cases of the same
        /// name in one module shadow each other at every unqualified construction.
        | ImpersonatesHolder of derived: WorkerId * named: WorkerId

    /// Re-read the markers and confirm the live winner is US.
    ///
    /// The other door to a `Held`, for a worker that took its lock in an earlier process — every command
    /// after `claim` is a fresh invocation, so without this the capability could not survive the one thing
    /// it exists to guard.
    ///
    /// It fails CLOSED: an unreadable marker set yields an error, never a `Held`. Manufacturing a
    /// capability from a failed read would be the fail-open this entire type exists to prevent, sitting
    /// inside its own constructor.
    ///
    /// **AND IT MATCHES ON A SESSION PREDICATE, NOT ON THE WORKER ID ALONE (#1031, #839 residual 2 of 4).**
    /// The id match was the last place this invariant was asserted by CONVENTION: `claim` has refused a twin
    /// since #419, and `release`/`heartbeat` scope to the winning comment id — so the reachable path was
    /// already closed — but a deliberately mis-targeted ref could still match a twin's marker here and be
    /// handed the capability that authorises deleting it. #419's whole lesson is that a shared id is the one
    /// thing this protocol cannot separate, so the door to `Held` may not be opened by an id.
    ///
    /// The predicate is `claim`'s, exactly, and it must stay that way — two answers to "is this marker ours?"
    /// that disagree is the defect, not the safeguard. We conclude "twin" ONLY when both sessions are known:
    /// a sessionless marker (a human, a harness exporting none, any pre-#419 marker) is genuinely
    /// indistinguishable from ours, and failing closed on it would lock workers out of items they really
    /// hold; and our own session is a match, never a twin, or no worker could verify its own lock.
    ///
    /// **AND THE SESSION IS NOT A CREDENTIAL EITHER (#1646).** #419 said an id two workers share cannot be a
    /// lock; #1031 answered it with a session predicate. But every subagent of one harness session shares
    /// that session, so for the fan-out this board actually runs, the second factor is one the impersonator
    /// holds too — and `twinSession` returns `None` for equal sessions, which `verifyHeld` read as "ours".
    /// The two previous repairs made the protocol better at telling IDS apart, and neither can help when the
    /// second factor is shared as well. `self` is the third fact, and it is the one `--worker` cannot restate:
    /// under `Derives d`, a live marker for a `worker` that is not `d` is refused as `ImpersonatesHolder`, no
    /// matter what the sessions say. Under `DerivesNothing` the question is unaskable and the older rules decide
    /// alone — the human operator and the session-less harness keep working, which is the #1031 boundary
    /// restated for a fact one step further out.
    ///
    /// **ORDER: IMPERSONATION IS DECIDED BEFORE THE TWIN.** A caller whose own id disagrees with the marker's
    /// is definitively not that marker's worker, so "you named somebody else" is the true diagnosis and
    /// "somebody shares your id" is not: `TwinHolds` would send them to `whoami --mint` for an identity
    /// collision they do not have.
    val verifyHeld:
        transport: IGitHubTransport ->
        leaseMinutes: int ->
        worker: WorkerId ->
        self: SelfIdentity ->
        session: SessionId option ->
        ref: Ref ->
            IoResult<HeldOutcome>

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
    ///
    /// THE SENTINEL IS ACCEPTED, and canonicalised to a single `none` (#863). `Paths: none` is a DECISION
    /// (#496), `widen --paths none` is how a worker declares it, and this function's own empty-token
    /// refusal tells them to — so refusing it as "a token that can never match a file" would be the tool
    /// contradicting its own instruction. `none` MIXED with real paths is a contradiction and is refused.
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
    ///
    /// WHERE THE FENCES ARE IS `Markdown`'s ANSWER, NOT THIS MODULE'S (#972). What `rewrite` WRITES,
    /// `TouchSet.parse` must be able to READ; they are the write and the read of one fact, so they cannot
    /// hold two fence rules between them. They did: this module used `^\s*` and `TouchSet` used `^ {0,3}`,
    /// so `widen` wrote under one rule and `take` scheduled under the other.
    ///
    /// AN UNTERMINATED FENCE IN `body` IS CLOSED, BEFORE ANY APPEND. An unterminated fence runs to the end
    /// of the body, so a declaration appended below one lands INSIDE the code block and declares nothing.
    /// Measured before #972: `widen` returned success and `TouchSet.parse` of the body it wrote returned
    /// `Undeclared` — the item sat `Ready`, apparently declared, and never scheduled. The closer matches the
    /// opener's spelling and length, because a `~~~~` fence is not closed by "```".
    ///
    /// This repair is a WRITE-side normalisation layered on the shared read, and deliberately not a second
    /// reading of it: a reader must leave an unterminated fence alone (the author is looking at a code
    /// block), while a writer that is about to append below one has to make the body well-formed first.
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

    /// A stale marker that has been PROVEN reapable — its lease has lapsed AND we LOOKED for the item's
    /// own `item/<n>-*` PR and found none. This is #581 as a TYPE.
    ///
    /// Abstract, and produced ONLY by `reapable`. There is no constructor from a `Marker` alone, and that
    /// is the whole point: lease expiry is EVIDENCE of abandonment, never PROOF (its false positive is
    /// systematic — work that outlasts its lease), so a marker whose PR is open, or whose liveness we could
    /// not read, cannot be turned into one of these. `reap` accepts nothing else, so "collect a claim only
    /// once you have ruled its work dead" is not an `if` a reaper can forget — it is the only door in.
    ///
    /// It carries the marker's COMMENT ID, because the lock IS that id: reaping by worker STRING would let a
    /// twin's id name the wrong comment, the #550 shape one command over.
    [<Sealed>]
    type Reapable =
        /// The item this stale lock sits on.
        member Ref: Ref

        /// The worker whose lapsed claim this is — named in the reap report, never the reaper's.
        member Worker: WorkerId

        /// The comment id of the stale marker. The lock IS this number.
        member MarkerId: int64

        /// The board column this claim overwrote (#481), so a reap can put it back rather than guess — the
        /// same restore `release` performs, now for a lock its own holder abandoned.
        member PreviousStatus: BoardStatus option

    /// Why a stale marker may NOT be reaped. NOT a `bool`: each case is a different thing to tell the
    /// operator, and collapsing "the work is alive" into "could not reap" is exactly how #581's reaper
    /// destroyed the claims of workers who were visibly still working.
    type ReapRefusal =
        /// #581: the lease lapsed, but an `item/<n>-*` PR is OPEN — the worktree protocol's own artifact,
        /// server-side proof the work is alive. Carries the PR number so the refusal is checkable.
        | WorkAlive of pr: int

        /// #1055: the lease lapsed, no PR is open, but a pushed `item/<n>-*` BRANCH exists — proof of life
        /// during §3, before §5 opens the PR. DISTINCT from `Undetermined`: we could tell, and the answer is
        /// "a branch is pushed", not "we could not read". No PR number to carry — there is no PR to land.
        | WorkAliveBranch

        /// WE COULD NOT TELL. The liveness read failed, and a lock we cannot rule dead we may not break —
        /// a transient 5xx must not become a reaped claim (the fail-closed direction on a lock).
        | Undetermined of reason: string

    /// Prove a STALE marker is reapable — #581's judgement, in ONE place.
    ///
    /// `Ok` only on `LeaseExpiredNoPr`: the lease lapsed and we LOOKED for the item's PR and found none.
    /// Every other `Liveness` is a `ReapRefusal` the caller must report instead of collecting. The caller
    /// owns finding the lease lapsed first (it must, or the `LeaseExpired*` cases are a lie) — this function
    /// owns the proof-of-life gate on top of it.
    val reapable: ref: Ref -> marker: Reads.Marker -> liveness: Liveness -> Result<Reapable, ReapRefusal>

    /// What `reap` did once it RE-VERIFIED the marker against a fresh read. `reapable` was minted from a
    /// SNAPSHOT — the scan — and a holder may have heartbeated between the scan and this delete. Deleting a
    /// lock because it USED TO BE stale is the one way `reap` can itself cause the double-hold it exists to
    /// clean up, so the marker's freshness is re-checked immediately before the delete, and the three
    /// outcomes are kept apart because each is a different line the operator must read.
    type ReapResult =
        /// The marker was still stale on the fresh read, and it was DELETED — the lock self-heals.
        | Reaped

        /// The holder HEARTBEATED between the scan and now: the marker is live again, so it was LEFT alone.
        /// Carries its now-current age so the report can say how recently it was renewed.
        | RenewedSinceScan of ageSeconds: int

        /// The marker was already gone by the time we re-read — a peer collected it first. Nothing to do.
        | AlreadyGone

    /// COLLECT a reaped claim — RE-VERIFY the marker is still stale, then delete it so the lease self-heals.
    ///
    /// Takes the `Reapable`, so #581 is unexpressible here: a live or unreadable claim has no way to reach
    /// this call. But `Reapable` was proven against the SCAN, and the scan is a snapshot — so this re-reads
    /// the markers and confirms the one it is about to delete is STILL stale (`RenewedSinceScan` if the
    /// holder heartbeated since; `AlreadyGone` if a peer collected it first), because reaping a lock that
    /// went live again between the scan and the delete is exactly how `reap` would cause the double-hold it
    /// exists to clean up. It deletes by COMMENT ID (a 404 is success — two reapers collecting the same
    /// marker must not turn the loser's benign miss into an error), and it does NOT touch the board: what the
    /// freed column becomes is the caller's decision (`PreviousStatus`), made after the lock is already gone,
    /// so a board it cannot read or write leaves the column alone rather than stranding a lock behind.
    val reap: transport: IGitHubTransport -> leaseMinutes: int -> reapable: Reapable -> IoResult<ReapResult>

    /// Post a message to another worker (`fsgg:msg`).
    ///
    /// Does NOT take a `Held` — deliberately. A worker who has just LOST a race, or who is warning the
    /// holder about an overlap, must still be able to speak. Requiring the lock to send a message would
    /// silence exactly the worker with something urgent to say.
    val say: transport: IGitHubTransport -> from: WorkerId -> toWorker: WorkerId -> ref: Ref -> text: string -> IoResult<unit>

    /// Record a follow-up disposition on the owed issue itself. Unlike a worker message this is durable
    /// evidence for a driver that later audits an abandoned queue.
    val followupDisposition: transport: IGitHubTransport -> ref: Ref -> worker: WorkerId -> text: string -> IoResult<unit>

    /// Posts the immutable receipt that says a `done` precondition check succeeded.  The status column is
    /// deliberately not used as that evidence: reconciliation may repair or defer that projection later.
    val doneReceipt: transport: IGitHubTransport -> ref: Ref -> receipt: string -> IoResult<unit>

    /// Appends a verified lifecycle ordering receipt to an item.
    val lifecycleWatermark: transport: IGitHubTransport -> ref: Ref -> marker: string -> IoResult<unit>

    /// Attach a child issue to a parent (`sub_issues`).
    ///
    /// The child's REST INTEGER ID, never its number — two repos can each have an issue #7, and posting a
    /// number where an id belongs attaches the wrong issue silently. It is sent as a JSON NUMBER, not a
    /// string: the string form collects a 422, which is what `gh api -F` (rather than `-f`) was for.
    val child: transport: IGitHubTransport -> parent: Ref -> childId: int64 -> IoResult<unit>

    /// Append a `Rooms: <roomRef>` line to a body, fence-safely (ADR-0051). PURE — exposed so `room open`'s
    /// body write is testable with no network. APPENDS, never replaces: a room membership is additive
    /// (`Rooms.parse` unions lines), and the fence is closed before the append (#972) so the line is not
    /// swallowed into a code block where `Rooms.parse` would never see it.
    val appendRoomLine: body: string -> roomRef: string -> string

    /// Write a `Rooms: <roomRef>` back-reference onto an item's body (ADR-0051). Does NOT take a `Held`:
    /// `room open` writes onto the items of a contended cluster it need not itself hold, exactly like `say`
    /// and `child`. The caller passes the current body (already read), so the append is pure.
    val writeRoomRef: transport: IGitHubTransport -> ref: Ref -> currentBody: string -> roomRef: string -> IoResult<unit>

    /// Ensure one room back-reference from live state. The write is skipped when already present and is
    /// re-read after both success and failure, making a response-lost PATCH retry-idempotent.
    val ensureRoomRef: transport: IGitHubTransport -> ref: Ref -> roomRef: string -> IoResult<unit>

    /// Result of ensuring a single marker-keyed ADR-0051 room.
    type RoomWrite =
        | RoomCreated of Ref
        | RoomAlreadyPresent of Ref

    /// Create or recover exactly one open room whose body carries `marker`. Every open-issue body must be
    /// readable before absence is believed. Success and failure are both reconciled through a fresh
    /// complete read; duplicate marker matches fail closed.
    val ensureRoom:
        transport: IGitHubTransport ->
        owner: string ->
        repo: string ->
        marker: string ->
        title: string ->
        body: string ->
            IoResult<RoomWrite>

    /// Create the room ISSUE (ADR-0051), returning its `Ref`. A net-new write — no other verb POSTs an
    /// issue. The room is created OFF the board (nothing calls `add`): coordination scaffolding, not
    /// deliverable work.
    val createRoom: transport: IGitHubTransport -> owner: string -> repo: string -> title: string -> body: string -> IoResult<Ref>

    /// Close the room ISSUE (ADR-0051 §4). A room's lifecycle is derived — it dies when every currently
    /// referencing item is done — so this only PATCHes the issue closed; a room carries no lock or lease.
    val closeRoom: transport: IGitHubTransport -> ref: Ref -> IoResult<unit>
