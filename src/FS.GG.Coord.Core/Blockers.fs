namespace FS.GG.Coord

module Blockers =

    open System
    open System.Text.RegularExpressions
    open Types

    let isResolved (blocker: Blocker) : bool =
        match blocker.State with
        | BlockerClosed
        | BlockerMerged -> true
        | BlockerOpen
        | BlockerUnknown
        | BlockerUnparseable -> false

    let unresolved (blockers: Blocker list) : Blocker list =
        blockers |> List.filter (isResolved >> not)

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
