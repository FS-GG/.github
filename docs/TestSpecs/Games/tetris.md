---
title: "Tetris"
slug: tetris
category: games
complexity: simple
genre: "Falling-block puzzle"
target_session_minutes: 10
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Tetris

## 1. Overview
Tetris is a falling-block puzzle. Seven distinct four-cell pieces (tetrominoes) descend
one at a time into a narrow 10-wide, 20-tall well. The player slides and rotates each
piece as it falls, packing them into a flat, gapless stack. Completely filling a
horizontal row clears it and awards points; the rows above collapse down. The fantasy is
pure spatial mastery under escalating time pressure: the well never empties, gravity
accelerates with every level, and a single misplaced piece can cascade into a topped-out
board. The core verb is **place** — every second is a small optimization problem solved
with rotate, shift, and drop. It is fun because the rules are trivial to learn, the
inputs are instantaneous, and the difficulty curve is self-inflicted: you lose because
the board you built failed, not because the game cheated.

## 2. Core Game Loop
**Moment-to-moment loop:** spawn piece at top → fall under gravity → player shifts /
rotates / soft-drops / hard-drops → piece lands and locks → check & clear full lines →
score & maybe level-up → spawn next piece → repeat.

**Session-level loop:** title screen → press Start → play (loop above) until a new piece
cannot spawn (top-out) → game-over screen showing final score / lines / level → press
Restart → new game with reset state and a freshly shuffled bag.

A single game is one continuous descent with no levels to "complete"; the session ends
only on loss. Target session length ~10 minutes for a competent player.

## 3. Controls & Input
Keyboard is primary. Input model mixes **edge-triggered** (fire once on key-down) and
**held with auto-repeat** (DAS/ARR) actions.

| Input | Action | Model |
|---|---|---|
| ← Left Arrow | Move piece one cell left | Edge-trigger on press; then **DAS** auto-repeat while held |
| → Right Arrow | Move piece one cell right | Edge-trigger on press; then **DAS** auto-repeat while held |
| ↓ Down Arrow | Soft drop (accelerated fall) | Held: gravity multiplied while down is held |
| ↑ Up Arrow / X | Rotate clockwise (CW) | Edge-trigger (no auto-repeat) |
| Z / Ctrl | Rotate counter-clockwise (CCW) | Edge-trigger |
| Space | Hard drop (instant drop + lock) | Edge-trigger |
| C / Shift | Hold piece (swap with hold slot) | Edge-trigger, once per spawn |
| P / Esc | Pause / resume | Edge-trigger |
| Enter | Start / Restart (on title & game-over) | Edge-trigger |

**DAS (Delayed Auto Shift):** after a directional key is held for **DAS = 170 ms**, the
piece begins shifting repeatedly at **ARR (Auto Repeat Rate) = 50 ms** per cell until the
key releases or hits a wall. The very first shift happens immediately on key-down.

**Soft drop:** while ↓ is held, gravity interval is divided by the soft-drop factor
(20×, see §4.3); releasing returns to normal gravity. Soft drop never skips the lock
delay.

## 4. Mechanics (detailed)

### 4.1 The Well (playfield grid)
The board is a grid **10 columns × 20 visible rows**, plus **2 hidden buffer rows above**
the top (rows -1, -2) where pieces spawn. Total logical grid is 10×22, with rows 0–19
visible. Each cell is either `Empty` or `Filled of color`. Coordinates: column `x`
increases rightward 0→9; row `y` increases **downward** 0→21 (y=0 is top visible row, y=21
is floor row). Gravity moves pieces toward increasing `y`.

### 4.2 Tetrominoes
Exactly 7 pieces, each 4 cells, identified by letter with a fixed Tetris-guideline color:

| Piece | Color (hex) | Spawn shape (cells, relative) |
|---|---|---|
| I | Cyan `#00F0F0` | horizontal bar, 4 wide |
| O | Yellow `#F0F000` | 2×2 square |
| T | Purple `#A000F0` | 3-wide row + 1 above center |
| S | Green `#00F000` | top two cells right-shifted |
| Z | Red `#F00000` | top two cells left-shifted |
| J | Blue `#0000F0` | 3-wide row + 1 above left |
| L | Orange `#F0A000` | 3-wide row + 1 above right |

Spawn placement: pieces spawn horizontally, centered in columns 3–6, with the piece's
bounding box straddling the hidden buffer / top visible row. The **I** and **O** spawn in
columns 3–6 / 4–5 respectively per guideline. If any spawn cell overlaps a filled cell,
the game tops out (§11).

### 4.3 Gravity & dropping
- **Gravity** moves the active piece down one cell every **gravity interval** (a function
  of level, §6.2). Each step is collision-checked; if the cell below is blocked the piece
  does not move and the **lock timer** runs.
- **Soft drop:** while ↓ held, effective gravity interval = `gravityInterval / 20`
  (clamped to a minimum of one step per frame). Awards 1 point per cell soft-dropped.
- **Hard drop:** the piece teleports to its lowest legal position instantly and **locks
  immediately** (no lock delay). Awards 2 points per cell traveled.

### 4.4 Rotation (SRS) & wall kicks
Rotation uses the **Super Rotation System (SRS)**. Each piece has 4 rotation states:
`0` (spawn), `R` (CW from spawn), `2` (180°), `L` (CCW from spawn). Rotation pivots around
the piece's SRS center.
- **O** never effectively rotates (no offset).
- On a rotation attempt, the game tests the target orientation at 5 candidate offsets (the
  SRS **kick table**) in order; the first offset with no collision is applied. If all 5
  fail, the rotation is rejected and the piece is unchanged.
- **J, L, S, T, Z** share the standard JLSTZ kick table; **I** uses its own I kick table.
  Kick offsets are the guideline values, e.g. for JLSTZ `0→R`: `(0,0) (-1,0) (-1,+1) (0,-2) (-1,-2)`
  (x right-positive, y up-positive in kick-table convention; convert to the y-down grid by
  negating the y component).

A **simpler fallback** is acceptable for v1 if SRS is too costly: rotate around the
bounding-box center and try at most 3 kicks — `(0,0)`, `(-1,0)`, `(+1,0)` — rejecting the
rotation if none fit. The acceptance tests in §14 are written against the simpler model and
hold for SRS too.

### 4.5 Lock delay
When a piece rests on the stack (cell below blocked), a **lock timer = 500 ms** starts.
- If the player moves or rotates the piece into a position where it can still fall, the
  timer resets — but only up to **15 move/rotate resets** per piece; after 15 resets the
  next time it rests it locks immediately. This prevents infinite stalling.
- When the timer expires (or hard drop fires), the piece's cells are written into the grid
  permanently and line-clear evaluation runs.

### 4.6 Line clears
After a lock, scan all rows. Any row whose 10 cells are all `Filled` is **cleared**:
removed, with every row above it shifted down by one, and empty rows inserted at the top.
Clearing 1/2/3/4 rows simultaneously scores differently (§11). Four at once is a "Tetris".

### 4.7 7-bag randomizer
The piece sequence uses the **7-bag** system: take all 7 piece types, shuffle them
(Fisher–Yates with the seeded RNG), and deal them one at a time. When the bag empties,
refill and reshuffle. This guarantees every 7 spawns contain each piece exactly once — no
floods, no droughts longer than 12 pieces.

### 4.8 Hold & next queue
- **Next queue:** the upcoming pieces are previewed; show the next **5** pieces (drawn
  from the current + refilled bag).
- **Hold:** pressing Hold moves the active piece into the hold slot. If the slot was empty,
  the next piece spawns; if occupied, the held and active pieces swap and the swapped-in
  piece respawns at the top. Hold may be used **once per spawned piece** (a `holdUsed` flag
  resets when a new piece spawns from the queue, not from a swap).

## 5. Entities / Game Objects

### 5.1 Cell
The atomic grid unit. A cell is `Empty` or `Filled of PieceColor`. No behavior; it is data
read by the renderer and the line-clear scan.

### 5.2 Active Piece
The single falling tetromino. Properties: piece kind (`I O T S Z J L`), an `(x, y)` grid
origin, a rotation state (`0 R 2 L`), and a 0–15 lock-reset counter. Behavior is a small
state machine: **Falling** (gravity steps, accepts input) → **Resting** (lock timer
running, still accepts input that may return it to Falling) → **Locked** (cells committed,
piece destroyed, next spawns). Created by spawn (from queue or hold swap); destroyed on
lock.

```fsharp
type PieceKind = I | O | T | S | Z | J | L
type Rotation  = R0 | R1 | R2 | R3            // 0, R(90 CW), 2(180), L(270)

type Piece =
    { Kind     : PieceKind
      Pos      : int * int                    // grid origin (col x, row y)
      Rotation : Rotation
      LockResets : int }                       // 0..15

/// 4 occupied grid cells for a piece at its current pos+rotation
val cellsOf : Piece -> (int * int) list
```

### 5.3 Board / Well
A 2D array of cells, `10 wide × 22 tall` (rows 0–21; 0–19 visible). Created at game start
(all `Empty`); mutated only at lock (write piece cells) and clear (shift rows). It is the
source of truth for collision and rendering.

### 5.4 Bag / Queue
Holds the remaining shuffled pieces of the current bag plus enough of the next bag to show
5 previews. `next` pops the head and refills/reshuffles when low.

### 5.5 Hold slot
Optional single `PieceKind` plus the `holdUsed` flag.

## 6. World / Levels / Progression

### 6.1 Playfield dimensions
Logical render canvas **1280×720**. The well is centered: each cell is a **32×32 px**
square, so the 10×20 visible well is **320×640 px**, drawn at origin `(480, 40)`
(centered horizontally; 40 px top margin, 40 px bottom margin). Hold panel sits left of the
well; next-queue panel and score HUD sit to the right.

### 6.2 Levels & difficulty ramp
- The game starts at **level 0** (or a chosen start level 0–9).
- **Level up:** every **10 lines cleared** total increases the level by 1 (line counter is
  cumulative; level = `totalLines / 10`, capped at the start level as a floor).
- Higher level = faster gravity. Gravity interval per level (frames at 60 FPS, classic
  guideline-style curve), converted to seconds:

| Level | Frames/cell | Seconds/cell |
|---|---|---|
| 0 | 48 | 0.800 |
| 1 | 43 | 0.717 |
| 2 | 38 | 0.633 |
| 3 | 33 | 0.550 |
| 4 | 28 | 0.467 |
| 5 | 23 | 0.383 |
| 6 | 18 | 0.300 |
| 7 | 13 | 0.217 |
| 8 | 8 | 0.133 |
| 9 | 6 | 0.100 |
| 10–12 | 5 | 0.083 |
| 13–15 | 4 | 0.067 |
| 16–18 | 3 | 0.050 |
| 19–28 | 2 | 0.033 |
| 29+ | 1 | 0.017 |

The only thing that changes over time is gravity speed (and thus required reaction time).
The board, scoring multipliers, and piece set are constant.

## 7. State Model (Elmish/MVU)

### 7.1 Model
```fsharp
type Phase = Title | Playing | Paused | GameOver

type Model =
    { Phase        : Phase
      Board        : Cell[,]            // [22, 10]  (rows, cols); rows 0-19 visible
      Active       : Piece option        // None between lock and spawn
      Bag          : PieceKind list      // remaining shuffled pieces (>= 5 buffered)
      Hold         : PieceKind option
      HoldUsed     : bool
      Score        : int
      Lines        : int                 // cumulative cleared lines
      Level        : int
      StartLevel   : int
      // timers (seconds)
      GravityAcc   : float               // accumulates dt; steps when >= gravityInterval
      LockTimer    : float option        // Some t = resting, counting down from 0.5
      DasTimer     : float               // for held left/right auto-shift
      Dir          : int                 // -1 left, 0 none, +1 right (held direction)
      SoftDrop     : bool
      Rng          : System.Random       // seeded
      HighScore    : int }
```

### 7.2 Msg
```fsharp
type Msg =
    | Tick of float                       // dt in seconds, ~1/60
    | MoveLeftDown | MoveLeftUp
    | MoveRightDown | MoveRightUp
    | SoftDropDown | SoftDropUp
    | RotateCW | RotateCCW
    | HardDrop
    | HoldPiece
    | TogglePause
    | StartGame                           // from Title / GameOver
```

### 7.3 update (key transitions)
- **`StartGame`**: reset Board to empty, shuffle a fresh bag from `Rng`, spawn first piece,
  `Phase = Playing`, zero score/lines, level = StartLevel.
- **`Tick dt`** (only when `Playing`):
  1. Advance `DasTimer` if `Dir <> 0`; when it crosses DAS, shift one cell every ARR.
  2. Add `dt` to `GravityAcc`; while `GravityAcc >= interval` (interval = soft-drop-adjusted
     gravity for current level) subtract and step the piece down one cell if legal.
  3. If the piece cannot move down: if `LockTimer = None` start it at 0.5; else decrement by
     `dt`; on ≤ 0 → **lock** (write cells, clear lines, score, spawn next, reset `HoldUsed`).
  4. If a downward step succeeded while resting, clear `LockTimer` (reset, counting a
     lock-reset, capped at 15).
- **`MoveLeftDown`/`MoveRightDown`**: set `Dir`, immediately try one shift; reset DAS timer.
  **`...Up`**: if it matches current `Dir`, set `Dir = 0`.
- **`RotateCW`/`RotateCCW`**: attempt SRS rotation with kick tests; on success, if resting,
  reset lock timer (count a reset).
- **`HardDrop`**: drop to lowest legal y, add 2×cells-traveled to score, lock immediately.
- **`HoldPiece`**: if not `HoldUsed`, swap active with hold (or pull from queue), respawn at
  top, set `HoldUsed = true`.
- **`TogglePause`**: `Playing ↔ Paused` (ignore other gameplay msgs while Paused).
- On lock that causes a spawn collision → `Phase = GameOver`, update `HighScore`.

### 7.4 view
`view` is pure: it reads the Model and emits draw commands (it does not mutate). It renders
the well grid, the locked cells, the ghost piece, the active piece, hold panel, next-queue,
and HUD text. Phase selects the screen (title / play / pause overlay / game-over). Skia
performs the actual GPU drawing from these commands.

### 7.5 Subscriptions
- **Tick:** a 60 FPS timer dispatching `Tick dt` with `dt` in seconds (target 0.0167 s);
  clamp `dt` to ≤ 0.05 s to avoid gravity jumps after a stall.
- **Input:** keyboard key-down → the edge-triggered / `...Down` messages; key-up → `...Up`
  messages for held keys (left/right/soft-drop).

## 8. Rendering (Skia 2D)
Coordinate system: origin top-left, +x right, +y down, logical 1280×720 (Skia scales to the
window). Single full redraw each frame (the board is small; no dirty-rect optimization
needed).

**Draw order (back to front):**
1. **Background** — solid `#101018` fill over the whole canvas.
2. **Well frame** — well background `#000000` rectangle at `(480,40)` size `320×640`; a
   1 px grid of `#202028` lines between cells; a 2 px border `#404050` around the well.
3. **Locked cells** — for each filled grid cell, a 32×32 rounded-rect (corner 3 px) in the
   piece color, with a lighter top-left bevel (`+20%` lightness) and darker bottom-right
   bevel (`-20%`) for a beveled-block look.
4. **Ghost piece** — the active piece projected to its hard-drop landing row, drawn as
   outlines / 30%-alpha fills in the piece color, so the player sees where it will land.
5. **Active piece** — same block style as locked cells, at its live position.
6. **Side panels** — Hold panel (left, at `(120,40)`, 200×120) and Next-queue panel (right,
   at `(840,40)`, 200×420) each with a `#181820` background, `#404050` border, and a label.
   Pieces drawn at half scale (16 px cells) inside.
7. **HUD text** — score / lines / level (see §9), white `#FFFFFF`, monospace font.
8. **Overlays** — pause dimmer (`#000000` at 60% alpha + "PAUSED") or game-over panel.

Line-clear visual effect: on a clear, flash the cleared rows white for ~120 ms (a short
animation timer) before collapsing — optional in v1 but specified for polish.

## 9. UI / HUD / Screens

**Screens:**
- **Title:** game name centered (~64 px), "Press Enter to Start" prompt, high score shown.
- **Play:** the well + panels + HUD (below).
- **Pause:** play screen frozen under a 60% dimmer with centered "PAUSED".
- **Game Over:** centered "GAME OVER", final Score / Lines / Level, "New High Score!" if
  beaten, and "Press Enter to Restart".

**HUD (during play):** right column, below the next-queue panel, monospace, right-aligned:
- `SCORE` — current score, zero-padded to 7 digits (e.g. `0012300`).
- `LINES` — cumulative lines cleared.
- `LEVEL` — current level.
- `HIGH` — session/persisted high score.
- **Hold** label above the hold panel; **Next** label above the queue panel.

## 10. Audio
Checklist (audio optional in v1):
- [ ] Piece move (left/right shift) — short tick.
- [ ] Rotate — soft click.
- [ ] Soft drop — subtle tick per cell.
- [ ] Hard drop — thud.
- [ ] Lock — light clack.
- [ ] Line clear (1–3) — chime.
- [ ] Tetris (4 lines) — bigger fanfare.
- [ ] Level up — ascending cue.
- [ ] Hold — swap whoosh.
- [ ] Game over — descending tone.
- [ ] Music — looping "Korobeiniki"-style theme; tempo optionally rises with level.

## 11. Win / Loss / Scoring
**No win condition** — Tetris is endless; the goal is the highest score before topping out.

**Loss (top-out):** the game ends when a newly spawned piece overlaps an already-filled cell
(the stack has reached the spawn zone). `Phase → GameOver`.

**Scoring (line clears, multiplied by `level + 1`):**

| Event | Base points | At level n |
|---|---|---|
| Single (1 line) | 100 | `100 × (n+1)` |
| Double (2 lines) | 300 | `300 × (n+1)` |
| Triple (3 lines) | 500 | `500 × (n+1)` |
| Tetris (4 lines) | 800 | `800 × (n+1)` |
| Soft drop | 1 per cell | (not multiplied) |
| Hard drop | 2 per cell | (not multiplied) |

Lives/continues: none. One game = one descent.

## 12. Difficulty & Balancing
Data-driven tunables:

| Parameter | Default | Range | Effect |
|---|---|---|---|
| `boardWidth` | 10 | 6–14 | Well width (cols) |
| `boardHeight` | 20 | 10–30 | Visible well height (rows) |
| `cellPx` | 32 | 16–48 | On-screen cell size |
| `dasMs` | 170 | 80–300 | Delay before auto-shift |
| `arrMs` | 50 | 0–120 | Auto-shift repeat rate |
| `lockDelayMs` | 500 | 200–1000 | Rest time before lock |
| `maxLockResets` | 15 | 5–30 | Anti-stall cap |
| `softDropFactor` | 20 | 2–40 | Gravity divisor while ↓ held |
| `linesPerLevel` | 10 | 5–20 | Lines to advance a level |
| `startLevel` | 0 | 0–19 | Initial gravity level |
| `nextPreview` | 5 | 1–6 | Visible upcoming pieces |
| `gravityCurve` | §6.2 | — | Seconds/cell per level table |

## 13. Technical Notes
- **Performance budget:** at most ~220 drawn cells (board 200 + active 4 + ghost 4 + panel
  previews) plus HUD text — trivially within a 16.7 ms frame at 60 FPS.
- **Timestep:** **fixed-logic, variable-render** — accumulate `dt` into `GravityAcc` so
  gravity is frame-rate independent; clamp `dt ≤ 0.05 s`. DAS/ARR and lock timers are also
  `dt`-accumulated.
- **Determinism / RNG:** all randomness (7-bag shuffle) flows through one seeded
  `System.Random`. Given the same seed and input sequence, the game is fully reproducible —
  essential for the acceptance tests below.
- **Persistence:** high score persisted to local storage / a settings file; loaded on title,
  written on game over if beaten.
- **Edge cases:** (a) rotation against a wall/floor uses kicks; if no kick fits, rotation is
  rejected, not clipped. (b) Hard drop into the spawn zone still locks then triggers top-out
  on next spawn. (c) Soft drop must never skip the lock delay (so a row can still be cleared
  after a fast drop). (d) Hold cannot be spammed (one use per piece). (e) Simultaneous
  multi-row clears must be detected in a single scan (a Tetris is one event, not four
  singles).

## 14. Acceptance Criteria (test scenarios)
All scenarios assume a fixed RNG seed and the default tunables unless stated.

1. **Spawn & fall.** *Given* a new game, *when* the first piece spawns, *then* it appears
   centered in columns 3–6 at the top and descends one cell per gravity interval for the
   current level (level 0 → one cell every 0.800 s ± one frame).

2. **Horizontal move & wall block.** *Given* a piece mid-board, *when* the player presses
   Left, *then* it shifts exactly one cell left; *when* it is against column 0 and Left is
   pressed, *then* it does not move and stays at column 0.

3. **DAS auto-shift.** *Given* Left held continuously, *when* 170 ms have elapsed, *then* the
   piece begins shifting one cell every 50 ms until release or a wall.

4. **Rotation with wall kick.** *Given* a piece flush against the right wall in an
   orientation that would clip the wall when rotated CW, *when* Rotate CW is pressed, *then*
   the piece kicks left into a legal position and rotates; *and if* no kick offset fits,
   *then* the piece's rotation and position are unchanged.

5. **Soft drop scoring.** *Given* a piece N cells above the stack, *when* ↓ is held until it
   lands, *then* gravity is ~20× faster and the score increases by N (1 point per soft-drop
   cell).

6. **Hard drop.** *Given* a piece with M cells of clear space below, *when* Space is pressed,
   *then* the piece instantly moves to the lowest legal row, locks immediately (no 500 ms
   delay), and the score increases by `2 × M`.

7. **Lock delay & reset.** *Given* a piece resting on the stack, *when* it has rested for
   500 ms with no input, *then* it locks; *but when* the player rotates or shifts it into a
   still-fallable position before 500 ms, *then* the lock timer resets — up to 15 times,
   after which it locks on next rest regardless.

8. **Single line clear & scoring.** *Given* row 19 has 9 of 10 cells filled and a piece fills
   the gap, *when* it locks, *then* exactly that row clears, rows above shift down one, and
   the score increases by `100 × (level + 1)`.

9. **Tetris (four-line) clear.** *Given* four stacked rows each missing only column 9 and a
   vertical I piece dropped into column 9, *when* it locks, *then* all four rows clear in a
   single event and the score increases by `800 × (level + 1)`.

10. **Level up & gravity change.** *Given* the player has cleared 9 lines at level 0, *when* a
    clear brings the cumulative total to 10, *then* the level becomes 1 and the gravity
    interval drops to 0.717 s/cell.

11. **7-bag fairness.** *Given* a fresh game, *when* the first 7 pieces are spawned, *then*
    each of the 7 tetromino kinds appears exactly once (in some order); *and* across any
    14 consecutive pieces, no kind appears more than twice.

12. **Hold piece, once per spawn.** *Given* an active piece and an empty hold slot, *when*
    Hold is pressed, *then* the piece moves to hold and the next-queue piece spawns; *when*
    Hold is pressed again before the new piece locks, *then* nothing happens (one hold per
    spawned piece).

13. **Next queue preview.** *Given* play in progress, *when* the current piece locks, *then*
    the piece previously shown first in the 5-deep next queue becomes active and the queue
    shifts up by one, revealing a new 5th preview.

14. **Top-out / game over.** *Given* the stack reaches the spawn zone, *when* the next piece
    would spawn onto an already-filled cell, *then* the game transitions to GameOver, the
    final score/lines/level are shown, and the high score updates if beaten.

15. **Pause freezes state.** *Given* play in progress with an active piece mid-fall, *when*
    Pause is pressed, *then* gravity, timers, and input are suspended and the board is shown
    dimmed; *when* Pause is pressed again, *then* play resumes from the identical state.

16. **Determinism.** *Given* the same RNG seed and the same recorded input sequence with
    timestamps, *when* the game is replayed, *then* the final board, score, lines, and level
    are identical to the original run.

## 15. Stretch Goals
1. **Ghost piece toggle & hold-to-rotate options** — accessibility/preference settings.
2. **Line-clear and lock animations** — flash + collapse tween, particle burst on Tetris.
3. **T-spin detection & bonus scoring** — reward kicked-in T placements (TSS/TSD/TST).
4. **Back-to-back & combo multipliers** — chained Tetrises / consecutive clears bonus.
5. **Marathon / Sprint (40-line) / Ultra (2-min) modes** — alternate goals & timers.
6. **Garbage / versus mode** — sent lines for local or networked 2-player.
7. **Replays & leaderboards** — record the deterministic input stream; persist top runs.
8. **Themes & skins** — swappable block palettes and backgrounds.
