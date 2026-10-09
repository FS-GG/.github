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
Dashboard helper subprocesses inherit the same scoped private environment as
direct CLI calls. A checked launcher proves private XDG selection and absence
of inherited association and credential keys at the actual helper boundary.
The browser uses the maintained fixture's standard headless Chromium launch.
The production durability assessor remains active; overlay refusal is expected.

`--engine-path ABSOLUTE_ENGINE` selects an already installed CLI without modifying
its installation or global selection. `--skip-browser` qualifies only the
collector/store/canonical-export/projection portion; it reports browser
`not-run`. A failed browser launch or navigation cannot qualify browser
acceptance. Browser runtime libraries may be supplied through a private
`LD_LIBRARY_PATH` in the runner's environment. On hosts without system fonts,
select a private font directory/cache through a private `FONTCONFIG_FILE`;
missing font configuration is an environment failure, not dashboard acceptance.

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

The existing dashboard CI workflow runs `test_environment.py` through unittest.
It checks real helper subprocess environment inheritance, the sentinel failure
control, and restoration after a child failure. That hosted test does not run or
accept the separately admitted durable-store/browser journey described above.

The optional `--activate-workspace` profile uses actual `workspace activate-local`
to enroll a uniquely identified producer in a fresh private configuration and
assessor-approved store. The synthetic collector then uses `--config` and the
explicit repository association, drains applied receipts, and the existing
packaged dashboard browser journey checks its one-use bootstrap, scoped item,
HttpOnly session and logout. This proves that private installed association and
its packaged collection/display route. Global selection, services, genuine
agent counters and historical operation custody remain separate.
