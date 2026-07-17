namespace FS.GG.Coord

module EpicBody =

    open System
    open System.Text.RegularExpressions

    // A task-list line: up to three leading spaces, one of the three bullets GitHub renders a task list for
    // (`-`, `*`, `+`), a `[ ]`/`[x]`/`[X]` checkbox, then a space. A matcher that knew only `-` would read
    // an epic written with `+` as declaring NO children, and the gate would wave it through — a gate failing
    // open on a formatting choice.
    let private taskLine =
        Regex(@"^[ \t]{0,3}[-*+][ \t]+\[[ xX]\][ \t]", RegexOptions.Compiled)

    // The opening or closing line of a fenced code block: up to three leading spaces, then a run of three-or-
    // more backticks or tildes, then the rest of the line (an info string on an opener; nothing but whitespace
    // on a closer). Group 1 is the marker run, group 2 the remainder.
    let private fenceLine =
        Regex(@"^[ \t]{0,3}(`{3,}|~{3,})(.*)$", RegexOptions.Compiled)

    // Is `line` a fence that CLOSES the run of `ch` of length `len`? GFM closes a fence only with the same
    // character, a run at least as long as the opener, and nothing but whitespace after it — so a ``` block
    // carries a ~~~ line as content, and vice versa.
    let private closesFence (ch: char) (len: int) (line: string) =
        let m = fenceLine.Match line

        m.Success
        && m.Groups.[1].Value.[0] = ch
        && m.Groups.[1].Value.Length >= len
        && String.IsNullOrWhiteSpace m.Groups.[2].Value

    // Does `line` OPEN a fence, and if so with what run? The backtick spelling has one extra rule, and it is
    // the one that matters here: a backtick opener's info string may not itself contain a backtick, so
    // ```` ```#5``` is the ref ```` is a paragraph rather than a fence. Getting that wrong is the dangerous
    // direction — a phantom opener swallows every real task line after it, and the epic reads as having NO
    // children. That is the gate failing OPEN, which is what `taskLine`'s own bullet comment exists to
    // prevent one line up.
    let private opensFence (line: string) =
        let m = fenceLine.Match line

        if not m.Success then
            None
        else
            let marker = m.Groups.[1].Value

            if marker.[0] = '`' && m.Groups.[2].Value.Contains "`" then
                None
            else
                Some(marker.[0], marker.Length)

    // The body's lines with every fenced code block removed. A task line inside a fence is a MENTION — the
    // `.fsi` contract has always said so ("a mention is not a declaration") — and there was no fence handling
    // in this module at all, so an epic that QUOTED a task list to talk about one declared its refs for real.
    // #965's own first draft quoted #672's acceptance in a fence to demonstrate the bug, and its body parsed
    // as declaring #561. That is #683's shape one module over: a doc that quotes a parser's input is parsed
    // by it.
    //
    // The consequence is a PHANTOM: a ref the body "declares" and the sub-issue graph can never contain,
    // because nothing linked it and nothing should. It fails CLOSED, not open — `bodyUnlinkedChildren` keeps
    // it, so `lint` reports an EPIC-UNLINKED-CHILD nobody can action and the rollup refuses to close an epic
    // whose real children are all done, naming a child that does not exist. Obstruction rather than a false
    // Done, but the remedy it prints ("link them with `fsgg-coord child`") cannot be carried out.
    //
    // An UNCLOSED fence runs to the end of the body, per CommonMark. That is not a lax reading — it is what
    // GitHub renders, so the parser and the human reading the issue agree about where the code starts and
    // stops, which is the whole point of the fix (#965).
    let private unfencedLines (lines: string array) =
        lines
        |> Array.fold
            (fun (fence, kept) line ->
                match fence with
                | Some(ch, len) ->
                    // Inside a block: the closing fence and its content alike declare nothing.
                    if closesFence ch len line then (None, kept) else (fence, kept)
                | None ->
                    match opensFence line with
                    | Some run -> (Some run, kept)
                    | None -> (None, line :: kept))
            (None, [])
        |> snd
        |> List.rev

    // The FIRST ref on the line, in one of three spellings. Alternation order is leftmost-then-first, so an
    // `owner/repo#n` is preferred over the bare `#n` it contains at the same position.
    let private refRe =
        Regex(
            @"([A-Za-z0-9._-]+/[A-Za-z0-9._-]+)#([0-9]+)|https?://github\.com/([^/\s]+)/([^/\s]+)/issues/([0-9]+)|#([0-9]+)",
            RegexOptions.Compiled
        )

    let childRefs (selfOwner: string) (selfRepo: string) (body: string) : string list =
        (if isNull body then "" else body).Replace("\r\n", "\n").Split('\n')
        |> unfencedLines
        |> List.choose (fun line ->
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
        |> List.distinct
        |> List.sort
