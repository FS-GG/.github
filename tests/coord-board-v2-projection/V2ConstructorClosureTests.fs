namespace FS.GG.Coord.Tests

/// FS-GG/.github#5113: the constructor closure and the `git` evidence `ConstructorIdentity` decides from. The pure
/// derivation is checked over synthetic trees and over this checkout's own HEAD tree; the evidence reader is checked
/// end to end against a throwaway repository whose commits change one thing each.
module V2ConstructorClosureTests =
    open System
    open System.Diagnostics
    open System.IO
    open Xunit
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.V2ConstructorClosure
    open FS.GG.Coord.GitHub.V2Preflight

    let private ok = function Ok value -> value | Error message -> failwithf "unexpected error: %s" message

    let private project (references: string list) =
        let items = references |> List.map (fun path -> $"    <ProjectReference Include=\"{path}\" />") |> String.concat "\n"
        $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n{items}\n  </ItemGroup>\n</Project>\n"

    /// A tree shaped like the real one: three hosts, a shared core, telemetry beside them and unrelated paths.
    let private baseTree =
        Map.ofList
            [ "src/FS.GG.Coord.GitHub/FS.GG.Coord.GitHub.fsproj", project [ "../FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj" ]
              "src/FS.GG.Coord.GitHub/V2Enrollment.fs", "module V2Enrollment\n"
              "src/FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj", project []
              "src/FS.GG.Coord.Core/Types.fs", "module Types\n"
              "src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj",
              project [ "../FS.GG.Coord.GitHub/FS.GG.Coord.GitHub.fsproj"; "../FS.GG.Telemetry.Store/FS.GG.Telemetry.Store.fsproj" ]
              "src/FS.GG.Coord.Cli/Program.fs", "module Program\n"
              "src/FS.GG.Telemetry.Store/FS.GG.Telemetry.Store.fsproj",
              "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <Compile Include=\"../FS.GG.Shared/Linked.fs\" />\n  </ItemGroup>\n</Project>\n"
              "src/FS.GG.Shared/Linked.fs", "module Linked\n"
              "src/FS.GG.Shared/Unlinked.fs", "module Unlinked\n"
              "tools/BoardV2Import/BoardV2Import.fsproj", project [ "../../src/FS.GG.Coord.GitHub/FS.GG.Coord.GitHub.fsproj" ]
              "tools/Other/run.py", "print()\n"
              "Directory.Build.props", "<Project>\n  <Import Project=\"$(MSBuildThisFileDirectory)dist/dotnet/Directory.Build.props\" />\n</Project>\n"
              "dist/dotnet/Directory.Build.props", "<Project />\n"
              "global.json", "{}\n"
              "docs/readme.md", "docs\n"
              "src/FS.GG.Unrelated/Unrelated.fs", "module Unrelated\n" ]

    let private deriveOf (tree: Map<string, string>) =
        derive (tree |> Map.keys |> Set.ofSeq) (fun path -> Map.tryFind path tree)

    [<Fact>]
    let ``the closure is every host's compiled project graph plus build configuration, and nothing unrelated`` () =
        let closure = deriveOf baseTree |> ok
        for path in [ "src/FS.GG.Coord.GitHub/V2Enrollment.fs"; "src/FS.GG.Coord.Core/Types.fs"; "src/FS.GG.Coord.Cli/Program.fs"
                      "src/FS.GG.Telemetry.Store/Anything.fs"; "src/FS.GG.Shared/Linked.fs"; "tools/BoardV2Import/Program.fs"
                      "Directory.Build.props"; "dist/dotnet/Directory.Build.props"; "dist/dotnet/global.json"; "global.json"
                      "Directory.Packages.props"; "NuGet.config"; "src/Directory.Build.props"; "tools/Directory.Build.props" ] do
            Assert.True(covers closure path, path)
        for path in [ "docs/readme.md"; "tools/Other/run.py"; "src/FS.GG.Unrelated/Unrelated.fs"; "src/FS.GG.Shared/Unlinked.fs"; "tests/x/y.fs" ] do
            Assert.False(covers closure path, path)
        // Causal: the reference edge alone brings the core in; without it the core is outside.
        let cut = baseTree |> Map.add "src/FS.GG.Coord.GitHub/FS.GG.Coord.GitHub.fsproj" (project [])
        Assert.False(covers (deriveOf cut |> ok) "src/FS.GG.Coord.Core/Types.fs")

    [<Fact>]
    let ``an added file shadows only at the root or beside an exact closure path`` () =
        let closure = deriveOf baseTree |> ok
        // The linked file is exact, so a file added beside it could be picked up by a later glob or link.
        Assert.True(shadows closure "src/FS.GG.Shared/New.fs")
        Assert.True(shadows closure "NewRoot.props")
        Assert.True(shadows closure "src/NewUnderSrc.txt")
        Assert.False(shadows closure "docs/new.md")
        Assert.False(shadows closure "src/FS.GG.Unrelated/New.fs")

    [<Fact>]
    let ``the derivation refuses what it cannot bound instead of returning a smaller closure`` () =
        let refused (tree: Map<string, string>) = match deriveOf tree with Error _ -> () | Ok closure -> failwithf "admitted %A" closure
        let compile (value: string) = "<Project>\n  <ItemGroup>\n    <Compile Include=\"" + value + "\" />\n  </ItemGroup>\n</Project>\n"
        refused (baseTree |> Map.add "src/FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj" (compile "$(SharedDir)/x.fs"))
        refused (baseTree |> Map.add "src/FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj" (compile "../../../outside.fs"))
        refused (baseTree |> Map.add "src/FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj" (compile "../../**/*.fs"))
        refused (baseTree |> Map.add "src/FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj" (compile "../../docs/"))
        let unreadable = derive (baseTree |> Map.keys |> Set.ofSeq) (fun path -> if path.EndsWith "Core.fsproj" then None else Map.tryFind path baseTree)
        Assert.True(Result.isError unreadable)
        // An in-project wildcard is inside the project's own prefix and is admitted.
        Assert.True(Result.isOk (deriveOf (baseTree |> Map.add "src/FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj" (compile "**/*.fs"))))

    [<Fact>]
    let ``the change listing admits only single-path entries and refuses truncation`` () =
        Assert.Equal<Change list>([], parseNameStatus "" |> ok)
        Assert.Equal<Change list>([ { Status = 'M'; Path = "a b/c.fs" }; { Status = 'A'; Path = "d" } ], parseNameStatus "M\000a b/c.fs\000A\000d\000" |> ok)
        // A listing cut inside its second path: without the terminator check it would parse as two complete entries.
        for bad in [ "M\000a"; "M\000a\000M\000bc"; "R100\000a\000b\000"; "M\000"; "U\000a\000"; "M\000\000" ] do
            Assert.True(Result.isError (parseNameStatus bad), bad.Replace("\000", "\\0"))

    // ---- this checkout's own tree ----

    let private repositoryRoot () =
        let rec up (directory: DirectoryInfo) =
            if isNull directory then failwith "no repository root above the test assembly"
            elif File.Exists(Path.Combine(directory.FullName, "src", "FS.GG.Coord.GitHub", "V2ConstructorClosure.fs")) then directory.FullName
            else up directory.Parent
        up (DirectoryInfo AppContext.BaseDirectory)

    let private git (directory: string) (arguments: string list) =
        let info = ProcessStartInfo("git")
        for argument in [ "-C"; directory; "-c"; "user.name=t"; "-c"; "user.email=t@example.invalid"; "-c"; "commit.gpgsign=false" ] @ arguments do
            info.ArgumentList.Add argument
        info.RedirectStandardOutput <- true
        info.RedirectStandardError <- true
        info.UseShellExecute <- false
        info.Environment.["GIT_CONFIG_GLOBAL"] <- "/dev/null"
        info.Environment.["GIT_CONFIG_NOSYSTEM"] <- "1"
        use proc = Process.Start info
        let output = proc.StandardOutput.ReadToEnd()
        let errors = proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        if proc.ExitCode <> 0 then failwithf "git %A: %s" arguments errors
        output

    [<Fact>]
    let ``this checkout's HEAD tree derives a closure holding the hosts, the core, telemetry and the build root`` () =
        let root = repositoryRoot ()
        let listing = git root [ "ls-tree"; "-r"; "-z"; "--full-tree"; "--name-only"; "HEAD" ]
        let files = listing.TrimEnd('\000').Split('\000') |> Set.ofArray
        let closure = derive files (fun path -> if files.Contains path then Some(git root [ "cat-file"; "blob"; "HEAD:" + path ]) else None) |> ok
        for prefix in [ "src/FS.GG.Coord.GitHub/"; "src/FS.GG.Coord.Core/"; "src/FS.GG.Coord.Cli/"; "src/FS.GG.Coord.Cli.Kernel/"
                        "src/FS.GG.Telemetry.Store/"; "tools/BoardV2Import/"; "dist/dotnet/" ] do
            Assert.Contains(prefix, closure.Prefixes)
        Assert.True(covers closure "src/FS.GG.Coord.GitHub/V2ConstructorClosure.fs")
        for outside in [ "docs/coordination/board-v2-import-manifest.json"; "scripts/release_source_closure.py"; ".github/workflows/coord-github.yml" ] do
            Assert.False(covers closure outside, outside)

    // ---- the evidence reader, end to end over a throwaway repository ----

    type private Fixture() =
        let directory = Directory.CreateTempSubdirectory("constructor-closure-").FullName
        member _.Path = directory
        member this.Write (files: (string * string) list) =
            for path, text in files do
                let full = System.IO.Path.Combine(directory, path)
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName full) |> ignore
                File.WriteAllText(full, text)
        member this.Commit (message: string) =
            git directory [ "add"; "-A" ] |> ignore
            git directory [ "commit"; "-q"; "--allow-empty"; "-m"; message ] |> ignore
            (git directory [ "rev-parse"; "HEAD" ]).Trim()
        interface IDisposable with
            member _.Dispose() = try Directory.Delete(directory, true) with _ -> ()

    let private observed (result: Observed<ConstructorContent>) =
        match result with
        | Known content -> content
        | NotObserved kind -> failwithf "not observed: %A" kind

    [<Fact>]
    let ``observeConstructorContent: unrelated changes are outside, closure changes and non-descendants are not, reads fail closed`` () =
        use fixture = new Fixture()
        git fixture.Path [ "init"; "-q"; "-b"; "main" ] |> ignore
        fixture.Write(baseTree |> Map.toList)
        let built = fixture.Commit "constructor source"
        // The cohort-90 case: main moved, only paths outside the closure changed.
        fixture.Write [ "docs/readme.md", "changed\n"; "src/FS.GG.Unrelated/Unrelated.fs", "changed\n"; "tools/Other/new.py", "x\n" ]
        let unrelated = fixture.Commit "unrelated"
        let content = V2EnrollmentCommands.observeConstructorContent fixture.Path built unrelated |> observed
        Assert.Equal((built, unrelated, true, 0, 3), (content.Built, content.Main, content.BuiltIsAncestor, content.ChangedInsideClosure, content.ChangedOutsideClosure))
        Assert.True(content.ClosureEntries > 10)
        // An empty diff (an empty commit) is a successful read with nothing changed.
        let empty = fixture.Commit "empty"
        Assert.Equal(0, (V2EnrollmentCommands.observeConstructorContent fixture.Path unrelated empty |> observed).ChangedInsideClosure)
        // Causal, one closure change per commit, each measured from the same built revision.
        let inside (files: (string * string) list) =
            fixture.Write files
            let main = fixture.Commit "inside"
            (V2EnrollmentCommands.observeConstructorContent fixture.Path built main |> observed).ChangedInsideClosure
        Assert.Equal(1, inside [ "src/FS.GG.Coord.GitHub/V2Enrollment.fs", "module V2Enrollment // changed\n" ])
        Assert.Equal(2, inside [ "src/FS.GG.Coord.Core/Types.fs", "module Types // transitive\n" ])
        Assert.Equal(3, inside [ "src/Directory.Build.props", "<Project />\n" ])
        Assert.Equal(4, inside [ "shadow.props", "<Project />\n" ])
        Assert.Equal(5, inside [ "dist/dotnet/Directory.Packages.props", "<Project />\n" ])
        // A main that is the constructor's parent, or a sibling, does not descend from it.
        let child (text: string) =
            git fixture.Path [ "checkout"; "-q"; "--detach"; built ] |> ignore
            fixture.Write [ "docs/readme.md", text ]
            fixture.Commit text
        let left, right = child "left\n", child "right\n"
        Assert.False((V2EnrollmentCommands.observeConstructorContent fixture.Path left right |> observed).BuiltIsAncestor)
        Assert.False((V2EnrollmentCommands.observeConstructorContent fixture.Path unrelated built |> observed).BuiltIsAncestor)
        Assert.True((V2EnrollmentCommands.observeConstructorContent fixture.Path built right |> observed).BuiltIsAncestor)
        // Reads that fail are unobserved, never evidence: an absent object and a malformed revision.
        Assert.Equal(NotObserved ReadFailed, V2EnrollmentCommands.observeConstructorContent fixture.Path built (String.replicate 40 "0"))
        Assert.Equal(NotObserved Unreadable, V2EnrollmentCommands.observeConstructorContent fixture.Path built "HEAD")
        Assert.Equal(NotObserved ReadFailed, V2EnrollmentCommands.observeConstructorContent (Path.Combine(fixture.Path, "absent")) built unrelated)
        // A closure the derivation cannot bound at the built revision is unobserved too.
        git fixture.Path [ "checkout"; "-q"; "--detach"; unrelated ] |> ignore
        fixture.Write [ "src/FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj", "<Project>\n  <ItemGroup>\n    <Compile Include=\"$(Elsewhere)/x.fs\" />\n  </ItemGroup>\n</Project>\n" ]
        let unbounded = fixture.Commit "unbounded"
        fixture.Write [ "docs/readme.md", "later\n" ]
        let later = fixture.Commit "later"
        Assert.Equal(NotObserved Incomplete, V2EnrollmentCommands.observeConstructorContent fixture.Path unbounded later)
