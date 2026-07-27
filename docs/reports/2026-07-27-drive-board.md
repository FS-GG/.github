# drive-board — 2026-07-27

Org-wide Coordination burn-down. Operator: @EHotwagner. Host: `drive-board`, ~6 concurrent disposable
workers, publish authorization granted mid-run.

**The board did not shrink, and that is the headline finding — not a footnote.** 68 non-Done rows at
start, 76 at end, with 26 items landed. This report explains why that is the expected result, and what
would actually change it.

---

## 1. What shipped, by repository

### `.github` (13)

| item | PR | what it was |
|---|---|---|
| #1561 | #1603 | registry flip — all three contracts, reworked from a branch that would have flipped *backwards* |
| #1562 | #1652 | `next`'s empty arm stops printing prose on the `$(…)` ref stream |
| #1564 | #1648 | Renovate could not see FS.GG.Templates' kit pin at all |
| #1565 | #1650 | why kit bump PRs never merge — measurement corrected 12/0 → **16/4** |
| #1574 | #1653 | `check-skill-quality` returned silently on an unsupported contract schema |
| #1575 | #1662 | `landable` greened a PR GitHub refuses to merge |
| #1576 | #1657 | the SkillMirror conformance table was dated |
| #1585 | #1660 | the canonical skill digest had five implementations |
| #1594 | #1665 | the shared checkout's engine goes stale mid-run — **+ FS.GG.Kit 0.12.0** |
| #1599 | #1645 | `repos-audit`'s sparse sweep caught `GateError` but the reader raises `SparseRefusal` |
| #1620 | #1647 | `claim --force` could not steal a live claim, but `adopt` said it could |
| #1649 | #1669 | the chore offer reads the board column it asks a worker to write |
| #1651 | #1675 | an out-of-vocabulary `Class:` reported as *"records no `Class:`"* |
| #1658 | #1661 | `engine-pin-coherence` red on main — **+ FS.GG.Kit 0.11.0** |
| #1659 | #1670 | registry flip to Contracts 7.4.0 |
| #1604, #1619 | — | closed as satisfied by #1561's rework; no PR of their own |

### `FS.GG.Rendering` (6)

| item | PR | what it was |
|---|---|---|
| #1086 | #1091 | a required canonical surface resolving to zero files passed |
| #1089 | #1095 | the template package restored UNLOCKED in the release lane |
| #1092 | #1097 | `filesForSurface` ignored the surface's declared `RootPath` |
| #1093 | #1104 | `inventorySkills` swallowed every read exception |
| #1094 | #1100 | template payload pin lagged the feed (adopted from a dead worker) |
| #1101 | #1107 | **the pre-#782 legacy bridge retired** |
| #1102 | #1105 | `pin-lags-feed` split — structural blocking, staleness scheduled |

### `FS.GG.SDD` (4)

| item | PR | what it was |
|---|---|---|
| #731 | #741 | coordination-kit red on main — pin bump, **not** the byte-copy the item asked for |
| #733 | #751 | `doctor` never content-verified owner-sourced skill copies |
| #736 | #746 | an extra file under a skill root was permanent unrepairable drift |
| #737 | #749 | SkillMirror must refuse invalid UTF-8 — **+ FS.GG.Contracts 7.3.0** |
| #742 | #753 | Contracts packed no `api-surface/` — **+ FS.GG.Contracts 7.4.0** |

### `FS.GG.Templates` (3)

| item | PR | what it was |
|---|---|---|
| #313 | #316 | premise false; resolved as a decision record |
| #315 | #319 | `SKILL_ASSERT_REF` had frozen three times with no alarm |
| #317 | #318 | composition pinned template and assertion but floated `fsgg-sdd` |
| #321 | #322 | composition docs described a gate that had moved on |

### Releases published and verified on both feeds

- **FS.GG.Contracts 7.3.0** (#737) — the byte-level read seam
- **FS.GG.Contracts 7.4.0** (#742) — packs `api-surface/*.fsi`, first release that does
- **FS.GG.Kit 0.11.0** (#1658) and **0.12.0** (#1594)

`coord-engine 0.13.0` is authorized and in flight as #1673 at time of writing.

---

## 2. The three correct non-merges

Counted as successes, because each avoided a worse outcome than not shipping.

- **`.github#1613`** — a worker claimed it, read the comment thread, found two recorded parking
  decisions, refused to build it, and returned it to `Backlog`. The host had promoted it in error.
- **`FS.GG.SDD#735`** — a fully green PR (1,787 tests, `landable` green) **closed unmerged**, because
  review proved it would make `surface --check` — a required gate — report `isCoherent: true`, exit 0,
  on a file it never read. Reproduced with the built CLI: `chmod 000` flipped a real drift from
  `blocked`/exit 1 to `succeededWithWarnings`/exit 0 while `checkedCount` still reported 6.
- **`.github#1644`** — premise **refuted** before implementation: ADR-0045 shipped the parking
  representation the item said was missing (`Blocked on: human/decision` → `Types.HumanBlock` →
  `Schedulability` step 3b). Had #1613 carried the sentinel, `take` would have refused it outright.

---

## 3. Premise staleness — measured

**35 items were examined against today's world. Zero were correctly closeable.**

A read-only premise-audit sweep covered 27 rows across three batches: **20 `CONFIRMED`, 7 `RESCOPE`,
0 `SUPERSEDED`, 0 `UNDECIDABLE`**. A further 8 were found during implementation.

The stale part was almost never *"this problem is gone"* — it was the **diagnosis or the prescribed
remedy**. Four items had a true premise and a fix that would have caused harm:

| item | prescribed remedy | what it would have done |
|---|---|---|
| `SDD#731` | byte-copy the coordination mirror | turned a **required gate RED** (the gate is pin-relative since #1584) |
| `.github#1575` | derive required contexts from `branches/{b}/protection` | needs `administration: read`, **not a valid `GITHUB_TOKEN` scope** — silently breaks the unattended caller, restoring #463 |
| `SDD#743` | emit an `unreadableFile`-class finding | **that class does not exist** — its baseline cites a change that only ever lived on PR #744, closed unmerged |
| `SDD#752` | collapse two producer digest domains | **breaks `skill-union-assert.sh --digest`** — the split is documented and deliberate |

Audit cost ≈ 12k tokens per row against 100–350k for a worker discovering the same thing after
committing to an implementation.

**Conclusion for planning: this board has no stale-and-closeable population.** Triage will not shrink it.

---

## 4. The one pattern behind seven symptoms

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
The host committed it three times itself: a `Blocked by` written where nothing reads it, two wrong
`EX_RATE` diagnoses, and a watch script whose exit condition treated a command error as a pass.

---

## 5. Decisions taken (operator)

| row | decision |
|---|---|
| `Rendering#1102` | **split the verdict** — structural pin checks stay merge-blocking; newest-stable comparison moves to a scheduled sweep that files an item. Frozen literal stays **exact** with a comment naming the gate. |
| `.github#1589` | the canonical digest is defined over **decoded text**; invalid UTF-8 refused upstream. AC3's `.github`-side half filed as `#1656`. |
| `SDD#747` | **ignore rule only, no repair** — no `DeleteFile` effect, no per-path prompt, `doctor` stays read-only. Binding condition: a closed literal list plus a test proving the *complement*. |
| `.github#1615` | **(c) keep the coupling**; `#1586`'s criterion 5 retired as unachievable. Decided on the measurement that only **1 of 7** receivers was current *before* the bump — the fan-out is not being discharged, so removing the coupling fixes nothing. |
| `.github#1624` | **closed as overtaken** — asked a human to class ~50 rows; `lint` reported 1, and the other ~49 were *derived* by reconcile, not decided. |
| `.github#1636` | **§5 flip authorized now**, ahead of phases 2–4, per ADR-0067's own "separable rather than optional". ADR-0065 must be amended in the landing change. |

---

## 6. Rate-limit incidents

Four distinct conditions, which the engine reports identically. This mattered: the host misdiagnosed
the first two.

1. **Secondary/abuse-detection 403 on `branches/main/protection`**, three times, at ~62% primary
   headroom. Consequence: `landable` returned `unknown` with **empty stderr** for every open PR — the
   merge gate blind fleet-wide. Recovered in 7 minutes; the engine reported ~10 from the **primary**
   header, which is the wrong number for a secondary limit.
2. **Primary GraphQL exhaustion**, 5000/5000, ~39 minutes. The board is Projects v2 = GraphQL-only, so
   `claim`/`who`/`say`/`done` were all unavailable. REST stayed healthy at ~4000.
3. The engine's *"reset time could not be read"* fired on a reset **GitHub does report** — readable from
   REST `gh api rate_limit` throughout.
4. `gh issue create`, `gh issue view` and `gh pr merge` are **GraphQL-backed**; raw REST
   (`POST /issues`, `PUT /pulls/{n}/merge`) works during a GraphQL outage and was used to keep working.

Filed as **`.github#1666`**, corrected once after the first diagnosis proved wrong.

**No board write was ever stranded** — `flush --dry-run` was clean after every incident.

---

## 7. Filed this run

**Root causes:** `#1644` (refuted, re-scoped), `#1649` ✅ landed, `#1651` ✅ landed, `#1679` (the cache
half of #1649), `#1666`, `#1668`, `#1680`, `#1663`, `#1664`, `#1677`.

**Cross-repo:** `SDD#742` ✅ landed, `SDD#743`, `#745`, `#748`, `#750`, `#752`, `#754`;
`Rendering#1101` ✅ landed, `#1103`, `#1106`; `.github#1643`, `#1646`, `#1654`, `#1655`, `#1656`,
`#1667`, `#1671`, `#1672`, `#1673`; `Templates#317` ✅ landed, `#321` ✅ landed.

**Rewrite ledger:** `#1674` (phase 3), `#1676` (phase 4) — ADR-0067's phases had **no board
representation** before today.

**13 open issues were on no board at all** and were swept in, including `#1541` — the blocker `#1524`
had been waiting on while the board had never heard of it.

---

## 8. Human-blocked, and what this run did NOT establish

**Awaiting a human:**
- `.github#1589` — decided, but sits `Ready` with `Class: decision`; should be reclassed now the
  decision is taken.
- `FS.GG.SDD#754` — the *"present but unreadable"* representation call. Gates `#735` and `#743`.
- `.github#1587` — `Blocked` behind `#1613`, which is parked by judgement. Needs an explicit call on
  whether the interim need survives ADR-0067 §9.

**Deliberately parked, with reasons on the row:** `Rendering#815` (needs a coordinated
`FS.GG.UI.Diagnostics` major), `.github#1613`.

**Not established by this run:**
- **The board is not defect-free.** 25 startable `defect` rows remain.
- **`Rendering#928` is unclassed and structurally cannot be classed** until `#1103` lands — the sweep
  rewrites its body every run. It is the sole `CLASS-UNSET`. Its severity is therefore *unknown*, not
  minor.
- Five repos remain `coordination-coherence` red on `main` (kit receiver staleness, parked behind the
  rewrite by operator decision).
- Concurrency limits are unmeasured: the account's **board budget** sustains fewer workers than the
  **lock protocol** does, and nothing records either number.

---

## 9. Recommendation

**Do not run another burn-down wave next.** 35 items examined, zero closeable, and the filing rate
tracks the fixing rate because the findings are real.

Two things would change the shape:

1. **ADR-0067 phases 2→4** (`#1635`, `#1674`, `#1676`), plus the authorized `§5` flip (`#1636`).
   Retiring the copying apparatus is the only mechanism that produces `SUPERSEDED` in bulk — roughly
   half the live rows are propagation machinery. Today measured its cost precisely: a **documentation**
   change under `.claude/skills/pnext-item` obliged a three-root hand-sync, a package release, and seven
   stale receivers.
2. **Enforce `#266` as a rule** rather than re-filing it. Ten instances above; every fix has been
   local, and the next instance is already arriving.

`§5` is the cheapest real progress: it removes a third of the mirroring, independently of everything
else, and is already authorized.
