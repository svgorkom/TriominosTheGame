# Triominos

A WPF implementation of the Triominos board game for Windows (.NET 10). Work in progress.

## Structure

- `Core/`: UI-agnostic game logic (rules, scoring, engine, models).
- `UI/`: WPF front end (MVVM with custom-drawn board and piece controls).

## Build and run

```powershell
dotnet build Triominos.sln
dotnet run --project UI/Triominos.UI.csproj
```

Requires the .NET 10 SDK. The UI project builds on Windows only; `Core` builds anywhere.

## Status

The game is playable but incomplete. Known rule gaps and defects are tracked in [docs/tracking/Defects.md](docs/tracking/Defects.md). Contributor and AI-assistant guidance is in [CLAUDE.md](CLAUDE.md).
