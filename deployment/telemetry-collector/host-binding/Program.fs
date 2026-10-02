open System
open System.Text
open System.Text.Json
open Fs.Gg.Telemetry.HostBinding

let private observation = function OwnedProcessScope.NotObserved->"not-observed"|OwnedProcessScope.ObservedFalse->"false"|OwnedProcessScope.ObservedTrue->"true"
let private reader = function OwnedProcessScope.ReaderNotObserved->"not-observed"|OwnedProcessScope.ReaderComplete->"complete"|OwnedProcessScope.ReaderFaulted->"faulted"|OwnedProcessScope.ReaderIncomplete->"incomplete"
let private retirement = function OwnedProcessScope.RetirementNotObserved->"not-observed"|OwnedProcessScope.RetirementClean->"clean"|OwnedProcessScope.RetirementUnknown->"unknown"
let private failure = function
    | OwnedProcessScope.NoFailure->"none"|OwnedProcessScope.ChildTaskLimit->"child-task-limit"|OwnedProcessScope.ChildToken->"child-token"
    | OwnedProcessScope.ChildEnumeration->"child-enumeration"|OwnedProcessScope.IdentityRead->"identity-read"|OwnedProcessScope.IdentityParent->"identity-parent"
    | OwnedProcessScope.PidFdOpen->"pidfd-open"|OwnedProcessScope.IdentityRecheck->"identity-recheck"|OwnedProcessScope.RetainedCapacity->"retained-capacity"
    | OwnedProcessScope.Signal->"signal"|OwnedProcessScope.ReapNoChild->"reap-no-child"|OwnedProcessScope.ReapError->"reap-error"
    | OwnedProcessScope.DirectWait->"direct-wait"|OwnedProcessScope.FinalDirectExit->"final-direct-exit"|OwnedProcessScope.FinalSettlement->"final-settlement"
    | OwnedProcessScope.PriorUnknown->"prior-unknown"|OwnedProcessScope.StdoutReader->"stdout-reader"|OwnedProcessScope.StderrReader->"stderr-reader"
let private exceptionClass = function OwnedProcessScope.NoException->"none"|OwnedProcessScope.Io->"io"|OwnedProcessScope.Format->"format"|OwnedProcessScope.Unauthorized->"unauthorized"|OwnedProcessScope.InvalidOperation->"invalid-operation"|OwnedProcessScope.Other->"other"
let private diagnosticText (value: OwnedProcessScope.DiagnosticSnapshot) =
    JsonSerializer.Serialize({| schema="fsgg.telemetry.host-binding-diagnostic/1";runOrdinal=value.RunOrdinal;executionRole=value.ExecutionRole;firstFailureSite=failure value.FirstFailureSite;errno=value.Errno;exceptionClass=exceptionClass value.ExceptionClass;directExit=observation value.DirectExit;settlement=observation value.Settlement;unknownBeforeFinal=observation value.UnknownBeforeFinal;stdoutReader=reader value.StdoutReader;stderrReader=reader value.StderrReader;firstRetirement=retirement value.FirstRetirement;finalRetirement=retirement value.FinalRetirement;deadlineExpired=observation value.Deadline|})

let private fail diagnostic message =
    Console.Error.WriteLine("host-binding-refused:" + message)
    diagnostic |> Option.iter(fun value ->
        try
            let text=diagnosticText value
            if Encoding.UTF8.GetByteCount(text) <= 4069 then Console.Error.WriteLine("host-binding-diagnostic:"+text)
        with _ -> ())
    2

let parseArgs (values: string array) =
    if values.Length % 2 <> 0 then raise (BindingRefusal "arguments-refused")
    let pairs = values |> Array.chunkBySize 2
    let parsed =
        pairs
        |> Array.map (fun pair ->
            if pair[0].Length < 3 || not (pair[0].StartsWith("--", StringComparison.Ordinal)) then
                raise (BindingRefusal "arguments-refused")
            pair[0].Substring(2), pair[1])
    if parsed |> Array.map fst |> Array.distinct |> Array.length <> parsed.Length then
        raise (BindingRefusal "arguments-refused")
    Map.ofArray parsed

let required name (values: Map<string, string>) =
    match values.TryFind(name) with
    | Some value when value.Length > 0 -> value
    | _ -> raise (BindingRefusal("argument-missing:" + name))

let serialize value = JsonSerializer.Serialize(value, JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase))

let readAdmission () =
    use input = Console.OpenStandardInput()
    let buffer = Array.zeroCreate<byte> 66
    let mutable count = 0
    let mutable complete = false
    while not complete && count < buffer.Length do
        let read = input.Read(buffer, count, buffer.Length - count)
        if read = 0 then complete <- true else count <- count + read
    if count <> 64 && not (count = 65 && buffer[64] = byte '\n') then
        raise (BindingRefusal "effect-admission-refused")
    for index in 0 .. 63 do
        let value = buffer[index]
        if not ((value >= byte '0' && value <= byte '9') || (value >= byte 'a' && value <= byte 'f')) then
            raise (BindingRefusal "effect-admission-refused")
    Text.Encoding.ASCII.GetString(buffer, 0, 64)

[<EntryPoint>]
let main argv =
    let mutable diagnostic: OwnedProcessScope.DiagnosticSnapshot option = None
    try
        use scope = OwnedProcessScope.enterCli ()
        try
            Console.CancelKeyPress.Add(fun event -> event.Cancel <- true; scope.RequestCancellation())
            use terminate = Runtime.InteropServices.PosixSignalRegistration.Create(Runtime.InteropServices.PosixSignal.SIGTERM, fun context -> context.Cancel <- true; scope.RequestCancellation())
            if argv.Length < 1 then raise (BindingRefusal "command-refused")
            let command = argv[0]
            let args = parseArgs argv[1..]
            let allowed = match command with | "inspect" | "render" -> set [ "source-root"; "source-sha"; "profile"; "source-pins" ] | "derive" -> set [ "source-root"; "source-sha"; "profile"; "source-pins"; "nonce" ] | "verify" -> set [ "source-root"; "source-sha"; "profile"; "source-pins"; "nonce"; "expected-binding-sha" ] | _ -> raise (BindingRefusal "command-refused")
            if Set.ofSeq args.Keys <> allowed then raise (BindingRefusal "arguments-refused")
            let binding = HostBinding.construct (required "source-root" args) (required "source-sha" args) (required "profile" args) (required "source-pins" args)
            match command with
            | "inspect" -> Console.Out.WriteLine(serialize binding); 0
            | "render" -> Console.Out.WriteLine(serialize {| schema = "fsgg.telemetry.host-binding-render/1"; recipeSourceSha = binding.SourceSha; recipeSourceTree = binding.SourceTree; profileSha256 = binding.ProfileSha256; operationId = binding.OperationId; sourcePinsSha256 = binding.SourcePinsSha256; producerSha256 = binding.ProducerSha256; bindingSha256 = binding.BindingSha256 |}); 0
            | "derive" -> Console.Out.WriteLine(HostBinding.deriveAdmission binding (required "nonce" args)); 0
            | "verify" -> let candidate = readAdmission () in HostBinding.verifyAdmission binding (required "nonce" args) (required "expected-binding-sha" args) candidate; Console.Out.WriteLine("{\"schema\":\"fsgg.telemetry.host-binding-verification/1\",\"verified\":true}"); 0
            | _ -> fail None "command-refused"
        with error ->
            let snapshot=scope.DiagnosticSnapshot
            if snapshot.FirstFailureSite<>OwnedProcessScope.NoFailure || snapshot.FinalRetirement=OwnedProcessScope.RetirementUnknown then diagnostic<-Some snapshot
            reraise()
    with
    | BindingRefusal message -> fail diagnostic message
    | :? JsonException -> fail diagnostic "json-refused"
    | :? UnauthorizedAccessException -> fail diagnostic "input-access-refused"
    | :? IO.IOException -> fail diagnostic "input-io-refused"
    | _ -> fail diagnostic "unexpected-refused"
