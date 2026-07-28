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

### ⚠ THE DELIVERY PATH IS NOT LIVE YET — correction recorded 2026-07-28, same day (`#1798`)

**Read this before the next section, which overstates the case.** The paragraph below is true about
Renovate's *manager*, and false about this org's *configuration*, and the difference was found hours
after this record landed.

`default.json`'s `packageRules[5]` sets `enabled: false` for `matchFileNames: [".config/dotnet-tools.json"]`
across all seven `coordination-kit` receivers. **So no receiver is currently offered an
`fs.gg.coord.cli` bump at all.** The rule was correct when written — the file was kit-materialized, so
a bump in a receiver was a guaranteed-red PR (the `#794` churn class) — and its premise is exactly what
this ADR removed. It is now suppressing a bump against a file each receiver owns outright, which is the
same defect `#1552` fixed one rule over in the same file.

Nothing in the org would have reported this: a non-proposal is not an error and appears nowhere
(`#1533`). It was found because a fan-out worker read FS.GG.Net's pin-file comment and asked why
Renovate was quiet there; FS.GG.Net's own `.github/renovate.json` had documented it in passing.

**What this does and does not invalidate:**

- **`#1077`'s invariant is unaffected.** The `repos-audit` engine-manifest sweep grades whether each
  receiver *declares* `fs.gg.coord.cli`, not which version — live run
  [30369075698](https://github.com/FS-GG/.github/actions/runs/30369075698): *"graded 7 of 7 … 7 declare
  fs.gg.coord.cli, 0 do NOT"*. Nothing is broken today.
- **The churn result stands.** An engine bump no longer edits kit content, no longer stales
  `registry/repos.lock`, and no longer obliges a republish. That is the decision's main claim and it
  does not depend on Renovate.
- **What is wrong is the last mile**: until `#1798` lands, each receiver's engine *version* moves only
  by hand, and nothing alarms when it goes stale.

This correction is recorded here rather than only on the issue, because a reader who opens this record
directly is the one who would otherwise inherit the error — the corpus's most common defect, and the
one `check-adr-coherence.py` exists to hunt.

#### ✅ RESOLVED the same day — `#1798`, and what it cost to make the sentence true

`packageRules[5]` is **deleted**. The delivery path this ADR asserted is now live, and — this is the
part worth carrying forward — it is *asserted by something that runs* rather than by this paragraph.

- **Measured before, through Renovate's real code, not inferred.** At renovate 43.281.1,
  `applyPackageRules` returned `enabled=false, skipReason=package-rules` for **both** tools in
  **every** receiver. Independently, every receiver's Renovate Dependency Dashboard listed
  `.config/dotnet-tools.json` under `nuget` with **zero** dependencies while the file declared two —
  the dashboard drops deps carrying a `skipReason` (`dist/workers/repository/package-files.js`). Two
  different observations of the same switched-off path.
- **Measured after**: the same driver returns MANAGED for both tools in all seven receivers, and
  still MANAGED for `dist/dotnet/.config/dotnet-tools.json` here.
- **The leg that can fail** is `tests/preset-repo-scope-coherence/drive-package-rules.mjs`, run on
  every CI pass of that workflow. It carries a negative control that re-injects the deleted rule and
  requires the verdict to flip, so it cannot decay into a step that reports "enabled" about
  everything. A second leg, in the same fixture, ties the preset to the roster **biconditionally**:
  the manifest is disabled *if and only if* the `kit:` block delivers it. That is the check whose
  absence let this ADR land with its own delivery path switched off.
- **The removal was not a one-line deletion**, and the reason is worth recording. `SYNCED_RECEIVER_FILES`
  in `check-pin-coherence.py` *required* a preset disable for every authority-synced file, so the
  rule could not be deleted while the manifest sat in that tuple. Removing it there exposed a
  coupling: the same tuple also derived the set of `dist/dotnet/` paths that no `ignorePaths` entry
  may reach. Left alone, the correct removal would have **withdrawn the `#678` substring protection
  from `dist/dotnet/.config/dotnet-tools.json` in the very change that made it the fleet's only
  delivery path**. The two facts are now derived separately (`BASELINE_MANAGED_PATHS`).

**No bump PR was observed, and that is a measurement rather than a gap.** All seven receivers pin
`fs.gg.coord.cli 0.14.0` and `fake-cli 6.1.4`, and both are the newest versions their feeds serve —
so there is nothing for Renovate to propose today, and a correct configuration and a broken one look
identical from the outside. That is precisely why the evidence above is a *driver* and a *dashboard
diff* rather than a PR link: waiting for a bump to appear would have made the check unrunnable until
the next engine release.

### Why the delivery mechanism is not in doubt

> **Both arguments below are weaker than they read, and `#1798` is what they missed.** Kept verbatim
> rather than quietly rewritten, because the shape of the error is the useful part: each is *necessary*
> evidence presented as *sufficient*. Read the two rebuttals under them before relying on either.

Renovate's nuget manager already reads tool manifests: `/(^|/)dotnet-tools\.json$/` is one of its four
**shipped** `managerFilePatterns`, read out of renovate 43.281.1's own
`dist/modules/manager/nuget/index.js` and recorded in this repo's `default.json`. It is not a custom
manager, not a regex this org maintains, and not new.

More persuasively: `default.json` also records that `fs.gg.coord.cli` is **the only `FS.GG.*` bump PR
Renovate has ever opened in this repo**. The path this decision relies on is the one path in this org
that has demonstrably worked unattended.

**Rebuttal to the first.** That the manager *matches the file* says nothing about whether the org
preset leaves it enabled. It did not: `packageRules[5]` disabled it in all seven receivers, and a
shipped `managerFilePatterns` entry is exactly as true in a repo where every dep in that file comes
back `skipReason: package-rules`. "Renovate can read this file" and "Renovate will propose a bump
here" are different claims, and only the second is the decision's premise.

**Rebuttal to the second, and it is the sharper one.** `#660` was opened in **`FS-GG/.github`** —
which is *not* a `coordination-kit` receiver, is not in `matchRepositories`, and was therefore the one
repository in the org the obstacle did not cover. The strongest supporting fact came from the only
place that could not have exhibited the defect. A demonstration that a path works *somewhere* is not a
demonstration that it works *where the decision routes it*, and the seven repos the decision actually
depends on were the seven where it was switched off.

**What would have settled it, and now does:** running the preset through
`applyPackageRules` for each `(receiver, .config/dotnet-tools.json, dep)` triple, which takes seconds
and answers the question asked. `tests/preset-repo-scope-coherence/drive-package-rules.mjs` does that
on every CI run of that workflow.

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
  now diverge, and Renovate is intended to converge them per repo — **but see the correction above:
  `default.json` currently disables that file in all seven, so until `#1798` lands the versions
  diverge with nothing converging them.**
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
