module FS.GG.Coord.Cli.Tests.EnrollmentApplicationTests

open System
open System.IO
open Xunit
open FS.GG.Coord.Cli
open FS.GG.Coord.GitHub

// Console capture is process-wide, so every capture goes through one lock.
let private gate = obj ()

let private capture (run: unit -> int option) =
    lock gate (fun () ->
        let out, err = Console.Out, Console.Error
        use stdout = new StringWriter()
        use stderr = new StringWriter()
        Console.SetOut stdout
        Console.SetError stderr
        try
            let code = run ()
            code, stdout.ToString(), stderr.ToString()
        finally
            Console.SetOut out
            Console.SetError err)

let private programSource () =
    let rec up (directory: string) =
        let candidate = Path.Combine(directory, "src", "FS.GG.Coord.Cli", "Program.fs")
        if File.Exists candidate then candidate
        else
            let parent = Path.GetDirectoryName directory
            if isNull parent then failwith "Program.fs not found" else up parent
    File.ReadAllText(up __SOURCE_DIRECTORY__)

[<Fact>]
let ``the engine answers exactly two new roots and the verbs map onto the shared library commands`` () =
    Assert.Equal<string list>([ "enrollment"; "route-preflight" ], EnrollmentApplication.roots)
    Assert.Equal(6, EnrollmentApplication.enrollmentVerbs.Length)
    for _, command in EnrollmentApplication.enrollmentVerbs do Assert.Contains(command, V2EnrollmentCommands.commands)
    // All seven library commands are reachable: six through `enrollment` (rebind-reader, #5120, among them), `preflight`
    // through `route-preflight`.
    Assert.Equal<string list>(V2EnrollmentCommands.commands |> List.sort, ("preflight" :: (EnrollmentApplication.enrollmentVerbs |> List.map snd)) |> List.sort)
    // No other root is answered, including the existing skill `preflight` root and the other out-of-catalogue roots.
    for other in [ "preflight"; "context-view"; "observe-operation"; "board-v2"; "scan"; "plan-successor"; "" ] do
        Assert.Equal(None, EnrollmentApplication.tryRun [ other; "--help" ])
    Assert.Equal(None, EnrollmentApplication.tryRun [])

[<Fact>]
let ``help documents every verb, every route and the exit codes`` () =
    for word in [ "plan"; "prepare"; "bind-runtime"; "rebind-reader"; "render-carrier"; "resolve-issues"; "route-preflight"
                  "ProgrammeEnrollmentSuccessor"; "RoutineDelivery"; "PublicationWriterRegistration"; "BindRuntime"; "RunWriter"; "10 unsupported"; "11 unknown" ] do
        Assert.Contains(word, EnrollmentApplication.usage)
    for root in EnrollmentApplication.roots do
        let code, out, _ = capture (fun () -> EnrollmentApplication.tryRun [ root; "--help" ])
        Assert.Equal(0, code.Value)
        Assert.Equal(EnrollmentApplication.usage + Environment.NewLine, out)

[<Fact>]
let ``an unknown enrollment verb is a closed refusal, not a fall-through`` () =
    for arguments in [ [ "enrollment" ]; [ "enrollment"; "apply" ]; [ "enrollment"; "plan-successor"; "--request"; "x" ] ] do
        let code, out, err = capture (fun () -> EnrollmentApplication.tryRun arguments)
        Assert.Equal(Some 2, code)
        Assert.Equal("", out)
        Assert.Equal("board-v2 successor refused: MalformedInput" + Environment.NewLine, err)

[<Fact>]
let ``enrollment verbs reach the shared library: a missing input is the typed InputUnreadable and nothing is printed to stdout`` () =
    let missing = Path.Combine(Path.GetTempPath(), "absent-" + Guid.NewGuid().ToString("N"))
    let code, out, err =
        capture (fun () ->
            EnrollmentApplication.tryRun
                [ "enrollment"; "plan"; "--request"; missing; "--native-facts"; missing + "-f"; "--repository"; missing + "-r"
                  "--template-output"; missing + "-t"; "--payload-output"; missing + "-p" ])
    Assert.Equal(Some 2, code)
    Assert.Equal("", out)
    Assert.Equal("board-v2 successor refused: InputUnreadable" + Environment.NewLine, err)
    // No output file was created.
    for suffix in [ "-t"; "-p" ] do Assert.False(File.Exists(missing + suffix))

[<Fact>]
let ``route-preflight reaches the shared preflight: a route and stage that do not belong together are an explicit unknown`` () =
    let code, out, err =
        capture (fun () -> EnrollmentApplication.tryRun [ "route-preflight"; "--route"; "RoutineDelivery"; "--stage"; "PlanSuccessor"; "--repository"; Path.GetTempPath() ])
    Assert.Equal(Some 11, code)
    Assert.Equal("", err)
    Assert.Contains("\"overall\":\"Unknown\"", out)
    Assert.Contains("\"exitCode\":11", out)
    Assert.Contains("Registry/NotRegistered", out)
    // A route the registry does not know is a refusal, not a verdict.
    let refused, _, refusal = capture (fun () -> EnrollmentApplication.tryRun [ "route-preflight"; "--route"; "Nope"; "--stage"; "PlanSuccessor"; "--repository"; "x" ])
    Assert.Equal(Some 2, refused)
    Assert.Equal("board-v2 successor refused: MalformedInput" + Environment.NewLine, refusal)

[<Fact>]
let ``Program dispatches both roots ahead of the typed handler table, like context-view`` () =
    let source = programSource ()
    let arm (text: string) = source.IndexOf(text, StringComparison.Ordinal)
    let table = arm "match SkillCommandApplication.tryRun arguments"
    Assert.True(table > 0)
    for text in [ "| \"enrollment\" :: _ ->"; "| \"route-preflight\" :: _ ->"; "| \"context-view\" :: _ ->" ] do
        Assert.True(arm text > 0 && arm text < table, text)
    Assert.Contains("EnrollmentApplication.tryRun arguments |> Option.defaultValue ExitError", source)
    // The skill `preflight` root is untouched: this module must not shadow it.
    Assert.DoesNotContain("| \"preflight\" :: _ ->", source)
