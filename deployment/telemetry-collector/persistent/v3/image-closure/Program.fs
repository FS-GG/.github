open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open FSGG.Telemetry.PersistentV3.ImageClosure

let options=JsonSerializerOptions(PropertyNameCaseInsensitive=true)
let sha bytes=Convert.ToHexString(SHA256.HashData(bytes:byte array)).ToLowerInvariant()
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
      let selection=JsonSerializer.Deserialize<Selection>(bytes,options)
      match ImageClosure.prepare selection with
      | Prepared result -> Console.OpenStandardOutput().Write(result); Environment.ExitCode<-0
      | Unavailable reason -> eprintfn "persistent-v3-image-closure-unavailable: %s" reason; Environment.ExitCode<-2
      | Refused reason -> eprintfn "persistent-v3-image-closure-refused: %s" reason; Environment.ExitCode<-3
  with error -> eprintfn "persistent-v3-image-closure-refused: %s" error.Message; Environment.ExitCode<-3
