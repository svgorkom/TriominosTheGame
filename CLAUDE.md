# CLAUDE.md

Triominos is a Windows-only WPF implementation of the Triominos board game (.NET 10). It is unfinished. Known problems are in [docs/tracking/Defects.md](docs/tracking/Defects.md).

## Commands

```powershell
dotnet build Triominos.sln
dotnet run --project UI/Triominos.UI.csproj    # Windows only
dotnet test Triominos.sln                      # no test project yet
```

`Core` targets `net10.0` and builds anywhere; `UI` targets `net10.0-windows`.

## Architecture

`UI → Core`; Core never references UI.

- **Core**: all game logic. `GameRules` (static: constants, scoring, placement validation), `GameEngine` (turn flow, raises events), models and interfaces.
- **UI**: WPF with hand-written MVVM. `GameViewModel` wraps the engine; the board and pieces are custom-drawn controls.
- **Grid:** a cell points up when `(row + col) % 2 == 0`.

## Rules

- Game logic lives in Core, never in controls, code-behind or view models.
- Keep the engine the single source of truth: the UI sends commands and renders state. Keep Core deterministic (inject `Random`) and free of UI and I/O, so online play can be added later without a rewrite. Do not build online features until they are asked for.
- Rules in `GameRules`/`GameEngine` get unit tests (xUnit in `tests/Triominos.Tests`, to be created with the first rule change). Fix bugs test-first.
- No unused constants, no hidden side effects (for example methods that mutate their arguments).
- Do not invent rules; if the intended Triominos rule is unclear, ask.
- Work on a branch (`feat/`, `fix/`), not on `main`. Do not merge; the user does that.
