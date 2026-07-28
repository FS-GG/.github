# The eight gates `#1810` could not measure — 2026-07-28

**Subject:** the **8** workflows in `FS-GG/.github` that `#1810` left explicitly **NOT MEASURED** out
of `#1582`'s 25 never-red gates. `#1829`.
**Method:** unchanged — `#1810`'s decision, `#1808`'s four-point specification, and the harness that
landed at `656c401`. **No second harness was written.** `scripts/lib/mutation.py` and
`scripts/gate-mutate.py` are untouched by this work; only the corpus grew.
**Reproduce:** `scripts/gate-mutate.py --only <id>` for any single leg, or
`scripts/gate-mutate.py --only publish-flags --only preset-repo-scope --only skill-view --only project-field-options --only parity-fixtures --only drivers-package --only touch-set-drift/no-verdict`
for wave 3 as a set. It exits **0**.

## Verdict

**7 of the 8 adjudicated. All 7 JUSTIFIED. 0 DECORATIVE. 1 still NOT MEASURED.**

| verdict | n | meaning |
| --- | --- | --- |
| **JUSTIFIED** | **7** | fired under mutation — it demonstrably still protects what it claims to. **Keep.** |
| **DECORATIVE** | **0** | none found. |
| **NOT MEASURED** | **1** | `cross-repo-request-predicate.yml`. **Not a pass, and not grounds for removing anything** (`#266`). |

Combined with `#1810`: **24 of `.github`'s 25 never-red gates are now adjudicated, all 24 JUSTIFIED,
zero DECORATIVE.** The five outside `.github` remain `#1830`'s.

**Nothing was removed, disabled or weakened**, and there was nothing to remove.

### The removal constraint, tightened mid-run

`#1829`'s brief originally said a gate that cannot fire is *"removed or repaired."* **That was
withdrawn by the repository owner while this ran**, and the standing rule is now the one `#1830`
already had: **a DECORATIVE verdict is FILED as its own row, with its mutation evidence and — if the
cause is visible — its diagnosis. The gate stays in place.** Repair is not the adjudicator's either;
`#1715`'s union gate turned out to want retiring rather than repairing, and that only became clear on
a second look.

The reasoning is worth keeping next to the result. `#1810` measured 17/17 JUSTIFIED, so **a
DECORATIVE verdict would be the first in the fleet** — the surprising outcome, and surprising
outcomes deserve a second reader before an irreversible action. On 2026-07-28 alone, four harnesses
mis-reported their own results (`#1784`, `#1582`, `#1790`, `#1794`); every one was caught, and only
ever by someone re-measuring. A wrong DECORATIVE verdict deletes a working gate.

**This run found none, so the constraint was never exercised** — recorded because a reader should not
have to infer from an empty set whether the rule was in force.

---

## The seven, with what each one asserts

`#1810`'s closing note is the reason this table has a "what it asserts" column at all:
*"'mutation-proven 2026-07-28' alone will rot exactly like the eleven never-fired selftests did."* A
future reader needs the SUBJECT to re-judge the gate against a changed tree.

| verdict | gate | runs | what the gate asserts | what the mutation broke | control → mutant |
| --- | --- | --- | --- | --- | --- |
| **JUSTIFIED** | `touch-set-drift.yml` | 1265 | that a PR whose touch-set could not be **checked at all** never shares a green with one that was checked and is clean (`#318`) | the `#318` fail-closed else-branch made unreachable: an rc-0 `verify-paths` printing no `FSGG-PATHS` marker is classified `skip`, which deletes the sticky comment and passes | 14/0 → 11/**3** |
| **JUSTIFIED** | `drivers-package.yml` | 89 | that the published `FS.GG.Drivers` nupkg carries the manifest **and every driver skill file**, each byte matching its ADR-0014 recorded sha256 | the pack hook's recursive glob (`/**/*` → `/*`), so the package ships the manifest and **zero** driver bytes | `OK` → `FAIL — nupkg is missing drivers/skills/work-roadmap/SKILL.md` |
| **JUSTIFIED** | `parity-fixtures.yml` | 60 | that all 46 coord-engine parity fixture servers hold the `#939` framing properties — HTTP/1.1 over `ThreadingHTTPServer`, no body on a 204, every body-bearing handler drains | one fixture dropped to HTTP/1.0, which closes the connection per response and reds the corpus **at random** (`#761`) | `OK — 46 fixture(s)` → **`1 finding(s) across 46 fixture(s)`** |
| **JUSTIFIED** | `project-field-options.yml` | 55 | that the board's bounded `Repo Scope` and `Class` option sets match the roster and the closed `ItemClass` vocabulary, and that an unrecognised `--field` **refuses** rather than answering about a field nothing examined (`#1588`) | the `--field` fence removed, so `check --field Severity` falls through to the Repo Scope leg and prints a green verdict about `Severity` | 17/0 → 16/**1** |
| **JUSTIFIED** | `publish-flags.yml` | 53 | that `vars.NUGET_ORG_PUBLISH` is **`true`** wherever a workflow gates a nuget.org publish on it (`#750`) | the empty string — an UNSET repo variable, which is `#750`'s literal hazard — accepted as if it were `true` | 12/0 → 9/**3** |
| **JUSTIFIED** | `preset-repo-scope-coherence.yml` | 28 | that every `enabled: false` + `matchFileNames` rule in `default.json` names **exactly** the roster's receivers for the fabric it declares, and that a kit-delivered file is scoped to the kit (`#1552`, `#1798`) | rule 4 retired: a kit-delivered file scoped to another fabric now passes, re-enabling Renovate on a materialized file in every kit-but-not-that-fabric receiver (`#1615`'s six-hour outage) | `OK` → **`4 FAILURE(S)`** |
| **JUSTIFIED** | `skill-view.yml` | 11 | that a runtime skill root which is absent, **dangling**, or a checked-out symlink body is reported LOUD and by its own diagnostic class, instead of resolving zero skills and exiting 0 (ADR-0067 §8) | `classify_root` collapsing `dangling-link` into `absent` — and `absent` is excusable by `--absent-ok`, so the collapse turns a zero-skill runtime into a green | 43/0 → 41/**2** |

**Reading the last column:** `<passed>/<failed>` from the fixture's own tally, unmutated → mutated;
or the terminal report line where the command counts no assertions.

### Three of these had no obstacle, only an unread step list

`#1829` recorded `project-field-options.yml` as *"reads live board field options; needs a board
fixture"*. It does read the live board — and the workflow's **first step** is
`bash tests/project-field-options/run.sh`, which is offline and drives the same tool. The board
fixture was never the obstacle. Likewise `parity-fixtures.yml` was recorded as blocked on
`tests/coord-engine-parity` being held by `#1794`; the mutation needs that tree only for the seconds
the harness holds it, and restores it in a `finally`. `drivers-package.yml` was recorded as having
*"no local fixture identified"*; `src/FS.GG.Drivers/verify-package.sh` is the fixture, it is what the
workflow runs, and it runs offline in ~2 s once `obj/` is warm.

That is worth recording as a pattern, not an apology: **"not reached" decayed into "hard" in the
space of one report.** `#266`'s rule protects against the opposite error, and this is its mirror —
an unmeasured thing acquiring a reason it never had.

### `drivers-package` needed the mutation to be chosen, not guessed

The obvious mutation — tamper a driver byte — **does not measure this gate**, and the harness says so
rather than crediting it. `stage-drivers.py` re-verifies every source digest and `die`s, so
`verify-package.sh` aborts under `set -e` **before printing any verdict of its own**. The workflow
would be red, but red without the script's own words is exactly `#1812`'s crash/finding asymmetry, and
the leg would grade `NOT_MEASURED`. The mutation that landed breaks the pack hook instead, which is
caught downstream by `verify-package.sh`'s own `fail()` — so the mutant's anchor is the gate's own
sentence naming the missing file. Recorded because the first choice looked obviously right.

---

## `touch-set-drift.yml`: what "fires" means for an advisory gate (`#1829` AC4)

`#1829` AC4 asks for **a stated, defended notion of "fires" for an advisory gate, or NOT MEASURED with
that reasoning — not a silent grade against the job conclusion.** Here is the notion, and it is
adjudicated, not deferred.

The gate is advisory **by decision** (ADR-0021, ADR-0027 §8): `verify-paths --warn` exits 0 on every
verdict, and the drift finding is a sticky **comment**. So *"has the job ever been red?"* is the wrong
question about the finding — **"never red" is its spec**, exactly as `#1810` said. But the gate has
**two** outputs, not one, and both are measurable:

**(a) Does the advisory VERDICT still discriminate?** — **Already answered, before anyone asked.**
Corpus leg 1 (`touch-set-drift/invalid-arm-misclassifies`, `#1810` wave 1) mutates
`.github/workflows/touch-set-drift.yml` — *this gate's own file* — so an unmatchable `Paths:`
declaration (`#273`) is reclassified as a clean pass, and `tests/touch-set-drift/run.sh` reds. The
classifier that decides ⚠️ / ⛔ / ✅ is mutation-covered. `#1810` filed this gate as unmeasured while
its own leg 1 was already measuring half of it; the two rows were never reconciled.

**(b) Can the JOB go red at all?** — Yes, and there is exactly one lane: `#318`'s fail-closed
branch. *"The touch-set was checked and is fine"* and *"the touch-set was never checked"* must not
share a green. Leg 24 removes that branch and the fixture reds (14/0 → 11/**3**).

So the defended notion is: **an advisory gate fires when its VERDICT discriminates and its
NO-VERDICT lane still fails closed.** Both halves hold under mutation. Its 1265 clean runs are its
specification being met, not evidence of decay.

**What this does NOT establish** (`#1810`'s own limit, applied to itself): that the comment is ever
*read*. Mutation proves the gate can say the right thing; nothing here proves anybody acts on it. That
is `#1611`'s category-D question and it is not this method's to answer.

---

## NOT MEASURED — 1, and a sharper reason than `#1810` had

**`cross-repo-request-predicate.yml`** — 725 retained runs, 0 red.

`#1810` recorded the obstacle as *"a faithful mutation needs a multi-repo fixture that does not exist
yet."* **That is not the obstacle, and the multi-repo fixture does exist.**
`tests/registry-predicate/run.sh` builds a synthetic `$FSGG_REGISTRY` and `$FSGG_REPOS_ROOT` with
producer manifests and drives the compiled `fsgg-coord-engine predicate` over them, offline, including
the `#1194` contradiction. It is run by **`coord-engine.yml`**, not by this workflow.

The real obstacles are two, and they are worth more than the one they replace:

1. **This workflow's job conclusion cannot carry its finding.** Its oracle step runs under
   `set -uo pipefail` — no `-e` — captures the oracle's rc into a variable, and ends on a `for` loop
   that exits 0. The finding is an **auto-comment**, gated on
   `steps.predicate.outputs.verdict == 'contradicts'`. So, like `touch-set-drift`, **"never red" is
   its specified behaviour**; its red surface is engine build, `git clone`, and `gh` failures — none
   of which is a verdict about the subject. Recording it under "needs a multi-repo fixture" hid this.

2. **Nothing drives this workflow's YAML.** The oracle is covered (`tests/registry-predicate`,
   `RegistryPredicateTests`); the **wiring** from the oracle's `contradicts` through
   `jq -r '.verdict'`, `$GITHUB_OUTPUT`, and the comment step's `if:` has no negative control at all.
   That wiring is what `touch-set-drift` has a fixture for and this does not. Building it is the
   honest unit of work, and it is filed rather than faked.

**Third, and separate from measurability:** **693 of this workflow's 725 retained runs are
`skipped`.** It triggers on every `issues: [opened, edited]` event and its job is `if:`-gated to the
`cross-repo:request` label, so only **32** runs ever executed a step. `#1582`'s "711 runs and not one
red" was counting 693 runs in which nothing was evaluated. Measured by run id
`gh api repos/FS-GG/.github/actions/workflows/cross-repo-request-predicate.yml/runs --paginate`,
2026-07-28: `693 skipped, 32 success, 0 failure`. Filed separately — it is a defect in the
measurement every verdict on this board leans on, not in this gate.

**None of this is grounds for removing it.** `#1810`'s decision is explicit: *"Do not remove a gate
merely because mutating it is hard — say so and leave it."*

---

## What this changes

* **24 of `.github`'s 25 never-red gates are adjudicated and every one fired.** After 24 legs across
  two waves, **zero DECORATIVE**. The ten dead checks of 2026-07-27/28 were found in code, not in
  workflow gates; on this evidence `.github`'s gate fabric is not where that class lives.
* **Two of the 25 are advisory by design** (`touch-set-drift`, `cross-repo-request-predicate`), and
  for both, "never red" is the specification rather than a symptom. That distinction did not exist in
  `#1582`'s ledger, which grades a workflow purely on run conclusions — so an advisory gate is
  structurally guaranteed to appear in its NEVER-FOUND list forever.
* **`totalRuns` counts `skipped`.** `check-gate-finding-history.py` correctly excludes non-verdict
  conclusions from `findingRuns`, but `totalRuns` is every run — so an `if:`-gated workflow looks
  well-sampled when it barely ran. `cross-repo-request-predicate` is the measured instance (32 real
  runs presented as 711).
* **Still nothing removed.** `#1810` AC3 and `#1829` AC3 both hold: no gate was deleted, disabled or
  weakened by this work, and no verdict here licenses one.

## What this does NOT establish

`#1810`'s note applies unchanged and is repeated here so this report cannot be read as more than it
is: **JUSTIFIED means the gate fires when its subject breaks. It does not mean the gate asks the
right question.** `#1715`'s union gate and `FS.GG.Audio#212`'s alarm both fired correctly for a while
and became tautologies when the tree changed underneath them. Each row above records what its gate
asserts precisely so that a future reader can re-judge that, rather than inheriting a date.

## Filed from this work

* **`#1839`** — `cross-repo-request-predicate.yml` has no negative control for its own wiring, and the
  multi-repo fixture `#1810` said was missing already exists (`tests/registry-predicate/run.sh`, run
  by `coord-engine.yml`). Build the fixture that drives its YAML the way `tests/touch-set-drift/run.sh`
  drives that gate's.
* **`#1840`** — `check-gate-finding-history.py` counts `skipped` runs in `totalRuns`, inflating the
  sample size for `if:`-gated workflows past the `MIN_RUNS` floor that exists to prevent exactly that
  reading, and has no verdict for a gate whose finding is a comment rather than a conclusion.
  Distinct from `#1812`, which is about the numerator.
