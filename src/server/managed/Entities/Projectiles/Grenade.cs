//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// npc_grenade_frag: the frag grenade, stock's CGrenadeFrag (grenade_frag.cpp) on
/// CBaseGrenade's detonation (basegrenade_shared.cpp).
///
/// <para>
/// A physics object players pass through (COLLISION_GROUP_WEAPON) that blips
/// as it spawns and every second after, every 0.3 s once its warning time has
/// come, and goes off when its timer runs out: a fireball temp entity turned
/// to the surface beneath it, the blast, a scorch, and BaseGrenade.Explode.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.NpcGrenadeFrag, ScriptedEntityKind.Prop)]
[Precache(PrecacheKind.Sound, BlipSound)]
[Precache(PrecacheKind.Sound, ExplodeSound)]
public sealed class Grenade : ScriptedEntity
{
    public Grenade(IEngineWorldQuery world, IEnginePrecache precache, IBlastSweep blast,
                   IServerDecals decals, IEngineEffects effects, ITempEnts temps, IEngineSound sound)
    {
        World = world;
        Precache = precache;
        Blast = blast;
        Decals = decals;
        Effects = effects;
        Temps = temps;
        Sound = sound;
    }

    /// <summary>tracing for the ground, and what is at a point.</summary>
    private readonly IEngineWorldQuery World;

    /// <summary>the fireball's sprites.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>RadiusDamage, managed. See <see cref="BlastSweep"/>.</summary>
    private readonly IBlastSweep Blast;

    /// <summary>the decal system, whose "Scorch" group the mark is picked from.</summary>
    private readonly IServerDecals Decals;

    /// <summary>painting the scorch.</summary>
    private readonly IEngineEffects Effects;

    /// <summary>the explosion temp entity, with the surface's normal and material.</summary>
    private readonly ITempEnts Temps;

    /// <summary>the blips and the bang.</summary>
    private readonly IEngineSound Sound;

    /// <summary>What a map may set on this entity. See <see cref="GrenadeVars"/>.</summary>
    [ScriptVars] public GrenadeVars Vars = new();

    private const string Model = "models/Weapons/w_grenade.mdl";

    internal const string BlipSound = "Grenade.Blip";
    internal const string ExplodeSound = "BaseGrenade.Explode";

    /// <summary>g_sModelIndexFireball.</summary>
    private const string Fireball = "sprites/zerogxplode.vmt";

    /// <summary>FRAG_GRENADE_BLIP_FREQUENCY and _FAST_FREQUENCY.</summary>
    internal const float BlipInterval = 1.0f, FastBlipInterval = 0.3f;

    /// <summary>FRAG_GRENADE_WARN_TIME: the blips quicken this long before it goes.</summary>
    internal const float WarnTime = 1.5f;

    /// <summary>DelayThink's period.</summary>
    private const float ThinkInterval = 0.1f;

    /// <summary>The grenade's te->Explosion frame rate.</summary>
    private const int FireballFrameRate = 25;

    /// <summary>TE_EXPLFLAG_NONE.</summary>
    private const int ExplosionFlagNone = 0;

    /// <summary>CreateEffects' glow and trail, on the "fuse" attachment.</summary>
    private const string GlowSprite = "sprites/redglow1.vmt", TrailSprite = "sprites/bluelaser1.vmt";

    private EntityHandle _thrower = EntityHandle.None;
    private EntityHandle _glow = EntityHandle.None;
    private EntityHandle _trail = EntityHandle.None;
    [SaveField] private float _detonateTime;
    [SaveField] private float _warnTime;
    [SaveField] private float _nextBlip;
    [SaveField] private bool _warned;
    private bool _exploded;

    public readonly Output OnExplode = new("OnExplode");

    public override void OnSpawn()
    {
        SetModel(Model);

        // Players pass through it and nothing stands on it. The kind makes its
        // physics object after this, as what it now is.
        SetCollisionGroup(CollisionGroup.Weapon);
        Entities.SetSolidity(Handle, SolidType.BoundingBox, SolidFlags.NotStandable);

        Sound.EntityEmitSound(Handle, BlipSound);
        _nextBlip = GameTime + BlipInterval;

        // A grenade a map places goes off on its own fuse.
        SetTimer(Vars.Fuse);
    }

    /// <summary>
    /// Fraggrenade_Create's SetThrower and SetTimer: who threw it, so the
    /// blast is theirs, and how long it has.
    /// </summary>
    public void ThrownBy(EntityHandle thrower, float fuse)
    {
        _thrower = thrower;
        Vars.Fuse = fuse;
        SetTimer(fuse);
    }

    /// <summary>SetTimer( detonateDelay, detonateDelay - FRAG_GRENADE_WARN_TIME ), and the effects.</summary>
    private void SetTimer(float fuse)
    {
        _detonateTime = GameTime + fuse;
        _warnTime = GameTime + fuse - WarnTime;
        SetNextThink(0f);
        CreateEffects();
    }

    /// <summary>
    /// CreateEffects: a red glow (kRenderGlow, alpha 200, no dissipation,
    /// scale 0.2, proxy 4) and a red trail (kRenderTransAdd, 8 wide to 1, half
    /// a second long), each following the grenade at its "fuse" attachment.
    /// Once each: a second timer keeps the ones it has.
    /// </summary>
    private void CreateEffects()
    {
        int fuse = Entities.LookupAttachmentAny(Handle, "fuse");

        if (_glow == EntityHandle.None || !Entities.IsValid(_glow))
            _glow = Follower(EntityNames.EnvSprite, fuse,
                             ("model", GlowSprite), ("scale", "0.2"), ("GlowProxySize", "4"),
                             ("rendermode", RenderGlow), ("renderfx", RenderFxNoDissipation),
                             ("rendercolor", "255 255 255"), ("renderamt", "200"));

        if (_trail == EntityHandle.None || !Entities.IsValid(_trail))
            _trail = Follower(EntityNames.EnvSpritetrail, fuse,
                              ("spritename", TrailSprite), ("rendermode", RenderTransAdd),
                              ("rendercolor", "255 0 0"), ("renderamt", "255"),
                              ("startwidth", "8"), ("endwidth", "1"), ("lifetime", "0.5"));
    }

    /// <summary>SpriteCreate / SpriteTrailCreate at the grenade, then FollowEntity and SetAttachment.</summary>
    private EntityHandle Follower(string className, int attachment, params (string Key, string Value)[] keys)
    {
        EntityHandle made = Entities.Create(className);
        if (!Entities.IsValid(made))
            return EntityHandle.None;

        foreach (var (key, value) in keys)
            Entities.SetKeyValue(made, key, value);

        Entities.SetOrigin(made, Origin);
        Entities.Spawn(made);
        Entities.SetParent(made, Handle, Math.Max(attachment, 0));
        SetNotSolid(made, true);
        return made;
    }

    /// <summary>kRenderGlow, kRenderTransAdd and kRenderFxNoDissipation.</summary>
    private const string RenderGlow = "3", RenderTransAdd = "5", RenderFxNoDissipation = "14";

    /// <summary>
    /// UpdateOnRemove's orphans: what followed the grenade goes with it.
    /// </summary>
    public override void OnRemoved()
    {
        if (_glow != EntityHandle.None && Entities.IsValid(_glow))
            Entities.Remove(_glow);
        if (_trail != EntityHandle.None && Entities.IsValid(_trail))
            Entities.Remove(_trail);
        _glow = _trail = EntityHandle.None;
    }

    /// <summary>DelayThink: go off when the time is up, and blip until then.</summary>
    public override void OnThink()
    {
        if (GameTime > _detonateTime)
        {
            Detonate();
            return;
        }

        if (!_warned && GameTime >= _warnTime)
            _warned = true;

        if (GameTime > _nextBlip)
        {
            Sound.EntityEmitSound(Handle, BlipSound);
            _nextBlip = GameTime + (_warned ? FastBlipInterval : BlipInterval);
        }

        SetNextThink(ThinkInterval);
    }

    /// <summary>
    /// CBaseGrenade::Detonate: the surface below, from eight units up to
    /// twenty-four down -- or from the grenade itself, when eight units up was
    /// inside something.
    /// </summary>
    private void Detonate()
    {
        Vector spot = Origin + new Vector(0f, 0f, 8f);
        SurfaceTraceResult tr = World.TraceLineSurface(spot, spot + new Vector(0f, 0f, -32f), TraceMask.ShotHull, Handle);

        if (World.PointContents(spot).HasFlag(Contents.Solid))
            tr = World.TraceLineSurface(Origin, Origin + new Vector(0f, 0f, -32f), TraceMask.ShotHull, Handle);

        Explode(in tr);
    }

    /// <summary>CBaseGrenade::Explode, DMG_BLAST.</summary>
    private void Explode(in SurfaceTraceResult tr)
    {
        if (_exploded)
            return;

        _exploded = true;
        OnExplode.Fire();

        // Pulled a little out of what it found.
        Vector at = Origin;
        if (tr.Fraction != 1f)
        {
            at = tr.End + tr.Normal * 0.6f;
            Entities.SetOrigin(Handle, at);
        }

        // g_sModelIndexWExplosion is precached only by HL1's W_Precache, so under
        // water an HL2MP grenade's explosion has model 0.
        int sprite = (World.PointContents(at) & Contents.MaskWater) == 0 ? Precache.PrecacheModel(Fireball) : 0;
        float scale = Vars.Radius * 0.03f;

        // te->Explosion, delay -1 so cl_interp does not hold it back, turned
        // to the surface and in its material when the trace found one.
        if (tr.Fraction != 1f)
        {
            SurfaceMaterial material = World.SurfacePropGameMaterial(tr.SurfaceProps);
            Temps.Explosion(TeFilters.Pas(at), -1f, at, sprite, scale, FireballFrameRate, ExplosionFlagNone,
                            (int)Vars.Radius, (int)Vars.Magnitude, tr.Normal,
                            material > 0 ? (byte)material : (byte)'C');
        }
        else
        {
            Temps.Explosion(TeFilters.Pas(at), -1f, at, sprite, scale, FireballFrameRate, ExplosionFlagNone,
                            (int)Vars.Radius, (int)Vars.Magnitude);
        }

        // RadiusDamage from the grenade, credited to its thrower (to itself
        // when nobody threw it). Passed through: stock has made it not solid.
        Blast.Apply(at, Vars.Magnitude, Vars.Radius, DamageType.Blast,
                    attacker: Entities.IsValid(_thrower) ? _thrower : Handle,
                    inflictor: Handle, ignored: Handle);

        // UTIL_DecalTrace( pTrace, "Scorch" ): where the trace landed, if it did.
        if (tr.Fraction != 1f)
        {
            int scorch = Decals.IndexForName(EnvExplosion.ScorchDecal);
            if (scorch >= 0)
                Effects.PaintDecal(scorch, tr.End, angles: null, projectDistance: 0f, permanent: false, lowPriority: false);
        }

        Sound.EntityEmitSound(Handle, ExplodeSound);
        Remove();
    }
}

/// <summary>
/// The map keys <see cref="Grenade"/> declares.
/// </summary>
[ScriptVars]
public partial class GrenadeVars
{
    /// <summary>Seconds before it goes off, from the moment it is thrown.</summary>
    public float Fuse { get; set; } = 3f;

    /// <summary>The frag's damage: weapon_frag.txt's "damage".</summary>
    public float Magnitude { get; set; } = 150f;

    /// <summary>GRENADE_DAMAGE_RADIUS.</summary>
    public float Radius { get; set; } = 250f;
}
