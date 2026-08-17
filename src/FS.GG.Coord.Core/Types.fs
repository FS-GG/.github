namespace FS.GG.Coord

module Types =

    open System

    type WorkerId =
        | WorkerId of string

        member this.Value =
            let (WorkerId v) = this
            v

    type SessionId =
        | SessionId of string

        member this.Value =
            let (SessionId v) = this
            v

    type BoardStatus =
        | NoStatus
        | Backlog
        | Ready
        | InProgress
        | Blocked
        | InReview
        | Done

    type IssueState =
        | Open
        | Closed

    type BlockerState =
        | BlockerOpen
        | BlockerClosed
        | BlockerMerged
        | BlockerUnknown
        | BlockerUnparseable

    type Ref =
        { Owner: string
          Repo: string
          Number: int }

        member this.Short = $"%s{this.Repo}#%d{this.Number}"
        member this.Canonical = $"%s{this.Owner}/%s{this.Repo}#%d{this.Number}"

    type Blocker =
        { Ref: Ref option
          Raw: string
          State: BlockerState }

        member this.Display =
            match this.Ref with
            | Some r -> r.Short
            | None -> this.Raw

    type PathToken =
        | Matchable of string
        | Unmatchable of string

    type TouchSet =
        | Undeclared
        | DeclaredNone
        | DeclaredChore
        | Declared of PathToken list
        | Unreadable of reason: string

    type HumanBlock =
        | AwaitingHumanDecision
        | AwaitingHumanAction

    type ItemClass =
        | Defect
        | Hardening
        | Decision

    type ItemKind =
        | Work
        | Anchor
        | Register
        | Directive

    type Severity =
        | Critical
        | High
        | Medium
        | Low
        | Unset

    type Phase =
        | P0Decisions
        | P1Rendering
        | P2Sdd
        | P3Governance
        | P4Templates
        | P5Versioning
        | P6Game
        | P7Audio
        | P8Net

    type Claim =
        { Worker: WorkerId
          Session: SessionId option
          AgeSeconds: int
          PreviousStatus: BoardStatus option }

    type Liveness =
        | LeaseHeld
        | LeaseExpiredNoPr
        | LeaseExpiredPrOpen of pr: int
        | LeaseExpiredBranchPushed
        | LivenessUnknown

    type PrState =
        | PrGreen
        | PrConflicted
        | PrPending
        | PrRed
        | PrUnknown
        | PrMerged
        | PrClosed

    type Item =
        { Ref: Ref
          PathRepo: string
          Status: BoardStatus
          State: IssueState
          TouchSet: TouchSet
          Blockers: Blocker list
          Claim: (Claim * Liveness) option
          ItemPr: int option
          ItemPrUnreadable: bool
          HumanBlock: HumanBlock option
          Predicate: RegistryPredicate.Verdict option

          Class: ItemClass option

          BoardClass: ItemClass option

          Kind: ItemKind option

          BoardKind: ItemKind option

          CommentCount: int option

          DeliveryRoute: DeliveryRoute.Verdict

          Severity: Severity

          Phase: Phase option

          AgeDays: int option }

    type Verdict<'a> =
        | Green of 'a
        | Red of reasons: string list
        | NoVerdict of reason: string

    // THE WIRE VOCABULARY, ONCE. This was four identical private `statusName`s (Scan, Writes, Client,
    // Snapshot). They agreed — which is the finding, not the reassurance (#916, #972). See Types.fsi.
    //
    // A TOTAL match, and deliberately no wildcard: adding a `BoardStatus` case must fail the BUILD here,
    // not render as "" on the wire and set a column to nothing. `ProtocolTests` pins the same property
    // by reflection over the union, so a case cannot reach the board unnamed (#983).
    let statusWireName (s: BoardStatus) =
        match s with
        | NoStatus -> ""
        | Backlog -> "Backlog"
        | Ready -> "Ready"
        | InProgress -> "In progress"
        | Blocked -> "Blocked"
        | InReview -> "In review"
        | Done -> "Done"

    // THE OTHER WIRE VOCABULARY, ONCE — and this one had TWO copies pointing OPPOSITE ways: `Scan`
    // rendered a `BlockerState` and `Snapshot` parsed one back, both `private`, in different projects,
    // with nothing asserting they were inverse. 775 tests passed with them disagreeing (#1012).
    //
    // A TOTAL match, no wildcard, for `statusWireName`'s reason. There is no empty case here: a blocker
    // has no "unset" state — `BlockerUnparseable` is a state we KNOW, not the absence of one.
    let blockerStateWireName (s: BlockerState) =
        match s with
        | BlockerOpen -> "open"
        | BlockerClosed -> "closed"
        | BlockerMerged -> "merged"
        | BlockerUnknown -> "unknown"
        | BlockerUnparseable -> "unparseable"

    // Every `BlockerState`, as the subject `blockerStateOfWireName` searches.
    //
    // THE ONE PART THE COMPILER CANNOT CHECK, named rather than hidden — `Protocol.everyCase` carries
    // the identical caveat for the same reason. A case missing here is a wire name that renders fine
    // and no longer PARSES, which is the round-trip breaking in the one direction a total match cannot
    // see. `TypesTests` pins this list against the union by reflection, so nobody has to remember it.
    let private everyBlockerState =
        [ BlockerOpen; BlockerClosed; BlockerMerged; BlockerUnknown; BlockerUnparseable ]

    // THE INVERSE, DERIVED — never a second list of the strings.
    //
    // This is the whole point of #1012. `Snapshot` hand-wrote the five strings a second time, in the
    // parse direction, in another project; the pair could drift and did not have to be caught. Reading
    // them back OUT of the renderer means the vocabulary is spelled exactly ONCE in this engine, and
    // "these two functions are inverse" stops being a thing anybody has to maintain.
    let blockerStateOfWireName (s: string) =
        let t = s.Trim().ToLowerInvariant()
        everyBlockerState |> List.tryFind (fun c -> blockerStateWireName c = t)

    // THE CLASS WIRE VOCABULARY, ONCE — and this one is THREE wires at birth, which is why it is written
    // as a pair on day one rather than after it drifts. The same three strings are the Projects v2 option
    // NAMES, the `Class:` body-line values `lint` reads back, and the words the `Class` options table in
    // `docs/coordination/board-schema.md` documents. #1012 measured what happens when a render/parse pair
    // lives in two modules with nothing asserting they are inverse: 775 tests stayed green while `merged`
    // rendered as `"MERGED"` and parsed as nothing, and every merged blocker read as still-holding.
    //
    // A TOTAL match, no wildcard, on `statusWireName`'s terms: a fourth `ItemClass` case must fail the
    // BUILD here rather than render as "" and clear somebody's board column. There is no empty case — an
    // item with no class has `None`, which is the absence of a class, not a class meaning absence.
    let itemClassWireName (c: ItemClass) =
        match c with
        | Defect -> "defect"
        | Hardening -> "hardening"
        | Decision -> "decision"

    // Every `ItemClass`, as the subject `itemClassOfWireName` searches.
    //
    // THE ONE PART THE COMPILER CANNOT CHECK, named rather than hidden — `everyBlockerState` carries the
    // identical caveat. A case missing here renders fine and no longer PARSES, so a `Class:` line the
    // docs tell a filer to write would read as no declaration at all, and `lint` would report the item
    // untriaged while its author had triaged it. `TypesTests` pins this list against the union by
    // reflection, so nobody has to remember it.
    let private everyItemClass = [ Defect; Hardening; Decision ]

    // THE INVERSE, DERIVED — never a second list of the strings (#1012). This is the function the body-line
    // parser calls, so the grammar `Class.fromBody` accepts is defined by the renderer above and cannot
    // drift from what `reconcile` writes onto the board.
    //
    // `None` means the string is not a class at all. Deliberately not a default: "a word we do not know"
    // must not resolve to one of three, because the resolution would be a GUESS with a parser's authority
    // behind it, which is exactly what #1588's AC3 forbids.
    let itemClassOfWireName (s: string) =
        if isNull s then
            None
        else
            let t = s.Trim().ToLowerInvariant()
            everyItemClass |> List.tryFind (fun c -> itemClassWireName c = t)

    // THE KIND WIRE VOCABULARY, on `itemClassWireName`'s exact terms and for its exact reasons — one
    // string that is three wires at birth: the Projects v2 `Kind` option name, the value a filer writes
    // in a `Kind:` body line, and the word `docs/coordination/board-schema.md`'s options table documents.
    //
    // A TOTAL match, no wildcard: a fifth `ItemKind` case must fail the BUILD here rather than render as
    // "" and clear somebody's board column. There is no empty case — an item declaring no kind carries
    // `None`, and the absence of a declaration is not a kind meaning absence.
    let itemKindWireName (k: ItemKind) =
        match k with
        | Work -> "work"
        | Anchor -> "anchor"
        | Register -> "register"
        | Directive -> "directive"

    // Every `ItemKind`, as the subject `itemKindOfWireName` searches — `everyItemClass`'s caveat
    // verbatim, and `TypesTests` pins it against the union by reflection so nobody has to remember it.
    let private everyItemKind = [ Work; Anchor; Register; Directive ]

    // THE INVERSE, DERIVED — never a second list of the strings (#1012). `Kind.fromBody` calls this, so
    // the grammar a filer may write is defined by the renderer above and cannot drift from what
    // `reconcile` writes onto the board.
    //
    // `None` means the string is not a kind at all. Deliberately not a default: resolving an unrecognised
    // word onto one of four would be a guess carrying a parser's authority — and here the guess could
    // silently EXEMPT a real work row from its own lifecycle, which is the worst direction available.
    let itemKindOfWireName (s: string) =
        if isNull s then
            None
        else
            let t = s.Trim().ToLowerInvariant()
            everyItemKind |> List.tryFind (fun k -> itemKindWireName k = t)

    let severityWireName (severity: Severity) =
        match severity with
        | Critical -> "Critical"
        | High -> "High"
        | Medium -> "Medium"
        | Low -> "Low"
        | Unset -> "Unset"

    let private everySeverity = [ Critical; High; Medium; Low; Unset ]

    let severityOfWireName (s: string) =
        if isNull s then
            None
        else
            let candidate = s.Trim()
            everySeverity
            |> List.tryFind (fun severity ->
                String.Equals(severityWireName severity, candidate, StringComparison.OrdinalIgnoreCase))

    let severityOrder (severity: Severity) =
        match severity with
        | Critical -> 0
        | High -> 1
        | Medium -> 2
        | Low -> 3
        | Unset -> 4

    // THE PHASE WIRE VOCABULARY, as a PAIR on day one — `itemClassWireName`'s rule, and for the reason
    // #1012 measured: a render/parse pair in two places with nothing asserting they are inverse stays
    // green while it is wrong. These strings are the live Projects v2 option NAMES and the rows of the
    // `repo-phase-map` table in `docs/coordination/board-schema.md`; the engine reads them and nothing
    // else writes them.
    //
    // A TOTAL match, no wildcard, so a tenth phase must fail the BUILD here rather than render as "" and
    // rank as no-phase-at-all. There is no empty case: an item with no phase carries `None`.
    let phaseWireName (p: Phase) =
        match p with
        | P0Decisions -> "P0 Decisions"
        | P1Rendering -> "P1 Rendering"
        | P2Sdd -> "P2 SDD"
        | P3Governance -> "P3 Governance"
        | P4Templates -> "P4 Templates"
        | P5Versioning -> "P5 Versioning"
        | P6Game -> "P6 Game"
        | P7Audio -> "P7 Audio"
        | P8Net -> "P8 Net"

    let phaseOrder (p: Phase) =
        match p with
        | P0Decisions -> 0
        | P1Rendering -> 1
        | P2Sdd -> 2
        | P3Governance -> 3
        | P4Templates -> 4
        | P5Versioning -> 5
        | P6Game -> 6
        | P7Audio -> 7
        | P8Net -> 8

    // Every `Phase`, as the subject `phaseOfWireName` searches — `everyItemClass`'s caveat, one
    // vocabulary over: a case missing here renders fine and no longer PARSES, so a board column a
    // human set would read as no phase at all and rank last.
    let private everyPhase =
        [ P0Decisions
          P1Rendering
          P2Sdd
          P3Governance
          P4Templates
          P5Versioning
          P6Game
          P7Audio
          P8Net ]

    // THE INVERSE, DERIVED — never a second list of the strings (#1012).
    //
    // `None` means the string is not a phase this engine speaks. Deliberately not a nearest match and
    // deliberately not `P0`: a word we do not know must not resolve to the phase that outranks every
    // other, which is the guess with a parser's authority behind it that #1588's AC3 forbids.
    let phaseOfWireName (s: string) =
        if isNull s then
            None
        else
            let t = s.Trim().ToLowerInvariant()
            everyPhase |> List.tryFind (fun p -> (phaseWireName p).ToLowerInvariant() = t)
