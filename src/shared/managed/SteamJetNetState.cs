using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a steam jet tells the client: how fast the gas comes out, how far it
/// gets, how wide it spreads, how big the puffs are at each end, and whether it
/// is on.
///
/// <para>
/// Stock networks exactly these eleven values, and none of them is a rendering
/// decision. How long a puff lives is not even one of them — the client divides
/// the jet's length by its speed to get that, which is the whole reason the
/// length is a mapper's key rather than a lifetime is.
/// </para>
///
/// <para>
/// Declared once and used by both classnames through
/// <see cref="EnvSteamNetState"/> and <see cref="EnvSteamJetNetState"/>, which
/// exist only because a table is keyed by classname and stock registers one
/// class under two names. The FIELDS are here, in one place, so the two tables
/// cannot drift apart — the same shape <see cref="IDustNetState"/> has.
/// </para>
/// </summary>
public interface ISteamJetNetState
{
    /// <summary>How fast a puff drifts sideways as it travels. The width of the plume.</summary>
    float SpreadSpeed { get; set; }

    /// <summary>How fast a puff leaves the nozzle. With the length, this is what sets a puff's lifetime.</summary>
    float Speed { get; set; }

    float StartSize { get; set; }
    float EndSize { get; set; }

    /// <summary>Puffs per second.</summary>
    float Rate { get; set; }

    /// <summary>How far the jet reaches. The client divides this by the speed to get a puff's lifetime.</summary>
    float JetLength { get; set; }

    /// <summary>Emitting. Driven by TurnOn, TurnOff, Toggle and by a non-player Use.</summary>
    bool Emit { get; set; }

    /// <summary>
    /// Point left rather than forward.
    ///
    /// The whole of what separates the two classnames, and it is a
    /// compatibility fact rather than a mapper's choice: the legacy
    /// env_steamjet faced left, env_steam faces forward, and stock decides which
    /// by asking its own classname in Spawn.
    /// </summary>
    bool FaceLeft { get; set; }

    /// <summary>0 is normal steam, 1 is a heat haze. STEAM_NORMAL and STEAM_HEATWAVE.</summary>
    int Type { get; set; }

    /// <summary>Bit 0 is SF_EMISSIVE: steam that glows rather than being lit.</summary>
    int SpawnFlags { get; set; }

    /// <summary>How fast a puff turns as it travels.</summary>
    float RollSpeed { get; set; }

    /// <summary>Tints the lighting ramp. The mapper's rendercolor, which stock reads off the entity rather than off DT_SteamJet.</summary>
    byte Red { get; set; }

    byte Green { get; set; }
    byte Blue { get; set; }
}

/// <summary>env_steam's table. See <see cref="ISteamJetNetState"/> for what the fields mean.</summary>
[StandaloneNetState(EntityNames.EnvSteam)]
public partial class EnvSteamNetState : ISteamJetNetState
{
    public partial float SpreadSpeed { get; set; }
    public partial float Speed { get; set; }
    public partial float StartSize { get; set; }
    public partial float EndSize { get; set; }
    public partial float Rate { get; set; }
    public partial float JetLength { get; set; }

    [NetField(Bits = 1)] public partial bool Emit { get; set; }
    [NetField(Bits = 1)] public partial bool FaceLeft { get; set; }

    public partial int Type { get; set; }

    // UNSIGNED, said explicitly, because this is a BITMASK and eight signed
    // bits stop at 127. Nothing set bit 7 in this tree, which is why it went
    // unnoticed: the value went on the wire as a negative and came back as
    // one, and no map had ever asked. Packing made the field's storage eight
    // bits as well as its wire, so the same mistake became a value refused
    // rather than a value quietly wrong -- and the fix is to say what a flags
    // word is rather than to widen it.
    [NetField(NetFieldKind.UInt, Bits = 8)] public partial int SpawnFlags { get; set; }

    public partial float RollSpeed { get; set; }

    [NetField(Bits = 8)] public partial byte Red { get; set; }
    [NetField(Bits = 8)] public partial byte Green { get; set; }
    [NetField(Bits = 8)] public partial byte Blue { get; set; }
}

/// <summary>env_steamjet's table. The same fields; see <see cref="ISteamJetNetState"/>.</summary>
[StandaloneNetState(EntityNames.EnvSteamjet)]
public partial class EnvSteamJetNetState : ISteamJetNetState
{
    public partial float SpreadSpeed { get; set; }
    public partial float Speed { get; set; }
    public partial float StartSize { get; set; }
    public partial float EndSize { get; set; }
    public partial float Rate { get; set; }
    public partial float JetLength { get; set; }

    [NetField(Bits = 1)] public partial bool Emit { get; set; }
    [NetField(Bits = 1)] public partial bool FaceLeft { get; set; }

    public partial int Type { get; set; }

    // UNSIGNED, said explicitly, because this is a BITMASK and eight signed
    // bits stop at 127. Nothing set bit 7 in this tree, which is why it went
    // unnoticed: the value went on the wire as a negative and came back as
    // one, and no map had ever asked. Packing made the field's storage eight
    // bits as well as its wire, so the same mistake became a value refused
    // rather than a value quietly wrong -- and the fix is to say what a flags
    // word is rather than to widen it.
    [NetField(NetFieldKind.UInt, Bits = 8)] public partial int SpawnFlags { get; set; }

    public partial float RollSpeed { get; set; }

    [NetField(Bits = 8)] public partial byte Red { get; set; }
    [NetField(Bits = 8)] public partial byte Green { get; set; }
    [NetField(Bits = 8)] public partial byte Blue { get; set; }
}
