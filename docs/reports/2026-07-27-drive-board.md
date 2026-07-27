# drive-board — 2026-07-27

Org-wide Coordination burn-down. Operator: @EHotwagner. Host: `drive-board`, up to 6 concurrent
disposable workers, publish authorization granted mid-run.

**The board did not shrink, and that is the headline finding — not a footnote.** 76 non-Done rows at
end, against 34 rows reaching Done during the run. This report explains why that is the expected
result and what would actually change it.

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

FS.GG.Contracts **7.3.0** and **7.4.0**; FS.GG.Kit **0.11.0** and **0.12.0**.
`coord-engine 0.13.0` was authorized and **still in flight** when this was written — merged and tagged
(`bf5509a`), publish and flips outstanding, `#1667` folded into it and closed.

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
- **The board is not defect-free.** 25 startable `defect` rows remain.
- **`Rendering#928` is unclassed and structurally cannot be classed** until `#1103` lands — the sweep
  rewrites its body every run. It is the sole `CLASS-UNSET`; its severity is **unknown**, not minor.
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
   Retiring the copying apparatus is the only mechanism that produces `SUPERSEDED` in bulk.
2. **Enforce `#266` as a rule** rather than re-filing it. Ten instances above; every fix has been local.

**Caveats on (1), from review.** The "roughly half the live rows are propagation machinery" claim was
independently re-tested and **holds** — 32/76 strict, 41/76 generous. But it is an *eyeballed*
categorisation with no board field behind it, so no reviewer can reproduce it; and 9 of the 10 live
`FS.GG.SDD` rows are the SkillMirror/doctor **library**, not `.github`'s copying apparatus. ADR-0067's
`Affects` line does name FS.GG.SDD, so the claim survives — but bulk closure there depends on phases
2–4 reaching that library, which `#1635`/`#1674`/`#1676` do not currently say they will.
