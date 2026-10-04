# Unified Roadmap handoff — 2026-10-04 13:37:31 UTC

Programme execution is **paused, incomplete**, at the user's `$stop-with-handoff`
request. Root is fdev. All implementation workers have stopped; SC2 and the
product-board adapter workers were interrupted with partial source preserved.
No new feature edit, repair push, publication, runtime admission or receiver grant
was performed after the stop. The only delivery authorized during shutdown is
this handoff. Native external PR checks may finish independently; no monitoring
continues after the save.

Resume through the installed `continue-from-handoff` skill
and [work-unified-roadmap](../../.agents/skills/work-unified-roadmap/SKILL.md), using
the [Unified Roadmap](../2026-09-07-154210-fs-gg-unified-development-roadmap.md)
and each owning subroadmap. This supersedes the unfinished predictions in the
[previous handoff](2026-10-03-1811-unified-roadmap-handoff.md); that report's
historical attempts and failures remain immutable. Protected `.github/main`
at shutdown was `94f5b84e0f1c91a5e3e819d79af7804b2bf6a739`, tree
`1998174fa2b6be14c2140f3493fa748da86150e1`. This save starts from that revision.

## Current authority and priorities

The user authorizes autonomous programme integration and aggressive independent
parallel lanes. New major-feature planning uses Astra high; existing bounded
implementation uses Sol medium. The focused BAR Astra review has already selected
one build route, one canonical capture mechanism, one diagnostic startup and
exactly one produced unit before expanding to six journeys. Ordinary build/test
failures require the smallest demonstrated repair and one fresh bounded attempt;
replan only when a failure invalidates the architecture.

The current architecture is **fdev communicating with selected store containers**.
This is already protected GitHub documentation in
[Unified §9.6 at the accepted revision](https://github.com/FS-GG/.github/blob/94f5b84e0f1c91a5e3e819d79af7804b2bf6a739/docs/2026-09-07-154210-fs-gg-unified-development-roadmap.md#L5603).
Home/Main's persistent orchestration is legacy. There is no Home replacement,
external operator reply or legacy Akka adoption prerequisite for the current
receiver route. Retirement of legacy state remains a separate record-preserving
operation. Existing receiver artifact, authority, store and recovery gates remain.

The known local store access configuration is `~/.ssh/fsgg_store_ssh_config`,
with SSH host alias `fsgg-store`. Access was observed working; credentials remain
local. The user-reported telemetry inventory is Host **0.3.0**, service
`telemetry-home` as user `telemetry`, database
`/srv/telemetry/data/utel-home-20261003/telemetry.sqlite3`, configuration
`/srv/telemetry/config/home-20261003/host.json` and supervisor definition
`/srv/telemetry/supervisor.d/telemetry-home.conf`. `/srv/telemetry` is persistent.
Backups exist, but production recovery and current schema/migration/rollback
compatibility remain unverified. Do not read telemetry records or credentials
to reconcile this inventory; any inactive assessment needs an isolated verified
backup copy and its own admission.

Keep the shortest useful joins: installed API qualification → BAR and SC2 checked
adapters independently → public package/adoption → joined guidance. Board and
Akka work can land independently. Portable Python is a separate preparation and
receiver lane. Learn's later comparison remains separate from full-V2 acceptance.
Progress reports should lead with actual installed execution, product preparation
and receiver behavior, with structured Progress/Working on/Problems every 30 minutes.

Root owns remote writes and concrete operation admission. Campaign remains
`unified-roadmap-20261003`: one open managed PR per dependency chain, at most two
newly qualifying managed PRs per repository. Use `tools/pr-lane-admission.py`
for creation and `tools/routine-delivery.py` for delivery. Preserve both exact-head
routine and lane markers. Required native checks and exact merged-tree readback
remain mandatory; source delivery, publication, adoption and activation are distinct.

## Verified delivery since the previous save

| Delivery | Accepted source and scope |
|---|---|
| Published inventory, [.github #4194](https://github.com/FS-GG/.github/pull/4194) | Merge `1a00c681f27b1d8802633ac1603872cb9fce3c45`, tree `cc16e982e06d15e44c4c78379f3a7e79b4f337fd`. Three registry rows and six projections now record WASM Contracts/Browser **0.3.0**, Coord CLI **0.96.0**, Wizard **0.13.0**. Existing publication receipts and both-feed checks passed; feed run `37202844807`. No rebuild, republication, installation pin or default change. |
| Accepted board consumer and polyglot architecture, [.github #4192](https://github.com/FS-GG/.github/pull/4192) | Merge `4fc6edf6acce60760cea858ba15e4384c9ce0340`, tree `00c77cb15bc9e2d49f82d55e72c2c6dced95bd24`. Native issue **#3009** closed completed. ADR 0092, design and dispositions delivered. Selected cohort stays SDD#928, Templates#441, .github#3010/#3009; the first three human Blocked fields and unselected SDD#935 are unchanged. Full board **.4** and product **.5/.6** remain open. |
| Immediate progress and receiver-route correction, [.github #4196](https://github.com/FS-GG/.github/pull/4196) | Merge `94f5b84e0f1c91a5e3e819d79af7804b2bf6a739`, tree above. Unified §0 records #3009's bounded closure and published inventory separately. §9.6/§9.8 record current fdev/store applicability. Root independently matched the whole expected and actual merged trees. |
| Current portable Python source, [Coordination #929](https://github.com/FS-GG/FS.GG.Coordination/pull/929) | Protected main `0ecb5945b79ca13de2b0dd91d6f05504319c78d2`, tree `8adf1e64708d78fd7eb5edf82e93a69a68bd81fe`. Genuine SDD 2.1/Templates 0.18 provider-input join and receiver-role parsing delivered; native checks passed after one exact manifest-digest repair. |

The protected Unified §0 also records the earlier resumed deliveries. In
particular, V2 preflight A/B from Coordination #928, telemetry coherent **0.96**
publication and bounded public installed readiness are delivered; the prior
handoff's pending predictions are historical. Local `fsgg-coord-engine` currently
selects `/home/developer/.local/share/fs-gg/tools/coord-0.96.0/fsgg-coord-engine`
and reports **0.96.0.0**. Historical Home **0.95** receipts remain separate.
Registry `package-version` records newest publication, independently of pending
receiver checks. Wizard release promotion and installed receiver qualification
remain open even though both feeds serve **0.13.0**.

## Stopped lanes and their next concrete joins

All owners have **no action now**. The next actions below require a new continuation.
Private evidence root `B` means `/home/developer/.local/share/fs-gg-private`;
knowledge root `K` means `/home/developer/.local/share/fsgg/knowledge/bar`.
These are access references, not public evidence copies. Check their continued
existence, hashes and actual source authority before reuse.

### Installed V2 API — accepted execution, open source PR

Owner `v2_installed_api` / root. [Coordination #930](https://github.com/FS-GG/FS.GG.Coordination/pull/930),
chain `V2-PREFLIGHT-01.E`, is open at `06b6bb38b6315f1cf9e6dda6182307e649e8fa8c`,
tree `5e0a77524fc95f437415d458974c11475123ebce`. Clean worktree
`/home/developer/projects/coord-v2-installed-api-source-20261004`, branch
`routine/v2-prepared-installed-api-source-20261004`. Its two qualified files remain
byte-identical to original qualified head `2f9cc0d86d3a2b9d5e59a2eaccb7d33b1fd6cb8f`;
the later merge only inherits protected #929 changes.

One actual bounded installed F# qualification passed in **3.937 seconds** with
the selected checkout unavailable and restored in `finally`. Genuine public
**0.2.1** package, pinned FSI/runtime and physical DLL layout were used. Ten
observations and six negative cases launched zero workloads; one instrumented
positive executor ran once and cleaned up once. All seven known owned process
generations were absent. This is genuine installed API execution with an
instrumented executor, not native BAR/SC2 actor execution. Other checkouts were
not masked; do not claim full filesystem isolation.

Acceptance: `B/v2-installed-api-placeholder-root-once-qualification-20261004`,
evidence SHA-256 `53ceb8c569e5663f9cd233f11b70ed090850970a7ae6ce63c5610ac1e0ef0fd5`,
root acceptance `f739b9e6684aa497c62a5b52a2682b9226d4592afa89206b70cafdf583b1d56b`.
Historical OOM, descriptor, output-layout and placeholder refusals stay failed.
Do not repeat this generic qualification solely for a source merge or product join.

At the stop snapshot, compiler/package checks passed and native coherent run
`37204134456` still had **formal-epoch in progress**; it is an external job, not
reported stopped. The earlier original-head coherent run was not blindly cancelled.
After continuation, inspect exact current native checks and merge #930 only when
eligible. Then bind each product independently to the installed contract.

### BAR — real capture works; Cargo custody repair awaits root acceptance

Owner `bar_ci` / root; startup owner is reusable after continuation. Canonical
source worktree `/home/developer/projects/worktrees/bar-stock-selection-capture-config-20261004`
is clean at `3d5ef1d5c74fa6b98abf31719d3e8dc6a7f73531`, tree
`00750d3ce9fba9be42b070f3f0babf62684ec84a`. It is an explicitly selected unprotected
candidate, not a protected release. Canonical `--capture-dir` / `--nuget-config`
repair has real build evidence; applicable managed artifacts remain reusable.

Accepted startup `B/bar-startup-snapshot-root-native-20261004` exited **0** in
**37.714 seconds**, reached map Avalanche 3.4/frame 1, and retained exact setup,
held-output/maps/custody/cleanup evidence. No command or unit was produced.
Accepted original trust `B/bar-focused-signed-body-trust-root-native-20261004`
contains genuine normal-TLS acquisition and 25 successful cryptographic verifier
exits; binding SHA-256 is `3719965cb0f9a391c8b859d057d7ae3a28baa8d17c83e4113b220d7473909185`.
Reuse those signed bytes and managed artifacts; do not repeat trust preparation.

Actual producer attempts and stages are immutable:

| Attempt | Actual outcome |
|---|---|
| `bar-focused-six-phase-producer-20261004` | Exit 3 / 29.218 s: original package payload guard refused before Fable. Literal Fable.Elmish archive mapping repaired. |
| `bar-focused-six-phase-elmish-producer-20261004` | Exit 3 / 67.954 s: real Vite missing `Broker.Browser.Wasm/index.js`. Five tracked canonical JS inputs restored to the frozen source selection. |
| `bar-focused-six-phase-wasm-producer-20261004` | Exit 3 / 92.594 s: first five build commands actually exited 0, including canonical StockSelection capture; stock locked-dependency validation omitted Fable.Core. Rust guest did not start. |
| `bar-focused-six-phase-stock-assets-producer-20261004` | Exit 1 / 108.297 s: first five commands exited 0, real Cargo started, then output custody refused two genuine hardlinked WASM aliases. Guest and worker result receipts are absent and raw guest logs empty: **Cargo exit remains unknown**. A produced WASM file alone is not a successful build. |

Those stage names are under `/tmp`; outer operation evidence is under matching
`B/*-root-native-20261004` directories. The fourth stage has exactly two output
aliases, same device/inode, owner, mode 0700, nlink 2 and **21,597 bytes**, SHA-256
`14aeecd01e85c2baffc872ae78cac953711f8d29ab61ee490550586b48a325bc`.
Worker reports all **1,514** frozen physical leaves unchanged and **19** known
generations absent, including the outer observer. Root previously observed
independent settled custody and unchanged 11,364 original / 1,306 managed /
411 inherited input leaves; final fourth-stage full audit remains for root.

Fifth-attempt SOURCE is frozen at
`B/bar-focused-six-phase-cargo-alias-source-20261004`, **251 leaves**, seal
`1bf61fa5f799a6215d3aeac14282d995e480c4e3823158427a40816f74d3e1c7`,
profile `4b9b71118927a95ceb04ef73221131b5449cd61611078bbb4b9644c86bfc8e3c`.
Root read the review and Cargo guard, but full diff/seal/control review is not
complete. Worker reports **45 pure controls**; this is not a fresh build.
The producer-only exception admits only the two literal Cargo output aliases
with exact actual phase origin and body correspondence. Other links refuse.
First-failure receipts and finite finalization preserve an actual failure even
when later quota/census steps fail; missing exit codes remain null.
Fresh `/tmp/bar-focused-six-phase-cargo-alias-producer-20261004` is absent and
unconsumed. No fifth build or game was admitted.

After continuation: finish independent repair review, then admit exactly one
fresh six-phase build. Retain actual guest exit and complete outputs. Bind that
exact capsule to checked preparation, prove real imports/discovery and missing
capture dependency refusal before launch, then attempt exactly one unit and
observe queue plus resulting unit. Useful gameplay remains **0/6**.

BAR knowledge was folded through the third producer and its stock-assets repair.
Current `question.next-native-stock-gates` digest is
`9628f27a50631026436b8566aa64a0ce090246beb9f48e93b13b0f0e23147235`;
safe aggregate is `K/evidence/bar-browser-stock-capture-safe-readback-20261004.json`,
SHA-256 `d8f792ba1b94e49c181ea3585628efd6ca77285a139db4d4ff67d1e2b53aa17b`.
Validation reported no unresolved conflict/integrity defect. Backup inspection
at 13:25 reported four retained, no pending deletion and no alerts; last success
13:16:25 UTC. **Fourth Cargo refusal/fifth SOURCE still need a minimal safe fold**
explicitly superseding that item without claiming a compiler exit. Do not ingest
raw maps, private records or licenses. The existing knowledge keeper is persistent
infrastructure and was not stopped as a task process.

### SC2 — full observer compiled; installed adapter proof is partial

Owner `sc2_source` / root. Accepted full eight-source observer compile is
`B/sc2-n3-full-observer-final-elf-repair-root-output-20261004`; all real compiler
commands exited 0. Exact image **93,552 bytes**, SHA-256
`2f99f6fbc7328e1ac39d333813fa4091017a33c876b4e6c53014fc605c1c8f92`,
actual RX extent **78,353 bytes**. Root acceptance
`B/sc2-n3-full-observer-final-elf-repair-acceptance-20261004/acceptance.json`
is 1,863 bytes, SHA-256
`ff372cc50422061d30d5b92a2a5ff0a55583c0b4314346be7542563047fc1df6`.
Root verified compiler correspondence, all 288 input pins and owned-process
absence. The image was **not executed**; BSS is not read-credit evidence.

Checked adapter SOURCE
`B/sc2-full-observer-checked-preparation-source-20261004`, 18-leaf seal
`d6a5913d67796c617af0144cad24c8819c47fe9988d19d22a291e6e68274363a`,
contains actual `PreparedAttempt.prepareAsync` / executor prerequisite calls.
Root checked source, pins and actual readonly checker observations in
`B/sc2-full-observer-checker-root-readback-20261004`: three real imports joined;
discovery correctly returned incomplete/actor ineligible. Forty top-level pure
assertion controls passed; they are not 40 unittest-runner tests. No F# adapter
compilation or actor launch occurred.

Whole resource fit under the unchanged 16 MiB contract remains unknown. The
known floor is **15,316,019 bytes**, leaving **1,461,197 bytes** before context,
maps, process/pipe state and cleanup; permitted 2 MiB auxiliary inputs already
give a counterexample before extras. Do not claim measured demand or raise caps.
Native semantic proof, current maps, provider readiness, fresh epochs and outer
custody/CPU capacity are still required for any actor admission.

Interrupted minimal installed-API proof is partial at
`B/sc2-checked-adapter-installed-qualification-source-20261004`:
`qualification.fsx` 10,487 bytes / SHA-256
`f08181a8ad3c800954b789d021d27a17bd386c9e9913e2563b2d87552438530b`,
`qualification-pins.json` 10,942 bytes / SHA-256
`ed172654ddc0f8edd009aeb04398e1125b43099bd71e6cb88fe87a63de3ac1be`.
No seal, finished controls, finite root admission or execution exists. Resume
this source rather than replaying generic E qualification. The public API uses
one refusal string for import/discovery mismatch: preserve a bounded real
imports observation, then require actual adapter and executor discovery refusal
with zero actor callbacks, plus missing-input refusal before checks. Synthetic
binding fixture identities must be labeled honestly.

### Product board — additive source preserved, qualification pending

Owner `v2_installed_api` / root for `COORD-BOARD-V2-01.5`; board .4 delivery above
is separate. Interrupted worktree
`/home/developer/projects/worktrees/board-product-adapter-source-20261004`,
branch `routine/board-product-adapter-source-20261004`, remains at protected base
`4fc6edf6acce60760cea858ba15e4384c9ce0340` with **11 modified files**: eight
shared F# implementation/interface paths and three existing tests. It is uncommitted
partial source, **372 additions / 26 deletions**, not compiler-qualified.
Root saved the exact 51,497-byte binary diff at
`B/stop-snapshot-20261004-133731/board-product-adapter-partial.diff`, SHA-256
`3bcbe26b72465645aaa28863c5abe1c0df6baf075f0f22dd22ef864093aa388a`.

Proposed contract keeps the closed Binding record/wire shape. Version 2 retains
organization scope and its admitted cohorts; version 3 binds one receiver
repository and 1–5 exact issue node-ID mappings. The same import/v1 manifest
is read from that repository at PopulationRevision and unchanged current-main
blob. Observation IDs/options come from Binding and fresh native project/cohort
identity. Added fixtures cover two products, refused foreign/stale/denied reads,
guarded write/no-op/lost response and decoding. No local SDK or remote board
mutation was admitted. Root must review isolation against both organization
projects and actual product ownership, complete bounded source qualification,
then connect the separately prepared product-creator consumer. No scheduler,
provider lifecycle, default, skills, manifest or shared-doc edit belongs in this slice.

### Portable Python — genuine stage accepted; candidate facts need acceptance

Owner `templates_budget` / root for `V2-LANG-01.2`. Source packet
`B/portable-python-current-receiver-runtime-join-source-20261004`, 17-leaf seal
`ec95b86cbade43e6d7d913e1faf2cf4bf183da0b7fc02fc76a9749399acf1e4e`,
has genuine SDD **2.1.0**, Templates **0.18.0** and original portable candidate
**0.2.1** joins. Protected Coordination workflow source is `0ecb5945...`, full
revision above, workflow SHA-256
`04df921ac74fbee4cead995e8cac17911d19cc7014163eb9c8b3dd5dbecb558e`.

First stage dispatch with a raw commit as `ref` actually failed HTTP 422 before
creating a run. Retained operation
`B/portable-python-current-stage-root-operation-20261004` remains failed.
Small repair used branch `main` after fresh exact-head/empty-queue checks,
leaving all inputs unchanged. Accepted stage run
[37204670941](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/37204670941),
attempt 1, source `0ecb5945b79ca13de2b0dd91d6f05504319c78d2`, passed source
contract/staging and actual archive cleanup. Original artifact **11304282185**
is **228,252,488 bytes**, SHA-256
`9ed4dada9b78f70186ceba6b50d3ae5e5872ad3e3558c14af656440c593ad1d0`.
Root checked all nine closed ZIP members, all package/image/runtime bodies,
canonical provider join and the exact `<pin>` descriptor transformation.
Acceptance is
`B/portable-python-current-stage-main-ref-root-operation-20261004/root-stage-acceptance.json`.
Provider execution and receiver grant creation remain false.

The already-admitted candidate-facts operation completed before the stop:
[37205732748](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/37205732748),
attempt 1 at the same protected source, successful source-contract and
candidate-facts jobs including owned-state cleanup. Operation directory
`B/portable-python-current-candidate-facts-root-operation-20261004`; acknowledged
POST at **13:28:20 UTC**. Do not dispatch it again to recover facts.
Original uploaded artifact **11304363860**, **586,214 bytes**, SHA-256
`40bd592e7457adcf8373d29c386d672d619d7bc5236b4a33d4eee5a9220844c3`,
was downloaded during shutdown only to preserve terminal evidence. Root verified
its full ZIP digest/size and eight member hashes; extraction, actual receiver
commit/tree/profile/inventory joins and root acceptance are still pending.
Saved original ZIP, inventory and native run metadata are under
`B/stop-snapshot-20261004-133731`. There are **eight actual members**, including
`upstream-expected.json`; do not assume a seven-file projection.

Next: accept these actual candidate facts, then prepare the existing private
facts-to-grant route against that receiver. Fresh private facts must bind native
actor/run/attempt/nonce/placement/workflow/source and expires ≤1 hour; grant
absence and owned cleanup are required. Root alone issues a fresh receiver-bound
grant from real facts. No private readiness operation or grant was admitted in
this window. Then qualify runtime-only execute/completed, execute/duplicate and
recover/duplicate with immutable package bytes and owned state. Publication of
accepted **0.2.1** bytes and fresh opt-in creation follow; retained upgrade/P5
and lifecycle/default changes remain separate. No Home operation is needed.

### Learn — local compilation accepted; same-PR CI repair ready, unpushed

Owner `bar_startup_runtime` / root for `LEARN-C2`. Clean worktree
`/home/developer/projects/.worktrees/learn-c2-inactive-source-20261004`, branch
`routine/learn-inactive-preparation-source-20261004`.
Actual finite SDK qualification used **10.0.401**, runtime **10.0.12**, exact
existing authenticated packages and task-private caches without acquisition.
Reader/closure/Host builds passed with zero warnings/errors, actual readonly
synthetic reader and 29 focused pure controls passed; two initial compiler
failures and bounded retries remain retained. Host build took **32.319 seconds**;
whole operation **359.165 seconds**. All owned groups were absent. Evidence
`B/learn-inactive-source-actual-sdk-qualification-20261004`, 30-leaf seal
`2abe022aed92055ef40e16fa9d7c5822c13dea43c6a01d0632fe0dd758ca4610`.
Synthetic reader identity is not genuine Manager-to-Host/OCI/C5 acceptance.

[.github #4195](https://github.com/FS-GG/.github/pull/4195), chain
`LEARN-C2-inactive-preparation`, remains open at remote head
`679ea676be0e866d5e149e0da961c6306c0e4103`. Three real native failures remain:

- Package run **37204918770**: correspondence's locked project graph omitted
  the new existing `PersistentV3.Preparation` transitive edge (`NU1004`).
- Selector run **37204918693**: inactive reader suite was unwired.
- Engine run **37204918756**: actual warm Store admission p95 **104.599 ms**
  exceeds the unchanged **100 ms** limit; startup/RSS/cold CLI passed. Cause
  remains unknown. Engine/Store/performance source is unchanged; this CI wiring
  repair cannot be claimed to repair that runtime result.

Ready local repair `dc5cbea7280bf8b30d6fa4f48b0f6109c0a57ab5`, tree
`c031077cad6112889e882a6be429cc768f5c02dd`, includes protected `94f5` and exactly
three repair files: correspondence lock, focused test `run.sh`, and linear
package-workflow invocation/both path filters. All 20 qualified source mode/blob
tuples remain unchanged. Worker reports real selector **64/64**, zero unexplained
or stale wiring, three actual-path controls and shell/static checks. No extra
local CLR or remote repair push occurred.

Packet `B/learn-inactive-native-ci-repair-20261004`, ten leaves, raw seal SHA-256
`2a22acf93e4647ffa8e1a3060c37c0d64bf202c0e01b4ace021a84ede66be1fc`.
Root read handoff/body; full repair diff/seal review remains. Actual failed engine
artifact **11304263254**, 14,070 bytes / SHA-256
`6565d62cb64ae963ca21c66055a7a8e9e415ae8ec93b9f81f1eaebe40e94295f`,
and failed logs are retained. No causal attribution, budget/sample/warmup change
or manual rerun of the old failure is authorized by this source packet.

After continuation: finish root review, rebind routine marker while retaining
lane marker, push the **same PR**, and obtain normal fresh native checks with
the unchanged performance gate. If it fails again, retain actual phase/custody
evidence and route a bounded diagnosis. Host **0.3.0** lacks the new inactive
reader command; a successor served Host and corresponding Manager distribution
are still required. Two cold OCI builds and genuine inactive Manager-to-Host
qualification precede a real turn, capture, restart/recovery and usage
completeness. Experiment enrollment comes later.

### Akka — current route applicability delivered

Owner Coordination / fdev; no operator reply is pending. Both completed C4
production fixes are present byte-for-byte in owner-reported historical
Coordination source `889827c1c2a0ec3d444b2de0d2c0bd1bcff6d798`, tree
`354641b3d20bfa50823c1ff2f75df97f9cbac2c5`. Exact current ordinary V2, Python
runtime and telemetry receiver contracts select neither legacy hosted-writer
component nor a persistent orchestration Host. Scoped source applicability
packet `B/akka-current-fdev-store-applicability-20261004` contains 13 sealed leaves;
read its full local seal before reuse. The accepted canonical disposition landed in #4196.
This is source/applicability evidence, not exercised installed Akka behavior.
No legacy republication, replacement, Home qualification or repeated O3/Choreo
run is required. Retained legacy store/recovery responsibility survives any
later separately selected retirement.

## Shutdown state, preservation and restart

The original `/home/developer/projects/.github` checkout remains at historical
`f38a0ec1116828c2cf49392b9d0c333f70d3ade7`, untouched. It has 11 modified paths:
the two work-unified skills and two mirrors; Unified, BAR, 4D and substrate
roadmaps; `scripts/generate-driver-manifest`; and two skill-registry test files.
Four continue/stop skill directories in `.agents` and `.claude` are untracked.
Preserve all of them. The handoff uses isolated worktree
`/home/developer/projects/worktrees/unified-roadmap-handoff-20261004-133731`,
branch `docs/unified-roadmap-handoff-20261004-133731`.

No task CLR/compiler/game operation is active. The permanent BAR knowledge
keeper and existing SC2 gateway are infrastructure, not new qualification
processes; neither was retired. A read-only historical API pagination process
started during shutdown was identified and retired through its exact live
identity. The external #930 coherent job above was still running at the snapshot;
it was not claimed cancelled. No publication or store mutation was in flight.
Any fresh local qualification must retain one CPU, at most two task CLR processes
plus the two permanent processes, finite deadlines and exact owned cleanup.
Do not kill stale numeric PIDs, recycle consumed roots/grants, weaken custody
or reconstruct missing old exits/logs.

SC2/product-adapter interrupted observations were closed **cancelled**;
Learn's completed source repair and Python's completed candidate-facts operation
were closed **completed**, with usage reconciliation. Native usage remains
unknown/unsupported where unjoined; metadata does not prove token costs or the
10% bureaucracy ceiling. Earlier dispatches with missing terminal joins remain
an evidence-reconciliation backlog; inspect actual attempts before closing them.
Dashboard activation is advisory inactive, not a new release or runtime gate.

The last remote mailbox comparison at shutdown found unchanged heads:
unified `fd7aba48779585959542cce8928446daf7d65346`, dedicated V2/LEARN
`6f47f256c51f536bd95b3d2d4801e112ee056df1`. Their locations and legacy ownership
labels are in the protected [channel directory](../coordination/worker-channels.md);
apply the current fdev/store architecture rather than reopening Home replacement.
No mailbox watcher or periodic update is installed by this save.

Ordered restart:

1. Read this protected handoff, current Unified §0 and exact live PR heads/checks;
   preserve dirty files and interrupted source. Reconcile #930's external job.
2. Review/push Learn's existing three-file repair on #4195. In parallel, finish
   SC2's minimal installed adapter SOURCE and product-board source qualification.
   Allocate the local CLR pair to only one admitted qualification at a time.
3. Accept the already-produced Python facts from their original ZIP and actual
   new receiver. Prepare fresh private facts and a receiver-bound grant; do not
   repeat successful staging or invent receiver identities.
4. Independently review BAR's Cargo exception/failure-finalization repair, fold
   the latest failure into knowledge, then admit one fresh real six-phase build.
   Join the resulting exact capsule to checked preparation before one-unit play.
5. Integrate exact-head native successes, then product checked adapters,
   publication/adoption and joined guidance. Keep Learn's actual OCI/capture/
   recovery path independent and preserve later upgrade/default gates.

Board and current Akka disposition need no Home-owner answer. Broad carryover,
product-board creation/adoption, Portable Python receiver execution, BAR gameplay,
SC2 actor/resource qualification and Learn capture remain incomplete. Prior
Rendering/Templates, Wizard promotion/receivers, retained upgrades and other
unselected roadmap obligations remain governed by their protected plans; no
checkbox or handoff closes them. After saving, execution and monitoring stop.
