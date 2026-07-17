namespace FS.GG.Coord

module HumanBlock =

    open System.Text.RegularExpressions
    open Types

    /// A `Blocked on:` line: up to three leading spaces, either case — the SAME shape as `TouchSet`'s
    /// `Paths:` regex, because #1103 decided one grammar for both. (Spaces, not tabs, exactly as the fence
    /// and `Paths:` rules — a token indented with a tab is code to the surrounding markdown anyway.)
    let private declRe =
        Regex(@"^ {0,3}[Bb]locked [Oo]n:\s*(?<rest>.*)$", RegexOptions.Compiled)

    /// The recognised sentinel values, normalised for case and surrounding space. Anything else is not a
    /// sentinel — a `Blocked on: #123` a filer wrote by hand is a `Blocked by` ref in the wrong place, not
    /// this line, and reading it as a human-block would refuse an item that has an ordinary blocker.
    let private classify (value: string) : HumanBlock option =
        match value.Trim().ToLowerInvariant() with
        | "human/decision" -> Some AwaitingHumanDecision
        | "human/action" -> Some AwaitingHumanAction
        | _ -> None

    let parse (body: string) : HumanBlock option =
        // OUTSIDE fences, via the one `Markdown.unfenced` every body-line rule shares (#972) — a
        // `Blocked on:` line quoted in a ``` block is documentation, exactly as a fenced `Paths:` is.
        let declared =
            Markdown.unfenced body
            |> List.choose (fun line ->
                let m = declRe.Match line
                if m.Success then classify (m.Groups.["rest"].Value) else None)

        // DECISION DOMINATES over ACTION when both are present: the stronger "a human must choose" must
        // never be weakened to "waiting on an action". Order the search, do not take the first line.
        if declared |> List.contains AwaitingHumanDecision then Some AwaitingHumanDecision
        elif declared |> List.contains AwaitingHumanAction then Some AwaitingHumanAction
        else None
