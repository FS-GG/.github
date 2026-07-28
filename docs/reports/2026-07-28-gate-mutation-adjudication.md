# Adjudicating the never-red gates by mutation — 2026-07-28

**Subject:** the **25** workflows in `FS-GG/.github` that `#1582` measured as NEVER-FOUND — ten or
more retained runs, not one red conclusion.
**Method:** the decision taken on `#1810` — **break the thing each gate claims to protect, and watch
whether it fires.** Green tells you nothing; that is the whole finding.
**Harness:** `scripts/lib/mutation.py` + `scripts/gate-mutate.py`, generalised here per `#1808`.
**Reproduce:** `scripts/gate-mutate.py` (add `--json`, `--markdown`, or `--only <substring>`). The
full 17-leg sweep takes ~6 minutes — `tests/repos-audit/run.sh` alone is ~135 s and every leg runs its
command twice — and it exits **0**: `{"DECORATIVE": 0, "JUSTIFIED": 17, "NOT_MEASURED": 0}`.

## Verdict

**17 of the 25 adjudicated. All 17 JUSTIFIED. 0 DECORATIVE. 8 NOT MEASURED, named below.**

| verdict | n | meaning |
| --- | --- | --- |
| **JUSTIFIED** | **17** | fired under mutation — it demonstrably still protects what it claims to. **Keep.** |
| **DECORATIVE** | **0** | none found. No gate in the adjudicated set was unable to fire. |
| **NOT MEASURED** | **8** | no measurement obtained. **Not a pass, and not grounds for removal** (`#266`). |

**Nothing was removed, disabled or weakened.** `#1810` AC3 forbids it, and there was nothing to
remove: every gate that was measured, fired.

### The scope covered, stated plainly

`#1582`'s fleet-wide figure is **30 across 8 repos**. This report covers **the 25 in `.github` only**,
and adjudicates **17** of them. The **five outside `.github`** — `FS.GG.Game/governance.yml` (878
runs), `FS.GG.Templates/lockfile-sync.yml` (333), `FS.GG.Audio/lockfile-sync.yml` (177),
`FS.GG.Net/gate.yml` (84), `FS.GG.Rendering/skill-view-check.yml` (21) — are **NOT adjudicated here**
and are filed as `#1830`. A trustworthy partial beats a complete-looking whole.

**AC5 is fully discharged: all ELEVEN selftests are in `.github`, and all eleven were adjudicated
first.** They are the negative controls the other verdicts lean on, and a negative control that has
never fired is the emptiest possible artefact — two of the ten dead checks found on 2026-07-27/28 were
exactly that (`#1715`'s union gate; `FS.GG.Audio#212`'s alarm).

### AC4: re-measured, not inherited

`scripts/check-gate-finding-history.py --fetch --repo FS-GG/.github` was re-run on 2026-07-28 during
this adjudication. The result is **still exactly 25 NEVER-FOUND in `.github`, the same set**, with run
counts advanced (`touch-set-drift` 1256 → 1265, `engine-flag-narrative` 1076 → 1092, `permission-
coherence` 524 → 532). **No workflow gained a red between the ledger and this adjudication**, so no row
is inherited.

---

## Wave 1 — the eleven selftests (AC5)

A selftest's SUBJECT is the gate it is the negative control for. So each leg breaks a real protection
in that GATE and requires the SELFTEST to say so. Every one did.

| verdict | selftest | runs | gate mutated | what was broken | control → mutant |
| --- | --- | --- | --- | --- | --- |
| **JUSTIFIED** | `test-selector-selftest` | 741 | `scripts/test` | the refusal of negated `paths:` patterns — a filter that EXCLUDES a subtree silently matches nothing (`#266`) | 37/0 → 34/**3** |
| **JUSTIFIED** | `touch-set-drift-selftest` | 305 | `.github/workflows/touch-set-drift.yml` | an unmatchable `Paths:` (`#273`) reclassified as a clean pass | 14/0 → 12/**2** |
| **JUSTIFIED** | `sync-build-config-selftest` | 129 | `scripts/sync-build-config.sh` | the `#387` hand-authored-file guard, inverted — `apply` overwrites a receiver's own `Directory.Build.props` | 28/0 → 26/**2** |
| **JUSTIFIED** | `repos-audit-selftest` | 98 | `scripts/repos-audit.sh` | the `uses:` detector's optional-quote class — a QUOTED reference becomes a fabricated GAP | 188/0 → 186/**2** |
| **JUSTIFIED** | `skill-union-selftest` | 83 | `scripts/skill-union-assert.sh` | the `#1506` fail-open: a skill in exactly 2 of 3 roots is never byte-compared | 75/0 → 68/**7** |
| **JUSTIFIED** | `new-sdd-workspace-selftest` | 60 | `scripts/NewSddWorkspace/Program.fs` | the `#388` closed-set validation of `--profile` | 42/0 → 41/**1** |
| **JUSTIFIED** | `lockfile-cold-selftest` | 53 | `.github/workflows/lockfile-sync.yml` | ADR-0032 §5 coldness — regeneration runs warm and commits an unrestorable lock (`#429`) | 17/0 → 16/**1** |
| **JUSTIFIED** | `dispatch-preflight-selftest` | 34 | `.github/workflows/lockfile-sync.yml` | the `#482` "required: true means PROVIDED, not NON-EMPTY" check, made always-true | 33/0 → 30/**3** |
| **JUSTIFIED** | `required-context-coherence-selftest` | 31 | `scripts/check-required-contexts.py` | negated `paths:` entries ignored, so `["**", "!docs/**"]` scores green — the `#1508` deadlock | 76/0 → 75/**1** |
| **JUSTIFIED** | `lock-range-coherence-selftest` | 26 | `scripts/check-lock-ranges.py` | the core version comparison, neutered — the un-regenerated release bump passes again | 44/0 → 38/**6** |
| **JUSTIFIED** | `kit-package-selftest` | 23 | `src/FS.GG.Kit/stage-kit.sh` | the `#1614` fail-closed registry read — a dead reader yields an EMPTY kit instead of a refusal | 16/0 → 12/**4** |

**Reading the last column:** `<passed>/<failed>` from the fixture's OWN tally, unmutated → mutated.
The exit code alone is never the evidence — see "the crash/finding asymmetry" below.

**The eleven negative controls are real.** This is the single most load-bearing result here: every
green those eleven selftests have ever produced now means something, because each has been shown to go
red when its subject breaks.

## Wave 2 — six plain gates

These are not selftests. Their shape is one level down: the gate is a `scripts/check-*.py`, and the
fixture is the `tests/*/run.sh` that the SAME workflow runs as its first job — `permission-coherence.yml`
says so out loud: *"Offline, and first: prove the gate can still say NO before believing it when it says
yes."* A mutation the fixture catches is a demonstration that the WORKFLOW can go red.

| verdict | gate | runs | what was broken | control → mutant |
| --- | --- | --- | --- | --- |
| **JUSTIFIED** | `engine-flag-narrative.yml` | 1092 | the history allowlist widened from `docs/adr/` to all of `docs/`, re-opening `#866`/`#910` tier two | 14/0 → 13/**1** |
| **JUSTIFIED** | `permission-coherence.yml` | 532 | the permission lattice collapsed so `write` no longer outranks `read` — a provable `startup_failure` (`#478`) reported green | 27/0 → 26/**1** |
| **JUSTIFIED** | `recipe-pagination.yml` | 345 | `sub_issues` removed from the known-collection set, neutering the endpoint backstop for the flagship `#547` read | 21/0 → 20/**1** |
| **JUSTIFIED** | `sparse-checkout-closure.yml` | 94 | the anchoring rule made unreachable — an unanchored `scripts/` matching at ANY depth stops being reported (`#1510`) | 38/0 → 35/**3** |
| **JUSTIFIED** | `ignored-author-coherence.yml` | 84 | the app-slug placeholder made case-insensitive, so a RE-CASED entry matches — and `Set.delete` is exact, so it parks every Renovate branch | `OK` → `1 FAILURE(S)` |
| **JUSTIFIED** | `gate-harness.yml` | 10 | `lib.gate.run` returning `FINDING` on a crash — a bug in a gate reported as a confident finding about its subject (`#266`, `#320`) | `OK` → `FAILED` |

### `engine-flag-narrative.yml` fired for real, on this very PR

While leg 12 was being written, the gate **went red on `tests/gate-mutation/specs.yml` itself** — the
`breaks:` sentence spelled the retired engine-selection flag in the present tense, which is precisely
the defect the gate exists to catch, and it is the fifth hand-written recurrence the gate's own header
predicts. That is a **live catch on a gate with 1092 clean runs**, obtained without any mutation at
all, and it is stronger evidence than the mutation that follows it. The sentence was reworded; the leg
then passed on its own terms.

---

## NOT MEASURED — 8, and why each

**`#266`: "I could not evaluate this" is NEVER "I evaluated it and it passed."** None of these eight is
a clean verdict, and none is grounds for removing anything. They are filed as `#1829`.

| gate | runs | why it was not measured |
| --- | --- | --- |
| `cross-repo-request-predicate.yml` | 711 | Not attempted. Its subject is the ADR-0050 registry oracle across producer checkouts under `$FSGG_REPOS_ROOT`; a faithful mutation needs a multi-repo fixture this worker did not build. |
| `touch-set-drift.yml` | 1265 | **Distinct from its selftest, which IS adjudicated.** The gate is advisory by design (`--warn`, ADR-0021): it comments a verdict and deliberately does not fail the job, so "never red" is its specified behaviour rather than evidence of decay. Adjudicating it needs a different question than the one this method asks. |
| `drivers-package.yml` | 89 | Not attempted — packaging workflow; no local fixture identified. |
| `parity-fixtures.yml` | 60 | Not attempted. Its subject overlaps `tests/coord-engine-parity`, held by `#1794` while this ran. |
| `project-field-options.yml` | 55 | Not attempted — reads live board field options; needs a board fixture. |
| `publish-flags.yml` | 53 | Not attempted — a local fixture exists (`tests/publish-flags/run.sh`); simply not reached. |
| `preset-repo-scope-coherence.yml` | 28 | Not attempted — a local fixture exists; not reached. |
| `skill-view.yml` | 11 | Not attempted — a local fixture exists; not reached. |

Six of the eight are "not reached", not "resisted measurement". They are cheap follow-on work and
`#1825` says so; the two that are genuinely harder (`cross-repo-request-predicate`, `touch-set-drift`)
are called out with the reason.

---

## The harness, and how it satisfies the four-point specification

`#1808` decided: **generalise the anchor-checked mutation harness, defer the corpus.** It had to meet a
four-point specification that four harnesses paid to learn — `#1784`, `#1582`, `#1790` and `#1794`
**each shipped an anchor check with a defect in it.** An audit of the audit fabric that cannot fail
would be the eleventh entry on the list it exists to prevent.

**1 — an unmutated CONTROL that must pass.** `#1582`'s harness reported all eleven of its mutants
"caught" while **none had executed a line**; they died at import. `adjudicate()` runs the pristine
command first and refuses to grade anything whose control is not green. *It fired for real:*
`engine-flag-narrative`'s first run came back `NOT_MEASURED — the UNMUTATED control exited 1`, which
is how the live catch above was found.

**2 — `NOT MEASURED` distinct from both `FAIL` and `PASS`, end to end.** `#1784` reported "1 leg
fired" when its anchor had silently failed to match. `Verdict` has exactly three members; the JSON
carries all three counters *including the empty one*; `exit_code()` maps DECORATIVE→1, NOT_MEASURED→3,
all-JUSTIFIED→0, so the three stay separable from the shell. **8 of the 25 gates in this report are
NOT MEASURED and none of them is counted as adjudicated.**

**3 — anchors independent of the guard under test.** `#1794` used the guard's own output as its
anchor, so mutating the guard removed the anchor and the leg reported `NOT MEASURED` exactly when it
should have reported `FAIL`. Here every anchor is the **fixture's** own tally or terminal line, never
the gate's, and independence is **checked mechanically, not promised**: `load_specs()` REFUSES a spec
whose `anchor.produced_by` is the mutation target, and `adjudicate()` re-hashes that producer either
side of the edit and refuses to grade if its bytes moved.

**4 — a leg that mutates the FIXTURE.** `#1794`'s M10. `tests/gate-mutation/selftest.py` leg **M10**
hands the selftest's own refutation helper a deliberately WRONG expectation and requires it to return
`False`, increment the FAILED counter exactly once, and — end to end — grade a mutation of a fixture's
own expectation as `JUSTIFIED`. Without it, `expect` could be a no-op and all the other legs would
print `PASS` forever.

**The crash/finding asymmetry (`#1812`), which is point 1 one level down.** A run concluding `failure`
may be a crash rather than a finding. So a non-zero exit is **never** sufficient here: the anchor must
match *and* a discriminator must positively identify the exit as a verdict — a failed assertion in the
fixture's own tally, the typed `FINDING` code, or (for the two tally-free fixtures) a terminal report
line proving the run finished. *This fired for real too:* the `gate-harness` leg's first run exited 1
with **no anchor**, and was graded `NOT_MEASURED` rather than JUSTIFIED. The cause was a defect in the
spec — a YAML block scalar stripped the indentation off an added Python line, so the mutant died of an
`IndentationError` at import, which is `#1582`'s failure mode exactly. **The harness caught the same
class of defect that motivated it, in its own corpus.**

### The mutation proving this harness can fail

Point 1 applied to the harness itself: a selftest that cannot go red proves nothing, and this harness
is the eleventh candidate for the list it exists to prevent. So **all NINE of its safeguards were
removed in turn, and every single removal was MEASURED to red `tests/gate-mutation/selftest.py`.**
Unmutated, the selftest reports **42 passed, 0 failed**.

| # | safeguard removed | models | measured result |
| --- | --- | --- | --- |
| S1 | `_fired()` bypassed — believe any non-zero exit | `#1582` | **RED** 37/**5** — M1, M4b ×2, M4c ×2 |
| S2 | `exit_code()` stops mapping NOT_MEASURED → 3 | point 2 | **RED** 41/**1** — M9 |
| S3 | `load_specs()` stops refusing a self-anchored spec | `#1794`, load-time | **RED** 41/**1** — M6 |
| S4 | the producer-integrity hash check removed | `#1794`, run-time | **RED** 41/**1** — M8 |
| S5 | the mutation-applied count check removed | no-op mutants | **RED** 39/**3** — M5, M5b, M9 |
| S6 | the CONTROL-anchor check removed | `#1784` | **RED** 40/**1** — M7 crashes, reported as `FAIL` |
| S7 | the MUTANT-anchor check removed | `#1582` | **RED** 40/**1** — M7 crashes, reported as `FAIL` |
| S8 | the control's exit code ignored | point 1 | **RED** 40/**2** — M3 ×2 |
| S9 | the restore in `finally` removed | tree safety | **RED** 12/**9** — every leg refuses with `FAILED TO RESTORE` |

Restored, the selftest is green again — the control for the probe itself. **Three of these probes found
real defects in this work**, which is the entire argument for running them rather than reasoning about
them:

- **S6/S7 found that the selftest CRASHED rather than reporting.** Removing either anchor check made
  `adjudicate()` raise `AttributeError`; the run exited 1 — red, but by a **crash with no tally**,
  which is the very conflation `#1812` records, committed by the file that exists to enforce the
  distinction. Every leg is now isolated: a crashed leg prints `FAIL` and the run still reaches its
  tally. That is why S6 and S7 above read as reported failures rather than tracebacks.
- **S1 found that leg M4b did not exist.** A harness mutant that replaced `_fired()` wholesale still
  passed the suite, because M4 was catching it one step earlier — at the anchor — leaving the
  discriminator itself unexercised. M4b now drives a mutant that reaches the guard, reports a CLEAN
  tally, and exits non-zero anyway.

**One further defect was found by running the harness on real gates, not by reading it:**
`tests/skill-union/run.sh` prints **four** tally lines, and the discriminator was reading the first
match in the raw output while the anchor had matched a different one — so a genuinely caught mutant
came back "contradictory: exited 1 but 0 failed". The refusal was correct; the question was wrong. The
tally is now read out of the **anchor's own match**, so a fixture with several tallies must name the
authoritative one. Leg **M4c** holds that fix in place.

### What the harness will not do

It never deletes, disables or weakens anything. It mutates one file, runs one command, and restores in
a `finally` — verified by hash, and the run aborts if the restore does not take. `git status` was clean
after every sweep in this report. A `DECORATIVE` verdict is the *justification* for a removal or
repair, filed and decided separately (`#1810` AC3); **"mutating it was hard" is a `NOT MEASURED` with a
reason, never a licence.**

---

## What this changes

`#1582` asked which gates have ever produced a finding, and its honest answer — 30 that never had —
was **an unmeasured claim of safety, not evidence of safety.** For 17 of the 25 in `.github` it is now
measured, and the answer is the good one: they fire. The eleven negative controls in particular are
real, so the greens of the gates they guard now mean something.

The result that would have been most valuable is the one that did not occur: **no DECORATIVE gate was
found.** That is worth stating precisely, because the ten dead checks of 2026-07-27/28 make the
opposite prior reasonable. It does not mean the fabric is healthy — it means these 17 are, and that
8 more in this repo and 5 outside it are still unmeasured, which is exactly what `#1829` and `#1830`
are for.

## Filed from this work

- **`#1829`** — adjudicate the remaining **8** never-red gates in `.github` (six have local fixtures
  and were simply not reached).
- **`#1830`** — adjudicate the **5** never-red gates outside `.github`, including
  `FS.GG.Net/gate.yml` (a repo's entire gate job) and `FS.GG.Rendering/skill-view-check.yml` (the
  successor to the caller whose invariants `#1715` found tautological).
