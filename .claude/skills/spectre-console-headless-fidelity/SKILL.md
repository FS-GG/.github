---
name: spectre-console-headless-fidelity
description: Diagnose a Spectre.Console render that behaves correctly locally but differs or fails in CI (GitHub Actions) — width/wrap assertions, plain / no-color output, or snapshots that go red only on the runner. Use when a rich-render test passes on your machine but fails headless, or when "plain" output leaks ANSI escapes on CI. Covers reproducing the divergence locally, classifying invisible-byte artifact vs genuine display overflow, and the matching fix.
metadata:
  source: FS.GG.Governance spec 091 / #32 / #34 / #37 (2026-06-29)
---

# Spectre.Console headless fidelity

A rich-render surface that is **correct locally but wrong (or red) in headless CI** is almost
always an *invisible-byte* problem, not a layout problem. Spectre re-detects terminal
capabilities on the CI host, force-enables ANSI, and leaks SGR escape bytes (`ESC[1m…`) into
output you believe is plain. Those bytes are invisible on screen but counted by `String.Length`,
so a length-based width/wrap assertion fails on the runner only.

This skill teaches you to reproduce that divergence **without a CI round-trip**, tell it apart
from a real display overflow (which has the *opposite* fix), and apply the correct fix scoped to
the test/plain surface.

## Symptom / When to use

Reach for this when Spectre.Console output is **right locally but wrong in CI (GitHub Actions)**:

- a width/wrap assertion (`line.Length <= bound`) passes locally, fails only on the runner;
- output you build as "plain" / no-color contains escape sequences on CI;
- a snapshot/golden render diff appears only headless;
- the test is green on every dev machine and red the moment it runs in Actions.

If the render is wrong *everywhere* (local too), this is an ordinary layout bug — not this skill.

## The problem

Spectre derives a `Profile` (ANSI support, color system, width, encoding, Unicode/Legacy) when
you call `AnsiConsole.Create`. Even when you pass `AnsiConsoleSettings.Ansi <- AnsiSupport.No`,
`Create` **re-detects ANSI from the host environment afterward**. Under `GITHUB_ACTIONS=true`
Spectre force-enables ANSI, so `Markup` writes emit SGR escapes (`ESC[1m`, `ESC[0m`, …) into the
output you intended to be plain.

The escapes render as zero visible columns but are real characters in the string. So on the CI
host:

```
cells (what the user sees)  ≠  String.Length (bytes in the buffer)
```

A naive assertion like `Expect.isLessThanOrEqual line.Length bound` measures **bytes**, while the
folding contract it means to check is about **display cells**. The invisible escapes inflate
`String.Length` past `bound`, so the assertion misjudges a perfectly-folded line as overflowing —
but only where ANSI was force-enabled, i.e. only on the runner.

## Reproduce locally

You do **not** need to push to CI. The runner's only relevant difference is the `GITHUB_ACTIONS`
environment variable, so set it yourself:

```sh
# Reproduces the headless divergence on a dev machine (red under the env var with the pre-fix
# console builder; green without it):
GITHUB_ACTIONS=true dotnet test tests/FS.GG.Governance.Cli.Tests -c Release --filter "WidthResilience"
```

> Paths/filter are this repo's example. Substitute your own render-test project and filter; the
> mechanism — set `GITHUB_ACTIONS=true` and run the render test — is what carries across repos.

Run it once with the variable and once without. If it fails **only** with `GITHUB_ACTIONS=true`,
you have reproduced the headless divergence locally and confirmed it is environment-driven, not a
real layout change.

## Diagnose

Decide which of two opposite problems you have by printing **both** measures per rendered line:
the display **cells** and the raw `String.Length`. Compare each against the width `bound`:

```fsharp
let esc = string (char 0x1B)
for line in out.Replace("\r\n", "\n").Split('\n') do
    // cells = visible width: strip ANSI SGR escapes before measuring
    let visible = System.Text.RegularExpressions.Regex.Replace(line, esc + @"\[[0-9;]*m", "")
    printfn "cells=%3d len=%3d bound=%3d | %s" visible.Length line.Length bound line
```

Classify each line:

| Observation | Meaning | Fix |
|---|---|---|
| `cells ≤ bound` **but** `len > bound` | **Invisible-byte artifact** (this incident): the line folds correctly; only leaked escapes inflate the byte count. | Force ANSI/color off on the plain surface (below), or assert on `cells`. |
| `cells > bound` | **Genuine display overflow**: the visible text really is too wide. | A real layout fix (narrower content, different wrap/truncation) — **not** the ANSI fix. |

If `len == cells` everywhere and the line still overflows, there are no leaked escapes — it is a
true overflow. The two diagnoses take **opposite** fixes, so always print both numbers before
changing anything.

## Fix

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
terminal's colors to "fix" a test is the wrong trade. Product rendering should stay colored in a
real terminal; only the deterministic surface is pinned.

Alternatively (or additionally), assert against **cells**, not bytes: strip the SGR escapes (as in
Diagnose) before comparing to `bound`. Pinning the capability is the more robust fix because it
keeps the plain surface genuinely plain for every consumer, not just the one assertion.

## Generalize

`GITHUB_ACTIONS` is one signal among many. Other CI systems force-enable color through their own
variables (`CI`, `TF_BUILD`, `TEAMCITY_VERSION`, `BUILDKITE`, …), and the cross-tool `NO_COLOR`
convention asks programs to suppress color when it is present. A plain surface that relies on
*absence* of these signals is fragile; pin the capability rather than trusting the environment.

The durable lesson outlives this specific variable and library version:

> **Assert against the same measure the system actually uses.** A width/wrap contract is about
> display **cells**; do not check it with **byte length** unless you have guaranteed the two are
> equal. When they can diverge (ANSI, combining marks, wide glyphs, encoding), measure the thing
> the user sees.

That framing applies to future fidelity problems that are not byte-for-byte identical to this one.

## Version scope

Verified on **Spectre.Console 0.57.x** (0.57.1 in FS.GG.Governance, 0.57.0 in FS.GG.SDD) on the
GitHub Actions runner. The re-detection behavior of `AnsiConsole.Create` is a library
implementation detail, not a documented guarantee — treat this as **version-scoped**. On a
different Spectre.Console version, re-run the *Reproduce locally* step to confirm the behavior
before assuming the same fix applies.

## Provenance

Every claim above traces to a verified incident — nothing is from memory:

- **Spec**: FS.GG.Governance `091` (headless render determinism).
- **Issues**: `#32` (the test-fidelity gap), `#34` (the publish it blocked), `#37` (the fix).
- **Evidence runs**: `28376202121` (the diagnostic cell-vs-byte dump that identified leaked
  escapes) and `28377734248` (the green `FS.GG.Governance.Cli@1.2.0` publish after the fix).
- **Date stamped**: 2026-06-29.
- **Corrected root cause**: `AnsiSupport.No` overridden under `GITHUB_ACTIONS` (leaked SGR bytes),
  **not** glyph/width measurement — an earlier hypothesis the diagnostic dump disproved.
- **Live fix in this repo**: `tests/FS.GG.Governance.Cli.Tests/RenderSupport.fs` (`plainConsole`
  pins `Capabilities.Ansi <- false` / `ColorSystem <- NoColors` post-`Create`); the assertion it
  protects is in `WidthResilienceTests.fs`.

## Distribution

This skill is **advisory** — it gates nothing. No build/test/publish/merge workflow references it;
deleting it changes no CI outcome.

- **Canonical source**: `FS-GG/.github` → `.claude/skills/spectre-console-headless-fidelity/SKILL.md`.
- **Installed (Spectre-using repos)**: FS.GG.Governance (0.57.1), FS.GG.SDD (0.57.0). Each carries
  a verbatim copy plus an `AGENTS.md` entrypoint that *references* this body (no second copy).
- **Excluded**: FS.GG.Rendering and FS.GG.Templates — they do not render with Spectre.Console, so
  the skill does not apply.

To install in another Spectre-using repo, copy this file to that repo's
`.claude/skills/spectre-console-headless-fidelity/SKILL.md` and add the `AGENTS.md` entrypoint.
