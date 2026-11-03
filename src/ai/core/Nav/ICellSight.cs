namespace SourceSharp.Ai;

/// <summary>
/// The map's static line of sight between places, for planning only (plan_hl2mp_ai
/// §6.2, §10 Cover): whether a standing eye at one cell sees one at another.
/// Perception never reads it (§4.1); a binding builds it from its nav data.
/// </summary>
public interface ICellSight
{
    /// <summary>How far the table answers; beyond it nothing is known to see anything.</summary>
    float Range { get; }

    /// <summary>Whether a standing eye at cell <paramref name="a"/> sees one at cell <paramref name="b"/>.</summary>
    bool Visible(int a, int b);

    /// <summary>The share of the places in range that see cell <paramref name="cell"/>, 0..1: how exposed standing there is.</summary>
    float Exposure(int cell);
}
