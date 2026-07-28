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
> historical block to carry a new headline, and do not add a second count anywhere. This document went
> stale three times on 2026-07-28 because the count was spread across four prose blocks in two
> sections, and no single edit could bring it current.

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

### The standing verdict — 2026-07-28 08:10Z: **3 of 7 retired**

> **`FS.GG.Templates`, `FS.GG.Audio`, `FS.GG.Net` are retired. `FS.GG.SDD` was attempted and refused.
> `FS.GG.Game` is IN FLIGHT.**
>
> | stage | receiver | outcome | mechanism | second root at the attempt |
> |---|---|---|---|---|
> | 1 | `FS.GG.Templates` | **RETIRED** | [Templates#323](https://github.com/FS-GG/FS.GG.Templates/pull/323), squash `531b01b` | 4 skills, 23 files |
> | 2 | `FS.GG.SDD` | **REFUSED** — B5; no PR was ever opened against it | — | 32 skills + `skill-manifest.json`, 52 files |
> | 3 | `FS.GG.Audio` | **RETIRED** | [Audio#210](https://github.com/FS-GG/FS.GG.Audio/pull/210), squash `52a358f` | 20 skills, 39 files |
> | 4 | `FS.GG.Net` | **RETIRED** | [Net#45](https://github.com/FS-GG/FS.GG.Net/pull/45), squash `602f47a` | 4 skills, 23 files |
> | 5 | `FS.GG.Game` | **IN FLIGHT** — [`#1734`](https://github.com/FS-GG/.github/issues/1734) | — | 21 skills committed |
>
> **Stage 5's outcome is deliberately absent, and this absence is load-bearing.** `#1734` was open and
> held when this block was written. An unobserved outcome written into this record is precisely the
> defect this section keeps acquiring — do not fill the row in from a PR title, an issue body, or the
> fact that earlier stages succeeded. Fill it in from `FS.GG.Game@main`, or leave it.
>
> Still committing a second root, re-read from each repo's `main` on 2026-07-28:
> `FS.GG.Governance` (15 skills), `FS.GG.Game` (21), `FS.GG.SDD` (32 + the manifest),
> `FS.GG.Rendering` (50).

**This heading carries the count, and nothing below it does.** The blocks under it are dated,
append-only records; when the count changes, change *this* line and append a record, never edit a
record to carry a new headline. That rule exists because it was learned the expensive way: this
document has been stale **three separate times on 2026-07-28** — after stage 1 it said 0 of 7, after
stage 3 it said 1 of 7, and `#1723` (which was filed to fix the second of those) was itself
**two stages out of date** by the time a worker reached it, asking for a count of 2 when the truth was
3. A count spread across four prose blocks in two sections cannot be updated atomically, so it is now
in one place. The mechanism that would stop a *fourth* recurrence is filed as
[`#1750`](https://github.com/FS-GG/.github/issues/1750); it is deliberately not built here.

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

### Stage 0 — unblock (no retirement)

B1 + B2 in one kit change and one republish; B3; then the seven bumps land. Nothing is retired here.

> **B1 + B2 LANDED 2026-07-28 as `FS.GG.Kit` 0.15.0 ([#1696](https://github.com/FS-GG/.github/issues/1696)),
> and retired nothing** — `FsggKitSkillRoots` and `FsggKitRetiredSkillRoots` hold their 0.14.0 values and
> `FsggKitViewSkillRoots` defaults empty, so a receiver that takes the bump and configures nothing gets
> 0.14.0's two roots plus three new files. **Stage 0 is not complete**: B3 is untouched and is now the
> whole of it. `#1693` is not `#1696`'s to fix and the payload reaches **zero receivers** until it clears.

### Stage 1 — per receiver, one at a time, each proven before the next

Roster order, cheapest blast radius first, so that the rollback path is exercised on the smallest tree
before the largest:

1. `FS.GG.Net` — no Renovate config (`default.json:12`), no `build-config`; the smallest surface. — **RETIRED (stage 4)**
2. `FS.GG.Audio` — **RETIRED (stage 3)**
3. `FS.GG.Templates` — **RETIRED (stage 1)**
4. `FS.GG.Governance`
5. `FS.GG.Game` — **stage 5 IN FLIGHT** ([`#1734`](https://github.com/FS-GG/.github/issues/1734))
6. `FS.GG.SDD` — attempted at stage 2 and **refused**; B5 has since cleared but findings 2 and 3 stand
7. `FS.GG.Rendering` — largest skill tree (`.claude=50`, measured at `registry/repos.yml:234-236`), last.

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

For each: re-run §4's four preconditions; run `diff -r <source> <second-root>` (§7) and stop if it is
not silent; retire the second committed root; **replace §8's alarm on a context that is already
required** (§4); confirm the repo's gates green **on `main`, by run id, not by merge**; close only
the board rows whose subject that repo's retirement actually dissolved.

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

### Stage 3 — LAST

The kit-pin freshness sweep (`scripts/repos-audit.sh:1892`, `#1540`). §9 orders it last because it is the
alarm that would say a retirement went wrong. Phase 4's finding (§1) is that its subject — distribution
staleness — **survives the rewrite**, so on today's evidence stage 3 is reached and then declined.
Retiring it requires a separate decision that says what replaces *"is receiver R's pin current?"*.

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
rm -rf <second-root>                          # discard the leftover generated view
scripts/skill-union-assert.sh                 # the old gate, over the restored copied tree
```

The `rm -rf` is required and is not optional tidying: `git revert` will not replace a symlink that the
working tree holds where the restored directory goes.

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

---

## 8. What this order does NOT authorize

- Retiring anything on a repo that has not passed §4's four preconditions on its own tree, that day.
- Hand-deleting a mirror on a receiver. ADR-0065 §Retiring a root, unchanged and still governing.
- Amending ADR-0011 Decision 1, ADR-0014 Decision 5, or ADR-0065's root set. Those are amended **in
  direction only** and stay in force until a change actually lands a mechanism retirement — ADR-0067 §9,
  and `#1676` AC 5. **No such change has landed**, so this document amends none of them.
- Closing a board row whose subject has not dissolved. See §1.
