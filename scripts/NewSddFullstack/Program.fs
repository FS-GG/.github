/// new-sdd-fullstack — scaffold a full-stack FS.GG product (SDD lifecycle + runnable Rendering
/// app + Governance overlay) using only existing, published machinery, with NO FS.GG.Templates
/// checkout required. This is the sole full-stack scaffolder — the F# successor to the retired
/// scripts/new-sdd-fullstack.sh (ADR-0016).
///
/// It orchestrates the commands that already exist today:
///   1. fetch the newest rendering provider descriptor from FS.GG.Templates (HTTP, no clone)
///   2. fsgg-sdd scaffold        (SDD lifecycle skeleton + runnable Rendering app)   [fatal]
///   3. governance overlay       (dotnet new fs-gg-governance — default on)          [non-fatal]
///   4. fsgg-sdd doctor          (read-only coherence check)                          [non-fatal]
///   5. fsgg-sdd upgrade         (optional --upgrade; ADR-0009 never automatic)       [fatal]
///
/// Currency stays EXPLICIT (ADR-0009): fetching `main` gives the current coherent set; pass
/// --ref <tag> to pin a reproducible version; run --upgrade to reconcile a behind project.
module NewSddFullstack.Program

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
      Governance: bool }

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
/// (FS-GG/FS.GG.Templates#82), so a bare `dotnet new install FS.GG.Templates` is anonymous
/// and fails with exit 103 — that is why the governance overlay used to always skip.
let private orgFeed = "https://nuget.pkg.github.com/FS-GG/index.json"

/// A feed read token from the environment, if any — a dedicated var first, then the ones CI
/// and the `gh` CLI already export. `read:packages` scope is enough (the package is public).
let private feedToken () =
    [ "FSGG_PACKAGES_TOKEN"; "GH_TOKEN"; "GITHUB_TOKEN" ]
    |> List.tryPick (fun name ->
        match Environment.GetEnvironmentVariable name with
        | null | "" -> None
        | v -> Some v)

/// Install the FS.GG.Templates template package (which carries the `fs-gg-governance` template).
/// If a feed token is present, run the install from a temp dir carrying a credentialed
/// nuget.config (`dotnet new install` has no --configfile; it discovers config from CWD upward),
/// then delete it. With no token, fall back to the ambient/global config — which works when the
/// caller already has an authenticated org-feed source (the common consumer case).
let private installGovernanceTemplate () : int * string =
    match feedToken () with
    | None -> runProcess false "dotnet" [ "new"; "install"; "FS.GG.Templates" ]
    | Some token ->
        let dir = Path.Combine(Path.GetTempPath(), "new-sdd-fullstack-" + Guid.NewGuid().ToString "N")
        Directory.CreateDirectory dir |> ignore
        try
            let cfg =
                String.concat "\n"
                    [ "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                      "<configuration>"
                      "  <packageSources>"
                      "    <clear />"
                      sprintf "    <add key=\"fs-gg-github\" value=\"%s\" />" orgFeed
                      "    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />"
                      "  </packageSources>"
                      "  <packageSourceCredentials>"
                      "    <fs-gg-github>"
                      "      <add key=\"Username\" value=\"fs-gg\" />"
                      sprintf "      <add key=\"ClearTextPassword\" value=\"%s\" />" (System.Security.SecurityElement.Escape token)
                      "    </fs-gg-github>"
                      "  </packageSourceCredentials>"
                      "</configuration>" ]
            File.WriteAllText(Path.Combine(dir, "nuget.config"), cfg)
            runProcessIn (Some dir) false "dotnet" [ "new"; "install"; "FS.GG.Templates" ]
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
    grid.AddRow("[grey]descriptor ref[/]", Markup.Escape opts.Ref) |> ignore
    grid.AddRow(
        "[grey]governance[/]",
        (if opts.Governance then "light / non-blocking" else "[dim]disabled (--no-governance)[/]")
    )
    |> ignore
    if opts.Upgrade then
        grid.AddRow("[grey]upgrade[/]", "reconcile if behind") |> ignore
    let panel = Panel(grid)
    panel.Header <- PanelHeader "[bold]new-sdd-fullstack[/]"
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
        AnsiConsole.MarkupLine(sprintf "[bold]Done:[/] full-stack product in [green]%s[/]" (Markup.Escape opts.Target))
        AnsiConsole.MarkupLine(
            sprintf
                "[bold]Next:[/] cd %s && dotnet build && dotnet run   [grey]# then: fsgg-sdd charter[/]"
                (Markup.Escape opts.Target)
        )

let private usage () =
    AnsiConsole.MarkupLine
        "[bold]new-sdd-fullstack[/] — scaffold a full-stack FS.GG product (SDD + Rendering + Governance)"
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine "[bold]Usage[/]"
    AnsiConsole.MarkupLine "  new-sdd-fullstack [grey]<target-dir> <product-name>[/] [[options]]"
    AnsiConsole.MarkupLine "  [dim](from a checkout: dotnet run --project scripts/NewSddFullstack -- <target-dir> <product-name>)[/]"
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine "[bold]Options[/]"
    AnsiConsole.MarkupLine "  [green]--ref[/] <git-ref>    FS.GG.Templates ref for the descriptor (default: main = newest)"
    AnsiConsole.MarkupLine "  [green]--upgrade[/]          also run `fsgg-sdd upgrade` after scaffolding (reconcile if behind)"
    AnsiConsole.MarkupLine "  [green]--no-governance[/]    skip the governance overlay"

// ── Arg parsing ──────────────────────────────────────────────────────────────

let private parse (argv: string list) : Result<Options, string> =
    let rec flags acc rest =
        match rest with
        | [] -> Ok acc
        | "--ref" :: value :: t -> flags { acc with Ref = value } t
        | [ "--ref" ] -> Error "--ref needs a value"
        | "--upgrade" :: t -> flags { acc with Upgrade = true } t
        | "--no-governance" :: t -> flags { acc with Governance = false } t
        | other :: _ -> Error(sprintf "unknown argument: %s" other)
    match argv with
    | target :: product :: rest when not (target.StartsWith "--") && not (product.StartsWith "--") ->
        flags
            { Target = target
              Product = product
              Ref = "main"
              Upgrade = false
              Governance = true }
            rest
    | _ -> Error "target dir and product name are required"

// ── Orchestration ────────────────────────────────────────────────────────────

let private run (opts: Options) : int =
    header opts

    if not (onPath "fsgg-sdd") then
        // Preflight (matches the shell's exit 127): steps 2/4/5 drive `fsgg-sdd`, so fail fast
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

        // 2 · fsgg-sdd scaffold (fatal on failure)
        if not fatal then
            step 2 "fsgg-sdd scaffold"
            let code, _ =
                runProcess true "fsgg-sdd"
                    [ "scaffold"; "--root"; opts.Target; "--provider"; "rendering"; "--param"; sprintf "productName=%s" opts.Product ]
            if code = 0 then
                AnsiConsole.MarkupLine "  [green]✓[/] SDD skeleton + runnable Rendering app scaffolded"
                results.Add { Title = "scaffold"; Outcome = Succeeded }
            else
                AnsiConsole.MarkupLine(sprintf "  [red]✗[/] scaffold failed (exit %d)" code)
                results.Add { Title = "scaffold"; Outcome = Failed(sprintf "exit %d" code) }
                fatal <- true

        // 3 · governance overlay (non-fatal; best-effort — needs the published template on a reachable feed)
        if not opts.Governance then
            results.Add { Title = "governance overlay"; Outcome = Skipped "--no-governance" }
        elif not fatal then
            step 3 "governance overlay"
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
                    | Some _ -> "could not install the FS.GG.Templates template from the org feed"
                    | None ->
                        "could not install the FS.GG.Templates template — the org feed authenticates every read, "
                        + "so set FSGG_PACKAGES_TOKEN (or GH_TOKEN) to a read:packages token, or configure an "
                        + "authenticated nuget.pkg.github.com/FS-GG source"
                AnsiConsole.MarkupLine(sprintf "  [yellow]⊘[/] %s — skipped" reason)
                installLog.Replace("\r\n", "\n").Split('\n')
                |> Array.iter (fun l -> if not (String.IsNullOrWhiteSpace l) then dim l)
                AnsiConsole.MarkupLine(
                    sprintf
                        "  add later: [grey]dotnet new install FS.GG.Templates && dotnet new fs-gg-governance -o %s --appName %s[/]"
                        (Markup.Escape opts.Target)
                        (Markup.Escape opts.Product)
                )
                results.Add { Title = "governance overlay"; Outcome = Skipped "FS.GG.Templates template feed not reachable" }

        // 4 · fsgg-sdd doctor (read-only, non-fatal — matches the shell's `|| true`)
        if not fatal then
            step 4 "fsgg-sdd doctor"
            let code, _ = runProcess true "fsgg-sdd" [ "doctor"; "--root"; opts.Target ]
            if code = 0 then
                AnsiConsole.MarkupLine "  [green]✓[/] product is coherent with its set"
                results.Add { Title = "doctor"; Outcome = Succeeded }
            else
                AnsiConsole.MarkupLine(sprintf "  [yellow]⚠[/] doctor reported issues (exit %d) — non-blocking" code)
                results.Add { Title = "doctor"; Outcome = Warned(sprintf "reported issues (exit %d)" code) }

        // 5 · fsgg-sdd upgrade (optional; fatal on failure, matching the shell's set -e)
        if opts.Upgrade && not fatal then
            step 5 "fsgg-sdd upgrade"
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
    match parse (List.ofArray argv) with
    | Ok opts -> run opts
    | Error msg ->
        AnsiConsole.MarkupLine(sprintf "[red]error:[/] %s" (Markup.Escape msg))
        AnsiConsole.WriteLine()
        usage ()
        2
