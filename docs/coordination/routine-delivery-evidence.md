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
100 artifact records, one 64 KiB ZIP with one 4 KiB JSON entry, and 100 run PR links. Changed-file metadata uses at most four 25-row pages within the same 100-file and 256 KiB per-response
stdout bounds. `gh api --jq` projects filename, previous filename and status before the bounded reader;
it does not bound the provider response or `gh` internal memory. Missing, denied, malformed, duplicate
or incomplete pages, or changed head/base/count, leave the population unavailable. No log ingestion is added. Larger or incomplete evidence stays unavailable. Automatic
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


## Explicit adoption of a legacy routine PR

For an existing routine PR that predates lane admission metadata, use the same
`tools/pr-lane-admission.py --update-pr <number> --previous-head-sha <old-marker-sha>`
route with explicit `--adopt-legacy-pr`, `--campaign-id <campaign>` and
`--dependency-chain-id <chain>`. Supply the existing repository, branch and base,
the exact already pushed candidate in `--head-sha`, and the reviewed title/body
through the usual `--title` and `--body-file` arguments. `--inspect` checks this
route without writing. This adds the supplied identity only when admission metadata
is entirely absent; it never repairs or replaces malformed, duplicated or conflicting
identity text. Without the flag, absent admission metadata still refuses.

Adoption requires an existing valid routine marker at the explicit previous or current
head, preserves its operation and other fields, and rebinds only its head. Unknown
machine-owned metadata is retained. Complete paginated open-PR reads must include
the same native PR exactly once, with no competing campaign/chain, unknown managed
reservation or third managed PR across campaigns. These facts are checked again before
writing, followed by the normal native metadata comparison. The route retains the
exact pushed-head check, one possible PATCH and independent native readback. An uncertain
write/readback remains unresolved and is never retried automatically. A subsequent
call with the matching existing identity follows normal managed-PR update semantics;
the flag grants no identity replacement or fresh PR creation.

This is source support for an explicitly selected adoption. It does not establish
native adoption, exact-head review/check qualification or Delivery acceptance.

## SC2 private-receiver first installation

The [canonical first-install source predicate](../../tools/routine-delivery.py) can inspect a candidate
before a PR or its checks exist. It requires the complete canonical five-file delta, unchanged remaining
receiver tree, current protected producer bytes and no partial or native producer installation. Full
PR identity, markers, file listing, native checks and qualification remain delivery requirements.

Producer identity reads start at each exact commit root and traverse complete non-recursive ancestor
directories for the five canonical files, the executing helper and maintained workspace inventory.
Shared ancestors are read once per traversal. The existing Templates assertion-pin exception also reads
its exact bundle path at the selected and prior pins. Every directory retains its native SHA, complete
non-truncated unique entries, modes and types; missing paths, symlink ancestors, malformed identities
or read limits refuse. Unrelated producer history is not enumerated. The 1 MiB native-read ceiling and
ordinary blob bounds remain unchanged. Receiver tree comparison still proves that every leaf outside
the selected installation delta is unchanged; this producer optimization does not reduce that proof.

The selected SC2 predecessor uses `pr-lane-admission.py --sc2-producer-predecessor-source <protected-sha>`
on the existing `outcome-led-programme-20261016` campaign and `sc2-routine-producer-bootstrap-20261017`
chain. It permits one third managed PR only for native private `FS-GG/FS.GG.SC2.Client` (repository ID 1390567199),
preserving the bound PR43/44 branches, heads and admission identities. Complete native reservations,
disjoint file populations, candidate ref and canonical source are rechecked before creation. Other
repositories and ordinary creation retain cap 2; unresolved reservations, drift or partial installation
refuse. A lost create response retains its original identity and is not permission to retry. Installing
the producer disables this first-install exception through the existing absence predicate.

The receiver-specific delivery contract selects its unchanged `verify.yml` (workflow ID 368229845) and verifier
entrypoint. It accepts only the native required-check reader's exact authenticated no-required-contexts
response, reported as `no-required-contexts-reported`; denial, malformed output and timeouts are not
absence. Regardless of required-context population, the complete nonempty exact-head verify cohort,
current attempt 1, Actions app 15368, run/job/check/suite joins, successful technical steps and qualified
product artifact are mandatory. Its name binds the actual synthetic checkout, while native artifact
metadata binds the candidate head; ordered base/head parents and the complete checkout tree must agree. Any reported required contexts remain additional requirements.
This makes no assertion that organization rulesets are absent and does not require a paid protection
feature or infer policy from a 403 response.

The default-branch eligibility workflow uses the receiver's automatic Actions token with contents-read
permission only. The raw token is removed before child processes; only the fixed-origin fetch receives
an ephemeral exact-host header, with redirects and credential helpers disabled. No candidate checkout
or execution occurs; the validator and its dependencies still come exclusively from the verified event
base. Local observer Checks-read and workflow-push permissions are separate capabilities.

Shared source delivery precedes regenerating the five-file receiver patch. Its actual unchanged-base
verify run, authenticated required-check observation and normal first-install merge/readback must then
qualify the predecessor. A subsequent genuine private eligibility event and newly qualified current
heads precede ordinary delivery of the original PRs. Earlier product runs retain their original heads;
source fixtures do not establish private fetch, installed activation or product acceptance.

Preflight uses existing static and actual-caller inert fixtures, with a 30-minute initial fixture-design
cap; no additional scheduling model is selected. Existing hosted budgets remain unchanged. The fixture
suite uses synthetic receiver blobs, not copied private verifier source. Ordinary source blobs retain
the 128 KiB limit. The protected helper self-correspondence read alone derives its bound from the exact
executing helper length, still under the existing 1 MiB transport ceiling and protected byte equality. Private native qualification,
local Checks-read availability and workflow-write capability remain independently observed gates.

## FourD private-receiver first installation

The named FourD adapter applies only to private `FS-GG/FS.GG.FourD` (repository ID
1390568106, default branch `main`). It preserves the five canonical producer files byte for
byte. Two additional files must equal closed transforms of their pinned receiver baselines:
the source-preparation workflow adds the five literal paths to both PR and push filters;
the workflow-inventory test admits the canonical fourth workflow with exactly one
`runs-on: ubuntu-latest` line. Existing native-runner guards and the other existing hosted
workflow guards remain intact. Missing files, additional edits or changed baselines refuse.

Both unchanged verification and derived source-preparation workflows must succeed at the
candidate head on current attempt 1. Complete Actions app 15368 run, job, check and suite
joins, successful technical steps and the verification artifact's synthetic-checkout/tree
correspondence remain mandatory. Reported required contexts add requirements to this pair.

The adapter authenticates the two accepted private/manual-process policy references and
recognizes only their exact named native-plan refusal responses. It reports
`native-policy-unavailable` separately from the required-check reader's exact authenticated
`no-required-contexts-reported` response. Denied Checks reads, malformed transport and
unexpected policy responses refuse; unavailable native policy never means empty policy.
Independent exact-head review remains an integration requirement.

Preflight reuses the bounded source predicates and inert transport/mutation fixtures, including
paired-workflow absence, provenance, policy and uncertain-apply refusals. This closed adapter
needs no additional scheduling model. Source delivery precedes receiver installation; installation
remains activation pending until a genuine subsequent private eligibility event succeeds.
