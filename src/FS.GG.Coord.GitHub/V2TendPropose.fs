namespace FS.GG.Coord.GitHub

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open Errors
open Transport

module V2TendPropose =

    let ledgerSchema = "fsgg.board-tend-proposals-ledger/1"
    let private statusNames = set [ "Backlog"; "Ready"; "In progress"; "In review"; "Blocked"; "Done" ]

    // The observer's placeholder (scripts/board-drift-observer.py `opaque`): HMAC-SHA256 under the UTF-8 key
    // bytes of the raw reference, 12 hex digits.
    let placeholder (key: byte[]) (raw: string) =
        use hmac = new HMACSHA256(key)
        "private:" + (hmac.ComputeHash(Encoding.UTF8.GetBytes raw) |> Convert.ToHexString).Substring(0, 12).ToLowerInvariant()

    type Entry =
        { Id: string
          Rule: string
          Subject: string
          Reason: string
          Suggested: string option }

    let private repoOf (reference: string) =
        match reference.LastIndexOf '#' with
        | -1 -> None
        | index -> Some(reference.Substring(0, index))

    // Everything published passes the visibility gate: a private or unknown-visibility subject is its placeholder,
    // and a Suggested value is published only when it is a Status name.
    let entries (key: byte[]) (isPublic: string -> bool) (proposals: V2Tend.Proposal list) : Entry list =
        proposals
        |> List.filter (fun p -> p.Ref <> "")
        |> List.map (fun p ->
            let subject =
                match repoOf p.Ref with
                | Some repo when isPublic repo -> p.Ref
                | _ -> placeholder key p.Ref
            { Id = "P:" + p.Rule + ":" + subject + ":" + p.Reason
              Rule = p.Rule
              Subject = subject
              Reason = p.Reason
              Suggested = p.Suggested |> Option.filter statusNames.Contains })
        |> List.distinctBy (fun e -> e.Id)

    let private graphql (document: string) (variables: (string * Var) list) (subject: string) : Request =
        { Method = "POST"; Path = "graphql"; Query = []; Body = Query(document, variables); Budget = GraphQl; IfNoneMatch = None; Subject = subject }

    let private str (e: JsonElement) (name: string) =
        match e.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
        | _ -> None

    let private obj (e: JsonElement) (name: string) =
        match e.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.Object -> Some v
        | _ -> None

    // Repository visibility in one request: true only for an explicit `isPrivate: false`.
    let readVisibility (transport: IGitHubTransport) (repos: string list) : Result<Map<string, bool>, string> =
        let repos = repos |> List.distinct
        if repos.IsEmpty then Ok Map.empty
        else
            let parts = repos |> List.map (fun r -> match r.IndexOf '/' with -1 -> r, "" | i -> r.Substring(0, i), r.Substring(i + 1))
            let declared = String.Join(", ", [ for i in 0 .. repos.Length - 1 -> sprintf "$o%d: String!, $n%d: String!" i i ])
            let selected = String.Join(" ", [ for i in 0 .. repos.Length - 1 -> sprintf "r%d: repository(owner: $o%d, name: $n%d) { isPrivate }" i i i ])
            let variables = [ for i, (o, n) in List.indexed parts do yield sprintf "o%d" i, VString o; yield sprintf "n%d" i, VString n ]
            match GraphQl.read transport (graphql (sprintf "query(%s) { %s }" declared selected) variables "tend proposals visibility") (fun data ->
                    Ok [ for i, repo in List.indexed repos ->
                            repo, (match obj data (sprintf "r%d" i) with
                                   | Some r -> (match r.TryGetProperty "isPrivate" with | true, v -> v.ValueKind = JsonValueKind.False | _ -> false)
                                   | None -> false) ]) with
            | Ok pairs -> Ok(Map.ofList pairs)
            | Error error -> Error(Errors.explain error)

    // ---- the ledger ----

    type LedgerEntry = { State: string; Comment: string option; FirstPosted: string option; LastSeen: string }

    type Pending = { Run: string; RecordedAt: DateTimeOffset; Ids: string list }

    type Ledger = { Ids: Map<string, LedgerEntry>; Pending: Pending option }

    let emptyLedger = { Ids = Map.empty; Pending = None }

    let decodeLedger (json: string) : Result<Ledger, string> =
        try
            use document = JsonDocument.Parse json
            let root = document.RootElement
            if str root "schema" <> Some ledgerSchema then Error "the proposals ledger schema differs"
            else
                let ids =
                    [ for p in root.GetProperty("ids").EnumerateObject() ->
                        let v = p.Value
                        p.Name, { State = (str v "state").Value; Comment = str v "comment"; FirstPosted = str v "firstPosted"; LastSeen = defaultArg (str v "lastSeen") "" } ]
                if ids |> List.exists (fun (_, e) -> e.State <> "live" && e.State <> "cleared") then Error "a proposals ledger entry has an unknown state"
                else
                    let pending =
                        match obj root "pending" with
                        | Some p ->
                            Some { Run = (str p "run").Value
                                   RecordedAt = DateTimeOffset.Parse((str p "recordedAt").Value)
                                   Ids = [ for i in p.GetProperty("ids").EnumerateArray() -> i.GetString() ] }
                        | None -> None
                    Ok { Ids = Map.ofList ids; Pending = pending }
        with error -> Error("the proposals ledger is malformed: " + error.Message)

    let encodeLedger (ledger: Ledger) =
        let root = JsonObject()
        root["schema"] <- ledgerSchema
        let ids = JsonObject()
        for KeyValue(id, e) in ledger.Ids do
            let v = JsonObject()
            v["state"] <- e.State
            v["comment"] <- (match e.Comment with Some c -> JsonValue.Create c :> JsonNode | None -> null)
            v["firstPosted"] <- (match e.FirstPosted with Some f -> JsonValue.Create f :> JsonNode | None -> null)
            v["lastSeen"] <- e.LastSeen
            ids[id] <- v
        root["ids"] <- ids
        root["pending"] <-
            (match ledger.Pending with
             | Some p ->
                let v = JsonObject()
                v["run"] <- p.Run
                v["recordedAt"] <- p.RecordedAt.ToString("o")
                v["ids"] <- JsonArray([| for i in p.Ids -> JsonValue.Create i :> JsonNode |])
                v :> JsonNode
             | None -> null)
        root.ToJsonString(JsonSerializerOptions(WriteIndented = true))

    // The ledger after a run that observed `current`: every observed ID is live (a cleared one becomes live again,
    // never reposted), every live ID no longer observed is cleared. Returns the IDs never posted before.
    let reconcile (ledger: Ledger) (current: Entry list) (now: DateTimeOffset) : Ledger * Entry list =
        let seen = current |> List.map (fun e -> e.Id) |> Set.ofList
        let pendingIds = ledger.Pending |> Option.map (fun p -> Set.ofList p.Ids) |> Option.defaultValue Set.empty
        let stamp = now.ToString("o")
        let updated =
            ledger.Ids
            |> Map.map (fun id e ->
                if seen.Contains id then { e with State = "live"; LastSeen = stamp }
                elif e.State = "live" then { e with State = "cleared" }
                else e)
        let fresh = current |> List.filter (fun e -> not (ledger.Ids.ContainsKey e.Id) && not (pendingIds.Contains e.Id))
        { ledger with Ids = updated }, fresh

    let commentBody (run: string) (fresh: Entry list) =
        let cell (text: string) = text.Replace("|", "\\|").Replace("\n", " ")
        String.Join(
            "\n",
            [ yield V2TendWrite.proposalsMarker + run + " -->"
              yield sprintf "`board-v2 tend` proposals for Root (run %s): %d new. Each ID is posted once; a cleared ID that fires again is not reposted (board reconciler design §6)." run fresh.Length
              yield ""
              yield "| ID | Rule | Subject | Reason | Suggested |"
              yield "|---|---|---|---|---|"
              for e in fresh -> sprintf "| `%s` | %s | %s | %s | %s |" (cell e.Id) (cell e.Rule) (cell e.Subject) (cell e.Reason) (defaultArg e.Suggested "") ])

    // ---- one proposals run ----

    let private commentsDocument =
        "query($cursor: String) { repository(owner: \"FS-GG\", name: \".github\") { issue(number: 4985) { comments(first: 100, after: $cursor) { totalCount pageInfo { hasNextPage endCursor } nodes { id body createdAt author { login } } } } } }"

    // Find a pending post by its run marker among Root's comments created after the custody time; never by recency.
    let private findPosted (context: V2TendWrite.Context) (pending: Pending) : Result<string list, string> =
        let marker = V2TendWrite.proposalsMarker + pending.Run + " -->"
        let subject = "tend proposals readback"
        GraphQl.drain subject "comments" { MaxPages = 50; MaxItems = 5000 } (fun cursor ->
            GraphQl.read context.Reads (graphql commentsDocument (match cursor with Some c -> [ "cursor", VString c ] | None -> []) subject) (fun data ->
                match obj data "repository" |> Option.bind (fun r -> obj r "issue") |> Option.bind (fun i -> obj i "comments") with
                | Some page ->
                    GraphQl.page subject "comments" (fun (id, _, _, _) -> id) (fun n ->
                        Ok(defaultArg (str n "id") "", defaultArg (str n "body") "", defaultArg (str n "createdAt") "",
                           obj n "author" |> Option.bind (fun a -> str a "login") |> Option.defaultValue "")) page
                | None -> Error(Malformed(subject, "issue #4985 could not be read"))))
        |> Result.mapError Errors.explain
        |> Result.map (fun comments ->
            comments
            |> List.filter (fun (_, body, created, author) ->
                body.Contains marker && context.Settings.RootLogins.Contains author
                && (match DateTimeOffset.TryParse created with | true, at -> at >= pending.RecordedAt.AddMinutes(-1.0) | _ -> false))
            |> List.map (fun (id, _, _, _) -> id))

    // One proposals run under the writer lock: resolve a pending post by its marker, reconcile the ledger with
    // this plan's proposals, and post one comment with the IDs never posted before. Without a key (16 bytes or
    // more) nothing is posted and the ledger is untouched.
    let run (context: V2TendWrite.Context) (syncDirectory: string -> unit) (ledgerPath: string) (key: byte[] option) (proposals: V2Tend.Proposal list) : V2TendWrite.Outcome list =
        let outcome result detail : V2TendWrite.Outcome = { Subject = "proposals"; Ref = "FS-GG/.github#4985"; Kind = "proposals"; Result = result; Detail = detail }
        match key with
        | None -> [ outcome "refused" "proposals-key-unavailable" ]
        | Some key when key.Length < 16 -> [ outcome "refused" "proposals-key-too-short" ]
        | Some key ->
            let loaded =
                if File.Exists ledgerPath then decodeLedger (File.ReadAllText ledgerPath) else Ok emptyLedger
            match loaded with
            | Error reason -> [ outcome "refused" reason ]
            | Ok ledger ->
                let now = context.Clock.GetUtcNow()
                let save (ledger: Ledger) = V2TendWrite.atomicWrite syncDirectory ledgerPath (encodeLedger ledger)
                // 1. A post from an earlier run whose outcome was uncertain is resolved first, by its marker.
                let ledger, resolved =
                    match ledger.Pending with
                    | None -> ledger, []
                    | Some pending ->
                        match findPosted context pending with
                        | Ok [ comment ] ->
                            let ids = pending.Ids |> List.fold (fun (m: Map<string, LedgerEntry>) id -> m.Add(id, { State = "live"; Comment = Some comment; FirstPosted = Some(pending.RecordedAt.ToString("o")); LastSeen = now.ToString("o") })) ledger.Ids
                            { Ids = ids; Pending = None }, [ outcome "verified" ("pending-post-found:" + string pending.Ids.Length) ]
                        | Ok [] -> ledger, [ outcome "uncertain" "pending-post-not-found" ]
                        | Ok _ -> ledger, [ outcome "uncertain" "pending-post-ambiguous" ]
                        | Error reason -> ledger, [ outcome "uncertain" ("pending-post-unreadable: " + reason) ]
                let repos = proposals |> List.choose (fun p -> repoOf p.Ref)
                match readVisibility context.Reads repos with
                | Error reason ->
                    save ledger
                    resolved @ [ outcome "refused" ("visibility-unreadable: " + reason) ]
                | Ok visibility ->
                    let current = entries key (fun repo -> visibility.TryFind repo = Some true) proposals
                    let ledger, fresh = reconcile ledger current now
                    if fresh.IsEmpty || ledger.Pending.IsSome then
                        save ledger
                        resolved @ [ outcome "noop" (if ledger.Pending.IsSome then "pending-post-unresolved" else "no-new-ids") ]
                    else
                        let fresh = fresh |> List.truncate 200
                        let runId = context.Custody.Run
                        // 2. The run and its IDs are recorded before the post, so an uncertain post is never repeated.
                        let pending = { Run = runId; RecordedAt = now; Ids = fresh |> List.map (fun e -> e.Id) }
                        let withPending = { ledger with Pending = Some pending }
                        save withPending
                        let body = commentBody runId fresh
                        let request : Request =
                            { Method = "POST"; Path = V2TendWrite.proposalsPath; Query = []; Body = Json(JsonSerializer.Serialize({| body = body |}))
                              Budget = Rest; IfNoneMatch = None; Subject = "tend proposals" }
                        context.Pacer.Before()
                        let sent = context.Send request
                        context.Pacer.After()
                        let commentId =
                            match sent with
                            | Ok response when response.Status = 201 ->
                                try
                                    use document = JsonDocument.Parse response.Body
                                    str document.RootElement "node_id"
                                with _ -> None
                            | _ -> None
                        match commentId with
                        | None -> resolved @ [ outcome "uncertain" "post-unconfirmed" ]
                        | Some id ->
                            // 3. Read the comment back by the id GitHub returned.
                            let readback =
                                GraphQl.read context.Reads (graphql "query($comment: ID!) { node(id: $comment) { ... on IssueComment { body } } }" [ "comment", VId id ] "tend proposals readback") (fun data ->
                                    Ok(obj data "node" |> Option.bind (fun n -> str n "body") |> Option.exists (fun b -> b.Contains(V2TendWrite.proposalsMarker + runId + " -->"))))
                            match readback with
                            | Ok true ->
                                let ids = fresh |> List.fold (fun (m: Map<string, LedgerEntry>) e -> m.Add(e.Id, { State = "live"; Comment = Some id; FirstPosted = Some(now.ToString("o")); LastSeen = now.ToString("o") })) ledger.Ids
                                save { Ids = ids; Pending = None }
                                resolved @ [ outcome "written" (sprintf "posted:%d" fresh.Length) ]
                            | _ -> resolved @ [ outcome "uncertain" "post-readback-differs" ]
