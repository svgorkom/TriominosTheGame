namespace Triominos.Web.Play;

/// <summary>A player as the HUD shows them.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Score">Score.</param>
/// <param name="RackCount">Tiles in the rack.</param>
/// <param name="Color">The colour the player chose.</param>
public sealed record HudPlayer(string Name, int Score, int RackCount, PlayerColor Color);

/// <summary>
/// What the HUD shows: scores, tile counts, the pot and the message. It is a copy, so the page
/// can keep showing the old numbers while the scene plays a move and update them when it is done.
/// </summary>
/// <param name="Players">The players in turn order.</param>
/// <param name="CurrentPlayer">Index of the player whose turn it is.</param>
/// <param name="GameOver">Whether the round is over.</param>
/// <param name="Pool">Tiles left in the pot.</param>
/// <param name="Message">The message about the last move.</param>
public sealed record HudState(IReadOnlyList<HudPlayer> Players, int CurrentPlayer, bool GameOver, int Pool, string Message)
{
    /// <summary>Copies what the HUD shows from a session.</summary>
    /// <param name="session">The session.</param>
    public static HudState From(PlaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var game = session.Game;
        return new HudState(
            [.. game.Players.Select((p, i) => new HudPlayer(p.Name, p.Score, p.Rack.Count, session.Setup[i].Color))],
            game.CurrentPlayerIndex,
            session.Screen == SessionScreen.Finished,
            game.PoolCount,
            session.Message);
    }
}
