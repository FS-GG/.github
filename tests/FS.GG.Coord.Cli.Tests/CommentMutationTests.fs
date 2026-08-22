module FS.GG.Coord.Cli.Tests.CommentMutationTests

open System
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.BoardOps

let private ok body =
    Ok
        { Status = 200
          Body = body
          ETag = None
          NextLink = None
          Headers = Map.empty }

let private comment id body =
    JsonSerializer.Serialize(
        [| {| id = id
              html_url = $"https://example.test/comments/%d{id}"
              body = body |} |]
    )

let private context (transport: IGitHubTransport) : Kernel.Context =
    { Transport = transport
      Owner = "FS-GG"
      Title = "Coordination"
      DefaultRepo = Some ".github"
      ChoreLocks = [] }

[<Fact>]
let ``comment command exposes only create and explicit-id amend forms`` () =
    match Options.parse [ "comment"; "create"; ".github#42"; ".github#2753"; "body.md" ] with
    | Ok opts ->
        Assert.Equal(Options.CommentCmd, opts.Command)
        Assert.Equal(Options.Json, opts.Render)
    | Error error -> failwith error

    match Options.parse [ "comment"; "amend"; ".github#42"; ".github#2753"; "77"; "body.md"; "--text" ] with
    | Ok opts ->
        Assert.Equal(Options.CommentCmd, opts.Command)
        Assert.Equal(Options.Text, opts.Render)
    | Error error -> failwith error

    let contract = Options.renderCommandContract ()
    Assert.Contains("\"name\": \"comment\"", contract)
    Assert.DoesNotContain("edit-last", contract)

[<Fact>]
let ``verified create reads the exact new comment back from the collection`` () =
    let queue =
        System.Collections.Generic.Queue<IoResult<Response>>([ ok "{\"id\":42}"; ok (comment 42L "hello π") ])

    let transport = Fake.Recorder(fun _ -> queue.Dequeue())

    match
        Writes.createVerifiedComment
            transport
            { Owner = "FS-GG"
              Repo = ".github"
              Number = 42 }
            "hello π"
    with
    | Error error -> failwith (Errors.explain error)
    | Ok receipt ->
        Assert.Equal(42L, receipt.CommentId)
        Assert.Equal(8, receipt.ByteLength)
        Assert.Equal("5c2747dcba9b399166829e0058228130e5732dbfa9c77a176cbf5bfe8ca4b46e", receipt.Sha256)
        Assert.Equal(2, transport.RestCalls)

[<Fact>]
let ``verified amend addresses the supplied comment id and reads it back`` () =
    let requests = ResizeArray<Request>()

    let queue =
        System.Collections.Generic.Queue<IoResult<Response>>([ ok "{}"; ok (comment 77L "replacement") ])

    let transport =
        Fake.Recorder(fun request ->
            requests.Add request
            queue.Dequeue())

    match
        Writes.amendVerifiedComment
            transport
            { Owner = "FS-GG"
              Repo = ".github"
              Number = 42 }
            77L
            "replacement"
    with
    | Error error -> failwith (Errors.explain error)
    | Ok receipt ->
        Assert.Equal(77L, receipt.CommentId)
        Assert.Equal("PATCH", requests[0].Method)
        Assert.Equal("repos/FS-GG/.github/issues/comments/77", requests[0].Path)
        Assert.Equal("GET", requests[1].Method)
        Assert.Equal("repos/FS-GG/.github/issues/42/comments", requests[1].Path)

[<Fact>]
let ``mismatched readback is a refusal rather than a write receipt`` () =
    let queue =
        System.Collections.Generic.Queue<IoResult<Response>>([ ok "{\"id\":42}"; ok (comment 42L "wrong") ])

    let transport = Fake.Recorder(fun _ -> queue.Dequeue())

    match
        Writes.createVerifiedComment
            transport
            { Owner = "FS-GG"
              Repo = ".github"
              Number = 42 }
            "intended"
    with
    | Ok receipt -> failwithf "mismatch unexpectedly produced receipt %A" receipt
    | Error(Malformed(_, detail)) -> Assert.Contains("readback mismatch", detail)
    | Error other -> failwithf "unexpected refusal: %A" other

[<Fact>]
let ``missing source refuses before transport or capability allocation`` () =
    let transport = Fake.Recorder(fun _ -> failwith "transport must not be reached")

    let missing =
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"), "missing.md")

    let previousWorker = Environment.GetEnvironmentVariable "FSGG_WORKER"

    try
        Environment.SetEnvironmentVariable("FSGG_WORKER", "smew-test-missing")

        let opts =
            Options.parse [ "comment"; "create"; ".github#42"; ".github#2753"; missing ]
            |> Result.defaultWith failwith

        Assert.Equal(1, Handlers.commentCmd (context transport) opts)
        Assert.Equal(0, transport.RestCalls)
    finally
        Environment.SetEnvironmentVariable("FSGG_WORKER", previousWorker)

[<Fact>]
let ``failed writes preserve distinct per-operation recovery capabilities`` () =
    let worker = "smew-test-isolation-" + Guid.NewGuid().ToString("n")

    let sourceDirectory =
        Path.Combine(Path.GetTempPath(), "fsgg-2753-source-" + Guid.NewGuid().ToString("n"))

    let source = Path.Combine(sourceDirectory, "body.md")

    let workerRoot =
        Path.Combine(Path.GetTempPath(), "fsgg-coord-comment-capabilities", worker)

    let previousWorker = Environment.GetEnvironmentVariable "FSGG_WORKER"
    let stdout = Console.Out
    let stderr = Console.Error

    try
        Directory.CreateDirectory sourceDirectory |> ignore
        File.WriteAllText(source, "recovery body")
        Environment.SetEnvironmentVariable("FSGG_WORKER", worker)

        let run () =
            let queue =
                System.Collections.Generic.Queue<IoResult<Response>>(
                    [ ok "{\"id\":42}"; ok (comment 42L "mismatched remote body") ]
                )

            let transport = Fake.Recorder(fun _ -> queue.Dequeue())
            use capturedOut = new StringWriter()
            use capturedErr = new StringWriter()
            Console.SetOut capturedOut
            Console.SetError capturedErr

            let opts =
                Options.parse [ "comment"; "create"; ".github#42"; ".github#2753"; source ]
                |> Result.defaultWith failwith

            let code = Handlers.commentCmd (context transport) opts
            Console.Out.Flush()
            Console.Error.Flush()

            let line =
                capturedErr.ToString().Split('\n')
                |> Array.find (fun value -> value.Contains("preserved at "))

            code, line.Substring(line.IndexOf("preserved at ", StringComparison.Ordinal) + 13).Trim()

        let code1, path1 = run ()
        let code2, path2 = run ()
        Assert.Equal(1, code1)
        Assert.Equal(1, code2)
        Assert.False((path1 = path2), $"two operations reused one recovery capability: %s{path1}")
        Assert.True(File.Exists path1, path1)
        Assert.True(File.Exists path2, path2)
        Assert.Equal("recovery body", File.ReadAllText path1)
        Assert.Equal("recovery body", File.ReadAllText path2)
    finally
        Console.SetOut stdout
        Console.SetError stderr
        Environment.SetEnvironmentVariable("FSGG_WORKER", previousWorker)

        if Directory.Exists sourceDirectory then
            Directory.Delete(sourceDirectory, true)

        if Directory.Exists workerRoot then
            Directory.Delete(workerRoot, true)
