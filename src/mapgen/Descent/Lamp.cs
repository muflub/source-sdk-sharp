using System.Globalization;

namespace SourceSharp.MapGen.Descent;

/// <summary>
/// A <c>light</c> entity's settings: its colour, its brightness (the fourth
/// number of <c>_light</c>) and the distances at which it falls to half and to
/// nothing.
/// </summary>
/// <param name="Colour">Red, green and blue, 0 to 255, space separated.</param>
/// <param name="Brightness">The fourth number of <c>_light</c>.</param>
/// <param name="FiftyPercent"><c>_fifty_percent_distance</c>: where the light is at half.</param>
/// <param name="ZeroPercent"><c>_zero_percent_distance</c>: where it is gone.</param>
/// <remarks>
/// The defaults are a work lamp for a grey room, measured (Phase 1, with
/// map-tools' <c>ssmap preview</c>): see <see cref="Default"/>. A falloff is
/// always given, so a lamp lights its own room and the next and fades
/// before the room after, which is all the rooms pipeline carries light
/// through anyway (one doorway).
/// </remarks>
public sealed record Lamp(string Colour = Lamp.DefaultColour, int Brightness = Lamp.DefaultBrightness,
                          int FiftyPercent = Lamp.DefaultFifty, int ZeroPercent = Lamp.DefaultZero)
{
    /// <summary>Warm white.</summary>
    public const string DefaultColour = "255 226 190";

    /// <summary>The default brightness.</summary>
    public const int DefaultBrightness = 200;

    /// <summary>The default distance to half light.</summary>
    public const int DefaultFifty = 256;

    /// <summary>The default distance to none.</summary>
    public const int DefaultZero = 768;

    /// <summary>
    /// A lamp of every default. Measured on a grey chamber (1024 square, 512
    /// high, a lamp 64 under the ceiling) joined to a framed drift (256 wide
    /// and high, a lamp 32 under it), linked and previewed with map-tools at
    /// 2b3bd46: <c>checks.lighting</c> mean 0.13, clipped 0.012, black 0.011.
    /// At 400 the clipped share is 0.043; a longer falloff (384 to 1024)
    /// gives 0.24 but 0.040 clipped. Phase 0's lamps (300 to 600, no
    /// falloff keys) measured 1.41 mean and 0.57 clipped.
    /// </summary>
    public static Lamp Default { get; } = new();

    /// <summary>The <c>_light</c> value.</summary>
    public string Light => string.Create(CultureInfo.InvariantCulture, $"{Colour} {Brightness}");

    /// <summary>The keys of the <c>light</c> entity, in the order a map file lists them (after its origin).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Keys =>
    [
        new("_light", Light),
        new("_lightHDR", "-1 -1 -1 1"),
        new("_fifty_percent_distance", Num(FiftyPercent)),
        new("_zero_percent_distance", Num(ZeroPercent)),
    ];

    /// <summary>The <c>light</c> entity at <paramref name="at"/>.</summary>
    public VmfEntity Entity(Point at)
    {
        var e = new VmfEntity { ClassName = "light" }.Set("origin", at.ToString());
        foreach (var kv in Keys)
            e.Set(kv.Key, kv.Value);
        return e;
    }

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
}
