module InactiveControls

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open FSGG.Telemetry.PersistentV3.ImageClosure

let run () =
    let digest=String('a',64)
    let expected=Map.ofList["hostConfigSha256",digest;"managerReceiptSha256",digest;"sidecarSha256",digest;"executableSha256",digest;"sourceReferenceSha256",digest;"readerProfileSha256",digest;"verifierRuntimeManifestSha256",digest]
    let value=JsonSerializer.SerializeToNode {|schema="fsgg.telemetry.inactive-preparation-read/1";status="prepared-inactive";hostConfigSha256=digest;managerReceiptSha256=digest;sidecarSha256=digest;executableSha256=digest;sourceReferenceSha256=digest;readerProfileSha256=digest;verifierRuntimeManifestSha256=digest;sourceVerification="unknown";snapshotOrigin="unknown";sharedCostCompleteness="unknown";captureQualified=false;nativeAcceptanceClaimed=false;activationAuthorized=false|}
    let encoded=value.ToJsonString()+"\n"
    InactiveQualification.validateResult expected encoded
    let refuse body=
        try InactiveQualification.validateResult expected body;failwith "malformed inactive result accepted"
        with
        | :? InvalidDataException->()
        | :? JsonException->()
        | :? InvalidOperationException->()
    for KeyValue(name,_) in expected do
        let changed=JsonNode.Parse(encoded).AsObject()
        changed[name]<-JsonValue.Create(String('b',64))
        refuse(changed.ToJsonString()+"\n")
    for name in ["captureQualified";"nativeAcceptanceClaimed";"activationAuthorized"] do
        let changed=JsonNode.Parse(encoded).AsObject()
        changed[name]<-JsonValue.Create true
        refuse(changed.ToJsonString()+"\n")
    for name in ["sourceVerification";"snapshotOrigin";"sharedCostCompleteness"] do
        let changed=JsonNode.Parse(encoded).AsObject()
        changed[name]<-JsonValue.Create "accepted"
        refuse(changed.ToJsonString()+"\n")
    refuse(encoded.TrimEnd())
    refuse("progress\n"+encoded)
    refuse(encoded+encoded)
    refuse(encoded.Replace("\"status\":\"prepared-inactive\"","\"status\":\"prepared-inactive\",\"status\":\"prepared-inactive\""))
    let extra=JsonNode.Parse(encoded).AsObject()
    extra["modelSessionStarts"]<-JsonValue.Create 0
    refuse(extra.ToJsonString()+"\n")
    // Existing runner refuses late/stale qualification, cancellation and cleanup failure.
    let prepared=Runner.initial digest
    let notPending=Runner.observe (QualificationObserved(true,digest)) prepared
    if Runner.qualificationAccepted notPending then failwith "unsolicited qualification accepted"
    printfn "PASS pure inactive closed result/hash/claim/framing controls"
