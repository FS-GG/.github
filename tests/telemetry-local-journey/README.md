# Fresh local telemetry journey

This focused integration test uses the source CLI and maintained dashboard
collector/projection functions, plus the existing dashboard suite's explicitly
synthetic Actions and delivery fixtures. Its fake Codex JSONL is labeled test
input: it never measures genuine agent usage or accepts a delivered item.

Build `src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj` first. Install the browser
fixture dependencies with `npm ci --prefix tests/telemetry-dashboard` and the
existing Playwright Chromium setup. Run `run.py --private-root ABSENT_OUTPUT
--store-root ABSENT_STORE` inside the maintained runtime-validation runner with
`mode: native-runtime`, `backend: pid-namespace`, a 180 second timeout, 15 second
cleanup, and a bounded output budget. The selected store must be a fresh child
of an explicitly admitted private durable parent outside the checkout.
The production durability assessor remains active; overlay refusal is expected.

`--engine ABSOLUTE_ENGINE` selects an already installed CLI without modifying
its installation or global selection. `--skip-browser` qualifies only the
collector/store/canonical-export/projection portion; it reports browser
`not-run`. A failed browser launch or navigation cannot qualify browser
acceptance. Browser runtime libraries may be supplied through a private
`LD_LIBRARY_PATH` in the runner's environment.

The test preserves synthetic JSONL bytes and native exit 37, checks exact
persisted counters and terminal outcome, verifies no machine delivery outcome
was invented, consumes the exact canonical compact snapshot and efficiency
export, and validates the dashboard payload. The browser uses an ephemeral
localhost port and the actual application assets. The namespace lifetime owns
all descendants; the browser and HTTP listener also close through their normal
handles. Each run requires a new output/store path and retains evidence.

No configuration activation, recurring publisher, remote Host, live Pages,
provider assessment, historical operation, or whole-item usage is accepted by
this test.
