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
          Unreadable: Classified list
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

    let private isTerminal (state: MaterialState) : bool =
        match state with
        | Done
        | Released -> true
        | _ -> false

    /// A terminal row regressing to `Ready` is never a legitimate transition (.github#2375 symptom 1,
    /// issue acceptance #2): once this process has itself classified a ref `Done` (board status Done,
    /// issue closed — the ONLY path `deriveState` reaches `Done` through) or `Released` (merged, closed,
    /// every declared obligation verified), those are end states with no forward edge back to
    /// unclaimed-and-schedulable in a legitimate lifecycle. A fresh read that disagrees is evidence of a
    /// stale or partial read racing the board's own eventual consistency — reported at 09:03:29Z, 8
    /// minutes after the merge, 11 seconds before a direct `gh issue view` confirmed CLOSED/Done — not
    /// proof the item became schedulable again. Overriding to `Unreadable` keeps the regression OUT of
    /// both `isActive` and "ready" while still surfacing, because `Unreadable` always reports
    /// (`alwaysReports` below): a human sees the disagreement instead of a driver silently re-offering
    /// finished work as startable. Scoped to the one dangerous regression the issue names — a terminal
    /// row disagreeing about anything OTHER than "ready/schedulable" is not this guard's problem.
    let private guardTerminalRegression (cursor: Cursor) (c: Classified) : Classified =
        match Map.tryFind c.Ref cursor, c.State with
        | Some previous, Ready when isTerminal previous ->
            { c with
                State = Unreadable $"cursor previously observed this item %A{previous}; a fresh read reported Ready, which regresses a terminal row and is refused rather than trusted"
                Reason = $"terminal regression refused: previous read was %A{previous}, this read says Ready" }
        | _ -> c

    /// A ref this process previously classified ACTIVE that is simply ABSENT from the current facts
    /// batch (.github#2375 symptom 2, issue acceptance #3) is a missing or partial read for THAT ref,
    /// not evidence it went quiet: nothing in a legitimate lifecycle removes an active item from a full
    /// board scan without first classifying it to some terminal or blocked state, and the caller's own
    /// contract is a COMPLETE facts batch every read. Synthesizing an `Unreadable` entry for it keeps
    /// the ref out of `Active` — matching what a genuinely failed read on that item would render — while
    /// still emitting a transition (`alwaysReports`), so the rendered output is never the sterile "no
    /// material transitions / no active items" pair with zero signal that three live claims went
    /// unobserved. `cursor` is authoritative for "was this active", never `facts`, precisely because
    /// this case is defined by the ref's ABSENCE from `facts`.
    /// The cursor's last-known state, worded so it CANNOT be read as a current observation (.github#2525
    /// acceptance #5).
    ///
    /// The old spelling interpolated the union with `%A`, which renders `Claimed "curlew-307b"` — a bare
    /// present-tense claim — into a sentence about an item nobody could read this pass. In the measured
    /// incident that string named a worker who had already released cleanly, while a different worker
    /// actually held the row. The information is still worth reporting; asserting it as CURRENT is the
    /// defect. Two things make the stale value stick and neither is safe to leave implicit: the sticky
    /// cursor fold re-pins the original state for any ref that stays absent, and `alwaysReports` re-emits
    /// `Unreadable` on every read — so a name that goes stale here is repeated forever and can never be
    /// superseded, because the only thing that could supersede it is a fresh classification of a ref that
    /// by definition is not in the batch.
    let private lastKnownPhrase (state: MaterialState) : string =
        match state with
        | Claimed worker -> $"last known to be held by %s{worker}"
        | ReviewHandoff(Some critic) -> $"last known to be in review with critic %s{critic}"
        | ReviewHandoff None -> "last known to be at review handoff"
        | ReviewRepair round -> $"last known to be in review repair round %d{round}"
        | CiLandable -> "last known to be CI-landable"
        | MergedAwaitingObligations pr -> $"last known to be merged awaiting obligations on PR #%d{pr}"
        | Ready -> "last known to be Ready"
        | Released -> "last known to be Released"
        | HumanBlocked reason -> $"last known to be human-blocked (%s{reason})"
        | Done -> "last known to be Done"
        | Unreadable reason -> $"last known to be unreadable (%s{reason})"

    let private missingActiveRefs (cursor: Cursor) (classified: Classified list) (observedAt: int64) : Classified list =
        let seenRefs = classified |> List.map (fun c -> c.Ref) |> Set.ofList
        let fallbackSha =
            classified |> List.tryHead |> Option.map (fun c -> c.SourceSha) |> Option.defaultValue ""

        cursor
        |> Map.toList
        |> List.filter (fun (ref, state) -> isActive state && not (Set.contains ref seenRefs))
        |> List.map (fun (ref, state) ->
            { Ref = ref
              State =
                Unreadable
                    $"this item is ABSENT from the current facts batch and its state this pass is UNKNOWN; it was %s{lastKnownPhrase state} as of the PREVIOUS read, which is a superseded observation and NOT a statement about who holds it now"
              Reason = "missing from this read: previously active, absent from the current facts batch"
              Evidence = "cursor-only; absent from current read"
              ObservedAt = observedAt
              SourceSha = fallbackSha })

    /// Idempotency is suppression of REPEATED news: a stable `Claimed`/`Ready`/etc. state that has not
    /// changed since the cursor is not worth re-announcing. `Unreadable` is the one state where that
    /// reasoning inverts (independent review round 1, finding 1, .github#2135 repair round 1):
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
        // Guard order matters: regressions are checked per-item over what THIS read produced, then the
        // cursor is checked for active refs this read produced NOTHING for at all. Both guards read the
        // cursor as their SOURCE OF TRUTH for "what did this ref last legitimately settle to".
        let rawClassified = facts |> List.map classify
        let regressionGuarded = rawClassified |> List.map (guardTerminalRegression cursor)
        let missing = missingActiveRefs cursor regressionGuarded observedAt
        let reported = regressionGuarded @ missing

        // Repair round 2 (independent review, .github#2375): a naive fold would persist EVERY
        // reported state into the cursor, including the guards' own remedial `Unreadable` — which is
        // neither `isTerminal` nor `isActive`. That erases the very fact ("Done", "Claimed w") each
        // guard re-reads the cursor for, so a SECOND consecutive stale/missing read (the realistic
        // production condition — the issue's own repro is two consecutive reads, 8 minutes apart from
        // the merge) finds a cursor the first guard already scrubbed and no longer fires: the terminal
        // regression is accepted on cycle 2, and the missing-active ref falls silent forever instead of
        // once. The cursor slot for every ref either guard touched therefore STICKS at its cursor-
        // supplied value across this read — untouched by what got reported — so the next read's guard
        // re-checks the SAME original fact, not its own prior override, for as long as the disagreement
        // persists.
        let overriddenRefs =
            List.zip rawClassified regressionGuarded
            |> List.choose (fun (raw, guarded) -> if raw.State <> guarded.State then Some guarded.Ref else None)
            |> Set.ofList

        let missingRefs = missing |> List.map (fun c -> c.Ref) |> Set.ofList
        let stickyRefs = Set.union overriddenRefs missingRefs

        let events, foldedCursor = deriveEvents cursor reported

        let newCursor =
            stickyRefs
            |> Set.fold
                (fun acc ref ->
                    match Map.tryFind ref cursor with
                    | Some original -> Map.add ref original acc
                    | None -> acc)
                foldedCursor

        { Transitions = events
          Active = reported |> List.filter (fun c -> isActive c.State)
          // THE COMPLETENESS OF THE ACTIVE SET, CARRIED (.github#2525). `isActive Unreadable` is false —
          // correctly, an item nobody could read is not running — so before this the `Active` filter simply
          // DISCARDED every unreadable row and the renderer had nothing left to distinguish "I measured an
          // empty active set" from "I could not measure the active set". Those are different facts and the
          // stopping rule consumes both, so the projection now carries them both.
          Unreadable =
            reported
            |> List.filter (fun c ->
                match c.State with
                | Unreadable _ -> true
                | _ -> false)
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

        // LINE TWO IS THE ONE THE STOPPING RULE READS (.github#2525 acceptance #1 and #4).
        //
        // `drive-board`/`work-board` terminate when nothing is schedulable and no claim is live, so
        // "no active items" is not prose — it is a positive assertion that the active set was MEASURED and
        // is empty. Every unreadable row was already being filtered out of `Active` by `isActive`, so a read
        // that could not see three live claims rendered the identical sentence to a read that correctly saw
        // none, and a host that trusted it would have declared the board finished with work outstanding.
        //
        // The literal is therefore reserved for the case that earns it, and an unaccounted-for item forces
        // the failed-read wording instead. .github#2385 had already made this class of shortfall visible on
        // the TRANSITIONS line; it is this line that stayed able to assert an empty inventory over a board
        // it never finished reading.
        let unreadableRefs =
            projection.Unreadable |> List.map (fun c -> c.Ref) |> String.concat ", "

        let activeLine =
            match projection.Active, projection.Unreadable with
            | [], [] -> "no active items"
            | [], unreadable ->
                $"ACTIVE INVENTORY UNREADABLE (%d{List.length unreadable} unaccounted for): the active set is UNKNOWN, not empty — %s{unreadableRefs}"
            | active, unreadable ->
                let rendered =
                    active
                    |> List.map (fun c -> $"%s{c.Ref} [%s{encodeState c.State}]")
                    |> String.concat ", "

                let tail =
                    if List.isEmpty unreadable then
                        ""
                    else
                        $" — INCOMPLETE, %d{List.length unreadable} further item(s) unaccounted for: %s{unreadableRefs}"

                $"active items (%d{List.length active}): %s{rendered}%s{tail}"

        transitionLine + "\n" + activeLine
