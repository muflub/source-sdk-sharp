//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Projectiles;

/// <summary>
/// <c>tf_projectile_rocket</c>'s networked state. Stock's
/// <c>DT_TFProjectile_Rocket</c> (<c>c_tf_projectile_rocket.cpp:29-33</c>)
/// carries <c>m_bCritical</c>; the team rides the entity's own team netvar,
/// which the client's <c>CreateTrails</c> reads as
/// <c>GetTeamNumber()</c> — here the projectile's row carries it, written
/// when the launcher arms the rocket and again when a deflection retargets
/// it (<c>CTFBaseRocket::Deflected</c>).
/// </summary>
[StandaloneNetState(ClassName)]
public partial class TFRocketNetState
{
    /// <summary>The entity's classname.</summary>
    public const string ClassName = "tf_projectile_rocket";

    /// <summary><c>m_bCritical</c>: the shot was fired critical.</summary>
    public partial bool Critical { get; set; }

    /// <summary>The owner's team, unassigned until the launcher arms it.</summary>
    public partial int TeamNumber { get; set; }

    /// <summary>
    /// The entity index of the player whose rocket this is — stock's networked
    /// <c>m_hOwnerEntity</c>, which the client's CreateTrails reads as
    /// <c>GetOwnerEntity()</c>. Zero is the world: no owner, no carrier to ask.
    /// </summary>
    public partial int OwnerIndex { get; set; }

    /// <summary>
    /// The weapon script whose launcher fired — stock's <c>m_hLauncher</c>
    /// (<c>GetLauncher()</c>), carried as the name the carrier's item row is
    /// looked up under, since the launcher entity itself is not networked here.
    /// </summary>
    [NetString(64)] public partial string WeaponName { get; set; }
}

/// <summary>
/// <c>tf_projectile_pipe</c>'s networked state: the critical flag and the
/// thrower's team the client's <c>GetTrailParticleName</c> and
/// <c>CreateTrailParticles</c> read (<c>tf_weapon_grenade_pipebomb.cpp</c>).
/// </summary>
[StandaloneNetState(ClassName)]
public partial class TFPipebombNetState
{
    /// <summary>The entity's classname.</summary>
    public const string ClassName = "tf_projectile_pipe";

    public partial bool Critical { get; set; }

    public partial int TeamNumber { get; set; }
}

/// <summary>
/// <c>tf_projectile_pipe_remote</c>'s networked state: the same two the
/// stickybomb's trail reads (stock's <c>HasStickyEffects()</c> arm names the
/// sticky systems), and the pulse. Stock's client times the pulse itself —
/// <c>Simulate</c> fires <c>stickybomb_pulse_*</c> when
/// <c>curtime - m_flCreationTime &gt;= GetLiveTime()</c>
/// (<c>tf_weapon_grenade_pipebomb.cpp:345-373</c>), on a creation time the
/// engine does not network — so the server decides here, at the arm think
/// (<see cref="TFProjectileDefs.StickyArmTime"/>, stock's default live
/// time) the client has no per-frame tick to stand in for.
/// </summary>
[StandaloneNetState(ClassName)]
public partial class TFStickybombNetState
{
    /// <summary>The entity's classname.</summary>
    public const string ClassName = "tf_projectile_pipe_remote";

    public partial bool Critical { get; set; }

    public partial int TeamNumber { get; set; }

    /// <summary><c>m_bPulsed</c>: the arm think ran, the bomb may wear its pulse.</summary>
    public partial bool Pulsed { get; set; }
}

/// <summary>
/// <c>tf_projectile_flare</c>'s networked state: the team and critical flag
/// the client's trail reads (<c>GetFlareTrailParticleName</c>,
/// <c>c_tf_projectile_flare.cpp:55-83</c>). The pair is the whole wire the
/// flare's trail asks: stock's crit wears <c>flaregun_trail_crit_*</c> in the
/// ONE system, no second glow beside it as the rocket's has.
/// </summary>
[StandaloneNetState(ClassName)]
public partial class TFFlareNetState
{
    /// <summary>The entity's classname.</summary>
    public const string ClassName = "tf_projectile_flare";

    public partial bool Critical { get; set; }

    /// <summary>The owner's team, unassigned until the launcher arms it.</summary>
    public partial int TeamNumber { get; set; }
}

/// <summary>
/// <c>tf_projectile_jar</c>'s networked state: the same pair the flare's
/// trail reads, the jar's own (<c>c_tf_projectile_jar.cpp</c>'s
/// <c>GetTrailParticleName</c> over the team). Both jar faces wear it — the
/// milk is <c>tf_projectile_jar_milk</c>'s alias of the same table, its
/// classname the one field apart the mirror's <see cref="ClassName"/> rows
/// are written by. The Gas Passer's jar (<c>tf_projectile_jar_gas</c>) is the
/// third face, under the same table.
/// </summary>
[StandaloneNetState(TFJarNetState.ClassName)]
[StandaloneNetStateAlias(MilkClassName)]
[StandaloneNetStateAlias(GasClassName)]
public partial class TFJarNetState
{
    /// <summary>The entity's classname.</summary>
    public const string ClassName = "tf_projectile_jar";

    /// <summary>The milk, networked under the jar's table.</summary>
    public const string MilkClassName = "tf_projectile_jar_milk";

    /// <summary>The Gas Passer's jar, networked under the jar's table.</summary>
    public const string GasClassName = "tf_projectile_jar_gas";

    public partial bool Critical { get; set; }

    /// <summary>The owner's team, unassigned until the launcher arms it.</summary>
    public partial int TeamNumber { get; set; }
}

/// <summary>
/// <c>tf_projectile_healing_bolt</c>'s networked state. Stock's
/// <c>DT_TFProjectile_HealingBolt</c> is empty
/// (<c>tf_projectile_arrow.cpp:74-75</c>): the client's trail asks the team
/// alone (<c>C_TFProjectile_HealingBolt::OnDataChanged</c>,
/// <c>c_tf_projectile_arrow.cpp:194-210</c>), which rides the entity's own
/// team netvar there and the row here.
/// </summary>
[StandaloneNetState(ClassName)]
public partial class TFHealingBoltNetState
{
    /// <summary>The entity's classname.</summary>
    public const string ClassName = "tf_projectile_healing_bolt";

    /// <summary>The owner's team, unassigned until the launcher arms it.</summary>
    public partial int TeamNumber { get; set; }
}
