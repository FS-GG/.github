# Issue throughput and recurring error loops — an org-wide audit of 2026-07-12

- **Date:** 2026-07-12 (~16:30 UTC), at commit `544433a` (main)
- **Owner:** `.github` (cross-repo coordination)
- **Status:** Audit report. **No decision is taken here.** §7 lists candidate actions; each would have
  to be argued on its own merits and filed as work.
- **Question:** Across all seven FS-GG repos, is the day's issue activity *real progress*, or are we
  re-fixing the same defects under new numbers?
- **Scope:** every issue opened or closed on 2026-07-12 in `.github`, FS.GG.SDD, FS.GG.Rendering,
  FS.GG.Governance, FS.GG.Templates, FS.GG.Game, FS.GG.Audio — 120 opened, 127 closed — plus the 203
  PRs merged the same day.
- **Method:** issue/PR census via `gh`, then five parallel deep traces (build-config drift; the
  `fsgg-coord` defect family; the skill-mirror treadmill; the doc-vs-pin gate; feeds/distribution).
  Every load-bearing claim below was then **re-verified directly against the live org** — branch
  protection via `gh api`, the nuget.org feed via anonymous HTTP, `registry/repos.lock` and
  `default.json` by direct read, the `scripts/fsgg-coord` line counts by walking the commit series.
  Claims that rest only on a trace are marked *(traced)*.

---

## 1. TL;DR

- **Both things are true at once.** Four repos did real, durable work today. One repo — `.github` —
  is the loop generator, and it went backwards.
- **63% of today's closures were same-day churn.** Of 127 issues closed, **80 were also filed today**.
  Most of the day's "progress" was cleaning up what the day itself produced.
- **Five loops are confirmed to regenerate**, and three of them are *accelerating*. They are not bad
  luck; each has a specific structural cause, and in four of the five the fix is already written down
  and un-owned.
- **The unifying defect, stated once:** *the org checks that a declaration exists, not that the
  capability behind it works.* `hostRules` is present but its token never resolves. The ADR says the
  manifest is distributed but nothing distributes it. The ApiCompat gate exits 0 but has never
  compared an API. The registry says zero repos receive `build-config` while four enforce it.
- That is [#266](https://github.com/FS-GG/.github/issues/266) — *"a check that passes when its subject
  is missing is worse than no check"* — one layer down. As
  [#417](https://github.com/FS-GG/.github/issues/417) already says of #266: **"it never turned the lens
  on itself."**

---

## 2. The census

| Repo | Opened | Closed | Filed **and** fixed today | Backlog burned | Net open |
|---|---|---|---|---|---|
| FS.GG.Rendering | 38 | 56 | 32 | 24 | **−18** |
| FS.GG.SDD | 6 | 8 | 5 | 3 | −2 |
| FS.GG.Governance | 9 | 8 | 8 | 0 | ≈0 |
| FS.GG.Game | 31 | 31 | 26 | 5 | 0 |
| **FS-GG/.github** | **36** | **23** | 9 | 14 | **+13** |
| FS.GG.Templates | 0 | 1 | 0 | 1 | −1 |
| FS.GG.Audio | 0 | 0 | 0 | 0 | 0 |
| **Total** | **120** | **127** | **80** | **47** | — |

**Rendering and SDD are the genuine progress.** Rendering burned 24 pre-existing issues and closed
its backlog by 18; SDD is down to 3 open. Governance closed everything it opened and stands at 1 open
issue. These repos are converging.

**`.github` is the only repo that grew.** 27 of the 36 issues it filed today are still open. **27 of
its 52 open issues concern a single file**, `scripts/fsgg-coord`.

**PR volume: 203 merged — 127 human, 76 bot.** The 76 are `chore: sync coordination kit` PRs, one per
`fsgg-coord` commit, fanned into six repos. FS.GG.Audio alone merged 15 of them and closed zero issues:
its entire day was absorbing `.github`'s churn.

---

## 3. Loop 1 — `fsgg-coord` is a treadmill, and it is winning

**Ratio today: 13 closed, 18 opened — 1.38 new defects per fix.** The standing debt grew.

The recurrence chains are 2–4 deep and **every one of them currently ends in an open issue** *(traced,
chains confirmed by reading the issue bodies)*:

| Chain | Mechanism |
|---|---|
| `#344 → #461 → #584` | `die` inside `$( )` exits the subshell, not the script. Fixed at the `gql()` sites, then at `$cand` — and **#584 is the identical fail-open one variable over** (`$claims`). Self-described: *"#344 recurrence at a missed call site."* |
| `#322 → #583 → #614` | Roll-up stamps Done over open children. **#614: `done --flip` closed an open parent** whose only child was an explicitly partial fix whose body said, in bold, that it did not close the parent. |
| `#440 → #452 → #481 → #488` | **#488: *"#440's fix reintroduced #440's defect in its own else-branch."*** |
| `#558 → #616` | A closing keyword in the commit *subject* strands the item on a permanent red stamp. **#616: an unclosed code fence in the body voids `Closes #N`** — *"reached by following the recipe correctly."* |
| `#533 → #629` | #533 fixed claim-drop. **#629: the recipe's own worktree step re-derives your worker id, so #533's fix goes inert.** |

In three of these (#614, #616, #629) the *new* defect is reached **by following the documented recipe
correctly**. That is the signature of a loop rather than a bug queue: the remediation and the
recurrence are the same commit's two halves.

**The measurement that settles it.** ADR-0034 was ratified **today** (#586) and indicts
`scripts/fsgg-coord` **at 4,024 lines**. Walking the commit series for the same day:

| commit | lines |
|---|---|
| `b2ad3bf` | 3,724 |
| `1d05b47` | 4,024 ← *the number in the ADR* |
| `df91684` | 4,339 |
| `11a53bf` | 4,655 |
| `49d221d` | 5,296 |
| `ab8d554` (HEAD) | **5,328** |

**The file grew 43% today, and 32% of that came *after* the ADR that condemned it was ratified.** The
typed replacement (`FS.GG.Coord.Core` + `.Cli`, ~1,355 lines F#) runs in **shadow mode** — both engines
decide, and *bash's answer is the one returned*. Every day the flip does not happen is a day the file
it is meant to replace gets bigger.

`df91684`'s own subject is the tell: *"#485 consolidated five predicates into one — **and three
disagreements outlived the merge**."*

---

## 4. Loop 2 — build-config drift, and the registry that lied about it

`.github` edits a shared file → every downstream repo's **required** drift check goes red → four repos
are merge-frozen → someone hand-copies the same lines into each. It has fired **five times** (07-02,
07-03, 07-11, and **twice on 07-12**) *(traced)*. Every fix is a hand-written file copy; there is **no
propagation channel for build-config**, even though the org has propagation machinery for every other
shared artifact.

**The failure is documented in the PR that caused it.** Commit `544433a` (PR #627, 15:38) merge-froze
Game, Rendering, Governance and SDD within forty minutes. Its PR body argues it *cannot* do so:

> "A genuinely-distributed managed file would have red-lit the drift check — a *required* check — in
> every adopting repo... **That is luck, not design.**"

That reasoning came from #626's audit, which read the `receives:` rows in `registry/repos.lock`.
**Verified directly:** `repos.lock` contains **zero** `build-config` rows — and **four of six repos
enforce the check in `gate.yml`** (SDD, Rendering, Governance, Game; only Templates and Audio do not).

**The registry under-reported its own blast radius, and the author believed it.** #628, filed at 16:13
— *after* the damage — names it exactly: *"four repos enforce it, the registry says zero, and #626 read
the zero."*

Two aggravations:

- **#592 diagnosed this loop correctly at 08:12 today**, with three viable fixes. It is open,
  unassigned, no linked PR. The loop fired again seven hours later, in the same repo, by an author who
  had read it.
- **The blast radius is growing, not converging.** Rendering wired up its drift check at 04:13 today
  (PR #571, closing #538) and was bitten by it eleven hours later (#658). The enforcer population went
  3 → 4. #626/#628/#609 would take it to 7.

---

## 5. Loop 3 — the skill-mirror treadmill is doubling daily

Skill bodies are **byte-identical across three repos** (ADR-0022 §6). Every canonical edit in
FS.GG.Game reds Rendering's `check-frozen-mirrors`, requiring a filed issue and a hand-authored PR to
re-copy bytes that already exist elsewhere.

Re-mirror/re-freeze issues by day *(traced)*: **1** (07-08) → **1** (07-10) → **3** (07-11) → **6**
(07-12, one every ~2.2 hours). Across the four affected files there are **15 re-freeze commits**; more
than half of `fs-gg-audio`'s and `fs-gg-persistence`'s entire file history is re-copying someone else's
bytes.

**The org already built the bot that would fix this.** `app/fs-gg-cross-repo-dispatch` propagates on
producer-commit — and it is pointed at the **registry digest**, not the **mirror bytes**. Rendering's
own guard hard-codes the asymmetry (`scripts/check-frozen-mirrors.fsx:398`): *"the autofix bot will
reconcile it; this guard judges the BODY."*

[#422](https://github.com/FS-GG/.github/issues/422) names the root cause precisely — *"Every correct
execution of a dual-homed skill edit must red main for a window. **There is no way to do it right**"* —
and lists four remedies. It is open and untouched. It also observes that the org has *"normalised a
recurring red main as the cost of a correct edit."*

**As of this writing, `FS.GG.Rendering@main` is red** on `kit / coordination-kit`, because sync PR #650
is open and unmerged. That is failure mode **(d)** from epic #266, catalogued on 07-09: *"drift whose
sync PR was opened and never merged — receivers sit red indefinitely and the red stops meaning
anything."*

---

## 6. Loop 4 — doc-vs-pin whack-a-mole, and Loop 5 — the feed nobody can read

### 6.1 The gate cannot converge because the thing it measures keeps growing

Five gate-widenings landed in Rendering today (PRs #593, #605, #609, #617, #627), each covering a
genuinely new document surface. They found **~9 distinct violations the same day** *(traced)*. Two of
those PRs announced themselves as covering "the last unjudged shipped doc surface"; both were wrong
within two hours.

The gate work is good — it is a real *reachability* check where every prior gate was a *coherence*
check. But the dominant term is structural: **Rendering ships its docs from `main` while its readers
bind the last tag (0.9.0), and `main` has run at least six public symbols ahead of it.** Each new gate
surface simply reveals more of an already-existing population.

**This one is being fixed correctly, and it is the good news of the day:** Rendering **PR #651** is
cutting the additive **0.9.1** release — which retires 3 of the 4 live ledger lines and unblocks
FS.GG.Game#219. (PR #642, the 0.10.0 removal, remains a deadlocked draft: merging *is* publishing, and
it is in a hard cycle with Game#219.)

### 6.2 The private feed is a decorative source of truth

`FS.GG.SDD.Cli`'s validator pin has frozen and been hand-advanced **three times** (#127 → #263 → #566 /
#576). The stated cause is a Mend App Secret that no code change can reach. **The amplifier has not
been named before now.** `default.json` — the preset every repo extends:

```json
"matchPackageNames": ["/^FS\\.GG\\./"],
"registryUrls": ["https://nuget.pkg.github.com/FS-GG/index.json"]
```

This **overrides discovered registries** and funnels every `FS.GG.*` lookup, org-wide, through the one
auth-required feed whose credential does not resolve — while **all `FS.GG.*` packages are public and
anonymously readable on nuget.org**, which is how the repos actually restore them. *A 401 on a Renovate
datasource is not an error; it is an empty version list.* The bot sees "no new versions" and reports
success.

Proof it has never worked from here *(traced)*: `.github` has had **9 Renovate PRs ever, and zero of
them are `FS.GG.*`**.

Consequences still live: **FS.GG.Templates' `FS.GG.UI.Template` is frozen at 0.8.0, and the fix has sat
in open, non-draft PR #140 since 07-11**, unfiled and unnoticed.

### 6.3 Correction: the coordination engine is **not** unreachable

#624 and #626 say the engine cannot reach the fleet. **Both are now stale.** Verified directly:

```
GET https://api.nuget.org/v3-flatcontainer/fs.gg.coord.cli/index.json  →  200, ["0.1.0"]   (anonymous)
```

The nuget.org dual-publish (#625) went green at 15:25 and Rendering, Governance and Game have all
adopted the tool. **Fleet coverage is 3/6** — SDD, Templates and Audio still lack it. The two issues
should be reconciled against reality rather than worked as written.

### 6.4 The gates in three repos cannot stop anything

`#594` says `.github/main` has no branch protection. **It is true, and it is worse than filed.**
Verified via `gh api`:

| Protected (required contexts) | **Unprotected** |
|---|---|
| SDD (3), Rendering (2), Templates (2), Audio (2) | **`.github`**, **FS.GG.Governance**, **FS.GG.Game** |

Three of seven repos — **including the authority repo that mandates required checks org-wide** — cannot
block a red merge. Only `.github`'s case has been filed; **Governance's and Game's have not.**

And the org is **structurally incapable of noticing**: #574 records that no workflow can read branch
protection, because `administration: read` is not a valid `GITHUB_TOKEN` permission scope.

---

## 7. Candidate actions

Ordered by leverage. None of these is a decision; each needs an owner.

1. **Merge Rendering PR #651** (FS.GG.UI 0.9.1). The single highest-leverage merge available: it
   retires an entire issue family and unblocks Game#219.
2. **Fix `default.json`'s `registryUrls`** to permit the public nuget.org fallback. This closes a
   third-recurrence loop with a config change and does **not** require the Mend admin action.
3. **Enable branch protection on `.github`, FS.GG.Governance and FS.GG.Game** — and file the latter
   two, which nobody has.
4. **Give #592 an owner, and stop editing `dist/dotnet` until the drift check pins a ref instead of
   tracking `@main`.** This is the direct cause of Loop 2.
5. **Point `app/fs-gg-cross-repo-dispatch` at the mirror bytes.** The machinery exists and runs on the
   right trigger; it is aimed one file over. Alternatively land #422's remedy (2) so a dual-homed edit
   fails as a PR instead of redding `main`.
6. **Merge Templates PR #140**; reconcile the stale #624/#626; get SDD, Templates and Audio onto the
   tool manifest.
7. **Land the ADR-0034 flip.** Shadow mode is not a resting state — bash still decides every call, and
   the file grew 43% on the day it was condemned.

---

## 8. The finding under the findings

Four of the five loops have a fix that is **already written down, correct, and un-owned** (#592, #422,
#576, PR #140). The org's diagnostic capability is not the bottleneck — it is excellent. The epics
(#266, #416, #417, #423) named this class of defect days ago, with precision.

What is missing is the step *after* diagnosis. #592 was filed at 08:12 with the correct mechanism and
three viable fixes, and the loop it describes fired again at 15:38 — because being *right* about a loop
and being *assigned* to it are different things, and only the second one stops the loop.
