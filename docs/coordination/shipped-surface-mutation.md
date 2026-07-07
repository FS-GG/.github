# The shipped-surface-mutation event

A **contract-first** platform's central event is a change to an *already-shipped* public
surface. [ADR-0025](../adr/0025-first-class-shipped-surface-mutation-event.md) makes that a
**first-class governed event** instead of an ordinary edit. This doc is the operator-facing
reconcile protocol — the checklist the event obliges, and the tool that makes its
consumer-impact step mechanical.

Origin: the FS.GG.Audio bootstrap ([#235](https://github.com/FS-GG/.github/issues/235) /
[#236](https://github.com/FS-GG/.github/issues/236), feedback §3.2/§5) — work item `004`
additively changed the already-published `FS.GG.Audio.Core`/`.Host` `.fsi` surfaces and
nothing detected it, classified it, prompted a version bump, or flagged consumers.

## The trigger

A committed `.fsi` **baseline** under `docs/api-surface/<pkg>/**` differs from the freshly
generated `.fsi` — observed by `fsgg-sdd surface --check` (the baseline mechanism from
[#240](https://github.com/FS-GG/.github/issues/240)). A surface with **no** committed
baseline is a *new* surface, not a mutation, and does not fire this event.

## The pipeline

| Step | Owner | What |
|---|---|---|
| **Detect** | FS.GG.SDD | a changed committed baseline is the trigger (`surface --check`) |
| **Classify** | FS.GG.SDD | additive (members only added — every prior signature intact) → **minor**; breaking (member removed/renamed/re-signed) → **major** |
| **Reconcile — version** | publishing | prompt the coherent-set bump on the repo's version axis (`$(FsGgAudioVersion)`, …); publish-before-flip (FR-007) still holds |
| **Reconcile — registry/projection/ADR** | **.github** | the checklist below; enforced by the **existing** validator + projection gates |
| **Reconcile — consumer flag** | coordination | enumerate consumers (`fsgg-surface-impact`) and flag them on the board |

Detect+classify is upstream of the version prompt, which is upstream of the registry +
consumer reconcile.

## The `.github` reconcile checklist

When a shipped surface mutates, the same PR that changes it (or the follow-on registry PR)
does — this is the existing `contract-change` protocol, now *triggered by the event*:

1. **Version** — bump the contract's `version` in
   [`registry/dependencies.yml`](../../registry/dependencies.yml): additive → minor, breaking
   → major. Bump dependency-edge `via:` pins that carry the contract.
2. **Changelog + date** — prepend one dated entry to
   [`registry/CHANGELOG.md`](../../registry/CHANGELOG.md) (`- **YYYY-MM-DD** — HEADER (owner;
   refs): additive|breaking surface change, contract id, from→to version`) and set the
   top-level `updated:` to match.
3. **Projection** — regenerate the [`docs/registry/compatibility.md`](../registry/compatibility.md)
   row(s); `scripts/check-projection.py` reds the PR on drift.
4. **Validate** — `fsgg-sdd registry validate` → valid / 0 diagnostics.
5. **ADR** — for a **breaking** change (or a notable additive one), record an
   [ADR](../adr/README.md) and reconcile the [architecture map](README.md#system-overview--the-architecture-map)
   if the change alters its contract picture; otherwise opt out (`architecture-map: unaffected`).
6. **Consumer flag** — step below.

This adds **no new gate** — the reconcile rides the validator + projection + architecture-map
gates already in CI. ADR-0025 adds the *event* that makes them fire on surface mutations.

## Consumer-impact flagging — `scripts/fsgg-surface-impact`

"Who consumes this?" was prose the operator wrote by hand. Make it a query:

```sh
scripts/fsgg-surface-impact <contract-id>            # human table of consumers + carrying edges
scripts/fsgg-surface-impact <contract-id> --json     # machine list (for a detection hook)
scripts/fsgg-surface-impact --list                   # all contract ids in the registry
```

It reads [`registry/dependencies.yml`](../../registry/dependencies.yml) and prints the exact
set a mutation must flag: the contract's declared `consumers`, plus every `dependencies[]`
edge whose `via:` names the contract. It is **read-only** — it takes no board action (so it
is safe in a detection hook); acting on the result is the operator's step:

- **breaking** — file a consumer-impact issue **in each consumer repo** (the `cross-repo`
  mailbox, [README](README.md)), `Blocked by` the producer release (ADR-0001 boundary-crossing).
- **additive** — record the impact set in the registry entry / changelog so each consumer can
  opt in at its own pace; no forced blocking issue.

## See also

- [ADR-0025](../adr/0025-first-class-shipped-surface-mutation-event.md) — the decision.
- [contract-coherence gate](contract-coherence-gate.md) — enforces the reconciled registry.
- [auto-update fabric](auto-update-fabric.md) — keeps consumer pins fresh once the producer ships.
