namespace Triominos.Core.Game;

/// <summary>
/// The house rules of one game on top of the published rules: hints, a penalty for a tile laid
/// where its corners do not match, taking a move back, and a clock. None of them changes how a
/// move scores.
/// </summary>
/// <param name="Hints">How hints work.</param>
/// <param name="MistakePenalty">Points a tile costs when it is laid next to a tile but its corners do not match; 0 turns this off.</param>
/// <param name="UndoAllowed">Whether a player may take back their last placement; see <see cref="TriominosGame.Undo"/>.</param>
/// <param name="Clock">The time a player gets; null for no clock. The game only stores it: the UI keeps the time and calls <see cref="TriominosGame.TimeOut"/>.</param>
public sealed record GameRules(HintRules Hints, int MistakePenalty = 0, bool UndoAllowed = false, ClockRules? Clock = null)
{
    /// <summary>Gets the rules a game uses unless told otherwise: the default hints and nothing else.</summary>
    public static GameRules Default { get; } = new(HintRules.Default);
}

/// <summary>
/// How much time a player gets. A turn clock gives the same time every turn. A game clock gives a
/// player one supply of time for the whole game (like a chess clock); once it is used up, every
/// turn gets <see cref="TurnAfterGameTime"/>.
/// </summary>
/// <param name="Turn">Time per turn, or null.</param>
/// <param name="Game">Time per player for the whole game, or null.</param>
/// <param name="TurnAfterGameTime">Time per turn once the game time is used up; only with <paramref name="Game"/>.</param>
public sealed record ClockRules(TimeSpan? Turn, TimeSpan? Game = null, TimeSpan? TurnAfterGameTime = null);
