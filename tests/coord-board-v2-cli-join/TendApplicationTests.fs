/// `board-v2 tend` plan-only through the CLI seam (FS-GG/.github#5136 phase 2 PR 3).
module FS.GG.Coord.GitHub.Tests.TendApplicationTests

open System
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord.Cli
open FS.GG.Coord.GitHub.Transport

/// An empty Project 3: one items page, one PR search page, no timeline read.
let private emptyBoard (sent: ResizeArray<Request>) =
    { new IGitHubTransport with
        member _.Send request =
            sent.Add request
            let document = match request.Body with Query(d, _) -> d | _ -> ""
            let data =
                if document.Contains "items(first: 50" then
                    """{"rateLimit":{"cost":1},"organization":{"projectV2":{"items":{"totalCount":0,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[]}}}}"""
                else """{"rateLimit":{"cost":1},"search":{"issueCount":0,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[]}}"""
            Ok { Status = 200; ETag = None; NextLink = None; Headers = Map.empty; Body = "{\"data\":" + data + "}" }
        member _.SendMutation _ = failwith "tend is read-only"
        member _.RetryMutation _ = failwith "tend is read-only" }

let private directory () =
    let path = Path.Combine(Path.GetTempPath(), "tend-cli-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    path

[<Fact>]
let ``tend writes a private plan and sends only GraphQL queries`` () =
    let dir = directory ()
    try
        let plan = Path.Combine(dir, "plan.json")
        let sent = ResizeArray<Request>()
        Assert.Equal(0, BoardV2Application.runTendWithTransport (emptyBoard sent) TimeProvider.System [ "--plan-file"; plan ])
        Assert.Equal(2, sent.Count)
        Assert.True(sent |> Seq.forall (fun r -> r.Method = "POST" && r.Path = "graphql"))
        use document = JsonDocument.Parse(File.ReadAllText plan)
        Assert.Equal("fsgg.board-tend-plan/1", document.RootElement.GetProperty("schema").GetString())
        Assert.Equal(UnixFileMode.UserRead ||| UnixFileMode.UserWrite, File.GetUnixFileMode plan)
        // A second run never overwrites the plan.
        Assert.Equal(1, BoardV2Application.runTendWithTransport (emptyBoard sent) TimeProvider.System [ "--plan-file"; plan ])
    finally Directory.Delete(dir, true)

[<Fact>]
let ``tend refuses an unknown rule, a missing plan file and stray arguments before any read`` () =
    let dir = directory ()
    try
        let sent = ResizeArray<Request>()
        let plan = Path.Combine(dir, "plan.json")
        Assert.Equal(1, BoardV2Application.runTendWithTransport (emptyBoard sent) TimeProvider.System [ "--plan-file"; plan; "--rules"; "W1h,X9" ])
        Assert.Equal(1, BoardV2Application.runTendWithTransport (emptyBoard sent) TimeProvider.System [ "--rules"; "W1h" ])
        Assert.Equal(1, BoardV2Application.runTendWithTransport (emptyBoard sent) TimeProvider.System [ "--plan-file"; plan; "--apply" ])
        Assert.Empty(sent)
        Assert.Equal(Some(Ok()), BoardV2Application.validateInvocation [ "board-v2"; "tend"; "--plan-file"; plan; "--rules"; "W1h,T1f,C1" ])
        Assert.True(BoardV2Application.validateInvocation [ "board-v2"; "tend"; "--apply" ] |> Option.exists Result.isError)
    finally Directory.Delete(dir, true)
