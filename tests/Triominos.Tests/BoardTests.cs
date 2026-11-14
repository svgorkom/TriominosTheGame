using Triominos.Core.Board;
using Triominos.Core.Game;
using static Triominos.Tests.TestTiles;

namespace Triominos.Tests;

public class BoardTests
{
    [Fact]
    public void Empty_board_accepts_any_cell()
    {
        var board = new GameBoard();

        Assert.Equal(MoveError.None, board.Validate(T(0, 1, 2), new Placement(new Cell(4, -3), 2)));
    }

    [Fact]
    public void Tile_must_share_an_edge_with_a_placed_tile()
    {
        var board = BoardOfOnes((0, 0));

        Assert.Equal(MoveError.NotAdjacent, board.Validate(T(1, 1, 1), new Placement(new Cell(0, 3), 0)));
        Assert.Equal(MoveError.None, board.Validate(T(1, 1, 1), new Placement(new Cell(0, 1), 0)));
    }

    [Fact]
    public void Occupied_cell_and_bad_rotation_are_rejected()
    {
        var board = BoardOfOnes((0, 0));

        Assert.Equal(MoveError.CellOccupied, board.Validate(T(1, 1, 1), new Placement(Cell.Origin, 0)));
        Assert.Equal(MoveError.InvalidRotation, board.Validate(T(1, 1, 1), new Placement(new Cell(0, 1), 3)));
    }

    [Fact]
    public void Edge_values_must_match()
    {
        // Up tile 1-2-3 at the origin: apex 1, bottom right 2, bottom left 3.
        var board = new GameBoard();
        board.Place(T(1, 2, 3), new Placement(Cell.Origin, 0));

        // The down cell to the right shares apex (1) and bottom right (2): its corners
        // 1 = top left = apex value 1, 0 = bottom = bottom right value 2.
        Assert.Equal(MoveError.None, board.Validate(T(2, 1, 4), new Placement(new Cell(0, 1), 0)));
        Assert.Equal(MoveError.CornerMismatch, board.Validate(T(2, 1, 4), new Placement(new Cell(0, 1), 1)));
        Assert.Equal(MoveError.CornerMismatch, board.Validate(T(1, 2, 4), new Placement(new Cell(0, 1), 0)));
    }

    /// <summary>
    /// A chain of tiles of ones around the up cell (0,0): it shares an edge only with (1,0), and its
    /// apex vertex (0,1) is touched diagonally by (-1,1).
    /// </summary>
    internal static readonly (int, int)[] ChainAroundOrigin =
        [(1, 0), (1, 1), (1, 2), (0, 2), (0, 3), (-1, 3), (-1, 2), (-1, 1)];

    [Fact]
    public void Corner_touching_only_diagonally_must_match_too() // D-007
    {
        var board = BoardOfOnes(ChainAroundOrigin);

        // Edge corners (1,2) and (1,0) are 1 and match; the apex (0,1) is 1 on the board, not 2.
        Assert.Equal(MoveError.CornerMismatch, board.Validate(T(2, 1, 1), new Placement(Cell.Origin, 0)));
        Assert.Equal(MoveError.None, board.Validate(T(1, 1, 1), new Placement(Cell.Origin, 0)));
    }

    [Fact]
    public void Validate_does_not_change_the_board_or_tile() // D-101
    {
        var board = BoardOfOnes((0, 0));
        var tile = T(1, 1, 1);
        var before = tile with { };

        board.Validate(tile, new Placement(new Cell(0, 1), 2));

        Assert.Equal(before, tile);
        Assert.Single(board.Tiles);
    }

    [Fact]
    public void Rotation_is_explicit_and_not_searched() // D-102
    {
        var board = new GameBoard();
        board.Place(T(1, 2, 3), new Placement(Cell.Origin, 0));

        var valid = board.ValidPlacements(T(2, 1, 4));

        Assert.Contains(new Placement(new Cell(0, 1), 0), valid);
        Assert.DoesNotContain(new Placement(new Cell(0, 1), 1), valid);
        Assert.DoesNotContain(new Placement(new Cell(0, 1), 2), valid);
    }

    [Fact]
    public void Valid_placements_equal_brute_force_search()
    {
        var board = BoardOfOnes((0, 0), (0, 1), (1, 0));
        var tile = T(1, 1, 4);

        var expected = new List<Placement>();
        for (var row = -3; row <= 4; row++)
        {
            for (var col = -3; col <= 5; col++)
            {
                for (var rotation = 0; rotation < 3; rotation++)
                {
                    var placement = new Placement(new Cell(row, col), rotation);
                    if (board.Validate(tile, placement) == MoveError.None)
                    {
                        expected.Add(placement);
                    }
                }
            }
        }

        Assert.NotEmpty(expected);
        Assert.Equal(expected.ToHashSet(), board.ValidPlacements(tile).ToHashSet());
    }

    [Fact]
    public void A_clone_is_a_separate_board_with_the_same_tiles_and_values()
    {
        var board = BoardOfOnes((0, 0), (0, 1));

        var copy = board.Clone();
        copy.Place(T(1, 1, 1), new Placement(new Cell(0, 2), 0));

        Assert.Equal(2, board.Tiles.Count);
        Assert.False(board.IsOccupied(new Cell(0, 2)));
        Assert.Equal(3, copy.Tiles.Count);
        Assert.Equal(board.ValueAt(new Vertex(0, 1)), copy.ValueAt(new Vertex(0, 1)));
        Assert.Contains(new Cell(0, 2), board.OpenCells);
        Assert.DoesNotContain(new Cell(0, 2), copy.OpenCells);
    }
}
