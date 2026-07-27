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

    /// Every `ItemClass` case, by reflection — `Protocol.everyBlockerState`'s shape exactly, and for its
    /// reason. All three cases are NULLARY, so the list can be DERIVED, and a list nobody writes is a
    /// list that cannot omit a case. `Types.everyItemClass` is `private` and is the same derivation done
    /// by hand behind a pin; this is the one the CHECKER reads, so .github#1651 AC5 is satisfied by
    /// construction rather than by remembering. A fourth `ItemClass` reaches the diagnostic the day it
    /// is declared, with no edit here and none in `lint`.
    let legalClasses: ItemClass list =
        FSharp.Reflection.FSharpType.GetUnionCases typeof<ItemClass>
        |> Array.map (fun c -> FSharp.Reflection.FSharpValue.MakeUnion(c, [||]) :?> ItemClass)
        |> Array.toList

    /// The RAW value of every `Class:` line this body declares, outside fences, in body order.
    ///
    /// One reading of the grammar, shared by `fromBody` (which resolves the values) and `unrecognised`
    /// (which reports the ones that do not resolve). A second regex for the second question is the copy
    /// #972 measured, and it would drift in the direction that hurts: a checker whose idea of "a `Class:`
    /// line is present" differed from the parser's would say "you wrote one and it is wrong" about a line
    /// the parser never saw.
    let private declaredValues (body: string) : string list =
        // OUTSIDE fences, via the one `Markdown.unfenced` every body-line rule shares (#972) — a `Class:`
        // line quoted in a ``` block is documentation, exactly as a fenced `Paths:` is. This file's own
        // ADR quotes the grammar; so will #1588's follow-ups.
        Markdown.unfenced body
        |> List.choose (fun line ->
            let m = declRe.Match line
            if m.Success then Some(m.Groups.["rest"].Value) else None)

    let unrecognised (body: string) : string list =
        // The value is reported as the author WROTE it, minus surrounding space — quoting `docs   ` back
        // with its padding would make the diagnostic look like the padding was the fault. An EMPTY
        // `Class:` line counts: a line that declares the key and no value is a declaration that could not
        // be read, and #266's rule says a subject you could not evaluate is never one that passed. Under
        // the old single rule it reported as "records no `Class:`", which is the wrong-fault shape this
        // item exists to remove.
        declaredValues body
        |> List.filter (fun v -> (classify v).IsNone)
        |> List.map (fun v -> v.Trim())
        |> List.distinct

    let fromBody (body: string) : ItemClass option =
        let declared = declaredValues body |> List.choose classify

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
