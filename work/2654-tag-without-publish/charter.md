---
schemaVersion: 1
workId: 2654-tag-without-publish
title: kit-auto-publish tags without prepared bytes
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

# kit-auto-publish tags without prepared bytes Charter

## Identity
- Work id: `2654-tag-without-publish`
- Lifecycle stage: charter
- Status: chartered
- Coordination item: `FS-GG/.github#2654`
- Delivery route: `sdd-required` (route-decision revision 1, digest
  `9b48867c3e70901f903bbeb0e6c17eb7417baa11cbccbcf47858b3b2019d8290`)

## Principles
- The coherent-set release train has exactly one irreversible edge — pushing
  `kit/v<version>`, `drivers/v<version>` and `coord-engine/v<version>` — and every control
  must sit on the *near* side of it. A control that can only be read after the tags exist is
  not a control.
- Two rails that each behave correctly in isolation are still a defect when nothing joins
  them. The join is the subject of this work, not either rail's internal logic.
- A trigger is not an act, and a tag is not a publication. `.github#2533` corrected
  "a workflow fires" to "a program decides"; `.github#2571` corrected "the program decides"
  to "the program decides to tag". This work corrects "the program decides to tag" to
  "the artifact is published".
- Prose that instructs a worker and the gate that grades that worker are two halves of one
  contract. Neither half may move alone.
- Restoring a capability that a refactor silently removed is not the same as widening an
  automated publisher's blast radius, and the difference must be argued from the record
  rather than asserted.

## Scope Boundaries
- In scope: the join between `kit-auto-publish.yml`'s tag decision and
  `release-saga-prepare.yml`'s packed bytes; the patch-line prose in `merge-and-release.md`
  and its `.agents` mirror; a regression leg downstream of `kit-auto-publish.py`'s
  `decide()`; and an explicit, recorded disposition for the stranded `0.58.1` tags.
- Out of scope: `.github#2442`'s frontier rail (same-line patch only) — it is a recorded
  maintainer decision and this work does not reopen it.
- Out of scope: `decide()`'s own action vocabulary and refusal reasons. The gap this work
  closes is downstream of every answer `decide()` gives.
- Out of scope: performing any publication to a public feed. Publication remains
  operator-gated; this work makes the automation able to reach it, and records what a human
  must still do for `0.58.1`.
- Keep SDD lifecycle ownership separate from optional Governance enforcement.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.
- Release policy pointers that bind this work: ADR-0012 (dual feed), ADR-0013 (OIDC trusted
  publishing), `.github#1772` (every published version names an immutable tagged commit),
  `.github#2402` (coherent-set versioning), `.github#2409` (three sibling tags at one
  commit), `.github#2442` (patch-only auto rail), `.github#2600` (the release saga).

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2654-tag-without-publish`.
