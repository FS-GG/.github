# Roster and registry reconciliation — healthcheck legs 11–12

**Date:** 2026-08-02

**Scope:** the organisation roster in `registry/repos.yml` and the producer-facing
skill inventories. This is a bounded healthcheck definition, not a new skill, a new
registry format, or a second implementation of an existing gate.

**Verdict vocabulary:** a complete clean comparison is exit `0`; a measured mismatch
is exit `1`; and an input that cannot establish the comparison is a no-verdict at exit
`3`. Future executable legs must reuse `ExitCode` and `GateError` from
`scripts/lib/gate.py` (and its `run` wrapper), not restate that contract. A no-verdict
is never a clean reconciliation.

## Leg 11: roster ↔ reality reconciliation

The roster's `receives:` entries are claims about what each repository actually wires.
The comparison is bidirectional and per capability:

1. Every rostered receiver must have the workflow, caller, materializer, or
   authority-push arrangement declared by that capability.
2. Every discovered receiver arrangement must have the matching `receives:` claim.

`scripts/repos-audit.sh` already performs this fleet traversal from
`registry/repos.yml`; it must remain the executable owner rather than a new script
with a second capability map. `scripts/repos.sh validate` is the structural companion:
it checks the roster vocabulary, declared detector shape, and generated kit lock.

Roster closure is a second boundary. `scripts/check-roster-closure.py` compares the
roster with both `registry/dependencies.yml` and a complete GitHub organisation
listing. A contract participant absent from the roster, a live unrostered repository,
or a stale `outside-fabric` exception is a concrete exit-`1` finding. This is the
FS.GG.Audio class: a repository can otherwise be invisible to every fabric precisely
because it is missing from the list those fabrics iterate.

An empty, unreadable, or demonstrably incomplete organisation listing is instead an
**exit-3 no-verdict**. In particular, a token that cannot see all private repositories
cannot prove the roster is closed, so the leg must not convert the partial listing into
a green result or a roster defect. `GateError` is the mechanism for this permanent
could-not-grade path; transient transport failures retain the shared runner's
retryable no-verdict behavior.

### Negative control

`tests/roster-closure/run.sh` supplies both sides of the control. It injects a
`dependencies.yml` participant absent from the roster and a live repository that is
neither rostered nor exempted; each must return exit `1` with its distinct cause. It
then supplies an empty, unreadable, or visibility-incomplete organisation listing;
each must return exit `3`, never `0` and never an invented roster finding. The
unmodified closed-world fixture remains the clean control.

## Leg 12: registry ↔ producer-manifest reconciliation

The active registry check is **registry = manifest = bytes**: every
`registry/skills.yml` row must resolve to its one authoritative producer manifest and
to canonical source-body bytes. The driver manifest and the consumer union are related
inventory boundaries, but they do not authorize a second producer or a second copy of
a body. The consumer boundary is the declared two-root set, `.claude/skills` and
`.agents/skills`.

The existing `scripts/fsgg-skill-registry-check` owns the registry = manifest = bytes
comparison: source existence, canonical digest, declared completeness, predicate, and
source ownership. `skill-union-assert` applies the two-root consumer-union boundary;
the driver-manifest generator supplies the corresponding driver inventory. ADR-0022
§6's frozen-mirror classification and cross-tree byte comparison were retired by
`#1862`; this leg must not reintroduce them. Scheduled
`skill-registry-coherence` is necessary because a producer can change without a
`.github` commit, leaving a locally green authority checkout with a stale catalogue.

The leg must distinguish a confirmed disagreement from an ungradable producer. A
missing required root, unreadable manifest or registry, malformed row, or ambiguous
producer identity is **exit 3** through `GateError`: there is no complete population to
compare. A row that is readable but has no matching producer, a producer omitted from
the required inventory, or a registry digest that differs from its authoritative
manifest/body is an exit-`1` finding. The report must name the source artifact and the
missing or divergent counterpart; a count without both sides is not evidence of
reconciliation.

### Negative control

The skill-registry coherence fixture must mutate one readable side at a time: remove
or alter a registry/manifest correspondence and require exit `1` naming the affected
skill; leave the matching fixture green. Separately, remove or corrupt a required
producer input and require exit `3`. This prevents the two dangerous regressions: a
catalogue mismatch silently passing, and an incomplete producer population being
reported as healthy.

## Evidence and interpretation

Each run should retain the roster revision, the GitHub listing/query boundary (for
leg 11), the roots and manifest revisions (for leg 12), and every ungraded subject
with its no-verdict reason. It should report per-capability and per-skill counts only
after the complete relevant population was established.

The historical kit-delivery observation from `#1565`, where relevant to a roster
consumer, is **16 opened / 4 merged**. The superseded `12 opened / 0 merged` figure is
not valid evidence and must not appear in a fixture, report, or acceptance check.

This document does not claim a present organisation-wide health result. It records the
existing executable owners and the conditions a future `org-healthcheck` run must
preserve before it can make one.
