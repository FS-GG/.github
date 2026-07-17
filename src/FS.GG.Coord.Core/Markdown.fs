namespace FS.GG.Coord

module Markdown =

    open System
    open System.Text.RegularExpressions

    type LineKind =
        | Text
        | FenceMarker
        | Code

    // The opening or closing line of a fenced code block: up to three leading SPACES, then a run of three-
    // or-more backticks or tildes, then the rest of the line (an info string on an opener; nothing but
    // whitespace on a closer). Group 1 is the marker run, group 2 the remainder.
    //
    // FOUR COLUMNS IS NOT A FENCE, it is an indented code block — which is why the bound is three and not
    // `^\s*`. `Writes` used `^\s*` and `TouchSet` used `^ {0,3}`, so they disagreed about every deeply
    // indented marker, and `widen` rewrote bodies under one rule that `take` then scheduled under another
    // (#972).
    //
    // SPACES, NOT `[ \t]`, AND THE TAB IS THE WHOLE REASON TO SAY SO. CommonMark advances a leading tab to
    // the next 4-column tab stop, so a tab-indented marker is already at column 4 — an indented code block
    // containing a literal ```, which is exactly what GitHub renders. Admitting `\t` here would open a
    // fence GitHub does not, and then swallow every line below it: a `Paths:` line that IS a live
    // declaration would parse as quoted, and the item would never schedule. That is the fail-open
    // direction — inventing a fence LOSES declarations — and it is the one this module exists to refuse.
    let private fenceLine =
        Regex(@"^ {0,3}(`{3,}|~{3,})(.*)$", RegexOptions.Compiled)

    // Does `line` CLOSE a fence opened with a run of `ch` of length `len`?
    //
    // Same character, a run at least as long as the opener, and nothing but whitespace after it. All three
    // conditions are load-bearing, and the trackers this module replaces had none of them: they toggled a
    // bool on any marker at all, so a ``` block ended on its first `~~~` line and a ```` block ended on its
    // first ```. Both are content per CommonMark, and both are shapes this org's own docs produce whenever
    // they quote a fence inside a fence — at which point the rest of the quoted block is read as live body
    // text, and a `Paths:` line quoted in an example becomes a REAL reservation of files the item never
    // touches. That is the mis-scheduling #277's rule ("unschedulable beats mis-scheduled") exists to
    // prevent, reintroduced by the tracker written to enforce it.
    let private closesFence (ch: char) (len: int) (line: string) =
        let m = fenceLine.Match line

        m.Success
        && m.Groups.[1].Value.[0] = ch
        && m.Groups.[1].Value.Length >= len
        && String.IsNullOrWhiteSpace m.Groups.[2].Value

    // Does `line` OPEN a fence, and with what run?
    //
    // The backtick spelling carries one extra rule and it earns its keep in the FAIL-OPEN direction: a
    // backtick opener's info string may not itself contain a backtick, so a line like ```#5``` is the ref
    // is a paragraph, not a fence. Reading it as one would swallow every line after it — every `Paths:`
    // line, every task line — and report the body as declaring nothing. Inventing a fence loses
    // declarations; missing one merely quotes them. The tilde spelling has no such rule: `~~~a~b` opens a
    // block, info string and all.
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

    // Walk the body once, carrying the open fence. Every answer this module gives is a fold over this, so
    // there is exactly one traversal to be wrong about.
    let private walk (body: string) =
        let lines =
            (if isNull body then "" else body).Replace("\r\n", "\n").Split('\n')

        let mutable fence = None
        let acc = ResizeArray<string * LineKind>(lines.Length)

        for line in lines do
            match fence with
            | Some(ch, len) ->
                if closesFence ch len line then
                    fence <- None
                    acc.Add(line, FenceMarker)
                else
                    acc.Add(line, Code)
            | None ->
                match opensFence line with
                | Some run ->
                    fence <- Some run
                    acc.Add(line, FenceMarker)
                | None -> acc.Add(line, Text)

        // AN UNTERMINATED FENCE RUNS TO THE END OF THE BODY, and it is left that way — `fence` is still
        // Some here, and every line after the opener is already Code. That is CommonMark's rule and
        // GitHub's rendering, so it is what the body's author saw. A reader must not silently "repair" it
        // into text: the author is looking at a code block, and a rule that disagrees is one that reads
        // their quotation as their declaration. A WRITER may repair it — see `unterminatedFenceCloser` —
        // but that is a write-side decision layered on this read, not a second reading of it.
        List.ofSeq acc, fence

    let classify (body: string) : (string * LineKind) list = fst (walk body)

    let unfenced (body: string) : string list =
        walk body
        |> fst
        |> List.choose (fun (line, kind) -> if kind = Text then Some line else None)

    let unterminatedFenceCloser (body: string) : string option =
        match snd (walk body) with
        | Some(ch, len) -> Some(String(ch, len))
        | None -> None
