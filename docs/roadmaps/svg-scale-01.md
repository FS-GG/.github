# SVG-SCALE-01 — accessibility and measured SVG scale

Status: complete at the source/generated-candidate boundary; `SVG-PREVIEW-C` selected. Route: routine for
source and generated-candidate work; any package publication remains a separately evidenced protected operation.

This is the executable M9 plan for the
[accepted SVG game-engine programme](../2026-09-07-064259-svg-game-engine-template-design-roadmap.md). It
closes C18/C19 with reproducible browser measurements and an exact generated-player accessibility matrix.

## Stable boundary

- Rendering owns scene invalidation, culling, layer/cache lifecycle, complexity counters and the measurement
  harness. Measurements distinguish JavaScript callbacks from style/layout/paint and declare unavailable
  browser stages instead of estimating them.
- Templates owns integrated player/Studio workloads, responsive and alternative-control journeys, and the
  installed candidate. It consumes exact producer artifacts and does not restate renderer algorithms.
- Chromium is the detailed trace/reference budget gate. Firefox and WebKit provide functional browser coverage
  and only the timing dimensions their engines expose. Raw samples, warm-up, window and host identity travel
  with every verdict.
- Culling changes live representation only. Selected/focused objects, semantic alternatives, authoritative
  state and full export remain present and equivalent.

## Milestones

- [x] **SVG-SCALE-01.1 — Freeze the measurement contract and failing controls — route: routine**

  Materialize the accepted workloads, reference-host/browser identities, warm-up, sample window, percentile
  method and unavailable-stage vocabulary in Rendering. Demonstrate that idle rebuild, world-extent growth,
  latency, missed-frame and retained-resource controls can fail before relying on them as release gates.

  Rendering [PR #1319](https://github.com/FS-GG/FS.GG.Rendering/pull/1319), merge
  `2b8427564c1b15da933f2e377415cebc25ebab4f`, froze the reference host, Chromium identity, workloads,
  warm-up, sample windows, nearest-rank p95 method, absolute budgets and executable failing controls. The
  later .3 evidence corrected only the host-isolation field to contract v2; workloads and thresholds stayed
  unchanged.

- [x] **SVG-SCALE-01.2 — Bound dirty scheduling and the spatial working set — route: routine**

  Qualify coalesced animation-frame scheduling, immutable revision dirtiness, viewport-plus-overscan queries and
  selected/focused semantic pinning. Prove idle stabilization reaches zero rebuilds and a 10× world extent with
  the same visible content does not increase live nodes and stays within the accepted 20% steady-state cost.

  Rendering [PR #1320](https://github.com/FS-GG/FS.GG.Rendering/pull/1320), merge
  `b1f5b7aaccd057dc75edb6417e1645cc187d6bae`, added deterministic immutable chunk indexing, bounded
  viewport-plus-overscan queries, semantic pinning, stable source order and explicit entry/query refusal.
  The 135 Scene tests, packed .NET/Fable consumers, generated products and packed Chromium adapter passed.

- [x] **SVG-SCALE-01.3 — Qualify caches, batching and SVG complexity — route: routine**

  Measure generation-scoped layer/cache invalidation and disposal, repeated-symbol/static-path batching,
  filters/masks/text/path counters and effect reduction at 100- and 200-visible-entity workloads. Retain raw
  lifecycle and latency samples and refuse a complexity limit without changing authoritative facts or targets.

  Rendering [PR #1321](https://github.com/FS-GG/FS.GG.Rendering/pull/1321), merge
  `1f4d822034284312dd6230a7af0e3afc089afdce`, retained the exact raw qualification receipt. Three
  independent ordinary p95 samples were 67.090–83.396 ms against 100 ms; dense p95 samples were
  117.688–133.267 ms against 150 ms; 10× world extent produced a 0.988 p95 ratio and zero live-node
  growth. The 30-second retained run delivered 1,800/1,800 frames and disposal cleared every listener,
  frame and active resource. Contract v2 gates candidate-cgroup CPU pressure/throttling and reports shared-host
  load diagnostically, because the latter is outside the candidate isolation boundary.

- [x] **SVG-SCALE-01.4 — Run the generated accessibility and scale matrix — route: routine**

  Run exact generated Player and Studio candidates in Chromium, Firefox and WebKit across desktop, narrow/touch
  and 400% reflow viewports. Qualify keyboard, touch, screen-reader alternatives, reduced motion, focus through
  culling/reflow, dense/continuous workloads, startup/chunk identities and the scoped timing evidence available
  from each browser.

  Templates [PR #479](https://github.com/FS-GG/FS.GG.Templates/pull/479), merge
  `6e531ffa3baf63971554296f243dd5a041d15b3f`, packed exact Rendering merge `1f4d8220`, generated
  Player and Studio candidates, and passed Chromium, Firefox and WebKit. The matrix observed 2,000 indexed
  entities, 200 live SVG objects and 200 semantic alternatives; desktop, touch, 320px reflow and 400%
  layout-equivalent zoom had no overflow; reduced motion, culling/reflow focus, touch input and Studio palette
  focus restoration passed. The scale source remains candidate-only and absent from the stable baseline.

- [x] **SVG-SCALE-01.5 — Close M9 and select Release C — route: routine**

  Reconcile C18/C19, M9 and section 13 performance/accessibility evidence against exact producer and generated
  candidate identities. Preserve raw traces and unavailable dimensions, keep public availability separate, and
  select `SVG-PREVIEW-C` only after every declared workload and accessibility journey passes without a silent
  rebaseline.

  The exact producer and generated identities above reconcile C18/C19, M9 and section 13.8 at the candidate
  boundary. Raw samples remain in Rendering, the browser-family evidence remains in Templates, unavailable
  engine stages are disclosed rather than estimated, and no threshold was relaxed. Public availability and
  installed public compatibility remain open under [SVG-PREVIEW-C](svg-preview-c.md), which is now selected.

## Completion boundary

This milestone completes C18/C19 and M9 at the source/generated-candidate boundary. It does not claim public
Release C artifacts, complete C20 delivery, Release D, or lifecycle/default activation. Those remain owned by
their later roadmap stages and explicit evidence boundaries.
