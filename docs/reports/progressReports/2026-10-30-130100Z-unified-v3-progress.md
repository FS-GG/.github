---
title: "Development progress — 2026-10-30"
as_of: "2026-10-30T13:01:00Z"
previous_report: "2026-10-29-120600Z-unified-v3-progress.md"
---

# Development progress — 2026-10-30

As of **13:01 UTC, 30 October 2026**. Compared with the [12:06 UTC report of 29 October](2026-10-29-120600Z-unified-v3-progress.md). Scope: all items in FS-GG Coordination Project 3. Later results are outside this snapshot.

Delivery has **sped up sharply**, but on different work from yesterday. Two blockers from yesterday are gone: the owner's [ruling that Root may make all decisions](https://github.com/FS-GG/.github/issues/3010) and the [Unity Personal licence](https://github.com/FS-GG/FS.GG.Unity.Client/issues/7). Today three items were accepted with their children (switchable game rendering, publication-freeze removal and the FourD SDK re-qualification), and two public releases were published and read back. The native runtime chain went the other way. fdev's move into a KVM VM invalidated the prepared run, which was withdrawn, so the useful workload is no further along than yesterday. Throughput is now limited mainly by the process: merges are serialized around main, and board admission is fragile.

| Lane | Meaningful progress | Problem / next step |
|---|---|---|
| Switchable game rendering ([GAME-RENDER-MULTI-01](https://github.com/FS-GG/.github/issues/4930)) | **Usable.** The game template can switch between SVG, PixiJS and BabylonJS through Elmish. The Fable bindings for the current Pixi and Babylon releases were rebuilt. A three-backend browser smoke test ran 187 checks on the real GPU. All four children were accepted, and Atlas records were captured. | No blocker. The independent review noted small inaccuracies in the Atlas record, kept for its next update. Adoption in products continues under [#4646](https://github.com/FS-GG/.github/issues/4646). |
| Workspace template and Wizard ([#3010](https://github.com/FS-GG/.github/issues/3010)) | Workspace.Template 0.20.1 and Wizard (FS.GG.NewSddWorkspace) 0.23.0 are **published and read back**. The Wizard's eight release effects were verified. The registry now records Workspace 0.20.1 and UI 0.32.4. | Not yet shown usable to people. Step 3 (public install, create all four products, build, test, Doctor) is running. The Wizard registry PR [#4957](https://github.com/FS-GG/.github/pull/4957) is open. One recorded executor deviation on the publish run: a login probe was skipped. Root accepted it. |
| Publication overhead ([#4753](https://github.com/FS-GG/.github/issues/4753)) | Accepted. Live runs showed that a stale executor is refused and that a conflicting promotion is refused under the lock. | Lock **queueing was never shown live**, because the attempt was invalidated by an executor polling fault. Economics are inconclusive. Root kept "refuse after main moves" as intended design, and that is the source of today's serialization (see below). |
| SDK security patch ([#4935](https://github.com/FS-GG/.github/issues/4935)) | Wave 0 moved to SDK 10.0.403 in Audio, Net, FsQuint, Governance, Unity.Client, SDD and .github. FourD was re-qualified ([#4944](https://github.com/FS-GG/.github/issues/4944)). | Rendering stays on 401 because its release toolchain binds that version. Four follow-ons ([#4943](https://github.com/FS-GG/.github/issues/4943), [#4945](https://github.com/FS-GG/.github/issues/4945)–[#4947](https://github.com/FS-GG/.github/issues/4947)) are open and unenrolled, held by the board's 100-row cap. #4935 itself has no Status on the board. |
| Coordination overhead ([COORD-EFF-02](https://github.com/FS-GG/.github/issues/4730)) | The two unowned designs were found and filed. The read-only context view ([#4948](https://github.com/FS-GG/.github/pull/4948)) and the batched enrollment planner ([#4950](https://github.com/FS-GG/.github/pull/4950)) are merged. | Both are source only, with no demonstrated saving yet. The preflight PR [#4956](https://github.com/FS-GG/.github/pull/4956) is open. Batched enrollment is what releases the queued follow-ons above. |
| Unity native operation ([Unity.Client#7](https://github.com/FS-GG/FS.GG.Unity.Client/issues/7)) | **Unblocked.** The licence activated headlessly on fdev without seat churn. Import-compile and the Linux dedicated-server build and smoke are merged, with a hardened runner. | The networking step ([#14](https://github.com/FS-GG/FS.GG.Unity.Client/pull/14)) is open. The Unity container breaks when shared podman tmp is recreated. |
| Native runtime and BAR ([#4778](https://github.com/FS-GG/.github/issues/4778), [#4650](https://github.com/FS-GG/.github/issues/4650)) | Root withdrew the Home d2f2 selection, which never started, because the migration changed its inputs. A plan to re-host the chain inside fdev ([#4939](https://github.com/FS-GG/.github/pull/4939)) was reviewed and revised, and Root recorded its decisions. | No workload, commit or cleanup progress, and yesterday's closure-guard diagnosis is superseded. The plan awaits re-review. I/O correspondence cannot be shown on the fdev path, so that assertion stays open. BAR qualification and shared-native consumers stay behind this. |
| Unchanged blocked lanes | Telemetry adoption, LEARN, portable execution, typed administration, SVG coherence, Fable accessibility and two-machine portability keep their earlier accepted milestones. | No movement today. See the blockers below. |

The **13:02 UTC native board read** returned 98 memberships: 73 Done, 17 Blocked, 7 In progress and 1 with no Status (#4935). Yesterday it was 91: 66 Done, 21 Blocked, three In progress and one Backlog. These are planning counts, not completion percentages. The installed inspection at about 11:00 UTC saw 105 memberships, before Root removed seven auto-added cards, so it does not describe the current population and is not reused for counts. It recorded zero mutations. This report made no board write.

## Token efficiency

Today's Root, child and follow-up token usage is **Unknown, not zero**. No source available today can measure it:

| Dimension | Observed usage | Coverage / interpretation |
|---|---|---|
| Native tokens (input, cached, output, reasoning) | Unknown | `telemetry workspace status` reports `unconfigured`, and the runtime check reports store and workspace `unconfigured` on the new fdev VM. Yesterday's report still saw a configured local client, so capture appears to have been lost in the migration. That is suspected, not proven. |
| Local Codex counter diagnostics | None | The maintained `collect-local.py` route reads only Codex sessions, and there are no Codex sessions for 30 October. Root ran in Claude Code, which this route does not cover. |
| Delivery activity (not cost) | 42 merged PRs in 12 repositories since 00:00 UTC. Seven issues accepted as completed. | Activity and acceptance counts only. PR count is not an acceptance denominator. |

Tokens per accepted Delivery, the cost of open work and any money figures are **unmeasured**. The last compatible baseline is the [23–24 October census](../2026-10-24-usage-census-4639.md). It covered PR, review and Actions-time coverage, not today's tokens, so there is no defensible comparison. Restoring the telemetry workspace configuration on fdev is an operator action for Root. Reporting did not activate it.

## Process problems we can fix

- **Merges are serialized around main.** The registry graph seal from [#4951](https://github.com/FS-GG/.github/pull/4951) breaks whenever main moves, and CI cannot detect a stale seal. Successor publishers also refuse after any main movement, which Root confirmed as intended under ADR-0097. As a result, Root held main through each publication and recomputed and re-reviewed the seal after every merge. `routine-eligibility` failed on stale bases whenever main moved. *Effect:* independent PRs waited in line, and every publication window froze the repository. *Change (Root, under #3010):* add a CI check that recomputes the seal and fails when it is stale, so drift is caught on the PR and not by a person. Treat the seal and successor-admission bindings as candidates for the [#4730](https://github.com/FS-GG/.github/issues/4730) designs. Relaxing ADR-0097 is a decision, not a fix. Root declined the narrow relaxation today and could revisit it once queueing has been shown live.
- **Board auto-add bypasses admission and breaks guarded writes.** Project 3's auto-add put seven newly filed issues on the board unadmitted. After that, the installed caller refused every Status preparation, and inspection failed 67 reads, with only a coarse refusal code ([Root's record](https://github.com/FS-GG/.github/issues/4941#issuecomment-6520038552)). Root removed the cards. *Change:* disable or narrow the auto-add workflow for Project 3. That is a board-configuration decision for Root or the owner. Also give the caller specific refusal codes (#4941). This failure will recur on every filing until auto-add is changed.
- **Negated closing keywords close issues.** A PR body that said an issue would *not* be closed still contained a closing keyword next to the reference. As a result, [Game#730](https://github.com/FS-GG/FS.GG.Game/issues/730) was shut automatically at 03:09 and reopened by Root at 03:21. A second case was caught just in time. *Change (Root):* write `Refs` only, and add a cheap PR-body lint that flags closing keywords near issue references.
- **Model-driven polling and shared scratch cause bad runs.** An executor's polling loop hit its iteration cap and dispatched early, which invalidated the #4753 queueing control. A scratch file shared between sessions was overwritten and briefly published a wrong PR body. *Change:* use bounded deterministic waits (`--watch-checks`, `gh run watch`) and a per-session `mktemp` directory. The benefit is plausible but unmeasured.
- **Review loops were productive.** The "REQUEST_CHANGES, repair, re-review" cycles caught real defects: runner hardening on Unity #12, substantive plan findings on #4939, and record inaccuracies on #4954. They are not waste. The cost to remove is the re-review triggered only by seal recomputation after main moved, not the review itself.

These findings stay with Root under #4730 and #3010. This report creates no separate findings ledger.

## Blockers the current process cannot resolve

- **Accessibility participants** for AT and IME journeys ([#4817](https://github.com/FS-GG/.github/issues/4817)). Only real participants can supply them.
- **Governance 423(b) evidence** for portable execution ([SDD#928](https://github.com/FS-GG/FS.GG.SDD/issues/928), [#4648](https://github.com/FS-GG/.github/issues/4648)). The supplier owns it.
- **Two independent hosted KVM machines** for unattended portability ([#4821](https://github.com/FS-GG/.github/issues/4821)). fdev being a VM does not supply the second machine.
- **Home's confirmation of the fdev VMM device set** after GPU and audio were added, [requested today](https://github.com/FS-GG/.github/issues/4517). The security-zone facts are out of date until Home answers.
- **Genuine I/O correspondence** for #4778. Root ruled that the fdev path cannot supply it. It needs Home-side evidence or an explicit later ruling.

More agents or retries cannot supply any of these, and none has a reliable date. Yesterday's NuGet verification-key boundary was not re-checked for this report.

## Priorities

1. **Remove the main-serialization tax.** Add a stale-seal CI check and disable or narrow Project 3 auto-add. Together these unblock parallel merges and enrolling the queued SDK and COORD-EFF follow-ons.
2. **Finish proving the releases are usable.** Complete #3010 Step 3's public journeys and reconcile the Wizard registry entry, so the published 0.20.1 and 0.23.0 become something people can actually use.
3. **Get the native chain back to a runnable selection.** Re-review the revised #4939 and admit one concrete fdev run aimed at the first useful workload. Keep I/O correspondence and all retained holds visible.

Sources: [Coordination board](https://github.com/orgs/FS-GG/projects/3), [#3010 Wizard publication record](https://github.com/FS-GG/.github/issues/3010#issuecomment-6523297488), [#4753 acceptance](https://github.com/FS-GG/.github/issues/4753#issuecomment-6519024139), [#4778 rehost decisions](https://github.com/FS-GG/.github/issues/4778#issuecomment-6522829725), [telemetry reference](../../reference/local-telemetry-store.md). Atlas records `environment/fdev-kvm-vm`, `environment/fdev-vm-is-the-only-security-zone` and `environment/fdev-gpu-and-audio` give the environment context. They were not re-verified against live host facts for this report. Private paths and credentials are excluded.
