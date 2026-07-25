namespace FS.GG.Coord.Cli

open FS.GG.Coord.GitHub

/// Pure application service for the `ready` command family.
///
/// The service owns row selection; transport reads and presentation remain at the CLI edge.
module ReadyApplication =

    /// Select the board rows exposed by `ready`.
    ///
    /// Pull requests are never work items. `status` matches the board column case-insensitively;
    /// otherwise `all = false` excludes only Done.
    val select: repo: string option -> status: string option -> all: bool -> rows: Scan.Row list -> Scan.Scoped
