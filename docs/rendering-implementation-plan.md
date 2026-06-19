---
title: Rendering implementation plan
category: FS.GG
categoryindex: 6
index: 10
description: Implementation plan for the rendering repository, starting from a fresh standard Spec Kit repository.
---

# Rendering implementation plan

The rendering repository is implemented first. It starts as a fresh standard
Spec Kit repository, then imports selected FS.Skia.UI runtime material. The goal
is a product repository that can build, test, document, package, validate
templates, and release without depending on governance tooling. The first
version should be deliberately light: import the tests and checks that protect
current product behavior, and leave behind mechanisms whose cost is not yet
justified.

"Deliberately light" is about not bulk-importing the legacy suite of several
hundred tests without justification. It is not about skimping on test
infrastructure. A comprehensive rendering / performance / mouse / keyboard test
harness is built early as deliberate infrastructure (Stage R5). The harness is a
capability, not a mandatory gate: its fast deterministic tiers are the default
inner loop, while the heavier live, performance, and kernel-input tiers are
opt-in and exercised only when a claim needs that level of evidence. Building it
does not mean it is always fully run.

## Objectives

- Create a fresh rendering repository using standard Spec Kit.
- Import only selected runtime product slices from this repository.
- Keep the product workflow understandable without custom governance machinery.
- Keep controls, design-system primitives, themes, and design-specific kits as
  rendering-owned layers.
- Require every imported test, generated fixture, validation gate, or governance
  mechanism to justify its product value and maintenance cost.
- Build a comprehensive but tiered, opt-in rendering / performance / mouse /
  keyboard test harness early, so faithful evidence is available on demand
  without making every edit pay for it.
- Keep templates with rendering unless their cadence later justifies a separate
  repository.
- Defer package rebrand unless explicitly approved as a release decision.

## Non-goals

- Do not transform this repository in place.
- Do not start by importing old `.specify` customizations or `speckit-*`
  workflow assumptions.
- Do not introduce a mandatory custom feature graph.
- Do not require the governance repository for build, test, docs, template, or
  package validation.
- Do not preserve every historical feature, readiness log, or generated
  artifact as active state.
- Do not import the old test and governance surface wholesale.
- Do not keep a check only because it once caught something or because it makes
  the repository look rigorous.

## Stage R1 - Create the fresh repository

Create the rendering repository before importing product code.

Deliverables:

- empty or minimal repository with README, license, ignore files, and standard
  Spec Kit setup;
- initial solution/project layout;
- basic package metadata policy;
- minimal build/test/docs commands;
- initial docs page that states the repository owns the rendering product;
- explicit statement that the initial validation set is intentionally small.

Exit criteria:

- a fresh checkout has a clear local setup path;
- standard Spec Kit is the feature workflow baseline;
- no custom governance platform is required.

## Stage R2 - Define product shape

Define the product boundary before copying code.

Deliverables:

- package/module map for scene, color, layout, input, viewer, Elmish, controls,
  controls Elmish integration, testing, and template support;
- decision on whether package IDs stay `FS.Skia.UI.*` initially or move later;
- design/control layering document copied or adapted from
  [Design and controls](design-and-controls.md);
- template ownership decision;
- list of product docs to import.

Exit criteria:

- maintainers can explain what rendering owns;
- controls, design-system primitives, themes, and design-specific kits have
  distinct boundaries;
- rebrand is either explicitly deferred or explicitly planned.

## Stage R3 - Define the initial validation set

Decide which tests and checks are worth importing before copying the full test
surface.

Each candidate test or check needs a justification record:

| Field | Purpose |
|---|---|
| Product contract | What user-visible or package/template behavior this protects. |
| Failure mode | The concrete regression it is expected to catch. |
| Owner | Who maintains it when it fails or becomes stale. |
| Frequency | Local inner loop, CI, release only, or manual/advisory. |
| Cost | Runtime, setup complexity, flake risk, fixture size, and maintenance burden. |
| Decision | Import now, defer, archive, or rewrite smaller. |

Default decisions:

- import focused unit tests for current runtime behavior;
- import public API and package checks only when they protect current package
  consumers;
- import template checks that simulate real generated products;
- defer broad historical readiness reports;
- archive generated fixtures that no longer represent a current product
  contract;
- rewrite oppressive checks into smaller tests before importing them;
- treat the rendering test harness as deliberate infrastructure with its own
  justification record, not as a legacy-test import — its display-agnostic parts
  (environment probe, CLI skeleton, evidence schema) MAY be scaffolded as early
  as this stage, with the live and performance tiers completed in Stage R5.

Exit criteria:

- the initial validation set is small enough for routine product work;
- every imported check has a justification record;
- deferred checks are not lost, but they are not active obligations;
- release-only checks are clearly separated from local development checks.

## Stage R4 - Import selected source

Copy selected product source into the fresh repository.

Candidate imports:

- runtime libraries under `src/**`;
- runtime tests selected by the validation-set justification;
- controls docs and examples;
- design-token and theme sources that belong to the product;
- template files and generated-product smoke tests selected by the
  validation-set justification;
- selected architecture docs and ADRs that remain current.

Rules:

- copy code as product source, not as old workflow state;
- remove or rewrite references to retired governance assumptions;
- keep provenance notes that identify source commit and copied paths;
- keep test/check justification notes with the imported validation surface;
- leave historical readiness logs and old feature workflow artifacts in this
  repository unless a specific migration note needs them.

Exit criteria:

- product code compiles in the fresh repository;
- tests run from the fresh repository;
- imported docs describe current product behavior;
- no old custom governance runtime is needed.

## Stage R5 - Build the rendering test harness

Build a comprehensive rendering / performance / mouse / keyboard test harness as
deliberate infrastructure, early. This is the productive use of the time saved by
not bulk-importing the legacy suite. The harness is a capability, not a mandatory
gate: fast deterministic tiers are the default inner loop, and the heavier tiers
are opt-in and run only when a claim needs them. Comprehensive does not mean
always fully used.

This stage depends on the viewer and controls seams imported in Stage R4
(`Viewer.captureScreenshotEvidence`, `Viewer.runBounded`,
`ControlsElmish.captureRespondsProof`, `ControlsElmish.Perf.runScript`,
`FrameMetrics`). The display-agnostic parts — environment probe, CLI skeleton,
and evidence schema — MAY be scaffolded earlier (Stage R3); the live and
performance tiers come online once the viewer code is present.

Each artifact MUST state what it proves and what it does not, so screenshots and
timings cannot overclaim. Tiers:

| Tier | Purpose | Display dependency | Authoritative for |
|---|---|---|---|
| T0 | Pure scene/control render + retained routing | none | determinism, tree equality, routing, non-blank offscreen PNGs |
| T1 | Offscreen GPU/CPU screenshot readback | offscreen / Skia | renderer pixel output (not desktop visibility) |
| T2 | Live X11 window smoke + XTEST input | X11 server + window manager | window creation, visibility, focus, real mouse/keyboard, desktop screenshot |
| T3 | Faithful frame pacing / performance | Xorg/KMS with real vblank | vsync, frame interval, paint/compose/swap timing |
| T-uinput | Kernel-level input fidelity (opt-in) | `/dev/uinput` + `/dev/input` | evdev/libinput input path |

Deliverables:

- a dedicated `tests/Rendering.Harness/` project, separate from any governance
  path, with subcommands `probe`, `offscreen`, `live-x11`, `perf`, and `input`;
- an environment probe that records display / GL / refresh / extension facts per
  run and the effective backend (X11 vs Wayland);
- an evidence artifact contract (`run.json` carrying `proofLevel`,
  `authoritativeFor`, `notAuthoritativeFor`, display/renderer/present facts, and
  timing percentiles) plus `metrics.csv` and a human `summary.md`;
- performance modes (`throughput`, `paced-60`, `paced-native`, `stress-resize`,
  `input-latency`), each declaring whether it is deterministic, live-host, or
  timing evidence;
- declarative input scripts with a `pure` backend (deterministic, mapped to
  `Perf.runScript` / `captureRespondsProof`), an `x11-xtest` backend (default
  live), and an opt-in `uinput` backend;
- a recorded capability baseline for the development environment.

Exit criteria:

- T0/T1 run with no live desktop and are fast enough for the default inner loop;
- T2 launches the viewer on X11 (Wayland disabled for the process), discovers the
  window, captures a non-blank window PNG, injects mouse and keyboard input, and
  confirms a visible state change;
- T3 runs a bounded frame set and persists per-frame and percentile metrics
  together with the display and swap-control facts, and refuses to label a run
  "vsync faithful" when those facts are missing;
- the kernel-input tier degrades cleanly when `/dev/uinput` is absent and is
  documented as opt-in (requires host device pass-through);
- no harness tier is required for a routine rendering change, and none depends on
  the governance repository.

Capability baseline (measured 2026-06-14 on the development container):

- `DISPLAY=:1` live; `XTEST`, `Present`, `RANDR`, `DRI3`, `XInput` available.
- Real output `HDMI-A-1` at 1920x1080 @ 119.93 Hz — a genuine refresh source, so
  T3 vsync/pacing is feasible rather than Xvfb-only.
- Hardware GL via AMD/Mesa (GL 4.6, direct rendering); the live host path is
  OpenGL (GL), consistent with this repository's backend.
- Full X11, capture, and performance toolchain installed (`xrandr`, `xdpyinfo`,
  `xinput`, `xdotool`, `xwd`, `maim`, ImageMagick, `ffmpeg`, `perf`, `radeontop`,
  `apitrace`, `mangohud`, `Xvfb`, `Xephyr`, `weston`, `xpra`).
- `/dev/uinput` and `/dev/input` are NOT present; the kernel-input tier needs
  host pass-through (`--device /dev/uinput --device /dev/input`).
- `WAYLAND_DISPLAY` is set; the harness MUST unset it for the viewer process,
  record the effective backend, and fail or classify the run as Wayland rather
  than silently proceeding.

See the rendering harness container research report for the detailed tier
rationale, tool list, and step-by-step procedures.

## Stage R6 - Stabilize product validation

Add only product checks that pay for themselves. If a check is valuable but
oppressive, rewrite it smaller before making it part of the default workflow.

Checks to consider:

- unit and integration tests;
- API surface drift checks;
- design-token and theme smoke checks;
- control behavior and accessibility checks;
- package skew checks;
- docs build checks;
- template pack/install/instantiate checks;
- generated-product restore/build checks;
- release package checks;
- selected rendering harness tiers wired in at the right frequency (T0/T1 local,
  T2/T3 on-demand or CI, kernel-input opt-in), each with a justification record.

Exit criteria:

- routine product changes have a documented validation path;
- default local validation is fast enough that contributors will actually run
  it;
- template validation simulates a real generated consumer;
- release validation is explicit and separate from local development checks;
- every active validation mechanism has a current justification and owner;
- none of the checks require the governance repository.

## Stage R7 - Bridge the old repository

After rendering is usable, document the handoff.

Deliverables:

- bridge README or report in this repository;
- source commit and import-path provenance;
- package/template migration notes if identities changed;
- archive note for old specs, reports, and readiness artifacts.

Exit criteria:

- new product work is opened in the rendering repository;
- this repository receives only bridge, archive, provenance, or emergency
  migration fixes;
- governance experiments are not mixed into rendering stabilization work.

## Stage R8 - Decide rebrand separately

Once the rendering repository is stable, decide whether package and template
identity should remain `FS.Skia.UI` or move to a new identity such as
`FS.GG.UI`.

If rebranding:

- choose root namespace and package prefix;
- choose template package ID and short name;
- choose docs URL and bridge policy;
- publish replacement packages before deprecating old packages;
- update template identity as one coherent matrix.

Exit criteria:

- package, namespace, template, docs, and repository names agree;
- migration docs explain old-to-new identity mapping;
- old package IDs are deprecated only after replacement packages exist.

## Acceptance criteria

The rendering plan is complete when:

- the rendering repository starts from standard Spec Kit;
- selected runtime code, docs, tests, templates, and package metadata are
  imported deliberately;
- controls, design-system primitives, themes, and design-specific kits are
  documented and separated;
- fresh checkout restore/build/test/docs/package/template validation works;
- a comprehensive but tiered, opt-in rendering / performance / mouse / keyboard
  test harness exists, with each artifact declaring what it proves;
- imported tests and governance checks are justified individually rather than
  moved wholesale;
- ordinary rendering work does not depend on governance tooling;
- this repository is bridge/archive for rendering product work.
