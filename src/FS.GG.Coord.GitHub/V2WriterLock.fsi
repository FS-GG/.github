namespace FS.GG.Coord.GitHub

/// One writer lock for every board writer of a project (board-reconciler design §8, FS-GG/.github#5136, #5126).
///
/// Production path: `$XDG_RUNTIME_DIR/fsgg/board-writer-<projectId>.lock`, which does not follow `TMPDIR`, so two
/// writers with different temporary directories still exclude each other. During the transition a lease also
/// takes the legacy lock the installed engine still uses, at the literal `/tmp` path. Every path is opened as a raw
/// descriptor and locked with an explicit `flock(2)` `LOCK_EX | LOCK_NB`, the same call the Python writers make with
/// `fcntl.flock`. It does not rely on .NET's `FileShare` emulation, which an environment variable can disable; that
/// emulation also uses flock, so an installed engine opening the legacy path with `FileShare.None` is excluded too.
module V2WriterLock =
    /// The ordered paths one lease locks, all or none.
    [<Sealed>]
    type Location =
        member Paths: string list

    /// `board-writer-<projectId>.lock`.
    val fileName: projectId: string -> string
    /// The installed engine's legacy `migration-apply` lock name for Project 3.
    val migrationLegacyName: string
    /// The installed engine's legacy `refresh` lock name for a project.
    val observationLegacyName: projectId: string -> string
    /// Production location. Error when `XDG_RUNTIME_DIR` is unset or not absolute: a writer refuses rather than
    /// writing without the shared lock.
    val production: projectId: string -> legacyName: string -> Result<Location, string>
    /// A location with both paths inside `directory`, for tests and fixtures, which never open the production path.
    val inDirectory: directory: string -> projectId: string -> legacyName: string -> Location
    /// Take every path, in order, without waiting. `Error "lock-held"` when another holder has one; anything taken
    /// is released first. Disposing the lease unlocks and closes every path.
    val tryAcquire: location: Location -> Result<System.IDisposable, string>
    /// `tryAcquire`, raising `IOException` instead of returning an error, for callers whose failure path is an
    /// exception handler that already refuses before any send.
    val acquire: location: Location -> System.IDisposable
