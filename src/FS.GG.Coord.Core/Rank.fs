namespace FS.GG.Coord

module Rank =

    open Types

    [<Literal>]
    let StarvationDays = 21

    // The unphased sentinel. One past `phaseOrder P8Net`, so an unphased item sorts after every phased
    // one without any comparison needing to know the union's size.
    let private unphased = 9

    type Rank =
        { Escalated: bool
          Blocking: int
          Severity: Severity
          Class: ItemClass option
          Phase: Phase option
          AgeDays: int option
          Number: int }

    // DEFECT, then HARDENING, then DECISION, then unclassed.
    //
    // **`decision` IS RANKED LAST OF THE THREE, AND THAT IS NOT `Class.fromBody`'S ORDER.** The two
    // orders answer different questions and this is the one place they must not be confused.
    // `Class.fromBody` resolves what an item IS when its text says two things at once, and there
    // `decision` dominates `hardening` because it is the stronger claim. THIS order decides what a
    // worker is HANDED, and `decision` means *a human must choose before any work is authorable* —
    // `Types.fsi` says it is "surfaced, never dispatched", and `drive-board`'s stopping rule says the
    // same.
    //
    // The scheduler cannot actually enforce that. `Schedulability` refuses a decision item only through
    // ADR-0045's `Blocked on: human/decision` sentinel; an item classed by a `[decision]` TITLE alone,
    // with a real touch-set and no sentinel, is `Startable` today. Ranking `decision` second would
    // therefore have taken the one class that must never be dispatched and dispatched it FIRST, ahead of
    // every hardening item on the board — a promotion produced entirely by this rewrite, in the exact
    // direction the vocabulary was created to prevent.
    //
    // `None` is LAST, and that is AC1's rule: no priority data, no promotion. It is worth naming what
    // that costs — `drive-board` treats an unclassed row as POSSIBLY a defect, and there are ~47 of them
    // org-wide (.github#1624) — but the alternative is to promote unclassed work above triaged work on
    // the strength of nobody having looked at it, which is a guess with a scheduler's authority behind
    // it. Starvation escalation is what stops "last" meaning "never".
    let private classOrder (c: ItemClass option) =
        match c with
        | Some Defect -> 0
        | Some Hardening -> 1
        | Some Decision -> 2
        | None -> 3

    let key (r: Rank) : int * int * int * int * int * int * int =
        ((if r.Escalated then 0 else 1),
         -r.Blocking,
         severityOrder r.Severity,
         classOrder r.Class,
         (r.Phase |> Option.map phaseOrder |> Option.defaultValue unphased),
         -(r.AgeDays |> Option.defaultValue 0),
         r.Number)

    let blockingCountsOf (edges: (Ref * Blocker list) list) : Map<Ref, int> =
        edges
        |> List.collect (fun (_, blockers) ->
            blockers
            |> List.filter (Blockers.isResolved >> not)
            |> List.choose (fun b -> b.Ref)
            |> List.distinct)
        |> List.countBy id
        |> Map.ofList

    let blockingCounts (items: Item list) : Map<Ref, int> =
        items
        |> List.filter (fun i -> i.State = Open)
        |> List.map (fun i -> i.Ref, i.Blockers)
        |> blockingCountsOf

    let isEscalated (status: BoardStatus) (ageDays: int option) =
        match status, ageDays with
        | Ready, Some d -> d >= StarvationDays
        | _ -> false

    let ofItem (counts: Map<Ref, int>) (item: Item) : Rank =
        { Escalated = isEscalated item.Status item.AgeDays
          Blocking = counts |> Map.tryFind item.Ref |> Option.defaultValue 0
          Severity = item.Severity
          Class =
            match item.Class with
            | Some c -> Some c
            | None -> item.BoardClass
          Phase = item.Phase
          AgeDays = item.AgeDays
          Number = item.Ref.Number }

    let ofItemsWith (counts: Map<Ref, int>) (items: Item list) : (Item * Rank) list =
        items |> List.map (fun i -> i, ofItem counts i)

    let ofItems (items: Item list) : (Item * Rank) list =
        ofItemsWith (blockingCounts items) items

    let isUnranked (r: Rank) =
        not r.Escalated
        && r.Blocking = 0
        && r.Severity = Unset
        && r.Class.IsNone
        && r.Phase.IsNone
        && r.AgeDays.IsNone

    let explain (r: Rank) : string =
        let severity = severityWireName r.Severity

        let cls =
            match r.Class with
            | Some c -> itemClassWireName c
            | None -> "unclassed"

        let phase =
            match r.Phase with
            | Some p -> phaseWireName p
            | None -> "no phase"

        let age =
            match r.AgeDays with
            | Some d -> $"%d{d}d old"
            | None -> "age unknown"

        let escalation =
            if r.Escalated then
                // "above EVERY other term", stated exactly. An earlier draft said "above class and
                // phase", which was the wrong sentence about the right behaviour: escalation also
                // outranks the BLOCKING COUNT, and it has to — an item starving behind a permanent hub
                // (something with many dependents that it always collides with) would otherwise starve
                // forever, which is the one case anti-starvation exists for.
                $", STARVED (Ready and >= %d{StarvationDays}d — escalated above every other rank term, blocking count included)"
            else
                ""

        $"blocking %d{r.Blocking}, severity %s{severity}, %s{cls}, %s{phase}, %s{age}%s{escalation}"
