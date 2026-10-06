namespace FS.GG.Coord


/// Pure fixed Responses request/decoder contract. No transport or admission authority.
module NativeResponses =
    [<Literal>]
    val Model: string = "gpt-6.1-sol"
    [<Literal>]
    val Effort: string = "medium"
    [<Literal>]
    val MaximumInputTokens: int64 = 8000L
    [<Literal>]
    val MaximumOutputTokens: int64 = 1500L

    type Request =
        { Instructions: string
          InputText: string
          SchemaName: string
          SchemaJson: byte array }

    /// Copy-on-read exact bytes; lowercase bare SHA256 digests.
    [<Class>]
    type FrozenRequest =
        member CountBody: byte array
        member GenerationBody: byte array
        member CountSha256: string
        member GenerationSha256: string
        member SharedFieldsSha256: string

    /// Pure count acceptance, not a credential/queue/provider authority.
    [<Class>]
    type CountAdmission =
        member InputTokens: int64
        member GenerationSha256: string
        member CountRequestSha256: string
        member CountResponseSha256: string

    /// Opaque provider string, never a local thread/turn/Guid/invocation identity.
    [<Class>]
    type ProviderResponseId =
        member Value: string

    type ResponseStatus =
        | Completed | Incomplete | Failed | Cancelled | InProgress | Queued
        | UnknownStatus of string option
    type UsageState = Unknown | Partial | Complete
    type UsageObservation =
        { State: UsageState
          InputTokens: int64 option
          OutputTokens: int64 option
          TotalTokens: int64 option
          CachedInputTokens: int64 option
          CacheWriteInputTokens: int64 option
          ReasoningOutputTokens: int64 option
          Issues: string list }
    type ResponseObservation =
        { ResponseId: ProviderResponseId option
          ObservedModel: string option
          ProviderCreatedAt: System.DateTimeOffset option
          Status: ResponseStatus
          Usage: UsageObservation
          OutputText: string option
          Refusal: string option
          FailureCode: string option
          IncompleteReason: string option
          Issues: string list }

    /// Freezes nine shared fields including schema, and fixed no-tools/inclusive1500 policies.
    val freeze: request: Request -> Result<FrozenRequest, string list>
    /// Strict bounded JSON count1..8000 joined to immutable request digests.
    val admitCount: request: FrozenRequest -> responseBytes: byte array -> Result<CountAdmission, string list>
    /// Duplicate-free bounded JSON, preserving missing/partial usage and incomplete status.
    val decodeResponse: responseBytes: byte array -> Result<ResponseObservation, string list>
    /// Text only for complete count-corresponding bounded usage and exact selected observed model.
    val validateCompletion: count: CountAdmission -> response: ResponseObservation -> Result<string, string list>
