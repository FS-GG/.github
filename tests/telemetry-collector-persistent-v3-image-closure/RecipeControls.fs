module RecipeControls

open System
open System.Text
open System.Text.Json
open System.IO
open FSGG.Telemetry.PersistentV3.ImageClosure

let private require condition reason=if not condition then failwith reason
let private refused action=
    let mutable rejected=false
    try action() with :? InvalidDataException | :? IOException | :? JsonException -> rejected<-true
    require rejected "recipe accepted changed input/command bytes"

let run (selection:Selection) (trusted:TrustedNativeSelection) (prepared:byte array)=
    let custody=String('c',64)
    let make target=CommandRecipe.prepare selection trusted prepared custody target "/recipe/parent.oci.tar" "/recipe/work" "/recipe/evidence" (String('a',32)) (String('b',32)) 32
    let c4=make "run-c4"
    let full=make "run-inactive"
    let validate target program=CommandRecipe.validate selection trusted prepared custody target "/recipe/parent.oci.tar" "/recipe/work" "/recipe/evidence" program
    validate "run-c4" c4
    validate "run-inactive" full
    require(c4.NormalCommands=20&&full.NormalCommands=26&&c4.CleanupCommands=12&&full.CleanupCommands=12&&c4.MaximumCommands=64&&full.MaximumCommands=70) "actual finite producer count drift"
    let raw=CommandRecipe.bytes full
    require(CommandRecipe.bytes(CommandRecipe.parse raw)=raw) "canonical recipe roundtrip differs"
    for text in [Encoding.UTF8.GetString raw+" ";Encoding.UTF8.GetString(raw).Replace("\"MaximumCommands\":70","\"MaximumCommands\":7e1");Encoding.UTF8.GetString(raw).Replace("\"MaximumCommands\":70","\"MaximumCommands\":70,\"MaximumCommands\":70");Encoding.UTF8.GetString(raw).Replace("\"Target\":\"run-inactive\"","\"Target\":null");Encoding.UTF8.GetString(raw).Replace("\"Target\":\"run-inactive\"","\"Target\":\"run-inactive\",\"unknown\":1")] do
        refused(fun()->CommandRecipe.parse(Encoding.UTF8.GetBytes text)|>ignore)
    let replace command=
        let rows=Array.copy c4.Steps
        rows[0]<-{rows[0] with Command=command}
        {c4 with Steps=rows}
    let original=c4.Steps[0].Command
    for changed in [
        {original with Executable="/bin/sh"}
        {original with Arguments=Array.append original.Arguments [|"--remote=true"|]}
        {original with Environment=original.Environment.Add("CONTAINER_HOST","foreign")}
        {original with WorkingDirectory="/recipe/foreign"}
        {original with OutputLimit=original.OutputLimit+1}] do refused(fun()->validate "run-c4" (replace changed))
    refused(fun()->validate "run-c4" {c4 with Selection={c4.Selection with NonceA=String('d',32)}})
    refused(fun()->validate "run-c4" {c4 with Selection={c4.Selection with Target="run-inactive"}})
    refused(fun()->validate "run-inactive" c4)
    refused(fun()->CommandRecipe.validate selection trusted prepared (String('d',64)) "run-c4" "/recipe/parent.oci.tar" "/recipe/work" "/recipe/evidence" c4)
    refused(fun()->CommandRecipe.validate selection trusted prepared custody "run-c4" "/recipe/other.oci.tar" "/recipe/work" "/recipe/evidence" c4)
    refused(fun()->CommandRecipe.validate selection trusted (Array.append prepared [|0uy|]) custody "run-c4" "/recipe/parent.oci.tar" "/recipe/work" "/recipe/evidence" c4)
    refused(fun()->CommandRecipe.validate {selection with HostSourceRevision=String('f',40)} trusted prepared custody "run-c4" "/recipe/parent.oci.tar" "/recipe/work" "/recipe/evidence" c4)
    refused(fun()->CommandRecipe.validate selection {trusted with PythonExecutable="/foreign/python"} prepared custody "run-c4" "/recipe/parent.oci.tar" "/recipe/work" "/recipe/evidence" c4)
    for limit in [-1;33] do refused(fun()->CommandRecipe.prepare selection trusted prepared custody "run-c4" "/recipe/parent.oci.tar" "/recipe/work" "/recipe/evidence" (String('a',32)) (String('b',32)) limit|>ignore)
    let returned=CommandRecipe.step c4 "a" "parent-load"
    returned.Arguments[0]<-"mutated-consumer-copy"
    validate "run-c4" c4
    require(CommandRecipe.identity c4<>CommandRecipe.identity full) "target not bound into recipe identity"
    printfn "PASS actual command producer/parser: C4/full counts, canonical closed bytes, argv/env/cwd/nonce/target/input joins, cleanup ceilings and copied command arrays"
