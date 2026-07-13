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
| `FS-GG/FS.GG.SDD` | framework | `labels`, `coordination-kit`, `lockfile-sync` |
| `FS-GG/FS.GG.Rendering` | framework | `labels`, `coordination-kit`, `lockfile-sync` |
| `FS-GG/FS.GG.Governance` | framework | `labels`, `coordination-kit`, `lockfile-sync` |
| `FS-GG/FS.GG.Templates` | framework | `labels`, `coordination-kit`, `lockfile-sync` |
| `FS-GG/FS.GG.Game` | framework | `labels`, `coordination-kit`, `lockfile-sync` |
| `FS-GG/FS.GG.Audio` | framework | `labels`, `coordination-kit`, `lockfile-sync` |

**Authority.** `.github` holds the canonical fabrics and the coordination kit and mirrors them out
(the analog of `fsgg-sdd` for product skills). It is the SOURCE of the coordination kit, so it never
*receives* `coordination-kit` — an invariant the validator enforces.

**Participation audit.** The fabrics are opt-in (a receiver participates by *wiring* something in its
own CI), so `receives` only *declares* intent. [`scripts/repos-audit.sh`](../../scripts/repos-audit.sh)
— run weekly by [`repos-audit.yml`](../../.github/workflows/repos-audit.yml) — closes the loop in
**both directions**, for every capability in the `capabilities:` block:

| what it finds | verdict |
|---|---|
| declared *and* wired | ok |
| declared, **not wired** | a **gap** — the repo promised to participate and did not (exit 1) |
| wired, **not declared** | **drift** — an adopted-but-unrostered capability (exit 1) |

The reverse direction is not symmetry for its own sake. The forward check starts from the
declaration, so it is blind by construction to a repo that adopted a fabric without saying so — and
the roster is what *every* org fabric iterates, so such a repo is invisible to all of them.

**Every capability declares a DETECTOR** — the answer to *"how would I know, by looking at the
receiver, that it really participates?"* (#628). Exactly one of:

| detector | how a receiver wires it | how the audit sees it |
|---|---|---|
| `workflow: <f>.yml` | calls the authority's reusable workflow | a `uses:` of `FS-GG/.github/.github/workflows/<f>.yml` |
| `script: <f>.sh` | **inlines a job** that checks `.github` out and runs the script | a reference to `<f>.sh` (matched on the **basename**) |
| `push: true` | **nothing** — the *authority* writes it into the receiver | nothing to see; not swept. Requires a `reason:` |

The `script:` kind exists because `build-config` is delivered as a script, not a reusable workflow —
there is nothing to `uses:`, so the workflow detector is **structurally blind** to it. The basename is
what is stable across receivers: Governance runs `_org-build/scripts/sync-build-config.sh` where SDD,
Rendering and Game run `.github/scripts/…`, so anchoring on any one prefix would report the others as
false gaps.

`push:` is the **one honest way to be unauditable at the receiver**. `labels` is pushed:
[`apply-labels.sh`](../../scripts/apply-labels.sh) reads this roster and creates the labels via the
API, so the `receives: labels` row is the **input to the push**, not a falsifiable claim about the
receiver's config — and no receiver-side artifact could ever verify it. It is only honest because it
must be **written down, with a reason** the validator refuses to leave blank.

**Nothing may be `receives`d that has no detector row.** This closure is what makes the guarantee at
the top of the registry actually *true*. It was not: `build-config` and `labels` were legal `receives:`
words with **no row at all**, so they were swept in **neither** direction — findable neither as unwired
nor as an unrostered adopter — while the header promised the list "can no longer rot without a red
check". **Four of six repos enforced `build-config` in CI** (SDD's as a *required* status check) **while
`receives:` said zero**, and the audit reported green over all of them for months.
[#626](https://github.com/FS-GG/.github/issues/626) then read those empty rows as *"propagates to
nobody"*, shipped on the conclusion, and four repos went red within twenty minutes. An unaudited
registry row is not a neutral gap — **it is a false negative that reads like a licence.** Both
`repos.sh validate` (exit 1) and `repos-audit.sh` (exit 3, a permanent no-verdict) now refuse one.

**Every capability is audited on its own** (#503). The non-vacuity guard used to *sum* the examined
pairs across capabilities, so one populated leg satisfied it for all of them: `coordination-kit` had
six receivers, `lockfile-sync` and `contract-coherence` had none rostered, and the audit reported
"every declared receiver is wired" having checked **one third of its own mandate** — while six repos
had really adopted `lockfile-sync` and the roster never caught up.
[`FS.GG.Game#137`](https://github.com/FS-GG/FS.GG.Game/issues/137) is the proof of what that cost: its
`lockfile-sync` caller `startup_failed` **119 consecutive times** and no gate said a word, because as
far as the roster was concerned nobody received `lockfile-sync`. A capability with no rostered
receiver now fails **on its own name**, and it keys on the roster (a deterministic file read) rather
than on how many pairs the run managed to examine — so an API outage reports as a *retryable*
no-verdict, never as "this capability has no receivers".

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
| `coordination-kit` | the four coordination skills + the `fsgg-coord` client | `scripts/coordination-sync` + `coordination-coherence.yml` gate | **built** (ADR-0019 slice 2) · **audited**, 6 receivers |
| `lockfile-sync` | the reusable lockfile-sync workflow | `.github/workflows/lockfile-sync.yml` | **adopted**, 6 receivers · **audited** (rostered by #503) |
| `contract-coherence` | the reusable contract-coherence gate | `.github/workflows/contract-coherence.yml` | **audited**, `receivers: none` — built to be receiver-wired, never adopted ([#519](https://github.com/FS-GG/.github/issues/519)) |
| `build-config` | the org-shared .NET build config | `scripts/sync-build-config.sh` | reserved; not audited (no reusable workflow) |

### The `capabilities:` block — what gets audited, and by which workflow

A capability is audited only if it has a row in `capabilities:` naming the reusable workflow that
wires it. That mapping used to be hardcoded in `repos-audit.sh` (a `wf_for_cap` case statement plus
an `AUDITED_CAPS` string) — two hand-maintained copies of a fact the registry already owned, so a
capability the roster gained was audited only if somebody *also* remembered to edit the script, and
forgetting was silent. `repos.sh validate` now proves each row instead: the workflow must exist and
must really carry a `workflow_call:` trigger (a workflow nothing can `uses:` would report every
declared receiver unwired, forever).

**`receivers: none` is a recorded claim, not a mute button.** A capability that genuinely has no
receiver says so out loud, with a required `reason` — a reviewed claim, exactly like
`outside-fabric:`. What keeps it honest is that it is **falsifiable**: the audit still sweeps every
rostered repo for a real caller, so a capability claiming no receivers while somebody actually wires
it goes **red** rather than quietly muting the leg. "Provably has none" is then a decision somebody
recorded, and a decision the gate re-checks on every run — not a row nobody filled in.

## The coordination kit

The content-addressed bundle every `coordination-kit` receiver must hold. `registry/repos.yml` names
each kit item (`id`, `kind`, `source`); the **digests live in [`registry/repos.lock`](../../registry/repos.lock)**,
which is **generated** — regenerate it with `scripts/repos.sh relock` (the digest of a `source` is its
`SKILL.md` for a skill dir, or the file itself for a file).

`repos.lock` is a **generated, CI-gated artifact** ([#309](https://github.com/FS-GG/.github/issues/309)):
nobody authors it, `repos-registry-selftest` fails on any drift in it, and a collision in it is a
rebase rather than a decision. **Do not reserve it in a `Paths:` touch-set** — regenerate it and name
it as expected drift in the PR. The digests used to be a `sha256:` field on each `kit:` row, which
forced every kit edit to reserve the whole authored roster and serialised them all against each other
([#527](https://github.com/FS-GG/.github/issues/527)).

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
