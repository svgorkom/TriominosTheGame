using Triominos.Core.Board;
using Triominos.Core.Tiles;

namespace Triominos.Tests;

/// <summary>Helpers to build tiles and boards in tests.</summary>
internal static class TestTiles
{
    private static int _nextId = 1000;

    /// <summary>Creates a tile with a fresh id. Tests may reuse values; the board does not care.</summary>
    public static Tile T(int a, int b, int c) => new(Interlocked.Increment(ref _nextId), a, b, c);

    /// <summary>Places tiles of all ones on the cells, in order, each validated.</summary>
    public static GameBoard BoardOfOnes(params (int Row, int Col)[] cells)
    {
        var board = new GameBoard();
        foreach (var (row, col) in cells)
        {
            board.Place(T(1, 1, 1), new Placement(new Cell(row, col), 0));
        }

        return board;
    }
}
