namespace FS.GG.Telemetry.Host

open System
open System.Threading
open System.Threading.Tasks
open FS.GG.Coord

type StoreConfig = { WorkspaceId:string; Root:string }
type CredentialConfig = { Reference:string; SecretFile:string; WorkspaceId:string; ProducerId:string; StreamId:string; Revoked:bool }
type HostConfig = { ListenUrl:string; CertificatePath:string; CertificatePasswordFile:string; ServiceLockPath:string; Stores:StoreConfig array; Credentials:CredentialConfig array }
type AuthEntry = { Scope:TelemetryReceipt.Scope; TokenHash:byte array; Revoked:bool }

module Configuration =
    val validate: HostConfig -> Result<HostConfig,string list>
    val load: string -> Result<HostConfig,string list>
    val credentials: HostConfig -> Map<string,AuthEntry>

type Reply = { Status:int; Body:byte array }

module Capacity =
    val admitsNewIdentity: lifetime:int64 -> pending:int64 -> pendingBytes:int64 -> incomingBytes:int64 -> bool

module Runtime =
    [<Sealed>]
    type ServiceLock =
        interface IDisposable
        static member Acquire: string -> Result<ServiceLock,string>
    [<Sealed>]
    type HostState =
        interface IDisposable
        new: HostConfig * (string->TelemetryStore.DurabilityAssessment) -> HostState
        new: HostConfig -> HostState
        member Ready: bool with get,set
        member StartDrain: unit -> unit
        member TryAcquireSlot: unit -> bool
        member ReleaseSlot: unit -> unit
    val authenticate: Map<string,AuthEntry> -> string -> TelemetryReceipt.Scope option
    val recover: HostConfig -> Result<unit,string list>
    val submit: HostState -> TelemetryReceipt.Scope -> byte array -> CancellationToken -> Task<Reply>
    val lookup: HostState -> TelemetryReceipt.Scope -> string -> CancellationToken -> Task<Reply>
    val submitAcquired: HostState -> TelemetryReceipt.Scope -> byte array -> CancellationToken -> Task<Reply>
    val lookupAcquired: HostState -> TelemetryReceipt.Scope -> string -> CancellationToken -> Task<Reply>
