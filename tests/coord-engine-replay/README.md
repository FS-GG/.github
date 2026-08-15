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
- `fixtures/<name>/expected/{reconcile,ready,driver-events}.json` — the checked-in command expectation;
  `reconcile-shadow.json` records classified legacy/intent differences from the same run.
- `fixtures/<name>/manifest.json` *(optional)* — names commands allowed to mismatch
  (`expectFailure.commands`) and why. Not currently used by any checked-in fixture: it existed for
  `fixtures/2216-oscillation/` until `.github#2384` fixed the defect that marker tracked; kept here as
  the mechanism for the next fixture that needs to prove an instrument catches a bug this suite does not
  yet fix.

## Fixtures (4, ~200 KB total — see `du -sh fixtures/*` to recheck)

- **`smoke`** (~52 KB, 22 recorded requests) — captured from `tests/coord-engine-e2e/stateful_server.py`,
  the existing hermetic multi-item board. `run.sh`'s leg 0 also drives the engine DIRECTLY against that
  same server and asserts the direct answer equals this fixture's checked-in expectation — proving the
  capture→replay round trip is faithful, not merely self-consistent.
- **`2216-oscillation`** (~52 KB, 10 recorded requests) — a small, synthetic, one-item board
  (`scenario_server.py`, committed alongside it) modeling the shape `.github#2216` tabulated: one
  unclaimed, `OPEN`, `In review` row with a genuinely open implementation PR on its own branch. Before
  `.github#2384`'s fix, the engine deterministically proposed `Status=Ready` for it — the wrong half of
  `.github#2216`'s tabulated verdict — which `expected/reconcile.json` (a deliberate override, asserting
  the CORRECT no-op answer) caught as a mismatch under a `manifest.json` `expectFailure` marker. `.github
  #2384` widened `Scan.fs`'s markerless-row `itemPr` probe to cover `In review`, not just
  `Ready`/`Backlog`/cleared-`Blocked` (see `scenario_server.py`'s docstring for the root cause), so this
  fixture's `expected/reconcile.json` is now what the FIXED engine actually outputs and `manifest.json` is
  gone. `transcript.json`'s `commands` list names `reconcile` TWICE, exactly as `2450-claimed-review-lag`
  does, so `run.sh` genuinely runs two consecutive `reconcile --json` passes through the fixed engine —
  `.github#2384` AC1/AC2 ("two consecutive reconcile passes over an unchanged open row ... produce the
  same Status remedy — or, correctly, no remedy on the second") proven end to end rather than asserted in
  prose. This is NOT a raw live capture: the real `.github#2216` row is a moving target this hermetic
  suite cannot depend on, so a small synthetic board reproducing the same shape is used instead, and
  `scenario_server.py` documents exactly how to regenerate it.
- **`2450-claimed-review-lag`** (~48 KB, 10 recorded requests) — the CLAIMED sibling of
  `2216-oscillation`: a live claim marker, `Status = In review`, and a genuinely open implementation PR
  on its own branch (`scenario_server.py`, committed alongside it). `.github#2450` — before its fix,
  `Scan.fs`'s claimed-row PR probe fired only for `Status = InProgress`, so this row's `Item.ItemPr` was
  never populated and the lifecycle projector proposed `Status=In progress` for a row that is
  legitimately `In review`, deterministically, forever. `transcript.json`'s `commands` list names
  `reconcile` TWICE — `run.sh` invokes the compiled engine binary independently for each entry against
  the same running replay server, so this genuinely runs two consecutive `reconcile --json` passes
  through the fixed engine, both matching the checked-in no-op `expected/reconcile.json` — `.github#2450`
  AC2 ("two consecutive `reconcile` passes ... produce no board write") proven end to end rather than
  asserted in prose. No `manifest.json`: unlike `2216-oscillation`, this fixture's expectation is what
  the FIXED engine actually outputs, not a deliberate override of a bug that is still open.
- **`m1-backlog-park`** (~48 KB, 9 recorded requests) — one otherwise-ready OPEN row deliberately in
  `Backlog`, with no claim, PR, blocker, or delivery fact to override scheduling intent. Two consecutive
  new-only reconcile passes remain no-ops, proving the persisted intent survives repeated observation.

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
