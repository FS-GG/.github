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

    let divergedRoots (roots: (string * byte[] option * byte[] option) list) : string list =
        roots
        |> List.choose (fun (name, a, b) ->
            match a, b with
            | Some x, Some y when x = y -> None // byte-identical across both roots
            | _ -> Some name) // diverged, or one root is missing the mirror
