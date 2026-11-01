/// Batched read-only reads for board-v2 tend (FS-GG/.github#5136 phase 2 PR 3; design §2).
module FS.GG.Coord.GitHub.Tests.V2TendReadsTests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport

let private observed = DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero)

let private inputs : V2TendReads.Inputs =
    { Owner = "FS-GG"; ProjectNumber = 3; RootLogins = set [ "EHotwagner" ]; ObservedAt = observed; Ledger = []; Enrolled = Map.empty }

// ---- a synthetic board served as GraphQL responses ----

type private Issue =
    { Number: int; State: string; Status: string option; WorkLevel: string option; Body: string; Parent: int option
      SubIssues: int list; SubTotal: int option; Author: string; Prs: JsonObject list; Comments: (string * string) list }

let private issue number =
    { Number = number; State = "OPEN"; Status = Some "In progress"; WorkLevel = Some "Delivery"; Body = ""; Parent = None
      SubIssues = []; SubTotal = None; Author = "agent"; Prs = []; Comments = [] }

let private pr (number: int) (state: string) (body: string) (mergedAt: string option) =
    let node = JsonObject()
    node["number"] <- number
    node["state"] <- state
    node["isDraft"] <- false
    node["updatedAt"] <- "2026-10-31T10:00:00Z"
    node["mergedAt"] <- (match mergedAt with Some m -> JsonValue.Create m :> JsonNode | None -> null)
    node["body"] <- body
    node["repository"] <- JsonObject(dict [ "nameWithOwner", JsonValue.Create "FS-GG/x" :> JsonNode ])
    node

let private ref' (number: int) = JsonObject(dict [ "number", JsonValue.Create number :> JsonNode; "state", JsonValue.Create "OPEN" :> JsonNode; "repository", JsonObject(dict [ "nameWithOwner", JsonValue.Create "FS-GG/x" :> JsonNode ]) :> JsonNode ])

let private itemNode (i: Issue) =
    let value (name: string) (key: string) (v: string option) =
        match v with Some v -> JsonObject(dict [ key, JsonValue.Create v :> JsonNode ]) :> JsonNode | None -> null
    let content = JsonObject()
    content["__typename"] <- "Issue"
    content["id"] <- sprintf "I_%d" i.Number
    content["number"] <- i.Number
    content["state"] <- i.State
    content["createdAt"] <- "2026-10-01T00:00:00Z"
    content["updatedAt"] <- "2026-10-31T00:00:00Z"
    content["closedAt"] <- (if i.State = "CLOSED" then JsonValue.Create "2026-10-31T00:00:00Z" :> JsonNode else null)
    content["body"] <- i.Body
    content["lastEditedAt"] <- null
    content["repository"] <- JsonObject(dict [ "nameWithOwner", JsonValue.Create "FS-GG/x" :> JsonNode ])
    content["author"] <- JsonObject(dict [ "login", JsonValue.Create i.Author :> JsonNode ])
    content["editor"] <- null
    content["parent"] <- (match i.Parent with Some p -> ref' p :> JsonNode | None -> null)
    let subs = JsonArray([| for s in i.SubIssues -> ref' s :> JsonNode |])
    content["subIssues"] <- JsonObject(dict [ "totalCount", JsonValue.Create (defaultArg i.SubTotal i.SubIssues.Length) :> JsonNode; "nodes", subs :> JsonNode ])
    content["blockedBy"] <- JsonObject(dict [ "totalCount", JsonValue.Create 0 :> JsonNode; "nodes", JsonArray() :> JsonNode ])
    let node = JsonObject()
    node["id"] <- sprintf "PVTI_%d" i.Number
    node["status"] <- (match i.Status with Some s -> JsonObject(dict [ "name", JsonValue.Create s :> JsonNode; "updatedAt", JsonValue.Create "2026-10-25T00:00:00Z" :> JsonNode ]) :> JsonNode | None -> null)
    node["workLevel"] <- value "workLevel" "name" i.WorkLevel
    node["track"] <- value "track" "name" (Some "Follow-up")
    node["roadmap"] <- null
    // An item whose content cannot be read (for example a private repository's issue) has no identity.
    node["content"] <- (if i.State = "UNREADABLE" then null else content :> JsonNode)
    node

type private Board(issues: Issue list, openPrs: JsonObject list, ?total: int, ?issueCount: int) =
    let mutable requests: Request list = []
    member _.Requests = List.rev requests
    interface IGitHubTransport with
        member _.Send request =
            requests <- request :: requests
            let document, variables = match request.Body with Query(d, v) -> d, v | _ -> failwith "query expected"
            let data = JsonObject()
            data["rateLimit"] <- JsonObject(dict [ "cost", JsonValue.Create 1 :> JsonNode ])
            if document.Contains "items(first: 50" then
                let cursor = variables |> List.tryPick (function ("cursor", VString c) -> Some(int c) | _ -> None) |> Option.defaultValue 0
                let page = issues |> List.skip cursor |> List.truncate 50
                let next = cursor + page.Length
                let items = JsonObject()
                items["totalCount"] <- defaultArg total issues.Length
                items["pageInfo"] <- JsonObject(dict [ "hasNextPage", JsonValue.Create (next < issues.Length) :> JsonNode; "endCursor", JsonValue.Create (string next) :> JsonNode ])
                items["nodes"] <- JsonArray([| for i in page -> itemNode i :> JsonNode |])
                data["organization"] <- JsonObject(dict [ "projectV2", JsonObject(dict [ "items", items :> JsonNode ]) :> JsonNode ])
            elif document.Contains "search(type: ISSUE" then
                let search = JsonObject()
                search["issueCount"] <- defaultArg issueCount openPrs.Length
                search["pageInfo"] <- JsonObject(dict [ "hasNextPage", JsonValue.Create false :> JsonNode; "endCursor", null ])
                search["nodes"] <- JsonArray([| for p in openPrs -> p.DeepClone() |])
                data["search"] <- search
            elif document.Contains "timelineItems" then
                for name, value in variables do
                    match value with
                    | VId nodeId ->
                        let number = int (nodeId.Substring 2)
                        let i = issues |> List.find (fun i -> i.Number = number)
                        let timeline = JsonObject()
                        timeline["totalCount"] <- i.Prs.Length
                        timeline["nodes"] <- JsonArray([| for p in i.Prs -> JsonObject(dict [ "source", p.DeepClone() ]) :> JsonNode |])
                        let comments = JsonObject()
                        comments["totalCount"] <- i.Comments.Length
                        comments["nodes"] <- JsonArray([| for author, body in i.Comments -> JsonObject(dict [ "author", JsonObject(dict [ "login", JsonValue.Create author :> JsonNode ]) :> JsonNode; "body", JsonValue.Create body :> JsonNode; "createdAt", JsonValue.Create "2026-10-31T00:00:00Z" :> JsonNode ]) :> JsonNode |])
                        data[name] <- JsonObject(dict [ "timelineItems", timeline :> JsonNode; "comments", comments :> JsonNode ])
                    | _ -> ()
            else failwith "unexpected query"
            Ok { Status = 200; ETag = None; NextLink = None; Headers = Map.empty; Body = JsonObject(dict [ "data", data :> JsonNode ]).ToJsonString() }
        member _.SendMutation _ = failwith "tend must never send a mutation"
        member _.RetryMutation _ = failwith "tend must never retry"

let private read (board: Board) =
    match V2TendReads.readSnapshot (V2TendReads.readOnly board) inputs with
    | Ok(snapshot, cost) -> snapshot, cost
    | Error e -> failwith e

let private card (snapshot: V2Tend.Snapshot) (number: int) = snapshot.Cards |> List.find (fun c -> c.Ref = sprintf "FS-GG/x#%d" number)

// ---------------------------------------------------------------------------------------------------

[<Fact>]
let ``body references follow the board-currency normalisation`` () =
    let body = "Refs #1, #2 and FS-GG/y#3\r\nCloses FS-GG/x#4\n`Refs #5`\n```\nRefs #6\n```\nfixes: #7"
    Assert.Equal<string list>([ "fs-gg/x#1"; "fs-gg/x#2"; "fs-gg/x#4"; "fs-gg/x#7"; "fs-gg/y#3" ], V2TendReads.bodyRefs "FS-GG/x" body)
    Assert.True(V2TendReads.continues "text\n<!-- fsgg:delivery-continues/v1 -->\n")
    Assert.False(V2TendReads.continues "`<!-- fsgg:delivery-continues/v1 -->`")

[<Fact>]
let ``body sections read the intended-fields line, Blocked on and Acceptance evidence`` () =
    let body = "Work level: Delivery · Track: Follow-up\n\n## Owner\nRoot\n\n## Blocked on\nNothing external.\n\n## Acceptance evidence\nNone yet.\n"
    Assert.Equal((Some "Work level: Delivery · Track: Follow-up", Some "Nothing external.", Some "None yet."), V2TendReads.bodySections body)
    Assert.Equal((None, None, None), V2TendReads.bodySections "")

[<Fact>]
let ``the linkage conformance corpus agrees with the board-currency linkage_of`` () =
    // The same corpus is run against scripts/board-currency-rules.py linkage_of by tests/board-currency.
    let corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "tend-linkage-corpus.json")))
    let mutable cases = 0
    let text (e: JsonElement) (name: string) = match e.TryGetProperty name with | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString()) | _ -> None
    let flag (e: JsonElement) (name: string) = match e.TryGetProperty name with | true, v -> v.ValueKind = JsonValueKind.True | _ -> false
    for case in corpus.RootElement.GetProperty("cases").EnumerateArray() do
        cases <- cases + 1
        let cards =
            [ for prop in case.GetProperty("cards").EnumerateObject() do
                let c = prop.Value
                // A pull-request card is absent from the reader's linkage map: it is never a Delivery.
                if text c "type" <> Some "PullRequest" then
                    let level = match text c "workLevel" with Some "Delivery" -> Some V2Tend.Delivery | Some "Outcome" -> Some V2Tend.Outcome | _ -> None
                    let status = match text c "status" with Some "Done" -> Some V2Tend.Done | _ -> Some V2Tend.InProgress
                    yield V2TendReads.canonicalRef prop.Name,
                          ({ WorkLevel = level; Open = (text c "state" = Some "OPEN"); Status = status; Parent = text c "parent"; Unreadable = flag c "unreadable" } : V2TendReads.LinkCard) ]
            |> Map.ofList
        let pr = case.GetProperty "pr"
        let actual =
            V2TendReads.linkage cards (flag case "anonymousUnreadable") (pr.GetProperty("repo").GetString()) (pr.GetProperty("number").GetInt32())
                (pr.GetProperty("state").GetString() = "OPEN") (pr.GetProperty("body").GetString())
        let expected = case.GetProperty("expect").GetString()
        let shown =
            match actual with
            | V2TendReads.Linked delivery -> delivery
            | V2TendReads.Unlinked reason | V2TendReads.Unknown reason -> reason
        Assert.True((shown = V2TendReads.canonicalRef expected || shown = expected), sprintf "%s: expected %s, got %s" (case.GetProperty("name").GetString()) expected shown)
        // Unknown carries exactly the U reasons.
        match actual with
        | V2TendReads.Unknown reason -> Assert.StartsWith("r1-", reason)
        | _ -> ()
    Assert.True(cases >= 16)

[<Fact>]
let ``references compare case-insensitively on owner and repository and numerically on the number`` () =
    Assert.Equal("fs-gg/x#1", V2TendReads.canonicalRef "FS-GG/X#01")
    Assert.Equal("fs-gg/x#0", V2TendReads.canonicalRef "FS-GG/x#000")
    Assert.Equal<string list>([ "fs-gg/x#1" ], V2TendReads.bodyRefs "FS-GG/x" "Refs #1 and FS-GG/X#001")
    // A lone CR ends a code span, exactly as a line feed does.
    Assert.Equal<string list>([ "fs-gg/x#1" ], V2TendReads.bodyRefs "FS-GG/x" "`x\rRefs #1`")

[<Fact>]
let ``the read-only transport admits only documents of queries, judged by parsing the operations`` () =
    let admitted document = V2TendReads.operationKinds document |> Option.exists (fun kinds -> kinds |> List.contains "query" && kinds |> List.forall (fun k -> k = "query" || k = "fragment"))
    Assert.Equal(Some [ "query" ], V2TendReads.operationKinds "query($a: Int) { viewer { login } }")
    Assert.Equal(Some [ "query" ], V2TendReads.operationKinds "  { viewer { login } }")
    Assert.Equal(Some [ "query"; "fragment" ], V2TendReads.operationKinds "query { ...F }\nfragment F on User { login }")
    Assert.True(admitted "# mutation { closeIssue }\nquery { search(query: \"mutation {\") { issueCount } }")
    Assert.True(admitted "query { a(b: \"\"\"mutation { x }\\\"\"\" still a string\"\"\") }")
    Assert.False(admitted "query { viewer { login } } mutation { closeIssue(input: {}) { clientMutationId } }")
    Assert.False(admitted "# query\nmutation { closeIssue(input: {}) { clientMutationId } }")
    Assert.False(admitted "subscription { x }")
    Assert.False(admitted "query { viewer { login }")
    Assert.False(admitted "query { a(b: \"unterminated) }")
    Assert.False(admitted "query { a } }")
    Assert.False(admitted "")
    Assert.False(admitted "fragment F on User { login }")
    Assert.False(admitted "nonsense { a }")

[<Fact>]
let ``a small board is read in three requests with its cost, facts and linkage`` () =
    let outcome = { (issue 10) with WorkLevel = Some "Outcome"; SubIssues = [ 11; 12 ]; Status = Some "In progress" }
    let merged = pr 101 "MERGED" "Refs #11\n<!-- fsgg:delivery-continues/v1 -->\n`<!-- fsgg:delivery-completes/v1 -->`" (Some "2026-10-28T10:00:00Z")
    let delivery =
        { (issue 11) with
            Parent = Some 10; Status = Some "In review"; Prs = [ merged ]
            Body = "Work level: Delivery · Intended Status: Ready\n\n## Blocked on\nNothing\n"; Author = "EHotwagner"
            Comments = [ "EHotwagner", "<!-- fsgg:tend-closed/v1 -->" ] }
    let loose = { (issue 12) with Parent = Some 10; Status = Some "Blocked"; Comments = [ "EHotwagner", "<!-- fsgg:blocked-on/v1 FS-GG/x#10 -->" ] }
    let closed = { (issue 13) with State = "CLOSED"; Status = Some "Done" }
    let openPr = pr 102 "OPEN" "Refs #12 and #11" None
    let board = Board([ outcome; delivery; loose; closed ], [ openPr ])
    let snapshot, cost = read board
    Assert.Equal(({ Requests = 3; GraphQlPoints = 3; RestCalls = 0 } : V2TendReads.Cost), cost)
    Assert.Equal(4, snapshot.Cards.Length)
    let d = card snapshot 11
    Assert.Equal((Some V2Tend.Delivery, Some V2Tend.InReview, Some "FS-GG/x#10"), (d.WorkLevel, d.Status, d.Parent))
    Assert.True(d.TimelineComplete)
    Assert.True(d.TendClosedBefore)
    Assert.Equal(Some "Nothing", d.BlockedOn)
    Assert.Equal(Some true, d.Intended |> Option.map (fun i -> i.RootAuthored))
    let strict = d.Prs |> List.find (fun p -> p.Id = "FS-GG/x#101")
    // A marker quoted in code does not count.
    Assert.Equal((V2Tend.Strict, V2Tend.PrMerged, true, false), (strict.Linkage, strict.State, strict.Continues, strict.Completes))
    // The open PR names two Deliveries: loose for both.
    Assert.Equal(V2Tend.Loose "multiple-deliveries", (d.Prs |> List.find (fun p -> p.Id = "FS-GG/x#102")).Linkage)
    let blocked = card snapshot 12
    Assert.Equal(Some [ ({ Ref = "FS-GG/x#10"; Open = true } : V2Tend.Blocker) ], blocked.MarkerBlockers)
    // The closed card is not given a timeline read: its absence evidence is incomplete, never "no PR".
    Assert.False((card snapshot 13).TimelineComplete)
    Assert.True(board.Requests |> List.forall (fun r -> r.Method = "POST" && r.Path = "graphql"))

[<Fact>]
let ``a truncated sub-issue list is incomplete, not empty`` () =
    let outcome = { (issue 10) with WorkLevel = Some "Outcome"; SubIssues = [ 11 ]; SubTotal = Some 80 }
    let snapshot, _ = read (Board([ outcome; { (issue 11) with Parent = Some 10 } ], []))
    Assert.False((card snapshot 10).SubIssuesComplete)

[<Fact>]
let ``a board over 500 items is refused on its first page, and one whose pages disagree with totalCount is refused`` () =
    let big = Board([ for n in 1 .. 501 -> issue n ], [])
    Assert.True(V2TendReads.readSnapshot big inputs |> Result.isError)
    Assert.Equal(1, big.Requests.Length) // refused from totalCount, never paged up to the limit (#5119)
    Assert.True(V2TendReads.readSnapshot (Board([ issue 1 ], [], total = 2)) inputs |> Result.isError)

[<Fact>]
let ``read cost stays within the design ceiling at 222 and 500 items`` () =
    for rows in [ 222; 500 ] do
        let issues = [ for n in 1 .. rows -> issue n ]
        let _, cost = read (Board(issues, []))
        let expected = (rows + 49) / 50 + 1 + (rows + 24) / 25
        Assert.Equal(expected, cost.Requests)
        Assert.True(cost.Requests <= V2TendReads.readCeiling rows, sprintf "%d requests at %d rows" cost.Requests rows)
    Assert.Equal(22, V2TendReads.readCeiling 222)
    Assert.Equal(38, V2TendReads.readCeiling 500)

[<Fact>]
let ``the read-only transport refuses mutations and anything that is not a GraphQL query`` () =
    let inner = Board([], [])
    let guarded = V2TendReads.readOnly inner
    let mutation : Request = { Method = "POST"; Path = "graphql"; Query = []; Body = Query("mutation { closeIssue(input: {issueId: \"I_1\"}) { clientMutationId } }", []); Budget = GraphQl; IfNoneMatch = None; Subject = "x" }
    Assert.True(guarded.Send mutation |> Result.isError)
    // A query followed by a mutation in one document is refused: the operations are parsed, not prefixed.
    Assert.True(guarded.Send { mutation with Body = Query("query { viewer { login } } mutation { closeIssue(input: {issueId: \"I_1\"}) { clientMutationId } }", []) } |> Result.isError)
    Assert.True(guarded.Send { mutation with Method = "GET"; Path = "repos/FS-GG/x/issues/1"; Body = NoBody } |> Result.isError)
    Assert.True(guarded.SendMutation { EffectId = "e"; Request = mutation } |> Result.isError)
    Assert.Empty(inner.Requests)

[<Fact>]
let ``an open-PR search whose node count differs from issueCount is refused`` () =
    let openPr = pr 102 "OPEN" "Refs #1" None
    Assert.True(V2TendReads.readSnapshot (Board([ issue 1 ], [ openPr ], issueCount = 2)) inputs |> Result.isError)
    Assert.True(V2TendReads.readSnapshot (Board([ issue 1 ], [ openPr ])) inputs |> Result.isOk)

[<Fact>]
let ``an unreadable card makes an off-board reference Unknown, never a strict link`` () =
    let outcome = { (issue 10) with WorkLevel = Some "Outcome"; SubIssues = [ 11 ] }
    let merged = pr 101 "MERGED" "Refs #11 and #77" (Some "2026-10-28T10:00:00Z")
    let delivery = { (issue 11) with Parent = Some 10; Status = Some "In review"; Prs = [ merged ] }
    let unreadable = { (issue 12) with State = "UNREADABLE" }
    let board = Board([ outcome; delivery; unreadable ], [])
    let snapshot, _ = read board
    let linked = (card snapshot 11).Prs |> List.find (fun p -> p.Id = "FS-GG/x#101")
    Assert.Equal(V2Tend.Loose "r1-reference-unreadable", linked.Linkage)
