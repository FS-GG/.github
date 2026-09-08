# UTEL — Prospective operational telemetry completeness

Part: Simplified baseline and v2 policy binding, V0. Owner: `FS-GG/.github`; Coordination, SDD and affected
scaffold/receiver owners adopt published contracts. Backlink: [Unified Development Roadmap
§9.8](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index).

## Outcome

Make telemetry automatic for future work admitted through repository-owned orchestration: root/child lineage,
available native usage, exact-head CI populations, delivery and later corrections reach the private host-local
SQLite store without agents hand-authoring observations. UTEL-02/03A/04A/05A source is delivered through
`9f92965a77917fe78e7d1290c4992c8ce6d4120a`; publication, adoption and operational qualification remain
separate.

## Capability boundary

Instrument real repository-owned launch, dispatch, provider, check and delivery boundaries once. Guidance alone
is not collection. Built-in `collaboration.spawn_agent` remains unsupported because no interceptable
admission/final-usage hook exists; keep that platform population explicitly incomplete, never zero or equivalent.
There is no historical session discovery, transcript scan, backfill or upload. Activation is prospective only.
One host-local SQLite writer/inbox and read-only snapshots remain the storage boundary; this plan adds no
permanent service, network database or scheduler, and telemetry never becomes delivery authority.

## Coverage contract

Keep validity, identity join, population, attribution, outcome, observer health and qualification independent.
Reconcile expected work from actual intake/dispatch and CI from native inventories. Include no-op, rejected,
failed, cancelled, retried, stopped and open work. Record stable original item, attempt, invocation and parent
identities; repository, base and head; policy digest; requested versus observed runtime/model/effort; and source
timestamps, durations and provenance. Missing billing, human, reasoning, critical-path or native child-usage facts
stay unknown. Useful tests are excluded from bureaucracy. Observation loss emits bounded diagnostics and never
changes native output, exit or delivery.

## Milestones

- [x] **UTEL-06.1 — Real observer acceptance and prospective orchestration identities.** Reproduce PR #3347's
  exact public evidence; add additive migration 5 for activation/scope, expected dispatch, invocation lineage,
  event time and reconciliation; preserve migrations 1–4. Cover root/child/follow-up, missing parent, identity
  conflict/cycle, unsupported runtime, timestamp/clock/order/late cases and old stores. Do not claim host
  completeness.
- [x] **UTEL-06.2 — Automatically observe repository-owned roots and nested workers.** Extend the packaged
  `codex-exec` launcher with machine-authored facts and inherited private context; cover child, grandchild,
  follow-up, retry, no-op, failure, cancellation, delayed usage, parent death, full inbox and writer contention.
  Preserve arguments, model/effort, worktree, stdin/stdout, permissions and exit. Keep unsupported platform-native
  calls explicit. Package helpers; source tests do not prove installation. Source evidence is the packaged CLI
  entrypoint and executable `TelemetryRuntimeApplicationTests`, including migration-5 ingestion/reconciliation with
  no caller-authored observation batches; publication, installation, activation and qualification remain pending.
- [x] **UTEL-06.3 — Discover and reconcile admitted exact-head CI population.** The actual delivery path registers
  repository, PR, base and head; independently witness first admission and retain admitted superseded heads.
  Discover nonrequired workflows, all runs/attempts/jobs/steps/check-runs, keeping unsupported causal bindings
  explicit. Use revision-safe bounded polls, continuation, pending/partial and visible rate-limit semantics with no
  model polling. Cover late runs, attempts, head movement, concurrent inventory, partial pagination,
  cancellation, late completion, correction/replay and an omitted-run incomplete control. A provider command is
  source preparation until the delivery driver invokes it automatically. Source now includes the advisory
  routine-delivery hook and executable reconciliation tests; publication, installation, activation and operational
  qualification remain pending.
- [ ] **UTEL-06.4 — Derive whole-item inputs and fail-visible budget reconciliation.** Derive population,
  attribution and interval facts from admitted runtime, CI and native outcome; accept no caller-authored verdicts.
  Merge does not close running children, and a late follow-up revises assessment. Expose this in the existing
  driver health summary without board counter or report ceremony. Preserve the more-than-10%, more-than-25%, 15
  distinct and verified-reset semantics and unknown dimensions.
- [ ] **UTEL-06.5 — Publish and qualify the coherent producer.** After 06.1–06.4 merge, select the next unoccupied
  stable minor (`0.87.0` is only a candidate), release all three members from one source through the existing saga,
  and verify gates, both feeds, normalized payload, clean installation, native SQLite and every supported
  command/helper. Resume partial publication through the saga; never repack a version.
- [ ] **UTEL-06.6 — Adopt the host, Coordination and generated workspaces.** Install/select the verified published
  engine, configure a new approved future-only private store with no existing-database import, and wire the actual
  repository-owned boundaries. SDD adopts Drivers/Kit/tool pins and publishes; Templates changes only if it owns
  affected bytes. Prove clean representative console/Fable-game creation and existing-workspace refresh; pin and
  no-clobber checks alone are not installed-byte proof.
- [ ] **UTEL-06.7 — Future-only operational qualification.** Use a new installed root, child and nested child plus
  no-op/failure/cancellation/retry; an ordinary real exact-head PR with automatic CI discovery and merge/readback;
  a controlled rerun/superseded head; and process-loss replay/late arrival without duplication or model polling.
  Negative controls keep unsupported child, missing usage, partial CI and store loss visible while delivery
  succeeds. Private structured evidence stays local; publish only allowlisted aggregates/provenance. Mark the
  supported repository-owned scope qualified while platform-native population remains incomplete until an actual
  adapter exists and passes a fresh equivalent journey.

## Cross-repository order and execution

The order is `.github` source, coherent publication, host and Coordination consumption, SDD
adoption/publication/materialization, Templates/provider changes only as needed, named receiver adoption, then the
future-only journey. Consumer preparation may overlap, but acceptance uses published bytes. Fresh-workspace
behavior changes only after an adopted release; existing repositories change only through a verified upgrade.
Keep the omitted lifecycle default `sdd`; do not activate v2 or alter protected epochs.

Execute 06.1 then 06.2. Milestone 06.3 may prepare from the identity contract; 06.4 requires both. Publication
and activation retain their real safeguards. Completion reporting must distinguish source delivered, published,
installed, activated, supported-scope operationally qualified and platform coverage incomplete.
