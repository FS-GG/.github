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

module V2TendWrite =

    // ---- the closed change type (design §5) ----

    type FieldValue =
        | OptionValue of optionId: string
        | TextValue of text: string

    type DeliveryToClose =
        private
            { IssueId: string
              ItemId: string
              Ref: string
              Comment: string }

        member this.Issue = this.IssueId
        member this.Item = this.ItemId
        member this.Reference = this.Ref
        member this.Body = this.Comment

    type TendChange =
        | SetFieldValue of projectId: string * itemId: string * values: (string * FieldValue) list
        | ClearFieldValue of projectId: string * itemId: string * fieldId: string
        | AddItem of projectId: string * contentId: string
        | RemoveCreatedItem of projectId: string * itemId: string
        | ArchiveItem of projectId: string * itemId: string
        | CloseDelivery of DeliveryToClose

    let projectRootFields =
        set [ "updateProjectV2ItemFieldValue"; "clearProjectV2ItemFieldValue"; "addProjectV2ItemById"; "deleteProjectV2Item"; "archiveProjectV2Item" ]

    let issueRootFields = set [ "addComment"; "closeIssue" ]

    let tendClosedMarker = "<!-- fsgg:tend-closed/v1 -->"

    let private graphql (document: string) (variables: (string * Var) list) (subject: string) : Request =
        { Method = "POST"; Path = "graphql"; Query = []; Body = Query(document, variables); Budget = GraphQl; IfNoneMatch = None; Subject = subject }

    let render (mutationId: string) (change: TendChange) : Request list =
        match change with
        | SetFieldValue(project, item, values) ->
            let declared = Text.StringBuilder("$project: ID!, $item: ID!")
            let fields = Text.StringBuilder()
            let variables = Collections.Generic.List<string * Var>([ "project", VId project; "item", VId item ])
            values
            |> List.iteri (fun index (fieldId, value) ->
                let kind, argument =
                    match value with
                    | OptionValue option -> "ID!", ("v" + string index, VId option)
                    | TextValue text -> "String!", ("v" + string index, VString text)
                declared.Append(sprintf ", $f%d: ID!, $v%d: %s, $c%d: String!" index index kind index) |> ignore
                let valueShape = match value with OptionValue _ -> sprintf "{singleSelectOptionId: $v%d}" index | TextValue _ -> sprintf "{text: $v%d}" index
                fields.Append(sprintf " e%d: updateProjectV2ItemFieldValue(input: {projectId: $project, itemId: $item, fieldId: $f%d, value: %s, clientMutationId: $c%d}) { clientMutationId }" index index valueShape index) |> ignore
                variables.Add(("f" + string index, VId fieldId))
                variables.Add argument
                variables.Add(("c" + string index, VString(sprintf "%s-%d" mutationId index))))
            [ graphql (sprintf "mutation(%s) {%s }" (declared.ToString()) (fields.ToString())) (List.ofSeq variables) item ]
        | ClearFieldValue(project, item, field) ->
            [ graphql "mutation($project: ID!, $item: ID!, $f0: ID!, $c0: String!) { e0: clearProjectV2ItemFieldValue(input: {projectId: $project, itemId: $item, fieldId: $f0, clientMutationId: $c0}) { clientMutationId } }"
                  [ "project", VId project; "item", VId item; "f0", VId field; "c0", VString(mutationId + "-0") ] item ]
        | AddItem(project, content) ->
            [ graphql "mutation($project: ID!, $content: ID!, $c0: String!) { e0: addProjectV2ItemById(input: {projectId: $project, contentId: $content, clientMutationId: $c0}) { clientMutationId item { id } } }"
                  [ "project", VId project; "content", VId content; "c0", VString(mutationId + "-0") ] content ]
        | RemoveCreatedItem(project, item) ->
            [ graphql "mutation($project: ID!, $item: ID!, $c0: String!) { e0: deleteProjectV2Item(input: {projectId: $project, itemId: $item, clientMutationId: $c0}) { clientMutationId deletedItemId } }"
                  [ "project", VId project; "item", VId item; "c0", VString(mutationId + "-0") ] item ]
        | ArchiveItem(project, item) ->
            [ graphql "mutation($project: ID!, $item: ID!, $c0: String!) { e0: archiveProjectV2Item(input: {projectId: $project, itemId: $item, clientMutationId: $c0}) { clientMutationId item { id } } }"
                  [ "project", VId project; "item", VId item; "c0", VString(mutationId + "-0") ] item ]
        | CloseDelivery delivery ->
            [ graphql "mutation($issue: ID!, $body: String!, $c0: String!) { e0: addComment(input: {subjectId: $issue, body: $body, clientMutationId: $c0}) { clientMutationId commentEdge { node { id } } } }"
                  [ "issue", VId delivery.IssueId; "body", VString delivery.Comment; "c0", VString(mutationId + "-comment") ] delivery.IssueId
              graphql "mutation($issue: ID!, $c0: String!) { e0: closeIssue(input: {issueId: $issue, stateReason: COMPLETED, clientMutationId: $c0}) { clientMutationId issue { id state } } }"
                  [ "issue", VId delivery.IssueId; "c0", VString(mutationId + "-close") ] delivery.IssueId ]

    // ---- the transport allowlist (design §5) ----

    let private commentText = Regex(@"#[^\r\n]*")
    let private argumentVariable (argument: string) = Regex(@"\b" + argument + @"\s*:\s*(\$?)(\w+)")
    let private declaredVariable = Regex(@"\$(\w+)\s*:")
    let private stateReason = Regex(@"\bstateReason\s*:\s*(\w+)")

    let admits (projectId: string) (closable: string -> bool) (request: Request) : Result<unit, string> =
        match request.Method, request.Path, request.Body with
        | "POST", "graphql", Query(document, variables) ->
            match V2TendReads.parseDocument document with
            | None -> Error "the mutation document does not parse"
            | Some parsed when parsed.HasStringLiteral -> Error "a tend mutation carries every value in a variable, never a literal"
            | Some _ when (variables |> List.map fst |> List.distinct |> List.length) <> variables.Length ->
                Error "a variable is supplied twice"
            | Some _ when (let header = commentText.Replace(document, " ") in
                           let declared = [ for m in declaredVariable.Matches(header.Substring(0, max 0 (header.IndexOf '{'))) -> m.Groups.[1].Value ]
                           (List.distinct declared).Length <> declared.Length) ->
                Error "a variable is declared twice"
            | Some { Operations = [ { Kind = "mutation"; RootFields = fields } ] } when not fields.IsEmpty ->
                let text = commentText.Replace(document, " ")
                let variable name = variables |> List.tryPick (fun (n, v) -> if n = name then Some v else None)
                // Every occurrence of the argument names exactly the expected variable.
                let bound (argument: string) (name: string) (expected: int) =
                    let found = [ for m in (argumentVariable argument).Matches text -> m.Groups.[1].Value = "$" && m.Groups.[2].Value = name ]
                    found.Length = expected && List.forall id found
                if fields |> List.forall projectRootFields.Contains then
                    if variable "project" <> Some(VId projectId) then Error "a project mutation must target Project 3"
                    elif not (bound "projectId" "project" fields.Length) then Error "every project mutation must take its projectId from $project"
                    else Ok()
                elif fields |> List.exists issueRootFields.Contains then
                    match fields with
                    | [ field ] when issueRootFields.Contains field ->
                        let argument = if field = "addComment" then "subjectId" else "issueId"
                        // closeIssue closes as completed and nothing else (decision 9).
                        let reasons = [ for m in stateReason.Matches text -> m.Groups.[1].Value ]
                        match variable "issue" with
                        | _ when field = "closeIssue" && reasons <> [ "COMPLETED" ] -> Error "closeIssue must carry stateReason: COMPLETED, once"
                        | _ when field = "addComment" && not reasons.IsEmpty -> Error "addComment carries no stateReason"
                        | Some(VId issue) when issue = request.Subject && closable issue && bound argument "issue" 1 -> Ok()
                        | _ -> Error(sprintf "%s is admitted only for the issue of a C1 Delivery closure in this plan" field)
                    | _ -> Error "an issue mutation must be alone in its document"
                else Error(sprintf "root field outside the allowlist: %s" (String.Join(",", fields |> List.filter (fun f -> not (projectRootFields.Contains f || issueRootFields.Contains f)))))
            | Some _ -> Error "exactly one mutation operation is admitted"
        | _ -> Error "only a GraphQL POST is admitted"

    // The sender every tend mutation goes through: refused before dispatch unless `admits` passes.
    let allowlisted (projectId: string) (closable: string -> bool) (send: Request -> IoResult<Response>) : Request -> IoResult<Response> =
        fun request ->
            match admits projectId closable request with
            | Ok() -> send request
            | Error reason -> Error(Malformed(request.Subject, "tend allowlist: " + reason))

    // ---- JSON helpers ----

    let private str (e: JsonElement) (name: string) =
        match e.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
        | _ -> None

    let private obj (e: JsonElement) (name: string) =
        match e.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.Object -> Some v
        | _ -> None

    let private refOf (e: JsonElement) =
        match obj e "repository" |> Option.bind (fun r -> str r "nameWithOwner"), e.TryGetProperty "number" with
        | Some repo, (true, n) when n.ValueKind = JsonValueKind.Number -> Some(repo + "#" + string (n.GetInt32()))
        | _ -> None

    // Every nested connection must be complete: a truncated value proves nothing.
    let rec private complete (e: JsonElement) =
        match e.ValueKind with
        | JsonValueKind.Object ->
            let ok =
                match e.TryGetProperty "totalCount", e.TryGetProperty "nodes" with
                | (true, total), (true, nodes) when nodes.ValueKind = JsonValueKind.Array -> total.GetInt32() = nodes.GetArrayLength()
                | (true, _), _ -> false
                | _ -> true
            ok && (e.EnumerateObject() |> Seq.forall (fun p -> complete p.Value))
        | JsonValueKind.Array -> e.EnumerateArray() |> Seq.forall complete
        | _ -> true

    let private read (transport: IGitHubTransport) (request: Request) (decode: JsonElement -> Result<'a, string>) : Result<'a, string> =
        match GraphQl.read transport request (fun data ->
                try
                    match decode data with
                    | Ok value -> Ok value
                    | Error reason -> Error(Malformed(request.Subject, reason))
                with error -> Error(Malformed(request.Subject, error.Message))) with
        | Ok value -> Ok value
        | Error error -> Error(Errors.explain error)

    // ---- the item-scoped re-read (design §6) ----

    type FieldSchema = { Id: string; Name: string; DataType: string; Options: (string * string) list }

    type ItemState =
        { ItemId: string
          ProjectId: string
          Archived: bool
          ContentId: string
          Ref: string
          Open: bool
          Parent: string option
          Fields: FieldSchema list
          // Every reported field value, as its canonical JSON, keyed by field id.
          Values: Map<string, string>
          // The reported name (single select) or text of each value, keyed by field name.
          Named: Map<string, string>
          // Deciding PRs re-read with the item: state, draft and merge time, keyed by `OWNER/REPO#N`.
          Prs: Map<string, string * bool * string option> }

    let private fieldValueSelection =
        let field = "field { __typename ... on ProjectV2FieldCommon { id name } }"
        "fieldValues(first: 100) { totalCount nodes { __typename "
        + sprintf "... on ProjectV2ItemFieldSingleSelectValue { name optionId %s } " field
        + sprintf "... on ProjectV2ItemFieldTextValue { text %s } " field
        + sprintf "... on ProjectV2ItemFieldNumberValue { number %s } " field
        + sprintf "... on ProjectV2ItemFieldDateValue { date %s } " field
        + sprintf "... on ProjectV2ItemFieldIterationValue { iterationId title startDate duration %s } " field
        + sprintf "... on ProjectV2ItemFieldLabelValue { labels(first: 100) { totalCount nodes { name } } %s } " field
        + sprintf "... on ProjectV2ItemFieldMilestoneValue { milestone { id } %s } " field
        + sprintf "... on ProjectV2ItemFieldRepositoryValue { repository { id } %s } " field
        + sprintf "... on ProjectV2ItemFieldUserValue { users(first: 100) { totalCount nodes { login } } %s } " field
        + sprintf "... on ProjectV2ItemFieldPullRequestValue { pullRequests(first: 100) { totalCount nodes { id } } %s } " field
        + "} }"

    let private itemDocument (prs: int) =
        let declared = String.Join("", [ for i in 0 .. prs - 1 -> sprintf ", $o%d: String!, $n%d: String!, $k%d: Int!" i i i ])
        let selected = String.Join(" ", [ for i in 0 .. prs - 1 -> sprintf "pr%d: repository(owner: $o%d, name: $n%d) { pullRequest(number: $k%d) { state isDraft mergedAt } }" i i i i ])
        sprintf "query($item: ID!%s) { node(id: $item) { __typename ... on ProjectV2Item { id isArchived project { id fields(first: 50) { totalCount nodes { __typename ... on ProjectV2FieldCommon { id name dataType } ... on ProjectV2SingleSelectField { options { id name } } } } } content { __typename ... on Issue { id number state repository { nameWithOwner } parent { number repository { nameWithOwner } } } } %s } } %s }"
            declared fieldValueSelection selected

    // Re-read one item, its field schema and the given deciding PRs (`OWNER/REPO#N`) in one request.
    let readItem (transport: IGitHubTransport) (itemId: string) (prs: string list) : Result<ItemState, string> =
        let parts =
            prs |> List.map (fun pr ->
                let repo, number = match pr.LastIndexOf '#' with -1 -> pr, "" | i -> pr.Substring(0, i), pr.Substring(i + 1)
                let owner, name = match repo.IndexOf '/' with -1 -> repo, "" | i -> repo.Substring(0, i), repo.Substring(i + 1)
                owner, name, (match Int32.TryParse number with | true, n -> n | _ -> -1))
        if parts |> List.exists (fun (o, n, k) -> o = "" || n = "" || k < 0) then Error "a deciding PR reference is malformed"
        else
            let variables =
                [ yield "item", VId itemId
                  for i, (o, n, k) in List.indexed parts do
                      yield sprintf "o%d" i, VString o
                      yield sprintf "n%d" i, VString n
                      yield sprintf "k%d" i, VNumber(double k) ]
            read transport (graphql (itemDocument parts.Length) variables itemId) (fun data ->
                match obj data "node" with
                | Some node when str node "__typename" = Some "ProjectV2Item" && str node "id" = Some itemId ->
                    let project = (obj node "project").Value
                    let content = obj node "content"
                    match content with
                    | Some issue when str issue "__typename" = Some "Issue" ->
                        let fieldsNode = (obj project "fields").Value
                        let values = (obj node "fieldValues").Value
                        if not (complete fieldsNode) || not (complete values) then Error "the item's fields or values are truncated"
                        else
                            let fields =
                                [ for f in fieldsNode.GetProperty("nodes").EnumerateArray() do
                                    match str f "id", str f "name" with
                                    | Some id, Some name ->
                                        let options =
                                            match f.TryGetProperty "options" with
                                            | true, o when o.ValueKind = JsonValueKind.Array -> [ for x in o.EnumerateArray() -> (str x "id").Value, (str x "name").Value ]
                                            | _ -> []
                                        yield { Id = id; Name = name; DataType = defaultArg (str f "dataType") ""; Options = options }
                                    | _ -> () ]
                            let valueNodes = [ for v in values.GetProperty("nodes").EnumerateArray() -> v ]
                            let fieldOf (v: JsonElement) = obj v "field" |> Option.bind (fun f -> str f "id")
                            if valueNodes |> List.exists (fun v -> (fieldOf v).IsNone) then Error "a field value names no field"
                            else
                                let keyed = valueNodes |> List.map (fun v -> (fieldOf v).Value, v)
                                if (keyed |> List.map fst |> List.distinct |> List.length) <> keyed.Length then Error "a field holds two values"
                                else
                                    let named =
                                        keyed
                                        |> List.choose (fun (id, v) ->
                                            fields |> List.tryFind (fun f -> f.Id = id)
                                            |> Option.bind (fun f -> (match str v "name" with Some n -> Some n | None -> str v "text") |> Option.map (fun x -> f.Name, x)))
                                        |> Map.ofList
                                    let prStates =
                                        [ for i, pr in List.indexed prs do
                                            match obj data (sprintf "pr%d" i) |> Option.bind (fun r -> obj r "pullRequest") with
                                            | Some p -> yield pr, ((defaultArg (str p "state") ""), (match p.TryGetProperty "isDraft" with | true, d -> d.ValueKind = JsonValueKind.True | _ -> false), str p "mergedAt")
                                            | None -> () ]
                                    if prStates.Length <> prs.Length then Error "a deciding PR could not be read"
                                    else
                                        Ok { ItemId = itemId
                                             ProjectId = (str project "id").Value
                                             Archived = (match node.TryGetProperty "isArchived" with | true, a -> a.ValueKind = JsonValueKind.True | _ -> true)
                                             ContentId = (str issue "id").Value
                                             Ref = (refOf issue).Value
                                             Open = (str issue "state" = Some "OPEN")
                                             Parent = obj issue "parent" |> Option.bind refOf
                                             Fields = fields
                                             Values = keyed |> List.map (fun (id, v) -> id, v.GetRawText()) |> Map.ofList
                                             Named = named
                                             Prs = Map.ofList prStates }
                    | _ -> Error "the item's content is not a readable issue"
                | _ -> Error "the item could not be read")

    // ---- custody (design §6 per effect, steps 3-6) ----

    let custodySchema = "fsgg.board-tend-custody/1"

    let private keyName (key: string) =
        SHA256.HashData(Encoding.UTF8.GetBytes(key.ToLowerInvariant())) |> Convert.ToHexString |> fun h -> h.Substring(0, 32).ToLowerInvariant()

    let private privateDirectory (path: string) =
        if not (Directory.Exists path) then
            Directory.CreateDirectory(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
        if not (OperatingSystem.IsWindows()) then
            let others = UnixFileMode.GroupRead ||| UnixFileMode.GroupWrite ||| UnixFileMode.GroupExecute ||| UnixFileMode.OtherRead ||| UnixFileMode.OtherWrite ||| UnixFileMode.OtherExecute
            if File.GetUnixFileMode path &&& others <> enum<UnixFileMode> 0 then
                File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)

    // Atomic durable replace: a private temporary file written through and flushed, renamed over the record,
    // then the directory synced.
    let private atomicWrite (syncDirectory: string -> unit) (path: string) (json: string) =
        let directory = Path.GetDirectoryName path
        privateDirectory directory
        let temporary = Path.Combine(directory, "." + Path.GetFileName path + "." + Guid.NewGuid().ToString("N"))
        let options = FileStreamOptions(Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.WriteThrough)
        if not (OperatingSystem.IsWindows()) then options.UnixCreateMode <- Nullable(UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        try
            using (new FileStream(temporary, options)) (fun stream ->
                let bytes = UTF8Encoding(false).GetBytes json
                stream.Write(bytes, 0, bytes.Length)
                stream.Flush true)
            File.Move(temporary, path, true)
        finally
            if File.Exists temporary then File.Delete temporary
        syncDirectory directory

    // The reconciler's own custody: one record per send under `<root>/<run>/`.
    type Custody(root: string, run: string, syncDirectory: string -> unit) =
        member _.Root = root
        member _.Run = run
        member _.Path(key: string) = Path.Combine(root, run, keyName key + ".json")
        member this.Write(key: string, record: JsonObject) =
            record["schema"] <- custodySchema
            record["run"] <- run
            record["key"] <- key
            atomicWrite syncDirectory (this.Path key) (record.ToJsonString())
        // Every readable record in every run, with its path; malformed files are counted, never trusted.
        member _.Records() : (string * JsonObject) list * int =
            if not (Directory.Exists root) then [], 0
            else
                let mutable malformed = 0
                let found =
                    [ for directory in Directory.GetDirectories root do
                        for file in Directory.GetFiles(directory, "*.json") do
                            match (try JsonNode.Parse(File.ReadAllText file) |> Option.ofObj with _ -> None) with
                            | Some(:? JsonObject as record) when
                                (record["schema"] |> Option.ofObj |> Option.map string) = Some custodySchema
                                && (record["key"] |> Option.ofObj |> Option.map (fun k -> keyName (string k) + ".json")) = Some(Path.GetFileName file) ->
                                yield file, record
                            | _ -> malformed <- malformed + 1 ]
                found, malformed
        member this.Rewrite(path: string, record: JsonObject) = atomicWrite syncDirectory path (record.ToJsonString())

    // ---- the merge gate's custody, for GR (design §6) ----

    let gateSchema = "fsgg.routine-board-custody/1"
    let gateStates = set [ "possible-send"; "verified"; "deferred"; "superseded"; "refused"; "not-applicable" ]

    type GateRecord = { Path: string; Record: JsonObject; Repo: string; Pr: int; Delivery: string; ItemId: string option; State: string; MergedAt: string option }

    // The merge gate's custody directory (`$XDG_STATE_HOME/fsgg/board-gate`), read and rewritten with the
    // gate's own validation: a record whose path is not the hash of its repository and PR is malformed.
    type GateCustody(root: string, syncDirectory: string -> unit) =
        member _.Root = root
        member _.Records() : GateRecord list * int =
            if not (Directory.Exists root) then [], 0
            else
                let mutable malformed = 0
                let found =
                    [ for file in Directory.GetFiles(root, "*.json") |> Array.sort do
                        let parsed =
                            try
                                match JsonNode.Parse(File.ReadAllText file) with
                                | :? JsonObject as r ->
                                    let text (name: string) = match r[name] with | :? JsonValue as v when v.GetValueKind() = JsonValueKind.String -> Some(v.GetValue<string>()) | _ -> None
                                    let pr = match r["pr"] with | :? JsonValue as v when v.GetValueKind() = JsonValueKind.Number -> Some(v.GetValue<int>()) | _ -> None
                                    match text "schema", text "repo", pr, text "delivery", text "state" with
                                    | Some s, Some repo, Some pr, Some delivery, Some state when s = gateSchema && gateStates.Contains state ->
                                        let expected = keyName (sprintf "%s#%d" repo pr) + ".json"
                                        if Path.GetFileName file <> expected then None
                                        else Some { Path = file; Record = r; Repo = repo; Pr = pr; Delivery = delivery; ItemId = text "itemId"; State = state; MergedAt = text "mergedAt" }
                                    | _ -> None
                                | _ -> None
                            with _ -> None
                        match parsed with
                        | Some record -> yield record
                        | None -> malformed <- malformed + 1 ]
                found, malformed
        member _.Rewrite(record: GateRecord, state: string, now: DateTimeOffset) =
            let copy = record.Record.DeepClone().AsObject()
            copy["state"] <- state
            if state = "verified" then copy["verifiedAt"] <- now.ToString("o")
            atomicWrite syncDirectory record.Path (copy.ToJsonString())

    // ---- outcomes ----

    type Outcome =
        { Subject: string
          Ref: string
          Kind: string
          Result: string
          Detail: string }

    type Settings =
        { ProjectId: string
          RootLogins: Set<string>
          MaxItemsPerRun: int
          MinimumMutationInterval: TimeSpan
          ArchiveRetention: int }

    let defaultSettings projectId rootLogins =
        { ProjectId = projectId; RootLogins = rootLogins; MaxItemsPerRun = 100; MinimumMutationInterval = TimeSpan.FromSeconds 1.0; ArchiveRetention = 200 }

    // Mutations at most one per `interval` (design §6 write budget).
    type Pacer(clock: TimeProvider, wait: TimeSpan -> unit, interval: TimeSpan) =
        let mutable last: int64 option = None
        member _.Before() =
            match last with
            | Some stamp ->
                let elapsed = clock.GetElapsedTime stamp
                if elapsed < interval then wait (interval - elapsed)
            | None -> ()
        member _.After() = last <- Some(clock.GetTimestamp())

    type Context =
        { Reads: IGitHubTransport
          Send: Request -> IoResult<Response>
          Custody: Custody
          Gate: GateCustody
          Clock: TimeProvider
          Pacer: Pacer
          // The issues whose close is running; the allowlisted sender admits issue mutations only for these.
          Closable: Collections.Generic.HashSet<string>
          Settings: Settings }

    // The Project 3 field a plan field writes (the plan's own keys are lower case).
    let boardField (field: V2Tend.Field) =
        match field with
        | V2Tend.StatusField -> "Status" | V2Tend.WorkLevelField -> "Work level" | V2Tend.TrackField -> "Track" | V2Tend.RoadmapField -> "Roadmap"

    let private statusText =
        function
        | V2Tend.Backlog -> "Backlog" | V2Tend.Ready -> "Ready" | V2Tend.InProgress -> "In progress"
        | V2Tend.InReview -> "In review" | V2Tend.Blocked -> "Blocked" | V2Tend.Done -> "Done"

    let private workLevelText = function V2Tend.Outcome -> "Outcome" | V2Tend.Delivery -> "Delivery"
    let private trackText = function V2Tend.ActiveDelivery -> "Active delivery" | V2Tend.FollowUp -> "Follow-up"

    let private prStateText = function V2Tend.PrOpen -> "OPEN" | V2Tend.PrMerged -> "MERGED" | V2Tend.PrClosed -> "CLOSED"

    // The snapshot's facts for an item, as the re-read reports them.
    let private snapshotNamed (card: V2Tend.Card) =
        [ "Status", card.Status |> Option.map statusText
          "Work level", card.WorkLevel |> Option.map workLevelText
          "Track", card.Track |> Option.map trackText
          "Roadmap", card.Roadmap ]

    // A difference between the snapshot and the immediate re-read refuses the item's effects this run. The re-read
    // covers the item itself and, for S3, S3b and S4, the deciding PRs. The inputs of O1 (the children's Status)
    // and T1i (the parent's Track) live on other items and are not re-read before the send; that is added when
    // those rules are enabled (both are gated or heuristic and off in the starting set).
    let private drift (card: V2Tend.Card) (item: ItemState) (deciding: V2Tend.LinkedPr list) : string option =
        let canonical = V2TendReads.canonicalRef
        if item.Archived then Some "item-archived"
        elif canonical item.Ref <> canonical card.Ref then Some "item-content-changed"
        elif item.Open <> card.Open then Some "issue-state-changed"
        elif (item.Parent |> Option.map canonical) <> (card.Parent |> Option.map canonical) then Some "parent-changed"
        else
            match snapshotNamed card |> List.tryFind (fun (name, expected) -> item.Named.TryFind name <> expected) with
            | Some(name, _) -> Some("field-changed:" + name)
            | None ->
                let changed =
                    deciding |> List.exists (fun pr ->
                        match item.Prs.TryFind pr.Id with
                        | Some(state, draft, merged) ->
                            state <> prStateText pr.State || draft <> pr.Draft
                            || (merged |> Option.map DateTimeOffset.Parse) <> pr.MergedAt
                        | None -> true)
                if changed then Some "deciding-pr-changed" else None

    let private fieldValueFor (item: ItemState) (effect: V2Tend.Effect) : Result<string * FieldValue, string> =
        let name = boardField effect.Field
        match item.Fields |> List.tryFind (fun f -> f.Name = name) with
        | None -> Error("field-missing:" + name)
        | Some field ->
            match effect.Field with
            | V2Tend.RoadmapField ->
                if field.DataType <> "TEXT" then Error("field-type:" + name) else Ok(field.Id, TextValue effect.Target)
            | _ ->
                match field.Options |> List.filter (fun (_, n) -> n = effect.Target) with
                | [ id, _ ] -> Ok(field.Id, OptionValue id)
                | _ -> Error("option-missing:" + name + ":" + effect.Target)

    // The readback equals the re-read with only the selected fields moved to their targets.
    let private provenByReadback (before: ItemState) (after: ItemState) (moves: (V2Tend.Effect * string) list) =
        let moved = moves |> List.map snd |> Set.ofList
        let unchanged =
            after.ItemId = before.ItemId && after.ProjectId = before.ProjectId && after.ContentId = before.ContentId
            && after.Open = before.Open && after.Parent = before.Parent && not after.Archived
            && (before.Values |> Map.filter (fun k _ -> not (moved.Contains k))) = (after.Values |> Map.filter (fun k _ -> not (moved.Contains k)))
        unchanged && moves |> List.forall (fun (effect, _) -> after.Named.TryFind(boardField effect.Field) = Some effect.Target)

    let private echoed (response: Response) (subject: string) (expected: string list) =
        match GraphQl.decode subject response.Body (fun data ->
                let ids = [ for i in 0 .. expected.Length - 1 -> obj data (sprintf "e%d" i) |> Option.bind (fun e -> str e "clientMutationId") ]
                Ok ids) with
        | Ok ids -> ids = (expected |> List.map Some)
        | Error _ -> false

    let private mutationIds (request: Request) =
        match request.Body with
        | Query(_, variables) -> variables |> List.choose (fun (n, v) -> match v with VString s when n.StartsWith "c" -> Some s | _ -> None)
        | _ -> []

    // ---- the run ----

    // Item ids the gate's custody holds: `possible-send` (uncertain) and `deferred` (replay pending).
    let gateHolds (records: GateRecord list) =
        let ids state = records |> List.filter (fun r -> r.State = state) |> List.choose (fun r -> r.ItemId) |> Set.ofList
        ids "possible-send", ids "deferred"

    // Cards the Done-retention archiver may archive (Done and closed beyond the newest `retention`): the
    // reconciler writes nothing to them while the archiver runs outside the writer lock (design §8).
    let archiveEligible (snapshot: V2Tend.Snapshot) (retention: int) =
        snapshot.Cards
        |> List.filter (fun c -> c.Status = Some V2Tend.Done && not c.Open)
        |> List.sortByDescending (fun c -> c.ClosedAt |> Option.defaultValue DateTimeOffset.MinValue, c.ItemId)
        |> fun closed -> if closed.Length <= retention then [] else List.skip retention closed
        |> List.map (fun c -> c.ItemId)
        |> Set.ofList

    let private fieldRecord (effects: (V2Tend.Effect * string * FieldValue) list) (item: ItemState) (state: string) (now: DateTimeOffset) =
        let record = JsonObject()
        record["kind"] <- "fields"
        record["itemId"] <- item.ItemId
        record["ref"] <- item.Ref
        record["state"] <- state
        record["recordedAt"] <- now.ToString("o")
        let fields = JsonArray()
        for effect, fieldId, value in effects do
            let f = JsonObject()
            f["effect"] <- effect.Id
            f["field"] <- boardField effect.Field
            f["fieldId"] <- fieldId
            f["preimage"] <- (match effect.Preimage with Some p -> JsonValue.Create p :> JsonNode | None -> null)
            f["target"] <- effect.Target
            f["value"] <- (match value with OptionValue o -> o | TextValue t -> t)
            fields.Add f
        record["fields"] <- fields
        record

    // Apply the plan's field effects of one item group under the item-scoped guard. Returns one outcome per
    // item and whether every effect of the item was written.
    let private applyItem (context: Context) (card: V2Tend.Card) (effects: V2Tend.Effect list) : Outcome * bool =
        let outcome result detail = { Subject = card.ItemId; Ref = card.Ref; Kind = "fields"; Result = result; Detail = detail }, result = "written" || result = "noop"
        let deciding =
            if effects |> List.exists (fun e -> e.Rules |> List.exists (fun r -> r = "S3" || r = "S3b" || r = "S4")) then card.Prs else []
        // The request immediately before the mutation is this re-read of the same item and its deciding PRs.
        match readItem context.Reads card.ItemId (deciding |> List.map (fun pr -> pr.Id)) with
        | Error reason -> outcome "refused" ("reread-failed: " + reason)
        | Ok before ->
            if before.ProjectId <> context.Settings.ProjectId then outcome "refused" "item-not-on-project"
            else
                match drift card before deciding with
                | Some reason -> outcome "refused" reason
                | None ->
                    let pending = effects |> List.filter (fun e -> before.Named.TryFind(boardField e.Field) <> Some e.Target)
                    if pending.IsEmpty then outcome "noop" ""
                    else
                        let resolved = pending |> List.map (fun e -> e, fieldValueFor before e)
                        match resolved |> List.tryPick (fun (_, r) -> match r with Error reason -> Some reason | Ok _ -> None) with
                        | Some reason -> outcome "refused" reason
                        | None ->
                            let moves = resolved |> List.map (fun (e, r) -> let fieldId, value = Result.defaultWith (fun _ -> failwith "unreachable") r in e, fieldId, value)
                            let now = context.Clock.GetUtcNow()
                            let record = fieldRecord moves before "possible-send" now
                            let written =
                                try
                                    context.Custody.Write(card.ItemId, record)
                                    true
                                with _ -> false
                            if not written then outcome "refused" "refused-custody"
                            else
                                let change = SetFieldValue(context.Settings.ProjectId, card.ItemId, moves |> List.map (fun (_, id, value) -> id, value))
                                let request = render (sprintf "tend-%s-%s" context.Custody.Run (keyName card.ItemId)) change |> List.head
                                context.Pacer.Before()
                                let sent = context.Send request
                                context.Pacer.After()
                                match sent with
                                | Error error -> outcome "uncertain" ("send: " + Errors.explain error)
                                | Ok response when not (echoed response card.ItemId (mutationIds request)) -> outcome "uncertain" "send-unconfirmed"
                                | Ok _ ->
                                    match readItem context.Reads card.ItemId [] with
                                    | Error reason -> outcome "uncertain" ("readback: " + reason)
                                    | Ok after ->
                                        if provenByReadback before after (moves |> List.map (fun (e, id, _) -> e, id)) then
                                            (try
                                                record["state"] <- "verified"
                                                record["verifiedAt"] <- context.Clock.GetUtcNow().ToString("o")
                                                context.Custody.Write(card.ItemId, record)
                                             with _ -> ())
                                            outcome "written" (String.Join(",", moves |> List.map (fun (e, _, _) -> e.Id)))
                                        else outcome "uncertain" "readback-differs"

    // ---- CloseDelivery (design §5) ----

    type IssueReread =
        { IssueId: string
          Ref: string
          Open: bool
          // Every cross-referencing PR: reference, state, draft, body.
          CrossReferences: (string * string * bool * string) list
          TendClosed: bool }

    let private issueTimelineDocument =
        "query($issue: ID!, $cursor: String) { node(id: $issue) { __typename ... on Issue { id number state repository { nameWithOwner } timelineItems(itemTypes: [CROSS_REFERENCED_EVENT], first: 100, after: $cursor) { totalCount pageInfo { hasNextPage endCursor } nodes { ... on CrossReferencedEvent { id source { __typename ... on PullRequest { number state isDraft body repository { nameWithOwner } } } } } } } } }"

    let private issueCommentsDocument =
        "query($issue: ID!, $cursor: String) { node(id: $issue) { __typename ... on Issue { comments(first: 100, after: $cursor) { totalCount pageInfo { hasNextPage endCursor } nodes { id body author { login } } } } } }"

    let private drainNode (transport: IGitHubTransport) (subject: string) (document: string) (issueId: string) (connection: string) (key: JsonElement -> string) (decode: JsonElement -> 'a) : IoResult<'a list> =
        GraphQl.drain subject connection { MaxPages = 20; MaxItems = 2000 } (fun cursor ->
            let variables = [ "issue", VId issueId ] @ (match cursor with Some c -> [ "cursor", VString c ] | None -> [])
            GraphQl.read transport (graphql document variables subject) (fun data ->
                match obj data "node" |> Option.bind (fun n -> obj n connection) with
                | Some page -> GraphQl.page subject connection fst (fun n -> Ok(key n, decode n)) page
                | None -> Error(Malformed(subject, "the issue could not be read"))))
        |> Result.map (List.map snd)

    let private visible (body: string) =
        let normalised = (if isNull body then "" else body).Replace("\r\n", "\n").Replace("\r", "\n")
        Regex.Replace(Regex.Replace(normalised, @"```.*?```|~~~.*?~~~", " ", RegexOptions.Singleline), @"`[^`\n]*`", " ")

    // Re-read a Delivery issue for C1: its complete cross-reference timeline and its complete comments.
    let readIssue (transport: IGitHubTransport) (rootLogins: Set<string>) (issueId: string) : Result<IssueReread, string> =
        let subject = "tend close re-read"
        let timeline =
            drainNode transport subject issueTimelineDocument issueId "timelineItems" (fun n -> defaultArg (str n "id") "") (fun n ->
                match obj n "source" with
                | Some source when str source "__typename" = Some "PullRequest" ->
                    refOf source |> Option.map (fun r ->
                        r, defaultArg (str source "state") "", (match source.TryGetProperty "isDraft" with | true, d -> d.ValueKind = JsonValueKind.True | _ -> false), defaultArg (str source "body") "")
                | _ -> None)
        let comments =
            drainNode transport subject issueCommentsDocument issueId "comments" (fun n -> defaultArg (str n "id") "") (fun n ->
                let author = obj n "author" |> Option.bind (fun a -> str a "login")
                (match author with Some a -> rootLogins.Contains a | None -> false) && (visible (defaultArg (str n "body") "")).Contains tendClosedMarker)
        let head =
            read transport (graphql "query($issue: ID!) { node(id: $issue) { __typename ... on Issue { id number state repository { nameWithOwner } } } }" [ "issue", VId issueId ] subject) (fun data ->
                match obj data "node" with
                | Some n when str n "__typename" = Some "Issue" -> Ok((refOf n).Value, str n "state" = Some "OPEN")
                | _ -> Error "the issue could not be read")
        match head, timeline, comments with
        | Ok(reference, isOpen), Ok events, Ok marks ->
            Ok { IssueId = issueId; Ref = reference; Open = isOpen; CrossReferences = List.choose id events; TendClosed = List.exists id marks }
        | Error e, _, _ -> Error e
        | _, Error e, _ | _, _, Error e -> Error(Errors.explain e)

    // The recorded independent verdict of a merged PR: the newest `fsgg:review-decision/v2` record's verdict
    // and comment URL, with the PR's merge commit.
    let readVerdict (transport: IGitHubTransport) (pr: string) : Result<string option * (string * string) option, string> =
        let repo, number = match pr.LastIndexOf '#' with -1 -> pr, "" | i -> pr.Substring(0, i), pr.Substring(i + 1)
        let owner, name = match repo.IndexOf '/' with -1 -> repo, "" | i -> repo.Substring(0, i), repo.Substring(i + 1)
        match Int32.TryParse number with
        | true, n when owner <> "" && name <> "" ->
            let subject = "tend close verdict"
            let mutable commit: string option = None
            let comments =
                GraphQl.drain subject "comments" { MaxPages = 20; MaxItems = 2000 } (fun cursor ->
                    let variables = [ "o", VString owner; "n", VString name; "k", VNumber(double n) ] @ (match cursor with Some c -> [ "cursor", VString c ] | None -> [])
                    GraphQl.read transport (graphql "query($o: String!, $n: String!, $k: Int!, $cursor: String) { repository(owner: $o, name: $n) { pullRequest(number: $k) { mergeCommit { oid } comments(first: 100, after: $cursor) { totalCount pageInfo { hasNextPage endCursor } nodes { id url body } } } } }" variables subject) (fun data ->
                        match obj data "repository" |> Option.bind (fun r -> obj r "pullRequest") with
                        | Some p ->
                            commit <- obj p "mergeCommit" |> Option.bind (fun m -> str m "oid")
                            match obj p "comments" with
                            | Some page -> GraphQl.page subject "comments" (fun (id, _, _) -> id) (fun c -> Ok(defaultArg (str c "id") "", defaultArg (str c "url") "", defaultArg (str c "body") "")) page
                            | None -> Error(Malformed(subject, "the PR comments could not be read"))
                        | None -> Error(Malformed(subject, "the PR could not be read"))))
            match comments with
            | Error e -> Error(Errors.explain e)
            | Ok list ->
                let verdict =
                    list
                    |> List.choose (fun (_, url, body) ->
                        let text = visible body
                        let index = body.IndexOf "<!-- fsgg:review-decision/v2 -->"
                        if index < 0 || not (text.Contains "<!-- fsgg:review-decision/v2 -->") then None
                        else
                            let m = Regex.Match(body.Substring index, "\"verdict\"\\s*:\\s*\"([a-z-]+)\"")
                            if m.Success then Some(m.Groups.[1].Value, url) else None)
                    |> List.tryLast
                Ok(commit, verdict)
        | _ -> Error "a merged PR reference is malformed"

    // The C1 closing comment (dispatch rule 3): each merged PR with its merge commit and recorded independent
    // verdict, or "Unknown: no recorded independent verdict", and the `fsgg:tend-closed/v1` marker.
    let closingComment (lines: (string * string option * (string * string) option) list) =
        let line (pr, commit, verdict) =
            let merge = match commit with Some oid -> "merge commit " + oid | None -> "merge commit Unknown"
            let judged = match verdict with Some(v, url) -> sprintf "independent verdict %s (%s)" v url | None -> "Unknown: no recorded independent verdict"
            sprintf "- %s: %s; %s" pr merge judged
        String.Join(
            "\n",
            [ yield "Closed by `board-v2 tend` under rule C1 (owner decision 9, FS-GG/.github#4730 issuecomment-6551301543): every strictly linked PR is merged, none is open, and the last merge declares `fsgg:delivery-completes/v1`."
              yield ""
              for l in lines -> line l
              yield ""
              yield "Reopen this issue if work remains: tend never closes a reopened issue again."
              yield ""
              yield tendClosedMarker ])

    // The only constructor of a `CloseDelivery` change: a C1 closure of the plan whose item re-read shows an
    // open Delivery (populated Work level) In review, and whose issue re-read is complete, carries no reopen
    // marker and has no open or draft linked PR.
    let deliveryToClose (closure: V2Tend.Closure) (item: ItemState) (issue: IssueReread) (comment: string) : Result<DeliveryToClose, string> =
        let own = V2TendReads.canonicalRef closure.Ref
        if item.ItemId <> closure.ItemId || V2TendReads.canonicalRef item.Ref <> own || item.ContentId <> issue.IssueId then Error "close-subject-changed"
        elif item.Named.TryFind "Work level" <> Some "Delivery" then Error "close-not-a-delivery"
        elif item.Named.TryFind "Status" <> Some "In review" then Error "close-not-in-review"
        elif not item.Open || not issue.Open then Error "close-issue-not-open"
        elif issue.TendClosed then Error "close-reopened-after-close"
        elif issue.CrossReferences |> List.exists (fun (r, state, _, body) -> state = "OPEN" && V2TendReads.bodyRefs (r.Substring(0, max 0 (r.LastIndexOf '#'))) body |> List.contains own) then
            Error "close-open-linked-pr"
        elif not (comment.Contains tendClosedMarker) then Error "close-comment-unmarked"
        else Ok { IssueId = issue.IssueId; ItemId = item.ItemId; Ref = item.Ref; Comment = comment }

    let private closeOne (context: Context) (closure: V2Tend.Closure) : Outcome =
        let closable = context.Closable
        let pacer = context.Pacer
        let outcome result detail = { Subject = closure.ItemId; Ref = closure.Ref; Kind = "close"; Result = result; Detail = detail }
        let verdicts = closure.MergedPrs |> List.map (fun pr -> pr, readVerdict context.Reads pr)
        match verdicts |> List.tryPick (fun (_, v) -> match v with Error e -> Some e | Ok _ -> None) with
        | Some reason -> outcome "refused" ("verdict-read-failed: " + reason)
        | None ->
            let comment = closingComment (verdicts |> List.map (fun (pr, v) -> let commit, verdict = Result.defaultWith (fun _ -> failwith "unreachable") v in pr, commit, verdict))
            match readItem context.Reads closure.ItemId [] with
            | Error reason -> outcome "refused" ("reread-failed: " + reason)
            | Ok item when item.ProjectId <> context.Settings.ProjectId -> outcome "refused" "item-not-on-project"
            | Ok item ->
                match readIssue context.Reads context.Settings.RootLogins item.ContentId with
                | Error reason -> outcome "refused" ("reread-failed: " + reason)
                | Ok issue ->
                    match deliveryToClose closure item issue comment with
                    | Error reason -> outcome "refused" reason
                    | Ok delivery ->
                        let record = JsonObject()
                        record["kind"] <- "close"
                        record["itemId"] <- closure.ItemId
                        record["issueId"] <- delivery.Issue
                        record["ref"] <- closure.Ref
                        record["state"] <- "possible-send"
                        record["recordedAt"] <- context.Clock.GetUtcNow().ToString("o")
                        let custody =
                            try
                                context.Custody.Write(delivery.Issue, record)
                                true
                            with _ -> false
                        if not custody then outcome "refused" "refused-custody"
                        else
                            closable.Add delivery.Issue |> ignore
                            try
                                match render (sprintf "tend-%s-%s" context.Custody.Run (keyName delivery.Issue)) (CloseDelivery delivery) with
                                | [ commentRequest; closeRequest ] ->
                                    pacer.Before()
                                    let commented = context.Send commentRequest
                                    pacer.After()
                                    let commentId =
                                        match commented with
                                        | Ok response when echoed response delivery.Issue (mutationIds commentRequest) ->
                                            GraphQl.decode delivery.Issue response.Body (fun data ->
                                                Ok(obj data "e0" |> Option.bind (fun e -> obj e "commentEdge") |> Option.bind (fun e -> obj e "node") |> Option.bind (fun n -> str n "id")))
                                            |> Result.toOption |> Option.flatten
                                        | _ -> None
                                    match commentId with
                                    | None -> outcome "uncertain" "comment-unconfirmed"
                                    | Some id ->
                                        (try
                                            record["commentId"] <- id
                                            context.Custody.Write(delivery.Issue, record)
                                         with _ -> ())
                                        pacer.Before()
                                        let closed = context.Send closeRequest
                                        pacer.After()
                                        match closed with
                                        | Ok response when echoed response delivery.Issue (mutationIds closeRequest) ->
                                            let readback =
                                                read context.Reads (graphql "query($issue: ID!, $comment: ID!) { issue: node(id: $issue) { ... on Issue { state stateReason } } comment: node(id: $comment) { ... on IssueComment { body } } }" [ "issue", VId delivery.Issue; "comment", VId id ] delivery.Issue) (fun data ->
                                                    let issueState = obj data "issue" |> Option.bind (fun i -> str i "state")
                                                    let reason = obj data "issue" |> Option.bind (fun i -> str i "stateReason")
                                                    let body = obj data "comment" |> Option.bind (fun c -> str c "body")
                                                    Ok(issueState = Some "CLOSED" && reason = Some "COMPLETED" && (body |> Option.exists (fun b -> b.Contains tendClosedMarker))))
                                            match readback with
                                            | Ok true ->
                                                (try
                                                    record["state"] <- "verified"
                                                    record["verifiedAt"] <- context.Clock.GetUtcNow().ToString("o")
                                                    context.Custody.Write(delivery.Issue, record)
                                                 with _ -> ())
                                                outcome "written" (String.Join(",", closure.MergedPrs))
                                            | Ok false -> outcome "uncertain" "readback-differs"
                                            | Error reason -> outcome "uncertain" ("readback: " + reason)
                                        | _ -> outcome "uncertain" "close-unconfirmed"
                                | _ -> outcome "refused" "close-render"
                            finally
                                closable.Remove delivery.Issue |> ignore

    // ---- custody readback sweeps ----

    // Read back every `possible-send` record of the reconciler's own custody; a proven target rewrites it to
    // `verified`. Never sends. Returns the keys still uncertain.
    let sweepOwn (context: Context) : Set<string> * Outcome list =
        let records, malformed = context.Custody.Records()
        let outcomes =
            [ for path, record in records do
                if string record["state"] = "possible-send" then
                    let key = string record["key"]
                    let kind = string record["kind"]
                    // A close record is keyed by its issue node id; the item it holds is its itemId. Uncertainty is
                    // reported by item, so an uncertain close also blocks the item's field writes and its next close.
                    let held = record["itemId"] |> Option.ofObj |> Option.map string |> Option.defaultValue key
                    let reference = record["ref"] |> Option.ofObj |> Option.map string |> Option.defaultValue ""
                    let proven =
                        match kind with
                        | "fields" ->
                            match readItem context.Reads (string record["itemId"]) [] with
                            | Ok item ->
                                (record["fields"].AsArray() |> Seq.forall (fun f -> item.Named.TryFind(string f["field"]) = Some(string f["target"])))
                            | Error _ -> false
                        | "close" ->
                            match record["commentId"] with
                            | null -> false
                            | comment ->
                                read context.Reads (graphql "query($issue: ID!, $comment: ID!) { issue: node(id: $issue) { ... on Issue { state } } comment: node(id: $comment) { ... on IssueComment { body } } }" [ "issue", VId(string record["issueId"]); "comment", VId(string comment) ] key) (fun data ->
                                    Ok((obj data "issue" |> Option.bind (fun i -> str i "state")) = Some "CLOSED"
                                       && (obj data "comment" |> Option.bind (fun c -> str c "body") |> Option.exists (fun b -> b.Contains tendClosedMarker))))
                                |> Result.defaultValue false
                        | _ -> false
                    if proven then
                        record["state"] <- "verified"
                        record["verifiedAt"] <- context.Clock.GetUtcNow().ToString("o")
                        (try context.Custody.Rewrite(path, record) with _ -> ())
                        yield held, { Subject = key; Ref = reference; Kind = "sweep"; Result = "verified"; Detail = kind }
                    else yield held, { Subject = key; Ref = reference; Kind = "sweep"; Result = "uncertain"; Detail = kind } ]
        let uncertain = outcomes |> List.filter (fun (_, o) -> o.Result = "uncertain") |> List.map fst |> Set.ofList
        let malformedOutcome = if malformed > 0 then [ { Subject = "custody"; Ref = ""; Kind = "sweep"; Result = "malformed"; Detail = string malformed } ] else []
        uncertain, (outcomes |> List.map snd) @ malformedOutcome

    // ---- GR: replay of the merge gate's deferred records (design §6) ----

    let private gateOrder (record: GateRecord) = match record.MergedAt with Some m -> 1, m | None -> 0, ""

    let private gateSend (context: Context) (record: GateRecord) (item: ItemState) (target: string) : string =
        let effect : V2Tend.Effect =
            { Id = "GR:" + record.Repo + "#" + string record.Pr; ItemId = item.ItemId; Ref = item.Ref; Field = V2Tend.StatusField
              Preimage = item.Named.TryFind "Status"; Target = target; Rules = [ "GR" ]; DependsOn = [] }
        match fieldValueFor item effect with
        | Error _ ->
            context.Gate.Rewrite(record, "refused", context.Clock.GetUtcNow())
            "refused"
        | Ok(fieldId, value) ->
            context.Gate.Rewrite(record, "possible-send", context.Clock.GetUtcNow())
            let request = render (sprintf "tend-%s-gr-%s" context.Custody.Run (keyName (record.Repo + "#" + string record.Pr))) (SetFieldValue(context.Settings.ProjectId, item.ItemId, [ fieldId, value ])) |> List.head
            context.Pacer.Before()
            let sent = context.Send request
            context.Pacer.After()
            match sent with
            | Ok response when echoed response item.ItemId (mutationIds request) ->
                match readItem context.Reads item.ItemId [] with
                | Ok after when provenByReadback item after [ effect, fieldId ] ->
                    (try context.Gate.Rewrite(record, "verified", context.Clock.GetUtcNow()) with _ -> ())
                    "verified"
                | _ -> "uncertain"
            | _ -> "uncertain"

    // The gate's custody pass, under the writer lock: a read-only readback sweep of `possible-send` records,
    // supersession per Delivery by `mergedAt`, then (when `replay`) one guarded send for each Delivery's newest
    // `deferred` record in merge order. Without `replay` only the sweep runs and nothing is sent.
    let gatePass (context: Context) (replay: bool) : Outcome list =
        let records, malformed = context.Gate.Records()
        let outcome (record: GateRecord) result = { Subject = record.Repo + "#" + string record.Pr; Ref = record.Delivery; Kind = "gate"; Result = result; Detail = "" }
        let now () = context.Clock.GetUtcNow()
        let swept =
            [ for record in records do
                if record.State = "possible-send" then
                    match record.ItemId, record.Record["to"] |> Option.ofObj |> Option.map string with
                    | Some itemId, Some target ->
                        match readItem context.Reads itemId [] with
                        | Ok item when item.Named.TryFind "Status" = Some target ->
                            (try context.Gate.Rewrite(record, "verified", now ()) with _ -> ())
                            yield outcome record "swept-verified"
                        | _ -> yield outcome record "swept-uncertain"
                    | _ -> yield outcome record "swept-uncertain" ]
        let replayed =
            if not replay then []
            else
                let records, _ = context.Gate.Records()
                let groups = records |> List.groupBy (fun r -> V2TendReads.canonicalRef r.Delivery)
                let superseded =
                    [ for _, group in groups do
                        let ordered = group |> List.sortBy gateOrder
                        for older in List.take (ordered.Length - 1) ordered do
                            if older.State = "deferred" then
                                context.Gate.Rewrite(older, "superseded", now ())
                                yield outcome older "superseded" ]
                let pending =
                    groups
                    |> List.choose (fun (_, group) -> let newest = group |> List.sortBy gateOrder |> List.last in if newest.State = "deferred" then Some newest else None)
                    |> List.sortBy gateOrder
                let sent =
                    [ for record in pending ->
                        match record.ItemId, record.Record["to"] |> Option.ofObj |> Option.map string, record.Record["from"] |> Option.ofObj |> Option.map string with
                        | Some itemId, Some target, from ->
                            match readItem context.Reads itemId [] with
                            | Error _ -> outcome record "unreadable"
                            | Ok item when item.ProjectId <> context.Settings.ProjectId || V2TendReads.canonicalRef item.Ref <> V2TendReads.canonicalRef record.Delivery ->
                                context.Gate.Rewrite(record, "refused", now ())
                                outcome record "refused"
                            | Ok item when not item.Open ->
                                context.Gate.Rewrite(record, "not-applicable", now ())
                                outcome record "not-applicable"
                            | Ok item when item.Named.TryFind "Status" <> from || from.IsNone ->
                                context.Gate.Rewrite(record, "refused", now ())
                                outcome record "refused"
                            | Ok item -> outcome record (gateSend context record item target)
                        | _ ->
                            context.Gate.Rewrite(record, "refused", now ())
                            outcome record "refused" ]
                superseded @ sent
        let malformedOutcome = if malformed > 0 then [ { Subject = "gate"; Ref = ""; Kind = "gate"; Result = "malformed"; Detail = string malformed } ] else []
        swept @ replayed @ malformedOutcome

    // ---- one apply run ----

    type RunInput =
        { Plan: V2Tend.Plan
          Snapshot: V2Tend.Snapshot
          // Item ids the reconciler's own custody holds as uncertain (from `sweepOwn`).
          OwnUncertain: Set<string>
          GateUncertain: Set<string>
          GateDeferred: Set<string> }

    // Apply the plan's Apply effects and Close closures. Effects are grouped by item (one mutation document
    // per item) in dependency order; an effect whose dependency was not written is deferred.
    let apply (context: Context) (input: RunInput) : Outcome list =
        let cards = input.Snapshot.Cards |> List.map (fun c -> c.ItemId, c) |> Map.ofList
        let eligible = archiveEligible input.Snapshot context.Settings.ArchiveRetention
        let blocked (itemId: string) =
            if input.OwnUncertain.Contains itemId then Some "uncertain-custody"
            elif input.GateUncertain.Contains itemId then Some "gate-custody-uncertain"
            elif input.GateDeferred.Contains itemId then Some "gate-deferred-pending"
            elif eligible.Contains itemId then Some "archive-eligible"
            else None
        // Item groups in dependency order: an item whose effects depend on another item's effects comes after it
        // (Kahn's order, ties in plan order), so a parent's fill and its child's move converge in one run. A cycle,
        // which the derivation never produces, leaves its items in plan order, where they defer.
        let groups =
            let grouped = input.Plan.Apply |> List.groupBy (fun e -> e.ItemId)
            let itemOf = input.Plan.Apply |> List.map (fun e -> e.Id, e.ItemId) |> Map.ofList
            let needs =
                grouped
                |> List.map (fun (itemId, effects) ->
                    itemId, effects |> List.collect (fun e -> e.DependsOn) |> List.choose itemOf.TryFind |> List.filter ((<>) itemId) |> Set.ofList)
                |> Map.ofList
            let rec order (placed: string list) (remaining: (string * V2Tend.Effect list) list) =
                match remaining |> List.tryFind (fun (itemId, _) -> Set.isSubset needs.[itemId] (Set.ofList placed)) with
                | Some((itemId, _) as next) -> next :: order (itemId :: placed) (remaining |> List.filter (fun (i, _) -> i <> itemId))
                | None -> remaining
            order [] grouped
        // An effect is sent only after every effect it depends on (outside its own item) was written.
        let succeeded = Collections.Generic.HashSet<string>()
        // Items whose field write came back uncertain in this run: their close is skipped in the same run too.
        let uncertainNow = Collections.Generic.HashSet<string>()
        let mutable items = 0
        let fieldOutcomes =
            [ for itemId, effects in groups do
                let card = cards.TryFind itemId
                let reference = effects.Head.Ref
                let own = effects |> List.map (fun e -> e.Id) |> Set.ofList
                let dependency = effects |> List.collect (fun e -> e.DependsOn) |> List.tryFind (fun d -> not (own.Contains d) && not (succeeded.Contains d))
                let refused =
                    match card, dependency with
                    | None, _ -> Some("refused", "card-missing")
                    | _, Some d -> Some("deferred", "dependency-not-written:" + d)
                    | Some c, None ->
                        match blocked itemId with
                        | Some reason -> Some("refused", reason)
                        | None when items >= context.Settings.MaxItemsPerRun -> Some("deferred", "budget")
                        | None -> None
                match refused with
                | Some(result, detail) -> yield { Subject = itemId; Ref = reference; Kind = "fields"; Result = result; Detail = detail }
                | None ->
                    items <- items + 1
                    let outcome, ok = applyItem context card.Value effects
                    if ok then for e in effects do succeeded.Add e.Id |> ignore
                    if outcome.Result = "uncertain" then uncertainNow.Add itemId |> ignore
                    yield outcome ]
        let closeOutcomes =
            [ for closure in input.Plan.Close do
                match blocked closure.ItemId with
                | Some reason -> yield { Subject = closure.ItemId; Ref = closure.Ref; Kind = "close"; Result = "refused"; Detail = reason }
                | None when uncertainNow.Contains closure.ItemId ->
                    yield { Subject = closure.ItemId; Ref = closure.Ref; Kind = "close"; Result = "refused"; Detail = "uncertain-custody" }
                | None when items >= context.Settings.MaxItemsPerRun -> yield { Subject = closure.ItemId; Ref = closure.Ref; Kind = "close"; Result = "deferred"; Detail = "budget" }
                | None ->
                    items <- items + 1
                    yield closeOne context closure ]
        fieldOutcomes @ closeOutcomes

    let encodeOutcomes (outcomes: Outcome list) =
        let root = JsonObject()
        root["schema"] <- "fsgg.board-tend-outcomes/1"
        let list = JsonArray()
        for o in outcomes do
            let x = JsonObject()
            x["subject"] <- o.Subject
            x["ref"] <- o.Ref
            x["kind"] <- o.Kind
            x["result"] <- o.Result
            x["detail"] <- o.Detail
            list.Add x
        root["outcomes"] <- list
        root.ToJsonString(JsonSerializerOptions(WriteIndented = true))

    // ---- the enable file (design §8) ----

    type Enable = { Apply: Set<string>; Propose: bool; MaxItemsPerRun: int }

    // Rules whose enablement needs the contract amendment (`fsgg:board-tend-contract/v1`, design §12 PR 6).
    let contractGated = set [ "S1"; "S3"; "S3b"; "S4"; "S9"; "S9r"; "O1"; "E1"; "C1"; "W1d"; "T1d" ]

    // Rules the enable file may name: every Apply rule and gate replay.
    let enableable = Set.add "GR" V2Tend.applyRules

    // Parse `board-tend.json`. Unknown keys and rules are refused; gated rules are dropped unless the
    // contract amendment is present, and each drop is reported.
    let parseEnable (json: string) (contractAmended: bool) : Result<Enable * string list, string> =
        try
            match JsonNode.Parse json with
            | :? JsonObject as root ->
                let unknown = root |> Seq.map (fun p -> p.Key) |> Seq.filter (fun k -> not ((set [ "apply"; "propose"; "maxItemsPerRun" ]).Contains k)) |> List.ofSeq
                if not unknown.IsEmpty then Error("unknown enable-file keys: " + String.Join(",", unknown))
                else
                    let apply =
                        match root["apply"] with
                        | :? JsonArray as a -> a |> Seq.map (fun x -> match x with :? JsonValue as v when v.GetValueKind() = JsonValueKind.String -> Some(v.GetValue<string>()) | _ -> None) |> List.ofSeq
                        | null -> []
                        | _ -> [ None ]
                    if apply |> List.exists Option.isNone then Error "apply must be a list of rule ids"
                    else
                        let rules = apply |> List.choose id
                        let unknownRules = rules |> List.filter (fun r -> not (enableable.Contains r))
                        if not unknownRules.IsEmpty then Error("unknown rules in the enable file: " + String.Join(",", unknownRules))
                        else
                            let dropped = if contractAmended then [] else rules |> List.filter contractGated.Contains
                            let propose = match root["propose"] with | :? JsonValue as v when v.GetValueKind() = JsonValueKind.True -> true | _ -> false
                            let maxItems =
                                match root["maxItemsPerRun"] with
                                | :? JsonValue as v when v.GetValueKind() = JsonValueKind.Number -> v.GetValue<int>()
                                | null -> 100
                                | _ -> -1
                            if maxItems < 0 || maxItems > 500 then Error "maxItemsPerRun must be between 0 and 500"
                            else
                                Ok({ Apply = rules |> List.filter (fun r -> not (List.contains r dropped)) |> Set.ofList; Propose = propose; MaxItemsPerRun = maxItems },
                                   dropped |> List.map (fun r -> r + " needs the contract amendment (fsgg:board-tend-contract/v1); not enabled"))
            | _ -> Error "the enable file must be a JSON object"
        with error -> Error("the enable file is not JSON: " + error.Message)
