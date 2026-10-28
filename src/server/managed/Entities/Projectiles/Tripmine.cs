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
/// npc_tripmine: the SLAM's laser tripmine, planted on a wall. Stock's
/// <c>CTripmineGrenade</c> (grenade_tripmine.cpp): it powers up for two
/// seconds, draws its beam out along the wall's normal, and goes off a quarter
/// of a second after anything alive crosses the beam or the beam's length
/// changes — or when it is shot.
///
/// <para>
/// A physics prop with its motion off, in the weapon collision group, as
/// stock's (<c>VPhysicsInitNormal</c>, then <c>EnableMotion( false )</c>).
/// The beam is an <c>ss_beam</c>, this hierarchy's <c>CBeam</c>.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.NpcTripmine, ScriptedEntityKind.Prop)]
[Precache(PrecacheKind.Model, Tripmine.Model)]
[Precache(PrecacheKind.Model, Tripmine.BeamSprite)]
[Precache(PrecacheKind.Sound, Tripmine.PlaceSound)]
[Precache(PrecacheKind.Sound, Tripmine.ActivateSound)]
public sealed class Tripmine : ScriptedEntity
{
    public Tripmine(IEngineDamage damage, IEngineWorldQuery world, IEngineEffects effects,
                    IEnginePrecache precache, IEngineHost host, IBlastSweep blast, IServerDecals decals,
                    IEngineSound sound, IEngineEntityRender render, IScreenEffects screen)
    {
        Damage = damage;
        World = world;
        Effects = effects;
        Precache = precache;
        Host = host;
        Blast = blast;
        Decals = decals;
        Sound = sound;
        Render = render;
        Screen = screen;
    }

    private readonly IEngineDamage Damage;
    private readonly IEngineWorldQuery World;
    private readonly IEngineEffects Effects;
    private readonly IEnginePrecache Precache;
    private readonly IEngineHost Host;
    private readonly IBlastSweep Blast;
    private readonly IServerDecals Decals;
    private readonly IEngineSound Sound;
    private readonly IEngineEntityRender Render;
    private readonly IScreenEffects Screen;

    public const string Model = "models/Weapons/w_slam.mdl";

    /// <summary><c>g_pModelNameLaser</c>.</summary>
    public const string BeamSprite = "sprites/laserbeam.vmt";

    public const string PlaceSound = "TripmineGrenade.Place";
    public const string ActivateSound = "TripmineGrenade.Activate";

    /// <summary>How long after it is planted the beam comes on.</summary>
    public const float PowerUpDelay = 2.0f;

    /// <summary>How far the beam reaches.</summary>
    public const float BeamReach = 2048f;

    /// <summary><c>SetDamage( 200 )</c>, and ExplosionCreate's radius.</summary>
    public const float BlastDamage = 200f;
    public const float BlastRadius = 200f;

    /// <summary>The fraction change that counts as the beam being broken.</summary>
    public const float BreakTolerance = 0.001f;

    private enum Phase
    {
        PowerUp,
        Watching,
        Dying,
        Gone,
    }

    // Carried across a hot reload, as stock's datadesc carries them: a mine
    // rebuilt with none of these would have no beam to watch and no clock.
    [SaveField] private int _phaseValue;
    [SaveField] private float _powerUpAt;
    [SaveField] private Vector _dir;
    [SaveField] private Vector _end;
    [SaveField] private float _beamLength;
    [SaveField] private EntityHandle _beam = EntityHandle.None;
    private SsBeam? _managedBeam;

    private Phase _phase
    {
        get => (Phase)_phaseValue;
        set => _phaseValue = (int)value;
    }

    /// <summary><c>m_hOwner</c>: who planted it, and who its blast is scored to.</summary>
    [SaveField] public EntityHandle Planter { get; private set; } = EntityHandle.None;

    /// <summary><c>m_bIsLive</c>: the beam is on.</summary>
    [SaveField] public bool IsLive { get; private set; }

    /// <summary>Which way the beam runs: out of the wall it was planted on.</summary>
    public Vector BeamDirection => _dir;

    public override void OnSpawn()
    {
        SetModel(Model);
        SetCollisionGroup(CollisionGroup.Weapon);

        _powerUpAt = GameTime + PowerUpDelay;
        _phase = Phase.PowerUp;
        SetNextThink(0.2f);

        Sound.EntityEmitSound(Handle, PlaceSound);

        // It sits at 90 on the wall, so it is rotated back to find the beam.
        Vector angles = Entities.GetAngles(Handle);
        _dir = Angles.Forward(new Vector(angles.X - 90f, angles.Y, angles.Z));
        _end = Origin + _dir * BeamReach;
    }

    public override void OnPhysicsCreated() => EnableMotion(false);

    /// <summary>Planted by <paramref name="owner"/>.</summary>
    public void PlantedBy(EntityHandle owner) => Planter = owner;

    public override void OnThink()
    {
        switch (_phase)
        {
            case Phase.PowerUp:
                PowerUpThink();
                break;
            case Phase.Watching:
                BeamBreakThink();
                break;
            case Phase.Dying:
                DelayDeathThink();
                break;
        }
    }

    /// <summary>
    /// A tripmine takes the one point of health it has: anything that hurts it
    /// sets it off (<c>m_iHealth = 1</c>, <c>DAMAGE_YES</c>).
    /// </summary>
    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
    {
        if (_phase is Phase.Dying or Phase.Gone || damage <= 0f)
            return 0;

        Killed();
        return (int)MathF.Ceiling(damage);
    }

    private void PowerUpThink()
    {
        if (GameTime > _powerUpAt)
        {
            MakeBeam();
            IsLive = true;
            Sound.EntityEmitSound(Handle, ActivateSound);
        }

        // Stock sets this after MakeBeam's own one second, so the first watch
        // is a tenth of a second after the beam comes on.
        SetNextThink(0.1f);
    }

    private void MakeBeam()
    {
        Vector from = Origin;
        var tr = World.TraceLine(from, _end, TraceMask.Shot, Handle);
        _beamLength = tr.Fraction;
        float drawLength = tr.Fraction;

        // Something alive in front of it already: the beam is measured through
        // it, so it comes on and the next watch sees the break.
        if (tr.Hit && Entities.IsPlayer(tr.Entity))
            _beamLength = World.TraceLine(from, _end, TraceMask.Shot, tr.Entity).Fraction;

        _phase = Phase.Watching;

        int sprite = Precache.PrecacheModel(BeamSprite);
        _beam = Entities.Create(EntityNames.SsBeam);

        if (_beam == EntityHandle.None)
            return;

        Entities.Spawn(_beam);
        _managedBeam = FindByHandle<SsBeam>(_beam);

        if (_managedBeam is null)
            return;

        _managedBeam.SetLook(sprite, 0.35f, 0.35f, 0f, 25.6f, 255, 55, 52, 64);
        _managedBeam.SetPoints(BeamStart(from), from + _dir * BeamReach * drawLength);
    }

    /// <summary>The beam leaves from the model's beam_attach, as stock's end attachment does.</summary>
    private Vector BeamStart(Vector fallback)
    {
        int attachment = Render.LookupAttachment(Handle, "beam_attach");

        return attachment > 0 && Render.GetAttachment(Handle, attachment, out Vector at, out _) ? at : fallback;
    }

    private void BeamBreakThink()
    {
        // NOT MASK_SHOT: only simple hit boxes.
        var tr = World.TraceLine(Origin, _end, TraceMask.Solid, Handle);

        // A reload rebuilt this object and not the beam: find it again by
        // handle rather than make a second one.
        if (_managedBeam is null && _beam != EntityHandle.None)
            _managedBeam = FindByHandle<SsBeam>(_beam);

        // Stock's "respawn detect": no beam at all, so make one and re-measure.
        if (_beam == EntityHandle.None)
        {
            MakeBeam();

            if (tr.Entity != EntityHandle.None)
                Planter = tr.Entity;
        }

        if ((tr.Hit && Entities.IsPlayer(tr.Entity)) || MathF.Abs(_beamLength - tr.Fraction) > BreakTolerance)
        {
            Killed();
            return;
        }

        SetNextThink(0.05f);
    }

    /// <summary><c>Event_Killed</c>: a quarter of a second, then the blast.</summary>
    private void Killed()
    {
        _phase = Phase.Dying;
        SetNextThink(0.25f);
    }

    private void DelayDeathThink()
    {
        _phase = Phase.Gone;
        KillBeam();

        Screen.Shake(Origin, 25.0f, 150.0f, 1.0f, 750f, ShakeCommand.Start);

        Explosions.Fire(World, Effects, Damage, Precache, Host, Blast, Decals,
                        Origin + _dir * 8f, BlastDamage, BlastRadius,
                        Entities.IsValid(Planter) ? Planter : Handle,
                        ignored: Handle, flags: Satchel.BlastFlags, inflictor: Handle);

        Remove();
    }

    private void KillBeam()
    {
        if (_beam != EntityHandle.None && Entities.IsValid(_beam))
            Entities.Remove(_beam);

        _beam = EntityHandle.None;
        _managedBeam = null;
    }

    public override void OnRemoved()
    {
        IsLive = false;
        KillBeam();
    }
}
