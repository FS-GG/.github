# Delivery backfill — native sub-issues for every open Outcome

This carrier records the native Delivery layer that Root/fdev added on 2026-10-31 under [COORD-EFF-02 #4730](https://github.com/FS-GG/.github/issues/4730). It answers the user's report that Coordination Project 3 showed neither the work done in the previous day nor the work still open, and that Outcomes stayed open with every listed Delivery closed. Before this, Root's recent work was tracked only as PRs carrying `Refs #<outcome>`. Several Outcomes (#4645, #4647, #4648, #4650, Game#718, Coordination#1026) looked finished only because every native sub-issue they had was closed. Their remaining acceptance had never been filed as Deliveries.

The proposed list was posted before any write ([#4730 issuecomment-6540174984](https://github.com/FS-GG/.github/issues/4730#issuecomment-6540174984)). It covers 150 PRs created or merged since 2026-10-30T00:00Z across nine repositories, plus the SDK pins in four more, and a reading of each open Outcome's Done when, owning plan, sub-issues and recent comments. No Outcome met all of its acceptance with evidence, so none is proposed for closure.

Two constraints held the board writes. The installed `coord-0.113.0` reader refuses more than 100 enrolled rows, and 93 are enrolled ([#4941 issuecomment-6519516153](https://github.com/FS-GG/.github/issues/4941#issuecomment-6519516153)). Project 3 workflow 4, "Auto-add sub-issues to project", was enabled, and unadmitted memberships had already broken `migration-prepare` and `inspect` on 2026-10-30. Root chose A then C. The owner disabled workflow 4, and the backfill then created and linked native issues only. Enrollment through the #4941 batched planner (C) follows the coordination CLI release that lifts the cap.

Root/fdev created 98 Delivery issues and made 11 reuse links. Each was serial, with an append-only custody journal and independent readback of body, parent and project membership:

- Every new issue is a native sub-issue of its Outcome. Its body carries the six-field contract and its intended Work level, Track and Status, and uses Refs only.
- The 25 Done Deliveries were closed as completed, each with a comment citing its merge commits and recorded verdicts.
- Where no verdict was found, the issue was still closed as Done under Root's ruling and says "Unknown: no recorded independent verdict". This applies to .github #4983, #4997, #4987, #4969, #4982 and #4991, and to the kit-materialize receiver PRs.
- Workflow 4 read `enabled: false` before the first effect and after the last. Project 3 held 98 items before and after, and no new issue has a Project 3 card. No Status, Track, Roadmap or Work level field was written, and no Outcome item or other item was changed.

| Outcome | New Done | New open (Ready / Backlog / Blocked / In progress / In review) | Reused, linked now | Sub-issues completed / total after |
|---|---|---|---|---|
| [#4638](https://github.com/FS-GG/.github/issues/4638) UTEL | 3 (5038–5040) | 1 / 2 / 1 / 1 / 0 (5037, 5041–5044) | #5007 | 4 / 10 |
| [#4640](https://github.com/FS-GG/.github/issues/4640) EFF | 0 | 0 / 3 / 2 / 0 / 0 (5045–5049) | — | 3 / 8 |
| [#4730](https://github.com/FS-GG/.github/issues/4730) COORD-EFF-02 | 7 (5050, 5052, 5056–5059, 5061) | 0 / 0 / 2 / 2 / 2 (5051, 5053–5055, 5060, 5062) | #5018 | 10 / 19 |
| [Coordination#1026](https://github.com/FS-GG/FS.GG.Coordination/issues/1026) PROC | 2 (Coord 1093–1094) | 0 / 2 / 1 / 1 / 2 (Coord 1095–1098, SDD#1147, FsQuint#50) | — | 5 / 11 |
| [#4647](https://github.com/FS-GG/.github/issues/4647) LEARN | 4 (5063–5065, Coord 1099) | 0 / 5 / 1 / 0 / 1 (5066–5071, Coord 1100) | — | 9 / 16 |
| [#4650](https://github.com/FS-GG/.github/issues/4650) V3-ENV and native chain | 2 (5072–5073) | 0 / 2 / 5 / 0 / 1 (5074–5077, Coord 1101 M0, 1102 M1, 1103 registration, 1104 M2) | #4778, #4820, #4821, Coord#1088 | 3 / 15 |
| [#4648](https://github.com/FS-GG/.github/issues/4648) LANG | 0 | 1 / 1 / 3 / 0 / 1 (5078–5079, Coord 1105–1108) | — | 3 / 9 |
| [#4645](https://github.com/FS-GG/.github/issues/4645) SVG | 2 (Templates#830, Rendering#1494) | 2 / 2 / 3 / 0 / 0 (Rendering 1495–1500, 5080) | Templates#827, #828 | 8 / 17 |
| [#4646](https://github.com/FS-GG/.github/issues/4646) FABLE | 0 | 0 / 1 / 0 / 0 / 0 (5081) | — | 2 / 6 |
| [Game#718](https://github.com/FS-GG/FS.GG.Game/issues/718) WASM | 0 | 1 / 1 / 4 / 0 / 0 (Game 738–743) | SC2.Client#49 | 1 / 8 |
| [#4649](https://github.com/FS-GG/.github/issues/4649) OPS-TYPED | 0 | 0 / 1 / 1 / 0 / 0 (5082–5083) | Coord#1051 | 0 / 4 |
| [SDD#928](https://github.com/FS-GG/FS.GG.SDD/issues/928) | 1 (Governance#479) | 1 / 1 / 0 / 0 / 0 (SDD 1148–1149) | — | 6 / 8 |
| [Unity.Client#7](https://github.com/FS-GG/FS.GG.Unity.Client/issues/7) | 3 (25–27) | 0 / 2 / 2 / 0 / 1 (28–32) | — | 3 / 8 |
| [#4842](https://github.com/FS-GG/.github/issues/4842) BARC | 0 | 0 / 0 / 4 / 0 / 0 (5084–5087) | Coord#1032 | 0 / 5 |
| [#4935](https://github.com/FS-GG/.github/issues/4935) SDK-PATCH | 1 (5088) | 1 / 1 / 1 / 0 / 0 (5089–5090, Templates#831) | — | 2 / 9 |

Bare numbers are `FS-GG/.github` issues unless prefixed. In total there are 25 Done, 7 Ready, 24 Backlog, 30 Blocked, 4 In progress and 8 In review.

Remaining for Root:

- **Enrollment (C).** It needs the release and installed adoption of #4940/#4941 with the 250-row cap, then one batched carrier enrolling the new identities with Work level Delivery, Track, the parent's Roadmap and the intended Status, through `migration-apply` with readback.
- **#4935** is on the board with every field empty and is Outcome-shaped. It needs Root classification.
- **Stale values.** #4640's board Status is stale, and the body text of #4815, #4818, #4819, #4821 and Coordination#1032 is stale. Those are owner edits, not part of this carrier.
- **Unity.Client#7** has .2–.7 still undivided. They are proposed as separate Outcomes.

All original holds and reservations stay as recorded. No resource release, enrollment or Outcome closure is claimed.
