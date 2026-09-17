namespace FS.GG.Coord.GitHub

open System
open System.Security.Cryptography
open System.Text.Json
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

    type private Scope =
        {
            OperationId: string
            Owner: string
            OperationGeneration: int64
            ExpectedClaimGeneration: int64 option
        }

    type OperationScope = private OperationScope of Scope

    let operationScope operationId owner operationGeneration expectedClaimGeneration =
        [
            if String.IsNullOrWhiteSpace operationId then
                "operation-id"
            if String.IsNullOrWhiteSpace owner then
                "dispatch-owner"
            if operationGeneration < 1L then
                "operation-generation"
            if expectedClaimGeneration |> Option.exists (fun generation -> generation < 1L) then
                "claim-generation"
        ]
        |> function
            | [] ->
                Ok(
                    OperationScope
                        {
                            OperationId = operationId
                            Owner = owner
                            OperationGeneration = operationGeneration
                            ExpectedClaimGeneration = expectedClaimGeneration
                        }
                )
            | errors -> Error errors

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

    let authorityPort readObjects rereadHead =
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

    let private importedAuthorityPort (authority: DurableAuthority) : AuthorityGitPort =
        {
            ReadObjects = authority.ReadObjects
            RereadHead = authority.RereadHead
        }

    let private readAuthority (authority: DurableAuthority) =
        authority.ReadObjects()
        |> Result.mapError (fun reason -> [ "authority-read:" + reason ])
        |> Result.bind (fun objects ->
            let fixedRead: AuthorityGitPort =
                {
                    ReadObjects = fun () -> Ok objects
                    RereadHead = authority.RereadHead
                }

            V1AdmissionRegistry.readVerified fixedRead
            |> Result.map (fun snapshot -> objects, snapshot))

    let private epochGeneration (objects: AuthorityGitObjects) =
        try
            let _, bytes = objects.HeadBlob
            use document = JsonDocument.Parse bytes
            let generation = document.RootElement.GetProperty("generation").GetInt64()

            if generation < 1L then
                Error [ "authority-generation" ]
            else
                Ok generation
        with _ ->
            Error [ "authority-generation" ]

    let private persistedRequestBytes effectId (read: RegistryJournalRead) =
        let commits =
            match read.Observation with
            | JournalComplete(_, values) -> values
            | _ -> []

        let parse (commit: JournalCommit) =
            try
                use document = JsonDocument.Parse commit.Event.Bytes
                let root = document.RootElement
                let kind = root.GetProperty("kind").GetString()

                if kind <> "intent" && kind <> "retry" then
                    None
                else
                    let intent = root.GetProperty("payload").GetProperty("intent")

                    if intent.GetProperty("effectId").GetString() <> effectId then
                        None
                    else
                        intent.GetProperty("canonicalRequestBase64").GetString()
                        |> Convert.FromBase64String
                        |> Some
            with _ ->
                None

        match commits |> List.rev |> List.tryPick parse with
        | Some bytes -> Ok(Array.copy bytes)
        | None -> Error [ "persisted-effect-request-missing" ]

    let private appendCandidate journal action operationId effectId observed candidate =
        let port = journalPort journal

        V1AdmissionRegistry.planAppend
            (commandId action operationId effectId (V1AdmissionRegistry.generation candidate))
            observed
            candidate
        |> Result.map (V1AdmissionRegistry.appendAndReconcile port)

    let private requireAccepted subject decision =
        match decision with
        | DurableAppendAccepted(registry, permit) -> Ok(registry, permit)
        | DurableAppendParentConflict _ -> Error [ subject + "-parent-conflict" ]
        | DurableAppendRefused(reason, _) -> Error [ subject + "-refused:" + reason ]
        | DurableAppendIndeterminate(reasons, _) -> Error(subject + "-indeterminate" :: reasons)

    type DurableMutationFence(authority: DurableAuthority, journal: DurableJournal, operation: OperationScope) =
        let (OperationScope scope) = operation
        let port = journalPort journal
        let liveAuthority = importedAuthorityPort authority

        let settle effectId owner provider =
            readRegistry journal
            |> Result.bind (fun (observed, registry) ->
                V1AdmissionRegistry.recoverOperation scope.OperationId registry
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
                            appendCandidate journal "settle" scope.OperationId effectId observed candidate
                            |> Result.bind (requireAccepted "effect-settlement")
                            |> Result.map ignore)))

        let settleEvidence effectId evidence =
            let provider: ProviderReconciliation = { Read = fun _ _ _ _ -> Ok evidence }

            settle effectId scope.Owner provider

        let returnAfterSettlement effectId response evidence =
            settleEvidence effectId evidence
            |> Result.bind (fun () ->
                match evidence with
                | Applied _ -> Ok(Ok response)
                | Partial reason -> Error [ "provider-response-partial:" + reason ]
                | Indeterminate reason -> Error [ "provider-response-indeterminate:" + reason ]
                | StronglyAbsent _ -> Error [ "provider-response-unexpected-absence" ])

        let authorize effectId requestBytes objects snapshot registry handle observed =
            epochGeneration objects
            |> Result.bind (fun generation ->
                requestDigest requestBytes
                |> Result.mapError List.singleton
                |> Result.map (fun digest ->
                    {
                        EffectId = effectId
                        RequestDigest = digest
                        CanonicalRequestBytes = Array.copy requestBytes
                        Preconditions =
                            {
                                ExpectedEpochCommit = objects.Commit
                                ExpectedEpochGeneration = generation
                                ExpectedClaimGeneration = scope.ExpectedClaimGeneration
                                ExpectedOperationGeneration = scope.OperationGeneration
                            }
                    }))
            |> Result.bind (fun mutationRequest ->
                match
                    V1AdmissionRegistry.prepareEffect
                        (V1AdmissionRegistry.head registry)
                        snapshot
                        handle
                        scope.Owner
                        (MutationRequest mutationRequest)
                        registry
                with
                | EffectAlreadyInFlight owner -> Error [ "effect-already-in-flight:" + owner ]
                | EffectAlreadySettled _ -> Error [ "effect-already-settled" ]
                | EffectRefused reasons -> Error reasons
                | EffectIntentAppended candidate ->
                    appendCandidate journal "intent" scope.OperationId effectId observed candidate
                    |> Result.bind (requireAccepted "effect-intent")
                    |> Result.bind (fun (confirmed, permit) ->
                        match permit with
                        | None -> Error [ "effect-intent-not-won-by-this-invocation" ]
                        | Some initialPermit ->
                            V1AdmissionRegistry.refreshDispatch
                                port
                                liveAuthority
                                initialPermit
                                handle
                                scope.Owner
                                effectId
                                confirmed
                            |> Result.bind (fun fence ->
                                match
                                    V1AdmissionRegistry.authorizeDispatch
                                        port
                                        liveAuthority
                                        fence
                                        handle
                                        scope.Owner
                                        effectId
                                        confirmed
                                with
                                | DispatchAuthorized -> Ok()
                                | DispatchRefused reasons -> Error reasons)))

        let dispatch effectId canonicalRequestBytes send responseEvidence =
            let requestBytes =
                if obj.ReferenceEquals(canonicalRequestBytes, null) then
                    Array.empty
                else
                    Array.copy canonicalRequestBytes

            readRegistry journal
            |> Result.bind (fun (observed, registry) ->
                V1AdmissionRegistry.recoverOperation scope.OperationId registry
                |> Result.bind (fun handle ->
                    readAuthority authority
                    |> Result.bind (fun (objects, snapshot) ->
                        authorize effectId requestBytes objects snapshot registry handle observed
                        |> Result.bind (fun () ->
                            match send () with
                            | Error providerError -> Ok(Error providerError)
                            | Ok response -> returnAfterSettlement effectId response (responseEvidence response)))))

        let retry effectId send responseEvidence =
            readRegistry journal
            |> Result.bind (fun (observed, registry) ->
                persistedRequestBytes effectId observed
                |> Result.bind (fun requestBytes ->
                    V1AdmissionRegistry.recoverOperation scope.OperationId registry
                    |> Result.bind (fun handle ->
                        match
                            V1AdmissionRegistry.retryAfterProvenAbsence
                                (V1AdmissionRegistry.head registry)
                                liveAuthority
                                handle
                                scope.Owner
                                effectId
                                registry
                        with
                        | EffectAlreadyInFlight owner -> Error [ "effect-already-in-flight:" + owner ]
                        | EffectAlreadySettled _ -> Error [ "retry-requires-proven-absence" ]
                        | EffectRefused reasons -> Error reasons
                        | EffectIntentAppended candidate ->
                            appendCandidate journal "retry" scope.OperationId effectId observed candidate
                            |> Result.bind (requireAccepted "effect-retry")
                            |> Result.bind (fun (confirmed, permit) ->
                                match permit with
                                | None -> Error [ "effect-retry-not-won-by-this-invocation" ]
                                | Some initialPermit ->
                                    V1AdmissionRegistry.refreshDispatch
                                        port
                                        liveAuthority
                                        initialPermit
                                        handle
                                        scope.Owner
                                        effectId
                                        confirmed
                                    |> Result.bind (fun fence ->
                                        match
                                            V1AdmissionRegistry.authorizeDispatch
                                                port
                                                liveAuthority
                                                fence
                                                handle
                                                scope.Owner
                                                effectId
                                                confirmed
                                        with
                                        | DispatchRefused reasons -> Error reasons
                                        | DispatchAuthorized ->
                                            match send (Array.copy requestBytes) with
                                            | Error providerError -> Ok(Error providerError)
                                            | Ok response ->
                                                returnAfterSettlement effectId response (responseEvidence response))))))

        interface IMutationFence with
            member _.Dispatch(effectId, canonicalRequestBytes, send, responseEvidence) =
                dispatch effectId canonicalRequestBytes send responseEvidence

            member _.Reconcile(effectId, provider) = settle effectId scope.Owner provider

            member _.RetryProvenAbsent(effectId, send, responseEvidence) = retry effectId send responseEvidence
