using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a volume of embers tells the client: how many, how long each lives, how
/// fast it rises, and whether it is emitting.
///
/// <para>
/// Stock networks exactly these four, and the interesting one is the density,
/// which is not a count but a RATE: the client feeds it to a TimedEvent and
/// spawns that many embers a second. It is read once, when the effect starts,
/// which is why a map that could drive it would not see it change.
/// </para>
///
/// <para>
/// Where the embers come out is not here and never was. Each one is placed at a
/// random point inside the entity's own brush, so the volume the mapper drew is
/// the emitter's shape — see <c>C_Embers::SpawnEmber</c>.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.EnvEmbers)]
public partial class EmbersNetState
{
    /// <summary>Embers per second. Stock's "density" key, which is a rate rather than a count.</summary>
    public partial int Density { get; set; }

    /// <summary>How long an ember lasts, in seconds.</summary>
    public partial int Lifetime { get; set; }

    /// <summary>How fast an ember rises.</summary>
    public partial int Speed { get; set; }

    /// <summary>Emitting. Driven by Use, and by whether the mapper named the entity at all.</summary>
    [NetField(Bits = 1)] public partial bool Emit { get; set; }
}
