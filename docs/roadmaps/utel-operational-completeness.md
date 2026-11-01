# UTEL — Prospective operational telemetry completeness

Part: Simplified baseline and v2 policy binding, V0. Owner: `FS-GG/.github`; Coordination, SDD and affected
scaffold/receiver owners adopt published contracts. Backlink: [Unified Development Roadmap
§9.8](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index).

## Outcome

Make telemetry automatic for future work admitted through repository-owned orchestration: root/child lineage,
available native usage, exact-head CI populations, delivery and later corrections reach the private host-local
SQLite store without agents hand-authoring observations. UTEL-02/03A/04A/05A source was published in coherent
release `0.87.0` from `bffc370933ec4f35af2af4acf5f72b5888844365`; the host, Coordination, SDD and
named S.I.R receiver have adopted it, and the supported repository-owned scope is operationally qualified.

## Passive observation decision — 2026-10-18

Local capture and reporting proceed independently of receiver readiness, protected host-policy
inspection and stronger authority-service qualification. Capture, attribution and delivery remain
separate: tokens may be observed while programme attribution is Unknown and delivery is pending.
The selected persistent receiver is the existing dedicated Home service; its installation owner
retains access, supervision, storage and application-consistent backup/restore qualification.
Receiver access restoration and local source work proceed independently. No credential or private
topology belongs in this public record.

The first source window decouples the existing `scripts/v2-progress` diagnostic collector and F#
adapter, represents missing remote queue facts as Unknown and retains strict healthy/capture claims.
Bounded local samples survive unavailable receiver state; incomplete histories, missing baselines and
young sessions withhold period totals without inventing zero usage. This supersedes the historical
no-session-scan restriction below only for authorized bounded local observation. It does not establish
historical attribution, backfill, durable delivery or complete programme coverage. Existing published
and original-effect acceptance records keep their original scope.

The next concrete window reuses `SkillTelemetryReaders.NativeUsage` parsing and existing
adapter/journal/spool/cursor mechanisms for modern response/turn records, including unassigned partial
observations and corrections. The first legacy-counter diagnostic does not close that provider-capture
outcome. No new collector framework, store, OTEL/key configuration or stronger service dependency is
introduced. Existing uncertain publications remain under their original identities; capture observations
supply no execution authority or reservation release. Required exact-source checks and operational
receiver restart/restore readbacks remain distinct gates.

### Modern passive source candidate — 2026-10-18

The following candidate adds `telemetry passive capture --config CONFIG --repository REPOSITORY
--input PRIVATE_SELECTION` and `telemetry passive report --config CONFIG --repository REPOSITORY`.
It shares the maintained native decoder and retains partial `token_usage_record` responses as the
additive `provider-usage-observation/1` payload in the existing canonical fact ledger. Stable native
thread/turn/response identity, revisions, serialized pending intent, cursor and immutable spool retain
corrections and replay; passive facts remain excluded from qualified item totals. No item, model,
cost, complete inventory or terminal status is invented. Local capture/reporting calls neither
App Server nor the receiver. This is source preparation, not compiled, installed or operational acceptance.

The explicit mode0600 no-follow regular selection uses `fsgg.telemetry.passive-selection/1`, an absolute
`codexHome` and explicit absolute `sources`. Initial bounds are eight source cursors, 4 MiB of reads
and 24 normalized response rows per pass, a 64-event/64 KiB batch, 128 MiB source files, 1 MiB lines,
a 256-response revision index and 1 MiB private state. Existing spool bounds remain 128 ready
envelopes/8 MiB with bounded outcome retention. Partial tails defer; truncation/replacement creates a
new source generation and continuity gap. Cumulative turn snapshots never become response spend.

Finished-source retirement (2026-10-23, Outcome #4640) keeps one long-running association inside
these bounds. A source closes when a pass reads its descriptor to the end on a line boundary and the
file has been unmodified for 15 minutes. Its cursor moves to a compact retired entry and its response
identities leave the index. A later append resumes from that entry. An active Claude transcript keeps only
its newest 24 response identities, relying on the observed (undocumented) writer order in which every content
block of one API response precedes the next response; active Codex rollouts keep the original rule.

Re-emitting a retired identity would be refused by the receiver as an identity conflict and quarantine the
whole batch, so each re-read path suppresses records instead, with a visible gap:
`source-alias-skipped` for the same file under another path (same inode and continuing the owning entry's
anchored cursor, so a reused inode is not mistaken for an alias); `closed-source-discontinuity-skipped` for a
closed file that no longer continues its entry (marked, and not rescanned); `continuity-replay-suppressed`
for an active file re-read after a continuity gap, at or before its high-water timestamp;
`retired-response-suppressed` for any identity in the bounded set of the 4096 most recently retired; and
`pre-horizon-record-suppressed` for an unknown source's records at or before the eviction horizon, which is
one second after the modification time of the newest evicted entry (at most 384 entries are kept).
Records without a timestamp are suppressed wherever a timestamp bound applies. One case remains: a copy of
an old closed file under a new inode, made after its identities left the recent set but before its entry is
evicted, can repeat responses and would be quarantined at the receiver. At about 150 transcripts and
10,000 responses a day, the recent set covers roughly nine hours and the retired entries roughly two and a
half days, so that window is about two days. A source leaves the active set only when a pass closes it, so
the caller must keep selecting each active path until it closes; eight abandoned active paths (for example,
deleted while active) block new sources with `source-index-full-capture-deferred`. Retirement travels in the existing
intent, so a crash applies it once. Measured before the change, two real transcripts filled the
256-identity index in ten passes and every later pass deferred.

Spool backpressure never drops queued observations. At 128 ready envelopes or 8 MiB the local enqueue
returns `spool-overload`; the pass keeps its intent, records `enqueue-deferred-cursor-retained` and the
failure, and advances no cursor, so capture stops and resumes from the same records after a drain. Ready
envelopes are never evicted; only settled (applied, rejected or expired) outcome receipts rotate. A pass emits
at most one batch of 24 responses, while a drain sends at most one envelope of each passive chain, because a
later envelope waits behind its earliest pending predecessor. Drain must therefore run at least as often as
capture produces a batch. Observations can still be lost, visibly, when a source file disappears while
capture is held (`source-unavailable-cursor-retained`), through the suppressions above, or when a conflicting
batch is quarantined by the receiver.

Existing `activate-remote --capture-only` is an explicit initial local setup option. It retains
configuration, permission, conflict and lock checks while skipping credential presence; its result is
capture-configured/receiver-unverified. Default activation and later credential/receipt checks remain
unchanged. Local-store capture-only is unsupported in this first profile. One canonical association
spool/state root owns the producer; undeclared same-principal cloned roots cannot be detected offline,
so cloning, migration or reinitialization still requires original state/fence reconciliation. Receiver
admission refuses cross-owner changes. A complete bounded census retains each structural
scope/source/generation's earliest pending head; numeric successors wait behind uncertain or
durable-but-not-applied predecessors. Independent eligible heads and ordinary traffic rotate through
the existing sixteen-envelope quantum with one private advisory cursor of at most 256 bytes. The
cursor advances under the configuration lock immediately before each actual head attempt. Expired
shared deadline/cancellation stops new attempts, and canceled lookup cannot Submit. Missing/invalid
cursor state resets deterministic order; crashes affect order only, not queued bytes, receipts or
authority. No fixed elapsed-time or eight-pass service guarantee follows. Exact-source tests and
review remain required. Unsupported/rejected receiver data stays pending.

The existing Core, CLI and Telemetry test suites and maintained `coord-engine`, `coord-core` and
semantic skill-quality workflows qualify this source. Source inclusion is not a passed test result;
actual hosted compilation and focused assertions remain required. Coherent publication, receiver
compatibility, installed producer capture and restart/restore durability remain separate gates. This
candidate does not replace the qualified installed 0.105 board executable or its accepted binding.

### Claude Code subagent dispatch attribution — 2026-11-01 (#5171)

Passive capture attributes a selected Claude Code subagent transcript,
`<project>/<session>/subagents/agent-<id>.jsonl`, to a dispatch and a Delivery. It reads only what Claude
Code already writes: the sibling `agent-<id>.meta.json` (`toolUseId`, `spawnDepth`, `parentAgentId`), the
transcript's first record (its timestamp and the first line of the brief), and the launch result for that
`toolUseId` and agent in the spawning transcript (`resolvedModel`). It reuses the existing kinds, so the
receiver needs no change and the store needs no migration:

- `expected-dispatch`: dispatch id `sha256("claude-code-subagent-dispatch/1", session, toolUseId)` truncated to
  32 hex characters, relation `child`, runtime `claude-code-subagent`, `expectedAt` from the first record
  (`host-wall`). The parent is the session (`claude-code-session-dispatch/1`) or, for a nested spawn, the
  spawning subagent's dispatch. The activation id names the session (`claude-code-session-activation/1`); no
  activation fact is written.
- `invocation-lineage`: invocation id is the subagent's existing passive thread id, so the per-response
  `provider-usage-observation/1` facts join by `threadId`; the root invocation is the session id.
- `runtime-admission`: `requestedModel` is the launch result's `resolvedModel`; `featureId` is the item,
  `attemptId` is the dispatch id, `producerStream` is `claude-code-subagent:<role>`.

The item comes only from the brief's first line, `fsgg-dispatch: item=OWNER/REPO#N role=worker|critic|analyst|report`.
A missing or malformed marker, or an unreadable first record, writes no dispatch rows: an item-less
`diagnostic` with code `item-unattributed:<reason>` names the dispatch and invocation. Missing metadata writes
`dispatch-identity-unavailable`. A launch result not yet written leaves the admission pending; if the
transcript retires without one, a `runtime-gap` records `claude-launch-result-unavailable`. Identities and
contents are deterministic, so a re-read replays identical facts and a resumed agent (SendMessage) adds
turns to the same dispatch. A per-source status in the cursor (`dispatch`) and retired entry (`d`) keeps a
pass from re-emitting. These dispatches carry no terminal fact and their runtime is not a settled budget
runtime, so an item with them keeps its budget population open; usage stays `provider-usage-observation/1`
and outside qualified item totals.

## Capability boundary

Instrument real repository-owned launch, dispatch, provider, check and delivery boundaries once. Guidance alone
is not collection. At the original 0.87.0 baseline, built-in `collaboration.spawn_agent` was unsupported because
there was no interceptable admission/final-usage hook; that platform population remained incomplete, never zero.
At that original baseline there was no historical session discovery, transcript scan, backfill or upload.
The bounded passive-observation decision above supersedes that scan restriction at its declared scope;
complete historical attribution remains unavailable. Activation remains prospective.
One host-local SQLite writer/inbox and read-only snapshots remain the storage boundary; this plan adds no
permanent service, network database or scheduler, and telemetry never becomes delivery authority.

September 2026 extension: a read-only host proof found a native child thread, completed turn identity and final
token counters through Codex App Server metadata plus the child’s private `token_usage_record` entries. The
prospective roadmap adapter now joins these to a dispatch and reconciles revised counters. This is narrower than
intercepting every native collaboration call: it requires an instrumented dispatch, a parent `CODEX_THREAD_ID`,
one unambiguous child path, and final host records. Other native calls and missing turns remain unknown.
The collector was published in [coherent set 0.91.0](https://github.com/FS-GG/.github/releases/tag/coherent-set/v0.91.0)
and selected in both development containers. A user-authorized documentation-review test item exercised one
prospective, instrumented native child: its producer usage ledger contains one verified turn, all eight root
and child batches have retained `applied` outcomes, and the private Host item usage total matched the exact
public aggregate increase of 98,306
([readback](https://github.com/FS-GG/.github/commit/038edba5782fef9dace67f5b96b563fd2988738f)). The direct
root has unsupported native usage, so item-level coverage remains incomplete as expected. A separate,
user-authorized controlled producer test queued one process-review envelope in `fsharp-dev-2` without changing
the receiver or container network. Two installed five-minute drain runs moved it through durable receipt to
terminal `applied`; retrying the same adapter input cleared its retained intent without a duplicate fact or
receipt. Private Host and public projections matched the review fact
([readback](https://github.com/FS-GG/.github/commit/81a4d7442129f036e5144f58c4d1abef802bfdbd)). This qualifies
the installed recovery path under the controlled-test standard; a naturally pending batch has not been observed.
[UTEL-REL-01](utel-release-successor.md) records the release route; the five retired release workflows remain
disabled under that contract.

## Coverage contract

Keep validity, identity join, population, attribution, outcome, observer health and qualification independent.
Reconcile expected work from actual intake/dispatch and CI from native inventories. Include no-op, rejected,
failed, cancelled, retried, stopped and open work. Record stable original item, attempt, invocation and parent
identities; repository, base and head; policy digest; requested versus observed runtime/model/effort; and source
timestamps, durations and provenance. Missing billing, human, reasoning, critical-path or native child-usage facts
stay unknown. Useful tests are excluded from bureaucracy. Observation loss emits bounded diagnostics and never
changes native output, exit or delivery.

## Gap-closure sequencing — 2026-10-09

Use the existing actor/executor integration to close prospective dispatch lineage, lifecycle observation
and supported provider-usage gaps. The [LEARN actor-owned telemetry acceptance criteria](2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md#11-telemetry-acceptance-at-actor-owned-dispatch--2026-10-09)
are the owning integration checklist under LEARN-01.2/.4; accepted O0–O3 and LEARN-01.3 source remain
reusable foundations. This is planned integration, not evidence of installed coverage or authorization
to activate a runtime.

| Gap | Owner and sequence |
|---|---|
| Prospective parent/child lineage, lifecycle and available native usage | Coordination executor/provider and `.github` telemetry owners implement at owned dispatch; SystemAdmin qualifies the installed boundary. Unsupported built-in runtime counters remain an explicit host capability dependency. |
| CI step classification | `.github` telemetry owners maintain exact workflow/job/step attribution rules and verify classified and unmatched cases. Proceed independently of actor integration, SDD, Config and Templates. |
| `pull_request_target` coverage | `.github` CI collector owners qualify native run-to-PR/revision association and ambiguity refusals before removing the unsupported-event gap. Proceed independently of actor integration and the product lanes. |
| Critical-path calculation | Telemetry analysis owners consume qualified dependency and timing observations, then validate the calculation separately. Until then, retain unknown critical-path coverage. |
| Historical missing counters or lineage | Recover only from authentic retained evidence. New instrumentation does not establish past observations or settle unresolved historical operations. |

Keep one observation path into the existing store. Extend the maintained integration journey rather than
building a temporary second collector. This sequence assigns responsibility and dependencies; it does
not mark these gaps closed or change the supported scope of completed milestones below.

## UTEL-CI-GAPS — bounded source window

- [ ] **UTEL-CI-01 — Exact maintained classification, deterministic input and honest gaps — routine.**
  Reuse UTEL-06.3. Select the profile from the exact repository/head, reject malformed or duplicate tuples,
  consolidate literal path/job/step matching and retain the profile digest in existing diagnostics.
  Maintain audited engine and routine-eligibility entries; unmatched identities stay unknown and mixed remains
  unsplit. An unavailable profile defers immutable step facts. Profile changes apply to future candidates;
  historical correction, producer publication and receiver adoption remain separate.
- [ ] **UTEL-CI-02 — Trustworthy target-event association and bounded ambiguity refusal — routine.**
  Parse the native relation for one exact PR/head/base and consistent repository IDs, retaining run/attempt
  identity and request/time bounds. Refuse empty, multiple, missing, foreign and conflicting relations with
  explicit gaps and no attributable target timing. Source fixtures do not qualify the observed native empty
  join (run `37984850593`, attempt 1), and no workflow/run-name witness is introduced in this window.
- [ ] **UTEL-CI-03 — Integrated source journey and durable scope projection — routine.**
  Verify mixed PR/target population discovery, profile refusal/recovery and replay through the existing
  application projection and disposable store, preserving native delivery when telemetry refuses.
  Source checks and native merge readback must precede source completion. Coordination profile adoption,
  coherent producer publication, installed receiver qualification and native positive target association
  remain pending; UTEL remains open.

Local candidate preparation extends the existing reader/application tests and changes only their reserved
source/profile/documentation paths. No retained store, package version, installer, workflow or generated
workspace defaults change. The later event-derived witness design requires separate source/provenance
selection and native proof; head/branch/title heuristics are excluded.

PR [#4344](https://github.com/FS-GG/.github/pull/4344) delivered the bounded CI source at
`99715df3d2214de2c86c9ae7c49e6b0ae9ca7f3b` with 48 successful checks and three skips.
Native target run [37997746345, attempt 1](https://github.com/FS-GG/.github/actions/runs/37997746345)
contains one complete PR 4344/head/base/repository relation, unlike the retained empty-array case.
The wired reader tests exercise that sanitized native relation through both collection entrypoints;
the application test uses its literal job/step observations and exact-head profile through a disposable
store, preserving mixed classification and replay without duplicate facts. These controlled populations
do not establish whole-head inventory or installed collection. The empty-array run remains unknown;
prospective automatic-delivery qualification, producer publication and receiver adoption remain pending.
No event-derived witness is selected for the complete native-relation case, and UTEL stays open.

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
  qualification remain pending. The C3 continuation source adds durable fence/graph/seal publication,
  authenticated current-receiver readback, pending recovery before acquisition, and an aggregate native request
  budget across repository/profile discovery and continuation. Combined Core (36), GitHub (90), and CLI (158)
  controls passed, including a metered 41-suite acquisition and actual disposable authenticated receiver
  crash/restart journey. The CLI execution passed all 158 cases; its full-identity report was corrected offline
  after a reporting failure, with separately timed unchanged build-output verification. These source controls
  do not establish coherent publication, installed adoption or the actual external more-than-40-request journey.
- [x] **UTEL-06.4 — Derive whole-item inputs and fail-visible budget reconciliation.** Derive population,
  attribution and interval facts from admitted runtime, CI and native outcome; accept no caller-authored verdicts.
  Merge does not close running children, and a late follow-up revises assessment. Expose this in the existing
  driver health summary without board counter or report ceremony. Preserve the more-than-10%, more-than-25%, 15
  distinct and verified-reset semantics and unknown dimensions. Source evidence is additive migration 7,
  transactionally derived projections and executable Core/CLI/driver journeys; publication, installation,
  activation and operational qualification remain pending.
- [x] **UTEL-06.5 — Publish and qualify the coherent producer.** Coherent release `0.87.0` published
  `FS.GG.Coord.Cli`, `FS.GG.Kit` and `FS.GG.Drivers` from one source through the release saga. Both feeds,
  normalized payloads, stable-channel promotion, a clean public-feed installation, native SQLite and the supported
  commands/helpers were verified. The completed recovery resumed the immutable artifacts after feed indexing
  exceeded the original wait rather than repacking the version. Release:
  [`coherent-set/v0.87.0`](https://github.com/FS-GG/.github/releases/tag/coherent-set/v0.87.0).
- [x] **UTEL-06.6 — Adopt the host, Coordination and generated workspaces.** Install/select the verified published
  engine, configure a new approved future-only private store with no existing-database import, and wire the actual
  repository-owned boundaries. SDD adopts Drivers/Kit/tool pins and publishes; Templates changes only if it owns
  affected bytes. Prove clean representative console/Fable-game creation and existing-workspace refresh; pin and
  no-clobber checks alone are not installed-byte proof.
- [x] **UTEL-06.7 — Future-only operational qualification.** Use a new installed root, child and nested child plus
  no-op/failure/cancellation/retry; an ordinary real exact-head PR with automatic CI discovery and merge/readback;
  a controlled rerun/superseded head; and process-loss replay/late arrival without duplication or model polling.
  Negative controls keep unsupported child, missing usage, partial CI and store loss visible while delivery
  succeeds. Private structured evidence stays local; publish only allowlisted aggregates/provenance. Mark the
  supported repository-owned scope qualified while platform-native population remains incomplete until an actual
  adapter exists and passes a fresh equivalent journey.

## UTEL-06.8 — Supported CI attribution correction

- [x] **UTEL-06.8a — Preserve and supersede CI assignment in the Store — routine.** Add a schema-13
  append-only correction/evidence ledger, exact predecessor/revision/digest closure, one effective attribution,
  atomic old/new derived reconciliation and immutable native-source integrity. Source preparation does not repair
  an installed Store. Historical UTEL-06.1–06.7 completion and accepted releases remain evidence.
- [x] **UTEL-06.8b — Supported local operator correction and coherent reports — routine.** Land with .8a:
  bounded read-only `ci correction-plan`, explicit `ci correct`, separate non-counting `ci correction-history`,
  local destination selection and producer replay fencing. Qualify real SQLite rollback/restart/chain/content/
  stale/concurrent controls, exact CI closure, unrelated work preservation, outcome-only coverage and CLI-to-report
  consistency. Remote correction is explicitly unsupported without local fallback. [Source delivery](https://github.com/FS-GG/.github/pull/4262) retains the normal required checks and
  coherent validation; publication and installed correction remain the following obligations.
- [ ] **UTEL-06.8c — Publish the coherent corrected producer.** After .8a–b delivery, use the existing release
  successor route for a fresh unused coherent CLI/Kit/Drivers version and journal; verify both feeds and promotion.
- [ ] **UTEL-06.8d — Adopt and qualify installed local correction.** Verify published bytes side by side,
  disposable misattributed fixtures, retained schema-12 copy migration, backup/recovery and older-client refusal.
- [ ] **UTEL-06.8e — Correct the retained Governance PR 444 observation and verify reports.** After installed
  qualification, discover the actual retained identity and apply the sealed supported plan for GOV-423-C3,
  preserving its original wrong V2-LANG-01.2 assignment as audit evidence. Verify one effective delivery and its
  provable CI population in GOV/V2-LANG CI, item, budget and dashboard reports. Unknown historical parent usage
  remains unknown. Missing targets, remote destination or ambiguous closure fence only that dependent effect.

## Operational evidence record

The producer was published as [coherent set
0.87.0](https://github.com/FS-GG/.github/releases/tag/coherent-set/v0.87.0) from
`bffc370933ec4f35af2af4acf5f72b5888844365`. A clean public-feed installation selected the packaged CLI and
initialized a new private host-backed schema-7 WAL store. Qualification exercised concurrent publishers,
single-writer contention, read-during-write, replay after process loss, late arrival, launch failure, cancellation,
no-op completion and public aggregate export. The store remained ready with an empty pending inbox. The test did
not inspect or import historical runtime sessions.

[Coordination PR #338](https://github.com/FS-GG/FS.GG.Coordination/pull/338) installed the published tool and
repository-owned runtime/delivery boundaries. Its telemetry-enabled routine delivery bound exact candidate
`a277938c3c1817b9552bb6a8ecd0953901327534` to merge
`62520d94ae7581a2b4b9dc1c36e7b87ecbf23282`. Automatic CI reconciliation retained three run attempts, twenty
jobs, 150 steps and one external check. Inventory, attempt, job, terminal, timestamp, lineage and check coverage
were complete. Classification remained unknown under the deliberately empty repository profile; dynamic event
causality and external-check attribution remained unsupported. The item correctly stayed whole-item incomplete
because it declared no prospective runtime dispatch. No missing fact became zero, a pass or a breach.

[SDD 1.6.0](https://github.com/FS-GG/FS.GG.SDD/releases/tag/v1.6.0) adopted the producer contracts and was
published from `8d648c8deaf1edc16b942d0cfccee722c3a0a24c`. The retained package candidate was reused after an initial
nuget.org indexing delay; both feeds compared equal, clean public installation passed, and public-package Quint
Q2/Q3 passed. A disposable existing-manifest scaffold reported `merged`, retained its Fable co-tenant and selected
exact SDD 1.6.0 and Coordination 0.87.0 entries. S.I.R is the named existing-workspace receiver for the final
bounded generator-owned refresh. [S.I.R PR #390](https://github.com/EHotwagner/S.I.R./pull/390) transferred the
verified generator-owned surface, retired only matching prior-owned bytes, retained historical readiness and
publication evidence, and preserved non-driver co-tenants. Candidate
`adc590433642f4d6422b17197deb1a992c51ef24` merged as
`80e1ac9328865ec8d1ee3eeea130560ef22b1b01`. Exact-head CI passed the receiver's integrity, mutation,
cross-runtime, browser, documentation and feedback-budget verdicts. One retained first-attempt product benchmark
outlier passed on the single bounded unchanged-head retry; no threshold or delivery bypass changed.

The future-only installed journey exercised a real root with native usage, a controlled root-child-grandchild
lineage, no-op, launch failure, cancellation, process loss, late replay and concurrent publish/read behavior. The
ordinary Coordination delivery above supplied exact-head CI admission, all run attempts, jobs, steps, external
checks and merge readback; earlier admitted superseded attempts remained distinct. Public export contained only
the allowlisted aggregate. The retained operational store reports schema 7, WAL mode, `ready` and zero pending
batches. These observations qualify repository-owned `codex-exec` and routine-delivery boundaries. They predate
the prospective, instrumented `collaboration.spawn_agent` adapter described above and do not establish its live
Host or public usage coverage.

## Cross-repository order and execution

The order is `.github` source, coherent publication, host and Coordination consumption, SDD
adoption/publication/materialization, Templates/provider changes only as needed, named receiver adoption, then the
future-only journey. Consumer preparation may overlap, but acceptance uses published bytes. Fresh-workspace
behavior changes only after an adopted release; existing repositories change only through a verified upgrade.
Keep the omitted lifecycle default `sdd`; do not activate v2 or alter protected epochs.

Execute 06.1 then 06.2. Milestone 06.3 may prepare from the identity contract; 06.4 requires both. Publication
and activation retain their real safeguards. Completion reporting must distinguish source delivered, published,
installed, activated, supported-scope operationally qualified and platform coverage incomplete.

## 2026-10-07 operational recovery source window

The same UTEL owner extends coherent0.99.1 after canonical0.99 pin and publication
projection landed. The source repairs the retained dispatch reader/writer bound and
prevalidates assembled observation batches before durable publication intent. A closed,
exact local pre-IO parser rejection permits only the existing rollback transition;
unknown/transport/foreign outcomes retain custody and exact retries. The root's malformed
nested evidence error and its retained pending publication remain an independent hold.
The supported collection/recovery recipe is in the [local-store reference](../reference/local-telemetry-store.md#operational-collection-and-malformed-observation-recovery).

Static source checks do not qualify final compiled source, canonical generators, publication,
installed engine selection, schema compatibility or receiver recovery. Existing0.99 bytes
remain immutable; the initial0.99.1 preparation is superseded by the published0.100.0
source recorded below. Keep separate original3e8 positive/red
receipts and the incomplete original130s window. No state edit, replay, migration,
activation, provider operation or fleet/default change is selected by source preparation.

Root separately admits compiled resource controls and the exact-head source qualification,
then the existing coherent candidate/preflight/publisher/both-feed sequence and explicit
operator installation. Retained poison recovery requires disposable fixture proof and an
original-identity/accepted-effects check before a single admitted supported retry. Then prove
one useful fresh begin/start/activity/terminal/usage/review and exact-head CI delivery window.
Current readiness, zero observation rows and historical unknown baselines establish neither
health nor savings. Resolve the user's selected CI population before bounded reconciliation;
report each metric's exact source window/cutoff/receipt/coverage and retain missing history.
This operational window and §9.8 stay open through actual collection/receiver acceptance.


## Current-only receipt and recovery boundary — 2026-10-08

Current supported local/remote workspace submit uses one exact JSON receipt ABI. The adapter retains pending input and sequence on malformed, foreign, transport or receiver error; an `invalid-request` substring is not no-publication-IO proof. Population-only binding requires applied rather than durable receipt. Current store/readers select14 with explicit authentic13 maintenance. The installed0.99.0 original root and pending operation remain held; source edits, pending-free backup fixtures and synthetic1MiB framing do not establish original-state recovery or observed usage. Qualification, publication, installed provenance and separately admitted original-identity receiver-effects inspection remain necessary.

## Exact host installation source and observed-state reconciliation — 2026-10-08

Coherent `0.100.0` is already [published](https://github.com/FS-GG/.github/releases/tag/coherent-set/v0.100.0)
from `3ed8ad419a64253ca6f665e9779e5e4d110f50a5`; publication is not another prerequisite run.
The preceding prepared-`0.99.1` source window is historical. Campaign `unified-roadmap-20261003`,
original item `telemetry-dispatch-state-source-repair`, and original root/reader custody continue.
Read-only host metadata now shows both version directories and a selector targeting `0.100.0`.
Those paths do not establish installation origin, verified payload, process termination or receiver
acceptance. Do not reinstall, reset the selector or replay the original reader to reconcile them.

- [x] **UTEL-100-HOST-01 — Exact installer source preparation — routine.** The durable source is
  [`scripts/update-fsgg-coord-cli`](../../scripts/update-fsgg-coord-cli) and its adjacent Python helper.
  It succeeds the existing 3,359-byte host script (SHA-256
  `4646925112f98b46cd86b5086157e37138b3938ff515a4993f50cc85bbc3c0b5`). No managed predecessor
  was found in the bounded source-location search; the installed helper is not patched by source delivery.
  The explicit `--version 0.100.0 --install-only --manifest PATH --archive PATH` recipe uses retained,
  pinned promoted-release/public-package bytes, the ordinary supported installer, isolated caches and a
  public-only NuGet config. Child HOME is disposable; [documented SDK first-use controls](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables)
  disable development-certificate generation and global-tool PATH changes. MSBuild server/node reuse
  is disabled for any restore path that uses it; this recipe invokes no compilation command.
  It keeps the existing lock inode, refuses an existing destination, verifies
  every packaged tool before and after the absolute version-only command, classifies generated files
  as observed SDK output, and witnesses selector equality. A dangling selector already targeting the
  new destination refuses because filling that directory could implicitly activate it. Partial installs
  and private command logs are retained; failure or reporting does not replace the first cause.
  Disposable tests exercise the source main entry and actual shell wrapper with fake installer/version
  commands, including the retained public archive. All 18 focused cases passed; routine eligibility
  and operation-boundary fixtures also passed. They establish source behavior only. Deployment must
  copy both source files together beside each other at the existing updater location; deployment and
  native execution are separate operator-owned effects. No latest query or activation mode is provided.
- [ ] **UTEL-100-HOST-02 — Original installed-state reconciliation and native version-only acceptance.**
  Parent/original owner must resolve the already observed installation/selector against original effect
  identities. Accept existing evidence when sufficient; otherwise name the missing payload/runtime or
  terminal/cleanup fact before selecting one bounded operation. SDK-origin investigation remains paused.
  The installer bounds retained child streams and elapsed work and records direct-child termination/EOF.
  It inherits the operation owner's runtime resource policy rather than imposing virtual-memory,
  process-wide file-size or GC limits. Stream limits are not filesystem quotas. An unreaped direct
  child pins the group number for owned cleanup; a previously reaped leader with open pipes records
  an unsupported group-custody boundary and is never signalled by stale numeric PGID. Neither result
  qualifies whole-process-tree cleanup or SDK authenticity from fixtures.
- [ ] **UTEL-100-HOST-03 — Separate selector and receiver recovery.** Decide whether selector action is
  needed only after original-effect reconciliation. Retained-store maintenance, supported original-intent
  observation/recovery and a useful prospective telemetry journey remain pending. Current14/authentic13
  boundaries stay in force. Source preparation selects no migration, retry, reader or store operation.

No generated-workspace, SDD/Templates, provider or lifecycle-default change is part of this host source
window. Fifteen prior synthetic adoption cases retain their original scope; no fixture permit or SDK
reconstruction gate becomes user authority. Historical usage and missing native dispatch collection
remain unknown, and no programme efficiency or installed-adoption claim follows from these tests.

A later, separately owned version-only observation executed the verified published entry DLL
with the already available runtime `10.0.12`: exit 0, exact `0.100.0.0` stdout with newline,
empty stderr, direct terminal and complete pipes, with the selector unchanged. This qualifies
that narrow call only; installation/shim provenance, whole custody and receiver acceptance
remain open. The predecessor CoreCLR initialization failure remains retained. The installer
source fixture results do not supply this native evidence or widen its scope.
