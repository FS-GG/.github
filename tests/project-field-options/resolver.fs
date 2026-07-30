namespace FS.GG.Coord

module RepoScope =

    let resolve (raw: string) : string =
        match raw.ToLowerInvariant() with
        | "sdd" -> "FS.GG.SDD"
        | "net" -> "FS.GG.Net"
        | _ -> raw
