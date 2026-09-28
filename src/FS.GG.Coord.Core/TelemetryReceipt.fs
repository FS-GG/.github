namespace FS.GG.Coord

open System
open System.Collections.Generic
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions

module TelemetryReceipt =
    [<Literal>]
    let Schema = "fsgg.telemetry.envelope/1"

    [<Literal>]
    let MaxEnvelopeBytes = 73728

    [<Literal>]
    let MaxAdmissionBytes = 77824

    type Scope =
        {
            Workspace: string
            Producer: string
            Stream: string
        }

    type ProducerRole =
        | Generic
        | NativeCollector

    type Principal =
        {
            Scope: Scope
            Role: ProducerRole
            GrantId: string option
            GrantGeneration: int64 option
        }

    type Envelope =
        {
            Scope: Scope
            BatchId: string
            Batch: TelemetryStore.Batch
            Canonical: string
            Digest: string
            Key: string
        }

    type Admission =
        {
            Envelope: Envelope
            Principal: Principal
            Canonical: string
        }

    let validId value =
        not (isNull value)
        && Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z")

    let key producer batch =
        CanonicalJson.sha256 (Encoding.UTF8.GetBytes(producer + "\n" + batch))

    // Reject duplicate members at every depth before the legacy payload parser sees them.
    // Integers have one representation; strings preserve Unicode scalars without NFC rewriting.
    let rec private validateJson (node: JsonElement) =
        match node.ValueKind with
        | JsonValueKind.Object ->
            let names = HashSet<string>(StringComparer.Ordinal)

            for property in node.EnumerateObject() do
                if not (names.Add property.Name) then
                    invalidOp "invalid-request"

                validateJson property.Value
        | JsonValueKind.Array ->
            for child in node.EnumerateArray() do
                validateJson child
        | JsonValueKind.Number ->
            match node.TryGetInt64() with
            | true, value when node.GetRawText() = value.ToString(Globalization.CultureInfo.InvariantCulture) -> ()
            | _ -> invalidOp "invalid-request"
        | JsonValueKind.String -> node.GetString() |> ignore
        | _ -> ()

    let parse (bytes: byte array) =
        if bytes.Length > MaxEnvelopeBytes then
            Error [ "oversized-batch" ]
        else
            try
                UTF8Encoding(false, true).GetString bytes |> ignore
                use document = JsonDocument.Parse bytes
                let root = document.RootElement
                validateJson root

                let fields =
                    Set [ "schema"; "workspaceId"; "producerId"; "streamId"; "batchId"; "payload" ]

                if
                    root.ValueKind <> JsonValueKind.Object
                    || (root.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq) <> fields
                then
                    invalidOp "invalid-request"

                let text name =
                    let value = root.GetProperty(name: string)

                    if value.ValueKind <> JsonValueKind.String then
                        invalidOp "invalid-request"

                    value.GetString()

                if text "schema" <> Schema then
                    Error [ "unsupported-version" ]
                else
                    let scope =
                        {
                            Workspace = text "workspaceId"
                            Producer = text "producerId"
                            Stream = text "streamId"
                        }

                    let batchId = text "batchId"

                    if
                        [ scope.Workspace; scope.Producer; scope.Stream; batchId ]
                        |> List.exists (validId >> not)
                    then
                        Error [ "invalid-request" ]
                    else
                        let rawPayload = Encoding.UTF8.GetBytes(root.GetProperty("payload").GetRawText())

                        if rawPayload.Length > TelemetryStore.MaxBatchBytes then
                            invalidOp "invalid-request"

                        let payload =
                            CanonicalJson.canonicalize rawPayload
                            |> Result.map Encoding.UTF8.GetBytes
                            |> Result.defaultWith (fun _ -> invalidOp "invalid-request")

                        match TelemetryStore.parseBatch payload, CanonicalJson.canonicalize bytes with
                        | Ok batch, Ok canonical when Encoding.UTF8.GetByteCount canonical <= MaxEnvelopeBytes ->
                            Ok
                                {
                                    Scope = scope
                                    BatchId = batchId
                                    Batch = batch
                                    Canonical = canonical
                                    Digest = CanonicalJson.sha256 (Encoding.UTF8.GetBytes canonical)
                                    Key = key scope.Producer batchId
                                }
                        | _ -> Error [ "invalid-request" ]
            with
            | :? JsonException
            | :? InvalidOperationException
            | :? ArgumentException -> Error [ "invalid-request" ]

    let authorize scope envelope =
        if scope = envelope.Scope then
            Ok()
        else
            Error [ "unauthorized-scope" ]

    let genericPrincipal scope =
        { Scope = scope; Role = Generic; GrantId = None; GrantGeneration = None }

    let validPrincipal principal =
        match principal.Role, principal.GrantId, principal.GrantGeneration with
        | Generic, None, None -> true
        | Generic, Some grant, Some generation -> validId grant && generation > 0L
        | NativeCollector, Some grant, Some generation -> validId grant && generation > 0L
        | _ -> false

    let encodeAdmission principal envelope =
        if not (validPrincipal principal) || principal.Scope <> envelope.Scope then
            Error [ "unauthorized-scope" ]
        else
            let root = JsonObject()
            root["schema"] <- "fsgg.telemetry.receipt-admission/1"
            root["role"] <- (match principal.Role with | Generic -> "generic" | NativeCollector -> "native-collector")
            root["grantId"] <- principal.GrantId |> Option.map JsonValue.Create |> Option.defaultValue null
            root["grantGeneration"] <- principal.GrantGeneration |> Option.map JsonValue.Create |> Option.defaultValue null
            root["envelopeDigest"] <- envelope.Digest
            root["envelope"] <- JsonNode.Parse envelope.Canonical
            CanonicalJson.canonicalize (Encoding.UTF8.GetBytes(root.ToJsonString()))
            |> Result.mapError List.singleton

    let parseAdmission bytes =
        let legacy () =
            parse bytes |> Result.map (fun envelope ->
                { Envelope = envelope; Principal = genericPrincipal envelope.Scope; Canonical = envelope.Canonical })
        let parseBounded () =
            try
                use document = JsonDocument.Parse bytes
                let root = document.RootElement
                match root.TryGetProperty "schema" with
                | true, schema when schema.GetString() = "fsgg.telemetry.receipt-admission/1" ->
                    let names = root.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
                    if names <> set [ "schema"; "role"; "grantId"; "grantGeneration"; "envelopeDigest"; "envelope" ] then
                        Error [ "invalid-request" ]
                    else
                        let envelopeBytes = Encoding.UTF8.GetBytes(root.GetProperty("envelope").GetRawText())
                        match parse envelopeBytes, CanonicalJson.canonicalize bytes with
                        | Ok envelope, Ok canonical when envelope.Digest = root.GetProperty("envelopeDigest").GetString() ->
                            let role = root.GetProperty("role").GetString()
                            let grant = root.GetProperty("grantId")
                            let generation = root.GetProperty("grantGeneration")
                            let principal =
                                if role = "generic" && grant.ValueKind = JsonValueKind.Null && generation.ValueKind = JsonValueKind.Null then
                                    Some(genericPrincipal envelope.Scope)
                                elif (role = "generic" || role = "native-collector") && grant.ValueKind = JsonValueKind.String && generation.ValueKind = JsonValueKind.Number then
                                    Some
                                        { Scope = envelope.Scope; Role = if role = "generic" then Generic else NativeCollector
                                          GrantId = Some(grant.GetString()); GrantGeneration = Some(generation.GetInt64()) }
                                else None
                            match principal with
                            | Some value when validPrincipal value ->
                                Ok { Envelope = envelope; Principal = value; Canonical = canonical }
                            | _ -> Error [ "invalid-request" ]
                        | _ -> Error [ "invalid-request" ]
                | _ -> legacy ()
            with _ -> Error [ "invalid-request" ]
        if bytes.Length > MaxAdmissionBytes then Error [ "oversized-batch" ] else parseBounded ()
