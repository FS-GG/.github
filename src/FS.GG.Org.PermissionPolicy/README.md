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

`PermissionEvidenceBinding.bind` checks supplied identities before later policy
work: the caller's observed repository must equal the expected repository and
appear in the authority roster; the callee read must name the exact workflow
path and ref from `uses:`; only `@main` may use a working-tree read. A pinned App
grant inventory must be present under the expected identity. The binder returns
bound facts rather than a permission verdict. Provider code still has to obtain
and authenticate the caller, roster, callee and App facts and enumerate App-token
requests; these records alone do not prove those reads happened.

`AppGrantComparison.compare` checks one supplied, static App-token request
against the bound inventory. An observed step with no `permission-*` inputs is
`Some { Requested = Absent }`; `None` means the request extraction fact is
missing and refuses. Explicit null, dynamic values, duplicate scopes and wrong
App identities refuse. Requested write above an installation's read grant is
an under-grant finding; a narrower request passes.

This source does not fetch a callee at its pinned ref, enumerate the roster or
App-token steps, authenticate current installation grants, or replace the live
Python gate. Those boundaries and installed parity remain separate work. The
independent Python fixture in
`tests/workflow-permissions/run.sh` remains the baseline.
