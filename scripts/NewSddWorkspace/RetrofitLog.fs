module NewSddWorkspace.RetrofitLog

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes

let relativePath = ".fsgg/coordination-retrofits.json"
let schemaId = "fsgg.new-sdd-workspace-coordination-retrofits/1"
let legacyProvenanceKey = "retrofits"

let private provenanceRelativePath = ".fsgg/scaffold-provenance.json"
let private writeOptions = JsonSerializerOptions(WriteIndented = true)

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

let private readObject (path: string) : Result<JsonObject, string> =
    try
        match JsonNode.Parse(File.ReadAllText path) with
        | :? JsonObject as o -> Ok o
        | _ -> Error(sprintf "%s is not a JSON object" path)
    with ex ->
        Error(sprintf "%s is not readable JSON: %s" path ex.Message)

let private objects (node: JsonNode) : JsonObject list =
    match node with
    | :? JsonArray as array -> array |> Seq.choose (function :? JsonObject as o -> Some o | _ -> None) |> List.ofSeq
    | _ -> []

let private readLog (path: string) : Result<JsonObject, string> =
    readObject path
    |> Result.bind (fun log ->
        let schemaOk = (try log.["schema"].GetValue<string>() = schemaId with _ -> false)

        match log.["retrofits"] with
        | _ when not schemaOk -> Error(sprintf "%s declares a missing or unsupported schema" path)
        | null
        | :? JsonArray -> Ok log
        | _ -> Error(sprintf "%s has a malformed retrofits member" path))

let private emptyLog () =
    let log = JsonObject()
    log.["schema"] <- JsonValue.Create schemaId
    log.["retrofits"] <- JsonArray()
    log

let private logPath (target: string) = Path.Combine(target, relativePath)

let private migrate (target: string) : Result<int, string> =
    let provenancePath = Path.Combine(target, provenanceRelativePath)

    if not (File.Exists provenancePath) then
        Ok 0
    else
        match readObject provenancePath with
        | Error _ -> Ok 0
        | Ok provenance when not (provenance.ContainsKey legacyProvenanceKey) -> Ok 0
        | Ok provenance ->
            try
                let path = logPath target
                let logResult = if File.Exists path then readLog path else Ok(emptyLog ())

                logResult
                |> Result.map (fun log ->
                    let legacy = objects provenance.[legacyProvenanceKey]
                    let existing = objects log.["retrofits"]
                    let merged = JsonArray()
                    let seen = Collections.Generic.HashSet<string>()

                    for entry in existing @ legacy do
                        if seen.Add(entry.ToJsonString()) then
                            merged.Add(entry.DeepClone())

                    log.["retrofits"] <- merged
                    writeWhole path (log.ToJsonString writeOptions)
                    provenance.Remove legacyProvenanceKey |> ignore
                    writeWhole provenancePath (provenance.ToJsonString writeOptions)
                    List.length legacy)
            with ex ->
                Error ex.Message

let migrateLegacy (target: string) = migrate target

let entries (target: string) : Result<JsonObject list, string> =
    migrate target
    |> Result.bind (fun _ ->
        let path = logPath target

        if File.Exists path then
            readLog path |> Result.map (fun log -> objects log.["retrofits"])
        else
            Ok [])

let append (target: string) (entry: JsonObject) : Result<unit, string> =
    migrate target
    |> Result.bind (fun _ ->
        try
            let path = logPath target
            let logResult = if File.Exists path then readLog path else Ok(emptyLog ())

            logResult
            |> Result.map (fun log ->
                let retrofits =
                    match log.["retrofits"] with
                    | :? JsonArray as a -> a
                    | _ ->
                        let a = JsonArray()
                        log.["retrofits"] <- a
                        a

                retrofits.Add(entry.DeepClone())
                writeWhole path (log.ToJsonString writeOptions))
        with ex ->
            Error ex.Message)
