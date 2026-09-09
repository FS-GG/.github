namespace FS.GG.Telemetry

open System
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Threading
open System.Threading.Tasks
open System.IO
open FS.GG.Coord

module RemoteClient =
    type CredentialResolver = string -> CancellationToken -> Task<string option>
    type Result = Acknowledged of RemoteContract.Receipt | Unacknowledged of string
    let createHandler () = new HttpClientHandler(AllowAutoRedirect=false)
    let private verify scope batch digest bytes =
        match RemoteContract.parseReceipt bytes with
        | Ok receipt when receipt.Scope=scope && receipt.BatchId=batch && String.Equals(receipt.Digest,digest,StringComparison.Ordinal) -> Acknowledged receipt
        | _ -> Unacknowledged "receipt-unavailable"
    let private readBounded (response:HttpResponseMessage) (token:CancellationToken) = task {
        use! input=response.Content.ReadAsStreamAsync(token)
        use output=new MemoryStream()
        let buffer=Array.zeroCreate<byte> 1024
        let mutable total=0
        let mutable more=true
        while more && total<=4096 do
            let! count=input.ReadAsync(buffer,token)
            if count=0 then more<-false else output.Write(buffer,0,count); total<-total+count
        return if total>4096 then None else Some(output.ToArray()) }
    let private request (client: HttpClient) (config:RemoteContract.ClientConfig) (resolve:CredentialResolver) (scope:TelemetryReceipt.Scope) (method:HttpMethod) (path:string) (content:byte array option) (batch:string) (digest:string) (cancellationToken:CancellationToken) = task {
        match RemoteContract.validateClientConfig config with
        | Error error -> return Unacknowledged error
        | Ok _ ->
            let! credential = resolve config.CredentialReference cancellationToken
            match credential with
            | None -> return Unacknowledged "credential-unavailable"
            | Some token ->
                let mutable attempt = 0
                let mutable answer = Unacknowledged "receipt-unavailable"
                let mutable finished = false
                while attempt < 5 && not finished && not cancellationToken.IsCancellationRequested do
                    attempt <- attempt + 1
                    try
                        use attemptCts=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                        attemptCts.CancelAfter(TimeSpan.FromSeconds 10.)
                        use message = new HttpRequestMessage(method, Uri(config.Endpoint,path))
                        message.Headers.Authorization <- AuthenticationHeaderValue("Bearer",token)
                        message.Headers.Accept.ParseAdd("application/json")
                        match content with Some bytes -> message.Content <- new ByteArrayContent(bytes); message.Content.Headers.ContentType <- MediaTypeHeaderValue("application/json") | None -> ()
                        use! response = client.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,attemptCts.Token)
                        if int response.StatusCode >= 300 && int response.StatusCode < 400 then finished <- true; answer <- Unacknowledged "redirect-refused"
                        else
                            let! bounded = readBounded response attemptCts.Token
                            match bounded with
                            | Some bytes when response.StatusCode=HttpStatusCode.OK || response.StatusCode=HttpStatusCode.Accepted ->
                                let candidate = verify scope batch digest bytes
                                match candidate with Acknowledged _ -> finished<-true; answer<-candidate | _ -> answer<-Unacknowledged "receipt-unavailable"
                            | Some bytes ->
                                match RemoteContract.parseError bytes with
                                | Some ("identity-conflict" as code) | Some ("unauthorized-scope" as code) | Some ("unsupported-version" as code) | Some ("invalid-request" as code) | Some ("oversized-batch" as code) -> finished<-true; answer<-Unacknowledged code
                                | Some code -> answer<-Unacknowledged code
                                | None -> answer<-Unacknowledged "receipt-unavailable"
                            | None -> answer <- Unacknowledged "receipt-unavailable"
                    with :? HttpRequestException -> answer <- Unacknowledged "receipt-unavailable"
                       | :? OperationCanceledException when not cancellationToken.IsCancellationRequested -> answer <- Unacknowledged "receipt-unavailable"
                    if not finished && attempt < 5 then
                        let cap = min 5000 (100 * (1 <<< (attempt-1)))
                        let delay = Random.Shared.Next(max 1 (cap/2),cap+1)
                        do! Task.Delay(delay,cancellationToken)
                return answer }
    let submitWithClientForTesting client config resolve scope bytes cancellationToken = task {
        match TelemetryReceipt.parse bytes with
        | Error _ -> return Unacknowledged "invalid-request"
        | Ok envelope when envelope.Scope <> scope -> return Unacknowledged "unauthorized-scope"
        | Ok envelope -> return! request client config resolve scope HttpMethod.Post "v1/batches" (Some bytes) envelope.BatchId envelope.Digest cancellationToken }
    let lookupWithClientForTesting client config resolve scope (batchId:string) expectedDigest cancellationToken =
        request client config resolve scope HttpMethod.Get ($"v1/receipts/{Uri.EscapeDataString(batchId)}") None batchId expectedDigest cancellationToken
    let submit config resolve scope bytes cancellationToken = task {
        use handler=createHandler()
        use client=new HttpClient(handler)
        return! submitWithClientForTesting client config resolve scope bytes cancellationToken }
    let lookup config resolve scope batchId expectedDigest cancellationToken = task {
        use handler=createHandler()
        use client=new HttpClient(handler)
        return! lookupWithClientForTesting client config resolve scope batchId expectedDigest cancellationToken }
