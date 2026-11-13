# Known defects and rule gaps

Found by reading `Core/` on 2026-11-11. Nothing here has been verified by a test yet; items marked *unverified* are suspicions from reading the code. Fix each with a failing test first.

## Rules

| ID | Item | Where | Status |
|---|---|---|---|
| D-001 | `IsFirstMove` is always false during play, because `StartGame` already places a random starting piece. The first-move triple bonus (`FirstMoveTripleBonus`) and the first-move branches in `ValidatePlacement` and `GetValidPlacements` are dead code. | `GameEngine.StartGame`, `GameEngine.IsFirstMove`, `GameRules` | Open |
| D-002 | `DrawPenalty`, `MaxDrawsPerTurn` and `GoingOutBonus` are never used. Drawing from the pool costs nothing and ends the turn immediately. | `GameRules`, `GameEngine.AddSelectedPieceToRack` | Open |
| D-003 | No detection of a blocked game (empty pool and nobody can move). The game ends only when a player goes out. | `GameEngine` | Open |
| D-004 | No end-of-game settlement for the pieces left in the other racks. | `GameEngine.EndGame` | Open |
| D-005 | Players can pick a piece from the open pool and play it directly. This is not in the official rules. | `GameEngine.SelectPieceFromPool`, `PlacePiece` | Open, decide |
| D-006 | Single player is allowed (`MinPlayers = 1`). | `GameRules` | Open, decide |
| D-007 | Placement compares edges only; corner values (vertices) shared with diagonal neighbours are not checked. Whether the official rules require this must be confirmed. | `GameRules.ValidatePlacement` | Unverified |
| D-008 | Hexagon detection treats every 2x3 block of cells as a hexagon, without checking that the block's cells form the triangle pattern around a vertex. This probably awards the 50-point bonus too often. | `GameRules.IsHexagonComplete` | Unverified |

## Code

| ID | Item | Where | Status |
|---|---|---|---|
| D-101 | `ValidatePlacement` mutates the piece it validates (`IsPointingUp`). | `GameRules.ValidatePlacement` | Open |
| D-102 | `PlacePiece` tries all three rotations and takes the first valid one, so the player's chosen rotation is only a preference. | `GameEngine.PlacePiece` | Open, decide |
| D-103 | Casts such as `(Piece)piece.Clone()` assume every `IPiece` is a `Piece`. | `GameRules`, `GameEngine` | Open |
| D-104 | No tests, no CI, README described another project (fixed on branch `feat/ai-setup`). | repository | Partly fixed |
| D-105 | The UI (`UI/`, about 2000 lines) has not been reviewed yet. | `UI/` | Not reviewed |
