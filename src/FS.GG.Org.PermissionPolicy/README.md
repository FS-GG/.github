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

`WorkflowPermissionSyntax.appTokenSteps` walks every ordinary job step in one
supplied workflow and records its inspected-step count and candidate
`actions/create-github-app-token@` requests. It refuses malformed job/step
shapes, ambiguous or dynamic App identity inputs, duplicate or dynamic
permission inputs, and a reusable-call job with local steps. A valid
reusable-call job has no local steps; a step with `if: false` is still inspected.
`AppGrantComparison.compareWorkflow` binds each observed candidate to the
selected inventory and returns per-step verdicts. A separately custodied App
requires its matching inventory identity; a different identity refuses. The
scan result is evidence, not an aggregate gate verdict.

`PermissionAggregate.evaluate` accepts one bound caller/callee pair, an expected
source ref, and a supplied authority workflow roster with each selected job's
step count and App step positions. It requires exactly those workflow snapshots
at that ref and a matching inventory snapshot for every selected App identity.
Missing workflows or jobs, extra or duplicate steps, stale refs and missing
inventories return no verdict. Only after completeness checks does it return a
satisfied result or caller/App under-grant findings. The supplied roster's own
completeness, workflow bytes, source ref and inventory provenance still need
independent provider authentication; this reducer covers one bound caller pair,
not a fleet sweep.

This source does not fetch a callee at its pinned ref, enumerate the roster or
workflow files from GitHub, authenticate current installation grants, or
replace the live Python gate. Those boundaries and installed parity remain separate work. The
independent Python fixture in
`tests/workflow-permissions/run.sh` remains the baseline.
