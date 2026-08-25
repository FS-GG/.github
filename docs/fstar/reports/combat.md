# Combat consequence specification

## Result

The combat slice verified successfully with F* 2026.08.23. It is a direct,
executable transcription of the consequence commitment already implemented in
S.I.R., not a replacement implementation of attack resolution.

## Source authority

- Repository: `EHotwagner/S.I.R.`
- Commit: `b24c1bfbaa2b0904468c9490e4704bf1bd0ed6e3`
- Source: `src/SIR.Simulation/CombatRules.fs`, especially
  `bounded100` and `resolveConsequences` at lines 588-658 in that snapshot
- Model: `combat/SIR.CombatConsequences.fst`

`resolveConsequences` first obtains `ExpectedDamage` from the spatial and armor
calculation. It then commits health, wound severity, incapacity, and suppression.
The F* model takes that already-authorized non-negative damage as input and
formalizes only this pure commitment step.

The following mappings are intentional:

| S.I.R. behavior | F* definition |
|---|---|
| clamp health and suppression to 0..100 | refinement `bounded100` and `clamp100` |
| subtract expected damage from health | `resolve` |
| apply non-negative suppression only on contact | `positive_part` and `resolve` |
| damage 25..49 is severity code 0 | `Serious` |
| damage 50 or more is severity code 1 | `Critical` |
| zero remaining health incapacitates | `incapacitated` field of `resolve` |

The named F* cases replace S.I.R.'s integer wound codes inside the proof model.
Extraction retains an ordinary discriminated union, so an adapter can map the
cases back to stable wire codes without making those codes the proof vocabulary.

## Proved properties

F* and its bundled Z3 discharged all verification conditions for:

- `clamp100_is_identity`: values already in range are unchanged;
- `health_never_increases`: consequence resolution cannot heal;
- `suppression_never_decreases`: this transition cannot reduce suppression;
- `zero_damage_commits_no_consequence`: zero damage preserves health and
  suppression, adds no wound, and adds no suppression;
- `lethal_damage_incapacitates`: damage at least equal to current health yields
  incapacity;
- `sublethal_damage_preserves_exact_health`: damage within available health is
  exact subtraction, with no hidden rounding or saturation;
- exact critical, serious, and no-wound threshold partitions.

The `bounded100` refinement also makes out-of-range result health or suppression
unrepresentable at the F* boundary.

## What this does not prove

This project does not prove the spatial trace, armor retention, fixed-point
arithmetic, explanation-tree construction, or the `Result` error path that
precedes commitment. It also does not establish machine-checked equivalence
between the existing hand-written F# function and the F* transcription. That
would require either replacing the production semantic function with extracted
code or checking both implementations against the same generated conformance
vectors/normalized AST semantics.
