module CallerCompositionTests

open System
open System.IO
open System.Reflection
open FSGG.Telemetry.PersistentV3.ImageClosure

let private require condition reason=if not condition then raise(InvalidDataException reason)
// Pure correspondence checks precede FFI; they claim no native operation or
// supplying-controller profile. Real C/wire composition is a separate fixture.
let recipeCorrespondence (inputs:RootlessInputs)=
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
    let production=Assembly.LoadFile productionPath
    require(production.GetName().Name="PersistentV3.ImageClosure") "caller-production-assembly-identity"
    let productionType=production.GetType("FSGG.Telemetry.PersistentV3.ImageClosure.ProductionCaller",true)
    require(isNull(productionType.GetMethod("runSourceBoundary"))&&isNull(production.GetType("FSGG.Telemetry.PersistentV3.ImageClosure.ProductionCaller+SourceBoundary"))) "caller-test-injection-present-in-production"
    require(not(isNull(productionType.GetMethod("run")))) "caller-production-entry-missing"
    printfn "PASS distinct linked-source caller assembly and production hook absence; supplying controller/native effects NOT_RUN"
