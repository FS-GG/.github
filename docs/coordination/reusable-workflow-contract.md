# The reusable-workflow contract

What a `workflow_call` workflow in `FS-GG/.github` owes its callers, and what it may not change
without breaking them.

Contract: the reusable-workflow fabric ([ADR-0006](../adr/0006-org-shared-dotnet-build-config-and-unified-restore-locked-mode-gate.md)).
Related: [.github#478](https://github.com/FS-GG/.github/issues/478),
[#482](https://github.com/FS-GG/.github/issues/482),
[#541](https://github.com/FS-GG/.github/issues/541),
[#549](https://github.com/FS-GG/.github/issues/549).

## Why this page exists

A reusable workflow looks like an internal implementation detail of the repo that hosts it. It is
not. Receivers pin it at a **moving ref** (`@main`), so every merge here ships straight into six
repos' CI, and several things a casual reader would call "internal" are in fact **API**.

The org has now found the same shape of bug four times, each time from the receiving end, each time
after it had already shipped: **a coupling across the `workflow_call` boundary that neither side can
see, and that no check asserted.** They are collected here so the fifth one is found by reading this
page rather than by a repo that stops merging.

## The surface

| What the callee declares | What breaks if it changes | Asserted by |
|---|---|---|
| **`inputs:` / `outputs:`** | A caller passing a removed input fails to start. The obvious one; nobody gets this wrong. | GitHub itself (validation error) |
| **`permissions:`** | A callee cannot request a permission its caller did not grant — the token is the **intersection**. GitHub kills the run at *startup*, which is neither red nor green, so it reads as "the gate is present" while it has never once executed. FS.GG.Game's lockfile-sync caller under-granted `packages: read` and **all 119 runs** ended that way. | [`permission-coherence.yml`](../../.github/workflows/permission-coherence.yml) (#478) |
| **Secrets / feed auth** | A callee that needs a credential the caller never forwarded cannot authenticate. | #482 |
| **`timeout-minutes`** | A caller **cannot** supply one: `timeout-minutes` is not a legal key on a `uses:` job. An unbounded job in a callee exports GitHub's **360-minute** default into every repo that adopts it, and the receiver has no way to bound it. | [`timeout-coherence.yml`](../../.github/workflows/timeout-coherence.yml) (#541) |
| **Job ids** | ← **the one this page is really about.** See below. | [`reusable-job-id-coherence.yml`](../../.github/workflows/reusable-job-id-coherence.yml) (#549) |

## A reusable workflow's job ids are API

When a caller invokes a reusable workflow, GitHub names the resulting check run:

```
<caller's job id or name>  /  <callee's job id or name>
```

FS.GG.Audio's gate calls this repo's lock-range gate:

```yaml
# FS.GG.Audio  .github/workflows/gate.yml
jobs:
  lock-ranges:                                                  # <- caller's job id
    uses: FS-GG/.github/.github/workflows/lock-range-coherence.yml@main
```
```yaml
# FS-GG/.github  .github/workflows/lock-range-coherence.yml
jobs:
  lock-ranges:                                                  # <- callee's job id
```

so the check reports as **`lock-ranges / lock-ranges`** — and **that exact string is what
FS.GG.Audio's branch protection requires.**

**Half of a required status check in another repo is a job id in this one.**

### The failure

Rename `lock-ranges:` here. It is an ordinary refactor of a workflow's own job id, in a repo whose
CI is green, reviewed by people with no reason to look downstream. Audio's gate now reports
`lock-ranges / <newname>`. The context Audio **requires** is never reported again, so GitHub holds
every pull request at *"Expected — waiting for status to be reported"* — **forever**.

Audio runs `enforce_admins: true` with no required reviews, so **there is no bypass**. Every PR to
Audio's `main` deadlocks. No commit in Audio changed. The cause is a commit in a different repo, and
the only way out is an admin editing Audio's protection settings — after somebody works out why the
whole repo stopped merging.

A rename used to be harmless: it relabelled an *advisory* check. It became a repo-wide outage the
moment a caller **required** the nested context, and FS.GG.Audio is the first FS-GG repo to do so.
FS.GG.SDD and FS.GG.Rendering require only flat, locally-defined contexts today, so neither is
exposed — and both will be the moment they require a shared gate's context, which is the direction
the org is already moving by lifting checks into reusable workflows.

### The rule

> **Renaming — or removing — a job in a `workflow_call` workflow is a BREAKING CONTRACT CHANGE.**
> Treat it like any other: it is not a refactor, and "the CI here is green" does not mean it is safe.

Before you rename one, find out who requires it — **from BOTH places GitHub keeps required checks.**

```sh
# Which repos require a context whose right-hand half is this job?
# CLASSIC branch protection (needs `administration: read`):
gh api repos/FS-GG/<repo>/branches/main/protection --paginate \
  --jq '.required_status_checks.checks[].context'

# RULESETS — a SEPARATE store the line above does not report (needs only `metadata: read`):
gh api repos/FS-GG/<repo>/rules/branches/main --paginate \
  --jq '.[] | select(.type == "required_status_checks")
            | .parameters.required_status_checks[].context'
```

**Asking only the first is how you get a confident, wrong "nobody requires it".** The two endpoints
read different stores and neither reports the other's rules; a branch may be governed by either,
both, or neither, and GitHub enforces both. **FS.GG.Governance is protected by a ruleset and answers
404 on the classic endpoint** — so the first command alone reports *nothing* for a repo that requires
five status checks. That is not a hypothetical: it is precisely how
[`check-required-contexts.py`](../../scripts/check-required-contexts.py) came to report
`requires NO status checks` over it, holding an admin token
([#574](https://github.com/FS-GG/.github/issues/574)).

If any receiver requires `<something> / <the job you are renaming>`, the rename must be sequenced:
land the receiver's protection change first, or do not rename it.

## What asserts it

[`reusable-job-id-coherence.yml`](../../.github/workflows/reusable-job-id-coherence.yml) runs on
every PR **to this repo**. For each workflow that declared `on: workflow_call` at the merge-base, it
asserts that **every context name it published then, it still publishes now** — catching all four
ways to break a caller:

| Change | Breaks a caller? |
|---|---|
| Rename a job **id** | **yes** |
| Rename, add, or remove a job's **`name:`** (it overrides the id as the published context) | **yes** |
| Delete a job | **yes** |
| Delete the workflow, or remove `on: workflow_call` | **yes** — every name it published is gone |
| **Add** a job | no — nobody can require a context that did not exist |
| Edit a job's body (steps, timeout, permissions) | no — the context name is unchanged |

It is **loud, not locked**. A rename may be exactly what you want; it is still breaking, and it must
be **sequenced** — update the receivers' branch protection *first*. To proceed deliberately, add the
`reusable-job-id:breaking` label, or put `reusable-job-id: breaking` on a line in the PR body. That
is the same explicit opt-out [`architecture-map.yml`](../../.github/workflows/architecture-map.yml)
uses, and its purpose is the same: to make breaking a contract *a decision somebody made* rather than
an omission nobody noticed.

## Why the check is here, and not in the receiver

#549 asked for the mirror-image gate: have each repo read its own
`branches/main/protection` and assert every required context is actually produced. **That check
cannot run in a receiver's OWN CI, by anyone** — which is why it lives here, as a central scheduled
gate, and not in the receiver.

- The protection endpoint requires **`administration: read`**.
- **`administration` is not a valid `permissions:` scope for a workflow's `GITHUB_TOKEN`.**
  Declaring it is a *workflow validation error*: the run dies at **startup**, produces **no check
  run at all**, and therefore shows as neither red nor green — the same
  [#478](https://github.com/FS-GG/.github/issues/478) blind spot that hid 119 dead `lockfile-sync`
  runs. (#549's own first attempt shipped exactly this, and was caught only by reading the
  *workflow-run* list rather than the *check-run* list.) No receiver can read its own protection from
  its own `GITHUB_TOKEN`, and no amount of central wiring changes that — it is why the gate is
  central and App-authenticated rather than a `workflow_call` fanned out to each receiver.
- The org's **dispatch App now holds the scope**, but did not until recently.
  [#463](https://github.com/FS-GG/.github/issues/463) learned the hard way that it did not:
  `coordination-propagate`'s protection probe returned `403 Resource not accessible by integration`
  on every receiver, fell through to the fail-closed arm, and stopped the kit landing anywhere. It was
  rewritten to ask the *pull request* instead of branch protection — a change that turned out to be
  the better design regardless (`mergeStateStatus` accounts for required reviews too), so it stays
  even now the scope exists. On **2026-07-17** an org admin granted the App `administration: read`
  ([#574](https://github.com/FS-GG/.github/issues/574), Option A), so a **per-receiver-scoped
  installation token** can now read protection. That is what
  [`required-context-coherence.yml`](../../.github/workflows/required-context-coherence.yml) mints.

> **This is true of CLASSIC protection. The ruleset store is different — but it does NOT give you a
> credential-free gate** ([#574](https://github.com/FS-GG/.github/issues/574)).
>
> `rules/branches/<b>` needs only `metadata: read`, so it is tempting to conclude that a
> ruleset-protected repo can be audited from a workflow with no credential. **It cannot**, and the
> reason is worth stating precisely, because the wrong conclusion here rebuilds the original bug:
>
> A required set is the **union** of both stores. A token without `administration: read` gets a
> **403** on the classic endpoint — and a 403 does not mean *"there is no classic protection"*, it
> means *"I cannot see whether there is."* So an unreadable classic store makes the union
> **unknowable**, whatever the ruleset says. `check-required-contexts.py` therefore exits 3 (no
> verdict) on ANY repo when it cannot read classic protection, ruleset-protected or not — a
> half-read is not a verdict, and a gate that reported on half the stores is how this whole class
> of bug started.
>
> The credential was, for a long time, the blocker for a scheduled org-wide gate; rulesets did not
> retire it. It was granted on 2026-07-17 (#574) — see below.

So the **preventive** gate asks a question that needs **no credential**, and it asks it at the
**source**: [`reusable-job-id-coherence.yml`](../../.github/workflows/reusable-job-id-coherence.yml)
runs on the PR that would cause the outage, rather than in the victim's repo afterwards. That is
strictly better for the failure it covers — it *prevents* the deadlock instead of *explaining* it.
But it can only see a rename authored **here**; a typo authored in a *receiver's* protection, or a
context required before its caller is wired, is invisible to it. That is what the scheduled gate below
now covers.

## The scheduled gate — the receiver-side half

[`scripts/check-required-contexts.py`](../../scripts/check-required-contexts.py) implements
#549's original question: point it at a repo and it proves every required status check is a context
some workflow reports **on every pull request**, deriving the producible set statically from committed
YAML (job `name:` else id; `<caller> / <callee>` nested to any depth; matrix suffixes). It also catches
a plain **typo** in a protection setting for free — a misspelled required context is indistinguishable
from a renamed one, and both deadlock identically.

**"On every pull request", not merely "from a workflow that triggers on `pull_request`"**
([#1508](https://github.com/FS-GG/.github/issues/1508)). The weaker phrasing is what this section used
to say, and it is strictly wider: a workflow whose `pull_request` trigger carries a `paths:` filter
does trigger on `pull_request` and still reports nothing on a PR that touches none of those paths.
GitHub does not skip such a required check — it never creates the check run — so protection waits on
it forever. The gate models `paths:`/`paths-ignore:` on both PR events; `branches:` and `types:` are
the same class and are not modelled yet ([#1519](https://github.com/FS-GG/.github/issues/1519)).

Reading protection needs a token with `administration: read`. A person with an admin token can run it
by hand:

```sh
python3 scripts/check-required-contexts.py --repo FS-GG/FS.GG.Audio --root <a checkout of it>
```

Since the #574 grant, **CI runs it too.**
[`required-context-coherence.yml`](../../.github/workflows/required-context-coherence.yml) sweeps the
`contract-coherence` receivers on a daily schedule (and on demand via `workflow_dispatch`): for each,
it mints a dispatch-App installation token scoped to that one repo — restricted to
`administration: read` + `contents: read` — checks the repo out, and runs the verifier against it. A
finding (exit 1) or a permanent no-verdict (exit 3) fails that matrix leg red, naming the receiver and
the deadlock; a retryable read failure (exit 2) is retried, then surfaced. It runs centrally in
`.github`, not as a per-receiver `workflow_call`, for the reason above: a receiver's own
`GITHUB_TOKEN` can never hold `administration: read`.

Its fixture — [`tests/required-contexts/`](../../tests/required-contexts/) — runs on every PR that
touches the verifier, so the tool cannot rot, and its headline leg *is* the outage: FS.GG.Audio's real
`gate.yml` against this repo's real `lock-range-coherence.yml` with the job renamed, asserting Audio
deadlocks. (The scheduled workflow itself needs the App and a live protection read, so like
`coordination-propagate` it has no offline fixture — only the script it drives does.)

## A worked instance: `materialize / kit-bump-shape`

The receiver-side kit-bump reporter ([#1713](https://github.com/FS-GG/.github/issues/1713)) is this
page's contract in use, and it is recorded here because the next person to reach for
`repos.sh require-context` will need both halves of it.

**The context string is `materialize / kit-bump-shape`.** Composed, as always, from the two halves
this page is about:

```
materialize / kit-bump-shape
^^^^^^^^^^^   ^^^^^^^^^^^^^^
|             the `name:` of the `bump-shape` job in FS-GG/.github
|             .github/workflows/kit-materialize.yml — THE ONE PLACE IT IS DEFINED
the caller job id, `materialize`, in all seven receivers'
.github/workflows/kit-materialize.yml (verified identical across SDD, Rendering,
Governance, Templates, Game, Audio and Net)
```

It cannot be guessed from convention. The receivers' existing required contexts show both shapes —
bare job names (`Deterministic gate`, `API compatibility gate (breaking-change → SemVer major)`) and
`workflow / job` (`kit / coordination-kit`, `skill-union / skill-union`) — so whatever eventually arms
this must be handed the literal above:

```sh
scripts/repos.sh require-context --context 'materialize / kit-bump-shape' --receives coordination-kit
```

Two things keep that literal honest. [`check-reusable-job-ids.py`](../../scripts/check-reusable-job-ids.py)
makes renaming the callee half a loud, opt-out-able breaking change, exactly as it does for
`lock-ranges / lock-ranges`. And [`tests/kit-bump-shape/run.sh`](../../tests/kit-bump-shape/run.sh)
asserts the string *and* the absence of an `if:` on the job, so neither the documented context nor the
producibility of it can drift away from the workflow in silence.

### Where its rule comes from, and why that is not `main`

The `bump-shape` job runs `scripts/check-kit-bump-shape.py`, which lives in this repository. It does
**not** check that rule out of `FS-GG/.github@main`. It resolves the ref from the receiver's own pin:

```
dotnet restore <the receiver project>   ->  project.assets.json names the resolved FS.GG.Kit version
that version                            ->  the tag `kit/v<version>`
that tag                                ->  peeled to a 40-hex commit, and the rule is checked out THERE
```

[#1713](https://github.com/FS-GG/.github/issues/1713) shipped it reading `main`, deliberately and with
the defect written on its face; [#1772](https://github.com/FS-GG/.github/issues/1772) closed it. The
property at stake is ADR-0067 §2 — *a gate's verdict MUST be a pure function of (tree under test,
pinned ref)* — and the cost of not having it is measured, not theoretical: `FS.GG.SDD#724` passed on
merged SHA `0376309` at 08:15Z and failed on byte-identical content at 08:21Z, because the hub moved
underneath a verdict that read it at check time ([#1584](https://github.com/FS-GG/.github/issues/1584)).

Two consequences worth knowing before you touch either end:

* **`release-kit.yml` will not publish a version that has no `kit/v<version>` tag on the commit being
  packed.** The tag used to be a way of *triggering* a release; it is now a *precondition* of one,
  because it is what the fleet resolves a rule from. That holds going forward only: the tag is still
  a mutable ref, and two historical versions (0.1.0 and 0.4.0) were published without one, so a bump
  PR targeting either can only be refused. [#1784](https://github.com/FS-GG/.github/issues/1784) is
  the coherence check and the tag protection that close it.
* **Every failure on this path is a refusal, never a pass** — an unparseable version, a missing tag,
  or a kit release older than the rule itself. It never falls back to `main`, which is the entire
  point.

### The ordering constraint: never arm a context before its producer reports

**`repos.sh require-context --apply` for a context nothing produces holds every pull request in every
targeted repository at *"Expected — waiting for status to be reported"*, permanently.** GitHub does not
fail such a PR; it waits, and the only repair is another `administration: write` call. That is not a
hypothetical: the writer's dry run over the seven `coordination-kit` receivers reported **6 would-add,
1 failed** at a moment when no receiver-side producer existed at all, and running it would have wedged
six repositories. `required-context-coherence.yml` would have gone red across all six on the next
sweep — after the damage.

So the sequence is fixed, and each step's evidence is named:

1. **Land the producer.** For this context, the `bump-shape` job in the reusable `kit-materialize.yml`,
   which reaches all seven receivers with no receiver-side edit because all seven already call it.
2. **Observe it REPORT on a real pull request.** `gh api repos/<r>/commits/<sha>/check-runs` must show
   the context by name. A workflow that *should* report is not a producer.
3. **Prove producibility statically**, which needs no write at all:
   `python3 scripts/check-required-contexts.py --repo FS-GG/<r> --root <checkout> --protection <a
   payload naming the context>`. This is the same derivation the daily sweep runs, and it answers
   "could this context ever report?" without arming anything.
4. **Prove the producer's verdict is a function of the receiver, not of the clock.** A context whose
   producer reads another repository's moving tip at check time has no durable verdict, so requiring
   it converts every hub commit into a potential receiver outage (ADR-0067 §2,
   [#1584](https://github.com/FS-GG/.github/issues/1584)). This is a *second* precondition on the same
   step, and it is separate from step 1: a producer can report perfectly and still be unarmable.
   For `materialize / kit-bump-shape` the **rule** is discharged — it is fetched at the commit the
   receiver's own pin names (above), and `tests/kit-bump-shape/run.sh` fails if any foreign checkout
   in that job takes a literal ref. The **workflow around it is not**: receivers call it as
   `kit-materialize.yml@main`, so its probe, its version grammar, its tag scheme and its exit-code
   mapping still move with the hub. That is a far smaller surface than a 678-line rule, and every
   line of it is now a #266 decision rather than a verdict — but it is not zero, and anyone arming
   this must decide whether it is small enough. Closing it means receivers pinning the reusable
   workflow at a sha, which is a fleet-wide change.
   **Do not arm a context whose producer has no equivalent assertion at all.** "It looks pinned" is
   the state `kit-bump-shape` was in for the whole of #1713.
5. **Only then** consider `--apply` — and run the dry run first, which needs only
   `administration: read` and is what proves step 1 actually happened.

The reporter shipped under #1713 stops at step 2 on purpose. #1587's automerge was re-scoped on
2026-07-28: the producer keeps its priority as a defect and automerge is re-decided once the producer
has run over real fan-outs, so nothing is armed and `materialize / kit-bump-shape` is required in no
repository. A `failure` conclusion from it therefore blocks no merge — it is how a reviewer sees, in a
check list, which of seven fanned-out bump PRs need reading.
