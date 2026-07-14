# ADR-0032: `FSharp.Core` was never re-published — the lock file's `contentHash` must not depend on the machine

- **Status:** Accepted
- **Date:** 2026-07-11
- **Affects:** `.github`, FS.GG.SDD, FS.GG.Rendering, FS.GG.Game, FS.GG.Audio, FS.GG.Templates, FS.GG.Governance
- **Supersedes:** [ADR-0031](0031-republished-package-is-a-named-failure.md), now **WITHDRAWN in full** —
  its premise is false, and **nothing in it is live**. Its two surviving decisions are folded in here:
  §Decision 1 (**cold restores**) → **[§Decision 5](#decision)**, and §Decision 3 (**never hand-write a
  `contentHash`**) → **[§Decision 4](#decision)**. Cite this ADR, not 0031.

## Context

ADR-0031 was ratified this morning on a premise that is **false**:

> `FSharp.Core 10.1.301` was **re-published**: same version, different bytes.

It was not. It was never re-published. **There have always been two different `.nupkg` files with that
id and version, and which one you get depends on which source served it** ([#471](https://github.com/FS-GG/.github/issues/471),
found from FS.GG.Audio; measured independently here on `dotnet 10.0.301` before this ADR was written):

| copy | bytes | sha256 |
|---|---:|---|
| the **.NET SDK's** bundled copy — `/usr/share/dotnet/sdk/10.0.301/FSharp/library-packs/FSharp.Core.10.1.301.nupkg` | 3,051,664 | `cdf9fbc3…` |
| **nuget.org**'s copy — `v3-flatcontainer/fsharp.core/10.1.301/…` | 3,066,660 | `9896603d…` |

Same id. Same version. ~15 KB apart. **Nothing was overwritten, and no feed lied.** The F# SDK injects
its bundled copy as a restore source (`Microsoft.FSharp.Core.NetSdk.props` → `_FSharpCoreLibraryPacksFolder`)
and pins `FSCorePackageVersion = 10.1.301` — precisely the version every FS-GG repo references.

So the three hashes the org spent a day chasing are not *stale* versus *fresh*. They are **two packages
and a sidecar**:

| hash | what it actually is |
|---|---|
| `FwQFuqOA…` | the **SDK `library-packs`** copy's `contentHash` — what **CI** resolves, and what every repo has committed |
| `excLf2zM…` | the **nuget.org** copy's `contentHash` — what a dev box resolves when `library-packs` is excluded |
| `UYKV0m7i…` | the `.nupkg.sha512` sidecar / catalog `packageHash` — never consulted for lock validation *(ADR-0031 got this right)* |

**`contentHash` is a function of WHICH SOURCE served the package — not of WHEN you restored.**

### The evidence that settles it

1. **The two files exist and differ.** Byte-compared here, on the SDK the runner installs. This alone
   refutes re-publication: the "old" bytes are still sitting on disk, shipped with the SDK.
2. **FS.GG.SDD's gate is green, cold, while committing `FwQFuqOA`.** Under the re-publication theory a
   cold restore would fetch the "fresh" hash and go red. Under this one it is exactly what you expect:
   cold or warm, CI resolves `library-packs`.
3. **A dev box resolves nuget.org and gets `excLf2zM`** — because this container's
   `~/.nuget/NuGet/NuGet.Config` carries a `packageSourceMapping` that does not admit `library-packs`.
   NuGet says so itself: `NU1100: … PackageSourceMapping is enabled, the following source(s) were not
   considered: …/library-packs`. **The mapping is why dev diverges from CI** — and it was added for an
   unrelated, good reason (binding `FS.GG.*` to nuget.org so a local feed can never serve one).

### What ADR-0031 got right, and must not be un-done

**Cold restores (ADR-0031 §Decision 1) stand, on their own merits — and are now carried by this ADR as
[§Decision 5](#decision), 0031 having been withdrawn.** A warm package folder genuinely
is a fail-open: `--force-evaluate` copies `contentHash` out of the installed folder and `--locked-mode`
validates against that *same* folder, so both compare a record to a record and never contact a feed.
[#460](https://github.com/FS-GG/.github/issues/460)'s fixture demonstrates it against a real restore —
poison a package's `.nupkg.metadata` and a broken lock file goes green, warm, every time.

But **cold does not mean hermetic, and ADR-0031 implied it did.** `library-packs` is a *local folder
source injected by MSBuild*: `NUGET_PACKAGES=$(mktemp -d)` does not bypass it, `dotnet nuget locals
http-cache --clear` does not touch it, and `<clear/>` in `nuget.config` does not remove it (it arrives
via `RestoreAdditionalProjectSources`, not as a configured source). A perfectly cold restore still
resolves `FSharp.Core` from the SDK. **#453 closed a real hole; it did not close this one.**

### What ADR-0031 got wrong, and what it would have cost

ADR-0031 §Decision 2 asks for the durable answer to **silent re-publication** (verify against the
catalog `packageHash`). That check would detect **nothing** — no re-publication ever occurred — while
the actual divergence, *the same package resolved from two sources*, sails straight through it. We
would have shipped a gate against a phantom, declared the class closed, and left the real one open.
That is epic [#266](https://github.com/FS-GG/.github/issues/266) happening to the fix for #266.

## Decision

**1. The lock file's `contentHash` MUST NOT depend on the machine.** A regenerated
`packages.lock.json` must be byte-identical whether it was produced in CI, in the dev container, or on
a maintainer's laptop. A lock file whose value depends on *who ran the restore* is not a lock file; it
is a coin toss with a `git diff`.

**2. `FSharp.Core` resolves from nuget.org, everywhere — the SDK's `library-packs` copy is excluded.**

Of the three ways to make the source deterministic, this is the one that makes `contentHash` a function
of **package identity alone**:

| option | why not |
|---|---|
| **Accept the SDK copy** (make dev boxes match CI) | The hash then depends on the **SDK patch level**. `global.json` says `rollForward: latestFeature`, so the next SDK that ships a different bundled `FSharp.Core` silently re-hashes every lock file in the org — the same class of surprise, arriving on someone else's release schedule. |
| **Pin per package, fail on an unexpected source** | Correct, and strictly more machinery than the problem needs today: one package, one bundled source. Revisit if a second SDK-bundled package appears. |
| **Exclude `library-packs`** ✅ | `contentHash` becomes a function of `(id, version)` and nothing else. nuget.org's copy is also the **signed** one. Cost is a one-time re-pin: every repo's committed `FwQFuqOA…` becomes `excLf2zM…`. |

**3. The one-time re-pin is a coherent set, not six independent bumps.** Every repo's lock file moves
together, generated by `lockfile-sync.yml` *after* the source exclusion lands in the shared build
config — otherwise a repo re-pinned to nuget.org's hash meets a CI that still resolves `library-packs`,
and its gate fails-closed on a correct lock file (which is exactly what blocked #429's repair).

**4. Say which source a hash came from.** `NU1403` is indistinguishable from a corrupt local cache and
invites the wrong repair (pasting in the `packageHash`, which fails too). Whatever reports a hash
mismatch must name **the source that served the package**, because that — not time — is the variable.
**And a `contentHash` is NEVER hand-written** — only a cold `--force-evaluate` may produce one. The
catalog `packageHash` is a *different* hash (SHA512 of the `.nupkg` bytes the feed serves, equal to the
local `.nupkg.sha512` sidecar; it is never consulted for lock validation), so pasting it in — the
obvious and wrong move the message above exists to head off — re-breaks the build. *(ADR-0031 §Decision
3, folded in here on its withdrawal.)*

**5. Any restore that WRITES or ENFORCES a lock file must be COLD.** *(ADR-0031 §Decision 1, folded in
here on its withdrawal — unchanged in substance. This was the one decision 0031 got right, and it is
independent of everything above: it is a property of NuGet's warm-vs-cold validation, not of the
re-publication story 0031 was built on.)*

Cold means: restore into a fresh, empty `NUGET_PACKAGES`, with `dotnet nuget locals http-cache --clear`
first. Both halves are load-bearing — `NUGET_PACKAGES` relocates the global-packages folder but **not**
the HTTP cache, which will replay stale `.nupkg` bytes. A gate job must not carry a NuGet cache
(`cache: false`); a cache that can change the verdict is not "speed only".

This applies to `--locked-mode` (the gate) and `--force-evaluate` (the sync) alike. A cold enforcer over
a lock file written by a warm generator only fails everyone honestly instead of silently.

The rationale: `--force-evaluate` copies `contentHash` out of the installed package folder, and
`--locked-mode` validates the committed `contentHash` against that **same** folder. If the folder is
warm — pre-seeded in the runner image, restored from an actions cache, or left by an earlier restore —
then **both the generator and the enforcer compare a record against a record and never contact the
feed.** [#460](https://github.com/FS-GG/.github/issues/460)'s fixture demonstrates it against a real
restore: poison a package's `.nupkg.metadata` and a broken lock file goes green, warm, every time. The
gate then fails in **both** directions — green on a lock file no fresh clone could restore (fails open),
and red on a correct, freshly-generated one (fails closed, which is what blocked #429's repair). A gate
whose verdict is a function of runner cache state is not a determinism gate.

It now has the regression guard 0031 shipped without: `tests/lockfile-cold/run.sh` (7 legs) and
`.github/workflows/lockfile-cold-selftest.yml` drive a real `dotnet restore` and assert both directions.

**Cold is not hermetic**, and 0031 implied it was — see *What ADR-0031 got right* above. A perfectly cold
restore still resolves the SDK's `library-packs` copy; that hole is closed by §2, not by coldness.

## Consequences

- **The framing in #429 / #453 / #457 / ADR-0031 is corrected**, including in the two places it had
  already been written into the repo as fact: `docs/architecture.md` and the `lockfile-sync.yml`
  header. A misdiagnosis that survives in the artifacts is one the next reader inherits.
- **[#460](https://github.com/FS-GG/.github/issues/460)'s fixture keeps every leg.** Its legs are
  properties of NuGet's *warm-vs-cold* validation, which this ADR does not touch; only its *narration*
  ("a package was re-published") was wrong, and that is corrected in place.
- **`--force-evaluate` in `lockfile-sync.yml` still runs cold** — §Decision 5 is unaffected.
- **This ADR now carries ADR-0031's surviving decisions, and 0031 is WITHDRAWN.** Superseding it *in
  part* left four markers — 0031's header, its correction box, this ADR's header, and the README row —
  disagreeing about which of its four decisions were still live, and a reader had to reconcile them to
  find out. A record whose premise is false cannot be a live citation target, however sound one of its
  sections is. So the two that survive are folded in above (§1 → §5, §3 → §4), every citation in the
  repo is re-pointed at **ADR-0032 §5**, and 0031 keeps its number and its body as a tombstone — the
  reasoning is the evidence of how the org got this wrong, and this ADR exists to preserve it, not to
  erase it. Its §Decision 2 (detect re-publication via the catalog `packageHash`) and §Decision 4 (we
  detect, we do not mirror) die with the premise and are **not** re-decided here: they were answers to a
  defect that never happened.
- **Implementation is not this ADR.** Excluding `library-packs` org-wide (shared build config) plus the
  coordinated re-pin is filed separately; this ADR decides *what must be true*, and the invariant to
  test against is **"regenerate the lock on two different machines, get the same bytes."**
- **A green gate is no longer evidence of hermeticity.** It is evidence that the *committed* hash
  matches what *this machine's* sources serve. Until §2 lands, that is all it has ever been.

## References

- [#471](https://github.com/FS-GG/.github/issues/471) — the finding (FS.GG.Audio, worker `osprey-a17`), independently re-measured here.
- [ADR-0031](0031-republished-package-is-a-named-failure.md) — **withdrawn** (premise false); its surviving decisions are §5 and §4 here. Kept as a tombstone; nothing in it is live.
- [#429](https://github.com/FS-GG/.github/issues/429) (the original defect), [#453](https://github.com/FS-GG/.github/pull/453) (cold generation), [#457](https://github.com/FS-GG/.github/issues/457) (the ADR-0031 item), [#460](https://github.com/FS-GG/.github/issues/460) (the failure-leg fixture), epic [#266](https://github.com/FS-GG/.github/issues/266).
