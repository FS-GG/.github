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
has **drifted bytes**, a provider skill **missing from one root**, or an `.agents` root that
**diverges** from `.claude` were all invisible. The apparatus that exists to guarantee "the roots are
the byte-identical union" never checked that they were. This assertion is that missing check, made
reusable so *every* lane (orchestrated `fsgg-sdd` **and** standalone template) asserts it where
skills are consumed.

## What it checks

Over the configured `AGENT_SKILL_ROOTS` ([which roots?](#which-roots--a-tree-declares-its-own-set-517)
— default ADR-0065's two: `.claude/skills`, `.agents/skills`), for **every** skill
in the union it asserts:

1. **present** — the skill directory exists in **every** root (a miss is a *partitioned* root);
2. **byte-identical** — its bytes are identical across **every root it is present in** (a diff is a
   *divergent* root). Checks 1 and 2 are **independent questions and both are always asked**: a skill
   can be *partitioned* **and** *divergent*, and is then reported as both. Check 1 used to
   short-circuit — see [a partition never suppresses the byte
   comparison](#a-partition-never-suppresses-the-byte-comparison-1506);
3. **matches-manifest** *(only with `--manifest`)* — if the producer's
   [skill-manifest](#the-manifest-and-the-canonical-digest) declares it, its `SKILL.md` digest
   equals the declared digest **in every root the skill is present in** (*drifted*); if the manifest
   does **not** declare it, it must match a `--co-tenants` glob (*dangling* otherwise). Check 3 is
   independent of checks 1–2 and is **always asked** — see [check 3 is the third independent fact,
   and it is per-root](#check-3-is-the-third-independent-fact-and-it-is-per-root-1513).
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
follows it, not vice versa. **That "follows it" is now pinned by a gate rather than by this
sentence** — see [what pins the alignment claim](#what-pins-120s-alignment-claim-1513). Check 4 is
the ADR-0017 tightening — see below.

## The manifest and the canonical digest

The producer manifest is JSON — ADR-0014's `{ id, scope, sha256, body }` per skill:

```json
{ "roots": [".claude/skills", ".agents/skills"],
  "skills": [
    { "id": "cross-repo-coordination", "scope": "process", "sha256": "<SKILL.md body sha256>" },
    { "id": "fs-gg-ui-render",          "scope": "product", "sha256": "<SKILL.md body sha256>" }
  ] }
```

**Digest.** The `sha256` is the **canonical-body sha256 of the skill's `SKILL.md` only** — the
algorithm `Fsgg.SkillMirror` ships (verified in
[.github#120](https://github.com/FS-GG/.github/issues/120)). Multi-file skills (`SKILL.md` +
`references/**`) are covered by the **cross-root identity** of checks 1–2, not by the digest.

**Two normalizations, both the library's.** Before hashing, the body has a leading UTF-8 **BOM
stripped** and **CRLF folded to LF**:

| input | canonical digest |
| --- | --- |
| `# beta skill\n` | `79215589…` |
| `# beta skill\r\n` | `79215589…` — **the same** |
| `<BOM># beta skill\n` | `79215589…` — the same |
| `<BOM># beta skill\r\n` | `79215589…` — the same |

**Say the consequence out loud: a CRLF file and its LF twin now hash identically**, so the digest
check is deliberately *slightly more permissive* than a byte comparison. That is the library's
considered semantics (feature 070 added it so an LF-authored body does not spuriously drift on a
Windows or `eol=crlf` checkout), not an accident, and a reader should not have to infer it from the
code. **Cross-root byte-identity (check 2) is untouched** — it compares raw bytes and still reports a
CRLF copy and an LF copy of one skill as `[divergent]`. The two questions stay independent: *"is this
the body the producer declared?"* tolerates line-ending translation; *"are the roots byte-for-byte
the same?"* does not.

The fold replaces the **pair** `\r\n`, never the character `\r`, so a lone CR survives into the
digest. This is not pedantry: deleting every `\r` would give `# beta skill\r` and `# beta skill` the
**same** digest — two distinct bodies sharing one value, a *missed* drift rather than a spurious one.
Pinned by the `lone-cr`, `cr-cr-lf` and `trailing-lone-cr` vectors in
[`skillmirror.fixtures.json`](../../tests/skill-union/skillmirror.fixtures.json).

This alignment is [#1547](https://github.com/FS-GG/.github/issues/1547): until it landed, the library
folded CRLF and the **two** shell implementations did not, so a CRLF checkout drew a spurious
`[drifted]` from both. See [Five implementations of one
digest](#five-implementations-of-one-digest--four-canonical-one-deliberately-raw) for why the shells
moved rather than the library, and for the two **producers** #1547 did not count
([#1585](https://github.com/FS-GG/.github/issues/1585)).

Every in-repo implementation exposes the algorithm as a **reference generator**, so producers and
checkers never drift, and so the conformance harness can drive them. #1585 added the third — a
*producer* seam, because "producers and checkers never drift" had been an unmeasured sentence for the
producer half:

```sh
scripts/skill-union-assert.sh --digest .claude/skills/<id>     # prints the canonical digest
scripts/fsgg-skill-registry-check --digest <path/to/SKILL.md>  # the Python checker's, same value
scripts/generate-driver-manifest --digest <path/to/SKILL.md>   # the producer's, same value (#1585)
scripts/repos.sh digest <skill-dir|file>                       # NOT this value — the raw bytes; see below
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
(the conditions live on the manifest entries; `--params` only supplies the values).

**Check 4 has no live caller yet — measured 2026-07-28, not predicted.** This line used to name the
[FS.GG.Templates composition gate](https://github.com/FS-GG/FS.GG.Templates/issues/49) as "the first
caller to pass provenance, enforcing `[missing]`/`[unexpected]` in both lanes", in the present tense.
Templates' gate *is* wired in both lanes (see
[the generated-subject shape](#the-generated-subject-shape-and-why-templates-is-not-a-uses-caller)), but
its two arms are `--product` and `--product --manifest --co-tenants`; it passes **no `--params`**, so
`[missing]`/`[unexpected]` are not enforced anywhere today. `FS.GG.Rendering`'s
`template-base-skill-union.yml`, the only `uses:` caller with a manifest, supplies no `params:` either.
The check is implemented and fixture-covered here; adoption is still zero, and this document says so
rather than naming a caller that a plan once expected. Corrected at
[#1643](https://github.com/FS-GG/.github/issues/1643).

**The predicate language** is deliberately tiny — evaluable in both the shell gate
(`eval_condition`) and the Python drift-normalizer (`normalize_when`) without a real expression
engine, and a shared fixture table pins the two together so they never drift
([`tests/skill-union/conformance.sh`](../../tests/skill-union/conformance.sh)):
`always` · `<param> == <value>` · `<param> != <value>` · `<param> in [<v>, <v>, …]` · clauses joined
by `and` / `or` (`and` binds tighter; no parentheses). Values and params are bare tokens
(`[A-Za-z0-9_-]`, plus `true`/`false`); a param absent from the provenance reads as empty. The
authoritative catalog of ids + conditions is the org skill registry `registry/skills.yml` (ADR-0017),
generated from the producer manifests.

## A co-tenant need not have a producer at all (#1509)

Process, product and kit skills all have an **external producer** to verify a digest against: a
manifest, a `.specify/` tree, an embedded-resource set, the coordination kit. A further class lives in
the committed roots and has none — a skill with **no producer manifest and no `registry/skills.yml`
row anywhere in the org**. It is undeclared because nothing declares it, not because it drifted in, and
it is a legitimate co-tenant.

It has two shapes, and they are not the same fact:

- **repo-native** — the receiver repo **authored** it. It is the origin.
- **vendored** — the repo holds a copy of somebody else's repo-native skill. `metadata.source` names
  the origin, and no fabric moves the bytes.

`spectre-console` is the worked example and shows both. FS.GG.Governance authored it (specs 091/093,
recorded in its `metadata.source`), so it is repo-native **there**; FS.GG.SDD's own repair classifies
its copy as a *"vendored co-tenant, `metadata.source: FS.GG.Governance spec 091`"*. Measured
2026-07-27, it sits in the runtime roots of `FS-GG/.github`, `FS.GG.Governance` and `FS.GG.SDD`, while
`registry/skills.yml` (50 rows) has **no row for it** and no producer manifest declares it. **Do not
assume one canonical body**: Governance's and SDD's `SKILL.md` are byte-identical
(`c5f71431…`), and `.github`'s is **not** (`603497ae…`) — nothing gates cross-repo identity for a
skill no manifest owns, and this assertion does not either. Its subject is one tree.

Three consequences:

1. **A repair driven from "the authoritative producer" silently leaves it behind.** Rematerializing a
   partitioned tree from its producer(s) fixes every id one of them emits and no others, so a
   producer-less co-tenant stays partitioned. Both completed rollout repairs had to **correct the
   attribution first** and give the skill an explicit authority row of its own inside the same
   materializer; had either driven the documented producer set alone, it would have merged a still-
   partitioned tree. See [Rollout state](#rollout-state-measured-2026-07-27) below.
2. **A `--co-tenants` glob set is not automatically wide enough.** The
   [Set semantics](#the-manifest-and-the-canonical-digest) example, `"fs-gg-sdd-* speckit-*"`, matches
   `spectre-console` under neither shape, so a caller passing a `--manifest` over a tree holding it
   gets `[dangling]` for a skill that belongs there. A tree with producer-less co-tenants must name
   them, or a glob covering them, in its own `--co-tenants`.
3. **"The producer" is usually plural, and naming one of several is the same error as naming none.**
   SDD's roots are written by four authorities: `.specify/` (the 10 `speckit-*`),
   `FS.GG.SDD.Commands.fsproj` EmbeddedResources (the 17 `fs-gg-sdd-*`), the FS.GG.Kit pin (the 4 kit
   skills, which were never partitioned), and the repo's own hand (`spectre-console`). Of its 28
   partitioned ids, "rematerialize from `.specify/`" reaches **10**; both code-owned producers together
   reach **27**; only enumerating the set reaches all 28.

None of this weakens checks 1–2: a producer-less skill is still required to be in **every** root and
byte-identical across them *within that tree*. Having no producer excuses it from the *digest*
cross-check, never from the union.

## Usage

Directly:

```sh
scripts/skill-union-assert.sh --product <product-dir> \
  [--roots ".claude/skills .agents/skills"] \
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
| **Scaffolded product** | `.claude/skills` `.agents/skills` (ADR-0065's two) | `fsgg-sdd`, the sole mirror authority |
| **Kit consumer** (FS-GG's own repos, incl. `.github`) | `.claude/skills` `.agents/skills` | `FS.GG.Kit` / `coordination-sync` |

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
it never weakens the answer. This is why the fix for #517 was a declaration rather than dropping a
root from the default: a scaffolded product whose producer never materialized `.agents/` must keep
failing — catching exactly that is the gate's reason to exist (ADR-0011's origin bug), and a default
that no longer asks for a root it still expects would be the fail-*open* pattern of #266/#292.

> `.codex/skills` **was** in this default and is no longer, and that is not the failure above.
> ADR-0067 §5 (.github#1636) established by measurement that it was Codex's *other* native root
> rather than a third runtime's, so it was removed from what is ASKED FOR, in the same change that
> removed it from what is DELIVERED. Narrowing a default because a root has no runtime is a contract
> migration; narrowing one while a producer still owes the root is the fail-open bug.

**The declaration is for a tree asserting *itself*, never for a tree being audited.** The reusable
`skill-union-assert.yml` gate therefore **always passes `--roots` explicitly** and never consults the
product's `.agent-skill-roots`. The tree it audits is producer-*generated* (`fsgg-sdd` and the
`fs-gg-ui` template write it), whereas the `roots:` input is human-authored by the consumer repo —
so honouring a declaration found *inside the subject* would let a template bug that emitted one
silently switch a root off and turn the gate green on a partitioned product. The roots come
from the caller; the declaration serves the bare local run and a repo's own selfcheck.

## Subset coherence is not union coherence (#1504)

Two org gates check the root invariant, they are **not** the same claim, and reading one as the
other cost the org three partitioned repositories:

| gate | subject | what a green means |
| --- | --- | --- |
| [`coordination-coherence.yml`](../../.github/workflows/coordination-coherence.yml) (`receives: coordination-kit`) | the **kit-owned subset** — exactly the skills `registry/repos.yml`'s `kit:` block names (today: `cross-repo-coordination`, `intra-repo-parallel-work`, `check-board`, `pnext-item`) | those four skills are materialized, in every root, byte-identical to canonical |
| this assertion, wired as `receives: skill-union` | the **complete runtime-visible union** — every skill in the repo's committed `.claude/skills`, `.agents/skills` | *every* skill in the union is present in every root and byte-identical across them |

`coordination-coherence` cannot see a co-tenant skill: it is not in the `kit:` block, so it is not in
that gate's subject. Here are the three trees it cost, **each measured at the commit named** rather
than at a moving `main` — Governance `9243c07` `.claude=15 .codex=4 .agents=4`; SDD `f419f0e`
`.claude=32 .codex=21 .agents=4`; Rendering `ee5e6c3` `.claude=50 .codex=4 .agents=50`. Projections
were **missing**, for 11, 28 and 46 skills, and all three were green on `coordination-coherence`
throughout, because the four kit skills really are coherent in all of their roots. (Those three
measurements predate ADR-0067 §5; their `.codex` column is history. The gap is unaffected — `.codex`
and `.agents` were both Codex-native, so a tree short on `.agents` was short on skills for Codex
either way.)

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
comparison. On `FS.GG.Rendering` at `ee5e6c3` the gate printed

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

## Check 3 is the third independent fact, and it is per-root (#1513)

The section above fixed two of the three. **Check 3 still short-circuited**, and it is the same defect
one check further down:

```sh
if [ -n "$partitioned" ] || [ -n "$differing" ]; then continue; fi   # ← checks 3 AND 4 skipped
```

Measured on `main` at `22461b4` — *after* #1506 landed — over a tree where `beta` is present in
`.claude` + `.codex`, absent from `.agents`, and whose `SKILL.md` digest does **not** match the
manifest's declared `sha256`:

```
::error::[partitioned] skill 'beta' is absent from root(s): .agents/skills
skill-union-assert: 2 skill(s) — in-every-root=1/2 partitioned=1 | byte-comparable=2 byte-compared=2 byte-identical=2/2 byte-differing=0 single-root=0 | manifest-matched=1 co-tenant=0 declared-absent=0
```

`beta`'s declared digest was never read, no `[drifted]` was emitted, and `manifest-matched=1` is a
count with no population over a 2-id union — **exactly the defect #1506 fixed for the byte counts,
left in place for the manifest counts**. The same tree now reports:

```
::error::[partitioned] skill 'beta' is absent from root(s): .agents/skills
::error::[drifted] skill 'beta' SKILL.md digest != manifest 1111…1111 in root(s): .claude/skills=4b929eb7… .codex/skills=4b929eb7…
skill-union-assert: 2 skill(s) — in-every-root=1/2 partitioned=1 | byte-comparable=2 byte-compared=2 byte-identical=2/2 byte-differing=0 single-root=0 | manifest-declared=2/2 manifest-comparable=2 manifest-examined=2 manifest-matched=1/2 manifest-no-reference=0 undeclared-rejected=0/0 co-tenant=0/0 declared-absent=0/2
```

### Per-root, not per-representative-root — and the evidence that decided it

`SkillMirror.verify`'s `HashMismatchRoots` is a **list of roots**; the shell digested **one
representative root** (the first root that has the skill). #1506's worker flagged the difference and
deliberately did not fold it in, because it is a design question and not a reordering. It is settled
here **on measurement**, not preference, and the measurement is in
[`tests/skill-union/skillmirror.fixtures.json`](../../tests/skill-union/skillmirror.fixtures.json)
(vector `divergent-and-the-REFERENCE-root-is-the-clean-one`), derived by running the library itself:

> Three roots. `.claude` and `.codex` match the manifest; `.agents` has **drifted**. The library
> returns `HashMismatchRoots = [".agents"]`. A representative-root digest takes `.claude` — one of the
> **clean** ones — and reports **nothing at all**.

That is not a difference of detail, it is a **fail-open**: the [#266](https://github.com/FS-GG/.github/issues/266)
family again, a check that was not made rendered as a check that passed. Representative-root also
cannot *name* the drifted root even when it happens to catch one. So check 3 is per-root, and reports
a **root list** on one line — the shape of `HashMismatchRoots`, and one line however many roots
drifted, because `::error::` is a workflow command parsed only to its first newline.

The change is a **no-op on every coherent tree**: an id that is whole and byte-identical has the same
digest in every root, so per-root and representative-root give the same verdict. It differs only where
representative-root was already unsound.

**The v2 whole-directory arm takes the same rule**, and that is this repo's own extension rather than
the library's: `ActualCopy.Body` is the `SKILL.md` body alone, so `Fsgg.SkillMirror` has no v2
counterpart to follow. Per-root there is the same reasoning — a divergent id has different bytes in
different roots, and auditing one of them is a claim about the id that only one root's evidence
supports.

### Independence must not become manufacture

Running check 3 unconditionally must not make a partition *look* like a drift. A partition whose
present copies **do** match the manifest emits **no** `[drifted]`, exactly as a partition of
byte-identical copies emits no `[divergent]`. Both directions are pinned in
`tests/skill-union/run.sh`.

### The manifest counts carry their populations

Each denominator names a **different** population, because these counts are not taken over the same
set — a single shared denominator would be the same overstatement in a new costume:

| field | population |
| --- | --- |
| `manifest-declared=<n>/<union>` | how much of the on-disk union the manifest declares at all |
| `manifest-comparable=<n>` | declared ∧ present ∧ condition-true ∧ **has a reference to compare against** |
| `manifest-examined=<n>` | what check 3 **did** compare. Unequal to `manifest-comparable` ⇒ the gate emits an `::error::` and **fails**, exactly as `byte-comparable` vs `byte-compared` does |
| `manifest-matched=<n>/<examined>` | never a bare count — the string `manifest-matched=1` is what #1513 was filed about |
| `manifest-no-reference=<n>` | declared, but with neither `sha256` nor `files`: **nothing was compared**, so it is never folded into `manifest-matched` |
| `undeclared-rejected` / `co-tenant` `=<n>/<undeclared>` | the two dispositions of an undeclared id; they sum to `<undeclared>` exactly |
| `declared-absent` / `missing` `=<n>/<declared>` | the manifest→disk sweep's own population |

**None of the new fields is spelled with a class word** — `undeclared-rejected` and `off-profile`, not
`dangling` and `unexpected`. `byte-differing` was spelled that way deliberately so a **zero** in the
summary could not trip a fixture asserting no `[divergent]` diagnostic, and a fixture in this very
suite does `grep -q 'divergent'` unbracketed. That care is load-bearing and it is kept. (`missing=`
predates this and is left alone; it is named here so the exception reads as a decision.)

### One more defect, found by the fixture that was written for the first

Adding the `manifest-no-reference` leg turned a latent bug red. The manifest rows were read as `@tsv`
with `IFS=$'\t'`, and **tab is an IFS *whitespace* character**: bash collapses runs of it even when
`IFS` names it explicitly, so an **empty field in the middle of a row disappears and every later field
shifts left**. A skill declared `"sha256": ""` — which `SkillMirror.ExpectedSkill` defines as *"no
reference digest"*, a legitimate row — therefore had its `materializes-when` **predicate read as its
digest**, and its condition read as empty (which evaluates as `always`). Two wrong answers from one
unnoticed shift: a spurious `[drifted]` on a row declaring no digest, and a false `[missing]` for a
legitimately off-profile skill. The rows are now separated by `\u001f` (ASCII unit separator), which is not IFS whitespace.

## What pins #120's alignment claim (#1513)

**The root cause of all three divergences is that nothing enforced the claim.**
[#120](https://github.com/FS-GG/.github/issues/120) settled that `Fsgg.SkillMirror` is ADR-0014's *one
implementation* and that this script *follows* it, and that sentence was pinned by **nothing** — so the
two drifted three times, each found by hand, each after real work had already been misdirected. Fixing
the third fact without pinning the claim buys a fourth.

The repo already knew the pattern: `tests/skill-union/conformance.sh` pins the shell `materializes-when`
evaluator against Python's `normalize_when` over a **shared fixture table**
([#398](https://github.com/FS-GG/.github/issues/398)), precisely because *"a divergence fails OPEN"*.
The shell ↔ `SkillMirror` pair now has the same:

| file | what it is |
| --- | --- |
| [`tests/skill-union/skillmirror.fixtures.json`](../../tests/skill-union/skillmirror.fixtures.json) | the shared vector table. Every vector states **all three** of `verify`'s facts, plus what the shell must report |
| [`tests/skill-union/skillmirror-conformance.sh`](../../tests/skill-union/skillmirror-conformance.sh) | materializes each vector as a real tree + manifest, runs the gate, and reads the three facts back **out of its diagnostics** — compared **one fact at a time**, so a shell that gets two right and drops the third fails on the third. Hermetic; folded into `run.sh` |
| [`tests/skill-union/skillmirror-oracle.sh`](../../tests/skill-union/skillmirror-oracle.sh) | the **derivation**: `#load`s the library's own source and runs `verify` over those exact vectors, so the table's expectations are *measured* rather than transcribed from the `.fsi` comments — which is the failure mode #1513 is about |

**The mechanism decision, which #1513 asks for explicitly rather than by assumption.** A fixture that
executes `Fsgg.SkillMirror` on every PR would couple this repo's suite to a cross-repo checkout or a
NuGet restore. So the two halves are split: **conformance is hermetic** (no dotnet, no package, no
network — it runs everywhere and cannot be skipped into a green), and **derivation is a committed,
re-runnable command** anyone holding the library can execute:

```sh
bash tests/skill-union/skillmirror-oracle.sh --lib <FS.GG.SDD checkout>/src/FS.GG.Contracts
```

It is a **checker, not a writer**. A generator that rewrote its own expectations from the
implementation would green any divergence the moment it was regenerated — the shape of the defect, not
a fix for it. It also verifies the `SkillMirror.fs` digest recorded in the table's `derivedFrom` block,
so a table derived from a *different* library revision cannot claim to have been derived from this one.

**The residual gap, and what now watches it.** This closes shell-drifts-from-table (a gate on every
PR) and table-drifts-from-library-at-the-revision-`derivedFrom`-records (a re-runnable derivation).
Naming that revision here rather than pointing at the block would be a second copy of a fact that
moves on every re-derive; it has been correctly updated alongside each one so far, and pointing at
the block is what keeps that true without anyone having to remember. Neither notices a **future**
library change: this repo's CI holds neither the source nor the package. That third leg landed as
[#1546](https://github.com/FS-GG/.github/issues/1546) (`f3a6d15`) — a scheduled cross-repo freshness
check, [`skillmirror-freshness.yml`](../../.github/workflows/skillmirror-freshness.yml) plus
[`scripts/check-skillmirror-freshness.py`](../../scripts/check-skillmirror-freshness.py), running daily
at **07:11 UTC**, which reads `SkillMirror.fs` over the API and compares its digest to
`derivedFrom.skillMirrorFsSha256`.

**Read what that gate actually asserts, because it is narrower than "the table is live."** It compares
a digest of **one file**, `src/FS.GG.Contracts/SkillMirror.fs`. It notices that the library *moved*; it
never says whether the move altered `verify`'s behaviour — only re-running the oracle does that. And
`Schemas.fs`, which the oracle also `#load`s, is **outside its reach**, so a change confined to
`Schemas.fs` can move `verify` while this gate stays green:
[#1577](https://github.com/FS-GG/.github/issues/1577). A freshness gate is a tripwire on one file, not a
proof of alignment, and calling it the latter would re-create the fail-open #1513 was filed about.
**#1577 is not hypothetical**: across the span #1576 re-derived below, `Schemas.fs` moved
`skillManifestVersion` from `1` to `2` (FS.GG.SDD#727 — the manifest now content-addresses a skill's
whole file set), and no digest this gate watches covered that line.

**The gate found the table dated, and the table was re-derived — the full loop, measured at each
step.** `derivedFrom` recorded `b1c7e94d…` (4371 B) at `a066e0b`; the scheduled check read
`SkillMirror.fs` on `FS-GG/FS.GG.SDD` `main` and reported a different digest, which was filed as
[#1576](https://github.com/FS-GG/.github/issues/1576) and closed by re-running the oracle against a
checkout at **`5debf6e`** (`95af075b…`, 27817 B) — every one of the 10 `verify` vectors and 11
`digestVectors` still agreeing with the live library, and `derivedFrom` plus
`digestVectors.measuredAgainst` updated **in the same change** as the re-derivation that justified
them. That is what closing the loop looks like, and it is worth naming which leg did which: the gate
said the library *moved* and could not say more; the **oracle** is what established that `verify`'s
answers were unchanged. `#120`'s claim is pinned in one direction on every PR and re-measured in the
other on demand, and drift is now **announced by a gate** on a schedule instead of waiting to be found
by hand for a fourth time.

**It has now happened a second time, and the loop closed the same way — but the first turn of it was
unattended for a day, which is its own finding.** The gate went red on `main` on **2026-07-28** and
stayed red, because a *scheduled* job blocks no PR: two workers hit it as a pre-merge surprise on two
unrelated items before anyone owned it. That is
[#1880](https://github.com/FS-GG/.github/issues/1880), and it closed by re-running the oracle against
**`bc93f94`** (`e44de4a0…`) — again every one of the 10 `verify` vectors and 11 `digestVectors`
reproduced **unchanged**, with both provenance blocks updated in the same change. The span
`5debf6e..bc93f94` is **31 commits** wide and touches `SkillMirror.fs` in exactly **two** of them,
both additive at the surface this table measures: FS.GG.SDD#737 appended the byte seam
(`decodeBody`/`sha256Bytes`, leaving `sha256` itself untouched), and FS.GG.SDD#760 gave the mirror
fold its third observation state, re-expressing `verifyFiles` and `verifyFileSet` as private cores
called with an *empty* unobserved set. Neither touches `verify` or `sha256`. **`Schemas.fs` did not
move at all across those 31 commits** — so unlike the #1576 turn, #1577's blind spot was not
exercised here, and that is a measurement across the whole span rather than a glance at two diffs.

**The re-derive is a race against a moving default branch, and #1880 lost the first heat.** The oracle
was run first against `58a1414` (`20fdb35c…`) — the head when the item was **claimed**, and the last
one still carrying the digest the issue recorded — and `FS-GG/FS.GG.SDD` `main` advanced to `bc93f94`
mid-item. (`58a1414` postdates the issue by four hours, so it is not the head #1880 was *filed*
against; what #1880 pinned was the **digest**, current from `da07830` through `58a1414`.) Both runs
reproduced every vector, and only the later revision is recorded, because the gate grades the
**default branch**: a provenance block naming a superseded head is red the moment it lands. Nothing in
this repo can close that race. It is the cost of pinning a revision of a repository that moves
independently, and it is the tripwire working rather than a defect in it.

### It happened a third time, and the third turn closed the recurrence instead of the instance (#2521)

The gate went red on `main` on **2026-08-08** and stayed red for **six consecutive scheduled runs**,
this time on **`Schemas.fs`** — the second watched file, added to `derivedFrom.libraryFiles` by
[#1577](https://github.com/FS-GG/.github/issues/1577). Ten days after #1880 closed by re-running the
oracle, the same red was back one file over. That is the shape pnext-item §4 names: *if a fix keeps
regenerating the same finding, the finding is not the bug — the thing that regenerates it is.* So
[#2521](https://github.com/FS-GG/.github/issues/2521) was filed against the **recurrence**: a dated
cross-repo snapshot with no owner and no re-derivation trigger. A third manual refresh buys a fourth.

**Three questions, three mechanisms, and confusing any two of them is how this stays broken.**

| question | mechanism | what it reads | when |
| --- | --- | --- | --- |
| Is the pin still **current**? | [`skillmirror-freshness.yml`](../../.github/workflows/skillmirror-freshness.yml) + `check-skillmirror-freshness.py` | the library's **default branch**, digests only | daily 07:11 |
| Is the table **what its pin claims**? | `skillmirror-redrive.yml` job `derivation` + `skillmirror-redrive.py --assert-derived` | the library at **`derivedFrom.commit`**, every vector | every PR touching the table |
| **Make** the pin current again | `skillmirror-redrive.yml` job `redrive` + `skillmirror-redrive.py --write` | the library's **default branch**, every vector | daily 07:41 |

The middle row is new and is the one that makes the third row trustworthy: it reads at the *recorded*
commit, which is exactly the read `check-skillmirror-freshness.py` **forbids itself** — there it would
compare a digest to the bytes it was computed from and be green forever. Here the compared quantities
are independent, so a PR that hand-edits a vector, or re-pins a commit without re-deriving, goes red.

**What the bot may write, and the line it may not cross.** The oracle is deliberately *a checker and
not a writer*, because "a generator that rewrote its own expectations from the implementation would
green any divergence the moment it was regenerated". Automating the re-derivation must not quietly
become that generator, so `skillmirror-redrive.py` writes **provenance only** — `derivedFrom.commit`,
the `libraryFiles` digests, `digestVectors.measuredAgainst`, and the two dates — and never a measured
column. A moved vector makes it **refuse and go red**, naming the vector. Two independent things
enforce that: an allow-list of the oracle's provenance messages that treats anything unrecognised as
a moved vector, and — because a message allow-list is a heuristic — a **re-run of the oracle after the
write**, which reverts it if the derivation still disagrees.

**It answers the question the freshness gate structurally cannot.** That gate compares digests: it can
say the library moved, never whether the move altered `verify`. Running the *oracle* is what answers
that, and it now happens **daily** rather than when someone remembers. A green there is a fresh
measurement of behaviour across all 21 vectors, not a byte comparison.

**It opens a PR; it does not merge one — unlike [`skill-registry-autofix.yml`](../../.github/workflows/skill-registry-autofix.yml).**
That bot merges because its diff is mechanical digest churn produced hourly, and
[#642](https://github.com/FS-GG/.github/issues/642) measured what an unmerged green bot PR is worth.
This one differs in the way that matters: each re-pin **asserts that the library's behaviour is
unchanged across a span of commits**, and this table's value has always rested on someone writing that
span down — the paragraphs above and below are what that looks like. A bot cannot author them, and
auto-merging would land the pin without them. So it opens a PR that is already green and already
measured, turning hours of hand-derivation into minutes of review. The branch is pushed with the
`fs-gg-cross-repo-dispatch` App token for the [#425](https://github.com/FS-GG/.github/issues/425)
reason: a `GITHUB_TOKEN` push does not re-trigger `on: pull_request`, so the `derivation` job — the
only independent check on the bot's own claim — would never run.

**The #2521 turn's own measurement.** The oracle was run against **`745f4ba2`** and **no vector
moved**: all 10 `fixtures[].library` blocks and all 11 `digestVectors[].digest` values reproduced
exactly, and the run's only disagreement was `libraryFiles[Schemas.fs]`. The span
`bc93f94..745f4ba2` is **48 commits** wide and does not touch `SkillMirror.fs` **at all** — its digest
is unchanged from #1880's pin. It touches `Schemas.fs` in exactly **one** commit, `5a7fef7d`
(FS.GG.SDD#804), which drops `.codex` from `Schemas.agentSkillRoots`, leaving `[".claude"; ".agents"]`.
That is FS.GG.SDD adopting a decision **this repository had already made**: ADR-0067 §5 retired
`.codex/skills` here at [#1636](https://github.com/FS-GG/.github/issues/1636). It cannot move `verify`,
which takes root **labels** from its caller and never reads `agentSkillRoots` — and the oracle run is
what turned that reasoning into a measurement rather than leaving it an argument.

Two limits stated rather than implied. The vectors still name `.codex/skills`, **deliberately**:
`verify` is root-agnostic, so the label is an arbitrary third root and the vectors remain a valid test
of the fold; renaming them would edit a *measured* column for cosmetic realism, and that edit would
have to be re-derived, not renamed. And `SkillMirror.fsi` **did** move in this span, while nothing here
measures it — the oracle `#load`s the implementation files only, so a signature-file change is
invisible to both the table and the gate.

### Intentional differences are asserted, not commented

One vector carries a `divergence` block, and it is asserted in **both** directions — the shell must
match its recorded behaviour **and** must still differ from the library. A documented difference that
quietly disappears is as much a drift as one that grows, and neither may be discovered by reading a
comment:

| difference | direction | why |
| --- | --- | --- |
| **declared ∧ absent from every root** — `verify` iterates the *expected* set and returns `MissingRoots = every root`; the shell iterates the *on-disk union*, so such an id is not a union member | the shell is **looser**, by design | Settled by [#120](https://github.com/FS-GG/.github/issues/120) (the manifest is a superset **catalog**) and tightened by ADR-0017's `--params`, which turns it into `[missing]` when the condition is true. Asserted so it stays a decision rather than becoming an accident |

**The CRLF row used to live here and no longer does.** It recorded that the library folded `\r\n`
and `skill_digest` did not, pinned as *stricter, fails closed*. [#1547](https://github.com/FS-GG/.github/issues/1547)
resolved it by aligning the shells to the library, so the vector still exists — renamed
`crlf-body-against-an-lf-digest-AGREES` — but now asserts **agreement**, and its `divergence` block is
gone. A table that kept asserting a difference which no longer existed would red, which is precisely
why removing the block was part of the same change.

### Five implementations of one digest — four canonical, one deliberately raw

**The count is five.** The `fixtures` table pins `verify`'s three facts across **two**
implementations; the **digest** has **five**, and until
[#1547](https://github.com/FS-GG/.github/issues/1547) nothing compared even three of them. This
heading said *three* from #1547 until [#1585](https://github.com/FS-GG/.github/issues/1585), because
#1547 was scoped to the two **verifiers** and counted only what it touched. The heading is the thing a
reader trusts, so it now states the measured number rather than the number one change happened to see.

| implementation | role | rule | folds CRLF? |
| --- | --- | --- | --- |
| `Fsgg.SkillMirror.sha256` | FS.GG.Contracts — ADR-0014's **one implementation** | canonical body | yes |
| `skill_digest` (`scripts/skill-union-assert.sh`) | verifier | canonical body | yes, since #1547 |
| `canonical_digest` (`scripts/fsgg-skill-registry-check`) | verifier | canonical body | yes, since #1547 |
| `canonical_digest` (`scripts/generate-driver-manifest`) | **producer** | canonical body | yes, **since #1585** |
| `digest` (`scripts/repos.sh`) | **producer** | **raw bytes** — deliberately not the canonical body | **no, by decision** (#1585) |

**#1585's decision (2026-07-27): the two producers split apart, and both are now pinned.** They were
found by an adversarial review of #1547's implementing PR, after #1547 had merged, and were filed
rather than folded in — changing what a *producer* writes into a shipped manifest is a different blast
radius from changing what a verifier accepts. Neither was a regression: no tracked `SKILL.md` in this
repo contains a CR or a BOM, so on today's content every rule below agrees and
`registry/driver-skill-manifest.json` and `registry/repos.lock` are **byte-unchanged** by #1585. The
split was latent and failed *closed* (a raw producer digest is one no verifier reproduces, so it reads
as `[drifted]`).

- **`generate-driver-manifest` follows the library.** Its `sha256` lands in
  `registry/driver-skill-manifest.json`, which the reconcile copies into `registry/skills.yml` and
  re-derives, and which `skill-union-assert.sh` check 3 compares against its own `skill_digest`. A
  producer of a value two verifiers re-derive must compute the verifiers' function; anything else emits
  a manifest they cannot reproduce. It gained a `--digest PATH` seam and is now asserted against the
  library's measured value exactly like the two verifiers.
- **`repos.sh` deliberately stays raw**, and its header sentence claiming byte-equivalence to
  `skill_digest` — false since #1547 — is **removed rather than repaired**, because repairing it would
  mean folding, and folding would be wrong three ways. (1) It is a **byte-integrity** digest, not a
  body-identity one: it writes `registry/repos.lock` and, through `src/FS.GG.Kit/stage-kit.sh` (which
  shells out to this same command), the `sha256` column a receiver checks a **materialized file**
  against at restore — a normalizing digest would report a CRLF-mangled delivery as intact. (2) It
  would break a live containment check: `scripts/check-kit-published-coherence.py` requires every
  `repos.lock` digest to appear among the canonical `kit-manifest.tsv` digests, which are per-file raw
  digests, so folding only the skill-dir arm would invent a gate failure. (3) It is not only a skill
  digest — the `kit:` roster's `client` and `config` rows (`scripts/fsgg-coord`,
  `dist/dotnet/.config/dotnet-tools.json`) come through the same function, and "canonical body" is not
  a defined notion for a shell script or a tool manifest.

**The deliberate difference is asserted in both directions**, the same rule the `divergence` blocks
apply to `verify`: `skillmirror-conformance.sh` holds `repos.sh digest` to the **raw** sha256 of every
vector (recomputed, never transcribed), *and* fails if no vector separates raw from canonical — so
repos.sh silently *adopting* the library's normalization, or the distinguishing vectors being tidied
away, reds exactly as loudly as a fresh divergence.

The two pins that existed were **pairwise, and both skipped the digest**: `conformance.sh` pins
shell↔Python for the *predicate grammar* only, and `skillmirror-conformance.sh` pinned shell↔library
for the *three facts*. So when the library folded CRLF and both shells did not, two of three agreed
with each other and all the pins were green. **Shell-vs-shell agreement was the trap** — they agreed
perfectly and were both wrong.

**The decision (2026-07-27): the shells moved.** ADR-0014 names `Fsgg.SkillMirror` the one
implementation and [#120](https://github.com/FS-GG/.github/issues/120) records that this repo's
checkers *follow* it, so the shells were the drifted copies. Changing the library instead would have
overturned a deliberate feature-070 decision and altered a published `FS.GG.Contracts` surface, which
needs a version story it does not have. Both shells were changed — leaving one behind would merely
have relocated the disagreement.

**The gate.** `digestVectors` in
[`skillmirror.fixtures.json`](../../tests/skill-union/skillmirror.fixtures.json) carries **eleven**
vectors (LF, CRLF, BOM+LF, BOM+CRLF, empty, lone CR, `\r\r\n`, trailing lone CR, no trailing newline,
NUL byte, many trailing newlines) with **one** expected `digest` each — deliberately not one column per
implementation, so re-introducing a disagreement cannot be done by editing one side's expectations.
Each value is **measured** by `skillmirror-oracle.sh` running the library's own `sha256`;
`skillmirror-conformance.sh` then holds **all four canonical implementations** to it hermetically on
every PR (#1585 added the driver-manifest producer to the three #1547 covered), via their `--digest`
reference seams, and compares each **to the library's measured value** rather than to one another.
`repos.sh digest` is driven over the same vectors against its own raw rule, per the split above.
Inputs are stored as `bytesBase64` because these vectors turn on a BOM, on lone CRs and
on exact trailing bytes — the things a JSON string literal or a stray reformat silently normalizes
away.

**The scope of the agreement claim: valid UTF-8.** The digest is defined over decoded text, while the
three in-repo copies hash raw bytes. They are compared only for a body that decodes cleanly. A body with
invalid UTF-8 is now refused by `FS.GG.Contracts` at its read seam, with its own diagnostic, before it
reaches `SkillMirror.sha256`; it is not a digest mismatch and has no expected digest. The shells still
hash raw bytes, so this is a refusal boundary, not a claim that the implementations converge over
invalid input. `digestVectors` deliberately records exactly one expected digest only for valid UTF-8
(.github#1656), rather than freezing a disagreement into an expectation.

The same boundary covers a **UTF-16/UTF-32 BOM**: `File.ReadAllText` detects those and decodes
accordingly, while every in-repo copy special-cases only the UTF-8 BOM `EF BB BF`, so a UTF-16LE
`SKILL.md` gets a different answer from the library. A body outside the canonical UTF-8 domain is
refused at the library read seam, rather than being assigned an expected digest.

**One implementation detail worth knowing before you edit `skill_digest`.** It streams the body
through `sed -z` rather than slurping it into a shell variable, and all three reasons are fail-open
defects that the *first* cut of #1547 actually shipped and an adversarial review caught: `$(cat …)`
silently **discards NUL bytes** (so `a\0b` and `ab` hashed identically — two files, one digest); a
command substitution reports its **last** command's status, so `$(cat f; printf x)` swallowed a read
failure and returned sha256("") with exit 0 for an unreadable file; and bash's pattern substitution is
**superlinear**, taking 215 s on a 4 MB CRLF body that `sed -z` handles in milliseconds — a cost that
is zero on LF and unbounded on exactly the CRLF checkout this change exists to support. All three are
now pinned: the `nul-byte` vector, the unreadable-`SKILL.md` case in `tests/skill-union/run.sh`, and
a start-up self-test of the `sed -z` fold itself.

## Adoption — wiring it into a consumer repo's CI

There are **two** kinds of caller, and the roster's `caller: skill-union` detector deliberately tells
them apart. Passing `product-path: <subdir>` audits a **generated product**; leaving it at its default
audits the **repository's own committed roots**. A `uses:` of this workflow does not say which, which is
exactly why the [repo roster](../../registry/repos.yml) declares this capability with a compound
`caller:` detector rather than a bare `workflow:` one — the latter would certify the full-union
capability off a call that never looks at the receiver's roots ([#628](https://github.com/FS-GG/.github/issues/628)).

### The required receiver caller — RETIRED 2026-07-28. Do not wire a new one.

> **This shape is retired org-wide, and the block below is kept as a record of what it was, not as an
> instruction.** Decided on [`#1715`](https://github.com/FS-GG/.github/issues/1715) (ADR-0067 §9 phase 4,
> blocker **B5**, shape (b)) with the repository owner's authorization, and executed the same day: the
> three receivers that had wired it — `FS.GG.SDD`, `FS.GG.Rendering`, `FS.GG.Governance` — no longer do,
> and `skill-union / skill-union` is no longer a required context on any of them.
>
> **Why: on the layout ADR-0067 phase 4 retires into, this gate cannot fail.** Measured on all three of
> those repos' own trees, at a dry-run retirement:
>
> ```
> --roots ".agents/skills"                 -> exit 2, "no skills found under any root"
> --roots ".claude/skills .agents/skills"  -> exit 0, in-every-root=N/N, byte-identical=N/N
> ```
>
> `union_ids()` enumerates with `find` and no `-L`, so a root that **is** a generated view contributes
> **zero** ids — that is the first line by itself. Every id in the second came from the tracked root
> alone, and the view root satisfied presence only because presence is `[ -d ]` **through** the symlink,
> which cannot fail. Both halves below become tautologies, on a context that was **required** under
> `enforce_admins` — epic `#266`'s most expensive shape.
>
> **What to wire instead**, in the receiver's own repo, with no `uses:` of anything here and the
> receiver's **own** pinned `scripts/skill-view` (`#1584`):
>
> ```yaml
> # .github/workflows/skill-view-check.yml — required context `skill-view-check`
> on:
>   pull_request:            # DELIBERATELY UNFILTERED — see the paths: section below, it still applies
>   push: { branches: [main] }
>   workflow_dispatch:
> permissions: { contents: read }
> jobs:
>   skill-view-check:
>     name: skill-view-check
>     steps:
>       - uses: actions/checkout@v7
>       - run: bash scripts/skill-view check --source .claude/skills --tree .
> ```
>
> It asserts ADR-0067 §8's absence classes and it is **mutation-proven to fail** — the generated view
> removed and the source root emptied each red it, by run id. The decision, the run ids, the per-repo
> swap order and the one direction the new gate does **not** cover are in
> [`skill-apparatus-retirement-order.md` §5.1](skill-apparatus-retirement-order.md). The
> **generated-product** caller is a different animal and is **not** retired — see "Auditing a subtree of
> the caller's checkout" below; `FS.GG.Rendering`'s `template-base-skill-union.yml` is still live.
>
> If you are here because a repo has no skill gate, wire `skill-view-check`. Wiring the block below
> would re-create the blocker `#1715` cleared.

This is the shape `receives: skill-union` used to mean, and `scripts/repos-audit.sh` requires **both
halves in one workflow file**. It is recorded verbatim:

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
way**: SDD (`a066e0b`) and then Governance (`c577961`) each wired the unfiltered form and armed the
context — the shape prescribed above — measured 2026-07-27.

**Two repairs exist, and this doc takes the first:**

1. **Drop the `paths:` filter, so the job always runs and always reports.** What the block above does.
   It costs a runner-minute of static shell per PR — the assertion needs no SDK, no restore and no
   network — and it is the shape the org already relies on. When
   [#1508](https://github.com/FS-GG/.github/issues/1508) was written, every context Governance required
   came from `gate.yml` or `coordination-coherence.yml`, and both were confirmed on 2026-07-27 to carry
   **no `pull_request` `paths:` or `paths-ignore:` filter at all** — the load-bearing half, and still
   true. Its *"that is all of them"* half has since expired: re-read on 2026-07-27,
   Governance's required set has grown to eight and now also includes `contract-coherence / coherence`
   and `skill-union / skill-union`. `repos-audit`'s detector reads an absent `paths:` as armed
   (Half 2), so this costs nothing in capability terms either.

   (Branch protection **is** readable here — `repos/FS-GG/<repo>/branches/main/protection` — so the
   required-context claims in this document are direct reads, not inferences. An earlier revision said
   that half needed `administration: read` and could not be re-measured; it can, and the two receiver
   claims below were measured that way.)
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
receiver's `main`: **two** of the seven now have the caller — `FS.GG.SDD` (`a066e0b`) and
`FS.GG.Governance` (`c577961`). Both carry a bare `pull_request:` with **no `paths:` key**, and both
have `skill-union / skill-union` in their default branch's required contexts, so both are the
prescribed shape rather than the deadlocking one. The other five ship no
`.github/workflows/skill-union.yml` at all, so correcting the block here still lands ahead of their
adoption. A receiver that wired the filtered form on a branch must drop the `pull_request` `paths:`
filter **before** arming the context, not after — arming first is the deadlock, and it takes the
un-arming PR down with it.

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

### Auditing a subtree of the caller's checkout

**This workflow is checkout-scoped, and that is a capability limit worth reading before you wire it.**
It runs `actions/checkout` of the caller into `caller/` and asserts `caller/${product-path}`. So the
subject must be a path **that exists in the caller's committed tree at the ref under test**. It can
audit a committed subdirectory or a committed runtime root; it **cannot** audit a tree that is
*generated during the run* — packed, installed, scaffolded or built into a workdir by an earlier job or
step — because a `uses:` job gets a clean runner with no such tree and this workflow has **no artifact
input** to receive one.

This heading used to read "Auditing a generated product", which promised the third thing and delivered
the first. That mismatch is how a repo whose subject is genuinely generated
([FS.GG.Templates](#the-generated-subject-shape-and-why-templates-is-not-a-uses-caller) below) came to be
cited under it as this block's first caller — a repo that, for the structural reason above, could never
have been one. The `product-path: <subdir>` **shape** is still
called *generated-product-shaped* elsewhere in this document and in `repos-audit`'s detector — that name
describes the caller's *shape* relative to the receiver contract (it audits something other than the
repo's own roots), **not** a claim that the subject was generated during the run.

```yaml
permissions:
  contents: read
jobs:
  skill-union:
    uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main
    with:
      product-path: "path/to/scaffolded/product"
      # roots: ".claude/skills .agents/skills"                 # default = ADR-0065's two, always
      #                                                        # passed; the product's own
      #                                                        # .agent-skill-roots is NOT consulted here
      # manifest: "path/to/skill-manifest.json"                # enables the digest cross-check
      # co-tenants: "fs-gg-sdd-* speckit-*"                    # undeclared co-tenant ids to admit
      # params: ".fsgg/scaffold-provenance.json"               # enables [missing]/[unexpected] (needs manifest)
```

**The one live caller of this shape is `FS.GG.Rendering`'s**
[`template-base-skill-union.yml`](https://github.com/FS-GG/FS.GG.Rendering/blob/main/.github/workflows/template-base-skill-union.yml)
(`product-path: template/base`, `manifest:` supplied). It audits a tree that is **committed** to the
repository rather than scaffolded by it — neither a runtime root nor a generated output — which is a
shape this document did not previously describe. It is a generated-product-shaped caller, a different
subject from the committed-root union that Rendering's former `receives: skill-union` row named; the
roster's detector says so out loud rather than conflating the two. That row no longer exists:
[#1715](https://github.com/FS-GG/.github/issues/1715) removed it on 2026-07-28, in the same commit that
retired the receiver caller it required. See
[the ninth audited tree](#the-ninth-audited-tree-fsggrenderings-templatebase) below.

#### The generated-subject shape, and why Templates is not a `uses:` caller

**The [FS.GG.Templates composition gate](https://github.com/FS-GG/FS.GG.Templates/issues/49) (roadmap
T3.2) is delivered, in both lanes, and has been since 2026-07-02 — and it is deliberately not a `uses:`
caller of this workflow.** Both halves of what this paragraph used to say were false, in opposite
directions: it first asserted flatly that Templates "is the first caller" (a plan read as a fact), and
[#1559](https://github.com/FS-GG/.github/issues/1559) then replaced that with the assertion that the
caller "was never wired" and that `composition.yml` "still uses the grep-and-skip shape" — the opposite
wrong fact, stated as a measurement. Corrected at
[#1643](https://github.com/FS-GG/.github/issues/1643).

Measured 2026-07-28 against `FS-GG/FS.GG.Templates@main` by contents API rather than code search (see
[Negative existence](#a-negative-existence-claim-needs-a-probe-that-can-say-no) below for why that
distinction is load-bearing):

| probe | result |
| --- | --- |
| `tests/composition/lib/skill-union.sh` | **exists** — blob `468096b`, 47 291 B; defines `assert_skill_union` |
| `assert_skill_union` callers | `FS-GG/FS.GG.Templates@574e90cba82653f4c1aab9f2777eb17fa683c1ba:tests/composition/stages/05-build.sh:157` (orchestrated lane, co-tenants `fs-gg-sdd-*`) and `FS-GG/FS.GG.Templates@574e90cba82653f4c1aab9f2777eb17fa683c1ba:tests/composition/stages/05b-standalone.sh:44` (standalone spec-kit lane, `speckit-*`) — **both lanes** |
| first landed | [`574e90c`](https://github.com/FS-GG/FS.GG.Templates/commit/574e90cba82653f4c1aab9f2777eb17fa683c1ba), merged **2026-07-02**, PR [Templates#51](https://github.com/FS-GG/FS.GG.Templates/pull/51), which discharged Templates#49 by a closing keyword in its body |
| the grep-and-skip shape | **retired by that same PR** — it removed the `scaffold.providerWroteSddTree` grep-and-SKIP lockstep ([Templates#47](https://github.com/FS-GG/FS.GG.Templates/issues/47)) in favour of hard failure; `assert_skill_union`'s unreachable-fetch arm calls `bad`, never `skip` |
| how it runs the assertion | **the same authority script, not a reimplementation** — `dist/skill-union-assert.sh` (the self-contained bundle generated from `scripts/skill-union-assert.sh`), fetched content-addressed at a pinned 40-hex `SKILL_ASSERT_REF` |

So #49 was closed **because it was delivered**. Do cite Templates as precedent — for the *bundle* shape,
not for the `uses:` block above.

**Why it is not, and will not become, a `uses:` caller** — decided at
[Templates#313](https://github.com/FS-GG/FS.GG.Templates/issues/313) and recorded in that repo's
`composition.yml`: Templates' subject is exactly the tree this workflow cannot reach. It is packed →
installed → scaffolded → built into a temp workdir *inside* the `composition` job, so it does not exist
in the checkout, and the checkout-scoped limit stated at the top of this section applies. A `uses:` there
could only ever name a path **committed to Templates** — which would be that repo's own runtime roots, a
different subject entirely (see the gap-row note below). **This is a limit of this reusable workflow, not
a Templates defect.**

**None of this closed `FS.GG.Templates`' `receives: skill-union` row, and this correction must never be
read as having closed it.** That row was about **Templates' own committed runtime roots** and needed a
committed-root caller — default `product-path`, ADR-0065's roots, and a trigger armed over those roots,
all in **one** workflow file. Templates never wired such a workflow, and
[#1742](https://github.com/FS-GG/.github/issues/1742) (2026-07-28) closed the row itself rather than
filling it — `registry/repos.yml` no longer declares `receives: skill-union` for Templates (or Game,
Audio, Net), the capability records `receivers: none`, and `repos-audit` reports **zero** gaps for it
(see [Rollout state](#rollout-state-measured-2026-07-27) below). Two subjects; per
[#1504](https://github.com/FS-GG/.github/issues/1504) and
[#628](https://github.com/FS-GG/.github/issues/628) one green never stood in for the other. Nor was any
gap count ever affected by the wrong paragraph: `repos-audit`'s `caller:` detector reads workflows
structurally (YAML→JSON), never via code search, so it was measuring correctly the whole time this
document was not.

**A generated subject is asserted with the bundle, not with a `uses:`.** That is the supported answer,
and it is the shape documented under
[Standalone fetch](#standalone-fetch--supported-and-it-is-dist-not-scripts) below: fetch
`dist/skill-union-assert.sh` at a pinned 40-char SHA and run it against the workdir from inside the job
that built it. `skill-union-bundle` reds on any drift between the bundle and its source, so the bundle
lane asserts the same semantics as this workflow.

**Neither caller passes `params:` yet.** The condition-aware check (ADR-0017, check 4) has **no live
caller in the org** as of 2026-07-28: Templates' two arms invoke the script as `--product` and as
`--product --manifest --co-tenants`, with no `--params`; Rendering's `template-base-skill-union.yml`
supplies `manifest:` and no `params:`. See
[Condition-aware (check 4)](#condition-aware-check-4----params-adr-0017) above.

### A negative existence claim needs a probe that can say "no"

**`GET /search/code` is not a valid negative existence proof for an FS-GG repository.** This is the
reusable half of [#1643](https://github.com/FS-GG/.github/issues/1643), and it generalises well past this
capability, so it is written here rather than left in the issue.

The wrong paragraph corrected above was not a stale index and not a typo. It rested on a code search that
returned 0 hits — and `/search/code` returns **0 for every term** in a repository that is not
code-search-indexed. `FS-GG/FS.GG.Templates` is such a repository. Measured 2026-07-27:

| probe | hits |
| --- | --- |
| `q=skill-union+repo:FS-GG/FS.GG.Templates` | 0 |
| `q=composition+repo:FS-GG/FS.GG.Templates` | 0 |
| `q=repo:FS-GG/FS.GG.Templates+scaffold` | 0 |

The third row is the control, and it is why the first row proves nothing: `scaffold` is unarguably all
over that repository. The endpoint **cannot distinguish "absent" from "not indexed"** — it emits the same
`0` either way, so it is a probe that cannot fail, reporting a fact nobody could read off it. That is the
[#266](https://github.com/FS-GG/.github/issues/266) shape at the measurement layer, and note it points in
*whichever* direction the reader wants: a 0 here has been used to assert absence, and could as easily be
used to assert that some other repo is clean.

**Use instead**, in preference order:

1. **A structural detector** — `scripts/repos-audit.sh` parses each workflow YAML→JSON and matches on
   shape. It never consulted `/search/code`, which is exactly why its gap counts were right while this
   document's prose was wrong.
2. **The contents / git API** — `GET /repos/<owner>/<repo>/contents/<path>` distinguishes `200` from
   `404` at a named path, and `GET /repos/<owner>/<repo>/git/trees/<sha>?recursive=1` enumerates a whole
   tree. Both answer from the object store, not from an index.
3. **A clone**, then `grep`. Slowest, and the only one that proves absence of a *string* rather than of a
   *path*.

**And always run a control.** Whatever the probe, issue it once for a term you *know* is present. A
negative result from an instrument that has not been shown to produce a positive is not a measurement.
Every existence claim in this document — the rollout table's per-row commits included — is subject to
this rule.

### Rollout state (measured 2026-07-27)

**The rostered repository trees are eight**, one committed tree each, and enumerating them here is
deliberate: "how many trees are audited?" must have an answer that is *read* rather than counted from
memory. The subject set today is **nine trees** — these eight, plus `FS.GG.Rendering`'s `template/base/`
subdirectory, a different shape recorded
[below](#the-ninth-audited-tree-fsggrenderings-templatebase) rather than as a ninth row here. How many
of them a *gate* keeps re-checking is a smaller number, and a separate question; it is answered under
the table, because conflating the two is the error this section keeps having to repair. **Every row
carries the commit it was measured at**, because a row without one is a summary, and summaries from this
document have misdirected three repairs already. Each was produced by running, over a fresh clone at
that commit:

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
| `FS.GG.Governance` | `c577961` | 15 | 15 / 15 / 15 | coherent — **repaired** (`9d8359c`), wired second |
| `FS.GG.SDD` | `a066e0b` | 32 | 32 / 32 / 32 | coherent — **repaired**, and the first wired receiver |
| `FS.GG.Rendering` | `e2d860b` | 50 | 50 / 50 / 50 | coherent — **repaired** ([Rendering#1080](https://github.com/FS-GG/FS.GG.Rendering/issues/1080)), wired third |

**All eight rows read coherent.** `FS.GG.Rendering` was the last holdout — 46 partitioned *and* 30
divergent at `ee5e6c3` — and its rematerialization landed on 2026-07-27. Re-measured here at `e2d860b`:
union **50**, `50 / 50 / 50`, **0 partitioned and 0 divergent**, comparing the `SKILL.md` blob of every
id across all three roots.

**Read that as eight rows, not as a live org-wide green, because the rows are not equally alive.** A
row is a claim about *its own commit*, and only the trees whose repo has wired the caller are re-checked
after it: `FS.GG.SDD`, `FS.GG.Governance` and `FS.GG.Rendering` had a gate that re-asserted theirs on
every push, and `.github` asserts itself via
[`skill-roots-selfcheck.yml`](../../.github/workflows/skill-roots-selfcheck.yml). The other four —
`FS.GG.Templates`, `FS.GG.Game`, `FS.GG.Audio`, `FS.GG.Net` — never wired a caller, and per
[#1742](https://github.com/FS-GG/.github/issues/1742) no longer declare the receiver row that would have
required one (see [Rollout state](#rollout-state-measured-2026-07-27) below — the four open gaps these
rows used to be are now closed, by deletion of the row rather than by wiring). Their rows above are
still **hand measurements that nothing has re-checked since**, and each could have drifted the
moment after it was taken without anything going red. That is not a footnote on the table, it *is* the
remaining rollout: a coherent row and a wired gate are different facts, and only the second one keeps
being true.

> **SUPERSEDED FOR THE THREE WIRED ROWS, 2026-07-28 ([`#1715`](https://github.com/FS-GG/.github/issues/1715)).**
> `FS.GG.SDD`, `FS.GG.Governance` and `FS.GG.Rendering` no longer wire this caller — it is retired, for
> the reason in "The required receiver caller" above — so **no tree in this table is re-checked by
> `skill-union-assert` any more except `.github`'s own** (via `skill-roots-selfcheck.yml`) and
> `FS.GG.Rendering`'s `template/base/` subdirectory (a different, still-live shape). All eight rows are
> now hand measurements, and the paragraph above should be read as applying to eight repos rather than
> four.
>
> **That is a narrowing of THIS gate's reach and not of the org's coverage**, and the distinction is the
> point: the three repos each gained a required `skill-view-check` context that asserts ADR-0067 §8's
> absence classes over their own roots on every pull request, and unlike the rows above it is
> mutation-proven to fail. What it does **not** assert is the cross-root **byte** comparison this table
> reports, for as long as a repo still commits two independent copies. That gap is real, is bounded by
> each repo's own phase-4 retirement, and is recorded in
> [`skill-apparatus-retirement-order.md` §5.1](skill-apparatus-retirement-order.md) rather than left for
> a reader of this table to infer.

This rollout is moving fast enough to invalidate a snapshot mid-edit, and it did: Governance's caller
landed while this section was being rewritten, taking the gap count from 6 to 5 between two runs of
`repos-audit` an hour apart. That is the argument for the commit column, not an excuse for it — a row
you can re-measure is repairable, a bare number is not.

**This table used to end "Nothing is `[divergent]`: every skill present in more than one root is
byte-identical." That was false, and it was false because the gate had not looked**
([#1506](https://github.com/FS-GG/.github/issues/1506) — a `[partitioned]` id short-circuited past the
byte comparison, and `byte-identical=4` then counted only the 4 ids that reached it). Over the roots
each id **is** present in — **before** the two repairs, which is where the lesson lives, and after:

| tree | at | comparable (≥2 roots) | identical | **differing** | single-root (not comparable) |
| --- | --- | --- | --- | --- | --- |
| `FS.GG.Governance` | `9243c07` (pre) | 4 of 15 | 4 | 0 | **11** |
| `FS.GG.Governance` | `c577961` (now) | 15 of 15 | 15 | 0 | 0 |
| `FS.GG.SDD` | `f419f0e` (pre) | 21 of 32 | 21 | 0 | **11** |
| `FS.GG.SDD` | `a066e0b` (now) | 32 of 32 | 32 | 0 | 0 |
| `FS.GG.Rendering` | `ee5e6c3` (pre) | 50 of 50 | 20 | **30** | 0 |
| `FS.GG.Rendering` | `e2d860b` (now) | 50 of 50 | 50 | 0 | 0 |

**The pre rows are the point.** Governance and SDD really were drift-free — but over **4 and 21** ids,
not 15 and 32, and the summary that said so never named the denominator. That is the whole of #1506 in
two rows, and it survives the repair only if the rows do.

*(This table previously gave SDD's pre-repair line as "21 of **33**", with a note calling the 33-vs-32
mismatch an in-flight snapshot. It was neither in flight nor 33: re-measured at `f419f0e`, the union is
**32** — 21 comparable + 11 single-root — and 21 + 12 never summed to a union at all. Corrected here
rather than quietly dropped, on the same principle as the rest of this section.)*

Rendering's repair was **both** kinds: 46 missing projections *and* 30 divergent pairs. A
byte-comparison-only checker would miss the partitions; the checker that short-circuited on partitions
missed the drift. Both are closed at `e2d860b`, and the row above is retained rather than dropped for
the same reason as the Governance and SDD pre rows — a repair whose evidence is deleted cannot be
re-checked.

#### Name the partitioned set; do not describe it by its producer (#1509)

Each of the three partitioned trees had its ids described here by naming a **producer set**, and two of
the three descriptions were wrong in the same way. Enumerated below at the commit where the partition
existed — which is now the **pre-repair** commit for all three, Rendering's included:

| tree | at | partitioned | what the ids actually are | what this doc used to say |
| --- | --- | --- | --- | --- |
| `FS.GG.Governance` | `9243c07` | 11 | **10** `speckit-*` **+ 1 producer-less `spectre-console`** (repo-native here) | "the `speckit-*` set" (at `9bb9856`) |
| `FS.GG.SDD` | `f419f0e` | 28 | **17** `fs-gg-sdd-*` **+ 10** `speckit-*` **+ 1 producer-less `spectre-console`** (vendored here) | "`fs-gg-sdd-*` + `speckit-*`" (at `9bb9856`) |
| `FS.GG.Rendering` | `ee5e6c3` | 46 | **30** `fs-gg-*` **+ 16** `speckit-*`, no producer-less member | "the product/Speckit set" (before `22461b4`) — this one *does* hold |

In both wrong cases the **count was right and the attribution was wrong**, and the missing member was
the same skill: `spectre-console`, a [co-tenant with no producer](#a-co-tenant-need-not-have-a-producer-at-all-1509)
and no `registry/skills.yml` row. Driving only the producer set this document named would have
rematerialized 10 of Governance's 11 and, for SDD, 10 of 28 from `.specify/` — or 27 of 28 counting all
three of its authorities — and left the remainder partitioned.

**What actually happened is the useful part, and it is not a war story.** Neither repair shipped a red
gate, because neither worker believed the attribution: Governance's caught it first and filed #1509,
recording the outcome as a counterfactual — *"a repair driving only `.specify/` **would have** left it
partitioned and the gate red"* — and SDD's classified `spectre-console` as a fourth authority in its
own producer table before materializing anything. Both then handled it **inside** the same materializer
as an explicit authority row, not as an out-of-band fix-up, and SDD deliberately armed its required
context only *after* the coherent merge so the gate would be green from its first run. The cost of the
mis-attribution was paid in re-derivation by two workers, not in red CI — which is the cheap outcome,
and it happened because they measured instead of reading this table.

A producer-set description is a *guess about where the ids came from* wearing the clothes of a
measurement. Enumerate the set, or say the breakdown is unmeasured; do not name a producer and let a
reader infer coverage from it.

At this measurement, `skill-union` was rostered on all seven framework repos, and **three had wired
it**: `FS.GG.SDD` (`a066e0b`), `FS.GG.Governance` (`c577961`) and `FS.GG.Rendering` (`e2d860b`), each of
which had also made `skill-union / skill-union` required on its default branch. So the scheduled
[`repos-audit`](../../.github/workflows/repos-audit.yml) reported **4 gaps** — measured 2026-07-27 by
running `scripts/repos-audit.sh` from this repo: 32 receiver-capability pairs, 28 wired, 4 gaps
(Templates, Game, Audio, Net), 0 unrostered adopters, 0 undetermined, every other capability green.

> **SUPERSEDED, 2026-07-28 ([`#1742`](https://github.com/FS-GG/.github/issues/1742)).** The 4 gaps
> above are not open any more, and did not close by anyone wiring a caller: Templates, Game, Audio and
> Net never wired one, and per [the retirement above](#the-required-receiver-caller--retired-2026-07-28-do-not-wire-a-new-one)
> now never may. `#1742` deleted their `receives: skill-union` rows from `registry/repos.yml` instead of
> filling them, so the capability records `receivers: none` and there is nothing left to gap. Measured
> on `main` after `#1742`, `scripts/repos-audit.sh` prints, verbatim: `skill-union (a
> .github/workflows/skill-union-assert.yml caller aimed at this repo's OWN committed .claude/.agents
> skill roots) — 0 receivers, as recorded; every rostered repo was scanned and none adopts it. The claim
> holds.`, and the org-wide summary line reads `0 gap(s)`. That is a **different** ratchet outcome than
> [#1504](https://github.com/FS-GG/.github/issues/1504) originally asked for — this closed by retiring
> the row rather than by every tree passing `skill-union-assert` — and the two paragraphs below, about
> the 2026-07-27 measurement and why the count moved 5 → 4, are history of the shape that was decided
> against, not a remaining plan.

**The gap count moved 5 → 4, and it is worth being exact about why, because the obvious reason is the
wrong one.** It did *not* move because `template/base/` came under audit. A gap is a **receiver**
capability — a repo that declared `receives: skill-union` and wired a caller at its own committed roots
— and the `template/base` caller is a generated-product-shaped subject that closes no receiver row at
all. `repos-audit` says so in its own words, refusing exactly that substitution: *"A call aimed at a
GENERATED product (`product-path: <subdir>`), or narrowed with `roots:`, is a different subject and
deliberately does not count."* The one gap that closed is `FS.GG.Rendering`'s, and it closed because
Rendering wired its **own** `skill-union.yml` at `product-path` default `.` — a second, unrelated
workflow. Counting the generated-product caller as a closed gap is precisely the error
[#628](https://github.com/FS-GG/.github/issues/628) was filed about.

All seven receivers were root-coherent at this measurement, and of those Governance, SDD and Rendering
had also wired the caller. The remaining four — Templates, Game, Audio and Net — never wired it, and per
[#1742](https://github.com/FS-GG/.github/issues/1742) their `receives: skill-union` rows are gone rather
than outstanding: there is nothing left for them to do here, and the retirement above forbids wiring a
new caller regardless. All three cross-repo rematerialization requests are now **closed**:
[SDD#716](https://github.com/FS-GG/FS.GG.SDD/issues/716),
[Governance#326](https://github.com/FS-GG/FS.GG.Governance/issues/326) and
[Rendering#1080](https://github.com/FS-GG/FS.GG.Rendering/issues/1080), their rematerializations having
landed in `a066e0b`, `9d8359c` and Rendering's `main` respectively — each from its authoritative
producer rather than by copying an arbitrary root. Each repair was independent of the other two — a
repo's roots are its own — and each depended on this repo only for the caller shape above and the
roster row.

#### The ninth audited tree: `FS.GG.Rendering`'s `template/base/`

One tree used to sit outside this capability's subject **and** outside the composition gate's, so
nothing audited it: `FS.GG.Rendering`'s `template/base/`. It is neither a committed runtime root nor a
scaffolded product, so it fell between the two subjects — and a tree that every gate believes is
someone else's is the [#266](https://github.com/FS-GG/.github/issues/266) shape exactly. It was filed
as a decision item, [Rendering#1081](https://github.com/FS-GG/FS.GG.Rendering/issues/1081), and **that
decision has been taken — the opposite way to the guess this document used to carry.**

**Outcome (Reading B, 2026-07-27): complete the tree, do not strip it.** The `.claude/skills/` copy is
explicitly **not** a residual ADR-0011 §3 provider leak; `.codex/` joined it. The reasoning is that the
standalone lane has no `fsgg-sdd` orchestrator to compute the union and fan it out, so a scaffolding
base tree must carry the roots itself. This does not overturn `specs/229-drop-claude-skills-mirror`,
which governs what a **provider** writes; a base tree for an orchestrator-less lane is a different
subject, and the two disagree without contradiction.

Measured at Rendering `e2d860b`: `template/base/` carries all three roots, each holding the single id
`fs-gg-project`, byte-identical across them (the same blob `e3846977…`, 6275 B), and its canonical
digest is `c9fac83f…` — the value
[`template/skill-manifest/skill-manifest.json`](https://github.com/FS-GG/FS.GG.Rendering/blob/main/template/skill-manifest/skill-manifest.json)
declares for that id, recomputed here rather than transcribed.

It is now gated by
[`template-base-skill-union.yml`](https://github.com/FS-GG/FS.GG.Rendering/blob/main/.github/workflows/template-base-skill-union.yml)
— a caller of this repo's reusable `skill-union-assert.yml` with `product-path: template/base` and
`manifest:` supplied, so all three checks are live over it, the digest cross-check included. Landed in
[Rendering PR #1083](https://github.com/FS-GG/FS.GG.Rendering/pull/1083); green there and on the merge
commit as the context **`template-base-skill-union / skill-union`**.

**This caller audits a different subject from the committed-root union that
`FS.GG.Rendering`'s former `receives: skill-union` row named, and must never be read as the same
thing.** The roster's detector keeps those subjects distinct rather than counting the
generated-product-shaped caller as evidence about Rendering's own committed roots. The receiver row
no longer exists: [#1715](https://github.com/FS-GG/.github/issues/1715) removed it on 2026-07-28, in the
same commit that retired Rendering's separate `skill-union.yml` receiver caller
([Rendering#1080](https://github.com/FS-GG/FS.GG.Rendering/issues/1080)). It changes no gap count; see
the paragraph above for why the count nevertheless moved.

**It is also a caller shape this document had not described** — and, measured 2026-07-28, the **only
live** generated-product-shaped `uses:` caller in the org. It audits a **committed subdirectory**, which
is neither a runtime root nor a generated output, and that is precisely why it *can* be a `uses:` caller:
the subject is in the checkout. Templates#49 audits a genuinely scaffolded artifact, which this workflow
structurally cannot reach, so it asserts the union with the pinned `dist/` bundle instead — delivered
2026-07-02 by [Templates#51](https://github.com/FS-GG/FS.GG.Templates/pull/51), decided at
[Templates#313](https://github.com/FS-GG/FS.GG.Templates/issues/313), corrected here at
[#1643](https://github.com/FS-GG/.github/issues/1643) (this paragraph previously said it "was closed
without wiring anything"). So
the audited set is nine trees, not eight: the eight rostered repository trees in the table above, plus
this one — recorded here rather than as a ninth row, because that table enumerates rostered
repositories and this is a subdirectory of one of them.

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
and manifest-drifted root, and that `--digest` equals the producers' `sha256sum SKILL.md` for a
BOM-free, LF-only body (the cases where the canonical digest deliberately *differs* from raw
`sha256sum` are `digestVectors`, measured against the library). It also pins
[#1506](https://github.com/FS-GG/.github/issues/1506): a tree that is **partitioned *and* divergent**
reports both diagnostics for the same id, every partitioned id is compared rather than just the first,
the summary is asserted byte-for-byte with its populations, no denominator-free `byte-identical=` may
reach it on any tree, and both a one-root skill and a one-root **root set** count `single-root` rather
than claiming a byte-identity nothing established. It pins
[#1513](https://github.com/FS-GG/.github/issues/1513) the same way: a tree that is **partitioned *and*
digest-mismatched** reports both facts with the drift naming **every** root it was found in, the
manifest counts are asserted byte-for-byte with their populations, no denominator-free
`manifest-matched=` may reach the summary, a drift in a **non-reference** root is still caught and
named (the fail-open that decided per-root), a partition of manifest-**matching** copies manufactures
**no** `[drifted]`, and a declared id with no reference digest counts `manifest-no-reference` rather
than as a match. **Six of those seven legs fail against the pre-#1513 script**; the seventh is the
no-manufacture leg, which must pass against both. It additionally runs
[`skillmirror-conformance.sh`](../../tests/skill-union/skillmirror-conformance.sh) — see [what pins
#120's alignment claim](#what-pins-120s-alignment-claim-1513). For the
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
