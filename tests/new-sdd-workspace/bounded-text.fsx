// Exercise the compiled shared reader with inert handlers/streams; no socket or SDK operation.
open NewSddWorkspace.Program
open System
open System.IO
open System.Net
open System.Net.Http
open System.Reflection
open System.Reflection.Emit
open System.Threading
open System.Threading.Tasks
open System.Text

let program = typeof<Options>.Assembly.GetType("NewSddWorkspace.Program")
let privateMethod name =
    let methodInfo = program.GetMethod(name, BindingFlags.Static ||| BindingFlags.NonPublic)
    if isNull methodInfo then failwithf "compiled transport method missing: %s" name
    methodInfo
let readMethod = privateMethod "readBoundedText"
let maximum = 256 * 1024
let mutable assertions = 0
let check label condition =
    assertions <- assertions + 1
    if not condition then failwith label
let expectOk label expected = function
    | Ok value -> check label (value = expected)
    | Error note -> failwithf "%s: %s" label note
let expectError label = function
    | Error _ -> assertions <- assertions + 1
    | Ok _ -> failwithf "%s accepted" label

type TrackedStream(bytes: byte array, stallAfterFirst: bool) =
    inherit Stream()
    let mutable position = 0
    let mutable reads = 0
    let mutable disposed = false
    let tokens = ResizeArray<CancellationToken>()
    member _.Reads = reads
    member _.BytesRead = position
    member _.Disposed = disposed
    member _.Tokens = tokens |> Seq.toArray
    override _.CanRead = true
    override _.CanSeek = false
    override _.CanWrite = false
    override _.Length = raise (NotSupportedException())
    override _.Position with get () = raise (NotSupportedException()) and set _ = raise (NotSupportedException())
    override _.Flush() = ()
    override _.Seek(_, _) = raise (NotSupportedException())
    override _.SetLength(_) = raise (NotSupportedException())
    override _.Write(_, _, _) = raise (NotSupportedException())
    override _.Read(destination, offset, count) =
        reads <- reads + 1
        let available = min count (bytes.Length - position)
        Array.Copy(bytes, position, destination, offset, available)
        position <- position + available
        available
    override _.ReadAsync(destination: Memory<byte>, token: CancellationToken) =
        reads <- reads + 1
        tokens.Add token
        if stallAfterFirst && reads > 1 then
            let pending = TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously)
            token.Register(fun () -> pending.TrySetCanceled(token) |> ignore) |> ignore
            ValueTask<int>(pending.Task)
        else
            let available = min destination.Length (bytes.Length - position)
            bytes.AsMemory(position, available).CopyTo(destination)
            position <- position + available
            ValueTask<int>(available)
    override _.Dispose(disposing) =
        disposed <- true
        base.Dispose(disposing)

type TrackedContent(source: TrackedStream) =
    inherit HttpContent()
    let mutable disposed = false
    let acquired = ResizeArray<CancellationToken>()
    member _.Disposed = disposed
    member _.Acquired = acquired |> Seq.toArray
    override _.TryComputeLength(length: byref<int64>) =
        length <- 0L
        false
    override _.SerializeToStreamAsync(_, _) =
        Task.FromException(InvalidOperationException("ResponseHeadersRead was bypassed by whole-body buffering"))
    override _.CreateContentReadStreamAsync() = Task.FromResult(source :> Stream)
    override _.CreateContentReadStreamAsync(token: CancellationToken) =
        acquired.Add token
        Task.FromResult(source :> Stream)
    override _.Dispose(disposing) =
        disposed <- true
        source.Dispose()
        base.Dispose(disposing)

type TrackedHandler(response: HttpResponseMessage, stallHeaders: bool) =
    inherit HttpMessageHandler()
    let mutable sends = 0
    let mutable disposed = false
    let requests = ResizeArray<string>()
    member _.Sends = sends
    member _.Disposed = disposed
    member _.Requests = requests |> Seq.toArray
    override _.SendAsync(request, token) =
        sends <- sends + 1
        requests.Add(request.RequestUri.AbsoluteUri)
        if stallHeaders then
            task {
                do! Task.Delay(Timeout.InfiniteTimeSpan, token)
                return response
            }
        else Task.FromResult response
    override _.Dispose(disposing) =
        disposed <- true
        base.Dispose(disposing)

let execute (status: HttpStatusCode) (bytes: byte array) (declaredLength: int64 option) (giantHeaders: bool) (stallHeaders: bool) (stallBody: bool) (deadlineMilliseconds: int) =
    let source = new TrackedStream(bytes, stallBody)
    let content = new TrackedContent(source)
    match declaredLength with
    | Some length -> content.Headers.ContentLength <- Nullable<int64>(length)
    | None -> ()
    let response = new HttpResponseMessage(status)
    response.Content <- content
    if int status >= 300 && int status < 400 then
        response.Headers.Location <- Uri("https://second.invalid/must-not-send")
    if giantHeaders then response.Headers.TryAddWithoutValidation("X-Finite", String('h', 16384)) |> ignore
    let handler = new TrackedHandler(response, stallHeaders)
    let client = new HttpClient(handler)
    client.Timeout <- Timeout.InfiniteTimeSpan
    use deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(float deadlineMilliseconds))
    let token = deadline.Token
    let result = readMethod.Invoke(null, [| box client; box token; box "https://first.invalid/immutable/text" |]) :?> Result<string, string>
    check "owned HttpClient/handler not disposed" handler.Disposed
    check "more than one explicit send" (handler.Sends = 1)
    if not stallHeaders then
        check "received response content not disposed" content.Disposed
        check "received body stream not disposed" source.Disposed
    else response.Dispose() // The inert fixture allocated an unreturned response; production never owned one.
    result, source, content, handler, token

let normal label bytes expected =
    let result, source, content, _, token = execute HttpStatusCode.OK bytes None false false false 5000
    result |> expectOk label expected
    check "original token missing from stream acquisition" (content.Acquired = [| token |])
    check "original token changed at body read/EOF" (source.Tokens.Length >= 1 && (source.Tokens |> Array.forall ((=) token)))
normal "ordinary UTF8" (Encoding.UTF8.GetBytes "source: pinned\nλ") "source: pinned\nλ"
normal "valid UTF8 BOM compatibility" (Array.append [| 0xEFuy; 0xBBuy; 0xBFuy |] (Encoding.UTF8.GetBytes "source: pinned\n")) "source: pinned\n"
normal "empty text compatibility" [||] ""
normal "exact body limit" (Array.create maximum 0x61uy) (String('a', maximum))

for status in [ HttpStatusCode.NotFound; HttpStatusCode.InternalServerError; HttpStatusCode.Found; HttpStatusCode.TemporaryRedirect; HttpStatusCode.PermanentRedirect ] do
    let result, stream, _, handler, _ = execute status [| 0x61uy |] None false false false 5000
    result |> expectError (sprintf "status%d" (int status))
    check "non2xx body was read or buffered" (stream.Reads = 0)
    check "redirect followed" (handler.Requests = [| "https://first.invalid/immutable/text" |])

let declaredOversize, declaredStream, _, _, _ = execute HttpStatusCode.OK [| 0x61uy |] (Some(int64 maximum + 1L)) false false false 5000
declaredOversize |> expectError "declared oversize"
check "declared oversized response read body" (declaredStream.Reads = 0)
let headerOversize, headerStream, _, _, _ = execute HttpStatusCode.OK [| 0x61uy |] None true false false 5000
headerOversize |> expectError "response header cap"
check "oversized headers read body" (headerStream.Reads = 0)
let streamedOversize, oversizedStream, _, _, _ = execute HttpStatusCode.OK (Array.create (maximum + 64) 0x61uy) None false false false 5000
streamedOversize |> expectError "stream oversize"
check "reader consumed beyond one refusal byte" (oversizedStream.BytesRead = maximum + 1)
let exactDeclared, _, _, _, _ = execute HttpStatusCode.OK [| 0x61uy |] (Some 1L) false false false 5000
exactDeclared |> expectOk "exact declared length" "a"
for declared, bytes in [ 20L, [| 0x61uy |]; 0L, [| 0x61uy |]; 1L, [||] ] do
    let result, _, _, _, _ = execute HttpStatusCode.OK bytes (Some declared) false false false 5000
    result |> expectError "declared/actual length mismatch"
for invalid in [ [| 0xFFuy |]; [| 0xC3uy; 0x28uy |]; [| 0xEFuy; 0xBBuy; 0xBFuy; 0xFFuy |] ] do
    let result, _, _, _, _ = execute HttpStatusCode.OK invalid None false false false 5000
    result |> expectError "malformed UTF8"
let stalledHeaders, _, _, _, _ = execute HttpStatusCode.OK [||] None false true false 200
stalledHeaders |> expectError "original deadline during headers"
let stalledBody, bodyStream, bodyContent, _, token = execute HttpStatusCode.OK [| 0x61uy |] None false false true 200
stalledBody |> expectError "original deadline remains active after successful headers/first body read"
check "body stall did not occur after first bytes" (bodyStream.BytesRead = 1 && bodyStream.Reads = 2)
check "body acquisition/read used another deadline token" (bodyContent.Acquired = [| token |] && (bodyStream.Tokens |> Array.forall ((=) token)))

// Inspect actual compiled calls rather than a retyped caller or a source-string assertion.
let opcodes =
    typeof<OpCodes>.GetFields(BindingFlags.Public ||| BindingFlags.Static)
    |> Array.map (fun field -> field.GetValue(null) :?> OpCode)
    |> Array.map (fun opcode -> int opcode.Value &&& 0xFFFF, opcode)
    |> Map.ofArray
let calledMethods (methodInfo: MethodInfo) =
    let il = methodInfo.GetMethodBody().GetILAsByteArray()
    let calls = ResizeArray<string>()
    let mutable index = 0
    while index < il.Length do
        let code =
            if il.[index] = 0xFEuy then
                index <- index + 2
                0xFE00 ||| int il.[index - 1]
            else
                let value = int il.[index]
                index <- index + 1
                value
        let opcode = opcodes.[code]
        if opcode.OperandType = OperandType.InlineMethod then
            calls.Add(methodInfo.Module.ResolveMethod(BitConverter.ToInt32(il, index)).Name)
        let size =
            match opcode.OperandType with
            | OperandType.InlineNone -> 0
            | OperandType.ShortInlineBrTarget | OperandType.ShortInlineI | OperandType.ShortInlineVar -> 1
            | OperandType.InlineVar -> 2
            | OperandType.InlineI8 | OperandType.InlineR -> 8
            | OperandType.InlineSwitch -> 4 + 4 * BitConverter.ToInt32(il, index)
            | _ -> 4
        index <- index + size
    calls |> Seq.toArray
check "descriptor bypasses actual shared fetchText" (calledMethods (privateMethod "fetchDescriptor") |> Array.contains "fetchText")
check "Kit fetchText bypasses actual bounded reader" (calledMethods (privateMethod "fetchText") |> Array.contains "readBoundedText")
let validateFactory () =
    use configuredClient = (privateMethod "createTextClient").Invoke(null, [||]) :?> HttpClient
    let handlerField = typeof<HttpMessageInvoker>.GetField("_handler", BindingFlags.Instance ||| BindingFlags.NonPublic)
    check "qualified runtime HttpMessageInvoker handler field unavailable" (not (isNull handlerField))
    let configuredHandler = handlerField.GetValue(configuredClient) :?> HttpClientHandler
    check "production handler follows redirects" (not configuredHandler.AllowAutoRedirect)
    check "production parsed-header cap differs" (configuredHandler.MaxResponseHeadersLength = 16)
    check "production client installs a separate header-only timeout" (configuredClient.Timeout = Timeout.InfiniteTimeSpan)
validateFactory ()
// Actual descriptor boundary: an invalid relative URI fails before any network send or write.
let validateDescriptorRefusal () =
    let previous = Environment.GetEnvironmentVariable "FSGG_TEMPLATES_RAW_BASE"
    let targetRoot = Path.Combine(Path.GetTempPath(), "bounded-descriptor-" + Guid.NewGuid().ToString("N"))
    let destination = Path.Combine(targetRoot, "providers.yml")
    try
        Environment.SetEnvironmentVariable("FSGG_TEMPLATES_RAW_BASE", "not-an-absolute-uri")
        let result = (privateMethod "fetchDescriptor").Invoke(null, [| box "app"; box "immutable"; box destination |]) :?> Result<string option * string option, string>
        match result with
        | Error _ -> assertions <- assertions + 1
        | Ok _ -> failwith "actual descriptor accepted an invalid request URI"
        check "actual descriptor refusal wrote receiver directory" (not (Directory.Exists targetRoot))
        check "actual descriptor refusal wrote provider file" (not (File.Exists destination))
    finally
        Environment.SetEnvironmentVariable("FSGG_TEMPLATES_RAW_BASE", previous)
validateDescriptorRefusal ()
printfn "bounded text: %d actual compiled reader/caller/factory assertions; no network sends" assertions
