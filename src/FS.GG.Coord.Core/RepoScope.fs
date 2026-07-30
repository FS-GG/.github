namespace FS.GG.Coord

module RepoScope =

    let resolve (raw: string) : string =
        match raw.ToLowerInvariant() with
        | "sdd" -> "FS.GG.SDD"
        | "rendering" -> "FS.GG.Rendering"
        | "governance" -> "FS.GG.Governance"
        | "templates" -> "FS.GG.Templates"
        | "game" -> "FS.GG.Game"
        | "audio" -> "FS.GG.Audio"
        | "net" -> "FS.GG.Net"
        | _ ->
            match raw.IndexOf('/') with
            | -1 -> raw
            | i -> raw.Substring(i + 1)
