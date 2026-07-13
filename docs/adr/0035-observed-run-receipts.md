# ADR-0035: Observed run receipts — a test obligation is satisfied by a run SDD *read*, not by a `pass` an agent *typed*

- **Status:** Proposed
- **Date:** 2026-07-11
- **Affects:** **FS.GG.SDD** (owner — the receipt shape, the `evidence` parse/record, the `verify` ladder, the failure-leg test), **FS.GG.Governance** (the enforcement half — receipt *freshness* and whether an unobserved obligation may cross a merge boundary), **.github** (this ADR; instance (j) of epic [#266](https://github.com/FS-GG/.github/issues/266))

## Context

### The defect

For the single most consequential fact in the lifecycle — **did this actually pass?** — the authoring agent is the source of truth, and nothing cross-checks it ([FS.GG.SDD#350](https://github.com/FS-GG/FS.GG.SDD/issues/350), verified against `FS.GG.SDD@v0.10.0`).

- Evidence disposition `"supported"` is reached (`HandlersEvidence.fs:679`) **solely** by `normalizedEvidenceResult declaration.Result = "pass"`.
- Verify's **test** disposition `TD-` reaches `"satisfied"` (`HandlersVerify.fs:243`) — again solely from `result = "pass"`. **Despite the name, it observes no test.**
- **No test runner is ever invoked.** `Process.Start` appears exactly once in `src/` (`CommandEffects.fs:211`) and serves only `scaffold`'s provider and `upgrade`'s self-update.

Demonstrated end-to-end: a lifecycle walked on pure tool-generated boilerplate, with `result: pass` / `synthetic: false` hand-written on all five obligations and citing a file that does not exist, reached `ship` **succeeded**, `shipEvidenceSupported: 5`, zero diagnostics. **No code written. No test run. Ninety seconds.**

The gates that do exist are **honesty-of-disclosure** gates (undisclosed `synthetic`, deferral rationale/owner/scope). They constrain *how the agent describes* its attestation — never whether anything ran.

### Why this is #266, not a local bug

Epic #266 ratified the rule this violates:

> A coherence gate must fail closed when its subject is absent, stale, or unreachable. **Compare against reality (the feed, the tag, the file on disk), not against a record of reality.**

`evidence.yml` is a *record of reality*. A test run is *reality*. This is instance (j), and it is the one that is load-bearing for every work item the org ships.

It also contradicts SDD's own Principle VII — *"agents author; they do not become a second source of truth"*. Here the agent **is** the source of truth for the merge-boundary verdict.

### The pressure is real and measured

This is not a hypothetical exploit. [FS.GG.SDD#351](https://github.com/FS-GG/FS.GG.SDD/issues/351) records a real run producing **36 evidence entries for a two-file work item** — 13 near-identical `EV###`, each a projection of an `FR###` the tool itself derived. Nobody carefully considers the 34th entry. Rubber-stamping is the *expected* behaviour under that ceremony ratio, and #350 proves it is undetectable.

### Most of the mechanism already exists

The cost of fixing this is far lower than it looks, because the shape was already anticipated and then never filled in:

| Piece | State today |
|---|---|
| `RunProcess of command * args * workingDir` | **Already an MVU effect** (`CommandTypes.fs:721`), interpreted at the `CommandEffects.fs:195` edge. No new effect kind is needed. |
| `evidence --from-tests <path>` | **Already exists** (`HandlersEvidence.fs:260`) — and only stamps the path *string* into `sourceRefs`. |
| `EvidenceSourceReference.Digest` / `.Result` | **Already fields.** Always `None`. The data model already has slots for a receipt; nothing ever populates them. |

### The constraint that decides the shape

FS.GG.SDD's constitution is explicit: **do not put provider- or toolchain-specific knowledge into generic SDD.** A design where `evidence` shells out to `dotnet test` embeds a .NET-specific command in a lifecycle tool that must also serve Rust, TypeScript, and Godot workspaces. The moment it is made configurable it becomes a provider contract — i.e. it collapses back into "read an artifact somebody else produced".

### Interaction with ADR-0026

[ADR-0026](0026-committed-compact-ship-verdict.md) commits a compact ship verdict to git history. **A verdict that certifies unverifiable claims is worse once it is permanent.** Landing 0026 without this decision durably records green verdicts that mean nothing.

## Decision

**SDD ingests an observed run receipt. SDD never runs a test.**

1. **Record, don't declare.** `fsgg-sdd evidence --from-tests <path>` **parses** a runner-produced report (TRX / JUnit XML — declared, versioned formats) and records an **observed-run receipt** on the evidence entry: the source path, a `sha256` of the report's bytes, the run outcome, and pass/fail/skip counts. The receipt is *recorded from an artifact SDD read*, not typed by the agent.

   ```yaml
   - id: EV001
     kind: verification
     subject: { type: task, id: T001 }
     result: pass
     synthetic: false
     observedRun:                          # recorded by the tool, not authored
       source: artifacts/test-results.trx
       digest: sha256:9f2c…                # of the report's bytes
       outcome: passed
       passed: 1630
       failed: 0
   ```

2. **`verify` fails closed.** A **test** obligation cannot reach `satisfied` on `result: pass` alone. It requires a matching `observedRun` receipt. `result: pass` with no receipt gets a new, non-satisfying disposition (`unobserved`) — it is neither a lie nor a pass, and it is *visible*.

3. **The boundary — who owns what.** This is the question the ADR exists to settle:

   | | Owner |
   |---|---|
   | The receipt's **shape**, its parse, its record, and whether `verify` counts it | **FS.GG.SDD.** It is a lifecycle artifact contract. SDD *reports* readiness. |
   | Whether a receipt is **fresh enough**, and whether an `unobserved` obligation may **cross a merge boundary** | **FS.GG.Governance.** Effective evidence freshness and gate *enforcement* are already Governance-owned; this is that, not a new concern. |

   SDD reports `unobserved`; it never enforces. Governance decides what an `unobserved` obligation costs you.

4. **Do this regardless, and first.** `ship` surfaces `evidenceSelfAttested: N` beside `shipEvidenceSupported: N`. It is nearly free, it is non-breaking, and it makes the asymmetry *visible* today — so nobody reads the green as proof while the rest lands. It is an **interim disclosure, not a substitute** for (1)–(3).

### What this does *not* claim

An agent can fabricate a TRX file. **This decision does not make evidence unforgeable, and it must not be sold as if it did** — that would be the same overclaim in a new place.

What it does is move the bar from **assertion** to **artifact**: from a word an agent typed in a file it also authored, to a structured report, of a declared format, whose bytes are hashed, whose counts must be internally consistent, and whose existence is checked (per [#349](https://github.com/FS-GG/FS.GG.SDD/issues/349)). Forging that is a deliberate act, not the path of least resistance — which is precisely what `result: pass` is today.

**Trust in the receipt's *provenance* is CI's job, not SDD's.** A receipt produced by the suite running in CI, in a context the agent does not control, is trustworthy; one produced on a developer's laptop is as trustworthy as the laptop. SDD's job is to make the receipt *exist, parse, and be checked*. Whether to trust its origin is a Governance/CI policy question, and the ADR deliberately leaves it there rather than pretending a hash settles it.

## Consequences

### Obliges FS.GG.SDD

- Extend the `evidence.yml` schema with `observedRun` (**additive**; schema version bump).
- Parse TRX + JUnit XML. Reject an unparseable or self-inconsistent report (`failed > 0` with `outcome: passed`) with a blocking diagnostic rather than silently recording it.
- Add the `unobserved` disposition to the `verify` ladder and surface it in `verify.json` / `ship.json`.
- **The committed failure-leg test #266 demands**: a lifecycle walked end-to-end on pure scaffolding with fabricated evidence **must not reach `shipReady`**. Per #266's own open note, a fix whose failure leg is untested is how this class survives.

### Obliges FS.GG.Governance

- Decide the enforcement policy for `unobserved`, and receipt freshness. SDD ships the fact; Governance ships the verdict. **No Governance runtime is required for SDD to emit the fact** — the dependency stays one-directional, as it is today.

### Registry + architecture-map obligation — **at resolution, not at proposal**

This ADR changes no repo, no boundary, and no on-disk surface: it *applies* the existing
"SDD reports, Governance enforces" line rather than redrawing it, and it is `Proposed`.

But resolving it will touch the §5 contract picture, and that obligation is recorded here so it
is not lost between the proposal and the patch:

- **`governance-handoff`** (`registry/dependencies.yml`, `version: 1.0.0`, `surface:
  readiness/<id>/governance-handoff.json`, owner `sdd`, consumer `governance`) gains the
  `unobserved` disposition. Additive → **minor bump within `1.x`**; no consumer break.
- `registry/dependencies.yml` is updated **first**, then `docs/architecture.md` §5 is reconciled —
  per `docs/coordination/README.md#system-overview--the-architecture-map`, the map is reconciled
  *after* the registry, not instead of it.
- The implementing feature spec owns both. **A PR that lands the receipt without the registry bump
  should be treated as incomplete**, not as a follow-up.

### Migration — this is a breaking change to the evidence contract

Every existing `evidence.yml` with `result: pass` and no receipt currently reaches `satisfied`; under this decision it becomes `unobserved`. That is the entire point, and it will turn work items that report ship-ready today into work items that do not.

Staged, so the org is not stopped dead:

1. **Disclose** — `evidenceSelfAttested: N` (decision 4). Non-breaking. Ship now.
2. **Warn** — `unobserved` is emitted and reported, but still satisfies. Everyone sees their true number.
3. **Fail closed** — `unobserved` stops satisfying. Flipped once the fleet is green, on a schema major.

### Rejected alternatives

- **SDD runs the suite itself** (`dotnet test` via the existing `RunProcess` edge). Strongest guarantee — the tool observes the run first-hand rather than trusting a handed-over artifact. **Rejected**: it puts toolchain knowledge inside generic SDD, violating the "no provider-specific commands in generic SDD" rule. Making it configurable turns it into a provider contract, which is decision (1) with extra steps. A future ADR may add a *provider-supplied* run command; this ADR deliberately does not.
- **Disclosure only** (`evidenceSelfAttested: N`, and stop). **Rejected as the endpoint**, adopted as step 1: it makes `ship` honest about certifying paperwork without making it certify work. #350's acceptance requires that a fabricated lifecycle *cannot* reach `shipReady`, and a counter does not achieve that.

### Open questions

- Which report formats at v1 — TRX + JUnit only, or a neutral `run-receipt.json` SDD defines, with adapters? (Leaning: TRX/JUnit, because the org's runners already emit them and a new format nobody produces is a receipt nobody records.)
- Does an obligation whose subject is *not* a test (`visual-inspection`, contract impact) get an analogous receipt, or stay authored? (Leaning: stays authored + `synthetic` disclosure — judgement is not observable, and pretending otherwise re-creates the ceremony problem #351 names.)
