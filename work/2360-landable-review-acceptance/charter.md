---
schemaVersion: 1
workId: 2360-landable-review-acceptance
title: Require review acceptance in landable by default
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

# Require review acceptance in landable by default Charter

## Identity
- Work id: `2360-landable-review-acceptance`
- Lifecycle stage: charter
- Status: chartered

## Principles
- A merge verdict used by the worker protocol must enforce review acceptance by default; an
  optional assertion that can be silently omitted is not a gate.
- Specialized unattended callers that deliberately have no critic must remain operable, but their
  exemption must be explicit in the verdict path and observable in command output.
- Review evidence is coordination-state-bound. Authorization binds the item, live claim generation,
  PR head, and the current tip of the PR-declared base branch; movement of any revision fails closed.
  The PR object's cached `base.sha` and a local merge-base are not base-tip authority.
- Every new default/exemption branch ships with an inverted test proving that its predicate decides
  the observed exit code, not merely a green suite around unreachable code.

## Scope Boundaries
- Keep SDD lifecycle ownership separate from optional Governance enforcement.
- Extend the structured acceptance record, `landable`, and guarded landing boundary together; do not
  alter option grammar, branch protection, or workflow definitions.
- Preserve `--require fsgg:review-decision/v2` as a compatible explicit spelling for worker recipes.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2360-landable-review-acceptance`.
