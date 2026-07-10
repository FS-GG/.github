# ADR-0029: The game TestSpec corpus is FS.GG.Game-owned; .github keeps pointer stubs

- **Status:** Accepted
- **Date:** 2026-07-11
- **Affects:** **FS.GG.Game** (becomes the single source of truth — the 15 `docs/TestSpecs/Games/*` specs + `docs/TestSpecTutorial.md`), **.github** (retires its canonical copies to redirect stubs, re-points the consumer/profile references, and supersedes the ADR-0022 open-item recommendation)

## Context

ADR-0022 extracted FS.GG.Game as an SDD-driven component and, as an **open item carried
into the epic** (not settled by that ADR), recommended: *relocate the 6 game-logic library
designs to FS.GG.Game, but keep the 15 game TestSpecs "where cross-repo tests reference
them"* — i.e. in `.github` — to be settled in P2/P3.

ADR-0022 §context already counts the game TestSpec corpus (15 specs + the flagship
`TestSpecTutorial.md`) as FS.GG.Game's **donor material** — Game owns its correctness. But
the corpus physically lived in `.github`, the org coordination hub, **outside the
contract/reconcile gates** that govern registry and tutorial prose. Nothing there checks a
spec against the surfaces it teaches.

That gap produced a concrete failure. ADR-0024 retired `FS.GG.UI.Canvas.Audio` in favour of
the standalone `FS.GG.Audio.Core` component, yet every spec's §10 and the tutorial kept
teaching the removed `open FS.GG.UI.Canvas` audio API — the platform's onboarding exercise
pointed at a deleted surface. Filed as [FS.GG.Game#122](https://github.com/FS-GG/FS.GG.Game/issues/122);
the `.github` source was corrected in [.github#393](https://github.com/FS-GG/.github/pull/393).

Investigating the corpus's home ([FS.GG.Game#124](https://github.com/FS-GG/FS.GG.Game/issues/124))
surfaced three facts that reverse ADR-0022's provisional recommendation:

1. **The corpus is already mirrored into FS.GG.Game.** The 15 specs were copied there in
   [FS.GG.Game#29](https://github.com/FS-GG/FS.GG.Game/pull/29) as *"verbatim copies;
   FS-GG/.github remains the source of truth"* — but as a **manual one-off, with no sync or
   drift gate**. A manual mirror silently rots: it still taught the removed audio API after
   the source was fixed, and had to be re-synced by hand in
   [FS.GG.Game#125](https://github.com/FS-GG/FS.GG.Game/pull/125).
2. **No *code* references the specs.** Nothing in `.fs`/`.fsx`/`.yml` — in either repo —
   reads these files; only prose docs link them (`profile/README.md`,
   `docs/consumer/index.md`, the tutorial). So ADR-0022's stated reason to keep them in
   `.github` — that cross-repo **tests** reference them — does not currently hold. The
   dependency is documentary, and a link redirects as easily as it resolves.
3. **The drift is structural, not incidental.** The canonical copy sat in the one repo whose
   gates could not catch it, while a stale mirror sat in the repo whose gates could. Keeping
   `.github` canonical preserves exactly that inversion.

## Decision

**FS.GG.Game is the single source of truth for the game TestSpec corpus** — the 15
`docs/TestSpecs/Games/*.md` specs and `docs/TestSpecTutorial.md`.

- The corpus lives in FS.GG.Game, where its normal review + CI gates govern it.
- `.github` **retires its canonical copies to one-line pointer stubs** at the same paths, so
  existing inbound links do not 404; each stub states that FS.GG.Game is canonical and links
  the file there.
- `.github`'s consumer-facing references (`docs/consumer/index.md`, `profile/README.md`) are
  re-pointed at the FS.GG.Game location.
- This **supersedes ADR-0022's open-item recommendation** to keep the 15 TestSpecs in
  `.github`; ADR-0022's companion recommendation to relocate the 6 game-logic *library
  designs* to FS.GG.Game is unaffected.

Not in scope: the corpus is **not a versioned contract**, so `registry/dependencies.yml` and
`docs/registry/compatibility.md` are untouched.

## Consequences

- **The governance gap closes.** The corpus now lives where a spec that teaches a removed
  surface is caught by the same review/CI that catches any other drift in FS.GG.Game — the
  root cause of #122.
- **One copy, not a mirror.** With a single canonical source there is no mirror to sync, so
  no drift-check gate is needed; the failure mode of #29/#125 (a hand-copied mirror rotting
  against its source) is designed out rather than patched.
- **`.github` stays cheap to keep coherent.** Its stubs are redirects — a few lines each —
  not content it must reconcile.
- **The tutorial changes repos.** `TestSpecTutorial.md` moves to FS.GG.Game; its inbound
  links from `docs/consumer/index.md` and `profile/README.md` are redirected. Historical
  citations (e.g. ADR-0024's `docs/TestSpecTutorial.md:348` reference, report prose) are
  point-in-time and are left as written.
- **Execution is decide-then-flip.** This ADR and Game's adoption of the full corpus land
  first; `.github` retires its copies to stubs only after FS.GG.Game holds the complete
  source, so no window exists where the canonical file is absent from both repos.

## Sequencing

1. **This ADR** (`.github`) — records the decision, supersedes the ADR-0022 open-item.
2. **FS.GG.Game completes the source** — adds `TestSpecTutorial.md` (the specs already live
   there, corrected in #125), so Game holds the full corpus.
3. **`.github` flips** — replaces `docs/TestSpecs/Games/*` and `docs/TestSpecTutorial.md`
   with pointer stubs to FS.GG.Game and re-points the two consumer references.

Tracked as the children of [FS.GG.Game#124](https://github.com/FS-GG/FS.GG.Game/issues/124).
