namespace FS.GG.Telemetry.Host

open System.Threading
open System.Threading.Tasks
open FS.GG.Coord

/// Host-owned direct provider composition. No process/thread custody is implied.
module NativeResponsesCollection =
    type internal OriginEvidence =
        { Principal: TelemetryReceipt.Principal
          ManagerReceiptSha256: string
          CapabilityProfileSha256: string
          CapabilityResultSha256: string
          InstallationSha256: string
          ObservedAt: string
          ExpiresAt: string }
    type internal OperationEvidence =
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

    type internal Failure = { Errors: string list; ClaimAttemptId: string option; ClaimReceipt: string option }

    /// Pure preclaim byte gate. Returns the exact input only when it is a valid
    /// retained evidence packet in the existing codec; never grants authority.
    val internal validateInputPacket: inputText: string -> Result<byte array, string list>

    /// Opaque installed state used only by the Host-owned capture constructor.
    type internal Installed

    [<Class>]
    type Capture =
        private new:
            phase: FS.GG.Telemetry.DirectResponses.Phase * bytes: byte array * snapshot: byte array *
            observed: NativeResponses.ResponseObservation option * failures: string list * claimReceipt: string *
            selected: Installed * operation: OperationEvidence * expiresAt: string -> Capture
        member internal Phase: FS.GG.Telemetry.DirectResponses.Phase
        member internal Bytes: byte array
        member internal Sha256: string
        member internal SnapshotBytes: byte array
        member internal Response: NativeResponses.ResponseObservation option
        member internal Failure: string list
        member internal ClaimReceipt: string

    /// Static installed proof and current collector eligibility precede the exact queue CAS.
    /// The returned capture is observation only: independent verification and atomic
    /// native receipt/attachment/settlement must still succeed under the original phase.
    val internal collect:
        hostConfigPath: string -> hostConfig: HostConfig -> storeRoot: string ->
        runtimePrincipal: TelemetryReceipt.Principal -> dispatchIdentity: string -> itemId: string ->
        claimTemplate: byte array -> request: NativeResponses.Request ->
        cancellationToken: CancellationToken -> Task<Result<Capture, Failure>>

    [<Class>]
    type VerifiedCapture =
        private new:
            origin: OriginEvidence * operation: OperationEvidence *
            observation: NativeResponses.ResponseObservation * accepted: bool -> VerifiedCapture
        member internal Origin: OriginEvidence
        member internal Operation: OperationEvidence
        member internal Observation: NativeResponses.ResponseObservation
        member internal CompletionAccepted: bool

    /// This operation accepts only the opaque capture created by the actual HTTP owner.
    /// Raw files, supplied flags and verifier exit status cannot construct VerifiedCapture.
    val internal verify:
        hostConfigPath: string -> hostConfig: HostConfig -> storeRoot: string ->
        capture: Capture -> Result<VerifiedCapture, Failure>
