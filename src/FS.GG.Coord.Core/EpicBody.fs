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

    let childRefs (selfOwner: string) (selfRepo: string) (body: string) : string list =
        (if isNull body then "" else body).Replace("\r\n", "\n").Split('\n')
        |> Array.choose (fun line ->
            if not (taskLine.IsMatch line) then
                None
            else
                let m = refRe.Match line

                if not m.Success then
                    None
                elif m.Groups.[1].Success then
                    Some $"%s{m.Groups.[1].Value}#%s{m.Groups.[2].Value}"
                elif m.Groups.[3].Success then
                    Some $"%s{m.Groups.[3].Value}/%s{m.Groups.[4].Value}#%s{m.Groups.[5].Value}"
                else
                    Some $"%s{selfOwner}/%s{selfRepo}#%s{m.Groups.[6].Value}")
        |> Array.toList
        |> List.distinct
        |> List.sort
