# Immutable knowledge records

An archive consists of `catalog.json` (`schema: fsgg.atlas/1`, sorted `records` SHA256 array)
and canonical UTF-8 JSON files named `records/<sha256>.json`. The digest covers the entire
record including its terminal newline. The bundled capture command writes canonical bytes.

Required fields:

| Field | Meaning |
| --- | --- |
| schema | `fsgg.atlas/1` |
| id | Stable lowercase project-qualified identity; e.g. `rendering/core-boundary` |
| repository | Owning `owner/repository` |
| kind | architecture, decision, contract, dependency, lesson, incident, boundary or question |
| title / summary | Brief name and curated finding, including scope and limits |
| status | recorded, disputed or question; recorded does not imply current verification |
| recordedAt | Actual UTC capture timestamp, `YYYY-MM-DDTHH:MM:SSZ` |
| tags | Up to20 short lowercase search terms |
| citations | One to16 repository/revision/path/sha256 source references; immutable40-hex Git revisions |
| supersedes | Earlier same-ID, same-owner digests explicitly replaced by this finding |
| conflictsWith | Existing record digests whose findings conflict; retain both |

Use empty arrays for absent relationships. Keep source citations even when documents are large;
Atlas stores no citation payload. Do not interpret a source digest as proof of deployment, authenticity
beyond the cited source, runtime acceptance or current authority. Questions still need the evidence
that identifies the missing knowledge. Safe public summaries cite public evidence; restricted evidence
remains in its private custody and requires a separately private archive.

Archive capture is a single-owner operation. If concurrent writes are detected, preserve the new
unindexed record and reconcile the catalog explicitly; do not discard either writer's knowledge.
A public PR must preserve every baseline digest and original byte. The Atlas CI gate checks this.

The SQLite database is generated and ignored by Git. It contains the validated corpus and FTS5 index,
not new authoritative state. Deleting it loses no committed knowledge. Live/local captures become
portable only after their archive is reviewed, committed and delivered.
