namespace FS.GG.Coord.GitHub

module BoardIntake =
    type Admission =
        {
            RepositoryId: string
            IssueId: string
            UpdatedAt: string
            AuthorId: string
            AuthorLogin: string
            Permission: string
        }

    /// persistCache=false disables both disk reads and writes; one invocation still shares
    /// bounded in-memory policy/permission facts and freshly reads every issue identity. Default is true.
    type Gate =
        new:
            transport: Transport.IGitHubTransport *
            ?ttl: System.TimeSpan *
            ?cacheRoot: string *
            ?clock: (unit -> System.DateTimeOffset) *
            ?persistCache: bool ->
                Gate

        member Authorize: owner: string * repo: string * number: int -> Errors.IoResult<Admission>
