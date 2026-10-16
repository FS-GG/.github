# Routine delivery checkout evidence

Ordinary `tools/routine-delivery.py` results for `FS-GG/.github` automatically append
`deliveryEvidence`, schema `fsgg.routine-delivery-evidence/1`, to the existing
`fsgg.routine-delivery/v1` result. The maintained route collects one bounded result after the
original delivery and telemetry paths. It preserves their outcome, coherent-validation state,
original merge identity, exit status and effect count. Collection never retries a merge.

The automatic reader selects the native run from the receiver-required `projection` check's
GitHub job link. It requires one exact `coherence`/`projection` PR context, then verifies the
native job's repository endpoint, run, attempt, name and candidate. It rechecks the projection
reference after collection; a replacement run or malformed, absent, conflicting or stale
reference leaves checkout evidence unavailable. It does not guess the newest workflow run.
`--evidence-run RUN_ID` remains available for explicit historical qualification.

The optional supporting library `tools/routine-delivery-evidence.py` must accompany the helper.
Missing support or unexpected collection failure produces `collection-unavailable` and an
`attention` assessment while retaining the original result. The installed helper remains below
its existing 128 KiB source-blob cap. Installation and recovery continue emitting the exact
legacy shape; `--evidence-run` cannot combine with `--install-routine-producer`. Recovery accepts
legacy or recognized extended results without renewing an original effect, within its unchanged
64 KiB retained-file bound. Other repositories keep legacy output unless explicitly selecting a run.

## Producer and native identity

The sole live producer is the existing required `projection` job in
[coherence](../../.github/workflows/coherence.yml). Its unfiltered opened, synchronize, reopened
and edited PR events include docs-only changes. Before validation it captures job-entry Git
commit/tree, candidate/base, repository, PR, run, attempt and job. Its always-uploaded artifact
is retained for seven days. Capture and upload continue on error so evidence collection cannot
suppress projection validation or replace its verdict. Missing artifacts remain unavailable.
This is one job-entry checkout witness, not proof of every job or its filesystem throughout a test.

One reader accepts exact workflow/job pairs: `coherence.yml/projection` reports scope
`github-pr-checkout`; retained artifacts from the retired
`skill-quality.yml/semantic-skill-quality` pair report `skill-quality-pilot`. Both use
`fsgg.tested-checkout/1`, `checkout.json`, and `tested-checkout-RUN-ATTEMPT`. No new job, metadata
ledger or CI check aggregator is introduced. Run `head_sha` associates a candidate; actual
checkout identity comes from the artifact and its native Git commit/tree.

Artifact joins must match repository/PR/head/base/run/attempt/job. The tested commit is classified
as `candidate`, or `synthetic-pr-merge` only with native ordered parents `[base, candidate]`.
After merge, GitHub can return an explicitly empty run `pull_requests` list. Only a confirmed
native delivery can then use `merged-pr-exact-checkout-parents`: unchanged PR branch, matching
run branch/repository/head/event/workflow/attempt, exact artifact joins and native synthetic
checkout parents are all required. Missing, malformed, duplicate or conflicting links, unrelated
drift, uncertain effects and candidate-only fallback remain unavailable. The complete bounded
link population and final PR head/base/branch/merged predicate are checked.

`parity` compares the tested tree with the original native delivered commit tree:
`identical-tree`, `different-tree`, `not-delivered` or `unavailable`. Equal trees can coexist with
a failed check or coherent dispute. The original delivery result retains that failure.

## Compact delivery assessment and consumers

A clean result still requires the ordinary `--coherent-workflow coherence.yml` selection for
this repository. Omitting it preserves the legacy `coherentValidation: not-required` result and
reports `attention` with `coherent-validation-not-required`; automatic checkout discovery does
not invent a coherent-validation verdict or silently change the selected validation route.

The extension retains changed file/status/previous filename records, required check rows
(including native exit-1 failures), producing-run references and explicit collection exceptions.
Its `assessment` is `ready`, `delivered` or `attention`, with `issues` naming gaps. A clean
assessment requires the corresponding original ready/delivered outcome, passed coherent
validation, and routine validation either passed or explicitly not required by the existing
native admission route. Unobserved, unresolved and installation-specific routine states remain
attention. It also requires a successful producer run, all observed required checks passing, a verified
checkout and, after delivery, identical trees. Pending publication remains an issue. This is a
projection of evidence; it grants no merge, publication or activation authority and changes no gate.

The maintained public [observer](../../tools/routine-observer.py) accepts the bounded versioned
shape and reports its assessment as `reported`; it does not reauthenticate the supplied JSON or
promote it to economic qualification. Unknown nested fields are refused by the existing public
I/O reader. Internal telemetry still receives the unchanged Summary. Other strict consumers
require their own explicit adoption.

The final compact JSON uses the existing 64 KiB custody budget. If check/file populations exceed
remaining space, they become null with `result-populations-over-budget` and `attention`; essential
run/checkout identities remain when they fit. If the extension still exceeds the budget, a compact
`result-over-budget` exception replaces it. Original native artifacts remain the evidence;
omitted populations never count as complete. The legacy Summary itself is preserved.

## Bounds and qualification

Collection has one 60-second budget, including existing direct-child cleanup reserve. Each native
response uses the existing 256 KiB limit. Bounds remain 100 changed files, 128 required checks,
100 artifact records, one 64 KiB ZIP with one 4 KiB JSON entry, and 100 run PR links. No population
pagination or log ingestion is added. Larger or incomplete evidence stays unavailable. Automatic
selection adds one native job read, a final run identity read and a required-check reread within that same budget.

Pipeline preflight uses focused Python fixtures and existing static checks: no job dependency,
fan-out, cancellation or shared-state interaction changes, so a new scheduling model has no
identified benefit. Local qualification is capped at ten minutes before reassessment; existing
hosted checks remain required. Producer/upload overhead and total delivery savings are unmeasured.
No measurement or comparison is a new delivery gate.

[PR4602](https://github.com/FS-GG/.github/pull/4602) qualified historical merged-run association.
[PR4604](https://github.com/FS-GG/.github/pull/4604) delivered the universal producer.
The genuine docs-only [PR4607](https://github.com/FS-GG/.github/pull/4607) scheduled no skill-quality
run; coherence run38866764067/artifact12030918081 supplied synthetic checkout7dbb1112 and tree2a7bc98a.
Its protected native merge638b4b26 retained that tree, with eight required checks passing and no
collection exception. These observations establish producer coverage and explicit-run readback;
the automatic selection and consumer changes were then independently reviewed and delivered
in [PR4609](https://github.com/FS-GG/.github/pull/4609). Its actual apply and subsequent read-only
collection selected the required projection run automatically, reported matching tested/delivered
trees and all eight required checks passing, and returned `delivered` with no evidence issues.
This qualifies the maintained repository helper and reader for the stated scope; fleet
installation and every-job test-time filesystem coverage remain separate.
