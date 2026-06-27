# ADR-0001: Cross-repo coordination via GitHub issues + a registry

- **Status:** Accepted
- **Date:** 2026-06-27
- **Affects:** FS.GG.SDD, FS.GG.Rendering, FS.GG.Governance, FS.GG.Templates, .github

## Context

The FS-GG repos are deliberately decoupled, but coupled at the edges through versioned
contracts (`scaffold-provider`, `governance-handoff`, the `fs-gg-ui` template + framework,
Governance config schemas). Agents and maintainers in one repo regularly need changes,
decisions, or releases from another, and the repos must stay version-coherent.

A concrete failure motivated this: FS.GG.Templates' `fs-gg-fullstack` could not build the
rendering app because the `fs-gg-ui` template pin (`FsSkiaUiVersion=0.1.0-preview.1`) had
drifted from the framework HEAD (`0.1.46+`, refactored Scene API), with no release tag and
no notification path to the consumer. See the `fs-skia-ui-version` row in the
[compatibility registry](../registry/compatibility.md).

A file-based "mailbox" folder was considered and rejected: git is an append-only history,
not a queue — concurrent writers conflict, there are no notifications, and threading /
assignment / status / search would be reinvented poorly.

## Decision

1. **Transient cross-repo requests/responses are GitHub issues** in the target repo,
   using the org-wide `Cross-repo request` template and the `cross-repo*` labels.
   Responses are comments; resolution closes the issue. Protocol:
   [docs/coordination/README.md](../coordination/README.md).
2. **Tracking** is an org-level Projects v2 board ("Coordination").
3. **Durable cross-repo facts** — the dependency graph and contract-version coherence —
   live in [`registry/dependencies.yml`](../../registry/dependencies.yml) (+ its human
   projection), not in issues. Any `contract-change` updates the registry as part of its
   resolution.
4. **Cross-repo decisions** are recorded as ADRs in this directory.

## Consequences

- Requests are notified, threaded, assignable, searchable, and `gh`-scriptable for
  autonomous agents — no bespoke infrastructure.
- The registry gives a single place to detect incoherence (the kind that silently broke
  the `fs-gg-fullstack` build) before consumers hit it.
- Each repo must apply the shared labels (`scripts/apply-labels.sh`) and keep its owned
  registry entries current.
- If fully autonomous, git-only agents (no GitHub API) ever become a hard requirement, an
  append-only event-log mailbox can be added under `.github` as a follow-up — but issues
  remain the default.
