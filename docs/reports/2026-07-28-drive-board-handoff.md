# drive-board — 2026-07-28 handoff

The 2026-07-27 run's report is `docs/reports/2026-07-27-drive-board.md` (merged `3af627f`); its §11 covers this day's first hours. This document covers the rest and is written for whoever picks the board up next.

Everything below was verified against the repositories rather than taken from a worker's narrative, except where it says otherwise.

---

## 1. What landed

**ADR-0067 phase 4 is complete — 7 of 7.** Every receiver's second committed skill root is retired; `.agents/skills` returns 404 on all seven and is a generated view of the tracked source.

| receiver | mechanism | files |
|---|---|---|
| FS.GG.Templates | stage 1 | 23 |
| FS.GG.Audio | `FS.GG.Audio#210`, `52a358f` | 39 |
| FS.GG.Net | `FS.GG.Net#45`, `602f47a` | 23 |
| FS.GG.Game | `FS.GG.Game#519`, `b1d4fbd` | 41 |
| FS.GG.Governance | `FS.GG.Governance#337`, `4daa25f` | 34 |
| FS.GG.Rendering | `FS.GG.Rendering#1125`, `63f08ba` | 70 |
| FS.GG.SDD | `FS.GG.SDD#779`, `730a214` | 51 |

Stage 2 **refused** — it measured FS.GG.SDD and retired nothing, because a receiver wiring a `skill-union` caller could not be retired: the reusable assertion runs over a bare checkout and a generated view root does not exist in one. That refusal produced blocker **B5** (`#1715`), which was decided as *retire the caller, stand `skill-view check` up in its place*, implemented across the three affected receivers producer-first, and closed the same day.

**The §8 alarm went from seven hand-written copies to one.** `#1710` collapsed them into the kit-delivered `skill-view check` and **raised the bar** doing it — all seven compared partial views by *directory count*; the shared one compares per-skill identity. `#1777` then deleted the local copies: **119,692 bytes across 7 files**, created that morning by the phase whose purpose is removing duplication.

**Two ADRs.** **ADR-0068** — the engine's tool-version pin leaves kit ownership, so an engine bump no longer republishes the kit. **ADR-0070** — *restore is a precondition of the repo being usable*, the contract that makes "commit no skill content in receivers" possible.

**Kit churn, measured.** Nine republishes across 2026-07-27/28 and **not one carried a change to a skill**. `#1586` removed three of the causes; ADR-0068 removed four. **7 of 9 eliminated.**

**Automerge, without arming anything.** `#1587` enabled `automerge: true, automergeType: "pr", platformAutomerge: false` for `FS.GG.Kit` alone. The `platformAutomerge: false` is load-bearing: Renovate's default hands the merge to GitHub, whose native auto-merge consults only the **required** subset — and `kit-bump-mechanical` is required nowhere, so the `mechanical + repair` class would have merged. `allow_auto_merge` is `true` in six of seven receivers, so nothing else would have stopped it.

**Three defects closed in the claim protocol**, all found under a fleet of five to seven concurrent workers: `#1646` (a claim could be taken in another worker's name, and `--force` signed the theft notice with an **innocent** worker's id), `#1740` (a live claim whose `Status` projection had not landed reserved nothing — observed live between two workers 53 seconds apart), `#1779` (the off-board and permanently-failed-write legs).

**`add` no longer files invisible rows.** Fourteen rows went silently unschedulable in three batches, every instance found by accident. `#1823` gave `add` a `Backlog` default that announces itself.

**Two releases**, both verified by **downloading the artifact** rather than by the job's colour: coord-engine 0.14.0 and FS.GG.Kit 0.16.0/0.17.0/0.18.0. The feed lagged 14 and 18 cache-busted polls behind the publish, with the index still listing the previous version while the blob 404'd.

**Two org-owner gates closed**: the dispatch App's `administration: write` (`#1712`), and FS.GG.Net's branch protection, which had **none of any kind** — `branches/main/protection` 404, `rulesets` empty, the only one of seven (`#1714`).

---

## 2. The gate fabric was measured, and it is not what anyone assumed

`#1582` asked whether the checks still measure anything. `#1810`, `#1829` and `#1830` answered it by **mutation** — break what each gate claims to protect, watch whether it fires.

> **30 of 30 adjudicated. 26 JUSTIFIED. 0 DECORATIVE. 4 NOT MEASURED.**

Including all eleven selftests, which went first because a negative control that has never fired is the emptiest possible artefact. Every one fires.

**Two gates fired for real during their own investigation.** `engine-flag-narrative` — 1,092 clean runs — caught the prose of the PR checking whether it ever fires. `FS.GG.Net/gate.yml` went red on NU1004 mid-adjudication and moved off the never-red list entirely.

**The one that looked decorative was not.** `FS.GG.Game/governance.yml` carries `continue-on-error: true` at job *and* step level, so its conclusion is `success` unconditionally — *"878 runs, 0 reds" is a tautology of its own config*. Its adjudicator **refused to write DECORATIVE**, on the grounds that it had read logs and inferred rather than mutated and watched. Filed as `FS.GG.Game#525`.

**A methodological correction that affects the population**: `check-gate-finding-history.py` counts `skipped` runs in `totalRuns`, which is what `MIN_RUNS` is computed against. For one gate, **693 of 725 retained runs are skipped**. `#1840`.

---

## 3. The finding that generalises

**Thirteen-plus checks that could not fail, across as many subsystems, in two days. None was found by reading. Every one was found by breaking something and watching.**

A partial list, because the pattern matters more than the count: a test built to red *"the day a rule reads the touch-set"* whose subject was a CLOSED issue (`#1644`); a union gate whose invariants became tautologies on a half-view (`#1715`); a §8 alarm whose dangling-root lane was unreachable because `[[ ! -e ]]` follows symlinks (`FS.GG.Audio#212`); a `pipefail` abort invisible to 157 passing legs (`#1768`); a fixture testing a hand-written **mirror** of the probe rather than the probe (`#1772`); `isNarrowing = true` passing all 462 assertions, in the PR written against that class (`#1740`); a guard that could never **pass** — `$(printf '\n')` is the empty string, so `*""*` matched everything and refused every bump PR in every receiver for two and a half hours (`#1799`); eleven mutants all reporting "caught" while none executed a line (`#1582`).

**Four separate harnesses reached for an anchor check and all four had a defect in it.** Their combined cost produced a specification, recorded on `#1808`:

1. an **unmutated control** that must pass, or every catch is an artefact;
2. **`NOT MEASURED` kept distinct from both `FAIL` and `PASS`**, end to end;
3. **anchors independent of the guard under test** — an anchor made of the guard's own output vanishes with the guard, so the leg reports `NOT MEASURED` exactly when it should report `FAIL`;
4. a leg that mutates the **fixture**, proving the refutation helper itself fires.

The rule, stated once: **an anchor must prove the command RAN, not that the guard FIRED.**

And the sharpest observation of the run, from `#1794`'s worker about its own work:

> My verification machinery caught my machinery's failures, but **nothing in it caught my reasoning's.**

---

## 4. What the driver got wrong

Recorded because the next reader will otherwise trust the same sources the same way.

**The central thesis was refuted.** I argued from *"30 gates have never been red"* that the apparatus was over-built, and filed `#1831` on it. The measurement said 26 of 30 JUSTIFIED and zero decorative. `#1831` is closed; what survived is `#1834`, a narrow measurable question, and it came back saying the byte-check reds **534 times in 24 days**.

**Three relayed inferences, all wrong, all more interesting than the truth.** That the parity failure count was unstable across observers (it was two different trees). That `shim.sh` was outside `#1751`'s `Paths:` (it was inside). That a stale branch would revert merged work (git merges against the merge base; `merge-tree` proved the blob identical). Each was a worker's *inference* passed on as a worker's *measurement*. The dull version was correct every time.

**A fabricated timestamp.** I wrote "verified at 09:20Z" into a brief; the retirement I was describing merged at 09:37:46Z. The finding was real, the precision was invented, and the worker caught it.

**A filed defect on a false premise.** `#1730` claimed FS.GG.Audio had no §8 alarm. It had one — reporting inside `Build + test` rather than as its own check run. I read check-run *names* and inferred absence. Withdrawn.

**An option that could not exist.** I recommended fixing `#1833`'s prose in canonical and letting it ride the next release. `check-board` is a kit `skill` row, so editing it reds `kit-published-coherence` on `main` **until** a release. The row had said so and I put the option up anyway.

**Two rows dispatched as parallel that were a lane of one.** `#1829` and `#1830` share a directory token, and a directory token swallows every file beneath it, so the narrower row cannot narrow out of the collision (`#1843`).

---

## 5. In flight at handoff

- **`#1825`** — the six unmeasured legs of `#1794`'s matrix plus the anchor fix, held by a worker.
- **`#1853`** — `workflow_dispatch` on the seven `kit-materialize.yml` callers, one receiver first.

---

## 6. What is next

**ADR-0070 step 4**: make `.claude/skills` generated in **one** receiver, prove the §8 alarm reds on an ungenerated tree **there**, then per repo under ADR-0067 §9. Only after that does anything get deleted.

**Three things to read before starting it**, all found after ADR-0070 was written and none of them fatal to it:

1. **`#1855` — the root holds content no package can regenerate.** ADR-0070 §1 is scoped to **kit-derived** content precisely because of this, and §1.1 names the obstacle. FS.GG.SDD's producer-authoritative `.claude/skills/skill-manifest.json` was rehomed *into* that root the same day by `FS.GG.SDD#771`, and without it SDD's required `gate` dies at *"producer manifest missing"*. FS.GG.Audio holds 16 of its own `fs-gg-sdd-*` skills there. ADR-0065 §Retiring a root — which ADR-0070 leaves unchanged — permits only the materializer to remove such a file. **The `#771` decision and the ADR-0070 decision were taken hours apart by the same driver and interact; neither anticipated the other.**

2. **The restore does not currently reach the materialize.** Six of seven receivers carry a repo-root solution and **none lists `.config/kit/FS.GG.Kit.receiver.proj`** (FS.GG.Templates has no repo-root solution — NOT MEASURED). So an ordinary `dotnet restore <solution>` does not run the materialize, and *"restore is a precondition"* does not yet deliver what it promises. Together with `#1845`'s measurement — **0 of 842 runs on `main`** — this is the gap step 4 has to close first.

3. **`FS.GG.Rendering/skill-view-check.yml`** was adjudicated JUSTIFIED with a **recorded expiry** — *"if the repo returns to two committed copies, the verdict must be re-taken."* ADR-0070 moves it the other way, to **zero** committed copies, which the expiry does not cover. That verdict must be re-taken against the new tree, not inherited. It would be the third gate to decay after being justified, and the first to decay deliberately.

**The open decisions**, none of which gates anything: `#1737` (a human-park records *that* someone must act, not who or why), `FS.GG.SDD#754` (how the core should represent "present but unreadable"), `FS.GG.SDD#778` (manifest rows still resolving through `.agents/skills`).

**The highest-value open defects**: `#1844` (four receivers materialize `Directory.Build.props` but `include-build-config` is passed by **0 of 7**, so it is never graded — self-healing *and* gate-blind), `#1840` (skipped runs counted as sample size), `#1838` (the 26 verdicts are point-in-time and nothing re-verifies them; two gates already decayed after being justified).

---

## 7. One thing to keep

The board did not shrink. It will not. Roughly forty items closed and roughly sixty opened, and the composition held — defects flat, hardening up, decisions down.

What changed is that **decisions got taken**: phase 4 finished, two ADRs written, automerge settled without arming, the lock protocol repaired, and the audit fabric measured for the first time since it was built.

Three workers refused instructions from the driver and were right every time — stage 2 refusing to retire SDD, `#1769` refuting the premise it was given, and `#1587` refusing to arm a context whose mechanism could not express the decision. **Every refusal was worth more than the work it declined to do.** A future driver should expect that and make it easy.
