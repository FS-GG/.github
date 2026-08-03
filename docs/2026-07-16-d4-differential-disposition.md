# D.4 — the five `51-fs-flip.sh` differential assertions, disposed on the record

**Date:** 2026-07-16
**Owner:** `.github` (the coordination engine)
**Governs:** the deletion of `bash scripts/fsgg-coord` in [ADR-0040](adr/0040-port-the-io-layer.md) Phase D.4
**Companion:** [the Phase D plan](2026-07-15-phase-d-corpus-through-shim-plan.md) §5 D.4

ADR-0040's "Phase D contradiction, and its resolution" (#756) is explicit that deleting bash removes a
handful of `51-fs-flip.sh` assertions whose **subject** — the bash decision engine and its `--engine bash`
escape hatch — no longer exists, and that this removal must be **recorded, not silent**:

> When the differential harness is deleted, land a manifest that **names the removed assertions and their
> disposition** — 1–2 subsumed by the ADR-0038 corpus-against-`fs`, 3–5 retired with the escape hatch. Not
> a silently shrinking gate; a documented retirement. **A silently shrinking gate is the failure; a
> documented retirement is not.**

This file is that manifest. The five assertions below drove `bash scripts/fsgg-coord-bash` as a
comparison *oracle* against the engine (`--engine fs` vs `--engine bash`, and the two against each other).
With bash deleted there is no oracle to compare against — but every **property** each one asserted is still
proven, one transport under, by the engine's own gates. Each row names where.

## The `fs`-returns-bash's-answer pair — **SUBSUMED**

| # | assertion (verbatim from `51-fs-flip.sh`) | disposition |
|---|---|---|
| 1 | `fs: on a board the engines AGREE about, fs returns bash's items (the flip is a no-op there)` | **subsumed** |
| 2 | `fs: ...and the same exit code` | **subsumed** |

These pinned that `--engine fs` returned the SAME items and exit code as `--engine bash` on a board the two
engines agreed about — i.e. that the engine's answer *matched* bash's. That property is exactly what the
**ADR-0038 defect-corpus-against-`fs`** (a precondition of D.1) asserts directly, and what D.1's
`tests/coord-engine-parity/` harness now proves for **all 27 of 27 corpus cases** (~445 assertions): the
compiled engine, over HTTP, returns the answer the shell corpus **certifies** — the corpus's own contract,
not bash-at-runtime. The engine's answer is no longer *compared to* bash's; it **is** the answer, held to
the certified golden. Comparing it to a second, now-deleted implementation adds nothing the certified
corpus does not already hold it to. (`tests/coord-engine-parity/run.sh` case 22: `batch --repo rendering →
["FS-GG/FS.GG.SDD#70","FS-GG/FS.GG.SDD#74"]`, and the exit-code contract in case 52 / `52-take-exit-codes-585`.)

## The `--engine bash` escape-hatch triple — **RETIRED**

| # | assertion (verbatim from `51-fs-flip.sh`) | disposition |
|---|---|---|
| 3 | `fs: --engine bash is byte-identical to the pre-flip answer` | **retired** |
| 4 | `fs: ...and its exit code too — the rollback is exact` | **retired** |
| 5 | `fs: --engine bash never consults the engine at all (a stale one cannot break the hatch)` | **retired** |

These pinned the **escape hatch** — that `--engine bash` reproduced the pre-flip tool byte-for-byte and
never even looked for an engine, so a rollback to bash was exact. The escape hatch **is the thing being
deleted.** `--engine bash` is removed *because there is no bash left to be*: the ~7,132-line monolith at
`scripts/fsgg-coord-bash` is gone, and with it the `--engine` flag it alone parsed (the shim
`scripts/fsgg-coord` is a transparent pass-through that never parsed `--engine`, and the engine rejects it
as an unknown flag). An assertion that a deleted rollback target is byte-exact has no subject. This is a
**retirement** in ADR-0040's precise sense — *removing an assertion because its subject no longer exists* —
not a reduction of the corpus.

## What is NOT lost

- The engine's answer is still held to the corpus's certified golden — that is the whole of D.1
  (`tests/coord-engine-parity/`, ~445 assertions across all 27 cases), and it survives D.4 unchanged.
- The engine's **fail-closed** guarantees that `51-fs-flip.sh` §3 pinned for `--engine fs` (a missing /
  stale / mute engine is FATAL, never a silent substitution) are structural in the engine and proven by
  its own unit + e2e suites; they were never a property of *bash*, so deleting bash does not touch them.
- The touch-set grammar and the `FSGG-PATHS` verify-paths markers, which two gates used to cross-check
  against bash's copy, are now cross-checked against the **engine's** copy (`Schedulability.TouchSetGrammar`
  via `facts`, and `src/FS.GG.Coord.Cli/Client.fs`'s markers) — one engine, one home (#485), no second
  copy to drift.

*This manifest executes ADR-0040 Phase D.4. It does not amend the ADR. Where the two differ, the ADR governs.*
