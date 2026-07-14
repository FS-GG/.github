# ADR-0037: Schema growth is publish-before-flip — two ordered PRs, and the validator gates on the declared version

- **Status:** Accepted
- **Date:** 2026-07-14
- **Affects:** `.github` (the two schema documents + the contract-coherence gate pin), SDD (`Fsgg.Registry`, the typed validator). Amends [ADR-0015](0015-register-the-registry-schema-as-a-governed-contract.md) §3.
- **Fixes:** [FS-GG/.github#689](https://github.com/FS-GG/.github/issues/689)

## Context

[ADR-0015](0015-register-the-registry-schema-as-a-governed-contract.md) registered the registry's own
schema as a governed contract — the right call, and the one contract in the system that previously was
not. Its §3 then specified the procedure. Teaching `Fsgg.Registry` a new field or a tightened rule
obliges, **in the same change**:

> - bump `registry-schema.version` **and** the file's top-level `schemaVersion`;
> - advance the `contract-coherence.yml` `FS.GG.SDD.Cli` pin to a CLI carrying that `Fsgg.Registry`
>   version;
> - keep the field-vocabulary comment (the human schema-of-record) current.

**That change cannot exist.** For *both* contracts ADR-0015 governs, the schema **document** and its
**validator** live in different repos:

| contract | schema document | validator |
|---|---|---|
| `registry-schema` | `.github` — `registry/dependencies.yml` (`schemaVersion`) | **FS.GG.SDD** — `Fsgg.Registry` (`FS.GG.Contracts`) |
| `skill-registry` | `.github` — `registry/skills.yml` (`schemaVersion`) | **FS.GG.SDD** — the same |

No PR spans two repos. And the second bullet is unsatisfiable even on its own terms: the pin cannot
advance to a CLI that **does not yet exist**. The CLI carrying the new `Fsgg.Registry` has to be
*published* before `.github` can pin it.

**Why nobody has tripped over it.** ADR-0015 was governance, not a behavioural change: it explicitly
held `schemaVersion` at `1`, and no schema has been bumped since. The procedure has therefore **never
been executed**. `mirrored:` ([#658](https://github.com/FS-GG/.github/issues/658), PR
[#687](https://github.com/FS-GG/.github/pull/687)) is the **first real schema growth** since ADR-0015
— and it immediately exposed that the procedure is unrunnable, which is why it landed *unbumped* and
needed [#686](https://github.com/FS-GG/.github/issues/686) to adjudicate.

### The window is not optional — this org's own machinery forces it

The tempting reading is that "same PR" is merely *tidy*, and two PRs landed close together are near
enough. They are not, and the reason is a device ADR-0015 itself installed.

ADR-0015 §3 leans on the Renovate annotation manager to keep the validator current *structurally*
between deliberate bumps, "so the H2 freeze cannot silently recur". That coupling is now also
**asserted**: `pin-coherence.yml` gates, daily and on every push to `main`, that every annotated pin
**equals the newest version live on the org feed**. Its own header states the consequence in as many
words:

> `push to main + schedule` — a CLI release that publishes a newer version turns `.github@main` red.

So the moment SDD publishes the CLI carrying the new validator, `.github@main` goes **red** until the
pin advances — and the pin advances **on its own**, by bot, with no human electing to. The
newly-published validator therefore **will** be pointed at the not-yet-bumped document, on `main`, on
a required gate, *guaranteed*. The gap between the two PRs is not a window we tolerate; it is a window
the org drives us through.

That is the fact ADR-0015 §3 could not see, because it assumed the pin advanced **as part of** the
bump. It does not. It advances **ahead** of the bump, and reds `main` until it is allowed to.

## Decision

**Schema growth is publish-before-flip (FR-007) — the same pattern the org already uses for every
package the registry claims: publish the artifact, then flip the row that claims it.**

1. **Two ordered PRs, never one.**

   1. **FS.GG.SDD** teaches `Fsgg.Registry` the new field or rule and **publishes** an
      `FS.GG.SDD.Cli` carrying it.
   2. **`.github`** *then* bumps the document's top-level `schemaVersion` **and** the contract
      `version`, advances the `contract-coherence.yml` `FS.GG.SDD.Cli` pin to that published CLI, and
      keeps the field-vocabulary comment current — one `.github` PR, after the CLI is live.

2. **The invariant is "the pin advances with the schema", not "one commit does both".** ADR-0015's
   actual intent — *the schema and its validator must not drift apart* — is preserved exactly. What is
   dropped is an **atomicity requirement the repo topology makes impossible**, and which was never the
   thing being protected.

3. **A new field or a tightened rule is enforced only when the document DECLARES the new
   `schemaVersion`.** This is an obligation on the **validator**, and it is what makes step 1 safe to
   land alone. Between the two PRs the pinned validator knows a schema the document has not yet
   declared; by §Context that state is *forced*, so a validator that enforces its new rule
   unconditionally would red `.github`'s own required contract-coherence gate on `main` the instant it
   published — and the only PR that could green it is step 2, now racing a red `main`. Version-gate the
   rule. Do not merely hope the two PRs land close together; the bot will not let them.

4. **An OPTIONAL, ADDITIVE, back-compatible field IS schema growth.** Decided in
   [#686](https://github.com/FS-GG/.github/issues/686), folded in here so the canonical source carries
   it. The tempting answer is *no* — the field is optional, readers ignore unknown keys, no consumer
   breaks — and ADR-0015 refutes it in its own Context: the motivating defect (**M5**) *was* additive
   growth, tolerated additively. "No consumer breaks" is not an **exemption** from schema growth; it is
   the **mechanism** of the defect — precisely why the drift is silent, and therefore precisely why it
   must be tracked.

   The test is **not** "does a reader break". The test is: **does the on-disk schema now carry a field
   or rule the pinned validator does not know?**

5. **Scope.** This amends ADR-0015 **§3 (the procedure) only**. Its §1 (`registry-schema` as a
   governed contract, SDD-owned typed authority), §2 (`.github` as a first-class registry node and the
   `github → sdd` edge), the `validator` field, and the schema↔validator coupling all **stand**.

## Consequences

- **ADR-0015 §3's "same change" clause is superseded**; the rest of ADR-0015 stands. The `0015` row in
  [`README.md`](README.md) is annotated, per this repo's convention of amending an Accepted ADR with a
  new one rather than rewriting it in place (cf. [0027](0027-worker-keyed-claim-lock-and-worker-channel.md)
  → 0021, [0032](0032-the-lock-hash-must-not-depend-on-the-machine.md) → 0031,
  [0014](0014-skill-vendoring-one-manifest-one-materialize-verify.md) → 0011).

- **The ordering is derived, not chosen — it is the direction that fails safe.** The two states are not
  symmetric:

  - A validator that knows **more** than the document declares is **safe**: by §3 it is asked to enforce
    nothing new, and the document it validates is one it already accepted.
  - A document that declares **more** than the pinned validator knows is the **H2/M5 failure itself** —
    a schema the gate cannot check, which is the entire defect ADR-0015 exists to close.

  Flip-then-publish would deliberately enter the second state, and could not be made green: no pin value
  satisfies a document whose schema no published CLI knows. Publish-before-flip is the only order whose
  intermediate state is *green by construction*.

- **It is also the only order whose failure modes are recoverable.** Reverting step 2 alone leaves a
  published CLI that knows a field the document does not declare — safe, by §3. Reverting step 1 alone
  would mean **unpublishing a package**, which this org has already named as a failure it does not
  commit ([ADR-0031](0031-republished-package-is-a-named-failure.md),
  [ADR-0032](0032-the-lock-hash-must-not-depend-on-the-machine.md)): a published version is immutable.

- **Both governed contracts follow this**, and the registry rows for `registry-schema` and
  `skill-registry` (plus the `docs/registry/compatibility.md` projection) already record the corrected
  procedure — [#686](https://github.com/FS-GG/.github/issues/686) got there first. This ADR is the
  **canonical source** catching up: a reader who goes to the ADR rather than the registry row was, until
  now, still told to do something they cannot.

- **The `skill-registry` bump stays owed and unpaid, deliberately.** `mirrored:`
  ([#658](https://github.com/FS-GG/.github/issues/658)) grew that schema and landed unbumped; under §4
  that was growth, so `skill-registry.version` 1→2 + `skills.yml` `schemaVersion` 1→2 is **owed**. It is
  not paid yet because `Fsgg.Registry` does not assert over `skills.yml` **at all** — bumping now would
  version a schema **no validator reads** and advance the pin to a CLI carrying nothing, which is the
  ornament shape of [#416](https://github.com/FS-GG/.github/issues/416) inside the contract registry
  itself. Step 1 is [FS.GG.SDD#420](https://github.com/FS-GG/FS.GG.SDD/issues/420). **An unpaid debt
  recorded is honest; a paid-looking one is not.**

- **Nothing enforces §3 today, and we say so rather than claim a guarantee we do not have.** A validator
  that enforces a new rule without version-gating it is caught only by the red gate it causes — loudly,
  on `main`, but *after* the fact. The clean tightening would be for the CLI to report the highest
  `schemaVersion` it knows, letting `contract-coherence.yml` assert `declared ≤ known` directly; that
  needs an SDD-side surface (`Fsgg.Registry`), so it is a **cross-repo decision, not a fix available
  here**, and it is left open deliberately rather than half-built.

- **The tightening path ADR-0015 kept open is unchanged in kind, and now runnable.** Making a
  today-optional field required, or rejecting unknown fields, still lands as a normal `contract-change`
  (bump `schemaVersion`, advance the pin) — it simply lands as **two ordered PRs**, with the tightened
  rule gated on the declared version per §3, rather than as one PR that could never have existed.

<!-- This changes a procedure, not the shape of the system: no new repo, boundary, coherent-set axis,
contract, or dependency edge, and the §5 contract picture is untouched. The `architecture-map:
unaffected` opt-out applies. -->

## Alternatives considered

- **Rewrite ADR-0015 §3 in place.** Rejected on this repo's standing convention: an Accepted ADR is
  amended by a **new** ADR, never edited into retroactive correctness. The record that we decided
  something, and that it turned out to be unrunnable, *is* the value — it is why the next reader
  believes the rest of ADR-0015.

- **Move the schema documents into FS.GG.SDD**, co-located with the validator, so one PR *can* do both.
  This would make §3 literally satisfiable, and it is the only alternative that would. Rejected:
  [ADR-0001](0001-cross-repo-coordination-via-issues.md) makes `.github` the cross-repo registry, and
  `.github`'s own gates are the registry's consumer — moving the document to satisfy a procedure inverts
  the ownership the procedure exists to serve. It would not even remove the split: the gate pin still
  lives in `.github`, so the change would still be two PRs, in the same order, for the same reason.

- **Vendor `Fsgg.Registry` into `.github`** so the validator is local. Rejected: ADR-0015 §1 makes SDD
  the **typed authority**, and a second copy of a validator is precisely the drift class ADR-0015 was
  written to close.

- **Flip-then-publish** (bump the document first, publish the validator after). Rejected as the unsafe
  direction — see Consequences. It knowingly parks the org in the H2/M5 state, and no pin value can green
  it.

- **Keep "same PR" and satisfy it with a monorepo or a submodule.** Out of scope, and rejected by the
  whole shape of ADR-0001.
