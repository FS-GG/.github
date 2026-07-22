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

## Guarded single-select migration

`scripts/project-field-options` is intentionally specific and fail-closed. Its
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
     --snapshot docs/coordination/board-schema-snapshots/2026-07-22-repo-scope-before-net.json
   ```

5. Run the live roster check, then populate new-repo items with the ordinary
   `fsgg-coord set-field --batch` path. Retain the pre-migration snapshot as the
   audit/recovery artifact; its item rows intentionally contain no issue titles
   or other private project content.

The restore verifies all prior item ids even if new cards appeared during the
operation. A disappeared prior item, missing option, dropped write, or mismatch
leaves the command red and the snapshot usable for another recovery attempt.
