# ADR-0054: A `.github`-authored, product-materialized *driver* skill is a third `skill-registry` class — `workRoadmap` rides it

- **Status:** Accepted
- **Date:** 2026-07-19
- **Affects:** FS-GG/.github (owns `registry/skills.yml`, the new producer emitter for its own authored drivers, the scaffold-time materializer wiring, and the `contract-coherence.yml` pin); FS.GG.SDD (owns `Fsgg.Registry`, which learns the driver-class vocabulary and publishes the CLI carrying it).
- **Resolves:** the delivery-fabric question [ADR-0053](0053-roadmap-driven-milestone-loop-disposable-sdd-subagents.md) §Consequences deferred ("Delivery to product trees is a follow-up, not decided here"). No prior decision is changed — every ADR-0053 decision stands.
- **Decides:** [FS-GG/.github#1224](https://github.com/FS-GG/.github/issues/1224)
- **Transport (resolves [.github#1300](https://github.com/FS-GG/.github/issues/1300)):** this ADR's "product-materialized" clause named the *class* but hand-waved the concrete **byte-transport** — "the existing scaffold-time materializer then delivers it". [#1300](https://github.com/FS-GG/.github/issues/1300) found that clause underspecified against the offline scaffolder, and the [Byte-transport](#byte-transport-resolves-github1300) section below fills it: **`.github` publishes the driver bytes as a versioned package the SDD CLI pins and materializes** — the [ADR-0062](0062-versioned-kit-package-replaces-byte-copy-sync.md) pattern, generalized one consumer over. The class decision above is unchanged; only its delivery mechanism is made concrete.
- **Amended by:**
  - [ADR-0057](0057-operator-scope-a-github-authored-never-materialized-skill-class.md) — adds a **fourth** class, `scope: operator` (`.github`-authored, materialized nowhere), beside the three named here. The three classes above and the `scope: driver` decision are unchanged.
  - [ADR-0056](0056-sdd-is-the-default-lifecycle-spec-kit-is-legacy-and-scheduled-for-removal.md) §Decision.3 — the composed (AND-of-producers) `materializes-when` this ADR gives `workRoadmap` **collapses to `fs-gg-feedback-report`'s `always`**. Its one binding clause was `fs-gg-feedback-capture`'s `feedback == true and lifecycle == spec-kit`; ADR-0056 freezes capture and schedules its removal, so the driver no longer ANDs against a skill that is going away, and it now materializes on the `sdd` lane. The `scope: driver` class, `.github`'s producer emitter, and the reconcile fabric are all unchanged — only this one row's predicate value changes ([.github#1247](https://github.com/FS-GG/.github/issues/1247)).

## Context

[ADR-0053](0053-roadmap-driven-milestone-loop-disposable-sdd-subagents.md) canonized the **`workRoadmap`**
driver skill and deliberately left one thing open: *which fabric delivers it into the product trees it
is built to run in*. #1224 is that decision. The skill is **authored** in `.github`'s two agent-skill
roots ([ADR-0011](0011-agent-skill-roots-full-union-orchestrator-owned-mirror.md)) but **not
delivered** anywhere — so `/workRoadmap` lights up only where the bytes happen to already sit.

Two delivery fabrics exist, and the decision turns on a fact the issue's framing understated: **they
deliver to different places.**

| fabric | source | delivers to | governed by |
|---|---|---|---|
| **coordination kit** (`registry/repos.yml` `kit:` → `scripts/coordination-sync`) | `.github`-authored, `.claude/skills/` | the **8 rostered framework repos** (`receives: coordination-kit`) | [ADR-0019](0019-org-repo-roster-registry-and-coordination-kit.md) |
| **producer catalog** (`registry/skills.yml` → `materializes-when`) | **producer**-owned, reconciled from producer manifests | scaffolded **product trees**, at scaffold time | [ADR-0014](0014-skill-vendoring-one-manifest-one-materialize-verify.md)/[ADR-0017](0017-skill-registry-condition-aware-materialization.md) |

ADR-0053 §6 fixes where `workRoadmap` must run: **only** in a scaffolded product tree that has
`fs-gg-sdd-*` **and** `fs-gg-feedback-*` materialized, and it **refuses** in a kit source lacking them
(that is `.github` itself, and every framework repo). So the delivery target is *scaffolded product
trees* — the column only the producer catalog reaches.

## Decision

**Teach `registry/skills.yml` a third class: a `.github`-authored, product-materialized *driver*.**
`.github` becomes a producer of its own authored driver skills, emitting `workRoadmap` from a
manifest; the row is reconciled into `skills.yml` as `owner: .github, scope: driver`, with a
`materializes-when` predicate that ANDs the two producer conditions
(`fs-gg-sdd-*` present ∧ `fs-gg-feedback-*` present). The existing scaffold-time materializer then
delivers it to exactly the trees that can run it.

This names a genuine third quadrant that neither existing fabric occupies:

- **kit** = `.github`-authored → **framework** repos
- **producer catalog** = **producer**-owned → **product** trees
- **driver (new)** = **`.github`**-authored → **product** trees

### Why not the other three

- **A — coordination kit — is disqualified on *target*, not taste.** The kit's receivers are the 8
  framework repos; it has **no path to a scaffolded product tree**. Mirroring `workRoadmap` through it
  ships the skill precisely to the repos where ADR-0053 §6 makes it *refuse to run*. This is structural,
  independent of the "blurs the kit's meaning = coordination protocol" cost the issue named.
- **C — re-home to FS.GG.SDD — was the runner-up, rejected on authorship.** It costs **zero schema
  growth** (another `owner: fs-gg-sdd, scope: process` row) and ships fastest, which is real. But it
  **contradicts the premise ADR-0053 and ADR-0011 just established** — `.github` as the canonical
  author — and makes SDD own a driver that *composes Game's* feedback pair. The composition-by-reference
  ADR-0053 is built on does not require moving ownership; C moves it anyway to make the machinery fit.
  This org's standing preference is to say the true thing **structurally** rather than reuse machinery
  that distorts ownership (cf. the `fsgg-coord`-manifest move into the kit so "receives the tool ⇒ can
  run it" is structural; the `outside-fabric` list so "deliberately outside" is sayable). B honors that;
  C does not.
- **D — status quo — permanently defeats ADR-0053 §6's purpose**: the skill never auto-appears in the
  product repos it is built for. It is the no-decision baseline, not an end-state.

### The cost, accepted deliberately

B is **schema growth on a governed contract** (`skill-registry`, [ADR-0015](0015-register-the-registry-schema-as-a-governed-contract.md)),
so it is **publish-before-flip: two ordered PRs, SDD first** ([ADR-0037](0037-schema-growth-is-publish-before-flip.md);
ADR-0015 §3 as amended). It is the same procedure ADR-0052 followed for the wire-contract dimension.
And it is more machinery than the other options because `.github` has **no producer emitter today** —
the emitter is net-new, not a registry edit.

### The sequence (derived from ADR-0037)

1. **FS.GG.SDD (step 1 — publish).** Teach `Fsgg.Registry` the driver-class vocabulary — `scope: driver`,
   a non-producer `owner: .github`, and the composed `materializes-when` (AND of two producer predicates)
   — and **publish** an `FS.GG.SDD.Cli` carrying it. Per ADR-0037 §3 the new validator **must still
   accept `skills.yml` as it stands at `.github` HEAD**: the driver rule is *known* in step 1 and
   *enforced* only against the bumped `schemaVersion` in step 2. Mind the already-armed trap — `skills.yml`
   still declares `schemaVersion: 1` with the `mirrored` 1→2 bump **owed** — so the new rule may reject
   nothing the live document does.

2. **`.github` (step 2 — flip), one PR after the CLI is live:**
   - stand up the **producer emitter** for `.github`-authored drivers (a skill-manifest `.github`
     emits, reconciled into `skills.yml` the way SDD's/Game's manifests are);
   - add the `workRoadmap` row (`owner: .github, scope: driver, materializes-when: "<sdd> and <feedback>"`);
   - teach the scaffold-time materializer / union gate (`scripts/skill-union-assert.sh`) to pull a
     `.github`-owned driver row (today every `source:` points into a *producer* repo);
   - bump `skills.yml` `schemaVersion` **and** the `skill-registry` contract `version`, **paying the
     owed `mirrored` bump in the same flip or explicitly sequencing it**, advance the
     `contract-coherence.yml` `FS.GG.SDD.Cli` pin to the step-1 CLI, and keep the field-vocabulary
     comment + `docs/registry/compatibility.md` projection + `registry/skills.CHANGELOG.md` current.

   Between the two PRs the pin advances **ahead** of the bump (ADR-0037 §Context): that intermediate
   state is *checked* and its one red (`pin-coherence`, self-clearing via Renovate's bump PR) is honest.
   The reverse order (flip then publish) is silently green over a schema nothing checks — rejected there,
   rejected here.

## Byte-transport (resolves [.github#1300](https://github.com/FS-GG/.github/issues/1300))

The Decision above chose the *class*; it left the concrete delivery to one clause — "the existing
scaffold-time materializer then delivers it", echoed by §Sequence step 2's "teach the scaffold-time
materializer / union gate to pull a `.github`-owned driver row". [#1300](https://github.com/FS-GG/.github/issues/1300)
found that clause underspecified against a constraint the Decision did not weigh: **`fsgg-sdd scaffold`
runs on an offline inner loop with no `.github` clone**, and generic SDD is contractually forbidden
(`FS.GG.SDD/CLAUDE.md`, scaffold FR-002 / SC-005) from embedding any cross-repo package id, path, or
source. The materializer today sources skills from the **seeded ∪ provider union** only — it has no path
to a `.github`-owned byte. So a *delivery channel* must exist before the materializer can consume it, and
choosing that channel is the sub-decision #1300 carries.

**Decision — the `.github`-published package.** `.github` emits `workRoadmap`'s bytes plus
`registry/driver-skill-manifest.json` (predicate + sha256) into a **versioned package on the org feed**.
`FS.GG.SDD.Cli` **references it at a pinned version** (a `contract-coherence.yml` pin, the same shape as
the step-1 CLI pin this ADR already sequences), **restores it at CLI build/publish time — online** — and
at **scaffold time — offline** — materializes the driver into the product tree's skill roots from bytes
already local to the CLI, with the [ADR-0014](0014-skill-vendoring-one-manifest-one-materialize-verify.md)
content-addressed *verify* (the manifest sha256) as the integrity guarantee. The offline constraint is
satisfied because the only network step — the package restore — happens when the CLI is built, not when a
product is scaffolded; the pin lives in the CLI's own build graph, never in generic scaffold output.

**This is [ADR-0062](0062-versioned-kit-package-replaces-byte-copy-sync.md) generalized to the
product-materialization fabric.** ADR-0062 (#1262) retired the byte-copy push and made every
`.github`-authored kit / build-config byte ship as the versioned `FS.GG.Kit` package, materialized on
disk in each receiver from a pin — and it flagged the identical load-bearing detail this decision turns
on: *"a package reference is not a materialized file … keep a materialize step + the ADR-0014 verify"*.
#1300's driver is the same shape one consumer over. The only differences are the **consumer** (the SDD
CLI → scaffolded product trees, not Renovate → framework repos) and the **materialize point** (scaffold
time, not restore time). The org spent P4 moving `.github`-authored bytes *onto* this rail; delivering
the driver by it rides a mechanism that exists and was just hardened, rather than teaching the
materializer a bespoke `.github`-into-scaffold path.

### Why not the other two transports

- **Provider-vendored** — the `fs-gg-ui` template carries the driver subtree, materialized through the
  *existing* provider path (no new SDD source). Simplest for SDD, and rejected for the same reason
  Decision rejected **Option C**, one layer down: it puts a `.github`-authored skill's **bytes** inside a
  provider, which is "pretending a producer owns it" at the byte level even while the registry row still
  reads `owner: .github`. It also splits the source of truth — `.github`'s `driver-skill-manifest.json`
  vs. the vendored subtree — into **two byte copies** a new reconcile gate must keep from drifting. That
  is exactly the restatement [ADR-0058](0058-adopt-one-governing-principle-derive-dont-restate.md) and
  ADR-0062 spent P4 removing; minting a fresh instance of it to buy simplicity trades the authorship
  invariant this ADR exists to protect.
- **Direct SDD↔`.github` scaffold-time fetch** — the literal ask of
  [FS.GG.SDD#620](https://github.com/FS-GG/FS.GG.SDD/issues/620). **Disqualified by constraint, not
  preference:** it requires the offline scaffolder to reach a cross-repo source, the one thing the
  offline loop + FR-002 / SC-005 forbid.

### Consequences of the transport choice

- **Authorship and single-byte-source are both preserved.** `.github` authors *and* is the sole origin
  of the bytes; there is no second copy to drift, so no new reconcile gate — the ADR-0014 package verify
  already guarantees integrity. This is why the package beats the vendored copy on the very axis Decision
  rejected Option C on.
- **One dependency edge is added** — `FS.GG.SDD.Cli → <the driver package>`, plus its pin — on top of the
  `github → sdd` validator edge §5 already records. That edge is the concrete new contract this
  sub-decision creates; the `<!-- §5 contract picture -->` note above gains a dependency edge but no new
  repo, boundary, or coherent-set axis.

### The two build steps, sequenced (publish-before-flip, per [ADR-0037](0037-schema-growth-is-publish-before-flip.md))

Both ride behind §Sequence step 1's CLI (the one that teaches the driver-class vocabulary): nothing lands
until that CLI is live.

1. **`.github` publishes the driver package** — the versioned artifact carrying `workRoadmap`'s bytes +
   `driver-skill-manifest.json`, on the org feed. This *concretises* §Sequence step 2's producer-emitter:
   the emitter's output is a **published package**, not only a reconciled `skills.yml` row.
2. **SDD's scaffold-time materializer consumes the delivered manifest** — filed as a **narrow SDD
   cross-repo request** naming the concrete *delivered* package artifact (pin → restore → materialize into
   the scaffold's skill roots, ADR-0014 verify), **not** a reach into `.github`. **Blocked on step 1's
   publish** — the request cannot name a delivered artifact that does not yet exist.

Existing scaffolds predate the driver, so whether they need `fsgg-sdd upgrade` / re-vendor to backfill is
an SDD-side call folded into step 2's request, not a `.github` decision.

## Consequences

- **`skills.yml` stops being "producer-owned only".** Its long-standing invariant — every row's bytes
  are reconciled from a *producer's* manifest — grows a second legitimate source: `.github`, emitting
  its own authored drivers. This is the schema growth, and it is why B is publish-before-flip rather
  than a registry edit. `.github` gaining a producer emitter is the concrete new machinery.
- **Authorship is preserved.** `workRoadmap` stays authored in `.github`'s two skill roots (ADR-0011);
  the new class is precisely what lets a `.github`-authored skill ride the product-materialization
  fabric without pretending a producer owns it. ADR-0053's "canonical author: `.github`" stands unamended.
- **The class is reusable, and that is the point of paying for it once.** The moment `.github` authors
  a *second* cross-cutting driver that composes producers and must run in product trees, it is another
  `scope: driver` row — no further schema growth. Paying the schema cost once buys the quadrant.
- **`materializes-when` gains a composed (AND-of-producers) predicate in practice.** The predicate
  language already supports it ([ADR-0017](0017-skill-registry-condition-aware-materialization.md)); this
  is its first *cross-producer* use — the driver materializes only where **both** composed producers'
  skills are present, matching ADR-0053 §6's refuse-if-incomplete rule structurally rather than in prose.
- **Nothing lands until step 1's CLI is published.** #1224 said "no code change lands until this is
  decided"; it is decided, and the successor rail is ADR-0037's ordering. The `.github` flip PR is
  blocked on the SDD publish, and the sequenced board items record that edge.

<!-- §5 contract picture: this grows the `skill-registry` schema (a governed contract already in the
picture) with a new owner-source and a `scope: driver` value; it adds no new repo, boundary, coherent-set
axis, contract, or dependency edge beyond the existing `github → sdd` validator edge ADR-0015 §2 records.
The architecture map's contract inventory is unchanged in shape. -->

## Alternatives considered

- **A — mirror through the coordination kit.** Rejected on target: the kit reaches framework repos, not
  scaffolded product trees, so it delivers `workRoadmap` to exactly where §6 makes it refuse. See Decision.
- **C — re-home ownership to FS.GG.SDD.** The only zero-schema-growth option, and the fastest. Rejected
  because it inverts the authorship ADR-0053/ADR-0011 established and makes SDD own a cross-producer
  driver; composition-by-reference does not require it. See Decision.
- **D — leave `.github`-local.** Rejected: never auto-appears where it must run; defeats §6.
- **A `.github`-authored variant of the kit that also targets scaffolds.** Rejected as C-in-reverse: it
  would teach the *coordination* fabric to deliver an SDD-lifecycle driver to product trees, overloading
  the kit's meaning (the very cost that sank A) to avoid touching `skills.yml`. The product-materialization
  fabric already targets scaffolds correctly; the honest growth is there, not in the kit.
