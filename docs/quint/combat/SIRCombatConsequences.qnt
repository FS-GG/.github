// A bounded executable specification of the consequence commitment in
// SIR.Simulation.CombatRules.resolveConsequences at S.I.R. commit
// b24c1bfbaa2b0904468c9490e4704bf1bd0ed6e3.
module SIRCombatConsequences {
  type WoundSeverity = Serious | Critical
  type Wound = NoWound | Wounded(WoundSeverity)

  type Consequence = {
    damage: int,
    remainingHealth: int,
    wound: Wound,
    incapacitated: bool,
    appliedSuppression: int,
    totalSuppression: int,
  }

  pure def clamp100(value: int): int =
    if (value < 0) 0 else if (value > 100) 100 else value

  pure def positivePart(value: int): int =
    if (value > 0) value else 0

  pure def woundFor(damage: int): Wound =
    if (damage >= 50) Wounded(Critical)
    else if (damage >= 25) Wounded(Serious)
    else NoWound

  pure def resolve(
    currentHealth: int,
    currentSuppression: int,
    suppressionDelta: int,
    damage: int,
  ): Consequence = {
    val health = clamp100(currentHealth - damage)
    val applied = if (damage > 0) positivePart(suppressionDelta) else 0
    val suppression = clamp100(currentSuppression + applied)
    {
      damage: damage,
      remainingHealth: health,
      wound: woundFor(damage),
      incapacitated: health == 0,
      appliedSuppression: applied,
      totalSuppression: suppression,
    }
  }

  // Deliberately wrong threshold used only by the negative-control initializer.
  pure def resolveBroken(
    currentHealth: int,
    currentSuppression: int,
    suppressionDelta: int,
    damage: int,
  ): Consequence = {
    val correct = resolve(currentHealth, currentSuppression, suppressionDelta, damage)
    {
      damage: correct.damage,
      remainingHealth: correct.remainingHealth,
      wound: if (damage > 50) Wounded(Critical)
        else if (damage >= 25) Wounded(Serious)
        else NoWound,
      incapacitated: correct.incapacitated,
      appliedSuppression: correct.appliedSuppression,
      totalSuppression: correct.totalSuppression,
    }
  }

  var currentHealth: int
  var currentSuppression: int
  var suppressionDelta: int
  var damage: int
  var result: Consequence

  action chooseScenario = {
    nondet nextHealth = 0.to(100).oneOf()
    nondet nextSuppression = 0.to(100).oneOf()
    nondet nextDelta = (-20).to(120).oneOf()
    nondet nextDamage = 0.to(120).oneOf()
    all {
      currentHealth' = nextHealth,
      currentSuppression' = nextSuppression,
      suppressionDelta' = nextDelta,
      damage' = nextDamage,
      result' = resolve(nextHealth, nextSuppression, nextDelta, nextDamage),
    }
  }

  action init = chooseScenario
  action step = chooseScenario

  val boundedResults = all {
    result.remainingHealth >= 0,
    result.remainingHealth <= 100,
    result.totalSuppression >= 0,
    result.totalSuppression <= 100,
  }

  val healthNeverIncreases = result.remainingHealth <= currentHealth
  val suppressionNeverDecreases = result.totalSuppression >= currentSuppression

  val zeroDamageCommitsNoConsequence = damage != 0 or all {
    result.remainingHealth == currentHealth,
    result.wound == NoWound,
    result.appliedSuppression == 0,
    result.totalSuppression == currentSuppression,
    result.incapacitated == (currentHealth == 0),
  }

  val lethalDamageIncapacitates =
    damage < currentHealth or result.incapacitated

  val sublethalDamagePreservesExactHealth =
    damage > currentHealth or result.remainingHealth == currentHealth - damage

  val woundThresholdsAreExact = all {
    damage < 50 or result.wound == Wounded(Critical),
    damage < 25 or damage >= 50 or result.wound == Wounded(Serious),
    damage >= 25 or result.wound == NoWound,
  }

  val invariant = all {
    boundedResults,
    healthNeverIncreases,
    suppressionNeverDecreases,
    zeroDamageCommitsNoConsequence,
    lethalDamageIncapacitates,
    sublethalDamagePreservesExactHealth,
    woundThresholdsAreExact,
  }

  run criticalExample = assert(
    resolve(80, 10, 7, 50) == {
      damage: 50,
      remainingHealth: 30,
      wound: Wounded(Critical),
      incapacitated: false,
      appliedSuppression: 7,
      totalSuppression: 17,
    }
  )

  run zeroDamageExample = assert(resolve(80, 10, 7, 0).wound == NoWound)

  // This action must violate woundThresholdsAreExact and is invoked separately.
  action brokenInit = all {
    currentHealth' = 80,
    currentSuppression' = 10,
    suppressionDelta' = 7,
    damage' = 50,
    result' = resolveBroken(80, 10, 7, 50),
  }

  action brokenStep = brokenInit
}
