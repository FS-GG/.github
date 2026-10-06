namespace FS.GG.Telemetry.Host

open System.Threading
open System.Threading.Tasks
open FS.GG.Coord

/// Host-owned direct provider composition. No process/thread custody is implied.
module internal NativeResponsesCollection =
    type OriginEvidence =
        { Principal: TelemetryReceipt.Principal
          ManagerReceiptSha256: string
          CapabilityProfileSha256: string
          CapabilityResultSha256: string
          InstallationSha256: string
          ObservedAt: string
          ExpiresAt: string }
    type OperationEvidence =
        { OperationId: string
          ItemId: string
          OriginalItemId: string
          InvocationId: string
          DispatchRef: byte array
          ClaimRef: byte array
          GenerationRequestSha256: string
          CountRequestSha256: string
          CountResponseSha256: string
          ResponseSha256: string
          ResponseBytes: int
          CaptureSha256: string
          VerificationSha256: string
          ObservedAt: string }

    type Failure = { Errors: string list; ClaimAttemptId: string option; ClaimReceipt: string option }

    [<Class>]
    type Capture =
        member Phase: FS.GG.Telemetry.DirectResponses.Phase
        member Bytes: byte array
        member Sha256: string
        member SnapshotBytes: byte array
        member Response: NativeResponses.ResponseObservation option
        member Failure: string list
        member ClaimReceipt: string

    /// Static installed proof and current collector eligibility precede the exact queue CAS.
    /// The returned capture is observation only: independent verification and atomic
    /// native receipt/attachment/settlement must still succeed under the original phase.
    val collect:
        hostConfigPath: string -> hostConfig: HostConfig -> storeRoot: string ->
        runtimePrincipal: TelemetryReceipt.Principal -> dispatchIdentity: string -> itemId: string ->
        claimTemplate: byte array -> request: NativeResponses.Request ->
        cancellationToken: CancellationToken -> Task<Result<Capture, Failure>>

    [<Class>]
    type VerifiedCapture =
        member Origin: OriginEvidence
        member Operation: OperationEvidence
        member Observation: NativeResponses.ResponseObservation
        member CompletionAccepted: bool

    val verify:
        hostConfigPath: string -> hostConfig: HostConfig -> storeRoot: string ->
        capture: Capture -> Result<VerifiedCapture, Failure>
