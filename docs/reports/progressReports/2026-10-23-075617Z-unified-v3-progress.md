---
title: "Development progress — 2026-10-23"
as_of: "2026-10-23T07:56:17Z"
previous_report: "2026-10-22-162227Z-unified-v3-progress.md"
---

# Development progress — 2026-10-23

As of 07:56:17 UTC (09:56 Vienna). The board snapshot was taken at 07:52:31 UTC. Compared with the [previous report](2026-10-22-162227Z-unified-v3-progress.md) (2026-10-22 16:22 UTC).

Delivery advanced overnight and turned several pieces of work into things people can use. The public SVG successor (#4709) is accepted through the public Wizard. LEARN's finite host profile passed its full A0–A26 acceptance on production. Workspace.Template 0.19.3 and FS.GG.Wasm 0.7.0 are published. The board is now more honest: an audit moved stale Blocked items into decisions and child issues, and the number of Blocked items fell from 24 to 19. Most remaining constraints are not source work. They are a few user-only acts (a nuget.org policy pass, privilege decisions, an unlocked desktop) and a root-terminal window on Home that closes around 17:06Z today.

| Lane | Meaningful progress | Problem / next step |
|---|---|---|
| Public SVG successor | **Usable:** [#4709](https://github.com/FS-GG/.github/issues/4709) was accepted at 22:03Z on the published pairing Wizard 0.21.0, SDD 2.4.0, Workspace.Template 0.19.2 and Rendering 0.32.3. A read-only hosted run on 0.19.3 also passed at 07:18Z. | When the lifecycle is omitted, Wizard records `sdd` rather than the promised `typed-sdd`. That is [#4811](https://github.com/FS-GG/.github/issues/4811). Its fix is merged ([#4813](https://github.com/FS-GG/.github/pull/4813)), and the Wizard 0.22.0 release waits for fdev's authorization. [#4644](https://github.com/FS-GG/.github/issues/4644) activation is blocked on it. |
| Shared game reference, downloaded-only journey | The custody pilot phase is complete. Game#727 is fixed and published as Wasm 0.7.0. Templates 0.19.3 is published, byte-verified and pinned. Run 3's precheck passed 27/27 on the published bytes. Run 3's hosted job ([39920080680](https://github.com/FS-GG/FS.GG.Templates/actions/runs/39920080680)) **finished successfully at 07:50Z**. | **Run 3 is not accepted yet.** Next come the independent acceptance of its artifact and the reset PR ([Templates#754](https://github.com/FS-GG/FS.GG.Templates/pull/754), open). Runs 1 and 2 failed as evidence; see below. |
| LEARN host execution | **A0–A26 were accepted (with gaps) on production** at 22:25Z, and spec rev 11 r2 governs. [#4777](https://github.com/FS-GG/.github/issues/4777) is decided: Home is the finite route. C3 r4 and C2 r5 are installed, and A0 r4 passed 9/9 at 07:51Z. | Part B r3b is running now. Still owed: the operator sudoers entry, the reader integration ([#4744](https://github.com/FS-GG/.github/issues/4744)) and the disconnect A-case. The root terminal closes about 17:06Z. |
| SC2 and unattended execution | The route is decided. Home's read-only facts are in: a loop image on `/mnt/storage`, and no io controller. The user directed that Home provides SC2. [#4650](https://github.com/FS-GG/.github/issues/4650) is split into #4820 (LEARN profile) and #4821 (hosted KVM). | Nothing is authored yet. Next is Home's SC2 bytes for review. Creating the 256 GiB image needs root once, and it will probably miss the terminal window. |
| Knowledge-aware providers | The Wizard-bindings positives slice was [accepted](https://github.com/FS-GG/.github/issues/4642#issuecomment-6360126520) at 07:48Z ([#4826](https://github.com/FS-GG/.github/pull/4826) and a read-only hosted run). | The remainder is on [#4822](https://github.com/FS-GG/.github/issues/4822). Its route map shows that most earlier acceptances ran on 0.19.0/SDD 2.2.0, and none has been re-run on the current 0.19.3/2.4.0. |
| Process supervision / Execution forward route | For [Coordination#1026](https://github.com/FS-GG/FS.GG.Coordination/issues/1026), the environment, App key and Trusted Publishing policy are in place. | The old-writer fence is only partial: 15 of 22 nuget.org policies use the `*` glob. The narrowing pass is a user act in the UI. Ordinal-4 custody is still undeclared. |
| SDD/Governance for polyglot providers | Governance 1.14.0 is merged. For [SDD#928](https://github.com/FS-GG/FS.GG.SDD/issues/928), the SDD half of C3.1-03 passes. | Governance Verify stops short of GateExecution. The owner is on [Governance#472](https://github.com/FS-GG/FS.GG.Governance/pull/472) (GOV-423-C2). |
| Telemetry (UTEL) | Last report's blocker is cleared: the user installed the sudoers block at 16:23Z. | The post-window steps run from 14:10Z today. The Claude collector exists in source only (see below). |
| Coordination efficiency | For [#4730](https://github.com/FS-GG/.github/issues/4730), the migration-prepare failure-custody fix is merged ([#4812](https://github.com/FS-GG/.github/pull/4812)). | The transient empty-items half of [Coordination#1049](https://github.com/FS-GG/FS.GG.Coordination/issues/1049) has no update since 15:51Z yesterday. |
| SVG performance | Budgets v1 are decided ([#4645](https://github.com/FS-GG/.github/issues/4645)). Home GPU benchmark r1.3 is admitted after three review rounds. | It needs an unlocked desktop with the user present. The hosted software benchmark is open as [Rendering#1465](https://github.com/FS-GG/FS.GG.Rendering/pull/1465). |
| Unchanged or deferred | FBX-07 ([Templates#725](https://github.com/FS-GG/FS.GG.Templates/issues/725)) closed, handing over to a scheduled watch (#751). Unity#7 is deferred by the user, with its obligation kept. | No change for BAR (#1032, #4778), Game#718, Templates#441, SDD#935, #3010 and #4753. FABLE-ADOPT children #4815–#4819 are in Backlog. |

**Board:** 76 items. 42 Done, 19 Blocked, 7 Backlog, 5 In progress and 3 Ready. Last time there were 69 items: 39 Done, 24 Blocked, 2 Backlog, 2 In progress and 2 Ready. These counts mix Outcomes and Deliveries, so they are not a completion percentage. In-progress cards now match the work that is actually active. That was a problem last time and is now fixed.

**Coverage caveat:** #4811, #4820 and #4821 are open but not on Project 3. #4822 and #4823 have no Work level.

## Token efficiency

**This is a bounded local diagnostic, not canonical usage.** The Claude collector ([#4802](https://github.com/FS-GG/.github/pull/4802)) is only in coherent-set 0.113.0 source. nuget.org's newest version is 0.112.0, and this host still runs `fs.gg.coord.cli` 0.102.0. So no installed route captured this window.

The figures below come from a read-only, counters-only pass over this host's Claude Code transcripts. It used the same accounting basis as the #4802 reader:

- input = uncached + cache read + cache write;
- one row per unique response.

The window runs from 16:22:27Z to 07:52:31Z (15.5 h). It covers 75 transcripts, 73 of them subagents, with no malformed lines.

It excludes Home's agent on its own host, Codex (no session files in the window) and Actions. It includes part of this reporting worker's own usage.

| Dimension | Observed usage | Coverage / interpretation |
|---|---|---|
| All Claude Code responses | 5,446 responses; 1.502 B input (95.1% cache reads, 73.1 M cache writes, 11.5 k uncached); 4.58 M output | Reasoning tokens are not reported separately. |
| Root coordination | 440 responses; 237.6 M input (99.5% cached); 0.25 M output | Mean input is about 540 k per response, a very large standing context. |
| Child workers and critics (Opus 5.5) | 4,824 responses; 1,237.5 M input (94.2% cached); 4.16 M output | Children account for 84% of input and 95% of output. A role is not a purpose. |
| Child Sonnet 5.5 / Haiku 5.5 | 181 responses, 27.3 M input / 1 response, 14 k input | Small share. |

**Not defensible:** tokens per accepted Delivery. Three items closed in the window: #4709, the #4777 decision and the Templates#725 disposition. Several accepted slices landed alongside them, including LEARN A0–A26, the #4642 bindings slice and two publications. But usage is not attributed to items, so any ratio would charge shared work to every item.

There is no compatible baseline, because the last report had no usage data. This is the first measured window.

**One observation worth acting on:** each root turn re-reads about 540 k cached tokens. Restarting the root session at natural handoff boundaries would plausibly cut most of the root's 238 M cache reads. The saving is unmeasured, and token share is not a share of cost.

## Process problems we can fix

- **Single-use admissions failed on the first live exercise of their setup code.**
  - **Pattern:** in #4710, run 1 spent its admission at 0/27 cases because of a deterministic restore defect that had never run before the hosted run. Run 2 got 9/27 and exposed a real product defect: Firefox's 250 ms deadline included Worker cold-start under 4 hosted CPUs. Each failure cost a full cycle (request, admission, review of the admission PR, dispatch, reset PR) of about 1–1.5 h. LEARN's A0 run 1 failed closed the same way, on a C3 r2 defect.
  - **What changed:** "rehearse first" was added after run 1. A local 27/27 harness ran under a 4-CPU limit and, for run 3, against the exact published bytes. Run 3's hosted job then succeeded in 3.5 minutes. Its acceptance is pending, so this is partly demonstrated, not proven.
  - **Not waste:** run 2's failure found a genuine defect, now fixed in Wasm 0.7.0.
  - **Change (fdev):** make "rehearse on the exact candidate bytes under hosted-like limits" the default precondition of every single-use admission. Separately, decide whether the admission and reset PR pair is still needed: six PRs for three runs. That would change a requirement, so it is a proposal.
- **Review rounds are catching real defects; keep them.**
  - **Examples:** C3 was rated DO-NOT-ADMIT with two reproduced defects. GPU r1.1 failed on interrupt teardown, and r1.2's Fable check failed on a real install. #4813 was REJECTed on red gates, #4812 got REQUEST CHANGES, and #738 went through three rounds.
  - **Cost:** LEARN review turnaround was usually 2–10 minutes, so review latency is not the constraint. The GPU r1.2 defect appeared only on a real install, so the same fix applies: exercise the real setup earlier.
  - **Change:** none to review itself.
- **Board drift built up until a manual audit fixed it.**
  - **Pattern:** fdev's audit found Blocked items whose blockers had already been delivered. Its summary is not in a public record; the resulting refreshes, rescopes and children are. Blocked fell from 24 to 19.
  - **New children:** #4811, #4820 and #4821 are open but not on the board, and #4822 and #4823 have no Work level.
  - **Status writer (session report, no owning record):** the guarded status writer reportedly broke when the board grew from 69 to 76 items, because of a fixed-count assertion. It is now parameterized.
  - **Change (fdev):** compare Blocked-on against closures at each material delivery, not in occasional sweeps. Enroll and classify children when they are created. Record the writer defect in #4730 so a recurrence is visible.
- **Approvals queue up behind Home's root-terminal deadline.**
  - **Before ~17:06Z:** LEARN r3b, the operator sudoers entry and, ideally, SC2's one-time image. SC2 has not been authored yet. The UTEL steps from 14:10Z use pinned sudo and are unaffected.
  - **After expiry:** each privileged step goes back to a separate user install and removal, and each of those is a user round trip.
  - **Change (user decision, fdev to prepare):** decide SC2's privilege model now, either standing hash-pinned entries or per-operation installs. Batch the remaining per-operation installs into one user session.
- **Unverified session reports.** False "posted" claims by agents (caught by critics) and the earlier tmpfs exhaustion appear in no owning record, so this report cannot confirm how often they happen. The existing rule is to read back the exact comment id. Logging each occurrence on #4730 would make the pattern measurable.

## Blockers the current process cannot resolve

- **nuget.org Trusted Publishing narrowing (user, nuget.org UI, one pass)**
  - **Effect:** until it is done, any `*`-glob workflow can mint tokens for `FS.GG.Execution.*`, so #1026 cannot enable its route.
  - **Workaround:** none.
- **Ordinal-4 custody (#1026)**
  - **Status:** the 2026-10-22 19:30Z deadline passed with no recorded answer, so the original recovery stays undeclared.
  - **Who can supply it:** only the custodian.
- **Home root terminal expiry (~17:06Z, user)**
  - **Effect:** after expiry, privileged LEARN and SC2 steps need user-run sudo.
  - **Workaround:** the user can extend the window or approve batched installs.
- **Unlocked desktop session (user)**
  - **Effect:** the #4645 .7 GPU baseline needs it.
  - **Workaround:** none. A hidden page refuses, which is valid evidence but not a baseline.
- **Unity licence and credentials (user)**
  - **Effect:** they are the only re-entry input for the deferred Unity.Client#7.

## Priorities

1. **Turn implemented work into usable work.**
   - Independently accept or reject #4710 run 3 from its artifact, and merge the reset.
   - Authorize the Wizard 0.22.0 release so #4644 can claim the `typed-sdd` default.
2. **User, in one sitting before ~17:06Z:**
   - decide SC2's privilege model;
   - run the nuget.org narrowing pass;
   - schedule the GPU benchmark on an unlocked desktop.

   Meanwhile, Home finishes LEARN r3b and the operator entry.
3. **Reduce rework and blind spots.**
   - Make exact-bytes rehearsal the default precondition for admissions.
   - Log session-only process defects on #4730.
   - Publish and install the engine that carries the Claude collector, so the next report is measured canonically.

Sources: [Project 3](https://github.com/orgs/FS-GG/projects/3), snapshot taken 07:52:31Z (76/76 items read). The owning issues and PRs are linked above. The Home channel is [#4516](https://github.com/FS-GG/.github/issues/4516): 97 comments since the last report, against 198 the day before. Usage is a local counters-only diagnostic of Claude Code transcripts. The UTEL store was not opened, and no telemetry, board, issue or product state was changed for this report.
