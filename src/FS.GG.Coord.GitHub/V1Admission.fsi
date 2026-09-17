namespace FS.GG.Coord.GitHub

open FS.GG.Coordination.GitHub

/// Producer-side adapters over the protected GS2-08.5 admission contract.
///
/// This module deliberately exposes no registry genesis operation and no caller-authored eligibility
/// switch. An ordinary producer can bind an existing admitted operation and append its protected journal.
module V1Admission =

    type JournalCompareAndSwapOutcome =
        | CompareAndSwapWon
        | CompareAndSwapParentConflict
        | CompareAndSwapRefused of reason: string
        | CompareAndSwapResponseUnknown

    type DurableJournal =
        {
            Read: AggregateAddress -> RegistryJournalRead
            CompareAndSwap: RegistryAppendProposal -> JournalCompareAndSwapOutcome
        }

    /// A fresh two-read source for the protected fleet authority.
    type DurableAuthority =
        {
            ReadObjects: unit -> Result<AuthorityGitObjects, string>
            RereadHead: unit -> Result<GitObjectId, string>
        }

    type ProviderAbsenceKind =
        | IdempotencyKeyExcluded
        | ConditionalFenceExcluded
        | OriginalRequestRetired

    type ProviderEvidence =
        | Applied of responseDigest: Sha256Digest
        | StronglyAbsent of
            kind: ProviderAbsenceKind *
            originalRequestDigest: Sha256Digest *
            evidenceDigest: Sha256Digest
        | Partial of reason: string
        | Indeterminate of reason: string

    type ProviderReconciliation =
        {
            Read: string -> string -> int64 -> byte array -> Result<ProviderEvidence, string>
        }

    /// Stable operation identity installed once when composing an operation-scoped transport. Per-call
    /// business code supplies only an effect id and provider request.
    type OperationScope

    val operationScope:
        operationId: string ->
        owner: string ->
        operationGeneration: int64 ->
        expectedClaimGeneration: int64 option ->
            Result<OperationScope, string list>

    /// One operation-scoped lifecycle. Dispatch includes durable intent, fresh permit, one provider attempt,
    /// provider evidence, and durable Applied settlement. Retry reads the persisted original bytes and is
    /// available only after provider-backed ProvenAbsent settlement.
    type IMutationFence =
        abstract Dispatch<'response, 'providerError> :
            effectId: string *
            canonicalRequestBytes: byte array *
            send: (unit -> Result<'response, 'providerError>) *
            responseEvidence: ('response -> ProviderEvidence) ->
                Result<Result<'response, 'providerError>, string list>

        abstract Reconcile: effectId: string * provider: ProviderReconciliation -> Result<unit, string list>

        abstract RetryProvenAbsent<'response, 'providerError> :
            effectId: string *
            send: (byte array -> Result<'response, 'providerError>) *
            responseEvidence: ('response -> ProviderEvidence) ->
                Result<Result<'response, 'providerError>, string list>

    val authorityPort:
        readObjects: (unit -> Result<AuthorityGitObjects, string>) ->
        rereadHead: (unit -> Result<GitObjectId, string>) ->
            DurableAuthority

    /// Only `CompareAndSwapWon` is translated to `ReceiveAccepted`.
    val journalPort: DurableJournal -> RegistryJournalPort

    /// Strong-absence evidence is accepted only when it binds the exact persisted original request bytes.
    val providerPort: ProviderReconciliation -> ProviderReconciliationPort

    val registryAddress: AggregateAddress

    type DurableMutationFence =
        new: authority: DurableAuthority * journal: DurableJournal * operation: OperationScope -> DurableMutationFence

        interface IMutationFence
