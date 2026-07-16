namespace FS.GG.Coord.GitHub

module Done =

    open System
    open System.Text.Json
    open FS.GG.Coord.Types
    open Errors
    open Transport

    type Closure =
        | ClosedByPullRequest of pr: int * oid: string * day: string
        | ResolvedWithoutPr of evidence: string
        | ClosedByNobody
        | StillOpen

    /// A pull request GitHub associates with closing this issue (`closedByPullRequestsReferences`). This
    /// list is a SUPERSET: it also carries PRs that merely MENTION the issue in prose, so which one is the
    /// closer is decided by `ClosesThis` (the `Closes #N` in the PR body) OR the issue's own CLOSED_EVENT.
    type ClosingPr =
        { Number: int
          Merged: bool
          /// ISO-8601 merge time, "" if unknown — the LATEST-merged among true closers wins (#342).
          MergedAt: string
          /// The merge commit's abbreviated oid, "" if unknown — named in the stamp.
          Oid: string
          /// Its own `closingIssuesReferences` names THIS issue (the `Closes #N` in the PR body).
          ClosesThis: bool }

    type Children =
        | NoChildren
        | AllResolved of count: int
        | SomeOpen of numbers: int list
        | Unverifiable of totalCount: int * seen: int

    type Discharge =
        | Completes
        | Partial of why: string

    type Facts =
        { Ref: Ref
          State: IssueState
          /// Every PR GitHub associates with closing this issue — a superset that also lists mentions.
          ClosingPrs: ClosingPr list
          /// The PR numbers GitHub's own `CLOSED_EVENT` names as the closer: a PullRequest closer directly,
          /// or the PR(s) associated with the Commit that closed it (#558 — a commit-subject keyword).
          CloserPrs: int list
          Children: Children
          BoardStatus: BoardStatus
          Parent: Ref option }

    type RollUp =
        | ParentClosed of Ref
        | ParentLeftOpen of Ref * reasons: string list
        | NoParent

    // ---- THE PRECONDITIONS, PURE -------------------------------------------------------------------

    let verify (prOverride: int option) (resolvedWithoutPr: string option) (facts: Facts) : Verdict<Closure> =
        match facts.Children with
        // TRUNCATION FIRST, AND IT IS NOT NEGOTIABLE. `totalCount` and the nodes we were handed disagree, so
        // the page was cut short. An unverifiable subject must not report green — and a truncated page that
        // happened to show only CLOSED children would sail straight through every test below this one, which
        // is exactly how a confident green comes to be printed over work nobody looked at.
        //
        // `NoVerdict`, not `Red`: we are not saying the work is unfinished. We are saying we could not tell,
        // and those are different sentences with different remedies.
        | Unverifiable(total, seen) ->
            NoVerdict
                $"the sub-issue list was truncated — %d{total} child(ren) exist and only %d{seen} were returned. Refusing to stamp a subject we could not fully read."

        | SomeOpen numbers ->
            // #583. A parent with open children is not done, whatever its board column says.
            let names = numbers |> List.map (fun n -> $"#%d{n}") |> String.concat ", "

            Red [ $"%d{List.length numbers} sub-issue(s) are still OPEN: %s{names}. A parent is not finished while its children are." ]

        | NoChildren
        | AllResolved _ ->

        match facts.State with
        | Open ->
            Red
                [ "the issue is still OPEN. The stamp records that work is finished; it does not finish it." ]

        | Closed ->

        // THE CLOSING ACT, FROM GITHUB'S OWN RECORD — never from the prose, and NEVER the first merge that
        // merely mentions the issue (#342).
        //
        // A PR CLOSES this issue if EITHER of GitHub's own records says so — both are records of the act,
        // neither is a mention:
        //   (A) its `closingIssuesReferences` names this issue — the `Closes #N` in the PR BODY (`ClosesThis`);
        //   (B) the issue's own `CLOSED_EVENT` names it as the closer, directly (a PullRequest) or as the PR
        //       associated with the closing Commit (`CloserPrs`). This is the leg #558/#543 needed: the
        //       recipe's `gh pr create --fill` routes a closing keyword in the commit SUBJECT to the PR TITLE,
        //       where (A) cannot see it — so a correctly merged, correctly closing PR stamped RED forever.
        //
        // Among those, the LATEST-MERGED wins. `closedByPullRequestsReferences` lists mentions too, and GitHub
        // returns them lowest-number-first — so taking the first stamped an earlier prose mention, or a merge
        // that never touched the work (#342). It is provenance, not just outcome.
        //
        // `--pr` overrides WHICH pull request the stamp names — NEVER whether it closed the issue. Selecting by
        // number alone (#543 leg 2) was a soundness hole: point it at any merged PR that mentions the issue and
        // the stamp went green. Same predicate, both paths.
        let closerSet = Set.ofList facts.CloserPrs
        let closes (p: ClosingPr) = p.ClosesThis || closerSet.Contains p.Number

        let chosen =
            match prOverride with
            | Some n -> facts.ClosingPrs |> List.filter (fun p -> p.Number = n && p.Merged && closes p) |> List.tryHead
            | None ->
                facts.ClosingPrs
                |> List.filter (fun p -> p.Merged && closes p)
                |> List.sortBy (fun p -> p.MergedAt)
                |> List.tryLast

        match chosen with
        | Some p ->
            let day = if p.MergedAt.Length >= 10 then p.MergedAt.Substring(0, 10) else p.MergedAt
            Green(ClosedByPullRequest(p.Number, p.Oid, day))

        | None ->
            // #600 — THE GREEN PATH FOR WORK RESOLVED WITHOUT A PR.
            //
            // An item legitimately closed with no code change in this repo at all: obsolete, resolved by
            // other work, a duplicate whose detail was transplanted into the survivor (which `pnext-item` §4
            // explicitly instructs), a decision item whose deliverable is an ADR somewhere else. Every one
            // of those stamped RED, reproducibly, on correct work — and a red that fires reproducibly on
            // correct work teaches every worker that red stamps are noise.
            //
            // The evidence is REQUIRED. A green path that took no argument would not be a stamp; it would be
            // a way of switching the stamp off, and it would be reached for by exactly the people it was not
            // meant for.
            match resolvedWithoutPr with
            | Some evidence when not (String.IsNullOrWhiteSpace evidence) -> Green(ResolvedWithoutPr evidence)

            | Some _ ->
                Red
                    [ "no PR closes this issue, and the evidence offered for resolving it without one is blank. Say what finished it." ]

            | None ->
                match prOverride with
                | Some n ->
                    // #543 leg 2 — `--pr` pointed at a PR that does NOT close this issue (it is not merged, or
                    // it only mentions the issue). `--pr` cannot launder a mention into a stamp.
                    Red
                        [ $"PR #%d{n} does not close this issue — it must be merged, and either name this issue in its body (Closes #%d{facts.Ref.Number}) or be what GitHub recorded as closing it. `--pr` overrides WHICH pull request the stamp names, never WHETHER it closed the issue (#543)." ]

                | None ->
                    Red
                        [ "no merged PR closes this issue, and nothing records what closed it — no merged PR names it, and its close event names no PR or commit."
                          "If it was resolved WITHOUT a pull request (obsolete, a duplicate, resolved by other work, a decision recorded elsewhere), say so with evidence — that is a green path, not a workaround (#600)." ]

    let render (ref: Ref) (verdict: Verdict<Closure>) =
        match verdict with
        | Green(ClosedByPullRequest(pr, oid, day)) ->
            let sha = if String.IsNullOrEmpty oid then "?" else oid
            let dsuffix = if String.IsNullOrEmpty day then "" else $" (%s{day})"
            $"✓✓ FSGG-DONE   %s{ref.Short}  ·  merged PR #%d{pr} @ %s{sha}%s{dsuffix}"
        | Green(ResolvedWithoutPr evidence) -> $"✓✓ FSGG-DONE   %s{ref.Short}  ·  resolved without a PR: %s{evidence}"
        | Green ClosedByNobody
        | Green StillOpen ->
            // These two are not green states, and the type cannot currently produce them here. If a later
            // change makes it possible, this is the line that will say so rather than printing a tick.
            $"?? FSGG-DEFECT %s{ref.Short}  ·  the engine reported a green verdict it has no stamp for"

        | Red reasons ->
            let lines = reasons |> List.map (fun r -> $"\n  %s{r}") |> String.concat ""
            $"✗✗ FSGG-NOT-DONE  %s{ref.Short}%s{lines}"

        | NoVerdict reason -> $"?? FSGG-UNVERIFIED %s{ref.Short}\n  %s{reason}"

    // ---- the one query ------------------------------------------------------------------------------

    [<Literal>]
    let private FactsDoc =
        "query($owner: String!, $repo: String!, $number: Int!) { \
         repository(owner: $owner, name: $repo) { \
           issue(number: $number) { \
             number state \
             closedByPullRequestsReferences(first: 10, includeClosedPrs: true) { nodes { number merged mergedAt mergeCommit { abbreviatedOid } closingIssuesReferences(first: 10) { nodes { number repository { nameWithOwner } } } } } \
             timelineItems(last: 10, itemTypes: [CLOSED_EVENT]) { nodes { ... on ClosedEvent { closer { __typename ... on PullRequest { number } ... on Commit { oid associatedPullRequests(first: 5) { nodes { number } } } } } } } \
             subIssues(first: 100) { totalCount nodes { number state } } \
             projectItems(first: 20) { nodes { project { number } status: fieldValueByName(name: \"Status\") { ... on ProjectV2ItemFieldSingleSelectValue { name } } } } \
             parent { number repository { name owner { login } } } \
           } \
         } rateLimit { cost remaining } }"

    let private boardStatusOf (s: string) =
        match s.Trim().ToLowerInvariant() with
        | "" -> NoStatus
        | "backlog" -> Backlog
        | "ready" -> Ready
        | "in progress" -> InProgress
        | "blocked" -> Blocked
        | "in review" -> InReview
        | "done" -> Done
        | _ -> NoStatus

    let facts (transport: IGitHubTransport) (board: Board.BoardMap) (ref: Ref) : IoResult<Facts> =
        let subject = ref.Short

        let request =
            { Method = "POST"
              Path = "graphql"
              Query = []
              Body =
                Query(
                    FactsDoc,
                    [ "owner", VString ref.Owner
                      "repo", VString ref.Repo
                      "number", VNumber(double ref.Number) ]
                )
              Budget = GraphQl
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->

        try
            use doc = JsonDocument.Parse response.Body

            match doc.RootElement.TryGetProperty "errors" with
            | true, errs when errs.ValueKind = JsonValueKind.Array && errs.GetArrayLength() > 0 ->
                let messages =
                    errs.EnumerateArray()
                    |> Seq.map (fun e ->
                        match e.TryGetProperty "message" with
                        | true, m when m.ValueKind = JsonValueKind.String -> m.GetString()
                        | _ -> "(no message)")
                    |> List.ofSeq

                if messages |> List.exists Budget.isRateLimited then
                    // Asserted, not read — this arm parses a GraphQL `errors` array, so the response is a
                    // GraphQL one by construction (same reasoning as `Scan.fs`).
                    Error(RateLimited(GraphQlBudget, None))
                else
                    Error(GraphQlErrors messages)

            | _ ->

            let issue =
                doc.RootElement.GetProperty("data").GetProperty("repository").GetProperty("issue")

            if issue.ValueKind = JsonValueKind.Null then
                Error(NotFound subject)
            else

            let state =
                match issue.TryGetProperty "state" with
                | true, s when s.ValueKind = JsonValueKind.String ->
                    if s.GetString().ToUpperInvariant() = "CLOSED" then Closed else Open
                | _ -> Open

            // The whole `closedByPullRequestsReferences` set, MERGE STATE AND PROVENANCE INTACT. `verify`
            // decides which one closed the issue (the latest-merged true closer), so the read must not collapse
            // the set to a single number: a mention and the real closer look identical until you keep their
            // `mergedAt` and their `closingIssuesReferences`.
            let strOf (el: JsonElement) (name: string) =
                match el.TryGetProperty name with
                | true, v when v.ValueKind = JsonValueKind.String -> v.GetString()
                | _ -> ""

            let closingPrs =
                match issue.TryGetProperty "closedByPullRequestsReferences" with
                | true, refs ->
                    match refs.TryGetProperty "nodes" with
                    | true, nodes when nodes.ValueKind = JsonValueKind.Array ->
                        nodes.EnumerateArray()
                        |> Seq.choose (fun n ->
                            match n.TryGetProperty "number" with
                            | true, num when num.ValueKind = JsonValueKind.Number ->
                                let merged =
                                    match n.TryGetProperty "merged" with
                                    | true, m -> m.ValueKind = JsonValueKind.True
                                    | _ -> false

                                let oid =
                                    match n.TryGetProperty "mergeCommit" with
                                    | true, mc when mc.ValueKind = JsonValueKind.Object -> strOf mc "abbreviatedOid"
                                    | _ -> ""

                                // `closesThis`: this PR's body names THIS issue in its own repo (#342/#543).
                                let closesThis =
                                    match n.TryGetProperty "closingIssuesReferences" with
                                    | true, cir ->
                                        match cir.TryGetProperty "nodes" with
                                        | true, cn when cn.ValueKind = JsonValueKind.Array ->
                                            cn.EnumerateArray()
                                            |> Seq.exists (fun c ->
                                                let num =
                                                    match c.TryGetProperty "number" with
                                                    | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt32())
                                                    | _ -> None

                                                let nwo =
                                                    match c.TryGetProperty "repository" with
                                                    | true, r when r.ValueKind = JsonValueKind.Object -> strOf r "nameWithOwner"
                                                    | _ -> ""

                                                num = Some ref.Number
                                                && String.Equals(nwo, $"%s{ref.Owner}/%s{ref.Repo}", StringComparison.OrdinalIgnoreCase))
                                        | _ -> false
                                    | _ -> false

                                Some
                                    { Number = num.GetInt32()
                                      Merged = merged
                                      MergedAt = strOf n "mergedAt"
                                      Oid = oid
                                      ClosesThis = closesThis }
                            | _ -> None)
                        |> List.ofSeq
                    | _ -> []
                | _ -> []

            // THE CLOSED_EVENT CLOSERS. A PullRequest closer names its own number; a Commit closer (a squash
            // whose subject carried the keyword) names the PR(s) associated with it (#558). Both are the PR that
            // actually closed the issue, per GitHub's own record.
            let closerPrs =
                match issue.TryGetProperty "timelineItems" with
                | true, tl ->
                    match tl.TryGetProperty "nodes" with
                    | true, nodes when nodes.ValueKind = JsonValueKind.Array ->
                        nodes.EnumerateArray()
                        |> Seq.collect (fun n ->
                            match n.TryGetProperty "closer" with
                            | true, c when c.ValueKind = JsonValueKind.Object ->
                                match c.TryGetProperty "number" with
                                | true, num when num.ValueKind = JsonValueKind.Number -> Seq.singleton (num.GetInt32())
                                | _ ->
                                    match c.TryGetProperty "associatedPullRequests" with
                                    | true, apr ->
                                        match apr.TryGetProperty "nodes" with
                                        | true, an when an.ValueKind = JsonValueKind.Array ->
                                            an.EnumerateArray()
                                            |> Seq.choose (fun a ->
                                                match a.TryGetProperty "number" with
                                                | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt32())
                                                | _ -> None)
                                        | _ -> Seq.empty
                                    | _ -> Seq.empty
                            | _ -> Seq.empty)
                        |> List.ofSeq
                        |> List.distinct
                    | _ -> []
                | _ -> []

            let children =
                match issue.TryGetProperty "subIssues" with
                | true, subs ->
                    let total =
                        match subs.TryGetProperty "totalCount" with
                        | true, t when t.ValueKind = JsonValueKind.Number -> t.GetInt32()
                        | _ -> 0

                    let seen =
                        match subs.TryGetProperty "nodes" with
                        | true, nodes when nodes.ValueKind = JsonValueKind.Array ->
                            nodes.EnumerateArray()
                            |> Seq.choose (fun n ->
                                let num =
                                    match n.TryGetProperty "number" with
                                    | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt32())
                                    | _ -> None

                                let st =
                                    match n.TryGetProperty "state" with
                                    | true, v when v.ValueKind = JsonValueKind.String -> v.GetString().ToUpperInvariant()
                                    | _ -> "OPEN"

                                num |> Option.map (fun n -> n, st))
                            |> List.ofSeq
                        | _ -> []

                    // THE TRUNCATION CHECK, AT THE READ. `totalCount` is the server's count; `nodes` is what
                    // it gave us. When they disagree the page was cut short, and an unverifiable subject may
                    // not report green.
                    if total <> List.length seen then
                        Unverifiable(total, List.length seen)
                    elif total = 0 then
                        NoChildren
                    else
                        let openOnes =
                            seen |> List.filter (fun (_, st) -> st <> "CLOSED") |> List.map fst

                        if List.isEmpty openOnes then
                            AllResolved total
                        else
                            SomeOpen openOnes

                | _ -> NoChildren

            let boardStatus =
                match issue.TryGetProperty "projectItems" with
                | true, items ->
                    match items.TryGetProperty "nodes" with
                    | true, nodes when nodes.ValueKind = JsonValueKind.Array ->
                        nodes.EnumerateArray()
                        |> Seq.tryPick (fun n ->
                            let onOurBoard =
                                match n.TryGetProperty "project" with
                                | true, p ->
                                    match p.TryGetProperty "number" with
                                    | true, num when num.ValueKind = JsonValueKind.Number ->
                                        num.GetInt32() = board.Number
                                    | _ -> false
                                | _ -> false

                            if not onOurBoard then
                                None
                            else
                                match n.TryGetProperty "status" with
                                | true, s when s.ValueKind = JsonValueKind.Object ->
                                    match s.TryGetProperty "name" with
                                    | true, nm when nm.ValueKind = JsonValueKind.String ->
                                        Some(boardStatusOf (nm.GetString()))
                                    | _ -> Some NoStatus
                                | _ -> Some NoStatus)
                        |> Option.defaultValue NoStatus
                    | _ -> NoStatus
                | _ -> NoStatus

            let parent =
                match issue.TryGetProperty "parent" with
                | true, p when p.ValueKind = JsonValueKind.Object ->
                    try
                        let num = p.GetProperty("number").GetInt32()
                        let repo = p.GetProperty("repository")
                        let name = repo.GetProperty("name").GetString()
                        let owner = repo.GetProperty("owner").GetProperty("login").GetString()

                        Some
                            { Owner = owner
                              Repo = name
                              Number = num }
                    with _ ->
                        None
                | _ -> None

            Ok
                { Ref = ref
                  State = state
                  ClosingPrs = closingPrs
                  CloserPrs = closerPrs
                  Children = children
                  BoardStatus = boardStatus
                  Parent = parent }

        with :? JsonException as e ->
            Error(Malformed(subject, $"the done-stamp query's response is not JSON: %s{e.Message}"))

    // ---- closing an issue ---------------------------------------------------------------------------

    /// CLOSE the issue, as COMPLETED.
    ///
    /// `state_reason` matters: an issue closed as `not_planned` is not the same fact as one completed, and
    /// the roll-up above reads `state` alone — so a parent closed the wrong way would still read as
    /// "resolved" to every ancestor.
    let private closeIssue (transport: IGitHubTransport) (ref: Ref) : IoResult<unit> =
        let payload = """{"state":"closed","state_reason":"completed"}"""

        let request =
            { Method = "PATCH"
              Path = $"repos/%s{ref.Owner}/%s{ref.Repo}/issues/%d{ref.Number}"
              Query = []
              Body = Json payload
              Budget = Rest
              IfNoneMatch = None
              Subject = ref.Short }

        transport.Send request |> Result.map ignore

    // ---- the EPIC-UNLINKED-CHILD set (shared with `lint`, #485) --------------------------------------

    let bodyUnlinkedChildren
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (body: string)
        (graphRefs: string list)
        : IoResult<string list> =

        let inGraph = Set.ofList graphRefs

        // The body declares these; the graph contains those; the difference is the candidate unlinked set.
        let declared =
            FS.GG.Coord.EpicBody.childRefs owner repo body
            |> List.filter (fun d -> not (inGraph.Contains d))

        let ownerRepoNum (r: string) =
            let m = Text.RegularExpressions.Regex.Match(r, @"^([^/]+)/([^#]+)#(\d+)$")

            if m.Success then
                Some(m.Groups.[1].Value, m.Groups.[2].Value, int m.Groups.[3].Value)
            else
                None

        // Drop the refs GitHub confirms are PRs (#346); KEEP one the probe cannot resolve (#266).
        let rec prune (kept: string list) (refs: string list) : IoResult<string list> =
            match refs with
            | [] -> Ok(List.rev kept)
            | r :: rest ->
                match ownerRepoNum r with
                | None -> prune (r :: kept) rest
                | Some(o, rp, n) ->
                    match Reads.refIsPullRequest transport o rp n with
                    | Ok true -> prune kept rest
                    | Ok false -> prune (r :: kept) rest
                    | Error _ -> prune (r :: kept) rest

        prune [] declared

    // ---- the climb ----------------------------------------------------------------------------------

    [<Literal>]
    let private MaxHops = 10

    /// Strip the owner from an `owner/repo#n` ref → `repo#n`, the form a reason NAMES a ref in.
    let private shortRef (r: string) =
        match r.IndexOf '/' with
        | i when i >= 0 -> r.Substring(i + 1)
        | _ -> r

    let rollUp
        (transport: IGitHubTransport)
        (board: Board.BoardMap)
        (worker: string)
        (parent: Ref)
        (discharge: Discharge)
        : IoResult<RollUp list> =

        // #614, AND IT IS THE FIRST THING THAT HAPPENS.
        //
        // A PARTIAL child stops the climb dead — before any read, before any write. FS.GG.SDD#398 said in
        // bold that it did NOT complete FS.GG.SDD#350; the roll-up closed it anyway, because it inferred completion
        // from "all children are done" and #398 was the only child. Whether a child discharges its parent is
        // a fact only the child's author knows, and no amount of reading the board recovers it.
        //
        // So it is an argument, it has no default, and `Partial` is honoured before anything else is even
        // looked at.
        match discharge with
        | Partial why ->
            Ok
                [ ParentLeftOpen(
                      parent,
                      [ $"this child is a PARTIAL fix and does not discharge its parent: %s{why}"
                        "the parent stays OPEN, even though this may be its only child (#614)." ]
                  ) ]

        | Completes ->

        let results = ResizeArray<RollUp>()

        let rec climb (current: Ref) (hops: int) : IoResult<unit> =
            if hops >= MaxHops then
                // A PARENT CHAIN LONGER THAN TEN HOPS IS A CYCLE, and a cycle is a bug — one that would
                // otherwise climb forever, stamping as it went.
                results.Add(
                    ParentLeftOpen(current, [ $"the parent chain is more than %d{MaxHops} deep — refusing to climb further; this is a cycle, not a hierarchy." ])
                )

                Ok()
            else

            match facts transport board current with
            | Error e -> Error e
            | Ok parentFacts ->

            // THE PARENT IS JUDGED BY THE SAME TOTAL FUNCTION AS EVERY OTHER ITEM. There is no second,
            // laxer set of rules for a parent — that asymmetry is how #613 and #614 both got in.
            //
            // The parent is still OPEN at this point (we are about to close it), so `verify` would refuse it
            // for that reason alone. What we need from it is the CHILD verdict, so the children are checked
            // directly and the closing act is not required of a parent: a parent is closed BY its children,
            // not by a PR of its own.
            match parentFacts.Children with
            | Unverifiable(total, seen) ->
                results.Add(
                    ParentLeftOpen(
                        current,
                        [ $"the parent's sub-issue list was truncated — %d{total} exist, %d{seen} returned. Refusing to close a parent whose children we could not fully see." ]
                    )
                )

                Ok()

            | SomeOpen numbers ->
                let names = numbers |> List.map (fun n -> $"#%d{n}") |> String.concat ", "

                results.Add(
                    ParentLeftOpen(current, [ $"%d{List.length numbers} sibling(s) are still OPEN: %s{names}." ])
                )

                Ok()

            | NoChildren ->
                // A PARENT WITH NO CHILDREN IS NOT A PARENT WE CLOSE. We reached it by climbing FROM a
                // child, so a zero-child answer means the read disagrees with the edge we followed — and
                // closing an issue on the strength of a contradiction is exactly the shape of #614.
                results.Add(
                    ParentLeftOpen(
                        current,
                        [ "we climbed to this parent from one of its children, and it reports NO children. Refusing to close on a contradiction." ]
                    )
                )

                Ok()

            | AllResolved _ ->

            // THE EPIC-UNLINKED-CHILD RULE, APPLIED TO THE ROLL-UP (#325). "All children resolved" is a
            // claim about the sub-issue GRAPH — but if the parent's BODY declares a child the graph does not
            // contain, that claim is about a set the body itself says is incomplete, and the roll-up would
            // close the parent over a criterion it split out and lost track of. So the same check `lint`
            // makes is made here: a body-cited PR ref is dropped (#346), an unresolvable one is kept (#266).
            // (`facts`' graph is summarised to counts, so the refs are re-read; a parent about to flip is rare.)
            match Reads.issueBody transport current.Owner current.Repo current.Number with
            | Error e -> Error e
            | Ok parentBody ->

            match Reads.subIssues transport current.Owner current.Repo current.Number with
            | Error e -> Error e
            | Ok graph ->

            match
                bodyUnlinkedChildren
                    transport
                    current.Owner
                    current.Repo
                    parentBody
                    (graph.Children |> List.map (fun c -> c.Ref))
            with
            | Error e -> Error e
            | Ok unlinked when not (List.isEmpty unlinked) ->
                let named = unlinked |> List.map shortRef |> String.concat ", "

                results.Add(
                    ParentLeftOpen(
                        current,
                        [ $"the epic body declares %d{List.length unlinked} child(ren) the sub-issue graph does not contain: %s{named}. Link them with `fsgg-coord child`, or drop them from the body, before it can roll up (#325)." ]
                    )
                )

                Ok()

            | Ok _ ->

            // BOARD *AND* ISSUE, TOGETHER. #613: the roll-up stamped the parent `Done` on the board and
            // never CLOSED THE ISSUE — so the upward climb died at the next hop (which reads an OPEN child),
            // and the board and the issue disagreed about the same work. FS.GG.Rendering#361 sat like that:
            // board `Done`, issue OPEN, all four children closed.
            let boardResult =
                Board.boardWrite transport board current.Owner current.Repo current.Number "Status" (Board.Set "Done") worker

            match boardResult with
            | Error e -> Error e
            | Ok _ ->

            match closeIssue transport current with
            | Error e -> Error e
            | Ok() ->
                results.Add(ParentClosed current)

                // CLIMB ON. The parent we just closed is now a resolved child of ITS parent, and it
                // `Completes` that parent by construction: we only got here because all of its own children
                // were resolved. This is the hop #613 was losing.
                match parentFacts.Parent with
                | None -> Ok()
                | Some grandparent -> climb grandparent (hops + 1)

        match climb parent 0 with
        | Error e -> Error e
        | Ok() ->
            if results.Count = 0 then
                Ok [ NoParent ]
            else
                Ok(List.ofSeq results)
