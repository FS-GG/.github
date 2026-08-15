namespace FS.GG.Coord

/// Deferred maintenance beside the single lifecycle reducer.
module Chore =

    open Types

    type ChoreSize =
        | Quick
        | Involved

        member Label: string

    /// Non-lifecycle maintenance plus the reducer's one Status repair carrier.
    type ChoreKind =
        | StaleClaim of holder: WorkerId
        | LifecycleProjectionLag of destination: BoardStatus
        | ClassProjectionLag of declared: ItemClass

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

    val offer: at: SafePoint -> Chore option
    /// Rank reducer-produced lifecycle repairs beside maintenance without deriving a second Status authority.
    val offerIncluding: at: SafePoint -> lifecycle: Chore list -> Chore option
    val isRetired: chore: Chore -> items: Item list -> bool
