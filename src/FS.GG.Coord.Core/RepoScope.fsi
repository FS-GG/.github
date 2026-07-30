namespace FS.GG.Coord

/// Pure normalization for repository scope tokens shared by command input and board rows.
module RepoScope =

    /// Map roster short ids and owner/repo spellings to the canonical repository name.
    val resolve: raw: string -> string
