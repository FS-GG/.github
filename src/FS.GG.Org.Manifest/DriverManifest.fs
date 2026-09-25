namespace FS.GG.Org.Manifest

open System
open System.IO
open System.Security.Cryptography
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

    let private fileBytes (files: FileEntry list) =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writer.WriteStartArray()
        for file in files |> List.sortBy _.Path do
            writer.WriteStartObject()
            writer.WriteString("path", file.Path)
            writer.WriteString("sha256", file.Sha256)
            writer.WriteBoolean("executable", file.Executable)
            writer.WriteEndObject()
        writer.WriteEndArray()
        writer.Flush()
        stream.ToArray()

    let private digest (bytes: byte array) =
        SHA256.HashData bytes |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()

    let private duplicate values =
        List.length values <> (values |> Set.ofList |> Set.count)

    let parse (expectedRoot: string) (json: string) =
        if not (safeRelative expectedRoot) then
            Error [ "expectedRoot: unsafe repository-relative producer root" ]
        else
            try
                use document = JsonDocument.Parse json
                let root = document.RootElement
                let errors = ResizeArray<string>()
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
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writer.WriteNumber("schemaVersion", 2)
        writer.WritePropertyName("skills")
        writer.WriteStartArray()
        for skill in manifest.Skills |> List.sortBy _.Id do
            writer.WriteStartObject()
            writer.WriteString("id", skill.Id)
            writer.WriteString("scope", skill.Scope)
            writer.WriteString("sha256", skill.Sha256)
            writer.WriteString("tree-sha256", skill.TreeSha256)
            writer.WritePropertyName("files")
            writer.WriteStartArray()
            for file in skill.Files |> List.sortBy _.Path do
                writer.WriteStartObject()
                writer.WriteString("path", file.Path)
                writer.WriteString("sha256", file.Sha256)
                writer.WriteBoolean("executable", file.Executable)
                writer.WriteEndObject()
            writer.WriteEndArray()
            writer.WriteString("supplied-by", skill.SuppliedBy)
            writer.WriteString("materializes-when", skill.MaterializesWhen)
            writer.WriteEndObject()
        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        Array.append (stream.ToArray()) [| 10uy |]
