# SKILL-FS-01 — Replace skill Python with packaged F# commands

Backlink: [Unified Development Roadmap section 9.8](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index)

Owner: `.github` tool and skill integrator, with selected receiver owners. Stage: independent source and
receiver track beside V2. Status: .1–.6 complete at the selected public SDD clean and retained
receiver boundary on 2026-09-28. Other materializers require their own declared adoption; no fleet or
private telemetry activation is inferred.
All milestones use the routine route through [work-roadmap](../../.agents/skills/work-roadmap/SKILL.md).
This plan grants no publication, telemetry activation or production-write authority.

## Outcome and observed inventory

Move all Python implementation logic in this repository's skill trees into typed F# commands. At the
reviewed source head `edd7e973b0b153c6975e5f3851c353de192e86b8`, there are four distinct Python files,
with eight physical copies across the tracked `.agents/skills` and `.claude/skills` trees:

| Path within each skill root | Current responsibility | F# replacement boundary |
|---|---|---|
| `work-roadmap/scripts/fsgg_telemetry_defaults.py` | Host/workspace configuration, canonical repository identity and credential selection | Typed configuration and identity validation shared by telemetry commands and callers |
| `work-roadmap/scripts/native_collaboration_usage.py` | Native agent lineage and usage extraction, including unavailable/unsupported outcomes | Bounded native-usage reader with explicit unknown coverage |
| `work-roadmap/scripts/roadmap-telemetry.py` | Dispatch population, state, lifecycle submissions, private publication and usage reconciliation | Packaged adapter over the existing typed telemetry engine |
| `pipeline-preflight/scripts/preflight.py` | Advisory cost assessment and literal workflow dependency-graph checks | Packaged `assess` and `graph` commands with existing advisory and refusal boundaries |

This is a skill-root inventory, not an inventory of all repository Python tools. Caller migration includes
[the wrapper](../../tools/roadmap-telemetry.py), direct configuration imports in
[routine delivery](../../tools/routine-delivery.py) and [the dashboard](../../tools/telemetry-dashboard.py),
skill instructions and tests. Their unrelated Python behavior stays outside this part. Recount both roots
and search the complete tracked tree and staged packages before closure; new implementations cannot escape
by retaining this four-file count.

The [manifest generator](../../scripts/generate-driver-manifest) currently classifies `work-roadmap` as
`FS.GG.Drivers` and `pipeline-preflight` as `neither` (repository-native, pending measured pilot adoption).
Its package declarations read `.claude` paths. Both roots are tracked; neither entire tree is generated from
`.agents`. Keep their equivalent bytes synchronized, regenerate declared projections/manifests with the
existing generators, and verify parity. [skill-view](../../scripts/skill-view) generates receiver views and
refuses to overwrite tracked roots. Shipping a new preflight command does not add the preflight skill to a
package or broaden its distribution.

Reuse the existing [CLI telemetry application](../../src/FS.GG.Coord.Cli/TelemetryApplication.fs),
workspace submission, store, runtime and CI modules. These prove existing source capability, not that the
Python adapter has already been replaced. Preserve the fixtures in
[telemetry adapter tests](../../tests/skill-quality/roadmap-telemetry.py),
[native usage tests](../../tests/skill-quality/native-collaboration-usage.py) and
[preflight tests](../../tests/pipeline-preflight/run.py).
The [earlier telemetry design](../reports/2026-09-04-fsharp-roadmap-telemetry-and-projection-automation-design.md#7-migration-plan)
named four different legacy helpers, now absent from the skill roots. Reuse its parity and
publish-before-flip principles without restoring removed routine phase or receipt obligations.

## First executable window

- [x] **SKILL-FS-01.1 — Freeze contracts and migration seams — routine.** Depends on: none.
  Scope: fixture corpus, command inventory and caller/receiver map in `.github`. Record all current commands,
  flags, defaults, exit codes, stdout/stderr shapes and contracted bytes, including `population-only`,
  `ci-assignment`, `review`, `activity`, `usage-attribution` and `complication`. Inventory skill references,
  imports, tests, manifests and staged artifacts, plus exact installed receiver pins. Distinguish Python
  import APIs from process APIs; define a narrow packaged configuration interface for callers without
  printing credentials. Freeze shared typed interfaces and state compatibility before parallel composition.
  Acceptance: every existing positive fixture and refusal mutation maps to a command or caller; no live
  private submissions are needed for the differential corpus. Unknown installed pins remain unknown.

- [x] **SKILL-FS-01.2 — Typed configuration and native usage readers — routine.** Depends on: .1.
  Scope: new typed reader modules and dedicated fixtures, behind the existing tool package.
  Acceptance: differential tests preserve repository/credential validation, configuration precedence,
  bounded parsing and timeouts, read-only GitHub lookups, lineage, deduplication and explicit unsupported or
  unavailable coverage. Fixtures exclude private conversation content. Missing usage never becomes zero or
  measured usage. The Python entry points remain the production oracle during source qualification.

- [x] **SKILL-FS-01.3 — Packaged telemetry adapter and caller interface — routine.** Depends on: .1;
  final composition consumes .2. Scope: new adapter modules and tests; integrator owns CLI registration.
  Acceptance: full frozen command corpus passes with contracted success bytes and equivalent refusal/exit
  behavior. Preserve durable dispatch tokens, identity/digest construction, sequence counters, pending
  batches and exact retry bytes, terminal/retry semantics, protected original-item binding and advisory
  dashboard refresh. Prove restart after interruption and ambiguous submission, duplicate retry, delayed
  native usage and a retained Python-created state directory. Unknown publication stays pending; migration
  cannot resend with a new identity or discard a pending batch. Define rollback or refuse incompatible
  state before writes. No new activation or submission authority is inferred from a successful conversion.

- [x] **SKILL-FS-01.4 — Packaged preflight commands — routine.** Depends on: .1; independent of .2/.3.
  Scope: new preflight modules and dedicated fixtures; integrator owns CLI/package registration.
  Acceptance: preserve advisory estimate outcomes, unknown inputs, finite-number checks and explicit rates.
  `graph` checks actual YAML literal `needs` ordering against independent requirements. Preserve the 2 MiB
  input and 256-job bounds, duplicate-key/dependency refusal, unknown jobs, cycles and unsupported expressions
  or shapes. Qualify the selected .NET YAML parser against the frozen corpus. It does not evaluate job
  conditions, job success, reusable-workflow internals or general GitHub expressions. Existing good/bad
  launch controls, malformed input, missing runtime/parser and timeout cases demonstrate that an unknown or
  failed check cannot launch expensive work. Required gates remain required.

The first ready assignment is .1. After its interface freeze, .2 and .4 can run concurrently; .3 can build
against the frozen interface while .2 proceeds, but its acceptance waits for .2. A mismatch in current source,
state schema or receiver support revises the affected contract before the dependent work continues.

The .1–.4 source candidate passes the frozen reader differential, adapter, CLI process and preflight suites,
including retained-state, executable lookup, strict identity, dashboard and durable-write controls. The
production CLI builds with zero warnings. These checkboxes record the source window at merge; coherent
publication, receiver adoption and Python retirement remain .5–.6 work.

## Parallel ownership and qualification

Use one Sol-medium worker and isolated worktree per admitted lane. The parent integrator assigns accountable
worker identities before dispatch. The proposed disjoint source allocation after .1 is:

| Lane | Exclusive files to create or change | Join |
|---|---|---|
| Readers (.2) | New `src/FS.GG.Coord.Cli/SkillTelemetryReaders.fs`/`.fsi`; `tests/skill-fsharp/readers/**` | .3 consumes the frozen interface |
| Adapter (.3) | New `src/FS.GG.Coord.Cli/SkillTelemetryAdapter.fs`/`.fsi`; `tests/skill-fsharp/adapter/**` | .2 parity before composed acceptance |
| Preflight (.4) | New `src/FS.GG.Coord.Cli/SkillPreflight.fs`/`.fsi`; `tests/skill-fsharp/preflight/**` | Independent source/parity, then package join |
| Parent integrator | Existing CLI dispatch and project files, dependency pins, test runners, workflows, tools, both skill roots, manifests, roadmap and release files | Registers all modules and runs composed qualification |

These filenames are the initial allocation, not a requirement to keep an oversized module. Narrow changes to
existing engine modules or shared tests go through the integrator; workers stop or rescope before touching
another lane. Source lanes use local prepared commits until admitted. Apply the
[Unified driver queue controls](../../.agents/skills/work-unified-roadmap/SKILL.md): stable campaign/chain
identities, one open delivery PR per dependency chain, at most two newly qualifying managed PRs per repository,
live open-PR/check-queue inspection, and `tools/pr-lane-admission.py` for integrator-created PRs. A ready third
lane can prepare local commits; it does not open another PR. Existing PR repair stays in that PR.

Choose proportionate preflight through the [pipeline skill](../../.agents/skills/pipeline-preflight/SKILL.md).
A graph pass grants no reuse, cancellation, merge or publication authority. Preserve
[ADR-0084](../adr/0084-semantic-reuse-never-cancels-coherent-validation.md), including exact-head reuse,
pending/disputed distinctions, coherent validation and dependent activation fencing.

## Later publication, adoption and retirement

**SKILL-FS-01.5 — Publish capability, then switch selected receivers.** Expand this window after .2–.4
pass and .1 identifies receiver support. The `.github` integrator uses the existing
[publishing route](../../.agents/skills/publishing-and-deployment/SKILL.md) for the coherent
`FS.GG.Coord.Cli`/Kit/Drivers set: accepted source, release saga, exact bytes on GitHub Packages then nuget.org,
and exact pinned installed-tool proof. Publication authorization is checked at that effect. An unavailable
publication prerequisite blocks publication and dependent adoption, not earlier source qualification.

The first bounded .5 source window is complete in the source change that adds
`skill telemetry-config discover [--config PATH]`. It reuses the typed reader and exposes only the bounded
configuration-discovery projection frozen in the contract. Focused process tests cover host and workspace
results, explicit/environment/default precedence, absent/default versus explicit-missing behavior, malformed,
oversized, insecure and symlink configs, workspace binding refusal with secret-bearing child stderr, response
bounds, and absence of state writes or credential-wrapper invocation. At that source boundary it changed
no caller, package version, published artifact, installed receiver, skill root or retirement state.

The additive discovery source subsequently merged in
[PR #3918](https://github.com/FS-GG/.github/pull/3918). The coherent
[0.92.0 release](https://github.com/FS-GG/.github/releases/tag/coherent-set/v0.92.0) published Kit,
Drivers and Coord.Cli from `eb0f7318` to GitHub Packages and nuget.org; the
[registry update](https://github.com/FS-GG/.github/pull/3926) records exact public pins. The first
[SDD receiver update](https://github.com/FS-GG/FS.GG.SDD/pull/1075) uses Coord.Cli 0.92.0 for clean
scaffolds and preserves byte-level refusal for retained 0.91.5 manifests. Its Kit and Drivers pins remain
0.91.5, so it does not yet execute replacement skill callers from a changed Drivers package. These were
the earlier .5 preparation boundaries, before the caller switch and retirement below.

Use an additive capability release before changing callers that would otherwise run against an old tool.
Then land the skill/tool caller switch, publish the changed Drivers/coherent set, and qualify installed
receivers against that exact set. A local candidate package can prepare this proof but cannot establish
published adoption. Name exact release identities when selected; do not invent versions now. Temporary old
path launchers are argument translation and `exec` only, retained only for identified supported callers,
for at most one measured coherent release. Select the retirement release and state rollback boundary before
switching. Older immutable releases stay available; they are not evidence that the new package lacks Python.

**SKILL-FS-01.6 — Retire Python and close against native evidence.** Depends on .5's selected receiver
and retained-state evidence. Delete the four implementations and matching copies, replace the wrapper,
remove imports and retire any bounded compatibility launchers. Migrate import-based tests to the frozen
process/typed contracts so deleting the oracle does not silently delete coverage. Add an executable absence
gate over both skill roots and their entries in new staged packages, plus a stale-reference check over supported callers,
manifests and tests. An injected Python skill script or live reference to a removed script must fail;
historical documentation and fixture labels are not executable callers. Publish changed artifacts and prove
clean and retained receivers again before claiming absence in the delivered set.

### Selected .5–.6 closure

The F# caller switch and Python retirement source merged in
[.github #3941](https://github.com/FS-GG/.github/pull/3941) at `f38a0ec1`.
The coherent [0.94.0 release](https://github.com/FS-GG/.github/releases/tag/coherent-set/v0.94.0)
published the replacement Coord.Cli, Kit and Drivers; the canonical receiver pin was reconciled by
[.github #3954](https://github.com/FS-GG/.github/pull/3954).

The selected SDD receiver merged in [SDD #1083](https://github.com/FS-GG/FS.GG.SDD/pull/1083)
at `0c26ac591e76d2839177da823b3f6ada5c09a698`; immutable `v2.0.3` resolves to that source.
Retained candidate [36475182052](https://github.com/FS-GG/FS.GG.SDD/actions/runs/36475182052)
passed. Publication [36475881724](https://github.com/FS-GG/FS.GG.SDD/actions/runs/36475881724)
pushed the original archives and compared both feeds before its clean install raced public indexing.
Supported readback-only [recovery 36478312479](https://github.com/FS-GG/FS.GG.SDD/actions/runs/36478312479)
passed; no repack, retag or repeated package push occurred. Independent archive inspection found equal
normalized payload bytes and source metadata on both feeds for Artifacts and CLI.

The receiver owner observed a fresh detached-main public-package qualification with no target-command
override: `SKILL-FS SDD clean + retained receiver qualification (public package): PASS`. Its clean
path used public SDD 2.0.3, Coord.Cli 0.94.0 and Templates 0.15.0, checked both skill-root mirrors,
retired-helper absence and replacement invocation. Its retained 2.0.2 path proved guarded retirement,
interruption/resume, idempotence, user/co-tenant preservation and upgraded body equality. The installed
0.94.0 adapter also replayed exact Python-created schema-v1 pending state. The local evidence record
SHA-256 is `5de581260ebf9bda813b6727f29e54573cdd0b2145d5311d90ffc75b9e29aebc`.

This closes .5, .6 and SKILL-FS-01 at the selected SDD receiver boundary. Wider unqualified materializers
remain separate adoption work. Telemetry discovery returned not-configured; usage, completeness and
efficiency remain unknown. The [owning receiver plan](https://github.com/FS-GG/FS.GG.SDD/blob/0c26ac591e76d2839177da823b3f6ada5c09a698/docs/roadmaps/skill-fs-receiver-qualification.md)
retains its native acceptance contract.

## Generated-workspace impact and observation

Under [Unified §9.9](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#99-when-new-workspaces-change),
the affected families are receivers that actually materialize Drivers' `work-roadmap` and related caller
skills, plus this repository's preflight users. Before adoption they invoke Python skill paths; after .5
adoption they invoke the exact installed F# tool. The preflight skill remains repository-native unless a
separate distribution decision is made. No provider, lifecycle default, v2 activation or telemetry enrollment
changes; the replacement follows the receiver's existing selected skill route.

.5 is the first milestone that changes enabled receiver behavior or fresh workspace contents. For SDD
families, inspect the actual embedded Drivers pin, adopt and publish the SDD receiver, then prove clean
creation through that installed SDD release; other Kit/Drivers materializers use their declared pin/adoption
path. Name the selected family and exact producer/scaffold/tool identities in .5 evidence. Clean creation
must materialize both visible skill roots and execute the replacement successfully without Python skill
helpers. Existing workspaces require a separate supported upgrade: preserve user-owned files, detect retained
old instructions/pins, replay pending telemetry state and prove recovery on interruption. Publication alone
does not overwrite retained owner-sourced skills. Unqualified families remain explicitly pending.

Observe this work with the existing roadmap adapter until its qualified replacement takes over. Preserve
feature/item/original-item/attempt lineage and native usage/CI observations under Unified §7.4; use the same
state through the switch. Missing host configuration, attribution or usage stays an attributed coverage gap,
not a compliance result or delivery blocker. No new collector or service is required by this plan.

## Feature exit

Closure requires all frozen positive and negative contracts to pass, privacy and authority boundaries to
hold, required native checks and package/mirror verification to pass, and no Python implementation or stale
executable reference in either skill root or their entries in the qualified delivered package set. Record protected-main
source readback, both-feed byte identity, exact receiver pins, clean creation, separate retained upgrade/state
proof and compatibility retirement. Source delivery, publication and installed adoption are separate results.
Completion of .1–.4 is only the source window; it does not close SKILL-FS-01.
