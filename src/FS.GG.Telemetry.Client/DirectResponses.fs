namespace FS.GG.Telemetry

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Net.Sockets
open System.Security.Cryptography
open System.Threading
open System.Threading.Tasks
open FS.GG.Coord

module internal DirectResponses =
    let private countEndpoint = Uri "https://api.openai.com/v1/responses/input_tokens"
    let private generationEndpoint = Uri "https://api.openai.com/v1/responses"
    let private wholeMilliseconds = 60000L
    let private networkMilliseconds = 55000L

    type Phase private (started: int64) =
        let mutable executionSelected = 0
        member _.TryBeginExecution() = Interlocked.CompareExchange(&executionSelected, 1, 0) = 0
        member _.ElapsedMilliseconds = int64 (Stopwatch.GetElapsedTime(started).TotalMilliseconds)
        member this.RemainingMilliseconds = int (max 0L (wholeMilliseconds - this.ElapsedMilliseconds))
        member this.NetworkRemainingMilliseconds = int (max 0L (networkMilliseconds - this.ElapsedMilliseconds))
        static member Begin() = Phase(Stopwatch.GetTimestamp())

    type Stage = NotSent | CountSent | CountAccepted | GenerationSent | ResponseCaptured

    type Outcome private
        (stage: Stage, countStatus: int option, generationStatus: int option,
         countBody: byte array, generationBody: byte array, countComplete: bool, generationComplete: bool, count: NativeResponses.CountAdmission option,
         response: NativeResponses.ResponseObservation option, failure: string list,
         cleanupFailure: string list, elapsed: int64) =
        member _.Stage = stage
        member _.CountStatus = countStatus
        member _.GenerationStatus = generationStatus
        member _.CountResponseBody = Array.copy countBody
        member _.GenerationResponseBody = Array.copy generationBody
        member _.CountBodyComplete = countComplete
        member _.GenerationBodyComplete = generationComplete
        member _.Count = count
        member _.Response = response
        member _.Failure = failure
        member _.CleanupFailure = cleanupFailure
        member _.ElapsedMilliseconds = elapsed
        static member Create(stage, countStatus, generationStatus, countBody, generationBody, countComplete, generationComplete, count, response, failure, cleanupFailure, elapsed) =
            Outcome(stage, countStatus, generationStatus, Array.copy countBody, Array.copy generationBody,
                    countComplete, generationComplete, count, response, failure, cleanupFailure, elapsed)

    let beginPhase () = Phase.Begin()

    let private hash (bytes: byte array) = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

    let private socketsClient (loopback: (int * string) option) =
        let handler = new SocketsHttpHandler()
        handler.AllowAutoRedirect <- false
        handler.UseProxy <- false
        handler.UseCookies <- false
        handler.Credentials <- null
        handler.PreAuthenticate <- false
        handler.AutomaticDecompression <- DecompressionMethods.None
        handler.MaxConnectionsPerServer <- 1
        handler.PooledConnectionLifetime <- TimeSpan.Zero
        handler.MaxResponseHeadersLength <- 16
        match loopback with
        | Some(_, certificateSha256) ->
            handler.SslOptions.RemoteCertificateValidationCallback <-
                System.Net.Security.RemoteCertificateValidationCallback(fun _ certificate _ _ ->
                    not(isNull certificate) && hash(certificate.GetRawCertData()) = certificateSha256)
        | None -> ()
        // A fresh handler gets one connection attempt. A framework retry cannot
        // establish another connection or replay a POST after ambiguous receipt.
        let mutable connected = 0
        handler.ConnectCallback <- Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>>(fun context token ->
            ValueTask<Stream>(task {
                if Interlocked.CompareExchange(&connected, 1, 0) <> 0 then
                    return raise (HttpRequestException "responses-connection-retry-refused")
                else
                    let socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
                    try
                        match loopback with
                        | Some(port, _) -> do! socket.ConnectAsync(IPEndPoint(IPAddress.Loopback, port), token)
                        | None -> do! socket.ConnectAsync(context.DnsEndPoint, token)
                        return new NetworkStream(socket, true) :> Stream
                    with error ->
                        socket.Dispose()
                        return raise error
            }))
        new HttpClient(handler, true, Timeout = Timeout.InfiniteTimeSpan)

    let private productionClient () = socketsClient None

    let createLoopbackClientForTest (port: int) (serverCertificateSha256: string) =
        if port < 1 || port > 65535 || isNull serverCertificateSha256 || serverCertificateSha256.Length <> 64
           || (serverCertificateSha256 |> Seq.exists (fun c -> not(Char.IsAsciiDigit c || (c >= 'a' && c <= 'f')))) then
            invalidArg "serverCertificateSha256" "invalid fixed loopback fixture"
        socketsClient (Some(port, serverCertificateSha256))

    let private readBounded (response: HttpResponseMessage) maximum (token: CancellationToken)
                            (retain: byte array -> unit) =
        task {
            if response.Content.Headers.ContentEncoding.Count <> 0 then
                return Error [ "responses-content-encoding-refused" ]
            elif response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > int64 maximum then
                return Error [ "responses-content-length-bound" ]
            else
                use! stream = response.Content.ReadAsStreamAsync(token)
                use output = new MemoryStream()
                let buffer = Array.zeroCreate<byte> 4096
                let mutable eof = false
                let mutable overflow = false
                try
                    while not eof && not overflow do
                        let! size = stream.ReadAsync(buffer.AsMemory(), token)
                        if size = 0 then eof <- true
                        else
                            let accepted = min size (maximum - int output.Length)
                            if accepted > 0 then output.Write(buffer, 0, accepted)
                            retain (output.ToArray())
                            if accepted <> size then overflow <- true
                    if overflow then return Error [ "responses-body-bound" ]
                    else return Ok(output.ToArray())
                finally
                    retain (output.ToArray())
        }

    let executeForTest (createClient: unit -> HttpClient) (phase: Phase)
                       (request: NativeResponses.FrozenRequest) (providerKey: string)
                       (cancellationToken: CancellationToken) =
        task {
            let mutable stage = NotSent
            let mutable countStatus = None
            let mutable generationStatus = None
            let mutable countBody = Array.empty<byte>
            let mutable generationBody = Array.empty<byte>
            let mutable countComplete = false
            let mutable generationComplete = false
            let mutable count = None
            let mutable decoded = None
            let failures = ResizeArray<string>()
            let cleanup = ResizeArray<string>()

            let send (endpoint: Uri) (body: byte array) (expectedHash: string) (maximum: int)
                     (onInvoked: unit -> unit) (onStatus: int -> unit) (retain: byte array -> unit) (onComplete: unit -> unit) =
                task {
                    if phase.NetworkRemainingMilliseconds <= 0 || cancellationToken.IsCancellationRequested then
                        return Error [ "responses-original-network-deadline" ]
                    elif hash body <> expectedHash then
                        return Error [ "responses-frozen-request-changed" ]
                    else
                        let mutable client: HttpClient = null
                        let mutable response: HttpResponseMessage = null
                        try
                            client <- createClient ()
                            if isNull client then invalidOp "responses-client-unavailable"
                            client.Timeout <- Timeout.InfiniteTimeSpan
                            use deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                            let remaining = phase.NetworkRemainingMilliseconds
                            if remaining <= 0 then
                                return Error [ "responses-original-network-deadline" ]
                            else
                                deadline.CancelAfter remaining
                                use message = new HttpRequestMessage(HttpMethod.Post, endpoint)
                                message.Version <- HttpVersion.Version11
                                message.VersionPolicy <- HttpVersionPolicy.RequestVersionExact
                                message.Headers.ConnectionClose <- Nullable true
                                message.Headers.ExpectContinue <- Nullable false
                                message.Headers.Authorization <- AuthenticationHeaderValue("Bearer", providerKey)
                                message.Content <- new ByteArrayContent(body)
                                message.Content.Headers.ContentType <- MediaTypeHeaderValue "application/json"
                                // This records a local send attempt, never remote-start proof.
                                onInvoked ()
                                let! observed = client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                                response <- observed
                                onStatus (int response.StatusCode)
                                let! received = readBounded response maximum deadline.Token retain
                                match received with
                                | Error errors -> return Error errors
                                | Ok bytes ->
                                    onComplete ()
                                    if response.StatusCode <> HttpStatusCode.OK then
                                        return Error [ "responses-http-status-" + string (int response.StatusCode) ]
                                    else return Ok bytes
                        finally
                            if not (isNull response) then
                                try response.Dispose() with _ -> cleanup.Add "responses-response-disposal-unproved"
                            if not (isNull client) then
                                try client.Dispose() with _ -> cleanup.Add "responses-client-disposal-unproved"
                }

            try
                if obj.ReferenceEquals(phase, null) || obj.ReferenceEquals(request, null) then
                    failures.Add "responses-request-unavailable"
                elif not (phase.TryBeginExecution()) then
                    failures.Add "responses-phase-already-consumed"
                elif String.IsNullOrEmpty providerKey || providerKey.Length > 4096
                     || (providerKey |> Seq.exists (fun c -> Char.IsWhiteSpace c || Char.IsControl c)) then
                    failures.Add "responses-provider-credential-invalid"
                else
                    let! counted =
                        send countEndpoint request.CountBody request.CountSha256 4096
                            (fun () -> stage <- CountSent) (fun status -> countStatus <- Some status)
                            (fun bytes -> countBody <- bytes) (fun () -> countComplete <- true)
                    match counted with
                    | Error errors -> failures.AddRange errors
                    | Ok bytes ->
                        match NativeResponses.admitCount request bytes with
                        | Error errors -> failures.AddRange errors
                        | Ok accepted ->
                            count <- Some accepted
                            stage <- CountAccepted
                            if cleanup.Count <> 0 then failures.Add "responses-count-cleanup-unproved"
                            elif accepted.GenerationSha256 <> request.GenerationSha256
                                 || accepted.CountRequestSha256 <> request.CountSha256
                                 || accepted.CountResponseSha256 <> hash countBody then
                                failures.Add "responses-count-generation-join"
                            else
                                let! generated =
                                    send generationEndpoint request.GenerationBody accepted.GenerationSha256 262144
                                        (fun () -> stage <- GenerationSent) (fun status -> generationStatus <- Some status)
                                        (fun bytes -> generationBody <- bytes) (fun () -> generationComplete <- true; stage <- ResponseCaptured)
                                match generated with
                                | Error errors ->
                                    failures.AddRange errors
                                    // Complete non200 bodies can still contain actual billable
                                    // provider observations. Preserve them without success credit.
                                    if generationComplete then
                                        match NativeResponses.decodeResponse generationBody with
                                        | Ok observation -> decoded <- Some observation
                                        | Error _ -> ()
                                | Ok responseBytes ->
                                    stage <- ResponseCaptured
                                    match NativeResponses.decodeResponse responseBytes with
                                    | Error errors -> failures.AddRange errors
                                    | Ok observation ->
                                        decoded <- Some observation
                                        match NativeResponses.validateCompletion accepted observation with
                                        | Ok _ -> ()
                                        | Error errors -> failures.AddRange errors
            with
            | :? OperationCanceledException -> failures.Add "responses-cancelled-or-original-deadline"
            | :? HttpRequestException -> failures.Add "responses-transport-unknown"
            | _ -> failures.Add "responses-local-failure"

            let elapsed = if obj.ReferenceEquals(phase, null) then 0L else phase.ElapsedMilliseconds
            if elapsed >= wholeMilliseconds then cleanup.Add "responses-original-whole-deadline-exhausted"
            return Outcome.Create(stage, countStatus, generationStatus, countBody, generationBody,
                                  countComplete, generationComplete, count, decoded, List.ofSeq failures, List.ofSeq cleanup, elapsed)
        }

    let execute (phase: Phase) (request: NativeResponses.FrozenRequest) (providerKey: string)
                (cancellationToken: CancellationToken) =
        executeForTest productionClient phase request providerKey cancellationToken
