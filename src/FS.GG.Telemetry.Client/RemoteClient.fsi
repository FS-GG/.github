namespace FS.GG.Telemetry

open System.Net.Http
open System.Threading
open System.Threading.Tasks
open FS.GG.Coord

module RemoteClient =
    type CredentialResolver = string -> CancellationToken -> Task<string option>
    type Result = Acknowledged of RemoteContract.Receipt | Unacknowledged of string
    val createHandler: unit -> HttpClientHandler
    val submit: RemoteContract.ClientConfig -> CredentialResolver -> TelemetryReceipt.Scope -> byte array -> CancellationToken -> Task<Result>
    val lookup: RemoteContract.ClientConfig -> CredentialResolver -> TelemetryReceipt.Scope -> batchId: string -> expectedDigest: string -> CancellationToken -> Task<Result>
    val submitWithClientForTesting: HttpClient -> RemoteContract.ClientConfig -> CredentialResolver -> TelemetryReceipt.Scope -> byte array -> CancellationToken -> Task<Result>
    val lookupWithClientForTesting: HttpClient -> RemoteContract.ClientConfig -> CredentialResolver -> TelemetryReceipt.Scope -> batchId: string -> expectedDigest: string -> CancellationToken -> Task<Result>
