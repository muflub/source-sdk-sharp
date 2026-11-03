namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// Where a radial menu's items go, and which one a pointer means: the first at
/// the top, the rest clockwise, evenly. Any direction outside the hub picks the
/// nearest item, so the pointer never has to reach one to choose it.
/// </summary>
public static class UiRadialLayout
{
    /// <summary>The most items a ring holds and still reads; a longer list is paged by its owner.</summary>
    public const int MaxItems = 9;

    /// <summary>An item's angle, radians, clockwise from the top.</summary>
    public static float Angle(int index, int count) => count <= 0 ? 0f : MathF.Tau * index / count;

    /// <summary>An item's wedge: where it starts, clockwise from the top, and how far round, centred on its angle with a gap to its neighbours.</summary>
    public static (float Start, float Span) Wedge(int index, int count, float gapRadians)
    {
        float span = MathF.Tau / MathF.Max(count, 1);
        return (Angle(index, count) - span / 2f + gapRadians / 2f, span - gapRadians);
    }

    /// <summary>An item's centre relative to the menu's, y down.</summary>
    public static (float X, float Y) Centre(int index, int count, float radiusX, float radiusY)
    {
        float a = Angle(index, count);
        return (MathF.Sin(a) * radiusX, -MathF.Cos(a) * radiusY);
    }

    /// <summary>The item a pointer at (dx, dy) from the menu's centre means; -1 inside the hub or with no items.</summary>
    public static int ItemAt(float dx, float dy, int count, float hubRadius)
    {
        if (count <= 0 || dx * dx + dy * dy < hubRadius * hubRadius)
            return -1;
        float a = MathF.Atan2(dx, -dy);
        if (a < 0f)
            a += MathF.Tau;
        return (int)MathF.Round(a / (MathF.Tau / count)) % count;
    }
}
