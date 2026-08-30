# E2E baseline comparison

- Baseline tree: untouched `origin/main` at `828fc0293ae92b3319cde9081659a345dba8ae41`.
- Host validator: `fsgg-sdd` 1.5.0.
- Command: `FSGG_COORD_ENGINE_BIN="$PWD/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine" bash tests/coord-engine-e2e/writes.sh`.
- Observed summary: `coord-engine writes: 190 assertion(s), 184 passed, 6 failed`.
- Complete baseline output SHA-256: `5fabaf3a8c4fa09908d8536eb678d2076ef8f015b6485521d0fcd6501f9eeb2b`.
- Complete candidate output SHA-256: `5fabaf3a8c4fa09908d8536eb678d2076ef8f015b6485521d0fcd6501f9eeb2b`.

All six failures are the same pre-existing cycle-validator compatibility refusal:

`rc=1 output=fsgg-coord-engine: cycle: fsgg-sdd validator toolVersion 1.5.0 is not vetted; accepted: 1.0.0 (Parameter 'artifactPath')`

It occurs for these assertions:

1. `#2133: cycle advance must consume valid provider artifacts`
2. `#2133: production advance must reject critique-shaped files the canonical validator rejects`
3. `#2133: production advance must reject feedback-shaped files the canonical validator rejects`
4. `#2133: critique validator authority must come from the engine, not artifact rootPath`
5. `#2133: feedback validator authority must come from the engine, not artifact rootPath`
6. `#2133: an unpinned engine-side validator replacement must fail closed`

The complete baseline and candidate logs are byte-identical. The candidate's new repair-assertion lifecycles (`.github#2865`, `.github#2819`, and `.github#3014`) all pass, and the candidate has no additional E2E failure relative to the untouched baseline.
