# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Triominos is a Windows-only WPF desktop implementation of the Triominos board game (.NET 10). It is unfinished: the rules are only partly implemented and the UI is rough. Known problems are listed in [docs/tracking/Defects.md](docs/tracking/Defects.md).

## Commands

```powershell
dotnet build Triominos.sln                                   # whole solution (CI uses -c Release)
dotnet run --project UI/Triominos.UI.csproj                  # run the game (Windows only)
dotnet test Triominos.sln                                    # all tests (no test project exists yet)
dotnet test --filter "FullyQualifiedName~GameRulesTests"     # one class, once a test project exists
```

- `.editorconfig` prefers `var` and file-scoped namespaces. Existing code still uses block-scoped namespaces and explicit types; migrate a file only when you are editing it anyway.
- `Core` targets plain `net10.0` and builds on any OS. `UI` targets `net10.0-windows` and only builds on Windows.

## Architecture

Dependency flow: `UI → Core`. Core never references UI.

- **Core** (`Core/`): UI-agnostic game logic.
  - `Interfaces/` hold the contracts (`IGameEngine`, `IGameBoard`, `IPiece`, `IPlayer`, `IDrawPile`).
  - `Models/` hold the implementations (`Piece`, `PlacedPiece`, `Player`, `GameBoard`, `DrawPile`).
  - `GameRules` is a static class with all constants, scoring and placement validation.
  - `Services/GameEngine` runs the game flow and raises events (`Core/Events/GameEvents.cs`) that the UI listens to.
- **UI** (`UI/`): WPF with hand-written MVVM (`ViewModelBase`, `RelayCommand`, no framework). `GameViewModel` wraps the engine. The board, the piece selector and the triangles are custom-drawn controls in `UI/Controls/`.
- **Board geometry:** triangles on a row/column grid. A cell points up when `(row + col) % 2 == 0` (`GameRules.IsPointingUp`). Orientation is derived from the cell, never chosen by the player.

## Rules

- **Game logic lives in Core**, never in code-behind, controls or view models. The UI only shows state and forwards commands to `IGameEngine`.
- **Every rule change starts with the rules spec.** `docs/specs/game-rules.md` does not exist yet; creating it (numbered, testable requirements, `GR-xxx` IDs) is the first task. Until then, state which rule you are changing and why, and ask for confirmation before changing scoring or placement behavior.
- **Tests for rules.** Business rules in `GameRules` and `GameEngine` get unit tests. Inject `Random` (the engine already accepts one) so tests are deterministic. A test project does not exist yet; add `tests/Triominos.Tests` (xUnit) with the first rule change.
- **Keep `GameRules` constants truthful.** Remove a constant or implement it; do not leave unused ones.
- **Avoid hidden side effects.** For example `GameRules.ValidatePlacement` currently mutates the piece it validates (a known defect); do not add more of this.
- **Keep docs in sync:** the README, the rules spec and [Defects.md](docs/tracking/Defects.md).
- **Zero new technical debt:** no unexplained TODOs, shortcuts or "temporary" workarounds.
- **Facts only.** Do not invent rules or system details; state assumptions. Official Triominos rules differ from this implementation in places; check the spec before assuming either is right.

## Workflow

For a feature, a rule change or a non-trivial fix:

1. Restate the problem and the affected rules; update the rules spec and wait for approval of the changed requirements.
2. Present a short design: layers touched, alternatives and why rejected, failure modes, UI impact, tests to add. Wait for approval before implementing.
3. Implement on a dedicated branch (`feat/<topic>`, `fix/<topic>`, `test/<topic>`), never directly on `main`.
4. Bug fixes: write a test that fails because of the bug first, then fix, then run the full suite.
5. Before the PR: run an independent review of the branch with a subagent that has no prior context, following [.claude/skills/review-changes/SKILL.md](.claude/skills/review-changes/SKILL.md) in branch mode. Fix Critical and High findings; list the rest in the PR.
6. Open a pull request against `main` with [.github/pull_request_template.md](.github/pull_request_template.md). Check CI afterwards and report failures honestly. Do not merge; the user does that.
