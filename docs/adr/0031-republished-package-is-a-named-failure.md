# ADR-0031: A silently re-published package is a NAMED failure — lock-file restores are cold, and the catalog `packageHash` names the cause

- **Status:** Accepted
- **Date:** 2026-07-11
- **Affects:** `.github`, FS.GG.SDD, FS.GG.Rendering, FS.GG.Game, FS.GG.Audio, FS.GG.Templates, FS.GG.Governance

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
