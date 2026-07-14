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
| `registry-schema` | `.github` — `registry/dependencies.yml` (`schemaVersion`) | **FS.GG.SDD** — `Fsgg.Registry` (`FS.GG.Contracts`), asserting today |
| `skill-registry` | `.github` — `registry/skills.yml` (`schemaVersion`) | **FS.GG.SDD** — the same `Fsgg.Registry`, which **does not yet assert over `skills.yml` at all** ([FS.GG.SDD#420](https://github.com/FS-GG/FS.GG.SDD/issues/420)) |

No PR spans two repos. And the second bullet is unsatisfiable even on its own terms: the pin cannot
advance to a CLI that **does not yet exist**. The CLI carrying the new `Fsgg.Registry` has to be
*published* before `.github` can pin it.

**ADR-0015 was not blind to this — it was inconsistent about it.** Its own Consequences say "SDD's own
release cadence already publishes the CLI the gate then pins", which is publish-then-pin, in as many
words. §3 nevertheless words the obligation as *atomic*. The defect is that the procedure and the
rationale disagree, and the procedure is the half people follow.

**Why nobody has tripped over it.** ADR-0015 was governance, not a behavioural change: it explicitly
held `schemaVersion` at `1`, and no schema has been bumped since. The procedure has therefore **never
been executed**. `mirrored:` ([#658](https://github.com/FS-GG/.github/issues/658), PR
[#687](https://github.com/FS-GG/.github/pull/687)) is the **first real schema growth** since ADR-0015
— and it immediately exposed that the procedure is unrunnable, which is why it landed *unbumped* and
needed [#686](https://github.com/FS-GG/.github/issues/686) to adjudicate.

### The window between the two PRs is not optional — the org's own machinery opens it

The tempting reading is that "same change" is merely *tidy*, and two PRs landed close together are
near enough. They are not, and the reason is a device ADR-0015 itself installed.

ADR-0015 §3 leans on the Renovate annotation manager to keep the validator current *structurally*
between deliberate bumps, "so the H2 freeze cannot silently recur". That coupling is now also
**asserted**: `pin-coherence.yml` hard-fails (not warns) when an annotated pin is behind the newest
version live on the org feed. A CLI release touches no file in `.github`, so it is the **daily
schedule** — not the path-filtered push trigger — that catches it. The workflow's own header says so:

> `push to main + schedule` — a CLI release that publishes a newer version turns `.github@main` red.
> **The schedule is what catches it**, since such a release touches no file in this repo.

So within a day of SDD publishing the CLI, `.github@main` is **red** and stays red until the pin
advances. Renovate then opens the bump PR — it does **not** merge it; there is no `automerge` in this
org's config, so a human still elects to land it. But the choice is only *when*, not *whether*: the
pin advance is the sole thing that greens `main`, and it is available immediately, while step 2's
bump may not be.

The consequence is what matters, and it does not depend on the bot being autonomous: **the pin
advances ahead of the schema bump, on its own PR.** The newly-published validator will therefore be
pointed at the not-yet-bumped document, on `main`, on a required gate. That gap is not a window we
merely tolerate — it is one the org's own anti-freeze machinery *opens for us*.

This is the fact ADR-0015 §3 could not see, because it assumed the pin advanced **as part of** the
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

3. **The validator published in step 1 MUST still accept the document as it stands at `.github` HEAD.**
   This is an obligation on the **validator**, and it is what makes step 1 safe to land alone. By
   §Context the pin advances *ahead* of the bump, so between the two PRs the new validator is pointed
   at the un-bumped document — on `main`, on a gate every consuming repo also runs, since they all call
   the same reusable `contract-coherence.yml`. Concretely, the new rule may **reject nothing the current
   document does**; enforce it only once the document *declares* the new `schemaVersion`.

   Be precise about when this bites, because the obvious reading makes it sound vacuous. A **purely
   additive optional** field enforces nothing, so an ungated validator reds nothing. The rule bites on
   any validator that would **reject the document it is about to be pinned against**, and there are two
   such shapes:

   - a **tightening** — a today-optional field made required, or unknown fields rejected; and
   - the far more tempting one: an additive field implemented as *"`mirrored` requires `schemaVersion`
     ≥ 2"*. That is the natural way to write it, and it is a **trap that is already armed**:
     `registry/skills.yml` carries `mirrored` **today, at `schemaVersion: 1`**. A validator asserting
     that coupling would reject the live document the moment
     [FS.GG.SDD#420](https://github.com/FS-GG/FS.GG.SDD/issues/420) ships the `skills.yml` assertion —
     which is step 1 of the very sequence this ADR prescribes.

   So the new field is *known* in step 1 and *required* only in step 2. Do not merely hope the two PRs
   land close together; the pin will not wait for them.

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

- **The ordering is derived, not chosen — it is the direction whose intermediate state is CHECKED.**
  The two states are not symmetric, and the asymmetry is not about which one is red:

  - **Validator ahead of document** (publish-before-flip): the pinned validator knows at least as much
    as the document declares, so every rule the document is subject to is a rule the gate can actually
    check. By §3 it enforces nothing new yet. The one red this state does produce — `pin-coherence`,
    while the pin is behind feed-newest — is **loud, expected, and self-clearing**: Renovate's bump PR
    greens it, and that PR stands alone, needing nothing from step 2.
  - **Document ahead of validator** (flip-then-publish): the document declares a schema the pinned
    validator has never heard of. This is **H2/M5 exactly** — the state ADR-0015 exists to close.

  And the second state does not announce itself. It is tempting to say it "cannot be made green"; that
  is **wrong**, and wrong in the direction that matters. Under the **additive tolerance** ADR-0015
  documents (`0015` Context, M5), a stale validator *passes* a grown document — it ignores what it does
  not know. `pin-coherence` is green too, because the pin still equals feed-newest (the new CLI does not
  exist yet). So flip-then-publish is **silently green over a schema nothing checks**, which is worse
  than any red: it is precisely the "typed validator degrades toward a YAML-parses check" failure, now
  *entered deliberately, by procedure*.

  That is the argument. Publish-before-flip's intermediate state is checked and its red is honest;
  flip-then-publish's is unchecked and its green is a lie.

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

- **Nothing enforces §3 today, and for `skill-registry` nothing would even NOTICE. We say so rather than
  claim a guarantee we do not have.** A validator that violates §3 against `registry/dependencies.yml`
  is caught by the red gate it causes — loudly, on `main`, but *after* the fact. Against
  `registry/skills.yml` it is not caught at all: `contract-coherence.yml` invokes
  `fsgg-sdd registry validate` on **`dependencies.yml` only**, so no gate reads `skills.yml` yet. The
  §3 obligation therefore lands on the **author of [FS.GG.SDD#420](https://github.com/FS-GG/FS.GG.SDD/issues/420)**,
  who ships the first `skills.yml` assertion and the gate wiring together — which is exactly why the
  `mirrored`-at-`schemaVersion: 1` trap in §3 is spelled out rather than left as an exercise.

  The clean tightening would be for the CLI to report the highest `schemaVersion` it knows, letting
  `contract-coherence.yml` assert `declared ≤ known` directly, for both documents. That needs an
  SDD-side surface (`Fsgg.Registry`), so it is a **cross-repo decision, not a fix available here**, and
  it is left open deliberately rather than half-built.

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
  direction — see Consequences. It parks the org in the H2/M5 state knowingly, and the damning part is
  that it does so **silently**: additive tolerance means the stale pinned validator *passes* the grown
  document, and `pin-coherence` is green because the pin still equals feed-newest. A procedure whose
  intermediate state is a green gate over a schema nothing checks is not a slower path to the same place;
  it is the defect, scheduled.

- **Keep "same PR" and satisfy it with a monorepo or a submodule.** Out of scope, and rejected by the
  whole shape of ADR-0001.
