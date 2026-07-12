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
| **Job ids** | ← **the one this page is really about.** See below. | [`required-context-coherence.yml`](../../.github/workflows/required-context-coherence.yml) (#549) |

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

Before you rename one, find out who requires it:

```sh
# Which repos require a context whose right-hand half is this job?
gh api repos/FS-GG/<repo>/branches/main/protection \
  --jq '.required_status_checks.checks[].context'
```

If any receiver requires `<something> / <the job you are renaming>`, the rename must be sequenced:
land the receiver's protection change first, or do not rename it.

## What is asserted, and what is still remembered

[`required-context-coherence.yml`](../../.github/workflows/required-context-coherence.yml) is a
**reusable** gate that a receiver calls. It reads its **own** branch protection and asserts that
every required context is one its `pull_request` workflows can actually produce — deriving the
producible set **statically**, from committed YAML, including the nested `caller / callee` names.
It catches the rename, and it catches a plain typo in a protection setting for free (a misspelled
required context is indistinguishable from a renamed one, and both deadlock identically).

**It is a diagnostic, not a preventive.** It runs in the receiver, so it fires on the receiver's
next PR — turning a silent forever-pending check into a named red that says which job was renamed.
It does **not** stop the rename from merging here.

Why not: reading required status checks needs `administration: read`, and a workflow's
`GITHUB_TOKEN` carries **no rights in any repo but its own**. A gate in `FS-GG/.github` therefore
**cannot** read FS.GG.Audio's protection — not for want of trying, but structurally. Each repo reads
its own, with its own token, granting the scope in its own caller; that is self-service, and needs
no org-admin change. Closing the preventive half needs a credential that does not exist today (a
GitHub App with org-wide `administration: read`), and is filed separately.

Until then, the rule above is **remembered, not enforced**, on the producing side — which is exactly
the state this page exists to make uncomfortable.

## Adopting the gate

```yaml
# <receiver>/.github/workflows/gate.yml
jobs:
  required-contexts:
    permissions:
      contents: read
      administration: read      # required: protection lives behind the administration API, and a
                                # callee cannot request a permission its caller withheld (#478)
    uses: FS-GG/.github/.github/workflows/required-context-coherence.yml@main
```

Grant `administration: read` in the **caller**. Without it the gate has **no verdict** (exit 3) —
never a green one.
