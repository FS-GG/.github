namespace FS.GG.Org.Policy

open System
open System.Net.Http

/// Dormant bounded HTTPS adapter for exact protected-main reads. No installed credential
/// source or receiver path constructs it; fixture handlers are confined to the test assembly.
module GitHubProtectedBranchHttp =
    [<Class>]
    type Reader =
        /// Create a reader with an owned, no-redirect, no-cookie handler and supplied bearer token.
        static member Create: token: string -> Result<Reader, unit>
        /// Construct with a fixture handler for deterministic transport checks.
        static member internal ForFixture: token: string * handler: HttpMessageHandler -> Result<Reader, unit>
        member Dispose: unit -> unit
        interface IDisposable
        interface GitHubProtectedBranchPin.IReadOnlyProtectedBranchReader
