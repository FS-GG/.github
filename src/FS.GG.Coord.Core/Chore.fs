namespace FS.GG.Coord

module Chore =

    open Types

    type ChoreSize =
        | Quick
        | Involved

        member this.Label =
            match this with
            | Quick -> "quick"
            | Involved -> "involved"

    type ChoreKind =
        | StaleClaim of holder: WorkerId
        | LifecycleProjectionLag of destination: BoardStatus
        | ClassProjectionLag of declared: ItemClass
        | KindProjectionLag of declared: ItemKind

        member this.RuleId =
            match this with
            | StaleClaim _ -> "STALE-CLAIM"
            | LifecycleProjectionLag _ -> "LIFECYCLE-PROJECTION-LAG"
            | ClassProjectionLag _ -> "CLASS-PROJECTION-LAG"
            | KindProjectionLag _ -> "KIND-PROJECTION-LAG"

        member this.Write: (string * string) option =
            match this with
            | StaleClaim _ -> None
            | LifecycleProjectionLag destination -> Some("Status", statusWireName destination)
            | ClassProjectionLag declared -> Some("Class", itemClassWireName declared)
            // IN CORE, beside its sibling, never in `Client.fs` — the field mapping lives here precisely
            // so the write target and the vocabulary are one decision (`Client.fs`'s own note at the
            // reconcile write loop).
            | KindProjectionLag declared -> Some("Kind", itemKindWireName declared)

    [<Sealed>]
    type Chore internal (subject: Ref, kind: ChoreKind, size: ChoreSize) =
        member _.Subject = subject
        member _.Kind = kind
        member _.Size = size

        member _.Id = $"%s{kind.RuleId}:%s{subject.Owner}/%s{subject.Repo}#%d{subject.Number}"

        member _.Statement =
            match kind with
            | StaleClaim holder ->
                $"%s{subject.Short}: %s{holder.Value}'s lease has lapsed and the item has no open `item/` PR — collect the marker and restore the column it overwrote."
            | LifecycleProjectionLag destination ->
                $"%s{subject.Short}: fresh lifecycle facts project Status=%s{statusWireName destination}; repair the stale board projection."
            | ClassProjectionLag declared ->
                $"%s{subject.Short}: the item's own text declares `Class: %s{itemClassWireName declared}` but the board's Class column does not say so — set Class to %s{itemClassWireName declared}."
            | KindProjectionLag declared ->
                $"%s{subject.Short}: the item's own text declares `Kind: %s{itemKindWireName declared}` but the board's Kind column does not say so — set Kind to %s{itemKindWireName declared}."

    type Boundary =
        | AtNext
        | AfterDone

        member this.Label =
            match this with
            | AtNext -> "next"
            | AfterDone -> "done"

    type Board =
        | Whole of Item list
        | Filtered of Item list

    [<Sealed>]
    type SafePoint internal (boundary: Boundary, worker: WorkerId, items: Item list) =
        member _.Boundary = boundary
        member _.Worker = worker
        member internal _.Items = items

    let private holdsLock (worker: WorkerId) (item: Item) =
        match item.Claim with
        | Some(claim, _) -> claim.Worker = worker
        | None -> false

    let safePoint boundary worker observed subject =
        match observed with
        | Filtered _ -> None
        | Whole items when items |> List.exists (holdsLock worker) -> None
        | Whole _ -> Some(SafePoint(boundary, worker, subject))

    // Derive non-lifecycle maintenance only. Status has exactly one authority:
    // `LifecycleProjection`, exposed through `lifecycleProjection` below.
    let private choresFor (item: Item) =
        match item.Claim with
        | Some(claim, LeaseExpiredNoPr) ->
            [ Chore(item.Ref, StaleClaim claim.Worker, Involved) ]
        | Some _ -> []
        | None ->
            // BOTH projections, from one pass. `Kind` is derived exactly as `Class` is — declared but not
            // rendered — and for the same reason: two fields, two facts, and the chore lives in the gap.
            // A row declaring NO `Kind:` line derives NO chore, so the board is not swept with `work`
            // writes for every row that simply never said anything (an absent declaration is not a
            // disagreement).
            let classLag =
                match item.Class, item.BoardClass with
                | Some declared, board when board <> Some declared ->
                    [ Chore(item.Ref, ClassProjectionLag declared, Quick) ]
                | _ -> []

            let kindLag =
                match item.Kind, item.BoardKind with
                | Some declared, board when board <> Some declared ->
                    [ Chore(item.Ref, KindProjectionLag declared, Quick) ]
                | _ -> []

            classLag @ kindLag

    let derive items = items |> List.collect choresFor

    let lifecycleProjection (item: Item) destination =
        if item.Status = destination || destination = NoStatus then None
        else Some(Chore(item.Ref, LifecycleProjectionLag destination, Quick))

    let private rank (chore: Chore) =
        match chore.Kind with
        | StaleClaim _ -> 0
        | LifecycleProjectionLag _ -> 1
        | ClassProjectionLag _ -> 2
        | KindProjectionLag _ -> 3

    let offerIncluding (at: SafePoint) (lifecycle: Chore list) =
        derive at.Items @ lifecycle
        |> List.sortBy (fun chore -> rank chore, chore.Id)
        |> List.tryHead

    let offer (at: SafePoint) = offerIncluding at []

    let isRetired (chore: Chore) items =
        derive items |> List.forall (fun current -> current.Id <> chore.Id)
