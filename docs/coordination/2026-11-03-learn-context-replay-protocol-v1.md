# Historical context reconstruction audit v1

Status: source proposal under [hardness-validation Delivery #5276](https://github.com/FS-GG/.github/issues/5276), parent #5248. This version authorizes no ratings, implementation experiments, routing or native annotations. Root retains integration and acceptance. The completed #5277 50-task judging pilot and its frozen controller, packets and results remain unchanged.

## Question and current support

Test whether richer, historically available repository context makes hardness assessments more informative than an issue-only description. That question differs from whether judges agree, whether they predict active work, and whether a task's difficulty transfers across executing models and effort levels. None follows automatically from another.

The existing census contains 3,397 issues and 8,778 PRs across 27 repositories. All 55 qualified creation-body issue histories already belong to the 50 development tasks or five excluded Outcome/administrative records. The existing histories therefore supply no independent unused task cohort. A preliminary current-parent-component exclusion leaves 3,050 public metadata candidates; that is an upper bound before the new time-block exclusion and reconstruction checks, not qualified historical context coverage.

The [existing history adapter](../../scripts/learn-history-evaluate.py) reconstructs exact issue bodies at selected boundaries. Its current [collector](../../scripts/learn-history-fetch.py) records current comment bodies and PR commit dates; those do not by themselves establish earlier comment contents or repository visibility. The new [context collector](../../scripts/learn-context-snapshot.py) adds bounded server visibility witnesses and exact local-object export. No real 16-case reconstruction audit has been run for this source proposal.

## Freeze a metadata-only audit cohort

The selector reads the complete digest-checked private census and the frozen development cohort. It excludes private/unknown-publicity issues and the complete undirected closure of every known current native parent component touching a development task. It also excludes UTC ISO creation-week blocks represented in development. Missing historical relationships remain a limitation; the current graph is a conservative available join, not proof that every historical connection is known.

Before any reconstruction, fix 16 cases through a versioned hash seed and round-robin repository/creation-month strata. Ranking uses only stable identity, repository, creation time, public scope and grouping exclusions. Current titles/bodies, outcomes, closure dates, durations, old hardness and model results do not select cases. The controller retains source-manifest and development-cohort hashes, grouping exclusions, seed, all 16 identities and selection digest.

Keep all selected cases. Unsupported, incomplete, interrupted or unavailable reconstruction is a recorded result, never a reason to draw a more convenient replacement. Eligibility and missingness are assessed before ratings or outcome analysis. Week blocking creates unused historical time groups; it is not a forward-in-time validation claim. A later prospective cohort supplies that separate test.

## Distinguish the information boundaries

| Boundary | Meaning and limit |
|---|---|
| Item creation | Exact native creation timestamp, reconstructed title/body and code provably visible before it; filing may follow earlier real work |
| First observed implementation start | Earliest authenticated available work/dispatch evidence, with source and uncertainty; it is not proof of the true first work |
| Prospective creator context | Context actually retained when a new item is created, including available requirements, research and source identities; private material stays private |
| Reconstructed public context | A documented proxy for historical information availability; missing conversation, private decisions and earlier research are not invented |

Executable v1 supports **item creation only**. It refuses a changed cutoff or another boundary kind. First-observed-start collection requires a separately versioned adapter and authentic evidence; no Git author date or present-day Current deliverable is substituted. Capture prospective creator assessments in parallel through the existing item/snapshot machinery once its actual recording route is selected. The collector itself performs no such write.

## Repository and requirements reconstruction

For each fixed case, read its native issue history once through the existing bounded query. Require complete issue edits, rename timeline and comments. Reconstruct the exact issue body and title at creation. A pre-cutoff comment whose current body was updated later is omitted with an explicit missing-revision gap. Comments created later never enter rater context. This version does not fetch related-discussion graphs or private creator conversations; those omissions are declared.

Use server `merged_at` values from the existing census to identify at most three latest pre-cutoff PR candidates for the selected repository. Read each candidate's native merge timestamp, merge commit/tree and base branch. A different branch can cause the next fixed candidate to be examined; a failed, conflicting or incomplete read stops the case without an automatic retry. Require the explicitly selected branch, native identity, matching server time and a merge before the cutoff. Git author and committer dates do not qualify visibility.

The selected commit is a **proven-before-cutoff checkpoint**, with recorded lag to the cutoff, also disclosed in the rater manifest. It is not guaranteed to be the last branch state: direct pushes, ref changes, cherry-picks and missing history can intervene. Do not silently call it an exact historical head. A missing local commit makes the case unavailable; the collector never fetches a replacement or checks out the current branch.

Verify the native commit/tree against exact local Git objects, disabling replacement refs and optional writes. Read tracked blobs without checkout, hooks, builds or executing repository code. Verify each object's Git hash, byte length and SHA256. Export only those bytes into a new private directory without `.git`, linked object stores or repository symlinks. Preserve original mode as metadata; export files without executable permission. Reject path traversal, `.git` components, backslashes/control characters, case collisions, symlink modes and size/count overflow.

Gitlinks remain opaque identities with an explicit unresolved-dependency gap, even when their objects happen to exist locally. Do not import a later dependency checkout. Git LFS pointers remain exact pointer bytes with an explicit unmaterialized-object gap. Binary blobs remain exact bytes and are identified as binary; this provides no claim that a judge interpreted them. Lockfiles are context, not proof of a recreated package/runtime environment. Existing implementation or task-relevant artifacts already present at the cutoff are not necessarily future leakage: retain the possibility of post-hoc filing and adjudicate it before treating creation as a predictive start boundary.

## Keep controller evidence out of rater context

The private output separates `controller/` from `rater/`. Controller evidence contains native identities, raw later history, source queries/hashes, visibility witnesses, cutoff/lag, source mapping and reconstruction results. Rater context contains the reconstructed task, exact exported snapshot files, content hashes and safe gaps. Its task text uses a fixed GitHub-link/reference masking transformation; source/package names can still identify repositories. This is partial identity blinding, not a claim of unrecognizability or freedom from pretraining contamination.

The [rater manifest schema](../../contracts/learn/context-snapshot-v1.schema.json) always declares `ratingLaunchAllowed: false`. No launch command exists. Incomplete exports never receive a completed rater manifest. A bundle remains private until a separately reviewed controller/runner exposes only its allowed contents.

Fresh collaboration conversations share host files and tools. They do not enforce a filesystem or network boundary. Before ratings, qualify a runner that prevents access to controller evidence, current repositories, future Git objects, other answers and unrestricted external lookup; retain actual tool-read traces. Do not present instructions to avoid those files as an enforced air gap. The inference service may remain available through its controlled channel while repository/network research is restricted to the frozen context. The collector implements none of that runtime isolation.

## Bounded audit interface

The source provides two operations, both read-only with respect to GitHub and source repositories:

- `select` creates one exclusive private selection file from the existing local inventory/development cohort. Default count is 16; no native requests occur.
- `collect-case` consumes that exact selection and an explicitly selected local repository/branch, performs bounded native reads, and creates one exclusive private case directory. It never chooses another task or mutates existing evidence.

Inputs use private, owned, nonsymlink parents; outputs are new-only with private permissions. Default per-case bounds are 120 seconds, eight native reads, 20,000 tree entries, 4 MiB per blob and 64 MiB total source blobs. Native JSON acquisition is separately capped at 8 MiB per read. The current path makes at most one issue read plus three checkpoint reads. Selection, history validation and acquisition charge the same case wall budget. Root must reserve collection/retirement time within the actual selected outer envelope; a new invocation does not renew an interrupted original audit claim.

Metadata-only execution of the selector on the retained census leaves 1,079 public candidates after 331 known development-component exclusions, 16 private exclusions and 1,971 development-week exclusions. These counts remain audit eligibility metadata, not reconstructed context coverage. The selected 16 metadata rows are only a smoke check until Root freezes a reviewed audit selection.

The first proposed real audit therefore has **16 fixed cases and at most 64 native reads**, no model calls, no builds, no mutation/telemetry/annotation effects, no dependency acquisition and no routing change. It needs separate exact-source/selection/resource admission after review. Missing local objects, visibility evidence or private context remain audit outcomes. Private artifact storage must account for all case/source/controller bytes before launch; per-case bounds do not imply an admitted 16-case aggregate reservation.

## Subsequent comparison and validity gates

After reconstruction and isolation qualification, freeze a new protocol before examining predictive results. Compare two treatments on the same common task text: issue-only versus that text plus the historical code bundle and bounded research. Use fresh agents for each item/profile/treatment; hold context/research budgets and evidence access rules constant and record actual access. Separate requested launch configuration from provider-effective evidence. Randomize presentation and treatment assignment; retain errors, missing outputs and deviations without repair or selective replacement.

The primary comparison must be specified before ratings: predictive error or calibrated ordering against a declared work/quality construct, with transparent training-median and simple task/repository/text baselines. Agreement and subjective confidence are secondary diagnostics. Preserve parent/time grouping, paired treatment comparisons, missingness and uncertainty; account for information lag and reconstruction quality. Sixteen cases form a feasibility audit, not an adequately powered invariance study or automatic license to fit a complex model.

Historical response envelopes and calendar lead time remain proxies. Code context cannot repair a missing active-work clock. Actual difficulty across executing profiles requires matched frozen implementations, common acceptance contracts, authentic configuration, observed work intervals and stopped/waiting/censored cases. Future fixes and grader tests may be controller-only evaluation material; they must not appear in research/rater inputs. Protected/native/publication tasks cannot be replayed as live effects merely to obtain a benchmark score. Keep a bounded sandbox-executable estimand separate from whole-Delivery acceptance.

Prospective intake assessments should begin accumulating once their qualified recording route is selected. Preserve what the creator actually knew, then compare against completion evidence later. Historical reconstructed context and prospective creator context answer related but different questions; neither replaces the other.

## Source qualification and remaining acceptance

Synthetic controls exercise metadata-only selection, parent/week contamination, changed outcomes, incomplete/edited history, backdated commits with later native visibility, future checkpoints, opaque future dependencies, paths/links, blob corruption, exact old-tree export, count/byte/request bounds and refusal to launch ratings. Tests use temporary local Git fixtures, mocked or in-memory provider facts and fixed synthetic UTC timestamps; they make no native API calls or experiment launches.

Root owns census/workflow/Atlas/generated integration and normal source review/CI/merge. This source proposal supplies a reconstruction mechanism and audit plan, not completed historical coverage, a qualified isolated rater runtime, active-work measurements, calibrated classification or cross-profile validity. All remaining #5276/#5248 promises remain open.
