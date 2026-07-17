module FS.GG.Coord.GitHub.Tests.DoneFactsTests

open Xunit
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.Done

/// THE READING HALF OF THE DONE-STAMP.
///
/// `DoneTests` proves `verify` — the PURE decision — from hand-built `Facts`. That is the right test for
/// the preconditions, and it is why they are pure. But it means the thing that READS the facts off GitHub's
/// GraphQL — `Done.facts` — was never exercised: the whole `verify` suite would pass identically if `facts`
/// returned garbage, because `verify` never sees the network.
///
/// And `facts` is doing real, fallible work: it distinguishes a MERGED PR from an open one, it reads the
/// closing ACT out of two different places, and it computes the truncation check that `verify` then trusts
/// absolutely. A bug in any of those is a bug `verify` cannot catch, because `verify` was told the wrong
/// facts and has no way to know it.
let private ref =
    { Owner = "FS-GG"
      Repo = "FS.GG.SDD"
      Number = 350 }

let private board: Board.BoardMap =
    { Number = 12
      Id = "PVT_coord"
      Owner = "FS-GG"
      Title = "Coordination"
      Fields = Map.empty }

let private serving (body: string) =
    Fake.Recorder(fun _ ->
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None })

/// The done-stamp query's response, with the pieces a test wants to vary.
let private response
    (state: string)
    (closingPrs: string)
    (closedEvent: string)
    (subIssues: string)
    (parent: string)
    =
    $"""{{"data":{{"repository":{{"issue":{{
        "number":350,"state":"%s{state}",
        "closedByPullRequestsReferences":{{"nodes":[%s{closingPrs}]}},
        "timelineItems":{{"nodes":[%s{closedEvent}]}},
        "subIssues":%s{subIssues},
        "projectItems":{{"nodes":[{{"project":{{"number":12}},"status":{{"name":"In progress"}}}}]}},
        "parent":%s{parent}}}}}}}}}"""

let private noSubs = """{"totalCount":0,"nodes":[]}"""

// ---- the closing act ----------------------------------------------------------------------------------

/// A `closedByPullRequestsReferences` node whose BODY names THIS issue (#350) — a true closer.
let private closesThis (n: int) =
    """{"number":"""
    + string n
    + ""","merged":true,"mergedAt":"2026-01-01T00:00:00Z","mergeCommit":{"abbreviatedOid":"c"""
    + string n
    + """"},"closingIssuesReferences":{"nodes":[{"number":350,"repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}"""

[<Fact>]
let ``a MERGED closing PR is read as a closing PR`` () =
    let transport =
        serving (response "CLOSED" (closesThis 399) "" noSubs "null")

    match facts transport board ref with
    | Ok f ->
        Assert.Equal<int list>([ 399 ], f.ClosingPrs |> List.map (fun p -> p.Number))
        Assert.True((List.head f.ClosingPrs).Merged)
        Assert.True((List.head f.ClosingPrs).ClosesThis)
    | Error e -> failwith $"the merged PR must be read — got %A{e}"

[<Fact>]
let ``an UNMERGED PR that references the issue does NOT close it`` () =
    // ONLY MERGED PRs COUNT. An OPEN or ABANDONED pull request that merely references the issue has closed
    // NOTHING — and treating it as the closing act would stamp work done on the strength of a PR somebody
    // threw away, or has not finished. The node is READ (so provenance stays whole), but `verify` filters it.
    let transport =
        serving (response "CLOSED" """{"number":399,"merged":false,"closingIssuesReferences":{"nodes":[{"number":350,"repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}""" "" noSubs "null")

    match facts transport board ref with
    | Ok f ->
        // No MERGED closer is present, so `verify` refuses (unless #600 evidence is given).
        Assert.DoesNotContain(f.ClosingPrs, fun (p: ClosingPr) -> p.Merged)

        match verify None None f with
        | Red _ -> ()
        | other -> failwith $"an unmerged reference does not close the issue — got %A{other}"
    | Error e -> failwith $"parse failed — got %A{e}"

[<Fact>]
let ``the CLOSED_EVENT closer is read WITH its merge facts, and it is not listed (#928)`` () =
    // `gh pr create --fill` maps the commit SUBJECT to the PR TITLE, so a commit whose subject carries the
    // keyword puts it where `closingIssuesReferences` never looks. The event's closer is the record of the ACT.
    //
    // AND THE REFERENCE LIST IS EMPTY — this fixture used to serve `{"number":399,"merged":true}` there, on
    // the premise that "the PR is still LISTED (a merge that closed the issue is)". It is not: GitHub builds
    // that list FROM the body linkage, so a PR whose body never named the issue is absent from it entirely
    // (#928, measured on .github#622). `facts` must therefore read the closer's merge facts out of the EVENT,
    // because there is no listed node to take them from.
    let transport =
        serving
            (response
                "CLOSED"
                ""
                """{"closer":{"__typename":"PullRequest","number":399,"merged":true,"mergedAt":"2026-01-01T00:00:00Z","mergeCommit":{"abbreviatedOid":"c399"}}}"""
                noSubs
                "null")

    match facts transport board ref with
    | Ok f ->
        Assert.Empty f.ClosingPrs
        Assert.Contains(f.CloserPrs, fun (p: ClosingPr) -> p.Number = 399 && p.Merged && p.Oid = "c399")

        match verify None None f with
        | Green(ClosedByPullRequest(399, "c399", _)) -> ()
        | other -> failwith $"the closing ACT must rescue the stamp — got %A{other}"
    | Error e -> failwith $"parse failed — got %A{e}"

[<Fact>]
let ``a COMMIT closer resolves through to its associated PR (#558/#928)`` () =
    // THE .github#622 SHAPE, MEASURED AND SERVED BACK VERBATIM: the reference list is [], the close event
    // names the squash COMMIT, and the commit names the merged PR. This is what GitHub actually returns for
    // every PR this org's own recipe produces, and it is the case #558 was written for — so it is the case
    // the fixture must serve.
    let transport =
        serving
            (response
                "CLOSED"
                ""
                """{"closer":{"__typename":"Commit","oid":"4cf06e10","associatedPullRequests":{"nodes":[{"number":926,"merged":true,"mergedAt":"2026-07-16T20:51:40Z","mergeCommit":{"abbreviatedOid":"4cf06e1"}}]}}}"""
                noSubs
                "null")

    match facts transport board ref with
    | Ok f ->
        Assert.Empty f.ClosingPrs
        Assert.Contains(f.CloserPrs, fun (p: ClosingPr) -> p.Number = 926 && p.Merged)

        match verify None None f with
        | Green(ClosedByPullRequest(926, "4cf06e1", "2026-07-16")) -> ()
        | other -> failwith $"a commit closer must resolve through to its PR — got %A{other}"
    | Error e -> failwith $"parse failed — got %A{e}"

[<Fact>]
let ``an UNMERGED PR associated with the closing commit is read, but does not stamp (#928)`` () =
    // `associatedPullRequests` returns the PRs that CONTAIN the commit — which need not be merged ones. The
    // node is READ (provenance stays whole, exactly as for the reference list), and `verify` refuses it.
    // This is why the read resolves merge facts instead of letting `verify` assume them.
    let transport =
        serving
            (response
                "CLOSED"
                ""
                """{"closer":{"__typename":"Commit","oid":"deadbeef","associatedPullRequests":{"nodes":[{"number":399,"merged":false}]}}}"""
                noSubs
                "null")

    match facts transport board ref with
    | Ok f ->
        Assert.Contains(f.CloserPrs, fun (p: ClosingPr) -> p.Number = 399 && not p.Merged)

        match verify None None f with
        | Red reasons -> Assert.Contains(reasons, fun r -> r.Contains "#399" && r.Contains "MERGED")
        | other -> failwith $"an unmerged associated PR has closed nothing — got %A{other}"
    | Error e -> failwith $"parse failed — got %A{e}"

// ---- the truncation check, at the read ----------------------------------------------------------------

[<Fact>]
let ``a truncated sub-issue page is detected - totalCount disagrees with the nodes`` () =
    // THIS IS WHERE THE TRUNCATION CHECK LIVES, and `verify` trusts it absolutely. `verify` cannot know a
    // page was cut short; it can only act on the `Unverifiable` that `facts` produces. So if `facts` reads
    // truncation wrong, a subject nobody fully saw gets a confident verdict.
    let subs =
        """{"totalCount":120,"nodes":[{"number":398,"state":"CLOSED"}]}"""

    let transport = serving (response "CLOSED" "" "" subs "null")

    match facts transport board ref with
    | Ok f ->
        match f.Children with
        | Unverifiable(120, 1) -> ()
        | other -> failwith $"a 120-vs-1 disagreement is truncation — got %A{other}"

        // AND `verify` REFUSES IT — `NoVerdict`, not a confident green over a subject it could not read.
        match verify None None f with
        | NoVerdict _ -> ()
        | other -> failwith $"a truncated child set must be UNVERIFIED — got %A{other}"
    | Error e -> failwith $"parse failed — got %A{e}"

[<Fact>]
let ``a full sub-issue page with an OPEN child is SomeOpen`` () =
    let subs =
        """{"totalCount":2,"nodes":[{"number":398,"state":"CLOSED"},{"number":401,"state":"OPEN"}]}"""

    let transport = serving (response "CLOSED" "" "" subs "null")

    match facts transport board ref with
    | Ok f ->
        match f.Children with
        | SomeOpen [ 401 ] -> ()
        | other -> failwith $"one open child is SomeOpen [401] — got %A{other}"
    | Error e -> failwith $"parse failed — got %A{e}"

[<Fact>]
let ``all children closed is AllResolved`` () =
    let subs =
        """{"totalCount":2,"nodes":[{"number":398,"state":"CLOSED"},{"number":401,"state":"CLOSED"}]}"""

    let transport = serving (response "CLOSED" "" "" subs "null")

    match facts transport board ref with
    | Ok f -> Assert.Equal(AllResolved 2, f.Children)
    | Error e -> failwith $"parse failed — got %A{e}"

// ---- the parent edge ----------------------------------------------------------------------------------

[<Fact>]
let ``the parent ref is read, cross-repo, so the roll-up can climb`` () =
    let parent =
        """{"number":417,"repository":{"name":".github","owner":{"login":"FS-GG"}}}"""

    let transport = serving (response "CLOSED" """{"number":399,"merged":true}""" "" noSubs parent)

    match facts transport board ref with
    | Ok f ->
        match f.Parent with
        | Some p ->
            Assert.Equal("FS-GG", p.Owner)
            Assert.Equal(".github", p.Repo)
            Assert.Equal(417, p.Number)
        | None -> failwith "the parent edge must be read, or the climb cannot start"
    | Error e -> failwith $"parse failed — got %A{e}"

// ---- failure legs -------------------------------------------------------------------------------------

[<Fact>]
let ``a 200-with-errors rate limit is RateLimited, not a malformed fact set`` () =
    let transport = serving """{"errors":[{"message":"API rate limit exceeded"}]}"""

    match facts transport board ref with
    | Error(RateLimited _) -> ()
    | other -> failwith $"an exhausted budget must propagate — got %A{other}"

[<Fact>]
let ``a null issue is NotFound - the query ran, the issue is not there`` () =
    let transport = serving """{"data":{"repository":{"issue":null}}}"""

    match facts transport board ref with
    | Error(NotFound _) -> ()
    | other -> failwith $"a null issue is a definite absence — got %A{other}"

[<Fact>]
let ``a body that is not JSON is Malformed - never an empty fact set`` () =
    let transport = serving "<html>502</html>"

    match facts transport board ref with
    | Error(Malformed _) -> ()
    | other -> failwith $"unreadable bytes are a failed read, not empty facts — got %A{other}"
