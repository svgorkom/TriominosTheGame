namespace Triominos.Web.Interop;

/// <summary>How fast the scene plays its animations.</summary>
public enum AnimationSpeed
{
    Slow,
    Normal,
    Fast,
    VeryFast,
}

/// <summary>Maps the speed setting to what the scene's <c>setAnimationSpeed</c> takes.</summary>
public static class AnimationSpeeds
{
    /// <summary>Gets every speed, slowest first.</summary>
    public static IReadOnlyList<AnimationSpeed> All { get; } = Enum.GetValues<AnimationSpeed>();

    /// <summary>Returns the multiplier for the scene: 1 is normal speed, 2 is twice as fast.</summary>
    public static double Multiplier(AnimationSpeed speed) => speed switch
    {
        AnimationSpeed.Slow => 0.6,
        AnimationSpeed.Normal => 1,
        AnimationSpeed.Fast => 2,
        AnimationSpeed.VeryFast => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(speed), speed, null),
    };

    /// <summary>Returns the Dutch name of a speed.</summary>
    public static string Label(AnimationSpeed speed) => speed switch
    {
        AnimationSpeed.Slow => "Langzaam",
        AnimationSpeed.Normal => "Normaal",
        AnimationSpeed.Fast => "Snel",
        AnimationSpeed.VeryFast => "Zeer snel",
        _ => throw new ArgumentOutOfRangeException(nameof(speed), speed, null),
    };
}
