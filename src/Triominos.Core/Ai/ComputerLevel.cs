namespace Triominos.Core.Ai;

/// <summary>How well the computer plays.</summary>
public enum ComputerLevel
{
    /// <summary>Plays a random valid move.</summary>
    Easy,

    /// <summary>Plays the move that scores most right now.</summary>
    Normal,

    /// <summary>Plans a few moves ahead with its own tiles.</summary>
    Strong,
}
