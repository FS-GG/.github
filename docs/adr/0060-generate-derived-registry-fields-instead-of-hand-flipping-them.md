# ADR-0060: Generate the derived registry fields instead of hand-flipping them

- **Status:** Accepted
- **Date:** 2026-07-20
- **Affects:** FS-GG/.github (owns `registry/dependencies.yml`, the coherence gates `check-feed-coherence.py` / `check-source-coherence.py` / `check-pin-coherence.py` / `check-projection.py`, `feed-autofix`, `generate-projections`, and `docs/registry/compatibility.md`). No product repo changes; consumers still receive versions through the existing pin/dispatch fabric.
- **Interacts with:** [ADR-0058](0058-adopt-one-governing-principle-derive-dont-restate.md) (this is the principle's flagship application — the single highest-leverage *derive, don't restate*); [ADR-0037](0037-schema-growth-is-publish-before-flip.md) (publish-before-flip — a generated `package-version` cannot lead the feed because it *is* the feed read); [ADR-0044](0044-generated-artifacts-are-derived-from-their-generators.md) (the generated-artifact model, here applied to registry fields as well as files); [ADR-0034](0034-typed-coordination-engine.md) (fail-closed gate discipline, preserved).
- **Decision-first ADR:** records the approach and a recommendation for the derived-field mechanism. The **implementation** — item [.github#1260](https://github.com/FS-GG/.github/issues/1260) — is left open and unblocked; it proceeds once an option here is accepted. This ADR builds nothing.
- **Source:** [docs/reports/2026-07-20-cross-repo-coordination-overhead-root-cause.md](../reports/2026-07-20-cross-repo-coordination-overhead-root-cause.md) §3A, §7 P1.

## Context

`registry/dependencies.yml` stores, as editable YAML, facts that already have an authoritative home
elsewhere:

| field | authoritative source | the gate that already reads that source |
|---|---|---|
| `package-version` | the newest version **live on the org feed** | `check-feed-coherence.py` (queries the feed) |
| `version` (fsgg-contracts) | the FS.GG.SDD source `<Version>` on `@main` | `check-source-coherence.py` (reads the fsproj) |
| `package-tag` / live tags | the producer's release tags | the release/feed reads |
| the literal region of `docs/registry/compatibility.md` | the same rows above | `check-projection.py` |

Because each stored field is a **copy**, each can drift, so each earned a **fail-closed gate**. And here
is the tell the report names: **every one of those gates already computes the authoritative value on
every run** — it queries `api.nuget.org`, reads SDD's fsproj — and then **asserts equality against the
hand-typed literal instead of emitting it.**

The result is the *publish-before-flip step 2* commit class: a producer publishes a package, a gate
reds `.github@main`, and a human (now increasingly a bot — see below) lands a separate PR to retype the
number the gate already knows. The report measured **six such commits in one representative 24h**, and
the registry CHANGELOG has run 2–7 entries every day for 18 days. `compatibility.md` is a *third* copy,
needing its own reconcile and its own gate.

The org has already started down this path for **one** field: `skill-registry-autofix.yml` (`.github#299`)
now auto-reconciles `skills.yml` against producer manifests, and those reconcile commits are bot-authored
rather than hand-typed. That is the right *direction* — derive from the producer — but the report's P1
note is precise about the gap: the value still lands as a **committed literal** (four such bot commits in
the last day), so the churn moved from a human's keyboard to the bot's; it did not disappear.

## Decision

**Decision — Option A: generate the derived fields at generation time; keep the fail-closed gate on
the semantic fields only.**

Replace the "assert equality against a typed literal" gates for the *derived* fields with **generators**:
the registry's derived fields (`package-version`, `version`, live tags) and the literal region of
`compatibility.md` are **emitted** by reading the feed/source value at generation time. There is then no
literal to flip and nothing to drift — the *publish-before-flip step 2* commit class and the
`feed-autofix` / reconcile satellites collapse entirely.

The fail-closed discipline is **retained, and this is the load-bearing constraint**: it moves from
"assert the literal matches the source" to "the generation step fails closed when it cannot read the
source." *"I could not read the feed"* must never render as *"the value is whatever was last committed"*
— it must red, exactly as `check-feed-coherence.py` reds today (ADR-0044's failed-closed subtraction is
the model). The gate that survives is on the **semantic** fields — ownership edges (`owner`,
`consumers`), coherence *intent*, scope *meaning* — which are genuine authored declarations with no
upstream to derive from, and which must stay hand-editable and gated.

Publish-before-flip (ADR-0037) is **strengthened**, not bypassed: a generated `package-version` that
*is* the feed read cannot lead the feed by construction, which is the invariant FR-007 exists to hold.

## Consequences

- **The `package-version` / `version` / tag rows stop being hand- or bot-authored.** The
  *publish-before-flip step 2* PR class disappears; `feed-autofix` and the `skill-registry-autofix`
  reconcile commits (the bot half-implementation) are retired in favour of read-at-generation.
- **`compatibility.md`'s literal region becomes generated**, joining the ADR-0044 family of
  derived-from-their-generator artifacts; its reconcile commit class disappears.
- **The generation step itself must be fail-closed and gated**, or the win becomes a silent-staleness
  regression (#266's shape): a generator that emits the last-known value on a failed read is worse than
  the assert-equality gate it replaced. This is the single implementation risk and the acceptance bar.
- **Semantic gates are untouched.** Ownership, coherence intent, and scope meaning stay authored and
  fail-closed. The registry does not become "all generated"; it becomes "derived where a source exists,
  authored where one does not."
- **This ADR changes nothing until #1260 is worked.** It records the target; the migration is the item.

## Alternatives considered

- **Option B — keep the literals, automate the flip fleet-wide (extend the autofix bot to every drifted
  row).** This is the direction `.github#299` already started, and it is the `.github#1067/#1081`
  escalation (widen `feed-autofix` beyond its hardcoded `fs-gg-ui-template`). It removes the *human* from
  the loop but keeps the **committed literal** and therefore keeps the commit churn, the CHANGELOG
  entries, and the drift window between publish and reconcile. It is strictly less than Option A and, by
  ADR-0058's test, is the anti-pattern automated rather than removed. Viable as an **interim** (it is
  partly shipped) but not the target. Rejected as the end state.
- **Option C — status quo (hand-flip, gate on equality).** The measured cost is the whole reason this
  item exists. Rejected.
- **Generate the *semantic* fields too.** There is no source to generate them from — ownership and
  intent are decisions, not derivations. Generating them would mean inventing an authoritative home that
  does not exist, which is a different (and worse) projection. Explicitly out of scope: Option A draws
  the line at "has an authoritative home elsewhere."
- **Drop the derived fields from the registry entirely and have consumers read the feed directly.**
  Removes the projection but also the single coherent snapshot the registry exists to be — a consumer
  would have to query N feeds to answer "what can I restore against?" The registry's value is that it is
  *one* place; Option A keeps that while making the one place generated. Rejected.
