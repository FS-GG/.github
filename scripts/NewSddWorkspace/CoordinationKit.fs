module NewSddWorkspace.CoordinationKit

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json.Nodes
open System.Text.RegularExpressions

type Pin =
    { Repository: string
      Revision: string
      EmbeddedSkills: string list
      ManifestSha256: string
      ShimSha256: string
      ToolManifestSha256: string }

type SkillFile =
    { Skill: string
      Path: string
      Executable: bool
      Content: string }

type Kit =
    { HasSkillManifest: bool
      Listed: (string * string) list
      Skills: SkillFile list
      Shim: string option
      ToolManifest: string option
      Unavailable: string list }

let private manifestPath = "registry/coordination-kit-skill-manifest.json"
let private shimPath = "scripts/fsgg-coord"
let private toolManifestPath = "dist/dotnet/.config/dotnet-tools.json"
let private pinSchema = "fsgg.new-sdd-workspace-coordination-kit-pin/1"

let sha256Hex (bytes: byte[]) =
    SHA256.HashData bytes |> Convert.ToHexString |> fun s -> s.ToLowerInvariant()

let private isDigest (value: string) = Regex.IsMatch(value, "^[0-9a-f]{64}$")
let private isRevision (value: string) = Regex.IsMatch(value, "^[0-9a-f]{40}$")

let private safeSkillId (value: string) =
    Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9._-]*$") && value <> ".."

let parsePin (json: string) : Result<Pin, string> =
    try
        let root = JsonNode.Parse(json).AsObject()
        let members = root |> Seq.map (fun kv -> kv.Key) |> Set.ofSeq

        if members <> Set.ofList [ "schema"; "repository"; "revision"; "anchors"; "embeddedSkills" ] then
            Error "coordination kit pin has a non-closed member set"
        elif root.["schema"].GetValue<string>() <> pinSchema then
            Error "coordination kit pin declares an unsupported schema"
        else
            let anchors = root.["anchors"].AsObject()
            let anchorNames = anchors |> Seq.map (fun kv -> kv.Key) |> Set.ofSeq

            if anchorNames <> Set.ofList [ manifestPath; shimPath; toolManifestPath ] then
                Error "coordination kit pin has a non-closed anchor set"
            else
                let digest (path: string) = anchors.[path].GetValue<string>()

                let embedded =
                    root.["embeddedSkills"].AsArray() |> Seq.map (fun n -> n.GetValue<string>()) |> List.ofSeq

                let pin =
                    { Repository = root.["repository"].GetValue<string>()
                      Revision = root.["revision"].GetValue<string>()
                      EmbeddedSkills = embedded
                      ManifestSha256 = digest manifestPath
                      ShimSha256 = digest shimPath
                      ToolManifestSha256 = digest toolManifestPath }

                if pin.Repository <> "FS-GG/.github" then
                    Error "coordination kit pin names an unexpected repository"
                elif embedded |> List.exists (fun id -> not (safeSkillId id)) || List.length (List.distinct embedded) <> List.length embedded then
                    Error "coordination kit pin has a malformed or repeated embedded skill id"
                elif not (isRevision pin.Revision) then
                    Error "coordination kit pin revision must be a full lowercase commit sha"
                elif not (isDigest pin.ManifestSha256 && isDigest pin.ShimSha256 && isDigest pin.ToolManifestSha256) then
                    Error "coordination kit pin carries a malformed sha256 anchor"
                else
                    Ok pin
    with ex ->
        Error("coordination kit pin is not readable: " + ex.Message)

let embeddedPin () : Result<Pin, string> =
    let assembly = Reflection.Assembly.GetExecutingAssembly()
    use stream = assembly.GetManifestResourceStream "NewSddWorkspace.CoordinationKitPin.json"

    if isNull stream then
        Error "the embedded coordination kit pin is missing"
    else
        use reader = new StreamReader(stream)
        parsePin (reader.ReadToEnd())

let rawUrl (rawBase: string) (pin: Pin) (path: string) =
    sprintf "%s/%s/%s/%s" (rawBase.TrimEnd '/') pin.Repository pin.Revision path

let selectRevision (pin: Pin) (requested: string) : Result<string, string> =
    if requested = pin.Revision then
        Ok pin.Revision
    else
        Error(
            sprintf
                "the coordination kit is pinned to %s; '%s' cannot be verified against the pinned digests"
                pin.Revision
                requested
        )

let private strictUtf8 = UTF8Encoding(false, true)

/// A manifest path is one `/`-separated relative path of simple segments; no traversal or separators.
let private safeSegment (value: string) =
    Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9._-]*$") && value <> ".."

let private safeRelativePath (value: string) =
    not (String.IsNullOrEmpty value)
    && (value.Split '/' |> Array.forall safeSegment)

type private Verified =
    | Verified of string
    | Unfetched of string
    | Refused of string

let private decode (path: string) (expected: string) (fetched: Result<byte[], string>) =
    match fetched with
    | Error reason -> Unfetched(sprintf "%s: %s" path reason)
    | Ok bytes when sha256Hex bytes <> expected ->
        Refused(sprintf "%s: sha256 %s differs from the pinned %s" path (sha256Hex bytes) expected)
    | Ok bytes ->
        try
            let offset = if bytes.Length >= 3 && bytes.[0] = 0xEFuy && bytes.[1] = 0xBBuy && bytes.[2] = 0xBFuy then 3 else 0
            Verified(strictUtf8.GetString(bytes, offset, bytes.Length - offset))
        with _ ->
            Refused(sprintf "%s: digest matches but the bytes are not UTF-8 text" path)

let fetchVerified (pin: Pin) (fetch: string -> Result<byte[], string>) : Result<Kit, string list> =
    let refusals = ResizeArray<string>()
    let unavailable = ResizeArray<string>()

    let take (path: string) (expected: string) =
        match decode path expected (fetch path) with
        | Verified text -> Some text
        | Unfetched reason ->
            unavailable.Add reason
            None
        | Refused reason ->
            refusals.Add reason
            None

    let manifestText = take manifestPath pin.ManifestSha256

    let manifestRows =
        match manifestText with
        | None -> None
        | Some text ->
            try
                let root = JsonNode.Parse(text).AsObject()

                if root.["schemaVersion"].GetValue<int>() <> 2 then
                    refusals.Add(sprintf "%s: unsupported schemaVersion" manifestPath)
                    None
                else
                    root.["skills"].AsArray()
                    |> Seq.collect (fun skill ->
                        let row = skill.AsObject()
                        let id = row.["id"].GetValue<string>()

                        row.["files"].AsArray()
                        |> Seq.map (fun file ->
                            let f = file.AsObject()
                            id, f.["path"].GetValue<string>(), f.["executable"].GetValue<bool>(), f.["sha256"].GetValue<string>()))
                    |> List.ofSeq
                    |> List.filter (fun (id, _, _, _) -> not (List.contains id pin.EmbeddedSkills))
                    |> Some
            with ex ->
                refusals.Add(sprintf "%s: malformed (%s)" manifestPath ex.Message)
                None

    let rows =
        match manifestRows with
        | Some rows ->
            let layoutOk =
                rows
                |> List.forall (fun (id, rel, _, sha) -> safeSegment id && safeRelativePath rel && isDigest sha)

            if layoutOk then
                rows
            else
                refusals.Add(sprintf "%s: a skill id, path or digest is outside the closed layout" manifestPath)
                []
        | None -> []

    let skills =
        rows
        |> List.choose (fun (id, rel, executable, sha) ->
            take (sprintf ".claude/skills/%s/%s" id rel) sha
            |> Option.map (fun content ->
                { Skill = id
                  Path = rel
                  Executable = executable
                  Content = content }))

    let shim = take shimPath pin.ShimSha256
    let tools = take toolManifestPath pin.ToolManifestSha256

    if refusals.Count > 0 then
        Error(List.ofSeq refusals)
    else
        Ok
            { HasSkillManifest = manifestText.IsSome
              Listed = rows |> List.map (fun (id, rel, _, _) -> id, rel)
              Skills = skills
              Shim = shim
              ToolManifest = tools
              Unavailable = List.ofSeq unavailable }
