namespace TF2.Descent.Server.Level;

/// <summary>Which arrival point a hero takes (mine plan D1). Pure.</summary>
public static class ArrivalPicker
{
    /// <summary>
    /// The point of <paramref name="points"/> a hero takes: the one it already holds this level
    /// (<paramref name="held"/>, a respawn), else the lowest point no other hero holds, so a party
    /// of four on four points stands on four. With every point held (a party larger than the
    /// level's points) heroes share them in turn: the hero takes point (heroes holding one) mod
    /// <paramref name="points"/>. -1 when there are no points.
    /// </summary>
    public static int Pick(int points, IReadOnlyCollection<int> heldByOthers, int? held)
    {
        if (points <= 0)
            return -1;
        if (held is int mine && mine >= 0 && mine < points)
            return mine;
        for (int i = 0; i < points; i++)
            if (!heldByOthers.Contains(i))
                return i;
        return heldByOthers.Count % points;
    }
}
