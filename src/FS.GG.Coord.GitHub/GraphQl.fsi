namespace FS.GG.Coord.GitHub

/// The only production boundary allowed to open a GraphQL response envelope.
module GraphQl =
    open System.Text.Json
    open Errors
    open Transport

    type RetryClassification =
        | Retryable of RateLimitKind
        | NotRetryable

    type FailureMetadata =
        { Retry: RetryClassification
          RateLimit: (RateLimitResource * System.DateTimeOffset option) option }

    type DrainLimits =
        { MaxPages: int
          MaxItems: int }

    type Page<'item>

    val classify: IoError -> FailureMetadata

    /// Parse one response and run a domain decoder only after the envelope proved complete.
    val decode: subject: string -> body: string -> decoder: (JsonElement -> IoResult<'value>) -> IoResult<'value>

    /// Send one GraphQL request and return only the decoder's typed domain value.
    val read:
        transport: IGitHubTransport -> request: Request -> decoder: (JsonElement -> IoResult<'value>) -> IoResult<'value>

    /// Decode one Relay connection page. Raw pageInfo never leaves this module.
    val page:
        subject: string ->
        what: string ->
        key: ('item -> string) ->
        decodeNode: (JsonElement -> IoResult<'item>) ->
        connection: JsonElement ->
        IoResult<Page<'item>>

    /// Decode a `first: window` page. An omitted pageInfo is accepted only when the returned node count is
    /// below the window, which independently proves there was no truncated tail.
    val pageWithin:
        subject: string ->
        what: string ->
        window: int ->
        key: ('item -> string) ->
        decodeNode: (JsonElement -> IoResult<'item>) ->
        connection: JsonElement ->
        IoResult<Page<'item>>

    /// Drain a connection with bounded pages/items and fail on repeated cursors, duplicate identities,
    /// changing totalCount, empty continuing pages, or a final count that disagrees with totalCount.
    val drain:
        subject: string ->
        what: string ->
        limits: DrainLimits ->
        fetch: (string option -> IoResult<Page<'item>>) ->
        IoResult<'item list>

    /// Extract partial-mutation facts without exposing the mixed data/errors envelope.
    val partialMutation:
        subject: string -> body: string -> IoResult<string list * (string * string) list>
