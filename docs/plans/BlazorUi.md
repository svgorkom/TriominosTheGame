# Plan: Blazor + Three.js front end

Status: approved 2026-11-13. Phase 0 done (branch `feat/core-real-rules`); phase 1 prototype done (branch `feat/scene-table`); phase 2 built (branch `feat/board-and-play`), awaiting review; phase 3 built (branch `feat/animations`, stacked on phase 2); phase 4 built (branch `feat/screens`, stacked on phase 3); phase 5 built (branch `feat/sound`, stacked on phase 4); phase 6 built (branch `feat/polish`, stacked on phase 5); phase 7 built (branch `feat/loading`, stacked on phase 6); realism pass built (branch `feat/realism`), awaiting review.

## Goals

- A luxurious, 3D look: a real table, tiles with depth, light and shadow, smooth animations.
- Smooth: a stable 60 fps, no stutter, and every click or drag answered within one frame.
- Easy to use: drag a tile, see at once where tiles may go, one key or click per action. Like at a real table, the game does not say where a tile fits.
- The real Triominos rules (phase 0).
- Ready for online play later: the engine stays the single source of truth and the UI only sends commands. Online play itself is not part of this plan.

## Non-goals (for now)

- Online multiplayer, accounts. (A computer opponent was a non-goal here; it came later, see [ComputerOpponent.md](ComputerOpponent.md).)
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
- **Load time:** trimmed WebAssembly, Brotli compression, compressed textures (KTX2) and a loading screen. Target: playable within 3 seconds on broadband after the first visit, within 6 on the first visit. Phase 7 measured the download and built the loading screen; the textures are procedural, so KTX2 does not apply.
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
| 2. Board and play | Rack as 3D tiles in front of the camera; drag with a ghost tile that snaps to the nearest open cell; R or the mouse wheel rotates; open cells glow softly; Space draws; Esc cancels. Zoom, pan and rotate the camera. |
| 3. Animations | Dealing, tile drop with a small bounce, floating points, gold effect for a bridge or hexagon, turn transition, end-of-round settlement. |
| 4. Screens | Main menu, player setup (names, colors), end screen, settings (animation speed, quality). Built; see the phase 4 decisions. A scoreboard across rounds was dropped (one round per game). |
| 5. Sound and finish | Tile on wood, drawing, bonus, win. Built; see the phase 5 decisions. |
| 6. Persistence and polish | Settings and last players kept between visits, the camera follows a laid tile, the hexagon flash covers the whole hexagon, bUnit tests. Built; see the phase 6 decisions. |
| 7. Loading and touch | Published size measured, a loading screen, touch tried on a touch screen. Built; see the phase 7 decisions and the open items after them. |

## Testing

- Rules: xUnit in `tests/Triominos.Tests`, seeded `Random`, test-first for every defect in `docs/tracking/Defects.md`.
- Web: bUnit tests in `tests/Triominos.Web.Tests` for the component logic (turn flow, button states, setup, stored settings), added in phase 6. The 3D scene is checked by hand and with the profiler, not by automated tests; a smoke test (`tools/smoke`) plays the published site in headless Chrome.

## Decisions (2026-11-13)

- Double and triple hexagon bonuses are included.
- Bridge: the tile shares exactly one edge with placed tiles and its opposite corner touches another tile (the current Goliath/Foxmind wording). Filling a gap that matches two edges scores no bridge bonus.
- Drawing is allowed even with a playable tile (current Goliath/Foxmind rules); passing is only allowed after three draws or with an empty pool, even when a playable tile is left. Deliberate deviation (2026-11-14): a player may have overlooked a move, and a disabled Passen button would hint at it.
- One round per game.
- The starting player chooses the starting tile from the tiles the rules allow.
- No triple: highest tile starts without bonus (matches the published rules).
- Blocked game: draw. This deviates from the published rules, where the player with the fewest tiles left wins and scores the other players' tiles.

## Decisions for phase 2 (2026-11-13)

- **The rack is its own small scene**, drawn into the bottom strip of the canvas after the table (a second render pass). It keeps its size and place whatever the table camera does; tiles lie in one strip, up and down triangles alternating, on a walnut tray.
- **The picked-up tile is the ghost.** While dragging, the tile snaps to the open cell under the pointer, a little above the table. It is never tinted, so colour gives no hint. Releasing over the rack, or on a cell where it does not fit, puts it back.
- **Open cells light up**: every empty cell that shares an edge with a placed tile (at the start: the origin), all the same way, whether the tile fits or not. This gives the player no hint, as at a real table; C# decides on the drop (`TriominosGame.OpenCells`).
- **Rotation:** `R` or the wheel turn clockwise (`Shift+R` or wheel up: counterclockwise). The wheel zooms the camera again once the tile is put down.
- **Click instead of drag:** a click on a rack tile (no drag) carries it; the next click on the table places it, a click on the rack, or `Esc`, cancels. Works on a trackpad and on a tablet.
- **A tile that fits nowhere can still be picked up**: the open cells light up as always and the drop is refused, so the player gets no hint. Draw or pass is the way out. In the starting phase the marked tiles (`AllowedStartTiles`) are the only ones that can be laid; the message says so.
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
- **Not built here:** the camera did not follow the move and the hexagon flash covered only the placed cell; both came in phase 6.

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
- **Settings:** quality (as before, moved from the HUD) and animation speed (langzaam 0.6x, normaal, snel 2x, zeer snel 4x), which calls `setAnimationSpeed` of `timing.ts`. Both apply at once. They were not stored between visits until phase 6. Sound is a phase 5 setting.
- **No scoreboard across rounds:** a game is one round (see the decisions above), so the end screen shows the result of that round and the choice between another game and the menu.
- **Smoke test:** goes through the menu and the settings, exercises the setup (adding and removing players, blank and repeated names) and starts with the default two players and `?seed=7`, so the game it plays is the same as before; it also opens the menu during the game and returns.

## Decisions for phase 5 (2026-11-13)

- **Sounds are synthesised with Web Audio** (`scene/src/sound.ts`), like the textures of phase 1: the CC0 sound libraries cannot be reached from the build environment, nothing is downloaded and the page stays as small as before. The only buffer is a second of noise made at the first sound.
- **Five sounds:** a tile on wood (filtered noise click, a low knock and a short wood tick, pitch varied a little each time), a tile sliding out of the pot (a swish as long as the flight), dealing (one flick per tile), a gold chime for a bridge or hexagon (one more note per gold line) and a bell run with a chord when a player goes out. A blocked game and a pass are silent: the rules of the plan do not call for a sound there.
- **The director plays them** at the animation moments, in JavaScript only: the fall and both bounces of a placed tile (softer each time), the burst of a bonus, the start and end of a flight, the deal, the going-out bonus. C# does not know about sounds apart from the setting. Sounds that depend on duration read `timing.ts` through `scaled()`, so they follow the animation speed. When an animation is skipped its sounds would fire at once, so each sound has a minimum gap.
- **Audio starts at the first click or key press** anywhere on the page (browsers require a gesture); the main menu is a natural first click. Sounds asked for earlier are dropped.
- **Settings:** sound on or off and a volume from 0 to 100 (squared to a gain, so the slider follows how loud it sounds). Changing the volume or turning the sound on plays one knock at the new level. Like the other settings it is stored from phase 6 on. `Home` sends the settings to the scene once after it starts.
- **Tests:** the scene logs every sound that is asked for and not muted (`sound:land`, `sound:draw`, `sound:deal`, `sound:bonus`, `sound:win`); the smoke test checks that the deal, drawing and every landing log a sound, and that nothing is logged with the sound off. The audio context itself may be suspended in headless Chrome, so what is audible is not tested.
- **Not checked by ear:** the sounds were written and tuned by reasoning and checked for errors in headless Chrome, but nobody has listened to them yet. Pitches, levels and durations are constants in `sound.ts` and are meant to be adjusted by listening.

## Decisions for phase 6 (2026-11-13)

- **Storage is browser local storage through a small module** (`wwwroot/js/storage.js`, `get` and `set`, both swallowing errors) behind `IPreferenceStore`. `Preferences` (in `Storage/`) reads and writes it; `Home` loads in `OnInitializedAsync`, before the scene is created, so the stored quality is the one the scene starts with.
- **One key per value**, so a damaged value falls back to its own default and the others stay: `triominos.quality`, `.speed` (enum names), `.sound` (`on` or `off`), `.volume` (a whole number from 0 to 100). Defaults: medium, normal, on, 60.
- **Players are one key** (`triominos.players`): `Colour:name` items separated by commas, names escaped with `Uri.EscapeDataString`. Not valid (fewer than 2 or more than 4 players, a blank or repeated name, a name over 16 characters or with surrounding spaces, an unknown or repeated colour) means the default players, whole. No JSON: it needs no source generator to survive trimming.
- **When it is saved:** settings at once when one changes (and the whole set is written, which is four small keys); players when a game starts, so "Nog een spel" and the next visit offer the same players.
- **Smoke test:** it starts a fresh browser, so storage is empty and the defaults apply. At the end it reloads the page and checks that the settings and the two players come back, then damages the volume and the player list in local storage, reloads and checks that those two fall back to their defaults while the other settings stay.
- **The camera follows the last tile laid** (`planFollow` in `main.ts`, played by the director at the end of a move, after the points have faded and the rack has slid away, so the next player sees the tile). It only slides the camera and its target (what the left button does when panning): view angle and zoom stay as the player set them. It moves only when the tile's corners fall outside the safe area of the canvas (sides 15% of the width, 80 to 240 px; top 20% of the height, 90 to 180 px; bottom above the rack and the buttons) and then by the least distance that brings the tile inside, in 0.5 s (`durations.follow`, scaled by the animation speed). When the player takes hold of the camera (the controls' `start` event: a click, drag or wheel) the move stops where it is. No per-frame interop; C# does not know about it. The felt limits of `clampTarget` still apply.
- **The hexagon flash covers every cell of every hexagon the tile completed**, each cell once (up to 13 for three hexagons, so the flash outline mesh holds 16). Core answers which: `GameBoard.HexagonCenters(cell)` returns the corners of a cell around which all six cells are filled; `EvaluateBonus` counts them, and `MoveViews` turns them into the cells in `PlacedEventView.Hexagon`. A bridge still flashes only the tile that made it.
- **Web tests** are in `tests/Triominos.Web.Tests` (bUnit 2, xUnit): the player setup (2 to 4 players, blank and repeated names, colours), the stored preferences (parsing and fallbacks), and the table page `Home` with a stand-in for JavaScript. The page tests play the scene's part: they call back into the page the way the scene does (drop a tile, press the draw key, report that the animations are done), read the running game through `Home.Session` (internal, visible to the tests) and check the screens and the Trekken and Passen buttons at every step, including a whole game played to the end screen by the simplest rule (lay, else draw, else pass). The tests need neither Node nor the scene: `dotnet test tests/Triominos.Web.Tests -p:SkipSceneBuild=true` builds Web without it. The project does not set that property itself (`AdditionalProperties` on the project reference builds Web a second time next to the solution's own build, and the two fail on the same files in `obj`); CI builds the whole solution, scene included, and runs the tests after it. `PageTitle` is stubbed (it needs a `HeadOutlet`).

## Decisions for phase 7 (2026-11-13)

### Published size

CI publishes twice (`ci.yml`): the normal Release publish and one with `-p:PublishTrimmed=false`, and `tools/size/measure.mjs` prints the size per file type (raw, gzip level 9, Brotli quality 11) next to the `.gz` and `.br` files that `dotnet publish` wrote itself. The numbers are in the log and the job summary of every run, so a later change shows up there.

Measured on `feat/loading` (KiB; the `.br` column equals the Brotli column, so the publish writes the best Brotli):

| type | files | raw | gzip | Brotli |
|---|---:|---:|---:|---:|
| WebAssembly, assemblies | 33 | 3446 | 1390 | 1154 |
| WebAssembly, .NET runtime (`dotnet.native.wasm`) | 1 | 2932 | 1167 | 954 |
| `scene.js` (Three.js and the scene) | 1 | 750 | 170 | 137 |
| other JavaScript (Blazor, runtime loader, `storage.js`, `boot.js`) | 6 | 445 | 123 | 107 |
| fonts (woff2, already compressed) | 6 | 138 | 138 | 138 |
| css, html, svg, licences | 6 | 31 | 10 | 8 |
| **site** | 53 | 7742 | 2999 | 2497 |

What a first visit actually downloads (the files requested by a fresh browser until the menu shows, 48 of the 53): raw 7.5 MiB, gzip 2.9 MiB, **Brotli 2.4 MiB**. That is about 2 s at 10 Mbit/s and 4 s at 5 Mbit/s for the transfer alone, before the runtime starts; the 6 s target for a first visit is reachable on broadband, tight on a slow connection. The three biggest files are `dotnet.native.wasm` (38% of the Brotli total), `System.Private.CoreLib.wasm` (540 KiB) and `scene.js`.

- **Trimming is on** (Release publish trims by default). Without it the assemblies alone are 4.9 MiB Brotli instead of 1.1 MiB; trimming saves 61% of the site in Brotli.
- **Brotli and gzip files are written** by the publish next to every file (`.br`, `.gz`); the fonts are not, because woff2 does not compress further.
- **What GitHub Pages compresses is not known yet.** The sandbox cannot reach the site and a web search found nothing reliable about Pages, so this is not stated here as fact. `deploy.yml` now ends with a step that requests the main files with `Accept-Encoding: br, gzip`, `gzip` and `identity` and prints `Content-Encoding` and `Content-Length` of each (information only; the step cannot fail the deploy). Read it after the first deploy. If Pages serves the files uncompressed or only gzip, a first visit downloads 7.5 MiB or 2.9 MiB instead of 2.4 MiB; the usual fix is to fetch the `.br` files in JavaScript and decode them (Blazor's `loadBootResource` plus a Brotli decoder), which is only worth building if the step shows it is needed.
- **Changed, because it demonstrably helps:**
  - `InvariantGlobalization`: no ICU data is published or downloaded. A browser used to fetch one of three files, depending on its language: 140 KiB, 217 KiB or 243 KiB Brotli (the smallest is for English, German, French, Spanish and Italian, as far as I know). Nothing in the game depends on the culture (names are compared ordinally, the UI text is fixed Dutch, numbers are written with the invariant culture); the 62 Core tests, the 59 Web tests and the smoke test pass unchanged, and `loading.mjs` fails the build if an ICU file is requested.
  - `<link rel="modulepreload" href="js/scene/scene.js">`: the scene used to be requested only after Blazor had started and a component had asked for it, so it downloaded after the WebAssembly. Now it is the fifth request of the page, before the first `.wasm` file (twelfth), and it is fetched once (the import by `TableScene` uses the same module map). `loading.mjs` checks the order and the single fetch.
  - Preload of the title font (Cormorant Garamond 700) and the body font (Inter 400) so the loading screen does not flash in a fallback font.
- **Not changed** (no demonstrable gain, or a bigger decision): AOT (bigger download), lazy loading of assemblies (nothing optional to defer), subsetting fonts (already Latin only, 138 KiB in total), fingerprinting `app.css`, `boot.js` and `scene.js` (see the open items).

### Loading screen

- `index.html` shows a Dutch loading screen in the style of phase 1 (felt in a vignette, a gold-edged ivory tile as mark, "Triominos" in Cormorant Garamond, Inter for the text). No library; the markup is in the page, the styles in `app.css`.
- **Progress** is drawn by CSS alone: Blazor sets `--blazor-load-percentage` and `--blazor-load-percentage-text` on the page while it downloads, the bar and the percentage read them. `loading.mjs` checks on the published site that Blazor does set the variable. The percentage counts files, not bytes, so it jumps while `dotnet.native.wasm` downloads.
- **`js/boot.js`** starts Blazor by hand (`autostart="false"`) so a failed start can be shown. Failure shows a message and a "Opnieuw proberen" button (reload) instead of the bar: Blazor script not loaded, `Blazor.start()` rejected, an unhandled error or rejection while the screen is still there, or no WebAssembly in the browser. Blocking the `.wasm` files raises several errors; the message is shown once.
- **Slow load:** when the percentage has not moved for 20 s a line says so. It disappears when the percentage moves again.
- **No JavaScript:** the `no-js` class on `<html>` hides the bar and the promise of loading; a `noscript` text says the game needs JavaScript.
- Blazor replaces the content of `#app` when it has started, so the screen needs no removal; `boot.js` touches it only while it is still there. Respects `prefers-reduced-motion` (the tile only bobs without it).
- **Not handled:** a browser that has WebAssembly but cannot create the scene (no WebGL). That would fail after the loading screen, in the app; not tried (see the open items).
- Tested in CI (`tools/smoke/loading.mjs`, on the published site): the screen shows while the runtime is slowed down, the percentage variable is set, the menu replaces it; the message shows with the `.wasm` files blocked, with `blazor.webassembly.js` blocked and without `WebAssembly`; the no-JavaScript text; one fetch of the scene before the first `.wasm`; no ICU file. The look was checked in headless Chrome screenshots (wide and phone width) against a stand-in for Blazor; nobody has looked at it on a real screen.

### Touch

Tried with Chrome's touch emulation and real touch events through the DevTools protocol, first against the scene's dev page, then on the published site (`tools/smoke/touch.mjs`, in CI).

- **Works:** tap a rack tile to carry it and tap the table to place it; drag a tile with one finger; the Trekken button by tap; one finger pans the camera; two fingers pinch (zoom) and twist (orbit). The tile cannot be dropped on a cell where it does not fit; a tap there leaves it in the hand.
- **Fixed:** a tap was told from a slow drag by the time the handler ran. On a slow frame (software rendering at 1280x800 with the CPU throttled 4x, a stand-in for a weak tablet; the touch test in CI failed on it too) the release was handled more than 400 ms after the touch and the tap counted as a drag that ended on the rack: the tile went back, 0 of 4 taps carried a tile. It now uses the times of the events themselves (`e.timeStamp`), 4 of 4. This is the only change to the scene's input.
- **No way to rotate on touch.** `R` and the wheel are the only rotations, and a tablet has neither. A tile whose fitting rotation is not the one it was picked up in cannot be placed there. This is a design choice, so nothing is built; proposals, in the order I would pick them:
  1. **A "Draai" button** in the action bar while a tile is carried: one tap turns the tile one step clockwise. It also helps mouse users who do not know `R`, and the Phase 1 HUD already planned a rotate button. Cost: one Razor button and one interop call into the scene (`rotateHeld`).
  2. **Rotate on a tap at a cell that fits in another rotation**: if the tile does not fit at the tapped cell in its current rotation but does in another, turn it and place it (or turn it and wait for a second tap). It removes the need to rotate for most moves, on mouse as well, but changes the phase 2 rule that green and red mean "fits in the current rotation".
  3. **Two-finger twist on the carried tile**, the natural gesture, but it competes with the camera's twist (orbit) and with the second finger being read as a drop attempt (see below).
- **Also seen, not changed:**
  - A second finger on the canvas while a tile is carried counts as a click on the table (a drop attempt) and is swallowed, so the camera pinch does not start. Harmless (a drop only happens on a green cell), but it blocks a pinch while carrying.
  - After a tap to carry, the green or red ghost tile only follows pointer moves, so on touch it does not show where the tile would land until the finger touches down again; the glowing cells show where it fits.
  - The hint text under the table still names `R` and the wheel. It is the same on touch.

### Tests added

- `tools/smoke/loading.mjs` and `tools/smoke/touch.mjs`, as CI steps after the smoke test. `tools/size/measure.mjs` prints the size report and, with `--loaded`, the sum for a first visit.
- No unit tests: the changes are static page content and scene input, which the plan checks with the smoke tests.

### Open items after phase 7

What stands between this and a first version that can be played and shared:

- Check on a real GPU: frame time against the budget, the first-use shader compile and the real loading time over a real network (phase 7 measured sizes, not time). This includes the camera move after a laid tile, which has only been seen in software rendering.
- Read the compression step of `deploy.yml` after the first deploy (see the published size section) and decide whether the `.br` files need to be fetched by hand.
- Listen to the sounds and look at the loading screen, menu, setup and settings screens, which were only tested by script.
- Decide how a carried tile is rotated on touch (the three proposals above).
- Quality is not picked automatically from a measurement (phase 1); the default is "Middel". Left as it is.
- Digits point to their corner, like on real tiles; the alternative is all digits upright. Left as it is.
- GitHub Pages has to be switched on, and the sub-path (`<base href>`) checked on the live site; the loading screen's files (`js/boot.js`, the fonts) are relative to it.
- Stored settings can only be tried in a real browser by the smoke test and by hand; a browser that blocks local storage simply starts from the defaults every time.
- Found in phase 7 and left alone because it did not fit:
  - Not tried: what the page shows when WebGL is missing or the scene cannot start. The loading screen only covers the start of Blazor; there is no message for a failing scene.
  - `app.css`, `boot.js`, `storage.js` and `scene.js` have no fingerprint in their name, so a returning visitor may be served a stale copy for as long as the host's cache allows (to be checked in the headers of the deploy step), and a new deploy can pair an old `index.html` with new files for a moment. The `_framework` files are fingerprinted already.
  - `dotnet.native.wasm` is 38% of the download and cannot be trimmed from here; only a newer runtime or dropping runtime features would shrink it.
  - A second finger while a tile is carried (see Touch).

## Decisions for the realism pass (2026-11-14)

A review of the scene for realism gave a list; these five were built. Recorded sounds were left for later.

- **A tile lands on felt, not on wood.** `Sound.land` is now a dull thud (low-passed noise and a short low tone). A tile that comes down against other tiles also clicks, sharper with every tile it touches; the director counts the edge neighbours on the board. This replaces the "tile on wood" of phase 5. Like the other sounds it has not been listened to.
- **Tiles lie a little out of line.** A tile on the table is turned up to one degree and shifted up to 0.006 units, the same way every time for the same tile (`variation.ts`, seeded by its three values). The amounts stay inside the gap that `TILE_SCALE` leaves, so neighbours never overlap. Tiles in the rack still lie straight. The glow and the hexagon flash stay on the exact cells.
- **Every tile has its own veins.** A tile shows a window of 45% of the ivory texture, moved and turned per tile through an instanced attribute and a small patch of the vertex shader (`showOwnGrain` in `tiles.ts`). The texture is drawn at twice the size (1024 px) to keep it sharp.
- **The carried tile follows the pointer** instead of jumping from cell to cell. It floats above the felt, eases onto a cell where it fits in its current rotation, and turns smoothly when it is rotated or comes over a cell that points the other way. Green and red still mean "fits here in this rotation" (phase 2). `Interaction.step` moves it once per drawn frame; frames are only drawn while it is on its way.
- **Its shadow is one blurred triangle** on the surface below (`shadow.ts`), moved along the direction of the sun and fainter the higher the tile is. A shadow map redrawn every frame of a drag was rejected: it costs a 2048 or 4096 pixel depth pass per frame, does not reach beyond the box fitted around the tiles, and is off at low quality.
- **Felt:** short fibres drawn over the noise (512 px, the same scale in both directions) and a sheen layer, which lights the cloth up at a low angle. The felt is the largest surface on screen and is now a physical material with sheen; its cost on a real GPU has not been measured.
- **Checked** in headless Chrome with the software rasteriser on the scene dev page: a tile carried, rotated, settled on a cell and laid, no errors. Not checked: frame time on a GPU, the .NET tests and the smoke tests of the published site (no .NET SDK where this was built; no C# changed).

## Decisions for the hint (2026-11-14)

A player who is stuck can pay for one suggestion. Like the computer opponent, it was not part of the original plan; the earlier rule that the game does not say where a tile fits still holds for everyone who does not press the button.

- **One suggestion, not a list.** A hint is the move the normal computer level would make with the player's own tiles (`ComputerPlayer.Decide`): a tile and a place, or "draw" or "pass" when nothing fits. Only the best-scoring move is shown; the player may ignore it.
- **It costs 10 points** (`Scoring.HintPenalty`, the same as a pass) **and a player may take 3 per game** (`Scoring.MaxHintsPerGame`), so at most 30 points; less than one bridge bonus. Rationale: an average tile is worth 7.5 points, a draw costs 5 and a pass 10. A hint cheaper than a draw would beat drawing for any player who is unsure. At 10 it only pays off when the alternative is a draw, a pass or a clearly worse move.
- **Always charged,** also when the answer is "draw" or "pass": that answer ("nothing fits") is information too. A second hint in the same turn is refused while one is showing, so no one pays twice for the same advice. After a draw the old hint is gone (the new tile may change the best move) and a new one costs again.
- **Core only counts.** `TriominosGame.Hint()` takes the penalty and records it per player (`Player.HintsUsed`); the advice comes from `PlaySession.RequestHint`, which uses the existing AI. Nothing changes in the turn order, the racks or the board. Computers never take hints.
- **How it shows:** the suggested tile is singled out in the rack like a marked starting tile, the target cell gets a golden outline (`setHint` in the scene, one extra `PlacementGlow`), and the message says what to do, including the one turn of the tile that is needed (`R` or `Shift+R`). Everything is cleared when the player makes a move or draws. The camera does not move to the cell.
- **Not built:** a setting to switch hints off for a table that wants the strict rules; a hint level that only tells which tile; hints in the starting phase are possible but only cost points (the choice there is between the allowed tiles).
- **Not run:** no .NET SDK where this was built, so the C# and its tests (`GameTests`, `PlaySessionHintTests`, `HomeTests`) have not been compiled or run. The scene change was checked with `tsc` and Vitest.
