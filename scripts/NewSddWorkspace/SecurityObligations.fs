module NewSddWorkspace.SecurityObligations

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes

let relativePath = ".fsgg/security-obligations.json"
let schemaId = "fsgg.new-sdd-workspace-security-obligations/1"
let legacyProvenanceKeys = [ "securityObligations"; "verifiedSecurityReceipts" ]

let private provenanceRelativePath = ".fsgg/scaffold-provenance.json"
let private writeOptions = JsonSerializerOptions(WriteIndented = true)

let filePath (target: string) = Path.Combine(target, relativePath)

type Migration =
    | NothingToMigrate
    | Migrated of obligations: int * receipts: int
    | Incomplete of reason: string

let emptyState () =
    let state = JsonObject()
    state.["schema"] <- JsonValue.Create schemaId
    state.["securityObligations"] <- JsonArray()
    state.["verifiedSecurityReceipts"] <- JsonArray()
    state

/// Whole-file replacement: a reader sees the old file or the new one, never a prefix.
let private writeWhole (path: string) (text: string) =
    let directory = Path.GetDirectoryName(Path.GetFullPath path)
    Directory.CreateDirectory directory |> ignore
    let temporary = Path.Combine(directory, "." + Path.GetFileName path + "." + Guid.NewGuid().ToString("N") + ".tmp")

    try
        File.WriteAllText(temporary, text)
        File.Move(temporary, path, true)
    finally
        if File.Exists temporary then
            File.Delete temporary

let private rows (state: JsonObject) (key: string) : JsonObject list =
    match state.[key] with
    | :? JsonArray as array -> array |> Seq.choose (function :? JsonObject as row -> Some row | _ -> None) |> List.ofSeq
    | _ -> []

/// Identity of a row for deduplication. Obligations and receipts are keyed by `kind` and the target
/// field they use (`target`, `repository` or `project`); an unkeyed row is kept as is.
let private rowKey (row: JsonObject) =
    let text (name: string) =
        match row.[name] with
        | null -> None
        | node ->
            try
                Some(node.GetValue<string>())
            with _ ->
                None

    match text "kind", (text "target" |> Option.orElse (text "repository") |> Option.orElse (text "project")) with
    | Some kind, Some target -> Some(kind + "\u0000" + target)
    | _ -> None

let private mergeRows (existing: JsonObject list) (legacy: JsonObject list) : JsonArray =
    let merged = JsonArray()
    let present = existing |> List.choose rowKey |> Set.ofList

    for row in existing do
        merged.Add(row.DeepClone())

    for row in legacy do
        match rowKey row with
        | Some key when Set.contains key present -> ()
        | _ -> merged.Add(row.DeepClone())

    merged

let private readObject (path: string) : Result<JsonObject, string> =
    try
        match JsonNode.Parse(File.ReadAllText path) with
        | :? JsonObject as o -> Ok o
        | _ -> Error(sprintf "%s is not a JSON object" path)
    with ex ->
        Error(sprintf "%s is not readable JSON: %s" path ex.Message)

let private readState (path: string) : Result<JsonObject, string> =
    readObject path
    |> Result.bind (fun state ->
        let typed (key: string) =
            match state.[key] with
            | null
            | :? JsonArray -> true
            | _ -> false

        match state.["schema"] with
        | null -> Error(sprintf "%s has no schema" path)
        | node when (try node.GetValue<string>() = schemaId with _ -> false) ->
            if typed "securityObligations" && typed "verifiedSecurityReceipts" then
                Ok state
            else
                Error(sprintf "%s has a malformed securityObligations or verifiedSecurityReceipts member" path)
        | _ -> Error(sprintf "%s declares an unsupported schema" path))

let migrateLegacy (target: string) : Migration =
    let provenancePath = Path.Combine(target, provenanceRelativePath)

    if not (File.Exists provenancePath) then
        NothingToMigrate
    else
        match readObject provenancePath with
        | Error _ -> NothingToMigrate
        | Ok provenance ->
            let present = legacyProvenanceKeys |> List.filter (fun key -> provenance.ContainsKey key)

            if List.isEmpty present then
                NothingToMigrate
            else
                try
                    let path = filePath target

                    let stateResult =
                        if File.Exists path then readState path else Ok(emptyState ())

                    match stateResult with
                    | Error reason -> Incomplete reason
                    | Ok state ->
                        let legacyRows key = rows provenance key
                        let obligations = mergeRows (rows state "securityObligations") (legacyRows "securityObligations")
                        let receipts = mergeRows (rows state "verifiedSecurityReceipts") (legacyRows "verifiedSecurityReceipts")
                        let legacyObligationCount = legacyRows "securityObligations" |> List.length
                        let legacyReceiptCount = legacyRows "verifiedSecurityReceipts" |> List.length
                        state.["securityObligations"] <- obligations
                        state.["verifiedSecurityReceipts"] <- receipts
                        // The Wizard-owned file is durable before provenance changes.
                        writeWhole path (state.ToJsonString writeOptions)

                        for key in present do
                            provenance.Remove key |> ignore

                        writeWhole provenancePath (provenance.ToJsonString writeOptions)
                        Migrated(legacyObligationCount, legacyReceiptCount)
                with ex ->
                    Incomplete ex.Message

let load (target: string) : Result<JsonObject option, string> =
    match migrateLegacy target with
    | Incomplete reason -> Error(sprintf "legacy security state could not be migrated: %s" reason)
    | NothingToMigrate
    | Migrated _ ->
        let path = filePath target

        if File.Exists path then readState path |> Result.map Some else Ok None

let save (target: string) (state: JsonObject) : Result<unit, string> =
    try
        state.["schema"] <- JsonValue.Create schemaId
        writeWhole (filePath target) (state.ToJsonString writeOptions)
        Ok()
    with ex ->
        Error ex.Message
