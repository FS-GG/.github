# ADR-0039: nuget.org is the read path; the org feed is the publish path

- **Status:** Accepted (ratified 2026-07-15)
- **Date:** 2026-07-14 (proposed) · 2026-07-15 (accepted)
- **Affects:** `.github` (Renovate preset, `dist/dotnet` tools manifest, `pin-coherence`, registry), FS.GG.SDD, FS.GG.Rendering, FS.GG.Governance, FS.GG.Game, FS.GG.Audio, FS.GG.Templates (every repo that *restores* an FS.GG.\* package)
- **Amends:** [ADR-0012](0012-dual-publish-to-nuget-org.md) §1 — "the org GitHub Packages feed stays the **coherence source of truth**" — which is no longer true of the read path. ADR-0012 §2–§5 (scope, byte-identical, gated ordering, listing metadata) stand.

## Context

ADR-0012 §1 ratified dual-publish as **additive**, and was explicit that nothing moved:

> Do not move the primary. The org GitHub Packages feed stays the **coherence source of truth**:
> Renovate (`default.json`), the contract-coherence gate, and the registry `package-version`
> fields continue to read from it.

**Every clause of that sentence is now false of Renovate, and the change was never ratified.** It
happened in config, as the fix to a four-year-shaped bug:

- `default.json` routes **all** `FS.GG.*` Renovate lookups to `https://api.nuget.org/v3/index.json`
  ([#576](https://github.com/FS-GG/.github/issues/576)).
- `renovate.json` **removed** the `hostRules` block that supplied a GitHub Packages token, rather
  than leaving it inert.

The reason is recorded at length in both files and is worth restating, because it is the strongest
argument for this ADR: the org feed **requires a credential even to read**, and a 401 on a Renovate
datasource *is not an error — it is an empty version list*. So the bot detected every `FS.GG.*`
dependency, enumerated no versions, and opened no PR. The `FS.GG.SDD.Cli` pin froze at 0.2.1
(#127), at 0.5.0 (#263), at 0.9.0 (#566), and at 0.10.0 — **four freezes, each hand-advanced, none
fixed**, because the config "obviously" already had the token. The confirming experiment had already
run by accident: `fs.gg.coord.cli` is pinned by its **lowercase** id, misses the case-sensitive
`matchPackageNames` override, escapes to nuget.org — and is the only `FS.GG.*` bump PR Renovate has
ever opened in this repo (#660). *The packages that matched the rule froze; the one that escaped it
bumped.*

**And the read path moved for consumers too, which is the part no document states at all.**
`release-coord-engine.yml` records it in a comment: only **1 of the 6 receiver repos** configures
the org feed as a NuGet source; **the other five restore every `FS.GG.*` package they use from
public nuget.org.** Meanwhile `dist/dotnet/.config/dotnet-tools.json` — which `sync-build-config`
byte-copies into **all six** — pins `fs.gg.coord.cli` 0.1.1. So `dotnet tool restore` in five repos
now **depends on the nuget.org push having happened**.

That inverts ADR-0012 §1's central claim. nuget.org is not the additive mirror. **It is the road into
the fleet.** The org feed is the one that needs a credential nobody has.

Two facts make this safe to ratify rather than merely tolerate: all **32 of 32** packages the org
publishes are already public on nuget.org at the same latest version (verified id-by-id, #576), and
ADR-0012 §3 requires the two feeds to carry the **byte-identical** `.nupkg`. There is no version of
this where the feeds disagree about *what* a package is.

## Decision

**1. nuget.org is the READ path for `FS.GG.*`.** Version discovery (Renovate) and package restore in
every repo that does not authenticate to the org feed resolve from `https://api.nuget.org/v3/index.json`.
No credential is required to read it, which is the property the org feed lacks and the reason the
pins froze.

**2. The org GitHub Packages feed is the PUBLISH path**, and remains the authenticated read path for
repos that configure it (`.github`'s own CI restores `FS.GG.SDD.Cli` from it with `GITHUB_TOKEN` +
`packages: read`, and keeps doing so). ADR-0012 §4's ordering is unchanged: **org feed first**, then
the byte-identical nuget.org push.

**3. The nuget.org push is therefore ON THE CRITICAL PATH, not additive** — and every decision that
treats it as optional must be re-read in that light. In particular, **ADR-0013 §5's
`vars.NUGET_ORG_PUBLISH` dormancy flag is now load-bearing.**

**But not where you would expect, and the difference is the point.** An earlier draft of this clause
said *"a producer whose flag is unset ships a package five of six receivers cannot restore"*. That is
**false of the five product repos**, and the check was worth running:

| repo | consults `vars.NUGET_ORG_PUBLISH`? | flag value | on nuget.org? |
|---|---|---|---|
| Rendering, SDD, Governance, Game, Audio | **no** — publish is gated only on `steps.ver.outputs.push` | **unset (404)** | **yes, all of them** |
| Templates | yes | `true` | yes |
| **`.github`** | **yes** | `true` | yes |

The five product repos push to nuget.org **unconditionally**, via OIDC Trusted Publishing. Their flag is
not merely unset — it is *absent*, and their packages are on nuget.org anyway. Verified 2026-07-14:
**all 32 of 32 packages match, org feed and nuget.org, at the identical newest version.** No producer is
skipping the push today.

**The flag governs exactly two workflows, and both are in `.github`:** `release-coord-engine.yml`
(**`FS.GG.Coord.Cli`**) and `release-new-sdd-workspace.yml` (`FS.GG.NewSddWorkspace`). Templates gates on
it too and has it set.

**That is a narrower hazard, and a considerably worse one.** Follow it through: `FS.GG.Coord.Cli` is
ADR-0034's coordination engine. It is pinned in `dist/dotnet/.config/dotnet-tools.json`, which
`sync-build-config` byte-copies into **all six receivers**, and **five of those six restore from
nuget.org** (decision 1). Its publish is gated behind a repo variable that **nothing asserts**. Unset it
in `.github` — or run a release in any context that does not inherit it — and the engine silently stops
reaching nuget.org, and `dotnet tool restore` fails in five repos. The failure mode is this document's
own thesis: not an error, just an empty version list.

It is `true` today. **Nothing holds it there.** So: **the one package whose absence would break the
coordination engine across the fleet is precisely the one hidden behind an unenforced flag.**

ADR-0013's own §5 amendment names the residual hole — *nothing asserts the flag is true where a policy
exists* — and this ADR is why that hole has teeth. It wants a gate: assert `NUGET_ORG_PUBLISH == true` in
every repo carrying a nuget.org Trusted Publishing policy. Tracked in [#750](https://github.com/FS-GG/.github/issues/750).

**4. One invariant, one feed.** `pin-coherence` must assert pin freshness against the feed Renovate
**bumps from**. It is currently split-brained: `scripts/check-pin-coherence.py` already defaults to
`api.nuget.org` (`PUBLIC_HOSTS` / `AUTH_HOSTS`, resolved per-pin), while
`.github/workflows/pin-coherence.yml`'s header still tells the reader freshness is measured against
`nuget.pkg.github.com/FS-GG`. The script is right and the prose is stale — but a gate whose *stated*
invariant is not its *enforced* invariant is one edit away from being wrong in the other direction.
Reconcile the prose to the script.

**5. `.github` is a package producer, and the registry must say so.** It publishes **`FS.GG.Coord.Cli`**
(ADR-0034's engine) and **`FS.GG.NewSddWorkspace`** (ADR-0016's scaffolder) to both feeds. Neither
appears anywhere in `registry/dependencies.yml` — the org's package inventory is **off by two**, and
ADR-0012 §2 and ADR-0013 §4 both enumerate a **closed producer set of three** that no longer holds.
Register both; extend the Trusted Publishing policy set to `.github`'s two workflows.

## Consequences

- **`.github`:** reconcile `pin-coherence.yml`'s header prose with `check-pin-coherence.py`;
  register `FS.GG.Coord.Cli` + `FS.GG.NewSddWorkspace` in `registry/dependencies.yml` (with their
  `nuget-org-published` state); correct the `publishing-and-deployment` skill, which today teaches
  *"each product repo owns its release workflow (**not `.github`**)"* and omits `.github` from its
  "what ships, and where from" table. Update `docs/architecture.md`'s feed picture.
- **Every receiver:** no change is required — this ADR **ratifies the state they are already in**.
  That is the point: five of them have been restoring from nuget.org for weeks with no record saying
  they may.
- **The `nuget-org-published` coherence row is materially incomplete** and should be re-derived, not
  patched: it claims *"all 23 packages / four policies"* while `default.json` and
  `check-pin-coherence.py` both assert **32 of 32** public, and `.github`'s two producers exist in
  neither count.
- **Reversibility.** Low. Retiring the org feed entirely is *not* decided here (see Alternatives) —
  but note that after this ADR the org feed's only unique role is as the first leg of the ordered
  push, and one repo's authenticated CI restore. If that erodes further, the honest next question is
  whether it earns its keep at all.
- **The prefix reservation still stands** (ADR-0013 §4, anti-squat). Note the registry currently
  calls it *"an optional anti-squat follow-on"* while both ADRs call it **required** — pick one; the
  ADRs are right.

## Alternatives considered

- **Restore the org feed as the read path** (re-add the Renovate token, have all six receivers
  authenticate). Rejected: it is the option the org *already tried*, for months, and it produced four
  silent pin freezes. Its failure mode — 401 reads as "no versions exist" — is undetectable by
  construction, which is the same fail-open shape as epic #266. It also puts a credential on the path
  of every consumer restore in order to read packages that are **public anyway**.
- **Retire the org GitHub Packages feed entirely; publish only to nuget.org.** Genuinely tempting —
  it would collapse two feeds to one and delete the whole dormancy-flag problem. **Not decided here**,
  because it is a strictly larger change (it moves the *publish* path, touches every producer's
  release workflow, and forfeits the private-by-default staging property the org feed gives a package
  before it is public). It deserves its own ADR, and this one is a prerequisite for having that
  argument on the facts.
- **Leave it unrecorded** (the status quo — config is the decision). Rejected: it is how ADR-0012 §1
  came to assert, in an Accepted record, the exact opposite of what ships. A reader following the
  corpus today would wire Renovate to a feed that cannot answer it.

<!-- Follow-up: reconcile docs/architecture.md (feed/distribution picture) and the
publishing-and-deployment skill once this is Accepted and the two .github packages are registered. -->
