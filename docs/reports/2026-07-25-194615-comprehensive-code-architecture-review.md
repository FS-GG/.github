# Comprehensive code and architecture review

- Repository: `FS-GG/.github`
- Reviewed revision: `3833e75eef42fa3f3a54f676476a2cc03424b7a6`
- Review completed: 2026-07-25 19:46:15 UTC (21:46:15 CEST)
- Scope: tracked source, tests, scripts, workflows, repository policy, current GitHub checks, and the relationship between this coordination repository and the seven product repositories

## Executive assessment

The repository has evolved from shared metadata into an operational control plane. Its Core/GitHub/CLI split is sound, its coordination rules are unusually executable, and 1,064 targeted Release tests passed. The main operational concern is that the default branch is currently red because a centrally declared CLI pin has drifted. The main maintainability concern is that several orchestration boundaries have grown into very large modules and scripts.

Overall risk: **medium**. No correctness defect was found in the tested coordination engine, but a red default branch weakens the signal of every subsequent change.

## Architecture

The architecture has three useful layers:

1. `FS.GG.Coord.Core` models snapshots, scheduling, lanes, and policy without GitHub transport concerns.
2. `FS.GG.Coord.GitHub` owns GraphQL/REST integration and organization state reads.
3. `FS.GG.Coord.Cli` composes the model and transport into operator-facing commands.

Repository workflows and Python/shell checks then make the written cross-repository contracts executable. This is the right dependency direction. The chief pressure point is at the presentation/composition edge: `Client.fs` is now a 4,700-line command surface, while complex checks such as pin coherence are substantial programs in their own right.

## Evidence

| Check | Result |
|---|---|
| `FS.GG.Coord.Core.Tests`, Release | 420 passed |
| `FS.GG.Coord.GitHub.Tests`, Release | 349 passed |
| `FS.GG.Coord.Cli.Tests`, Release | 295 passed |
| Current-revision GitHub checks | 95 succeeded, 2 skipped, 2 failed, 1 cancelled |
| Failed-check inspection | Both failures reproduce the same pin-coherence violation |

The review was static plus build/test and live-check inspection. It was not a penetration test, production load test, or audit of organization secrets.

## Findings

### 1. High — the default branch is red because the SDD CLI pin is stale

`contract-coherence.yml` declares `FS.GG.SDD.Cli` version `0.18.0`, while the repository registry and published package line have advanced to `0.23.0`. `scripts/check-pin-coherence.py` correctly fails closed and points to Renovate item #1189.

This is not a false-positive check: the repository is explicitly asserting mutually inconsistent desired state. Until the pin update is merged, failures caused by new changes are harder to distinguish from the standing failure.

Recommendation: resolve #1189, then require a green default branch before accepting unrelated control-plane changes. Keep the fail-loud behavior.

### 2. Medium — the CLI composition boundary is a maintenance hotspot

`src/FS.GG.Coord.Cli/Client.fs` is approximately 4,711 lines. It combines command orchestration, result shaping, presentation decisions, and many operational paths. Nearby large files include `Reads.fs`, `Board.fs`, and `Snapshot.fs`.

The test coverage materially reduces immediate risk, but the size increases review radius and makes independent ownership difficult.

Recommendation: extract command families behind small application-service modules while preserving Core as the source of scheduling truth. Keep JSON contracts at the outer boundary and add characterization tests before moving behavior.

### 3. Medium — workflow and coherence-check breadth creates a large change surface

The repository contains roughly 60 workflows plus large policy scripts, including a pin-coherence checker of about 1,844 lines. This breadth is justified by the control-plane role, but it increases duplicated bootstrapping, permissions review, and supply-chain maintenance.

Recommendation: continue consolidating repeated workflow setup into pinned composite actions and reusable workflows. Maintain an explicit owner and contract-test fixture for every large policy checker.

## Strengths

- Coordination policy is represented as code and tested rather than left as prose.
- The pure Core layer makes scheduling behavior reproducible without GitHub access.
- Pin coherence fails loudly and provides a concrete remediation path.
- Test coverage spans core modeling, GitHub integration behavior, and CLI contracts.
- Cross-repository registry and provider metadata make dependency movement observable.

## Recommended order

1. Merge the outstanding SDD CLI pin update and restore a green default branch.
2. Define seams within `Client.fs`, starting with the least coupled command family.
3. Inventory workflow duplication and consolidate the highest-churn setup paths.
4. Add a lightweight dashboard or scheduled summary for standing default-branch failures.
