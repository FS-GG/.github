namespace FS.GG.Coord.GitHub.Tests

open System
open System.Collections.Generic
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.HistoricalLossRetainedNativeCensus

module HistoricalLossRetainedNativeCensusTests =

    let private api = "https://api.github.test"
    let private at = "2026-09-27T00:00:00Z"
    let private horizon = "2026-09-28T00:00:00Z"
    let private head = String.replicate 40 "a"
    let private digest = String.replicate 64 "b"

    let private ok body next =
        { Status = 200
          Body = body
          Headers = Map [ "X-RateLimit-Resource", "core" ]
          ETag = None
          NextLink = next }

    let private query name (request: Request) =
        request.Query |> List.find (fst >> (=) name) |> snd

    let private repoForPath (path: string) =
        repositories
        |> List.find (fun repository -> path.StartsWith("repos/" + repository.FullName, StringComparison.Ordinal))

    let private issue id node number body isPull =
        let pull = if isPull then ",\"pull_request\":{}" else ""
        $"{{\"id\":{id},\"node_id\":\"{node}\",\"number\":{number},\"created_at\":\"{at}\",\"body\":{System.Text.Json.JsonSerializer.Serialize body}{pull}}}"

    let private comment id node number body repo =
        $"{{\"id\":{id},\"node_id\":\"{node}\",\"created_at\":\"{at}\",\"body\":{System.Text.Json.JsonSerializer.Serialize body},\"issue_url\":\"{api}/repos/{repo}/issues/{number}\"}}"

    type Fixture(?statusFor: Request -> int option, ?nextFor: Request -> string option option, ?bodyFor: (Request * int) -> string -> string) =
        let requests = ResizeArray<Request>()
        let calls = Dictionary<string, int>()
        let statusFor = defaultArg statusFor (fun _ -> None)
        let nextFor = defaultArg nextFor (fun _ -> None)
        let bodyFor = defaultArg bodyFor (fun _ body -> body)

        let defaultBody (request: Request) =
            let repository = repoForPath request.Path
            let page = request.Query |> List.tryFind (fst >> (=) "page") |> Option.map snd |> Option.defaultValue "1"
            let root = "repos/" + repository.FullName

            if request.Path = root then
                $"{{\"id\":{repository.DatabaseId},\"node_id\":\"{repository.NodeId}\",\"full_name\":\"{repository.FullName}\"}}"
            elif request.Path.EndsWith("/timeline", StringComparison.Ordinal) then
                "[{\"id\":9001}]"
            elif request.Path = root + "/issues" then
                if repository.FullName = "FS-GG/.github" then
                    "[" + issue 1001 "I_gh1" 1 "ordinary PR body" true + "]"
                elif repository.FullName = "FS-GG/Rendering" then
                    let marker = $"<!-- fsgg:intake:v1 id=rendering-2 digest={digest} -->\nwork item"
                    "[" + issue 2001 "I_rendering2" 2 marker false + "]"
                else "[]"
            elif request.Path = root + "/pulls" then
                if repository.FullName = "FS-GG/.github" then "[{\"id\":3001,\"number\":1}]" else "[]"
            elif request.Path = root + "/issues/comments" then
                if repository.FullName = "FS-GG/.github" then
                    let receipt = $"<!-- fsgg:delivery-receipt id=kit-1 head={head} evidence=https://example.test/a -->\n\nproof"
                    "[" + comment 4001 "IC_delivery" 1 receipt repository.FullName + "," + comment 4002 "IC_nonreceipt" 1 "quoted receipt example" repository.FullName + "]"
                elif repository.FullName = "FS-GG/Rendering" then
                    "[" + comment 5001 "IC_done" 2 "<!-- fsgg:done-receipt v=1 -->\nverified" repository.FullName + "]"
                else "[]"
            elif request.Path = root + "/issues/events" && repository.FullName = "FS-GG/.github" && page = "1" then
                [ 1 .. 100 ] |> List.map (fun id -> $"{{\"id\":{6000 + id}}}") |> String.concat "," |> fun rows -> "[" + rows + "]"
            else "[]"

        interface ISinglePageGitHubTransport with
            member _.SendSingle request =
                requests.Add request
                let count = calls.GetValueOrDefault(request.Path, 0) + 1
                calls.[request.Path] <- count
                let body = bodyFor (request, count) (defaultBody request)

                match statusFor request with
                | Some status ->
                    Ok { (ok "{}" None) with Status = status }
                | None ->
                    let defaultNext =
                        if request.Path = "repos/FS-GG/.github/issues/events" && query "page" request = "1" then
                            Some $"{api}/repos/FS-GG/.github/issues/events?per_page=100&page=2"
                        else None
                    let next = defaultArg (nextFor request) defaultNext
                    Ok(ok body next)

        member _.Requests = List.ofSeq requests

    [<Fact>]
    let ``two stable native passes retain exact pages and derive only source-bound receipts`` () =
        let fixture = Fixture()

        match collectTwoPass fixture api horizon with
        | Error error -> failwithf "%A" error
        | Ok capture ->
            Assert.Equal(9, capture.First.Draft.Repositories.Length)
            Assert.Equal(3, capture.First.Draft.Subjects.Length)
            Assert.True(capture.First.Draft.Subjects = capture.Second.Draft.Subjects)
            Assert.Equal(capture.First.Draft.EvidenceFingerprint, capture.Second.Draft.EvidenceFingerprint)
            Assert.Contains(capture.First.Draft.Subjects, fun item -> item.Family = DeliveryReceipt && item.NativeId = "IC_delivery")
            Assert.Contains(capture.First.Draft.Subjects, fun item -> item.Family = IntakeReceipt && item.Origin = IssueBody)
            Assert.Contains(capture.First.Draft.Subjects, fun item -> item.Family = LegacyDoneReceipt && item.NativeId = "IC_done")
            Assert.DoesNotContain(capture.First.Draft.Subjects, fun item -> item.NativeId = "IC_nonreceipt")
            Assert.Contains(capture.First.Pages, fun page -> page.Stream = IssueEvents && page.Index = 2 && page.ItemCount = 0 && page.Terminal)
            Assert.Equal(2, capture.First.Pages |> List.filter (fun page -> match page.Stream with Timeline _ -> true | _ -> false) |> List.length)
            Assert.All(fixture.Requests, fun request -> Assert.Equal("GET", request.Method); Assert.Equal(NoBody, request.Body))

    [<Theory>]
    [<InlineData(401)>]
    [<InlineData(403)>]
    [<InlineData(404)>]
    let ``bounded authorization and absence statuses refuse instead of becoming empty streams`` status =
        let fixture = Fixture(statusFor = (fun request -> if request.Path.EndsWith("/issues", StringComparison.Ordinal) then Some status else None))
        match collectTwoPass fixture api horizon, status with
        | Error(Unauthorized _), 401
        | Error(Forbidden _), 403
        | Error(NotFound _), 404 -> ()
        | actual -> failwithf "unexpected result %A" actual

    [<Fact>]
    let ``foreign continuation refuses before a second page request`` () =
        let fixture = Fixture(nextFor = (fun request ->
            if request.Path = "repos/FS-GG/.github/issues/events" then Some(Some "https://evil.invalid/repos/FS-GG/.github/issues/events?per_page=100&page=2")
            else None))
        match collectTwoPass fixture api horizon with
        | Error(UnsafeContinuation _) -> ()
        | actual -> failwithf "unexpected result %A" actual

    [<Fact>]
    let ``marker-shaped malformed comment is rejected and never becomes a draft row`` () =
        let fixture = Fixture(bodyFor = (fun (request, _) body ->
            if request.Path = "repos/FS-GG/.github/issues/comments" then
                "[" + comment 4001 "IC_bad" 1 "<!-- fsgg:delivery-receipt id=kit head=NOT-A-SHA evidence=https://example.test/a -->" "FS-GG/.github" + "]"
            else body))
        match collectTwoPass fixture api horizon with
        | Error(MalformedCandidate _) -> ()
        | actual -> failwithf "unexpected result %A" actual

    [<Fact>]
    let ``duplicate native ids and changed second pass both fail closed`` () =
        let duplicate = Fixture(bodyFor = (fun (request, _) body ->
            if request.Path = "repos/FS-GG/Audio/issues/events" then "[{\"id\":1},{\"id\":1}]" else body))
        match collectTwoPass duplicate api horizon with
        | Error(DuplicateNativeId _) -> ()
        | actual -> failwithf "duplicate was not refused: %A" actual

        let drifting = Fixture(bodyFor = (fun (request, count) body ->
            if request.Path = "repos/FS-GG/.github/issues/comments" && count = 2 then body.Replace("proof", "changed proof") else body))
        match collectTwoPass drifting api horizon with
        | Error(PassDrift _) -> ()
        | actual -> failwithf "drift was not refused: %A" actual
