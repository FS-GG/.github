namespace FS.GG.Telemetry.Host

module internal NativeResponsesEntry =
    /// Pure outer declaration projection. No receipt admission or capture authority is created.
    val renderAssessmentFact:
        itemId: string -> operationId: string -> observedAt: string ->
        canonicalRequestBytes: byte array -> assessmentBytes: byte array -> Result<byte array, string list>

    /// Fixed installed route. The input supplies data, never a provider endpoint,
    /// credential value, captured response, success flag or source authority.
    val run:
        configPath: string -> config: HostConfig -> runtimeCredentialReference: string ->
        dispatchIdentity: string -> itemId: string -> requestPath: string -> Result<string, string list>
