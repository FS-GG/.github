namespace FS.GG.Coord

module EpicBody =

    open System.Text.RegularExpressions

    // A task-list line: up to three leading spaces, one of the three bullets GitHub renders a task list for
    // (`-`, `*`, `+`), a `[ ]`/`[x]`/`[X]` checkbox, then a space. A matcher that knew only `-` would read
    // an epic written with `+` as declaring NO children, and the gate would wave it through — a gate failing
    // open on a formatting choice.
    let private taskLine =
        Regex(@"^[ \t]{0,3}[-*+][ \t]+\[[ xX]\][ \t]", RegexOptions.Compiled)

    // The FIRST ref on the line, in one of three spellings. Alternation order is leftmost-then-first, so an
    // `owner/repo#n` is preferred over the bare `#n` it contains at the same position.
    let private refRe =
        Regex(
            @"([A-Za-z0-9._-]+/[A-Za-z0-9._-]+)#([0-9]+)|https?://github\.com/([^/\s]+)/([^/\s]+)/issues/([0-9]+)|#([0-9]+)",
            RegexOptions.Compiled
        )

    // EVERY acceptance line the body states, and the ONE place that decides what one is. `childRefs` and
    // `undelegatedAcceptance` are the two halves of this list — the lines that name a child, and the lines
    // that do not — so they cannot disagree about which lines are acceptance in the first place.
    //
    // A task line inside a fenced code block is a MENTION, not a declaration — and WHERE the fence is, is
    // `Markdown`'s question, not this module's (#972). This file used to carry its own fence tracker; it was
    // the third in the engine, and the three disagreed.
    let private acceptanceLines (body: string) : string list =
        Markdown.unfenced body |> List.filter taskLine.IsMatch

    let childRefs (selfOwner: string) (selfRepo: string) (body: string) : string list =
        acceptanceLines body
        |> List.choose (fun line ->
            let m = refRe.Match line

            // NO REF IS NOT NOTHING. This returns `None` because the line names no child — and that is the
            // whole of what it means. The line itself is un-delegated acceptance, which `undelegatedAcceptance`
            // KEEPS and the roll-up refuses to close over (#965). Dropping it HERE is correct; dropping it
            // everywhere is what closed #561.
            if not m.Success then
                None
            elif m.Groups.[1].Success then
                Some $"%s{m.Groups.[1].Value}#%s{m.Groups.[2].Value}"
            elif m.Groups.[3].Success then
                Some $"%s{m.Groups.[3].Value}/%s{m.Groups.[4].Value}#%s{m.Groups.[5].Value}"
            else
                Some $"%s{selfOwner}/%s{selfRepo}#%s{m.Groups.[6].Value}")
        |> List.distinct
        |> List.sort

    let undelegatedAcceptance (body: string) : string list =
        acceptanceLines body
        |> List.filter (fun line -> not (refRe.IsMatch line))
        |> List.map (fun line -> line.Trim())

    // THE THIRD ANSWER. `childRefs` and `undelegatedAcceptance` partition the task lines between them, so
    // an empty result from BOTH is ambiguous: it means "every criterion is a child" and "nothing is written
    // down" alike. #965's guard read the first from the second and closed #561's shape anyway (#1003).
    let statesAcceptance (body: string) : bool =
        acceptanceLines body |> List.isEmpty |> not
