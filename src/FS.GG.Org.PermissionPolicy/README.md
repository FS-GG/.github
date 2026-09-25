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
satisfied result, caller/App under-grant findings, or an unproven caller-default
finding when the callee declares a floor. The supplied roster's own
completeness, workflow bytes, source ref and inventory provenance still need
independent provider authentication; this reducer covers one bound caller pair,
not a fleet sweep.

`WorkflowPermissionSyntax.callerCallJobs` enumerates organization calls from
every job in one supplied caller workflow. `PermissionFleet.evaluate` requires
an exact caller repository/workflow roster, snapshots at one source ref and one
binding fact per enumerated call before reducing the full selected fleet. It
refuses missing or duplicate snapshots or call facts, dynamic call targets,
empty call sets and per-call roster mismatches. A second rostered caller's
under-grant therefore becomes a finding instead of being hidden by a satisfied
first caller. It reuses `PermissionAggregate.evaluate` per call, so authority
App findings may appear once per caller in the returned list.

The fleet roster is supplied data. If its provider omits a repository and its
workflow snapshots together, pure code cannot discover the missing repo. The
provider must authenticate the roster's provenance and completeness, enumerate
each listed repository's workflow files, and bind those exact bytes and refs
before this reducer can support a gate verdict.
The provider handoff must name the exact `registry/repos.yml` source ref and a
verified head for each caller repository, establish the complete repository set
from that roster read, distinguish a visible
repository with no workflows from an unreadable or invisible repository, and
pair every listed `.yml`/`.yaml` file with bytes at its repository's verified
head.
A disappeared file, transport failure, or unverified roster source is no
verdict; a supplied JSON test scenario is not that evidence.

`FleetInventoryContract.evaluateSupplied` now checks an independent snapshot of
`registry/repos.yml` at the expected source ref against the supplied fleet
roster. It requires exactly one repository head and one terminal workflow
enumeration per registry identity, with listing paths equal to the fleet roster
at that head. A visible empty workflow directory is represented by a terminal
empty listing; a missing or incomplete listing refuses. The result is explicitly
`ProvisionalFleetVerdict`. This pure contract does not authenticate the
registry bytes, the heads, the visibility check behind an empty listing, or
the provider's claim that an enumeration was terminal. The current fleet
reducer also accepts only `FS-GG/*` caller identities, so the actual registry's
non-organization rows require an explicit policy decision and installed proof
before a live sweep can use this adapter.

This source does not fetch a callee at its pinned ref, enumerate the roster or
workflow files from GitHub, authenticate current installation grants, or
replace the live Python gate. Those boundaries and installed parity remain separate work. The
independent Python fixture in
`tests/workflow-permissions/run.sh` remains the baseline.
