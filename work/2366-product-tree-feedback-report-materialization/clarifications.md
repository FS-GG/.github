---
schemaVersion: 1
workId: 2366-product-tree-feedback-report-materialization
title: Product Tree Feedback Report Materialization
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2366-product-tree-feedback-report-materialization/spec.md
publicOrToolFacingImpact: true
---

# Product Tree Feedback Report Materialization Clarifications

## Source Specification
- work/2366-product-tree-feedback-report-materialization/spec.md

## Clarification Questions
- CQ-001: Should FR-001's cross-reference implication check use a full symbolic predicate solver, or is domain enumeration over corpus-observed parameter values sufficient?

## Answers
- CQ-001: Domain enumeration is sufficient for the registry's current shape (schemaVersion 3 declares `parameters: [profile, lifecycle, feedback, designSystem]`, but every live `materializes-when` clause today references only `profile`, with five observed values across the whole file). A full solver would require guessing an unbounded value domain the registry never declares; enumeration over corpus-observed values plus the unset/empty string is exact whenever every legal value of every referenced parameter already appears somewhere in the corpus — true today by inspection — and is a documented, bounded guarantee rather than a silent gap if the registry later gains a parameter value that appears in no `materializes-when` clause yet.

## Decisions
- DEC-001 [CQ-001]: FR-001's implication check is implemented by enumerating the cartesian product of each referenced parameter's corpus-observed value set (plus the unset/empty-string state), evaluating both predicates under a small grammar mirroring `skill-union-assert.sh`'s `eval_clause`/`eval_and`/`eval_condition` (`always`/`true`/`false`, `key in [...]`, `key == v`, `key != v`, `and`/`or`), and failing on any combination where the referencing predicate holds and the referenced predicate does not. This is documented in the check's own docstring as a bounded guarantee (sound over the observed-value domain, not a universal solver), matching the file's existing convention of stating what each check does and does not prove.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2366-product-tree-feedback-report-materialization`.
