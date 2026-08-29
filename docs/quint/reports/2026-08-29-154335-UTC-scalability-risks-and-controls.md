# Quint scalability risks and controls

**Recorded:** 2026-08-29 15:43:35 UTC

**Scope:** Quint 0.32.0, Rust and TypeScript simulators, Apalache 0.56.1, and the TLC backend

**Decision target:** Quint-first v2 migration, especially GS2-03.4 and GS2-03.5

## Executive verdict

Growing the Quint corpus does not require one permanently growing verification project. Source modules may
remain together for reuse, but CI should expose multiple small, independently executable root modules whose
transitive import closures and state spaces are deliberately bounded. Merely moving declarations into files
does not create isolation: Quint flattens the selected root and the declarations transitively used by it.

The benchmark found five distinct scaling axes:

1. **Dependency depth:** a chain of 2,000 pure definitions typechecked, then reproducibly crashed `quint
   compile` in `Flattener.enterApp` with `RangeError: Maximum call stack size exceeded`. A 1,000-deep chain
   compiled. This is a structural recursion limit, not a line-count limit.
2. **Root breadth:** shallow independent definitions avoided that crash, but 4,000 definitions required
   89–99 seconds and about 1.34 GiB to typecheck; compile took 88 seconds and emitted 16.6 MB of JSON.
3. **State-space products:** 4, 8, 12, and 16 independent booleans produced 16, 256, 4,096, and 65,536
   distinct TLC states. At 16 booleans the checker generated 589,825 states and peaked near 0.80 GiB.
4. **Bookkeeping state:** tracking 12 independent received-message flags produced 4,097 distinct states;
   a behavior-focused summary of the same completion condition produced 3. This 1,366× reduction is the
   most effective control tested, but it requires an independent semantic oracle so abstraction cannot hide
   the behavior under test.
5. **Samples, steps, and artifacts:** 2,000 simulations of up to 500 steps peaked at 2.47 GiB without MBT
   metadata and 4.20 GiB with it. Both completed locally, but this validates the memory mechanism behind the
   open large-MBT-output report. ITF disk use grew linearly from about 0.41 MB for 100 traces to 4.13 MB for
   1,000.

The v2 migration should therefore adopt **root budgets, bounded profile matrices, abstraction oracles,
chunked traces, and tiered CI now**. Waiting for one large model to become slow would make the eventual split
both harder to validate and more likely to create a second behavioral authority.

## Test setup

The test corpus and runners were generated under a unique `/tmp/quint-scalability-*` directory and were not
added to the product tree. The setup contained:

- deep dependency chains at 100, 500, 1,000, 2,000, and 4,000 definitions;
- shallow independent-definition modules at 1,000, 2,000, and 4,000 definitions;
- eight libraries of 250 definitions, selected by roots importing one, four, or all eight libraries;
- product-state models with 4, 8, 12, and 16 independent booleans;
- message-bookkeeping and behavior-summary models at widths 4, 8, and 12;
- Rust and TypeScript simulation runs, 1/4/12/24 Rust workers, TLC, Apalache, combined/separate properties,
  inductive checking, ITF traces, and MBT output; and
- an Eclipse Temurin JRE downloaded only into the temporary directory.

Commands were timed as child processes. Successful small tests used three or five repetitions where
practical and the tables report medians. Formal checks used two repetitions. The two 4,000-wide typechecks
and single compile are shown as ranges/single observations because each run took roughly 90 seconds. Every
failing deep compile was repeated three times. Timeouts bounded the formal and large-output probes.

### Environment

| Component | Value |
|---|---|
| Host | Linux 7.1.8 x86-64 |
| CPU | AMD Ryzen 9 7900, 12 cores / 24 threads |
| Memory | 61 GiB, no swap |
| Quint | 0.32.0 |
| Node.js | 26.8.1 |
| Rust evaluator | bundled with Quint 0.32.0 |
| Apalache | 0.56.1 |
| TLC | distribution bundled with Apalache 0.56.1 |
| Java | Eclipse Temurin 21.0.12.1+1 LTS |

The temporary inputs and result ledgers were fingerprinted before teardown:

| Artifact | SHA-256 |
|---|---|
| corpus generator | `9b8bd4404c6ea9f516757a252150079553bb10a78a7a0b1fc71d582f1fdf8545` |
| cheap-run matrix | `45e1eb69aa602aac87a9a7308791082c84194717593e894b4a62b272f27f25f2` |
| formal-run matrix | `cc04dbaaf9c35683000d931032b6844453e7aca04818bcc33a59f3c92e9d889d` |
| measurement wrapper | `cb7542a78c7a0486a8e0502ac184d7a42cb111cdc3290490e718242314b70c54` |
| source/compile/simulation ledger | `671ebf793a9372c026b531ebba2b969db44a2843bf1ef120c73df7c4ed11f1d1` |
| shallow-width ledger | `ac9683a99649fa88b31ffd5e00a3b66d28599a66ef7a72ea0bd2a606b7c0674e` |
| formal/output ledger | `a182e57844aba89c804d20b3bbe1a5ef19081d446b54d16b11fcf1b9c4e239b7` |
| upstream Teaching Concurrency input | `7837a9e5a03f94f2670f3db2c05c1cc38c7e7adea0349a34317f83c7c8f1a214` |

These are experiment-identification hashes, not a substitute for committing a benchmark harness if FS-GG
later promotes these probes into release gates.

## Findings

### 1. Deep dependencies fail before large shallow modules do

The deep model made every definition call its predecessor. The shallow control defined the same number of
independent functions and used them from one aggregate expression.

| Shape | Definitions | Typecheck | Compile | Peak RSS | Compiled JSON | Result |
|---|---:|---:|---:|---:|---:|---|
| deep chain | 100 | 0.303 s | 0.349 s | 137 MiB | 0.40 MB | pass |
| deep chain | 500 | 0.593 s | 1.013 s | 223 MiB | 1.97 MB | pass |
| deep chain | 1,000 | 1.073 s | 3.101 s | 270 MiB | 3.95 MB | pass |
| deep chain | 2,000 | 2.423 s | 7.535 s | 326 MiB | none | compile stack overflow, 3/3 |
| deep chain | 4,000 | 7.383 s | 13.708 s | 377 MiB | none | compile stack overflow, 3/3 |
| shallow width | 1,000 | 3.586 s | 3.836 s | 295 MiB | 4.10 MB | pass |
| shallow width | 2,000 | 15.601 s | 19.351 s | 625 MiB | 8.27 MB | pass |
| shallow width | 4,000 | 88.9–98.7 s | 87.927 s | 1.34 GiB | 16.65 MB | pass |

The immediate control is to keep call/expansion structure shallow. It would be unsafe to encode this result
as “fewer than 2,000 lines” or even “fewer than 2,000 definitions”: the shallow 2,000-definition model
passes, while the deep one fails. CI should measure root closure size and dependency depth separately, and
retain the 1,000-pass/2,000-fail shape as a canary until Quint's flattener behavior changes.

The shallow results also indicate superlinear front-end work for this deliberately wide aggregate. A pass is
not proof of acceptable CI cost. Large generated lists, folds, maps, or repeated expressions should be
benchmarked at representative candidate sizes instead of admitted solely because typechecking succeeds.

### 2. Files are organizational; executable roots provide isolation

The official transpiler architecture says flattening embeds into the root all imported definitions and the
definitions they transitively use. The benchmark confirmed that increasing one root's used import closure
increased both compile time and emitted IR even though the source was already split across files.

| Root closure | Definitions available through libraries | Typecheck | Compile | JSON |
|---|---:|---:|---:|---:|
| one library | 250 | 0.437 s | 0.612 s | 0.94 MB |
| four libraries | 1,000 | 0.842 s | 1.996 s | 3.78 MB |
| eight libraries | 2,000 | 1.396 s | 5.299 s | 7.62 MB |

The eight-library root emitted 8.06× the JSON and took 8.65× the compile time of the one-library root. The
strategy is therefore:

- share small vocabulary and pure helpers through modules;
- define separate runnable roots for claim/election, relation mutation, lifecycle, saga, epoch, rollback,
  and cross-domain integration;
- prohibit a convenience “import everything” root from becoming the default PR gate;
- calculate the transitive used closure for every root and budget it in the qualification manifest; and
- reserve a composed root for nightly/release coverage with explicit bounds.

At this small scale, sequentially launching two isolated 8-boolean TLC roots took longer than one 16-boolean
root because fixed startup dominated. But the isolated checks explore 512 distinct states in total versus
65,536 in the composed root—a 128× state-count difference—and can run in parallel. Root isolation is a
state-space and failure-containment strategy, not a promise that every tiny check gets faster.

### 3. State-space growth is exponential and abstraction is decisive

| Independent booleans | Distinct states | Generated states | TLC median | Peak RSS |
|---:|---:|---:|---:|---:|
| 4 | 16 | 49 | 3.422 s | 163 MiB |
| 8 | 256 | 1,281 | 3.560 s | 187 MiB |
| 12 | 4,096 | 28,673 | 3.752 s | 207 MiB |
| 16 | 65,536 | 589,825 | 4.150 s | 803 MiB |

Wall time is deceptively flat here because JVM, translation, and TLC startup dominate the smaller cases.
Generated/distinct states and memory expose the risk earlier. A CI budget should therefore record state
counts and RSS, not only elapsed seconds.

The bookkeeping model represented each received message independently. The summary model represented only
whether the relevant threshold had been observed and whether completion occurred.

| Width | Bookkeeping distinct states | Summary distinct states | Reduction |
|---:|---:|---:|---:|
| 4 | 17 | 3 | 5.7× |
| 8 | 257 | 3 | 85.7× |
| 12 | 4,097 | 3 | 1,365.7× |

This agrees with Quint's published “message soup” experience: it reports at least 3× faster witness finding
and traces shrinking from more than 500 steps to about 37 for one consensus scenario. The technique is
necessary where per-message transport mechanics are not the behavior under review.

Abstraction must not silently weaken the protocol. For GS2-03.4, an independently authored oracle should:

- name the concrete behaviors that the abstraction must preserve;
- exercise boundary cases on the concrete implementation or compiled contract;
- require anti-vacuity witnesses that reach success, refusal, expiry, rollback, and concurrency outcomes;
- compare a bounded concrete slice with the abstract root or a separately implemented oracle; and
- reject an abstraction/profile change unless its semantic effect and state-count delta are reviewed.

### 4. Rust simulation and measured parallelism belong in the fast tier

For 50,000 samples of up to 20 steps, the Rust backend used roughly half the memory of TypeScript and was
materially faster. Representative medians are below.

| Model | Backend/workers | Width 4 | Width 8 | Width 12 |
|---|---|---:|---:|---:|
| bookkeeping | TypeScript / 1 | 8.801 s | 10.044 s | 9.529 s |
| bookkeeping | Rust / 1 | 2.427 s | 2.455 s | 2.444 s |
| bookkeeping | Rust / 4 | 0.554 s | 0.557 s | 0.560 s |
| bookkeeping | Rust / 12 | 0.382 s | 0.384 s | 0.393 s |
| bookkeeping | Rust / 24 | 0.365 s | 0.362 s | 0.358 s |
| summary | TypeScript / 1 | 5.695 s | 5.919 s | 5.692 s |
| summary | Rust / 12 | 0.327 s | 0.341 s | 0.297 s |

On this 12-core/24-thread host, most improvement arrived by 12 workers; 24 workers added little and was
occasionally slower. CI should benchmark a small worker matrix per runner class and pin the best observed
value, not blindly use the reported logical CPU count. Parallelize independent roots at the job level only
after accounting for each process's workers and memory budget, or nested parallelism will oversubscribe the
runner.

The TypeScript backend remains useful as a compatibility/control lane, but it is a poor default for large
simulation sampling on this version.

### 5. Apalache has meaningful fixed cost; batch compatible properties

The small Apalache checks took about 3.0–4.1 seconds. Increasing the synthetic product from 4 to 16 booleans
at three steps increased elapsed time from 3.20 to 4.03 seconds. Increasing the 8-boolean bound from one to
ten steps moved only from 3.37 to 3.47 seconds because the property was tautological and fixed startup and
translation dominated. This does **not** establish that trace depth is cheap for realistic properties; it
establishes that micro-checks cannot be optimized from wall time alone.

Checking eight compatible invariants together took 3.48 seconds. Eight separate checker invocations totaled
27.09 seconds—7.8× longer. Combine compatible properties for encoding/startup efficiency while preserving
named per-property diagnostics. Split a property into a separate job when it needs different bounds,
constants, fairness, backend, timeout, or failure attribution.

The exact Quint 0.32.0 Teaching Concurrency model completed inductive verification locally in 4.28 seconds,
and completed with its correctness property in 4.53 seconds. The open upstream hang therefore did not
reproduce on this host. Inductive checks remain valuable for escaping fixed-depth assurance, but until the
open report is resolved they should be time-bounded and version-pinned, with bounded safety checks retained
as a fallback rather than silently skipped.

### 6. Retention can dominate memory independently of model complexity

| Workload | Wall time | Peak RSS | Retained output |
|---|---:|---:|---:|
| 100 ITF traces, up to 50 steps | 0.330 s | 144 MiB | 0.41 MB / 100 files |
| 1,000 ITF traces, up to 50 steps | 0.695 s | 246 MiB | 4.13 MB / 1,000 files |
| 100 × 50 samples/steps, MBT | 0.379 s | 152 MiB | — |
| 500 × 100 samples/steps, MBT | 0.769 s | 317 MiB | — |
| 2,000 × 500 samples/steps, no MBT | 4.709 s | 2.47 GiB | — |
| 2,000 × 500 samples/steps, MBT | 10.398 s | 4.20 GiB | — |

The upstream `RangeError: Invalid string length` at 2,000 × 500 with MBT did not reproduce under Node
26.8.1, but the same workload's 4.20 GiB peak is enough to make a single unbounded CI command unsafe.

Use chunked batches with deterministic seeds, retain only failures plus a small successful sample, compress
ITF artifacts, and impose per-job byte/file/RSS/time limits. A release lane may merge batch summaries only
if candidate identity, root, constants, bounds, seed range, Quint/evaluator versions, and property set are
part of the cache/evidence key. Never cache a bare green result by source filename.

## CI strategy for the v2 migration

### Pull-request tier

- Typecheck every changed root and validate root-closure/dependency-depth budgets.
- Run examples, anti-vacuity witnesses, and Rust simulation with deterministic seeds.
- Run bounded safety checks only for affected roots and directly dependent integration roots.
- Batch properties that share root, constants, backend, and bounds.
- Fail on timeout, missing witness, missing property, empty/truncated result, or artifact-budget breach.
- Record toolchain, root closure, constants, bounds, seed range, worker count, elapsed time, peak RSS, and
  state/sample counts in the qualification result.

### Main/nightly tier

- Exercise wider constant/bound profiles and the composed cross-domain roots.
- Run TLC where finite explicit-state enumeration gives useful state counts; run Apalache where symbolic
  bounded or inductive checking is more appropriate.
- Repeat concurrency-sensitive simulations across deterministic seed partitions.
- Track historical p50/p95, RSS, state counts, trace bytes, and closure size; alert on both absolute budgets
  and material regressions.

### Release tier

- Verify the exact packaged/pinned Quint source and compiled-contract candidate, not a workspace lookalike.
- Execute the full qualification manifest, independent black-box oracles, mutation controls, and selected
  inductive/liveness checks under hard timeouts.
- Preserve failing counterexamples and a compact evidence manifest; do not retain every successful trace.
- Require review for a bound reduction, abstraction change, root merger, property removal, or evidence-cache
  key change.

## Required roadmap changes

GS2-03.4 should own the independent oracle for abstraction soundness and scale-envelope behavior. It should
force both existing and future Quint roots to demonstrate that state-reduction choices preserve the required
black-box outcomes, including anti-vacuity witnesses and bounded concrete-vs-abstract comparisons.

GS2-03.5 should implement the execution policy: independently runnable roots, measured closure/depth/state
budgets, Rust-first deterministic simulation, batched compatible invariants, TLC/Apalache profiles, hard
timeouts, chunked trace retention, and nightly/release expansion. The qualification manifest from GS2-03.1
should carry those identities and measurements so cached evidence cannot cross candidate or profile
boundaries.

No universal numeric threshold should be copied from this workstation. The measured failure shapes should
become regression canaries, while production budgets should be calibrated on FS-GG's CI runner classes and
representative v2 models.

## Sources and confidence

The measured findings above are local to the stated versions and synthetic models. They are strongest for
identifying mechanisms and regression shapes, not predicting the final protocol's absolute runtime.

Primary upstream evidence:

- Quint's [transpiler architecture](https://github.com/quint-co/quint/blob/main/docs/content/docs/development-docs/architecture-decision-records/adr001-transpiler-architecture.md)
  documents root flattening and transitive embedding.
- Quint's [Message Soup article](https://github.com/quint-co/quint/blob/main/docs/content/posts/soup.mdx)
  reports production-model gains from removing message-by-message bookkeeping.
- The [Quint CLI reference](https://github.com/quint-co/quint/blob/main/docs/content/docs/quint.md)
  distinguishes simulation, symbolic Apalache verification, and explicit-state TLC verification.
- Quint [v0.32.0 release notes](https://github.com/quint-co/quint/releases/tag/v0.32.0) identify the tested
  release and its TLC packaging behavior.
- Open issue [#1965](https://github.com/quint-co/quint/issues/1965) reports the large-MBT-output V8 failure;
  this experiment reproduced high memory growth but not the crash.
- Open issue [#1989](https://github.com/quint-co/quint/issues/1989) reports an inductive-check hang;
  this experiment used its exact tagged input but did not reproduce the hang.

## Final recommendation

Proceed with increasing Quint use in v2, but treat scalability as a qualification property from the first
new root. Keep one source corpus if that preserves shared vocabulary and authority; keep many executable
roots. Optimize semantics before hardware: remove irrelevant bookkeeping, bound domains, and isolate state
products. Then use the Rust backend, measured worker counts, batched properties, checker selection, caching,
and CI tiering. Every optimization that changes the model must be defended by an independent oracle and
anti-vacuity evidence.
