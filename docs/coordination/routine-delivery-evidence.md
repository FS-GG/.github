# Routine delivery evidence pilot

`tools/routine-delivery.py --evidence-run RUN_ID` appends `deliveryEvidence` with schema
`fsgg.routine-delivery-evidence/1` to its final `fsgg.routine-delivery/v1` result.
The option collects read-only evidence after the original delivery and telemetry paths.
The optional supporting library `tools/routine-delivery-evidence.py` must accompany the helper
when selecting this option. Missing library or unexpected collection failure reports
`collection-unavailable` while retaining the original JSON and exit status. The installation
helper stays under its existing 128 KiB source-blob bound; the cap is unchanged.
It preserves their outcome, coherent-validation state, original merge identity and effect count.
Default output and telemetry input keep the legacy field set. Installation recovery accepts
both the exact legacy result and that result with the versioned extension; the extension cannot
renew an original merge attempt. Other strict consumers must continue using default output
until they explicitly support this extension. The CLI refuses `--evidence-run` together with
`--install-routine-producer` before any effect: pilot check populations can exceed the existing
64 KiB retained installation-result bound. Installation recovery retains that bound and uses
the legacy output. Supporting an extended shape does not waive its existing file-size bound.

The producer in [skill-quality](../../.github/workflows/skill-quality.yml) captures the job-entry
Git checkout commit and tree, candidate/base, repository, PR, run, attempt and job before tests.
Its artifact is uploaded even when later steps fail and retained for seven days. This covers
only `semantic-skill-quality` on PR events matching that workflow's existing paths; tools,
skills and its own workflow are covered, while arbitrary docs-only/product PRs may be absent.
It proves neither every required job's checkout nor the filesystem state throughout a test.
The Actions run's `head_sha` associates a run with a candidate; it does not identify its checkout.

The reader binds the artifact to native run/repository/PR/head/base/attempt metadata and checks
its tree against the native Git commit. A checkout is classified as `candidate`, or as
`synthetic-pr-merge` only when its ordered native parents are the exact base and candidate.
GitHub can return an empty run `pull_requests` list after merge, as observed for
[PR4597](https://github.com/FS-GG/.github/pull/4597) and
[run38860185845](https://github.com/FS-GG/.github/actions/runs/38860185845), while the native PR
head/base remain unchanged. Only for a confirmed delivered/merged PR, an explicitly empty list
can use `merged-pr-exact-checkout-parents` association: the native run branch must match the
unchanged PR branch, its repository/head/event/workflow/attempt must match, the artifact must
retain the exact PR/head/base/run joins, and its native checkout commit must have the ordered
exact base/head parents. A missing, malformed or conflicting run association, open/uncertain
merge, candidate-only checkout or unrelated head/base/branch drift remains unavailable.
The entire bounded run-link population is validated before selecting a match; malformed,
duplicate or conflicting entries refuse even after a matching entry. The final native PR
snapshot rechecks branch identity and the merged predicate, as well as head/base/merge commit.
This fallback does not relax tree parity or coherent-validation results.
`parity` compares that tested tree with the native delivered commit tree: `identical-tree`,
`different-tree`, `not-delivered` or `unavailable`. Equal trees can coexist with failed or
pending checks, a late coherent dispute, or an unresolved installation. This field grants
no acceptance, merge, publication or activation authority.

The result also retains changed filenames/status/previous filenames, native required-check
rows (including native exit-1 failed checks) and producing-run references. Unknown evidence stays null with a bounded exception label;
independent available results survive another collection failure. PR identity is checked before
and after collection; head/base drift invalidates checkout, file and check associations.
There is no retry or effect replay. Raw hosted artifacts remain the original evidence.

## Bounds and qualification

The collection gets one 60-second budget including the existing direct-child cleanup reserve.
Each response uses the existing 256 KiB native-query limit. The pilot permits at most 100
changed files, 128 required checks, 100 artifact records, one 64 KiB ZIP and one 4 KiB checkout
JSON entry, and 100 run PR links. Larger or incomplete populations remain unavailable. It does not paginate or
create a second check aggregator. The workflow adds two Git reads, a small JSON write and one
artifact upload; hosted latency and storage overhead are unmeasured.

Pipeline preflight uses focused Python fixtures and static YAML/step-order inspection. The
change adds no job dependency, fan-out, cancellation, publication or shared mutable state,
so a new model would add maintenance without testing a new scheduling interaction. The local
inspection/fixture budget is ten minutes; the existing full required checks remain applicable.
Tests cover candidate/synthetic checkout, differing trees, artifact identity/absence, head/base
drift, late coherent failure, uncertain merge and both prior-result shapes. Local fixture success
is source qualification only. The next qualification is a genuine matching skill-quality PR run,
its retained artifact, and post-delivery native commit/tree readback through this option. Record
measured collection/upload latency and exceptions before expanding the pilot's coverage.
