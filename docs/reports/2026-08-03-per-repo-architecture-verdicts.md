# Per-repository architecture review verdicts

**Date:** 2026-08-03

**Scope:** a bounded healthcheck definition for `.github#2020`. It defines what a per-repository architecture review may conclude and what evidence that conclusion requires. It does not add a scanner, change a repository's runtime or public contracts, or turn a prose review into an organisation-wide health claim.

The purpose is to stop the most misleading architecture-review result: a well-written report that has read only part of its subject but concludes that the subject is sound. An architecture verdict is a result over a declared, readable comparison population, not a reviewer impression.

## Verdict record and ownership

The eventual `org-healthcheck` architecture leg emits one record per repository and review window. Its command edge imports `ExitCode`, `GateError`, and `run` from `scripts/lib/gate.py`; it does not copy their numeric or exception contract. Pure collection or comparison helpers may return typed/JSON data, but only that shared command edge turns the result into a process exit.

Each record identifies the review subject, the commit or API snapshot it read, the expected architectural surfaces, the surfaces actually observed, and the evidence locator for every conclusion. The minimal shape is:

```json
{
  "repository": "FS-GG/.github",
  "subject": "architecture-review",
  "observedAt": "2026-08-03T00:00:00Z",
  "revision": "<reviewed commit or immutable API snapshot>",
  "expectedSurfaces": ["entrypoint", "runtime", "tests", "delivery"],
  "observedSurfaces": ["entrypoint", "runtime", "tests", "delivery"],
  "gateOutcome": "ExitCode.OK | ExitCode.FINDING | no-verdict",
  "architectureDisposition": "no change | targeted simplification | rewrite worth it | no-verdict",
  "evidence": ["path:line or immutable API/run URL"]
}
```

The surface list is repository-specific but not discretionary after the review starts: it is derived from declared product/automation boundaries, then recorded with the verdict. Examples include a command or application entry point, its owning runtime/module, the executable test or player journey that reaches it, and its release/deployment edge. A repo with no product runtime still has an automation and delivery boundary; absence of an F# project does not make it exempt from architecture review.

The review reuses existing authorities rather than recreating them:

| Question | Existing owner / evidence |
| --- | --- |
| Was the board/process state established? | `fsgg-coord` fresh typed reads and the coordination-engine fixtures |
| Does a gate distinguish clean, finding, and unavailable evidence? | `scripts/lib/gate.py` (`ExitCode`, `GateError`, `run`) |
| Does a claimed product route actually work? | A built artifact and a bot-driven journey through the real entry/input surface |
| Is a proposed skill rule durable? | An executable fixture or narrowly scoped code example in the skill, not a prose-only recollection |

For meaningful reachable behaviour, source inspection is insufficient. The review records the built artifact, executed command, compared production route, and observed result. For game functionality, the journey boots the real entry point and uses player-emittable input; direct message injection or a seeded mid-game model is not a substitute. Where no meaningful production-route comparison exists, the record says why rather than silently claiming one.

## Architecture disposition

The shared gate outcome and the architecture decision are separate facts. A readable finding can coexist with a `no change` disposition when the finding does not alter the architecture; a clean gate outcome cannot by itself select simplification or rewrite. A complete review therefore records exactly one of these dispositions:

- **`no change`** — the current boundaries remain the least-cost way to preserve the observed behaviour. The record states the bounded alternatives considered and why their expected profit does not exceed their migration, compatibility, operational, and verification cost.
- **`targeted simplification`** — a bounded ownership, dependency, duplication, or seam change has a stated cost and measurable profit, while preserving the current architecture. Evidence names the affected surfaces, the migration/verification cost, and the expected reduction in coupling, recurring operational work, or future change cost.
- **`rewrite worth it`** — the current architecture cannot economically reach its required behaviour or evolution path through bounded simplification. The record compares a rewrite's delivery, migration, compatibility, rollout, and verification cost with the specific profit: retired recurring failure/cost, simpler ownership, or a capability otherwise unreachable. “Newer” or “cleaner” is not profit evidence.
- **`no-verdict`** — insufficient evidence exists to make the architecture decision. This remains distinct from a recommendation to make no change and cannot be silently converted into one.

Targeted-simplification and rewrite recommendations require both cost and profit evidence; `no change` requires the bounded rationale above. The report routes a readable architectural defect to its established root-cause repository, but does not recommend a rewrite merely because an adjacent gate finding exists.

## Exit semantics and no-verdict boundary

The command edge returns the symbolic shared `ExitCode` outcomes through `run`; `gate.py` remains their sole numeric and exception-contract owner. The one required numeric statement is that a permanent no-verdict is exit **`3`** through `GateError` / `ExitCode.NO_VERDICT_PERMANENT` when the review cannot establish its subject. A malformed corpus, missing review revision, absent required surface, unparseable architecture declaration, or a missing built-artifact/journey where the repository claims reachable functionality are all such conditions. A transport/rate-limit failure remains the shared retryable no-verdict path.

Neither no-verdict may become clean. In particular, an empty surface list does not prove a repository has no architecture, and a report that can read source but cannot reach the named runtime/test/delivery evidence has not completed the comparison. This is the same fail-closed distinction the shared harness makes: “could not look” is neither a finding nor a pass.

## Executable negative controls and skill/code-example handoff

The future implementation must carry a minimal planted fixture for each verdict outcome, exercised through the shared gate wrapper:

1. A complete repository corpus with all declared surfaces observed and a passing built-route comparison yields `ExitCode.OK`. This is the contrasting clean control, not an empty corpus.
2. A corpus whose runtime entry point has no reachable test/player journey raises `GateError` and yields the required permanent no-verdict exit `3`, even if source files and unit tests are present. This prevents a source-only review from certifying an unreachable feature.
3. A corpus with an observed contradiction — for example a delivery declaration naming a package or workflow no runtime/release surface owns — yields `ExitCode.FINDING` with the stable subject and both evidence locators.
4. A malformed or incomplete surface declaration raises `GateError` for the required permanent no-verdict exit `3`; it never produces a fabricated finding or a clean result.

These controls are durable content inputs, not just test notes. Each becomes one of: (a) an executable negative-control example adjacent to the future healthcheck gate, (b) a small runnable code example in the `org-healthcheck` skill showing a correct clean/finding/no-verdict invocation, or (c) a narrowly evidenced coordination item when it exposes a separate root cause. The skill must point users to the shared `gate.py` contract and these executable fixtures; it must not teach an alternate shell exit table or prose-only review recipe.

## Review procedure and boundaries

1. Declare the repository, immutable revision/snapshot, review window, and expected surface population before reading conclusions.
2. Collect evidence from authoritative owners, preserving exact paths, run URLs, API reads, or artifact commands. A specific assertion without such a locator is explicitly unverified and cannot support a clean verdict.
3. Compare expected and observed surfaces, then run the meaningful production route where one exists. A discrepancy is a candidate finding, not a rewrite instruction.
4. Emit the per-repository record, shared gate outcome, and separately evidenced architecture disposition. A finding is routed to the repository owning its established root cause; a distinct unestablished cause remains no-verdict rather than being guessed into a board item.

This leg is intentionally an evidence and verdict contract. It does not impose a uniform runtime architecture, add a second coordination scanner, or use a single review to reclassify a whole repository. The current report is likewise not an `FS-GG/.github` cleanliness verdict; it is the executable successor's acceptance boundary.

When kit-delivery history is relevant, `.github#1565`'s corrected measurement is **16 opened / 4 merged**. The superseded `12 opened / 0 merged` figure is not valid evidence.
