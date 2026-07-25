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
