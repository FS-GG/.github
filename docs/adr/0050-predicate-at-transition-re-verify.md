# ADR-0050: Re-verify a registry predicate against its owning manifest at the transition — filing-time and flip-time

- **Status:** Accepted (2026-07-19)
- **Date:** 2026-07-19
- **Affects:** `.github` (the coordination engine: a new authority-side registry-predicate reader, `lint`/`Client.fs`, `Chore.fs`; the cross-repo-request issue form), and every repo that files a `cross-repo:request` or whose registry rows are owned by another repo — sdd, rendering, governance, templates, game, audio
- **Decides:** [#1199](https://github.com/FS-GG/.github/issues/1199). The two call-sites are filed as child issues (this record does **not** wire them).

## Context

Two board events route through a human every time, and the [#923](https://github.com/FS-GG/FS.GG.Rendering/issues/923)↔[#1194](https://github.com/FS-GG/.github/issues/1194) deadlock is the same defect surfacing at both:

**A. Filing-time.** A `cross-repo:request`'s asked-for **value** is never checked against the owning producer before triage. #1194 asked `.github` to set `fs-gg-playtest` `mirrored: true`. That field is owned by FS.GG.Game's producer manifest, which declares `false`; the row already existed on `main` (reconciled verbatim in [#299](https://github.com/FS-GG/.github/issues/299), commit `48702e1`). The request was **refutable from data at filing time** — it was the [FS.GG.Rendering#505](https://github.com/FS-GG/FS.GG.Rendering/issues/505) / [#658](https://github.com/FS-GG/.github/issues/658) trap, the one `mirrored:` exists to refuse — yet nothing caught it until hand-triage. This is the recurring re-digest class ([#1186](https://github.com/FS-GG/.github/issues/1186), [#740](https://github.com/FS-GG/.github/issues/740), [#734](https://github.com/FS-GG/.github/issues/734)): a consumer reads a projection, infers a `.github` change, and files against the wrong layer.

**B. Flip-time.** `Blocked→Ready` clears when the recorded blocker **closes**, not when the item's own acceptance predicate re-verifies. The flip is the `BlockerCleared` chore (`Chore.fs:201-207`): every `Blocked by` ref `isResolved` (CLOSED **or** MERGED, #476), recomputed from a fresh scan. #923's recorded blocker is a **proxy** — "WI-2 (Game publishes the skill)". WI-2 closing flips #923 to Ready even though the semantic dependency (the row exists **and** agrees with the owner) is not satisfied.

**Both gaps reduce to one check** the refutation of #1194 already performed by hand:

> for a registry `id`/`field`/`value`: does the row exist, and does the owning producer's manifest agree?

Three facts about where that check can live shaped the decision:

1. **The logic already exists — but not where the issue's framing put it.** `fsgg-coord lint` is board-health only (touch-set, human-block, epic roll-up, blocker cycles); it has no registry or manifest awareness. The predicate — `owner:` picks whose word is law, and **an absent value is UNKNOWN, never False** — lives in the Python `scripts/fsgg-skill-registry-check` (`mirror_of`, `twin_of`, `mirrors()`, `predicates()`) and its auto-reconcile fabric.
2. **A general item's prose predicate is not mechanically evaluable.** `EpicBody.fsi` records this directly: re-verifying an item's predicates at a transition "is unimplementable as stated: nothing can mechanically decide 'the tripwire is intact' from prose." The org's remedy elsewhere was to force acceptance to be a **delegated child ref**, not to parse prose. So gap B is tractable **only** for the machine-checkable subclass — a declared registry predicate.
3. **The request carries no machine-readable assertion.** The `cross-repo-request.yml` issue form is free-text prose. #1194's `mirrored: true` sat in a fenced YAML block — and a parser cannot tell a fenced **quotation** from an **assertion** ([#683](https://github.com/FS-GG/.github/issues/683)).

## Decision

**Adopt one mechanism — *re-verify a registry predicate against its owning manifest at the state transition* — and apply it at both call-sites through a single predicate oracle.**

**1. One oracle.** A predicate check `P(id, field, value)` returning one of three verdicts:

| verdict | meaning |
|---|---|
| **Agrees** | the row exists and the owning manifest declares `field = value` |
| **Contradicts**(owner-value, note) | the row/manifest declares a different value; carries the owner-declared value and the governing note |
| **Unknown** | the row, the manifest, or the owner checkout could not be read |

`P` reads `registry/skills.yml` and the **owning producer's manifest** (`owner:` decides whose declaration is authoritative), reusing the reconcile rules `fsgg-skill-registry-check` already encodes — chief among them **absent ⇒ Unknown, never False**, so a hand-forged value the owner never declared is a finding rather than a match.

**2. The oracle is a reader in the `.github` coordination engine** (not a shell-out to the Python tool). The flip check (call-site B) lives inside the same typed chore derivation as `BlockerCleared`; keeping `P` in-engine means that gate is one predicate in the same pass rather than a process boundary the chore path cannot type over. This obliges the engine to gain a registry/manifest reader it did not have — see *Relationship to ADR-0042*, which is why this is compatible with 0042 rather than a reversal of it.

**3. Call-site A — filing-time (lint-adjacent).** The `cross-repo-request` issue form gains a **structured** assertion field (`id` / `field` / `value`), so the claim is machine-readable by construction — ADR-0044's rule ("derive from the structured source, don't parse prose") applied to the request. A lint-adjacent check runs `P` on that field; on **Contradicts** it auto-comments the owner-declared value and the governing note **before** triage. The #1194 class is then refuted from data, not from a human's memory of `mirrored:` semantics.

**4. Call-site B — flip-time (the `BlockerCleared` chore).** An item that **declares** a machine-checkable acceptance predicate does not leave `Blocked` on blockers-cleared alone: `P` must also return **Agrees**.

- **Contradicts** ⇒ hold the item `Blocked`, report it. A proxy blocker closing can no longer fake readiness.
- **Unknown** ⇒ **fail closed** — hold and report — exactly as one `BlockerUnknown`/`BlockerUnparseable` already keeps `BlockerCleared` from firing ([#266](https://github.com/FS-GG/.github/issues/266), #421). "Could not evaluate the predicate" is not "the predicate holds."

**5. The subclass is the boundary.** Only items carrying a **declared** registry predicate are gated. An item with no such predicate flips on blockers-cleared exactly as today — fact 2 forbids inventing a predicate for it. This is `/check-board`'s existing re-verify (and #1102's ON-BOARD-NO-STATUS) extended one step: from "do the blockers still hold?" to "does the item's own declared predicate still hold?".

### Relationship to ADR-0042 — this is not an amendment

[ADR-0042](0042-the-chore-lock-ref-is-embedded-beside-the-roster.md) decided the engine "has no YAML reader, and must not." Its Alternative B rejected a `repos.yml` reader — and the whole force of that rejection is one clause: `repos.yml` is **absent exactly where a receiver runs**, so a reader would resolve in `.github` and make `offer` refuse in all six receivers forever. The prohibition is about **configuration a receiver needs**, read from a file the shipped `kind: client` shim ships without.

The registry predicate is the mirror image. It is evaluated **where the registry lives** — the `.github` authority context and CI, where `registry/skills.yml` and producer checkouts are present — never in a receiver's scaffold path. And it obeys 0042's own fail-closed shape: when `registry/` is absent (a receiver, or any context without it), `P` returns **Unknown**, which per decision 4 fails closed — precisely as `choreLockRef` returns `None` and `offer` refuses. The shipped shim never reaches a code path that *requires* the registry; it reaches one that *safely reports it could not look*.

So the reader lives **inside** ADR-0042's carve-out (authority-repo capability, fail-closed when its source is absent), and 0042's decision — the chore-lock ref is embedded, not read from YAML — is untouched. No marker is added to 0042.

### Relationship to ADR-0044

This record **is** [ADR-0044](0044-generated-artifacts-are-derived-from-their-generators.md)'s principle at a new seam: verify against the generator/owner, never against the cached projection. #1194 asked `.github` (the projection) to assert a fact only FS.GG.Game (the owner) may declare; `P` derives the answer from the owner. The structured request field is 0044's "a fact belongs in the structured source the machine already reads, not in prose a parser must guess at."

## Consequences

- **The #1194 class is refuted before a human triages it.** A cross-repo request that contradicts its owner's manifest gets an auto-comment with the owner-declared value; the re-digest class (#1186/#740/#734) stops reaching hand-triage as a fresh surprise each time.
- **A proxy blocker closing can no longer fake readiness for a registry item.** Flip-time joins blocker-close and predicate-agree; either failing holds the item.
- **Fail-closed on both sides.** Unknown never advances an item and never asserts a refutation it could not prove — the #266 family, honored at both call-sites.
- **The engine gains an authority-side registry/manifest reader.** This is the cost. It is bounded: authority-scoped, fail-closed when its source is absent, and it reuses rules the Python reconcile already proved, rather than inventing a second predicate. The receiver invariant ADR-0042 protects is preserved by construction.
- **General (prose-predicate) items are unchanged.** Gap B gates only the declared machine-checkable subclass; everything else keeps today's blockers-cleared flip.
- **Two child issues implement the two call-sites** — one for filing-time (the structured field + the lint-adjacent check), one for flip-time (the `BlockerCleared` predicate gate). Sequencing lives on the Coordination board (ADR-0001), not here.

## Alternatives considered

**1. Reuse the Python `fsgg-skill-registry-check` as the oracle (shell out from both flows).** The predicate logic already lives there, complete and tested, and reuse would add no second copy — the stronger pull of the two. Rejected in favor of the in-engine reader (decision 2): the flip check (call-site B) belongs in the typed `Chore` derivation beside `BlockerCleared`, and a shell-out crosses a process boundary the chore path cannot type over, spends a subprocess per item, and splits the predicate across two languages. The honest trade is duplication-avoidance (reuse) against a single typed derivation (in-engine); the flip-time site decided it, and the in-engine reader is held to `fsgg-skill-registry-check`'s rules so the two cannot silently diverge.

**2. A new rule *inside* `fsgg-coord lint` reading the registry, with no structured field — parse the request body.** Rejected: `lint` is board-health only today, and body-parsing walks into #683 (a fenced YAML quotation reads as an assertion). The structured field makes the claim unambiguous; the check is then lint-adjacent, not lint-guessing.

**3. Flip-anyway / advisory on Unknown at call-site B.** Treat the predicate re-check as best-effort and honor the blockers-cleared flip when `P` cannot evaluate. Rejected: it re-opens the exact fail-open gap #1199 exists to close, and contradicts the fail-closed treatment `BlockerCleared` already gives an unresolvable blocker.

**4. Do nothing — keep hand-triage.** Rejected on #1194's measured cost and the recurrence of its class (#1186/#740/#734). The refutation is mechanical and data-sourced; leaving it to a human's recall of `mirrored:` semantics is the thing that failed.
