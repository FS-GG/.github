// Runs the production step-5b writer (`recordSecurityObligations`) for a workspace named by KIT_TARGET with
// the no-gh pending outcome Root observed (repository not yet created; Project unreadable).
open System
open NewSddWorkspace.Program

let target = Environment.GetEnvironmentVariable "KIT_TARGET"
let options = { assembleWizardTemplateOptions target "Product" "python" None None None None with Coordinate = true }
recordSecurityObligations options { Repository = None; Project = ProjectPending("FS-GG/Coordination", "ProjectV2 visibility could not be read"); Outcome = Warned "pending" }
printfn "RECORDED"
