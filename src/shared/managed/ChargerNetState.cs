using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a wall charger tells the client: the sequence it shows and the cycle
/// it holds that sequence at. Stock networks both through DT_BaseAnimating
/// and never advances them (CNewWallHealth and CNewRecharge set the cycle
/// themselves, at a playback rate of 0): "idle" at 1 - juice/max as it
/// drains, "emptyclick" at its last frame once it is dry.
/// </summary>
public interface IChargerNetState
{
    /// <summary>The model's sequence index.</summary>
    int Sequence { get; set; }

    /// <summary>Where in it, 0 to 1.</summary>
    float Cycle { get; set; }
}

/// <summary>item_healthcharger's table. See <see cref="IChargerNetState"/>.</summary>
[StandaloneNetState(EntityNames.ItemHealthcharger)]
public partial class HealthChargerNetState : IChargerNetState
{
    public partial int Sequence { get; set; }
    public partial float Cycle { get; set; }
}

/// <summary>item_suitcharger's table. See <see cref="IChargerNetState"/>.</summary>
[StandaloneNetState(EntityNames.ItemSuitcharger)]
public partial class SuitChargerNetState : IChargerNetState
{
    public partial int Sequence { get; set; }
    public partial float Cycle { get; set; }
}
