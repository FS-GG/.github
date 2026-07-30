# Which gates have ever produced a finding? — 2026-07-28

**Subject:** every workflow in the eight rostered FS-GG repos.
**Measured at:** `2026-07-28T15:57:46Z`, over **retained** GitHub Actions run history.
**Tool:** `scripts/check-gate-finding-history.py` (`.github#1582`, PR #1809). Reproduce with:

```
scripts/check-gate-finding-history.py --fetch --repo FS-GG/.github … --out corpus.json
scripts/check-gate-finding-history.py --corpus corpus.json --markdown
```

> Superseded for sample-size terminology by `.github#1840`: `totalRuns` included non-verdict
> conclusions. The ledger's retained totals remain historical observations, but only evaluated
> (`success`/red) runs may satisfy the `MIN_RUNS` floor or support a NEVER-FOUND verdict.

**Verdict: exit 1 — findings.** 134 workflows classified; 30 findings; 20 unmeasured; **0 unread**.

---

## Why this exists

On 2026-07-27/28 this org found **ten checks that could not fail, in ten different subsystems, in one
day** — `#1644`, `#1715`, `FS.GG.Audio#212`, `FS.GG.Rendering#1120`, `#1710`, `#1768`, `#1772`,
`#1740`, `#1784`, `#1799`. Not one was found by the check itself. Every one was found because a person
built the same thing twice and compared. `#1582` named the gap: *~70 workflows and ~50 checker scripts
audit the code, and nothing audits the auditors.*

Three of that day's five new `repos-audit` sweeps found nothing on their own subject. The gate all five
live in had been red for over 24 hours on a single unrelated cause. Nobody watching its colour could
have told a new finding from the standing one.

The one question here is the one that run history can answer without new infrastructure: **has this
gate ever, once, been red?**

---

## The answer

| verdict | n | meaning |
| --- | --- | --- |
| EXERCISED | 84 | has been red at least once — demonstrably can fail |
| **NEVER-FOUND** | **30** | ran ≥10 times, never once red |
| **STANDING-RED** | **0** at 15:57Z — but **1 at 15:00Z**; see below | |
| NEVER-RAN | 0 | |
| REUSABLE-ELSEWHERE | 7 | `workflow_call`-only — unmeasurable here, **not** clean |
| LOW-SAMPLE | 13 | <10 runs — **unmeasured**, not clean |
| UNREAD | 0 | |

The full per-workflow ledger is appended.

### 30 gates have never once been red

The sharpest, by retained run count:

| repo | workflow | runs | reds |
| --- | --- | --- | --- |
| `.github` | `touch-set-drift.yml` | 1256 | 0 |
| `.github` | `engine-flag-narrative.yml` | 1076 | 0 |
| `FS.GG.Game` | `governance.yml` | 878 | 0 |
| `.github` | `test-selector-selftest.yml` | 725 | 0 |
| `.github` | `cross-repo-request-predicate.yml` | 692 | 0 |
| `.github` | `permission-coherence.yml` | 524 | 0 |
| `.github` | `recipe-pagination.yml` | 341 | 0 |
| `FS.GG.Templates` | `lockfile-sync.yml` | 333 | 0 |
| `.github` | `touch-set-drift-selftest.yml` | 300 | 0 |
| `FS.GG.Audio` | `lockfile-sync.yml` | 177 | 0 |
| `.github` | `sync-build-config-selftest.yml` | 129 | 0 |
| `.github` | `repos-audit-selftest.yml` | 98 | 0 |
| `FS.GG.Net` | `gate.yml` | 84 | 0 |

**This is a question, not an accusation.** Each is either guarding something that never breaks, or it
cannot fail — and from outside those are indistinguishable, which is precisely `#1582`'s thesis. The
measurement cannot tell them apart and does not pretend to. Adjudication is filed as **`#1810`**.

**Eleven of the thirty are themselves selftests** — `test-selector`, `touch-set-drift`,
`sync-build-config`, `repos-audit`, `skill-union`, `new-sdd-workspace`, `lockfile-cold`,
`dispatch-preflight`, `required-context-coherence`, `kit-package`, `lock-range-coherence`. A selftest is
the negative control that makes its gate's green mean something; a negative control that has never
fired is the same question one level up. `#1715`'s and `FS.GG.Audio#212`'s lanes both lacked a
can-fire demonstration, and both were tautologies. `#1810` adjudicates these first.

`FS.GG.Net/gate.yml` is a repo's **entire** gate job at 84 runs and no red, and `FS.GG.Game/governance.yml`
at 878 is the largest single number in the fleet.

### The standing red — measured live, and repaired mid-measurement

An earlier sweep the same afternoon recorded:

```
STANDING-RED: 1
  FS-GG/.github  .github/workflows/repos-audit.yml
    — red on the default branch for 30.3h across 7 consecutive run(s)
      — past the 24h point where a colour stops being read
```

Unbroken from `2026-07-27T09:31:13Z`. It went green at `2026-07-28T15:46:29Z` on `79cc1ff` — *"registry:
drop the last four `receives: skill-union` rows"* (`#1742`, PR #1805) — **eleven minutes before the
final sweep**, which is why the table above reads 0.

Both readings are correct and the pair is the useful evidence: the rule fires on a real 30-hour red,
and it stops firing the moment the gate is repaired, rather than becoming a permanent accusation
(`#238`). This is `#1611`'s category-D finding — *a gate that never runs and a gate that always passes
are indistinguishable from outside* — with "always fails" as the third case, now measurable.

### Two leftover temporary workflows

`.github/workflows/tmp-app-token-probe.yml` (1 run) and `FS.GG.SDD`'s `regen-lockfiles-temp.yml`
(1 run). Both are LOW-SAMPLE, i.e. **unmeasured**, not clean — but both are named `tmp`/`temp` and have
run once. Noted, not filed: removing them is somebody's call, not this report's.

---

## What I deliberately did NOT measure

`#266`'s rule is that "I could not evaluate this" is never "I evaluated it and it passed". That applies
to this report as a whole, so the gaps are listed as loudly as the findings.

1. **Whether a red was a FINDING or a CRASH.** A run concludes `failure` when the gate found a defect
   **and** when the gate itself crashed, timed out, lost a token, or died at load in a sparse checkout
   (`#1510`/`#1512`/`#1515`). This counts run conclusions, so EXERCISED is an **upper bound** on "can
   detect something". It can never invent a gate that fired, but it can excuse one that only ever fell
   over — and several EXERCISED rows rest on a single red in hundreds of runs (`shell-lint` 2/1041,
   `worker-id-attractor` 4/1173, `recipe-landable` 1/663, `timeout-coherence` 1/442). Filed as
   **`#1812`**.
2. **Whether the subject ever changed.** A gate that never fired over a subject nobody touched is
   unremarkable; the same gate over a subject that moved daily is a suspect. Joining run history to
   path-filtered commit history is the obvious next leg. **Not done.** This is `#1582`'s rule S2 and it
   is the single biggest thing missing.
3. **The `--fetch` half is untested.** Acquisition has no fixture; a mock would only assert the mock
   (`#1772`). The URL shapes, `total_count` semantics, the listing-truncation guard, the trigger read
   and the backoff path are all unasserted. Filed as **`#1811`**.
4. **Retention.** GitHub retains runs for a bounded window. Every count here is over **retained**
   history, so "never red" always means "never red within retention". A gate that red-lit once a year
   ago reads as NEVER-FOUND.
5. **Workflow identity.** A workflow is keyed by file path; renaming it starts a fresh history. A
   recently renamed gate looks young and this cannot tell.
6. **The 7 `workflow_call` reusables.** `contract-coherence`, `coordination-coherence`,
   `dispatch-sender`, `kit-materialize`, `lock-range-coherence`, `lockfile-sync`, `skill-union-assert`
   run inside their callers' runs. They are reported as REUSABLE-ELSEWHERE — **unmeasured**, not clean.
   Measuring them means reading the callers' job-level conclusions and is not done.
7. **Required contexts, loop liveness, pin drift, registry reconciliation, the S1–S8 detective, the
   per-repo architecture review** — everything else in `#1582`'s AC1–10. Filed as **`#1814`**.
8. **`--min-runs 10` and `--red-hours 24` are judgements, not laws.** They are flags. Ten is set where
   it is because the gates that demonstrably do fire here reached their first red well inside ten runs;
   24h is `#1611`'s boundary made concrete.

### One thing that nearly went unmeasured and was recovered

The first two sweeps reported **7 of 8 repos UNREAD (HTTP 403)**, and the second reported all 8. That
was **not** a permissions boundary: the primary quota was 87% unused and a single `gh api` call to the
same URL succeeded. It was GitHub's **secondary** (rate-of-request) limit, triggered by ~450 requests in
a burst. Reporting "7 of 8 repos unread" would have been honest and useless — a line a reader learns to
skip, which is category-D arriving from the other direction. The tool now backs off and retries, and
this run reads **all eight repos with zero UNREAD**.

---

## The mutation proof — and the two bugs it found in this work

`#1582` demands a negative control of everything it audits. An audit of the audit fabric that could not
fail would be the funniest possible entry on the list it produces, and the eleventh.
`tests/gate-finding-history/run.sh` is therefore in two parts — **50 legs, 14 mutants, all caught**:

- **Part A — input mutation.** Planted corpora, one per verdict class, each asserted to produce exactly
  that verdict and exit code; plus a clean corpus asserted to produce **no** finding, which is what
  stops the gate inventing one.
- **Part B — source mutation.** Each rule is removed from a copy of the gate and Part A must go red. A
  mutant that survives means Part A was asserting something true regardless — `#1715`'s and `#1740`'s
  shape.

Two real defects in this work were found **by that harness**, not by review:

**1. All eleven mutants reported "caught" and not one had executed a line of classification code.**
Mutants were written to a temp directory, so `from lib.gate import …` failed and every one died at load
with exit 1 — which a naive harness cannot distinguish from a mutant Part A caught. **That is `#1784`'s
bug reproduced inside the harness written to avoid it.** Part B was 100% vacuous and green.

The fix is structural, and it is the generalisation of `#1784`: an **unmutated control**. An identical
copy is carried through the same copy step, path, interpreter and environment, and must **pass** Part A.
If it does not, every "caught" below it is declared an artefact of the machinery rather than a catch.
The apparatus must be shown to produce a PASS before it is trusted to produce a FAIL.

**2. The first classifier called all seven `workflow_call`-only reusables NEVER-RAN.** They cannot
accrue runs of their own. Seven loud, confident, wrong findings — `#238`'s false accusation, and the
way a report gets ignored. Fixed with the REUSABLE-ELSEWHERE verdict, and the discriminator is spelled
as the small closed set of **exclusions** rather than an enumeration of allowed events, so an event
GitHub adds over-reports NEVER-RAN instead of silently excusing a dead workflow. Both directions are
pinned by fixture legs and by two mutants.

The harness also refuses `#1784`'s exact failure directly: every mutation counts its anchor occurrences
and hard-fails unless it replaced **exactly one**, and a mutant run that does not complete Part A is
reported as a **harness failure**, never as a catch. Anchor rot cannot look like a caught mutant.

---

## Filed

| item | what |
| --- | --- |
| **`#1810`** | Adjudicate the 30 NEVER-FOUND gates — one verdict each, selftests first. *(decision)* |
| **`#1811`** | `--fetch` has no fixture; a mock would only assert the mock. *(hardening)* |
| **`#1812`** | `failure` ≠ finding — EXERCISED is an upper bound. *(hardening)* |
| **`#1814`** | The unbuilt remainder of `#1582`: legs 3–4, 9–14, the S1–S8 detective, the architecture review, and the still-open naming judgement. *(decision)* |

No gate was disabled, deleted or weakened by this work.

---

## Appendix — the full ledger

Generated by `scripts/check-gate-finding-history.py --corpus corpus.json --markdown` at
`2026-07-28T15:57:46Z`.

Verdict: exit **1** (FINDING).

| verdict | count | meaning |
| --- | --- | --- |
| EXERCISED | 84 | has been red at least once — demonstrably can fail |
| STANDING-RED | 0 | red on the default branch past the threshold — its colour carries no news |
| NEVER-FOUND | 30 | ran enough times and was never red — decorative, or guarding the unbreakable |
| NEVER-RAN | 0 | no runs in retained history despite a trigger that could start one |
| REUSABLE-ELSEWHERE | 7 | `workflow_call`-only — runs inside its callers, invisible here. UNMEASURED |
| LOW-SAMPLE | 13 | too few runs for 'never red' to mean anything — UNMEASURED, not clean |
| UNREAD | 0 | the API did not answer — UNMEASURED, not clean (#266) |

## EXERCISED — 84

| repo | workflow | runs | red runs | detail |
| --- | --- | --- | --- | --- |
| FS-GG/.github | `.github/workflows/adr-coherence.yml` | 179 | 4 | 4 of 179 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/architecture-map.yml` | 1744 | 158 | 158 of 1744 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/closing-keywords.yml` | 1165 | 31 | 31 of 1165 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/coherence.yml` | 2445 | 16 | 16 of 2445 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/coord-core.yml` | 219 | 1 | 1 of 219 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/coord-engine.yml` | 602 | 27 | 27 of 602 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/coord-github.yml` | 322 | 3 | 3 of 322 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/coordination-sync-selftest.yml` | 546 | 1 | 1 of 546 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/engine-freshness.yml` | 379 | 34 | 34 of 379 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/engine-pin-coherence.yml` | 312 | 88 | 88 of 312 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/feed-autofix.yml` | 69 | 11 | 11 of 69 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/feed-coherence.yml` | 263 | 43 | 43 of 263 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/generator-list.yml` | 333 | 1 | 1 of 333 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/graphql-monopoly.yml` | 353 | 3 | 3 of 353 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/kit-bump-shape.yml` | 13 | 1 | 1 of 13 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/kit-package.yml` | 167 | 10 | 10 of 167 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/kit-published-coherence.yml` | 347 | 63 | 63 of 347 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/paths-coherence.yml` | 429 | 6 | 6 of 429 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/pin-coherence.yml` | 145 | 32 | 32 of 145 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/policy-checker-inventory.yml` | 266 | 3 | 3 of 266 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/projection-selftest.yml` | 271 | 3 | 3 of 271 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/projections.yml` | 772 | 20 | 20 of 772 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/recipe-followup.yml` | 383 | 1 | 1 of 383 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/recipe-landable.yml` | 663 | 1 | 1 of 663 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/release-coord-engine.yml` | 19 | 1 | 1 of 19 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/release-drivers.yml` | 14 | 1 | 1 of 14 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/release-kit.yml` | 31 | 2 | 2 of 31 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/repo-filter-monopoly.yml` | 274 | 1 | 1 of 274 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/repos-audit.yml` | 13 | 10 | 10 of 13 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/repos-registry-selftest.yml` | 913 | 42 | 42 of 913 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/required-context-coherence.yml` | 13 | 1 | 1 of 13 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/reusable-job-id-coherence.yml` | 342 | 1 | 1 of 342 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/shell-lint.yml` | 1042 | 2 | 2 of 1042 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/skill-quality.yml` | 187 | 1 | 1 of 187 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/skill-registry-autofix.yml` | 118 | 1 | 1 of 118 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/skill-registry-coherence.yml` | 247 | 86 | 86 of 247 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/skill-roots-selfcheck.yml` | 383 | 8 | 8 of 383 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/skill-union-bundle.yml` | 54 | 3 | 3 of 54 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/skillmirror-freshness.yml` | 14 | 2 | 2 of 14 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/source-coherence.yml` | 177 | 7 | 7 of 177 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/timeout-coherence.yml` | 443 | 1 | 1 of 443 retained run(s) red — this gate demonstrably can fail |
| FS-GG/.github | `.github/workflows/worker-id-attractor.yml` | 1174 | 4 | 4 of 1174 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Audio | `.github/workflows/coordination-coherence.yml` | 398 | 21 | 21 of 398 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Audio | `.github/workflows/gate.yml` | 404 | 12 | 12 of 404 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Audio | `.github/workflows/kit-materialize.yml` | 32 | 3 | 3 of 32 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Audio | `.github/workflows/release.yml` | 11 | 3 | 3 of 11 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Game | `.github/workflows/coordination-coherence.yml` | 896 | 71 | 71 of 896 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Game | `.github/workflows/dependency-review.yml` | 1 | 1 | 1 of 1 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Game | `.github/workflows/gate.yml` | 897 | 80 | 80 of 897 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Game | `.github/workflows/kit-materialize.yml` | 119 | 3 | 3 of 119 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Game | `.github/workflows/lockfile-sync.yml` | 534 | 4 | 4 of 534 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Game | `.github/workflows/release.yml` | 18 | 1 | 1 of 18 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Game | `.github/workflows/skill-refs-sweep.yml` | 63 | 8 | 8 of 63 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Game | `.github/workflows/skills-package.yml` | 37 | 1 | 1 of 37 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Governance | `.github/workflows/coordination-coherence.yml` | 577 | 34 | 34 of 577 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Governance | `.github/workflows/gate.yml` | 735 | 95 | 95 of 735 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Governance | `.github/workflows/kit-materialize.yml` | 116 | 3 | 3 of 116 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Governance | `.github/workflows/lockfile-sync.yml` | 415 | 13 | 13 of 415 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Governance | `.github/workflows/publish.yml` | 24 | 4 | 4 of 24 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Governance | `.github/workflows/skill-view-check.yml` | 56 | 2 | 2 of 56 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Net | `.github/workflows/coordination-coherence.yml` | 71 | 7 | 7 of 71 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Net | `.github/workflows/kit-materialize.yml` | 47 | 4 | 4 of 47 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Net | `.github/workflows/lockfile-sync.yml` | 47 | 2 | 2 of 47 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Rendering | `.github/workflows/coordination-coherence.yml` | 1648 | 178 | 178 of 1648 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Rendering | `.github/workflows/gate.yml` | 1976 | 471 | 471 of 1976 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Rendering | `.github/workflows/kit-materialize.yml` | 235 | 3 | 3 of 235 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Rendering | `.github/workflows/lockfile-sync.yml` | 1102 | 21 | 21 of 1102 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Rendering | `.github/workflows/packaged-consumer.yml` | 403 | 11 | 11 of 403 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Rendering | `.github/workflows/release-tags.yml` | 515 | 7 | 7 of 515 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Rendering | `.github/workflows/release.yml` | 40 | 10 | 10 of 40 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Rendering | `.github/workflows/skill-refs-sweep.yml` | 33 | 7 | 7 of 33 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Rendering | `.github/workflows/template-dispatch.yml` | 36 | 2 | 2 of 36 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.SDD | `.github/workflows/composition-acceptance.yml` | 35 | 5 | 5 of 35 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.SDD | `.github/workflows/coordination-coherence.yml` | 1021 | 193 | 193 of 1021 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.SDD | `.github/workflows/gate.yml` | 1283 | 411 | 411 of 1283 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.SDD | `.github/workflows/kit-materialize.yml` | 193 | 5 | 5 of 193 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.SDD | `.github/workflows/lockfile-sync.yml` | 731 | 10 | 10 of 731 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.SDD | `.github/workflows/release.yml` | 68 | 2 | 2 of 68 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.SDD | `.github/workflows/skill-view-check.yml` | 25 | 2 | 2 of 25 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Templates | `.github/workflows/composition.yml` | 636 | 63 | 63 of 636 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Templates | `.github/workflows/coordination-coherence.yml` | 523 | 30 | 30 of 523 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Templates | `.github/workflows/kit-materialize.yml` | 95 | 3 | 3 of 95 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Templates | `.github/workflows/release.yml` | 9 | 1 | 1 of 9 retained run(s) red — this gate demonstrably can fail |
| FS-GG/FS.GG.Templates | `.github/workflows/upstream-bump.yml` | 65 | 1 | 1 of 65 retained run(s) red — this gate demonstrably can fail |

## STANDING-RED — 0

_none_

## NEVER-FOUND — 30

| repo | workflow | runs | red runs | detail |
| --- | --- | --- | --- | --- |
| FS-GG/.github | `.github/workflows/cross-repo-request-predicate.yml` | 692 | 0 | 692 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/dispatch-preflight-selftest.yml` | 34 | 0 | 34 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/drivers-package.yml` | 89 | 0 | 89 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/engine-flag-narrative.yml` | 1076 | 0 | 1076 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/gate-harness.yml` | 10 | 0 | 10 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/ignored-author-coherence.yml` | 74 | 0 | 74 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/kit-package-selftest.yml` | 23 | 0 | 23 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/lock-range-coherence-selftest.yml` | 26 | 0 | 26 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/lockfile-cold-selftest.yml` | 53 | 0 | 53 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/new-sdd-workspace-selftest.yml` | 60 | 0 | 60 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/parity-fixtures.yml` | 56 | 0 | 56 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/permission-coherence.yml` | 524 | 0 | 524 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/preset-repo-scope-coherence.yml` | 26 | 0 | 26 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/project-field-options.yml` | 54 | 0 | 54 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/publish-flags.yml` | 53 | 0 | 53 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/recipe-pagination.yml` | 341 | 0 | 341 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/repos-audit-selftest.yml` | 98 | 0 | 98 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/required-context-coherence-selftest.yml` | 29 | 0 | 29 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/skill-union-selftest.yml` | 83 | 0 | 83 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/skill-view.yml` | 11 | 0 | 11 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/sparse-checkout-closure.yml` | 87 | 0 | 87 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/sync-build-config-selftest.yml` | 129 | 0 | 129 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/test-selector-selftest.yml` | 725 | 0 | 725 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/touch-set-drift-selftest.yml` | 300 | 0 | 300 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/.github | `.github/workflows/touch-set-drift.yml` | 1256 | 0 | 1256 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/FS.GG.Audio | `.github/workflows/lockfile-sync.yml` | 177 | 0 | 177 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/FS.GG.Game | `.github/workflows/governance.yml` | 878 | 0 | 878 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/FS.GG.Net | `.github/workflows/gate.yml` | 84 | 0 | 84 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/FS.GG.Rendering | `.github/workflows/skill-view-check.yml` | 21 | 0 | 21 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |
| FS-GG/FS.GG.Templates | `.github/workflows/lockfile-sync.yml` | 333 | 0 | 333 retained run(s) and NOT ONE red — either it guards something that never breaks, or it cannot fail. From outside those are indistinguishable. |

## NEVER-RAN — 0

_none_

## REUSABLE-ELSEWHERE — 7

| repo | workflow | runs | red runs | detail |
| --- | --- | --- | --- | --- |
| FS-GG/.github | `.github/workflows/contract-coherence.yml` | 0 | 0 | no runs of its own, and no self-starting trigger (workflow_call) — it executes inside its CALLERS' runs, which this measurement cannot see. UNMEASURED, not dead: reporting it as never-run would be a false accusation (#238). |
| FS-GG/.github | `.github/workflows/coordination-coherence.yml` | 0 | 0 | no runs of its own, and no self-starting trigger (workflow_call) — it executes inside its CALLERS' runs, which this measurement cannot see. UNMEASURED, not dead: reporting it as never-run would be a false accusation (#238). |
| FS-GG/.github | `.github/workflows/dispatch-sender.yml` | 0 | 0 | no runs of its own, and no self-starting trigger (workflow_call) — it executes inside its CALLERS' runs, which this measurement cannot see. UNMEASURED, not dead: reporting it as never-run would be a false accusation (#238). |
| FS-GG/.github | `.github/workflows/kit-materialize.yml` | 0 | 0 | no runs of its own, and no self-starting trigger (workflow_call) — it executes inside its CALLERS' runs, which this measurement cannot see. UNMEASURED, not dead: reporting it as never-run would be a false accusation (#238). |
| FS-GG/.github | `.github/workflows/lock-range-coherence.yml` | 0 | 0 | no runs of its own, and no self-starting trigger (workflow_call) — it executes inside its CALLERS' runs, which this measurement cannot see. UNMEASURED, not dead: reporting it as never-run would be a false accusation (#238). |
| FS-GG/.github | `.github/workflows/lockfile-sync.yml` | 0 | 0 | no runs of its own, and no self-starting trigger (workflow_call) — it executes inside its CALLERS' runs, which this measurement cannot see. UNMEASURED, not dead: reporting it as never-run would be a false accusation (#238). |
| FS-GG/.github | `.github/workflows/skill-union-assert.yml` | 0 | 0 | no runs of its own, and no self-starting trigger (workflow_call) — it executes inside its CALLERS' runs, which this measurement cannot see. UNMEASURED, not dead: reporting it as never-run would be a false accusation (#238). |

## LOW-SAMPLE — 13

| repo | workflow | runs | red runs | detail |
| --- | --- | --- | --- | --- |
| FS-GG/.github | `.github/workflows/release-new-sdd-workspace.yml` | 6 | 0 | 6 retained run(s), none red — below the 10-run floor, so 'never fired' is not evidence. NOT a clean verdict: this is unmeasured. |
| FS-GG/.github | `.github/workflows/release-train-tooling.yml` | 5 | 0 | 5 retained run(s), none red — below the 10-run floor, so 'never fired' is not evidence. NOT a clean verdict: this is unmeasured. |
| FS-GG/.github | `.github/workflows/skill-view-parity.yml` | 8 | 0 | 8 retained run(s), none red — below the 10-run floor, so 'never fired' is not evidence. NOT a clean verdict: this is unmeasured. |
| FS-GG/.github | `.github/workflows/surface-impact-selftest.yml` | 7 | 0 | 7 retained run(s), none red — below the 10-run floor, so 'never fired' is not evidence. NOT a clean verdict: this is unmeasured. |
| FS-GG/.github | `.github/workflows/tmp-app-token-probe.yml` | 1 | 0 | 1 retained run(s), none red — below the 10-run floor, so 'never fired' is not evidence. NOT a clean verdict: this is unmeasured. |
| FS-GG/FS.GG.Game | `.github/workflows/release-skills.yml` | 6 | 0 | 6 retained run(s), none red — below the 10-run floor, so 'never fired' is not evidence. NOT a clean verdict: this is unmeasured. |
| FS-GG/FS.GG.Net | `.github/workflows/release.yml` | 7 | 0 | 7 retained run(s), none red — below the 10-run floor, so 'never fired' is not evidence. NOT a clean verdict: this is unmeasured. |
| FS-GG/FS.GG.Rendering | `.github/workflows/capability.yml` | 7 | 0 | 7 retained run(s), none red — below the 10-run floor, so 'never fired' is not evidence. NOT a clean verdict: this is unmeasured. |
| FS-GG/FS.GG.Rendering | `.github/workflows/dispatch-smoketest.yml` | 1 | 0 | 1 retained run(s), none red — below the 10-run floor, so 'never fired' is not evidence. NOT a clean verdict: this is unmeasured. |
| FS-GG/FS.GG.Rendering | `.github/workflows/pending-tests.yml` | 2 | 0 | 2 retained run(s), none red — below the 10-run floor, so 'never fired' is not evidence. NOT a clean verdict: this is unmeasured. |
| FS-GG/FS.GG.Rendering | `.github/workflows/template-base-skill-union.yml` | 3 | 0 | 3 retained run(s), none red — below the 10-run floor, so 'never fired' is not evidence. NOT a clean verdict: this is unmeasured. |
| FS-GG/FS.GG.Rendering | `.github/workflows/template-pin-staleness-sweep.yml` | 5 | 0 | 5 retained run(s), none red — below the 10-run floor, so 'never fired' is not evidence. NOT a clean verdict: this is unmeasured. |
| FS-GG/FS.GG.SDD | `.github/workflows/regen-lockfiles-temp.yml` | 1 | 0 | 1 retained run(s), none red — below the 10-run floor, so 'never fired' is not evidence. NOT a clean verdict: this is unmeasured. |

## UNREAD — 0

_none_
