# coord-engine-replay

`.github#2401` — recorded board transcripts replayed against the compiled `fsgg-coord-engine`, as a
supplement to the hand-authored fixtures in `tests/coord-engine-e2e/` and `tests/coord-engine-parity/`.
Every one of those is hand-authored: it encodes its author's belief about what GitHub returns, which is
the exact blind spot for the lifecycle/projection defect class (`.github#2384`, `.github#2394`) — those
bugs are about the engine meeting a board state its author never pictured, not about mishandling a
response it expected. A recording captures whatever the board actually contained.

This directory does not replace either of those suites and deletes nothing from them.

## Layout

- `fixture_lib.py` — shared canonicalization/normalization/redaction, used by both `replay_server.py`
  here and `../../scripts/record-board-fixture.py`.
- `replay_server.py` — serves one recorded transcript back to the real engine binary; fails loudly
  (`REPLAY-UNMATCHED-REQUEST`) on any request the transcript does not cover, rather than inventing one.
- `compare.py` — diffs a command's actual JSON output against a fixture's checked-in expectation.
- `run.sh` — the CI leg (`.github/workflows/coord-engine.yml`, `.github/workflows/release-coord-engine.yml`).
- `fixtures/<name>/transcript.json` — the recorded request/response pairs.
- `fixtures/<name>/expected/{reconcile,ready,driver-events}.json` — the checked-in expectation each is
  replayed against.
- `fixtures/<name>/manifest.json` *(optional)* — names commands allowed to mismatch
  (`expectFailure.commands`) and why. Used by `fixtures/2216-oscillation/` only.

## Fixtures (2, ~164 KB total — see `du -sh fixtures/*` to recheck)

- **`smoke`** (~52 KB, 22 recorded requests) — captured from `tests/coord-engine-e2e/stateful_server.py`,
  the existing hermetic multi-item board. `run.sh`'s leg 0 also drives the engine DIRECTLY against that
  same server and asserts the direct answer equals this fixture's checked-in expectation — proving the
  capture→replay round trip is faithful, not merely self-consistent.
- **`2216-oscillation`** (~52 KB, 7 recorded requests) — a small, synthetic, one-item board
  (`scenario_server.py`, committed alongside it) modeling the shape `.github#2216` tabulated: one
  unclaimed, `OPEN`, `In review` row with a genuinely open implementation PR on its own branch. Today's
  engine deterministically proposes `Status=Ready` for it — the wrong half of `.github#2216`'s tabulated
  verdict — which `expected/reconcile.json` (deliberately set to the CORRECT no-op answer) catches as a
  mismatch. `manifest.json` marks that command `expectFailure`, referencing `.github#2384`, the tracked
  root cause (`Scan.fs`'s `itemPr` probe is never attempted for a markerless `In review` row — see
  `scenario_server.py`'s docstring). This is NOT a raw live capture: the real `.github#2216` row is a
  moving target this hermetic suite cannot depend on, so a small synthetic board reproducing the same
  shape is used instead, and `scenario_server.py` documents exactly how to regenerate it.

## Refreshing a fixture

```bash
dotnet build src/FS.GG.Coord.Cli -c Release
python3 <fixture's hermetic server>.py &            # prints its port on stdout
SRV_PID=$!
GITHUB_TOKEN=fixture-token python3 scripts/record-board-fixture.py \
  --repo <REPO> --upstream "http://127.0.0.1:<PORT>" \
  --out tests/coord-engine-replay/fixtures/<name>
kill $SRV_PID
```

Capture overwrites every `expected/*.json` with whatever the engine currently outputs. For an ordinary
fixture that IS the new baseline — review the diff like any other snapshot update, per `.github#2401`
AC3 ("A projection change that alters any recorded board's verdict must update the expectation in the
same PR"). For `fixtures/2216-oscillation/`, `expected/reconcile.json` is a deliberate override (the
CORRECT answer, not the captured one) — restore it by hand after recapturing, and do not delete
`manifest.json`'s `expectFailure` until `.github#2384` is actually fixed and the fixture genuinely
passes.

Capturing straight against the LIVE org board (`--upstream https://api.github.com`) is supported by the
same code path but is a manual, operator-run action, not part of this CI leg: CI has no token and must
stay hermetic, exactly like `tests/coord-engine-e2e/`'s own fixtures.
