using System.Globalization;

namespace TF2.Descent.Server.Level;

/// <summary>
/// What a map's name says about it (plan_descent_mine_levels: levels are named
/// <c>tf2_descent-&lt;depth&gt;</c>; <c>make descent-level</c> links
/// <c>tf2_descent-&lt;depth&gt;-s&lt;seed&gt;</c>; the host's pool instances are
/// <c>tf2_descent-&lt;depth&gt;-&lt;hash&gt;</c>).
/// </summary>
public static class LevelNames
{
    private const string Prefix = "tf2_descent-";

    /// <summary>Whether <paramref name="map"/> is a generated Descent level: <c>tf2_descent-</c>, then a depth of 1 or more.</summary>
    public static bool IsLevel(string map) => Depth(map) > 0;

    /// <summary>The depth the name carries (<see cref="LevelTransition.DepthOf"/>), 0 for a map that is not a level.</summary>
    public static int Depth(string map) => LevelTransition.DepthOf(map);

    /// <summary>
    /// The layout seed an unhosted level's name carries, <c>tf2_descent-&lt;d&gt;-s&lt;seed&gt;</c>, or
    /// null: a pool instance's name carries a hash, not the seed, and a hosted server learns its seed
    /// from Booting.
    /// </summary>
    public static ulong? Seed(string map)
    {
        if (!IsLevel(map))
            return null;
        ReadOnlySpan<char> rest = map.AsSpan(Prefix.Length);
        int dash = rest.IndexOf('-');
        if (dash < 0)
            return null;
        ReadOnlySpan<char> tail = rest[(dash + 1)..];
        return tail.Length > 1 && tail[0] == 's'
               && ulong.TryParse(tail[1..], NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed)
            ? seed
            : null;
    }
}
