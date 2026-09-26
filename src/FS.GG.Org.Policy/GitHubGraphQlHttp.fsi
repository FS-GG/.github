namespace FS.GG.Org.Policy

open System
open System.Net.Http

/// Dormant bounded HTTPS adapter for exact GitHub commit membership reads.
module GitHubGraphQlHttp =
    [<Class>]
    type Reader =
        /// Create a reader with an owned, no-redirect handler and supplied bearer token.
        static member Create: token: string -> Result<Reader, unit>
        /// Construct with a fixture handler for deterministic transport checks.
        static member internal ForFixture: token: string * handler: HttpMessageHandler -> Result<Reader, unit>
        member Dispose: unit -> unit
        interface IDisposable
        interface GitHubCommitMembership.IReadOnlyGraphQlReader
