# Cross-repo coordination overhead — a root-cause analysis of 2026-07-20

- **Date:** 2026-07-20, hub at `.github@b5ba1fb` (main)
- **Owner:** `.github` (cross-repo coordination)
- **Status:** Analysis + proposals. No decision is taken here; §7 lists candidate changes, each of which would need to be argued and filed.
- **Question:** The last 24 hours across the eight FS-GG repos were dominated by coordination work and repos pinging each other. Why, and what would reduce it?
- **Method:** org-wide commit census (8 repos, `--since=2026-07-19`); six parallel deep traces (registry coherence + publish-before-flip; kit/build-config sync; the typed coordination engine + rooms/predicate; ADR + skill-scope churn; package/Contracts propagation; live board state); cross-checked against the prior [2026-07-12 throughput audit](2026-07-12-issue-throughput-and-recurring-error-loops.md) to see what the org learned in the eight days since.

---

## 1. TL;DR

- **Three quarters of the day was overhead.** Of **98 commits** merged org-wide in the last 24h, **~76% (74) were coordination/registry/release bookkeeping or ADR authoring; only ~24 were substantive feature or fix work.** That is not a one-off — the registry CHANGELOG has run 2–7 entries **every single day** for 18 days.
- **The pinging is largely self-inflicted, and it has one shape.** Almost every cross-repo "ping" this org generates is the same move: **a fact that already exists in an authoritative place (a package feed, a source `<Version>`, a producer's skill manifest, a shared file's bytes) is copied by hand into a second place (`registry/dependencies.yml`, `skills.yml`, a vendored mirror, `compatibility.md`), and then a gate is built to detect the drift the copy just made possible.** The org now runs **26 coherence gate scripts** and **62 workflows** — **14 of them exist only to test the coordination machinery itself.**
- **Ten ADRs were authored in two days (0048–0057), two of them (0054, 0057) solely to add one enum value each.** The decision cadence is now faster than the feature cadence, and much of it is the coordination system making decisions about itself.
- **The org's diagnosis is excellent; its restraint is the bottleneck.** The 2026-07-12 audit named this exact failure mode ("the org checks that a declaration exists, not that the capability behind it works") and its top fixes *did* land — `default.json` now reads nuget.org, and the 7,000-line `fsgg-coord` bash monster is now a 481-line shim over a typed engine. But in the same eight days the system **added** four new coordination surfaces (predicate gates, rooms, driver scope, operator scope). It fixes loops and accretes new ones at the same time. **Net complexity went up.**
- **The single highest-leverage change:** stop storing derived data (version/package literals, feed state, mirror bytes) as hand-flipped registry rows. The gates already *compute* the true value on every run — they just assert it against a human-typed copy instead of generating it. Doing this collapses the entire "publish-before-flip step 2" commit class (6 of the last 24h) and most of the daily registry churn, while keeping every fail-closed gate intact.

---

## 2. The census — what the 24h actually contained

| Repo | 24h commits | Character |
|---|---:|---|
| **`.github`** (hub) | **37** | almost entirely registry flips, coherence hardening, ADRs, scope-class rollouts |
| FS.GG.Game | 16 | **real work** — Red Blob algorithms M1–M4 (pathfinding, hex, visibility, combat), headless harness |
| FS.GG.Rendering | 15 | mostly `chore(deps)` adopts + lifecycle-default flip + ledger reconcile |
| FS.GG.SDD | 13 | FR-classifier feature + two CLI releases + two "accept scope, known-not-enforced" step-1 commits |
| FS.GG.Governance | 8 | gate-inheritance feature + two Contracts adopt PRs |
| FS.GG.Net | 7 | **real work** — new 8th repo scaffolded and shipped to 0.1.0 |
| FS.GG.Audio | 1 | one inbound kit-sync chore |
| FS.GG.Templates | 1 | one inbound kit-sync chore |

Genuine product progress happened in exactly two places — **FS.GG.Game** (the Red Blob algorithm milestones) and **FS.GG.Net** (a whole new transport component stood up in a day). Everything else was the coordination layer maintaining itself: version flips, adopt PRs, kit syncs, ADRs about ADRs, and new registry enum values.

---

## 3. The five friction engines

Each is individually well-engineered. The problem is not any one of them; it is that they all instantiate the same anti-pattern and compound.

### A. The registry is a hand-flipped duplicate of ground truth

`registry/dependencies.yml` (1,772 lines) stores, as editable YAML, facts that already have an authoritative home elsewhere: `package-version` (= newest on the feed), `version` (= the source `<Version>` on SDD's `main`), live tags, coherence booleans. Because those are *copies*, each can drift, so each got its own **fail-closed gate** — `check-feed-coherence`, `check-source-coherence`, `check-pin-coherence`, `check-projection`. And here is the tell: **every one of those gates already computes the authoritative value on every run** (it queries `api.nuget.org`, reads SDD's fsproj, etc.) — it just asserts equality against the hand-typed literal instead of *emitting* it.

The result is the **"publish-before-flip step 2"** commit class: a producer publishes a package, a gate reds `.github@main`, and a human lands a separate PR to retype the number the gate already knows. **Six of the last 24h commits were exactly this.** `compatibility.md` is then a *third* copy of the same facts, needing its own reconcile commit and its own gate.

### B. The schema document and its validator live in different repos

The registry's schema lives in `.github`; its typed validator (`Fsgg.Registry`) lives in **FS.GG.SDD** and ships as a versioned CLI. ADR-0015 wanted the schema-teach and schema-bump in one PR — but **no PR spans two repos** (`.github#689`). So every schema change is forced onto the **publish-before-flip rail** (ADR-0037): teach + publish a CLI in SDD (step 1, "known-not-enforced"), then bump + pin in `.github` (step 2). This is why adding **one enum value** (`driver`, then `operator`) costs *an ADR + two ordered PRs across two repos + a CLI release + a pin advance + four regenerated artifacts.* The phrase "known-not-enforced / step 1 / step 2 / publish-before-flip" appears in **12 commit subjects** in five days. It is a load-bearing ceremony, and its sole cause is that the document and its checker were placed in different repos with no spanning change.

### C. The F# positional-record ABI tax

`FS.GG.Contracts` has burned three majors in one week — 2.0.0, 3.0.0, 4.0.0 — and **all three trace to adding a field to one record, `ContractEntry`.** An F# record compiles to a positional primary constructor, so *any* new field changes constructor arity and deletes the old one → ApiCompat flags CP0002 → a **SemVer major is forced** → every consuming repo files an adopt PR and the hub files a registry flip. The catch: these are real **binary** breaks but rarely real **semantic** breaks — 4.0.0's new field is optional and "rejects nothing the current document does." **The org pays a fleet-wide adopt round for changes that are semantically additive.** This is a language-idiom tax, not a genuine contract break, and it is the engine behind the "adopt Contracts 4.0.0" ping-pong (SDD → hub flip → Governance → Rendering, ~4 repos + hub per major).

### D. The kit and build-config fan out by byte-copy

Shared skills, the `fsgg-coord` client, and `dist/dotnet` build config are distributed to receivers by **byte-identical file copy**, content-addressed by digest. So *any* change to a managed file — including a whitespace or comment edit — is a new digest that deterministically opens **one sync PR in each of 7 (kit) or 4 (build-config) receivers.** Over ten days this produced **80–116 kit-sync commits per repo.** FS.GG.Audio's and FS.GG.Templates' entire 24h was absorbing one such wave and nothing else. ADR-0036 removed the *merge-freeze* half of this pain (behind is now green-with-notice, not red), but the fan-out multiplication remains: the hub is a single write point whose every commit multiplies into N downstream commits.

### E. The coordination engine and taxonomy accrete gates faster than they retire them

The typed engine (ADR-0034) was a genuine win — it replaced ~7,000 lines of fail-open bash with a total `schedulability` function and three-valued fail-closed logic, and that discipline demonstrably prevents double-claims and fake-ready flips. But the last 24h *also* added: a three-valued predicate oracle re-verified at **two** transitions (ADR-0050), a new `Rooms:` body-grammar family (ADR-0051), a `driver` scope class (ADR-0054), and an `operator` scope class (ADR-0057). To file **one** cross-repo request an agent now fills ~9 form fields, may touch 3–5 fence-aware body-grammar families (`Paths:`, `Blocked by:`, `Rooms:`, block/chore sentinels, the registry assertion triple), and to advance it clears a 4-condition flip gate that reads two files across a repo boundary. Two of the ten recent ADRs (0054, 0057) exist *only* to catalog one already-working skill each; 0056 then re-keys 0053/0054 again. The taxonomy is over-fit — one skill per class — and each new coordination concern adds *another* parsed surface rather than reusing one.

---

## 4. The one root cause under the five

> **The org models reality as hand-maintained projections, then builds a gate per projection to catch the drift the projection made possible — and every new capability is rolled out by adding another projection and another gate rather than by deriving from a single source.**

This is the same defect the 07-12 audit stated one layer down ("a check that passes when its subject is missing is worse than no check") generalized: *a registry that must be hand-edited to match the feed is worse than a registry generated from the feed.* The gates are not the problem — fail-closed gates are the org's best invention. The problem is that **half of what they gate is a copy the org itself is obligated to keep typing.** The split-repo decision of 2026-06 (`project-split-decision.md`) explicitly split the repos to *escape* a recursive maintenance cost where "product changes become governance-schema changes." The coordination layer has quietly re-created exactly that: a product change (add a field, publish a package, edit a skill) is now, reliably, a registry-schema change, a gate-reconcile, and a cross-repo ping round.

---

## 5. Does the org learn, or just accrete? — Both.

The 07-12 audit's top recommendations **landed**: `default.json` now resolves `FS.GG.*` from nuget.org (killing the 401-freeze loop), and `fsgg-coord` is now a shim over the compiled engine (killing the 43%-a-day bash treadmill). So the diagnostic→fix pipeline *works* when a fix is owned. But in the same window the org shipped four brand-new coordination surfaces (§3E). **The rate of new gates/scopes/ADRs exceeds the rate of retirement.** The 07-12 meta-finding still holds verbatim eight days later: the bottleneck is not seeing the problem — it is deciding to *stop adding mechanism* and instead *remove the class of drift the mechanism guards.*

---

## 6. What is actually stuck right now

The coordination *board* is healthy — this matters. Projects v2 #1 holds **1,339 items, 1,334 Done**; only **5 are live** (3 Blocked, 1 In progress, 1 Ready), and 209 historical blockers have collapsed to **3 active**. The pain this report is about is **not** a jammed board; it is the *volume of ceremony required to keep the board that clean.* The machinery works — it is just enormously expensive per unit of real work.

There is exactly **one live coordination hotspot**, and it is textbook:

```
Templates#258  re-pin composition/providers to sdd default        (Blocked)
   └─ blocked by → Templates#260  composition CI red on main       (Blocked)
        └─ blocked by → SDD#609  scaffold doesn't mirror skill-registry
                                  v3 skills into ADR-0011 roots     (Blocked, ROOT — unassigned)
```

- **SDD#609 is the true bottleneck** and its own `blocked by` is **null** — nothing is scheduled to clear it. A Templates worker found it while working #260, correctly root-caused it across the repo boundary into SDD, filed it there — and it now sits **unowned.** This is the 07-12 meta-finding reproduced live: *being right about a blocker and being assigned to it are different things.*
- **Blast radius:** SDD#609 notes `composition` is a required check under `enforce_admins`, so its red gates **every open Templates PR** — including the innocent comment-only PR#259 — not just #258.
- **Stale board state:** Rendering#939 (In progress) has a `blocked by` field pointing at Rendering#951, which is **already closed** (08:04 today). The field should be re-verified and cleared.

The entire live surface is one migration wave — the ADR-0056 spec-kit→sdd default flip — rippling SDD → Templates and Rendering. Clearing SDD#609 unblocks the chain; refreshing #939's stale field clears the second.

**Immediate, no-design-needed actions:** (1) assign an owner to **SDD#609**; (2) re-verify and clear **Rendering#939**'s blocker; (3) close the dangling Governance **PR#284** ("…v4 — abandoned").

---

## 7. Proposals — leverage-ordered

Backwards compatibility is explicitly not a constraint here. These are ordered by (churn removed ÷ effort).

### P1 — Generate the derived registry fields; stop hand-flipping them. *(kills ~6 commits/day)*
`package-version`, `version`, live tags, and the whole of `compatibility.md`'s literal region are **pure functions of ground truth** that the coherence gates already compute. Replace the "assert equality against a typed literal" gates with **generators**: the registry *reads* the feed/source value at generation time; there is nothing to flip and nothing to drift. Keep the fail-closed gate on the *semantic* fields only (ownership edges, coherence intent, scope meaning). This alone collapses the entire "publish-before-flip step 2" class and the `feed-autofix`/reconcile satellites. It is the single highest-leverage change in the system.

### P2 — Collapse publish-before-flip by removing the two-repo split. *(kills the step-1/step-2 dance)*
The schema document and its validator are in different repos *only* because of where they were first written. Options, any of which dissolves the rail: (a) move the `Fsgg.Registry` validator's **schema-of-record into `.github`** (a data file the CLI reads), so a schema change is one PR in `.github`; or (b) make the validator **schema-version-agnostic** — validate structure, not a pinned enum, so adding an enum value is not "schema growth" at all and needs no CLI republish. (b) is the deeper fix: it also removes the "additive is still growth" tax (`.github#686`) for the common case.

### P3 — Kill the F# positional-record ABI tax on `FS.GG.Contracts`. *(kills the fleet adopt round on additive changes)*
Stop paying a major for a new optional field. Change the wire/registry contract surface so additive fields are **not** binary breaks: use records with optional members via a non-positional shape (e.g. `[<CLIMutable>]` with defaulted members, a builder, or an explicit interface surface), or split the frequently-grown `ContractEntry` into a stable core + an open extension map. Then ApiCompat sees a minor, Renovate opens no fleet-wide adopt round, and the hub files no flip. This removes the largest *legitimate-looking* source of cross-repo pings.

### P4 — Replace byte-copy kit/build-config sync with a versioned package. *(kills 80–116 sync commits/repo per 10 days)*
The kit and build config fan out as file copies because they are not packages. Ship them as one **versioned artifact** (`FS.GG.Kit` on the same feed) that each repo references like any other dependency. Then a hub change is *one publish*; receivers pick it up through the auto-update fabric that already exists (Renovate + dispatch) — the same machinery used for every other shared artifact — instead of N bespoke sync PRs and a hand-maintained `paths:` filter. This also deletes the `coordination-propagate` / `build-config-propagate` / `-selftest` workflow family.

### P5 — Freeze the coordination taxonomy; require two instances before a new class. *(caps ADR/scope churn)*
Two of the last ten ADRs added a scope enum value for a single skill. Adopt a rule: **no new scope class, body-grammar family, or predicate surface until at least two real cases demand it**, and prefer re-homing a skill to a producer repo (zero schema growth) over minting a class to catalog it in place. Collapse `driver` + `operator` into one "hub-authored, delivery-varies" class with a delivery field. This is a policy change, not code, and it directly slows the decision-cadence-exceeds-feature-cadence problem.

### P6 — Adopt one governing principle and hold the line on it.
*Derive, don't restate. Gate capabilities, not declarations. Do not add a projection you must then hand-maintain; if a fact has an authoritative home, read it — never copy it.* Every one of the five engines above is a violation of this single sentence. Making it the explicit test that every new ADR must pass would convert the org's excellent diagnosis into the restraint it currently lacks.

---
