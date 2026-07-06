# Org repo roster

Human projection of [`registry/repos.yml`](../../registry/repos.yml) — the single authoritative
list of the FS-GG framework repos the org fabrics iterate, and the capabilities each one
participates in (ADR-0019). Update **both** when a repo joins/leaves or a repo's `receives`
capabilities change, and prepend an entry to [`registry/repos.CHANGELOG.md`](../../registry/repos.CHANGELOG.md).

Sibling projection of [`compatibility.md`](compatibility.md) (which projects `dependencies.yml`).
The roster is validated by [`scripts/repos.sh validate`](../../scripts/repos.sh); every fabric reads
it via `repos.sh list --receives <cap>` instead of hardcoding the repo list.

## Participants

| Repo | Role | Receives |
|---|---|---|
| `FS-GG/.github` | **authority** | `labels` |
| `FS-GG/FS.GG.SDD` | framework | `labels`, `coordination-kit` |
| `FS-GG/FS.GG.Rendering` | framework | `labels`, `coordination-kit` |
| `FS-GG/FS.GG.Governance` | framework | `labels`, `coordination-kit` |
| `FS-GG/FS.GG.Templates` | framework | `labels`, `coordination-kit` |
| `FS-GG/FS.GG.Game` | framework | `labels`, `coordination-kit` |

**Authority.** `.github` holds the canonical fabrics and the coordination kit and mirrors them out
(the analog of `fsgg-sdd` for product skills). It is the SOURCE of the coordination kit, so it never
*receives* `coordination-kit` — an invariant the validator enforces.

**Participation audit.** The fabrics are opt-in (a receiver participates by calling a reusable
`.github` workflow), so `receives` only *declares* intent. [`scripts/repos-audit.sh`](../../scripts/repos-audit.sh)
— run weekly by [`repos-audit.yml`](../../.github/workflows/repos-audit.yml) — closes the loop:
for each capability that maps to a reusable workflow (today `coordination-kit` →
`coordination-coherence.yml`), it verifies every declared receiver actually calls it. A
declared-but-unwired repo fails the audit, so `receives` has teeth.

## Capabilities (`receives` vocabulary)

| Capability | What the repo participates in | Consumer | Status |
|---|---|---|---|
| `labels` | the shared cross-repo labels | `scripts/apply-labels.sh` | **migrated** (ADR-0019 slice 1) |
| `coordination-kit` | the `cross-repo-coordination` skill + the `fsgg-coord` client | `scripts/coordination-sync` + `coordination-coherence.yml` gate | **built** (ADR-0019 slice 2) |
| `build-config` | the org-shared .NET build config | `scripts/sync-build-config.sh` | reserved; migrate in a follow-up |
| `lockfile-sync` | the reusable lockfile-sync workflow | `.github/workflows/lockfile-sync.yml` | reserved; migrate in a follow-up |
| `contract-coherence` | the reusable contract-coherence gate | `.github/workflows/contract-coherence.yml` | reserved; migrate in a follow-up |

## The coordination kit

The content-addressed bundle every `coordination-kit` receiver must hold (`sha256` is the digest of
`source` — for a skill dir, its `SKILL.md`; for a file, the file). Regenerate a digest with
`scripts/repos.sh digest <source>`.

| Kit id | Kind | Source |
|---|---|---|
| `cross-repo-coordination` | skill | `.claude/skills/cross-repo-coordination` |
| `intra-repo-parallel-work` | skill | `.claude/skills/intra-repo-parallel-work` |
| `fsgg-coord` | client | `scripts/fsgg-coord` |

**Distribution & coherence (slice 2).** [`scripts/coordination-sync`](../../scripts/coordination-sync)
writes the kit into a receiver (`coordination-sync <target>`) and drift-checks it
(`--check`, exit 1 on drift). Each `coordination-kit` receiver's CI calls the reusable
[`coordination-coherence.yml`](../../.github/workflows/coordination-coherence.yml) (`workflow_call`),
which checks out `.github` (the authority) and the caller and runs `coordination-sync --check --repo
<caller>` — so a receiver fails CI if its kit copy drifts from canonical, and a non-receiver passes
trivially. `.github` remains the source (never a receiver), enforced by the roster validator.
