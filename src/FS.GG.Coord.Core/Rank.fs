namespace FS.GG.Coord

module Rank =

    open Types

    [<Literal>]
    let StarvationDays = 21

    /// The unphased sentinel. One past `phaseOrder P8Net`, so an unphased item sorts after every phased
    /// one without any comparison needing to know the union's size.
    let private unphased = 9

    type Rank =
        { Escalated: bool
          Blocking: int
          Class: ItemClass option
          Phase: Phase option
          AgeDays: int option
          Number: int }

    /// DEFECT, then HARDENING, then DECISION, then unclassed.
    ///
    /// **`decision` IS RANKED LAST OF THE THREE, AND THAT IS NOT `Class.fromBody`'S ORDER.** The two
    /// orders answer different questions and this is the one place they must not be confused.
    /// `Class.fromBody` resolves what an item IS when its text says two things at once, and there
    /// `decision` dominates `hardening` because it is the stronger claim. THIS order decides what a
    /// worker is HANDED, and `decision` means *a human must choose before any work is authorable* —
    /// `Types.fsi` says it is "surfaced, never dispatched", and `drive-board`'s stopping rule says the
    /// same.
    ///
    /// The scheduler cannot actually enforce that. `Schedulability` refuses a decision item only through
    /// ADR-0045's `Blocked on: human/decision` sentinel; an item classed by a `[decision]` TITLE alone,
    /// with a real touch-set and no sentinel, is `Startable` today. Ranking `decision` second would
    /// therefore have taken the one class that must never be dispatched and dispatched it FIRST, ahead of
    /// every hardening item on the board — a promotion produced entirely by this rewrite, in the exact
    /// direction the vocabulary was created to prevent.
    ///
    /// `None` is LAST, and that is AC1's rule: no priority data, no promotion. It is worth naming what
    /// that costs — `drive-board` treats an unclassed row as POSSIBLY a defect, and there are ~47 of them
    /// org-wide (.github#1624) — but the alternative is to promote unclassed work above triaged work on
    /// the strength of nobody having looked at it, which is a guess with a scheduler's authority behind
    /// it. Starvation escalation is what stops "last" meaning "never".
    let private classOrder (c: ItemClass option) =
        match c with
        | Some Defect -> 0
        | Some Hardening -> 1
        | Some Decision -> 2
        | None -> 3

    /// THE SORT KEY. Lower is EARLIER, and every term is a lexicographic tier below the one before it.
    ///
    /// ESCALATION IS THE TOP TERM, above the blocking count and not merely above class and phase. That is
    /// what makes it a LIVENESS guarantee rather than a nudge: the item anti-starvation exists for is one
    /// whose touch-set permanently collides with something better-ranked, and "something better-ranked"
    /// is most often a hub with many dependents. An escalation that lost to the blocking count would
    /// leave exactly that item starving.
    ///
    /// The negations are not decoration: MORE blocked dependents and MORE days old both mean EARLIER, and
    /// the natural order of an `int` says the opposite. An unknown age contributes 0 — the same as an item
    /// created today — which is deliberate: `None` must never out-rank a measured age.
    let key (r: Rank) : int * int * int * int * int * int =
        ((if r.Escalated then 0 else 1),
         -r.Blocking,
         classOrder r.Class,
         (r.Phase |> Option.map phaseOrder |> Option.defaultValue unphased),
         -(r.AgeDays |> Option.defaultValue 0),
         r.Number)

    /// How many OPEN items name each ref in a `Blocked by` edge that is still holding.
    ///
    /// THE EDGES THIS SKIPS ARE THE FINDING, NOT AN OMISSION. An unparseable blocker — prose in a
    /// dependency field — has no `Ref` by construction (`Types.Blocker`), so there is no node to credit
    /// and guessing one would distort every rank around it. A RESOLVED edge is not a dependency any more,
    /// so counting it would keep promoting an item whose dependents were all unblocked weeks ago.
    ///
    /// Counted per SOURCE item, deduplicated: an item naming `#42` twice in one field is one dependent,
    /// not two. Nothing is read — the whole graph is already on the candidate list.
    let blockingCounts (items: Item list) : Map<Ref, int> =
        items
        |> List.filter (fun i -> i.State = Open)
        |> List.collect (fun i ->
            i.Blockers
            |> List.filter (Blockers.isResolved >> not)
            |> List.choose (fun b -> b.Ref)
            |> List.distinct)
        |> List.countBy id
        |> Map.ofList

    /// STARVATION, and it is deliberately narrow.
    ///
    /// Only a `Ready` item escalates. `--include-backlog` can make a `Backlog` row a candidate, and a
    /// parked row is one somebody DECIDED not to do — letting it age its way to the front of a queue
    /// would undo the triage decision that put it there, which is the opposite of what this item is for.
    ///
    /// An item whose age we could not read never escalates, and that is the honest direction: escalating
    /// on an unknown age would promote every unreadable row above the whole board.
    let isEscalated (status: BoardStatus) (ageDays: int option) =
        match status, ageDays with
        | Ready, Some d -> d >= StarvationDays
        | _ -> false

    /// One item's rank, given the blocking counts derived from the whole candidate set.
    ///
    /// `Class` prefers the item's OWN TEXT over the board column, on `Class.derive`'s terms: the text is
    /// the authority and the column is a projection of it (.github#1588). The column is the fallback, not
    /// the other way round, so a stale projection can never outrank what the item says about itself.
    let ofItem (counts: Map<Ref, int>) (item: Item) : Rank =
        { Escalated = isEscalated item.Status item.AgeDays
          Blocking = counts |> Map.tryFind item.Ref |> Option.defaultValue 0
          Class =
            match item.Class with
            | Some c -> Some c
            | None -> item.BoardClass
          Phase = item.Phase
          AgeDays = item.AgeDays
          Number = item.Ref.Number }

    /// Every candidate's rank, in one pass, so the blocking graph is walked once for the whole batch.
    let ofItems (items: Item list) : (Item * Rank) list =
        let counts = blockingCounts items
        items |> List.map (fun i -> i, ofItem counts i)

    /// TRUE when the rank carries no priority evidence at all — no dependents, no class, no phase, no
    /// readable age. Such an item still schedules; it simply sorts last, which is AC1's whole safety
    /// argument: a board with no priority data behaves exactly as it did before this existed.
    let isUnranked (r: Rank) =
        not r.Escalated
        && r.Blocking = 0
        && r.Class.IsNone
        && r.Phase.IsNone
        && r.AgeDays.IsNone

    /// The rank's INPUTS, in English, for `batch --explain`.
    ///
    /// It prints what each term CONTRIBUTED, never a single opaque score. A scheduler whose ordering
    /// cannot be inspected is one nobody trusts (AC5), and "rank 7" answers no question a driver has.
    let explain (r: Rank) : string =
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

        $"blocking %d{r.Blocking}, %s{cls}, %s{phase}, %s{age}%s{escalation}"
