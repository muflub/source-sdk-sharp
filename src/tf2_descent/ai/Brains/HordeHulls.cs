using SourceSharp.Ai;

namespace TF2.Descent.Ai.Brains;

/// <summary>
/// The clearance class a robot plans its paths by (plan_descent_logic 3.6, HO3's hull classes):
/// robots are scaled 0.6 to 1.9, and a body's hull is the TF player's (48 wide, 82 tall) times
/// its scale. A robot of scale 1 or less plans as the game's own body
/// (<see cref="ClearanceClass.Stand"/>: a smaller hull goes where it goes, and the graph has no
/// place only a runt fits); a larger one by the smallest of the level graph's extra hulls whose
/// scale is at least its own (<see cref="Scales"/>, class <see cref="ClearanceClass.Hull2"/> the
/// first). The server builds the graph's extra hulls from the same scales, so a class always
/// names a hull the graph was classed for.
/// </summary>
public static class HordeHulls
{
    /// <summary>The TF player's standing hull: the scale-1 robot's.</summary>
    public const float StandWidth = 48f, StandHeight = 82f;

    /// <summary>How far over a band's scale a robot still plans by it: a rolled scale is a double, the bands are floats.</summary>
    private const float Slack = 1e-3f;

    /// <summary>
    /// The scales the graph's extra hulls are built for, smallest first: the top of the ordinary
    /// robots' roll (1.15), then the giants' (1.5, 1.75, 1.9; robots.json's scale bands).
    /// </summary>
    public static ReadOnlySpan<float> Scales => [1.15f, 1.5f, 1.75f, 1.9f];

    /// <summary>The class a robot of <paramref name="scale"/> plans by.</summary>
    public static ClearanceClass ClassOf(float scale)
    {
        if (scale <= 1f + Slack)
            return ClearanceClass.Stand;
        ReadOnlySpan<float> scales = Scales;
        for (int i = 0; i < scales.Length; i++)
        {
            if (scale <= scales[i] + Slack)
                return (ClearanceClass)(2 + i);
        }

        return (ClearanceClass)(1 + scales.Length);
    }

    /// <summary>The width of extra hull <paramref name="hull"/> (0 the first), rounded up to the unit.</summary>
    public static float Width(int hull) => MathF.Ceiling(StandWidth * Scales[hull]);

    /// <summary>The height of extra hull <paramref name="hull"/>, rounded up to the unit.</summary>
    public static float Height(int hull) => MathF.Ceiling(StandHeight * Scales[hull]);

    /// <summary>Half the width of the hull <paramref name="cls"/> plans for: the stand hull's, or extra hull's (crouch: the stand hull's).</summary>
    public static float HalfWidthOf(ClearanceClass cls) =>
        (int)cls - 2 is var i && i >= 0 && i < Scales.Length ? Width(i) * 0.5f : StandWidth * 0.5f;

    /// <summary>The height of the hull <paramref name="cls"/> plans for (crouch: the stand hull's, the most a crouched body reaches).</summary>
    public static float HeightOf(ClearanceClass cls) =>
        (int)cls - 2 is var i && i >= 0 && i < Scales.Length ? Height(i) : StandHeight;

    /// <summary>A class's name for the dev print: stand, or the scale its hull is built for.</summary>
    public static string NameOf(ClearanceClass cls) => cls switch
    {
        ClearanceClass.Stand => "stand",
        ClearanceClass.Crouch => "crouch",
        _ => (int)cls - 2 < Scales.Length ? FormattableString.Invariant($"x{Scales[(int)cls - 2]:0.00}") : cls.ToString(),
    };
}
