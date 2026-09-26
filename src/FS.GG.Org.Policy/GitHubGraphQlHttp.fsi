namespace FS.GG.Org.Policy

open System
open System.Net.Http

/// Dormant bounded HTTPS adapter for the fixed GitHub commit-membership query. No credential
/// source or policy entry point constructs this reader in the installed receiver.
module GitHubGraphQlHttp =
    [<Class>]
    type Reader =
        /// Create a reader with an owned, no-redirect handler and supplied bearer token.
        /// Accepted token custody and receiver activation remain separate.
        static member Create: token: string -> Result<Reader, unit>
        /// Construct with a fixture handler for deterministic transport checks.
        static member internal ForFixture: token: string * handler: HttpMessageHandler -> Result<Reader, unit>
        member Dispose: unit -> unit
        interface IDisposable
        interface GitHubCommitMembership.IReadOnlyGraphQlReader
