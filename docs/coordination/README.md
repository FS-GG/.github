# Cross-repo coordination protocol

How agents and maintainers across the six FS-GG framework components
([FS.GG.SDD](https://github.com/FS-GG/FS.GG.SDD),
[FS.GG.Rendering](https://github.com/FS-GG/FS.GG.Rendering),
[FS.GG.Governance](https://github.com/FS-GG/FS.GG.Governance),
[FS.GG.Templates](https://github.com/FS-GG/FS.GG.Templates),
[FS.GG.Game](https://github.com/FS-GG/FS.GG.Game),
[FS.GG.Audio](https://github.com/FS-GG/FS.GG.Audio)) — plus this `.github`
coordination repo, seven in all — request things from each
other, respond, and keep cross-repo contracts coherent.

The repos are deliberately decoupled (see
[project-split-decision.md](../project-split-decision.md) and
[transition-and-boundaries.md](../transition-and-boundaries.md)). Coordination happens
through **GitHub-native primitives** — not a shared mutable folder — so requests are
notified, threaded, assignable, searchable, and scriptable via `gh`.

> **Inner-repo sibling.** For running multiple workers **in parallel on different items
> inside one repo** (rather than *between* repos), see the
> [intra-repo parallel-work protocol](parallel-work.md) ([ADR-0021](../adr/0021-parallel-intra-repo-work-claim-worktree-touchset.md)) —
> it reuses this board and `fsgg-coord`, adding a claim lock, per-item git worktrees, and a
> declared `Paths:` touch-set with an overlap check.

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
# request: open in the TARGET repo.
# REST throughout — `gh issue create`/`list` are GraphQL, on a budget the whole fleet shares (#587).
gh api -X POST repos/FS-GG/FS.GG.Rendering/issues \
  -f title='[cross-repo] fs-gg-ui template drifted from framework HEAD' \
  -f 'labels[]=cross-repo' -f 'labels[]=cross-repo:request' -f 'labels[]=blocked' \
  -f body="From: FS.GG.Templates. Blocks: FS-GG/FS.GG.Templates build of fs-gg-fullstack.
Contract: fs-gg-ui-version. template/base/src/Product/*.fs (2026-06-15) no longer
compiles against src/Scene/Types.fsi (2026-06-22). No release tag pins a coherent set.

Paths: template/base/src/Product/" --jq .html_url

# triage / respond — `fsgg-coord issues` is REST + ETag, so a repeat read costs ZERO.
scripts/fsgg-coord issues rendering --label cross-repo
gh api -X POST repos/FS-GG/FS.GG.Rendering/issues/<n>/comments -f body="## Response
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
with `fsgg-coord add`.

Projects v2 is GraphQL-only, so board work spends from GitHub's GraphQL rate limit.
Route reads/writes through the thrifty client [`scripts/fsgg-coord`](../../scripts/fsgg-coord)
— it caches the static field/option ids, resolves board items narrowly, and reads issues
over REST with an ETag. See [graphql-budget.md](graphql-budget.md) for the cost model.

> One-time setup (needs the `project` scope: `gh auth refresh -s project,read:project`). Run once, by
> a human, with admin rights — never on a worker path, which is why it may spend GraphQL directly:
>
> <!-- graphql-monopoly: exempt — one-time board provisioning, run once by a human; never on a worker path -->
> ```sh
> gh project create --owner FS-GG --title "Coordination"
> ```
> Then add a saved filter/view on the `cross-repo` label across the org's repos.

## Durable contracts → the registry, not issues

Issues are for *transient* requests. The **stable** facts — who depends on whom, which
contract versions are coherent — live in the
[contract & compatibility registry](../registry/compatibility.md)
([`registry/dependencies.yml`](../../registry/dependencies.yml)). Any `contract-change`
issue must update the registry as part of its resolution. Every registry change also
**prepends one dated entry** (newest-first) to the registry changelog
[`registry/CHANGELOG.md`](../../registry/CHANGELOG.md) — `- **YYYY-MM-DD** — HEADER
(owner; refs): body` — and sets the file's `updated:` date to match. One entry per change
keeps PR diffs reviewable (this replaced the former ~42 KB single-line `updated:` comment;
see .github#129). Larger cross-repo decisions are recorded as [ADRs](../adr/README.md).

So a registry PR touches **four** files, not two: `registry/dependencies.yml`, the changelog
`registry/CHANGELOG.md`, the hand-maintained projection
[`docs/registry/compatibility.md`](../registry/compatibility.md), and the architecture map
[`docs/architecture.md`](../architecture.md) — because `registry/dependencies.yml` is itself
an [architecture-map reconcile trigger](#system-overview--the-architecture-map) (a
non-structural flip opts out; see that section). Note that `fsgg-sdd registry validate` and
`check-feed-coherence` cover only the first file: **a green validator is not a green PR.**

## Surface change → the shipped-surface-mutation event

A change to an *already-shipped* public surface is the one event a contract-first platform
exists to govern. The [shipped-surface-mutation protocol](shipped-surface-mutation.md)
([ADR-0025](../adr/0025-first-class-shipped-surface-mutation-event.md)) makes it first-class:
a changed committed `.fsi` baseline (detected by `fsgg-sdd surface --check`) is **classified**
additive/breaking, then reconciled through the registry/projection/ADR checklist above and a
**consumer-impact flag** — `scripts/fsgg-surface-impact <contract-id>` enumerates exactly which
consumers a mutation must notify, turning the hand-written "who consumes this" note into a query.

## System overview → the architecture map

The registry and the ADRs are **point artifacts** — the registry records *what is
currently coherent*, an ADR records *why one decision was made*. Neither produces the
**synthesis**: a single narrative of *what the system is*. That synthesis is
[`docs/architecture.md`](../architecture.md) — the one system-overview artifact, owned
by `FS-GG/.github`. It is **non-authoritative** (detail stays in the registry, the
ADRs, and each component repo) but it is **process-owned**, not ad hoc:

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
red when its actual build config stops matching the registry's declared values, and when the
registry itself stops being schema-valid. Wire it into each repo's CI (see the doc for the snippet).

That gate asserts only what a **caller's own PR can fix** — the rule .github#741 established, after
its `fsgg-contracts` version assertion (against another repo's `main`) turned every Contracts bump
into an org-wide wedge with no safe landing order. Registry-vs-reality is asserted instead by two
**`.github`-local** gates, on PR + push + a daily schedule, so a red stops only the repo that can
flip the registry: [`source-coherence`](../../.github/workflows/source-coherence.yml) (`version` ==
the `FS.GG.Contracts` source SemVer on SDD's `main`) and
[`feed-coherence`](../../.github/workflows/feed-coherence.yml) (`package-version` == newest live on
the org feed). Both need the schedule for the same reason `skill-registry-coherence` does, below: an
SDD bump or a publish stales this registry with no `.github` commit to trigger on.

A second reusable gate, the [skill-union assertion](skill-union-assertion.md) (`workflow_call`),
proves a scaffolded workspace's agent-skill roots are the **byte-identical union** of process +
product skills — content, not presence (ADR-0014's consumer-side check; the composition gate is its
first caller).

### Skill runtime exposure

`.agent-skill-roots` is the transport/parity declaration: `.claude/skills`, `.codex/skills`, and
`.agents/skills` must remain complete byte-identical mirrors. It is not a request for every host to
catalog every root. Invoke a skill through the selector the active host supports (Codex CLI/IDE uses
`$skill-name` or `/skills`; other hosts may provide a picker). Do not treat a literal `/skill-name`
in historical prose as universally executable syntax.

Codex natively discovers repository skills from `.agents/skills`. An installation or wrapper that
also exposes `.codex/skills` will therefore show a duplicate name, because Codex does not merge
same-named skills. Keep both directories synchronized and suppress only the duplicate catalog entry
with Codex's supported per-skill override in `~/.codex/config.toml`:

```toml
[[skills.config]]
path = "/absolute/path/to/repository/.codex/skills/drive-board/SKILL.md"
enabled = false
```

Repeat the entry for each duplicated `.codex/skills/*/SKILL.md`, using resolved absolute paths, then
restart Codex. The `.agents` copy remains enabled. Suppression is runtime configuration only: never
delete, omit, or desynchronize the `.codex` mirror to hide a duplicate.

Where that gate checks a *consumer's* tree, `skill-registry-coherence` checks this repo's *catalog*:
`scripts/fsgg-skill-registry-check` asserts every `registry/skills.yml` row against the producer body
it names — `source:` exists, `sha256:` equals that body's canonical digest, and a `fs-gg-game`-owned
row still byte-matches Rendering's frozen copy (ADR-0022 §6). The invariant **registry = manifest =
bytes** was asserted in every changelog entry and enforced nowhere; 14 of 32 rows had gone stale
(.github#247). It runs on a **schedule** as well as on PR, because a producer body change stales this
registry with no `.github` commit to trigger on.

## Propagation → the cross-repo auto-update fabric

The coherence gate goes *red* on stale pins; the [auto-update fabric](auto-update-fabric.md)
keeps them *fresh* in the first place — a reusable `dispatch-sender` workflow (producer release →
consumer `repository_dispatch`) plus an org-shared Renovate preset (custom managers for every
embedded cross-repo pin). Both are authored in `.github`, and the H4 admin step
([#21](https://github.com/FS-GG/.github/issues/21) — App + Packages feed) is **done and verified**:
push is smoke-tested, both producer halves have fired, and Renovate authenticates to the feed. The
fabric is one green `FS.GG.*` Renovate sweep from `coherent: true` (see
[auto-update fabric](auto-update-fabric.md)).
