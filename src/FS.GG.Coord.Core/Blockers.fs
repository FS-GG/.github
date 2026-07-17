namespace FS.GG.Coord

module Blockers =

    open System
    open System.Text.RegularExpressions
    open Types

    let isResolvedState (state: BlockerState) : bool =
        match state with
        | BlockerClosed
        | BlockerMerged -> true
        | BlockerOpen
        | BlockerUnknown
        | BlockerUnparseable -> false

    // ONE decision, asked twice — never two matches that agree today. A `Blocker` resolves iff its STATE
    // does; there is nothing else in the record to consult.
    let isResolved (blocker: Blocker) : bool = isResolvedState blocker.State

    let unresolved (blockers: Blocker list) : Blocker list =
        blockers |> List.filter (isResolved >> not)

    let cycles (nodes: (Ref * Blocker list) list) : Ref list list =
        // First occurrence wins. A duplicated ref is a caller's accident, not a second node, and letting it
        // through would let one item hold two different blocker lists in the same graph.
        let deduped =
            nodes
            |> List.fold
                (fun (seen, acc) (r, bs) ->
                    if Set.contains r seen then
                        (seen, acc)
                    else
                        (Set.add r seen, (r, bs) :: acc))
                (Set.empty, [])
            |> snd
            |> List.rev

        let known = deduped |> List.map fst |> Set.ofList

        // The edges. `isResolved` decides resolution — asked, never re-answered (#520). A blocker we cannot
        // place in this graph draws NO edge: it may sit on a ring we cannot see, and inventing the edge would
        // manufacture a deadlock, while omitting it merely under-reports. Under-reporting is the safe
        // direction for a claim this loud (#266).
        let succs =
            deduped
            |> List.map (fun (r, bs) ->
                r,
                bs
                |> List.filter (isResolved >> not)
                |> List.choose (fun b -> b.Ref)
                |> List.filter (fun t -> Set.contains t known)
                |> List.distinct)
            |> Map.ofList

        // TARJAN. Linear, and it terminates on every graph — including one that is entirely a single ring,
        // which is exactly the input a naive "walk until you repeat" loop never returns from.
        let mutable counter = 0
        let index = System.Collections.Generic.Dictionary<Ref, int>()
        let low = System.Collections.Generic.Dictionary<Ref, int>()
        let onStack = System.Collections.Generic.HashSet<Ref>()
        let stack = System.Collections.Generic.Stack<Ref>()
        let found = ResizeArray<Ref list>()
        let sortKey (r: Ref) = (r.Owner, r.Repo, r.Number)

        let rec strongconnect (v: Ref) =
            index.[v] <- counter
            low.[v] <- counter
            counter <- counter + 1
            stack.Push v
            onStack.Add v |> ignore

            for w in Map.find v succs do
                if not (index.ContainsKey w) then
                    strongconnect w
                    low.[v] <- min low.[v] low.[w]
                elif onStack.Contains w then
                    low.[v] <- min low.[v] index.[w]

            if low.[v] = index.[v] then
                let comp = ResizeArray<Ref>()
                let mutable popping = true

                while popping do
                    let w = stack.Pop()
                    onStack.Remove w |> ignore
                    comp.Add w
                    if w = v then popping <- false

                let members = List.ofSeq comp

                // A COMPONENT IS A RING IFF it has more than one member, or its ONE member blocks ITSELF.
                // Without the self-edge test every node on an acyclic board is its own component and would
                // be reported as a deadlock — the false positive that would make this diagnosis worthless
                // on precisely the boards that are fine.
                let isRing =
                    match members with
                    | [ single ] -> Map.find single succs |> List.contains single
                    | _ -> true

                if isRing then
                    found.Add(members |> List.sortBy sortKey)

        for (v, _) in deduped do
            if not (index.ContainsKey v) then
                strongconnect v

        found |> List.ofSeq |> List.sortBy (List.map sortKey)

    type BlockedByRefusal =
        | Placeholder
        | NotIssueRefs

    /// Canonicalize ONE token to `owner/repo#n`, or `None` when it is not an issue ref. The four accepted
    /// forms are anchored (`^…$`) so trailing prose — `FS-GG/FS.GG.SDD#8 (republish vehicle)` — cannot be
    /// silently swallowed by a ref prefix; only a token that is a ref, whole, canonicalizes.
    let private canonToken (defaultOwner: string) (defaultRepo: string) (tok: string) : string option =
        let url = Regex.Match(tok, @"^https?://github\.com/([\w.-]+)/([\w.-]+)/issues/(\d+)$")

        if url.Success then
            Some $"%s{url.Groups.[1].Value}/%s{url.Groups.[2].Value}#%s{url.Groups.[3].Value}"
        else
            let full = Regex.Match(tok, @"^([\w.-]+)/([\w.-]+)#(\d+)$")

            if full.Success then
                Some $"%s{full.Groups.[1].Value}/%s{full.Groups.[2].Value}#%s{full.Groups.[3].Value}"
            else
                let repoN = Regex.Match(tok, @"^([\w.-]+)#(\d+)$")

                if repoN.Success then
                    // `repo#n`: the owner defaults to the blocked item's — the same reduction `repo#n` gets
                    // as a `<ref>` everywhere else.
                    Some $"%s{defaultOwner}/%s{repoN.Groups.[1].Value}#%s{repoN.Groups.[2].Value}"
                else
                    let bare = Regex.Match(tok, @"^#(\d+)$")

                    if bare.Success then
                        // A bare `#n` adopts the blocked item's OWN owner/repo.
                        Some $"%s{defaultOwner}/%s{defaultRepo}#%s{bare.Groups.[1].Value}"
                    else
                        None

    let canonicalizeBlockedBy
        (defaultOwner: string)
        (defaultRepo: string)
        (raw: string)
        : Result<string option, BlockedByRefusal> =
        let trimmed = raw.Trim()

        if trimmed = "" then
            Ok None
        // The placeholder set is bash's `canon_blocked_by` verbatim: a run of hyphens, an em/en dash, or
        // one of none / n/a / tbd / todo (case-insensitive). All mean "nothing blocks this", and all are
        // refused toward CLEARING — so a reader never has to guess whether a placeholder is a blocker the
        // field could not parse.
        elif Regex.IsMatch(trimmed.ToLowerInvariant(), @"^(-+|—|–|none|n/?a|tbd|todo)$") then
            Error Placeholder
        else
            let tokens =
                trimmed.Split(',')
                |> Array.map (fun t -> t.Trim())
                |> Array.filter (fun t -> t <> "")

            // A value that was all separators (`,`) carries no ref — prose, not a dependency.
            if Array.isEmpty tokens then
                Error NotIssueRefs
            else
                let canonicalized = tokens |> Array.map (canonToken defaultOwner defaultRepo)

                if canonicalized |> Array.exists Option.isNone then
                    // ANY token that is not a ref refuses the WHOLE write — a dependency field half
                    // full of prose is the drift this gate exists to stop.
                    Error NotIssueRefs
                else
                    let deduped =
                        canonicalized
                        |> Array.choose id
                        |> Array.fold (fun acc r -> if List.contains r acc then acc else acc @ [ r ]) []

                    Ok(Some(String.Join(", ", deduped)))
