namespace FS.GG.Org.Policy.Tests

open System
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Threading
open System.Threading.Tasks
open FS.GG.Org.Policy
open Xunit

module GitHubProtectedBranchHttpTests =
    let private repo: GitHubProtectedBranchPin.ExactRepository =
        { RepositoryNodeId = "R_fixture_one"; RepositoryFullName = "FS-GG/.github" }
    let private endpoint = "https://api.github.com/repos/FS-GG/.github/branches/main"
    let private branchJson =
        """{"name":"main","commit":{"sha":"539aff7e655d22b1761850cd6be868eecc2886e4"},"protected":true}"""

    type private FixtureHandler(answer: HttpRequestMessage -> HttpResponseMessage) =
        inherit HttpMessageHandler()
        override _.SendAsync(request: HttpRequestMessage, _: CancellationToken) =
            Task.FromResult(answer request)

    let private response status media body (request: HttpRequestMessage) =
        let result = new HttpResponseMessage(enum<HttpStatusCode> status)
        result.RequestMessage <- request
        result.Content <- new StringContent(body, Encoding.UTF8)
        result.Content.Headers.ContentType <- MediaTypeHeaderValue(media)
        result

    let private fixtureReader answer =
        match GitHubProtectedBranchHttp.Reader.ForFixture("fixture_token", new FixtureHandler(answer)) with
        | Ok reader -> reader
        | Error () -> failwith "fixture credential refused"

    let private refused answer =
        use reader = fixtureReader answer
        match GitHubProtectedBranchPin.inspectProvisionalPin repo reader with
        | Error diagnostic -> Assert.Equal("github-protected-pin", diagnostic.Code)
        | Ok pin -> failwithf "unsafe HTTP branch response produced pin: %A" pin

    [<Fact>]
    let ``fixed authenticated GET yields protected commit pin`` () =
        use reader =
            fixtureReader (fun request ->
                Assert.Equal(HttpMethod.Get, request.Method)
                Assert.Equal(endpoint, request.RequestUri.AbsoluteUri)
                Assert.Null(request.Content)
                Assert.Equal("Bearer", request.Headers.Authorization.Scheme)
                Assert.Equal("fixture_token", request.Headers.Authorization.Parameter)
                Assert.Contains("2026-03-10", request.Headers.GetValues("X-GitHub-Api-Version"))
                let result = response 200 "application/json" branchJson request
                result.Content.Headers.ContentType.CharSet <- "utf-8"
                result)
        match GitHubProtectedBranchPin.inspectProvisionalPin repo reader with
        | Error diagnostic -> failwithf "protected fixture refused: %A" diagnostic
        | Ok pin -> Assert.Equal("539aff7e655d22b1761850cd6be868eecc2886e4", pin.CommitId)

    [<Theory>]
    [<InlineData(301)>]
    [<InlineData(401)>]
    [<InlineData(404)>]
    [<InlineData(429)>]
    [<InlineData(500)>]
    let ``non 200 branch response refuses even with valid protected data`` status =
        refused (fun request ->
            let result = response status "application/json" branchJson request
            if status = 301 then result.Headers.Location <- Uri("https://foreign.example/branch")
            result)

    [<Fact>]
    let ``foreign final origin and Location on 200 refuse`` () =
        refused (fun request ->
            let result = response 200 "application/json" branchJson request
            result.RequestMessage <- new HttpRequestMessage(HttpMethod.Get, "https://foreign.example/branch")
            result)
        refused (fun request ->
            let result = response 200 "application/json" branchJson request
            result.Headers.Location <- Uri("https://foreign.example/branch")
            result)

    [<Fact>]
    let ``non JSON oversized and underreported body refuse`` () =
        refused (response 200 "text/html" branchJson)
        refused (response 200 "application/json" (String.replicate 65537 "x"))
        refused (fun request ->
            let result = response 200 "application/json" (String.replicate 65537 "x") request
            result.Content.Headers.ContentLength <- Nullable 1L
            result)

    [<Fact>]
    let ``valid protected JSON with false declared length cannot mint pin`` () =
        let actualLength = int64 (Encoding.UTF8.GetByteCount(branchJson))
        for declaredLength in [ actualLength - 1L; actualLength + 1L ] do
            refused (fun request ->
                let result = response 200 "application/json" branchJson request
                result.Content.Headers.ContentLength <- Nullable declaredLength
                result)

    [<Fact>]
    let ``declared gzip over plain protected JSON cannot mint pin`` () =
        refused (fun request ->
            let result = response 200 "application/json" branchJson request
            result.Content.Headers.ContentEncoding.Add("gzip")
            result)

    [<Fact>]
    let ``foreign declared charset over valid protected JSON cannot mint pin`` () =
        refused (fun request ->
            let result = response 200 "application/json" branchJson request
            result.Content.Headers.ContentType.CharSet <- "iso-8859-1"
            result)

    [<Fact>]
    let ``blank credential and foreign request URL never send`` () =
        match GitHubProtectedBranchHttp.Reader.ForFixture("", new FixtureHandler(response 200 "application/json" branchJson)) with
        | Error () -> ()
        | Ok reader ->
            reader.Dispose()
            failwith "blank credential accepted"
        let mutable calls = 0
        use reader = fixtureReader (fun request ->
            calls <- calls + 1
            response 200 "application/json" branchJson request)
        let foreign: GitHubProtectedBranchPin.ExactRequest =
            { Owner = "FS-GG"; Name = ".github"; Branch = "main"
              Url = "https://foreign.example/repos/FS-GG/.github/branches/main" }
        match (reader :> GitHubProtectedBranchPin.IReadOnlyProtectedBranchReader).ReadExact foreign with
        | Error () -> Assert.Equal(0, calls)
        | Ok _ -> failwith "foreign URL reached protected branch reader"
        let dotSegment = { foreign with Url = "https://api.github.com/repos/../FS-GG/.github/branches/main" }
        match (reader :> GitHubProtectedBranchPin.IReadOnlyProtectedBranchReader).ReadExact dotSegment with
        | Error () -> Assert.Equal(0, calls)
        | Ok _ -> failwith "path escape reached protected branch reader"

    [<Fact>]
    let ``malformed bearer credentials cannot construct branch reader`` () =
        for token in [ "token with space"; "token:scope"; "token;scope"; "token@scope" ] do
            match GitHubProtectedBranchHttp.Reader.ForFixture(
                token, new FixtureHandler(response 200 "application/json" branchJson)) with
            | Error () -> ()
            | Ok reader ->
                reader.Dispose()
                failwithf "malformed bearer credential constructed branch reader: %s" token

    [<Fact>]
    let ``authenticated transport does not turn unprotected JSON into pin`` () =
        refused (response 200 "application/vnd.github+json" (branchJson.Replace("true", "false")))
