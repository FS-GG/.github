# ADR-0044: Generated artifacts are derived from their generators, not declared

- **Status:** Accepted
- **Date:** 2026-07-17
- **Affects:** FS-GG/.github (the protocol, the engine, the generators); every repo that runs `verify-paths`

## Context

[ADR-0021](0021-parallel-intra-repo-work-claim-worktree-touchset.md) §3 makes a `Paths:` touch-set the
declaration of what an item touches, and `verify-paths` reports a PR that changes a file outside it as
`DRIFT`.

[.github#309](https://github.com/FS-GG/.github/issues/309) then added a rule on top: **do not reserve a
generated, CI-gated artifact in a touch-set.** Nobody authors such a file — a checked-in generator emits
it and a CI gate reds on any diff — so a collision in it is a rebase, not a decision. Reserving one
serialises every item that regenerates it; before the rule, one `readiness/surface-baselines/<pkg>.txt`
held every `[core]` item in FS.GG.Game behind a single worker.

The rule works and it left a hole, deliberately. Workers now *exclude* the artifact and then *regenerate*
it, so `verify-paths` reports it as drift — correctly, and forever. The advisory now fires on the
behaviour the protocol mandates:

| what happened | `verify-paths` says | what it means |
|---|---|---|
| you edited a file you never declared | `DRIFT` | a real finding — you should have widened |
| you regenerated the artifact §1 told you not to declare | `DRIFT` | expected, per #309 |

[.github#498](https://github.com/FS-GG/.github/issues/498) is that hole. The cost is not the noise, it is
the **habituation**: a worker told to expect `DRIFT` on every template-touching PR stops reading the list,
and the one time it names a real overrun, nobody looks. That is the
[#266](https://github.com/FS-GG/.github/issues/266) family arriving at the advisory — the gate still
reports, but it has stopped carrying information.

FS.GG.Rendering#436 is the measured instance: `verify-paths` drifted on
`template/skill-manifest/skill-manifest.json` and `docs/reports/skills-parity.md`, both correctly left
undeclared, both with a checked-in generator and a CI gate. The PR did nothing wrong.

## Decision

**`verify-paths` subtracts the artifacts the GENERATORS THEMSELVES name.** The set is *derived*, by asking
each generator; it is never declared, and no file lists it.

1. **Every generator answers `--list`**, writing nothing, in the format `generate-projections` already
   uses: `kind<TAB>path<TAB>marker`, one row per artifact, paths repo-relative.

2. **An EMPTY marker means the whole file is generated** — nobody authors it, so `verify-paths` may
   subtract it. **A marker names a generated REGION inside a file somebody authored**, which stays
   declarable and whose drift stays a true finding. This is #309's authorship test, in the wire format.

3. **`scripts/generated-paths` unions the roster** and prints the subtractable set.
   `scripts/check-generator-list.py` and `tests/generator-list/run.sh` hold every generator to the contract.

4. **An empty, absent, or failing `--list` subtracts NOTHING** — drift stays reported exactly as it is
   today.

5. **The gated half of #309's test is proven BY EXECUTION**, in the fixture: dirty the artifact, run its
   guard, assert it reds.

### There is still only ONE declaration surface, and that is the point

#498 asked for a record of *"why there are two declaration surfaces and which one wins"*, and both options
it filed would have created one. **The chosen option creates none.** `Paths:` remains the single, whole
declaration of what an item touches, exactly as ADR-0021 §3 has it — **so this record does not amend
ADR-0021.** What is derived here is not a declaration about an item at all; it is a fact about the
*repository*, which ADR-0021 never spoke to and does not have to.

The one refinement worth stating plainly, because it was always implied and never written: a touch-set
declares what an item **authors**, not what its diff happens to contain. A regenerated artifact is
touched and not authored. #309 decided that; this record just names it.

## Consequences

- **A worker who regenerates a CI-gated artifact gets a clean `verify-paths`.** The advisory means
  something again, so the drift list is worth reading.
- **A worker who edits a file they never declared still gets the finding** — and now it is not buried
  under expected noise.
- **Adding a generator obliges you to add its `--list` invocation** to `generated-paths`' roster. Forget,
  and the gate says so; if it somehow does not, the failure is a drift line you already see today.
- **A generated REGION does not make its file undeclarable.** Every `SKILL.md` and
  `docs/coordination/parallel-work.md` carries one, and all of them remain authored, declarable, and
  drift-reported.
- **`generate-skill-union-bundle` refuses unknown arguments now.** It parsed them as
  `[ "${1:-}" = "--check" ] && CHECK=1`, so every argument that was not exactly `--check` fell through to
  the **write**: asking that generator a question regenerated the bundle. The convention's first act was
  to walk into that, which is why the gate asserts refusal rather than trusting it.
- **The roster in `generated-paths` is the one thing still written by hand.** That is deliberate, and its
  failure direction is the whole argument — see below.

## Alternatives considered

**1. A `Generated:` line in the issue body**, parsed exactly as `Paths:` is. #498's own body names the
cost: *"every item that regenerates the same artifact must repeat it"* — a hand-copied list, per item,
forever. It also answers a repo-level question with a per-item field: whether `registry/repos.lock` is
generated is a fact about the repo, and it does not become a different fact because a different worker is
asking.

**2. A repo-level ignore list (`.fsgg/generated-paths`).** The filer's own weak preference, and
**disqualified on the cost #498 itself states**: *"a stale entry silently suppresses a real drift
finding — a fail-open surface of exactly the kind this org keeps finding late."* That is #266's family,
and it would have entered through the change meant to make drift reporting trustworthy. The suppression
would be invisible: nothing distinguishes "no drift" from "drift, suppressed by a line nobody has read in
a year."

**Both are a second copy of a fact the generator already holds**, and that is the actual defect. This repo
has already paid for it once, in the file this decision leans on. `generate-projections`' own comment:

> *"the first copy of that list drifted into `projections.yml` and had to be edited in step, by hand"*

The fix then was to read the generator's own list. Re-introducing that copy in order to solve a problem
*caused by* generated artifacts would be an unusually pure mistake. #609's *"an import cannot drift by
construction"*: the generator cannot forget what it emits, because emitting it is what it does.

**3. Reporting drift in two sections (`regenerated (expected):` vs `undeclared (review):`)** — suggested on
#498, and tempting because it looks like it dodges this decision. It does not. You still have to know
*which* files are generated, so it needs the same derivation; it only changes what is done with the
answer. Worth naming because it is the shape a worker in a hurry reaches for.

**4. Deciding "is it CI-gated?" by grepping workflow `run:` text** for `scripts/<x> --check`. Unsound, and
this repo has the measurement: `check-paths-coherence.py` records that scanning `run:` blocks for repo
paths returns three hits against this repo and **all three are false** — `scripts/fsgg-coord` inside a
YAML comment. A parser cannot tell a MENTION from a USE
([#683](https://github.com/FS-GG/.github/issues/683)). It would also find nothing for
`registry/repos.lock`, whose guard is `repos.sh validate` and not a `--check` at all. So the gated half is
proven by execution instead — a generator's claim is not self-certifying, but a claim something *runs* is
no longer a claim.

**5. Deriving the ROSTER too**, so nothing is hand-kept. The engine cannot: it has no YAML reader and
[ADR-0042](0042-the-chore-lock-ref-is-embedded-beside-the-roster.md) decided it must not, because the shim
ships to receivers without the roster. A filesystem convention (`scripts/generate-*`) does not fit either
— `repos.sh` is a multi-command tool whose generator is the `relock` **subcommand**, and it already has an
unrelated `list` subcommand of its own.

So a roster of **invocations** is written by hand, and it is kept because of how it fails, not despite it.
**A roster of generators is not a roster of artifacts:**

| forgotten entry | consequence |
|---|---|
| an **artifact** (alternative 2) | drift on it is silently suppressed. **Fail-open, invisible.** |
| a **generator** (this decision) | its artifacts are not subtracted, so a worker sees the expected-drift line they see today. **Fail-closed, and the failure is noise — which someone eventually complains about.** |

#498 *is* that complaint. The copy that remains is the one whose staleness cannot hide — and it is gated
anyway, because "fails safe" is a reason to allow a copy, not a reason to stop watching it.
