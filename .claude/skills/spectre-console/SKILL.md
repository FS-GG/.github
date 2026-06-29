---
name: spectre-console
description: Work with Spectre.Console rich-terminal output in this project — the capability/Profile mental model, the widget tour (markup, tables, panels, rules/trees, prompts, live/status), and the FS-GG rendering conventions (the HumanRender/HumanText presentation edge, the Json-is-contract / Plain+Rich-are-projections rule, degrade-to-zero-ANSI, deterministic fixed-width test rendering). Also use when a Spectre.Console render behaves correctly locally but differs or fails in CI (GitHub Actions) — width/wrap assertions, plain / no-color output, or snapshots that go red only on the runner — covering reproducing the divergence locally, classifying invisible-byte artifact vs genuine display overflow, and the matching fix.
metadata:
  source: FS.GG.Governance spec 091 / #32 / #34 / #37 (2026-06-29); evolved to a first-class skill by spec 093 (2026-06-29)
---

# Spectre.Console (FS-GG)

[Spectre.Console](https://spectreconsole.net/) is the rich-terminal library FS-GG uses to render
human-facing CLI output — verdict banners, grouped tables, trees, prompts, and live displays. This
skill carries the working model: **Part A** is a generic Spectre primer (the capability/Profile
mental model + a widget tour), and **Part B** is how FS-GG renders with it (the presentation edge,
the rich/plain/JSON parity rule, degrade-to-zero-ANSI, deterministic test rendering, and the
headless-fidelity pitfall absorbed from spec 091).

It is **advisory** — it gates nothing. Read Part A to do real Spectre work; read Part B before you
add or change a rich-output surface in this repo. For the exhaustive per-widget API, link out to the
[upstream docs](https://spectreconsole.net/) — this skill stays at working depth and does not restate
them.

---

# Part A — Spectre.Console primer

## Capability / Profile mental model

The single idea that explains most Spectre behavior: every console carries a **`Profile`**, and the
Profile decides what actually reaches the terminal. When you call `AnsiConsole.Create settings`,
Spectre builds a Profile by **sensing the host**:

| Profile facet | What it controls | Sensed from |
|---|---|---|
| `Capabilities.Ansi` | whether SGR escape sequences (`ESC[…m`) are emitted at all | terminal + environment (`TERM`, CI vars) |
| `Capabilities.ColorSystem` | color depth: `NoColors`, `Standard` (16), `EightBit` (256), `TrueColor` | terminal detection |
| `Width` (and `Height`) | the column count tables/wrap/rules lay out against | terminal size, else a default |
| `Encoding` | output encoding — drives glyph/box-character selection | the output writer's `Encoding` |
| `Capabilities.Unicode` / `Capabilities.Legacy` | whether wide/box glyphs are used or down-converted | encoding + console kind |

Two facts about this matter constantly:

1. **Each facet can be pinned after `Create`.** `console.Profile.Width <- 80`,
   `console.Profile.Capabilities.Ansi <- false`, `console.Profile.Capabilities.ColorSystem <-
   ColorSystem.NoColors`, etc. Pinning is how you make output a pure function of (content, width)
   instead of a function of the host — essential for deterministic tests (Part B).
2. **`Create` re-detects some facets from the environment even after you set the matching
   `AnsiConsoleSettings`.** Notably ANSI: `settings.Ansi <- AnsiSupport.No` is overridden by host
   detection under CI. This is the root of the headless pitfall in Part B — pin the *capability*
   post-create, not just the setting.

"Rich" output (color, styled banners) exists only when the Profile permits ANSI + a color system.
"Plain" / no-color output is the same content with those capabilities off. Keep that surface
relationship in mind: rich and plain are two projections of one Profile decision, not two code paths.

## Markup & styles

`Markup` is Spectre's inline styling mini-language: square-bracket tags wrap text.

```fsharp
AnsiConsole.Markup "[bold red]error[/]: [yellow]2[/] gates blocked"
AnsiConsole.MarkupLine "[green]ok[/]"      // trailing newline
```

- Tags: color names (`red`, `green3`, `#ff8800`), styles (`bold`, `italic`, `dim`, `underline`),
  background via `on` (`[white on red]`). Close with `[/]`.
- **Escaping is mandatory for untrusted/literal text.** A literal `[` in data will be parsed as a
  tag and either mis-style or throw. Escape with `Markup.Escape(s)` (or double the bracket: `[[`).
  Any string that came from user/file/computed content must be escaped before it enters a markup
  string.
- When ANSI is off in the Profile, markup tags are stripped to plain text — same content, no escapes.

## Tables

`Table` lays columns out against `Profile.Width`, wrapping cell content to fit.

```fsharp
let t = Table()
t.AddColumn "Gate" |> ignore
t.AddColumn(TableColumn("Status").RightAligned()) |> ignore
t.AddRow("build:ship", "[red]blocked[/]") |> ignore
AnsiConsole.Write t
```

- Columns carry width/alignment/padding; `t.Expand` fills the width, `t.Border` picks the box style.
- **Wrap behavior is width-driven**: a cell longer than its column folds onto multiple lines; an
  unbreakable token (no spaces) is broken at the column edge. Because width comes from the Profile,
  the *same* table renders differently at width 80 vs an unknown/auto width — pin width when you need
  determinism.

## Panels

`Panel` draws a bordered box around any renderable, optionally with a header.

```fsharp
let p = Panel("[bold]Verdict[/]: blocked")
p.Header <- PanelHeader "ship"
p.Border <- BoxBorder.Rounded
p.Padding <- Padding(1, 0, 1, 0)
AnsiConsole.Write p
```

Borders/padding/header behave like tables: they consume width, so a panel inside a narrow Profile
wraps its content. Border glyphs depend on `Capabilities.Unicode`/`Legacy` (box-drawing vs ASCII).

## Rules & trees

- **`Rule`** is a horizontal section divider, optionally titled: `AnsiConsole.Write(Rule("[dim]gates[/]"))`.
- **`Tree`** renders hierarchy with connector glyphs:

```fsharp
let root = Tree "report"
let gates = root.AddNode "gates"
gates.AddNode "build:ship — blocked" |> ignore
AnsiConsole.Write root
```

Tree/rule connector glyphs are Unicode by default and down-convert under a legacy/ASCII Profile —
another reason to pin encoding/Unicode capabilities for reproducible output.

## Prompts

Interactive input lives in the prompt family:

```fsharp
let name = AnsiConsole.Ask<string> "Name?"
let ok   = AnsiConsole.Confirm "Proceed?"
let pick = SelectionPrompt<string>().Title("Pick").AddChoices([|"a";"b"|]) |> AnsiConsole.Prompt
```

Prompts **require an interactive (TTY) input**. On a redirected/non-interactive stream they throw or
cannot read — never put a prompt on a code path that runs headless (CI, piped, `--json`). Gate
prompting behind a sensed interactivity check (Part B's capability sensing).

## Live & status

`Live`, `Status`, and `Progress` animate in place by rewriting lines:

```fsharp
AnsiConsole.Status().Start("working…", fun ctx -> doWork ())
```

Their **non-interactive behavior is the thing to remember**: when ANSI/cursor control is unavailable
(redirected output, `TERM=dumb`, CI without ANSI), Spectre degrades these to plain, non-animated
writes rather than emitting cursor-movement garbage — *if* the Profile reflects the lack of ANSI.
That "if" is exactly the headless pitfall: a Profile that wrongly believes it has ANSI will emit
escape sequences into output you meant to be plain.

## Capability profiles (detect / force)

For predictable output, set the Profile explicitly instead of trusting detection:

```fsharp
let settings = AnsiConsoleSettings()
settings.Ansi <- AnsiSupport.No                  // request no ANSI…
settings.ColorSystem <- ColorSystemSupport.NoColors
settings.Out <- AnsiConsoleOutput writer          // encoding comes from the writer
let console = AnsiConsole.Create settings
console.Profile.Width <- 80                        // pin width
console.Profile.Capabilities.Ansi <- false         // …and PIN it (Create re-detects — see Part B)
console.Profile.Capabilities.ColorSystem <- ColorSystem.NoColors
console.Profile.Capabilities.Unicode <- true
console.Profile.Capabilities.Legacy <- false
```

Detection also honors the cross-tool **`NO_COLOR`** convention and `TERM`; CI systems
(`GITHUB_ACTIONS`, `CI`, `TF_BUILD`, …) can force ANSI *on*. Treat detection as a hint and pin the
facets you depend on.

**Exhaustive API** — every widget, every option — lives in the upstream docs:
<https://spectreconsole.net/>. This skill deliberately stops at working depth; link out rather than
restate.

---

# Part B — FS-GG rendering conventions

> The identifiers below (`HumanRender`, `RenderMode`, `ReportView`, `RenderSupport.fs`, …) are this
> repo's real code, cited as **examples** of the convention — not a required shape every repo must
> copy. The conventions (edge-only Spectre, JSON-is-contract, degrade-to-plain, pinned test render)
> are the durable part; the module names are FS.GG.Governance's expression of them.

## Rendering boundary — Spectre lives only at the presentation edge

Spectre.Console is confined to the **HumanRender** layer. No domain, route, gate, or ship module
references Spectre; they produce a presentation-free **`ReportView`**, and only the edge turns it
into terminal output.

- `src/FS.GG.Governance.HumanRender/RichRender.fsi` — `emit : RenderMode -> ReportView -> plain:string
  -> IAnsiConsole -> unit` draws the rich surface; `emitStdout` is the wiring hosts inject as their
  `RenderReport` port "so NO host references Spectre directly".
- `src/FS.GG.Governance.HumanRender/Capability.fsi` — `senseCapability` is the *only* sensing point.
- `src/FS.GG.Governance.HumanRender/Tui.fsi`, `Watch.fsi` — read-only MVU surfaces whose pure
  `update` navigates the same `ReportView`; key input / redraw are effects at the Spectre edge.
- `src/FS.GG.Governance.HumanText/` — the plain-text projection (`HumanText.fsi`), the shared
  view-model (`ReportView.fsi`), and the mode decision (`RenderMode.fsi`).

The rule: **add Spectre code in `HumanRender`, nowhere else.** If a feature needs new rich output,
extend `ReportView` (presentation-free) upstream and render it at the edge.

## Rich / plain / JSON parity

`src/FS.GG.Governance.HumanText/RenderMode.fsi` defines `RenderMode = Json | Plain | Rich` and the
parity contract:

- **`Json` is the byte-identical automation contract and always wins.** `selectMode` returns `Json`
  whenever `--json` is set, whatever the terminal looks like. The host writes the JSON string
  directly; `RichRender.emit` is a no-op for `Json` (present in the match only for totality).
- **`Plain` and `Rich` are non-contractual human projections of the *same* `ReportView`.** Per
  `ReportView.fsi`, the view is "the single, presentation-free structure BOTH the plain-text
  projection (HumanText) and the rich tables / TUI (HumanRender) render, so every human surface stays
  parity-true to the SAME immutable report object." Rich **adds or drops no facts** versus plain or
  JSON — it only adds color and box layout. If rich shows something plain doesn't, that's a parity
  bug, not a feature.
- **`selectMode` is pure and total.** It decides the mode from an explicit-JSON flag plus a
  `ColorCapability` record — it senses nothing itself.

When you add a surface, derive it from `ReportView`; never compute a fact inside `RichRender` that the
plain/JSON projections can't also see.

## Degrade-to-zero-ANSI

Rich is the enhancement; **plain is the floor**, and the floor is genuinely ANSI-free. The decision
lives in `RenderMode.selectMode` over the `ColorCapability` record `{ IsTty; NoColorEnv;
ExplicitPlain; Width }`:

> `explicitJson = true` ⇒ `Json`; else `Rich` iff `IsTty && not NoColorEnv && not ExplicitPlain`;
> else `Plain`.

So rich appears only on an attached terminal with color allowed and no `--plain`; a redirected pipe,
`NO_COLOR`, `TERM=dumb`, or `--plain` all degrade to `Plain`. The sensing that fills the record
(`Capability.senseCapability`: TTY attached? `NO_COLOR` set? `--plain` parsed? width known?) is an
**Effect at the interpreter edge**, never inside the pure `selectMode`. `RichRender.emit` writes the
precomputed `HumanText` plain string **verbatim** for `Plain` — byte-equal, no ANSI.

## Deterministic test rendering

A rich render that depends on the host terminal can't be asserted reproducibly. The pattern in
`tests/FS.GG.Governance.Cli.Tests/RenderSupport.fs` makes layout a **pure function of (content,
width)** by backing an `IAnsiConsole` with a `StringWriter` and pinning every wrap-affecting Profile
facet:

```fsharp
// RenderSupport.fs — plainConsole: an ANSI-free console at a fixed width, host-independent.
let sw = new Utf8StringWriter()              // reports UTF-8 (stock StringWriter reports UTF-16)
let settings = AnsiConsoleSettings()
settings.Ansi <- AnsiSupport.No
settings.ColorSystem <- ColorSystemSupport.NoColors
settings.Out <- AnsiConsoleOutput sw
let console = AnsiConsole.Create settings
console.Profile.Width <- width
console.Profile.Capabilities.Unicode <- true
console.Profile.Capabilities.Legacy  <- false
console.Profile.Capabilities.Ansi <- false                         // pinned post-Create (see pitfall)
console.Profile.Capabilities.ColorSystem <- ColorSystem.NoColors
```

Every facet that influences wrapping — ANSI, color, width, output encoding, Unicode/Legacy — is
pinned, with no host/environment branching. A sibling `colorConsole` does the same with ANSI/color
*on* for the rich-color assertions. The assertion these protect lives in
`tests/FS.GG.Governance.Cli.Tests/WidthResilienceTests.fs`. Reuse this builder for any new
render test; don't hand-roll a console that trusts host detection.

## Headless-fidelity pitfall (absorbed from spec 091)

A rich-render surface that is **correct locally but wrong (or red) in headless CI** is almost always
an *invisible-byte* problem, not a layout problem. Spectre re-detects terminal capabilities on the CI
host, force-enables ANSI, and leaks SGR escape bytes (`ESC[1m…`) into output you believe is plain.
Those bytes are invisible on screen but counted by `String.Length`, so a length-based width/wrap
assertion fails on the runner only.

### When this applies

Reach for this when Spectre.Console output is **right locally but wrong in CI (GitHub Actions)**:

- a width/wrap assertion (`line.Length <= bound`) passes locally, fails only on the runner;
- output you build as "plain" / no-color contains escape sequences on CI;
- a snapshot/golden render diff appears only headless;
- the test is green on every dev machine and red the moment it runs in Actions.

If the render is wrong *everywhere* (local too), this is an ordinary layout bug — not this section.

### The mechanism

Spectre derives a `Profile` (ANSI support, color system, width, encoding, Unicode/Legacy) when you
call `AnsiConsole.Create`. Even when you pass `AnsiConsoleSettings.Ansi <- AnsiSupport.No`, `Create`
**re-detects ANSI from the host environment afterward**. Under `GITHUB_ACTIONS=true` Spectre
force-enables ANSI, so `Markup` writes emit SGR escapes (`ESC[1m`, `ESC[0m`, …) into the output you
intended to be plain. The escapes render as zero visible columns but are real characters in the
string, so on the CI host:

```
cells (what the user sees)  ≠  String.Length (bytes in the buffer)
```

A naive `Expect.isLessThanOrEqual line.Length bound` measures **bytes**, while the folding contract it
means to check is about **display cells**. The invisible escapes inflate `String.Length` past
`bound`, so the assertion misjudges a perfectly-folded line as overflowing — but only where ANSI was
force-enabled, i.e. only on the runner.

### Reproduce locally (no CI round-trip)

The runner's only relevant difference is the `GITHUB_ACTIONS` environment variable, so set it
yourself:

```sh
# Red under the env var with the pre-fix console builder; green without it:
GITHUB_ACTIONS=true dotnet test tests/FS.GG.Governance.Cli.Tests -c Release --filter "WidthResilience"
```

> Paths/filter are this repo's example. Substitute your own render-test project and filter; the
> mechanism — set `GITHUB_ACTIONS=true` and run the render test — is what carries across repos.

Run it once with the variable and once without. If it fails **only** with `GITHUB_ACTIONS=true`, you
have reproduced the headless divergence locally and confirmed it is environment-driven.

### Classify — cells vs bytes (opposite fixes)

Decide which of two opposite problems you have by printing **both** measures per rendered line:

```fsharp
let esc = string (char 0x1B)
for line in out.Replace("\r\n", "\n").Split('\n') do
    // cells = visible width: strip ANSI SGR escapes before measuring
    let visible = System.Text.RegularExpressions.Regex.Replace(line, esc + @"\[[0-9;]*m", "")
    printfn "cells=%3d len=%3d bound=%3d | %s" visible.Length line.Length bound line
```

| Observation | Meaning | Fix |
|---|---|---|
| `cells ≤ bound` **but** `len > bound` | **Invisible-byte artifact** (this incident): the line folds correctly; only leaked escapes inflate the byte count. | Force ANSI/color off on the plain surface (below), or assert on `cells`. |
| `cells > bound` | **Genuine display overflow**: the visible text really is too wide. | A real layout fix (narrower content, different wrap/truncation) — **not** the ANSI fix. |

If `len == cells` everywhere and the line still overflows, there are no leaked escapes — true
overflow. The two diagnoses take **opposite** fixes, so always print both numbers before changing
anything.

### Fix — pin the capability on the plain surface

For the **invisible-byte artifact**, force the capabilities off immediately after
`AnsiConsole.Create`, so re-detection can't re-enable ANSI behind your back:

```fsharp
let console = AnsiConsole.Create settings        // re-detects ANSI from the host here
console.Profile.Width <- width
// Settings.Ansi <- No is not enough: Create re-enables ANSI under GITHUB_ACTIONS. Pin it OFF
// post-create so the output is genuinely ANSI-free on every host.
console.Profile.Capabilities.Ansi <- false
console.Profile.Capabilities.ColorSystem <- ColorSystem.NoColors
```

**Scope this to the deterministic test / plain-console builder only.** It is the right fix for a
surface that is *supposed* to be plain (a no-color degrade path, a width-assertion test, a golden
snapshot). Do **not** apply it to intentional, human-facing product output — degrading a real
terminal's colors to "fix" a test is the wrong trade. Product rendering should stay colored in a real
terminal; only the deterministic surface is pinned.

Alternatively (or additionally), assert against **cells**, not bytes: strip the SGR escapes (as in
Classify) before comparing to `bound`. Pinning the capability is the more robust fix because it keeps
the plain surface genuinely plain for every consumer, not just the one assertion.

### Generalize

`GITHUB_ACTIONS` is one signal among many. Other CI systems force-enable color through their own
variables (`CI`, `TF_BUILD`, `TEAMCITY_VERSION`, `BUILDKITE`, …), and the cross-tool `NO_COLOR`
convention asks programs to suppress color when present. A plain surface that relies on the *absence*
of these signals is fragile; pin the capability rather than trusting the environment. The durable
lesson outlives this variable and library version:

> **Assert against the same measure the system actually uses.** A width/wrap contract is about
> display **cells**; do not check it with **byte length** unless you have guaranteed the two are
> equal. When they can diverge (ANSI, combining marks, wide glyphs, encoding), measure the thing the
> user sees.

## Version scope

Verified on **Spectre.Console 0.57.x** (0.57.1 in FS.GG.Governance, 0.57.0 in FS.GG.SDD) on the
GitHub Actions runner. Treat the version-dependent claims here — especially the re-detection behavior
of `AnsiConsole.Create` (a library implementation detail, **not** a documented guarantee) and the
specific Profile facet names — as **version-scoped**. The durable conventions (edge-only Spectre,
JSON-is-contract, degrade-to-plain, pinned deterministic render) outlive any version. On a different
Spectre.Console version, re-run *Reproduce locally* before assuming the same fix applies.

## Provenance

Every Part B claim traces to this repo's live code or a verified incident — nothing is from memory:

- **Code pointers**: `RenderMode.fsi` (`RenderMode`, `ColorCapability`, `selectMode`),
  `ReportView.fsi` (the shared view-model), `RichRender.fsi` (`emit`/`emitStdout`), `Capability.fsi`
  (`senseCapability`), `RenderSupport.fs` (`plainConsole`/`colorConsole`), `WidthResilienceTests.fs`.
- **Spec**: FS.GG.Governance `091` (headless render determinism).
- **Issues**: `#32` (the test-fidelity gap), `#34` (the publish it blocked), `#37` (the fix).
- **Evidence runs**: `28376202121` (the diagnostic cell-vs-byte dump that identified leaked escapes)
  and `28377734248` (the green `FS.GG.Governance.Cli@1.2.0` publish after the fix).
- **Date stamped**: 2026-06-29.
- **Corrected root cause**: `AnsiSupport.No` overridden under `GITHUB_ACTIONS` (leaked SGR bytes),
  **not** glyph/width measurement — an earlier hypothesis the diagnostic dump disproved.

---

## Distribution

This skill is **advisory** — it gates nothing. No build/test/publish/merge workflow references it;
deleting it changes no CI outcome.

- **Canonical source**: `FS-GG/.github` → `.claude/skills/spectre-console/SKILL.md`.
- **Installed (Spectre-using repos)**: FS.GG.Governance (0.57.1), FS.GG.SDD (0.57.0). Each carries a
  byte-identical copy plus an `AGENTS.md` entrypoint that *references* this body (no second copy).
- **Excluded**: FS.GG.Rendering and FS.GG.Templates — they do not render with Spectre.Console, so the
  skill does not apply.

To install in another Spectre-using repo, copy this file to that repo's
`.claude/skills/spectre-console/SKILL.md` and add the `AGENTS.md` entrypoint marker. This skill
evolved from the narrower `spectre-console-headless-fidelity` skill (spec 092) into a first-class
`spectre-console` skill by spec 093; the headless-fidelity diagnostic is preserved verbatim-in-
substance as the *Headless-fidelity pitfall* section of Part B.
