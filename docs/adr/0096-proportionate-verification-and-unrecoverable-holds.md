# ADR-0096: Proportionate verification operations and release of unrecoverable inherited holds

- **Status:** Accepted
- **Date:** 2026-10-24
- **Affects:** all FS-GG repositories (coordination process); `.github` work-board, check-board and recovery skills
- **Decision owner:** repository owner (user ruling), recorded by Programme/root
- **Amends:** [ADR-0093](0093-original-103-authentication-domain-policy.md) and [ADR-0095](0095-utel-resource-containment-for-fresh-profiles.md) — the "all 439 inherited reservations survive" clauses. The reservations survive as citable history but no longer fence demonstrably isolated fresh operations. Original `.103` stays attempted-unknown and unreplayed, and `.104` stays under its own release gates.
- **Amended:** 2026-10-25 — finite delegated envelopes and demonstrated isolation; [user-decision disposition](https://github.com/FS-GG/.github/issues/4516#issuecomment-6397778554).
- **Ruling custody:** [FS-GG/.github#4730 comment 6385546431](https://github.com/FS-GG/.github/issues/4730#issuecomment-6385546431)

## Context

The original-operation discipline was designed for effects that are hard to reverse or observe:
publication, credentials, live stores, Home/VM/native-capability actions and uncertain outcomes.
Protected effects retain Root runtime admission, finite original clocks, exact final inputs,
substantive source review, birth-joined retirement and independent retirement acceptance. Section C
permits one reviewed finite sequence without repeating approval after each successful step.

In practice that discipline also governs ordinary local builds and tests of unpublished source:

- On 2026-10-24, [FS.GG.SDD#935](https://github.com/FS-GG/FS.GG.SDD/issues/935) passed through
  about twelve source-ready review rounds (`SDD05`–`Q12`). None of them produced a completed
  test-project build.
- [FS.GG.Templates#441](https://github.com/FS-GG/FS.GG.Templates/issues/441) had two admitted
  compiler operations fail inside the custody wrapper itself, before any compiler pass.

In both cases the ceremony consumed the effort that the verification was supposed to protect.

Separately, the [forward-recovery decision](../../.agents/skills/work-board/references/lifecycle-contract.md#recover-ownership-and-unavailable-history)
retains 439 inherited and 46 later original reservations as Unknown and unreleased. Their
evidence-search task is already closed as unavailable, so no event can ever satisfy their
release conditions. In practice they appear as a standing hold on nearly every Delivery.

## Decision

**A. Proportionate verification operations.** A local build, test or static check of unpublished
source is ordinary development verification when all of these hold:

- it runs in a freshly created, isolated output/cache root;
- it uses no credentials;
- it writes to no shared or remote destination;
- it performs no publication, deployment, live-store, Home, VM or native-capability effect.

Ordinary development verification needs none of the following: Root runtime admission, an
original-operation identity or clock, a frozen custody packet, or a pre-run peer review. The
executing owner:

- stays within the CPU/memory share the coordinator states;
- keeps first failures as ordinary logs;
- reports the results with the delivery.

Independent source review of the candidate, exact-head required CI and the ADR-0084 coherent
validation are unchanged. The full original-operation route remains mandatory for:

- publication;
- credentials;
- live-store or shared-destination writes;
- Home, VM and native-capability effects;
- destructive or uncertain effects;
- any operation whose own owning plan explicitly requires it as acceptance evidence.

**B. Unrecoverable inherited holds.** The 439 inherited reservations and 46 later originals are
declared unrecoverable. They are released for fresh operations whose generation, writable state,
identity and outputs are demonstrably isolated from them. They remain citable history.

B does not:

- replay or retry any original;
- reuse a consumed identity or clock;
- claim any original's outcome;
- release a named live hold that has its own current owner and scope. For example, retained
  residual files of a specific cancelled original, or a compiler logical hold, keep their
  owning-issue scope.

**C. Finite delegated execution envelopes.** Root may review one concrete finite envelope for a
protected sequence using the existing owning issue and maintained runner. It binds:

- exact maintained source/effect revisions, immutable input identities and the actual credential,
  engine, host and supported runtime context;
- ordered permitted actions, accountable executing owner, each step's independent original identity
  and finite clock, overall sequence horizon, success assertions and required independent readback;
- the complete transitive resource demand and CPU, memory, swap, tasks, storage and cleanup bounds,
  plus mutable/write domains, artifact destinations and retention through terminal observation.

The executing owner advances conditionally after verified success without another per-step Root
approval. Source review, exact-head required CI and independent readback remain required where
applicable; messages do not replace them. Start each original clock before its setup/helpers and
reserve its cleanup. An envelope does not combine, renew or extend original clocks. Stop at the first
failure, Unknown, determining-input drift or exhausted bound; retain that original outcome. There is
no automatic retry, renewed clock or replacement-ID replay. A continuation requires reconciliation
and explicit authority for that continuation, never an assumed success branch.

An earlier closed, verified output may supply a later input only through an exact derivation reviewed
in the envelope, with the resulting identity checked before use. Unknown future inputs cannot be
filled from expectation. End the envelope before any unreviewed input, action or write-domain boundary.
The envelope is a bounded application of existing admission, not a new service, CLI, approval ledger
or gate for ordinary isolated verification or authorized read-only diagnostics.

**Isolation precedes execution.** Prove independent engine storage/graphroot, runtime/runroot,
temporary state, outputs and actual process containment. New operation names or directories alone
are insufficient. Include image acquisition/import, backing storage, engine/database state, rootless
pause/helper processes and cleanup ownership in the interference analysis. Existing shared-store
objects, paused holds and Unknowns remain untouched. They block fresh work only where interference
is relevant; demonstrate the boundary rather than assuming either release or global blockage.

**Early real preflight and typed observations.** Before substantial implementation or launch,
exercise the actual canonical preflight with the available credential context, accepted native
repository policy, required workflow identities, trigger filters/current inventory and supported
runtime. Preserve real refusals; neither simulated readiness nor marker removal satisfies them.
Report process observations as running, paused, stopped or reported-but-unverifiable, with observed
boot/namespace/source/clock and authentic birth scope. Missing birth remains Unknown and cannot
serve as an authenticated witness. Keep the original terminal outcome separate from cleared current
manager metadata; neither absence nor metadata cleanup converts an original failure into success.

**Finish the selected consumer path.** Preserve runner → genuine bake → registration → first
consumer priority. Parallel source work removes a concrete dependency and retains the same owner
through applicable publication and adoption. Plan reusable supported glibc2.44 builder and selected
Ubuntu Noble desktop/Orca/IME facilities after the first completed shared-runner run. Bind exact
facility source/dependency tuples and qualify each actual runtime; use source-bound artifact handoff
where runtimes differ. Facility selection or source delivery proves no native qualification. Existing
owning plans and holds still govern; this amendment selects no native case and waives no acceptance.

## Consequences

- Owners can build and test candidates immediately. Review effort moves from operation
  ceremony to the source candidate and its results.
- A cheap local result does not settle installed, published or native acceptance. Those
  remain distinct claims, exactly as before.
- Owning issues stop carrying the 439/46 holds as a blocker. A specific named hold must be
  cited by its own owning record.
- The work-board lifecycle contract, recovery references and organization inspection contract
  state this boundary. The check-board copy of the inspection contract ships in the coordination
  kit, and its matching sentence follows at the next coherent-set release. Until then this ADR
  governs where the two copies differ.

## Alternatives considered

- **Keep the full discipline and add capacity for reviewers.** Rejected: the failures were in
  the ceremony itself, not in reviewer availability.
- **Per-operation exemptions decided by Root.** Rejected: this keeps an admission step, and
  the admission step was the bottleneck.
- **Delete the hold history.** Rejected: the history remains useful evidence, and the
  isolation requirement still guards against a genuinely shared destination.
