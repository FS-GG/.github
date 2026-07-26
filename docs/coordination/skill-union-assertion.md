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
2. **byte-identical** — its bytes are identical across all roots (a diff is a *divergent* root);
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
that gate's subject. **Measured on `origin/main`, 2026-07-27** — Governance `.claude=15 .codex=4
.agents=4`; Rendering `.claude=50 .codex=4 .agents=50`; SDD `.claude=32 .codex=21 .agents=4` — with
every multi-root skill byte-identical. Nothing had *drifted*; projections were **missing**, for 11, 46
and 28 skills. All three were green on `coordination-coherence` throughout, because the four kit skills
really are coherent in all three of their roots.

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
  pull_request:
    paths:
      - ".claude/skills/**"
      - ".codex/skills/**"
      - ".agents/skills/**"
      - ".github/workflows/skill-union.yml"
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
changes: either it carries `paths:` covering all three, or it carries no `paths:` filter at all (wider,
and therefore fine). A `paths-ignore:` naming a root disarms it. A `push`-only workflow reports nothing
on a pull request, so it can never be the required check. **An unarmed gate is not a gate** — the
[#332](https://github.com/FS-GG/.github/issues/332)/[#334](https://github.com/FS-GG/.github/issues/334)/[#880](https://github.com/FS-GG/.github/issues/880)
class, where the check is correct and its trigger is what fails open.

Both halves must be in the *same* file, because a trigger cannot arm a workflow it is not in: the roots
change, the other workflow runs, and the one that audits them does not.

**Make the resulting context required** on the receiver's default branch. GitHub names it
`skill-union / skill-union` (`<caller job> / <callee job>`) — see
[reusable-workflow-contract](reusable-workflow-contract.md), and note that the callee job id is
**public API** the authority may not rename without sequencing it.

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

### Rollout state (as of 2026-07-27)

**The audited trees are the org's rostered repositories — eight of them**, one committed tree each, and
enumerating them here is deliberate: "how many trees are audited?" must have an answer that is *read*
rather than counted from memory. Measured over `main` per repo, comparing every blob under the three
roots:

| tree | union | `.claude` / `.codex` / `.agents` | verdict |
| --- | --- | --- | --- |
| `.github` (authority — asserts itself) | 13 | 13 / 13 / 13 | coherent |
| `FS.GG.Templates` | 4 | 4 / 4 / 4 | coherent |
| `FS.GG.Game` | 21 | 21 / 21 / 21 | coherent |
| `FS.GG.Audio` | 20 | 20 / 20 / 20 | coherent |
| `FS.GG.Net` | 4 | 4 / 4 / 4 | coherent |
| `FS.GG.Governance` | 15 | 15 / 4 / 4 | **11 partitioned** (the `speckit-*` set) |
| `FS.GG.SDD` | 32 | 32 / 21 / 4 | **28 partitioned** (`fs-gg-sdd-*` + `speckit-*`) |
| `FS.GG.Rendering` | 50 | 50 / 4 / 50 | **46 partitioned** (the product/Speckit set, absent from `.codex`) |

Nothing is `[divergent]`: every skill present in more than one root is byte-identical. The defect is
**missing projections**, which is why a byte-comparison-only checker sees nothing wrong.

`skill-union` is rostered on all seven framework repos and wired by none of them yet, so the scheduled
[`repos-audit`](../../.github/workflows/repos-audit.yml) reports **7 gaps** (measured: 32
receiver-capability pairs, 25 wired, 7 gaps, 0 unrostered adopters — every other capability green). That
is the ratchet [#1504](https://github.com/FS-GG/.github/issues/1504) asks for, not a defect: the rollout
is complete only when `scripts/skill-union-assert.sh --product <fresh origin/main tree>` passes for every
tree above **and** every receiver check is green.

Four of the seven — Templates, Game, Audio, Net — are already root-coherent and need only the caller
above. Three must **rematerialize from their authoritative producers first** (not copy an arbitrary
root, and proving an idempotent second materialization), and those repairs are filed and sequenced as
cross-repo requests against Governance, Rendering and SDD.

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
and manifest-drifted root, and that `--digest` equals the producers' `sha256sum SKILL.md`. For the
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
