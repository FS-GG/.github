namespace FS.GG.Coord

module SemanticDiff =
    open System
    open System.Security.Cryptography
    open System.Text
    open System.Text.RegularExpressions
    open System.Text.Json

    type Classification = StringLiteral | CharacterLiteral | Comment | SerializedKey | GoldenText | TestText | Documentation | GeneratedArtifact
    type Disposition = IntendedContractChange | IntendedTestOrDocumentationUpdate | GeneratedOutput | AccidentalFixRequired | Unresolved
    type Occurrence =
        { Id: string; Path: string; Line: int; Classification: Classification; Confidence: int
          Before: string; After: string; Disposition: Disposition }
    type Receipt =
        { SchemaVersion: int; Repository: string; BaseSha: string; HeadSha: string; DeclaredPaths: string list
          Required: bool; Occurrences: Occurrence list }

    let classificationName = function
        | StringLiteral -> "string-literal" | CharacterLiteral -> "character-literal" | Comment -> "comment"
        | SerializedKey -> "serialized-key" | GoldenText -> "golden-text" | TestText -> "test-text"
        | Documentation -> "documentation" | GeneratedArtifact -> "generated-artifact"
    let dispositionName = function
        | IntendedContractChange -> "intended-contract-change"
        | IntendedTestOrDocumentationUpdate -> "intended-test-doc-update"
        | GeneratedOutput -> "generated-output" | AccidentalFixRequired -> "accidental-fix-required" | Unresolved -> "unresolved"

    let private dispositionOfName = function
        | "intended-contract-change" -> Some IntendedContractChange
        | "intended-test-doc-update" -> Some IntendedTestOrDocumentationUpdate
        | "generated-output" -> Some GeneratedOutput
        | "accidental-fix-required" -> Some AccidentalFixRequired
        | "unresolved" -> Some Unresolved
        | _ -> None

    let private containsToken (token: string) (text: string) =
        Regex.IsMatch(text, $@"(?<![A-Za-z0-9_]){Regex.Escape token}(?![A-Za-z0-9_])")
    let private digest (value: string) =
        SHA256.HashData(Encoding.UTF8.GetBytes value) |> Convert.ToHexString |> fun hash -> hash.ToLowerInvariant()
    let private classify (path: string) (line: string) =
        let lower = path.ToLowerInvariant()
        if lower.EndsWith(".md") || lower.StartsWith("docs/") then Documentation, 100
        elif lower.Contains("generated") || lower.EndsWith(".g.fs") then GeneratedArtifact, 95
        elif lower.Contains("golden") || lower.Contains("snapshot") then GoldenText, 95
        elif lower.Contains("test") then TestText, 90
        elif line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("(*") then Comment, 98
        elif Regex.IsMatch(line, @"'([^'\\]|\\.)*'") then CharacterLiteral, 90
        elif line.Contains '"' then
            if Regex.IsMatch(line, "\\\"[^\\\"]+\\\"\\s*:") then SerializedKey, 85 else StringLiteral, 90
        else StringLiteral, 0

    /// Pairs changed lines by ordinal. The git edge supplies the before/after blobs; no heuristic is
    /// allowed to silently turn a failed diff read into an empty audit.
    let inventory (path: string) (before: string) (after: string) (oldToken: string) (newToken: string) =
        let oldLines = before.Replace("\r\n", "\n").Split '\n'
        let newLines = after.Replace("\r\n", "\n").Split '\n'
        let count = max oldLines.Length newLines.Length
        [ for index in 0 .. count - 1 do
              let oldLine = if index < oldLines.Length then oldLines[index] else ""
              let newLine = if index < newLines.Length then newLines[index] else ""
              if oldLine <> newLine && containsToken oldToken oldLine && containsToken newToken newLine then
                  let classification, confidence = classify path oldLine
                  if confidence > 0 then
                      let id = digest $"v1\n{path}\n{index + 1}\n{classificationName classification}\n{oldLine}\n{newLine}"
                      yield { Id = id; Path = path; Line = index + 1; Classification = classification; Confidence = confidence
                              Before = oldLine; After = newLine; Disposition = Unresolved } ]

    let receipt (repository: string) (baseSha: string) (headSha: string) (declaredPaths: string list) (required: bool) (occurrences: Occurrence list) =
        { SchemaVersion = 1; Repository = repository; BaseSha = baseSha; HeadSha = headSha
          DeclaredPaths = declaredPaths |> List.distinct |> List.sort; Required = required; Occurrences = occurrences }

    let validate (expectedBase: string) (expectedHead: string) (receipt: Receipt) =
        [ if receipt.SchemaVersion <> 1 then "diff-audit receipt schema version is unsupported"
          if String.IsNullOrWhiteSpace receipt.Repository then "diff-audit repository is missing"
          if receipt.BaseSha <> expectedBase then "diff-audit receipt base SHA is stale"
          if receipt.HeadSha <> expectedHead then "diff-audit receipt head SHA is stale"
          if List.isEmpty receipt.DeclaredPaths then "diff-audit declared paths are missing"
          let ids = receipt.Occurrences |> List.map _.Id
          if ids |> List.distinct |> List.length <> ids.Length then "diff-audit occurrence ids are duplicated"
          for occurrence in receipt.Occurrences do
              if String.IsNullOrWhiteSpace occurrence.Id || String.IsNullOrWhiteSpace occurrence.Path then
                  "diff-audit occurrence identity is missing"
              if occurrence.Line < 1 then "diff-audit occurrence line is invalid"
              if occurrence.Confidence < 0 || occurrence.Confidence > 100 then "diff-audit occurrence confidence is invalid"
              if occurrence.Disposition = Unresolved then
                  "diff-audit has an unresolved occurrence" ]

    let toJson (receipt: Receipt) =
        use stream = new IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writer.WriteStartObject(); writer.WriteNumber("schemaVersion", receipt.SchemaVersion)
        writer.WriteString("repository", receipt.Repository); writer.WriteString("baseSha", receipt.BaseSha); writer.WriteString("headSha", receipt.HeadSha)
        writer.WriteBoolean("required", receipt.Required); writer.WriteStartArray("declaredPaths")
        receipt.DeclaredPaths |> List.iter writer.WriteStringValue; writer.WriteEndArray(); writer.WriteStartArray("occurrences")
        for occurrence in receipt.Occurrences do
            writer.WriteStartObject(); writer.WriteString("id", occurrence.Id); writer.WriteString("path", occurrence.Path); writer.WriteNumber("line", occurrence.Line)
            writer.WriteString("classification", classificationName occurrence.Classification); writer.WriteNumber("confidence", occurrence.Confidence)
            writer.WriteString("before", occurrence.Before); writer.WriteString("after", occurrence.After); writer.WriteString("disposition", dispositionName occurrence.Disposition); writer.WriteEndObject()
        writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    let ofJson (json: string) =
        try
            use document = JsonDocument.Parse json
            let root = document.RootElement
            let str (name: string) = root.GetProperty(name).GetString()
            let classification = function
                | "string-literal" -> Some StringLiteral | "character-literal" -> Some CharacterLiteral | "comment" -> Some Comment
                | "serialized-key" -> Some SerializedKey | "golden-text" -> Some GoldenText | "test-text" -> Some TestText
                | "documentation" -> Some Documentation | "generated-artifact" -> Some GeneratedArtifact | _ -> None
            let rows =
                [ for row in root.GetProperty("occurrences").EnumerateArray() do
                    match classification (row.GetProperty("classification").GetString()), dispositionOfName (row.GetProperty("disposition").GetString()) with
                    | Some kind, Some decision ->
                        yield Ok { Id = row.GetProperty("id").GetString(); Path = row.GetProperty("path").GetString(); Line = row.GetProperty("line").GetInt32()
                                   Classification = kind; Confidence = row.GetProperty("confidence").GetInt32(); Before = row.GetProperty("before").GetString()
                                   After = row.GetProperty("after").GetString(); Disposition = decision }
                    | _ -> yield Error "diff-audit occurrence classification or disposition is unknown" ]
            let errors = rows |> List.choose (function Error e -> Some e | _ -> None)
            if not errors.IsEmpty then Error errors else
            Ok { SchemaVersion = root.GetProperty("schemaVersion").GetInt32(); Repository = str "repository"; BaseSha = str "baseSha"; HeadSha = str "headSha"
                 DeclaredPaths = [ for p in root.GetProperty("declaredPaths").EnumerateArray() -> p.GetString() ]; Required = root.GetProperty("required").GetBoolean()
                 Occurrences = rows |> List.choose (function Ok row -> Some row | _ -> None) }
        with ex -> Error [ $"diff-audit receipt is malformed: %s{ex.Message}" ]

    let toBase64 receipt = toJson receipt |> Encoding.UTF8.GetBytes |> Convert.ToBase64String
    let ofBase64 value =
        try value |> Convert.FromBase64String |> Encoding.UTF8.GetString |> ofJson
        with ex -> Error [ $"diff-audit receipt base64 is malformed: %s{ex.Message}" ]
