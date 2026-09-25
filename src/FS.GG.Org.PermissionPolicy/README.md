# Provisional reusable-workflow permission reducer

This nonpackable F# source compares permission blocks for one caller/callee
workflow pair. Its read-only YAML adapter uses the `YamlDotNet` 18.1.0 pin from
the FSC-03 scaffold #3701. It preserves absent and explicit-null blocks and
refuses duplicate keys, aliases, multiple documents and unsupported shapes.
The reducer follows the grant ordering and job override in
`scripts/check-workflow-permissions.py`; when a callee declares a permission
floor, malformed caller input is refused instead of treated as an empty grant.

`callerCall` reads one selected job and requires an exact organization
reusable-workflow `uses:` target with a callee filename and ref. `callableCallee`
requires a `workflow_call` event before returning the callee's grant; it refuses
malformed event declarations and preserves an absent grant as inheritance.
The older `caller` and `callee` entry points parse permission blocks without
providing call evidence.

This source does not fetch a callee at its pinned ref, bind returned bytes to
that ref and filename, enumerate the roster, evaluate App installation grants,
or replace the live Python gate. Those boundaries and installed parity remain
separate work. The independent Python fixture in
`tests/workflow-permissions/run.sh` remains the baseline.
