namespace FS.GG.Coord.GitHub

module V2ConstructorClosure =
    open System
    open System.Text.RegularExpressions

    type Closure = { Paths: Set<string>; Prefixes: Set<string> }

    let roots =
        [ "src/FS.GG.Coord.GitHub/FS.GG.Coord.GitHub.fsproj"
          "src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj"
          "tools/BoardV2Import/BoardV2Import.fsproj" ]

    let buildRoot =
        [ "global.json"; "Directory.Build.props"; "Directory.Build.targets"; "Directory.Packages.props"; "Directory.Build.rsp"
          "Directory.Solution.props"; "Directory.Solution.targets"; "NuGet.config"; "nuget.config"; "NuGet.Config"
          ".gitattributes"; ".gitmodules"; ".editorconfig"; ".config/"; "dist/dotnet/" ]

    // Every path-bearing attribute on any element; item types whose Include names a package, namespace or assembly
    // identity rather than a file are the only ones skipped (the same rule as scripts/release_source_closure.py).
    let private projectAttribute =
        Regex("<([A-Za-z_][\\w.]*)\\b[^>]*?\\b(?:Include|Project|Exclude|Update|Remove)=\"([^\"]+)\"", RegexOptions.CultureInvariant)
    let private exists = Regex("Exists\\(\\s*'([^']+)'\\s*\\)", RegexOptions.CultureInvariant)
    let private nonPathItems =
        set [ "PackageReference"; "PackageVersion"; "GlobalPackageReference"; "PackageDownload"; "FrameworkReference"
              "Using"; "InternalsVisibleTo"; "AssemblyAttribute" ]
    let private projectSuffixes = [ ".fsproj"; ".csproj"; ".props"; ".targets" ]
    let private isProjectFile (path: string) = projectSuffixes |> List.exists (fun suffix -> path.EndsWith(suffix, StringComparison.Ordinal))
    let private isProject (path: string) = path.EndsWith("proj", StringComparison.Ordinal)

    let private directoryOf (path: string) =
        match path.LastIndexOf '/' with
        | -1 -> ""
        | index -> path.Substring(0, index)

    // Normalize a repository-relative path; None when it escapes the repository or names the root itself.
    let private normalize (path: string) : string option =
        let segments = Collections.Generic.List<string>()
        let mutable escaped = path.StartsWith("/", StringComparison.Ordinal)
        for segment in path.Replace('\\', '/').Split('/') do
            match segment with
            | "" | "." -> ()
            | ".." -> if segments.Count = 0 then escaped <- true else segments.RemoveAt(segments.Count - 1)
            | value -> segments.Add value
        if escaped || segments.Count = 0 then None else Some(String.Join("/", segments))

    let private ancestors (directory: string) =
        if directory = "" then [ "" ]
        else
            let parts = directory.Split('/')
            "" :: [ for count in 1 .. parts.Length -> String.Join("/", parts |> Array.take count) ]

    let private isDirectory (files: Set<string>) (path: string) =
        let prefix = path.TrimEnd('/') + "/"
        files |> Set.exists (fun name -> name.StartsWith(prefix, StringComparison.Ordinal))

    // One MSBuild item/import value resolved against the declaring file's directory; Error for a property path or an
    // escape, None for an item/metadata reference.
    let private resolve (file: string) (directory: string) (raw: string) : Result<string option, string> =
        let value = raw.Trim()
        if value = "" || value.StartsWith("@(", StringComparison.Ordinal) || value.StartsWith("%(", StringComparison.Ordinal) then Ok None
        else
            let replaced =
                value.Replace("$(MSBuildThisFileDirectory)", "").Replace("$(MSBuildProjectDirectory)/", "").Replace("$(MSBuildProjectDirectory)", ".")
            if replaced.Contains "$(" then Error("MSBuild property path is not covered by the constructor closure: " + file + ": " + value)
            else
                let wildcard = replaced.Contains "*" || replaced.Contains "?"
                let stem =
                    if not wildcard then replaced
                    else
                        let segments = replaced.Replace('\\', '/').Split('/') |> Array.takeWhile (fun segment -> not (segment.Contains "*" || segment.Contains "?"))
                        String.Join("/", segments)
                let joined = if directory = "" then stem else directory + "/" + stem
                match normalize joined with
                | None when wildcard && stem = "" && directory <> "" -> Ok(Some(directory + "/"))
                | None -> Error("MSBuild path escapes the repository or names its root: " + file + ": " + value)
                | Some path -> Ok(Some(if wildcard then path + "/" else path))

    let covers (closure: Closure) (path: string) =
        closure.Paths.Contains path || closure.Prefixes |> Set.exists (fun prefix -> path.StartsWith(prefix, StringComparison.Ordinal))

    let shadows (closure: Closure) (path: string) =
        let directory = directoryOf path
        directory = "" || closure.Paths |> Set.exists (fun exact -> directoryOf exact = directory)

    let derive (files: Set<string>) (read: string -> string option) : Result<Closure, string> =
        let paths = Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        let prefixes = Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        let seen = Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        let pending = Collections.Generic.Stack<string>()
        let mutable failure: string option = None
        let fail (message: string) = if failure.IsNone then failure <- Some message
        let addPrefix (prefix: string) =
            // A prefix must name at least a second-level directory unless it is a listed build-root entry, so no
            // derivation can widen the closure to a whole top-level tree or the repository.
            if prefix.TrimEnd('/').Contains "/" || List.contains prefix buildRoot then prefixes.Add prefix |> ignore
            else fail ("constructor closure directory is too broad: " + prefix)
        for entry in buildRoot do
            if entry.EndsWith("/", StringComparison.Ordinal) then addPrefix entry else pending.Push entry
        for root in roots do pending.Push root
        while pending.Count > 0 && failure.IsNone do
            let item = pending.Pop()
            if seen.Add item then
                if item.EndsWith("/", StringComparison.Ordinal) then addPrefix item
                elif not (isProjectFile item) && isDirectory files item then addPrefix (item + "/")
                else
                    paths.Add item |> ignore
                    // An absent exact path stays inside: adding it later is a closure change. Only project files that
                    // exist are read.
                    if isProjectFile item && files.Contains item then
                        match read item with
                        | None -> fail ("constructor closure file is unreadable: " + item)
                        | Some text ->
                            let directory = directoryOf item
                            if isProject item then
                                if directory = "" then fail ("constructor closure project at the repository root: " + item)
                                else addPrefix (directory + "/")
                            for ancestor in ancestors directory do
                                for name in [ "Directory.Build.props"; "Directory.Build.targets"; "Directory.Packages.props" ] do
                                    pending.Push(if ancestor = "" then name else ancestor + "/" + name)
                            let values =
                                [ for m in projectAttribute.Matches text do
                                    if not (nonPathItems.Contains m.Groups.[1].Value) then yield m.Groups.[2].Value
                                  for m in exists.Matches text -> m.Groups.[1].Value ]
                            for value in values do
                                for raw in value.Split(';') do
                                    match resolve item directory raw with
                                    | Error message -> fail message
                                    | Ok None -> ()
                                    | Ok(Some path) ->
                                        // Inside a project's own directory, which is already a prefix.
                                        if not (isProject item && path.StartsWith(directory + "/", StringComparison.Ordinal) && not (isProjectFile path)) then
                                            pending.Push path
        match failure with
        | Some message -> Error message
        | None -> Ok { Paths = Set.ofSeq paths; Prefixes = Set.ofSeq prefixes }

    type Change = { Status: char; Path: string }

    let parseNameStatus (text: string) : Result<Change list, string> =
        if text = "" then Ok []
        elif not (text.EndsWith("\000", StringComparison.Ordinal)) then Error "change listing is truncated"
        else
            let fields = text.Substring(0, text.Length - 1).Split('\000')
            if fields.Length % 2 <> 0 then Error "change listing is not status/path pairs"
            else
                let changes = [ for index in 0 .. 2 .. fields.Length - 1 -> fields.[index], fields.[index + 1] ]
                match changes |> List.tryFind (fun (status, path) -> not (List.contains status [ "A"; "D"; "M"; "T" ]) || path = "") with
                | Some(status, _) -> Error("change listing has an inadmissible entry: " + status)
                | None -> Ok(changes |> List.map (fun (status, path) -> { Status = status.[0]; Path = path }))

    let changesInside (closure: Closure) (changes: Change list) =
        changes |> List.filter (fun change -> covers closure change.Path || (change.Status = 'A' && shadows closure change.Path))
