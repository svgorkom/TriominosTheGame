using Triominos.Core.Board;
using Triominos.Core.Tiles;

namespace Triominos.Tests;

public class GeometryTests
{
    public static TheoryData<int, int> SomeCells() => new()
    {
        { 0, 0 }, { 0, 1 }, { 1, 0 }, { -3, 2 }, { 5, -7 }, { -2, -2 },
    };

    [Fact]
    public void Standard_set_has_56_unique_tiles_ascending_clockwise()
    {
        var tiles = TileSet.Standard();

        Assert.Equal(TileSet.StandardSize, tiles.Count);
        Assert.Equal(tiles.Count, tiles.Select(t => (t.A, t.B, t.C)).Distinct().Count());
        Assert.All(tiles, t => Assert.True(t.A <= t.B && t.B <= t.C));
        Assert.Equal(6, tiles.Count(t => t.IsTriple));
    }

    [Theory]
    [MemberData(nameof(SomeCells))]
    public void Neighbors_alternate_orientation_and_point_back(int row, int col)
    {
        var cell = new Cell(row, col);

        for (var edge = 0; edge < 3; edge++)
        {
            var neighbor = cell.Neighbor(edge);
            Assert.NotEqual(cell.IsUp, neighbor.IsUp);
            Assert.Contains(cell, Enumerable.Range(0, 3).Select(neighbor.Neighbor));
        }
    }

    [Theory]
    [MemberData(nameof(SomeCells))]
    public void Neighbors_share_exactly_the_two_corners_of_the_edge(int row, int col)
    {
        var cell = new Cell(row, col);

        for (var edge = 0; edge < 3; edge++)
        {
            var neighborCorners = Enumerable.Range(0, 3).Select(cell.Neighbor(edge).Corner).ToHashSet();
            var shared = Enumerable.Range(0, 3).Select(cell.Corner).Where(neighborCorners.Contains).ToHashSet();

            Assert.True(shared.SetEquals([cell.Corner(edge), cell.Corner((edge + 1) % 3)]));
            Assert.DoesNotContain(cell.Corner((edge + 2) % 3), neighborCorners);
        }
    }

    [Theory]
    [MemberData(nameof(SomeCells))]
    public void Each_corner_vertex_lists_the_cell_among_its_six(int row, int col)
    {
        var cell = new Cell(row, col);

        for (var corner = 0; corner < 3; corner++)
        {
            var cells = cell.Corner(corner).Cells().ToList();
            Assert.Equal(6, cells.Distinct().Count());
            Assert.Contains(cell, cells);
            Assert.All(cells, c => Assert.Contains(cell.Corner(corner), Enumerable.Range(0, 3).Select(c.Corner)));
        }
    }

    [Fact]
    public void Rotation_shifts_values_clockwise()
    {
        var tile = new Tile(0, 1, 2, 3);

        Assert.Equal(new[] { 1, 2, 3 }, Enumerable.Range(0, 3).Select(c => tile.ValueAtCorner(0, c)));
        Assert.Equal(new[] { 2, 3, 1 }, Enumerable.Range(0, 3).Select(c => tile.ValueAtCorner(1, c)));
        Assert.Equal(new[] { 3, 1, 2 }, Enumerable.Range(0, 3).Select(c => tile.ValueAtCorner(2, c)));
    }
}
