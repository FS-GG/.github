# ADR-0009: The `fsgg-sdd` CLI is the single orchestrator — detect-and-remediate, not silent auto-update

- **Status:** Proposed
- **Date:** 2026-07-01
- **Affects:** FS.GG.SDD (CLI producer + policy owner), .github (registry/ADR owner), FS.GG.Templates + every scaffolded consumer

## Context

[ADR-0008](0008-fsgg-sdd-cli-first-class-member-of-coherent-set.md) established the *orchestrator
axis*: a `fs-gg-ui-template@<V>` coherent set carries a **minimum coherent `fsgg-sdd` version**,
and the CLI must "**warn (or fail)** when the installed CLI is behind" that minimum. It
deliberately left the *enforcement and remediation policy* open — "warn or fail" is a fork, not a
decision. This ADR settles that fork.

A tempting simplification is to make the CLI the **single source of truth** that, on every
consumer invocation, **auto-updates itself and then auto-updates the other artifacts** (template
pin, seeded `fs-gg-sdd-*` skills, `.fsgg/early-stage-guidance.md`, …). One command, and the whole
consumer is coherent. The intent is right — a single surface that keeps a consumer current — but
taken *literally* ("only source of truth" + "automatically updates") it collides with three
invariants this system is built on:

1. **Reproducibility / determinism.** The coherent-set model rests on pinned versions and
   byte-identical verification (the `FR-005`-family diff guarantees; CI restores under
   `RestoreLockedMode`, [ADR-0006](0006-org-shared-dotnet-build-config-and-unified-restore-locked-mode-gate.md)).
   A tool that silently self-mutates mid-invocation makes the *same command* produce *different*
   output on different days — the opposite of what the whole system guarantees.

2. **The layered coordination model** ([ADR-0001](0001-cross-repo-coordination-via-issues.md)).
   Truth is deliberately *declarative and reviewable*: the [registry](../../registry/dependencies.yml)
   owns contracts, ADRs own decisions, `scaffold-provenance.json` records what happened.
   **Publish-before-flip (`FR-007`)** depends on the registry being a *separate* artifact you flip
   *after* the feed is live. Truth that lives only inside an executable cannot be diffed in a PR,
   gated by `contract-coherence`, or flipped after publish — so the CLI cannot *be* the truth.

3. **Consumer ownership.** A scaffolded template is the developer's *own* source. The *governed*
   template pin lives in the Templates provider descriptor
   (`FS.GG.Templates` `providers/rendering.providers.yml`), while a scaffolded project keeps its
   *own* `.fsgg/providers.yml`. Silently rewriting either clobbers local edits or diverges the
   consumer from the governed set. (This is the same ownership seam
   [ADR-0005](0005-fsgg-slot-ownership-sdd-project-governance-governance.md) draws inside `.fsgg/`.)

There is also a **bootstrapping** problem: an old CLI cannot know it is old *from itself* — it must
consult an external reference (the feed / registry). So the CLI can be the single *consulter and
enforcer* of the truth; it cannot be the truth.

## Decision

The `fsgg-sdd` CLI is the **single orchestration surface and enforcement point** for coherence,
but it is **not** the source of truth, and it **never silently self-updates or silently rewrites
consumer artifacts**. Concretely:

1. **Truth stays declarative.** The current coherent set (template pin + framework + minimum
   `fsgg-sdd`) is owned by the registry and recorded in the provider descriptor (ADR-0008); what
   actually happened to a project is recorded in `scaffold-provenance.json`. The CLI **reads**
   these — it does not embed or become them.

2. **Every invocation detects drift — read-only.** On any command, `fsgg-sdd` compares (a) its own
   version against the pin's required minimum and (b) the seeded artifacts present against those
   the pin expects (via provenance + the declarative minimum). **Detection never writes.**

3. **Remediation is explicit and diff-driven — never a side effect.** A dedicated verb
   (`fsgg-sdd upgrade`, with a read-only `fsgg-sdd doctor` for reporting) performs self-update
   (`dotnet tool update`), template re-pin, and artifact re-seed (`refresh-agents`) — **each shown
   as a diff and confirmed**. No other command mutates the CLI or the consumer's artifacts as a
   side effect.

4. **Interactive warns; CI fails closed.** When behind the minimum: an interactive run prints a
   **non-fatal warning** pointing at `fsgg-sdd upgrade`; a CI / non-interactive run **exits
   non-zero** (fail-closed, consistent with the release-only gates). *Neither auto-updates.* CI
   pins the tool via the committed `.config/dotnet-tools.json`; the fail-closed check **protects**
   that pin rather than fighting it.

5. **Consumer-artifact updates respect ownership.** `upgrade` reconciles by rewriting the values
   the *consumer* owns (its `.fsgg/providers.yml`), surfaced as a reviewable diff; it does not
   reach across the ownership boundary into governed registry/provider state (a governed pin bump
   remains a PR in its owning repo).

Reframed in one line: **single orchestrator + single enforcement point — yes; single source of
truth or silent auto-update — no.**

## Consequences

- **FS.GG.SDD** (policy owner): extends [FS.GG.SDD#49](https://github.com/FS-GG/FS.GG.SDD/issues/49)
  from "warn (or fail)" to the full policy above — a read-only staleness check on every command, an
  explicit `upgrade` / `doctor` verb (self-update + re-pin + re-seed behind a confirmable diff),
  interactive-warn vs CI-fail-closed keyed off an interactivity / `--ci` signal, and provenance
  stamping of CLI-used + required-minimum. SDD#49 is retargeted onto this decision.
- **.github**: **no registry surface change** beyond ADR-0008's minimum field — this ADR constrains
  *how* that field is enforced, not *what* it is. `contract-coherence` and every CI caller keep
  pinning the tool via `.config/dotnet-tools.json`; `FR-007` publish-before-flip and the typed
  `fsgg-sdd registry validate` gate are preserved unchanged.
- **FS.GG.Templates + scaffolded consumers**: gain a **one-command, diff-reviewed** path
  (`fsgg-sdd upgrade`) to reconcile a project with its coherent set — self-update, re-pin, re-seed —
  **without ever being silently mutated**. The `minimumFsggSdd` block in
  `providers/rendering.providers.yml` (FS.GG.Templates#43) is the declarative datum the check reads.
- **Trade-off accepted:** coherence is *not* achieved by magic auto-update — a behind consumer must
  run one explicit command, or a CI failure forces the issue. This is the deliberate cost of keeping
  runs deterministic, truth reviewable/gate-able, and consumer code un-clobbered.
- **Relationship to ADR-0008:** this **refines** ADR-0008, it does not reverse it. ADR-0008 defines
  the orchestrator axis and *that* the CLI must react to a behind-CLI scaffold; ADR-0009 fixes the
  *policy* of that reaction (detect-and-remediate, not silent auto-update). ADR-0002
  composition-by-scaffold and ADR-0001's declarative-truth model are preserved.
