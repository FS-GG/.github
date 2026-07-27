namespace FS.GG.Coord

module Class =

    open System
    open System.Text.RegularExpressions
    open Types

    /// A `Class:` line: up to three leading spaces, either case — the SAME shape as `TouchSet`'s `Paths:`
    /// and `HumanBlock`'s `Blocked on:`, because #1103 decided one grammar for body-line sentinels and a
    /// third spelling would be the drift ADR-0045 exists to prevent.
    let private declRe =
        Regex(@"^ {0,3}[Cc]lass:\s*(?<rest>.*)$", RegexOptions.Compiled)

    /// The `[decision]` TITLE prefix, after leading space. Anchored, deliberately — see the .fsi.
    let private titleRe =
        Regex(@"^\s*\[decision\]", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)

    /// The recognised values, normalised for case and surrounding space. Anything else is not a class —
    /// a `Class: P1` or a `Class: blocker` somebody wrote by hand is a vocabulary this engine does not
    /// speak, and mapping it onto the nearest of three would be the guess AC3 forbids. `itemClassOfWireName`
    /// is the parse, DERIVED from the renderer, so the vocabulary is spelled exactly once (#1012).
    let private classify (value: string) : ItemClass option =
        itemClassOfWireName value

    let fromBody (body: string) : ItemClass option =
        // OUTSIDE fences, via the one `Markdown.unfenced` every body-line rule shares (#972) — a `Class:`
        // line quoted in a ``` block is documentation, exactly as a fenced `Paths:` is. This file's own
        // ADR quotes the grammar; so will #1588's follow-ups.
        let unfenced = Markdown.unfenced body

        let declared =
            unfenced
            |> List.choose (fun line ->
                let m = declRe.Match line
                if m.Success then classify (m.Groups.["rest"].Value) else None)

        // The ADR-0045 sentinel, read as EVIDENCE rather than restated (AC5). `HumanBlock.parse` owns
        // that grammar and this asks it rather than re-matching `Blocked on:` — the second copy of a
        // body-line rule in one engine is #972 exactly, and it is not made safe by agreeing today.
        //
        // Only `human/decision`. `human/action` is a park on somebody DOING something, which says nothing
        // about how bad the underlying item is: a `defect` can be blocked on a credential grant. Reading
        // it as `decision` would class every waiting-on-an-action defect as a thing no driver may schedule.
        let sentinel =
            match HumanBlock.parse body with
            | Some AwaitingHumanDecision -> [ Decision ]
            | Some AwaitingHumanAction
            | None -> []

        let all = declared @ sentinel

        // DEFECT DOMINATES, then DECISION, then HARDENING. Ordered search, never `List.tryHead` over the
        // lines: a body declaring both must resolve to the reading that keeps a burn-down running, and
        // "whichever line the author typed first" is not a rule anybody could rely on.
        if all |> List.contains Defect then Some Defect
        elif all |> List.contains Decision then Some Decision
        elif all |> List.contains Hardening then Some Hardening
        else None

    let fromTitle (title: string) : ItemClass option =
        if isNull title then None
        elif titleRe.IsMatch title then Some Decision
        else None

    let derive (title: string) (body: string) : ItemClass option =
        match fromBody body with
        | Some c -> Some c
        | None -> fromTitle title
