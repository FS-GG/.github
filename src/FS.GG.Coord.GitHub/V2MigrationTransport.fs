namespace FS.GG.Coord.GitHub

module V2MigrationTransport =
    open System
    open System.Diagnostics
    open System.IO
    open System.Net.Http
    open System.Net.Http.Headers
    open System.Text
    open System.Text.Json
    open System.Text.Json.Nodes
    open System.Threading
    open Errors
    open Transport

    type IConnection =
        inherit IDisposable
        abstract Reads: IGitHubTransport
        abstract RemainingTime: TimeSpan
        abstract ObservationReads: planningWindowUntil: DateTimeOffset -> IGitHubTransport
        abstract Dispatch: prepared: V2Migration.Prepared * change: V2Migration.Change * planningWindowUntil: DateTimeOffset -> IoResult<Transport.Response>

    let private refused detail = Error(Malformed("Project 3 migration transport", detail))
    let validateClearEnvelope (body: string) : IoResult<unit> =
        match GraphQlEnvelope.classifyMigrationClear body with
        | GraphQlEnvelope.ClearSuccess -> Ok()
        | GraphQlEnvelope.ClearErrors ->
            refused "native GraphQL response has errors or malformed errors; clear success unavailable"
        | GraphQlEnvelope.ClearMalformed ->
            refused "native GraphQL response envelope is malformed; clear success unavailable"

    let private composition (read: Transport.Request -> TimeSpan -> DateTimeOffset -> IoResult<Transport.Response>)
                            (once: Transport.Request -> TimeSpan -> DateTimeOffset -> IoResult<Transport.Response>)
                            (dispose: unit -> unit) : IConnection =
        let clock = Stopwatch.StartNew()
        let mutable calls = 0
        let mutable disposed = false
        let invoke (request: Transport.Request) (windowUntil: DateTimeOffset option) (action: Transport.Request -> TimeSpan -> DateTimeOffset -> IoResult<Transport.Response>) =
            let remaining = TimeSpan.FromSeconds 270.0 - clock.Elapsed
            let now = DateTimeOffset.UtcNow
            let deadline = min (now + min remaining (TimeSpan.FromSeconds 30.0)) (windowUntil |> Option.defaultValue DateTimeOffset.MaxValue)
            if disposed || calls >= 500 || remaining <= TimeSpan.Zero || deadline <= now then refused "shared request/deadline ceiling reached; no dispatch"
            else
                calls <- calls + 1
                action request (deadline - now) deadline
        let reads (windowUntil: DateTimeOffset option) =
            { new IGitHubTransport with
                member _.Send(request: Transport.Request) =
                    match request.Body with
                    | Query(document, _) when request.Method = "POST" && request.Path = "graphql"
                                              && request.Query.IsEmpty && request.IfNoneMatch.IsNone
                                              && request.Budget = GraphQl && document.StartsWith("query", StringComparison.Ordinal)
                                              && not (document.Contains("mutation", StringComparison.Ordinal))
                                              && not (document.Contains("subscription", StringComparison.Ordinal)) ->
                        invoke request windowUntil read |> Result.bind (fun response ->
                            if response.Status <> 200 then refused "native read returned non-success HTTP status; snapshot incomplete"
                            else validateClearEnvelope response.Body |> Result.map (fun () -> response))
                    | _ -> refused "only fixed GraphQL query reads are allowed"
                member _.SendMutation(_: MutationIntent) = refused "effects require the typed migration reconstruction edge"
                member _.RetryMutation(_: string) = refused "migration never retries or replays a mutation" }
        { new IConnection with
            member _.RemainingTime =
                if disposed then TimeSpan.Zero
                else max TimeSpan.Zero (TimeSpan.FromSeconds 270.0 - clock.Elapsed)
            member _.Reads = reads None
            member _.ObservationReads(planningWindowUntil: DateTimeOffset) = reads (Some planningWindowUntil)
            member _.Dispatch(prepared: V2Migration.Prepared, change: V2Migration.Change, planningWindowUntil: DateTimeOffset) =
                V2Migration.request prepared change |> Result.bind (fun request -> invoke request (Some planningWindowUntil) once)
            member _.Dispose() =
                if not disposed then
                    disposed <- true
                    dispose() }

    let compose (reads: IGitHubTransport) (nativeOnce: Transport.Request -> IoResult<Transport.Response>) : IConnection =
        composition (fun request _ _ -> reads.Send request) (fun request _ _ -> nativeOnce request) ignore

    let private body (request: Transport.Request) =
        match request.Body with
        | Json json -> Ok json
        | Query(document, variables) ->
            let values = JsonObject()
            for name, value in variables do
                let node: JsonNode =
                    match value with
                    | VString text | VId text -> JsonValue.Create text
                    | VDate date -> JsonValue.Create date
                    | VNumber number -> JsonValue.Create number
                values.Add(name, node)
            let payload = JsonObject()
            payload.Add("query", JsonValue.Create document)
            payload.Add("variables", values)
            Ok(payload.ToJsonString())
        | NoBody -> refused "missing fixed GraphQL body"

    let createWithHandler (token: string) (handler: HttpMessageHandler) : IoResult<IConnection> =
        if String.IsNullOrWhiteSpace token || token.Length > 16384 || (token |> Seq.exists Char.IsControl) then refused "native credential is missing; no capability or approval inferred"
        else
            let client = new HttpClient(handler, true)
            client.Timeout <- Timeout.InfiniteTimeSpan
            client.DefaultRequestHeaders.UserAgent.ParseAdd "FS.GG.Coord.Migration/1"
            client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", token)
            let send (request: Transport.Request) (remaining: TimeSpan) (deadline: DateTimeOffset) : IoResult<Transport.Response> =
                let acquisitionClock = Stopwatch.StartNew()
                match body request with
                | Error error -> Error error
                | Ok json when Encoding.UTF8.GetByteCount json > 262144 -> refused "request exceeds 256 KiB; not sent"
                | Ok json ->
                    let allowance = min (remaining - acquisitionClock.Elapsed) (deadline - DateTimeOffset.UtcNow)
                    if allowance <= TimeSpan.Zero then refused "selected window/deadline expired before native acquisition; not sent"
                    else
                        try
                            use cancellation = new CancellationTokenSource(allowance)
                            use message = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/graphql")
                            message.Content <- new StringContent(json, Encoding.UTF8, "application/json")
                            if DateTimeOffset.UtcNow >= deadline || acquisitionClock.Elapsed >= remaining then
                                raise (OperationCanceledException "native acquisition deadline reached")
                            // Exactly one acquisition. No redirect, Link following, automatic retry or new budget.
                            use response = client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellation.Token).GetAwaiter().GetResult()
                            use stream = response.Content.ReadAsStreamAsync(cancellation.Token).GetAwaiter().GetResult()
                            use captured = new MemoryStream()
                            let buffer = Array.zeroCreate<byte> 8192
                            let mutable doneReading = false
                            let mutable overflow = false
                            while not doneReading && not overflow do
                                let count = stream.ReadAsync(buffer.AsMemory(), cancellation.Token).AsTask().GetAwaiter().GetResult()
                                if count = 0 then doneReading <- true
                                elif captured.Length + int64 count > 4L * 1024L * 1024L then overflow <- true
                                else captured.Write(buffer, 0, count)
                            if overflow then refused "response acquisition exceeded 4 MiB after possible send; Unknown; readback only"
                            else
                                let utf8 = UTF8Encoding(false, true)
                                let payload = utf8.GetString(captured.ToArray())
                                Ok ({ Status = int response.StatusCode; Body = payload; Headers = Map.empty; ETag = None; NextLink = None })
                        with
                        | :? OperationCanceledException -> refused "shared remaining deadline expired after possible send; Unknown; readback only"
                        | :? HttpRequestException | :? IOException | :? DecoderFallbackException ->
                            refused "bounded response acquisition failed after possible send; Unknown; readback only"
            let read (request: Transport.Request) (remaining: TimeSpan) (deadline: DateTimeOffset) : IoResult<Transport.Response> =
                send request remaining deadline |> Result.bind (fun response ->
                    if response.Status = 200 then Ok response
                    else refused "native read returned non-success HTTP status; snapshot incomplete")
            Ok(composition read send (fun () -> client.Dispose()))

    let createLive (token: string) : IoResult<IConnection> =
        createWithHandler token (new HttpClientHandler(AllowAutoRedirect = false))
