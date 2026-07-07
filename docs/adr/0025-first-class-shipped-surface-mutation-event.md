# ADR-0025: First-class shipped-surface mutation — the governed event + reconcile protocol

- **Status:** Proposed
- **Date:** 2026-07-07
- **Affects:** **FS.GG.SDD** (owner of `fsgg-sdd surface` — detect a changed committed `.fsi` baseline and **classify** the delta additive-vs-breaking), **FS.GG.SDD**/**publishing** (prompt the coherent-set version bump on the repo's version axis, e.g. `$(FsGgAudioVersion)`), **.github** (this ADR; the `registry/dependencies.yml` + compatibility-projection reconcile; and the **consumer-impact enumeration** that turns "who is affected" from prose into a checkable query — `scripts/fsgg-surface-impact`), **coordination** (flag the enumerated consumers onto the `Coordination` board; route boundary-crossing changes through ADR-0001)

## Context

A **contract-first** framework's whole thesis is that a public surface is a governed
artifact. Yet the FS.GG.Audio greenfield bootstrap ([#235](https://github.com/FS-GG/.github/issues/235),
epic) exposed that the one event the thesis exists to govern — a **mutation of an
already-shipped surface** — is not first-class in the tooling. Work item `004`
additively changed the already-published `FS.GG.Audio.Core` and `.Host` `.fsi` surfaces
and the tooling treated it as an ordinary edit ([#236](https://github.com/FS-GG/.github/issues/236),
Audio feedback §3.2/§5): no detection that a committed `.fsi` changed, no additive-vs-breaking
classification, no prompt to bump the coherent-set version, no `registry/dependencies.yml`
+ projection + ADR reconcile, no "who consumes this" flag. The only record that the change
was contract-relevant was prose the operator wrote by hand.

This bites only *after* a repo has a **published** surface to mutate — which is exactly why
a young platform of mostly pre-1.0 repos had not felt it until a seventh repo shipped fast.

Two prerequisites are already in place:

- The **baseline mechanism** — [#240](https://github.com/FS-GG/.github/issues/240) shipped
  `fsgg-sdd surface --check/--update`: a tool-owned committed `.fsi` baseline under
  `docs/api-surface/<pkg>/**` and a drift check. #236 can only *detect* a shipped-surface
  change because there is now a canonical baseline to diff against. This ADR builds the
  **classification + reconcile** layer on top.
- The **governance fabric** — ADR-0001 (cross-repo coordination via issues + registry),
  ADR-0015 (the registry schema is itself a governed contract), the typed
  `fsgg-sdd registry validate` gate, and `scripts/check-projection.py` (the
  registry→`compatibility.md` projection gate). The reconcile side of this event is
  *already enforced* once it is *recorded*; what was missing was the **event** that makes
  it get recorded, and a way to enumerate the **consumers** to notify.

## Decision

Treat a **change to a committed `.fsi` baseline of a shipped surface** as a **first-class
governed event** — the *shipped-surface mutation* — with a defined pipeline. The event
fires when `fsgg-sdd surface --check` observes that a committed baseline (not a
never-published surface) differs from the generated `.fsi`.

1. **Detect** *(FS.GG.SDD)* — a changed committed baseline under `docs/api-surface/<pkg>/**`
   is the trigger. (Mechanism: #240.) A surface with no committed baseline is a *new* surface,
   not a mutation, and is out of scope for this event.

2. **Classify** *(FS.GG.SDD)* — diff the baseline delta and label it:
   - **additive** — members only added; every prior signature still present → a **minor**
     coherent-set bump.
   - **breaking** — a member removed, renamed, or its signature changed → a **major**
     coherent-set bump.
   Classification is advisory-but-loud; the operator confirms. (ApiCompat already gates the
   *published-package* direction; this classifies the *committed-baseline* direction, at
   source, before publish.)

3. **Reconcile** — the classification drives three obligations, each owned by its layer:

   a. **Version** *(publishing)* — prompt a coherent-set version bump on the repo's version
      axis (`$(FsGgAudioVersion)`, `$(FsGgGameVersion)`, …): additive → minor, breaking →
      major. Publish-before-flip (FR-007) still holds — the package leads the registry pin.

   b. **Registry + projection + ADR** *(.github)* — in the same PR that mutates the surface,
      bump the contract's `version` (and any dependency-edge `via:` pins), prepend a
      `registry/CHANGELOG.md` entry, set the top-level `updated:`, regenerate the
      `docs/registry/compatibility.md` projection, and — for a **breaking** change, or a
      notable additive one — record an ADR. Enforced by the existing validator + projection
      gates; no new gate.

   c. **Consumer-impact flag** *(coordination)* — enumerate the contract's consumers from
      the registry (`contracts[].consumers` + `dependencies[]` edges) and flag them on the
      `Coordination` board. A **breaking** change files a consumer-impact issue per consumer,
      `Blocked by` the producer release (ADR-0001 boundary-crossing protocol); an **additive**
      change records the impact set in the registry entry so consumers can opt in when ready.

The `.github` repo makes step 3c mechanical rather than prose: **`scripts/fsgg-surface-impact
<contract-id>`** reads `registry/dependencies.yml` and prints the exact consumer set (and the
edges carrying the contract) that a mutation must flag — the checkable analog of the
hand-written "who consumes this" note. It is read-only and takes no board action itself
(board writes remain the operator's claim/file step), so it is safe to run in a detection hook.

## Consequences

- **FS.GG.SDD** gains `surface` **classification** (additive/breaking) on top of the #240
  drift check, and emits the classification so the reconcile can be prompted. Tracked as the
  detection/classification child of #236.
- **publishing** gains the version-bump prompt keyed off the classification (additive→minor,
  breaking→major) into the repo's version axis. Tracked as the version-bump child.
- **.github** carries this ADR, the reconcile checklist
  (`docs/coordination/shipped-surface-mutation.md`), and `scripts/fsgg-surface-impact` (with a
  fixture under `tests/`). The registry/projection reconcile rides the **existing** gates —
  this ADR adds no gate; it adds the *event* that makes the existing gates fire on surface
  mutations.
- **coordination** flags the enumerated consumers; boundary-crossing (breaking) changes route
  through ADR-0001 as consumer-impact issues sequenced `Blocked by` the producer release.
- **Ordering.** Detect+classify (sdd) is upstream of the version prompt (publishing) and the
  registry/consumer reconcile (.github/coordination). The four are sequenced as #236's child
  issues; the `.github` slice (this ADR + protocol doc + `fsgg-surface-impact`) is the
  unblocking first step and is buildable now, independent of any specific contract landing.
- **No architecture-map change.** This event adds no repo, boundary, or coherent-set axis; it
  formalizes governance of an *existing* surface class, so `docs/architecture.md` is untouched
  (map opt-out per the registry-change checklist).

<!-- No registry surface changes in this ADR itself (it decides a protocol, not a contract
version), so registry/dependencies.yml is untouched here; the per-mutation registry reconcile
this ADR *governs* is what updates it, per change. -->
