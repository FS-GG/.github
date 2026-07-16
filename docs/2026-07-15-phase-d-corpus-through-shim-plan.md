# Phase D — the corpus through the shim, and the deletion of bash

**Date:** 2026-07-15
**Owner:** `.github` (the coordination engine)
**Governs:** the execution of [ADR-0040](adr/0040-port-the-io-layer.md) Phase D
**Status:** In progress — **D.1 underway**. Phases A–C have landed. The corpus-through-engine parity
harness has grown from the prototype to **25 full + 1 partial of 27 corpus cases** (~407 assertions); D.2–D.4 not started.
Case 31 is now FULL — its #720 superseded-run verdict drives through the engine's first-class `landable`
command, and its #724 `--wait` poll loop (which never believes an early green — it waits for the run set to
STOP GROWING) landed on top of it. Case 13 is now FULL too — its last leg, `reap` (the DESTRUCTIVE worker
command) scoping to the checkout you are standing in (#480), is proven: a bare `reap` from an SDD checkout
considers only SDD's claims, from a Rendering checkout only Rendering's, and outside a checkout it REFUSES
rather than fall back to the org-wide scan that once deleted across five repos. Case 24 (the last partial)
has begun to close: its **lock-fails-closed adversarial legs** — a marker quoted in a message does not forge
a lock, a malformed marker BLOCKS, an expired worker cannot resurrect its claim under a new holder, and a
failed or empty CAS re-read is a LOSS that withdraws its own marker rather than orphaning it — now drive
through the engine over HTTP.
See [§5 D.1 progress](#d1--drive-the-full-corpus-through-the-engine-locally-green) for the ported/remaining ledger.

---

## 1. Where we are

[ADR-0040](adr/0040-port-the-io-layer.md) ports the coordination engine's IO layer to F# and then
makes `scripts/fsgg-coord` the ~40-line **shim** of ADR-0034 §4.4 that execs the compiled tool. It stages
the work A→D, "each step reachable from the one before it." A, B, and C have landed:

- **Phase A/B (read + write path)** — the `IGitHub` seam, the HTTP adapter, the recording fake, the CAS,
  the board writes, `done`, `widen`, `child`, `set-field`, `release`, `heartbeat`, `say`. The engine
  reads its own board and performs every write over HTTP.
- **Phase C (preconditions)** — `setup-dotnet` + `dotnet tool restore` in the workflows that shell out
  ([#770](https://github.com/FS-GG/.github/issues/770)), and the `NUGET_ORG_PUBLISH` restore gate
  ([#750](https://github.com/FS-GG/.github/issues/750), [#765](https://github.com/FS-GG/.github/issues/765)).

**The engine is now proven case-by-case, over HTTP, against the corpus's certified answers.** The
`tests/coord-engine-parity/` harness (~392 assertions across **25 of 27 corpus cases**, 28 fixture
servers) drives the *compiled binary* against fixture GitHub servers and holds it to the exact answers
the shell corpus certifies for bash — scheduling, blockers, starved-vs-empty, cross-repo scoping,
fail-closed reads, touch-set fabrication, one-item-per-worker, `child` idempotency, `set-field --batch`,
`claim`'s column restore, the honest empty-queue reason, the git-remote repo scope, the `verify-paths`
touch-set gate (OK/DRIFT/SKIP, #322's "I could not check is never a verdict", the `--issue`
repo-boundary refusals — #479 cross-repo straddle, #494 the repo-qualified issue read — the #430
git-remote repo default when neither `--repo` nor `--issue` is given, and the cross-repo closing-ref
SKIP), the `whoami --mint`/twin-session identity defence (#419), the **resolver cache and its budget**
(#418 — `bootstrap` costs two GraphQL points and day-caches the id map; `board`/`field-id`/`option-id`
read it for zero; `item-id` resolves in one and then serves from cache — the §3 call-counting
transformation, re-expressed as HTTP request counts), and the full `take` exit-code contract.
**Ten real defects the port was *for* have been closed in the engine along the
way**, each proven with a parity slice: [#516](https://github.com/FS-GG/.github/issues/516) (one item per
worker), [#585](https://github.com/FS-GG/.github/issues/585) (distinct `take` exit codes),
[#533](https://github.com/FS-GG/.github/issues/533) (`done` drops the worker's own claim),
[#320](https://github.com/FS-GG/.github/issues/320) (`child` reads the edge before it links),
[#440](https://github.com/FS-GG/.github/issues/440) (`next`/`take` name the observed reason, not a guess),
[#448](https://github.com/FS-GG/.github/issues/448) (`set-field --batch`),
[#481](https://github.com/FS-GG/.github/issues/481) (`claim` records the column it overwrites),
[#480](https://github.com/FS-GG/.github/issues/480) (a worker command scopes to the checkout you are
standing in), [#419](https://github.com/FS-GG/.github/issues/419) (`claim` refuses a marker with our id
but another session — two workers sharing one id is not a lock), and
[#418](https://github.com/FS-GG/.github/issues/418) (the board id map and item ids are disk-cached, so a
worker command pays `bootstrap`'s two GraphQL points once a day, not once an invocation — the budget that
dies first). The rest
of ADR-0040's "~19" are either addressed in source or closed by construction (a typed `Result` makes a
failed read an `Error` at every call site — [#584](https://github.com/FS-GG/.github/issues/584) cannot
exist in the engine).

**What has NOT happened:** the *full* corpus (29 cases, 891 assertions) still drives **`bash`
scripts/fsgg-coord** through a PATH-shim `gh` stub. The engine shadows it (`50-shadow-engine`) and is
compared against it (`51-fs-flip`), but bash is still the thing under test, and bash still exists.

Phase D closes that.

## 2. The exit criterion (from ADR-0040)

> **Bash is deleted when the corpus is green through the shim in all six receivers, with the restore
> gate green.**

Not a date. A computable condition. Three obligations inside it:

1. The **corpus runs green through the shim** — i.e. against the *engine*, not bash.
2. It does so in **all six receivers** (sdd, rendering, governance, templates, game, audio).
3. The **`NUGET_ORG_PUBLISH` restore gate** is green (done — C3).

## 3. The one hard problem: the corpus counts `gh`, the engine speaks HTTP

This is the crux ADR-0040 C1 names, and it is the whole of the technical work.

The 891-assertion corpus is a **black box** over `bash scripts/fsgg-coord`, driven against a **PATH-shim
`gh` stub that counts calls**. Every budget assertion ("this operation costs N GraphQL points"), every
ETag-304 assertion, every fail-closed assertion works by counting or faulting `gh` invocations. **An F#
tool calling `HttpClient` directly is invisible to that stub** — it makes zero `gh` calls — so a corpus
that simply pointed `scripts/fsgg-coord` at the shim would see every call-count collapse to zero and die
at the moment it is most needed.

ADR-0040 C1's resolution: the corpus keeps its black-box character by driving the tool **through a
configurable API base**, with the call-counting moved from the `gh` stub to the **HTTP layer** (a fixture
server that counts requests, or the recording fake). This is a **transformation of the fixture layer, not
a reduction of the assertions** — the property ("costs N GraphQL calls") is still checked; it is counted
one transport over.

**The `tests/coord-engine-parity/` harness is the working prototype of exactly this.** It already drives
the compiled engine against stdlib HTTP fixture servers (`pw_server.py`, `starved_server.py`,
`ratelimit_server.py`, …), counts and faults at the HTTP level (the malformed-marker toggle, the 403
rate-limit, the ETag-capable transport), and holds the engine to the corpus's certified answers. Phase D
is, in essence, **growing that prototype to cover the full corpus** — or, equivalently, teaching the
existing corpus harness to drive the engine through a configurable API base.

## 4. Preconditions (ADR-0040 C1–C4) — status

| # | precondition | status |
|---|---|---|
| **C1** | no step may *reduce* the corpus; the IO layer is a PORT with an `IGitHub` seam + recording fake; drive through a configurable API base | seam + fake landed (A/B); the **configurable-API-base corpus** is the work of §5 below |
| **C2** | the kit row runs where there is no .NET — `setup-dotnet` in every workflow that shells out, green in all six receivers, *before* the shim | **done** ([#770](https://github.com/FS-GG/.github/issues/770)) |
| **C3** | the shim presumes the tool is restorable — the `NUGET_ORG_PUBLISH` gate must exist | **done** ([#750](https://github.com/FS-GG/.github/issues/750)/[#765](https://github.com/FS-GG/.github/issues/765)) |
| **C4** | the lock stays on REST (GraphQL dies first under fan-out) | held — the CAS was re-expressed on REST in Phase B, not re-designed |

## 5. The staged plan — each step reachable from the one before it

### D.1 — Drive the FULL corpus through the engine, locally, green

Grow the parity prototype into a **full corpus-through-engine harness**: every one of the 29 cases'
certified answers, produced by the *engine* over HTTP against a fixture server, with the budget/ETag/
fail-closed assertions re-expressed at the HTTP layer.

- Prefer **reusing the shell corpus verbatim** by giving it a configurable API base and an HTTP-level
  counting fixture (the `gh` stub becomes an HTTP fixture; `run` points `scripts/fsgg-coord` — under the
  shim — at it). This keeps the 891 assertions *as they are* and honours C1's "no reduction".
- Where an assertion counts `gh` invocations specifically, re-express it as an HTTP request count. Log,
  do not silently drop, any assertion that genuinely has no HTTP-level form.
- **Exit:** the corpus is green driving the engine (through the shim) locally, with call counts intact,
  and `50-shadow-engine` / `51-fs-flip` still green (bash still present, still agreeing).

**Progress (as of the off-board `who` slice, 2026-07-15).** The harness is grown one defect/case
at a time — each PR titled `parity: … (case N)` (the engine already matched bash — port the slice) or
`fix(engine): … (#NNN)` (a real port gap — fix the engine, then prove it). **25 of 27 cases fully covered,
plus 1 partial (24)** — the 27 being the full corpus's 29 minus `50-shadow-engine`/`51-fs-flip`,
which are the differential harness D.4 disposes of, not engine-behaviour cases:

| covered | case | note |
|---|---|---|
| ✓ | 10, 11, 12, 13, 14, 15, 20, 21, 22, 23, 25, 26, 30, 31, 32, 33, 34, 35, 40, 41, 42, 44, 45, 46, 52 | see the parity ledger in `tests/coord-engine-parity/run.sh` |
| ◑ | 24 (`--issue` boundary + cross-repo close, shared with 23) | the #479/#494 `verify-paths --issue` legs and the cross-repo CLOSING-ref SKIP are DONE; the lock's **fail-closed** adversarial reads (forged/malformed markers, heartbeat resurrection + expired-lease refusal, failed/empty CAS re-read) now land too; and `say --to` normalization (a mis-cased target is slugged to the id `inbox` matches, `*` stays literal) now lands too; what remains is the lock's **mutating** interleavings — stale-marker collection + notify (`claim` leaves the stale marker for `reap`, a documented divergence), `reap` re-verify-before-delete + delete-before-notify, the shared-id re-claim warning, and `overlap`'s `paths_of` fail-closed |

Case **14 is now FULL** (#807): the `done` PR-**provenance** legs land — with no `--pr`, `done` stamps the
LATEST-merged among the issue's TRUE closers (#342, `Facts.ClosingPrs` became a `ClosingPr list` carrying
`mergedAt`/oid/`ClosesThis`, `ClosedByEvent` became `CloserPrs: int list`), a commit-subject keyword routed
to the PR title is rescued by GitHub's own CLOSED_EVENT and a commit closer resolves through to its PR (#558),
and `--pr` names WHICH true closer to stamp but can never launder a mere mention (#543). The stamp names the
merge commit (`merged PR #92 @ 09c836e`). #543's `--pr`-mention leg exits non-zero (engine `ExitRed=3`;
bash's literal 1 disposed as the property). New `doneprov_server.py`; 13 parity + 5 DoneTests + 2 DoneFactsTests.

Case **23 is now FULL**: `verify-paths` OK/DRIFT/SKIP + #322 fail-closed, the `--issue` boundary (#479
straddle, #494 repo-qualified read, `--repo` reduction / bare-repo `--issue` agreement, `--issue`-decides-repo,
head-ref-read bypass), the **#430 git-remote repo default** (repo off the checkout's remote when neither
`--repo` nor `--issue` is given, resolved FREE/offline with no GraphQL — so a dead budget is never blamed on
the checkout; no remote → an earned refusal), and the **cross-repo CLOSING-ref SKIP** (a PR closing another
repo's issue → SKIP naming the other repo, no verdict across the boundary). The SKIP-exit divergence and
bash's `gh repo view` fallback are disposed on the record (the engine has no gh-repo-view leg, so its
EX_RATE-vs-checkout failure modes are structurally absent).

**Remaining (case 43 + the rest of 24), each classified as a port gap or a deliberate divergence:**

| case | what it needs | class |
|---|---|---|
| 24-remainder | the lock's **fail-closed** adversarial reads are DONE (forged marker does not hold, malformed marker BLOCKS, heartbeat resurrection + expired-lease refusal, failed/empty CAS re-read is a LOSS that withdraws its own marker), and `say --to` normalization is DONE (a mis-cased target is slugged to the id `inbox` matches, `*` stays literal); what remains is the **mutating** interleavings — stale-marker collection + notify (`claim`/`adopt` leave the stale marker for `reap`, the documented GC-on-transfer follow-up), `reap` re-verify-before-delete + delete-before-notify, the shared-id re-claim warning + `lease renewed` wording, and `overlap`'s `paths_of` fail-closed | `reap`/`claim` mutating legs (genuine engine changes) |
| 43 (kit-digest-and-argv) | kit digest / argv passthrough | overlaps D.2 (the shim's own contract) |

Case **44 is now FULL** (#419): `whoami --mint` is one eval-able line, CSPRNG-unique per call, and
round-trips through `whoami`; the shared-session warning points at the mint COMMAND and offers no literal id
to copy; and `claim` REFUSES a live marker carrying our worker id but a DIFFERENT session — a twin — naming
the other session and surviving `--force` (a broken identity is fixed with a new identity, not a steal),
while a sessionless or same-session marker stays ours (a heartbeat, the back-compat boundary). The fix was
the `Twin` CAS outcome and the stderr warning; `Identity.mint`/`whoami --mint`/marker `session=` already
existed. The "lease renewed" WORDING is disposed on the record (bash's; the engine reports a re-claim as
`claimed <ref> by worker <w>`), re-expressed as the property — a same/sessionless re-claim SUCCEEDS.

Case **10 is now FULL** (#418): the §3 "one hard problem" — the call-counting transformation — is
discharged. `bootstrap` resolves the board + field/option id map in exactly **two** GraphQL points and
now **day-caches** it to disk (`Cache.getBoardMap`/`putBoardMap`, `FSGG_COORD_BOARD_TTL_SEC` default a
day); `board`/`field-id`/`option-id` re-hydrate that map and cost **zero**; `item-id` resolves an issue's
board item in **one** call and then serves it **forever** (`Cache.getItemId`/`putItemId`, positives only —
#421's `Ok None` is never memoised). Every "costs N `gh` calls" assertion is re-expressed as an HTTP
request count read off the fixture's `/_gql` counter (`cache_server.py`), and `set-field`'s dataType
routing (single-select → option id, DATE → date, TEXT → text, empty → the clear mutation) is proved at the
mutation the engine emits. **The fix was the port gap the `.fsi` doc-comments already described but no
layer implemented:** `bootstrap`/`itemId` were re-resolved on every invocation, so five workers looping
`take` re-paid `bootstrap`'s two points each time — the exact #418 drain. `bootstrapCached` (rolled out to
every worker-command call site) and `itemIdCached` (the `item-id` command) close it; the five diagnostic
subcommands expose the resolver the corpus counts. `--refresh` drops the day-cache (the remedy
`Snapshot.fs` already pointed at but no command backed). 23 parity assertions + 7 Board/Cache tests + 3
Options tests.

Case **14 is PARTIAL** (#496): the new `lint` command ships with its SCHEDULABILITY rules — **NO-TOUCH-SET**
(a Ready/Backlog OPEN issue that declares no `Paths:` at all, fence-aware so a quoted-only declaration is
none, #277) and **BAD-TOUCH-SET** (one that declared a touch-set every token of which is unmatchable). Both
are the same condition — "no worker can ever pick this up" — and both are errors; the `Paths: none` sentinel
suppresses them (a deliberate touch-set-less epic/decision item is legitimate but must SAY so), and In
progress / closed / real-Paths items are clean. The command is a reconciler read (fresh, never the cache),
scopes on `--repo` (short-ids resolved), renders the `FSGG-LINT <SEV>  <CODE>  <short-id>  — <detail>` text
projection or a `--json` array (`code`/`severity`/`id`/`status`/`url`/`detail` — no scratch field leaks),
and is a gate (`--strict` makes a note fatal too). It reuses the engine's `TouchSet.parse`/`unmatchable`
(one grammar, both surfaces — #485) and fails closed on an unreadable body (#266). A second slice then added
the **epic-ROLL-UP-graph rules**: EPIC-NO-CHILDREN, EPIC-CHILDREN-TRUNCATED (the Total kept apart from the
visible nodes — a "no unlinked child" verdict over a set already known to be short is #266), EPIC-DONE-OPEN-CHILD,
the DONE-STATUS-OPEN-ISSUE note, and the intricate **EPIC-UNLINKED-CHILD** — an epic whose body declares a
child the sub-issue graph does not contain, with a body-cited **PR ref dropped** (a PR can never be a
sub-issue, #346) and an **unresolvable ref KEPT** (fail closed, #266). New engine machinery in its #485 "one
home": Core `EpicBody.childRefs` (task-list child refs — all three bullets, first-ref-wins, canonicalized +
sorted) and `Reads.subIssues` (the graph with Total + per-child state) / `Reads.refIsPullRequest` (the probe).
18 + 16 parity assertions + 3 Options + 7 EpicBody + 4 Reads tests. **The whole `lint` command is now DONE.**

A third slice then closed the **`done --flip` epic ROLLUP**: it HOLDS the parent while a sibling is open
(#235/#583), FLIPS when every child is Done + closed (stamped Done AND closed, #613), REFUSES when the
epic's body declares a child the sub-issue graph does not contain (#325 — naming the child and pointing at
`fsgg-coord child`), and flips over a body-cited PR ref (#346). The unlinked check reuses `EpicBody` and a
new **shared `Done.bodyUnlinkedChildren`** (the #485 one-home the lint EPIC-UNLINKED-CHILD rule was
refactored onto — one definition, both surfaces). 12 parity assertions + 1 Done rollup test.

A fourth slice (#807) then closed the **`done` PR-provenance legs — case 14 is now FULL**. With no `--pr`,
`done` stamps the LATEST-merged among the issue's TRUE closers, never the first prose mention (#342):
`Facts.ClosingPrs` became a `ClosingPr list` carrying `mergedAt`/oid/`ClosesThis` (its body names THIS
issue), and `ClosedByEvent: int option` became `CloserPrs: int list` (the PR(s) the CLOSED_EVENT names — a
`PullRequest` directly, or the PR associated with the closing `Commit`). A keyword in the commit SUBJECT,
routed by `gh pr create --fill` to the PR title where `closingIssuesReferences` never looks, is rescued by
GitHub's own close event, and a commit closer resolves through to its PR (#558). `--pr` names WHICH true
closer to stamp but is held to the same closer predicate — it can never launder a mention (#543). The stamp
names the merge commit (`merged PR #92 @ 09c836e`). New `doneprov_server.py`; 13 parity + 5 DoneTests + 2
DoneFactsTests. Disposed on the record (ADR-0040 §5): bash exits 1 on a red NOT-DONE, the engine's certified
Red exit is `ExitRed=3` — re-expressed as the property (a refused stamp exits non-zero).

Case **34 is now FULL** (#809 then this slice): the `overlap` command landed first (#353). `Paths:` tokens
are repo-relative, so `TouchSet.conflicts` is only meaningful WITHIN a repo — bash's `overlap` compared an
item's tokens against every OTHER repo's live claims, so `scripts/fsgg-coord` in one repo "collided" with the
same string in another (two files, two repositories). The engine had the repo-scoped conflict primitive
already (case 35) but no command surface; #809 ported it read-only: `overlap <ref> --active` (the item vs its
own repo's live claims — a cross-repo namesake excluded, its holder never named) and `overlap <a> <b>`
(DISJOINT by construction across a repo boundary, else `TouchSet.conflicts`). A real overlap exits
`ExitContended=6` and names the colliding item + holder + shared token stems.

This slice then closed the **`widen` collision-DETECT-and-NOTIFY half** — ADR-0021's "re-declare AND
re-check overlap before continuing", and the part a worker cannot do alone. After the widen LANDS, the engine
re-checks the NEW touch-set (the rewritten body's, never the old) against the live claims in THIS item's repo
and NOTIFIES each worker it now collides with, on their own issue: a cross-repo namesake is a phantom (#353)
and its innocent holder is left uncommented, while a genuine same-repo neighbour is named (`now collides with
FS.GG.SDD#403`), notified (`notified worker sdd-sib on FS.GG.SDD#403` — a `Writes.say` comment), and the
widen exits non-zero. The #353 collision scan is **factored out of `overlap --active` as a shared
`activeCollisions`** and reused verbatim by `widen`, so the repo scope cannot drift between the two surfaces
(#485's one-home shape). New `widennotify_server.py` (write-capable — it records the PATCH and counts the
notify POST per issue); 9 parity assertions. Disposed on the record (ADR-0040 §5): the engine's `widen`
requires the widener to HOLD the lock (#706) — bash's does not, so the fixture's #401 carries a claim the
corpus omits (an engine strengthening, not a change to the property under test); and the collision exit is
`ExitContended=6`, bash's literal 1 re-expressed as the property (a real collision exits non-zero).

Case **26 is now FULL**, and case **25's `who` legs land with it** (this slice): `who` now reads the LOCK
**off the board**. The lock is not the board column — a claim marker sits on the ISSUE, whose board Status
may be Ready (a column flip that FAILED) or nowhere at all (a claim that never reached the board) — so
`who --repo` scans the repo's OPEN ISSUES (arm B), unions them with the board's In-progress rows (arm A,
the only fact that licenses an `unclaimed` verdict on a markerless item), and classifies each by its
marker: an **off-board HELD** claim the board never knew about (named, with its touch-set read from the
body), a **board-says-Ready-but-HELD** claim (the lock, not the column, #461), an In-progress markerless
**UNCLAIMED**, and — never — a chatty markerless issue. The scan **PAGINATES** (a lock has no 100-issue
limit) and is **NEVER conditional** (a 304 could serve a `comments: 0` captured before a marker was
posted and hide a live lock); both re-expressed at the HTTP layer via the fixture's `/_requests` ledger
(`page=2` fetched, `inm=none` on every issue-list request). On top of that scan rides case 26's **#581
proof of life**: a STALE row whose own `item/<n>-*` PR is OPEN carries `livePr` = `#NNN item/<n>-…` in
`--json` and reads `STALE (#NNN OPEN)` in the human table — while a stale claim with no open PR is a **bare
`STALE`** a reaper may collect. The scan reuses `Reads.openIssues` (the paginated, unconditional off-board
read `reap` introduced) and probes proof of life with `prAlive` + `prHeadRef`; new `offboard_server.py`
(the case-25 world + case-26's live PR in one fixture), pw_server grew a repo-scoped issue-list; 11 parity
assertions. Disposed on the record (ADR-0040 §5): the `who` proof-of-life probe is TWO REST reads (the
open-PR scan, then that PR's head ref) where bash's `pr_alive` is one — the head ref `prAlive` matches on
is not surfaced through `Liveness`, and the path (a stale claim WITH an open PR) is rare; and the
`prState` **landable colour** bash adds to the STALE row (`green`/`conflicted`/`pending`/`red`) is deferred
to case 30/31's `landable`/`adopt` — the base proof of life stands without it (a null `prState` is bash's
own `STALE (#NNN OPEN)` fallthrough). Case **25 stays PARTIAL**: `batch` must still learn the off-board
scan `who` now performs (reserve an off-board claim's touch-set), and the #428 starved-queue prose +
`inbox` legs remain (see the remaining table).

The **`reap` command** landed earlier (#581). A lease is EVIDENCE of abandonment,
never PROOF: its false positive is systematic (work that outlasts its lease), and bash's reaper broke a
lock on expiry alone and collected the claims of workers who were visibly still working, TWICE. The new
`reap [--repo] [--apply]` scans the repo's OPEN ISSUES (a lock lives off the board too, #461/#581), and for
each stale marker it LOOKS for the item's own `item/<n>-*` PR — the worktree protocol's own server-side
proof of life — and **REFUSES** to break a lock whose PR is open (naming the PR), reaps one whose work is
genuinely dead, and fails CLOSED when it cannot read the liveness at all. The refusal is not an `if` to
forget: `Writes.reapable` (green only on `LeaseExpiredNoPr`) is the single constructor of the `Reapable`
capability `Writes.reap` consumes, so a live or unreadable claim cannot reach the delete. `--apply` gates
the destructive break — the bare form is a DRY RUN (`would reap …`) — and an OFF-BOARD claim's post-reap
column restore honestly reports `not on board (nothing to reset)` rather than claiming a reset it never
performed (case 25). New `reap_server.py` (write-capable — it records the marker DELETE and toggles the
item's open PR); 8 parity assertions + 5 Writes tests. Disposed on the record (ADR-0040 §5): the collision
between "the lease lapsed" and "the work is alive" is decided by the PR probe, not the lease clock — bash's
literal exit codes re-expressed as the properties (a refusal deletes nothing; a collect deletes exactly the
marker), counted at the HTTP layer so the #581 bug — a refusal that deleted anyway — cannot pass. Case 26's
`who` proof-of-life legs (the `livePr` field and the `STALE (#NNN OPEN)` row) landed in the off-board `who`
slice above — case 26 is now FULL.

The **`batch` off-board RESERVATION** landed next (case 25, this slice): the SCHEDULER now honours the lock
`who` reads. Disjointness is only sound if the reserved set is COMPLETE, and a claim lives off the board — a
marker on an issue whose column flip failed (the board says Ready, the lock says held), or on one the board
never listed. The board scan is blind to the second kind, so a candidate declaring the same files would be
handed a tree its holder is standing in — the exact double-book the scheduler exists to prevent. So
`Scan.snapshot` (the one read `batch`/`next`/`take` all share) now runs the SAME paginated, unconditional
open-issue scan `who`/`reap` use — bash's `active_claims` arm B (arm A, the board's In-progress rows, is
already the candidate loop) — and reserves every LIVE claim on an issue the board did not list, keyed so a
board item is never re-read. The result: `batch` schedules only the item no live marker touches, skips a
Ready item a marker actually holds (naming the worker and its lease window), and refuses to schedule over an
OFF-BOARD claim — naming the holder, its item, and the colliding path stems (`held by puffin-h11 on
FS.GG.Rendering#215 (lease frees in ~…): src/Off/Sub ⇄ src/Off`), reusing the existing `Batch`
holder/lease machinery unchanged. The off-board scan is REST (C4 — the lock never moves to the budget that
dies first). New `offboardbatch_server.py` (a two-page issue list proving the scan PAGINATES and is never
conditional at the scheduler's surface); 10 parity assertions + 1 GitHub-layer round-trip test. The
fixtures that drive scheduling grew a bare `/issues` endpoint — bash's `active_claims` fetches it on every
scheduling call, so a fixture that omitted it was under-specifying the world; where a world has no off-board
claim it answers empty, the honest scan result. **`--engine fs`/shadow/flip are untouched: they feed a
pre-built snapshot to `decide`, which never scans** — the engine's own scan is now what bash's snapshot
already carried. Case 25 stays PARTIAL: the #428 starved-queue banner over this reservation, and off-board
`inbox`, remain (see the remaining table).

The **#428 starved-queue BANNER** landed next (case 25, this slice): the AGGREGATE the per-item reasons
cannot give. In a repo where one file is nearly every item's touch-set, ONE claim serialises the whole
queue — `batch` correctly hands out nothing, and "nothing schedulable" reads exactly like an empty backlog,
so a worker goes home from a repo with work in it. This slice makes the scheduler say the queue is **BUSY,
not empty**: it names every holder (`held by: ghost-222, kite-z01, tern-y99` — who to talk to), gives the
**soonest lease** (whether the wait is worth it), and — for a lease already EXPIRED — points at `reap`
(`N of those lease(s) have EXPIRED — collect them: fsgg-coord reap …`), the one blocker a worker clears
alone. Two reservations the board scan had been blind to had to land first, both under the lock, not the
column (#461): a **STALE-but-unreaped off-board claim** now reserves (a lapsed lease is a clock; a lock is
broken only by `reap` — so `Reads.reserver` reads the lowest-id marker regardless of lease, where `winner`,
which decides IDENTITY, drops it), carrying its true expired age so the collision reads `lease EXPIRED —
reapable`; and a **MARKERLESS `In progress` row** reserves too (arm A of bash's `active_claims` — something
is evidently editing those files), but as `Unowned` — no worker to name and no lease to wait out, so a
colliding candidate is told `In progress with NO claim marker` and the banner NEVER dresses it up as a
holder "—" nor counts it among the queued-behind-claims. `Batch.starvedBanner` is the pure computation
(silent whenever work was handed out, or the queue is starved by blockers/columns — that is #440's per-item
business); `batch`/`next`/`decide` relay it to stderr. New `starvedqueue_server.py`; 10 parity assertions +
6 `Batch` + 2 `Scan` round-trip tests. Disposed on the record (ADR-0040 §5): the "EXPIRED — reapable"
verdict is decided by the lease clock at the banner, but the `reap` it points at RE-probes and refuses a
claim whose `item/<n>-*` PR is still open (#581), so the advice can never break a lock over live work.

The **`say` / `inbox` channel** landed last, and case **25 is now FULL** (this slice): the mailbox rides the
off-board scan too. `say` posts a message as an `fsgg:msg` comment on the ITEM it concerns, and `inbox`
delivers the ones addressed to a worker (or broadcast, `to=*`) across every in-flight claim. The port gap
was case 25's whole point: a claim — and the message riding it — can sit on an issue the board never listed
(a failed column flip, or one that never reached the board), so a mailbox that read only the board's
In-progress column would silently DROP a message posted on an off-board claim. So `inbox` runs the SAME
paginated, unconditional open-issue scan (arm B) unioned with the board's In-progress rows (arm A) that
`who`/`reap`/`batch` run, reads the `fsgg:msg` comments on each (`Reads.messages`, unconditional like the
lock — a 304 could hide a message posted after the cached page), and filters to new-and-for-me: `id >` a
per-worker **cursor** (`Cache.inboxCursor`/`putInboxCursor`, the bash client's `inbox-<slug>` file, so a
worker that switched engines mid-loop does not re-read old mail), `from ≠` me, and `to ∈ {me, *}`. The
cursor advances past every message SEEN (so a broadcast I sent does not resurface forever), while delivery
gates on the OLD cursor; `--peek` shows the mail and leaves the cursor. The whole thing is a pure engine
round-trip in the harness — the engine `say`s each message over HTTP and the engine `inbox`es it back, the
fixture seeding none. New `inbox_server.py` (write-capable: it stores the POSTed comment and serves it back);
7 parity assertions (off-board delivery, broadcast, the item named, cursor advance, self-filter, `--peek`
shows + does-not-advance) + 6 `Reads.messages` + 5 `Cache` cursor tests. Disposed on the record (ADR-0040
§5): the engine's `say` takes `--message`, where bash takes the message as a trailing positional — the same
already-recorded `say` divergence, re-expressed as the property (a message posted by `say` is delivered by
`inbox`); and the cursor's unreadable-file fallback is `0` (re-show old mail — noise), the OPPOSITE of the
lock's fail-closed, because a cursor read too HIGH would hide new mail.

The **`landable` verdict** landed next (case 30's `who`/`reap` legs, this slice): #581 reads WHETHER an
`item/<n>-*` PR exists; #697 reads **what it SAYS**. That blind spot pointed the tool's own destructive verb
at the best work on the board — `reap` refused a stale claim whose PR was open (right) and then offered
exactly one exit, *"close it, then reap"*, which for a green, reviewed, mergeable PR DESTROYS a worker's
finished work minutes from merge. The new pure `Landable.score` (Core) classifies a PR from its
`mergeable` + the checks on its head SHA into `green`/`conflicted`/`pending`/`red`/`unknown`, scoring the
**union of WORKFLOW RUNS and CHECK RUNS** (#720 — a run can fail with no check-runs, a check-run can fail
while its run succeeds, and a non-Actions app appears only in check-runs), dropping a **superseded** run's
suite (a `cancelled` run a later run of its own concurrency group replaced — keyed on
`path`+`event`+`head_branch`+`prs`, not `path` alone, so a `workflow_dispatch` run can never license the
drop, #703), and calling **zero live subjects `red`, never `green`** (#606 — "every check passed" and "CI
never started" are the same empty set). `Reads.prLandable` does the three REST reads and hands them to the
scorer; it returns a `PrState`, not an `IoResult`, because this is the one read whose FAILURE IS ITS ANSWER
— `unknown` is the honest fail-closed verdict that makes `who`/`reap` advise nothing on a guess, not a
masqueraded empty. On top of it, `who` now flies the right flag — `STALE (#701 OPEN — GREEN: LAND IT)` with
a first-class `prState` in `--json` and an orphan block that points at `fsgg-coord adopt`, not `reap` — and
`reap` speaks the right refusal per verdict: FINISHED work is named FINISHED and sent to `adopt` (never
"close it, then reap"), a `pending` PR is UNFINISHED ("Do NOT close it — let CI settle"), and only a
genuinely `red`/`conflicted` PR may be told to close. New `landable_server.py` (the #697 world — a green
orphan and a mid-CI one, each scored off its head SHA's runs + check-runs); 16 parity assertions + 15
`Landable` unit tests. Disposed on the record (ADR-0040 §5): the runs/check-runs reads are single-page
(the array-merging transport does not flatten these endpoints' OBJECT bodies), so a real multi-page runs
list degrades to `unknown` — fail closed, never a wrong verdict — with pagination (#547) a follow-up; the
`adopt` command itself (case 30 parts 3–5), the lazy-`mergeable` re-read, and case 31's superseded-run
scoring are the remaining case-30/31 legs.

The **`adopt` command** landed next, and case **30 is now FULL** (this slice): a worker may LAND another
worker's orphaned PR through one verified command that cannot be talked into landing anything else. `adopt`
is a **GATE IN FRONT OF `claim`** — the transfer itself is `claim`'s (the same comment-id CAS, the `prev`
carry #481, the #516 second-hold refusal), so there is no second lock to carry the bug. The gate reads the
lock off the item and refuses everything that is not *finish somebody's finished work*: a **LIVE claim** is
not an orphan (`held by a LIVE claim` — taking it is a steal, and `--force` is the honest way to say so); an
**expired claim with no open PR** is merely dead (`no finished work to adopt` — `reap` it and claim
normally); a PR that is not green is not finished (`CONFLICTED`, `checks RUNNING`, or **`NOT green`** for the
zero-checks #606 case); and `PrUnknown` refuses too, because adopting on a guess launders the destructive
act this command replaces. Only a `PrGreen` verdict transfers — and only then does the ADOPTED epilogue
print (`Do NOT rebuild it, and do NOT close PR #NNN. Land it: …`), because the "GREEN and MERGEABLE" line is
a **precondition report, not a success banner**: the `claim` beneath it can still lose the CAS. This slice
also completed **`Reads.prLandable`'s lazy-`mergeable` re-read** — a present `null` (GitHub computes
mergeability in a background job) is re-read a bounded 3 times (~1s apart, env-tunable so the test harness
drives the fixture's read-count flip without the wall-clock), so a PR that reads `null` then `false`
resolves to `CONFLICTED`, never landable; an ABSENT field is not re-read (it will not appear on a second
look). New write-capable `adopt_server.py` (the six #697 worlds — green transfer, conflicted, zero-checks,
live, no-PR, lazy, pending); 13 parity assertions + 1 Options parse test. Disposed on the record (ADR-0040
§5): the engine's transfer WINS the CAS (the adopter becomes the sole live holder) but does not delete the
orphan's stale marker (left for `reap` — it is neither `winner` nor `reserver`, so the lock is correct) nor
post the adoption `say` to the orphan's worker; both are follow-ups that do not affect the lock. Case 31's
superseded-run scoring (`Landable.supersede` exists and is unit-tested; case 31 drives it against a
force-pushed PR's `cancelled` suites) is the remaining case-30/31 leg.

Case 31's **superseded-run verdict** landed next (#720, this slice), and case **31 is now PARTIAL**: the
#697/#720 verdict got a first-class home — a standalone **`landable <pr> --repo` command** that prints ONE
word (`green`/`conflicted`/`pending`/`red`/`unknown`) on stdout and puts the DECISION in the exit code, so a
poll loop tells "keep waiting" from "stop" without parsing prose. It is the read `who`/`reap`/`adopt` already
make (`Reads.prLandable` → `Landable.score`), surfaced on its own so the verdict has ONE home and the recipe
§5 that re-derived it in ~40 lines of jq — wrong four times (#547/#606/#698/#720), fixed in a COPY each time
because nothing executes a recipe — can NAME the gate. Case 30's fixtures carried SINGLE runs; case 31's
world is MULTIPLE runs on one SHA, and this slice drives all ten #720 legs through the engine over HTTP
(`landable_super_server.py`, one PR per leg): a **cancelled run REPLACED by a later run of its own
concurrency group is superseded** and dropped with its check-runs (`green`), a cancelled run **nobody re-ran**
is still a finding (`red`, the drop is not a hole), a **`workflow_dispatch` run supersedes nothing** (a
different `github.ref` — keying on the path alone would count its vacuous green, #703), **zero runs is an
empty subject** (`red`, #606 survives the rewrite), an **in-flight run is `pending`**, a **failing third-party
check** (in the check-runs only) still reds the PR (the Actions rollup must not go blind), a **`startup_failure`
run with NO check-runs** reds a PR whose sibling workflow is green, and a **failed check-run whose run
concluded `success`** (job-level `continue-on-error`) reds it too (the verdict is the UNION of runs and
check-runs). The exit code is the poll-loop contract (`/pnext-item` §5, #724): green `0`, pending the ONE
retryable code, a red/conflicted verdict a distinct do-not-wait code, unknown fail-closed. New engine surface
is thin — `Reads.prLandable` + `Landable.name` + the exit map — because the SCORING already existed and is
unit-tested (15 `Landable` tests, including the three supersession cases). 12 parity assertions + 1 Options
parse test. Disposed on the record (ADR-0040 §5): (a) the exit codes — bash numbers the poll loop `0/3/1`
(green/pending/red), the engine keeps `3 == red` across every verdict command (`done`/`decide`/`adopt`) and
gives PENDING its own `7`, so the LITERALS differ while the PROPERTY (green `0`; pending a distinct retryable
code; red a distinct do-not-wait code) does not; (b) leg 9's argv-128KB cap (`MAX_ARG_STRLEN`) is bash's —
its rollup piped both lists to jq through argv and a real run set died with "Argument list too long"; the
engine reads the JSON off `HttpClient`, so the failure mode is STRUCTURALLY ABSENT, and the fat payload is
served only to prove the engine rolls a real-sized body up to `green`.

The **`landable --wait` poll loop** landed next (#724), and case **31 is now FULL** (this slice): the single
read becomes a POLL that carries the one thing a single-shot verdict cannot — refusing a PREMATURE green.
GitHub registers a PR's runs over 20-60s, so the subject set is empty at first (a `red` that is really "CI
has not started YET", #606's registration race) and then GROWS (an early all-green is a PARTIAL rollup whose
failing check has not been CREATED yet — #606's defect at one remove, and the one that MERGES A BAD PR). The
break-vs-wait decision is a pure, unit-tested `Landable.settled`: `conflicted`/`unknown` settle at once (no
waiting fixes a conflict); a `red` settles only with a subject to be red ABOUT (`n > 0`, else keep waiting
for the runs to register); a `green` settles only once the subject count has STOPPED GROWING (`n > 0 && n =
prev` — stable across two consecutive polls); `pending` never settles. `Reads.prLandableN` surfaces the
subject count (`Landable.scoreN`) the loop polls on — `prLandable` is now that, with the count dropped — and
the `landable` command threads the previous count through a `--tries`/`--interval` loop, keeping the LAST
verdict for when the tries run out (the honest #606 red if the runs never registered). New stateful
`landable_wait_server.py` (sha810's runs/checks GROW on the second read, exactly as GitHub schedules them); 4
parity assertions (settled-green, registration-race, growing-set, conflicted-at-once) + 12 `Landable`
(`scoreN` + `settled`) tests + 3 Options parse tests. Disposed on the record (ADR-0040 §5): the exit codes
are the engine's own (`--wait` green `0`, red/conflicted `3`), where bash numbers green/red `0/1` — the
PROPERTY (green `0`; red/conflicted a distinct do-not-wait code) re-expressed, not bash's literals.

The **`Blocked by` WRITE gate** landed next (case 13, this slice): `Blocked by` is a TYPED dependency edge,
but Projects v2 has no dependency field — so it is TEXT, and in bash it drifted back into a resolution LOG
("RESOLVED: #8 closed, shipped @d80a8ae") that `.blocked`, which reads the field back as refs, could not
parse, so an item the board DISPLAYED as blocked reached the scheduler UNBLOCKED. The gate is on the WRITE:
`set-field <issue> 'Blocked by' <value>` canonicalizes every accepted form (`owner/repo#n`, `repo#n`, a bare
`#n` adopting the item's OWN owner/repo, an issue URL) to one `owner/repo#n`, de-dupes refs that canonicalize
alike (first occurrence wins), and — the point — REFUSES prose: a delivery log, the inverted `blocks X` edge,
and a ref TRAILED by prose all fail the anchored per-token match, and the refusal REDIRECTS (`set-field
<issue> Status Blocked` — "the item IS blocked" is a Status, not a dependency), while the `-`/`none`
placeholder is refused toward CLEARING (`'Blocked by' ''`). An empty value clears via the distinct clear
mutation (never an empty `--text`, a no-op on the real API). The rule is a pure, unit-tested
`Blockers.canonicalizeBlockedBy` (one home, #485), and it runs in a shared `gateField` on BOTH set-field
surfaces — the single write and `--batch` — BEFORE any board read, so a refused value spends ZERO GraphQL
(the budget that dies first). New `blockedby_server.py` records each field mutation (the field, SET-vs-CLEAR,
and the text — mapped from the `fieldId` variable) and counts the GraphQL requests, so "a refused write
spends no GraphQL" is a request count of ZERO; 11 parity assertions + 12 `Blockers` unit tests. Disposed on
the record (ADR-0040 §5): the `--text FS-GG/...` wording is bash's `gh`-log form, re-expressed as the
PROPERTY (the canonical value the mutation carries) one transport under; and the refusal exit is the engine's
`ExitError`, bash's literal 1 re-expressed as the property (a refused write exits non-zero and writes
nothing). Case 13 stays PARTIAL: the `issues` short-id command (#446) and `reap`'s #480 checkout scope remain
(see the remaining table).

The **`issues` short-id command** landed next (case 13, this slice): the last repo-taking command taught to
resolve a registry short-id (#446). `issues` lists a repo's issues over REST with ETag revalidation — the
read both coordination skills advertise as THE way to read issues WITHOUT spending GraphQL (a 304 costs
nothing, #418) — but it was the ONE command that took its `<repo>` argument VERBATIM where every sibling ran
it through `resolve_repo`. So `issues game` asked GitHub for `repos/FS-GG/game` and 404'd while `--repo game`
resolved everywhere else, and the natural recovery from that 404 is `gh issue list` — 2 GraphQL points a
call, the exact budget the command exists to save. The engine now resolves the arg the same way as the rest:
an explicit `owner/repo` splits and passes through untouched (an owner is authoritative), a bare short-id
maps through the shared `resolveRepo` to `owner/<repo-name>` — so `issues game` reads `FS-GG/FS.GG.Game` and
the bare `FS-GG/game` never reaches GitHub. The read is a NEW `Reads.issues` primitive that finally consumes
the ETag body cache built for it (`Cache.getETag`/`getBody`/`putBody`, unused until now): CONDITIONAL by
design (unlike the claim scan's `openIssues`, whose subject is the lock and must never be served a stale
304, #461 — a listing has no marker to hide), so a repeat read sends the stored validator and a 304 serves
the body from cache for zero fresh cost, and `--refresh` drops the validator to force a full re-read. It
emits the raw JSON array — the caller projects it with real jq. New `issues_server.py` records the
`owner/repo` (and state/label/If-None-Match) of every `/repos/*/issues` request, so the corpus's
`issue-list FS-GG/<repo>` `gh`-log assertion becomes "the fixture was asked for `FS-GG/FS.GG.Game`, NEVER
`FS-GG/game`", one transport under; the fixture serves NO GraphQL, because `issues` is a pure REST read that
never bootstraps the board. 9 parity assertions + 3 `Reads.issues` unit tests (the 200→cache→304 round-trip,
`--refresh`, fail-closed) + 4 Options parse tests. Disposed on the record (ADR-0040 §5): bash's `--jq EXPR`
is an ERGONOMIC — the engine emits the raw array and the caller pipes it to real jq (the Json-is-contract
rule), so `issues … | jq` IS the port of `issues … --jq …`, and the engine refuses an unknown `--jq` flag.

Case 13's **`reap` #480 checkout scope** landed last, and case **13 is now FULL** (this slice): the last leg
was the DESTRUCTIVE one. `reap --apply` is the ONE worker command that DELETES another worker's state — it
breaks their claim marker — so an org-wide default is the worst place to keep one: a janitor run from a
`.github` checkout would collect claims in five repos it was never pointed at (the corpus, case 13 line 54,
asserts exactly this on the DRY RUN — a bare reap from an SDD checkout must NOT name a Templates/Rendering/…
claim). Like its siblings (`next`/`take`/`batch`/`who`, #480), a bare `reap` now takes the repo of the
checkout you are standing in — read FREE and offline from `git config remote.origin.url`, never `gh repo
view` — and considers ONLY that repo's claims; an explicit `--repo` (a registry short-id resolved) wins; and
OUTSIDE a checkout `reap` REFUSES (`--repo required`) before any network read rather than fall back to the
org-wide scan. The engine change was already in place (`Reap` rides the shared `scopedRepo` resolver, and the
`reap` command's own `--repo required` guard is the refusal) — this slice PROVES it over HTTP from FAKE
CHECKOUTS against a MULTI-REPO world (`reap_scope_server.py` — a dead stale claim in SDD AND Rendering, so a
leak is visible), two ways: the dry-run line names the checkout's repo, and the fixture's `/_requests` ledger
shows which repo's `/issues` was fetched — the corpus's "considers only THAT repo's claims" (`gh`-counted)
one transport under. 7 parity assertions (bare-SDD names SDD not Rendering + only SDD's issues fetched, the
remote-read Rendering leg, explicit `--repo` wins + short-id resolves, the outside-a-checkout refusal, and
nothing deleted across every leg). No new unit tests — the resolver and the guard were already covered by the
`#480` scope section (`next`/`take`/`batch`) and the reap `--repo required` behaviour.

Case 24's **lock-fails-closed adversarial reads** landed next (this slice): the interleavings in which two
workers could end up believing they hold ONE item — the failure the whole ADR-0027 protocol exists to
prevent — but the half the engine ALREADY implements, so a parity-proof slice. A claim marker is only a
marker at the START of a comment body (`^<!--\s*fsgg:claim`), so a `fsgg:msg` that merely QUOTES one in prose
does not forge a lock — the item is still claimable (leg e). A marker we cannot parse a worker out of FAILS
CLOSED — it BLOCKS the item rather than reading as free, because a lock you cannot read is still a lock (leg
f, engine "unparseable lock" for bash's "unparsed-marker"). An expired worker cannot HEARTBEAT its claim back
to life once another worker legitimately holds the item — it is named the new holder and told to STOP working
(leg c, the resurrection bug), and an expired lease is refused even when nobody else took the item (leg d,
"EXPIRED — re-claim"); the refused renew patches NOTHING (proven at the fixture's `/_patches` ledger). And
the two CAS re-read LOSSES: a transient read FAILURE on the re-read withdraws the marker we just posted
rather than orphaning it (leg g, "removed our marker" proven at `/_deletes`), and an EMPTY re-read — our own
marker vanished — is a LOSS too, never a lock announced on an observation we did not make (leg i, "marker
vanished"). New `casadversarial_server.py` (one FS.GG.SDD world; legs g/i MUTATE it — `claim` POSTs, the
re-read faults or comes back empty, and the withdraw DELETEs the posted marker); 13 parity assertions, no new
unit tests (the marker anchor, the `BlockedByUnparseableMarker` outcome, the `verifyHeld` fail-closed, and
the `withdraw` loss path are all already covered in `Reads`/`Writes` unit tests). Disposed on the record
(ADR-0040 §5): where the engine's wording differs from bash's literal — (c) `held by heron-b71` vs `worker
'heron-b71' does`, (d) `claim --force` vs `fsgg-coord claim`, (f) `unparseable lock` vs `unparsed-marker`,
(g) `could not take … a LOSS` vs `removed our marker`/`nothing was claimed` — the PROPERTY is asserted (name
the holder; point at re-claiming; BLOCK the item; DELETE the posted marker and claim nothing), one transport
under. Case 24 stays PARTIAL: the MUTATING interleavings remain (see the remaining table).

Case 24's **`say --to` normalization** landed next (leg n, this slice) — the smallest genuine engine change of
the remainder, and a lock-safety one despite looking cosmetic. Worker ids are `slug()`'d at creation, and
`inbox` matches a message's `.to` against a worker id by EXACT string, so an unslugged `say --to Heron-B71`
posts a message addressed to `Heron-B71` that its real recipient — `heron-b71` — can never see: the message
lands on the item but is addressed to an id nobody holds. The engine now runs the `--to` target through the
SAME `Identity.slug` that creates ids (the #485 one-home — `slug` was `private`, now a documented public
val), keeps `*` (anyone holding the item) as the one literal that is not a worker id, refuses a target that
slugs to empty (`not a usable worker id`), and WARNS on stderr when it changed anything (`addressing worker
'heron-b71' (normalized from 'Heron-B71')`). The parity proof rides the existing `inbox_server.py` as a pure
engine round-trip: the engine `say`s `--to 'Heron-B71'` and the worker `heron-b71` INBOXES it back — delivery
IS the proof the marker was normalized to the id `inbox` matches (had the engine posted `to=Heron-B71`
verbatim, the exact-string match would never fire), and the existing broadcast assertion proves `*` stays
literal. 2 parity assertions + 4 `Identity.slug` unit tests. Disposed on the record (ADR-0040 §5): the warning
prefix is `fsgg-coord-engine:` where bash's is `fsgg-coord:`, re-expressed as the property (the message names
the slug it normalized to and the original it came from).

The clean "engine already matches bash by construction" cases are largely ported; what remains clusters into
the **larger port gap** of `reap`/`claim`'s MUTATING adversarial legs (case 24 — stale-marker collection +
notify, `reap` re-verify-before-delete, the shared-id re-claim warning) and the documented GC-on-transfer
follow-up. The verify-paths repo-boundary divergences (case 23's SKIP-exit code
and the absent `gh repo view` fallback) are now disposed on the record in the harness, and the call-counting
transformation is demonstrated end-to-end by case 10.

### D.2 — Cut the shim

Replace `scripts/fsgg-coord` with the ~40-line resolver of ADR-0034 §4.4: resolve `fs.gg.coord.cli` from
`.config/dotnet-tools.json`, exec it, pass through args and exit code. The `kind: client` kit row still
digests, still byte-copies, still byte-compares — none of that machinery changes (why Option D was
chosen).

- **Exit:** the corpus (D.1) is green *through the shim* on `.github@main`; every workflow that shells
  out is green (C2); the restore gate is green (C3).

### D.3 — Green in all six receivers

Roll the shim to the six `receives: coordination-kit` repos via the existing digest → byte-copy →
byte-compare fabric. No receiver edits — the shim and its corpus are distributed like every other kit
artifact.

- **Exit:** the corpus is green through the shim in **all six receivers**.

### D.4 — Delete bash, dispose the five differential assertions on the record

Delete the ~4,000 lines of `bash scripts/fsgg-coord`. `--engine=bash` is removed *because there is no
bash left to be*. Per ADR-0040's "Phase D contradiction, and its resolution", the five `51-fs-flip.sh`
differential assertions are **retired on the record** — not silently dropped:

| # | assertion | disposition |
|---|---|---|
| 1–2 | `fs` returns bash's items / same exit code | **subsumed** by the ADR-0038 defect-corpus-against-`fs` (a precondition of D.1) |
| 3–5 | `--engine=bash` is byte-exact / never consults the engine | **retired** — the escape hatch is the thing being deleted |

Land a `51-fs-flip.sh` (or sibling manifest) that **records** the five and their disposition, so the drop
is a decision in the diff, reviewable, never a silent gap. **A silently shrinking gate is the failure; a
documented retirement is not.**

- **Exit:** bash is gone; the corpus is green through the shim in all six receivers; the disposition
  manifest is on the record.

## 6. Risks and rollback

- **The configurable-API-base corpus is the risk.** If an assertion genuinely cannot be expressed at the
  HTTP layer, that is *information about the assertion* (C1) — surface it, do not drop it. The parity
  harness proves the shape is achievable; the risk is breadth, not feasibility.
- **Rollback is per-phase.** D.1 adds a harness and changes nothing shipped. D.2's shim is revertible
  (restore the bash file) *until D.4*. **D.4 is the one-way door** — it is taken only after D.1–D.3 are
  green in all six receivers, which is the whole point of gating it on a computable condition rather than
  a calendar.
- **The lock must not move to the budget that dies first (C4).** Any temptation during D.1 to satisfy a
  budget assertion by moving a REST read onto GraphQL is the exact regression ADR-0034 forbids.

## 7. Definition of done

- [~] D.1 — the full corpus green through the engine locally, call counts intact, shadow/flip still green.
      **In progress: 25 full + 1 partial of 27 cases** ported to `tests/coord-engine-parity/` (~407
      assertions); the rest remain (see the §5 D.1 ledger). Case 24 (the last partial) has begun to close —
      its **lock-fails-closed** adversarial reads (a quoted marker does not forge a lock, a malformed marker
      BLOCKS, an expired worker cannot resurrect its claim, and a failed/empty CAS re-read is a LOSS that
      withdraws its own marker) now drive through the engine over HTTP (`casadversarial_server.py`), and its
      **`say --to` normalization** landed too (a mis-cased target is slugged to the id `inbox` matches via the
      now-public `Identity.slug`, `*` stays literal, an unslugged target is WARNED about — proven as an engine
      round-trip through `inbox_server.py`); the lock's MUTATING interleavings are what remain of it. Case 14 is now FULL — its whole `lint` command
      (schedulability + epic-graph), its `done --flip` epic rollup, and its `done` PR-provenance legs
      (#342 latest-merged closer, #558 commit-subject/commit closer, #543 `--pr` can't launder a mention) all
      landed; case 34 is now FULL too — the read-only `overlap` command (#353 repo-scoped collision) plus
      `widen`'s collision-DETECT-and-notify half (re-check the new touch-set against the same repo's live
      claims and notify each colliding worker, reusing the shared `activeCollisions` scan); the
      **`reap` command** (#581) landed — an expired lease is EVIDENCE of abandonment, never
      PROOF, so `reap` REFUSES to break a lock whose `item/<n>-*` PR is open (the leg that reaped live work
      twice), reaps one whose work is dead, and fails closed on an unreadable liveness; the `#581` refusal is
      structural (`Writes.reapable` is the only constructor of the capability `Writes.reap` consumes), and
      `--apply` gates the break; and the **off-board `who` scan** now makes case 26 FULL and lands case 25's
      `who` legs — `who --repo` reads the LOCK off the board (a paginated, never-conditional open-issue scan
      unioned with the board's In-progress rows), reporting an off-board HELD claim, a board-says-Ready-but-held
      claim, an In-progress markerless UNCLAIMED, and the #581 proof-of-life STALE row (`livePr` /
      `STALE (#NNN OPEN)`) — never a chatty markerless issue; and the **`batch` off-board RESERVATION** then
      taught the SCHEDULER (`batch`/`next`/`take`) to run that same scan (bash's `active_claims` arm B) so it
      reserves an off-board claim's touch-set — schedule only the item no live marker touches, skip a
      board-Ready item a marker holds, refuse to schedule over an off-board claim naming its holder/item/paths
      and lease window; REST-only (C4), and `--engine fs`/shadow/flip are untouched (they `decide` a pre-built
      snapshot, which never scans); and the **#428 starved-queue BANNER** then made a starved queue say it is
      **BUSY, not empty** — a STALE off-board claim and a MARKERLESS In-progress row now reserve too (the lock,
      not the column, #461), and when that leaves nothing to hand out the queue names every holder, the soonest
      lease, and — for an EXPIRED lease — the exact `reap` (`Batch.starvedBanner`, silent on a healthy queue).
      Case 25 is now FULL — the **`say` / `inbox` channel** ported last: `inbox` runs the same off-board
      scan `who`/`reap`/`batch` do, reads the `fsgg:msg` comments (`Reads.messages`, unconditional), and
      delivers new-and-for-me mail past a per-worker cursor (`Cache.inboxCursor`, the bash `inbox-<slug>`
      file), so a message posted on an OFF-BOARD claim is delivered and `--peek` shows without consuming. Ten engine
      defects the port was *for* closed along the way, plus the
      `verify-paths --issue` repo-boundary port gap (#479/#494) and the #430 git-remote repo default, which
      together close case 23 in full (the cross-repo closing-ref SKIP ported alongside), the #419
      twin-session refusal + shared-id warning, which closes case 44 in full, and the #418 resolver cache
      (board id map day-cached, item ids cached forever), which closes case 10 in full — the §3
      call-counting transformation, re-expressed as HTTP request counts. Case 30's `who`/`reap` legs then
      landed the **`landable` verdict** (#697/#720): `Landable.score` classifies a PR from `mergeable` + the
      union of workflow runs and check runs (superseded suites dropped, zero-checks is `red` #606), so `who`
      flies `STALE (#NNN OPEN — GREEN: LAND IT)` with a `prState` field and `reap` refuses FINISHED work as
      FINISHED and sends it to `adopt` — never the "close it, then reap" loaded gun. Case **30 is now FULL**:
      the **`adopt` command** landed — a GATE in front of `claim` that lands a green, mergeable orphan and
      refuses everything else (live claim, no PR, conflicted, zero-checks/`NOT green`, pending, unknown),
      with `prLandable`'s lazy-`mergeable` re-read so a `null`-then-`false` PR resolves to `CONFLICTED`.
      Case **31 is now FULL**: the #720 superseded-run verdict got a first-class home — a standalone
      **`landable <pr> --repo` command** (one verdict word on stdout, the decision in the exit code) — and
      all ten #720 legs drive through the engine over HTTP, including the MULTI-run-on-one-SHA supersession
      (a cancelled run replaced by a later run of its own concurrency group is superseded and dropped with
      its check-runs; a `workflow_dispatch` run supersedes nothing, #703; zero runs is `red`, #606). The
      exit code is the poll-loop contract (green `0` / pending a distinct retryable code / red a distinct
      do-not-wait code / unknown fail-closed). And **`landable --wait`** (#724) then landed the poll loop that
      does not believe an early green: a pure `Landable.settled` keeps waiting while zero runs have registered
      (the #606 registration race) and believes a `green` only once the subject count (`Reads.prLandableN` →
      `Landable.scoreN`) has STOPPED GROWING across two consecutive polls (the partial-rollup trap that merges
      a bad PR), while `conflicted`/`unknown` return at once — proven through a stateful
      `landable_wait_server.py` whose run set grows on the second read. Case 13's **`Blocked by` WRITE gate**
      then landed: a pure
      `Blockers.canonicalizeBlockedBy` reduces every accepted form to one `owner/repo#n`, de-dupes, and
      REFUSES prose (a delivery log, the inverted `blocks X`, a ref trailed by prose — redirecting "the item
      IS blocked" to a Status), while a `-`/`none` placeholder is refused toward clearing; it runs in a
      shared `gateField` on BOTH set-field surfaces BEFORE any board read, so a refused write spends ZERO
      GraphQL (`blockedby_server.py` counts the requests; 11 parity + 12 `Blockers` unit tests). The
      **`issues` short-id command** (#446) then landed the LAST repo-taking command taught to resolve a
      registry short-id: `issues game` now reads `FS-GG/FS.GG.Game` (never the `repos/FS-GG/game` 404 that
      sent a worker to the budget-burning `gh issue list`), via a new `Reads.issues` primitive that finally
      consumes the ETag body cache built for it — CONDITIONAL, so a repeat listing is a free 304 (#418),
      where the claim scan's `openIssues` must never be (a stale 304 could hide a lock, #461). `issues_server.py`
      records the resolved `owner/repo` of every REST request; 9 parity + 3 `Reads.issues` + 4 Options tests.
      Case **13 is now FULL** — its last leg, **`reap`'s #480 checkout scope** (the DESTRUCTIVE worker
      command), is proven: a bare `reap` takes the repo of the checkout you are standing in (`git config
      remote.origin.url`, FREE/offline) and considers ONLY that repo's claims — from an SDD checkout it names
      SDD's claim and fetches only SDD's `/issues` (never Rendering's), from a Rendering checkout only
      Rendering's, an explicit `--repo` wins, and OUTSIDE a checkout it REFUSES (`--repo required`) rather than
      fall back to the org-wide scan that once deleted across five repos. The engine already rode the shared
      `scopedRepo` resolver; this slice proved it over HTTP from FAKE CHECKOUTS against a MULTI-REPO
      `reap_scope_server.py` (a dead stale claim in SDD AND Rendering, a leak visible in the dry-run line and
      the `/_requests` ledger). 7 parity assertions; no new unit tests (resolver + guard already covered).
- [ ] D.2 — the shim cut; corpus green through it on `.github@main`; C2 + C3 green.
- [ ] D.3 — green through the shim in all six receivers.
- [ ] D.4 — bash deleted; `--engine=bash` removed; the five `51-fs-flip.sh` assertions disposed of on the
      record; the `engine-retires` label and epic [#729](https://github.com/FS-GG/.github/issues/729)'s
      "retires 22 of 40" re-derived honestly (ADR-0040 Consequences).

---

*This plan executes ADR-0040 Phase D. It does not amend it. Where the two differ, the ADR governs.*
