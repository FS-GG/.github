# Combat consequence specification

## Result

The combat slice typechecked, its two named examples passed, and Apalache 0.56.1
found no violation of the combined invariant through two symbolic transitions
over the declared bounded domains. An independently selected initializer using
a deliberately wrong critical-wound boundary produced a one-state ITF
counterexample, proving that the verification route can fail for the intended
semantic defect.

This is an executable transcription of the consequence commitment already
implemented in S.I.R., not a replacement implementation of attack resolution.

## Source authority

- Repository: `EHotwagner/S.I.R.`
- Commit: `b24c1bfbaa2b0904468c9490e4704bf1bd0ed6e3`
- Source: `src/SIR.Simulation/CombatRules.fs`, especially `bounded100` and
  `resolveConsequences` at lines 588-658 in that snapshot
- Model: `combat/SIRCombatConsequences.qnt`

`resolveConsequences` first obtains `ExpectedDamage` from spatial and armor
calculation. The Quint model takes that already-authorized non-negative damage
as input and formalizes only the pure commitment step.

| S.I.R. behavior | Quint definition |
|---|---|
| clamp health and suppression to 0..100 | `clamp100` and `boundedResults` |
| subtract expected damage from health | `resolve` |
| apply non-negative suppression only on contact | `positivePart` and `resolve` |
| damage 25..49 is severity code 0 | `Wounded(Serious)` |
| damage 50 or more is severity code 1 | `Wounded(Critical)` |
| zero remaining health incapacitates | `incapacitated` field of `resolve` |

Named variants replace S.I.R.'s integer wound codes inside the model. A future
FS-GG lowering could map these variants into a stable domain AST and generate
the existing wire-code adapter without making the codes authoring vocabulary.

## Checked properties

The combined invariant covers:

- health and total suppression remain in `0..100`;
- consequence resolution cannot heal;
- this transition cannot reduce suppression;
- zero damage preserves health and suppression and creates no wound;
- damage at least equal to health incapacitates;
- sublethal damage preserves exact subtraction; and
- critical, serious, and no-wound thresholds form the intended partition.

The model chooses health and suppression from `0..100`, suppression delta from
`-20..120`, and damage from `0..120`. These bounds make the symbolic claim
explicit. Unlike F*'s refinement proof, this result does not prove the
properties for every mathematical integer.

## Negative control

`resolveBroken` changes the critical condition from `damage >= 50` to
`damage > 50`. The `brokenInit` action fixes damage at exactly 50. Verification
must exit non-zero, report a counterexample, and emit a non-empty ITF trace.
`verify.sh` treats absence of that exact failure shape as a failed experiment.

## What this does not prove

The model does not prove spatial tracing, armor retention, fixed-point damage,
explanation construction, or the preceding error path. It also does not prove
the current F# implementation equivalent to this transcription. That requires
production to consume semantics lowered from the same normalized model or a
model-based adapter to replay generated traces against the implementation.
