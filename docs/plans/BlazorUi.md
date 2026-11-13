# Plan: Blazor + Three.js front end

Status: approved 2026-11-13. Phase 0 done (branch `feat/core-real-rules`); phase 1 prototype done (branch `feat/scene-table`); phase 2 built (branch `feat/board-and-play`), awaiting review; phase 3 built (branch `feat/animations`, stacked on phase 2); phase 4 built (branch `feat/screens`, stacked on phase 3).

## Goals

- A luxurious, 3D look: a real table, tiles with depth, light and shadow, smooth animations.
- Smooth: a stable 60 fps, no stutter, and every click or drag answered within one frame.
- Easy to use: drag a tile, see at once whether it fits, one key or click per action.
- The real Triominos rules (phase 0).
- Ready for online play later: the engine stays the single source of truth and the UI only sends commands. Online play itself is not part of this plan.

## Non-goals (for now)

- Online multiplayer, accounts, a computer opponent.
- A phone layout. Touch input on a tablet should work, but a small screen is not designed for. Revisit when online play is planned.

## Architecture

```
Core (net10.0, unchanged layering)  ←  Triominos.Web (Blazor WebAssembly, standalone)
                                           ├─ Razor components: menus, HUD, scores, dialogs (HTML/CSS)
                                           └─ wwwroot/scene: Three.js scene (TypeScript, bundled with Vite)
```

- **Core** keeps all rules. Blazor components call `IGameEngine` and listen to its events.
- **The 3D scene runs entirely in JavaScript**: render loop, animations, camera, drag and hit testing. C# never drives it per frame.
- **Interop is event-sized**, a few calls per move, not per frame:
  - C# → JS: `init(canvas, quality, dotNetRef)`, `setBoard(tiles)`, `setRack(tiles)` and, from phase 3, `playMove(move)` and `deal(counts)`. The planned `animatePlacement`, `animateDraw`, `showScore` and `setActivePlayer` became one `playMove` per move that carries the game events (see the phase 3 decisions).
  - JS → C# (via `DotNetObjectReference`): `AnimationsFinished()` (phase 3), `TileSelected(tileId)`, which returns the placements (cell plus rotation) where that tile fits, `TileDropped(tileId, row, col, rotation)` and `DrawRequested()` (space bar). Returning the placements from `TileSelected` replaces a separate `showValidPlacements` call: one round trip, nothing to get out of step.
- **Valid placements** are computed once in C# when a tile is picked up (cell plus the rotation that fits) and passed to JS. During a drag, JS only looks them up.
- **The WPF project** was removed in phase 0: the real rules changed the engine API so much that keeping it running would have meant rewriting a UI that was going to be replaced.

Alternatives rejected:

| Alternative | Why not |
|---|---|
| Keep WPF (`Viewport3D` or 2.5D) | No real shadows, PBR materials or post-processing. Rendering is largely CPU-bound, so effects make it stutter. Windows only, so online play would need a second front end. |
| Unity or Godot | Excellent 3D, but Core would have to be ported or bridged, the build is heavier, and web export is larger and slower to load than Blazor. |
| Blazor drawing in C# (canvas or WebGL wrappers per frame) | Interop on every frame causes the stutter this plan wants to avoid. |
| Plain JS modules without a build step | Fewer tools, but the scene layer grows to thousands of lines; TypeScript catches errors at build time. Vite adds Node to the build, which is accepted. |

## Rendering and performance

- **Render on demand:** a frame is drawn only while something moves (drag, animation, camera). An idle board costs no GPU time, which also keeps laptop fans quiet.
- **One shared tile geometry** (beveled, extruded triangle). The numbers come from one texture atlas, so each tile is one draw call or less.
- **Shadows:** one directional light with soft shadows. The shadow map is updated only when a tile moves, not every frame.
- **Hit testing by math, not raycasting meshes:** the mouse ray is intersected with the table plane and converted to `(row, col)` with a formula.
- **Quality setting** (low, medium, high): shadow resolution, bloom, anti-aliasing and pixel-ratio cap. The default is picked from a short measurement at startup.
- **Load time:** trimmed WebAssembly, Brotli compression, compressed textures (KTX2) and a loading screen. Target: playable within 3 seconds on broadband after the first visit, within 6 on the first visit.
- **Budget:** 60 fps with frame time under 10 ms on an integrated laptop GPU (medium quality). Every phase ends with a measurement in the browser profiler.

## Phase 0: foundation (Core rules and project setup)

Core changes, each with xUnit tests in `tests/Triominos.Tests`:

1. **Hidden pool.** Remove picking from the open pool (`SelectPieceFromPool`, `AddSelectedPieceToRack`). Fixes D-005.
2. **Drawing:** a player who cannot or will not play draws one tile at a time, -5 points per tile, up to 3 tiles. If the third still does not fit: -10 extra and the turn passes. An empty pool and no move: -10 and the turn passes. Fixes D-002.
3. **Deal and start:** 9 tiles each with 2 players, 7 each with 3 or 4. No random tile on the board: the player with the highest triple starts and plays it for tile sum +10. If that player also holds 0-0-0, they may play that instead for +40. If nobody has a triple, the player with the highest tile sum plays it, without bonus (ties: next-highest tile). The starting player chooses which allowed tile to play. Fixes D-001.
4. **Bonuses:** bridge +40, hexagon +50, double hexagon +60, triple hexagon +70. Fix hexagon detection (D-008) and check corner matching (D-007).
5. **End of round:** going out gives +25 plus the sum of the tiles left in the other racks. The game is one round. A blocked game (empty pool, nobody can move) ends as a draw: no winner, no settlement (D-003, D-004).
6. **Explicit rotation:** `PlacePiece(row, col, rotation)` instead of trying all rotations (D-102). `ValidatePlacement` no longer mutates the tile (D-101). `GetValidPlacements` returns the fitting rotation and does not clone per cell.
7. **One source of truth:** `TriominosGame` exposes only read-only state and three commands that return events, so the UI, and later a server, render from one object. A separate snapshot type was not needed.

Project setup:

- `Triominos.Web` (Blazor WebAssembly) and `wwwroot/scene` (TypeScript, Vite), wired into `dotnet build`.
- CI builds and tests Core and Web on Linux. The WPF build stays on Windows until it is removed.
- Deployment to GitHub Pages (static hosting, free).

## Phase 1: look and feel

- **Setting:** a walnut table with an inset of deep emerald felt, lit warmly from above-left.
- **Tiles:** ivory with a light marble grain and a clearcoat sheen. Numbers engraved in brushed gold. Slightly rounded edges.
- **Colors:** walnut `#3B2416`, emerald felt `#0F3D2E`, ivory `#F4EEDF`, gold `#C9A24B`, champagne text `#EFE3C8`.
- **Type:** Cormorant Garamond for titles, Playfair Display for all numbers (lining figures: a clear 1, unlike Cormorant's old-style figures), Inter for the interface. All three are open-source (SIL OFL).
- **HUD:** dark, slightly translucent panels with a thin gold border, over the 3D scene: players and scores, pool counter, buttons for draw, rotate and menu.
- **Assets:** CC0 materials and an HDR light environment (Poly Haven, ambientCG), so there are no licensing issues.

Deliverable: a static 3D table with a few tiles, the camera controls and the HUD, measured against the performance budget.

Phase 1 as built (2026-11-13):

- **Draw calls:** all tile bodies are one instanced mesh and the digits six instanced meshes (one per value), so a full board costs 9 draw calls in total.
- **Textures are procedural** (felt, walnut, ivory, digit atlas), drawn on canvases at startup. The CC0 asset sites could not be reached from the build environment, and procedural textures keep the download small. They can be swapped for photographed materials later.
- **Digits** are engraved gold, with their tops pointing at their corner, as on the physical tiles.
- **Camera:** left drag pans, right drag orbits, wheel zooms; the target stays above the felt. Left drag will also pick up tiles in phase 2.
- **Quality setting** in the HUD (low, medium, high). Picking it automatically from a startup measurement is not built: CPU-side frame timing does not measure GPU cost, and a GPU timer query is not available in every browser. Revisit if players report stutter on the default.
- **Demo:** until phase 2 adds input, `Prototype/DemoGame` plays rule-checked moves for both players when "Volgende zet" is clicked. It is removed in phase 2.
- **Not yet measured on a real GPU.** The build environment renders with a software rasteriser (SwiftShader), where a frame takes 11 to 20 ms of CPU; that says nothing about a GPU. Render-on-demand was verified: an idle table draws no frames.
- **UI language:** Dutch.

## Later phases

| Phase | Content |
|---|---|
| 2. Board and play | Rack as 3D tiles in front of the camera; drag with a ghost tile that snaps to the nearest cell (green if it fits, red if not); R or the mouse wheel rotates; valid cells glow softly; Space draws; Esc cancels. Zoom, pan and rotate the camera. |
| 3. Animations | Dealing, tile drop with a small bounce, floating points, gold effect for a bridge or hexagon, turn transition, end-of-round settlement. |
| 4. Screens | Main menu, player setup (names, colors), end screen, settings (animation speed, quality). Built; see the phase 4 decisions. A scoreboard across rounds was dropped (one round per game), sound moves to phase 5. |
| 5. Sound and finish | Tile on wood, drawing, bonus, win. |

## Testing

- Rules: xUnit in `tests/Triominos.Tests`, seeded `Random`, test-first for every defect in `docs/tracking/Defects.md`.
- Web: a few bUnit tests for the component logic (turn flow, button states). The 3D scene is checked by hand and with the profiler, not by automated tests.

## Decisions (2026-11-13)

- Double and triple hexagon bonuses are included.
- Bridge: the tile shares exactly one edge with placed tiles and its opposite corner touches another tile (the current Goliath/Foxmind wording). Filling a gap that matches two edges scores no bridge bonus.
- Drawing is allowed even with a playable tile (current Goliath/Foxmind rules); passing is only allowed without a playable tile, after three draws or with an empty pool.
- One round per game.
- The starting player chooses the starting tile from the tiles the rules allow.
- No triple: highest tile starts without bonus (matches the published rules).
- Blocked game: draw. This deviates from the published rules, where the player with the fewest tiles left wins and scores the other players' tiles.

## Decisions for phase 2 (2026-11-13)

- **The rack is its own small scene**, drawn into the bottom strip of the canvas after the table (a second render pass). It keeps its size and place whatever the table camera does; tiles lie in one strip, up and down triangles alternating, on a walnut tray.
- **The picked-up tile is the ghost.** While dragging, the tile snaps to the cell under the pointer, a little above the table: green when it fits in the current rotation, red when not (also above an occupied cell). Releasing over the rack, or on a red cell, puts it back.
- **Cells light up** where the tile fits in some rotation, brighter where it fits in the current one, so rotating shows at once where a tile goes.
- **Rotation:** `R` or the wheel turn clockwise (`Shift+R` or wheel up: counterclockwise). The wheel zooms the camera again once the tile is put down.
- **Click instead of drag:** a click on a rack tile (no drag) carries it; the next click on the table places it, a click on the rack, or `Esc`, cancels. Works on a trackpad and on a tablet.
- **A tile that fits nowhere cannot be picked up**; the message says why. In the starting phase only the marked tiles (`AllowedStartTiles`) can be lifted.
- **Hot-seat:** after every move that ends a turn, a screen hides the rack until the next player presses "Toon mijn stenen". Drawing keeps the turn, so there is no screen then. The messages never name a drawn tile.
- Two players, "Speler 1" and "Speler 2", for now; phase 4 made the number and the names configurable.
- `?seed=N` plays the same deal every time; `?debug` exposes tile positions on `window.__triominos` for the smoke test.

## Decisions for phase 3 (2026-11-13)

- **One call per move.** C# sends `playMove({ events, board, rack })` once: the game events (`placed`, `drawn`, `passed`, `finished`, with Dutch score labels from C#), the board after the move and the rack to show afterwards (empty when the turn passes on). `deal({ counts })` does the same for the start of a round. Both return at once; the scene calls `AnimationsFinished` on C# when everything has settled. Timing, easing and sequencing are JavaScript only.
- **Durations in one place:** `scene/src/timing.ts` (`durations`, `easing`, `setAnimationSpeed`). The animation speed setting of phase 4 only has to call `setAnimationSpeed`. The fade of the screens between turns is the one CSS duration (`--screen-fade` in `app.css`).
- **Placing:** the tile starts at the ghost tile's position and height (so there is no jump), falls, bounces twice and is then handed to the board layer. The shadow map is redrawn once, when the tile lands, not per frame (a tile in the move layer does not cast a shadow while it falls); it is redrawn once more when the board is replaced, which also refits the shadow camera.
- **Points:** floating HTML lines (Web Animations API, no scene frames) above the tile: tile value, start, bridge and hexagon bonus, then the total. Bridge and hexagon lines are gold and add a burst of sparkles (one `Points` draw call; 12, 32 or 56 sparkles for low, medium, high) and a flash outline.
- **Drawing and dealing:** tiles fly from the pot (the `data-anchor="pot"` element) to their rack slot or, when dealing, face down to the player panel (`data-anchor="player-N"`). Flying tiles are drawn in a third pass over the whole canvas (also over the rack), positioned from canvas pixels through the table camera.
- **Turn transition:** after the move the rack slides out of the strip; the handover screen shows when the scene is done, and the next rack slides in when the screen closes. While the scene is busy the HUD keeps its old numbers (a copy, `HudState`), buttons are disabled and the scene refuses to pick up a tile or draw with the space bar.
- **Settlement:** after going out, the bonus floats up at the winner's panel; then each other rack slides in face up with what its tiles are worth, one rack at a time. A blocked game shows only the penalty.
- **Skipping:** a new move, `setBoard` or `setRack` while something plays makes it jump to its end state first, so the scene is never left half-way. A failing animation still reports `AnimationsFinished`, so the page cannot get stuck.
- **Not built:** the camera does not follow the move; the hexagon flash covers only the placed cell, not every cell of the hexagon.

### Phase 3 measured (2026-11-13)

Software rasteriser (SwiftShader) at 960x600 in headless Chrome, scene dev page, 40 tiles. This says nothing about GPU frame time (see the testing note in phase 1):

- Idle table: 18 draw calls on the last drawn frame, 0 frames drawn per second.
- During animations: at most 20 draw calls for the deal and the draw, 21 to 25 for a placement with bonus effects (the move layer, flash, sparkles and the flying-tile pass add a few).
- Not measured: frame time on a real GPU, and the first-use shader compile (the scene compiles the flight and sparkle shaders at startup to avoid it).

## Decisions for phase 4 (2026-11-13)

- **Screens are overlays on the one page**, not routes. The scene stays alive behind them, so the menu shows the empty table and no scene is rebuilt (and no routing under the GitHub Pages sub-path). `Home` holds which overlay is open (`None`, `Menu`, `Setup`, `Settings`); the handover and end screens of earlier phases show only while no overlay is open.
- **Main menu:** "Verder spelen" (only with a game in progress), "Nieuw spel", "Instellingen". The HUD has a "Menu" button for the same menu during a game; opening it puts a carried tile back in the rack. The space bar, drops and the Trekken and Passen buttons do nothing while an overlay is open.
- **Starting a game goes through player setup:** 2 to 4 players (`Scoring.MinPlayers`/`MaxPlayers`), a name per player (at most 16 characters) and a colour per player. Turn order is the order on the setup screen; who starts is still decided by the tiles. The setup remembers the last players, also for "Nog een spel" on the end screen, which starts again without the setup.
- **Names:** blank or repeated names (ignoring case and surrounding spaces) are refused. The rule lives in Core (`PlayerNameCheck`, used by `TriominosGame.Start`, with tests); the setup screen uses it to show the Dutch message.
- **Colours are presentation only:** six choices (robijn, saffier, amethist, koraal, turkoois, roze), each used by one player at most. They mark the name on the score panel, the handover screen and the end screen. The scene does not know them (flying tiles and tile backs have no player colour), and Core does not either.
- **Settings:** quality (as before, moved from the HUD) and animation speed (langzaam 0.6x, normaal, snel 2x, zeer snel 4x), which calls `setAnimationSpeed` of `timing.ts`. Both apply at once and last until the page is closed; they are not stored. Sound is a phase 5 setting.
- **No scoreboard across rounds:** a game is one round (see the decisions above), so the end screen shows the result of that round and the choice between another game and the menu.
- **Smoke test:** goes through the menu and the settings, exercises the setup (adding and removing players, blank and repeated names) and starts with the default two players and `?seed=7`, so the game it plays is the same as before; it also opens the menu during the game and returns.
