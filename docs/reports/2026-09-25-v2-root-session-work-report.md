# GitHub Substrate V2 root-session work report — 2026-09-25

**Observation period:** 2026-09-25 04:47:05–19:50:12 UTC. The end is a fixed
source-evidence cutoff, not the end of the V2 program. This report records work
performed during that period and the inherited conditions against which it was
done. A draft PR, passing local test, rendered report, or ready telemetry endpoint
is not a protected GS2 receipt, installed receiver, accepted producer, native
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
At this cutoff its late-session table contains **129 distinct source-PR rows**:
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
