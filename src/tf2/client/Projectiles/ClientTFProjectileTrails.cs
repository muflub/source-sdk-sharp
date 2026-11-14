//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;

using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Shared.Projectiles;

namespace TF2.Client.Projectiles;

/// <summary>
/// The trail a TF projectile's mirror wears, the client halves of
/// <c>C_TFBaseRocket::CreateTrails</c> (<c>c_tf_projectile_rocket.cpp:226</c>)
/// and <c>CTFGrenadePipebombProjectile::CreateTrailParticles</c>
/// (<c>tf_weapon_grenade_pipebomb.cpp:357</c>). Stock attaches a particle
/// system at <c>PATTACH_ABSORIGIN_FOLLOW</c> — here the native shell feeds
/// the mirror's origin to the running system's control point 0 every frame
/// (<c>ShellParticles_PreRender</c>), which is that attachment's effect.
///
/// <para>
/// <b>THE RE-CREATE TRIGGER IS THE WIRE, NOT THE DEFLECT FLAG.</b> Stock
/// re-runs CreateTrails when <c>m_iCachedDeflect != GetDeflected()</c>; the
/// deflection is not itself networked (stock's <c>DT_TFProjectile_Rocket</c>
/// carries only <c>m_bCritical</c>), so the visible wire difference is the
/// retargeted team and the crit — the mirror restarts when that pair
/// changes, which is every case stock's re-run changes a name for. A
/// deflection that changes neither (same-team deflector) would restart the
/// same name in stock and is skipped here; nothing observable differs.
/// </para>
///
/// <para>
/// <b>TWO SYSTEMS PER MIRROR.</b> Stock's crit glow is a SECOND simultaneous
/// system (<c>pEffectCrit</c>, stopped before a re-create and nulled on
/// remove), beside the trail — <c>ParticleSystemStartExtra</c> is that slot.
/// A shot that is not critical wears no glow.
/// </para>
/// </summary>
public abstract class TFProjectileTrail : ClientStandaloneEntity
{
    private protected readonly IEngineEffects Effects;

    /// <summary>The name of the system running, or null when none.</summary>
    private string? _running;

    /// <summary>The name of the crit glow running, or null when none.</summary>
    private string? _extraRunning;

    /// <summary>The last (team, crit, pulsed) triple seen, and whether any was.</summary>
    private (int Team, bool Critical, bool Pulsed) _seen;
    private bool _seenAny;

    protected TFProjectileTrail(IEngineEntityRender render, IEngineEffects effects)
        : base(render) => Effects = effects;

    /// <summary>Point 0 is the only one a trail reads — the origin it follows.</summary>
    protected const int FollowPoint = 0b1;

    /// <summary>The wire: the team and crit the server wrote.</summary>
    protected abstract (int Team, bool Critical) Wire();

    /// <summary>
    /// The one-shot flag the re-create rides when it is not the team or the
    /// crit — the stickybomb's pulse (stock's <c>m_bPulsed</c>), false for
    /// every mirror that has no such flag.
    /// </summary>
    protected virtual bool Pulsed() => false;

    /// <summary>The system this shot wears now.</summary>
    protected abstract string TrailName();

    /// <summary>The crit glow this shot wears now, or null for none.</summary>
    protected abstract string? CritName();

    /// <summary>
    /// What the Extra slot wears at the (re-)create. Stock's crit glow is
    /// that slot; the stickybomb's pulse, once on the wire, wears it instead
    /// — the shell carries two systems per mirror and stock's pulse is a
    /// third simultaneous one (a divergence the class doc names).
    /// </summary>
    protected virtual string? ExtraName() => CritName();

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        (int team, bool critical) = Wire();
        bool pulsed = Pulsed();
        if (_seenAny && _seen == (team, critical, pulsed) && _running is not null)
            return;

        bool onlyPulse = _seenAny && (_seen.Team, _seen.Critical) == (team, critical);
        _seen = (team, critical, pulsed);
        _seenAny = true;

        if (onlyPulse)
        {
            // The one-shot flag flipped alone: stock's Simulate starts the
            // pulse without re-running CreateTrails (the trail never
            // restarts for it), so only the Extra slot is re-worn.
            WearExtra(ExtraName());
            return;
        }

        Restart();
    }

    public override void OnRemoved()
    {
        if (_running is not null)
        {
            Effects.ParticleSystemStop(Handle);
            _running = null;
        }

        if (_extraRunning is not null)
        {
            Effects.ParticleSystemStopExtra(Handle);
            _extraRunning = null;
        }
    }

    /// <summary>
    /// Stock's stop-then-create: the running systems are released and fresh
    /// ones started under the names the shot now wears. The glow is stopped
    /// even when the new shot wears none, as stock nulls <c>pEffectCrit</c>
    /// before every re-create.
    /// </summary>
    private void Restart()
    {
        if (_running is not null)
            Effects.ParticleSystemStop(Handle);

        WearExtra(ExtraName());

        string name = TrailName();
        _running = null;
        if (!Effects.ParticleSystemStart(Handle, name))
        {
            Log.Warning($"[TF2/Projectiles] {name} did not start on {ClassName}\n");
            return;
        }

        _running = name;
        Effects.ParticleSystemFollowPoints(Handle, FollowPoint);
    }

    /// <summary>
    /// Stock's stop-then-create for the Extra slot: the glow (or the pulse)
    /// running there is released and <paramref name="name"/> started, as
    /// stock nulls <c>pEffectCrit</c> before every re-create.
    /// </summary>
    private protected void WearExtra(string? name)
    {
        if (_extraRunning is not null)
        {
            Effects.ParticleSystemStopExtra(Handle);
            _extraRunning = null;
        }

        if (name is not null && Effects.ParticleSystemStartExtra(Handle, name))
            _extraRunning = name;
    }
}

/// <summary>
/// The rocket's mirror: stock's <c>C_TFProjectile_Rocket::CreateTrails</c>
/// arms — no "trail" attachment, no trail; over water or unassigned team,
/// <c>rockettrail_underwater</c>; the launcher's <c>mini_rockets</c>,
/// <c>rockettrail_airstrike</c> (:101-117); else the launcher's
/// <c>no_self_blast_dmg</c>, <c>rockettrail_RocketJumper</c>
/// (<c>GetTrailParticleName</c>, :150-163); else <c>rockettrail</c>. The
/// launcher item is the owner's carrier row under the wire's weapon name
/// (<see cref="ITFWeaponOwners.ItemOf"/>), as stock reads it off the networked
/// launcher entity's item view.
///
/// <para>
/// <b>DIVERGENCE, the sticky's own.</b> The blast-jumping owner's
/// <c>rockettrail_airstrike_line</c> (:110-115) is a THIRD simultaneous
/// system in stock, beside trail and crit glow; the shell carries two slots,
/// so the line wears the Extra slot and the crit glow steps out of it while
/// it runs — the trade the stickybomb's pulse already made.
/// </para>
/// </summary>
[ClientStandaloneEntity(TFRocketNetState.ClassName)]
public sealed class ClientTFRocket : TFProjectileTrail
{
    private readonly IClientSplashCarryOut Splash;
    private readonly ITFPlayerData Players;
    private readonly ITFWeaponOwners Owners;
    private readonly AttributeHooks Attributes;

    public ClientTFRocket(IEngineEntityRender render, IEngineEffects effects, IClientSplashCarryOut splash,
                          ITFPlayerData players, ITFWeaponOwners owners, AttributeHooks attributes)
        : base(render, effects) => (Splash, Players, Owners, Attributes) = (splash, players, owners, attributes);

    public override string ClassName => TFRocketNetState.ClassName;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public TFRocketNetState State = new();

    protected override (int Team, bool Critical) Wire() => (State.TeamNumber, State.Critical);

    protected override string TrailName()
    {
        if (InWater() || State.TeamNumber == 0)
            return "rockettrail_underwater";

        if (Airstrike())
            return "rockettrail_airstrike";

        if (Launcher() is { } launcher
            && Attributes.Int(0, "no_self_blast_dmg", launcher) != 0)
            return "rockettrail_RocketJumper";

        return "rockettrail";
    }

    /// <summary>
    /// The blast-jump line wears the Extra slot when the shot is a mini
    /// rocket and its owner is blast-jumping (stock :110-115, the condition
    /// off the networked carrier, as <c>m_Shared.InCond</c> reads the
    /// networked words); the crit glow is the slot's fallback.
    /// </summary>
    protected override string? ExtraName()
        => Airstrike() && OwnerBlastjumping() ? "rockettrail_airstrike_line" : CritName();

    /// <summary>The fired-from launcher's item, the owner's carrier row.</summary>
    private IAttributeProvider? Launcher()
        => State.OwnerIndex > 0 ? Owners.ItemOf(State.OwnerIndex, State.WeaponName) : null;

    /// <summary>The launcher's <c>mini_rockets</c>: the airstrike's arm.</summary>
    private bool Airstrike()
        => Launcher() is { } launcher && Attributes.Int(0, "mini_rockets", launcher) != 0;

    /// <summary><c>TF_COND_BLASTJUMPING</c> off the owner's networked words.</summary>
    private bool OwnerBlastjumping()
        => State.OwnerIndex > 0 && Players.Networked(State.OwnerIndex) is { } owner
           && owner.InCond(TFCond.Blastjumping);

    /// <summary>
    /// Stock's <c>GetCritParticleName</c>
    /// (<c>c_tf_projectile_rocket.cpp:129</c>): blue, red, and every other
    /// team the eye boss.
    /// </summary>
    protected override string? CritName()
    {
        if (!State.Critical)
            return null;

        return State.TeamNumber switch
        {
            (int)TFTeam.Blue => "critical_rocket_blue",
            (int)TFTeam.Red => "critical_rocket_red",
            _ => "eyeboss_projectile",
        };
    }

    /// <summary>
    /// <c>GetPointContents(GetAbsOrigin()) &amp; MASK_WATER</c> at the
    /// create — the rocket's own origin, read through this mirror's index.
    /// </summary>
    private bool InWater()
    {
        if (!Effects.ClientEntityOrigin(Index, out Vector origin))
            return false;

        return ((uint)Splash.PointContents(origin) & IParticleMaskTrace.MaskWater) != 0;
    }

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        // "If we don't have a trail attachment, then we have no trail"
        // (c_tf_projectile_rocket.cpp): the create is refused, and a later
        // changed update may try again.
        if (Render.LookupAttachment(Handle, "trail") == 0)
            return;

        base.OnDataChanged(blob, updateType);
    }
}

/// <summary>
/// The pipebomb's mirror: <c>pipebombtrail_blue</c> or
/// <c>pipebombtrail_red</c> by the thrower's team — stock's
/// <c>GetTrailParticleName</c> falls to red for every team that is not
/// blue. No water arm: stock asks the contents only on the rocket.
/// </summary>
[ClientStandaloneEntity(TFPipebombNetState.ClassName)]
public sealed class ClientTFPipebomb : TFProjectileTrail
{
    public ClientTFPipebomb(IEngineEntityRender render, IEngineEffects effects)
        : base(render, effects) { }

    public override string ClassName => TFPipebombNetState.ClassName;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public TFPipebombNetState State = new();

    protected override (int Team, bool Critical) Wire() => (State.TeamNumber, State.Critical);

    protected override string TrailName() => State.TeamNumber == (int)TFTeam.Blue
        ? "pipebombtrail_blue"
        : "pipebombtrail_red";

    /// <summary>Stock's crit arm (<c>tf_weapon_grenade_pipebomb.cpp:309</c>).</summary>
    protected override string? CritName()
    {
        if (!State.Critical)
            return null;

        return State.TeamNumber switch
        {
            (int)TFTeam.Blue => "critical_pipe_blue",
            (int)TFTeam.Red => "critical_pipe_red",
            _ => null,
        };
    }
}

/// <summary>
/// The stickybomb's mirror: the remote-detonate pipe's own names —
/// <c>stickybombtrail_blue</c> / <c>stickybombtrail_red</c>, stock's
/// <c>HasStickyEffects()</c> arm — and the pulse,
/// <c>stickybomb_pulse_blue</c> / <c>stickybomb_pulse_red</c>
/// (<c>Simulate</c>, <c>tf_weapon_grenade_pipebomb.cpp:345-373</c>) once the
/// wire's <c>Pulsed</c> flips.
///
/// <para>
/// <b>DIVERGENCE.</b> Stock keeps the pulse running BESIDE the trail and the
/// crit glow — three simultaneous systems. The shell carries two slots per
/// mirror, so a pulsed bomb wears the pulse in the Extra slot and the crit
/// glow (if any) steps out of it: a bomb 0.8 s old keeps its trail either
/// way.
/// </para>
/// </summary>
[ClientStandaloneEntity(TFStickybombNetState.ClassName)]
public sealed class ClientTFStickybomb : TFProjectileTrail
{
    public ClientTFStickybomb(IEngineEntityRender render, IEngineEffects effects)
        : base(render, effects) { }

    public override string ClassName => TFStickybombNetState.ClassName;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public TFStickybombNetState State = new();

    protected override (int Team, bool Critical) Wire() => (State.TeamNumber, State.Critical);

    protected override bool Pulsed() => State.Pulsed;

    protected override string TrailName() => State.TeamNumber == (int)TFTeam.Blue
        ? "stickybombtrail_blue"
        : "stickybombtrail_red";

    /// <summary>Stock's pulse arm: red for the red team, blue for everyone else.</summary>
    private string PulseName() => State.TeamNumber == (int)TFTeam.Red
        ? "stickybomb_pulse_red"
        : "stickybomb_pulse_blue";

    /// <summary>
    /// Stock's crit arm through <c>HasStickyEffects()</c>
    /// (<c>tf_weapon_grenade_pipebomb.cpp:309</c>): the grenade names.
    /// </summary>
    protected override string? CritName()
    {
        if (!State.Critical)
            return null;

        return State.TeamNumber switch
        {
            (int)TFTeam.Blue => "critical_grenade_blue",
            (int)TFTeam.Red => "critical_grenade_red",
            _ => null,
        };
    }

    protected override string? ExtraName() => State.Pulsed ? PulseName() : CritName();
}

/// <summary>
/// The flare's mirror: stock's <c>GetFlareTrailParticleName</c>
/// (<c>c_tf_projectile_flare.cpp:55-83</c>) — <c>flaregun_trail_blue</c> or
/// <c>flaregun_trail_red</c> by the owner's team, the crit's name in the SAME
/// system (<c>flaregun_trail_crit_*</c>), no second glow as the rocket wears.
/// Attached at <c>PATTACH_ABSORIGIN_FOLLOW</c> (<c>CreateTrails</c>, :88-108),
/// the shell's point 0.
///
/// <para>
/// Not here (docs/plans/tf2/deferred.tsv): the Scorch Shot's
/// <c>scorchshot_trail_*</c> and the Manmelter's <c>drg_manmelter_projectile</c>
/// read the launcher's flare-gun mode (<c>C_TFFlareGun::GetFlareGunType</c>),
/// which waits on those weapons' modes as they do.
/// </para>
/// </summary>
[ClientStandaloneEntity(TFFlareNetState.ClassName)]
public sealed class ClientTFFlare : TFProjectileTrail
{
    public ClientTFFlare(IEngineEntityRender render, IEngineEffects effects)
        : base(render, effects) { }

    public override string ClassName => TFFlareNetState.ClassName;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public TFFlareNetState State = new();

    protected override (int Team, bool Critical) Wire() => (State.TeamNumber, State.Critical);

    protected override string TrailName()
    {
        return State.TeamNumber == (int)TFTeam.Blue
            ? Critical("flaregun_trail_blue", "flaregun_trail_crit_blue")
            : Critical("flaregun_trail_red", "flaregun_trail_crit_red");
    }

    /// <summary>The crit's name in the trail's own slot; stock keeps one system.</summary>
    private string Critical(string plain, string crit) => State.Critical ? crit : plain;

    /// <summary>The trail carries the crit; the Extra slot stays empty, as stock's
    /// flare has no second system.</summary>
    protected override string? CritName() => null;
}

/// <summary>
/// The jarate's mirror: stock's <c>C_TFProjectile_Jar::GetTrailParticleName</c>
/// (<c>tf_weapon_jar.cpp:766-776</c>) — the leaking jar's trail, by team and
/// nothing else. No crit glow: the glow names in that family are the
/// cleaver's (<c>tf_weapon_jar.cpp:1155-1165</c>).
/// </summary>
[ClientStandaloneEntity(TFJarNetState.ClassName)]
public sealed class ClientTFJar : TFProjectileTrail
{
    public ClientTFJar(IEngineEntityRender render, IEngineEffects effects)
        : base(render, effects) { }

    public override string ClassName => TFJarNetState.ClassName;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public TFJarNetState State = new();

    protected override (int Team, bool Critical) Wire() => (State.TeamNumber, State.Critical);

    protected override string TrailName() => State.TeamNumber == (int)TFTeam.Blue
        ? "peejar_trail_blu"
        : "peejar_trail_red";

    /// <summary>The trail is the whole wear; the Extra slot stays empty.</summary>
    protected override string? CritName() => null;
}

/// <summary>
/// The mad milk's mirror: stock's <c>C_TFProjectile_JarMilk</c> overrides
/// nothing the trail reads — the same leaking trail under the milk's name
/// (<c>tf_weapon_jar.h:196-216</c>), so the same declaration again, the
/// alias the wire rides.
/// </summary>
[ClientStandaloneEntity(TFJarNetState.MilkClassName)]
public sealed class ClientTFJarMilk : TFProjectileTrail
{
    public ClientTFJarMilk(IEngineEntityRender render, IEngineEffects effects)
        : base(render, effects) { }

    public override string ClassName => TFJarNetState.MilkClassName;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public TFJarNetState State = new();

    protected override (int Team, bool Critical) Wire() => (State.TeamNumber, State.Critical);

    protected override string TrailName() => State.TeamNumber == (int)TFTeam.Blue
        ? "peejar_trail_blu"
        : "peejar_trail_red";

    protected override string? CritName() => null;
}

/// <summary>
/// The gas jar's mirror: stock's <c>CTFProjectile_JarGas</c> derives from the
/// jar and overrides nothing the trail reads (<c>tf_weapon_jar_gas.h:69-90</c>),
/// so the leaking trail again, under the gas's name and the jar's table.
/// </summary>
[ClientStandaloneEntity(TFJarNetState.GasClassName)]
public sealed class ClientTFJarGas : TFProjectileTrail
{
    public ClientTFJarGas(IEngineEntityRender render, IEngineEffects effects)
        : base(render, effects) { }

    public override string ClassName => TFJarNetState.GasClassName;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public TFJarNetState State = new();

    protected override (int Team, bool Critical) Wire() => (State.TeamNumber, State.Critical);

    protected override string TrailName() => State.TeamNumber == (int)TFTeam.Blue
        ? "peejar_trail_blu"
        : "peejar_trail_red";

    protected override string? CritName() => null;
}

/// <summary>
/// The healing bolt's mirror: stock's
/// <c>C_TFProjectile_HealingBolt::OnDataChanged</c>
/// (<c>c_tf_projectile_arrow.cpp:194-210</c>) starts
/// <c>healshot_trail_blue</c> / <c>healshot_trail_red</c> by the owner's team
/// at <c>DATA_UPDATE_CREATED</c>, <c>PATTACH_ABSORIGIN_FOLLOW</c> — the
/// shell's point 0. No crit arm: the wire carries the team alone
/// (<see cref="TFHealingBoltNetState"/>), stock's empty
/// <c>DT_TFProjectile_HealingBolt</c> beside it.
/// </summary>
[ClientStandaloneEntity(TFHealingBoltNetState.ClassName)]
public sealed class ClientTFHealingBolt : TFProjectileTrail
{
    public ClientTFHealingBolt(IEngineEntityRender render, IEngineEffects effects)
        : base(render, effects) { }

    public override string ClassName => TFHealingBoltNetState.ClassName;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public TFHealingBoltNetState State = new();

    /// <summary>No crit rides the wire; the pair is the whole restart key.</summary>
    protected override (int Team, bool Critical) Wire() => (State.TeamNumber, false);

    protected override string TrailName() => State.TeamNumber == (int)TFTeam.Blue
        ? "healshot_trail_blue"
        : "healshot_trail_red";

    protected override string? CritName() => null;
}
