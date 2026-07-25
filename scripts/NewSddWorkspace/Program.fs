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
      /// The per-repo chore-lock roster for a NON-FS-GG board (`FSGG_COORD_CHORE_LOCKS`). None for the
      /// FS-GG board, which uses the engine's embedded table. Set with `--chore-locks owner/repo#n,…`.
      ChoreLocks: string option }

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
let private fetchDescriptor (gitRef: string) (dest: string) : Result<string option, string> =
    try
        let url =
            sprintf "https://raw.githubusercontent.com/FS-GG/FS.GG.Templates/%s/providers/rendering.providers.yml" gitRef
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

/// The four coordination skills (the `coordination-kit` rows of `registry/repos.yml`). Each lands in
/// BOTH agent-skill roots byte-identical (ADR-0011/0014); the shim and the engine tool manifest
/// complete the kit. Fetched from FS-GG/.github at scaffold time — the packaged tool has no checkout,
/// so it pulls the bytes over HTTP exactly as it fetches the rendering descriptor (no `coordination-sync`).
let private coordinationSkills =
    [ "cross-repo-coordination"; "intra-repo-parallel-work"; "check-board"; "pnext-item" ]

/// `owner/title` → (owner, title). No `/` ⇒ that owner's default `Coordination` board.
let private parseBoard (value: string) : string * string =
    match value.IndexOf '/' with
    | -1 -> value, "Coordination"
    | i -> value.Substring(0, i), value.Substring(i + 1)

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
    // 1 · the four skills → every agent-skill root, byte-identical. The SDD scaffold fans its own
    //     union into .claude/.agents/.codex (ADR-0011), so the coordination kit joins all three —
    //     a codex-driven agent in the workspace sees the same skills a Claude one does.
    for s in coordinationSkills do
        match fetchText (raw (sprintf ".claude/skills/%s/SKILL.md" s)) with
        | Ok content ->
            for root in [ ".claude/skills"; ".agents/skills"; ".codex/skills" ] do
                writeUnder opts.Target (sprintf "%s/%s/SKILL.md" root s) content
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
    match existing with
    | Some cur when cur = desired -> Kept rel
    | _ ->
        try
            writeUnder target rel desired
            if makeExec then (try setExecutable dest with _ -> ())
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
    let record =
        function
        | Wrote(rel, m) -> wrote.Add(rel, m)
        | Kept rel -> kept.Add rel
        | Errored d -> problems.Add d
    // 1 · the four skills → every agent-skill root, byte-identical (reconciled per root file).
    for s in coordinationSkills do
        match fetchText (raw (sprintf ".claude/skills/%s/SKILL.md" s)) with
        | Ok content ->
            for root in [ ".claude/skills"; ".agents/skills"; ".codex/skills" ] do
                record (reconcileFile opts.Target (sprintf "%s/%s/SKILL.md" root s) content false)
        | Error e -> problems.Add e
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
    grid.AddRow(
        "[grey]profile[/]",
        (opts.Profile |> Option.map Markup.Escape |> Option.defaultValue "[dim]game (provider default)[/]")
    )
    |> ignore
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
    AnsiConsole.MarkupLine "  [dim](from a checkout: dotnet run --project scripts/NewSddWorkspace -- <target-dir> <product-name>)[/]"
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine "[bold]Subcommands[/]"
    AnsiConsole.MarkupLine "  [aqua]retrofit[/] <target-dir>   idempotently wire coordination ONTO an existing workspace (the"
    AnsiConsole.MarkupLine "                        inverse of the scaffold-time step): vendor the kit + write the board"
    AnsiConsole.MarkupLine "                        env, re-emit only what drifted, and record it in scaffold-provenance.json"
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine "[bold]Options[/]"
    AnsiConsole.MarkupLine "  [green]--profile[/] <name>   render profile (default: game = provider default)"
    AnsiConsole.MarkupLine(sprintf "                    [dim]%s[/]" (String.Join(", ", profiles |> List.map fst)))
    AnsiConsole.MarkupLine "  [green]--ref[/] <git-ref>    FS.GG.Templates ref for the descriptor (default: main = newest)"
    AnsiConsole.MarkupLine "  [green]--board[/] <owner/title>  coordination board to wire the workspace to (default: FS-GG/Coordination)"
    AnsiConsole.MarkupLine "  [green]--repo[/] <owner/repo>    this workspace's own repo (its board identity + chore-lock basis)"
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
      Profile: string option
      Governance: bool option
      Ref: string option
      Pinned: bool option
      Upgrade: bool option
      Coordinate: bool option
      WorkspaceRepo: string option
      BoardOwner: string option
      BoardTitle: string option
      ChoreLocks: string option }

let private emptyDraft =
    { Product = None
      Target = None
      Profile = None
      Governance = None
      Ref = None
      Pinned = None
      Upgrade = None
      Coordinate = None
      WorkspaceRepo = None
      BoardOwner = None
      BoardTitle = None
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
    row "profile" (d.Profile |> Option.map (fun p -> sprintf "[magenta]%s[/]" (Markup.Escape p)) |> Option.defaultValue pendingCell)
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
    row "upgrade"
        (match d.Upgrade with
         | Some true -> "[green]yes[/] [grey](reconcile)[/]"
         | Some false -> "[grey]no[/]"
         | None -> pendingCell)
    row "coordination"
        (match d.Coordinate with
         | Some true ->
             let board =
                 sprintf "board [aqua]%s/%s[/]"
                     (Markup.Escape(d.BoardOwner |> Option.defaultValue "FS-GG"))
                     (Markup.Escape(d.BoardTitle |> Option.defaultValue "Coordination"))
             match d.WorkspaceRepo with
             | Some r -> sprintf "%s [grey]· repo[/] [green]%s[/]" board (Markup.Escape r)
             | None -> board
         | Some false -> "[grey]none (skipped)[/]"
         | None -> pendingCell)
    let panel = Panel(grid)
    panel.Header <- PanelHeader "[bold]parameters[/]"
    panel.Border <- BoxBorder.Rounded
    panel.Padding <- Padding(1, 0, 1, 0)
    panel

/// Right card: a tree of what the run will produce, growing as the answers land. Structural
/// nodes are always present (a workspace always has them); their annotations and the
/// optional leaves (game-core, governance, upgrade) concretise as the draft fills in.
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
    tree.AddNode(sprintf "[grey].fsgg/providers.yml[/]  rendering descriptor %s" refAnno) |> ignore

    let prodAnno =
        d.Product |> Option.map (fun p -> sprintf "[grey](productName=[/][green]%s[/][grey])[/]" (Markup.Escape p)) |> Option.defaultValue pendingCell
    let sdd = tree.AddNode(sprintf "SDD lifecycle skeleton  %s" prodAnno)
    sdd.AddNode "[grey37]charter · spec · plan · tasks[/]" |> ignore

    let profileAnno =
        d.Profile |> Option.map (fun p -> sprintf "[grey](fs-gg-ui · profile [/][magenta]%s[/][grey])[/]" (Markup.Escape p)) |> Option.defaultValue pendingCell
    let app = tree.AddNode(sprintf "runnable Rendering app  %s" profileAnno)
    app.AddNode "[grey37]dotnet build && dotnet run[/]" |> ignore
    // The simulation core materializes only for the game-family profiles (game, sample-pack).
    match d.Profile with
    | Some p when hasGameCore p -> app.AddNode "[grey37]+ fs-gg-game-core (fixed-step · seeded RNG · AABB)[/]" |> ignore
    | _ -> ()
    // The standalone FS.GG.Audio component ships on the same simulation profiles (own repo/axis;
    // the real host-side realization behind the pure AudioEffect edge). See ADR-0024.
    match d.Profile with
    | Some p when hasAudio p -> app.AddNode "[grey37]+ fs-gg-audio (buses · fades/ducking · 3D · device backend)[/]" |> ignore
    | _ -> ()

    (match d.Governance with
     | Some true -> tree.AddNode "[green]governance overlay[/]  [grey](profile: light)[/]"
     | Some false -> tree.AddNode "[grey37]governance overlay — skipped[/]"
     | None -> tree.AddNode(sprintf "governance overlay  %s" pendingCell))
    |> ignore

    (match d.Coordinate with
     | Some true ->
         let board =
             sprintf "%s/%s"
                 (d.BoardOwner |> Option.defaultValue "FS-GG")
                 (d.BoardTitle |> Option.defaultValue "Coordination")
         let node = tree.AddNode(sprintf "[green]coordination kit[/]  [grey](board [/][aqua]%s[/][grey])[/]" (Markup.Escape board))
         node.AddNode "[grey37].claude+.agents/skills · scripts/fsgg-coord · .config/dotnet-tools.json · .claude/settings.json env[/]" |> ignore
         node
     | Some false -> tree.AddNode "[grey37]coordination — skipped[/]"
     | None -> tree.AddNode(sprintf "coordination  %s" pendingCell))
    |> ignore

    match d.Upgrade with
    | Some true -> tree.AddNode "[green]fsgg-sdd upgrade[/]  [grey](reconcile to the coherent set)[/]" |> ignore
    | _ -> ()

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
    (match d.Profile with Some p when p <> "game" -> parts.Add(sprintf "--profile %s" p) | _ -> ())
    (match d.Ref with Some r when r <> "main" -> parts.Add(sprintf "--ref %s" r) | _ -> ())
    (match d.Coordinate with
     | Some false -> parts.Add "--no-coordination"
     | Some true ->
         let owner = d.BoardOwner |> Option.defaultValue "FS-GG"
         let title = d.BoardTitle |> Option.defaultValue "Coordination"
         if owner <> "FS-GG" || title <> "Coordination" then parts.Add(sprintf "--board %s/%s" owner title)
         (match d.WorkspaceRepo with
          | Some r when r <> sprintf "FS-GG/%s" (d.Product |> Option.defaultValue "") -> parts.Add(sprintf "--repo %s" r)
          | _ -> ())
         (match d.ChoreLocks with Some cl -> parts.Add(sprintf "--chore-locks %s" cl) | None -> ())
     | None -> ())
    (match d.Pinned with Some true -> parts.Add "--pinned" | _ -> ())
    (match d.Governance with Some false -> parts.Add "--no-governance" | _ -> ())
    (match d.Upgrade with Some true -> parts.Add "--upgrade" | _ -> ())
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
/// The surface mirrors the CLI exactly: product + target (text), profile + governance + ref
/// (selection), upgrade (confirm). A live preview grows beside the prompts. A non-interactive
/// stdin never reaches here (see `main`), so the piped/CI usage-error contract is unchanged.
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
    let profile =
        AnsiConsole.Prompt(
            SelectionPrompt<string>()
                .Title("Render [magenta]profile[/]?")
                .AddChoices(profiles |> List.map (fun (id, gloss) -> sprintf "%s — %s" id gloss) |> List.toArray))
            .Split(' ').[0]
    draft <- { draft with Profile = Some profile }

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
    let upgrade =
        AnsiConsole.Confirm("Also run [green]fsgg-sdd upgrade[/] after scaffolding (reconcile if behind)?", false)
    draft <- { draft with Upgrade = Some upgrade }

    draftView draft
    // Coordination is an explicit sequence — org, board, this workspace's repo, chore-locks — each a
    // step with FS-GG defaults, so the common case is still Enter-through but a product org is never
    // buried behind a sub-choice. The repo's owner defaults the board org (a prompt default, no magic).
    let coordinate, workspaceRepo, boardOwner, boardTitle, choreLocks =
        if not (AnsiConsole.Confirm("Wire this workspace to a [green]coordination board[/]?", true)) then
            false, sprintf "FS-GG/%s" product, "FS-GG", "Coordination", None
        else
            let repo =
                AnsiConsole.Prompt(
                    TextPrompt<string>("  This workspace's [green]repo[/] [grey](owner/repo)[/]?")
                        .DefaultValue(sprintf "FS-GG/%s" product)
                        .Validate(fun (s: string) -> required "repo" s)).Trim()
            let owner =
                AnsiConsole.Prompt(
                    TextPrompt<string>("  Coordination board [green]org[/] [grey](owner)[/]?")
                        .DefaultValue(fst (parseBoard repo))
                        .Validate(fun (s: string) -> required "org" s)).Trim()
            let title =
                AnsiConsole
                    .Prompt(TextPrompt<string>("  Board [green]title[/]?").DefaultValue("Coordination"))
                    .Trim()
            let cl =
                let raw =
                    AnsiConsole
                        .Prompt(
                            TextPrompt<string>(
                                "  [green]Chore-locks[/] [grey](owner/repo#n,… — non-FS-GG boards; blank to skip)[/]?"
                            )
                                .AllowEmpty())
                        .Trim()
                if String.IsNullOrWhiteSpace raw then None else Some raw
            true, repo, owner, title, cl
    draft <-
        { draft with
            Coordinate = Some coordinate
            WorkspaceRepo = Some workspaceRepo
            BoardOwner = Some boardOwner
            BoardTitle = Some boardTitle
            ChoreLocks = choreLocks }

    // Final full preview, then a go/no-go before anything touches disk.
    draftView draft
    if AnsiConsole.Confirm("[bold]Create this scaffold now?[/]", true) then
        Some
            { Options.Target = target
              Product = product
              Ref = gitRef
              Upgrade = upgrade
              Governance = governance
              Pinned = pinned
              Profile = Some profile
              Coordinate = coordinate
              WorkspaceRepo = Some workspaceRepo
              BoardOwner = boardOwner
              BoardTitle = boardTitle
              ChoreLocks = choreLocks }
    else
        None

// ── Arg parsing ──────────────────────────────────────────────────────────────

let private parse (argv: string list) : Result<Options, string> =
    let knownProfiles = profiles |> List.map fst
    // A `--flag`-looking token is a missing value, not a value — the same guard repos.sh's
    // `need_val` applies. Without it, `new-sdd-workspace ./x P --profile --ref v1` swallows
    // `--ref` as the profile and then blames `v1` (`unknown argument: v1`) for a mistake made
    // two args earlier. `--profile` is also validated here against the known set, so an invalid
    // profile is caught on the CLI path — not late, inside the `fsgg-sdd scaffold` child — to
    // match the interactive wizard, which already constrains it to `profiles`.
    let rec flags (acc: Options) rest =
        match rest with
        | [] -> Ok acc
        | "--profile" :: value :: _ when value.StartsWith "--" ->
            Error(sprintf "--profile needs a value (got flag '%s')" value)
        | "--profile" :: value :: t ->
            if List.contains value knownProfiles then
                flags { acc with Profile = Some value } t
            else
                Error(sprintf "unknown profile '%s' (choose one of: %s)" value (String.Join(", ", knownProfiles)))
        | [ "--profile" ] -> Error "--profile needs a value"
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
              Ref = "main"
              Upgrade = false
              Governance = true
              Pinned = false
              Profile = None
              Coordinate = true
              WorkspaceRepo = None
              BoardOwner = "FS-GG"
              BoardTitle = "Coordination"
              ChoreLocks = None }
            rest
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
                    sprintf "fetching descriptor from FS.GG.Templates@%s…" opts.Ref,
                    fun _ -> fetchDescriptor opts.Ref descriptorPath
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
            // Pin the profile only when chosen; None defers to the provider default (game).
            let profileParam =
                match opts.Profile with
                | Some p -> [ "--param"; sprintf "profile=%s" p ]
                | None -> []
            let code, _ =
                runProcess true "fsgg-sdd"
                    ([ "scaffold"; "--root"; opts.Target; "--provider"; "rendering" ]
                     @ profileParam
                     @ [ "--param"; sprintf "productName=%s" opts.Product ])
            if code = 0 then
                AnsiConsole.MarkupLine "  [green]✓[/] SDD skeleton + runnable Rendering app scaffolded"
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
    | args ->
        match parse args with
        | Ok opts -> run opts
        | Error msg ->
            AnsiConsole.MarkupLine(sprintf "[red]error:[/] %s" (Markup.Escape msg))
            AnsiConsole.WriteLine()
            usage ()
            2
