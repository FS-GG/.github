namespace FS.GG.Coord.GitHub

open System
open System.Security.Cryptography
open FS.GG.Coordination.GitHub

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

    type Mutation =
        {
            OperationId: string
            Owner: string
            Request: MutationRequest
        }

    type IMutationFence =
        abstract Dispatch<'response> : mutation: Mutation * send: (unit -> 'response) -> Result<'response, string list>

    let authorityPort readObjects rereadHead : AuthorityGitPort =
        {
            ReadObjects = readObjects
            RereadHead = rereadHead
        }

    let journalPort (journal: DurableJournal) : RegistryJournalPort =
        {
            Read = journal.Read
            Write =
                fun proposal ->
                    match journal.CompareAndSwap proposal with
                    | CompareAndSwapWon -> ReceiveAccepted
                    | CompareAndSwapParentConflict -> ReceiveParentConflict
                    | CompareAndSwapRefused reason -> ReceiveDefiniteRefusal reason
                    | CompareAndSwapResponseUnknown -> ReceiveResponseUnknown
        }

    let private requestDigest (bytes: byte array) =
        if obj.ReferenceEquals(bytes, null) then
            Error "provider-request-bytes"
        else
            SHA256.HashData bytes
            |> Convert.ToHexString
            |> _.ToLowerInvariant()
            |> V1AdmissionRegistry.sha256Digest

    let providerPort (provider: ProviderReconciliation) : ProviderReconciliationPort =
        {
            Read =
                fun operationId effectId attempt bytes ->
                    provider.Read operationId effectId attempt (Array.copy bytes)
                    |> Result.bind (fun evidence ->
                        match evidence with
                        | Applied digest -> Ok(ProviderApplied digest)
                        | Partial reason -> Ok(ProviderPartial reason)
                        | Indeterminate reason -> Ok(ProviderIndeterminate reason)
                        | StronglyAbsent(kind, original, proof) ->
                            requestDigest bytes
                            |> Result.bind (fun expected ->
                                if original <> expected then
                                    Error "strong-absence-does-not-bind-original-request"
                                else
                                    match kind with
                                    | IdempotencyKeyExcluded ->
                                        Ok(ProviderStronglyAbsent(ProviderIdempotencyExclusion(original, proof)))
                                    | ConditionalFenceExcluded ->
                                        Ok(ProviderStronglyAbsent(ConditionalFenceExclusion(original, proof)))
                                    | OriginalRequestRetired ->
                                        Ok(ProviderStronglyAbsent(OriginalRequestRetirement(original, proof)))))
        }

    let registryAddress =
        ShardedJournalAdapter.address Operation "fleet-v1-admission:fs-gg-production"
        |> Result.defaultWith (string >> invalidOp)

    let private commandId action operationId effectId generation =
        $"producer:%s{action}:%s{operationId}:%s{effectId}:%d{generation}"

    let private readRegistry (journal: DurableJournal) =
        let observed = journal.Read registryAddress

        V1AdmissionRegistry.restore observed
        |> Result.map (fun registry -> observed, registry)

    type DurableMutationFence(authority: AuthorityGitPort, journal: DurableJournal) =
        let port = journalPort journal

        let dispatch (mutation: Mutation) send =
            readRegistry journal
            |> Result.bind (fun (observed, registry) ->
                V1AdmissionRegistry.recoverOperation mutation.OperationId registry
                |> Result.bind (fun handle ->
                    V1AdmissionRegistry.readVerified authority
                    |> Result.bind (fun snapshot ->
                        match
                            V1AdmissionRegistry.prepareEffect
                                (V1AdmissionRegistry.head registry)
                                snapshot
                                handle
                                mutation.Owner
                                (MutationRequest mutation.Request)
                                registry
                        with
                        | EffectAlreadyInFlight owner -> Error [ "effect-already-in-flight:" + owner ]
                        | EffectAlreadySettled _ -> Error [ "effect-already-settled" ]
                        | EffectRefused reasons -> Error reasons
                        | EffectIntentAppended candidate ->
                            V1AdmissionRegistry.planAppend
                                (commandId
                                    "intent"
                                    mutation.OperationId
                                    mutation.Request.EffectId
                                    (V1AdmissionRegistry.generation candidate))
                                observed
                                candidate
                            |> Result.bind (fun proposal ->
                                match V1AdmissionRegistry.appendAndReconcile port proposal with
                                | DurableAppendAccepted(confirmed, Some permit) ->
                                    V1AdmissionRegistry.refreshDispatch
                                        port
                                        authority
                                        permit
                                        handle
                                        mutation.Owner
                                        mutation.Request.EffectId
                                        confirmed
                                    |> Result.bind (fun fence ->
                                        match
                                            V1AdmissionRegistry.authorizeDispatch
                                                port
                                                authority
                                                fence
                                                handle
                                                mutation.Owner
                                                mutation.Request.EffectId
                                                confirmed
                                        with
                                        | DispatchAuthorized -> Ok(send ())
                                        | DispatchRefused reasons -> Error reasons)
                                | DurableAppendAccepted(_, None) ->
                                    Error [ "effect-intent-not-won-by-this-invocation" ]
                                | DurableAppendParentConflict _ -> Error [ "effect-intent-parent-conflict" ]
                                | DurableAppendRefused(reason, _) -> Error [ "effect-intent-refused:" + reason ]
                                | DurableAppendIndeterminate(reasons, _) -> Error reasons))))

        let reconcile operationId owner effectId (provider: ProviderReconciliation) =
            readRegistry journal
            |> Result.bind (fun (observed, registry) ->
                V1AdmissionRegistry.recoverOperation operationId registry
                |> Result.bind (fun handle ->
                    V1AdmissionRegistry.reconcileEffect (providerPort provider) handle effectId registry
                    |> Result.bind (fun proof ->
                        match
                            V1AdmissionRegistry.settleEffect
                                (V1AdmissionRegistry.head registry)
                                owner
                                effectId
                                proof
                                registry
                        with
                        | RegistryRefused reasons -> Error reasons
                        | RegistryAppended candidate ->
                            V1AdmissionRegistry.planAppend
                                (commandId "settle" operationId effectId (V1AdmissionRegistry.generation candidate))
                                observed
                                candidate
                            |> Result.map (V1AdmissionRegistry.appendAndReconcile port))))

        interface IMutationFence with
            member _.Dispatch(mutation, send) = dispatch mutation send

        member _.Reconcile(operationId, owner, effectId, provider) =
            reconcile operationId owner effectId provider
