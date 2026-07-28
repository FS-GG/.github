# drive-board — 2026-07-27

Org-wide Coordination burn-down. Operator: @EHotwagner. Host: `drive-board`, up to 6 concurrent
disposable workers, publish authorization granted mid-run.

**The board did not shrink, and that is the headline finding — not a footnote.** 74 non-Done rows at
the time of this revision, against 34 rows reaching Done during the run. This report explains why that
is the expected result and what would actually change it.

> **Review note.** A first draft of this report was reviewed adversarially before merge and came back
> `CHANGES REQUIRED`. It was wrong on the landed count, contradicted itself on two internal counts,
> made a false claim about `#1589`, overstated `#1644` as re-scoped when it was not, and **omitted the
> run's clearest self-inflicted cost**. All are corrected below, and §5 now records the host's errors
> in full rather than in passing. The review is on PR #1681.

---

## 1. What reached Done — 34 rows

**31 landed through their own merged PR.** Three more closed without one: `#1604` and `#1619` were
satisfied by `#1561`'s rework, and `#1624` was closed as overtaken.

Counted from the board (`ready --all --json`, rows `Done` with `closed_at` after 16:15Z), not by hand
— the first draft hand-counted and got 26.

### `.github` — 18

`#1561` (PR #1603, registry flip, reworked from a branch that would have flipped *backwards*) ·
`#1562` (#1652) · `#1564` (#1648) · `#1565` (#1650, measurement corrected 12/0 → **16/4**) ·
`#1574` (#1653) · `#1575` (#1662, `landable` greened a PR GitHub refuses to merge) ·
`#1576` (#1657) · `#1585` (#1660) · `#1594` (#1665, **+ FS.GG.Kit 0.12.0**) · `#1599` (#1645) ·
`#1620` (#1647) · `#1649` (#1669) · `#1651` (#1675) · `#1658` (#1661, **+ FS.GG.Kit 0.11.0**) ·
`#1659` (#1670) · `#1604`, `#1619` (no PR — satisfied by #1561) · `#1624` (closed as overtaken)

### `FS.GG.Rendering` — 7

`#1086` (#1091) · `#1089` (#1095) · `#1092` (#1097) · `#1093` (#1104) ·
`#1094` (#1100, adopted from a dead worker) · `#1101` (#1107, **the pre-#782 legacy bridge retired**) ·
`#1102` (#1105, `pin-lags-feed` split)

### `FS.GG.SDD` — 5

`#731` (#741, a pin bump — **not** the byte-copy the item asked for) · `#733` (#751) · `#736` (#746) ·
`#737` (#749, **+ FS.GG.Contracts 7.3.0**) · `#742` (#753, **+ FS.GG.Contracts 7.4.0**)

### `FS.GG.Templates` — 4

`#313` (#316, premise false; resolved as a decision record) · `#315` (#319) · `#317` (#318) ·
`#321` (#322)

### Releases published and verified on both feeds

FS.GG.Contracts **7.3.0** and **7.4.0**; FS.GG.Kit **0.11.0**, **0.12.0** and **0.13.0**;
**coord-engine 0.13.0**.

`#1673` completed after the first draft: coord-engine 0.13.0 (PR #1678 → `bf5509a`) and the registry +
pin + kit chain (PR #1682 → `3b2d03f`), with `#1667` folded in. Both artifacts verified by **downloading
and comparing archive SHA-256 on each feed** — 31 and 35 payload entries byte-identical respectively,
nuget.org differing only by `.signature.p7s`. All five gates green on `main` by run id
(`engine-pin-coherence` 30309433996, `engine-freshness` 30309433948, `feed-coherence` 30309433951,
`source-coherence` 30309434053, `kit-published-coherence` 30309433960). Both predicted transient reds
occurred and neither was filed. nuget.org's index lagged ~4 minutes on both.

**That release produced evidence that independently validates the `#1615` decision.** The kit republish
was owed **twice over**: `#1667`'s regenerated `pnext-item` projection had already moved the kit tree
digest at `bf5509a`, *before the pin was touched*. So an engine release would have obliged a kit
republish **even if the pin lived somewhere else entirely** — option (a) or (b) would not have avoided
the fan-out. Also measured: **0 of 7 receivers were current** before this republish (newest pin anywhere
0.10.0 against a kit at 0.12.0; Audio at 0.6.0), and three kit releases today moved no receiver at all.
The bottleneck is discharge, not coupling.

---

## 2. One correct non-merge, and two self-corrections

The first draft banked all three as successes. Only the first is one.

- **`FS.GG.SDD#735` — a genuine correct non-merge.** A fully green PR (1,787 tests, `landable` green)
  **closed unmerged**, because review proved it would make `surface --check` — a required gate —
  report `isCoherent: true`, exit 0, on a file it never read. Reproduced with the built CLI: `chmod 000`
  flipped a real drift from `blocked`/exit 1 to `succeededWithWarnings`/exit 0 while `checkedCount`
  still reported 6.
- **`.github#1613` — a worker catching the HOST's error.** It was promoted to `Ready` despite two
  recorded parking decisions, because the host triaged issue bodies and not comments. The worker
  claimed it, read the thread, refused to build it, and returned it to `Backlog`. A success for the
  protocol; a failure by the host that the protocol absorbed.
- **`.github#1644` — a row the host filed and then refuted.** ADR-0045 had already shipped the parking
  representation it claimed was missing. Filing it was the error; refuting it was the correction.

---

## 3. Premise staleness — measured

**35 items were examined against today's world. Zero were correctly closeable.**

A read-only premise-audit sweep covered 27 rows in three batches: **20 `CONFIRMED`, 7 `RESCOPE`,
0 `SUPERSEDED`, 0 `UNDECIDABLE`**. A further 8 were found during implementation. *The verdict blocks
live in the run transcript and in the per-issue RESCOPE comments the host posted; they are not
committed to this repo, so this count is not independently reproducible from the tree. Stated as a
limitation, not a result.*

The stale part was almost never *"this problem is gone"* — it was the **diagnosis or the prescribed
remedy**. Four items had a true premise and a fix that would have caused harm:

| item | prescribed remedy | what it would have done |
|---|---|---|
| `SDD#731` | byte-copy the coordination mirror | turned a **required gate RED** (pin-relative since #1584) |
| `.github#1575` | derive required contexts from `branches/{b}/protection` | needs `administration: read`, **not a valid `GITHUB_TOKEN` scope** — silently breaks the unattended caller, restoring #463 |
| `SDD#743` | emit an `unreadableFile`-class finding | **that class does not exist** — its baseline cites a change that lived only on PR #744, closed unmerged |
| `SDD#752` | collapse two producer digest domains | **breaks `skill-union-assert.sh --digest`** — the split is documented and deliberate |

Audit cost ≈ 12k tokens per row against 100–350k for a worker discovering the same after committing to
an implementation.

**Conclusion for planning: this board has no stale-and-closeable population.** Triage will not shrink it.

---

## 4. One rule behind ten symptoms

Every stall today was a component reporting a conclusion it could not support, unable to distinguish
*"I could not evaluate this"* from *"I evaluated it and it passed."*

| symptom | reported | true |
|---|---|---|
| `#1649` chore offer | "this chore is outstanding" | a fresh read at the same instant disagreed |
| `#1679` chore offer | same | reads through a 90s cache; **cold cache made it vanish** |
| `#1651` lint | "records no `Class:`" | it records an **invalid** one |
| `#1666` `EX_RATE` | "REST budget exhausted", reset ~10m | secondary limit at ~62% headroom; recovered in 7 |
| `#1664` `stale_guard` | "run `git pull --ff-only`" | **inoperable** on the detached-HEAD checkout it names |
| `#1575` `landable` | `unknown`, **empty stderr** | a 403 it could not classify |
| `#1680` `landable` | `pending`/exit 7 — *"worth retrying"* | the PR is **merged**; a retry loop waits forever |
| `#1668` `who` | `UNCLAIMED — no claim marker` | a live marker existed 32s later — **and the inverse also occurs** |
| `BLOCKER-CLEARED` | `Status=Ready` | three items had **open PRs**; `Ready` invites duplicate work |
| CI on #1091/#1097 | `failure` | verdicts produced **3h39m and 2h12m before the fix existed** |

This is `.github#266`'s rule, re-filed under a new name at least ten times. **It is enforced nowhere.**

**`#1649` did not close its own symptom.** Its fix (the board join) landed; `#1679` then found a second,
independent cause — the offer reads `Cache.Scheduling` while `reconcile` reads `Cache.Reconciling`.
Separated by four measurements including a cold-cache reproduction. The symptom is still live on `main`.

---

## 5. The host's own errors

Recorded in full because a report that catalogues only the fleet's mistakes is the same selective
reporting this run kept catching. Seven, not the three the first draft admitted.

1. **Promoted `.github#1613` to `Ready`** despite two recorded parking decisions — triaged bodies, not
   comments. Cost: one worker's full context.
2. **Wrote a `Blocked by` edge into an issue body** when it is a board *field*. The edge was empty;
   `lint` caught it as `BLOCKED-NO-REASON`.
3. **Misdiagnosed `EX_RATE` twice** — first as account exhaustion, then as a self-imposed counter. It
   was a secondary/abuse-detection 403. Nearly halved the fleet on the first reading.
4. **Dispatched two workers at `#1604`/`#1619` on a false premise** — both were already satisfied by
   `#1561`'s rework, and the host had told them `#1619` would green `engine-pin-coherence`, which no
   registry flip can ever do.
5. **Wrote a watch script whose exit condition treated a command error as a passing condition** — the
   exact `#266` shape, in the host's own tooling.
6. **Dispatched a redundant worker at `#1651`** (~100k tokens). The host read PR state, reported it, and
   dispatched without re-verifying; the PR merged 90 seconds later. The run's clearest self-inflicted
   cost, and absent from the first draft entirely.
7. **Asserted "the body `Class:` line is the authority" and acted on it for `#1589`**, which has no such
   line — its class derived from the `[decision]` title prefix.

**And miscounted three times**: the audit as 19/8 before recounting to 20/7; the landed total as 26
against its own tables' 31; and the `#266` instances as "seven" in a section listing ten.

Total token cost of the run is not instrumented, so it is not reported. The two figures quoted in §3
are the only measured ones and they flatter the audit; the wasted dispatches in items 4 and 6 are not
netted against them.

---

## 6. Decisions taken (operator)

| row | decision |
|---|---|
| `Rendering#1102` | **split the verdict** — structural pin checks stay merge-blocking; newest-stable moves to a scheduled sweep. Frozen literal stays **exact** with a comment naming the gate. |
| `.github#1589` | digest defined over **decoded text**; invalid UTF-8 refused upstream. AC3's `.github` half filed as `#1656`. Reclassed `decision` → `hardening` once taken. |
| `SDD#747` | **ignore rule only, no repair** — no `DeleteFile` effect, no prompt, `doctor` stays read-only. Binding condition: a closed literal list plus a test proving the *complement*. |
| `SDD#754` | **third read state at the read seam**, additive — `Bytes \| Absent \| Unreadable`. Every fold must match all three; `isCoherent` false on any `Unreadable`; `checkedCount` counts `Bytes` only. |
| `.github#1615` | **(c) keep the coupling**; `#1586`'s criterion 5 retired as unachievable. Decided on the measurement that only **1 of 7** receivers was current *before* the bump. |
| `.github#1624` | **closed as overtaken** — asked a human to class ~50 rows; `lint` reported 1, and the other ~49 were *derived* by reconcile. |
| `.github#1636` | **§5 flip authorized now**, ahead of phases 2–4. ADR-0065 must be amended in the landing change. |

---

## 7. Rate-limit incidents

Four distinct conditions, reported identically by the engine. The host misdiagnosed the first two.

1. **Secondary/abuse-detection 403 on `branches/main/protection`**, three times, at ~62% primary
   headroom. `landable` returned `unknown` with **empty stderr** for every open PR — the merge gate
   blind fleet-wide. Recovered in 7 minutes; the engine reported ~10 from the **primary** header.
2. **Primary GraphQL exhaustion**, 5000/5000, ~39 minutes. The board is Projects v2 = GraphQL-only, so
   `claim`/`who`/`say`/`done` were all unavailable. REST stayed healthy at ~4000.
3. The engine's *"reset time could not be read"* fired on a reset **GitHub does report** — readable from
   REST `gh api rate_limit` throughout.
4. `gh issue create`, `gh issue view` and `gh pr merge` are **GraphQL-backed**; raw REST
   (`POST /issues`, `PUT /pulls/{n}/merge`) works during a GraphQL outage and was used to keep working.

Filed as **`.github#1666`**, corrected once after the first diagnosis proved wrong.
**No board write was ever stranded** — `flush --dry-run` was clean after every incident.

---

## 8. Filed this run

**Root causes:** `#1644` (refuted, re-scoped 22:0xZ — retitled with a warning banner so it cannot hand a
worker a false premise), `#1649` ✅, `#1651` ✅, `#1679`, `#1666`, `#1668`, `#1680`, `#1663`, `#1664`,
`#1677`.

**Cross-repo:** `SDD#742` ✅, `#743`, `#745`, `#748`, `#750`, `#752`, `#754`; `Rendering#1101` ✅,
`#1103`, `#1106`; `.github#1643`, `#1646`, `#1654`, `#1655`, `#1656`, `#1667` (closed into #1673),
`#1671`, `#1672`, `#1673`; `Templates#317` ✅, `#321` ✅.

**Rewrite ledger:** `#1674` (phase 3), `#1676` (phase 4) — ADR-0067's phases had **no board
representation** before today.

**13 open issues were on no board at all** and were swept in, including `#1541` — the blocker `#1524`
had been waiting on while the board had never heard of it.

---

## 9. Human-blocked, and what this run did NOT establish

**Awaiting a human:** `.github#1587` (`Blocked` behind `#1613`, which is parked by judgement — needs an
explicit call on whether the interim need survives ADR-0067 §9).

**Deliberately parked, with reasons on the row:** `Rendering#815`, `.github#1613`.

**Not established:**
- **The board is not defect-free.** 24 startable `defect` rows remain at the time of this revision.
- **`Rendering#928` is unclassed and structurally cannot be classed** until `#1103` lands — the sweep
  rewrites its body every run, so a `Class:` line added by hand is erased and the column cannot be
  hand-edited. Its severity is **unknown**, not minor.

  **`lint` now reports 0 findings, and that does not mean what it looks like.** `#928` was moved to
  `Blocked` behind a real `Blocked by: FS.GG.Rendering#1103` edge — correct sequencing, and the honest
  column, since it cannot be scheduled until its fix lands. But `CLASS-UNSET` fires on *`Ready` without
  a `Class:`*, so a clean lint here means **no unclassed `Ready` rows**, not no unclassed rows. The row
  is still unclassed. Recorded because it is the same shape as everything in §4, one level up at the
  reporting layer: a green signal whose scope is narrower than its reading.
- **The starting count of 68 is unverifiable** — no snapshot survives, and the arithmetic does not
  close (68 − 34 + ~33 filed + 13 swept ≠ 76). Treat the *end* state as measured and the delta as
  approximate.
- Five repos remain `coordination-coherence` red on `main` (kit receiver staleness, parked behind the
  rewrite by operator decision).
- Concurrency limits are unmeasured: the account's **board budget** sustains fewer workers than the
  **lock protocol** does, and nothing records either number.

---

## 10. Recommendation

**Do not run another burn-down wave next.** 35 items examined, zero closeable, and the filing rate
tracks the fixing rate because the findings are real.

1. **ADR-0067 phases 2→4** (`#1635`, `#1674`, `#1676`) plus the authorized **§5** flip (`#1636`).
2. **Enforce `#266` as a rule** rather than re-filing it. Ten instances above; every fix has been local.

> ### Correction: the "bulk closure" claim is weaker than first written
>
> The first two drafts said retiring the copying apparatus *"is the only mechanism that produces
> `SUPERSEDED` in bulk"*. **Phase 4 executed and measured otherwise, and the claim is withdrawn as
> stated.**
>
> `#1676`'s worker sorted the 18 live rows whose subject is this apparatus by a distinction the earlier
> categorisation missed: **duplication within a repo** versus **distribution between repos**. Only the
> first dissolves. Sorted that way, of ADR-0067's own list of seven pieces:
>
> | piece | fate under the rewrite |
> |---|---|
> | `skill-union-assert` | **retires** |
> | the second committed root | **retires** |
> | kit materialization | **narrows** (keeps non-skill subjects) |
> | `coordination-coherence` | **narrows** (keeps non-skill subjects) |
> | `kit-published-coherence` | **unchanged** |
> | the Renovate bump loop | **unchanged** |
> | the kit-pin freshness sweep | **unchanged** — its subject, *"is receiver R's pin current?"*, survives entirely |
>
> So most of the propagation cluster is about **distribution**, and distribution is what ADR-0067 §4
> explicitly declined to change when it rejected a monorepo. `#1676` therefore closed **zero** rows as
> `SUPERSEDED` — deliberately, because closing them would have closed live subjects.
>
> **What survives of the recommendation:** the rewrite is still the right work, it still removes real
> duplication (§5 alone cut the Codex catalog 6335 → 3174 chars and deleted a per-machine config block),
> and phases 2 and 3 landed clean with 8/8 repos measured in agreement. What does **not** survive is the
> expectation that finishing it collapses the board. The reviewer's caveat on the earlier draft — that
> the categorisation was eyeballed and that bulk closure *"depends on phases 2–4 reaching that library"*
> — was closer to right than the recommendation it was attached to.
>
> **And phase 4 was blocked on something that was on nobody's board:** `scripts/skill-view` was **not a
> `kit:` row**, so no published FS.GG.Kit — 0.14.0 included — had ever delivered the phase-2 replacement.
> A perfectly current receiver would still have had no replacement. Fixed by `.github#1696` (Kit 0.15.0).
>
> ### Settled: there is no bulk closure. The duplication was a cost, not a backlog.
>
> Phase 4 **stage 1 subsequently landed** — FS.GG.Templates' second committed root retired, 23 files,
> `coordination-coherence` and the required `composition` gate green on `main`. With a real retirement
> executed on a real receiver, the question could finally be measured instead of argued:
>
> > Of 66 live rows, **40 mention the apparatus. Not one has "a receiver commits its skills twice" as
> > its subject.** And **FS.GG.Templates has zero open board rows at all.**
>
> They are about *distribution* (`#1587`, `#1615`, `#1607`), `.github`'s **own** tree (`#1531`, `#1685`,
> `#1706`), corpus currency (`#1703`), or the coord engine. **The bulk closure is mis-sized, not
> deferred** — the duplication was a standing cost that nobody ever filed as a row, so retiring it closes
> nothing. This report claimed the opposite in three successive drafts. It is wrong, and this is the
> measurement that closes it.
>
> What the retirement *did* buy is real and separate: 23 files no longer committed twice in Templates,
> `coordination-coherence` narrowing 51→28 graded files **with no gate edit** (it derives roots from the
> receiver's own `FsggKitSkillRoots`), and — from §5 earlier — the Codex catalog down from 99% of its
> ceiling to 46%.
>
> **Phase 4 is not done and its worker refused to say otherwise.** AC7 remains unreachable as written
> (the freshness sweep's subject survives the rewrite entirely), and six receivers remain at stage 0
> behind `#1587`. `#1676` was set back to `Ready` rather than stamped — the second time that item has
> declined a false completion.

**Caveats on (1), from review.** The "roughly half the live rows are propagation machinery" claim was
independently re-tested and **holds** — 32/76 strict, 41/76 generous. But it is an *eyeballed*
categorisation with no board field behind it, so no reviewer can reproduce it; and 9 of the 10 live
`FS.GG.SDD` rows are the SkillMirror/doctor **library**, not `.github`'s copying apparatus. ADR-0067's
`Affects` line does name FS.GG.SDD, so the claim survives — but bulk closure there depends on phases
2–4 reaching that library, which `#1635`/`#1674`/`#1676` do not currently say they will.

---

## 11. Phase 4, as of 2026-07-28 07:30Z — in flight, not finished

This report is landed with phase 4 open rather than held until it closes. Holding a finished document
against an unfinished decision is how the stale premises catalogued in §3 got made, and §10's claim
that *"six receivers remain at stage 0 behind `#1587`"* has already expired — which is the point.

### Kit delivery: solved by hand, and `#1587` was never the gate

`#1587` (automerge) was described throughout this run as phase 4's blocker. It is not, and §10 is wrong
where it implies otherwise. It gates **unattended** delivery. Delivery itself was five open Renovate
bump PRs; landing them took one morning:

| receiver | pin | how |
|---|---|---|
| FS.GG.SDD | 0.15.0 | already current |
| FS.GG.Templates | 0.15.0 | already current |
| FS.GG.Governance | 0.15.0 | PR #333 → `2d37fa1`, clean and green |
| FS.GG.Audio | 0.15.0 | PR #209 → `1f9c58c`, clean and green |
| FS.GG.Net | 0.15.0 | PR #42 → `e97186a`, clean and green |
| FS.GG.Rendering | 0.15.0 | PR #1088 → `2d64ee5`, **after a receiver-side repair** |
| FS.GG.Game | 0.8.0 | PR #514 red on `#1718` — the kit's own `scripts/skill-view` |

**Six of seven.** The five-repo `coordination-coherence` red named in this session's opening handoff is
cleared.

Two of those were not mechanical, and both are now filed:

- **Rendering** needed `scripts/materialize-skill-roots.sh` fixed *in the bump PR*. Its checker verified
  the kit class across three roots; 0.15.0 retired one, so the correct sweep read as receiver drift. The
  repair cannot land before the bump (at 0.8.0 it is wrong) or after (the bump is red until it lands).
  **`#1587`'s shape guard would have refused this PR** — `#1726`.
- **Game** is red on a kit defect: `scripts/skill-view`, shipped first in 0.15.0, sources `lib/args.sh`
  and `lib/roots.sh` with no `source-path=SCRIPTDIR`. The four receivers already on 0.15.0 are green
  while carrying the identical broken file — Game owns the org's only SC1091 guard, and `.github`'s own
  `lint-shell.sh` runs at `-S warning` where SC1091 is invisible (`#1718`, `#1719`).

### Renovate: diagnosed, and it was not the preset

§10 left Audio and Net's dashboards unexplained. Ticking each dashboard's `<!-- manual job -->` checkbox
resolved both. Audio re-extracted and produced its bump. Net re-extracted, correctly detected
`0.8.0 → 0.15.0` against the post-`#1580` preset, and was holding the branch under
`<!-- unlimit-branch= -->` — **a rate limit, not a preset fault**. Ticking that produced PR #42 within
four minutes. No portal access was needed.

### What phase 4 actually retired: 1 of 7, and stage 2 refused

- **Stage 1** — FS.GG.Templates retired (§10 above).
- **Stage 2** — FS.GG.SDD **measured and retired nothing**, deliberately. `FS.GG.SDD@main` is unchanged
  at `387adc6`, verified. It found blocker **B5** (`#1715`): a receiver wiring a `skill-union` caller
  cannot be retired, because the reusable assertion runs over a bare checkout and a generated view root
  does not exist in one — `configured root is absent`, exit 2 — and a `uses:` job cannot add a generate
  step. The retirement order's §6 had priced this step at zero on the premise that *"zero receivers have
  wired one"*; SDD's caller landed `a066e0b` a day before that sentence was written.

B5 reaches **three** receivers — SDD, Rendering, Governance — measured by reading each receiver's
`.github/workflows/` directly. `registry/repos.yml` still asserts none do (`#1716`).

**Decided 2026-07-28, shape (b):** retire the caller, swap in `skill-view check --source <root>`. The
reason is this report's own recurring subject. On a half-view the existing gate's two headline
invariants become tautologies — `union_ids()` enumerates with `find` and no `-L`, so a view root
contributes zero ids, and presence is then tested with `[ -d ]` through the symlink and cannot fail. It
would keep reporting `success` on a required context under `enforce_admins` while asserting nothing.
That is `#266` in its most expensive form: a green indistinguishable from a green that means something.

**AC7 is retired as unachievable**, with the measurement, on the precedent of `#1586`'s criterion 5. The
freshness sweep's subject — *"is receiver R's pin current?"* — survives the rewrite entirely; it was
never inside the sequence it claimed to be last in. Independently confirmed: Rendering's
`kit / coordination-kit` was **green on `main` at pin 0.8.0**. The coherence gate grades content against
the declared pin and has nothing to say about the pin being seven versions stale. AC7 assumed the two
instruments were one.

`#1676` is now a parent with per-repo children (`#1720` Audio, `#1721` Net) rather than one row whose
single claim serialised seven independent retirements.

### Credential and protection, both closed

- **`#1712`** — the dispatch App's grant. Checked rather than accepted: the App *declared*
  `administration: write` while the **installation** still carried `read`, which is a pending permission
  request needing per-installation owner approval. After approval, re-read and verified `write`. Then
  the dry run found the grant was not the last gate — `kit-bump-shape` **has no receiver-side producer**
  (`#1713`), and `--apply` would have required a context nothing produces in six repositories, holding
  every PR at *"Expected — waiting for status to be reported"*. `#266` inverted, caught only because
  `#1613` made dry run the default.
- **`#1714`** — `FS.GG.Net`'s `main` had no protection of any kind: `branches/main/protection` 404,
  `rulesets` empty, the only one of seven. Now armed with its three verdict contexts,
  `enforce_admins: true`, `strict: false`. The roster sweep reads **7 repos: 0 would-add, 7 unchanged,
  0 failed**. Closed.

### What this section does not claim

Phase 4 is **not** done. Four workers are live on `#1715`, `#1718`, `#1720` and `#1721` as this lands,
and their results are not in this document. One repo of seven is retired. The honest statement is that
the rewrite is proceeding, that every blocker found so far was found by executing rather than planning,
and that three separate workers today returned items un-done with measurements rather than stamping
them — which is the behaviour this report spent §5 arguing for.
