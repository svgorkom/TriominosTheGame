using System.Globalization;
using Triominos.Core.Game;

namespace Triominos.Web.Play;

/// <summary>
/// The time of the human players, kept by the UI; the game only gets a
/// <see cref="TriominosGame.TimeOut"/> when it runs out. With a turn clock every turn starts with
/// the same time. With a game clock each player has one supply for the whole game; while it lasts a
/// turn has no limit of its own, and once it is used up every turn gets the time after it.
/// </summary>
public sealed class TurnClock
{
    private readonly ClockRules _rules;
    private readonly TimeSpan[] _gameLeft;
    private TimeSpan? _turnLeft;

    /// <summary>Creates the clock of a game.</summary>
    /// <param name="rules">How much time a player gets.</param>
    /// <param name="playerCount">Players in the game.</param>
    public TurnClock(ClockRules rules, int playerCount)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
        _gameLeft = [.. Enumerable.Repeat(rules.Game ?? TimeSpan.Zero, playerCount)];
    }

    /// <summary>Gets the player whose time runs.</summary>
    public int Player { get; private set; }

    /// <summary>Gets the time left in this turn, or null when the turn has no limit of its own.</summary>
    public TimeSpan? TurnLeft => _turnLeft;

    /// <summary>Returns the game time a player has left, or null without a game clock.</summary>
    /// <param name="player">The player.</param>
    public TimeSpan? GameLeft(int player) => _rules.Game is null ? null : _gameLeft[player];

    /// <summary>Starts the time of a turn; also after a draw, which gives the player a fresh turn time.</summary>
    /// <param name="player">The player whose turn it is.</param>
    public void StartTurn(int player)
    {
        Player = player;
        _turnLeft = _rules.Game is null ? _rules.Turn
            : _gameLeft[player] > TimeSpan.Zero ? null
            : _rules.TurnAfterGameTime;
    }

    /// <summary>Lets time pass for the current player; returns true when their time ran out.</summary>
    /// <param name="elapsed">Time since the last tick.</param>
    public bool Tick(TimeSpan elapsed)
    {
        if (_rules.Game is not null && _gameLeft[Player] > TimeSpan.Zero)
        {
            var used = elapsed < _gameLeft[Player] ? elapsed : _gameLeft[Player];
            _gameLeft[Player] -= used;
            elapsed -= used;
            if (_gameLeft[Player] > TimeSpan.Zero)
            {
                return false;
            }

            _turnLeft = _rules.TurnAfterGameTime;
        }

        if (_turnLeft is not { } left)
        {
            return false;
        }

        left -= elapsed;
        _turnLeft = left > TimeSpan.Zero ? left : TimeSpan.Zero;
        return left <= TimeSpan.Zero;
    }

    /// <summary>Writes a time as minutes and seconds, rounded up: 0:42, 8:00.</summary>
    /// <param name="time">The time.</param>
    public static string Format(TimeSpan time)
    {
        var seconds = (int)Math.Ceiling(Math.Max(0, time.TotalSeconds));
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }
}
