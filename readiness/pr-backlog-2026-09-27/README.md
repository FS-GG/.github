# Organization PR backlog reset — 2026-09-27

The live FS-GG census counted 300 open PRs before this reset. A live organization search at 2026-09-27 08:41 UTC counted 82 open PRs. The search was complete (`incomplete_results=false`), but the count can move as bots create or update PRs.

## Guarded draft closure

| Repository | Drafts closed | Local preservation | Readback |
|---|---:|---|---|
| FsQuint | 12 | Current-main 13-patch equivalent FSC-08 candidate, later merged as #28 | `fsquint-closure.json` |
| FS.GG.Rendering | 11 | Three current-main FSC-06/FSC-08 candidates; first refreshed after #1272 and admitted as #1343 | `rendering-closure.json` |
| FS.GG.Audio | 7 | 11-patch FSC-06 candidate rebased after merged #312, later merged as #321 | `audio-closure.json` |
| FS.GG.Game | 14 | Two disjoint staging and pin candidates; original tips are exact ancestors | `game-closure.json` |
| FS.GG.Templates | 111 | Provider, both SVG forks, and independent provider-floor candidates; #498 later reconciled locally with the provider stack | `templates-closure.json` |
| FS.GG.SDD | 63 | Full main chain plus sibling #1003 preserved in a locally qualified candidate | `sdd-closure.json` |
| **Total** | **218** | | |

Each closure was preceded by an exact live PR head/base and remote branch comparison against its repository snapshot or preservation manifest. Every closed PR was read back as closed and unmerged. A final `git ls-remote --heads` audit rechecked all 218 source branches at their recorded full SHAs. No source branch was deleted. The snapshot, candidate, and closure JSON files beside this report retain the row-level evidence. The separate source candidates still require their own current-head hosted gates before merge; closure itself grants no publication, receiver adoption, or roadmap acceptance.

## Source delivered during this reset

- FsQuint #28 merged from `7209a9b353bf22fc9e0eee6958bc25c6d65ed791` at `c1347d948b416825f5301a51adcc56f0fb2ce544` after pinned local and hosted qualification.
- Audio #312 merged from `86d7dad366c773148f5014ab3ebd0a8c1b06f78f` at `5fb981307e23f687d5927d0f7b37f956db2cc3d9`.
- Audio #321 merged from `806b479816b7b89caf43bf87957422d94bac50ce` at `650e12822512cb3faaa48e0b379b57d55a201198` after current-head hosted qualification.
- Net #92 merged from `b8d0f7ac481147298076120d5ecb079aed59aa9d` at `4674c2ef84060ccec1d411b7744f4deb471f55af`; a transient GitHub 504 was read back as still open before a successful retry.
- Rendering #1272 merged from `14d9ac6519a6e4a34c60fa8ad5515742ba7fd21a` at `05e969159010ab132b9c3b412f9145aada15ae77`.
- Game #608 merged from `5e9472b1e7bdbc3506d7d495b6f98ccc9860abb2` at `c1ff05d579326c36c70c39a8bb5699f843132be0`.

Rendering #1343 is the single open managed replacement at this checkpoint. It uses exact head `5404494364b1d82c5eedae7562be8039a432d118` and requires current-head protected checks before merge.

## Queue controls and remaining work

The parent integrator admits one managed PR per dependency chain and at most two managed PRs per repository. It checks each repository's live hosted queue before admission; independent repositories can qualify in parallel when capacity is available. Intermediate source batches remain local. Existing ready PRs are merged only after exact-head checks and current-main integration review. The nine September 13 queued runs identified in the census could not be canceled: both normal and force-cancel GitHub requests returned HTTP 409, so no capacity was inferred from them.

SDD, Templates, Rendering, and Game preserved candidates remain distinct from merged source, publication, installed receiver adoption, and roadmap acceptance. Net #57 needs the #53 protobuf-net.Grpc version plus repaired lockfiles; a local combined candidate passes locked restore and 29 tests. SDD and Templates source cuts and Game batches are being prepared against current main. Protected `.github` #3695 and Coordination #545 holds were not bypassed.

## 08:48 UTC continuation

Five Renovate onboarding PRs were closed with exact-head readback and retained source refs: Coordination.Authority #1, GitHub.Substrate.Sandbox #3, and #1 in each of the SVG qualification-clean, public-clean and public-retained fixtures. Their six-line `renovate.json` proposals would activate dependency bot queues in retained qualification/sandbox fixtures; the two public SVG PRs also had failing protected browser builds. Row-level evidence is in `renovate-onboarding-closures.json`.

SDD #929 was closed unmerged at exact head `780b656c8ab83b10340f8f0258cf0e5568f78102`: its Go 1.27.1 bump fails the Q1-qualified Go 1.24.1 binary hash, so a future compiler change needs a new governed toolchain qualification. Its branch remains retained. SDD #919 merged from exact head `734b9d36fd43b2ba5c38422f409d3c9140049677` at `0dbf5ce9e42d18ddec45dd66b706f8c2984b7184`. Its merge is incorporated into the refreshed local FSC-04 first source cut.

A live complete organization search at 2026-09-27 08:48 UTC reported 77 open PRs. This is a moving count; new bot PRs can offset closures. Rendering #1343 remains in hosted qualification with its deterministic gate in progress.

## 08:58 UTC continuation

SDD #1065 (Fantomas 8.0.5) merged from exact green head `a581e32d949b91a55f299ecfbbb961352056c72f` at `36fb1ceaf0b7c5db2d17958840cf2fa886592c39`. Rendering #1343 merged from exact green head `5404494364b1d82c5eedae7562be8039a432d118` at `0aea2cdbd7288ee96413d14db22178cac8f0bfb3`; its release-preflight source is delivered, while publication and receiver adoption remain separate gates. Both post-merge push suites are still running at this checkpoint.

Templates #473 closed unmerged at exact head `a3a996759651a3547c274c14c9966a036d429b98`, with `chore/bump-fs-gg-ui-template` retained at that SHA. Current main already has the exact 0.31.0 README/provider pin and completed release history. The old PR would reintroduce a `PIN HISTORY ENTRY REQUIRED` stub and had failed composition. Closure was read back as closed and unmerged; the retained branch was rechecked. Row-level evidence is in `templates-473-closure.json`.

Locally prepared, source-preserving next cuts are the SDD FSC-04 first cut, Templates provider cut (body in `templates-provider-first-cut-pr-body.md`), Game staging and pin cuts, and Rendering's characterized FSC-06/FSC-08 successors. Their source-only checks are recorded in the respective worktrees; none is counted as delivered until guarded admission, exact-head hosted qualification, and merge. The live complete organization search after these three PR dispositions counted 74 open PRs.

## 09:03 UTC continuation

FsQuint #7 and #10 merged serially at `009c35c198a80a8e6bd0fbc2eb53d8cbac9c135d` and `73a7acb049e8d2d16beadc6bba3569b989b0a8e0` from their exact green heads. Local combined qualification had passed before the batch. The final-main CI completed successfully; the superseded intermediate-main run was canceled after the second merge, avoiding a redundant qualification. Existing FsQuint #6 was then fast-forward repaired in place from `ad9567add3039294dee8bebaed712114e15cd09b` to `c228dfb7c4843fe0fa507c604a20ea0295f41ede`. The two-file BoundedQueue FSharp.Core 10.1.401 repair passed locked restore, the 66-check core suite, package builds and example locally; exact repaired-head hosted checks are now in progress. No replacement PR was opened.

## 09:10 UTC continuation

FsQuint #6 merged from exact green repaired head `c228dfb7c4843fe0fa507c604a20ea0295f41ede` at `4866bf280e6315bd60b334964fb334416490967e`; its post-merge CI passed. Audio #310, #293, #295 and #296 merged serially from exact green heads at `dd876a7154b8e6acdbea195e6768239723dfca01`, `be4a92db3cff0ac10edb09138dc18b7ae558486b`, `22180cc8312faccc0692c8ad40d297540690869e` and `8eefbca2a5f13c63a61726c9554539dd5f5631f9`. Before the batch, an isolated replay of all four passed cold locked restore, Debug/Release zero-warning builds and 97 headless tests in each configuration. Remaining PR heads and mergeability were re-read after every merge. Superseded Audio push runs were canceled where GitHub had not already done so; the final main head remains in CI.

SDD's post-#1065 main gate passed. Renovate #1064 had rebased to `5348aca2f68b4d909d6f00155e011e8fd304bee1`; the lockfile repair was replayed and tested on that exact head, then fast-forward pushed to `e8f6b233abb99176643f891f8c9bffe6106fd53a`. Its new hosted checks are pending. A complete organization search at 09:09 UTC counted 68 open PRs.

## 09:16 UTC continuation

Audio's final combined main gate and browser run both passed; all superseded batch runs read back canceled. Game #643 was fast-forward repaired in place from `4867971ef2a2c3fbd7bab7ae56cc4689010ece31` to `d4a60ce46fea89fc83b014c748fff3caa3b56d5b`, adding the matching Audio.Host 0.6.0 pin to its Audio.Core 0.6.0 update. Local locked restore and pin-coherence checks passed, then every exact-head hosted check passed and #643 merged at `4169122b115bde21a8f2d3693eee94053dfbe75f`. Its post-merge push gate is pending. The redundant single-pin Game #355 closed unmerged at exact head `687145fc76ef6353c95b7553396609f9cd36bf74` after main was read back with both 0.6.0 pins; the source ref remains retained and `game-355-closure.json` records the closure. A complete organization search then counted 66 open PRs.

## 09:26 UTC continuation

Game #643's post-merge gate passed. SDD #1064 merged from exact green repaired head `e8f6b233abb99176643f891f8c9bffe6106fd53a` at `61ca836421b6c9c15e51ee40ba8c24dd40ba0ea3`; its post-merge gate is in progress. The SDD FSC-04 local first cut was refreshed to this base with all 21 source patch IDs retained.

Rendering #1053 merged from exact green head `fc25931ee3d86c462400d44d0a90102ef0018cc1` at `1211b7799548fe8e951127c2ec9e06eff948cff2`. Its post-merge gate run `36308935378` failed one Issue1256 Chromium frame-budget sample (one dropped frame) although #1053 changed only workflow upload-artifact and #1343's prior main gate passed. The failure is under focused triage; the next Rendering PR remains unadmitted. A source-preserving consolidated test-infrastructure branch is refreshed and remote-retained at `dd822cb714d4fde355bf2898a97aff6ecb54a5f2`.

Net #95 opened from a branch with an `item/` prefix, which made protected receiver validation require a native `fsgg:pr-authorization` marker absent from this routine consolidation. All source/build checks passed, but the authorization failure was correct. #95 closed unmerged at exact head `54b7de145a1dcaa9c98daa1d7f6bc7102b0f37f1`; its branch was retained, and `net-95-route-closure.json` records the readback. The identical commit was admitted through the guard as #96 on `routine/net-ready-dependencies-20260927`; new exact-branch checks are in progress. Original #41/#43/#44/#49/#52 remain open until #96 qualifies and merges. A complete live search at 09:26 UTC counted 69 open PRs, including new Renovate PRs and #96.

## 09:28 UTC continuation

Net #96's new receiver validation and all other required checks passed at exact head `54b7de145a1dcaa9c98daa1d7f6bc7102b0f37f1`, then it merged at `5b76131772c62606cb3082b7a646d487c33bd0f8`. Original #49, #44, #43, #41 and #52 closed unmerged only after main was read back with all five intended package versions, the retained #92 YoloDev version and the merged #96 commit. Every original head and source branch was checked immediately before closure and again afterward. Row-level evidence is in `net-ready-original-closures.json`. The replacement's post-merge CI is pending. A complete organization search after those dispositions counted 63 open PRs.

## 09:32 UTC continuation

Net #96's post-merge `gate` and `coordination-coherence` runs passed at main `5b76131772c62606cb3082b7a646d487c33bd0f8`. SDD #1064's post-merge gate passed at main `61ca836421b6c9c15e51ee40ba8c24dd40ba0ea3`. Governance #431 (Fantomas 8.0.5) merged from exact green head `ba147a4984ffd7ffe5ee30e8275eb5d7ad51b47b` at `f4eb16f06d933674c81aad038f2a60b78a3be06b`; the merge and main commit were read back, and its post-merge gate remains in progress. Rendering's single exact-main rerun of `36308935378` remains in progress, so no new Rendering PR has been admitted. The complete organization search counted 66 open PRs at 09:31 UTC as new bot PRs arrived.

## 09:40 UTC continuation

Net #98 (FS.GG.Kit 0.91.4) independently merged at `0b1014f3a98ecd87631ac5ed73980c693feb7a4c`; its main gate and coherence run passed. Net #53 (protobuf-net.Grpc 1.3.14) then merged from exact green head `cb9bd7a3b69e334816f61d05f2217f40002a33de` at `65e57fef8c15271fbbdb127ae37fe5cea65e55af`, with the main gate and coherence run passing. Existing #57 was repaired in place by a normal fast-forward from `9202e53eb3ce768f65c37d9b24c9feebed5c8491` to `773169097527e1d193490699a3c24d4e93e489c3`; the exact actual-main candidate passed locked restore, zero-warning build, 29 tests and release source contract before push. Its new hosted checks are pending.

SDD #1066 (Fable.Core 5.3.0) merged from exact green head `7f1bce7817f2912c72cf3bd1c29bde2be7073022` at `76fc434308081bc4fab7001daaecb38a4e18800c`; its post-merge gate is in progress. Game #660 (FS.GG.Coord.Cli 0.91.4) merged from exact green head `e48c07ddbe196ebe3097ce749297b78d54f1916e` at `9fffe4e60b581a5ca565851d0f16696a0cb737f5`; its post-merge gate is queued. A complete organization search at 09:36 UTC counted 62 open PRs. The next same-repository merges are waiting for their current-main gates to finish, preventing redundant or canceled hosted qualification.

## 09:50 UTC continuation

Rendering main `1211b7799548fe8e951127c2ec9e06eff948cff2` passed the single exact-commit rerun of gate `36308935378` on attempt 2, including the unchanged zero-drop browser budget. The prepared test-infrastructure stack was admitted by the guarded helper as #1345 at exact head `dd822cb714d4fde355bf2898a97aff6ecb54a5f2` on that main; its 23 hosted contexts are qualifying. Original #851/#1238/#1236/#1052 remain open until #1345 passes and merges.

Net #57 merged from its repaired exact green head `773169097527e1d193490699a3c24d4e93e489c3` at `337cf3451a9281731d80da369ced01ee12246608`; its main gate and coherence run passed. Net #94 (Fantomas 8.0.5) then merged from exact green head `c849ef5027c496eef758c7c74e354235043efd6d` at `5df9c2db9f6ee3546985054ab84a9b021733f12e`; its post-merge gate is queued.

Game #660's post-merge gate passed. Game #661 (FS.GG.Kit 0.91.4) auto-rebased to exact green head `ca466cfcf331e4efc7f5cbe1777cda0ba290ba6b` on #660 main and merged at `130f1956a4187d12938a3d7f3b8a9bc920bd568e`; its main gate is queued. Governance #431's main gate passed, then #345 (checkout v7) auto-rebased and merged from exact green head `cd6f93f4a1db49341c17d68b0ab1d79c374a9027` at `0afb9d9b32cddade8966b2a24e55d06313d66f6f`; its main gate is running. SDD #1066's main gate passed, then #994 (Artifacts 2.0.2 fixture) merged from exact green head `2ab37b3d4826f5f0219c12f17b78ea1531533a96` at `e4c43a57e30a426900b162a9f8771ee826fee99e`; its main gate is running. Templates #408 (tsx v4) merged from exact green head `8900bbab2049af3663ba5591f48550701632d7d7` at `a4147485803510bed891a1cfc46491c6e50f399a`; its composition gate is queued.

Audio #314 was repaired on its existing Renovate branch to `3963b55cd01199d8eb3bf1cc1e2c5868fbb0e20e`, fixing the observed `NU1008` artifact restore failure. Native build, lock, browser, receiver and kit checks passed, but `materialize / kit-bump-mechanical` correctly reports verdict 4 for a mechanical pin plus receiver-authored repair. That verdict is an explicit human-review fence, so a separate source-repair branch is being prepared before any #314 merge. A complete organization search at 09:50 UTC counted 55 open PRs; bot churn and a new private SC2 source PR are included in that moving total.

## 10:10 UTC continuation

The complete organization search at 10:04 counted 53 open PRs, down from the initial 300; new bot and product PRs make the live total variable. The private SC2 Client Renovate onboarding #1 was closed at exact head with its branch retained. Private SC2 Client #2 merged at `c90c07fc32c9c17374ecfef9d1df1b019e973f2a` and its main `verify` passed; private FourD #1 and #2 merged at `5661d85ce62264b84a56a4fb32716cd1350408ff` and `8096fd19431a74fff7366217142908c4bf226496`, each with passing exact-main `verify`. Their source milestones are projected separately in the Unified Roadmap; neither merge establishes live or installed operation.

Governance #345 and #346 merged serially at `0afb9d9b32cddade8966b2a24e55d06313d66f6f` and `364a4bc92917b04c6e4f069d4bcc1319115f404e`; the latter main gate remains in progress. SDD #994's exact-main checks passed, then #941 setup-go v7 merged from green head `c79aa80c4b9587a921da555f8f30950dbef3dc04` at `abe44656f2f1b628fbb99fed99acc634aa742733`; its main gate is pending. Net #40 merged at `4541c4f101524cdff88f71680b935db68a0e67ea` with a passing main gate. Net #35 was repaired by a normal fast-forward to `ae43afe54bfb1e2f387cde148035ec4965b3e3f9`, preserving package and lock bytes; fresh exact-head hosted checks are pending.

Rendering #1345 merged from green head `dd822cb714d4fde355bf2898a97aff6ecb54a5f2` at `f6b3627903c94794ef08886fafd48aa6d1fc3bdb`; its main gate and browser suites are in progress, so original #851/#1238/#1236/#1052 remain open. Audio #322's fixture repair passed the main gate, then the existing #314 branch was restored to a pure Kit pin at `15a76a1a9f7d5d1cd047e98501a78e07b0becca4`; all exact-head checks, including mechanical eligibility, passed. #314 merged at `11334f05cb5bc18b5b1a83f186ad665f9118d87a` and its main browser/gate runs are pending. Game #659's exact-main gate passed, then #443 checkout digest merged from green head `e1fbae90574bb9df03fb74edfc3f2b971cb3b87d`; its new main gate is pending.

Templates #408's main composition passed. One consolidated browser tooling PR #610 was admitted through the queue guard at exact head `cf4feaa928378b16cc2486e536dbc15724d545bf`, carrying Vite, Playwright and TypeScript pin intent from #412/#375/#373. Its hosted checks are pending. The three originals remain open until a passing main readback. The guard admitted one managed Templates PR and no duplicate chain. Protected `.github` #3695 and Coordination #545 remain held; GS2-09.7 provider and GS2-09.9 operator qualification continue in isolated source lanes without protected effects.
