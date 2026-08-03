# `org-healthcheck` operator skill

**Date:** 2026-08-03

**Scope:** the bounded operator-skill design for `.github#2021`.  It specifies
how an operator runs and interprets the already-defined healthcheck legs.  It
does not add a scanner, change a repository runtime or public contract, or
duplicate the shared gate harness.

`org-healthcheck` is the approved name: it is an operator skill over the whole
roster, rather than an `fs-gg-*` product skill.  The skill is a consumer of
the leg contracts in the healthcheck reports, not their replacement.  In
particular, a clean run means only that the declared, readable population had
no findings; it is never an organisation-wide cleanliness assertion.

## Operator contract and bounded inputs

Before running a leg, the skill records the immutable revision/API snapshot,
the review window, the selected repositories, and the complete comparison
population.  It refuses a conclusion when any of those facts is missing,
unreadable, malformed, or only partly observed.  A repository omitted because
its evidence cannot be read is a no-verdict, not a clean exclusion.

The skill reuses the common command edge in `scripts/lib/gate.py`:

```python
from lib.gate import ExitCode, GateError, run
```

The eventual executable invokes its leg function through `run`; it imports
the shared symbols rather than copying their numeric values, exception
handling, or shell table.  A definite incomplete subject raises `GateError`.
Consequently the required permanent no-verdict is the shared exit **3**, while
transient access failures retain the shared retryable no-verdict path.  Neither
kind of no-verdict may be rendered as green or silently converted into a
finding.

The skill has three layers with separate owners:

| Layer | Input | Output / owner |
| --- | --- | --- |
| Leg collector | authoritative run, API, artifact, and repository facts | normalized evidence rows; each leg report owns its schema |
| Shared gate edge | normalized rows plus complete population | clean, finding, or no-verdict through `gate.py` |
| Operator skill | exact invocation, evidence locations, and disposition | a reproducible run record and causal handoff; it does not adjudicate a suspect by prose |

This separation prevents the skill from creating a second gate contract or
turning a heuristic into a confirmed defect.  A specific assertion in the
run record names its command, immutable URL, artifact, or `path:line`; an
unverified assertion says so explicitly and cannot support a clean conclusion.

## Reusable run procedure

1. Select only legs whose report defines their subject, population, evidence,
   no-verdict boundary, and contrasting controls.  Record the selected leg
   versions and the roster/revision before collection.
2. Run the reusable leg executable against the declared corpus.  Preserve the
   input digest, stdout/stderr, exit result, and source-row/evidence locators.
   Do not replace an unavailable input with an empty list or a remembered
   result.
3. Interpret the shared gate outcome: clean is a completed comparison with no
   findings; a finding is a readable discrepancy; a no-verdict is incomplete
   evidence.  The skill publishes all three outcomes and the population count.
4. For a mechanical suspect, hand the stable subject and source rows to the
   bounded detective procedure.  Only a reproduced root cause can become a
   coordination item; an exoneration and a no-verdict remain visible in the
   report.
5. Feed useful content back into durable artefacts.  A recurring result becomes
   an executable fixture, a small runnable skill/code example, or a narrowly
   evidenced board item—not a remembered operator convention.

The command surface is intentionally small: one invocation takes a named leg
and its declared corpus/revision, and emits a machine-readable record beside a
human-readable summary.  It does not accept a free-form claim that a
repository is healthy, and it does not run a partial roster as if it were the
whole organization.

## Concrete controls and code-example inputs

Every future leg has a checked-in minimal corpus with an asserted observable
result.  The operator skill links to and reuses these fixtures; it must not
copy their checks into prose or a second shell implementation.

| Control | Minimal fixture | Expected result | Reused in skill/example |
| --- | --- | --- | --- |
| Clean contrasting control | Complete declared population; every required evidence locator is readable and no leg discrepancy exists | shared clean outcome | Runnable `org-healthcheck` example showing the recorded population and clean summary |
| Missing population negative control | One declared repository/surface is absent or unreadable | `GateError`, permanent no-verdict (exit 3) | Example demonstrates that partial roster input cannot pass |
| Malformed evidence negative control | A required normalized row lacks its subject, revision, or provenance locator | `GateError`, permanent no-verdict (exit 3) | Example shows the shared error path, not a custom exit table |
| S-rule contrasting controls | For each S1–S8 rule, one planted suspect and one complete clean corpus | stable rule/subject/evidence for suspect; no suspect for clean corpus | The skill points to `healthcheck-suspects` fixtures and shows how to inspect their records |
| Architecture route negative control | A repository names reachable functionality but supplies no built route and player/test journey | `GateError`, permanent no-verdict (exit 3) | The skill links the architecture-verdict fixture rather than teaching source-only certification |

These are executable-contract requirements, not illustrative prose.  For
example, an S4 fixture with anomalous proposals must preserve the corrected
`.github#1565` measurement of **16 opened / 4 merged** where that history is
used; the superseded 12/0 figure is never an example input.  A later skill
example imports or invokes the shared gate and the corresponding fixture,
asserts its exact outcome, stable subject, and evidence fields, and never
reimplements `ExitCode` or `GateError`.

## Relationship to the existing leg reports

The skill starts with reports that already own their domain facts:

- `2026-08-02-required-context-reconciliation.md` owns its authenticated
  branch-protection boundary.
- `2026-08-02-sparse-checkout-closure-fleet.md`,
  `2026-08-02-automation-liveness-and-pin-feed-drift.md`,
  `2026-08-02-roster-and-registry-reconciliation.md`,
  `2026-08-02-cli-surface-reconciliation.md`, and
  `2026-08-03-board-hygiene.md` own their respective populations and fixture
  boundaries.
- `2026-08-03-healthcheck-s1-s8-detective.md` owns mechanical suspect
  generation and the read-only detective handoff.
- `2026-08-03-per-repo-architecture-verdicts.md` owns repository architecture
  records, dispositions, and built-route/journey evidence.

The operator skill makes the relationships navigable: for each selected leg it
links the authoritative report, fixture, executable owner, population, and
evidence output.  It neither normalizes away a leg's no-verdict boundary nor
promotes a report into an implementation before that executable item is
separately scoped and routed.

## Acceptance boundary for later implementation

A later implementation may add the skill, registry/manifest/catalog wiring,
and runnable examples only after its own delivery-route decision.  It passes
this design when it:

1. invokes the shared `gate.py` contract rather than duplicating it;
2. keeps a permanent no-verdict at exit 3 through `GateError` and demonstrates
   it with the missing-population and malformed-evidence fixtures;
3. carries one planted suspect and one clean contrasting corpus for every
   selected S-rule, asserting subject and evidence rather than text matches;
4. exposes reports, fixtures, and runnable examples as mutually linked durable
   skill inputs; and
5. preserves a complete population/revision/evidence record so a clean result
   cannot overclaim beyond what the run observed.

That implementation is deliberately outside this item's `docs/reports` touch
set.  If authoring it requires scripts, generated contracts, or runtime work,
the mandatory delivery route must be decided again before the scope expands.
