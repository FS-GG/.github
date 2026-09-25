namespace FS.GG.Org.Policy

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open System.Xml
open System.Xml.Linq

/// Pure single-project XML adapter for Rule (b). Caller-supplied bytes and project identity are
/// inputs; project discovery, source authentication, and installed receiver wiring are separate.
module ProjectReferenceXml =
    let private error code path message =
        Error { Code = code; Path = path; Message = message }

    let private normalized (path: string) =
        not (String.IsNullOrWhiteSpace path)
        && not (path.StartsWith("/", StringComparison.Ordinal))
        && not (path.Contains('\\'))
        && not (path.Contains("//", StringComparison.Ordinal))
        && (path.Split('/') |> Array.forall (fun part -> part <> "" && part <> "." && part <> ".."))

    let private resolve (projectPath: string) (includePath: string) =
        let relative = includePath.Replace('\\', '/')
        if String.IsNullOrWhiteSpace relative
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
                        element.Name.LocalName = "ProjectReference" && inTarget element)
                let removedReference =
                    document.Descendants()
                    |> Seq.exists (fun element ->
                        element.Name.LocalName = "ProjectReference"
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
                        if element.Name.LocalName = "ProjectReference" && invalid.IsNone then
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
                elif document.Descendants()
                     |> Seq.exists (fun element ->
                         String.Equals(element.Name.LocalName, "ProjectReference", StringComparison.OrdinalIgnoreCase)) then
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
