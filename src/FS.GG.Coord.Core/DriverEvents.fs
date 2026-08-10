namespace FS.GG.Coord

module DriverEvents =
    open Types

    type MaterialState =
        | Ready
        | Claimed of worker: string
        | ReviewHandoff of critic: string option
        | ReviewRepair of round: int
        | CiLandable
        | MergedAwaitingObligations of pr: int
        | Released
        | HumanBlocked of reason: string
        | Done
        | Unreadable of reason: string

    type ItemFacts =
        { Ref: string
          ReadOk: bool
          UnreadableReason: string option
          BoardStatus: BoardStatus option
          IssueState: IssueState option
          ClaimWorker: string option
          HumanBlock: HumanBlock option
          Pr: int option
          Review: Driver.ReviewChain option
          Merged: bool
          ObligationsDeclared: bool
          Obligations: Delivery.Obligation list
          Evidence: string
          ObservedAt: int64
          SourceSha: string }

    type Classified =
        { Ref: string
          State: MaterialState
          Reason: string
          Evidence: string
          ObservedAt: int64
          SourceSha: string }

    type Cursor = Map<string, MaterialState>

    type TransitionEvent =
        { Ref: string
          Previous: MaterialState option
          New: MaterialState
          Reason: string
          Evidence: string
          ObservedAt: int64
          SourceSha: string }

    type Projection =
        { Transitions: TransitionEvent list
          Active: Classified list
          Cursor: Cursor
          RenderedAt: int64 }

    /// The pure state+reason derivation. A failed read wins over every other fact — an item this
    /// process could not read is never confidently Ready/Blocked/Done from stale or partial data.
    let private deriveState (facts: ItemFacts) : MaterialState * string =
        if not facts.ReadOk then
            Unreadable(facts.UnreadableReason |> Option.defaultValue "live read failed"), "live read failed or was incomplete"
        else
            match facts.HumanBlock with
            | Some AwaitingHumanDecision -> HumanBlocked "Blocked on: human/decision", "human-decision sentinel present"
            | Some AwaitingHumanAction -> HumanBlocked "Blocked on: human/action", "human-action sentinel present"
            | None ->

            match facts.BoardStatus with
            | Some Types.Blocked -> HumanBlocked "board status Blocked", "board status is Blocked"
            | _ ->

            // Merged/closed outranks every claim/review fact: a claim marker left behind by a worker who
            // never released it must not keep a merged item reading as "still claimed" forever.
            if facts.Merged && facts.IssueState = Some Closed then
                let undischarged =
                    facts.ObligationsDeclared
                    && facts.Obligations |> List.exists (fun o -> not o.Verified)

                if undischarged then
                    MergedAwaitingObligations(facts.Pr |> Option.defaultValue 0), "merged; one or more obligations unverified"
                else
                    Released, "merged; every declared obligation is verified (or none was declared)"
            elif facts.BoardStatus = Some Types.Done && facts.IssueState = Some Closed then
                Done, "board status Done; issue closed"
            else
                match facts.ClaimWorker with
                | Some worker ->
                    match facts.Review with
                    | Some chain when chain.MarkerValid && chain.RepairPhase ->
                        ReviewRepair(List.length chain.Rounds), "review marker valid; in repair phase"
                    | Some chain when chain.MarkerValid && chain.ChecksGreen && chain.HostAccepted ->
                        CiLandable, "review marker valid; checks green; host-accepted"
                    | Some chain when chain.MarkerValid ->
                        ReviewHandoff chain.CriticIdentity, "review marker valid; awaiting critic or host action"
                    | Some _ -> Claimed worker, "a review marker is present but invalid; treated as claimed pending a valid handoff"
                    | None -> Claimed worker, "claim marker live; no review evidence yet"
                | None ->
                    match facts.BoardStatus with
                    | Some Types.Done -> Done, "board status Done"
                    | _ -> Ready, "no live claim; unclaimed and schedulable"

    let classify (facts: ItemFacts) : Classified =
        let state, reason = deriveState facts

        { Ref = facts.Ref
          State = state
          Reason = reason
          Evidence = facts.Evidence
          ObservedAt = facts.ObservedAt
          SourceSha = facts.SourceSha }

    let isActive (state: MaterialState) : bool =
        match state with
        | Claimed _
        | ReviewHandoff _
        | ReviewRepair _
        | CiLandable
        | MergedAwaitingObligations _ -> true
        | Ready
        | Released
        | HumanBlocked _
        | Done
        | Unreadable _ -> false

    /// Idempotency is suppression of REPEATED news: a stable `Claimed`/`Ready`/etc. state that has not
    /// changed since the cursor is not worth re-announcing. `Unreadable` is the one state where that
    /// reasoning inverts (fsgg:independent-review:v1 round 1, finding 1, .github#2135 repair round 1):
    /// a PERSISTENT failure is itself the news, every read, for as long as it persists. Suppressing it
    /// after cycle one makes a rotting item indistinguishable from a healthy one — a failed read must
    /// never become an empty or successful result (issue acceptance #7), and "quiet because nothing
    /// changed" is exactly that outcome for an item stuck broken.
    let private alwaysReports (state: MaterialState) : bool =
        match state with
        | Unreadable _ -> true
        | Ready
        | Claimed _
        | ReviewHandoff _
        | ReviewRepair _
        | CiLandable
        | MergedAwaitingObligations _
        | Released
        | HumanBlocked _
        | Done -> false

    let deriveEvents (cursor: Cursor) (classified: Classified list) : TransitionEvent list * Cursor =
        let events =
            classified
            |> List.choose (fun c ->
                let previous = Map.tryFind c.Ref cursor

                if previous = Some c.State && not (alwaysReports c.State) then
                    None
                else
                    Some
                        { Ref = c.Ref
                          Previous = previous
                          New = c.State
                          Reason = c.Reason
                          Evidence = c.Evidence
                          ObservedAt = c.ObservedAt
                          SourceSha = c.SourceSha })

        let newCursor =
            classified |> List.fold (fun acc c -> Map.add c.Ref c.State acc) cursor

        events, newCursor

    let project (cursor: Cursor) (facts: ItemFacts list) (observedAt: int64) : Projection =
        let classified = facts |> List.map classify
        let events, newCursor = deriveEvents cursor classified

        { Transitions = events
          Active = classified |> List.filter (fun c -> isActive c.State)
          Cursor = newCursor
          RenderedAt = observedAt }

    let encodeState (state: MaterialState) : string =
        match state with
        | Ready -> "ready"
        | Claimed worker -> $"claimed:%s{worker}"
        | ReviewHandoff critic -> $"""review-handoff:%s{critic |> Option.defaultValue ""}"""
        | ReviewRepair round -> $"review-repair:%d{round}"
        | CiLandable -> "ci-landable"
        | MergedAwaitingObligations pr -> $"merged-awaiting-obligations:%d{pr}"
        | Released -> "released"
        | HumanBlocked reason -> $"blocked:%s{reason}"
        | Done -> "done"
        | Unreadable reason -> $"unreadable:%s{reason}"

    let decodeState (encoded: string) : MaterialState option =
        match encoded with
        | "ready" -> Some Ready
        | "ci-landable" -> Some CiLandable
        | "released" -> Some Released
        | "done" -> Some Done
        | value when value.StartsWith "claimed:" -> Some(Claimed(value.Substring 8))
        | value when value.StartsWith "review-handoff:" ->
            match value.Substring 15 with
            | "" -> Some(ReviewHandoff None)
            | critic -> Some(ReviewHandoff(Some critic))
        | value when value.StartsWith "review-repair:" ->
            match System.Int32.TryParse(value.Substring 14) with
            | true, round -> Some(ReviewRepair round)
            | false, _ -> None
        | value when value.StartsWith "merged-awaiting-obligations:" ->
            match System.Int32.TryParse(value.Substring 28) with
            | true, pr -> Some(MergedAwaitingObligations pr)
            | false, _ -> None
        | value when value.StartsWith "blocked:" -> Some(HumanBlocked(value.Substring 8))
        | value when value.StartsWith "unreadable:" -> Some(Unreadable(value.Substring 11))
        | _ -> None

    let renderText (projection: Projection) : string =
        let transitionLine =
            if List.isEmpty projection.Transitions then
                "no material transitions"
            else
                let rendered =
                    projection.Transitions
                    |> List.map (fun e ->
                        let previous =
                            e.Previous
                            |> Option.map encodeState
                            |> Option.defaultValue "unobserved"

                        $"%s{e.Ref}: %s{previous} -> %s{encodeState e.New} (%s{e.Reason})")
                    |> String.concat "; "

                $"material transitions (%d{List.length projection.Transitions}): %s{rendered}"

        let activeLine =
            if List.isEmpty projection.Active then
                "no active items"
            else
                let rendered =
                    projection.Active
                    |> List.map (fun c -> $"%s{c.Ref} [%s{encodeState c.State}]")
                    |> String.concat ", "

                $"active items (%d{List.length projection.Active}): %s{rendered}"

        transitionLine + "\n" + activeLine
