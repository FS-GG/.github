namespace FS.GG.Telemetry.Tests

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Security
open System.Net.Sockets
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates
open System.Text
open System.Threading
open System.Threading.Tasks
open Xunit
open FS.GG.Coord
open FS.GG.Telemetry

/// Actual HTTP1.1 wire fixtures. No provider, production credential, or external address.
module DirectResponsesWireTests =
    type private Request = { Path: string; Header: string; Body: byte array }
    let private unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
    let private frozen () =
        NativeResponses.freeze { Instructions="Return the fixed JSON object.";InputText="fixture";SchemaName="fixture";
                                 SchemaJson=Encoding.UTF8.GetBytes "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}" }
        |> unwrap
    let private generation =
        $"""{{"object":"response","id":"resp_wire","model":"{NativeResponses.Model}","status":"completed","usage":{{"input_tokens":17,"output_tokens":1,"total_tokens":18}},"output":[{{"type":"message","content":[{{"type":"output_text","text":"{{}}"}}]}}]}}"""
    let private count = "{\"object\":\"response.input_tokens\",\"input_tokens\":17}"
    let private reply (status: string) (extra: string) (body: string) (declared: int option) =
        let bytes = Encoding.UTF8.GetBytes body
        let length = Option.defaultValue bytes.Length declared
        Encoding.UTF8.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {length}\r\nConnection: close\r\n{extra}\r\n{body}")

    let private readRequest (stream: SslStream) (token: CancellationToken) = task {
        use header = new MemoryStream()
        let one = Array.zeroCreate<byte> 1
        let mutable complete = false
        while not complete do
            if header.Length >= 16384L then invalidOp "wire header bound"
            let! size = stream.ReadAsync(one.AsMemory(),token)
            if size = 0 then invalidOp "wire request header truncated"
            header.WriteByte one.[0]
            if header.Length >= 4L then
                let bytes = header.GetBuffer()
                let index = int header.Length - 4
                complete <- bytes.[index]=13uy && bytes.[index+1]=10uy && bytes.[index+2]=13uy && bytes.[index+3]=10uy
        let raw = Encoding.ASCII.GetString(header.ToArray())
        let lines = raw.Split([|"\r\n"|],StringSplitOptions.None)
        let start = lines.[0].Split ' '
        if start.Length<>3 || start.[0]<>"POST" || start.[2]<>"HTTP/1.1" then invalidOp "wire request line"
        let lengths = lines |> Array.filter (fun line -> line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))
        if lengths.Length<>1 then invalidOp "wire content length missing or repeated"
        let length = Int32.Parse(lengths.[0].Substring("Content-Length:".Length).Trim())
        if length<=0 || length>262144 then invalidOp "wire body bound"
        let body = Array.zeroCreate<byte> length
        let mutable offset = 0
        while offset<length do
            let! size = stream.ReadAsync(body.AsMemory(offset),token)
            if size=0 then invalidOp "wire request body truncated"
            offset <- offset+size
        return { Path=start.[1];Header=raw;Body=body }
    }

    let private exercise (scenario: string) = task {
        use key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
        let certificateRequest = CertificateRequest("CN=api.openai.com",key,HashAlgorithmName.SHA256)
        use certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1.),DateTimeOffset.UtcNow.AddMinutes(1.))
        let fingerprint = Convert.ToHexString(SHA256.HashData certificate.RawData).ToLowerInvariant()
        let listener = TcpListener(IPAddress.Loopback,0)
        listener.Start(4)
        let port = (listener.LocalEndpoint :?> IPEndPoint).Port
        use timeout = new CancellationTokenSource(TimeSpan.FromSeconds 10.)
        use stop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token)
        let requests = ResizeArray<Request>()
        let mutable connections = 0
        let server = task {
            try
                while not stop.IsCancellationRequested do
                    use! socket = listener.AcceptTcpClientAsync(stop.Token)
                    connections <- connections+1
                    if connections>3 then invalidOp "wire unexpected connection count"
                    use stream = new SslStream(socket.GetStream(),false)
                    let options = SslServerAuthenticationOptions(ServerCertificate=certificate,ClientCertificateRequired=false)
                    do! stream.AuthenticateAsServerAsync(options,stop.Token)
                    let! request = readRequest stream stop.Token
                    requests.Add request
                    let response =
                        match scenario,request.Path with
                        | "redirect",_ -> Some(reply "302 Found" "Location: https://api.openai.com/v1/replayed\r\n" "{}" None)
                        | "authentication",_ -> Some(reply "401 Unauthorized" "WWW-Authenticate: Basic realm=fixture\r\n" "{}" None)
                        | "count-reset",_ -> None
                        | _,"/v1/responses/input_tokens" -> Some(reply "200 OK" "" count None)
                        | "generation-reset",_ -> None
                        | "generation-truncated",_ -> Some(reply "200 OK" "" "{" (Some 100))
                        | _,"/v1/responses" -> Some(reply "200 OK" "" generation None)
                        | _ -> invalidOp "unexpected fixed endpoint"
                    match response with
                    | Some bytes ->
                        do! stream.WriteAsync(bytes.AsMemory(),stop.Token)
                        do! stream.FlushAsync(stop.Token)
                    | None -> socket.Client.LingerState <- LingerOption(true,0)
            with
            | :? OperationCanceledException when stop.IsCancellationRequested -> ()
            | :? ObjectDisposedException when stop.IsCancellationRequested -> ()
            | :? SocketException when stop.IsCancellationRequested -> ()
        }
        let request = frozen()
        let mutable outcome : DirectResponses.Outcome option = None
        try
            let! observed = DirectResponses.executeForTest (fun () -> DirectResponses.createLoopbackClientForTest port fingerprint)
                                (DirectResponses.beginPhase()) request "fixture-key" timeout.Token
            outcome <- Some observed
        finally
            stop.Cancel()
            listener.Stop()
        // Observes every accepted connection/task; no detached fixture work survives.
        do! server
        Assert.False(timeout.IsCancellationRequested,"wire fixture original10s deadline exhausted")
        let observed = Option.get outcome
        let expected = if List.contains scenario ["success";"generation-reset";"generation-truncated"] then 2 else 1
        Assert.Equal(expected,connections)
        Assert.Equal(expected,requests.Count)
        Assert.Equal("/v1/responses/input_tokens",requests.[0].Path)
        Assert.Equal<byte>(request.CountBody,requests.[0].Body)
        if expected=2 then
            Assert.Equal("/v1/responses",requests.[1].Path)
            Assert.Equal<byte>(request.GenerationBody,requests.[1].Body)
        for actual in requests do
            Assert.Contains("Connection: close",actual.Header,StringComparison.OrdinalIgnoreCase)
            Assert.Contains("Host: api.openai.com",actual.Header,StringComparison.OrdinalIgnoreCase)
            Assert.DoesNotContain("Expect: 100-continue",actual.Header,StringComparison.OrdinalIgnoreCase)
        match scenario with
        | "success" -> Assert.Empty observed.Failure; Assert.Equal(DirectResponses.ResponseCaptured,observed.Stage)
        | "count-reset" -> Assert.Equal(DirectResponses.CountSent,observed.Stage);Assert.NotEmpty observed.Failure
        | "generation-reset" | "generation-truncated" -> Assert.Equal(DirectResponses.GenerationSent,observed.Stage);Assert.NotEmpty observed.Failure
        | _ -> Assert.Equal(DirectResponses.CountSent,observed.Stage);Assert.NotEmpty observed.Failure
        Assert.Empty observed.CleanupFailure
    }

    [<Theory>]
    [<InlineData("success")>]
    [<InlineData("redirect")>]
    [<InlineData("authentication")>]
    [<InlineData("count-reset")>]
    [<InlineData("generation-reset")>]
    [<InlineData("generation-truncated")>]
    let ``actual sockets wire sends no redirect authentication or ambiguous retry`` (scenario: string) = exercise scenario
