namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open Xunit
open FS.GG.Coord
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

/// Transaction fixtures deliberately stop immediately after the receipt boundary.  The recording
/// transport makes the invariant observable: a retry may repair projection, but it cannot issue a
/// second `POST /issues`.
module IntakeTransactionTests =
    let private ok body = Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty }
    let private draft = """{"schema":"fsgg.coord.intake/v1","id":"tx-2134","owner":"FS-GG","repository":".github","title":"same","observed":"o","rootCause":"r","acceptance":"a","verification":"v","paths":["src/FS.GG.Coord.Core"],"class":"hardening","status":"Backlog","disposition":"create"}"""

    // `Options.parse` intentionally leaves intake's action/path in its generic positional bucket;
    // keep the test on the real parser, then make that command-local shape explicit for the handler.
    let private options path =
        let parsed = Options.parse [ "intake"; "apply"; path ] |> Result.defaultWith failwith
        { parsed with Args = [ "apply"; path ] }
    let private context transport : Client.Context = { Transport = transport; Owner = "FS-GG"; Title = "Coordination"; DefaultRepo = Some ".github"; ChoreLocks = [] }

    let private invoke cache transport =
        let path = Path.Combine(cache, "draft.json")
        File.WriteAllText(path, draft)
        match IntakeApplication.readDraft path with
        | Error error -> failwith error
        | Ok _ -> ()
        Client.intakeCmd (context transport) (options path)

    let private withCache action =
        let cache = Path.Combine(Path.GetTempPath(), "fsgg-intake-" + Guid.NewGuid().ToString("N"))
        let previous = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        Directory.CreateDirectory cache |> ignore
        Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", cache)
        try action cache finally Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previous); Directory.Delete(cache, true)

    [<Fact>]
    let ``#2134 a durable receipt bypasses issue creation`` () =
        withCache <| fun cache ->
            Cache.putIntakeReceipt { IntakeReceipt.Receipt.DraftId = "tx-2134"; Owner = "FS-GG"; Repository = ".github"; IssueNumber = 77 } |> ignore
            let mutable posts = 0
            let world = Fake.Recorder(fun req ->
                if req.Method = "POST" && req.Path.EndsWith "/issues" then posts <- posts + 1
                Error(NotFound "stop after receipt recovery"))
            Assert.Equal(Client.ExitError, invoke cache world)
            Assert.Equal(0, posts)

    [<Fact>]
    let ``#2134 interruption after create persists receipt and retry issues no second POST`` () =
        withCache <| fun cache ->
            let mutable posts = 0
            let first = Fake.Recorder(fun req ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "repos/FS-GG/.github/issues" -> ok "[]"
                | "POST", "repos/FS-GG/.github/issues" -> posts <- posts + 1; ok "{\"number\":77}"
                | _ -> Error(NotFound "interrupted after persisted create"))
            Assert.Equal(Client.ExitError, invoke cache first)
            if posts <> 1 then failwith (String.concat "\n" first.Log)
            let mutable retryPosts = 0
            let retry = Fake.Recorder(fun req ->
                if req.Method = "POST" && req.Path.EndsWith "/issues" then retryPosts <- retryPosts + 1
                Error(NotFound "stop after recovered receipt"))
            Assert.Equal(Client.ExitError, invoke cache retry)
            Assert.Equal(0, retryPosts)

    [<Theory>]
    [<InlineData("{\"number\":7,\"state\":\"closed\",\"title\":\"same\",\"body\":\"x\"}")>]
    [<InlineData("{\"number\":8,\"state\":\"open\",\"title\":\"same\",\"body\":\"x\",\"pull_request\":{}}")>]
    let ``#2134 duplicate closed issue or PR refuses before create POST`` candidate =
        withCache <| fun cache ->
            let mutable posts = 0
            let world = Fake.Recorder(fun req ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "repos/FS-GG/.github/issues" -> ok ("[" + candidate + "]")
                | "POST", _ -> posts <- posts + 1; Error(NotFound "duplicate must stop before any other write")
                | _ -> Error(NotFound "duplicate must stop before any other write"))
            Assert.Equal(Client.ExitError, invoke cache world)
            Assert.Equal(0, posts)

    [<Fact>]
    let ``#2134 unreadable and binding-mismatched receipts fail closed before POST`` () =
        withCache <| fun cache ->
            File.WriteAllText(Path.Combine(cache, "intake-tx-2134.json"), "not-json")
            let unreadable = Fake.Recorder(fun _ -> Error(NotFound "must not read network"))
            Assert.Equal(Client.ExitError, invoke cache unreadable)
            Assert.Equal(0, unreadable.RestCalls)
            File.WriteAllText(Path.Combine(cache, "intake-tx-2134.json"), "{\"draftId\":\"tx-2134\",\"owner\":\"other\",\"repository\":\".github\",\"issueNumber\":77}")
            let mismatched = Fake.Recorder(fun _ -> Error(NotFound "must not read network"))
            Assert.Equal(Client.ExitError, invoke cache mismatched)
            Assert.Equal(0, mismatched.RestCalls)
