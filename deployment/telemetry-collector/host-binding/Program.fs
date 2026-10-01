open System
open System.Text.Json
open Fs.Gg.Telemetry.HostBinding

let fail message =
    Console.Error.WriteLine("host-binding-refused:" + message)
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

[<EntryPoint>]
let main argv =
    try
        if argv.Length < 1 then raise (BindingRefusal "command-refused")
        let command = argv[0]
        let args = parseArgs argv[1..]
        let allowed =
            match command with
            | "inspect" | "render" -> set [ "source-root"; "source-sha"; "profile"; "source-pins" ]
            | "derive" -> set [ "source-root"; "source-sha"; "profile"; "source-pins"; "nonce" ]
            | "verify" -> set [ "source-root"; "source-sha"; "profile"; "source-pins"; "nonce"; "expected-binding-sha" ]
            | _ -> raise (BindingRefusal "command-refused")
        if Set.ofSeq args.Keys <> allowed then raise (BindingRefusal "arguments-refused")
        let binding = HostBinding.construct (required "source-root" args) (required "source-sha" args) (required "profile" args) (required "source-pins" args)
        match command with
        | "inspect" -> Console.Out.WriteLine(serialize binding); 0
        | "render" ->
            Console.Out.WriteLine(serialize {| schema = "fsgg.telemetry.host-binding-render/1"; recipeSourceSha = binding.SourceSha; recipeSourceTree = binding.SourceTree; profileSha256 = binding.ProfileSha256; operationId = binding.OperationId; sourcePinsSha256 = binding.SourcePinsSha256; producerSha256 = binding.ProducerSha256; bindingSha256 = binding.BindingSha256 |})
            0
        | "derive" -> Console.Out.WriteLine(HostBinding.deriveAdmission binding (required "nonce" args)); 0
        | "verify" ->
            let candidate = Console.In.ReadToEnd().Trim()
            HostBinding.verifyAdmission binding (required "nonce" args) (required "expected-binding-sha" args) candidate
            Console.Out.WriteLine("{\"schema\":\"fsgg.telemetry.host-binding-verification/1\",\"verified\":true}")
            0
        | _ -> fail "command-refused"
    with
    | BindingRefusal message -> fail message
    | :? JsonException -> fail "json-refused"
    | :? UnauthorizedAccessException -> fail "input-access-refused"
    | :? IO.IOException -> fail "input-io-refused"
    | _ -> fail "unexpected-refused"
