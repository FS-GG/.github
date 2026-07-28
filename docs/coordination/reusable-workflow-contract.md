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
   line of it is now a #266 decision rather than a verdict — but it is not zero.
   **This step no longer asks the next person to decide that.** It was decided in
   [#1783](https://github.com/FS-GG/.github/issues/1783), and the answer is below under
   [Reusable-workflow calls are NOT pinned](#reusable-workflow-calls-are-not-pinned): **a
   `@main` call to a workflow in `FS-GG/.github` SATISFIES this precondition**, on the stated and
   narrow grounds that the hub tip a receiver reads is one the hub's own required checks have passed,
   and that its published contexts cannot be renamed without a loud, opt-out-able breaking change.
   Read that section before arming anything: it says exactly what the acceptance does *not* buy, and
   the residual is real rather than argued away.
   **Do not arm a context whose producer has no equivalent assertion at all.** "It looks pinned" is
   the state `kit-bump-shape` was in for the whole of #1713.
   And **`@main` is the only moving ref that satisfies it.** Any other — a branch, a tag, another
   repository — is a finding from `check-required-contexts.py`, on every receiver, every day.
5. **Only then** consider `--apply` — and run the dry run first, which needs only
   `administration: read` and is what proves step 1 actually happened.

The reporter shipped under #1713 stops at step 2 on purpose. #1587's automerge was re-scoped on
2026-07-28: the producer keeps its priority as a defect and automerge is re-decided once the producer
has run over real fan-outs, so nothing is armed and `materialize / kit-bump-shape` is required in no
repository. A `failure` conclusion from it therefore blocks no merge — it is how a reviewer sees, in a
check list, which of seven fanned-out bump PRs need reading.

## Reusable-workflow calls are NOT pinned

> **DECISION ([#1783](https://github.com/FS-GG/.github/issues/1783), 2026-07-28). A receiver calls a
> reusable workflow in `FS-GG/.github` at `@main`, and does not pin it at a commit. That is the
> accepted state, not an omission; pinning was considered and DECLINED. `@main` is the only moving
> ref accepted — any other is a finding.**

The alternative was live and unowned for as long as this page has existed, and #1772 filed #1783 to
end that. What follows is what was measured, and what the acceptance does and does not buy.

### The surface it is a decision about

Measured 2026-07-28 from each receiver's branch protection (both stores) joined to its committed
workflows — not from any in-repo prose about what is required:

| required context | receivers requiring it | the hub workflow that names it | ref |
|---|---|---|---|
| `kit / coordination-kit` | SDD, Rendering, Governance, Templates, Game, Audio, Net (**all 7**) | `coordination-coherence.yml` | `@main` |
| `contract-coherence / coherence` | Governance, Net | `contract-coherence.yml` | `@main` |
| `lock-ranges / lock-ranges` | Audio | `lock-range-coherence.yml` | `@main` |
| `Lock-range coherence (project refs track declared versions) / lock-ranges` | Game | `lock-range-coherence.yml` | `@main` |

**Eleven required contexts, across every one of the seven receivers, three hub workflows, none
pinned.** #1783's own body said "two of those are already required"; all three are, and
`lock-range-coherence.yml` is required in two repos under two different caller job names. Half of
each of those eleven strings is a job id in this repository, and the whole of each verdict is a
function of this repository's tip.

### Why pinning was declined

Four measurements, in the order that decided it.

**1. Pinning `uses:` would not have discharged ADR-0067 §2 at all.** All three callees fetch the rule
they run out of this repo *at check time*, with `actions/checkout` and
`ref: ${{ inputs.github-ref }}` — whose default is the string `"main"`, in every one of the three.
No receiver passes that input; verified across all seven. So a receiver that pinned its `uses:` at a
commit would freeze the *stanza* and leave the *rule* on the hub's tip, and the pin would be exactly
the "it looks pinned" state step 4 above warns about. Closing §2 by pinning needs **both** halves —
the `uses:` ref and the `github-ref` input — which is two edits in seven repositories that must stay
in step with each other forever, not one. The split is a defect on its own terms, whatever anyone
later decides about pinning, and is filed as
[#1786](https://github.com/FS-GG/.github/issues/1786).

**2. Nothing would advance the pins, and this is not a prediction.** Renovate's `github-actions`
manager is live in these repos and does open PRs there (`actions/upload-artifact` v7,
`actions/setup-dotnet` v6 in FS.GG.Rendering). Its Dependency Dashboard for FS.GG.Rendering
(`FS-GG/FS.GG.Rendering#14`) lists every `@main` hub call as a detected dependency — `FS-GG/.github
main`, six of them across `coordination-coherence.yml`, `gate.yml`, `kit-materialize.yml`,
`lockfile-sync.yml` and `template-base-skill-union.yml`. And under `template-dispatch.yml`, which
holds the org's **only** hand-authored SHA pin of a hub reusable workflow, it lists exactly one
dependency: `actions/checkout v7`. **The two `dispatch-sender.yml@5fed2838…` calls in that file are
not detected as dependencies at all.** Pinning does not put a call under Renovate's management; it
removes it from Renovate's view. What happens next is written on the pin itself — its comment reads
`# main as of 2026-06-28`, and `main` is now **988 commits** ahead of it.

That is the answer to "Renovate can update `uses:` pins natively". The mechanism that *would* exist
is `pinDigests`, which the org preset does not enable (`default.json` extends `config:recommended`,
which does not include it) and which this decision does not enable either. Turning it on is how the
decision gets revisited — see below.

**3. Even a bot-opened pin PR would not land.** Measured in `registry/repos.yml` under #1565: 16
`renovate/fs.gg.kit-*` PRs exist org-wide all-time; **four** have ever merged, all by a human
account, all within 28 minutes of each other. Automated dependency PRs into these seven repos are not
a reliable landing mechanism today, and a pin that does not advance is a receiver frozen on an old
gate — with the freeze invisible, because a stale pin is green.

**4. It re-imposes the seven-way fan-out the org is currently removing.** Every hub change to a
reusable workflow would need seven receiver PRs. [#1615](https://github.com/FS-GG/.github/issues/1615)
and [#1769](https://github.com/FS-GG/.github/issues/1769) are open work to *reduce* exactly that cost.

### What `@main` buys, stated exactly

Not §2's property. §2 asks for a verdict that is a pure function of (tree under test, **pinned ref**),
and `@main` is not a pinned ref. What is true instead, and it is weaker:

> A receiver's required verdict is a function of (its own tree, **a hub tip that has itself passed
> the hub's required checks**).

Two things hold that up, and both are mechanical rather than cultural:

* **`FS-GG/.github@main` is protected**, and requires `contract-coherence / coherence`, `projection`,
  `roster-closure`, `drift` and `reconcile`. The hub data these three callees read at `main` is what
  the hub's own required gate validates: `contract-coherence.yml` grades `registry/dependencies.yml`
  and `dist/dotnet/*.props` out of the hub checkout, and both are subjects of the hub's own
  `contract-coherence / coherence`.
* **The context names cannot move silently.** `reusable-job-id-coherence.yml` makes renaming or
  deleting a published context a loud, opt-out-able breaking change on the PR that would cause the
  outage — the deadlock half of this page.

### The residual, which is real

A hub commit that passes the hub's own gates can still change a receiver's verdict on byte-identical
content. That is #1584's shape and it is not closed by this decision; it is *bounded* by it. Per
callee:

* `coordination-coherence.yml` — the **comparand** is no longer the hub: #1584 moved it to the
  receiver's own pinned `FS.GG.Kit` package. What still moves is the *verifier program* and the
  roster. Smallest residual of the three, and the one that already paid for its lesson.
* `contract-coherence.yml` — the largest. It reads hub **data** at `main` and grades it, so a hub
  commit to `registry/dependencies.yml` or `dist/dotnet/*.props` moves the required
  `contract-coherence / coherence` verdict in FS.GG.Governance and FS.GG.Net directly. This is
  #1584's shape, unmigrated, on a required context, in two repos —
  [#1787](https://github.com/FS-GG/.github/issues/1787).
* `lock-range-coherence.yml` — program only (`sparse-checkout: /scripts/`); the comparand is the
  caller's own tree.

**This decision does not depend on `kit/v*` tags, and therefore does not depend on
[#1784](https://github.com/FS-GG/.github/issues/1784).** The refs it accepts are `main` and a 40-hex
commit; a tag is refused precisely *because* it is mutable and unchecked after publication, which is
what #1784 is about. #1772's rule resolution does depend on those tags — that dependency is real and
is #1784's, not this page's.

### What is checked, and how it can fail

Prose is not a control. `scripts/check-required-contexts.py` now audits, for every context a repo's
protection **requires**, every cross-repo `uses:` its producer had to cross to be named — and fails
(exit 1) unless each one targets `FS-GG/.github` at either a 40-hex commit or a ref in the accepted
set. The accepted set is a constant in that file with this decision written above it, so widening it
is a code change under review, not a comment somebody stopped believing.

It runs where it can already read protection: `required-context-coherence.yml`, daily, over all seven
receivers, with a per-receiver App installation token. **Measured on all seven live trees against live
protection on 2026-07-28: exit 0, 43 required contexts audited.** The decision and the fleet agree
today, which is the only condition under which recording it is honest.

`tests/required-contexts/run.sh` proves it can fail, on the real files rather than a drawing:
FS.GG.Audio's real `gate.yml` and this repo's real `lock-range-coherence.yml`, with the one token
`@main` mutated to `@wip` — the debugging branch left behind in a `uses:` line, which reads exactly
like `@main` to a reviewer and to every other gate here. That leg asserts the finding, the ref by
name, the reason, and that the summary does **not** call the repo deadlocked (it is not — the context
reports; only its meaning moved). A tag is refused on the same leg; a 40-hex commit and a
non-required context on an odd ref are both green, because #1783 declined to *require* pinning and
never forbade it.

### What would reverse this

Any one of these, and the decision is re-opened rather than eroded:

* `pinDigests` scoped to `FS-GG/.github` is enabled in `default.json` **and** demonstrated to open and
  land digest bumps in a receiver — which repairs measurement 2 and part of 3.
* The `github-ref` half is closed, so that pinning a `uses:` pins the rule with it. GitHub documents
  `github.job_workflow_sha` as the commit SHA of the reusable workflow file for the job running it,
  which would let a callee source its own scripts from its own commit with **no receiver-side edit at
  all** — and would make a later pin mean what a reader assumes it means.
  [#1786](https://github.com/FS-GG/.github/issues/1786), and it is **unmeasured**: that item may not
  change three required gates on the strength of a documentation sentence either.
* A required verdict is measurably moved by a hub commit again, as in #1584. One instance is a
  measurement; a second is this decision being wrong.
