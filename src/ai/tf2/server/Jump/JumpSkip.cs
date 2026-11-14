using System.Globalization;
using SourceSharp.Abi;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// A place the link pass is told to leave alone (lane G6b; plan_bots_teams_tf2_part_g G.6's "a jump the derivation was
/// told to skip"): no candidate that lands within <paramref name="Radius"/> of <paramref name="At"/> over the ground and
/// <see cref="HeightSlack"/> of its height is searched. For proving that a jump a player showed becomes a link where the
/// pass made none; set by <c>ai_tf_links_skip</c> (<see cref="TFLinksSkipConVar"/>), a cheat, off by default.
/// </summary>
public readonly record struct JumpSkip(Vector At, float Radius)
{
    /// <summary>How far above or below the place a landing may be and still be skipped: half a bin of the experience.</summary>
    public const float HeightSlack = 36f;

    /// <summary>Whether a candidate landing at <paramref name="landing"/> is skipped.</summary>
    public bool Covers(Vector landing)
    {
        float dx = landing.X - At.X, dy = landing.Y - At.Y;
        return dx * dx + dy * dy <= Radius * Radius && MathF.Abs(landing.Z - At.Z) <= HeightSlack;
    }

    /// <summary>The place <paramref name="text"/> names as "x y z radius" (invariant numbers, the radius over 0), or null for an empty or malformed one.</summary>
    public static JumpSkip? Parse(string? text)
    {
        string[] parts = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
            return null;
        var n = new float[4];
        for (int i = 0; i < 4; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i]) || !float.IsFinite(n[i]))
                return null;
        }

        return n[3] > 0f ? new JumpSkip(new Vector(n[0], n[1], n[2]), n[3]) : null;
    }

    /// <summary>"x y z radius", invariant: what the links' cache key holds of it.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{At.X:R} {At.Y:R} {At.Z:R} {Radius:R}");
}

/// <summary>
/// <c>ai_tf_links_skip</c> (lane G6b): "x y z radius", a place the level's link pass proposes no landing in
/// (<see cref="JumpSkip"/>); empty, the default, for none. Read when a level's links are found; its own part of the
/// links' cache key, so no file of a pass that searched there answers for one that did not. A cheat.
/// </summary>
[ConVar("ai_tf_links_skip", "", Flags = ConVarFlags.Cheat,
        Help = "x y z radius: the TF2 blast link pass proposes no landing within radius of that place (proof of learned links); empty for none")]
public sealed class TFLinksSkipConVar : StringConVar { }
