namespace FS.GG.Coord

module SemanticDiff =
    open System
    open System.Security.Cryptography
    open System.Text
    open System.Text.RegularExpressions
    open System.Text.Json

    type Classification =
        | StringLiteral
        | CharacterLiteral
        | Comment
        | SerializedKey
        | GoldenText
        | TestText
        | Documentation
        | GeneratedArtifact

    type Disposition =
        | IntendedContractChange
        | IntendedTestOrDocumentationUpdate
        | GeneratedOutput
        | AccidentalFixRequired
        | Unresolved

    type Occurrence =
        { Id: string
          Path: string
          Line: int
          Classification: Classification
          Confidence: int
          Before: string
          After: string
          Disposition: Disposition }

    type Receipt =
        { SchemaVersion: int
          Repository: string
          BaseSha: string
          HeadSha: string
          OldToken: string
          NewToken: string
          DeclaredPaths: string list
          Required: bool
          Occurrences: Occurrence list }

    let classificationName =
        function
        | StringLiteral -> "string-literal"
        | CharacterLiteral -> "character-literal"
        | Comment -> "comment"
        | SerializedKey -> "serialized-key"
        | GoldenText -> "golden-text"
        | TestText -> "test-text"
        | Documentation -> "documentation"
        | GeneratedArtifact -> "generated-artifact"

    let dispositionName =
        function
        | IntendedContractChange -> "intended-contract-change"
        | IntendedTestOrDocumentationUpdate -> "intended-test-doc-update"
        | GeneratedOutput -> "generated-output"
        | AccidentalFixRequired -> "accidental-fix-required"
        | Unresolved -> "unresolved"

    let private dispositionOfName =
        function
        | "intended-contract-change" -> Some IntendedContractChange
        | "intended-test-doc-update" -> Some IntendedTestOrDocumentationUpdate
        | "generated-output" -> Some GeneratedOutput
        | "accidental-fix-required" -> Some AccidentalFixRequired
        | "unresolved" -> Some Unresolved
        | _ -> None

    let private containsToken (token: string) (text: string) =
        Regex.IsMatch(text, $@"(?<![A-Za-z0-9_]){Regex.Escape token}(?![A-Za-z0-9_])")

    let private digest (value: string) =
        SHA256.HashData(Encoding.UTF8.GetBytes value)
        |> Convert.ToHexString
        |> fun hash -> hash.ToLowerInvariant()

    let private classify (path: string) (line: string) =
        let lower = path.ToLowerInvariant()

        if lower.EndsWith(".md") || lower.StartsWith("docs/") then
            Documentation, 100
        elif lower.Contains("generated") || lower.EndsWith(".g.fs") then
            GeneratedArtifact, 95
        elif lower.Contains("golden") || lower.Contains("snapshot") then
            GoldenText, 95
        elif lower.Contains("test") then
            TestText, 90
        elif line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("(*") then
            Comment, 98
        elif Regex.IsMatch(line, @"'([^'\\]|\\.)*'") then
            CharacterLiteral, 90
        elif line.Contains '"' then
            if Regex.IsMatch(line, "\\\"[^\\\"]+\\\"\\s*:") then
                SerializedKey, 85
            else
                StringLiteral, 90
        else
            StringLiteral, 0

    let private renameProjection (oldToken: string) (newToken: string) (line: string) =
        Regex.Replace(line, $@"(?<![A-Za-z0-9_]){Regex.Escape oldToken}(?![A-Za-z0-9_])", newToken)

    /// Aligns rename-shaped lines by their token-substituted content instead of line number.  Insertions
    /// and deletions elsewhere in a file therefore cannot hide a semantic occurrence.  Repeated equal
    /// lines are paired first-to-first, which keeps the inventory deterministic.
    let inventory (path: string) (before: string) (after: string) (oldToken: string) (newToken: string) =
        let oldLines = before.Replace("\r\n", "\n").Split '\n'
        let newLines = after.Replace("\r\n", "\n").Split '\n'

        let candidates =
            newLines
            |> Array.mapi (fun index line -> index, line)
            |> Array.filter (fun (_, line) -> containsToken newToken line)

        let used = Collections.Generic.HashSet<int>()

        [ for oldIndex, oldLine in oldLines |> Array.indexed do
              if containsToken oldToken oldLine then
                  let projected = renameProjection oldToken newToken oldLine

                  match
                      candidates
                      |> Array.tryFind (fun (newIndex, newLine) -> not (used.Contains newIndex) && newLine = projected)
                  with
                  | Some(newIndex, newLine) ->
                      used.Add newIndex |> ignore
                      let classification, confidence = classify path oldLine

                      if confidence > 0 then
                          let id =
                              digest
                                  $"v2\n{path}\n{oldIndex + 1}\n{newIndex + 1}\n{classificationName classification}\n{oldLine}\n{newLine}"

                          yield
                              { Id = id
                                Path = path
                                Line = oldIndex + 1
                                Classification = classification
                                Confidence = confidence
                                Before = oldLine
                                After = newLine
                                Disposition = Unresolved }
                  | None -> () ]

    /// Every maximal word run and separator run of a line, in order.  Word runs use exactly the
    /// character class `containsToken`'s look-arounds use, so a run boundary here IS a rename boundary
    /// there — the discovery below and the inventory above cannot disagree about what a token is.
    let private runs (line: string) =
        Regex.Matches(line, @"[A-Za-z0-9_]+|[^A-Za-z0-9_]+")
        |> Seq.map (fun m -> m.Value)
        |> Seq.toArray

    let private isWordRun (value: string) = Regex.IsMatch(value, @"^[A-Za-z0-9_]+$")

    /// The one word substitution that turns `before` into `after`, when the two lines have the rename
    /// shape and nothing else: identical run structure, differing only at word runs, and every differing
    /// position carrying the SAME old/new pair.  A line that changed in two unrelated ways is not a
    /// rename and is deliberately not guessed at.
    let private singleSubstitution (before: string) (after: string) =
        let left = runs before
        let right = runs after

        if left.Length <> right.Length then
            None
        else
            let differing =
                Array.zip left right |> Array.filter (fun (x, y) -> x <> y)

            match differing |> Array.distinct with
            | [| (oldToken, newToken) |] when
                oldToken <> newToken && isWordRun oldToken && isWordRun newToken
                ->
                Some(oldToken, newToken)
            | _ -> None

    /// The lines of `before` that `after` does not also contain, counting duplicates — the deletion side
    /// of the diff, without needing git to hand us hunks.
    let private surplus (source: string[]) (other: string[]) =
        let counts = Collections.Generic.Dictionary<string, int>()

        for line in other do
            counts[line] <- (match counts.TryGetValue line with
                             | true, n -> n
                             | _ -> 0)
                            + 1

        [ for line in source do
              match counts.TryGetValue line with
              | true, n when n > 0 ->
                  counts[line] <- n - 1
              | _ -> yield line ]

    /// Recovers the rename tokens from the live diff itself, for the delivery path where no receipt
    /// supplies them.
    ///
    /// This exists because the configured threshold counts semantic OCCURRENCES, and the only other
    /// number available without a receipt — the changed-FILE count — is a different quantity that is
    /// always a lower bound.  Substituting it let a one-file rename with six quoted occurrences report
    /// `1`, fall under the default threshold of 5, and keep the receipt mechanically optional
    /// (.github#2144); an omitted receipt must not be able to answer the question it exists to answer.
    ///
    /// Each element of `files` is `path, contentAtBase, contentAtHead`.  The result is deduplicated and
    /// ordered, so the same diff always yields the same tokens in the same order.
    let discoverRenames (files: (string * string * string) list) =
        [ for _, before, after in files do
              let oldLines = before.Replace("\r\n", "\n").Split '\n'
              let newLines = after.Replace("\r\n", "\n").Split '\n'
              let removed = surplus oldLines newLines
              let added = surplus newLines oldLines |> List.toArray
              let used = Collections.Generic.HashSet<int>()

              for removedLine in removed do
                  match
                      added
                      |> Array.indexed
                      |> Array.tryPick (fun (index, addedLine) ->
                          if used.Contains index then
                              None
                          else
                              singleSubstitution removedLine addedLine
                              |> Option.map (fun pair -> index, pair))
                  with
                  | Some(index, pair) ->
                      used.Add index |> ignore
                      yield pair
                  | None -> () ]
        |> List.distinct
        |> List.sort

    /// Every occurrence the discovered renames account for across the same live files.  This is the
    /// occurrence count the threshold is measured against when no receipt was submitted.
    let discoveredOccurrences (files: (string * string * string) list) =
        let pairs = discoverRenames files

        [ for oldToken, newToken in pairs do
              for path, before, after in files do
                  yield! inventory path before after oldToken newToken ]
        |> List.distinctBy _.Id

    let activationRequired (threshold: int) (occurrenceCount: int) (commitMessage: string) itemBody =
        let declaration (text: string) =
            text.Replace("\r\n", "\n").Split '\n'
            |> Array.exists (fun line -> Regex.IsMatch(line, @"^\s*Bulk rename:\s*true\s*$", RegexOptions.IgnoreCase))

        occurrenceCount >= threshold
        || commitMessage.Contains("[bulk-rename]", StringComparison.OrdinalIgnoreCase)
        || declaration commitMessage
        || (itemBody |> Option.exists declaration)

    let receipt
        (repository: string)
        (baseSha: string)
        (headSha: string)
        (oldToken: string)
        (newToken: string)
        (declaredPaths: string list)
        (required: bool)
        (occurrences: Occurrence list)
        =
        { SchemaVersion = 1
          Repository = repository
          BaseSha = baseSha
          HeadSha = headSha
          OldToken = oldToken
          NewToken = newToken
          DeclaredPaths = declaredPaths |> List.distinct |> List.sort
          Required = required
          Occurrences = occurrences }

    let validate (expectedBase: string) (expectedHead: string) (receipt: Receipt) =
        [ if receipt.SchemaVersion <> 1 then
              "diff-audit receipt schema version is unsupported"
          if String.IsNullOrWhiteSpace receipt.Repository then
              "diff-audit repository is missing"
          if receipt.BaseSha <> expectedBase then
              "diff-audit receipt base SHA is stale"
          if receipt.HeadSha <> expectedHead then
              "diff-audit receipt head SHA is stale"
          if
              String.IsNullOrWhiteSpace receipt.OldToken
              || String.IsNullOrWhiteSpace receipt.NewToken
          then
              "diff-audit rename tokens are missing"
          if List.isEmpty receipt.DeclaredPaths then
              "diff-audit declared paths are missing"
          if receipt.Required && List.isEmpty receipt.Occurrences then
              "required diff-audit inventory is empty"
          let ids = receipt.Occurrences |> List.map _.Id

          if ids |> List.distinct |> List.length <> ids.Length then
              "diff-audit occurrence ids are duplicated"

          for occurrence in receipt.Occurrences do
              if
                  String.IsNullOrWhiteSpace occurrence.Id
                  || String.IsNullOrWhiteSpace occurrence.Path
              then
                  "diff-audit occurrence identity is missing"

              if occurrence.Line < 1 then
                  "diff-audit occurrence line is invalid"

              if occurrence.Confidence < 0 || occurrence.Confidence > 100 then
                  "diff-audit occurrence confidence is invalid"

              if occurrence.Disposition = Unresolved then
                  "diff-audit has an unresolved occurrence" ]

    let validateAgainst (expected: Receipt) (submitted: Receipt) =
        let identity occurrence =
            occurrence.Id,
            occurrence.Path,
            occurrence.Line,
            occurrence.Classification,
            occurrence.Confidence,
            occurrence.Before,
            occurrence.After

        [ yield! validate expected.BaseSha expected.HeadSha submitted
          if submitted.Repository <> expected.Repository then
              "diff-audit repository does not match the live inventory"
          if
              submitted.OldToken <> expected.OldToken
              || submitted.NewToken <> expected.NewToken
          then
              "diff-audit rename tokens do not match the live inventory"
          if submitted.DeclaredPaths <> expected.DeclaredPaths then
              "diff-audit paths do not match the live inventory"
          if submitted.Required <> expected.Required then
              "diff-audit activation does not match the live inventory"
          if
              (submitted.Occurrences |> List.map identity)
              <> (expected.Occurrences |> List.map identity)
          then
              "diff-audit occurrences do not match the live inventory" ]

    let toJson (receipt: Receipt) =
        use stream = new IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writer.WriteStartObject()
        writer.WriteNumber("schemaVersion", receipt.SchemaVersion)
        writer.WriteString("repository", receipt.Repository)
        writer.WriteString("baseSha", receipt.BaseSha)
        writer.WriteString("headSha", receipt.HeadSha)
        writer.WriteString("oldToken", receipt.OldToken)
        writer.WriteString("newToken", receipt.NewToken)
        writer.WriteBoolean("required", receipt.Required)
        writer.WriteStartArray("declaredPaths")
        receipt.DeclaredPaths |> List.iter writer.WriteStringValue
        writer.WriteEndArray()
        writer.WriteStartArray("occurrences")

        for occurrence in receipt.Occurrences do
            writer.WriteStartObject()
            writer.WriteString("id", occurrence.Id)
            writer.WriteString("path", occurrence.Path)
            writer.WriteNumber("line", occurrence.Line)
            writer.WriteString("classification", classificationName occurrence.Classification)
            writer.WriteNumber("confidence", occurrence.Confidence)
            writer.WriteString("before", occurrence.Before)
            writer.WriteString("after", occurrence.After)
            writer.WriteString("disposition", dispositionName occurrence.Disposition)
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    let ofJson (json: string) =
        try
            use document = JsonDocument.Parse json
            let root = document.RootElement
            let str (name: string) = root.GetProperty(name).GetString()

            let classification =
                function
                | "string-literal" -> Some StringLiteral
                | "character-literal" -> Some CharacterLiteral
                | "comment" -> Some Comment
                | "serialized-key" -> Some SerializedKey
                | "golden-text" -> Some GoldenText
                | "test-text" -> Some TestText
                | "documentation" -> Some Documentation
                | "generated-artifact" -> Some GeneratedArtifact
                | _ -> None

            let rows =
                [ for row in root.GetProperty("occurrences").EnumerateArray() do
                      match
                          classification (row.GetProperty("classification").GetString()),
                          dispositionOfName (row.GetProperty("disposition").GetString())
                      with
                      | Some kind, Some decision ->
                          yield
                              Ok
                                  { Id = row.GetProperty("id").GetString()
                                    Path = row.GetProperty("path").GetString()
                                    Line = row.GetProperty("line").GetInt32()
                                    Classification = kind
                                    Confidence = row.GetProperty("confidence").GetInt32()
                                    Before = row.GetProperty("before").GetString()
                                    After = row.GetProperty("after").GetString()
                                    Disposition = decision }
                      | _ -> yield Error "diff-audit occurrence classification or disposition is unknown" ]

            let errors =
                rows
                |> List.choose (function
                    | Error e -> Some e
                    | _ -> None)

            if not errors.IsEmpty then
                Error errors
            else
                Ok
                    { SchemaVersion = root.GetProperty("schemaVersion").GetInt32()
                      Repository = str "repository"
                      BaseSha = str "baseSha"
                      HeadSha = str "headSha"
                      OldToken = str "oldToken"
                      NewToken = str "newToken"
                      DeclaredPaths = [ for p in root.GetProperty("declaredPaths").EnumerateArray() -> p.GetString() ]
                      Required = root.GetProperty("required").GetBoolean()
                      Occurrences =
                        rows
                        |> List.choose (function
                            | Ok row -> Some row
                            | _ -> None) }
        with ex ->
            Error [ $"diff-audit receipt is malformed: %s{ex.Message}" ]

    let toBase64 receipt =
        toJson receipt |> Encoding.UTF8.GetBytes |> Convert.ToBase64String

    let ofBase64 value =
        try
            value |> Convert.FromBase64String |> Encoding.UTF8.GetString |> ofJson
        with ex ->
            Error [ $"diff-audit receipt base64 is malformed: %s{ex.Message}" ]
