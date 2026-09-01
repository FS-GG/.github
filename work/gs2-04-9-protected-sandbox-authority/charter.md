---
schemaVersion: 1
workId: gs2-04-9-protected-sandbox-authority
title: GS2-04.9 Protected Sandbox Authority
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# GS2-04.9 Protected Sandbox Authority Charter

## Identity
- Work id: `gs2-04-9-protected-sandbox-authority`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Mint the existing organization App only inside the credential-owning protected repository and request the minimum granted permissions.
- Bind every effect to the exact FS.GG.Coordination candidate and the explicitly disposable repository and Project identities.
- Separate human administrative provisioning from App-performed qualification effects.
- Fail before writes on identity, target, quota, candidate, or expiry mismatch; always compensate and retain authoritative cleanup evidence.

## Scope Boundaries
- In: one protected manual workflow that mints `fs-gg-cross-repo-dispatch`, checks out an exact product candidate, drives the registered live Q4 plan against `FS.GG.GitHub.Substrate.Sandbox` and Project 2, and retains evidence.
- Out: production repositories or Projects, human-token qualification effects, package publication, deployment, stable release, or any roadmap unit after GS2-04.9.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Cross-repo request FS-GG/.github#3122 and source item FS-GG/FS.GG.Coordination#178 own the acceptance boundary.
- The App installation's live grant is the authority ceiling; workflow permission declarations and exact repository scoping must remain a strict subset.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work gs2-04-9-protected-sandbox-authority`.
