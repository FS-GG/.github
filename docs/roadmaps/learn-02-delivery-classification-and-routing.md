# LEARN-02.1: Delivery hardness classification and model/effort routing (WIP)

Status: **work in progress, not reviewed.** Delivery FS-GG/.github#5249 under Outcome #5248. This draft
records the design direction reached so far; the calibration, recording tool and Atlas record are not yet
delivered. Nothing here is adopted dispatch policy.

## Inputs reused

- LEARN roadmap §2.1 profiles and §3 task-dimension families
  (`docs/roadmaps/2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md`).
- LEARN-01.2 `learn-task-snapshot` fact (item, snapshotId, rubricVersion, snapshotDigest, capturedAt), already
  admitted by the receiver, so recording needs no receiver change (`src/FS.GG.Coord.Core/TelemetryStore.fs`).
- #5171/#5178 Claude subagent dispatch attribution: `expected-dispatch`, `invocation-lineage` and
  `runtime-admission` rows joined to per-response usage by the brief marker `fsgg-dispatch: item=… role=…`.

## Rubric v1 (draft)

Eight dimensions, each scored 0–3 or `unknown`, by fixed rules over the issue body and Done when as they stood
before the first dispatch (reconstructed from `userContentEdits`). An optional LLM-assisted scorer uses a fixed
prompt; each score records provenance (`rule` or `model:<id>`) and rubric version.

| Dimension | 0 | 3 |
|---|---|---|
| D1 scope | one repo, few files | several repos, contract boundaries |
| D2 novelty / research | known pattern | new design, ADR or research |
| D3 cross-cutting invariants | local | concurrency, lifecycle, crash safety, sizing |
| D4 safety / data criticality | none | deletes, publishes, upgrades, credentials, data |
| D5 numeric / determinism | none | Fable lockstep, fixed point, replay |
| D6 UI / browser matrix | none | multi-backend rendering, browser matrix |
| D7 review-round risk | few, clear criteria | many criteria, public contract, hostile states |
| D8 required evidence | source tests | live, installed or native gates |

## Routing policy v1 (draft)

| Class | Worker tier | Effort | Critic tier | Review depth |
|---|---|---|---|---|
| Safety floor (D4 ≥ 2, or any delete/publish/upgrade/credential/data class) | best | high | best | critic + delta review |
| High (H ≥ 13, or D3/D5 ≥ 2) | best | high | best | critic + delta review |
| Medium (7 ≤ H ≤ 12) | normal | high | best | critic + delta review |
| Low (H ≤ 6) | normal | medium | normal | single critic (owner review for inert prose) |

Safety-floor classes never route below best tier, including during exploration. Root or a human may override;
the override and its reason are recorded. Analysis can only propose a policy version; a reviewed PR changes it.

## Evaluation (direction, per Root's 2026-11-02 guidance)

- Outcomes on decision-relevant scales: log tokens (geometric mean) with robust estimators, review rounds and
  blocking findings; not the arithmetic mean of raw tokens.
- Effects that matter for routing: ≥30% token change (|Δ log| ≥ 0.357) or ≥0.5 review rounds.
- Variance reduction: CUPED / regression adjustment on the pre-dispatch hardness scores; stratify by safety and
  hardness class; paired comparisons where possible.
- Decisions: Bayesian posterior probability and expected loss, or an always-valid sequential test
  (Johari, Pekelis and Walsh, arXiv:1512.04922); contextual Thompson-sampling bandit (ADR-0098 N20) with bounded
  exploration inside each safety class, logged propensities, and IPW / doubly robust off-policy evaluation
  (Dudík, Langford and Li, arXiv:1103.4601; Li et al., arXiv:1003.0146).
- Power: n per arm ≈ 2(z₀.₉₇₅ + z₀.₈)² σ² / δ². With σ = 0.8 and δ = 0.357 that is about 79; with CUPED R² = 0.5,
  about 40. **To do:** verify σ and R² against the calibration data.
- Why not LEARN-01's 6,908 per arm: that figure targets a 10% change in the arithmetic mean with CV 2.0 under a
  fixed-horizon independent-items test (2 × 7.85 × 4 / 0.01 × 1.1). Reconciling the two needs either that
  estimand (capacity planning) or an explicit tail guardrail beside the log-scale routing estimand.

## Remaining work

1. Retrospective calibration: score recent Project 3 Deliveries as if pre-dispatch and join them to review
   rounds, REQUEST_CHANGES, blocking findings, repair commits and attributed tokens; aggregates only. Anchors:
   Game#797 (6 rounds), Game#794 (5), SDD#1158 (2), .github#5242 (2), Templates#845 (2), Algorithms#4/#5 (1).
2. Recording tool: classification and profile marker comment plus a `learn-task-snapshot` observation submitted
   through `telemetry workspace submit`.
3. Atlas record (framework audience).
4. Literature section: RouteLLM (arXiv:2406.18665), FrugalGPT (arXiv:2305.05176), METR task horizon
   (arXiv:2503.14499), CUPED (Deng et al., WSDM 2013).
