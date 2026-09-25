namespace FS.GG.Org.Policy

open System
open System.Net
open System.Net.Http
open System.Net.Http.Headers

/// Dormant authenticated HTTPS reader for the exact protected-main branch request. No installed
/// credential source or receiver path constructs it. The fixture handler is test-assembly only.
module GitHubProtectedBranchHttp =
    let private maximumBytes = 65536

    let private validToken (token: string) =
        not (String.IsNullOrWhiteSpace token)
        && token |> Seq.forall (fun c -> c >= '!' && c <= '~' && c <> '"' && c <> '\\')

    let private jsonMediaType (media: string) =
        String.Equals(media, "application/json", StringComparison.OrdinalIgnoreCase)
        || String.Equals(media, "application/vnd.github+json", StringComparison.OrdinalIgnoreCase)

    let private utf8Charset (charset: string) =
        String.IsNullOrEmpty charset
        || String.Equals(charset, "utf-8", StringComparison.OrdinalIgnoreCase)
        || String.Equals(charset, "\"utf-8\"", StringComparison.OrdinalIgnoreCase)

    type Reader private (token: string, handler: HttpMessageHandler) =
        let client = new HttpClient(handler, true)

        do client.Timeout <- TimeSpan.FromSeconds 30.0

        let read (request: GitHubProtectedBranchPin.ExactRequest)
            : Result<GitHubProtectedBranchPin.BranchResponse, unit> =
            if not (GitHubProtectedBranchPin.isExactReadRequest request) then
                Error ()
            else
                try
                    use message = new HttpRequestMessage(HttpMethod.Get, Uri(request.Url))
                    message.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)
                    message.Headers.UserAgent.ParseAdd("fsgg-org-policy/0.1")
                    message.Headers.Accept.ParseAdd("application/vnd.github+json")
                    message.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2026-03-10") |> ignore
                    use response =
                        client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead)
                            .GetAwaiter().GetResult()
                    if response.StatusCode <> HttpStatusCode.OK
                       || not (isNull response.Headers.Location)
                       || isNull response.RequestMessage
                       || isNull response.RequestMessage.RequestUri
                       || not (String.Equals(response.RequestMessage.RequestUri.AbsoluteUri,
                                             request.Url, StringComparison.Ordinal))
                       || isNull response.Content
                       || isNull response.Content.Headers.ContentType
                       || response.Content.Headers.ContentEncoding.Count <> 0
                       || not (jsonMediaType response.Content.Headers.ContentType.MediaType)
                       || not (utf8Charset response.Content.Headers.ContentType.CharSet)
                       || (response.Content.Headers.ContentLength.HasValue
                           && response.Content.Headers.ContentLength.Value > int64 maximumBytes) then
                        Error ()
                    else
                        use stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                        let buffer = Array.zeroCreate<byte> (maximumBytes + 1)
                        let mutable used = 0
                        let mutable ended = false
                        while not ended && used < buffer.Length do
                            let count = stream.Read(buffer, used, buffer.Length - used)
                            if count = 0 then ended <- true
                            else used <- used + count
                        if not ended || used = 0
                           || (response.Content.Headers.ContentLength.HasValue
                               && int64 used <> response.Content.Headers.ContentLength.Value) then
                            Error ()
                        else
                            Ok { StatusCode = int response.StatusCode
                                 ResponseUrl = response.RequestMessage.RequestUri.AbsoluteUri
                                 MediaType = response.Content.Headers.ContentType.MediaType
                                 Body = buffer.[0 .. used - 1] }
                with _ ->
                    // Transport errors never expose credentials, request headers or response bytes.
                    Error ()

        /// The normal factory owns a no-redirect, no-cookie handler for the fixed GitHub host.
        static member Create(token: string) : Result<Reader, unit> =
            if not (validToken token) then Error ()
            else
                let handler =
                    new HttpClientHandler(AllowAutoRedirect = false,
                                          UseCookies = false,
                                          AutomaticDecompression = DecompressionMethods.None)
                Ok(new Reader(token, handler))

        /// Test assembly only: deterministic response and no external network access.
        static member internal ForFixture(token: string, handler: HttpMessageHandler) : Result<Reader, unit> =
            if not (validToken token) || isNull handler then Error ()
            else Ok(new Reader(token, handler))

        member _.Dispose() = client.Dispose()

        interface IDisposable with
            member this.Dispose() = this.Dispose()

        interface GitHubProtectedBranchPin.IReadOnlyProtectedBranchReader with
            member _.ReadExact request = read request
