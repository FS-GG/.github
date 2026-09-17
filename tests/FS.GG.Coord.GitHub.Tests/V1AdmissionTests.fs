module FS.GG.Coord.GitHub.Tests.V1AdmissionTests

open System
open System.Security.Cryptography
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.V1Admission
open FS.GG.Coordination.GitHub

let private sha256 (bytes: byte array) =
    SHA256.HashData bytes
    |> Convert.ToHexString
    |> _.ToLowerInvariant()
    |> V1AdmissionRegistry.sha256Digest
    |> Result.defaultWith failwith

[<Fact>]
let ``journal adapter reports accepted only for an explicitly won CAS`` () =
    let translate outcome =
        let journal =
            {
                Read = fun _ -> Unchecked.defaultof<RegistryJournalRead>
                CompareAndSwap = fun _ -> outcome
            }

        (journalPort journal).Write(Unchecked.defaultof<RegistryAppendProposal>)

    Assert.Equal(ReceiveAccepted, translate CompareAndSwapWon)
    Assert.Equal(ReceiveParentConflict, translate CompareAndSwapParentConflict)
    Assert.Equal(ReceiveDefiniteRefusal "denied", translate (CompareAndSwapRefused "denied"))
    Assert.Equal(ReceiveResponseUnknown, translate CompareAndSwapResponseUnknown)

[<Fact>]
let ``provider strong absence is bound to the exact original canonical request bytes`` () =
    let originalBytes = Text.Encoding.UTF8.GetBytes "canonical-provider-request\n"
    let originalDigest = sha256 originalBytes
    let evidenceDigest = sha256 (Text.Encoding.UTF8.GetBytes "provider-proof\n")
    let mutable observed = Array.empty<byte>

    let provider: ProviderReconciliation =
        {
            Read =
                fun _ _ _ bytes ->
                    observed <- Array.copy bytes
                    Ok(StronglyAbsent(ConditionalFenceExcluded, originalDigest, evidenceDigest))
        }

    match (providerPort provider).Read "operation" "effect" 1L originalBytes with
    | Ok(ProviderStronglyAbsent(ConditionalFenceExclusion(request, proof))) ->
        Assert.Equal(V1AdmissionRegistry.sha256Value originalDigest, V1AdmissionRegistry.sha256Value request)
        Assert.Equal(V1AdmissionRegistry.sha256Value evidenceDigest, V1AdmissionRegistry.sha256Value proof)
        Assert.Equal<byte>(originalBytes, observed)
    | other -> failwithf "expected exact strong-absence evidence, got %A" other

[<Fact>]
let ``provider evidence for different bytes cannot exclude a delayed original request`` () =
    let originalBytes = Text.Encoding.UTF8.GetBytes "the-original\n"
    let differentDigest = sha256 (Text.Encoding.UTF8.GetBytes "some-other-request\n")
    let evidenceDigest = sha256 (Text.Encoding.UTF8.GetBytes "provider-proof\n")

    let provider: ProviderReconciliation =
        {
            Read = fun _ _ _ _ -> Ok(StronglyAbsent(IdempotencyKeyExcluded, differentDigest, evidenceDigest))
        }

    match (providerPort provider).Read "operation" "effect" 1L originalBytes with
    | Error "strong-absence-does-not-bind-original-request" -> ()
    | other -> failwithf "mismatched absence evidence must refuse, got %A" other

[<Fact>]
let ``ordinary producer restore refuses an absent genesis journal`` () =
    let missing =
        {
            Repository = "FS-GG/FS.GG.Coordination.Authority"
            RepositoryId = 1351660651L
            Ref = registryAddress.Ref
            FirstHead = None
            SecondHead = None
            Observation = JournalDeleted
            CommitBytes = Map.empty
            TreeBytes = Map.empty
        }

    match V1AdmissionRegistry.restore missing with
    | Error reasons -> Assert.Contains("registry-journal-identity-or-head", reasons)
    | Ok _ -> failwith "an ordinary producer must not initialize registry genesis"
