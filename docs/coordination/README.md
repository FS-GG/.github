# Cross-repo coordination protocol

How agents and maintainers across the FS-GG repos
([FS.GG.SDD](https://github.com/FS-GG/FS.GG.SDD),
[FS.GG.Rendering](https://github.com/FS-GG/FS.GG.Rendering),
[FS.GG.Governance](https://github.com/FS-GG/FS.GG.Governance),
[FS.GG.Templates](https://github.com/FS-GG/FS.GG.Templates)) request things from each
other, respond, and keep cross-repo contracts coherent.

The repos are deliberately decoupled (see
[project-split-decision.md](../project-split-decision.md) and
[transition-and-boundaries.md](../transition-and-boundaries.md)). Coordination happens
through **GitHub-native primitives** — not a shared mutable folder — so requests are
notified, threaded, assignable, searchable, and scriptable via `gh`.

## Requests and responses → cross-repo issues

A "mailbox message" is a **GitHub issue in the target repo**.

- **Request:** open an issue **in the repo you need something from**, using the
  `Cross-repo request` template (available org-wide from this repo). It is labelled
  `cross-repo` + `cross-repo:request`. Always name the affected contract/registry id
  and the work it blocks.
- **Response:** a **comment** on that issue (start it with `## Response`), or a linked
  PR/issue. The maintainer/agent of the target repo owns the response.
- **Resolution:** closing the issue (ideally via a linked PR) is the "done" signal.
  The requester confirms.

Cross-reference everything with `FS-GG/<repo>#<n>`, commit shas, and contract ids so
the thread is self-describing.

### Agent recipe (`gh` CLI)

```sh
# request: open in the TARGET repo
gh issue create --repo FS-GG/FS.GG.Rendering \
  --title "[cross-repo] fs-gg-ui template drifted from framework HEAD" \
  --label cross-repo --label cross-repo:request --label blocked \
  --body "From: FS.GG.Templates. Blocks: FS-GG/FS.GG.Templates build of fs-gg-fullstack.
Contract: fs-skia-ui-version. template/base/src/Product/*.fs (2026-06-15) no longer
compiles against src/Scene/Types.fsi (2026-06-22). No release tag pins a coherent set."

# triage / respond
gh issue list   --repo FS-GG/FS.GG.Rendering --label cross-repo
gh issue comment <n> --repo FS-GG/FS.GG.Rendering --body "## Response
Refreshing template/base to the current Scene API; will tag 0.2.0-preview.1."
```

## Labels (apply org-wide)

| Label | Meaning |
|---|---|
| `cross-repo` | touches more than one FS-GG repo |
| `cross-repo:request` | an incoming request from another repo |
| `cross-repo:response` | a response/handoff back to another repo |
| `blocked` | this work is blocked on another repo |
| `contract-change` | changes a versioned cross-repo contract (needs registry update) |

Create them in every repo with [`scripts/apply-labels.sh`](../../scripts/apply-labels.sh).

## Tracking → org Project

Cross-repo issues are aggregated on the org-level **Coordination** Project (Projects
v2) so blocked/in-flight requests are visible across repos in one board. Add an issue
with `gh project item-add`.

> One-time setup (needs the `project` scope: `gh auth refresh -s project,read:project`):
> ```sh
> gh project create --owner FS-GG --title "Coordination"
> ```
> Then add a saved filter/view on the `cross-repo` label across the org's repos.

## Durable contracts → the registry, not issues

Issues are for *transient* requests. The **stable** facts — who depends on whom, which
contract versions are coherent — live in the
[contract & compatibility registry](../registry/compatibility.md)
([`registry/dependencies.yml`](../../registry/dependencies.yml)). Any `contract-change`
issue must update the registry as part of its resolution. Larger cross-repo decisions
are recorded as [ADRs](../adr/README.md).

## System overview → the architecture map

The registry and the ADRs are **point artifacts** — the registry records *what is
currently coherent*, an ADR records *why one decision was made*. Neither produces the
**synthesis**: a single narrative of *what the system is*. That synthesis is
[`docs/architecture.md`](../architecture.md) — the one system-overview artifact, owned
by `FS-GG/.github`. It is **non-authoritative** (detail stays in the registry, the
ADRs, and each product repo) but it is **process-owned**, not ad hoc:

> **Reconcile trigger.** Any ADR that changes the shape of the system, and any
> `contract-change` that alters the architecture map's contract picture (its §5),
> MUST reconcile `docs/architecture.md` as part of its resolution — after the registry
> update, not instead of it.

This is the same discipline as "a `contract-change` must update the registry," applied
to the overview so it can't silently drift while staying the documented "start here."

A lightweight reminder gate enforces it: [`architecture-map.yml`](../../.github/workflows/architecture-map.yml)
reds a PR that adds/edits a numbered ADR or touches `registry/dependencies.yml` without
also touching `docs/architecture.md`. Because "shape-changing" is a judgment call, it is
not a hard block — a non-structural change (a version bump, a coherence-row edit, a typo,
an ADR status flip) opts out with a one-line `architecture-map: unaffected` in the PR body
or the `architecture-map:unaffected` label. Loud, but never in the way.

## Enforcement → the contract-coherence gate

The registry is not just documentation: the reusable
[contract-coherence gate](contract-coherence-gate.md) (`workflow_call`) makes every repo's CI go
red when its actual pins/build-config stop matching the registry's declared values. Wire it into
each repo's CI (see the doc for the snippet).

## Propagation → the cross-repo auto-update fabric

The coherence gate goes *red* on stale pins; the [auto-update fabric](auto-update-fabric.md)
keeps them *fresh* in the first place — a reusable `dispatch-sender` workflow (producer release →
consumer `repository_dispatch`) plus an org-shared Renovate preset (custom managers for every
embedded cross-repo pin). Both are authored in `.github` and dormant until the H4 admin step
([#21](https://github.com/FS-GG/.github/issues/21)) provisions the App + Packages feed.
