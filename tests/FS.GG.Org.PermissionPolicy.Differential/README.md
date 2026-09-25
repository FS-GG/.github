# Offline FSC-03 differential corpus

`differential.py` runs the live Python workflow-permissions gate and the pure F#
aggregate over the same caller, callee and authority workflow YAML bytes. A
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
finding. The corpus reports four remaining differences:

- F# refuses a separately selected App without its own inventory; Python falls
  back to the default inventory.
- F# refuses duplicate YAML keys; PyYAML keeps the last value.
- The F# aggregate covers one bound caller/callee pair; Python enumerates all
  rostered caller repositories. A second caller can therefore produce a Python
  finding outside that one F# pair.
- F# requires every workflow in its supplied authority roster. Python scans
  the authority directory without a separate expected manifest.

The first, second and fourth differences are conservative refusals. The fleet
case needs authenticated caller enumeration and a fleet aggregate. The local
fixture cannot prove provider authentication, current App installation grants,
accepted scaffold dependencies or installed receiver parity.
