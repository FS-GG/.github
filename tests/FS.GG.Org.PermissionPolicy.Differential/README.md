# Offline FSC-03 differential corpus

`differential.py` runs the Python workflow-permissions gate source in its
proposed `--require-app-identity-grants` mode and the pure F# aggregate over
the same caller, callee and authority workflow YAML bytes. A
local `gh` fixture supplies repository listings and pinned-ref callee reads;
the F# runner receives declared provider facts. It normalizes each result to
OK, FINDING or NO_VERDICT and prints differences.

Run with the repository's pinned test Python environment (PyYAML 6.0.3):

```sh
dotnet restore tests/FS.GG.Org.PermissionPolicy.Differential/DifferentialRunner.fsproj --locked-mode
dotnet build tests/FS.GG.Org.PermissionPolicy.Differential/DifferentialRunner.fsproj --no-restore
python tests/FS.GG.Org.PermissionPolicy.Differential/differential.py
```

The first differential run found one supported mismatch: with an absent caller
permission block and a nonempty callee floor, Python returned FINDING and F#
returned NO_VERDICT. The aggregate now returns an explicit unproven-default
finding. This stacked source proposal also offers an explicit mode that refuses
a selected App secret without a matching inventory in the Python gate.
Red-before, Python returned a finding when the default inventory was too narrow
and green when it was broad enough; F# returned NO_VERDICT in both cases. The
same fallback also produced green for a `vars.*` identity and a literal App ID;
strict mode now refuses both unsupported forms. The corpus expects each Python
refusal reason and reports three remaining differences:

- F# refuses duplicate YAML keys; PyYAML keeps the last value.
- The F# aggregate covers one bound caller/callee pair; Python enumerates all
  rostered caller repositories. A second caller can therefore produce a Python
  finding outside that one F# pair.
- F# requires every workflow in its supplied authority roster. Python scans
  the authority directory without a separate expected manifest.

The duplicate-key and authority-roster differences are conservative refusals.
The fleet case needs authenticated caller enumeration and a fleet aggregate.
The local fixture cannot prove provider authentication, current App installation
grants, accepted scaffold dependencies or installed receiver parity. The live
receiver currently supplies a default App inventory for some selected App
secrets. It must supply explicit per-identity facts before the new mode can be
enabled, and unsupported `vars.*` or literal identities need an authenticated
binding design or source migration. The Python gate's default behavior remains
unchanged in this draft.
