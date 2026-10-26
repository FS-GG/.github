# Optional evidence delta and issue observation

These read-only helpers reduce repeated local evidence reading. They are optional views over
existing evidence, with no execution, dispatch, acceptance or merge authority. The maintained
`routine-delivery.py --watch-checks` remains the PR-check observer. The separate issue helper
can watch the existing Home #4516/#4517 comment channels without posting or starting work.
Do not postpone an already authorized consumer for these helpers.

## Byte evidence

Existing schema-v1 CI artifact manifests and `create`/`verify` stay compatible. Additive
`verify-delta` accepts a `fsgg.evidence-delta/2` JSON object. Required fields are:

```json
{
  "schema": "fsgg.evidence-delta/2",
  "source": {"head": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "tree": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"},
  "files": [{"id": "controller", "kind": "source", "path": "controller.py", "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", "bytes": 123, "gitBlob": "dddddddddddddddddddddddddddddddddddddddd"}],
  "runs": [{"id": "original-run", "attempt": 1, "outcome": "failure", "clocks": {"startedAt": "original-record", "deadlineAt": "original-record"}, "limits": {"wallSeconds": 900}}],
  "dependencies": [{"id": "runtime", "reference": "existing-exact-runtime-record"}],
  "evidence": [{"id": "original-receipt", "reference": "existing-custody-reference"}],
  "acceptance": {"scope": "source-only review", "reference": "existing-review-reference"},
  "gaps": ["Native consumer acceptance remains unobserved"]
}
```

Use actual hashes/identities in place of the illustrative values. An artifact row uses
`kind: "artifact"` and `gitBlob: null`. Every file has a distinct ID and relative path.
`source` and `gitBlob` identify the asserted source; this helper does not check Git
correspondence. Dependency/evidence records have distinct `id` values and retain their
existing references. Their changed/added/removed identities appear in the short delta.
Run outcomes are `success`, `failure`, `cancelled`, `unknown` or `running`. Original run records
cannot change types, values or disappear when a baseline is supplied: append subsequent records or place
later result/settlement metadata in optional `observations`. Such metadata never promotes the
helper's own process-settlement or acceptance fields. Clocks/limits are retained records,
not enforcement or proof that the original runtime respected them.

First read the selected inventory locally and retain its byte-verification receipt:

```sh
python3 scripts/evidence-manifest.py verify-delta baseline.json --root /private/baseline > baseline-receipt.json
```

The caller selects the independently accepted baseline, including the exact SHA256 of both
its manifest and its existing verification receipt. Candidate JSON cannot assert trust:

```sh
python3 scripts/evidence-manifest.py verify-delta candidate.json --root /private/candidate \
  --baseline baseline.json --baseline-sha256 EXACT_BASELINE_SHA256 \
  --baseline-receipt baseline-receipt.json --baseline-receipt-sha256 EXACT_RECEIPT_SHA256
```

Output is `fsgg.evidence-byte-receipt/2`, including the candidate manifest digest, selected
baseline digests, inventory, original runs, dependency/evidence references, acceptance scope,
gaps and changed/removed identities. Redirect it locally if needed. The helper itself writes
nothing. Baseline selection and acceptance remain the caller's responsibility: a SHA256 pin
binds the selected bytes but does not authenticate a reviewer or establish native acceptance.

Unchanged inventory rows inherit **historical byte proofs only**. The helper does not reread
those candidate paths, assert their current existence or join them to current Git. Changed
and added payloads are bounded regular-file reads, rejecting symlinks in every component,
traversal, special files, size/hash mismatches and detected mutation during reads. Bounds are
256 KiB per JSON input, 512 identities per array, 8 MiB per payload and 64 MiB aggregate declared
payloads. Stable read checks concern the opened file/path at read completion; they do not freeze
future writers or provide a filesystem snapshot. Receipts can be chained only within the same
bounded JSON-input size. Artifact hashes and retained observations prove none of actual process,
writer, cgroup, namespace or cleanup settlement; each remains explicitly `unknown`, as does native
acceptance. Expired v1 evidence calls for newly authorized evidence or a recorded gap, never an
unchanged replay.

`measurements` reports actual local payload-file/byte reads and historical proofs reused.
Optional manifest `reportedMetrics` has exactly `coordinatorInterventions`, `sourceReadyAt`,
`firstNativeAt` and a nonempty `reference` to the existing reporting evidence. Counts are
nonnegative integers or null, timestamps are timezone-bearing ISO dates or null. The receipt
labels the resulting count and elapsed seconds as **reported**, not independently measured.
Missing metrics and token savings remain null/Unknown. Neither wall-time nor token savings
are inferred from local proof reuse.

## One bounded comment observer

Create a private local custody directory's parent, then take a bounded successful cursor
using only the fixed GitHub issue-comment GET route. Snapshot output contains IDs, update
times and body hashes, never comment bodies. Treat its hash as the explicit successful cursor:

```sh
python3 tools/routine-issue-observer.py snapshot --repo FS-GG/.github \
  --issue 4516 --issue 4517 --original-request EXISTING_REQUEST_REFERENCE \
  --since 2026-10-26T13:25:00Z \
  --seconds 60 --custody-dir /private/observer > successful-cursor.json
python3 tools/routine-issue-observer.py watch --repo FS-GG/.github \
  --issue 4516 --issue 4517 --original-request EXISTING_REQUEST_REFERENCE \
  --since 2026-10-26T13:25:00Z \
  --seconds 120 --custody-dir /private/observer \
  --baseline successful-cursor.json --baseline-sha256 EXACT_CURSOR_SHA256
```

The Home example selects the existing original request's source/reply window, starting at
`2026-10-26T13:25:00Z`; select the actual agreed UTC boundary for a different request. Optional
`--since` is a fixed RFC3339 timestamp, normalized to UTC (up to six fractional digits).
Every page uses the same URL-encoded `since`. GitHub filters by comment update time, so an
older comment edited inside this window can appear. Cursor/output `since` declares that
historical coverage boundary; comments neither created nor updated since it are outside the
sample, and their absence proves nothing about earlier history. Omitting `--since` retains
unfiltered behavior and the same fail-closed pagination bound. A filtered cursor is never a
whole-history claim, and this timestamp does not move forward during polling.

These examples are instructions for an explicitly selected future observer; they do not launch
one as part of source qualification. Both modes hold one nonblocking cooperative `flock` keyed
by repository, sorted issues and original request until return. The baseline original request must
match the selected request, and its normalized `since` must match the selected window before
lock acquisition or any GET. The lock key deliberately excludes `since`: selecting a different
window cannot create a second cooperating observer for the same original request. Use the same private custody
directory and request identity for cooperating callers. This prevents duplicates among those
callers, not independent watchers or arbitrary hostile filesystem writers. It creates only a
lock file, not a status ledger. It cannot coordinate canonical PR watchers.

A watch emits one determining `change`, `observation-failure` or `deadline` and exits. Changes
include edits to an existing message's timestamp or body hash and removed message IDs. The
last complete successful cursor survives partial reads, malformed replies, failures and reserve
exhaustion. A watch polls successful unchanged reads; it never retries a failed read. Each fresh
read requires the entire five-second query plus one-second cleanup reserve before spawning.
The original monotonic window is 1–600 seconds; no action renews it. There are at most two issues,
four 100-comment pages each, 256 KiB stdout per GET, 48 KiB per successful cursor and 64 KiB final
output. A population beyond a bound fails explicitly; it is not silently truncated. Pages provide
a bounded sample, not an atomic GitHub snapshot, and older comments beyond these bounds require
a separately selected observation design.

Only `gh api --hostname github.com --method GET repos/OWNER/REPO/issues/N/comments` is constructed.
No arbitrary command/endpoint, POST, workflow rerun, credential print, dispatch or native operation
is exposed. The existing local `gh` authentication is used only for that selected GET. Failed
reader output is reported as a partial byte count, not raw bytes. On failure, cancellation or setup interruption after spawning, the helper kills its
reader group and waits for its direct child before releasing its operation lock; descendant settlement remains unverified. Success
likewise grants no runtime-settlement claim. Cooperative locks and observers provide no hard
process containment. Exit 0 means a complete snapshot or observed change; exit 3 means observation
failed or reached its bound. Neither means ready, accepted or safe.

Run focused local controls with `python3 tests/evidence-delta/test_controls.py`; the existing
`tests/evidence-manifest/run.sh` covers schema-v1 behavior and invokes these controls through
the existing `evidence-manifest-fixture` policy subject. Both policy workflow event filters include
the helper, observer, controls and usage documentation paths. Apply the coordinator's ordinary
isolated verification resource share to the test process and its inert children.
