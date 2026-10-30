module FS.GG.Coord.Cli.Tests.ContextViewApplicationTests

open System
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.ContextView
open FS.GG.Coord.Cli
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport

let private head = String.replicate 40 "a"
let private body = "## Owner\n\nprocess_evidence_tools\n\n## Done when\n\n- Deliver `x` verbatim.\n\n## Current deliverable\n\nNone.\n\n## Next action\n\nImplement.\n\n## Blocked on\n\nNothing external.\n\n## Acceptance evidence\n\nNone yet.\n\nPaths: src/a.fs\n"
let private issueJson =
    JsonSerializer.Serialize {| number = 4940; title = "T"; state = "open"; updated_at = "2026-10-30T00:00:00Z"; body = body |}
let private prJson = $"""{{"number":9,"html_url":"https://github.com/o/r/pull/9","state":"open","draft":false,"merged":false,"head":{{"sha":"{head}","ref":"routine/x"}},"base":{{"ref":"main"}},"changed_files":3,"body":"<!-- fsgg:routine-development/v1 head={head} operation=source -->"}}"""
let private checksJson = """{"total_count":1,"check_runs":[{"name":"build","status":"completed","conclusion":"success"}]}"""

type private Recording(respond: Request -> Errors.IoResult<Response>) =
    let calls = ResizeArray<Request>()
    member _.Calls = calls |> Seq.toList
    interface IDeadlineSinglePageGitHubTransport with
        member _.SendSingleWithin(request, _) = calls.Add request; respond request

let private ok body : Errors.IoResult<Response> = Ok { Status = 200; Body = body; Headers = Map.empty; ETag = None; NextLink = None }
let private native (request: Request) =
    if request.Path.Contains "/issues/" then ok issueJson
    elif request.Path.EndsWith "/check-runs" then ok checksJson
    elif request.Path.Contains "/pulls/" then ok prJson
    else Error(Errors.NotFound request.Path)

let private temp () =
    let path = Path.Combine(Path.GetTempPath(), "context-view-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory path |> ignore
    path

let private arguments report : ContextViewApplication.Arguments =
    { Issue = { Repository = "o/r"; Number = 4940 }; PullRequest = Some 9; Action = Review; CapBytes = DefaultCapBytes
      Custody = None; Workspace = None; ReportFile = report }

[<Fact>]
let ``arguments are a closed vocabulary`` () =
    let good = ContextViewApplication.parseArguments [ "--issue"; "o/r#4940"; "--report-file"; "/x/new.json"; "--pr"; "9"; "--action"; "review"; "--cap-bytes"; "8192" ]
    match good with
    | Ok parsed ->
        Assert.Equal(Some 9, parsed.PullRequest)
        Assert.Equal(Review, parsed.Action)
        Assert.Equal(8192, parsed.CapBytes)
    | Error e -> failwith e
    match ContextViewApplication.parseArguments [ "--issue"; "o/r#4940"; "--report-file"; "/x" ] with
    | Ok parsed -> Assert.Equal(DefaultCapBytes, parsed.CapBytes)
    | Error e -> failwith e
    for bad in
        [ [ "--report-file"; "/x" ]; [ "--issue"; "o/r#1" ]; [ "--issue"; "bad"; "--report-file"; "/x" ]
          [ "--issue"; "o/r#1"; "--report-file"; "/x"; "--pr"; "0" ]; [ "--issue"; "o/r#1"; "--report-file"; "/x"; "--pr"; "9"; "--pr"; "9" ]
          [ "--issue"; "o/r#1"; "--report-file"; "/x"; "--action"; "dispatch" ]; [ "--issue"; "o/r#1"; "--report-file"; "/x"; "--force"; "1" ]
          [ "--issue"; "o/r#1"; "--report-file"; "/x"; "--cap-bytes" ] ] do
        Assert.True(Result.isError (ContextViewApplication.parseArguments bad), String.Join(" ", bad))

[<Fact>]
let ``execute writes one new owner-only report with GET-only reads and a compact result`` () =
    let directory = temp ()
    let report = Path.Combine(directory, "view.json")
    let transport = Recording native
    let line, code = ContextViewApplication.execute transport (arguments report)
    Assert.Equal(0, code)
    Assert.True(transport.Calls |> List.forall (fun c -> c.Method = "GET" && c.Body = NoBody))
    Assert.Equal(3, transport.Calls.Length)
    use result = JsonDocument.Parse line
    Assert.Equal("fsgg.coord.context-view-result/1", result.RootElement.GetProperty("schema").GetString())
    Assert.Equal(File.ReadAllBytes(report).Length, result.RootElement.GetProperty("bytes").GetInt32())
    Assert.True(line.Length < 800)
    use view = JsonDocument.Parse(File.ReadAllText report)
    Assert.Equal("fsgg.coord.context-view/1", view.RootElement.GetProperty("schema").GetString())
    Assert.Equal("- Deliver `x` verbatim.", view.RootElement.GetProperty("doneWhen").GetProperty("text").GetString())
    Assert.Equal("bound-current", view.RootElement.GetProperty("source").GetProperty("routineMarker").GetProperty("state").GetString())
    if not (OperatingSystem.IsWindows()) then
        Assert.Equal(UnixFileMode.UserRead ||| UnixFileMode.UserWrite, File.GetUnixFileMode report)
    Assert.Equal(1, Directory.GetFiles(directory).Length)

[<Fact>]
let ``an existing report file is refused and left untouched`` () =
    let directory = temp ()
    let report = Path.Combine(directory, "view.json")
    File.WriteAllText(report, "precious")
    let transport = Recording native
    let line, code = ContextViewApplication.execute transport (arguments report)
    Assert.Equal(3, code)
    Assert.Contains("already exists", line)
    Assert.Equal("precious", File.ReadAllText report)
    Assert.Empty transport.Calls
    let missing, missingCode = ContextViewApplication.execute transport (arguments (Path.Combine(directory, "nope", "v.json")))
    Assert.Equal(3, missingCode)
    Assert.Contains("directory does not exist", missing)

[<Fact>]
let ``replaying the same native facts into two files gives identical bytes`` () =
    let directory = temp ()
    let a, b = Path.Combine(directory, "a.json"), Path.Combine(directory, "b.json")
    ContextViewApplication.execute (Recording native) (arguments a) |> ignore
    ContextViewApplication.execute (Recording native) (arguments b) |> ignore
    Assert.Equal<byte[]>(File.ReadAllBytes a, File.ReadAllBytes b)

[<Fact>]
let ``an unreadable issue still produces a typed incomplete view and a bad cap writes nothing`` () =
    let directory = temp ()
    let report = Path.Combine(directory, "view.json")
    let down = Recording(fun r -> Error(Errors.Transport "down"))
    let line, code = ContextViewApplication.execute down (arguments report)
    Assert.Equal(0, code)
    use result = JsonDocument.Parse line
    Assert.Equal("incomplete", result.RootElement.GetProperty("status").GetString())
    Assert.True(result.RootElement.GetProperty("obligationGaps").GetInt32() >= 1)
    let refused = Path.Combine(directory, "refused.json")
    let line2, code2 = ContextViewApplication.execute (Recording native) { arguments refused with CapBytes = 10 }
    Assert.Equal(3, code2)
    Assert.Contains("refusal", line2)
    Assert.False(File.Exists refused)

[<Fact>]
let ``custody is read from the original observer cursor and a corrupt one is unavailable`` () =
    let directory = temp ()
    let identity : OperationObservation.Identity =
        { Issue = "o/r#4940"; Slice = "source"; Attempt = "attempt-7"; Owner = "owner"; SourceRevision = String.replicate 40 "b"
          IssueUpdatedAt = "r"; IssueBodySha256 = String.replicate 64 "0"; Repository = "o/r"; PullRequest = 9; Head = head
          RunId = 42L; RunAttempt = 1; PopulationRevision = "p"; Unknowns = [ "u1"; "u2" ] }
    let cursor = OperationObservation.start identity "boot" 1000L 60 |> Result.defaultWith failwith
    let path = Path.Combine(directory, "cursor.json")
    File.WriteAllText(path, OperationObservationApplication.encodeCursor cursor)
    match ContextViewApplication.readCustody path with
    | Supplied fact ->
        Assert.Equal("attempt-7", fact.Attempt)
        Assert.Equal<string list>([ "u1"; "u2" ], fact.Unknowns)
    | other -> failwithf "%A" other
    let before = File.ReadAllBytes path
    File.WriteAllText(path, File.ReadAllText(path).Replace("attempt-7", "attempt-8"))
    Assert.True(match ContextViewApplication.readCustody path with Unavailable _ -> true | _ -> false)
    Assert.True(match ContextViewApplication.readCustody (Path.Combine(directory, "none")) with Unavailable _ -> true | _ -> false)
    Assert.NotEqual<byte[]>(before, File.ReadAllBytes path)

[<Fact>]
let ``workspace navigation hashes files marks absence and refuses escapes and reads the Atlas archive`` () =
    let root = temp ()
    File.WriteAllText(Path.Combine(root, "AGENTS.md"), "hello")
    Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
    let records = Path.Combine(root, "docs", "atlas", "records")
    Directory.CreateDirectory records |> ignore
    File.WriteAllText(Path.Combine(records, "abc.json"), """{"id":"platform/x","kind":"decision","status":"recorded","title":"X","supersedes":[],"conflictsWith":[]}""")
    match ContextViewApplication.readNavigation root [ "AGENTS.md"; "missing.md"; "src"; "../outside"; "/etc/passwd" ] with
    | Supplied nav ->
        Assert.Equal(File "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", nav.Files["AGENTS.md"])
        Assert.Equal(Absent, nav.Files["missing.md"])
        Assert.Equal(Directory, nav.Files["src"])
        Assert.True(match nav.Files["../outside"] with Unreadable _ -> true | _ -> false)
        Assert.True(match nav.Files["/etc/passwd"] with Unreadable _ -> true | _ -> false)
        match nav.Atlas with
        | Supplied [ entry ] -> Assert.Equal("platform/x", entry.Id); Assert.Equal("abc", entry.Record)
        | other -> failwithf "%A" other
    | other -> failwithf "%A" other
    match ContextViewApplication.readNavigation (Path.Combine(root, "nope")) [] with
    | Unavailable _ -> ()
    | other -> failwithf "%A" other
    File.WriteAllText(Path.Combine(records, "bad.json"), "{broken")
    match ContextViewApplication.readNavigation root [] with
    | Supplied { Atlas = Unavailable reason } -> Assert.Contains("could not be parsed", reason)
    | other -> failwithf "%A" other

[<Fact>]
let ``tryRun answers only context-view`` () =
    Assert.Equal(None, ContextViewApplication.tryRun [ "observe-operation" ])
    Assert.Equal(Some 3, ContextViewApplication.tryRun [ "context-view"; "--issue"; "bad" ])

[<Fact>]
let ``help documents every flag the parser accepts and the cap bounds`` () =
    for flag in [ "--issue"; "--report-file"; "--pr"; "--action"; "--cap-bytes"; "--custody"; "--workspace" ] do
        Assert.Contains(flag, ContextViewApplication.usage)
    Assert.Contains(string DefaultCapBytes, ContextViewApplication.usage)
    Assert.Equal(Some 0, ContextViewApplication.tryRun [ "context-view"; "--help" ])

[<Fact>]
let ``a trailing separator on the workspace does not make every path unreadable`` () =
    let root = temp ()
    File.WriteAllText(Path.Combine(root, "AGENTS.md"), "hello")
    let expected = File "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"
    for spelling in [ root; root + "/"; root + "//"; Path.Combine(root, ".") + "/" ] do
        match ContextViewApplication.readNavigation spelling [ "AGENTS.md"; "missing.md" ] with
        | Supplied nav ->
            Assert.Equal(expected, nav.Files["AGENTS.md"])
            Assert.Equal(Absent, nav.Files["missing.md"])
        | other -> failwithf "%s: %A" spelling other

[<Fact>]
let ``the archive reader carries supersedes and conflictsWith so the head can be resolved`` () =
    let root = temp ()
    let records = Path.Combine(root, "docs", "atlas", "records")
    Directory.CreateDirectory records |> ignore
    File.WriteAllText(Path.Combine(records, "aaa.json"), """{"id":"platform/x","kind":"decision","status":"recorded","title":"old","supersedes":[],"conflictsWith":[]}""")
    File.WriteAllText(Path.Combine(records, "zzz.json"), """{"id":"platform/x","kind":"decision","status":"recorded","title":"new","supersedes":["aaa"],"conflictsWith":["q"]}""")
    match ContextViewApplication.readNavigation root [] with
    | Supplied { Atlas = Supplied entries } ->
        let head = entries |> List.find (fun e -> e.Record = "zzz")
        Assert.Equal<string list>([ "aaa" ], head.Supersedes)
        Assert.Equal<string list>([ "q" ], head.ConflictsWith)
    | other -> failwithf "%A" other
    // a record whose chain fields are missing or mistyped makes the archive unavailable, not silently chainless
    File.WriteAllText(Path.Combine(records, "bad.json"), """{"id":"platform/y","kind":"k","status":"s","title":"t","supersedes":"aaa","conflictsWith":[]}""")
    match ContextViewApplication.readNavigation root [] with
    | Supplied { Atlas = Unavailable _ } -> ()
    | other -> failwithf "%A" other

[<Fact>]
let ``the result line names omitted mandatory sections so an empty shell is noticed`` () =
    let directory = temp ()
    let bulky = String.replicate 90 "- A long obligation sentence that must be carried whole or not at all.\n"
    let big = JsonSerializer.Serialize {| number = 4940; title = "T"; state = "open"; updated_at = "2026-10-30T00:00:00Z"; body = body.Replace("- Deliver `x` verbatim.", bulky) |}
    let respond (request: Request) = if request.Path.Contains "/issues/" then ok big else native request
    let line, code = ContextViewApplication.execute (Recording respond) { arguments (Path.Combine(directory, "v.json")) with CapBytes = MinimumCapBytes; PullRequest = None }
    Assert.Equal(0, code)
    use result = JsonDocument.Parse line
    let names = [ for n in result.RootElement.GetProperty("mandatoryOmissions").EnumerateArray() -> n.GetString() ]
    Assert.Contains("doneWhen", names)
    Assert.Equal("incomplete", result.RootElement.GetProperty("status").GetString())
    // and an unbounded run reports none
    let clean, _ = ContextViewApplication.execute (Recording native) (arguments (Path.Combine(directory, "w.json")))
    use cleanResult = JsonDocument.Parse clean
    Assert.Equal(0, cleanResult.RootElement.GetProperty("mandatoryOmissions").GetArrayLength())
