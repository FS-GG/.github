namespace FS.GG.Coord

module IntakeTransaction =
    open System
    open System.Collections.Generic
    open System.IO
    open System.Security.Cryptography
    open System.Text
    open System.Text.Json

    [<Literal>]
    let AggregateId = "intake-transactions:fs-gg-production"
    [<Literal>]
    let JournalRef = "refs/heads/fsgg/v2/journal/operation/13"
    [<Literal>]
    let Schema = "fsgg.coord.intake-transaction/v1"
    [<Literal>]
    let MaxEventBytes = 65536

    type Identity = { RepositoryId: int64; RepositoryNodeId: string; DraftId: string }
    type Binding =
        { Identity: Identity
          Owner: string
          Repository: string
          DraftDigest: string
          CompatibleDigests: string list
          RequestSha256: string
          RequestBytes: byte[] }
    type NativeIssue = { RepositoryId: int64; NodeId: string; Number: int; Url: string }
    type Event =
        | Intent of Binding
        | InFlight of Binding
        | Unknown of Binding
        | Bound of Binding * NativeIssue * IntakeReceipt.Receipt
    type Entry =
        { Phase: string
          Binding: Binding
          Issue: NativeIssue option
          Receipt: IntakeReceipt.Receipt option }
    type State = { Entries: Map<string, Entry> }

    let private sha (bytes: byte[]) =
        SHA256.HashData(bytes) |> Convert.ToHexString |> fun s -> s.ToLowerInvariant()

    let private validDigest (value: string) =
        not (isNull value) && value.Length = 64 && value |> Seq.forall (fun c -> Char.IsAsciiHexDigit c && not (Char.IsLetter c) || c >= 'a' && c <= 'f')

    let private validIdentity (identity: Identity) =
        identity.RepositoryId > 0L
        && not (String.IsNullOrWhiteSpace identity.RepositoryNodeId)
        && not (String.IsNullOrWhiteSpace identity.DraftId)
        && Encoding.UTF8.GetByteCount(identity.RepositoryNodeId) <= 256
        && Encoding.UTF8.GetByteCount(identity.DraftId) <= 256

    let key (identity: Identity) =
        if not (validIdentity identity) then Error "invalid immutable intake identity"
        else
            use stream = new MemoryStream()
            use writer = new BinaryWriter(stream, Encoding.UTF8, true)
            let field (value: string) =
                let bytes = Encoding.UTF8.GetBytes value
                writer.Write(bytes.Length)
                writer.Write(bytes)
            field "fsgg.coord.intake-transaction-key/v1"
            field (string identity.RepositoryId)
            field identity.RepositoryNodeId
            field identity.DraftId
            writer.Flush()
            Ok(sha (stream.ToArray()))

    let private validBinding (binding: Binding) =
        validIdentity binding.Identity
        && not (String.IsNullOrWhiteSpace binding.Owner)
        && not (String.IsNullOrWhiteSpace binding.Repository)
        && Encoding.UTF8.GetByteCount(binding.Owner) <= 256
        && Encoding.UTF8.GetByteCount(binding.Repository) <= 256
        && validDigest binding.DraftDigest
        && not (List.isEmpty binding.CompatibleDigests)
        && List.head binding.CompatibleDigests = binding.DraftDigest
        && List.length binding.CompatibleDigests <= 4
        && List.distinct binding.CompatibleDigests = binding.CompatibleDigests
        && List.forall validDigest binding.CompatibleDigests
        && not (isNull binding.RequestBytes)
        && binding.RequestBytes.Length > 0
        && binding.RequestBytes.Length <= 32768
        && validDigest binding.RequestSha256
        && sha binding.RequestBytes = binding.RequestSha256

    let prepareIntent identity (draft: Intake.Draft) (request: byte[]) =
        if not (validIdentity identity) || identity.DraftId <> draft.Id then Error "draft and immutable target identity differ"
        elif draft.Schema <> Intake.Schema then Error "unsupported intake draft schema"
        elif Intake.validate draft |> Result.isError then Error "invalid intake draft"
        elif draft.Owner = "" || draft.Repository = "" then Error "missing draft owner/repository"
        elif isNull request || request.Length = 0 || request.Length > 32768 then Error "request bytes are absent or oversized"
        else
            let copy = Array.copy request
            let digests = IntakeReceipt.compatibleDigests draft |> List.distinct
            let binding =
                { Identity = identity
                  Owner = draft.Owner
                  Repository = draft.Repository
                  DraftDigest = IntakeReceipt.digest draft
                  CompatibleDigests = digests
                  RequestSha256 = sha copy
                  RequestBytes = copy }
            if validBinding binding then Ok(Intent binding) else Error "invalid intake binding"

    let private bindingOf = function
        | Intent b | InFlight b | Unknown b | Bound(b, _, _) -> b

    let private sameBinding (left: Binding) (right: Binding) =
        left.Identity = right.Identity
        && left.Owner = right.Owner
        && left.Repository = right.Repository
        && left.DraftDigest = right.DraftDigest
        && left.CompatibleDigests = right.CompatibleDigests
        && left.RequestSha256 = right.RequestSha256
        && left.RequestBytes = right.RequestBytes

    let private validBound (binding: Binding) (issue: NativeIssue) (receipt: IntakeReceipt.Receipt) =
        issue.RepositoryId = binding.Identity.RepositoryId
        && not (String.IsNullOrWhiteSpace issue.NodeId)
        && not (String.IsNullOrWhiteSpace issue.Url)
        && Encoding.UTF8.GetByteCount(issue.NodeId) <= 256
        && Encoding.UTF8.GetByteCount(issue.Url) <= 2048
        && issue.Number > 0
        && receipt.IssueNumber = issue.Number
        && receipt.DraftId = binding.Identity.DraftId
        && receipt.Owner = binding.Owner
        && receipt.Repository = binding.Repository
        && List.contains receipt.DraftDigest binding.CompatibleDigests

    let find state identity =
        key identity |> Result.map (fun transactionKey -> Map.tryFind transactionKey state.Entries)

    let apply prior event =
        let binding = bindingOf event
        if not (validBinding binding) then Error "invalid or changed intake binding"
        else
            let transactionKey = key binding.Identity |> Result.defaultValue ""
            let entries = prior |> Option.map (fun state -> state.Entries) |> Option.defaultValue Map.empty
            let current = Map.tryFind transactionKey entries
            if current |> Option.exists (fun old -> not (sameBinding old.Binding binding)) then Error "intake binding changed"
            else
                let next =
                    match current, event with
                    | None, Intent _ ->
                        Ok { Phase = "Intent"; Binding = { binding with RequestBytes = Array.copy binding.RequestBytes }; Issue = None; Receipt = None }
                    | Some old, InFlight _ when old.Phase = "Intent" -> Ok { old with Phase = "InFlight" }
                    | Some old, Unknown _ when old.Phase = "InFlight" -> Ok old
                    | Some old, Bound(_, issue, receipt) when old.Phase = "InFlight" && validBound binding issue receipt ->
                        Ok { old with Phase = "Bound"; Issue = Some issue; Receipt = Some receipt }
                    | Some old, Bound(_, issue, receipt) when old.Phase = "Bound" && old.Issue = Some issue && old.Receipt = Some receipt -> Ok old
                    | _ -> Error "wrong intake predecessor or incompatible native binding"
                next |> Result.map (fun entry -> { Entries = Map.add transactionKey entry entries })

    let private phaseOf = function
        | Intent _ -> "Intent"
        | InFlight _ -> "InFlight"
        | Unknown _ -> "Unknown"
        | Bound _ -> "Bound"

    let encodeEvent event =
        let binding = bindingOf event
        if not (validBinding binding) then Error "invalid intake event binding"
        elif (match event with Bound(b, issue, receipt) -> not (validBound b issue receipt) | _ -> false) then
            Error "incompatible native issue or receipt"
        else
            use stream = new MemoryStream()
            use writer = new Utf8JsonWriter(stream)
            writer.WriteStartObject()
            writer.WriteString("schema", Schema)
            writer.WriteString("aggregateId", AggregateId)
            writer.WriteString("journalRef", JournalRef)
            writer.WriteString("phase", phaseOf event)
            writer.WriteString("key", key binding.Identity |> Result.defaultValue "")
            writer.WriteNumber("repositoryId", binding.Identity.RepositoryId)
            writer.WriteString("repositoryNodeId", binding.Identity.RepositoryNodeId)
            writer.WriteString("draftId", binding.Identity.DraftId)
            writer.WriteString("owner", binding.Owner)
            writer.WriteString("repository", binding.Repository)
            writer.WriteString("draftDigest", binding.DraftDigest)
            writer.WriteStartArray("compatibleDigests")
            for digest in binding.CompatibleDigests do writer.WriteStringValue(digest)
            writer.WriteEndArray()
            writer.WriteString("requestSha256", binding.RequestSha256)
            writer.WriteString("requestBytes", Convert.ToBase64String(binding.RequestBytes))
            match event with
            | Bound(_, issue, receipt) ->
                writer.WriteStartObject("issue")
                writer.WriteNumber("repositoryId", issue.RepositoryId)
                writer.WriteString("nodeId", issue.NodeId)
                writer.WriteNumber("number", issue.Number)
                writer.WriteString("url", issue.Url)
                writer.WriteEndObject()
                writer.WriteStartObject("receipt")
                writer.WriteString("draftId", receipt.DraftId)
                writer.WriteString("owner", receipt.Owner)
                writer.WriteString("repository", receipt.Repository)
                writer.WriteNumber("issueNumber", receipt.IssueNumber)
                writer.WriteString("draftDigest", receipt.DraftDigest)
                writer.WriteEndObject()
            | _ -> ()
            writer.WriteEndObject()
            writer.Flush()
            let bytes = stream.ToArray()
            if bytes.Length > MaxEventBytes then Error "intake event exceeds maximum bytes" else Ok bytes

    let private rejectDuplicates (element: JsonElement) =
        let rec walk (element: JsonElement) =
            match element.ValueKind with
            | JsonValueKind.Object ->
                let properties = element.EnumerateObject() |> Seq.toList
                let names = properties |> List.map (fun p -> p.Name)
                names.Length = (names |> List.distinct |> List.length)
                && properties |> List.forall (fun p -> walk p.Value)
            | JsonValueKind.Array -> element.EnumerateArray() |> Seq.forall walk
            | _ -> true
        walk element

    let decodeEvent (bytes: byte[]) =
        if isNull bytes || bytes.Length = 0 || bytes.Length > MaxEventBytes then Error "intake event bytes are absent or oversized"
        else
            try
                use document = JsonDocument.Parse(ReadOnlyMemory bytes)
                let root = document.RootElement
                if root.ValueKind <> JsonValueKind.Object || not (rejectDuplicates root) then Error "duplicate or invalid intake JSON"
                else
                    let s (name: string) = root.GetProperty(name).GetString()
                    if s "schema" <> Schema || s "aggregateId" <> AggregateId || s "journalRef" <> JournalRef then
                        Error "incompatible intake schema, aggregate, or journal ref"
                    else
                        let identity = { RepositoryId = root.GetProperty("repositoryId").GetInt64(); RepositoryNodeId = s "repositoryNodeId"; DraftId = s "draftId" }
                        let binding =
                            { Identity = identity
                              Owner = s "owner"
                              Repository = s "repository"
                              DraftDigest = s "draftDigest"
                              CompatibleDigests = root.GetProperty("compatibleDigests").EnumerateArray() |> Seq.map (fun x -> x.GetString()) |> Seq.toList
                              RequestSha256 = s "requestSha256"
                              RequestBytes = root.GetProperty("requestBytes").GetBytesFromBase64() }
                        let event =
                            match s "phase" with
                            | "Intent" -> Intent binding
                            | "InFlight" -> InFlight binding
                            | "Unknown" -> Unknown binding
                            | "Bound" ->
                                let native = root.GetProperty("issue")
                                let receipt = root.GetProperty("receipt")
                                Bound(binding,
                                      { RepositoryId = native.GetProperty("repositoryId").GetInt64(); NodeId = native.GetProperty("nodeId").GetString(); Number = native.GetProperty("number").GetInt32(); Url = native.GetProperty("url").GetString() },
                                      { DraftId = receipt.GetProperty("draftId").GetString(); Owner = receipt.GetProperty("owner").GetString(); Repository = receipt.GetProperty("repository").GetString(); IssueNumber = receipt.GetProperty("issueNumber").GetInt32(); DraftDigest = receipt.GetProperty("draftDigest").GetString() })
                            | _ -> raise (FormatException "unknown intake phase")
                        match encodeEvent event with
                        | Ok canonical when canonical = bytes && s "key" = (key identity |> Result.defaultValue "") -> Ok event
                        | _ -> Error "noncanonical or invalid intake event"
            with :? JsonException | :? InvalidOperationException | :? FormatException | :? KeyNotFoundException | :? ArgumentException ->
                Error "malformed intake event"
