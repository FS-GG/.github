Run `python3 tests/update-fsgg-coord-cli/test_update.py`. The main-entry fixtures
replace only in-memory publication pins for their tiny synthetic package and use
disposable HOME/PATH directories with fake installer/version commands. The
production script exposes no fixture pin or arbitrary executable override.

For the additional actual-shell check, set `UTEL_TEST_PUBLIC_ARCHIVE` and
`UTEL_TEST_RELEASE_MANIFEST` to the retained public 0.100.0 package and promoted
manifest. This reads those bytes and verifies their production pins. A fake
installer copies inert package files and creates a fake version command; it never
executes a packaged assembly, SDK, real installation or retained telemetry reader.

Deployment maps both `scripts/update-fsgg-coord-cli` and its adjacent
`update_fsgg_coord_cli.py` to the existing updater directory. These tests and source
delivery do not deploy either file or qualify actual host adoption.
