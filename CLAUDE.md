# CLAUDE.md

Triominos is a browser implementation of the Triominos board game: Blazor WebAssembly (.NET 10) with a Three.js scene. It is unfinished; the plan is [docs/plans/BlazorUi.md](docs/plans/BlazorUi.md) and known problems are in [docs/tracking/Defects.md](docs/tracking/Defects.md).

## Commands

```bash
dotnet build Triominos.slnx                                   # also builds the scene (npm ci + Vite)
dotnet test Triominos.slnx
dotnet test --filter "FullyQualifiedName~GameTests"           # one class
dotnet test tests/Triominos.Web.Tests -p:SkipSceneBuild=true   # bUnit tests of the page and the screens, without building the scene (no Node needed)
dotnet run --project src/Triominos.Web                         # play in the browser
cd src/Triominos.Web/scene && npm test                         # Vitest: pure scene logic (geometry, timing, variation); no browser needed
cd src/Triominos.Web/scene && npm run watch                    # rebuild the scene on change; run the app with -p:SkipSceneBuild=true
cd src/Triominos.Web/scene && npm run dev                      # scene alone with a random board: http://localhost:5173/dev/index.html (?quality=low|medium|high&tiles=N)
cd server && npm test                                          # Vitest: the rules of a room (online play); `npm run typecheck` checks the types
cd server && npm run dev                                       # the Worker with the published site (dotnet publish -o publish first) and the rooms; `npm run e2e` plays clients against it
```

Requires the .NET 10 SDK and Node 22.

## Architecture

`Web → Core`; Core never references Web.

- **Core** (`src/Triominos.Core`): all rules. `TriominosGame` is the single source of truth: read-only state, three commands (`Place`, `Draw`, `Pass`) that return a `MoveResult` with `GameEvent`s. `GameBoard` validates placements and evaluates bonuses. Randomness only via the injected `Random`.
- **Board model:** an unbounded triangle grid. A cell points up when `(Row + Col) % 2 == 0`. Corner values are stored per `Vertex`, so a placement is valid when every corner equals the value already at that vertex (diagonal contact included) and the cell shares an edge with a placed tile. Corner and edge numbering is documented on `Cell`.
- **Web** (`src/Triominos.Web`): Razor components for menus, HUD and dialogs; the 3D scene is TypeScript in `scene/`, bundled by Vite into `wwwroot/js/scene/` and loaded with JS interop.
- **Online play** (see [docs/plans/OnlinePlay.md](docs/plans/OnlinePlay.md)): every screen runs the whole game from one seed; the Worker in `server/` only seats players and orders the actions, and knows no rules. `GameAction` (Core) is a command as a value; `PlaySession` has a local seat and takes the actions of the others through `ApplyRemote`; `OnlineClient` (Web) is the connection. Changing the protocol means changing `server/src/room.ts` and `Online/OnlineProtocol.cs` together and bumping the version.
- **Interop is event-sized:** C# calls the scene a few times per move; the render loop, animations, camera and drag run in JavaScript only. Never drive the scene per frame from C#.

## Rules

- The UI language is Dutch. Write user-facing text in Dutch; code, comments and docs stay English.
- Game logic lives in Core, never in Razor components or the scene. The scene renders and reports input; the UI sends commands to `TriominosGame`.
- Keep Core deterministic and free of UI and I/O: online play depends on it. The same seed and the same actions must always give the same game, so no clock, no `Random.Shared` and no hidden state in Core.
- Rules get unit tests in `tests/Triominos.Tests` (xUnit). Fix bugs test-first. Component logic (turn flow, button states, setup, stored settings) gets bUnit tests in `tests/Triominos.Web.Tests`. Pure scene logic (no WebGL, no DOM) gets Vitest tests in `src/Triominos.Web/scene/test`.
- Performance: render on demand, no per-frame interop, measure with the browser profiler after visual changes (target 60 fps, frame under 10 ms).
- No unused constants, no hidden side effects (methods that mutate their arguments).
- Do not invent rules; the rules and the decisions that deviate from them are in the plan. If unclear, ask.
- Work on a branch (`feat/`, `fix/`), not on `main`. Do not merge; the user does that.
