namespace FS.GG.Telemetry

open System
open System.Text
open System.Text.Json
open FS.GG.Coord

module RemoteContract =
    [<Literal>]
    let ReceiptSchema = "fsgg.telemetry.receipt/1"
    [<Literal>]
    let ErrorSchema = "fsgg.telemetry.error/1"
    type Receipt = { Scope: TelemetryReceipt.Scope; BatchId: string; Digest: string; Status: string; Code: string option }
    type ClientConfig = { Endpoint: Uri; CredentialReference: string }
    let private errorCodes = set [ "invalid-request"; "unsupported-version"; "oversized-batch"; "unauthorized-scope"; "identity-conflict"; "overload"; "receipt-unavailable"; "storage-unavailable" ]
    let validErrorCode code = Set.contains code errorCodes
    let validateClientConfig config =
        if isNull config.Endpoint || not config.Endpoint.IsAbsoluteUri || config.Endpoint.Scheme <> Uri.UriSchemeHttps then Error "endpoint must be an absolute HTTPS URI"
        elif not (String.IsNullOrEmpty config.Endpoint.UserInfo) || not (String.IsNullOrEmpty config.Endpoint.Query) || not (String.IsNullOrEmpty config.Endpoint.Fragment) || (config.Endpoint.AbsolutePath <> "/" && config.Endpoint.AbsolutePath <> "") then Error "endpoint must be an HTTPS origin"
        elif String.IsNullOrWhiteSpace config.CredentialReference || config.CredentialReference.Length > 256 then Error "credential reference is required and bounded"
        elif config.CredentialReference.IndexOfAny([| '/'; '\\'; '\r'; '\n' |]) >= 0 then Error "credential reference is invalid"
        else Ok config
    let private scalar (root: JsonElement) (name:string) =
        match root.TryGetProperty name with true,value when value.ValueKind = JsonValueKind.String -> Some(value.GetString()) | _ -> None
    let parseReceipt bytes =
        try
            let options = JsonDocumentOptions(AllowTrailingCommas=false,CommentHandling=JsonCommentHandling.Disallow,MaxDepth=8)
            use document = JsonDocument.Parse(ReadOnlyMemory<byte>(bytes), options)
            let root = document.RootElement
            let allowed = set [ "schema"; "workspaceId"; "producerId"; "streamId"; "batchId"; "digest"; "status"; "code" ]
            if root.ValueKind <> JsonValueKind.Object || (root.EnumerateObject() |> Seq.exists (fun p -> not (Set.contains p.Name allowed))) then Error "invalid receipt"
            else
                let names = root.EnumerateObject() |> Seq.map _.Name |> Seq.toArray
                if names.Length <> 8 || (names |> Array.distinct).Length <> 8 || allowed <> Set.ofArray names then Error "invalid receipt"
                else
                    match scalar root "schema", scalar root "workspaceId", scalar root "producerId", scalar root "streamId", scalar root "batchId", scalar root "digest", scalar root "status" with
                    | Some schema,Some workspace,Some producer,Some stream,Some batch,Some digest,Some status
                        when schema = ReceiptSchema && TelemetryReceipt.validId workspace && TelemetryReceipt.validId producer && TelemetryReceipt.validId stream && TelemetryReceipt.validId batch && digest.Length = 64 && (status = "durably-received" || status = "applied" || status = "rejected" || status = "expired") ->
                        let codeResult = match root.TryGetProperty "code" with | true,v when v.ValueKind=JsonValueKind.String && v.GetString().Length <= 64 && v.GetString() |> Seq.forall(fun c -> Char.IsAsciiLetterLower c || Char.IsAsciiDigit c || c='-') -> Ok(Some(v.GetString())) | true,v when v.ValueKind=JsonValueKind.Null -> Ok None | _ -> Error "invalid receipt"
                        if digest |> Seq.exists(fun c -> not(Char.IsAsciiHexDigit c) || (c>='A' && c<='F')) then Error "invalid receipt"
                        else codeResult |> Result.map(fun code -> { Scope={Workspace=workspace;Producer=producer;Stream=stream}; BatchId=batch; Digest=digest; Status=status; Code=code })
                    | _ -> Error "invalid receipt"
        with :? JsonException -> Error "invalid receipt"
    let writeError code =
        let stable = if validErrorCode code then code else "storage-unavailable"
        JsonSerializer.SerializeToUtf8Bytes {| schema=ErrorSchema; code=stable |}
    let parseError bytes =
        try
            use document=JsonDocument.Parse(ReadOnlyMemory<byte>(bytes),JsonDocumentOptions(MaxDepth=4))
            let root=document.RootElement
            let names=root.EnumerateObject() |> Seq.map _.Name |> Seq.toArray
            if names.Length<>2 || Set.ofArray names<>set["schema";"code"] then None
            else
                match scalar root "schema",scalar root "code" with Some schema,Some code when schema=ErrorSchema && validErrorCode code -> Some code | _ -> None
        with :? JsonException -> None
