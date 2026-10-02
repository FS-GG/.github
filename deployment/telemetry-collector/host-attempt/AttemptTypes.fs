namespace Fs.Gg.Telemetry.HostAttempt

open System

exception AttemptRefusal of string

[<RequireQualifiedAccess>]
type Phase =
    | Unprepared
    | Prepared
    | PlacementObserved
    | SecretPlacementPending
    | SecretsObserved
    | DispatchPending
    | Discovery
    | Watching
    | CleanupPending
    | Finalized
    | Refused
    | Unknown

[<RequireQualifiedAccess>]
type NativeDisposition = NativeUnknown | NativeAccepted | NativeRefused

[<RequireQualifiedAccess>]
type CleanupDisposition = CleanupUnknown | CleanupPending | SecretsAbsent

[<RequireQualifiedAccess>]
type SecretRole = NativeAuth | EffectAdmission

[<RequireQualifiedAccess>]
type FixedAction =
    | ReadPublicIdentity
    | RequestBranchFastForward
    | InspectAuthMetadata
    | InvokeBinding
    | TransferSecret of SecretRole
    | DispatchOnce
    | ListRuns
    | GetRun of string
    | CancelOwnedRun of string
    | DeleteSecret of SecretRole
    | ReadSecretAbsence of SecretRole
    | DownloadRunArtifacts of string
    | EmitRootReadback

type PreparedIdentity =
    { SourceGeneration: int
      PlacementSha: string
      PlacementTree: string
      WorkflowSha256: string
      RecipeSourceSha: string
      RecipeSourceTree: string
      ProfileSha256: string
      OperationId: string
      BindingSha256: string
      BindingProducerSha256: string
      SourcePinsSha256: string
      ProducerSha256: string
      RuntimeHostSha256: string
      MechanismAdapterSha256: string
      Nonce: string
      DestinationId: string }

type AttemptState =
    { Schema: string
      Phase: Phase
      Prepared: PreparedIdentity
      CurrentSourceGeneration: int
      CurrentBindingSha256: string
      ElapsedSeconds: int
      BudgetSeconds: int
      DispatchIntended: bool
      DispatchMayHaveEffect: bool
      DispatchAcknowledged: bool
      EffectCheckFresh: bool
      SecretIntentions: Set<SecretRole>
      SecretAcknowledgments: Set<SecretRole>
      SecretsMayHaveEffect: Set<SecretRole>
      SecretAbsenceObserved: Set<SecretRole>
      CandidateRuns: string list
      OwnedRunId: string option
      CancellationMayHaveEffect: bool
      RunRetirementObserved: bool
      NativeDisposition: NativeDisposition
      CleanupDisposition: CleanupDisposition
      Refusal: string option }

type NativeEvidence =
    { RunId: string
      Repository: string
      Workflow: string
      QualificationRef: string
      PlacementSha: string
      Nonce: string
      ProfileSha256: string
      OperationId: string
      BindingSha256: string
      EvidenceComplete: bool
      NonceJoined: bool
      ProfileVerified: bool
      ActivityComplete: bool
      CompletionCommunicated: bool
      ChildAck: bool
      ParentAck: bool
      WriterJoinsHealthy: bool }

type RunListing =
    { Repository: string
      Workflow: string
      QualificationRef: string
      PlacementSha: string
      PriorRunIds: string list
      CandidateRunIds: string list
      ListingComplete: bool }

type OwnedRunEvidence =
    { Repository: string
      Workflow: string
      QualificationRef: string
      PlacementSha: string
      Nonce: string
      RunId: string
      RootOwnershipObserved: bool }

type RunRetirementEvidence =
    { Repository: string
      Workflow: string
      QualificationRef: string
      PlacementSha: string
      Nonce: string
      RunId: string
      RootOwnershipObserved: bool
      HttpStatus: int
      ObservationComplete: bool
      RunActive: bool }

type SecretAbsenceEvidence =
    { Role: SecretRole
      Repository: string
      Environment: string
      HttpStatus: int
      ListingComplete: bool
      SecretPresent: bool }

type EffectCheckEvidence =
    { SourceGeneration: int
      BindingSha256: string
      ProfileSha256: string
      WorkflowSha256: string
      BindingProducerSha256: string
      RuntimeHostSha256: string
      MechanismAdapterSha256: string }

[<RequireQualifiedAccess>]
type OperationFailure = MechanismFailure

[<RequireQualifiedAccess>]
type Observation =
    | SourceRevalidated of generation: int * bindingSha256: string * profileSha256: string * workflowSha256: string
    | PlacementObserved
    | EffectCheckObserved of EffectCheckEvidence
    | EffectRequested of FixedAction
    | EffectAcknowledged of FixedAction
    | EffectResponseLost of FixedAction
    | RunsObserved of RunListing
    | OwnedRunObserved of OwnedRunEvidence
    | OwnedRunRetired of RunRetirementEvidence
    | NativeEvidenceObserved of NativeEvidence
    | SecretAbsenceObserved of SecretAbsenceEvidence
    | CleanupCallFailed
    | OperationFailed of OperationFailure
    | TimeAdvanced of seconds: int
    | SourceInvalidated

type Reduction = { State: AttemptState; Actions: FixedAction list }

module ClosedNames =
    let phase = function
        | Phase.Unprepared -> "unprepared" | Phase.Prepared -> "prepared"
        | Phase.PlacementObserved -> "placement-observed" | Phase.SecretPlacementPending -> "secret-placement-pending"
        | Phase.SecretsObserved -> "secrets-observed" | Phase.DispatchPending -> "dispatch-pending"
        | Phase.Discovery -> "discovery" | Phase.Watching -> "watching"
        | Phase.CleanupPending -> "cleanup-pending" | Phase.Finalized -> "finalized"
        | Phase.Refused -> "refused" | Phase.Unknown -> "unknown"

    let native = function NativeDisposition.NativeUnknown -> "unknown" | NativeDisposition.NativeAccepted -> "accepted" | NativeDisposition.NativeRefused -> "refused"
    let cleanup = function CleanupDisposition.CleanupUnknown -> "unknown" | CleanupDisposition.CleanupPending -> "pending" | CleanupDisposition.SecretsAbsent -> "secrets-absent"
    let secret = function SecretRole.NativeAuth -> "native-auth" | SecretRole.EffectAdmission -> "effect-admission"
    let secretOf = function "native-auth" -> SecretRole.NativeAuth | "effect-admission" -> SecretRole.EffectAdmission | _ -> raise (AttemptRefusal "secret-role-refused")

    let action = function
        | FixedAction.ReadPublicIdentity -> "read-public-identity", None
        | FixedAction.RequestBranchFastForward -> "request-branch-fast-forward", None
        | FixedAction.InspectAuthMetadata -> "inspect-auth-metadata", None
        | FixedAction.InvokeBinding -> "invoke-binding", None
        | FixedAction.TransferSecret role -> "transfer-secret", Some(secret role)
        | FixedAction.DispatchOnce -> "dispatch-once", None
        | FixedAction.ListRuns -> "list-runs", None
        | FixedAction.GetRun run -> "get-run", Some run
        | FixedAction.CancelOwnedRun run -> "cancel-owned-run", Some run
        | FixedAction.DeleteSecret role -> "delete-secret", Some(secret role)
        | FixedAction.ReadSecretAbsence role -> "read-secret-absence", Some(secret role)
        | FixedAction.DownloadRunArtifacts run -> "download-run-artifacts", Some run
        | FixedAction.EmitRootReadback -> "emit-root-readback", None

    let actionOf name argument =
        match name, argument with
        | "read-public-identity", None -> FixedAction.ReadPublicIdentity
        | "request-branch-fast-forward", None -> FixedAction.RequestBranchFastForward
        | "inspect-auth-metadata", None -> FixedAction.InspectAuthMetadata
        | "invoke-binding", None -> FixedAction.InvokeBinding
        | "transfer-secret", Some role -> FixedAction.TransferSecret(secretOf role)
        | "dispatch-once", None -> FixedAction.DispatchOnce
        | "list-runs", None -> FixedAction.ListRuns
        | "get-run", Some run when run.Length > 0 && run.Length <= 32 -> FixedAction.GetRun run
        | "cancel-owned-run", Some run when run.Length > 0 && run.Length <= 32 -> FixedAction.CancelOwnedRun run
        | "delete-secret", Some role -> FixedAction.DeleteSecret(secretOf role)
        | "read-secret-absence", Some role -> FixedAction.ReadSecretAbsence(secretOf role)
        | "download-run-artifacts", Some run when run.Length > 0 && run.Length <= 32 -> FixedAction.DownloadRunArtifacts run
        | "emit-root-readback", None -> FixedAction.EmitRootReadback
        | _ -> raise (AttemptRefusal "action-refused")
