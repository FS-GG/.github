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

[<Fact>]
let ``a MERGED closing PR is read as a closing PR`` () =
    let transport =
        serving (response "CLOSED" """{"number":399,"merged":true}""" "" noSubs "null")

    match facts transport board ref with
    | Ok f -> Assert.Equal<int list>([ 399 ], f.ClosingPrs)
    | Error e -> failwith $"the merged PR must be read — got %A{e}"

[<Fact>]
let ``an UNMERGED PR that references the issue is NOT a closing PR`` () =
    // ONLY MERGED PRs COUNT. An OPEN or ABANDONED pull request that merely references the issue has closed
    // NOTHING — and treating it as the closing act would stamp work done on the strength of a PR somebody
    // threw away, or has not finished.
    let transport =
        serving (response "CLOSED" """{"number":399,"merged":false}""" "" noSubs "null")

    match facts transport board ref with
    | Ok f ->
        Assert.Empty(f.ClosingPrs)
        // With no closing PR and no closing event, `verify` will (correctly) refuse this unless evidence is
        // supplied — which is the #600 path.
        match verify None f with
        | Red _ -> ()
        | other -> failwith $"an unmerged reference does not close the issue — got %A{other}"
    | Error e -> failwith $"parse failed — got %A{e}"

[<Fact>]
let ``the CLOSED_EVENT closer is read when the PR body never carried the keyword`` () =
    // `gh pr create --fill` maps the commit SUBJECT to the PR TITLE, so a commit whose subject carries the
    // keyword puts it where `closingIssuesReferences` never looks. The event's closer is the record of the
    // ACT, and `facts` has to pull it out of a differently-shaped node.
    let transport =
        serving (response "CLOSED" "" """{"closer":{"__typename":"PullRequest","number":399}}""" noSubs "null")

    match facts transport board ref with
    | Ok f ->
        Assert.Empty(f.ClosingPrs)
        Assert.Equal(Some 399, f.ClosedByEvent)

        match verify None f with
        | Green(ClosedByPullRequest 399) -> ()
        | other -> failwith $"the closing ACT must rescue the stamp — got %A{other}"
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
        match verify None f with
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
