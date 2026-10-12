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
/// env_laser: a beam that hurts whatever crosses it.
///
/// The first entity here that OWNS a networked entity rather than being one. A
/// beam is drawn by a client renderer that stretches a scrolling sprite between
/// two points, and none of that is a decision — so this holds one and tells it
/// where its ends are.
///
/// <para>
/// The damage, by contrast, is entirely here: trace to the far end, hurt what
/// the trace found, at a rate this decides. That needed no new engine surface
/// at all, which is the useful part — the pieces were already on the managed
/// side and only the beam was missing.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvLaser)]
public sealed class EnvLaser : ScriptedEntity
{
    public EnvLaser(IGameClock clock, IEngineDamage damage, IEngineEffects effects, IEnginePrecache precache, IEngineWorldQuery world)
    {
        Clock = clock;
        Damage = damage;
        Effects = effects;
        Precache = precache;
        World = world;
    }

    /// <summary>what the server thinks the time is.</summary>
    private readonly IGameClock Clock;

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;

    /// <summary>the small effects.</summary>
    private readonly IEngineEffects Effects;

    /// <summary>asking for what this entity will need before the level runs.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>asking the world what is there.</summary>
    private readonly IEngineWorldQuery World;
    /// <summary>What a map may set on this entity. See <see cref="EnvLaserVars"/>.</summary>
    [ScriptVars] public EnvLaserVars Vars = new();

    private const int StartOnFlag = 1;

    /// <summary>
    /// Damage and sparks every tenth of a second rather than every tick.
    ///
    /// The interval is a rate limit, NOT the dose. What is applied is scaled by
    /// the time actually elapsed, so "damage" means damage per second however
    /// often this happens to fire. That is stock's rule — CBeam::BeamDamage
    /// multiplies by (curtime - lastFireTime) — and it is easy to get wrong: an
    /// earlier version of this class applied the whole number every interval
    /// and hurt ten times too hard, which measured as a player going from 100
    /// health to 1 in three seconds under a laser keyed for 20.
    /// </summary>
    private const float FireInterval = 0.1f;

    private EntityHandle _beam = EntityHandle.None;

    /// <summary>The beam's managed half, so its state can be written directly.</summary>
    private SsBeam? _managedBeam;
    private float _nextFire;

    /// <summary>When damage was last applied, so the next dose can be scaled.</summary>
    private float _lastFire;
    private bool _on;

    public override void OnSpawn()
    {
        if (Vars.Sprite.Length == 0)
        {
            // Stock removes itself here. This says so and stays, for the same
            // reason the other entities in this file do: an entity that deleted
            // itself at load is invisible to the debugger.
            Log.Warning($"[SourceSharp] env_laser '{Name}': no texture, nothing to draw\n");
            return;
        }

        int sprite = Precache.PrecacheModel(Vars.Sprite);

        if (sprite < 0)
        {
            Log.Warning($"[SourceSharp] env_laser '{Name}': sprite '{Vars.Sprite}' would not load\n");
            return;
        }

        // A managed beam now, not a CBeam. It is an ss_beam on the parallel
        // hierarchy that networks its own appearance and draws itself on the
        // client -- see BeamNetState.
        _beam = Entities.Create(EntityNames.SsBeam);

        if (!Entities.IsValid(_beam))
        {
            Log.Warning($"[SourceSharp] env_laser '{Name}': could not create {EntityNames.SsBeam}\n");
            return;
        }

        Entities.Spawn(_beam);

        _managedBeam = FindByHandle<SsBeam>(_beam);

        if (_managedBeam is null)
        {
            Log.Warning($"[SourceSharp] env_laser '{Name}': {EntityNames.SsBeam} has no managed half\n");
            return;
        }

        _managedBeam.SetLook(sprite, Vars.Width, Vars.Width, Vars.Noise, Vars.ScrollRate,
                             (byte)Vars.Colour.X, (byte)Vars.Colour.Y, (byte)Vars.Colour.Z, (byte)Vars.Brightness);

        // A named laser that was not told to start on begins off, which is
        // stock's rule: naming it means the map intends to switch it.
        if ((SpawnFlags & StartOnFlag) != 0 || Name.Length == 0)
            TurnOn();
        else
            TurnOff();
    }

    public override void OnRemoved()
    {
        // The beam is ours and nothing else will clean it up: it is a plain
        // entity the engine has no reason to associate with this one.
        if (Entities.IsValid(_beam))
            Entities.Remove(_beam);
    }

    public override void OnThink()
    {
        if (!_on)
            return;

        Vector from = Origin;
        Vector at = Aim(from);

        var hit = World.TraceLine(from, at, TraceMask.Shot, Handle);

        _managedBeam?.SetPoints(from, hit.End);

        if (Clock.Now >= _nextFire)
        {
            // Scaled by what actually elapsed, so the keyvalue means damage per
            // SECOND regardless of how often this fires. See FireInterval.
            float elapsed = Clock.Now - _lastFire;
            _lastFire = Clock.Now;
            _nextFire = Clock.Now + FireInterval;

            if (Entities.IsValid(hit.Entity))
                Damage.TakeDamage(hit.Entity, Handle, Vars.Damage * elapsed, DamageType.EnergyBeam);

            // Where it lands, not where it starts. A laser that sparked at its
            // own origin would look like a fault in the emitter rather than
            // something being burned at the far end.
            Effects.SparkEffect(hit.End, 2, 2, makeSound: true, direction: null);
        }

        SetNextThink(0f);
    }

    /// <summary>
    /// Where the beam points.
    ///
    /// Re-resolved every think rather than cached, which is stock's and is what
    /// lets a laser track something that moves. A target that does not exist
    /// leaves the beam pointing where its own angles say, so a map with a
    /// misspelt name still gets a laser rather than nothing.
    /// </summary>
    private Vector Aim(Vector from)
    {
        if (Vars.Target.Length > 0)
        {
            var target = Entities.FindByName(Vars.Target);

            if (Entities.IsValid(target))
                return Entities.GetOrigin(target);
        }

        return from + Angles.Forward(Entities.GetAngles(Handle)) * 4096f;
    }

    private void TurnOn()
    {
        _on = true;
        _nextFire = Clock.Now;

        // Reset with the beam, or a laser switched off for a minute deals that
        // whole minute's worth on the first tick after it comes back.
        _lastFire = Clock.Now;
        if (_managedBeam is not null) _managedBeam.Visible = true;
        SetNextThink(0f);
    }

    private void TurnOff()
    {
        _on = false;
        if (_managedBeam is not null) _managedBeam.Visible = false;
        StopThinking();
    }

    [ScriptedEvent("TurnOn")]
    private void OnTurnOn() { if (Entities.IsValid(_beam)) TurnOn(); }

    [ScriptedEvent("TurnOff")]
    private void OnTurnOff() { if (Entities.IsValid(_beam)) TurnOff(); }

    [ScriptedEvent("Toggle")]
    private void OnToggle()
    {
        if (!Entities.IsValid(_beam)) return;

        if (_on) TurnOff();
        else     TurnOn();
    }
}

/// <summary>
/// The map keys <see cref="EnvLaser"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvLaserVars
{
    /// <summary>Name of what to point at. Re-resolved every think, as stock does.</summary>
    [VarName("LaserTarget")]
    public string Target { get; set; } = string.Empty;

    /// <summary>The beam sprite. Without one there is nothing to draw and stock removes itself.</summary>
    [VarName("texture")]
    public string Sprite { get; set; } = string.Empty;

    public float Width { get; set; } = 2f;

    [VarName("NoiseAmplitude")]
    public float Noise { get; set; }

    [VarName("TextureScroll")]
    public int ScrollRate { get; set; } = 35;

    [VarName("renderamt")]
    public int Brightness { get; set; } = 100;

    [VarName("rendercolor")]
    public Vector Colour { get; set; } = new(255f, 255f, 255f);

    /// <summary>Damage per second to whatever is in the way.</summary>
    public float Damage { get; set; } = 100f;
}
