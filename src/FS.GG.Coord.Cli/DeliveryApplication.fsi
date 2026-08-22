namespace FS.GG.Coord.Cli

/// JSON snapshot boundary for the pure claim-to-done lifecycle decision.
module DeliveryApplication =
    /// Whether a consumed delivery receipt still authorizes the live adapter to issue its merge request.
    ///
    /// This is a two-case answer on purpose: there is no "probably", and no case that carries a
    /// merge alongside a caveat. `authorizeGuardedLanding` re-reads the winning claim generation at
    /// the moment of asking, so an authorization is a statement about NOW and cannot be cached
    /// across a head or claim change.
    type LandingAuthorization =
        /// The receipt, the freshness token, the action key and the currently winning claim
        /// generation all still agree; the adapter may issue its merge request.
        | MergeAuthorized
        /// Refused, with the specific disagreement named. Every path that is not a full agreement
        /// lands here — a stale freshness token, a different action, an absent or changed claim
        /// generation, or any action other than `Delivery.GuardedLand` — so a caller can report why
        /// rather than retrying blindly.
        | MergeRefused of reason: string

    type LandingReceipt<'result> =
        { HeadSha: string
          BaseSha: string
          Result: 'result }

    /// Render one lifecycle verdict from facts observed by either the snapshot or live adapter. Actions
    /// carrying a problem preserve it in JSON and text rather than reducing it to the action token.
    val render: Options.Options -> FS.GG.Coord.Delivery.Snapshot -> int

    /// Parse only exact, head-bound v1 delivery obligation declarations and receipts from PR comments.
    val obligationsFromComments:
        headSha: string ->
        comments: FS.GG.Coord.Driver.ReviewComment list ->
            Result<FS.GG.Coord.Delivery.Obligation list, string>

    /// One `fsgg:merge-election` marker as it sits on the item (.github#2395, design slice 3 of
    /// .github#1858).
    ///
    /// `Id` is the comment id, and it is the whole of the authorization's `grant=`: GitHub assigns
    /// it, so no caller chooses it, which is what separates a grounded authorization from a
    /// decorative one. `Fields` is raw and unvalidated on purpose — the fence tolerates fields it
    /// does not require, and a producer that could not even PARSE an election written by another
    /// engine version would post a duplicate rather than reuse it.
    type Election = { Id: int64; Fields: Map<string, string> }

    /// The exact `fsgg:merge-election` text `delivery` appends to the item — the six fields
    /// `scripts/check-claim-fence.py` requires, plus `pr=`, this producer's idempotence
    /// discriminator. See the implementation's own comment for why `pr=` is load-bearing in BOTH
    /// directions: without it a repeated call denies its own pull request, and with a laxer rule two
    /// executors under one generation would both pass check 4.
    val electionMarker:
        opkey: string -> item: string -> gen: string -> receiver: string -> pr: int -> string

    /// Every election on the item, one per comment whose body OPENS with the marker at byte 0 —
    /// the fence's own anchoring, which does not trim, so a comment that merely quotes an election
    /// is not one.
    val electionsFromComments: comments: FS.GG.Coord.Driver.ReviewComment list -> Election list

    /// The elections this delivery target already owns: same operation key AND same pull request.
    /// Deliberately narrower than the fence's candidate set, which is keyed on the opkey alone.
    val electionsOwnedBy: opkey: string -> pr: int -> elections: Election list -> Election list

    /// Consume the inspected receipt and require the current winning claim generation before a merge.
    val authorizeGuardedLanding:
        freshnessToken: string ->
        actionKey: string ->
        facts: FS.GG.Coord.Delivery.Snapshot ->
        currentClaimGeneration: string option ->
            LandingAuthorization

    /// Invoke the supplied merge adapter only after a receipt and re-read claim generation authorize it.
    val guardedLanding:
        freshnessToken: string ->
        actionKey: string ->
        facts: FS.GG.Coord.Delivery.Snapshot ->
        currentClaimGeneration: string option ->
        currentHead: string option ->
        currentBase: string option ->
        merge: (unit -> 'result) ->
            Result<LandingReceipt<'result>, string>

    /// Run `delivery --snapshot FILE` — the PURE, IO-free form, which reads a supplied lifecycle
    /// snapshot and prints one freshness-bound action.
    ///
    /// THIS IS NOT THE FORM THAT WRITES. The live `delivery <ref> --pr N` path is `Client`'s, and it
    /// is the only one that PATCHes a pull request's `fsgg:pr-authorization` marker; the distinction
    /// is load-bearing, because a worker who runs this form believing it authorized a merge has
    /// performed no write at all (.github#2488). Nothing reachable from here touches the board or
    /// the network.
    val run: Options.Options -> int
