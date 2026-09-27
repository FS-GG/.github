namespace FS.GG.Coord.Cli

/// Prospective, head-bound delivery receipt write. It makes no claim about older receipts.
module DeliveryReceiptProducer =
    type Request =
        { PullRequest: int; HeadSha: string; ObligationId: string; Evidence: string }

    type Ports =
        { CurrentHead: unit -> Result<string, string>
          Comments: unit -> Result<FS.GG.Coord.Driver.ReviewComment list, string>
          AuthorizeWrite: unit -> Result<unit, string>
          WriteDurableComment: string -> string -> Result<unit, string> }

    type Outcome =
        | Written
        | AlreadyPresent

    /// Fail closed over a supplied, complete PR comment census and durable comment writer.
    val produceWith: ports: Ports -> request: Request -> Result<Outcome, string>

    /// Bind one exact GitHub PR and the durable writer to caller-owned write authority.
    val produceLive:
        transport: FS.GG.Coord.GitHub.Transport.IGitHubTransport ->
        target: FS.GG.Coord.Types.Ref ->
        authorizeWrite: (unit -> Result<unit, string>) ->
        request: Request -> Result<Outcome, string>
