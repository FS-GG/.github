namespace FS.GG.Telemetry.Host

open System

type BrowserPrincipalConfig =
    { PrincipalId: string
      KeyHashFile: string
      WorkspaceIds: string array
      Revoked: bool }

type BrowserOptions =
    { PublicOrigin: Uri
      IdleLifetime: TimeSpan
      AbsoluteLifetime: TimeSpan
      MaximumSessions: int
      LoginAttemptsPerMinute: int
      LoginAdmission: int
      QueryAdmission: int
      QueryTimeout: TimeSpan }

type BrowserIdentity =
    { PrincipalId: string
      WorkspaceIds: Set<string> }

type BrowserLoginResult =
    | LoginAccepted of sessionId: string * BrowserIdentity
    | LoginDenied
    | LoginOverloaded

module BrowserSecurity =
    val validateOptions: BrowserOptions -> Result<BrowserOptions,string list>
    val validatePrincipals: BrowserPrincipalConfig array -> Result<BrowserPrincipalConfig array,string list>

    [<Sealed>]
    type Service =
        interface IDisposable
        new: BrowserOptions * BrowserPrincipalConfig array -> Service
        member Login: principalId:string * accessKey:string * now:DateTimeOffset -> BrowserLoginResult
        member LoginAcquired: principalId:string * accessKey:string * now:DateTimeOffset -> BrowserLoginResult
        member TryAcquireLogin: unit -> bool
        member ReleaseLogin: unit -> unit
        member Validate: sessionId:string * now:DateTimeOffset -> BrowserIdentity option
        member Rotate: sessionId:string * now:DateTimeOffset -> (string * BrowserIdentity) option
        member Logout: sessionId:string -> unit
        member SessionCount: int
        member TryAcquireQuery: unit -> bool
        member ReleaseQuery: unit -> unit
