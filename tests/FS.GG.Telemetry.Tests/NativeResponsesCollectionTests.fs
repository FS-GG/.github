namespace FS.GG.Telemetry.Tests

open System
open System.Reflection
open System.Threading
open System.Text
open Xunit
open FS.GG.Coord
open FS.GG.Telemetry.Host

module NativeResponsesCollectionTests =
    [<Fact>]
    let ``capture and verified capture have no public caller constructor`` () =
        for candidate in [ typeof<NativeResponsesCollection.Capture>; typeof<NativeResponsesCollection.VerifiedCapture> ] do
            Assert.Empty(candidate.GetConstructors(BindingFlags.Public ||| BindingFlags.Instance))
            Assert.Empty(candidate.GetMethods(BindingFlags.Public ||| BindingFlags.Static))

    [<Fact>]
    let ``absent owned capture cannot become verified source authority`` () =
        let result = NativeResponsesCollection.verify "/absent/config.json" Unchecked.defaultof<HostConfig> "/absent/store" Unchecked.defaultof<NativeResponsesCollection.Capture>
        match result with
        | Ok _ -> failwith "caller absence became verified capture"
        | Error failure ->
            Assert.True failure.ClaimReceipt.IsNone
            Assert.True failure.ClaimAttemptId.IsNone
            Assert.NotEmpty failure.Errors

    [<Fact>]
    let ``absent installed authority refuses before queue claim or provider request`` () = task {
        let principal: TelemetryReceipt.Principal =
            { Scope = { Workspace = "fixture"; Producer = "runtime"; Stream = "stream" }
              Role = TelemetryReceipt.Generic; GrantId = None; GrantGeneration = None }
        let request: NativeResponses.Request =
            { Instructions = "fixture"; InputText = "{}"; SchemaName = "fixture"; SchemaJson = Encoding.UTF8.GetBytes "{}" }
        let! result = NativeResponsesCollection.collect "/absent/responses-host/config.json" Unchecked.defaultof<HostConfig>
                        "/absent/store" principal "dispatch" "item" (Encoding.UTF8.GetBytes "{}") request CancellationToken.None
        match result with
        | Ok _ -> failwith "absent installed authority reached provider"
        | Error failure ->
            Assert.True failure.ClaimReceipt.IsNone
            Assert.True failure.ClaimAttemptId.IsNone
            Assert.NotEmpty failure.Errors
    }
