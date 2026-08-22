# Phase 0 baseline: coordination change amplification

Snapshot date: 2026-08-22
Source head: `e31132dec1a93a260a76916c6807ed630c7cba17`

The machine-readable snapshot is `phase-0-baseline.json`. It freezes the incident corpus and metric
definitions before a new authority is introduced. It deliberately does not manufacture elapsed-time or
review-ledger values that cannot be recovered from committed repository state.

## Command-addition baseline

The `.github#2753` merge added `comment` at twelve independently authored inventory locations: five in
`Options`, three in `HandlerRegistration`, and four in positive test or dispatch inventories. This count
excludes the command's actual handler behavior, transport implementation, negative tests, documentation,
SDD artifacts, generated readiness views, packaging, and registry updates; it measures only repeated facts
that the catalogue design intends to derive or close mechanically.

The target is at least a 50% reduction in authored inventory locations. With this baseline, a future nullary
command must require at most six such locations, while the design's stronger target remains one descriptor
and one handler binding.

## Frozen incident corpus

- `.github#2753` at merge `484f23ff`: command metadata and handler registration amplification.
- `.github#2773` at merge `a6adfe30`: delivery and `verify-paths` classifier divergence.
- `.github#2819` at merge `e31132de`: review projection and escalation-writer divergence.
- `.github#2820` at merge `340cbfa3`: immutable recovery attempted to mutate release evidence.
- `.github#643` at commit `6f916e42`: narrative text triggered a premature completion projection.

These are references to immutable Git objects and committed work artifacts. Later fixtures may minimize
their facts, but must retain the incident and source-commit binding.

## Monthly measurement contract

Report the eight metric names in `phase-0-baseline.json` over at least twenty engine pull requests. Every
report must name its exact PR sample and observation window. A missing observation is reported as unknown,
not zero. Projection/writer divergence, premature `Done`, and unreceipted self-host writes have invariant
targets of zero; the metrics are not permission to average those failures away.
