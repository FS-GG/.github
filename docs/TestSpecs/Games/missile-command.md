---
title: "Missile Command"
slug: missile-command
category: games
complexity: simple
genre: "fixed-shooter / arcade defense"
target_session_minutes: 8
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Missile Command

## 1. Overview
You are the last line of air defense. Streaks of light fall from the top of the
screen toward six cities and three missile batteries below. With the mouse you
aim, and with a click you launch a counter-missile that arcs to the crosshair and
detonates into an expanding fireball — anything caught in that blast is vaporized.
The core verb is **place a defensive explosion ahead of the threat**: you are not
shooting bullets at targets, you are painting kill-zones in the sky and timing
detonations so cascading fireballs sweep waves of warheads out of existence. It is
fun because it is a pure spatial-prediction puzzle under escalating time pressure,
with the bittersweet arcade arc of inevitable loss — you cannot win, only outscore
how long you lasted.

## 2. Core Game Loop
**Moment-to-moment:** track incoming missiles → move crosshair to an intercept point →
click to fire the nearest battery with ammo → blast expands and chains kills →
manage scarce ammo across three batteries → survive the wave.

**Wave loop:** wave starts → fixed number of warheads rain down over the wave window →
last threat resolves → **bonus tally** (surviving cities + unused ammo) → batteries
reload → difficulty escalates → next wave.

**Session loop:** title → play → (wave → bonus → wave …) → all six cities destroyed →
**game over** → final score + high-score check → restart.

## 3. Controls & Input
Mouse is the **primary** input; keyboard provides battery selection and meta actions.

| Input | Action | Model |
|---|---|---|
| Mouse move | Move crosshair (aim point) | Continuous; clamped to playfield |
| Left mouse button | Fire counter-missile from the **auto-selected** battery toward crosshair | Edge-triggered (on press) |
| `A` key | Force-fire **Left** battery (Alpha) | Edge-triggered |
| `S` key | Force-fire **Center** battery (Bravo) | Edge-triggered |
| `D` key | Force-fire **Right** battery (Charlie) | Edge-triggered |
| `P` / `Esc` | Toggle pause | Edge-triggered |
| `Enter` / Left click | Start game (title) / Restart (game-over) | Edge-triggered |

**Auto-selection rule (left click):** fire the battery that (a) has ammo > 0 and (b) is
nearest in horizontal distance to the crosshair X. If the nearest has no ammo, fall
through to the next-nearest with ammo. If no battery has ammo, the click is ignored
(play a dry-click cue). Force-fire keys (`A`/`S`/`D`) ignore the auto rule and fire the
named battery if it has ammo.

## 4. Mechanics (detailed)

### 4.1 Coordinate system & ground
Logical playfield **1280×720**, origin top-left, +X right, +Y down. The **ground line**
is at `y = 680`. Cities and batteries sit on the ground; sky is `y < 680`.

### 4.2 Crosshair
A reticle that follows the mouse, clamped to `x ∈ [0,1280]`, `y ∈ [0, 660]` (cannot aim
into the ground). Rendered as a 24×24 px "+" with a center dot.

### 4.3 Counter-missiles (player)
- Spawn from the firing battery's muzzle at its tower top.
- Travel in a **straight line** toward the click target at **fixed speed 900 px/s**.
- Leave a thin trail (polyline of recent positions, last 0.4 s).
- On reaching the target point (within 6 px) they **detonate** into a blast.
- Counter-missiles are not destructible and do not collide with enemy missiles in
  flight — only the **blast** kills. Travel time to target = `distance / 900`.

### 4.4 Blast (fireball)
- Spawns at the detonation point as an expanding circle.
- **Grows** from radius 0 to **max radius 60 px** at **160 px/s** (≈0.375 s to grow),
  **holds** at max for **0.25 s**, then **shrinks** back to 0 at 160 px/s.
- Total blast lifetime ≈ **1.0 s**.
- **Kill rule:** any enemy missile whose head point is within the current blast radius
  is destroyed instantly and awards points. MIRV split is suppressed if killed.
- **Chaining:** when an enemy warhead is destroyed *by reaching/striking a target it is
  not* — kills do not chain by themselves, but overlapping player blasts and the wide
  radius let one detonation clear clusters. (No secondary explosions from enemy deaths
  in v1; see Stretch Goals.)
- Multiple blasts may be active simultaneously (cap 12, see §13).

### 4.5 Incoming missiles (ICBMs)
- Spawn at a random X along the top edge (`y = 0`), choose a random ground target from
  the set of still-alive cities and batteries.
- Travel in a **straight line** from spawn to target at **wave-scaled speed** (§6).
- Speed table baseline **wave 1 = 70 px/s**, **+12 px/s per wave**, cap **220 px/s**.
- Leave a colored trail (polyline) from origin to current head.
- **Impact:** when the head reaches its target's ground point (`y ≥ 680`), it destroys
  that target (city or battery) and is removed. No points to player.
- **Killed by blast:** removed and awards **25 pts × wave multiplier** (§11).

### 4.6 MIRV split (multiple warheads)
- Starting **wave 3**, a fraction of incoming missiles are **MIRVs**.
- A MIRV splits **once** at a random altitude `y ∈ [220, 380]`, spawning **2–3** child
  missiles, each retargeting a random alive ground target, inheriting parent speed.
- Split probability per incoming = **min(0.10 + 0.04·(wave−3), 0.45)**.
- Children are normal missiles (cannot split again). Killing the parent before split
  prevents the children.

### 4.7 Smart bombs / planes
- Starting **wave 5**, occasional **bombers** (planes) cross horizontally.
- Plane enters at `y ∈ [80, 260]`, flies left→right or right→left at **130 px/s**,
  and **drops a bomb** every **1.5 s** while on screen (bomb = a normal incoming
  missile spawned at the plane's position aimed at a random alive target).
- Plane has a small body hitbox (40×14 px); a blast touching it destroys it for
  **100 pts × wave multiplier**.
- Smart bomb (rare, **wave 7+**): a warhead that **evades** — if a blast center is
  within 90 px and the smart bomb's path, it makes one lateral dodge of up to 40 px.
  Each smart bomb may dodge at most **twice**. Worth **125 pts × wave multiplier**.
- At most **1 plane on screen** at a time; spawn check every 4 s with probability
  `min(0.25 + 0.05·(wave−5), 0.6)`.

### 4.8 Batteries & ammo
- **3 batteries**: Alpha (x≈160), Bravo (x≈640), Charlie (x≈1120), towers on the ground.
- Each battery starts each wave with **10 counter-missiles**.
- Firing decrements that battery's ammo by 1; ammo does not regenerate mid-wave.
- A battery whose city/tower is destroyed by an incoming impact is **out** — it cannot
  fire for the rest of the game and shows as rubble.
- Batteries **reload to 10** at the start of each wave (only surviving batteries).

### 4.9 Cities
- **6 cities** placed in two groups of three between the batteries:
  group L between Alpha and Bravo (x ≈ 280, 400, 520), group R between Bravo and Charlie
  (x ≈ 760, 880, 1000). Each city is a static 60×40 px structure on the ground.
- A city hit by an incoming missile is destroyed (becomes rubble) and cannot be
  re-targeted. Cities do not regenerate during a game (no rebuild in v1; see Stretch).
- **Game over** when all 6 cities are destroyed (batteries surviving does not matter).

## 5. Entities / Game Objects

### 5.1 City
- Size 60×40 px, hitpoints 1, static. States: `Alive | Rubble`.
- Created at game start (6). Destroyed by incoming impact.

### 5.2 Battery
- Tower ~30×50 px, holds `Ammo: int`. States: `Online | Destroyed`.
- Created at game start (3). Reloaded per wave. Destroyed by incoming impact.

### 5.3 CounterMissile (player projectile)
- Head point + target point, speed 900 px/s, trail. State: `Flying | Detonated(removed)`.
- Created on fire; removed when it reaches target (spawns a Blast).

### 5.4 Blast
- Center, current radius, phase. States: `Growing | Holding | Shrinking | Done`.
- Created by CounterMissile detonation; removed when radius returns to 0.

### 5.5 IncomingMissile (ICBM)
- Head point, velocity vector, target ground point, `Kind`, optional `SplitAtY`,
  `DodgesLeft`. States: `Falling | Killed(scored) | Impacted`.
- Created by spawner / MIRV split / plane bomb-drop.

### 5.6 Plane (bomber)
- Body 40×14 px, horizontal velocity, `DropTimer`. State: `Flying | Destroyed`.

F#-flavored sketch:
```fsharp
type Vec2 = { X: float; Y: float }

type CityState = Alive | Rubble
type BatteryState = Online | Destroyed

type City    = { Pos: Vec2; State: CityState }
type Battery = { Pos: Vec2; Muzzle: Vec2; Ammo: int; State: BatteryState }

type IncomingKind = Standard | Mirv | SmartBomb | PlaneBomb

type Incoming =
    { Id: int
      Head: Vec2
      Vel: Vec2                 // px/s, points toward target
      Target: Vec2              // ground impact point
      Kind: IncomingKind
      SplitAtY: float option    // for Mirv: altitude to split
      DodgesLeft: int           // for SmartBomb
      Trail: Vec2 list }

type CounterMissile =
    { Id: int; Head: Vec2; Target: Vec2; Trail: Vec2 list }

type BlastPhase = Growing | Holding | Shrinking
type Blast = { Center: Vec2; Radius: float; Phase: BlastPhase; HoldLeft: float }

type Plane =
    { Id: int; Pos: Vec2; Vel: Vec2; DropTimer: float; Dir: int }
```

## 6. World / Levels / Progression
- Playfield **1280×720**; ground at `y = 680`.
- A **wave** = a fixed budget of incoming missiles released over a window, then a lull.

| Param | Wave 1 | Per-wave change | Cap |
|---|---|---|---|
| Incoming count | 8 | +2 / wave | 32 |
| Incoming speed | 70 px/s | +12 / wave | 220 px/s |
| Spawn interval | 1.4 s | −0.06 / wave | 0.35 s |
| MIRV chance | 0 (until W3) | +0.04 / wave from W3 | 0.45 |
| Plane chance/check | 0 (until W5) | +0.05 / wave from W5 | 0.60 |
| Wave multiplier | ×1 | +×1 every 2 waves | ×6 |

- Difficulty ramps via more/faster missiles, tighter spawn cadence, then MIRVs (W3),
  then planes (W5), then smart bombs (W7).
- **Score multiplier** for kills and bonuses increases every 2 waves: waves 1–2 ×1,
  3–4 ×2, 5–6 ×3, … capped ×6.

## 7. State Model (Elmish/MVU)

### Model
```fsharp
type Phase = Title | Playing | WaveBonus | Paused | GameOver

type Model =
    { Phase: Phase
      Wave: int
      Score: int
      HighScore: int
      Multiplier: int
      Cities: City list            // 6
      Batteries: Battery list      // 3 (index 0=Alpha,1=Bravo,2=Charlie)
      Incoming: Incoming list
      Counters: CounterMissile list
      Blasts: Blast list
      Planes: Plane list
      Crosshair: Vec2
      // wave scheduling
      ToSpawn: int                 // remaining incoming to release this wave
      SpawnTimer: float            // s until next spawn
      SpawnInterval: float
      IncomingSpeed: float
      MirvChance: float
      PlaneChance: float
      // bonus tally animation
      BonusTimer: float
      NextId: int
      Rng: System.Random
      Now: float }                 // accumulated seconds (for trails/animation)
```

### Msg
```fsharp
type Battery3 = Alpha | Bravo | Charlie

type Msg =
    | Tick of float                 // dt seconds
    | MouseMoved of Vec2
    | Fire of Vec2                  // left-click target -> auto-select battery
    | FireFrom of Battery3 * Vec2   // A/S/D force-fire
    | TogglePause
    | StartGame
    | Restart
```

### update (key cases)
- `Tick dt` (Playing): advance `Now += dt`; integrate counter-missiles (move toward
  target, detonate→spawn Blast on arrival); advance blasts (grow/hold/shrink phases);
  integrate incoming (move; MIRV split when `Head.Y ≥ SplitAtY`; smart-bomb dodge
  check; on `Head.Y ≥ 680` impact target → destroy city/battery); integrate planes
  (move; drop bombs on `DropTimer`); **collision pass**: for each incoming/plane within
  any blast radius → remove + add score; run spawner (`SpawnTimer`, `ToSpawn`); detect
  **wave clear** (`ToSpawn=0 && Incoming=[] && Counters in-flight resolved`) → `WaveBonus`;
  detect **all cities rubble** → `GameOver`.
- `MouseMoved p` → clamp and set `Crosshair`.
- `Fire target` → resolve auto-select battery; if ammo>0 spawn CounterMissile, ammo−1.
- `FireFrom (b,target)` → fire named battery if ammo>0.
- `WaveBonus Tick` → run `BonusTimer`; award city bonus + ammo bonus; when done,
  `Wave+1`, recompute wave params, reload batteries, → `Playing`.
- `TogglePause` → `Playing ↔ Paused`.
- `StartGame` / `Restart` → fresh `init` model (preserve `HighScore`).

### view
Pure: reads `Model` and emits draw commands (no mutation). Renders ground, cities
(alive/rubble), batteries (with ammo count), incoming trails+heads, counter trails+heads,
blasts (radial fill), planes, crosshair, HUD, and the active screen overlay
(title/pause/bonus/game-over). Skia performs the actual drawing.

### Subscriptions
- **Tick:** request-animation-frame / 60 FPS timer → `Tick dt` with `dt` in **seconds**
  (clamped to ≤ 0.05 s to avoid tunneling on stalls).
- **Input:** mouse-move → `MouseMoved`; mouse-down → `Fire`; key-down → `FireFrom` /
  `TogglePause` / `StartGame` / `Restart`.

## 8. Rendering (Skia 2D)
Coordinate space = logical 1280×720, scaled to the surface. Clear each frame (full
redraw; cheap at these entity counts).

**Draw order (back → front):**
1. **Sky** background: vertical gradient `#0B1026` (top) → `#1B2A4A` (horizon).
2. **Ground** band `y ≥ 680`: filled `#2E7D32` (alive tint) line/strip.
3. **Cities:** alive = `#4FC3F7` block with 2–3 "building" rects; rubble = `#5D4037`
   low mound.
4. **Batteries:** tower `#90A4AE`; if destroyed, `#37474F` rubble. Ammo count drawn
   above each as small dots/number `#ECEFF1`.
5. **Incoming trails & heads:** trail polyline `#FF7043` 2 px; head dot 4 px `#FFAB91`.
   MIRV head tinted `#FFD54F`; smart bomb `#E040FB`.
6. **Plane:** body `#B0BEC5` with two engine dots.
7. **Counter-missile trails & heads:** trail `#80DEEA` 2 px; head 3 px `#E0F7FA`.
8. **Blasts:** filled circle, radial-ish flicker — alternate fill `#FFF176`/`#FF8A65`
   each ~50 ms, alpha 0.85; thin ring outline `#FFFFFF`.
9. **Crosshair:** "+" reticle `#FFFFFF` with center dot, 24×24 px.
10. **HUD & overlays:** see §9. Font: a clean monospace (e.g. system mono) for score;
    title uses a bold sans at 64 px.

**Effects:** counter/incoming trails are short polylines of recent head positions.
Blast flicker via two-color alternation. No camera (fixed view). Redraw strategy: clear
+ full repaint every frame.

## 9. UI / HUD / Screens

**HUD (during Playing), drawn over the scene:**
- **Score:** top-center, `SCORE 0001234`, 28 px mono `#ECEFF1`.
- **High score:** top-center small, under score, `HI 0009999`.
- **Wave:** top-left, `WAVE 3`, 24 px.
- **Multiplier:** top-right, `x2`, 24 px `#FFD54F`.
- **Ammo:** per battery, dots above each tower (filled = available, hollow = spent).

**Screens:**
- **Title:** big "MISSILE COMMAND", subtitle "Click / Enter to defend", controls hint
  (mouse aim, A/S/D batteries), high score.
- **Playing:** scene + HUD.
- **Paused:** dim overlay (black @ 0.5 alpha) + "PAUSED — P to resume".
- **WaveBonus:** dim overlay + tally lines animating in:
  `CITIES SURVIVED  n × 100 × mult`, `UNUSED AMMO  k × 25 × mult`, then `BONUS  +XXXX`.
- **Game Over:** "GAME OVER", final score, NEW HIGH SCORE banner if beaten,
  "Click / Enter to restart".

## 10. Audio (checklist, optional v1)
- Counter-missile launch → short "thunk/whoosh".
- Blast detonation → boom (pitch slightly randomized).
- Incoming impact on city/battery → heavy explosion + low rumble.
- Enemy killed by blast → light "pop".
- MIRV split → "crackle".
- Plane on screen → faint engine drone (loop while present).
- Dry click (no ammo) → soft "click" / negative cue.
- Wave start → ascending chime; wave bonus tally → per-line ticks; game over → descending tone.
- Music: optional low ambient pad during play; tension rises with wave (out of scope v1).

## 11. Win / Loss / Scoring

**Kill scoring (× wave multiplier):**
- Standard incoming: **25**.
- MIRV parent (killed before split): **25** (children prevented = good defensive value).
- MIRV child: **25**.
- Smart bomb: **125**.
- Plane (bomber): **100**.

**End-of-wave bonus (× wave multiplier):**
- **Surviving city bonus:** `100 × (cities still Alive)`.
- **Unused ammo bonus:** `25 × (sum of remaining ammo across online batteries)`.
  (Computed before the reload for the next wave.)

**Win condition:** none — the game is an endless arcade survival; "winning" = highest
score. (Optional: define a milestone like reaching Wave 10 for tests.)

**Loss condition:** all **6 cities** are `Rubble` → `GameOver`. Losing all batteries but
keeping a city is **not** game over (you simply cannot shoot — death spiral, but not an
instant loss).

**Lives/continues:** none. Single run, then restart. High score persists (§13).

## 12. Difficulty & Balancing

| Param | Default | Range | Effect |
|---|---|---|---|
| `groundY` | 680 | 600–700 | Vertical play space |
| `counterSpeed` | 900 px/s | 600–1400 | How fast counters reach target (reaction window) |
| `blastMaxRadius` | 60 px | 40–110 | Kill-zone size; bigger = easier |
| `blastGrowSpeed` | 160 px/s | 100–300 | Time to full radius |
| `blastHold` | 0.25 s | 0.0–0.6 | Dwell time at max radius |
| `ammoPerBattery` | 10 | 5–20 | Shots available per wave |
| `baseIncomingCount` | 8 | 4–16 | Wave-1 threat volume |
| `incomingCountStep` | 2 | 1–4 | Volume ramp |
| `baseIncomingSpeed` | 70 px/s | 40–120 | Wave-1 difficulty |
| `incomingSpeedStep` | 12 | 5–25 | Speed ramp |
| `incomingSpeedCap` | 220 px/s | 150–300 | Max fall speed |
| `baseSpawnInterval` | 1.4 s | 0.8–2.0 | Wave-1 cadence |
| `spawnIntervalStep` | 0.06 s | 0.02–0.15 | Cadence tightening |
| `minSpawnInterval` | 0.35 s | 0.2–0.6 | Cadence floor |
| `mirvStartWave` | 3 | 1–6 | When MIRVs appear |
| `planeStartWave` | 5 | 3–8 | When bombers appear |
| `smartBombStartWave` | 7 | 5–10 | When evaders appear |
| `multiplierEveryWaves` | 2 | 1–4 | Score multiplier cadence |

All tunables live in a single `Config` record so balance is data-driven and testable.

## 13. Technical Notes
- **Performance budget:** target **60 FPS / 16.7 ms** frame. Worst-case entity counts:
  ≤ 32 incoming (+ up to ~16 MIRV children mid-wave), ≤ 30 counter-missiles, **≤ 12
  blasts**, ≤ 1 plane. Trails capped to ~12 points each. Well within Skia 2D budget.
- **Timestep:** variable `dt` from the tick, **clamped to 0.05 s** to prevent tunneling
  (fast missiles skipping the blast on a stall). Integration is `pos += vel·dt`.
  Optionally substep movement when `dt` large, but clamp is sufficient at these speeds.
- **Collision:** point-in-circle for incoming head vs. blast radius (cheap O(blasts ×
  incoming), ≤ 12×48 per frame). Counter-missile arrival = distance-to-target ≤ 6 px.
- **Determinism / RNG:** single seeded `System.Random` in the model; all spawn X,
  target choice, MIRV split altitude/count, plane spawn drawn from it. Seed injectable
  for tests so scenarios are reproducible.
- **Persistence:** high score saved to local storage / a small file; loaded at startup,
  written on game over if beaten. If unavailable, high score is session-only.
- **Edge cases:** click with no ammo anywhere → ignored (+ dry cue); all batteries
  destroyed but ≥1 city alive → keep playing, no firing possible; MIRV reaching split
  altitude with no alive targets → children pick from batteries, else fly straight to
  ground; blast cap reached → ignore further fires (cosmetic dry cue); incoming spawned
  exactly on a target column still travels full vertical distance.

## 14. Acceptance Criteria (test scenarios)

1. **Aim follows mouse.** *Given* the game is Playing, *when* the mouse moves to
   (500, 300), *then* `Crosshair = (500, 300)`; *when* it moves to (700, 700), *then*
   `Crosshair.Y` is clamped to ≤ 660.

2. **Auto-select fires nearest battery with ammo.** *Given* all three batteries have
   ammo and the crosshair X is 300, *when* `Fire` is issued, *then* a CounterMissile
   spawns from **Alpha** (nearest to X=300) and Alpha's ammo decreases by 1.

3. **Fall-through when nearest is empty.** *Given* Bravo (x=640) has 0 ammo and Alpha
   has ammo, *when* `Fire` is issued at crosshair X=620, *then* the counter spawns from
   the nearest battery **with ammo** (Alpha), not Bravo.

4. **Counter-missile detonates at target.** *Given* a counter fired toward (640, 360)
   at 900 px/s from muzzle (640, 640), *when* enough `Tick`s elapse for it to travel the
   280 px (≈0.31 s), *then* it is removed and a Blast spawns centered at (640, 360).

5. **Blast destroys an incoming and scores.** *Given* an active blast at (640,360) with
   radius 50 and an incoming whose head is at (660,360) (distance 20 ≤ 50), *when* the
   collision pass runs on `Tick`, *then* the incoming is removed and score increases by
   `25 × multiplier`.

6. **Blast lifecycle.** *Given* a fresh blast, *when* time advances, *then* radius grows
   0→60 at 160 px/s, holds at 60 for 0.25 s, shrinks 60→0 at 160 px/s, and the blast is
   removed; total lifetime ≈ 1.0 s (±0.05 s).

7. **Incoming impact destroys a city.** *Given* an incoming targeting city #3 (alive),
   *when* its head reaches `y ≥ 680` over city #3, *then* city #3 becomes `Rubble`, the
   incoming is removed, and the player score is unchanged.

8. **Game over only when all cities gone.** *Given* 1 city alive and all batteries
   destroyed, *when* `Tick` runs, *then* phase stays `Playing`; *when* that last city is
   impacted, *then* phase becomes `GameOver`.

9. **MIRV split.** *Given* wave 4 and a Mirv incoming with `SplitAtY = 300`, *when* its
   head crosses y=300, *then* 2–3 child Standard incomings spawn at that point, each with
   an alive target, and the original MIRV is replaced (children cannot split again).

10. **MIRV killed before split spawns no children.** *Given* the same MIRV, *when* a
    blast destroys it at y=250 (above split altitude), *then* no children spawn and score
    increases by `25 × multiplier`.

11. **Wave bonus tally.** *Given* a wave ends with 4 cities alive and 7 unused ammo total
    on online batteries at multiplier ×2, *when* the bonus resolves, *then* score
    increases by `(100×4 + 25×7) × 2 = (400 + 175) × 2 = 1150`.

12. **Wave escalation.** *Given* wave 1 (8 incoming, speed 70, interval 1.4), *when* the
    game advances to wave 2, *then* incoming count = 10, speed = 82 px/s, spawn interval
    = 1.34 s, and surviving batteries reload to 10.

13. **Dry click ignored.** *Given* every battery has 0 ammo, *when* `Fire` is issued,
    *then* no CounterMissile spawns, no ammo changes, and (if audio) a dry-click cue is
    requested.

14. **Force-fire key targets named battery.** *Given* Charlie has ammo and crosshair X is
    200 (nearest=Alpha), *when* `FireFrom(Charlie, target)` is issued, *then* the counter
    spawns from **Charlie** regardless of crosshair proximity, and Charlie's ammo −1.

15. **Pause freezes simulation.** *Given* Playing with incoming in flight, *when*
    `TogglePause` is issued, *then* phase = `Paused` and subsequent `Tick`s do not move
    any entity; *when* `TogglePause` again, simulation resumes from the same positions.

16. **Determinism.** *Given* two games initialized with the same RNG seed and the same
    ordered input sequence, *when* both run the same number of ticks, *then* their
    `Score`, `Wave`, and entity positions are identical.

## 15. Stretch Goals
1. **Chain reactions:** enemy missiles destroyed by a blast spawn their own small
   secondary blast, enabling combo cascades (classic arcade feel).
2. **City rebuild bonus:** award a rebuilt city every N points (e.g. every 10,000).
3. **Combo/streak scoring:** multiple kills from one blast escalate per-kill value.
4. **Persistent leaderboard** with initials entry on new high score.
5. **Screen shake & particle debris** on impacts (juice).
6. **Defcon-style color ramp:** background shifts redder as cities fall / wave climbs.
7. **Difficulty selects** (rookie/veteran) preloading different `Config` records.
8. **Touch/trackpad mode:** tap-to-fire mapping for non-mouse devices.
9. **Per-battery missile-speed differences** (a "fast" central battery) for tactical depth.
