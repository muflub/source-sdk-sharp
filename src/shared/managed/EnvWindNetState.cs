using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What env_wind tells the client: the parameters of the whole wind, not the
/// wind itself.
///
/// <para>
/// Stock's DT_EnvWindShared is these same twelve values, and the comment on
/// its send props says why two of them are NOSCALE: the client runs the
/// ENTIRE gust simulation itself, deterministically, from the seed and the
/// start time — so the numbers must arrive *exactly*, not quantised, or the
/// two sides' winds drift apart. This port keeps that shape: the server
/// decides the parameters once at spawn, the client's stock CEnvWindShared
/// (kept alive by shell_effects_sim.cpp) simulates every gust from them.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.EnvWind)]
public partial class EnvWindNetState
{
    /// <summary>Sustained wind, minimum, in units per second.</summary>
    [NetField(NetFieldKind.UInt, Bits = 10)] public partial int MinWind { get; set; }

    /// <summary>Sustained wind, maximum.</summary>
    [NetField(NetFieldKind.UInt, Bits = 10)] public partial int MaxWind { get; set; }

    /// <summary>A gust's added speed, minimum.</summary>
    [NetField(NetFieldKind.UInt, Bits = 10)] public partial int MinGust { get; set; }

    /// <summary>A gust's added speed, maximum.</summary>
    [NetField(NetFieldKind.UInt, Bits = 10)] public partial int MaxGust { get; set; }

    /// <summary>Seconds between gusts, minimum. Exact — see the class note.</summary>
    public partial float MinGustDelay { get; set; }

    /// <summary>Seconds between gusts, maximum.</summary>
    public partial float MaxGustDelay { get; set; }

    /// <summary>How many degrees a gust may swing the direction.</summary>
    [NetField(NetFieldKind.UInt, Bits = 9)] public partial int GustDirChange { get; set; }

    /// <summary>How long one gust blows.</summary>
    public partial float GustDuration { get; set; }

    /// <summary>The seed both sides' simulations run from. A full int, no packing: it is 32 random bits.</summary>
    public partial int WindSeed { get; set; }

    /// <summary>Where the wind blows from at level start: the entity's yaw, in degrees.</summary>
    [NetField(NetFieldKind.UInt, Bits = 9)] public partial int InitialWindDir { get; set; }

    /// <summary>How hard it blows at level start.</summary>
    public partial float InitialWindSpeed { get; set; }

    /// <summary>When the simulation started, on the server's clock. Exact.</summary>
    public partial float StartTime { get; set; }
}
