namespace FS.GG.Org.Policy

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text.RegularExpressions
open System.Xml
open System.Xml.Linq

/// Pure single-project XML adapter for Rule (b). Caller-supplied bytes and project identity are
/// inputs; project discovery, source authentication, and installed receiver wiring are separate.
module ProjectReferenceXml =
    let private error code path message =
        Error { Code = code; Path = path; Message = message }

    let private isProjectReference (element: XElement) =
        // MSBuild item names are case-insensitive; XML structural names retain their case.
        String.Equals(element.Name.LocalName, "ProjectReference", StringComparison.OrdinalIgnoreCase)

    let private setsTargetsOverride (document: XDocument) =
        // A property assignment can replace nearest Directory.Build.targets selection. Metadata
        // with the same name is not a property assignment.
        document.Descendants()
        |> Seq.exists (fun element ->
            String.Equals(element.Name.LocalName, "DirectoryBuildTargetsPath", StringComparison.OrdinalIgnoreCase)
            && not (isNull element.Parent)
            && element.Parent.Name.LocalName = "PropertyGroup")

    let private normalized (path: string) =
        not (String.IsNullOrWhiteSpace path)
        && not (path.StartsWith("/", StringComparison.Ordinal))
        && not (Regex.IsMatch(path, "^[A-Za-z]:", RegexOptions.CultureInvariant))
        && not (path.Contains('\\'))
        && not (path.Contains("//", StringComparison.Ordinal))
        && (path.Split('/') |> Array.forall (fun part -> part <> "" && part <> "." && part <> ".."))

    let private discoverableProject (path: string) =
        // Match the live Rule (b) project roster, including case-varied MSBuild extensions.
        [ ".fsproj"; ".csproj"; ".vbproj" ]
        |> List.exists (fun extension -> path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))

    let private resolve (projectPath: string) (includePath: string) =
        let relative = includePath.Replace('\\', '/')
        if String.IsNullOrWhiteSpace relative
           // Evaluated MSBuild items trim surrounding whitespace. Preserve no static edge when
           // these XML bytes would name a different path before evaluation.
           || not (String.Equals(relative, relative.Trim(), StringComparison.Ordinal))
           || relative.StartsWith("/", StringComparison.Ordinal)
           || Regex.IsMatch(relative, "^[A-Za-z]:", RegexOptions.CultureInvariant)
           // MSBuild expands item lists, globs, and %-escaped characters. One literal graph edge
           // for any of these would invent a path and could hide a real dependency.
           || relative.IndexOfAny([| ';'; '*'; '?'; '%' |]) >= 0
           || ([ "$("; "%("; "@(" ] |> List.exists (relative.Contains)) then
            None
        else
            let parts = ResizeArray<string>(projectPath.Split('/'))
            parts.RemoveAt(parts.Count - 1)
            let mutable valid = true
            for part in relative.Split('/') do
                if valid then
                    match part with
                    | "" -> valid <- false
                    | "." -> ()
                    | ".." when parts.Count > 0 -> parts.RemoveAt(parts.Count - 1)
                    | ".." -> valid <- false
                    | value -> parts.Add(value)
            let target = String.Join("/", parts)
            if valid && normalized target then Some target else None

    /// Decode ProjectReference Include values and resolve them to normalized repo-relative paths.
    /// Explicit imports and target-time ProjectReference changes require MSBuild evaluation.
    /// Tasks may also emit ProjectReference items without an item element in these XML bytes.
    /// Dynamic Output item names cannot be resolved from one XML file.
    /// ProjectReference Remove changes the evaluated item set and is not a static edge.
    /// No filesystem reads or assertions about a complete project roster occur here.
    let inspect (projectPath: string) (xml: string) : Result<string list, SyntaxDiagnostic> =
        if not (normalized projectPath) then
            error "project-path" projectPath "project identity must be a normalized repo-relative path"
        elif isNull xml then
            error "project-xml" projectPath "project XML is absent"
        else
            try
                let settings = XmlReaderSettings()
                settings.DtdProcessing <- DtdProcessing.Prohibit
                settings.XmlResolver <- null
                use input = new StringReader(xml)
                use reader = XmlReader.Create(input, settings)
                let document = XDocument.Load(reader)
                let inTarget (element: XElement) =
                    element.Ancestors()
                    |> Seq.exists (fun ancestor -> ancestor.Name.LocalName = "Target")
                let targetReference =
                    document.Descendants()
                    |> Seq.exists (fun element ->
                        isProjectReference element && inTarget element)
                let removedReference =
                    document.Descendants()
                    |> Seq.exists (fun element ->
                        isProjectReference element
                        && not (isNull (element.Attribute(XName.Get("Remove")))))
                let outputItemNames =
                    document.Descendants()
                    |> Seq.filter (fun element -> element.Name.LocalName = "Output" && inTarget element)
                    |> Seq.choose (fun element ->
                        let itemName = element.Attribute(XName.Get("ItemName"))
                        if isNull itemName then None else Some itemName.Value)
                    |> Seq.toList
                let dynamicTaskOutputName =
                    outputItemNames
                    |> List.exists (fun name ->
                        [ "$("; "@("; "%(" ]
                        |> List.exists (fun token -> name.Contains(token, StringComparison.Ordinal)))
                let taskOutputReference =
                    outputItemNames
                    |> List.exists (fun name ->
                        String.Equals(name, "ProjectReference", StringComparison.OrdinalIgnoreCase))
                if isNull document.Root || document.Root.Name.LocalName <> "Project" then
                    error "project-xml" projectPath "project XML root must be Project"
                elif setsTargetsOverride document then
                    error "project-reference" projectPath "DirectoryBuildTargetsPath overrides implicit target selection; requires MSBuild import evaluation"
                elif document.Descendants() |> Seq.exists (fun element -> element.Name.LocalName = "Import") then
                    error "project-reference" projectPath "explicit MSBuild Import requires evaluation"
                elif targetReference then
                    error "project-reference" projectPath "target-time ProjectReference changes require evaluation"
                elif removedReference then
                    error "project-reference" projectPath "ProjectReference Remove requires MSBuild evaluation"
                elif dynamicTaskOutputName then
                    error "project-reference" projectPath "dynamic task Output ItemName requires evaluation"
                elif taskOutputReference then
                    error "project-reference" projectPath "task Output to ProjectReference requires evaluation"
                else
                    let references = ResizeArray<string>()
                    let mutable invalid = None
                    for element in document.Descendants() do
                        if isProjectReference element && invalid.IsNone then
                            let includeAttribute = element.Attribute(XName.Get("Include"))
                            if not (isNull includeAttribute) then
                                match resolve projectPath includeAttribute.Value with
                                | Some target -> references.Add(target)
                                | None -> invalid <- Some includeAttribute.Value
                    match invalid with
                    | Some value -> error "project-reference" projectPath ("unresolvable Include: " + value)
                    | None -> Ok(List.ofSeq references)
            with
            | :? XmlException as ex -> error "project-xml" projectPath ex.Message
            | :? ArgumentException as ex -> error "project-xml" projectPath ex.Message

    /// Assemble only caller-supplied project XML into a closed local graph. Duplicate identities,
    /// missing referenced sources, and identities outside the live project discovery extensions
    /// refuse before Map construction can erase evidence. This does not authenticate discovery,
    /// file bytes, implicit imports, or evaluated MSBuild items.
    let inspectSuppliedProjectSet
        (sources: (string * string) list)
        : Result<Map<string, string list>, SyntaxDiagnostic> =
        if isNull (box sources) || List.isEmpty sources then
            error "project-roster" "<projects>" "supplied project source set is absent or empty"
        else
            let rec collect seen graph remaining =
                match remaining with
                | [] ->
                    let missing =
                        graph
                        |> Map.toSeq
                        |> Seq.tryPick (fun (project, references) ->
                            references
                            |> List.tryFind (fun dependency -> not (Map.containsKey dependency graph))
                            |> Option.map (fun dependency -> project, dependency))
                    match missing with
                    | Some(project, dependency) ->
                        error "project-roster" project (sprintf "referenced project %s is absent from supplied source set" dependency)
                    | None -> Ok graph
                | (path, xml) :: rest ->
                    if not (isNull path) && normalized path && not (discoverableProject path) then
                        error "project-roster" path "supplied identity is outside discoverable .fsproj/.csproj/.vbproj roster"
                    elif Set.contains path seen then
                        error "project-roster" path "duplicate supplied project identity"
                    else
                        match inspect path xml with
                        | Error diagnostic -> Error diagnostic
                        | Ok references ->
                            collect (Set.add path seen) (Map.add path references graph) rest
            collect Set.empty Map.empty sources

    /// Require an exact identity match between a separately supplied expected roster and source
    /// rows. This closes the local handoff against omitted independent projects, but the caller
    /// must still authenticate that the expected roster is a complete provider enumeration.
    let inspectSuppliedProjectSetAgainstRoster
        (expected: string list)
        (sources: (string * string) list)
        : Result<Map<string, string list>, SyntaxDiagnostic> =
        if isNull (box expected) || List.isEmpty expected then
            error "project-roster" "<projects>" "expected project roster is absent or empty"
        else
            let rec validate seen remaining =
                match remaining with
                | [] -> Ok seen
                | path :: rest ->
                    if not (normalized path) || not (discoverableProject path) then
                        error "project-roster" path "expected identity must be a normalized discoverable project path"
                    elif Set.contains path seen then
                        error "project-roster" path "duplicate expected project identity"
                    else
                        validate (Set.add path seen) rest
            match validate Set.empty expected with
            | Error diagnostic -> Error diagnostic
            | Ok expectedSet ->
                match inspectSuppliedProjectSet sources with
                | Error diagnostic -> Error diagnostic
                | Ok graph ->
                    let suppliedSet = graph |> Map.toSeq |> Seq.map fst |> Set.ofSeq
                    match Set.difference expectedSet suppliedSet |> Seq.tryHead with
                    | Some path -> error "project-roster" path (sprintf "expected project %s is absent from supplied sources" path)
                    | None ->
                        match Set.difference suppliedSet expectedSet |> Seq.tryHead with
                        | Some path -> error "project-roster" path (sprintf "supplied project %s is absent from expected roster" path)
                        | None -> Ok graph

    /// Bind supplied raw XML bytes to a separately supplied digest roster before reducing the
    /// graph. XML decoding follows the byte stream's declaration. Digest provenance and complete
    /// provider enumeration remain external requirements; this function authenticates neither.
    let inspectSuppliedProjectBytesAgainstDigests
        (expected: (string * string) list)
        (sources: (string * byte[]) list)
        : Result<Map<string, string list>, SyntaxDiagnostic> =
        if isNull (box expected) || List.isEmpty expected then
            error "project-roster" "<projects>" "expected digest roster is absent or empty"
        elif isNull (box sources) || List.isEmpty sources then
            error "project-roster" "<projects>" "supplied project bytes are absent or empty"
        else
            let rec bindDigests bindings remaining =
                match remaining with
                | [] -> Ok bindings
                | (path, digest) :: rest ->
                    if not (normalized path) || not (discoverableProject path) then
                        error "project-roster" path "digest roster identity must be a normalized discoverable project path"
                    elif Map.containsKey path bindings then
                        error "project-source-digest" path "duplicate expected digest binding"
                    elif isNull digest || not (Regex.IsMatch(digest, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant)) then
                        error "project-source-digest" path "expected SHA-256 digest must be 64 lowercase hexadecimal characters"
                    else
                        bindDigests (Map.add path digest bindings) rest
            match bindDigests Map.empty expected with
            | Error diagnostic -> Error diagnostic
            | Ok digests ->
                let settings = XmlReaderSettings()
                settings.DtdProcessing <- DtdProcessing.Prohibit
                settings.XmlResolver <- null
                let rec readSources
                    (seen: Set<string>)
                    (decoded: (string * string) list)
                    (remaining: (string * byte[]) list) =
                    match remaining with
                    | [] ->
                        inspectSuppliedProjectSetAgainstRoster (expected |> List.map fst) (List.rev decoded)
                    | (path, bytes) :: rest ->
                        if not (normalized path) || not (discoverableProject path) then
                            error "project-roster" path "supplied bytes identity must be a normalized discoverable project path"
                        elif Set.contains path seen then
                            error "project-roster" path "duplicate supplied project bytes identity"
                        elif isNull bytes then
                            error "project-source-xml" path "supplied project bytes are absent"
                        else
                            match Map.tryFind path digests with
                            | None -> error "project-roster" path "supplied project bytes are absent from expected digest roster"
                            | Some expectedDigest ->
                                let actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
                                if actual <> expectedDigest then
                                    error "project-source-digest" path "supplied bytes do not match expected SHA-256 digest"
                                else
                                    try
                                        use input = new MemoryStream(bytes, false)
                                        use reader = XmlReader.Create(input, settings)
                                        let document = XDocument.Load(reader)
                                        let xml = document.ToString(SaveOptions.DisableFormatting)
                                        readSources (Set.add path seen) ((path, xml) :: decoded) rest
                                    with
                                    | :? XmlException as ex -> error "project-source-xml" path ex.Message
                                    | :? ArgumentException as ex -> error "project-source-xml" path ex.Message
                readSources Set.empty [] sources

    /// A local observation of one caller-supplied implicit file. The result does not establish
    /// nearest-file selection, import closure, source provenance, or a Rule (b) graph verdict.
    type SuppliedImplicitObservation = NoDirectReferenceInSuppliedXml

    let inspectSuppliedImplicitXml
        (sourcePath: string)
        (xml: string)
        : Result<SuppliedImplicitObservation, SyntaxDiagnostic> =
        let fileName =
            if isNull sourcePath then "" else sourcePath.Split('/') |> Array.last
        if not (normalized sourcePath)
           || Regex.IsMatch(sourcePath, "^[A-Za-z]:", RegexOptions.CultureInvariant)
           || (fileName <> "Directory.Build.props" && fileName <> "Directory.Build.targets") then
            error "implicit-source-path" sourcePath "expected a normalized Directory.Build.props/targets path"
        elif isNull xml then
            error "implicit-source-xml" sourcePath "implicit source XML is absent"
        else
            try
                let settings = XmlReaderSettings()
                settings.DtdProcessing <- DtdProcessing.Prohibit
                settings.XmlResolver <- null
                use input = new StringReader(xml)
                use reader = XmlReader.Create(input, settings)
                let document = XDocument.Load(reader)
                if isNull document.Root || document.Root.Name.LocalName <> "Project" then
                    error "implicit-source-xml" sourcePath "implicit source XML root must be Project"
                elif setsTargetsOverride document then
                    error "implicit-source-selection" sourcePath "supplied implicit XML sets DirectoryBuildTargetsPath; requires MSBuild import evaluation"
                elif document.Descendants()
                     |> Seq.exists (fun element ->
                         isProjectReference element) then
                    error "implicit-project-reference" sourcePath "supplied implicit XML contains ProjectReference"
                elif document.Descendants()
                     |> Seq.exists (fun element -> element.Name.LocalName = "Import") then
                    error "implicit-import" sourcePath "supplied implicit XML has unresolved Import closure"
                elif document.Descendants()
                     |> Seq.exists (fun element ->
                         let itemName = element.Attribute(XName.Get("ItemName"))
                         element.Name.LocalName = "Output"
                         && not (isNull itemName)
                         && (String.Equals(itemName.Value, "ProjectReference", StringComparison.OrdinalIgnoreCase)
                             || ([ "$("; "@("; "%(" ]
                                 |> List.exists (fun token -> itemName.Value.Contains(token, StringComparison.Ordinal))))) then
                    error "implicit-task-output" sourcePath "supplied implicit XML may emit ProjectReference"
                else
                    Ok NoDirectReferenceInSuppliedXml
            with
            | :? XmlException as ex -> error "implicit-source-xml" sourcePath ex.Message
            | :? ArgumentException as ex -> error "implicit-source-xml" sourcePath ex.Message
