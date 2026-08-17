---
schemaVersion: 1
workId: 2752-authorship-independent-verification-efficacy
title: A verification artifact's efficacy is proven against its author's own model
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

# A verification artifact's efficacy is proven against its author's own model Charter

## Identity
- Work id: `2752-authorship-independent-verification-efficacy`
- Coordination item: `FS-GG/.github#2752`
- Parent epic: `FS-GG/.github#266` — this row is that epic's **fifth admitted mechanism** and its
  instance (j)
- Delivery route: `sdd-required` (route-decision revision 1, digest
  `4b6568ffe6d9d753bdad303406f6e5974684f44b8fd5f138335fddef26fed428`)
- Canonical spec home: `work/2752-authorship-independent-verification-efficacy/spec.md`
- Lifecycle stage: charter
- Status: chartered

## Principles

- **The mechanism is an authorship relation, not a defect shape.** `#266`'s first four admitted
  mechanisms each describe *what the artifact does wrong*. This one describes *why nobody caught it*:
  the proof of efficacy and the artifact share an author, so the proof inherits the artifact's blind
  spot. Stating it as one of its two surfaces — "sweeps are incomplete", or "fixtures are
  self-referential" — loses the thing that makes it predictive.

- **A rule a reviewer can agree with and still not apply has the same failure shape.** The subject of
  this work is the contract every FS-GG critic is dispatched under. Prose that reads well and binds
  nothing is a verification artifact reporting a pass that carries no information — `#266`'s own test,
  applied to `#266`'s own remedy. So every requirement this work adds must name something a critic
  **executes or measures** and a host can read back off the review record.

- **The proof must be drawn from someone else's model.** This work authors a verification artifact
  about verification artifacts. Its own efficacy evidence therefore may not be a control set of the
  author's invention: the artifacts it is shown to discriminate are ones **other agents already
  measured and recorded**, with the verdict fixed before this work existed. Anything less reproduces
  the mechanism one level up, which is the specific failure this row exists to prevent.

- **An always-red rule is as informationless as an always-green one.** A negative control proves the
  rule *can* refuse; a positive control proves it does not refuse everything. `#2752` AC4 names the
  first; the second is its necessary partner, and the reference the acceptance criteria already point
  at (`tests/receiver-validate/run.sh` section F) is the artifact that must be **admitted**.

- **A contract that cannot be satisfied is a contract every future chain apologises for.** `#2757`
  records that this contract already runs three ordinary rounds plus a repair phase with no stated
  stopping rule. Every requirement added here carries an explicit bound and an explicit terminal
  disposition, so a legitimate row on which a leg genuinely cannot be constructed can *close* rather
  than loop.

## Scope Boundaries

- **In scope:** `.claude/skills/pnext-item/references/independent-review.md` — the independent-review
  contract itself, and only it. Specifically the `Gate-inversion evidence` section, which is this
  org's existing procedure for proving a verification artifact's efficacy, and the materiality list
  in `Root cause, dedupe, and materiality`, which is what makes a new requirement blocking rather
  than decorative.

- **Out of scope, and each for a stated reason:**
  - **`.github#266`'s own body.** AC1 asks that the epic's admitted-mechanism table carry the fifth
    mechanism. It already does — the analyst wrote it when filing this row. AC1 is discharged by
    verification, not by an edit, and `.github#2695` makes the analyst the sole filing authority in
    any case.
  - **`tests/claim-fence/`, `scripts/check-claim-fence.py`, `.github/workflows/fsgg-claim-fence.yml`.**
    The first debtor of AC2, and explicitly owned by `.github#2719`, which has a live worker on it.
    Folding it in would put one test leg inside two rows' declarations.
  - **`tests/receiver-validate/run.sh`.** The *reference implementation* of AC2's leg, cited and not
    edited. It is already correct; a citation is the whole use this work makes of it.
  - **`.claude/skills/pnext-item/SKILL.md` and every other kit source.** The new material is sited
    inside the `Gate-inversion evidence` section that `SKILL.md` §3 already links by anchor, so the
    wiring needs no second edit. This is a deliberate siting decision, not an omission.
  - **Cutting the owed `FS.GG.Kit` release.** Editing a kit source obliges a coherent-set kit
    republish. It is **declared** as a post-merge obligation and **not performed**: publishing needs
    per-cut operator authorisation. `.github#2333`'s permanently-wrong `kit/v0.47.0` is what getting
    this backwards produces.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2752-authorship-independent-verification-efficacy`.
