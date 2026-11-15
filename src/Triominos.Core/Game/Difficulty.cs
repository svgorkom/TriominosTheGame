namespace Triominos.Core.Game;

/// <summary>
/// How hard a game is. A level only changes the house rules (<see cref="GameRules"/>) and the help
/// the UI gives; the published rules and the scoring stay the same at every level.
/// </summary>
public enum Difficulty
{
    /// <summary>Free hints, a move may be taken back, no clock.</summary>
    Easy,

    /// <summary>The default hints, no clock.</summary>
    Normal,

    /// <summary>One costly hint and a minute per turn.</summary>
    Hard,

    /// <summary>No hints, a game clock, and a penalty for a tile whose corners do not match.</summary>
    VeryHard,
}

/// <summary>The house rules of each <see cref="Difficulty"/>.</summary>
public static class Difficulties
{
    /// <summary>Points a mismatched tile costs at <see cref="Difficulty.VeryHard"/>: the same as drawing a tile.</summary>
    public const int VeryHardMistakePenalty = Scoring.DrawPenalty;

    /// <summary>Gets every level, from easy to very hard.</summary>
    public static IReadOnlyList<Difficulty> All { get; } = [Difficulty.Easy, Difficulty.Normal, Difficulty.Hard, Difficulty.VeryHard];

    /// <summary>Returns the house rules of a level.</summary>
    /// <param name="difficulty">The level.</param>
    public static GameRules Rules(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => new GameRules(new HintRules(5, 0), UndoAllowed: true),
        Difficulty.Normal => GameRules.Default,
        Difficulty.Hard => new GameRules(new HintRules(1, 15), Clock: new ClockRules(TimeSpan.FromSeconds(60))),
        Difficulty.VeryHard => new GameRules(
            new HintRules(0, 0),
            MistakePenalty: VeryHardMistakePenalty,
            Clock: new ClockRules(null, TimeSpan.FromMinutes(8), TimeSpan.FromSeconds(15))),
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "Unknown difficulty."),
    };
}
