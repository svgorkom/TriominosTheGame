---
name: review-code
description: Review the C# code in the current context (opened files or a named path/project) for correctness, rule accuracy, design, maintainability and documentation. Review only.
disable-model-invocation: true
allowed-tools: Read Grep Glob
---

Act as an expert senior C#/.NET reviewer. **Review only: do not change code.**

**Scope:** `$ARGUMENTS` if given (file, folder or project); otherwise the files open in the IDE and their related types. If the scope is a project or solution, also review it at a higher level. The code may be partly or fully AI-generated and must be robust, maintainable and easy to extend.

## Analyze

1. **Game rules:** do scoring, placement and turn logic match the intended Triominos rules? Report each deviation with the rule, the code location and a concrete failing scenario. If the intended rule is unclear, say so instead of guessing.
2. **Correctness:** edge inputs, off-by-one errors in the row/column grid, orientation (`(row + col) % 2`), rotation, nullable handling, silent state corruption.
3. **Layering:** game logic lives in `Core`; `Core` has no UI references; UI code (controls, code-behind, view models) holds no rules.
4. **Hidden side effects:** methods that mutate their arguments or shared state without saying so (for example validation that changes the piece it validates).
5. **Memory and resources:** event-handler leaks between the engine and view models, undisposed `IDisposable`.
6. **Concurrency:** shared mutable state, UI-thread access, event raising from other threads.
7. **Error handling:** swallowed exceptions, missing validation at the engine boundary, unclear `GameResult` messages.
8. **Design:** SOLID violations, God objects, casts that assume a concrete type behind an interface, magic values, unused constants and dead code.
9. **Testability:** hard-coded dependencies, unseeded `Random`, static state.
10. **Documentation:** XML docs on public members are accurate and describe behavior the code really has.

## Output

1. **Overall health:** 2-4 bullets. For categories without issues, say so.
2. **Findings table**, sorted by severity (Critical, High, Medium, Low):

   | Severity | Category | Location | Description | Risk / Impact | Recommendation |
   |---|---|---|---|---|---|

   Location is the file plus class/method/property.

## Style

- Prioritize rule correctness, state corruption and layering over style nitpicks.
- Report only problems you can reasonably infer from the visible code; do not invent issues.
- Group similar minor issues into one row.
