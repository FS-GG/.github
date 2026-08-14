# Compact evidence and CI artifact retention

Git stores decisions and content addresses; CI stores bulky execution output. New TRX files, long logs,
coverage trees, and generated run reports are uploaded once with `actions/upload-artifact`, then represented
in Git by a schema-v1 manifest produced by `scripts/evidence-manifest.py`. Each row binds the artifact name,
byte count, SHA-256, immutable run URL, and expiry. The manifest also binds the exact source SHA and
a local reproduction command.

The default retention period is 90 days. Verification fails closed after expiry and tells the operator to
rerun the recorded command and replace the manifest. `--allow-expired` exists only for historical inspection;
it does not make an expired artifact current. A malformed hash, non-GitHub URL, duplicate name, missing field,
or timezone-free timestamp also fails closed. `verify` requires a local `--artifact NAME=PATH` binding for
every row, so validating caller-authored metadata without observing all retained bytes is impossible. Consumers
download the named artifact and compare its SHA-256;
the URL locates bytes, while the digest establishes identity.

Historical checked-in evidence is not deleted in M5. The 29 existing TRX files (27,532,297 bytes at
`7a9b8742f259af4d82b9562bfbe8e40d963b0be7`) remain recoverable while new runs use manifests. M6 may migrate
them only after uploading the exact bytes and committing a hash/URL manifest, or retain them when no durable
artifact can be established. Rollback is to keep or restore the checked-in output and remove its ignore rule;
no manifest is accepted as a replacement unless `verify` passes.

Material coordination policy remains authored in the typed neutral sources named by
`scripts/generate-projections` (principally `Protocol.fs` and the registries). Runtime skill files are generated
projections, and `scripts/skill-view check --source .agents/skills --tree .` proves the two runtime views expose
the same bytes. M5 does not replace those product-specific sources or their generation gate. The CI subjects
consolidated here live in `policy/subjects.json` and are executed by one runner and one workflow. Adding another
subject in this family changes the inventory instead of adding another entry-point workflow.

The `policy` workflow demonstrates the operational retention path: it captures the runner's potentially long
output, creates its content-addressed manifest, and uploads both with `retention-days: 90`. The workflow run URL
is stable; integrity comes from verifying the downloaded payload against the committed or accompanying digest.

Verification commands:

```sh
bash tests/evidence-manifest/run.sh
python3 scripts/policy-runner.py run all
bash scripts/skill-view check --source .agents/skills --tree .
```
