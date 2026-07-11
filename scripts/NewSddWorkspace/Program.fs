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
///   5. fsgg-sdd doctor          (read-only coherence check)                                  [non-fatal]
///   6. fsgg-sdd upgrade         (optional --upgrade — reconcile an existing project)         [fatal]
///
/// Currency is the DEFAULT (ADR-0030, the creation-time carve-out to ADR-0009): the CLI
/// self-updates to the newest coherent set BEFORE scaffolding, so a fresh workspace is always
/// produced by current tooling. --pinned skips the self-update; --pinned with --ref <tag> gives a
/// fully reproducible, pinned scaffold. ADR-0009's "no silent auto-update" still governs the
/// in-project fsgg-sdd verbs — this default is only the create-a-new-workspace step.
module NewSddWorkspace.Program

open System
open System.IO
open System.Diagnostics
open System.Net.Http
open System.Runtime.InteropServices
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
      Profile: string option }

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

/// Self-update the `fsgg-sdd` global tool to the newest published build BEFORE scaffolding, so a
/// fresh workspace is produced by the current coherent set's tooling — the DEFAULT (ADR-0030, the
/// creation-time carve-out to ADR-0009: there is no existing consumer artifact to clobber, and
/// newest-by-default is the whole point of creating a workspace; `--pinned` + `--ref <tag>`
/// restores a reproducible pin). `FS.GG.SDD.Cli` lives only on the org GitHub Packages feed, whose
/// reads are all authenticated (FS.GG.Templates#82), so the update needs a `read:packages` token;
/// with none the feed is unreachable and the step skips (scaffolding proceeds with the installed
/// CLI). Best-effort throughout: an offline or failed update warns and never blocks creation. Runs
/// from an isolated temp config so the org feed's anonymous 401 can't poison the restore.
let private selfUpdateCli () : Outcome =
    match feedToken () with
    | None -> Skipped "no read:packages token (set FSGG_PACKAGES_TOKEN) — scaffolding with the installed CLI"
    | Some token ->
        let dir = Path.Combine(Path.GetTempPath(), "new-sdd-workspace-upd-" + Guid.NewGuid().ToString "N")
        Directory.CreateDirectory dir |> ignore
        try
            let cfg = Path.Combine(dir, "nuget.config")
            File.WriteAllText(cfg, orgFeedConfig token)
            let code, _ =
                runProcess true "dotnet" [ "tool"; "update"; "--global"; "FS.GG.SDD.Cli"; "--configfile"; cfg ]
            if code = 0 then Succeeded
            else Warned(sprintf "update failed (exit %d) — scaffolding with the installed CLI" code)
        finally
            try Directory.Delete(dir, true) with _ -> ()

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
    AnsiConsole.MarkupLine "  [dim](from a checkout: dotnet run --project scripts/NewSddWorkspace -- <target-dir> <product-name>)[/]"
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine "[bold]Options[/]"
    AnsiConsole.MarkupLine "  [green]--profile[/] <name>   render profile (default: game = provider default)"
    AnsiConsole.MarkupLine(sprintf "                    [dim]%s[/]" (String.Join(", ", profiles |> List.map fst)))
    AnsiConsole.MarkupLine "  [green]--ref[/] <git-ref>    FS.GG.Templates ref for the descriptor (default: main = newest)"
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
      Upgrade: bool option }

let private emptyDraft =
    { Product = None; Target = None; Profile = None; Governance = None; Ref = None; Pinned = None; Upgrade = None }

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
              Profile = Some profile }
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
              Profile = None }
            rest
    | _ -> Error "target dir and product name are required"

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

        // 5 · fsgg-sdd doctor (read-only, non-fatal — matches the shell's `|| true`)
        if not fatal then
            step 5 "fsgg-sdd doctor"
            let code, _ = runProcess true "fsgg-sdd" [ "doctor"; "--root"; opts.Target ]
            if code = 0 then
                AnsiConsole.MarkupLine "  [green]✓[/] product is coherent with its set"
                results.Add { Title = "doctor"; Outcome = Succeeded }
            else
                AnsiConsole.MarkupLine(sprintf "  [yellow]⚠[/] doctor reported issues (exit %d) — non-blocking" code)
                results.Add { Title = "doctor"; Outcome = Warned(sprintf "reported issues (exit %d)" code) }

        // 6 · fsgg-sdd upgrade (optional; fatal on failure, matching the shell's set -e). With the
        //     default pre-scaffold self-update this is largely redundant on a fresh scaffold; it
        //     stays for the explicit "reconcile an existing project" invocation.
        if opts.Upgrade && not fatal then
            step 6 "fsgg-sdd upgrade"
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
    | args ->
        match parse args with
        | Ok opts -> run opts
        | Error msg ->
            AnsiConsole.MarkupLine(sprintf "[red]error:[/] %s" (Markup.Escape msg))
            AnsiConsole.WriteLine()
            usage ()
            2
