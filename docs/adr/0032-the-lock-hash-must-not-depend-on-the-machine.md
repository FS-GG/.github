# ADR-0032: `FSharp.Core` was never re-published — the lock file's `contentHash` must not depend on the machine

- **Status:** Accepted
- **Date:** 2026-07-11
- **Affects:** `.github`, FS.GG.SDD, FS.GG.Rendering, FS.GG.Game, FS.GG.Audio, FS.GG.Templates, FS.GG.Governance
- **Supersedes:** [ADR-0031](0031-republished-package-is-a-named-failure.md) §Context and §Decision 2.
  ADR-0031 §Decision 1 (**cold restores**) stands — see *What ADR-0031 got right*.

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

**Cold restores (ADR-0031 §Decision 1) stand, on their own merits.** A warm package folder genuinely
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

## Consequences

- **The framing in #429 / #453 / #457 / ADR-0031 is corrected**, including in the two places it had
  already been written into the repo as fact: `docs/architecture.md` and the `lockfile-sync.yml`
  header. A misdiagnosis that survives in the artifacts is one the next reader inherits.
- **[#460](https://github.com/FS-GG/.github/issues/460)'s fixture keeps every leg.** Its legs are
  properties of NuGet's *warm-vs-cold* validation, which this ADR does not touch; only its *narration*
  ("a package was re-published") was wrong, and that is corrected in place.
- **`--force-evaluate` in `lockfile-sync.yml` still runs cold** — ADR-0031 §Decision 1 is unaffected.
- **Implementation is not this ADR.** Excluding `library-packs` org-wide (shared build config) plus the
  coordinated re-pin is filed separately; this ADR decides *what must be true*, and the invariant to
  test against is **"regenerate the lock on two different machines, get the same bytes."**
- **A green gate is no longer evidence of hermeticity.** It is evidence that the *committed* hash
  matches what *this machine's* sources serve. Until §2 lands, that is all it has ever been.

## References

- [#471](https://github.com/FS-GG/.github/issues/471) — the finding (FS.GG.Audio, worker `osprey-a17`), independently re-measured here.
- [ADR-0031](0031-republished-package-is-a-named-failure.md) — superseded in part; its cold-restore decision stands.
- [#429](https://github.com/FS-GG/.github/issues/429) (the original defect), [#453](https://github.com/FS-GG/.github/pull/453) (cold generation), [#457](https://github.com/FS-GG/.github/issues/457) (the ADR-0031 item), [#460](https://github.com/FS-GG/.github/issues/460) (the failure-leg fixture), epic [#266](https://github.com/FS-GG/.github/issues/266).
