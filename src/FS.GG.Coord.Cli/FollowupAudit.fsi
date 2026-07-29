namespace FS.GG.Coord.Cli

/// The local, worker-keyed fact `done` needs after it has completed an item.
module FollowupAudit =

    open FS.GG.Coord.Cli.Identity

    /// A completed worker's queue is either absent, still owed, or could not be read.
    type Outcome =
        | Empty
        | Owed of count: int
        | Unreadable of why: string

    /// The per-worker queue path, refusing an id that cannot safely key a file.
    val path: worker: Worker -> Result<string, string>

    /// Read the queue without consuming or rewriting it.
    val inspect: worker: Worker -> Outcome
