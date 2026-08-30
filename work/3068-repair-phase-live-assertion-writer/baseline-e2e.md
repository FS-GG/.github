# E2E baseline comparison

- Baseline tree: untouched `origin/main` at `828fc02907edc1c3577b68568f57112e3ed1d1d3`.
- Host validator: `fsgg-sdd` 1.5.0.
- Command: `FSGG_COORD_ENGINE_BIN="$PWD/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine" bash tests/coord-engine-e2e/writes.sh`.
- Baseline summary: `coord-engine writes: 190 assertion(s), 184 passed, 6 failed`.
- Candidate summary: `coord-engine writes: 195 assertion(s), 189 passed, 6 failed`.
- Complete baseline output SHA-256: `5fabaf3a8c4fa09908d8536eb678d2076ef8f015b6485521d0fcd6501f9eeb2b`.
- Complete candidate output SHA-256: `1f5be7d5e1ded969687976d9194e2752737260d5624a49d3b7b01c3dc07c2d08`.

All six failures are the same pre-existing cycle-validator compatibility refusal:

`rc=1 output=fsgg-coord-engine: cycle: fsgg-sdd validator toolVersion 1.5.0 is not vetted; accepted: 1.0.0 (Parameter 'artifactPath')`

It occurs for these assertions:

1. `#2133: cycle advance must consume valid provider artifacts`
2. `#2133: production advance must reject critique-shaped files the canonical validator rejects`
3. `#2133: production advance must reject feedback-shaped files the canonical validator rejects`
4. `#2133: critique validator authority must come from the engine, not artifact rootPath`
5. `#2133: feedback validator authority must come from the engine, not artifact rootPath`
6. `#2133: an unpinned engine-side validator replacement must fail closed`

The candidate adds exactly five passing assertions: current-first topology selection, malformed selected-predecessor fail-closed behavior, live #3068/#3067/#3069 historical-isolation, and the host-grant/assertion command-follow path including caller-authority and zero-mutation inversions. The candidate's repair-assertion lifecycles (`.github#2865`, `.github#2819`, and `.github#3014`) also pass, and the candidate has no additional failure relative to the untouched baseline.
