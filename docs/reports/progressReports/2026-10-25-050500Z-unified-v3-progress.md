---
title: "Development progress — 2026-10-25"
as_of: "2026-10-25T05:05:00Z"
previous_report: "2026-10-24-045800Z-unified-v3-progress.md"
---

# Development progress — 2026-10-25

As of **05:05 UTC, 25 October 2026**. Compared with the [last saved report](2026-10-24-045800Z-unified-v3-progress.md) (04:58 UTC, 24 October) and with the Claude root's takeover at about 14:35 UTC on 24 October ([resume record](https://github.com/FS-GG/.github/issues/4820#issuecomment-6385473336)).

Delivery is **advancing faster than at any point in the last report window**. Since the takeover, ten Project 3 items have closed with accepted evidence, including two full Outcomes. The four-language Templates pack passed its genuine qualification and was entering publication at the cutoff. The user's rulings removed the main supplier holds named last time. The biggest constraint is now internal: the self-hosted native runner. BAR, SC2, the FourD native image and two-machine portability all wait on it. It is still in review, and Home's 48-hour root terminal expires at about 14:37 UTC on 26 October.

Project 3 went from 51 of 85 items Done at takeover to **61 of 89** at the cutoff. Four new Deliveries were added and closed in the session. The remaining 28 are 14 Blocked, 11 In progress, 1 Ready and 2 Backlog. Memberships mix Outcomes and Deliveries, so this is not a completion percentage.

| Lane | Meaningful progress | Problem / next step |
|---|---|---|
| Four-language workspace templates | [Templates#441](https://github.com/FS-GG/FS.GG.Templates/issues/441): one archive passed genuine qualification for TypeScript, JavaScript, Rust and Go, and root accepted it at 04:34. Every product passed all 8 catalog cases and all 8 Governance Verify gates. The register entry merged at 04:52 ([PR796](https://github.com/FS-GG/FS.GG.Templates/pull/796)). | **0.20.0 is not yet on nuget.org.** Its release workflow was running at the cutoff. Next, consumers adopt it: [#4818](https://github.com/FS-GG/.github/issues/4818) published-byte acceptance, then [#3010](https://github.com/FS-GG/.github/issues/3010) and [SDD#928](https://github.com/FS-GG/FS.GG.SDD/issues/928). |
| Consumer-defined SDD sources and provider SDK | [SDD#935](https://github.com/FS-GG/FS.GG.SDD/issues/935) closed with SDD 2.5.x published. Consumers can now use provider templates from local paths and pinned sources. The provider SDK package-only qualification ([#1131](https://github.com/FS-GG/FS.GG.SDD/issues/1131)), its harness ([#1134](https://github.com/FS-GG/FS.GG.SDD/issues/1134)) and the schema-v1 compatibility window ([#1139](https://github.com/FS-GG/FS.GG.SDD/issues/1139)) were added and closed. | Polyglot scaffold contracts (SDD#928) stay Blocked behind the Templates publication. |
| SVG coherence (Rendering) | Spatial queries no longer scale with the source list ([Rendering#1469](https://github.com/FS-GG/FS.GG.Rendering/issues/1469)). Bucket construction is no longer quadratic ([#1472](https://github.com/FS-GG/FS.GG.Rendering/issues/1472)). | Parent [#4645](https://github.com/FS-GG/.github/issues/4645) remains in progress. The Rendering `release-tags` workflow fails on every main push ([Rendering#1474](https://github.com/FS-GG/FS.GG.Rendering/issues/1474)). |
| Release-D and usage coverage | Two full Outcomes are accepted and closed: Release-D receiver activation ([#4644](https://github.com/FS-GG/.github/issues/4644), with Delivery [#4849](https://github.com/FS-GG/.github/issues/4849)) and routine/review/usage coverage ([#4639](https://github.com/FS-GG/.github/issues/4639)). The census tools are now in `tools/usage-census`. | None for these Outcomes. Their measurement surfaces feed the token section below. |
| Fable adoption | Preserving upgrades and rollback ([#4816](https://github.com/FS-GG/.github/issues/4816)) closed. The #4818 source-built gaps passed 33 of 33 cases across three browsers. The FourD .5e archive is done, and the native-image caller is retargeted onto FS.GG.FourD itself ([FourD#48](https://github.com/FS-GG/FS.GG.FourD/pull/48)). | #4818 waits on Templates 0.20.0. [#4819](https://github.com/FS-GG/.github/issues/4819) native image waits on the runner. [#4817](https://github.com/FS-GG/.github/issues/4817) (actual AT/accessibility journeys) is **Ready** and is the next independent result. |
| Publication overhead | ADR-0097 replaced repository-wide publication freezes with an org-wide writer lock, and 10 producer repositories adopted it ([#4753](https://github.com/FS-GG/.github/issues/4753)). The Coordination physical-attempt race is fixed (Coordination#1067). | Writer-coverage checker gaps ([#4869](https://github.com/FS-GG/.github/issues/4869)) and the Net settlement conflict ([Net#115](https://github.com/FS-GG/FS.GG.Net/issues/115)) are open. |
| Self-hosted native runner (critical path) | The user approved a self-hosted runner with an Ubuntu 24.04 guest. On GitHub Free that means private callers (FS.GG.Native.Qualification, accepted at source scope). Home revised the runner four times in about eight hours (r4, r4b, r4c). Each revision answered a concrete review finding, the latest being a fail-open guard (F1). | r4c is under independent review ([Home channel](https://github.com/FS-GG/.github/issues/4516#issuecomment-6391072035)). Nothing native has run yet. Review, admission and the first job should land well before the root terminal expires. |
| Home facts-to-browser journey | r6 ran once with valid custody, then failed `JOURNEY_DEADLINE`, stalling at or before the login wait. Root diagnosed it and specified r7. | r5 lapsed unrun because root missed Home's admission deadline (see below). The r7 source is under review. [#4707](https://github.com/FS-GG/.github/issues/4707) also gates [#4640](https://github.com/FS-GG/.github/issues/4640). |
| BAR knowledge keeper | The original keeper source was unrecoverable, so Home wrote a new keeper ([FSBarV2#51](https://github.com/FS-GG/FSBarV2/pull/51)), installed it and upgraded it once ([#4843](https://github.com/FS-GG/.github/issues/4843)). | The re-review found a crash-wedge defect (M1). Home's repair is under review. BAR image and Count1 work ([Coord#1032](https://github.com/FS-GG/FS.GG.Coordination/issues/1032), [#4778](https://github.com/FS-GG/.github/issues/4778), [#4842](https://github.com/FS-GG/.github/issues/4842)) waits on the runner. |
| Unchanged or held | Unity is deferred by user ruling. Trusted publication ([Coord#1026](https://github.com/FS-GG/FS.GG.Coordination/issues/1026)), UTEL collection ([#4638](https://github.com/FS-GG/.github/issues/4638)), LEARN ([#4647](https://github.com/FS-GG/.github/issues/4647)), portable execution ([#4648](https://github.com/FS-GG/.github/issues/4648)), typed ops ([#4649](https://github.com/FS-GG/.github/issues/4649), [#4815](https://github.com/FS-GG/.github/issues/4815), Coord#1051) and shared WASM ([Game#718](https://github.com/FS-GG/FS.GG.Game/issues/718)) show no change. | These were not re-verified in detail for this report. [#4821](https://github.com/FS-GG/.github/issues/4821) also needs two independent runner allocations. |

## Token efficiency

**Sources.** Two adjacent windows from the same coordinator host:

- **14:32–18:00 UTC.** The settled [#4639 continuation census](../2026-10-24-usage-census-4639.md), for the Claude root session after takeover.
- **18:00–05:00 UTC.** A fresh read-only pass for this report. It used the census's own reader and collector (`tools/usage-census/collect.py`, reader pinned at `61526e94`), with deduplication by provider response ID.

**What the fresh pass did not do.** It froze no PR population, so no per-PR attribution was attempted. It opened no receiver or telemetry store.

**Coverage.**

- **Read:** 2 main transcripts and 63 subagent transcripts.
- **Withheld:** 4 subagent transcripts, each with a response that has no completed usage record (likely still running, including this reporting worker).
- **Refused:** 2 subagent transcripts that mix session identities.
- **Not covered:** Codex has no responses in the window, because its coordinator had stopped. Home and other hosts are **Unknown**, and so is billed cost.

| Dimension | Observed usage | Coverage / interpretation |
|---|---|---|
| Claude, 14:32–18:00 | 1,908 responses, 334.8 M tokens | Settled census figure |
| Claude, 18:00–05:00 | 3,920 responses, 1,008.5 M tokens | Fresh read; total = input + output |
| Input, 18:00–05:00 | 1,005.1 M, of which 923.3 M cache reads (91.9 %) and 81.9 M cache writes | Cache reads are part of input, not extra tokens |
| Output, 18:00–05:00 | 3.33 M (reasoning 1.21 M is part of this) | — |
| Root (Opus, medium effort) | 120 responses, 52.8 M tokens (5.2 %) | Main transcript only |
| Subagent workers and critics (Sonnet, high) | 3,799 responses, 955.4 M tokens (94.7 %) | Role does not show purpose; implementation and review cannot be separated |
| Time profile | About 855 M tokens from 18:00 to 24:00, about 148 M from 00:00 to 05:00 | Usage followed active parallel deliveries |

**Cost per accepted result.** About 1.34 B observed Claude tokens since takeover sit beside 10 accepted Project 3 closures (8 Deliveries and 2 Outcomes) plus the accepted Templates#441 qualification. That is roughly 130 M tokens per closure. Treat it as **partial observed cost, not a whole-item ratio**: much of the closed work began under the Codex root, the withheld transcripts are missing, and Home's usage is not included.

**Comparison with the Codex root.** The previous Codex root used 2.81 B tokens between 14:54 UTC on 23 October and 12:35 UTC on 24 October ([census](../2026-10-24-usage-census-4639.md)), and 2 Project 3 items closed in that window. The runtime, cache accounting and work mix all differ. Much of that window's work only closed after the handover. So this is **not a compatible baseline**, and no efficiency improvement is demonstrated.

## Process problems we can fix

- **Root is a serial bottleneck for Home's time-boxed admissions.**
  - **What happened:** Home's r5 packet arrived at 15:02 and lapsed unrun at its 16:45 deadline because root did not review it. The re-run (r6) only took place at 19:48, more than four hours later. At 23:05 a root triage wrongly reported that r6 and the keeper repair had not happened, because of a comment-paging error on a 1,700-comment thread. Root corrected it on [#4516](https://github.com/FS-GG/.github/issues/4516#issuecomment-6390745363).
  - **What is already working:** where a change is only mechanical, Home now generates the packet and root supplies a verifier that confirms the clock-only delta, so Home can self-admit. That route worked for r6.
  - **Proposed change:** read the Home channel deterministically with a since-cursor and full paging, and track admission deadlines as explicit timers. This belongs under [#4730](https://github.com/FS-GG/.github/issues/4730) (COORD-EFF-02), owned by root. Consider splitting the oversized channel; the size of the thread contributing to the paging error is a suspected cause, not a measured one.
- **The routine-eligibility check refuses the first CI run on every new head** ([#4868](https://github.com/FS-GG/.github/issues/4868)). This recurred on about 10 PRs.
  - **Effect:** a wasted CI round, plus a manual edit of the PR body after every push. It adds about one CI cycle per head, which matters in a window with 63 merged PRs, 24 of them in Templates.
  - **Proposed change:** let the check derive the head from the event, or have the delivery tool refresh the marker on push.
  - **Owner:** .github tooling. The saving is plausible but unmeasured.
- **Cohort enrollment wrappers are cloned and hand-patched for each cohort** ([#4874](https://github.com/FS-GG/.github/issues/4874)). Cohorts 84, 85 and 86 each needed their own PR in this session. A parametric wrapper would remove one PR per protected Delivery and a source of copy errors. Owner: .github.
- **Standing-red and intermittent signals:**
  - Rendering `release-tags` fails on every main push (Rendering#1474).
  - The Net settlement journal conflict is unresolved (Net#115).
  - The Choreo C2 harness fails intermittently on a `/proc` read ([Coord#1065](https://github.com/FS-GG/FS.GG.Coordination/issues/1065)).
  - A Quint semantic check needed one single-job rerun.

  Standing red teaches workers to ignore red. Each owning repository should either fix its signal or mark it as explicitly pending, so CI is not left failing permanently.
- **Shared scratch custody failed once.** A worker deleted root tools from the shared scratchpad, and root rebuilt them in private custody. Workers should get their own scratch directories and should not write into the root's. This is applied ad hoc and not yet written down.
- **Independent review is earning its cost; keep it.** Critics caught:
  - a `.yaml` label-scan hole in FourD#48;
  - the fail-open runner guard;
  - the keeper crash wedge;
  - Templates defects before qualification.

  One gap: Home installed the first keeper under self-admission before any review of PR #51, and the later reviews required two repair rounds. Nothing was damaged, but review-before-install would be cheaper for anything that runs persistently as root. That decision rests with Home and root.

## Blockers the current process cannot resolve

- **The root terminal deadline.** Home's root-terminal exception runs until about 14:37 UTC on 26 October. Runner installation, r7 and the keeper repair all depend on it. If they are not finished by then, only the user can extend or renew it.
- **Two independent machines for portability.** [#4821](https://github.com/FS-GG/.github/issues/4821) needs two self-hosted ephemeral allocations that share no disk, kernel or engine store. Only the user or Home's hardware can supply that capacity. More agents cannot.
- **Effects held by owners who are gone.** The original FourD owner has been declared gone, and the frozen BAR keeper source is unrecoverable. Effects held by those originals stay Unknown under ADR-0096. Successor work is proceeding, but there is no workaround that recovers the original evidence.
- **Unity** stays deferred by user ruling. It needs an operator before it can resume.

The self-hosted runner itself is **not** an external blocker. It is in-progress work with a working review loop.

## Priorities

1. **Land the runner.** Finish the r4c review, admit it, and run the first native job before the root terminal expires. This one capability unblocks BAR, SC2, the FourD native image and #4821.
2. **Turn Templates 0.20.0 into consumer results.** Confirm the publication on nuget.org, then run #4818's published-byte acceptance and start #3010/SDD#928. Meanwhile, run #4817 (AT journeys), which is Ready and independent.
3. **Remove repeat friction.** Fix the head-marker refusal (#4868), make cohort enrollment parametric (#4874), and give root deterministic Home-mailbox reading with deadline alerts (#4730).

Sources: [Project 3](https://github.com/orgs/FS-GG/projects/3) (read at 05:00 UTC), the owning issues and PRs linked above, the [Home channel](https://github.com/FS-GG/.github/issues/4516), and the [#4639 census](../2026-10-24-usage-census-4639.md). Token figures for 18:00–05:00 come from a read-only census-reader pass. Its private per-response rows stay off-repository, and Home's usage and billed cost are Unknown. Facts are frozen at the cutoff. No product work, test, telemetry mutation or board change was made to produce this report.
