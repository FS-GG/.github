# Independent review and material filing

Review authority is the append-only structured ledger. Every decision is a digest-chained JSON
record posted with `scripts/fsgg-coord review record <ref> <draft.json> --pr <n> --json`.
Narrative prose, quoted JSON, and historical marker-shaped text carry no authority.

## Wire contract

<!-- BEGIN GENERATED: fsgg-protocol:review-policy -->
*Generated structured review contract. The digest validator and state machine consume these exact values.*

| fact | value |
|---|---|
| schema | `fsgg.coord.review-decision/v2` |
| kinds | `initial, confirmation, escalation, repair-phase, acceptance` |
| ordinary repair ceiling | 3 |
| repair-phase ceiling | 10 |

<!-- END GENERATED: fsgg-protocol:review-policy -->

- Marker: `<!-- fsgg:review-decision/v2 -->` followed immediately by one JSON object.
- Schema: `fsgg.coord.review-decision/v2`.
- Kinds: `initial`, `confirmation`, `escalation`, `repair-phase`, `acceptance`.
- Revisions start at one and are contiguous. `previousDigest` binds the prior canonical record.
- Every record binds the exact PR subject, 40-hex head SHA, minted critic identity, policy version,
  timestamp, kind-specific fields, and its canonical digest.
- Generic route identities such as `fsgg-critic-normal` are not minted critic identities.

Prepare a draft with the schema fields; the writer seals revision, predecessor, and digest from the
live ledger. It rejects malformed, gapped, stale, or concurrently advanced ledgers and never falls
back to prose.

## State transitions

<!-- BEGIN GENERATED: fsgg-protocol:lifecycle-policy -->
*Generated lifecycle boundary. These are machine-owned prerequisites; judgement about the work remains authored.*

Required housekeeping: `host-identity`, `stale-claim`, `engine-currency`, `pending-writes`, `reconcile`, `triage`.

Host acceptance fields: `accepted-head`, `initial-review`, `latest-confirmation`.

Terminal transition evidence: `merge` → `post-merge-obligations` → `done-stamp`.

<!-- END GENERATED: fsgg-protocol:lifecycle-policy -->

<!-- BEGIN GENERATED: fsgg-protocol:ledger-policy -->
*Generated ledger schema. The receipt id binds these fields; prose does not substitute for the ledger.*

Schema: `fsgg.coord.planning-receipt/3`.

Observation fields: `kind`, `observedAt`, `sourceSha`, `outcome`, `receiptId`.

Receipt fields: `schema`, `observedAt`, `sourceSha`, `complete`, `consolidationApproved`, `observations`, `contentIntakes`, `contentDispositions`.

<!-- END GENERATED: fsgg-protocol:ledger-policy -->

Mint a critic with `eval "$(scripts/fsgg-coord whoami --mint)"`. The `initial` record has verdict
`pass` or `changes-required`, round zero, and no review backlinks. A passing record carries either
four ordered meaningful-route evidence strings or exactly one not-meaningful reason. Set
`diffAuditRequired` when mechanically discovered semantic replacements exist.

After material repair, the same critic posts a `confirmation` for the new exact head. Rounds are
contiguous and one-based; `initialReview` names the initial comment and `precedingReview` names the
immediately prior structured comment. At most three ordinary confirmations are allowed.

If that ceiling is exhausted, append `escalation` then `repair-phase`. Escalation without the typed
repair-phase fact has no authority. Repair phase permits at most ten confirmations before human
escalation.

Only the host posts `acceptance`, after the latest critic record is `pass` and all checks are green.
It uses verdict `accepted`, binds the exact head, initial URL and latest critic URL, follows the
critic comment, and preserves generation critic identity. When diff audit is required it carries
base64 typed receipts; the engine recomputes live inventory and refuses missing, malformed, stale,
partial-coverage, mixed-head, or byte-drifted evidence.

Immediately before merge run:

`scripts/fsgg-coord landable <pr> --repo <repo> --wait --sha <head> --require fsgg:review-decision/v2`

A moved head retires the accepted older generation without rewriting it and requires a fresh initial
generation. Backlinks, head bindings, critic continuity, and digest continuity fail closed.

## Independent critic boundary

The critic is independent of implementation context: provide the roadmap/spec, diff, exact head and
verification evidence, but not hidden implementation reasoning. The same critic handles repairs in a
generation. Use the explicit succession workflow if replacement is unavoidable; prose is never
authority.

Report concrete findings first, ordered by severity and linked to files or commands. A pass means no
unresolved material finding remains at the reviewed head. The host validates the ledger and checks
itself and never translates a prose verdict into a structured pass.

## Gate-inversion evidence

A gate that has never been red is equally consistent with "nothing was ever wrong" and "it cannot
fire", and reading cannot separate those. `.github#2223` measured ten such gates in one run across six
items and four repositories, three months after `.github#1610` found the same class. So these are
numbered steps, not a virtue some critics happen to have. The bound is one mutation per touched gate,
plus the single non-vacuity leg step 2 names; this is never a suite-wide sweep.

1. **Inventory the gates the change adds or modifies, and show each one is REACHED.** A gate is
   anything whose purpose is to refuse: a test, an assertion, a fixture case, a checker script, a
   workflow step, a schema or parser rule. The inventory is bounded to what the diff touches — never
   the whole suite. For each, name the workflow, the job, and the invocation line that actually calls
   it. Every step below measures whether a gate *can* fire; not one of them asks whether anything
   ever *runs* it. A gate no workflow invokes is graded `NOT_MEASURED` at best and is material by the
   same logic step 3 applies to a surviving inversion: inverting it by hand reds exactly as step 2
   asks and certifies nothing, because the CI signal it exists to produce has never once been
   generated. `.github#2537` is the worked instance, cross-referenced here rather than restated, and
   its own repair belongs to it.

   **"Reached" includes the trigger's own `paths:` filter, evaluated against THIS change.** Where a
   gate runs under a `paths:`-filtered workflow, name the filter and the path in this diff that
   matches it. A gate that is wired, invertible, and simply never triggered for the diff in hand is
   indistinguishable from an absent one — and it is not merely accidentally silent but *selectively*
   silent: a path filter is quietest on the additive changes that leave a stale artifact in place, and
   loudest on the destructive ones that were easiest to notice anyway. On `.github#2230`,
   `.github#2510`'s coverage gate fired only because that change happened to DELETE files under a
   watched glob; the "keep both homes" variant — add the new home, leave the old one populated — would
   have matched no path in the filter at all, and two disagreeing copies would have landed with
   nothing watching. That variant is the one a reasonable implementer picks precisely to stay green.

2. **Invert each gate exactly once by breaking its SUBJECT, and show it examined something.** Break
   the thing the gate claims to protect — not the gate's own predicate — run the suite, and record the
   exact mutation and the exact observed result under `Verification:`. Where a subject mutation
   genuinely cannot be constructed, say so, record predicate inversion as the strictly weaker evidence
   it is, and grade the gate `NOT_MEASURED` — never `JUSTIFIED`.

   **Vacuous green: a gate can also pass because it examined nothing.** Breaking the subject presumes
   the gate had a subject in front of it. Where a gate's verdict is computed over a corpus, a fixture
   set, or any input collection, empty that collection and re-run: if the gate still passes, then
   "found nothing" and "looked at nothing" share an exit code, which is `#266`'s shape one layer in.
   `.github#2534` measured the need — of seven gate mutations the most load-bearing was the one that
   emptied the scanned corpus, and only a separate non-vacuity leg caught the vacuous pass;
   `.github#2510` measured its half-closed form, where a repair that closed "the declared root does
   not exist" left "the root exists but is EMPTY" producing the identical confident green.

   **A source-text gate has this failure mode and a behavioural gate does not.** An empty corpus
   satisfies a gate that greps for a name in a way it cannot satisfy a gate that executes the code. So
   a gate whose subject is source text carries a **non-vacuity leg** — the gate shown red on a
   non-empty corpus containing a genuine offender, so the two outcomes stop being indistinguishable.
   That leg is the one further mutation this section requires, it is owed only by source-text gates,
   and it is why a self-test for a scanner **calls** the scanner rather than grepping for its name.

3. **A surviving inversion is material by definition** — not a judgement call, not a style note, and
   not something a later round may absorb silently. So is a gate graded `NOT_MEASURED` because nothing
   invokes it.

4. **A test that claims a property must provide it.** Where the property is supplied by a test other
   than the one named for it, name that provider; a test named for an invariant it does not exercise
   is a decorative gate whichever way it happens to be passing.

5. **The fixture must reproduce production.** A fixture that omits the shape production has cannot go
   red on it, so the inversion has measured the fixture rather than the subject.

6. **The measurement environment must not supply what production lacks.** A run with a tool,
   credential, path, or file that CI does not have measures a different system; say which environment
   produced each observation.

For each touched gate the review marker names the mutation applied, the observed result, and the
workflow and job that invoke it — with the trigger's path filter and the path in this diff that
matches it, wherever that trigger is filtered — and, where the gate's subject is source text, its
non-vacuity leg. For each gate whose inversion could not be obtained, the reason, which is
`NOT_MEASURED` and never a pass. `scripts/gate-mutate.py` is this org's harness for the sweep and its
verdict vocabulary is the one to use: `JUSTIFIED` fired, `DECORATIVE` could not fire, `NOT_MEASURED`
obtained no measurement.
