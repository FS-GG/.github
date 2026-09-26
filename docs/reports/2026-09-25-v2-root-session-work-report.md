# GitHub Substrate V2 root-session work report — 2026-09-25

**Observation period:** 2026-09-25 04:47:05–19:50:12 UTC, with a verified
post-cutoff addendum through 2026-09-25 21:05:31 UTC and a continuity
checkpoint through 21:26:28 UTC below. Their source-PR sets were frozen at
21:04:46 and 21:24:30 UTC, respectively. Neither cutoff ends the V2 program.
A draft PR, passing local test, rendered report, or ready telemetry endpoint is
not a protected GS2 receipt, installed receiver, accepted producer, native
effect, or production cutover.

## Coverage and evidence method

The session followed the [V2 roadmap](../github-substrate-v2-roadmap.md), its
[F# automation convergence design](../coordination/2026-09-25-fsharp-automation-convergence-design-and-roadmap.md),
the owning repositories' PRs, and the [roadmap draft PR #3697](https://github.com/FS-GG/.github/pull/3697).
The design inventoried all **17 repositories** returned by the authenticated
organization listing on 2026-09-25. Its executable census covers `.github`,
Coordination, SDD, Governance, Templates, Rendering, Game, Audio, Net, FsQuint,
the two state repositories, generated receivers, and remaining roster
dispositions. Nine organization repositories map into the authority registry;
one is outside the fabric, seven lack a roster disposition, and the tenth
registry row is external. `.py`/`.sh` suffix counts are tracked-file inventory,
not a count of runnable commands or migration effort; extensionless scripts,
workflow bodies, generated receivers, and installed pins were separately
identified. Private qualification repository identities are omitted here.

The [roadmap section 1.0 source ledger](../github-substrate-v2-roadmap.md#10-qualification-strength-at-child-and-parent-boundaries)
is the public **exact PR/head/test/hold trail** used for the active V2 and
FSC-03/04/05 lanes; the convergence design links the additional product-owner
routes.
At this cutoff its [immutable late-session table](https://github.com/FS-GG/.github/blob/abf738ec4c2bcf101cf2ae3f147e4b83acd8cec0/docs/github-substrate-v2-roadmap.md#L982-L1110)
contains **129 distinct source-PR rows**:
31 GS2-09.9, 24 GS2-09.7, 30 FSC-03, 15 FSC-04, 14 FSC-05, and 15
telemetry rows. Earlier session drafts and repairs appear in the linked prose
immediately before that table and in the convergence design; the 129 rows are
therefore a table count, not a total number of session PRs or accepted changes.
The report below groups that ledger by outcome and gives direct PR links for
the key edges. It does not silently include later notices: [telemetry #767](https://github.com/FS-GG/FS.GG.Coordination/pull/767)
was verified at 19:50:18 UTC, six seconds after this cutoff, and belongs in the
next update. Nonpublic qualification packets, credential material, raw private
turn contents, and post-cutoff work are outside this public report.

## Inherited baseline, separate from this session

Before 04:47 UTC, the protected roadmap had already accepted GS2-00/01,
GS2-02–07, and GS2-08.1–08.9; [V2-CI-I1](../github-substrate-v2-roadmap.md#v2-ci-i1--unattended-credential-execution-interlude)
was qualified on 2026-09-24. Those are **baseline**, not accomplishments of
this root session. The [GS2-08.2 accepted ledger-protection receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-08.2.json)
is likewise prior evidence. The earlier trust-anchor mismatch remains
unresolved inherited context; this session neither changed keys nor
requalified GS2-08.2. Production authority remained **v1** throughout the
period. No `OpenV2` transition or cutover was authorized or performed.

## Work performed during this root session

| Stream | Source work and direct evidence through the cutoff | Status and remaining gate |
| --- | --- | --- |
| **GS2-09.9 callable ordinary V2** | The [Q3/Q6 characterization #547](https://github.com/FS-GG/FS.GG.Coordination/pull/547) exposed wrong-head/base acceptance after ambiguous POST, force-push protection acceptance after ambiguous PUT, and exception-chain secret retention. [Versioned operator #549](https://github.com/FS-GG/FS.GG.Coordination/pull/549), [loopback transport #551](https://github.com/FS-GG/FS.GG.Coordination/pull/551), and [v5 harness #550](https://github.com/FS-GG/FS.GG.Coordination/pull/550) supplied repair and offline controls. The later ledger records sealed-plan, identity, journal, request, one-send and token-free custody slices through [#759](https://github.com/FS-GG/FS.GG.Coordination/pull/759). [#761](https://github.com/FS-GG/FS.GG.Coordination/pull/761) adds a separate no-grant entry that exits 78 before protected ports; [#764](https://github.com/FS-GG/FS.GG.Coordination/pull/764) builds and verifies a deterministic local ZIP candidate for that entry. | **Source drafts only.** #764 head `ed4b7fab43f892920d032f4ebe3f9f8a1796b802`, committed 19:45:52 UTC; nine focused/adjacent tests and two equal clean builds. The archive was not committed or protected-installed. [#545](https://github.com/FS-GG/FS.GG.Coordination/pull/545) remains disputed; #550's installed-provider/native-effect gate remains held. |
| **GS2-09.7 isolated rehearsal** | Source-only issue, settings, inventory, custody, signed-seal and journal work is recorded from the early characterization through [#758](https://github.com/FS-GG/FS.GG.Coordination/pull/758), [#762](https://github.com/FS-GG/FS.GG.Coordination/pull/762), and [#766](https://github.com/FS-GG/FS.GG.Coordination/pull/766). #762 binds a one-use signed-attestation claim to the exact run/target/store; #766 adds exact prehead generation/digest CAS, deterministic successor and final-head readback. | **Source drafts only.** #766 head `719dd2a9f46731336f50123eb46edcfbeb1e3254`, committed 19:46:49 UTC; full unit suite 737/737. Protected atomic journal, native store/head freshness, authenticated signer/clock, complete initial census, full typed inspect and Q5/Q6 are still open. [`.github` #3690](https://github.com/FS-GG/.github/pull/3690) was merged during this period outside the common OperatingV1 effect-admission boundary; the process deviation was reported. That merge is not GS2-09.7 acceptance and dispatched no sandbox run. |
| **FSC-03 organization policy** | [F# YAML/parser scaffold #3701](https://github.com/FS-GG/.github/pull/3701), [rule (a) source #3704](https://github.com/FS-GG/.github/pull/3704), Python [path-coherence repair #3698](https://github.com/FS-GG/.github/pull/3698), and subsequent registry, permissions, MSBuild, Git object, GraphQL and REST drafts produced the ledger's FSC-03 chain. Late controls include non-UTF-8 charset [#3824](https://github.com/FS-GG/.github/pull/3824), bearer syntax [#3825](https://github.com/FS-GG/.github/pull/3825), terminal-newline Git SHA refusal [#3826](https://github.com/FS-GG/.github/pull/3826), and terminal-newline repository identity refusal [#3827](https://github.com/FS-GG/.github/pull/3827). | **Dormant source, not live policy.** #3827 head `016da99fec14523e9e7313c7d85ea68f7fa923ca`, verified 19:49:36 UTC; F# suite 272/272. The Python gate remains live. Rule (a) parity waits for accepted #3698; authenticated roster, credential/commit pin, evaluated MSBuild graph, provider installation, and receiver parity remain open. |
| **FSC-04 SDD/projection source custody** | [SDD #1000](https://github.com/FS-GG/FS.GG.SDD/pull/1000) began generic source-closure work; the ledger tracks closed roots, physical paths, pinned Linux descriptors, multi-root/ABA counterexamples, optional selected-Git-commit preview and [#1048](https://github.com/FS-GG/FS.GG.SDD/pull/1048). [#1049](https://github.com/FS-GG/FS.GG.SDD/pull/1049) compares observed opened-file executable mode with selected Git mode and checks mid-read chmod. | **Read-only observation.** #1049 head `d1bcc2aed975a47668c68e9ab59b7c61b80cbff1`, committed 19:46:56 UTC; seven focused and 1,537 Commands tests, Release build without warnings/errors. Matching sequential captures do not prove a common instant; ABA, complete-source, producer authorization, installed receiver and effect remain open. |
| **FSC-05 Templates provider/archive** | [Live Python floor repair #498](https://github.com/FS-GG/FS.GG.Templates/pull/498), [F# provider reducer #497](https://github.com/FS-GG/FS.GG.Templates/pull/497), [composition #499](https://github.com/FS-GG/FS.GG.Templates/pull/499), workspace/ZIP/descriptor/archive controls and the source ledger through [#592](https://github.com/FS-GG/FS.GG.Templates/pull/592) were drafted and tested. | **No selected release verdict or receiver flip.** #592 has 34 custody and 101 payload controls, but selected native versus retained archive remains `NO_VERDICT`; producer/served bytes, transaction CAS and installed parity remain separate. The Templates worker later moved to telemetry source qualification. |
| **FSC-06/08 product policy and archive primitives** | Audio [Python staging repair #312](https://github.com/FS-GG/FS.GG.Audio/pull/312) and [F# reducer #313](https://github.com/FS-GG/FS.GG.Audio/pull/313) plus [pinned capture #317](https://github.com/FS-GG/FS.GG.Audio/pull/317); Game [staging repair #644](https://github.com/FS-GG/FS.GG.Game/pull/644), [F# reducer #645](https://github.com/FS-GG/FS.GG.Game/pull/645) and follow-ons through [#655](https://github.com/FS-GG/FS.GG.Game/pull/655); Rendering [custody #1330](https://github.com/FS-GG/FS.GG.Rendering/pull/1330) and physical/mode source through [#1339](https://github.com/FS-GG/FS.GG.Rendering/pull/1339); FsQuint [archive reducer #15](https://github.com/FS-GG/FsQuint/pull/15) and guards through [#23](https://github.com/FS-GG/FsQuint/pull/23). | **Owner-scoped drafts and open repair routes.** Product-specific canonical bytes, negative oracles, packed/served archive identity, output rollback and clean installed parity still govern adoption. No product receiver was flipped by this report. |
| **FSC-00/01/02 and roadmap** | [Executable census #3700](https://github.com/FS-GG/.github/pull/3700), direct repairs [CI output #3696](https://github.com/FS-GG/.github/pull/3696), [effective-step wiring #3699](https://github.com/FS-GG/.github/pull/3699), [skill roster #3706](https://github.com/FS-GG/.github/pull/3706), and [telemetry/Core closure #3702](https://github.com/FS-GG/.github/pull/3702) support the owner roadmap. [Roadmap #3697](https://github.com/FS-GG/.github/pull/3697) gained the convergence plan, aggressive safe-lane capacity rule, one reserved direct-V2 worker, explicit GPT-6-Sol/high launch evidence, telemetry readiness/capture split, ten-minute reporting, and fixed source cutoffs. | **Roadmap/report branch is draft.** It changes no protected milestone checkbox. The pre-report pushed roadmap head was `acaab98c265fb6cb87b11c3e0ce34b72e446413e` at 19:48:30 UTC; this report's later commit has its own head. |

### Architecture and code-review response

The [convergence design's findings section](../coordination/2026-09-25-fsharp-automation-convergence-design-and-roadmap.md#findings-requiring-direct-repair)
contains the reproducible cases and links for all review-driven repairs. The
most consequential false greens were: a newline in a matched CI path creating
an extra workflow output; filtered/unfiltered PR/push path splits and YAML
marker impersonation; checker names present only in comments or inert shell;
empty/foreign skill rosters; malformed Templates provider roots; duplicate
JSON/BOM/ZIP members; Game and Audio staging outside declared source closure;
and mismatched canonicalization between producer and stager. The response used
independent negative fixtures before each narrow Python or F# repair. It also
identified load-bearing Linux descriptor, credential, process and ambiguous-CAS
behavior in Coordination, so the migration plan retains those semantics and
independent Python oracles instead of treating a language port as proof.

The design assigns generic artifacts to SDD, organization policy to `.github`,
execution to Coordination, composition to Templates, and product policy to
each product owner. It permits source scaffolds, characterization and read-only
parity in parallel while serializing producer publication, receiver pins,
protected merges and effects. Optional ports are not selected into GS2-10 at
this checkpoint. V1-only Q9 contraction is an independent future obligation;
it is not contingent on finishing every F# port.

## Telemetry, reporting, and lane discipline

At the 19:49:01 UTC checkpoint, authenticated `fdev-telemetry health` was
`ready`; workspace `main-fsharp-dev` was `configured`, with `pending=0`,
`pendingUnacknowledged=0`, and `unacknowledgedLossy=false`. These facts prove
**readiness only**. The separate acceptance still needs one genuine work item
through the instrumented orchestration runner, native turn IDs and usage, an
**applied Host receipt for that exact workspace/item**, and a later authenticated
zero queue. Board admission remained blocked because the current credential
could read the selected Coordination board but received an access denial while
the installed bootstrap enumerated another organization project. Source-only
direct-board and direct-session producer drafts in the
[roadmap telemetry ledger](../github-substrate-v2-roadmap.md#10-qualification-strength-at-child-and-parent-boundaries)
were neither installed nor a captured turn. This direct interactive session is
outside that runner; a credential wrapper alone emits no native turn record.
No Host capture of this transcript is claimed.

[F# progress-renderer draft #3735](https://github.com/FS-GG/.github/pull/3735)
produced a typed snapshot validator, semantic Markdown labels with visible
text, a newest-five UTC completion table, required nonblank `CurrentWork` for
running lanes, and a script workflow at `scripts/v2-progress/run.sh`.
Its exact draft head at this cutoff was
`48ef2d177c4d209b81ed979110d62bf8b7018f90`; 25 focused tests passed.
The wrapper refreshes authenticated telemetry and local native JSONL counters;
lane/tasks, PR heads, checks and completion history must be freshly rebuilt in
metadata before each ten-minute report. The 19:49 report used six active
GPT-6-Sol/high lanes: orchestrator, reserved direct GS2-09.9 worker, direct
GS2-09.7 worker, FSC-03 worker, FSC-04 worker, and telemetry source worker.
Runtime self-introspection was unavailable; explicit launch settings are the
recorded worker evidence. Finished lanes were refilled with disjoint source
tasks rather than held behind hosted queues.

The 19:49 renderer scanned 30 local root-family sessions and 24,619 native
`token_count` events. Its latest completed ten-minute team delta
(19:37:05–19:47:05 UTC) was **41,684,024 aggregate tokens**: 41,577,465
input, including 41,309,056 cached and 268,409 noncached input, plus 106,559
output. Across 90 completed root-anchored periods, including zero-use periods,
the all-period **team** mean was 31,764,640.19 tokens per period. The local
weekly counter showed 93% used at 19:49:01 UTC, reset
2026-09-30 07:28:28 UTC; the same-reset continuous-use, account-wide slope
projected approximately 21:36 UTC exhaustion. These are **local, unverified
diagnostics** without authenticated collector/account scope or a matching
applied Host receipt. They do not establish telemetry capture or GS2-09.9.

## Tests, protected holds, and next authoritative gates

The cited PRs carry their own exact-head fixtures and suite counts. For this
roadmap branch, the 19:49 evidence update passed `git diff --check`, all
**19/19** prose-citation fixtures, and the live prose corpus check (**394**
documents, **55** local citations, **221** section citations). The F# progress
workflow built in Release with zero warnings/errors and rendered a six-row
lane/task table from freshly checked metadata. Those checks establish source
quality and report consistency, not installed or protected acceptance.

The Coordination protected merge sequence remains
[#532](https://github.com/FS-GG/FS.GG.Coordination/pull/532) →
[#527](https://github.com/FS-GG/FS.GG.Coordination/pull/527) →
[#526](https://github.com/FS-GG/FS.GG.Coordination/pull/526) →
[#529](https://github.com/FS-GG/FS.GG.Coordination/pull/529), under the owner
gate and fresh exact-head qualification. The GS2-09.9 next gate is an
independently reviewed, runnable exact integrated artifact/workflow, followed
by protected identity, grant, one-attempt native effect and readback under
#550. The GS2-09.7 next gate is complete census and native protected
store/journal/attestation custody before Q5/Q6 and any isolated rehearsal.
Telemetry end-to-end capture remains a separate runner/Host proof. F# ports
need accepted source repairs, exact producer artifacts, clean installed
receivers and owner qualification before any live replacement. Apart from the
separately disclosed #3690 process deviation, this reporting lane performed
no further protected merge, sandbox mutation, immutable producer publication
or receiver pin, live gate flip, Authority write, or cutover. This report
authorizes none of those actions.

## Verified addendum: 19:50:12–21:05:31 UTC

The source-PR set was frozen at **21:04:46 UTC** in roadmap head
`edb2f038d6df732ed9608b5b22dfb39d88458960`; telemetry and the F#
progress observation followed at **21:05:31 UTC**. Comparing the earlier
129-row table with the [immutable post-cutoff ledger](https://github.com/FS-GG/.github/blob/edb2f038d6df732ed9608b5b22dfb39d88458960/docs/github-substrate-v2-roadmap.md#L1126-L1211)
gives **86 additional distinct source-PR rows**, each with an exact head,
tests and an explicit hold: 14 GS2-09.9, 15 GS2-09.7, 16 FSC-03, 10 FSC-04,
8 FSC-05 and 23 telemetry. The two tables contain 215 rows in total. They
index drafts, not accepted changes or every earlier session repair. Drafts
verified after 21:04:46 UTC, including [Templates #601](https://github.com/FS-GG/FS.GG.Templates/pull/601)
and [Coordination #819](https://github.com/FS-GG/FS.GG.Coordination/pull/819),
[#820](https://github.com/FS-GG/FS.GG.Coordination/pull/820) and
[#821](https://github.com/FS-GG/FS.GG.Coordination/pull/821), are excluded.

| Stream | Verified source result through the cutoff | Remaining boundary |
| --- | --- | --- |
| GS2-09.9 | Fake-port no-grant, issuer and verifier custody drafts culminated in the [owner packet #806](https://github.com/FS-GG/FS.GG.Coordination/pull/806), [verifier packaging comparison #810](https://github.com/FS-GG/FS.GG.Coordination/pull/810) and [provider-response refusal #816](https://github.com/FS-GG/FS.GG.Coordination/pull/816). #816 reproduced direct classifier false greens after explicit 302/401 responses; 66 focused tests passed. | The real verifier route, approved key/revocation/nonce custody, integrated runnable artifact, installed no-grant observation and separate #550 target/App/CAS/grant decision remain owner-held. #545 is disputed; no native effect occurred. |
| GS2-09.7 | Fake protected handoff and read-only recovery drafts added [signed snapshot verification #807](https://github.com/FS-GG/FS.GG.Coordination/pull/807), [clock identity #812](https://github.com/FS-GG/FS.GG.Coordination/pull/812), [marker readback #814](https://github.com/FS-GG/FS.GG.Coordination/pull/814) and [structural selection #817](https://github.com/FS-GG/FS.GG.Coordination/pull/817); #817 passed 763 full unit tests. | Both recovery paths remain hold-only. Protected signer/reader/store authenticity, #3690 adjudication, complete census and Q5/Q6 remain open. No sandbox run or token action occurred. |
| FSC-03 | Dormant F# policy gained Git object, tree, alias and raw-commit controls through [#3843](https://github.com/FS-GG/.github/pull/3843); four Git-rejected identity fixtures went red before repair, and the F# suite passed 307/307. | The live Python gate remains in force; #3698, provider/evaluated graph and installed receiver parity remain open. The optional worker moved to FSC-05. |
| FSC-04 | SDD's selected-commit preview gained object-store, pack, fanout, loose-leaf and no-lazy-fetch controls. [#1059](https://github.com/FS-GG/FS.GG.SDD/pull/1059) verified selected blob bytes against SHA-1/SHA-256 object IDs; 1,579 Commands tests passed. | Commit/tree integrity, handle custody, swap-back ABA, complete sources, common-instant proof and installed parity remain open. The preview is read-only and non-authorizing. |
| FSC-05 | Templates F# provider source resumed from [#593](https://github.com/FS-GG/FS.GG.Templates/pull/593) through [#600](https://github.com/FS-GG/FS.GG.Templates/pull/600). The latest draft refused unquoted YAML null spellings against an independent YAML parser; exact-head checks passed 68 F#, 39 provider-tool and 34 archive custody controls. | Several F# refusals are deliberately stricter than live Python and do not establish installed parity. The selected archive remains `NO_VERDICT`; no producer publication or receiver flip occurred. |
| Telemetry source | Dormant native event and usage parsers advanced through [#818](https://github.com/FS-GG/FS.GG.Coordination/pull/818), which bound candidate usage-frame digests to structurally replayed journal digests; 39 focused and 251 full Release tests passed. The separate direct-board drafts [#3751](https://github.com/FS-GG/.github/pull/3751), [#3752](https://github.com/FS-GG/.github/pull/3752) and [#3772](https://github.com/FS-GG/.github/pull/3772) were repaired/rebased, with local signature-doc-siting 90/90 and adapter suites 844/846. | Structural matches remain `NO_VERDICT` for trusted journal custody or completed-turn usage. Hosted checks, owner admission, installed runner wiring, a genuine item and matching applied Host receipt remain pending. |

At the 21:05 checkpoint, six GPT-6-Sol/high lanes were active: the
orchestrator, a **reserved direct GS2-09.9** worker, a direct GS2-09.7 worker,
and separate FSC-05, FSC-04 and telemetry source workers. The
[F# progress-renderer draft #3735](https://github.com/FS-GG/.github/pull/3735)
rendered typed six-lane checkpoints with current tasks, frozen source heads,
newest-five completion history and protected holds. Its latest completed
10-minute **team** period (20:47:05–20:57:05 UTC) had 34,396,777 local
diagnostic tokens: 34,299,946 input, including 33,962,880 cached and 337,066
noncached input, plus 96,831 output. Across 97 completed periods since
04:47:05 UTC, including zero-use periods, the team mean was 32,050,131.92
tokens per period. A local weekly-rate observation at 21:04:59 UTC showed
98% used and a continuous-use, account-wide projection of about 21:35 UTC.
These JSONL figures lack authenticated collector/account provenance and are
**not** runner/Host capture evidence.

At **21:05:31 UTC**, authenticated telemetry health was ready and workspace
status configured with `pending=0`, `pendingUnacknowledged=0` and
`unacknowledgedLossy=false`. End-to-end acceptance still requires a genuine
instrumented runner work item, native turn IDs and usage, a matching **applied
Host receipt**, and a later authenticated zero queue. This direct interactive
session remains outside that runner. Neither readiness nor local counters
clear GS2-09.9's installed-provider/native-effect hold.

The protected Coordination order remained
[#532](https://github.com/FS-GG/FS.GG.Coordination/pull/532) →
[#527](https://github.com/FS-GG/FS.GG.Coordination/pull/527) →
[#526](https://github.com/FS-GG/FS.GG.Coordination/pull/526) →
[#529](https://github.com/FS-GG/FS.GG.Coordination/pull/529). At the 21:02 UTC
read-only check, #532 was open/behind without a review decision, #527
open/blocked, and #526/#529 draft/behind. Apart from the separately disclosed
#3690 process deviation in the base period, no further protected merge,
sandbox mutation, immutable producer publication or receiver pin, live gate
flip, Authority write, or cutover was performed through this addendum cutoff.

## Continuity checkpoint: 21:05:31–21:26:28 UTC

The second source set was frozen at **21:24:30 UTC** and pushed as the
[immutable exact-head roadmap ledger](https://github.com/FS-GG/.github/blob/06ae99c8a23a268d80dc39b4e2fa2187bf8d259d/docs/github-substrate-v2-roadmap.md)
at `06ae99c8a23a268d80dc39b4e2fa2187bf8d259d`. It adds bounded draft
source for GS2-09.9, GS2-09.7, FSC-04, FSC-05 and telemetry. The latest five
verified draft completions at that cutoff were [GS2-09.7 #833](https://github.com/FS-GG/FS.GG.Coordination/pull/833),
[GS2-09.9 #832](https://github.com/FS-GG/FS.GG.Coordination/pull/832),
[FSC-05 #605](https://github.com/FS-GG/FS.GG.Templates/pull/605),
[FSC-04 #1061](https://github.com/FS-GG/FS.GG.SDD/pull/1061) and
[telemetry #831](https://github.com/FS-GG/FS.GG.Coordination/pull/831).
Later drafts, including [FSC-05 #606](https://github.com/FS-GG/FS.GG.Templates/pull/606),
are excluded from this checkpoint even though the live roadmap can advance.
These are draft PRs, not accepted GS2 receipts or installed tooling.

At 21:26:28 UTC, the [F# progress renderer #3735](https://github.com/FS-GG/.github/pull/3735)
produced a diagnostic snapshot. At that observation,
six GPT-6-Sol/high lanes were active: the orchestrator; a reserved direct
GS2-09.9 worker on native response/readback identity; a direct GS2-09.7
worker on recovery identity; and separate FSC-05 provider, FSC-04 packed Git
object, and telemetry native-usage schema workers. The local JSONL diagnostic
for the latest completed ten-minute team period (21:07:05–21:17:05 UTC) was
32,213,057 tokens; the all-period team mean across 99 completed periods,
including zero-use periods, was 32,081,260.82 tokens per period. A local
weekly-rate observation at 21:26:23 UTC reached **100% used**, but its
account/collector scope is unverified and **no actual CLI limit failure had
been observed at this checkpoint**. These figures do not prove runner/Host
capture.

Authenticated telemetry health and workspace status at **21:26:28 UTC** were
ready/configured with `pending=0`, `pendingUnacknowledged=0` and
`unacknowledgedLossy=false`. The same credential still read Coordination
Project 1 but was denied Project 2 at the 21:17 UTC read-only recheck. A
genuine instrumented runner item, native turn IDs and usage, matching applied
Host receipt and later queue-zero observation therefore remain absent. The
protected Coordination order #532 → #527 → #526 → #529, GS2-09.7 Q5/Q6,
GS2-09.9 #545/#550 installed-provider/native-effect gate, and all cutover
holds remain. Apart from the separately disclosed earlier #3690 process
deviation, no further protected merge or effect occurred through this cutoff.
