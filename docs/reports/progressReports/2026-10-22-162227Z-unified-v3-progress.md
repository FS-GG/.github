---
title: "Development progress — 2026-10-22"
as_of: "2026-10-22T16:22:27Z"
previous_report: "2026-10-21-052746Z-unified-v3-progress.md"
---

# Development progress — 2026-10-22

As of 16:22:27 UTC (18:22:27 Vienna). The board snapshot was taken at 16:19:30 UTC. Compared with the [previous report](2026-10-21-052746Z-unified-v3-progress.md) (2026-10-21 05:27 UTC).

Delivery is advancing, and today it produced several things people can use. The telemetry receiver on Home runs the new Host 0.8.0 and accepted its first genuine durable producer receipt. SDD 2.4.0, Templates 0.19.1 and Governance 1.13.0 are published. FourD's native image is accepted locally. The original SVG tag uncertainty from the last report is settled. The biggest constraint is no longer source work. Several lanes now wait on a few short human actions on Home: sudo-authenticated installs and custody answers. Coordination overhead is also visible, with long review/revise loops and a board whose Status no longer shows what is actually running.

| Lane | Meaningful progress | Problem / next step |
|---|---|---|
| Telemetry receiver (UTEL) | **Usable:** Host 0.8.0 has been [live on the schema-14 store](https://github.com/FS-GG/.github/issues/4707) since 14:05Z, after a 21 s outage, and steps 3a–3d are accepted. [#4705 is Done](https://github.com/FS-GG/.github/issues/4705): one authenticated producer batch (25 events) was durably received and applied at 15:15Z, with no duplicates or corrections. | Coverage is partial. Batch 2 of 2 is still pending, and the observations have no programme/activity attribution (Unknown). Draining batch 2 and the retention cleanup both need the user's sudoers install on Home. The rollback window runs until 2026-10-23 14:10Z. Parent [#4638](https://github.com/FS-GG/.github/issues/4638) remains open. |
| SVG / Rendering 0.32.3 | The original tag intent was **settled and verified** at 12:00Z: all three tags point at the original commit, and the main freeze is lifted. The publisher-eligibility rehearsal passed locally. | Publication is **held**. Independent review returned NO-GO: event recording would make about 1,150 API calls against a 1,000/h limit, which risks an irreversible partial nuget.org push. The fix is [Rendering#1464](https://github.com/FS-GG/FS.GG.Rendering/pull/1464); re-review and dispatch come next. Public adoption and the Wizard qualification remain open in [#4709](https://github.com/FS-GG/.github/issues/4709). |
| Shared game reference and process supervision | The managed-group supervision contract was requested ([Coordination#1046](https://github.com/FS-GG/FS.GG.Coordination/issues/1046)), delivered ([#1047](https://github.com/FS-GG/FS.GG.Coordination/pull/1047)) and bound into the Templates WASM caller ([Templates#731](https://github.com/FS-GG/FS.GG.Templates/pull/731), merged 16:16Z), all on the same day. The bound-intent fence (#4805, Coordination #1045) is also in, kept dormant. | This is implemented, but nobody can use it yet. Next is the hosted downloaded-only receiver run against Workspace.Template 0.19.1 for [#4710](https://github.com/FS-GG/.github/issues/4710). [#1026](https://github.com/FS-GG/FS.GG.Coordination/issues/1026) enablement still needs the protected environment, OIDC, App and policy setup, plus old-writer fencing, a hosted run and Linux containment. |
| LEARN host execution | The finite-profile fixture passed on revision 10, and spec rev 10 is [accepted as the production basis](https://github.com/FS-GG/.github/issues/4745). The production proposal structure is agreed. | No host qualification exists yet. Next is Home's production implementation. Its P0 reader will need a second hash-pinned sudoers install (`fsgg-learn`) by the user. |
| SDD / Governance supply for polyglot providers | SDD 2.4.0 is published and qualified ([SDD#1119](https://github.com/FS-GG/FS.GG.SDD/issues/1119)). Its publisher now waits for the feed to index before reading back. Governance 1.14.0 ([PR #471](https://github.com/FS-GG/FS.GG.Governance/pull/471)) has an APPROVE review, and its cold-installed tool shows parity with the old Verify. | PR #471 is unmerged, and its `routine-eligibility` check fails. [SDD#928](https://github.com/FS-GG/FS.GG.SDD/issues/928) also still needs a runtime carrier with physical blocked-read evidence. |
| Knowledge-aware Fable providers | The game, PLAYER and COMPLETE provider qualifications ([#4773](https://github.com/FS-GG/.github/issues/4773), [#4780](https://github.com/FS-GG/.github/issues/4780), [#4798](https://github.com/FS-GG/.github/issues/4798)) are all accepted and closed, along with the re-pin to Templates 0.19.1 (#4804). | The parent [#4642](https://github.com/FS-GG/.github/issues/4642) keeps its deferred Wizard-binding positives and other routes. |
| FourD | The native-image Outcome is [accepted](https://github.com/FS-GG/FS.GG.FourD/issues/37), and the protection entitlement (#39) is resolved. | The gap is hosted evidence: the hosted main run failed closed in its rootless capability preflight. |
| BAR, SC2 and other retained outcomes | Unchanged since the last report. | [BAR #1032](https://github.com/FS-GG/FS.GG.Coordination/issues/1032) and [SC2 #4746](https://github.com/FS-GG/.github/issues/4746) still lack their writer fence and authenticated executor. The LEARN profile work may later serve SC2's shared route ([#4777](https://github.com/FS-GG/.github/issues/4777)), but that is not yet established. |

On the board snapshot, Project 3 holds 69 items: 39 Done, 24 Blocked, 2 Ready, 2 In progress and 2 Backlog. The previous report counted 58 items: 25 Done, 28 Blocked, 1 Ready, 1 In progress and 3 Backlog. Outcomes and Deliveries are mixed in these counts, so they are a planning snapshot, not a completion percentage.

**Read-quality caveat:** GitHub's API intermittently returned Project 3 with zero items during an open GitHub incident (from 15:28Z). Of three reads, the first returned all 69 items and the next two returned 0. The report uses the complete read, which matches the 69 items verified independently at 15:51Z.

## Token efficiency

**No canonical usage data is available for this window.** That means usage is unmeasured, not zero.

- **Claude Code:** most of today's coordination ran in Claude Code. Its capture route ([#4802](https://github.com/FS-GG/.github/pull/4802), merged 06:29Z) exists in source only. The engine installed on this host is `fs.gg.coord.cli` 0.102.0, which has no Claude collector, and installed adoption is still owed under [#4640](https://github.com/FS-GG/.github/issues/4640).
- **Home UTEL store:** it is not readable from here. The one receipt applied today carries Codex usage from 2026-10-18, with attribution Unknown, so it says nothing about today's cost.
- **Local file counts (shape only):** this workspace touched 3 main Claude Code transcripts and 102 subagent transcripts today, and this host has 6 Codex session files. These are file counts, not tokens or requests.

So there is no tokens-per-accepted-Delivery ratio, no role or model split and no baseline. Any claim that today was cheaper or dearer than yesterday would be invented. The cheapest way to close this gap is to install the published engine that carries the #4802 collector on the coordinating host. That is ordinary #4640 adoption, not a reporting gate.

A proxy for coordination volume does exist. The Home channel [#4516](https://github.com/FS-GG/.github/issues/4516) received **198 comments today**, peaking at 30–36 per hour between 11:00 and 14:00Z. Much of that was review rounds on admitted scripts (see below).

## Process problems we can fix

- **Long review/revise loops on Home-admitted scripts.** The pattern: the LEARN fixture took 10 revisions (r1–r10), the UTEL cutover took five attempts (two rolled back cleanly on probe defects), and the observe/enroll scripts needed a second revision. Each round crosses the Home channel, waits for a reviewer and often needs a user sudo, so the pattern stretches elapsed time more than tokens.
  - **What worked:** the safety design held. Both rollbacks left no residue, and independent review caught a real defect elsewhere: the 0.32.3 rate-limit NO-GO prevented a likely irreversible partial publish.
  - **Suspected cause:** the scripts' own probes and health checks were first exercised in the live attempt, not against the scratch copy. The 3c preflight on a scratch store passed first time; the 3d probes, which were not rehearsed that way, failed twice.
  - **Change:** before admitting a live run, run each admitted script's probes and rollback against its scratch rehearsal. Have reviewers return all findings for a revision in one pass. Separately, 0.32.3's defect hid behind a mocked network path, so the Rendering owner should add one un-mocked call-count check in the #1464 fix.
  - **Owners:** fdev (root) with Home. The benefit is plausible but unmeasured.
- **Board Status no longer shows active work.** The only In-progress cards are the Outcomes #4642 and #4648, both last updated at 03:13Z. Meanwhile #4707, #4710 and #4745 (Blocked) and #4709 (Ready) were actively worked today.
  - **Effect:** nobody can see what is running from the board, and stale owners go unnoticed. According to the session (there is no public record of it), a stalled SDD owner went unnoticed for about 5 hours.
  - **Causes:** Status writes are a heavyweight guarded step that gets deferred. Since 15:28Z the guarded writer has also been failing closed on GitHub's empty-items answers, which [Coordination#1049](https://github.com/FS-GG/FS.GG.Coordination/issues/1049) tracks.
  - **Change:** write In progress at dispatch, as the existing Status lifecycle already requires, and reconcile the four active Deliveries once reads stabilise. #1049 should classify a sudden drop to zero items as a transient provider failure, so callers can retry the read instead of refusing.
  - **Owners:** fdev for the reconciliation, and the Coordination execution owner for #1049. #1049 is not enrolled on Project 3.
- **Shared local resources and liveness are not covered by admission.** According to the session (no public record exists), parallel agents' test runs filled the shared 1 GiB `/var/tmp/fdev-podman` tmpfs twice. It was 33% used at report time. A gap in mail-watcher timing also delayed Home by about 20 minutes.
  - **Change:** give each operation its own scratch directory, or count that tmpfs as a storage reservation in the existing admission accounting. Add an owner-liveness check at each coordination boundary.
  - **Owner:** fdev. These are low-cost changes. Their effect is unmeasured because the incidents were not recorded in owning issues. Recording them in those issues would make recurrence visible.
- **Existing owned findings.** The previous report's lost-diagnostic-output finding has now recurred in a different form: the writer's readback refused without saying why. That is item 1 of #1049. The #4705 readback gap was closed by an independent comparison of the whole board, and nothing was resent.

## Blockers the current process cannot resolve

- **Install the UTEL sudoers block on Home.** Only the user can do this, because Home's non-interactive sudo requires a password. The block is approved and hash-pinned (fdev's OK is on #4516 at 16:20Z), and must go in before 2026-10-23 14:10Z.
  - **Affects:** batch-2 credential re-delivery, retention cleanup inside the rollback window, and the #4638 coverage work.
  - **Workaround:** none is supported.
  - **Coming next:** LEARN P0 will need a similar `fsgg-learn` install.
- **Ordinal-4 custody for #1026.** The original PROC custodian (the request went to @EHotwagner) must supply the authentic ordinal-4 pointers, or a per-record "unavailable" disposition. That answer is due by **2026-10-22 19:30 UTC**.
  - **Workaround:** none. Missing original evidence cannot be reconstructed, and no agent can supply it. The forward route (#1043) avoids depending on that evidence for new outputs, but the original recovery stays undeclared.
- **#1026 enablement settings.** The protected environment, OIDC/App and policy configuration needs decisions from an organization or repository admin. The route has not been exercised yet, so its exact asks are not established.
- **GitHub incident.** It is external. The only workaround is to wait and retry reads, and writes stay single-use.

## Priorities

1. **User:** install the approved UTEL sudoers block on Home before 2026-10-23 14:10Z, and answer the ordinal-4 custody request by 19:30Z today.
2. Land [Rendering#1464](https://github.com/FS-GG/FS.GG.Rendering/pull/1464), re-review it, and dispatch the 0.32.3 publication. In parallel, run #4710's hosted receiver on Templates 0.19.1. These are the two nearest paths from "implemented" to "usable".
3. Once reads stabilise, reconcile Status for active Deliveries. Rehearse admitted scripts' probes on their scratch copies before going live, and install the published engine that carries the Claude collector, so the next report can measure cost.

Sources: [Project 3](https://github.com/orgs/FS-GG/projects/3), with the snapshot at 16:19:30Z (1 of 3 reads complete, because of the incident). Owning issues and PRs are linked above, with the Home channel at [#4516](https://github.com/FS-GG/.github/issues/4516). The tmpfs, mail-watcher and stalled-owner observations come from the root session and are not yet in owning records. No telemetry store was read, activated or changed. No board, issue or product state was changed for this report.
