# CLAUDE.md

Triominos is a browser implementation of the Triominos board game: Blazor WebAssembly (.NET 10) with a Three.js scene. It is unfinished; the plan is [docs/plans/BlazorUi.md](docs/plans/BlazorUi.md) and known problems are in [docs/tracking/Defects.md](docs/tracking/Defects.md).

## Commands

```bash
dotnet build Triominos.slnx                                   # also builds the scene (npm ci + Vite)
dotnet test Triominos.slnx
dotnet test --filter "FullyQualifiedName~GameTests"           # one class
dotnet test tests/Triominos.Web.Tests -p:SkipSceneBuild=true   # bUnit tests of the page and the screens, without building the scene (no Node needed)
dotnet run --project src/Triominos.Web                         # play in the browser
cd src/Triominos.Web/scene && npm run watch                    # rebuild the scene on change; run the app with -p:SkipSceneBuild=true
cd src/Triominos.Web/scene && npm run dev                      # scene alone with a random board: http://localhost:5173/dev/index.html (?quality=low|medium|high&tiles=N)
```

Requires the .NET 10 SDK and Node 22.

## Architecture

`Web → Core`; Core never references Web.

- **Core** (`src/Triominos.Core`): all rules. `TriominosGame` is the single source of truth: read-only state, three commands (`Place`, `Draw`, `Pass`) that return a `MoveResult` with `GameEvent`s. `GameBoard` validates placements and evaluates bonuses. Randomness only via the injected `Random`.
- **Board model:** an unbounded triangle grid. A cell points up when `(Row + Col) % 2 == 0`. Corner values are stored per `Vertex`, so a placement is valid when every corner equals the value already at that vertex (diagonal contact included) and the cell shares an edge with a placed tile. Corner and edge numbering is documented on `Cell`.
- **Web** (`src/Triominos.Web`): Razor components for menus, HUD and dialogs; the 3D scene is TypeScript in `scene/`, bundled by Vite into `wwwroot/js/scene/` and loaded with JS interop.
- **Interop is event-sized:** C# calls the scene a few times per move; the render loop, animations, camera and drag run in JavaScript only. Never drive the scene per frame from C#.

## Rules

- The UI language is Dutch. Write user-facing text in Dutch; code, comments and docs stay English.
- Game logic lives in Core, never in Razor components or the scene. The scene renders and reports input; the UI sends commands to `TriominosGame`.
- Keep Core deterministic and free of UI and I/O, so online play can be added later. Do not build online features until they are asked for.
- Rules get unit tests in `tests/Triominos.Tests` (xUnit). Fix bugs test-first. Component logic (turn flow, button states, setup, stored settings) gets bUnit tests in `tests/Triominos.Web.Tests`.
- Performance: render on demand, no per-frame interop, measure with the browser profiler after visual changes (target 60 fps, frame under 10 ms).
- No unused constants, no hidden side effects (methods that mutate their arguments).
- Do not invent rules; the rules and the decisions that deviate from them are in the plan. If unclear, ask.
- Work on a branch (`feat/`, `fix/`), not on `main`. Do not merge; the user does that.
