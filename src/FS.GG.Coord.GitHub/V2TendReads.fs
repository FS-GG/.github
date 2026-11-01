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

    type LinkCard =
        { WorkLevel: V2Tend.WorkLevel option
          Open: bool
          Status: V2Tend.Status option
          Parent: string option
          Unreadable: bool }

    type Linkage =
        | Linked of delivery: string
        | Unlinked of reason: string
        | Unknown of reason: string

    let readCeiling (rows: int) = 8 + (rows + 49) / 50 + (rows + 24) / 25

    type Operation = { Kind: string; RootFields: string list }

    type Document = { Operations: Operation list; HasStringLiteral: bool }

    type private Token =
        | Name of string
        | Punct of char
        | Spread
        | StringToken

    // Lex a GraphQL document: comments and commas are skipped; strings and block strings become one token.
    let private lex (text: string) : Token list option =
        let n = text.Length
        let tokens = Collections.Generic.List<Token>()
        let mutable i = 0
        let mutable ok = true
        while ok && i < n do
            let c = text.[i]
            if c = '#' then
                while i < n && text.[i] <> '\n' && text.[i] <> '\r' do i <- i + 1
            elif Char.IsWhiteSpace c || c = ',' || c = '\uFEFF' then i <- i + 1
            elif c = '"' then
                if i + 2 < n && text.[i + 1] = '"' && text.[i + 2] = '"' then
                    let mutable j = i + 3
                    let mutable closed = false
                    while not closed && j <= n - 3 do
                        if text.[j] = '\\' && j + 3 < n && text.[j + 1] = '"' && text.[j + 2] = '"' && text.[j + 3] = '"' then j <- j + 4
                        elif text.[j] = '"' && text.[j + 1] = '"' && text.[j + 2] = '"' then closed <- true
                        else j <- j + 1
                    if closed then
                        tokens.Add StringToken
                        i <- j + 3
                    else ok <- false
                else
                    let mutable j = i + 1
                    let mutable closed = false
                    while ok && not closed && j < n do
                        match text.[j] with
                        | '\\' -> j <- j + 2
                        | '"' -> closed <- true
                        | '\n' | '\r' -> ok <- false
                        | _ -> j <- j + 1
                    if closed then
                        tokens.Add StringToken
                        i <- j + 1
                    else ok <- false
            elif c = '.' then
                if i + 2 < n && text.[i + 1] = '.' && text.[i + 2] = '.' then
                    tokens.Add Spread
                    i <- i + 3
                else ok <- false
            elif Char.IsLetter c || c = '_' then
                let start = i
                while i < n && (Char.IsLetterOrDigit text.[i] || text.[i] = '_') do i <- i + 1
                tokens.Add(Name(text.Substring(start, i - start)))
            elif "{}()[]:=!$@&|-+".IndexOf c >= 0 || Char.IsDigit c then
                tokens.Add(Punct c)
                i <- i + 1
            else ok <- false
        if ok then Some(List.ofSeq tokens) else None

    // Skip one balanced group that starts at `index` with `opening`; the index after its closing token.
    let private skipGroup (tokens: Token[]) (index: int) (opening: char) (closing: char) =
        let mutable depth = 0
        let mutable i = index
        let mutable fin = -1
        while fin < 0 && i < tokens.Length do
            match tokens.[i] with
            | Punct c when c = opening -> depth <- depth + 1
            | Punct c when c = closing ->
                depth <- depth - 1
                if depth = 0 then fin <- i + 1
            | _ -> ()
            i <- i + 1
        fin

    // The root fields of one selection set starting at `index` (a `{`): field names after any alias, `...`
    // for a fragment spread or inline fragment. Returns the fields and the index after the closing `}`.
    let private rootSelection (tokens: Token[]) (index: int) : (string list * int) option =
        let fields = Collections.Generic.List<string>()
        let mutable i = index + 1
        let mutable fin = -1
        let mutable ok = true
        let skipArgumentsAndDirectives () =
            let mutable go = true
            while ok && go && i < tokens.Length do
                match tokens.[i] with
                | Punct '(' ->
                    let next = skipGroup tokens i '(' ')'
                    if next < 0 then ok <- false else i <- next
                | Punct '@' when i + 1 < tokens.Length ->
                    match tokens.[i + 1] with
                    | Name _ -> i <- i + 2
                    | _ -> ok <- false
                | _ -> go <- false
        while ok && fin < 0 && i < tokens.Length do
            match tokens.[i] with
            | Punct '}' -> fin <- i + 1
            | Spread ->
                fields.Add "..."
                i <- i + 1
                // `...Name` or `... on Type` with directives and a selection set
                match (if i < tokens.Length then Some tokens.[i] else None) with
                | Some(Name "on") -> i <- i + 2
                | Some(Name _) -> i <- i + 1
                | _ -> ()
                skipArgumentsAndDirectives ()
                match (if i < tokens.Length then Some tokens.[i] else None) with
                | Some(Punct '{') ->
                    let next = skipGroup tokens i '{' '}'
                    if next < 0 then ok <- false else i <- next
                | _ -> ()
            | Name first ->
                let field =
                    if i + 2 < tokens.Length && tokens.[i + 1] = Punct ':' then
                        match tokens.[i + 2] with
                        | Name real ->
                            i <- i + 3
                            Some real
                        | _ -> None
                    else
                        i <- i + 1
                        Some first
                match field with
                | None -> ok <- false
                | Some name ->
                    fields.Add name
                    skipArgumentsAndDirectives ()
                    match (if i < tokens.Length then Some tokens.[i] else None) with
                    | Some(Punct '{') ->
                        let next = skipGroup tokens i '{' '}'
                        if next < 0 then ok <- false else i <- next
                    | _ -> ()
            | _ -> ok <- false
        if ok && fin > 0 then Some(List.ofSeq fields, fin) else None

    let parseDocument (document: string) : Document option =
        match lex (if isNull document then "" else document) with
        | None -> None
        | Some list ->
            let tokens = Array.ofList list
            let operations = Collections.Generic.List<Operation>()
            let mutable i = 0
            let mutable ok = true
            while ok && i < tokens.Length do
                let kind, start =
                    match tokens.[i] with
                    | Punct '{' -> Some "query", i
                    | Name("query" | "mutation" | "subscription" | "fragment" as kind) ->
                        // Skip the name, variable definitions and directives up to the selection set.
                        let mutable j = i + 1
                        let mutable found = -1
                        while found < 0 && j < tokens.Length do
                            match tokens.[j] with
                            | Punct '{' -> found <- j
                            | Punct '(' ->
                                let next = skipGroup tokens j '(' ')'
                                if next < 0 then j <- tokens.Length else j <- next
                            | Punct '}' -> j <- tokens.Length
                            | _ -> j <- j + 1
                        (if found < 0 then None else Some kind), found
                    | _ -> None, -1
                match kind with
                | None -> ok <- false
                | Some kind ->
                    match rootSelection tokens start with
                    | None -> ok <- false
                    | Some(fields, next) ->
                        operations.Add { Kind = kind; RootFields = fields }
                        i <- next
            if ok && operations.Count > 0 then
                Some { Operations = List.ofSeq operations; HasStringLiteral = list |> List.contains StringToken }
            else None

    // The kinds of a GraphQL document's top-level definitions; None when the document does not parse.
    let operationKinds (document: string) : string list option =
        parseDocument document |> Option.map (fun d -> d.Operations |> List.map (fun o -> o.Kind))

    let private maximumItems = 500

    let readOnly (transport: IGitHubTransport) : IGitHubTransport =
        { new IGitHubTransport with
            member _.Send request =
                match request.Method, request.Path, request.Body with
                | "POST", "graphql", Query(document, _) when
                    (match operationKinds document with
                     | Some kinds -> kinds |> List.contains "query" && kinds |> List.forall (fun kind -> kind = "query" || kind = "fragment")
                     | None -> false) ->
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
    let private completesMarker = "<!-- fsgg:delivery-completes/v1 -->"
    let private tendClosedMarker = "<!-- fsgg:tend-closed/v1 -->"
    let private blockedOnMarker = Regex(@"<!--\s*fsgg:blocked-on/v1\b([^>]*)-->")
    let private fullRef = Regex(@"([\w.-]+/[\w.-]+)#(\d+)")

    let private visible (body: string) =
        let normalised = (if isNull body then "" else body).Replace("\r\n", "\n").Replace("\r", "\n")
        inline'.Replace(fence.Replace(normalised, " "), " ")

    // N as a number: any Unicode decimal digits (as Python's int() reads them), no leading zeros.
    let private canonicalNumber (digits: string) =
        let ascii = String(digits.ToCharArray() |> Array.map (fun c -> char (int '0' + int (Char.GetNumericValue c))))
        match ascii.TrimStart '0' with
        | "" -> "0"
        | trimmed -> trimmed

    let canonicalRef (reference: string) =
        let text = if isNull reference then "" else reference
        match text.LastIndexOf '#' with
        | -1 -> text.ToLowerInvariant()
        | index ->
            let number = text.Substring(index + 1)
            if number <> "" && number |> Seq.forall Char.IsDigit then text.Substring(0, index).ToLowerInvariant() + "#" + canonicalNumber number
            else text.ToLowerInvariant()

    let bodyRefs (repository: string) (body: string) =
        let text = visible body
        [ for pattern in [ refsLine; closing ] do
            for m in pattern.Matches text do
                for r in oneRef.Matches m.Groups.[1].Value do
                    let repo = if r.Groups.[1].Success then r.Groups.[1].Value else repository
                    yield canonicalRef (repo + "#" + r.Groups.[2].Value) ]
        |> List.distinct
        |> List.sort

    let continues (body: string) = (visible body).Contains continuesMarker

    let completes (body: string) = (visible body).Contains completesMarker

    let linkage (cards: Map<string, LinkCard>) (anonymousUnreadable: bool) (prRepository: string) (prNumber: int) (prOpen: bool) (body: string) =
        let find (reference: string) = cards |> Map.tryFind (canonicalRef reference)
        let own = canonicalRef (prRepository + "#" + string prNumber)
        let refs = bodyRefs prRepository body |> List.filter (fun r -> r <> own)
        // An unreadable referenced card, or a miss while the board holds an unreadable card with no identity
        // (the miss could be that card), gives no verdict.
        let unreadable = refs |> List.exists (fun r -> match find r with Some c -> c.Unreadable | None -> anonymousUnreadable)
        if unreadable then Unknown "r1-reference-unreadable"
        else
            let deliveries = refs |> List.choose (fun r -> match find r with Some c when c.WorkLevel = Some V2Tend.Delivery -> Some(r, c) | _ -> None)
            match deliveries with
            | [] -> Unlinked "no-delivery"
            | [ delivery, card ] ->
                // linkage_of() lists every failure; a `U` failure means no verdict, so Unknown is preferred over
                // an R1 failure when both apply (as board-linkage and the merge gate read the list).
                let parentUnknown =
                    match card.Parent |> Option.map find with
                    | Some(Some p) -> p.Unreadable
                    | Some None -> anonymousUnreadable
                    | None -> false
                if parentUnknown then Unknown "r1-parent-unreadable"
                elif prOpen && not card.Open then Unlinked "delivery-closed"
                elif prOpen && card.Status = Some V2Tend.Done then Unlinked "delivery-done"
                else
                    match card.Parent |> Option.bind find with
                    | Some p when p.WorkLevel = Some V2Tend.Outcome -> Linked delivery
                    | _ -> Unlinked "no-parent-outcome"
            | _ -> Unlinked "multiple-deliveries"

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
            let issueCounts = Collections.Generic.List<int>()
            let openPrs =
                GraphQl.drain searchSubject "the open pull requests" { MaxPages = 10; MaxItems = 1000 } (fun cursor ->
                    let variables = [ "q", VString(sprintf "org:%s is:pr is:open" inputs.Owner) ] @ (match cursor with Some c -> [ "cursor", VString c ] | None -> [])
                    sendIo (request searchDocument variables searchSubject) (fun data ->
                        let search = data.GetProperty "search"
                        issueCounts.Add(search.GetProperty("issueCount").GetInt32())
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
            // The search connection has no totalCount: its issueCount must hold on every page and equal the
            // nodes read, or the open-PR set is incomplete (a PR opened or closed mid-read, or a short page).
            if issueCounts |> Seq.distinct |> Seq.length <> 1 || issueCounts.[0] <> openPrs.Length then
                failwithf "%s: read %d open pull requests but the search reported %s" searchSubject openPrs.Length
                    (String.Join(", ", issueCounts |> Seq.distinct |> Seq.map string))
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
            // Pull-request and draft cards are never a Delivery: they are off the board for linkage, as in
            // linkage_of(). An unreadable card has no identity here, so every miss becomes Unknown.
            let lookup =
                cards
                |> List.filter (fun c -> c.Kind = V2Tend.Issue)
                |> List.map (fun c -> canonicalRef c.Ref, ({ WorkLevel = c.WorkLevel; Open = c.Open; Status = c.Status; Parent = c.Parent; Unreadable = false } : LinkCard))
                |> Map.ofList
            let anonymousUnreadable = cards |> List.exists (fun c -> c.Kind = V2Tend.Unreadable)
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
                                    let states = named |> List.map (fun r -> r, lookup |> Map.tryFind (canonicalRef r))
                                    if states |> List.exists (snd >> Option.isNone) then None // a blocker off the board: its state is unknown
                                    else Some(states |> List.map (fun (r, s) -> ({ Ref = r; Open = s.Value.Open } : V2Tend.Blocker)))
                                | None -> if commentsComplete then Some [] else None
                            c.ItemId, (prs, complete, marker, tendClosed) ]) with
                | Error e -> failwith e
                | Ok rows -> for item, row in rows do timelines.[item] <- row
            // Link PRs to cards with the board-currency linkage.
            let allPrs (c: V2Tend.Card) =
                let fromTimeline = match timelines.TryGetValue c.ItemId with | true, (prs, _, _, _) -> prs | _ -> []
                let own = canonicalRef c.Ref
                let relevant = openPrs |> Seq.filter (fun pr -> bodyRefs pr.Repo pr.Body |> List.contains own) |> List.ofSeq
                fromTimeline @ relevant
                |> List.distinctBy (fun pr -> pr.Repo.ToLowerInvariant(), pr.Number)
                |> List.filter (fun pr -> bodyRefs pr.Repo pr.Body |> List.contains own)
                |> List.map (fun pr ->
                    let verdict = linkage lookup anonymousUnreadable pr.Repo pr.Number (pr.State = V2Tend.PrOpen) pr.Body
                    ({ Id = pr.Repo + "#" + string pr.Number; State = pr.State; Draft = pr.Draft
                       // Unknown is never strict: it gates nothing automatically and is kept as the reason.
                       Linkage =
                           (match verdict with
                            | Linked d when d = own -> V2Tend.Strict
                            | Linked _ -> V2Tend.Loose "names-another-delivery"
                            | Unlinked reason | Unknown reason -> V2Tend.Loose reason)
                       UpdatedAt = pr.UpdatedAt; MergedAt = pr.MergedAt; Continues = continues pr.Body; Completes = completes pr.Body } : V2Tend.LinkedPr))
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
