namespace FS.GG.Telemetry.Host

module internal NativeResponsesEntry =
    /// Fixed installed route. The input supplies data, never a provider endpoint,
    /// credential value, captured response, success flag or source authority.
    val run:
        configPath: string -> config: HostConfig -> runtimeCredentialReference: string ->
        dispatchIdentity: string -> itemId: string -> requestPath: string -> Result<string, string list>
