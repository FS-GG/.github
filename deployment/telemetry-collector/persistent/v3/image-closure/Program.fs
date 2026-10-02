open System
open System.IO
open System.Text.Json
open System.Collections.Generic
open System.Threading
open FSGG.Telemetry.PersistentV3.ImageClosure

let options=JsonSerializerOptions(PropertyNameCaseInsensitive=false)
let exactProperties (expected:Set<string>) (value:JsonElement)=value.ValueKind=JsonValueKind.Object&&(value.EnumerateObject()|>Seq.length)=expected.Count&&(value.EnumerateObject()|>Seq.map _.Name|>Set.ofSeq)=expected
let properties (kind:Type)=kind.GetProperties()|>Array.map _.Name|>Set.ofArray
let require condition reason=if not condition then raise(InvalidDataException reason)
let rec closed (node:JsonElement)=
    match node.ValueKind with
    | JsonValueKind.Object->let names=HashSet<string>() in for item in node.EnumerateObject() do require(names.Add item.Name) "input-is-not-closed";closed item.Value
    | JsonValueKind.Array->require(node.GetArrayLength()<=8192) "input-array-bound";for item in node.EnumerateArray() do closed item
    | JsonValueKind.Null|JsonValueKind.Undefined->raise(InvalidDataException "input-null")
    | _->()
let load path=
    let info=FileInfo path
    require(info.Exists&&info.Length>0L&&info.Length<=4L*1024L*1024L) "input-size"
    require(String.IsNullOrEmpty info.LinkTarget) "input-link"
    File.ReadAllBytes path
let placeholder bytes=
    use document=JsonDocument.Parse(bytes:byte array)
    let mutable status=Unchecked.defaultof<JsonElement>
    if document.RootElement.TryGetProperty("status",&status)&&status.GetString()="acquisition-required" then
        if OciEvidence.shaBytes bytes="01fb92930f358a7898479bbe80b94988bda7e1ccc98d5013449e8b325cfad62a" then Some(Unavailable "native-image-closure-inventory-acquisition-required")
        else Some(Refused "acquisition-required-selection-bytes-differ")
    else None
let selection bytes=
    use doc=JsonDocument.Parse(bytes:byte array)
    closed doc.RootElement
    require(exactProperties (properties typeof<Selection>) doc.RootElement) "input-is-not-closed"
    let files=doc.RootElement.GetProperty("Files")
    let roles=doc.RootElement.GetProperty("RoleManifests")
    require(files.ValueKind=JsonValueKind.Array&&files.GetArrayLength()>0&&(files.EnumerateArray()|>Seq.forall(exactProperties(properties typeof<FileRow>)))) "input-is-not-closed"
    require(roles.ValueKind=JsonValueKind.Array&&roles.GetArrayLength()=4&&(roles.EnumerateArray()|>Seq.forall(exactProperties(properties typeof<RoleManifestReference>)))) "input-is-not-closed"
    JsonSerializer.Deserialize<Selection>(bytes,options)
let trusted bytes=
    use doc=JsonDocument.Parse(bytes:byte array)
    closed doc.RootElement
    require(exactProperties(properties typeof<TrustedNativeSelection>)doc.RootElement) "trusted-input-is-not-closed"
    JsonSerializer.Deserialize<TrustedNativeSelection>(bytes,options)
let emit=function
    | Prepared bytes->Console.OpenStandardOutput().Write bytes;0
    | Unavailable reason->eprintfn "persistent-v3-image-closure-unavailable: %s" reason;2
    | Refused reason->eprintfn "persistent-v3-image-closure-refused: %s" reason;3
let args=Environment.GetCommandLineArgs()|>Array.skip 1
try
    if args.Length=1 then
        let bytes=load args[0]
        Environment.ExitCode<-match placeholder bytes with Some result->emit result|None->emit(ImageClosure.prepare(selection bytes))
    else
        require(args.Length=5||args.Length=11) "usage: persistent-v3-image-closure prepare --selection ABS --trusted-native-selection ABS | run-c4 --selection ABS --trusted-native-selection ABS --parent-oci ABS --work-root FRESH_ABS --evidence-root FRESH_ABS"
        let command=args[0]
        require((command="prepare"&&args.Length=5)||(command="run-c4"&&args.Length=11)) "command-arguments"
        let flags=Dictionary<string,string>()
        for index in 1..2..args.Length-1 do require(flags.TryAdd(args[index],args[index+1])&&Path.IsPathFullyQualified args[index+1]) "duplicate-flag-or-relative-path"
        let expected=if command="prepare" then set["--selection";"--trusted-native-selection"] else set["--selection";"--trusted-native-selection";"--parent-oci";"--work-root";"--evidence-root"]
        require(Set.ofSeq flags.Keys=expected) "unknown-or-missing-flag"
        let bytes=load flags["--selection"]
        match placeholder bytes with
        | Some result->Environment.ExitCode<-emit result
        | None->
            let selected=selection bytes
            let rootTrusted=trusted(load flags["--trusted-native-selection"])
            if selected.IdentityClass="production" then
                RootlessPolicy.requireRootlessNamespace()
                RootlessPolicy.requirePrivateInput flags["--selection"]
                RootlessPolicy.requirePrivateInput flags["--trusted-native-selection"]
            match ImageClosure.prepareWithTrustedNative(Some rootTrusted)selected with
            | Prepared prepared when command="run-c4"->
                let inputs=RootlessPolicy.select selected rootTrusted flags["--selection"] flags["--trusted-native-selection"] flags["--parent-oci"] flags["--work-root"] flags["--evidence-root"] prepared
                use cancellation=new CancellationTokenSource()
                Console.CancelKeyPress.Add(fun event->event.Cancel<-true;cancellation.Cancel())
                let mechanism=RootlessMechanism(inputs,RealProcessBackend())
                let deadline=DateTimeOffset.UtcNow.AddMinutes 90.
                let state,trace=RunnerExecution.runWithBudgets RunnerExecution.productionBudgets 32 deadline cancellation.Token mechanism (Runner.initialC4 inputs.ExpectedInput)
                if Runner.c4Ready state then
                    mechanism.SealTerminal(state,trace)
                    cancellation.Token.ThrowIfCancellationRequested()
                    require(DateTimeOffset.UtcNow<deadline) "terminal-seal-deadline"
                    printfn "%s" (JsonSerializer.Serialize {|schema="fsgg.telemetry.persistent-v3-c4-result/1";status="C4Ready";input=inputs.ExpectedInput;qualificationAccepted=false;evidence=inputs.EvidenceRoot|})
                    Environment.ExitCode<-0
                else
                    eprintfn "persistent-v3-c4-not-ready: %A; refusal=%A; owned=%A; running=%A" state.Phase state.Refusal state.Owned state.Running
                    Environment.ExitCode<-3
            | result->Environment.ExitCode<-emit result
with error->eprintfn "persistent-v3-image-closure-refused: %s" error.Message;Environment.ExitCode<-3
