open System
open System.IO
open System.Text.Json.Nodes
open FSGG.Telemetry.PersistentV3.ImageClosure

let fail message=raise(InvalidOperationException message)
let path=Environment.GetEnvironmentVariable "PERSISTENT_V3_RUNNER_ITF"
if String.IsNullOrWhiteSpace path||not(File.Exists path) then fail "PERSISTENT_V3_RUNNER_ITF required"
let phase=function Prepared->"Prepared"|Acquired->"Acquired"|Validated->"Validated"|BuildingFirst->"BuildingFirst"|BuildingSecond->"BuildingSecond"|Comparing->"Comparing"|Cleanup->"Cleanup"|Complete->"Complete"|Refused->"Refused"
let build=function NoResult->"None"|Unknown->"Unknown"|Digest value->value
let qualification=function QualificationUnknown->"Unknown"|Accepted->"Accepted"
let actions=[Acquire;Validate;CreateStores;FirstBuilt "aaa";SecondBuilt "aaa";Qualify;RemoveStore "build-a";RemoveStore "build-b";Finish]
let actual=actions|>List.scan(fun state action->Runner.apply action state)Runner.initial
let root=JsonNode.Parse(File.ReadAllText path).AsObject()
let states=root["states"].AsArray()
if states.Count<>actual.Length then fail $"ITF state count {states.Count} differs from reducer {actual.Length}"
List.iteri(fun index expected->
  let observed=((states[index]).AsObject()["state"]).AsObject()
  let text (name:string)=observed[name].GetValue<string>()
  let boolean (name:string)=observed[name].GetValue<bool>()
  let stores=(((observed["stores"]).AsObject()["#bigint"]).GetValue<string>())|>int
  if text "phase"<>phase expected.Phase||text "first"<>build expected.First||text "second"<>build expected.Second||text "qualification"<>qualification expected.Qualification||boolean "current"<>expected.Current||boolean "cancelled"<>expected.Cancelled||boolean "cleanupFailed"<>expected.CleanupFailed||stores<>expected.Stores.Count then fail $"ITF/reducer mismatch at state {index}") actual
if not(Runner.qualificationAccepted actual[actual.Length-1]) then fail "correspondence terminal was not accepted"
printfn "persistent v3 runner canonical ITF correspondence passed: %d states" actual.Length
