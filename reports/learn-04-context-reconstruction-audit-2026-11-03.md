# Historical context reconstruction: 13 bundles from a fixed 16-case audit

Date: 2026-11-03. Delivery [#5276](https://github.com/FS-GG/.github/issues/5276), under [#5248](https://github.com/FS-GG/.github/issues/5248). Programme/Root accepted this bounded reconstruction result; the broader hardness-validity promise remains open. This report contains safe aggregates only.

**Historical repository context can be reconstructed for useful research.** Thirteen of 16 fixed cases produced verified context bundles. Two lacked a qualifying strictly earlier merge checkpoint in the available census; one contained an unsupported link/tree mode. All first outcomes remain retained, without replacement or retry. This establishes feasibility for a later context experiment. It does not establish a good hardness predictor or invariance across executing models and effort levels.

The [public aggregate](learn-04-context-reconstruction-audit-2026-11-03.json) records the counts and resource observations. Native case identities, mappings, individual task text, repository/era cells, code bundles, current histories and raw responses remain private.

## Fixed selection and reconstruction boundary

The existing complete census contains 3,397 issues and 8,778 PRs across 27 repositories. Its 55 qualified creation-body issue histories are already the 50-task classifier development sample or five excluded Outcome/administrative records; they are not an independent unused cohort.

The new selector excluded 331 issue nodes in known development parent components, 16 private records outside those components and 1,971 records in development UTC creation-week blocks. That left **1,079 public metadata candidates**. A fixed seed and round-robin repository/month strata selected 16 before native reconstruction. Outcomes, durations, current task text, old hardness and prediction results did not select them. The current-parent graph comes from the frozen census and may omit historical or later relationships. Unused historical weeks are not a claim of forward-in-time validation.

This audit assessed those same 16 cases. It did not replace cases that lacked evidence or were unsupported. The 13 successes are a feasibility result within this selected audit, not an estimate of coverage across all 3,397 issues.

The [separate protocol](../docs/coordination/2026-11-03-learn-context-replay-protocol-v1.md) distinguishes item creation, first observed implementation start and the true start of work. Executable v1 supports item creation only. A timestamped filing may occur after research or partial implementation. Ratings given that state concern remaining work or accepted success conditional on supplied information; they cannot be compared without qualification to total work or intrinsic demand measured before the work began. Changed scope and late filing remain unadjudicated uncertainties.

## What the collector verified

The reviewed [collector](../scripts/learn-context-snapshot.py), [manifest contract](../contracts/learn/context-snapshot-v1.schema.json) and [synthetic controls](../tests/learn-context-snapshot/test_snapshot.py) bind source provenance and enforce the following boundary:

- Reconstruct the exact creation body and title from complete qualified native revision/rename histories. Later current bodies are controller evidence, never substitutes for historical input.
- Use server merge timestamps, native merge commit/tree identities and the explicitly selected branch. Git author/committer dates alone do not establish visibility. No exact historical default/head is inferred from a current branch name.
- Require witnesses strictly before the cutoff. Equal timestamp buckets do not establish ordering. Creation-title renames at or after creation are reversed; same-time comments and merges cannot supply earlier context.
- Verify local commit, root/subtree objects and each exported blob. Export exact bytes without checkout, hooks, Git stores or snapshot-code execution. Disable implicit fetching from partial/promisor clones; missing local objects remain unavailable.
- Keep raw later histories, mappings, timing diagnostics and source joins controller-only. Uniform limitations and facts of the allowed earlier tree may enter the rater manifest. No marker or count revealing later event existence is exposed merely because its content was omitted.

Independent source review found three real defects before the audit: lazy fetching could write a supposedly read-only source store; same-second events could be mistaken for prior information; and conditional omission diagnostics could reveal later activity. All three were repaired before native collection. Causal fixtures demonstrate unguarded versus guarded promisor behavior, strict timestamp handling, and byte-identical rater artifacts for identical permitted evidence despite different later histories. Sixteen synthetic controls passed.

The audited source was candidate `219a29add6688f3287636842ffb03bb3479cf459`, tree `3208269aa846bfe2767850f2fd328d23645067bb`. Its four collector/schema/protocol/test blobs exactly matched independently reviewed `5becba8100b07529e58f7051a115ecff295add09`; the additional integration wired controls into the existing LEARN fixture job. This report describes the subsequent audit, rather than silently rewriting the earlier protocol/source record.

## Observed coverage and resource use

| Observation | Result |
|---|---:|
| Fixed cases / built bundles / retained unavailable | 16 / 13 / 3 |
| No strictly earlier merge checkpoint candidate | 2 |
| Unsupported link/tree mode | 1 |
| Exported files / source bytes | 18,718 / 199,649,050 |
| Native case CLI invocations / prior preflight | 30 / 1 |
| Collection wall time / collection cgroup CPU | 240.62 s / 158.57 s |
| Private bytes after readback | 267,612,160 |
| Physical memory limit and peak | 536,870,912 bytes |
| Memory-max events / OOM events / OOM kills | 25 / 0 / 0 |
| Rating calls / native mutations / replacements / retries | 0 / 0 / 0 / 0 |

Invocation counts are bounded CLI acquisitions, not independently measured wire-level HTTP attempts. All 16 originals ran sequentially under one CPU, 512 MiB physical cgroup memory, zero swap and a 128-task ceiling. No virtual-address cap was imposed on Go/Git. The memory limit was reached with reclaim pressure; it was not an OOM-free run with abundant headroom. None of the three unavailable outcomes was attributed to a memory failure.

The original aggregate clock conservatively began at 16:22 UTC before retained setup/preflight, with a 17:02 UTC deadline and a three-minute final reserve. Collection and readback completed within that unchanged horizon. Private disk stayed below 2 GiB. Every original case group was waited and retired, captures were closed after termination, and outside readback confirmed the original controller PID absent, both ephemeral scopes inactive and their cgroups gone in the same boot/namespace. No persistent timer or watcher remains.

Readback verified every built task/manifest hash, all 18,718 blob byte lengths, SHA256/Git hashes, native witness joins and unchanged frozen source/input hashes. Unavailable cases retained their original partial evidence and received no completed rater manifest.

## Limits of the historical information

Checkpoint lag was **57 seconds minimum, 89,662 seconds median (about 24.9 hours), and 729,052 seconds maximum (about 8.44 days)**. These are proven-before-cutoff checkpoints, not exact historical branch heads. Intervening direct pushes, ref changes or unpublished working state may be missing. A missing earlier PR-merge witness does not mean Git history or some other historical witness is absent; it means this bounded adapter did not qualify one.

Source manifests declare unresolved Gitlinks and unmaterialized Git LFS objects rather than importing later dependency checkouts. Binary bytes are retained without a claim that they have been interpreted. Lockfiles do not establish a reconstructed runtime. Related discussions, external research and private creator conversation remain incomplete. The common task text and available code do not recover everything an orchestrating agent knew when it filed an item.

Artifact separation was checked, but **shared-agent filesystem/network isolation is not qualified**. All manifests retain `ratingLaunchAllowed: false`. No rater was launched, no source snapshot was built or executed, and no prediction/routing policy was fitted or activated. Calendar time and historical response envelopes remain imperfect outcomes; richer context does not repair a missing active-work clock.

## Reproduction, custody and next bounded steps

The public aggregate is a deliberately small projection, not a private-case reproduction dataset. The source's metadata selector, bounded collector and tests accompany this report. Programme/Root is custodian of raw native histories, source mappings, frozen selection, code bundles and runtime evidence under the qualified host's private recovery root `~/.local/share/fs-gg-private/5276-context-audit-20261103T1622Z/`. The private `audit-proof-manifest.json` hashes the clock, frozen inputs, selection, originals, aggregate and readback/retirement proofs. No raw rows or runtime logs belong in Atlas.

Next steps are proposals, not completed evidence:

1. Qualify a rater runtime that can expose only permitted frozen context and retain actual read traces. Fresh conversations alone cannot enforce that boundary.
2. Freeze a paired issue-only versus context-plus-bounded-research protocol with identical target/cutoff, common information budgets, fresh item/profile/treatment agents and retained failures. Disclose reconstruction missingness and context lag; do not select favorable outcomes or replace these unavailable cases.
3. Evaluate predictive benefit against transparent baselines and a declared work/quality construct. Agreement and confidence are secondary. A small reconstruction audit cannot establish statistical power or executing-profile invariance.
4. Accumulate prospective creator-context assessments and authentic work/stop/wait evidence through the existing qualified recording route. Matched implementation trials require their own bounded, common acceptance and configuration evidence; protected native effects cannot be replayed to manufacture benchmark labels.

Source/report publication, installed or selected caller adoption, actual isolated rating, active-work measurement and whole #5276/#5248 validity acceptance remain distinct. This audit narrows the reconstruction uncertainty; it makes no token, cost, delivery-throughput or predictor-quality claim.
