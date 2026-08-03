# Healthcheck S1–S8 detective and suspect generator

**Date:** 2026-08-03

**Scope:** a bounded design and evidence report for `.github#2019`. It defines
the S1–S8 mechanical suspect-generator boundary and the read-only detective
handoff. It does not implement the `org-healthcheck` skill, invent a second
gate harness, or adjudicate a current organisation-wide health verdict.

## Inputs and ownership

The generator consumes a normalized fact corpus emitted by healthcheck legs,
not repository prose and not an agent's recollection. Each row has a stable
subject and enough provenance to compare an assertion with what it asserts:

```json
{
  "subject": "FS-GG/.github:gate-harness",
  "assertedAt": "2026-08-03T00:00:00Z",
  "subjectLastChanged": "2026-08-02T00:00:00Z",
  "assertionStillGreen": true,
  "populationSize": 42,
  "comparisonsMade": 42
}
```

Rows may add the rule-specific fields below, but may not omit the common
fields. An unreadable, malformed, or incomplete corpus is a permanent
**no-verdict at exit `3`**. The executable owner imports `ExitCode`,
`GateError`, and `run` from `scripts/lib/gate.py`; it does not duplicate their
contract. Transport failures retain the shared retryable no-verdict path.

`scripts/healthcheck-suspects.fsx` is the eventual mechanical owner: it reads
the corpus, emits ranked suspects as JSON, and never turns a heuristic into a
finding. The detective is the separate causal/adjudication owner.

## S1–S8 rules and executable fixture contract

| Rule | A suspect is emitted when | Required planted fixture / contrasting control | Historical anchor |
| --- | --- | --- | --- |
| S1 — provenance older than subject | `derivedFrom`, verification time, or digest precedes the latest subject change | A changed input after its recorded digest emits S1; the same timestamps in order emit none | `.github#1576`, `.github#1546`, `.github#1577` |
| S2 — green with no work | green is recorded while the subject changed in the window and the gate ran zero times, or it compared zero subjects | A changed subject plus zero runs/comparisons emits S2; an unchanged subject or a non-zero completed comparison does not | `.github#1510`, `.github#1512`, `.github#1515`, `.github#1506` |
| S3 — motionless in a moving system | a pin, roster entry, version, or inventory did not move while each tracked upstream fact advanced | A fixed pin with an advanced feed emits S3; an intentionally pinned row carrying its explicit pin reason is not silently exonerated and requires detective evidence | `.github#1560`, `.github#1561`, `.github#1531` |
| S4 — proposals without landings | a configured window has an anomalous opened-to-merged rate, rather than merely an open PR at one instant | A corpus with many opened and few merged proposals emits S4; a comparable healthy rate emits none | `.github#1533`; `.github#1565` measured **16 opened / 4 merged** |
| S5 — asymmetric duplicates | two spellings mapped to one fact disagree in last-change/provenance state | One updated copy beside one stale copy emits S5; co-updated copies emit none | `.github#1573`, `.github#1547`, `.github#1530` |
| S6 — a count that never varies | a reported count remains constant while its measured population grows | A constant comparison count over growing populations emits S6; matching growth emits none | `.github#1506` |
| S7 — suspiciously clean | a leg has never emitted a finding across an established observation history | A non-empty, all-clean history emits S7; at least one historical finding suppresses S7 | `.github#1510`; `scripts/check-gate-finding-history.py` and `tests/gate-finding-history/run.sh` |
| S8 — identity and timing oddities | a foreign identity writes a bot branch, a claim outlives its lease, or distinct actors commit within the configured anomalous interval | A foreign actor on a bot branch and two near-simultaneous distinct actors emit S8; a normal single-actor timeline emits none | `.github#1533` |

The executable selftest must contain one planted suspect and one clean
contrasting corpus per rule. It asserts the emitted rule id, stable subject,
and evidence fields—not merely that JSON contains a rule-name substring. A
missing required field is a no-verdict fixture (exit `3`), not a clean corpus.
This makes the content usable as code-example input for the future
`org-healthcheck` skill: each rule is paired with a runnable minimal corpus
and an expected observable result.

## Ranking and record shape

Each suspect record preserves the underlying observations so a detective can
reproduce rather than trust the generator:

```json
{
  "rule": "S4",
  "subject": "FS-GG:kit-renovate-window",
  "rank": 1,
  "evidence": {
    "opened": 16,
    "merged": 4,
    "window": "configured-by-corpus"
  },
  "sourceRows": ["facts/automation.json#42"]
}
```

Ranking orders reproducibility and blast radius ahead of novelty: a candidate
with a direct API/run/commit locator and organisation-wide effect ranks above
one that is merely unusual. A rank is triage, never confirmation. The
generator reports every S-rule with zero suspects by name in its summary; an
empty rule output may not disappear from the report.

## Detective handoff

For every emitted suspect, dispatch one disposable, minted, read-only worker
with a fixed evidence budget and the exact source-row locators. The worker may
inspect APIs, runs, commits, and fixtures, but makes no repair and creates no
coordination-board item. It returns exactly one of:

1. **CONFIRMED**, with the exact command or API read and observed output that
   reproduces the discrepancy.
2. **EXONERATED**, with the concrete benign cause and evidence for it; “looks
   fine” is not an explanation.
3. **NO VERDICT**, with the unavailable evidence and the exhausted budget.

A report may include only reproduced confirmations in its proposed-work
addendum. It must retain exonerations, no-verdicts, and S-rules that produced
zero suspects. Thus no confirmations is “nothing confirmed by this run,” not
“the organisation is clean.”

## Existing executable evidence and boundaries

The future implementation should reuse rather than re-describe existing
owners: `scripts/check-gate-finding-history.py` with
`tests/gate-finding-history/run.sh` supplies the history/negative-control
pattern relevant to S7; `tests/required-contexts/run.sh` proves no comparison
population is not a pass; and the board engine's liveness/touch-set fixtures
remain the authority for the claim component of S8. The generator only joins
their normalized measurements.

This item deliberately does not turn an S-rule into a universal static scan,
nor claim that a content report replaces executable tests. Content discovered
by detectives is useful only when it becomes one of three durable inputs:
an executable fixture, a narrowly scoped code example in the skill, or a
separately evidenced coordination item.

When kit-delivery history is relevant, `.github#1565` uses the corrected
measurement **16 opened / 4 merged**. The superseded `12 opened / 0 merged`
figure is not valid evidence.
