using Triominos.Core.Game;

namespace Triominos.Web.Prototype;

/// <summary>
/// Phase 1 prototype only: plays moves for both players so the table can be shown with a real,
/// rule-checked game. It picks the highest tile that fits, otherwise draws, otherwise passes.
/// Replaced in phase 2 by player input (see docs/plans/BlazorUi.md).
/// </summary>
public sealed class DemoGame
{
    private static readonly string[] PlayerNames = ["Speler 1", "Speler 2"];

    /// <summary>Starts a demo with a fixed seed, so the same game is shown each time.</summary>
    public DemoGame(int seed)
    {
        Game = TriominosGame.Start(PlayerNames, new Random(seed));
    }

    /// <summary>Gets the game being played.</summary>
    public TriominosGame Game { get; }

    /// <summary>Plays one move for the current player and returns a Dutch description of it.</summary>
    public string PlayOneMove()
    {
        if (Game.Phase == GamePhase.Finished)
        {
            return "Het spel is afgelopen.";
        }

        var player = Game.CurrentPlayer;
        foreach (var tile in player.Rack.OrderByDescending(t => t.Value).ToList())
        {
            var placements = Game.ValidPlacements(tile.Id);
            if (placements.Count == 0)
            {
                continue;
            }

            var result = Game.Place(tile.Id, placements[0]);
            var placed = result.Events.OfType<TilePlaced>().Single();
            return $"{player.Name} legt {tile} voor {placed.Score.Total} punten{BonusText(placed.Score)}.";
        }

        if (Game.CanDraw)
        {
            Game.Draw();
            return $"{player.Name} trekt een steen (-{Scoring.DrawPenalty}).";
        }

        if (Game.CanPass)
        {
            Game.Pass();
            return $"{player.Name} past (-{Scoring.PassPenalty}).";
        }

        return $"{player.Name} kan niets doen.";
    }

    private static string BonusText(ScoreBreakdown score)
    {
        if (score.HexagonBonus > 0)
        {
            return " met hexagonbonus";
        }

        if (score.BridgeBonus > 0)
        {
            return " met brugbonus";
        }

        return score.StartBonus > 0 ? " met startbonus" : string.Empty;
    }
}
