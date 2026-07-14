# ADR-0036 — The shared-build-config drift check compares against a pin, not against `main`

- **Status:** Accepted
- **Date:** 2026-07-14
- **Amends:** [ADR-0006](0006-org-shared-dotnet-build-config-and-unified-restore-locked-mode-gate.md) (the org-shared .NET build config)
- **Issue:** [.github#592](https://github.com/FS-GG/.github/issues/592) · filed from FS.GG.SDD while working FS.GG.SDD#379
- **Related:** [#499](https://github.com/FS-GG/.github/issues/499), [#536](https://github.com/FS-GG/.github/issues/536), [#561](https://github.com/FS-GG/.github/issues/561), [#626](https://github.com/FS-GG/.github/issues/626) (the push arm), [ADR-0032](0032-the-lock-hash-must-not-depend-on-the-machine.md)

## Context

Every `receives: build-config` repo runs a **`Shared-build-config drift check`** in its own `gate.yml`.
That job checks `FS-GG/.github` out at **`ref: main`** and runs `scripts/sync-build-config.sh --check`,
which required its managed files (`Directory.Build.props`, `Directory.Packages.props`,
`.config/dotnet-tools.json`) to be **byte-identical to `dist/dotnet/` as of that checkout**. In
FS.GG.SDD the check is **required**, so it hard-blocks the merge button.

The verdict of that check was therefore **a function of another repo's moving branch, evaluated at
whatever moment CI happened to run**. Two consequences follow, and the second is the serious one:

1. A receiver **could not make the check green from its own PR**. Nothing in the PR's tree determined
   the answer.
2. The instant *anything* landed in `dist/dotnet/` here, **every open PR in every adopting repo went
   red on a required check** — through no fault of its own, with no change to its branch.

That is not a drift check. It is a race, and it fired twice:

- **[#499](https://github.com/FS-GG/.github/issues/499)** moved `Directory.Build.props` (ADR-0032).
  FS.GG.SDD could merge **nothing** for hours; a finished, green PR sat blocked (FS.GG.SDD#379).
- **[#536](https://github.com/FS-GG/.github/issues/536)** then edited **the same file's XML comment** —
  correcting prose, no MSBuild element changed. That was enough to turn FS.GG.SDD#380 **red
  mid-flight**: the PR had already made the drift check green, and it went red between two pushes
  because the target moved underneath it. A **comment** was enough.

`gate.yml` named its own escape condition — *"Track main rather than a pinned SHA… **Revisit only if
upstream churn causes flakiness**"* — and upstream churn had, twice.

### The push arm was necessary and not sufficient

[#626](https://github.com/FS-GG/.github/issues/626) built `build-config-propagate.yml`: a rolling,
auto-merging `build-config/sync` PR opened in every receiver whenever `dist/dotnet/` changes. That
ended a *different* defect — the fabric was a **ratchet**, enforcing an update nothing ever pushed —
and it shortened the freeze. **It cannot remove it**, for two reasons:

- The window remains: between the edit landing here and the sync PR landing there, every open PR in the
  receiver is red on a required check.
- The freeze stays **reachable indefinitely**. If the sync PR's *own* `gate` job goes red — a config
  change that moves the restore graph needs a lockfile regenerated in the same PR — auto-merge never
  fires, and the receiver is frozen until a human intervenes. `build-config-propagate.yml`'s
  fail-closed classifier exists to shout about precisely that state.

## Decision

**The receiver records which `.github` commit its managed files were synced from, and `--check`
compares against *that commit's* `dist/dotnet/` — not against `main`.**

- The pin is `.config/fsgg-build-config.sha` in the receiver: a self-describing file whose one bare
  40-hex line is the `.github` commit. `scripts/sync-build-config.sh` writes it on a clean sync;
  `build-config-propagate.yml` bumps it **in the same PR as the files it distributes**.
- `--check` resolves `dist/dotnet/` at that commit out of the `.github` checkout it is already running
  from (fetching the commit if the checkout is shallow, which in receiver CI it always is) and diffs
  against it.

The verdict becomes a **pure function of the receiver's own tree** — its files and its pin, which
arrive together on the PR's own merge ref. Nothing `.github` does to `main` can move it.

### What the two outcomes now mean

| | |
|---|---|
| files **==** `dist/dotnet@pin` | **GREEN.** The copy is faithful. It may be **behind** `main`, and that is fine — being behind is not a defect in the PR, and the propagate bot already has a rolling, auto-merging sync PR open to close the gap. A loud `NOTICE` names the gap. |
| files **!=** `dist/dotnet@pin` | **RED.** A managed file was hand-edited. That **is** a defect in the PR, and the author can fix it from the branch they are on — which is the only thing a required check can honestly demand. |

**This is a deliberate inversion**: from *merge-freeze-by-default* to *stale-until-someone-merges*. For
a **required** check, that is the safer default. Staleness becomes **visible** (the pin is a committed
file you can read) and **bot-remediated**; it is no longer "enforced" by freezing everyone's merges.

### Three properties that make this safe, and are not incidental

- **No receiver changes anything.** Both the source files *and the checking script* are pulled from
  `.github@main` by the receiver's `gate.yml`, so `.github` owns `--check`'s semantics unilaterally.
  No `gate.yml` edit, no flag day, no coordinated rollout, no per-repo adoption item.
- **An absent pin is legal, and means legacy mode** — compare against `main`, exactly as before. Every
  receiver is in that state today and behaves *identically* until the propagate bot's next sync PR
  pins it. **The rollout therefore cannot freeze anyone**, which matters more than usual here: a
  migration that merge-froze the org would be this very defect, committed by its own fix. The pin is
  emphatically **not** a member of the script's `FILES` list — `--check` treats a missing member of
  `FILES` as drift, so listing it would red-light every unpinned receiver on day one.
- **An unresolvable pin is UNEVALUATABLE, and passes.** If the pinned commit cannot be read (unknown
  commit; a managed file that did not exist at it; a `git fetch` that fails three times), the check
  **refuses to judge**: it warns loudly, reports `ADVISORY`, and exits 0.

  This is not squeamishness, it is the only honest verdict. Without that baseline, *behind* and
  *hand-edited* are **indistinguishable** — and they have opposite verdicts. The tempting alternative,
  falling back to comparing against `main`, is **wrong in the precise way this ADR is about**: a
  merely-behind receiver does not match `main`, so the fallback **red-lights it**. One transient `git
  fetch` blip in CI would then re-create the #499/#536 freeze on an innocent PR, on a required check.
  A gate that reddens people for a network hiccup is this defect wearing a new hat. (The fetch retries
  three times, so reaching this state at all should be rare.)

  **The residual, stated plainly:** a receiver that *both* corrupts its pin *and* hand-edits a managed
  file goes green. Accepted. It takes a deliberate, conspicuous edit to a file whose entire content is
  a bot-written SHA — visible in any review — and the propagate bot rewrites both halves on its next
  run, after which the check bites again. The alternative is freezing four repos because GitHub's git
  endpoint blinked.

## Alternatives considered

- **Keep tracking `main`, make the check advisory.** Removes the freeze, but gives up catching a
  genuine hand-edit at the PR boundary — and `required` is *branch protection* in FS.GG.SDD, which
  `.github` cannot unset from here. Rejected: it discards the check's one honest job.
- **Compare semantically** — normalize comments and whitespace out of the byte-identity test. Would
  have stopped #536, but not #499 (a real property change). #592 calls this out itself: a mitigation,
  not a fix. Rejected.
- **Classify against `.github`'s history** — no pin file; ask instead whether the receiver's copy
  matches *any past* canonical version (behind → green; matches nothing → hand-edited → red). Zero-touch
  and tempting. Rejected: the oracle is `.github`'s commit history, so a squash/force-push that rewrote
  it would red-light **every receiver at once**, and the check would need a full history deepen in CI.
  A pin is a *positive, auditable assertion* — you can read a repo's baseline without running anything.

## Consequences

- `shared-build-config` **1.0.0 → 1.1.0**. Additive and backward-compatible: an unpinned receiver is
  bit-for-bit unchanged in behaviour. Per [ADR-0015](0015-register-the-registry-schema-as-a-governed-contract.md),
  additive growth is still growth, so the minor bumps.
- A new file appears in each receiver (`.config/fsgg-build-config.sha`), written by the bot, in the
  same PR as the files it describes.
- **Staleness is now possible and green.** That is the trade, taken with eyes open. Currency is
  enforced by `build-config-propagate.yml`, whose failure now means *"a repo is drifting out of date"*
  — a real problem, but no longer *"a repo cannot merge"*.
- **No staleness bound.** A "fail if more than N commits / N days behind" rule was considered and
  rejected: it would reintroduce a failure that depends on time and on `.github`'s commit rate rather
  than on the receiver's tree — the exact class of defect being removed here.
- The receivers' `gate.yml` comments still say *"Track main rather than a pinned SHA"*. They are now
  **stale prose in four repos** and describe a policy that no longer holds. They are inert — the ref is
  still `main`, which is correct and required, since the script and the pin's object store both come
  from that checkout — but they will mislead. Filed as a follow-up rather than fixed here: `.github`
  cannot open PRs in the receivers from this worktree.
