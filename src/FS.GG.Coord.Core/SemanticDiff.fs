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

    /// The line with every word run blanked and every separator run kept verbatim.
    ///
    /// A single word substitution preserves this exactly, so two lines can only be a rename pair if
    /// their skeletons are equal.  That makes it a sound bucket key: pairing may scan one bucket
    /// instead of every added line, which matters because the diff shape this module exists for — a
    /// BULK rename — is precisely the one where both sides are large and a quadratic scan would not
    /// finish.
    let private skeleton (parts: string[]) =
        parts
        |> Array.map (fun run -> if isWordRun run then "\000" else run)
        |> String.concat "\001"

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

    /// A token pair is only a rename candidate when both sides could plausibly BE an identifier or a
    /// piece of authored text.  Two classes of word run are structurally indistinguishable from a rename
    /// and are never one:
    ///
    ///   * a content-addressed digest — every kit change rewrites `sha256`/`tree-sha256` values, and
    ///     `"3e73eb76…"` -> `"f7e1d784…"` is a perfectly formed single word substitution on an ALIGNED
    ///     line, so alignment alone cannot reject it (.github#2144 repair-phase round 1, finding 1);
    ///   * a run with no letter at all — a version bump or a count is a value edit, not a rename.
    ///
    /// This is a plausibility filter, not a taste filter: it rejects only what cannot be a renamed name.
    let private plausibleRenameToken (token: string) =
        let digestLike = token.Length >= 16 && Regex.IsMatch(token, @"^[0-9a-fA-F]+$")
        let hasLetter = token |> Seq.exists Char.IsLetter
        hasLetter && not digestLike

    /// What alignment treats as "the same line".
    ///
    /// Indentation is deliberately not part of it.  Wrapping a block in a new scope re-indents every
    /// line, which under exact equality makes the whole block one enormous replace region — and then
    /// `contexts` at one indent pairs with `checks` at another and is reported as a rename at confidence
    /// 90, which is exactly what happened to `Reads.fs` (.github#2144 repair-phase round 1, finding 1).
    /// Two lines differing ONLY by indentation cannot be a rename anyway: `singleSubstitution` requires
    /// every differing run to be a word run, and leading whitespace is not one.  So ignoring indentation
    /// here removes false regions without hiding a single real rename.
    let private alignKey (line: string) = line.Trim()

    /// The lines occurring exactly once in BOTH slices.  Patience diff's insight: a line that is unique
    /// on each side is an anchor no plausible alignment moves past, so it can be matched with no search.
    let private uniqueCommon (oldLines: string[]) (newLines: string[]) oldLo oldHi newLo newHi =
        let count (lines: string[]) lo hi =
            let counts = Collections.Generic.Dictionary<string, int>()

            for index in lo .. hi - 1 do
                let key = alignKey lines[index]

                counts[key] <-
                    (match counts.TryGetValue key with
                     | true, n -> n
                     | _ -> 0)
                    + 1

            counts

        let oldCounts = count oldLines oldLo oldHi
        let newCounts = count newLines newLo newHi

        let firstIndex (lines: string[]) lo hi key =
            let mutable found = -1
            let mutable index = lo

            while found < 0 && index < hi do
                if alignKey lines[index] = key then found <- index
                index <- index + 1

            found

        [| for KeyValue(key, n) in oldCounts do
               if n = 1 then
                   match newCounts.TryGetValue key with
                   | true, 1 ->
                       yield firstIndex oldLines oldLo oldHi key, firstIndex newLines newLo newHi key
                   | _ -> () |]
        |> Array.sortBy fst

    /// The longest strictly increasing subsequence by second coordinate, over anchors already sorted by
    /// the first.  Anchors that survive are a consistent, non-crossing alignment.
    let private longestIncreasing (anchors: (int * int)[]) =
        if anchors.Length = 0 then
            [||]
        else
            let tailIndex = ResizeArray<int>()
            let previous = Array.create anchors.Length -1

            for index in 0 .. anchors.Length - 1 do
                let _, value = anchors[index]
                let mutable lo = 0
                let mutable hi = tailIndex.Count

                while lo < hi do
                    let mid = (lo + hi) / 2
                    if snd anchors[tailIndex[mid]] < value then lo <- mid + 1 else hi <- mid

                if lo > 0 then previous[index] <- tailIndex[lo - 1]
                if lo = tailIndex.Count then tailIndex.Add index else tailIndex[lo] <- index

            let result = ResizeArray<int * int>()
            let mutable cursor = tailIndex[tailIndex.Count - 1]

            while cursor >= 0 do
                result.Add anchors[cursor]
                cursor <- previous[cursor]

            result.Reverse()
            result.ToArray()

    /// The diff's REPLACE regions: maximal runs of removed lines paired with the added lines that took
    /// their place, as `removed, added`.
    ///
    /// This is the fact the module was missing.  Discovery used to take the whole-file multiset
    /// difference and let ANY removed line pair with ANY added line sharing its skeleton, so a bare
    /// `else` deleted at line 667 paired with a bare `Some` added at line 1057, and a block that merely
    /// changed indentation re-paired against unrelated neighbours — both reported at confidence 90
    /// (.github#2144 repair-phase round 1, finding 1).  Restricting pairing to lines the diff actually
    /// puts opposite each other removes that entire class without weakening real discovery, because a
    /// rename's two lines are opposite each other by construction.
    let private replaceRegions (oldLines: string[]) (newLines: string[]) =
        let regions = ResizeArray<string list * string list>()
        let work = Collections.Generic.Stack<int * int * int * int>()
        work.Push(0, oldLines.Length, 0, newLines.Length)

        while work.Count > 0 do
            let oldLo, oldHi, newLo, newHi = work.Pop()

            // Equal prefix and suffix are alignment nobody has to search for.
            let mutable a = oldLo
            let mutable b = newLo

            while a < oldHi && b < newHi && alignKey oldLines[a] = alignKey newLines[b] do
                a <- a + 1
                b <- b + 1

            let mutable c = oldHi
            let mutable d = newHi

            while c > a && d > b && alignKey oldLines[c - 1] = alignKey newLines[d - 1] do
                c <- c - 1
                d <- d - 1

            if a < c || b < d then
                let anchors = uniqueCommon oldLines newLines a c b d |> longestIncreasing

                if anchors.Length = 0 then
                    // Nothing to align on: this really is one replace region.  Both sides non-empty is
                    // what makes it a REPLACE rather than a pure insertion or deletion, and only a
                    // replace can contain a rename.
                    if a < c && b < d then
                        regions.Add(
                            [ for index in a .. c - 1 -> oldLines[index] ],
                            [ for index in b .. d - 1 -> newLines[index] ]
                        )
                else
                    // Recurse into the gaps between consecutive anchors; the anchors themselves match.
                    let mutable previousOld = a
                    let mutable previousNew = b

                    for anchorOld, anchorNew in anchors do
                        work.Push(previousOld, anchorOld, previousNew, anchorNew)
                        previousOld <- anchorOld + 1
                        previousNew <- anchorNew + 1

                    work.Push(previousOld, c, previousNew, d)

        regions

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

              // Pair ONLY inside a replace region the diff actually produced.  Scoping the search this
              // way is what separates a rename from a coincidence: `else` and `Some` share a skeleton
              // wherever they appear, so with the whole file in scope they paired across 400 lines.
              for removed, added in replaceRegions oldLines newLines do
                  let added = List.toArray added

                  // Within the region, pairing is still by skeleton bucket rather than by position.
                  // That is what keeps discovery robust to a line inserted or deleted INSIDE the region
                  // (the shifted-rename case round 1 of the #2149 chain was repaired for), and it keeps
                  // the bulk-rename shape linear rather than quadratic — each bucket holds the handful
                  // of candidates that could possibly pair, in ascending index order, so first-match
                  // picks exactly what an unbucketed scan would.
                  let buckets = Collections.Generic.Dictionary<string, ResizeArray<int>>()

                  added
                  |> Array.iteri (fun index line ->
                      let key = skeleton (runs line)

                      match buckets.TryGetValue key with
                      | true, bucket -> bucket.Add index
                      | _ ->
                          let bucket = ResizeArray<int>()
                          bucket.Add index
                          buckets[key] <- bucket)

                  let used = Collections.Generic.HashSet<int>()

                  for removedLine in removed do
                      match buckets.TryGetValue(skeleton (runs removedLine)) with
                      | true, bucket ->
                          match
                              bucket
                              |> Seq.tryPick (fun index ->
                                  if used.Contains index then
                                      None
                                  else
                                      singleSubstitution removedLine added[index]
                                      |> Option.map (fun pair -> index, pair))
                          with
                          | Some(index, (oldToken, newToken)) ->
                              used.Add index |> ignore

                              if plausibleRenameToken oldToken && plausibleRenameToken newToken then
                                  yield oldToken, newToken
                          | None -> ()
                      | _ -> () ]
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
