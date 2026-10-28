//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// npc_satchel: the SLAM's satchel charge, thrown or stuck to a wall, which
/// goes off when its thrower presses the detonator. Stock's
/// <c>CSatchelCharge</c> (grenade_satchel.cpp).
///
/// <para>
/// A physics prop, as stock's is (<c>VPhysicsInitNormal</c>,
/// <c>MOVETYPE_VPHYSICS</c>), in the weapon collision group, with the red
/// glow that says it is live. One stuck to a wall has its motion off, which is
/// what stock's <c>MOVETYPE_FLY</c> with no gravity comes to.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.NpcSatchel, ScriptedEntityKind.Prop)]
[Precache(PrecacheKind.Model, Satchel.Model)]
[Precache(PrecacheKind.Model, Satchel.GlowSprite)]
[Precache(PrecacheKind.Sound, Satchel.GrenadeExplodeSound)]
public sealed class Satchel : ScriptedEntity
{
    public Satchel(IEngineDamage damage, IEngineWorldQuery world, IEngineEffects effects,
                   IEnginePrecache precache, IEngineHost host, IBlastSweep blast, IServerDecals decals,
                   IEngineSound sound, IScreenEffects screen)
    {
        Damage = damage;
        World = world;
        Effects = effects;
        Precache = precache;
        Host = host;
        Blast = blast;
        Decals = decals;
        Sound = sound;
        Screen = screen;
    }

    private readonly IEngineSound Sound;
    private readonly IScreenEffects Screen;
    private readonly IEngineDamage Damage;
    private readonly IEngineWorldQuery World;
    private readonly IEngineEffects Effects;
    private readonly IEnginePrecache Precache;
    private readonly IEngineHost Host;
    private readonly IBlastSweep Blast;
    private readonly IServerDecals Decals;

    public const string Model = "models/Weapons/w_slam.mdl";

    /// <summary>SLAM_SPRITE.</summary>
    public const string GlowSprite = "sprites/redglow1.vmt";

    /// <summary><c>SetDamage( 150 )</c> in Spawn.</summary>
    public const float BlastDamage = 150f;

    /// <summary>InputExplode's radius.</summary>
    public const float BlastRadius = 200f;

    /// <summary>How long after the detonator is pressed a satchel goes off (SatchelDetonate's event delay).</summary>
    public const float DetonateDelay = 0.20f;

    /// <summary>InputExplode lifts the blast this far off the satchel.</summary>
    public static Vector BlastLift => new(0f, 0f, 16f);

    /// <summary>ExplosionCreate's flags in InputExplode.</summary>
    public const EnvExplosion.SpawnFlag BlastFlags =
        EnvExplosion.SpawnFlag.NoSparks | EnvExplosion.SpawnFlag.NoDynamicLights | EnvExplosion.SpawnFlag.NoSmoke;

    private const string ExplodeContext = "explode";

    // Carried across a hot reload: a satchel rebuilt without its thrower is
    // one no detonator can find.
    [SaveField] private EntityHandle _glow = EntityHandle.None;
    [SaveField] private bool _exploded;

    /// <summary>Who threw it: whose detonator sets it off, and who the blast is scored to.</summary>
    [SaveField] public EntityHandle Thrower { get; private set; } = EntityHandle.None;

    /// <summary><c>m_bIsLive</c>: thrown by somebody and not yet gone off.</summary>
    [SaveField] public bool IsLive { get; private set; }

    /// <summary><c>m_bIsAttached</c>: stuck to a wall rather than thrown.</summary>
    [SaveField] public bool IsAttached { get; private set; }

    /// <summary>Spawn's UTIL_SetSize: the box it flies as.</summary>
    public static Vector ThrownMins => new(-6f, -6f, -2f);
    /// <inheritdoc cref="ThrownMins"/>
    public static Vector ThrownMaxs => new(6f, 6f, 2f);

    /// <summary>SatchelThink's resize once it is on a wall, "so player can pick up off wall".</summary>
    public static Vector AttachedMins => new(-2f, -2f, -6f);
    /// <inheritdoc cref="AttachedMins"/>
    public static Vector AttachedMaxs => new(2f, 2f, 6f);

    /// <summary>SatchelThink's period.</summary>
    public const float ThinkPeriod = 0.1f;

    /// <summary>MAX_COORD_INTEGER: past it, IsInWorld says no.</summary>
    private const float MaxCoord = 16384f;

    // Stock's GetOwnerEntity, which the shell answers nothing for: the thrower
    // owns it (and so does not collide with it) until it is clear of them.
    [SaveField] private bool _heldByThrower;
    [SaveField] private Vector _lastPosition;

    public override void OnSpawn()
    {
        SetModel(Model);
        Entities.SetBounds(Handle, ThrownMins, ThrownMaxs);
        SetCollisionGroup(CollisionGroup.Weapon);
        CreateEffects();
        SetNextThink(ThinkPeriod);
    }

    /// <summary>
    /// CSatchelCharge::SatchelThink: the small box once on a wall; the thrower
    /// let go of once the satchel can rise five units with nothing in the way
    /// (they can shoot their own charge from then on); still once it stops
    /// moving; gone once it has left the world.
    /// </summary>
    public override void OnThink()
    {
        if (IsAttached)
            Entities.SetBounds(Handle, AttachedMins, AttachedMaxs);

        Vector at = Origin;
        if (_heldByThrower)
        {
            Entities.SetOwner(Handle, EntityHandle.None);
            Vector upABit = at + new Vector(0f, 0f, 5f);
            var (mins, maxs) = Entities.GetBounds(Handle);
            var tr = World.EntityTraceHull(Handle, at, upABit, mins, maxs, EnginePushWorld.MaskSolid,
                                           (int)CollisionGroup.Weapon);
            if (tr.StartSolid || (tr.EndPos - upABit).LengthSquared > 0f)
                Entities.SetOwner(Handle, Thrower);
            else
                _heldByThrower = false;
        }
        else if ((at - _lastPosition).LengthSquared < 1f)
        {
            // SetThink( NULL ): at rest, it thinks no more.
            return;
        }
        _lastPosition = at;

        SetNextThink(ThinkPeriod);

        if (MathF.Abs(at.X) > MaxCoord || MathF.Abs(at.Y) > MaxCoord || MathF.Abs(at.Z) > MaxCoord)
            Remove();
    }

    /// <summary>Thrown (or stuck) by <paramref name="thrower"/>, and live from now.</summary>
    public void ThrownBy(EntityHandle thrower, bool attached)
    {
        Thrower = thrower;
        IsLive = true;
        IsAttached = attached;

        // Create( "npc_satchel", ..., GetOwner() ): the thrower owns it.
        Entities.SetOwner(Handle, thrower);
        _heldByThrower = true;

        if (attached)
            EnableMotion(false);
    }

    /// <summary>The detonator: goes off <see cref="DetonateDelay"/> from now.</summary>
    public void Detonate() => SetContextThink(ExplodeContext, DetonateDelay);

    public override void OnThink(string context)
    {
        if (context == ExplodeContext)
            Explode();
    }

    [ScriptedEvent("Explode")]
    private void OnExplodeInput() => Explode();

    /// <summary><c>CSatchelCharge::InputExplode</c>.</summary>
    private void Explode()
    {
        if (_exploded)
            return;

        _exploded = true;
        IsLive = false;

        Explosions.Fire(World, Effects, Damage, Precache, Host, Blast, Decals,
                        Origin + BlastLift, BlastDamage, BlastRadius,
                        Entities.IsValid(Thrower) ? Thrower : Handle,
                        ignored: Handle, flags: BlastFlags, inflictor: Handle);

        // Stock's ExplosionCreate names the satchel its INFLICTOR and ignores
        // nothing, so the blast also reaches the satchel itself: one point of
        // health, CBaseGrenade::Event_Killed, Detonate. That second, grenade
        // blast is carried out here rather than left to a self-hit the blast
        // traversal is told to skip.
        GrenadeBlast();

        Remove();
    }

    /// <summary>
    /// Anything else that hurts it — a bullet, another satchel's blast — sets
    /// off the grenade blast alone (<c>CBaseGrenade::Event_Killed</c>), and it
    /// is gone: the 150-damage charge never goes off.
    /// </summary>
    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
    {
        if (_exploded || damage <= 0f)
            return 0;

        _exploded = true;
        IsLive = false;

        GrenadeBlast();
        Remove();

        return (int)MathF.Ceiling(damage);
    }

    /// <summary>
    /// <c>CBaseGrenade::Detonate</c> and <c>Explode</c> at the satchel's own
    /// numbers: <c>SetDamage( 150 )</c>, and a radius of
    /// <c>sk_satchel_radius</c>, which no shipped skill file sets, so zero —
    /// a blast that hurts nobody but is seen, heard (BaseGrenade.Explode),
    /// scorches the floor and shakes the screen.
    /// </summary>
    private void GrenadeBlast()
    {
        Vector spot = Origin + new Vector(0f, 0f, 8f);
        var tr = World.TraceLine(spot, spot + new Vector(0f, 0f, -32f), TraceMask.ShotHull, Handle);

        int sprite = Precache.PrecacheModel(EnvExplosion.DefaultFireballSprite);
        Effects.ExplosionEffect(Origin, sprite, GrenadeRadius * 0.03f, 25, 0, (int)GrenadeRadius, (int)BlastDamage);

        if (tr.Hit)
        {
            int scorch = Decals.IndexForName(EnvExplosion.ScorchDecal);
            if (scorch >= 0)
                Effects.PaintDecal(scorch, tr.End, angles: null, projectDistance: 0f, permanent: false, lowPriority: false);
        }

        Sound.EntityEmitSound(Handle, GrenadeExplodeSound);
        Screen.Shake(Origin, 25.0f, 150.0f, 1.0f, 750.0f, ShakeCommand.Start);
    }

    /// <summary><c>sk_satchel_radius</c>'s value: its default, which nothing overrides.</summary>
    private const float GrenadeRadius = 0f;

    public const string GrenadeExplodeSound = "BaseGrenade.Explode";

    /// <summary>
    /// <c>CreateEffects</c>: a strobing red glow on the satchel, additive, at
    /// a fifth of its size.
    /// </summary>
    private void CreateEffects()
    {
        if (_glow != EntityHandle.None)
            return;

        _glow = Entities.Create(EntityNames.EnvSprite);

        if (_glow == EntityHandle.None)
            return;

        Entities.SetKeyValue(_glow, "model", GlowSprite);
        Entities.SetKeyValue(_glow, "rendermode", ((int)RenderMode.TransAdd).ToString(CultureInfo.InvariantCulture));
        Entities.SetKeyValue(_glow, "renderfx", StrobeFast.ToString(CultureInfo.InvariantCulture));
        Entities.SetKeyValue(_glow, "rendercolor", "255 255 255");
        Entities.SetKeyValue(_glow, "renderamt", "255");
        Entities.SetKeyValue(_glow, "scale", "0.2");
        Entities.SetOrigin(_glow, Origin);
        Entities.Spawn(_glow);
        Entities.SetParent(_glow, Handle);
    }

    /// <summary>kRenderFxStrobeFast.</summary>
    private const int StrobeFast = 10;

    public override void OnRemoved()
    {
        IsLive = false;

        if (_glow != EntityHandle.None && Entities.IsValid(_glow))
            Entities.Remove(_glow);

        _glow = EntityHandle.None;
    }
}
