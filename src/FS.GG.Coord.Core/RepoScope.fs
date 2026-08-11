namespace FS.GG.Coord

module RepoScope =

    type Scope =
        | Repository of name: string
        | NonRepository of token: string

    let resolve (raw: string) : Scope =
        match raw.ToLowerInvariant() with
        | "sdd" -> Repository "FS.GG.SDD"
        | "rendering" -> Repository "FS.GG.Rendering"
        | "governance" -> Repository "FS.GG.Governance"
        | "templates" -> Repository "FS.GG.Templates"
        | "game" -> Repository "FS.GG.Game"
        | "audio" -> Repository "FS.GG.Audio"
        | "net" -> Repository "FS.GG.Net"
        | "sir" -> Repository "S.I.R."
        // THE ONE DELIBERATE NON-ROSTER VALUE (`docs/coordination/board-schema.md`). Matched on the
        // lowercased token, same as every roster spelling above, but tagged `NonRepository` rather than
        // returned as a bare string — that tag is the whole fix (#2398): nothing downstream can compare
        // it against a repository, reserve a path under it, or select a claim lock with it without first
        // admitting, in a match arm the compiler checks, that this is not one.
        | "cross-repo" -> NonRepository raw
        | _ ->
            match raw.IndexOf('/') with
            | -1 -> Repository raw
            | i -> Repository(raw.Substring(i + 1))

    let orFallback (fallback: string) (scope: Scope) : string =
        match scope with
        | Repository name -> name
        | NonRepository _ -> fallback
