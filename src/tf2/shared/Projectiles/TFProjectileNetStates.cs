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
