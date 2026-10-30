// Drives the production creation-time kit step (`wireLegacyCoordination`, step 5) against the target
// named by KIT_TARGET, with the kit host relocated to a local server by FSGG_COORDINATION_KIT_RAW_BASE
// (the pinned revision and every sha256 are still enforced). Prints the outcome for coordination-kit-pin.py.
open System
open NewSddWorkspace.Program

let target = Environment.GetEnvironmentVariable "KIT_TARGET"
let options = { assembleWizardTemplateOptions target "Product" "python" None None None None with Coordinate = true }
match wireLegacyCoordination options with
| Succeeded -> printfn "OUTCOME Succeeded"
| Warned note -> printfn "OUTCOME Warned %s" note
| Skipped reason -> printfn "OUTCOME Skipped %s" reason
| Failed note -> printfn "OUTCOME Failed %s" note
