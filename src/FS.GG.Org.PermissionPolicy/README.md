# Provisional reusable-workflow permission reducer

This nonpackable F# source compares already parsed permission blocks for one
caller/callee workflow pair. It follows the grant ordering and job override in
`scripts/check-workflow-permissions.py`. A syntax adapter must preserve absent,
explicit null, duplicate and unsupported values; when a callee declares a
permission floor, this reducer refuses malformed caller input instead of
treating it as an empty grant.

This source does not fetch a callee at its pinned ref, enumerate the roster,
parse YAML, evaluate App installation grants, or replace the live Python gate.
Those boundaries and installed parity remain separate work. The independent
Python fixture in `tests/workflow-permissions/run.sh` remains the baseline.
