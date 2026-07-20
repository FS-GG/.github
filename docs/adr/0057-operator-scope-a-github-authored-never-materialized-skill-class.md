# ADR-0057: A `.github`-authored, never-materialized *operator* skill is a fourth `skill-registry` class — `drive-board` rides it

- **Status:** Proposed
- **Date:** 2026-07-20
- **Affects:** FS-GG/.github (owns `registry/skills.yml`, the producer emitter for its own authored skills, the `generate-projections` summary, the `contract-coherence.yml` pin, and this ADR); FS.GG.SDD (owns `Fsgg.Registry`, which learns the `operator` vocabulary and publishes the `FS.GG.SDD.Cli` carrying it).
- **Amends:** [ADR-0054](0054-workroadmap-delivery-fabric-a-github-authored-product-materialized-driver.md) — adds a **fourth** class to the three it named (kit / producer-catalog / driver). ADR-0054's `scope: driver` decision is unchanged; this extends the same governed contract with one more owner-authored quadrant. *(The reciprocal `Amended by ADR-0057` marker must be added to ADR-0054 in the resolving PR — a one-sided link is the corpus's most common defect.)*
- **Interacts with:** [ADR-0053](0053-roadmap-driven-milestone-loop-disposable-sdd-subagents.md) (the `workRoadmap` loop `drive-board` is the cross-repo sibling of); [ADR-0015](0015-register-the-registry-schema-as-a-governed-contract.md) (the governed `skill-registry` contract this grows); [ADR-0037](0037-schema-growth-is-publish-before-flip.md) (the publish-before-flip rail the growth rides); [ADR-0017](0017-skill-registry-condition-aware-materialization.md) (the predicate language that already carries `false`).
- **Decides:** the delivery/catalog class for `drive-board` — an org-operator board driver that has **no product tree to be delivered into**.

## Context

`drive-board` is a new `.github`-authored driver skill: the **cross-repo sibling of `workRoadmap`**
([ADR-0053](0053-roadmap-driven-milestone-loop-disposable-sdd-subagents.md)). Where `workRoadmap`
burns down one repo's markdown roadmap milestone by milestone, `drive-board` burns down the org-level
Coordination board across **every** repo — reconciling the board (`check-board`), sizing each repo's
wave, fanning fresh disposable subagents out to run `/pnext-item`, verifying each result against ground
truth, and re-planning. It composes `check-board`, `pnext-item`, and `intra-repo-parallel-work`.

The fact that decides this ADR is **where it runs**: `drive-board` runs **only** from an operator
checkout where every rostered repo is present as a sibling — it `cd`s a worker into `../FS.GG.Rendering`
and worktrees from there. A **single product tree has no siblings**, so `drive-board` cannot run in one
and must never be delivered into one. This is the exact inverse of ADR-0054's `workRoadmap`, whose whole
point is to be delivered *into* a scaffolded product tree.

[ADR-0054](0054-workroadmap-delivery-fabric-a-github-authored-product-materialized-driver.md) named
three delivery classes, and **none fits**, because all three *deliver somewhere* and `drive-board`
stays home:

| class | source | delivers to |
|---|---|---|
| **kit** (`repos.yml` `kit:` → `coordination-sync`) | `.github`-authored | the 8 **framework** repos |
| **producer catalog** (`skills.yml` → `materializes-when`) | **producer**-owned | scaffolded **product** trees |
| **driver** (ADR-0054) | **`.github`**-authored | scaffolded **product** trees |

A `.github`-authored skill that is **cataloged but materialized nowhere downstream** is a fourth
quadrant no existing class occupies. Today `drive-board` sits outside the registry entirely: it is
byte-identical in `.github`'s two agent-skill roots and passes `skill-roots-selfcheck` (which runs the
**bare** union assert — presence + byte-identity, no `--manifest`), so it is green and invokable, but it
carries **no registry row, no projection, no owner/scope of record**. It is uncataloged. The moment a
second operator skill is authored, the question is re-posed with nothing to answer it.

## Decision

**Teach `registry/skills.yml` a fourth class: a `.github`-authored, *never-materialized* skill —
`scope: operator`.** It is authored in `.github`'s two skill roots (ADR-0011), cataloged as a first-class
registry row (`owner: .github, scope: operator`), and **delivered nowhere**: its `materializes-when`
predicate is the literal **`false`**, which the predicate language already provides
([ADR-0017](0017-skill-registry-condition-aware-materialization.md)) and `always`'s exact opposite.

This names the fourth quadrant precisely:

- **kit** = `.github`-authored → **framework** repos
- **producer catalog** = **producer**-owned → **product** trees
- **driver** (ADR-0054) = **`.github`**-authored → **product** trees
- **operator (new)** = **`.github`**-authored → **nowhere** (runs in the owning operator checkout only)

**`drive-board` is its first rider**, exactly as `workRoadmap` was the driver class's first rider.

### The mechanism needs no new gate code

The union gate (`scripts/skill-union-assert.sh`) is **scope-agnostic** — it keys on `id` + `sha256` and
evaluates `materializes-when` against a scaffold's `effectiveParameters`. An operator row with
`materializes-when: "false"` therefore lands in every scaffold's evaluation as **declared ∧
condition-false ∧ absent → a justified omission** — the legitimate class ADR-0017 already defined. No
scaffold ever expects it; none flags it missing. The "never-materialized" behavior is carried by the
**predicate**, not by teaching the gate a scope. The scope value's job is the *catalog*: to say what the
skill is, who owns it, and — via a step-2 enforcement rule — to hold operator rows to `materializes-when:
"false"` so the never-materialize guarantee cannot be edited away by accident.

### Why not the other classes

- **driver — disqualified on *target*, the mirror image of ADR-0054's rejection of the kit for
  `workRoadmap`.** A driver materializes into scaffolded product trees; `drive-board` in a product tree
  is a skill that cannot run — no sibling repos to drive. Riding `driver` ships it to exactly the trees
  where it is inert, and (worse than inert) advertises an org-operator capability in a single-product
  context. This is structural, not taste.
- **kit — same target problem, plus meaning-overload.** The kit reaches framework repos, not the
  operator checkout, and mirroring an org-driver through the *coordination* fabric overloads the kit's
  meaning (the very cost that sank option A in ADR-0054).
- **leave `.github`-local, uncataloged (status quo) — green but invisible.** It works today, but the
  skill has no owner/scope of record, no projection row, and no answer for the second operator skill.
  ADR-0053/ADR-0054 established that this org says the true thing **structurally** rather than leaving a
  real quadrant unnamed (cf. the `outside-fabric` list, so "deliberately outside" is *sayable*). An
  operator skill deliberately delivered nowhere is exactly that shape: name it, or the next reader
  cannot tell "cataloged as never-delivered" from "someone forgot to register it."
- **a dedicated `never` predicate token — unnecessary machinery.** `false` is already in the grammar
  (ADR-0017), evaluates identically in the shell gate and `Fsgg.Registry`, and reads correctly. Adding a
  synonym would be two spellings of one fact.

### The cost, accepted deliberately

Like ADR-0054, this is **schema growth on the governed `skill-registry` contract**
([ADR-0015](0015-register-the-registry-schema-as-a-governed-contract.md)) — a new `scope` value — so it
is **publish-before-flip: two ordered PRs, SDD first** ([ADR-0037](0037-schema-growth-is-publish-before-flip.md)).
The `scope` allow-list is `Fsgg.Registry`'s (`src/FS.GG.Contracts/Registry.fs`, `skillScopes`), shipped as
`FS.GG.SDD.Cli` and pinned by `.github` at `contract-coherence.yml`; the current pin (`0.17.0`) **rejects**
an unknown scope exactly as `0.15.0` rejected `driver`. So the vocabulary ships from FS.GG.SDD first, and
the `.github` flip advances the pin onto it.

### The sequence (derived from ADR-0037)

1. **FS.GG.SDD (step 1 — publish).** Teach `Fsgg.Registry` the `operator` token — add `"operator"` to
   `skillScopes` (`Registry.fs`) and to the unknown-scope message, and add the operator-shape
   **enforcement** (an `operator` row MUST carry `materializes-when: "false"` and `owner: .github`),
   gated — like the driver-shape rule — to the **bumped** `schemaVersion` so the validator still accepts
   `.github` HEAD verbatim (ADR-0037 §3). Accepting `operator` is monotonic: the token growth rejects
   nothing the live document contains. **Publish an `FS.GG.SDD.Cli`** (e.g. `0.18.0`) carrying it.

2. **`.github` (step 2 — flip), one PR after the CLI is live:**
   - extend the producer emitter (`scripts/generate-driver-manifest`) from *driver-only* to
     *`.github`-authored non-kit skills*, and add the `drive-board` declaration
     (`scope: operator, materializes-when: "false"`); regenerate `registry/driver-skill-manifest.json`;
   - add the reconciled `drive-board` row to `registry/skills.yml`
     (`owner: .github, scope: operator, materializes-when: "false"`);
   - bump `skills.yml` `schemaVersion` **and** the `skill-registry` contract `version` in
     `registry/dependencies.yml`, and add `operator` to that contract's `surface:` enumeration;
   - advance the `contract-coherence.yml` `FS.GG.SDD.Cli` pin to the step-1 CLI;
   - teach `scripts/generate-projections` to count `operator` in the summary line (its per-scope table
     already self-adapts via `group_by`), regenerate `docs/registry/compatibility.md`, and keep the
     field-vocabulary comment + `registry/skills.CHANGELOG.md` current.

   Between the two PRs the pin advances **ahead** of the bump (ADR-0037 §Context): that intermediate
   state is *checked*, and its one red (`pin-coherence`, self-clearing via Renovate's bump PR) is honest.
   The reverse order (flip then publish) is silently green over a schema nothing checks — rejected there,
   rejected here.

## Consequences

- **`skills.yml` gains a fourth quadrant, and the class is reusable.** A second `.github`-authored
  operator skill is another `scope: operator` row — no further schema growth. Paying the schema cost once
  buys the quadrant, exactly as ADR-0054 argued for `driver`.
- **"Never materialized" reuses existing machinery.** The `false` predicate + the scope-agnostic union
  gate already produce a justified downstream absence; no gate learns a scope. The only new *enforcement*
  is the step-1 rule binding an `operator` row to `materializes-when: "false"`, so the guarantee is
  structural rather than a convention a future edit can break.
- **The emitter generalizes.** `generate-driver-manifest` becomes the emitter for **`.github`-authored
  non-kit skills** (driver *and* operator); its name and docstring follow the broadened role. `.github`'s
  producer-emitter half of `registry = manifest = bytes` now spans two scopes.
- **`drive-board` is canonized as the first operator skill.** Its loop protocol (reconcile → size wave →
  fan out `/pnext-item` → verify against ground truth → re-plan → terminate via `check-board`) lives in
  its `SKILL.md`; a future ADR may formalize that protocol as ADR-0053 did for `workRoadmap`. This ADR
  decides its **class**, not its protocol — the same split ADR-0053/ADR-0054 drew.
- **Nothing lands until step 1's CLI is published.** The `.github` flip is blocked on the SDD publish;
  the sequenced board items record that edge. Until then, `drive-board` remains the green, invokable,
  uncataloged `.github`-local skill it is today.

<!-- §5 contract picture: this grows the `skill-registry` schema (a governed contract already in the
picture) with one new `scope` value (`operator`) on the existing `.github` owner-source ADR-0054 already
added; it adds no new repo, boundary, coherent-set axis, contract, or dependency edge beyond the existing
`github → sdd` validator edge ADR-0015 §2 records. The architecture map's contract inventory is unchanged
in shape. -->

## Alternatives considered

- **`scope: driver`.** Rejected on target: a driver materializes into product trees, where `drive-board`
  cannot run (no sibling repos). The mirror image of ADR-0054's rejection of the kit for `workRoadmap`.
- **Mirror through the coordination kit.** Rejected: reaches framework repos, not the operator checkout,
  and overloads the kit's meaning to avoid touching `skills.yml`.
- **Leave `drive-board` `.github`-local and uncataloged.** The status quo — green, but no owner/scope of
  record and no answer for the next operator skill. Rejected on the "say it structurally" preference.
- **A dedicated `never` predicate token.** Rejected as a synonym for `false`, which the grammar already
  carries and both evaluators already agree on.
