using Triominos.Core.Board;
using static Triominos.Tests.TestTiles;

namespace Triominos.Tests;

public class BonusTests
{
    [Fact]
    public void Plain_placement_has_no_bonus()
    {
        var board = BoardOfOnes((0, 0));

        Assert.Equal(new PlacementBonus(false, 0), board.EvaluateBonus(new Cell(0, 1)));
    }

    [Fact]
    public void One_shared_edge_and_occupied_opposite_corner_is_a_bridge()
    {
        var board = BoardOfOnes(BoardTests.ChainAroundOrigin);

        Assert.Equal(new PlacementBonus(true, 0), board.EvaluateBonus(Cell.Origin));
    }

    [Fact]
    public void Filling_the_sixth_cell_around_a_vertex_completes_a_hexagon() // D-008
    {
        // The six cells around vertex (1,2); the last one placed is (1,0).
        var board = BoardOfOnes((0, 0), (0, 1), (0, 2), (1, 2), (1, 1));

        Assert.Equal(new PlacementBonus(false, 1), board.EvaluateBonus(new Cell(1, 0)));
    }

    [Fact]
    public void Hexagon_centers_name_the_vertices_a_tile_completes_before_and_after_it_is_placed()
    {
        var board = BoardOfOnes((0, 0), (0, 1), (0, 2), (1, 2), (1, 1));
        var last = new Cell(1, 0);

        Assert.Equal(new[] { new Vertex(1, 2) }, board.HexagonCenters(last));

        board.Place(T(1, 1, 1), new Placement(last, 0));

        Assert.Equal(new[] { new Vertex(1, 2) }, board.HexagonCenters(last));
    }

    [Fact]
    public void Hexagon_centers_are_empty_when_no_hexagon_is_full()
    {
        var board = BoardOfOnes((0, 0));

        Assert.Empty(board.HexagonCenters(new Cell(0, 1)));
    }

    [Fact]
    public void A_full_two_by_three_block_not_around_a_vertex_is_no_hexagon() // D-008
    {
        // Rows 0-1, columns 1-3: six cells in a 2x3 block, but no single vertex is shared by all
        // six. The old code counted every 2x3 block as a hexagon.
        var board = BoardOfOnes((0, 1), (0, 2), (0, 3), (1, 2), (1, 3));

        Assert.Equal(0, board.EvaluateBonus(new Cell(1, 1)).Hexagons);
    }

    [Fact]
    public void Completing_two_hexagons_at_once_counts_two()
    {
        // Hexagons around vertices (1,2) and (1,4); the shared cell (1,2) is placed last.
        var board = BoardOfOnes((0, 0), (0, 1), (0, 2), (0, 3), (0, 4), (1, 4), (1, 3), (1, 0), (1, 1));

        Assert.Equal(new PlacementBonus(false, 2), board.EvaluateBonus(new Cell(1, 2)));
    }
}
