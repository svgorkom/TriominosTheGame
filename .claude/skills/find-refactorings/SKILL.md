---
name: find-refactorings
description: Analyze the codebase for refactoring opportunities (DRY violations, oversized classes, mixed concerns, WPF/MVVM and .NET best practices) and produce a prioritized plan. Analysis only.
disable-model-invocation: true
allowed-tools: Read Grep Glob
---

Analyze `$ARGUMENTS` (default: the whole solution) for improvement opportunities in code quality and maintainability, following .NET and WPF/MVVM best practices and this repository's architecture (see CLAUDE.md). **Analysis only: do not change code**, and per CLAUDE.md present a design before any implementation.

## Analysis

1. **Structure overview:** directory structure, main patterns, key dependencies.
2. **DRY violations:** duplicated logic (for example the "try all three rotations" loop that appears in several places in `GameEngine` and `GameRules`), duplicated drawing code in the custom controls, repeated XAML styling.
3. **Size and complexity:** classes over ~200 lines, deep nesting, mixed concerns (rules in the UI, drawing in view models).
4. **.NET and WPF/MVVM practices:** async/await use, event-handler cleanup, binding and command patterns, logic in code-behind, replacing the hand-written `RelayCommand` and `ViewModelBase` only if a framework clearly pays off.
5. **Layering:** `UI → Core` only; rules only in `Core`; interfaces that are really implementation-bound (casts to `Piece`).
6. **Also flag:** performance issues on the board hot paths (`GetValidPlacements` clones and validates per cell), dead code and unused constants, missing test coverage.

## Output

Per finding:

**[Category]** · Priority: High/Medium/Low
**File:** `path/to/file.cs` (with line numbers)
**Issue:** brief description
**Proposed solution:** concrete refactoring
**Impact:** benefits
**Files to create/modify:** list

Then a prioritized plan: **Quick wins**, **Medium effort**, **Large refactors**.

## Constraints

- Preserve game behavior unless the rules spec says otherwise; add characterization tests before refactoring untested code.
- Be thorough but practical.
