# ADR-0034: The coordination engine is a typed core; the tool is the model, and the docs are its projection

- **Status:** Accepted
- **Date:** 2026-07-12 (proposed) · 2026-07-12 (accepted)
- **Affects:** `.github` (authority), and every `receives: coordination-kit` repo — sdd, rendering, governance, templates, game, audio
- **Design doc:** [docs/design/coordination-engine.md](../design/coordination-engine.md)
- **Amends:** [0019](0019-org-repo-roster-registry-and-coordination-kit.md) (the `kit:` row shape), [0027](0027-worker-keyed-claim-lock-and-worker-channel.md) (implementation only — the CAS stands)
- **Contract-change under:** [0015](0015-register-the-registry-schema-as-a-governed-contract.md) — `registry/repos.yml` `schemaVersion` bump

## Context

`scripts/fsgg-coord` is 4,024 lines of bash whose state model is jq regex captures over prose:
claims are HTML comments matched with `capture("^<!--\\s*fsgg:claim\\s+worker=(?<w>[^\\s>]+)")`,
touch-sets are a bare `Paths:` line read by an awk/grep/sed pipeline, and dependency edges are a
free-text field — as the tool itself records, *"`Blocked by` is TEXT only because Projects v2 has
no typed dependency field."*

That is a **concurrent, transactional, budget-constrained domain modelled in a substrate with no
types, no `Result`, no atomicity, and whose default failure mode is to fail open.** The defect
record follows from the substrate, not from carelessness:

- **The scheduler family — 34 issues on one question.** "Is this item startable?" has consumed
  #431, #435, #437, #440, #445, #454, #480, #488, #496, #516, #520, #533, #534, #540, #581 and
  more. One function went four rounds: #440 → #452 fixed it → #481 found the fix broke an
  invariant → #488 found *"#440's fix reintroduced #440's defect in its own else-branch."*
  [#485](https://github.com/FS-GG/.github/issues/485) names the cause: **startability is computed
  in five places and agrees in none.**
- **The fail-open family — epic [#266](https://github.com/FS-GG/.github/issues/266) has 51
  children.** In bash an error, an empty result, and a legitimate "no" are the same value.
  #461: `active_claims` fails open, so a failed scan reads as *"nothing is claimed"* and `take`
  hands a held item to a second worker.
- **The projection family.** A rule is stated in up to six places — the ADR, the canonical doc,
  the tool, and four `SKILL.md` × two skill roots — then content-addressed into `repos.lock` and
  pushed to six receivers: **54 vendored copies of the protocol.** The propagation edge is *a
  second issue and a second PR, every time*: #309 → #502, #481 → #531. The collision attractor
  was removed **by hand twice** (#532, #551) before #570 gated it.
- **The budget has no owner.** The GraphQL primary limit is 5,000 pt/hr *shared across the whole
  fleet* (N agents, one account); five workers looping `take` drained it in ~15 minutes (#418).
  Yet three skills, two docs, and a workflow call `gh api graphql` / `gh project` **directly,
  outside the tool** — which is what produced #528 and #538.

Two facts constrain any remedy more than the domain does. First, `fsgg-coord` is not a standalone
CLI: it is a `kind: client` row in the coordination kit, sha256'd into `registry/repos.lock`,
byte-copied with its exec bit into six repos, byte-compared there by `coordination-coherence`, and
shelled out to by `touch-set-drift.yml`, which **greps its stdout for verdict tokens**. Second,
the comment-order CAS at the heart of the claim lock is **sound** — GitHub issues comment ids from
one server-side sequence, so "lowest live marker wins" is a total order every racer observes
identically — and it lives on REST *deliberately*, because GraphQL is the first budget to die
under fan-out and **a lock may never live on the budget that dies first.**

## Decision

**1. The coordination domain moves to a typed F# core.**

`FS.GG.Coord.Core` is pure and IO-free and is the **only** place a coordination rule exists.
Schedulability becomes **one total function** returning a discriminated union, never a bool —
`Startable | NotOnBoard | NoTouchSet | DeliberatelyNoTouchSet | BlockedBy | BlockerUnknown |
Held | IssueClosed | WrongStatus | Undetermined`. Fourteen of the currently-open scheduler issues
are a missing case in that union.

Every check returns `Green | Red | NoVerdict`, and **`NoVerdict` is non-zero**. The org already
evolved this rule the hard way — the non-vacuity guard plus `exit 3` — and enforces it by
convention across a dozen scripts. It is a type. Make it one.

The comment-order CAS, the REST-hosted lock, and the `fsgg:claim` marker format are **preserved
unchanged**. The bugs around them (#419, #461, #550) were in worker *identity* and *fail-open
scanning*, not in the lock. A git-ref CAS was considered and **rejected**: it is a third state
substrate, it puts the lock behind git auth in CI, and it replaces a mechanism that is already
correct with one that is merely more elegant.

**2. Distribution: the kit row becomes a shim; the engine ships as a `dotnet tool`.**

The `fsgg-coord` `kit:` row stays — as a small bash shim that resolves the tool from
`.config/dotnet-tools.json` (already distributed to every repo by `sync-build-config.sh`, and
already watched by Renovate) and `exec`s it. Therefore the kit row, its digest, its exec bit, and
the `scripts/fsgg-coord` path that every doc, workflow, and skill references are **unchanged**;
`coordination-coherence`, `repos-registry-selftest`, and `touch-set-drift` keep working with no
edit; and the shim stops churning, so the kit stops re-drifting on every protocol edit.

**The publish cycle is broken by asymmetry:** `.github` builds the engine **from source** and
never depends on the feed. Only *receivers* consume the package. A broken feed therefore cannot
prevent the coordination tool from being fixed.

Rejected: a self-contained single-file binary as the `kind: client` row (mechanically it fits —
the kit already carries a digest and an exec bit — but a 20–70 MB binary × 6 receivers × every
version is git bloat, needs a per-RID matrix, and **is invisible to Renovate**, which is
`datasource=nuget`; no org precedent exists). Also rejected: receivers installing the tool with no
shim, which would break every hard-coded `scripts/fsgg-coord` path in the fabric.

This changes the `kit:` row shape and is a **contract-change under ADR-0015**: `registry/repos.yml`
takes a `schemaVersion` bump.

**3. The tool is the sole GraphQL principal in the org, and it is gated.**

No skill, doc, recipe, workflow, or agent may invoke `gh api graphql`, `gh project`,
`gh issue view`, or `gh issue list`. Every board and issue read goes through the tool — the only
thing that can meter, cache, and queue against a budget that is shared by the entire fleet.

A new gate (`check-graphql-monopoly`) enforces this over the skill roots, `docs/`, and
`.github/workflows/`, with a non-vacuity guard and `exit 3` on zero subjects, modelled on
`check-worker-id-attractor.py`. **Six violators exist at HEAD.**

The monopoly is the enabling condition for three things that are otherwise unbuildable: one budget
accountant, one scan cache, and **aliased writes** — a Projects v2 field mutation returns ~1 node
and therefore costs the 1-point floor *no matter how many are aliased into one document*, so a
placement pass drops from 6 points per item to 1 ([#448](https://github.com/FS-GG/.github/issues/448),
a measured ~6× that has been open since the errata in `graphql-budget.md` corrected the claim that
"batching does nothing").

**4. The docs and skills become generated projections of the model.**

`fsgg-coord` is **already the model** — it is simply not the *source*. In every drift that can be
dated, the tool was right and the prose was wrong (`db279ca` fixed the tool one PR before `cb98ad3`
fixed three docs). `check-worker-id-attractor.py` already calls `parallel-work.md` *"the document
those skills are a projection OF"* — and exists only because the projection is copied by hand.

So invert the dependency: `docs/coordination/parallel-work.md` and the four `SKILL.md` bodies are
**emitted from the typed model** and guarded by a regeneration gate, exactly like `registry/repos.lock`.
A rule then cannot land in one tier and not the others, because there are no longer tiers.

**5. Migration is shadow-mode. Bash stays authoritative until divergence is zero.**

The engine ships behind `--engine=fs`, defaulting off. Both engines run on every invocation; bash's
answer is returned; divergence is logged. Cut-over requires **zero divergence across the live fleet
for three consecutive days**. The 4,024 lines of bash comments are an incident log, and porting them
into ~60 named regression tests — one per historical defect — happens **before** any production code
is trusted.

## Consequences

**What this obliges `.github` to do**

- Bump `registry/repos.yml` `schemaVersion` and teach `repos.sh`, `coordination-sync`, and the
  coherence gate the shim/tool distinction (ADR-0015 contract-change).
- Ship `check-graphql-monopoly` and fix the six violators at HEAD.
- Own the engine's release workflow. Precedent exists: `release-new-sdd-workspace.yml`, whose header
  already states the rule — *".github does not normally own a release workflow — this is the
  exception: the tool lives here, so its publish does too."*
- Adopt SDD's stricter standard for the new project (`Directory.Build.props`, a lockfile, xUnit), not
  `.github`'s existing looser one — `NewSddWorkspace` consumes none of those today.

**What this obliges every receiver to do**

- Gain an `actions/setup-dotnet` step where the coordination tool is invoked in CI (`touch-set-drift`
  runs on bare `ubuntu-latest` today).
- Carry a .NET runtime on the coordination path. All six receivers are .NET repos, so this is close
  to free — but it is a new dependency for coordination *specifically*, and it is the honest cost of
  this decision.

**What gets retired**

**22 of the 40 open `.github` issues (55%)** live in the domain this restructures. Phase 4's generated
projections retire the #502 / #531 / #551 / #555 family and the 54 vendored protocol copies **by
construction** — a collision in a generated file is a rebase, not a decision (#309's rule then applies
to them), and `check-worker-id-attractor.py` can be deleted, because it exists only to catch a
copy-paste that no longer happens.

**What this explicitly does NOT fix**

The build/publish/pin/feed substrate — #504, #561, #574, #576, #519, and epic
[#423](https://github.com/FS-GG/.github/issues/423). That is a separate root cause with its own
design and it will keep producing findings at its current rate. This ADR must not be read as a
remedy for it.

**The architecture map — reconciled with this ratification**

The proposing PR ([#589](https://github.com/FS-GG/.github/pull/589)) opted out of the map reconcile,
correctly: a *Proposed* ADR changes no repo, boundary, or coherent-set axis. It recorded that
**accepting it would not stay free**, and named the obligation so it could not be lost.

Ratification discharges that obligation in the same change. `docs/architecture.md` §1 and §7 now
record two things, and the second was already true before this ADR:

1. **`.github` is a producer.** Its `Ships` column said `—`. It has shipped `FS.GG.NewSddWorkspace`
   (`new-sdd-workspace`) as a `dotnet tool` since ADR-0016, with its own release workflow. The map
   never caught up. This ADR adds a *second* tool to that repo, so the row had to be right first.
2. **The coordination client becomes a coherent-set member.** `FS.GG.Coord.Cli` (`fsgg-coord`) is a
   packaged `dotnet tool`; the `kit:` row becomes a digest-pinned **shim** that resolves it from the
   already-distributed `.config/dotnet-tools.json`. That is the shape change this ADR decides, and
   it is now on the map as *planned*, not as done — the engine does not exist yet.

**The `repos.yml` `schemaVersion` bump is NOT part of this ratification.** It is a contract-change
under ADR-0015 and it lands with the *implementation* (Phase 3), not with the decision. Bumping a
schema for a shape no code produces would put the registry ahead of reality, which is the failure
this ADR is about.

**The standing risk**

The tool schedules the work that rewrites the tool. Shadow mode is what makes that survivable: nothing
is cut over on faith, and bash remains authoritative until the new engine has been proven to agree with
it on live traffic. If the port stalls, the shadow costs nothing and bash keeps running.

**A note on arrival rate.** 103 findings were filed in `.github` in the 24 h to 2026-07-12 against 71
closed. That is *supply*, not demand — the fleet finds faster than it fixes. No rewrite changes that,
and this ADR does not claim to.
