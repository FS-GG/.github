namespace FS.GG.Telemetry.Host

module NativeSourceVerification =
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
