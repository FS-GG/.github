namespace FS.GG.Coord

module SelfHost =
    [<RequireQualifiedAccess>]
    type BootstrapReason =
        | NewSchemaCase
        | RelocatedDecisionBoundary

    type Evidence =
        { Build: string
          Unit: string
          FocusedProductionRoute: string
          Provenance: string
          Inversion: string }

    type HostAcceptance =
        { Actor: string
          AcceptedAt: System.DateTimeOffset }

    type SelfHostBootstrapReceipt =
        { BaseSha: string
          CandidateHeadSha: string
          CandidateBinarySha256: string
          CandidateVersion: string
          SharedRefusal: string
          SnapshotSha256: string
          Reason: BootstrapReason
          Evidence: Evidence
          CandidateDecisionKey: string
          CandidateActionKey: string
          HostAcceptance: HostAcceptance
          Digest: string }

    type Replay =
        { DecisionKey: string
          ActionKey: string }

    type SelfHostReplayReceipt =
        { BootstrapDigest: string
          SnapshotSha256: string
          DecisionKey: string
          ActionKey: string
          ReplayedAt: System.DateTimeOffset
          Digest: string }

    type ReplayState =
        | NoBootstrap
        | ReplayRequired of SelfHostBootstrapReceipt
        | VerifiedReplay of SelfHostReplayReceipt
        | InvalidReplay of errors: string list

    [<Literal>]
    val ReceiptMarker: string = "<!-- fsgg:self-host-bootstrap/v1 -->"

    [<Literal>]
    val ReplayReceiptMarker: string = "<!-- fsgg:self-host-replay/v1 -->"

    val createReceipt:
        baseSha: string ->
        candidateHeadSha: string ->
        candidateBinarySha256: string ->
        candidateVersion: string ->
        sharedRefusal: string ->
        snapshotSha256: string ->
        reason: BootstrapReason ->
        evidence: Evidence ->
        candidateDecisionKey: string ->
        candidateActionKey: string ->
        hostAcceptance: HostAcceptance ->
            Result<SelfHostBootstrapReceipt, string list>

    /// The stable shared verifier. A host write must receive Ok from this function immediately before IO.
    val authorizeWrite: receipt: SelfHostBootstrapReceipt -> Result<unit, string list>

    /// Post-merge shared-engine replay must reproduce the candidate decision and action keys.
    val verifyReplay: receipt: SelfHostBootstrapReceipt -> replay: Replay -> Result<unit, string list>

    val createReplayReceipt:
        bootstrap: SelfHostBootstrapReceipt ->
        snapshotSha256: string ->
        replay: Replay ->
        replayedAt: System.DateTimeOffset ->
            Result<SelfHostReplayReceipt, string list>

    val verifyReplayReceipt:
        bootstrap: SelfHostBootstrapReceipt -> receipt: SelfHostReplayReceipt -> Result<unit, string list>

    val encodeReceipt: receipt: SelfHostBootstrapReceipt -> string
    val tryDecodeReceipt: body: string -> Result<SelfHostBootstrapReceipt option, string list>
    val encodeReplayReceipt: receipt: SelfHostReplayReceipt -> string
    val tryDecodeReplayReceipt: body: string -> Result<SelfHostReplayReceipt option, string list>
    val replayState: comments: string list -> ReplayState
