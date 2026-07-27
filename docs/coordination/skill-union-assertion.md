# The skill-union assertion

A single reusable check — [`scripts/skill-union-assert.sh`](../../scripts/skill-union-assert.sh),
wrapped by the [`skill-union-assert.yml`](../../.github/workflows/skill-union-assert.yml)
(`workflow_call`) workflow — that any consumer CI calls to prove a scaffolded workspace's
agent-skill roots are the **byte-identical union** of process + product skills. It is the
**consumer-side arm** of [ADR-0014](../adr/0014-skill-vendoring-one-manifest-one-materialize-verify.md)'s
content-addressed design, delivered by [.github#111](https://github.com/FS-GG/.github/issues/111)
(epic [#110](https://github.com/FS-GG/.github/issues/110), roadmap phase **P3.G3.1**). It mirrors
the [contract-coherence gate](contract-coherence-gate.md) and the
[dispatch-sender](auto-update-fabric.md) reusable pattern: one script authored in FS-GG/.github,
one thin `workflow_call` wrapper, wired into a caller with a single `uses:` block.

## Why

ADR-0014's four-repo audit (finding **F2**) found the skill-vendoring apparatus verified
**presence only** — `doctor`/`upgrade` checked `Option.isSome`, the composition gate asserted
*nothing* about the roots, and `scaffold-provenance` carried no digest. So a root that exists but
has **drifted bytes**, a provider skill **missing from one root**, or a `.codex` that **diverges**
from `.claude` were all invisible. The apparatus that exists to guarantee "the three roots are the
byte-identical union" never checked that they were. This assertion is that missing check, made
reusable so *every* lane (orchestrated `fsgg-sdd` **and** standalone template) asserts it where
skills are consumed.

## What it checks

Over the configured `AGENT_SKILL_ROOTS` ([which roots?](#which-roots--a-tree-declares-its-own-set-517)
— default ADR-0011's three: `.claude/skills`, `.codex/skills`, `.agents/skills`), for **every** skill
in the union it asserts:

1. **present** — the skill directory exists in **every** root (a miss is a *partitioned* root);
2. **byte-identical** — its bytes are identical across **every root it is present in** (a diff is a
   *divergent* root). Checks 1 and 2 are **independent questions and both are always asked**: a skill
   can be *partitioned* **and** *divergent*, and is then reported as both. Check 1 used to
   short-circuit — see [a partition never suppresses the byte
   comparison](#a-partition-never-suppresses-the-byte-comparison-1506);
3. **matches-manifest** *(only with `--manifest`)* — if the producer's
   [skill-manifest](#the-manifest-and-the-canonical-digest) declares it, its `SKILL.md` digest
   equals the declared digest (*drifted*); if the manifest does **not** declare it, it must match
   a `--co-tenants` glob (*dangling* otherwise).
4. **condition-aware** *(only with `--manifest` **and** `--params`)* — evaluates each declared
   skill's `materializes-when` against the scaffold's `effectiveParameters`
   ([the condition-aware manifest](#condition-aware-check-4----params-adr-0017), ADR-0017), adding
   *missing* (declared ∧ condition true ∧ absent everywhere) and *unexpected* (present ∧ condition
   false). Without `--params` the gate keeps the check-3 superset semantics exactly.

Checks 1–2 are **self-contained** — they need nothing but the workspace tree. Check 3 cross-checks
the manifests the producers actually ship ([FS.GG.SDD#61](https://github.com/FS-GG/FS.GG.SDD/issues/61) /
[FS.GG.Rendering#43](https://github.com/FS-GG/FS.GG.Rendering/issues/43), ADR-0014 P0–P2), in
**their** semantics — aligned by [.github#120](https://github.com/FS-GG/.github/issues/120):
`Fsgg.SkillMirror` (FS.GG.Contracts 1.4.0) is ADR-0014's "one implementation", so the assertion
follows it, not vice versa. Check 4 is the ADR-0017 tightening — see below.

## The manifest and the canonical digest

The producer manifest is JSON — ADR-0014's `{ id, scope, sha256, body }` per skill:

```json
{ "roots": [".claude/skills", ".codex/skills", ".agents/skills"],
  "skills": [
    { "id": "cross-repo-coordination", "scope": "process", "sha256": "<SKILL.md body sha256>" },
    { "id": "fs-gg-ui-render",          "scope": "product", "sha256": "<SKILL.md body sha256>" }
  ] }
```

**Digest.** The `sha256` is the **canonical-body sha256 of the skill's `SKILL.md` only** — the
algorithm `Fsgg.SkillMirror` ships (byte-equivalent to `sha256sum SKILL.md`, verified in
[.github#120](https://github.com/FS-GG/.github/issues/120)). Multi-file skills (`SKILL.md` +
`references/**`) are covered by the **cross-root identity** of checks 1–2, not by the digest.
The assertion exposes the algorithm as a **reference generator** so producers and checker never
drift:

```sh
scripts/skill-union-assert.sh --digest .claude/skills/<id>   # prints the canonical digest
```

**Set semantics.** The manifest is a **superset catalog** — an *upper bound*, not an exact set.
Producers declare every skill they can emit, but emission is lifecycle/profile-conditioned, so:

- **declared ∧ present** → the digest must match (else `[drifted]`);
- **declared ∧ absent from every root** → legitimate (skipped, surfaced in the summary count);
- **declared ∧ present in only some roots** → still `[partitioned]` (check 1);
- **present ∧ undeclared** → `[dangling]`, **unless** the id matches a `--co-tenants` glob —
  the roots legitimately hold process skills from co-tenant producers the product manifest
  doesn't own (e.g. `--co-tenants "fs-gg-sdd-* speckit-*"` for the sdd / spec-kit lanes).

The one gap this leaves: **declared ∧ absent from every root is *blanket*-tolerated** — the manifest
records no reason a skill was skipped, so a genuinely-dropped skill is indistinguishable from an
intentional off-profile omission. That is exactly how a supply failure (`fs-gg-project`) shipped
unnoticed. Check 4 closes it.

### A co-tenant need not have a producer at all — the repo-native class (#1509)

Process, product and kit skills all have an **external producer** to verify against: a manifest, a
`.specify/` tree, the coordination kit. A fourth class lives in the committed roots and has none — a
skill **whose owner is the receiver repo itself**, authored there, with no producer manifest and no
`registry/skills.yml` row. Call it **repo-native**, and treat it as a legitimate co-tenant rather than
a defect: it is undeclared because nothing declares it, not because it drifted in.

`spectre-console` is the worked example. It was authored in **FS.GG.Governance** by that repo's own
specs 091/093, its `SKILL.md` frontmatter carries `metadata.source` recording exactly that provenance,
and — measured 2026-07-27 — it is committed to the runtime roots of `FS-GG/.github`,
`FS.GG.Governance` and `FS.GG.SDD` while `registry/skills.yml` (50 rows) carries **no row for it** and
no producer manifest declares it.

Two consequences, and both have already cost work:

1. **A repair driven from "the authoritative producer" silently leaves it behind.** Rematerializing a
   partitioned tree from `.specify/` fixes every id that came from `.specify/` and no others, so a
   repo-native co-tenant stays partitioned — and the tree stays red under a gate that was just made
   required. This is not hypothetical; it is what both completed rollout repairs ran into. See
   [Rollout state](#rollout-state-measured-2026-07-27) below.
2. **A `--co-tenants` glob set is not automatically wide enough.** The example above,
   `"fs-gg-sdd-* speckit-*"`, matches neither `spectre-console` nor any other repo-native id, so a
   caller that passes a `--manifest` over a tree holding one gets `[dangling]` for a skill that is
   supposed to be there. A tree with repo-native skills must name them (or a glob covering them) in
   its own `--co-tenants`.

Neither consequence weakens checks 1–2: a repo-native skill is still required to be in **every** root
and byte-identical across them. Having no producer excuses it from the *digest* cross-check, never
from the union.

## Condition-aware (check 4) — `--params` (ADR-0017)

[ADR-0017](../adr/0017-skill-registry-condition-aware-materialization.md) makes absence *checkable*
by recording the **emission condition** on each manifest entry — an optional `materializes-when`
predicate (absent ⇒ `always`):

```json
{ "skills": [
    { "id": "fs-gg-scene",   "scope": "product", "sha256": "…", "materializes-when": "profile in [app, headless-scene, governed, sample-pack, game]" },
    { "id": "fs-gg-project", "scope": "product", "sha256": "…", "materializes-when": "lifecycle == spec-kit" }
  ] }
```

Pass the scaffold's own parameters with **`--params <scaffold-provenance.json>`** (read from
`.effectiveParameters`) and the assertion evaluates each declared skill's predicate, turning
"declared ∧ absent" from blanket-tolerated into **justified**. Two classes are added to the four
above:

| declared? | condition | materialized? | verdict |
|---|---|---|---|
| yes | true  | yes | check `sha256` → `[drifted]` if mismatch, else **ok** |
| yes | true  | **no**  | **`[missing]`** — FAIL (new; catches the dropped `fs-gg-project`) |
| yes | false | **yes** | **`[unexpected]`** — FAIL (new; materialized off-profile) |
| yes | false | no  | legitimate — *justified* off-profile absence (was: blanket-tolerated) |
| no  | —     | yes | `[dangling]` unless `--co-tenants` admits it (unchanged) |

`[partitioned]` / `[divergent]` (the cross-root checks 1–2) are independent of conditions and
unchanged. **Without `--params` the gate keeps today's superset semantics exactly**, so adoption is
opt-in per caller — no consumer is forced to change at once. `--params` **requires** `--manifest`
(the conditions live on the manifest entries; `--params` only supplies the values). The
[FS.GG.Templates composition gate](https://github.com/FS-GG/FS.GG.Templates/issues/49) is the first
caller to pass provenance, enforcing `[missing]`/`[unexpected]` in both lanes.

**The predicate language** is deliberately tiny — evaluable in both the shell gate
(`eval_condition`) and the Python drift-normalizer (`normalize_when`) without a real expression
engine, and a shared fixture table pins the two together so they never drift
([`tests/skill-union/conformance.sh`](../../tests/skill-union/conformance.sh)):
`always` · `<param> == <value>` · `<param> != <value>` · `<param> in [<v>, <v>, …]` · clauses joined
by `and` / `or` (`and` binds tighter; no parentheses). Values and params are bare tokens
(`[A-Za-z0-9_-]`, plus `true`/`false`); a param absent from the provenance reads as empty. The
authoritative catalog of ids + conditions is the org skill registry `registry/skills.yml` (ADR-0017),
generated from the producer manifests.

## Usage

Directly:

```sh
scripts/skill-union-assert.sh --product <product-dir> \
  [--roots ".claude/skills .codex/skills .agents/skills"] \
  [--manifest <manifest.json>] \
  [--co-tenants "fs-gg-sdd-* speckit-*"] \
  [--params <scaffold-provenance.json>]        # enables the condition-aware check 4 (needs --manifest)
```

`AGENT_SKILL_ROOTS` (env) overrides the default root set — ADR-0014's "one declared constant":
adding a runtime root is a one-line change, no per-repo source edits. Exit `0` = the roots are the
byte-identical union; non-zero = at least one violation, each printed with its class
(`[partitioned]` / `[divergent]` / `[dangling]` / `[drifted]` / `[missing]` / `[unexpected]`).

### Which roots — a tree declares its own set (#517)

ADR-0065 gives both delivery lanes the same default root set:

| Lane | Roots | Written by |
| --- | --- | --- |
| **Scaffolded product** | `.claude/skills` `.codex/skills` `.agents/skills` (ADR-0011's three) | `fsgg-sdd`, the sole mirror authority |
| **Kit consumer** (FS-GG's own repos, incl. `.github`) | `.claude/skills` `.codex/skills` `.agents/skills` | `FS.GG.Kit` / `coordination-sync` |

The roots resolve in this order; a tree declares a set only when it intentionally overrides the
universal default:

1. `--roots` — explicit; what the reusable workflow passes.
2. `$AGENT_SKILL_ROOTS` — the env knob, shared with `coordination-sync`.
3. `<product>/.agent-skill-roots` — **checked in**; whitespace/newline-separated, `#` comments allowed.
4. ADR-0011's three — the scaffolded-product default.

`FS-GG/.github` ships such a declaration, which is why `scripts/skill-union-assert.sh` with **no
arguments** is green on a clean tree here, and why `skill-roots-selfcheck.yml` runs that exact bare
command — the command CI runs is the command a worker runs.

**An absent root stays a hard exit `2` at every level.** Declaring roots narrows *what is asked for*;
it never weakens the answer. This is why the fix for #517 was a declaration rather than dropping
`.codex/skills` from the default: a scaffolded product whose producer never materialized `.codex/`
must keep failing — catching exactly that is the gate's reason to exist (ADR-0011's origin bug), and
a default that no longer asks for the root would be the fail-*open* pattern of #266/#292.

**The declaration is for a tree asserting *itself*, never for a tree being audited.** The reusable
`skill-union-assert.yml` gate therefore **always passes `--roots` explicitly** and never consults the
product's `.agent-skill-roots`. The tree it audits is producer-*generated* (`fsgg-sdd` and the
`fs-gg-ui` template write it), whereas the `roots:` input is human-authored by the consumer repo —
so honouring a declaration found *inside the subject* would let a template bug that emitted one
silently switch `.codex/skills` off and turn the gate green on a partitioned product. The roots come
from the caller; the declaration serves the bare local run and a repo's own selfcheck.

## Subset coherence is not union coherence (#1504)

Two org gates check the three-root invariant, they are **not** the same claim, and reading one as the
other cost the org three partitioned repositories:

| gate | subject | what a green means |
| --- | --- | --- |
| [`coordination-coherence.yml`](../../.github/workflows/coordination-coherence.yml) (`receives: coordination-kit`) | the **kit-owned subset** — exactly the skills `registry/repos.yml`'s `kit:` block names (today: `cross-repo-coordination`, `intra-repo-parallel-work`, `check-board`, `pnext-item`) | those four skills are materialized, in every root, byte-identical to canonical |
| this assertion, wired as `receives: skill-union` | the **complete runtime-visible union** — every skill in the repo's committed `.claude/skills`, `.codex/skills`, `.agents/skills` | *every* skill in the union is present in every root and byte-identical across them |

`coordination-coherence` cannot see a co-tenant skill: it is not in the `kit:` block, so it is not in
that gate's subject. The three trees that cost, **each measured at the commit named** rather than "on
`main`" — Governance `9243c07` `.claude=15 .codex=4 .agents=4`; SDD `f419f0e` `.claude=32 .codex=21
.agents=4`; Rendering `ee5e6c3` `.claude=50 .codex=4 .agents=50`. Projections were **missing**, for
11, 28 and 46 skills, and all three were green on `coordination-coherence` throughout, because the
four kit skills really are coherent in all three of their roots.

Two of those three have since been repaired. **The current numbers are stated once, in Rollout state
below, and nowhere else** — a present-tense count restated in a second place is how this document
aged into three wrong issue bodies.

The sentence that stood here also said *"with every multi-root skill byte-identical"*, and it was
**wrong** — read off a summary line that had not checked ([#1506](https://github.com/FS-GG/.github/issues/1506)).
Rendering's 46 partitioned ids were present in **two** roots each and 30 of them **differ** between
`.claude/skills` and `.agents/skills`. The gate held those bytes and never compared them, because check 1
short-circuited; see below.

[ADR-0065](../adr/0065-one-agent-skill-root-contract.md)'s receiver rollout
([#1016](https://github.com/FS-GG/FS.GG.Rendering/issues/1016),
[Governance#298](https://github.com/FS-GG/FS.GG.Governance/issues/298),
[SDD#669](https://github.com/FS-GG/FS.GG.SDD/issues/669)) proved `coordination-coherence` and described
that subset result as restored three-root coherence. The co-tenant process and product skills those
trees had been populated with by earlier writers were outside the materializer **and** outside its
acceptance check, so they survived the migration unexamined. So: **a `coordination-coherence` green is
evidence about four skills. Only a `skill-union` green is evidence about the tree.**

`tests/skill-union/run.sh` pins the distinction as a pair of legs — the kit-owned four-skill subset
passes on its own, and the *same* coherent subset plus one partitioned co-tenant (process **or**
product) fails `[partitioned]` — including when the partitioned skill's bytes are identical wherever it
does appear, because the defect is a missing projection and not a divergent byte.

## A partition never suppresses the byte comparison (#1506)

Check 1 used to **short-circuit**: a `[partitioned]` id was skipped past check 2, so the copies it *did*
have were never diffed — and `byte-identical=` then counted only the ids that survived to the
comparison. On `FS.GG.Rendering@main` the gate printed

```
::error::[partitioned] skill 'fs-gg-layout' is absent from root(s): .codex/skills
  … 46 such lines …
skill-union-assert: 50 skill(s) — present=4 byte-identical=4
```

Both numbers are true, and both are statements about **4 skills out of 50**. Read without opening the
log, `present=4 byte-identical=4` says *"the comparable skills are fine and nothing is divergent"* — and
that reading became the stated central premise of two downstream issues
([FS.GG.Rendering#1080](https://github.com/FS-GG/FS.GG.Rendering/issues/1080): *"Nothing is divergent.
Every skill present in more than one root is byte-identical"*), whose entire repair plan was sized
against it. Direct measurement: **30 of Rendering's 50 ids differ** between `.claude/skills` and
`.agents/skills`, the two roots that both hold them. This is the [#266](https://github.com/FS-GG/.github/issues/266)
family — *"I could not check"* rendered as *"I checked, and it is fine."*

Two things changed, and the second is the one that matters:

1. **Both checks always run.** Absence and drift are independent facts about an id. A partitioned skill
   is byte-compared across the roots it *is* present in, with the first **present** root as reference, so
   an id can report `[partitioned]` *and* `[divergent]`. Nothing is reclassified: a partition is still
   `[partitioned]`, exit is still `1`, and a partition of byte-identical copies still emits **no**
   `[divergent]`.
2. **Every count in the summary carries the population it was taken over**, so a partial comparison
   cannot masquerade as a complete one:

```
skill-union-assert: 50 skill(s) — in-every-root=4/50 partitioned=46 | byte-comparable=50 byte-compared=50 byte-identical=20/50 byte-differing=30 single-root=0
```

| field | meaning |
| --- | --- |
| `in-every-root=<n>/<union>` | ids that passed check 1 — the whole ones |
| `partitioned=<n>` | ids that failed check 1 |
| `byte-comparable=<n>` | ids with copies in **≥2 roots** — the population check 2 *can* examine |
| `byte-compared=<n>` | ids check 2 **did** examine. Unequal to `byte-comparable` ⇒ the gate emits an `::error::` and **fails**: a summary that overstates its own coverage is the defect, not a cosmetic flaw |
| `byte-identical=<n>/<compared>` | never a bare count. `20/50` cannot be read as covering 50 |
| `byte-differing=<n>` | ids whose copies differ (each also printed as `[divergent]`) |
| `single-root=<n>` | ids present in exactly **one** root — genuinely **not comparable**, and never folded into `byte-identical`. *"Nothing to compare"* must not render as *"compared, and identical"* |

`single-root` also closes the same hole one root down: with a **single-root** root set the old code's
"compare against roots 2..n" loop was empty, fell through, and counted every id `byte-identical` over a
comparison that never ran.

`tests/skill-union/run.sh` pins all of it — a tree that is partitioned **and** divergent reporting both
diagnostics for the same id at exit 1, all six of that tree's partitioned ids compared rather than just
the first, the summary asserted **byte-for-byte**, a regex refusing any denominator-free
`byte-identical=` on any tree, a byte-identical partition still accounted `partitioned=1
byte-differing=0`, a one-root skill counted `single-root`, and a single-root **root set** claiming `0/0`
rather than byte-identity. **All seven legs fail against the pre-fix script**, which is what makes them a
regression test rather than a description.

## Adoption — wiring it into a consumer repo's CI

There are **two** kinds of caller, and the roster's `caller: skill-union` detector deliberately tells
them apart. Passing `product-path: <subdir>` audits a **generated product**; leaving it at its default
audits the **repository's own committed roots**. A `uses:` of this workflow does not say which, which is
exactly why the [repo roster](../../registry/repos.yml) declares this capability with a compound
`caller:` detector rather than a bare `workflow:` one — the latter would certify the full-union
capability off a call that never looks at the receiver's roots ([#628](https://github.com/FS-GG/.github/issues/628)).

### The required receiver caller — a framework repo's own committed roots

This is the shape `receives: skill-union` means, and `scripts/repos-audit.sh` requires **both halves in
one workflow file**. Copy it verbatim:

```yaml
# .github/workflows/skill-union.yml
name: skill-union
on:
  # DELIBERATELY UNFILTERED — do not add a `paths:` filter here. This context is REQUIRED on the
  # default branch, and a required check that does not report on every PR blocks the repo. See
  # "Why the pull_request trigger carries no paths: filter" below before you tidy this.
  pull_request:
  push:
    branches: [main]
    paths:
      - ".claude/skills/**"
      - ".codex/skills/**"
      - ".agents/skills/**"
      - ".github/workflows/skill-union.yml"
  workflow_dispatch:

permissions:
  contents: read

jobs:
  skill-union:
    uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main
    # No `with:` at all is the most correct form: `product-path` defaults to "." (the repository root)
    # and `roots` to ADR-0011's three. Writing them out is equivalent, and narrowing either is not.
```

**Half 1 — the call is aimed at your own roots.** `product-path` absent or `.`; `roots` absent or naming
all three. A call aimed at a subdirectory is a generated-product audit and does not satisfy this
capability; a narrowed `roots:` is a smaller audit than the capability claims.

**Half 2 — the gate is armed.** The `pull_request` trigger must fire when any of the three roots
changes. `repos-audit` accepts two ways of satisfying that — a `paths:` filter covering all three, or
no `paths:` filter at all — but **only the second is compatible with making the context required**, so
the block above uses it and this section prescribes it. A `paths-ignore:` naming a root disarms it. A
`push`-only workflow reports nothing on a pull request, so it can never be the required check.
**An unarmed gate is not a gate** — the
[#332](https://github.com/FS-GG/.github/issues/332)/[#334](https://github.com/FS-GG/.github/issues/334)/[#880](https://github.com/FS-GG/.github/issues/880)
class, where the check is correct and its trigger is what fails open.

Both halves must be in the *same* file, because a trigger cannot arm a workflow it is not in: the roots
change, the other workflow runs, and the one that audits them does not.

**Write it however YAML lets you.** `repos-audit` **parses** the workflow (`yq`, else `python3`+PyYAML)
rather than grepping it, so key order, flow mappings (`with: {product-path: "."}`), inline sequences
(`paths: [".claude/skills/**", …]`), anchors/aliases, comments and indentation style are all equivalent.
`paths:` coverage is real glob matching, so a *broader* filter passes — `.claude/**` and `**/skills/**`
arm the gate — while `.claude/skills-archive/**` does not, and a `!`-negated entry subtracts. This is not
a convenience: the detector began as a line scanner, and five legal YAML shapes went through it in one
review, two of them fail-open. `product-path: ${{ … }}` is *not* accepted — an expression is not a value
the detector can resolve, and an unresolvable subject fails closed.

**Make the resulting context required** on the receiver's default branch. GitHub names it
`skill-union / skill-union` (`<caller job> / <callee job>`) — see
[reusable-workflow-contract](reusable-workflow-contract.md), and note that the callee job id is
**public API** the authority may not rename without sequencing it.

#### Why the `pull_request` trigger carries no `paths:` filter

**Because the context above is REQUIRED, and GitHub does not skip a required check whose workflow a
path filter excluded — it never creates the check run at all.** Branch protection cannot tell that
apart from a check that has not reported yet, so it holds the pull request at *"Expected — waiting for
status to be reported"*, indefinitely. Filtering the trigger and requiring the context are therefore
**incompatible instructions**, and a receiver that did both would block **every PR that does not touch
a skill root** — which is most of them, in a repo whose protection is typically `enforce_admins: true`
with no bypass. (The repair PR itself would squeak through, because the old filter listed
`.github/workflows/skill-union.yml` and so fires on a PR editing that file — but only a PR that edits
*that* file, which is a narrow escape hatch to be holding a whole repository open with.)

This section used to prescribe exactly that pair ([#1504](https://github.com/FS-GG/.github/issues/1504)),
and seven rostered receivers were queued behind it. Governance reached the wiring step first and
filed [#1508](https://github.com/FS-GG/.github/issues/1508); SDD, holding `admin: true`, independently
declined to arm the context for the same reason. Nothing mechanical stopped either of them — which is
the second half of that fix, below. **Both refusals were correct and both are now resolved the same
way**: SDD went on to wire the unfiltered form and arm the context (`a066e0b`, measured 2026-07-27),
which is the shape prescribed above.

**Two repairs exist, and this doc takes the first:**

1. **Drop the `paths:` filter, so the job always runs and always reports.** What the block above does.
   It costs a runner-minute of static shell per PR — the assertion needs no SDK, no restore and no
   network — and it is the shape the org already relies on: [#1508](https://github.com/FS-GG/.github/issues/1508)
   reports that every context Governance requires today is produced by `gate.yml` or
   `coordination-coherence.yml`, and both of those were confirmed on 2026-07-27 to carry **no
   `pull_request` `paths:` or `paths-ignore:` filter at all**. (The producing side is readable without
   credentials; which contexts are *required* needs `administration: read`, so that half is #1508's
   report rather than a re-measurement here.) `repos-audit`'s detector reads an absent `paths:` as
   armed (Half 2), so this costs nothing in capability terms either.
2. Keep the filter and add a **no-op twin** reporting the same context for the excluded paths — a
   second workflow filtered by the exact complement (`paths-ignore:` naming the same roots), whose job
   id and callee job id match, so it derives the byte-identical `skill-union / skill-union`. This is
   GitHub's own documented remedy ("Handling skipped but required checks") and **it does work here**,
   including for a nested `uses:`-shaped context: the duplicate job id lives in a *second file*, so
   nothing forbids it. It is rejected on cost, not on possibility — it needs a second caller, a no-op
   callee added to *this* repo, and two filter lists that must stay exact complements forever, across
   seven receivers, or the gate silently stops covering some PRs. Option 1 needs none of that.

   Do not read "possible" as "supported": nothing in this repo ships that no-op callee, so a receiver
   choosing option 2 is building and maintaining it themselves.

Keeping the filter on `push:` is deliberate and safe: a `push` filter has no bearing on what a pull
request reports, and the required context is a PR check.

**No receiver is left carrying the filtered form.** Re-measured 2026-07-27 over each rostered
receiver's `main`: **one** of the seven now has the caller — `FS.GG.SDD` at `a066e0b`, and it carries
a bare `pull_request:` with **no `paths:` key**, with `skill-union / skill-union` required on its
default branch. The other six ship no `.github/workflows/skill-union.yml` at all, so correcting the
block here still lands ahead of their adoption. (`FS.GG.Governance` has one in flight on
[Governance#329](https://github.com/FS-GG/FS.GG.Governance/issues/329); it was not on `main` at the
time of measurement.) A receiver that wired the filtered form on a branch must drop the
`pull_request` `paths:` filter **before** arming the context, not after — arming first is the
deadlock, and it takes the un-arming PR down with it.

**This is now asserted, not remembered.**
[`scripts/check-required-contexts.py`](../../scripts/check-required-contexts.py) reports a required
context whose only producer is path-filtered, naming the workflow, the event and the filter key. It
previously asked only whether a producing workflow *triggers on* `pull_request` — a strictly wider
question than whether the context *reports on every* pull request, and the filtered-and-required
combination sat in the gap. `required-context-coherence.yml` sweeps the roster nightly with it.

It is deliberately conservative about option 2: the complementary `paths: P` / `paths-ignore: P` twin
above is recognised and passes, and any *other* all-filtered arrangement is a no-verdict (exit 3)
rather than a guess, because general glob coverage is not computable from the YAML. What it does not
yet model is `branches:` and `types:`, which starve a required context the same way — tracked as
[#1519](https://github.com/FS-GG/.github/issues/1519), and named in the checker's own docstring so the
gap is not mistaken for coverage.

**`FS-GG/.github` is not a receiver.** It is the *source* of the assertion and asserts its own roots with
[`skill-roots-selfcheck.yml`](../../.github/workflows/skill-roots-selfcheck.yml), running the bare
command a worker runs. Running your own gate is not participating in your own fabric, so the detector
does not match it and the roster does not claim it.

### Auditing a generated product

```yaml
permissions:
  contents: read
jobs:
  skill-union:
    uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main
    with:
      product-path: "path/to/scaffolded/product"
      # roots: ".claude/skills .agents/skills"                 # default = ADR-0011's three, always
      #                                                        # passed; the product's own
      #                                                        # .agent-skill-roots is NOT consulted here
      # manifest: "path/to/skill-manifest.json"                # enables the digest cross-check
      # co-tenants: "fs-gg-sdd-* speckit-*"                    # undeclared co-tenant ids to admit
      # params: ".fsgg/scaffold-provenance.json"               # enables [missing]/[unexpected] (needs manifest)
```

The [FS.GG.Templates composition gate](https://github.com/FS-GG/FS.GG.Templates/issues/49)
(roadmap **T3.2**) is the first caller — it invokes this for the orchestrated **and** standalone
lanes, replacing the current "grep for the failure string and skip" (ADR-0014 F2, consumer half). It is
a generated-product caller, so it does **not** satisfy Templates' own `receives: skill-union` row: those
are different subjects, and the roster's detector says so out loud rather than counting one as the other.

### Rollout state (measured 2026-07-27)

**The audited trees are the org's rostered repositories — eight of them**, one committed tree each, and
enumerating them here is deliberate: "how many trees are audited?" must have an answer that is *read*
rather than counted from memory. **Every row carries the commit it was measured at**, because a row
without one is a summary, and summaries from this document have misdirected three repairs already. Each
was produced by running, over a fresh clone at that commit:

```sh
scripts/skill-union-assert.sh --product <tree> --roots ".claude/skills .codex/skills .agents/skills"
```

| tree | at | union | `.claude` / `.codex` / `.agents` | verdict |
| --- | --- | --- | --- | --- |
| `.github` (authority — asserts itself) | `9bb9856` | 13 | 13 / 13 / 13 | coherent |
| `FS.GG.Templates` | `754eaad` | 4 | 4 / 4 / 4 | coherent |
| `FS.GG.Game` | `84fb307` | 21 | 21 / 21 / 21 | coherent |
| `FS.GG.Audio` | `2df9e1d` | 20 | 20 / 20 / 20 | coherent |
| `FS.GG.Net` | `9e5f757` | 4 | 4 / 4 / 4 | coherent |
| `FS.GG.Governance` | `3a3aca2` | 15 | 15 / 15 / 15 | coherent — **repaired**, `9d8359c` |
| `FS.GG.SDD` | `a066e0b` | 32 | 32 / 32 / 32 | coherent — **repaired**, and the first wired receiver |
| `FS.GG.Rendering` | `ee5e6c3` | 50 | 50 / 4 / 50 | **46 partitioned** *and* **30 divergent** — see below |

**Seven of the eight are coherent. `FS.GG.Rendering` is the one that is not.**

**This table used to end "Nothing is `[divergent]`: every skill present in more than one root is
byte-identical." That was false, and it was false because the gate had not looked**
([#1506](https://github.com/FS-GG/.github/issues/1506) — a `[partitioned]` id short-circuited past the
byte comparison, and `byte-identical=4` then counted only the 4 ids that reached it). Over the roots
each id **is** present in, at the same commits:

| tree | at | comparable (≥2 roots) | identical | **differing** | single-root (not comparable) |
| --- | --- | --- | --- | --- | --- |
| `FS.GG.Governance` | `3a3aca2` | 15 of 15 | 15 | 0 | 0 |
| `FS.GG.SDD` | `a066e0b` | 32 of 32 | 32 | 0 | 0 |
| `FS.GG.Rendering` | `ee5e6c3` | 50 of 50 | 20 | **30** | 0 |

Rendering's repair is **both** kinds: 46 missing projections *and* 30 divergent pairs. A
byte-comparison-only checker would miss the partitions; the checker that short-circuited on partitions
missed the drift.

#### Name the partitioned set; do not describe it by its producer (#1509)

Both repaired rows above once described their partitioned ids by naming a **producer set**, and both
descriptions were wrong in the same way. Measured at the pre-repair commits:

| tree | at | partitioned | what the ids actually are | what this doc used to say |
| --- | --- | --- | --- | --- |
| `FS.GG.Governance` | `9243c07` | 11 | **10** `speckit-*` **+ 1 repo-native `spectre-console`** | "the `speckit-*` set" |
| `FS.GG.SDD` | `f419f0e` | 28 | **17** `fs-gg-sdd-*` **+ 10** `speckit-*` **+ 1 repo-native `spectre-console`** | "`fs-gg-sdd-*` + `speckit-*`" |
| `FS.GG.Rendering` | `ee5e6c3` | 46 | **30** `fs-gg-*` **+ 16** `speckit-*`, no repo-native member | "the product/Speckit set" — this one *does* hold |

In both completed cases the **count was right and the attribution was wrong**, and the missing member
was the same skill: `spectre-console`, a [repo-native co-tenant](#a-co-tenant-need-not-have-a-producer-at-all--the-repo-native-class-1509)
with no `.specify/` producer and no `registry/skills.yml` row. A repair driven only from "the
authoritative producer" therefore rematerializes 10 of Governance's 11 and 27 of SDD's 28 and leaves
the last one partitioned — with the newly-required `skill-union / skill-union` red on arrival. Both
repair workers hit exactly that in the repo and had to handle `spectre-console` separately.

A producer-set description is a *guess about where the ids came from* wearing the clothes of a
measurement. Enumerate the set, or say the breakdown is unmeasured; do not name a producer and let a
reader infer coverage from it.

`skill-union` is rostered on all seven framework repos, and **one has wired it**: `FS.GG.SDD`
(`a066e0b`), which also made `skill-union / skill-union` required on its default branch. So the
scheduled [`repos-audit`](../../.github/workflows/repos-audit.yml) reports **6 gaps** — measured
2026-07-27 by running `scripts/repos-audit.sh` from this repo: 32 receiver-capability pairs, 26 wired,
6 gaps, 0 unrostered adopters, 0 undetermined, every other capability green. That is the ratchet
[#1504](https://github.com/FS-GG/.github/issues/1504) asks for, not a defect: the rollout is complete
only when `scripts/skill-union-assert.sh --product <fresh origin/main tree>` passes for every tree
above **and** every receiver check is green.

Six of the seven receivers — Templates, Game, Audio, Net, Governance and SDD — are now root-coherent,
and of those only SDD has wired the caller, so the other five need nothing but the block above. The
seventh, `FS.GG.Rendering`, must **rematerialize from its authoritative producer first** (not copy an
arbitrary root, and proving an idempotent second materialization):
[Rendering#1080](https://github.com/FS-GG/FS.GG.Rendering/issues/1080). The other two such requests are
[SDD#716](https://github.com/FS-GG/FS.GG.SDD/issues/716), **closed**, and
[Governance#326](https://github.com/FS-GG/FS.GG.Governance/issues/326), whose rematerialization landed
in `9d8359c` and which remains open only on its caller-wiring step
([Governance#329](https://github.com/FS-GG/FS.GG.Governance/issues/329), in flight at the time of
measurement). Each repair is independent of the other two — a repo's roots are its own — and each
depends on this repo only for the caller shape above and the roster row.

One tree sits outside this capability's subject and outside the composition gate's, so nothing audits it:
`FS.GG.Rendering`'s `template/base/`, which carries `.claude/skills` and `.agents/skills` and no `.codex/`.
It is neither a committed runtime root nor a generated product, and ADR-0011 §3 confines a provider to
`.agents/skills/` — so the correct repair may be to *remove* the `.claude/` copy rather than add a root.
Filed as a decision item: [Rendering#1081](https://github.com/FS-GG/FS.GG.Rendering/issues/1081).

### Standalone fetch — supported, and it is `dist/`, not `scripts/`

**Fetching the assertion as a single file is a supported consumer pattern**
([#843](https://github.com/FS-GG/.github/issues/843)). Fetch **[`dist/skill-union-assert.sh`](../../dist/skill-union-assert.sh)**,
at a pinned 40-char commit SHA:

```sh
curl -fsSL -o skill-union-assert.sh \
  "https://raw.githubusercontent.com/FS-GG/.github/<40-char-sha>/dist/skill-union-assert.sh"
bash skill-union-assert.sh --product path/to/product
```

`dist/skill-union-assert.sh` is **self-contained by construction** and that is a *gated* property, not
a promise: it is generated from `scripts/skill-union-assert.sh` + `scripts/lib/*` by
[`scripts/generate-skill-union-bundle`](../../scripts/generate-skill-union-bundle), and the
`skill-union-bundle` gate re-runs the entire [self-test](#self-test) against the bundle **from a
directory with no `lib/` siblings** — the consumer's actual conditions — on every PR that touches
either. A `source` added to the source script is inlined into the bundle by the same commit that adds
it.

> **Fetch `dist/`, never `scripts/`.** `scripts/skill-union-assert.sh` sources `scripts/lib/args.sh` and
> `scripts/lib/roots.sh` relative to its own dirname, so fetched alone it dies immediately with
> `lib/args.sh: No such file or directory`. That is not a bug to be fixed by inlining: `lib/roots.sh` is
> shared with `coordination-sync` **on purpose**, so the script that WRITES a tree's roots resolves them
> exactly as the one that ASSERTS them does — they diverged silently once, and the asserter blamed the
> tree for the writer's omission ([#525](https://github.com/FS-GG/.github/issues/525)). The libs stay
> shared; the consumer-facing artifact is generated. **The file set under `scripts/` is internal and may
> be refactored without notice. `dist/skill-union-assert.sh` is the contract.**
>
> This is exactly how #843 arose: [#358](https://github.com/FS-GG/.github/issues/358) and
> [#524](https://github.com/FS-GG/.github/issues/524) hoisted those helpers, and because every other
> consumer arrives via the reusable workflow's `actions/checkout` (siblings included, for free), the
> break was invisible here and red only in FS.GG.Templates' CI — for two weeks, since their pin sat
> stale under `tests/` where `config:recommended` ignores it and never attempted the bump that would
> have failed.

Either pattern is fine, and they are not ranked: the reusable workflow is less to wire up, while the
pinned fetch is deterministic, carries its own integrity check by content address, makes moving the pin
a reviewable commit, and needs no network to re-run offline.

## Self-test

[`tests/skill-union/run.sh`](../../tests/skill-union/run.sh) — run in CI by
[`skill-union-selftest.yml`](../../.github/workflows/skill-union-selftest.yml) — builds throwaway
workspace trees and proves the assertion **passes** on a coherent union (including a
superset-catalog manifest with declared-but-absent ids and `--co-tenants`-admitted process
skills) and **fails** on a divergent (`SKILL.md` *and* `references/**`), partitioned, dangling,
and manifest-drifted root, and that `--digest` equals the producers' `sha256sum SKILL.md`. It also pins
[#1506](https://github.com/FS-GG/.github/issues/1506): a tree that is **partitioned *and* divergent**
reports both diagnostics for the same id, every partitioned id is compared rather than just the first,
the summary is asserted byte-for-byte with its populations, no denominator-free `byte-identical=` may
reach it on any tree, and both a one-root skill and a one-root **root set** count `single-root` rather
than claiming a byte-identity nothing established. For the
condition-aware check (ADR-0017) it additionally proves — with a `--params` provenance — a
`[missing]` (declared ∧ true ∧ absent, the `fs-gg-project` case), an `[unexpected]` (present ∧
false), a **justified** absence + compound-true present that **pass**, that the *same* manifest
**without** `--params` keeps the superset semantics (declared-absent tolerated), and that
`--params` without `--manifest` is a misconfiguration (exit 2). This is the acceptance evidence for
#111 (semantics aligned by #120) and #164 (ADR-0017 check 4).

## Where this sits

- **Produced** by the P1 SDD `mirror`/`verify` library and the P2 fs-gg-ui single-materialize step
  (ADR-0014) — they write the roots and self-verify at the source.
- **Consumed** here — asserted again where workspaces are composed, so a non-identical set fails a
  gate instead of shipping green.
- Flips **enforcing** and the `skill-mirror-verified` coherence id to `coherent: true` at roadmap
  **P4**, closing the [#47](https://github.com/FS-GG/FS.GG.Templates/issues/47) chain.
