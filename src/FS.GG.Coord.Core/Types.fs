namespace FS.GG.Coord

module Types =

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

    /// A `Blocked by` entry.
    ///
    /// `Ref` is an OPTION because `BlockerUnparseable` is a real case and prose is not a ref: "Blocked by
    /// RESOLVED: shipped last week" blocks, and it has no owner, no repo and no number. The record used to
    /// demand a `Ref` anyway — so the one state the type system was told to expect was the one it could not
    /// hold, and the client quietly dropped every such blocker on the floor rather than fail to build one.
    /// An item bash called BLOCKED then reached the engine as unblocked — and the engine's answer is the
    /// one a worker acts on, so that is a worker being handed blocked work.
    ///
    /// `Raw` is what the field actually SAID, and it is always present — it is the only thing there is to
    /// show a human when the ref did not parse.
    type Blocker =
        { Ref: Ref option
          Raw: string
          State: BlockerState }

        /// What to call it in a sentence: the canonical ref when we have one, else the prose we were given.
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
        /// `Paths: any` — a file-less CHORE. It reserves nothing and conflicts with nothing, so it is
        /// SCHEDULABLE and runs alongside any concurrent item — the opposite of `DeclaredNone`, which is
        /// deliberately unschedulable. Both "reserve nothing"; they differ only in schedulability, which
        /// is the collapse #1103 leg 8 exists to break. This is a DELIBERATE empty reservation (a sentinel
        /// the parser verified), NOT #273's fail-open — a path-shaped token that reserves nothing by
        /// mistake. See Types.fsi and ADR-0045.
        | DeclaredChore
        | Declared of PathToken list
        /// The body was never read, so the touch-set is UNKNOWN — not absent. See Types.fsi.
        | Unreadable of reason: string

    /// A `Blocked on: human/...` body-line sentinel: the item cannot be scheduled because a HUMAN must
    /// act first, whatever its `Paths:` line says. The action-vs-decision distinction is load-bearing —
    /// `Blocked by` is ref-typed and structurally cannot say "blocked on a person" (#1103 leg 2), so this
    /// carries the WHY a bare empty `Blocked by` flattened. Decided by @EHotwagner on #1103; see ADR-0045.
    type HumanBlock =
        /// `Blocked on: human/decision` — unstartable until a human CHOOSES (a decision item, e.g. #918/#498).
        | AwaitingHumanDecision
        /// `Blocked on: human/action` — blocked on a human ACTION such as a scope/credential grant; it
        /// becomes startable the moment the action lands (e.g. #574), but not before.
        | AwaitingHumanAction

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

    type Item =
        { Ref: Ref
          Status: BoardStatus
          State: IssueState
          TouchSet: TouchSet
          Blockers: Blocker list
          Claim: (Claim * Liveness) option
          /// The open `item/<n>-*` PR when this item carries NO live-held claim marker — a duplicate
          /// implementation already in flight (#651). `None` when there is no such PR, or when a claim
          /// marker already governs liveness (there the open PR is the claim's `LeaseExpiredPrOpen`).
          ItemPr: int option
          /// A `Blocked on: human/...` sentinel parsed from the body (#1103 leg 2). `None` when the item
          /// declares no such line. When present it refuses scheduling regardless of `TouchSet`.
          HumanBlock: HumanBlock option
          /// The item's declared machine-checkable registry predicate, ALREADY RESOLVED against the owning
          /// manifest (ADR-0050 call-site B / .github#1203). `None` means the item declares no such
          /// predicate — the common case, and the one that flips on blockers-cleared exactly as today. A
          /// resolved `Verdict` is the FACT the pure `BLOCKER-CLEARED` derivation reads, the way it reads
          /// `Blocker.State`: `Agrees` lets the flip proceed, `Contradicts`/`Unknown` HOLD it (fail closed,
          /// #266). Resolved at the impure edge, never in `Chore.derive` — see `RegistryPredicate`.
          Predicate: RegistryPredicate.Verdict option }

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

    /// Every `BlockerState`, as the subject `blockerStateOfWireName` searches.
    ///
    /// THE ONE PART THE COMPILER CANNOT CHECK, named rather than hidden — `Protocol.everyCase` carries
    /// the identical caveat for the same reason. A case missing here is a wire name that renders fine
    /// and no longer PARSES, which is the round-trip breaking in the one direction a total match cannot
    /// see. `TypesTests` pins this list against the union by reflection, so nobody has to remember it.
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
