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
| `FS-GG/FS.GG.Audio` | framework | `labels`, `coordination-kit` |

**Authority.** `.github` holds the canonical fabrics and the coordination kit and mirrors them out
(the analog of `fsgg-sdd` for product skills). It is the SOURCE of the coordination kit, so it never
*receives* `coordination-kit` — an invariant the validator enforces.

**Participation audit.** The fabrics are opt-in (a receiver participates by calling a reusable
`.github` workflow), so `receives` only *declares* intent. [`scripts/repos-audit.sh`](../../scripts/repos-audit.sh)
— run weekly by [`repos-audit.yml`](../../.github/workflows/repos-audit.yml) — closes the loop:
for each capability that maps to a reusable workflow (today `coordination-kit` →
`coordination-coherence.yml`), it verifies every declared receiver actually calls it. A
declared-but-unwired repo fails the audit, so `receives` has teeth.

**Closed-world gate.** The audit above iterates repos that are *in* this roster, so a repo missing
from it is missing from the audit too. The roster is a closed-world assumption, and
[`scripts/check-roster-closure.py`](../../scripts/check-roster-closure.py) (#269) is what asserts the
world is actually closed — from both sides. **(A)** every `repos:` participant in
[`dependencies.yml`](../../registry/dependencies.yml) has a row here; **(B)** every repo that really
exists in the GitHub org is either rostered or carries an explicit `outside-fabric:` row. It runs on
every PR and every push to `main` (`coherence.yml`) and **fails closed**: an errored, empty, or
too-narrow org listing is an error rather than a skip, because "nothing to check" and "checked, and
it's fine" must not share an exit code. `FS.GG.Audio` — registered as a contract owner, live on the
feed, rostered nowhere for weeks — is the defect this closes.

**`outside-fabric:` — the reviewed opt-out.** A repo genuinely outside every fabric says so in one
row (`{ full, reason }`). Without it, "deliberately outside" and "accidentally outside" look the same
to every gate. It is not a mute button: `reason` is required, a repo may not be both rostered and
exempt, an exemption naming a repo that no longer exists in the org fails as *stale*, and archived or
forked repos are **not** auto-exempt — archiving must never be a way out of the gate. Empty today.

## Capabilities (`receives` vocabulary)

| Capability | What the repo participates in | Consumer | Status |
|---|---|---|---|
| `labels` | the shared cross-repo labels | `scripts/apply-labels.sh` | **migrated** (ADR-0019 slice 1) |
| `coordination-kit` | the four coordination skills + the `fsgg-coord` client | `scripts/coordination-sync` + `coordination-coherence.yml` gate | **built** (ADR-0019 slice 2) |
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
| `check-board` | skill | `.claude/skills/check-board` |
| `pnext-item` | skill | `.claude/skills/pnext-item` |
| `fsgg-coord` | client | `scripts/fsgg-coord` |

The first two skills define the **protocol**; `check-board` and `pnext-item` are **command skills**
that drive it — respectively, reconciling the board against issue state, and taking a repo's next
schedulable item from claim to done-stamp. Each skill materializes into every root in
`AGENT_SKILL_ROOTS` (`.claude/skills`, `.agents/skills`), byte-identical. They are deliberately
**not** `registry/skills.yml` rows: that catalog governs skills a producer *emits into a scaffold*,
gated by `materializes-when`; these are kit skills for the framework repos themselves.

**Distribution & coherence (slice 2).** [`scripts/coordination-sync`](../../scripts/coordination-sync)
writes the kit into a receiver (`coordination-sync <target>`) and drift-checks it
(`--check`, exit 1 on drift). Each `coordination-kit` receiver's CI calls the reusable
[`coordination-coherence.yml`](../../.github/workflows/coordination-coherence.yml) (`workflow_call`),
which checks out `.github` (the authority) and the caller and runs `coordination-sync --check --repo
<caller>` — so a receiver fails CI if its kit copy drifts from canonical, and a non-receiver passes
trivially. That trivial pass is *only* safe because the closed-world gate above independently proves
no repo is a non-receiver by accident. `.github` remains the source (never a receiver), enforced by
the roster validator.

**The gate ATTRIBUTES drift; it does not merely report it** (#450). Canonical is `.github@main`, which
moves constantly, and a receiver's `main` trails it in the window before `coordination-propagate`'s sync
PR lands. A check that only asks *"does this tree equal canonical?"* therefore reds branches that never
went near the kit — which is how a worker came to file a long, evidenced issue about a resync that had
merged 110 seconds earlier ([FS.GG.Rendering#473](https://github.com/FS-GG/FS.GG.Rendering/issues/473)),
and a second lost an hour to the same signal. So the two events a receiver wires differ:

| run | invocation | verdict |
|---|---|---|
| `push` to `main` | `--check` (strict) | the **verdict of record**: any drift is a hard red (exit 1). |
| `pull_request` | `--check --base-ref origin/<base>` | only drift the **branch authored** (merge-base-relative, per file) is a red. Drift it *inherited* — from a base that is behind canonical, or from a branch cut before a sync landed — is an **advisory** (exit 0) naming whose job the fix is. |

A branch may still change the kit *correctly* — the `coordination-kit/sync` PR does exactly that — and
passes when the result matches canonical; a gate that red the one PR which fixes the drift would deadlock
the fabric. Receivers need no change for this: `workflow_call` inherits the caller's event, so the
reusable gate derives the base ref itself.
