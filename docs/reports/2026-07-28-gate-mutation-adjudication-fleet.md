# Adjudicating the never-red gates OUTSIDE `.github` by mutation — 2026-07-28

**Subject:** the **5** never-red workflows `#1582` measured outside `FS-GG/.github`, filed as `#1830`.
**Companions:** [`2026-07-28-gate-mutation-adjudication.md`](2026-07-28-gate-mutation-adjudication.md)
(`#1810`, 17 of the 25 inside `.github`) and
[`2026-07-28-gate-mutation-wave3.md`](2026-07-28-gate-mutation-wave3.md) (`#1829`, 7 of the remaining
8). **This report is the fleet answer's last piece and links the other two, so the whole answer lives
in one place** (`#1830` AC6).
**Method:** unchanged — break the thing each gate claims to protect, and watch whether it fires.
**Harness:** `scripts/lib/mutation.py` + `scripts/gate-mutate.py`, **extended here** with a second
mutation kind. What had to change, and what refused to change, is the second half of this report.
**Reproduce:** see `tests/gate-mutation/specs-fleet/README.md`. Both sweeps exit **0**.

## Verdict

**All five adjudicated. 2 JUSTIFIED. 0 DECORATIVE. 3 NOT MEASURED.**

| verdict | n | meaning |
| --- | --- | --- |
| **JUSTIFIED** | **2** | fired under mutation — it demonstrably still protects what it claims to. **Keep.** |
| **DECORATIVE** | **0** | none found. |
| **NOT MEASURED** | **3** | no measurement obtained. **Not a pass, and not grounds for removal** (`#266`). |

**Nothing was deleted, disabled or weakened, in any repository.** One finding was filed to its owning
repo (`FS.GG.Game#525`) rather than acted on from here: removal in another repository is a higher bar
than in `.github`, and this worker is not that repo's owner (`#1830` AC5).

**No repository's `main` was mutated.** Every measurement was taken in a throwaway
`git clone --depth 1` under a scratch directory, and both clones' `git status --porcelain` was **empty**
after every sweep.

---

## AC4 analogue: re-measured, not inherited — and the set changed under us

`scripts/check-gate-finding-history.py --fetch` was re-run over all five repos at
**`2026-07-28T19:11:47Z`**, 0 UNREAD. **`FS.GG.Net/gate.yml` is no longer NEVER-FOUND.**

| repo | workflow | runs (ledger → now) | verdict (ledger → now) |
| --- | --- | --- | --- |
| `FS.GG.Game` | `governance.yml` | 878 → 878 | NEVER-FOUND → NEVER-FOUND |
| `FS.GG.Templates` | `lockfile-sync.yml` | 333 → 334 | NEVER-FOUND → NEVER-FOUND |
| `FS.GG.Audio` | `lockfile-sync.yml` | 177 → 177 | NEVER-FOUND → NEVER-FOUND |
| **`FS.GG.Net`** | **`gate.yml`** | **84 → 88** | **NEVER-FOUND → EXERCISED (1 red)** |
| `FS.GG.Rendering` | `skill-view-check.yml` | 21 → 21 | NEVER-FOUND → NEVER-FOUND |

---

## The two that fired

Every leg below is re-runnable: `scripts/gate-mutate.py --root <clone> --specs
tests/gate-mutation/specs-fleet/<repo>.yml`.

### `FS.GG.Net/gate.yml` — **JUSTIFIED**

`#1830` AC3 requires this to be adjudicated as **the whole job**. `Build + test (locked restore)` is a
required status check on `main`, so all four of its assertions block a merge.

| # | what it asserts | verdict |
| --- | --- | --- |
| 1 | the committed `packages.lock.json` set reproduces the resolved graph (`--locked-mode`) | **JUSTIFIED — live production catch** |
| 2 | the solution builds Debug with warnings-as-errors | NOT MEASURED — needs a restore against the org feed |
| 3 | the Expecto suite passes | NOT MEASURED — same |
| 4 | ADR-0067 §8's runtime skill-root contract (generate → selftest → check) | **JUSTIFIED — 3 legs, all fired** |

**Assertion 1 fired for real, in production, while this was being adjudicated.** Run id
**`30379948771`**, `2026-07-28T16:48:07Z`, branch `renovate/protobuf-net.grpc.aspnetcore-1.x`. `#1812`
says a run concluding `failure` may be a crash, so the log was read rather than the conclusion:

```
error NU1004: The package reference protobuf-net.Grpc.AspNetCore version has changed
              from [1.1.1, ) to [1.2.2, ). The packages lock file is inconsistent with
              the project dependencies so restore can't be run in locked mode.
##[error]locked restore failed — resolved graph != committed packages.lock.json.
         Run: dotnet restore FS.GG.Net.slnx --force-evaluate  and commit the lockfiles.
```

The second line is the job's **own** `::error::` sentence, naming the subject defect and its
remediation. That is a gate reporting about its subject. It is stronger evidence than any mutation,
and it is the same shape as `#1810`'s live `engine-flag-narrative` catch.

| leg | broke | control → mutant |
| --- | --- | --- |
| `net-gate-skill-view-absent-root` | the generated view root; this job does **not** pass `--absent-ok`, so absence must be red | rc 0 → **rc 1** |
| `net-gate-skill-view-partial-view` | the view's COMPLETENESS — one skill omitted from a root that still resolves | rc 0 → **rc 1** |
| `net-gate-roots-declaration` | the roots DECLARATION in `.config/kit/FS.GG.Kit.receiver.proj` — the root leaves the contract while the tree still holds it, so every other kit gate stays green over it | rc 0 → **rc 1** |

**The in-gate negative control is live, measured BY HAND.** The job runs `bash scripts/skill-view
selftest` (16 lanes). Neutering `do_check`'s per-skill presence test took it from `16 passed, 0 failed`
to **`14 passed, 2 failed`**. It has no harness leg, and the reason is finding **H2** below — the
harness refuses it, correctly.

### `FS.GG.Rendering/skill-view-check.yml` — **JUSTIFIED**

`#1830` AC2 requires this to be adjudicated **against `#1715`**. The prior is not neutral: this file
is the successor to a caller whose two headline invariants had become tautologies, and the burden is
not "can it fail" but **"is it a tautology on a partial view, the way its predecessor was."**

**What it asserts** — recorded, not just that it can fail:

> Every skill the `--source` directory declares is visible at every runtime root the contract still
> names, with ADR-0067 §8's absence classes reported separately.

Two structural differences from the predecessor carry it. The expected set is `ids_from_source(--source)`
and is **never** derived from the roots — `do_check` refuses (`exit 2`) an empty expected set rather
than reporting "everything is visible" over nothing, and the predecessor's tautology began exactly
where its id set came from the thing under test. And presence is per-skill and per-**file**,
`[ -e "$root/$id/SKILL.md" ]`, not `[ -d "$root" ]`.

| leg | broke | control → mutant |
| --- | --- | --- |
| `rendering-skill-view-absent-root` | the view was never generated — the state every clean clone starts in | rc 0 → **rc 1 `[absent-root]`** |
| `rendering-skill-view-dangling-root` | the view resolves to nothing while the path is still there | rc 0 → **rc 1 `[dangling-root]`** |
| `rendering-skill-view-text-file-root` | ADR-0067 §6's `core.symlinks=false` shape | rc 0 → **rc 1 `[text-file-root]`** |
| **`rendering-skill-view-partial-view`** | **the view's COMPLETENESS: 49 per-skill symlinks where the source declares 50** | **rc 0 → rc 1 `[missing-skill]`, one line, for the one omitted skill** |

**The last row is AC2's answer.** Where the predecessor scored `50/50` over a root contributing zero
ids, the successor detects a **single** missing skill out of fifty and names it. **It is not a
tautology on a partial view.**

#### The expiry condition on this verdict, which is the point of recording the subject

`#1810`'s standing note: *"mutation-proven 2026-07-28" alone will rot exactly like the eleven
never-fired selftests did*, and two of the ten dead checks found on 2026-07-27/28 **fired correctly
for a while** and became tautologies when the tree moved underneath them.

So, stated rather than left implicit: `.agents/skills` is a whole-directory symlink to
`.claude/skills`, so a view cannot diverge from what it is a view of, and the gate deliberately does
**not** byte-compare the two roots. **If this repo ever returns to committing two independent copies,
the per-skill lane at the view root becomes 50 tests of a file against itself and this verdict must be
re-taken.** That sentence is in the spec file too, where the next reader of the corpus will find it.

---

## The three that were not measured, and why each

**`#266`: "I could not evaluate this" is NEVER "I evaluated it and it passed."** None of these is a
clean verdict and none is grounds for removing anything.

### `FS.GG.Game/governance.yml` — the never-red row can never say anything

`continue-on-error: true` is set at **both** the job level and the verdict step, so the run conclusion
is `success` unconditionally. **"878 runs, 0 reds" is a tautology of the workflow's own configuration.**
`#1582`'s method has no purchase — the same reason `#1810` recorded `touch-set-drift.yml` as NOT
MEASURED. The design is deliberate and documented; the problem is only that the row invites the reading
*"878 clean runs, so this is fine."*

The **different** question is measurable, and the answer is not good. The advisory output at both ends
of the retention window — runs **`29976156970`** (2026-07-23) and **`30371362820`** (2026-07-28) — is
**identical**: `exit: success (0)`, `stakes: routine — light, no gates`, `blocking (0)`, and **all
eleven evidence oracles reported `unavailable: missing …`**. All eleven paths are genuinely absent on
`main`. Sharpest single line: `speckit:constitution unavailable: missing .specify/memory/constitution.md`
while `.fsgg/constitution.md` **exists** — the repo declares a constitution the gate looks elsewhere for.

**It is recorded as NOT MEASURED and deliberately not as DECORATIVE.** Nothing in `FS.GG.Game` was
mutated and watched to fail to change; two logs were read and the rest is **inference**. That is
precisely the failure class `#1810`'s standing note records — *"my verification machinery caught my
machinery's failures, but nothing in it caught my reasoning's"* — and the remedy is a second party
measuring, not this worker feeling surer. **Filed as `FS-GG/FS.GG.Game#525`** with the evidence, for
that repo to settle.

### `FS.GG.Templates/lockfile-sync.yml` and `FS.GG.Audio/lockfile-sync.yml`

Both are **thin callers**: the whole body is `uses: FS-GG/.github/.github/workflows/lockfile-sync.yml@main`.
No mutation was run against either, so neither earns a JUSTIFIED. Two facts bound what is unknown, and
**neither is a verdict**:

1. **The body they call is already mutation-proven, in this repo.** `#1810` Wave 1 adjudicated
   `lockfile-cold-selftest` (`17/0` → `16/1`) and `dispatch-preflight-selftest` (`33/0` → `30/3`), and
   the mutation target of both is `.github/workflows/lockfile-sync.yml` — literally the file these two
   `uses:`.
2. **The body demonstrably executes in both repos**, which rules out the *"present on paper, never once
   executed"* shape: run **`30388549220`** (Templates) and **`30369656683`** (Audio) each ran both jobs
   and every step, including the two preflight assertions and the cold regeneration. 27 and 16
   renovate-branch runs respectively in the sampled 100.

**What is genuinely unmeasured** is the caller-owned surface, which is all a caller can get wrong: the
`permissions: packages: read` ceiling (whose absence is a `startup_failure` before the job-level `if`
is evaluated) and `restore-target:`. Both fail only on a real runner against the org feed.

> **A stale claim, corrected by re-measuring rather than repeated.** `FS.GG.Audio`'s workflow header
> states that `FS.GG.Game` omits `packages: read` and *"its lockfile-sync has therefore
> startup_failure'd on EVERY run since it was added"* (`FS.GG.Game#137`). That was true when written
> and is **not true now**: `FS.GG.Game`'s caller carries `packages: read` today and its last 20 runs
> are all `success`. Recorded here because the whole method is worthless if inherited prose is passed
> along unchecked.

---

## What had to change to run the harness outside `.github`

`#1830` predicted `--root` would be most of it. `--root` was the easy part and it works unchanged.
**Three things did not fit, and two of them are findings about the harness rather than about these
repos.** All three are specified as `#1842`; H1 and H3 are implemented here.

### H1 — every leg `#1810` and `#1829` wrote is a TEXT substitution; these gates' subjects are the TREE

`Mutation` was `find`/`replace` in one file, counted and byte-restored. Perfect when the gate is a
`scripts/check-*.py` — which is every gate in `.github`, so nobody had reason to notice.

**It cannot express a single one of the seven legs above.** The defects `skill-view-check.yml` exists
to catch are *an absent directory, a dangling symlink, a symlink checked out as a regular file, and a
view that resolves but is incomplete.* None is an edit to any file. A text-only harness pointed at
these gates returns `NOT MEASURED` for every leg, for a reason that is about the harness — and `#266`
forbids reading that as anything else.

So `MutationKind` gained a second member and `PathOp` is its **closed** vocabulary: `delete`,
`retarget`, `replace-with-file`, `partial-view`. **Deliberately not a "run this shell command" escape
hatch** — an arbitrary command keeps the ergonomics and loses two safeguards `#1810` measured to be
load-bearing (S5, the mutation-applied check; S9, the restore), because both stop being checkable.
Both stay checkable via `path_signature()` — `absent` / `link:<target>` / `file:<sha>` / recursive
`dir:<sha>`, never following a link — computed either side of the edit.

`partial-view` earns its place because a whole-directory symlink makes a partial view
**unrepresentable**: the state has to be *constructed*, not edited into place. It is the leg that
discharges AC2, and without it the vocabulary would have been three members that all break the root
outright — passing a gate that cannot see `#1715`'s actual defect.

**One property is stronger here than in the text kind, not weaker.** A path leg mutates the **subject**
(a view root) and anchors on the **gate** (`scripts/skill-view`). Those are genuinely different files,
so the producer-integrity hash is a real check rather than a formality.

### H2 — in a receiver, the gate and its negative control are the SAME FILE

In `.github` the gate (`scripts/check-*.py`) and the anchor producer (`tests/*/run.sh`) are always
different files, so specification point 3 costs nothing. In every kit receiver, **`scripts/skill-view`
is both the tool and its own selftest** — one file printing its own `16 passed, 0 failed`. A leg
mutating it and anchoring on that tally has `anchor.produced_by == target`, and `load_specs()` refuses
it.

**The refusal is correct and was not worked around.** The tempting fix — a thin wrapper re-emitting the
tool's tally as its own "terminal line" — is *anchor laundering*: it satisfies the mechanism while the
guard still authors the number, which is `#1794`'s defect wearing a second file's name.

The cost, stated: `FS.GG.Net/gate.yml`'s `skill-view selftest` step is **NOT MEASURED by the harness**,
and was measured by hand and labelled as by-hand. `#1842` AC5 settles it — with a `.github` fixture
that drives a receiver's `skill-view check` over planted trees and keeps its **own** bookkeeping, or
with a recorded decision that the leg stays NOT MEASURED. Never by relaxing the producer rule.

### H3 — one flat corpus with one implicit root

Fleet legs cannot live in `tests/gate-mutation/specs.yml`: a default `.github` sweep would report
`NOT_MEASURED — mutation target .agents/skills does not exist` for every one and turn this repo's clean
`exit 0` into a `3`. They live in `tests/gate-mutation/specs-fleet/<repo>.yml`, one file per repository,
run with an explicit `--root`. `tests/gate-mutation/run.sh` **loads and validates** them on every PR —
loading is pure and instant, and it is the half that catches a spec which could never yield a
measurement. **A harness that only works in one repo is a finding about the harness**; this is the
mildest of the three, a packaging change rather than a design change.

### The trap that would have silently disarmed three of the four Rendering legs

A view root is git-ignored, so a fresh clone has none and the unmutated control is red — correctly
reported as `NOT MEASURED`. The obvious fix is to put `skill-view generate` into the leg's `command:`.
**That must never be done**, and the corpus README says so at length: the generate would rebuild the
very root the mutation just deleted before the gate ever looked at it, and `delete`, `retarget` and
`replace-with-file` would all become unmeasurable at once. `partial-view` would survive, because
`generate` is a no-op over a root that already resolves — **so the sweep would still look like it had
run, with three legs quietly measuring nothing and one still passing.** That is the exact fail-open
shape `skill-view-check.yml`'s own header warns about, and committing it in the file that exists to
detect it would have been the eleventh entry on the list.

---

## Holding the new kind to the standard the old one is held to

`#1810` removed all nine of the harness's safeguards in turn and measured every removal to red the
selftest. The same was done for the path kind's four. Unmutated, the selftest reports
**89 passed, 0 failed** (up from 42 — the new legs are `P1`–`P5`).

| # | safeguard removed | measured result |
| --- | --- | --- |
| S10 | the path **no-op** check (`path_signature(target) == sig_before`) | **RED** 87/**2** — P3b |
| S11 | the path **restore** (the stashed original is never renamed back) | **RED** 61/**2** — every path leg refuses, `FAILED TO RESTORE` |
| S12 | the **precheck ordering** — inspect the target after stashing rather than before | **RED** 81/**8** — P1, P2 |
| S13 | the **closed vocabulary** — an unknown `op:` silently accepted | **RED** 86/**2** — P4 |

**Two of those four probes found real defects in this work**, which is the entire argument for running
them rather than reasoning about them:

* **S12 was a real bug, and it shipped in the first cut.** The original code stashed the target and
  *then* asked the op whether it could proceed — so `retarget` and `partial-view`, the two ops that
  must read the original, inspected an already-empty path and refused with
  `needs view to be a symlink, and it is not (absent)`. The P1/P2 legs caught it. **The failure was in
  the safe direction** — `NOT MEASURED`, never a false `JUSTIFIED` — and therefore completely invisible
  in any sweep that did not look at the verdicts: `partial-view`, the one op `#1715` makes necessary,
  would have measured nothing forever while the file that contained it looked correct.
* **S10 found that leg P3b did not exist.** Removing the path no-op check left the selftest at
  **86/0** — a safeguard with no negative control, which is `#1810`'s S1/M4b shape exactly, in code
  written by someone who had just read that entry. P3b now drives `retarget` at a symlink's *existing*
  target: the loader cannot catch that (catching it means reading the tree, and `load_specs` is pure),
  so the run-time check is the only thing standing between "the gate held" and "nothing was broken" —
  and those are opposite facts.

**A third defect was found by running the corpus rather than by reading it.** The first sweep returned
`NOT_MEASURED` × 4 for Rendering: the anchor pattern `roots='.*' expecting` never matched, because the
real line carries ` (from default (ADR-0065's two)) ` between them. The harness reported *"the anchor
did not match the GREEN control run — the anchor is wrong, so no run can be checked against it
(#1784)"* and graded nothing. **`#1784`'s check, working, on the first corpus written against it.**

---

## Filed from this work

* **`FS-GG/FS.GG.Game#525`** — `governance.yml` cannot conclude `failure` by construction, and its
  advisory verdict has been byte-identically vacuous across the retention window. **Filed in the
  owning repo, with the evidence; the removal-or-repair decision is theirs.**
* **`#1842`** — the harness findings H1/H2/H3 as a specified item. H1 and H3 are implemented here;
  **H2 is not**, and AC5 is where it gets a real anchor or a recorded decision.
* **`#1843`** — `#1829` and `#1830` were filed as parallel rows and are a lane of one: a directory
  token swallows every file beneath it, so the narrower row cannot narrow out of the collision
  (`#1732`, one level in). This work waited on that lane and says so.

## What this changes

`#1582`'s fleet figure was **30 never-red gates across 8 repos**. With `#1810` (17 of the 25 in
`.github`), `#1829` (7 of the remaining 8, all JUSTIFIED) and this report (all 5 outside), **every one
of the 30 now carries a verdict**:

| | JUSTIFIED | DECORATIVE | NOT MEASURED |
| --- | --- | --- | --- |
| `.github` (25) | 24 | 0 | 1 — `cross-repo-request-predicate.yml` |
| outside `.github` (5) | 2 | 0 | 3 — `governance.yml`, both `lockfile-sync.yml` callers |
| **fleet (30)** | **26** | **0** | **4** |

**Four is not zero and is not rounded down.** Each of the four is named with its reason above or in
`#1829`'s report, none is a pass, and none is grounds for removing anything.

The result that would have been most valuable is again the one that did not occur: **no DECORATIVE
gate was found.** That is worth stating precisely, because `FS.GG.Rendering/skill-view-check.yml` was
the single most suspect gate in the fleet — the direct successor to a caller whose invariants were
tautologies — and it is not one. **It does not mean the fabric is verified.** It means these seven
legs fire, that three of the five gates are still unmeasured for named reasons, and that the harness
which produced the answer had two defects in it this morning that only mutation found.
