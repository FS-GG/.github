# ADR-0061: Collapse the publish-before-flip schema/validator split

- **Status:** Proposed
- **Date:** 2026-07-20
- **Affects:** FS-GG/.github (owns `registry/dependencies.yml` and its schema-of-record comment block, the `contract-coherence` pin, `registry-schema`); FS.GG.SDD (owns `Fsgg.Registry` — the typed validator in FS.GG.Contracts — and publishes the `FS.GG.SDD.Cli` that carries it). The decision is about *which repo the schema-of-record lives in*, so it is inherently cross-repo.
- **Interacts with:** [ADR-0058](0058-adopt-one-governing-principle-derive-dont-restate.md) (the governing principle — the split is a fact stored in two places); [ADR-0015](0015-register-the-registry-schema-as-a-governed-contract.md) §3 (the "one PR teaches and bumps" step that *cannot exist* because no PR spans two repos); [ADR-0037](0037-schema-growth-is-publish-before-flip.md) (the two-ordered-PR rail this split forces every schema change onto); `.github#689` (the incident that named ADR-0015 §3 impossible); `.github#686` (the "additive is still growth" tax option (b) would remove).
- **Decision-first ADR:** records two viable approaches and a recommendation. The **implementation** — item [.github#1261](https://github.com/FS-GG/.github/issues/1261) — is left open and unblocked; it proceeds once an option here is accepted. This ADR builds nothing.
- **Source:** [docs/reports/2026-07-20-cross-repo-coordination-overhead-root-cause.md](../reports/2026-07-20-cross-repo-coordination-overhead-root-cause.md) §3B, §7 P2.

## Context

The registry's schema-of-record lives in `.github` (a comment block at the head of
`dependencies.yml`); its typed validator, `Fsgg.Registry`, lives in **FS.GG.Contracts** and ships from
**FS.GG.SDD** as a versioned CLI. Because the document and its checker are in different repos, and
**no PR spans two repos** (`.github#689`), every schema change is forced onto the publish-before-flip
rail (ADR-0037): teach + publish a CLI in SDD (step 1, "known-not-enforced"), then bump + pin in
`.github` (step 2). ADR-0015 §3 originally asked for the teach-and-bump in *one* PR — a PR that cannot
exist — which is why ADR-0037 recast it as two ordered PRs.

The measured cost: adding **one enum value** (`driver`, then `operator`) costs *an ADR + two ordered
PRs across two repos + a CLI release + a pin advance + four regenerated artifacts.* The phrase
"known-not-enforced / step 1 / step 2 / publish-before-flip" appeared in **12 commit subjects in five
days**. It is a load-bearing ceremony, and the report is precise about its sole cause: the document and
its checker were placed in different repos with no spanning change. This is the same *derive/restate*
defect (ADR-0058) in a structural key — the schema is authored in one repo and re-encoded as a pinned
validator version in another.

## Decision

**Recommended — Option (b): make the validator schema-version-agnostic.**

Change `Fsgg.Registry` to validate **structure**, not a pinned enum: an unknown scope value (or wire
provenance, or any enumerated token) is accepted as an opaque, well-formed string rather than checked
against a compiled-in list. Adding an enum value then **is not "schema growth" at all** — no CLI
republish, no `schemaVersion` bump, no pin advance, no step-1/step-2 dance. A one-value addition
becomes a one-line PR in `.github`.

This is the deeper fix because it also removes the **"additive is still growth" tax** (`.github#686`)
for the common case: today an additive change (a new optional field, a new enum value) still trips the
publish-before-flip rail merely because the validator's enum is exhaustive. A structural validator has
nothing to grow, so additive changes stop being events.

The fail-closed discipline is **retained where it earns its keep**: the validator still rejects
*malformed* structure (a mistyped field name, a value of the wrong shape, a missing required key), and
semantic gates (ownership edges, coherence intent) still fail closed. What it stops doing is treating
"a token I have not been recompiled to know" as an error — which is the behaviour that couples the
document to the validator's version and manufactures the rail.

## Consequences

- **Adding an enum value is one PR in `.github`.** The step-1/step-2 ceremony, the CLI release, and the
  pin advance disappear for the enum-growth case (the common one). ADR-0037's rail remains for genuine
  *structural* schema changes (a new required field, a retyped field), which are rare.
- **`.github#686`'s additive tax is removed** for enum/optional-field growth: with nothing to grow, an
  additive change is not schema growth.
- **The validator's contract narrows deliberately.** It guarantees *well-formedness*, not
  *enum-membership*. The trade-off (see Alternatives) is that a genuinely bogus scope value —
  `scope: drivver` — is no longer caught by the validator; it must be caught by the semantic layer that
  actually consumes the token (the scheduler, the materializer), which is where its meaning lives
  anyway (ADR-0058: gate the capability, not the declaration).
- **FS.GG.SDD still owns `Fsgg.Registry`.** This option does not move the validator; it changes what it
  asserts. The `FS.GG.SDD.Cli` publish path is unchanged; it simply stops needing a republish for enum
  growth.
- **This ADR changes nothing until #1261 is worked.** It records the target; the migration is the item.

## Alternatives considered

- **Option (a) — move the schema-of-record into `.github` as a data file the CLI reads.** The validator
  stays typed and exhaustive, but the enum list becomes a **data file in `.github`** that
  `Fsgg.Registry` loads at runtime rather than a compiled-in constant. A schema change is then one PR in
  `.github` (edit the data file), and the CLI need not republish because it reads the list rather than
  embedding it. This also collapses the split and is a smaller change to the validator's *philosophy*
  (it stays enum-checking). **Why (b) is preferred:** (a) keeps a projection — the enum list is still a
  restated fact, now shipped as data, and the CLI must fetch/pin *that* file, re-introducing a version
  coupling one level down (which data-file version does this CLI honour?). (b) removes the projection
  entirely. (a) is a reasonable, lower-risk fallback if (b)'s narrowed validator contract proves to let
  too much through in practice.
- **Status quo — keep the split, keep the rail.** The measured 12-commits-in-5-days ceremony is the
  reason this item exists. Rejected.
- **Move `Fsgg.Registry` itself into `.github`.** Puts document and checker in one repo, killing the
  rail — but `.github` has *no F# build* and deliberately no YAML reader in the shipped shim (ADR-0042);
  hosting the typed validator there would drag a build and a runtime into a repo that is a distribution
  point, not a component. The split exists for a real reason (SDD owns the typed core); the fix is to
  stop *coupling on version*, not to relocate the code. Rejected.
- **Fold this into ADR-0060 (P1).** Both are *derive, don't restate*, but P1 is about generated *field
  values* and this is about the *schema/validator coupling* — different mechanisms, different repos,
  different risk. Kept as separate items so each is argued and accepted on its own.
