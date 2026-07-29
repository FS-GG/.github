# Coordination board schema

The FS-GG Coordination project is organization project **FS-GG/1**. Its
single-select fields are live GitHub schema, not repository files, so changes to
them require the guarded migration below. Never call `updateProjectV2Field`
directly: changing a single-select has historically recreated its options and
cleared the field on every project item.

## Repo Scope

Every `repos:` row in `registry/repos.yml` must have a same-named option. The
`cross-repo` option is the one deliberate non-roster value.

<!-- repo-scope-options:start -->
| option | source |
|---|---|
| `.github` | roster |
| `sdd` | roster |
| `rendering` | roster |
| `governance` | roster |
| `templates` | roster |
| `game` | roster |
| `audio` | roster |
| `net` | roster |
| `cross-repo` | board-only aggregate |
<!-- repo-scope-options:end -->

The pull-request gate checks this bounded table against the roster without
requiring Projects credentials. Operators additionally check the live project:

```sh
scripts/project-field-options check
```

The live form must pass after every migration. A roster addition with no board
option, an unexpected board option, an unreadable project, or a partial field
read is a failure, never an empty/clean result.

## Phase

Product-repo scope determines the product phase. `P0 Decisions` and
`P5 Versioning` remain board-wide phases rather than homes for a single product
repo; every other rostered product maps as follows.

<!-- repo-phase-map:start -->
| Repo Scope | Phase |
|---|---|
| `.github` | `P0 Decisions` |
| `rendering` | `P1 Rendering` |
| `sdd` | `P2 SDD` |
| `governance` | `P3 Governance` |
| `templates` | `P4 Templates` |
| `game` | `P6 Game` |
| `audio` | `P7 Audio` |
| `net` | `P8 Net` |
<!-- repo-phase-map:end -->

The coordination-kit protocol carries the same mapping. In particular, `net`
is `P8 Net`; it must not be filed under `P1 Rendering` merely because a caller
may eventually render network state.

The board's complete `Phase` vocabulary is a separate table. The mapping above
cannot serve as this list: `P5 Versioning` is a board-wide phase and therefore
has no Repo Scope row. The gate compares this list against the engine's closed
`Phase` union in both directions; it does not infer options from the mapping.

<!-- phase-options:start -->
| option | meaning |
|---|---|
| `P0 Decisions` | board-wide decisions |
| `P1 Rendering` | rendering work |
| `P2 SDD` | SDD work |
| `P3 Governance` | governance work |
| `P4 Templates` | template work |
| `P5 Versioning` | board-wide versioning work |
| `P6 Game` | game work |
| `P7 Audio` | audio work |
| `P8 Net` | network work |
<!-- phase-options:end -->

Operators may verify the live field with:

```sh
scripts/project-field-options check --field Phase
```

The pull-request gate uses the same check with `--schema` because CI has no
Projects credential.

## Class

How BAD an item is ([.github#1588](https://github.com/FS-GG/.github/issues/1588)) — the axis
neither `Repo Scope` (where) nor `Phase` (which product) can carry. The vocabulary is **closed**
at exactly three values.

<!-- class-options:start -->
| option | meaning |
|---|---|
| `defect` | something is broken now: a red gate, a wrong answer, a rule that fails open |
| `hardening` | nothing is broken; the change removes a way it could break |
| `decision` | a human must choose before any work is authorable |
<!-- class-options:end -->

The **authority is the item's own `Class:` body line** — ADR-0045's sentinel grammar, shared
verbatim with `Paths:` and `Blocked on:` (a line at up to three leading spaces, outside any
fenced code block), plus a `[decision]` title prefix and a `Blocked on: human/decision` sentinel
read as evidence. **This board field is a downstream PROJECTION written by `reconcile`, not a
hand-maintained input.** Editing the field on a card does not change what the item is; the next
`reconcile` overwrites it from the body. A board field nobody derives is a fourth hand-maintained
copy of a fact, which is the drift ADR-0045 refused a board field to avoid.

The pull-request gate checks this bounded table without requiring Projects credentials:

```sh
scripts/project-field-options check --field Class --schema docs/coordination/board-schema.md
```

Unlike Repo Scope there is no roster file to be the authority, so the check compares this table
against a closed three-value vocabulary hardcoded in the tool — the engine's `ItemClass` union.
Drift in either direction, an absent marker block, or an unreadable schema is a refusal, never an
empty/clean result. Operators may additionally check the live project with
`--field Class` and no `--schema`.

### Creating `Class` is not a guarded migration

Recorded so a future operator does not read `project-field-options` as refusing to help them:
creating `Class` is `createProjectV2Field` on a field that **does not yet exist**. There are no
assignments to lose, so no snapshot precondition is meaningful and `add-option` has nothing to
guard. `project-field-options` exists to fence `updateProjectV2Field`, whose historical failure
recreated a field's options and cleared the value on **every** item. The guarded
snapshot → `add-option` → restore sequence below becomes relevant to `Class` only if a **later
fourth option** is ever added — which is an ADR, not a board edit.

On a product-workspace board, create `Class` with `createProjectV2Field` **before** the first item writes a
`Class:` body line. `reconcile --apply` retains body-based `CLASS-UNSET` linting when the field is absent,
but withholds the impossible downstream projection and reports the missing field once rather than failing a
write for every classed row.

## Severity

How costly the row is, independently of what kind of work it represents. The operator decided the
closed rank order on [.github#1901](https://github.com/FS-GG/.github/issues/1901):

<!-- severity-options:start -->
| option | meaning |
|---|---|
| `Critical` | highest-cost work; ranks before every other severity |
| `High` | high-cost work |
| `Medium` | medium-cost work |
| `Low` | low-cost work |
| `Unset` | not yet triaged; ranks last and triggers `SEVERITY-UNSET` lint |
<!-- severity-options:end -->

Severity is a hand-triaged board input. It ranks above `Class`; `Unset` never promotes a row and
remains a lint error on every open, non-`Done` row until a human records an evidenced rating.
The offline schema gate checks both the exact values and their order:

```sh
scripts/project-field-options check --field Severity --schema docs/coordination/board-schema.md
```

### Live field creation and population

The live Coordination project gained `Severity` on 2026-07-29 as a new
`ProjectV2SingleSelectField`, with the five options created in the order above. First-time creation
used `createProjectV2Field`, not the guarded update path: a field that does not yet exist has no
assignments to preserve.

Creation and population are deliberately separate operations. Existing rows initially have no
assignment, which the engine renders as `Unset`; [.github#1918](https://github.com/FS-GG/.github/issues/1918)
owns the evidence-based triage pass across every open non-`Done` row. That pass must rate rows from
their own text, report unratable/exempt rows, and write no other board axis. Until it completes,
`SEVERITY-UNSET` is expected and keeps the unfinished triage visible.

After creation, any option change is a destructive field update and must use the guarded
snapshot → `add-option` → restore sequence below. Verify the live field at any time with:

```sh
scripts/project-field-options check --field Severity
```

## Guarded single-select migration

`scripts/project-field-options` is field-generic and fail-closed. Its
snapshot contains every project item id and that item's option name (or null),
the complete option metadata, the project/field identities, `totalCount`, and a
SHA-256 over the canonical payload. It refuses duplicate ids, incomplete
pagination, a changing `totalCount`, a tampered snapshot, a stale precondition,
or mutation without `--apply`.

Use this sequence from a clean, reviewed branch:

1. Prove the migration and recovery legs locally.

   ```sh
   bash tests/project-field-options/run.sh
   ```

2. Capture the live field immediately before mutation. Keep the file in a
   durable remote commit before proceeding; the precondition will refuse if
   any item or assignment changes after capture.

   ```sh
   scripts/project-field-options snapshot \
     --field 'Repo Scope' \
     --output docs/coordination/board-schema-snapshots/2026-07-22-repo-scope-before-net.json
   scripts/project-field-options verify-snapshot \
     --snapshot docs/coordination/board-schema-snapshots/2026-07-22-repo-scope-before-net.json
   git add docs/coordination/board-schema-snapshots/2026-07-22-repo-scope-before-net.json
   git commit -m 'coord: persist Repo Scope recovery snapshot'
   git push
   ```

3. Apply exactly one option addition. Existing option ids, names, colors, and
   descriptions are sent back unchanged. Whether GitHub preserves assignments
   or clears them, the tool compares every snapshotted item, repairs all
   differences in bounded batches, and re-reads the whole project. A response
   failure after mutation enters the same recovery path.

   ```sh
   scripts/project-field-options add-option \
     --field 'Repo Scope' \
     --snapshot docs/coordination/board-schema-snapshots/2026-07-22-repo-scope-before-net.json \
     --name net --color GRAY \
     --description 'FS.GG.Net — render-independent transport component (ADR-0052)' \
     --apply
   ```

4. If the process is interrupted or any final comparison fails, do not update
   other board fields. Re-run the idempotent recovery command until it reports
   that every prior assignment matches:

   ```sh
   scripts/project-field-options restore \
     --field 'Repo Scope' \
     --snapshot docs/coordination/board-schema-snapshots/2026-07-22-repo-scope-before-net.json
   ```

5. Run the live roster check, then populate new-repo items with the ordinary
   `fsgg-coord set-field --batch` path. Retain the pre-migration snapshot as the
   audit/recovery artifact; its item rows intentionally contain no issue titles
   or other private project content.

The restore verifies all prior item ids even if new cards appeared during the
operation. A disappeared prior item, missing option, dropped write, or mismatch
leaves the command red and the snapshot usable for another recovery attempt.
