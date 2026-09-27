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
