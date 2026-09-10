namespace FS.GG.Telemetry.Host

open System.Collections.Generic
open System.Security.Cryptography.X509Certificates
open Microsoft.AspNetCore.Builder

module Hosting =
    val createBuilder: listenUrl:string -> certificate:X509Certificate2 -> WebApplicationBuilder

module Endpoints =
    val configure: app:WebApplication -> state:Runtime.HostState -> credentials:Map<string,AuthEntry> -> unit

module Operations =
    val runWithAssessment: argv:string array -> assessmentFor:(string -> FS.GG.Coord.TelemetryStore.DurabilityAssessment) -> int
    val run: argv:string array -> int

module Program =
    val main: argv:string array -> int
