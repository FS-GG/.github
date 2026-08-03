namespace FS.GG.Coord.Cli

/// JSON snapshot boundary for the pure claim-to-done lifecycle decision.
module DeliveryApplication =
    /// Whether a consumed delivery receipt still authorizes the live adapter to issue its merge request.
    type LandingAuthorization =
        | MergeAuthorized
        | MergeRefused of reason: string

    /// Render one lifecycle verdict from facts observed by either the snapshot or live adapter.
    val render: Options.Options -> FS.GG.Coord.Delivery.Snapshot -> int

    /// Parse only exact, head-bound v1 delivery obligation declarations and receipts from PR comments.
    val obligationsFromComments:
        headSha: string ->
        comments: FS.GG.Coord.Driver.ReviewComment list ->
            Result<FS.GG.Coord.Delivery.Obligation list, string>

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
        merge: (unit -> 'result) ->
            Result<'result, string>

    val run: Options.Options -> int
