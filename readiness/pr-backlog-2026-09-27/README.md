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

The parent integrator admits no more than two actively qualifying heads across the programme, one managed PR per dependency chain and at most two managed PRs per repository. Intermediate source batches remain local. Existing ready PRs are merged only after exact-head checks and current-main integration review. The nine September 13 queued runs identified in the census could not be canceled: both normal and force-cancel GitHub requests returned HTTP 409, so no capacity was inferred from them.

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
