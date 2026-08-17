namespace FS.GG.Coord

module Kind =

    open System.Text.RegularExpressions
    open Types

    /// A `Kind:` line: up to three leading spaces, either case — the SAME shape as `TouchSet`'s `Paths:`,
    /// `Class`'s `Class:` and `HumanBlock`'s `Blocked on:`, because #1103 decided one grammar for
    /// body-line sentinels and a fourth spelling would be the drift ADR-0045 exists to prevent.
    let private declRe =
        Regex(@"^ {0,3}[Kk]ind:\s*(?<rest>.*)$", RegexOptions.Compiled)

    /// The recognised values, normalised for case and surrounding space. `itemKindOfWireName` IS the
    /// parse, derived from the renderer, so the vocabulary is spelled exactly once (#1012).
    let private classify (value: string) : ItemKind option = itemKindOfWireName value

    /// Every `ItemKind` case, by reflection — `Class.legalClasses`' shape exactly, and for its reason.
    /// All four cases are NULLARY, so the list can be DERIVED, and a list nobody writes cannot omit a
    /// case. A fifth `ItemKind` reaches every diagnostic the day it is declared, with no edit here.
    let legalKinds: ItemKind list =
        FSharp.Reflection.FSharpType.GetUnionCases typeof<ItemKind>
        |> Array.map (fun c -> FSharp.Reflection.FSharpValue.MakeUnion(c, [||]) :?> ItemKind)
        |> Array.toList

    /// The RAW value of every `Kind:` line this body declares, outside fences, in body order.
    ///
    /// ONE reading of the grammar, shared by `fromBody` (which resolves the values) and `unrecognised`
    /// (which reports the ones that do not). A second regex for the second question is the copy #972
    /// measured, and it drifts in the direction that hurts.
    let private declaredValues (body: string) : string list =
        // OUTSIDE fences, via the one `Markdown.unfenced` every body-line rule shares (#972). A `Kind:`
        // line quoted in a ``` block is documentation — and this module's own .fsi and the board-schema
        // doc both quote it, so the fence rule is exercised by this change's own text.
        Markdown.unfenced body
        |> List.choose (fun line ->
            let m = declRe.Match line
            if m.Success then Some(m.Groups.["rest"].Value) else None)

    let unrecognised (body: string) : string list =
        // Reported as the author WROTE it, minus surrounding space. An EMPTY `Kind:` line counts: a line
        // declaring the key and no value is a declaration that could not be READ, and #266's rule is that
        // a subject you could not evaluate is never one that passed.
        declaredValues body
        |> List.filter (fun v -> (classify v).IsNone)
        |> List.map (fun v -> v.Trim())
        |> List.distinct

    let fromBody (body: string) : ItemKind option =
        let declared = declaredValues body |> List.choose classify

        // `work` DOMINATES, then the standing kinds in declaration-independent order. An ORDERED search,
        // never `List.tryHead` over the lines: a body declaring both `work` and `register` must resolve to
        // the reading that keeps the row under the reducer, and "whichever line the author typed first" is
        // not a rule anybody could rely on. The .fsi carries the full argument.
        //
        // Among the three STANDING kinds there is no safety ordering to make — none of them is more
        // exempt than another — so they are searched in the union's own declaration order, which
        // `legalKinds` also produces, rather than in an order this function invents.
        if declared |> List.contains Work then Some Work
        elif List.isEmpty declared then None
        else legalKinds |> List.tryFind (fun k -> declared |> List.contains k)

    let govern (declared: ItemKind option) : ItemKind = declared |> Option.defaultValue Work

    // DERIVED as "not `Work`", never listed as three cases: a fifth `ItemKind` is standing by default,
    // which is the fail-closed direction. A kind this predicate has not been taught about is not one we
    // may claim has a completion condition.
    let isStanding (k: ItemKind) : bool = k <> Work
