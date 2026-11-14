using Triominos.Core.Board;
using Triominos.Core.Game;
using Triominos.Web.Online;

namespace Triominos.Web.Tests;

public class MoveCodecTests
{
    [Fact]
    public void Every_action_survives_the_trip()
    {
        GameAction[] actions =
        [
            new PlaceAction(12, new Placement(new Cell(-3, 4), 2)),
            new DrawAction(),
            new PassAction(),
            new HintAction(),
        ];

        foreach (var action in actions)
        {
            Assert.Equal(action, MoveCodec.FromDto(MoveCodec.ToDto(action)));
        }
    }

    [Fact]
    public void Something_that_is_not_an_action_is_not_taken_for_one()
    {
        Assert.Null(MoveCodec.FromDto(null));
        Assert.Null(MoveCodec.FromDto(new MoveDto { K = "cheat" }));
        Assert.Null(MoveCodec.FromDto(new MoveDto { K = "place", Tile = 1 }));
    }
}
