namespace FS.GG.Coord.GitHub

open System
open FS.GG.Coord.GitHub.ProtectedIntakeAuthority

/// Deterministic, parentless object plan for the retained intake journal genesis. This module can
/// describe and verify receive-pack inputs, but deliberately exposes no credential or write port.
module ProtectedIntakeGenesis =
    [<Literal>]
    val Schema: string = "fsgg.github.protected-intake-genesis-plan/1"
    [<Literal>]
    val OperationId: string = "preinstalled-empty-genesis"

    type Plan =
        { Ref: string
          ExpectedOldObjectId: string option
          ProposedObjectId: string
          Refspec: string
          ForceWithLease: string
          Proposal: Proposal }

    type ReadbackFailure =
        | PlanDrift
        | ExpectedAbsentRefNotAbsent
        | RefReadbackIndeterminate
        | ResultingRefMismatch
        | ObjectCustodyMismatch

    type ReadbackConfirmed = private ReadbackConfirmed of commitOid: string

    type AuthorizationFailure =
        | IntakeAuthorityRefused of ProtectedIntakeAuthority.Failure list
        | GenesisPolicyMismatch
        | GenesisReadbackRefused of ReadbackFailure list

    type ValidatedGenesis = private ValidatedGenesis of commitOid: string

    /// Produces the sole canonical operation/13 genesis proposal. The expected-old value is absent,
    /// represented to receive-pack as `--force-with-lease=<ref>:`.
    val plan: unit -> Plan

    /// Classifies only complete exact readback. It does not authorize or perform a write.
    val validateReadback:
        Plan ->
        before: RefObservation ->
        after: RefObservation ->
        objects: ObjectReadback ->
            Result<ReadbackConfirmed, ReadbackFailure list>

    /// Requires the separate INTAKE-A policy, native approval, credential and protected readback
    /// validator in addition to the deterministic genesis and exact object-custody checks.
    val validateAuthorized:
        now: DateTimeOffset ->
        Plan ->
        Policy ->
        NativeAuthorization ->
        ProtectedReadback ->
            Result<ValidatedGenesis, AuthorizationFailure list>
