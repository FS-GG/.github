let resolveRepo (raw: string) : string =
    match raw.ToLowerInvariant() with
    | "sdd" -> "FS.GG.SDD"
    | "net" -> "FS.GG.Net"
    | _ -> raw

/// An owner + repo fixture boundary.
