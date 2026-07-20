# ADR-0060: Generating registry fields derived from external truth — the model P1 needs

- **Status:** Proposed
- **Date:** 2026-07-20
- **Affects:** FS-GG/.github (registry coordination fabric); every producer repo as feed/source ground truth
- **Applies:** [ADR-0058](0058-adopt-one-governing-principle-derive-dont-restate.md) — *derive, don't restate; prefer a generator to an assert-equality gate.* This record is that principle worked through its hardest case: a fact whose ground truth is **external and mutable**, where "derive at generation time" collides with a load-bearing invariant. The resolution keeps the value *derived* (a machine, never a human, writes it) without a generation-time network read.

## Context

The 2026-07-20 root-cause report (`docs/reports/2026-07-20-cross-repo-coordination-overhead-root-cause.md`, §3A) measured that ~6 of the last 24h's commits were "publish-before-flip step 2": a producer publishes a package, a coherence gate reds `.github@main`, and a human lands a separate PR retyping a number the gate already computed. Its proposal **P1** (epic #1259, item #1260) is *"Generate the derived registry fields; stop hand-flipping them."* ADR-0058 (P6, #1264) then adopted the governing principle P1 is an instance of — *prefer a generator to an assert-equality gate*. This ADR decides the **mechanism** for P1, because the principle's default reading does not survive contact with P1's ground truth. The parent report landed **"report-only; no decision taken"** — so P1 is a proposal awaiting exactly this call, not approved work.

Investigating P1 end-to-end (recorded on #1260) turns up the constraint:

- **The tractable half is already landed.** `docs/registry/compatibility.md`'s contract-version literal region is already a generated projection of `registry/dependencies.yml` (`scripts/generate-projections` → the `fsgg-contract-versions` region, gated by the required `projections` workflow; ADR-0044, #1081, #748). A registry flip regenerates it green — the "third copy" needs no hand-reconcile. This is ADR-0058's principle already shipped at a seam where the ground truth is *local*.

- **The remaining hand-flipped literals — `package-version` / `version` / `package-tag` in `registry/dependencies.yml` — derive from ground truth that is external and mutable:** newest on the live org NuGet feed (`scripts/check-feed-coherence.py` via `fsgg_feed.newest()`), and `FS.GG.Contracts`' `<Version>` on `FS-GG/FS.GG.SDD@main` (`scripts/check-source-coherence.py`, a remote checkout).

- **The org deliberately separated two roles, and the separation is load-bearing:**
  - **Generation** (`scripts/generate-projections`) reads only **local** files and is **offline-deterministic**. That is what makes its `--check` a safe **blocking** PR gate: two runs on the same tree always agree, so a red means the author forgot to regenerate, never that the world moved.
  - **Detection** (`check-feed-coherence`, `check-source-coherence`) reads **live** external truth, is allowed to be non-deterministic, and runs on a **schedule** (`cron` + `repository_dispatch`) as well as on PRs — so a publish that touched no file here is still caught.
  - `#748` forbids the `feed-autofix` bot from writing prose for the same reason: the machine owns the mechanical literal, the human owns the judgement.

The naïve reading of "prefer a generator" — *make the generator read the feed at generation time* — **collapses that separation and regresses the blocking gate**: a generator reading the live feed makes `--check` non-deterministic, so a producer publishing mid-PR regenerates a different value and reds an unrelated PR. That is "a publish reds `.github@main`," reintroduced into the *blocking* gate — the exact behaviour P1 exists to remove.

The premise worth challenging is *"eliminate the committed literal."* A committed literal that caches external truth is not the defect; it buys **offline determinism** for every consumer (the projection, humans reading the file, CI with no feed token). The measured pain is that a **human hand-types** it. Reframe the literal as a **materialized cache whose sole writer is a machine**, and the churn dissolves without touching the generation/detection split — and without violating ADR-0058, because the value is still *derived from* its authoritative home, just written by a bot against the scheduled detection gate rather than read at offline-generation time.

## Decision

Adopt the **materialized-cache** model for registry fields derived from external truth, and make P1 the automation of the *write path*, not the elimination of the stored value:

1. **The derived literals (`package-version`, `version`, `package-tag`) stay committed** in `registry/dependencies.yml`. They are a cache of external ground truth, and being committed is what keeps generation offline-deterministic and the file readable without feed access.

2. **A machine becomes their sole writer.** Generalize `scripts/feed-autofix` from its one hardcoded row (`CONTRACT = "fs-gg-ui-template"`) to every package-bearing contract, so *all* `package-version` advances land as bot-authored reconcile PRs (the mechanism already exists: App-authored, opens one standing PR, regenerates the projection, forbidden prose per #748). This retires the human "step-2 flip" commit class — the ~6 commits/day the report measured. `#299`'s `skill-registry-autofix` already established this shape for the sibling `skills.yml` case.

3. **Detection stays exactly where it is.** `check-feed-coherence` / `check-source-coherence` remain the live, scheduled gates. They are the fail-closed safety net that makes a *cache* trustworthy: if the bot is down or wrong, the gate still reds. Generation stays offline-deterministic — **no `scripts/generate-*` generator reads the network.**

4. **The "no committed literal at all" end state (compute-on-demand) is explicitly deferred, not pursued now.** It is gated on P2 (#1261) — removing a field from the registry schema crosses the schema/validator repo split — and it trades one hand-flip for pervasive network dependence in every consumer. Revisit only if P2 lands and a concrete consumer benefit appears.

This is Proposed for ratification alongside ADR-0058: accepting it authorizes #1260 to be scheduled as the bot-generalization work above; rejecting it (in favour of the network-reading generator, or of compute-on-demand) is the alternative the record below preserves.

## Consequences

- **#1260 becomes executable and well-scoped:** generalize `feed-autofix` across the package-bearing contract set, keeping the existing standing-PR / App-token / prose-forbidden shape. The `contract-id → package-id` map it needs already exists as `CONTRACT_PACKAGES` in `check-feed-coherence.py`; sharing one copy (rather than duplicating it) is the natural refactor, and keeping it gate-/bot-local avoids the schema growth P2 owns.
- **A new invariant, stated for future ADRs to inherit:** no `scripts/generate-*` generator may read the network — that offline-determinism is what lets its `--check` block PRs. "Derive an external fact" work routes through the scheduled detection gate + a bot writer, not a generation-time network read. This is the boundary ADR-0058's principle needs at the external-truth seam, so a future reader does not apply "prefer a generator" to the feed and regress the blocking gate.
- **Coherent sets** (e.g. `fs-gg-audio` → four packages, `fs-gg-net` → six) and the FRAMEWORK-vs-TEMPLATE classification currently specific to `fs-gg-ui-template` are the real work in generalizing the bot; the classification must be re-derived per contract rather than assumed. This is the risk surface to review in #1260's PR.
- **`version` (SDD source) is left on detection-only for now.** It advances rarely (a Contracts release), its writer would need a cross-repo checkout, and folding it into the bot is a smaller, separable follow-up once the feed path is generalized.
- **The report's "full P1 win" is reframed, not delivered.** This records that the committed cache is a deliberate keep, so a future reader does not re-open P1 believing the literal is unfinished business.

## Alternatives considered

- **A — Network-reading generator with an advisory (`--warn`, non-blocking) `--check`.** Makes regeneration the write path by having the generator read the feed/source. Rejected: it breaks the invariant that generation is offline-deterministic, and demoting `--check` to advisory weakens every projection gate's guarantee, not just this one. It moves the non-determinism *into* the generator rather than isolating it in the scheduled detection gate where the org deliberately put it — the literal-minded reading of ADR-0058 that ADR-0058's own §4 ("keep fail-closed gates") does not demand.

- **C — Remove the derived fields from the registry schema; compute on demand.** The report's literal "no committed literal at all." Rejected for now: (1) it is gated on P2 (#1261) — the field lives in a schema validated by `Fsgg.Registry` shipping from SDD, so removing it is the two-repo publish-before-flip rail P2 exists to dissolve; (2) it trades one hand-flip for network dependence in *every* consumer that today reads the value offline (the projection, humans, tokenless CI), which is a determinism regression, not an improvement. Reconsider only after P2, and only against a concrete consumer that benefits.

- **Do nothing (keep humans in the write loop).** Rejected: this is the measured ~6 commits/day the report set out to remove, and #299's `skill-registry-autofix` already established the "bot writes the reconcile" pattern for the sibling `skills.yml` case.
