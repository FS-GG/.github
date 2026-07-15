# Phase D — the corpus through the shim, and the deletion of bash

**Date:** 2026-07-15
**Owner:** `.github` (the coordination engine)
**Governs:** the execution of [ADR-0040](adr/0040-port-the-io-layer.md) Phase D
**Status:** In progress — **D.1 underway**. Phases A–C have landed. The corpus-through-engine parity
harness has grown from the prototype to **20 full + 3 partial of 27 corpus cases** (~230 assertions); D.2–D.4 not started.
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
`tests/coord-engine-parity/` harness (~196 assertions across **20 of 27 corpus cases**, 16 fixture
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

**Progress (as of the `#496 lint schedulability` slice, 2026-07-15).** The harness is grown one defect/case
at a time — each PR titled `parity: … (case N)` (the engine already matched bash — port the slice) or
`fix(engine): … (#NNN)` (a real port gap — fix the engine, then prove it). **18 of 27 cases fully covered,
plus 3 partial (13, 14, 24)** — the 27 being the full corpus's 29 minus `50-shadow-engine`/`51-fs-flip`,
which are the differential harness D.4 disposes of, not engine-behaviour cases:

| covered | case | note |
|---|---|---|
| ✓ | 10, 11, 12, 15, 20, 21, 22, 23, 32, 33, 35, 40, 41, 42, 44, 45, 46, 52 | see the parity ledger in `tests/coord-engine-parity/run.sh` |
| ◑ | 13 (§#480 scope only) | the git-remote repo scope for `next`/`take`/`batch`/`who` + short-id resolution; `lint`(epic-graph)/`issues`/`reap`/`Blocked by` legs deferred (see the remaining table) |
| ◑ | 14 (`lint` FULL — all rules; `done --flip` rollup deferred) | the new `lint` command with EVERY rule: the schedulability pair (NO-TOUCH-SET / BAD-TOUCH-SET, #496) AND the epic-ROLL-UP-graph rules (EPIC-NO-CHILDREN, EPIC-CHILDREN-TRUNCATED, EPIC-DONE-OPEN-CHILD, DONE-STATUS-OPEN-ISSUE note, and the PR-probing/fail-closed EPIC-UNLINKED-CHILD — #325/#346/#266) + `--repo`/`--json`/`--strict`/exit-codes; only the `done --flip` epic rollup (a separate `done` concern) is deferred (see the remaining table) |
| ◑ | 24 (`--issue` boundary + cross-repo close, shared with 23) | the #479/#494 `verify-paths --issue` legs and the cross-repo CLOSING-ref SKIP; the lock's adversarial interleavings (`reap`/`overlap`/`heartbeat` resurrection, forged/malformed markers) deferred — they need `reap`/`overlap` commands the engine lacks (see the remaining table) |

Case **23 is now FULL**: `verify-paths` OK/DRIFT/SKIP + #322 fail-closed, the `--issue` boundary (#479
straddle, #494 repo-qualified read, `--repo` reduction / bare-repo `--issue` agreement, `--issue`-decides-repo,
head-ref-read bypass), the **#430 git-remote repo default** (repo off the checkout's remote when neither
`--repo` nor `--issue` is given, resolved FREE/offline with no GraphQL — so a dead budget is never blamed on
the checkout; no remote → an earned refusal), and the **cross-repo CLOSING-ref SKIP** (a PR closing another
repo's issue → SKIP naming the other repo, no verdict across the boundary). The SKIP-exit divergence and
bash's `gh repo view` fallback are disposed on the record (the engine has no gh-repo-view leg, so its
EX_RATE-vs-checkout failure modes are structurally absent).

**Remaining (8 full + the rest of 13/24), each classified as a port gap or a deliberate divergence:**

| case | what it needs | class |
|---|---|---|
| 13-remainder | the `issues` short-id (#446), `Blocked by` canonicalization gate, `reap` scope — deferred on the record when the #480 scope slice landed (the whole `lint` command, schedulability + epic-graph, now shipped in the case-14 slices) | new `issues`/`reap` commands |
| 14-remainder | the `done --flip` epic rollup (#235/#558/#543/#583, #342 provenance) — a `done`-command concern, NOT `lint`; the whole `lint` command (schedulability + all epic-graph rules) is DONE. The `EpicBody.childRefs` + `Reads.subIssues` this needs already landed with the lint epic-graph slice | `done --flip` rollup (reuses `EpicBody`/`subIssues`) |
| 24-remainder | the lock's adversarial interleavings (stale-marker collection, the heartbeat resurrection bug, forged/malformed markers, the empty-CAS-re-read loss, concurrent GC) + `reap`/`overlap`/`say --to` normalization — the `--issue`/#479/#494 legs and the cross-repo CLOSING-ref SKIP are now DONE | needs `reap`/`overlap` commands the engine lacks (larger) |
| 25 (offboard-claims) | paginated open-issue scan for `who` (off-board markers) + starved `batch` prose | port gap (larger) |
| 26 (expired-lease) | `reap` / expired-lease-vs-open-PR (#581) | new `reap` command |
| 30 (pr-existence-697) | `who`/`adopt` land-the-finished-PR path (#697) | new `adopt` command |
| 31 (superseded-run-720) | `adopt`/`landable` superseded-run scoring (#720) | new `landable`/`adopt` command |
| 34 (xrepo-touchset-353) | `widen` collision-detect + `overlap` command (#353) | port gap — repo-scoping itself covered via case 35 |
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
**Deferred on the record (14-remainder):** only the `done --flip` epic rollup (#235/#558/#543/#583, #342
provenance) — a `done`-command concern that will REUSE `EpicBody`/`subIssues`.

The clean "engine already matches bash by construction" cases are largely ported; what remains clusters
into **new commands** (`reap`, `adopt`/`landable`, `overlap`, `issues`), the **`done --flip` epic rollup**,
and **larger port gaps** (paginated off-board `who`). The verify-paths repo-boundary divergences (case 23's
SKIP-exit code and the absent `gh repo view` fallback) are now disposed on the record in the harness, and
the call-counting transformation is demonstrated end-to-end by case 10.

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
      **In progress: 20 full + 3 partial of 27 cases** ported to `tests/coord-engine-parity/` (~230
      assertions); the rest remain (see the §5 D.1 ledger). The whole `lint` command (schedulability +
      epic-ROLL-UP-graph rules) landed as case 14's slices; only its `done --flip` rollup leg is deferred.
      Ten engine defects the port was *for* closed along the way, plus the
      `verify-paths --issue` repo-boundary port gap (#479/#494) and the #430 git-remote repo default, which
      together close case 23 in full (the cross-repo closing-ref SKIP ported alongside), the #419
      twin-session refusal + shared-id warning, which closes case 44 in full, and the #418 resolver cache
      (board id map day-cached, item ids cached forever), which closes case 10 in full — the §3
      call-counting transformation, re-expressed as HTTP request counts.
- [ ] D.2 — the shim cut; corpus green through it on `.github@main`; C2 + C3 green.
- [ ] D.3 — green through the shim in all six receivers.
- [ ] D.4 — bash deleted; `--engine=bash` removed; the five `51-fs-flip.sh` assertions disposed of on the
      record; the `engine-retires` label and epic [#729](https://github.com/FS-GG/.github/issues/729)'s
      "retires 22 of 40" re-derived honestly (ADR-0040 Consequences).

---

*This plan executes ADR-0040 Phase D. It does not amend it. Where the two differ, the ADR governs.*
