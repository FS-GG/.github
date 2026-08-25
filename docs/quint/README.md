# Quint specification experiments

This directory contains the experiment that led to
[ADR-0077](../adr/0077-quint-first-typed-specification-authority.md), which selects Quint as the target
canonical authoring language for future Typed SDD work rather than merely as another proof backend.
It models the same two materially different S.I.R. slices used by the adjacent
F* experiment and keeps source, reproducible checking, typed-IR evidence,
negative controls, and findings together.

The experiment is not S.I.R. runtime authority. Its source snapshot is
`EHotwagner/S.I.R.` commit
`b24c1bfbaa2b0904468c9490e4704bf1bd0ed6e3`; the reports identify the exact
source files and distinguish implemented behavior from accepted design.

## Contents

- `combat/SIRCombatConsequences.qnt` models committed health, wound,
  incapacity, and suppression consequences over bounded input domains.
- `communication/SIRCommunicationNetwork.qnt` models weakest-link route
  capacity, latency, relay behavior, and monotonic observation merging.
- `reports/` records traceability, checked properties, typed-IR findings, and
  the authoring-language assessment.
- `toolchain.json` pins Quint, Apalache, and the Java runtime plus their source
  archive digests.
- `verify.sh` typechecks both modules, emits typed Quint IR to a temporary
  directory, runs executable examples, symbolically checks their invariants,
  and proves two injected defects produce ITF counterexamples.

## Verify

From the repository root:

```bash
docs/quint/verify.sh
```

The first run downloads approximately 180 MB of compressed toolchain archives.
Set `QUINT_SPIKE_CACHE` to choose the download/cache directory. Generated Quint
IR, Apalache output, logs, and ITF traces are written only below temporary or
cache directories reported by the script; verification does not write derived
evidence into the repository.

The run uses:

- Quint 0.32.0 from its exact npm archive, checked by SHA-512;
- Apalache 0.56.1 from its exact release archive, checked by SHA-256; and
- Eclipse Temurin JRE 21.0.12.1+1, checked by SHA-256.

The Quint archive is pinned, but its npm package declares ranged transitive
dependencies and publishes no shrinkwrap. Consequently this experiment is not
yet a hermetic npm installation. A production decision would need a committed
lock, a content-addressed bundle/container, or an upstream standalone artifact.

## Accepted authority boundary

Quint 0.32.0 can emit resolved JSON IR plus inferred type and effect maps. That
makes this architecture technically possible:

```text
canonical .qnt source
       -> pinned Quint typed IR
       -> versioned FS-GG Quint-profile validation
       -> small language-neutral compiled contract
       -> F# bindings, semantic diff, docs, CI impact, schemas, and replay

canonical .qnt source
       -> Quint run/test/verify
       -> ITF traces and behavioral proof evidence
```

The raw Quint IR is compiler-owned and is not the stable FS-GG contract. The generated contract contains
stable integration facts and does not reproduce arbitrary Quint expression semantics. Implementation has
not started: the published F# specification kernel remains production authority until the
[migration design](../coordination/2026-08-25-quint-first-typed-sdd-migration-design.md) completes its
qualification, publication, and explicit consumer rollout.

Model-based testing does not turn this experiment or FS.GG.SDD into the owner of S.I.R. behavior. The
producer owns pinned Quint/ITF mechanics and a generic replay protocol. A test-only S.I.R. qualification
child owns the real interpreter adapter, observable-state projection, and product fixtures; Q4 later
productionizes the accepted boundary when canonical rule authority moves. These research models remain
evidence and are not copied into production by implication.

That qualification also evaluates the upstream
[`quint-co/quint-llm-kit`](https://github.com/quint-co/quint-llm-kit) language, modeling, verification,
implementation, and refactoring guidance. It is an Apache-2.0 authoring accelerator, not authority. FS-GG
will pin and test any adopted revision, avoid moving installers/latest-version assumptions, and retain its
own profile, evidence, migration, and authorization rules.
