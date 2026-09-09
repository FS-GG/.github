namespace FS.GG.Coord

open System
open System.Collections.Generic
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

module TelemetryReceipt =
    [<Literal>]
    let Schema = "fsgg.telemetry.envelope/1"
    [<Literal>]
    let MaxEnvelopeBytes = 73728
    type Scope = { Workspace: string; Producer: string; Stream: string }
    type Envelope =
        { Scope: Scope; BatchId: string; Batch: TelemetryStore.Batch
          Canonical: string; Digest: string; Key: string }
    let validId value = not (isNull value) && Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")
    let key producer batch = CanonicalJson.sha256(Encoding.UTF8.GetBytes(producer + "\n" + batch))

    // Reject duplicate members at every depth before the legacy payload parser sees them.
    // Integers have one representation; strings preserve Unicode scalars without NFC rewriting.
    let rec private validateJson (node: JsonElement) =
        match node.ValueKind with
        | JsonValueKind.Object ->
            let names = HashSet<string>(StringComparer.Ordinal)
            for property in node.EnumerateObject() do
                if not (names.Add property.Name) then invalidOp "invalid-request"
                validateJson property.Value
        | JsonValueKind.Array -> for child in node.EnumerateArray() do validateJson child
        | JsonValueKind.Number ->
            match node.TryGetInt64() with
            | true, value when node.GetRawText() = value.ToString(Globalization.CultureInfo.InvariantCulture) -> ()
            | _ -> invalidOp "invalid-request"
        | JsonValueKind.String -> node.GetString() |> ignore
        | _ -> ()

    let parse (bytes: byte array) =
        if bytes.Length > MaxEnvelopeBytes then Error [ "oversized-batch" ] else
        try
            UTF8Encoding(false, true).GetString bytes |> ignore
            use document = JsonDocument.Parse bytes
            let root = document.RootElement
            validateJson root
            let fields = Set [ "schema"; "workspaceId"; "producerId"; "streamId"; "batchId"; "payload" ]
            if root.ValueKind <> JsonValueKind.Object || (root.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq) <> fields then invalidOp "invalid-request"
            let text name = root.GetProperty(name: string).GetString()
            if text "schema" <> Schema then Error [ "unsupported-version" ] else
            let scope = { Workspace = text "workspaceId"; Producer = text "producerId"; Stream = text "streamId" }
            let batchId = text "batchId"
            if [ scope.Workspace; scope.Producer; scope.Stream; batchId ] |> List.exists (validId >> not) then Error [ "invalid-request" ] else
            let rawPayload = Encoding.UTF8.GetBytes(root.GetProperty("payload").GetRawText())
            if rawPayload.Length > TelemetryStore.MaxBatchBytes then invalidOp "invalid-request"
            let payload =
                CanonicalJson.canonicalize rawPayload
                |> Result.map Encoding.UTF8.GetBytes
                |> Result.defaultWith (fun _ -> invalidOp "invalid-request")
            match TelemetryStore.parseBatch payload, CanonicalJson.canonicalize bytes with
            | Ok batch, Ok canonical when Encoding.UTF8.GetByteCount canonical <= MaxEnvelopeBytes ->
                Ok { Scope = scope; BatchId = batchId; Batch = batch; Canonical = canonical
                     Digest = CanonicalJson.sha256(Encoding.UTF8.GetBytes canonical); Key = key scope.Producer batchId }
            | _ -> Error [ "invalid-request" ]
        with
        | :? JsonException | :? InvalidOperationException | :? ArgumentException -> Error [ "invalid-request" ]

    let authorize scope envelope =
        if scope = envelope.Scope then Ok () else Error [ "unauthorized-scope" ]
