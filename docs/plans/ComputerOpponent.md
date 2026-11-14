# Plan: computer opponent

Status: approved 2026-11-14 and built on branch `feat/computer-opponent`; awaiting review. Decisions on the open questions: one strength, a pause of about 700 ms (shorter at higher animation speed), no computers-only games.

Not yet verified: the build environment of this change had no .NET SDK, so the C# code and its tests were written but not compiled or run (CI does that on the pull request). The scene compiles (`npm run build`), but the new face-down flights were not looked at in a browser or measured against the frame budget; do that before merging.

## Goal

A player can play against one or more computer players, alone or mixed with humans on one screen. The computer follows the same rules as a human, sees only what a human in its seat would see, and plays at a pace and with animations that are easy to follow.

## Non-goals

- Learning, and simulating the opponent's unknown tiles (see "Levels").
- Online play. The design keeps Core deterministic so a server could run the same strategy later.
- A thinking animation beyond a short pause.

## Design

### Core: the strategy (`src/Triominos.Core/Ai/`)

```csharp
public abstract record ComputerMove;
public sealed record PlaceMove(int TileId, Placement Placement) : ComputerMove;
public sealed record DrawMove : ComputerMove;
public sealed record PassMove : ComputerMove;

public static class ComputerPlayer
{
    public static ComputerMove Decide(TriominosGame game);
}
```

- `Decide` is a pure function of the game state and has no randomness: the same state always gives the same move (ties are broken by tile id, then cell, then rotation). Games stay reproducible with `?seed=N`.
- It reads only what the current player may see: their own rack, the board, the pool size. It never looks at another player's rack.
- It returns a move; the caller applies it with the normal `Place`, `Draw` and `Pass` commands. The strategy never mutates the game, so every move is validated by the same code as a human move.

Strategy (greedy, one ply):

1. List every valid `(tile, placement)` of the current rack with `game.ValidPlacements(tileId)`.
2. Score each with the real score (tile value, start bonus, bridge, hexagons).
3. Play the highest. Ties: the tile with the higher value first (empties the rack of heavy tiles), then lowest tile id, cell and rotation.
4. No valid move: `DrawMove` while `CanDraw`; then `PassMove` when `CanPass`.
5. Starting phase: the same procedure applies, since `ValidPlacements` already restricts it to the allowed starting tiles. With 0-0-0 allowed, the +40 makes it win automatically.
6. It never draws while it can play. Drawing costs 5 points and is allowed by the rules, but a greedy player has no reason to.

To avoid copying scoring rules, Core gets one small public method: `TriominosGame.ScoreOf(int tileId, Placement placement)` returning the `ScoreBreakdown` that `Place` would give. It reuses the private `StartScore`/`PlayScore`, so there is one place for the rules. It does not change state.

### Web

**Setup** (`PlayerSetup`, `PlayerSetupForm`)
- `PlayerSetup` gets `bool IsComputer = false`. Existing call sites and tests keep compiling.
- Each player row gets a toggle "Mens / Computer". A computer player gets the default name "Computer" (then "Computer 2", ...), editable, and still passes `PlayerNameCheck`.
- At least one human is required: a game of only computers is not offered (no spectator mode planned).
- `Preferences` stores the flag per player in the existing players key; a stored value without the flag loads as human (old data keeps working).

**Session** (`PlaySession`)
- `IsComputerTurn`: the current player is a computer and the round is not finished.
- `Screen` flow: the handover screen exists to hide a rack from the other humans. It is only used when two or more humans share the screen. With one human there is no handover. A computer turn never gets a handover screen.
- During a computer turn `Pick`, `Place`, `Draw` and `Pass` from the UI are refused (message: "De computer is aan de beurt.").
- New `PlayComputerStep()`: asks `ComputerPlayer.Decide`, applies the move through the existing `Apply`, and returns the `MoveResult`. One step is one move: one placement, one draw or one pass. A computer that draws three times and then plays takes four steps, each animated.

**Page** (`Home.razor.cs`)
- After `AnimationsFinished`, if `IsComputerTurn`, wait a short pause (about 700 ms, scaled by the animation speed setting; none at "instant", if that exists) and run one `PlayComputerStep`, through the same `AfterMoveAsync` as a human move. The page stays `_busy` the whole time, so buttons and input stay locked.
- The loop ends by itself when the next player is human or the round is finished. Opening the menu during a computer turn is blocked while busy, as it is now; closing the menu or starting a new game cancels a pending step (a version counter per session, so a stale step does nothing).
- The rack strip: with exactly one human, the human's own rack stays visible during the computer's turn. With several humans it is empty until the next human continues.

**Scene** (TypeScript, small changes, still event-sized)
- `PlacedEvent` and `DrawnEvent` get `hidden: boolean` (true for a computer player) and the scene gets the anchor of the player panel (`data-anchor="player-N"`), which exists already for the deal.
- A hidden placement: the tile flies face down from the player's panel, turns over above its cell, then falls and bounces like a normal placement (no ghost tile to start from; the code path for "no ghost" exists).
- A hidden draw: a face-down tile flies from the pot to the player's panel instead of to a rack slot, and its value is never sent to the scene (`MoveViews` omits the tile values for hidden draws).
- Score floaters, sparkles and sounds are unchanged.

**Messages** (Dutch, in `PlaySession.Describe`) already name the player and do not reveal drawn tiles, so "Computer legt 2-3-5 voor 10 punten." and "Computer trekt een steen (-5)." work without change.

### Why this split

- The strategy is Core, because it is game logic and has to be testable and reusable by a server.
- Pacing and animation are Web/scene, because they are presentation.
- The computer goes through the same commands as a human, so it cannot make an illegal move or skip a rule.

## Levels (added after approval of the first version)

The strength is a setting per computer player on the setup screen: Makkelijk, Normaal, Sterk. `Decide(game, level, random)` takes the level; `Decide(game)` stays and means Normal. The level is stored in the players key (`Color:name:computer[:easy|strong]`; Normal is the default and not written).

- **Easy:** a random valid placement. It draws or passes only when nothing fits. The `Random` comes from the session, so a seed still reproduces a game.
- **Normal:** the placement with the highest score (see "Strategy" above).
- **Strong:** a beam search over its own next three moves. The opponent's tiles are unknown, so the board is assumed to change only by these moves; later moves count less (x0.5 per step) because the opponent moves in between. A line is worth its discounted points (plus the going-out bonus if the rack empties) minus 5 points per tile left that fits nowhere (it will probably cost a draw). Eight lines are kept per step and only the best 24 by points are checked for stuck tiles, which bounds the work (a decision should take well under a second; Blazor WebAssembly has one thread, so a long search would freeze the page). It is deterministic, and the starting move is the same as Normal's. Moves are tried on `GameBoard.Clone()`, so the game is never touched.

What is not done: guessing the opponent's tiles (the unseen tiles are the set minus the board minus its own rack) and simulating their replies, which would be the next step if Strong turns out to be too weak.

The strength tests (Normal beats Easy in at least 75% of the games; Strong wins at least half of its games against Normal) are the first evidence; the Strong bar is low on purpose because the planning could not be measured when it was written. Read the numbers from CI and raise the bar.

## Tests

Core (`tests/Triominos.Tests/ComputerPlayerTests.cs`, seeded):
- Chooses the placement with the highest score, including a hexagon or bridge over a higher tile value.
- Starts with the allowed tile that scores the most (0-0-0 with its +40).
- Never draws while a valid move exists; draws when there is none; passes only when `CanPass`.
- Same state gives the same move (determinism).
- Does not use other players' racks: swapping another player's rack does not change the move.
- Simulation: 500 seeded games of computers against each other (2, 3 and 4 players) always end, never produce a refused move, and finish in a bounded number of moves.
- Strength check: the computer beats a random valid mover in at least 75% of 200 seeded games.
- `ScoreOf` equals the score `Place` gives for the same move, in the start phase and with bridge and hexagon.

Web (`tests/Triominos.Web.Tests`, bUnit):
- Setup: the toggle, default and unique computer names, at least one human, the flag is stored and old stored players load as human.
- `PlaySession`: no handover with one human; handover kept with two humans; `IsComputerTurn`; human input refused during a computer turn; `PlayComputerStep` applies exactly one move.
- `Home`: after the human's move and `AnimationsFinished`, the computer moves without a click; the loop stops at a human turn and at the end of the round; buttons stay disabled while it plays; starting a new game cancels a pending step.

Scene: checked by hand and with the smoke test (`tools/smoke`): a full game against the computer, and the frame budget during a hidden placement (CLAUDE.md: measure after visual changes).

## Steps

1. Core: `ScoreOf`, `ComputerPlayer` and tests (test-first).
2. Web logic: `PlayerSetup.IsComputer`, preferences, `PlaySession`, the loop in `Home`, bUnit tests.
3. Setup form: the toggle.
4. Scene: hidden placement and draw, smoke test and measurement.

Each step builds and passes its tests on its own.

## Open questions

1. ~~One strength or two?~~ Decided: three levels, see "Levels".
2. Pause before a computer move, proposal ~700 ms at normal speed.
3. Allow a game with only computers (spectating)? Proposal: no.
