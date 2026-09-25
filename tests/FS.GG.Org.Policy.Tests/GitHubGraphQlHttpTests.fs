namespace FS.GG.Org.Policy.Tests

open System
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FS.GG.Org.Policy
open Xunit

module GitHubGraphQlHttpTests =
    let private pin: GitCommitProvenance.ExactCommitPin =
        { RepositoryNodeId = "R_fixture_one"
          RepositoryFullName = "FS-GG/.github"
          CommitId = "539aff7e655d22b1761850cd6be868eecc2886e4" }
    let private rootTreeId = "2b552d6bf9d7b4458a28fc65663fbc2c7b0221dc"
    let private valid =
        """{"data":{"repository":{"id":"R_fixture_one","nameWithOwner":"FS-GG/.github","object":{"__typename":"Commit","oid":"539aff7e655d22b1761850cd6be868eecc2886e4","tree":{"oid":"2b552d6bf9d7b4458a28fc65663fbc2c7b0221dc"}}}}}"""

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
        match GitHubGraphQlHttp.Reader.ForFixture("fixture_token", new FixtureHandler(answer)) with
        | Ok reader -> reader
        | Error () -> failwith "fixed fixture token was refused"

    let private refused answer =
        use reader = fixtureReader answer
        match GitHubCommitMembership.inspectProvisionalMembership pin rootTreeId reader with
        | Error diagnostic -> Assert.Equal("github-commit-membership", diagnostic.Code)
        | Ok fact -> failwithf "unsafe HTTP response produced membership: %A" fact

    [<Fact>]
    let ``fixed authenticated POST carries variables and accepts exact 200 JSON`` () =
        use reader =
            fixtureReader (fun request ->
                Assert.Equal(HttpMethod.Post, request.Method)
                Assert.Equal("https://api.github.com/graphql", request.RequestUri.AbsoluteUri)
                Assert.Equal("Bearer", request.Headers.Authorization.Scheme)
                Assert.Equal("fixture_token", request.Headers.Authorization.Parameter)
                use payload = JsonDocument.Parse(request.Content.ReadAsStringAsync().GetAwaiter().GetResult())
                let root = payload.RootElement
                let query = root.GetProperty("query").GetString()
                Assert.Contains("followRenames: false", query)
                Assert.Contains("object(oid: $commit)", query)
                let variables = root.GetProperty("variables")
                Assert.Equal("FS-GG", variables.GetProperty("owner").GetString())
                Assert.Equal(".github", variables.GetProperty("name").GetString())
                Assert.Equal(pin.CommitId, variables.GetProperty("commit").GetString())
                response 200 "application/json" valid request)
        match GitHubCommitMembership.inspectProvisionalMembership pin rootTreeId reader with
        | Error diagnostic -> failwithf "exact HTTP fixture refused: %A" diagnostic
        | Ok fact -> Assert.Equal(rootTreeId, fact.TreeId)

    [<Fact>]
    let ``GitHub JSON vendor media type is accepted`` () =
        use reader = fixtureReader (response 200 "application/vnd.github+json" valid)
        match GitHubCommitMembership.inspectProvisionalMembership pin rootTreeId reader with
        | Error diagnostic -> failwithf "GitHub JSON response refused: %A" diagnostic
        | Ok fact -> Assert.Equal(rootTreeId, fact.TreeId)

    [<Theory>]
    [<InlineData(302)>]
    [<InlineData(401)>]
    [<InlineData(429)>]
    [<InlineData(500)>]
    let ``non 200 status refuses even with valid GraphQL data`` status =
        refused (fun request ->
            let result = response status "application/json" valid request
            if status = 302 then result.Headers.Location <- Uri("https://foreign.example/graphql")
            result)

    [<Fact>]
    let ``foreign response origin and non JSON media type refuse`` () =
        refused (fun request ->
            let result = response 200 "application/json" valid request
            result.RequestMessage <- new HttpRequestMessage(HttpMethod.Post, "https://foreign.example/graphql")
            result)
        refused (response 200 "text/html" valid)
        refused (fun request ->
            let result = response 200 "application/json" valid request
            result.Headers.Location <- Uri("https://foreign.example/graphql")
            result)

    [<Fact>]
    let ``oversized or absent body refuses before GraphQL parsing`` () =
        refused (response 200 "application/json" (String.replicate 65537 "x"))
        refused (fun request ->
            let result = response 200 "application/json" (String.replicate 65537 "x") request
            result.Content.Headers.ContentLength <- Nullable 1L
            result)
        refused (response 200 "application/json" "")

    [<Fact>]
    let ``blank credential and mutation document cannot reach HTTP handler`` () =
        match GitHubGraphQlHttp.Reader.ForFixture("", new FixtureHandler(response 200 "application/json" valid)) with
        | Error () -> ()
        | Ok reader ->
            reader.Dispose()
            failwith "blank credential accepted"
        let mutable calls = 0
        use reader = fixtureReader (fun request ->
            calls <- calls + 1
            response 200 "application/json" valid request)
        let unsafeRequest: GitHubCommitMembership.ExactRequest =
            { Document = "mutation { deleteRepository(input: {}) { clientMutationId } }"
              Owner = "FS-GG"; Name = ".github"; CommitId = pin.CommitId }
        match (reader :> GitHubCommitMembership.IReadOnlyGraphQlReader).ExecuteExact unsafeRequest with
        | Error () -> Assert.Equal(0, calls)
        | Ok _ -> failwith "mutation passed read-only transport"
