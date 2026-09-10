namespace FS.GG.Coord.GitHub

module BoardIntake =
    type Admission =
        { RepositoryId: string
          IssueId: string
          UpdatedAt: string
          AuthorId: string
          AuthorLogin: string
          Permission: string }

    type Gate =
        new: transport: Transport.IGitHubTransport * ?ttl: System.TimeSpan * ?cacheRoot: string * ?clock: (unit -> System.DateTimeOffset) -> Gate
        member Authorize: owner: string * repo: string * number: int -> Errors.IoResult<Admission>
