namespace FS.GG.Coord.GitHub

open System
open System.Text.Json
open System.Text.RegularExpressions
open Errors
open Transport

module V2TendReads =
    type Cost = { Requests: int; GraphQlPoints: int; RestCalls: int }

    type Inputs =
        { Owner: string
          ProjectNumber: int
          RootLogins: Set<string>
          ObservedAt: DateTimeOffset
          Ledger: V2Tend.Deferral list
          Enrolled: Map<string, string option> }

    let readCeiling (rows: int) = 8 + (rows + 49) / 50 + (rows + 24) / 25

    let private maximumItems = 500

    let readOnly (transport: IGitHubTransport) : IGitHubTransport =
        { new IGitHubTransport with
            member _.Send request =
                match request.Method, request.Path, request.Body with
                | "POST", "graphql", Query(document, _) when document.TrimStart().StartsWith("query", StringComparison.Ordinal) ->
                    transport.Send request
                | _ -> Error(Malformed(request.Subject, "board-v2 tend is read-only: only GraphQL queries are sent"))
            member _.SendMutation mutation = Error(Malformed(mutation.Request.Subject, "board-v2 tend is read-only"))
            member _.RetryMutation effectId = Error(Malformed(effectId, "board-v2 tend is read-only")) }

    // ---- body parsing (board-currency normalisation) ----

    let private fence = Regex(@"```.*?```|~~~.*?~~~", RegexOptions.Singleline)
    let private inline' = Regex(@"`[^`\n]*`")
    let private refList = @"((?:[\w.-]+/[\w.-]+)?#\d+(?:\s*(?:,|and|&)\s*(?:[\w.-]+/[\w.-]+)?#\d+)*)"
    let private refsLine = Regex(@"\bRefs\s+" + refList, RegexOptions.IgnoreCase)
    let private closing = Regex(@"\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\s*:?\s+" + refList, RegexOptions.IgnoreCase)
    let private oneRef = Regex(@"(?:([\w.-]+/[\w.-]+))?#(\d+)")
    let private continuesMarker = "<!-- fsgg:delivery-continues/v1 -->"
    let private tendClosedMarker = "<!-- fsgg:tend-closed/v1 -->"
    let private blockedOnMarker = Regex(@"<!--\s*fsgg:blocked-on/v1\b([^>]*)-->")
    let private fullRef = Regex(@"([\w.-]+/[\w.-]+)#(\d+)")

    let private visible (body: string) =
        let normalised = (if isNull body then "" else body).Replace("\r\n", "\n").Replace("\r", "\n")
        inline'.Replace(fence.Replace(normalised, " "), " ")

    let bodyRefs (repository: string) (body: string) =
        let text = visible body
        [ for pattern in [ refsLine; closing ] do
            for m in pattern.Matches text do
                for r in oneRef.Matches m.Groups.[1].Value do
                    let repo = if r.Groups.[1].Success then r.Groups.[1].Value else repository
                    yield (repo + "#" + r.Groups.[2].Value).ToLowerInvariant() ]
        |> List.distinct
        |> List.sort

    let continues (body: string) = (visible body).Contains continuesMarker

    let linkage (cards: Map<string, V2Tend.WorkLevel option * bool * V2Tend.Status option * string option>) (prRepository: string) (prNumber: int) (prOpen: bool) (body: string) =
        let own = (prRepository + "#" + string prNumber).ToLowerInvariant()
        let refs = bodyRefs prRepository body |> List.filter (fun r -> r <> own)
        let deliveries = refs |> List.filter (fun r -> match cards |> Map.tryFind r with Some(Some V2Tend.Delivery, _, _, _) -> true | _ -> false)
        match deliveries with
        | [] -> Error "no-delivery"
        | [ delivery ] ->
            let _, isOpen, status, parent = cards.[delivery]
            if prOpen && not isOpen then Error "delivery-closed"
            elif prOpen && status = Some V2Tend.Done then Error "delivery-done"
            else
                match parent |> Option.map (fun p -> p.ToLowerInvariant()) |> Option.bind (fun p -> cards |> Map.tryFind p) with
                | Some(Some V2Tend.Outcome, _, _, _) -> Ok delivery
                | _ -> Error "no-parent-outcome"
        | _ -> Error "multiple-deliveries"

    let bodySections (body: string) =
        let lines = (if isNull body then "" else body).Replace("\r\n", "\n").Split('\n') |> Array.map (fun l -> l.TrimEnd())
        let intended = lines |> Array.tryFind (fun l -> l.Trim() <> "") |> Option.map (fun l -> l.Trim())
        let section (name: string) =
            match lines |> Array.tryFindIndex (fun l -> l.Trim().Equals("## " + name, StringComparison.OrdinalIgnoreCase)) with
            | None -> None
            | Some index ->
                let content =
                    lines
                    |> Array.skip (index + 1)
                    |> Array.takeWhile (fun l -> not (l.StartsWith("## ", StringComparison.Ordinal)))
                    |> Array.filter (fun l -> l.Trim() <> "")
                if content.Length = 0 then None else Some(String.Join(" ", content |> Array.map (fun l -> l.Trim())))
        intended, section "Blocked on", section "Acceptance evidence"

    // ---- requests ----

    let private request (document: string) (variables: (string * Var) list) (subject: string) : Request =
        { Method = "POST"; Path = "graphql"; Query = []; Body = Query(document, variables); Budget = GraphQl; IfNoneMatch = None; Subject = subject }

    let private itemsDocument = """query($owner: String!, $number: Int!, $cursor: String) {
  rateLimit { cost }
  organization(login: $owner) { projectV2(number: $number) {
    items(first: 50, after: $cursor) {
      totalCount
      pageInfo { hasNextPage endCursor }
      nodes {
        id
        status: fieldValueByName(name: "Status") { ... on ProjectV2ItemFieldSingleSelectValue { name updatedAt } }
        workLevel: fieldValueByName(name: "Work level") { ... on ProjectV2ItemFieldSingleSelectValue { name } }
        track: fieldValueByName(name: "Track") { ... on ProjectV2ItemFieldSingleSelectValue { name } }
        roadmap: fieldValueByName(name: "Roadmap") { ... on ProjectV2ItemFieldTextValue { text } }
        content {
          __typename
          ... on Issue {
            id number state createdAt updatedAt closedAt body lastEditedAt
            repository { nameWithOwner }
            author { login } editor { login }
            parent { number repository { nameWithOwner } }
            subIssues(first: 50) { totalCount nodes { number state repository { nameWithOwner } } }
            blockedBy(first: 50) { totalCount nodes { number state repository { nameWithOwner } } }
          }
          ... on PullRequest { number repository { nameWithOwner } }
        }
      }
    }
  } }
}"""

    let private searchDocument = """query($q: String!, $cursor: String) {
  rateLimit { cost }
  search(type: ISSUE, query: $q, first: 100, after: $cursor) {
    issueCount
    pageInfo { hasNextPage endCursor }
    nodes { ... on PullRequest { number state isDraft updatedAt mergedAt body repository { nameWithOwner } } }
  }
}"""

    let private commentWindow = 50

    let private timelineFragment = """... on Issue {
      timelineItems(itemTypes: [CROSS_REFERENCED_EVENT], last: 25) {
        totalCount
        nodes { ... on CrossReferencedEvent { source { ... on PullRequest { number state isDraft updatedAt mergedAt body repository { nameWithOwner } } } } }
      }
      comments(last: 50) { totalCount nodes { author { login } body createdAt } }
    }"""

    type private Pr =
        { Repo: string; Number: int; State: V2Tend.PrState; Draft: bool; UpdatedAt: DateTimeOffset; MergedAt: DateTimeOffset option; Body: string }

    let private str (e: JsonElement) (name: string) =
        match e.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
        | _ -> None
    let private time e name = str e name |> Option.map DateTimeOffset.Parse
    let private obj (e: JsonElement) (name: string) =
        match e.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.Object -> Some v
        | _ -> None

    let private decodePr (node: JsonElement) =
        match obj node "repository", node.TryGetProperty "number" with
        | Some repo, (true, number) ->
            let state =
                match str node "state" with
                | Some "MERGED" -> V2Tend.PrMerged
                | Some "CLOSED" -> V2Tend.PrClosed
                | _ -> V2Tend.PrOpen
            Some { Repo = (str repo "nameWithOwner").Value; Number = number.GetInt32(); State = state
                   Draft = (match node.TryGetProperty "isDraft" with | true, v -> v.ValueKind = JsonValueKind.True | _ -> false)
                   UpdatedAt = (time node "updatedAt").Value; MergedAt = time node "mergedAt"; Body = defaultArg (str node "body") "" }
        | _ -> None

    let private emptyCard (itemId: string) (observed: DateTimeOffset) : V2Tend.Card =
        { ItemId = itemId; Ref = ""; Kind = V2Tend.Unreadable; Open = true; CreatedAt = observed; UpdatedAt = observed; ClosedAt = None
          Status = None; StatusUpdatedAt = None; WorkLevel = None; Track = None; Roadmap = None; Parent = None; SubIssues = []
          SubIssuesComplete = true; Blockers = []; BlockersComplete = true; MarkerBlockers = Some []; Prs = []; TimelineComplete = false
          Intended = None; BlockedOn = None; AcceptanceEvidence = None; Enrolled = false; EnrolledRoadmap = None; TendClosedBefore = false }

    let private statusOf =
        function
        | "Backlog" -> Some V2Tend.Backlog | "Ready" -> Some V2Tend.Ready | "In progress" -> Some V2Tend.InProgress
        | "In review" -> Some V2Tend.InReview | "Blocked" -> Some V2Tend.Blocked | "Done" -> Some V2Tend.Done | _ -> None

    let readSnapshot (transport: IGitHubTransport) (inputs: Inputs) : Result<V2Tend.Snapshot * Cost, string> =
        let mutable requests = 0
        let mutable points = 0
        // One counted GraphQL request; the decoder runs only after GraphQl.decode proved the envelope complete.
        let sendIo (req: Request) (decode: JsonElement -> IoResult<'a>) : IoResult<'a> =
            requests <- requests + 1
            match transport.Send req with
            | Error error -> Error error
            | Ok response ->
                GraphQl.decode req.Subject response.Body (fun data ->
                    match obj data "rateLimit" with
                    | Some rate -> points <- points + rate.GetProperty("cost").GetInt32()
                    | None -> ()
                    decode data)
        let send (req: Request) (decode: JsonElement -> 'a) : Result<'a, string> =
            sendIo req (fun data -> Ok(decode data)) |> Result.mapError (fun error -> req.Subject + ": " + Errors.explain error)
        let orFail (subject: string) (result: IoResult<'a>) =
            match result with
            | Ok value -> value
            | Error error -> failwith (subject + ": " + Errors.explain error)
        try
            // (A) board items, paged through the typed boundary: a board reporting more than 500 items is refused
            // on its first page (#5119), and a final count that disagrees with totalCount is refused.
            let itemsSubject = "tend board items"
            let items =
                GraphQl.drain itemsSubject "the board items" { MaxPages = (maximumItems + 49) / 50; MaxItems = maximumItems } (fun cursor ->
                    let variables =
                        [ "owner", VString inputs.Owner; "number", VNumber(double inputs.ProjectNumber) ]
                        @ (match cursor with Some c -> [ "cursor", VString c ] | None -> [])
                    sendIo (request itemsDocument variables itemsSubject) (fun data ->
                        GraphQl.page itemsSubject "the board items" (fun (node: JsonElement) -> defaultArg (str node "id") "")
                            (fun node -> Ok(node.Clone()))
                            (data.GetProperty("organization").GetProperty("projectV2").GetProperty("items"))))
                |> orFail itemsSubject
            // (B) open pull requests in the organization (the search API reads at most 1000 results)
            let searchSubject = "tend open pull requests"
            let openPrs =
                GraphQl.drain searchSubject "the open pull requests" { MaxPages = 10; MaxItems = 1000 } (fun cursor ->
                    let variables = [ "q", VString(sprintf "org:%s is:pr is:open" inputs.Owner) ] @ (match cursor with Some c -> [ "cursor", VString c ] | None -> [])
                    sendIo (request searchDocument variables searchSubject) (fun data ->
                        let search = data.GetProperty "search"
                        if search.GetProperty("issueCount").GetInt32() > 1000 then
                            Error(Malformed(searchSubject, "more than 1000 open pull requests: the search cannot be read completely"))
                        else
                            GraphQl.page searchSubject "the open pull requests" (fun (pr: Pr) -> sprintf "%s#%d" pr.Repo pr.Number)
                                (fun node ->
                                    match decodePr node with
                                    | Some pr -> Ok pr
                                    | None -> Error(Malformed(searchSubject, "a pull request node omitted its repository or number")))
                                search))
                |> orFail searchSubject
            // Decode cards (without PRs yet).
            let cardOf (node: JsonElement) =
                let itemId = node.GetProperty("id").GetString()
                let single name = obj node name |> Option.bind (fun v -> str v "name")
                let status = single "status"
                let content = obj node "content"
                let kind = content |> Option.bind (fun c -> str c "__typename")
                match kind, content with
                | Some "Issue", Some issue ->
                    let repo = (obj issue "repository" |> Option.bind (fun r -> str r "nameWithOwner")).Value
                    let number = issue.GetProperty("number").GetInt32()
                    let reference = repo + "#" + string number
                    let connection name =
                        let c = issue.GetProperty(name: string)
                        let nodes = [ for n in c.GetProperty("nodes").EnumerateArray() -> (obj n "repository" |> Option.bind (fun r -> str r "nameWithOwner")).Value + "#" + string (n.GetProperty("number").GetInt32()), str n "state" = Some "OPEN" ]
                        nodes, c.GetProperty("totalCount").GetInt32() = nodes.Length
                    let subIssues, subComplete = connection "subIssues"
                    let blockers, blockersComplete = connection "blockedBy"
                    let author = obj issue "author" |> Option.bind (fun a -> str a "login")
                    let editor = obj issue "editor" |> Option.bind (fun a -> str a "login")
                    let edited = (time issue "lastEditedAt").IsSome
                    let rootAuthored = (match author with Some a -> inputs.RootLogins.Contains a | None -> false) && (not edited || (match editor with Some e -> inputs.RootLogins.Contains e | None -> false))
                    let body = defaultArg (str issue "body") ""
                    let line, blockedOn, evidence = bodySections body
                    let parent = obj issue "parent" |> Option.map (fun p -> (obj p "repository" |> Option.bind (fun r -> str r "nameWithOwner")).Value + "#" + string (p.GetProperty("number").GetInt32()))
                    let enrolled = inputs.Enrolled |> Map.tryFind (reference.ToLowerInvariant())
                    Some(issue.GetProperty("id").GetString(),
                         { emptyCard itemId inputs.ObservedAt with
                             Ref = reference; Kind = V2Tend.Issue; Open = (str issue "state" = Some "OPEN")
                             CreatedAt = (time issue "createdAt").Value; UpdatedAt = (time issue "updatedAt").Value; ClosedAt = time issue "closedAt"
                             Status = status |> Option.bind statusOf
                             StatusUpdatedAt = obj node "status" |> Option.bind (fun v -> time v "updatedAt")
                             WorkLevel = single "workLevel" |> Option.bind (fun s -> match s with "Outcome" -> Some V2Tend.Outcome | "Delivery" -> Some V2Tend.Delivery | _ -> None)
                             Track = single "track" |> Option.bind (fun s -> match s with "Active delivery" -> Some V2Tend.ActiveDelivery | "Follow-up" -> Some V2Tend.FollowUp | _ -> None)
                             Roadmap = obj node "roadmap" |> Option.bind (fun v -> str v "text")
                             Parent = parent; SubIssues = subIssues; SubIssuesComplete = subComplete
                             Blockers = blockers |> List.map (fun (r, o) -> ({ Ref = r; Open = o } : V2Tend.Blocker)); BlockersComplete = blockersComplete
                             Intended = line |> Option.bind (fun l -> V2Tend.parseIntendedLine l rootAuthored)
                             BlockedOn = blockedOn; AcceptanceEvidence = evidence
                             Enrolled = enrolled.IsSome; EnrolledRoadmap = enrolled |> Option.flatten })
                | Some "PullRequest", Some pr ->
                    let repo = (obj pr "repository" |> Option.bind (fun r -> str r "nameWithOwner")).Value
                    Some("", { emptyCard itemId inputs.ObservedAt with Ref = repo + "#" + string (pr.GetProperty("number").GetInt32()); Kind = V2Tend.PullRequestCard })
                | other, _ ->
                    Some("", { emptyCard itemId inputs.ObservedAt with Kind = (match other with Some "DraftIssue" -> V2Tend.DraftCard | _ -> V2Tend.Unreadable) })
            let decoded = items |> Seq.choose cardOf |> List.ofSeq
            let cards = decoded |> List.map snd
            let lookup =
                cards
                |> List.filter (fun c -> c.Kind = V2Tend.Issue)
                |> List.map (fun c -> c.Ref.ToLowerInvariant(), (c.WorkLevel, c.Open, c.Status, c.Parent))
                |> Map.ofList
            // (C) timeline and comments for every open issue card, 25 aliased per request.
            let targets = decoded |> List.filter (fun (nodeId, c) -> nodeId <> "" && c.Open && c.Kind = V2Tend.Issue)
            let timelines = Collections.Generic.Dictionary<string, Pr list * bool * V2Tend.Blocker list option * bool>()
            for chunk in targets |> List.chunkBySize 25 do
                let aliases = chunk |> List.mapi (fun i (nodeId, c) -> sprintf "i%d" i, nodeId, c)
                let document =
                    "query(" + String.Join(", ", aliases |> List.map (fun (a, _, _) -> "$" + a + ": ID!")) + ") {\n  rateLimit { cost }\n"
                    + String.Join("\n", aliases |> List.map (fun (a, _, _) -> sprintf "  %s: node(id: $%s) { %s }" a a timelineFragment)) + "\n}"
                let variables = aliases |> List.map (fun (a, nodeId, _) -> a, VId nodeId)
                match send (request document variables "tend timelines") (fun data ->
                        [ for (a, _, c) in aliases ->
                            let node = data.GetProperty a
                            let timeline = node.GetProperty "timelineItems"
                            let prs = [ for n in timeline.GetProperty("nodes").EnumerateArray() do
                                          match obj n "source" |> Option.bind decodePr with Some pr -> yield pr | None -> () ]
                            let comments = node.GetProperty "comments"
                            let commentsComplete = comments.GetProperty("totalCount").GetInt32() <= commentWindow
                            // Truncated comments hide a possible tend-closed marker: the issue's evidence is incomplete.
                            let complete = timeline.GetProperty("totalCount").GetInt32() <= 25 && commentsComplete
                            let tendClosed =
                                [ for cm in comments.GetProperty("nodes").EnumerateArray() do
                                    match obj cm "author" |> Option.bind (fun x -> str x "login") with
                                    | Some login when inputs.RootLogins.Contains login && (visible (defaultArg (str cm "body") "")).Contains tendClosedMarker -> yield ()
                                    | _ -> () ]
                                |> List.isEmpty |> not
                            let markers =
                                [ for cm in comments.GetProperty("nodes").EnumerateArray() do
                                    let author = obj cm "author" |> Option.bind (fun x -> str x "login")
                                    match author with
                                    | Some login when inputs.RootLogins.Contains login ->
                                        let m = blockedOnMarker.Match(visible (defaultArg (str cm "body") ""))
                                        if m.Success then yield (time cm "createdAt").Value, m.Groups.[1].Value
                                    | _ -> () ]
                            let marker =
                                match markers |> List.sortBy fst |> List.tryLast with
                                | Some(_, refs) ->
                                    let named = [ for r in fullRef.Matches refs -> (r.Groups.[1].Value + "#" + r.Groups.[2].Value) ]
                                    let states = named |> List.map (fun r -> r, lookup |> Map.tryFind (r.ToLowerInvariant()))
                                    if states |> List.exists (snd >> Option.isNone) then None // a blocker off the board: its state is unknown
                                    else Some(states |> List.map (fun (r, s) -> let _, o, _, _ = s.Value in ({ Ref = r; Open = o } : V2Tend.Blocker)))
                                | None -> if commentsComplete then Some [] else None
                            c.ItemId, (prs, complete, marker, tendClosed) ]) with
                | Error e -> failwith e
                | Ok rows -> for item, row in rows do timelines.[item] <- row
            // Link PRs to cards with the board-currency linkage.
            let allPrs (c: V2Tend.Card) =
                let fromTimeline = match timelines.TryGetValue c.ItemId with | true, (prs, _, _, _) -> prs | _ -> []
                let relevant = openPrs |> Seq.filter (fun pr -> bodyRefs pr.Repo pr.Body |> List.contains (c.Ref.ToLowerInvariant())) |> List.ofSeq
                fromTimeline @ relevant
                |> List.distinctBy (fun pr -> pr.Repo.ToLowerInvariant(), pr.Number)
                |> List.filter (fun pr -> bodyRefs pr.Repo pr.Body |> List.contains (c.Ref.ToLowerInvariant()))
                |> List.map (fun pr ->
                    let verdict = linkage lookup pr.Repo pr.Number (pr.State = V2Tend.PrOpen) pr.Body
                    ({ Id = pr.Repo + "#" + string pr.Number; State = pr.State; Draft = pr.Draft
                       Linkage = (match verdict with Ok d when d = c.Ref.ToLowerInvariant() -> V2Tend.Strict | Ok _ -> V2Tend.Loose "names-another-delivery" | Error reason -> V2Tend.Loose reason)
                       UpdatedAt = pr.UpdatedAt; MergedAt = pr.MergedAt; Continues = continues pr.Body } : V2Tend.LinkedPr))
            let linked =
                cards
                |> List.map (fun c ->
                    if c.Kind <> V2Tend.Issue then c
                    else
                        match timelines.TryGetValue c.ItemId with
                        | true, (_, complete, marker, tendClosed) -> { c with Prs = allPrs c; TimelineComplete = complete; MarkerBlockers = marker; TendClosedBefore = tendClosed }
                        | _ -> { c with Prs = allPrs c; TimelineComplete = false })
            Ok(({ ObservedAt = inputs.ObservedAt; Cards = linked; Ledger = inputs.Ledger } : V2Tend.Snapshot), { Requests = requests; GraphQlPoints = points; RestCalls = 0 })
        with error -> Error("tend read failed: " + error.Message)
