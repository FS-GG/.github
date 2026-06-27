---
name: cross-repo-coordination
description: Coordinate work across the FS-GG repos (FS.GG.SDD, FS.GG.Rendering, FS.GG.Governance, FS.GG.Templates). Use when you need something from another FS-GG repo, are changing a versioned cross-repo contract, or hit a cross-repo version/API incoherence. File and answer requests as GitHub issues, keep the contract/compatibility registry coherent, and record cross-repo decisions as ADRs. Canonical protocol lives in FS-GG/.github.
---

# Cross-repo coordination (FS-GG)

The FS-GG repos are deliberately decoupled but coupled at the edges through versioned
contracts. Coordinate through **GitHub-native primitives** — never a shared file
"mailbox" (git is not a queue). Canonical docs:

- Protocol: `FS-GG/.github` → `docs/coordination/README.md`
- Registry: `FS-GG/.github` → `registry/dependencies.yml` + `docs/registry/compatibility.md`
- Decisions: `FS-GG/.github` → `docs/adr/`

## When to use this skill

1. You need a change, decision, or release from **another** FS-GG repo.
2. You are about to change a **versioned cross-repo contract** (`scaffold-provider`,
   `scaffold-provenance`, `governance-handoff`, the governance config schemas,
   `fs-gg-ui-template`/`fs-skia-ui-version`).
3. You hit a **cross-repo incoherence** (a consumer can't build/run against an upstream).

## File a request (the "mailbox message")

A request is a **GitHub issue in the target repo**, using the org-wide
`Cross-repo request` template (labels `cross-repo` + `cross-repo:request`). Always name
the affected contract/registry id and the work it blocks; cross-reference with
`FS-GG/<repo>#<n>`, commit shas, and contract ids.

```sh
gh issue create --repo FS-GG/<target> \
  --title "[cross-repo] <short summary>" \
  --label cross-repo --label cross-repo:request [--label blocked] \
  --body "From: <your repo>. Blocks: <ref>. Contract: <id>. <what you need and why>"
```

## Respond / resolve

- **Respond:** comment on the issue, starting with `## Response` (or link a PR/issue).
  The target repo's agent/maintainer owns the response.
- **Resolve:** close the issue (ideally via a linked PR). The requester confirms.

```sh
gh issue list    --repo FS-GG/<repo> --label cross-repo
gh issue comment <n> --repo FS-GG/<repo> --body "## Response ..."
```

## Keep the registry coherent (required for contract changes)

Before changing a versioned cross-repo contract, read
`FS-GG/.github` → `registry/dependencies.yml`. Any `contract-change` issue MUST update
that registry (and its `docs/registry/compatibility.md` projection) as part of its
resolution — including flipping a `coherence` entry and linking its `tracking` issue.
Record larger cross-repo decisions as an ADR under `docs/adr/`.

## Labels

`cross-repo`, `cross-repo:request`, `cross-repo:response`, `blocked`, `contract-change`.
Apply them to a repo with `FS-GG/.github` → `scripts/apply-labels.sh`.

## Setup notes

- Org Project tracking needs the `project` scope: `gh auth refresh -s project,read:project`
  then `gh project create --owner FS-GG --title "Coordination"`.
- To make this skill active in a product repo, copy it into that repo's
  `.claude/skills/cross-repo-coordination/`.
