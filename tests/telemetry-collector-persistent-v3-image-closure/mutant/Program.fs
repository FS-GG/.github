open System
open FSGG.Telemetry.PersistentV3.ImageClosure
let stale={Runner.initial "input-1" with Phase=Revalidating;Owned=Map.ofList["a","owned-a";"b","owned-b"]}
let requested,_=Runner.nextEffect stale
let observed=Runner.observe(InputsValidated "changed-input") requested
if observed.Phase=Cleanup&&not observed.Current&&observed.Refusal=Some "InputChanged" then
  printfn "guard retained"
  Environment.ExitCode<-0
else
  eprintfn "input revalidation safety property failed"
  Environment.ExitCode<-1
