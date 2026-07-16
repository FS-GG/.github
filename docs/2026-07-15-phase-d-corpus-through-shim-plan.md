# Phase D — the corpus through the shim, and the deletion of bash

**Date:** 2026-07-15
**Owner:** `.github` (the coordination engine)
**Governs:** the execution of [ADR-0040](adr/0040-port-the-io-layer.md) Phase D
**Status:** **COMPLETE — D.1 through D.4 have all landed. Bash is deleted; the port is done.** Phases A–C have landed. The
corpus-through-engine parity harness grew from the prototype to **all 27 of 27 corpus cases** (~445
assertions); the full corpus drives the engine over HTTP, green, with the call counts intact. D.2 landed in
two slices: slice 1 ([#831](https://github.com/FS-GG/.github/pull/831)) landed the ADR-0034 §4.4 shim as a
proven artifact green THROUGH it; **slice 2 — THE SWAP — made `scripts/fsgg-coord` BE the shim**, preserving
the ~7,132-line bash verbatim at `scripts/fsgg-coord-bash` for the `50`/`51` differential gates until D.4.
Everything that executes `scripts/fsgg-coord` now runs the engine, and (C2/C3 having readied the receivers:
the distributed manifest declares the published `fs.gg.coord.cli` 0.1.1) the merge's auto-propagation WAS the
D.3 delivery. **D.3 is now COMPLETE** — the swap-merge fired `coordination-propagate` (10:31Z 2026-07-16),
which opened/force-updated the rolling `coordination-kit/sync` PR in each of the six receivers with the shim's
bytes; every one went green and merged on its own required checks (three small receivers — Templates, Game,
Audio — landed at once; SDD [#465](https://github.com/FS-GG/FS.GG.SDD/pull/465)→`559efdc`, Governance
[#224](https://github.com/FS-GG/FS.GG.Governance/pull/224)→`22b788d`, Rendering
[#834](https://github.com/FS-GG/FS.GG.Rendering/pull/834)→`76df7e8` finished their native build+test `gate`
minutes later). All six now carry `scripts/fsgg-coord` **byte-identical to canonical**
(`sha256 3b884ccd…`), `coordination-coherence` green on each `main`, zero open sync PRs. Receivers only
*byte-compare* the shim (`coordination-coherence.yml`); they never execute it in CI — the exec-the-client
workflows (`touch-set-drift.yml`) live only in `.github`, so D.3 was, as designed, the *verification that
they went green*, not a rollout with its own execution surface. **D.4 is now COMPLETE — the one-way door
is through.** The ~7,132-line `scripts/fsgg-coord-bash` monolith and the entire `tests/fsgg-coord/` shell
corpus (all 29 cases, incl. `50-shadow-engine`/`51-fs-flip`) are deleted; `--engine=bash` is gone *because
there is no bash left to be* (the shim never parsed `--engine`; the flag lived only in the monolith). The
five `51-fs-flip` differential assertions are recorded, with their disposition, in a standalone manifest —
[the D.4 differential disposition](2026-07-16-d4-differential-disposition.md) — so the drop is a decision in
the diff, never a silent gap (1–2 subsumed by the ADR-0038 corpus-against-`fs`, now the ~445-assertion
`tests/coord-engine-parity/` corpus; 3–5 retired with the escape hatch). The three gates that had been
repointed at `-bash` in D.2 now interrogate the ENGINE: `recipe-landable` greps `src/FS.GG.Coord.Cli/`
(`Options.fs` routes the `landable` token, `Client.fs` dispatches it), `generate-projections` dropped its
second-engine grammar cross-check (one engine, one home), and the `touch-set-drift` selftest compares the
gate's `FSGG-PATHS` vocabulary against `Client.fs`'s markers. `fsgg-coord-selftest.yml` (which drove the
shell corpus against bash) is deleted, and `coord-engine.yml` lost its shadow step. The shim's own bytes
changed (its doc-comment no longer references a deleted `-bash`), so `repos.lock` was relocked — a
kit-client-content change, not schema growth, which propagates to the six receivers on merge exactly as the
D.2 swap did. Green end-to-end: parity 445/445, shim 5/5, e2e 6/6 + writes 17/17, Core/GitHub/Cli
183/217/92, projections/touch-set-drift/recipe-landable/repos-registry selftests all green.
Case 31 is now FULL — its #720 superseded-run verdict drives through the engine's first-class `landable`
command, and its #724 `--wait` poll loop (which never believes an early green — it waits for the run set to
STOP GROWING) landed on top of it. Case 13 is now FULL too — its last leg, `reap` (the DESTRUCTIVE worker
command) scoping to the checkout you are standing in (#480), is proven: a bare `reap` from an SDD checkout
considers only SDD's claims, from a Rendering checkout only Rendering's, and outside a checkout it REFUSES
rather than fall back to the org-wide scan that once deleted across five repos. **Case 24 is now FULL** — its
last legs, the lock's MUTATING interleavings on `reap` and `claim`, land: `reap` RE-VERIFIES a stale marker
against a fresh read immediately before breaking it (a holder that heartbeated between the scan and the delete
is SKIPPED — the one way `reap` could itself cause the double-hold it exists to clean up) and REPORTS a failed
delete rather than swallowing it (the marker stands, and the worker is never told it was released over a lock
that still holds); and the shared-id `claim` RE-CLAIM renews its own live marker IN PLACE (a PATCH, not a
duplicate) and WARNS that it adopted a lock on the strength of an id alone, without running the CAS.
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
`tests/coord-engine-parity/` harness (~433 assertions across **26 of 27 corpus cases**, 29 fixture
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

**Progress (as of the case-43 slice, 2026-07-16 — D.1 COMPLETE).** The harness was grown one defect/case
at a time — each PR titled `parity: … (case N)` (the engine already matched bash — port the slice) or
`fix(engine): … (#NNN)` (a real port gap — fix the engine, then prove it). **ALL 27 of 27 cases are fully
covered** — the 27 being the full corpus's 29 minus `50-shadow-engine`/`51-fs-flip`, which are the
differential harness D.4 disposes of, not engine-behaviour cases:

| covered | case | note |
|---|---|---|
| ✓ | 10, 11, 12, 13, 14, 15, 20, 21, 22, 23, 24, 25, 26, 30, 31, 32, 33, 34, 35, 40, 41, 42, 43, 44, 45, 46, 52 | see the parity ledger in `tests/coord-engine-parity/run.sh` |

Case **24 is now FULL**: the `--issue` boundary (#479/#494) and cross-repo CLOSING-ref SKIP (shared with 23),
the lock's **fail-closed** adversarial reads (forged/malformed markers, heartbeat resurrection + expired-lease
refusal, failed/empty CAS re-read), `say --to` normalization (leg n), `overlap`'s `paths_of` fail-closed (leg
k), `claim`'s **stale-marker COLLECTION + notify** (legs a, b, l), and now the lock's **MUTATING interleavings**
(legs h, m, j): `reap` RE-VERIFIES a stale marker against a fresh read immediately before the delete (a claim
heartbeated between the scan and the delete is SKIPPED, leg h) and REPORTS a failed delete rather than
swallowing it (the marker stands; the worker is never notified over a lock that still holds, leg m), and the
shared-id `claim` RE-CLAIM renews its own live marker in place (a PATCH, not a duplicate) and WARNS that it
adopted a lock on an id alone without running the CAS (leg j).

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

**Remaining: NONE.** Case 43 (kit-digest-and-argv) landed as the last slice — the kit-digest obligation is
now OBSERVED off the tree in a pure `Core.Kit` wired into `widen`, and the #497 argv-128 KiB cap is disposed
on the record as structurally absent (the engine reads bodies as JSON off `HttpClient`, never argv) while
still proving `who` reads a >128 KiB candidate set. Every one of the 27 corpus cases now drives the engine
over HTTP, green.

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

Case 24's **`overlap` `paths_of` fail-closed** landed next (leg k, this slice) — the other half of the same
guarantee `claims_of` already carries: `claims_of` refuses to guess the LOCK state from a failed read, and the
TOUCH-SET is the half a scheduler compares AGAINST it. An empty touch-set reads as "disjoint from everything",
so a body read we could not complete — a rate limit, a network fault — must NOT collapse to an empty set: that
is #266's fail-open one subtree down, and it would let the scheduler hand out work overlapping a held item. The
tell the corpus greps for is WHICH diagnosis comes out. `overlap`'s subject touch-set now reads through a
shared `failSchedule`, which — on a body-read `Error` — swaps the generic IO explain for the scheduler refusal
`cannot read the touch-set on <owner/repo#n> (rate limit? network?) — refusing to schedule against an unknown
touch-set.`, while CARRYING the `IoError`'s own exit code (a rate limit stays `ExRate`), so a failed read is
never rendered as the empty-but-successfully-read DISJOINT and never as `no 'Paths:' touch-set declared`. Only a
SUCCESSFUL read with no `Paths:` is the honest empty DISJOINT. The parity proof rides `overlap_server.py` with a
new `OVERLAP_FAIL_ISSUE` toggle (500 on that issue's body read, standing in for bash's `GH_FAIL_ISSUE_GET=94`):
`overlap 403 405` faults on 403's read and must print "refusing to schedule", must NOT print "declared
nothing", and must NOT fall through to a DISJOINT. 3 parity assertions, no new unit tests (`failSchedule` is a
private message/exit-code routing at the CLI surface, proven at the HTTP layer as the fail-closed legs c–i
were). Disposed on the record (ADR-0040 §5): bash's `die` exits 1 where the engine keeps the read's own exit
code — the property is the refusal SENTENCE (which the corpus greps under `|| true`), not the literal.

Case 24's **`claim` stale-marker COLLECTION + notify** landed next (legs a, b, l, this slice) — the FIRST of
the MUTATING legs, and the one that closes the documented GC-on-transfer divergence. A stale marker (a lapsed
lease) is NOT `Reads.winner`'s concern — `winner` filters to LIVE markers, so a stale claim reads as free and
`claim` posts over it. The bug the port carried: it then LEFT the stale marker in place. An ignored stale
marker is exactly what a later `heartbeat` resurrects underneath the new holder — two live markers, one item,
the double-hold the whole ADR-0027 protocol exists to prevent (leg a); and a worker whose OWN marker went
stale minted a SECOND marker of its own rather than renewing the one it had (leg b). So a WON claim now
**collects** the stale debris on the item: a private `collectStale`, run at both win points (the fresh-CAS win
and the re-claim heartbeat), DELETEs every stale marker that is not our winning one — a 404 is success (a peer
collected the same marker first, the concurrent-GC race, leg l — `deleteComment` already treated 404 as Ok) —
and hands back the OTHER workers it evicted in a new `Won of Held * WorkerId list`. The CLI TELLS each evicted
worker on their own item (`collected worker '<w>' expired claim` + a `Writes.say` "your expired claim … was
collected — worker '<me>' has taken the item. Stop working it."), because a silent eviction is how a worker
keeps building against a lock it no longer holds. Our OWN superseded stale marker is collected too — so a
renew ends with exactly ONE marker — but is never in the notify list (you do not message yourself, leg b).
Collection is **best-effort**: a stale marker we could NOT delete (a non-404 fault) is LEFT for `reap`, never a
reason to fail a claim already won. Because `adopt` transfers through `claim`, this also (correctly) GCs an
adopted orphan's stale marker and notifies its worker — the "GC-on-transfer + notify" follow-up, now partly
discharged. New `casadversarial_server.py` legs (issues 84/85/95; the fixture now reflects DELETEs on
`/comments` and 404s #95's collect via `_DELETE_404`); 7 parity assertions + 4 `Writes` CAS unit tests (collect
an other-worker's stale marker, renew our own to one, 404-tolerant collect, a failed collect left for reap).
Disposed on the record (ADR-0040 §5): the collect/notify WORDING is the engine's (`collected worker '<w>'
expired claim`), bash's `collected worker '…' expired claim` matched where it is greppable; the property is
that exactly one marker survives and the evicted worker is told. Case 24 stays PARTIAL: `reap`
re-verify-before-delete (h) + delete-before-notify (m), and the shared-id re-claim warning (j), remain.

Case 24's **`reap`/`claim` MUTATING interleavings** landed last, and case **24 is now FULL** (legs h, m, j,
this slice) — the interleavings where the destructive verb, or a bypassed CAS, could ITSELF cause the
double-hold the whole ADR-0027 protocol exists to prevent. Two guarantees on `reap`, one on `claim`:
(h) `Reapable` is a SNAPSHOT verdict — proven against the scan's read — and a holder may heartbeat between
the scan and the delete, so `Writes.reap` now RE-VERIFIES the marker's freshness against a FRESH read
immediately before breaking the lock: it returns a new `ReapResult` (`Reaped` / `RenewedSinceScan age` /
`AlreadyGone`), and a marker gone live again is SKIPPED (`renewed since the scan`, its marker SURVIVES) —
deleting a lock because it USED TO BE stale is the one way `reap` causes the very double-hold it cleans up.
(m) `reap` DELETEs before it would ever notify — and this engine's reap posts NO notify at all — so a failed
delete is REPORTED (`FAILED … board left untouched, worker not notified`) and the scan moves on, the marker
LEFT in place (still held) rather than a worker told-to-stop over a lock that still holds for a full lease
(a genuine, non-404 delete failure is not fatal to the whole reap; the other items still collect).
(j) a marker bearing our worker id is not proof it is ours (#419: rules 4/5 hand one id to several workers),
and the re-claim path bypasses the CAS entirely — so `Writes.claim`'s "already ours" branch now returns a
distinct `Renewed of Held * WorkerId list` and RENEWS the marker IN PLACE (a PATCH, never a second POST it
would lose to its own first), and the CLI prints `held … (lease renewed)` and, on a shared id
(`Identity.FromSharedSession`), WARNS it `adopted ITS lock` without running the CAS and that the id `may not
be unique to this worker`. New `reap_race_server.py` (the h/m world: #91's marker flips stale→fresh on the
RE-VERIFY read — `GH_REAP_RACE` one transport under; #96's DELETE 500s — `GH_FAIL_DELETE`); leg j rides
`casadversarial_server.py` (a new #93 whose FRESH marker carries a worker id DERIVED from a shared
claude-code session, `name_from_seed` replicated in the fixture so it matches the engine's `Identity`). 10
parity assertions + 5 `Writes` unit tests (re-claim renews in place / no second POST; reap re-verify skips a
renewed marker / treats an already-gone marker as `AlreadyGone` / deletes a still-stale one); the `Renewed`
signature change rippled the three re-claim `WriteTests` to script the PATCH. Disposed on the record
(ADR-0040 §5): the engine's reap posts NO notify (leg m's "worker not notified" is STRUCTURAL, not an
ordering it could get wrong — the full stale-sweep-with-notify remains `reap`'s job, and the GC-on-transfer
notify is `claim`'s, discharged in the collection slice above); and the `FAILED`/`renewed since the scan`/
`adopted ITS lock` wording is the engine's — the PROPERTY is asserted (a renewed lock is skipped and
survives; a failed delete is reported and the marker stands; a re-claim renews one marker and warns), counted
at the HTTP layer via `/_deletes` and the /comments read-back.

With case 24 full, the clean "engine already matches bash by construction" cases and the larger `reap`/`claim`
MUTATING port gap are BOTH discharged; only case 43 (kit digest/argv, which overlaps D.2) remained of the 27.
The verify-paths repo-boundary divergences (case 23's SKIP-exit code and the absent `gh repo view` fallback)
are disposed on the record in the harness, and the call-counting transformation is demonstrated end-to-end by
case 10.

Case 43's **kit-digest obligation, and the #497 argv cap**, landed LAST — and **case 43 is now FULL, so the
FULL corpus (all 27) drives the engine over HTTP, green; D.1 is COMPLETE**. Two guarantees. (A) The
kit-digest warning is OBSERVED, not INFERRED (#469/#563/#588): `registry/repos.lock` pins a content digest
of every kit source (ADR-0019, #527), so editing one and not relocking reds `main`. The warning that named
it used to infer the obligation from what a worker DECLARED — "is `registry/repos.yml` in your touch-set?"
— which FAILED OPEN once #527 moved the digests into the generated `repos.lock`: declaring `repos.yml`
silenced the warning while the lock was still stale, and the advice named `repos.sh digest` (which now
writes nothing, #588) and told a worker to reserve the generated lock (the three-worker deadlock #527
removed, #309/#428). A DECLARATION is not the obligation; a MATCHING DIGEST is — so `widen` now RECOMPUTES
the digest off the tree and LOOKS, in a new pure `Core.Kit` (`parseLock`/`staleSources`/`divergedRoots`,
unit-tested) whose file IO is wired in the CLI: it names each STALE source (client OR skill — content-
addressed on the file itself or its `SKILL.md`), prints the CURRENT `repos.sh relock` command and the
`repos-registry-selftest` gate, says NOT to reserve the generated lock, and — separately — names each
DIVERGED skill root with the `cp` mirror command (the byte-identical union, ADR-0011/0014), while a
client-only staleness never nags about roots. It is advisory (the widen still lands, `repos-registry-selftest`
is the authority) and silent where there is no tree (`FSGG_KIT_ROOT`, else the git toplevel) or no lock to
read — a receiver mirrors the kit but not the registry, and must not be nagged about a file it does not have.
(B) The #497 argv cap: bash's `active_claims` funnelled the whole claim-scan candidate set back through the
jq COMMAND LINE, so once the org's open-issue bodies crossed MAX_ARG_STRLEN (128 KiB, July 2026) `execve`
returned E2BIG, jq never ran, and EVERY claim-aware read (who/reap/batch/take/inbox/widen) died at once — a
loud outage (#461 refused to report the empty set as "nobody holds anything"), but one no waiting would
clear. STRUCTURALLY ABSENT in the engine, which reads each body as JSON off `HttpClient` and never marshals
the set through argv. New `kit_server.py` (the widen #74 world) is driven against a throwaway kit tree the
harness stands up and edits; `argv_server.py` serves a >128 KiB candidate set to prove `who` READS it and
still classifies honestly (the marked #530 held by kite-497; the two chatty-but-markerless fat issues are
not claims). 17 parity assertions + 6 `Kit` unit tests. Disposed on the record (ADR-0040 §5): the KIT
DIGEST / SKILL ROOTS wording is the engine's (`fsgg-coord-engine:` prefix), re-expressed as the property the
corpus greps — name the stale source, print `repos.sh relock` not the no-op `digest`, name the gate, do NOT
reserve the lock, name the diverged root's mirror; and the argv-128 KiB cap is proven BY CONSTRUCTION (the
engine reads a real-sized set) rather than reproduced, exactly as case 31 leg 9's `MAX_ARG_STRLEN` cap was —
the failure mode is structurally absent because the engine never touches argv.

**D.1 IS COMPLETE — all 27 of 27 corpus cases now drive the engine over HTTP, green (~445 assertions), with
the budget/ETag/fail-closed call counts intact at the HTTP layer.** Next: D.2 (cut the shim), D.3 (green in
all six receivers), D.4 (delete bash, dispose the five differential assertions on the record).

### D.2 — Cut the shim

Replace `scripts/fsgg-coord` with the ~40-line resolver of ADR-0034 §4.4: resolve `fs.gg.coord.cli` from
`.config/dotnet-tools.json`, exec it, pass through args and exit code. The `kind: client` kit row still
digests, still byte-copies, still byte-compares — none of that machinery changes (why Option D was
chosen).

**Slice 1 landed ([#831](https://github.com/FS-GG/.github/pull/831)).** The shim existed as a proven
artifact and the D.1 corpus was green THROUGH it (`scripts/fsgg-coord-shim` beside the bash, not over it),
with the 4-tier resolution order lifted verbatim from bash's proven `engine_resolve`: explicit
`FSGG_COORD_ENGINE_BIN` (honoured or REFUSED, never fallen back from) → a global tool on PATH → a local
`.config/dotnet-tools.json` manifest (restored if only declared, #655) → the from-source `.github` build;
nothing resolvable is a loud non-zero with advice, never the silent no-op (#266) the resolver exists to
end. `tests/coord-engine-parity/shim.sh` re-runs the full D.1 parity corpus with the engine indirected
through the shim (all 445 assertions green through it — a dropped arg / swallowed byte / mangled exit code
would red one) plus the resolution/refusal legs pass-through cannot show; `coord-engine.yml` runs it after
the parity gate. C2/C3 were already done.

**Slice 2 landed — THE SWAP.** `scripts/fsgg-coord` IS the shim now: slice 1's `scripts/fsgg-coord-shim`
body moved onto the canonical path (its doc-comment rewritten from "ships beside bash" to "IS the
entrypoint"), and the standalone `-shim` file was folded away. The ~7,132-line bash monolith is preserved
**verbatim** (a pure `git mv`, zero content diff) at **`scripts/fsgg-coord-bash`**, where the shadow /
differential gates (`50-shadow-engine`/`51-fs-flip`) and the escape-hatch corpus keep driving it against
the engine until D.4 deletes it. Everything that *executes* `scripts/fsgg-coord` now transparently runs the
engine; `registry/repos.lock` was re-locked so the kit distributes the shim's bytes. Proven locally green
end-to-end: the swapped entrypoint resolves the from-source engine (tier 4) and execs every subcommand;
D.1 parity **445/445** and shim parity **5/5** through the new `scripts/fsgg-coord`; the shell corpus
**891/891** (incl. `50-shadow-engine` 96 + `51-fs-flip` 28) driving the preserved bash against the engine,
in both default and `FSGG_COORD_ENGINE=fs` modes; Cli unit tests 92/92; coordination-sync 86/86,
touch-set-drift selftest 14/14, repos-registry 95/95, repos-audit 46/46; `repos.sh validate` OK.

Three decisions on the record:

1. **No `schemaVersion` bump, no CHANGELOG entry.** The swap changes a kit *client's content*, not the
   registry *schema shape* (the `fsgg-coord` row is structurally identical — `kind: client, source:
   scripts/fsgg-coord`). `registry/repos.CHANGELOG.md` states the rule outright: "Re-locking a kit digest
   is not [changelog-worthy]." So the memory's anticipated ADR-0015 bump was **not in fact required** —
   there is no ADR-0037 publish-before-flip to run, only a `repos.sh relock`. (Had the row's *kind* or a
   *field* changed, that would be schema growth; it did not.)
2. **The shell-corpus retirement moves to D.4, not here.** Retiring `tests/fsgg-coord/*` is inseparable
   from deleting bash: `50-shadow-engine`/`51-fs-flip` need bash *and its whole harness world*, and the
   escape-hatch cases hold bash exact — all of which live until D.4. So the harness `COORD` var was
   repointed at `scripts/fsgg-coord-bash` (keeping all 29 cases green against bash) rather than the corpus
   retired; D.4 deletes bash and the corpus together.
3. **The three "read-`fsgg-coord`-as-bash-source" gates were repointed at `scripts/fsgg-coord-bash`**, not
   rewritten to interrogate the engine (that is D.4 work, when bash is gone): `recipe-landable`'s two
   `landable`-existence greps, `generate-projections`'s `TOUCHSET_GRAMMAR` drift scrape, and the
   `touch-set-drift` selftest's FSGG-PATHS marker-vocabulary check. The engine's copies of all three are
   already held to the same contract by the D.1/shell corpus (case 23 verify-paths, `facts` grammar), so
   nothing goes uncovered. `touch-set-drift.yml` — the ONE workflow that *executes* the client on a runner
   — gained the engine build + `FSGG_COORD_ENGINE_BIN` wiring C2 pre-staged, so `verify-paths` resolves an
   engine there instead of dying #266-loud.

- **Exit:** the corpus (D.1) is green *through the shim* on `.github@main`; every workflow that shells
  out is green (C2); the restore gate is green (C3). **DONE** — the swap has landed on `.github@main`.

**Receiver readiness for D.3 (confirmed here, not assumed).** Merging the swap auto-fires
`coordination-propagate` (it triggers on a push touching `scripts/fsgg-coord`), byte-copying the shim to
the six receivers — which is the *intended* D.3 delivery, not a hazard, **because C2/C3 made the receivers
ready**: the distributed `dist/dotnet/.config/dotnet-tools.json` (sync'd to every receiver via
`build-config`) declares `fs.gg.coord.cli` **0.1.1** → `fsgg-coord-engine`, that version **is published**
to the org feed (tags `coord-engine/v0.1.0`/`v0.1.1`, `release-coord-engine.yml`), and `setup-dotnet` is in
every receiver workflow that shells out. So a receiver resolves the shim via **tier 3** (declared manifest +
`dotnet tool restore` + `dotnet` present). D.3 is now the *verification* that they went green, not a
separate rollout.

### D.3 — Green in all six receivers — **DONE (2026-07-16)**

Roll the shim to the six `receives: coordination-kit` repos via the existing digest → byte-copy →
byte-compare fabric. No receiver edits — the shim and its corpus are distributed like every other kit
artifact.

**What happened.** Merging the swap (#833) fired `coordination-propagate` at 10:31Z, which opened/force-updated
the rolling `coordination-kit/sync` PR in each receiver with the shim's bytes and armed auto-merge. Each PR
gated on the receiver's OWN required checks: `kit / coordination-kit` (the byte-compare coherence gate) went
green immediately in all six, and every receiver's native build+test `gate` went green too, so auto-merge
landed each PR without a human:

| receiver | sync PR | merge | landed |
|---|---|---|---|
| FS.GG.Templates | (rolling) | — | at propagation |
| FS.GG.Game | (rolling) | — | at propagation |
| FS.GG.Audio | (rolling) | — | at propagation |
| FS.GG.SDD | [#465](https://github.com/FS-GG/FS.GG.SDD/pull/465) | `559efdc` | 10:37Z |
| FS.GG.Governance | [#224](https://github.com/FS-GG/FS.GG.Governance/pull/224) | `22b788d` | 10:40Z |
| FS.GG.Rendering | [#834](https://github.com/FS-GG/FS.GG.Rendering/pull/834) | `76df7e8` | 10:42Z |

All six now carry `scripts/fsgg-coord` **byte-identical to canonical** (`sha256 3b884ccd…`), with
`coordination-coherence` green on each `main` and zero open sync PRs. Note a receiver never *executes* the
shim in CI — it only byte-compares it (`coordination-coherence.yml`); the workflow that executes the client
(`touch-set-drift.yml`, resolving the engine via tier 3) lives only in `.github`. So D.3 was, by design, the
*verification that the receivers went green under the swap*, not a rollout with a per-receiver execution
surface: coherence proves the bytes, the receivers' own CI proves the swap broke nothing, and C2/C3 already
proved tier-3 resolution is available where the shim would run.

- **Exit:** the corpus is green through the shim in **all six receivers**. **MET.**

### D.4 — Delete bash, dispose the five differential assertions on the record — **DONE (2026-07-16)**

Deleted `scripts/fsgg-coord-bash` (the ~7,132-line monolith) and the whole `tests/fsgg-coord/` shell corpus
that drove it. `--engine=bash` is removed *because there is no bash left to be* — the flag was parsed only
by the monolith; the shim `scripts/fsgg-coord` is a transparent pass-through that never knew `--engine`, and
the engine rejects it as an unknown flag. Per ADR-0040's "Phase D contradiction, and its resolution", the
five `51-fs-flip.sh` differential assertions are **retired on the record** — not silently dropped:

| # | assertion | disposition |
|---|---|---|
| 1–2 | `fs` returns bash's items / same exit code | **subsumed** by the ADR-0038 defect-corpus-against-`fs` (a precondition of D.1), now the ~445-assertion `tests/coord-engine-parity/` corpus that holds the engine to the certified golden |
| 3–5 | `--engine=bash` is byte-exact / never consults the engine | **retired** — the escape hatch is the thing being deleted |

The five and their disposition are recorded in [the D.4 differential disposition
manifest](2026-07-16-d4-differential-disposition.md), so the drop is a decision in the diff, reviewable,
never a silent gap. **A silently shrinking gate is the failure; a documented retirement is not.**

Consequential edits that kept the tree green: `.github/workflows/fsgg-coord-selftest.yml` deleted (it drove
the shell corpus against bash); `coord-engine.yml` lost its shadow step and its `tests/fsgg-coord/**` /
`-bash` triggers; the three D.2-repointed gates now interrogate the engine (`recipe-landable` greps
`src/FS.GG.Coord.Cli/{Options,Client}.fs`; `generate-projections` dropped the two-engine grammar
cross-check; the `touch-set-drift` selftest compares against `Client.fs`'s `FSGG-PATHS` markers); and the
shim's own doc-comment lost its `-bash` reference, so `repos.lock` was relocked (a kit-client-content change,
propagating to the six receivers on merge as the D.2 swap did).

- **Exit:** bash is gone; the corpus is green through the shim; the disposition manifest is on the record.
  **DONE.**

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

- [x] D.1 — the full corpus green through the engine locally, call counts intact, shadow/flip still green.
      **COMPLETE: all 27 of 27 cases** ported to `tests/coord-engine-parity/` (~445 assertions). Case 43
      (kit digest/argv) landed last — the kit-digest obligation is OBSERVED off the tree in a pure `Core.Kit`
      wired into `widen`, and the #497 argv-128 KiB cap is disposed on the record as structurally absent
      (see the §5 D.1 ledger).
      **Case 24 is now FULL** — its **lock-fails-closed** adversarial reads (a quoted marker does not forge a
      lock, a malformed marker BLOCKS, an expired worker cannot resurrect its claim, and a failed/empty CAS
      re-read is a LOSS that withdraws its own marker) drive through the engine over HTTP
      (`casadversarial_server.py`), its **`say --to` normalization** landed (a mis-cased target is slugged to
      the id `inbox` matches via the now-public `Identity.slug`, `*` stays literal, an unslugged target is
      WARNED about — proven as an engine round-trip through `inbox_server.py`), its **`overlap` `paths_of`
      fail-closed** landed (leg k — a failed body read refuses to schedule against an unknown touch-set via a
      shared `failSchedule`, never mis-read as the empty-set DISJOINT nor as "declared nothing", proven with an
      `OVERLAP_FAIL_ISSUE` toggle on `overlap_server.py`), its **`claim` stale-marker COLLECTION + notify**
      landed (legs a, b, l — a won claim DELETEs the stale marker it claims over via a private `collectStale`,
      TELLS each evicted worker on their own item, renews its OWN stale marker to exactly one, and treats a
      concurrent-GC 404 as success; the outcome grew a collected-workers list, and `adopt` — which transfers
      through `claim` — now GCs an adopted orphan's marker too, partly discharging the GC-on-transfer
      follow-up), and its **`reap`/`claim` MUTATING interleavings** landed last (legs h, m, j — `Writes.reap`
      RE-VERIFIES a stale marker against a fresh read immediately before the delete and returns a `ReapResult`,
      so a claim heartbeated between the scan and the delete is SKIPPED and its marker survives (h) and a failed
      delete is REPORTED, not swallowed, the marker left in place and the worker never told (m); and
      `Writes.claim`'s "already ours" branch is now a distinct `Renewed` that RENEWS the marker IN PLACE (a
      PATCH, not a duplicate) and WARNS on a shared id that it `adopted ITS lock` without running the CAS (j) —
      new `reap_race_server.py`, a #93 leg on `casadversarial_server.py`, 10 parity + 5 `Writes` unit tests).
      Case 14 is now FULL — its whole `lint` command
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
- [x] D.2 — the shim cut; corpus green through it on `.github@main`; C2 + C3 green. **DONE.**
      Slice 1 ([#831](https://github.com/FS-GG/.github/pull/831)) landed the ADR-0034 §4.4 shim as a proven
      artifact (`tests/coord-engine-parity/shim.sh`). **Slice 2 — THE SWAP —** made `scripts/fsgg-coord` BE
      the shim: the ~7,132-line bash is preserved verbatim (pure `git mv`) at `scripts/fsgg-coord-bash` for
      the `50`/`51` differential gates until D.4, the standalone `-shim` file folded onto the canonical path,
      and `repos.lock` re-locked to the shim's bytes. Everything that executes `scripts/fsgg-coord` now runs
      the engine; `touch-set-drift.yml` (the one runner that executes it) gained the C2-staged engine build +
      `FSGG_COORD_ENGINE_BIN`. Green locally end-to-end: parity 445/445 and shim parity 5/5 through the new
      entrypoint, the shell corpus 891/891 (bash vs engine, default + `fs`), Cli 92/92, coordination-sync /
      touch-set-drift / repos-registry / repos-audit selftests all green, `repos.sh validate` OK. **No
      `schemaVersion` bump** (a client-content change, not schema growth — `repos.CHANGELOG.md`'s "re-locking
      is not changelog-worthy" rule); **shell-corpus retirement deferred to D.4** (it lives with bash). The
      three gates that read `fsgg-coord` as bash source repoint at `scripts/fsgg-coord-bash`.
- [x] D.3 — green through the shim in all six receivers. **DONE (2026-07-16):** the swap-merge
      propagation opened a `coordination-kit/sync` PR in each receiver; all six went green on their own
      required checks (coherence byte-match + native build/test) and merged (SDD `559efdc`, Governance
      `22b788d`, Rendering `76df7e8`; Templates/Game/Audio at propagation). Every receiver's
      `scripts/fsgg-coord` is now byte-identical to canonical `sha256 3b884ccd…`, coherence green, zero
      open sync PRs. Receivers byte-compare the shim but do not execute it — D.3 was the verification they
      went green, not a rollout.
- [x] D.4 — **DONE (2026-07-16).** `scripts/fsgg-coord-bash` and the whole `tests/fsgg-coord/` shell corpus
      deleted; `--engine=bash` gone (no bash left to be); the five `51-fs-flip.sh` assertions disposed of on
      the record in [the D.4 differential disposition manifest](2026-07-16-d4-differential-disposition.md)
      (1–2 subsumed by the ADR-0038 corpus-against-`fs`; 3–5 retired with the escape hatch). The three
      D.2-repointed gates now interrogate the engine, `fsgg-coord-selftest.yml` is deleted, `coord-engine.yml`
      lost its shadow step, and `repos.lock` was relocked for the shim's comment change. Epic
      [#729](https://github.com/FS-GG/.github/issues/729)'s "retires 22 of 40" re-derived honestly per
      ADR-0040 Consequences (the flip retired four to six; the completed port retires the write-path/IO
      family it was *for*). Green: parity 445/445, shim 5/5, e2e 23/23, Core/GitHub/Cli 183/217/92,
      projections/touch-set-drift/recipe-landable/repos-registry selftests all green.

---

*This plan executes ADR-0040 Phase D. It does not amend it. Where the two differ, the ADR governs.*
