namespace FS.GG.Coord

/// Deferred maintenance beside the single lifecycle reducer.
module Chore =

    open Types

    type ChoreSize =
        | Quick
        | Involved

        member Label: string

    /// The only safe destinations after premature issue closure.
    type CompletionCorrectionStatus =
        | CorrectionInReview
        | CorrectionBlocked

    val completionCorrectionStatus: CompletionCorrectionStatus -> BoardStatus

    /// Non-lifecycle maintenance plus the reducer's one Status repair carrier.
    type ChoreKind =
        | StaleClaim of holder: WorkerId
        | LifecycleProjectionLag of destination: BoardStatus
        /// Receipt-free issue closure must be restored to a safe nonterminal projection.
        | PrematureCompletion of destination: CompletionCorrectionStatus
        /// A typed completion receipt exists but issue/Projects projections are incomplete.
        | CompletionProjection
        | ClassProjectionLag of declared: ItemClass
        /// The board's `Kind` column disagrees with the item's own `Kind:` line (.github#2712).
        ///
        /// `ClassProjectionLag`'s shape and authority direction exactly (ADR-0066): the body declares, the
        /// column is written from it, and the chore exists in the gap. A row declaring NO `Kind:` line
        /// derives no chore — an absent declaration is not a disagreement, and sweeping `work` onto every
        /// unclassified row would write a fact nobody asserted.
        | KindProjectionLag of declared: ItemKind

        member RuleId: string
        member Write: (string * string) option

    [<Sealed>]
    type Chore =
        member Subject: Ref
        member Kind: ChoreKind
        member Size: ChoreSize
        member Id: string
        member Statement: string

    type Boundary =
        | AtNext
        | AfterDone

        member Label: string

    [<Sealed>]
    type SafePoint =
        member Boundary: Boundary
        member Worker: WorkerId

    type Board =
        | Whole of Item list
        | Filtered of Item list

    val safePoint:
        boundary: Boundary ->
        worker: WorkerId ->
        observed: Board ->
        subject: Item list ->
            SafePoint option

    /// Derive stale-claim and Class maintenance. It never independently derives Status.
    val derive: items: Item list -> Chore list

    /// Carry the one verified intent-reducer destination into the reconcile write path.
    val lifecycleProjection: item: Item -> destination: BoardStatus -> Chore option

    /// Carry the fail-closed completion correction chosen from the same observed item facts.
    val prematureCompletion: item: Item -> destination: BoardStatus -> Chore option

    /// Carry repair of issue closure and Status=Done from existing typed completion authority.
    val completionProjection: item: Item -> Chore option

    val offer: at: SafePoint -> Chore option
    /// Rank reducer-produced lifecycle repairs beside maintenance without deriving a second Status authority.
    val offerIncluding: at: SafePoint -> lifecycle: Chore list -> Chore option
    val isRetired: chore: Chore -> items: Item list -> bool
