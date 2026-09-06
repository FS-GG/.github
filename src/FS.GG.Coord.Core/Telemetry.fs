namespace FS.GG.Coord

open System
open System.Globalization
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions

module private TelemetryJson =
    let sha256 (bytes: byte array) = SHA256.HashData bytes |> Convert.ToHexString |> _.ToLowerInvariant()

    let required label (value: string) =
        if String.IsNullOrWhiteSpace value then Error $"%s{label} must be a non-empty string" else Ok value

    let canonical (node: JsonNode) =
        let rec write (writer: Utf8JsonWriter) (value: JsonNode) =
            match value with
            | null -> writer.WriteNullValue()
            | :? JsonObject as item ->
                writer.WriteStartObject()
                item |> Seq.sortBy _.Key |> Seq.iter (fun pair -> writer.WritePropertyName pair.Key; write writer pair.Value)
                writer.WriteEndObject()
            | :? JsonArray as item ->
                writer.WriteStartArray()
                item |> Seq.iter (write writer)
                writer.WriteEndArray()
            | _ -> value.WriteTo writer
        use stream = new IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false))
        write writer node
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

module CanonicalJson =
    let sha256 bytes = TelemetryJson.sha256 bytes

    let canonicalize (bytes: byte array) =
        try
            let node = JsonNode.Parse(bytes)
            if isNull node then Error "JSON root must not be null"
            else Ok(TelemetryJson.canonical node)
        with :? JsonException as error ->
            Error $"invalid JSON: %s{error.Message}"

module RuntimeUsage =
    type TokenCounts =
        { Input: int64; CachedInput: int64; CacheWriteInput: int64; Output: int64; Reasoning: int64 option; Total: int64 }
    type UsageRow =
        { Timestamp: string; Task: string; SessionId: string; ThreadId: string; TurnId: string; ResponseId: string
          Provider: string; Model: string; Effort: string; RuntimeVersion: string; CoordinationVersion: string
          SddVersion: string; ContractsVersion: string; LedgerSchema: int; Response: TokenCounts; Turn: TokenCounts
          Thread: TokenCounts option; Source: string }
    type Collection = { Rows: UsageRow list; SourceDigest: string }

    let private text (label: string) (node: JsonElement) (name: string) =
        match node.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(value.GetString())) -> Ok(value.GetString())
        | _ -> Error $"%s{label}.%s{name} must be a non-empty string"

    let private counts (label: string) (node: JsonElement) =
        let number (name: string) =
            match node.TryGetProperty name with
            | true, value when value.ValueKind = JsonValueKind.Number ->
                match value.TryGetInt64() with true, count when count >= 0L -> Ok count | _ -> Error $"%s{label}.%s{name} must be a non-negative integer"
            | _ -> Error $"%s{label}.%s{name} must be a non-negative integer"
        match number "input_tokens", number "cached_input_tokens", number "cache_write_input_tokens", number "output_tokens", number "reasoning_output_tokens", number "total_tokens" with
        | Ok input, Ok cached, Ok write, Ok output, Ok reasoning, Ok total when total <> input + output -> Error $"%s{label}.total_tokens must equal input_tokens + output_tokens"
        | Ok input, Ok cached, Ok write, Ok output, Ok reasoning, Ok _ when cached + write > input -> Error $"%s{label} cache token subsets exceed input_tokens"
        | Ok _, Ok _, Ok _, Ok output, Ok reasoning, Ok _ when reasoning > output -> Error $"%s{label}.reasoning_output_tokens exceeds output_tokens"
        | Ok input, Ok cached, Ok write, Ok output, Ok reasoning, Ok total -> Ok { Input = input; CachedInput = cached; CacheWriteInput = write; Output = output; Reasoning = Some reasoning; Total = total }
        | values ->
            [ match values with
              | Error e, _, _, _, _, _ -> yield e | _ -> ()
              match values with
              | _, Error e, _, _, _, _ -> yield e | _ -> ()
              match values with
              | _, _, Error e, _, _, _ -> yield e | _ -> ()
              match values with
              | _, _, _, Error e, _, _ -> yield e | _ -> ()
              match values with
              | _, _, _, _, Error e, _ -> yield e | _ -> ()
              match values with
              | _, _, _, _, _, Error e -> yield e | _ -> () ] |> String.concat "; " |> Error

    let private ensureVersions (task: string) (coordination: string) (sdd: string) (contracts: string) =
        [ "task", task; "coordination version", coordination; "SDD version", sdd; "contracts version", contracts ]
        |> List.choose (fun (label, value) -> match TelemetryJson.required label value with Ok _ -> None | Error e -> Some e)

    let collectCodex (task: string) (turnId: string option) (allResponses: bool) (sinceUtc: string option) (untilUtc: string option) (coordinationVersion: string) (sddVersion: string) (contractsVersion: string) (bytes: byte array) =
        try
            let errors = ensureVersions task coordinationVersion sddVersion contractsVersion
            if not errors.IsEmpty then Error errors else
            let contexts = Collections.Generic.Dictionary<string, string * string>()
            let records = ResizeArray<string option * JsonElement>()
            let mutable runtimeVersion = ""
            Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            |> Array.iteri (fun index line ->
                try
                    use document = JsonDocument.Parse line
                    let root = document.RootElement
                    let kind = match root.TryGetProperty "type" with true, v when v.ValueKind = JsonValueKind.String -> v.GetString() | _ -> ""
                    let payload = match root.TryGetProperty "payload" with true, v when v.ValueKind = JsonValueKind.Object -> Some v | _ -> None
                    match kind, payload with
                    | "session_meta", Some value -> runtimeVersion <- match value.TryGetProperty "cli_version" with true, v when v.ValueKind = JsonValueKind.String -> v.GetString() | _ -> ""
                    | "turn_context", Some value ->
                        match text "turn_context" value "turn_id", text "turn_context" value "model" with
                        | Ok turn, Ok model ->
                            let effort = match value.TryGetProperty "effort" with true, v when v.ValueKind = JsonValueKind.String -> v.GetString() | _ -> ""
                            contexts[turn] <- model, effort
                        | _ -> ()
                    | "token_usage_record", Some value ->
                        match value.TryGetProperty "turn_id" with
                        | true, turn when turn.ValueKind = JsonValueKind.String ->
                            let timestamp = match root.TryGetProperty "timestamp" with true, current when current.ValueKind = JsonValueKind.String -> Some(current.GetString()) | _ -> None
                            records.Add(timestamp, value.Clone())
                        | _ -> ()
                    | _ -> ()
                with :? JsonException as error -> raise (FormatException($"line %d{index + 1}: invalid JSON: %s{error.Message}")))
            if String.IsNullOrWhiteSpace runtimeVersion then Error [ "session_meta.cli_version must be a non-empty string" ]
            elif records.Count = 0 then Error [ "no token_usage_record rows found" ]
            else
                let filtered =
                    records
                    |> Seq.filter (fun (timestamp, payload) -> turnId |> Option.forall (fun wanted -> payload.GetProperty("turn_id").GetString() = wanted))
                    |> Seq.filter (fun (timestamp, _) -> sinceUtc |> Option.forall (fun since -> timestamp |> Option.exists (fun current -> String.CompareOrdinal(current, since) >= 0)))
                    |> Seq.filter (fun (timestamp, _) -> untilUtc |> Option.forall (fun until -> timestamp |> Option.exists (fun current -> String.CompareOrdinal(current, until) < 0)))
                    |> List.ofSeq
                if filtered.IsEmpty then Error [ "no token usage records matched the requested turn/time window" ] else
                let selected = if allResponses then filtered else [ List.last filtered ]
                let rows =
                    selected |> List.map (fun (timestampValue, payload) ->
                        let turn = payload.GetProperty("turn_id").GetString()
                        if not (contexts.ContainsKey turn) then Error $"no turn_context model for turn_id %s{turn}" else
                        match TelemetryJson.required "timestamp" (timestampValue |> Option.defaultValue ""), text "payload" payload "session_id", text "payload" payload "thread_id", text "payload" payload "response_id", counts "usage" (payload.GetProperty "usage"), counts "turn_token_usage" (payload.GetProperty "turn_token_usage"), counts "thread_token_usage" (payload.GetProperty "thread_token_usage") with
                        | Ok timestamp, Ok session, Ok thread, Ok response, Ok usage, Ok turnUsage, Ok threadUsage ->
                            let model, effort = contexts[turn]
                            Ok { Timestamp = timestamp; Task = task; SessionId = session; ThreadId = thread; TurnId = turn; ResponseId = response; Provider = "OpenAI"; Model = model; Effort = effort; RuntimeVersion = runtimeVersion; CoordinationVersion = coordinationVersion; SddVersion = sddVersion; ContractsVersion = contractsVersion; LedgerSchema = 1; Response = usage; Turn = turnUsage; Thread = Some threadUsage; Source = "codex-session-jsonl:sha256:" + TelemetryJson.sha256 bytes }
                        | values -> Error(sprintf "%A" values))
                let failures = rows |> List.choose (function Error e -> Some e | _ -> None)
                if not failures.IsEmpty then Error failures else
                let accepted = rows |> List.choose (function Ok row -> Some row | _ -> None)
                let identities = accepted |> List.map (fun row -> row.Provider, row.ResponseId)
                if List.distinct identities <> identities then Error [ "token usage response identity is duplicated" ]
                else Ok { Rows = accepted; SourceDigest = "codex-session-jsonl:sha256:" + TelemetryJson.sha256 bytes }
        with error -> Error [ error.Message ]

    let collectClaude task coordinationVersion sddVersion contractsVersion (bytes: byte array) =
        try
            let errors = ensureVersions task coordinationVersion sddVersion contractsVersion
            if not errors.IsEmpty then Error errors else
            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            let root = document.RootElement
            let modelNode = root.GetProperty "model"
            let usage = root.GetProperty("context_window").GetProperty("current_usage")
            let number (name: string) = let value = usage.GetProperty name in match value.TryGetInt64() with true, n when n >= 0L -> n | _ -> invalidArg name "must be a non-negative integer"
            let uncached, cached, write, output = number "input_tokens", number "cache_read_input_tokens", number "cache_creation_input_tokens", number "output_tokens"
            let input = uncached + cached + write
            let tokenCounts = { Input = input; CachedInput = cached; CacheWriteInput = write; Output = output; Reasoning = None; Total = input + output }
            let get (label: string) (node: JsonElement) (name: string) = match text label node name with Ok value -> value | Error e -> invalidArg name e
            let timestamp, session, turn, model, version = get "snapshot" root "timestamp", get "snapshot" root "session_id", get "snapshot" root "prompt_id", get "model" modelNode "id", get "snapshot" root "version"
            let effort =
                match root.TryGetProperty "effort" with
                | true, e when e.ValueKind = JsonValueKind.Object ->
                    match e.TryGetProperty "level" with
                    | true, level when level.ValueKind = JsonValueKind.String -> level.GetString()
                    | _ -> ""
                | _ -> ""
            let responseKey = JsonObject()
            responseKey["model"] <- JsonValue.Create(model: string)
            responseKey["prompt_id"] <- JsonValue.Create(turn: string)
            responseKey["session_id"] <- JsonValue.Create(session: string)
            responseKey["timestamp"] <- JsonValue.Create(timestamp: string)
            responseKey["usage"] <- JsonNode.Parse(usage.GetRawText())
            let responseId = "claude-" + (TelemetryJson.canonical responseKey |> Encoding.UTF8.GetBytes |> TelemetryJson.sha256)
            let source = "claude-statusline-json:sha256:" + TelemetryJson.sha256 bytes
            Ok { Rows = [ { Timestamp = timestamp; Task = task; SessionId = session; ThreadId = ""; TurnId = turn; ResponseId = responseId; Provider = "Anthropic"; Model = model; Effort = effort; RuntimeVersion = version; CoordinationVersion = coordinationVersion; SddVersion = sddVersion; ContractsVersion = contractsVersion; LedgerSchema = 1; Response = tokenCounts; Turn = tokenCounts; Thread = None; Source = source } ]; SourceDigest = source }
        with error -> Error [ error.Message ]

    let private csvEscape (value: string) = if value.IndexOfAny([| ','; '"'; '\n'; '\r' |]) >= 0 then "\"" + value.Replace("\"", "\"\"") + "\"" else value
    let private optional = Option.map string >> Option.defaultValue ""
    let private countValues (counts: TokenCounts) = [ string counts.Input; string counts.CachedInput; string counts.CacheWriteInput; string counts.Output; optional counts.Reasoning; string counts.Total ]
    let private headers = [ "timestamp"; "task"; "session_id"; "thread_id"; "turn_id"; "response_id"; "provider"; "model"; "effort"; "runtime_version"; "coordination_version"; "sdd_version"; "contracts_version"; "ledger_schema"; "input"; "cached_input"; "cache_write_input"; "output"; "reasoning"; "total"; "turn_input"; "turn_cached_input"; "turn_cache_write_input"; "turn_output"; "turn_reasoning"; "turn_total"; "thread_input"; "thread_cached_input"; "thread_cache_write_input"; "thread_output"; "thread_reasoning"; "thread_total"; "source" ]
    let private values (row: UsageRow) =
        [ row.Timestamp; row.Task; row.SessionId; row.ThreadId; row.TurnId; row.ResponseId; row.Provider; row.Model; row.Effort; row.RuntimeVersion; row.CoordinationVersion; row.SddVersion; row.ContractsVersion; string row.LedgerSchema ] @ countValues row.Response @ countValues row.Turn @ (row.Thread |> Option.map countValues |> Option.defaultValue [ ""; ""; ""; ""; ""; "" ]) @ [ row.Source ]
    let renderCsv (rows: UsageRow list) = String.concat "," headers + "\n" + (rows |> List.map (values >> List.map csvEscape >> String.concat ",") |> String.concat "\n") + (if rows.IsEmpty then "" else "\n")
    let renderJsonLines (rows: UsageRow list) =
        rows |> List.map (fun row ->
            let numeric = Set.ofList [ "ledger_schema"; "input"; "cached_input"; "cache_write_input"; "output"; "reasoning"; "total"; "turn_input"; "turn_cached_input"; "turn_cache_write_input"; "turn_output"; "turn_reasoning"; "turn_total"; "thread_input"; "thread_cached_input"; "thread_cache_write_input"; "thread_output"; "thread_reasoning"; "thread_total" ]
            let result = JsonObject()
            List.zip headers (values row) |> List.iter (fun (name, value) ->
                result[name] <- if numeric.Contains name && value <> "" then JsonValue.Create(Int64.Parse value) else JsonValue.Create(value))
            TelemetryJson.canonical result) |> String.concat "\n" |> fun value -> if rows.IsEmpty then "" else value + "\n"

    let parseJsonLines (jsonLines: string) =
        let parse (lineNumber: int) (line: string) =
            try
                use document = JsonDocument.Parse line
                let root = document.RootElement
                let get (name: string) = match text "usage row" root name with Ok value -> value | Error reason -> invalidArg name reason
                let optionalCount (name: string) =
                    let value = root.GetProperty name
                    if value.ValueKind = JsonValueKind.String && value.GetString() = "" then None
                    elif value.ValueKind = JsonValueKind.Number then Some(value.GetInt64())
                    else invalidArg name "must be a non-negative integer or empty string"
                let count (prefix: string) =
                    let name (value: string) = if prefix = "" then value else prefix + "_" + value
                    let input = optionalCount (name "input") |> Option.defaultWith (fun () -> invalidArg (name "input") "is required")
                    let cached = optionalCount (name "cached_input") |> Option.defaultWith (fun () -> invalidArg (name "cached_input") "is required")
                    let write = optionalCount (name "cache_write_input") |> Option.defaultWith (fun () -> invalidArg (name "cache_write_input") "is required")
                    let output = optionalCount (name "output") |> Option.defaultWith (fun () -> invalidArg (name "output") "is required")
                    let total = optionalCount (name "total") |> Option.defaultWith (fun () -> invalidArg (name "total") "is required")
                    let reasoning = optionalCount (name "reasoning")
                    if List.exists (fun value -> value < 0L) [ input; cached; write; output; total ] then invalidArg prefix "counts must be non-negative"
                    if total <> input + output then invalidArg prefix "total must equal input + output"
                    if cached + write > input then invalidArg prefix "cache subsets exceed input"
                    if reasoning |> Option.exists (fun value -> value < 0L || value > output) then invalidArg prefix "reasoning is invalid"
                    { Input = input; CachedInput = cached; CacheWriteInput = write; Output = output; Reasoning = reasoning; Total = total }
                let thread =
                    match root.GetProperty("thread_input").ValueKind with
                    | JsonValueKind.String when root.GetProperty("thread_input").GetString() = "" -> None
                    | _ -> Some(count "thread")
                let source = get "source"
                if not (Regex.IsMatch(source, "^(codex-session-jsonl|claude-statusline-json):sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)) then invalidArg "source" "must be normalized content-digest provenance"
                Ok
                    { Timestamp = get "timestamp"; Task = get "task"; SessionId = get "session_id"; ThreadId = root.GetProperty("thread_id").GetString()
                      TurnId = get "turn_id"; ResponseId = get "response_id"; Provider = get "provider"; Model = get "model"
                      Effort = root.GetProperty("effort").GetString(); RuntimeVersion = get "runtime_version"; CoordinationVersion = get "coordination_version"
                      SddVersion = get "sdd_version"; ContractsVersion = get "contracts_version"; LedgerSchema = root.GetProperty("ledger_schema").GetInt32()
                      Response = count ""; Turn = count "turn"; Thread = thread; Source = source }
            with error -> Error $"line %d{lineNumber}: %s{error.Message}"
        let parsed = jsonLines.Split('\n') |> Array.filter (String.IsNullOrWhiteSpace >> not) |> Array.mapi (fun index line -> parse (index + 1) line) |> List.ofArray
        let errors = parsed |> List.choose (function Error reason -> Some reason | _ -> None)
        if not errors.IsEmpty then Error errors else
        let rows = parsed |> List.choose (function Ok value -> Some value | _ -> None)
        let identities = rows |> List.map (fun row -> row.Provider, row.ResponseId)
        if List.distinct identities <> identities then Error [ "usage report response identity is duplicated" ] else Ok rows

    let private csvRecords (text: string) =
        let records = ResizeArray<string list>()
        let fields = ResizeArray<string>()
        let field = StringBuilder()
        let mutable quoted = false
        let mutable index = 0
        let finishField () = fields.Add(field.ToString()); field.Clear() |> ignore
        let finishRecord () = finishField (); records.Add(List.ofSeq fields); fields.Clear()
        while index < text.Length do
            let current = text[index]
            if quoted then
                if current = '"' && index + 1 < text.Length && text[index + 1] = '"' then field.Append('"') |> ignore; index <- index + 1
                elif current = '"' then quoted <- false
                else field.Append current |> ignore
            else
                match current with
                | '"' when field.Length = 0 -> quoted <- true
                | ',' -> finishField ()
                | '\n' -> finishRecord ()
                | '\r' -> ()
                | value -> field.Append value |> ignore
            index <- index + 1
        if quoted then Error [ "usage report has an unterminated quoted CSV field" ]
        else
            if field.Length > 0 || fields.Count > 0 then finishRecord ()
            Ok(List.ofSeq records)

    let parseCsvReceipt (bytes: byte array) =
        match csvRecords (Encoding.UTF8.GetString bytes) with
        | Error errors -> Error errors
        | Ok [] -> Error [ "usage report is empty" ]
        | Ok(header :: records) when header <> headers -> Error [ "usage report header does not match the stable collector schema" ]
        | Ok(header :: records) ->
            let numeric = Set.ofList [ "ledger_schema"; "input"; "cached_input"; "cache_write_input"; "output"; "reasoning"; "total"; "turn_input"; "turn_cached_input"; "turn_cache_write_input"; "turn_output"; "turn_reasoning"; "turn_total"; "thread_input"; "thread_cached_input"; "thread_cache_write_input"; "thread_output"; "thread_reasoning"; "thread_total" ]
            let json =
                records |> List.mapi (fun index values ->
                    if values.Length <> header.Length then Error $"usage report line %d{index + 2} has %d{values.Length} fields; expected %d{header.Length}" else
                    let item = JsonObject()
                    let mutable error = None
                    List.zip header values |> List.iter (fun (name, value) ->
                        if numeric.Contains name && value <> "" then
                            match Int64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture) with
                            | true, parsed -> item[name] <- JsonValue.Create parsed
                            | _ -> error <- Some $"usage report line %d{index + 2} field %s{name} is not a non-negative integer"
                        else item[name] <- JsonValue.Create(value))
                    match error with Some reason -> Error reason | None -> Ok(TelemetryJson.canonical item))
            let errors = json |> List.choose (function Error reason -> Some reason | _ -> None)
            if not errors.IsEmpty then Error errors else
            match parseJsonLines (json |> List.choose (function Ok value -> Some value | _ -> None) |> String.concat "\n") with
            | Error errors -> Error errors
            | Ok rows ->
                let identities = rows |> List.map (fun row -> row.Provider, row.ResponseId)
                if List.distinct identities <> identities then Error [ "usage report response identity is duplicated" ]
                else Ok("runtime-usage-csv:sha256:" + TelemetryJson.sha256 bytes, rows)

module UsageReceiptStore =
    type ArchivedReceipt = { Source: string; Path: string }

    let private prefix = "runtime-usage-csv:sha256:"
    let private normalized (path: string) =
        let full = Path.GetFullPath path
        let volumeRoot = Path.GetPathRoot full
        if String.Equals(full, volumeRoot, StringComparison.Ordinal) then full
        else full.TrimEnd(Path.DirectorySeparatorChar)
    let private within parent child =
        let parent = normalized parent + string Path.DirectorySeparatorChar
        (normalized child + string Path.DirectorySeparatorChar).StartsWith(parent, StringComparison.Ordinal)

    let defaultRoot () =
        match Environment.GetEnvironmentVariable "FSGG_USAGE_RECEIPT_STORE" with
        | value when not (String.IsNullOrWhiteSpace value) -> normalized value
        | _ ->
            let state = Environment.GetEnvironmentVariable "XDG_STATE_HOME"
            let basis =
                if not (String.IsNullOrWhiteSpace state) then state
                else Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData
            Path.Combine(basis, "fsgg", "telemetry", "usage") |> normalized

    let private validateRoot root =
        let root = normalized root
        let temporary = normalized (Path.GetTempPath())
        let working = normalized Environment.CurrentDirectory
        let volumeRoot = normalized (Path.GetPathRoot root)
        let rec insideRepository path =
            if Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git")) then true
            else
                match Directory.GetParent path with
                | null -> false
                | parent -> insideRepository parent.FullName
        let rec hasSymbolicLink path =
            let parent = Directory.GetParent path
            let ancestorHasLink = if isNull parent then false else hasSymbolicLink parent.FullName
            ancestorHasLink
            || ((Directory.Exists path || File.Exists path)
                && File.GetAttributes(path).HasFlag FileAttributes.ReparsePoint)
        if root = volumeRoot then Error [ "usage receipt store must not be a filesystem root" ]
        elif hasSymbolicLink root then Error [ "usage receipt store path must not contain a symbolic link" ]
        elif within temporary root || root = temporary then Error [ "usage receipt store must not be inside the system temporary directory" ]
        elif within working root || root = working || insideRepository root then Error [ "usage receipt store must not be inside a repository worktree" ]
        else Ok root

    let private target (root: string) (digest: string) = Path.Combine(root, "sha256", digest.Substring(0, 2), digest + ".csv")
    let private privateDirectory (path: string) =
        Directory.CreateDirectory path |> ignore
        if File.GetAttributes(path).HasFlag FileAttributes.ReparsePoint then
            raise (IOException($"usage receipt store path must not contain a symbolic link: %s{path}"))
        if not (OperatingSystem.IsWindows()) then
            File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
    let private verify digest (path: string) =
        try
            if File.GetAttributes(path).HasFlag FileAttributes.ReparsePoint then
                Error [ "canonical usage receipt must not be a symbolic link" ]
            elif not (OperatingSystem.IsWindows()) && (File.GetUnixFileMode(path) &&& (UnixFileMode.GroupRead ||| UnixFileMode.GroupWrite ||| UnixFileMode.GroupExecute ||| UnixFileMode.OtherRead ||| UnixFileMode.OtherWrite ||| UnixFileMode.OtherExecute)) <> enum 0 then
                Error [ "canonical usage receipt permissions are not owner-only" ]
            else
            let bytes = File.ReadAllBytes path
            if TelemetryJson.sha256 bytes <> digest then Error [ $"canonical usage receipt is corrupted or collides with digest %s{digest}" ]
            else Ok bytes
        with error -> Error [ $"canonical usage receipt cannot be read: %s{error.Message}" ]

    let archive root bytes =
        match RuntimeUsage.parseCsvReceipt bytes with
        | Error errors -> Error errors
        | Ok(source, _) ->
            let digest = source.Substring(prefix.Length)
            match validateRoot (root |> Option.defaultWith defaultRoot) with
            | Error errors -> Error errors
            | Ok store ->
                try
                    privateDirectory store
                    let path = target store digest
                    privateDirectory (Path.GetDirectoryName path)
                    if File.Exists path then
                        verify digest path |> Result.map (fun _ -> { Source = source; Path = path })
                    else
                        let temporary = path + ".new-" + Guid.NewGuid().ToString("n")
                        try
                            do
                                use stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)
                                stream.Write bytes
                                stream.Flush true
                            if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(temporary, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
                            try File.Move(temporary, path, false)
                            with :? IOException when File.Exists path -> ()
                            verify digest path |> Result.map (fun _ -> { Source = source; Path = path })
                        finally
                            if File.Exists temporary then File.Delete temporary
                with error -> Error [ $"usage receipt archive failed: %s{error.Message}" ]

    let private sourceDigest (source: string) =
        if Regex.IsMatch(source, "^runtime-usage-csv:sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant) then
            Ok(source.Substring(prefix.Length))
        else Error [ "usage receipt source must be runtime-usage-csv:sha256:<64-lowercase-hex>" ]

    let tryResolve root source =
        match sourceDigest source, validateRoot (root |> Option.defaultWith defaultRoot) with
        | Error errors, _ | _, Error errors -> Error errors
        | Ok digest, Ok store ->
            let path = target store digest
            if not (File.Exists path) then Ok None else verify digest path |> Result.map Some

    let resolve root source =
        match tryResolve root source with
        | Ok(Some bytes) -> Ok bytes
        | Ok None -> Error [ $"canonical usage receipt is missing: %s{source}" ]
        | Error errors -> Error errors

module LegacyReceiptProof =
    [<Literal>]
    let Schema = "fsgg.telemetry.legacy-receipt-proof/v1"
    type Proof =
        { OriginalEventDigest: string; MissingReceiptSource: string; AuthoritySubject: string
          AuthorityCommentId: int64; LookupEvidence: string list; Author: string; Reviewer: string
          ReviewEvidence: string list; Decision: string; Digest: string }

    let private jsonArray (values: string list) = JsonArray(values |> List.map (fun value -> JsonValue.Create(value) :> JsonNode) |> Array.ofList)
    let private node (proof: Proof) includeDigest =
        let authority = JsonObject()
        authority["subject"] <- JsonValue.Create proof.AuthoritySubject
        authority["comment_id"] <- JsonValue.Create proof.AuthorityCommentId
        let value = JsonObject()
        value["schema"] <- JsonValue.Create Schema
        value["original_event_digest"] <- JsonValue.Create proof.OriginalEventDigest
        value["missing_receipt_source"] <- JsonValue.Create proof.MissingReceiptSource
        value["authority"] <- authority
        value["lookup_evidence"] <- jsonArray proof.LookupEvidence
        value["author"] <- JsonValue.Create proof.Author
        value["reviewer"] <- JsonValue.Create proof.Reviewer
        value["review_evidence"] <- jsonArray proof.ReviewEvidence
        value["decision"] <- JsonValue.Create proof.Decision
        if includeDigest then value["digest"] <- JsonValue.Create proof.Digest
        value

    let canonicalize proof = TelemetryJson.canonical (node proof true) + "\n"
    let parse (bytes: byte array) =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            let root = document.RootElement
            let expected = Set.ofList [ "schema"; "original_event_digest"; "missing_receipt_source"; "authority"; "lookup_evidence"; "author"; "reviewer"; "review_evidence"; "decision"; "digest" ]
            let fields = root.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
            let text (name: string) = root.GetProperty(name).GetString()
            let strings (name: string) = root.GetProperty(name).EnumerateArray() |> Seq.map _.GetString() |> List.ofSeq
            let authority = root.GetProperty "authority"
            let proof =
                { OriginalEventDigest = text "original_event_digest"; MissingReceiptSource = text "missing_receipt_source"
                  AuthoritySubject = authority.GetProperty("subject").GetString(); AuthorityCommentId = authority.GetProperty("comment_id").GetInt64()
                  LookupEvidence = strings "lookup_evidence"; Author = text "author"; Reviewer = text "reviewer"
                  ReviewEvidence = strings "review_evidence"; Decision = text "decision"; Digest = text "digest" }
            let calculated = node proof false |> TelemetryJson.canonical |> Encoding.UTF8.GetBytes |> TelemetryJson.sha256
            let errors =
                [ if root.ValueKind <> JsonValueKind.Object || fields <> expected then yield "legacy receipt proof has missing or unexpected fields"
                  if root.GetProperty("schema").GetString() <> Schema then yield $"legacy receipt proof schema must be %s{Schema}"
                  if not (Regex.IsMatch(proof.OriginalEventDigest, "^[0-9a-f]{64}$")) then yield "original_event_digest must be 64 lowercase hexadecimal characters"
                  if not (Regex.IsMatch(proof.MissingReceiptSource, "^runtime-usage-csv:sha256:[0-9a-f]{64}$")) then yield "missing_receipt_source is invalid"
                  if proof.AuthorityCommentId <= 0L || not (Regex.IsMatch(proof.AuthoritySubject, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+#[1-9][0-9]*$")) then yield "authority must bind a canonical issue subject and positive comment id"
                  if proof.LookupEvidence.IsEmpty || proof.LookupEvidence |> List.exists String.IsNullOrWhiteSpace then yield "lookup_evidence must be non-empty"
                  if not (Regex.IsMatch(proof.Author, "^[a-z][a-z0-9-]*-[0-9a-f]{4}$")) || not (Regex.IsMatch(proof.Reviewer, "^[a-z][a-z0-9-]*-[0-9a-f]{4}$")) || proof.Author = proof.Reviewer then yield "legacy proof author and reviewer must be distinct minted worker identities"
                  if proof.ReviewEvidence.IsEmpty || proof.ReviewEvidence |> List.exists (fun value -> not (Regex.IsMatch(value, "^https://github.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/(issues|pull)/[1-9][0-9]*#issuecomment-[1-9][0-9]*$"))) then yield "review_evidence must contain immutable GitHub review-comment URLs"
                  if proof.Decision <> "irrecoverable-exclude-usage" then yield "legacy proof decision must be irrecoverable-exclude-usage"
                  if proof.Digest <> calculated then yield "legacy receipt proof digest does not bind its canonical content" ]
            if errors.IsEmpty then Ok proof else Error errors
        with error -> Error [ $"invalid legacy receipt proof: %s{error.Message}" ]

module LifecycleTelemetry =
    type Transition = Started | Completed | Blocked | Resumed
    type Finding =
        | InvalidEvent of line: int * reason: string
        | InvalidTransition of phase: string * reason: string
        | EditedAuthorityComment of commentId: int64
        | RejectedFork of winningCommentId: int64 * rejectedCommentId: int64
    type Validation = { EventCount: int; CompletedPhases: string list; ActivePhases: string list; BlockedPhases: string list; ExcludedUsageSources: string list }

    let private digest (value: JsonObject) =
        let clone = value.DeepClone().AsObject()
        clone.Remove "digest" |> ignore
        TelemetryJson.canonical clone |> Encoding.UTF8.GetBytes |> TelemetryJson.sha256
    let private objects (text: string) =
        text.Split('\n') |> Array.filter (String.IsNullOrWhiteSpace >> not) |> Array.mapi (fun i line ->
            try Ok(JsonNode.Parse(line).AsObject()) with error -> Error(InvalidEvent(i + 1, error.Message))) |> List.ofArray
    let private stringAt (name: string) (item: JsonObject) = match item[name] with null -> "" | value -> value.GetValue<string>()
    let private intAt (name: string) (item: JsonObject) = match item[name] with null -> 0 | value -> value.GetValue<int>()
    let private keys (item: JsonObject) = item |> Seq.map _.Key |> Set.ofSeq
    let private eventFields = Set.ofList [ "schema_version"; "run_id"; "unit_id"; "item"; "sequence"; "phase_order"; "phase"; "event"; "at"; "actor"; "model"; "source"; "evidence"; "actual_minutes"; "historical_durations_minutes"; "historical_average_minutes"; "token_usage"; "tooling"; "revision"; "previous_digest"; "digest"; "authority" ]
    let private nonempty name (item: JsonObject) = not (String.IsNullOrWhiteSpace(stringAt name item))
    let private exact expected (item: JsonObject) = keys item = Set.ofList expected
    let private arrayStrings allowEmpty (node: JsonNode) =
        match node with
        | :? JsonArray as values -> (allowEmpty || values.Count > 0) && values |> Seq.forall (fun item -> item <> null && item.GetValueKind() = JsonValueKind.String && not (String.IsNullOrWhiteSpace(item.GetValue<string>())))
        | _ -> false

    let private validateModel line (node: JsonNode) (findings: ResizeArray<Finding>) =
        match node with
        | :? JsonObject as model ->
            let status = stringAt "status" model
            if status = "recorded" then
                if not (exact [ "status"; "provider"; "name"; "source" ] model || exact [ "status"; "provider"; "name"; "effort"; "source" ] model) then findings.Add(InvalidEvent(line, "recorded model has missing or unexpected fields"))
                for name in [ "provider"; "name"; "source" ] do if not (nonempty name model) then findings.Add(InvalidEvent(line, $"model.%s{name} must be non-empty"))
            elif status = "unavailable" then
                if not (exact [ "status"; "reason"; "source" ] model) then findings.Add(InvalidEvent(line, "unavailable model has missing or unexpected fields"))
                for name in [ "reason"; "source" ] do if not (nonempty name model) then findings.Add(InvalidEvent(line, $"model.%s{name} must be non-empty"))
            else findings.Add(InvalidEvent(line, "model status must be recorded or unavailable"))
        | _ -> findings.Add(InvalidEvent(line, "model must be an object with status"))

    let private validateTooling line (node: JsonNode) (findings: ResizeArray<Finding>) =
        match node with
        | :? JsonObject as tooling ->
            if not (exact [ "ledger_schema"; "runtime"; "coordination"; "sdd"; "contracts" ] tooling) || intAt "ledger_schema" tooling <> 1 then findings.Add(InvalidEvent(line, "tooling must contain ledger_schema 1 and all four components"))
            for name in [ "runtime"; "coordination"; "sdd"; "contracts" ] do
                match tooling[name] with
                | :? JsonObject as tool ->
                    let status = stringAt "status" tool
                    if status = "recorded" then
                        if not (exact [ "status"; "name"; "version"; "source" ] tool) then findings.Add(InvalidEvent(line, $"recorded tooling.%s{name} has missing or unexpected fields"))
                        for field in [ "name"; "version"; "source" ] do if not (nonempty field tool) then findings.Add(InvalidEvent(line, $"tooling.%s{name}.%s{field} must be non-empty"))
                    elif status = "unavailable" || status = "not_applicable" then
                        if not (exact [ "status"; "name"; "reason"; "source" ] tool) then findings.Add(InvalidEvent(line, $"%s{status} tooling.%s{name} has missing or unexpected fields"))
                        for field in [ "name"; "reason"; "source" ] do
                            if not (nonempty field tool) then
                                findings.Add(InvalidEvent(line, $"tooling.%s{name}.%s{field} must be non-empty"))
                    else findings.Add(InvalidEvent(line, $"tooling.%s{name}.status is invalid"))
                | _ -> findings.Add(InvalidEvent(line, $"tooling.%s{name} must be a status object"))
        | _ -> findings.Add(InvalidEvent(line, "tooling must be an object"))

    let private validateTokens line terminal (node: JsonNode) (findings: ResizeArray<Finding>) =
        match node with
        | :? JsonObject as usage ->
            let status = stringAt "status" usage
            if not terminal then
                if not (exact [ "status" ] usage) || status <> "pending" then findings.Add(InvalidEvent(line, "started/resumed token_usage must be exactly pending"))
            elif status = "measured" then
                if not (exact [ "status"; "input"; "cached_input"; "cache_write_input"; "output"; "reasoning"; "total"; "source"; "session_ids"; "turn_ids" ] usage) then findings.Add(InvalidEvent(line, "measured token_usage has missing or unexpected fields"))
                else
                    let numbers = [ "input"; "cached_input"; "cache_write_input"; "output"; "total" ] |> List.map (fun name -> name, usage[name])
                    let invalid = numbers |> List.exists (fun (_, value) -> value = null || value.GetValueKind() <> JsonValueKind.Number || value.GetValue<int64>() < 0L)
                    if invalid then findings.Add(InvalidEvent(line, "measured token counts must be non-negative integers")) else
                    let number (name: string) = usage[name].GetValue<int64>()
                    if number "total" <> number "input" + number "output" then findings.Add(InvalidEvent(line, "measured total must equal input + output"))
                    if number "cached_input" + number "cache_write_input" > number "input" then findings.Add(InvalidEvent(line, "measured cache counts exceed input"))
                    match usage["reasoning"] with null -> () | value when value.GetValueKind() = JsonValueKind.Null -> () | value when value.GetValueKind() = JsonValueKind.Number && value.GetValue<int64>() >= 0L && value.GetValue<int64>() <= number "output" -> () | _ -> findings.Add(InvalidEvent(line, "measured reasoning is invalid"))
                    if not (nonempty "source" usage) || not (arrayStrings false usage["session_ids"]) || not (arrayStrings false usage["turn_ids"]) then findings.Add(InvalidEvent(line, "measured usage provenance is incomplete"))
            elif status = "unavailable" then
                if not (exact [ "status"; "reason"; "source" ] usage) || not (nonempty "reason" usage) || not (nonempty "source" usage) then findings.Add(InvalidEvent(line, "unavailable token_usage has missing or invalid fields"))
            elif status = "pending" then findings.Add(InvalidEvent(line, "terminal public events require token reconciliation"))
            else findings.Add(InvalidEvent(line, "terminal token usage status is invalid; estimates are forbidden"))
        | _ -> findings.Add(InvalidEvent(line, "token_usage must be an object with status"))

    let validate (runId: string) (unitId: string) (requireTerminal: bool) (requiredPhases: string list) (jsonLines: string) =
        let parsed = objects jsonLines
        let parseErrors = parsed |> List.choose (function Error e -> Some e | _ -> None)
        if not parseErrors.IsEmpty then Error parseErrors else
        let events = parsed |> List.choose (function Ok v -> Some v | _ -> None)
        if events.IsEmpty then Error [ InvalidEvent(0, "log is empty") ] else
        let mutable previous: string option = None
        let states = Collections.Generic.Dictionary<string, string * DateTime * DateTime * int * string * string>()
        let findings = ResizeArray<Finding>()
        if not (Regex.IsMatch(runId, "^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant)) then findings.Add(InvalidEvent(0, "run id must be lowercase and path-safe"))
        if not (Regex.IsMatch(unitId, "^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)) then findings.Add(InvalidEvent(0, "unit id must be path-safe"))
        let mutable itemIdentity: string option = None
        events |> List.iteri (fun index item ->
            let line = index + 1
            try
                if keys item <> eventFields then findings.Add(InvalidEvent(line, "entry has missing or unexpected fields"))
                if intAt "schema_version" item <> 1 then findings.Add(InvalidEvent(line, "schema_version must be 1"))
                if stringAt "run_id" item <> runId || stringAt "unit_id" item <> unitId then findings.Add(InvalidEvent(line, "run_id or unit_id does not match"))
                if intAt "sequence" item <> line || intAt "revision" item <> line then findings.Add(InvalidEvent(line, "sequence and revision must be contiguous and equal"))
                let predecessor = match item["previous_digest"] with null -> None | value when value.GetValueKind() = JsonValueKind.Null -> None | value -> Some(value.GetValue<string>())
                if predecessor <> previous then findings.Add(InvalidEvent(line, "previous_digest does not extend the canonical chain"))
                let actualDigest = stringAt "digest" item
                if actualDigest <> digest item then findings.Add(InvalidEvent(line, "digest does not bind the canonical event"))
                previous <- Some actualDigest
                let phase, event = stringAt "phase" item, stringAt "event" item
                if not (Regex.IsMatch(phase, "^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant)) then findings.Add(InvalidEvent(line, "phase must be lowercase and path-safe"))
                if not (Set.contains event (Set.ofList [ "started"; "completed"; "blocked"; "resumed" ])) then findings.Add(InvalidEvent(line, "unknown event"))
                if not (nonempty "actor" item) then findings.Add(InvalidEvent(line, "actor must be non-empty"))
                validateModel line item["model"] findings
                validateTooling line item["tooling"] findings
                match item["authority"] with
                | :? JsonObject as authority ->
                    if not (exact [ "kind"; "subject"; "claim_generation" ] authority) || stringAt "kind" authority <> "github_issue_comment" || not (nonempty "subject" authority) || not (nonempty "claim_generation" authority) then findings.Add(InvalidEvent(line, "authority must bind GitHub issue subject and claim generation"))
                | _ -> findings.Add(InvalidEvent(line, "authority must be an object"))
                match item["item"] with
                | :? JsonObject as identity ->
                    let repo, number, url = stringAt "repo" identity, intAt "number" identity, stringAt "url" identity
                    if not (exact [ "repo"; "number"; "url" ] identity) || not (Regex.IsMatch(repo, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) || number <= 0 || url <> $"https://github.com/%s{repo}/issues/%d{number}" then findings.Add(InvalidEvent(line, "item must be a canonical GitHub issue identity"))
                    match item["authority"] with :? JsonObject as authority when stringAt "subject" authority <> $"%s{repo}#%d{number}" -> findings.Add(InvalidEvent(line, "authority.subject must equal canonical item")) | _ -> ()
                    let current = TelemetryJson.canonical identity
                    match itemIdentity with None -> itemIdentity <- Some current | Some original when original <> current -> findings.Add(InvalidEvent(line, "item identity changed within ledger")) | _ -> ()
                | _ -> findings.Add(InvalidEvent(line, "item must be an object"))
                match item["source"] with
                | :? JsonObject as source ->
                    let repository = stringAt "repository" source
                    let revision, unavailable = source.ContainsKey "revision", source.ContainsKey "unavailable_reason"
                    if not (Regex.IsMatch(repository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) || revision = unavailable then findings.Add(InvalidEvent(line, "source must bind repository and exactly one revision state"))
                    elif revision && not (exact [ "repository"; "revision" ] source) then findings.Add(InvalidEvent(line, "source has unexpected fields"))
                    elif unavailable && not (exact [ "repository"; "unavailable_reason" ] source) then findings.Add(InvalidEvent(line, "source has unexpected fields"))
                    elif revision && not (Regex.IsMatch(stringAt "revision" source, "^[0-9a-f]{40}$")) then findings.Add(InvalidEvent(line, "source.revision must be lowercase 40-hex"))
                    elif unavailable && not (nonempty "unavailable_reason" source) then findings.Add(InvalidEvent(line, "source.unavailable_reason must be non-empty"))
                | _ -> findings.Add(InvalidEvent(line, "source must be an object"))
                if not (arrayStrings false item["evidence"]) then findings.Add(InvalidEvent(line, "evidence must be a non-empty string array"))
                let at = DateTime.ParseExact(stringAt "at" item, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal)
                let tokenStatus = match item["token_usage"] with :? JsonObject as usage -> stringAt "status" usage | _ -> ""
                validateTokens line (event = "completed" || event = "blocked") item["token_usage"] findings
                let history = item["historical_durations_minutes"]
                let historyValid = match history with :? JsonArray as values -> values |> Seq.forall (fun value -> value <> null && value.GetValueKind() = JsonValueKind.Number && value.GetValue<int>() >= 0) | _ -> false
                if not historyValid then findings.Add(InvalidEvent(line, "historical durations must be non-negative whole minutes"))
                if event <> "completed" && ((match history with :? JsonArray as values -> values.Count > 0 | _ -> false) || (item["historical_average_minutes"] <> null && item["historical_average_minutes"].GetValueKind() <> JsonValueKind.Null)) then findings.Add(InvalidEvent(line, "only completed events may carry historical averages"))
                if event = "completed" && historyValid then
                    let values = (history :?> JsonArray) |> Seq.map _.GetValue<int>() |> List.ofSeq
                    let expectedAverage = if values.IsEmpty then None else Some((2 * List.sum values + values.Length) / (2 * values.Length))
                    let actualAverage = match item["historical_average_minutes"] with null -> None | value when value.GetValueKind() = JsonValueKind.Null -> None | value -> Some(value.GetValue<int>())
                    if actualAverage <> expectedAverage then findings.Add(InvalidEvent(line, "historical_average_minutes does not match its basis"))
                match event, states.TryGetValue phase with
                | "started", (false, _) ->
                    let expectedOrder = states.Count + 1
                    if intAt "phase_order" item <> expectedOrder then findings.Add(InvalidEvent(line, "phase_order must be contiguous in first-seen order"))
                    if item["actual_minutes"] <> null && item["actual_minutes"].GetValueKind() <> JsonValueKind.Null then findings.Add(InvalidEvent(line, "started actual_minutes must be null"))
                    states[phase] <- "active", at, at, intAt "phase_order" item, TelemetryJson.canonical item["model"], TelemetryJson.canonical item["tooling"]
                | "resumed", (true, ("blocked", started, last, order, model, tooling)) ->
                    if at < last then findings.Add(InvalidEvent(line, "timestamps must be nondecreasing within phase"))
                    if intAt "phase_order" item <> order || TelemetryJson.canonical item["model"] <> model || TelemetryJson.canonical item["tooling"] <> tooling then findings.Add(InvalidEvent(line, "phase order, model, or tooling changed within phase"))
                    if item["actual_minutes"] <> null && item["actual_minutes"].GetValueKind() <> JsonValueKind.Null then findings.Add(InvalidEvent(line, "resumed actual_minutes must be null"))
                    states[phase] <- "active", started, at, order, model, tooling
                | ("completed" | "blocked"), (true, ("active", started, last, order, model, tooling)) ->
                    if at < last then findings.Add(InvalidEvent(line, "timestamps must be nondecreasing within phase"))
                    if intAt "phase_order" item <> order || TelemetryJson.canonical item["model"] <> model || TelemetryJson.canonical item["tooling"] <> tooling then findings.Add(InvalidEvent(line, "phase order, model, or tooling changed within phase"))
                    let expected = int (Math.Floor((at - started).TotalSeconds / 60.0 + 0.5))
                    match item["actual_minutes"] with null -> findings.Add(InvalidEvent(line, "terminal actual_minutes is required")) | value when value.GetValue<int>() <> expected -> findings.Add(InvalidEvent(line, $"actual_minutes must equal rounded elapsed wall time (%d{expected})")) | _ -> ()
                    states[phase] <- event, started, at, order, model, tooling
                | _ -> findings.Add(InvalidTransition(phase, $"event %s{event} is invalid from the current phase state"))
            with error -> findings.Add(InvalidEvent(line, error.Message)))
        for phase in requiredPhases do if not (states.ContainsKey phase) then findings.Add(InvalidTransition(phase, "required phase is missing"))
        let byState value = states |> Seq.choose (fun pair -> let status, _, _, _, _, _ = pair.Value in if status = value then Some pair.Key else None) |> Seq.sort |> List.ofSeq
        let completed, active, blocked = byState "completed", byState "active", byState "blocked"
        if requireTerminal && (not active.IsEmpty || not blocked.IsEmpty || states.Count <> completed.Length) then findings.Add(InvalidEvent(events.Length, "terminal log has active, blocked, or incomplete phases"))
        if findings.Count = 0 then Ok { EventCount = events.Length; CompletedPhases = completed; ActivePhases = active; BlockedPhases = blocked; ExcludedUsageSources = [] } else Error(List.ofSeq findings)

    type HistoryRow = { Phase: string; ToolingFingerprint: string; ActualMinutes: int; Source: string }

    let parseHistoryCsv (text: string) =
        let lines = text.Replace("\r\n", "\n").Split('\n') |> Array.filter (String.IsNullOrWhiteSpace >> not)
        if lines.Length = 0 || lines[0] <> "phase,tooling_fingerprint,actual_minutes,source" then Error [ "history report header must be phase,tooling_fingerprint,actual_minutes,source" ] else
        let parsed =
            lines[1..] |> Array.mapi (fun index line ->
                match line.Split(',') with
                | [| phase; fingerprint; minutes; source |] when Regex.IsMatch(fingerprint, "^[0-9a-f]{64}$") && Regex.IsMatch(source, "^https://github.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/issues/[1-9][0-9]*#issuecomment-[1-9][0-9]*$") ->
                    match Int32.TryParse minutes with true, value when value >= 0 -> Ok { Phase = phase; ToolingFingerprint = fingerprint; ActualMinutes = value; Source = source } | _ -> Error $"history report line %d{index + 2}: actual_minutes must be a whole minute"
                | _ -> Error $"history report line %d{index + 2} is invalid") |> List.ofArray
        let errors = parsed |> List.choose (function Error reason -> Some reason | _ -> None)
        let rows = parsed |> List.choose (function Ok row -> Some row | _ -> None)
        if not errors.IsEmpty then Error errors
        elif rows |> List.map _.Source |> List.distinct |> List.length <> rows.Length then Error [ "history report sources must be unique" ]
        else Ok rows

    let requiredUsageSources jsonLines =
        objects jsonLines
        |> List.choose Result.toOption
        |> List.choose (fun item ->
            match item["token_usage"] with
            | :? JsonObject as usage when stringAt "status" usage = "measured" -> Some(stringAt "source" usage)
            | _ -> None)
        |> List.filter (fun source -> Regex.IsMatch(source, "^runtime-usage-csv:sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant))
        |> List.distinct

    let private validateWithEvidenceInternal (legacyProofs: LegacyReceiptProof.Proof list) runId unitId requireTerminal requiredPhases (usageReports: (string * RuntimeUsage.UsageRow list) list) (history: HistoryRow list) jsonLines =
        match validate runId unitId requireTerminal requiredPhases jsonLines with
        | Error errors -> Error errors
        | Ok validation ->
            let reports = Map.ofList usageReports
            let findings = ResizeArray<Finding>()
            let events = objects jsonLines |> List.choose (function Ok value -> Some value | _ -> None)
            let usedProofDigests = Collections.Generic.HashSet<string>()
            let proofGroups = legacyProofs |> List.groupBy (fun proof -> proof.MissingReceiptSource, proof.OriginalEventDigest)
            proofGroups |> List.iter (fun (_, proofs) -> if proofs.Length <> 1 then findings.Add(InvalidEvent(0, "legacy receipt proof is duplicated")))
            let evidenceValues (item: JsonObject) =
                (item["evidence"] :?> JsonArray) |> Seq.map _.GetValue<string>() |> Set.ofSeq
            let legacyProofFor index (item: JsonObject) source =
                let eventDigest = stringAt "digest" item
                let authority = item["authority"] :?> JsonObject
                legacyProofs
                |> List.tryFind (fun proof ->
                    proof.MissingReceiptSource = source
                    && proof.OriginalEventDigest = eventDigest
                    && proof.AuthoritySubject = stringAt "subject" authority
                    && (events
                        |> List.skip (index + 1)
                        |> List.exists (fun later ->
                            stringAt "phase" later = "legacy-receipt-recovery-" + stringAt "phase" item
                            && evidenceValues later |> Set.contains ("legacy-receipt-proof:sha256:" + proof.Digest))))
            events |> List.iteri (fun index item ->
                let line = index + 1
                let phase = stringAt "phase" item
                let toolingFingerprint = TelemetryJson.canonical item["tooling"] |> Encoding.UTF8.GetBytes |> TelemetryJson.sha256
                let expectedHistory = history |> List.filter (fun row -> row.Phase = phase && row.ToolingFingerprint = toolingFingerprint) |> List.map _.ActualMinutes
                let actualHistory = (item["historical_durations_minutes"] :?> JsonArray) |> Seq.map _.GetValue<int>() |> List.ofSeq
                if stringAt "event" item = "completed" && actualHistory <> expectedHistory then findings.Add(InvalidEvent(line, "historical durations do not equal supplied same-tooling history report"))
                match item["token_usage"] with
                | :? JsonObject as usage when stringAt "status" usage = "measured" ->
                    let source = stringAt "source" usage
                    match reports.TryFind source with
                    | None ->
                        match legacyProofFor index item source with
                        | Some proof -> usedProofDigests.Add proof.Digest |> ignore
                        | None -> findings.Add(InvalidEvent(line, "measured token usage has no matching immutable usage receipt digest or reviewed exclusion proof"))
                    | Some rows ->
                        let ids (name: string) = (usage[name] :?> JsonArray) |> Seq.map _.GetValue<string>() |> Set.ofSeq
                        let sessions, turns = ids "session_ids", ids "turn_ids"
                        let identity = item["item"] :?> JsonObject
                        let repo, number = stringAt "repo" identity, intAt "number" identity
                        let task = $"%s{repo}#%d{number}/%s{phase}"
                        let selected = rows |> List.filter (fun row -> sessions.Contains row.SessionId && turns.Contains row.TurnId && row.Task = task)
                        if selected.IsEmpty then findings.Add(InvalidEvent(line, "measured token usage has no matching usage-report rows")) else
                        let sum (selector: RuntimeUsage.UsageRow -> int64) = selected |> List.sumBy selector
                        let compare (name: string) (observed: int64) = if usage[name].GetValue<int64>() <> observed then findings.Add(InvalidEvent(line, $"measured %s{name} does not equal usage-report sum (%d{observed})"))
                        compare "input" (sum _.Response.Input)
                        compare "cached_input" (sum _.Response.CachedInput)
                        compare "cache_write_input" (sum _.Response.CacheWriteInput)
                        compare "output" (sum _.Response.Output)
                        compare "total" (sum _.Response.Total)
                        let expectedReasoning = if selected |> List.forall (_.Response.Reasoning >> Option.isSome) then Some(selected |> List.sumBy (_.Response.Reasoning >> Option.defaultValue 0L)) else None
                        let actualReasoning = match usage["reasoning"] with null -> None | value when value.GetValueKind() = JsonValueKind.Null -> None | value -> Some(value.GetValue<int64>())
                        if actualReasoning <> expectedReasoning then findings.Add(InvalidEvent(line, "measured reasoning does not equal usage-report sum"))
                        let model = item["model"] :?> JsonObject
                        if stringAt "status" model = "recorded" then
                            selected |> List.iter (fun row -> if row.Model <> stringAt "name" model || row.Provider <> stringAt "provider" model || row.Effort <> stringAt "effort" model then findings.Add(InvalidEvent(line, "model does not match authoritative usage report")))
                        let tooling = item["tooling"] :?> JsonObject
                        for name, observed in
                            [ "runtime", (fun (row: RuntimeUsage.UsageRow) -> row.RuntimeVersion)
                              "coordination", (fun row -> row.CoordinationVersion)
                              "sdd", (fun row -> row.SddVersion)
                              "contracts", (fun row -> row.ContractsVersion) ] do
                            match tooling[name] with
                            | :? JsonObject as tool when stringAt "status" tool = "recorded" -> selected |> List.iter (fun row -> if stringAt "version" tool <> observed row then findings.Add(InvalidEvent(line, $"tooling.%s{name}.version does not match usage report")))
                            | _ -> ()
                | _ -> ())
            legacyProofs
            |> List.iter (fun proof ->
                if not (usedProofDigests.Contains proof.Digest) then
                    findings.Add(InvalidEvent(0, "legacy receipt proof is unconsumed or its canonical receipt is still available")))
            if findings.Count = 0 then
                Ok { validation with ExcludedUsageSources = legacyProofs |> List.map _.MissingReceiptSource |> List.distinct |> List.sort }
            else Error(List.ofSeq findings)

    let validateWithEvidence runId unitId requireTerminal requiredPhases usageReports history jsonLines =
        validateWithEvidenceInternal [] runId unitId requireTerminal requiredPhases usageReports history jsonLines

    let private validateReconciledInternal legacyProofs runId unitId requireTerminal requiredPhases usageReports history jsonLines =
        match validateWithEvidenceInternal legacyProofs runId unitId requireTerminal requiredPhases usageReports history jsonLines with
        | Error errors -> Error errors
        | Ok validation ->
            let events = objects jsonLines |> List.choose Result.toOption
            let findings = ResizeArray<Finding>()
            let supersessionPrefix = "supersedes-lifecycle-digest:"
            let recoveryPrefix = "telemetry-reconciliation-"
            let eventDigest (item: JsonObject) = stringAt "digest" item
            let evidenceValues (item: JsonObject) =
                (item["evidence"] :?> JsonArray)
                |> Seq.map _.GetValue<string>()
                |> List.ofSeq
            let recoveryTargets =
                events
                |> List.indexed
                |> List.choose (fun (index, item) ->
                    let phase = stringAt "phase" item
                    if not (phase.StartsWith(recoveryPrefix, StringComparison.Ordinal)) then None
                    elif stringAt "event" item = "started" || stringAt "event" item = "resumed" then None
                    else
                        let targets =
                            match evidenceValues item with
                            | [ value ] when value.StartsWith(supersessionPrefix, StringComparison.Ordinal) ->
                                [ value.Substring(supersessionPrefix.Length) ]
                            | _ -> []
                        let usage = item["token_usage"] :?> JsonObject
                        if stringAt "event" item <> "completed" then
                            findings.Add(InvalidEvent(index + 1, "telemetry reconciliation must be completed"))
                        if stringAt "status" usage <> "measured" then
                            findings.Add(InvalidEvent(index + 1, "telemetry reconciliation must carry measured token usage"))
                        match targets with
                        | [ target ] when Regex.IsMatch(target, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant) ->
                            Some(target, phase.Substring(recoveryPrefix.Length), index + 1)
                        | _ ->
                            findings.Add(InvalidEvent(index + 1, "telemetry reconciliation must name exactly one supersedes-lifecycle-digest:<64-hex> target"))
                            None)
            for duplicate, entries in recoveryTargets |> List.groupBy (fun (digest, _, _) -> digest) do
                if entries.Length > 1 then
                    findings.Add(InvalidEvent(entries.Head |> fun (_, _, line) -> line, $"lifecycle digest %s{duplicate} is superseded more than once"))
            let byDigest = events |> List.map (fun item -> eventDigest item, item) |> Map.ofList
            for target, phase, line in recoveryTargets do
                match Map.tryFind target byDigest with
                | None -> findings.Add(InvalidEvent(line, "telemetry reconciliation target digest is not present in the lifecycle ledger"))
                | Some targetEvent ->
                    let targetPhase = stringAt "phase" targetEvent
                    let targetUsage = targetEvent["token_usage"] :?> JsonObject
                    if phase <> targetPhase then
                        findings.Add(InvalidEvent(line, $"telemetry reconciliation phase must be telemetry-reconciliation-%s{targetPhase}"))
                    if stringAt "status" targetUsage <> "unavailable" then
                        findings.Add(InvalidEvent(line, "telemetry reconciliation may supersede only an unavailable terminal usage record"))
            let isGenuinePostCompletionFailure (reason: string) =
                [ "post-completion runtime usage lookup failed:"
                  "post-completion collector schema validation failed:" ]
                |> List.exists (fun prefix ->
                    reason.StartsWith(prefix, StringComparison.Ordinal)
                    && not (String.IsNullOrWhiteSpace(reason.Substring(prefix.Length))))
            let requiresRecovery reason = not (isGenuinePostCompletionFailure reason)
            events
            |> List.indexed
            |> List.iter (fun (index, item) ->
                match item["token_usage"] with
                | :? JsonObject as usage when stringAt "status" usage = "unavailable" && requiresRecovery (stringAt "reason" usage) ->
                    let digest = eventDigest item
                    let phase = stringAt "phase" item
                    if recoveryTargets |> List.exists (fun (target, _, _) -> target = digest) |> not then
                        findings.Add(InvalidEvent(index + 1, $"unavailable timing placeholder must be superseded by telemetry-reconciliation-%s{phase} with exact digest %s{digest}"))
                | _ -> ())
            if findings.Count = 0 then Ok validation else Error(List.ofSeq findings)

    let validateReconciledWithEvidence runId unitId requireTerminal requiredPhases usageReports history jsonLines =
        validateReconciledInternal [] runId unitId requireTerminal requiredPhases usageReports history jsonLines

    let validateWithEvidenceAndLegacy runId unitId requireTerminal requireReconciled requiredPhases usageReports legacyProofs history jsonLines =
        if requireReconciled then validateReconciledInternal legacyProofs runId unitId requireTerminal requiredPhases usageReports history jsonLines
        else validateWithEvidenceInternal legacyProofs runId unitId requireTerminal requiredPhases usageReports history jsonLines

    let sealSuccessorWithEvidence (runId: string) (unitId: string) (usageReports: (string * RuntimeUsage.UsageRow list) list) (history: HistoryRow list) (existingJsonLines: string) (draftJson: string) =
        let seal legacyProofs =
            match objects existingJsonLines, objects draftJson with
            | existing, [ Ok draft ] when existing |> List.forall Result.isOk ->
                let suppliedChainFields = [ "sequence"; "revision"; "previous_digest"; "digest" ] |> List.filter draft.ContainsKey
                if not suppliedChainFields.IsEmpty then
                    let fieldNames = String.concat ", " suppliedChainFields
                    Error [ InvalidEvent(1, $"successor draft must omit chain-owned fields: %s{fieldNames}") ]
                else
                    let current = existing |> List.choose (function Ok value -> Some value | _ -> None)
                    let revision = current.Length + 1
                    draft["sequence"] <- JsonValue.Create revision
                    draft["revision"] <- JsonValue.Create revision
                    draft["previous_digest"] <- match current |> List.tryLast with Some item -> JsonValue.Create(stringAt "digest" item) | None -> null
                    draft["digest"] <- JsonValue.Create(String.replicate 64 "0")
                    draft["digest"] <- JsonValue.Create(digest draft)
                    let rendered = TelemetryJson.canonical draft
                    match validateWithEvidenceInternal legacyProofs runId unitId false [] usageReports history (String.concat "\n" ([ yield! current |> List.map TelemetryJson.canonical; rendered ])) with Ok _ -> Ok(rendered + "\n") | Error e -> Error e
            | _, _ -> Error [ InvalidEvent(1, "successor draft must contain exactly one JSON object") ]
        seal []

    let sealSuccessorWithEvidenceAndLegacy (runId: string) (unitId: string) (usageReports: (string * RuntimeUsage.UsageRow list) list) (legacyProofs: LegacyReceiptProof.Proof list) (history: HistoryRow list) (existingJsonLines: string) (draftJson: string) =
        match objects existingJsonLines, objects draftJson with
        | existing, [ Ok draft ] when existing |> List.forall Result.isOk ->
            let suppliedChainFields = [ "sequence"; "revision"; "previous_digest"; "digest" ] |> List.filter draft.ContainsKey
            if not suppliedChainFields.IsEmpty then
                let fieldNames = String.concat ", " suppliedChainFields
                Error [ InvalidEvent(1, $"successor draft must omit chain-owned fields: %s{fieldNames}") ]
            else
                let current = existing |> List.choose (function Ok value -> Some value | _ -> None)
                let revision = current.Length + 1
                draft["sequence"] <- JsonValue.Create revision
                draft["revision"] <- JsonValue.Create revision
                draft["previous_digest"] <- match current |> List.tryLast with Some item -> JsonValue.Create(stringAt "digest" item) | None -> null
                draft["digest"] <- JsonValue.Create(String.replicate 64 "0")
                draft["digest"] <- JsonValue.Create(digest draft)
                let rendered = TelemetryJson.canonical draft
                match validateWithEvidenceInternal legacyProofs runId unitId false [] usageReports history (String.concat "\n" ([ yield! current |> List.map TelemetryJson.canonical; rendered ])) with Ok _ -> Ok(rendered + "\n") | Error e -> Error e
        | _, _ -> Error [ InvalidEvent(1, "successor draft must contain exactly one JSON object") ]

    let sealSuccessor runId unitId existingJsonLines draftJson =
        sealSuccessorWithEvidence runId unitId [] [] existingJsonLines draftJson

    let exportComments (runId: string) (unitId: string) (commentsJson: string) =
        try
            let root: JsonNode = JsonNode.Parse(commentsJson: string)
            let comments =
                match root with
                | :? JsonArray as values -> values |> Seq.collect (function :? JsonArray as nested -> nested :> seq<JsonNode> | item -> Seq.singleton item) |> List.ofSeq
                | _ -> raise (FormatException("GitHub comment export must be an array"))
            let pattern = Regex("\\A<!-- fsgg:item-lifecycle/v1 -->\\n```json\\n([^\\n]+)\\n```\\n?\\z", RegexOptions.CultureInvariant)
            let candidates =
                comments |> List.choose (fun (node: JsonNode) ->
                    let item = node.AsObject()
                    let body = stringAt "body" item
                    if not (body.StartsWith("<!-- fsgg:item-lifecycle/v1 -->", StringComparison.Ordinal)) then None else
                    let id = item["id"].GetValue<int64>()
                    if stringAt "created_at" item <> stringAt "updated_at" item then raise (InvalidOperationException($"edited:%d{id}"))
                    let matched = pattern.Match body
                    if not matched.Success then raise (FormatException($"comment %d{id} has malformed lifecycle body"))
                    let event = JsonNode.Parse(matched.Groups[1].Value).AsObject()
                    if stringAt "run_id" event = runId && stringAt "unit_id" event = unitId then Some(id, event) else None) |> List.sortBy fst
            let canonical = ResizeArray<JsonObject>()
            let canonicalCommentIds = ResizeArray<int64>()
            let rejected = ResizeArray<Finding>()
            let mutable previous: string option = None
            for id, event in candidates do
                if id <= 0L then raise (FormatException("GitHub lifecycle comment has no positive numeric id"))
                let expected = canonical.Count + 1
                let predecessor = match event["previous_digest"] with null -> None | value when value.GetValueKind() = JsonValueKind.Null -> None | value -> Some(value.GetValue<string>())
                if intAt "sequence" event = expected && intAt "revision" event = expected && predecessor = previous then
                    canonical.Add event
                    canonicalCommentIds.Add id
                    previous <- Some(stringAt "digest" event)
                elif intAt "sequence" event >= 1 && intAt "sequence" event < expected && intAt "sequence" event = intAt "revision" event then
                    let revision = intAt "revision" event
                    let claimedPredecessor = if revision = 1 then None else Some(stringAt "digest" canonical[revision - 2])
                    let claimedDigest = stringAt "digest" event
                    if predecessor <> claimedPredecessor || not (Regex.IsMatch(claimedDigest, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant)) || digest event <> claimedDigest then
                        raise (FormatException($"comment %d{id} is not a digest-valid sibling of canonical revision %d{revision}"))
                    let claimedHistory = [ yield! canonical |> Seq.take (revision - 1) |> Seq.map TelemetryJson.canonical; TelemetryJson.canonical event ] |> String.concat "\n"
                    match validate runId unitId false [] claimedHistory with
                    | Error errors -> raise (FormatException($"comment %d{id} is not a valid lifecycle sibling: %A{errors}"))
                    | Ok _ -> rejected.Add(RejectedFork(canonicalCommentIds[revision - 1], id))
                else raise (FormatException($"comment %d{id} does not extend canonical revision %d{expected - 1}"))
            if canonical.Count = 0 then raise (FormatException("GitHub comment export contains no matching lifecycle events"))
            let rendered = canonical |> Seq.map TelemetryJson.canonical |> String.concat "\n" |> fun value -> if value = "" then value else value + "\n"
            match validate runId unitId false [] rendered with Ok _ -> Ok(rendered, List.ofSeq rejected) | Error errors -> Error errors
        with
        | :? InvalidOperationException as e when e.Message.StartsWith("edited:") -> Error [ EditedAuthorityComment(Int64.Parse(e.Message.Substring 7)) ]
        | error -> Error [ InvalidEvent(1, error.Message) ]

module TelemetrySummary =
    type Summary =
        { Responses: int; Sessions: int; Turns: int; Input: int64; CachedInput: int64
          CacheWriteInput: int64; FreshInput: int64; Output: int64; Reasoning: int64 option; Total: int64 }
    let summarize (rows: RuntimeUsage.UsageRow list) =
        let sum selector = rows |> List.sumBy selector
        let reasoning =
            if rows |> List.forall (_.Response.Reasoning >> Option.isSome) then
                Some(rows |> List.sumBy (_.Response.Reasoning >> Option.defaultValue 0L))
            else None
        let input = sum _.Response.Input
        let cached = sum _.Response.CachedInput
        let write = sum _.Response.CacheWriteInput
        { Responses = rows.Length
          Sessions = rows |> List.map _.SessionId |> List.distinct |> List.length
          Turns = rows |> List.map _.TurnId |> List.distinct |> List.length
          Input = input
          CachedInput = cached
          CacheWriteInput = write
          FreshInput = input - cached - write
          Output = sum _.Response.Output
          Reasoning = reasoning
          Total = sum _.Response.Total }

module CritiqueReceipt =
    type Receipt = { CycleId: string; ReviewedCommit: string; RepairRounds: int; GameFunctionality: bool; PlayerJourneyPassed: bool; ArtifactDigest: string }
    let validate (expectedCycle: string) (expectedHead: string option) (bytes: byte array) =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            let root = document.RootElement
            let errors = ResizeArray<string>()
            let has name = root.TryGetProperty(name: string) |> fst
            let value name = match root.TryGetProperty(name: string) with true, item -> item | _ -> Unchecked.defaultof<JsonElement>
            let stringValue (item: JsonElement) = if item.ValueKind = JsonValueKind.String then item.GetString() else ""
            let nonempty (item: JsonElement) = not (String.IsNullOrWhiteSpace(stringValue item))
            let strings (item: JsonElement) = item.ValueKind = JsonValueKind.Array && item.EnumerateArray() |> Seq.forall nonempty
            let nonemptyStrings (item: JsonElement) = strings item && item.GetArrayLength() > 0
            let sha (value: string) = Regex.IsMatch(value, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant)
            let schema = if has "schema_version" && (value "schema_version").ValueKind = JsonValueKind.Number then (value "schema_version").GetInt32() else -1
            let cycle = stringValue (value "cycle_id")
            let rounds = if has "repair_rounds" && (value "repair_rounds").ValueKind = JsonValueKind.Number then (value "repair_rounds").GetInt32() else -1
            let confirmation = value "confirmation"
            let confirmationValue name = if confirmation.ValueKind = JsonValueKind.Object then match confirmation.TryGetProperty(name: string) with true, item -> item | _ -> Unchecked.defaultof<JsonElement> else Unchecked.defaultof<JsonElement>
            let head, verdict = stringValue (confirmationValue "reviewed_commit"), stringValue (confirmationValue "verdict")
            let game = has "game_functionality" && (value "game_functionality").ValueKind = JsonValueKind.True
            let notOwnable = has "entry_point_not_test_ownable" && (value "entry_point_not_test_ownable").ValueKind = JsonValueKind.True
            let journeys = value "player_journeys"
            let journey = journeys.ValueKind = JsonValueKind.Array && journeys.EnumerateArray() |> Seq.exists (fun item ->
                item.ValueKind = JsonValueKind.Object
                && stringValue (match item.TryGetProperty "entry_point" with true, v -> v | _ -> Unchecked.defaultof<JsonElement>) = "product-boot"
                && stringValue (match item.TryGetProperty "input_surface" with true, v -> v | _ -> Unchecked.defaultof<JsonElement>) = "player-control-messages"
                && (match item.TryGetProperty "reached" with true, v -> v.ValueKind = JsonValueKind.True | _ -> false))
            if schema <> 3 then errors.Add "schema_version must be 3"
            if cycle <> expectedCycle then errors.Add "cycle_id does not match"
            for name in [ "milestone"; "critic" ] do if not (nonempty (value name)) then errors.Add($"%s{name} must be a non-empty string")
            let initialCommit = stringValue (value "initial_reviewed_commit")
            if not (sha initialCommit) then errors.Add "initial_reviewed_commit must be a lowercase 40-character git SHA"
            let requiredScope = Set.ofList [ "requirements"; "diff"; "tests"; "architecture"; "roadmap-evidence" ]
            let scope = value "scope"
            let actualScope = if scope.ValueKind = JsonValueKind.Array then scope.EnumerateArray() |> Seq.map stringValue |> List.ofSeq else []
            if Set.ofList actualScope <> requiredScope || actualScope.Length <> requiredScope.Count then errors.Add "scope must contain each required review area exactly once"
            let initialVerdict = stringValue (value "initial_verdict")
            if initialVerdict <> "pass" && initialVerdict <> "changes-required" then errors.Add "initial_verdict must be pass or changes-required"
            if not (has "game_functionality") || ((value "game_functionality").ValueKind <> JsonValueKind.True && (value "game_functionality").ValueKind <> JsonValueKind.False) then errors.Add "game_functionality must be a boolean"
            if not (has "entry_point_not_test_ownable") || ((value "entry_point_not_test_ownable").ValueKind <> JsonValueKind.True && (value "entry_point_not_test_ownable").ValueKind <> JsonValueKind.False) then errors.Add "entry_point_not_test_ownable must be a boolean"
            let ownableReason = value "entry_point_not_test_ownable_reason"
            if notOwnable && not (nonempty ownableReason) then errors.Add "entry_point_not_test_ownable_reason must be non-empty when entry point is not test-ownable"
            if not notOwnable && ownableReason.ValueKind <> JsonValueKind.Null then errors.Add "entry_point_not_test_ownable_reason must be null unless entry point is not test-ownable"
            let uncovered = value "uncovered_functionality"
            if uncovered.ValueKind <> JsonValueKind.Array || not (strings uncovered) then errors.Add "uncovered_functionality must be a string array"
            if journeys.ValueKind <> JsonValueKind.Array then errors.Add "player_journeys must be an array"
            else
                journeys.EnumerateArray() |> Seq.iteri (fun index item ->
                    let prefix = $"player_journeys[%d{index}]"
                    if item.ValueKind <> JsonValueKind.Object then errors.Add($"%s{prefix} must be an object") else
                    let field name = match item.TryGetProperty(name: string) with true, v -> v | _ -> Unchecked.defaultof<JsonElement>
                    if not (nonempty (field "functionality")) then errors.Add($"%s{prefix}.functionality must be a non-empty string")
                    if stringValue (field "entry_point") <> "product-boot" then errors.Add($"%s{prefix}.entry_point must be product-boot")
                    if stringValue (field "input_surface") <> "player-control-messages" then errors.Add($"%s{prefix}.input_surface must be player-control-messages")
                    if (field "reached").ValueKind <> JsonValueKind.True && (field "reached").ValueKind <> JsonValueKind.False then errors.Add($"%s{prefix}.reached must be a boolean")
                    if not (nonemptyStrings (field "evidence")) then errors.Add($"%s{prefix}.evidence must be a non-empty string array"))
            if game && not notOwnable && (journeys.ValueKind <> JsonValueKind.Array || journeys.GetArrayLength() = 0) then errors.Add "player_journeys must contain an entry for game functionality"
            if not game && journeys.ValueKind = JsonValueKind.Array && journeys.GetArrayLength() <> 0 then errors.Add "player_journeys must be empty when game_functionality is false"
            if not game && notOwnable then errors.Add "entry_point_not_test_ownable is only meaningful when game_functionality is true"
            if rounds < 0 || rounds > 10 then errors.Add "repair_rounds is outside 0 through 10"
            let reviewed = value "reviewed_commits"
            let reviewedCommits = if reviewed.ValueKind = JsonValueKind.Array then reviewed.EnumerateArray() |> Seq.map stringValue |> List.ofSeq else []
            let validChain = rounds >= 0 && rounds <= 10 && reviewedCommits.Length = rounds + 1 && reviewedCommits |> List.forall sha && List.distinct reviewedCommits = reviewedCommits && reviewedCommits |> List.tryHead = Some initialCommit
            if not validChain then errors.Add "reviewed_commits must be a unique ordered lowercase-SHA chain with one commit per repair round"
            if verdict <> "pass" then errors.Add "confirmation verdict is not pass"
            expectedHead |> Option.iter (fun expected -> if head <> expected then errors.Add "confirmation reviewed_commit does not match")
            if game && not notOwnable && not journey then errors.Add "game critique requires a passing product-entry player journey"
            if validChain && head <> List.last reviewedCommits then errors.Add "confirmation.reviewed_commit must equal latest reviewed commit"
            if not (sha head) then errors.Add "confirmation.reviewed_commit must be a lowercase 40-character git SHA"
            let unresolved = confirmationValue "unresolved_blocker_major"
            if unresolved.ValueKind <> JsonValueKind.Array || unresolved.GetArrayLength() <> 0 then errors.Add "confirmation.unresolved_blocker_major must be empty"
            let findings = value "findings"
            let findingIds = Collections.Generic.HashSet<string>()
            let mutable blockerMajor = false
            let mutable resolved = false
            if findings.ValueKind <> JsonValueKind.Array then errors.Add "findings must be an array"
            else
                findings.EnumerateArray() |> Seq.iteri (fun index item ->
                    let prefix = $"findings[%d{index}]"
                    if item.ValueKind <> JsonValueKind.Object then errors.Add($"%s{prefix} must be an object") else
                    let field name = match item.TryGetProperty(name: string) with true, v -> v | _ -> Unchecked.defaultof<JsonElement>
                    let id, severity, disposition = stringValue (field "id"), stringValue (field "severity"), stringValue (field "disposition")
                    if String.IsNullOrWhiteSpace id then errors.Add($"%s{prefix}.id must be non-empty") elif not (findingIds.Add id) then errors.Add($"%s{prefix}.id must be unique")
                    if not (Set.contains severity (Set.ofList [ "blocker"; "major"; "minor" ])) then errors.Add($"%s{prefix}.severity is invalid")
                    if severity = "blocker" || severity = "major" then blockerMajor <- true
                    if not (nonempty (field "summary")) then errors.Add($"%s{prefix}.summary must be non-empty")
                    if not (nonemptyStrings (field "evidence")) then errors.Add($"%s{prefix}.evidence must be non-empty")
                    if not (Set.contains disposition (Set.ofList [ "resolved"; "follow-up"; "unresolved" ])) then errors.Add($"%s{prefix}.disposition is invalid")
                    if (severity = "blocker" || severity = "major") && disposition <> "resolved" then errors.Add($"%s{prefix} blocker/major finding must be resolved")
                    if disposition = "resolved" then resolved <- true
                    if not (nonemptyStrings (field "resolution_evidence")) then errors.Add($"%s{prefix}.resolution_evidence must be non-empty"))
            if blockerMajor && initialVerdict <> "changes-required" then errors.Add "initial_verdict must be changes-required when blocker/major findings exist"
            if rounds > 0 && initialVerdict <> "changes-required" then errors.Add "initial_verdict must be changes-required when repair_rounds is non-zero"
            if initialVerdict = "changes-required" && not resolved then errors.Add "changes-required must have at least one resolved finding"
            if rounds = 0 && resolved then errors.Add "repair_rounds cannot be 0 when a finding was resolved"
            let escalation = value "human_escalation"
            if escalation.ValueKind <> JsonValueKind.Null && escalation.ValueKind <> JsonValueKind.Undefined then errors.Add "human escalation is terminal and cannot satisfy milestone acceptance"
            if errors.Count = 0 then Ok { CycleId = cycle; ReviewedCommit = head; RepairRounds = rounds; GameFunctionality = game; PlayerJourneyPassed = journey; ArtifactDigest = "sha256:" + TelemetryJson.sha256 bytes } else Error(List.ofSeq errors)
        with error -> Error [ "critique artifact is invalid: " + error.Message ]

module FeedbackReceipt =
    type Receipt = { CycleId: string; Phases: string list; MaterialEvents: int; ReportDigest: string }
    let validate (expectedCycle: string) (expectedPhases: string list) (reportPath: string) (reportBytes: byte array) (auditBytes: byte array) (checkpointJsonLines: string option) =
        try
            let text = Encoding.UTF8.GetString(reportBytes).Replace("\r\n", "\n").Replace("\r", "\n")
            let front = Regex.Match(text, "\\A---\\n(.*?)\\n---", RegexOptions.Singleline)
            if not front.Success then invalidArg "report" "frontmatter is missing"
            let meta = front.Groups[1].Value.Split('\n') |> Array.choose (fun line -> let at = line.IndexOf ':' in if at > 0 then Some(line[..at-1].Trim(), line[at+1..].Trim()) else None) |> Map.ofArray
            if meta.TryFind "feedbackSchema" <> Some "2" then invalidArg "feedbackSchema" "must be 2"
            if meta.TryFind "cycle" <> Some expectedCycle then invalidArg "cycle" "does not match"
            let section = Regex.Match(text, "(?ms)^## §1 Provenance and confidence\\s*$\\n(.*?)(?=^## §2\\s)")
            if not section.Success then invalidArg "report" "must contain §1 followed by §2"
            let field (name: string) = let values = Regex.Matches(section.Groups[1].Value, $"(?mi)^-\\s+\\*\\*%s{Regex.Escape name}:\\*\\*\\s+(.+?)\\s*$") in if values.Count <> 1 then invalidArg name "must occur exactly once" else values[0].Groups[1].Value.Trim()
            if not ((field "activation").Equals("active", StringComparison.OrdinalIgnoreCase)) then invalidArg "activation" "must be active"
            let phases = field "phases" |> _.Split(',') |> Array.map _.Trim() |> Array.filter (String.IsNullOrWhiteSpace >> not) |> List.ofArray
            if phases <> expectedPhases then invalidArg "phases" "do not match expected order"
            let events = match Int32.TryParse(field "material events") with true, value when value >= 0 -> value | _ -> invalidArg "material events" "must be a non-negative integer"
            use audit = JsonDocument.Parse(ReadOnlyMemory auditBytes)
            let auditRoot = audit.RootElement
            if auditRoot.GetProperty("auditSchema").GetInt32() <> 1 || auditRoot.GetProperty("report").GetString().Replace('\\', '/') <> reportPath.Replace('\\', '/') || auditRoot.GetProperty("reportSha256").GetString() <> TelemetryJson.sha256 (Encoding.UTF8.GetBytes text) then invalidArg "audit" "does not bind current report"
            let unresolved = auditRoot.GetProperty("findings").EnumerateArray() |> Seq.exists (fun finding -> let status = match finding.TryGetProperty "status" with true, v when v.ValueKind = JsonValueKind.String -> v.GetString() | _ -> "" in status = "incomplete" || status = "unsupported")
            if unresolved then invalidArg "audit" "contains unresolved findings"
            let reason = field "zero-event reason"
            if events = 0 then
                if checkpointJsonLines.IsSome then invalidArg "checkpoint" "must be absent when material events is zero"
                if Set.contains (reason.ToLowerInvariant()) (Set.ofList [ ""; "n/a"; "none"; "none observed." ]) then invalidArg "zero-event reason" "must explain why no material event occurred"
            else
                let checkpoint = checkpointJsonLines |> Option.defaultWith (fun () -> invalidArg "checkpoint" "is required when material events is non-zero")
                let normalized = checkpoint.Replace("\r\n", "\n").Replace("\r", "\n")
                let allLines = normalized.Split('\n')
                let lines = if allLines.Length > 0 && allLines[allLines.Length - 1] = "" then allLines[..allLines.Length - 2] else allLines
                if lines.Length = 0 then invalidArg "checkpoint" "must not be empty"
                lines |> Array.iteri (fun index line ->
                    if String.IsNullOrWhiteSpace line then invalidArg "checkpoint" $"contains an empty line at %d{index + 1}"
                    use row = JsonDocument.Parse line
                    if row.RootElement.ValueKind <> JsonValueKind.Object then invalidArg "checkpoint" $"line %d{index + 1} must be an object"
                    match row.RootElement.TryGetProperty "cycle" with
                    | true, value when value.ValueKind = JsonValueKind.String && value.GetString() = expectedCycle -> ()
                    | _ -> invalidArg "checkpoint" $"line %d{index + 1} does not declare cycle %s{expectedCycle}")
                if events <> lines.Length then invalidArg "checkpoint" "material event count does not match"
                if not (Set.contains (reason.ToLowerInvariant()) (Set.ofList [ "n/a"; "not applicable" ])) then invalidArg "zero-event reason" "must be n/a for material events"
            Ok { CycleId = expectedCycle; Phases = phases; MaterialEvents = events; ReportDigest = "sha256:" + TelemetryJson.sha256 (Encoding.UTF8.GetBytes text) }
        with error -> Error [ "feedback artifact is invalid: " + error.Message ]

module RoadmapClosure =
    type Check = { Name: string; Required: bool; Passed: bool; Owner: string option }
    type Evidence = { UnitId: string; Title: string; RoadmapSourceDigest: string; AcceptedReceiptDigest: string; CandidateHead: string; ImplementationMergeHead: string; AcceptanceMergeHead: string; ReviewHead: string; FeedbackHead: string; CycleId: string; CycleUpdateDigest: string; CritiqueVerdict: string; RepairRounds: int; IssueUrl: string; PullRequestUrl: string; ClaimsRemaining: int; Checks: Check list }
    type ExternalObligation = { Check: string; Owner: string; Reason: string }
    type Closed = { Evidence: Evidence; ExternalObligations: ExternalObligation list }
    type Inputs =
        { UnitId: string; Title: string; RoadmapSourceDigest: string
          AcceptedReceipt: byte array; DeliveryReceipt: byte array; Critique: byte array
          FeedbackReportPath: string; FeedbackReport: byte array; FeedbackAudit: byte array
          FeedbackPhases: string list; FeedbackCheckpoint: string option; FeedbackBinding: byte array
          CycleUpdate: byte array; CheckReceipts: byte array list }

    let private shaPattern = "^[0-9a-f]{64}$"
    let private headPattern = "^[0-9a-f]{40}$"
    let private digest bytes = TelemetryJson.sha256 bytes
    let private nodeAt (name: string) (node: JsonObject) =
        let mutable value: JsonNode = null
        if node.TryGetPropertyValue(name, &value) then value else null
    let private selfReceipt (label: string) (expectedSchema: string) (bytes: byte array) =
        let node = JsonNode.Parse(bytes).AsObject()
        let get (name: string) = match nodeAt name node with null -> "" | value when value.GetValueKind() = JsonValueKind.String -> value.GetValue<string>() | _ -> ""
        if get "schema" <> expectedSchema then Error $"%s{label} schema is not %s{expectedSchema}" else
        let claimed = get "digest"
        let clone = node.DeepClone().AsObject()
        clone.Remove "digest" |> ignore
        let actual = TelemetryJson.canonical clone |> Encoding.UTF8.GetBytes |> digest
        if not (Regex.IsMatch(claimed, shaPattern, RegexOptions.CultureInvariant)) || claimed <> actual then Error $"%s{label} self-digest does not bind its canonical bytes"
        else Ok(node, claimed)
    let private text (label: string) (name: string) (node: JsonObject) =
        match nodeAt name node with
        | null -> Error $"%s{label}.%s{name} is required"
        | value when value.GetValueKind() = JsonValueKind.String && not (String.IsNullOrWhiteSpace(value.GetValue<string>())) -> Ok(value.GetValue<string>())
        | _ -> Error $"%s{label}.%s{name} must be a non-empty string"
    let private integer (label: string) (name: string) (node: JsonObject) =
        match nodeAt name node with
        | null -> Error $"%s{label}.%s{name} is required"
        | value when value.GetValueKind() = JsonValueKind.Number ->
            try Ok(value.GetValue<int>()) with _ -> Error $"%s{label}.%s{name} must be an integer"
        | _ -> Error $"%s{label}.%s{name} must be an integer"
    let private boolean (label: string) (name: string) (node: JsonObject) =
        match nodeAt name node with
        | null -> Error $"%s{label}.%s{name} is required"
        | value when value.GetValueKind() = JsonValueKind.True -> Ok true
        | value when value.GetValueKind() = JsonValueKind.False -> Ok false
        | _ -> Error $"%s{label}.%s{name} must be a boolean"
    let private result (errors: ResizeArray<string>) (label: string) (value: Result<'a, string>) =
        match value with Ok item -> Some item | Error error -> errors.Add($"%s{label}: %s{error}"); None

    let inspect (inputs: Inputs) =
        try
            let errors = ResizeArray<string>()
            if String.IsNullOrWhiteSpace inputs.UnitId then errors.Add "unit id is required"
            if String.IsNullOrWhiteSpace inputs.Title then errors.Add "title is required"
            if not (Regex.IsMatch(inputs.RoadmapSourceDigest, "^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)) then errors.Add "roadmap source digest must be sha256 content identity"

            let accepted = selfReceipt "accepted receipt" "fsgg.coordination.unit-acceptance/1" inputs.AcceptedReceipt |> result errors "accepted receipt"
            let delivery = selfReceipt "delivery receipt" "fsgg.roadmap.delivery/1" inputs.DeliveryReceipt |> result errors "delivery receipt"
            let feedbackBinding = selfReceipt "feedback binding" "fsgg.roadmap.feedback-binding/1" inputs.FeedbackBinding |> result errors "feedback binding"
            let cycleUpdate = selfReceipt "cycle update" "fsgg.roadmap.cycle-update/1" inputs.CycleUpdate |> result errors "cycle update"
            let checkNodes = inputs.CheckReceipts |> List.mapi (fun index bytes -> selfReceipt $"check receipt %d{index + 1}" "fsgg.roadmap.check/1" bytes |> result errors $"check receipt %d{index + 1}") |> List.choose id

            let getText label name node = text label name node |> result errors label |> Option.defaultValue ""
            let getInt label name node = integer label name node |> result errors label |> Option.defaultValue -1
            let getBool label name node = boolean label name node |> result errors label |> Option.defaultValue false
            let acceptedNode, acceptedDigest = accepted |> Option.defaultValue (JsonObject(), "")
            let deliveryNode, _ = delivery |> Option.defaultValue (JsonObject(), "")
            let feedbackNode, _ = feedbackBinding |> Option.defaultValue (JsonObject(), "")
            let cycleNode, cycleDigest = cycleUpdate |> Option.defaultValue (JsonObject(), "")
            let candidate = getText "delivery receipt" "candidateHead" deliveryNode
            let implementationMerge = getText "delivery receipt" "implementationMergeHead" deliveryNode
            let acceptanceMerge = getText "delivery receipt" "acceptanceMergeHead" deliveryNode
            let issueUrl = getText "delivery receipt" "issueUrl" deliveryNode
            let pullRequestUrl = getText "delivery receipt" "pullRequestUrl" deliveryNode
            let claimsRemaining = getInt "delivery receipt" "claimsRemaining" deliveryNode
            let cycleId = getText "cycle update" "cycleId" cycleNode
            let feedbackHead = getText "feedback binding" "head" feedbackNode

            for label, head in [ "candidate", candidate; "implementation merge", implementationMerge; "acceptance merge", acceptanceMerge; "feedback", feedbackHead ] do
                if not (Regex.IsMatch(head, headPattern, RegexOptions.CultureInvariant)) then errors.Add($"%s{label} head must be a lowercase 40-character git SHA")
            if getText "accepted receipt" "unitId" acceptedNode <> inputs.UnitId || getText "accepted receipt" "state" acceptedNode <> "accepted" then errors.Add "accepted receipt does not accept this unit"
            if getText "accepted receipt" "sourceRevision" acceptedNode <> candidate then errors.Add "accepted receipt source revision does not match candidate head"
            let hasCandidateArtifact =
                match nodeAt "artifacts" acceptedNode with
                | :? JsonArray as artifacts ->
                    artifacts |> Seq.exists (fun node ->
                        match node with
                        | :? JsonObject as artifact ->
                            match text "artifact" "name" artifact, text "artifact" "sha256" artifact with
                            | Ok name, Ok hash when Regex.IsMatch(hash, shaPattern, RegexOptions.CultureInvariant) -> name = "implementation-candidate-" + candidate
                            | _ -> false
                        | _ -> false)
                | _ -> false
            if not hasCandidateArtifact then errors.Add "accepted receipt does not bind the implementation candidate artifact"
            if getText "delivery receipt" "unitId" deliveryNode <> inputs.UnitId then errors.Add "delivery receipt unit does not match"
            if claimsRemaining <> 0 then errors.Add "claim census is not zero"
            if not (Regex.IsMatch(issueUrl, "^https://github.com/[^/]+/[^/]+/issues/[1-9][0-9]*$")) then errors.Add "issue URL is not canonical"
            if not (Regex.IsMatch(pullRequestUrl, "^https://github.com/[^/]+/[^/]+/pull/[1-9][0-9]*$")) then errors.Add "pull request URL is not canonical"

            let critique = CritiqueReceipt.validate cycleId (Some candidate) inputs.Critique
            let critiqueReceipt = match critique with Ok value -> Some value | Error values -> errors.AddRange(values |> List.map (fun value -> "critique: " + value)); None
            let feedback = FeedbackReceipt.validate cycleId inputs.FeedbackPhases inputs.FeedbackReportPath inputs.FeedbackReport inputs.FeedbackAudit inputs.FeedbackCheckpoint
            if feedback |> Result.isError then match feedback with Error values -> errors.AddRange(values |> List.map (fun value -> "feedback: " + value)) | _ -> ()
            if getText "feedback binding" "unitId" feedbackNode <> inputs.UnitId || getText "feedback binding" "cycleId" feedbackNode <> cycleId then errors.Add "feedback binding identity does not match unit and cycle"
            if feedbackHead <> acceptanceMerge then errors.Add "feedback head does not match acceptance merge head"
            if getText "feedback binding" "reportSha256" feedbackNode <> digest inputs.FeedbackReport || getText "feedback binding" "auditSha256" feedbackNode <> digest inputs.FeedbackAudit then errors.Add "feedback binding does not bind report and audit bytes"
            if getText "cycle update" "unitId" cycleNode <> inputs.UnitId || getText "cycle update" "head" cycleNode <> acceptanceMerge then errors.Add "cycle update does not bind unit and acceptance merge head"

            let checks =
                checkNodes |> List.map (fun (node, _) ->
                    let check = { Name = getText "check receipt" "name" node; Required = getBool "check receipt" "required" node; Passed = getBool "check receipt" "passed" node; Owner = match nodeAt "owner" node with null -> None | value when value.GetValueKind() = JsonValueKind.Null -> None | value when value.GetValueKind() = JsonValueKind.String && not (String.IsNullOrWhiteSpace(value.GetValue<string>())) -> Some(value.GetValue<string>()) | _ -> errors.Add "check receipt owner must be null or a non-empty string"; None }
                    if getText "check receipt" "unitId" node <> inputs.UnitId || getText "check receipt" "head" node <> acceptanceMerge then errors.Add($"check receipt %s{check.Name} does not bind unit and acceptance merge head")
                    if check.Required && not check.Passed then errors.Add($"required check failed: %s{check.Name}")
                    if not check.Required && not check.Passed && check.Owner.IsNone then errors.Add($"failed non-required check has no separate owner: %s{check.Name}")
                    check)
            if checks.IsEmpty then errors.Add "at least one content-addressed check receipt is required"
            if (checks |> List.map _.Name |> List.distinct |> List.length) <> checks.Length then errors.Add "check receipt names must be unique"

            if errors.Count > 0 then Error(List.ofSeq errors) else
            let review = critiqueReceipt.Value
            let evidence =
                { UnitId = inputs.UnitId; Title = inputs.Title; RoadmapSourceDigest = inputs.RoadmapSourceDigest
                  AcceptedReceiptDigest = "sha256:" + acceptedDigest; CandidateHead = candidate; ImplementationMergeHead = implementationMerge
                  AcceptanceMergeHead = acceptanceMerge; ReviewHead = review.ReviewedCommit; FeedbackHead = feedbackHead
                  CycleId = cycleId; CycleUpdateDigest = "sha256:" + cycleDigest; CritiqueVerdict = "pass"; RepairRounds = review.RepairRounds
                  IssueUrl = issueUrl; PullRequestUrl = pullRequestUrl; ClaimsRemaining = claimsRemaining; Checks = checks }
            let obligations = checks |> List.choose (fun check -> if not check.Required && not check.Passed then check.Owner |> Option.map (fun owner -> { Check = check.Name; Owner = owner; Reason = "non-required failed check" }) else None)
            Ok { Evidence = evidence; ExternalObligations = obligations }
        with error -> Error [ "roadmap closure evidence is invalid: " + error.Message ]

module RoadmapProjection =
    let private startMarker (id: string) = $"<!-- fsgg:roadmap-unit/%s{id} -->"
    let private endMarker (id: string) = $"<!-- /fsgg:roadmap-unit/%s{id} -->"
    let renderBlock (closed: RoadmapClosure.Closed) =
        let e = closed.Evidence
        let requiredPassed = e.Checks |> List.filter _.Required |> List.filter _.Passed |> List.length
        let requiredTotal = e.Checks |> List.filter _.Required |> List.length
        [ startMarker e.UnitId
          $"- [x] **%s{e.UnitId} — %s{e.Title}**"
          $"  - Accepted receipt: `%s{e.AcceptedReceiptDigest}`; candidate `%s{e.CandidateHead}`; implementation merge `%s{e.ImplementationMergeHead}`; acceptance merge `%s{e.AcceptanceMergeHead}`."
          $"  - Gates: %d{requiredPassed}/%d{requiredTotal} required passed; critique pass after %d{e.RepairRounds} repair round(s); cycle `%s{e.CycleId}` update `%s{e.CycleUpdateDigest}`."
          $"  - Evidence: [issue](%s{e.IssueUrl}) · [pull request](%s{e.PullRequestUrl}); claim census 0."
          endMarker e.UnitId ] |> String.concat "\n"
    let private sourceDigest (bytes: byte array) = "sha256:" + TelemetryJson.sha256 bytes
    let private bounds (text: string) (id: string) =
        let first, last = startMarker id, endMarker id
        let starts, ends = Regex.Matches(text, Regex.Escape first), Regex.Matches(text, Regex.Escape last)
        if starts.Count <> 1 || ends.Count <> 1 || starts[0].Index >= ends[0].Index then Error [ "roadmap unit markers are missing, duplicated, or reversed" ] else Ok(starts[0].Index, ends[0].Index + ends[0].Length)
    let render (expectedSourceDigest: string) (roadmapBytes: byte array) (closed: RoadmapClosure.Closed) =
        let text = Encoding.UTF8.GetString roadmapBytes
        if sourceDigest roadmapBytes <> expectedSourceDigest || closed.Evidence.RoadmapSourceDigest <> expectedSourceDigest then Error [ "roadmap source digest is stale" ] else
        match bounds text closed.Evidence.UnitId with
        | Error e -> Error e
        | Ok(first, last) ->
            let existing = text.Substring(first, last - first)
            let accepted = renderBlock closed
            if existing.Contains("- [x]", StringComparison.Ordinal) && existing <> accepted then Error [ "already-checked roadmap unit differs from accepted evidence" ]
            else Ok(text.Substring(0, first) + accepted + text.Substring(last))
    let verify (expectedSourceDigest: string) (sourceRoadmapBytes: byte array) (candidateRoadmapBytes: byte array) (closed: RoadmapClosure.Closed) =
        let sourceText, candidateText = Encoding.UTF8.GetString sourceRoadmapBytes, Encoding.UTF8.GetString candidateRoadmapBytes
        if sourceDigest sourceRoadmapBytes <> expectedSourceDigest || closed.Evidence.RoadmapSourceDigest <> expectedSourceDigest then Error [ "roadmap source digest is stale" ] else
        match bounds sourceText closed.Evidence.UnitId, bounds candidateText closed.Evidence.UnitId with
        | Error e, _ | _, Error e -> Error e
        | Ok(sourceFirst, sourceLast), Ok(candidateFirst, candidateLast) ->
            let sourcePrefix, sourceSuffix = sourceText.Substring(0, sourceFirst), sourceText.Substring(sourceLast)
            let candidatePrefix, candidateSuffix = candidateText.Substring(0, candidateFirst), candidateText.Substring(candidateLast)
            if sourcePrefix <> candidatePrefix || sourceSuffix <> candidateSuffix then Error [ "roadmap content outside the bounded unit was modified" ]
            elif candidateText.Substring(candidateFirst, candidateLast - candidateFirst) <> renderBlock closed then Error [ "roadmap unit block differs from accepted evidence" ]
            else Ok ()
