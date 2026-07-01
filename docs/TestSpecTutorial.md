---
title: "Your first build: a TestSpec, end to end"
category: FS.GG
categoryindex: 6
index: 12
description: A complete first-run tutorial for new users — install FS-GG, scaffold a runnable Skia/Elmish app, then build a real game from a TestSpec by driving it through the whole charter → ship lifecycle.
---

# Your first build: a TestSpec, end to end

This is the **first real thing to do** with FS-GG. It takes you from an empty
machine to a running F# game that you built yourself, under a managed lifecycle,
in one sitting. Instead of inventing a project on the spot, you start from a
ready-made **TestSpec** — a complete, unambiguous game design in
[`docs/TestSpecs/`](TestSpecs/) — and drive it through the FS.GG.SDD lifecycle
from `charter` to `ship`.

By the end you will have:

- the `fsgg-sdd` CLI installed,
- a windowed **Skia/OpenGL Elmish/MVU** app scaffolded and running,
- one TestSpec (we use **Pong**) carried through every lifecycle stage, with its
  acceptance criteria turned into passing tests.

If you have never read the platform overview, skim the
[consumer guide](consumer/index.md) first — but you can also just follow along
here; everything you need is below.

---

## Why start from a TestSpec?

A TestSpec is a single Markdown file that fully specifies a small game: the core
loop, controls, mechanics with exact numbers, the Elmish/MVU state model, the
Skia render order, win/loss rules, balancing tables, and — crucially — a
numbered list of **acceptance criteria written as test scenarios**. See
[`docs/TestSpecs/Games/pong.md`](TestSpecs/Games/pong.md) for the one we use.

That makes it the ideal first project, because:

- **Nothing is left to your imagination.** The spec already answers the
  questions the lifecycle's `clarify` stage exists to surface.
- **It maps almost one-to-one onto the lifecycle.** Each numbered section of the
  spec feeds a specific stage (you'll see the mapping below).
- **"Done" is defined.** Section 14 is a checklist of Given/When/Then scenarios.
  When those pass, the feature is genuinely shippable — not "looks fine on my
  screen."

### Anatomy of a TestSpec

Every spec under `docs/TestSpecs/Games/` shares the same 15-section shape. Here
is how each section maps to a lifecycle stage — keep this table next to you for
the whole tutorial:

| Spec section | Feeds lifecycle stage |
|---|---|
| §1 Overview, §2 Core Game Loop | `charter`, `specify` |
| §3 Controls & Input | `specify` |
| §4 Mechanics (detailed) | `specify`, `plan` |
| §5 Entities / Game Objects | `specify`, `plan`, `tasks` |
| §6 World / Levels / Progression | `specify` |
| §7 State Model (Elmish/MVU) | `plan` (the F# `Model`/`Msg`/`update`/`view`) |
| §8 Rendering (Skia 2D) | `plan` |
| §9 UI / HUD / Screens | `specify`, `plan` |
| §10 Audio | `tasks` (often deferred in v1) |
| §11 Win / Loss / Scoring | `specify`, `checklist` |
| §12 Difficulty & Balancing | `plan` (data-driven `Config`) |
| §13 Technical Notes | `plan`, `analyze` |
| §14 Acceptance Criteria | `checklist`, `tasks`, `evidence`, `verify` |
| §15 Stretch Goals | out of scope for v1 (future work items) |

### Pick your spec

Start with the simplest. Complexity and a rough session length are in each
spec's front-matter:

| Spec | Complexity | Why pick it |
|---|---|---|
| **`pong.md`** | simple (~5 min) | **Recommended first build.** Two paddles, one ball, no RNG spawning, tiny state model. |
| `snake.md` | simple (~5 min) | Grid stepping, a direction queue, one classic edge case (180° reversal). |
| `breakout.md`, `space-invaders.md` | simple | Once Pong feels easy. |
| `tetris.md` | simple (~10 min) | After your first end-to-end pass — a fuller state model (piece bag, lock delay, line clears). |
| `tower-defense.md` | complex (~25 min) | Your first genuinely complex spec: economy, waves, pathing. |
| `metroidvania.md`, `sandbox-survival.md` | complex (~45 min) | Save these for later. |

The rest of this tutorial uses **Pong**. Everything generalizes — swap the slug
and the section references and the workflow is identical.

---

## Part A — Install and scaffold

### Prerequisites

- **.NET SDK with `net10.0`** — every FS-GG product targets `net10.0`.
- **A GL/X11 session** to *see* the live window. Headless/offscreen and the
  deterministic test path need no display; only the live viewer does (e.g.
  `DISPLAY=:1` on Linux).
- **Git** — the lifecycle (and optional governance) read repository state.

### 1. Install the lifecycle CLI

`fsgg-sdd` is a .NET global tool:

```sh
dotnet tool install --global FS.GG.SDD.Cli   # exposes the `fsgg-sdd` command
fsgg-sdd --version
```

For pinned versions and feeds, see
[Versions, feeds & updates](consumer/versioning-and-updates.md).

> **`fsgg-sdd --version` is the *lifecycle CLI* version — not "the fs-gg version" of
> your app.** The CLI and the rendered `FS.GG.UI` set move on **independent** lines:
> updating the tool (`dotnet tool update --global FS.GG.SDD.Cli`) does **not** change
> which UI version you scaffold. The fs-gg-ui version is pinned by the provider
> descriptor in step 2, and you verify it in the generated product (`FsGgUiVersion`,
> step 3). Don't read "newest fs-gg version" off `fsgg-sdd --version`.
>
> **Your CLI version does still govern one thing, though.** As the lifecycle
> *orchestrator* ([ADR-0008](adr/0008-fsgg-sdd-cli-first-class-member-of-coherent-set.md)),
> `fsgg-sdd` **seeds** the `fs-gg-sdd-*` process skills and `.fsgg/early-stage-guidance.md`
> a scaffold is expected to carry — so install (or `dotnet tool update` to) a **current
> CLI (`0.3.0`+)**, otherwise the scaffold silently omits them. `fsgg-sdd doctor`
> reports whether a project is coherent with its set and `fsgg-sdd upgrade` reconciles
> it. See [Who drives the lifecycle](consumer/who-drives-the-lifecycle.md).

### 2. Scaffold a runnable product

`fsgg-sdd scaffold` lays down the SDD lifecycle skeleton **and** materializes a
real, runnable app in one command, via a template *provider*. The reference
`rendering` provider materializes the live, version-pinned
[FS.GG.Rendering](https://github.com/FS-GG/FS.GG.Rendering) `fs-gg-ui` app.

SDD embeds no provider, so first register the `rendering` descriptor into your
project's `.fsgg/providers.yml`. **This descriptor's `source:` line is the one pin
that sets your fs-gg-ui version** (`FS.GG.UI.Template::<version>`). We'll name the
project after the game.

Fetch the descriptor from `main` to get the **newest** coherent set — Templates
keeps its `FS.GG.UI.Template` pin moved up to the latest release, so `main` is the
newest — then **confirm the version you just pinned**:

```sh
mkdir -p ./Pong/.fsgg
curl -fsSL https://raw.githubusercontent.com/FS-GG/FS.GG.Templates/main/providers/rendering.providers.yml \
  -o ./Pong/.fsgg/providers.yml

grep 'source:' ./Pong/.fsgg/providers.yml      # -> FS.GG.UI.Template::<version>  (this IS your fs-gg-ui version)
```

> **For a reproducible scaffold, freeze the version instead of tracking `main`.**
> `main` can move under you. To pin an exact version, copy the descriptor from a
> **release tag** of [FS.GG.Templates](https://github.com/FS-GG/FS.GG.Templates) —
> or simply edit the `source:` line to the version you want:
>
> ```sh
> git -C /path/to/FS.GG.Templates show <tag>:providers/rendering.providers.yml \
>   > ./Pong/.fsgg/providers.yml
> ```

Then scaffold:

```sh
fsgg-sdd scaffold --root ./Pong --provider rendering --param productName=Pong
```

What you get (the `rendering` provider defaults to its **`game`** starter profile — a
small playable game scene, the ideal base to replace with Pong; pass
`--param profile=app` instead for the controls-showcase starter):

- a runnable **Skia/OpenGL Elmish/MVU** app (Scene, SkiaViewer, Controls);
- the **`.fsgg/` lifecycle skeleton** (`project.yml`, `sdd.yml`, `agents.yml`,
  `work/`, `readiness/`);
- the **CLI-seeded process artifacts** — `.fsgg/early-stage-guidance.md` and the
  `fs-gg-sdd-*` process skills under the agent skill folders (`.claude/skills/`,
  `.agents/skills/`) — which is why a current CLI matters (see the note above);
- a `.fsgg/scaffold-provenance.json` recording the externally owned files the
  provider wrote — and, as of CLI `0.3.0`, the `fsgg-sdd` version used (the
  orchestrator axis).

Useful flags: `--dry-run` plans without executing; `--no-update` skips refreshing
the template; `--force` materializes into a non-empty directory. Exit codes are
meaningful: malformed input (unknown provider, missing parameter, target
collision) exits `1`; a provider defect exits `2`; an incomplete scaffold is
never reported as complete.

> For the skeleton only — no runtime app — use `fsgg-sdd init` instead of
> `scaffold` (see the pinned-descriptor note above for reproducible provider
> registration either way).

### 3. Build and run the stock app

First **confirm the fs-gg-ui version the scaffold actually pinned** — the generated
product carries a single source of version truth, and this is the authoritative
"am I on the newest fs-gg version?" check (not `fsgg-sdd --version`):

```sh
cd ./Pong
grep FsGgUiVersion Directory.Packages.props   # the one FS.GG.UI.* version literal
```

> **Feed:** `FS.GG.UI.*` are **preview** packages served from the org GitHub feed
> (`https://nuget.pkg.github.com/FS-GG/index.json`), not nuget.org. If `dotnet
> build` can't find them, add that feed to the product's `NuGet.config` (or your
> global config). To move to a newer set later, it's **one edit** to `FsGgUiVersion`
> + `dotnet restore` — see [Versions, feeds & updates](consumer/versioning-and-updates.md).

```sh
dotnet build
dotnet run            # opens the live window (needs a GL/X11 session)
```

You now have a real, running product — the renderer's starter scene, not Pong
yet. Confirm the window opens (or that the headless build succeeds) before you
go further: everything below is about *replacing that starter scene with Pong*,
driven by the lifecycle.

---

## Part B — Drive the TestSpec through the lifecycle

The lifecycle ordering is fixed:

```text
charter → specify → clarify → checklist → plan → tasks → analyze → evidence → verify → ship
```

Each command reads and writes structured artifacts under `work/` and
`readiness/<id>/` and prints a **deterministic report**. You author in Markdown;
the CLI keeps the machine-readable JSON in sync. (Background:
[the development lifecycle](consumer/lifecycle.md).)

Every command speaks three projections, selected by flag with precedence
`--rich` > `--text` > `--json` > default. Use `--rich` at your terminal for a
readable summary; the JSON is the contract everything else builds on:

```sh
fsgg-sdd <stage> --rich     # human-friendly; degrades to plain text with no ANSI
fsgg-sdd <stage>            # default JSON automation contract
```

Work through the stages **with the Pong spec open beside you**, copying its
content into the artifact each stage authors.

### 1. `charter` — frame the product

```sh
fsgg-sdd charter --rich
```

Use Pong §1 (Overview) and §2 (Core Game Loop) to state intent and boundaries:
*"A two-player arcade table-tennis game; first to 11 wins; single static
playfield; keyboard only."* The charter is the product's north star, not a
feature spec — keep it short.

### 2. `specify` — write the feature spec

```sh
fsgg-sdd specify --rich
```

This is where the bulk of the TestSpec lands. Transcribe, into the spec the CLI
opens under `work/`:

- **§3 Controls & Input** — exact key bindings and input model (edge- vs
  held-state).
- **§4 Mechanics** — the precise rules and numbers (paddle speed, ball speed,
  bounce angles, serve direction).
- **§5 Entities** — paddles, ball, score, with their properties.
- **§6 World / §9 Screens / §11 Win-Loss-Scoring** — the playfield, the
  Title/Play/Point/GameOver screens, and "first to 11."

The golden rule: **if the spec gives a number, put the number in the spec
artifact.** Don't paraphrase `serveSpeed = 420 px/s` as "fast" — §12's balancing
table is the source of truth for every tunable.

### 3. `clarify` — resolve the unknowns

```sh
fsgg-sdd clarify --rich
```

Normally this is where you'd discover gaps. A TestSpec is deliberately
gap-free — so this stage mostly **confirms** there are no open questions, and
that's a valid result. If `clarify` flags something, the answer is almost always
already in a later section of the spec (e.g. §13 Technical Notes pins down
determinism and timestep). Record the resolution and move on.

### 4. `checklist` — generate the requirements checklist

```sh
fsgg-sdd checklist --rich
```

Seed the checklist directly from Pong **§14 Acceptance Criteria**. Each
Given/When/Then scenario becomes a checklist item — e.g. *"ball reflects with a
steeper angle when it strikes the paddle's edge,"* *"a point resets the ball to
center and serves toward the loser."* This list is your definition of done.

> **Coverage is a strict grammar.** `checklist` marks a requirement *covered*
> only when a list item leads with `- FR-###:` and carries its acceptance id **on
> the same line** — `- FR-001: W/S move the left paddle. (covers AC-002)`. A
> **bold** id (`**FR-001** …`) or a missing colon is *counted but reported
> uncovered*: the form looks present but establishes no coverage, and `plan` stays
> blocked. The exact accepted/rejected forms are in SDD's
> [Authoring Contracts](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/reference/authoring-contracts.md)
> reference (also restated in `.fsgg/early-stage-guidance.md`).

### 5. `plan` — design the implementation

```sh
fsgg-sdd plan --rich
```

The TestSpec hands you the design almost verbatim:

- **§7 State Model (Elmish/MVU)** is your F# architecture. Copy the `Model`,
  `Msg`, and `update`/`view` shape straight in. For Pong that's roughly:

  ```fsharp
  type Screen = Title | Playing | PointScored | GameOver

  type Model =
      { Screen: Screen
        Ball: Ball
        LeftPaddle: Paddle
        RightPaddle: Paddle
        LeftScore: int
        RightScore: int
        Config: Config }

  type Msg =
      | StartGame
      | PaddleInput of Side * Direction
      | Tick of float            // dt in seconds, per frame
      | Restart
  ```

- **§8 Rendering (Skia 2D)** is your `view`/draw plan: the back-to-front draw
  order, colors, and the logical→pixel scale.
- **§12 Difficulty & Balancing** becomes a data-driven `Config` record so every
  tunable is testable without touching code.
- **§13 Technical Notes** sets non-negotiables: fixed-timestep simulation via an
  accumulator, and a **seeded RNG** so tests are deterministic.

### 6. `tasks` — break it into ordered work

```sh
fsgg-sdd tasks --rich
```

Decompose the plan into dependency-ordered tasks. A natural Pong breakdown:

1. `Config` + `Model` types and initial state (§5, §7, §12).
2. `update`: paddle movement and clamping (§4).
3. `update`: ball integration + wall bounce (§4).
4. `update`: paddle collision + angle-from-contact-point (§4).
5. `update`: scoring, serve reset, win at 11 (§11).
6. `view`: Skia draw list and HUD (§8, §9).
7. Subscriptions: 60 FPS `Tick`, keyboard input (§3, §7).
8. Tests for each §14 scenario (see Part C).

§10 Audio is usually a **deferred** task in v1 — record it as deferred rather
than dropping it.

### 7. `analyze` — cross-artifact consistency

```sh
fsgg-sdd analyze --rich
```

A non-destructive check that spec, plan, and tasks agree. This catches the
classic drift: a number you changed in the plan but not the spec, a task with no
matching requirement, an acceptance criterion with no task. Fix any mismatch
before writing code.

### 8. Implement, then `evidence` — record what you built

Now write the F# against the plan. The scaffold already gave you a runnable
Scene/SkiaViewer; you are replacing the starter scene's `Model`/`update`/`view`
with Pong's. Build and run as you go:

```sh
dotnet build
dotnet run
```

When a slice is implemented and checked, record it:

```sh
fsgg-sdd evidence --rich
```

`evidence` captures declared implementation, verification, synthetic, and
deferral evidence — the audit trail that the work was actually done and how it
was checked (link the tests from Part C here).

> **What actually satisfies an obligation.** In `evidence.yml`, a `verify`
> obligation is *satisfied* only by a declaration whose `result` is `pass` **and**
> whose `synthetic` is `false`. A `synthetic: true` pass discloses a stand-in and
> does **not** satisfy; `result: deferred` records an accepted deferral, not a
> satisfaction; an unrecognized `kind` silently becomes `verification`. So link a
> real passing Part C test (`kind: verification`, `result: pass`, `synthetic:
> false`) — a synthetic placeholder will keep `verify` short. Full `kind`/`result`
> vocabulary and copyable examples: SDD's
> [Authoring Contracts](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/reference/authoring-contracts.md)
> reference. (This is the lifecycle `evidence.yml`, not any same-named doc the
> scaffolded app ships.)

### 9. `verify` — evaluate verification readiness

```sh
fsgg-sdd verify --rich
```

This evaluates readiness over your task / evidence / test obligations into
`readiness/<id>/verify.json`. It answers: *are the acceptance criteria actually
covered by passing tests?* If §14 has twelve scenarios and you have ten tests,
this is where the gap shows up.

### 10. `ship` — aggregate merge-boundary readiness

```sh
fsgg-sdd ship --rich
```

`ship` aggregates everything into `readiness/<id>/ship.json`. With no governance
installed it simply **reports** readiness (it never blocks your inner loop). If
you've adopted governance, this is also the protected-boundary handoff — see
Part D.

When `ship` reports ready and your game runs, you've completed your first
end-to-end FS-GG build.

---

## Part C — Acceptance criteria become tests

This is the part that makes a TestSpec special: **§14 is already your test
suite.** Each scenario is Given/When/Then, which translates directly into an
arrange/act/assert test against your pure `update` function. Because the MVU
`update` is a pure `Model -> Msg -> Model`, you test the whole simulation with no
window and no GL.

The `rendering` scaffold's test project uses **[Expecto](https://github.com/haf/expecto)**
(`open Expecto`, `testList`/`test`, `Expect.*`) with the `YoloDev.Expecto.TestSdk`
runner — not xUnit. Author your tests in that style so they compile and run under
the scaffolded `dotnet test`.

Take this Pong-style scenario:

> **Ball reflects off the top wall.**
> Given the ball at `(640, 8)` moving up-right,
> When one physics step occurs,
> Then its vertical velocity is inverted and it stays inside the playfield.

It becomes an Expecto test like:

```fsharp
open Expecto

[<Tests>]
let ballPhysics =
    testList "ball physics" [
        test "ball reflects off the top wall" {
            let model =
                { initial with
                    Ball = { Pos = { X = 640.0; Y = 8.0 }
                             Vel = { X = 300.0; Y = -300.0 } } }
            let stepped = update (Tick (1.0 / 60.0)) model
            Expect.isGreaterThan stepped.Ball.Vel.Y 0.0
                "vertical velocity is inverted"
            Expect.isGreaterThanOrEqual stepped.Ball.Pos.Y 0.0
                "ball stayed inside the playfield"
        }
    ]
```

Group related scenarios in one `testList` and mark the top-level list with
`[<Tests>]` so `YoloDev.Expecto.TestSdk` discovers it under `dotnet test` — no
manual `runTests` entry point is needed. Work through every numbered scenario in
§14 the same way. Two rules from §13 make this reliable:

- **Fixed timestep** — drive the simulation with an explicit `dt`, never with
  wall-clock time, so a step is reproducible.
- **Seeded RNG** — inject a seeded `System.Random` (e.g. `Random(12345)`) so any
  randomized behavior is deterministic in tests.

When every §14 scenario has a passing test, `verify` goes green and your
`evidence` has real verification behind it — that's the loop closing.

---

## Part D — (Optional) add governance

Governance is **never required** to build, run, or ship. When you want gates,
drop in the reference gate set and choose a posture:

```sh
dotnet new install FS.GG.Templates
dotnet new fs-gg-governance -o ./Pong --appName Pong --defaultProfile light
```

`light` is the non-blocking inner-loop posture; `strict` / `release` make the
block-on-ship gates actually block. This never changes how you build and run.
See [Adopting governance](consumer/governance.md).

---

## You did it — what's next

You now have the full muscle memory: install → scaffold → run → drive a spec
through `charter → ship` → turn acceptance criteria into tests. To go further:

- **Build a second game** from a different spec — try `snake.md` (a direction
  queue and the 180°-reversal edge case) or `breakout.md`.
- **Tackle a complex spec** — `tetris.md`, then `tower-defense.md`, once the loop
  is second nature.
- **Pick up a Stretch Goal** (§15) as a *new* work item — run it through the same
  lifecycle, which is exactly how real features get added.
- **Wire the JSON into CI** — see [Output, automation & CI](consumer/automation.md);
  build your pipeline on `fsgg-sdd <stage>` JSON, not on `--rich`.

### If a step misbehaved

- Scaffolding or provider resolution failed → re-read
  [Getting started](consumer/getting-started.md) §2 and check
  `.fsgg/providers.yml` exists and `--provider rendering` matches its descriptor.
- The window won't open → you likely have no GL/X11 session; the build and tests
  still run headless.
- A lifecycle command surprised you → [FAQ & troubleshooting](consumer/faq.md)
  and [the lifecycle tour](consumer/lifecycle.md).
