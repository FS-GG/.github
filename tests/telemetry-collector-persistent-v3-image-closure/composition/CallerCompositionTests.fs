module CallerCompositionTests

open System
open System.IO
open System.Reflection.Metadata
open System.Reflection.PortableExecutable
open FSGG.Telemetry.PersistentV3.ImageClosure

let private require condition reason=if not condition then raise(InvalidDataException reason)
// Pure correspondence checks precede FFI; they claim no native operation or
// supplying-controller profile. Real C/wire composition is a separate fixture.
let internal recipeCorrespondence (inputs:RootlessInputs)=
    let native=RootRecipeBinding.prepare inputs
    require(native.RecipeSha256=CommandRecipe.identity inputs.Program&&native.ExpectedInputSha256=inputs.ExpectedInput) "caller-native-input-digest"
    require(native.Version=1u&&native.Schema=RootRecipeBinding.Schema&&native.MaximumCommands<=70&&native.Steps.Length=native.NormalCount+12) "caller-native-shape"
    for i in 0..native.Steps.Length-1 do
        require(native.Steps[i]=inputs.Program.Steps[i]) "caller-native-selected-command-drift"
        require(not(Object.ReferenceEquals(native.Steps[i].Command.Arguments,inputs.Program.Steps[i].Command.Arguments))) "caller-native-arguments-not-copied"
    let id=String('d',64)
    for index in 0..3 do
        let logical=if index<2 then "a" else "b"
        let command=CommandRecipe.removal inputs.Program logical (index%2=1) id
        let selected=native.RemovalPrefixes[index]
        require({selected with Arguments=Array.append selected.Arguments [|id|]}=command) "caller-native-census-tail-correspondence"
    native

let testIdentity ()=
    require(typeof<ProductionCaller.Outcome>.Assembly.GetName().Name="PersistentV3.CallerComposition.Tests") "caller-test-assembly-identity"
    let moduleType=typeof<ProductionCaller.Outcome>.DeclaringType
    require(not(isNull moduleType)&&not(isNull(moduleType.GetMethod("runSourceBoundary")))) "caller-test-boundary-missing"
    require(not(isNull(moduleType.GetMethod("run")))) "caller-shared-production-entry-missing"
    let productionPath=Environment.GetEnvironmentVariable "PERSISTENT_V3_CALLER_PRODUCTION_ASSEMBLY"
    require(not(String.IsNullOrWhiteSpace productionPath)&&Path.IsPathFullyQualified productionPath) "caller-production-assembly-required"
    // Inspect immutable PE metadata only: no assembly load, entry point,
    // module initializer, native library resolution or production code runs.
    use productionStream=File.OpenRead productionPath
    use productionPe=new PEReader(productionStream)
    require(productionPe.HasMetadata) "caller-production-metadata-missing"
    let metadata=productionPe.GetMetadataReader()
    let assembly=metadata.GetAssemblyDefinition()
    require(metadata.GetString assembly.Name="PersistentV3.ImageClosure") "caller-production-assembly-identity"
    let types=metadata.TypeDefinitions |> Seq.map(fun handle->metadata.GetTypeDefinition handle) |> Seq.toArray
    let productionTypes=types |> Array.filter(fun definition->metadata.GetString definition.Namespace="FSGG.Telemetry.PersistentV3.ImageClosure"&&metadata.GetString definition.Name="ProductionCaller")
    require(productionTypes.Length=1) "caller-production-module-missing"
    let methodNames=productionTypes[0].GetMethods() |> Seq.map(fun handle->metadata.GetString(metadata.GetMethodDefinition(handle).Name)) |> Set.ofSeq
    let boundaryPresent=types |> Array.exists(fun definition->metadata.GetString definition.Name="SourceBoundary")
    require(not(methodNames.Contains "runSourceBoundary")&&not boundaryPresent) "caller-test-injection-present-in-production"
    require(methodNames.Contains "run") "caller-production-entry-missing"
    printfn "PASS distinct linked-source caller assembly and production hook absence; supplying controller/native effects NOT_RUN"
