# ADR-0065: One agent-skill root contract for framework repos and product workspaces

- **Status:** Accepted
- **Date:** 2026-07-22
- **Affects:** FS-GG/.github, every coordination-kit receiver, FS.GG.SDD, FS.GG.Rendering, and scaffolded product workspaces
- **Amends:** [ADR-0014](0014-skill-vendoring-one-manifest-one-materialize-verify.md) Decision 5; interacts with [ADR-0019](0019-org-repo-roster-registry-and-coordination-kit.md) and [ADR-0062](0062-versioned-kit-package-replaces-byte-copy-sync.md)
- **Clarifies:** [ADR-0014](0014-skill-vendoring-one-manifest-one-materialize-verify.md) Decision 1 and [ADR-0062](0062-versioned-kit-package-replaces-byte-copy-sync.md): a skill is a directory transport unit, while runtime catalog exposure is a host policy over those materialized directories.
- **Amended by:** [ADR-0067](0067-resolve-dont-copy-one-skill-source-two-runtime-roots-a-generated-view.md) (2026-07-27) — **direction only; this record stays IN FORCE, the ordered root set is still three, and its transport contract still forbids deleting a mirror.** ADR-0067 decides that the *end state* is two roots — `.agents/skills` + `.claude/skills` — and that a runtime root becomes a checkout-time generated view rather than a committed copy. Executing that is a contract migration with the publication and re-materialization tail this record's own Consequences name, so it amends §Decision **in the change that lands the mechanism**, not here. See §Decision.

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
.codex/skills
.agents/skills
```

> **2026-07-27 ([ADR-0067](0067-resolve-dont-copy-one-skill-source-two-runtime-roots-a-generated-view.md) §5–§6):**
> this ordered set is **unchanged and still governs**. The decided end state drops `.codex/skills`
> — `.agents/skills` is Codex's own second native root (measured, Codex CLI 0.145.0), so the third
> root has no runtime behind it and its only effect is the duplicate catalog entry this record's
> §Runtime exposure has hosts suppress. Dropping it is a contract migration and lands with the
> replacement mechanism, never before it (ADR-0067 §9).

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
  discovery violates the transport contract.
- **Runtime exposure:** each host chooses which transported root it catalogs. Claude uses its supported
  skill surface. Codex natively discovers `.agents/skills`; an installation that also exposes
  `.codex/skills` suppresses only the duplicate catalog entries through supported runtime
  configuration. Explicit-only policy in `agents/openai.yaml` controls selection, not delivery.

Thus “three roots are present” does not mean “three copies must appear in one host's picker,” and “one
catalog entry is visible” does not permit a receiver to carry only one root.

## Consequences

- Codex receives every coordination-kit skill by default in framework repos and wired workspaces.
- `coordination-sync`, `FS.GG.Kit`, local parity checks, and the coordination engine's drift advisory
  all understand the same three roots.
- Existing two-root receivers gain committed `.codex/skills` copies on their next kit materialization.
- Manifests and package materializers carry complete directories and executable modes. The historical
  `SKILL.md` digest remains additive compatibility metadata until all readers can rely on the tree
  manifest.
- Host-specific duplicate suppression is configuration outside the materialized tree and cannot alter
  the three-root parity verdict.
- Adding another runtime root remains a contract migration, followed by package publication and
  receiver re-materialization.
- Rendering's standalone vendored mirror remains byte-equivalent to `Fsgg.SkillMirror`; replacing it
  with a runtime package dependency is rejected because standalone/offline scaffolds must keep working.
