namespace FS.GG.Coord

module Kit =

    /// `read -r want src` splits a lock line on IFS whitespace: the first token is the digest, the rest is
    /// the source path (which carries no spaces here). A `#`-prefixed first token is a comment; a blank
    /// line or one with no second field is skipped.
    let parseLock (content: string) : (string * string) list =
        content.Replace("\r\n", "\n").Split('\n')
        |> Array.toList
        |> List.choose (fun raw ->
            match raw.Split([| ' '; '\t' |], System.StringSplitOptions.RemoveEmptyEntries) |> Array.toList with
            | want :: _ when want.StartsWith "#" -> None // a comment line
            | _want :: src :: _ -> Some(_want, src)
            | _ -> None) // blank, or a digest with no source field

    let staleSources (resolve: string -> string option) (lock: (string * string) list) : string list =
        lock
        |> List.choose (fun (want, src) ->
            match resolve src with
            | Some have when have <> want -> Some src // present AND its digest differs → stale
            | _ -> None) // matches, or the file is not in this tree → not a staleness

    let declaredSources (lock: (string * string) list) (declared: string list) : string list =
        lock
        |> List.choose (fun (_want, src) ->
            // EITHER DIRECTION, and that is `tokensOverlap`'s contract, not an accident of using it: a
            // worker declaring `.claude/skills/pnext-item/**` names the source, and so does one declaring
            // the bare parent `.claude/skills` — which reserves it just as effectively (#309).
            if declared |> List.exists (fun token -> TouchSet.tokensOverlap token src) then
                Some src
            else
                None)

    let skillMirror (roots: string list) (src: string) : (string * string) option =
        // The source's own root is read OFF THE DECLARATION, never assumed. `.claude/skills/<id>` is a
        // convention nothing enforces — `repos.sh validate` accepts a row whose id and source directory
        // differ — so a mirror rebuilt from the id would be a location invented here instead of read from
        // the registry that owns it, which is the bug this function exists to fix, one field over.
        let trim (s: string) = s.TrimEnd([| '/' |])
        let src = trim src
        let lane = roots |> List.map trim

        // ORDINAL, like every other path comparison in the engine (`TouchSet.tokensOverlap`). A path is
        // bytes, not prose: the culture-sensitive default can match across ignorable characters.
        let sourceRoot =
            lane |> List.tryFind (fun r -> src.StartsWith(r + "/", System.StringComparison.Ordinal))

        match sourceRoot with
        | None -> None // not under a root of this kit lane: no mirror this rule can name
        | Some sr ->
            let name = src.Substring(sr.Length + 1)

            if name = "" || name.Contains "/" then
                None // a root itself, or a nested path: not a skill directory
            else
                // The OTHER root of the lane. Direction is a CONSEQUENCE of which root the registry
                // declared, so the mirror can never be copied back over its own source.
                match lane |> List.filter (fun r -> r <> sr) with
                | [ mirrorRoot ] -> Some(src, mirrorRoot + "/" + name)
                | _ -> None // no single opposite root — say nothing rather than guess

    let divergedRoots (roots: ('a * byte[] option * byte[] option) list) : 'a list =
        roots
        |> List.choose (fun (key, a, b) ->
            match a, b with
            | Some x, Some y when x = y -> None // byte-identical across both roots
            | _ -> Some key) // diverged, or one root is missing the mirror
