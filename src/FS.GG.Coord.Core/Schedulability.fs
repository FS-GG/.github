namespace FS.GG.Coord

module Schedulability =

    open Types

    type ColumnStartability =
        | AlwaysStartable
        | WithBacklogOptIn
        | NeverStartable

    type Schedulability =
        | Startable
        | WrongStatus of BoardStatus
        | IssueClosed
        | NoTouchSet
        | DeliberatelyNoTouchSet
        | UnusableTouchSet of tokens: string list
        | BlockedBy of Blocker list
        /// A `Blocked on: human/...` sentinel refuses the item regardless of its touch-set (#1103 leg 2).
        | AwaitingHuman of HumanBlock
        | HeldBy of WorkerId
        | HeldByLiveWork of WorkerId * pr: int
        | ItemPrOpen of pr: int
        | OverlapsInFlight of (string * string) list
        | Undetermined of reason: string

    // THE COLUMN DECISION, NAMED — so a document can READ it instead of restating it (#1057).
    //
    // These three legs were inline in `schedulable` below. They still decide exactly what they decided
    // there; what changed is that the fact now has a name, and `Protocol.boardStatuses` derives the
    // published `startable?` column from it. Typing those six answers into `Protocol.fs` instead would
    // have been a copy with a generator's authority behind it — #865's defect, #916's trap 1 — and the
    // copy would have been RIGHT on the day it was written, which is precisely how that defect gets in.
    //
    // A TOTAL match, no wildcard, for `statusWireName`'s reason: a new `BoardStatus` case must fail the
    // BUILD here rather than default to "not startable" and go quietly missing from the queue.
    let columnStartability (status: BoardStatus) : ColumnStartability =
        match status with
        | Ready -> AlwaysStartable
        | Backlog -> WithBacklogOptIn
        | NoStatus
        | InProgress
        | Blocked
        | InReview
        | Done -> NeverStartable

    // THE STARTABILITY WIRE VOCABULARY, ONCE — in `Core`, beside the union, on `statusWireName`'s and
    // `blockerStateWireName`'s terms. The first draft of #1057 spelled these three strings in the Cli's
    // writer; renaming one there compiled with zero F# errors and only a `jq` filter caught it. See the
    // .fsi.
    let columnStartabilityWireName (c: ColumnStartability) =
        match c with
        | AlwaysStartable -> "always"
        | WithBacklogOptIn -> "with-backlog-opt-in"
        | NeverStartable -> "never"

    let schedulable (allowBacklog: bool) (inFlight: TouchSet list) (item: Item) : Schedulability =

        // ORDER IS PART OF THE SPEC, not an implementation detail. Each check must come before every
        // check whose answer it would make meaningless — and it must come AFTER any check that
        // produces a stronger, more actionable statement about the same item.

        // 1. THE ISSUE BEFORE THE COLUMN (#520). The board column is a projection; the issue is the
        //    work. Asking "is the column Ready?" of a CLOSED issue is asking the wrong question of the
        //    wrong record — and answering it is how a closed item got handed out twice.
        match item.State with
        | Closed -> IssueClosed
        | Open ->

        // 2. The column. `NoStatus` is its own case and must not read as `Backlog` (#437).
        //
        //    WHICH columns are startable is `columnStartability`'s answer, not a second match here
        //    (#1057). `WrongStatus` still carries `item.Status` itself, so the case a worker is told
        //    about is the one the board actually holds — that is #437's bug, and it is a fact about the
        //    ITEM rather than about the vocabulary.
        match columnStartability item.Status with
        | NeverStartable -> WrongStatus item.Status
        | WithBacklogOptIn when not allowBacklog -> WrongStatus item.Status
        | WithBacklogOptIn
        | AlwaysStartable ->

        // 3. THE BLOCKERS, BEFORE THE TOUCH-SET — and this is the ORDERING DECISION that ADR-0034 left
        //    open for the flip to make. The two engines checked in different orders, both were right
        //    about the item, and only one of them can be the sentence a worker reads.
        //
        //    Bash's order wins, for two reasons that point the same way:
        //
        //    SEMANTICS. A blocked item cannot be started whatever its touch-set says. "No 'Paths:'
        //    declared" is an OMISSION the worker can fix in ten seconds — and telling them that about an
        //    item they still could not start afterwards sends them to fix the wrong thing, then come
        //    back to the same queue. "Blocked by #999" is the fact that actually governs.
        //
        //    COST, which is the one that settles it. Blockers are BOARD facts: the scan already has
        //    them, and they are free. A touch-set lives in the issue BODY — one REST read per item. So
        //    bash never reads the body of a blocked candidate, and never needed to. Checking the
        //    touch-set first would force the client to fetch a body for every blocked item on the board
        //    just to reach a verdict that the board could already have given — paying the budget that
        //    dies first (#418) to answer a question that was already answered.
        match Blockers.unresolved item.Blockers with
        | _ :: _ as holding -> BlockedBy holding
        | [] ->

        // 3b. THE HUMAN DECISION/ACTION, BEFORE THE TOUCH-SET (#1103/#1887). `Class: decision` is the
        //     item's own declaration that a HUMAN must choose first; a `Blocked on: human/...` sentinel
        //     records the same hold for a decision, or the distinct "human must act" hold. Either governs
        //     whatever the `Paths:` line says — a decision item may legitimately carry a real touch-set
        //     recording where its eventual implementation will land (#918). So this is checked here, not
        //     folded into the touch-set: reporting "unmatchable token" or "nobody has claimed this" about
        //     an item awaiting a human sends the reader to fix the wrong thing.
        //
        //     AFTER the concrete blockers (step 3): a `Blocked by #999` is a more actionable sentence
        //     than "a human must decide", and an item can carry both. AFTER the column (step 2): neither
        //     body declaration may be defeated by `--include-backlog`.
        //
        //     THE AUTHORITY IS THE ITEM'S TEXT, NOT THE BOARD COLUMN (#1887 AC6). `item.Class` is parsed
        //     from the `Class:` body line (with the title/sentinel compatibility evidence folded in by
        //     `Class.derive`); `item.BoardClass` is deliberately not consulted. The column is a projection
        //     and can lag. This makes all decision-class rows unschedulable even when they carry no
        //     duplicated human/decision sentinel, and makes release-to-Ready harmless: the guard is
        //     evaluated on every scheduling pass.
        //
        //     CHOSEN OVER "REQUIRE THE SENTINEL" (#1887 AC2): reading `Class` here does give scheduling
        //     two human-hold inputs, but they are not rival authorities — both come from the issue's own
        //     text, and `Class: decision` already promises "surfaced, never dispatched". Requiring every
        //     decision to repeat itself as `Blocked on: human/decision` would preserve one input only by
        //     creating a second mandatory encoding on every issue; one missing copy recreates the defect,
        //     and release circulates that row until the copy lands. The direct guard makes the declared
        //     class true immediately and keeps the action-vs-decision sentinel distinction intact.
        match item.Class, item.HumanBlock with
        | Some Decision, _ -> AwaitingHuman AwaitingHumanDecision
        | _, Some hb -> AwaitingHuman hb
        | _, None ->

        // 4. THE TOUCH-SET, BEFORE THE LOCK. "Nobody can claim this item" is a stronger and cheaper
        //    statement than "somebody already has", and a worker told the second when the first is
        //    also true fixes the wrong thing.
        match item.TouchSet with
        | Unreadable reason ->
            // WE DID NOT READ THE BODY, SO WE DO NOT KNOW THE TOUCH-SET — and an unknown touch-set is
            // not an absent one. Coercing this to `Undeclared` would report a confident OMISSION about
            // an item nobody looked at, and then schedule everything else against a surface we cannot
            // see. It is `Undetermined`: not startable, and SAID SO. (A BLOCKED item never reaches
            // here — that is the whole point of the order above, and it is why one unreadable body
            // cannot starve a worker.)
            Undetermined $"the issue body could not be read, so its touch-set is UNKNOWN — not absent (%s{reason})"
        | Undeclared -> NoTouchSet
        | DeclaredNone -> DeliberatelyNoTouchSet
        // `Paths: any` — a file-less chore (#1103 leg 8). It reserves nothing, so it has no unmatchable
        // token to refuse and (step 6) conflicts with nothing: it flows straight through to the lock and
        // is Startable. This is the whole point of splitting it from `DeclaredNone`, which stops here.
        // `TouchSet.usability` answers `Usable` for it (no tokens), so it shares the `Declared` arm below.
        | DeclaredChore
        | Declared _ ->

        // ANY unmatchable token refuses the item — and the RULE is `TouchSet.usability`'s, not this
        // function's (#864). It used to be decided here, and `Lanes.partition` decided it again and
        // reached the OPPOSITE verdict on a partly-unmatchable touch-set: this refused the item forever
        // while `Lanes` gave it a lane and left it off the chore list, so it read as blocked work rather
        // than a broken declaration. ADR-0034's promise is ONE schedulability function; a predicate that
        // shadows part of it is #485 rebuilt inside the remedy.
        //
        // The `every`/`some` split that stood here was a DEAD GUARD: two branches, one `when` clause
        // counting the tokens, and both returned `UnusableTouchSet bad`. It promised the linter "must say
        // which (#496)" and never told them apart, while `Schedulability.fsi` documented the case as
        // "EVERY token is unmatchable" — a rule this code has never implemented. Both deaths name their
        // offending tokens and both are fixed by widening those same tokens, so there is ONE verdict, and
        // now there is one branch.
        // The every/some distinction is COLLAPSED here, deliberately and in the open (#945): both mean
        // the same thing to a scheduler — the declaration reserves less than it names, so handing the
        // item out is entering a collision voluntarily — and both are fixed by widening the same
        // tokens. Collapsing is not re-deriving: the rule stayed in `TouchSet.usability`, and a new
        // case would break this match rather than slip past a threshold spelled out again here.
        match TouchSet.usability item.TouchSet with
        | TouchSet.AllUnmatchable bad
        | TouchSet.SomeUnmatchable bad -> UnusableTouchSet bad
        | TouchSet.Usable ->

        // 5. THE LOCK — and what "held" actually means.
        match item.Claim with
        | Some(claim, LeaseHeld) -> HeldBy claim.Worker

        | Some(claim, LeaseExpiredPrOpen pr) ->
            // #581. The lease lapsed; the WORK did not. An open PR on the item's own branch is the
            // worktree protocol's own artifact, and it outranks a timer. `take` handed out an item
            // exactly like this one while its worker was on it, because a loaded box stretched one
            // build past the lease — and it later reaped the claim on #485 while that worker was
            // fixing #485.
            HeldByLiveWork(claim.Worker, pr)

        | Some(_, LeaseExpiredBranchPushed) ->
            // #1055. The lease lapsed and no PR is open YET, but a pushed `item/<n>-*` branch is proof of
            // life during §3 — a REST outage can expire the lease before §5 opens the PR, and `heartbeat`
            // (REST) cannot renew through the same outage. WITHHELD, so `take` does not re-offer an item its
            // worker is standing in. `Undetermined`, not `HeldByLiveWork`: a branch is WEAKER evidence than a
            // PR (it can be a stale leftover), so this is the fail-closed "not certain the work is alive, but
            // a lock we cannot rule dead we may not hand out" — the same posture `reap` takes (it refuses).
            Undetermined "the claim's lease has expired, but a pushed item/<n>-* branch is proof of life before its PR is opened (#1055/#581) — not offered, and reap will not collect it"

        | Some(_, LivenessUnknown) ->
            // We could not ask whether the work is alive. That is NOT the same as "no PR", and
            // treating it as such is what destroyed uncommitted work. An unverifiable claim is not a
            // free item.
            Undetermined "the claim's lease has expired and we could not check for an open item/<n> PR — an unverifiable claim is not an abandoned one (#581)"

        | Some(_, LeaseExpiredNoPr)
        | None ->

        // 5b. THE MARKERLESS PR (#581 one leg over — #651). No live-held claim governs this item, but an
        //     open `item/<n>-*` PR on its own branch is an implementation ALREADY IN FLIGHT — a worker who
        //     opened a PR without a claim marker, or whose marker lapsed and was cleaned. Offering it costs a
        //     DUPLICATE implementation of work that is already written. The open-PR probe is proof of life
        //     whether or not a marker points at it; #581 read it only through the marker, so a markerless
        //     item slipped straight through to `Startable`. `ItemPr` is `None` unless the scan found such a
        //     PR AND no marker claimed the item, so a claimed item never double-counts here.
        match item.ItemPr with
        | Some pr -> ItemPrOpen pr
        | None ->

        // 6. DISJOINTNESS, last: it is the only check that depends on other items.
        let hits = inFlight |> List.collect (TouchSet.conflicts item.TouchSet)

        match hits with
        | _ :: _ -> OverlapsInFlight hits
        | [] -> Startable

    /// THE OPERATOR'S WORDS, NOT THE COMPILER'S. `%A` on a union prints its CASE NAME — `BlockerOpen`,
    /// `InProgress` — and after the flip (ADR-0034 Phase 3b) this prose is what every worker reads when
    /// a scheduler hands them nothing. `InProgress` is not even what the board says; the column is
    /// literally "In progress", so a worker who greps for what they were told finds nothing. These
    /// renderings are the projection edge: the DU is the model, and this is the only place it is put
    /// into English.
    let private statusText (s: BoardStatus) =
        match s with
        | NoStatus -> "no Status"
        | Backlog -> "Backlog"
        | Ready -> "Ready"
        | InProgress -> "In progress"
        | Blocked -> "Blocked"
        | InReview -> "In review"
        | Done -> "Done"

    let private blockerText (s: BlockerState) =
        match s with
        | BlockerOpen -> "open"
        | BlockerClosed -> "closed"
        | BlockerMerged -> "merged"
        | BlockerUnknown -> "unknown"
        | BlockerUnparseable -> "unparseable"

    /// The grammar, stated once. A refusal that does not say what WOULD have been accepted just moves
    /// the worker's confusion one step later.
    let TouchSetGrammar =
        "supported: an exact path ('src/Foo.fs'), or a directory prefix ('src/Foo', 'src/Foo/*', 'src/Foo/**'). There is no glob matcher: a leading '**/' or an interior '*' matches nothing — spell the paths out."

    /// THE OPERATOR'S QUESTION IS NOT "IS IT HELD?" BUT "SHOULD I WAIT?" — AND THAT IS A NUMBER (#428).
    ///
    /// "nothing schedulable" and "queued behind a claim held by <w>, lease frees in ~96m" are the same
    /// fact and two completely different instructions: the first reads as an empty queue and sends a
    /// worker home. Bash has always answered this (`lease_window`). The typed core must too, or the
    /// flip is an information REGRESSION on the one line a starved worker actually reads.
    ///
    /// A NEGATIVE age means the age is UNKNOWN and says so, rather than guessing. `claim` has always
    /// recorded a timestamp, but a hand-written or truncated marker may not — and inventing
    /// "frees in ~120m" out of a missing field is the confident-but-unfounded sentence that #440 and
    /// #488 were both closed for.
    let leaseWindow (leaseMinutes: int) (ageSeconds: int) : string =
        if ageSeconds < 0 then
            "lease unknown"
        else
            let left = leaseMinutes - ageSeconds / 60

            // An expired lease is not a WAIT, it is a REAP. Printing "frees in ~-180m" — or rounding
            // that to zero and implying "any moment now" — sends a worker off to wait for a holder who
            // is very likely dead. The touch-set stays reserved regardless: only `reap` may break a lock.
            if left <= 0 then
                "lease EXPIRED — reapable"
            else
                $"lease frees in ~%d{left}m"

    /// The path-collision pair, as bash renders it — STEMMED, so `src/Off/Sub/**` and `src/Off/Sub` do
    /// not read as two different things when they are one subtree. The wide arrow is not decoration:
    /// these lines are read beside a wall of issue refs, and the collision is what the eye must land on.
    let collisionText (hits: (string * string) list) =
        hits
        |> List.map (fun (a, b) -> $"%s{TouchSet.stem a}  ⇄  %s{TouchSet.stem b}")
        |> String.concat ", "

    /// THE WIRE VOCABULARY, AND THE ONLY PLACE IT IS SPELLED (#865).
    ///
    /// A `function` match, so the COMPILER enforces what a list literal never could: add a case to the
    /// union and this fails to build (FS0025, incomplete match — and this project is warnings-as-errors),
    /// so a verdict cannot reach the wire without a name, nor the docs without an entry. That is not a
    /// style preference. It is the property `ProtocolTests` claimed in a comment and did not have: the
    /// kinds were typed a THIRD time in `Snapshot`, and `ItemPrOpen` shipped on the wire while
    /// `Protocol.verdicts` never heard of it, with the guard green over both.
    ///
    /// The strings are the shipped ones. `held-by` is deliberately NOT `held`: `held` is `who`'s
    /// CLAIM-STATE vocabulary (held/stale/unclaimed, `Client.whoStateName`), a different question with a
    /// different answer set. `Protocol.verdicts` documented the verdict as `held` for exactly as long as
    /// nothing derived it, which sent a reader grepping a schedulability verdict into the claim-state
    /// vocabulary — a wrong hit reads as an answer, where a miss would at least read as a question.
    let kind =
        function
        | Startable -> "startable"
        | WrongStatus _ -> "wrong-status"
        | IssueClosed -> "issue-closed"
        | NoTouchSet -> "no-touch-set"
        | DeliberatelyNoTouchSet -> "deliberately-no-touch-set"
        | UnusableTouchSet _ -> "unusable-touch-set"
        | BlockedBy _ -> "blocked-by"
        // ONE kind, exactly as `WrongStatus` carries its column and `BlockedBy` its refs: the union has
        // ONE `AwaitingHuman` case, and `Protocol.verdicts` is pinned 1:1 to the cases by reflection. The
        // action-vs-decision distinction is the whole point (#1103) and it is NOT lost — it rides on the
        // wire as DETAIL (`Snapshot.writeDetail` emits `humanBlock: decision|action`), the same edge
        // `wrong-status` uses for its column.
        | AwaitingHuman _ -> "awaiting-human"
        | HeldBy _ -> "held-by"
        | HeldByLiveWork _ -> "held-by-live-work"
        | ItemPrOpen _ -> "item-pr-open"
        | OverlapsInFlight _ -> "overlaps-in-flight"
        | Undetermined _ -> "undetermined"

    let explain (leaseMinutes: int) (item: Item) (result: Schedulability) : string =
        let id = item.Ref.Short

        let age =
            match item.Claim with
            | Some(c, _) -> c.AgeSeconds
            | None -> -1

        match result with
        | Startable -> $"%s{id} — startable"
        | IssueClosed ->
            $"%s{id} — the issue is closed (the board column says %s{statusText item.Status}; /check-board reconciles it)"
        | WrongStatus NoStatus ->
            $"%s{id} — no Status on the board: invisible to every scheduler, and nobody set it"
        | WrongStatus s -> $"%s{id} — Status is %s{statusText s}"
        | NoTouchSet ->
            $"%s{id} — no 'Paths:' declared (cannot schedule — this is an OMISSION; declare one, or 'Paths: none' if it truly has no touch-set)"
        | DeliberatelyNoTouchSet -> $"%s{id} — 'Paths: none' (deliberately has no touch-set; not schedulable by design)"
        | UnusableTouchSet tokens ->
            let toks = String.concat ", " tokens
            $"%s{id} — unmatchable 'Paths:' token(s): %s{toks} (cannot schedule; %s{TouchSetGrammar})"
        | BlockedBy holding ->
            let names =
                holding
                |> List.map (fun b -> $"%s{b.Display} (%s{blockerText b.State})")
                |> String.concat ", "

            $"%s{id} — blocked by %s{names}"
        | AwaitingHuman AwaitingHumanDecision ->
            $"%s{id} — the item's own text records a human DECISION (`Class: decision` and/or `Blocked on: human/decision`): not schedulable by design, whatever its `Paths:` line records (#1103/#1887)"
        | AwaitingHuman AwaitingHumanAction ->
            $"%s{id} — 'Blocked on: human/action': blocked on a human ACTION (e.g. a scope grant); startable the moment it lands, not before (#1103)"
        | HeldBy(WorkerId w) -> $"%s{id} — already claimed by worker %s{w} (%s{leaseWindow leaseMinutes age})"
        | HeldByLiveWork(WorkerId w, pr) ->
            $"%s{id} — lease EXPIRED, but PR #%d{pr} is open: worker %s{w} is demonstrably still working it (#581). Not offering it; its touch-set stays reserved."
        | ItemPrOpen pr ->
            $"%s{id} — no claim marker, but PR #%d{pr} is open on its `item/%d{item.Ref.Number}-*` branch: an implementation is already in flight. Not offering it — claiming it now would duplicate work that is already written (#651)."
        | OverlapsInFlight hits ->
            // The HOLDER-BLIND rendering. `Batch.explainDecision` overrides this with the holder and its
            // lease window whenever the decision carries one — which, in a batch, is always. This is the
            // last resort, for a collision whose holder genuinely could not be named.
            $"%s{id} — overlaps in-flight work: %s{collisionText hits}"
        | Undetermined reason -> $"%s{id} — UNDETERMINED: %s{reason}"
