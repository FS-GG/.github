namespace FS.GG.Telemetry.Host

open Microsoft.AspNetCore.Routing

type BrowserSnapshotProvider = string -> string option -> Result<byte array,string list>

module BrowserEndpoints =
    val map: endpoints:IEndpointRouteBuilder -> options:BrowserOptions -> security:BrowserSecurity.Service -> snapshot:BrowserSnapshotProvider -> unit
