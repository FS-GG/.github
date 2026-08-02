namespace FS.GG.Coord

/// THE PROTOCOL, AS DATA — the source every projection is emitted FROM (ADR-0034 §4.5).
///
/// A coordination rule is currently stated in up to six places: the ADR, the canonical doc, the tool,
/// and four `SKILL.md` bodies across two skill roots — then content-addressed into `repos.lock` and
/// byte-copied into six receivers. **54 vendored copies of the protocol.** The propagation edge is a
/// second issue and a second PR, every time (#309 → #502, #481 → #531), and the collision attractor was
/// removed BY HAND twice (#532, #551) before #570 gated it.
///
/// The inversion is the whole of §4.5: `fsgg-coord` was ALREADY the model — it was simply not the
/// SOURCE. In every drift that can be dated, the tool was right and the prose was wrong.
/// `check-worker-id-attractor.py` even calls `parallel-work.md` *"the document those skills are a
/// projection OF"*, and exists only because that projection is copied by hand.
///
/// So the rules live HERE, once, in the typed core that already enforces them, and the prose is
/// GENERATED. A rule then cannot land in one tier and not the others, because there are no tiers — it
/// is `repos.lock`'s discipline applied to the protocol itself: a generated, CI-gated artifact that
/// nobody authors, where a collision is a rebase rather than a decision.
///
/// The proof this was needed arrived while the flip was being written: `TouchSetGrammar` was typed into
/// F# by hand, byte-identical to bash's `TOUCHSET_GRAMMAR` purely by luck, with NOTHING holding the two
/// in step. That is a seventh copy, added by the change that was fixing the copies.
module Protocol =

    open Types

    /// One rule. `Id` is the anchor a projection references, so a doc can cite a rule without restating
    /// it, and a reader can grep the id back to the code that enforces it.
    type Rule =
        { Id: string
          Title: string
          /// The rule itself, in one paragraph. This is the text that lands in every projection.
          Statement: string
          /// Why it is this way — the incident that bought it. Emitted into the canonical doc, and
          /// omitted from the terse skill projections.
          Because: string }

    /// A schedulability verdict, as the worker meets it.
    type VerdictDoc =
        { Kind: string
          Meaning: string }

    /// One exit code, as the CALLER's contract — the fact a shell script reads without parsing prose.
    type ExitCodeDoc =
        { Code: int
          /// The `EX_*` spelling, where the code has one a worker would recognise; `""` where it does
          /// not. The name is a label on the number, never a second source for it.
          Name: string
          /// What the code means the engine OBSERVED.
          Meaning: string
          /// What the caller should DO about it. A code whose remedy is unstated is a code a worker
          /// invents a remedy for.
          Action: string }

    /// One row of `release`/`reap`'s column precedence — see `Protocol.fsi` for the shape and why it is
    /// not an `ExitCodeDoc`.
    type ReleaseColumnDoc =
        { /// The input, in PRECEDENCE ORDER — the first row whose condition holds wins.
          Condition: string
          /// The column the item ends in.
          EndState: string
          /// Whether `release` writes the column, or leaves it as it is — the #331 observable.
          Writes: bool
          /// The stdout line — the tell, since exit 0 cannot confirm a park.
          Stdout: string }

    /// One `BlockerState`, as a reader of the scan's JSON meets it.
    type BlockerStateDoc =
        { /// The string `scan` emits — `Types.blockerStateWireName`'s answer, never a second spelling.
          Wire: string
          /// Whether the blocker HOLDS. The one bit a reconciler acts on, and the one the union's case
          /// name does not carry: `unknown` and `unparseable` read like non-answers and BLOCK.
          Holds: bool
          /// What the state says about the blocker, and why it holds or does not.
          Meaning: string }

    /// One board `Status` option, as a filer meets it. See Protocol.fsi.
    type BoardStatusDoc =
        { /// The Projects v2 option name — `Types.statusWireName`'s answer, never a second spelling.
          Wire: string
          /// Whether a scheduler offers an item in this column — `columnStartability`'s answer, spelled by
          /// `columnStartabilityWireName`. A string on `VerdictDoc.Kind`'s terms; see Protocol.fsi.
          Startable: string
          /// What the column asserts, and why a scheduler does or does not offer it.
          Meaning: string }

    /// One TOP-LEVEL key of the snapshot document, as a reader of `scan --json` meets it. See Protocol.fsi.
    type SnapshotKeyDoc =
        { /// The key as it appears on the wire — `Scan.snapshot` writes this string.
          Key: string
          /// Whether a RECONCILER acts on this key, or merely carries it. The one bit that decides
          /// whether a `jq` filter has any business selecting on it, and the one a key NAME cannot
          /// carry: `limit` and `leaseMinutes` are the scan's own parameters echoed back, not board
          /// facts, so a pass that reconciles against them is reconciling against its own request.
          Reconciled: bool
          /// What the key carries, and why a reconciler does or does not act on it.
          Meaning: string }

    // ---- the verdicts ------------------------------------------------------------------------------
    // ONE TOTAL FUNCTION, ONE UNION. Fourteen of the scheduler family's issues were a missing case in
    // it; #485 named the cause — startability was computed in five places and agreed in none. These are
    // those cases, and the projection cannot drift from them because it is emitted from them.
    //
    // IT SAID THAT, AND IT WAS A TWELFTH COPY (#865). `verdicts` was a hand-typed list of Kind/Meaning
    // pairs that referenced the union NOWHERE, so "emitted from them" was an aspiration the code did not
    // implement — and it had already drifted, in both directions at once:
    //
    //   * `ItemPrOpen` (#651) was returned by `schedulable`, emitted as `item-pr-open` on the wire, and
    //     documented NOWHERE. A worker handed a verdict grepped it into the doc that explains it —
    //     the promise `Protocol.fsi` opens by making — and found nothing.
    //   * `held` was documented where the wire has always emitted `held-by`. Worse than a miss: `held`
    //     IS a live token in `who`'s claim-state vocabulary, so the grep SUCCEEDS, in the wrong
    //     vocabulary, and answers a question the reader did not ask.
    //
    // And the guard was green over both, because it counted entries and compared them to a THIRD
    // hand-written set of the same strings (`ProtocolTests`), whose comment claimed a compiler property
    // F# does not give a list literal. A generator whose source is hand-maintained just moves the drift
    // upstream and makes it authoritative — which is precisely what ADR-0034 §4.5 was about to lean on.
    //
    // So the Kind is no longer WRITTEN here. It is `Schedulability.kind`, applied to each case: an
    // exhaustive match the compiler checks. What is left here is the one thing the union genuinely does
    // not carry — the MEANING — and it is a total match too, so a new case cannot reach the docs
    // undocumented. The build fails; nobody has to notice.

    /// One value of each `Schedulability` case, as the subject the two matches below are applied to.
    ///
    /// THE ONE PART THE COMPILER CANNOT CHECK, and it is named rather than hidden. F# gives no
    /// exhaustiveness property for a list literal — that was #865's defect, asserted in a comment as
    /// though it were a guarantee. The samples' FIELDS are irrelevant (`kind` and `meaning` both ignore
    /// them); only the set of CASES matters, and `ProtocolTests` pins that against the union by
    /// reflection, which does not depend on anybody remembering this list.
    let private everyCase: Schedulability.Schedulability list =
        [ Schedulability.Startable
          Schedulability.IssueClosed
          Schedulability.WrongStatus Backlog
          Schedulability.BlockedBy []
          Schedulability.AwaitingHuman AwaitingHumanDecision
          Schedulability.NoTouchSet
          Schedulability.DeliberatelyNoTouchSet
          Schedulability.UnusableTouchSet []
          Schedulability.HeldBy(WorkerId "")
          Schedulability.HeldByLiveWork(WorkerId "", 0)
          Schedulability.ItemPrOpen 0
          Schedulability.OverlapsInFlight []
          Schedulability.Undetermined "" ]

    /// What the verdict MEANS to the worker who is handed it — the one fact the union does not carry.
    /// A total match: a new case fails the build here rather than reaching a projection undocumented.
    let private meaning =
        function
        | Schedulability.Startable -> "Nothing holds it. It can be claimed now."
        | Schedulability.IssueClosed ->
            "The issue is CLOSED while the board still shows it open. The issue's state is the WORK; the board column is a PROJECTION of it. When they disagree, the issue wins — run /check-board."
        | Schedulability.WrongStatus _ ->
            "Its board Status is not one a scheduler hands out (or it has none at all, which makes it invisible to every scheduler and is a bug, not a decision)."
        | Schedulability.BlockedBy _ ->
            "A `Blocked by` entry is unresolved. CLOSED and MERGED resolve; OPEN, unverifiable and unparseable all BLOCK."
        | Schedulability.AwaitingHuman _ ->
            "`Blocked on: human/...` — a HUMAN must act first, whatever the `Paths:` line records, so an agent cannot make the call the item exists to escalate (#918). `human/decision` is unschedulable until a human CHOOSES; `human/action` becomes startable the moment a human action (e.g. a scope grant) lands, but not before. Which one rides on the verdict's `humanBlock` detail."
        | Schedulability.NoTouchSet ->
            "No `Paths:` line at all — an OMISSION. The item is real work and it is invisible to every worker who asks for work. Declare one, or `Paths: none` if it truly has no touch-set."
        | Schedulability.DeliberatelyNoTouchSet ->
            "`Paths: none` — a decision somebody made. An epic, a decision item, an investigation whose scope IS the question. Unschedulable BY DESIGN, and correct."
        | Schedulability.UnusableTouchSet _ ->
            "The declaration contains token(s) that can match no file, so they reserve NOTHING — and files nobody reserved are invisible to every other worker's overlap check."
        | Schedulability.HeldBy _ -> "A live claim marker holds it. Wait out the lease, or talk to the worker."
        | Schedulability.HeldByLiveWork _ ->
            "The lease EXPIRED but the work did not: an open `item/<n>-*` PR is the worktree protocol's own artifact, and it outranks a timer. Not offered; its touch-set stays reserved."
        | Schedulability.ItemPrOpen _ ->
            "No claim marker governs it, but an `item/<n>-*` PR is already OPEN on its branch — an implementation is in flight whether or not anyone claimed it. Not offered: claiming it would duplicate work that is already written (#651)."
        | Schedulability.OverlapsInFlight _ ->
            "Its files collide with work already in flight. The holder and its lease window are named, because \"nothing schedulable\" and \"queued behind a claim that frees in ~96m\" are the same fact and two completely different instructions."
        | Schedulability.Undetermined _ ->
            "WE COULD NOT DECIDE — and that is never a silent no. An unreachable answer is not a negative one. This is the case whose absence made every other case a lie waiting to happen."

    let verdicts: VerdictDoc list =
        everyCase
        |> List.map (fun c ->
            { Kind = Schedulability.kind c
              Meaning = meaning c })

    // ---- the blocker wire vocabulary ---------------------------------------------------------------
    // `check-board` §1 spelled the five cases by hand, in a sentence that NAMED its source: "the five
    // cases of the engine's `BlockerState`". Naming the source is not reading it. A reconciler selects
    // on these strings in `jq`, so a list that drifts does not merely mislead — it matches nothing, every
    // blocker reads as still-holding, `BLOCKER-CLEARED` never fires, and the pass reports a CLEAN BOARD
    // over items rotting behind shipped work (#476). A false clean is this skill's worst output by its
    // own account, and the doc was one edit away from producing it.
    //
    // This could not be generated before #1012. The vocabulary was two `private` INVERSE copies outside
    // `Core` (`Scan` rendered, `Snapshot` parsed), so `Protocol.fs` could not reach it, and typing the
    // five cases here would have been a THIRD copy wearing a generator's authority — #865's defect, and
    // the trap #916 wrote down. `Types.blockerStateWireName` owns the strings now; this module asks it.

    /// Every `BlockerState` case, by reflection.
    ///
    /// NOT a hand-written list, and the asymmetry with `everyCase` above is the point rather than an
    /// inconsistency. `everyCase` must be written out because `Schedulability`'s cases carry FIELDS, so
    /// there is no value to build without inventing one — which is exactly why it needs `ProtocolTests`
    /// to pin it by reflection, and why #865 got in. `BlockerState`'s five cases are all NULLARY, so the
    /// list can be DERIVED, and a list nobody writes is a list that cannot omit a case. No pin is needed
    /// here because there is no copy to pin — the defect is absent rather than tested for.
    let private everyBlockerState: BlockerState list =
        FSharp.Reflection.FSharpType.GetUnionCases typeof<BlockerState>
        |> Array.map (fun c -> FSharp.Reflection.FSharpValue.MakeUnion(c, [||]) :?> BlockerState)
        |> Array.toList

    /// What the state SAYS — a total match, on the same terms as `meaning` above.
    let private blockerMeaning =
        function
        | BlockerOpen -> "The blocker is open. It HOLDS."
        | BlockerClosed -> "The blocker issue is closed. It does not hold — the work it named is finished or abandoned."
        | BlockerMerged ->
            "The blocker is a MERGED pull request. It does not hold. A rule that cleared only on CLOSED would unblock when the PR was ABANDONED and block forever once it was FINISHED — the gate opening precisely when the work is thrown away (#476)."
        | BlockerUnknown ->
            "The ref parsed and its state could not be read. It HOLDS: \"I could not look\" is not \"I looked and it is fine\" (#266). Usually an off-board ref the scan could not resolve — board it, and it becomes `open` or clears."
        | BlockerUnparseable ->
            "The `Blocked by` text is not an issue ref at all. It HOLDS: prose in a dependency field is a question nobody answered, and a field this pass cannot read is not a field it may declare empty."

    // NEITHER of the two facts here is decided in this module. `Wire` is `Types.blockerStateWireName`;
    // `Holds` is `Blockers.isResolvedState`, negated. This file DOCUMENTS the protocol — the moment it
    // also DECIDES a bit of it, it is a copy with a generator's authority behind it, which is strictly
    // worse than the hand-written table it replaced (#865). The first draft of this function typed the
    // five `Holds` answers out by hand and they were right, which is exactly how that defect gets in.
    let blockerStates: BlockerStateDoc list =
        everyBlockerState
        |> List.map (fun b ->
            { Wire = blockerStateWireName b
              Holds = not (Blockers.isResolvedState b)
              Meaning = blockerMeaning b })

    // ---- the board's Status vocabulary -------------------------------------------------------------
    // `cross-repo-coordination` spelled the six options by hand, in the field table a filer reads before
    // setting a column — the same defect as `blockerStates` above, in the vocabulary the org has already
    // drifted on once (`Repo Scope`'s row on that same table carries a "**not** `Repository`" warning
    // because a previous reader used the wrong field).
    //
    // The bite is asymmetric, and it is the asymmetry that makes this worth generating. A filer who
    // copies a drifted spelling is fine: `set-field` refuses an unknown option, loudly, before it writes.
    // A RECONCILER is not: it selects these strings in `jq`, a `.status` selector that matches nothing
    // yields no rows, and no rows reads as a CLEAN BOARD (#476, and #1012's measured shape).
    //
    // `Wire` is `Types.statusWireName` and `Startable` is `Schedulability.columnStartability` — this
    // module decides NEITHER. `Startable` was not publishable at all until #1057: it lived as three legs
    // of a `match` inside `schedulable`, reachable only by running the scheduler, so the only way to
    // state it here was to type the six answers out again — a copy with a generator's authority behind
    // it (#865, #916's trap 1). The fix was to name the fact, not to copy it carefully.

    /// Every `BoardStatus` case, by reflection — nullary cases, so derivable on `everyBlockerState`'s
    /// terms, and a list nobody writes is a list that cannot omit a case.
    let private everyBoardStatus: BoardStatus list =
        FSharp.Reflection.FSharpType.GetUnionCases typeof<BoardStatus>
        |> Array.map (fun c -> FSharp.Reflection.FSharpValue.MakeUnion(c, [||]) :?> BoardStatus)
        |> Array.toList

    /// What the column ASSERTS — and, by returning an option, whether the board offers it as a column
    /// option at all. ONE total match, deliberately: "is this a `Status` option?" and "what does it mean?"
    /// are answered in the same place, so a new `BoardStatus` case must be classified exactly ONCE and
    /// cannot be added to one list while being forgotten by the other.
    ///
    /// `NoStatus` is the only `None`, and it is not an omission. Its wire form is `""` — the ABSENCE of a
    /// column, not an option a filer can select — and a document that published `""` as a settable option
    /// would be inviting exactly #437: `NoStatus` read as though it were `Backlog`.
    let private boardOptionMeaning =
        function
        | NoStatus -> None
        | Backlog ->
            Some
                "Filed, not triaged. The honest resting place for a finding nobody has scheduled yet. A scheduler passes it over unless the caller asks for it (`--include-backlog`), so a park here is invisible to a plain `take` BY DESIGN — that is what parking means."
        | Ready ->
            Some
                "Triaged and startable. The only column a scheduler hands out unconditionally, so an item that is real work belongs here or it will not be worked."
        | InProgress ->
            Some
                "A worker holds it — the claim's own footprint, written by `claim` and reset by `release`. Not a column to set by hand: the claim marker is the lock, and this column is only its shadow."
        | Blocked ->
            Some
                "Something else must land first, and `Blocked by` names what. Mirrors the `blocked` label. Not offered — and an item sitting here whose blockers have all resolved is startable work no `take` will ever offer, which is why /check-board re-verifies them."
        | InReview -> Some "The work is written and its PR is open. Not offered: claiming it would duplicate an implementation already in flight."
        | Done -> Some "Finished and merged, and the stamp says so. `done --flip` sets it once it confirms the merge; setting it by hand is how a board starts lying."

    let boardStatuses: BoardStatusDoc list =
        everyBoardStatus
        |> List.choose (fun s ->
            boardOptionMeaning s
            |> Option.map (fun m ->
                { Wire = statusWireName s
                  Startable = s |> Schedulability.columnStartability |> Schedulability.columnStartabilityWireName
                  Meaning = m }))

    // ---- take's exit contract ----------------------------------------------------------------------
    // #585 gave `take` codes that tell "I claimed you an item" apart from the four ways it can claim
    // NOTHING, so `take && work_it` cannot fire on nothing. The codes were then RESTATED by hand in
    // /pnext-item §1 — and the restatement was wrong in three ways at once, which is the case for
    // generating it rather than proof-reading it harder:
    //
    //   * it documented `EX_PARTIAL` as "could not read the board — a no-verdict". `Errors.ExPartial`
    //     is a WRITE that half-landed (a `set-field --batch` outcome). `take` never returns it, and the
    //     code a failed READ actually carries is 1 (or EX_RATE for a budget).
    //   * its "≠0, ≠2" row swallowed rows 5, 6 and 75 — every one of them is also "≠0, ≠2", so read
    //     top-down the table contradicted itself. It gets one thing right that a naive replacement
    //     loses, though, and the first draft of THIS list lost it: a catch-all covers 3 (the batch was
    //     REFUSED), which is reachable and easy to forget. Enumerating beats a catch-all only if the
    //     enumeration is complete.
    //   * it read EX_RATE as "the GraphQL budget". Since #897 the engine names the budget that ACTUALLY
    //     died, and REST is the one that takes `claim`/`take`/`who` down with it (ADR-0034 §3).
    //
    // The list is ordered as a worker meets it: the one success first, then the failures by how likely
    // they are to be the reason a loop stopped.
    let takeExitCodes: ExitCodeDoc list =
        [ { Code = ExitCode.toInt ExitCode.Green
            Name = ""
            Meaning = "An item was CLAIMED. This is the ONLY code that means you hold one."
            Action = "Go work it — and only here." }
          { Code = ExitCode.toInt ExitCode.NoneStartable
            Name = "EX_NONE"
            Meaning =
              "Looked, and nothing was startable — an empty or all-blocked queue. A LOOK THAT SUCCEEDED and found nothing, which is why it is not 0 and not a read failure."
            Action =
              "Nothing to do: stop, or wait for the board to free up. Diagnose before you idle — `batch --include-backlog`, `who`, `next` each name a different reason a full board looks empty." }
          { Code = ExitCode.toInt ExitCode.Contended
            Name = "EX_CONTENDED"
            Meaning =
              "The item was startable when it was picked and the claim CAS lost every race for it — somebody else got there first."
            Action = "Back off briefly and retry. The board is busy, not empty." }
          { Code = ExitCode.toInt ExitCode.Rate
            Name = "EX_RATE"
            Meaning =
              "A rate limit is exhausted. In JSON mode the stderr failure envelope carries `rateLimit: primary|secondary|unknown` (#1892), so drivers do not parse the explanation. A primary REST limit takes `claim`/`take`/`who` with it, because the lock lives there (ADR-0034 §3); GraphQL takes the board reads. A secondary (abuse-detection) limit is different: it may name NO reset, never appears in `gh api rate_limit`, and is triggered by concurrent request bursts. A healthy `gh api rate_limit` reading therefore does not disprove this refusal. For a primary REST limit, the fleet standing down is designed behaviour (#976): answering \"is this item takeable?\" costs the very budget that is gone, and a lock you cannot verify is not a lock."
            Action =
              "For a PRIMARY limit, back off until the reset it names — do not loop. For a SECONDARY limit, reduce concurrency and back off; it may name no reset, and waiting alone will not prevent the same request burst from re-triggering it. After either limit clears, run `flush --dry-run`: a board write you made on an exhausted budget is QUEUED, and nothing replays it for you. AND IF YOU ARE HOLDING AN ITEM, `heartbeat` is REST too — an outage that outlives your lease cannot be renewed through, and the moment REST returns your item is startable again and the next `take` hands it to somebody else. Two things save you and neither is the timer: an OPEN `item/<n>-*` PR (#581 — the lease lapsed, the work did not), or a liveness probe that itself fails (which fails closed, #266). Push the branch and open the PR EARLY: it is the only proof of life that does not depend on the budget you just lost." }
          { Code = ExitCode.toInt ExitCode.Red
            Name = ""
            Meaning =
              "REFUSED — the batch cannot be scheduled at all. Some in-flight claim declares a touch-set that matches no file, so it reserves NOTHING, and scheduling against it would hand its files to a second worker. The message names the item and the offending tokens."
            Action =
              "Do NOT retry — it will refuse identically until the declaration is fixed. Fix the claim it names (`widen <issue> --paths '<paths>'`), or talk to its holder." }
          { Code = ExitCode.toInt ExitCode.Error
            Name = ""
            Meaning =
              "No verdict was reached, for one of two reasons the message tells apart: the engine refused your INPUT before it looked (no worker id resolves; the board document does not parse), or the board READ failed. A read failure is never an empty queue and never EX_NONE (#266) — \"I could not look\" and \"I looked, and it is empty\" keep different codes on purpose."
            Action =
              "Read the message. A refused input is not retryable — it names its own remedy. Retry only a read failure, and investigate one that persists." }
          { Code = ExitCode.toInt ExitCode.Defect
            Name = ""
            Meaning =
              "The ENGINE broke — an unhandled defect, with a stack trace. Its own code, so a broken engine cannot hide behind a stream of what look like bad inputs."
            Action = "Report it. Do not retry, and do not work an item you were not handed." } ]

    // ---- landable's exit contract ------------------------------------------------------------------
    // #900, and it is #889 one command over: /pnext-item §5 restated `landable`'s codes BY HAND, and
    // restated BASH's — "green (0), pending (3), red / conflicted / unknown (1)". The engine's are
    // 0/7/3/4, and the divergence is deliberate and on the record (`Client.fs`, ADR-0040 §5): bash
    // numbered the poll loop 0/3/1, where the engine keeps 3 == red across every verdict command
    // (`done`/`decide`/`adopt`) and gives pending its own 7. The LITERALS differ; the PROPERTY does not.
    //
    // The table was therefore wrong in BOTH directions on the two codes that matter, and `landable`'s
    // exit code exists precisely so a poll loop need not parse the verdict word:
    //
    //   * 3 means RED, and the table said PENDING — so `until landable "$pr"; do sleep 30; done` waits
    //     FOREVER on a PR that will never go green. Not a wrong answer: a hang, on the failure case, so
    //     it survives every green rehearsal.
    //   * 7 means PENDING, and the table had no 7 — so a loop reads it as an unrecognised failure and
    //     stops waiting on a PR that is merely still running.
    //
    // ENUMERATING BEATS A CATCH-ALL ONLY IF THE ENUMERATION IS COMPLETE (#889's lesson, paid for by a
    // first draft of `takeExitCodes` that dropped `ExitRed`). The four-row match `Client.landable` ends
    // on is NOT the whole contract — #900's own issue body lists those four and stops. Two more are
    // reachable, and they are the easy ones to forget:
    //
    //   * 1, from the REFUSED-INPUT arms ahead of the read (`--repo` absent, a PR ref that is not a
    //     number, `oneArg`'s arity check). Never retryable.
    //   * 2, from `Program.main`'s top-level defect handler, which wraps every command.
    //
    // AND THERE IS NO 75 HERE, WHICH IS THE ROW A READER OF `take`'S TABLE WILL EXPECT. `landable` is
    // fail-closed BY CONSTRUCTION: `Reads.prLandableRequire` returns a bare `PrState` with no error
    // channel, so a rate limit, a 404 and a `null` mergeability all resolve to `PrUnknown` — exit 4, an
    // honest no-verdict — rather than to a budget code. A `landable` that exited 75 would be a defect.
    //
    // AND THE ENUMERATION GREW AGAIN AT #1680, which is the lesson above arriving a second time. The
    // four-row match was complete over the OPEN-PR vocabulary and silent about a PR that is not open —
    // so `landable` on a MERGED PR returned 7, the one code documented as worth retrying, for the most
    // terminal state GitHub has. `NotOpen` (10) is that missing row. Note it is a row the ENGINE gained
    // a verdict for, not merely a table entry: the fix is `PrMerged`/`PrClosed` in the vocabulary, and
    // this table is DERIVED from that (`ExitCode.landableCodes` is checked complete against it), which
    // is #1680 AC6 — the projections must follow the vocabulary, never restate it beside it.
    //
    // Ordered as a poll loop meets it: the one green, then the one code worth retrying, then the ways
    // to stop.
    let landableExitCodes: ExitCodeDoc list =
        [ { Code = ExitCode.toInt ExitCode.Green
            Name = ""
            Meaning =
              "GREEN — the PR is finished work: it merges cleanly, and every workflow run and check-run scored on its head SHA passed. The ONLY code that means merge it."
            Action = "Merge it. This is the only code that says so." }
          { Code = ExitCode.toInt ExitCode.Pending
            Name = ""
            Meaning =
              "PENDING — the verdict has not SETTLED: checks are still running, none have registered yet, the run set is still growing, GitHub has not finished computing the PR's mergeability (it does so in a BACKGROUND job, and `null` is the normal first answer for a PR you just opened — #950), GITHUB ITSELF SAYS IT WILL REFUSE THIS MERGE — its `mergeable_state` for the gated head is `blocked` (the base branch policy is not satisfied: a required context that failed, or that has NO CHECK RUN AT ALL because the workflow producing it is not on this branch, or a required review, or an unresolved conversation), `behind` (a strict base moved) or `draft` (#1575) — or an assertion you added (`--require`, `--sha`) is not yet met. The ONE retryable verdict, which is why it has a code of its own rather than sharing one with a way to stop."
            Action =
              "Keep waiting — this is the only code that says wait. Prefer `--wait`, which polls until the verdict settles rather than believing an early green. A `pending` that NEVER resolves is a finding, and the remedies are OPPOSITE, so read which one you were handed. GitHub refused the merge (`blocked`/`behind`/`draft`, and the line names the state and the base branch): REBASE onto the current base so the required workflow exists on this head and can fire; mark a DRAFT ready for review; or stop requiring a context nothing on this branch can produce. WAITING CANNOT CREATE A CHECK RUN FOR A WORKFLOW THAT DOES NOT EXIST ON THIS BRANCH — no amount of polling makes an absent producer report, which is why this arm names the context rather than telling you to wait harder. Otherwise: the job was RENAMED, its workflow's `paths:` filter no longer matches, `--sha` named the wrong commit, or GitHub never finished computing mergeability (rare, and not something waiting longer fixes — read the PR yourself)." }
          { Code = ExitCode.toInt ExitCode.Red
            Name = ""
            Meaning =
              "RED or CONFLICTED — two words, one code, because both mean STOP and neither improves by waiting. Red: a run or check-run failed. Conflicted: the PR does not merge cleanly, so GitHub cannot build `refs/pull/N/merge` and gives it NO CI at all — which is why it is returned immediately rather than polled."
            Action =
              "Stop. Do NOT wait — 3 is the code the recipe used to call `pending`, and a loop that waits on it never terminates. A red check is a finding; a conflicted PR needs a rebase, which is AUTHORING, not landing." }
          { Code = ExitCode.toInt ExitCode.NotOpen
            Name = ""
            Meaning =
              "MERGED, or CLOSED without merging — the PR is NOT OPEN, so there is nothing left to gate (#1680). The word on stdout tells the two apart: `merged` means the work LANDED, `closed` means it was abandoned or rejected and nothing landed. Terminal, and deliberately NOT 7: GitHub reports `mergeable: null` for a merged PR, which used to read as \"still computing\" and return PENDING — the ONE code the contract defines as worth retrying — for the most terminal state there is. `landable <merged-pr> --wait` therefore spent its entire 600s budget (30 tries x 20s) on a settled fact, every time, and the caller that meets this most is the RECOVERY path (`pnext-item` §5, `adopt`), which re-gates a PR whose worker died between merge and stamp. It was told to wait forever on work that was already landed. It is also not 3: `merged` is a success, and folding it into the red/conflicted code would tell that same successor to stop rather than to stamp."
            Action =
              "Stop polling — no amount of waiting reopens a PR, and `--wait` does not poll this at all. READ THE WORD, because the next act is opposite. `merged`: the work is LANDED. Do not merge it again and do not treat it as a failure — if you are recovering an item whose worker died mid-flight, go STAMP it (`done <ref> --flip --pr <pr>`). `closed`: nothing landed, so do NOT stamp it done; the branch was abandoned or the PR rejected, and the item needs re-work or release, not a merge." }
          { Code = ExitCode.toInt ExitCode.NoVerdict
            Name = ""
            Meaning =
              "UNKNOWN — no verdict, and this is the FAIL-CLOSED one (#266). The read could not be made or its answer was not conclusive: a rate limit, a 404, a PR whose `mergeable` field is ABSENT entirely. Note what it is NOT. An UNREADABLE BRANCH POLICY is NOT one of these causes (#1575): when GitHub says it will refuse the merge, the refusal stands on GitHub's OWN word (`mergeable_state`, already in the PR body) and the policy read is DIAGNOSIS ONLY — a 403 or a rate limit on it costs you the SENTENCE naming which context is unmet, never the verdict, which stays PENDING (7). A gate that failed closed on a read the fleet's token cannot make would not fail closed; it would fail ALWAYS (#463). A `mergeable` GitHub has not computed YET is PENDING (7), not this — it is guaranteed to change, and calling it unknown made `--wait` settle at once and abandon a seconds-old PR (#950). And there is no EX_RATE (75) here, unlike `take`: an exhausted budget arrives as this code, because `landable` has no error channel to carry a budget on."
            Action =
              "Do not merge, and do not treat it as a red. An unreachable answer is not a negative one. Look at why the read failed — check `budget` if you suspect a rate limit — and ask again." }
          { Code = ExitCode.toInt ExitCode.Error
            Name = ""
            Meaning =
              "REFUSED — the engine rejected your INPUT before it ever looked at the PR: no `--repo` (so which repo the PR is in is undefined), a ref that is not a PR number, or the wrong number of arguments. It is not a verdict about the PR, and no word is printed."
            Action = "Read the message and fix the call. Not retryable — it will refuse identically." }
          { Code = ExitCode.toInt ExitCode.Defect
            Name = ""
            Meaning =
              "The ENGINE broke — an unhandled defect, with a stack trace. Its own code, so a broken engine cannot hide behind a stream of what look like bad inputs."
            Action = "Report it. Do not retry, and do not merge a PR you have no verdict on." } ]

    // ---- release's column precedence ---------------------------------------------------------------
    // #1099, and it is #889/#900 a THIRD time: `release`'s column semantics is the single most-repaired
    // behaviour in this org — #331/#354/#531/#867/#911/#914/#921, seven issues, one behaviour — and
    // every repair edited a hand copy while the engine's `Client.unclaimColumn` was (eventually) right.
    // #1059 moved the org's four RULE-shaped restatements into `driverRules` and could NOT move this
    // one: it is not a `Rule` but a PRECEDENCE with a documented end state, the same shape as the two
    // exit-code tables, so it stayed authored prose in `pnext-item`'s "Abandoning an item" section.
    //
    // The rows are ordered as `Client.release` EVALUATES them: an explicit `--status` beats everything
    // (#867/#914), then the recorded restore, then the `Ready` fallback, then the preserve arms, then
    // the fail-closed case a chosen write shares with an unreadable column. The `Stdout` is the load-
    // bearing field: `release` exits 0 even when the write does not land, so the exit code cannot
    // confirm a park and the stdout LINE is the tell — a preserve names no column it set, a bare
    // `released <ref>` means nothing landed and stderr says why (#331/#914).
    let releaseColumns: ReleaseColumnDoc list =
        [ { Condition =
              "You pass an explicit `--status <col>`. It BEATS the recorded restore and the `Ready` fallback alike — the caller naming the deliberate end state (#867/#914), which is why parking an item into a column is `release <n> --status <col>`."
            EndState = "`<col>` — the column you named."
            Writes = true
            Stdout = "released <ref> → <col>" }
          { Condition =
              "No `--status`; the live column is still the `In progress` the claim wrote, and the marker recorded NO other column (or recorded `In progress` — the same footprint written twice)."
            EndState = "`Ready` — the fallback for a claim with nothing to restore (#481)."
            Writes = true
            Stdout = "released <ref> → Ready" }
          { Condition =
              "No `--status`; the live column is the claim's own `In progress`, and the marker recorded a DIFFERENT column at claim time — what the claim overwrote."
            EndState = "the recorded column, RESTORED — a `Backlog` item returns to `Backlog`, not `Ready` (#481)."
            Writes = true
            Stdout = "released <ref> → <recorded>" }
          { Condition =
              "No `--status`; the live column is anything OTHER than the claim's `In progress` — it was chosen DURING the lease (you parked it `Blocked`, say). `reap` asks the same question, so a lapsed lease does not revert it either (#331/#911)."
            EndState = "that column, PRESERVED — the write is skipped, and the absence of the write is what says the column was nobody's to change."
            Writes = false
            Stdout = "released <ref> (column left at <col>)" }
          { Condition = "No `--status`; the item has no `Status` set, or is not on this board — so there is no column to reset."
            EndState = "nothing to set."
            Writes = false
            Stdout = "released <ref> (no column to reset — not on this board, or no Status set)" }
          { Condition =
              "The live column could not be READ (unresolvable board, or a transient failure), OR a column `release` chose to write was DEFERRED on an exhausted budget or FAILED. A column it cannot read is one it will not overwrite (#266/#331)."
            EndState =
              "UNCHANGED — left exactly as it is; the lock is dropped regardless. The BARE line — no `→`, no `(...)` — is the tell, and stderr immediately above it names the repair."
            Writes = false
            Stdout = "released <ref>" } ]

    // ---- the rules ---------------------------------------------------------------------------------

    let touchSetGrammar: Rule =
        { Id = "touch-set-grammar"
          Title = "The touch-set grammar — it is NOT a glob language"
          Statement = Schedulability.TouchSetGrammar
          Because =
            "#273. Four hand-copied forms of the unmatchable-token predicate existed across two engines. A token that matches no file conflicts with nothing — so an item declaring only such tokens reserves NOTHING, clears every overlap check, and the lock succeeds under exactly the conditions it exists to prevent." }

    let touchSetDeclaration: Rule =
        { Id = "touch-set-declaration"
          Title = "`Paths:` is a declaration, and a fenced one is a QUOTATION"
          Statement =
            "Declare the touch-set as a `Paths:` line at up to three leading spaces. A `Paths:` line INSIDE a fenced code block is a quotation of the grammar, not a use of it — the protocol docs quote it constantly. `Paths: none` is a SENTINEL meaning \"this item deliberately has no touch-set\", and it is not the same fact as having forgotten one."
          Because =
            "#277 (a fenced line read as a declaration would let a doc reserve files) and #496 (an epic and a forgotten touch-set rendered identically, so no gate could be written at all — nine items of real work went invisible, and the surface whose job is board health reported `0 error(s)` over a dead queue)." }

    let blockerResolution: Rule =
        { Id = "blocker-resolution"
          Title = "A MERGED blocker is RESOLVED; an unreadable one BLOCKS"
          Statement =
            "`Blocked by` is a Projects v2 board FIELD, not a body line — the same medium split as `Paths:` and its own fence rule, in reverse: `Paths:` lives in the body and a `Blocked by` FIELD is the only place this dependency is recorded. A `Blocked by:` line written into the issue BODY is inert: nothing that clears a blocker reads the body, so it looks like a declaration and does nothing. Write the edge with `set-field <ref> \"Blocked by\" <ref>`. Once the edge is on the field: `Blocked by` clears on CLOSED **or MERGED**. It does not clear on OPEN, on a blocker whose state could not be read (unverifiable), or on prose that is not an issue ref at all (unparseable) — all three BLOCK."
          Because =
            "#476: `Blocked by` may name a PULL REQUEST, whose state is OPEN | CLOSED | MERGED. A rule clearing only on CLOSED unblocks when the blocking work is ABANDONED and blocks forever once it is FINISHED — the gate opened precisely when the work was thrown away and shut precisely when it was done. And #266/#421: \"I could not look\" is not \"I looked and it is fine\"; prose in a dependency field is not permission. And .github#1933: two agents independently read a `Blocked by:` BODY line, found no FIELD edge, and concluded there was none — one filed a false defect (.github#1931) and withheld from promoting a row only because a third worker caught the contradiction by hand. Nothing in the operator-facing docs said which medium held the fact; only the `.fsi` comments did, and filers do not open those." }

    let checkOrder: Rule =
        { Id = "check-order"
          Title = "Blockers are checked BEFORE the touch-set"
          Statement =
            "The scheduler asks, in order: is the issue closed? is its Status one we hand out? is it BLOCKED? is its touch-set usable? is it HELD? does it overlap work in flight? The first answer that is not \"no\" is the verdict, and it is the one sentence the worker reads."
          Because =
            "ADR-0038. A blocked item cannot be started whatever its touch-set says, so reporting \"no `Paths:` declared\" sends a worker to fix something that leaves them exactly where they were. And blockers are FREE — they are board facts already in the scan — where a touch-set costs a body READ per item, on the budget that dies first (#418). That is why bash never fetched a blocked item's body, and how an unreadable one could silently cease to exist." }

    let claimLock: Rule =
        { Id = "claim-lock"
          Title = "The claim lock is a comment-order CAS, and the ASSIGNEE cannot hold it"
          Statement =
            "A claim is an `fsgg:claim` marker COMMENT, and the lowest live marker id wins. GitHub issues comment ids from one server-side sequence, so \"lowest live marker\" is a total order every racer observes identically. The GitHub ASSIGNEE cannot be the lock, because N agents share one account. That total order is over MARKERS, and it separates WORKERS only while their ids are DISTINCT: an id two workers share is an id this lock cannot separate, and `release`, `heartbeat`, `say` and `inbox` then act on one another's claims. So a worker id is MINTED, never chosen — a worker asked to pick one is not a random source."
          Because =
            "ADR-0027, and #419 for the distinctness half: agents asked to invent an id converge on the same corner of the name space, and this board carried FOUR `finch-*` workers at once — every one of them lifted from the single example id that then sat in the recipe. The attractor is the WORD, not the suffix, which is why the remedy is a mint rather than a reminder to be careful, and why #532/#551/#570 had to remove the pasteable id from the docs twice by hand before a gate asserted it. The lock lives on REST, and the invariant it serves — a lock may never live on the budget that dies first — is unamended. What inverted is WHICH budget that is, so this rule no longer asserts a standing answer. #418 measured GraphQL dying first (five workers looping `take` drained 5,000 pt/hr in ~15 minutes), and REST was chosen as the survivor. #895 measured the reverse, twice on 2026-07-16: REST core hit 0/5,000 and took `claim`/`take`/`who` down with it, while GraphQL stayed healthy through both — 3,639/5,000 at the first of them. This rule used to state \"GraphQL is the first budget to die\" as standing fact, and that premise is what kept regenerating the doctrine that caused the inversion — a recipe steering every worker's reads onto REST to save GraphQL points, on one shared account, spending the lock's own budget to save 7 points of 5,000. #895 decided (2026-07-17) that the lock STAYS and the DOCTRINE moves (#968): REST is metered per request and cannot be batched, so under fan-out it is structurally the scarcer budget with no lever to pull, where GraphQL batches 100 nodes to a query. Discretionary reads belong on GraphQL; REST carries the lock, which has no alternative." }

    let leaseRule: Rule =
        { Id = "claim-lease"
          Title = "The lease is a WINDOW, and an unknown age says so"
          Statement =
            "A claim's lease is 120 minutes by default (`FSGG_CLAIM_LEASE_MIN`), and `heartbeat` renews it only while it is LIVE. Past it the claim is REAPABLE — not free: only `reap` may break a lock, and an item's touch-set stays reserved until it does. An EXPIRED lease cannot be renewed in place; the holder must re-claim. Evidence that the work is alive — an open `item/<n>-*` PR — withholds the item from `take` and REFUSES a `reap`, but it does not revive the lease. A claim whose age cannot be read reports `lease unknown`, never a window."
          Because =
            "#428 (\"nothing schedulable\" and \"queued behind a claim held by <w>, lease frees in ~96m\" are the same fact and two completely different operator instructions — the first reads as an empty queue and sends a worker home) and #440/#488 (inventing \"frees in ~120m\" from a missing timestamp is a confident-but-unfounded sentence, which is the class both were closed for). And the lease is a TIMER, which is why it never decides alone: it cannot see a REST outage, and `heartbeat` is REST, so an outage on the lock's budget spends a lease nobody can renew and silently reads as abandonment (#976, ratifying that the fleet stops there rather than making the clock outage-aware). What answers instead is evidence — an open `item/<n>-*` PR (#581), or a liveness probe that failed and therefore fails closed (#266). Expiry is EVIDENCE of abandonment, never proof." }

    let failClosed: Rule =
        { Id = "fail-closed"
          Title = "A read that did not happen may never render as a confident answer"
          Statement =
            "An error, an empty result, and a legitimate \"no\" are three different facts. A failed board scan is not an empty board; a failed marker read is not an unheld item; an unread issue body is not an undeclared touch-set. Every one of them fails CLOSED and says which it was."
          Because =
            "Epic #266, which has 51 children. #461: a failed claim scan read as \"nothing is claimed\", so `take` handed a held item to a second worker. #344: a rate-limited scan exited 0 with no verdict, and a worker read \"nothing to do\" off a board it never managed to read." }

    /// Every rule, in the order a projection presents them.
    let rules: Rule list =
        [ touchSetDeclaration
          touchSetGrammar
          checkOrder
          blockerResolution
          claimLock
          leaseRule
          failClosed ]

    /// The rules a worker FILING an item must satisfy — the subset `cross-repo-coordination` restates
    /// (#889).
    ///
    /// A SUBSET OF `rules`, NEVER A SECOND LIST. Every member is the same value the canonical list holds,
    /// so the two cannot disagree about what a rule SAYS — which is the only thing #731's mechanism was
    /// built to guarantee. `ProtocolTests` pins the containment, because the failure this invites is a
    /// rule authored straight into here and reaching a projection while the canonical doc never states it.
    ///
    /// WHY A SUBSET AT ALL, rather than emitting `rules`. `cross-repo-coordination` files work into
    /// ANOTHER repo; it does not schedule, claim, or hold a lease, and it links to
    /// `intra-repo-parallel-work` for all of that. Emitting the full block would bury the four lines it
    /// needs under `check-order`, `claim-lock`, `claim-lease` and `fail-closed` — seventy lines of
    /// scheduler internals a filer does not act on. That is the trap #916 named: a region carries what its
    /// document is FOR, which is why the kinds exist.
    ///
    /// What a FILER acts on: how to declare a touch-set (`touch-set-declaration`), what the grammar will
    /// actually accept (`touch-set-grammar`), and what `Blocked by` does once the edge is recorded
    /// (`blocker-resolution`). Nothing else on this list is a decision they make.
    let filingRules: Rule list =
        [ touchSetDeclaration; touchSetGrammar; blockerResolution ]

    /// The rules a RECONCILER must satisfy — the subset `check-board` restates (#889).
    ///
    /// A SUBSET OF `rules`, on exactly the terms `filingRules` is: same values, containment pinned, never
    /// a second list. See that list's note for why the containment is the whole assertion.
    ///
    /// WHY THESE THREE. `check-board` answers two questions — "is the board in sync with the issues?" and
    /// "do the recorded blockers still hold?" — and its own finding codes (`BLOCKER-CLEARED`,
    /// `UNDECLARED-PATHS`, …) are PROCEDURE, not protocol: they are decisions that skill makes, and they
    /// stay authored. What it may not restate is the protocol those decisions read:
    ///
    /// - `blocker-resolution` — §3 IS this rule. A reconciler that clears on CLOSED but not MERGED
    ///   unblocks abandoned work and blocks finished work (#476).
    /// - `fail-closed` — the reconciler's worst output is a FALSE CLEAN: a snapshot it could not read,
    ///   reported as a board with nothing wrong. It buys confidence in the projection instead of
    ///   correcting it, which is worse than not running (#266).
    /// - `touch-set-declaration` — `UNDECLARED-PATHS` turns on the fence rule and the `Paths: none`
    ///   sentinel. A hand-rolled `^Paths:` grep is a fourth parser of a grammar that has one, and it is
    ///   the loosest: it reads a QUOTED line as a declaration (#277) and a deliberate epic as a
    ///   forgotten touch-set (#496).
    ///
    /// NOT `claim-lock` or `claim-lease`, though this skill reports `STALE-CLAIM` and
    /// `UNCLAIMED-IN-PROGRESS`: it does not TAKE the lock or hold a lease — it reads `who` and delegates
    /// to `reap`. NOT `check-order`, which is the scheduler's internal order and not a fact a reconciler
    /// acts on. NOT `touch-set-grammar`: this skill never authors a `Paths:` line — `UNDECLARED-PATHS` is
    /// report-only precisely because the fix is an ISSUE edit, and it never writes to an issue.
    let reconcileRules: Rule list =
        [ touchSetDeclaration; blockerResolution; failClosed ]

    /// The rules a worker DRIVING an item must satisfy — the subset `pnext-item` restates (#889/#1059).
    ///
    /// A SUBSET OF `rules`, on exactly the terms `filingRules` is: same values, containment pinned, never a
    /// second list. See that list's note for why the containment is the whole assertion.
    ///
    /// WHY THESE FOUR — and this list is the one whose subset is least obvious, because a driver does more
    /// of the protocol than any other role. It files, it schedules, it claims, it holds a lease. So the
    /// naive answer is "project `rules`", and `RuleSubsetTests` refuses that outright: a kind that covers
    /// everything is not a kind. The question that actually cuts is not "what does a driver TOUCH" but
    /// "what does a driver DECIDE" — the rest is the engine's, and a driver reads its verdict rather than
    /// applying the rule themselves.
    ///
    /// - `claim-lock` — §0 IS this rule. A driver MINTS an id and takes a lock with it, and the two facts
    ///   that make that safe are the total order and the distinctness of the id it is over (#419).
    /// - `claim-lease` — §3. A driver HOLDS a lease and must heartbeat it; that an EXPIRED one cannot be
    ///   renewed in place is the fact §3 and §6 disagreed about for as long as both were prose (#1059).
    /// - `touch-set-declaration` — §3. A driver AUTHORS a `Paths:` line, with `widen`, mid-flight.
    /// - `touch-set-grammar` — §3. A driver's `widen` is refused by this grammar, so it is the one role
    ///   that meets it as an error message rather than a description.
    ///
    /// NOT `check-order`, for `reconcileRules`' reason exactly: it is the scheduler's internal order, and
    /// `take` performs it FOR the driver. A worker acts on the one sentence it prints, never on the order
    /// that produced it. NOT `blocker-resolution`: a driver RECORDS an edge (§4) but never resolves one —
    /// that is `take`'s question on the way in and a reconciler's on the way back (`reconcileRules`), and a
    /// driver who applied it by hand would be deciding their own item is startable. NOT `fail-closed`: it
    /// is the engine's discipline about its own reads, and where a driver does meet it — `landable`'s
    /// UNKNOWN, `take`'s read failure — the exit-code regions already carry it, stated as the code to act
    /// on rather than as a principle to apply.
    let driverRules: Rule list =
        [ touchSetDeclaration; touchSetGrammar; claimLock; leaseRule ]

    // ================================================================================================
    // THE INVENTORY (#1027) — which facts the document states, and in what order.
    // ================================================================================================

    /// One section of the facts document: a key, and the facts it states under that key.
    ///
    /// THE CASES ARE SHAPES, NOT FACTS. There is one case per JSON shape the writer knows how to emit —
    /// not one per key — which is the whole distinction this type exists to draw. `rules`, `filingRules`
    /// and `reconcileRules` are three keys of ONE shape, and `Snapshot` cannot tell them apart: it is
    /// handed a key and a list, and writes what it is given. So a new Core-owned fact key is an edit to
    /// `factsDocument` and nothing else, and a new fact SHAPE — rare, and genuinely a writer's concern —
    /// is the only thing that reaches the Cli.
    ///
    /// EVERY CASE CARRIES ITS KEY, including the two that have exactly one member today. The asymmetry
    /// is tempting — `Verdicts of VerdictDoc list` needs no key to be unambiguous — and it would put the
    /// STRING `"verdicts"` back in `Snapshot.fs`, which is to say: half the inventory in the Cli again,
    /// and no way to read the document's key list off this file. The inventory is either here or it is
    /// not.
    type FactSection =
        | Rules of key: string * Rule list
        | Verdicts of key: string * VerdictDoc list
        | BlockerStates of key: string * BlockerStateDoc list
        | BoardStatuses of key: string * BoardStatusDoc list
        | ExitCodes of key: string * ExitCodeDoc list
        | ReleaseColumns of key: string * ReleaseColumnDoc list
        /// The snapshot document's SHAPE. The only case carrying a scalar beside its list, because the
        /// shape IS a schema string plus its keys — and a schema emitted as a one-member `keys` entry
        /// would be a lie about what it is. See `snapshotSchema` in Protocol.fsi for the ownership call.
        | SnapshotShape of key: string * schema: string * keys: SnapshotKeyDoc list

    /// The facts document's schema version — a fact about the document's SHAPE, so it lives with the
    /// document rather than in the writer that renders it (#1027).
    ///
    /// /2 `takeExitCodes` (#889) · /3 `landableExitCodes` (#900) · /4 `filingRules` (#889) ·
    /// /5 `reconcileRules` (#889) · /6 `blockerStates` (#889) · /8 `snapshotDocument` (#889/#1058) ·
    /// /9 `driverRules` (#889/#1059) · /10 `releaseColumns` (#889/#1099).
    ///
    /// Each bump is additive for a reader that ignores unknown members, and the number is bumped anyway:
    /// it says what the surface IS, not merely whether an old reader survives it.
    ///
    /// A NUMBER A HUMAN REMEMBERS TO INCREMENT IS A NUMBER THAT DRIFTS, so nothing here relies on the
    /// remembering: `ProtocolTests` pins this string against `factsDocument`'s key list, and a key added
    /// without a bump reds that test. The pin cannot DERIVE the number — what a version increment means
    /// is a judgement, and a schema computed from its own content would bump on a key RENAME and stay put
    /// on a semantic change. So the test forces the decision rather than making it.
    ///
    /// NOT `[<Literal>]`, though its predecessor was: a literal must state its VALUE in the signature
    /// file too (FS0034), and nothing consumes this at compile time. The old one could afford the
    /// attribute because it was `private` and had no signature entry to keep in step.
    let factsSchema = "fsgg.coord.protocol/10"

    /// The snapshot document's schema string — the `schema` member `Scan.snapshot` writes and
    /// `Snapshot.parse` refuses a document without.
    ///
    /// THIS IS A THIRD COPY, AND SAYING SO IS THE POINT (#865/#916 trap 1). `Scan.fs` writes the string,
    /// `Snapshot.fs` reads it, and neither imports it from here — so this module states a fact it does
    /// not itself render, which is exactly the cost #1058's ownership call accepted rather than hid. The
    /// alternative — own it where it is rendered and project it from there — is the stricter reading and
    /// was rejected on cost, not on principle.
    ///
    /// A DECISION LIKE THAT IS ONLY HONEST IF A TEST HOLDS IT. `ProtocolTests` pins this string against
    /// `Scan`'s and `Snapshot`'s, so the drift the call accepts reds a test rather than rotting a doc.
    /// Do not "tidy" the copies away without moving the ownership; the pin is what makes three copies
    /// safe, and deleting it makes them three copies again.
    let snapshotSchema = "fsgg.coord.snapshot/1"

    /// THE SNAPSHOT DOCUMENT'S TOP-LEVEL KEYS, in the order `Scan.snapshot` writes them.
    ///
    /// ORDER IS THE WRITER'S, NOT THE PROSE'S — and the literal this replaced had it wrong TWICE. It
    /// spelled `leaseMinutes` before `limit`, and `inFlight` before `items`; the writer emits `limit`
    /// first and `items` first. Nothing caught either, because nothing compared them.
    ///
    /// The second one was inherited straight into the FIRST DRAFT OF THIS LIST, by an author reading the
    /// literal — and `ScanRoundTripTests` caught it on the first run. That is the argument for this whole
    /// change, made by the change itself: a shape stated once and pinned to its writer, or a shape
    /// re-typed by whoever is looking at the old copy.
    ///
    /// `Reconciled` IS THE LOAD-BEARING COLUMN. A reader of this table is a `jq` filter in `check-board`,
    /// and the question it needs answered is not "what keys exist" but "which of them may I select on".
    /// `limit` and `leaseMinutes` are the SCAN'S OWN PARAMETERS echoed back — a pass that reconciles
    /// against them reconciles against its own request, and would report drift that is its own flag.
    let snapshotKeys: SnapshotKeyDoc list =
        [ { Key = "schema"
            Reconciled = false
            Meaning =
              "The document's contract, `fsgg.coord.snapshot/1`. `Snapshot.parse` REFUSES a document \
               without it rather than defaulting — a malformed snapshot is an error, never a default." }
          { Key = "allowBacklog"
            Reconciled = false
            Meaning =
              "Whether the scan was asked to include `Backlog`. The scan's own parameter, echoed back: \
               `lanes` reads it from HERE rather than taking its own flag (#991), which is why it is on \
               the document at all." }
          { Key = "limit"
            Reconciled = false
            Meaning = "The `-n` cap the scan was asked for, or `null` for uncapped. The scan's parameter, not a board fact." }
          { Key = "leaseMinutes"
            Reconciled = false
            Meaning =
              "The lease window the scan resolved staleness against (`FSGG_CLAIM_LEASE_MIN`, default \
               120). The scan's parameter. The prose this replaced hardcoded `90`, which was neither \
               the default nor a fact — the clearest evidence a reader cannot tell an example from a \
               contract when both are hand-typed." }
          { Key = "items"
            Reconciled = true
            Meaning =
              "The board rows — THE reconcilable key, and the only one. Each carries `owner`, `repo`, \
               `number`, `status`, `state`, `body` and `blockers`. Named `items` on the wire, not \
               `candidates`: that is what the parser reads." }
          { Key = "inFlight"
            Reconciled = false
            Meaning =
              "What live claims already reserve, each naming its HOLDER. A scheduler's input, not a \
               column to reconcile: `check-board` acts on the MARKER through `who`, which carries the \
               lease state this does not." } ]

    /// THE INVENTORY — every fact the document states, under the key it states it, in document order.
    ///
    /// This list WAS `Snapshot.renderFacts`'s parameter list: one positional parameter per fact kind,
    /// hand-maintained in the Cli, across three files, for facts this module owns outright. So the
    /// inventory of facts was a second copy of this module's, hand-maintained, in the file whose whole
    /// purpose is to end hand-maintained copies (#1027) — `rules` was emitted rather than authored, and
    /// the LIST OF WHAT GETS EMITTED was authored. `render_filing_rules` in `scripts/generate-projections`
    /// refuses to let the generator re-derive subset membership in a `jq` filter, for that reason exactly;
    /// `Snapshot.fs` made the same argument, in the schema note this change replaced, and did not apply it
    /// to itself.
    ///
    /// THE COST WAS A CHOKEPOINT, not an untidiness. Adding one Core-owned key took five edits across
    /// four files, four of them pure ceremony — and one of them landed in `Snapshot.fs`, so every
    /// remaining slice of #889 declared that file and serialised behind whoever held it. That is #428's
    /// shape one file over, and #428 was not fixed by sequencing the items behind it.
    ///
    /// ORDER IS THE DOCUMENT'S ORDER. The writer folds this list in sequence, so the key order below IS
    /// the JSON's key order — there is nowhere else it could be stated, and no second list to keep in
    /// step with this one.
    let factsDocument: FactSection list =
        [ Rules("rules", rules)
          Rules("filingRules", filingRules)
          Rules("reconcileRules", reconcileRules)
          Rules("driverRules", driverRules)
          Verdicts("verdicts", verdicts)
          BlockerStates("blockerStates", blockerStates)
          BoardStatuses("boardStatuses", boardStatuses)
          ExitCodes("takeExitCodes", takeExitCodes)
          ExitCodes("landableExitCodes", landableExitCodes)
          ReleaseColumns("releaseColumns", releaseColumns)
          SnapshotShape("snapshotDocument", snapshotSchema, snapshotKeys) ]
