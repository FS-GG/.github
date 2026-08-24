namespace FS.GG.Coord.Cli

module LintApplication =

    open FS.GG.Coord
    open FS.GG.Coord.Types
    open FS.GG.Coord.GitHub

    type EpicFinding =
        { Code: string
          Severity: string
          Detail: string }

    type Summary =
        { Errors: int
          Notes: int
          Fails: bool }

    let badTouchSetDetail (status: string) (usability: TouchSet.Usability) : string option =
        match usability with
        | TouchSet.Usable -> None
        | TouchSet.AllUnmatchable bad ->
            let bad = String.concat ", " bad

            Some
                $"%s{status}, and EVERY declared `Paths:` token is unmatchable: %s{bad}. A token that matches no file conflicts with nothing, so `batch` refuses it and no worker can ever pick this up — the item is as dead as one with no touch-set at all. Not a glob language: exact paths, directory prefixes, and a TRAILING `/**` or `/*`."
        | TouchSet.SomeUnmatchable bad ->
            let bad = String.concat ", " bad

            Some
                $"%s{status}, and at least one of its `Paths:` tokens is unmatchable: %s{bad}. This is WORSE than every token being so: the item looks declared and its other tokens reserve work, but an unmatchable token silently reserves NOTHING — the files it names are invisible to every other worker's overlap check, so two workers can be handed them at once. Spell the path(s) out — not a glob language: exact paths, directory prefixes, and a TRAILING `/**` or `/*`."

    // ---- the CLASS vocabulary, READ rather than restated (.github#1651 AC5) ------------------------
    //
    // Everything below spells the three legal values EXACTLY ZERO times. `Class.legalClasses` is the
    // union by reflection and `Types.itemClassWireName` is the one function that renders a case to a
    // word; the only thing this module adds is the GLOSS, which the union genuinely does not carry —
    // and that is a TOTAL match, so a fourth `ItemClass` fails the build here rather than reaching a
    // filer's diagnostic unexplained. It is `Protocol.meaning`'s shape, for `Protocol.meaning`'s reason.

    let private gloss =
        function
        | Defect -> "something is broken now"
        | Hardening -> "nothing is broken; this removes a way it could break"
        | Decision -> "a human must choose first"

    /// The menu a filer picks from: every legal value, spelled as the body line they must write.
    let classMenu: string =
        Class.legalClasses
        |> List.map (fun c -> $"`Class: %s{itemClassWireName c}` (%s{gloss c})")
        |> String.concat ", "

    /// The sentence `lint`'s `CLASS-INVALID` and `add`'s refusal BOTH render, spelled once.
    ///
    /// `None` when every `Class:` line in the body resolves — including when there is no line at all,
    /// which is `CLASS-UNSET`'s business and not this one's.
    let outOfVocabularyClass (body: string) : string option =
        match Class.unrecognised body with
        | [] -> None
        | bad ->
            let quoted =
                bad
                |> List.map (fun v -> if v = "" then "(empty)" else $"\"%s{v}\"")
                |> String.concat ", "

            // QUOTE THE OFFENDING VALUE BACK AND LIST THE LEGAL SET (AC3). Both measured authors believed
            // they were classing the row correctly; `docs` and `enhancement` are exactly the words a
            // reasonable person picks from the general vocabulary of issue triage when nothing says the
            // set is closed. A diagnostic that said only "invalid" would send them to the ADR to find out
            // what it wanted, which is the cost this rule exists to remove.
            Some
                $"its text DOES declare a `Class:` line and the value is not one this engine speaks: %s{quoted}. The vocabulary is CLOSED and has exactly these values — %s{classMenu}. Anything else is not a class at all: the row reads as untriaged, counts as a POSSIBLE defect under ADR-0066's stopping rule, and blocks a clean termination read for the whole board until a human reads the body (.github#1651). Value case and surrounding space are normalised, so `Class: Defect` and `class: defect` are fine; an unlisted WORD is not."

    /// The CLASS axis's whole verdict over one candidate row: `Some(code, detail)`, or `None` when the
    /// row's own text classes it.
    ///
    /// **THE TWO CAUSES ARE SEPARATE FINDINGS, AND THE INVALID ONE WINS.** They were one rule, and a row
    /// carrying `Class: docs` was told it *"records no `Class:`"* — false, and false in the direction
    /// that costs the most: the reader goes looking for a missing line, finds a present one, and has to
    /// work out unaided that the value is the fault. Emitting BOTH would be the same collapse wearing two
    /// hats, so an unrecognised value suppresses the absent-value rule: a body that wrote a line did not
    /// omit one.
    ///
    /// The same shape as `.github#1625` one layer down — a diagnostic collapsing two causes into one
    /// message and naming the wrong one. That item owns the projection side (`CLASS-PROJECTION-LAG`
    /// cannot tell "this board has no `Class` field" from "this row is unclassed"); this owns the body
    /// side. They compose and neither supersedes the other: #1625's own AC2 requires that `CLASS-UNSET`
    /// keep firing on a field-less board precisely BECAUSE this rule reads the item's text and not the
    /// column, and that stays true of `CLASS-INVALID` for the same reason.
    let classVerdict (status: string) (title: string) (body: string) : (string * string) option =
        match outOfVocabularyClass body with
        | Some detail -> Some("CLASS-INVALID", $"%s{status}, and %s{detail}")
        | None ->
            if (Class.derive title body).IsNone then
                Some(
                    "CLASS-UNSET",
                    $"%s{status} but its text records no `Class:` — so a driver cannot tell a live defect from deliberate hardening, and a burn-down keying on severity cannot terminate (#1588). Declare one body line: %s{classMenu}. A `[decision]` title prefix or a `Blocked on: human/decision` sentinel already derives `decision` — no second line needed."
                )
            else
                None

    // ---- the STATUS axis (.github#1823 AC5) --------------------------------------------------------
    //
    // `CLASS-UNSET`'s exact sibling, and it is here for the reason that item records: `add` used to leave
    // `Status` unset, fourteen rows were filed that way in one day, and **every instance was found by
    // accident** — a driver reading `batch` output for an unrelated reason. Nothing reported any of them.
    //
    // The two rules answer the two questions the board is asked about a row, and they are not the same
    // question: `CLASS-UNSET` asks whether it is TRIAGED, this asks whether it is VISIBLE AT ALL. A row
    // with no `Status` is not merely untriaged — `Schedulability` refuses it outright ("no Status on the
    // board: invisible to every scheduler, and nobody set it"), so no lane, no `batch`, no `take`, and no
    // driver report will ever mention it again.
    //
    // IT REPORTS; IT DOES NOT PREVENT, and it is therefore a COMPLEMENT to the `add` default and never a
    // substitute for it. `add`'s default stops the row being filed invisible in the first place; this
    // catches the ones already there, and the ones a future write path drops the same way. All fourteen
    // instances went unreported precisely because nothing asked this question.
    //
    // ITS POPULATION IS NOT `isSchedulableCandidate`. That predicate is `Ready || Backlog`, and the whole
    // subject here is a row that is NEITHER — an unset column cannot be inside a set of columns. So this
    // reads the one condition it is about: OPEN, on the board, with `NoStatus`. `Done`, `Blocked`,
    // `In progress` and `In review` rows are all deliberately silent — they have a column.

    /// `lint`'s STATUS verdict for one row: `Some detail` when an OPEN row on the board carries no
    /// `Status` this engine can read, `None` otherwise.
    ///
    /// Severity is the caller's to supply, on `badTouchSetDetail`'s terms.
    ///
    /// **THE SENTENCE SAYS "NONE THIS ENGINE CAN READ", NOT "NONE", AND THE DIFFERENCE IS LOAD-BEARING.**
    /// `Scan.boardStatusOf` ends `| _ -> NoStatus`, so a column outside the six names it was taught
    /// collapses into the same case as an empty one — `Snapshot.boardStatus` refuses exactly that
    /// coercion, loudly, and this rule reads the surface that performs it. A rule that reported
    /// "no `Status` at all" would then be stating something FALSE about a row that has one, at `error`
    /// severity, the day somebody adds a column to the board. So the finding names both readings and
    /// carries both remedies. It stays one finding at one severity because the CONSEQUENCE is identical
    /// and is what the rule is actually about: `Schedulability` refuses the row either way, so no lane,
    /// no `batch`, no `take`, and no driver report will ever mention it again.
    let statusVerdict (state: IssueState) (status: BoardStatus) : string option =
        if state = IssueState.Open && status = BoardStatus.NoStatus then
            Some
                "OPEN and on the board with no `Status` THIS ENGINE CAN READ — invisible to EVERY scheduler, so `batch`, `take` and every driver report will pass over it silently and it can sit here forever (.github#1823: fourteen rows were filed this way in one day and every one was found by accident). Two readings, both this: the column is genuinely EMPTY — give it one, `Backlog` if it is untriaged, which is what `add` now defaults to (`scripts/fsgg-coord set-field <issue> Status Backlog`) — or the board grew a column this engine was never taught, which `Scan` folds into the same case (`scripts/fsgg-coord bootstrap --refresh`, then teach `Scan.boardStatusOf` the name). Read the row on the board to tell which."
        else
            None

    /// `lint`'s Severity verdict. `Unset` is representable so unread/untriaged rows rank last, but it is
    /// not a completed triage decision and remains an error on every open, non-Done row.
    let severityVerdict (state: IssueState) (status: BoardStatus) (severity: Severity) : string option =
        if state = IssueState.Open && status <> BoardStatus.Done && severity = Severity.Unset then
            Some
                "Severity is `Unset` — this row ranks last until a human triages it. Set the board's `Severity` field to one of `Critical`, `High`, `Medium`, or `Low` from evidence in the row's own text (.github#1901)."
        else
            None

    let blockedNoReasonVerdict state status (blockedBy: string) (body: string) : string option =
        if state = IssueState.Open && status = BoardStatus.Blocked && System.String.IsNullOrWhiteSpace blockedBy && (HumanBlock.parse body).IsNone then
            Some "Status is Blocked with an empty Blocked by field and no human-block sentinel; record the machine blocker or the human reason."
        else
            None

    // Diagnose a `Blocked by:` BODY projection without ever treating it as dependency authority
    // (.github#2907). Fenced examples are quotations and ignored. An agreeing canonical projection is
    // useful human-readable text and stays green; empty, divergent, duplicated, or invalid projections
    // are actionable because operators otherwise read an inert line as the live edge set.
    let blockedByBodyProjectionVerdict
        (owner: string)
        (repo: string)
        (blockedBy: string)
        (body: string)
        : string option =

        let mutable fence: string option = None
        let projections = ResizeArray<string>()

        for line in body.Replace("\r\n", "\n").Split('\n') do
            let trimmed = line.TrimStart()
            let marker =
                if trimmed.StartsWith("```") then Some "```"
                elif trimmed.StartsWith("~~~") then Some "~~~"
                else None

            match fence, marker with
            | None, Some opened -> fence <- Some opened
            | Some opened, Some closed when opened = closed -> fence <- None
            | None, None ->
                let prefix = "Blocked by:"
                if trimmed.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase) then
                    projections.Add(trimmed.Substring(prefix.Length).Trim())
            | _ -> ()

        let render value = if System.String.IsNullOrWhiteSpace value then "<empty>" else value
        let canonicalSet (value: string) =
            value.Split(',') |> Array.map (fun part -> part.Trim()) |> Set.ofArray

        match List.ofSeq projections with
        | [] -> None
        | _ :: _ :: _ ->
            Some
                $"body declares %d{projections.Count} unfenced `Blocked by:` lines, but the Projects-v2 `Blocked by` field is the sole authority; keep exactly one generated human-readable projection."
        | [ raw ] ->
            match Blockers.canonicalizeBlockedBy owner repo raw with
            | Error _
            | Ok None ->
                Some
                    $"body declares an invalid or empty `Blocked by:` projection (`%s{render raw}`); it is inert because only the Projects-v2 field creates dependency edges."
            | Ok(Some bodyCanonical) ->
                match Blockers.canonicalizeBlockedBy owner repo blockedBy with
                | Ok(Some fieldCanonical) when canonicalSet fieldCanonical = canonicalSet bodyCanonical -> None
                | Ok None ->
                    Some
                        $"body says `Blocked by: %s{bodyCanonical}` while the authoritative Projects-v2 field is empty; the body line is inert and creates no dependency edge."
                | Ok(Some fieldCanonical) ->
                    Some
                        $"body projects `Blocked by: %s{bodyCanonical}` but the authoritative Projects-v2 field is `%s{fieldCanonical}`; regenerate or remove the divergent body projection."
                | Error _ ->
                    Some
                        $"body projects `Blocked by: %s{bodyCanonical}` but the authoritative Projects-v2 field is not a canonical issue-ref set (`%s{render blockedBy}`); repair the field authority before trusting the projection."

    let humanParkResolvedVerdict state status (blockers: Blocker list) (body: string) : string option =
        match HumanBlock.parse body with
        | Some AwaitingHumanDecision when state = IssueState.Open && status = BoardStatus.Blocked && not (List.isEmpty blockers) && (blockers |> List.forall Blockers.isResolved) ->
            Some "all machine blockers are resolved; only a human decision remains. Keep the row Blocked until a human chooses."
        | Some AwaitingHumanAction when state = IssueState.Open && status = BoardStatus.Blocked && not (List.isEmpty blockers) && (blockers |> List.forall Blockers.isResolved) ->
            Some "all machine blockers are resolved; only a human action remains. Keep the row Blocked until that action is complete."
        | _ -> None

    let blockerCycleVerdicts (graph: (Ref * Blocker list) list) : (Ref * string) list =
        let byRef = graph |> Map.ofList

        Blockers.cycles graph
        |> List.collect (fun ring ->
            let members = ring |> List.map (fun r -> r.Short) |> String.concat ", "
            ring
            |> List.choose (fun r ->
                Map.tryFind r byRef
                |> Option.map (fun _ ->
                    r, $"on a blocked-by cycle that can never become startable; a human must break one edge. The ring: %s{members}")))

    let private shortRef (ref: string) =
        match ref.IndexOf '/' with
        | i when i >= 0 -> ref.Substring(i + 1)
        | _ -> ref

    let epicVerdict
        (state: IssueState)
        (status: BoardStatus)
        (body: string)
        (graph: Reads.SubIssueSet)
        (unlinked: string list)
        : EpicFinding list =
        let mk code severity detail =
            { Code = code
              Severity = severity
              Detail = detail }

        let visible = List.length graph.Children

        let noChildren =
            if state = IssueState.Open && graph.Total = 0 then
                [ mk "EPIC-NO-CHILDREN" "error" "open [epic] with zero sub-issues — nothing rolls up" ]
            else
                []

        let truncated =
            if graph.Total > visible then
                [ mk
                      "EPIC-CHILDREN-TRUNCATED"
                      "error"
                      $"%d{graph.Total} sub-issues, only %d{visible} visible — cannot verify rollup" ]
            else
                []

        let doneOpenChild =
            if status = BoardStatus.Done && graph.Children |> List.exists (fun child -> child.Open) then
                let openRefs =
                    graph.Children
                    |> List.filter (fun child -> child.Open)
                    |> List.map (fun child -> shortRef child.Ref)
                    |> String.concat ", "

                [ mk "EPIC-DONE-OPEN-CHILD" "error" $"board says Done, but open child: %s{openRefs}" ]
            else
                []

        let undelegated =
            if state <> IssueState.Open then
                []
            else
                match EpicBody.undelegatedAcceptance body with
                | [] -> []
                | lines ->
                    let named =
                        lines
                        |> List.map (fun line ->
                            if line.Length <= 90 then
                                $"\"%s{line}\""
                            else
                                $"\"%s{line.Substring(0, 90)}…\"")
                        |> String.concat "; "

                    [ mk
                          "EPIC-UNDELEGATED-ACCEPTANCE"
                          "error"
                          $"%d{List.length lines} acceptance line(s) delegate to no child, so no child can ever discharge them and the rollup would close them unread: %s{named}. An epic's acceptance IS its children (#965) — make each one a child, or drop it from the body." ]

        let noAcceptance =
            if state = IssueState.Open && not (EpicBody.statesAcceptance body) then
                [ mk
                      "EPIC-NO-STATED-ACCEPTANCE"
                      "error"
                      "body states NO task-line acceptance, so nothing in it can be checked against the sub-issue graph and this epic can never roll up — closing it on the strength of that graph would close an unread body (#1003). State each criterion as a task line naming its child — `- [ ] #123 the thing` — and link it with `child`." ]
            else
                []

        let unlinkedFinding =
            match unlinked with
            | _ when graph.Total <> visible -> []
            | [] -> []
            | kept ->
                let named = kept |> List.map shortRef |> String.concat ", "

                [ mk
                      "EPIC-UNLINKED-CHILD"
                      "error"
                      $"body declares child(ren) absent from the sub-issue graph, so rollup cannot see them: %s{named}" ]

        let refusals =
            noChildren @ truncated @ doneOpenChild @ undelegated @ noAcceptance @ unlinkedFinding

        let rollupReady =
            if
                List.isEmpty refusals
                && state = IssueState.Open
                && graph.Children |> List.forall (fun child -> not child.Open)
            then
                [ mk
                      "EPIC-ROLLUP-READY"
                      "note"
                      $"all %d{visible} child(ren) are resolved, the graph is whole, and the body's acceptance is fully delegated — every mechanical precondition to roll up holds, and this epic is still OPEN. Nothing has asked it to: the roll-up climbs only when a worker stamps a child. Whether those children DISCHARGE this epic is an argument only a human can make (#614) — decide it, do not infer it." ]
            else
                []

        refusals @ rollupReady

    // ---- CONSOLIDATION-CANDIDATE (.github#1914) ----------------------------------------------------
    //
    // The overlap data that REFUSES a contended lane also answers "are these the same work?", and until
    // now nothing asked it. `Schedulability` parses every row's `Paths:` and computes overlap so `batch`
    // can refuse a contended candidate — one question over that graph. This rule asks the adjacent one.
    //
    // IT REPORTS AND NOTHING ELSE. Detection is mechanical; the merge decision belongs to the agent
    // running `lint` — not automatic, and not escalated to a separate human. That agent is already the
    // triaging host with the board in front of it, so it is handed a NAMED candidate set with the shared
    // tokens printed, and it disposes. `lint` writes nothing and repairs nothing, and `reconcile --apply`
    // gains nothing from this rule: there is no mechanically safe repair for "these two might be one
    // item". Hence `note`, on `EPIC-ROLLUP-READY`'s terms — reddening a gate on a question nobody has
    // been asked yet is how a gate becomes noise (#698).
    //
    // ---- TRAP 1: NOT CONNECTED COMPONENTS, AND NOT NEARLY -------------------------------------------
    //
    // Transitive closure over "shares at least one token" puts 74 of 78 `Ready` rows into ONE cluster,
    // measured on this board on 2026-07-29. A handful of files are hubs — `scripts/repos-audit.sh` is
    // declared by nine rows, `tests/repos-audit` by six, `registry/repos.yml` by five — so the closure
    // walks the whole board through them and reports that everything is everything. That is #1732/#1843's
    // directory-token problem resurfacing inside the analysis rather than inside the scheduler.
    //
    // So similarity is PAIRWISE, always, and a GROUP is a set in which EVERY pair independently clears
    // the bar and which additionally retains a token every member declares. Nothing is ever admitted by
    // way of a third row: A and C are grouped only if A~C holds on its own, never because A~B and B~C.
    //
    // ---- TRAP 2: HIGH OVERLAP IS NOT THE SAME OPERATION ---------------------------------------------
    //
    // On the same board, four rows (#1869, #1875, #1860, #1733) score at or above 0.50 plain Jaccard on
    // the hub token `registry/repos.yml` ALONE, while their objectives are unrelated — absence classes on
    // receivers, a bulk sweep of every open row, `[decision]` plugin delivery, and a CHANGELOG taxonomy.
    // #1869 and #1875 declare NOTHING BUT that token, so their plain Jaccard is 1.00. A rule that merged
    // on score would have proposed nonsense.
    //
    // ---- WHY A WEIGHTED RATIO CANNOT FIX TRAP 2, WHICH IS THE WHOLE DESIGN --------------------------
    //
    // MEASURED, and it is the counter-intuitive part. Down-weighting hub tokens INSIDE the similarity
    // ratio does nothing to that pair: the same weight appears in the numerator and the denominator and
    // CANCELS. #1869/#1875 score 1.00 under plain Jaccard, 1.00 under IDF-weighted Jaccard, and 1.00
    // under every other weighting, because a set is always exactly similar to itself. The hub discount
    // only bites when it is spent ABSOLUTELY — on how much distinct evidence the shared part actually
    // carries — so this rule has TWO factors and they answer two different questions:
    //
    //   COVERAGE  — plain Jaccard over declared tokens, unweighted. "Do these two declarations describe
    //               substantially the same files?" This is exactly the reference measurement's own
    //               statistic, at exactly its own threshold, deliberately left alone.
    //
    //   EVIDENCE  — the hub-discounted weight of the SHARED tokens, summed, and NOT divided by anything.
    //               "Is the overlap distinctive, or is it just a file everybody touches?"
    //
    // Both must clear their floor. That makes the rule the reference implementation plus exactly one
    // isolable factor, which is what lets its effect be stated rather than asserted: on the live board
    // coverage alone yields 95 pairs INCLUDING five of the false ones, and adding the evidence floor
    // yields 76 pairs including NONE of them, while still naming all six real groups.
    //
    // ---- THE WEIGHTING, AND WHY IT IS THIS ONE ------------------------------------------------------
    //
    // A token declared by `n` rows in the population weighs `1 / sqrt(n - 1)`: 1.00 when exactly two rows
    // declare it (the token is that pair's own), 0.71 at three, 0.58 at four, 0.35 at nine. The reading
    // is that a token shared by `n` rows is evidence for `n - 1` different partners at once, so no single
    // pair may claim all of it — damped by a square root, for a measured reason:
    //
    //   * UNDAMPED (`1 / (n - 1)`) destroys real groups. The repos-audit group's two shared tokens are
    //     declared by nine and six rows, so it scores 0.13 + 0.20 = 0.33 and DIES — along with the
    //     check-board-skill and command-surface groups. Four of the six real groups lost.
    //   * FLAT (no discount) keeps every group and every false pair. That is the reference.
    //   * The square root is where both hold: repos-audit scores 0.35 + 0.45 = 0.80 and lives;
    //     `registry/repos.yml` alone scores 0.50 and dies; `docs/adr` alone scores 0.45 and dies.
    //
    // The floor is `1 / sqrt 2`, which has a plain reading worth stating because it is what a reader will
    // actually reason with: ONE shared token carries a finding by itself only if AT MOST THREE rows in
    // the population declare it. A token declared by four or more needs company.
    //
    // ---- FAIL CLOSED (#266) -------------------------------------------------------------------------
    //
    // A row whose `Paths:` could not be read is REPORTED as unreadable and never silently dropped, and
    // the verdict over such a board is a NO-VERDICT rather than "no clusters". A rule that quietly shrank
    // its own population would answer "nothing to consolidate" about rows it never looked at, which is
    // the shape epic #266 is about — a gate that could not read an item must not report it clean.
    //
    // ---- REPO-RELATIVE, SO PARTITIONED BY REPO (#353) -----------------------------------------------
    //
    // `Paths:` tokens are REPO-RELATIVE. `TouchSet.conflicts` states the contract — both token lists must
    // come from the same repo, because comparing across repos invents collisions that do not exist — and
    // `lint` without `--repo` scans every repo on the board. So the population is partitioned by repo
    // before anything is compared, and the hub frequencies are counted per repo as well: `scripts/` being
    // a hub in `.github` says nothing about `scripts/` in another repository.

    /// One board row as the consolidation rule sees it.
    type ConsolidationRow =
        { /// The row's display ref, as the finding will name it.
          Ref: string
          /// The repo the tokens are relative to. Rows from different repos are NEVER compared (#353).
          Repo: string
          TouchSet: TouchSet }

    /// A candidate set of rows that MAY be one piece of work — never a claim that they are.
    type ConsolidationGroup =
        { /// Every member's ref, sorted. Never fewer than two.
          Members: string list
          /// The subtrees EVERY member declares — the group's shared touch-set, and the evidence the
          /// runner judges on. Never empty: a set with no common token is not a candidate operation.
          Shared: string list
          /// The WEAKEST member pair's coverage (plain Jaccard). Every pair is at least this similar.
          Coverage: float
          /// The WEAKEST member pair's hub-discounted shared-token evidence.
          Evidence: float }

    /// What the consolidation rule saw, INCLUDING what it could not see.
    type ConsolidationVerdict =
        { Groups: ConsolidationGroup list
          /// Rows whose `Paths:` could not be read, with the reason. Never dropped (#266) — while this
          /// is non-empty the verdict is a NO-VERDICT, not an absence of clusters.
          Unreadable: (string * string) list
          /// How many rows carried a declaration this rule could compare.
          Compared: int
          /// The whole population it was handed.
          Population: int }

    /// Coverage floor: plain Jaccard over declared tokens. The reference measurement's own threshold,
    /// deliberately unchanged — every difference from it is meant to live in the evidence floor.
    [<Literal>]
    let consolidationCoverageFloor = 0.5

    /// Evidence floor. `1 / sqrt 2` — read it as: ONE shared token is enough on its own only when at
    /// most THREE rows in the population declare it.
    let consolidationEvidenceFloor = 1.0 / sqrt 2.0

    /// What one shared token is worth when `rowsDeclaring` rows in the population declare it.
    ///
    /// `1 / sqrt(n - 1)`: full value when the token is a pair's own, damped — not crushed — as it turns
    /// into a hub. Both halves of that are measured; see the header. Total for `n <= 1` (a token nobody
    /// else declares cannot be worth more than a token exactly one other row declares).
    let consolidationTokenWeight (rowsDeclaring: int) : float =
        1.0 / sqrt (float (max (rowsDeclaring - 1) 1))

    /// The tokens this rule can compare: the MATCHABLE ones, reduced to the subtree they name.
    ///
    /// An `Unmatchable` token reserves nothing and therefore overlaps nothing (#273) — admitting it here
    /// would let two rows be called the same work on the strength of two tokens that name no file. The
    /// four token-less shapes yield nothing to compare, and each is another rule's business (#496).
    ///
    /// `Unreadable _ -> []` is an ADVICE consumer, safe as written (.github#2233 scope item 5 audit):
    /// `consolidationVerdict` below tracks `Unreadable` rows in its OWN separate `unreadable` field and
    /// reports them as a no-verdict, never as an absence of clusters — so a row this function returns
    /// `[]` for is merely excluded from pairwise comparison, never read as "declares nothing." This
    /// function only ever ADDS a consolidation suggestion; it never reaches a scheduling or verdict
    /// decision on the strength of an unread body.
    let private comparableStems (touchSet: TouchSet) : string list =
        match touchSet with
        | Declared tokens ->
            tokens
            |> List.choose (function
                | Matchable t -> Some(TouchSet.stem t)
                | Unmatchable _ -> None)
            |> List.distinct
        | Undeclared
        | DeclaredNone
        | DeclaredChore
        | Unreadable _ -> []

    /// The consolidation verdict over one board read: which open rows MAY be the same piece of work.
    ///
    /// PAIRWISE ONLY, and grouped by mutual similarity plus a common token — never by transitive closure.
    /// See the header for both traps this is shaped around and for the measurements behind the weighting.
    let consolidationVerdict (rows: ConsolidationRow list) : ConsolidationVerdict =
        let unreadable =
            rows
            |> List.choose (fun r ->
                match r.TouchSet with
                | Unreadable reason -> Some(r.Ref, reason)
                | _ -> None)
            |> List.sortBy fst

        let comparable =
            rows
            |> List.map (fun r -> r.Repo, r.Ref, comparableStems r.TouchSet)
            |> List.filter (fun (_, _, stems) -> not (List.isEmpty stems))

        // #353: one repo at a time, hub frequencies included.
        let groupsForRepo (population: (string * string list) list) : ConsolidationGroup list =
            let byRef = Map.ofList population
            let pop = List.toArray population

            let overlapsAny (token: string) (stems: string list) =
                stems |> List.exists (TouchSet.tokensOverlap token)

            // How many rows in THIS repo's population declare a token overlapping this one. Counted over
            // overlap rather than equality on purpose: a row declaring `scripts` really does reserve
            // `scripts/repos-audit.sh`, so it really is one more partner competing for that evidence.
            let frequency =
                population
                |> List.collect snd
                |> List.distinct
                |> List.map (fun token ->
                    token, population |> List.filter (fun (_, stems) -> overlapsAny token stems) |> List.length)
                |> Map.ofList

            let weight (token: string) =
                consolidationTokenWeight (Map.tryFind token frequency |> Option.defaultValue 1)

            let sharedOf (a: string list) (b: string list) =
                a @ b
                |> List.distinct
                |> List.filter (fun t -> overlapsAny t a && overlapsAny t b)
                |> List.sort

            let scoreOf (a: string list) (b: string list) =
                let shared = sharedOf a b
                let union = a @ b |> List.distinct

                let coverage =
                    if List.isEmpty union then
                        0.0
                    else
                        float (List.length shared) / float (List.length union)

                coverage, shared |> List.sumBy weight

            let edges =
                [ for i in 0 .. pop.Length - 1 do
                      for j in i + 1 .. pop.Length - 1 do
                          let refA, stemsA = pop.[i]
                          let refB, stemsB = pop.[j]
                          let coverage, evidence = scoreOf stemsA stemsB

                          if coverage >= consolidationCoverageFloor && evidence >= consolidationEvidenceFloor then
                              yield (min refA refB, max refA refB), (coverage, evidence) ]

            let scores = Map.ofList edges

            let adjacency =
                edges
                |> List.collect (fun ((a, b), _) -> [ a, b; b, a ])
                |> List.groupBy fst
                |> List.map (fun (k, vs) -> k, vs |> List.map snd |> Set.ofList)
                |> Map.ofList

            // The tokens EVERY member declares. Candidates are drawn from all members, not from one:
            // overlap is not transitive, so the token that covers the whole set may be spelled only on
            // the row with the narrowest declaration.
            let commonOf (members: Set<string>) =
                let stemSets =
                    members |> Set.toList |> List.map (fun r -> Map.tryFind r byRef |> Option.defaultValue [])

                stemSets
                |> List.collect id
                |> List.distinct
                |> List.filter (fun t -> stemSets |> List.forall (overlapsAny t))
                |> List.sort

            // Bron-Kerbosch over the PAIRWISE graph, with the common-token requirement folded into the
            // candidate sets rather than applied inside the loop.
            //
            // THE FILTER IS AT ENTRY, AND THAT PLACEMENT IS THE WHOLE CORRECTNESS ARGUMENT. Losing the
            // common token is monotone — no further member can restore it — so a vertex that would
            // destroy it is dead for this branch and every branch below it. Filtering BOTH `p` and `x`
            // on arrival makes the base case mean what it must: "no FEASIBLE extension exists", i.e.
            // this set is maximal among common-token cliques.
            //
            // Rejecting such a vertex inside the loop instead is wrong twice over, and both were
            // measured on the four-row fixture in `TouchSetLintTests`. Dropping it from `p` mid-loop
            // leaves the base case unreachable — the recursion returns having reported NOTHING, and a
            // population whose only clique is common-token-less reports no groups at all rather than
            // its legitimate sub-cliques. Parking it in `x` instead is the opposite error: an infeasible
            // vertex would suppress a genuinely maximal set as though it were extendable.
            let mutable candidates: Set<string> list = []

            let rec expand (r: Set<string>) (p: Set<string>) (x: Set<string>) =
                let feasible = Set.filter (fun v -> not (List.isEmpty (commonOf (Set.add v r))))
                let p = feasible p
                let x = feasible x

                if Set.isEmpty p && Set.isEmpty x then
                    if Set.count r >= 2 then
                        candidates <- r :: candidates
                else
                    let mutable remaining = p
                    let mutable excluded = x

                    for v in Set.toList p do
                        if Set.contains v remaining then
                            let neighbours =
                                Map.tryFind v adjacency |> Option.defaultValue Set.empty

                            expand (Set.add v r) (Set.intersect remaining neighbours) (Set.intersect excluded neighbours)
                            remaining <- Set.remove v remaining
                            excluded <- Set.add v excluded

            expand Set.empty (adjacency |> Map.toList |> List.map fst |> Set.ofList) Set.empty

            let distinct = List.distinct candidates

            distinct
            // Keep only the maximal sets. The pruning above can emit a subset of a set it later grows,
            // and reporting both would name the same candidate operation twice.
            |> List.filter (fun c -> distinct |> List.forall (fun d -> c = d || not (Set.isSubset c d)))
            |> List.map (fun members ->
                let refs = members |> Set.toList |> List.sort

                let pairs =
                    [ for i in 0 .. refs.Length - 1 do
                          for j in i + 1 .. refs.Length - 1 do
                              match Map.tryFind (min refs.[i] refs.[j], max refs.[i] refs.[j]) scores with
                              | Some s -> yield s
                              | None -> () ]

                { Members = refs
                  Shared = commonOf members
                  // The WEAKEST pair, so the number a reader sees is a floor over the whole group and
                  // never an average flattering it.
                  Coverage = pairs |> List.map fst |> List.fold min 1.0
                  Evidence = pairs |> List.map snd |> List.fold min infinity })

        let groups =
            comparable
            |> List.groupBy (fun (repo, _, _) -> repo)
            |> List.sortBy fst
            |> List.collect (fun (_, rowsInRepo) ->
                rowsInRepo |> List.map (fun (_, ref, stems) -> ref, stems) |> groupsForRepo)
            |> List.sortBy (fun g -> -(List.length g.Members), g.Members)

        { Groups = groups
          Unreadable = unreadable
          Compared = List.length comparable
          Population = List.length rows }

    /// The `CONSOLIDATION-CANDIDATE` sentence for one group.
    ///
    /// It prints the SHARED TOKENS (AC1) so the runner can judge same-operation without opening every
    /// issue, it says the disposition is the runner's (AC8), and it states what it cannot see (AC6) —
    /// in the finding, because a docstring is not where the reader is standing.
    let consolidationDetail (group: ConsolidationGroup) : string =
        let members = group.Members |> String.concat ", "
        let shared = group.Shared |> String.concat ", "

        $"MAY BE THE SAME PIECE OF WORK as %s{members} — %d{List.length group.Members} open rows whose declared touch-sets overlap on: %s{shared}. Weakest pair: coverage %.2f{group.Coverage} (plain Jaccard over `Paths:` tokens), shared-token evidence %.2f{group.Evidence} (each shared token discounted by how many rows declare it — `1/sqrt(n-1)`, so a token only these rows declare is worth 1.00 and one declared by nine is worth 0.35; the floor is `1/sqrt 2`, i.e. ONE shared token suffices only if at most THREE rows declare it). Grouped PAIRWISE — every pair here independently clears both floors and the whole set shares the tokens above; nothing was admitted through a third row, because transitive closure over shared tokens puts 74 of 78 rows in one cluster (.github#1914). WHAT THIS CANNOT SEE, and you must: two rows with DISJOINT `Paths:` may still be one operation, and nothing here detects that — a shared touch-set is evidence of the same FILES, never proof of the same OBJECTIVE (four pairs scored 0.50+ on `registry/repos.yml` alone with entirely unrelated objectives). THE DISPOSITION IS YOURS: `lint` reports and writes nothing, and consolidating — or deciding these are separate work and moving on — is the judgement of the agent running it, not a separate human's and never automatic."

    /// The `CONSOLIDATION-UNREADABLE` sentence. `lint` FAILS CLOSED (#266): a row this rule could not
    /// read is named, and the board's consolidation verdict is a NO-VERDICT rather than "no clusters".
    let consolidationUnreadableDetail (reason: string) : string =
        $"its `Paths:` could not be read (%s{reason}), so this row was compared against NOTHING — and the consolidation verdict over this board is therefore INCOMPLETE, not empty. A board that could not be read in full yields no verdict, never a clean bill (#266): any `CONSOLIDATION-CANDIDATE` groups reported alongside this are still true, but their ABSENCE proves nothing, because this row could have belonged to one. Re-run once the body is readable."

    let summarize (strict: bool) (severities: string list) : Summary =
        let errors = severities |> List.filter ((=) "error") |> List.length
        let notes = severities |> List.filter ((=) "note") |> List.length

        { Errors = errors
          Notes = notes
          Fails = errors > 0 || (strict && notes > 0) }
