// Native offline controls for the pinned coordination kit (FS-GG/.github#4964). They run against the exact
// compiled Creator with an injected fetcher: no network and no process. The e2e retrofit/creation-step
// controls live in coordination-kit-pin.py.
open System
open System.Text
open NewSddWorkspace
open NewSddWorkspace.CoordinationKit

let fail message = failwith message
let expect condition message = if not condition then fail message
let bytes (value: string) = Encoding.UTF8.GetBytes value
let sha (value: string) = sha256Hex (bytes value)

// A tiny but structurally faithful kit.
let skillText = "# skill\n"
let referenceText = "reference\n"
let manifest (skillSha: string) (referenceSha: string) (path: string) =
    sprintf """{"schemaVersion":2,"skills":[{"id":"check-board","files":[{"path":"SKILL.md","sha256":"%s","executable":false},{"path":"%s","sha256":"%s","executable":true}]}]}""" skillSha path referenceSha
let manifestText = manifest (sha skillText) (sha referenceText) "references/run.sh"
let shimText = "#!/usr/bin/env bash\necho shim\n"
let toolsText = """{"version":1,"isRoot":true,"tools":{"fs.gg.coord.cli":{"version":"0.113.0","commands":["fsgg-coord-engine"]}}}"""

let pin: Pin =
    { Repository = "FS-GG/.github"
      Revision = String('a', 40)
      ManifestSha256 = sha manifestText
      ShimSha256 = sha shimText
      ToolManifestSha256 = sha toolsText }

let served =
    dict [ "registry/coordination-kit-skill-manifest.json", manifestText
           ".claude/skills/check-board/SKILL.md", skillText
           ".claude/skills/check-board/references/run.sh", referenceText
           "scripts/fsgg-coord", shimText
           "dist/dotnet/.config/dotnet-tools.json", toolsText ]

let fetchFrom (overrides: (string * Result<byte[], string>) list) (path: string) =
    match overrides |> List.tryFind (fun (p, _) -> p = path) with
    | Some(_, result) -> result
    | None ->
        match served.TryGetValue path with
        | true, value -> Ok(bytes value)
        | _ -> Error(sprintf "HTTP 404 fetching %s" path)

// ── verified kit ─────────────────────────────────────────────────────────────────────────────────────
match fetchVerified pin (fetchFrom []) with
| Ok kit ->
    expect kit.HasSkillManifest "the verified manifest was not recognised"
    expect (kit.Skills.Length = 2 && kit.Listed.Length = 2) "skill files differ"
    expect (kit.Skills |> List.exists (fun f -> f.Path = "references/run.sh" && f.Executable && f.Content = referenceText)) "executable bit or content lost"
    expect (kit.Shim = Some shimText && kit.ToolManifest = Some toolsText && kit.Unavailable.IsEmpty) "shim, tools or unavailable list differ"
| Error reasons -> fail (sprintf "valid kit refused: %A" reasons)

// ── digest mismatch is a refusal, whichever file changed ───────────────────────────────────────────────
let tampered (path: string) = [ (path, Ok(bytes "tampered\n")) ]
for path in [ "registry/coordination-kit-skill-manifest.json"; ".claude/skills/check-board/SKILL.md"; ".claude/skills/check-board/references/run.sh"; "scripts/fsgg-coord"; "dist/dotnet/.config/dotnet-tools.json" ] do
    match fetchVerified pin (fetchFrom (tampered path)) with
    | Error reasons -> expect (reasons |> List.exists (fun r -> r.StartsWith path && r.Contains "differs from the pinned")) (sprintf "refusal for %s does not name it: %A" path reasons)
    | Ok _ -> fail (sprintf "tampered %s was accepted" path)

// a changed manifest that is internally consistent is still refused: its own digest is pinned
let swapped = manifest (sha "other\n") (sha referenceText) "references/run.sh"
(match fetchVerified pin (fetchFrom [ "registry/coordination-kit-skill-manifest.json", Ok(bytes swapped); ".claude/skills/check-board/SKILL.md", Ok(bytes "other\n") ]) with
 | Error _ -> ()
 | Ok _ -> fail "a self-consistent but unpinned manifest was accepted")

// BOM-prefixed or non-UTF-8 bytes whose digest matches are refused rather than rewritten
let invalid = [| 0xC3uy; 0x28uy |]
let invalidPin = { pin with ShimSha256 = sha256Hex invalid }
(match fetchVerified invalidPin (fetchFrom [ ("scripts/fsgg-coord", Ok invalid) ]) with
 | Error reasons -> expect (reasons |> List.exists (fun r -> r.Contains "not UTF-8")) "non-UTF-8 reason missing"
 | Ok _ -> fail "invalid UTF-8 was accepted")

// ── layout: a manifest that escapes the skill directory is refused even when its digest is pinned ─────────
for bad in [ "../escape"; "/abs"; "a//b"; ".hidden/x"; "a\\b" ] do
    let hostile = manifest (sha skillText) (sha referenceText) bad
    let hostilePin = { pin with ManifestSha256 = sha hostile }
    match fetchVerified hostilePin (fetchFrom [ ("registry/coordination-kit-skill-manifest.json", Ok(bytes hostile)) ]) with
    | Error reasons -> expect (reasons |> List.exists (fun r -> r.Contains "closed layout")) (sprintf "layout reason missing for %s" bad)
    | Ok _ -> fail (sprintf "manifest path %s was accepted" bad)

// ── unavailable (not mismatched) stays a warning ───────────────────────────────────────────────────────────
(match fetchVerified pin (fetchFrom [ "scripts/fsgg-coord", Error "HTTP 404 fetching scripts/fsgg-coord"; ".claude/skills/check-board/references/run.sh", Error "timeout" ]) with
 | Ok kit ->
    expect (kit.Shim.IsNone && kit.Unavailable.Length = 2) "unavailable files were not reported"
    expect (kit.Skills.Length = 1 && kit.Listed.Length = 2) "a missing file must still be listed by the verified manifest"
 | Error reasons -> fail (sprintf "an unavailable file became a refusal: %A" reasons))
(match fetchVerified pin (fetchFrom [ ("registry/coordination-kit-skill-manifest.json", Error "offline") ]) with
 | Ok kit -> expect (not kit.HasSkillManifest && kit.Skills.IsEmpty && kit.Listed.IsEmpty) "an unreachable manifest must not close the skill set"
 | Error reasons -> fail (sprintf "an unreachable manifest became a refusal: %A" reasons))

// ── pin document ─────────────────────────────────────────────────────────────────────────────────────────
let pinJson revision manifestDigest extra =
    sprintf """{"schema":"fsgg.new-sdd-workspace-coordination-kit-pin/1","repository":"FS-GG/.github","revision":"%s","anchors":{"registry/coordination-kit-skill-manifest.json":"%s","scripts/fsgg-coord":"%s","dist/dotnet/.config/dotnet-tools.json":"%s"}%s}""" revision manifestDigest pin.ShimSha256 pin.ToolManifestSha256 extra
expect (parsePin (pinJson pin.Revision pin.ManifestSha256 "") = Ok pin) "a valid pin document did not parse"
for invalidDoc in [ pinJson "main" pin.ManifestSha256 ""; pinJson (String('A', 40)) pin.ManifestSha256 ""; pinJson pin.Revision "abc" ""; pinJson pin.Revision pin.ManifestSha256 ",\"extra\":1"; "{}"; "not json" ] do
    match parsePin invalidDoc with
    | Error _ -> ()
    | Ok _ -> fail (sprintf "invalid pin accepted: %s" invalidDoc)

// ── revision selection and URL ───────────────────────────────────────────────────────────────────────────────
expect (selectRevision pin pin.Revision = Ok pin.Revision) "the pinned revision was not accepted"
for requested in [ "main"; "v1"; String('b', 40); pin.Revision.Substring(0, 12) ] do
    match selectRevision pin requested with
    | Error reason -> expect (reason.Contains pin.Revision) "refusal does not name the pinned revision"
    | Ok _ -> fail (sprintf "unpinned revision %s accepted" requested)
expect (rawUrl "https://raw.githubusercontent.com/" pin "scripts/fsgg-coord" = sprintf "https://raw.githubusercontent.com/FS-GG/.github/%s/scripts/fsgg-coord" pin.Revision) "raw URL differs"

// ── the pin embedded in this tool is the closed, well-formed pin; it is never a moving ref ──────────────────────
match embeddedPin () with
| Ok embedded -> expect (embedded.Repository = "FS-GG/.github" && embedded.Revision.Length = 40 && embedded.Revision <> "main") "embedded pin is not a pinned revision"
| Error reason -> fail reason

printfn "coordination-kit controls: OK"
