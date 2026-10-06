namespace FS.GG.Telemetry.Tests

open System
open System.Net
open System.IO
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open FS.GG.Coord
open FS.GG.Telemetry
open Xunit

module DirectResponsesTests =
    let private bytes (value: string) = Encoding.UTF8.GetBytes value
    let private frozen () =
        let request: NativeResponses.Request =
            { Instructions = "Synthetic fixture. No tools."
              InputText = "Retained synthetic evidence only."
              SchemaName = "assessment"
              SchemaJson = bytes "{\"type\":\"object\",\"properties\":{\"result\":{\"type\":\"string\"}},\"required\":[\"result\"],\"additionalProperties\":false}" }
        match NativeResponses.freeze request with Ok value -> value | Error errors -> failwith (String.concat ";" errors)
    let private counted amount = "{\"object\":\"response.input_tokens\",\"input_tokens\":" + amount + "}"
    let private response status =
        "{\"id\":\"provider_response_fixture\",\"object\":\"response\",\"model\":\"gpt-6.1-sol\",\"status\":\"" + status
        + "\",\"output\":[{\"type\":\"message\",\"role\":\"assistant\",\"status\":\"completed\",\"content\":[{\"type\":\"output_text\",\"text\":\"{\\\"result\\\":\\\"synthetic\\\"}\"}]}],\"usage\":{\"input_tokens\":17,\"output_tokens\":7,\"total_tokens\":24}}"
    type private Handler(reply: HttpRequestMessage -> CancellationToken -> Task<HttpResponseMessage>) =
        inherit HttpMessageHandler()
        override _.SendAsync(request, token) = reply request token
    let private answer status body =
        new HttpResponseMessage(status, Content = new StringContent(body, Encoding.UTF8, "application/json"))
    let private clients reply () = new HttpClient(new Handler(reply), true)

    [<Fact>]
    let ``one count and generation retain exact request bytes and opaque response identity`` () = task {
        let calls = ResizeArray<Uri * byte array>()
        let request = frozen ()
        let reply (message: HttpRequestMessage) (token: CancellationToken) = task {
            Assert.Equal(HttpVersion.Version11, message.Version)
            Assert.Equal(HttpVersionPolicy.RequestVersionExact, message.VersionPolicy)
            Assert.Equal(Nullable true, message.Headers.ConnectionClose)
            Assert.Equal(Nullable false, message.Headers.ExpectContinue)
            Assert.Equal("Bearer", message.Headers.Authorization.Scheme)
            let! body = message.Content.ReadAsByteArrayAsync token
            calls.Add(message.RequestUri, body)
            return answer HttpStatusCode.OK (if calls.Count = 1 then counted "17" else response "completed")
        }
        let! outcome = DirectResponses.executeForTest (clients reply) (DirectResponses.beginPhase()) request "synthetic-only" CancellationToken.None
        Assert.Empty outcome.Failure
        Assert.Empty outcome.CleanupFailure
        Assert.Equal(2, calls.Count)
        Assert.Equal("https://api.openai.com/v1/responses/input_tokens", (fst calls.[0]).AbsoluteUri)
        Assert.Equal("https://api.openai.com/v1/responses", (fst calls.[1]).AbsoluteUri)
        Assert.Equal<byte>(request.CountBody, snd calls.[0])
        Assert.Equal<byte>(request.GenerationBody, snd calls.[1])
        Assert.True outcome.CountBodyComplete
        Assert.True outcome.GenerationBodyComplete
        Assert.Equal("provider_response_fixture", outcome.Response.Value.ResponseId.Value.Value)
        let original = outcome.GenerationResponseBody
        original.[0] <- 0uy
        Assert.NotEqual(0uy, outcome.GenerationResponseBody.[0])
    }

    [<Theory>]
    [<InlineData("8001")>]
    [<InlineData("0")>]
    [<InlineData("1.5")>]
    [<InlineData("null")>]
    let ``denied count never invokes generation`` amount = task {
        let mutable calls = 0
        let reply (_: HttpRequestMessage) (_: CancellationToken) =
            calls <- calls + 1
            Task.FromResult(answer HttpStatusCode.OK (counted amount))
        let! outcome = DirectResponses.executeForTest (clients reply) (DirectResponses.beginPhase()) (frozen()) "synthetic-only" CancellationToken.None
        Assert.Equal(1, calls)
        Assert.NotEmpty outcome.Failure
        Assert.Equal(DirectResponses.CountSent, outcome.Stage)
        Assert.Empty outcome.GenerationResponseBody
    }

    [<Theory>]
    [<InlineData(302)>]
    [<InlineData(401)>]
    [<InlineData(429)>]
    [<InlineData(500)>]
    let ``count redirect auth quota server failure never retry or generate`` status = task {
        let mutable calls = 0
        let reply (_: HttpRequestMessage) (_: CancellationToken) =
            calls <- calls + 1
            Task.FromResult(answer (enum<HttpStatusCode> status) (counted "17"))
        let! outcome = DirectResponses.executeForTest (clients reply) (DirectResponses.beginPhase()) (frozen()) "synthetic-only" CancellationToken.None
        Assert.Equal(1, calls)
        Assert.Equal(Some status, outcome.CountStatus)
        Assert.True outcome.CountBodyComplete
        Assert.NotEmpty outcome.Failure
    }

    [<Fact>]
    let ``generation transport ambiguity consumes phase without retry`` () = task {
        let mutable calls = 0
        let reply (_: HttpRequestMessage) (_: CancellationToken) =
            calls <- calls + 1
            if calls = 1 then Task.FromResult(answer HttpStatusCode.OK (counted "17"))
            else Task.FromException<HttpResponseMessage>(HttpRequestException "synthetic ambiguous send")
        let phase = DirectResponses.beginPhase()
        let! first = DirectResponses.executeForTest (clients reply) phase (frozen()) "synthetic-only" CancellationToken.None
        let! second = DirectResponses.executeForTest (clients reply) phase (frozen()) "synthetic-only" CancellationToken.None
        Assert.Equal(2, calls)
        Assert.Equal(DirectResponses.GenerationSent, first.Stage)
        Assert.Contains("responses-transport-unknown", first.Failure)
        Assert.Contains("responses-phase-already-consumed", second.Failure)
        Assert.True first.Response.IsNone
    }

    [<Fact>]
    let ``cancelled original token cannot start count`` () = task {
        let mutable calls = 0
        let reply (_: HttpRequestMessage) (_: CancellationToken) =
            calls <- calls + 1
            Task.FromResult(answer HttpStatusCode.OK (counted "17"))
        use cancellation = new CancellationTokenSource()
        cancellation.Cancel()
        let! result = DirectResponses.executeForTest (clients reply) (DirectResponses.beginPhase()) (frozen()) "synthetic-only" cancellation.Token
        Assert.Equal(0, calls)
        Assert.Equal(DirectResponses.NotSent, result.Stage)
        Assert.NotEmpty result.Failure
    }

    [<Fact>]
    let ``encoded count and oversized count refuse before generation`` () = task {
        for encoded in [ false; true ] do
            let mutable calls = 0
            let reply (_: HttpRequestMessage) (_: CancellationToken) =
                calls <- calls + 1
                let value = answer HttpStatusCode.OK (if encoded then counted "17" else String('x',4097))
                if encoded then value.Content.Headers.ContentEncoding.Add "gzip"
                Task.FromResult value
            let! result = DirectResponses.executeForTest (clients reply) (DirectResponses.beginPhase()) (frozen()) "synthetic-only" CancellationToken.None
            Assert.Equal(1, calls)
            Assert.False result.CountBodyComplete
            Assert.NotEmpty result.Failure
    }

    [<Theory>]
    [<InlineData(200, "incomplete")>]
    [<InlineData(500, "failed")>]
    let ``failed provider response preserves real costs without completion`` status state = task {
        let mutable calls = 0
        let reply (_: HttpRequestMessage) (_: CancellationToken) =
            calls <- calls + 1
            Task.FromResult(if calls = 1 then answer HttpStatusCode.OK (counted "17") else answer (enum<HttpStatusCode> status) (response state))
        let! result = DirectResponses.executeForTest (clients reply) (DirectResponses.beginPhase()) (frozen()) "synthetic-only" CancellationToken.None
        Assert.Equal(2, calls)
        Assert.Equal(DirectResponses.ResponseCaptured, result.Stage)
        Assert.True result.GenerationBodyComplete
        Assert.NotEmpty result.Failure
        Assert.Equal(Some 24L, result.Response.Value.Usage.TotalTokens)
        Assert.True result.Response.Value.Usage.ReasoningOutputTokens.IsNone
    }


    type private FailingRead(prefix: byte array) =
        inherit MemoryStream(prefix, false)
        let mutable read = false
        override this.ReadAsync(destination: Memory<byte>, token: CancellationToken) =
            if read then ValueTask<int>(Task.FromException<int>(IOException "synthetic after-prefix failure"))
            else
                read <- true
                base.ReadAsync(destination, token)

    [<Fact>]
    let ``interrupted response retains prefix and never claims body complete`` () = task {
        let mutable calls = 0
        let prefix = bytes "{\"id\":\"provider_partial\",\"usage\":"
        let reply (_: HttpRequestMessage) (_: CancellationToken) =
            calls <- calls + 1
            if calls = 1 then Task.FromResult(answer HttpStatusCode.OK (counted "17"))
            else
                let content = new StreamContent(new FailingRead(prefix))
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK, Content = content))
        let! result = DirectResponses.executeForTest (clients reply) (DirectResponses.beginPhase()) (frozen()) "synthetic-only" CancellationToken.None
        Assert.Equal(2, calls)
        Assert.Equal<byte>(prefix, result.GenerationResponseBody)
        Assert.False result.GenerationBodyComplete
        Assert.True result.Response.IsNone
        Assert.NotEmpty result.Failure
    }

    [<Theory>]
    [<InlineData("")>]
    [<InlineData("white space")>]
    [<InlineData("line\nfeed")>]
    let ``invalid provider credential never constructs an HTTP client`` key = task {
        let mutable constructed = 0
        let factory () =
            constructed <- constructed + 1
            new HttpClient()
        let! result = DirectResponses.executeForTest factory (DirectResponses.beginPhase()) (frozen()) key CancellationToken.None
        Assert.Equal(0, constructed)
        Assert.Equal(DirectResponses.NotSent, result.Stage)
        Assert.Contains("responses-provider-credential-invalid", result.Failure)
    }
