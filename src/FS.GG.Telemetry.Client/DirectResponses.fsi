namespace FS.GG.Telemetry

open System
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open FS.GG.Coord

/// Fixed provider transport, visible only to the owning Host and focused test assembly.
module internal DirectResponses =
    [<Class>]
    type Phase =
        member ElapsedMilliseconds: int64
        member RemainingMilliseconds: int

    type Stage = NotSent | CountSent | CountAccepted | GenerationSent | ResponseCaptured

    /// Actual locally owned exchange observations, not a provider or collector grant.
    [<Class>]
    type Outcome =
        member Stage: Stage
        member CountStatus: int option
        member GenerationStatus: int option
        member CountResponseBody: byte array
        member GenerationResponseBody: byte array
        member CountBodyComplete: bool
        member GenerationBodyComplete: bool
        member Count: NativeResponses.CountAdmission option
        member Response: NativeResponses.ResponseObservation option
        member Failure: string list
        member CleanupFailure: string list
        member ElapsedMilliseconds: int64

    /// Begin once, before installation/claim. Network work stops inside this original60s phase.
    val beginPhase: unit -> Phase
    /// One count and at most one generation; no retry, endpoint override or implicit credential discovery.
    val execute:
        phase: Phase -> request: NativeResponses.FrozenRequest -> providerKey: string ->
        cancellationToken: CancellationToken -> Task<Outcome>

    /// Internal fixture seam; never exposed through Host configuration or an installed entrypoint.
    val executeForTest:
        createClient: (unit -> HttpClient) -> phase: Phase -> request: NativeResponses.FrozenRequest ->
        providerKey: string -> cancellationToken: CancellationToken -> Task<Outcome>

    /// Fixed loopback/certificate-only wire fixture, sharing production socket policy.
    val createLoopbackClientForTest: port: int -> serverCertificateSha256: string -> HttpClient
