---
title: "4D Tactical Skirmish: Three Design Directions — Version 2"
category: Design
categoryindex: 4
index: 39
description: Revised player-facing playtest proposals for Firelanes, Commitment and Pressure in a four-dimensional tactical skirmish.
status: accepted-for-bounded-prototyping
document-type: game-design-proposal
last-updated: 2026-09-29
---

# 4D Tactical Skirmish: Three Design Directions — Version 2

Date: 2026-09-28

Status: Accepted on 2026-09-29 for bounded Commitment/Pressure prototyping under FOURD-01; neither a selected final ruleset nor a claim of balance or completed playtesting.

Scope: This document owns game design and player-facing interaction. The separately owned [implementation amendment](https://github.com/FS-GG/FS.GG.FourD/blob/540f41baa0c7a9cb9fe6005d836c5290ee8cc408/docs/FOURD-01.design-v2.md) supplies the authorized bounded source plan; implementation and technical architecture are outside this document's scope. The plan is a published branch draft at the exact commit linked above; it will land with substantive implementation rather than a planning-only PR.

This companion to the [FOURD-01 design and roadmap](../2026-09-08-152551-4d-grid-tactics-algorithms-design-roadmap.md) uses X, Y and Z for the three base axes and H for height. The existing algorithm roadmap calls those axes x, y and w, with z for height. This is a naming map for comparing proposals, not a change to the adopted coordinate contract or current source rules. The alternatives below are playtest candidates; their reaction, damage and information rules do not retroactively describe the current implementation.

**Adoption boundary.** The user selected incorporation into the ongoing programme on 2026-09-29. Compare Commitment and Pressure first, using the same geometry, information rules, maps and presentation. Firelanes remains an outline. The delivered `fourd-tactics-v1` encounter remains the default; this amendment authorizes distinct opt-in prototypes, not a silent replacement or save conversion. Exact source, rules/replay identities, resolved implementation choices and qualification gates belong to the owning implementation amendment. The original local proposal and its accompanying uncommitted roadmap edit were preserved unchanged.

**Full V2 acceptance scope — 2026-09-29.** The user's [acceptance amendment](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#full-v2-acceptance-amendment--2026-09-29) removes human participation as a gate: unfamiliar-player recruitment, six consenting participants, four completed paired comparisons and participant-derived design selection are follow-up evaluation work. Full V2 acceptance uses automated technical comparison and qualification of actual downloaded runtime and retained saves. Both candidates may remain opt-in without a selected winner. Automated results do not establish human preference or usability; any later participant study retains its consent requirements.

**Recommendation: compare Commitment and Pressure first; retain Firelanes as the more demanding alternative.** Commitment tests whether preserving readiness creates the best decisions. Pressure tests whether predictable combat makes the unfamiliar space easier to reason about. Firelanes tests the appeal of persistent defensive threats.

All three retain whole-side IGOUGO: one side completes its active turn, then the other. Reactions are automatic consequences of preparations made beforehand. This is a proposed interpretation of IGOUGO, not a claim that alternating individual activations would be unsuitable.

The established games supply useful mechanisms. The combinations, 4D adaptations, interface rules and numerical values below are original proposals that need testing.

**What changes from version 1**

| Earlier assumption | Revised decision | Reason |
|---|---|---|
| Fog of war added around a tabletop-style system | Observation and loss of contact are part of the shared foundation | Ordinary concealment should emerge from the world |
| Reaction choices made at each interruption | A small set of firing policies selected during your turn | Preserve preparation while avoiding repeated defender prompts |
| Visible enemies reveal their exact remaining AP | Enemy action reserves and firing policies stay private | Seeing a soldier does not reveal its whole internal state |
| Movement and new contact left underspecified | Explicit exposure, reaction and stopping sequence | Surprise must arise from commitment, with understandable consequences |
| Two designs share fairly lethal two-die attacks | One bounded damage roll per attack | Reduce compounded uncertainty while learning 4D positioning |
| Pressure persists until a paid recovery | Partial automatic recovery plus a useful paid recovery action | Reduce repeated denial of an entire turn |
| Reserved actions declared the clear favorite | Conditional recommendation, compared against deterministic Pressure | Computer interaction and information rules can change the ranking |

The intended complexity lies in observation, positioning and commitment. Automatic bookkeeping supports these decisions; it does not justify adding ammunition, fatigue, morale, cooldowns and injuries all at once.

---

## Shared foundation: the battlefield and its geometry

These are initial testing defaults, not irrevocable product requirements.

| Element | Starting rule |
|---|---|
| Forces | Five identical units per side; two wounds each; removed at zero remaining wounds |
| Coordinates | Three base axes X, Y, Z, with height H at each base position |
| Walkable terrain | One walkable surface height per base cell in the initial maps |
| Terrain forms | Axis-aligned solid blocks and ramps rising along exactly one base axis |
| Ramp orientations | +X, -X, +Y, -Y, +Z, -Z; one height level per uphill step |
| Movement | Up to three orthogonal base-cell steps per Move; supported, unoccupied cells only |
| Height changes | Follow ramps; no jumping, free climbing, falling or flying in the baseline |
| Weapon range | Eight units of straight-line distance across X, Y, Z and H, with equal axis scales |
| Observation range | No separate distance cap on the small initial maps; terrain and facing limit observation |
| Facing | Six base-axis directions; no independent upward/downward facing |
| Turn structure | Whole-side IGOUGO; one active unit acts at a time |
| Collision and sight | Units occupy cells but do not themselves block sight or provide cover |
| Damage | No critical hits, armor saves or extra flank/surprise damage |

A Move may follow a bent route but preserves the unit's facing along it. If the declared endpoint is reached, the unit may adopt one declared final facing. Intermediate rotations are not free. A move must change position; a zero-distance move is a Turn.

Facing defines a broad forward half of the base space. For example, a +X-facing unit observes positions with X equal to or greater than its own, subject to terrain. The boundary plane is included. Height affects occlusion and range, but not this facing test; directly higher or lower positions remain in the observation sector. This is an intentionally generous starting sector. Narrower sectors should be tested only if facing proves too weak.

Changing projection never changes facing, geometry, cover or knowledge. A ramp has one physical uphill direction even if its projection appears diagonal.

### Sight, cover and firing are separate checks

Use one stable logical silhouette for every unit. For the first geometric test, a unit is two height levels tall, with an upper reference point 1.5 levels above its supporting surface and a lower point 0.5 levels above it. This lets one-level blocks provide partial cover without introducing another terrain shape.

- Geometric sight requires a clear straight segment between the two upper reference points. Without facing, this test is reciprocal.
- Observation additionally requires the target to be in the observer's facing sector.
- A visible target is protected if terrain blocks the segment from the attacker's upper point to the target's lower point; otherwise it is exposed.
- Firing additionally requires weapon range, the relevant action or readiness, and personal observation by the shooter.
- Terrain boundaries block a segment that touches them; a grazing corner grants no sight. Unit reference points stand clear of the supporting terrain.

These are deliberately abstract geometric rules, not true-model visibility. Keep them identical across the three designs. Their purpose is predictable outcomes across every projection; the precise silhouette dimensions remain tuning parameters.

Team reports do not grant shooting through walls or outside the shooter's facing. Cover belongs to an attacker–target relationship, not to a cell universally. A position may protect against one enemy and expose the unit to another.

## Shared foundation: information

Start with a fully known, static terrain map and unknown enemy deployment inside publicly marked deployment regions. This isolates uncertainty about opponents from uncertainty about the shape of an unfamiliar space. Unexplored terrain and destruction can be later variants.

Observation is automatic and deterministic. There are no baseline spotting rolls, camouflage markers or Discover actions. All friendly units share their current observations immediately.

| Information | Player presentation |
|---|---|
| Own units | Exact position, facing, wounds, resources, policies and status |
| Currently observed enemies | Exact position, facing, remaining wounds and pressure if applicable |
| Enemy AP, orders spent out of sight, reaction policy | Private; never automatically revealed by selecting a visible enemy |
| Previously observed enemies | Last-seen position, facing and condition, explicitly dated and stale |
| Unobserved enemy movement or recovery | No updates to remembered information |
| Terrain and objectives | Known from the beginning |
| Scores and turn count | Public, including any information their changes imply |
| A unit outside the selected slice but currently observed | Still listed as a live contact with a way to locate it |

Exact enemy wounds and pressure are chosen gameplay abstractions. They make observed combat understandable; they are not claims about realistic perception. Enemy readiness remains private to preserve uncertainty about commitment.

When a remembered position is observed empty, mark that location as cleared; do not imply that the enemy was destroyed or reveal where it went. The event history can retain the earlier sighting.

Firing does not globally reveal a unit. Normal sight and facing still govern who sees the shooter. A unit hit from an unobserved direction receives an incoming-fire indication along the attack direction, shared with its team, but no exact source position, identity or resource count. A missed unseen shot gives no additional location report in the baseline. There is no separate sound propagation system yet.

An attempted move into an unseen enemy-occupied cell stops before entry and establishes contact for both involved units regardless of facing. It grants no free turn or attack. The attempted step consumes the movement action, including any unused distance. Previews must not reveal the hidden occupant in advance.

Infinity's Hidden Deployment illustrates a tabletop procedure for maintaining private positions. Here, ordinary units can remain unseen through normal geometry, and can lose contact again after being observed. This replaces the administrative need for a special hidden-state procedure without copying its rules. [Infinity: Hidden Deployment State](https://infinitythewiki.com/Hidden_Deployment_State)

### The interface's information contract

The interface must distinguish three things: currently observed, remembered, and outside the current view. It must also distinguish "no known threat" from "safe."

Before commitment, show:

- Route, movement cost and final facing.
- Terrain obstruction and range for selected known targets.
- Protection or exposure relative to each relevant observed enemy.
- Friendly observation and firing sectors, including coverage outside the current slice.
- Own reaction policy and remaining capacity.
- Why an action is unavailable, without disclosing hidden enemies.

Do not show enemy readiness icons, unseen blockers, hidden reaction opportunities or target indicators derived from unavailable information. A geometric firing lane may be previewed into unobserved space because the terrain is known; that preview says nothing about whether an enemy occupies it.

During resolution, group simultaneous events and provide a short explanation, such as "incoming fire from an unobserved direction" or "no response: outside facing." Never identify a hidden shooter merely to explain a rule. Event history contains only the information available to that side at the time.

Camera changes and inspection are always free. They reveal no new battlefield information. Player attention spent switching views should not become a substitute for a soldier's observation limits.

XCOM 2 demonstrates the value of movement boundaries and attack previews; Into the Breach demonstrates that a computer can deliberately expose consequences as well as conceal opponents. These proposals combine clear known consequences with uncertain enemy location and commitment. [XCOM 2 manual](https://www.feralinteractive.com/en/manuals/xcom2/latest/steam/), [Into the Breach](https://www.subsetgames.com/itb.html)

## Shared foundation: commitment and automatic reactions

Every unit has one simple firing policy:

| Policy | Automatic behavior when otherwise eligible |
|---|---|
| Guard | Fire at the first eligible opportunity against the acting enemy |
| Ambush | Fire at the first eligible opportunity at which that enemy is exposed to this shooter |
| Hold | Do not react |

Policies can be changed freely during the owning side's turn, between completed actions. They are locked during the opponent's turn. Setting a policy does not rotate a unit, grant sight, create readiness or trigger an attack. All units begin on Guard, adjustable during deployment.

This is the entire initial policy vocabulary. There are no target scripts, health thresholds, manual defender confirmation dialogs or reaction dodges. In particular, Hold does not grant an emergency exception when attacked: the value and risk of that commitment are intentional.

Reaction eligibility always requires personal observation and range. A unit can only react to the currently acting enemy. Resource limits differ by design.

### Resolution sequence

1. The active player commits an action and pays its cost. A movement route and final facing are declared together.
2. Movement resolves as discrete cell-to-cell steps. Eligible defenders can react at the starting cell and at each entered cell, taking the first opportunity allowed by their policy. Intermediate route cells count; the unit cannot skip them by selecting a distant endpoint. There are no midway-between-cell firing positions in this baseline.
3. All reactions triggered at the same position resolve together. Their resources are committed before results are applied, so redundant shots can consume several defenders' readiness.
4. Resolve those consequences before further movement. No player can stop just before an already-triggered shot.
5. If the mover survives, continue the route unless a contact stop applies. At a reached endpoint, apply the declared final facing and update observation.
6. For an Attack, declare its target before resolving eligible reactions. That attack and reactions triggered by its declaration resolve simultaneously. A unit killed in this exchange still delivers its already-committed shot.
7. For other stationary actions, eligible reactions resolve before the action's effect. A killed unit does not complete that action. Update observation again after a surviving Turn.

Only active actions trigger reactions. There are no reactions to reactions, damage, automatic recovery, camera changes or the appearance of a reactive shooter. If a new shooter becomes observable during a reaction, it can be dealt with by a later active action.

### Contact stops

If a moving unit personally acquires an enemy it did not observe at the start of that Move, or its team acquires a previously unobserved enemy during the Move, resolution pauses after any reactions at that cell. The active player may continue the declared route or stop there.

Stopping before the declared endpoint ends the Move, consumes its full cost and forfeits unused distance and the uncompleted final facing change. If the endpoint has been reached, complete the declared final facing normally. Continuing preserves the original route and facing declaration; rerouting requires another Move. A composite order may still have a remaining Attack, as specified in Firelanes.

This gives the player a response to discovery without free retreat, free scanning or cancellation of exposure. An incoming-fire indication also permits this stop after the triggered exchange. A forced stop due to collision likewise grants no final rotation.

Uncommitted plans can be edited freely. Committed actions cannot be undone after gaining information or resolving combat. Save/reload policy for a future solo campaign is a separate product choice.

---

## Design 1 — Firelanes: persistent defensive threat

**The central decision:** Where can I establish or break a field of fire?

The inspiration is Infinity's order pool and recurring reactive threat, combined with a small action vocabulary. Infinity allows successive enemy orders to create reaction opportunities and excludes reactions to reactions. The automatic policies and sequential movement exposure here are adaptations; this is not Infinity's complete simultaneous order-resolution system. [Infinity ARO rules](https://infinitythewiki.com/ARO:_Automatic_Reaction_Order)

### Active turn

Receive six orders. Each unit may receive at most two orders per turn. Unspent orders expire.

| Order | Effect |
|---|---|
| Advance | Move up to three steps, then optionally Attack |
| Reposition | Move up to six steps; no Attack |
| Fire | Attack without moving |
| Turn | Change facing without moving |

Advance uses one continuous route and permits one final facing choice at the reached endpoint. If stopped early on contact, the move ends without the final rotation, but its optional Attack remains available if the unit survives and personally observes a valid target. Choose the Attack target after movement, using information legitimately acquired.

Mission-specific Interact orders can be added later; the common occupancy mission below needs none.

### Combat

Each ordinary Attack rolls one d6:

- Exposed target: one wound on 3+.
- Protected target: one wound on 5+.

Each reaction rolls one d6:

- Exposed target: one wound on 4+.
- Protected target: one wound on 6.

There is no damage roll and no extra flank or surprise bonus. Two wounds remove a fresh unit, so one attack cannot eliminate it. Multiple overlapping reactions can still do so.

### Reactions

Every living unit can fire once per enemy order, following its locked policy. It needs no saved action and refreshes that opportunity for the next enemy order.

A defender that fires during an Advance's movement cannot fire again at that order's Attack. A defender that held an Ambush shot while the mover was protected may fire later in the same order if the target becomes exposed.

### Example

A defender on Guard covers the direct route. Your scout starts from protection, draws that defender's one shot for the order and survives. It advances and attacks. However, a second hidden defender can still fire when the scout enters its sector. The first shot never certifies that the whole route is safe.

Alternatively, a flanker approaches outside both defenders' facing. It earns an engagement without their reactions, provided no other observer covers it.

### Why it fits the computer

The computer handles repeated geometric checks and resolves automatic policies without asking the defender at every step. Hidden defenders make reconnaissance and overlapping observation valuable. The player commands the active operation while the opponent's prior preparations remain consequential.

### Main risk

Persistent reactions can produce immobility, while a Move-plus-Attack order may also favor powerful concentrated assaults. Large numbers of overlapping hidden shots can make a correct-looking move fatal.

Use terrain that breaks long lanes and objectives accessible by genuinely different base-axis approaches. If progress still depends on sacrificing scouts, this direction has failed its first test. Do not solve that by adding a large library of exceptions.

**Choose this for:** the strongest emphasis on defended approaches, coordinated support and dangerous movement.

---

## Design 2 — Commitment: preserve the ability to respond

**The central decision:** How much action should I spend now, and how much response capacity should I retain?

The inspiration is readiness-dependent reactions in BLKOUT, BPRE's commitment to orientation and interruption timing, and Combat Zone's connection between acting and reacting. The three-point reserve below is an original abstraction. BLKOUT's alternating activations and Combat Zone's wound/action system are not imported. [BLKOUT overview](https://www.blkoutgame.com/pages/how-to-play), [BPRE FAQ](https://www.blackpowderredearth.com/28mm_faq.php), [Combat Zone overview](https://monsterfightclub.com/products/combat-zone)

### Active turn

Each surviving unit refreshes to three AP at the beginning of its own turn. Old AP are replaced, never accumulated.

Activate each unit once, in any order. Complete its activation before selecting another unit. Each action costs one AP:

- Move up to three steps, with the normal final facing choice.
- Turn in place.
- Attack; at most one ordinary Attack during this activation.

End an activation whenever desired. Remaining AP are reserved for the opponent's turn. A unit need not spend AP at all. Before ending the side's turn, review reserves, facing and firing policies.

Both sides begin with three AP per unit, so the side moving second can react before its first active turn.

### Combat and reactions

Use Firelanes' one-die combat probabilities. An automatic reaction costs one reserved AP and follows the unit's locked policy.

Each defender may react at most once during each enemy unit's entire activation, even if that enemy takes several actions. It may react again against a different enemy activation if AP remain.

There is no reaction movement in this revision. Removing Evade avoids hidden destination choices, additional collision cases and a defender prompt at every attack. A future evasion rule must justify those costs rather than being assumed necessary.

Zero AP prevents reactions but does not prevent observation or reporting. AP refresh and firing policy are private enemy information. Observed actions can support deductions, but the interface does not turn incomplete observations into an exact reserve display.

### Example

Your unit moves and attacks, retaining one AP. The Move already permitted a final facing choice: it did not also require a Turn.

That unit can now give one automatic response during the enemy turn. Another unit spends Move, Attack and a subsequent Turn to watch a different approach; it has no AP left to respond.

An enemy exposes a protected unit first. Guard might spend the reserve on that low-probability shot. Ambush preserves it, but allows the protected enemy to act unanswered. This replaces repeated "shoot or wait?" dialogs with a prior tactical commitment.

### Why it fits the computer

Individual reserves are tracked automatically, but the player manages only one resource. The interface can show a clear own-side summary: actions spent, responses remaining, watched sector and policy. Hidden reserves make baiting an inference rather than exact arithmetic on an enemy's visible tokens.

### Main risk

Automatic policies may feel too crude when a reserve is precious. Excessive automatic overkill or repeated baiting can make the player feel that the units waste resources.

Judge whether Guard/Ambush/Hold is enough before adding a policy editor. If richer manual reactions are desired, test them as a separate synchronous mode; acknowledge that this changes pacing and asynchronous suitability.

**Choose this for:** a compact action economy built around initiative, preparation and imperfect knowledge of readiness.

---

## Design 3 — Pressure: create a temporary opening

**The central decision:** Which defender must I disrupt so another unit can move?

The inspiration is Spectre's use of stress and suppression, and the limited command capacity discussed in Chain of Command. Deterministic resolution also draws on the clarity demonstrated by Into the Breach. Four fixed commands, two pressure levels and the recovery timing here are original design choices, not copied rules. [Spectre revision notes](https://www.spectreminiatures.com/blogs/news/spectre-operations-revised-edition), [Chain of Command designer discussion](https://toofatlardies.co.uk/forum/viewtopic.php?t=57), [Into the Breach](https://www.subsetgames.com/itb.html)

### Active turn

Receive four commands for the side. Each unit may receive at most two commands and make at most one ordinary Attack per turn. Commands can be distributed in any sequence; unspent commands expire.

| Command | Effect |
|---|---|
| Move | Up to three steps, with normal final facing |
| Turn | Change facing in place |
| Fire | Make an ordinary Attack |
| Watch | Prepare one automatic reaction shot under the selected firing policy |
| Recover | Clear all pressure; optionally withdraw one legal step while keeping facing |

Watch lasts until spent, canceled, the unit receives another command, or the beginning of its next own turn. Only a unit with zero pressure may enter Watch. Taking any pressure cancels an unspent Watch.

Both sides may begin with one prepared Watch per unit. This prevents a free opening rush before the second side has had a turn.

### Deterministic combat

An ordinary or reaction shot that satisfies sight, facing and range has the same result:

| Target relative to shooter | Result |
|---|---|
| Exposed | One wound and one pressure |
| Protected | One pressure, no wound |
| Unobserved or geometrically blocked | No direct attack permitted |

A Watch shot uses the shared policy and timing rules and is then spent. There is no free return shot without Watch.

If Fire and an eligible Watch response occur in the same exchange, both resolve. Incoming pressure does not cancel a response already committed to that exchange. If a Watch shot hits a moving unit earlier in its route, those effects apply before any later Fire command.

### Pressure and recovery

Pressure is capped at two:

| Pressure | Effect |
|---|---|
| 0 | Normal capability |
| 1 | Cannot Watch or react; can move, turn, fire and control an objective |
| 2 | Also cannot Fire or control an objective; can still move, turn and Recover |

At the beginning of a side's turn, first score objectives using the current pressure levels, then reduce every surviving friendly unit's pressure by one automatically. Thus a unit at two pressure loses that scoring opportunity but begins its action phase at one and can act.

Recover clears the remaining pressure and may include one withdrawal step. Resolve reactions against Recover before clearing pressure; if the unit survives, clear all pressure then perform its optional step. Other eligible watchers can react along that step, potentially adding pressure again. A unit can always Recover without moving.

This provides useful choices without promising immunity to continued fire. It prevents pressure accumulated during the enemy turn from automatically removing the entire next active turn. Repeated suppression can still deny scoring, and that must be tested.

### Example

A protected defender has Watch. Your supporting unit, also protected, Fires at it. Both exchange shots and gain one pressure; the defender's Watch is spent.

A second command moves your flanker through the now-unwatched lane. A third command could Fire on the defender from an exposed direction, causing a wound and a second pressure.

On its owner's next turn, the defender cannot score before rallying. It then drops to one pressure and can move or Fire without first spending a command on recovery. Recover is needed if restoring Watch is the priority.

### Why it fits the computer

The interface can state exact consequences against known targets. Unknown positions and opposing choices supply uncertainty. Pressure tracking and automatic recovery require no tokens or reminders, while the visible states remain few.

### Main risk

Perfect protection against wounds may make protected exchanges repetitive. There must be routes that genuinely change the attacker's cover relationship. If all useful positions can remain protected from every reachable approach, this combat model will stall.

Do not immediately add random chip damage: first determine whether the terrain supplies the maneuver this design needs. If it does and stalls persist, reconsider absolute cover protection as a separate variant.

**Choose this for:** the most abstract, predictable battle of suppression, displacement and coordinated advances.

---

## A common mission and initial test conditions

Use the same mission to compare the directions:

- Three publicly marked objective cells, initially unoccupied.
- A living unit controls the objective cell it occupies; no remote control radius and no Interact cost.
- Beginning with each side's second turn, score one point per controlled objective at the start of that side's turn.
- In Pressure, a unit at two pressure does not control its objective at that scoring check.
- Complete six rounds, with one turn per side per round, then compare scores. Equal scores are a draw.
- Elimination ends the game immediately; simultaneous elimination is a draw.
- No victory points for kills. Alternate first player and deployment region when comparing results.

Scores are intentionally public intelligence: gaining a point reveals that a side controls an objective, even if its occupier is unseen. The score does not reveal facing, readiness or the identity of the occupier.

Use known deployment regions out of initial firing contact. Deployment positions remain secret until normal observation reveals them. Evaluate initial observation before the first turn; this alone triggers no reactions.

Begin with short, static maps containing a direct route, an alternate base-axis approach and a ramp-based height approach. Check that each route actually changes sight or cover relationships. Merely drawing the same firing lane in a different projection does not create another tactical approach.

Do not assume identical maps balance all three action economies. Use shared maps to expose differences first, then tune ranges, commands and objective spacing with that evidence.

### PvP and PvE boundaries

The initial comparison uses symmetric forces and rules, whether controlled by humans or an AI. An AI should choose actions and policies using its side's available observations; hidden player positions should not secretly drive tactical targeting.

A future PvE mode may use unequal forces, distinctive enemy behavior and objectives such as extraction. That is encounter design, not an inherent property of digital play.

Campaign persistence, injuries, progression, ammunition, destructible terrain, smoke, hearing and specialist stealth are deferred. Each could be valuable, but including them now would obscure whether the core information and positioning game works.

Automatic defender policies support both live and asynchronous play. The active player's contact stop is part of its own turn and does not require an opponent to answer.

---

## Comparison and recommendation

| Question | Firelanes | Commitment | Pressure |
|---|---|---|---|
| What sustains defense? | A reaction opportunity for every enemy order | AP saved from the defender's previous turn | One prepared Watch |
| What opens an approach? | Bypass facing, draw a shot within an order, or defeat the defender | Exploit spending, policy or exhausted reserves | Apply pressure, then move through the opening |
| Main hidden commitment | Firing policy and unseen supporting units | Firing policy, saved AP and unseen support | Unobserved Watch preparation and support |
| Combat uncertainty | One bounded wound roll | One bounded wound roll | Deterministic against known targets |
| Persistent state beyond wounds | Policy; reaction use within current order | Policy; AP; reaction use within current enemy activation | Policy; Watch; zero to two pressure |
| Main interaction risk | Too many automatic exchanges | Valuable reserves spent on poor triggers | Repetitive suppression and recovery |
| Main spatial demand | Understand overlapping fields of fire | Balance progress and sector coverage | Find approaches that defeat cover |
| Best reason to choose it | Persistent danger | Resource commitment | Readable maneuver |

**My revised recommendation is to test Commitment and Pressure before choosing a foundation.** Commitment remains the best candidate for the desired preparation-versus-action trade-off. Pressure is now an equally important comparison because deterministic consequences may make four-dimensional tactics substantially easier to understand. Firelanes is the stronger choice only if recurring defensive fire proves enjoyable rather than restrictive.

Keep the shared observation, cover and interface rules stable during this comparison. Otherwise a better presentation may be mistaken for a better combat system.

### What to learn from the first tests

| Test | Evidence to collect | Failure signal |
|---|---|---|
| Projection comprehension | Can players identify the same unit, facing and attack relationship in another view? | Mistakes mostly come from losing the coordinate context |
| Knowledge clarity | Can players distinguish live contacts, memories and uncovered approaches? | An off-screen contact is mistaken for an unseen one |
| Commitment clarity | Can players predict the sequence of movement, contact and reactions? | "I would not have ordered that if I knew the rule" |
| Flanking value | How often does a changed approach remove a reaction or defeat cover? | Maneuver rarely matters, or unseen attacks decide everything |
| Reaction autonomy | Would players have preferred a different response under their chosen policy? | Frequent regret caused by policy limitations rather than their own preparation |
| Pacing | Time deciding, changing views and watching resolution | Interface handling dominates tactical decisions |
| Initiative balance | Repeat with sides and first player swapped | Consistent advantage unrelated to decisions |
| Suppression resilience | Can pressured units make useful decisions next turn? | Repeated recovery or score denial leaves no practical counterplay |

These are proposed evaluations, not completed validation. A clean ruleset is the starting hypothesis; the test is whether players can understand and deliberately create tactical advantages.

### Version history

- Version 1: tabletop-inspired directions centered on continuous reactions, reserved actions and suppression.
- 2026-09-29 adoption: Commitment/Pressure bounded prototypes selected; v1 delivery and pending player evidence preserved; implementation governed by the separate owning amendment.
- Version 2: computer-first information contract, explicit geometry defaults, automatic firing policies, contact timing, hidden readiness, reduced damage variance, revised pressure recovery, a shared test mission and a conditional recommendation.
