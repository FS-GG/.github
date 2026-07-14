# ADR-0031 (WITHDRAWN): A silently re-published package is a NAMED failure — lock-file restores are cold, and the catalog `packageHash` names the cause

- **Status:** Withdrawn — premise false; surviving decisions (§1 cold restore, §3 never hand-write a `contentHash`) folded into [ADR-0032](0032-the-lock-hash-must-not-depend-on-the-machine.md) §5 and §4.
- **Date:** 2026-07-11
- **Withdrawn:** 2026-07-14
- **Affects:** `.github`, FS.GG.SDD, FS.GG.Rendering, FS.GG.Game, FS.GG.Audio, FS.GG.Templates, FS.GG.Governance

> # ⛔ WITHDRAWN — NOTHING BELOW THIS LINE IS LIVE. DO NOT CITE THIS ADR.
>
> **Everything after this banner is a historical record of a decision the org got wrong.** Not one
> section of it — not the Context, not any of the four Decisions, not any of the Consequences — is in
> force. **Go to [ADR-0032](0032-the-lock-hash-must-not-depend-on-the-machine.md).** It carries the two
> decisions that survived, and it decides the question this record failed to ask.
>
> **Why: the premise is FALSE. `FSharp.Core 10.1.301` was never re-published.** There have always been
> **two different `.nupkg` files** with that id and version: the copy the .NET SDK bundles
> (`…/sdk/10.0.301/FSharp/library-packs/`, 3,051,664 B, sha256 `cdf9fbc3…`) and the copy nuget.org
> serves (3,066,660 B, sha256 `9896603d…`). **Which `contentHash` you get is a function of WHICH SOURCE
> served the package, not of WHEN you restored** — CI resolves the SDK's copy (`FwQFuqOA…`), a dev box
> whose NuGet config excludes `library-packs` resolves nuget.org's (`excLf2zM…`). Nothing was
> overwritten and no feed lied. See [#471](https://github.com/FS-GG/.github/issues/471).
>
> **Where each section went:**
>
> | section | disposition |
> |---|---|
> | §Context — "`FSharp.Core` was re-published" | **False.** Corrected by ADR-0032 §Context. |
> | §Decision 1 — every restore that writes or enforces a lock file must be **COLD** | **SURVIVES → [ADR-0032 §Decision 5](0032-the-lock-hash-must-not-depend-on-the-machine.md#decision).** Independently correct: a warm package folder makes both the generator and the enforcer compare a record to a record — a genuine fail-open, demonstrated against a real restore by [#460](https://github.com/FS-GG/.github/issues/460)'s fixture. Cite **0032 §5**. |
> | §Decision 2 — NAME a re-publication via the catalog `packageHash` | **VOID.** It would detect **nothing** — no re-publication ever occurred — while the actual divergence sails straight through it. |
> | §Decision 3 — a `contentHash` is never hand-written | **SURVIVES → [ADR-0032 §Decision 4](0032-the-lock-hash-must-not-depend-on-the-machine.md#decision).** |
> | §Decision 4 — we DETECT re-publication, we do not PREVENT it | **VOID.** An answer to a defect that never happened; not re-decided anywhere. |
> | §Consequences | **STALE — see below.** |
>
> **The Consequences below are out of date and must not be read as current.** Two in particular:
> the claim that the failure leg *"still has no test, so this decision has no regression guard"* is no
> longer true — it is guarded by `tests/lockfile-cold/run.sh` (7 legs) and
> `.github/workflows/lockfile-cold-selftest.yml` ([#460](https://github.com/FS-GG/.github/issues/460)) —
> and the adoption list naming FS.GG.Game / FS.GG.Rendering / FS.GG.Audio as non-compliant is spent: all
> five F# repos have synced the shared build config and re-pinned (#504, recorded in
> `docs/architecture.md`).
>
> Also note **"cold" does NOT mean "hermetic"**, which this record implied: `library-packs` is an
> MSBuild-injected local folder source that a fresh `NUGET_PACKAGES` and a cleared HTTP cache do not
> bypass. That hole is closed by ADR-0032 §Decision 2.
>
> **The text below is left unedited.** An ADR is a record of what was decided and why; rewriting its
> premise would erase the evidence of how the org got it wrong, and ADR-0032 exists to preserve exactly
> that. Read it as history, and act on nothing in it.

## Context

`FSharp.Core 10.1.301` was **re-published**: same version, different bytes. Every FS-GG repo pins it
through a committed `packages.lock.json`, and the org did not notice — the deterministic gate stayed
green while `main` could not be restored from a fresh clone ([#429](https://github.com/FS-GG/.github/issues/429),
epic [#266](https://github.com/FS-GG/.github/issues/266)).

### Three hashes, and only one of them is the lock file's

The confusion this defect caused is worth ratifying away. A NuGet package carries three distinct
values, and treating any two as interchangeable re-breaks the build:

| value | where it lives | what it is |
|---|---|---|
| `contentHash` | `packages.lock.json` | copied **verbatim** from the installed package's `.nupkg.metadata`, which NuGet writes **once, at install time** |
| `packageHash` | nuget.org catalog entry | SHA512 of the `.nupkg` bytes the feed currently serves — **equal** to the local `.nupkg.sha512` sidecar |
| the `.nupkg` bytes | the package itself | never hashed by `--force-evaluate` |

Established by falsification (`dotnet 10.0.301`, the SDK the runner installs): planting a stale
`contentHash` in a package's `.nupkg.metadata` makes `--force-evaluate` write **that** value into
`packages.lock.json`. Corrupting the `.nupkg` **bytes** changes nothing. Poisoning the
`.nupkg.sha512` **sidecar** changes nothing. For `FSharp.Core 10.1.301` the values are
`FwQFuqOA…` (the stale metadata every repo committed), `excLf2zM…` (what the feed now serves), and
`UYKV0m7i…` (the catalog `packageHash` / sidecar, SHA512, 3066660 bytes).

### Why a warm restore cannot decide anything

`--force-evaluate` copies `contentHash` out of the installed package folder, and `--locked-mode`
validates the committed `contentHash` against that **same** folder. If the folder is warm — pre-seeded
in the runner image, restored from an actions cache, or left by an earlier restore — then **both the
generator and the enforcer compare a record against a record and never contact the feed.**

The gate therefore failed in **both** directions, which is what makes this a #266 item rather than a
papercut:

- **green on `main` with a lock file no fresh clone could restore** — fails open, and
- **red on a correct, freshly-generated lock file** — fails closed, which blocked the repair.

A gate whose verdict is a function of runner cache state is not a determinism gate.

### What has already landed

- **Enforcement** — FS.GG.SDD's `locked-restore` action restores into a fresh `NUGET_PACKAGES` with
  the HTTP cache cleared, and its gate job sets `cache: false`.
- **Generation** — `.github`'s `lockfile-sync.yml`, the reusable workflow that *writes* every repo's
  lock file, does the same ([#453](https://github.com/FS-GG/.github/pull/453)).

Together these make a re-publication **real** instead of a lottery. Neither decides what should
*happen* when one occurs: the symptom is `NU1403 (package content hash validation failed)`, which is
indistinguishable from a corrupt local cache and invites exactly the wrong repair — pasting the
package's published hash into the lock file, which is the `packageHash`, and which fails the gate too.

## Decision

**1. Any restore that WRITES or ENFORCES a lock file must be COLD.**

Cold means: restore into a fresh, empty `NUGET_PACKAGES`, with `dotnet nuget locals http-cache --clear`
first. Both halves are load-bearing — `NUGET_PACKAGES` relocates the global-packages folder but **not**
the HTTP cache, which will replay stale `.nupkg` bytes. A gate job must not carry a NuGet cache
(`cache: false`); a cache that can change the verdict is not "speed only".

This applies to `--locked-mode` (the gate) and `--force-evaluate` (the sync) alike. A cold enforcer
over a lock file written by a warm generator only fails everyone honestly instead of silently.

**2. A re-published package is NAMED, not merely failed.**

When a cold locked restore fails `NU1403`, the failing check resolves the package's catalog entry
(registration → `catalogEntry`) and compares the feed's `packageHash` / `packageSize` / `published`
timestamp against the pinned package. It then reports which of two facts is true:

- *"`<id> <version>` was (re)published at `<timestamp>`; your lock file predates it — regenerate it
  cold and review the diff"*, or
- *"the local package copy is stale/corrupt; clear it"*.

The gate's message must name the cause and prescribe the remediation. `NU1403` alone does not.

**3. A `contentHash` is never hand-written.**

Only a cold `--force-evaluate` may produce one. Pasting a hash — in particular the catalog
`packageHash`, which is the obvious and wrong move — re-breaks the build.

**4. We DETECT re-publication; we do not PREVENT it.**

Mirroring third-party packages into an immutable org-owned feed would make a version's bytes
unchangeable underneath FS-GG. It is rejected **for now** on cost: a mirror to run and populate, and a
`nuget.config` migration in every repo. Revisit if re-publication recurs, or if a package we cannot
audit changes bytes. This ADR buys a loud, named failure — not immutability, and the distinction is
the honest limit of the decision.

## Consequences

**Every repo must adopt the cold restore, and the sequencing is not optional.** Now that the generator
is cold, the next `lockfile-sync` run commits the **correct** hash — at which point every gate still
validating against a warm package folder fails `NU1403`. Those repos' lock files are *already*
unrestorable from a cold clone; the fix only stops the gate hiding it. Filed and linked as children of
#429:

- **FS.GG.Game** — `--locked-mode` on 6 restores with `cache: true` on 4 jobs; most exposed
  ([Game#135](https://github.com/FS-GG/FS.GG.Game/issues/135))
- **FS.GG.Rendering** ([#482](https://github.com/FS-GG/FS.GG.Rendering/issues/482)) and
  **FS.GG.Audio** ([#39](https://github.com/FS-GG/FS.GG.Audio/issues/39)) — `--locked-mode` with no
  cold-restore treatment
- **FS.GG.SDD** — already compliant; its action is the reference implementation to copy

**The failure leg still has no test, so this decision has no regression guard.** Per #266, a lock file
that a cold restore cannot satisfy must FAIL the gate — and nothing today asserts it. Until that test
exists, nothing prevents a warm restore being reintroduced and this whole class returning silently.
This is #429's third ask and it remains open; it is the highest-value follow-up to this ADR.

**Cost.** A cold restore re-downloads the dependency graph on every gate run and every sync, trading
runner minutes for a verdict that means something. That trade is the entire point: the cache was
buying speed by deciding the answer.

**Obligation on new repos.** Any repo joining the roster with a committed `packages.lock.json` adopts
the cold restore in its gate before its first green run counts for anything.
