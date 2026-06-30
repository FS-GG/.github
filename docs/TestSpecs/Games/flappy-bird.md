---
title: "Flappy Bird"
slug: flappy-bird
category: games
complexity: simple
genre: "Endless side-scrolling arcade"
target_session_minutes: 3
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Flappy Bird

## 1. Overview
You are a tubby bird that cannot stop falling. One button — a flap — gives you a single
upward beat of the wings, and gravity immediately starts dragging you back down. The
world scrolls left at a constant clip, hurling an endless procession of green pipes at
you, each with a narrow gap punched somewhere in it. The fantasy is pure twitch mastery:
threading a too-small gap, again and again, on nothing but timing and nerve. It is fun
because the control is brutally simple, the failure is instant and unambiguous, and the
score is a public dare — "one more try" is the whole game.

## 2. Core Game Loop
**Moment-to-moment:** fall → judge the next gap → flap to gain altitude → glide down →
pass the pipe (score) → judge the next gap → repeat. The player is constantly trading
altitude against the approaching gap, tapping just enough to stay alive.

**Session-level:** title/ready screen → tap to start → play (flap through pipes,
accumulate score) → collide (with pipe or ground) → instant death → game-over screen
showing score + best → tap to restart from the ready screen. A full session is dozens of
2-to-60-second runs.

## 3. Controls & Input
The game is single-button. The same action ("Flap") starts a run, flaps during play, and
(after a short lockout) restarts from game-over.

| Input | Device | Action | Model |
|-------|--------|--------|-------|
| `Space` | Keyboard | Flap / Start / Restart | Edge-triggered (key-down only; holding does NOT auto-repeat) |
| `↑` (Up arrow) | Keyboard | Flap (alias) | Edge-triggered |
| Left mouse click | Mouse | Flap / Start / Restart | Edge-triggered (button-down) |
| `Esc` | Keyboard | Pause / resume | Edge-triggered |

Input model notes:
- **Flap is edge-triggered.** Each physical key-down or click yields exactly one flap
  impulse. Auto-repeat from a held key MUST be ignored (track key-held state; only the
  press transition counts).
- A flap is accepted at any time during `Playing`, including when the bird is already
  rising (it overrides current vertical velocity — see 4.2).
- On the game-over screen, input is ignored for `restartLockoutMs` (default 600 ms) to
  prevent an accidental instant restart from the killing tap.

## 4. Mechanics (detailed)

All physics use a **logical pixel coordinate system** with **+Y pointing DOWN** (screen
convention). Positions/velocities are in logical px and px/s. The simulation runs on a
**fixed 60 Hz timestep** (`dt = 1/60 s ≈ 0.01667 s`); see §13.

### 4.1 Gravity & Falling
- Constant downward acceleration: **`gravity = 2400 px/s²`**.
- Applied every tick to the bird's vertical velocity: `vy ← vy + gravity * dt`.
- **Terminal velocity (downward):** `vyMax = +900 px/s`. After applying gravity, clamp
  `vy = min(vy, vyMax)`.

### 4.2 Flap Impulse
- A flap **sets** (does not add to) vertical velocity to a fixed upward value:
  **`vy ← flapImpulse = -620 px/s`** (negative = up).
- Because it overrides `vy`, rapid flaps do not stack into ever-higher jumps; each flap is
  one identical beat.
- There is no upward velocity cap beyond the impulse itself (the bird can never exceed
  `|flapImpulse|` upward, since flap sets and gravity only reduces upward speed).
- Optional ceiling rule: the bird may rise above the top of the screen (`y < 0`) but is
  **clamped** so its top never goes above `y = -birdHeight` — it cannot fly off-screen
  upward to dodge pipes. Hitting the ceiling clamps `y` and sets `vy = max(vy, 0)`.

### 4.3 Horizontal Scroll
- The bird's X position is **fixed** at `birdX = 320` (logical px from the left). The bird
  never moves horizontally.
- The world scrolls toward the bird: all pipes move left at **`scrollSpeed = 180 px/s`**
  (`pipe.x ← pipe.x - scrollSpeed * dt`). This is the effective forward speed of the bird.
- `scrollSpeed` is constant in v1 (no speed ramp — see §12 for the optional ramp tunable).

### 4.4 Pipe Spawning
- Pipes spawn as **pairs** (a top pipe and a bottom pipe sharing one gap).
- **Horizontal spacing:** a new pipe pair is created so that consecutive gaps are
  **`pipeSpacing = 360 px`** apart (measured center-to-center on X). Equivalently, spawn a
  new pair when the most-recent pair has moved `pipeSpacing` px from the spawn line.
- **Spawn line:** new pairs are created with `x = playfieldWidth + pipeWidth` (just off the
  right edge), i.e. `x = 1280 + 80 = 1360`.
- **Despawn:** a pair is removed once fully off the left edge (`x + pipeWidth < 0`).
- **Pipe width:** `pipeWidth = 80 px`.

### 4.5 Gap Size & Vertical Randomization
- **Gap height (vertical opening):** `gapHeight = 200 px` in v1 (tunable; see §12).
- The gap's vertical center `gapCenterY` is randomized per pair within a safe band so the
  gap never clips the ground or the top:
  - `gapMargin = 80 px` (minimum distance from gap edge to screen top and to the ground).
  - Let `groundY = 640` (top of the ground strip; see 4.7).
  - `gapCenterY ∈ [gapMargin + gapHeight/2, groundY - gapMargin - gapHeight/2]`
    = `[80 + 100, 640 - 80 - 100]` = **`[180, 460]`**.
  - Draw `gapCenterY = rng.NextFloat(180, 460)` (inclusive range).
- Derived geometry for a pair at horizontal position `x`:
  - **Top pipe:** rect `{ x; y = 0; w = 80; h = gapCenterY - gapHeight/2 }`.
  - **Bottom pipe:** rect `{ x; y = gapCenterY + gapHeight/2; w = 80; h = groundY - (gapCenterY + gapHeight/2) }`.

### 4.6 Scoring on Pass
- Each pipe pair has a `scored: bool` flag, initially `false`.
- When the **pipe pair's right edge** passes the **bird's X** (`pipe.x + pipeWidth < birdX`)
  and `scored = false`, increment `score` by **1** and set `scored = true`.
- Score is awarded for surviving the gap, not for touching it; passing the X line without a
  collision is sufficient.

### 4.7 Collision & Ground
- **Bird collision box (AABB):** centered on the bird, `birdWidth = 34 px` ×
  `birdHeight = 24 px`. To make near-misses feel fair, the collision box is **inset** by
  `collisionInset = 2 px` on every side relative to the drawn sprite (effective box
  ~30×20). All collision uses this inset AABB.
- **Pipe collision:** AABB-vs-AABB test of the bird box against each pipe rect (top and
  bottom) of every on-screen pair. Any overlap = collision.
- **Ground:** a solid ground strip occupies `y ∈ [groundY, playfieldHeight]` =
  `[640, 720]`. If `bird.box.bottom ≥ groundY`, that is a ground collision.
- **Ceiling:** not lethal — the bird is clamped (see 4.2).
- **Instant-death model:** ANY collision (pipe or ground) ends the run immediately. There
  is no health, no invulnerability frames, no knockback. On death: freeze scrolling,
  transition to `GameOver`, the bird falls under gravity to rest on the ground (cosmetic),
  and the death SFX/score panel show.

## 5. Entities / Game Objects

### Bird (exactly one)
- **Size (sprite):** 34×24 px. **Collision box:** inset AABB (~30×20), see 4.7.
- **Properties:** `x = 320` (fixed), `y` (float), `vy` (float), `rotationDeg` (cosmetic).
- **Behavior:** integrates gravity each tick; flap sets `vy`. Rotation is derived from
  velocity for flavor: `rotationDeg = clamp(vy * 0.06, -25, +90)` (nose-up when rising,
  nose-down when diving).
- **Created:** once, at run start, at `y = playfieldHeight/2 = 360`, `vy = 0`.
- **Destroyed:** never pooled away; reset on restart.

### PipePair (0..N, typically 4–5 on screen)
- **Properties:** `x` (float, left edge), `gapCenterY` (float), `scored` (bool).
- **Size:** width 80; top/bottom rects derived from `gapCenterY` and `gapHeight` (see 4.5).
- **Behavior:** moves left at `scrollSpeed`; flags `scored` when passed.
- **Created:** by the spawner every `pipeSpacing` px of scroll, off the right edge.
- **Destroyed:** when `x + pipeWidth < 0`.

### Ground (visual + collision)
- A static strip `y ∈ [640, 720]`. Texture/pattern scrolls left at `scrollSpeed` for
  parallax but its collision top stays at `groundY = 640`.

F# type sketch:
```fsharp
type Bird =
    { Y: float
      Vy: float
      RotationDeg: float }   // X is the constant birdX = 320.0

type PipePair =
    { X: float               // left edge, moves left over time
      GapCenterY: float
      Scored: bool }
```

## 6. World / Levels / Progression
- **Playfield:** `1280 × 720` logical px. Origin top-left, +Y down. The view scales this
  logical canvas to the physical window (letterboxed) so physics stays resolution-independent.
- **No discrete levels.** The game is a single endless run; "progression" is the rising
  score and the player's own improving skill.
- **Difficulty ramp (v1 = flat):** all constants (`scrollSpeed`, `gapHeight`, `pipeSpacing`)
  are constant for the whole run. The optional ramp (see §12) tightens the gap and/or
  speeds scroll as score climbs; it is OFF by default to match the classic feel.
- **What changes over time:** only the randomized `gapCenterY` per pair, the parallax
  scroll of background/ground, and the score.

## 7. State Model (Elmish/MVU)

### Model
```fsharp
type Phase =
    | Ready                  // waiting for first flap
    | Playing
    | GameOver of finalScore: int

type Model =
    { Phase: Phase
      Bird: Bird
      Pipes: PipePair list
      Score: int
      Best: int                       // persisted high score
      DistanceSinceSpawn: float       // px scrolled since last spawn, drives spawner
      Rng: System.Random              // seeded RNG for gap placement (determinism)
      GameOverElapsedMs: float        // for the restart lockout
      Paused: bool }
```

### Msg
```fsharp
type Msg =
    | Flap                  // edge-triggered: start / flap / restart (post-lockout)
    | TogglePause
    | Tick of dt: float     // seconds, fixed ~0.01667
```

### update — key cases
- **`Flap` while `Ready`:** transition to `Playing`, apply one flap impulse
  (`vy = flapImpulse`), reset score, clear pipes, reseed/keep RNG.
- **`Flap` while `Playing`:** set `bird.vy = flapImpulse` (override). Play flap SFX.
- **`Flap` while `GameOver`:** if `GameOverElapsedMs ≥ restartLockoutMs`, reset to a fresh
  `Ready` model (preserving `Best` and `Rng`); otherwise ignore.
- **`TogglePause`:** flip `Paused`; while paused, `Tick` is a no-op for physics.
- **`Tick dt` while `Playing` and not paused (in order):**
  1. Integrate bird: `vy = min(vy + gravity*dt, vyMax)`; `y += vy*dt`; clamp ceiling.
  2. Scroll pipes: `x -= scrollSpeed*dt`; advance `DistanceSinceSpawn += scrollSpeed*dt`.
  3. Spawn: while `DistanceSinceSpawn ≥ pipeSpacing`, emit a new pair (random `gapCenterY`
     from `Rng`), subtract `pipeSpacing`.
  4. Despawn pairs with `x + pipeWidth < 0`.
  5. Scoring: for each unpassed pair with `x + pipeWidth < birdX`, `score += 1; scored = true`.
  6. Collision: if bird AABB overlaps any pipe rect, or `bird.box.bottom ≥ groundY`,
     transition to `GameOver finalScore=score` and update `Best`.
- **`Tick dt` while `GameOver`:** advance `GameOverElapsedMs`; let bird keep falling to
  ground (cosmetic), do not scroll pipes.
- **`Tick dt` while `Ready`:** bird bobs gently (cosmetic sine hover), no gravity death.

### view
Pure function `Model -> Scene`. It reads phase + entities and emits draw commands
(background, pipes, ground, bird, HUD, overlays). It performs **no mutation** and no
physics; Skia draws the returned scene. Phase selects which overlay (ready prompt vs.
game-over panel) is added.

### Subscriptions
- **Tick:** a 60 FPS timer subscription dispatching `Tick (1.0/60.0)`. Implementation uses
  a fixed-timestep accumulator (see §13) so logic is frame-rate independent.
- **Input:** keyboard key-down (`Space`, `↑`, `Esc`) and mouse button-down events mapped to
  `Flap` / `TogglePause`, with key-held de-bounce so only press edges dispatch.

## 8. Rendering (Skia 2D)
Coordinate system matches physics (top-left origin, +Y down), logical 1280×720 scaled to
the window. Redraw the full canvas each frame (cheap at this entity count); no dirty-rect
optimization needed.

**Draw order (back to front):**
1. **Sky background** — solid fill `#4EC0CA` (classic cyan). Optional parallax cloud layer
   scrolling left at `0.3 * scrollSpeed`.
2. **Pipes** — for each pair, draw top + bottom rects. Body fill `#73BF2E`, 2 px darker
   outline `#558022`, and a "lip" cap rect (`88 px` wide, `26 px` tall, centered on
   `pipeWidth`) at each gap-facing end for the classic mushroom look.
3. **Ground strip** — `y ∈ [640, 720]`, fill `#DED895` with a `#73BF2E` top edge band
   (8 px). Texture offset scrolls left at `scrollSpeed`, wrapping every 48 px.
4. **Bird** — 34×24 sprite/rounded-rect, body `#F7D51D`, drawn rotated by `bird.rotationDeg`
   about its center. Simple 2-frame wing flap toggling on each `Flap`.
5. **HUD** — score text, top-center (see §9).
6. **Overlays** — ready prompt or game-over panel (see §9), drawn last.

**Fonts:** a single bold sans (e.g. bundled "Press Start 2P"-style or system bold). Score
uses a large outlined style: white fill `#FFFFFF` with a 3 px black `#000000` outline.

**Visual effects (optional, cheap):** a brief white flash (1 frame, `#FFFFFF` at 50% alpha
fading over 150 ms) on collision; small feather/dust particles on flap (≤ 6 particles).

## 9. UI / HUD / Screens

**Screens:**
- **Ready:** title "Flappy Bird" centered-upper; the bird bobbing at center; prompt
  "Tap / Space to flap" with a tap icon. Best score shown small, top-right.
- **Playing:** only the in-world HUD (live score).
- **Paused:** dim the playfield to 40% and show "PAUSED — Esc to resume" centered.
- **Game Over:** a centered panel (`~420×260 px`) with header "Game Over", the run score,
  the medal (optional), the best score, and "Tap to play again" (greyed until lockout ends).

**HUD elements:**
- **Live score:** top-center at `(640, 80)`, large outlined digits, integer, updates on pass.
- **Best score:** small, top-right `(1180, 24)`, format `BEST 000` (right-aligned).
- All text horizontally centered on its anchor unless noted.

## 10. Audio
Audio is optional in v1 (a silent build must still pass acceptance).

| Event | SFX |
|-------|-----|
| Flap | short "wing" whoosh |
| Pass pipe / score | bright "point" blip |
| Collision (pipe) | "hit" thud |
| Ground impact | "die" splat (after hit) |
| New best on game-over | "fanfare" sting |

Music: none during play (classic). Optional low ambient on title screen.

## 11. Win / Loss / Scoring
- **Scoring:** +1 per pipe pair passed (see 4.6). No multipliers, no time bonus, no combo.
- **Win condition:** none — the game is endless. "Winning" is beating your `Best`.
- **Loss condition:** any collision (pipe or ground) ends the run instantly (see 4.7).
- **Lives / continues:** none. One life per run; death → game-over → manual restart.
- **Best:** `Best = max(Best, finalScore)`, persisted across runs (see §13).

## 12. Difficulty & Balancing
Data-driven tunables (defaults match classic feel). The optional `*Ramp` values are OFF
by default.

| Name | Default | Range | Effect |
|------|---------|-------|--------|
| `gravity` | 2400 px/s² | 1500–3200 | Fall acceleration; higher = heavier, harder |
| `flapImpulse` | -620 px/s | -500..-760 | Upward beat strength (set, not added) |
| `vyMax` | 900 px/s | 600–1200 | Terminal fall speed |
| `scrollSpeed` | 180 px/s | 120–300 | World/forward speed; higher = less reaction time |
| `pipeSpacing` | 360 px | 260–480 | Horizontal distance between gaps |
| `gapHeight` | 200 px | 130–260 | Vertical opening; smaller = harder |
| `gapMargin` | 80 px | 40–140 | Keeps gaps off the top/ground |
| `pipeWidth` | 80 px | 60–100 | Pipe thickness |
| `birdX` | 320 px | 200–480 | Bird's fixed screen column |
| `collisionInset` | 2 px | 0–6 | Forgiveness on the bird hitbox |
| `restartLockoutMs` | 600 ms | 0–1200 | Anti-misclick delay on game-over |
| `gapHeightRamp` | 0 (off) | 0–0.5 px/pt | Optional: shrink gap by N px per point scored (floor 130) |
| `scrollSpeedRamp` | 0 (off) | 0–2 px/s/pt | Optional: speed up scroll per point (cap 300) |

## 13. Technical Notes
- **Performance budget:** trivial — 1 bird + ~5 pipe pairs + ground + HUD ≈ < 20 draw
  calls/frame. Target 60 FPS / 16.7 ms frame with vast headroom.
- **Fixed vs. variable timestep:** **fixed** 60 Hz logic. The tick subscription uses an
  accumulator: accumulate real elapsed time, step `update (Tick (1/60))` while
  `accumulator ≥ 1/60`, subtract each step. This makes physics deterministic and
  frame-rate independent; rendering interpolation is optional and not required for v1.
- **Determinism / RNG:** `gapCenterY` is the only randomness. Use a single seeded
  `System.Random` stored in the model. A fixed seed + a fixed sequence of `Flap` ticks
  yields an identical pipe layout — this is what acceptance tests rely on.
- **Persistence:** `Best` is saved to local storage / a small JSON file (`flappy.best`) and
  loaded on boot. If absent, `Best = 0`.
- **Edge cases:** flap on the exact death frame still dies; multiple pipes can be passed in
  a single tick at high `scrollSpeed` (loop all unpassed pairs, not just the front one);
  if the window loses focus, auto-`TogglePause`; resizing only rescales the logical canvas,
  never the physics constants; a gap must always satisfy `gapHeight + 2*gapMargin ≤ groundY`
  (200 + 160 = 360 ≤ 640 ✓) or spawning asserts.

## 14. Acceptance Criteria (test scenarios)

1. **Gravity pulls the bird down.**
   GIVEN a `Playing` model with `bird.y = 360`, `bird.vy = 0`
   WHEN one `Tick(1/60)` is applied with no `Flap`
   THEN `bird.vy ≈ 40` px/s (`2400 * 1/60`) and `bird.y > 360` (moved downward).

2. **Terminal velocity is clamped.**
   GIVEN a `Playing` model with `bird.vy = 880`
   WHEN one `Tick(1/60)` is applied (no flap)
   THEN `bird.vy = 900` (clamped to `vyMax`), not 920.

3. **Flap sets a fixed upward velocity (override, not additive).**
   GIVEN a `Playing` model with `bird.vy = 300` (falling)
   WHEN `Flap` is dispatched
   THEN `bird.vy = -620` exactly; AND dispatching `Flap` again immediately also yields
   `bird.vy = -620` (no stacking).

4. **Held key does not auto-flap.**
   GIVEN `Space` is pressed and held down across 30 ticks
   WHEN no new key-down edge occurs
   THEN exactly one flap impulse was applied (only on the initial press edge).

5. **Score increments when a pipe pair passes the bird.**
   GIVEN a pipe pair at `x` such that `x + pipeWidth = birdX + 1` and `scored = false`, score = 4
   WHEN ticks advance until `x + pipeWidth < birdX`
   THEN `score = 5` and that pair's `scored = true`; AND further ticks do not increment
   again for the same pair.

6. **Multiple pipes can score in one tick.**
   GIVEN two unpassed pairs both with `x + pipeWidth` just past `birdX` after a large `dt`
   WHEN one `Tick` is applied
   THEN `score` increases by 2 (the scorer loops all unpassed passed pairs).

7. **Collision with a pipe is instant death.**
   GIVEN a `Playing` model where the bird AABB overlaps a top pipe rect
   WHEN one `Tick` is applied
   THEN `Phase = GameOver finalScore=score` and scrolling stops on subsequent ticks.

8. **Ground collision is instant death.**
   GIVEN `bird.y` such that `bird.box.bottom ≥ 640` (groundY)
   WHEN one `Tick` is applied
   THEN `Phase = GameOver` (regardless of pipe positions).

9. **Ceiling is clamped, not lethal.**
   GIVEN repeated `Flap`s drive the bird to `y ≤ -birdHeight`
   WHEN ticks continue
   THEN `bird.y` is clamped to `-birdHeight`, `vy ≥ 0`, and `Phase` stays `Playing`.

10. **Pipe spawning cadence.**
    GIVEN a fresh `Playing` run with empty pipes and `scrollSpeed = 180`
    WHEN the world scrolls `pipeSpacing = 360` px (i.e. 2.0 s of ticks)
    THEN exactly one new pair has spawned per 360 px of scroll, each at `x = 1360`.

11. **Gap randomization stays in the safe band.**
    GIVEN any spawned pair across 1000 seeded spawns
    WHEN `gapCenterY` is sampled
    THEN `180 ≤ gapCenterY ≤ 460` for every pair (gap never clips top or ground).

12. **Deterministic layout from a seed.**
    GIVEN two runs with the same RNG seed and the identical sequence of `Flap`/`Tick`
    WHEN both run for 30 s
    THEN the two pipe layouts (`x`, `gapCenterY` sequences) are identical and scores match.

13. **Restart lockout on game-over.**
    GIVEN `Phase = GameOver` with `GameOverElapsedMs = 200` (< 600)
    WHEN `Flap` is dispatched
    THEN the model stays `GameOver` (input ignored); AND once `GameOverElapsedMs ≥ 600`,
    a `Flap` resets to a fresh `Ready` model preserving `Best`.

14. **Best score persists and updates.**
    GIVEN `Best = 7` and a run that ends with `finalScore = 12`
    WHEN entering `GameOver`
    THEN `Best = 12`; AND a subsequent run ending at `finalScore = 5` leaves `Best = 12`.

15. **Pause freezes physics.**
    GIVEN a `Playing` model
    WHEN `TogglePause` then several `Tick`s are applied
    THEN `bird.y`, `bird.vy`, and pipe `x` are unchanged until `TogglePause` resumes.

## 15. Stretch Goals
1. **Medals** (bronze/silver/gold/platinum at score thresholds 10/20/30/40) on game-over.
2. **Difficulty ramp** — enable `gapHeightRamp`/`scrollSpeedRamp` for an escalating mode.
3. **Day/night theme swap** every N points (palette change for sky/pipes).
4. **Ghost replay** of your best run (deterministic RNG makes this nearly free).
5. **Daily seed challenge** — fixed seed of the day, leaderboard by score.
6. **Alternate birds / skins** (cosmetic palette swaps).
7. **Gamepad + touch** input parity for handheld/mobile builds.
