# ADR-0096: Proportionate verification operations and release of unrecoverable inherited holds

- **Status:** Accepted
- **Date:** 2026-10-24
- **Affects:** all FS-GG repositories (coordination process); `.github` work-board, check-board and recovery skills
- **Decision owner:** repository owner (user ruling), recorded by Programme/root
- **Amends:** [ADR-0093](0093-original-103-authentication-domain-policy.md) and [ADR-0095](0095-utel-resource-containment-for-fresh-profiles.md) — the "all 439 inherited reservations survive" clauses. The reservations survive as citable history but no longer fence demonstrably isolated fresh operations. Original `.103` stays attempted-unknown and unreplayed, and `.104` stays under its own release gates.
- **Ruling custody:** [FS-GG/.github#4730 comment 6385546431](https://github.com/FS-GG/.github/issues/4730#issuecomment-6385546431)

## Context

The original-operation discipline was designed for effects that are hard to reverse or observe:
publication, credentials, live stores, Home/VM/native-capability actions and uncertain outcomes.
Every effect admitted under it needs Root runtime admission, a finite original clock, frozen
final-input packets, a peer "source-ready" review, birth-joined retirement and an independent
retirement acceptance.

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
