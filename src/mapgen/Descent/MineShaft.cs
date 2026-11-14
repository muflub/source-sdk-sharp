namespace SourceSharp.MapGen.Descent;

/// <summary>
/// The mine shaft the town and level 1 share (plan_descent_mine_levels.md §8,
/// task A1): the opening, the cage, the timber that lines it, the lantern
/// that hangs by it, and the afternoon sun and sky that fall down it. The town
/// generator (<see cref="TF2DescentTown"/>) and the room generators take these
/// values from here, so the shaft a party rides down is the shaft it arrives
/// at.
/// </summary>
public static class MineShaft
{
    /// <summary>The opening's edge: the shaft is this square.</summary>
    public const int Size = 192;

    /// <summary>Half the opening, the distance from the shaft's axis to its lining.</summary>
    public const int Half = Size / 2;

    /// <summary>The cage's floor is this square.</summary>
    public const int CageSize = 128;

    /// <summary>The cage's height from its floor's underside to its roof's top.</summary>
    public const int CageHeight = 120;

    /// <summary>The timber the shaft is lined with.</summary>
    public const string Lining = "wood/wood_wall007";

    /// <summary>The planks of the shaft's floor and the cage's floor.</summary>
    public const string Floor = "wood/wood_floor002";

    /// <summary>The beams of the headframe, the fence and the cage's bars.</summary>
    public const string Beam = "wood/wood_beam02";

    /// <summary>The sky over the town, and down the shaft onto level 1's cage.</summary>
    public const string Sky = "sky_badlands_01";

    /// <summary>The lantern hung by the shaft (a <c>prop_dynamic</c>: it is not compiled as a static prop).</summary>
    public const string LanternModel = "models/props_2fort/lantern001.mdl";

    /// <summary>The light a lantern gives.</summary>
    public static Lamp Lantern { get; } = new(Colour: "255 196 120", Brightness: 120, FiftyPercent: 192, ZeroPercent: 512);

    /// <summary>The afternoon sun (<c>light_environment</c>).</summary>
    public static Sun Sun { get; } = new(Angles: "-50 35 0", Pitch: "-50", Light: "255 232 196 450", Ambient: "126 138 160 70");
}
