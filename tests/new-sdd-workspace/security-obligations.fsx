// Native offline controls for the Wizard-owned security state (FS-GG/.github#4964). They run against the
// exact compiled Creator and a temporary workspace: no network, no process, no credential.
//   1. recordSecurityObligations (the production step-5b writer) writes only .fsgg/security-obligations.json
//      and leaves the SDD-owned provenance byte-identical;
//   2. obligations round-trip through the file, reruns do not duplicate, receipts survive;
//   3. a legacy provenance carrying the Wizard 0.23.0 members is migrated tolerantly and idempotently;
//   4. malformed or foreign files are never overwritten;
//   5. the retrofit log follows the same ownership rule.
open System
open System.IO
open System.Text.Json.Nodes
open NewSddWorkspace
open NewSddWorkspace.Program

let fail message = failwith message
let expect condition message = if not condition then fail message
let temporary () =
    let root = Path.Combine(Path.GetTempPath(), "security-obligations-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(Path.Combine(root, ".fsgg")) |> ignore
    root
let provenancePath root = Path.Combine(root, ".fsgg", "scaffold-provenance.json")
let sddProvenance =
    """{"schemaVersion":3,"generator":{"id":"FS.GG.SDD.Artifacts","version":"2.5.3"},"declaration":{"providerName":"go-cli"},"observation":{"outcome":"providerSucceeded"},"ownership":{"sddOwnedPaths":[]}}"""
let seedProvenance root (extra: (string * string) list) =
    let json = JsonNode.Parse(sddProvenance).AsObject()
    for key, value in extra do json.[key] <- JsonNode.Parse value
    File.WriteAllText(provenancePath root, json.ToJsonString())
let members (path: string) = (JsonNode.Parse(File.ReadAllText path).AsObject()) |> Seq.map (fun kv -> kv.Key) |> Set.ofSeq
let rowsOf (root: string) (key: string) =
    match SecurityObligations.load root with
    | Ok(Some state) -> state.[key].AsArray() |> Seq.map (fun n -> n.AsObject()) |> List.ofSeq
    | other -> fail (sprintf "state unreadable: %A" other)
let text (row: JsonObject) (key: string) = row.[key].GetValue<string>()

let options root =
    { assembleWizardTemplateOptions root "Product" "python" None None None None with
        Coordinate = true
        BoardOwner = "FS-GG"
        BoardTitle = "Coordination" }

// ── 1 · the production writer leaves SDD provenance alone ───────────────────────────────────────────
let pendingReport =
    { Repository = None
      Project = ProjectPending("FS-GG/Coordination", "ProjectV2 visibility could not be read")
      Outcome = Warned "pending" }
let fresh = temporary ()
seedProvenance fresh []
let provenanceBefore = File.ReadAllBytes(provenancePath fresh)
recordSecurityObligations (options fresh) pendingReport
expect (File.ReadAllBytes(provenancePath fresh) = provenanceBefore) "the Wizard rewrote the SDD-owned provenance"
expect (File.Exists(SecurityObligations.filePath fresh)) "the Wizard-owned obligations file was not written"
expect (SecurityObligations.filePath fresh = Path.Combine(fresh, ".fsgg", "security-obligations.json")) "unexpected obligations path"
let state = JsonNode.Parse(File.ReadAllText(SecurityObligations.filePath fresh)).AsObject()
expect (state.["schema"].GetValue<string>() = SecurityObligations.schemaId) "missing schema stamp"
let obligations = rowsOf fresh "securityObligations"
expect (obligations.Length = 2) "expected the repository and Project obligations"
let repository = obligations |> List.find (fun r -> text r "kind" = "repository-issue-policy")
let project = obligations |> List.find (fun r -> text r "kind" = "project-access")
expect (text repository "state" = "pending" && text repository "target" = "repository-not-yet-created" && text repository "targetPolicy" = "COLLABORATORS_ONLY") "repository obligation content changed"
expect ((text repository "resume").StartsWith "new-sdd-workspace secure ") "repository resume command changed"
expect (text project "state" = "pending-human-verification" && text project "target" = "FS-GG/Coordination" && text project "expectedBasePermission" = "READ") "project obligation content changed"
expect ((text project "humanVerification").Contains "Manage access" && (text project "resume").Contains "'--project' 'FS-GG/Coordination'") "project human verification or resume text changed"

// ── 2 · round trip, rerun without duplication, receipts preserved ───────────────────────────────────
recordSecurityObligations (options fresh) pendingReport
expect ((rowsOf fresh "securityObligations").Length = 2) "a rerun duplicated obligations"
let partial =
    { Repository = None
      Project =
        ProjectPartiallyVerified(
            "FS-GG/Coordination", "PVT_1", true, "alice",
            [ { Requested = "alice"; ActorKind = "User"; ActorId = "U_1" } ]
        )
      Outcome = Warned "partial" }
recordSecurityObligations (options fresh) partial
let afterPartial = rowsOf fresh "securityObligations"
expect (afterPartial |> List.exists (fun r -> text r "kind" = "project-base-access-human-verification" && text r "target" = "FS-GG/Coordination")) "partial Project verification lost its base-access obligation"
let receipts = rowsOf fresh "verifiedSecurityReceipts"
expect (receipts.Length = 1 && text receipts.Head "kind" = "project-access" && text receipts.Head "project" = "FS-GG/Coordination") "the verified receipt is not in the Wizard-owned file"
expect (File.ReadAllBytes(provenancePath fresh) = provenanceBefore) "provenance changed while recording receipts"
expect (not ((members (provenancePath fresh)).Contains "securityObligations") && not ((members (provenancePath fresh)).Contains "verifiedSecurityReceipts")) "provenance carries a Wizard member"

// ── 3 · legacy migration ──────────────────────────────────────────────────────────────────────────
let legacy = temporary ()
let legacyRows =
    [ "securityObligations",
      """[{"kind":"repository-issue-policy","target":"acme/app","state":"pending","resume":"new-sdd-workspace secure '/w' '--repo' 'acme/app'"},
          {"kind":"project-access","target":"acme/Roadmap","humanVerification":"Project → Settings → Manage access","resume":"r","state":"pending-human-verification"}]"""
      "verifiedSecurityReceipts", """[{"kind":"repository-issue-policy","repository":"acme/old","finalPolicy":"COLLABORATORS_ONLY"}]""" ]
seedProvenance legacy legacyRows
let sddMembers = members (provenancePath legacy) |> Set.filter (fun m -> m <> "securityObligations" && m <> "verifiedSecurityReceipts")
expect (SecurityObligations.migrateLegacy legacy = SecurityObligations.Migrated(2, 1)) "legacy rows were not migrated"
expect (members (provenancePath legacy) = sddMembers) "migration did not remove exactly the Wizard members"
expect (JsonNode.Parse(File.ReadAllText(provenancePath legacy)).AsObject().["generator"].["version"].GetValue<string>() = "2.5.3") "migration altered an SDD member"
expect ((rowsOf legacy "securityObligations").Length = 2 && (rowsOf legacy "verifiedSecurityReceipts").Length = 1) "migrated rows differ"
let migratedRepo = rowsOf legacy "securityObligations" |> List.find (fun r -> text r "kind" = "repository-issue-policy")
expect (text migratedRepo "resume" = "new-sdd-workspace secure '/w' '--repo' 'acme/app'") "migration altered a resume command"
expect (SecurityObligations.migrateLegacy legacy = SecurityObligations.NothingToMigrate) "migration is not idempotent"

// an interrupted migration (state written, provenance not yet cleaned) converges without duplicates
let interrupted = temporary ()
seedProvenance interrupted legacyRows
SecurityObligations.save interrupted (let s = SecurityObligations.emptyState () in (s.["securityObligations"] <- JsonNode.Parse("""[{"kind":"repository-issue-policy","target":"acme/app","state":"verified-newer"}]"""); s)) |> ignore
expect (SecurityObligations.migrateLegacy interrupted = SecurityObligations.Migrated(2, 1)) "interrupted migration did not converge"
let converged = rowsOf interrupted "securityObligations"
expect (converged.Length = 2) "interrupted migration duplicated a row"
expect (text (converged |> List.find (fun r -> text r "target" = "acme/app")) "state" = "verified-newer") "a legacy row overwrote the newer Wizard-owned row"

// load migrates tolerantly when the next reader is the resume path
let viaLoad = temporary ()
seedProvenance viaLoad legacyRows
expect ((rowsOf viaLoad "securityObligations").Length = 2) "load did not migrate before reading"
expect (not ((members (provenancePath viaLoad)).Contains "securityObligations")) "load left the legacy member in provenance"

// ── 4 · foreign or malformed files are never overwritten ────────────────────────────────────────────
let malformed = temporary ()
seedProvenance malformed legacyRows
File.WriteAllText(SecurityObligations.filePath malformed, "{broken")
let provenanceWithLegacy = File.ReadAllBytes(provenancePath malformed)
(match SecurityObligations.migrateLegacy malformed with
 | SecurityObligations.Incomplete _ -> ()
 | other -> fail (sprintf "malformed Wizard file was not reported incomplete: %A" other))
expect (File.ReadAllBytes(provenancePath malformed) = provenanceWithLegacy) "provenance changed although migration could not complete"
expect (File.ReadAllText(SecurityObligations.filePath malformed) = "{broken") "malformed Wizard file was overwritten"
(match SecurityObligations.load malformed with Error _ -> () | _ -> fail "a malformed Wizard file loaded")

let foreignSchema = temporary ()
File.WriteAllText(SecurityObligations.filePath foreignSchema, """{"schema":"other/1","securityObligations":[]}""")
(match SecurityObligations.load foreignSchema with Error _ -> () | _ -> fail "a foreign schema loaded")
let unreadableProvenance = temporary ()
File.WriteAllText(provenancePath unreadableProvenance, "{broken")
expect (SecurityObligations.migrateLegacy unreadableProvenance = SecurityObligations.NothingToMigrate) "unreadable provenance must be left to SDD"
expect (File.ReadAllText(provenancePath unreadableProvenance) = "{broken") "unreadable provenance was touched"
expect (SecurityObligations.load unreadableProvenance = Ok None) "no state expected without a file"

// ── 5 · the retrofit log ─────────────────────────────────────────────────────────────────────────────
let retrofit = temporary ()
seedProvenance retrofit [ ("retrofits", """[{"kind":"coordination","ref":"main","materialized":["a"],"reMaterializedDrift":[]}]""") ]
let entry = JsonObject()
entry.["kind"] <- JsonValue.Create "coordination"
entry.["ref"] <- JsonValue.Create "pinned"
expect (RetrofitLog.append retrofit entry = Ok()) "retrofit entry not appended"
(match RetrofitLog.entries retrofit with
 | Ok entries -> expect (entries.Length = 2 && entries.[0].["ref"].GetValue<string>() = "main" && entries.[1].["ref"].GetValue<string>() = "pinned") "retrofit log entries or order differ"
 | Error reason -> fail reason)
expect (not ((members (provenancePath retrofit)).Contains "retrofits")) "legacy retrofit member stayed in provenance"
expect (RetrofitLog.migrateLegacy retrofit = Ok 0) "retrofit migration is not idempotent"
let noProvenance = temporary ()
expect (RetrofitLog.append noProvenance entry = Ok()) "retrofit append without provenance failed"
expect (not (File.Exists(provenancePath noProvenance))) "the Wizard created a provenance file"

printfn "security-obligations controls: OK"
