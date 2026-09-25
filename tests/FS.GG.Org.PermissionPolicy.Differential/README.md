# Offline FSC-03 differential corpus

`differential.py` runs the Python workflow-permissions gate source with its
proposed strict App identity and authority roster options, alongside the pure
F# aggregate over the same caller, callee and authority workflow YAML bytes.
A local `gh` fixture supplies repository listings and pinned-ref callee reads;
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
strict mode now refuses both unsupported forms. This follow-up also rejects
repeated YAML mapping keys at every nesting level: red-before, Python returned
green for duplicate caller permissions, callee events and App permission inputs
while F# refused each. This follow-up supplies an independent workflow, job,
step count and App-step roster to the Python gate. Red-before, omitted files,
jobs and App steps each produced Python OK while F# returned NO_VERDICT. The
opt-in roster check now refuses those shapes, a manifest that omits an observed
workflow, and duplicate manifest entries. The F# runner now supplies a caller
fleet roster, exact workflow snapshots and per-call binding facts to
`PermissionFleet.evaluate`. Red-before, a second rostered caller under-granted
while the one-pair F# adapter returned OK; the fleet reducer returns FINDING.
The corpus has zero remaining outcome differences over its supplied facts.

The local fixture cannot prove provider authentication, current App installation
grants, either roster's provenance or completeness, accepted scaffold
dependencies or installed receiver parity. The live
receiver currently supplies a default App inventory for some selected App
secrets. It must supply explicit per-identity facts before the new mode can be
enabled, and unsupported `vars.*` or literal identities need an authenticated
binding design or source migration. The authority roster also remains opt-in;
its source ref is checked for equality but cannot be authenticated by this
offline fixture. Duplicate-key refusal applies to every YAML read in the
proposed Python source.
