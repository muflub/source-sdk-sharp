using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a smoke volume tells the client: two colours to blend between, a
/// material, and the numbers that decide how thick the smoke is and how it
/// moves.
///
/// <para>
/// The two colours are not a gradient in space. Each puff picks a point between
/// them and keeps it, so a volume reads as smoke of one general colour with
/// variation in it rather than as a wall of one flat tone — which is the whole
/// difference between this and a fog volume.
/// </para>
///
/// <para>
/// The spacing is the number that costs: the client divides the brush into a
/// 3D grid of cells of that size and holds a particle slot per cell, so halving
/// it multiplies the grid by eight. Stock networks it and so does this, because
/// it is a level designer's dial and not a graphics setting.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.FuncSmokevolume)]
public partial class SmokeVolumeNetState
{
    [NetField(Bits = 8)] public partial byte Red1 { get; set; }
    [NetField(Bits = 8)] public partial byte Green1 { get; set; }
    [NetField(Bits = 8)] public partial byte Blue1 { get; set; }

    [NetField(Bits = 8)] public partial byte Red2 { get; set; }
    [NetField(Bits = 8)] public partial byte Green2 { get; set; }
    [NetField(Bits = 8)] public partial byte Blue2 { get; set; }

    /// <summary>The material each puff is drawn with. A name, because the client looks it up in the particle manager rather than in the model list.</summary>
    [NetField(Bits = 128)] public partial string Material { get; set; }

    public partial float ParticleDrawWidth { get; set; }

    /// <summary>Cell size of the grid the volume is divided into. Halving it multiplies the particle count by eight.</summary>
    public partial float ParticleSpacingDistance { get; set; }

    /// <summary>How fast the volume thins out or thickens up when the density changes.</summary>
    public partial float DensityRampSpeed { get; set; }

    public partial float RotationSpeed { get; set; }
    public partial float MovementSpeed { get; set; }

    /// <summary>0 to 1. The one value a map drives at runtime, through SetDensity.</summary>
    public partial float Density { get; set; }

    /// <summary>Bit 0 is SF_EMISSIVE: smoke that glows rather than being lit.</summary>
    public partial int SpawnFlags { get; set; }
}
