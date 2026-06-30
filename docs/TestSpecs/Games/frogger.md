---
title: "Frogger"
slug: frogger
category: games
complexity: simple
genre: "Arcade / fixed-screen crossing"
target_session_minutes: 8
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Frogger

## 1. Overview
You are a frog trying to get home. Between you and safety lies a five-lane highway of
cars and trucks roaring left and right, then a river you cannot swim in — your only
footing is a churn of drifting logs and the backs of turtles (some of which dive and
leave you to drown). The core verb is **hop**: a single grid-snapped jump in one of
four directions, deliberate and discrete. The fun is reading traffic and current as a
shifting puzzle, timing each hop, and the nerve test of riding a log across open water
toward one of five home slots before the clock runs out. It is twitch-pattern-reading
in bite-sized, instantly-readable screens.

## 2. Core Game Loop
**Moment-to-moment:** read lanes → pick a gap/platform → hop one cell → land safely (or
ride) → re-read → repeat upward until a home slot is filled → respawn at start → fill
the next slot.

**Session-level:** title → press Start → play (fill 5 home slots = clear level) → level
advances (faster, denser traffic, more diving turtles) → lose all lives → game over →
show score + high score → restart.

A single "life attempt" loop: spawn at start row with a fresh per-life timer → cross →
either reach home (score, reset position, keep level) or die (lose a life, reset
position, timer resets) → continue while lives remain.

## 3. Controls & Input
Input is **edge-triggered** (one hop per key press; holding does nothing and auto-repeat
is ignored). The frog never moves continuously — every press queues exactly one
grid-snapped hop, and presses during an in-progress hop are dropped (not buffered) in v1.

| Input | Action | Model |
|---|---|---|
| Arrow Up / W | Hop one cell up (toward home) | Edge-triggered (KeyDown only) |
| Arrow Down / S | Hop one cell down | Edge-triggered |
| Arrow Left / A | Hop one cell left | Edge-triggered |
| Arrow Right / D | Hop one cell right | Edge-triggered |
| Enter / Space | Start game / confirm restart (menus only) | Edge-triggered |
| P | Pause / unpause | Edge-triggered |
| Esc | Return to title (from pause) | Edge-triggered |

Notes: no mouse or gamepad required for v1 (gamepad d-pad MAY map 1:1 to the arrows).
A hop into a wall (left edge while on the leftmost column, etc.) is rejected and consumes
no time and triggers no animation.

## 4. Mechanics (detailed)

The playfield is a **grid of cells**, `CellW = 64 px` wide and `CellH = 64 px` tall, on a
1280×720 logical canvas. That gives **20 columns** (1280 / 64) and a vertical stack of
**rows** described in §6. Column index `0..19`; the frog's logical position is a cell
`(col, row)` for snapped state, plus a smooth pixel offset during a hop and a fractional
`subX` while riding a platform.

### 4.1 Hopping movement (grid-snapped)
- A hop moves the frog exactly one cell in the pressed direction.
- A hop is animated over `HopDuration = 0.12 s`: the sprite lerps from the source cell
  center to the destination cell center. Gameplay-wise the frog is considered to occupy
  the **destination** cell for collision the instant the hop resolves (at hop end).
- During a hop the frog ignores new input (no buffering in v1).
- Hopping up from the start row into row 1 begins the crossing. Each **distinct
  furthest row reached** awards points once (see §11).
- Horizontal hops while riding a platform are relative to the frog's current world
  position snapped to the nearest column on landing.

### 4.2 Road section (death on contact)
- Five road lanes (rows 7–11, see §6). Each lane scrolls vehicles in one direction at a
  fixed speed.
- Vehicles are **lethal**: if the frog's hitbox overlaps any vehicle hitbox at any time
  (including while standing still as a car drives into it), the frog dies instantly.
- Lane directions alternate; speeds increase toward the median. Base values (level 1):

| Lane (row) | Vehicle | Direction | Speed (px/s) | Count | Spacing (px) | Hitbox (w×h) |
|---|---|---|---|---|---|---|
| 11 (nearest start) | Car | → (right) | 80 | 3 | ~420 | 56×48 |
| 10 | Truck | ← (left) | 60 | 2 | ~620 | 120×48 |
| 9 | Car | → (right) | 120 | 3 | ~420 | 56×48 |
| 8 | Car (fast) | ← (left) | 160 | 4 | ~320 | 56×48 |
| 7 | Bulldozer/Car | → (right) | 100 | 3 | ~420 | 72×48 |

- Vehicles wrap: when a vehicle's leading edge exits one side it re-enters the opposite
  side at the same speed (toroidal in X), preserving lane spacing.

### 4.3 Median (safe row)
- Row 6 is a **safe median** strip (grass). No hazards. Frog may rest here indefinitely
  (timer still ticks).

### 4.4 River section (death without platform)
- Five river lanes (rows 1–5). The river water is **lethal by default**: if the frog
  ends a hop on a water cell that is **not** covered by a platform, it **drowns**.
- Platforms (logs and turtle groups) drift horizontally. Standing on a platform is the
  only safe footing in the river.
- **Riding (velocity inheritance):** while the frog is standing on a platform and not
  mid-hop, the frog's world X position advances by the platform's velocity each tick
  (`frog.x += platform.vx * dt`). The frog inherits the platform's drift exactly.
- If a ridden platform carries the frog off-screen (frog center leaves `[0, 1280]`), the
  frog dies (carried into the wall / off the world).
- After any hop the frog re-evaluates footing: it snaps its column to the nearest grid
  column but keeps a fractional `subX` for smooth riding; collision uses world pixels.

| River lane (row) | Platform | Direction | Speed (px/s) | Platform length | Gap |
|---|---|---|---|---|---|
| 5 (nearest home) | Log (short) | ← (left) | 70 | 2 cells (128 px) | 1.5 cells |
| 4 | Turtle group (3) | → (right) | 90 | 3 turtles (192 px) | 2 cells |
| 3 | Log (long) | ← (left) | 60 | 4 cells (256 px) | 2 cells |
| 2 | Turtle group (2) | → (right) | 110 | 2 turtles (128 px) | 2.5 cells |
| 1 | Log (medium) | ← (left) | 80 | 3 cells (192 px) | 1.5 cells |

### 4.5 Diving turtles
- Turtle-group platforms cycle through a dive animation. Each group has a phase timer.
- Cycle: **Up (safe) 4.0 s → Sinking 0.6 s → Down (lethal/no footing) 2.0 s → Rising
  0.6 s → Up …** (total ~7.2 s).
- While **Down**, the turtle provides no footing — a frog standing on it drowns the
  instant the group fully submerges (end of Sinking). While **Up** or transitioning
  (Sinking/Rising) it still supports the frog.
- Not all turtle lanes dive: lane 4's 3-turtle group dives; lane 2's 2-turtle group is
  non-diving in level 1 and begins diving from level 2 onward.

### 4.6 Home slots
- Row 0 (home) has **5 home slots** at fixed columns. Between slots are lethal hedges/wall.
- The frog must hop up from row 1 into an **empty** home slot to score a "home."
- Hopping into an **occupied** slot, or into the hedge between slots, is a **death**.
- Filling all 5 slots clears the level (see §11).

### 4.7 Bonus targets
- **Fly:** periodically a fly appears in a random **empty** home slot for `FlyDuration =
  6 s`. Reaching home in that slot while the fly is present awards a bonus.
- **Lady frog:** occasionally a "lady frog" rides a log in the river. If the frog hops
  onto the lady frog's cell, she joins (escort); delivering her home awards a bonus and
  she then disappears. (Implement as a special rider entity in a river lane.)

### 4.8 Per-life timer
- Each life attempt has a countdown timer `LifeTime = 30 s` shown as a draining bar.
- Reaching home resets the timer for the next attempt. Timer reaching 0 = death.
- Remaining time at the moment of reaching home contributes a **time bonus** (see §11).

## 5. Entities / Game Objects

All hitboxes are axis-aligned rectangles; collision is AABB overlap in world pixels.

### 5.1 Frog
- Sprite ~48×48 px, hitbox 40×40 px centered in its cell.
- State machine: `Idle` (snapped, on a cell) → `Hopping` (lerping, `HopDuration`) →
  `Idle`/`Riding`; `Riding` (on platform, inheriting `vx`); `Dying` (death anim 0.5 s)
  → respawn or game over; `Home` (slot filled, brief celebrate 0.4 s) → respawn at start.
- Created at level start and after each death/home at the **start cell** (col 9 or 10,
  start row). Destroyed only conceptually (state reset).

### 5.2 Vehicle
- Properties: lane row, `vx` (signed px/s), width (per table), hitbox h = 48, sprite kind.
- Behavior: constant-velocity scroll with toroidal wrap; always lethal on overlap.

### 5.3 Log (platform)
- Properties: lane row, `vx`, length in px. Hitbox = full length × 56 h.
- Behavior: constant-velocity drift with wrap. Provides footing always.

### 5.4 TurtleGroup (platform)
- Properties: lane row, `vx`, turtle count, `divePhase`, `phaseTimer`, `canDive: bool`.
- Behavior: drift + dive cycle (§4.5). Footing only when not fully `Down`.

### 5.5 HomeSlot
- 5 fixed slots on row 0. Properties: `col`, `occupied: bool`, `hasFly: bool`.
- Created at level start (all empty); `occupied` set true on a successful home.

### 5.6 Bonus riders
- `Fly` (in an empty home slot, timed) and `LadyFrog` (rides a river log, escort target).

F#-flavored sketch:

```fsharp
type Dir = Up | Down | Left | Right

type FrogState =
    | Idle
    | Hopping of fromCell: int * int * toCell: int * int * t: float   // t in [0, HopDuration]
    | Riding  of platformId: int
    | Dying   of t: float
    | Home    of slot: int * t: float

type DivePhase = TUp | TSinking | TDown | TRising

type Platform =
    { Id: int; Row: int; X: float; Vx: float; LengthPx: float
      Kind: PlatformKind }
and PlatformKind =
    | Log
    | Turtles of count: int * canDive: bool * phase: DivePhase * phaseTimer: float

type Vehicle =
    { Row: int; X: float; Vx: float; WidthPx: float; Kind: VehicleKind }
```

## 6. World / Levels / Progression
Logical canvas **1280×720**, grid 64×64 → **20 cols × 11.25 rows**. We use a fixed row
layout (top = home, bottom = start). Top HUD band occupies y ∈ [0, 32); play rows start
below it. Row → y-top mapping (each row 64 px), `rowY(r) = 32 + r*64` is illustrative; the
implementer MAY adjust the HUD band height so 12 bands fit 720 px.

| Row | y band | Contents |
|---|---|---|
| 0 | top | **Home** (5 slots + hedges) |
| 1 | | River lane (log, ←) |
| 2 | | River lane (turtles ×2, →) |
| 3 | | River lane (log long, ←) |
| 4 | | River lane (turtles ×3 diving, →) |
| 5 | | River lane (log short, ←) |
| 6 | | **Median** (safe grass) |
| 7 | | Road lane (car, →) |
| 8 | | Road lane (car fast, ←) |
| 9 | | Road lane (car, →) |
| 10 | | Road lane (truck, ←) |
| 11 | bottom | **Start** (safe grass) + road lane (car, →) overlaps as needed |

Start cell: column 9 or 10 on the start row.

**Difficulty ramp (per level cleared):**
- Vehicle and platform speeds ×`(1 + 0.12 * (level-1))`, capped at ×1.6.
- Diving turtles: lane 2 group begins diving at level ≥ 2; dive `Down` duration grows
  +0.3 s per level (cap 3.5 s).
- `LifeTime` shrinks by 2 s per level, floor 20 s.
- Gaps between platforms tighten by ~8% per level, floor 1 cell.
- Optionally spawn an extra vehicle per road lane at levels 3 and 5.

## 7. State Model (Elmish/MVU)

**Model:**
```fsharp
type Phase = Title | Playing | Paused | GameOver

type Model =
    { Phase: Phase
      Level: int
      Lives: int
      Score: int
      HighScore: int
      Frog: {| Cell: int * int; State: FrogState; WorldX: float |}
      Vehicles: Vehicle list
      Platforms: Platform list
      HomeSlots: {| Col: int; Occupied: bool; HasFly: bool; FlyTimer: float |} []   // length 5
      Lady: {| PlatformId: int; Escorted: bool |} option
      LifeTimer: float            // seconds remaining, counts down from LifeTime
      MaxRowReached: int          // for per-row scoring this attempt
      Rng: System.Random
      ElapsedTotal: float }
```

**Msg:**
```fsharp
type Msg =
    | Tick of dt: float          // seconds, ~1/60
    | Hop of Dir                 // edge-triggered input
    | StartGame
    | TogglePause
    | ToTitle
```

**update — key transitions:**
- `Tick dt`: advance vehicles/platforms (X += Vx*dt, wrap); advance turtle dive phases;
  if `Frog.State = Riding`, `WorldX += platform.Vx*dt`; progress `Hopping`/`Dying`/`Home`
  timers; decrement `LifeTimer`; spawn/expire flies; on `LifeTimer <= 0` → death; run
  collision resolution (vehicle hit, water-without-platform, off-screen ride).
- `Hop dir` (only when `Phase=Playing`, `Frog.State=Idle|Riding`): validate target cell
  (reject walls/edges); start `Hopping`. On hop resolve: re-evaluate footing → set
  `Idle`/`Riding`/`Dying`/`Home`; if reaching a new furthest row, add row points; if into
  home slot, score home + time bonus + fly/lady bonuses, reset frog to start.
- `StartGame`: init level 1, lives 3, score 0, build entities, `Phase=Playing`.
- `TogglePause`: `Playing ↔ Paused`.
- `ToTitle`: from `Paused`/`GameOver` → `Title`.
- Death handler: `Lives-1`; if 0 → `Phase=GameOver` (commit HighScore); else respawn,
  reset `LifeTimer`, `MaxRowReached`.

**view:** pure function `Model -> Scene` describing layers/shapes for Skia to draw
(§8). No mutation, no drawing side-effects in `view`.

**Subscriptions:**
- A 60 FPS animation-frame/timer subscription emitting `Tick dt` (dt in seconds, clamped,
  see §13).
- Keyboard subscription translating edge-triggered KeyDown → `Hop`/`StartGame`/
  `TogglePause`/`ToTitle`.

## 8. Rendering (Skia 2D)
Coordinate system: logical 1280×720, origin top-left, y-down. Backbuffer scaled to window
preserving aspect (letterbox). Redraw the full scene every frame (cheap at this entity
count); no dirty-rect optimization in v1.

**Draw order (back to front):**
1. **Background bands:** road asphalt `#2B2B2B` (rows 7–11), median grass `#3FA34D`
   (row 6), river water `#1E5FA8` (rows 1–5), home strip `#1E5FA8`/`#143A2B` hedges
   (row 0), start grass `#3FA34D` (row 11).
2. **Platforms:** logs rounded rects `#6B4423` with grain lines `#5A3A1E`; turtles as
   circles `#2E8B57` (shell) with head; submerging turtles fade alpha 1→0 during Sinking.
3. **Vehicles:** rounded rects per kind — car `#E03B3B`, fast car `#F4D03F`, truck
   `#B0B0B0` cab + cargo, bulldozer `#E67E22`.
4. **Bonus:** fly = small dark `#222` dot pair in a slot; lady frog = pink `#E08AB0` frog.
5. **Frog:** body `#7CD42B`, eyes `#FFFFFF`/`#000`. Hop = scale-pop (1.0→1.15→1.0).
   Death = brief `#FFFFFF` flash + shrink, then skull/X mark for 0.5 s.
6. **Home fills:** a parked frog `#5FAE1F` drawn in each occupied slot.
7. **HUD overlay** (§9), then screen overlays (title/pause/game-over scrims `#000` @ 60%).

Particles/effects: small splash ring (water death, 6 droplet circles), squash puff on
car death. Font: a clean monospace/bitmap font, white `#FFFFFF`, sizes 16–48 px.

## 9. UI / HUD / Screens
**Screens:**
- **Title:** game name, "PRESS ENTER", high score, simple animated frog hopping in place.
- **Playing:** the board + HUD.
- **Paused:** dimmed board + "PAUSED — P resume, ESC title".
- **Game Over:** "GAME OVER", final score, high score, "PRESS ENTER to restart".

**HUD (top band, y 0–32):**
- Score (left), `SCORE 00000`, 5-digit zero-padded.
- Level (center), `LVL 1`.
- High score (right), `HI 00000`.

**Bottom strip (overlaid on start row):**
- Lives: remaining frog icons (lives-1, since one is active), bottom-left.
- **Timer bar:** bottom-right, draining green→yellow→red bar representing `LifeTimer /
  LifeTime`, ~300 px wide.

## 10. Audio
Checklist (optional in v1):
- Hop: short "boop".
- Plunk (water death): low splash.
- Squash (vehicle death): thud.
- Home reached: chime/jingle.
- Fly/lady bonus: sparkle.
- Timer low (<5 s): ticking.
- Level clear: fanfare.
- Game over: descending tone.
- Music: light looping arcade theme on title/play (toggleable).

## 11. Win / Loss / Scoring

**Scoring:**
| Event | Points |
|---|---|
| Each new furthest row advanced (per attempt) | +10 |
| Reach a home slot | +50 |
| Time bonus on reaching home | +10 per full second remaining on `LifeTimer` |
| Eat the fly (home into slot with fly) | +200 |
| Escort lady frog home | +200 |
| Clear a level (all 5 slots) | +1000 |
| Each spare life remaining at level clear | +100 |

- "New furthest row" awards +10 only the first time the frog reaches that row in the
  current attempt (tracked via `MaxRowReached`).

**Win condition:** Fill all 5 home slots → level clears → next level begins (slots reset,
speeds/density increase). The game has no hard end; it ramps until the player loses.

**Loss condition (death):** any of —
- frog hitbox overlaps a vehicle;
- hop resolves on water without a platform (drown);
- ridden platform carries frog off-screen;
- standing on a fully-submerged diving turtle;
- hop into an occupied home slot or hedge;
- `LifeTimer` reaches 0.

On death: lose 1 life, respawn at start, reset timer & `MaxRowReached`. **Lives = 3**
(one active + the rest). At 0 lives → Game Over.

## 12. Difficulty & Balancing
| Param | Default | Range | Effect |
|---|---|---|---|
| `CellW`/`CellH` | 64 / 64 px | 48–80 | Grid scale |
| `HopDuration` | 0.12 s | 0.06–0.25 | Hop snappiness |
| `LifeTime` | 30 s | 15–45 | Time pressure |
| Road speeds | 60–160 px/s | 40–260 | Traffic difficulty |
| River speeds | 60–110 px/s | 40–180 | Current difficulty |
| Vehicle counts/lane | 2–4 | 1–6 | Traffic density |
| Platform gaps | 1.5–2.5 cells | 1–4 | River traversability |
| Turtle Up duration | 4.0 s | 2–6 | Diving generosity |
| Turtle Down duration | 2.0 s | 1–4 | Diving punishment |
| Fly spawn interval | 12 s | 6–30 | Bonus frequency |
| `FlyDuration` | 6 s | 3–12 | Bonus window |
| Level speed mult/level | +0.12 | 0–0.3 | Ramp steepness |
| Speed mult cap | 1.6 | 1.2–2.5 | Difficulty ceiling |
| Lives | 3 | 1–5 | Run length |

## 13. Technical Notes
- **Entity budget:** ~5 road lanes × ≤4 vehicles + 5 river platforms (+turtles) + 1 frog +
  5 slots + ≤2 bonuses ≈ **40 entities**. Trivially within 60 FPS / 16.7 ms.
- **Timestep:** variable-`dt` `Tick` accepted from the frame subscription, but **clamp
  dt to ≤ 0.05 s** (skip huge frames after pause/stall). For determinism in tests, a
  fixed-step accumulator (1/60 s) MAY drive `Tick` so collision is repeatable.
- **Determinism / RNG:** all randomness (fly slot, fly timing, lady spawns, optional
  level variation) draws from `Model.Rng` seeded explicitly so a given seed → identical
  run; tests pass a fixed seed.
- **Collision order each Tick:** (1) move world; (2) advance turtle phases; (3) if Riding
  update WorldX; (4) resolve hop completion; (5) evaluate death conditions; (6) award
  scoring. Death checks are evaluated once per tick, after movement.
- **Persistence:** high score stored locally (e.g., a `highscore.json` / app-data file),
  loaded on launch, written on Game Over if beaten.
- **Edge cases:** simultaneous valid hop + lethal vehicle on the destination → death;
  hop into wall → no-op (no time/anim); frog riding the seam between two platforms uses
  whichever AABB it overlaps (logs/turtles in one lane don't overlap, so at most one);
  fly expiring exactly as frog lands → bonus granted if `HasFly` true at land-resolve
  tick; turtle submerging exactly when frog lands on it → land first, then re-check next
  tick (one-tick grace).

## 14. Acceptance Criteria (test scenarios)

1. **Grid hop up.** *Given* the frog is `Idle` at start cell (9, 11) *When* `Hop Up` is
   dispatched and `HopDuration` elapses via `Tick`s *Then* the frog is `Idle` at cell
   (9, 10) and score increased by +10 (new furthest row).

2. **Edge-triggered single hop.** *Given* the frog is mid-`Hopping` *When* another `Hop`
   is dispatched before the hop resolves *Then* the second `Hop` is ignored and the frog
   completes exactly one cell of movement.

3. **Wall rejection.** *Given* the frog is `Idle` at column 0 *When* `Hop Left` is
   dispatched *Then* the frog remains at column 0, no animation starts, and `LifeTimer`
   is unchanged for that input.

4. **Vehicle kills.** *Given* the frog is `Idle` on road row 9 and a car's hitbox moves
   to overlap the frog's hitbox *When* the next `Tick` resolves collision *Then* the frog
   enters `Dying`, lives decrement by 1, and it respawns at the start cell with
   `LifeTimer = LifeTime`.

5. **Drown on water.** *Given* the frog hops up into a river row onto a water cell **not**
   covered by any platform *When* the hop resolves *Then* the frog drowns (a splash effect
   plays) and a life is lost.

6. **Safe on log.** *Given* the frog hops onto a log whose AABB covers the destination
   cell *When* the hop resolves *Then* the frog is `Riding` and does not drown.

7. **Velocity inheritance.** *Given* the frog is `Riding` a log with `Vx = -70` px/s
   *When* `1.0 s` of `Tick`s elapse with no input *Then* the frog's `WorldX` decreased by
   ~70 px (within one frame's `dt*Vx`) and the frog is still alive.

8. **Carried off-screen.** *Given* the frog is `Riding` a leftward log near the left edge
   *When* continued `Tick`s carry the frog's center to `WorldX < 0` *Then* the frog dies
   (off-screen).

9. **Diving turtle drowns rider.** *Given* the frog is standing on a diving turtle group
   in phase `TUp` *When* the group completes `TSinking` and enters `TDown` while the frog
   has not hopped off *Then* the frog drowns at the moment it is fully submerged.

10. **Reach home + scoring.** *Given* the frog is `Idle` in river row 1 aligned with an
    empty home slot and `LifeTimer = 12.0` *When* `Hop Up` resolves into the empty slot
    *Then* that slot becomes `Occupied`, score increases by +50 (home) + 120 (time bonus,
    12 full seconds × 10), and the frog respawns at start with a reset timer.

11. **Occupied slot is lethal.** *Given* a home slot is already `Occupied` *When* the frog
    hops into that same slot *Then* the frog dies and loses a life (slot stays occupied).

12. **Fly bonus.** *Given* an empty home slot has `HasFly = true` *When* the frog reaches
    home in that slot *Then* an additional +200 is awarded and the fly is cleared.

13. **Timer death.** *Given* `Playing` with the frog idle *When* enough `Tick`s elapse to
    drive `LifeTimer` from its start to ≤ 0 *Then* the frog dies and a life is lost.

14. **Level clear.** *Given* 4 home slots are occupied *When* the frog fills the 5th slot
    *Then* +1000 (level clear) plus +100 per remaining spare life is awarded, `Level`
    increments, all slots reset to empty, and entity speeds scale by the level multiplier
    (capped at ×1.6).

15. **Game over + high score.** *Given* `Lives = 1` and `Score > HighScore` *When* the
    frog dies *Then* `Phase = GameOver`, `HighScore` is updated to `Score`, and persisted.

16. **Determinism.** *Given* two runs seeded with the same RNG value and an identical
    `Tick`/`Hop` input sequence *Then* fly spawns, lady spawns, and all positions are
    bit-identical between runs.

## 15. Stretch Goals
1. **Input buffering** — queue one hop during an in-progress hop for smoother play.
2. **Crocodiles** — open-jaw croc segment on a log that is lethal (front) but rideable
   (back), plus crocs that surface in empty home slots.
3. **Snake on the median** — a moving lethal hazard on the "safe" median row at higher levels.
4. **Otters / divers** — additional river hazards.
5. **Two-frog co-op** — two frogs, shared lives, split-screen-free same board.
6. **Daily seed challenge** — fixed seed leaderboard using the deterministic RNG.
7. **Variable canvas / responsive grid** — recompute cell size for non-1280×720 windows.
8. **Touch/swipe controls** — directional swipes map to hops for mobile.
