# ADR-0065: One agent-skill root contract for framework repos and product workspaces

- **Status:** Accepted
- **Date:** 2026-07-22
- **Affects:** FS-GG/.github, every coordination-kit receiver, FS.GG.SDD, FS.GG.Rendering, and scaffolded product workspaces
- **Amends:** [ADR-0014](0014-skill-vendoring-one-manifest-one-materialize-verify.md) Decision 5; interacts with [ADR-0019](0019-org-repo-roster-registry-and-coordination-kit.md) and [ADR-0062](0062-versioned-kit-package-replaces-byte-copy-sync.md)
- **Clarifies:** [ADR-0014](0014-skill-vendoring-one-manifest-one-materialize-verify.md) Decision 1 and [ADR-0062](0062-versioned-kit-package-replaces-byte-copy-sync.md): a skill is a directory transport unit, while runtime catalog exposure is a host policy over those materialized directories.
- **Amended by:** [ADR-0067](0067-resolve-dont-copy-one-skill-source-two-runtime-roots-a-generated-view.md) §5 — **EXECUTED 2026-07-28 ([#1636](https://github.com/FS-GG/.github/issues/1636)). The ordered root set is now TWO: `.claude/skills`, `.agents/skills`.** ADR-0067 decided the direction and explicitly said it decided *"the direction, not the flip"*; the flip was authorised on `#1636` by the maintainer on 2026-07-27 and lands with this amendment, in the same change, as ADR-0067's Consequences require. `.codex/skills` is retired. This record otherwise stays IN FORCE, and its transport contract — including the prohibition on deleting a mirror to hide a duplicate — is **unchanged and still governs**: see §Decision and §Retiring a root, which is what makes this an amendment rather than a violation of it. ADR-0067 §6's *generated view* is a separate, later mechanism and is **not** landed here; both roots remain committed copies today.

## Context

Product materialization uses `.claude/skills`, `.codex/skills`, and `.agents/skills`, while the
coordination-kit lane defaulted to `.claude/skills` and `.agents/skills`. That exception made Codex
availability depend on which delivery lane installed a skill and produced real drift across framework
repos. `coordination-sync`, `FS.GG.Kit`, the local roots declaration, tests, and documentation each
restated the exception.

The triggers legitimately differ: `fsgg-sdd scaffold/refresh` owns generated workspaces and
`FS.GG.Kit` owns framework coordination skills. The runtime surface does not need to differ.

## Decision

Every FS-GG skill materializer defaults to the ordered root set:

```text
.claude/skills
.agents/skills
```

> **AMENDED 2026-07-28 ([ADR-0067](0067-resolve-dont-copy-one-skill-source-two-runtime-roots-a-generated-view.md) §5,
> executed by [#1636](https://github.com/FS-GG/.github/issues/1636)).** The set was three; `.codex/skills`
> is **retired**. `.agents/skills` is Codex's own second native root — re-measured on Codex CLI 0.145.0
> for this change, not inherited from phase 1: in a tree carrying the same skill under all three roots,
> `codex debug prompt-input` renders it **twice**, once from `.codex/skills` and once from
> `.agents/skills`, and never from `.claude/skills`; in a two-root tree it renders **once**, from
> `.agents/skills`, with no configuration at all. So the third root had no runtime the other two did
> not, and its only observable effect was the duplicate catalog entry §Runtime exposure had hosts
> suppress. That suppression is now unnecessary and is removed (`docs/coordination/README.md`).
>
> ADR-0067 §6's *generated view* is a **separate mechanism and is not landed here**; §9's "nothing is
> retired before its replacement is proven" governs that mechanism, not this root-set narrowing, which
> ADR-0067's own Alternatives call *"separable rather than optional"*. Both remaining roots are still
> committed copies.

`Fsgg.Schemas.agentSkillRoots` remains the contract definition for product materialization. The
coordination-kit package and its compatibility writer use the same ordered values. A checked-in
`.agent-skill-roots`, `AGENT_SKILL_ROOTS`, or an explicit command argument may override the set for an
intentional single-runtime or experimental tree; an override is a reviewed exception, not a lane
default.

Materialization creates parent directories, writes real files, and verifies canonical-body hashes.
The skill's owner and canonical source remain unchanged. Product and framework triggers remain
separate adapters over the same root contract; this ADR does not centralize skill authorship or make
the coordination package responsible for provider skills.

The transported unit is the complete skill directory, not only `SKILL.md`. `SKILL.md` remains the
required entry point and its canonical digest remains a compatibility field, but a current manifest
also records every regular file beneath the skill directory, its relative path, digest, and executable
mode, plus a whole-tree digest. Materializers reject a missing, additional, modified, or mode-divergent
resource. This explicitly supersedes ADR-0014 Decision 1's earlier body-only wording; it does not erase
that decision's one-owner, content-addressed-manifest rationale.

Transport parity and runtime exposure are deliberately different contracts:

- **Transport/parity:** every declared root carries the same complete directories, including
  `references/`, `agents/openai.yaml`, and executable resources. Deleting a mirror to avoid duplicate
  discovery violates the transport contract. **This is unchanged by the 2026-07-28 amendment** — see
  §Retiring a root, which is the only sanctioned way a root leaves the set.
- **Runtime exposure:** each host chooses which transported root it catalogs. Claude uses its supported
  skill surface (`.claude/skills`). Codex natively discovers `.agents/skills`. Explicit-only policy in
  `agents/openai.yaml` controls selection, not delivery.

Thus “two roots are present” does not mean “two copies must appear in one host's picker,” and “one
catalog entry is visible” does not permit a receiver to carry only one root.

## Retiring a root

A root leaves the declared set by **contract migration**, never by deletion. The distinction this
record's transport contract draws is not between "the directory is gone" and "the directory is there" —
it is between *hiding* a duplicate and *deciding* one is unnecessary:

- **Forbidden (unchanged):** deleting or desynchronizing a mirror of a root that is still declared, to
  make a duplicate catalog entry go away. The tree then lies about the contract, and the next
  materialization silently restores what the operator removed.
- **Sanctioned:** narrowing the declared set itself, in one change that migrates every consumer of it,
  amends this record, and gives the materializer the means to complete the retirement on receivers.

The last clause is load-bearing and was discovered by measurement in `#1636`: the kit materializer's
stale-file sweep only visits roots the manifest still maps into, so dropping a root from the set makes
the materializer **stop looking at it**, and the retired copies would survive on every receiver
forever. `FS.GG.Kit` therefore declares `FsggKitRetiredSkillRoots` alongside `FsggKitSkillRoots`, and
removes the kit's own skill directories from each retired root on the receiver's next restore — leaving
any skill the receiver itself put there untouched. A receiver never hand-deletes a mirror; the
materializer that created it is the thing that removes it.

## Consequences

- Codex receives every coordination-kit skill by default in framework repos and wired workspaces.
- `coordination-sync`, `FS.GG.Kit`, local parity checks, and the coordination engine's drift advisory
  all understand the same roots.
- **2026-07-28:** receivers LOSE their committed `.codex/skills` copies on their next kit
  materialization, by the materializer. Codex's effective catalog cost on this repo's own driver
  skills falls from **6335 characters across its two native roots to 3174 across one** — measured with
  `scripts/generate-driver-manifest --catalog-report` before and after — and the per-machine
  `[[skills.config]]` suppression that duplicate forced is deleted rather than documented.
- Manifests and package materializers carry complete directories and executable modes. The historical
  `SKILL.md` digest remains additive compatibility metadata until all readers can rely on the tree
  manifest.
- Host-specific duplicate suppression is configuration outside the materialized tree and cannot alter
  the parity verdict. With two roots there is no duplicate left to suppress here.
- Adding **or removing** a runtime root remains a contract migration, followed by package publication
  and receiver re-materialization. Removing one additionally requires the retired-root declaration in
  §Retiring a root, or the retirement never reaches a receiver.
- Rendering's standalone vendored mirror remains byte-equivalent to `Fsgg.SkillMirror`; replacing it
  with a runtime package dependency is rejected because standalone/offline scaffolds must keep working.
