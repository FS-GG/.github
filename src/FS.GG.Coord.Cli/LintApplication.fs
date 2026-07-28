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

    let summarize (strict: bool) (severities: string list) : Summary =
        let errors = severities |> List.filter ((=) "error") |> List.length
        let notes = severities |> List.filter ((=) "note") |> List.length

        { Errors = errors
          Notes = notes
          Fails = errors > 0 || (strict && notes > 0) }
