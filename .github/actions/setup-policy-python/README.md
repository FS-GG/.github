# Policy Python bootstrap

This composite action owns the common Python bootstrap for policy checks that parse YAML. It pins
the runtime action in one place and pins PyYAML to an exact version, so dependency updates produce
one reviewable diff instead of changes across many workflows.

Inventory at introduction (`.github#1406`):

| Surface | Count | Disposition |
|---|---:|---|
| Workflow files | 67 | Keep separate: their triggers, permissions, and verdicts are distinct contracts. |
| `actions/checkout@v7` steps | 95 | Keep explicit: a local composite action cannot run until its repository has been checked out. |
| `actions/setup-python@v7` steps | 48 | Consolidate the 32 YAML-policy bootstraps here; leave pure-stdlib jobs dependency-free. |
| Ad-hoc PyYAML installs | 32 | Replace with this action and the single `PyYAML==6.0.3` pin. |
| `actions/setup-dotnet@v6` steps | 21 | Keep explicit: release, cache, and SDK-selection requirements vary by job. |

Large policy programs are inventoried separately in
[`scripts/policy-checkers.json`](../../../scripts/policy-checkers.json). The inventory gate requires
an owner and an exercised fixture for every `scripts/check-*` program at or above 500 lines.
