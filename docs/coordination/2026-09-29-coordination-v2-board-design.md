# Coordination V2 board design

Create a fresh organization project named **Coordination V2** and carry forward only work with a
verified remaining outcome. Reuse existing repository issues, discussions and dependency identities.
The board provides planning and visibility; native delivery and the selected V2 operation authorities
remain the sources of delivery truth.

**Status:** selected design, 2026-09-29. This document does not create the project, import items,
activate a writer or switch consumers. Those outcomes remain unchecked below.
**Owner:** `.github` owns the organization planning surface and consumer coordination; repository
owners retain their deliverables and evidence. This is **COORD-BOARD-V2-01** in the
[Unified roadmap](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index)
and the [V2 execution roadmap](../github-substrate-v2-roadmap.md#coordination-v2-board--2026-09-29).

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
authorized operation under existing controls.

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

## Delivery and acceptance

- [x] **COORD-BOARD-V2-01.1 — Select the design.** Record the planning boundary, selective carryover,
  host restriction and implementation sequence in the Unified and V2 roadmaps.
- [ ] **COORD-BOARD-V2-01.2 — Prepare the bounded import.** Inventory live consumers and candidate
  issues; define exact schema and field ownership; retain an adjudicated dry run. Create the selected
  project and apply a small approved pilot through existing operation controls. Read back unchanged
  issue identities, membership, fields and dependency references before broader import.
- [ ] **COORD-BOARD-V2-01.3 — Qualify restricted projection.** Implement the explicit V2 adapter and
  fixed job. Prove duplicate retry, lost response, incomplete pagination, stale source, wrong-project,
  field drift and denied-access behavior. Verify limited writes and truthful unknown/stale reporting;
  demonstrate that an unavailable board job cannot block valid ordinary source delivery.
- [ ] **COORD-BOARD-V2-01.4 — Adopt and retain history.** Import the remaining approved outcomes,
  switch selected consumers with independent readback, and label the old board as legacy reference.
  Verify no automatic V1 writer resumes and no host-resident autonomous agent is required. Retain the
  actual adopted scope and unresolved items; do not report unselected consumers as migrated.

This feature is follow-on planning work, not a new prerequisite for the already selected full V2
acceptance profile. The host execution requirement has its own required qualification outcome;
design delivery or board adoption cannot stand in for that proof.

No generated SDD/Templates workspace family, scaffold default or published package changes with this
design. The first visible planning change is the selected pilot in .2; tooling changes arrive through
.3 and explicit adoption in .4. Producer publication is required only if the selected adapter is
distributed, with exact published identity and receiver qualification recorded at that milestone.
Existing-workspace upgrade is separately selected and never implied by a project rename.
