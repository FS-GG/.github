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
        Assert.Equal(1, BoardV2Application.runTendWithTransport (emptyBoard sent) TimeProvider.System [ "--plan-file"; plan; "--approve" ])
        Assert.Empty(sent)
        Assert.Equal(Some(Ok()), BoardV2Application.validateInvocation [ "board-v2"; "tend"; "--plan-file"; plan; "--rules"; "W1h,T1f,C1" ])
        Assert.True(BoardV2Application.validateInvocation [ "board-v2"; "tend"; "--apply" ] |> Option.exists Result.isError)
    finally Directory.Delete(dir, true)

[<Fact>]
let ``tend refuses a plan directory that is missing or not private before any read`` () =
    let dir = directory ()
    try
        let sent = ResizeArray<Request>()
        let shared = Path.Combine(dir, "shared")
        Directory.CreateDirectory shared |> ignore
        File.SetUnixFileMode(shared, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute ||| UnixFileMode.GroupRead ||| UnixFileMode.GroupExecute)
        Assert.Equal(1, BoardV2Application.runTendWithTransport (emptyBoard sent) TimeProvider.System [ "--plan-file"; Path.Combine(shared, "plan.json") ])
        Assert.Equal(1, BoardV2Application.runTendWithTransport (emptyBoard sent) TimeProvider.System [ "--plan-file"; Path.Combine(dir, "absent", "plan.json") ])
        Assert.Empty(sent)
        Assert.False(File.Exists(Path.Combine(shared, "plan.json")))
    finally Directory.Delete(dir, true)

let private applyDeps (sent: ResizeArray<Request>) (mutations: ResizeArray<Request>) (root: string) : BoardV2Application.TendDependencies =
    { Reads = emptyBoard sent
      SendOnce = fun request -> mutations.Add request; Error(FS.GG.Coord.GitHub.Errors.Malformed(request.Subject, "no board in this test"))
      Clock = TimeProvider.System
      Wait = ignore
      WriterLock = Ok(FS.GG.Coord.GitHub.V2WriterLock.inDirectory (Path.Combine(root, "lock")) "PVT_kwDOEYAWY84Bldpa" FS.GG.Coord.GitHub.V2WriterLock.migrationLegacyName)
      ConfigHome = Path.Combine(root, "config")
      StateHome = Path.Combine(root, "state")
      SyncDirectory = ignore }

let private enableFile (root: string) (json: string) (mode: UnixFileMode) =
    let directory = Path.Combine(root, "config", "fsgg")
    Directory.CreateDirectory directory |> ignore
    let path = Path.Combine(directory, "board-tend.json")
    File.WriteAllText(path, json)
    File.SetUnixFileMode(path, mode)

[<Fact>]
let ``tend --apply writes nothing without a private enable file, and refuses --rules`` () =
    let dir = directory ()
    try
        let sent, mutations = ResizeArray<Request>(), ResizeArray<Request>()
        let deps = applyDeps sent mutations dir
        Assert.Equal(1, BoardV2Application.runTend deps [ "--plan-file"; Path.Combine(dir, "a.json"); "--apply"; "--rules"; "W1h" ])
        Assert.Empty(sent)
        // No enable file: plan-only, no outcomes, no custody.
        Assert.Equal(0, BoardV2Application.runTend deps [ "--plan-file"; Path.Combine(dir, "b.json"); "--apply" ])
        Assert.True(File.Exists(Path.Combine(dir, "b.json")))
        Assert.False(File.Exists(Path.Combine(dir, "b.json.outcomes.json")))
        // An enable file readable by others: plan-only.
        enableFile dir """{"apply":["W1h"]}""" (UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.GroupRead ||| UnixFileMode.OtherRead)
        Assert.Equal(0, BoardV2Application.runTend deps [ "--plan-file"; Path.Combine(dir, "c.json"); "--apply" ])
        Assert.False(File.Exists(Path.Combine(dir, "c.json.outcomes.json")))
        // Only gated rules before the contract amendment: plan-only.
        enableFile dir """{"apply":["C1","S3"]}""" (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        Assert.Equal(0, BoardV2Application.runTend deps [ "--plan-file"; Path.Combine(dir, "d.json"); "--apply" ])
        Assert.False(File.Exists(Path.Combine(dir, "d.json.outcomes.json")))
        Assert.Empty(mutations)
        Assert.False(Directory.Exists(Path.Combine(dir, "state", "fsgg", "board-tend")))
    finally Directory.Delete(dir, true)

[<Fact>]
let ``tend --apply with a private enable file runs under the writer lock, and a held lock gives plan-only`` () =
    let dir = directory ()
    try
        let sent, mutations = ResizeArray<Request>(), ResizeArray<Request>()
        let deps = applyDeps sent mutations dir
        enableFile dir """{"apply":["W1h","T1f","GR"],"maxItemsPerRun":10}""" (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        Assert.Equal(0, BoardV2Application.runTend deps [ "--plan-file"; Path.Combine(dir, "a.json"); "--apply" ])
        let outcomes = Path.Combine(dir, "a.json.outcomes.json")
        Assert.True(File.Exists outcomes)
        Assert.Equal(UnixFileMode.UserRead ||| UnixFileMode.UserWrite, File.GetUnixFileMode outcomes)
        use document = JsonDocument.Parse(File.ReadAllText outcomes)
        Assert.Equal("fsgg.board-tend-outcomes/1", document.RootElement.GetProperty("schema").GetString())
        // Another writer holds the lock: the run is plan-only.
        use held =
            match FS.GG.Coord.GitHub.V2WriterLock.tryAcquire (FS.GG.Coord.GitHub.V2WriterLock.inDirectory (Path.Combine(dir, "lock")) "PVT_kwDOEYAWY84Bldpa" FS.GG.Coord.GitHub.V2WriterLock.migrationLegacyName) with
            | Ok lease -> lease
            | Error e -> failwith e
        Assert.Equal(0, BoardV2Application.runTend deps [ "--plan-file"; Path.Combine(dir, "b.json"); "--apply" ])
        Assert.True(File.Exists(Path.Combine(dir, "b.json")))
        Assert.False(File.Exists(Path.Combine(dir, "b.json.outcomes.json")))
        Assert.Empty(mutations)
    finally Directory.Delete(dir, true)
