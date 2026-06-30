---
title: "Breakout"
slug: breakout
category: games
complexity: simple
genre: "Arcade / brick-breaker"
target_session_minutes: 8
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Breakout

## 1. Overview
Breakout is a single-screen arcade game in which the player slides a horizontal paddle
along the bottom of the screen to keep a bouncing ball in play. The ball ricochets off
the walls, the paddle, and a wall of colored bricks at the top; each brick the ball
strikes is destroyed and scores points. The core verb is **deflect** — the player reads
the ball's trajectory and positions the paddle to both survive and to *aim* the rebound
at the bricks they want to clear. It is fun because of the tight, deterministic physics:
a skilled player controls the ball's angle by where it hits the paddle, turning a
reactive defensive game into an offensive one of carving tunnels through the wall and
trapping the ball above it for a cascade of free hits.

## 2. Core Game Loop
**Moment-to-moment:** read ball trajectory → reposition paddle → deflect ball → control
rebound angle → break bricks → collect/avoid power-ups → repeat. The tension cycle is
"ball descending toward the gutter" → "successful save" → brief relief → repeat.

**Session-level:** Title screen → Serve (ball stuck to paddle, player launches) → Play
(clear the brick wall while keeping the ball alive) → either **Level Clear** (advance to
next layout, ball re-serves) or **Ball Lost** (lose a life; re-serve if lives remain) →
**Game Over** when lives reach 0 → Score summary / high-score table → Restart.

## 3. Controls & Input
Keyboard is primary. Mouse is an optional alternative for paddle movement. Input model
noted per row (held = continuous while down; pressed = edge-triggered on key-down).

| Input | Action | Model |
|---|---|---|
| `Left Arrow` / `A` | Move paddle left | Held |
| `Right Arrow` / `D` | Move paddle right | Held |
| `Space` | Launch ball from paddle (during Serve); fire laser (with Laser power-up) | Pressed (edge) |
| `Mouse X` | Move paddle to mouse X (clamped to playfield) | Continuous (optional) |
| `Left Click` | Launch ball / fire laser | Pressed (edge) |
| `P` / `Esc` | Pause / resume | Pressed (edge) |
| `Enter` | Start game (title) / restart (game over) | Pressed (edge) |
| `M` | Toggle mute | Pressed (edge) |

Notes: keyboard and mouse paddle control are mutually live — the most recent input source
wins for the current frame. Holding both Left and Right cancels to zero horizontal input.

## 4. Mechanics (detailed)

### 4.1 Coordinate system & playfield
Logical playfield is **1280×720** px, origin top-left, +x right, +y down. A **wall border**
of 16 px frames the top, left, and right edges (bricks and ball bounce off the inner
faces). The **bottom edge is open** (the "gutter"): a ball crossing y = 720 is lost. The
HUD occupies the top 48 px band; the brick field begins below it.

### 4.2 Paddle movement
- Paddle size: **104×18** px (default; the Widen power-up makes it 168 px).
- Horizontal speed: **620 px/s** (keyboard). Movement is velocity-based, no acceleration
  ramp (instant start/stop) for crisp arcade feel; friction is irrelevant.
- Paddle Y is fixed at **y = 680** (top edge of paddle).
- Clamped so the paddle stays fully inside the side walls: `x ∈ [16, 1280 − 16 − width]`.
- Mouse control: paddle center snaps to mouse X each frame, then clamped identically.

### 4.3 Ball physics
- Ball is a circle, radius **7** px (diameter 14).
- Constant speed magnitude; direction changes on bounce. **Speed is preserved exactly on
  wall and brick bounces** (perfectly elastic, no energy loss).
- Base speed **360 px/s** at level 1 serve. Speed increases with progression (see §4.4
  and §6).
- Gravity: **none**. Drag: **none**. The ball travels in straight lines between
  collisions.
- Wall bounce: reflect the velocity component normal to the wall (left/right walls flip
  `vx`; top wall flips `vy`). Reposition the ball just outside the wall to prevent
  tunneling/sticking.
- Minimum vertical speed guard: after any bounce, if `|vy| < 60 px/s` the ball is nudged
  so `|vy| = 60` (preserving total speed by recomputing `vx`). This prevents a
  near-horizontal ball from getting stuck ping-ponging between side walls forever.

### 4.4 Paddle deflection & angle control (the skill mechanic)
When the ball collides with the top face of the paddle, the rebound angle is **not** a
mirror reflection — it is determined by the **impact offset** from the paddle center:

```
offset   = (ball.x − paddleCenterX) / (paddleWidth / 2)   // ∈ [−1, 1]
maxAngle = 60°                                             // from vertical
bounceAngle = offset * maxAngle
vx = speed * sin(bounceAngle)
vy = −speed * cos(bounceAngle)   // always upward
```

So hitting the far-left edge sends the ball up-left at ~60° from vertical, dead center
sends it straight up, far-right sends it up-right at ~60°. `offset` is clamped to
[−1, 1]. The ball's `vy` is always forced negative (upward) after a paddle hit. Ball
speed magnitude is unchanged by paddle contact (control, not acceleration).

A small bonus: if the paddle is moving when it strikes the ball, add **15%** of paddle
velocity to `vx` (then renormalize to constant speed) — a subtle "english"/spin that
rewards active play. Capped so total horizontal angle never exceeds 75°.

### 4.5 Brick collision & destruction
- The ball checks against bricks using swept circle-vs-AABB (or, acceptably for v1,
  discrete AABB overlap with the ball's bounding box, since dt is small).
- On overlap, determine the dominant penetration axis: smaller overlap on X → flip `vx`;
  smaller on Y → flip `vy`. Reposition out of the brick to avoid double hits.
- Each hit reduces the brick's HP by 1. At HP 0 the brick is destroyed, awards points
  (§11), and may spawn a power-up (§4.6). Most bricks are HP 1; **silver** bricks are HP 2
  and **gold** bricks are indestructible (HP ∞, bounce only, no score).
- **One brick destroyed per frame max** is *not* enforced — multiple bricks may be hit in
  a single physics step (e.g. multiball), but a single ball resolves at most one brick
  collision per substep to keep reflection sane.
- Speed bump: every time the **cumulative bricks destroyed** crosses a multiple of 8, ball
  speed increases by **+8%** up to a cap of **620 px/s**. Speed also steps up when the ball
  first touches the top wall and when it breaks an orange or red brick (classic Atari
  rule), whichever raises it — see §12 for the tunable.

### 4.6 Power-ups
When a destructible brick is destroyed there is a **12%** chance to drop a power-up capsule.
Capsules fall straight down at **140 px/s** and are collected when they overlap the paddle;
a capsule reaching the gutter is lost. At most **1 power-up active per category**; collecting
a new capsule refreshes/replaces. Capsule size: 28×14 px.

| Power-up | Color | Effect | Duration |
|---|---|---|---|
| **Sticky** (Catch) | Green | Ball sticks to paddle on contact; player re-launches with Space/Click. Aim by moving paddle, then launch. | Until 3 catches used, or level end |
| **Multiball** | Cyan | Splits the active ball into **3** balls at ±25° spread; all share current speed. | Instant (balls persist) |
| **Widen** | Blue | Paddle width 104 → **168** px. | 15 s |
| **Laser** | Red | Paddle gains twin cannons; Space/Click fires a laser bolt that travels up at 700 px/s and destroys one brick (HP −1) on contact. Fire rate capped at 4/s. | 12 s |

Power-up drop selection is uniform across the four types unless tuned (§12). Only one
capsule may be on screen per the slot rule below: drops are allowed to stack on screen,
but a hard cap of **3 falling capsules** prevents clutter (excess drops are skipped).

### 4.7 Lives & serving
- Start with **3 lives**.
- A life is lost when the **last** active ball crosses y = 720. (With multiball, losing
  one ball while others are in play costs nothing.)
- On life loss (lives remain): reset to **Serve** state — a single ball is placed centered
  on the paddle, stuck, awaiting launch. Power-up timers and Widen/Laser are cleared on
  life loss; the layout (remaining bricks) persists.
- Bonus life at **20,000** points (once).

## 5. Entities / Game Objects

### 5.1 Paddle
- Size 104×18 (or 168×18 widened), HP n/a. Position (x = left edge, y = 680 fixed).
- State: `Normal | Widened | Sticky | Laser` flags can co-exist (composed in Model).
- Created at game start; never destroyed; reset on life loss.

### 5.2 Ball
- Circle r = 7. Properties: position (cx, cy), velocity (vx, vy), `stuck: bool`.
- Behavior: free-flight straight-line motion; reflects on collisions; "stuck" balls track
  the paddle and ignore physics until launched.
- Created on serve / split by multiball; destroyed when it exits the gutter.

### 5.3 Brick
- Size **64×24** px, 2 px gap between bricks. Properties: row, col, color, `hp`, `points`,
  `breakable: bool`.
- Behavior: static; decrement HP on hit; destroyed at HP 0.
- Created from level layout; destroyed by ball/laser.

### 5.4 PowerUp capsule
- Size 28×14. Properties: position, falling velocity (0, 140), `kind: PowerUpKind`.
- Behavior: falls; collected on paddle overlap; lost at gutter.

### 5.5 Laser bolt
- Size 4×16. Velocity (0, −700). Destroys one brick HP on contact; despawns on hit or at
  top wall.

F#-flavored sketch:
```fsharp
type Color = Yellow | Orange | Green | Red | Silver | Gold

type Brick =
    { Row: int; Col: int
      Bounds: Rect            // x, y, w=64, h=24
      Color: Color
      Hp: int                 // 1, 2, or Int32.MaxValue for Gold
      Points: int
      Breakable: bool }

type Ball =
    { Pos: Vec2; Vel: Vec2; Radius: float; Stuck: bool }

type PowerUpKind = Sticky | Multiball | Widen | Laser
type Capsule = { Pos: Vec2; Kind: PowerUpKind }
type Bolt = { Pos: Vec2 }
```

## 6. World / Levels / Progression

### 6.1 Brick grid layout
Playfield 1280×720. Brick field: **18 columns × 8 rows**.
- Brick cell: 64×24 with 2 px horizontal gap → 18 × 66 = 1188 px wide. Centered: left
  margin = (1280 − 1188) / 2 + 1 ≈ **47** px (inside the 16 px wall). Vertical gap 2 px.
- Field top at **y = 96** (below the 48 px HUD + 48 px breathing room). Rows occupy
  y = 96 … 96 + 8×26 = 304.

Default **Level 1** color rows (top → bottom), classic Atari point scheme:

| Rows | Color | Hex | HP | Points |
|---|---|---|---|---|
| 1–2 (top) | Red | `#D8482E` | 1 | 7 |
| 3–4 | Orange | `#E07A18` | 1 | 5 |
| 5–6 | Green | `#3FA34D` | 1 | 3 |
| 7–8 (bottom) | Yellow | `#E0C020` | 1 | 1 |

### 6.2 Level progression
At least **4** hand-authored layouts that cycle (after level 4, repeat with higher speed):

1. **Classic Wall** — full 18×8 grid as above.
2. **Fortress** — 18×8 grid with a 2-row band of **Silver** (HP 2, 50×level pts) across
   rows 3–4 and a single **Gold** (indestructible) brick in the center of row 1.
3. **Checkerboard** — alternating bricks present/absent in a checker pattern; gaps let the
   ball weave through; mixes Green/Orange.
4. **Tunnels** — three vertical Gold columns split the wall into channels, encouraging the
   tunnel-and-trap strategy; Red/Orange fill.

### 6.3 Difficulty ramp
- Ball serve speed: **360 + 20 × (level − 1)** px/s (capped contribution; total ball speed
  still capped at 620).
- Power-up drop chance falls slightly: `max(6%, 12% − 1% × (level−1))`.
- Paddle width and speed are constant across levels (player skill is the variable).
- "Speed-up triggers" (top-wall touch, orange/red brick break, every 8 bricks) apply each
  level.

## 7. State Model (Elmish/MVU)

### 7.1 Model
```fsharp
type Phase =
    | Title
    | Serve                      // ball stuck on paddle, awaiting launch
    | Playing
    | Paused
    | LevelClear of sinceMs: float
    | GameOver

type Paddle =
    { X: float                   // left edge
      Width: float               // 104 or 168
      Vx: float                  // last-frame velocity (for english)
      StickyCharges: int         // >0 => catch mode
      LaserUntilMs: float option
      WidenUntilMs: float option }

type Model =
    { Phase: Phase
      Paddle: Paddle
      Balls: Ball list
      Bricks: Brick list
      Capsules: Capsule list
      Bolts: Bolt list
      Score: int
      Lives: int
      Level: int
      BricksDestroyed: int       // cumulative, drives speed-up
      BallSpeed: float
      HighScore: int
      AwardedBonusLife: bool
      ElapsedMs: float
      Rng: System.Random
      Input: InputState }        // current held-key snapshot

and InputState =
    { Left: bool; Right: bool; MouseX: float option }
```

### 7.2 Msg
```fsharp
type Msg =
    | Tick of dt: float          // seconds, ~1/60
    | KeyDown of Key
    | KeyUp of Key
    | MouseMove of x: float
    | LaunchBall                 // from Space/Click during Serve or Sticky
    | FireLaser
    | TogglePause
    | StartGame
    | Restart
    | ToggleMute
```

### 7.3 update (key transitions)
- `StartGame` (from `Title`): build Level 1, set 3 lives, score 0, enter `Serve`.
- `LaunchBall` (from `Serve`): set ball `Stuck = false`, give it an upward velocity at the
  current angle (straight up, or slight angle from offset); → `Playing`.
- `Tick dt` (in `Playing`):
  1. Integrate paddle from `Input` (clamp); record `Vx`.
  2. Move stuck balls to follow paddle; integrate free balls.
  3. Resolve ball–wall, ball–paddle (§4.4), ball–brick collisions; update score,
     `BricksDestroyed`, ball speed; roll power-up drops.
  4. Integrate capsules; collect on paddle overlap → apply power-up; cull gutter.
  5. Integrate bolts; resolve bolt–brick; cull off-screen.
  6. Expire timed power-ups by `ElapsedMs`.
  7. Remove balls past gutter; if no balls remain → lose life (→ `Serve` or `GameOver`).
  8. If no breakable bricks remain → `LevelClear`.
- `LevelClear` after a 1.5 s delay: increment `Level`, build next layout, re-serve.
- `TogglePause`: `Playing ↔ Paused` (freezes Tick integration).
- `Restart` (from `GameOver`): persist high score, → `Title`.
- Input messages mutate `Input`/edge-trigger actions only; physics never runs outside
  `Tick` (keeps it deterministic).

### 7.4 view
Pure projection of `Model` → a draw command list (no side effects). Renders bricks,
paddle, balls, capsules, bolts, HUD text, and the current screen overlay based on `Phase`.
Skia executes the draw list (§8).

### 7.5 Subscriptions
- **Tick**: a 60 FPS timer (target dt = 1/60 ≈ 0.0167 s) emits `Tick dt` with the real
  elapsed delta, clamped to a max of 0.05 s to avoid spiral-of-death after a stall.
- **Input**: keyboard down/up and mouse-move events from the FS.GG.Rendering host mapped to
  `KeyDown/KeyUp/MouseMove`.

## 8. Rendering (Skia 2D)
Coordinate system matches the logical playfield (1280×720); the host scales to the window.
**Draw order (back → front):**
1. Background fill `#101018` (near-black blue). Optional subtle vertical gradient.
2. Wall border: 16 px frame, `#3A3A48`.
3. Bricks: filled `SKRect` per brick in its color hex; 1 px inner highlight (`+15%`
   lightness top/left) and shadow (`−15%` bottom/right) for a beveled look. Silver
   `#B8B8C0`, Gold `#E8C53A`. Destroyed bricks are simply absent from the list.
4. Capsules: rounded rect (radius 4) in the power-up color with a 1-letter glyph
   (S/M/W/L).
5. Paddle: rounded rect (radius 6), `#C8D0E0`; when Laser active draw two small cannon
   nubs; when Widened it's simply longer.
6. Laser bolts: 4×16 `#FF4040` rects with additive glow.
7. Balls: filled white circles `#FFFFFF` r = 7, plus a short 3-sample motion trail
   (previous positions, alpha 0.3 → 0).
8. HUD text (§9) and screen overlays (title/pause/game-over) with a 60% black scrim.

Particles: on brick destruction, emit **6–8** small square particles in the brick's color,
initial speed 80–160 px/s in a hemisphere away from impact, gravity 400 px/s², lifetime
0.4 s, fading alpha. Font: a bundled monospace/arcade font, e.g. "PressStart2P" or fallback
sans-serif bold.

**Redraw strategy:** full-frame clear-and-redraw every Tick (the scene is small; ~150
bricks + a few dynamic objects fit comfortably in the 16.7 ms budget). No dirty-rect
optimization needed for v1.

## 9. UI / HUD / Screens

**HUD** (top 48 px band, `#101018` with bottom border line):
- **Score** — left, x = 24, baseline y = 32, format `SCORE 0001234` (zero-padded 7).
- **High** — center, `HI 0050000`.
- **Lives** — right, x = 1256 right-aligned, rendered as up to 3 small paddle icons.
- **Level** — small, under score, `LV 02`.

**Screens:**
- **Title:** game name centered (~y = 240), "PRESS ENTER TO START" blinking at y = 420,
  high score below, control hints at bottom.
- **Play:** HUD + playfield.
- **Serve overlay:** "PRESS SPACE TO LAUNCH" pulsing near the paddle.
- **Pause:** scrim + "PAUSED" centered.
- **Level Clear:** "LEVEL n CLEAR" + brief countdown, 1.5 s.
- **Game Over:** scrim + "GAME OVER", final score, "NEW HIGH SCORE!" if beaten, "PRESS
  ENTER" to return to title.

## 10. Audio
Audio is optional in v1; provide hooks. SFX checklist (event → sound):
- [ ] Ball ↔ wall bounce → short low blip.
- [ ] Ball ↔ paddle → mid blip (pitch varies with offset).
- [ ] Ball ↔ brick → pitched click; pitch rises with row color (yellow→red).
- [ ] Brick destroyed (multi-HP) → heavier crunch on final hit.
- [ ] Power-up capsule collected → ascending chime (distinct per kind).
- [ ] Laser fire → pew; laser hit → zap.
- [ ] Life lost → descending tone.
- [ ] Level clear → short fanfare.
- [ ] Game over → longer descending jingle.
- [ ] Bonus life → sparkle.

Music: optional looping ambient/chiptune at low volume during Play; muted on Pause. `M`
toggles all audio.

## 11. Win / Loss / Scoring

**Scoring (per brick destroyed):**
- Yellow = 1, Green = 3, Orange = 5, Red = 7 (per §6.1).
- Silver = **50 × level** (only on the hit that destroys it, HP 2).
- Gold = 0 (indestructible).
- Combo: no multiplier in v1 (kept simple); see Stretch.

**Win condition:** clearing all *breakable* bricks → `LevelClear` → next level. The game
loops levels indefinitely (Atari-style); there is no final win screen in v1 — the goal is a
high score. (A "completed all 4 unique layouts twice = win" variant is a Stretch goal.)

**Loss condition:** lose a life when the last ball exits the gutter; **Game Over** at 0
lives. No continues in v1.

**Lives:** start 3; bonus life at 20,000 points (once); max display 3 icons (extra lives
counted but capped in the icon row).

## 12. Difficulty & Balancing
Data-driven tunables (defaults shown):

| Name | Default | Range | Effect |
|---|---|---|---|
| `ballBaseSpeed` | 360 px/s | 240–480 | Serve speed at level 1 |
| `ballSpeedCap` | 620 px/s | 480–800 | Max ball speed |
| `speedUpPerN` | +8% / 8 bricks | 0–20% | Periodic speed increase |
| `paddleSpeed` | 620 px/s | 400–900 | Keyboard paddle velocity |
| `paddleWidth` | 104 px | 72–140 | Normal paddle width |
| `widenWidth` | 168 px | 120–220 | Widened width |
| `maxBounceAngle` | 60° | 45–75 | Paddle deflection range |
| `minVerticalSpeed` | 60 px/s | 30–120 | Anti-stall vy guard |
| `lives` | 3 | 1–5 | Starting lives |
| `dropChance` | 12% | 0–30% | Power-up drop probability |
| `capsuleFallSpeed` | 140 px/s | 80–240 | Capsule descent |
| `multiballCount` | 3 | 2–5 | Balls after split |
| `widenDurationMs` | 15000 | 5k–30k | Widen lifetime |
| `laserDurationMs` | 12000 | 5k–30k | Laser lifetime |
| `bonusLifeScore` | 20000 | 5k–50k | One-time extra life |
| `levelSpeedStep` | 20 px/s | 0–60 | Serve speed added per level |

## 13. Technical Notes
- **Performance budget:** ~150 bricks + ≤8 balls + ≤3 capsules + ≤8 bolts + ≤64 particles.
  Trivial for 60 FPS / 16.7 ms; the bottleneck is brick draw (batch into one path/loop).
- **Fixed vs. variable timestep:** physics uses the variable `dt` from `Tick`, **clamped to
  ≤ 0.05 s**. For robustness against tunneling at high speed, sub-step the ball integration
  so no ball moves more than its radius (7 px) per substep (e.g. `n = ceil(speed*dt/7)`).
- **Determinism / RNG:** all randomness (drop rolls, drop kind, multiball spread, particles)
  draws from `Model.Rng`, a seeded `System.Random`. A fixed seed + recorded input sequence
  reproduces a run exactly — important for the acceptance tests below.
- **Persistence:** high score stored to a small local file/registry key
  (`breakout.highscore`), loaded on Title, saved on Game Over if beaten.
- **Edge cases:** ball trapped between brick wall and top (tunnel trap) — allowed and
  desirable; corner hits resolve by dominant-axis rule; simultaneous multi-brick hits each
  resolve once; capsule + gutter exit on same frame counts as lost; pausing freezes all
  timers (power-up durations use `ElapsedMs` which only advances on unpaused Ticks).

## 14. Acceptance Criteria (test scenarios)

1. **Launch from serve** — *Given* the game is in `Serve` with a ball stuck to a centered
   paddle, *When* the player presses Space, *Then* the phase becomes `Playing` and the ball
   has speed ≈ 360 px/s with `vy < 0` (upward).

2. **Wall bounce preserves speed** — *Given* a ball moving up-right at 360 px/s, *When* it
   strikes the right wall, *Then* `vx` flips sign, `vy` is unchanged, and the speed
   magnitude remains 360 ± 0.5 px/s.

3. **Paddle angle control** — *Given* a ball descending onto the paddle, *When* it contacts
   the **far-right** edge (offset ≈ +1), *Then* the rebound travels up-right at ≈ 60° from
   vertical; *And When* it contacts dead center (offset ≈ 0), *Then* the rebound is within
   ±3° of straight up.

4. **Brick destroyed scores correctly** — *Given* a level-1 wall, *When* the ball destroys
   one **red** brick, *Then* score increases by exactly 7 and that brick is removed from
   `Bricks`.

5. **Silver brick takes two hits** — *Given* a Silver brick (HP 2) at level 2, *When* the
   ball hits it once, *Then* it bounces, HP becomes 1, and no points are awarded; *When*
   hit again, *Then* it is destroyed and score increases by 100 (50 × level 2).

6. **Gold brick is indestructible** — *Given* a Gold brick, *When* the ball hits it any
   number of times, *Then* the ball always bounces, the brick remains, and score is
   unchanged.

7. **Level clear advances** — *Given* one breakable brick remaining, *When* the ball
   destroys it, *Then* phase becomes `LevelClear`, and after 1.5 s `Level` increments and a
   fresh layout is built with the ball re-served.

8. **Life lost on gutter exit** — *Given* a single ball in play with 3 lives, *When* the
   ball crosses y = 720, *Then* `Lives` becomes 2 and phase returns to `Serve`.

9. **Multiball does not cost a life until last ball lost** — *Given* the Multiball power-up
   produced 3 balls, *When* one ball exits the gutter, *Then* `Lives` is unchanged and 2
   balls remain in play; *And When* the final ball exits, *Then* a life is lost.

10. **Game over at zero lives** — *Given* 1 life remaining and one ball in play, *When* the
    ball exits the gutter, *Then* `Lives` becomes 0 and phase becomes `GameOver`.

11. **Power-up drop & collect (Widen)** — *Given* a deterministic seed that forces a Widen
    drop, *When* the brick is destroyed a capsule falls at 140 px/s, *And When* the paddle
    overlaps it, *Then* paddle width becomes 168 px and reverts to 104 px after 15 s of
    unpaused play.

12. **Laser fires and breaks a brick** — *Given* the Laser power-up is active, *When* the
    player presses Space, *Then* a bolt spawns moving up at 700 px/s, *And When* it contacts
    a breakable brick, *Then* that brick loses 1 HP and the bolt despawns.

13. **Anti-stall guard** — *Given* a bounce that would leave `|vy| < 60 px/s`, *When* the
    bounce resolves, *Then* `|vy|` is corrected to ≥ 60 px/s while total speed is preserved
    within 0.5 px/s.

14. **Input: held movement & clamp** — *Given* the paddle at the left wall, *When* `Left` is
    held for 1 s, *Then* the paddle does not move past x = 16 (stays clamped at the wall);
    *And When* `Right` is held, *Then* it moves rightward at ≈ 620 px/s.

15. **Pause freezes timers** — *Given* an active Widen with 10 s remaining, *When* the game
    is paused for 5 s and resumed, *Then* the Widen still has ≈ 10 s remaining (timer used
    only unpaused Ticks) and no ball/brick state changed during the pause.

16. **Determinism** — *Given* a fixed RNG seed and a recorded input sequence, *When* the
    sequence is replayed, *Then* the final `Score`, `Lives`, `Level`, and surviving brick
    set are identical across runs.

17. **Bonus life** — *Given* score just below 20,000 with the bonus not yet awarded, *When*
    a hit pushes score to ≥ 20,000, *Then* `Lives` increases by 1 once and never again.

## 15. Stretch Goals
1. **Combo multiplier** — consecutive brick hits without a paddle touch increase a score
   multiplier (rewards tunnel-trap play).
2. **Persistent power-up loadout & shop** between runs.
3. **Boss / moving brick formations** that shift horizontally.
4. **Two-player alternating** (classic Atari) and **co-op** (two paddles) modes.
5. **Editor** for custom brick layouts (data-driven layout files already enable this).
6. **"Win" mode** — finish all unique layouts twice for a victory screen + ranking.
7. **Screen-shake & juicier particles**, ball trails scaling with speed.
8. **Accessibility:** colorblind-safe brick palette toggle, adjustable ball speed.
