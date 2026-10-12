using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a dust volume tells the client. Everything a mote's life is made of:
/// colour, how many, how big, how fast, how long, and how far away it is still
/// worth drawing one.
///
/// <para>
/// None of this is a rendering decision, which is the point of moving it. What
/// colour the dust in a room is and how thick it hangs is a level design
/// decision that happens to be carried out by a particle system, and stock
/// already networks exactly these numbers — the entity on the server has never
/// been anything but a volume and a set of keys.
/// </para>
///
/// <para>
/// Declared once and used by both dust classes through
/// <see cref="DustMotesNetState"/> and <see cref="DustCloudNetState"/>, which
/// exist only because a table is keyed by classname and there are two
/// classnames. The FIELDS are here, in one place, so the two tables cannot
/// drift apart.
/// </para>
/// </summary>
public interface IDustNetState
{
    byte Red { get; set; }
    byte Green { get; set; }
    byte Blue { get; set; }

    /// <summary>Peak opacity of a mote. The map's "Alpha", which stock has to reapply after parsing because colour parsing stamps on it.</summary>
    byte Alpha { get; set; }

    /// <summary>Motes per second.</summary>
    int SpawnRate { get; set; }

    float SizeMin { get; set; }
    float SizeMax { get; set; }

    /// <summary>How fast a mote drifts, in any direction, before the wind takes over.</summary>
    int SpeedMax { get; set; }

    int LifetimeMin { get; set; }
    int LifetimeMax { get; set; }

    /// <summary>Past this distance from the viewer a mote is not drawn at all.</summary>
    int DistMax { get; set; }

    /// <summary>Extra downward drift, over and above the drift speed. Dust falls; a cloud need not.</summary>
    float FallSpeed { get; set; }

    /// <summary>On, screen-scaled, frozen. DUSTFLAGS_ in func_dust_shared.h.</summary>
    int DustFlags { get; set; }
}

/// <summary>func_dustmotes' table. See <see cref="IDustNetState"/> for what the fields mean.</summary>
[StandaloneNetState(EntityNames.FuncDustmotes)]
public partial class DustMotesNetState : IDustNetState
{
    [NetField(Bits = 8)] public partial byte Red { get; set; }
    [NetField(Bits = 8)] public partial byte Green { get; set; }
    [NetField(Bits = 8)] public partial byte Blue { get; set; }
    [NetField(Bits = 8)] public partial byte Alpha { get; set; }

    public partial int SpawnRate { get; set; }
    public partial float SizeMin { get; set; }
    public partial float SizeMax { get; set; }
    public partial int SpeedMax { get; set; }
    public partial int LifetimeMin { get; set; }
    public partial int LifetimeMax { get; set; }
    public partial int DistMax { get; set; }
    public partial float FallSpeed { get; set; }
    public partial int DustFlags { get; set; }
}

/// <summary>func_dustcloud's table. The same fields; see <see cref="IDustNetState"/>.</summary>
[StandaloneNetState(EntityNames.FuncDustcloud)]
public partial class DustCloudNetState : IDustNetState
{
    [NetField(Bits = 8)] public partial byte Red { get; set; }
    [NetField(Bits = 8)] public partial byte Green { get; set; }
    [NetField(Bits = 8)] public partial byte Blue { get; set; }
    [NetField(Bits = 8)] public partial byte Alpha { get; set; }

    public partial int SpawnRate { get; set; }
    public partial float SizeMin { get; set; }
    public partial float SizeMax { get; set; }
    public partial int SpeedMax { get; set; }
    public partial int LifetimeMin { get; set; }
    public partial int LifetimeMax { get; set; }
    public partial int DistMax { get; set; }
    public partial float FallSpeed { get; set; }
    public partial int DustFlags { get; set; }
}

/// <summary>
/// The three bits a dust volume's behaviour is made of. Must match DUSTFLAGS_ in
/// game/shared/func_dust_shared.h, which is what the client's particle system
/// reads.
/// </summary>
[Flags]
public enum DustFlags
{
    /// <summary>Emitting. Cleared by TurnOff and by the map's StartDisabled.</summary>
    On = 1 << 0,

    /// <summary>
    /// Motes keep the same size on SCREEN rather than in the world, so they do
    /// not shrink with distance. What separates func_dustmotes from
    /// func_dustcloud, and the only difference between the two entities.
    /// </summary>
    ScaleMotes = 1 << 1,

    /// <summary>Emit one batch and then stop and hold it. Dust in a shaft of light, not falling.</summary>
    Frozen = 1 << 2,
}
