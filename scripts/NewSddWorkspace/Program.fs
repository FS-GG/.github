/// new-sdd-workspace — scaffold an FS.GG workspace (SDD lifecycle + runnable Rendering
/// app + optional Governance overlay) using only existing, published machinery, with NO
/// FS.GG.Templates checkout required. This is the sole workspace scaffolder — the F# successor
/// to the retired scripts/new-sdd-fullstack.sh (ADR-0016; renamed from new-sdd-fullstack once
/// `--profile` made the output-app shape selectable — "workspace" is the ADR-0020 term).
///
/// It orchestrates the commands that already exist today:
///   1. fetch the newest rendering provider descriptor from FS.GG.Templates (HTTP, no clone)
///   2. update fsgg-sdd          (self-update to the newest build — DEFAULT; --pinned skips)  [non-fatal]
///   3. fsgg-sdd scaffold        (SDD lifecycle skeleton + runnable Rendering app)            [fatal]
///   4. governance overlay       (dotnet new fs-gg-governance — default on)                   [non-fatal]
///   5. coordination wiring      (vendor the kit + write FSGG_COORD_* env — default on)       [non-fatal]
///   6. fsgg-sdd doctor          (read-only coherence check)                                  [non-fatal]
///   7. fsgg-sdd upgrade         (optional --upgrade — reconcile an existing project)         [fatal]
///
/// Currency is the DEFAULT (ADR-0030, the creation-time carve-out to ADR-0009): the CLI
/// self-updates to the newest coherent set BEFORE scaffolding, so a fresh workspace is always
/// produced by current tooling. --pinned skips the self-update; --pinned with --ref <tag> gives a
/// fully reproducible, pinned scaffold. ADR-0009's "no silent auto-update" still governs the
/// in-project fsgg-sdd verbs — this default is only the create-a-new-workspace step.
///
/// It also carries a `retrofit <target>` subcommand — the INVERSE of step 5's scaffold-time wiring:
/// run inside an ALREADY-scaffolded workspace (one made --no-coordination, or before the wiring
/// existed), it idempotently materializes the coordination kit + board env onto it, re-emitting only
/// what drifted and recording the event in scaffold-provenance.json. It is the precondition work-board
/// (ADR-0064) documents but cannot itself satisfy.
module NewSddWorkspace.Program

open System
open System.IO
open System.Diagnostics
open System.Net.Http
open System.Runtime.InteropServices
open System.Text.Json
open System.Text.Json.Nodes
open Spectre.Console

// ── Model ────────────────────────────────────────────────────────────────────

/// The outcome of one orchestration step — the raw material for the "what worked /
/// what didn't" summary. `Succeeded`/`Failed` are self-evident; `Warned` ran but flagged
/// something non-blocking (e.g. doctor found issues); `Skipped` did not run (by flag or feed).
type Outcome =
    | Succeeded
    | Warned of note: string
    | Skipped of reason: string
    | Failed of note: string

type StepResult = { Title: string; Outcome: Outcome }

type Options =
    { Target: string
      Product: string
      /// The SDD scaffold provider selected by --template. Omitted selection remains rendering.
      Template: string
      Ref: string
      Upgrade: bool
      Governance: bool
      /// Skip the pre-scaffold `fsgg-sdd` self-update (step 2) and scaffold with the installed
      /// CLI. Default false = self-update to the newest coherent set first (ADR-0030); set true
      /// (via `--pinned`) for a reproducible, pinned scaffold, ideally with `--ref <tag>`.
      Pinned: bool
      /// The `fs-gg-ui` render profile (game/app/headless-scene/governed/sample-pack).
      /// None = pass no `--param profile`, deferring to the scaffold-provider default (game) —
      /// keeps the bare-CLI invocation byte-identical to before this flag existed.
      Profile: string option
      /// The npm package/version closure a fable-bindings provider materializes. Both are required
      /// for that provider and meaningless for every other provider.
      NpmPackage: string option
      NpmVersion: string option
      BindingTarget: string option
      /// Wire the workspace to a coordination board (default ON): vendor the coordination kit and
      /// write the `FSGG_COORD_*` env so `/pnext-item` and `/check-board` work out of the box.
      /// `--no-coordination` skips the whole step. (Opens ADR-0019's deferred product-mirror slice.)
      Coordinate: bool
      /// This workspace's own repo (`owner/repo`) — its identity on the board and the basis for its
      /// chore-lock ref. Not consumed as env (the engine resolves the repo from the git remote); it
      /// defaults the wizard's board-org prompt and drives the chore-lock next-step hint. `--repo`.
      WorkspaceRepo: string option
      /// The board this workspace coordinates against — `FSGG_COORD_OWNER` / `FSGG_COORD_PROJECT`.
      /// Default `FS-GG` / `Coordination` (the org board); `--board <owner>/<title>` overrides.
      BoardOwner: string
      BoardTitle: string
      /// Explicit public/private intent for a product Project. None preserves an existing board.
      PublicBoard: bool option
      /// Named team/user writer identities. Required whenever `--public-board` is requested.
      TrustedWriters: string list
      /// The per-repo chore-lock roster for a NON-FS-GG board (`FSGG_COORD_CHORE_LOCKS`). None for the
      /// FS-GG board, which uses the engine's embedded table. Set with `--chore-locks owner/repo#n,…`.
      ChoreLocks: string option }

/// Assemble the no-argument wizard's decisions after its meaningful prompts have completed.
/// Coordination and a current fresh scaffold are established defaults, so the wizard does not
/// manufacture confirmation answers for them: explicit `--no-coordination` / `--upgrade` remain
/// available only on the CLI path.
let assembleWizardOptions
    (target: string)
    (product: string)
    (template: string)
    (gitRef: string)
    (governance: bool)
    (pinned: bool)
    (profile: string option)
    (npmPackage: string option)
    (npmVersion: string option)
    (bindingTarget: string option)
    (workspaceRepo: string)
    (boardOwner: string)
    (boardTitle: string)
    (choreLocks: string option)
    : Options =
    { Target = target
      Product = product
      Template = template
      Ref = gitRef
      Upgrade = false
      Governance = governance
      Pinned = pinned
      Profile = profile
      NpmPackage = npmPackage
      NpmVersion = npmVersion
      BindingTarget = bindingTarget
      Coordinate = true
      WorkspaceRepo = Some workspaceRepo
      BoardOwner = boardOwner
      BoardTitle = boardTitle
      PublicBoard = None
      TrustedWriters = []
      ChoreLocks = choreLocks }

// ── Effects ──────────────────────────────────────────────────────────────────

/// One lock guards every write to AnsiConsole from the process-output pump threads, so
/// interleaved stdout/stderr lines never tear a markup write.
let private consoleGate = obj ()

let private dim (line: string) =
    lock consoleGate (fun () -> AnsiConsole.MarkupLine(sprintf "[grey37]  │ %s[/]" (Markup.Escape line)))

/// Run a child process, streaming its stdout+stderr (dimmed, indented) when `echo`, and
/// returning (exitCode, combinedOutput). `workingDir` sets the process CWD (used so
/// `dotnet new install` discovers a specific nuget.config — it has no --configfile). A
/// missing executable maps to exit 127 — the shell's "command not found", so callers keep
/// the script's exit-code contract.
let private runProcessIn (workingDir: string option) (echo: bool) (exe: string) (args: string list) : int * string =
    let psi = ProcessStartInfo(exe)
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    workingDir |> Option.iter (fun d -> psi.WorkingDirectory <- d)
    args |> List.iter psi.ArgumentList.Add
    use p = new Process()
    p.StartInfo <- psi
    let sb = System.Text.StringBuilder()
    let sink (data: string) =
        if not (isNull data) then
            lock consoleGate (fun () ->
                sb.AppendLine data |> ignore
                if echo then AnsiConsole.MarkupLine(sprintf "[grey37]  │ %s[/]" (Markup.Escape data)))
    p.OutputDataReceived.Add(fun e -> sink e.Data)
    p.ErrorDataReceived.Add(fun e -> sink e.Data)
    try
        p.Start() |> ignore
        p.BeginOutputReadLine()
        p.BeginErrorReadLine()
        p.WaitForExit()
        p.ExitCode, sb.ToString()
    with :? System.ComponentModel.Win32Exception ->
        127, sprintf "%s: command not found" exe

let private runProcess (echo: bool) (exe: string) (args: string list) : int * string =
    runProcessIn None echo exe args

/// The org GitHub Packages feed. It authenticates ALL reads, including PUBLIC packages
/// (FS-GG/FS.GG.Templates#82), so a bare `dotnet new install FS.GG.Templates` against a config
/// that carries this source fails with a 401 on every anonymous read.
let private orgFeed = "https://nuget.pkg.github.com/FS-GG/index.json"

/// nuget.org, which serves FS.GG.Templates anonymously (the `light` governance overlay only
/// needs whatever version is public here).
let private nugetOrg = "https://api.nuget.org/v3/index.json"

/// A feed read token from the environment, if any — a dedicated var first, then the ones CI
/// and the `gh` CLI already export. `read:packages` scope is enough (the package is public).
let private feedToken () =
    [ "FSGG_PACKAGES_TOKEN"; "GH_TOKEN"; "GITHUB_TOKEN" ]
    |> List.tryPick (fun name ->
        match Environment.GetEnvironmentVariable name with
        | null | "" -> None
        | v -> Some v)

/// Run `dotnet new install FS.GG.Templates` from a temp dir carrying `configXml` as its
/// nuget.config, then delete the temp dir. `dotnet new install` has no --configfile; it
/// discovers config from CWD upward, so the isolated dir gives us a config with a `<clear />`
/// that no ambient source can widen — a 401-on-read org feed in the caller's global config then
/// can't poison the restore (one source hard-failing fails the whole restore).
let private installFromTempConfig (configXml: string) : int * string =
    let dir = Path.Combine(Path.GetTempPath(), "new-sdd-workspace-" + Guid.NewGuid().ToString "N")
    Directory.CreateDirectory dir |> ignore
    try
        File.WriteAllText(Path.Combine(dir, "nuget.config"), configXml)
        runProcessIn (Some dir) false "dotnet" [ "new"; "install"; "FS.GG.Templates" ]
    finally
        try Directory.Delete(dir, true) with _ -> ()

/// An isolated nuget.config exposing only nuget.org (anonymous).
let private nugetOrgConfig () =
    String.concat "\n"
        [ "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
          "<configuration>"
          "  <packageSources>"
          "    <clear />"
          sprintf "    <add key=\"nuget.org\" value=\"%s\" />" nugetOrg
          "  </packageSources>"
          "</configuration>" ]

/// An isolated nuget.config exposing the credentialed org feed (may carry a newer build) with
/// nuget.org as a same-restore fallback.
let private orgFeedConfig (token: string) =
    String.concat "\n"
        [ "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
          "<configuration>"
          "  <packageSources>"
          "    <clear />"
          sprintf "    <add key=\"fs-gg-github\" value=\"%s\" />" orgFeed
          sprintf "    <add key=\"nuget.org\" value=\"%s\" />" nugetOrg
          "  </packageSources>"
          "  <packageSourceCredentials>"
          "    <fs-gg-github>"
          "      <add key=\"Username\" value=\"fs-gg\" />"
          sprintf "      <add key=\"ClearTextPassword\" value=\"%s\" />" (System.Security.SecurityElement.Escape token)
          "    </fs-gg-github>"
          "  </packageSourceCredentials>"
          "</configuration>" ]

/// Install the FS.GG.Templates template package (which carries the `fs-gg-governance` template).
/// FS.GG.Templates is published anonymously on nuget.org AND (possibly a newer build) on the
/// org feed, whose reads are all authenticated (FS-GG/FS.GG.Templates#82). Best-effort ladder:
/// with a token, try the org feed first (may carry a newer version); with no token — or if the
/// org-feed install fails — fall back to nuget.org anonymously. Only when BOTH paths fail is the
/// skip surfaced. Each install runs from an isolated temp config so the org feed's anonymous 401
/// can't poison the restore.
let private installGovernanceTemplate () : int * string =
    match feedToken () with
    | None -> installFromTempConfig (nugetOrgConfig ())
    | Some token ->
        let code, log = installFromTempConfig (orgFeedConfig token)
        if code = 0 then
            code, log
        else
            let code2, log2 = installFromTempConfig (nugetOrgConfig ())
            code2, log + "\n" + log2

/// Run `dotnet tool update --global FS.GG.SDD.Cli` from a temp dir carrying `configXml` as its
/// nuget.config (passed with --configfile), then delete the temp dir. The isolated config's
/// `<clear />` stops an ambient 401-on-read org feed in the caller's global config from poisoning
/// the restore (one source hard-failing fails the whole restore). Mirrors `installFromTempConfig`.
let private updateCliFromTempConfig (configXml: string) : int * string =
    let dir = Path.Combine(Path.GetTempPath(), "new-sdd-workspace-upd-" + Guid.NewGuid().ToString "N")
    Directory.CreateDirectory dir |> ignore
    try
        let cfg = Path.Combine(dir, "nuget.config")
        File.WriteAllText(cfg, configXml)
        runProcess true "dotnet" [ "tool"; "update"; "--global"; "FS.GG.SDD.Cli"; "--configfile"; cfg ]
    finally
        try Directory.Delete(dir, true) with _ -> ()

/// Self-update the `fsgg-sdd` global tool to the newest published build BEFORE scaffolding, so a
/// fresh workspace is produced by the current coherent set's tooling — the DEFAULT (ADR-0030, the
/// creation-time carve-out to ADR-0009: there is no existing consumer artifact to clobber, and
/// newest-by-default is the whole point of creating a workspace; `--pinned` + `--ref <tag>`
/// restores a reproducible pin). `FS.GG.SDD.Cli` is dual-published (ADR-0012): anonymously on
/// nuget.org AND — possibly a newer build — on the org GitHub Packages feed, whose reads are all
/// authenticated (FS.GG.Templates#82). Same best-effort ladder as the governance overlay: with a
/// token, try the org feed first (may carry a newer build) and fall back to nuget.org; with no
/// token, go straight to nuget.org anonymously. Non-fatal throughout — an offline or failed update
/// warns and scaffolding proceeds with the installed CLI.
let private selfUpdateCli () : Outcome =
    let code, _ =
        match feedToken () with
        | None -> updateCliFromTempConfig (nugetOrgConfig ())
        | Some token ->
            let code, log = updateCliFromTempConfig (orgFeedConfig token)
            if code = 0 then code, log
            else
                let code2, log2 = updateCliFromTempConfig (nugetOrgConfig ())
                code2, log + "\n" + log2
    if code = 0 then Succeeded
    else Warned(sprintf "update failed (exit %d) — scaffolding with the installed CLI" code)

/// `command -v <cmd>` — is the executable resolvable on PATH? (dotnet global tools install
/// to a directory that is itself on PATH, so this finds `fsgg-sdd`.)
let private onPath (cmd: string) =
    match Environment.GetEnvironmentVariable "PATH" with
    | null | "" -> false
    | path ->
        let exts =
            if RuntimeInformation.IsOSPlatform OSPlatform.Windows then [ ".exe"; ".cmd"; ".bat"; "" ]
            else [ "" ]
        path.Split(Path.PathSeparator)
        |> Array.exists (fun dir ->
            not (String.IsNullOrWhiteSpace dir)
            && exts |> List.exists (fun ext -> File.Exists(Path.Combine(dir, cmd + ext))))

/// Fetch the committed (already concretely pinned) rendering provider descriptor and write it
/// to <target>/.fsgg/providers.yml. Returns the pinned `source:` line for display, or an error
/// string (a non-2xx response mirrors curl's `-f` failure — fatal to the run).
let private fetchDescriptor (template: string) (gitRef: string) (dest: string) : Result<string option, string> =
    try
        let baseUrl =
            match Environment.GetEnvironmentVariable "FSGG_TEMPLATES_RAW_BASE" with
            | null | "" -> "https://raw.githubusercontent.com/FS-GG/FS.GG.Templates"
            | value -> value.TrimEnd('/')
        let url =
            sprintf "%s/%s/providers/%s.providers.yml" baseUrl gitRef template
        use client = new HttpClient()
        client.Timeout <- TimeSpan.FromSeconds 30.0
        use resp = client.GetAsync(url).GetAwaiter().GetResult()
        if not resp.IsSuccessStatusCode then
            Error(sprintf "HTTP %d fetching %s" (int resp.StatusCode) url)
        else
            let content = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath dest)) |> ignore
            File.WriteAllText(dest, content)
            let pinned =
                content.Replace("\r\n", "\n").Split('\n')
                |> Array.tryFind (fun l -> l.TrimStart().StartsWith "source:")
                |> Option.map (fun l -> l.Trim())
            Ok pinned
    with ex ->
        Error ex.Message

// ── Coordination wiring (the kit + board env into the workspace) ──────────────

/// Legacy fallback for refs predating the v2 directory manifest. Once all supported refs publish
/// `coordination-kit-skill-manifest.json`, this list can retire in the reader-removal phase.
let private legacyCoordinationSkills =
    [ "cross-repo-coordination"; "intra-repo-parallel-work"; "check-board"; "pnext-item" ]

/// `owner/title` → (owner, title). No `/` ⇒ that owner's default `Coordination` board.
let private parseBoard (value: string) : string * string =
    match value.IndexOf '/' with
    | -1 -> value, "Coordination"
    | i -> value.Substring(0, i), value.Substring(i + 1)

/// Parse the only repository identity accepted by the policy client. Keeping this typed at the
/// boundary prevents a shell fragment or a prose URL from reaching `gh` as an authority.
let private parseRepository (value: string) : Result<string * string, string> =
    match value.Split('/', StringSplitOptions.RemoveEmptyEntries) with
    | [| owner; name |] when not (String.IsNullOrWhiteSpace owner) && not (String.IsNullOrWhiteSpace name) ->
        Ok(owner, name)
    | _ -> Error "--repo must be an owner/repository identity"

/// A repository-policy result is deliberately separate from the orchestration outcome: a failed
/// read is a no-verdict, never evidence that issue intake is restricted.
type private RepositoryPolicyReceipt =
    | RepositorySecured of repository: string * prior: string * actor: string
    | RepositoryPending of repository: string * reason: string

type private ProjectAccessReceipt =
    | ProjectObserved of project: string * projectId: string * isPublic: bool
    | ProjectPending of project: string * reason: string

type private SecurityReport =
    { Repository: RepositoryPolicyReceipt option
      Project: ProjectAccessReceipt
      Outcome: Outcome }

let private graphql (query: string) (variables: (string * string) list) =
    let args =
        [ yield "api"; yield "graphql"; yield "-f"; yield "query=" + query
          for name, value in variables do
              yield "-F"
              yield sprintf "%s=%s" name value ]
    runProcess false "gh" args

/// Apply the repository's typed `IssueCreationPolicy` only after reading it, then re-read the
/// exact field. This is intentionally not a best-effort security claim: unavailable `gh`, a 404,
/// permission failure, mutation failure, or stale post-write read becomes `RepositoryPending`.
let private secureRepository (repository: string) : RepositoryPolicyReceipt =
    match parseRepository repository with
    | Error reason -> RepositoryPending(repository, reason)
    | Ok(owner, name) ->
        let read = "query($owner:String!,$name:String!){viewer{login} repository(owner:$owner,name:$name){id issueCreationPolicy}}"
        let code, output = graphql read [ "owner", owner; "name", name ]
        if code <> 0 then
            RepositoryPending(repository, "repository policy could not be read (missing repository or administration permission)")
        else
            try
                let root = JsonNode.Parse(output).AsObject()
                let repo = root.["data"].AsObject().["repository"].AsObject()
                let id = repo.["id"].GetValue<string>()
                let prior = repo.["issueCreationPolicy"].GetValue<string>()
                let actor = root.["data"].AsObject().["viewer"].AsObject().["login"].GetValue<string>()
                if prior = "COLLABORATORS_ONLY" then
                    RepositorySecured(repository, prior, actor)
                else
                    let mutation = "mutation($id:ID!){updateRepository(input:{repositoryId:$id,issueCreationPolicy:COLLABORATORS_ONLY}){repository{issueCreationPolicy}}}"
                    let changed, _ = graphql mutation [ "id", id ]
                    if changed <> 0 then
                        RepositoryPending(repository, "IssueCreationPolicy mutation failed")
                    else
                        let reread, verified = graphql read [ "owner", owner; "name", name ]
                        if reread <> 0 then
                            RepositoryPending(repository, "post-write repository policy read failed")
                        else
                            let finalPolicy = JsonNode.Parse(verified).AsObject().["data"].AsObject().["repository"].AsObject().["issueCreationPolicy"].GetValue<string>()
                            if finalPolicy = "COLLABORATORS_ONLY" then RepositorySecured(repository, prior, actor)
                            else RepositoryPending(repository, sprintf "post-write policy was %s, not COLLABORATORS_ONLY" finalPolicy)
            with _ ->
                RepositoryPending(repository, "repository policy response was not a typed GitHub result")

/// Read Project visibility through the supported ProjectV2 surface. The schema deliberately does
/// not expose the base/effective access permission; that absence is retained as a no-verdict by
/// the caller, rather than substituted with viewerCanUpdate or a browser scrape.
let private inspectProject (owner: string) (title: string) : ProjectAccessReceipt =
    let project = sprintf "%s/%s" owner title
    let query = "query($owner:String!){organization(login:$owner){projectsV2(first:100){nodes{id title public}}}}"
    let code, output = graphql query [ "owner", owner ]
    if code <> 0 then ProjectPending(project, "ProjectV2 visibility could not be read")
    else
        try
            let nodes = JsonNode.Parse(output).AsObject().["data"].AsObject().["organization"].AsObject().["projectsV2"].AsObject().["nodes"].AsArray()
            match nodes |> Seq.tryFind (fun n -> n.AsObject().["title"].GetValue<string>() = title) with
            | None -> ProjectPending(project, "configured Project does not exist yet")
            | Some node -> ProjectObserved(project, node.AsObject().["id"].GetValue<string>(), node.AsObject().["public"].GetValue<bool>())
        with _ -> ProjectPending(project, "ProjectV2 response was not a typed GitHub result")

/// Update only the visibility fact GitHub exposes and then re-read it. Access base permission and
/// collaborators are intentionally outside this success result: their absence from the read model
/// means a visibility green cannot certify Project write access.
let private applyProjectVisibility (owner: string) (title: string) (desired: bool option) : ProjectAccessReceipt =
    match inspectProject owner title, desired with
    | receipt, None -> receipt
    | ProjectPending _ as receipt, _ -> receipt
    | (ProjectObserved(_, _, current) as receipt), Some wanted when current = wanted -> receipt
    | ProjectObserved(project, id, _), Some wanted ->
        let mutation = "mutation($id:ID!,$public:Boolean!){updateProjectV2(input:{projectId:$id,public:$public}){projectV2{public}}}"
        let code, _ = graphql mutation [ "id", id; "public", if wanted then "true" else "false" ]
        if code <> 0 then ProjectPending(project, "Project visibility mutation failed")
        else
            match inspectProject owner title with
            | ProjectObserved(_, verifiedId, actual) when actual = wanted -> ProjectObserved(project, verifiedId, actual)
            | ProjectObserved(_, _, actual) -> ProjectPending(project, sprintf "post-write visibility was %b" actual)
            | ProjectPending(_, reason) -> ProjectPending(project, "post-write Project visibility read failed: " + reason)

/// GitHub's supported ProjectV2 collaborator mutation is used for the explicit writer allowlist.
/// Collaborator ids cannot be safely guessed from display names, so the typed API's own read is
/// the authority: when it cannot expose the collaborators we retain a pending human obligation.
let private applyProjectWriters (owner: string) (title: string) (desired: string list) : ProjectAccessReceipt =
    match applyProjectVisibility owner title None with
    | ProjectPending _ as receipt -> receipt
    | ProjectObserved(project, id, _) ->
        // ProjectV2 accepts actor ids, not names. Resolve explicit users and `team:<slug>`
        // identities through the live typed schema; no UI scrape or guessed id enters a mutation.
        let resolve (writer: string) =
            let isTeam = writer.StartsWith("team:", StringComparison.OrdinalIgnoreCase)
            let name = if isTeam then writer.Substring("team:".Length) else writer
            let query = if isTeam then "query($owner:String!){organization(login:$owner){teams(first:100){nodes{id slug}}}}" else "query($login:String!){user(login:$login){id}}"
            let variables = if isTeam then [ "owner", owner ] else [ "login", name ]
            let code, output = graphql query variables
            if code <> 0 then None
            else
                try
                    let data = JsonNode.Parse(output).AsObject().["data"].AsObject()
                    if isTeam then
                        data.["organization"].AsObject().["teams"].AsObject().["nodes"].AsArray()
                        |> Seq.tryFind (fun team -> team.AsObject().["slug"].GetValue<string>() = name)
                        |> Option.map (fun team -> "teamId:" + team.AsObject().["id"].GetValue<string>())
                    else Some("userId:" + data.["user"].AsObject().["id"].GetValue<string>())
                with _ -> None
        let collaborators =
            desired
            |> List.map (fun writer -> resolve writer |> Option.map (fun id ->
                if id.StartsWith "teamId:" then sprintf "{teamId:%s,role:WRITER}" (id.Substring "teamId:".Length)
                else sprintf "{userId:%s,role:WRITER}" (id.Substring "userId:".Length)))
        if collaborators |> List.exists Option.isNone then
            ProjectPending(project, "one or more trusted writers could not be resolved through the typed GitHub API")
        else
            let collaborators = collaborators |> List.choose (fun value -> value) |> String.concat ","
            // `gh api -F` serializes an array spelling as a string.  The collaborator objects are
            // built solely from typed GitHub node ids, so place that trusted, schema-shaped list in
            // the mutation document and keep the Project id as the normal GraphQL variable.
            let mutation = "mutation($id:ID!){updateProjectV2Collaborators(input:{projectId:$id,collaborators:[" + collaborators + "]}){collaborators{totalCount}}}"
            let code, _ = graphql mutation [ "id", id ]
            if code <> 0 then ProjectPending(project, "Project writer allowlist mutation failed")
            else
                match inspectProject owner title with
                | ProjectObserved(_, verifiedId, verifiedPublic) ->
                    ProjectObserved(project, verifiedId, verifiedPublic)
                | ProjectPending(_, reason) -> ProjectPending(project, "post-write Project access read failed: " + reason)

/// Secure repository issue intake where the resource exists. Project access remains an explicit
/// no-verdict until its typed access surface can be both read and re-read; that boundary is surfaced
/// in the run summary instead of inferring safety from `viewerCanUpdate` or scraping GitHub's UI.
let private workspaceSecurity (opts: Options) : SecurityReport =
    let repository = opts.WorkspaceRepo |> Option.map secureRepository
    let project =
        match applyProjectVisibility opts.BoardOwner opts.BoardTitle opts.PublicBoard with
        | ProjectObserved _ as visible when List.isEmpty opts.TrustedWriters -> visible
        | ProjectObserved(project, _, _) -> applyProjectWriters opts.BoardOwner opts.BoardTitle opts.TrustedWriters
        | ProjectPending _ as pending -> pending
    let repositoryMessage =
        match repository with
        | None -> "repository security pending — pass --repo owner/repository after the repository exists"
        | Some(RepositorySecured(repo, prior, actor)) -> sprintf "repository %s issue policy verified as COLLABORATORS_ONLY (prior %s; actor %s)" repo prior actor
        | Some(RepositoryPending(repo, reason)) -> sprintf "repository security pending for %s — %s; re-run new-sdd-workspace secure after creation/permission is available" repo reason
    let projectMessage =
        match project with
        | ProjectObserved(project, _, true) -> sprintf "Project %s is public-readable; base Read and explicit writer allowlist require the recorded human verification" project
        | ProjectObserved(project, _, false) -> sprintf "Project %s is private; base access and explicit writer allowlist require the recorded human verification" project
        | ProjectPending(project, reason) -> sprintf "Project access pending for %s — %s" project reason
    { Repository = repository; Project = project; Outcome = Warned(repositoryMessage + "; " + projectMessage) }

/// Persist the security facts that could not be established. This lives beside the scaffold's
/// provenance rather than in console prose so a later operator has one exact recovery target.
/// The Project obligation is retained even after repository policy succeeds: GitHub's ordinary
/// ProjectV2 read does not expose an effective/base access verdict, so visibility or
/// `viewerCanUpdate` cannot safely clear it.
let private recordSecurityObligations (opts: Options) (report: SecurityReport) =
    let dest = Path.Combine(opts.Target, ".fsgg", "scaffold-provenance.json")
    try
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath dest)) |> ignore
        let root =
            if File.Exists dest then
                match JsonNode.Parse(File.ReadAllText dest) with
                | :? JsonObject as o -> o
                | _ -> JsonObject()
            else JsonObject()
        let obligations = JsonArray()
        let repo = JsonObject()
        repo.["kind"] <- JsonValue.Create "repository-issue-policy"
        repo.["target"] <- JsonValue.Create(opts.WorkspaceRepo |> Option.defaultValue "repository-not-yet-created")
        repo.["targetPolicy"] <- JsonValue.Create "COLLABORATORS_ONLY"
        repo.["resume"] <- JsonValue.Create(sprintf "new-sdd-workspace secure --repo %s" (opts.WorkspaceRepo |> Option.defaultValue "owner/repository"))
        match report.Repository with
        | Some(RepositorySecured(repository, prior, actor)) ->
            let receipt = JsonObject()
            receipt.["kind"] <- JsonValue.Create "repository-issue-policy"
            receipt.["repository"] <- JsonValue.Create repository
            receipt.["priorPolicy"] <- JsonValue.Create prior
            receipt.["finalPolicy"] <- JsonValue.Create "COLLABORATORS_ONLY"
            receipt.["actor"] <- JsonValue.Create actor
            receipt.["source"] <- JsonValue.Create "GitHub GraphQL repository.issueCreationPolicy re-read"
            root.["verifiedSecurityReceipts"] <- JsonArray(receipt)
        | _ ->
            repo.["state"] <- JsonValue.Create "pending"
            obligations.Add repo
        let project = JsonObject()
        project.["kind"] <- JsonValue.Create "project-access"
        project.["target"] <- JsonValue.Create(sprintf "%s/%s" opts.BoardOwner opts.BoardTitle)
        project.["requestedVisibility"] <- JsonValue.Create(match opts.PublicBoard with Some true -> "public" | Some false -> "private" | None -> "preserve")
        project.["expectedBasePermission"] <- JsonValue.Create "READ"
        let writers = JsonArray()
        opts.TrustedWriters |> List.iter (fun writer -> writers.Add(JsonValue.Create writer))
        project.["trustedWriters"] <- writers
        project.["humanVerification"] <- JsonValue.Create "Project → Settings → Manage access; verify base permission Read and the explicit trusted writer allowlist. Re-run new-sdd-workspace secure <workspace> --project owner/title --public-board|--private-board --trusted-writers ids after verification."
        project.["state"] <- JsonValue.Create "pending-human-verification"
        obligations.Add project
        root.["securityObligations"] <- obligations
        File.WriteAllText(dest, root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))
    with ex ->
        raise (InvalidOperationException("security provenance persistence failed; no verified or pending security state was recorded", ex))

/// Fetch one text file from a raw URL. `Error` on any non-2xx or transport failure — the coordination
/// step is best-effort, so the caller downgrades a miss to a warning rather than failing the scaffold.
let private fetchText (url: string) : Result<string, string> =
    try
        use client = new HttpClient()
        client.Timeout <- TimeSpan.FromSeconds 30.0
        use resp = client.GetAsync(url).GetAwaiter().GetResult()
        if resp.IsSuccessStatusCode then
            Ok(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult())
        else
            Error(sprintf "HTTP %d fetching %s" (int resp.StatusCode) url)
    with ex ->
        Error ex.Message

let private writeUnder (target: string) (relPath: string) (content: string) =
    let dest = Path.Combine(target, relPath)
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath dest)) |> ignore
    File.WriteAllText(dest, content)

/// Add the owner/group/other execute bits to a file (the `fsgg-coord` shim must be runnable).
/// Best-effort at the call site — a filesystem that rejects the mode (or Windows) leaves the file
/// non-executable rather than failing the whole step, exactly as the inline version this replaces did.
let private setExecutable (dest: string) =
    File.SetUnixFileMode(
        dest,
        File.GetUnixFileMode dest
        ||| UnixFileMode.UserExecute
        ||| UnixFileMode.GroupExecute
        ||| UnixFileMode.OtherExecute
    )

let private setExecutableState (dest: string) (executable: bool) =
    if not (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) then
        let current = File.GetUnixFileMode dest
        let executeBits =
            UnixFileMode.UserExecute ||| UnixFileMode.GroupExecute ||| UnixFileMode.OtherExecute
        File.SetUnixFileMode(dest, (if executable then current ||| executeBits else current &&& ~~~executeBits))

let private isExecutable (dest: string) =
    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then false
    else
        let executeBits =
            UnixFileMode.UserExecute ||| UnixFileMode.GroupExecute ||| UnixFileMode.OtherExecute
        (File.GetUnixFileMode(dest) &&& executeBits) <> enum<UnixFileMode> 0

/// Fetch the generated closed-set directory index. Old refs have no index, so they retain the
/// SKILL.md-only reader during publish-before-flip.
let private coordinationSkillFiles (raw: string -> string) =
    match fetchText (raw "registry/coordination-kit-skill-manifest.json") with
    | Error _ ->
        false, (legacyCoordinationSkills |> List.map (fun id -> id, "SKILL.md", false))
    | Ok json ->
        true,
        (JsonNode.Parse(json).AsObject().["skills"].AsArray()
         |> Seq.collect (fun skill ->
             let row = skill.AsObject()
             let id = row.["id"].GetValue<string>()
             row.["files"].AsArray()
             |> Seq.map (fun file ->
                 let f = file.AsObject()
                 id, f.["path"].GetValue<string>(), f.["executable"].GetValue<bool>()))
         |> List.ofSeq)

/// Merge the `fs.gg.coord.cli` tool into the workspace's `.config/dotnet-tools.json`, preserving any
/// tools already there (the SDD scaffold may have written one). `manifestJson` is the fetched
/// `dist/dotnet/.config/dotnet-tools.json` — we lift only the coord entry out of it.
let private mergeToolManifest (target: string) (manifestJson: string) =
    let dest = Path.Combine(target, ".config", "dotnet-tools.json")
    let coord = JsonNode.Parse(manifestJson).AsObject().["tools"].AsObject().["fs.gg.coord.cli"]
    let root =
        if File.Exists dest then
            JsonNode.Parse(File.ReadAllText dest).AsObject()
        else
            let o = JsonObject()
            o.["version"] <- JsonValue.Create 1
            o.["isRoot"] <- JsonValue.Create true
            o.["tools"] <- JsonObject()
            o
    let tools =
        match root.["tools"] with
        | :? JsonObject as t -> t
        | _ ->
            let t = JsonObject()
            root.["tools"] <- t
            t
    tools.["fs.gg.coord.cli"] <- coord.DeepClone()
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath dest)) |> ignore
    File.WriteAllText(dest, root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))

/// Write/MERGE the coordination env into the workspace's `.claude/settings.json` — where Claude Code
/// (and thus a skill-run `fsgg-coord`) reads env. Merge, never clobber: the SDD scaffold may already
/// have written a settings.json (hooks, etc.), so we touch only the `env` keys we own.
let private writeCoordinationEnv (target: string) (owner: string) (title: string) (choreLocks: string option) =
    let dest = Path.Combine(target, ".claude", "settings.json")
    let root =
        if File.Exists dest then
            match JsonNode.Parse(File.ReadAllText dest) with
            | :? JsonObject as o -> o
            | _ -> JsonObject()
        else
            JsonObject()
    let env =
        match root.["env"] with
        | :? JsonObject as e -> e
        | _ ->
            let e = JsonObject()
            root.["env"] <- e
            e
    env.["FSGG_COORD_OWNER"] <- JsonValue.Create owner
    env.["FSGG_COORD_PROJECT"] <- JsonValue.Create title
    match choreLocks with
    | Some cl -> env.["FSGG_COORD_CHORE_LOCKS"] <- JsonValue.Create cl
    | None -> ()
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath dest)) |> ignore
    File.WriteAllText(dest, root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))

/// Vendor the coordination kit from FS-GG/.github@<ref> into the workspace and write the board env.
/// Best-effort (mirrors governance): a file that 404s becomes a Warning — the env still lands, and the
/// rest is fetchable by hand — never a fatal that would strand a good scaffold.
let private wireCoordination (kitRef: string) (opts: Options) : Outcome =
    let raw p = sprintf "https://raw.githubusercontent.com/FS-GG/.github/%s/%s" kitRef p
    let problems = ResizeArray<string>()
    let _, skillFiles = coordinationSkillFiles raw
    // 1 · complete skill directories → every agent-skill root, byte- and mode-identical.
    //     union into .claude/.agents (ADR-0065 as amended by ADR-0067 §5), so the coordination kit
    //     joins both — a Codex-driven agent in the workspace sees the same skills a Claude one does,
    //     because `.agents/skills` is Codex's OWN native root and needs no pointing.
    for s, rel, executable in skillFiles do
        match fetchText (raw (sprintf ".claude/skills/%s/%s" s rel)) with
        | Ok content ->
            for root in [ ".claude/skills"; ".agents/skills" ] do
                let destRel = sprintf "%s/%s/%s" root s rel
                writeUnder opts.Target destRel content
                try setExecutableState (Path.Combine(opts.Target, destRel)) executable with _ -> ()
        | Error e -> problems.Add e
    // 2 · the fsgg-coord shim (executable)
    (match fetchText (raw "scripts/fsgg-coord") with
     | Ok content ->
         writeUnder opts.Target "scripts/fsgg-coord" content
         try setExecutable (Path.Combine(opts.Target, "scripts", "fsgg-coord"))
         with _ -> ()
     | Error e -> problems.Add e)
    // 3 · the engine tool manifest (merge the coord tool)
    (match fetchText (raw "dist/dotnet/.config/dotnet-tools.json") with
     | Ok content ->
         try mergeToolManifest opts.Target content
         with ex -> problems.Add ex.Message
     | Error e -> problems.Add e)
    // 4 · the board env (no network — always writable)
    (try writeCoordinationEnv opts.Target opts.BoardOwner opts.BoardTitle opts.ChoreLocks
     with ex -> problems.Add ex.Message)
    if problems.Count = 0 then
        Succeeded
    else
        Warned(
            sprintf
                "%d kit file(s) not vendored (env written) — add later from FS-GG/.github: %s"
                problems.Count
                (problems.[problems.Count - 1])
        )

// ── Coordination RETROFIT (the inverse of #1142's scaffold-time wiring) ───────
//
// `wireCoordination` above runs AT SCAFFOLD TIME, on a directory the scaffold just created, and
// writes unconditionally. `retrofit` runs LATER, INSIDE an already-scaffolded workspace that was
// created `--no-coordination` (or before #1142 landed), and must be IDEMPOTENT: it materializes only
// what is missing or has drifted, leaves a coherent kit untouched, and records the event in
// `scaffold-provenance.json`. It is the precondition `work-board` (ADR-0064) documents but cannot
// itself satisfy. Same kit, same board env, same HTTP fetch (no checkout) as the scaffold path.

/// The retrofit invocation surface. A subset of `Options` — only the coordination inputs are
/// meaningful here; the SDD/render/governance steps do not re-run on an existing workspace.
type RetrofitOptions =
    { Target: string
      /// The FS-GG/.github ref to vendor the kit from (default `main`). Mirrors `--ref` on scaffold.
      Ref: string
      WorkspaceRepo: string option
      BoardOwner: string
      BoardTitle: string
      ChoreLocks: string option }

/// One file's reconciliation outcome. `Wrote(rel, wasMissing)` distinguishes a fresh materialization
/// (`wasMissing = true`) from a drift repair (`false`); `Kept` means present-and-identical (no write);
/// `Errored` carries a fetch/write failure. This three-way split is what makes the retrofit idempotent
/// and lets the provenance record name exactly what changed.
type private DriftAction =
    | Wrote of rel: string * wasMissing: bool
    | Kept of rel: string
    | Errored of detail: string

/// Write `desired` to `<target>/<rel>` ONLY if the on-disk bytes differ (or the file is absent),
/// returning which of those three happened. The heart of the idempotent retrofit: an unchanged file
/// is never rewritten, so re-running the retrofit on a coherent workspace is a pure no-op.
let private reconcileFile (target: string) (rel: string) (desired: string) (makeExec: bool) : DriftAction =
    let dest = Path.Combine(target, rel)
    let existing = if File.Exists dest then Some(File.ReadAllText dest) else None
    let modeMatches =
        not (File.Exists dest)
        || RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        || isExecutable dest = makeExec
    match existing with
    | Some cur when cur = desired && modeMatches -> Kept rel
    | _ ->
        try
            writeUnder target rel desired
            try setExecutableState dest makeExec with _ -> ()
            Wrote(rel, existing.IsNone)
        with ex ->
            Errored ex.Message

/// Reconcile the `fs.gg.coord.cli` entry in `.config/dotnet-tools.json`. Drift is detected on the coord
/// tool ONLY (the workspace's own tools are none of the retrofit's business) — if the manifest already
/// carries an identical coord entry it is `Kept`, otherwise `mergeToolManifest` folds it in, preserving
/// every other tool. `wasMissing` is true when no coord entry was there before.
let private reconcileToolManifest (target: string) (manifestJson: string) : DriftAction =
    let rel = ".config/dotnet-tools.json"
    let dest = Path.Combine(target, ".config", "dotnet-tools.json")
    try
        let desiredCoord =
            JsonNode.Parse(manifestJson).AsObject().["tools"].AsObject().["fs.gg.coord.cli"]
        let currentCoord =
            if File.Exists dest then
                try
                    match JsonNode.Parse(File.ReadAllText dest) with
                    | :? JsonObject as o ->
                        match o.["tools"] with
                        | :? JsonObject as t ->
                            match t.["fs.gg.coord.cli"] with
                            | null -> None
                            | v -> Some v
                        | _ -> None
                    | _ -> None
                with _ ->
                    None
            else
                None
        let identical =
            match currentCoord with
            | Some c -> c.ToJsonString() = desiredCoord.ToJsonString()
            | None -> false
        if identical then
            Kept rel
        else
            mergeToolManifest target manifestJson
            Wrote(rel, currentCoord.IsNone)
    with ex ->
        Errored ex.Message

/// The current value of one `env` key in `.claude/settings.json`, or None if the file/key is absent.
let private currentEnvValue (target: string) (name: string) : string option =
    let dest = Path.Combine(target, ".claude", "settings.json")
    if not (File.Exists dest) then
        None
    else
        try
            match JsonNode.Parse(File.ReadAllText dest) with
            | :? JsonObject as o ->
                match o.["env"] with
                | :? JsonObject as e ->
                    match e.[name] with
                    | null -> None
                    | v -> Some(v.GetValue<string>())
                | _ -> None
            | _ -> None
        with _ ->
            None

/// Reconcile the board env. If `.claude/settings.json` already carries the exact
/// `FSGG_COORD_OWNER`/`FSGG_COORD_PROJECT` (+ `FSGG_COORD_CHORE_LOCKS` when one was asked for) it is
/// `Kept`; otherwise `writeCoordinationEnv` merges the keys (never clobbering the rest of settings.json)
/// and it is `Wrote`. `wasMissing` is true when no `FSGG_COORD_OWNER` was present before.
let private reconcileEnv (target: string) (owner: string) (title: string) (choreLocks: string option) : DriftAction =
    let rel = ".claude/settings.json (env)"
    let curOwner = currentEnvValue target "FSGG_COORD_OWNER"
    let choreMatches =
        match choreLocks with
        | None -> true // not asked for ⇒ nothing to reconcile on this key
        | Some cl -> currentEnvValue target "FSGG_COORD_CHORE_LOCKS" = Some cl
    let identical =
        curOwner = Some owner
        && currentEnvValue target "FSGG_COORD_PROJECT" = Some title
        && choreMatches
    if identical then
        Kept rel
    else
        try
            writeCoordinationEnv target owner title choreLocks
            Wrote(rel, curOwner.IsNone)
        with ex ->
            Errored ex.Message

/// What the retrofit did, aggregated across every kit file + the env.
type private RetrofitReport =
    { Wrote: (string * bool) list // (rel, wasMissing) — wasMissing=true ⇒ fresh; false ⇒ drift repair
      Kept: string list
      Problems: string list }

/// Idempotently materialize the coordination kit + board env into an already-scaffolded workspace,
/// touching ONLY what is missing or drifted. Same fetch surface as `wireCoordination` (the four skills
/// into all three agent-skill roots, the `fsgg-coord` shim, the `fs.gg.coord.cli` tool manifest, the
/// board env) — but every write goes through a reconcile so a coherent kit is left byte-for-byte intact.
let private retrofitCoordination (opts: RetrofitOptions) : RetrofitReport =
    let raw p = sprintf "https://raw.githubusercontent.com/FS-GG/.github/%s/%s" opts.Ref p
    let wrote = ResizeArray<string * bool>()
    let kept = ResizeArray<string>()
    let problems = ResizeArray<string>()
    let hasDirectoryManifest, skillFiles = coordinationSkillFiles raw
    let record =
        function
        | Wrote(rel, m) -> wrote.Add(rel, m)
        | Kept rel -> kept.Add rel
        | Errored d -> problems.Add d
    // 1 · complete skill directories → every agent-skill root (reconciled per file and mode).
    for s, rel, executable in skillFiles do
        match fetchText (raw (sprintf ".claude/skills/%s/%s" s rel)) with
        | Ok content ->
            for root in [ ".claude/skills"; ".agents/skills" ] do
                record (reconcileFile opts.Target (sprintf "%s/%s/%s" root s rel) content executable)
        | Error e -> problems.Add e
    if hasDirectoryManifest then
        let expected = skillFiles |> Seq.map (fun (id, rel, _) -> id, rel) |> Set.ofSeq
        for root in [ ".claude/skills"; ".agents/skills" ] do
            for id in skillFiles |> Seq.map (fun (id, _, _) -> id) |> Seq.distinct do
                let dir = Path.Combine(opts.Target, root, id)
                if Directory.Exists dir then
                    for file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories) do
                        let rel = Path.GetRelativePath(dir, file).Replace(Path.DirectorySeparatorChar, '/')
                        if not (Set.contains (id, rel) expected) then
                            try
                                File.Delete file
                                wrote.Add(sprintf "%s/%s/%s" root id rel, false)
                            with ex -> problems.Add ex.Message
    // 2 · the fsgg-coord shim (executable)
    (match fetchText (raw "scripts/fsgg-coord") with
     | Ok content -> record (reconcileFile opts.Target "scripts/fsgg-coord" content true)
     | Error e -> problems.Add e)
    // 3 · the engine tool manifest (merge the coord tool)
    (match fetchText (raw "dist/dotnet/.config/dotnet-tools.json") with
     | Ok content -> record (reconcileToolManifest opts.Target content)
     | Error e -> problems.Add e)
    // 4 · the board env (no network — always reconcilable)
    record (reconcileEnv opts.Target opts.BoardOwner opts.BoardTitle opts.ChoreLocks)
    { Wrote = List.ofSeq wrote
      Kept = List.ofSeq kept
      Problems = List.ofSeq problems }

/// Append a `coordination` entry to the `retrofits` array in `.fsgg/scaffold-provenance.json`, naming
/// what was freshly materialized vs re-emitted as drift. Additive and read-safe: a `retrofits` key is
/// unknown to SDD's provenance schema, and System.Text.Json ignores unknown members on read, so a
/// `doctor`/`verify` parse is unaffected. Merges into an existing document (never rewriting SDD's own
/// keys); if none exists (a workspace with no provenance at all) it writes a minimal one carrying only
/// the retrofit log.
let private recordRetrofit (target: string) (opts: RetrofitOptions) (materialized: string list) (drift: string list) =
    let dest = Path.Combine(target, ".fsgg", "scaffold-provenance.json")
    let root =
        if File.Exists dest then
            match JsonNode.Parse(File.ReadAllText dest) with
            | :? JsonObject as o -> o
            | _ -> JsonObject()
        else
            JsonObject()
    let retrofits =
        match root.["retrofits"] with
        | :? JsonArray as a -> a
        | _ ->
            let a = JsonArray()
            root.["retrofits"] <- a
            a
    let entry = JsonObject()
    entry.["kind"] <- JsonValue.Create "coordination"
    entry.["tool"] <- JsonValue.Create "new-sdd-workspace"
    entry.["at"] <- JsonValue.Create(DateTime.UtcNow.ToString "o")
    entry.["ref"] <- JsonValue.Create opts.Ref
    entry.["board"] <- JsonValue.Create(sprintf "%s/%s" opts.BoardOwner opts.BoardTitle)
    opts.ChoreLocks |> Option.iter (fun cl -> entry.["choreLocks"] <- JsonValue.Create cl)
    let arrOf (xs: string list) =
        let a = JsonArray()
        for x in xs do
            a.Add(JsonValue.Create x)
        a
    entry.["materialized"] <- arrOf materialized
    entry.["reMaterializedDrift"] <- arrOf drift
    retrofits.Add entry
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath dest)) |> ignore
    File.WriteAllText(dest, root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))

// ── Rendering (presentation edge) ────────────────────────────────────────────

let private step (n: int) (title: string) =
    AnsiConsole.WriteLine()
    AnsiConsole.Write((Rule(sprintf "[bold]%d[/] · [bold]%s[/]" n (Markup.Escape title))).LeftJustified())

/// Map an outcome to (result-cell markup, glyph line prefix) for the summary + inline lines.
let private outcomeCell =
    function
    | Succeeded -> "[green]✓ worked[/]", ""
    | Warned note -> "[yellow]⚠ warning[/]", note
    | Skipped reason -> "[grey]⊘ skipped[/]", reason
    | Failed note -> "[red]✗ failed[/]", note

let private header (opts: Options) =
    let grid = Grid()
    grid.AddColumn() |> ignore
    grid.AddColumn() |> ignore
    grid.AddRow("[grey]product[/]", sprintf "[bold]%s[/]" (Markup.Escape opts.Product)) |> ignore
    grid.AddRow("[grey]target[/]", Markup.Escape opts.Target) |> ignore
    grid.AddRow("[grey]template[/]", Markup.Escape opts.Template) |> ignore
    match opts.Profile with
    | Some profile -> grid.AddRow("[grey]profile[/]", Markup.Escape profile) |> ignore
    | None -> ()
    match opts.NpmPackage, opts.NpmVersion with
    | Some packageName, Some version -> grid.AddRow("[grey]npm[/]", Markup.Escape(sprintf "%s@%s" packageName version)) |> ignore
    | _ -> ()
    match opts.BindingTarget with
    | Some bindingTarget -> grid.AddRow("[grey]binding target[/]", Markup.Escape bindingTarget) |> ignore
    | None -> ()
    grid.AddRow("[grey]descriptor ref[/]", Markup.Escape opts.Ref) |> ignore
    grid.AddRow(
        "[grey]currency[/]",
        (if opts.Pinned then "[dim]pinned — installed CLI (--pinned)[/]" else "update fsgg-sdd before scaffold")
    )
    |> ignore
    grid.AddRow(
        "[grey]governance[/]",
        (if opts.Governance then "light / non-blocking" else "[dim]disabled (--no-governance)[/]")
    )
    |> ignore
    grid.AddRow(
        "[grey]coordination[/]",
        (if opts.Coordinate then
             sprintf "board [aqua]%s/%s[/]" (Markup.Escape opts.BoardOwner) (Markup.Escape opts.BoardTitle)
         else
             "[dim]disabled (--no-coordination)[/]")
    )
    |> ignore
    if opts.Upgrade then
        grid.AddRow("[grey]upgrade[/]", "reconcile if behind") |> ignore
    let panel = Panel(grid)
    panel.Header <- PanelHeader "[bold]new-sdd-workspace[/]"
    panel.Border <- BoxBorder.Rounded
    panel.Padding <- Padding(1, 0, 1, 0)
    AnsiConsole.Write panel

let private summary (results: StepResult seq) (opts: Options) (fatal: bool) =
    AnsiConsole.WriteLine()
    let table = Table()
    table.Border <- TableBorder.Rounded
    table.AddColumn "[bold]Step[/]" |> ignore
    table.AddColumn "[bold]Result[/]" |> ignore
    table.AddColumn "[bold]Detail[/]" |> ignore
    for r in results do
        let result, detail = outcomeCell r.Outcome
        table.AddRow(Markup.Escape r.Title, result, Markup.Escape detail) |> ignore
    let panel = Panel(table)
    panel.Header <-
        PanelHeader(if fatal then "[red]scaffold summary — incomplete[/]" else "[green]scaffold summary[/]")
    panel.Border <- BoxBorder.Rounded
    AnsiConsole.Write panel
    if not fatal then
        AnsiConsole.WriteLine()
        AnsiConsole.MarkupLine(sprintf "[bold]Done:[/] workspace in [green]%s[/]" (Markup.Escape opts.Target))
        AnsiConsole.MarkupLine(
            sprintf
                "[bold]Next:[/] cd %s && dotnet build && dotnet run   [grey]# then: fsgg-sdd charter[/]"
                (Markup.Escape opts.Target)
        )
        // The chore queue needs a per-repo lock issue, and this workspace's repo does not exist on GitHub
        // yet — so the one thing the scaffolder cannot do for a NON-FS-GG board is name it in the summary.
        if opts.Coordinate && opts.BoardOwner.ToLowerInvariant() <> "fs-gg" then
            let repo = opts.WorkspaceRepo |> Option.defaultValue (sprintf "%s/%s" opts.BoardOwner opts.Product)
            AnsiConsole.MarkupLine(
                sprintf
                    "[bold]Coord:[/] for [aqua]offer[/]/chores, create a closed [grey]`[[chore-lock]]`[/] issue in [green]%s[/] and add [grey]%s#<n>[/] to [grey]FSGG_COORD_CHORE_LOCKS[/] (.claude/settings.json)"
                    (Markup.Escape repo)
                    (Markup.Escape repo)
            )

/// The `fs-gg-ui` render profiles, in menu order — id + one-line gloss. `game` is the
/// scaffold-provider default (a minimal Pong-style starter); the rest are the sibling lanes.
let private profiles =
    [ "game", "minimal Pong-style starter (default)"
      "app", "controls-showcase MVU/Elmish app"
      "headless-scene", "scene render, no interactive shell"
      "governed", "scene/app pre-wired for the governance gates"
      "sample-pack", "a pack of sample scenes" ]

let private templates =
    [ "rendering", "FS.GG.Rendering application (supports --profile)"
      "console", "minimal F# executable"
      "web", "ASP.NET Core + TypeScript/Vite workspace"
      "fable-game", "Fable/Elmish game workspace"
      "fable-bindings", "Fable interop library (requires an exact npm package/version and target)" ]

let private supportsProfile template = template = "rendering"
let private requiresNpmClosure template = template = "fable-bindings"

/// Profiles that vendor the deterministic simulation core (`fs-gg-game-core`) —
/// `materializes-when: profile in [game, sample-pack]` in the render skill-manifest.
let private hasGameCore (profile: string) =
    profile = "game" || profile = "sample-pack"

/// Profiles that ship the standalone FS.GG.Audio component — its own repo/release axis, wired in
/// as the first consumer edge of the `fs-gg-audio` package contract (ADR-0024). Same
/// `materializes-when: profile in [game, sample-pack]` as the audio skill in the render manifest.
let private hasAudio (profile: string) =
    profile = "game" || profile = "sample-pack"

let private usage () =
    AnsiConsole.MarkupLine
        "[bold]new-sdd-workspace[/] — scaffold an FS.GG workspace (SDD + Rendering + optional Governance)"
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine "[bold]Usage[/]"
    AnsiConsole.MarkupLine "  new-sdd-workspace [grey]<target-dir> <product-name>[/] [[options]]"
    AnsiConsole.MarkupLine "  new-sdd-workspace [aqua]retrofit[/] [grey]<target-dir>[/] [[--board owner/title]] [[--repo owner/repo]] [[--chore-locks refs]] [[--ref git-ref]]"
    AnsiConsole.MarkupLine "  new-sdd-workspace [aqua]secure[/] [grey][[workspace]] --repo owner/repository | --project owner/title (--public-board|--private-board) --trusted-writers ids[/]"
    AnsiConsole.MarkupLine "  [dim](from a checkout: dotnet run --project scripts/NewSddWorkspace -- <target-dir> <product-name>)[/]"
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine "[bold]Subcommands[/]"
    AnsiConsole.MarkupLine "  [aqua]retrofit[/] <target-dir>   idempotently wire coordination ONTO an existing workspace (the"
    AnsiConsole.MarkupLine "                        inverse of the scaffold-time step): vendor the kit + write the board"
    AnsiConsole.MarkupLine "                        env, re-emit only what drifted, and record it in scaffold-provenance.json"
    AnsiConsole.MarkupLine "  [aqua]secure[/] --repo <owner/repo>  apply and verify collaborator-only repository issue intake"
    AnsiConsole.MarkupLine "  [aqua]secure[/] <workspace> --project <owner/title> (--public-board|--private-board) --trusted-writers <ids>"
    AnsiConsole.MarkupLine "                        apply/re-read observable Project visibility and writers; retain base-Read human verification"
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine "[bold]Options[/]"
    AnsiConsole.MarkupLine "  [green]--template[/] <name>  provider/template (default: rendering; omitted for compatibility)"
    AnsiConsole.MarkupLine(sprintf "                    [dim]%s[/]" (String.Join(", ", templates |> List.map fst)))
    AnsiConsole.MarkupLine "  [green]--profile[/] <name>   rendering-only profile (default: game = provider default)"
    AnsiConsole.MarkupLine(sprintf "                    [dim]%s[/]" (String.Join(", ", profiles |> List.map fst)))
    AnsiConsole.MarkupLine "  [green]--npm-package[/] <name>  fable-bindings package name (requires --npm-version)"
    AnsiConsole.MarkupLine "  [green]--npm-version[/] <exact> fable-bindings exact package version (requires --npm-package)"
    AnsiConsole.MarkupLine "  [green]--binding-target[/] <target> fable-bindings runtime target: browser, node, or universal"
    AnsiConsole.MarkupLine "  [green]--ref[/] <git-ref>    FS.GG.Templates ref for the descriptor (default: main = newest)"
    AnsiConsole.MarkupLine "  [green]--board[/] <owner/title>  coordination board to wire the workspace to (default: FS-GG/Coordination)"
    AnsiConsole.MarkupLine "  [green]--repo[/] <owner/repo>    this workspace's own repo (its board identity + chore-lock basis)"
    AnsiConsole.MarkupLine "  [green]--public-board[/]        request a public-readable product Project (requires --trusted-writers)"
    AnsiConsole.MarkupLine "  [green]--private-board[/]       request a private product Project"
    AnsiConsole.MarkupLine "  [green]--trusted-writers[/] <ids> comma-separated explicit Project team/user writer allowlist"
    AnsiConsole.MarkupLine "  [green]--chore-locks[/] <refs>   FSGG_COORD_CHORE_LOCKS for a non-FS-GG board (owner/repo#n,… — comma-separated)"
    AnsiConsole.MarkupLine "  [green]--no-coordination[/]  skip wiring the workspace to a coordination board (no kit, no env)"
    AnsiConsole.MarkupLine "  [green]--pinned[/]           skip the pre-scaffold fsgg-sdd self-update (scaffold with the installed CLI)"
    AnsiConsole.MarkupLine "                    [dim]default: self-update to the newest coherent set first; pair --pinned with --ref <tag> for a reproducible scaffold[/]"
    AnsiConsole.MarkupLine "  [green]--upgrade[/]          also run `fsgg-sdd upgrade` after scaffolding (reconcile an existing project)"
    AnsiConsole.MarkupLine "  [green]--no-governance[/]    skip the governance overlay"
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine "[dim]Run with no arguments on an interactive terminal to build the invocation with prompts.[/]"

// ── Interactive wizard (no-arg invocation) ────────────────────────────────────

/// The answers gathered so far — every field optional so the live preview can render a
/// half-built invocation and fill in as the user answers, step by step.
type private Draft =
    { Product: string option
      Target: string option
      Template: string option
      Profile: string option
      NpmPackage: string option
      NpmVersion: string option
      BindingTarget: string option
      Governance: bool option
      Ref: string option
      Pinned: bool option
      WorkspaceRepo: string option
      BoardOwner: string option
      BoardTitle: string option
      PublicBoard: bool option
      TrustedWriters: string list option
      ChoreLocks: string option }

let private emptyDraft =
    { Product = None
      Target = None
      Template = None
      Profile = None
      NpmPackage = None
      NpmVersion = None
      BindingTarget = None
      Governance = None
      Ref = None
      Pinned = None
      WorkspaceRepo = None
      BoardOwner = None
      BoardTitle = None
      PublicBoard = None
      TrustedWriters = None
      ChoreLocks = None }

/// Require a non-blank answer — the shared validator for the text prompts.
let private required (label: string) (s: string) =
    if String.IsNullOrWhiteSpace s then ValidationResult.Error(sprintf "[red]%s is required[/]" label)
    else ValidationResult.Success()

let private pendingCell = "[grey37]· pending[/]"

/// Left card: the parameters as a key/value grid, answered rows lit, unanswered dimmed.
let private paramsPanel (d: Draft) =
    let grid = Grid()
    grid.AddColumn() |> ignore
    grid.AddColumn() |> ignore
    let row (k: string) (v: string) = grid.AddRow(sprintf "[grey]%s[/]" k, v) |> ignore
    row "product" (d.Product |> Option.map (fun p -> sprintf "[bold green]%s[/]" (Markup.Escape p)) |> Option.defaultValue pendingCell)
    row "target" (d.Target |> Option.map (fun t -> sprintf "[green]%s[/]" (Markup.Escape t)) |> Option.defaultValue pendingCell)
    row "template" (d.Template |> Option.map (fun p -> sprintf "[magenta]%s[/]" (Markup.Escape p)) |> Option.defaultValue pendingCell)
    match d.Template, d.Profile with
    | Some "rendering", _ -> row "profile" (d.Profile |> Option.map (fun p -> sprintf "[magenta]%s[/]" (Markup.Escape p)) |> Option.defaultValue pendingCell)
    | _ -> ()
    match d.Template, d.NpmPackage, d.NpmVersion with
    | Some "fable-bindings", Some packageName, Some version -> row "npm" (sprintf "[magenta]%s@%s[/]" (Markup.Escape packageName) (Markup.Escape version))
    | Some "fable-bindings", _, _ -> row "npm" pendingCell
    | _ -> ()
    match d.Template, d.BindingTarget with
    | Some "fable-bindings", Some bindingTarget -> row "binding target" (Markup.Escape bindingTarget)
    | Some "fable-bindings", None -> row "binding target" pendingCell
    | _ -> ()
    row "governance"
        (match d.Governance with
         | Some true -> "[green]light[/] [grey]overlay[/]"
         | Some false -> "[grey]none (skipped)[/]"
         | None -> pendingCell)
    row "descriptor ref"
        (match d.Ref with
         | Some "main" -> "[aqua]main[/] [grey](newest set)[/]"
         | Some r -> sprintf "[aqua]%s[/] [grey](pinned)[/]" (Markup.Escape r)
         | None -> pendingCell)
    row "currency"
        (match d.Pinned with
         | Some true -> "[grey]pinned (installed CLI)[/]"
         | Some false -> "[green]update[/] [grey]fsgg-sdd first[/]"
         | None -> pendingCell)
    row "coordination"
        (let board =
             sprintf "board [aqua]%s/%s[/]"
                 (Markup.Escape(d.BoardOwner |> Option.defaultValue "FS-GG"))
                 (Markup.Escape(d.BoardTitle |> Option.defaultValue "Coordination"))
         match d.WorkspaceRepo with
         | Some r -> sprintf "%s [grey]· repo[/] [green]%s[/]" board (Markup.Escape r)
         | None -> sprintf "%s [grey](default on)[/]" board)
    row "Project visibility"
        (match d.PublicBoard with Some true -> "[green]public-readable[/]" | Some false -> "[aqua]private[/]" | None -> pendingCell)
    row "Project base permission" "[yellow]Read (human verification required)[/]"
    row "Project writers"
        (d.TrustedWriters |> Option.map (fun writers -> sprintf "[green]%s[/]" (Markup.Escape(String.Join(", ", writers)))) |> Option.defaultValue pendingCell)
    let panel = Panel(grid)
    panel.Header <- PanelHeader "[bold]parameters[/]"
    panel.Border <- BoxBorder.Rounded
    panel.Padding <- Padding(1, 0, 1, 0)
    panel

/// Right card: a tree of what the run will produce, growing as the answers land. Structural
/// nodes are always present (a workspace always has them); their annotations and the
/// optional leaves (game-core, governance) concretise as the draft fills in.
let private previewPanel (d: Draft) =
    let root = d.Target |> Option.map Markup.Escape |> Option.defaultValue "[grey37]<target>[/]"
    let tree = Tree(sprintf "[bold]%s[/]  [grey]· new workspace[/]" root)
    tree.Guide <- TreeGuide.BoldLine

    // The pre-scaffold self-update is a tooling step, not an output artifact, but it shapes how
    // current everything below will be — so it leads the tree (ADR-0030).
    (match d.Pinned with
     | Some true -> tree.AddNode "[grey37]fsgg-sdd — pinned to the installed build (--pinned)[/]"
     | Some false -> tree.AddNode "[green]fsgg-sdd self-update[/]  [grey](newest coherent set, before scaffold)[/]"
     | None -> tree.AddNode(sprintf "fsgg-sdd currency  %s" pendingCell))
    |> ignore

    let refAnno = d.Ref |> Option.map (fun r -> sprintf "[aqua]@ %s[/]" (Markup.Escape r)) |> Option.defaultValue pendingCell
    let template = d.Template |> Option.defaultValue "<template>"
    tree.AddNode(sprintf "[grey].fsgg/providers.yml[/]  %s descriptor %s" (Markup.Escape template) refAnno) |> ignore

    let prodAnno =
        d.Product |> Option.map (fun p -> sprintf "[grey](productName=[/][green]%s[/][grey])[/]" (Markup.Escape p)) |> Option.defaultValue pendingCell
    let sdd = tree.AddNode(sprintf "SDD lifecycle skeleton  %s" prodAnno)
    sdd.AddNode "[grey37]charter · spec · plan · tasks[/]" |> ignore

    let appLabel, profileAnno =
        match d.Template with
        | Some "rendering" -> "runnable Rendering app", d.Profile |> Option.map (fun p -> sprintf "[grey](fs-gg-ui · profile [/][magenta]%s[/][grey])[/]" (Markup.Escape p)) |> Option.defaultValue pendingCell
        | Some "console" -> "F# console executable", "[grey](no npm lane)[/]"
        | Some "web" -> "web workspace", "[grey](ASP.NET Core + TypeScript/Vite)[/]"
        | Some "fable-game" -> "Fable game workspace", "[grey](Elmish + game provider)[/]"
        | Some "fable-bindings" -> "Fable bindings library", d.BindingTarget |> Option.map (fun target -> sprintf "[grey](%s target)[/]" (Markup.Escape target)) |> Option.defaultValue pendingCell
        | _ -> "generated workspace", pendingCell
    let app = tree.AddNode(sprintf "%s  %s" appLabel profileAnno)
    app.AddNode "[grey37]dotnet build && dotnet run[/]" |> ignore
    // The simulation core materializes only for the game-family profiles (game, sample-pack).
    match d.Template, d.Profile with
    | Some "rendering", Some p when hasGameCore p -> app.AddNode "[grey37]+ fs-gg-game-core (fixed-step · seeded RNG · AABB)[/]" |> ignore
    | _ -> ()
    // The standalone FS.GG.Audio component ships on the same simulation profiles (own repo/axis;
    // the real host-side realization behind the pure AudioEffect edge). See ADR-0024.
    match d.Template, d.Profile with
    | Some "rendering", Some p when hasAudio p -> app.AddNode "[grey37]+ fs-gg-audio (buses · fades/ducking · 3D · device backend)[/]" |> ignore
    | _ -> ()

    (match d.Governance with
     | Some true -> tree.AddNode "[green]governance overlay[/]  [grey](profile: light)[/]"
     | Some false -> tree.AddNode "[grey37]governance overlay — skipped[/]"
     | None -> tree.AddNode(sprintf "governance overlay  %s" pendingCell))
    |> ignore

    let board =
        sprintf "%s/%s"
            (d.BoardOwner |> Option.defaultValue "FS-GG")
            (d.BoardTitle |> Option.defaultValue "Coordination")
    let coordination =
        tree.AddNode(sprintf "[green]coordination kit[/]  [grey](board [/][aqua]%s[/][grey])[/]" (Markup.Escape board))
    coordination.AddNode "[grey37].claude+.agents/skills · scripts/fsgg-coord · .config/dotnet-tools.json · .claude/settings.json env[/]" |> ignore
    let access = tree.AddNode "[yellow]Project access boundary[/]  [grey](base Read requires operator verification)[/]"
    access.AddNode(
        match d.PublicBoard with
        | Some true -> "[green]public-readable[/]"
        | Some false -> "[aqua]private[/]"
        | None -> pendingCell) |> ignore
    access.AddNode(sprintf "writers: %s" (d.TrustedWriters |> Option.map (String.concat ", " >> Markup.Escape) |> Option.defaultValue pendingCell)) |> ignore

    let panel = Panel(tree)
    panel.Header <- PanelHeader "[bold]scaffold preview[/]"
    panel.Border <- BoxBorder.Rounded
    panel.Padding <- Padding(1, 0, 1, 0)
    panel

/// The CLI you could have typed to skip the wizard — taught back as a dim footer. `--profile`
/// only shows for a non-default profile (game defers to the provider, so the flag is redundant).
let private equivalentCommand (d: Draft) =
    let parts = ResizeArray<string>()
    parts.Add "new-sdd-workspace"
    parts.Add(d.Target |> Option.defaultValue "<target>")
    parts.Add(d.Product |> Option.defaultValue "<product>")
    (match d.Template with Some t when t <> "rendering" -> parts.Add(sprintf "--template %s" t) | _ -> ())
    (match d.Template, d.Profile with Some "rendering", Some p when p <> "game" -> parts.Add(sprintf "--profile %s" p) | _ -> ())
    (match d.NpmPackage, d.NpmVersion with Some packageName, Some version -> parts.Add(sprintf "--npm-package %s --npm-version %s" packageName version) | _ -> ())
    (match d.BindingTarget with Some bindingTarget -> parts.Add(sprintf "--binding-target %s" bindingTarget) | _ -> ())
    (match d.Ref with Some r when r <> "main" -> parts.Add(sprintf "--ref %s" r) | _ -> ())
    let owner = d.BoardOwner |> Option.defaultValue "FS-GG"
    let title = d.BoardTitle |> Option.defaultValue "Coordination"
    if owner <> "FS-GG" || title <> "Coordination" then parts.Add(sprintf "--board %s/%s" owner title)
    (match d.WorkspaceRepo with
     | Some r when r <> sprintf "FS-GG/%s" (d.Product |> Option.defaultValue "") -> parts.Add(sprintf "--repo %s" r)
     | _ -> ())
    (match d.PublicBoard with Some true -> parts.Add "--public-board" | Some false -> parts.Add "--private-board" | None -> ())
    (match d.TrustedWriters with Some writers -> parts.Add(sprintf "--trusted-writers %s" (String.Join(",", writers))) | None -> ())
    (match d.ChoreLocks with Some cl -> parts.Add(sprintf "--chore-locks %s" cl) | None -> ())
    (match d.Pinned with Some true -> parts.Add "--pinned" | _ -> ())
    (match d.Governance with Some false -> parts.Add "--no-governance" | _ -> ())
    String.Join(" ", parts)

/// Clear and repaint the whole preview — the "getting fuller and fuller" frame the prompts
/// sit beneath. Called before each question so the just-captured answer shows up above.
let private draftView (d: Draft) =
    AnsiConsole.Clear()
    AnsiConsole.Write((Rule "[bold aqua]new-sdd-workspace[/] [grey]· interactive setup[/]").LeftJustified())
    AnsiConsole.WriteLine()
    let cards = ResizeArray<Rendering.IRenderable>()
    cards.Add(paramsPanel d :> Rendering.IRenderable)
    cards.Add(previewPanel d :> Rendering.IRenderable)
    AnsiConsole.Write(Columns(cards))
    AnsiConsole.MarkupLine(sprintf "[grey]equivalent:[/] [dim]%s[/]" (Markup.Escape(equivalentCommand d)))
    AnsiConsole.WriteLine()

/// When invoked with no arguments on an interactive terminal, walk the user through the
/// scaffold parameters with Spectre.Console prompts instead of failing with a usage error.
/// The surface follows the CLI defaults: product + target (text), profile + governance + ref
/// (selection), then the non-default coordination values. A live preview grows beside the prompts.
/// A non-interactive stdin never reaches here (see `main`), so the piped/CI usage-error contract
/// is unchanged.
/// Returns None if the user declines the final confirmation.
let private interactive () : Options option =
    let mutable draft = emptyDraft

    draftView draft
    let product =
        AnsiConsole.Prompt(
            TextPrompt<string>("[green]Product[/] name?")
                .Validate(fun (s: string) -> required "product name" s)).Trim()
    draft <- { draft with Product = Some product }

    draftView draft
    let target =
        AnsiConsole.Prompt(
            TextPrompt<string>("[green]Target[/] directory?")
                .DefaultValue("./" + product)
                .Validate(fun (s: string) -> required "target directory" s)).Trim()
    draft <- { draft with Target = Some target }

    draftView draft
    let template =
        AnsiConsole.Prompt(
            SelectionPrompt<string>()
                .Title("Application [magenta]type[/]?")
                .AddChoices(templates |> List.map (fun (id, gloss) -> sprintf "%s — %s" id gloss) |> List.toArray))
            .Split(' ').[0]
    draft <- { draft with Template = Some template }

    let profile =
        if supportsProfile template then
            draftView draft
            let selected =
                AnsiConsole.Prompt(
                    SelectionPrompt<string>()
                        .Title("Render [magenta]profile[/]?")
                        .AddChoices(profiles |> List.map (fun (id, gloss) -> sprintf "%s — %s" id gloss) |> List.toArray))
                    .Split(' ').[0]
            draft <- { draft with Profile = Some selected }
            selected
        else
            ""

    let npmPackage, npmVersion, bindingTarget =
        if requiresNpmClosure template then
            draftView draft
            let packageName = AnsiConsole.Prompt(TextPrompt<string>("npm [green]package[/]?").Validate(fun s -> required "npm package" s)).Trim()
            draft <- { draft with NpmPackage = Some packageName }
            draftView draft
            let version = AnsiConsole.Prompt(TextPrompt<string>("exact npm [green]version[/]?").Validate(fun s -> required "npm version" s)).Trim()
            draft <- { draft with NpmVersion = Some version }
            draftView draft
            let target =
                AnsiConsole.Prompt(
                    SelectionPrompt<string>()
                        .Title("Bindings runtime [magenta]target[/]?")
                        .AddChoices([| "browser"; "node"; "universal" |]))
            draft <- { draft with BindingTarget = Some target }
            Some packageName, Some version, Some target
        else None, None, None

    draftView draft
    let governance =
        AnsiConsole.Prompt(
            SelectionPrompt<string>()
                .Title("[green]Governance[/] overlay?")
                .AddChoices([| "light — apply the overlay (recommended)"; "none — skip it (--no-governance)" |]))
            .StartsWith "light"
    draft <- { draft with Governance = Some governance }

    draftView draft
    let gitRef =
        let choice =
            AnsiConsole.Prompt(
                SelectionPrompt<string>()
                    .Title("Descriptor [green]ref[/] to pin the coherent set?")
                    .AddChoices([| "main — newest coherent set"; "pin a specific ref…" |]))
        if choice.StartsWith "main" then
            "main"
        else
            AnsiConsole.Prompt(
                TextPrompt<string>("  Git ref [grey](tag / branch / sha)[/]?")
                    .Validate(fun (s: string) -> required "ref" s)).Trim()
    draft <- { draft with Ref = Some gitRef }

    draftView draft
    let pinned =
        AnsiConsole.Prompt(
            SelectionPrompt<string>()
                .Title("Tooling [green]currency[/] before scaffolding?")
                .AddChoices(
                    [| "update — self-update fsgg-sdd to the newest build (default, recommended)"
                       "pinned — scaffold with the installed CLI (--pinned)" |]))
            .StartsWith "pinned"
    draft <- { draft with Pinned = Some pinned }

    draftView draft
    // Coordination remains default-on. Ask only for its meaningful values; scripted callers retain
    // `--no-coordination` when they intentionally want to skip the whole step.
    let workspaceRepo =
        AnsiConsole.Prompt(
            TextPrompt<string>("  This workspace's [green]repo[/] [grey](owner/repo)[/]?")
                .DefaultValue(sprintf "FS-GG/%s" product)
                .Validate(fun (s: string) -> required "repo" s)).Trim()
    let boardOwner =
        AnsiConsole.Prompt(
            TextPrompt<string>("  Coordination board [green]org[/] [grey](owner)[/]?")
                .DefaultValue(fst (parseBoard workspaceRepo))
                .Validate(fun (s: string) -> required "org" s)).Trim()
    let boardTitle =
        AnsiConsole
            .Prompt(TextPrompt<string>("  Board [green]title[/]?").DefaultValue("Coordination"))
            .Trim()
    let choreLocks =
        let raw =
            AnsiConsole
                .Prompt(
                    TextPrompt<string>(
                        "  [green]Chore-locks[/] [grey](owner/repo#n,… — non-FS-GG boards; blank to skip)[/]?"
                    )
                        .AllowEmpty())
                .Trim()
        if String.IsNullOrWhiteSpace raw then None else Some raw
    draftView draft
    let publicBoard =
        AnsiConsole.Prompt(
            SelectionPrompt<string>()
                .Title("Product Project [green]visibility[/]?")
                .AddChoices([| "private — preserve private access"; "public — public-readable, never public-writable" |]))
            .StartsWith "public"
    draft <- { draft with PublicBoard = Some publicBoard }
    draftView draft
    let trustedWriters =
        AnsiConsole.Prompt(
            TextPrompt<string>("  Explicit Project [green]writers[/] [grey](team/user ids, comma-separated)[/]?")
                .Validate(fun (s: string) -> required "trusted writer allowlist" s))
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun s -> s.Trim())
        |> Array.filter (String.IsNullOrWhiteSpace >> not)
        |> List.ofArray
    draft <-
        { draft with
            WorkspaceRepo = Some workspaceRepo
            BoardOwner = Some boardOwner
            BoardTitle = Some boardTitle
            PublicBoard = Some publicBoard
            TrustedWriters = Some trustedWriters
            ChoreLocks = choreLocks }

    // Final full preview, then a go/no-go before anything touches disk.
    draftView draft
    if AnsiConsole.Confirm("[bold]Create this scaffold now?[/]", true) then
        Some({ assembleWizardOptions target product template gitRef governance pinned (if supportsProfile template then Some profile else None) npmPackage npmVersion bindingTarget workspaceRepo boardOwner boardTitle choreLocks with
                 PublicBoard = Some publicBoard
                 TrustedWriters = trustedWriters })
    else
        None

// ── Arg parsing ──────────────────────────────────────────────────────────────

let private parse (argv: string list) : Result<Options, string> =
    let knownProfiles = profiles |> List.map fst
    let knownTemplates = templates |> List.map fst
    let validate (opts: Options) =
        match opts.Profile, opts.Template with
        | Some _, template when not (supportsProfile template) ->
            Error(sprintf "--profile is only supported by the rendering template (selected: %s)" template)
        | _, template when requiresNpmClosure template && (opts.NpmPackage.IsNone || opts.NpmVersion.IsNone) ->
            Error "--template fable-bindings requires both --npm-package and --npm-version"
        | _, template when requiresNpmClosure template && opts.BindingTarget.IsNone ->
            Error "--template fable-bindings requires --binding-target (browser, node, or universal)"
        | _, template when (opts.NpmPackage.IsSome || opts.NpmVersion.IsSome) && not (requiresNpmClosure template) ->
            Error(sprintf "--npm-package/--npm-version are only supported by the fable-bindings template (selected: %s)" template)
        | _, template when opts.BindingTarget.IsSome && not (requiresNpmClosure template) ->
            Error(sprintf "--binding-target is only supported by the fable-bindings template (selected: %s)" template)
        | _, _ when opts.BindingTarget |> Option.exists (fun target -> not (List.contains target [ "browser"; "node"; "universal" ])) ->
            Error "--binding-target must be browser, node, or universal"
        | _, template when requiresNpmClosure template && (opts.NpmVersion |> Option.exists (fun version -> String.IsNullOrWhiteSpace version || version.Equals("latest", StringComparison.OrdinalIgnoreCase) || version.IndexOfAny([| '*'; '^'; '~'; '>'; '<'; '|'; ' ' |]) >= 0)) ->
            Error "--npm-version must be an exact version (not latest or a range)"
        | _ -> Ok opts
    // A `--flag`-looking token is a missing value, not a value — the same guard repos.sh's
    // `need_val` applies. Without it, `new-sdd-workspace ./x P --profile --ref v1` swallows
    // `--ref` as the profile and then blames `v1` (`unknown argument: v1`) for a mistake made
    // two args earlier. `--profile` is also validated here against the known set, so an invalid
    // profile is caught on the CLI path — not late, inside the `fsgg-sdd scaffold` child — to
    // match the interactive wizard, which already constrains it to `profiles`.
    let rec flags (acc: Options) rest =
        match rest with
        | [] -> validate acc
        | "--template" :: value :: _ when value.StartsWith "--" ->
            Error(sprintf "--template needs a value (got flag '%s')" value)
        | "--template" :: value :: t ->
            if List.contains value knownTemplates then flags { acc with Template = value } t
            else Error(sprintf "unknown template '%s' (choose one of: %s)" value (String.Join(", ", knownTemplates)))
        | [ "--template" ] -> Error "--template needs a value"
        | "--profile" :: value :: _ when value.StartsWith "--" ->
            Error(sprintf "--profile needs a value (got flag '%s')" value)
        | "--profile" :: value :: t ->
            if List.contains value knownProfiles then
                flags { acc with Profile = Some value } t
            else
                Error(sprintf "unknown profile '%s' (choose one of: %s)" value (String.Join(", ", knownProfiles)))
        | [ "--profile" ] -> Error "--profile needs a value"
        | "--npm-package" :: value :: _ when value.StartsWith "--" ->
            Error(sprintf "--npm-package needs a value (got flag '%s')" value)
        | "--npm-package" :: value :: t -> flags { acc with NpmPackage = Some value } t
        | [ "--npm-package" ] -> Error "--npm-package needs a value"
        | "--npm-version" :: value :: _ when value.StartsWith "--" ->
            Error(sprintf "--npm-version needs a value (got flag '%s')" value)
        | "--npm-version" :: value :: t -> flags { acc with NpmVersion = Some value } t
        | [ "--npm-version" ] -> Error "--npm-version needs a value"
        | "--binding-target" :: value :: _ when value.StartsWith "--" ->
            Error(sprintf "--binding-target needs a value (got flag '%s')" value)
        | "--binding-target" :: value :: t -> flags { acc with BindingTarget = Some value } t
        | [ "--binding-target" ] -> Error "--binding-target needs a value"
        | "--ref" :: value :: _ when value.StartsWith "--" ->
            Error(sprintf "--ref needs a value (got flag '%s')" value)
        | "--ref" :: value :: t -> flags { acc with Ref = value } t
        | [ "--ref" ] -> Error "--ref needs a value"
        | "--board" :: value :: _ when value.StartsWith "--" ->
            Error(sprintf "--board needs a value (got flag '%s')" value)
        | "--board" :: value :: t ->
            let owner, title = parseBoard value
            flags { acc with BoardOwner = owner; BoardTitle = title } t
        | [ "--board" ] -> Error "--board needs a value"
        | "--repo" :: value :: _ when value.StartsWith "--" ->
            Error(sprintf "--repo needs a value (got flag '%s')" value)
        | "--repo" :: value :: t -> flags { acc with WorkspaceRepo = Some value } t
        | [ "--repo" ] -> Error "--repo needs a value"
        | "--public-board" :: t -> flags { acc with PublicBoard = Some true } t
        | "--private-board" :: t -> flags { acc with PublicBoard = Some false } t
        | "--trusted-writers" :: value :: _ when value.StartsWith "--" ->
            Error(sprintf "--trusted-writers needs a value (got flag '%s')" value)
        | "--trusted-writers" :: value :: t ->
            let writers = value.Split(',', StringSplitOptions.RemoveEmptyEntries) |> Array.map (fun v -> v.Trim()) |> Array.filter (String.IsNullOrWhiteSpace >> not) |> List.ofArray
            if List.isEmpty writers then Error "--trusted-writers needs at least one team or user"
            else flags { acc with TrustedWriters = writers } t
        | [ "--trusted-writers" ] -> Error "--trusted-writers needs a value"
        | "--chore-locks" :: value :: _ when value.StartsWith "--" ->
            Error(sprintf "--chore-locks needs a value (got flag '%s')" value)
        | "--chore-locks" :: value :: t -> flags { acc with ChoreLocks = Some value } t
        | [ "--chore-locks" ] -> Error "--chore-locks needs a value"
        | "--no-coordination" :: t -> flags { acc with Coordinate = false } t
        | "--pinned" :: t -> flags { acc with Pinned = true } t
        | "--upgrade" :: t -> flags { acc with Upgrade = true } t
        | "--no-governance" :: t -> flags { acc with Governance = false } t
        | other :: _ -> Error(sprintf "unknown argument: %s" other)
    match argv with
    | target :: product :: rest when not (target.StartsWith "--") && not (product.StartsWith "--") ->
        flags
            { Options.Target = target
              Product = product
              Template = "rendering"
              Ref = "main"
              Upgrade = false
              Governance = true
              Pinned = false
              Profile = None
              NpmPackage = None
              NpmVersion = None
              BindingTarget = None
              Coordinate = true
              WorkspaceRepo = None
              BoardOwner = "FS-GG"
              BoardTitle = "Coordination"
              PublicBoard = None
              TrustedWriters = []
              ChoreLocks = None }
            rest
        |> Result.bind (fun opts ->
            match opts.PublicBoard with
            | Some true when List.isEmpty opts.TrustedWriters -> Error "--public-board requires an explicit --trusted-writers allowlist"
            | _ -> Ok opts)
    | _ -> Error "target dir and product name are required"

/// Parse `retrofit <target> [options]` — the coordination-retrofit subcommand. Only the coordination
/// inputs (`--board`/`--repo`/`--chore-locks`/`--ref`) are accepted; the scaffold/render/governance
/// flags are meaningless on an existing workspace and are rejected as unknown. Carries the same
/// #388 flag-as-value guard as `parse`.
let private parseRetrofit (argv: string list) : Result<RetrofitOptions, string> =
    let rec flags (acc: RetrofitOptions) rest =
        match rest with
        | [] -> Ok acc
        | "--board" :: value :: _ when value.StartsWith "--" ->
            Error(sprintf "--board needs a value (got flag '%s')" value)
        | "--board" :: value :: t ->
            let owner, title = parseBoard value
            flags { acc with BoardOwner = owner; BoardTitle = title } t
        | [ "--board" ] -> Error "--board needs a value"
        | "--repo" :: value :: _ when value.StartsWith "--" ->
            Error(sprintf "--repo needs a value (got flag '%s')" value)
        | "--repo" :: value :: t -> flags { acc with WorkspaceRepo = Some value } t
        | [ "--repo" ] -> Error "--repo needs a value"
        | "--chore-locks" :: value :: _ when value.StartsWith "--" ->
            Error(sprintf "--chore-locks needs a value (got flag '%s')" value)
        | "--chore-locks" :: value :: t -> flags { acc with ChoreLocks = Some value } t
        | [ "--chore-locks" ] -> Error "--chore-locks needs a value"
        | "--ref" :: value :: _ when value.StartsWith "--" ->
            Error(sprintf "--ref needs a value (got flag '%s')" value)
        | "--ref" :: value :: t -> flags { acc with Ref = value } t
        | [ "--ref" ] -> Error "--ref needs a value"
        | other :: _ -> Error(sprintf "unknown argument: %s" other)
    match argv with
    | target :: rest when not (target.StartsWith "--") ->
        flags
            { RetrofitOptions.Target = target
              Ref = "main"
              WorkspaceRepo = None
              BoardOwner = "FS-GG"
              BoardTitle = "Coordination"
              ChoreLocks = None }
            rest
    | _ -> Error "retrofit needs a target directory (the workspace to wire): retrofit <target> [--board owner/title]"

// ── Orchestration ────────────────────────────────────────────────────────────

let private run (opts: Options) : int =
    header opts

    if not (onPath "fsgg-sdd") then
        // Preflight (matches the shell's exit 127): steps 2/3/5/6 drive `fsgg-sdd`, so fail fast
        // with an actionable message rather than a bare "command not found" mid-scaffold.
        let panel =
            Panel(
                "[red]fsgg-sdd is not on PATH.[/]\n\nInstall the CLI first, then re-run:\n  [bold]dotnet tool install --global FS.GG.SDD.Cli[/]"
            )
        panel.Header <- PanelHeader "[red]preflight failed[/]"
        panel.Border <- BoxBorder.Rounded
        panel.Padding <- Padding(1, 0, 1, 0)
        AnsiConsole.Write panel
        127
    else
        let results = ResizeArray<StepResult>()
        let mutable fatal = false

        // 1 · fetch descriptor (fatal on failure)
        step 1 "fetch provider descriptor"
        let descriptorPath = Path.Combine(opts.Target, ".fsgg", "providers.yml")
        let fetched =
            AnsiConsole
                .Status()
                .Start(
                    sprintf "fetching %s descriptor from FS.GG.Templates@%s…" opts.Template opts.Ref,
                    fun _ -> fetchDescriptor opts.Template opts.Ref descriptorPath
                )
        match fetched with
        | Ok pinned ->
            match pinned with
            | Some line -> AnsiConsole.MarkupLine(sprintf "  [green]✓[/] pinned: [grey]%s[/]" (Markup.Escape line))
            | None -> AnsiConsole.MarkupLine "  [green]✓[/] descriptor fetched"
            results.Add { Title = "fetch descriptor"; Outcome = Succeeded }
        | Error e ->
            AnsiConsole.MarkupLine(sprintf "  [red]✗[/] %s" (Markup.Escape e))
            results.Add { Title = "fetch descriptor"; Outcome = Failed e }
            fatal <- true

        // 2 · update fsgg-sdd to the newest coherent set BEFORE scaffolding — the DEFAULT
        //     (ADR-0030, the creation-time carve-out to ADR-0009); --pinned skips it. Non-fatal:
        //     an offline/failed update warns and scaffolding proceeds with the installed CLI.
        if opts.Pinned then
            results.Add { Title = "update fsgg-sdd"; Outcome = Skipped "--pinned (installed CLI)" }
        elif not fatal then
            step 2 "update fsgg-sdd"
            let outcome = selfUpdateCli ()
            (match outcome with
             | Succeeded -> AnsiConsole.MarkupLine "  [green]✓[/] fsgg-sdd is at the newest published build"
             | Warned n -> AnsiConsole.MarkupLine(sprintf "  [yellow]⚠[/] %s" (Markup.Escape n))
             | Skipped r -> AnsiConsole.MarkupLine(sprintf "  [yellow]⊘[/] %s" (Markup.Escape r))
             | Failed n -> AnsiConsole.MarkupLine(sprintf "  [red]✗[/] %s" (Markup.Escape n)))
            results.Add { Title = "update fsgg-sdd"; Outcome = outcome }

        // 3 · fsgg-sdd scaffold (fatal on failure)
        if not fatal then
            step 3 "fsgg-sdd scaffold"
            // Provider parameters stay provider-scoped: profile belongs to rendering; the bindings
            // package closure belongs to fable-bindings. Omitted --template remains rendering.
            let profileParam =
                match opts.Profile with
                | Some p -> [ "--param"; sprintf "profile=%s" p ]
                | None -> []
            let npmParams =
                match opts.NpmPackage, opts.NpmVersion with
                | Some packageName, Some version -> [ "--param"; sprintf "npmPackage=%s" packageName; "--param"; sprintf "npmVersion=%s" version ]
                | _ -> []
            let bindingTargetParam =
                opts.BindingTarget |> Option.map (fun target -> [ "--param"; sprintf "target=%s" target ]) |> Option.defaultValue []
            let code, _ =
                runProcess true "fsgg-sdd"
                    ([ "scaffold"; "--root"; opts.Target; "--provider"; opts.Template ]
                     @ profileParam
                     @ npmParams
                     @ bindingTargetParam
                     @ [ "--param"; sprintf "productName=%s" opts.Product ])
            if code = 0 then
                AnsiConsole.MarkupLine(sprintf "  [green]✓[/] SDD skeleton + %s workspace scaffolded" (Markup.Escape opts.Template))
                results.Add { Title = "scaffold"; Outcome = Succeeded }
            else
                AnsiConsole.MarkupLine(sprintf "  [red]✗[/] scaffold failed (exit %d)" code)
                results.Add { Title = "scaffold"; Outcome = Failed(sprintf "exit %d" code) }
                fatal <- true

        // 4 · governance overlay (non-fatal; best-effort — needs the published template on a reachable feed)
        if not opts.Governance then
            results.Add { Title = "governance overlay"; Outcome = Skipped "--no-governance" }
        elif not fatal then
            step 4 "governance overlay"
            let installCode, installLog = installGovernanceTemplate ()
            if installCode = 0 then
                let govCode, _ =
                    runProcess true "dotnet"
                        [ "new"; "fs-gg-governance"; "-o"; opts.Target; "--appName"; opts.Product; "--defaultProfile"; "light" ]
                if govCode = 0 then
                    AnsiConsole.MarkupLine "  [green]✓[/] governance overlay applied (profile: light / non-blocking)"
                    results.Add { Title = "governance overlay"; Outcome = Succeeded }
                else
                    AnsiConsole.MarkupLine "  [yellow]⚠[/] overlay command failed — the product is fine without it"
                    results.Add { Title = "governance overlay"; Outcome = Warned "overlay command failed; product is fine without it" }
            else
                let reason =
                    match feedToken () with
                    | Some _ ->
                        "could not install the FS.GG.Templates template from the org feed or nuget.org"
                    | None ->
                        "could not install the FS.GG.Templates template from nuget.org (network?) — "
                        + "set FSGG_PACKAGES_TOKEN (or GH_TOKEN) to a read:packages token to try the org feed too"
                AnsiConsole.MarkupLine(sprintf "  [yellow]⊘[/] %s — skipped" reason)
                installLog.Replace("\r\n", "\n").Split('\n')
                |> Array.iter (fun l -> if not (String.IsNullOrWhiteSpace l) then dim l)
                AnsiConsole.MarkupLine(
                    sprintf
                        "  add later: [grey]dotnet new install FS.GG.Templates --nuget-source %s && dotnet new fs-gg-governance -o %s --appName %s[/]"
                        nugetOrg
                        (Markup.Escape opts.Target)
                        (Markup.Escape opts.Product)
                )
                results.Add { Title = "governance overlay"; Outcome = Skipped "FS.GG.Templates template feed not reachable" }

        // 5 · wire the workspace to its coordination board — vendor the kit + write the FSGG_COORD_*
        //     env (default ON; --no-coordination skips). Best-effort like governance: a 404 on a kit file
        //     warns and the env still lands. Opens ADR-0019's deferred product-mirror slice; unblocked by
        //     the env-multi-tenant engine (#1140).
        if not opts.Coordinate then
            results.Add { Title = "coordination"; Outcome = Skipped "--no-coordination" }
        elif not fatal then
            step 5 "coordination"
            let outcome =
                AnsiConsole
                    .Status()
                    .Start(
                        sprintf "vendoring the coordination kit for %s/%s…" opts.BoardOwner opts.BoardTitle,
                        fun _ -> wireCoordination "main" opts
                    )
            (match outcome with
             | Succeeded ->
                 AnsiConsole.MarkupLine(
                     sprintf
                         "  [green]✓[/] kit vendored + env written — board [aqua]%s/%s[/]"
                         (Markup.Escape opts.BoardOwner)
                         (Markup.Escape opts.BoardTitle)
                 )
                 if opts.BoardOwner.ToLowerInvariant() <> "fs-gg" then
                     AnsiConsole.MarkupLine
                         "  [grey]note: offer/chores on a non-FS-GG board need an engine build with #1140 (post-0.4.0)[/]"
             | Warned n -> AnsiConsole.MarkupLine(sprintf "  [yellow]⚠[/] %s" (Markup.Escape n))
             | Skipped r -> AnsiConsole.MarkupLine(sprintf "  [yellow]⊘[/] %s" (Markup.Escape r))
             | Failed n -> AnsiConsole.MarkupLine(sprintf "  [red]✗[/] %s" (Markup.Escape n)))
            results.Add { Title = "coordination"; Outcome = outcome }

        // 5b · repository issue-intake policy. This is intentionally a distinct, typed step from
        // board wiring: a board can be readable while its repository has not been created yet, and
        // that must produce a durable pending security result rather than a false secured summary.
        if opts.Coordinate && not fatal then
            step 5 "repository security"
            let report = workspaceSecurity opts
            let outcome = report.Outcome
            recordSecurityObligations opts report
            match outcome with
            | Succeeded -> AnsiConsole.MarkupLine "  [green]✓[/] repository issue policy verified"
            | Warned note -> AnsiConsole.MarkupLine(sprintf "  [yellow]⚠[/] %s" (Markup.Escape note))
            | Skipped reason -> AnsiConsole.MarkupLine(sprintf "  [yellow]⊘[/] %s" (Markup.Escape reason))
            | Failed note -> AnsiConsole.MarkupLine(sprintf "  [red]✗[/] %s" (Markup.Escape note))
            results.Add { Title = "repository security"; Outcome = outcome }

        // 6 · fsgg-sdd doctor (read-only, non-fatal — matches the shell's `|| true`)
        if not fatal then
            step 6 "fsgg-sdd doctor"
            let code, _ = runProcess true "fsgg-sdd" [ "doctor"; "--root"; opts.Target ]
            if code = 0 then
                AnsiConsole.MarkupLine "  [green]✓[/] product is coherent with its set"
                results.Add { Title = "doctor"; Outcome = Succeeded }
            else
                AnsiConsole.MarkupLine(sprintf "  [yellow]⚠[/] doctor reported issues (exit %d) — non-blocking" code)
                results.Add { Title = "doctor"; Outcome = Warned(sprintf "reported issues (exit %d)" code) }

        // 7 · fsgg-sdd upgrade (optional; fatal on failure, matching the shell's set -e). With the
        //     default pre-scaffold self-update this is largely redundant on a fresh scaffold; it
        //     stays for the explicit "reconcile an existing project" invocation.
        if opts.Upgrade && not fatal then
            step 7 "fsgg-sdd upgrade"
            let code, _ = runProcess true "fsgg-sdd" [ "upgrade"; "--root"; opts.Target ]
            if code = 0 then
                AnsiConsole.MarkupLine "  [green]✓[/] reconciled to the current coherent set"
                results.Add { Title = "upgrade"; Outcome = Succeeded }
            else
                AnsiConsole.MarkupLine(sprintf "  [red]✗[/] upgrade failed (exit %d)" code)
                results.Add { Title = "upgrade"; Outcome = Failed(sprintf "exit %d" code) }
                fatal <- true

        summary results opts fatal
        if fatal then 1 else 0

// ── Retrofit orchestration ────────────────────────────────────────────────────

let private retrofitHeader (opts: RetrofitOptions) =
    let grid = Grid()
    grid.AddColumn() |> ignore
    grid.AddColumn() |> ignore
    grid.AddRow("[grey]target[/]", Markup.Escape opts.Target) |> ignore
    grid.AddRow(
        "[grey]board[/]",
        sprintf "[aqua]%s/%s[/]" (Markup.Escape opts.BoardOwner) (Markup.Escape opts.BoardTitle)
    )
    |> ignore
    opts.WorkspaceRepo
    |> Option.iter (fun r -> grid.AddRow("[grey]repo[/]", Markup.Escape r) |> ignore)
    grid.AddRow("[grey]kit ref[/]", Markup.Escape opts.Ref) |> ignore
    let panel = Panel(grid)
    panel.Header <- PanelHeader "[bold]new-sdd-workspace[/] [grey]· retrofit coordination[/]"
    panel.Border <- BoxBorder.Rounded
    panel.Padding <- Padding(1, 0, 1, 0)
    AnsiConsole.Write panel

/// Retrofit the coordination kit + board env onto an already-scaffolded workspace. Idempotent: it
/// materializes only what is missing, repairs only what has drifted, and no-ops cleanly on a coherent
/// workspace. Refuses (exit 2) a directory that is not a scaffolded workspace (no `.fsgg/`). Exit 1
/// only when the kit could not be materialized AT ALL (every fetch failed on an unwired workspace);
/// otherwise 0, mirroring the best-effort contract of the scaffold-time coordination step.
let private runRetrofit (opts: RetrofitOptions) : int =
    retrofitHeader opts
    let fsggDir = Path.Combine(opts.Target, ".fsgg")
    if not (Directory.Exists fsggDir) then
        // The precondition the issue names: a workspace has a `.fsgg/` config. No `.fsgg/` ⇒ this is not
        // a scaffolded workspace, so there is nothing to retrofit ONTO — refuse cleanly, naming the fix.
        // A concise leading line (no target path) so the refusal is greppable on one line at any width.
        AnsiConsole.WriteLine()
        AnsiConsole.MarkupLine "[red]retrofit refused:[/] not a scaffolded workspace (no .fsgg/ directory)"
        let panel =
            Panel(
                sprintf
                    "[red]%s is not a scaffolded workspace[/] (no [grey].fsgg/[/] directory).\n\nRetrofit wires coordination ONTO an existing workspace. Scaffold one first:\n  [bold]new-sdd-workspace %s <product-name>[/]"
                    (Markup.Escape opts.Target)
                    (Markup.Escape opts.Target)
            )
        panel.Header <- PanelHeader "[red]nothing to retrofit[/]"
        panel.Border <- BoxBorder.Rounded
        panel.Padding <- Padding(1, 0, 1, 0)
        AnsiConsole.Write panel
        2
    else
        step 1 "retrofit coordination kit"
        let report =
            AnsiConsole
                .Status()
                .Start(
                    sprintf "reconciling the coordination kit for %s/%s…" opts.BoardOwner opts.BoardTitle,
                    fun _ -> retrofitCoordination opts
                )
        let materialized = report.Wrote |> List.choose (fun (r, m) -> if m then Some r else None)
        let drift = report.Wrote |> List.choose (fun (r, m) -> if m then None else Some r)
        let changed = not (List.isEmpty report.Wrote)

        // Surface any fetch/write problems (best-effort — a 404 on one kit file never fails the run).
        for p in report.Problems do
            AnsiConsole.MarkupLine(sprintf "  [yellow]⚠[/] %s" (Markup.Escape p))

        if changed then
            // Fresh materialization and/or drift repair happened → record it in the provenance log.
            recordRetrofit opts.Target opts materialized drift
            if not (List.isEmpty materialized) then
                AnsiConsole.MarkupLine(
                    sprintf "  [green]✓[/] materialized %d missing kit piece(s): [grey]%s[/]"
                        materialized.Length (Markup.Escape(String.Join(", ", materialized)))
                )
            if not (List.isEmpty drift) then
                AnsiConsole.MarkupLine(
                    sprintf "  [green]✓[/] re-emitted %d drifted kit piece(s): [grey]%s[/]"
                        drift.Length (Markup.Escape(String.Join(", ", drift)))
                )
            if not (List.isEmpty report.Kept) then
                AnsiConsole.MarkupLine(
                    sprintf "  [grey]· %d piece(s) already coherent — left untouched[/]" report.Kept.Length
                )
            AnsiConsole.MarkupLine(
                sprintf
                    "  [green]✓[/] recorded retrofit in [grey].fsgg/scaffold-provenance.json[/] — board [aqua]%s/%s[/]"
                    (Markup.Escape opts.BoardOwner)
                    (Markup.Escape opts.BoardTitle)
            )
            if opts.BoardOwner.ToLowerInvariant() <> "fs-gg" then
                AnsiConsole.MarkupLine
                    "  [grey]note: offer/chores on a non-FS-GG board need an engine build with #1140 (post-0.4.0)[/]"
            AnsiConsole.WriteLine()
            AnsiConsole.MarkupLine(
                sprintf "[bold]Done:[/] [green]%s[/] is now wired for coordination — /pnext-item and /check-board work." (Markup.Escape opts.Target)
            )
            0
        elif not (List.isEmpty report.Kept) then
            // Nothing written, but pieces are present → already wired. Refuse cleanly (no partial state):
            // the kit is coherent, so the retrofit is a no-op and no provenance entry is appended.
            AnsiConsole.MarkupLine(
                sprintf "  [green]✓[/] already wired — %d kit piece(s) coherent, no drift to re-emit" report.Kept.Length
            )
            if not (List.isEmpty report.Problems) then
                AnsiConsole.MarkupLine
                    "  [yellow]⚠[/] some kit files could not be fetched to verify — re-run when the network is reachable"
            AnsiConsole.WriteLine()
            AnsiConsole.MarkupLine(
                sprintf "[bold]Already wired:[/] [green]%s[/] carries the coordination kit + board env." (Markup.Escape opts.Target)
            )
            0
        else
            // Nothing present and nothing written — every fetch failed on an unwired workspace. Nothing
            // was materialized, so there is no partial state; report the failure and exit non-zero.
            let panel =
                Panel(
                    "[red]could not vendor the coordination kit[/] — every fetch from FS-GG/.github failed.\n\nCheck network reachability and re-run; nothing was written, so the workspace is unchanged."
                )
            panel.Header <- PanelHeader "[red]retrofit failed[/]"
            panel.Border <- BoxBorder.Rounded
            panel.Padding <- Padding(1, 0, 1, 0)
            AnsiConsole.Write panel
            1

/// Re-run just the repository policy after a freshly scaffolded repository has been created or
/// an operator has received the required administration grant. This is the resumable half of the
/// scaffold-time pending receipt; it has no filesystem side effects and never prints credentials.
let private clearRepositorySecurityObligation (target: string) (repository: string) (prior: string) (actor: string) =
    let path = Path.Combine(target, ".fsgg", "scaffold-provenance.json")
    if File.Exists path then
        let root = JsonNode.Parse(File.ReadAllText path).AsObject()
        match root.["securityObligations"] with
        | :? JsonArray as obligations ->
            let kept = JsonArray()
            obligations
            |> Seq.filter (fun entry ->
                let row = entry.AsObject()
                not (row.["kind"].GetValue<string>() = "repository-issue-policy" && row.["target"].GetValue<string>() = repository))
            |> Seq.iter (fun entry -> kept.Add(entry.DeepClone()))
            root.["securityObligations"] <- kept
            let receipt = JsonObject()
            receipt.["kind"] <- JsonValue.Create "repository-issue-policy"
            receipt.["repository"] <- JsonValue.Create repository
            receipt.["priorPolicy"] <- JsonValue.Create prior
            receipt.["finalPolicy"] <- JsonValue.Create "COLLABORATORS_ONLY"
            receipt.["actor"] <- JsonValue.Create actor
            receipt.["source"] <- JsonValue.Create "GitHub GraphQL repository.issueCreationPolicy re-read"
            root.["verifiedSecurityReceipts"] <- JsonArray(receipt)
            File.WriteAllText(path, root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))
        | _ -> ()

let private clearProjectSecurityObligation (target: string) (project: string) =
    let path = Path.Combine(target, ".fsgg", "scaffold-provenance.json")
    if File.Exists path then
        let root = JsonNode.Parse(File.ReadAllText path).AsObject()
        match root.["securityObligations"] with
        | :? JsonArray as obligations ->
            let kept = JsonArray()
            obligations
            |> Seq.filter (fun entry ->
                let row = entry.AsObject()
                not (row.["kind"].GetValue<string>() = "project-access" && row.["target"].GetValue<string>() = project))
            |> Seq.iter (fun entry -> kept.Add(entry.DeepClone()))
            let baseAccess = JsonObject()
            baseAccess.["kind"] <- JsonValue.Create "project-base-access-human-verification"
            baseAccess.["target"] <- JsonValue.Create project
            baseAccess.["expectedBasePermission"] <- JsonValue.Create "READ"
            baseAccess.["resume"] <- JsonValue.Create "new-sdd-workspace secure <workspace> --project <owner/title> --public-board|--private-board --trusted-writers <ids>"
            baseAccess.["state"] <- JsonValue.Create "pending-human-verification"
            kept.Add(baseAccess)
            root.["securityObligations"] <- kept
            File.WriteAllText(path, root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))
        | _ -> ()

let private runSecure (target: string option) (repository: string) : int =
    match secureRepository repository with
    | RepositorySecured(repo, prior, actor) ->
        target |> Option.iter (fun workspace -> clearRepositorySecurityObligation workspace repo prior actor)
        AnsiConsole.MarkupLine(sprintf "[green]verified:[/] %s IssueCreationPolicy is COLLABORATORS_ONLY (prior %s; actor %s)" (Markup.Escape repo) (Markup.Escape prior) (Markup.Escape actor))
        0
    | RepositoryPending(repo, reason) ->
        AnsiConsole.MarkupLine(sprintf "[yellow]pending:[/] %s — %s" (Markup.Escape repo) (Markup.Escape reason))
        1

let private runSecureProject (target: string) (board: string) (isPublic: bool) (writers: string list) : int =
    let owner, title = parseBoard board
    match applyProjectVisibility owner title (Some isPublic) with
    | ProjectPending(project, reason) ->
        AnsiConsole.MarkupLine(sprintf "[yellow]pending:[/] %s — %s" (Markup.Escape project) (Markup.Escape reason))
        1
    | ProjectObserved(project, _, _) ->
        match applyProjectWriters owner title writers with
        | ProjectObserved(_, _, _) ->
            // The supported API cannot prove the organization base permission.  Keep the durable
            // human obligation rather than falsely clearing it; the observable visibility/writers
            // receipt is nevertheless emitted for the operator's Manage access verification.
            clearProjectSecurityObligation target project
            AnsiConsole.MarkupLine(sprintf "[yellow]partial verified receipt:[/] %s; requested writers [[%s]]; verify base Read at Project → Settings → Manage access" (Markup.Escape project) (Markup.Escape(String.Join(",", writers))))
            1
        | ProjectPending(_, reason) ->
            AnsiConsole.MarkupLine(sprintf "[yellow]pending:[/] %s — %s" (Markup.Escape project) (Markup.Escape reason))
            1

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    | [ "-h" ] | [ "--help" ] ->
        usage ()
        0
    // No arguments on an interactive terminal → prompt for the parameters. A redirected/CI
    // stdin (not Interactive) falls through to `parse`, which keeps the usage-error exit-2
    // contract — prompting there would only hang or throw.
    | [] when AnsiConsole.Profile.Capabilities.Interactive ->
        match interactive () with
        | Some opts -> run opts
        | None ->
            AnsiConsole.MarkupLine "[yellow]aborted[/] — no scaffold created."
            130
    // `retrofit <target> …` — wire coordination ONTO an existing workspace (the inverse of the
    // scaffold-time step). Its own parser/orchestrator; it does not scaffold, so it skips the wizard
    // and the fsgg-sdd steps entirely.
    | "retrofit" :: rest ->
        match parseRetrofit rest with
        | Ok opts -> runRetrofit opts
        | Error msg ->
            AnsiConsole.MarkupLine(sprintf "[red]error:[/] %s" (Markup.Escape msg))
            AnsiConsole.WriteLine()
            usage ()
            2
    | [ "secure"; "--repo"; repository ] -> runSecure None repository
    | [ "secure"; target; "--repo"; repository ] -> runSecure (Some target) repository
    | [ "secure"; target; "--project"; board; "--public-board"; "--trusted-writers"; writers ] ->
        runSecureProject target board true (writers.Split(',', StringSplitOptions.RemoveEmptyEntries) |> Array.map (fun writer -> writer.Trim()) |> Array.filter (String.IsNullOrWhiteSpace >> not) |> List.ofArray)
    | [ "secure"; target; "--project"; board; "--private-board"; "--trusted-writers"; writers ] ->
        runSecureProject target board false (writers.Split(',', StringSplitOptions.RemoveEmptyEntries) |> Array.map (fun writer -> writer.Trim()) |> Array.filter (String.IsNullOrWhiteSpace >> not) |> List.ofArray)
    | "secure" :: _ ->
        AnsiConsole.MarkupLine "[red]error:[/] secure requires exactly --repo owner/repository"
        2
    | args ->
        match parse args with
        | Ok opts -> run opts
        | Error msg ->
            AnsiConsole.MarkupLine(sprintf "[red]error:[/] %s" (Markup.Escape msg))
            AnsiConsole.WriteLine()
            usage ()
            2
