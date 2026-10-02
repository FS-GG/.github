open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open FSGG.Telemetry.PersistentV3.ImageClosure

let options=JsonSerializerOptions(PropertyNameCaseInsensitive=true)
let sha bytes=Convert.ToHexString(SHA256.HashData(bytes:byte array)).ToLowerInvariant()
let exactProperties (expected:Set<string>) (value:JsonElement)=value.ValueKind=JsonValueKind.Object&&(value.EnumerateObject()|>Seq.map _.Name|>Set.ofSeq)=expected
let selectionProperties=typeof<Selection>.GetProperties()|>Array.map _.Name|>Set.ofArray
let rowProperties=typeof<FileRow>.GetProperties()|>Array.map _.Name|>Set.ofArray
if Environment.GetCommandLineArgs().Length<>2 then eprintfn "usage: persistent-v3-image-closure <selection.json>"; Environment.ExitCode<-64
else
  try
    let bytes=File.ReadAllBytes(Environment.GetCommandLineArgs()[1])
    use document=JsonDocument.Parse bytes
    let mutable status=Unchecked.defaultof<JsonElement>
    if document.RootElement.TryGetProperty("status",&status) && status.GetString()="acquisition-required" then
      if sha bytes="01fb92930f358a7898479bbe80b94988bda7e1ccc98d5013449e8b325cfad62a" then
        eprintfn "persistent-v3-image-closure-unavailable: native-image-closure-inventory-acquisition-required"; Environment.ExitCode<-2
      else
        eprintfn "persistent-v3-image-closure-refused: acquisition-required-selection-bytes-differ"; Environment.ExitCode<-3
    else
      let mutable files=Unchecked.defaultof<JsonElement>
      if not(exactProperties selectionProperties document.RootElement)||not(document.RootElement.TryGetProperty("Files",&files))||files.ValueKind<>JsonValueKind.Array||files.GetArrayLength()=0||files.GetArrayLength()>8192||(files.EnumerateArray()|>Seq.exists(fun row->not(exactProperties rowProperties row))) then
        eprintfn "persistent-v3-image-closure-refused: input-is-not-closed"; Environment.ExitCode<-3
      else
        let selection=JsonSerializer.Deserialize<Selection>(bytes,options)
        match ImageClosure.prepare selection with
        | Prepared result -> Console.OpenStandardOutput().Write(result); Environment.ExitCode<-0
        | Unavailable reason -> eprintfn "persistent-v3-image-closure-unavailable: %s" reason; Environment.ExitCode<-2
        | Refused reason -> eprintfn "persistent-v3-image-closure-refused: %s" reason; Environment.ExitCode<-3
  with error -> eprintfn "persistent-v3-image-closure-refused: %s" error.Message; Environment.ExitCode<-3
