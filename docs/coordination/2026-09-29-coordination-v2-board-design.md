# Coordination V2 board design

Use V2 boards for organization coordination and GitHub-coordinated product workspaces. Create a
fresh organization project named **Coordination V2** and product-scoped V2 boards, carrying forward
only work with a verified remaining outcome. Reuse existing repository issue and dependency identities.
The board provides planning and visibility; native delivery and the selected V2 operation authorities
remain the sources of delivery truth.

**Status:** selected design, 2026-09-29; organization pilot passed 2026-10-02. Project 3, its schema
and the three approved memberships are read back; .3 qualifies its fixed root-local refresh. A
four-target .4successor and organization inspection guidance are prepared but gated pending actual
protected artifact/import/inspection and selected consumer readback. Broader adoption and .5–.6product
publication remain pending below.
**Owner:** `.github` owns the organization planning surface and shared consumer contract; SDD and
Templates own published workspace integration, and product owners adopt their scoped boards.
Repository owners retain their deliverables and evidence. This is **COORD-BOARD-V2-01** in the
[Unified roadmap](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index)
and the [V2 execution roadmap](../github-substrate-v2-roadmap.md#coordination-v2-board--2026-09-29).

## Migration priority and execution sequence — 2026-10-02

**Next selected item: `COORD-BOARD-V2-01.4`.** The bounded organization pilot in .2 passed through
an explicit user-authorized native one-shot administration exception. The
[pilot evidence](board-v2-import-preparation.md) and [bound manifest](board-v2-import-manifest.json)
record Project 3, four fields, four filtered views, the three approved existing issues and independent
readback. A repeat emitted zero mutation intents. This selected exception supersedes the earlier
preparation's sole shared-transport route only for .2; it does not qualify the automatic writer.

The complete project has four memberships: the three selected issues and unapproved, unseeded
SDD#935, a native child of selected SDD#928. No operator add requested that fourth membership.
Child propagation is an inference, with cause/actor unproven. Keep this distinction visible during
broader population-policy adoption; neither it nor the successful pilot approves broader carryover.

Reconcile all currently open V2 roadmap outcomes before broader transfer. Include only verified
remaining work; a source merge does not finish its unpublished package or unqualified native operation.
Keep delivered outcomes and superseded V1 requirements in history. Preserve feature identities and
dependencies, group adjacent steps into reviewable outcomes, and avoid creating a row for every
checkbox, CI failure or intermediate report. Use the organization board for organization-relevant
outcomes and the selected product boards for detailed product execution.

The `.3` fixed root-local adapter passed against the actual pilot binding; see the
[restricted refresh evidence](board-v2-import-preparation.md#restricted-root-local-refresh-qualification--2026-10-02).
The hosted job remains dormant and unenrolled. Broader import and the `.4`
scheduling switch follow verified pilot membership and qualified restricted projection. Inventory and
switch the actual driving skills and consumer bindings together, preserving routine delivery,
selected technical checks and native merge readback. After that switch the board is the primary
scheduling queue; owning roadmaps retain design, sequencing and acceptance, and native evidence
remains completion authority. Board unavailability cannot stop otherwise valid source delivery.

Product integration preparation can proceed independently, but `.5` publication depends on the
qualified `.3` contract. `.6` qualifies actual published fresh and retained workspace behavior before
each product switches. Full V2 acceptance remains complete at its selected profile throughout this
follow-on migration. The delivery checklist below remains the sole milestone ledger; this priority
selection closes the verified .2 pilot and .3 root-local refresh; .4–.6 remain open.

## Bounded .4 source window — prepared, not adopted

The [successor preparation](board-v2-import-preparation.md#four-target-successor-and-consumer-adoption--source-preparation)
keeps the accepted three issues and proposes existing .github#3009 as the fourth, with exact native
identity and fresh complete empty blocked_by observations. Its architecture choice is already accepted;
the remaining organization ADR/linked design is a genuine deliverable. The
[outcome dispositions](board-v2-outcome-dispositions.md) cover the actual named programme rows while
preserving unknown native mappings and unselected later outcomes. Do not create rows per source PR,
checkbox or receipt. SDD#935 remains outside the selected queue.

The source adds read-only `board-v2 inspect` and a deterministic candidate display using current
integrator PR/touch-set/capacity facts. Human fields, observation/currentness and outcome acceptance stay
separate. The fixed Observation authorizer supports only exact original3 or proposed4 bindings and
freshly checks each selected map; source admission never supplies live credential/manifest authority.
This stateless descriptor/view work needs no new Quint protocol or scheduler. Qualified legacy/fence
behavior and dormant hosted activation remain unchanged.

Organization-only work-unified-roadmap, drive-board/normal/best and check-board guidance is gated on
actual four-target qualification and root-selected consumer invocation. Until that gate is passed, no
current live switch is claimed. After adoption that admitted scope uses inspection as its primary queue,
with owning roadmaps/intake/native evidence still authoritative. Product/local-only bindings and ordinary
work with no represented issue remain valid. Full .4 stays open until remaining approved outcomes,
imports and consumer/disposition evidence are actually recorded; .5–.6 remain separate.

## Current boundary

[ADR-0091](../adr/0091-speed-first-clean-v2-start.md) supersedes the unfinished V1 fleet-migration
sequence. Its historical rows must not become current prerequisites simply because they remain open.
The [legacy board workflow](../../.github/workflows/coord-board-reconcile.yml) is a credential-free,
manual retirement diagnostic. It neither evaluates board state nor implements a V2 projection.

The [legacy bootstrap](../../src/FS.GG.Coord.GitHub/Board.fs) includes an `exact-project1` mode pinned
to Project 1, `Coordination`, and `PVT_kwDOEYAWY84Bb08W`. Renaming a board or changing a title argument
cannot redirect that mode safely. The new project needs an explicitly selected identity, schema and
consumer route; the existing pinned mode retains its original meaning.

## Planning surface

Use built-in title, repository and assignees plus a small set of planning fields:

| Field | Meaning |
|---|---|
| Status | Backlog, Ready, In progress, Blocked or Done; scheduling intent and evidence-based completion remain distinguishable |
| Roadmap | Link to the owning outcome and its acceptance evidence |
| Track | Active delivery or explicitly selected follow-up |
| Observation | Verified, stale or unknown, with the last successful observation identified in the refresh report |

Provide active, blocked, backlog and follow-up views. Installed experiments, W6 observation and genuine
player studies belong in follow-up when selected; deferral does not mean completion. Add priority only
if an owner uses it to choose work. Do not recreate V1 Class/Kind, claim phases, leases or duplicate
completion ledgers as required project fields.

Ordinary routine work can proceed without an issue or project item. A project Status cannot grant
permission, settle an operation, close an issue or certify delivery. Readiness derives from the owning
plan and current dependencies. Unknown evidence is visible and cannot be inferred as Ready or Done.
A closed issue supplies native closure evidence, but does not by itself prove an undelivered roadmap
outcome. Human scheduling edits and machine observation fields have separate, documented owners;
refresh must not overwrite current scheduling intent with inferred lifecycle state.

## Selective carryover

Before writing, produce a bounded dry-run manifest using current issue, roadmap and delivery evidence.
Record the source issue identity, observed revision, decision, reason, remaining outcome, owning plan
and unresolved evidence. Treat legacy board metadata as a hint requiring reassessment.

| Decision | Treatment |
|---|---|
| Genuine remaining outcome | Add the existing issue to V2 and reassess scheduling and dependencies |
| Delivered outcome | Keep its evidence in the owner/history; omit from the active import |
| Superseded V1 migration or ceremony | Leave in legacy history with its actual disposition; do not restore it as Ready |
| Installed experiment or human study | Include only if selected, in the follow-up view |
| Unclear owner, evidence or remaining scope | Report for adjudication; do not silently import or close |

Deduplicate by canonical repository and issue identity. GitHub supports
[adding existing issues](https://docs.github.com/en/issues/planning-and-tracking-with-projects/managing-items-in-your-project/adding-items-to-your-project)
to projects; adding membership preserves the original issue and discussion. Do not clone issues,
automatically reopen/close them, copy obsolete blockers or convert every draft into a repository issue.
Dependencies may refer to work outside the project; preserve their native identities and report missing
readback as unknown. During verification an issue may belong to both projects without duplication.

Apply an approved manifest first to a small representative set, then to the remaining selected items.
Retries read membership before adding and retain per-item outcomes. A lost response or partial page is
an unresolved observation, not proof of absence. No bulk deletion or forced closure is part of import.

## Restricted refresh and consumer adoption

Implement refresh as a reviewed, versioned, deterministic one-shot job, manually dispatched or
scheduled under the owning repository's existing policy. It must satisfy the
[host execution boundary](../github-substrate-v2-roadmap.md#required-host-execution-boundary--2026-09-29):
no general-purpose autonomous agent on Home/Main, unrestricted CI agent, broad standing SSH/sudo,
arbitrary shell input or uploaded executable recipe.

Bind the exact organization, new project ID, supported field IDs, recipe revision and permitted
repository scope. Use narrowly scoped project-write and source-read access, serialized writers,
bounded batches, pagination, runtime and retry limits. Select trusted reviewed code independently of
untrusted PR bytes. Refuse unknown targets, field drift or missing authorization before affected writes;
never fall back to Project 1 or title discovery. Activation of credentials remains a separately
authorized operation under existing controls. Bind the actual project owner kind and identity for
product boards as well; a product workspace must never inherit the organization target implicitly.

The writer may update only selected project membership and owned projection fields. It cannot mutate
source refs, issue lifecycle, dependency authority, claims, grants or settlement journals. Retain
observed source identities, completed pages, per-item effects, gaps and cleanup results. A partial
refresh must identify the unobserved population and retain the last verified state with stale/unknown
health. Board freshness and job failure remain outside the ordinary source merge path.

Inventory consumers before adoption, including skills, CLI bootstrap modes, caches and workflows.
Introduce an explicit V2 projection route compatible with this smaller schema; do not point legacy
claim/writer adapters at it. Verify target and fields by readback, revalidate affected caches, and switch
each selected consumer deliberately. Leave the legacy writer retired. After adoption, retain the old
board as a labeled legacy reference and stop routine consumer writes to it. Preserving or
[archiving historical items](https://docs.github.com/en/issues/planning-and-tracking-with-projects/managing-items-in-your-project/archiving-items-from-your-project)
is preferable to deleting their context; any archive operation needs its own selected scope.

## Product workspace use

Product board integration follows the
[V2-LANG-01 language-independent contract](../roadmaps/2026-09-29-language-independent-workspaces-and-agent-integration.md#portable-workspace-contract).
Product language and toolchain choices remain independent of the shared adapter's runtime. Publish
standalone commands or a versioned wire interface; qualify non-.NET and mixed-language receivers
without requiring product code to import Akka or Microsoft Agent Framework. Shared tooling declares
its own runtime prerequisites. Native product checks remain component-specific and feed the existing
delivery evidence; board status or an agent/UI completion event cannot replace those checks.

GitHub-coordinated product workspaces must use the same V2 planning and projection contract through
their own selected board. Use a product-scoped project by default when board integration is selected;
an explicitly selected shared project with a repository-scoped view is also supported. Keep the
repository allowlist enforced in the adapter: a view filter alone is not a write boundary. Product
work and cross-repository coordination can reference the same native issues without copying them.
The organization board includes only selected organization-relevant outcomes, not every product task.

Product-local board commands and the materialized `work-board` guidance must resolve the selected
V2 binding and smaller schema. They must not route through Project 1, require legacy claim fields or
dispatch an organization-wide board driver from a product tree. A board offers planning candidates;
the owning roadmap, native dependencies and protected delivery still determine actual work and
completion. Routine source delivery remains possible without a project item or a healthy refresh job.

For new GitHub-coordinated workspaces, publish the shared adapter and update the owning SDD/Templates
materialization and initialization routes to select V2 board integration. Record the repository,
project owner kind, immutable project and field identities, schema version and adapter version in the
existing workspace configuration/provenance surfaces. Templates carry no access credentials or
organization-specific project ID. Initialization resolves and verifies the selected binding; it never
creates a GitHub resource or grants access without the existing operation authorization. The
[current initialization skill](../../.agents/skills/initialize-sdd-workspace/SKILL.md) remains the
source boundary until the owning implementation and publication are delivered.

Existing coordinated workspaces need a bounded adopter that checks their current binding, previews
the V2 change, preserves owner-authored configuration and refuses conflicting edits. Apply selective
carryover to their old boards too; stop their selected legacy writers only after verified adoption.
Retain old board history and unresolved operations without reviving V1 authority. Local-only and
uncoordinated workspaces remain supported without GitHub or a board; this requirement changes the
selected board route, not provider or lifecycle defaults.

Qualify one freshly generated coordinated workspace and one retained workspace against the actual
published artifacts. Verify product-local commands, native issue identity, selected-board writes,
stale/unknown display, repeat initialization/adoption and conflicting-config refusal. Include two
different product bindings and negative tests for wrong project, foreign repository and denied access;
prove neither workspace can mutate the other's board through the configured adapter. Also retain a
local-only initialization case with no GitHub dependency. Source and organization-board qualification
alone cannot close product adoption.

## Delivery and acceptance

- [x] **COORD-BOARD-V2-01.1 — Select the design.** Record the planning boundary, selective carryover,
  host restriction and implementation sequence in the Unified and V2 roadmaps.
- [x] **COORD-BOARD-V2-01.2 — Prepare the bounded import.** Inventory live consumers and candidate
  issues; define exact schema and field ownership; retain an adjudicated dry run. Create the selected
  project and apply a small approved pilot through existing operation controls. Read back unchanged
  issue identities, membership, fields and dependency references before broader import. Completed
  2026-10-02 by the selected manual administration exception: Project 3, approved SDD#928 /
  Templates#441 / .github#3010, four fields/views, independent readback and zero-write repeat.
  [Evidence](board-v2-import-preparation.md) retains delayed reads, the unapproved child membership
  and separate historical/corrected constructor provenance. Automatic projection remains .3.
- [x] **COORD-BOARD-V2-01.3 — Qualify restricted projection.** Implement the explicit V2 adapter and
  fixed job. Prove duplicate retry, lost response, incomplete pagination, stale source, wrong-project,
  field drift and denied-access behavior. Verify limited writes and truthful unknown/stale reporting;
  demonstrate that an unavailable board job cannot block valid ordinary source delivery.
  Qualified 2026-10-02 at protected3829 for the selected root-local route: three verified items,
  zero-write no-op, one acknowledged bounded Observation write, independent field readback and
  zero-write repeat. Hosted enrollment remains a separate operating join; no wider writer is enabled.
- [ ] **COORD-BOARD-V2-01.4 — Adopt and retain history.** Import the remaining approved outcomes,
  switch selected consumers with independent readback, and label the old board as legacy reference.
  Verify no automatic V1 writer resumes and no host-resident autonomous agent is required. Retain the
  actual adopted scope and unresolved items; do not report unselected consumers as migrated.
- [ ] **COORD-BOARD-V2-01.5 — Publish product workspace integration.** SDD/Templates and the shared
  adapter owner implement and publish the V2 binding, initialization, product-local commands and
  materialized guidance. Record exact coherent package identities and the selected coordinated-board
  default; preserve local-only operation and existing provider/lifecycle choices. Depends on .3's
  qualified shared contract; preparation can run alongside .2 and .4.
- [ ] **COORD-BOARD-V2-01.6 — Qualify and adopt product boards.** Prove fresh and retained published
  workspace journeys, separate product bindings and scope refusals. Selectively import each enrolled
  product's relevant issues, switch its consumers and stop its legacy projection after readback.
  Record adopted workspace/board identities and any unresolved consumers. Depends on .5; each
  product's own verified binding permits its adoption without waiting for all other products.

This feature is follow-on planning work, not a new prerequisite for the already selected full V2
acceptance profile. The host execution requirement has its own required qualification outcome;
design delivery or board adoption cannot stand in for that proof.

This design delivery changes no generated files or published packages. The planned workspace effect
is V2 board configuration and guidance for GitHub-coordinated SDD/Templates families. The organization
pilot first changes planning in .2; shared tooling follows in .3 and explicit organization adoption
in .4. Product integration is published in .5 and first changes fresh and retained receivers through
.6's qualified adoption. Public package identities and actual materialized bytes must be read back;
a project rename or producer source merge cannot qualify an existing-workspace upgrade.
