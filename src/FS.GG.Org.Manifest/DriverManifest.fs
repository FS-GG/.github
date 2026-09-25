namespace FS.GG.Org.Manifest

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open System.Text.Json

module DriverManifest =
    type private FileEntry =
        { Path: string
          Sha256: string
          Executable: bool }

    type private SkillEntry =
        { Id: string
          Scope: string
          Sha256: string
          TreeSha256: string
          Files: FileEntry list
          SuppliedBy: string
          MaterializesWhen: string }

    type Manifest =
        private
            { Skills: SkillEntry list }

    let private safeRelative (value: string) =
        not (String.IsNullOrWhiteSpace value)
        && not (value.StartsWith("/", StringComparison.Ordinal))
        && not (value.Contains(char 92))
        && not (value.Contains(':'))
        && not (value.Contains(char 0))
        && (value.Split('/')
            |> Array.forall (fun segment -> segment <> "" && segment <> "." && segment <> ".."))

    let private sha256 (value: string) =
        value.Length = 64
        && value |> Seq.forall (fun c -> c >= '0' && c <= '9' || c >= 'a' && c <= 'f')

    let private property (name: string) (element: JsonElement) =
        let mutable value = Unchecked.defaultof<JsonElement>
        if element.ValueKind = JsonValueKind.Object && element.TryGetProperty(name, &value) then Some value else None

    let private requiredString (errors: ResizeArray<string>) (location: string) (name: string) (element: JsonElement) =
        match property name element with
        | Some value when value.ValueKind = JsonValueKind.String ->
            let text = value.GetString()
            if String.IsNullOrWhiteSpace text then
                errors.Add($"{location}.{name}: missing or blank")
                None
            else Some text
        | _ ->
            errors.Add($"{location}.{name}: missing or non-string")
            None

    // The live Python producer uses json.dumps(..., ensure_ascii=False). System.Text.Json's
    // encoder escapes non-ASCII (and its relaxed encoder still escapes non-BMP and U+2028),
    // so its bytes would validate a different tree. This small writer mirrors Python's
    // string escaping and rejects a lone UTF-16 surrogate rather than changing bytes.
    let private quoted (value: string) =
        let output = StringBuilder().Append('"')
        let mutable index = 0
        while index < value.Length do
            let ch = value.[index]
            match ch with
            | '"' -> output.Append("\\\"") |> ignore
            | '\\' -> output.Append("\\\\") |> ignore
            | '\b' -> output.Append("\\b") |> ignore
            | '\f' -> output.Append("\\f") |> ignore
            | '\n' -> output.Append("\\n") |> ignore
            | '\r' -> output.Append("\\r") |> ignore
            | '\t' -> output.Append("\\t") |> ignore
            | _ when ch < char 32 -> output.Append("\\u").Append((int ch).ToString("x4")) |> ignore
            | _ when Char.IsHighSurrogate ch ->
                if index + 1 >= value.Length || not (Char.IsLowSurrogate value.[index + 1]) then
                    invalidArg (nameof value) "lone UTF-16 surrogate in manifest string"
                output.Append(ch).Append(value.[index + 1]) |> ignore
                index <- index + 1
            | _ when Char.IsLowSurrogate ch -> invalidArg (nameof value) "lone UTF-16 surrogate in manifest string"
            | _ -> output.Append(ch) |> ignore
            index <- index + 1
        output.Append('"').ToString()

    // Python sorts strings by Unicode scalar value. UTF-8 byte order preserves that order;
    // .NET's ordinal/F# string order compares UTF-16 units and reverses some astral/BMP pairs.
    let private comparePython (left: string) (right: string) =
        let a = Encoding.UTF8.GetBytes left
        let b = Encoding.UTF8.GetBytes right
        let mutable index = 0
        let mutable answer = 0
        while answer = 0 && index < min a.Length b.Length do
            answer <- compare a.[index] b.[index]
            index <- index + 1
        if answer <> 0 then answer else compare a.Length b.Length

    let private fileBytes (files: FileEntry list) =
        files
        |> List.sortWith (fun left right -> comparePython left.Path right.Path)
        |> List.map (fun file ->
            "{\"path\":" + quoted file.Path + ",\"sha256\":" + quoted file.Sha256
            + ",\"executable\":" + (if file.Executable then "true" else "false") + "}")
        |> String.concat ","
        |> fun values -> Encoding.UTF8.GetBytes("[" + values + "]")

    let private digest (bytes: byte array) =
        SHA256.HashData bytes |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()

    let private duplicate values =
        List.length values <> (values |> Set.ofList |> Set.count)

    let private duplicateProperties (root: JsonElement) =
        let errors = ResizeArray<string>()
        let rec inspect location (element: JsonElement) =
            match element.ValueKind with
            | JsonValueKind.Object ->
                let names = HashSet<string>(StringComparer.Ordinal)
                for item in element.EnumerateObject() do
                    let child = $"{location}.{item.Name}"
                    if not (names.Add item.Name) then errors.Add($"{child}: duplicate property")
                    inspect child item.Value
            | JsonValueKind.Array ->
                element.EnumerateArray()
                |> Seq.iteri (fun index child -> inspect $"{location}[{index}]" child)
            | _ -> ()
        inspect "$" root
        List.ofSeq errors

    let parse (expectedRoot: string) (json: string) =
        if not (safeRelative expectedRoot) then
            Error [ "expectedRoot: unsafe repository-relative producer root" ]
        else
            try
                use document = JsonDocument.Parse json
                let root = document.RootElement
                let errors = ResizeArray<string>()
                for violation in duplicateProperties root do errors.Add violation
                let schema = property "schemaVersion" root
                let schemaIsTwo =
                    match schema with
                    | Some value when value.ValueKind = JsonValueKind.Number ->
                        let mutable version = 0
                        value.TryGetInt32(&version) && version = 2
                    | _ -> false
                if not schemaIsTwo then
                    errors.Add("schemaVersion: expected 2")

                let skills =
                    match property "skills" root with
                    | Some value when value.ValueKind = JsonValueKind.Array ->
                        value.EnumerateArray()
                        |> Seq.mapi (fun index skill ->
                            let location = $"skills[{index}]"
                            let id = requiredString errors location "id" skill
                            let scope = requiredString errors location "scope" skill
                            let skillSha = requiredString errors location "sha256" skill
                            let treeSha = requiredString errors location "tree-sha256" skill
                            let producer = requiredString errors location "supplied-by" skill
                            let materializes = requiredString errors location "materializes-when" skill
                            let files =
                                match property "files" skill with
                                | Some value when value.ValueKind = JsonValueKind.Array ->
                                    value.EnumerateArray()
                                    |> Seq.mapi (fun fileIndex file ->
                                        let fileLocation = $"{location}.files[{fileIndex}]"
                                        let path = requiredString errors fileLocation "path" file
                                        let fileSha = requiredString errors fileLocation "sha256" file
                                        let executable =
                                            match property "executable" file with
                                            | Some value when value.ValueKind = JsonValueKind.True || value.ValueKind = JsonValueKind.False ->
                                                Some(value.GetBoolean())
                                            | _ ->
                                                errors.Add($"{fileLocation}.executable: missing or non-boolean")
                                                None
                                        match path, fileSha, executable with
                                        | Some path, Some fileSha, Some executable ->
                                            if not (safeRelative path) then errors.Add($"{fileLocation}.path: unsafe relative path")
                                            if not (sha256 fileSha) then errors.Add($"{fileLocation}.sha256: expected lowercase SHA-256")
                                            Some { Path = path; Sha256 = fileSha; Executable = executable }
                                        | _ -> None)
                                    |> Seq.choose (fun value -> value)
                                    |> Seq.toList
                                | _ ->
                                    errors.Add($"{location}.files: missing or non-array")
                                    []
                            if List.isEmpty files then errors.Add($"{location}.files: empty")
                            if not (files |> List.exists (fun file -> file.Path = "SKILL.md")) then
                                errors.Add($"{location}.files: missing SKILL.md")
                            if duplicate (files |> List.map _.Path) then errors.Add($"{location}.files: duplicate path")
                            match id, scope, skillSha, treeSha, producer, materializes with
                            | Some id, Some scope, Some skillSha, Some treeSha, Some producer, Some materializes ->
                                if not (safeRelative id) || id.Contains('/') then errors.Add($"{location}.id: unsafe identifier")
                                if not (sha256 skillSha) then errors.Add($"{location}.sha256: expected lowercase SHA-256")
                                if not (sha256 treeSha) then errors.Add($"{location}.tree-sha256: expected lowercase SHA-256")
                                if producer <> $"{expectedRoot}/{id}" then errors.Add($"{location}.supplied-by: wrong producer root")
                                if not (List.isEmpty files) && digest (fileBytes files) <> treeSha then
                                    errors.Add($"{location}.tree-sha256: files digest mismatch")
                                Some { Id = id; Scope = scope; Sha256 = skillSha; TreeSha256 = treeSha
                                       Files = files; SuppliedBy = producer; MaterializesWhen = materializes }
                            | _ -> None)
                        |> Seq.choose (fun value -> value)
                        |> Seq.toList
                    | _ ->
                        errors.Add("skills: missing or non-array")
                        []
                if List.isEmpty skills then errors.Add("skills: empty")
                if duplicate (skills |> List.map _.Id) then errors.Add("skills: duplicate id")
                if errors.Count > 0 then Error(List.ofSeq errors) else Ok { Skills = skills }
            with :? JsonException as error -> Error [ $"json: malformed ({error.Message})" ]

    let renderCanonical manifest =
        let lines = ResizeArray<string>()
        lines.Add("{")
        lines.Add("  \"schemaVersion\": 2,")
        lines.Add("  \"skills\": [")
        let skills = manifest.Skills |> List.sortWith (fun left right -> comparePython left.Id right.Id) |> List.toArray
        for skillIndex in 0 .. skills.Length - 1 do
            let skill = skills.[skillIndex]
            lines.Add("    {")
            lines.Add("      \"id\": " + quoted skill.Id + ",")
            lines.Add("      \"scope\": " + quoted skill.Scope + ",")
            lines.Add("      \"sha256\": " + quoted skill.Sha256 + ",")
            lines.Add("      \"tree-sha256\": " + quoted skill.TreeSha256 + ",")
            lines.Add("      \"files\": [")
            let files = skill.Files |> List.sortWith (fun left right -> comparePython left.Path right.Path) |> List.toArray
            for fileIndex in 0 .. files.Length - 1 do
                let file = files.[fileIndex]
                lines.Add("        {")
                lines.Add("          \"path\": " + quoted file.Path + ",")
                lines.Add("          \"sha256\": " + quoted file.Sha256 + ",")
                lines.Add("          \"executable\": " + (if file.Executable then "true" else "false"))
                lines.Add("        }" + (if fileIndex < files.Length - 1 then "," else ""))
            lines.Add("      ],")
            lines.Add("      \"supplied-by\": " + quoted skill.SuppliedBy + ",")
            lines.Add("      \"materializes-when\": " + quoted skill.MaterializesWhen)
            lines.Add("    }" + (if skillIndex < skills.Length - 1 then "," else ""))
        lines.Add("  ]")
        lines.Add("}")
        Encoding.UTF8.GetBytes(String.Join("\n", lines) + "\n")
