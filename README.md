# Triominos

The Triominos board game in the browser: Blazor WebAssembly (.NET 10) with a Three.js scene. Work in progress.

## Structure

- `src/Triominos.Core`: rules and game engine (UI-agnostic).
- `src/Triominos.Web`: Blazor front end; `scene/` holds the Three.js scene (TypeScript, Vite).
- `tests/Triominos.Tests`: unit tests for the rules.

## Build and run

Requires the .NET 10 SDK and Node 22.

```bash
dotnet build Triominos.slnx
dotnet test Triominos.slnx
dotnet run --project src/Triominos.Web
```

Pushes to `main` are deployed to GitHub Pages.

## Status

See [docs/plans/BlazorUi.md](docs/plans/BlazorUi.md) for the plan and [docs/tracking/Defects.md](docs/tracking/Defects.md) for known problems. Contributor and AI-assistant guidance is in [CLAUDE.md](CLAUDE.md).
