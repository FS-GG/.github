---
schemaVersion: 1
workId: 2730-doc-comments-to-signatures
title: Doc Comments Sited Where The Compiler Keeps Them
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

# Doc Comments Sited Where The Compiler Keeps Them Charter

## Identity
- Work id: `2730-doc-comments-to-signatures`
- Coordination item: `FS-GG/.github#2730`
- Related cause row: `FS-GG/.github#266` (a check that cannot fail)
- Lifecycle stage: charter
- Status: chartered
- Delivery route: `sdd-required` (route-decision revision 1, digest
  `e01b9954492dc7c8c1e391de88472b3bbcb383589d257f3139d5a1f5a9095539`)

## Principles

- **The defect is siting, not writing.** Measured at `0ddd4b88`: 2,511 substantive doc-comment lines
  are written *only* in implementation files that have a sibling signature file, and **zero** of them
  reach the generated XML documentation. The prose is good; it is in a place the compiler discards.
  Nothing here asks the repository's `.fsi` writing to change — `TouchSet.fsi`, `Writes.fsi` and
  `Landable.fsi` are the standard this work moves prose *toward*.

- **An empty result set is not a negative finding, so the central claim is proved by a controlled
  experiment and not by a grep that found nothing.** One declaration
  (`FS.GG.Coord.Core/IntakeReceipt.validate`), one build, one grep, run twice: the sentinel in the
  `.fs` yields **0** hits in the generated XML, the identical sentinel in the `.fsi` yields **1**. The
  positive leg is what makes the negative leg mean anything.

- **The line this work must draw is about audience, not about location.** Prose that is genuinely
  about the implementation — why *this* loop, this ordering constraint, this fail-closed branch, the
  incident that produced this branch — is correctly placed in the `.fs` and a policy that sweeps it
  into the `.fsi` makes the signature file worse. The sweep therefore classifies by the question *"can
  a caller who never opens the `.fs` act on this sentence?"*, and only a `yes` moves.

- **The gate must not be able to fire on the correct side of that line, and it is built so that it
  cannot.** Its subject is the *comment marker*, never the content: `//` is invisible to it. A
  maintainer who writes implementation prose exactly where it belongs is never touched, so there is
  nothing here to learn to suppress. The only thing refused is a `///` whose content the compiler
  provably discards — wrong regardless of what it says.

- **A gate ships with evidence it can fail, and the failure mode this class dies of is
  "passes because it found nothing to look at" (`.github#266`).** So: discovering zero subject files
  is a *no verdict*, never a pass; the baseline must match the tree exactly rather than `<=`, so a
  stale baseline reds in both directions; and every negative leg of the fixture asserts the *reason*,
  not merely a non-zero exit.

- **The residue is recorded, not dropped.** `src/FS.GG.Coord.Cli` is outside this item's lane and its
  941 lines across 12 files are not swept here. They are enumerated in the baseline, as counts a
  future lane decrements, rather than left as an unstated exception.

## Scope Boundaries

- **In scope**
  - Every `.fs` file under `src/FS.GG.Coord.Core` (27 files, 1,847 `///` lines) and
    `src/FS.GG.Coord.GitHub` (10 files, 1,117 lines) that has a sibling `.fsi`, plus those siblings
    where contract prose is moved into them.
  - A new gate: `scripts/check-signature-doc-siting.py`, its fixture and baseline under
    `tests/signature-doc-siting/`, and `.github/workflows/signature-doc-siting.yml`.
  - This SDD package under `work/` and `readiness/`.

- **Out of scope, each for a stated reason**
  - **`src/FS.GG.Coord.Cli` (941 lines, 12 files).** `.github#2724` holds `Client.fs`/`Client.fsi`
    right now and the extraction programme moves those modules with their prose. Sweeping them here
    would collide with every extraction lane for no benefit. They enter the gate's baseline instead.
  - **Rewriting any `.fsi` that already carries the contract.** Where the signature already says what
    the implementation's block says, the duplicate is dropped and enumerated; the `.fsi` text is not
    re-authored.
  - **Any behaviour change.** No executable line moves. The only compiled artifact that may differ is
    the generated XML, and it may only gain.
  - **Adding this gate to branch protection.** A required context is a separate, owner-held decision;
    this ships as an ordinary workflow like `pipefail-assertions`.

## Sequencing, which is not a dependency

`.github#2724` and `.github#2731` each *add* signature files, so both move this work's own denominator
("implementation files that have a signature file"). Running this last would be cheaper. That is a
preference with a reason and **no blocker edge is added on the strength of it**: this session measured
`.github#2653` held blocked by an undocumented edge onto `.github#2106`, and the cost of that was real.
The mechanical consequence is stated rather than hidden: whichever of the three lands second re-computes
the baseline, which is the baseline mechanism working, not a conflict.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2730-doc-comments-to-signatures`.
