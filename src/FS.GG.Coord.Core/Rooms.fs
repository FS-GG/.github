namespace FS.GG.Coord

module Rooms =

    open System.Text.RegularExpressions
    open Types

    // A `Rooms:` line: up to three leading spaces, either case — the SAME shape as `TouchSet`'s `Paths:`
    // regex, because ADR-0051 puts it in the `Paths:`/`Blocked on:` declaration family (ADR-0045). Spaces,
    // not tabs: a token indented with a tab is code to the surrounding markdown anyway, exactly as the
    // fence and `Paths:` rules have it.
    let private declRe =
        Regex(@"^ {0,3}[Rr]ooms:\s*(?<rest>.*)$", RegexOptions.Compiled)

    // The four ref spellings, the SAME grammar `EpicBody.childRefs` and `Blockers.canonToken` accept —
    // `ProtocolTests` pins the three against one token set so they cannot drift (#1153). Unlike
    // `childRefs`, which reads the FIRST ref of a task line, a `Rooms:` line may carry SEVERAL refs (a
    // room over `#12, #13`), so `parse` reads EVERY match on the line.
    let private refRe =
        Regex(
            @"([A-Za-z0-9._-]+/[A-Za-z0-9._-]+)#([0-9]+)|https?://github\.com/([^/\s]+)/([^/\s]+)/issues/([0-9]+)|([A-Za-z0-9._-]+)#([0-9]+)|#([0-9]+)",
            RegexOptions.Compiled
        )

    // Reduce one matched ref token to a canonical `Ref`. `owner`/`repo` in group 1 are each `[.\w-]+`
    // (no slash), so the pair splits on its single `/`. A `repo#n` carries only the repo and a bare `#n`
    // carries neither, both defaulting to the referencing item's own owner/repo — the same reduction
    // `EpicBody.childRefs` and `Blockers.canonToken` give.
    let private toRef (selfOwner: string) (selfRepo: string) (m: Match) : Ref =
        if m.Groups.[1].Success then
            let parts = m.Groups.[1].Value.Split('/')
            { Owner = parts.[0]
              Repo = parts.[1]
              Number = int m.Groups.[2].Value }
        elif m.Groups.[3].Success then
            { Owner = m.Groups.[3].Value
              Repo = m.Groups.[4].Value
              Number = int m.Groups.[5].Value }
        elif m.Groups.[6].Success then
            // `repo#n`: the owner defaults to the referencing item's own; only the REPO is carried.
            { Owner = selfOwner
              Repo = m.Groups.[6].Value
              Number = int m.Groups.[7].Value }
        else
            // bare `#n`: owner AND repo default to the referencing item's own.
            { Owner = selfOwner
              Repo = selfRepo
              Number = int m.Groups.[8].Value }

    let parse (selfOwner: string) (selfRepo: string) (body: string) : Ref list =
        // OUTSIDE fences, via the one `Markdown.unfenced` every body-line rule shares (#972) — a `Rooms:`
        // line quoted in a ``` block is documentation, exactly as a fenced `Paths:` is. `Markdown.unfenced`
        // also normalises a null body to an empty one, so no caller repeats either.
        Markdown.unfenced body
        |> List.collect (fun line ->
            let m = declRe.Match line

            if m.Success then
                // EVERY ref on the line — a room membership is additive, not first-wins (ADR-0051 §4).
                refRe.Matches(m.Groups.["rest"].Value)
                |> Seq.cast<Match>
                |> Seq.map (toRef selfOwner selfRepo)
                |> List.ofSeq
            else
                [])
        // UNION across every `Rooms:` line, de-duped and sorted — a ref set is diffed (the room's
        // close computation asks "which items still reference me?"), so the order must be deterministic.
        |> List.distinct
        |> List.sort
