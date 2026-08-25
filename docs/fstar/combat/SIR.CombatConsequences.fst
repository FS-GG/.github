module SIR.CombatConsequences

(**
  A proof-oriented transcription of the consequence commitment in
  SIR.Simulation.CombatRules.resolveConsequences at S.I.R. commit
  b24c1bfbaa2b0904468c9490e4704bf1bd0ed6e3.

  Spatial tracing and fixed-point expected-damage evaluation are deliberately
  outside this slice. The input [damage] is their already-authorized outcome.
*)

type bounded100 = x:int { 0 <= x /\ x <= 100 }

type wound_severity =
  | Serious
  | Critical

type consequence = {
  damage: nat;
  remaining_health: bounded100;
  wound: option wound_severity;
  incapacitated: bool;
  applied_suppression: nat;
  total_suppression: bounded100
}

let clamp100 (value:int) : Tot bounded100 =
  if value < 0 then 0
  else if value > 100 then 100
  else value

let positive_part (value:int) : Tot nat =
  if value > 0 then value else 0

let wound_for (damage:nat) : Tot (option wound_severity) =
  if damage >= 50 then Some Critical
  else if damage >= 25 then Some Serious
  else None

let resolve
  (current_health:bounded100)
  (current_suppression:bounded100)
  (suppression_delta:int)
  (damage:nat)
  : Tot consequence
  =
  let health = clamp100 (current_health - damage) in
  let applied = if damage > 0 then positive_part suppression_delta else 0 in
  let suppression = clamp100 (current_suppression + applied) in
  {
    damage = damage;
    remaining_health = health;
    wound = wound_for damage;
    incapacitated = (health = 0);
    applied_suppression = applied;
    total_suppression = suppression
  }

let clamp100_is_identity (value:bounded100)
  : Lemma (clamp100 value == value)
  = ()

let health_never_increases
  (health:bounded100)
  (suppression:bounded100)
  (delta:int)
  (damage:nat)
  : Lemma ((resolve health suppression delta damage).remaining_health <= health)
  = ()

let suppression_never_decreases
  (health:bounded100)
  (suppression:bounded100)
  (delta:int)
  (damage:nat)
  : Lemma ((resolve health suppression delta damage).total_suppression >= suppression)
  = ()

let zero_damage_commits_no_consequence
  (health:bounded100)
  (suppression:bounded100)
  (delta:int)
  : Lemma
      (let result = resolve health suppression delta 0 in
       result.remaining_health == health /\
       result.wound == None /\
       result.applied_suppression == 0 /\
       result.total_suppression == suppression /\
       result.incapacitated == (health = 0))
  = ()

let lethal_damage_incapacitates
  (health:bounded100)
  (suppression:bounded100)
  (delta:int)
  (damage:nat)
  : Lemma
      (requires damage >= health)
      (ensures (resolve health suppression delta damage).incapacitated)
  = ()

let sublethal_damage_preserves_exact_health
  (health:bounded100)
  (suppression:bounded100)
  (delta:int)
  (damage:nat)
  : Lemma
      (requires damage <= health)
      (ensures (resolve health suppression delta damage).remaining_health == health - damage)
  = ()

let critical_threshold_is_exact (damage:nat)
  : Lemma
      (requires damage >= 50)
      (ensures wound_for damage == Some Critical)
  = ()

let serious_threshold_is_exact (damage:nat)
  : Lemma
      (requires damage >= 25 /\ damage < 50)
      (ensures wound_for damage == Some Serious)
  = ()

let below_wound_threshold_is_none (damage:nat)
  : Lemma
      (requires damage < 25)
      (ensures wound_for damage == None)
  = ()
