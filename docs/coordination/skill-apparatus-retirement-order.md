# The skill-apparatus retirement order (ADR-0067 §9, phase 4)

ADR-0067 §9, verbatim: *"build the view and the absence check; run it alongside the existing gates
unchanged; **port** the fixtures rather than re-derive them; **retire the old apparatus per repo, with
the freshness sweep last**."*

This is that order, **written before the first retirement** rather than derived per repo as it goes
(`.github#1676` AC 1). Phases 1–3 are landed and closed: `#1621` (measurement), `#1635` (the view and
§8's loud absence check, `scripts/skill-view`), `#1674` (the alongside comparison,
`scripts/skill-view-parity.sh`, 8/8 repos AGREE). §5 (three roots → two) landed separately as `#1636`.

Everything below was **measured on 2026-07-28** against `0ea5396`. Where a number appears, the command
that produced it appears with it.

> **Where the live state is.** The retirement count lives in **one** place — §4's *standing verdict* —
> and every other block in this document is a dated, append-only record of an attempt. If you are
> updating this document after a stage, change the standing verdict and **append**; do not edit a
> historical block to carry a new headline, and **do not add a second count anywhere, in this file or
> any other.** This document went stale three times on 2026-07-28 because the count was spread across
> four prose blocks in two sections and no single edit could bring it current — and a **fourth** time
> after that consolidation, because four receivers retired in ninety minutes while three separate
> workers queued their rows rather than contend for this file. Consolidation made the repair one edit;
> it could not make anyone able to take the lock.
>
> **`docs/architecture.md` carried a second count until 2026-07-28 and no longer does.** It now points
> at §4's standing verdict. There is exactly one count in the repository; keep it that way, and see
> [`#1750`](https://github.com/FS-GG/.github/issues/1750).
>
> **The receiver sequence is COMPLETE — 7 of 7.** Nothing in §6's *stage 1* is dispatchable.
>
> **There is a SECOND axis, and it has its own single home.** The receiver count lives in §4; the
> **stage** the order has reached lives in §6's *standing stage verdict* and nowhere else. This
> document went stale a fifth time on exactly that axis — this block and §6 and §8 and §9 each carried
> their own answer to *"has stage 2 started?"*, and stage 2 landed while all four said *not started*.
> Same failure as the count, one axis over. **Do not restate the stage anywhere either.**
>
> As of **2026-07-29**: stage 1 complete, **stage 2 LANDED**, **stage 3 reached and DECLINED** into a
> decision row. §9's sequence has terminated — see §6. Still open are the rows §4, §8 and §9 name.

---

## 1. What actually retires — and what does not

ADR-0067's Consequences name seven things "for eventual replacement": `coordination-sync`, kit
materialization, `kit-published-coherence`, `coordination-coherence`, `skill-union-assert`, the kit-pin
freshness sweep, and the kit Renovate bump loop. **Phase 4 measured that list against the mechanism and
found it conflates two different things**, and the distinction decides the whole order:

- **Duplication *within* a repo** — one repo committing the same skill under two roots. This is what
  resolve-don't-copy removes. One source, one generated view.
- **Distribution *between* repos** — `.github` shipping skills to seven receivers through a versioned
  package. §4 settled that the multi-repo split is **not** revisited, so distribution survives the
  rewrite entirely. The kit still ships; pins still go stale; bumps still have to land.

Sorting the seven by that distinction:

| piece | verdict under resolve-don't-copy | why |
|---|---|---|
| `skill-union-assert` | **RETIRES** | Its subject is *"every skill … present in every root and byte-identical across them"* (`registry/repos.yml:226`). With a generated view there is **one object**, so divergence is not detected-and-absent, it is **structurally impossible**. `scripts/skill-view-parity.sh` checks that claim rather than assuming it, and reported it on every resolved tree measured below. |
| the second committed root, per repo | **RETIRES** | This *is* the copy. |
| kit materialization | **NARROWS** | Two roots → one. The package still ships and still materializes. |
| `coordination-coherence` (+ `coordination-sync --check`) | **NARROWS** | Its skill paths halve. It also covers `scripts/fsgg-coord` and `.config/dotnet-tools.json`, which are **not** skills and are untouched by this rewrite. It does not dissolve. **Measured on the first retirement (2026-07-28, `FS.GG.Templates`): 51 graded files → 28, green both sides, with NO gate edit** — `coordination-sync`'s pin verifier derives its roots from the receiver's own `FsggKitSkillRoots`, so a receiver narrows this gate by moving its own property. The narrowing needs no hub change and reaches no other receiver. |
| `kit-published-coherence` | **UNCHANGED** | Compares the published nupkg's manifest to the staged one. The kit still publishes. |
| the kit Renovate bump loop | **UNCHANGED** | The kit still versions and still needs bumping. |
| **the kit-pin freshness sweep** | **UNCHANGED — and this is why it goes last** | *"Is receiver R's pin current?"* is a question about **distribution**. Resolve-don't-copy does not answer it and does not remove it. §9 orders it retired last; phase 4's measurement is that it has **no retirement trigger at all** under the decided end state. It goes last, and on today's evidence it does not go. |

**Consequence for the board.** Roughly a quarter of live rows (18 of 71, measured 2026-07-28) have this
apparatus as their subject, but most of them are about **distribution** — bump loops, pin staleness,
receiver fan-out — not about duplication. They do not dissolve when the copy goes. `#1676` expected a
bulk `SUPERSEDED` closure; the measured answer is that **the closure is real but it is downstream of a
retirement that has not happened yet**, and closing those rows now would close live subjects.

> **RE-MEASURED AFTER THE FIRST RETIREMENT ACTUALLY HAPPENED (2026-07-28, `FS.GG.Templates`): still
> ZERO rows close, and the reason is now sharper than "not yet".** Of 66 live rows, 40 mention the
> apparatus. Not one of them has *"a receiver commits its skills twice"* as its subject: they are
> distribution (`#1587`, `#1615`, `#1607`), `.github`'s own tree (`#1531`, `#1685`, `#1706`), the
> corpus's currency (`#1703`), or the coord engine. And the repo where the duplication fact **did**
> change has **zero open board rows of its own** — `FS.GG.Templates` is not on the board at all.
>
> So the bulk closure this rewrite promised is not merely deferred, it is **mis-sized**. Retiring the
> copy on one receiver dissolves no row because no row was ever opened about that receiver's copy; the
> duplication was a standing cost nobody filed. A closure will come from `#1685` and `#1706` when
> `.github`'s **own** duplicate roots go (§6 "Not in this order"), not from the receiver sequence.
>
> **RE-MEASURED AGAIN FOR THE SECOND CANDIDATE RECEIVER (2026-07-28, `FS.GG.SDD`): also ZERO, and this
> time the receiver IS on the board.** 69 live rows; **8** are SDD's (`#748 #750 #752 #754 #757 #760
> #764 #769`). Not one has *"FS.GG.SDD commits its skills twice"* as its subject: five are
> `SkillMirror` core semantics that ADR-0067 §3 owns and the rewrite does not touch (`#748` U+FFFD in
> the read seam, `#750` dropped `UndeclaredRoots`, `#754` the "present but unreadable" decision, `#760`
> the unobserved-copy third state, `#764` provider auxiliary-file digests), `#752` is a digest-domain
> disagreement, `#757` is the **published** `Fsgg.Schemas.agentSkillRoots` constant still saying three
> roots — an ADR-0067 **§5** row, already executed upstream by `#1636` — and `#769` is
> `coordination-coherence.yml`'s stale three-root comment and the *"71 destinations"* figure derived
> from it, which is a row about a gate that **narrows** rather than retires and therefore survives.
> Nothing was retired on SDD in any case, so nothing could dissolve. **Zero, twice, by argument.**

---

## 2. The layout every retirement retires INTO, and why it is a HALF-view

Two shapes are available. Only one of them is usable while `.github#1685` is open.

- **Half-view (the order uses this).** One root is the tracked source; the other is a generated view of
  it. In `.github` that is `.claude/skills` (tracked — it is the kit source, see §3) and
  `.agents/skills` (generated, git-ignored).
- **All-view.** Both roots are views of a third tracked directory. This is the cleaner reading of §1,
  and it is **refused today**: `scripts/skill-union-assert.sh:456` enumerates with `find` and no `-L`,
  so a root that *is* a symlink contributes zero ids and the gate exits 2.

Measured, on `.github`'s own 13 skills:

```
half-view, source=.claude/skills  →  skill-union-assert exit 0 ; skill-view-parity AGREE
half-view, source=.agents/skills  →  skill-union-assert exit 0 ; skill-view-parity AGREE
all-view,  source=skills/         →  skill-union-assert exit 2 ("no skills found under any root")
                                     skill-view-parity  exit 2 (NOT COMPARABLE), naming #1685
```

So **`#1685` is not a blocker for this order** — it binds only the all-view shape, which the order does
not use. It fails closed in both directions, and phase 3 pinned it. It becomes a blocker only if a later
change wants the all-view end state.

On the half-view layout `skill-view-parity.sh` reports the byte-identity fact as *"OLD-ONLY, and
STRUCTURALLY IMPOSSIBLE to violate here — every configured root resolves to the same object … Checked,
not assumed."* That sentence is the evidence for retiring `skill-union-assert` on that repo, and it is
re-derived per repo rather than inherited.

---

## 3. Which root is the source

**`.claude/skills`.** Not a preference — two independent consumers already require it:

- `registry/repos.yml:328-334` — all four `kit:` skill rows source `.claude/skills/<id>`, and
  `registry/repos.lock` content-addresses those exact paths.
- `scripts/generate-driver-manifest:38-41` — *"The digest is read from the canonical `.claude` root (the
  `supplied-by` path)"*.

Making `.agents/skills` the source instead would rewrite both, relock, and touch a `kit:` source — which
obliges a kit republish and a seven-receiver fan-out **for a path rename**. That is precisely the churn
ADR-0067 exists to end, so the order does not spend it.

This does not contradict ADR-0067 §5's *"`.agents/skills` (canonical, because it needs no pointing)"*.
§5's claim is about **runtime discovery** — Codex finds `.agents/skills` with no configuration — and a
generated view at that path is discovered identically. §5 did not decide which root **git tracks**;
phase 4 does, and the two consumers above decide it.

---

## 4. Per-repo eligibility — the precondition, and today's verdict

ADR-0067 §9: *nothing is retired before its replacement is proven.* For repo R the replacement is proven
when **all** of these hold. Re-run them for R immediately before retiring R; do not inherit a fleet-wide
verdict.

1. `scripts/skill-view` is **present and executable in R**. It is the replacement; a repo without it has
   no replacement.
2. `scripts/skill-view-parity.sh --tree <R>` exits **0 (AGREE)** on R's current tree, and again on R's
   resolved equivalent (the recipe is in that script's header, lines 54–67).
3. R's post-retirement layout is a **half-view**, not all-view (§2).
4. R's second root is not left committed. `scripts/skill-view:331` **refuses to generate over a root git
   tracks, with no override** — so a repo whose second root is still committed cannot have a view
   generated there at all.

### The standing verdict — 2026-07-28 09:55Z: **7 of 7 retired. The receiver sequence is COMPLETE.**

> **No receiver commits its skills twice. Nothing is in flight.** `FS.GG.SDD` was refused at attempt 2
> and retired at attempt 8, after the two findings that refused it were fixed in that repo.
>
> **Counted, not carried forward**, and counted from the *tree* rather than from any report:
>
> ```
> # per receiver in registry/repos.yml:114-120, against that repo's main
> gh api "repos/FS-GG/<R>/git/trees/<main-sha>?recursive=1" \
>   --jq '[.tree[]|select(.type=="blob")|select(.path|startswith(".agents/skills/"))]|length'
> gh api "repos/FS-GG/<R>/contents/.agents/skills"          # expect 404
> ```
>
> **All seven return `0` tracked blobs and `404`** — in fact zero `.agents/**` tree entries of any
> kind. The tree read is the load-bearing half: the contents endpoint answers a secondary rate limit
> with **403**, and a 403 is *"I could not evaluate this"*, which is never a 404 (`#266`). This sweep
> was rate-limited twice before it produced a number, which is why it is dated **09:55Z** and not
> earlier. **A 09:20Z sweep could not have said 7 of 7**: `FS.GG.SDD`'s retirement merged at
> **09:37:46Z**, so before that the honest answer was 6 of 7.
>
> | attempt | receiver | outcome | mechanism | merged | second root at the attempt |
> |---|---|---|---|---|---|
> | 1 | `FS.GG.Templates` | **RETIRED** | [Templates#323](https://github.com/FS-GG/FS.GG.Templates/pull/323), squash `531b01b` | 03:14:36Z | 4 skills, **23** files |
> | 2 | `FS.GG.SDD` | **REFUSED** — B5 + `skill-manifest.json`; no PR was ever opened | — | — | 32 skills + the manifest, 52 files |
> | 3 | `FS.GG.Audio` | **RETIRED** | [Audio#210](https://github.com/FS-GG/FS.GG.Audio/pull/210), squash `52a358f` | 07:24:38Z | 20 skills, **39** files |
> | 4 | `FS.GG.Net` | **RETIRED** | [Net#45](https://github.com/FS-GG/FS.GG.Net/pull/45), squash `602f47a` | 07:25:08Z | 4 skills, **23** files |
> | 5 | `FS.GG.Game` | **RETIRED** | [Game#519](https://github.com/FS-GG/FS.GG.Game/pull/519), squash `b1d4fbd` | 08:21:59Z | 21 skills, **41** files |
> | 6 | `FS.GG.Governance` | **RETIRED** | [Governance#337](https://github.com/FS-GG/FS.GG.Governance/pull/337), squash `4daa25f` | 08:45:21Z | 15 skills, **34** files |
> | 7 | `FS.GG.Rendering` | **RETIRED** | [Rendering#1125](https://github.com/FS-GG/FS.GG.Rendering/pull/1125), squash `63f08ba` | 09:01:09Z | 50 skills, **70** files |
> | 8 | `FS.GG.SDD` | **RETIRED** — the retry | [SDD#779](https://github.com/FS-GG/FS.GG.SDD/pull/779), squash `730a214` | 09:37:46Z | 32 skills, **51** files |
>
> Every file count above is the number of paths under `.agents/skills/` the squash reports with
> `status: removed`, read from `repos/FS-GG/<R>/commits/<squash>` — not the number the retiring
> worker stated. They agree, with **one correction**: stage 2 recorded **52** tracked files on
> `FS.GG.SDD` and the retirement removed **51**, because `FS.GG.SDD#771` moved `skill-manifest.json`
> into the surviving root in between.
>
> **"Stage" is ambiguous in this record, and the table above fixes it by numbering ATTEMPTS.** Two
> blocks below both call themselves *stage 5* — `FS.GG.Game`'s and `FS.GG.Governance`'s — because one
> numbered attempts and the other numbered receivers-actually-retired. Both were right about their own
> ordinal and neither could see the other. Governance is the **sixth attempt** and the **fifth
> receiver retired**; the historical blocks are left as they were written, and this table is the
> tie-breaker.

**This heading carries the count, and nothing anywhere else does.** The blocks under it are dated,
append-only records; when the count changes, change *this* line and append a record, never edit a
record to carry a new headline. That rule was learned the expensive way — this document was stale
**three separate times on 2026-07-28**: after attempt 1 it said 0 of 7, after attempt 3 it said 1 of
7, and `#1723` (filed to fix the second of those) was itself **two attempts out of date** by the time
a worker reached it, asking for a count of 2 when the truth was 3.

> **It went stale a FOURTH time, and `#1723`'s consolidation is not what failed.** This block said
> *"3 of 7"* while four more receivers retired under it, because four retirements in ninety minutes
> each queued their record rather than contending for this file (`#1754`, `#1756`, and `#1723`'s own
> Game comment). Consolidating the count made the repair **one edit instead of four**; it did not make
> anyone able to take the lock. The fourth recurrence is a *scheduling* fact, not a layout one.
>
> **And the count was living in a THIRD file the whole time.** `docs/architecture.md` carried its own
> *"FIVE of the seven receivers hold a view root"* headline with its own seven-row table — stale in
> the same way, and citing `#1754` while doing it. It has been reduced to a pointer at this block in
> the same change that wrote this one. **There is now exactly one count in the repository. Do not add
> a second.** The checker that would catch a fifth recurrence is
> [`#1750`](https://github.com/FS-GG/.github/issues/1750); it is deliberately not built here.

#### How the verdict got here (historical — do not read as current)

The roster is `registry/repos.yml:104-110` — `sdd`, `rendering`, `governance`, `templates`, `game`,
`audio`, `net`. **When this section was first written all seven failed precondition 1**, for a reason
that was not on the board:

> **`scripts/skill-view` is not a `kit:` row.** The kit's six sources are four skills, `scripts/fsgg-coord`
> and `dist/dotnet/.config/dotnet-tools.json` (`registry/repos.yml:328-343`, `registry/repos.lock:9-14`).
> Nothing delivers the replacement to a receiver, and **no published version of `FS.GG.Kit` ever has** —
> including 0.14.0.

This is a stronger statement than "the receivers are stale". A current receiver would still have no
replacement.

Preconditions 2 and 3 are satisfiable — the parity harness measured 8/8 AGREE on `#1674`, and §2 above
re-measured the resolved half-view shape green. Precondition 4 is **not reachable by any mechanism that
exists**, which is blocker B2 below.

> **UPDATED 2026-07-28 ([#1696](https://github.com/FS-GG/.github/issues/1696)).** Both statements above
> were true of every kit up to and including 0.14.0 and are **no longer true of the package**. `FS.GG.Kit`
> **0.15.0** delivers `scripts/skill-view` (with the two libraries it sources) and gives precondition 4 a
> mechanism: `FsggKitViewSkillRoots`, the third root disposition, whose sweep is what un-tracks a
> receiver's second root without anybody hand-deleting a mirror.
>
> **The verdict is still 0 of 7, and for a different reason.** Precondition 1 asks about repo R's tree,
> not about the newest package, and no receiver has taken the bump: `#1587`'s diff-shape guard refuses it
> on all seven ([`#1693`](https://github.com/FS-GG/.github/issues/1693), B3 below), which is now the
> *only* thing between the fleet and stage 1. Re-run §4's four preconditions on R's own tree the day R is
> retired, exactly as this section already says; do not read 0.15.0's existence as any repo's eligibility.

> **UPDATED 2026-07-28, later the same day. The verdict is 1 of 7: `FS.GG.Templates`, and it is DONE.**
> `FS.GG.Templates#320` merged (`8f649bc`) and took **0.15.0**. `#1693`'s own worker had already corrected
> the paragraph above: `#1587`'s diff-shape guard **does not exist yet**, so nothing was ever mechanically
> refusing the bumps — they were unmerged because nothing automerges them, and a hand merge was available
> the whole time. Do not carry "B3 blocks the fleet" forward; carry "each receiver's bump has to land, and
> five of the six remaining have no current one".
>
> §4's four preconditions, re-run on `FS.GG.Templates@8f649bc` **that day** rather than inherited from
> phase 3's fleet-wide 8/8:
>
> | precondition | measured |
> |---|---|
> | 1 — `scripts/skill-view` present and executable in R | **yes**, 21,851 bytes, and it ran: it produced the view and its own `check` passed over it |
> | 2 — parity AGREE on R's tree, and again on R's resolved equivalent | **AGREE / AGREE.** Committed tree: `old=ok new=ok` over 4 ids in 2 roots. Resolved half-view: same, with byte-identity reported *"STRUCTURALLY IMPOSSIBLE to violate here — every configured root resolves to the same object … Checked, not assumed."* |
> | 3 — half-view, not all-view | **half-view** (`.claude/skills` tracked source, `.agents/skills` generated), so `#1685` is not engaged |
> | 4 — second root not left committed | **satisfied by the retirement commit itself** — the deletions and the generate are the same change, which is the only order `skill-view` permits: it refused to generate while the root was still tracked, measured |
>
> Retired on `FS.GG.Templates` as [#323](https://github.com/FS-GG/FS.GG.Templates/pull/323), squash
> `531b01b`. **Stage 1 order deviated deliberately**: §6 lists `FS.GG.Net` first on blast radius, and
> eligibility outranks blast radius — §9 forbids retiring where the replacement is unproven, and Net has
> no bump PR at all. Templates was the only eligible repo, and it is also the only receiver with **no
> `gate.yml` and no `build-config`**, so it runs no second materialize that a view root could red.
>
> **Two things the first retirement measured that §4 did not ask for, and which every later receiver
> owes:**
>
> 1. **The view must be generated in the receiver project, not in a workflow.** An uncommitted root is
>    absent in every fresh checkout, and `FsggKitCheckSkillView` runs on **every** materialize — so a
>    fresh clone of the retirement commit *without* a generate step fails with *"view skill root
>    '.agents/skills' is ABSENT or a DANGLING link"*, and the receiver's next Renovate kit bump would red
>    on a tree nobody touched. Templates ships `FsggTemplatesGenerateSkillView`, a target with
>    `BeforeTargets="FsggKitCheckSkillView"` that runs **the receiver's own pinned `scripts/skill-view`**.
>    Not a hub workflow step: a workflow step only covers the trees that run that workflow, and a hub
>    script would put hub state back inside a receiver's verdict (`#1584`).
> 2. **The retirement removes the only gate that would notice the root leaving the contract later, and the
>    same change must replace it.** Measured on a tree with `FsggKitViewSkillRoots` emptied: the
>    materialize is green (*"nothing to assert"*), `coordination-sync --check --against-pin` is green (it
>    reads `FsggKitSkillRoots` alone), and the root is simply gone from the runtime contract — the exact
>    silent class ADR-0067 §8 exists to prevent. Templates' replacement is
>    `tests/composition/lib/skill-view-roots.sh`, asserting the union is ADR-0011's two on its **required**
>    `composition` check, with an offline can-fire demo that drives the assertion (both `bad` arms mutated
>    to `ok`; the demo red both times).

> **STAGE 2 ATTEMPTED ON `FS.GG.SDD`, 2026-07-28, AND REFUSED.** (This block said *"the verdict is
> still 1 of 7"*. It was true for about an hour. The count now lives only in the standing verdict
> above.) `FS.GG.SDD@387adc6` passed all four of §4's preconditions and was **still not eligible**, because §9's
> precondition is not "the four checks pass", it is *"nothing is retired before its replacement is
> proven"* — and SDD is the first receiver where the thing being retired has a **live, required
> replacement-less consumer**. Everything below was measured on that tree, that day.
>
> §4's four preconditions, re-run on `FS.GG.SDD@387adc6` rather than inherited from Templates:
>
> | precondition | measured |
> |---|---|
> | 1 — `scripts/skill-view` present and executable in R | **yes**, 21,851 bytes, mode 0755, with `lib/roots.sh` + `lib/args.sh` beside it; it ran and produced the view |
> | 2 — parity AGREE on R's tree, and again on R's resolved equivalent | **AGREE / AGREE.** Committed tree: `old=ok new=ok` over **32 ids in 2 roots**, byte-identity OLD-ONLY and still live (`byte-differing=0`). Resolved half-view: same population, byte-identity *"STRUCTURALLY IMPOSSIBLE to violate here — every configured root resolves to the same object … Checked, not assumed."* |
> | 3 — half-view, not all-view | **half-view** (`.claude/skills` tracked source, `.agents/skills` generated), so `#1685` is not engaged |
> | 4 — second root not left committed | reachable — the dry run took it (52 tracked files + `.gitignore`, one commit) |
>
> Kit pin **0.15.0** (`Directory.Packages.local.props`, via `FS.GG.SDD#762`/PR #730 → `317f692`);
> re-creation hazard closed by `FS.GG.SDD#767`/PR #768 → `387adc6`; gates green on `main` by run id —
> `kit / coordination-kit` **30328649443**, `skill-union / skill-union` **30328649411**, `gate`
> **30328649420**, all `success` at `387adc6`.
>
> **THE THREE THINGS THAT STOP IT, all measured on a dry-run retirement of `387adc6` in a throwaway
> clone (retire → generate → run the gates):**
>
> 1. **SDD wires a `skill-union` caller, it is a REQUIRED context under `enforce_admins`, and the
>    reusable workflow it calls CANNOT see a generated root.** `.github/workflows/skill-union.yml`
>    (landed `a066e0b`, FS.GG.SDD#718) is a `uses:` of this repo's `skill-union-assert.yml`, which
>    checks the caller out and asserts over the checkout — there is no generate step and a `uses:` job
>    cannot add one. Measured on the retired tree **without** the view:
>    `::error::skill-union-assert: configured root is absent: ./.agents/skills`, **exit 2**. With the
>    view present it is exit 0 (`in-every-root=32/32`), so the gate is fine about the *layout* and fatal
>    about the *checkout*. That workflow's own header states the limit and forbids inferring the lift:
>    *"this workflow can audit a COMMITTED tree … it CANNOT audit a tree generated during the run …
>    Adding an artifact input to lift that limit is a decision, not a tidy-up — do not infer it from
>    this note."* So the replacement for SDD's required union gate **does not exist**, and §9 forbids
>    retiring ahead of it. This is **B4's own trigger**, corrected below, and it is filed as
>    [#1715](https://github.com/FS-GG/.github/issues/1715).
> 2. **`.agents/skills/skill-manifest.json` is producer-authoritative, lives ONLY in the root being
>    retired, and the view silently deletes it.** 6,891 bytes, tracked, the one path `diff -r
>    .claude/skills .agents/skills` reports as `Only in .agents/skills`. It is read by
>    `scripts/materialize-skill-roots.fsx` for content-addressing, pinned at that literal path by
>    `tests/FS.GG.SDD.Commands.Tests/ProcessSkillManifestTests.fs`, and documented in `AGENTS.md` and
>    `CLAUDE.md`. After the dry-run retirement `git status --porcelain` is **empty** and the file is
>    **gone** — the loss is invisible to git. Measured consequence on SDD's **required** `gate` job,
>    which runs `materialize-skill-roots.fsx --check`: `System.Exception: producer manifest missing:
>    …/.agents/skills/skill-manifest.json`, **both** with the view absent and with the view generated.
>    Relocating it to `.claude/skills/skill-manifest.json` fixes it (re-measured: `--check` clean) and
>    preserves the `.agents/skills/skill-manifest.json` path through the view — but moving a
>    producer-authoritative manifest is a contract change, not a mechanical one, and it is **not** the
>    28 repo-owned skills in `.codex/skills`, which this order does not touch (ADR-0065 §Retiring a
>    root). Filed as [`FS.GG.SDD#771`](https://github.com/FS-GG/FS.GG.SDD/issues/771).
> 3. **A view root stays in `materialize-skill-roots.fsx`'s WRITE set.** That driver derives its write
>    set as `Schemas.agentSkillRoots` **minus `FsggKitRetiredSkillRoots`** and subtracts nothing for
>    `FsggKitViewSkillRoots` (`scripts/materialize-skill-roots.fsx:219-236`). With `.agents/skills`
>    moved to the view disposition it still reports `roots : .claude .agents` and `writes : 102 planned
>    by SkillMirror.mirrorFiles` — `changed : 0` only because `--mode link` makes both paths the same
>    object. Under `--mode copy` (the tool's own Windows fallback) it would write a second real copy
>    back into the view root. That is `FS.GG.SDD#767`'s re-creation hazard **one disposition over**;
>    #767 closed it for retired roots only. Filed as
>    [`FS.GG.SDD#770`](https://github.com/FS-GG/FS.GG.SDD/issues/770).
>
> **Nothing was retired on `FS.GG.SDD`.** No PR was opened against it; the dry run lived and died in a
> throwaway clone. Items 2 and 3 are SDD-side and fixable in SDD; item 1 is not, and is the one that
> makes the refusal a §9 refusal rather than a scheduling one.
>
> > **ITEM 1 IS RESOLVED — [`#1715`](https://github.com/FS-GG/.github/issues/1715) closed 2026-07-28
> > 08:04:47Z, shape (b).** The `skill-union` caller was **retired** rather than taught to see a
> > generated root, so the thing that made SDD ineligible no longer exists on any receiver. Verified
> > on the three repos that wired one: `FS.GG.SDD`, `FS.GG.Rendering` and `FS.GG.Governance` each
> > require **`skill-view-check`** and **no longer require `skill-union / skill-union`** (read back
> > from `…/branches/main/protection/required_status_checks` on 2026-07-28), and
> > `FS.GG.SDD/.github/workflows/skill-union.yml` is **404**. The full decision is §5.1.
> >
> > **SDD is still not retired**, and item 1 clearing does not retire it: findings **2 and 3 above
> > stand unresolved** (`FS.GG.SDD#771`, `FS.GG.SDD#770`). SDD's `.agents/skills` still holds 32 skill
> > directories **and `skill-manifest.json`** — re-read from `main` 2026-07-28, and the 33rd entry is
> > finding 2 sitting exactly where the dry run found it. A worker re-approaching SDD is unblocked on
> > the *gate* and blocked on the *manifest*.
>
> **What this changes about the shape of the remaining six.** Templates was the *thin* receiver — no
> `gate.yml`, no `build-config`, no repo-owned skills in the audited roots, no `skill-union` caller.
> SDD is the *framework* receiver: its own test suite reads its own roots from the repo path in a bare
> checkout, `gate.yml` runs the materializer's `--check` on that same bare checkout, and it wires the
> union gate. Its blast radius is `.github`-shaped, which is precisely §6's *"Not in this order"*
> argument for why the authority's own roots are a separate item. **Do not read Templates' cost as the
> per-receiver cost.**

> **STAGE 3 RETIRED `FS.GG.Audio` and STAGE 4 RETIRED `FS.GG.Net`, both 2026-07-28.** Preconditions
> re-run on each receiver's own pre-retirement tree — `FS.GG.Audio@1f9c58c`, `FS.GG.Net@e97186a` — and
> re-derived again here from those trees rather than inherited from either stage's report:
>
> | precondition | `FS.GG.Audio@1f9c58c` | `FS.GG.Net@e97186a` |
> |---|---|---|
> | 1 — `scripts/skill-view` present and executable | **yes**, 21,851 bytes mode 0755; it ran and produced the view (`20/20 declared skill(s) visible`) | **yes**; it ran and produced the view (`4/4 declared skill(s) visible`) |
> | 2 — parity AGREE on the tree and on the resolved equivalent | **AGREE / AGREE** over **20 ids in 2 roots** | **AGREE / AGREE** over **4 ids in 2 roots** |
> | 3 — half-view, not all-view | **half-view**, so `#1685` unengaged | **half-view**, so `#1685` unengaged |
> | 4 — second root not left committed | satisfied by the retirement commit itself (39 tracked deletions) | satisfied by the retirement commit itself (23 tracked deletions) |
> | B5 reach — does R wire a `skill-union` caller? | **no** (`grep -rl skill-union .github/` empty) | **no** |
> | `diff -r <source> <second-root>` (§7's pre-retirement check) | **silent** | **silent** |
>
> Landed as [Audio#210](https://github.com/FS-GG/FS.GG.Audio/pull/210) squash `52a358f` and
> [Net#45](https://github.com/FS-GG/FS.GG.Net/pull/45) squash `602f47a`; both **green on `main` by run
> id** — Audio `Build + test` **90207951357**, `kit / coordination-kit` **90207951562**; Net
> `Build + test` **90208065783**, `Composition (runtime skill roots)` **90208065757**,
> `kit / coordination-kit` **90208065377**, all `success` at the squash.
>
> **THE COST PREDICTOR STAGE 2 GUESSED IS WRONG, AND THAT MATTERS MORE THAN THE TWO ROWS ABOVE.**
> Stage 2 generalised from SDD that *framework-shaped* receivers — a `gate.yml`, `build-config`,
> repo-owned skills in the audited roots — would be expensive. **Audio disproved it.** Audio has a
> `gate.yml` and **16 repo-owned `fs-gg-sdd-*` skills inside the root being retired** (of 20 ids
> total), and was still cheap — because those 16 sat in **both** audited roots, so the view reproduces
> them and `diff -r` is silent. Nothing had to be relocated.
>
> The two predictors that actually held across five attempts are:
>
> 1. **`diff -r <source> <second-root>` in the second-root direction.** Non-empty means a file with no
>    home in the view, and it is the only thing that stopped a receiver on content. It was non-empty
>    exactly once — SDD's `skill-manifest.json` — and that is exactly the one receiver that stalled on
>    content.
> 2. **Whether R wires a `skill-union` caller** — historical, now moot fleet-wide (`#1715`), and it is
>    the only thing that stopped a receiver on gating.
>
> Repo size and repo shape predicted **neither**. Audio is the largest retirement so far by files (39)
> and by repo-owned skills (16) and cost the least trouble of the three.
>
> **And `FS.GG.Net` was not a third data point at all — it was `FS.GG.Templates` again.** Re-measured
> here: both receivers carry the **same 4 kit skill ids** (`check-board`,
> `cross-repo-coordination`, `intra-repo-parallel-work`, `pnext-item`), **23 files**, and **zero**
> repo-owned skills in the audited roots. So the sample is smaller than the stage count suggests: five
> attempts, but only **three distinct receiver shapes** — thin (Templates, Net), framework with a
> producer-authoritative file (SDD), and skill-heavy-but-symmetric (Audio). Reading "four stages
> succeeded" as four independent confirmations overstates the evidence by one.
>
> **A record that only records what held is not a record.** Stage 2's generalisation is written down
> here *because* it was wrong; the previous version of this document would have carried it forward
> unchallenged into Governance, Game and Rendering, each of which is framework-shaped and would have
> been priced as expensive on an argument Audio had already falsified.

> **THE KIT PIN IS NOT IN THE SAME FILE ON EVERY RECEIVER, AND THE ORDER USED TO IMPLY IT WAS**
> ([`#1725`](https://github.com/FS-GG/.github/issues/1725) — it caused a real wrong turn on stage 4).
> **Three distinct locations across the four receivers measured so far**, each read from the repo:
>
> | receiver | where `FS.GG.Kit`'s version actually is |
> |---|---|
> | `FS.GG.Templates` | inline `<PackageReference Include="FS.GG.Kit" Version="…" />`, with `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>` — no CPM |
> | `FS.GG.Net` | **CPM** — `Directory.Packages.props:19`, `<PackageVersion Include="FS.GG.Kit" Version="0.15.0" />`. `.config/kit/FS.GG.Kit.receiver.proj` contains **zero** `Version=` and says so itself: *"Version pinned centrally in Directory.Packages.props (CPM; Renovate-managed), like every other pin."* |
> | `FS.GG.Audio` | **CPM** — `Directory.Packages.props`, `Version="0.15.0"`; its receiver project likewise contains **zero** `Version=` |
> | `FS.GG.SDD` | `Directory.Packages.local.props` |
>
> **`FS.GG.Governance`, `FS.GG.Game` and `FS.GG.Rendering` were NOT measured for this table** — none of
> them declares `FS.GG.Kit` in `Directory.Packages.props`, and where each one *does* pin it was not
> established here. That is an unknown, not a fourth shape, and it is written down as an unknown
> deliberately: guessing it is the exact failure `#1725` records.
>
> **Do not assert a path you have not opened.** A worker who re-measures the *value* dutifully while
> taking the stated *path* on trust opens a file that does not contain the pin, finds nothing, and has
> to guess whether that is "no pin" or "wrong file" — on Net the honest reading of the stated path was
> that precondition 1 FAILS, which is the opposite of the truth. The location-independent read that
> works on all three shapes is `coordination-sync --check --against-pin`, which reports the resolved
> version (*"all N materialized file(s) match the FS.GG.Kit 0.15.0 this tree pins"*) without naming a
> file at all. **Prefer it.**
>
> > **ALL SEVEN ARE NOW MEASURED, and the three unknowns above resolved to the SAME location — there is
> > no fourth shape.** `FS.GG.Governance`, `FS.GG.Game` and `FS.GG.Rendering` each pin in
> > `Directory.Packages.local.props` (Game's at `:36`), which is `FS.GG.SDD`'s. So the fleet has **three
> > distinct locations, not four**: inline (Templates), `Directory.Packages.props` under CPM (Net,
> > Audio), and `Directory.Packages.local.props` (SDD, Game, Governance, Rendering). **Game's stage
> > report calls itself *"a FOURTH pin location"*; it is a fourth data point for the same third
> > location, and the distinction is the whole of `#1725`.** Recording it as a new location would have
> > been the same class of error `#1725` was filed about.

> **THE §8 REPLACEMENT ALARM HAS BEEN HAND-BUILT THREE TIMES, IN THREE DIFFERENT SHAPES, AND ONE OF
> THEM CANNOT BLOCK A MERGE.** §4's stage-1 finding 2 says the retirement removes the only gate that
> would notice a view root leaving the runtime contract, and that the same change must replace it. It
> did — differently every time. Read back from each repo's `gate.yml` and its branch protection on
> 2026-07-28:
>
> | receiver | the alarm | rides | required? |
> |---|---|---|---|
> | `FS.GG.Templates` | `tests/composition/lib/skill-view-roots.sh` | the inherited `composition` check | **yes** — `composition` is a required context |
> | `FS.GG.Audio` | `scripts/check-skill-view-roots.sh`, as a *step* named *"Runtime skill-root contract"* | the existing `Build + test (locked restore, net10.0, headless)` job | **yes** — that job is a required context |
> | `FS.GG.Net` | `tests/composition/skill-view-roots.sh`, in a **new job** named `Composition (runtime skill roots)` | nothing — it is its own job | **NO** ([`#1727`](https://github.com/FS-GG/.github/issues/1727)) |
>
> Net's job is `success` on `main` (**90208065757**) and it is *not* in
> `…/protection/required_status_checks` — so the check that replaces the retirement's lost loud
> failure is the one check that cannot stop a merge. A green that cannot block is `#266`'s subject.
>
> **The next receiver must ride a context that is ALREADY REQUIRED rather than invent a fourth shape.**
> Adding a new job is the tempting move — it is tidier and it isolates the concern — and it is the move
> that silently drops the alarm's authority, because a new job's context is not required until somebody
> POSTs it, and nothing in the retirement makes them. Audio's shape (a step inside an
> already-required job) is the cheapest one that keeps the teeth.
> [`#1710`](https://github.com/FS-GG/.github/issues/1710) owns collapsing the three into one kit-shipped
> assertion and predicted exactly this hand-copy; it has now happened three times out of three.
> [`#1730`](https://github.com/FS-GG/.github/issues/1730) was filed and closed when Audio landed with no
> alarm at all.

> **THE §8 ALARM'S SHAPE IS NOW SETTLED, 6–1 — and the block above is superseded rather than edited.**
> Seven receivers, seven hand-built alarms, read back from each repo's workflows and branch protection:
>
> | receiver | the alarm | rides | required? |
> |---|---|---|---|
> | `FS.GG.Templates` | `tests/composition/lib/skill-view-roots.sh` | the inherited `composition` check | **yes** |
> | `FS.GG.Audio` | `scripts/check-skill-view-roots.sh`, as a step | `Build + test (locked restore, net10.0, headless)` | **yes** |
> | `FS.GG.Net` | `tests/composition/skill-view-roots.sh`, in a **new job** | nothing — it is its own job | **NO** ([`#1727`](https://github.com/FS-GG/.github/issues/1727)) |
> | `FS.GG.Game` | `scripts/check-skill-view-roots.sh`, **first** step | `Build-config drift check (shared-build-config)` | **yes** |
> | `FS.GG.Governance` | `scripts/check-skill-view-roots.sh` | `skill-view-check` | **yes** |
> | `FS.GG.Rendering` | `scripts/check-skill-view-roots.sh` | `Deterministic gate` | **yes** |
> | `FS.GG.SDD` | `scripts/check-skill-view-roots.sh` | `skill-view-check` | **yes** |
>
> **Ride a context that is ALREADY REQUIRED.** Adding a new job is the tempting move — tidier, isolates
> the concern — and it is the move that silently drops the alarm's authority, because a new job's
> context is not required until somebody POSTs it and nothing in the retirement makes them. Audio's
> shape — a repo-owned script inside a job the branch already requires — was **ported** to Game,
> Governance, Rendering and SDD rather than re-derived, and Templates reaches the same place through an
> inherited context. **`FS.GG.Net` is the sole outlier**, and it is now a population of one rather than
> a pattern. *Which* required job is a per-repo judgement; the four later receivers each picked one
> whose subject already **is** that repo's kit receiver contract, and Game's goes before `setup-dotnet`
> so a failed restore below it cannot turn the assertion into a step that never ran.
>
> **EVERY LANE NEEDS A CAN-FIRE DEMONSTRATION, AND THE DEMONSTRATION ITSELF MUST BE MUTATION-TESTED.**
> This is the hardest-won finding in the §8 story, and hand-copying is what found it:
>
> * **Audio's shipped alarm reported a DANGLING view root as GREEN** — ADR-0067 §8's own headline class
>   passing the alarm written to catch it. The guard was `[[ ! -e "$view" ]]`, and **`-e` follows
>   symlinks**, so `.agents/skills -> ../does-not-exist` answered it exactly as a missing path does, and
>   the `! -d` branch carrying the dangling message was **unreachable for the case it names**. Game's
>   worker found it while porting, fixed it there (`! -e && ! -L`, plus a dedicated dangling branch),
>   and filed [`FS.GG.Audio#212`](https://github.com/FS-GG/FS.GG.Audio/issues/212) — **since repaired in
>   Audio at `26d2bb7`; that issue is CLOSED.**
> * **Why it survived upstream is the generalisable half.** Audio's can-fire demo drove the
>   *declaration* lane over six fixtures and was thorough. The *view* lane — the only one that reads the
>   filesystem — **had no demo at all**. A lane without a demonstration is not a weaker lane, it is an
>   unobserved one.
> * **SDD mutated the DEMONSTRATION, which is the standard from here.** Collapsing its absence test back
>   to a bare `[[ ! -e ]]` — `FS.GG.Audio#212` exactly — drops `can-fire(resolve)` from 5/5 to **4/5**
>   and reds the alarm. That is what makes a demo not a tautology.
> * **[`FS.GG.Templates#324`](https://github.com/FS-GG/FS.GG.Templates/issues/324) is OPEN, and is a
>   wider gap than Audio's was.** Templates' alarm has **no view-resolution lane at all** — no
>   `assert_view_resolves`, and nothing else in its composition harness asserts `.agents/skills` on
>   disk. Dangling, text-file (`core.symlinks=false`, ADR-0067 §6) and partial view roots are each
>   **unobserved** there, exit 0 with no diagnostic in both runtimes. **The fleet's oldest alarm is its
>   weakest**, which is what you would expect and nobody checked.
> * **`FS.GG.Net`'s alarm has NO can-fire demonstration this worker could find**, and no measurement
>   that Net's own alarm *reds* on the mutated tree. `#1721`'s report proves the **required set** goes
>   green there — that is the hole, not the alarm's response to it. **That is "I could not evaluate
>   this", and it is never "I evaluated it and it passed" (`#266`).** Net is the outlier twice —
>   unrequired *and* undemonstrated — and both halves belong on `#1727`.
>
> **The `absent outright` carve-out does NOT transfer; re-decide it per receiver.** Audio and Game treat
> an absent view root as green-by-design, because nothing at the path is the normal pre-materialize
> state of a bare checkout there and reddening it would fire on every green build. Governance, Rendering
> and SDD treat absence as **RED**, because their host job *generates the view immediately before
> asserting it*, so absence means the generate step was removed. Measured on SDD with the
> receiver-project target deleted: `-t:FsggKitMaterialize` on a bare checkout of the retired tree fails
> *"view skill root '.agents/skills' is ABSENT or a DANGLING link"* — and that is the command its
> **required** `Shared-build-config drift check` runs. **Copy the shape; re-decide the carve-out.**
>
> [`#1710`](https://github.com/FS-GG/.github/issues/1710) owns collapsing these into one kit-shipped
> assertion and predicted exactly this hand-copy. **It has happened seven times out of seven** — and,
> awkwardly for its own case, the copying is the only reason two of the defects above were ever found.

> **STAGE 5 EXECUTED ON `FS.GG.Governance`, 2026-07-28 — the first retirement B5's clearance MADE
> POSSIBLE rather than merely failed to block** ([#1748](https://github.com/FS-GG/.github/issues/1748),
> [FS.GG.Governance#337](https://github.com/FS-GG/FS.GG.Governance/pull/337)). Governance is one of the
> three receivers that wired a `skill-union` caller, so §5.1's decision is the only reason it was
> dispatchable. Verified on the receiver, not inherited from `#1715`: `.github/workflows/skill-union.yml`
> is gone (`37c12d1`), and `main`'s required contexts are `Deterministic gate (locked restore + build)`,
> `Full test suite (dotnet fsi build.fsx test)`, `Full test suite — Release`, `Build-config drift check
> (shared-build-config)`, `Reference gate set — pack guard`, `contract-coherence / coherence`,
> `kit / coordination-kit` and **`skill-view-check`** — eight, with no union gate among them.
>
> §4's four preconditions, re-run on `FS.GG.Governance@37c12d1` that day:
>
> | precondition | measured |
> |---|---|
> | 1 — `scripts/skill-view` present and executable in R | **yes**, 21,851 bytes, mode 0755, with `lib/args.sh` + `lib/roots.sh` beside it; it ran and produced the view |
> | 2 — parity AGREE on R's tree, and again on R's resolved equivalent | **AGREE / AGREE.** Committed tree: `old=ok new=ok` over **15 ids in 2 roots**. Resolved half-view: same population, byte identity *"STRUCTURALLY IMPOSSIBLE to violate here — every configured root resolves to the same object … Checked, not assumed."* |
> | 3 — half-view, not all-view | **half-view** (`.claude/skills` tracked source, `.agents/skills` generated), so `#1685` is not engaged |
> | 4 — second root not left committed | satisfied by the retirement commit itself — `skill-view` refused to generate while the root was tracked, measured, so the deletions and the generate are necessarily one change |
>
> Kit pin **0.15.0** (`Directory.Packages.local.props`, via PR #333 → `2d37fa1`).
>
> **`diff -r .claude/skills .agents/skills` was SILENT**, and hardened past the diff: 15 skills and
> **34 tracked files** per root, identical path sets, identical git modes, zero symlinks, and nothing
> untracked or ignored inside either. Four receivers have now had this check decide their cost and one
> (`FS.GG.SDD`) has had it stop the retirement; it has never once been redundant.
>
> **THE ONE THING THIS RECEIVER MEASURED THAT THE FIRST THREE COULD NOT, and it retro-justifies stage 1's
> finding 1.** Stage 1 concluded the generate belongs in the receiver project rather than a workflow
> step, on the argument that a workflow step covers only the trees that run that workflow. On Governance
> that stops being an argument: `-t:FsggKitMaterialize` runs in **two** trees, and one of them is
> **`kit-materialize.yml`, a `uses:` of this repo's reusable workflow, which a caller cannot add a step
> to at all**. Measured on a bare checkout of the retired tree: *"view skill root '.agents/skills' is
> ABSENT or a DANGLING link"*, build FAILED — so a workflow-step generate would have left Governance's
> next Renovate kit bump red on a tree nobody touched, **with no file the receiver owns in which to fix
> it**. That is B5's own shape (a reusable workflow whose subject is a bare checkout) recurring on a
> different gate, and the receiver-project target is what makes it a non-event.
> `FsggGovernanceGenerateSkillView` (`BeforeTargets="FsggKitCheckSkillView"`, `Condition` on the view
> property) covers both trees; the required `Build-config drift check` went green on it in 10s.
> **Every remaining `build-config` receiver owes the same, and for this reason rather than stage 1's.**
>
> **The §8 hole, re-measured here.** With `FsggKitViewSkillRoots` emptied and the root deleted: the
> materialize reports *"no view skill roots declared … nothing to assert"* and succeeds, and
> `coordination-sync --check --against-pin` reports *"OK — all 30 materialized file(s)"* — the
> **required** `kit / coordination-kit`, green on exactly the tree the alarm exists to fail. Governance's
> alarm is `scripts/check-skill-view-roots.sh` on its required `skill-view-check` context: §5.1's
> replacement gate given a second leg, which is the cheapest correct home this repo had (no
> `composition` harness; no other required job that is both skill-shaped and free of a .NET restore).
> It is **FS.GG.Audio's shape, ported rather than re-derived**, extended in one direction — an absent
> declared root is a RED here rather than "expected on a bare clone", because this host job generates
> the view immediately before asserting it, which is what makes the alarm itself fire on both mutations
> instead of only the declaration one.
>
> **Mutation-proven in CI, by run id, and the green was read in the job log rather than inferred:**
>
> | tree | run | verdict |
> |---|---|---|
> | unmutated retirement branch | [30343002231](https://github.com/FS-GG/FS.GG.Governance/actions/runs/30343002231) | `success` — alarm `4 passed, 0 failed`, both legs present in the log |
> | `<FsggKitViewSkillRoots>` emptied, view root left in place | [30343106728](https://github.com/FS-GG/FS.GG.Governance/actions/runs/30343106728) | **failure** — *"cannot read the runtime root set … both properties must be declared"*, `1 passed, 2 failed` |
> | the generate dropped, so the view root is absent | [30343108765](https://github.com/FS-GG/FS.GG.Governance/actions/runs/30343108765) | **failure** — *"declared runtime root '.agents/skills' does not exist"*, `3 passed, 1 failed` |
>
> Note the middle row: on that tree `skill-view check` **alone is green**, which is precisely why the
> alarm is a separate leg and not a comment. Both proof branches were deleted unmerged — nothing was
> retired to obtain this evidence. **This is the third hand-copy of the alarm and the second of the
> generate target**; `#1710` still owns collapsing them, and three receivers have now paid.
>
> **A finding this stage produced that is NOT about the retirement, and was not fixed here.**
> Governance's own `scripts/materialize-skill-roots.sh` hardcodes ADR-0011's **three** roots as its
> write set (`:137`) and subtracts neither `FsggKitRetiredSkillRoots` nor `FsggKitViewSkillRoots`. So
> `--check` **fails on an untouched `main`** — 23 drift paths under the retired `.codex/skills` — and
> the remedy it prints re-creates the four kit skill directories there, which ADR-0065 §Retiring a root
> forbids and `#1636`'s sweep undid. Nothing noticed because **no workflow runs it**. After the
> retirement it also plans writes into the view root, i.e. through the symlink into the tracked source
> the script's own header says it never writes. That is `FS.GG.SDD#767` and `FS.GG.SDD#770` recurring in
> a third repo, in one file. Filed as
> [`FS.GG.Governance#338`](https://github.com/FS-GG/FS.GG.Governance/issues/338). **It is also the
> answer to "is this repo's own checker a safety net for the retirement?" — it is not**, and a worker on
> a later receiver should not treat a repo-local materializer as one.

> **ATTEMPTS 5, 7 AND 8 — `FS.GG.Game`, `FS.GG.Rendering` and `FS.GG.SDD`, 2026-07-28. Written from
> the retiring workers' own measurements, which they queued rather than contended for.** These three
> rows were owed to this document by `#1754` and `#1756`. Each retiring worker deliberately did not
> declare `docs/coordination` — `shrike-44a4` (Game) queued theirs as a comment on `#1723`,
> `#1747`'s worker filed `#1756`, and `merlin-efdc` (SDD) queued theirs on `#1760`. **Three workers
> chose the queue over the contention and the record went stale as a result; that trade is `#1732`'s
> subject and the alternative was serialising four retirements behind a prose file.** What is below
> is transcribed from those three reports and their squashes; where this worker re-derived a number
> it says so, and where it could not it says that instead.
>
> §4's four preconditions, re-run per receiver on that receiver's own pre-retirement tree:
>
> | precondition | `FS.GG.Game@acef7d0` | `FS.GG.Rendering@44981d8` | `FS.GG.SDD@f0e3d97` |
> |---|---|---|---|
> | 1 — `scripts/skill-view` present and executable | **yes**, **23,671** bytes, mode 0755, `lib/args.sh` + `lib/roots.sh` beside it; it ran | **yes**, 21,851 bytes, mode 0755, both libraries; it ran and produced the view | **yes**; it ran and produced the view |
> | 2 — parity AGREE on the tree and on its resolved equivalent | **AGREE / AGREE** over **21 ids in 2 roots**, `byte-differing=0`; re-run a **third** time on merged `main` at `b1d4fbd` — AGREE | **AGREE / AGREE** over **50 ids in 2 roots**, `byte-differing=0` | **AGREE / AGREE** over **32 ids in 2 roots**, `byte-differing=0`; re-run a **third** time on merged `main` at `730a214` — AGREE. Stage 2's population is **confirmed**, not inherited |
> | 3 — half-view, not all-view | **half-view**, `#1685` unengaged | **half-view**, `#1685` unengaged | **half-view**, `#1685` unengaged |
> | 4 — second root not left committed | satisfied by the retirement commit; `skill-view` refused to generate while it was tracked — **exit 2**, measured | satisfied by the retirement commit | satisfied by the retirement commit |
> | kit pin | **0.15.1**, `Directory.Packages.local.props:36` | **0.15.0**, `Directory.Packages.local.props` | **0.15.0** |
> | B5 reach — does R wire a `skill-union` caller? | **no**, `grep -rl skill-union .github/` empty, re-verified that day | **was one of the three**; `skill-union.yml` gone at `44981d8`, required contexts hold `skill-view-check` and no union gate | **was one of the three**; caller retired at `83b1f75`, `skill-view-check` required at `37f2b85`, landed producer-first |
> | `diff -r <source> <second-root>` | **silent** | **silent**, exit 0, verbatim: no output | **`Only in .claude/skills: skill-manifest.json`** — see below |
>
> **`FS.GG.SDD`'s `diff -r` is the model of a CORRECT asymmetry, and it is worth more than a silent
> one.** The output is not empty, and that is fine, because the direction that decides a retirement is
> `Only in .agents/skills:` — **that half is empty**. `FS.GG.SDD#771` deliberately moved the
> producer-authoritative manifest into the **surviving** root, which is precisely where stage 2's
> finding 2 said it had to go. A worker who reads "the diff must be silent" as the rule will stop on
> this tree for the wrong reason. **The rule is directional: nothing may live only in the root being
> retired.**
>
> SDD's worker hardened past `diff -r` in a way that supersedes it and should be the standard from
> here: **51 tracked paths per root, identical relative path sets, identical git modes, and identical
> BLOB IDS** — `git ls-files -s` with the root prefix stripped diffs to nothing. That compares the
> *index*, not the worktree, and it is a strictly stronger statement than `diff -r`.
>
> **`FS.GG.Game`'s three numbers that this document would otherwise have propagated wrong**
> (`shrike-44a4`, and all three are re-derivable):
>
> 1. **`scripts/skill-view` is 23,671 bytes on a 0.15.1 receiver, not the 21,851 recorded for
>    Templates, SDD and Audio.** `#1718` rewrote the file and 0.15.1 carries it. Every
>    precondition-1 row in this document reads as if that size were a constant. **It is a
>    per-kit-version fact**, and any digest carried forward from a 0.15.0 stage is stale — the 0.15.1
>    `skill-view` digest is `c0bfe2dc8a4e…`, published 07:30:03Z.
> 2. `#1734` said Game's roots held 20 skills. They held **21** — the issue's enumeration omitted the
>    kit's fourth skill, `pnext-item`. 17 own + 4 kit = 21, **41** tracked files per root.
> 3. Game's report calls its pin location *"a FOURTH pin location"*. **It is not, and this correction
>    matters because `#1725` is about exactly this class of claim.** Game pins in
>    `Directory.Packages.local.props`, which is **SDD's location**. There are **three** distinct
>    locations across the seven receivers — inline (Templates), `Directory.Packages.props` (Net,
>    Audio), `Directory.Packages.local.props` (SDD, Game, Governance, Rendering) — and Game is a
>    fourth *data point*, not a fourth location. `#1725`'s table lists Governance, Game and Rendering
>    as unmeasured; all three are now measured and all three are the third location.
>
> **What `FS.GG.Rendering` measured that no earlier receiver could, and it is the most important thing
> in this block: `diff -r` is NECESSARY but NOT SUFFICIENT.** Rendering's `diff -r` was silent, exit
> 0, 50 ids and 70 files byte-identical across both roots — and the retirement still shipped a broken
> artifact past a green gate. `.template.config/template.json` vendored the **repo-root**
> `.agents/skills/` into the `dotnet new` template (`include: ["speckit-*/**"]`,
> `lifecycle == "spec-kit"`), so the retired root is an input to every job that installs, scaffolds,
> audits, **packs** or publishes that template. Measured on a bare clone of the retirement commit:
>
> * `dotnet new install .` → `[Error][MV012] Source '.agents/skills/' in template does not exist`, and
>   the template does not load at all;
> * **`dotnet pack` does NOT fail.** It shipped **1843 entries instead of 1914, none under
>   `content/.agents/skills/`**. A green publish could have produced a broken artifact.
>
> **`diff -r` compares two directories' CONTENTS. It can never see a third file that NAMES one of
> them.** Both roots agreed byte for byte; the dependency was on the **path**. CI caught this, not the
> worker. The predictor this demands is in §6, sharpened by a second data point on SDD. Owned by
> [`FS.GG.Rendering#1126`](https://github.com/FS-GG/FS.GG.Rendering/issues/1126).
>
> **`FS.GG.SDD` is the second data point, and it fails the OTHER way — which is what makes the
> predictor actionable.** SDD holds 16 × `<EmbeddedResource Include="../../.claude/skills/…/SKILL.md" />`
> in `src/FS.GG.SDD.Commands/FS.GG.SDD.Commands.fsproj`. Same class of reference, opposite behaviour:
> repointing them at `.agents/skills` on a bare retired checkout dies **loudly**, `FSC error FS0078:
> Unable to find the file`. SDD also searched and found a **stated negative** — no `.template.config`
> outside `tests/fixtures/`, nothing naming `.agents/**` as a build or packaging input, and
> `dotnet pack` producing the same 5 packages and 73 entries before and after, 0 under `.agents` in
> either.
>
> **`FS.GG.SDD#770` moved from MASKED to REACHABLE, and §7 now has to say so.** SDD's
> `materialize-skill-roots.fsx` still keeps the view root in its write set; `--mode link` used to
> collapse that to `changed: 0`. On a **bare checkout with no view** — the normal post-retirement
> state — `--check` is exit 1 with **51 DRIFT**, and **write mode exits 0 and creates a REAL
> `.agents/skills` of 51 files with `git status --porcelain` at 0 lines**. `skill-view generate` then
> **refuses** it (*"exists, is not a symlink, and carries no `.skill-view` receipt"*, exit 2),
> reddening two required contexts, with `rm -rf .agents/skills` the only repair and **nothing printing
> it**. No workflow runs the driver in write mode, so nothing is red today. It is not fixed and it is
> not this document's to fix — but it is the one case in the whole record where `rm -rf
> <second-root>` is the answer to something, and §7 records it there.
>
> **Green on `main` by run id.** `FS.GG.Game` at `b1d4fbd`: **26 check runs, 26 success, 0 red, 0
> pending**, all 18 required contexts among them — `Build-config drift check (shared-build-config)`
> **90219785231**, `kit / coordination-kit` **90219792211**, `Deterministic gate` **90219785130** /
> **90219785119**. `FS.GG.Rendering` at `63f08ba`: `Deterministic gate` **90228387824**, `API
> compatibility gate` **90228387832**, `kit / coordination-kit` **90228379597**, `skill-view-check`
> **90228379510**, all `success`. `FS.GG.SDD` at `730a214`: `skill-view-check` **90236697312**,
> `kit / coordination-kit` **90236699144**, `Shared-build-config drift check` **90236701029**,
> `API compatibility gate` **90236700980**, `Deterministic gate` **90236700974**, all `success`. In
> each case the worker reports having read the job log rather than the tick, and quotes the new legs
> firing.
>
> **`.codex/skills` was not touched on any of the three**, again: Game **17** before and after,
> Rendering **46**, SDD **28**. ADR-0065 §Retiring a root forbids hand-deleting them and none of these
> attempts did.

> **THE REMAINING GATE-SHAPED QUESTION IS OPEN, AND THIS DOCUMENT DOES NOT ANSWER IT.**
> [`#1759`](https://github.com/FS-GG/.github/issues/1759) asks whether `.github/workflows/kit-materialize.yml`
> is B5's shape on a second gate — a `uses:` of the hub workflow running the materialize over a bare
> checkout, which no caller can add a generate step to. If it holds, a retirement is not complete when
> the second root stops being committed, and every retired receiver has a latent red.
>
> **What this worker verified first-hand, 2026-07-28 09:55Z:** all seven receivers declare
> `<FsggKitSkillRoots>.claude/skills</…>` and `<FsggKitViewSkillRoots>.agents/skills</…>`, git-ignore
> the view root, **and carry a receiver-project target `Fsgg<Repo>GenerateSkillView` with
> `BeforeTargets="FsggKitCheckSkillView"`** — read out of each repo's
> `.config/kit/FS.GG.Kit.receiver.proj` on `main`. That target is the *mechanism* that would make
> `kit-materialize.yml` a non-event, because it runs inside the materialize and therefore inside the
> callee's own checkout. **Its presence on 7 of 7 is measured. Whether the build is green is not** —
> this worker did not run it.
>
> **`#1759`'s holder (`tern-f6ba`) reports it REFUTED**, on a bare shallow clone of all seven running
> `-t:FsggKitMaterialize`: seven `Build succeeded`, seven `skill-view check: OK`, `git status` empty on
> all seven, and — the part that makes it not vacuous — **mutation-proven** by deleting that generate
> target from a copy of Governance's tree, which reds with `#1748`'s exact text. On that reading the
> affected set is **0 of 7** and `#1748`'s finding was a real failure mode on an intermediate tree,
> before `FsggGovernanceGenerateSkillView` landed in the same PR.
>
> **`#1759` is nevertheless recorded here as OPEN and UNRESOLVED.** It is open on the board, the
> refutation is that worker's to land on their own item, and this worker ran none of those builds.
> Phase 4's receiver sequence is complete; **"complete" is not "sound"**, and this document does not
> get to convert somebody else's unlanded measurement into its own verdict. If `#1759` closes as
> refuted this sentence is still true; if it reopens, nothing here has to be repaired.

### The per-receiver sequence 0.15.0 makes available (stage 1, per repo)

Measured end-to-end by `src/FS.GG.Kit/verify-package.sh` §3f, on a receiver tree holding a stale
materialized `.agents/skills` and one skill of its own:

1. In R's `.config/kit/FS.GG.Kit.receiver.proj`: drop the view root from `FsggKitSkillRoots` and name it
   in `FsggKitViewSkillRoots`. The **union is unchanged**, so R's runtime root set is unchanged.
2. `dotnet build .config/kit/FS.GG.Kit.receiver.proj -t:FsggKitMaterialize`. The materializer removes the
   kit's own skill directories from the view root and then the now-empty root, and **fails loudly**
   because no view has been generated there yet. That failure is the §8 assertion doing its job, not a
   defect — a root nothing materializes into is a root nothing else can vouch for. Any skill R itself put
   under that root survives and must be moved under the live source before the root can become a view.
3. Commit the deletions (this is the retirement of the *copy*) plus the `.gitignore` line.
4. `scripts/skill-view generate --source <live root>` — the tool the same package delivered. It is no
   longer refused, because the root is no longer tracked.
5. Re-run the materialize: green, with `all N kit skill file(s) visible` at the view root. Wire
   `-t:FsggKitCheckSkillView` into R's `gate.yml` so the assertion runs on every PR and not only on
   kit-bump PRs.
6. **In the SAME commit as step 3, give R's `skill-view-check.yml` its generate step.** This is a
   required context and it is the step that is easiest to leave until "after", which is a RED window on
   every pull request rather than a tidy-up:

   ```yaml
   - name: Resolve the view roots this tree no longer commits
     run: bash scripts/skill-view generate --source .claude/skills --roots ".agents/skills"
   ```

   Why it cannot go earlier and cannot go later. **Earlier**: `scripts/skill-view` refuses to generate
   over a root git TRACKS, with no override (`skill-view:331`, ADR-0067 §9), so on a pre-retirement tree
   the step dies exit 2 — the gate would be red before the retirement instead of after it. **Later**: a
   view root does not exist in a fresh checkout, so the check reports `[absent-root]` and the required
   context is red on every PR until the follow-up lands. Measured on a bare checkout of a dry-run
   retirement of `FS.GG.SDD`: without the step, exit 1
   ([30339144429](https://github.com/FS-GG/FS.GG.SDD/actions/runs/30339144429)); with it,
   `success` ([30339084890](https://github.com/FS-GG/FS.GG.SDD/actions/runs/30339084890)). The window
   between the two commits is the whole cost, so there must not be two commits.

   `--roots` names the VIEW roots **explicitly**, exactly as `FsggKitViewSkillRoots` does for the
   materializer, and for the same reason: the default pair would also try to resolve R's tracked live
   root and be a refusal rather than a no-op.

   **Do not make the step conditional on the root being missing.** "Generate whatever is absent, then
   assert it is present" makes `[absent-root]` unreachable and hands back a gate that cannot fail —
   which is the exact defect (§5.1) that retired the gate this one replaced. The generate is authorised
   by R **declaring** a root to be a view, never by observing that one is missing.
7. Re-run §7's rollback transcript against R before the next receiver is touched (`#1676` AC 3).

---

## 5. The blockers, in the order they must clear

| id | blocker | state |
|---|---|---|
| **B1** | The kit does not deliver `scripts/skill-view` (or the absence-check wiring) to receivers. Precondition 1 fails on all seven. | **CLEARED in `FS.GG.Kit` 0.15.0** ([#1696](https://github.com/FS-GG/.github/issues/1696)). `scripts/skill-view` is a `kit:` row, as are the two libraries it sources at startup — `repos.sh validate` now refuses a kit that separates them, because a receiver holding the tool without them would read as satisfying precondition 1 while the tool exits non-zero on every run. **Re-read precondition 1 as written**: "present *and executable*" means it runs, and the kit's own gate proves that by running the materialized copy from a receiver tree. |
| **B2** | The materializer has exactly two states for a root: `FsggKitSkillRoots` (materialize into it) and `FsggKitRetiredSkillRoots` (delete the kit's directories from it) — `src/FS.GG.Kit/build/FS.GG.Kit.props:19,25`. There is **no third state** for *"still a declared runtime root, but generated locally rather than materialized"*, which is exactly what a view root is. Without it a receiver's second root stays committed, and `scripts/skill-view:331` then refuses to generate the view there. ADR-0065 §Retiring a root forbids the receiver hand-deleting it — *"A receiver never hand-deletes a mirror; the materializer that created it is the thing that removes it."* | **CLEARED in `FS.GG.Kit` 0.15.0** ([#1696](https://github.com/FS-GG/.github/issues/1696)). `FsggKitViewSkillRoots` is the third state — a root that stays in the runtime contract, is not materialized into, has its previously-materialized kit directories swept by the materializer, and is then **asserted visible** (`FsggKitCheckSkillView`, ADR-0067 §8). ADR-0065 §A root's three dispositions records the contract. **Empty by default**, so it retires nothing until a receiver's own stage-1 change sets it. |
| **B3** | [`#1693`](https://github.com/FS-GG/.github/issues/1693) — `#1587`'s diff-shape guard refuses the 0.14.0 bump on all seven receivers, so no kit change reaches any receiver today regardless. 0 of 7 are current (SDD 0.10.0; five at 0.8.0; Audio 0.6.0). | **CLOSED 2026-07-28 01:33:08Z, and its premise was false** — the diff-shape guard did not exist, so nothing was ever mechanically refusing the bumps (the same correction §4's stage-1 block records). The counts in this cell are the 2026-07-27 reading and are **superseded**: `Templates`, `SDD`, `Audio` and `Net` all pin **0.15.0** as of 2026-07-28. Each receiver's bump still has to LAND — nothing automerges them — which is a scheduling fact, not a blocker. |
| **B4** | `scripts/repos-audit.sh:1841` requires a receiver's gate to be armed on a change to a **committed** skill root. A generated root cannot be armed that way. Repairing this is **sanctioned** — ADR-0067 says the apparatus *"keeps running unchanged, and keeps being repaired"* until §9's order reaches it — but it must precede the first receiver retirement, and it **must not be confused with retiring the sweep**, which is last. | **NOT a blocker for a receiver that never wired the caller — measured 2026-07-28 on the first retirement.** The arming check at `repos-audit.sh:1909` is reached only when a repo BOTH declares `skill-union` and calls the workflow (`declared=1 && calls_it=1`). Zero receivers call it (`registry/repos.yml`'s `skill-union` row; Templates#313 recorded the decision not to), so every receiver lands on the **GAP** branch instead — before the retirement and after it, identically. `FS.GG.Templates` was a `skill-union` gap on 2026-07-27 and is a `skill-union` gap now, for the same reason and with no new red. **Still open, and still precedes the first receiver that DOES wire a caller** — which is a different, later event than "the first receiver retirement", and this row conflated the two. |
| **B5** | **A receiver that DOES wire a `skill-union` caller cannot be retired at all**, and the caller-arming question B4 asks is the smaller half of it. `skill-union-assert.yml` is a reusable workflow: it checks the caller out and asserts over that checkout, so a root that only exists after a `generate` is **absent** in its subject. Measured on `FS.GG.SDD@387adc6`'s dry-run retirement: `configured root is absent: ./.agents/skills`, **exit 2**, on a context that is **required** under `enforce_admins`. The workflow's own header refuses the obvious lift — *"Adding an artifact input to lift that limit is a decision, not a tidy-up — do not infer it from this note."* | **CLEARED 2026-07-28 by [#1715](https://github.com/FS-GG/.github/issues/1715), shape (b) — the caller is retired in all THREE receivers that wired one.** Shape (a) (an opt-in input teaching the reusable workflow to generate the caller's view) was **declined**: it widens a contract five other repos read in order to preserve a gate whose subject the rewrite is deliberately removing, and it makes a gate produce its own subject. See §5.1 below for the decision, the measurement that forced it, the replacement, and the run ids that prove the replacement can fail. |

B1 and B2 are both kit-content changes, so they land together, in one republish, and then ride B3.

> **B4's premise — *"zero receivers call it"* — is FALSE as of 2026-07-28, and B5 above is what it
> becomes.** `registry/repos.yml:104` rosters `sdd` with `receives: … skill-union`, and
> `FS.GG.SDD/.github/workflows/skill-union.yml` is a live `uses:` of
> `FS-GG/.github/.github/workflows/skill-union-assert.yml@main` aimed at the repository root with the
> default roots — landed in `a066e0b` (FS.GG.SDD#718), reporting the **required** context
> `skill-union / skill-union` (run **30328649411**, `success` at `387adc6`). So SDD lands on
> `repos-audit`'s `declared=1 && calls_it=1` branch, not the GAP branch, and it is the *"first receiver
> that DOES wire a caller"* B4 names. B4 is not red on SDD today — its `pull_request:` trigger is
> unfiltered, which `repos-audit` reads as armed — but the event B4 was waiting for **has happened**.
> The roster's own `skill-union` capability prose still says *"No framework repo has wired the receiver
> caller yet (measured 2026-07-27: zero `uses:` of skill-union-assert.yml in any of the seven)"*
> (`registry/repos.yml:268`). **That sentence was true when it was written and false 87 minutes
> later**: it landed at 2026-07-27 01:07 (`#1505`) and SDD's caller landed at 2026-07-27 02:34
> (`a066e0b`). It is filed as [#1716](https://github.com/FS-GG/.github/issues/1716) rather than
> corrected here, because this document is not the roster.

---

## 5.1 B5's decision, and the gate that replaces the caller

**Decided 2026-07-28 on [#1715](https://github.com/FS-GG/.github/issues/1715), with the repository
owner's authorization: shape (b). The `skill-union` receiver caller is RETIRED, and each receiver's
required skill context is now its own `skill-view check --source <live root>`.** This section is where
the next receiver's worker reads it, so it does not have to be relitigated per repo.

### The measurement that decided it

A gate that cannot fail is worse than no gate, and on the half-view layout §2 retires INTO, this one
cannot. Measured on **each of the three receivers' own trees**, at a dry-run retirement:

| repo | `skill-union-assert --roots ".agents/skills"` (the view alone) | `--roots ".claude/skills .agents/skills"` |
|---|---|---|
| `FS.GG.SDD` | exit 2, *"no skills found under any root"* | exit 0, `in-every-root=32/32 byte-identical=32/32` |
| `FS.GG.Rendering` | exit 2, same | exit 0, `in-every-root=50/50 byte-identical=50/50` |
| `FS.GG.Governance` | exit 2, same | exit 0, `in-every-root=15/15 byte-identical=15/15` |

**Read the two columns together.** `union_ids()` enumerates with `find` and no `-L`, so a view root
contributes **zero** ids — that is the left column, on its own, and it is a fact about the tool rather
than an inference. Every id in the right column therefore came from the **tracked** root alone, and the
view root satisfied the presence test only because presence is `[ -d ]` **through** the symlink, which
cannot fail. Both of the gate's headline invariants are tautologies on this layout, and
`byte-identical=N/N` is N comparisons of a file with itself. It would have kept reporting `success` on
a **required** context under `enforce_admins` while asserting nothing — epic `#266` in its most
expensive form, because that green is indistinguishable from a green that means something.

Note which way the failure runs: the gate is **fatal about the checkout** (`configured root is absent`,
exit 2, on a bare checkout) and **vacuous about the content** (exit 0, asserting nothing, once the view
exists). Neither half is a gate. Shape (a) would have repaired only the first.

### Why (a) was declined

1. It widens a `workflow_call` contract five other repos read, permanently, to preserve a gate whose
   subject this rewrite is deliberately removing. That is a permanent cost for a transitional shape.
2. A gate that produces its own subject must make the generate a separately visible step whose failure
   is distinguishable from a green assert (`#1710` criterion 2). Achievable — but it is new machinery
   guarding new machinery, and `skill-view check` already asserts §8's absence classes and **can
   genuinely fail**.

Shape (b) is also what §9 directs on a plain reading. It was not chosen to make the retirement
convenient; the retirement is what exposed that the gate had stopped meaning anything.

### The replacement, and the proof it can fail

Each receiver runs its own `.github/workflows/skill-view-check.yml` — no `uses:`, its **own** pinned
`scripts/skill-view` (a receiver's verdict must be a function of the receiver, `#1584`), an unfiltered
`pull_request` trigger, and the required context `skill-view-check`:

```yaml
- run: bash scripts/skill-view check --source .claude/skills --tree .
```

**Mutation-checked in CI on `FS.GG.SDD`'s own tree, by run id, before the requirement moved** — this is
`#1715`'s AC2/AC3 and the whole justification for the decision:

| what | run | verdict |
|---|---|---|
| `success` on a **bare checkout of a RETIRED tree** (`.agents/skills` untracked + git-ignored, view generated at checkout) | [30339084890](https://github.com/FS-GG/FS.GG.SDD/actions/runs/30339084890) | `success` — *"OK — 32 declared skill(s) visible in every one of 2 root(s) (64 path(s) examined)"* |
| the **generated view removed** | [30339144429](https://github.com/FS-GG/FS.GG.SDD/actions/runs/30339144429) | **failure** — `[absent-root] .agents/skills does not exist` |
| the **source root emptied** | [30339430795](https://github.com/FS-GG/FS.GG.SDD/actions/runs/30339430795) | **failure**, exit 2 — *"the expected skill set is EMPTY — refusing to report 'everything is visible' over nothing"* |

Both proof pull requests were **closed unmerged** and their branches deleted: nothing was retired to
obtain this evidence (`#1715` AC5).

### What the swap does NOT keep, said out loud

`skill-view check --source X` asserts that everything X declares is **visible** at every root, with §8's
absence classes named separately. It does **not** compare the two roots' bytes against each other. So
for as long as a receiver still commits **two independent copies**, the swap is weaker than the union
gate in exactly one direction: a skill present in `.agents/skills` and absent from `.claude/skills` is
outside the new gate's subject. That direction stops existing the moment the second root becomes a view
— a view cannot diverge from what it is a view of — so the gap is bounded by that receiver's own stage-1
retirement, and it is a reason to sequence stage 1 promptly rather than a reason to keep a tautology.
`kit / coordination-kit` keeps grading the kit-owned skills' bytes throughout, which is evidence about
those skills and never about the tree (`#1504`).

### The order the swap was performed in, and why it is not negotiable

`#1715` AC4. A required context that never posts wedges **every** pull request at *"Expected — waiting
for status to be reported"*, and the org has paid for that twice, both times in `FS.GG.SDD` (`#370`,
`#525`). Per repo:

1. land `skill-view-check.yml` on `main`, and **observe it report `success` on a real pull request** —
   `FS.GG.SDD` [30338722448](https://github.com/FS-GG/FS.GG.SDD/actions/runs/30338722448),
   `FS.GG.Rendering` [30338748566](https://github.com/FS-GG/FS.GG.Rendering/actions/runs/30338748566),
   `FS.GG.Governance` [30338750948](https://github.com/FS-GG/FS.GG.Governance/actions/runs/30338750948);
2. `scripts/repos.sh require-context --context skill-view-check … --apply` (add-only: it POSTs to the
   contexts collection, which has no delete semantics);
3. `scripts/repos.sh unrequire-context --context "skill-union / skill-union" … --confirm-remove "skill-union / skill-union" --apply`;
4. **only then** delete `.github/workflows/skill-union.yml`.

At no instant did any of the three branches require a context no workflow could report. Steps 3 and 4
are the ones that must never swap: with the caller deleted first, the still-required context has no
producer and the repository stops merging — including the pull request that would repair it. Reading
the branch's required contexts back **immediately before** the delete commit is the cheap check that
catches the mistake, and the retirement change makes it a hard precondition.

> **The credential exists now, and that is not a reason to risk needing it.** The org dispatch App was
> granted `administration: write` on 2026-07-28 (`#1712`), so a wedged branch is repairable where it
> previously was not. `repos.sh`'s own header says the same thing the other way round: needing the
> repair is a symptom, not a plan. Nothing in this order relies on it.

**Nothing about `enforce_admins`, `strict`, reviews or force-push was touched on any repo.** `repos.sh`
cannot reach those fields: it names only `…/protection/required_status_checks` and its `contexts`
sub-resource, never the bare protection endpoint, whose whole-object `PUT` disables by omission.

---

## 6. The order

### The standing stage verdict — 2026-07-29: **§9's sequence has TERMINATED. Stage 1 complete, stage 2 landed, stage 3 declined.**

> **This heading carries the stage, and nothing anywhere else does** — the same rule §4 carries for the
> receiver count, and for the same reason. The stage blocks below are dated, append-only records; when
> the stage changes, change *this* line and append.
>
> | stage | subject | state |
> |---|---|---|
> | 0 | unblock (B1–B3) | **complete** |
> | 1 | per receiver, one at a time | **complete — 7 of 7**, and the count is §4's, not this table's |
> | 2 | fleet-wide narrowing of kit materialization and `coordination-coherence`'s skill paths | **LANDED 2026-07-29** — [#1868](https://github.com/FS-GG/.github/pull/1868), released as `FS.GG.Kit` 0.19.0 ([#1871](https://github.com/FS-GG/.github/pull/1871)) |
> | 3 | the kit-pin freshness sweep — **last** | **REACHED and DECLINED** — [#1864](https://github.com/FS-GG/.github/issues/1864) holds the decision |
>
> **"Terminated" is not "everything retired", and the difference is the whole of stage 3.** §9 ordered
> the sweep last so that the alarm outlived the fire. The order reached it, measured that its subject
> — distribution staleness — **survives this rewrite** (§1), and therefore did **not** retire it. A
> declined last step is a completed order, not an abandoned one; what it is not is authority to retire
> the sweep later without answering `#1864` first.

### Stage 0 — unblock (no retirement)

B1 + B2 in one kit change and one republish; B3; then the seven bumps land. Nothing is retired here.

> **B1 + B2 LANDED 2026-07-28 as `FS.GG.Kit` 0.15.0 ([#1696](https://github.com/FS-GG/.github/issues/1696)),
> and retired nothing** — `FsggKitSkillRoots` and `FsggKitRetiredSkillRoots` hold their 0.14.0 values and
> `FsggKitViewSkillRoots` defaults empty, so a receiver that takes the bump and configures nothing gets
> 0.14.0's two roots plus three new files. **Stage 0 is not complete**: B3 is untouched and is now the
> whole of it. `#1693` is not `#1696`'s to fix and the payload reaches **zero receivers** until it clears.

### Stage 1 — per receiver, one at a time, each proven before the next

**This list is a PREFERENCE and it is now spent — every receiver on it is retired. It carries no
per-receiver status, deliberately: the outcomes live in §4's standing verdict and nowhere else.**
It was written as roster order, cheapest blast radius first, so the rollback path would be exercised on
the smallest tree before the largest:

1. `FS.GG.Net` — no Renovate config (`default.json:12`), no `build-config`; the smallest surface.
2. `FS.GG.Audio`
3. `FS.GG.Templates`
4. `FS.GG.Governance`
5. `FS.GG.Game`
6. `FS.GG.SDD`
7. `FS.GG.Rendering` — largest skill tree (`.claude=50`, measured at `registry/repos.yml:234-236`), last.

**The order that actually happened was Templates, Audio, Net, Game, Governance, Rendering, SDD** — see
§4's standing verdict, which is the only place that sequence is recorded. This list predicted the first
receiver wrong and the last receiver wrong, for the reason the note below already gives.

> **ACTUAL ORDER, 2026-07-28: `FS.GG.Templates` went first, and the list above is now a preference
> rather than a sequence.** §9 forbids retiring where the replacement is unproven, so **eligibility
> outranks blast radius** — and eligibility is a fact about a receiver's pin, which this list cannot
> predict. `FS.GG.Net` is still the smallest surface and it has **no bump PR at all**; ordering the
> queue by size would have meant waiting for the smallest repo while the one repo that *could* be
> proven sat idle. Read the list as "when two receivers are both eligible, take the smaller".
>
> Blast radius was not ignored, it was re-measured: Templates is the only receiver with **no `gate.yml`
> and no `build-config`**, so it runs no second `FsggKitMaterialize` outside `kit-materialize.yml` — the
> four `build-config` receivers (SDD, Rendering, Governance, Game) each run one in their own
> `build-config-drift` job and will need the generate reachable from that tree too.
>
> **Retired 1 of 7 at that point. Remaining: Net, Audio, Governance, Game, SDD, Rendering — all still
> stage 0** (`#1587` owns delivery and is itself `Blocked`).
>
> **STILL 1 of 7 after stage 2 was attempted on `FS.GG.SDD` (2026-07-28).** SDD is the only receiver
> besides Templates carrying 0.15.0 and it passes all four §4 preconditions, and it was **not** retired:
> see §4's stage-2 block and B5. Nothing on SDD was changed; no PR was opened against it.
>
> **Stages 3 and 4 then retired `FS.GG.Audio` and `FS.GG.Net` (2026-07-28), and stage 5
> (`FS.GG.Game`, [`#1734`](https://github.com/FS-GG/.github/issues/1734)) is in flight as this is
> written.** The count is in §4's standing verdict and **only** there; this paragraph deliberately no
> longer carries one, because carrying it in two sections is how it went stale three times in one day.

For each: re-run §4's four preconditions; run **both** of the two cheap predictors below and stop if
either fires; retire the second committed root; **replace §8's alarm on a context that is already
required**, with a can-fire demonstration per lane (§4); confirm the repo's gates green **on `main`, by
run id, not by merge**; close only the board rows whose subject that repo's retirement actually
dissolved.

#### The two cheap predictors — run BOTH, because the first cannot see what the second finds

**1. `diff -r <source> <second-root>` — necessary, and it is DIRECTIONAL.** A non-empty
`Only in <second-root>:` is a file with no home in the view: the retirement deletes it and
`git status` reports **nothing**. It fired exactly once in seven receivers — `FS.GG.SDD`'s
`skill-manifest.json` — and that one receiver is the one that stalled on content. **The other direction
is not a failure.** SDD's post-`#771` tree prints `Only in .claude/skills: skill-manifest.json` and is
correct: the producer-authoritative file belongs in the surviving root. Read the direction, not the
exit code. Stronger still, and free: compare `git ls-files -s` with each root prefix stripped — that
compares blob ids in the *index* rather than bytes in the worktree.

**2. `grep` for path references to the root being retired — and grade them by KIND.** This is the
predictor `diff -r` structurally cannot be:

```
grep -rn '\.agents/skills' <repo> \
  --include='*.json' --include='*.props' --include='*.targets' \
  --include='*.fsproj' --include='*.csproj' --include='*.yml' --include='*.yaml' \
  --exclude-dir=.agents --exclude-dir=.claude
```

> **`diff -r` answers *"does anything live only in the copy being retired?"*. It can never answer
> *"does anything NAME the copy being retired?"* — a path in a template manifest, a packaging glob, a
> test fixture. On `FS.GG.Rendering` both roots agreed byte for byte and the dependency was on the
> **path**.**
>
> **The rule is NOT "does anything mention the root". It is: does anything reference it as a DIRECTORY
> OR A GLOB, rather than as a literal file path?** Two measured data points, and they fail in opposite
> directions:
>
> | receiver | the reference | kind | what happened |
> |---|---|---|---|
> | `FS.GG.Rendering` | `.template.config/template.json` vendoring `.agents/skills/` into the `dotnet new` payload | **directory** | **FAILS OPEN.** `dotnet new install .` dies `[MV012]`, but **`dotnet pack` does not fail** — it silently shipped **1843 entries instead of 1914**, none under `content/.agents/skills/`. A green publish could have produced a broken artifact. |
> | `FS.GG.SDD` | 16 × `<EmbeddedResource Include="../../.claude/skills/…/SKILL.md" />` | **literal file path** | **FAILS CLOSED.** Counterfactual measured: repoint them at `.agents/skills` on a bare retired checkout and the build dies `FSC error FS0078: Unable to find the file`. |
>
> **The dangerous ones are the directory and glob references, because the tooling treats a missing
> directory as an EMPTY SET and ships a smaller artifact rather than an error.** That is what makes this
> a check with a verdict instead of a grep that returns noise. A hit that resolves to a literal file
> path is loud on its own and needs nothing; a hit that names a directory or a glob is the one to chase.
>
> **A stated negative is a result.** SDD ran the same sweep and found no `.template.config` outside
> `tests/fixtures/` and nothing naming `.agents/**` as a build or packaging input, then *proved* it:
> `dotnet pack` gave the same 5 packages and 73 entries before and after, 0 under `.agents` in either.
> Report the negative with the command that produced it.
>
> **CI caught Rendering's, not the worker.** Owned by
> [`FS.GG.Rendering#1126`](https://github.com/FS-GG/FS.GG.Rendering/issues/1126) — whether that payload
> should source the tracked root instead.

#### What actually predicted per-receiver cost, over seven receivers and eight attempts

**Not repo size and not repo shape.** That generalisation was made at attempt 2 from `FS.GG.SDD`
(framework-shaped ⇒ expensive) and falsified at attempt 3 by `FS.GG.Audio` — a `gate.yml` and **16
repo-owned `fs-gg-sdd-*` skills inside the root being retired**, and still cheap, because those 16 sat
in **both** audited roots so the view reproduces them and `diff -r` was silent. `FS.GG.Game` repeated
the falsification at a larger scale: **18 required contexts, 17 repo-owned skills, a `build-config`
receiver**, and the cheapest retirement after Templates, for the same reason. What predicted cost was
**the two checks above, plus (historically) whether R wired a `skill-union` caller** — the latter moot
fleet-wide since `#1715`.

**Do not read the attempt count as the evidence count.** `FS.GG.Templates` and `FS.GG.Net` are the
**same shape** — the same 4 kit skill ids (`check-board`, `cross-repo-coordination`,
`intra-repo-parallel-work`, `pnext-item`), **23** files each, zero repo-owned skills in the audited
roots. Reading the stage count as that many independent confirmations overstates the evidence, and this
document has already had to retract one such overstatement.

**A record that only records what held is not a record.** Attempt 2's cost generalisation is written
down here *because* it was wrong; the previous version of this document would have carried it forward
into Governance, Game and Rendering — each framework-shaped, each priced expensive on an argument Audio
had already falsified.

> **THE PER-REPO CALLER STEP IS GONE — it was *"retire that repo's `skill-union-assert` caller"*, and
> there is no longer any such caller to retire.** [`#1715`](https://github.com/FS-GG/.github/issues/1715)
> closed 2026-07-28 08:04:47Z by retiring the caller **fleet-wide** in the three receivers that had one
> (SDD, Rendering, Governance), so this is no longer per-receiver work and a stage-1 worker should not
> go looking for it. What replaced it — each receiver's own required `skill-view-check` — is §5.1, and
> it is already standing on all three. **If a future receiver is found wiring a `skill-union` caller,
> that is a regression to report, not a step to perform.**

> **THE CALLER STEP IS NOT VACUOUS, AND THIS SENTENCE SAID IT WAS.** It read *"(note: zero receivers
> have wired one — `registry/repos.yml:268-271` — so this step is vacuous today and costs nothing)"*.
> That was true of `FS.GG.Templates`, which recorded the decision not to wire one (Templates#313), and
> it was **already false of `FS.GG.SDD`** when it was written: SDD's caller landed at 2026-07-27 02:34
> (`a066e0b`), a day before this order, and its `skill-union / skill-union` context is **required under
> `enforce_admins`**. It was written by trusting `registry/repos.yml`'s prose instead of the receiver's
> workflows, which is the one thing this order's own §4 says not to do. On SDD this step is the
> single most expensive one in the list and it is what stopped stage 2 — B5. Generalising one
> receiver's cost to the roster is the mistake this order has now made twice (the first was reading
> Templates' empty `.codex/skills` as the receiver-wide shape; SDD's holds **28** repo-owned skills).
> **Measure the step on R before pricing it.**

### Stage 2 — fleet-wide, only after all seven

Narrow (do not delete) kit materialization to one root, and narrow `coordination-coherence`'s skill
paths. Both keep their non-skill subjects.

> **STAGE 2 LANDED 2026-07-29** — [#1868](https://github.com/FS-GG/.github/pull/1868), released as
> `FS.GG.Kit` 0.19.0 ([#1871](https://github.com/FS-GG/.github/pull/1871)). Its precondition was stage
> 1 at 7 of 7, and that was met before it was opened.
>
> | property, in `src/FS.GG.Kit/build/FS.GG.Kit.props` | before | 0.19.0 |
> |---|---|---|
> | `FsggKitSkillRoots` | `.claude/skills;.agents/skills` | `.claude/skills` |
> | `FsggKitViewSkillRoots` | *(empty)* | `.agents/skills` |
> | `DEFAULT_ROOTS`, in `scripts/coordination-sync` | `.claude/skills .agents/skills` | `.claude/skills` |
>
> **It narrowed defaults, not the contract, and it deleted nothing** — the runtime surface is the
> UNION of the materialized and view roots, and that union is still ADR-0011's two. That is what makes
> this the *narrowing* §9 asks for rather than a retirement.
>
> **And it changed no receiver's evaluated answer on the day it landed:** all seven already override
> both properties inline with exactly these values. What the old defaults broke is the receiver that
> overrides **neither** — it materialized real bytes into a root that is supposed to be generated. The
> flip closes that in the package that tells every tool what the roots are, which is the same defect
> `FS.GG.SDD#770` and `FS.GG.Governance#338` each had to close inside one tool.
>
> **AC 5 was NOT satisfied by that change, and this record says so rather than smoothing it.**
> ADR-0065 §A root's three dispositions states those defaults *normatively*, and #1868 flipped them
> without amending it in the same change. The amendment is
> [#1874](https://github.com/FS-GG/.github/pull/1874) and it landed **late** — recorded as late, not
> backdated. #1868 is the counter-example to AC 5's own rule, sitting inside the item that wrote it.

### Stage 3 — LAST

The kit-pin freshness sweep (`scripts/repos-audit.sh:1892`, `#1540`). §9 orders it last because it is the
alarm that would say a retirement went wrong. Phase 4's finding (§1) is that its subject — distribution
staleness — **survives the rewrite**, so on today's evidence stage 3 is reached and then declined.
Retiring it requires a separate decision that says what replaces *"is receiver R's pin current?"*.

> **STAGE 3 REACHED AND DECLINED 2026-07-29. The decision the paragraph above asks for is filed as
> [#1864](https://github.com/FS-GG/.github/issues/1864)** — a `decision` row, currently `Blocked`, and
> it belongs to a human. It is not being routed around and this order does not answer it.
>
> **Reaching the last step and declining it is how this order ENDS.** The sweep is the only member of
> §1's list whose subject the rewrite does not dissolve: resolve-don't-copy removes a repo committing
> the same skill twice, and *"is receiver R's pin current?"* is a distribution question that survives
> it intact. Retiring it on the strength of "the order reached stage 3" would remove a live alarm
> because a sequence ran out — the precise inversion of why §9 put it last.
>
> **Nothing here authorizes retiring it later without `#1864`.** See §8.

### Not in this order

`.github`'s own duplicate roots. `.github` is the **authority, not a receiver** (`registry/repos.yml:103`
— it receives `labels` only), and its two committed roots are the **kit source**, not materialized
output. Retiring them is real resolve-don't-copy work and it is **separable and substantial**: measured
on 2026-07-28, the local gates come up green on the half-view layout (`skill-union-assert` 0,
`generate-driver-manifest --check` 0, `check-paths-coherence` 0, `repos.sh validate` 0, parity AGREE),
but a bare CI checkout has no generated root, so `skill-roots-selfcheck`, `skill-registry-coherence`,
`skill-view`, `skill-view-parity`, `projections`, `skill-quality` and `feed-autofix` each need a
generate step; `scripts/generate-projections`' twelve targets collapse to six of the same file; and
`scripts/check-skill-quality.py:166`'s mirror leg starts comparing a file to **itself** — a vacuous pass
(epic #266) created *by* the retirement. That is its own item, not a rider on this one.

---

## 7. The rollback path

**Stated.** A view retirement is rolled back by `git revert` alone, and this is a property of the
mechanism rather than a procedure to remember: the generated root is **untracked**, so the retirement
commit contains the entire change — the deletion of the committed copy, the `.gitignore` line, and the
generate step. Reverting it restores the committed copy; the stale generated root is then a symlink
sitting where a real directory belongs.

```
git revert --no-edit <retirement-commit>     # restores the committed second root
rm -rf <second-root>                          # PRODUCER-ONLY in practice — see the settled verdict below
scripts/skill-union-assert.sh                 # the old gate, over the restored copied tree
```

> **SETTLED — this line used to read *"the `rm -rf` is required and is not optional tidying"*, and that
> is FALSIFIED for every receiver ever measured.** `git revert` **alone** restores the root correctly on
> all seven: a real directory with the right tracked file count, `find .agents -type l` → **0**, `diff -r`
> identical, the old union gate exit 0, and `git status --porcelain` → **0 changed paths**. Git replaces
> the symlink with the restored directory by itself. The measurements are in the per-receiver transcripts
> below and they are unanimous.
>
> **The one tree that needed it is `.github` itself at `0ea5396` — the producer**, whose second root is
> a kit *source* rather than materialized output (§6 *"Not in this order"*). So it is a property of that
> tree, not of the mechanism.
>
> **The step stays in the stated path anyway, and the reason is asymmetry rather than doubt**: it is a
> no-op when unnecessary, and the failure it prevents is a `git revert` that cannot restore a directory
> — discovered at the exact moment you are rolling back. **Do not delete the step. Do not be surprised
> when it reports nothing to remove. Do not let seven green receivers talk you out of running it on the
> producer.** This document will not carry another *"N receivers against the producer's one"* tally;
> N is 7, the sequence is over, and counting further proves nothing new.
>
> **There is exactly one place `rm -rf <second-root>` is the answer to something, and it is not
> rollback.** On `FS.GG.SDD`, `scripts/materialize-skill-roots.fsx` still keeps the view root in its
> write set ([`FS.GG.SDD#770`](https://github.com/FS-GG/FS.GG.SDD/issues/770), **open**). Run in write
> mode on a bare post-retirement checkout it **exits 0 and creates a real `.agents/skills` of 51 files
> with a clean `git status`**; `skill-view generate` then refuses that directory (*"exists, is not a
> symlink, and carries no `.skill-view` receipt"*, exit 2), reddening two required contexts — and
> `rm -rf .agents/skills` is the only repair, with **nothing printing it**. No workflow runs the driver
> in write mode, so nothing is red today. The retirement did not cause this; it moved it from **masked
> to reachable**, because `--mode link` used to collapse the write set to `changed: 0`.

**Tested**, 2026-07-28, on `.github`'s own tree at `0ea5396` (13 skills, both roots committed):

| step | command | observed |
|---|---|---|
| retire | `git rm -r --cached .agents/skills` + `rm -rf` + `/.agents/skills` in `.gitignore` + `scripts/skill-view generate --source .claude/skills` | `13/13 declared skill(s) visible` in both roots; `git status --porcelain` shows **only** the 56 tracked deletions and the `.gitignore` edit — the generated root is invisible to git |
| prove | `scripts/skill-union-assert.sh` / `scripts/skill-view-parity.sh --tree .` | `0` / `0` (**AGREE**), byte-identity reported STRUCTURALLY IMPOSSIBLE |
| rollback | `rm -rf .agents/skills` + `git checkout -- .gitignore .agents` + `git reset HEAD .agents` | `git status --porcelain` → **0 changed paths**; `.agents/skills` back to 13 committed directories |

The rollback was exercised against the **producer**, because §4 measured that no receiver is eligible to
be the first repo. It must be re-run against the first receiver before the second is touched
(`#1676` AC 3).

**Re-tested against the first RECEIVER, 2026-07-28** — `FS.GG.Templates` at the retirement commit, with
the view generated exactly as a checkout produces it (`.agents/skills -> ../.claude/skills`):

| step | command | observed |
|---|---|---|
| retired state | `dotnet build .config/kit/FS.GG.Kit.receiver.proj -t:FsggKitMaterialize` | `all 23 kit skill file(s) visible` at the view root; `git status --porcelain` → **0 changed paths** |
| rollback | `git revert --no-edit <retirement-commit>` | rc 0; `.agents/skills` back to **23 tracked files in a real directory**, 0 symlinks |
| prove | `skill-union-assert.sh --product .` / `diff -r .claude/skills .agents/skills` | `OK — all roots hold the byte-identical union` (exit 0) / identical |
| after | `git status --porcelain` | **0 changed paths** |

**One correction to the stated path, from this measurement: `rm -rf <second-root>` was NOT required on
this receiver.** Git replaced the symlink with the restored directory by itself. It *was* required on
`.github` at `0ea5396`, so the difference is a property of the tree rather than of the mechanism and the
step stays in the path — it is harmless when unnecessary. Both orderings were run against Templates
(`rm -rf` first, and revert alone) and **both land at 0 changed paths and a green old gate**. Do not
drop the step on the strength of one repo; do not be surprised when it reports nothing to remove.

**Re-run against the SECOND receiver, `FS.GG.SDD`, 2026-07-28** — `#1676` AC 3 requires this before a
second repo is touched, and it was run **even though SDD was then refused**, because the rollback path
is the thing that has to be true *before* a retirement, not after one. Dry run in a throwaway clone of
`387adc6` (32 skills, 52 tracked files under `.agents/skills`):

| step | command | observed |
|---|---|---|
| retire | `git rm -r --cached .agents/skills` + `rm -rf` + `/.agents/skills` in `.gitignore`, one commit | **53 files changed, 3 insertions, 9012 deletions** |
| generate | `scripts/skill-view generate --source .claude/skills --tree .` | `.agents/skills: generated (link)`; `32/32 declared skill(s) visible` in **both** roots, 64 paths examined |
| prove | `skill-union-assert.sh --product . --roots ".claude/skills .agents/skills"` | `OK — all roots hold the byte-identical union`, `in-every-root=32/32 partitioned=0` (exit 0) |
| retired state | `git status --porcelain` | **0 changed paths** — the generated root is invisible to git |
| rollback | `git revert --no-edit <retirement-commit>`, **with no `rm -rf` first** | rc 0; `.agents/skills` back to **52 tracked files in a real directory**, `find .agents -type l` → **0** symlinks |
| prove | `skill-union-assert.sh --product .` / `diff -r .claude/skills .agents/skills` | `OK — all roots hold the byte-identical union` (exit 0) / one difference, see below |
| after | `git status --porcelain` | **0 changed paths** |

**SDD agrees with Templates and not with `.github`: `rm -rf <second-root>` was NOT required.** Git
replaced the symlink with the restored directory by itself. (SDD's run was a dry run in a throwaway
clone; SDD was refused and never retired.)

**One thing this rollback surfaced that Templates' could not.** `diff -r .claude/skills .agents/skills`
on the *restored* tree is not silent: it prints `Only in .agents/skills: skill-manifest.json`. That is
§4's stage-2 finding 2 — a tracked, producer-authoritative file living in the root being retired, which
a view of the other root cannot carry, and which the retirement deletes with `git status` reporting
**nothing**. The rollback restores it correctly; the *retirement* is what has to notice it first. **Run
`diff -r <source> <second-root>` before retiring any receiver** — a non-empty diff in the second-root
direction is a file with no home in the view.

**Re-run against the THIRD and FOURTH receivers, `FS.GG.Audio` and `FS.GG.Net`, 2026-07-28** — and
unlike SDD's, these are rollbacks of **real, merged retirements**, replayed from each squash commit in
a throwaway clone: check out the squash, generate the view exactly as a fresh checkout does, then
revert **with no `rm -rf` first**.

| step | command | `FS.GG.Audio` @ `52a358f` | `FS.GG.Net` @ `602f47a` |
|---|---|---|---|
| retired state | `skill-view generate --source .claude/skills --roots ".agents/skills"` | `.agents/skills: generated (link)`; `20/20 declared skill(s) visible`; `.agents/skills -> ../.claude/skills` | `.agents/skills: generated (link)`; `4/4 declared skill(s) visible` |
| retired state | `git status --porcelain` | **0 changed paths** | **0 changed paths** |
| rollback | `git revert --no-edit <squash>`, **no `rm -rf`** | rc **0** | rc **0** |
| restored | `git ls-files .agents/skills` / `find .agents -type l` | **39** tracked files in a real directory / **0** symlinks | **23** / **0** symlinks |
| prove | `diff -r .claude/skills .agents/skills` | **identical** (rc 0) | **identical** (rc 0) |
| prove | `skill-union-assert.sh --product .` | **exit 0** — `in-every-root=20/20 byte-identical=20/20 byte-differing=0` | **exit 0** — `in-every-root=4/4 byte-identical=4/4 byte-differing=0` |
| after | `git status --porcelain` | **0 changed paths** | **0 changed paths** |

**THE TALLY IS NOW THREE RECEIVERS AGAINST THE PRODUCER'S ONE.** `FS.GG.Templates`, `FS.GG.Audio` and
`FS.GG.Net` — every receiver actually retired — each restore correctly from `git revert` **alone**, and
in each case git replaced the symlink with the restored directory by itself. (`FS.GG.SDD` agrees, on a
dry run of a retirement that never happened, and is not counted in the three.) The single tree that
required `rm -rf` remains `.github` itself at `0ea5396` — the **producer**, whose second root is a kit
source rather than materialized output (§6 *"Not in this order"*).

So the stated path's `rm -rf` line is now, on the measured evidence, **unnecessary on every receiver
tried**. It stays in the transcript regardless, and the reason is asymmetry rather than doubt: the step
is a no-op when unnecessary and the failure it prevents is a `git revert` that cannot restore a
directory, which is exactly the situation in which the rollback is being run and the worst moment to
discover it. **Do not delete the step. Do not be surprised when it reports nothing to remove. And do
not let three green receivers talk you out of running it on the producer.**

**Re-run against `FS.GG.Governance`, 2026-07-28, BEFORE the retirement landed** (`#1676` AC 3). Dry run
in a throwaway clone of `37c12d1` (15 skills, 34 tracked files under `.agents/skills`):

| step | command | observed |
|---|---|---|
| sweep | `dotnet build .config/kit/FS.GG.Kit.receiver.proj -t:FsggKitMaterialize` with the view disposition set | removed the kit's 4 materialized directories from the view root, then **failed loudly** — *"23 of 23 kit skill file(s) are NOT visible there"*. That failure is the §8 assertion working, and it is step 2 of the per-receiver sequence above |
| retire | `git rm -r --cached .agents/skills` + `rm -rf` + `/.agents/skills` in `.gitignore`, one commit | **36 files changed, 13 insertions, 6303 deletions** |
| generate | `scripts/skill-view generate --source .claude/skills --roots ".agents/skills"` | `.agents/skills: generated (link)`; `15/15 declared skill(s) visible` in **both** roots, 30 paths examined |
| retired state | `git status --porcelain` | **0 changed paths** — the generated root is invisible to git |
| rollback | `git revert --no-edit <retirement-commit>`, **with no `rm -rf` first** | rc 0; `.agents/skills` back to **34 tracked files in a real directory** holding 15 skills, `find .agents -type l` → **0** symlinks |
| prove | `skill-union-assert.sh --product .` / `diff -r .claude/skills .agents/skills` | `OK — all roots hold the byte-identical union`, `in-every-root=15/15 byte-identical=15/15` (exit 0) / identical, silent |
| after | `git status --porcelain` | **0 changed paths** |

**`rm -rf <second-root>` was NOT required, and the tally above becomes FOUR receivers against the
producer's one.** Templates, Audio, Net and now Governance — every receiver actually retired at the
time of writing — each restore correctly from `git revert` alone, with git replacing the symlink by
itself (SDD agrees on a dry run and is still not counted). The paragraph above stands unchanged,
including its last sentence: **do not let four green receivers talk you out of running the step on the
producer.**

**Unlike SDD's, this rollback surfaced nothing new** — `diff -r` on the restored tree is silent, because
it was silent before the retirement too. That is the expected outcome and it is worth recording as such:
the diff is a cheap check whose *negative* result is the whole point, and four of five receivers have
now paid nothing for it.

**Re-run against `FS.GG.Game`, `FS.GG.Rendering` and `FS.GG.SDD` — 2026-07-28, the last three, which
complete the roster** (`#1676` AC 3). Each in a throwaway clone, view generated exactly as a fresh
checkout produces it, then `git revert --no-edit <retirement commit>` **with no `rm -rf` first**:

| step | `FS.GG.Game` | `FS.GG.Rendering` | `FS.GG.SDD` |
|---|---|---|---|
| retired state, `git status --porcelain` | **0 changed paths** | **0 changed paths** | **0 changed paths** |
| `git revert`, no `rm -rf` | rc **0** | rc **0** | rc **0** |
| restored | **41** tracked files in a real directory | **70** tracked files in **50** real directories; `test -d .agents/skills -a ! -L` → YES | **51** tracked files in a real directory |
| `find .agents -type l` | **0** | **0** | **0** |
| `diff -r .claude/skills .agents/skills` | identical | identical (rc 0) | back to its **one deliberate line**, `Only in .claude/skills: skill-manifest.json` |
| `skill-union-assert.sh --product .` | exit 0 — `in-every-root=21/21 byte-identical=21/21` | exit 0 — `in-every-root=50/50 byte-identical=50/50` | exit 0 — `in-every-root=32/32` |
| `git status --porcelain` after | **0 changed paths** | **0 changed paths** | **0 changed paths** |
| `rm -rf` needed? | **NO** | **NO** — both orderings run, both land at 0 | **NO** |

**That is the whole roster: seven receivers, seven clean `git revert` rollbacks, zero needing
`rm -rf`.** The verdict is settled at the top of this section rather than restated here as an
eighth tally.

**Rendering's rollback restores something a rollback cannot fix, and this is the sharpest limit in
§7.** `git revert` puts the 70 files back, so the tree is correct — and
`.template.config/template.json` was *already* broken by the retirement in a way `diff -r`,
`skill-union-assert` and `git status` all report as fine (`dotnet pack` shipping 1843 entries instead
of 1914, silently). **A rollback proves the tree can be restored. It proves nothing about whether the
retirement was safe to make.** Those are different questions and §7 only ever answered the first.

---

## 8. What this order does NOT authorize

- Retiring anything on a repo that has not passed §4's four preconditions on its own tree, that day.
- Hand-deleting a mirror on a receiver. ADR-0065 §Retiring a root, unchanged and still governing.
- Amending ADR-0011 Decision 1, ADR-0014 Decision 5, or ADR-0065's root set. Those are amended **in
  direction only** and stay in force until a change actually lands a mechanism retirement — ADR-0067 §9,
  and `#1676` AC 5. **This document amends none of them**, and never did.
  > **The sentence here used to read *"no such change has landed"*, and that stopped being true on
  > 2026-07-29.** Two did. Neither is a mechanism retirement and neither touched ADR-0011 Decision 1 or
  > ADR-0014 Decision 5, so the prohibition above is unchanged — but the *reason* given for it was a
  > fact with a shelf life, and it expired:
  > - **§5's root-set flip** ([#1636](https://github.com/FS-GG/.github/issues/1636)) executed on
  >   2026-07-28; ADR-0065 carries the `EXECUTED` marker and was amended in that same change.
  > - **Stage 2** ([#1868](https://github.com/FS-GG/.github/pull/1868)) narrowed defaults on
  >   2026-07-29; ADR-0065 was amended for it by
  >   [#1874](https://github.com/FS-GG/.github/pull/1874), **late** rather than in the same change.
  >
  > Cite the prohibition, not the expired premise. What still authorizes an amendment is a change that
  > lands a mechanism retirement, and **stage 3 declined to be one** — see §6.
- **Retiring the kit-pin freshness sweep on the strength of this order alone.** The order reached
  stage 3 and **declined** it. [`#1864`](https://github.com/FS-GG/.github/issues/1864) is where that
  decision lives; until it is answered, the sweep keeps running and keeps being repaired.
- Closing a board row whose subject has not dissolved. See §1.
- **Reading "the receiver sequence is complete" as "the receiver sequence is sound."** Completeness is a
  fact about seven trees and it is measured. Soundness is a different claim and this document does not
  make it.

## 9. What is still open after 7 of 7

Recorded here so that "complete" is never read as "finished". None of these is retired, fixed or
answered by this document, and each has an owner:

| row | what it is | state |
|---|---|---|
| [`#1759`](https://github.com/FS-GG/.github/issues/1759) | is `kit-materialize.yml` B5's shape on a second gate? If it were, every retired receiver would have a latent red | **OPEN.** Its holder reports it refuted on all seven, mutation-proven; that measurement is theirs to land. §4 records why this document does not convert it into a verdict |
| [`#1727`](https://github.com/FS-GG/.github/issues/1727) | `FS.GG.Net`'s §8 alarm is not a required context — **and this worker could find no can-fire demonstration for it either** | **OPEN**, now on both counts |
| [`FS.GG.Templates#324`](https://github.com/FS-GG/FS.GG.Templates/issues/324) | Templates' alarm has no view-resolution lane at all; dangling, text-file and partial view roots are unobserved | **OPEN** |
| [`FS.GG.SDD#770`](https://github.com/FS-GG/FS.GG.SDD/issues/770) | the view root is still in `materialize-skill-roots.fsx`'s write set; the retirement moved it from masked to **reachable** (§7) | **OPEN** |
| [`FS.GG.Governance#338`](https://github.com/FS-GG/FS.GG.Governance/issues/338) | the same write-set defect in Governance's own `materialize-skill-roots.sh`; `--check` fails on an untouched `main` and no workflow runs it | **OPEN** |
| [`FS.GG.Rendering#1126`](https://github.com/FS-GG/FS.GG.Rendering/issues/1126) | should the `dotnet new` payload source the tracked root instead of the retired one? | **OPEN** |
| [`#1710`](https://github.com/FS-GG/.github/issues/1710) | collapse seven hand-copied §8 alarms into one kit-shipped assertion | **OPEN**; seven hand-copies now paid |
| [`#1750`](https://github.com/FS-GG/.github/issues/1750) | a checker that compares §4's standing verdict against the receivers' trees | **OPEN**, and deliberately not built here |
| [`#1725`](https://github.com/FS-GG/.github/issues/1725) | the kit pin is not in the same file on every receiver | all seven now measured — **three** distinct locations (§4) |
| [`#1864`](https://github.com/FS-GG/.github/issues/1864) | what replaces *"is receiver R's pin current?"*, without which the freshness sweep cannot retire | **OPEN**, `Blocked`, a `decision` for a human. Stage 3 reached it and declined — §6 |
| [`#1875`](https://github.com/FS-GG/.github/issues/1875) | `#1676` AC 6's bulk closure — every open board row swept against today's tree | **OPEN**, split out of `#1676` because it is a pass over ~103 rows and a different kind of work from a retirement step |
| §6 stage 2 / stage 3 | the fleet-wide narrowing, then the freshness sweep last | **settled — see §6's standing stage verdict, which is the only place that carries it.** Stage 2 landed; stage 3 declined into `#1864` |
