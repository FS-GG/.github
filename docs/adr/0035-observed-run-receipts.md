# ADR-0035: Observed run receipts — a test obligation is satisfied by a run SDD *read*, not by a `pass` an agent *typed*

- **Status:** Accepted (2026-07-14) — decisions (1)–(4) have **landed**; what remains is the *stage-3
  default flip*, which this ADR itself defers to "once the fleet is green" (see
  [Migration](#migration--this-is-a-breaking-change-to-the-evidence-contract)). `Accepted` here means
  **decided and binding**, not fully rolled out — the flip is scheduled work under an accepted
  decision, not an open question ([#715](https://github.com/FS-GG/.github/issues/715)).
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

### Registry + architecture-map obligation — **at resolution, not at proposal** — ✅ PAID

This ADR changes no repo, no boundary, and no on-disk surface: it *applies* the existing
"SDD reports, Governance enforces" line rather than redrawing it.

Resolving it touched the §5 contract picture, and that obligation was recorded here so it would not
be lost between the proposal and the patch. It was **paid on 2026-07-14** by
[.github#701](https://github.com/FS-GG/.github/issues/701) / [PR #713](https://github.com/FS-GG/.github/pull/713):
`registry/dependencies.yml` first (`governance-handoff` → **1.1.0**), then `docs/architecture.md` §5,
then the `compatibility.md` projection — per
`docs/coordination/README.md#system-overview--the-architecture-map`, the map is reconciled *after*
the registry, not instead of it. The implementing feature spec owned both, and **a PR that lands the
receipt without the registry bump is incomplete**, not a follow-up.

> ⚠️ **This section originally said `governance-handoff` "gains the `unobserved` disposition". That
> was WRONG, and it is corrected here rather than quietly dropped — the sentence misled the worker who
> paid the obligation, and an ADR is exactly the wrong place to leave a claim that has been disproved.**
>
> `unobserved` is a **`TD-` test-disposition state in `verify.json`**. It is **never persisted** to
> `readiness/<id>/governance-handoff.json`. The handoff's enums are unmoved — `readiness.shipDisposition`
> stays `{shipReady, blocked}`, `readiness.verificationReadiness` stays
> `{verificationReady, needsVerificationCorrection}`, `evidence.nodes[].state` stays
> `{pending, real, synthetic, failed, skipped}` — and its `schemaVersion` stays `1`.
>
> What the contract actually gained is **one blocking diagnostic id**, `ship.unobservedEvidence`,
> newly reachable in `readiness.blockingDiagnosticIds[]`. Additive, so the **minor bump within `1.x`**
> the obligation called for was the right bump — for the wrong stated reason.
>
> The divergence was deliberate on SDD's side, not an oversight: [FS.GG.SDD#422](https://github.com/FS-GG/FS.GG.SDD/pull/422)
> withheld the state from the `ED-` ladder *on purpose*, because giving `ED-` an `unobserved` state
> "would instead change a persisted enum on the governance-handoff surface — a schema change this
> stage deliberately does not make". This ADR was written at proposal; the code is what shipped.

~~**Still outstanding, and it is what makes the bump real:** SDD stamps a hardcoded `contractVersion`
into every emitted handoff and it still reads `1.0.0`, so the registry declares `1.1.0` while the
artifact self-declares `1.0.0` — and **no gate compares the two**. Tracked as
[FS.GG.SDD#427](https://github.com/FS-GG/FS.GG.SDD/issues/427) and as coherence id
`governance-handoff-emitted-version` (`coherent: false`). It is harmless only while the default stays
off; **it must land before the stage-3 flip below**, or a consumer meets a value its declared contract
never announced — the precise failure this obligation exists to prevent.~~

**Paid on the SDD side; one clause of it survives, and it is ours.** The paragraph above is struck
whole rather than edited clause-by-clause, because its *conclusion* — that this blocks the stage-3b
flip — is what a reader acts on, and that conclusion is false even though one of its premises
(*"no gate compares the two"*) is still true. Leaving the true clause unstruck inside a false
paragraph is how the next reader re-derives the wrong answer.

> ⚠️ **The struck text above was true when written and is now FALSE — the SDD-side half was paid on
> 2026-07-14 by [FS.GG.SDD#427](https://github.com/FS-GG/FS.GG.SDD/issues/427), which closed the same
> day [#715](https://github.com/FS-GG/.github/issues/715) reconciled this ADR's header. That edit fixed
> the status line and left this claim behind, which is how the two came apart.** Corrected in place
> rather than rewritten, for the same reason as the block above: a reader deciding the stage-3b flip
> reads *"two things must land"*, checks this one, finds it unmet **on this ADR's word**, and stops —
> when it is in fact paid ([#1082](https://github.com/FS-GG/.github/issues/1082)).
>
> **What was paid — the stamp.** Measured in FS.GG.SDD @ `7c6c78a`: nothing is hardcoded any more.
> `GovernanceHandoff.fs:261` and `ReleaseContract.fs:590-592` both **read**
> `Fsgg.Schemas.governanceHandoffContractVersion` (`src/FS.GG.Contracts/Schemas.fs:185` = `"1.1.0"`),
> anchored by `SchemaVersionConstantTests.fs:55-58`. The artifact self-declares `1.1.0` and the registry
> declares `1.1.0`. **They agree.** The hand-kept mirror the registry warned about is gone: emitter and
> constant are now **one literal**, so they cannot drift — a structural fix, which is stronger than the
> gate that was asked for.
>
> **What is still owed — the comparison, and it is not SDD's.** Nothing compares SDD's constant to the
> `version` `registry/dependencies.yml` declares, so `governance-handoff` remains free to advance here
> while SDD stamps something older. Both repos independently wrote that gap down and both named
> **`.github`** as its owner; neither built it. It is now tracked at
> [**#1085**](https://github.com/FS-GG/.github/issues/1085) — a live issue rather than a closed one.
>
> **#427 was two findings under one title** — *the stamp is wrong* (paid, SDD's) and *nothing compares
> the two* (owed, ours). This ADR collapsed them into a single precondition and attributed the whole of
> it to SDD. The precondition **on SDD** is discharged; the gate **on `.github`** is not, and it is the
> flip's real remaining contract-version risk.

### Migration — this is a breaking change to the evidence contract

Every existing `evidence.yml` with `result: pass` and no receipt currently reaches `satisfied`; under this decision it becomes `unobserved`. That is the entire point, and it will turn work items that report ship-ready today into work items that do not.

Staged, so the org is not stopped dead:

| | stage | state |
|---|---|---|
| 1 | **Disclose** — `evidenceSelfAttested: N` (decision 4). Non-breaking. | ✅ **landed** — [FS.GG.SDD#398](https://github.com/FS-GG/FS.GG.SDD/issues/398) |
| 2 | **Record** — the `observedRun` receipt; TRX/JUnit parsed, hashed, and checked. | ✅ **landed** — [FS.GG.SDD#415](https://github.com/FS-GG/FS.GG.SDD/issues/415) |
| 3 | **Fail closed** — `unobserved` stops satisfying. | ⚙️ **mechanism landed, default OFF** — [FS.GG.SDD#422](https://github.com/FS-GG/FS.GG.SDD/pull/422) shipped it opt-in behind `--require-observed` (on **both** `verify` and `ship`), with the failure-leg proof #266 demands. |
| 3b | **The flip** — `--require-observed` becomes the default. | ✅ **landed 2026-07-17** — [FS.GG.SDD#526](https://github.com/FS-GG/FS.GG.SDD/pull/526) shipped it as the `0.14.0` breaking cut, on the human decision at [FS.GG.SDD#497](https://github.com/FS-GG/FS.GG.SDD/issues/497). Flipped **ahead of the fleet being green** — a deliberate override of the receipts-recorded precondition below; see the ⚠️ note. |

~~**The flip is the only thing left, and it is deliberately not scheduled here.** It is gated on
*"once the fleet is green"* — and the fleet is not: no `evidence.yml` in the org yet carries a
receipt, so flipping today would turn every ship-ready work item in every FS-GG repo not-ship-ready
at once, with no remedy available. Accepting this ADR does **not** flip it. What accepting it settles
is that the flip *is coming* and that work should be planned against it, not that it happens now.~~

> ⚠️ **The struck text above was true when written and is now FALSE — the flip landed 2026-07-17
> ([FS.GG.SDD#526](https://github.com/FS-GG/FS.GG.SDD/pull/526)), and it landed *before* the
> fleet-green precondition it names was met.** A human (@EHotwagner) accepted the schema major on
> [FS.GG.SDD#497](https://github.com/FS-GG/FS.GG.SDD/issues/497) and directed the flip now,
> **explicitly overriding** the "receipts are actually recorded" precondition — which is still
> unmet at **0 of 25** `evidence.yml` ([FS.GG.SDD#511](https://github.com/FS-GG/FS.GG.SDD/issues/511)).
> Corrected in place rather than deleted, for this ADR's usual reason: a reader who sees "deliberately
> not scheduled" and stops would conclude the default is still off, which shipped code now contradicts.
>
> **What that means for consumers.** The break is real and now default: `verify`/`ship` block an
> unobserved `result: pass`. It shipped with the `--no-require-observed` opt-out (a migration window)
> and a mandatory migration note (`docs/release/migrations/0.14.0.md`), so a work item that has not yet
> adopted receipts is not stopped dead — but the org SHOULD now record receipts rather than plan to.
> The precondition being overridden rather than met makes recording the first real receipt
> ([FS.GG.SDD#511](https://github.com/FS-GG/FS.GG.SDD/issues/511)) **remediation behind a shipped
> default**, not a gate ahead of it.

~~**Two things must land before the flip**~~ — **ONE does**, and this list said two until
[#1082](https://github.com/FS-GG/.github/issues/1082). The count is corrected in place rather than
quietly edited down, because a reader who checks a phantom precondition, finds it unmet on this ADR's
word, and stops is the exact harm the stale entry caused:

- **Receipts must actually be recorded.** The gate is only fair once the fleet can pass it. **This is
  the precondition, and it is genuinely unmet** — measured at **0 of 25** `evidence.yml` in FS.GG.SDD
  carrying a receipt ([FS.GG.SDD#511](https://github.com/FS-GG/FS.GG.SDD/issues/511)). It is the
  binding constraint, and while the entry below sat beside it looking equally unmet, it got half the
  attention it was owed.
- ~~**[FS.GG.SDD#427](https://github.com/FS-GG/FS.GG.SDD/issues/427)** — the emitted `contractVersion`
  still says `1.0.0` (see the obligation section above). Flip before that, and Governance meets
  `ship.unobservedEvidence` under a contract version that never declared it.~~ **PAID 2026-07-14, and
  no longer a precondition at all.** The emitter reads the constant; both sides declare `1.1.0`; the
  consumer this bullet protected can no longer meet an undeclared value, because there is nothing
  undeclared. See the ⚠️ correction in the obligation section above.

  **What survives is a safeguard, not a gate on the flip, and it is `.github`'s not SDD's:** nothing
  compares SDD's constant to the `version` this org's registry declares
  ([**#1085**](https://github.com/FS-GG/.github/issues/1085)). The two agree **today** and are kept so
  **by hand, across a repo boundary** — so the risk #1085 addresses is *drift before the flip*, not the
  flip itself. Land it and the hand-kept step stops being load-bearing. Do not re-list it above as a
  third precondition: that is how this table acquired a phantom in the first place.

### Rejected alternatives

- **SDD runs the suite itself** (`dotnet test` via the existing `RunProcess` edge). Strongest guarantee — the tool observes the run first-hand rather than trusting a handed-over artifact. **Rejected**: it puts toolchain knowledge inside generic SDD, violating the "no provider-specific commands in generic SDD" rule. Making it configurable turns it into a provider contract, which is decision (1) with extra steps. A future ADR may add a *provider-supplied* run command; this ADR deliberately does not.
- **Disclosure only** (`evidenceSelfAttested: N`, and stop). **Rejected as the endpoint**, adopted as step 1: it makes `ship` honest about certifying paperwork without making it certify work. #350's acceptance requires that a fabricated lifecycle *cannot* reach `shipReady`, and a counter does not achieve that.

### Open questions

- ~~Which report formats at v1 — TRX + JUnit only, or a neutral `run-receipt.json` SDD defines, with
  adapters?~~ **Settled by the implementation: TRX + JUnit**, as the leaning predicted — the org's
  runners already emit them, and a new format nobody produces is a receipt nobody records
  ([FS.GG.SDD#415](https://github.com/FS-GG/FS.GG.SDD/issues/415)). A report recording **no executed
  tests** (`passed + failed = 0`) is refused rather than recorded: a run in which nothing executed
  proves nothing.
- Does an obligation whose subject is *not* a test (`visual-inspection`, contract impact) get an analogous receipt, or stay authored? (Leaning: stays authored + `synthetic` disclosure — judgement is not observable, and pretending otherwise re-creates the ceremony problem #351 names.)
