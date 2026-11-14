---
name: review-changes
description: Thorough review of all uncommitted and staged changes, or of the whole branch with the argument `branch` (correctness, rules, design, tests, docs). Review only, no code changes.
argument-hint: "[branch]"
disable-model-invocation: true
allowed-tools: Read Grep Glob Bash(git status:*) Bash(git diff:*) Bash(git log:*)
---

Act as a senior C#/.NET engineer reviewing all changed code in this repository. **Review only: do not write or change code.**

## Input

The scope depends on `$ARGUMENTS`:

- **No argument:** the uncommitted work. Get the changes with `git status` and `git diff HEAD` (staged and unstaged). Include untracked files from `git status`.
- **`branch`:** everything the current branch adds to `main`, as it would appear in a pull request. Get the changes with `git diff main...HEAD` and the intent with `git log main..HEAD`. Also include uncommitted changes from `git status` and `git diff HEAD`, and say in the summary that they are not yet committed.

Review **every** changed file, however small. For each, also read the surrounding code, related types and callers (use Grep).

## Checklist

Assess each item for every changed file; skip an item only if clearly not applicable.

1. **Correctness:** logic for normal and edge inputs; grid coordinates and orientation; rotation; return values and nullables; silent state corruption.
2. **Game rules:** changed scoring, placement or turn behavior is intended, covered by tests, and listed in the PR summary.
3. **Layering:** `Web → Core` only. `Core` has no UI references and does no I/O. No game rules in Razor components, code-behind, `PlaySession` or the TypeScript scene.
4. **Memory and resources:** `IDisposable`/`IAsyncDisposable` disposed (JS object references, the `TableScene`); event handlers and `DotNetObjectReference`s released; the scene frees its Three.js resources.
5. **Thread safety and async:** shared mutable state; Blazor calls that need `InvokeAsync`; no `.Result`/`.Wait()`; no fire-and-forget (`_ = ...`) that can outlive a replaced session or leave the page stuck busy.
6. **Error handling:** no empty or swallowed `catch`; input validated at the engine boundary; nulls handled.
7. **Side effects and design:** methods that mutate their arguments; casts assuming a concrete type behind an interface; SOLID; God objects; 200+ line methods without reason.
8. **Tests:** new behavior covered in `tests/`; changed behavior has updated tests; bug fixes have a regression test that failed before the fix; randomness is seeded or injected.
9. **Documentation:** public members have accurate XML docs; README, `CLAUDE.md`, the plan in `docs/plans/` and `docs/tracking/Defects.md` still match the code.
10. **UI and interop (if UI or scene changed):** user-facing text is Dutch; the C# interop records (`Interop/`) and `scene/src/types.ts` still match field for field; interop stays event-sized (no per-frame calls from C#); new commands are reachable; messages and empty states match the new behavior.
11. **Readability:** clear names consistent with the codebase; no magic values; no dead code or unused constants.

## Output

1. **Summary:** 3-5 bullets on overall quality and risk, critical issues, test status, documentation status.
2. **Findings table**, sorted Critical → High → Medium → Low:

   | Severity | Category | File / Location | Description | Risk / Impact | Recommendation |
   |---|---|---|---|---|---|

   Category is one of the checklist names above. Location is the relative path plus class/method.
3. **What looks good:** 3-5 concrete points, no generic praise.
4. **Must fix before merge:** numbered checklist of all Critical and High findings.

## Constraints

- Report only problems you can infer from the visible code and context; do not invent issues.
- Consolidate findings with the same root cause instead of repeating them per file.
- For a clean category, state: "No issues found in this category."
