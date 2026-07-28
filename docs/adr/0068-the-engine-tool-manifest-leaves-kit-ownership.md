# ADR-0068: the engine tool manifest leaves kit ownership, and #1077's invariant is asserted instead of arranged for

- **Status:** Accepted
- **Date:** 2026-07-28
- **Affects:** FS-GG/.github (authority), and the seven `coordination-kit` receivers — FS.GG.SDD,
  FS.GG.Rendering, FS.GG.Governance, FS.GG.Templates, FS.GG.Game, FS.GG.Audio, FS.GG.Net

## Context

`scripts/fsgg-coord` is a shell resolver that execs `fs.gg.coord.cli`, the typed coordination engine
(ADR-0034). A repo can only run that engine if it holds a `.config/dotnet-tools.json` declaring the
tool, so `dotnet tool restore` can install it.

**`.github#1077` made that structural, and it was fixing a real defect.** Before it, the manifest rode
the `build-config` fabric (4 receivers) while the shim rode the coordination kit (6). FS.GG.Templates
and FS.GG.Audio are deliberately not `build-config` receivers — a reviewed decision about how those
repos *build* — so they received the `fsgg-coord` shim and had **no engine to exec**. Nothing caught
it, because no gate asked *"can this receiver run the engine?"*. `#1077` put the manifest on the kit
too, making the two receiver sets equal by construction, and added a rule to `scripts/repos.sh
validate` refusing to let the shim row and the manifest row ride different fabrics again.

That reasoning has never been recorded in an ADR. It lives only in comments in `registry/repos.yml`
(at the `receives:` header and on the `kit:` row itself) — which is precisely why this record exists:
overturning a decision recorded in prose, with a diff that deletes the prose, loses the argument.

### What the arrangement cost, counted rather than argued

The kit is content-addressed: `registry/repos.lock` carries a digest per `kit:` source, and
`kit-published-coherence` reds on `main` whenever the roster's digests do not match the published
`FS.GG.Kit`. So `dist/dotnet/.config/dotnet-tools.json` being a `kit:` row meant that **bumping the
engine's version by one integer edited kit content** — which staled the lock, reddened `main`, and
obliged a full kit republish plus a seven-receiver fan-out.

Kit republishes on 2026-07-27 and 2026-07-28, counted:

| when | event | what changed | carried a skill change? |
|---|---|---|---|
| 07-27 | `#1528`, `#1534`, `#1535` | shim verb partition | no — fixed by `#1586` |
| 07-27 | `#1507`, `#1517`, `#1523` | engine changes, **through this row** | no |
| 07-28 | kit 0.15.1 | a `# shellcheck source-path=SCRIPTDIR` comment in `scripts/skill-view` | no |
| 07-28 | kit 0.16.0 | engine pin 0.13.0 → 0.14.0, **through this row** | no |
| 07-28 | kit 0.17.0 | the ADR-0067 §8 alarm collapse (`#1710`) | no — but a genuine kit change |

**Nine republishes, each fanning out to seven repositories, and not one carried a change to a skill.**
Four of the nine went through this row alone.

It also made a mechanical bump structurally unmergeable. Renovate PR `#1340` changed only the manifest
and sat open from 2026-07-21 to 2026-07-27, because `kit-package / verify` failed with `staged digest …
is not in registry/repos.lock` and **Renovate cannot run `scripts/repos.sh relock`**. No rebase or
retry of that PR could ever have gone green, and `engine-pin-coherence` was red on `main` on every push
for that whole period.

### The measurement that changed the answer

`#1615` was first decided as option (c) — *keep the coupling* — on 2026-07-27, on the argument that the
fan-out was not actually being discharged (7 of 7 receivers stale, spread 0.6.0–0.10.0) so the coupling
was not what hurt. That decision was **reversed by the repository owner on 2026-07-28** once the
republish count above was assembled: the cost is not one deferred fan-out, it is nine releases spent
carrying no skill change, four of them attributable to one row.

## Decision

**`dist/dotnet/.config/dotnet-tools.json` is no longer a `kit:` row.** Each `coordination-kit` receiver
owns its own `.config/dotnet-tools.json`, and Renovate bumps `fs.gg.coord.cli` in it directly.

The file **remains in this repository** as `.github`'s own canonical engine manifest and as
`engine-pin-coherence`'s subject. It is simply no longer copied into anybody else's tree.

**`#1077`'s invariant is preserved — by asserting it rather than arranging for it.** The co-fabric rule
in `scripts/repos.sh validate` is **replaced**, not deleted, by a roster-derived sweep in
`scripts/repos-audit.sh`:

> For every repo with `receives: coordination-kit`, read that repo's actual `.config/dotnet-tools.json`
> and require it to declare `fs.gg.coord.cli` with a usable version. Reported daily.

### Why the replacement is strictly stronger, and not a consolation

This is the substance of the decision, so it is stated as a property rather than a hope.

| | the rule that stood | the rule that replaces it |
|---|---|---|
| shape | `f(this repo's roster)` | `f(roster, receiver tree)` |
| what it reads | two rows in `registry/repos.yml` | each receiver's own `.config/dotnet-tools.json` |
| receiver deletes its manifest by hand | **green forever** | **red, naming the repo** |
| receiver obtains an engine another way | reads as broken | reads as fine |
| diagnostic | "these rows are on different fabrics" | "FS.GG.Templates receives the kit and declares no engine" |

The old rule could only ever constrain *which fabric two rows rode*, and inferred the receiver property
from that arrangement. It prevented **one origin** of the defect and said nothing when it recurred by
another route. The sweep grades the property `#1077` actually wanted.

The sweep's subject is **every** `coordination-kit` receiver, not only the `--kit-delivery package`
ones. The kit-pin freshness sweep beside it narrows to package receivers because a byte-copy receiver
legitimately has no `PackageReference` to grade; there is no equivalent excuse here, and narrowing
would carve `#1077`'s two original victims out of the check written to replace it.

### Why the delivery mechanism is not in doubt

Renovate's nuget manager already reads tool manifests: `/(^|/)dotnet-tools\.json$/` is one of its four
**shipped** `managerFilePatterns`, read out of renovate 43.281.1's own
`dist/modules/manager/nuget/index.js` and recorded in this repo's `default.json`. It is not a custom
manager, not a regex this org maintains, and not new.

More persuasively: `default.json` also records that `fs.gg.coord.cli` is **the only `FS.GG.*` bump PR
Renovate has ever opened in this repo**. The path this decision relies on is the one path in this org
that has demonstrably worked unattended.

### What this does *not* claim

The sweep does not grade the engine **version**. Whether a receiver's `fs.gg.coord.cli` is the newest
published one is the kit-pin sweep's shape, one package over. `#1077`'s invariant was never *"runs the
newest engine"*; it was *"can run the engine at all"*, and widening it here would red the whole fleet
on the day the engine ships a version — the opposite of what this decision buys.

The shim remains the runtime backstop: `scripts/fsgg-coord`'s resolution failure is a hard error naming
what to do, never a silent no-op. But a runtime error in one worker's session is not a substitute for
an org-wide daily check, and this decision does not treat it as one.

## Consequences

- **`#1586`'s criterion 5 is un-retired.** It read *"a CLI behaviour change after this lands must
  require no kit republish and no receiver fan-out"*, and was formally retired as unachievable between
  `#1586` and this record. Both doors it named are now shut — the verb partition by `#1586`, the
  version pin by this decision — so the criterion is meetable and is asserted by
  `tests/coord-engine-parity/shim.sh` §3f legs (a) and (c).
- **An engine release stops being a kit release.** `dotnet-tools.json` edits no longer stale
  `registry/repos.lock`, so `repos.sh relock` is no longer a precondition for merging a mechanical
  engine bump — which is what made Renovate `#1340` unmergeable by construction.
- **One kit republish is still owed by this change itself**, because kit membership changed: the kit
  goes from 9 members to 8 and stops materializing `.config/dotnet-tools.json` into receivers.
- **Receivers keep the manifest they already have.** It is a tracked file in all seven; the kit simply
  stops overwriting it. No receiver loses its engine at the moment this lands, and the sweep is what
  says so rather than an assumption.
- **A receiver's `.config/dotnet-tools.json` becomes receiver-owned.** It is no longer verified
  byte-identical by `coordination-coherence`, which is the point: the receivers' engine versions may
  now diverge, and Renovate converges them per repo.
- **`tests/coord-engine-parity/shim.sh` §3f leg (c) is inverted**, and a leg (d) is added asserting the
  replacement sweep exists — so deleting the sweep cannot silently leave the invariant unasserted.

## Alternatives considered

- **(c) Keep the coupling and retire `#1586`'s criterion 5 permanently.** This was the recorded
  decision for one day, on two arguments: ADR-0067 §9 phase 4 (`#1676`) retires the copying apparatus
  per repo anyway, so building a replacement is work that rewrite removes; and the fan-out was not being
  discharged, so the coupling was not the binding constraint. **Rejected** once the republish count
  reached nine with zero skill changes: "the rewrite will fix it" is not a reason to spend four more
  releases, and phase 4 has no date. The second argument survives and is not answered here — standing
  receiver staleness is a real, separate problem, addressed by `#1587` (automerging mechanical bumps)
  and alarmed by `#1540`'s freshness sweep.
- **(b) The kit stops materializing `.config/dotnet-tools.json` with nothing put in its place.**
  **Rejected**: it re-opens exactly the defect `#1077` closed, in the same two repos. This ADR is (a),
  not (b), and the difference is entirely the sweep.
- **(a′) Move the manifest onto the `build-config` fabric and onboard Templates and Audio.**
  **Rejected**: onboarding either repo to `build-config` is a deliberate call about somebody else's
  build (Audio has its own hand-authored `Directory.Build.props`; Templates has none at all), and it is
  not a coordination decision's to make. It would also leave the manifest on a copying fabric, so an
  engine bump would still be a materialize-fabric event.
- **Keep the `validate` rule as well as adding the sweep.** **Rejected**: with the row gone the rule
  would red on the correct roster from the day this lands, and a weaker duplicate of a properly checked
  rule is ADR-0058's restate-don't-derive defect.
- **Have the sweep grade the engine version too.** **Rejected** as scope creep in the dangerous
  direction — see *What this does not claim*.
