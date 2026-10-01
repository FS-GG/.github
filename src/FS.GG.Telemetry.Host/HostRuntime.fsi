namespace FS.GG.Telemetry.Host

open System
open System.Threading
open System.Threading.Tasks
open FS.GG.Coord

type StoreConfig = { WorkspaceId: string; Root: string }

type CredentialConfig =
    {
        Reference: string
        SecretFile: string
        WorkspaceId: string
        ProducerId: string
        StreamId: string
        Role: string
        GrantId: string
        GrantGeneration: int64
        Revoked: bool
    }

type BrowserSessionConfig =
    {
        IdleSeconds: int
        AbsoluteSeconds: int
        MaximumSessions: int
        LoginAttemptsPerMinute: int
        LoginAdmission: int
        QueryAdmission: int
        QueryTimeoutSeconds: int
    }

type HostConfig =
    {
        Schema: string
        ListenUrl: string
        CertificatePath: string
        CertificatePasswordFile: string
        ServiceLockPath: string
        Stores: StoreConfig array
        Credentials: CredentialConfig array
        BrowserPrincipals: BrowserPrincipalConfig array
        BrowserSession: BrowserSessionConfig
    }

type AuthEntry =
    {
        Principal: TelemetryReceipt.Principal
        TokenHash: byte array
        Revoked: bool
    }

type NativeVerifierConfig =
    {
        RuntimeExecutablePath: string
        RuntimeExecutableSha256: string
        ModulePath: string
        ModuleSha256: string
        RuntimeManifestPath: string
        RuntimeManifestSha256: string
    }

type NativeCollectorInstallationConfig =
    {
        Schema: string
        CredentialReference: string
        ExecutablePath: string
        CodexHome: string
        EvidenceRoot: string
        Provider: string
        Model: string
        Effort: string
        NativeVerifier: NativeVerifierConfig option
    }

type NativeDeliverySourceInstallationConfig =
    {
        Schema: string
        CredentialReference: string
        GitHubCredentialFile: string
        AllowedRepositories: string array
    }

module Configuration =
    val validate: HostConfig -> Result<HostConfig, string list>
    val load: string -> Result<HostConfig, string list>
    val credentials: HostConfig -> Map<string, AuthEntry>
    val loadNativeCollectorInstallation:
        hostConfigPath: string ->
        hostConfig: HostConfig ->
            Result<NativeCollectorInstallationConfig * TelemetryReceipt.Principal, string list>
    val loadNativeDeliverySourceInstallation:
        hostConfigPath: string ->
        hostConfig: HostConfig ->
            Result<NativeDeliverySourceInstallationConfig * NativeCollectorInstallationConfig * TelemetryReceipt.Principal, string list>
    val browserKeyHashes: HostConfig -> Map<string, byte array * Set<string> * bool>

type Reply = { Status: int; Body: byte array }

module Capacity =
    val admitsNewIdentity: lifetime: int64 -> pending: int64 -> pendingBytes: int64 -> incomingBytes: int64 -> bool

module Runtime =
    [<Sealed>]
    type ServiceLock =
        interface IDisposable
        static member Acquire: string -> Result<ServiceLock, string>
        static member Probe: string -> Result<bool, string>

    [<Sealed>]
    type HostState =
        interface IDisposable
        new: HostConfig * (string -> TelemetryStore.DurabilityAssessment) -> HostState
        new: HostConfig -> HostState
        member Ready: bool with get, set
        member StartDrain: unit -> unit
        member TryAcquireSlot: unit -> bool
        member ReleaseSlot: unit -> unit

    val authenticate: Map<string, AuthEntry> -> string -> TelemetryReceipt.Principal option
    val recover: HostConfig -> Result<unit, string list>
    val submit: HostState -> TelemetryReceipt.Scope -> byte array -> CancellationToken -> Task<Reply>
    val submitPrincipal: HostState -> TelemetryReceipt.Principal -> byte array -> CancellationToken -> Task<Reply>
    val lookup: HostState -> TelemetryReceipt.Scope -> string -> CancellationToken -> Task<Reply>
    val submitAcquired: HostState -> TelemetryReceipt.Scope -> byte array -> CancellationToken -> Task<Reply>
    val submitPrincipalAcquired: HostState -> TelemetryReceipt.Principal -> byte array -> CancellationToken -> Task<Reply>
    val lookupAcquired: HostState -> TelemetryReceipt.Scope -> string -> CancellationToken -> Task<Reply>
