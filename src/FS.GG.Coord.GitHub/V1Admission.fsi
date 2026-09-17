namespace FS.GG.Coord.GitHub

open FS.GG.Coordination.GitHub

/// Producer-side adapters over the protected GS2-08.5 admission contract.
///
/// This module deliberately exposes no registry genesis operation. An ordinary producer can restore and
/// append an existing protected Operation journal, but cannot create the first commit.
module V1Admission =

    /// A CAS outcome reported by the durable journal implementation. `CompareAndSwapWon` is the only
    /// value translated to `ReceiveAccepted`.
    type JournalCompareAndSwapOutcome =
        | CompareAndSwapWon
        | CompareAndSwapParentConflict
        | CompareAndSwapRefused of reason: string
        | CompareAndSwapResponseUnknown

    /// Durable storage for the already-initialized protected admission journal.
    type DurableJournal =
        {
            Read: AggregateAddress -> RegistryJournalRead
            CompareAndSwap: RegistryAppendProposal -> JournalCompareAndSwapOutcome
        }

    /// Provider evidence kinds that can exclude the original request from arriving late.
    type ProviderAbsenceKind =
        | IdempotencyKeyExcluded
        | ConditionalFenceExcluded
        | OriginalRequestRetired

    /// Evidence returned by provider-specific reconciliation. Strong absence carries the digest of the
    /// exact original canonical request bytes; a mismatched digest is refused by `providerPort`.
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

    /// A mutation already admitted by the protected registry. Eligibility is recovered from durable
    /// authority; callers cannot supply an `eligible` flag.
    type Mutation =
        {
            OperationId: string
            Owner: string
            Request: MutationRequest
        }

    /// The callback is invoked only after this invocation durably wins the effect-intent CAS and consumes
    /// a fresh, single-use dispatch fence.
    type IMutationFence =
        abstract Dispatch<'response> : mutation: Mutation * send: (unit -> 'response) -> Result<'response, string list>

    /// Adapt a fresh two-read authority source to the imported verifier.
    val authorityPort:
        readObjects: (unit -> Result<AuthorityGitObjects, string>) ->
        rereadHead: (unit -> Result<GitObjectId, string>) ->
            AuthorityGitPort

    /// Adapt the durable journal. Only `CompareAndSwapWon` becomes `ReceiveAccepted`.
    val journalPort: DurableJournal -> RegistryJournalPort

    /// Adapt provider reconciliation while binding strong-absence evidence to the exact original bytes.
    val providerPort: ProviderReconciliation -> ProviderReconciliationPort

    /// The fixed protected Operation-journal address used by ordinary producers.
    val registryAddress: AggregateAddress

    /// Production mutation fence over existing durable authority and journal adapters.
    type DurableMutationFence =
        new: authority: AuthorityGitPort * journal: DurableJournal -> DurableMutationFence

        interface IMutationFence

        /// Reconcile an unresolved effect through provider-specific evidence and durably append its
        /// settlement. This is the recovery path after an unknown or interrupted provider response.
        member Reconcile:
            operationId: string * owner: string * effectId: string * provider: ProviderReconciliation ->
                Result<DurableAppendDecision, string list>
