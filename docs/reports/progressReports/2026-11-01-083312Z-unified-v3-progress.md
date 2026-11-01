---
title: "Development progress — 2026-11-01"
as_of: "2026-11-01T08:33:12Z"
previous_report: "2026-10-31-091621Z-unified-v3-progress.md"
---

# Development progress — 2026-11-01

As of **08:33 UTC, 1 November 2026**. The host clock and GitHub's server `Date` header agree on this time. Compared with the [09:16 UTC report of 31 October](2026-10-31-091621Z-unified-v3-progress.md). Scope: all of FS-GG Coordination Project 3, unfiltered (native read of 239 cards).

Clock caveat: the GitHub API stamps Root's latest merges (#5147, #5162, #5161, #5152) at 09:17–09:20Z and the closures at 09:14–09:31Z. Both are later than the server's own clock. The merge commits record 08:17–08:20Z. I quote API stamps as given and treat them as roughly one hour fast.

Delivery is **advancing, and the board is readable again**. Root acted on all three of the last report's priorities:
- **Board.** The 124-card jump was traced to a briefly enabled auto-add. Coherent 0.114.0 and then 0.114.1 were published and installed. The first batched enrollment initialized 114 cards with 0 wrong values. Every one of the 239 cards now has a Status, against 94 without one before.
- **Outcomes accepted.** Telemetry collection ([#4638](https://github.com/FS-GG/.github/issues/4638)) was accepted, the first Outcome closure in two reports. So was supported-route enrollment ([#4941](https://github.com/FS-GG/.github/issues/4941)), a Delivery-level card.
- **Throughput.** 62 PRs merged in about 23 hours, with two idle gaps (about 6 hours on 31 October and 2.5 hours overnight). 45 Deliveries closed.

The binding constraint has moved. Without Home, Home-only evidence is now deferred by owner ruling rather than awaited. **fdev itself is now the limit**: load 21–25 on 12 cores at the snapshot, with two Ready lanes waiting for capacity.

| Lane | Meaningful progress | Problem / next step |
|---|---|---|
| Telemetry ([#4638](https://github.com/FS-GG/.github/issues/4638) accepted 10/10; [#4640](https://github.com/FS-GG/.github/issues/4640) 4/8) | **Usable and accepted.**<br>- Backlog wave 2b reached exact identity parity: 11,501 Claude facts in 499 receipts.<br>- Continuous Claude capture is live on a 5-minute timer, capped at 300 receipts a day.<br>- Dispatch attribution is qualified for `codex-exec` ([#5041](https://github.com/FS-GG/.github/issues/5041)). | **Claude dispatch attribution is explicitly unsupported**: the engine rejects subagent spawns. I found no Delivery filed for it.<br>Historical reconciliation was closed by decision, not by evidence. The dashboard and assessment (#5045, #5046) are Ready but unstarted. |
| Supported route and board size ([#4941](https://github.com/FS-GG/.github/issues/4941) accepted; [#5119](https://github.com/FS-GG/.github/issues/5119) 4/4) | **Usable.**<br>- Coherent [0.114.1](https://github.com/FS-GG/.github/releases/tag/coherent-set%2Fv0.114.1) is installed.<br>- The 500-row cap and Done-retention archiver are on main.<br>- [#5163](https://github.com/FS-GG/.github/pull/5163) removes the per-release rebind PR. | #5119 shows all children done but is still In progress; it needs Root's closure. |
| Coordination overhead ([#4730](https://github.com/FS-GG/.github/issues/4730)); 22/28, was 11/22 | **Usable:**<br>- Board-currency phase 2 is merged.<br>- `board-linkage` and `no-silent-revert` are now **required** on main (verified), a week earlier than planned.<br>- Moving-main and publisher-retry fixes merged (#5111, #5112).<br>- Context view accepted ([#4940](https://github.com/FS-GG/.github/issues/4940#issuecomment-6555519951)).<br>- Board reconciler ([#5136](https://github.com/FS-GG/.github/issues/5136)): PRs 1–5 merged; PR 5 was #5161. | Reconciler PR 6 (contract amendment) is running.<br>PR 7's coherent release blocks #5113 and #5120. Their fixes have merged but are not installed.<br>The claim-generation split (#5004) is being refreshed.<br>Savings are still **unmeasured**: #5062 is Blocked. |
| SDK 10.0.403 ([#4935](https://github.com/FS-GG/.github/issues/4935)); 6/11 | Owning plan (#5135), SDD 2.5.4 release source ([SDD#1150](https://github.com/FS-GG/FS.GG.SDD/pull/1150)), Templates locks and the Wizard host statement landed. | In flight: [Coordination#1115](https://github.com/FS-GG/FS.GG.Coordination/pull/1115) (#4946) and [Game#744](https://github.com/FS-GG/FS.GG.Game/pull/744) (#4947).<br>Ready but **held for capacity**: #4943 and SDD#1148. |
| Fable reference ([#4646](https://github.com/FS-GG/.github/issues/4646)); 3/6 | #4817 closed: automated assistive-technology evidence was accepted by ruling. [Templates#833](https://github.com/FS-GG/FS.GG.Templates/pull/833) fixed both player defects (#827, #828). Orphaned PRs were tidied. | #4818 is Ready, with Templates#821, #823 and #824 retargeted to it.<br>Rendering#1492 is held. |
| SVG coherence ([#4645](https://github.com/FS-GG/.github/issues/4645)); 12/16 | Keyed reconciler lookup and bounded recomputation merged ([Rendering#1501](https://github.com/FS-GG/FS.GG.Rendering/pull/1501), [#1502](https://github.com/FS-GG/FS.GG.Rendering/pull/1502)). | Physical GPU, input and screen-reader-user evidence moved to deferred [#5148](https://github.com/FS-GG/.github/issues/5148).<br>Next: the glyph matrix (Rendering#1495) and the candidate freeze (Rendering#1499). |
| Unity ([Unity.Client#7](https://github.com/FS-GG/FS.GG.Unity.Client/issues/7)); 5/9 | Held-connection mode (#24) and trace-scenario mode (#34) merged; admission 3 (#28) closed. | IL2CPP build (#29) and EditMode (#30) are Ready.<br>The fixed-action adapter and custody (#31) are Home-owned. |
| Process supervision ([Coordination#1026](https://github.com/FS-GG/FS.GG.Coordination/issues/1026)); 8/11 | The 0.3.1 operation is retired, adoption is closed, and the launcher hardening is closed. | The Linux backend and the first consumer (#1096–#1098) are Blocked and unstarted. |
| Native runtime ([#4650](https://github.com/FS-GG/.github/issues/4650) 8/17, [#4648](https://github.com/FS-GG/.github/issues/4648) 5/9) | The P4 executor is provisioned on fdev instead of through a Home image request ([Coordination#1114](https://github.com/FS-GG/FS.GG.Coordination/pull/1114)). #4778 is closed. | **Still no native workload has run.**<br>M1 (Coordination#1102) is authorized to proceed on what fdev can supply, but its card still reads Blocked. |
| LEARN ([#4647](https://github.com/FS-GG/.github/issues/4647)); 9/16 | Pod image #5027 merged. Egress ruling: provider API only, with a per-run token cap. | Finalizer [#5029](https://github.com/FS-GG/.github/pull/5029) had no update for about 26 hours. It was re-dispatched today (#5066).<br>#5067–#5071 wait on the pod. |
| Unchanged | Typed administration (#4649, 0/4), BAR qualification (#4842, 0/5), shared WASM (Game#718, 2/8), SDD#928 (7/8), OPS-MAINT standing Outcome (#5097, 3/9) | Blocked behind the native chain, or standing work. |

Counts are native sub-issue done/total. They are **not percentages**: denominators grow as Deliveries are filed (#4730 went from 22 to 28).

## Token efficiency

Window: 31 October 09:16Z to 1 November 08:33Z.
- **Source:** the live store `fdev-vm-20261030`, as reported in the [#4638 checkpoints](https://github.com/FS-GG/.github/issues/4638#issuecomment-6547061761) and the [#5041 evidence](https://github.com/FS-GG/.github/issues/5041#issuecomment-6551649189).
- **Health:** `fdev-telemetry health` reads `ready` at 08:34Z, and the capture and drain timers are ticking.
- **Not read:** I did not query the private store. No published summary or dashboard exists, because #5045 is unstarted.

| Dimension | Observed | Coverage / interpretation |
|---|---|---|
| Claude capture facts | 11,501 (at 17:55Z on 31 October), 499 receipts, 0 rejected | Exact parity for the selected backlog. Excluded: two live-agent transcripts, the Root session and pre-baseline files (forward-only ruling). Was about 59% last report. |
| Continuous capture since 00:49Z | Count **not published** | Capped at 300 receipts a day. Captured responses only: rows after a malformed transcript row are skipped. |
| One attributed `codex-exec` dispatch | input 35,570; cached 27,520 (a subset of input); output 138; reasoning 0 | A single qualification run. It proves the join, not a population. |
| Claude per-Delivery or per-role attribution | **None** | Unsupported in the engine, and most programme work is Claude subagents. |
| Context size, #4940 | 4,302 bytes against 40,291 bytes of raw `gh` JSON (about 9.4× smaller) | Measured in **bytes** on one real review. Token savings and worker adoption are **Unknown**. |

**Tokens per accepted Delivery: not measurable.** This window has a real denominator (45 Deliveries, #4941 among them, and one Outcome closed). The numerator still cannot be attributed, because Claude work cannot be tied to a dispatch.

Capture coverage improved from 59% to full parity on the selected population. Cost per useful result is no better known than yesterday.

## Process problems we can fix

- **Host capacity, not the Actions queue, is now the bottleneck.**
  - *Observed:* At 08:34Z, 36 runs were queued and 9 in progress in `.github`, against 183–216 queued at the earlier peak. fdev load was 21–25 on 12 cores, rising, and the host had been up 25 minutes, so it rebooted at about 08:09Z.
  - *Effect:* With six lanes running, Root held #4943 and SDD#1148 even though they are Ready.
  - *Interpretation:* The queue relief fits the concurrency work and #5162, but is **not demonstrated**: today's session is not as busy as the 31 October peak.
  - *Change (Root):* Admit lanes against a load ceiling, and run the heavy SDK re-qualifications serially rather than in parallel with builds. This turns waiting into an explicit queue, not an overloaded host.
  - *Open finding:* The fdev-restart finding (reconsider if none for a week) remains open. I could not tell whether today's boot was planned.
- **Fixes land, but are not installed.**
  - *Observed:* #5113 and #5120 are fixed on main (#5147, #5152) yet Blocked until reconciler PR 7 ships in a coherent release.
  - *Effect:* Merged correctness work is unusable for a release cycle. 0.114.0 and 0.114.1 shipped about 13 hours apart.
  - *Already reduced:* #5163 removes the per-release rebind PR.
  - *Change (Root):* Batch PR 7 with every pending engine fix into the next coherent release, and close #5113 and #5120 on installed readback, not on merge.
- **Every reviewed merge spawned a follow-up.**
  - *Observed:* The four PRs merged after independent review today produced three follow-ups (#5164–#5166). This repeats the hardening pattern from last report.
  - *Interpretation:* Review is finding real gaps, such as atomic outputs and live-body reads. The step pays, so *keep it*.
  - *Risk:* OPS-MAINT (#5097, 3/9) becomes a growing tail.
  - *Suggested change (Root):* Before merge, decide whether each review finding is fixed now or deferred. Defer only items that do not affect the Delivery's acceptance.
- **Parked uncommitted work.**
  - *Observed:* #5029 depended on review repairs saved off tmpfs by hand, and sat idle for about 26 hours. It was the last report's second priority.
  - *Effect:* LEARN (#5067–#5071) stayed Blocked throughout.
  - *Change (lane owner):* Push repairs to the PR branch, even as a draft, before a stop, so a resume starts from durable state.
- **Board lag is small and narrowing.**
  - *Observed:* #5159 is still "In review" although its only PR merged, and #5119 is In progress at 4/4.
  - *Status:* The tend writer that applies closure rule C1 is merged behind an enable file (#5158), and the 45-card blocker-currency pass is running.
  - *Effect:* Low now, but Blocked cards are 49 of 239. Stale blocker links would hide Ready work.

**Owned findings.**
- **Closed with a landed change:** census advisory (#5099), revert guard (#5019, now required), moving main (#5107), publisher race (#5108), fixture determinism (#5109), main-health and drift observers (#4989, #4990), body-edit cancellations (#5159 via #5162).
- **Open:**
  - Body-reading checks that read the event payload (#5166).
  - fdev restarts.
  - Claude dispatch attribution, which has no owner or Delivery that I found.

## Blockers the current process cannot resolve

- **Home is unavailable by owner statement** ([decisions](https://github.com/FS-GG/.github/issues/4730#issuecomment-6551301543)). The following have **no supported workaround** and are deferred or held, not in progress:
  - Bare-metal GPU B1–B8, physical-input and screen-reader-user evidence ([#5148](https://github.com/FS-GG/.github/issues/5148), Rendering#1500).
  - Unity custody and the fixed-action adapter (Unity.Client#31).
  - Blank-slate portability on two hosted KVM machines (#4821), which also needs a stock guest image from the common-base owner.

  More agents cannot supply these. They return only when Home or the owner reopens them.
- **Claude dispatch attribution** is an engine capability gap, not an external block. Until it is scheduled, cost per Delivery stays unknown for most work. The route is uninvestigated.
- No other external blocker is established. The remaining Blocked cards are serial: the coherent release, the native chain and the LEARN pod.

## Priorities

1. **Ship reconciler PR 7 in one coherent release with #5147 and #5152,** and accept #5113 and #5120 on installed readback. This unblocks two Deliveries and lets the tend writer keep the board current.
2. **Admit lanes against fdev's load**, so the Ready SDK re-qualifications (#4943, SDD#1148) and native M1 run in order, not by overload.
3. **File and own Claude dispatch attribution** (or rule it out of #5062). Without it, the coordination-overhead savings cannot be costed.

Sources: [Project 3](https://github.com/orgs/FS-GG/projects/3) (native read 08:33Z, 239 cards: 155 Done, 49 Blocked, 22 Ready, 12 In progress, 1 In review), [Root decisions](https://github.com/FS-GG/.github/issues/4730#issuecomment-6551301543), [actions on the last report](https://github.com/FS-GG/.github/issues/4730#issuecomment-6545424480), [#4638 acceptance](https://github.com/FS-GG/.github/issues/4638#issuecomment-6551856572), [0.114.0 acceptance](https://github.com/FS-GG/.github/issues/4941#issuecomment-6545785822).

Limits:
- Telemetry totals come from owner checkpoints, not an independent store read.
- The Codex-only local diagnostic was not run, because it does not cover Claude work.
- The API time stamps are inconsistent, as noted above.
