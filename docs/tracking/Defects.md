# Known defects and rule gaps

The defects found in the original WPF code (2026-11-11) were fixed by rewriting Core in phase 0. Each rule fix has a test, named in the table.

## Fixed

| ID | Item | Test |
|---|---|---|
| D-001 | A random tile was placed at the start, so the start rules never applied. | `GameTests.Start_deals_tiles_by_player_count` and the start tests |
| D-002 | Draw penalty, draw limit and going-out bonus were unused. | `GameTests.Drawing_costs_five_per_tile_up_to_three_then_passing_costs_ten` |
| D-003 | No detection of a blocked game. Now a draw (deliberate deviation, see the plan). | `GameTests.Everyone_passing_with_an_empty_pool_is_a_draw` |
| D-004 | No settlement at the end of the round. | `GameTests.Going_out_scores_twenty_five_plus_the_other_racks` |
| D-005 | Tiles could be picked from an open pool. The pool is now face down. | `GameTests.Drawing_costs_five_per_tile_up_to_three_then_passing_costs_ten` |
| D-006 | One player was allowed. Now 2 to 4. | `GameTests.Start_rejects_unsupported_player_counts` |
| D-007 | Corners shared only diagonally were not checked. | `BoardTests.Corner_touching_only_diagonally_must_match_too` |
| D-008 | Every 2x3 block of cells counted as a hexagon. | `BonusTests.A_full_two_by_three_block_not_around_a_vertex_is_no_hexagon` |
| D-101 | Validation mutated the tile it validated. | `BoardTests.Validate_does_not_change_the_board_or_tile` |
| D-102 | The engine tried all rotations instead of using the player's. | `BoardTests.Rotation_is_explicit_and_not_searched` |
| D-103 | Casts assumed concrete types behind interfaces. Removed with the interfaces. | n/a |
| D-104 | No tests, no CI, wrong README. | n/a |
| D-105 | The WPF UI was never reviewed. Removed; replaced by the Blazor front end. | n/a |

## Open

None known.
