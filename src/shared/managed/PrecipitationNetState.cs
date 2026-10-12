using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a precipitation volume tells the client: which kind of weather, and how
/// heavy.
///
/// <para>
/// Two numbers, and stock networks only the first — the second travels as the
/// entity's RENDER COLOUR ALPHA, which the client remaps into a density. That
/// is a strange place to keep the weather, and it is where a mapper puts it:
/// the key is "renderamt", and any map that has ever tuned its rain has tuned it
/// there. So the value is carried here explicitly rather than smuggled through a
/// colour, and the native side puts it back into the alpha the renderer reads.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.FuncPrecipitation)]
public partial class PrecipitationNetState
{
    /// <summary>Rain, snow, ash or snowfall. See <see cref="PrecipitationType"/>.</summary>
    [NetField(Bits = 3)] public partial int PrecipType { get; set; }

    /// <summary>How heavy, 0..255. The map's "renderamt".</summary>
    [NetField(Bits = 8)] public partial byte Density { get; set; }
}

/// <summary>
/// The kinds of weather a func_precipitation can be. Must match
/// PrecipitationType_t in game/shared/precipitation_shared.h — these are the
/// numbers the client's renderer switches on.
/// </summary>
public enum PrecipitationType
{
    Rain = 0,
    Snow = 1,
    Ash = 2,

    /// <summary>
    /// The heavier snow, which is drawn by a process-global manager rather than
    /// per volume — one of these builds it and the rest share it.
    /// </summary>
    SnowFall = 3,
}
