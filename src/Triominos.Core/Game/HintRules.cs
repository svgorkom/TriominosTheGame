namespace Triominos.Core.Game;

/// <summary>How hints work in one game: how many each player may take and what one costs.</summary>
/// <param name="MaxPerPlayer">Hints each player may take in the game; 0 turns hints off.</param>
/// <param name="Penalty">Points a hint costs; 0 makes hints free.</param>
public readonly record struct HintRules(int MaxPerPlayer, int Penalty)
{
    /// <summary>Gets the rules a game uses unless told otherwise.</summary>
    public static HintRules Default { get; } = new(Scoring.DefaultHintsPerPlayer, Scoring.DefaultHintPenalty);

    /// <summary>Gets whether hints can be taken at all.</summary>
    public bool Enabled => MaxPerPlayer > 0;
}
