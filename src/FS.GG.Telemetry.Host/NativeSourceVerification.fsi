namespace FS.GG.Telemetry.Host

module NativeSourceVerification =
    val internal validateResponsesManifest: verifier: NativeVerifierConfig -> bool
    type Limits =
        {
            Timeout: System.TimeSpan
            StdoutBytes: int
            StderrBytes: int
        }

    val verifyRetainedWithLimits:
        limits: Limits ->
        verifier: NativeVerifierConfig ->
        evidenceRoot: string ->
        captureBytes: byte array ->
        snapshotBytes: byte array ->
        retainedVerificationBytes: byte array ->
            Result<unit, string list>

    val verifyRetained:
        verifier: NativeVerifierConfig ->
        evidenceRoot: string ->
        captureBytes: byte array ->
        snapshotBytes: byte array ->
        retainedVerificationBytes: byte array ->
            Result<unit, string list>

    /// Reuses the installed verifier process boundary inside the original Responses phase.
    /// A negative replay is returned as actual bytes; unproved retirement is an error.
    val internal verifyResponses:
        phase: FS.GG.Telemetry.DirectResponses.Phase -> verifier: NativeVerifierConfig -> evidenceRoot: string ->
        captureBytes: byte array -> snapshotBytes: byte array -> Result<byte array, string list>
