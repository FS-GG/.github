---
schemaVersion: 1
workId: 2725-cli-kernel-extraction
title: Cli Kernel Extraction
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

# Cli Kernel Extraction Charter

## Identity
- Work id: `2725-cli-kernel-extraction`
- Coordination item: `.github#2725`
- Lifecycle stage: charter
- Status: chartered

## Principles
- **Lane concurrency is the entire justification, and the row says so against its own interest.**
  Measured on the row: touching `Client.fs` (11,090 lines) and touching `Json.fs` (72 lines) both
  rebuild in ~3.9s. This split is **not** justified by build time. What it buys is that
  `.github#2726`–`#2729` can run as disjoint lanes instead of queueing behind one `<Compile Include>`
  ordering list in `FS.GG.Coord.Cli.fsproj` — already the second most-touched file in the repository
  (34 of the last 500 commits). **If the boundary drawn here does not create those disjoint lanes,
  this work has not delivered its reason for existing**, and no other success criterion substitutes.
- **Four rows inherit this cut, so a wrong one is paid five times.** This is the first of five
  extractions. `.github#2726`–`#2729` each depend on whatever the Kernel turns out to be. That is why
  the route is `sdd-required` for a change whose diff is mostly `git mv`.
- **The obstacle is the test project, not production coupling.** `.github#2724` measured it at
  `479d185a`: of the 77 surviving exports on `Client`, exactly **four** are required by production
  code — `run`, `whoami`, `followupAudit`, `predicate`, all from `Program.fs` — and the other **73**
  are held open by `tests/FS.GG.Coord.Cli.Tests` alone. So "tests are moved, not duplicated" is the
  hard half of this work, not a tidiness note. A cut chosen without confronting the 4-vs-73 split
  produces a Kernel whose only real client is the test project.
- **Relocation and extraction are different risks and must not be priced the same.** `Options.fs`
  (2,099), `Render.fs` (696), `Json.fs` (72) and `RefParsing.fs` (57) already exist as separate files
  and already carry signature files. Moving them is a project move of whole files, verbatim. Only the
  block lifted out of `Client.fs` is an extraction.
- **A signature file is a decision about what reaches a consumer, and this work is the first to move
  code under one.** Because `Client.fsi` now exists, the compiler discards every `///` comment in
  `Client.fs` — 1,714 lines of them, measured. Every relocated block either lands in a Kernel `.fsi`
  and reaches consumers again, or lands behind a signature and stays discarded. That is decided for
  thousands of lines whether or not it is decided deliberately, so the rule is stated in the
  specification and applied uniformly rather than left to the diff.
- **A conditional blocking criterion is not a checkbox.** `FS.GG.Coord.Cli` is `PackAsTool` and packs
  its whole output directory, so a new assembly and its `.pdb` become `payloadSha256` entries. If the
  release saga or a payload manifest check does not tolerate the added entries, that is a blocking
  finding on its own row before `.github#2726` proceeds — confirmed by execution against the real
  manifest path, not by reading the code.

## Scope Boundaries
- Keep SDD lifecycle ownership separate from optional Governance enforcement.
- The subject is a new `src/FS.GG.Coord.Cli.Kernel` project, the modules that move into it, and the
  test project that covers them. Behaviour is out of scope in both directions: no verb's exit
  contract, output, or IO may change.
- **The three `let mutable private` forward declarations in `Client.fs` are out of scope.**
  `generatedPathCollector`, `completeDelivery` and `followupAuditContextOverride` are the *evidence*
  motivating the programme. `.github#2727` owns replacing `completeDelivery` with a real dependency
  inversion; beginning it here would turn a compiler-checked move into a design change.
- **A general documentation sweep of `Client.fs` is out of scope.** `.github#2730` owns detecting and
  repairing the discarded-`///` condition across the file and is live in its repair phase under
  another worker, holding `src/FS.GG.Coord.Core`, `src/FS.GG.Coord.GitHub` and
  `tests/source-coherence`. The prose carried across by this work is this work's; the rest is not.
- `src/FS.GG.Coord.Core` and `src/FS.GG.Coord.GitHub` are not touched. The engine confirms the two
  lanes are disjoint.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2725-cli-kernel-extraction`.
